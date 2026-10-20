using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Tactics.Grid;
using Killtime.Core.Combat;
using Killtime.Core.Character;
using Killtime.Core.Dice;
using Killtime.Core.Inventory;
using Killtime.UI;

namespace Killtime.Tactics.CombatUI
{
    /// <summary>
    /// Actions personnelles sur soi-même (Souffle, Overwatch, postures, tenir/retarder/canaliser, bouclier, dopants, munitions, armes) — extrait sans modification depuis CombatActionRegistry.cs.
    /// </summary>
    public static partial class CombatActionRegistry
    {
        private static void AddSelfActions(List<CombatAction> actions, TacticalUnit actor, TacticalUnit target, CombatDevArena arena, bool isSelf, bool isEnemy, bool targetIsAlive)
        {
            // Souffle : actions personnelles (Livres I §4.2 + VI §20.2).
            // Refresh PA = début de son propre tour uniquement (TurnManager).
            // Ici : les deux conversions Souffle <-> PA, jouables sur soi-même.
            if (isSelf)
            {
                if (TitanFootprint.IsTitan(actor.FootprintType))
                {
                    int zoneHexCount = TitanFootprint.GetHexCount(actor.FootprintType);
                    int estSelfStompDmg = Mathf.Max(4, actor.Stats.Attributes.Force + 2);

                    actions.Add(new CombatAction(
                        $"💥 Piétinement au Sol ({zoneHexCount} hex, 3 PA)",
                        $"Écrase le sol sous et autour du corps colosse ({zoneHexCount} cases) : {estSelfStompDmg} dégâts à tous les ennemis au contact direct, [À Terre] et [Déstabilisé].",
                        ActionCategory.AttaqueEtPassesDarmes,
                        3,
                        (act, tgt) => act.Stats.CurrentActionPoints >= 3,
                        (act, tgt) =>
                        {
                            if (!act.Stats.ConsumeActionPoints(3)) return;
                            var perimeter = TitanFootprint.GetPerimeterCoordinates(act.CurrentCoords, act.FootprintType);
                            var pSet = new HashSet<HexCoordinates>(perimeter);
                            var all = TacticalUnitRegistry.AllUnits;
                            int hitCount = 0;
                            int stompDmg = Mathf.Max(4, act.Stats.Attributes.Force + 2);

                            for (int i = 0; i < all.Count; i++)
                            {
                                var u = all[i];
                                if (u == null || u.Stats == null || !u.Stats.IsAlive || u == act) continue;

                                bool touchesPerimeter = false;
                                var uCoords = u.OccupiedCoords;
                                for (int c = 0; c < uCoords.Count; c++)
                                {
                                    if (pSet.Contains(uCoords[c]))
                                    {
                                        touchesPerimeter = true;
                                        break;
                                    }
                                }

                                if (u.IsPlayerControlled != act.IsPlayerControlled && touchesPerimeter)
                                {
                                    hitCount++;
                                    int rem = u.Stats.CurrentHealth - stompDmg;
                                    if (rem <= 0) u.Stats.EvaluateFatalBlow(BodyPart.Torse, stompDmg);
                                    else u.Stats.CurrentHealth = rem;

                                    u.Stats.ApplyStatus(StatusEffect.ATerre, 1);
                                    u.Stats.ApplyStatus(StatusEffect.Destabilise, 1);

                                    var uVis = u.GetComponent<TacticalUnitVisual>();
                                    uVis?.TriggerHitFlash();
                                    uVis?.SpawnFloatingText($"-{stompDmg} PIÉTINÉ [À TERRE]", Color.red);
                                    if (!u.Stats.IsAlive) uVis?.TriggerFallingBackDeath();
                                }
                            }

                            var actVis = act.GetComponent<TacticalUnitVisual>();
                            actVis?.SpawnFloatingText($"💥 IMPACT SOL ({hitCount} écrasé(s))", new Color(1f, 0.4f, 0.1f));
                            arena?.Log($"💥 <b>{act.Stats.Name}</b> piétine le sol autour de lui (-3 PA) : {hitCount} ennemi(s) écrasé(s) ({stompDmg} dégâts, À Terre) !");
                            arena?.RecordChronoSnapshot($"Piétinement Sol : {act.Stats.Name}");
                        }
                    ));
                }

                bool hasMarathonHeart = actor.Stats.HasSpecialization("Course d'Endurance : Cœur de Marathon");
                int breathPA = hasMarathonHeart ? 3 : 2;
                actions.Add(new CombatAction(
                    $"🫁 Souffle d'Urgence (+{breathPA} PA, +1 ESS)",
                    $"Prend 1 point d'essoufflement pour gagner {breathPA} PA immédiats.",
                    ActionCategory.TraumatologieEtSoins,
                    0,
                    (act, tgt) => act.Stats.Essoufflement < act.Stats.Attributes.Constitution,
                    (act, tgt) => act.Stats.TakeEmergencyBreath(act.Stats.HasSpecialization("Course d'Endurance : Cœur de Marathon") ? 3 : 2)
                ));
                bool hasSecondWind = actor.Stats.HasSpecialization("Course d'Endurance : Second Souffle");
                int recoverESS = hasSecondWind ? 2 : 1;
                actions.Add(new CombatAction(
                    $"🌬️ Reprendre son Souffle (1 PA → -{recoverESS} ESS)",
                    $"Début de son propre tour : dépense 1 PA pour effacer {recoverESS} point(s) d'essoufflement (répétable, max Constitution).",
                    ActionCategory.TraumatologieEtSoins,
                    1,
                    (act, tgt) => act.Stats.Essoufflement > 0 && act.Stats.CurrentActionPoints >= 1,
                    (act, tgt) =>
                    {
                        if (act.Stats.ConsumeActionPoints(1))
                        {
                            // Second Souffle (Athlétisme) : récupération doublée.
                            int recovered = act.Stats.HasSpecialization("Course d'Endurance : Second Souffle") ? 2 : 1;
                            act.Stats.RecoverBreath(recovered);
                            var vis = act.GetComponent<TacticalUnitVisual>();
                            vis?.SpawnFloatingText($"Souffle repris (-{recovered} ESS)", UnityEngine.Color.green);
                        }
                    }
                ));

                if (actor.Stats.HasSpecialization("Seconde Respiration"))
                {
                    actions.Add(new CombatAction(
                        "🌬️ Seconde Respiration (0 PA → -2 ESS, 1x/combat)",
                        "Ventilation cellulaire d'urgence : efface immédiatement 2 points d'essoufflement sans dépense de PA (1 fois par combat).",
                        ActionCategory.TraumatologieEtSoins,
                        0,
                        (act, tgt) => act.Stats.Essoufflement > 0 && !act.Stats.HasUsedSecondeRespiration,
                        (act, tgt) =>
                        {
                            act.Stats.HasUsedSecondeRespiration = true;
                            act.Stats.RecoverBreath(2);
                            var vis = act.GetComponent<TacticalUnitVisual>();
                            vis?.SpawnFloatingText("Seconde Respiration (-2 ESS)", UnityEngine.Color.cyan);
                            arena?.Log($"🌬️ <b>{act.Stats.Name}</b> déclenche sa <b>Seconde Respiration</b> (-2 ESS, 0 PA) !");
                        }
                    ));
                }

                actions.Add(new CombatAction(
                    "⚡ Poussée Cardiovasculaire (Redline : +1 PA, +1 ESS)",
                    "Dépasse les limites physiologiques pour forcer 1 PA au prix d'un sur-échauffement immédiat.",
                    ActionCategory.TraumatologieEtSoins,
                    0,
                    (act, tgt) => act.Stats.Essoufflement < act.Stats.Attributes.Constitution,
                    (act, tgt) =>
                    {
                        if (act.Stats.TriggerRedlineAP(1))
                        {
                            var vis = act.GetComponent<TacticalUnitVisual>();
                            vis?.SpawnFloatingText("⚡ REDLINE (+1 PA, +1 ESS)", UnityEngine.Color.red);
                            arena?.Log($"⚡ <b>{act.Stats.Name}</b> entre en <b>Poussée Cardiovasculaire (Redline)</b> (+1 PA, +1 ESS) !");
                        }
                    }
                ));

                // RD-030 Guet / Overwatch : réserve un tir de réaction (2 PA, arme à
                // distance). Duel aveugle auto pendant le tour adverse, 1 tir puis
                // consommé. Expire au prochain tour personnel, annulé si Étourdi/Paralysé.
                int overwatchCost = Killtime.Core.Rules.CoreRulesConfig.Instance.OverwatchAPCost;
                actions.Add(new CombatAction(
                    $"👁️ Guet / Overwatch ({overwatchCost} PA, tir réservé)",
                    "Réserve un tir de réaction dans le rayon de l'arme à distance : duel aveugle automatique (Ballistique vs Esquive) pendant le tour adverse. 1 tir puis consommé.",
                    ActionCategory.TactiqueEtOrdres,
                    overwatchCost,
                    (act, tgt) => act != null && act.Stats != null && act.Stats.IsAlive
                        && HasRangedWeaponEquipped(act)
                        && act.Stats.CurrentActionPoints >= Killtime.Core.Rules.CoreRulesConfig.Instance.OverwatchAPCost
                        && !OverwatchState.IsWatching(act.Stats)
                        && !OverwatchState.IsCancelledByStatus(act.Stats),
                    (act, tgt) =>
                    {
                        if (arena != null)
                        {
                            arena.TryEnterOverwatch(act, out _);
                        }
                        else
                        {
                            var w = act.Sheet?.GetEquippedWeapon();
                            int range = w != null ? Mathf.Max(1, w.RangeInTiles) : 1;
                            if (new CombatCalculator().TryEnterOverwatch(act.Stats, range, out _))
                            {
                                var vis = act.GetComponent<TacticalUnitVisual>();
                                vis?.SpawnFloatingText($"👁️ EN GUET ({range} cases)", Color.cyan);
                            }
                        }
                    }
                ));

                // RD-031 Opportunités (Livre V §20.4) : posture de réaction au contact
                // rompu (persistante, 0 PA, changeable à volonté pendant son tour) +
                // Décrochage (1 PA : prochain déplacement sans réaction adverse).
                // 1 réaction/round : Frappe gratuite (défaut), Poursuite 1 PA (suit
                // d'1 case), Blocage 1 PA (annule le pas, sans grapple), Balayage
                // gratuit (Jambes : À Terre + Ralenti si choc).
                var opportunityStances = new (OpportunityReactionType stance, string icon, string desc)[]
                {
                    (OpportunityReactionType.Strike, "🗡️", "Frappe gratuite de mêlée en duel aveugle auto quand un ennemi quitte votre contact. Base du hit-and-run."),
                    (OpportunityReactionType.Follow, "👣", "Suit le fuyard d'1 case (1 PA de réaction) au lieu de frapper. Sans attaque, sans grapple."),
                    (OpportunityReactionType.Block, "🛡️", "Retient le fuyard (duel Athlétisme, 1 PA) : succès = pas annulé, sans grapple permanent, PA du fuyard remboursés."),
                    (OpportunityReactionType.Trip, "🦵", "Balayage gratuit visant les Jambes : À Terre + Ralenti en cas de choc, au lieu de dégâts max."),
                };
                for (int si = 0; si < opportunityStances.Length; si++)
                {
                    var stanceChoice = opportunityStances[si].stance;
                    string stanceIcon = opportunityStances[si].icon;
                    string stanceDesc = opportunityStances[si].desc;
                    actions.Add(new CombatAction(
                        $"{stanceIcon} Posture : {OpportunityState.StanceLabel(stanceChoice)} (0 PA)",
                        stanceDesc + $" Posture actuelle : {OpportunityState.StanceLabel(OpportunityState.GetStance(actor.Stats))}.",
                        ActionCategory.TactiqueEtOrdres,
                        0,
                        (act, tgt) => act != null && act.Stats != null && act.Stats.IsAlive
                            && OpportunityState.GetStance(act.Stats) != stanceChoice,
                        (act, tgt) =>
                        {
                            OpportunityState.SetStance(act.Stats, stanceChoice);
                            act.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText($"{stanceIcon} {OpportunityState.StanceLabel(stanceChoice)}", Color.cyan);
                            arena?.Log($"🎯 <b>{act.Stats.Name}</b> adopte la posture d'opportunité <b>{OpportunityState.StanceLabel(stanceChoice)}</b> (réaction au contact rompu).");
                        }
                    ));
                }

                int disengageCost = Killtime.Core.Rules.CoreRulesConfig.Instance.DisengageAPCost;
                actions.Add(new CombatAction(
                    $"💨 Décrochage ({disengageCost} PA : prochain déplacement sans opportunité)",
                    "Prépare un retrait propre (Livre V §22.2) : le prochain déplacement ne provoque aucune frappe/poursuite/blocage/balayage. Expire au prochain tour personnel si inutilisé.",
                    ActionCategory.TactiqueEtOrdres,
                    disengageCost,
                    (act, tgt) => act != null && act.Stats != null && act.Stats.IsAlive
                        && !OpportunityState.HasSafeDisengage(act.Stats)
                        && !OpportunityState.IsExemptFromProvoking(act.Stats)
                        && act.Stats.CurrentActionPoints >= Killtime.Core.Rules.CoreRulesConfig.Instance.DisengageAPCost
                        && !OpportunityState.IsCancelledByStatus(act.Stats),
                    (act, tgt) =>
                    {
                        if (arena != null) arena.TryRequestSafeDisengage(act, out _);
                        else if (OpportunityState.RequestSafeDisengage(act.Stats, Killtime.Core.Rules.CoreRulesConfig.Instance.DisengageAPCost, out _))
                            act.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("💨 DÉCROCHAGE", Color.cyan);
                    }
                ));

                // RD-202 Dégagement (Livre V §20.3) : action principale (attaque OU
                // parade résolue) immédiatement suivie d'une fuite à +1 PA. La
                // surcharge se paie ici, le trajet ensuite au coût normal — et la
                // fuite peut provoquer (DISTINCT du Décrochage préventif ci-dessus).
                int getAwayCost = Killtime.Core.Rules.CoreRulesConfig.Instance.GetAwayExtraAPCost;
                actions.Add(new CombatAction(
                    $"🏃 Dégagement ({getAwayCost} PA : fuite après frappe/parade)",
                    "Après une attaque ou une parade résolue (Livre V §20.3) : paie la surcharge de fuite, puis déplacez-vous normalement. Les réactions d'opportunité adverses restent possibles.",
                    ActionCategory.TactiqueEtOrdres,
                    getAwayCost,
                    (act, tgt) => act != null && act.Stats != null && act.Stats.IsAlive
                        && GetAwayState.HasWindow(act.Stats)
                        && act.Stats.CurrentActionPoints >= Killtime.Core.Rules.CoreRulesConfig.Instance.GetAwayExtraAPCost
                        && !PreparationState.IsCancelledByStatus(act.Stats),
                    (act, tgt) =>
                    {
                        if (arena != null) arena.TryGetAwayMove(act, out _);
                        else if (new CombatCalculator().TryGetAwayMove(act.Stats, out _))
                            act.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("🏃 DÉGAGEMENT", Color.yellow);
                    }
                ));

                // RD-048 Retarder / Tenir / Canalisation (Livre V — Tours) :
                // Tenir (0 PA, en attente + interruption), Retarder (-2 init,
                // 1/round), Canalisation (1 PA, +2 au jet, -1 en défense,
                // interrompue si dégât net > encaissement).
                int holdCost = Killtime.Core.Rules.CoreRulesConfig.Instance.HoldAPCost;
                actions.Add(new CombatAction(
                    $"⏸️ Tenir l'action ({holdCost} PA : en attente + interruption)",
                    "Passe son tour en conservant ses PA et se déclare en attente : interviendra juste après l'unité active du moment (interruption), ou rejouera normalement au round suivant.",
                    ActionCategory.TactiqueEtOrdres,
                    holdCost,
                    (act, tgt) => act != null && act.Stats != null && act.Stats.IsAlive
                        && !DelayedTurnState.IsHolding(act.Stats)
                        && act.Stats.CurrentActionPoints >= Killtime.Core.Rules.CoreRulesConfig.Instance.HoldAPCost
                        && !DelayedTurnState.IsCancelledByStatus(act.Stats),
                    (act, tgt) =>
                    {
                        if (arena != null) arena.TryHoldAction(act, out _);
                        else if (new CombatCalculator().TryHoldAction(act.Stats, out _))
                            act.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("⏸️ EN ATTENTE", Color.cyan);
                    }
                ));

                int delayPenalty = Killtime.Core.Rules.CoreRulesConfig.Instance.DelayInitiativePenalty;
                actions.Add(new CombatAction(
                    $"⏳ Retarder (init -{delayPenalty}, 1/round)",
                    "Repousse son tour plus tard dans le round en cours : initiative réduite de la pénalité puis réinsertion après les unités qui devancent encore. Passe la main à l'unité suivante.",
                    ActionCategory.TactiqueEtOrdres,
                    0,
                    (act, tgt) => act != null && act.Stats != null && act.Stats.IsAlive
                        && !DelayedTurnState.HasDelayed(act.Stats)
                        && act.Stats.InitiativeRollTotal != int.MinValue
                        && !DelayedTurnState.IsCancelledByStatus(act.Stats),
                    (act, tgt) =>
                    {
                        if (arena != null) arena.TryDelayTurn(act, out _);
                        else if (new CombatCalculator().TryDelayInitiative(act.Stats, out _, out _))
                            act.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("⏳ RETARDÉ", Color.yellow);
                    }
                ));

                int channelCost = Killtime.Core.Rules.CoreRulesConfig.Instance.ChannelStartAPCost;
                int channelBonus = Killtime.Core.Rules.CoreRulesConfig.Instance.ChannelAttackBonus;
                int channelMalus = Killtime.Core.Rules.CoreRulesConfig.Instance.ChannelDefensePenalty;
                actions.Add(new CombatAction(
                    $"⏳ Canaliser le coup ({channelCost} PA : +{channelBonus} au jet)",
                    $"Étale l'effort (Livre V §20.3) : +{channelBonus} au jet de la prochaine attaque, {channelMalus} en défense tant qu'on canalise. Interrompue si dégât net > encaissement.",
                    ActionCategory.TactiqueEtOrdres,
                    channelCost,
                    (act, tgt) => act != null && act.Stats != null && act.Stats.IsAlive
                        && !ChannelingState.IsChanneling(act.Stats)
                        && act.Stats.CurrentActionPoints >= Killtime.Core.Rules.CoreRulesConfig.Instance.ChannelStartAPCost
                        && !ChannelingState.IsCancelledByStatus(act.Stats),
                    (act, tgt) =>
                    {
                        if (arena != null) arena.TryBeginChanneling(act, "Coup en progression", out _);
                        else if (new CombatCalculator().TryBeginChanneling(act.Stats, "Coup en progression", out _))
                            act.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("⏳ CANALISE", Color.magenta);
                    }
                ));

                // RD-202 Préparation d'Action (Livre V §20.3) : 1 PA -> +2 au jet
                // de l'action ou de la défense suivante (attaque OU parade).
                // One-shot, sans malus, sans persistance, sans interruption —
                // et sans cumul avec la canalisation (exclusion mutuelle).
                int prepCost = Killtime.Core.Rules.CoreRulesConfig.Instance.PreparationAPCost;
                int prepBonus = Killtime.Core.Rules.CoreRulesConfig.Instance.PreparationBonus;
                actions.Add(new CombatAction(
                    $"🎯 Préparer l'action ({prepCost} PA : +{prepBonus} au jet suivant)",
                    $"Se concentre (Livre V §20.3) : +{prepBonus} au jet de l'action ou de la parade suivante. One-shot consommée au jet suivant, ne cumule pas avec la canalisation.",
                    ActionCategory.TactiqueEtOrdres,
                    prepCost,
                    (act, tgt) => act != null && act.Stats != null && act.Stats.IsAlive
                        && !PreparationState.IsPrepared(act.Stats)
                        && !ChannelingState.IsChanneling(act.Stats)
                        && act.Stats.CurrentActionPoints >= Killtime.Core.Rules.CoreRulesConfig.Instance.PreparationAPCost
                        && !PreparationState.IsCancelledByStatus(act.Stats),
                    (act, tgt) =>
                    {
                        if (arena != null) arena.TryBeginPreparation(act, out _);
                        else if (new CombatCalculator().TryBeginPreparation(act.Stats, out _))
                            act.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("🎯 PRÉPARÉ", Color.green);
                    }
                ));

                // Cellules Nytharite : recharge tactique du champ de force personnel (1 PA).
                if (actor.Stats.MaxShieldHP > 0 && actor.Stats.CurrentShieldHP < actor.Stats.MaxShieldHP)
                {
                    if (HasTool(actor, "Cellule Nytharite Standard"))
                    {
                        actions.Add(new CombatAction(
                            "🔮 Cellule Nytharite (+10 Bouclier, 1 PA)",
                            "Consomme 1 Cellule Nytharite Standard pour restaurer 10 PV de barrière énergétique.",
                            ActionCategory.TraumatologieEtSoins,
                            1,
                            (act, tgt) => act.Stats.CurrentActionPoints >= 1 && act.Stats.CurrentShieldHP < act.Stats.MaxShieldHP && HasTool(act, "Cellule Nytharite Standard"),
                            (act, tgt) =>
                            {
                                if (arena != null)
                                {
                                    if (arena.TryRechargeShieldWithCell(act, false, out string msg)) arena.Log(msg);
                                    else arena.Log("⚠️ " + msg);
                                }
                            }
                        ));
                    }
                    if (HasTool(actor, "Cellule Nytharite Pure"))
                    {
                        actions.Add(new CombatAction(
                            "🔮 Cellule Pure (+25 Bouclier, 1 PA)",
                            "Consomme 1 Cellule Nytharite Pure pour restaurer 25 PV de barrière énergétique.",
                            ActionCategory.TraumatologieEtSoins,
                            1,
                            (act, tgt) => act.Stats.CurrentActionPoints >= 1 && act.Stats.CurrentShieldHP < act.Stats.MaxShieldHP && HasTool(act, "Cellule Nytharite Pure"),
                            (act, tgt) =>
                            {
                                if (arena != null)
                                {
                                    if (arena.TryRechargeShieldWithCell(act, true, out string msg)) arena.Log(msg);
                                    else arena.Log("⚠️ " + msg);
                                }
                            }
                        ));
                    }
                }

                // Boîte à Outils Arcanotech : maintenance de son propre bouclier (1 PA).
                if (actor.Stats.MaxShieldHP > 0 && actor.Stats.CurrentShieldHP < actor.Stats.MaxShieldHP && HasTool(actor, "Boîte à Outils"))
                {
                    actions.Add(new CombatAction(
                        "🔧 Boîte à Outils : Réparer Bouclier (1 PA)",
                        "Répare le circuit du champ de force (+10 Bouclier, outil réutilisable).",
                        ActionCategory.TraumatologieEtSoins,
                        1,
                        (act, tgt) => act.Stats.CurrentActionPoints >= 1 && act.Stats.CurrentShieldHP < act.Stats.MaxShieldHP && HasTool(act, "Boîte à Outils"),
                        (act, tgt) =>
                        {
                            if (!act.Stats.ConsumeActionPoints(1)) return;
                            int before = act.Stats.CurrentShieldHP;
                            act.Stats.CurrentShieldHP = Mathf.Min(act.Stats.MaxShieldHP, act.Stats.CurrentShieldHP + 10);
                            int gained = act.Stats.CurrentShieldHP - before;
                            var vis = act.GetComponent<TacticalUnitVisual>();
                            vis?.SpawnFloatingText($"🔧 +{gained} Bouclier", Color.cyan);
                            arena?.Log($"🔧 <b>{act.Stats.Name}</b> répare son champ à la Boîte à Outils (+{gained} Bouclier, -1 PA).");
                        }
                    ));
                }

                // Moteur Arcanique de Poche : injection d'énergie de secours (1 PA, non consommé).
                if (actor.Stats.MaxShieldHP > 0 && actor.Stats.CurrentShieldHP < actor.Stats.MaxShieldHP && HasTool(actor, "Moteur"))
                {
                    actions.Add(new CombatAction(
                        "⚙️ Moteur Arcanique (+10 Bouclier, 1 PA)",
                        "Force une suralimentation du champ de force via le générateur portatif (10 PV barrière, non consommé).",
                        ActionCategory.TraumatologieEtSoins,
                        1,
                        (act, tgt) => act.Stats.CurrentActionPoints >= 1 && act.Stats.CurrentShieldHP < act.Stats.MaxShieldHP && HasTool(act, "Moteur"),
                        (act, tgt) =>
                        {
                            if (!act.Stats.ConsumeActionPoints(1)) return;
                            int before = act.Stats.CurrentShieldHP;
                            act.Stats.CurrentShieldHP = Mathf.Min(act.Stats.MaxShieldHP, act.Stats.CurrentShieldHP + 10);
                            int gained = act.Stats.CurrentShieldHP - before;
                            var vis = act.GetComponent<TacticalUnitVisual>();
                            vis?.SpawnFloatingText($"⚙️ +{gained} Bouclier", Color.cyan);
                            arena?.Log($"⚙️ <b>{act.Stats.Name}</b> déclenche son <b>Moteur Arcanique de Poche</b> (+{gained} Bouclier, -1 PA).");
                        }
                    ));
                }

                // Résonateur Nytharite (focus) : harmonisation psi offensive (1 PA).
                if (HasTool(actor, "Résonateur"))
                {
                    actions.Add(new CombatAction(
                        "🔮 Harmonisation Psi (1 PA)",
                        "Canalise le Résonateur Nytharite : confère l'état Survolté (+1 EC) pour 1 tour.",
                        ActionCategory.CinquiemeForceEtSorts,
                        1,
                        (act, tgt) => act.Stats.CurrentActionPoints >= 1 && !act.Stats.ActiveStatus.HasFlag(StatusEffect.Survolte) && HasTool(act, "Résonateur"),
                        (act, tgt) =>
                        {
                            if (!act.Stats.ConsumeActionPoints(1)) return;
                            act.Stats.ApplyStatus(StatusEffect.Survolte, 1);
                            var vis = act.GetComponent<TacticalUnitVisual>();
                            vis?.SpawnFloatingText("🔮 SURVOLTÉ (+1 EC)", Color.magenta);
                            arena?.Log($"🔮 <b>{act.Stats.Name}</b> s'harmonise avec son <b>Résonateur Nytharite</b> (+1 EC, Survolté).");
                        }
                    ));
                }

                // Dopants marketplace Livre VII §28.3 : Antidouleurs + Speed, 1 PA, soi-même.
                InventoryItem antalgique = FindConsumableByName(actor, "Antidouleur");
                if (antalgique != null)
                {
                    actions.Add(new CombatAction(
                        $"💊 {antalgique.Name} (+2 Encaissement, 1 PA)",
                        "Dopage 2h : +2 Encaissement pour le combat, dissipe Étourdi/Déstabilisé. Consomme 1 dose. Continue au-delà des limites.",
                        ActionCategory.TraumatologieEtSoins,
                        1,
                        (act, tgt) => act.Stats.CurrentActionPoints >= 1 && FindConsumableByName(act, "Antidouleur") != null,
                        (act, tgt) =>
                        {
                            var dose = FindConsumableByName(act, "Antidouleur");
                            if (dose == null) return;
                            if (!act.Stats.ConsumeActionPoints(1)) return;
                            act.Sheet?.ConsumeOne(dose.ItemId);
                            act.NotifyInventoryChanged(true);
                            act.Stats.AddEncaissementBonus(2);
                            act.Stats.RemoveStatus(StatusEffect.Etourdi);
                            act.Stats.RemoveStatus(StatusEffect.Destabilise);
                            var vis = act.GetComponent<TacticalUnitVisual>();
                            vis?.SpawnFloatingText("💊 Dopé (+2 Encaissement)", new Color(1f, 0.6f, 0.2f));
                            arena?.Log($"💊 <b>{act.Stats.Name}</b> prend <b>{dose.Name}</b> (+2 Encaissement, douleurs ignorées).");
                        }
                    ));
                }
                InventoryItem speed = FindConsumableByName(actor, "Speed");
                if (speed != null)
                {
                    actions.Add(new CombatAction(
                        $"💨 {speed.Name} (+3 PA, 1 PA)",
                        "Stimulant : +3 PA immédiats + état Rapide affiché. Consomme 1 dose.",
                        ActionCategory.TraumatologieEtSoins,
                        1,
                        (act, tgt) => act.Stats.CurrentActionPoints >= 1 && FindConsumableByName(act, "Speed") != null,
                        (act, tgt) =>
                        {
                            var dose = FindConsumableByName(act, "Speed");
                            if (dose == null) return;
                            if (!act.Stats.ConsumeActionPoints(1)) return;
                            act.Sheet?.ConsumeOne(dose.ItemId);
                            act.NotifyInventoryChanged(true);
                            act.Stats.CurrentActionPoints += 3;
                            act.Stats.ApplyStatus(StatusEffect.Rapide, 3);
                            var vis = act.GetComponent<TacticalUnitVisual>();
                            vis?.SpawnFloatingText("💨 SPEED (+3 PA)", Color.yellow);
                            arena?.Log($"💨 <b>{act.Stats.Name}</b> prend <b>{dose.Name}</b> (+3 PA, Rapide).");
                        }
                    ));
                }

                // RD-033 Munitions : recharger le chargeur + désenrayer (gratuit avec Kit d'Entretien).
                InventoryItem magWeapon = actor.Sheet?.GetEquippedWeapon();
                if (magWeapon != null && magWeapon.AmmoCapacity > 0)
                {
                    if (magWeapon.Jammed)
                    {
                        bool hasMaintenanceKit = HasTool(actor, "Kit d'Entretien");
                        int clearCost = hasMaintenanceKit ? 0 : 1;
                        actions.Add(new CombatAction(
                            hasMaintenanceKit ? "🔧 Désenrayer (0 PA, Kit d'Entretien)" : "🔧 Désenrayer (1 PA)",
                            $"Remet {magWeapon.Name} en batterie après un enrayement sur critique adverse.{(hasMaintenanceKit ? " Kit d'Entretien : 0 PA !" : "")}",
                            ActionCategory.TactiqueEtOrdres,
                            clearCost,
                            (act, tgt) =>
                            {
                                var w = act.Sheet?.GetEquippedWeapon();
                                int reqCost = HasTool(act, "Kit d'Entretien") ? 0 : 1;
                                return w != null && w.Jammed && act.Stats.CurrentActionPoints >= reqCost;
                            },
                            (act, tgt) =>
                            {
                                if (arena.TryClearJam(act, out string msg)) arena?.Log(msg);
                                else arena?.Log("⚠️ " + msg);
                            }
                        ));
                    }
                    else if (magWeapon.AmmoRemaining < magWeapon.AmmoCapacity)
                    {
                        int rcost = Mathf.Max(1, magWeapon.ReloadAPCost);
                        int stdStock = WeaponAmmo.StockShots(actor.Sheet, magWeapon.AmmoType, false);
                        int hdStock = magWeapon.HeavyAmmo ? WeaponAmmo.StockShots(actor.Sheet, magWeapon.AmmoType, true) : 0;
                        if (stdStock > 0)
                        {
                            actions.Add(new CombatAction(
                                $"🔋 Recharger ({rcost} PA) — {magWeapon.AmmoRemaining}/{magWeapon.AmmoCapacity}, réserve {stdStock}",
                                $"Recharge {magWeapon.Name} en charges standard (éjecte le reste HD éventuel).",
                                ActionCategory.TactiqueEtOrdres,
                                rcost,
                                (act, tgt) =>
                                {
                                    var w = act.Sheet?.GetEquippedWeapon();
                                    return w != null && !w.Jammed && w.AmmoCapacity > 0
                                        && w.AmmoRemaining < w.AmmoCapacity
                                        && WeaponAmmo.StockShots(act.Sheet, w.AmmoType, false) > 0
                                        && act.Stats.CurrentActionPoints >= Mathf.Max(1, w.ReloadAPCost);
                                },
                                (act, tgt) =>
                                {
                                    if (arena.TryReloadWeapon(act, false, out string msg)) arena?.Log(msg);
                                    else arena?.Log("⚠️ " + msg);
                                }
                            ));
                        }
                        if (hdStock > 0)
                        {
                            actions.Add(new CombatAction(
                                $"🔋 Recharger HD +1D ({rcost} PA) — {magWeapon.AmmoRemaining}/{magWeapon.AmmoCapacity}, réserve {hdStock}",
                                $"Recharge {magWeapon.Name} en Haute Densité : +1 dégât tant que chambrée (sniper/Deglazer).",
                                ActionCategory.TactiqueEtOrdres,
                                rcost,
                                (act, tgt) =>
                                {
                                    var w = act.Sheet?.GetEquippedWeapon();
                                    return w != null && !w.Jammed && w.HeavyAmmo && w.AmmoCapacity > 0
                                        && w.AmmoRemaining < w.AmmoCapacity
                                        && WeaponAmmo.StockShots(act.Sheet, w.AmmoType, true) > 0
                                        && act.Stats.CurrentActionPoints >= Mathf.Max(1, w.ReloadAPCost);
                                },
                                (act, tgt) =>
                                {
                                    if (arena.TryReloadWeapon(act, true, out string msg)) arena?.Log(msg);
                                    else arena?.Log("⚠️ " + msg);
                                }
                            ));
                        }
                    }
                }

                // Manips d'armes Livre V : ramasser au sol (1 PA combat / gratuit explo) + swap (1 PA combat).
                int pickupPa = Killtime.Tactics.Units.DroppedWeaponPickup.PickupCostPA();
                actions.Add(new CombatAction(
                    pickupPa > 0 ? "⚔ Ramasser arme au sol (1 PA)" : "⚔ Ramasser arme au sol",
                    "Ramasse la plus proche arme au sol à portée. 1 PA en combat, gratuit en exploration.",
                    ActionCategory.TactiqueEtOrdres,
                    pickupPa,
                    (act, tgt) => act.Stats.CurrentActionPoints >= Killtime.Tactics.Units.DroppedWeaponPickup.PickupCostPA(),
                    (act, tgt) =>
                    {
                        // Client VTT : le GM résout (autorité), on transmet le dropId visé.
                        if (TurnManager.IsMultiplayerPlayerClient())
                        {
                            if (Killtime.Tactics.Units.DroppedWeaponPickup.TryGetNearest(act, out var nearest) && nearest != null)
                            {
                                Killtime.Multi.VTTTableSync.Instance?.RequestPickup(act, nearest.DropId ?? "");
                                arena?.Log($"📡 Demande de ramassage envoyée au GM (<b>{act.Stats.Name}</b>).");
                            }
                            else
                            {
                                arena?.Log($"⚠️ <b>{act.Stats.Name}</b> : aucune arme au sol à portée.");
                            }
                            return;
                        }
                        bool ok = Killtime.Tactics.Units.DroppedWeaponPickup.TryPickupNearest(act, out string msg);
                        arena?.Log((ok ? "⚔ " : "⚠️ ") + $"<b>{act.Stats.Name}</b> : {msg}");
                    }
                ));
                if (actor.Sheet?.Inventory != null)
                {
                    int swapPa = Killtime.Tactics.Units.DroppedWeaponPickup.PickupCostPA();
                    for (int i = 0; i < actor.Sheet.Inventory.Count; i++)
                    {
                        var sw = actor.Sheet.Inventory[i];
                        if (sw == null || sw.Type != ItemType.Weapon || sw.IsEquipped) continue;
                        if (sw.IsThrowableGrenade() || sw.IsLauncher) continue;
                        string swapName = sw.Name;
                        string swapId = sw.ItemId;
                        actions.Add(new CombatAction(
                            swapPa > 0 ? $"🔄 Équiper : {swapName} (1 PA)" : $"🔄 Équiper : {swapName}",
                            $"Swap d'arme : range l'arme en main et équipe {swapName}. Coût 1 PA en combat.",
                            ActionCategory.TactiqueEtOrdres,
                            swapPa,
                            (act, tgt) =>
                            {
                                if (act.Stats.CurrentActionPoints < Killtime.Tactics.Units.DroppedWeaponPickup.PickupCostPA()) return false;
                                var cand = act.Sheet?.Inventory?.Find(x => x != null && x.ItemId == swapId);
                                return cand != null && !cand.IsEquipped;
                            },
                            (act, tgt) =>
                            {
                                int cost = Killtime.Tactics.Units.DroppedWeaponPickup.PickupCostPA();
                                if (cost > 0 && !act.Stats.ConsumeActionPoints(cost)) return;
                                if (act.Sheet.EquipItem(swapId))
                                {
                                    act.NotifyInventoryChanged(true);
                                    var vis = act.GetComponent<TacticalUnitVisual>();
                                    vis?.SpawnFloatingText(cost > 0 ? $"🔄 {swapName} équipée (-1 PA)" : $"🔄 {swapName} équipée", Color.cyan);
                                    arena?.Log(cost > 0 ? $"🔄 <b>{act.Stats.Name}</b> équipe <b>{swapName}</b> (-1 PA)." : $"🔄 <b>{act.Stats.Name}</b> équipe <b>{swapName}</b>.");
                                }
                            }
                        ));
                    }
                }
            }
        }
    }
}

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
    /// §1 Attaque & passes d'armes (Livre V) : frappes, bouclier, charge, assaut groupé, poursuite, grenades, lancer, balise, grappin, menottes, lutte RD-039, Titan RD-052 — extrait sans modification depuis CombatActionRegistry.cs.
    /// </summary>
    public static partial class CombatActionRegistry
    {
        private static void AddAttackActions(List<CombatAction> actions, TacticalUnit actor, TacticalUnit target, CombatDevArena arena, bool isSelf, bool isEnemy, bool targetIsAlive)
        {
            // =========================================================================
            // 1. ATTAQUE & PASSES D'ARMES (LIVRE VI)
            // =========================================================================
            if (!isSelf && isEnemy && targetIsAlive)
            {
                // Frappe Standard
                actions.Add(new CombatAction(
                    $"🎯 Attaque Standard (2 PA){AmmoTag(actor, target)}",
                    "Frappe générale sur le centre de masse (Torse).",
                    ActionCategory.AttaqueEtPassesDarmes,
                    2,
                    (act, tgt) => !IsEntraveVsTarget(act, tgt) && CanAttackTarget(act, tgt, 2) && CanFireShot(act, tgt),
                    (act, tgt) => arena.ExecuteAttack(BodyPart.Torse, cancelPenaltyWithAP: false,
                        attackSkill: ResolveContactSkill(act, tgt), attackerBonusAP: CombatContextMenuUI.CurrentInjectedAP, attackerPE: CombatContextMenuUI.CurrentInjectedPE, explicitTarget: tgt, explicitAttacker: act)
                ));

                // Visée Chirurgicale Tête
                actions.Add(new CombatAction(
                    $"💀 Visée : Tête (3 PA){AmmoTag(actor, target)}",
                    "Tir chirurgical avec dépense préalable de +1 PA pour annuler le malus de -2.",
                    ActionCategory.AttaqueEtPassesDarmes,
                    3,
                    (act, tgt) => !IsEntraveVsTarget(act, tgt) && CanAttackTarget(act, tgt, 3) && CanFireShot(act, tgt),
                    (act, tgt) => arena.ExecuteAttack(BodyPart.Tete, cancelPenaltyWithAP: true,
                        attackSkill: ResolveContactSkill(act, tgt), attackerBonusAP: CombatContextMenuUI.CurrentInjectedAP, attackerPE: CombatContextMenuUI.CurrentInjectedPE, explicitTarget: tgt, explicitAttacker: act)
                ));

                // Désarmement (Bras Droit) : fait tomber l'arme au sol sur gros différentiel.
                actions.Add(new CombatAction(
                    $"🗡️ Visée : Bras Droit (2 PA){AmmoTag(actor, target)}",
                    "Frappe le bras porteur : désarme et fait tomber l'arme au sol si Diff ≥ 4 (≥ 3 Croc, ≥ 2 Fleuret).",
                    ActionCategory.AttaqueEtPassesDarmes,
                    2,
                    (act, tgt) => !IsEntraveVsTarget(act, tgt) && CanAttackTarget(act, tgt, 2) && CanFireShot(act, tgt),
                    (act, tgt) => arena.ExecuteAttack(BodyPart.BrasDroit, cancelPenaltyWithAP: false,
                        attackSkill: ResolveContactSkill(act, tgt), attackerBonusAP: CombatContextMenuUI.CurrentInjectedAP, attackerPE: CombatContextMenuUI.CurrentInjectedPE, explicitTarget: tgt, explicitAttacker: act)
                ));

                // Faucher (Jambes)
                actions.Add(new CombatAction(
                    $"🦵 Visée : Jambes (2 PA){AmmoTag(actor, target)}",
                    "Impact sur les membres inférieurs pour infliger l'état À Terre et Ralenti.",
                    ActionCategory.AttaqueEtPassesDarmes,
                    2,
                    (act, tgt) => !IsEntraveVsTarget(act, tgt) && CanAttackTarget(act, tgt, 2) && CanFireShot(act, tgt),
                    (act, tgt) => arena.ExecuteAttack(BodyPart.Jambes, cancelPenaltyWithAP: false,
                        attackSkill: ResolveContactSkill(act, tgt), attackerBonusAP: CombatContextMenuUI.CurrentInjectedAP, attackerPE: CombatContextMenuUI.CurrentInjectedPE, explicitTarget: tgt, explicitAttacker: act)
                ));

                // RD-201 : CANON ENTRAVÉ (Livre V §22.2) — au contact d'un
                // conscient avec une arme longue, les passes génériques
                // ci-dessus sont remplacées par les 4 options du Livre :
                // rétention, visé, crosse, mains nues.
                if (IsEntraveVsTarget(actor, target))
                {
                    SkillType entraveShotSkill = ResolveContactSkill(actor, target);
                    bool canShootEntrave = entraveShotSkill == SkillType.Ballistique
                        || entraveShotSkill == SkillType.ProjectilesTir;
                    if (canShootEntrave)
                    {
                        // 1. Tir de rétention (2 PA, -2 conservé).
                        actions.Add(new CombatAction(
                            $"🔫 Tir de Rétention (2 PA, -2){AmmoTag(actor, target)}",
                            "Canon entravé (Livre V §22.2) : tir précipité au contact, malus de -2 conservé.",
                            ActionCategory.AttaqueEtPassesDarmes,
                            2,
                            (act, tgt) => CanAttackTarget(act, tgt, 2) && CanFireShot(act, tgt),
                            (act, tgt) => arena.ExecuteAttack(BodyPart.Torse, cancelPenaltyWithAP: false,
                                attackSkill: ResolveContactSkill(act, tgt), attackerBonusAP: CombatContextMenuUI.CurrentInjectedAP, attackerPE: CombatContextMenuUI.CurrentInjectedPE, explicitTarget: tgt, explicitAttacker: act)
                        ));

                        // 2. Tir visé (3 PA : +1 PA annule le malus, membres autorisés).
                        AddEntraveAimedShot(actions, actor, target, arena, BodyPart.Tete, "💀 Tir Visé : Tête (3 PA)",
                            "Canon entravé compensé (+1 PA) : visée chirurgicale autorisée, malus annulé.");
                        AddEntraveAimedShot(actions, actor, target, arena, BodyPart.BrasDroit, "🎯 Tir Visé : Bras Droit (3 PA)",
                            "Canon entravé compensé (+1 PA) : désarme si Diff ≥ 4 (≥ 3 Croc, ≥ 2 Fleuret).");
                        AddEntraveAimedShot(actions, actor, target, arena, BodyPart.Jambes, "🎯 Tir Visé : Jambes (3 PA)",
                            "Canon entravé compensé (+1 PA) : À Terre et Ralenti sur choc.");
                    }

                    // 3. Crosse (2 PA, Maniement contondant 3-4 bruts, Déstabilisé si > CON x2).
                    int crosseDmg = CanonEntraveRules.ButtstrokeBaseDamage(actor.Sheet?.GetEquippedWeapon());
                    actions.Add(new CombatAction(
                        $"🔨 Crosse ({crosseDmg} bruts, 2 PA)",
                        "Canon entravé (Livre V §22.2) : coup de crosse au Maniement (3-4 bruts, sans munition). Déstabilisé si les bruts dépassent CON x2.",
                        ActionCategory.AttaqueEtPassesDarmes,
                        2,
                        (act, tgt) => act.Stats.CurrentActionPoints >= 2 && targetIsAlive && GetDistance(act, tgt) <= 1,
                        (act, tgt) => arena.ExecuteAttack(BodyPart.Torse, cancelPenaltyWithAP: false,
                            attackSkill: SkillType.ManiementArmes,
                            weaponBaseDamage: CanonEntraveRules.ButtstrokeBaseDamage(act.Sheet?.GetEquippedWeapon()),
                            attackerBonusAP: CombatContextMenuUI.CurrentInjectedAP, attackerPE: CombatContextMenuUI.CurrentInjectedPE,
                            explicitTarget: tgt, explicitAttacker: act, isButtstroke: true)
                    ));

                    // 4. Mains Nues (2 PA, exemption totale, Refoulement sur choc / Delta ≥ 4).
                    actions.Add(new CombatAction(
                        "🥋 Mains Nues (2 PA, sans entrave)",
                        "Canon entravé (Livre V §22.2) : frappe à mains nues exempte du malus. Refoulement (1 case) sur choc traumatique ou Delta ≥ 4.",
                        ActionCategory.AttaqueEtPassesDarmes,
                        2,
                        (act, tgt) => act.Stats.CurrentActionPoints >= 2 && targetIsAlive && GetDistance(act, tgt) <= 1,
                        (act, tgt) => arena.ExecuteAttack(BodyPart.Torse, cancelPenaltyWithAP: false,
                            attackSkill: SkillType.MainsNues,
                            weaponBaseDamage: CanonEntraveRules.UnarmedBaseDamage,
                            attackerBonusAP: CombatContextMenuUI.CurrentInjectedAP, attackerPE: CombatContextMenuUI.CurrentInjectedPE,
                            explicitTarget: tgt, explicitAttacker: act)
                    ));
                }

                // RD-040 : Bousculade générique & Coup de bouclier (2 PA, contact)
                bool hasShield = HasEquippedShield(actor);
                if (hasShield)
                {
                    actions.Add(new CombatAction(
                        "🛡️ Coup de Bouclier (2 PA)",
                        "Percussion au bouclier (test FOR opposé, 2 PA) : repousse la cible d'1 case (Knockback) + dégâts contondants légers. Chute À Terre si mur ou obstacle !",
                        ActionCategory.AttaqueEtPassesDarmes,
                        2,
                        (act, tgt) => act.Stats.CurrentActionPoints >= 2 && targetIsAlive && act.CurrentCoords.DistanceTo(tgt.CurrentCoords) <= 1,
                        (act, tgt) => arena.ExecuteShove(act, tgt, true, CombatContextMenuUI.CurrentInjectedAP, CombatContextMenuUI.CurrentInjectedPE)
                    ));
                }

                actions.Add(new CombatAction(
                    "💨 Bousculade (2 PA)",
                    "Poussée physique au corps-à-corps (test FOR opposé, 2 PA) : repousse la cible d'1 case (Knockback). Chute À Terre si mur ou obstacle !",
                    ActionCategory.AttaqueEtPassesDarmes,
                    2,
                    (act, tgt) => act.Stats.CurrentActionPoints >= 2 && targetIsAlive && act.CurrentCoords.DistanceTo(tgt.CurrentCoords) <= 1,
                    (act, tgt) => arena.ExecuteShove(act, tgt, false, CombatContextMenuUI.CurrentInjectedAP, CombatContextMenuUI.CurrentInjectedPE)
                ));

                // RD-038 : Charge au contact (3+ cases) + frappe d'assaut
                int chargeAtkCost = 2;
                bool isChargeDistance = actor.CurrentCoords.DistanceTo(target.CurrentCoords) >= 3;
                bool canChargeTarget = arena != null && arena.Pathfinder != null
                    && arena.Pathfinder.CanChargeTarget(actor, target, 3, chargeAtkCost)
                    && !GrappleState.IsGrappled(actor.Stats);

                if (canChargeTarget || isChargeDistance)
                {
                    int minChargeAP = 3 + chargeAtkCost;
                    actions.Add(new CombatAction(
                        $"⚡ Charge au contact ({minChargeAP}+ PA)",
                        "Manœuvre d'assaut (RD-038, Livre V) : déplacement d'au moins 3 cases vers un flanc libre de la cible immédiatement suivi d'une frappe au contact (+2 dégâts d'impact, -1 défense jusqu'au prochain tour personnel, tir bloqué ce tour).",
                        ActionCategory.AttaqueEtPassesDarmes,
                        minChargeAP,
                        (act, tgt) => arena != null && arena.Pathfinder != null && arena.Pathfinder.CanChargeTarget(act, tgt, 3, chargeAtkCost) && !GrappleState.IsGrappled(act.Stats),
                        (act, tgt) => arena.ExecuteChargeAttack(act, tgt, BodyPart.Torse, CombatContextMenuUI.CurrentInjectedAP, CombatContextMenuUI.CurrentInjectedPE)
                    ));
                }

                // RD-084 : Assaut groupé (Livre V §21.2) — synchronisé sur le plus
                // lent, résultats offensifs sommés face à l'unique défense (armure 1×).
                {
                    int combinedCost = Killtime.Core.Rules.CoreRulesConfig.Instance != null ? Killtime.Core.Rules.CoreRulesConfig.Instance.CombinedAttackAPCost : 2;
                    int combinedMin = Killtime.Core.Rules.CoreRulesConfig.Instance != null ? Killtime.Core.Rules.CoreRulesConfig.Instance.CombinedAttackMinMembers : 2;
                    actions.Add(new CombatAction(
                        $"⚔️ Assaut Groupé ({combinedCost} PA chacun)",
                        "Attaque combinée (Livre V §21.2) : les alliés à portée frappent à l'unisson sur l'initiative du membre le plus lent, résultats offensifs sommés face à l'unique défense de la cible — armure et encaissement pulvérisés par le dépassement massif.",
                        ActionCategory.AttaqueEtPassesDarmes,
                        combinedCost,
                        (act, tgt) =>
                        {
                            if (act.Stats.CurrentActionPoints < combinedCost || !targetIsAlive) return false;
                            if (arena == null) return false;
                            if (GrappleState.IsGrappled(act.Stats)) return false;
                            if (!arena.TryResolveCombinedSkill(act, tgt, out var sk)) return false;
                            var members = arena.CollectCombinedAttackers(act, tgt, sk);
                            return members.Count >= combinedMin && members.Contains(act);
                        },
                        (act, tgt) => arena.ExecuteCombinedAttack(act, tgt, BodyPart.Torse)
                    ));
                }

                // RD-084 : Course-poursuite (Livre V §21.1) — concours d'Athlétisme
                // proie vs poursuivant ; 4 faillites cumulées = capture immédiate.
                {
                    int pursuitCost = Killtime.Core.Rules.CoreRulesConfig.Instance != null ? Killtime.Core.Rules.CoreRulesConfig.Instance.PursuitAPCost : 1;
                    actions.Add(new CombatAction(
                        $"🏃 Poursuite : gain de terrain ({pursuitCost} PA)",
                        "Course-poursuite (Livre V §21.1) : concours d'Athlétisme contre la cible. Réussite de la proie = distance maintenue (≥ 1 case) ; faillite = le poursuivant gagne du terrain. 4 faillites cumulées = proie rattrapée et acculée (arrêt immédiat).",
                        ActionCategory.TactiqueEtOrdres,
                        pursuitCost,
                        (act, tgt) => act.Stats.CurrentActionPoints >= pursuitCost && targetIsAlive
                            && !PursuitState.IsCaught(tgt.Stats)
                            && GetDistance(act, tgt) >= 1,
                        (act, tgt) => arena.ExecutePursuitRound(tgt, new List<TacticalUnit> { act })
                    ));
                }

                // --- Grenades : lancer sur la case de la cible si à portée ---
                InventoryItem firstGrenade = null;
                InventoryItem anyLauncher = null;
                if (actor.Sheet != null && actor.Sheet.Inventory != null)
                {
                    for (int i = 0; i < actor.Sheet.Inventory.Count; i++)
                    {
                        var it = actor.Sheet.Inventory[i];
                        if (it == null) continue;
                        if (firstGrenade == null && it.IsThrowableGrenade()) firstGrenade = it;
                        if (anyLauncher == null && it.IsLauncher) anyLauncher = it;
                    }
                }
                if (firstGrenade != null)
                {
                    var gCap = firstGrenade;
                    int handMaxRange = GrenadeRules.ComputeMaxRange(gCap, null);
                    actions.Add(new CombatAction(
                        $"💣 Grenade : {gCap.Name} (2 PA, main)",
                        $"Souffle R{gCap.BlastRadius} : {gCap.BaseDamage}+{gCap.DamageDiceCount}d10 + shrap {gCap.ShrapnelDamage}. Vise la case de la cible.",
                        ActionCategory.AttaqueEtPassesDarmes,
                        2,
                        (act, tgt) => act.Stats.CurrentActionPoints >= 2 && targetIsAlive && act.CurrentCoords.DistanceTo(tgt.CurrentCoords) <= handMaxRange,
                        (act, tgt) => arena.ExecuteGrenadeThrow(tgt.CurrentCoords, gCap.ItemId, false, false, 0)
                    ));
                    if (anyLauncher != null)
                    {
                        var lCap = anyLauncher;
                        var gCap2 = firstGrenade;
                        int launcherMaxRange = GrenadeRules.ComputeMaxRange(gCap2, lCap);
                        int launcherCost = Mathf.Max(1, 3 + lCap.ApCostModifier);
                        actions.Add(new CombatAction(
                            $"💣 Lance-grenades : {gCap2.Name} via {lCap.Name} ({launcherCost} PA)",
                            $"Portée {launcherMaxRange} cases, dispersion réduite. Vise la case de la cible.",
                            ActionCategory.AttaqueEtPassesDarmes,
                            launcherCost,
                            (act, tgt) => act.Stats.CurrentActionPoints >= launcherCost && targetIsAlive && act.CurrentCoords.DistanceTo(tgt.CurrentCoords) <= launcherMaxRange,
                            (act, tgt) => arena.ExecuteGrenadeThrow(tgt.CurrentCoords, gCap2.ItemId, true, false, 0)
                        ));
                    }
                }

                // Lancer d'arme équipée (2 PA) : 2-4 cases, duel Ballistique, l'arme tombe à la case cible.
                InventoryItem equippedForThrow = actor.Sheet?.GetEquippedWeapon();
                if (equippedForThrow != null && !equippedForThrow.IsThrowableGrenade() && !equippedForThrow.IsLauncher)
                {
                    string throwName = equippedForThrow.Name;
                    int throwDmg = equippedForThrow.BaseDamage;
                    actions.Add(new CombatAction(
                        $"🗡️ Lancer : {throwName} (2 PA, 2-4 cases)",
                        $"Projette l'arme ({throwDmg}D + diff) en Ballistique. L'arme quitte la main et tombe au sol, même manquée.",
                        ActionCategory.AttaqueEtPassesDarmes,
                        2,
                        (act, tgt) =>
                        {
                            if (act.Stats.CurrentActionPoints < 2 || !targetIsAlive) return false;
                            var w = act.Sheet?.GetEquippedWeapon();
                            if (w == null || w.IsThrowableGrenade() || w.IsLauncher) return false;
                            int d = act.CurrentCoords.DistanceTo(tgt.CurrentCoords);
                            return d >= 2 && d <= 4;
                        },
                        (act, tgt) => arena.ExecuteWeaponThrow(act, tgt)
                    ));
                }

                // Balise laser (Lampe + Batterie) : 1 PA, ≤12 cases, cible visible.
                // Expose la faille (prochaine attaque ignore l'encaissement).
                if (HasTool(actor, "Lampe") && HasTool(actor, "Batterie"))
                {
                    actions.Add(new CombatAction(
                        "🔦 Balise Laser (1 PA, 12 cases)",
                        "Désigne la cible (visible, 12 cases) : expose sa faille, la prochaine attaque ignore son encaissement.",
                        ActionCategory.TactiqueEtOrdres,
                        1,
                        (act, tgt) => act.Stats.CurrentActionPoints >= 1 && targetIsAlive
                            && act.CurrentCoords.DistanceTo(tgt.CurrentCoords) <= 12
                            && HasTool(act, "Lampe") && HasTool(act, "Batterie")
                            && (arena == null || arena.GetCoverToTarget(act, tgt) != CoverType.Full),
                        (act, tgt) =>
                        {
                            if (!act.Stats.ConsumeActionPoints(1)) return;
                            Killtime.Core.Combat.SkillTechniqueState.ApplyExposedFlaw(act.Stats, tgt.Stats);
                            var vis = tgt.GetComponent<TacticalUnitVisual>();
                            vis?.SpawnFloatingText("🔦 DÉSIGNÉ", Color.yellow);
                            arena?.Log($"🔦 <b>{act.Stats.Name}</b> désigne <b>{tgt.Stats.Name}</b> à la balise (faille exposée).");
                            arena?.RecordChronoSnapshot($"Balise : {act.Stats.Name} -> {tgt.Stats.Name}");
                        }
                    ));
                }

                // Grappin Magnétique (2 PA, 2-4 cases) : harpon 3D, À Terre si Diff ≥ 2.
                if (HasTool(actor, "Grappin"))
                {
                    actions.Add(new CombatAction(
                        "🪝 Grappin : Harpon (2 PA, 2-4 cases)",
                        "Projette le grappin (3D Ballistique) : À Terre si différentiel ≥ 2. Le grappin revient.",
                        ActionCategory.AttaqueEtPassesDarmes,
                        2,
                        (act, tgt) =>
                        {
                            if (act.Stats.CurrentActionPoints < 2 || !targetIsAlive) return false;
                            if (!HasTool(act, "Grappin")) return false;
                            int d = act.CurrentCoords.DistanceTo(tgt.CurrentCoords);
                            return d >= 2 && d <= 4;
                        },
                        (act, tgt) => arena.ExecuteGrapnel(act, tgt)
                    ));
                }

                // Menottes Magnétiques (2 PA, contact, cible affaiblie) : Immobilise 2 tours, consommées.
                InventoryItem cuffs = FindTool(actor, "Menottes");
                if (cuffs != null)
                {
                    actions.Add(new CombatAction(
                        "🔗 Menotter (2 PA, contact)",
                        "Entrave une cible affaiblie (À Terre, Déstabilisée, Sonnée, Étourdie, Paralysée) : Immobilise 2 tours. Menottes consommées.",
                        ActionCategory.TactiqueEtOrdres,
                        2,
                        (act, tgt) => act.Stats.CurrentActionPoints >= 2 && targetIsAlive
                            && act.CurrentCoords.DistanceTo(tgt.CurrentCoords) <= 1
                            && HasWeakenedStatus(tgt.Stats) && HasTool(act, "Menottes"),
                        (act, tgt) =>
                        {
                            if (!act.Stats.ConsumeActionPoints(2)) return;
                            if (!ConsumeTool(act, "Menottes")) return;
                            act.NotifyInventoryChanged(true);
                            tgt.Stats.ApplyStatus(StatusEffect.Immobilise, 2);
                            var vis = tgt.GetComponent<TacticalUnitVisual>();
                            vis?.SpawnFloatingText("🔗 MENOTTÉ (2 tours)", Color.yellow);
                            arena?.Log($"🔗 <b>{act.Stats.Name}</b> menotte <b>{tgt.Stats.Name}</b> (Immobilise 2 tours).");
                        }
                    ));
                }

                // Corde : Ligoter (1 PA, contact, ennemi À Terre) → Immobilise 1 tour, réutilisable.
                if (HasTool(actor, "Corde") && target.Stats.ActiveStatus.HasFlag(StatusEffect.ATerre))
                {
                    actions.Add(new CombatAction(
                        "🪢 Ligoter (1 PA, contact)",
                        "Ligue un ennemi à terre : Immobilise 1 tour. La corde est réutilisable.",
                        ActionCategory.TactiqueEtOrdres,
                        1,
                        (act, tgt) => act.Stats.CurrentActionPoints >= 1 && targetIsAlive
                            && act.CurrentCoords.DistanceTo(tgt.CurrentCoords) <= 1
                            && tgt.Stats.ActiveStatus.HasFlag(StatusEffect.ATerre) && HasTool(act, "Corde"),
                        (act, tgt) =>
                        {
                            if (!act.Stats.ConsumeActionPoints(1)) return;
                            tgt.Stats.ApplyStatus(StatusEffect.Immobilise, 1);
                            var vis = tgt.GetComponent<TacticalUnitVisual>();
                            vis?.SpawnFloatingText("🪢 LIGOTÉ", Color.yellow);
                            arena?.Log($"🪢 <b>{act.Stats.Name}</b> ligote <b>{tgt.Stats.Name}</b> (Immobilise 1 tour).");
                        }
                    ));
                }

                // =============================================================
                // RD-039 : LUTTE, GRAPPLE, ÉTRANGLEMENT & TRAÎNÉE DE CORPS
                // =============================================================
                bool isGrappledByMe = Killtime.Core.Combat.GrappleState.IsGrappling(actor.Stats, target.Stats);
                bool isGrappledByTarget = Killtime.Core.Combat.GrappleState.IsGrappling(target.Stats, actor.Stats);

                if (isGrappledByTarget)
                {
                    actions.Add(new CombatAction(
                        "🔓 Lutte : Se Libérer de la Prise (2 PA)",
                        "Duel opposé aveugle Athlétisme/MainsNues/Acrobatie vs Athlétisme pour briser la prise et dissiper l'état Immobilisé.",
                        ActionCategory.AttaqueEtPassesDarmes,
                        2,
                        (act, tgt) => act.Stats.CurrentActionPoints >= 2 && Killtime.Core.Combat.GrappleState.IsGrappling(tgt.Stats, act.Stats),
                        (act, tgt) => arena.ExecuteGrappleEscape(act, tgt)
                    ));
                }
                else if (isGrappledByMe)
                {
                    actions.Add(new CombatAction(
                        "🥋 Lutte : Relâcher la Prise (0 PA)",
                        $"Relâche l'étreinte sur {target.Stats.Name} (dissipe son état Immobilisé de lutte).",
                        ActionCategory.AttaqueEtPassesDarmes,
                        0,
                        (act, tgt) => Killtime.Core.Combat.GrappleState.IsGrappling(act.Stats, tgt.Stats),
                        (act, tgt) =>
                        {
                            Killtime.Core.Combat.GrappleState.ReleaseGrapple(act.Stats);
                            act.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("Prise relâchée", Color.gray);
                            arena?.Log($"🥋 <b>{act.Stats.Name}</b> relâche sa prise de lutte sur <b>{tgt.Stats.Name}</b>.");
                        }
                    ));

                    actions.Add(new CombatAction(
                        "🫁 Étranglement (2 PA, contact)",
                        "Suffocation au corps-à-corps (Athlétisme vs Endurance) : dégâts bruts au cou + statut Asphyxie (2 tours, drain 1 PA/tour, pool 2 dégâts continus).",
                        ActionCategory.AttaqueEtPassesDarmes,
                        2,
                        (act, tgt) => act.Stats.CurrentActionPoints >= 2 && targetIsAlive && act.CurrentCoords.DistanceTo(tgt.CurrentCoords) <= 1,
                        (act, tgt) => arena.ExecuteStrangulation(act, tgt)
                    ));

                    actions.Add(new CombatAction(
                        "🤼 Traîner le corps (2 PA, 1 case)",
                        "Recule ou pivote d'1 case et tire la cible saisie dans la case libérée (coût 2 PA pour le grappler).",
                        ActionCategory.AttaqueEtPassesDarmes,
                        2,
                        (act, tgt) => act.Stats.CurrentActionPoints >= 2 && act.CurrentCoords.DistanceTo(tgt.CurrentCoords) <= 1 && (arena == null || arena.CanDragBody(act, tgt)),
                        (act, tgt) => arena.ExecuteDragBody(act, tgt)
                    ));
                }
                else
                {
                    actions.Add(new CombatAction(
                        "🥋 Lutte : Prise au Corps-à-Corps (2 PA)",
                        "Duel opposé aveugle Athlétisme vs Athlétisme : saisit et applique l'état Immobilisé 1 tour. Permet ensuite la traînée (1 case / 2 PA) et l'étranglement (Asphyxie).",
                        ActionCategory.AttaqueEtPassesDarmes,
                        2,
                        (act, tgt) => act.Stats.CurrentActionPoints >= 2 && targetIsAlive
                            && GetDistance(act, tgt) <= 1
                            && TitanFootprint.GetHexCount(act.FootprintType) >= TitanFootprint.GetHexCount(tgt.FootprintType)
                            && !act.Stats.ActiveStatus.HasFlag(StatusEffect.Immobilise),
                        (act, tgt) => arena.ExecuteGrapple(act, tgt)
                    ));

                    if (target.Stats.ActiveStatus.HasFlag(StatusEffect.Immobilise))
                    {
                        actions.Add(new CombatAction(
                            "🫁 Étranglement (2 PA, contact)",
                            "Suffocation au corps-à-corps (Athlétisme vs Endurance) sur cible immobilisée : dégâts bruts au cou + statut Asphyxie (2 tours, drain 1 PA/tour).",
                            ActionCategory.AttaqueEtPassesDarmes,
                            2,
                            (act, tgt) => act.Stats.CurrentActionPoints >= 2 && targetIsAlive && GetDistance(act, tgt) <= 1,
                            (act, tgt) => arena.ExecuteStrangulation(act, tgt)
                        ));

                        actions.Add(new CombatAction(
                            "🤼 Traîner le corps (2 PA, 1 case)",
                            "Déplace le corps d'une cible immobilisée d'1 case (coût 2 PA).",
                            ActionCategory.AttaqueEtPassesDarmes,
                            2,
                            (act, tgt) => act.Stats.CurrentActionPoints >= 2 && GetDistance(act, tgt) <= 1 && (arena == null || arena.CanDragBody(act, tgt)),
                            (act, tgt) => arena.ExecuteDragBody(act, tgt)
                        ));
                    }
                }

                // =============================================================
                // ACTIONS TITANESQUES (RD-052 : ROSETTE 7 / COLOSSUS 19)
                // =============================================================
                if (TitanFootprint.IsTitan(actor.FootprintType))
                {
                    int zoneHexCount = TitanFootprint.GetHexCount(actor.FootprintType);
                    int estGrabDmg = Mathf.Max(3, actor.Stats.Attributes.Force);
                    int estStompDmg = Mathf.Max(4, actor.Stats.Attributes.Force + 2);
                    int estBreathDmg = Mathf.Max(5, (actor.Stats.Attributes.Magie > 0 ? actor.Stats.Attributes.Magie : actor.Stats.Attributes.Constitution) + 2);

                    // 1. Saisie Colossale (Grab)
                    actions.Add(new CombatAction(
                        "✊ Saisie Titanesque (Grab) (2 PA)",
                        $"Saisit une créature plus petite au contact du corps colosse : {estGrabDmg} dégâts de broyage immédiats + Immobilisé en lutte active.",
                        ActionCategory.AttaqueEtPassesDarmes,
                        2,
                        (act, tgt) => act.Stats.CurrentActionPoints >= 2 && targetIsAlive
                            && TitanFootprint.CanTitanGrab(act.FootprintType, tgt.FootprintType, GetDistance(act, tgt))
                            && !act.Stats.ActiveStatus.HasFlag(StatusEffect.Immobilise),
                        (act, tgt) =>
                        {
                            if (!act.Stats.ConsumeActionPoints(2)) return;
                            int grabDmg = Mathf.Max(3, act.Stats.Attributes.Force);
                            int rem = tgt.Stats.CurrentHealth - grabDmg;
                            if (rem <= 0) tgt.Stats.EvaluateFatalBlow(BodyPart.Torse, grabDmg);
                            else tgt.Stats.CurrentHealth = rem;

                            var tgtVis = tgt.GetComponent<TacticalUnitVisual>();
                            tgtVis?.TriggerHitFlash();

                            if (tgt.Stats.IsAlive)
                            {
                                Killtime.Core.Combat.GrappleState.SetGrapple(act.Stats, tgt.Stats);
                                tgt.Stats.ApplyStatus(StatusEffect.Destabilise, 1);
                                tgtVis?.SpawnFloatingText($"-{grabDmg} SAISIE [IMMOBILISÉ]", Color.yellow);
                            }
                            else
                            {
                                tgtVis?.TriggerFallingBackDeath();
                                arena?.AutoTargetNextAlive();
                            }

                            var actVis = act.GetComponent<TacticalUnitVisual>();
                            actVis?.SpawnFloatingText("✊ SAISIE TITANESQUE", Color.cyan);

                            arena?.Log($"✊ <b>{act.Stats.Name}</b> saisit <b>{tgt.Stats.Name}</b> d'une poigne colossale (-2 PA) : {grabDmg} dégâts de broyage !");
                            arena?.RecordChronoSnapshot($"Saisie Titanesque : {act.Stats.Name} -> {tgt.Stats.Name}");
                        }
                    ));

                    // 2. Piétinement de Zone (Stomp 7/19 hex)
                    actions.Add(new CombatAction(
                        $"💥 Piétinement de Zone ({zoneHexCount} hex, 3 PA)",
                        $"Écrase la zone ciblée ({zoneHexCount} cases, gabarit {actor.FootprintType}) : {estStompDmg} dégâts de choc à toutes les unités dans la zone, chute [À Terre] et [Déstabilisé].",
                        ActionCategory.AttaqueEtPassesDarmes,
                        3,
                        (act, tgt) => act.Stats.CurrentActionPoints >= 3 && targetIsAlive
                            && GetDistance(act, tgt) <= 2,
                        (act, tgt) =>
                        {
                            if (!act.Stats.ConsumeActionPoints(3)) return;
                            var zone = TitanFootprint.GetStompZone(tgt.CurrentCoords, act.FootprintType);
                            var zoneSet = new HashSet<HexCoordinates>(zone);
                            var all = TacticalUnitRegistry.AllUnits;
                            int hitCount = 0;
                            int stompDmg = Mathf.Max(4, act.Stats.Attributes.Force + 2);

                            for (int i = 0; i < all.Count; i++)
                            {
                                var u = all[i];
                                if (u == null || u.Stats == null || !u.Stats.IsAlive || u == act) continue;

                                bool inZone = false;
                                var uCoords = u.OccupiedCoords;
                                for (int c = 0; c < uCoords.Count; c++)
                                {
                                    if (zoneSet.Contains(uCoords[c]))
                                    {
                                        inZone = true;
                                        break;
                                    }
                                }

                                if (inZone)
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
                                    if (!u.Stats.IsAlive)
                                    {
                                        uVis?.TriggerFallingBackDeath();
                                        if (u == tgt) arena?.AutoTargetNextAlive();
                                    }
                                }
                            }

                            var actVis = act.GetComponent<TacticalUnitVisual>();
                            actVis?.SpawnFloatingText($"💥 PIÉTINEMENT ({hitCount} touché(s))", new Color(1f, 0.4f, 0.1f));
                            arena?.Log($"💥 <b>{act.Stats.Name}</b> déclenche un <b>Piétinement de Zone ({zone.Count} hex)</b> sur {tgt.CurrentCoords} (-3 PA) : {hitCount} unité(s) écrasée(s) ({stompDmg} dégâts, À Terre + Déstabilisé) !");
                            arena?.RecordChronoSnapshot($"Piétinement : {act.Stats.Name} ({zone.Count} hex)");
                        }
                    ));

                    // 3. Souffle Titanesque (Breath 7/19 hex)
                    actions.Add(new CombatAction(
                        $"🔥 Souffle Titanesque ({zoneHexCount} hex, 3 PA)",
                        $"Projette un torrent destructeur couvrant une zone de {zoneHexCount} cases (gabarit {actor.FootprintType}, portée 8) : {estBreathDmg} dégâts élémentaires/arcaniques à toutes les unités, [En Feu] et [Déstabilisé].",
                        ActionCategory.AttaqueEtPassesDarmes,
                        3,
                        (act, tgt) => act.Stats.CurrentActionPoints >= 3 && targetIsAlive
                            && GetDistance(act, tgt) <= 8,
                        (act, tgt) =>
                        {
                            if (!act.Stats.ConsumeActionPoints(3)) return;
                            var zone = TitanFootprint.GetBreathZone(tgt.CurrentCoords, act.FootprintType);
                            var zoneSet = new HashSet<HexCoordinates>(zone);
                            var all = TacticalUnitRegistry.AllUnits;
                            int hitCount = 0;
                            int breathDmg = Mathf.Max(5, (act.Stats.Attributes.Magie > 0 ? act.Stats.Attributes.Magie : act.Stats.Attributes.Constitution) + 2);

                            for (int i = 0; i < all.Count; i++)
                            {
                                var u = all[i];
                                if (u == null || u.Stats == null || !u.Stats.IsAlive || u == act) continue;

                                bool inZone = false;
                                var uCoords = u.OccupiedCoords;
                                for (int c = 0; c < uCoords.Count; c++)
                                {
                                    if (zoneSet.Contains(uCoords[c]))
                                    {
                                        inZone = true;
                                        break;
                                    }
                                }

                                if (inZone)
                                {
                                    hitCount++;
                                    int rem = u.Stats.CurrentHealth - breathDmg;
                                    if (rem <= 0) u.Stats.EvaluateFatalBlow(BodyPart.Torse, breathDmg);
                                    else u.Stats.CurrentHealth = rem;

                                    u.Stats.ApplyStatus(StatusEffect.EnFeu, 2);
                                    u.Stats.ApplyStatus(StatusEffect.Destabilise, 1);

                                    var uVis = u.GetComponent<TacticalUnitVisual>();
                                    uVis?.TriggerHitFlash();
                                    uVis?.SpawnFloatingText($"-{breathDmg} SOUFFLE [EN FEU]", new Color(1f, 0.45f, 0.1f));
                                    if (!u.Stats.IsAlive)
                                    {
                                        uVis?.TriggerFallingBackDeath();
                                        if (u == tgt) arena?.AutoTargetNextAlive();
                                    }
                                }
                            }

                            var actVis = act.GetComponent<TacticalUnitVisual>();
                            actVis?.SpawnFloatingText($"🔥 SOUFFLE ({hitCount} touché(s))", new Color(1f, 0.5f, 0.1f));
                            arena?.Log($"🔥 <b>{act.Stats.Name}</b> crache un <b>Souffle Titanesque ({zone.Count} hex)</b> sur {tgt.CurrentCoords} (-3 PA) : {hitCount} unité(s) calcinée(s) ({breathDmg} dégâts, En Feu + Déstabilisé) !");
                            arena?.RecordChronoSnapshot($"Souffle Titanesque : {act.Stats.Name} ({zone.Count} hex)");
                        }
                    ));
                }
            }
        }

        /// <summary>
        /// RD-201 : ajoute une option de Tir Visé entravé (3 PA, malus annulé,
        /// visée membre autorisée) au registre.
        /// </summary>
        private static void AddEntraveAimedShot(
            List<CombatAction> actions, TacticalUnit actor, TacticalUnit target,
            CombatDevArena arena, BodyPart part, string title, string description)
        {
            string ammoTag = AmmoTag(actor, target);
            actions.Add(new CombatAction(
                $"{title}{ammoTag}",
                description,
                ActionCategory.AttaqueEtPassesDarmes,
                3,
                (act, tgt) => CanAttackTarget(act, tgt, 3) && CanFireShot(act, tgt),
                (act, tgt) => arena.ExecuteAttack(part, cancelPenaltyWithAP: true,
                    attackSkill: ResolveContactSkill(act, tgt), attackerBonusAP: CombatContextMenuUI.CurrentInjectedAP, attackerPE: CombatContextMenuUI.CurrentInjectedPE, explicitTarget: tgt, explicitAttacker: act)
            ));
        }
    }
}

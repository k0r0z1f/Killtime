using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Core.Combat;
using Killtime.Core.Character;
using Killtime.Core.Dice;
using Killtime.Core.Inventory;
using Killtime.UI;

namespace Killtime.Tactics.CombatUI
{
    /// <summary>
    /// Registre officiel des actions contextuelles de combat classées par livre du Codex.
    /// Valide rigoureusement la portée géométrique (mêlée vs tir) et l'armement équipé.
    /// </summary>
    public static class CombatActionRegistry
    {
        public static bool HasRangedWeaponEquipped(TacticalUnit actor)
        {
            if (actor == null) return false;
            var weapon = actor.Sheet?.GetEquippedWeapon();
            return weapon != null && weapon.RangeInTiles > 1;
        }

        public static int GetAttackMaxRange(TacticalUnit actor)
        {
            if (actor == null) return 1;
            var weapon = actor.Sheet?.GetEquippedWeapon();
            if (weapon != null && weapon.RangeInTiles > 0) return weapon.RangeInTiles;
            return 1; // Mains nues par défaut = portée de contact 1 case (Codex Livre VI)
        }

        public static bool IsTargetInRange(TacticalUnit actor, TacticalUnit target)
        {
            if (actor == null || target == null) return false;
            int distance = actor.CurrentCoords.DistanceTo(target.CurrentCoords);
            int maxRange = GetAttackMaxRange(actor);

            // Arme de contact ou mains nues (portée 1)
            if (maxRange <= 1 || !HasRangedWeaponEquipped(actor))
            {
                return distance <= 1;
            }

            // Arme à distance équipée
            return distance <= maxRange;
        }

        public static bool HasEnoughAP(TacticalUnit actor, int apCost)
        {
            return actor != null && actor.Stats != null && actor.Stats.CurrentActionPoints >= apCost;
        }

        private static bool CanAttackTarget(TacticalUnit actor, TacticalUnit target, int apCost)
        {
            if (actor == null || target == null) return false;
            if (target.Stats == null || !target.Stats.IsAlive) return false;
            if (actor.Stats == null || actor.Stats.CurrentActionPoints < apCost) return false;

            int distance = actor.CurrentCoords.DistanceTo(target.CurrentCoords);
            int maxRange = GetAttackMaxRange(actor);

            // Combat à mains nues ou arme de contact : contact immédiat requis (<= 1 case)
            if (maxRange <= 1 || !HasRangedWeaponEquipped(actor))
            {
                return distance <= 1;
            }

            return distance <= maxRange;
        }

        private static SkillType ResolveContactSkill(TacticalUnit actor, TacticalUnit target)
        {
            if (actor == null) return SkillType.MainsNues;

            // Hors de portée de contact avec une arme à distance disponible => tir.
            // Évite le cas "mêlée jouée à distance" quand le menu a été ouvert au contact
            // puis l'unité s'est déplacée avant de valider.
            if (actor != null && target != null)
            {
                int dist = actor.CurrentCoords.DistanceTo(target.CurrentCoords);
                if (dist > 1 && HasRangedWeaponEquipped(actor))
                {
                    return SkillType.Ballistique;
                }
            }

            var weapon = actor.Sheet?.GetEquippedWeapon();

            if (weapon != null)
            {
                // Valeur legacy ArmesContondantes rabattue sur Maniement d'Arme.
                return SkillDefinitions.ResolveBaseSkill(weapon.AssociatedSkill);
            }

            // Absence d'inventaire ou d'arme équipée : combat au corps-à-corps à mains nues
            return SkillType.MainsNues;
        }

        public static List<CombatAction> GetAvailableActions(TacticalUnit actor, TacticalUnit target, CombatDevArena arena)
        {
            var actions = new List<CombatAction>();
            if (actor == null || target == null) return actions;

            bool isSelf = (actor == target);
            bool isEnemy = !target.IsPlayerControlled;
            bool targetIsAlive = target.Stats.IsAlive;

            // =========================================================================
            // 1. ATTAQUE & PASSES D'ARMES (LIVRE VI)
            // =========================================================================
            if (!isSelf && isEnemy && targetIsAlive)
            {
                // Frappe Standard
                actions.Add(new CombatAction(
                    "🎯 Attaque Standard (2 PA)",
                    "Frappe générale sur le centre de masse (Torse).",
                    ActionCategory.AttaqueEtPassesDarmes,
                    2,
                    (act, tgt) => CanAttackTarget(act, tgt, 2),
                    (act, tgt) => arena.ExecuteAttack(BodyPart.Torse, cancelPenaltyWithAP: false,
                        attackSkill: ResolveContactSkill(act, tgt), attackerBonusAP: CombatContextMenuUI.CurrentInjectedAP, attackerPE: CombatContextMenuUI.CurrentInjectedPE, explicitTarget: tgt, explicitAttacker: act)
                ));

                // Visée Chirurgicale Tête
                actions.Add(new CombatAction(
                    "💀 Visée : Tête (3 PA)",
                    "Tir chirurgical avec dépense préalable de +1 PA pour annuler le malus de -2.",
                    ActionCategory.AttaqueEtPassesDarmes,
                    3,
                    (act, tgt) => CanAttackTarget(act, tgt, 3),
                    (act, tgt) => arena.ExecuteAttack(BodyPart.Tete, cancelPenaltyWithAP: true,
                        attackSkill: ResolveContactSkill(act, tgt), attackerBonusAP: CombatContextMenuUI.CurrentInjectedAP, attackerPE: CombatContextMenuUI.CurrentInjectedPE, explicitTarget: tgt, explicitAttacker: act)
                ));

                // Désarmement (Bras Droit)
                actions.Add(new CombatAction(
                    "🗡️ Visée : Bras Droit (2 PA)",
                    "Frappe ciblée pour tenter un désarmement ou un malus d'attaque.",
                    ActionCategory.AttaqueEtPassesDarmes,
                    2,
                    (act, tgt) => CanAttackTarget(act, tgt, 2),
                    (act, tgt) => arena.ExecuteAttack(BodyPart.BrasDroit, cancelPenaltyWithAP: false,
                        attackSkill: ResolveContactSkill(act, tgt), attackerBonusAP: CombatContextMenuUI.CurrentInjectedAP, attackerPE: CombatContextMenuUI.CurrentInjectedPE, explicitTarget: tgt, explicitAttacker: act)
                ));

                // Faucher (Jambes)
                actions.Add(new CombatAction(
                    "🦵 Visée : Jambes (2 PA)",
                    "Impact sur les membres inférieurs pour infliger l'état À Terre et Ralenti.",
                    ActionCategory.AttaqueEtPassesDarmes,
                    2,
                    (act, tgt) => CanAttackTarget(act, tgt, 2),
                    (act, tgt) => arena.ExecuteAttack(BodyPart.Jambes, cancelPenaltyWithAP: false,
                        attackSkill: ResolveContactSkill(act, tgt), attackerBonusAP: CombatContextMenuUI.CurrentInjectedAP, attackerPE: CombatContextMenuUI.CurrentInjectedPE, explicitTarget: tgt, explicitAttacker: act)
                ));

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
                        actions.Add(new CombatAction(
                            $"💣 Lance-grenades : {gCap2.Name} via {lCap.Name} (3 PA)",
                            $"Portée {launcherMaxRange} cases, dispersion réduite. Vise la case de la cible.",
                            ActionCategory.AttaqueEtPassesDarmes,
                            3,
                            (act, tgt) => act.Stats.CurrentActionPoints >= 3 && targetIsAlive && act.CurrentCoords.DistanceTo(tgt.CurrentCoords) <= launcherMaxRange,
                            (act, tgt) => arena.ExecuteGrenadeThrow(tgt.CurrentCoords, gCap2.ItemId, true, false, 0)
                        ));
                    }
                }
            }

            // =========================================================================
            // 2. CINQUIÈME FORCE & SORTS MODULAIRES (LIVRE IV)
            // =========================================================================
            if (actor.Sheet != null && actor.Sheet.LearnedSpells.Count > 0)
            {
                foreach (var spell in actor.Sheet.LearnedSpells)
                {
                    actions.Add(new CombatAction(
                        $"🔮 Canaliser : {spell.Name} ({spell.ActionPointCost} PA)",
                        $"5e Force ({spell.Discipline}) — Dégâts de base: {spell.BaseArcaneDamage}",
                        ActionCategory.CinquiemeForceEtSorts,
                        spell.ActionPointCost,
                        (act, tgt) => act.Stats.CurrentActionPoints >= spell.ActionPointCost && targetIsAlive,
                        (act, tgt) =>
                        {
                            var dice = new DiceRoller();
                            if (spell.Cast(act.Stats, tgt.Stats, dice, out string log))
                            {
                                var vis = tgt.GetComponent<TacticalUnitVisual>();
                                vis?.TriggerHitFlash();
                                vis?.SpawnFloatingText($"-{spell.BaseArcaneDamage} Arcanique", Color.magenta);
                            }
                        }
                    ));
                }
            }

            // =========================================================================
            // 3. TRAUMATOLOGIE & SOINS (LIVRE VII)
            // =========================================================================
            if (targetIsAlive)
            {
                // Chirurgie : Suture Réflexe (Livre III) — gestes médicaux d'urgence
                // sur le front : les premiers soins passent de 3 PA à 2 PA.
                int healCost = (actor.Stats != null && actor.Stats.HasSpecialization("Chirurgie : Suture Réflexe")) ? 2 : 3;
                string healDesc = healCost == 2
                    ? "Suture Réflexe au contact (Régénère Constitution × 2 PV). Coût réduit à 2 PA par la spécialisation."
                    : "Stabilisation et suture d'urgence au contact (Régénère Constitution × 2 PV).";
                actions.Add(new CombatAction(
                    $"🩹 Premiers Soins d'Urgence ({healCost} PA)",
                    healDesc,
                    ActionCategory.TraumatologieEtSoins,
                    healCost,
                    (act, tgt) => act.Stats.CurrentActionPoints >= healCost && act.CurrentCoords.DistanceTo(tgt.CurrentCoords) <= 1,
                    (act, tgt) =>
                    {
                        if (act.Stats.ConsumeActionPoints(healCost))
                        {
                            int healAmount = tgt.Stats.Attributes.Constitution * 2;
                            tgt.Stats.CurrentHealth = Mathf.Min(tgt.Stats.MaxHealth, tgt.Stats.CurrentHealth + healAmount);
                            tgt.Stats.ActiveStatus &= ~StatusEffect.Saignement;

                            var vis = tgt.GetComponent<TacticalUnitVisual>();
                            vis?.SpawnFloatingText($"+{healAmount} PV Soignés", Color.green);
                        }
                    }
                ));
            }
            else
            {
                // Réanimation d'urgence
                actions.Add(new CombatAction(
                    "⚡ Défibrillation Arcanique (4 PA)",
                    "Tente de ramener un combattant au contact à 1 PV avant le coma définitif.",
                    ActionCategory.TraumatologieEtSoins,
                    4,
                    (act, tgt) => act.Stats.CurrentActionPoints >= 4 && act.CurrentCoords.DistanceTo(tgt.CurrentCoords) <= 1,
                    (act, tgt) =>
                    {
                        if (act.Stats.ConsumeActionPoints(4))
                        {
                            tgt.Stats.CurrentHealth = 1;
                            tgt.Stats.ActiveStatus &= ~StatusEffect.Inconscient;

                            var vis = tgt.GetComponent<TacticalUnitVisual>();
                            vis?.SpawnFloatingText("RÉANIMATION!", Color.cyan);
                        }
                    }
                ));
            }

            // Souffle : actions personnelles (Livres I §4.2 + VI §24.2).
            // Refresh PA = début de son propre tour uniquement (TurnManager).
            // Ici : les deux conversions Souffle <-> PA, jouables sur soi-même.
            if (isSelf)
            {
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
            }

            // =========================================================================
            // 4. TACTIQUE & COMMANDEMENT (LIVRE III)
            // =========================================================================
            if (!isSelf && !isEnemy && targetIsAlive)
            {
                actions.Add(new CombatAction(
                    "📢 Ordre Tactique : Couvrir (+1 PA) (2 PA)",
                    "Délègue un point d'action réflexe à l'allié désigné.",
                    ActionCategory.TactiqueEtOrdres,
                    2,
                    (act, tgt) => act != null && act.Stats.CurrentActionPoints >= 2 && act.CurrentCoords.DistanceTo(tgt.CurrentCoords) <= 6,
                    (act, tgt) =>
                    {
                        if (act == null || tgt == null) return;
                        if (act.Stats.ConsumeActionPoints(2))
                        {
                            var actVis = act.GetComponent<TacticalUnitVisual>();
                            actVis?.SpawnFloatingText("Ordre Tactique (-2 PA)", new Color(0.2f, 0.85f, 1.0f));

                            tgt.Stats.CurrentActionPoints = Mathf.Min(tgt.Stats.MaxActionPoints, tgt.Stats.CurrentActionPoints + 1);
                            var tgtVis = tgt.GetComponent<TacticalUnitVisual>();
                            tgtVis?.SpawnFloatingText("+1 PA Reçu", Color.cyan);

                            if (Killtime.Audio.KilltimeAudioManager.Instance != null)
                            {
                                Killtime.Audio.KilltimeAudioManager.Instance.PlayAt(Killtime.Audio.SoundId.PA_Refill, tgt.transform.position, 0.7f);
                            }

                            arena?.Log($"📢 <b>{act.Stats.Name}</b> donne un ordre de couverture à <b>{tgt.Stats.Name}</b> (-2 PA / +1 PA réflexe) !");
                            arena?.RecordChronoSnapshot($"Ordre Tactique : {act.Stats.Name} -> {tgt.Stats.Name}");
                        }
                    }
                ));
            }

            if (!isSelf && isEnemy && targetIsAlive)
            {
                actions.Add(new CombatAction(
                    "🗣️ Intimidation / Provocation (2 PA)",
                    "Défi opposé aveugle Intimidation vs Intuition : mises masquées (PA/PE), révélation simultanée. Victoire = cible Déstabilisée (-2) et -1 PA de réaction.",
                    ActionCategory.TactiqueEtOrdres,
                    2,
                    (act, tgt) => act != null && act.Stats.CurrentActionPoints >= 2 && act.CurrentCoords.DistanceTo(tgt.CurrentCoords) <= 6,
                    (act, tgt) =>
                    {
                        if (act == null || tgt == null) return;
                        if (act.Stats.ConsumeActionPoints(2))
                        {
                            var calc = new CombatCalculator();
                            // Défi opposé aveugle (Livres II §7 + III) : mises déclarées
                            // avant les jets, résultat caché jusqu'à révélation simultanée.
                            // La cible résiste en aveugle (mise auto estimée, jamais de PE auto).
                            var duel = calc.ResolveOpposedCheck(
                                act.Stats, SkillType.Intimidation,
                                tgt.Stats, SkillType.Intuition,
                                attackerBonusAP: 0, attackerPE: 0,
                                defenderAutoStakes: true);

                            var actVis = act.GetComponent<TacticalUnitVisual>();
                            var tgtVis = tgt.GetComponent<TacticalUnitVisual>();
                            arena?.Log($"🗣️ <b>{act.Stats.Name}</b> intimide <b>{tgt.Stats.Name}</b> (-2 PA base, duel aveugle) :\n   {duel.CombatLog}");

                            if (duel.AttackerWins)
                            {
                                actVis?.SpawnFloatingText("Intimidation (-2 PA)", new Color(0.2f, 0.85f, 1.0f));
                                tgt.Stats.ActiveStatus |= StatusEffect.Destabilise;
                                tgtVis?.TriggerHitFlash();
                                tgtVis?.SpawnFloatingText("DÉSTABILISÉ! (-2)", Color.yellow);

                                // Amputation de 1 PA de réaction/réserve sur la cible si disponible (Livre III)
                                if (tgt.Stats.CurrentActionPoints > 0)
                                {
                                    tgt.Stats.ConsumeActionPoints(1);
                                    tgtVis?.SpawnFloatingText("-1 PA Réaction", new Color(1f, 0.6f, 0.2f));
                                }
                            }
                            else
                            {
                                actVis?.SpawnFloatingText("Intimidation contenue", new Color(0.6f, 0.6f, 0.6f));
                                tgtVis?.SpawnFloatingText("IMPASSIBLE", Color.cyan);
                            }

                            if (Killtime.Audio.KilltimeAudioManager.Instance != null)
                            {
                                Killtime.Audio.KilltimeAudioManager.Instance.PlayAt(Killtime.Audio.SoundId.Trauma_Shock, tgt.transform.position, 0.8f);
                            }

                            arena?.RecordChronoSnapshot($"Intimidation : {act.Stats.Name} -> {tgt.Stats.Name}");
                        }
                    }
                ));
            }

            // =========================================================================
            // 5. TECHNIQUES DE SPÉCIALISATION (LIVRE III)
            // Clé d'Articulation, Analyse de Faille, Rugissement, Regard de
            // Prédateur, Commandement de zone, Tenir la Ligne ! — 2 PA chacune.
            // Construites par le registre partagé joueur + IA.
            // =========================================================================
            if (CombatTechniqueRegistry.HasAnyCombatTechnique(actor))
            {
                actions.AddRange(CombatTechniqueRegistry.GetTechniqueActions(actor, target, arena));
            }

            // =========================================================================
            // 5b. DUO-TECH (Lucas + Mina) — action unique, paiement immédiat des
            // deux, tissage souris T1 + onde + T2, sans expiration.
            // =========================================================================
            actions.AddRange(DuoTechRegistry.GetDuoTechActions(actor, target, arena));

            // =========================================================================
            // 6. COMMANDES DÉVELOPPEUR
            // =========================================================================
            actions.Add(new CombatAction(
                "📜 [DEV] Fiche de Personnage Complète",
                "Ouvre et affiche la fiche technique intégrale (Attributs, PA, Encaissement, Compétences).",
                ActionCategory.CommandesDev,
                0,
                null,
                (act, tgt) => CharacterDevWindow.OpenForUnit(tgt)
            ));

            actions.Add(new CombatAction(
                "🎒 [DEV] Gérer l'Inventaire & Armes",
                "Ouvre la fenêtre d'inventaire et d'armurerie 3D pour cette unité.",
                ActionCategory.CommandesDev,
                0,
                null,
                (act, tgt) =>
                {
                    InventoryDevWindow.Open();
                    InventoryDevWindow.Instance?.InspectUnit(tgt);
                }
            ));

            actions.Add(new CombatAction(
                "⚡ [DEV] Recharger tous les PA",
                "Restaure instantanément la réserve de PA au plafond maximal.",
                ActionCategory.CommandesDev,
                0,
                null,
                (act, tgt) => tgt.Stats.CurrentActionPoints = tgt.Stats.MaxActionPoints
            ));

            actions.Add(new CombatAction(
                "❤️ [DEV] Soin Intégral & Dissipation",
                "Restaure l'intégralité des PV et annule tous les statuts négatifs.",
                ActionCategory.CommandesDev,
                0,
                null,
                (act, tgt) =>
                {
                    tgt.Stats.CurrentHealth = tgt.Stats.MaxHealth;
                    tgt.Stats.ActiveStatus = StatusEffect.None;
                    tgt.Stats.Essoufflement = 0;
                }
            ));

            actions.Add(new CombatAction(
                "💀 [DEV] Neutraliser Immédiatement (K.O.)",
                "Bascule les PV à 0 et applique l'inconscience clinique.",
                ActionCategory.CommandesDev,
                0,
                null,
                (act, tgt) =>
                {
                    tgt.Stats.CurrentHealth = 0;
                    tgt.Stats.ActiveStatus |= StatusEffect.Inconscient;
                }
            ));

            actions.Add(new CombatAction(
                "🗑️ [DEV] Retirer de la Carte (Supprimer)",
                "Désenregistre et détruit définitivement cette unité de la grille.",
                ActionCategory.CommandesDev,
                0,
                null,
                (act, tgt) =>
                {
                    if (arena != null)
                    {
                        arena.RemoveUnit(tgt);
                    }
                    else
                    {
                        var tm = Object.FindAnyObjectByType<TurnManager>();
                        tm?.UnregisterUnit(tgt);
                        Object.Destroy(tgt.gameObject);
                    }
                }
            ));

            return actions;
        }
    }
}
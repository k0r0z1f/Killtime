using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics.Units;
using Killtime.Core.Combat;
using Killtime.Core.Character;

namespace Killtime.Tactics.CombatUI
{
    /// <summary>
    /// Registre des techniques de spécialisation activables en combat (Livre III).
    /// Chaque technique correspond à une spécialisation du compendium
    /// (CharacterProgressionManager) dont l'effet mécanique est jouable au tour par tour.
    /// Utilisé à la fois par le menu contextuel joueur (CombatActionRegistry) et par
    /// l'IA tactique (TacticalAIController) : une seule implémentation, deux appelants.
    ///
    /// Coûts fixes (Livre III) : 2 PA par technique, débités via ConsumeActionPoints
    /// (Ralenti x2 appliqué automatiquement). Toute réussite coche la progression
    /// organique de la compétence source (Livre I §5).
    /// </summary>
    public static class CombatTechniqueRegistry
    {
        public const int CleCost = 2;
        public const int AnalyseCost = 2;
        public const int RugissementCost = 2;
        public const int RegardCost = 2;
        public const int MenerCost = 2;
        public const int TenirCost = 2;

        public const int CleRawDamage = 3;
        public const int AnalyseRange = 10;
        public const int RegardRange = 6;
        public const int AuraRange = 3;
        public const int TenirArmorBonus = 2;

        public const string SpecCle = "Arts Martiaux : Clé d'Articulation";
        public const string SpecRupture = "Arts Martiaux : Rupture Ligamentaire";
        public const string SpecAnalyse = "Analyse de Faille";
        public const string SpecTirCoordonne = "Analyse de Faille : Tir Coordonné";
        public const string SpecRugissement = "Intimider : Rugissement de Terreur";
        public const string SpecRegard = "Intimider : Regard de Prédateur";
        public const string SpecMener = "Mener (Commandement)";
        public const string SpecTenir = "Mener : Tenir la Ligne !";

        // =====================================================================
        // REQUÊTES GÉNÉRIQUES
        // =====================================================================

        public static bool HasAnyCombatTechnique(TacticalUnit actor)
        {
            if (actor?.Stats == null) return false;
            var s = actor.Stats;
            return s.HasSpecialization(SpecCle)
                || s.HasSpecialization(SpecAnalyse)
                || s.HasSpecialization(SpecRugissement)
                || s.HasSpecialization(SpecRegard)
                || s.HasSpecialization(SpecMener)
                || s.HasSpecialization(SpecTenir);
        }

        private static List<TacticalUnit> GetAllUnits()
        {
            return new List<TacticalUnit>(Object.FindObjectsByType<TacticalUnit>(FindObjectsInactive.Exclude));
        }

        private static bool IsEnemyOf(TacticalUnit actor, TacticalUnit other)
        {
            if (actor == null || other == null) return false;
            return other.IsPlayerControlled != actor.IsPlayerControlled;
        }

        private static bool IsAliveUnit(TacticalUnit u)
        {
            return u != null && u.Stats != null && u.Stats.IsAlive;
        }

        public static int CountEnemiesInRange(TacticalUnit actor, int range, bool onlyNotDestabilised = false)
        {
            if (actor?.Stats == null) return 0;
            int count = 0;
            var all = GetAllUnits();
            for (int i = 0; i < all.Count; i++)
            {
                var u = all[i];
                if (!IsAliveUnit(u) || !IsEnemyOf(actor, u)) continue;
                if (actor.CurrentCoords.DistanceTo(u.CurrentCoords) > range) continue;
                if (onlyNotDestabilised && (u.Stats.ActiveStatus & StatusEffect.Destabilise) != 0) continue;
                count++;
            }
            return count;
        }

        public static int CountAlliesInRange(TacticalUnit actor, int range, bool onlyMissingPA = false, bool onlyWithoutLineBonus = false)
        {
            if (actor?.Stats == null) return 0;
            int count = 0;
            var all = GetAllUnits();
            for (int i = 0; i < all.Count; i++)
            {
                var u = all[i];
                if (!IsAliveUnit(u) || u == actor || IsEnemyOf(actor, u)) continue;
                if (actor.CurrentCoords.DistanceTo(u.CurrentCoords) > range) continue;
                if (onlyMissingPA && u.Stats.CurrentActionPoints >= u.Stats.MaxActionPoints) continue;
                if (onlyWithoutLineBonus && SkillTechniqueState.HasLineBonus(u.Stats)) continue;
                count++;
            }
            return count;
        }

        private static void TickProgression(CharacterSheet sheet, SkillType skill, CombatDevArena arena)
        {
            if (sheet == null) return;
            if (CharacterProgressionManager.RegisterSuccessfulSkillUse(sheet, skill, out string msg, out _, true)
                && !string.IsNullOrEmpty(msg))
            {
                arena?.Log(msg);
            }
        }

        // =====================================================================
        // 1. CLÉ D'ARTICULATION (Mains Nues — 2 PA, contact)
        // =====================================================================

        public static bool CanUseCle(TacticalUnit actor, TacticalUnit target)
        {
            if (!IsAliveUnit(actor) || !IsAliveUnit(target)) return false;
            if (!IsEnemyOf(actor, target)) return false;
            if (actor.CurrentCoords.DistanceTo(target.CurrentCoords) > 1) return false;
            if (!actor.Stats.HasSpecialization(SpecCle)) return false;
            return actor.Stats.CurrentActionPoints >= CleCost;
        }

        public static bool ExecuteCle(TacticalUnit actor, TacticalUnit target, CombatDevArena arena)
        {
            if (!CanUseCle(actor, target)) return false;
            if (!actor.Stats.ConsumeActionPoints(CleCost)) return false;

            bool wasDestabilised = (target.Stats.ActiveStatus & StatusEffect.Destabilise) != 0;

            // 3 dégâts bruts sans réduction d'armure (Livre III).
            int remaining = target.Stats.CurrentHealth - CleRawDamage;
            if (remaining <= 0)
            {
                target.Stats.EvaluateFatalBlow(BodyPart.Torse, CleRawDamage);
            }
            else
            {
                target.Stats.CurrentHealth = remaining;
            }

            // Rupture Ligamentaire : cible déjà Déstabilisée → Paralysé 1 tour,
            // sinon Immobilisé 1 tour (effet de base de la Clé).
            string statusLabel;
            if (wasDestabilised && actor.Stats.HasSpecialization(SpecRupture))
            {
                target.Stats.ApplyStatus(StatusEffect.Paralyse, 1);
                statusLabel = "PARALYSÉ !";
            }
            else
            {
                target.Stats.ApplyStatus(StatusEffect.Immobilise, 1);
                statusLabel = "IMMOBILISÉ !";
            }

            actor.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("🥋 Clé d'Articulation (-2 PA)", Color.cyan);
            var tgtVis = target.GetComponent<TacticalUnitVisual>();
            tgtVis?.TriggerHitFlash();
            tgtVis?.SpawnFloatingText($"-{CleRawDamage} bruts + {statusLabel}", Color.red);
            arena?.Log($"🥋 <b>{actor.Stats.Name}</b> verrouille <b>{target.Stats.Name}</b> en <b>Clé d'Articulation</b> (-2 PA) : {CleRawDamage} dégâts bruts + [{statusLabel}] !");
            arena?.RecordChronoSnapshot($"Clé d'Articulation : {actor.Stats.Name} -> {target.Stats.Name}");

            TickProgression(actor.Sheet, SkillType.MainsNues, arena);
            return true;
        }

        // =====================================================================
        // 2. ANALYSE DE FAILLE (Tactique & Stratégie — 2 PA, ≤10 cases)
        // =====================================================================

        public static bool CanUseAnalyse(TacticalUnit actor, TacticalUnit target)
        {
            if (!IsAliveUnit(actor) || !IsAliveUnit(target)) return false;
            if (!IsEnemyOf(actor, target)) return false;
            if (actor.CurrentCoords.DistanceTo(target.CurrentCoords) > AnalyseRange) return false;
            if (!actor.Stats.HasSpecialization(SpecAnalyse)) return false;
            if (SkillTechniqueState.IsFlawExposed(target.Stats)) return false;
            return actor.Stats.CurrentActionPoints >= AnalyseCost;
        }

        public static bool ExecuteAnalyse(TacticalUnit actor, TacticalUnit target, CombatDevArena arena)
        {
            if (!CanUseAnalyse(actor, target)) return false;
            if (!actor.Stats.ConsumeActionPoints(AnalyseCost)) return false;

            SkillTechniqueState.ApplyExposedFlaw(actor.Stats, target.Stats);

            // Tir Coordonné : le guidage balistique déstabilise la cible exposée
            // (équivalent mécanique du +2 d'attaque d'escouade, Livre III).
            bool coordinated = actor.Stats.HasSpecialization(SpecTirCoordonne);
            if (coordinated)
            {
                target.Stats.ApplyStatus(StatusEffect.Destabilise, 1);
            }

            actor.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("📡 Analyse de Faille (-2 PA)", new Color(0.2f, 0.85f, 1.0f));
            target.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("FAILLE EXPOSÉE !", Color.yellow);
            arena?.Log($"📡 <b>{actor.Stats.Name}</b> expose la faille de <b>{target.Stats.Name}</b> (-2 PA) : la prochaine touche ignore l'encaissement{(coordinated ? " + [Déstabilisé] (Tir Coordonné)" : "")} !");
            arena?.RecordChronoSnapshot($"Analyse de Faille : {actor.Stats.Name} -> {target.Stats.Name}");

            TickProgression(actor.Sheet, SkillType.TactiqueStrategie, arena);
            return true;
        }

        // =====================================================================
        // 3. RUGISSEMENT DE TERREUR (Intimidation — 2 PA, zone 3 cases, sur soi)
        // =====================================================================

        public static bool CanUseRugissement(TacticalUnit actor)
        {
            if (!IsAliveUnit(actor)) return false;
            if (!actor.Stats.HasSpecialization(SpecRugissement)) return false;
            if (actor.Stats.CurrentActionPoints < RugissementCost) return false;
            return CountEnemiesInRange(actor, AuraRange, onlyNotDestabilised: true) > 0;
        }

        public static bool ExecuteRugissement(TacticalUnit actor, CombatDevArena arena)
        {
            if (!CanUseRugissement(actor)) return false;
            if (!actor.Stats.ConsumeActionPoints(RugissementCost)) return false;

            var calc = new CombatCalculator();
            int shaken = 0;
            var all = GetAllUnits();
            for (int i = 0; i < all.Count; i++)
            {
                var u = all[i];
                if (!IsAliveUnit(u) || !IsEnemyOf(actor, u)) continue;
                if (actor.CurrentCoords.DistanceTo(u.CurrentCoords) > AuraRange) continue;
                if ((u.Stats.ActiveStatus & StatusEffect.Destabilise) != 0) continue;

                var duel = calc.ResolveOpposedCheck(actor.Stats, SkillType.Intimidation, u.Stats, SkillType.Intuition, 0, 0, defenderAutoStakes: true);
                arena?.Log($"😱 <b>{actor.Stats.Name}</b> rugit sur <b>{u.Stats.Name}</b> :\n   {duel.CombatLog}");
                if (duel.AttackerWins)
                {
                    u.Stats.ApplyStatus(StatusEffect.Destabilise, 1);
                    var vis = u.GetComponent<TacticalUnitVisual>();
                    vis?.TriggerHitFlash();
                    vis?.SpawnFloatingText("TERRORISÉ ! (-2)", Color.yellow);
                    shaken++;
                }
            }

            actor.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText($"😱 Rugissement (-2 PA, {shaken} secoué(s))", new Color(1f, 0.5f, 0.1f));
            arena?.Log($"😱 <b>{actor.Stats.Name}</b> déchaîne un <b>Rugissement de Terreur</b> (-2 PA) : {shaken} ennemi(s) Déstabilisé(s) à moins de {AuraRange} cases !");
            arena?.RecordChronoSnapshot($"Rugissement de Terreur : {actor.Stats.Name} ({shaken})");

            if (shaken > 0) TickProgression(actor.Sheet, SkillType.Intimidation, arena);
            return true;
        }

        // =====================================================================
        // 4. REGARD DE PRÉDATEUR (Intimidation — 2 PA, ≤6 cases)
        // =====================================================================

        public static bool CanUseRegard(TacticalUnit actor, TacticalUnit target)
        {
            if (!IsAliveUnit(actor) || !IsAliveUnit(target)) return false;
            if (!IsEnemyOf(actor, target)) return false;
            if (actor.CurrentCoords.DistanceTo(target.CurrentCoords) > RegardRange) return false;
            if (!actor.Stats.HasSpecialization(SpecRegard)) return false;
            return actor.Stats.CurrentActionPoints >= RegardCost;
        }

        public static bool ExecuteRegard(TacticalUnit actor, TacticalUnit target, CombatDevArena arena)
        {
            if (!CanUseRegard(actor, target)) return false;
            if (!actor.Stats.ConsumeActionPoints(RegardCost)) return false;

            var calc = new CombatCalculator();
            var duel = calc.ResolveOpposedCheck(actor.Stats, SkillType.Intimidation, target.Stats, SkillType.Intuition, 0, 0, defenderAutoStakes: true);
            arena?.Log($"👁️ <b>{actor.Stats.Name}</b> fixe <b>{target.Stats.Name}</b> en duel singulier (-2 PA) :\n   {duel.CombatLog}");

            var actVis = actor.GetComponent<TacticalUnitVisual>();
            var tgtVis = target.GetComponent<TacticalUnitVisual>();
            if (duel.AttackerWins)
            {
                target.Stats.ApplyStatus(StatusEffect.Destabilise, 1);
                SkillTechniqueState.ApplyTaunt(actor.Stats, target.Stats);
                tgtVis?.TriggerHitFlash();
                tgtVis?.SpawnFloatingText("PROVOQUÉ ! (doit t'attaquer)", Color.yellow);
                actVis?.SpawnFloatingText("👁️ Regard de Prédateur (-2 PA)", Color.cyan);
                if (target.Stats.CurrentActionPoints > 0)
                {
                    target.Stats.ConsumeActionPoints(1);
                    tgtVis?.SpawnFloatingText("-1 PA Réaction", new Color(1f, 0.6f, 0.2f));
                }
                TickProgression(actor.Sheet, SkillType.Intimidation, arena);
            }
            else
            {
                actVis?.SpawnFloatingText("Regard soutenu sans effet", Color.gray);
                tgtVis?.SpawnFloatingText("IMPASSIBLE", Color.cyan);
            }

            arena?.RecordChronoSnapshot($"Regard de Prédateur : {actor.Stats.Name} -> {target.Stats.Name}");
            return true;
        }

        // =====================================================================
        // 5. MENER / COMMANDEMENT ZONE (Leadership — 2 PA, alliés ≤3 cases, sur soi)
        // =====================================================================

        public static bool CanUseMenerZone(TacticalUnit actor)
        {
            if (!IsAliveUnit(actor)) return false;
            if (!actor.Stats.HasSpecialization(SpecMener)) return false;
            if (actor.Stats.CurrentActionPoints < MenerCost) return false;
            return CountAlliesInRange(actor, AuraRange, onlyMissingPA: true) > 0;
        }

        public static bool ExecuteMenerZone(TacticalUnit actor, CombatDevArena arena)
        {
            if (!CanUseMenerZone(actor)) return false;
            if (!actor.Stats.ConsumeActionPoints(MenerCost)) return false;

            int rallied = 0;
            var all = GetAllUnits();
            for (int i = 0; i < all.Count; i++)
            {
                var u = all[i];
                if (!IsAliveUnit(u) || u == actor || IsEnemyOf(actor, u)) continue;
                if (actor.CurrentCoords.DistanceTo(u.CurrentCoords) > AuraRange) continue;
                if (u.Stats.CurrentActionPoints >= u.Stats.MaxActionPoints) continue;
                u.Stats.CurrentActionPoints = Mathf.Min(u.Stats.MaxActionPoints, u.Stats.CurrentActionPoints + 1);
                u.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("+1 PA Reçu", Color.cyan);
                rallied++;
            }

            actor.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText($"📢 Commandement (-2 PA, {rallied} rallié(s))", new Color(0.2f, 0.85f, 1.0f));
            arena?.Log($"📢 <b>{actor.Stats.Name}</b> galvanise l'escouade (-2 PA) : +1 PA réflexe à {rallied} allié(s) à moins de {AuraRange} cases !");
            arena?.RecordChronoSnapshot($"Commandement : {actor.Stats.Name} ({rallied})");

            TickProgression(actor.Sheet, SkillType.Leadership, arena);
            return true;
        }

        // =====================================================================
        // 6. TENIR LA LIGNE ! (Leadership — 2 PA, alliés ≤3 cases, sur soi)
        // =====================================================================

        public static bool CanUseTenir(TacticalUnit actor)
        {
            if (!IsAliveUnit(actor)) return false;
            if (!actor.Stats.HasSpecialization(SpecTenir)) return false;
            if (actor.Stats.CurrentActionPoints < TenirCost) return false;
            return CountAlliesInRange(actor, AuraRange, onlyWithoutLineBonus: true) > 0;
        }

        public static bool ExecuteTenir(TacticalUnit actor, CombatDevArena arena)
        {
            if (!CanUseTenir(actor)) return false;
            if (!actor.Stats.ConsumeActionPoints(TenirCost)) return false;

            int braced = 0;
            var all = GetAllUnits();
            for (int i = 0; i < all.Count; i++)
            {
                var u = all[i];
                if (!IsAliveUnit(u) || u == actor || IsEnemyOf(actor, u)) continue;
                if (actor.CurrentCoords.DistanceTo(u.CurrentCoords) > AuraRange) continue;
                if (SkillTechniqueState.HasLineBonus(u.Stats)) continue;
                SkillTechniqueState.ApplyLineBonus(actor.Stats, u.Stats, TenirArmorBonus);
                u.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText($"+{TenirArmorBonus} Armure (Ligne !)", Color.green);
                braced++;
            }

            actor.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText($"🛡️ Tenir la Ligne ! (-2 PA, {braced} soutenu(s))", Color.green);
            arena?.Log($"🛡️ <b>{actor.Stats.Name}</b> ordonne de <b>Tenir la Ligne !</b> (-2 PA) : +{TenirArmorBonus} armure à {braced} allié(s) jusqu'à son prochain tour !");
            arena?.RecordChronoSnapshot($"Tenir la Ligne ! : {actor.Stats.Name} ({braced})");

            TickProgression(actor.Sheet, SkillType.Leadership, arena);
            return true;
        }

        // =====================================================================
        // CONSTRUCTION DES ACTIONS DU MENU CONTEXTUEL
        // =====================================================================

        /// <summary>
        /// Construit les actions de techniques débloquées par l'acteur, filtrées
        /// selon la cible du menu (ennemi : Clé/Analyse/Regard ; soi : zones).
        /// </summary>
        public static List<CombatAction> GetTechniqueActions(TacticalUnit actor, TacticalUnit target, CombatDevArena arena)
        {
            var actions = new List<CombatAction>();
            if (!IsAliveUnit(actor) || target == null) return actions;

            bool isSelf = (actor == target);
            bool isEnemy = IsEnemyOf(actor, target);
            bool targetIsAlive = IsAliveUnit(target);

            // Comptages de zone figés à l'ouverture du menu : le radial bloque toute
            // action extérieure tant qu'il est ouvert, donc l'état est stable jusqu'au
            // clic (l'exécution re-valide en live via les Execute*). Évite un scan de
            // scène par frame dans les Conditions du menu.
            int shakableAtOpen = isSelf ? CountEnemiesInRange(actor, AuraRange, onlyNotDestabilised: true) : 0;
            int alliesMissingPAAtOpen = isSelf ? CountAlliesInRange(actor, AuraRange, onlyMissingPA: true) : 0;
            int alliesNoLineAtOpen = isSelf ? CountAlliesInRange(actor, AuraRange, onlyWithoutLineBonus: true) : 0;

            if (isEnemy && targetIsAlive)
            {
                if (actor.Stats.HasSpecialization(SpecCle))
                {
                    actions.Add(new CombatAction(
                        $"🥋 Clé d'Articulation ({CleCost} PA)",
                        "Prise de soumission au contact : 3 dégâts bruts (ignore l'armure) + Immobilisé 1 tour (Paralysé si cible déjà Déstabilisée + Rupture Ligamentaire).",
                        ActionCategory.TechniquesDeSpecialisation,
                        CleCost,
                        (act, tgt) => CanUseCle(act, tgt),
                        (act, tgt) => ExecuteCle(act, tgt, arena)
                    ));
                }

                if (actor.Stats.HasSpecialization(SpecAnalyse))
                {
                    bool coordinated = actor.Stats.HasSpecialization(SpecTirCoordonne);
                    actions.Add(new CombatAction(
                        $"📡 Analyse de Faille ({AnalyseCost} PA)",
                        $"Détection du point faible (≤{AnalyseRange} cases) : la prochaine touche ignore l'encaissement{(coordinated ? " + Déstabilisé (Tir Coordonné)" : "")}.",
                        ActionCategory.TechniquesDeSpecialisation,
                        AnalyseCost,
                        (act, tgt) => CanUseAnalyse(act, tgt),
                        (act, tgt) => ExecuteAnalyse(act, tgt, arena)
                    ));
                }

                if (actor.Stats.HasSpecialization(SpecRegard))
                {
                    actions.Add(new CombatAction(
                        $"👁️ Regard de Prédateur ({RegardCost} PA)",
                        $"Défi en duel singulier (≤{RegardRange} cases, Intimidation vs Intuition) : Déstabilisé + la cible doit vous attaquer.",
                        ActionCategory.TechniquesDeSpecialisation,
                        RegardCost,
                        (act, tgt) => CanUseRegard(act, tgt),
                        (act, tgt) => ExecuteRegard(act, tgt, arena)
                    ));
                }
            }

            if (isSelf)
            {
                if (actor.Stats.HasSpecialization(SpecRugissement))
                {
                    actions.Add(new CombatAction(
                        $"😱 Rugissement de Terreur ({RugissementCost} PA)",
                        $"Pression psychologique de zone (ennemis ≤{AuraRange} cases, duel Intimidation vs Intuition chacun) : Déstabilisé 1 tour.",
                        ActionCategory.TechniquesDeSpecialisation,
                        RugissementCost,
                        (act, tgt) => IsAliveUnit(act) && act.Stats.HasSpecialization(SpecRugissement)
                            && act.Stats.CurrentActionPoints >= RugissementCost && shakableAtOpen > 0,
                        (act, tgt) => ExecuteRugissement(act, arena)
                    ));
                }

                if (actor.Stats.HasSpecialization(SpecMener))
                {
                    actions.Add(new CombatAction(
                        $"📢 Mener : Commandement ({MenerCost} PA)",
                        $"Coordination d'escouade (alliés ≤{AuraRange} cases) : +1 PA réflexe à chacun (zone, vs Ordre Tactique mono-cible).",
                        ActionCategory.TechniquesDeSpecialisation,
                        MenerCost,
                        (act, tgt) => IsAliveUnit(act) && act.Stats.HasSpecialization(SpecMener)
                            && act.Stats.CurrentActionPoints >= MenerCost && alliesMissingPAAtOpen > 0,
                        (act, tgt) => ExecuteMenerZone(act, arena)
                    ));
                }

                if (actor.Stats.HasSpecialization(SpecTenir))
                {
                    actions.Add(new CombatAction(
                        $"🛡️ Tenir la Ligne ! ({TenirCost} PA)",
                        $"Discipline de fer (alliés ≤{AuraRange} cases) : +{TenirArmorBonus} armure à chacun jusqu'à votre prochain tour.",
                        ActionCategory.TechniquesDeSpecialisation,
                        TenirCost,
                        (act, tgt) => IsAliveUnit(act) && act.Stats.HasSpecialization(SpecTenir)
                            && act.Stats.CurrentActionPoints >= TenirCost && alliesNoLineAtOpen > 0,
                        (act, tgt) => ExecuteTenir(act, arena)
                    ));
                }
            }

            return actions;
        }
    }
}

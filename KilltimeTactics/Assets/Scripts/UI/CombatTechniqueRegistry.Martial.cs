using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics.Units;
using Killtime.Tactics.Grid;
using Killtime.Core.Combat;
using Killtime.Core.Character;
using Killtime.Core.Rules;

namespace Killtime.Tactics.CombatUI
{
    /// <summary>
    /// Clé d'Articulation, Analyse de Faille, Rugissement de Terreur, Regard de Prédateur. — extrait sans modification depuis CombatTechniqueRegistry.cs.
    /// </summary>
    public static partial class CombatTechniqueRegistry
    {

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
    }
}

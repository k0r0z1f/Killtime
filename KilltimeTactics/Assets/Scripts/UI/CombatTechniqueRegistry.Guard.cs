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
    /// Garde du corps & interception d'allié (RD-032, Livre V §20.5). — extrait sans modification depuis CombatTechniqueRegistry.cs.
    /// </summary>
    public static partial class CombatTechniqueRegistry
    {

        // =====================================================================
        // GARDE DU CORPS & INTERCEPTION D'ALLIÉ (RD-032, Livre V §20.5)
        // =====================================================================

        /// <summary>
        /// Posture active : le protecteur choisit de veiller sur un allié adjacent.
        /// </summary>
        public static bool CanSetBodyguard(TacticalUnit actor, TacticalUnit targetAlly)
        {
            if (!IsAliveUnit(actor) || !IsAliveUnit(targetAlly)) return false;
            if (actor == targetAlly || IsEnemyOf(actor, targetAlly)) return false;
            if (!actor.Stats.HasSpecialization(SpecTenir) && !actor.Stats.HasSpecialization(SpecGardeDuCorps)) return false;
            int dist = TitanFootprint.MinDistanceBetweenUnits(actor.CurrentCoords, actor.FootprintType, targetAlly.CurrentCoords, targetAlly.FootprintType);
            return dist <= InterceptRange;
        }

        public static bool ExecuteSetBodyguard(TacticalUnit actor, TacticalUnit targetAlly, CombatDevArena arena)
        {
            if (!CanSetBodyguard(actor, targetAlly)) return false;

            SkillTechniqueState.SetBodyguard(actor.Stats, targetAlly.Stats);
            actor.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText($"🛡️ Garde : {targetAlly.Stats.Name}", Color.cyan);
            targetAlly.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText($"🛡️ Protégé par {actor.Stats.Name}", Color.green);
            arena?.Log($"🛡️ <b>{actor.Stats.Name}</b> prend la posture de <b>Garde du corps</b> pour <b>{targetAlly.Stats.Name}</b> : prendra les coups à sa place au contact (1 PA réaction) !");
            arena?.RecordChronoSnapshot($"Garde du corps : {actor.Stats.Name} -> {targetAlly.Stats.Name}");
            return true;
        }

        /// <summary>
        /// Règle pure Core : vérifie si un protecteur peut intercepter un coup ciblant targetAlly (RD-032).
        /// Contact (distance <= 1), 1 PA disponible (ajusté avec Ralenti), réaction du round disponible,
        /// non neutralisé (étourdi/paralysé/sonné/inconscient), et qualifié (spé Tenir la Ligne / Garde du corps,
        /// donneur du bonus de ligne, ou garde du corps attitré).
        /// </summary>
        public static bool CanInterceptStats(CharacterStats protector, CharacterStats targetAlly, int distance)
        {
            if (protector == null || targetAlly == null || protector == targetAlly) return false;
            if (!protector.IsAlive || !targetAlly.IsAlive) return false;
            if (OpportunityState.IsCancelledByStatus(protector)) return false;
            if (!OpportunityState.CanReact(protector)) return false;

            if (!protector.CanAffordActionPoints(InterceptCost)) return false;
            if (distance > InterceptRange) return false;

            bool hasSpec = protector.HasSpecialization(SpecTenir)
                        || protector.HasSpecialization(SpecGardeDuCorps);
            bool isDonor = SkillTechniqueState.TryGetLineBonusDonor(targetAlly, out var donor) && donor == protector;
            bool isBodyguard = SkillTechniqueState.IsGuardedBy(targetAlly, protector);

            return hasSpec || isDonor || isBodyguard;
        }

        /// <summary>
        /// Débite 1 PA et consomme l'unique réaction du round du protecteur pour s'interposer.
        /// </summary>
        public static bool ExecuteInterceptStats(CharacterStats protector, CharacterStats targetAlly, out string error)
        {
            error = null;
            if (!CanInterceptStats(protector, targetAlly, 1))
            {
                error = "Interception impossible (invalide, distance > 1, ou PA/réaction manquante).";
                return false;
            }

            int effectiveCost = protector.GetEffectiveApCost(InterceptCost);
            if (!protector.ConsumeActionPoints(InterceptCost))
            {
                error = $"{protector.Name} n'a pas assez de PA ({protector.CurrentActionPoints}/{effectiveCost}) pour intercepter.";
                return false;
            }
            if (!OpportunityState.TryConsumeReaction(protector))
            {
                protector.CurrentActionPoints += effectiveCost;
                error = $"{protector.Name} a déjà dépensé sa réaction ce round.";
                return false;
            }
            return true;
        }

        public static bool CanIntercept(TacticalUnit protector, TacticalUnit targetAlly)
        {
            if (!IsAliveUnit(protector) || !IsAliveUnit(targetAlly)) return false;
            if (protector == targetAlly || IsEnemyOf(protector, targetAlly)) return false;
            int dist = TitanFootprint.MinDistanceBetweenUnits(
                protector.CurrentCoords, protector.FootprintType,
                targetAlly.CurrentCoords, targetAlly.FootprintType);
            return CanInterceptStats(protector.Stats, targetAlly.Stats, dist);
        }

        public static bool ExecuteIntercept(TacticalUnit protector, TacticalUnit targetAlly, CombatDevArena arena)
        {
            if (protector == null || targetAlly == null) return false;
            int dist = TitanFootprint.MinDistanceBetweenUnits(
                protector.CurrentCoords, protector.FootprintType,
                targetAlly.CurrentCoords, targetAlly.FootprintType);
            if (!CanInterceptStats(protector.Stats, targetAlly.Stats, dist)) return false;

            if (!ExecuteInterceptStats(protector.Stats, targetAlly.Stats, out var err))
            {
                arena?.Log($"⚠️ {err}");
                return false;
            }

            int apCost = protector.Stats.GetEffectiveApCost(InterceptCost);

            var pVis = protector.GetComponent<TacticalUnitVisual>();
            var tVis = targetAlly.GetComponent<TacticalUnitVisual>();

            pVis?.SpawnFloatingText($"🛡️ Garde du corps (-{apCost} PA)", Color.cyan);
            pVis?.TriggerBodyBlock();
            tVis?.SpawnFloatingText($"🛡️ Protégé par {protector.Stats.Name} !", Color.green);

            arena?.Log($"🛡️ <b>{protector.Stats.Name}</b> s'interpose pour <b>{targetAlly.Stats.Name}</b> (Tenir la Ligne : -{apCost} PA réaction) et prend le coup à sa place !");
            arena?.RecordChronoSnapshot($"Interception allié : {protector.Stats.Name} -> {targetAlly.Stats.Name}");

            TickProgression(protector.Sheet, SkillType.Leadership, arena);
            return true;
        }

        public static List<TacticalUnit> GetEligibleProtectors(TacticalUnit targetAlly)
        {
            var list = new List<TacticalUnit>();
            if (!IsAliveUnit(targetAlly)) return list;
            var all = GetAllUnits();
            for (int i = 0; i < all.Count; i++)
            {
                var u = all[i];
                if (CanIntercept(u, targetAlly)) list.Add(u);
            }
            return list;
        }

        public static bool HasPlayerControlledProtector(TacticalUnit targetAlly)
        {
            var protectors = GetEligibleProtectors(targetAlly);
            for (int i = 0; i < protectors.Count; i++)
            {
                if (protectors[i].IsPlayerControlled) return true;
            }
            return false;
        }

        public static TacticalUnit FindBestAIProtector(TacticalUnit targetAlly)
        {
            var protectors = GetEligibleProtectors(targetAlly);
            TacticalUnit best = null;
            float bestScore = -1f;

            for (int i = 0; i < protectors.Count; i++)
            {
                var p = protectors[i];
                if (p.IsPlayerControlled) continue;

                float targetHpRatio = (float)targetAlly.Stats.CurrentHealth / Mathf.Max(1, targetAlly.Stats.MaxHealth);
                float protHpRatio = (float)p.Stats.CurrentHealth / Mathf.Max(1, p.Stats.MaxHealth);

                // Ne pas sacrifier un garde déjà moribond pour un allié en pleine forme
                if (p.Stats.CurrentHealth <= 3 && targetHpRatio > 0.6f) continue;

                float score = (1.0f - targetHpRatio) * 10f + protHpRatio * 5f + p.Stats.BaseArmorAbsorption;
                if (SkillTechniqueState.IsGuardedBy(targetAlly.Stats, p.Stats)) score += 20f;
                else if (SkillTechniqueState.TryGetLineBonusDonor(targetAlly.Stats, out var d) && d == p.Stats) score += 10f;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = p;
                }
            }
            return best;
        }
    }
}

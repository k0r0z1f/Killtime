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
    /// Requêtes génériques (présence, voisinage, comptages, progression organique). — extrait sans modification depuis CombatTechniqueRegistry.cs.
    /// </summary>
    public static partial class CombatTechniqueRegistry
    {

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
                || s.HasSpecialization(SpecTenir)
                || s.HasSpecialization(SpecGardeDuCorps)
                || s.HasSpecialization(SpecCommandementTectonique)
                || s.HasSpecialization(SpecPasDeRetraite)
                || s.HasSpecialization(SpecChuteDePression)
                || s.HasSpecialization(SpecSuggestionsBreves)
                || s.HasSpecialization(SpecArretVectoriel);
        }

        private static List<TacticalUnit> GetAllUnits()
        {
            return new List<TacticalUnit>(TacticalUnitRegistry.AllUnits);
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
    }
}

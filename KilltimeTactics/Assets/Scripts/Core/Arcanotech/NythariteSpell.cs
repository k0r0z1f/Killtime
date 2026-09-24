using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Core.Character;
using Killtime.Core.Dice;

namespace Killtime.Core.Arcanotech
{
    [Serializable]
    public class NythariteSpell
    {
        public string SpellId = Guid.NewGuid().ToString("N");
        public string Name = "Nouveau Pouvoir";
        public string Description = "";
        public string AssociatedAttribute = "Magie";
        public SkillType AssociatedSkill = SkillType.MagieEsprit;
        public PsychicDiscipline Discipline = PsychicDiscipline.Telekinesie;
        public PsychicStage Stage = PsychicStage.Stade1_PerceptionSurface;
        public ArcanotechActionType ActionType = ArcanotechActionType.ActionPrincipale;

        public int CreationXpCost;
        public int ActionPointCost;
        public int HybridXpPenalty;

        [SerializeField] private int _baseArcaneDamage = 0;
        [SerializeField] private int _rangeInTiles = 8;

        public List<PowerModuleSelection> Modules = new();

        public int BaseArcaneDamage
        {
            get
            {
                if (_baseArcaneDamage > 0) return _baseArcaneDamage;
                int dmg = 0;
                if (Modules != null)
                {
                    for (int i = 0; i < Modules.Count; i++)
                    {
                        var m = Modules[i];
                        if (m == null || m.Rank <= 0) continue;
                        if (m.ModuleId == ArcanotechModuleId.Offensive_DirectDamage) dmg += m.Rank * 2;
                        else if (m.ModuleId == ArcanotechModuleId.Offensive_DirectAbsoluteDamage) dmg += m.Rank;
                        else if (m.ModuleId == ArcanotechModuleId.Offensive_AbsoluteDamageArea) dmg += m.Rank;
                    }
                }
                return dmg > 0 ? dmg : Math.Max(1, ActionPointCost * 2);
            }
            set => _baseArcaneDamage = value;
        }

        public int RangeInTiles
        {
            get => _rangeInTiles > 0 ? _rangeInTiles : 8;
            set => _rangeInTiles = value;
        }

        public NythariteSpell()
        {
            RecalculateCosts();
        }

        public NythariteSpell(string name, PsychicDiscipline discipline, PsychicStage stage, int costXpPa, int damage = 0, int range = 8)
        {
            Name = name;
            Discipline = discipline;
            Stage = stage;
            CreationXpCost = costXpPa;
            ActionPointCost = costXpPa;
            HybridXpPenalty = 0;
            _baseArcaneDamage = damage > 0 ? damage : costXpPa * 2;
            _rangeInTiles = range > 0 ? range : 8;
        }

        public NythariteSpell(
            string name,
            string description,
            SkillType skill,
            string associatedAttribute,
            PsychicDiscipline discipline,
            PsychicStage stage,
            IEnumerable<PowerModuleSelection> modules)
        {
            Name = name;
            Description = description;
            AssociatedSkill = skill;
            AssociatedAttribute = associatedAttribute;
            Discipline = discipline;
            Stage = stage;
            Modules = modules != null ? new List<PowerModuleSelection>(modules) : new List<PowerModuleSelection>();
            RecalculateCosts();
        }

        public void RecalculateCosts()
        {
            if (Modules == null || Modules.Count == 0)
            {
                if (ActionPointCost <= 0 && CreationXpCost > 0)
                {
                    ActionPointCost = CreationXpCost;
                }
                HybridXpPenalty = 0;
                return;
            }

            int basePoints = 0;
            var categories = new HashSet<PowerCategory>();

            for (int i = 0; i < Modules.Count; i++)
            {
                var sel = Modules[i];
                if (sel == null || sel.Rank <= 0) continue;
                var def = ArcanotechWorkshop.GetModuleDefinition(sel.ModuleId);
                if (def == null) continue;

                basePoints += def.UnitPointCost * sel.Rank;
                categories.Add(def.Category);
            }

            int categoryCount = categories.Count;
            HybridXpPenalty = categoryCount > 1 ? (categoryCount - 1) : 0;
            ActionPointCost = basePoints;
            CreationXpCost = basePoints + HybridXpPenalty;
        }

        public HashSet<PowerCategory> GetActiveCategories()
        {
            var set = new HashSet<PowerCategory>();
            if (Modules == null) return set;

            for (int i = 0; i < Modules.Count; i++)
            {
                var sel = Modules[i];
                if (sel == null || sel.Rank <= 0) continue;
                var def = ArcanotechWorkshop.GetModuleDefinition(sel.ModuleId);
                if (def != null) set.Add(def.Category);
            }
            return set;
        }

        public bool Cast(CharacterStats caster, CharacterStats target, DiceRoller diceRoller, out string combatLog, int distanceInTiles = 1)
        {
            var result = ArcanotechWorkshop.CastSpell(caster, target, this, distanceInTiles, cancelRangePenaltyWithAP: false, diceRoller);
            combatLog = result.CombatLog;
            return result.IsSuccess;
        }

        public static int CalculateBaseRange(int magicRank, int trainingCount)
        {
            return Math.Max(1, magicRank + Math.Max(0, trainingCount));
        }

        public static int CalculateDistancePenalty(int distanceInTiles, int baseRange)
        {
            if (distanceInTiles <= baseRange || baseRange <= 0) return 0;
            int extraDistance = distanceInTiles - baseRange;
            return -((extraDistance + baseRange - 1) / baseRange);
        }
    }
}
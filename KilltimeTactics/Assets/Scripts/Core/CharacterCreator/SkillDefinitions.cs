using System;
using Killtime.Core.Dice;

namespace Killtime.Core.Character
{
    public enum SkillType
    {
        // Physique - Force
        MainsNues,
        ArmesContondantes,
        ArmesTranchantes,
        Athletisme,
        DefenseCorporelle,

        // Physique - Agilité & Rapidité
        Acrobatie,
        Discretion,
        Esquive,
        ArmesPercantes,
        ProjectilesTir,
        ConduitePilotage,
        Subterfuge,

        // Physique - Constitution
        EndurancePhysique,
        Cardio,
        SystemeImmunitaire,

        // Mental - Intelligence & Érudition
        Academie,
        PremiersSoins,
        MedecineAvancee,
        IngenierieArcanotech,
        TactiqueStrategie,

        // Social - Charisme
        Communication,
        Intimidation,
        Leadership,

        // Instinct & Sagesse
        Observation,
        Ecoute,
        Intuition,
        NatureSurvie,
        Artisanat,

        // Cinquième Force (Magie)
        MagieElementale,
        MagiePrimale,
        MagieEsprit
    }

    [Serializable]
    public class SkillProgressionEntry
    {
        public SkillType Skill;
        public int TrainingLevel; // Chaque niveau = +1 palier de dé (coûte 5 XP)

        public SkillProgressionEntry(SkillType skill, int trainingLevel = 0)
        {
            Skill = skill;
            TrainingLevel = trainingLevel;
        }

        public DiceType CalculateSkillDie(int baseStatRank)
        {
            int effectiveRank = baseStatRank + TrainingLevel;
            return effectiveRank switch
            {
                <= 2 => DiceType.D2,
                3 => DiceType.D4,
                4 or 5 => DiceType.D6,
                6 or 7 => DiceType.D8,
                8 or 9 => DiceType.D10,
                10 => DiceType.D12,
                11 or 12 or 13 => DiceType.TwoD12Plus10, // Utilise la structure échelon
                _ => DiceType.TwoD12Plus10
            };
        }
    }
}
using System;
using Killtime.Core.Dice;

namespace Killtime.Core.Character
{
    public enum SkillType
    {
        // Physique - Combat & Mêlée
        MainsNues,
        ManiementArmes,
        ArmesTranchantes = ManiementArmes,
        ArmesContondantes,
        ArmesPercantes,
        DefenseCorporelle,
        Athletisme,

        // Physique - Agilité & Balistique
        Ballistique,
        ProjectilesTir = Ballistique,
        Esquive,
        Acrobatie,
        Discretion,
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
        public int TrainingLevel;

        public SkillProgressionEntry(SkillType skill, int trainingLevel = 0)
        {
            Skill = skill;
            TrainingLevel = trainingLevel;
        }

        public DiceType CalculateSkillDie(int baseStatRank)
        {
            return SkillDefinitions.CalculateSkillDie(baseStatRank + TrainingLevel);
        }
    }

    public static class SkillDefinitions
    {
        public static DiceType CalculateSkillDie(int effectiveRank)
        {
            return effectiveRank switch
            {
                <= 2 => DiceType.D2,
                3 => DiceType.D4,
                4 or 5 => DiceType.D6,
                6 or 7 => DiceType.D8,
                8 or 9 => DiceType.D10,
                10 => DiceType.D12,
                11 or 12 or 13 => DiceType.TwoD6,
                14 or 15 or 16 => DiceType.TwoD8,
                17 or 18 or 19 => DiceType.TwoD10,
                20 or 21 or 22 => DiceType.TwoD12,
                _ => DiceType.TwoD12Plus10
            };
        }

        public static DiceType StepDownDie(DiceType die)
        {
            return die switch
            {
                DiceType.TwoD12Plus10 => DiceType.TwoD12,
                DiceType.TwoD12 => DiceType.TwoD10,
                DiceType.TwoD10 => DiceType.TwoD8,
                DiceType.TwoD8 => DiceType.TwoD6,
                DiceType.TwoD6 => DiceType.D12,
                DiceType.D12 => DiceType.D10,
                DiceType.D10 => DiceType.D8,
                DiceType.D8 => DiceType.D6,
                DiceType.D6 => DiceType.D4,
                DiceType.D4 => DiceType.D2,
                _ => DiceType.D2
            };
        }

        public static DiceType StepUpDie(DiceType die)
        {
            return die switch
            {
                DiceType.D2 => DiceType.D4,
                DiceType.D4 => DiceType.D6,
                DiceType.D6 => DiceType.D8,
                DiceType.D8 => DiceType.D10,
                DiceType.D10 => DiceType.D12,
                DiceType.D12 => DiceType.TwoD6,
                DiceType.TwoD6 => DiceType.TwoD8,
                DiceType.TwoD8 => DiceType.TwoD10,
                DiceType.TwoD10 => DiceType.TwoD12,
                _ => DiceType.TwoD12Plus10
            };
        }

        public static int GetBaseRank(SkillType skill, Attributes attr)
        {
            return skill switch
            {
                SkillType.MainsNues => (attr.Force + attr.Agilite + 1) / 2,
                SkillType.ManiementArmes => (attr.Force + attr.Agilite + 1) / 2,
                SkillType.ArmesContondantes => attr.Force,
                SkillType.ArmesPercantes => attr.Agilite,
                SkillType.DefenseCorporelle => (attr.Force + attr.Constitution + 1) / 2,
                SkillType.Athletisme => (attr.Force + attr.Constitution + attr.Agilite + 1) / 3,

                SkillType.Ballistique => attr.Agilite,
                SkillType.Esquive => (attr.Agilite + attr.Rapidite + 1) / 2,
                SkillType.Acrobatie => (attr.Agilite + attr.Rapidite + 1) / 2,
                SkillType.Discretion => (attr.Agilite + attr.Intelligence + 1) / 2,
                SkillType.ConduitePilotage => (attr.Agilite + attr.Rapidite + 1) / 2,
                SkillType.Subterfuge => (attr.Agilite + attr.Intelligence + 1) / 2,

                SkillType.EndurancePhysique => (attr.Constitution + attr.Force + 1) / 2,
                SkillType.Cardio => attr.Constitution,
                SkillType.SystemeImmunitaire => attr.Constitution,

                SkillType.Academie => (attr.Erudition + attr.Intelligence + 1) / 2,
                SkillType.PremiersSoins => attr.Erudition,
                SkillType.MedecineAvancee => (attr.Erudition + attr.Intelligence + 1) / 2,
                SkillType.IngenierieArcanotech => (attr.Intelligence + attr.Erudition + 1) / 2,
                SkillType.TactiqueStrategie => attr.Intelligence,

                SkillType.Communication => (attr.Charisme + attr.Intelligence + 1) / 2,
                SkillType.Intimidation => (attr.Charisme + attr.Force + 1) / 2,
                SkillType.Leadership => (attr.Charisme + attr.Intelligence + 1) / 2,

                SkillType.Observation => (attr.Vision + attr.Instinct + 1) / 2,
                SkillType.Ecoute => (attr.Ouie + attr.Instinct + 1) / 2,
                SkillType.Intuition => (attr.Instinct + attr.Intelligence + 1) / 2,
                SkillType.NatureSurvie => (attr.Instinct + attr.Erudition + 1) / 2,
                SkillType.Artisanat => (attr.Instinct + attr.Intelligence + 1) / 2,

                SkillType.MagieElementale or SkillType.MagiePrimale or SkillType.MagieEsprit => (attr.Magie + attr.Intelligence + 1) / 2,

                _ => 3
            };
        }

        public static int GetPrimaryModifier(SkillType skill, Attributes attr)
        {
            return skill switch
            {
                SkillType.MainsNues => Math.Max(attr.Force, attr.Agilite),
                SkillType.ManiementArmes => Math.Max(attr.Force, attr.Agilite),
                SkillType.ArmesContondantes => attr.Force,
                SkillType.ArmesPercantes => attr.Agilite,
                SkillType.Ballistique => attr.Agilite,
                SkillType.Esquive => Math.Max(attr.Agilite, attr.Rapidite),
                SkillType.DefenseCorporelle => Math.Max(attr.Force, attr.Constitution),
                SkillType.PremiersSoins => attr.Erudition,
                SkillType.Intimidation => Math.Max(attr.Charisme, attr.Force),
                SkillType.Communication => attr.Charisme,
                SkillType.MagieElementale or SkillType.MagiePrimale or SkillType.MagieEsprit => attr.Intelligence,
                _ => attr.Agilite
            };
        }

        public static bool IsExclusivelyDefensive(SkillType skill)
        {
            return skill == SkillType.Esquive || skill == SkillType.DefenseCorporelle;
        }

        public static bool HasDefensiveSpecialization(CharacterSheet sheet, SkillType skill)
        {
            if (sheet == null || sheet.UnlockedSpecializations == null) return false;
            if (skill == SkillType.ManiementArmes || skill == SkillType.ArmesTranchantes)
            {
                return sheet.UnlockedSpecializations.Contains("Maniement de l'Épée")
                    || sheet.UnlockedSpecializations.Contains("Escrime")
                    || sheet.UnlockedSpecializations.Contains("Bloquer");
            }
            if (skill == SkillType.MainsNues)
            {
                return sheet.UnlockedSpecializations.Contains("Arts Martiaux")
                    || sheet.UnlockedSpecializations.Contains("Bloquer");
            }
            return false;
        }

        public static string GetDisplayName(SkillType skill)
        {
            return skill switch
            {
                SkillType.MainsNues => "Mains Nues",
                SkillType.ManiementArmes => "Maniement d'Arme",
                SkillType.ArmesContondantes => "Armes Contondantes",
                SkillType.ArmesPercantes => "Armes Perçantes",
                SkillType.Ballistique => "Ballistique / Tir",
                SkillType.Esquive => "Esquive",
                SkillType.DefenseCorporelle => "Défense Corporelle",
                SkillType.Athletisme => "Athlétisme",
                SkillType.PremiersSoins => "Premiers Soins",
                SkillType.Intimidation => "Intimidation",
                SkillType.Communication => "Communication",
                _ => skill.ToString()
            };
        }
    }
}
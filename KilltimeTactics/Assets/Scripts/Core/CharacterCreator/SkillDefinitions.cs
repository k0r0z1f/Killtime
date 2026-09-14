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
            return SkillDefinitions.DieFromTotalSteps(
                SkillDefinitions.CharacteristicSteps(baseStatRank) + TrainingLevel);
        }
    }

    public static class SkillDefinitions
    {
        /// <summary>
        /// RÈGLE DE BASE (Livre II, §6) : une caractéristique donne des NIVEAUX DE DÉS,
        /// pas un dé direct. Échelle : 1-2 → +0, 3-4 → +1, 5-6 → +2, 7-8 → +3, 9-10 → +4.
        /// Chaque entraînement ajoute +1 niveau. Total compté depuis d2
        /// (d2 → d4 → d6 → d8 → d10 → d12 → 2d6 → 2d8 → 2d10 → 2d12 → 2d12+10).
        /// Ex : carac 5 (+2) + 3 entraînements = 5 niveaux → d12.
        /// </summary>
        public static int CharacteristicSteps(int characteristicValue)
        {
            return (Math.Clamp(characteristicValue, 1, 10) - 1) / 2;
        }

        /// <summary>
        /// Convertit un total de niveaux (paliers carac + entraînements) en dé,
        /// en montant depuis d2. Plafonné à 2d12+10.
        /// </summary>
        public static DiceType DieFromTotalSteps(int totalSteps)
        {
            DiceType die = DiceType.D2;
            for (int i = 0; i < totalSteps && die != DiceType.TwoD12Plus10; i++)
            {
                die = StepUpDie(die);
            }
            return die;
        }

        /// <summary>
        /// Ancienne table de rangs (remplacée par l'échelle en paliers).
        /// Conservé pour compatibilité : convertit désormais via CharacteristicSteps.
        /// </summary>
        [Obsolete("Règle en paliers (Livre II §6) : utilisez CharacteristicSteps + DieFromTotalSteps.")]
        public static DiceType CalculateSkillDie(int effectiveRank)
        {
            return DieFromTotalSteps(CharacteristicSteps(effectiveRank));
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

        /// <summary>
        /// RÈGLE GÉNÉRALE « CORPS AUGMENTÉ » (Livre III) : en attaque offensive à mains nues,
        /// un personnage éveillé (MAG &gt; 0) utilise sa Magie comme caractéristique de référence
        /// à la place de la moyenne FOR/AGI (on retient la meilleure des deux, jamais de malus).
        /// Les entraînements Mains Nues s'ajoutent ensuite en paliers. Défense et parades :
        /// inchangées (FOR/AGI). Ex : MAG 5 (+2 paliers) + 3 entraînements = 5 niveaux → d12.
        /// </summary>
        public static bool UsesMagicAugmentation(SkillType skill, Attributes attr, bool isOffensive)
        {
            return isOffensive && skill == SkillType.MainsNues && attr.Magie > 0;
        }

        public static int GetBaseRank(SkillType skill, Attributes attr)
        {
            return GetBaseRank(skill, attr, false);
        }

        public static int GetBaseRank(SkillType skill, Attributes attr, bool isOffensive)
        {
            if (UsesMagicAugmentation(skill, attr, isOffensive))
            {
                int normalRank = (attr.Force + attr.Agilite + 1) / 2;
                return Math.Max(normalRank, attr.Magie);
            }

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

        /// <summary>
        /// RÈGLE CODEX (Livres I-III) : aucun modificateur de caractéristique n'est ajouté au jet.
        /// Les attributs fixent uniquement le rang de base (GetBaseRank) donc le dé lancé.
        /// Seuls les PA injectés et les malus situationnels (visée, états Livre VII) modifient le total.
        /// Conservé pour compatibilité : retourne toujours 0.
        /// </summary>
        [Obsolete("Règle Codex : aucun mod de caractéristique ajouté au jet. Les caracs fixent le dé via GetBaseRank. Utilisez GetStatusModifier.")]
        public static int GetPrimaryModifier(SkillType skill, Attributes attr)
        {
            return 0;
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
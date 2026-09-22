using System;
using System.Collections.Generic;
using Killtime.Core.Dice;

namespace Killtime.Core.Character
{
    public enum SkillType
    {
        // Physique - Combat & Mêlée
        MainsNues,
        ManiementArmes,
        ArmesTranchantes = ManiementArmes,
        /// <summary>
        /// LEGACY : les armes de choc (marteau, masse, gourdin) ne sont plus une
        /// compétence de base. Elles relèvent de la spécialisation
        /// « Armes Contondantes » de <see cref="ManiementArmes"/>.
        /// Valeur conservée pour la compatibilité des sauvegardes : toute logique
        /// (rang de base, entraînement, réserve XP) est redirigée vers ManiementArmes.
        /// Ne plus créer de nœud de compétence pour cette valeur.
        /// </summary>
        [Obsolete("Devenue la spécialisation 'Armes Contondantes' de Maniement d'Arme.")]
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

        /// <summary>
        /// Cases de progression cochées par réussites (Livre I §5).
        /// +1 case par épreuve de cette compétence RÉUSSIE. Échec = 0.
        /// Remise à 0 lors de la conversion en XP lié.
        /// </summary>
        public int ProgressTicks;

        /// <summary>
        /// Banque d'XP LIÉE à cette compétence (Livre I §5).
        /// +1 à chaque fois que la piste (cases = rang de base) est pleine.
        /// Dépensable au coût normal pour cette compétence, au coût doublé ailleurs.
        /// </summary>
        public int ReserveXP;

        public SkillProgressionEntry(SkillType skill, int trainingLevel = 0)
        {
            Skill = skill;
            TrainingLevel = trainingLevel;
            ProgressTicks = 0;
            ReserveXP = 0;
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

        /// <summary>
        /// Compétence de base effective : la valeur legacy ArmesContondantes est
        /// rabattue sur ManiementArmes (spécialisation « Armes Contondantes »).
        /// Les alias (Tranchantes, Projectiles) se résolvent aussi ici.
        /// </summary>
        public static SkillType ResolveBaseSkill(SkillType skill)
        {
#pragma warning disable CS0618
            if (skill == SkillType.ArmesContondantes) return SkillType.ManiementArmes;
#pragma warning restore CS0618
            return skill;
        }

        /// <summary>
        /// Vrai si la valeur est une compétence de base entraînable.
        /// ArmesContondantes (legacy) et les alias ne sont pas des compétences de base.
        /// </summary>
        public static bool IsBaseSkill(SkillType skill)
        {
#pragma warning disable CS0618
            if (skill == SkillType.ArmesContondantes) return false;
#pragma warning restore CS0618
            if (skill == SkillType.ArmesTranchantes) return false;
            if (skill == SkillType.ProjectilesTir) return false;
            return true;
        }

        public static int GetBaseRank(SkillType skill, Attributes attr)
        {
            return GetBaseRank(skill, attr, false);
        }

        public static int GetBaseRank(SkillType skill, Attributes attr, bool isOffensive)
        {
            skill = ResolveBaseSkill(skill);
            if (UsesMagicAugmentation(skill, attr, isOffensive))
            {
                int normalRank = (attr.Force + attr.Agilite + 1) / 2;
                return Math.Max(normalRank, attr.Magie);
            }

            return skill switch
            {
                SkillType.MainsNues => (attr.Force + attr.Agilite + 1) / 2,
                SkillType.ManiementArmes => (attr.Force + attr.Agilite + 1) / 2,
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

        /// <summary>
        /// Compétence d'arme blanche (épée, marteau, masse, pique...) : Maniement
        /// d'Arme ou Armes Perçantes. La valeur legacy ArmesContondantes est
        /// rabattue sur ManiementArmes (spécialisation « Armes Contondantes »).
        /// </summary>
        public static bool IsMeleeWeaponSkill(SkillType skill)
        {
            skill = ResolveBaseSkill(skill);
            return skill == SkillType.ManiementArmes || skill == SkillType.ArmesPercantes;
        }

        /// <summary>
        /// Compétence d'attaque au contact : Mains Nues ou arme blanche.
        /// </summary>
        public static bool IsMeleeAttackSkill(SkillType skill)
        {
            skill = ResolveBaseSkill(skill);
            return skill == SkillType.MainsNues
                || skill == SkillType.ManiementArmes
                || skill == SkillType.ArmesPercantes;
        }

        public static bool HasDefensiveSpecialization(CharacterSheet sheet, SkillType skill)
        {
            if (sheet == null || sheet.UnlockedSpecializations == null) return false;
            skill = ResolveBaseSkill(skill);
            if (skill == SkillType.ManiementArmes || skill == SkillType.ArmesTranchantes)
            {
                return sheet.UnlockedSpecializations.Contains("Maniement de l'Épée")
                    || sheet.UnlockedSpecializations.Contains("Armes Contondantes")
                    || sheet.UnlockedSpecializations.Contains("Hache de Guerre")
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
            skill = ResolveBaseSkill(skill);
            return skill switch
            {
                SkillType.MainsNues => "Mains Nues",
                SkillType.ManiementArmes => "Maniement d'Arme",
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

        private static Dictionary<string, SkillType> _displayNameLookup;

        /// <summary>
        /// Retrouve une compétence depuis son nom tel qu'affiché dans les logs
        /// ("Armes Perçantes", "Ballistique / Tir", "Esquive"… ou nom brut).
        /// Le nom legacy "Armes Contondantes" est rabattu sur ManiementArmes.
        /// </summary>
        public static bool TryParseDisplayName(string name, out SkillType skill)
        {
            skill = default;
            if (string.IsNullOrWhiteSpace(name)) return false;
            if (_displayNameLookup == null)
            {
                _displayNameLookup = new Dictionary<string, SkillType>(StringComparer.OrdinalIgnoreCase);
                foreach (SkillType s in Enum.GetValues(typeof(SkillType)))
                {
                    string disp = GetDisplayName(s);
                    if (!_displayNameLookup.ContainsKey(disp)) _displayNameLookup[disp] = s;
                    string raw = s.ToString();
                    if (!_displayNameLookup.ContainsKey(raw)) _displayNameLookup[raw] = s;
                }
                // Compatibilité des anciens logs : « Armes Contondantes » → Maniement d'Arme.
                _displayNameLookup["Armes Contondantes"] = SkillType.ManiementArmes;
            }
            if (_displayNameLookup.TryGetValue(name.Trim(), out skill))
            {
                skill = ResolveBaseSkill(skill);
                return true;
            }
            return false;
        }

        private static string AvgRank(string a, int va, string b, int vb)
        {
            return $"({a} {va}+{b} {vb})/2={(va + vb + 1) / 2}";
        }

        /// <summary>
        /// Abréviations des caractéristiques associées à une compétence
        /// (« FOR+AGI », « AGI », « MAG+INT »…), sans valeurs.
        /// Miroir de GetBaseRank : toute formule modifiée là-bas doit l'être ici aussi.
        /// Affiché dans la Voûte Céleste (étiquettes et inspection).
        /// </summary>
        public static string GetAssociatedAttributeNames(SkillType skill)
        {
            skill = ResolveBaseSkill(skill);
            switch (skill)
            {
                case SkillType.MainsNues:
                case SkillType.ManiementArmes:
                    return "FOR+AGI";
                case SkillType.ArmesPercantes:
                case SkillType.Ballistique:
                    return "AGI";
                case SkillType.DefenseCorporelle:
                    return "FOR+CON";
                case SkillType.Athletisme:
                    return "FOR+CON+AGI";
                case SkillType.Esquive:
                case SkillType.Acrobatie:
                case SkillType.ConduitePilotage:
                    return "AGI+RAP";
                case SkillType.Discretion:
                case SkillType.Subterfuge:
                    return "AGI+INT";
                case SkillType.EndurancePhysique:
                    return "CON+FOR";
                case SkillType.Cardio:
                case SkillType.SystemeImmunitaire:
                    return "CON";
                case SkillType.Academie:
                case SkillType.MedecineAvancee:
                    return "ÉRU+INT";
                case SkillType.PremiersSoins:
                    return "ÉRU";
                case SkillType.IngenierieArcanotech:
                    return "INT+ÉRU";
                case SkillType.TactiqueStrategie:
                    return "INT";
                case SkillType.Communication:
                case SkillType.Leadership:
                    return "CHA+INT";
                case SkillType.Intimidation:
                    return "CHA+FOR";
                case SkillType.Observation:
                    return "VUE+INS";
                case SkillType.Ecoute:
                    return "OUIE+INS";
                case SkillType.Intuition:
                case SkillType.Artisanat:
                    return "INS+INT";
                case SkillType.NatureSurvie:
                    return "INS+ÉRU";
                case SkillType.MagieElementale:
                case SkillType.MagiePrimale:
                case SkillType.MagieEsprit:
                    return "MAG+INT";
                default:
                    return "—";
            }
        }

        /// <summary>
        /// Décompose le rang de base en chaîne concise pour les tooltips
        /// (ex "AGI 5", "(FOR 4+AGI 6)/2=5", "MAG 5 ★"). Miroir de GetBaseRank :
        /// toute formule modifiée là-bas doit l'être ici aussi.
        /// </summary>
        public static string DescribeBaseRank(SkillType skill, Attributes attr, bool isOffensive)
        {
            skill = ResolveBaseSkill(skill);
            if (UsesMagicAugmentation(skill, attr, isOffensive))
            {
                int normalRank = (attr.Force + attr.Agilite + 1) / 2;
                if (attr.Magie >= normalRank) return $"MAG {attr.Magie} ★";
                return $"(FOR {attr.Force}+AGI {attr.Agilite})/2={normalRank}";
            }

            switch (skill)
            {
                case SkillType.MainsNues:
                case SkillType.ManiementArmes:
                    return AvgRank("FOR", attr.Force, "AGI", attr.Agilite);
                case SkillType.ArmesPercantes:
                    return $"AGI {attr.Agilite}";
                case SkillType.DefenseCorporelle:
                    return AvgRank("FOR", attr.Force, "CON", attr.Constitution);
                case SkillType.Athletisme:
                    return $"(FOR {attr.Force}+CON {attr.Constitution}+AGI {attr.Agilite})/3={(attr.Force + attr.Constitution + attr.Agilite + 1) / 3}";
                case SkillType.Ballistique:
                    return $"AGI {attr.Agilite}";
                case SkillType.Esquive:
                case SkillType.Acrobatie:
                case SkillType.ConduitePilotage:
                    return AvgRank("AGI", attr.Agilite, "RAP", attr.Rapidite);
                case SkillType.Discretion:
                case SkillType.Subterfuge:
                    return AvgRank("AGI", attr.Agilite, "INT", attr.Intelligence);
                case SkillType.EndurancePhysique:
                    return AvgRank("CON", attr.Constitution, "FOR", attr.Force);
                case SkillType.Cardio:
                case SkillType.SystemeImmunitaire:
                    return $"CON {attr.Constitution}";
                case SkillType.Academie:
                case SkillType.MedecineAvancee:
                    return AvgRank("ERU", attr.Erudition, "INT", attr.Intelligence);
                case SkillType.PremiersSoins:
                    return $"ERU {attr.Erudition}";
                case SkillType.IngenierieArcanotech:
                    return AvgRank("INT", attr.Intelligence, "ERU", attr.Erudition);
                case SkillType.TactiqueStrategie:
                    return $"INT {attr.Intelligence}";
                case SkillType.Communication:
                case SkillType.Leadership:
                    return AvgRank("CHA", attr.Charisme, "INT", attr.Intelligence);
                case SkillType.Intimidation:
                    return AvgRank("CHA", attr.Charisme, "FOR", attr.Force);
                case SkillType.Observation:
                    return AvgRank("VUE", attr.Vision, "INS", attr.Instinct);
                case SkillType.Ecoute:
                    return AvgRank("OUIE", attr.Ouie, "INS", attr.Instinct);
                case SkillType.Intuition:
                case SkillType.Artisanat:
                    return AvgRank("INS", attr.Instinct, "INT", attr.Intelligence);
                case SkillType.NatureSurvie:
                    return AvgRank("INS", attr.Instinct, "ERU", attr.Erudition);
                case SkillType.MagieElementale:
                case SkillType.MagiePrimale:
                case SkillType.MagieEsprit:
                    return AvgRank("MAG", attr.Magie, "INT", attr.Intelligence);
                default:
                    return "Fixe 3";
            }
        }
    }
}
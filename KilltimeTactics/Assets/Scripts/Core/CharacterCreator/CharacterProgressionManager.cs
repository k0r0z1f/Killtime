using System;
using Killtime.Core.Arcanotech;

namespace Killtime.Core.Character
{
    /// <summary>
    /// Moteur d'évolution organique et de dépense d'XP (Livre I, III & IV).
    /// </summary>
    public class CharacterProgressionManager
    {
        public const int XP_COST_TRAINING = 5;
        public const int XP_COST_SPECIALIZATION = 5;

        public static bool GrantXP(CharacterSheet sheet, int amount)
        {
            if (amount <= 0) return false;
            sheet.AvailableXP += amount;
            sheet.TotalEarnedXP += amount;
            return true;
        }

        /// <summary>
        /// Augmente le niveau d'entraînement d'une compétence d'un cran (+1 palier de dé).
        /// </summary>
        public static bool TrainSkill(CharacterSheet sheet, SkillType skill, out string message)
        {
            if (sheet.AvailableXP < XP_COST_TRAINING)
            {
                message = $"XP insuffisant ({sheet.AvailableXP}/{XP_COST_TRAINING} requis).";
                return false;
            }

            var entry = sheet.GetSkill(skill);
            sheet.AvailableXP -= XP_COST_TRAINING;
            entry.TrainingLevel++;

            message = $"Entraînement acquis pour {skill} ! Niveau actuel : +{entry.TrainingLevel} dé(s).";
            return true;
        }

        /// <summary>
        /// Débloque une spécialisation martiale, académique ou sociale (5 XP).
        /// </summary>
        public static bool UnlockSpecialization(CharacterSheet sheet, string specializationName, out string message)
        {
            if (sheet.UnlockedSpecializations.Contains(specializationName))
            {
                message = "Spécialisation déjà acquise.";
                return false;
            }

            if (sheet.AvailableXP < XP_COST_SPECIALIZATION)
            {
                message = $"XP insuffisant ({sheet.AvailableXP}/{XP_COST_SPECIALIZATION} requis).";
                return false;
            }

            sheet.AvailableXP -= XP_COST_SPECIALIZATION;
            sheet.UnlockedSpecializations.Add(specializationName);

            message = $"Spécialisation '{specializationName}' débloquée !";
            return true;
        }

        /// <summary>
        /// Apprend un sort conçu dans l'Atelier Arcanotech (Règle : Coût XP = Coût PA).
        /// </summary>
        public static bool LearnModularSpell(CharacterSheet sheet, NythariteSpell spell, out string message)
        {
            if (sheet.AvailableXP < spell.CreationXpCost)
            {
                message = $"XP insuffisant ({sheet.AvailableXP}/{spell.CreationXpCost} requis) pour canaliser {spell.Name}.";
                return false;
            }

            sheet.AvailableXP -= spell.CreationXpCost;
            sheet.LearnedSpells.Add(spell);

            message = $"Sort '{spell.Name}' gravé dans les voies neurales ! (Coût Combat : {spell.ActionPointCost} PA).";
            return true;
        }
    }
}
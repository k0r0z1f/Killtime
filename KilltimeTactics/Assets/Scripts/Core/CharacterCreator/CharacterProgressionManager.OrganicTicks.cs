using System;
using System.Collections.Generic;
using Killtime.Core.Arcanotech;

namespace Killtime.Core.Character
{
    public partial class CharacterProgressionManager
    {
        // CharacterProgressionManager.OrganicTicks.cs — Gains organiques (XP libre/lie, coches, challenges) + entrainements.

        // ------------------------------------------------------------------
        // GAINS : XP libre (MJ) vs XP lié (progression organique)
        // ------------------------------------------------------------------

        /// <summary>
        /// Don de XP LIBRE (MJ, récompense générique). Dépensable partout au coût normal.
        /// N'est PAS compté dans l'XP total (seul le dépensé compte, Livre I §5.5).
        /// </summary>
        public static bool GrantXP(CharacterSheet sheet, int amount)
        {
            if (sheet == null || amount <= 0) return false;
            sheet.AvailableXP += amount;
            sheet.TotalEarnedXP += amount;
            return true;
        }

        /// <summary>
        /// Remet l'XP LIBRE à 0 (outil MJ / création). Ne touche ni aux banques liées,
        /// ni aux entraînements, ni à l'XP total (dépensé). L'historique
        /// <c>TotalEarnedXP</c> est conservé. Retourne le montant écarté.
        /// </summary>
        public static bool ResetFreeXP(CharacterSheet sheet, out string message, out int discarded)
        {
            discarded = 0;
            if (sheet == null)
            {
                message = "Fiche invalide.";
                return false;
            }
            discarded = System.Math.Max(0, sheet.AvailableXP);
            if (discarded <= 0)
            {
                message = "XP libre déjà à 0 : rien à remettre à zéro.";
                return true;
            }
            sheet.AvailableXP = 0;
            message = $"XP libre remis à 0 ({discarded} XP écarté(s)). Banques liées et XP total inchangés.";
            return true;
        }

        /// <summary>
        /// Taille de la piste de progression = rang de base de la compétence (min 1, max 10).
        /// Ex. base 3 → 3 cases, base 5 → 5 cases (Livre I §5.2).
        /// </summary>
        public static int GetProgressMax(int baseRank)
        {
            return Math.Clamp(baseRank, 1, 10);
        }

        /// <summary>
        /// Rang de base effectif (attributs + espèces) pour calibrer la piste.
        /// </summary>
        public static int GetBaseRankForSheet(CharacterSheet sheet, SkillType skill, bool isOffensive = false)
        {
            if (sheet == null) return 3;
            var eff = sheet.GetEffectiveAttributes();
            return SkillDefinitions.GetBaseRank(skill, eff, isOffensive);
        }

        /// <summary>
        /// Enregistre UNE réussite d'épreuve de compétence : coche +1 case.
        /// Si la piste devient pleine → +1 XP lié dans cette compétence, piste remise à 0.
        /// Retourne true si la coche a eu lieu. `converted` = true si un XP a été gagné.
        /// </summary>
        public static bool RegisterSuccessfulSkillUse(CharacterSheet sheet, SkillType skill, out string message, out bool converted, bool isOffensive = false)
        {
            message = null;
            converted = false;
            if (sheet == null) return false;
            if (!Rules.CoreRulesConfig.Instance.EnableSkillProgressTicks) return false;

            var entry = sheet.GetSkill(skill);
            int baseRank = GetBaseRankForSheet(sheet, skill, isOffensive);
            int max = GetProgressMax(baseRank);

            // Sécurise les anciennes sauvegardes (champs à 0 par défaut JSON).
            if (entry.ProgressTicks < 0) entry.ProgressTicks = 0;
            if (entry.ReserveXP < 0) entry.ReserveXP = 0;
            if (entry.ProgressTicks >= max) entry.ProgressTicks = max - 1; // évite le blocage plein

            entry.ProgressTicks++;
            if (entry.ProgressTicks >= max)
            {
                entry.ProgressTicks = 0;
                entry.ReserveXP++;
                sheet.TotalEarnedXP++;
                converted = true;
                message = $"▸ Progression {SkillDefinitions.GetDisplayName(skill)} : piste {max}/{max} → +1 XP lié (banque {entry.ReserveXP}).";
            }
            else
            {
                message = $"▸ Progression {SkillDefinitions.GetDisplayName(skill)} : {entry.ProgressTicks}/{max} cases.";
            }
            return true;
        }

        /// <summary>
        /// Surcharge simplifiée (défaut non-offensif).
        /// </summary>
        public static bool RegisterSuccessfulSkillUse(CharacterSheet sheet, SkillType skill, out string message)
        {
            return RegisterSuccessfulSkillUse(sheet, skill, out message, out _, false);
        }

        /// <summary>
        /// Récompense de challenge (Livre II §9) : `ticks` coches à répartir dans la
        /// compétence utilisée pour l'étape. Chaque piste complétée convertit en XP lié.
        /// </summary>
        public static int RegisterChallengeTicks(CharacterSheet sheet, SkillType skill, int ticks, bool isOffensive = false)
        {
            if (sheet == null || ticks <= 0) return 0;
            int gained = 0;
            for (int i = 0; i < ticks; i++)
            {
                RegisterSuccessfulSkillUse(sheet, skill, out _, out bool conv, isOffensive);
                if (conv) gained++;
            }
            return gained;
        }


        // ------------------------------------------------------------------
        // INVESTISSEMENTS
        // ------------------------------------------------------------------

        /// <summary>
        /// Augmente le niveau d'entraînement d'une compétence (+1 palier de dé). Plafond : 3.
        /// Coût nominal 5 XP liés à CETTE compétence (ou libre) ; XP d'une autre compétence : x2.
        /// Les entraînements gratuits d'Érudition passent par <see cref="TrainSkillFree"/>
        /// (0 XP, hors XP total).
        /// </summary>
        public static bool TrainSkill(CharacterSheet sheet, SkillType skill, out string message)
        {
            if (!IsSkillAccessible(sheet, skill))
            {
                message = $"[RESTRICTION D'ÂME] {sheet.Name} n'a aucun accès à {SkillDefinitions.GetDisplayName(skill)} (Affinité Primordiale Pure).";
                return false;
            }

            var entry = sheet.GetSkill(skill);

            if (entry.TrainingLevel >= MAX_SKILL_TRAINING)
            {
                message = $"Plafond d'entraînement atteint pour {SkillDefinitions.GetDisplayName(skill)} (+{MAX_SKILL_TRAINING} max).";
                return false;
            }

            if (!SpendForSkill(sheet, skill, XP_COST_TRAINING, out string funding))
            {
                int freeLeft = sheet != null ? sheet.GetFreeTrainingsRemaining() : 0;
                string freeHint = freeLeft > 0 ? $" Il reste {freeLeft} entraînement(s) gratuit(s) d'Érudition." : "";
                message = $"XP insuffisant : {funding} (coût {XP_COST_TRAINING} XP liés {SkillDefinitions.GetDisplayName(skill)} ou libre, {XP_COST_TRAINING * OFF_SKILL_MULTIPLIER} si croisé).{freeHint}";
                return false;
            }

            entry.TrainingLevel++;
            message = $"Entraînement acquis pour {SkillDefinitions.GetDisplayName(skill)} ! Niveau actuel : +{entry.TrainingLevel} dé(s). [{funding}]";
            return true;
        }

        // ------------------------------------------------------------------
        // ENTRAÎNEMENTS GRATUITS D'ÉRUDITION (Livre I, Étape 5)
        // ------------------------------------------------------------------

        /// <summary>
        /// Budget d'entraînements gratuits = Érudition effective (espèce incluse).
        /// </summary>
        public static int GetFreeTrainingBudget(CharacterSheet sheet)
        {
            if (sheet == null) return 0;
            return sheet.GetFreeTrainingBudget();
        }

        /// <summary>
        /// Entraînements gratuits restants = budget − déjà consommés (plancher 0).
        /// Chaque +1 Érudition ouvre automatiquement +1 gratuit (budget dérivé).
        /// </summary>
        public static int GetFreeTrainingsRemaining(CharacterSheet sheet)
        {
            if (sheet == null) return 0;
            return sheet.GetFreeTrainingsRemaining();
        }

        /// <summary>
        /// Applique +1 niveau d'entraînement GRATUIT (Érudition) sur une compétence.
        /// Plafond +3 par compétence, budget limité (Érudition − déjà consommés).
        /// Ne coûte aucun XP et ne compte jamais dans l'XP total (<c>TotalSpentXP</c> inchangé).
        /// </summary>
        public static bool TrainSkillFree(CharacterSheet sheet, SkillType skill, out string message)
        {
            if (sheet == null)
            {
                message = "Fiche invalide.";
                return false;
            }

            if (!IsSkillAccessible(sheet, skill))
            {
                message = $"[RESTRICTION D'ÂME] {sheet.Name} n'a aucun accès à {SkillDefinitions.GetDisplayName(skill)} (Affinité Primordiale Pure).";
                return false;
            }

            var entry = sheet.GetSkill(skill);

            if (entry.TrainingLevel >= MAX_SKILL_TRAINING)
            {
                message = $"Plafond d'entraînement atteint pour {SkillDefinitions.GetDisplayName(skill)} (+{MAX_SKILL_TRAINING} max).";
                return false;
            }

            int remaining = sheet.GetFreeTrainingsRemaining();
            if (remaining <= 0)
            {
                message = $"Plus d'entraînement gratuit disponible (budget Érudition {sheet.GetFreeTrainingBudget()}, {Math.Max(0, sheet.FreeTrainingsUsed)} consommé(s)). Augmentez l'Érudition (+1 = +1 gratuit) ou payez en XP.";
                return false;
            }

            entry.TrainingLevel++;
            sheet.FreeTrainingsUsed = Math.Max(0, sheet.FreeTrainingsUsed) + 1;
            int left = sheet.GetFreeTrainingsRemaining();
            message = $"Entraînement GRATUIT (Érudition) appliqué à {SkillDefinitions.GetDisplayName(skill)} ! Niveau actuel : +{entry.TrainingLevel} dé(s). Gratuits restants : {left}. (0 XP, hors XP total).";
            return true;
        }

    }
}

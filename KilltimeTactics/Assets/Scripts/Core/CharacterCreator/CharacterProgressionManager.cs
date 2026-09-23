using System;
using System.Collections.Generic;
using Killtime.Core.Arcanotech;

namespace Killtime.Core.Character
{
    /// <summary>
    /// Moteur d'évolution organique et de dépense d'XP (Livre I §5, Livres III &amp; IV).
    ///
    /// RÈGLE OFFICIELLE (Livre I §5) :
    /// - Chaque RÉUSSITE d'épreuve de compétence coche +1 case de progression DANS CETTE compétence.
    /// - Taille de la piste = rang de base de la compétence (ex. base 3 → 3 cases, base 5 → 5 cases).
    /// - Piste pleine → +1 XP LIÉ à cette compétence (banque), piste remise à 0.
    /// - Dépense au coût normal si l'XP provient de la compétence améliorée (ou XP libre),
    ///   coût DOUBLÉ (x2) si l'XP provient d'une autre compétence.
    /// - XP TOTAL du personnage = somme des XP DÉPENSÉS en améliorations (TotalSpentXP),
    ///   jamais les banques. Les banques (réserves liées + XP libre) sont l'argent en poche.
    /// </summary>
    public class CharacterProgressionManager
    {
        public static int XP_COST_TRAINING => Rules.CoreRulesConfig.Instance.XPCostTraining;
        public static int XP_COST_SPECIALIZATION => Rules.CoreRulesConfig.Instance.XPCostSpecialization;
        public static int OFF_SKILL_MULTIPLIER => Math.Max(2, Rules.CoreRulesConfig.Instance.XPOffSkillCostMultiplier);

        public const int MAX_SKILL_TRAINING = 3;

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
        // DÉPENSE : coût normal (associé / libre) vs doublé (croisé)
        // ------------------------------------------------------------------

        /// <summary>
        /// Coût effectif prélevé sur une source donnée pour une cible donnée.
        /// Source associée (== cible) ou libre (null) → coût normal.
        /// Source liée différente → coût doublé (OFF_SKILL_MULTIPLIER).
        /// </summary>
        public static int GetEffectiveCost(int baseCost, SkillType? sourceSkill, SkillType? targetSkill)
        {
            if (baseCost <= 0) return 0;
            if (sourceSkill == null) return baseCost; // XP libre : jamais surtaxé
            if (targetSkill == null) return baseCost;
            if (sourceSkill.Value.Equals(targetSkill.Value)) return baseCost;
            return baseCost * OFF_SKILL_MULTIPLIER;
        }

        private struct FundingPlan
        {
            public int TakeFromTarget;
            public int TakeFromFree;
            public int TakeFromOthers; // montant PRÉLEVÉ dans les autres réserves (déjà doublé)
            public int TotalLevied;
            public bool Affordable;
        }

        /// <summary>
        /// Calcule le prélèvement optimal : priorité réserve associée (1:1),
        /// puis XP libre (1:1), puis autres réserves (2:1).
        /// </summary>
        private static FundingPlan PlanSpend(CharacterSheet sheet, SkillType targetSkill, int baseCost)
        {
            var plan = new FundingPlan { Affordable = false };
            if (sheet == null || baseCost <= 0) return plan;

            int need = baseCost;
            var target = sheet.GetSkill(targetSkill);
            int availTarget = Math.Max(0, target.ReserveXP);
            int availFree = Math.Max(0, sheet.AvailableXP);

            int takeTarget = Math.Min(availTarget, need);
            need -= takeTarget;

            int takeFree = Math.Min(availFree, need);
            need -= takeFree;

            int takeOthers = 0;
            if (need > 0)
            {
                int needFromOthers = need * OFF_SKILL_MULTIPLIER;
                int availOthers = 0;
                if (sheet.Skills != null)
                    for (int i = 0; i < sheet.Skills.Count; i++)
                    {
                        var e = sheet.Skills[i];
                        if (e == null || e.Skill.Equals(targetSkill)) continue;
                        availOthers += Math.Max(0, e.ReserveXP);
                    }
                if (availOthers < needFromOthers) return plan; // insuffisant
                takeOthers = needFromOthers;
                need = 0;
            }

            plan.TakeFromTarget = takeTarget;
            plan.TakeFromFree = takeFree;
            plan.TakeFromOthers = takeOthers;
            plan.TotalLevied = takeTarget + takeFree + takeOthers;
            plan.Affordable = true;
            return plan;
        }

        /// <summary>
        /// Fonds réellement mobilisables pour une cible (en valeur d'achat) :
        /// réserve associée + libre + plancher(autres / multiplicateur).
        /// </summary>
        public static int GetSpendableFor(CharacterSheet sheet, SkillType targetSkill)
        {
            if (sheet == null) return 0;
            sheet.MigrateLegacyBluntSkill();
            int target = Math.Max(0, sheet.GetSkill(targetSkill).ReserveXP);
            int free = Math.Max(0, sheet.AvailableXP);
            int others = 0;
            if (sheet.Skills != null)
                for (int i = 0; i < sheet.Skills.Count; i++)
                {
                    var e = sheet.Skills[i];
                    if (e == null || e.Skill.Equals(targetSkill)) continue;
                    others += Math.Max(0, e.ReserveXP);
                }
            return target + free + others / OFF_SKILL_MULTIPLIER;
        }

        /// <summary>
        /// Prélève `baseCost` (valeur d'achat) pour la compétence cible selon la priorité
        /// associé → libre → autres (x2). Met à jour TotalSpentXP du MONTANT PRÉLEVÉ
        /// (XP total = dépensé, Livre I §5.5). Atomique : rien si insuffisant.
        /// </summary>
        public static bool SpendForSkill(CharacterSheet sheet, SkillType targetSkill, int baseCost, out string breakdown)
        {
            breakdown = null;
            if (sheet == null || baseCost <= 0) return false;
            sheet.MigrateLegacyBluntSkill();
            var plan = PlanSpend(sheet, targetSkill, baseCost);
            if (!plan.Affordable)
            {
                int have = GetSpendableFor(sheet, targetSkill);
                breakdown = $"Fonds insuffisants pour {SkillDefinitions.GetDisplayName(targetSkill)} ({have}/{baseCost} mobilisables ; autres banques comptent x{OFF_SKILL_MULTIPLIER}).";
                return false;
            }

            var target = sheet.GetSkill(targetSkill);
            target.ReserveXP -= plan.TakeFromTarget;
            sheet.AvailableXP -= plan.TakeFromFree;

            int rest = plan.TakeFromOthers;
            if (rest > 0 && sheet.Skills != null)
                for (int i = 0; i < sheet.Skills.Count && rest > 0; i++)
                {
                    var e = sheet.Skills[i];
                    if (e == null || e.Skill.Equals(targetSkill)) continue;
                    int take = Math.Min(Math.Max(0, e.ReserveXP), rest);
                    e.ReserveXP -= take;
                    rest -= take;
                }

            sheet.TotalSpentXP += plan.TotalLevied;

            var parts = new List<string>();
            if (plan.TakeFromTarget > 0) parts.Add($"{plan.TakeFromTarget} XP lié {SkillDefinitions.GetDisplayName(targetSkill)}");
            if (plan.TakeFromFree > 0) parts.Add($"{plan.TakeFromFree} XP libre");
            if (plan.TakeFromOthers > 0) parts.Add($"{plan.TakeFromOthers} XP croisé (x{OFF_SKILL_MULTIPLIER})");
            breakdown = string.Join(" + ", parts) + $" = {plan.TotalLevied} XP dépensés (XP total {sheet.TotalSpentXP}).";
            return true;
        }

        public static int GetAttributeUpgradeCost(int currentVal)
        {
            if (currentVal < 1 || currentVal >= 10) return 0;
            return currentVal + 1;
        }

        public static List<SkillType> GetSkillsForAttribute(int attrIndex)
        {
            return attrIndex switch
            {
                0 => new List<SkillType> { SkillType.MainsNues, SkillType.ManiementArmes, SkillType.Athletisme, SkillType.DefenseCorporelle },
                1 => new List<SkillType> { SkillType.MainsNues, SkillType.ManiementArmes, SkillType.ArmesPercantes, SkillType.Ballistique, SkillType.Esquive, SkillType.Athletisme },
                2 => new List<SkillType> { SkillType.Athletisme, SkillType.DefenseCorporelle, SkillType.EndurancePhysique, SkillType.Cardio, SkillType.SystemeImmunitaire },
                3 => new List<SkillType> { SkillType.Esquive, SkillType.Athletisme },
                4 => new List<SkillType> { SkillType.TactiqueStrategie, SkillType.IngenierieArcanotech, SkillType.MedecineAvancee, SkillType.Academie, SkillType.Communication, SkillType.Intuition },
                5 => new List<SkillType> { SkillType.Academie, SkillType.MedecineAvancee, SkillType.IngenierieArcanotech },
                6 => new List<SkillType> { SkillType.Communication, SkillType.Intimidation, SkillType.Leadership },
                7 => new List<SkillType> { SkillType.Observation, SkillType.Intuition, SkillType.Artisanat },
                8 => new List<SkillType> { SkillType.MagiePrimale, SkillType.MagieElementale, SkillType.MagieEsprit },
                _ => new List<SkillType>()
            };
        }

        public static int GetSpendableForAttribute(CharacterSheet sheet, int attrIndex)
        {
            if (sheet == null) return 0;
            sheet.MigrateLegacyBluntSkill();
            var associated = GetSkillsForAttribute(attrIndex);

            int associatedXP = 0;
            int otherXP = 0;

            if (sheet.Skills != null)
            {
                for (int i = 0; i < sheet.Skills.Count; i++)
                {
                    var e = sheet.Skills[i];
                    if (e == null) continue;
                    int r = Math.Max(0, e.ReserveXP);
                    if (associated.Contains(e.Skill))
                        associatedXP += r;
                    else
                        otherXP += r;
                }
            }

            int free = Math.Max(0, sheet.AvailableXP);
            return associatedXP + free + (otherXP / OFF_SKILL_MULTIPLIER);
        }

        public static bool UpgradeAttribute(CharacterSheet sheet, int attrIndex, out string message)
        {
            message = null;
            if (sheet == null)
            {
                message = "Fiche invalide.";
                return false;
            }

            int currentVal = sheet.GetBaseAttributeValue(attrIndex);
            if (currentVal >= 10)
            {
                message = $"Plafond absolu de 10 déjà atteint pour cet attribut ({currentVal}/10).";
                return false;
            }

            int baseCost = GetAttributeUpgradeCost(currentVal);
            if (baseCost <= 0)
            {
                message = "Coût d'amélioration invalide.";
                return false;
            }

            sheet.MigrateLegacyBluntSkill();
            var associated = GetSkillsForAttribute(attrIndex);

            int need = baseCost;
            int takeAssociated = 0;
            int takeFree = 0;
            int takeOthers = 0;

            if (sheet.Skills != null)
            {
                for (int i = 0; i < sheet.Skills.Count && need > 0; i++)
                {
                    var e = sheet.Skills[i];
                    if (e == null || !associated.Contains(e.Skill)) continue;
                    int avail = Math.Max(0, e.ReserveXP);
                    int take = Math.Min(avail, need);
                    takeAssociated += take;
                    need -= take;
                }
            }

            int availFree = Math.Max(0, sheet.AvailableXP);
            takeFree = Math.Min(availFree, need);
            need -= takeFree;

            if (need > 0)
            {
                int needFromOthers = need * OFF_SKILL_MULTIPLIER;
                int availOthers = 0;
                if (sheet.Skills != null)
                {
                    for (int i = 0; i < sheet.Skills.Count; i++)
                    {
                        var e = sheet.Skills[i];
                        if (e == null || associated.Contains(e.Skill)) continue;
                        availOthers += Math.Max(0, e.ReserveXP);
                    }
                }

                if (availOthers < needFromOthers)
                {
                    int have = GetSpendableForAttribute(sheet, attrIndex);
                    string attrName = GetAttributeName(attrIndex);
                    message = $"XP insuffisant pour augmenter {attrName} de {currentVal} à {currentVal + 1} ({have}/{baseCost} XP mobilisables ; autres banques comptent x{OFF_SKILL_MULTIPLIER}).";
                    return false;
                }

                takeOthers = needFromOthers;
                need = 0;
            }

            int totalLevied = takeAssociated + takeFree + takeOthers;

            int restAssoc = takeAssociated;
            if (restAssoc > 0 && sheet.Skills != null)
            {
                for (int i = 0; i < sheet.Skills.Count && restAssoc > 0; i++)
                {
                    var e = sheet.Skills[i];
                    if (e == null || !associated.Contains(e.Skill)) continue;
                    int take = Math.Min(Math.Max(0, e.ReserveXP), restAssoc);
                    e.ReserveXP -= take;
                    restAssoc -= take;
                }
            }

            sheet.AvailableXP -= takeFree;

            int restOthers = takeOthers;
            if (restOthers > 0 && sheet.Skills != null)
            {
                for (int i = 0; i < sheet.Skills.Count && restOthers > 0; i++)
                {
                    var e = sheet.Skills[i];
                    if (e == null || associated.Contains(e.Skill)) continue;
                    int take = Math.Min(Math.Max(0, e.ReserveXP), restOthers);
                    e.ReserveXP -= take;
                    restOthers -= take;
                }
            }

            sheet.TotalSpentXP += totalLevied;

            int nextVal = currentVal + 1;
            sheet.SetBaseAttributeValue(attrIndex, nextVal);

            if (sheet.AttributeUpgradesPurchased == null || sheet.AttributeUpgradesPurchased.Length < 9)
            {
                var old = sheet.AttributeUpgradesPurchased;
                sheet.AttributeUpgradesPurchased = new int[9];
                if (old != null) Array.Copy(old, sheet.AttributeUpgradesPurchased, Math.Min(old.Length, 9));
            }
            sheet.AttributeUpgradesPurchased[attrIndex]++;

            string name = GetAttributeName(attrIndex);
            message = $"★ {name} augmenté à {nextVal} ! (Coût : {baseCost} XP payé via {totalLevied} XP prélevé(s)). XP Total : {sheet.TotalSpentXP}.";
            return true;
        }

        public static string GetAttributeName(int attrIndex)
        {
            return attrIndex switch
            {
                0 => "Force (FOR)",
                1 => "Agilité (AGI)",
                2 => "Constitution (CON)",
                3 => "Rapidité (RAP)",
                4 => "Intelligence (INT)",
                5 => "Érudition (ÉRU)",
                6 => "Charisme (CHA)",
                7 => "Instinct (INS)",
                8 => "Magie (5e Force)",
                _ => "Attribut"
            };
        }

        public static bool IsPresetStartingSpec(CharacterSheet sheet, string specName)
        {
            if (sheet == null || string.IsNullOrWhiteSpace(sheet.ActiveClassId) || string.IsNullOrWhiteSpace(specName))
                return false;
            try
            {
                var preset = Classes.CharacterClassCatalog.GetById(sheet.ActiveClassId);
                if (preset?.StartingSpecializations != null)
                {
                    return preset.StartingSpecializations.Contains(specName);
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Synchronise et assainit l'état de progression de la fiche (Livre I §5) :
        /// - Impute les entraînements gratuits d'Érudition pour couvrir les niveaux d'entraînement existants.
        /// - Calcule le montant minimal d'XP réellement investi sur la fiche (niveaux payés,
        ///   spécialisations payantes, attributs en XP, sorts) et synchronise TotalSpentXP.
        /// </summary>
        public static void SynchronizeProgression(CharacterSheet sheet)
        {
            if (sheet == null) return;
            sheet.MigrateLegacyBluntSkill();

            int totalTrainings = sheet.GetTotalTrainingLevels();
            int freeBudget = sheet.GetFreeTrainingBudget();

            if (totalTrainings > 0 && freeBudget > 0)
            {
                sheet.FreeTrainingsUsed = Math.Clamp(Math.Max(sheet.FreeTrainingsUsed, Math.Min(totalTrainings, freeBudget)), 0, freeBudget);
            }
            else if (totalTrainings == 0)
            {
                sheet.FreeTrainingsUsed = 0;
            }

            int freeCovered = Math.Min(totalTrainings, sheet.FreeTrainingsUsed);
            int paidTrainings = Math.Max(0, totalTrainings - freeCovered);
            int paidTrainingsXP = paidTrainings * XP_COST_TRAINING;

            int paidSpecs = 0;
            if (sheet.UnlockedSpecializations != null)
            {
                for (int i = 0; i < sheet.UnlockedSpecializations.Count; i++)
                {
                    string spec = sheet.UnlockedSpecializations[i];
                    if (string.IsNullOrWhiteSpace(spec)) continue;

                    if (MinaCharacter.IsInnateUnlocked(spec, sheet)) continue;
                    if (LucasCharacter.IsInnateUnlocked(spec, sheet)) continue;
                    if (IsPresetStartingSpec(sheet, spec)) continue;

                    paidSpecs++;
                }
            }
            int paidSpecsXP = paidSpecs * XP_COST_SPECIALIZATION;

            int spellsXP = 0;
            if (sheet.LearnedSpells != null)
            {
                for (int i = 0; i < sheet.LearnedSpells.Count; i++)
                {
                    var sp = sheet.LearnedSpells[i];
                    if (sp != null) spellsXP += Math.Max(1, sp.CreationXpCost);
                }
            }

            int attributesXP = 0;
            if (sheet.AttributeUpgradesPurchased != null)
            {
                for (int i = 0; i < sheet.AttributeUpgradesPurchased.Length && i < 9; i++)
                {
                    int upgrades = sheet.AttributeUpgradesPurchased[i];
                    int currentVal = sheet.GetBaseAttributeValue(i);
                    for (int u = 0; u < upgrades; u++)
                    {
                        int rankAtUpgrade = currentVal - u;
                        attributesXP += Math.Max(1, rankAtUpgrade);
                    }
                }
            }

            int minSpentXP = paidTrainingsXP + paidSpecsXP + spellsXP + attributesXP;

            if (sheet.TotalSpentXP < minSpentXP)
            {
                sheet.TotalSpentXP = minSpentXP;
            }
        }

        public static string GetSpentXPBreakdownString(CharacterSheet sheet)
        {
            if (sheet == null) return "Fiche invalide";
            SynchronizeProgression(sheet);

            int totalTrainings = sheet.GetTotalTrainingLevels();
            int freeCovered = Math.Min(totalTrainings, sheet.FreeTrainingsUsed);
            int paidTrainings = Math.Max(0, totalTrainings - freeCovered);
            int paidTrainingsXP = paidTrainings * XP_COST_TRAINING;

            int paidSpecs = 0;
            int innateSpecs = 0;
            if (sheet.UnlockedSpecializations != null)
            {
                for (int i = 0; i < sheet.UnlockedSpecializations.Count; i++)
                {
                    string spec = sheet.UnlockedSpecializations[i];
                    if (string.IsNullOrWhiteSpace(spec)) continue;
                    if (MinaCharacter.IsInnateUnlocked(spec, sheet) || LucasCharacter.IsInnateUnlocked(spec, sheet) || IsPresetStartingSpec(sheet, spec))
                    {
                        innateSpecs++;
                        continue;
                    }
                    paidSpecs++;
                }
            }
            int paidSpecsXP = paidSpecs * XP_COST_SPECIALIZATION;

            int spellsXP = 0;
            int spellCount = 0;
            if (sheet.LearnedSpells != null)
            {
                for (int i = 0; i < sheet.LearnedSpells.Count; i++)
                {
                    var sp = sheet.LearnedSpells[i];
                    if (sp != null)
                    {
                        spellsXP += Math.Max(1, sp.CreationXpCost);
                        spellCount++;
                    }
                }
            }

            int attributesXP = 0;
            int attrUpgrades = 0;
            if (sheet.AttributeUpgradesPurchased != null)
            {
                for (int i = 0; i < sheet.AttributeUpgradesPurchased.Length && i < 9; i++)
                {
                    int upgrades = sheet.AttributeUpgradesPurchased[i];
                    attrUpgrades += upgrades;
                    int currentVal = sheet.GetBaseAttributeValue(i);
                    for (int u = 0; u < upgrades; u++)
                    {
                        int rankAtUpgrade = currentVal - u;
                        attributesXP += Math.Max(1, rankAtUpgrade);
                    }
                }
            }

            var parts = new List<string>();
            if (paidTrainingsXP > 0)
                parts.Add($"Entraînements : {paidTrainingsXP} XP ({paidTrainings} payé(s)" + (freeCovered > 0 ? $", {freeCovered} gratuit(s) ÉRU" : "") + ")");
            else if (freeCovered > 0)
                parts.Add($"Entraînements : 0 XP ({freeCovered} gratuit(s) ÉRU)");

            if (paidSpecsXP > 0)
                parts.Add($"Spécialisations : {paidSpecsXP} XP ({paidSpecs} payée(s)" + (innateSpecs > 0 ? $", {innateSpecs} innée(s)" : "") + ")");
            else if (innateSpecs > 0)
                parts.Add($"Spécialisations : 0 XP ({innateSpecs} innée(s))");

            if (attributesXP > 0)
                parts.Add($"Attributs : {attributesXP} XP ({attrUpgrades} palier(s))");

            if (spellsXP > 0)
                parts.Add($"Sorts : {spellsXP} XP ({spellCount} sort(s))");

            if (parts.Count == 0)
                return "0 XP dépensé (profil de départ)";

            return string.Join(" • ", parts) + $" = {sheet.TotalSpentXP} XP total";
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

        [Serializable]
        public class SpecializationDetail
        {
            public string Name;
            public SkillType SourceSkill;
            public string ParentSpecialization;
            public bool IsImprovement;
            public bool IsHidden;
            public bool IsVolume2;
            public bool IsMinaExclusive;
            public bool IsLucasExclusive;
            public string Description;
            public string MechanicalEffect;
        }

        private static readonly Dictionary<string, SpecializationDetail> _registry = new(StringComparer.OrdinalIgnoreCase);
        private static bool _registryBuilt = false;

        public static readonly Dictionary<string, SkillType> SpecializationSources = new(StringComparer.OrdinalIgnoreCase);
        public static readonly Dictionary<string, string> SpecializationPrerequisites = new(StringComparer.OrdinalIgnoreCase);
        public static readonly HashSet<string> HiddenSpecializations = new(StringComparer.OrdinalIgnoreCase);
        public static readonly HashSet<string> MinaExclusiveSpecializations = new(StringComparer.OrdinalIgnoreCase);
        public static readonly HashSet<string> LucasExclusiveSpecializations = new(StringComparer.OrdinalIgnoreCase);

        public static bool IsMina(CharacterSheet sheet)
        {
            // Délégation propre : logique héroïque dans MinaCharacter.
            return MinaCharacter.IsMina(sheet);
        }

        public static bool IsLucas(CharacterSheet sheet)
        {
            // Délégation propre : logique héroïque dans LucasCharacter.
            return LucasCharacter.IsLucas(sheet);
        }

        public static bool IsSkillAccessible(CharacterSheet sheet, SkillType skill)
        {
            // Délégation propre : restrictions d'âme dans MinaCharacter / LucasCharacter.
            if (sheet != null && MinaCharacter.IsMina(sheet))
            {
                if (MinaCharacter.IsSkillForbidden(skill))
                    return false;
            }
            if (sheet != null && LucasCharacter.IsLucas(sheet))
            {
                if (LucasCharacter.IsSkillForbidden(skill))
                    return false;
            }
            return true;
        }

        public static bool IsMinaExclusiveSpecialization(string name)
        {
            EnsureRegistryBuilt();
            if (string.IsNullOrWhiteSpace(name)) return false;
            return MinaExclusiveSpecializations.Contains(name.Trim());
        }

        public static bool IsLucasExclusiveSpecialization(string name)
        {
            EnsureRegistryBuilt();
            if (string.IsNullOrWhiteSpace(name)) return false;
            return LucasExclusiveSpecializations.Contains(name.Trim());
        }

        static CharacterProgressionManager()
        {
            EnsureRegistryBuilt();
        }

        public static void EnsureRegistryBuilt()
        {
            if (_registryBuilt) return;
            _registryBuilt = true;

            RegisterSpec(SkillType.MainsNues, "Arts Martiaux", null, false, false,
                "Maîtrise complète du corps-à-corps à mains nues et pieds.",
                "Parade sans malus non-exclusif. Immunité à la contrainte Canon Entravé. Refoulement (1 case) sur choc traumatique ou Delta >= 4.");
            RegisterSpec(SkillType.MainsNues, "Arts Martiaux : Enchaînement Fluide", "Arts Martiaux", false, false,
                "Transition réflexe après un impact au corps-à-corps.",
                "La seconde attaque de mêlée du tour bénéficie d'une réduction de 1 PA.");
            RegisterSpec(SkillType.MainsNues, "Arts Martiaux : Frappe Téléportée", "Arts Martiaux : Enchaînement Fluide", false, false,
                "Bond cinétique instantané vers la cible.",
                "Permet un déplacement gratuit d'une case vers l'adversaire avant la frappe si PA >= 2.");
            RegisterSpec(SkillType.MainsNues, "Arts Martiaux : Déphasage Cinétique", "Arts Martiaux : Frappe Téléportée", true, false,
                "Déphasage transdimensionnel à l'impact.",
                "Sur coup critique, ignore totalement la parade et les contre-attaques adverses.");
            RegisterSpec(SkillType.MainsNues, "Arts Martiaux : Onde Tellurique", "Arts Martiaux", false, false,
                "Projection d'onde de choc résiduelle.",
                "Sur coup critique à mains nues, inflige l'état Déstabilisé à tous les ennemis adjacents.");
            RegisterSpec(SkillType.MainsNues, "Arts Martiaux : Brise-Blindage", "Arts Martiaux : Onde Tellurique", false, false,
                "Impact focalisé destructeur d'alliages.",
                "Les attaques à mains nues ignorent 2 points d'armure physique.");
            RegisterSpec(SkillType.MainsNues, "Arts Martiaux : Paume de Brum'korath", "Arts Martiaux : Brise-Blindage", true, false,
                "Frappe martiale ancestrale du Temple englouti.",
                "Sur coup critique, inflige un Choc Traumatique instantané et projette la cible de 3 cases.");
            RegisterSpec(SkillType.MainsNues, "Arts Martiaux : Balayage Rotatif", "Arts Martiaux", false, false,
                "Balayage circulaire des appuis adverses.",
                "Sur différentiel net Delta >= 2, fait chuter la cible À Terre.");
            RegisterSpec(SkillType.MainsNues, "Arts Martiaux : Clé d'Articulation", "Arts Martiaux : Balayage Rotatif", false, false,
                "Prise de soumission au contact.",
                "Au contact, applique Immobilisé et inflige 3 dégâts bruts sans réduction d'armure.");
            RegisterSpec(SkillType.MainsNues, "Arts Martiaux : Rupture Ligamentaire", "Arts Martiaux : Clé d'Articulation", true, false,
                "Torsion destructrice des tendons.",
                "Applique l'état Paralysé pendant 1 tour si la cible est déjà Déstabilisée.");

            // --- MAGIE PRIMORDIALE : PHASE 1 (VOLUME I — ATAVISME CINÉTIQUE & SOMA-OVERDRIVE) ---
            // NOTE : "Corps Augmenté" et sa résonance (innés héroïques) sont enregistrés
            // dans MinaCharacter.RegisterSpecializations() — voir fin de EnsureRegistryBuilt().

            RegisterSpec(SkillType.MagiePrimale, "Leviers Densifiés", null, false, false,
                "Renforcement osseux et focalisation de la pression (Phase 1).",
                "Les attaques de mêlée à mains nues ignorent 1 point d'armure physique (Perforant 1).");
            RegisterSpec(SkillType.MagiePrimale, "Leviers Densifiés : Perforation Osseuse", "Leviers Densifiés", false, false,
                "Pénétration osseuse chirurgicale.",
                "La perforation d'armure à mains nues passe à 2 points d'armure ignorés.");
            RegisterSpec(SkillType.MagiePrimale, "Leviers Densifiés : Pointe Cristalline", "Leviers Densifiés : Perforation Osseuse", true, false,
                "Densification nanométrique des phalanges.",
                "Ignore 3 points d'armure et convertit tous les dégâts en Dégâts Absolus.");
            RegisterSpec(SkillType.MagiePrimale, "Leviers Densifiés : Ancrage Myologique", "Leviers Densifiés", false, false,
                "Stabilité structurelle inébranlable.",
                "Immunise le porteur au refoulement et au statut À Terre face aux frappes de contact.");
            RegisterSpec(SkillType.MagiePrimale, "Leviers Densifiés : Poigne Titanesque", "Leviers Densifiés : Ancrage Myologique", false, false,
                "Prise musculaire indécrochable.",
                "Permet de désarmer automatiquement l'assaillant sur différentiel net Delta >= 3.");

            RegisterSpec(SkillType.MagiePrimale, "Teep de Rupture", null, false, false,
                "Coup de pied d'arrêt à transfert cinétique lourd (Phase 1 — Atavisme).",
                "Tout coup réussi (Delta >= 0) refoule la cible de 2 cases. Collision contre obstacle inflige 3 dégâts et [Sonné].");
            RegisterSpec(SkillType.MagiePrimale, "Teep de Rupture : Impact Dévastateur", "Teep de Rupture", false, false,
                "Amplification de la projection contre les parois.",
                "Les dégâts d'impact contre obstacle passent à 6 PV et appliquent également Déstabilisé pendant 2 tours.");
            RegisterSpec(SkillType.MagiePrimale, "Teep de Rupture : Onde de Choc Linéaire", "Teep de Rupture : Impact Dévastateur", false, false,
                "Propagation cinétique axiale.",
                "La distance de refoulement est portée à 3 cases.");
            RegisterSpec(SkillType.MagiePrimale, "Teep de Rupture : Brise-Châssis Titan", "Teep de Rupture : Onde de Choc Linéaire", true, false,
                "Impact démesuré destructeur de machines lourdes.",
                "Refoulement porté à 4 cases et désactivation immédiate des boucliers énergétiques touchés.");
            RegisterSpec(SkillType.MagiePrimale, "Teep de Rupture : Interception d'Assaut", "Teep de Rupture", false, false,
                "Réflexe d'arrêt d'élan entrant.",
                "Déclenche un Teep de Rupture automatique en réaction contre tout ennemi tentant d'entrer au contact direct.");
            RegisterSpec(SkillType.MagiePrimale, "Teep de Rupture : Rebond Mural", "Teep de Rupture : Interception d'Assaut", false, false,
                "Reprise d'appui après refoulement.",
                "Accorde 1 case de replacement gratuit sans coût de PA après avoir projeté une cible.");

            RegisterSpec(SkillType.MainsNues, "Perforation de Silicate", null, false, false,
                "Fissuration des blindages rocheux et synthétiques denses.",
                "Ignore 2 points d'armure et convertit les dégâts en Dégâts Absolus face aux carapaces de pierre.");
            RegisterSpec(SkillType.MainsNues, "Perforation de Silicate : Fracture Sismique", "Perforation de Silicate", false, false,
                "Onde de brisure souterraine.",
                "Ignore 3 points d'armure et détruit les couvertures rocheuses adjacentes à la cible.");
            RegisterSpec(SkillType.MainsNues, "Perforation de Silicate : Cœur d'Adamas", "Perforation de Silicate : Fracture Sismique", true, false,
                "Maîtrise ultime des pressions tectoniques.",
                "Ignore 4 points d'armure et inflige Sonné sur tout différentiel net Delta >= 2.");

            RegisterSpec(SkillType.ManiementArmes, "Maniement de l'Épée", null, false, false,
                "Escrime militaire lourde et précision du tranchant.",
                "Annule le malus de parade non-exclusive. Autorise la contre-attaque immédiate en réaction.");
            RegisterSpec(SkillType.ManiementArmes, "Maniement de l'Épée : Riposte Éclair", "Maniement de l'Épée", false, false,
                "Contre-attaque sans perte d'assiette.",
                "La contre-attaque en réaction ne subit aucun malus de riposte.");
            RegisterSpec(SkillType.ManiementArmes, "Maniement de l'Épée : Tranchant Tempétueux", "Maniement de l'Épée : Riposte Éclair", false, false,
                "Vitesse d'exécution tranchante.",
                "Confère +2 Dégâts bruts sur toute riposte réussie avec une lame.");
            RegisterSpec(SkillType.ManiementArmes, "Maniement de l'Épée : Lame de Ligne Temporelle Zéro", "Maniement de l'Épée : Tranchant Tempétueux", true, false,
                "Synchronisation causale absolue de la garde (Ligne Temporelle Zéro).",
                "Sur réussite critique en parade, annule totalement l'attaque ennemie et inflige les dégâts complets de l'arme en retour.");
            RegisterSpec(SkillType.ManiementArmes, "Maniement de l'Épée : Garde Haute Impériale", "Maniement de l'Épée", false, false,
                "Posture défensive protégeant le torse et la tête.",
                "Confère +2 au jet de parade contre les frappes lourdes et tirs ciblés.");
            RegisterSpec(SkillType.ManiementArmes, "Maniement de l'Épée : Lame Miroir", "Maniement de l'Épée : Garde Haute Impériale", false, false,
                "Déviation des rayonnements thermiques.",
                "Permet de parer les tirs de fusils laser au contact sur différentiel net Delta >= 0.");
            RegisterSpec(SkillType.ManiementArmes, "Maniement de l'Épée : Dôme de Parade", "Maniement de l'Épée : Lame Miroir", true, false,
                "Balisage cinétique protecteur d'escouade.",
                "Permet d'interposer sa lame pour parer une attaque ciblant un allié adjacent.");

            RegisterSpec(SkillType.ManiementArmes, "Fente de Rupture", null, false, false,
                "Frappe d'estoc perforante dans les défauts de blindage.",
                "Dépense +1 PA à l'assaut pour ignorer 2 points d'armure physique balistique ou métallique.");
            RegisterSpec(SkillType.ManiementArmes, "Fente de Rupture : Pointe Chirurgicale", "Fente de Rupture", false, false,
                "Guidage ultra-précis de la lame.",
                "Ignore 3 points d'armure au lieu de 2 lors d'une frappe ciblée.");
            RegisterSpec(SkillType.ManiementArmes, "Fente de Rupture : Transpercement Traversant", "Fente de Rupture : Pointe Chirurgicale", false, false,
                "Frappe perforant la première cible de part en part.",
                "Ignore 4 points d'armure et blesse également l'ennemi situé immédiatement derrière la cible.");
            RegisterSpec(SkillType.ManiementArmes, "Fente de Rupture : Fissure Causal", "Fente de Rupture : Transpercement Traversant", true, false,
                "Fente transperçant les strates temporelles.",
                "Inflige le statut Chrono-Fracture et draine instantanément 2 PA de la réserve adverse.");

            RegisterSpec(SkillType.ManiementArmes, "Bloquer", null, false, false,
                "Interposition de bouclier ou garde hermétique.",
                "Permet de bloquer sans malus de dé non-exclusif.");
            RegisterSpec(SkillType.ManiementArmes, "Bloquer : Mur de Bouclier", "Bloquer", false, false,
                "Absorption cinétique sur pavois.",
                "Absorbe 3 dégâts bruts supplémentaires lors d'un blocage actif.");
            RegisterSpec(SkillType.ManiementArmes, "Bloquer : Heurt de Bouclier", "Bloquer : Mur de Bouclier", false, false,
                "Frappe contondante en riposte directe.",
                "Après un blocage réussi, inflige 4 dégâts d'impact et repousse l'adversaire de 1 case.");
            RegisterSpec(SkillType.ManiementArmes, "Bloquer : Forteresse Impénétrable", "Bloquer : Heurt de Bouclier", true, false,
                "Bastion mécanique infranchissable.",
                "Immunité totale aux coups critiques et aux altérations Déstabilisé et À Terre pendant la garde.");

            RegisterSpec(SkillType.ManiementArmes, "Armes Contondantes", null, false, false,
                "Maîtrise des armes de choc : marteaux, masses, gourdins et fléaux.",
                "Annule le malus de parade non-exclusive au Maniement d'Arme. Prérequis du Marteau de Guerre.");
            RegisterSpec(SkillType.ManiementArmes, "Marteau de Guerre", "Armes Contondantes", false, false,
                "Impact tellurique écrasant.",
                "Sur succès net, la cible tombe À Terre, subit +2 Dégâts bruts et convertit les dégâts en Dégâts Absolus.");
            RegisterSpec(SkillType.ManiementArmes, "Marteau de Guerre : Écrasement Osseux", "Marteau de Guerre", false, false,
                "Broie les armures de plaques.",
                "Confère +3 Dégâts bruts additionnels contre toute cible portant une armure lourde.");
            RegisterSpec(SkillType.ManiementArmes, "Marteau de Guerre : Brise-Crâne", "Marteau de Guerre : Écrasement Osseux", false, false,
                "Visée haute commotrice.",
                "Une frappe ciblée à la Tête applique Sonné et Étourdi sans subir le malus de visée habituel.");
            RegisterSpec(SkillType.ManiementArmes, "Marteau de Guerre : Cataclysme de Fer", "Marteau de Guerre : Brise-Crâne", true, false,
                "Libération d'énergie de choc colossale.",
                "Confère +5 Dégâts bruts et projette une onde de choc renversant toutes les unités adjacentes.");
            RegisterSpec(SkillType.ManiementArmes, "Marteau de Guerre : Onde de Faille", "Marteau de Guerre", false, false,
                "Fissuration du sol par le marteau.",
                "Crée une zone de terrain difficile sur 2 cases autour du point d'impact.");
            RegisterSpec(SkillType.ManiementArmes, "Marteau de Guerre : Onde Sismique Réverbérante", "Marteau de Guerre : Onde de Faille", false, false,
                "Résonance acoustique destructrice.",
                "Applique le statut Déstabilisé à toute cible située dans un rayon de 2 cases.");

            RegisterSpec(SkillType.ManiementArmes, "Hache de Guerre", null, false, false,
                "Maîtrise des haches de bataille, cognées et bardiches.",
                "Annule le malus de parade non-exclusive au Maniement d'Arme. Sur succès net, inflige +2 Dégâts bruts.");
            RegisterSpec(SkillType.ManiementArmes, "Hache de Guerre : Fente du Bûcheron", "Hache de Guerre", false, false,
                "Abattage en puissance dans les plaques.",
                "Confère +3 Dégâts bruts additionnels contre toute cible portant une armure lourde.");
            RegisterSpec(SkillType.ManiementArmes, "Hache de Guerre : Brise-Garde", "Hache de Guerre : Fente du Bûcheron", false, false,
                "Crochet ascendant sous la garde adverse.",
                "Confère +4 Dégâts bruts et fait chuter la cible À Terre sur différentiel net Delta >= 2.");
            RegisterSpec(SkillType.ManiementArmes, "Hache de Guerre : Exécution du Bourreau", "Hache de Guerre : Brise-Garde", true, false,
                "Coup de billot sans appel.",
                "Confère +5 Dégâts bruts et convertit les dégâts en Dégâts Absolus sur coup critique.");
            RegisterSpec(SkillType.ManiementArmes, "Hache de Guerre : Croc de Désarmement", "Hache de Guerre", false, false,
                "Crochet du bec de la hache dans la garde adverse.",
                "Désarme la cible sur différentiel net Delta >= 3.");
            RegisterSpec(SkillType.ManiementArmes, "Hache de Guerre : Arrache-Bouclier", "Hache de Guerre : Croc de Désarmement", false, false,
                "Arrachement brutal des protections.",
                "Une parade adverse réussie de justesse (différentiel -1 ou 0) est convertie en touche franche.");

            RegisterSpec(SkillType.ArmesPercantes, "Escrime", null, false, false,
                "Jeu de lame rapide et précis à la rapière ou dague.",
                "Annule la punition de visée chirurgicale vers les membres vitaux légers. Parade sans malus non-exclusif.");
            RegisterSpec(SkillType.ArmesPercantes, "Escrime : Riposte à l'Estoc", "Escrime", false, false,
                "Riposte réflexe immédiate après parade.",
                "Autorise une contre-attaque immédiate pour 1 PA de réaction.");
            RegisterSpec(SkillType.ArmesPercantes, "Escrime : Fleuret Éclair", "Escrime : Riposte à l'Estoc", false, false,
                "Agilité de pointe dans le duel.",
                "Confère +2 en initiative réflexe et autorise une parade sans dépense de PA une fois par tour.");
            RegisterSpec(SkillType.ArmesPercantes, "Escrime : Frappe Fantôme", "Escrime : Fleuret Éclair", true, false,
                "Désaxement visuel imperceptible.",
                "L'attaque ignore la défense active adverse si la cible a déjà consommé des PA ce tour.");
            RegisterSpec(SkillType.ArmesPercantes, "Escrime : Désarmement Fleuret", "Escrime", false, false,
                "Torsion de garde métallique.",
                "Désarme la cible sur différentiel net Delta >= 2.");
            RegisterSpec(SkillType.ArmesPercantes, "Escrime : Entaille Précise", "Escrime : Désarmement Fleuret", false, false,
                "Section des tendons de préhension.",
                "Applique un malus de -2 à toutes les attaques de la cible pendant 2 tours.");

            RegisterSpec(SkillType.ArmesPercantes, "Frappe Jugulaire", null, false, false,
                "Visée chirurgicale de la gorge et des carotides.",
                "Sur Delta >= 3, déclenche l'état Saignement (dégâts résiduels par tour égaux aux entraînements).");
            RegisterSpec(SkillType.ArmesPercantes, "Frappe Jugulaire : Hémorragie Profonde", "Frappe Jugulaire", false, false,
                "Lacération artérielle majeure.",
                "Les dégâts résiduels de Saignement sont doublés chaque tour.");
            RegisterSpec(SkillType.ArmesPercantes, "Frappe Jugulaire : Asphyxie Sanguine", "Frappe Jugulaire : Hémorragie Profonde", false, false,
                "Obstruction des voies respiratoires.",
                "Applique l'état Asphyxie en plus du Saignement, drainant 1 PA de réserve par tour.");
            RegisterSpec(SkillType.ArmesPercantes, "Frappe Jugulaire : Tranche-Artère Silencieux", "Frappe Jugulaire : Asphyxie Sanguine", true, false,
                "Assassinat clinique d'infiltrateur.",
                "Si les dégâts nets dépassent l'encaissement, provoque la mort instantanée sans jet de survie.");

            RegisterSpec(SkillType.ArmesPercantes, "Arme de Hast", null, false, false,
                "Maîtrise des piques, hallebardes et lances d'arrêt.",
                "Les attaques à 2 cases d'allonge ne subissent aucun malus. Prérequis des techniques de hast.");
            RegisterSpec(SkillType.ArmesPercantes, "Arme de Hast : Arrêt de Charge", "Arme de Hast", false, false,
                "Pique plantée face à l'assaut.",
                "La pointe trouve les défauts : ignore 2 points d'armure physique balistique ou métallique.");
            RegisterSpec(SkillType.ArmesPercantes, "Arme de Hast : Mur de Piques", "Arme de Hast : Arrêt de Charge", false, false,
                "Hérisson d'acier discipliné.",
                "Ignore 3 points d'armure au lieu de 2 et repousse l'assaillant d'1 case sur succès net.");
            RegisterSpec(SkillType.ArmesPercantes, "Arme de Hast : Phalange d'Acier", "Arme de Hast : Mur de Piques", true, false,
                "Sarre de la garde impériale.",
                "Ignore 4 points d'armure et blesse également l'ennemi situé immédiatement derrière la cible.");
            RegisterSpec(SkillType.ArmesPercantes, "Arme de Hast : Fauchage Latéral", "Arme de Hast", false, false,
                "Balayage des appuis à pleine allonge.",
                "Sur différentiel net Delta >= 2, fait chuter la cible À Terre.");
            RegisterSpec(SkillType.ArmesPercantes, "Arme de Hast : Crochet de Hallebarde", "Arme de Hast : Fauchage Latéral", false, false,
                "Croc du bec de faucon dans les étriers et les gardes.",
                "Désarme la cible ou la tire à terre sur différentiel net Delta >= 2.");

            RegisterSpec(SkillType.Ballistique, "Pistolet & Tir Rapide", null, false, false,
                "Cadence réflexe à une main en tir rapproché.",
                "Sur réussite nette, applique Ralenti. Sur 5 ou 6 brut, fait chuter la cible À Terre.");
            RegisterSpec(SkillType.Ballistique, "Pistolet & Tir Rapide : Dégainé Réflexe", "Pistolet & Tir Rapide", false, false,
                "Tir instinctif dès la première seconde d'engagement.",
                "Le premier tir de pistolet de chaque tour coûte 1 PA de moins (minimum 1 PA).");
            RegisterSpec(SkillType.Ballistique, "Pistolet & Tir Rapide : Akimbo Assaut", "Pistolet & Tir Rapide : Dégainé Réflexe", false, false,
                "Tir simultané avec deux armes de poing.",
                "Permet de faire feu avec deux pistolets pour 3 PA totaux sur la même cible.");
            RegisterSpec(SkillType.Ballistique, "Pistolet & Tir Rapide : Trait d'Orichalque", "Pistolet & Tir Rapide : Akimbo Assaut", true, false,
                "Balistique énergétique surcadencée.",
                "Les tirs de pistolet ignorent le couvert partiel et transpercent jusqu'à 2 cibles alignées.");
            RegisterSpec(SkillType.Ballistique, "Pistolet & Tir Rapide : Balle dans le Genou", "Pistolet & Tir Rapide", false, false,
                "Tir ciblé d'amputation de mobilité.",
                "Visée chirurgicale des Jambes sans malus de visée, infligeant l'état Ralenti garanti.");
            RegisterSpec(SkillType.Ballistique, "Pistolet & Tir Rapide : Tir de Neutralisation", "Pistolet & Tir Rapide : Balle dans le Genou", false, false,
                "Désarmement à distance.",
                "Visée chirurgicale du Bras Droit désarme la cible sur tout différentiel net Delta >= 1.");

            RegisterSpec(SkillType.Ballistique, "Fusil de Précision", null, false, false,
                "Tir longue portée et perforation à fort grossissement.",
                "Portée accrue de 4 cases et bonus de critique augmenté.");
            RegisterSpec(SkillType.Ballistique, "Fusil de Précision : Visée Millimétrique", "Fusil de Précision", false, false,
                "Stabilisation respiratoire du tireur.",
                "Confère +2 au jet d'attaque chirurgicale si aucun déplacement n'a été effectué ce tour.");
            RegisterSpec(SkillType.Ballistique, "Fusil de Précision : Perforation Hyper-Véloce", "Fusil de Précision : Visée Millimétrique", false, false,
                "Munitions au noyau de tungstène lourd.",
                "Ignore jusqu'à 4 points d'armure balistique ou métallique.");
            RegisterSpec(SkillType.Ballistique, "Fusil de Précision : Tir à Travers les Parois", "Fusil de Précision : Perforation Hyper-Véloce", true, false,
                "Capteurs optiques à rayons X et faisceau perforant.",
                "Permet de cibler une unité située derrière un couvert total avec seulement -2 de malus au jet.");
            RegisterSpec(SkillType.Ballistique, "Fusil de Précision : Tir de Suppression", "Fusil de Précision", false, false,
                "Tir forçant l'ennemi à se terrer.",
                "Applique Déstabilisé et draine 1 PA de réserve défensive sur la cible.");
            RegisterSpec(SkillType.Ballistique, "Fusil de Précision : Calibre Anti-Matériel", "Fusil de Précision : Tir de Suppression", false, false,
                "Frappe perforante anti-véhicules.",
                "Confère +4 Dégâts bruts contre les tourelles, droïdes et structures fortifiées.");

            RegisterSpec(SkillType.Ballistique, "Grenadier d'Assaut", null, false, false,
                "Spécialisation dans les explosifs et lanceurs de zone.",
                "Dispersion réduite et portée de lancer étendue de 2 cases.");
            RegisterSpec(SkillType.Ballistique, "Grenadier d'Assaut : Calcul Balistique", "Grenadier d'Assaut", false, false,
                "Correction de trajectoire parabolique.",
                "Réduit la dispersion des grenades d'une case supplémentaire (dispersion minimale 0).");
            RegisterSpec(SkillType.Ballistique, "Grenadier d'Assaut : Dégoupillage Éclair", "Grenadier d'Assaut : Calcul Balistique", false, false,
                "Préparation immédiate de l'explosif.",
                "Lancer une grenade coûte 1 PA de moins (1 PA à la main, 2 PA au lanceur).");
            RegisterSpec(SkillType.Ballistique, "Grenadier d'Assaut : Souffle Thermobarique", "Grenadier d'Assaut : Dégoupillage Éclair", true, false,
                "Amorçage de dépression atmosphérique.",
                "Étend le rayon d'effet de souffle de +1 case pour toutes les grenades lancées.");

            RegisterSpec(SkillType.Ballistique, "Tir à l'Arc", null, false, false,
                "Maîtrise des arcs composites, arcs longs et arbalètes.",
                "Tirs silencieux : ne révèlent pas la position du tireur. Portée accrue de 2 cases.");
            RegisterSpec(SkillType.Ballistique, "Tir à l'Arc : Flèche Perforante", "Tir à l'Arc", false, false,
                "Pointe bodkin contre les blindages.",
                "Ignore jusqu'à 2 points d'armure balistique ou métallique.");
            RegisterSpec(SkillType.Ballistique, "Tir à l'Arc : Tir en Cloche", "Tir à l'Arc : Flèche Perforante", false, false,
                "Trajectoire parabolique par-dessus les couverts.",
                "Ignore le couvert partiel et étend la portée de 4 cases supplémentaires.");
            RegisterSpec(SkillType.Ballistique, "Tir à l'Arc : Pluie d'Acier", "Tir à l'Arc : Tir en Cloche", true, false,
                "Volée de saturation.",
                "Une attaque réussie blesse également une seconde cible adjacente à la première.");
            RegisterSpec(SkillType.Ballistique, "Tir à l'Arc : Tir Instinctif", "Tir à l'Arc", false, false,
                "Décoche sans visée, au jugé.",
                "Le premier tir à l'arc de chaque tour coûte 1 PA de moins (minimum 1 PA).");
            RegisterSpec(SkillType.Ballistique, "Tir à l'Arc : Chasseur Silencieux", "Tir à l'Arc : Tir Instinctif", false, false,
                "Fantôme des bois d'Hybris.",
                "Abattre une cible isolée ne déclenche aucune alerte ; +2 en Discrétion après un tir réussi.");

            RegisterSpec(SkillType.MagiePrimale, "Protocole des Pas Invisibles", null, false, false,
                "Dissipation mathématique des ondes de choc au sol (Phase 1 — Atavisme).",
                "Le premier déplacement de chaque tour coûte 0 PA au lieu de 1.");
            RegisterSpec(SkillType.MagiePrimale, "Protocole des Pas Invisibles : Dissipation d'Échappement", "Protocole des Pas Invisibles", false, false,
                "Extension de la foulée furtive.",
                "Le premier déplacement à 0 PA s'étend jusqu'à 2 cases au lieu d'une.");
            RegisterSpec(SkillType.MagiePrimale, "Protocole des Pas Invisibles : Pas Fantôme", "Protocole des Pas Invisibles : Dissipation d'Échappement", false, false,
                "Translation sans masse inertielle.",
                "Le premier déplacement s'étend à 3 cases sans consommer de PA.");
            RegisterSpec(SkillType.MagiePrimale, "Protocole des Pas Invisibles : Célérité Quantique", "Protocole des Pas Invisibles : Pas Fantôme", true, false,
                "Glissement quantique à travers l'espace de combat.",
                "Le premier déplacement s'étend à 4 cases et permet de traverser librement les cases occupées par des ennemis.");

            RegisterSpec(SkillType.MagiePrimale, "Élan Sans Drag", null, false, false,
                "Fluidité hydrodynamique absolue en mouvement (Phase 1).",
                "Ignore les pénalités de terrain difficile (Ralenti, boue, gravats). Vitesse accrue à 5.5 m/s.");
            RegisterSpec(SkillType.MagiePrimale, "Élan Sans Drag : Friction Zéro", "Élan Sans Drag", false, false,
                "Glisse parfaite sur le sol tactique.",
                "La vitesse de déplacement atteint 7.0 m/s et immunise aux dégâts de chute.");
            RegisterSpec(SkillType.MagiePrimale, "Élan Sans Drag : Glissade Balistique", "Élan Sans Drag : Friction Zéro", false, false,
                "Tir d'appui pendant la course.",
                "Permet d'effectuer un tir ou une attaque pendant un déplacement sans interrompre la course.");
            RegisterSpec(SkillType.MagiePrimale, "Élan Sans Drag : Évasion Inertielle", "Élan Sans Drag : Glissade Balistique", true, false,
                "Conservation cinétique totale.",
                "Vitesse portée à 8.5 m/s et immunité absolue aux zones d'acide, de ronces et de flammes.");

            RegisterSpec(SkillType.MagiePrimale, "Décélération Contrôlée", null, false, false,
                "Gestion instantanée des vecteurs d'inertie négative (Phase 1 — Atavisme).",
                "[Phase 1 : Vecteur] Élimine le risque de fracture fémorale et les traumatismes cinétiques de freinage brusque. Immunité aux dégâts de choc contre parois.");
            RegisterSpec(SkillType.MagiePrimale, "Décélération Contrôlée : Freinage Magnétique", "Décélération Contrôlée", false, false,
                "Inversion polaire instantanée des appuis.",
                "Permet d'effectuer un virage à 180° sans perte de vitesse et octroie +1 PA réflexe après une course.");
            RegisterSpec(SkillType.MagiePrimale, "Décélération Contrôlée : Ancrage Cinétique Absolu", "Décélération Contrôlée : Freinage Magnétique", true, false,
                "Verrouillage gravitationnel plantaire.",
                "Immunité totale au refoulement et à l'altération Déstabilisé consécutive à une projection.");

            RegisterSpec(SkillType.MagiePrimale, "Réflexes Myotatiques", null, false, false,
                "Arc réflexe proprioceptif ultra-rapide (Phase 1 — Atavisme).",
                "[Phase 1 : Trajectoire] Confère +1 palier de dé effectif permanent sur tous les jets d'Esquive active.");
            RegisterSpec(SkillType.MagiePrimale, "Réflexes Myotatiques : Synapses Hyper-Accélérées", "Réflexes Myotatiques", false, false,
                "Anticipation myologique réflexe.",
                "Confère +2 en Initiative réflexe et +1 supplémentaire sur tout jet de défense opposée.");

            RegisterSpec(SkillType.MagiePrimale, "Roulade de Décrochage", null, false, false,
                "Évitement avec translation spatiale rapide.",
                "Sur esquive réussie avec Delta >= 2, accorde 1 case de recul gratuit sans consommer de PA.");
            RegisterSpec(SkillType.Esquive, "Roulade de Décrochage : Reprise d'Appui", "Roulade de Décrochage", false, false,
                "Rebond immédiat après le décrochage.",
                "Accorde +1 PA de réserve pour le tour suivant après une esquive parfaite.");
            RegisterSpec(SkillType.Esquive, "Roulade de Décrochage : Ombre Évasive", "Roulade de Décrochage : Reprise d'Appui", false, false,
                "Disparition des lignes de mire directes.",
                "L'esquive réussie empêche les ennemis de cibler le personnage jusqu'à son prochain tour.");
            RegisterSpec(SkillType.Esquive, "Roulade de Décrochage : Pas Déphasé", "Roulade de Décrochage : Ombre Évasive", true, false,
                "Translation temporelle d'évitement.",
                "Une esquive réussie téléporte instantanément le personnage de 2 cases dans une direction au choix.");
            RegisterSpec(SkillType.Esquive, "Roulade de Décrochage : Esquive Réactive", "Roulade de Décrochage", false, false,
                "Souplesse réflexe sous altération.",
                "Le coût d'esquive en réaction ne peut être doublé par l'altération Ralenti.");
            RegisterSpec(SkillType.Esquive, "Roulade de Décrochage : Contre-Poussée", "Roulade de Décrochage : Esquive Réactive", false, false,
                "Dévier le centre de gravité adverse.",
                "Repousse l'assaillant de 1 case après une esquive réussie au contact.");

            RegisterSpec(SkillType.Esquive, "Acrobatie d'Évitement", null, false, false,
                "Voltige de combat et franchissement aérien.",
                "Permet de franchir les tranchées et demi-couverts sans dépense de PA additionnelle.");
            RegisterSpec(SkillType.Esquive, "Acrobatie d'Évitement : Saut Périlleux", "Acrobatie d'Évitement", false, false,
                "Échappée par les hauteurs.",
                "Permet de monter sur les barricades ou obstacles mi-hauteur sans coût de mouvement.");
            RegisterSpec(SkillType.Esquive, "Acrobatie d'Évitement : Vrille Balistique", "Acrobatie d'Évitement : Saut Périlleux", false, false,
                "Esquive des tirs en trajectoire hélicoïdale.",
                "Confère +2 en esquive contre toutes les attaques de fusils et de grenades.");

            RegisterSpec(SkillType.Athletisme, "Course d'Endurance", null, false, false,
                "Sprint, fond et gestion du souffle en mouvement.",
                "Le premier déplacement de chaque tour coûte 1 PA de moins (minimum 0).");
            RegisterSpec(SkillType.Athletisme, "Course d'Endurance : Second Souffle", "Course d'Endurance", false, false,
                "Récupération de fond du coureur.",
                "L'action Reprendre son Souffle efface 2 points d'essoufflement au lieu d'1.");
            RegisterSpec(SkillType.Athletisme, "Course d'Endurance : Cœur de Marathon", "Course d'Endurance : Second Souffle", true, false,
                "Pompe de marathonien.",
                "Le Souffle d'Urgence fournit +3 PA immédiats au lieu de +2.");
            RegisterSpec(SkillType.Athletisme, "Franchissement", null, false, false,
                "Escalade, nage de combat et progression en terrain hostile.",
                "Ignore les pénalités de terrain difficile (boue, gravats, pentes) et les coûts de franchissement.");
            RegisterSpec(SkillType.Athletisme, "Franchissement : Escalade Assurée", "Franchissement", false, false,
                "Murs, façades et cordes.",
                "Franchit les tranchées, murets et obstacles mi-hauteur sans dépense de PA additionnelle.");
            RegisterSpec(SkillType.Athletisme, "Franchissement : Nage de Combat", "Franchissement : Escalade Assurée", false, false,
                "Apnée et progression palmée.",
                "Aucune épreuve requise en eau agitée ; retient son souffle pendant 2 × Constitution en tours.");

            RegisterSpec(SkillType.DefenseCorporelle, "Bouclier Vivant", null, false, false,
                "Protection des équipiers au détriment de sa propre chair.",
                "Permet d'interposer son corps pour intercepter un tir ciblant un allié adjacent.");
            RegisterSpec(SkillType.DefenseCorporelle, "Bouclier Vivant : Interposition Réflexe", "Bouclier Vivant", false, false,
                "Interception sans temps de latence.",
                "L'interposition coûte 1 PA au lieu de 2.");
            RegisterSpec(SkillType.DefenseCorporelle, "Bouclier Vivant : Dévier le Calibre", "Bouclier Vivant : Interposition Réflexe", false, false,
                "Déviation oblique des munitions.",
                "Réduit de moitié les dégâts des tirs interceptés.");
            RegisterSpec(SkillType.DefenseCorporelle, "Bouclier Vivant : Mur Inébranlable", "Bouclier Vivant : Dévier le Calibre", true, false,
                "Ancrage tellurique face aux déflagrations.",
                "Encaisse les explosions de grenades sans subir de refoulement ni d'altération Déstabilisé.");

            RegisterSpec(SkillType.MagiePrimale, "Fibres Résilientes", null, false, false,
                "Densification arcanique des tissus cellulaires (Phase 1 — Atavisme).",
                "Le seuil d'encaissement de choc traumatique passe de CON x 2 à (CON x 2) + 2.");
            RegisterSpec(SkillType.MagiePrimale, "Fibres Résilientes : Tissu Densifié", "Fibres Résilientes", false, false,
                "Matrice cellulaire blindée.",
                "Le seuil d'encaissement est augmenté de +4 au lieu de +2.");
            RegisterSpec(SkillType.MagiePrimale, "Fibres Résilientes : Carapace Sous-Cutanée", "Fibres Résilientes : Tissu Densifié", false, false,
                "Tissage conjonctif ultra-dense.",
                "Le seuil d'encaissement est augmenté de +6 par rapport à la valeur nominale.");
            RegisterSpec(SkillType.MagiePrimale, "Fibres Résilientes : Chair d'Inflexible", "Fibres Résilientes : Carapace Sous-Cutanée", true, false,
                "Physiologie invincible aux traumatismes ouverts.",
                "Le seuil d'encaissement est augmenté de +8 et le personnage gagne +1 point d'armure naturelle permanente.");

            RegisterSpec(SkillType.MagiePrimale, "Encaissement Élastique", null, false, false,
                "Déformation viscoélastique des tissus conjonctifs (Phase 1 — Atavisme).",
                "[Phase 1 : Matrice] Absorption des micro-fractures cinétiques : augmente le seuil d'encaissement de +2 et accorde +1 Armure d'encaissement passive.");
            RegisterSpec(SkillType.MagiePrimale, "Encaissement Élastique : Tissu Myo-Amortisseur", "Encaissement Élastique", false, false,
                "Réseau amortisseur myofibrillaire.",
                "Le seuil d'encaissement augmente de +4 additionnels et absorbe 2 dégâts bruts sur tout coup critique subi.");
            RegisterSpec(SkillType.MagiePrimale, "Encaissement Élastique : Dissipation Plastique", "Encaissement Élastique : Tissu Myo-Amortisseur", true, false,
                "Dissipation intégrale des vibrations internes.",
                "Immunité complète au statut Sonné provoqué par une collision ou un projectile lourd.");

            RegisterSpec(SkillType.MagiePrimale, "Régénération Métabolique", null, false, false,
                "Mitose réparatrice continue des tissus (Phase 1 — Atavisme).",
                "[Phase 1 : Homéostasie] Régénère automatiquement 1 PV au début de chaque tour et cautérise immédiatement l'état Saignement.");

            RegisterSpec(SkillType.MagiePrimale, "Homéostasie Accélérée", null, false, false,
                "Mitose réparatrice continue des tissus (Soma).",
                "Régénère automatiquement 1 PV au début de chaque tour et cautérise le Saignement.");
            RegisterSpec(SkillType.MagiePrimale, "Homéostasie Accélérée : Régénération Cellulaire Avancée", "Homéostasie Accélérée", false, false,
                "Poussée régénératrice d'élite.",
                "Régénère 2 PV par tour et purge également le statut Empoisonné.");
            RegisterSpec(SkillType.MagiePrimale, "Homéostasie Accélérée : Coagulation Flash", "Homéostasie Accélérée : Régénération Cellulaire Avancée", false, false,
                "Cicatrisation instantanée des brûlures.",
                "Régénère 3 PV par tour et purge immédiatement le statut En Feu.");
            RegisterSpec(SkillType.MagiePrimale, "Homéostasie Accélérée : Pulsion Phénix", "Homéostasie Accélérée : Coagulation Flash", true, false,
                "Survie cellulaire absolue.",
                "Régénère 4 PV par tour et ressuscite automatiquement à 1 PV une fois par combat si terrassé.");

            RegisterSpec(SkillType.MagiePrimale, "Seconde Respiration", null, false, false,
                "Ventilation cellulaire d'urgence (Soma).",
                "1 fois par combat : l'action de reprise de souffle est gratuite (0 PA) et efface 2 points d'essoufflement.");
            RegisterSpec(SkillType.Cardio, "Seconde Respiration : Rechargement Anaérobie", "Seconde Respiration", false, false,
                "Conversion de l'acide lactique en énergie.",
                "Efface 3 points d'essoufflement au lieu de 2 lors de la reprise de souffle.");
            RegisterSpec(SkillType.Cardio, "Seconde Respiration : Poumons d'Acier", "Seconde Respiration : Rechargement Anaérobie", false, false,
                "Hyperventilation oxygénée.",
                "Efface 4 points d'essoufflement et octroie +1 PA immédiat.");
            RegisterSpec(SkillType.Cardio, "Seconde Respiration : Coeur de Berserker", "Seconde Respiration : Poumons d'Acier", true, false,
                "Pompage d'adrénaline continu.",
                "Utilisable 2 fois par combat et confère l'immunité au statut Ralenti.");

            RegisterSpec(SkillType.Cardio, "Poussée Cardiovasculaire", null, false, false,
                "Sur-régime cardiaque volontaire pour forcer des actions d'urgence.",
                "Permet de forcer 1 PA d'action en prenant un point d'essoufflement.");

            RegisterSpec(SkillType.EndurancePhysique, "Condition de Fer", null, false, false,
                "Marches forcées, veilles et privations encaissées.",
                "Régénère 1 PV au début de chaque tour personnel si blessé et conscient.");
            RegisterSpec(SkillType.EndurancePhysique, "Condition de Fer : Dur à Cuire", "Condition de Fer", false, false,
                "Encaisse sans broncher.",
                "Le seuil d'encaissement de choc traumatique est augmenté de +2.");
            RegisterSpec(SkillType.EndurancePhysique, "Condition de Fer : Mur de Chair", "Condition de Fer : Dur à Cuire", true, false,
                "Rempart vivant de la ligne.",
                "Le seuil d'encaissement est augmenté de +2 et confère +1 point d'armure naturelle permanente.");
            RegisterSpec(SkillType.EndurancePhysique, "Trempe de Fer", null, false, false,
                "Douleur, peur et privations apprivoisées.",
                "Le malus de Déstabilisé passe de -2 à -1 sur tous les jets.");
            RegisterSpec(SkillType.EndurancePhysique, "Trempe de Fer : Ignorer la Douleur", "Trempe de Fer", false, false,
                "Blessures graves mises en sourdine.",
                "Le malus d'À Terre en attaque (-1) est ignoré.");
            RegisterSpec(SkillType.EndurancePhysique, "Trempe de Fer : Esprit de Granit", "Trempe de Fer : Ignorer la Douleur", false, false,
                "Mental de roc sous le choc.",
                "Le malus d'Étourdi (-1) est ignoré sur tous les jets.");
            RegisterSpec(SkillType.Cardio, "Poussée Cardiovasculaire : Redline Stabilisé", "Poussée Cardiovasculaire", false, false,
                "Régulation de la surchauffe métabolique.",
                "Atteindre le plafond d'essoufflement n'applique plus le statut Ralenti.");
            RegisterSpec(SkillType.Cardio, "Poussée Cardiovasculaire : Injection d'Adrénaline", "Poussée Cardiovasculaire : Redline Stabilisé", false, false,
                "Pic d'énergie décuplé.",
                "La poussée octroie +2 PA au lieu de +1 pour chaque point d'essoufflement engagé.");

            RegisterSpec(SkillType.SystemeImmunitaire, "Immunité Toxique", null, false, false,
                "Anticorps arcaniques et neutralisation des poisons.",
                "Confère l'immunité complète au statut Empoisonné.");
            RegisterSpec(SkillType.SystemeImmunitaire, "Immunité Toxique : Neutralisation des Poisons", "Immunité Toxique", false, false,
                "Filtrage biologique hépatique renforcé.",
                "Divise par deux les dégâts de toutes les toxines subies.");
            RegisterSpec(SkillType.SystemeImmunitaire, "Immunité Toxique : Filtrage des Gaz de Guerre", "Immunité Toxique : Neutralisation des Poisons", false, false,
                "Barrière bronchique imperméable.",
                "Immunité complète aux effets de grenades lacrymogènes et neurotoxiques.");
            RegisterSpec(SkillType.SystemeImmunitaire, "Immunité Toxique : Sang Antidote Universel", "Immunité Toxique : Filtrage des Gaz de Guerre", true, false,
                "Plasma biologique thérapeutique.",
                "Immunise tous les alliés situés à moins de 2 cases contre les gaz et toxines.");

            RegisterSpec(SkillType.MedecineAvancee, "Chirurgie", null, false, false,
                "Intervention chirurgicale de guerre en milieu hostile.",
                "Permet de refermer les blessures critiques et de lever l'état Souffrant.");
            RegisterSpec(SkillType.MedecineAvancee, "Chirurgie : Diagnostic Vital", "Chirurgie", false, false,
                "Analyse clinique immédiate.",
                "Permet de connaître exactement les PV, seuils et armure de toute unité examinée.");
            RegisterSpec(SkillType.MedecineAvancee, "Chirurgie : Suture Réflexe", "Chirurgie : Diagnostic Vital", false, false,
                "Gestes médicaux d'urgence sur le front.",
                "Le coût en PA des premiers soins d'urgence passe de 3 PA à 2 PA.");
            RegisterSpec(SkillType.MedecineAvancee, "Chirurgie : Greffe d'Urgence", "Chirurgie : Suture Réflexe", true, false,
                "Reconstitution cellulaire instantanée.",
                "Restaure immédiatement la fonctionnalité d'un membre détruit ou amputé en combat.");

            RegisterSpec(SkillType.MedecineAvancee, "Apothicaire de Guerre", null, false, false,
                "Synthèse chimique de terrain et stimulants de combat.",
                "Permet de composer des stimulants (Antidouleurs, Speed) sans laboratoire fixe.");
            RegisterSpec(SkillType.MedecineAvancee, "Apothicaire de Guerre : Stimulant Neuro-Accélérateur", "Apothicaire de Guerre", false, false,
                "Dopage de vitesse synaptique.",
                "Synthétise un stimulant accordant l'état Rapide (+3 PA de mouvement) pendant 4 heures.");
            RegisterSpec(SkillType.MedecineAvancee, "Apothicaire de Guerre : Sérum Antidote", "Apothicaire de Guerre : Stimulant Neuro-Accélérateur", false, false,
                "Contre-poison instantané.",
                "Purge immédiatement toutes les altérations biologiques actives sur un allié au contact.");
            RegisterSpec(SkillType.MedecineAvancee, "Apothicaire de Guerre : Cocktail Panacée", "Apothicaire de Guerre : Sérum Antidote", true, false,
                "Élixir de survie ultime.",
                "Soigne 15 PV et immunise contre toutes les altérations d'état pendant 3 tours.");

            RegisterSpec(SkillType.IngenierieArcanotech, "Surfréquence Nytharite", null, false, false,
                "Surcharge des circuits énergétiques à cristal de nytharite.",
                "Dépense 2 PA : double la puissance d'une arme laser ou d'un champ de force pendant 1 tour.");
            RegisterSpec(SkillType.IngenierieArcanotech, "Surfréquence Nytharite : Stabilisateur de Flux", "Surfréquence Nytharite", false, false,
                "Élimination des retours de flamme.",
                "L'équipement en surfréquence n'a plus aucun risque d'être endommagé ou jammé.");
            RegisterSpec(SkillType.IngenierieArcanotech, "Surfréquence Nytharite : Décharge Harmonique", "Surfréquence Nytharite : Stabilisateur de Flux", false, false,
                "Surplus de puissance plasmique.",
                "La surfréquence ajoute +3 Dégâts d'énergie pure à toutes les attaques du tour.");
            RegisterSpec(SkillType.IngenierieArcanotech, "Surfréquence Nytharite : Résonance Infinie", "Surfréquence Nytharite : Décharge Harmonique", true, false,
                "Couplage parfait avec la Cinquième Force.",
                "Maintient la surfréquence active pendant 3 tours consécutifs sans coût de recharge.");

            RegisterSpec(SkillType.TactiqueStrategie, "Analyse de Faille", null, false, false,
                "Détection des points faibles de blindage adverse.",
                "Dépense 2 PA : le prochain coup porté par vous ou un allié ignore l'encaissement adverse.");
            RegisterSpec(SkillType.TactiqueStrategie, "Analyse de Faille : Tir Coordonné", "Analyse de Faille", false, false,
                "Guidage balistique pour l'escouade.",
                "Tous les alliés bénéficient d'un bonus de +2 au jet d'attaque contre la cible analysée.");
            RegisterSpec(SkillType.TactiqueStrategie, "Analyse de Faille : Prédiction de Retraite", "Analyse de Faille : Tir Coordonné", false, false,
                "Anticipation géométrique des angles de fuite.",
                "Empêche la cible analysée de bénéficier de toute couverture pendant son prochain tour.");
            RegisterSpec(SkillType.TactiqueStrategie, "Analyse de Faille : Échec et Mat", "Analyse de Faille : Prédiction de Retraite", true, false,
                "Piège tactique absolu du Maître de Guerre.",
                "Toute attaque réussie contre la cible analysée inflige un coup critique garanti.");

            RegisterSpec(SkillType.Communication, "Tromper", null, false, false,
                "Feintes oratoires, diversion et duperie.",
                "Épreuve sociale opposée à l'Intuition adverse pour tromper un interlocuteur.");
            RegisterSpec(SkillType.Communication, "Tromper : Feinte Oratoire", "Tromper", false, false,
                "Détournement d'attention en plein combat.",
                "Dépense 1 PA : force un ennemi à tourner le dos jusqu'au tour suivant.");
            RegisterSpec(SkillType.Communication, "Tromper : Faux Signalement", "Tromper : Feinte Oratoire", false, false,
                "Infiltration et leurres radio.",
                "Provoque la dispersion des renforts ennemis hors du champ de bataille.");
            RegisterSpec(SkillType.Communication, "Tromper : Mascarade Totale", "Tromper : Faux Signalement", true, false,
                "Usurpation d'identité parfaite.",
                "Permet de traverser les zones ennemies sans déclencher aucune hostilité.");

            RegisterSpec(SkillType.Communication, "Négocier", null, false, false,
                "Marchandage institutionnel impérial.",
                "Réduit de 10% à 50% le prix d'achat des équipements en crédits CE.");
            RegisterSpec(SkillType.Communication, "Négocier : Marché Noir Impérial", "Négocier", false, false,
                "Accès aux filières clandestines.",
                "Débloque l'achat de prototypes et d'équipements de rareté Légendaire.");
            RegisterSpec(SkillType.Communication, "Négocier : Cessez-le-Feu", "Négocier : Marché Noir Impérial", false, false,
                "Trêve militaire temporaire.",
                "Suspend les attaques ennemies pendant 1 tour pour engager des pourparlers.");
            RegisterSpec(SkillType.Communication, "Négocier : Rachat d'Allégeance", "Négocier : Cessez-le-Feu", true, false,
                "Corruption et retournement de mercenaires.",
                "Permet de convertir un sbire ennemi en allié pour le reste du combat.");

            RegisterSpec(SkillType.Intimidation, "Intimider", null, false, false,
                "Ascendant psychologique et menaces de mort.",
                "Sur un différentiel net Delta >= 3, force un sbire à fuir le champ de bataille.");
            RegisterSpec(SkillType.Intimidation, "Intimider : Rugissement de Terreur", "Intimider", false, false,
                "Pression psychologique de zone.",
                "Applique l'état Déstabilisé à tous les ennemis situés à moins de 3 cases.");
            RegisterSpec(SkillType.Intimidation, "Intimider : Regard de Prédateur", "Intimider : Rugissement de Terreur", false, false,
                "Défi du champion en duel singulier.",
                "Empêche la cible désignée d'attaquer toute autre unité que vous.");
            RegisterSpec(SkillType.Intimidation, "Intimider : Paralysie Psychologique", "Intimider : Regard de Prédateur", true, false,
                "Terreur tétanisante absolue.",
                "Applique l'état Paralysé pendant 1 tour sur toute cible dont la Volonté est inférieure au Charisme de l'attaquant.");

            RegisterSpec(SkillType.Leadership, "Mener (Commandement)", null, false, false,
                "Coordination tactique et galvanisation des troupes.",
                "Dépense 2 PA : confère +1 PA réflexe à tous les alliés situés à moins de 3 cases.");
            RegisterSpec(SkillType.Leadership, "Mener : En avant !", "Mener (Commandement)", false, false,
                "Impulsion d'assaut coordonné.",
                "Confère +2 PA de déplacement à deux alliés désignés.");
            RegisterSpec(SkillType.Leadership, "Mener : Tenir la Ligne !", "Mener : En avant !", false, false,
                "Discipline de fer sous le feu.",
                "Augmente de +3 le seuil d'encaissement de tous les alliés proches pendant 1 tour.");
            RegisterSpec(SkillType.Leadership, "Mener : Galvanisation des Héros", "Mener : Tenir la Ligne !", true, false,
                "Inspiration cosmique de l'Ordre.",
                "Restaure 2 PA à toute l'escouade et purge immédiatement toutes les altérations mentales.");

            RegisterSpec(SkillType.Observation, "Vigilance Réflexe", null, false, false,
                "Vigilance sensorielle immédiate et détection des menaces.",
                "Immunise contre l'effet de surprise et conserve la réaction défensive complète lors d'une embuscade.");
            RegisterSpec(SkillType.Observation, "Vigilance Réflexe : Oeil de Lynx", "Vigilance Réflexe", false, false,
                "Acuité visuelle perçante.",
                "Portée visuelle augmentée de 4 cases dans la pénombre et l'obscurité.");
            RegisterSpec(SkillType.Observation, "Vigilance Réflexe : Détection Thermique", "Vigilance Réflexe : Oeil de Lynx", false, false,
                "Vision infrarouge naturelle.",
                "Révèle les unités camouflées et invisibles à travers les parois légères.");
            RegisterSpec(SkillType.Observation, "Vigilance Réflexe : Perception Panoramique 360°", "Vigilance Réflexe : Détection Thermique", true, false,
                "Omnivision sans angle mort.",
                "Immunité totale aux attaques de dos et bonus de surprise permanent.");

            RegisterSpec(SkillType.Intuition, "Sphère de Résonance", null, false, true,
                "Champ proprioceptif étendu (Volume II — Résonance).",
                "Immunité aux attaques de dos et embuscades dans un rayon de 3 cases.");
            RegisterSpec(SkillType.Intuition, "Sphère de Résonance : Écho Myotatique", "Sphère de Résonance", false, true,
                "Perception des vecteurs d'attaque.",
                "Permet d'ajuster gratuitement sa mise de +1 PA lors du Duel Aveugle en défense.");
            RegisterSpec(SkillType.Intuition, "Sphère de Résonance : Champ Prédictif", "Sphère de Résonance : Écho Myotatique", false, true,
                "Calcul prédictif des trajectoires.",
                "Anticipe la zone anatomique ciblée par l'attaquant et gagne +2 en esquive.");
            RegisterSpec(SkillType.Intuition, "Sphère de Résonance : Omniprésence Synaptique", "Sphère de Résonance : Champ Prédictif", true, true,
                "Clairsentience absolue du Fleuve du Temps.",
                "Révèle les mises de PA et PE de l'adversaire avant de déclarer sa propre défense.");

            RegisterSpec(SkillType.Intuition, "Sens du Danger", null, false, false,
                "Réagit avant même de comprendre la menace.",
                "L'Instinct compte dans la base d'Initiative (max Rapidité, Agilité, Intelligence, Instinct).");
            RegisterSpec(SkillType.Intuition, "Sens du Danger : Premiers Réflexes", "Sens du Danger", false, false,
                "Détente du félin acculé.",
                "Confère +1 palier de dé effectif sur le jet d'Initiative.");
            RegisterSpec(SkillType.Intuition, "Sens du Danger : Clairvoyance du Vétéran", "Sens du Danger : Premiers Réflexes", true, false,
                "Jamais surpris, jamais pris à revers.",
                "Immunité totale aux embuscades et aux attaques de dos.");
            RegisterSpec(SkillType.Intuition, "Instinct du Chasseur", null, false, false,
                "Pistage, affût et lecture du terrain giboyeux.",
                "Suit une piste fraîche sans épreuve et estime nombre, poids et allure de la proie.");
            RegisterSpec(SkillType.Intuition, "Instinct du Chasseur : Pistage", "Instinct du Chasseur", false, false,
                "Nez du limier d'Hybris.",
                "Confère +2 en NatureSurvie pour pister, même sur sol dur ou par nuit sans lune.");
            RegisterSpec(SkillType.Intuition, "Instinct du Chasseur : Piégeur", "Instinct du Chasseur : Pistage", false, false,
                "Collets, fosses et traquenards.",
                "Pose et détecte les pièges de campagne ; les proies prises sont Immobilisées.");

            RegisterSpec(SkillType.Artisanat, "Affûtage de Précision", null, false, false,
                "Forge d'acier et préparation mécanique des tranchants.",
                "Confère +1 Dégât brut aux 3 prochaines attaques réussies avec l'arme.");
            RegisterSpec(SkillType.Artisanat, "Affûtage : Fil Monomoléculaire", "Affûtage de Précision", false, false,
                "Aiguisage nanométrique extrême.",
                "Confère +2 Dégâts bruts permanents aux armes blanches équipées.");
            RegisterSpec(SkillType.Artisanat, "Affûtage : Blindage Trempé", "Affûtage : Fil Monomoléculaire", false, false,
                "Trempe thermique de plaques de blindage.",
                "Renforce les armures métalliques équipées de +1 point d'absorption permanent.");
            RegisterSpec(SkillType.Artisanat, "Affûtage : Acier de Maître d'Hybris", "Affûtage : Blindage Trempé", true, false,
                "Forge légendaire des Maîtres d'Armes.",
                "L'arme ne s'émousse jamais et inflige des coups critiques dès un résultat de 11 ou 12 sur les dés.");

            // --- Arborescence Primordiale Standard (Personnages par défaut) ---
            RegisterSpec(SkillType.MagiePrimale, "Barricade de Racines", null, false, true,
                "Mitose végétale accélérée de terrain.",
                "Fait jaillir instantanément un Couvert 3/4 sur un hexagone ciblé à 4 cases.");
            RegisterSpec(SkillType.MagiePrimale, "Barricade de Racines : Épines de Granit", "Barricade de Racines", false, true,
                "Croissance végétale agressive.",
                "La barricade inflige 3 dégâts de lacération à tout ennemi qui la franchit.");
            RegisterSpec(SkillType.MagiePrimale, "Barricade de Racines : Bastion Vivant", "Barricade de Racines : Épines de Granit", false, true,
                "Densification ligneuse armée.",
                "La barricade absorbe jusqu'à 25 PV de dégâts avant d'être détruite.");
            RegisterSpec(SkillType.MagiePrimale, "Barricade de Racines : Mur d'Orichalque Végétal", "Barricade de Racines : Bastion Vivant", true, true,
                "Forteresse végétale impénétrable.",
                "La barricade s'étend sur 3 cases contiguës et devient un Couvert Total indestructible.");

            RegisterSpec(SkillType.MagiePrimale, "Vignes Constrictrices", null, false, true,
                "Entrave végétale prédatrice.",
                "Applique l'état Immobilisé à une cible jusqu'à 4 cases.");
            RegisterSpec(SkillType.MagiePrimale, "Vignes Constrictrices : Étau Végétal", "Vignes Constrictrices", false, true,
                "Compression thoracique des lianes.",
                "Inflige le statut Asphyxie en plus d'Immobilisé, drainant 1 PE par tour.");
            RegisterSpec(SkillType.MagiePrimale, "Vignes Constrictrices : Moisson Sanguine", "Vignes Constrictrices : Étau Végétal", false, true,
                "Absorption chlorophyllienne vitale.",
                "Draine 3 PV par tour à la cible entravée et les transfère sous forme de soins au lanceur.");
            RegisterSpec(SkillType.MagiePrimale, "Vignes Constrictrices : Étranglement Toxique", "Vignes Constrictrices : Moisson Sanguine", true, true,
                "Constriction venimeuse mortelle.",
                "Applique les statuts Paralysé et Empoisonné permanent à la cible entravée.");

            RegisterSpec(SkillType.MagiePrimale, "Catalyse Tissulaire", null, false, true,
                "Toucher régénérateur d'allié.",
                "Touche un allié adjacent : soigne 5 PV et dissipe Déstabilisé et Étourdi.");
            RegisterSpec(SkillType.MagiePrimale, "Catalyse Tissulaire : Surcroît Vital", "Catalyse Tissulaire", false, true,
                "Irrigation cellulaire de pointe.",
                "Soigne 10 PV au lieu de 5 et purge également le statut Saignement.");
            RegisterSpec(SkillType.MagiePrimale, "Catalyse Tissulaire : Bouclier Végétal", "Catalyse Tissulaire : Surcroît Vital", false, true,
                "Écorce biologique protectrice.",
                "Accorde une armure naturelle d'écorce de +3 pendant 2 tours à l'allié soigné.");
            RegisterSpec(SkillType.MagiePrimale, "Catalyse Tissulaire : Transfusion Symbiotique", "Catalyse Tissulaire : Bouclier Végétal", true, true,
                "Transfert d'énergie régénératrice d'urgence.",
                "Ressuscite un allié tombé au contact avec 50% de ses PV maximaux.");

            RegisterSpec(SkillType.MagiePrimale, "Champ de Ronces", null, false, true,
                "Zone d'épines bioluminescentes.",
                "Zone de rayon 1 hexagone : applique Ralenti et 3 dégâts par case franchie.");
            RegisterSpec(SkillType.MagiePrimale, "Champ de Ronces : Ronces Neurotoxiques", "Champ de Ronces", false, true,
                "Venin végétal paralysant les muscles.",
                "Applique Empoisonné et Ralenti dans une zone de 2 cases.");
            RegisterSpec(SkillType.MagiePrimale, "Champ de Ronces : Enchevêtrement Explosif", "Champ de Ronces : Ronces Neurotoxiques", false, true,
                "Détonation végétale sur commande.",
                "Provoque l'explosion des ronces infligeant 8 dégâts de zone à toutes les unités présentes.");

            RegisterSpec(SkillType.MagiePrimale, "Greffe Cinétique", null, false, true,
                "Transmission d'élan à un allié au contact.",
                "Transfère son bonus de déplacement à un allié au contact (+2 PA mouvement).");
            RegisterSpec(SkillType.MagiePrimale, "Greffe Cinétique : Osmose d'Escouade", "Greffe Cinétique", false, true,
                "Réseau cinétique partagé.",
                "Transfère le bonus de déplacement à tous les alliés situés à moins de 2 cases.");
            RegisterSpec(SkillType.MagiePrimale, "Greffe Cinétique : Frappe Symbiotique", "Greffe Cinétique : Osmose d'Escouade", false, true,
                "Guidage arcanique d'impact.",
                "Permet à un allié adjacent d'exécuter son attaque avec le dé de Magie Primale du lanceur.");

            // --- EXTENSION EXCLUSIVE À MINA : voir MinaCharacter.RegisterSpecializations() ---
            // (Magie Primordiale profonde : Rempart de Silice, Palissade, Arbre-Citadelle,
            //  Vrilles, Épines Neurotoxiques, Lierre Causal, Régénération Osmotique,
            //  Cautérisation Sèveuse, Cœur d'Éclosion, Rosier Noir, Tapis, Jardin,
            //  Pont Végétal, Racines d'Accélération, Réseau Mycélien, Éveil Chlorophyllien...)

            RegisterSpec(SkillType.MagieElementale, "Incinération Pyrocinétique", null, false, false,
                "Combustion spontanée par excitation atomique.",
                "Projette un jet de flammes infligeant 6 dégâts de brûlure et l'état En Feu.");
            RegisterSpec(SkillType.MagieElementale, "Incinération : Flamme Bleue", "Incinération Pyrocinétique", false, false,
                "Plasma thermique pur.",
                "Les dégâts d'incinération ignorent totalement l'armure physique balistique.");
            RegisterSpec(SkillType.MagieElementale, "Incinération : Fournaise Déferlante", "Incinération : Flamme Bleue", false, false,
                "Expansion conique de la flamme.",
                "Le souffle embrase un cône de 3 hexagones adjacents.");
            RegisterSpec(SkillType.MagieElementale, "Incinération : Nova Thermique", "Incinération : Fournaise Déferlante", true, false,
                "Déflagration pyrocinétique suprême.",
                "Provoque une explosion thermique infligeant 12 dégâts absolus dans un rayon de 2 cases.");

            // --- MAGIE ÉLÉMENTALE : AIR, EAU, TERRE (GÉNÉRIQUE — MAGICIENS NORMAUX) ---
            // Le Feu est déjà couvert par l'Incinération Pyrocinétique. Ces trois branches
            // complètent le quatuor classique : vents cisaillants (contrôle), eaux lourdes
            // (entrave), pierre profonde (encaissement). Restrictions héroïques :
            // voir MinaCharacter.IsSkillForbidden / LucasCharacter (voie inversée exclusive).
            RegisterSpec(SkillType.MagieElementale, "Aéromancie", null, false, false,
                "Appel des vents cisaillants.",
                "Dépense 2 PA : rafale à 4 cases — refoule la cible d'1 case et lui impose -1 à sa prochaine attaque.");
            RegisterSpec(SkillType.MagieElementale, "Aéromancie : Lame de Vent", "Aéromancie", false, false,
                "Tranchant d'air supersonique.",
                "La rafale inflige 4 dégâts Absolus et ignore le couvert partiel.");
            RegisterSpec(SkillType.MagieElementale, "Aéromancie : Courant Porteur", "Aéromancie", false, false,
                "Vent ascendant d'escouade.",
                "Déplace un allié consentant d'1 case sans coût de PA ni attaque d'opportunité.");
            RegisterSpec(SkillType.MagieElementale, "Aéromancie : Tempête de Lames", "Aéromancie : Lame de Vent", true, false,
                "Ouragan de shrapnels aériens.",
                "Zone de 2 cases de rayon : 8 dégâts Absolus et cibles À Terre.");

            RegisterSpec(SkillType.MagieElementale, "Hydromancie", null, false, false,
                "Appel des eaux lourdes.",
                "Dépense 2 PA : jet à 4 cases — 3 dégâts Absolus et Ralenti pendant 1 tour.");
            RegisterSpec(SkillType.MagieElementale, "Hydromancie : Étreinte Abyssale", "Hydromancie", false, false,
                "Mâchoire d'eau glacée.",
                "Cible à 4 cases : Immobilisée pendant 1 tour (Delta >= 2 requis contre PJ/PNJ majeur).");
            RegisterSpec(SkillType.MagieElementale, "Hydromancie : Brume Aveuglante", "Hydromancie", false, false,
                "Brouillard opaque.",
                "Nappe sur 1 hexagone pendant 1 tour : -2 à toutes les attaques effectuées à travers.");
            RegisterSpec(SkillType.MagieElementale, "Hydromancie : Raz-de-Marée", "Hydromancie : Étreinte Abyssale", true, false,
                "Déferlante en ligne.",
                "Ligne de 3 cases : 8 dégâts Absolus et cibles À Terre.");

            RegisterSpec(SkillType.MagieElementale, "Géomancie", null, false, false,
                "Appel de la pierre profonde.",
                "Dépense 2 PA : secousse à 4 cases — Déstabilisé pendant 1 tour.");
            RegisterSpec(SkillType.MagieElementale, "Géomancie : Poing Tellurique", "Géomancie", false, false,
                "Uppercut de bedrock.",
                "Cible à 2 cases : 4 dégâts Absolus et À Terre sur Delta >= 2.");
            RegisterSpec(SkillType.MagieElementale, "Géomancie : Peau de Pierre", "Géomancie", false, false,
                "Cuirasse minérale.",
                "Soi ou allié au contact : +2 armure naturelle pendant 2 tours.");
            RegisterSpec(SkillType.MagieElementale, "Géomancie : Séisme Localisé", "Géomancie : Poing Tellurique", true, false,
                "Faille ouverte sous les appuis.",
                "Zone de 2 cases de rayon : 8 dégâts Absolus, cibles À Terre, terrain difficile pendant 2 tours.");

            RegisterSpec(SkillType.MagieEsprit, "Télékinésie", null, false, false,
                "Manipulation de vecteurs de force à distance.",
                "Permet de déplacer des objets ou projeter des ennemis à distance.");
            RegisterSpec(SkillType.MagieEsprit, "Télékinésie : Projection d'Objets", "Télékinésie", false, false,
                "Balistique de débris lourds.",
                "Projette un bloc ou caisse infligeant 6 dégâts contondants jusqu'à 6 cases.");
            RegisterSpec(SkillType.MagieEsprit, "Télékinésie : Barrière Cinétique", "Télékinésie : Projection d'Objets", false, false,
                "Bouclier télékinétique déflecteur.",
                "Arrête net tous les projectiles physiques et grenades pendant 1 tour.");
            RegisterSpec(SkillType.MagieEsprit, "Télékinésie : Fissure Gravifique", "Télékinésie : Barrière Cinétique", true, false,
                "Singularité gravitationnelle concentrée.",
                "Crée un puits de gravité attirant tous les ennemis dans un rayon de 3 cases et les projetant À Terre.");

            // --- MAGIE DE L'ESPRIT : TÉLÉPATHIE & CLAIRVOYANCE (GÉNÉRIQUE — MAGICIENS NORMAUX) ---
            // Voie accessible à tout éveillé à l'Esprit (héros, PNJ). Volontairement en deçà de
            // l'Architecture Neurale héroïque (6 stades, réécriture, possession — voir LucasCharacter) :
            // ici, écoute, lien et prescience — jamais de contrôle ni de réécriture.
            // Restrictions héroïques : voir MinaCharacter.IsSkillForbidden.
            RegisterSpec(SkillType.MagieEsprit, "Télépathie", null, false, false,
                "Écoute empathique des esprits proches.",
                "Dépense 2 PA : établit un lien silencieux avec un allié consentant à 6 cases pendant 1 tour et confère +1 en Intuition contre les mensonges.");
            RegisterSpec(SkillType.MagieEsprit, "Télépathie : Lien Empathique", "Télépathie", false, false,
                "Partage du fardeau émotionnel.",
                "Dépense 2 PA : purge l'état Déstabilisé d'un allié à 3 cases et lui rend 1 PA de réserve.");
            RegisterSpec(SkillType.MagieEsprit, "Télépathie : Sondage de Surface", "Télépathie : Lien Empathique", false, false,
                "Lecture des pensées de surface.",
                "Lit les intentions immédiates d'une cible consentante ou d'un sbire (Delta >= 2 requis contre PJ/PNJ majeur).");
            RegisterSpec(SkillType.MagieEsprit, "Télépathie : Injonction Impérieuse", "Télépathie : Sondage de Surface", true, false,
                "Ordre bref irrésistible.",
                "Impose à un sbire un ordre simple (lâcher son arme, fuir, s'immobiliser 1 tour). Contre un PJ/PNJ majeur, requiert Delta >= 3.");

            RegisterSpec(SkillType.MagieEsprit, "Clairvoyance", null, false, false,
                "Perception à travers les obstacles.",
                "Dépense 2 PA : perçoit à travers une paroi légère et gagne +2 en Observation pendant 1 tour.");
            RegisterSpec(SkillType.MagieEsprit, "Clairvoyance : Œil Distant", "Clairvoyance", false, false,
                "Projection sensorielle à distance.",
                "Projette sa perception jusqu'à 6 cases sans ligne de vue (immobile, 1 tour).");
            RegisterSpec(SkillType.MagieEsprit, "Clairvoyance : Prescience du Danger", "Clairvoyance : Œil Distant", false, false,
                "Frisson d'alerte pré-cognitif.",
                "Conserve une unique réaction défensive à -1 lors d'une embuscade (au lieu d'aucune).");
            RegisterSpec(SkillType.MagieEsprit, "Clairvoyance : Champ de Prescience", "Clairvoyance : Prescience du Danger", true, false,
                "Voile prémonitoire d'escouade.",
                "Dépense 3 PA : tous les alliés à 2 cases gagnent +2 en Esquive pendant 1 tour.");

            // --- FICHES HÉROÏQUES EXTRAITES (DÉLÉGATION PROPRE) ---
            // Tout le contenu Lucas / Mina vit dans LucasCharacter / MinaCharacter.
            MinaCharacter.RegisterSpecializations();
            LucasCharacter.RegisterSpecializations();
        }

        public static void RegisterSpec(SkillType skill, string name, string parent, bool isHidden, bool isVol2, string desc, string mechanic, bool isMinaExclusive = false, bool isLucasExclusive = false)
        {
            var detail = new SpecializationDetail
            {
                Name = name,
                SourceSkill = skill,
                ParentSpecialization = parent,
                IsImprovement = !string.IsNullOrEmpty(parent),
                IsHidden = isHidden,
                IsVolume2 = isVol2,
                IsMinaExclusive = isMinaExclusive,
                IsLucasExclusive = isLucasExclusive,
                Description = desc,
                MechanicalEffect = mechanic
            };

            _registry[name] = detail;
            SpecializationSources[name] = skill;

            if (!string.IsNullOrEmpty(parent))
            {
                SpecializationPrerequisites[name] = parent;
            }

            if (isHidden)
            {
                HiddenSpecializations.Add(name);
            }

            if (isMinaExclusive)
            {
                MinaExclusiveSpecializations.Add(name);
            }

            if (isLucasExclusive)
            {
                LucasExclusiveSpecializations.Add(name);
            }
        }

        public static SpecializationDetail GetSpecializationDetail(string name)
        {
            EnsureRegistryBuilt();
            if (string.IsNullOrWhiteSpace(name)) return null;
            return _registry.TryGetValue(name.Trim(), out var detail) ? detail : null;
        }

        public static IEnumerable<SpecializationDetail> GetAllSpecializations(CharacterSheet sheet = null)
        {
            EnsureRegistryBuilt();
            bool forMina = sheet != null && IsMina(sheet);
            bool forLucas = sheet != null && IsLucas(sheet);

            foreach (var detail in _registry.Values)
            {
                if (forMina)
                {
                    // Fiche héroïque Mina : voir MinaCharacter.IsSkillForbidden.
                    if (MinaCharacter.IsSkillForbidden(detail.SourceSkill))
                        continue;
                    // Ni aux voies exclusives de la fiche héroïque Lucas.
                    if (detail.IsLucasExclusive)
                        continue;
                }
                else if (forLucas)
                {
                    // Fiche héroïque Lucas : voir LucasCharacter.IsSkillForbidden.
                    if (LucasCharacter.IsSkillForbidden(detail.SourceSkill))
                        continue;
                    // Ni aux voies exclusives de la fiche héroïque Mina.
                    if (detail.IsMinaExclusive)
                        continue;
                }
                else
                {
                    // Les personnages par défaut n'ont accès ni aux spécialisations
                    // exclusives des fiches héroïques (Mina / Lucas).
                    if (detail.IsMinaExclusive)
                        continue;
                    if (detail.IsLucasExclusive)
                        continue;
                }

                yield return detail;
            }
        }

        public static List<SpecializationDetail> GetSpecializationsForSkill(SkillType skill)
        {
            EnsureRegistryBuilt();
            var list = new List<SpecializationDetail>();
            foreach (var detail in _registry.Values)
            {
                if (detail.SourceSkill == skill)
                {
                    list.Add(detail);
                }
            }
            return list;
        }

        public static bool IsHiddenSpecialization(string name)
        {
            EnsureRegistryBuilt();
            if (string.IsNullOrWhiteSpace(name)) return false;
            return HiddenSpecializations.Contains(name.Trim());
        }

        public static bool TryGetParentSpecialization(string specializationName, out string parentName)
        {
            EnsureRegistryBuilt();
            parentName = null;
            if (string.IsNullOrWhiteSpace(specializationName)) return false;
            string clean = specializationName.Trim();
            if (SpecializationPrerequisites.TryGetValue(clean, out parentName)) return true;
            int colonIdx = clean.IndexOf(" : ", StringComparison.Ordinal);
            if (colonIdx > 0)
            {
                parentName = clean.Substring(0, colonIdx).Trim();
                return true;
            }
            return false;
        }

        public static bool IsVolume2Unlocked()
        {
            var director = Story.ScenarioDirector.Instance;
            return director != null && director.State != null && director.State.HasFlag("FLAG_MINALIA_AWAKENED");
        }

        public static bool IsVolume2Specialization(string specializationName)
        {
            EnsureRegistryBuilt();
            if (string.IsNullOrWhiteSpace(specializationName)) return false;
            string s = specializationName.Trim();
            if (_registry.TryGetValue(s, out var detail)) return detail.IsVolume2;
            if (TryGetParentSpecialization(s, out string parent))
            {
                if (_registry.TryGetValue(parent, out var pDetail)) return pDetail.IsVolume2;
            }
            return false;
        }

        public static bool TryGetSpecializationSource(string specializationName, out SkillType source)
        {
            EnsureRegistryBuilt();
            source = default;
            if (string.IsNullOrWhiteSpace(specializationName)) return false;
            string clean = specializationName.Trim();
            if (SpecializationSources.TryGetValue(clean, out source)) return true;
            if (SpecializationPrerequisites.TryGetValue(clean, out string pre))
            {
                return TryGetSpecializationSource(pre, out source);
            }
            if (TryGetParentSpecialization(clean, out string parent))
            {
                return TryGetSpecializationSource(parent, out source);
            }
            return false;
        }

        /// <summary>
        /// Débloque une spécialisation ou une amélioration de spécialisation (5 XP).
        /// Tarif normal si payé depuis la réserve de la compétence source (ou XP libre), doublé sinon.
        /// </summary>
        public static bool UnlockSpecialization(CharacterSheet sheet, string specializationName, out string message, SkillType? associatedSkill = null, bool forceFree = false)
        {
            if (sheet == null || string.IsNullOrWhiteSpace(specializationName))
            {
                message = "Spécialisation invalide.";
                return false;
            }
            if (sheet.UnlockedSpecializations.Contains(specializationName))
            {
                message = "Spécialisation déjà acquise.";
                return false;
            }
            sheet.MigrateLegacyBluntSkill();

            SkillType target = associatedSkill ?? (TryGetSpecializationSource(specializationName, out SkillType s) ? s : (SkillType?)null) ?? SkillType.Academie;

            if (IsMina(sheet) && MinaCharacter.IsSkillForbidden(target))
            {
                message = MinaCharacter.ForbiddenSkillMessage(target);
                return false;
            }

            if (IsLucas(sheet) && LucasCharacter.IsSkillForbidden(target))
            {
                message = LucasCharacter.ForbiddenSkillMessage(target);
                return false;
            }

            if (!IsMina(sheet) && IsMinaExclusiveSpecialization(specializationName))
            {
                message = MinaCharacter.ExclusiveSpecializationMessage(specializationName);
                return false;
            }

            if (!IsLucas(sheet) && IsLucasExclusiveSpecialization(specializationName))
            {
                message = LucasCharacter.ExclusiveSpecializationMessage(specializationName);
                return false;
            }

            if (forceFree || MinaCharacter.IsInnateUnlocked(specializationName, sheet))
            {
                sheet.UnlockedSpecializations.Add(specializationName);
                message = specializationName.Equals(MinaCharacter.InnateSpecialization, StringComparison.OrdinalIgnoreCase)
                    ? $"★ Maîtrise innée [{MinaCharacter.InnateSpecialization}] synchronisée sans dépense d'XP !"
                    : $"[CHEAT] Maîtrise '{specializationName}' débloquée immédiatement sans coût !";
                return true;
            }

            if (forceFree || LucasCharacter.IsInnateUnlocked(specializationName, sheet))
            {
                sheet.UnlockedSpecializations.Add(specializationName);
                message = $"★ Maîtrise innée [{LucasCharacter.InnateSpecialization}] synchronisée sans dépense d'XP !";
                return true;
            }

            if (SpecializationPrerequisites.TryGetValue(specializationName, out string reqPre))
            {
                if (!sheet.UnlockedSpecializations.Contains(reqPre))
                {
                    message = $"Prérequis non satisfait : requiert la spécialisation parente '{reqPre}'.";
                    return false;
                }
            }
            else if (TryGetParentSpecialization(specializationName, out string parentSpec))
            {
                if (!sheet.UnlockedSpecializations.Contains(parentSpec))
                {
                    message = $"Prérequis non satisfait : requiert la spécialisation parente '{parentSpec}'.";
                    return false;
                }
            }

            if (IsVolume2Specialization(specializationName))
            {
                if (!IsVolume2Unlocked())
                {
                    message = $"[VERROU CAUSAL] '{specializationName}' requiert l'éveil du Volume II (Flag : FLAG_MINALIA_AWAKENED).";
                    return false;
                }
            }

            if (!SpendForSkill(sheet, target, XP_COST_SPECIALIZATION, out string funding))
            {
                string src = SkillDefinitions.GetDisplayName(target);
                message = $"XP insuffisant : {funding} (coût {XP_COST_SPECIALIZATION} XP liés {src} ou libre, {XP_COST_SPECIALIZATION * OFF_SKILL_MULTIPLIER} si croisé).";
                return false;
            }

            sheet.UnlockedSpecializations.Add(specializationName);
            message = $"Spécialisation '{specializationName}' débloquée (source {SkillDefinitions.GetDisplayName(target)}) ! [{funding}]";
            return true;
        }

        /// <summary>
        /// Apprend un sort conçu dans l'Atelier Arcanotech (Règle : Coût XP = Coût PA).
        /// Affinité par défaut : Magie Élémentale. Payé depuis une autre voie → coût doublé.
        /// </summary>
        public static bool LearnModularSpell(CharacterSheet sheet, NythariteSpell spell, out string message, SkillType? affinitySkill = null)
        {
            if (sheet == null || spell == null)
            {
                message = "Sort invalide.";
                return false;
            }
            SkillType target = affinitySkill ?? SkillType.MagieElementale;
            int cost = Math.Max(1, spell.CreationXpCost);

            if (IsLucas(sheet) && target == SkillType.MagiePrimale)
            {
                message = LucasCharacter.ForbiddenSpellMessage();
                return false;
            }

            if (!SpendForSkill(sheet, target, cost, out string funding))
            {
                message = $"XP insuffisant : {funding} pour canaliser {spell.Name} (coût {cost} XP liés {SkillDefinitions.GetDisplayName(target)} ou libre, {cost * OFF_SKILL_MULTIPLIER} si croisé).";
                return false;
            }

            sheet.LearnedSpells.Add(spell);
            message = $"Sort '{spell.Name}' gravé dans les voies neurales ! (Coût Combat : {spell.ActionPointCost} PA). [{funding}]";
            return true;
        }

        // ------------------------------------------------------------------
        // RESET : remise à zéro de l'arbre de progression (création)
        // ------------------------------------------------------------------

        /// <summary>
        /// Remet à zéro l'arbre de progression du personnage (Livre I §5) :
        /// - tous les niveaux d'entraînement repassent à 0 (payants ET gratuits) ;
        /// - toutes les cases de progression sont effacées ;
        /// - tout l'XP PAYÉ est restauré en XP libre (remboursement au coût nominal) :
        ///   seuls les niveaux payés (entraînements), spécialisations et banques
        ///   liées non dépensées sont reversés dans <see cref="CharacterSheet.AvailableXP"/> ;
        /// - les entraînements GRATUITS d'Érudition ne sont jamais remboursés (0 XP dépensé,
        ///   hors XP total) : le compteur <c>FreeTrainingsUsed</c> est simplement remis à 0,
        ///   ce qui rend le budget à nouveau disponible ;
        /// - l'XP total (dépensé) est décrémenté des seuls montants payés, jamais en dessous de 0.
        /// Les sorts appris ne sont pas touchés sauf si <paramref name="includeSpells"/> est vrai
        /// (remboursement au coût de création nominal, sans surcoût croisé).
        /// Pensé pour la phase de création : permet de tout reprendre sans perdre d'XP payé.
        /// </summary>
        public static bool ResetProgressionTree(CharacterSheet sheet, out string message, bool includeSpells = false)
        {
            if (sheet == null)
            {
                message = "Fiche invalide.";
                return false;
            }

            int totalLevels = sheet.GetTotalTrainingLevels();
            int freeUsed = Math.Max(0, sheet.FreeTrainingsUsed);
            // Les gratuits sont imputés en premier : seuls les niveaux payés sont remboursés.
            int paidLevels = Math.Max(0, totalLevels - Math.Min(freeUsed, totalLevels));
            int freedSlots = Math.Min(freeUsed, totalLevels);
            int refundedTrainings = paidLevels * XP_COST_TRAINING;
            int refundedSpecs = 0;
            int refundedReserves = 0;
            int presetFreeSpecs = 0;

            // Package de création (archétype de classe) : les entraînements du preset
            // sont déjà comptés comme gratuits (FreeTrainingsUsed = total preset, voir
            // CharacterClassDefinition.ApplyToSheet) donc jamais remboursés ci-dessus.
            // Les spécialisations du preset sont gratuites aussi : on ne rembourse que
            // celles hors preset pour éviter tout farm XP (charger → reset → encaisser).
            if (!string.IsNullOrWhiteSpace(sheet.ActiveClassId))
            {
                try
                {
                    var preset = Classes.CharacterClassCatalog.GetById(sheet.ActiveClassId);
                    if (preset?.StartingSpecializations != null && sheet.UnlockedSpecializations != null)
                    {
                        for (int i = 0; i < preset.StartingSpecializations.Count; i++)
                        {
                            string s = preset.StartingSpecializations[i];
                            if (!string.IsNullOrWhiteSpace(s) && sheet.UnlockedSpecializations.Contains(s))
                                presetFreeSpecs++;
                        }
                    }
                }
                catch { presetFreeSpecs = 0; }
            }

            if (sheet.Skills != null)
            {
                for (int i = 0; i < sheet.Skills.Count; i++)
                {
                    var e = sheet.Skills[i];
                    if (e == null) continue;
                    e.TrainingLevel = 0;
                    e.ProgressTicks = 0;
                    if (e.ReserveXP > 0)
                    {
                        refundedReserves += e.ReserveXP;
                        e.ReserveXP = 0;
                    }
                    if (e.ProgressTicks < 0) e.ProgressTicks = 0;
                    if (e.ReserveXP < 0) e.ReserveXP = 0;
                }
            }
            sheet.FreeTrainingsUsed = 0;

            int freeSpecs = presetFreeSpecs;
            if (sheet.UnlockedSpecializations != null)
            {
                for (int i = 0; i < sheet.UnlockedSpecializations.Count; i++)
                {
                    string s = sheet.UnlockedSpecializations[i];
                    if (string.IsNullOrWhiteSpace(s)) continue;
                    if (MinaCharacter.IsInnateUnlocked(s, sheet) || LucasCharacter.IsInnateUnlocked(s, sheet))
                        freeSpecs++;
                }
            }

            if (sheet.UnlockedSpecializations != null && sheet.UnlockedSpecializations.Count > 0)
            {
                int paidSpecs = Math.Max(0, sheet.UnlockedSpecializations.Count - Math.Max(0, freeSpecs));
                refundedSpecs = paidSpecs * XP_COST_SPECIALIZATION;
                sheet.UnlockedSpecializations.Clear();

                if (IsMina(sheet)) sheet.UnlockedSpecializations.Add(MinaCharacter.InnateSpecialization);
                if (IsLucas(sheet)) sheet.UnlockedSpecializations.Add(LucasCharacter.InnateSpecialization);
            }

            int refundedSpells = 0;
            if (includeSpells && sheet.LearnedSpells != null && sheet.LearnedSpells.Count > 0)
            {
                for (int i = 0; i < sheet.LearnedSpells.Count; i++)
                {
                    var sp = sheet.LearnedSpells[i];
                    if (sp != null) refundedSpells += Math.Max(1, sp.CreationXpCost);
                }
                sheet.LearnedSpells.Clear();
            }

            int refundedAttributes = 0;
            if (sheet.AttributeUpgradesPurchased != null && sheet.AttributeUpgradesPurchased.Length >= 9)
            {
                for (int i = 0; i < 9; i++)
                {
                    int count = sheet.AttributeUpgradesPurchased[i];
                    while (count > 0)
                    {
                        int currentVal = sheet.GetBaseAttributeValue(i);
                        refundedAttributes += currentVal;
                        sheet.SetBaseAttributeValue(i, Math.Max(1, currentVal - 1));
                        count--;
                    }
                    sheet.AttributeUpgradesPurchased[i] = 0;
                }
            }

            int spentRefund = refundedTrainings + refundedSpecs + refundedSpells + refundedAttributes;
            int totalRefund = spentRefund + refundedReserves;

            sheet.AvailableXP = Math.Max(0, sheet.AvailableXP) + totalRefund;
            sheet.TotalSpentXP = Math.Max(0, sheet.TotalSpentXP - spentRefund);

            message = $"Arbre de progression réinitialisé : entraînements à 0, cases effacées, {totalRefund} XP restaurés en XP libre " +
                      $"(entraînements payés {refundedTrainings} + spés {refundedSpecs}" +
                      (includeSpells ? $" + sorts {refundedSpells}" : "") +
                      (refundedAttributes > 0 ? $" + attributs {refundedAttributes}" : "") +
                      $" + banques liées {refundedReserves}). {freedSlots} entraînement(s) gratuit(s) rendus au budget Érudition. XP total (dépensé) : {sheet.TotalSpentXP}.";
            return true;
        }
    }
}

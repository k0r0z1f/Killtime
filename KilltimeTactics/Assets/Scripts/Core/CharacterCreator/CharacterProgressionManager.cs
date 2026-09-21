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

        /// <summary>Table Spécialisation → Compétence source (Livre III §13).</summary>
        public static readonly Dictionary<string, SkillType> SpecializationSources =
            new Dictionary<string, SkillType>(StringComparer.OrdinalIgnoreCase)
            {
                { "Maniement de l'Épée", SkillType.ManiementArmes },
                { "Escrime", SkillType.ArmesPercantes },
                { "Escrime / Rapière", SkillType.ArmesPercantes },
                { "Marteau de Guerre", SkillType.ArmesContondantes },
                { "Pistolet & Tir Rapide", SkillType.Ballistique },
                { "Armes Lourdes (Canons)", SkillType.Ballistique },
                { "Arts Martiaux", SkillType.MainsNues },
                { "Bloquer", SkillType.ManiementArmes },
                { "Chirurgie", SkillType.MedecineAvancee },
                { "Médecine & Chirurgie", SkillType.MedecineAvancee },
                { "Télékinésie", SkillType.MagieEsprit },
                { "Tromper", SkillType.Communication },
                { "Négocier", SkillType.Communication },
                { "Intimider", SkillType.Intimidation },
                { "Leadership", SkillType.Leadership },
            };

        public static bool TryGetSpecializationSource(string specializationName, out SkillType source)
        {
            source = default;
            if (string.IsNullOrWhiteSpace(specializationName)) return false;
            return SpecializationSources.TryGetValue(specializationName.Trim(), out source);
        }

        /// <summary>
        /// Débloque une spécialisation (5 XP). Tarif normal si payé depuis la réserve de
        /// la compétence source (ou XP libre), doublé sinon (Livre I §5.4 / III §13).
        /// </summary>
        public static bool UnlockSpecialization(CharacterSheet sheet, string specializationName, out string message, SkillType? associatedSkill = null)
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

            SkillType target = associatedSkill ?? (TryGetSpecializationSource(specializationName, out SkillType s) ? s : (SkillType?)null) ?? SkillType.Academie;

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

            if (sheet.UnlockedSpecializations != null && sheet.UnlockedSpecializations.Count > 0)
            {
                refundedSpecs = sheet.UnlockedSpecializations.Count * XP_COST_SPECIALIZATION;
                sheet.UnlockedSpecializations.Clear();
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

            int spentRefund = refundedTrainings + refundedSpecs + refundedSpells;
            int totalRefund = spentRefund + refundedReserves;

            sheet.AvailableXP = Math.Max(0, sheet.AvailableXP) + totalRefund;
            sheet.TotalSpentXP = Math.Max(0, sheet.TotalSpentXP - spentRefund);

            message = $"Arbre de progression réinitialisé : entraînements à 0, cases effacées, {totalRefund} XP restaurés en XP libre " +
                      $"(entraînements payés {refundedTrainings} + spés {refundedSpecs}" +
                      (includeSpells ? $" + sorts {refundedSpells}" : "") +
                      $" + banques liées {refundedReserves}). {freedSlots} entraînement(s) gratuit(s) rendus au budget Érudition. XP total (dépensé) : {sheet.TotalSpentXP}.";
            return true;
        }
    }
}

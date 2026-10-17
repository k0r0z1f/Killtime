using System;
using System.Collections.Generic;
using Killtime.Core.Arcanotech;

namespace Killtime.Core.Character
{
    public partial class CharacterProgressionManager
    {
        // CharacterProgressionManager.Funding.cs — Moteur monetaire XP : couts, plan de financement, depenses skill.

        public static int XP_COST_TRAINING => Rules.CoreRulesConfig.Instance.XPCostTraining;
        public static int XP_COST_SPECIALIZATION => Rules.CoreRulesConfig.Instance.XPCostSpecialization;
        public static int OFF_SKILL_MULTIPLIER => Math.Max(2, Rules.CoreRulesConfig.Instance.XPOffSkillCostMultiplier);

        public const int MAX_SKILL_TRAINING = 3;


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

    }
}

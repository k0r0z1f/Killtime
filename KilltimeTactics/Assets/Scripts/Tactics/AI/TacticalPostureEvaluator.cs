using Killtime.Tactics.Grid;

namespace Killtime.Tactics.AI
{
    /// <summary>
    /// Évaluation de posture tactique extraite de TacticalAIController (phase 1).
    /// Pur C# : le contrôleur réunit les entrées (PV, létalité, allié blessé
    /// adjacent, couvert via GetCoverLevel) puis délègue ici. Ordre de priorité
    /// strictement identique à l'original :
    /// repli mortel → all-in létal → soutien → hit-and-run → flanc → frappe mesurée.
    /// Ne lit jamais la mise adverse (respect du duel aveugle).
    /// </summary>
    public static class TacticalPostureEvaluator
    {
        public static TacticalPosture Evaluate(
            float hpRatio,
            float retreatHealthRatio,
            bool canKillTargetThisTurn,
            bool hasWoundedAllyAdjacent,
            int currentAP,
            AIPersonality personality,
            int dist,
            CoverType targetCover)
        {
            bool isMortalDanger = hpRatio <= retreatHealthRatio;

            if (isMortalDanger && !canKillTargetThisTurn)
            {
                return TacticalPosture.TacticalRetreat;
            }

            if (canKillTargetThisTurn)
            {
                return TacticalPosture.AllInLethal;
            }

            if (personality != AIPersonality.Aggressive
                && currentAP >= AIPersonalityProfile.SupportMinAP
                && hasWoundedAllyAdjacent)
            {
                return TacticalPosture.SupportAndHeal;
            }

            if (AIPersonalityProfile.SeeksHitAndRun(personality)
                && dist == 1 && currentAP >= AIPersonalityProfile.HitAndRunMinAP)
            {
                return TacticalPosture.HitAndRun;
            }

            if (targetCover == CoverType.ThreeQuarters || targetCover == CoverType.Full)
            {
                return TacticalPosture.FlankAndSuppress;
            }

            return TacticalPosture.CalculatedStrike;
        }
    }
}

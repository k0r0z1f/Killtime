using UnityEngine;
using Killtime.Core.Combat;
using Killtime.Tactics.Grid;

namespace Killtime.Tactics.AI
{
    /// <summary>
    /// Doctrine IA extraite du god-class TacticalAIController (phase 1 du refactor).
    /// Pur C# : aucune ref MonoBehaviour, aucun singleton, testable en EditMode.
    /// Regroupe les constantes et règles liées à AIPersonality afin que le
    /// contrôleur ne contienne plus de switch doctrinaux dispersés.
    /// Comportement strictement identique à l'original (cf. GetRequiredDefensiveReserve).
    /// </summary>
    public static class AIPersonalityProfile
    {
        /// <summary>Seuil PV allié déclenchant SupportAndHeal (voir EvaluateTacticalPosture).</summary>
        public const float WoundedAllyHpRatio = 0.35f;

        /// <summary>Coût PA minimal pour envisager une mission de soutien.</summary>
        public const int SupportMinAP = 3;

        /// <summary>Coût PA minimal pour le HitAndRun du Tactician/Survivor.</summary>
        public const int HitAndRunMinAP = 4;

        public static bool SeeksMedicalSupport(AIPersonality personality)
        {
            return personality != AIPersonality.Aggressive;
        }

        public static bool SeeksHitAndRun(AIPersonality personality)
        {
            return personality == AIPersonality.Tactician
                || personality == AIPersonality.Survivor;
        }

        /// <summary>
        /// Réserve défensive (RD-IA03) : AllInLethal et Aggressive = 0,
        /// AlertedPatrol = 1, sinon réglage clampé 0-2.
        /// Extrait verbatim de TacticalAIController.GetRequiredDefensiveReserve.
        /// </summary>
        public static int GetDefensiveReserve(
            AIPersonality personality, TacticalPosture posture, int baseReserve)
        {
            if (posture == TacticalPosture.AllInLethal) return 0;
            if (personality == AIPersonality.Aggressive) return 0;
            if (posture == TacticalPosture.AlertedPatrol) return 1;
            return Mathf.Clamp(baseReserve, 0, 2);
        }

        /// <summary>
        /// RD-031 : posture d'opportunité du round (extrait verbatim du contrôleur).
        /// Grappler (Athlétisme) → Blocage ; rapide (RAP 5+) → Poursuite ;
        /// esquiveur → Balayage ; défaut → Frappe gratuite.
        /// </summary>
        public static OpportunityReactionType ChooseOpportunityStance(
            bool hasAthletisme, int rapidite, bool hasEsquive)
        {
            if (hasAthletisme)
                return OpportunityReactionType.Block;
            if (rapidite >= 5)
                return OpportunityReactionType.Follow;
            if (hasEsquive)
                return OpportunityReactionType.Trip;
            return OpportunityReactionType.Strike;
        }

        /// <summary>Bonus doctrinaux appliqués au score de l'hex de combat (voir AIMovementSolver).</summary>
        public static float CombatHexDoctrineBonus(
            AIPersonality personality, CoverType targetCoverFromHex, CoverType defCoverAgainstTarget)
        {
            float bonus = 0f;
            if (personality == AIPersonality.Survivor)
                bonus += (defCoverAgainstTarget != CoverType.None ? 25f : -15f);
            if (personality == AIPersonality.Aggressive)
                bonus += (targetCoverFromHex == CoverType.None ? 30f : 0f);
            return bonus;
        }
    }
}

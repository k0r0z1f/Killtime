using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics.Grid;

namespace Killtime.Tactics.AI
{
    /// <summary>
    /// Scoring de mouvement extrait de TacticalAIController (phase 1).
    /// Pur C# : chaque Score* est une formule verbatim, testable sans grille.
    /// Le contrôleur garde l'accès grille/pathfinder (Unity-bound) et délègue
    /// uniquement le calcul du score. Le jitter de fuite panique est injecté
    /// en paramètre (l'original appelait UnityEngine.Random dans la boucle,
    /// ce qui rendait le scoring non déterministe et non testable).
    /// </summary>
    public static class AIMovementSolver
    {
        /// <summary>
        /// Score d'un hex de combat (FindOptimalCombatHex, lignes ~2330-2355).
        /// crossfireBonus = EvaluatePrecomputedCrossfireBonus déjà calculé.
        /// distancePenalty = CurrentCoords.DistanceTo(hex) * 3.5f (pré-calculé
        /// par l'appelant pour garder cette méthode sans dépendance grille).
        /// </summary>
        public static float ScoreCombatHex(
            CoverType targetCoverFromHex,
            CoverType defCoverAgainstTarget,
            bool isRanged,
            int dist,
            float crossfireBonus,
            float distancePenalty,
            AIPersonality personality)
        {
            float score = 50f;

            // Flanking offensif : suppression du couvert adverse
            if (targetCoverFromHex == CoverType.None) score += 45f;
            else if (targetCoverFromHex == CoverType.Half) score += 20f;

            // Couvert défensif : protection contre la riposte
            if (defCoverAgainstTarget == CoverType.ThreeQuarters) score += 40f;
            else if (defCoverAgainstTarget == CoverType.Half) score += 25f;

            // Tir croisé d'escouade (Livre V §21.3)
            if (isRanged)
            {
                score += crossfireBonus;
            }

            // Distance optimale pour tireur
            if (isRanged && dist >= 3 && dist <= 6) score += 15f;

            // Modulateurs doctrinaux
            score += AIPersonalityProfile.CombatHexDoctrineBonus(
                personality, targetCoverFromHex, defCoverAgainstTarget);

            // Coût d'approche (sans A* redondant dans la boucle)
            score -= distancePenalty;

            return score;
        }

        /// <summary>
        /// Filtre de bande de portée (avant tout scan de couvert coûteux).
        /// Reproduit les deux premiers `continue` de la boucle originale :
        /// doit s'appeler AVANT GetCoverLevel (scan 28 rayons Cyrus-Beck).
        /// </summary>
        public static bool IsRangeEligible(bool isRanged, int dist, int maxRange)
        {
            if (isRanged && (dist < 2 || dist > maxRange)) return false;
            if (!isRanged && dist != 1) return false;
            return true;
        }

        /// <summary>
        /// Filtre d'éligibilité complet d'un hex de combat (portée + couvert).
        /// Reproduit l'ensemble des `continue` de la boucle originale.
        /// </summary>
        public static bool IsCombatHexEligible(
            bool isRanged, int dist, int maxRange, CoverType targetCoverFromHex)
        {
            if (!IsRangeEligible(isRanged, dist, maxRange)) return false;
            if (targetCoverFromHex == CoverType.Full) return false;
            return true;
        }

        /// <summary>
        /// Score de repli couvert (FindBestCoveredRetreatHex) : distance * 10
        /// + bonus couvert Full 65 / ThreeQuarters 45 / Half 25.
        /// </summary>
        public static float ScoreRetreatHex(int distToThreat, CoverType coverFromThreat)
        {
            float score = distToThreat * 10f;
            if (coverFromThreat == CoverType.Full) score += 65f;
            else if (coverFromThreat == CoverType.ThreeQuarters) score += 45f;
            else if (coverFromThreat == CoverType.Half) score += 25f;
            return score;
        }

        /// <summary>
        /// Score de fuite panique (FindBestPanickedFleeHex) : distance * 10
        /// + jitter injecté + 15 si un couvert quelconque.
        /// </summary>
        public static float ScoreFleeHex(int distToThreat, CoverType cover, float jitter)
        {
            float score = distToThreat * 10f + jitter;
            if (cover != CoverType.None) score += 15f;
            return score;
        }

        /// <summary>
        /// Troncature d'un chemin A* par budget PA (RD-115 sous-lot).
        /// Factorise verbatim les 3 boucles dupliquées de
        /// FindPathTowardsTarget / FindPathTowardsRangedPosition /
        /// FindPathTowardsCoords : cumul des coûts, arrêt au dépassement de
        /// budget ou à la première case bloquée. Les coûts et blocages sont
        /// injectés (le contrôleur lit la grille, les tests passent des
        /// lambdas) : aucune dépendance grille/pathfinder ici.
        /// lastStepIndex borne la progression (tir à distance : on s'arrête
        /// à la première case de tir, pas au bout du chemin).
        /// Retourne null si moins de 2 cases conservées (verbatim).
        /// </summary>
        public static List<HexCoordinates> TruncatePathByBudget(
            IList<HexCoordinates> fullPath,
            int apBudget,
            Func<HexCoordinates, int> stepCost,
            Func<HexCoordinates, bool> isBlocked,
            int lastStepIndex,
            out int spentCost)
        {
            spentCost = 0;
            if (fullPath == null || fullPath.Count <= 1) return null;
            if (stepCost == null || isBlocked == null) return null;

            var truncatedPath = new List<HexCoordinates> { fullPath[0] };
            int accumulatedCost = 0;
            int last = Math.Min(lastStepIndex, fullPath.Count - 1);

            for (int i = 1; i <= last; i++)
            {
                var step = fullPath[i];
                int cost = stepCost(step);

                if (accumulatedCost + cost > apBudget) break;
                if (isBlocked(step)) break;

                accumulatedCost += cost;
                truncatedPath.Add(step);
            }

            spentCost = accumulatedCost;
            return truncatedPath.Count > 1 ? truncatedPath : null;
        }

        /// <summary>
        /// Bonus de tir croisé en L (50°-130°, optimum 90°) extrait verbatim de
        /// EvaluatePrecomputedCrossfireBonus. Prend des positions monde déjà
        /// calculées : aucune dépendance grille/transform, testable pur.
        /// </summary>
        /// <param name="toCandidate">direction cible → hex candidat (Y ignoré).</param>
        /// <param name="toAllies">directions cible → alliés en ligne de tir.</param>
        public static float ScoreCrossfire(Vector3 toCandidate, List<Vector3> toAllies)
        {
            Vector3 flat = toCandidate;
            flat.y = 0f;
            if (flat.sqrMagnitude < 0.01f) return 0f;
            flat.Normalize();

            float best = 0f;
            if (toAllies == null) return 0f;
            for (int i = 0; i < toAllies.Count; i++)
            {
                Vector3 ally = toAllies[i];
                ally.y = 0f;
                if (ally.sqrMagnitude < 0.01f) continue;
                ally.Normalize();

                float angle = Vector3.Angle(flat, ally);
                if (angle >= 50f && angle <= 130f)
                {
                    float angleQuality = 1.0f - (Mathf.Abs(90f - angle) / 40f);
                    float s = 35f * Mathf.Clamp01(angleQuality);
                    if (s > best) best = s;
                }
            }
            return best;
        }
    }
}

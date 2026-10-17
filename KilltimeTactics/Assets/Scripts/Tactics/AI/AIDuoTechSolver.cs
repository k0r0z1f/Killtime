using System;
using System.Collections.Generic;
using Killtime.Core.Combat;
using Killtime.Tactics.Grid;

namespace Killtime.Tactics.AI
{
    /// <summary>
    /// Duo-Tech automatique pour l'IA (RD-105) — Sillage Igné sans tissage souris.
    ///
    /// L'IA n'ouvre jamais le WeaveController (souris T1/T2) : ce solver pur choisit
    /// la ligne de dash Mina (2 à 4 ennemis alignés) et évalue la synchro Lucas par
    /// DISTANCE au sillage, au lieu du timing d'anticipation manuel :
    /// Lucas collé au sillage = Risky (friendly fire 3 bruts sur Mina),
    /// à bonne distance = Perfect, trop loin = Late (effet divisé).
    ///
    /// Pur C# EditMode : axial uniquement, aucune scène, aucun singleton.
    /// Le paiement atomique + la limite 1/round restent dans DuoTechRegistry
    /// (TryDeclarePayment), la téléportation + les dégâts dans la colle
    /// TacticalAIController.DuoTech.
    /// </summary>
    public struct AIDuoTechSillagePlan
    {
        public HexCoordinates DashDest;
        public List<int> TargetIndexes;
        public WeaveSyncGrade Grade;
    }

    public static class AIDuoTechSolver
    {
        public const int MaxBinomeDist = 2;
        public const int MinTargets = 2;
        public const int MaxTargets = 4;
        public const int MaxDashRange = 6;
        public const float AlignTol = 0.6f;
        public const float RiskyLucasDist = 1.2f;
        public const float PerfectLucasDist = 5.0f;

        private const float Sqrt3 = 1.7320508f;

        /// <summary>
        /// Projection axiale -> plan XZ (rayon 1), miroir de
        /// HexCoordinates.ToWorldPosition(radius: 1). Y ignoré (WeaveVec2 2D).
        /// </summary>
        public static WeaveVec2 AxialToVec2(HexCoordinates c)
        {
            return new WeaveVec2(Sqrt3 * (c.Q + c.R * 0.5f), 1.5f * c.R);
        }

        private static float DistPointToSegment(WeaveVec2 p, WeaveVec2 a, WeaveVec2 b)
        {
            float abx = b.X - a.X;
            float aby = b.Y - a.Y;
            float lenSq = abx * abx + aby * aby;
            if (lenSq < 1e-8f) return WeaveVec2.Distance(a, p);
            float t = ((p.X - a.X) * abx + (p.Y - a.Y) * aby) / lenSq;
            t = Math.Max(0f, Math.Min(1f, t));
            float cx = a.X + abx * t;
            float cy = a.Y + aby * t;
            float dx = p.X - cx;
            float dy = p.Y - cy;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>
        /// Cherche le meilleur dash Mina -> fin : le segment qui attrape le plus
        /// d'ennemis (2 à 4, à portée de dash), à tolérance d'alignement manuelle.
        /// Grade Lucas évalué par distance au segment. Faux si aucun alignement.
        /// </summary>
        public static bool TryPlanSillage(
            HexCoordinates mina, HexCoordinates lucas,
            IList<HexCoordinates> enemies, out AIDuoTechSillagePlan plan)
        {
            plan = default;
            if (enemies == null || enemies.Count < MinTargets) return false;

            var minaV = AxialToVec2(mina);
            var lucasV = AxialToVec2(lucas);

            int bestCount = 0;
            float bestLen = float.MaxValue;
            var bestIdx = new List<int>();
            HexCoordinates bestDest = default;

            for (int e = 0; e < enemies.Count; e++)
            {
                if (mina.DistanceTo(enemies[e]) > MaxDashRange) continue;
                var endV = AxialToVec2(enemies[e]);
                float dashLen = WeaveVec2.Distance(minaV, endV);
                if (dashLen < 1e-5f) continue;

                var caught = new List<(int idx, float proj)>();
                for (int i = 0; i < enemies.Count; i++)
                {
                    if (mina.DistanceTo(enemies[i]) > MaxDashRange) continue;
                    var p = AxialToVec2(enemies[i]);
                    if (DistPointToSegment(p, minaV, endV) <= AlignTol)
                    {
                        float abx = endV.X - minaV.X;
                        float aby = endV.Y - minaV.Y;
                        float lenSq = abx * abx + aby * aby;
                        float t = lenSq < 1e-8f ? 0f : ((p.X - minaV.X) * abx + (p.Y - minaV.Y) * aby) / lenSq;
                        caught.Add((i, t));
                    }
                }
                if (caught.Count < MinTargets) continue;

                caught.Sort((x, y) => x.proj.CompareTo(y.proj));
                int take = Math.Min(MaxTargets, caught.Count);
                var idx = new List<int>(take);
                for (int k = 0; k < take; k++) idx.Add(caught[k].idx);
                HexCoordinates dest = enemies[caught[take - 1].idx];

                if (take > bestCount || (take == bestCount && dashLen < bestLen))
                {
                    bestCount = take;
                    bestLen = dashLen;
                    bestIdx = idx;
                    bestDest = dest;
                }
            }

            if (bestCount < MinTargets) return false;

            var destV = AxialToVec2(bestDest);
            float lucasDist = DistPointToSegment(lucasV, minaV, destV);
            WeaveSyncGrade grade = lucasDist < RiskyLucasDist
                ? WeaveSyncGrade.Risky
                : lucasDist <= PerfectLucasDist ? WeaveSyncGrade.Perfect : WeaveSyncGrade.Late;

            plan = new AIDuoTechSillagePlan
            {
                DashDest = bestDest,
                TargetIndexes = bestIdx,
                Grade = grade
            };
            return true;
        }

        /// <summary>
        /// Risky brûle Mina (3 bruts friendly fire) : seulement en AllInLethal /
        /// doctrine Aggressive. Perfect et Late toujours joués (Late = 4+1, sans
        /// brûlure, mieux qu'un tir manqué).
        /// </summary>
        public static bool ShouldExecuteSillage(WeaveSyncGrade grade, bool allIn)
        {
            return grade == WeaveSyncGrade.Perfect
                || grade == WeaveSyncGrade.Late
                || (grade == WeaveSyncGrade.Risky && allIn);
        }
    }
}

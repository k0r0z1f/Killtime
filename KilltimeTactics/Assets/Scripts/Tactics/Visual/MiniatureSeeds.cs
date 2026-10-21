using System.Collections.Generic;
using UnityEngine;

namespace Killtime.Tactics.Visual
{
    /// <summary>
    /// RD-207 : maths pures du randomizer miniature (déterministes, testables hors Play).
    /// Extraites de MiniatureRandomizer/MiniatureLookRig pour garder les composants <200 l.
    /// </summary>
    public static class MiniatureSeeds
    {
        public const float MinSpeed = 0.85f;
        public const float MaxSpeed = 1.15f;
        public const float MinScale = 0.97f;
        public const float MaxScale = 1.03f;

        /// <summary>Vitesse déterministe dans [0.85, 1.15] pour un seed donné. Pur.</summary>
        public static float ComputeSpeedForSeed(int seed)
        {
            var rng = new System.Random(seed);
            return MinSpeed + (float)rng.NextDouble() * (MaxSpeed - MinSpeed);
        }

        /// <summary>Échelle uniforme déterministe dans [0.97, 1.03]. Pur.</summary>
        public static float ComputeScaleForSeed(int seed)
        {
            var rng = new System.Random(seed ^ 0x3C6EF35F);
            return MinScale + (float)rng.NextDouble() * (MaxScale - MinScale);
        }

        /// <summary>Jitter déterministe dans [0, 1] pour (seed, index). Pur.</summary>
        public static float ComputeJitterForSeed(int seed, int index)
        {
            unchecked
            {
                int h = seed * 374761393 + index * 668265263;
                h = (h ^ (h >> 13)) * 1274126177;
                h ^= h >> 16;
                return (h & 0x7fffffff) / (float)int.MaxValue;
            }
        }

        /// <summary>Candidat de regard (pur, testable).</summary>
        public readonly struct GazeCandidate
        {
            public readonly Vector3 Position;
            public readonly bool IsPlayer;
            public readonly bool Alive;
            public readonly bool Visible;
            public GazeCandidate(Vector3 position, bool isPlayer, bool alive, bool visible)
            {
                Position = position;
                IsPlayer = isPlayer;
                Alive = alive;
                Visible = visible;
            }
        }

        /// <summary>Ennemi (camp opposé) vivant visible le plus proche, sinon repli. Pur.</summary>
        public static Vector3 PickGazePoint(Vector3 selfPos, bool selfIsPlayer,
            IList<GazeCandidate> candidates, Vector3 fallback)
        {
            Vector3 best = fallback;
            float bestDist = float.MaxValue;
            for (int i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                if (c.IsPlayer == selfIsPlayer || !c.Alive || !c.Visible) continue;
                float d = (c.Position - selfPos).sqrMagnitude;
                if (d < bestDist)
                {
                    bestDist = d;
                    best = c.Position;
                }
            }
            return best;
        }
    }
}

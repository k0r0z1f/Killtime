using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Core.Rules;

namespace Killtime.Tactics.Grid
{
    /// <summary>
    /// Paramètres du cône de visée (Livre VI §25.3). Valeurs Codex = hauteurs des
    /// obstacles visuels (muret 0.7, barricade 1.2, mur 2.2) et gabarit humain
    /// (œil 1.5, corps 1.8).
    /// </summary>
    public struct CoverScanParams
    {
        public float HexRadius;
        public float EyeHeight;
        public float BodyHeight;
        public float HalfHeight;
        public float ThreeQuartersHeight;
        public float FullHeight;
        public float FullVisibleFraction;
        public float HalfVisibleFraction;

        public static CoverScanParams CodexDefaults(float hexRadius = 1f)
        {
            return new CoverScanParams
            {
                HexRadius = hexRadius,
                EyeHeight = 1.5f,
                BodyHeight = 1.8f,
                HalfHeight = 0.7f,
                ThreeQuartersHeight = 1.2f,
                FullHeight = 2.2f,
                FullVisibleFraction = 0.9f,
                HalfVisibleFraction = 0.45f
            };
        }
    }

    /// <summary>Un rayon du cône : origine (coin attaquant) ➔ point échantillonné de la cible.</summary>
    public struct CoverRayHit
    {
        public Vector3 Origin;
        public Vector3 Target;
        public bool Blocked;
        public Vector3 HitPoint;
        public bool HasHitPoint;
        public HexCoordinates BlockingCell;
        public bool HasBlockingCell;
    }

    /// <summary>Résultat complet du scan : couvert, fraction visible, meilleur coin, rayons.</summary>
    public struct CoverScanResult
    {
        public CoverType Cover;
        public float VisibleFraction;
        public int TestedRays;
        public int VisibleRays;
        public Vector3 BestOrigin;
        public bool HasBestOrigin;
        public List<CoverRayHit> Rays;
    }

    /// <summary>
    /// Lecture rigoureuse du couvert & de la visibilité (Livre VI §25.3) :
    /// - Le personnage occupe toute sa case : l'attaquant tire depuis le COIN de sa
    ///   case le plus avantageux (celui qui laisse le moins de couvert à la cible).
    /// - Depuis ce coin (hauteur des yeux), un CÔNE de rayons balaie la HAUTEUR de
    ///   la cible (28 échantillons : 7 positions × 4 hauteurs sur 1.8 m).
    /// - Fraction visible f : f ≥ 90% ➔ à découvert (0) ; 45% ≤ f &lt; 90% ➔ moitié
    ///   visible (-1) ; 0% &lt; f &lt; 45% ➔ 3/4 couvert (-2) ; f = 0% ➔ non visible,
    ///   attaque directe impossible.
    /// Chaque rayon est testé en 3D exacte (clip de Cyrus-Beck contre les prismes
    /// hexagonaux des obstacles + relief du terrain) : murets, barricades, murs ET
    /// dénivelés bloquent émergemment, sans table de cas particuliers.
    /// Pur C# système (aucun Physics.Raycast) : déterministe et testable.
    /// </summary>
    public static class CoverSystem
    {
        // 7 positions d'échantillonnage du corps (6 coins + centre) × 4 hauteurs.
        private static readonly float[] SampleLevels = { 0.1f, 0.35f, 0.6f, 0.85f };
        private const float EpsilonT = 1e-6f;
        private const float EpsilonY = 1e-4f;

        /// <summary>Coins de l'hexagone, EXACTEMENT comme le maillage visuel (30° + 60°×i).</summary>
        public static Vector2 HexCornerOffset(int index, float radius)
        {
            double angle = (30.0 + 60.0 * (index % 6)) * Math.PI / 180.0;
            return new Vector2((float)(radius * Math.Cos(angle)), (float)(radius * Math.Sin(angle)));
        }

        public static CoverScanParams ResolveParams(float hexRadius = 1f)
        {
            try
            {
                var cfg = CoreRulesConfig.Instance;
                if (cfg != null)
                {
                    return new CoverScanParams
                    {
                        HexRadius = hexRadius,
                        EyeHeight = cfg.CoverEyeHeight,
                        BodyHeight = cfg.CoverBodyHeight,
                        HalfHeight = cfg.CoverHalfHeight,
                        ThreeQuartersHeight = cfg.CoverThreeQuartersHeight,
                        FullHeight = cfg.CoverFullHeight,
                        FullVisibleFraction = cfg.CoverFullVisibleFraction,
                        HalfVisibleFraction = cfg.CoverHalfVisibleFraction
                    };
                }
            }
            catch { /* repli Codex */ }
            return CoverScanParams.CodexDefaults(hexRadius);
        }

        public static float CoverObstacleHeight(CoverType cover, CoverScanParams p)
        {
            return cover switch
            {
                CoverType.Half => p.HalfHeight,
                CoverType.ThreeQuarters => p.ThreeQuartersHeight,
                CoverType.Full => p.FullHeight,
                _ => 0f,
            };
        }

        /// <summary>
        /// Scan complet : teste les 6 coins attaquants, retient le meilleur (fraction
        /// visible max) et retourne ses 28 rayons (pour l'affichage debug).
        /// Géométrie pure : AUCUNE exception au contact ici, la règle « couvert
        /// ignoré au contact » est appliquée par les appelants (calculateur, arène).
        /// </summary>
        public static CoverScanResult ScanCover(
            HexCoordinates from,
            HexCoordinates to,
            Func<HexCoordinates, HexNode> getNode,
            CoverScanParams p)
        {
            var result = new CoverScanResult
            {
                Cover = CoverType.None,
                Rays = new List<CoverRayHit>(28)
            };
            if (getNode == null) return result;

            float attackerBase = CellBaseY(getNode, from);
            float targetBase = CellBaseY(getNode, to);
            Vector2 attackerCenter = CellCenterXZ(getNode, from, p.HexRadius);
            Vector2 targetCenter = CellCenterXZ(getNode, to, p.HexRadius);

            // 28 échantillons de la silhouette (7 positions × 4 hauteurs).
            var samples = new Vector3[28];
            int si = 0;
            for (int pos = 0; pos < 7; pos++)
            {
                Vector2 offset = (pos < 6) ? HexCornerOffset(pos, p.HexRadius) : Vector2.zero;
                for (int lvl = 0; lvl < SampleLevels.Length; lvl++)
                {
                    samples[si++] = new Vector3(
                        targetCenter.x + offset.x,
                        targetBase + p.BodyHeight * SampleLevels[lvl],
                        targetCenter.y + offset.y);
                }
            }

            int bestVisible = -1;
            Vector3 bestOrigin = Vector3.zero;
            List<CoverRayHit> bestRays = null;

            for (int corner = 0; corner < 6; corner++)
            {
                Vector2 offset = HexCornerOffset(corner, p.HexRadius);
                var origin = new Vector3(
                    attackerCenter.x + offset.x,
                    attackerBase + p.EyeHeight,
                    attackerCenter.y + offset.y);

                int visible = 0;
                var rays = new List<CoverRayHit>(samples.Length);
                for (int i = 0; i < samples.Length; i++)
                {
                    bool blocked = RayBlocked(origin, samples[i], getNode, p, out HexCoordinates cell, out Vector3 hit);
                    if (!blocked) visible++;
                    rays.Add(new CoverRayHit
                    {
                        Origin = origin,
                        Target = samples[i],
                        Blocked = blocked,
                        HitPoint = hit,
                        HasHitPoint = blocked,
                        BlockingCell = cell,
                        HasBlockingCell = blocked
                    });
                }

                if (visible > bestVisible)
                {
                    bestVisible = visible;
                    bestOrigin = origin;
                    bestRays = rays;
                    if (visible == samples.Length) break; // imbattable : tout visible.
                }
            }

            result.TestedRays = samples.Length;
            result.VisibleRays = Math.Max(0, bestVisible);
            result.VisibleFraction = result.TestedRays > 0 ? (float)result.VisibleRays / result.TestedRays : 1f;
            result.BestOrigin = bestOrigin;
            result.HasBestOrigin = bestRays != null;
            if (bestRays != null) result.Rays = bestRays;

            float f = result.VisibleFraction;
            result.Cover = f >= p.FullVisibleFraction ? CoverType.None
                : f >= p.HalfVisibleFraction ? CoverType.Half
                : f > 0f ? CoverType.ThreeQuarters
                : CoverType.Full;
            return result;
        }

        public static CoverScanResult ScanCover(HexCoordinates from, HexCoordinates to, TacticalHexGrid grid)
        {
            if (grid == null) return new CoverScanResult { Cover = CoverType.None, Rays = new List<CoverRayHit>() };
            return ScanCover(from, to, c => grid.GetNode(c), ResolveParams(grid.HexRadius));
        }

        /// <summary>Niveau de couvert seul (sans détail des rayons).</summary>
        public static CoverType EvaluateCover(
            HexCoordinates from,
            HexCoordinates to,
            Func<HexCoordinates, HexNode> getNode,
            float hexRadius = 1f)
        {
            if (getNode == null) return CoverType.None;
            return ScanCover(from, to, getNode, ResolveParams(hexRadius)).Cover;
        }

        /// <summary>Variante pratique branchée directement sur la grille.</summary>
        public static CoverType EvaluateCover(HexCoordinates from, HexCoordinates to, TacticalHexGrid grid)
        {
            if (grid == null) return CoverType.None;
            return ScanCover(from, to, grid).Cover;
        }

        /// <summary>Vrai si la cible est attaquable (tout sauf Full / non visible).</summary>
        public static bool CanAttack(CoverType cover) => cover != CoverType.Full;

        /// <summary>
        /// Malus d'attaque correspondant au couvert. Lit les valeurs du Codex via
        /// les paramètres (config en jeu, constantes Codex par défaut) pour rester
        /// utilisable hors Unity / hors CoreRulesConfig.
        /// </summary>
        public static int AttackPenalty(CoverType cover, int halfPenalty = -1, int threeQuartersPenalty = -2)
        {
            return cover switch
            {
                CoverType.Half => halfPenalty,
                CoverType.ThreeQuarters => threeQuartersPenalty,
                _ => 0,
            };
        }

        public static string Label(CoverType cover)
        {
            return cover switch
            {
                CoverType.Half => "À moitié visible (-1)",
                CoverType.ThreeQuarters => "Aux 3/4 couvert (-2)",
                CoverType.Full => "Non visible (attaque impossible)",
                _ => "À découvert (0)",
            };
        }

        /// <summary>Log court pour les journaux de combat.</summary>
        public static string LogFragment(CoverType cover)
        {
            return cover switch
            {
                CoverType.Half => "[Couvert : moitié visible -1]",
                CoverType.ThreeQuarters => "[Couvert : 3/4 couvert -2]",
                CoverType.Full => "[Couvert TOTAL : cible non visible — attaque impossible]",
                _ => "",
            };
        }

        // ------------------------------------------------------------------
        // Géométrie 3D exacte.
        // ------------------------------------------------------------------

        private static float CellBaseY(Func<HexCoordinates, HexNode> getNode, HexCoordinates coords)
        {
            try
            {
                var node = getNode(coords);
                if (node != null) return node.WorldPosition.y;
            }
            catch { /* ignore */ }
            return 0f;
        }

        private static Vector2 CellCenterXZ(Func<HexCoordinates, HexNode> getNode, HexCoordinates coords, float hexRadius)
        {
            try
            {
                var node = getNode(coords);
                if (node != null) return new Vector2(node.WorldPosition.x, node.WorldPosition.z);
            }
            catch { /* ignore */ }
            Vector3 fallback = coords.ToWorldPosition(hexRadius, 0f);
            return new Vector2(fallback.x, fallback.z);
        }

        /// <summary>
        /// Vrai si le segment origine ➔ cible traverse un obstacle ou le relief.
        /// Parcourt les cases coupées par la droite 2D (collecte exhaustive, sans
        /// limite arbitraire) puis teste chaque prisme en 3D exacte.
        /// </summary>
        private static bool RayBlocked(
            Vector3 origin,
            Vector3 target,
            Func<HexCoordinates, HexNode> getNode,
            CoverScanParams p,
            out HexCoordinates blockingCell,
            out Vector3 hitPoint)
        {
            blockingCell = default;
            hitPoint = target;
            // Collecte exhaustive des cases coupées : pas fin (quart de rayon) pour ne
            // rater aucune cellule ; le test prisme exact tranche ensuite (le frôlement
            // de coin reste visible via les epsilons).
            float segLen = Vector2.Distance(
                new Vector2(origin.x, origin.z), new Vector2(target.x, target.z));
            int steps = Mathf.CeilToInt(segLen / Math.Max(0.01f, p.HexRadius * 0.25f)) + 1;

            var visited = new HashSet<HexCoordinates>();
            for (int i = 0; i <= steps; i++)
            {
                float t = steps > 0 ? (float)i / steps : 0f;
                float x = origin.x + (target.x - origin.x) * t;
                float z = origin.z + (target.z - origin.z) * t;
                HexCoordinates stepped = WorldXZToHex(x, z, p.HexRadius);
                if (!visited.Add(stepped)) continue;

                HexNode node = null;
                try { node = getNode(stepped); }
                catch { node = null; }

                float baseY = node != null ? node.WorldPosition.y : 0f;
                Vector2 center = node != null
                    ? new Vector2(node.WorldPosition.x, node.WorldPosition.z)
                    : HexToCenterXZ(stepped, p.HexRadius);
                CoverType cover = node != null ? node.Cover : CoverType.None;

                // 1) Obstacle posé sur la case (muret, barricade, mur).
                if (cover != CoverType.None)
                {
                    float top = baseY + CoverObstacleHeight(cover, p);
                    if (SegmentHitsPrism(origin, target, center, p.HexRadius, baseY, top, out float tHit))
                    {
                        blockingCell = stepped;
                        hitPoint = origin + (target - origin) * tHit;
                        return true;
                    }
                }

                // 2) Relief : le rayon ne passe jamais sous le terrain.
                if (SegmentHitsPrism(origin, target, center, p.HexRadius, baseY - 1000f, baseY, out float tGround))
                {
                    blockingCell = stepped;
                    hitPoint = origin + (target - origin) * tGround;
                    return true;
                }

                // 3) Accessoire 3D (pilier, caisse, mobilier, visuel de couvert) :
                // dimensions réelles, même si la case est marquée None (Livre VI §25.3).
                // Avec bornes mesh : test AABB exact sur la vérité affichée — tout
                // rayon traversant le mesh visible est bloqué, par construction.
                if (PropObstacleRegistry.TryGet(stepped, out PropObstacle prop))
                {
                    if (prop.HasMeshBounds)
                    {
                        if (SegmentHitsBounds(origin, target, prop.MeshBounds, out float tMesh))
                        {
                            blockingCell = stepped;
                            hitPoint = origin + (target - origin) * tMesh;
                            return true;
                        }
                    }
                    else if (SegmentHitsPrism(origin, target, prop.CenterXZ, prop.Radius, prop.MinY, prop.MaxY, out float tProp))
                    {
                        blockingCell = stepped;
                        hitPoint = origin + (target - origin) * tProp;
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Test exact segment ➔ prisme hexagonal vertical (clip de Cyrus-Beck sur les
        /// 6 demi-plans + recouvrement en Y). Un simple frôlement (contact ponctuel)
        /// ne bloque pas : il faut une traversée réelle (epsilons).
        /// </summary>
        private static bool SegmentHitsPrism(
            Vector3 p0, Vector3 p1,
            Vector2 center, float radius,
            float yBase, float yTop,
            out float tEnter)
        {
            tEnter = 0f;
            float apothem = radius * 0.8660254f; // R·cos(30°), cf. maillage visuel.
            float dx = p1.x - p0.x;
            float dz = p1.z - p0.z;
            float wx = p0.x - center.x;
            float wz = p0.y - center.y;

            float t0 = 0f, t1 = 1f;
            for (int k = 0; k < 6; k++)
            {
                double a = (60.0 * k) * Math.PI / 180.0;
                float nx = (float)Math.Cos(a);
                float nz = (float)Math.Sin(a);
                float denom = dx * nx + dz * nz;
                float dist0 = wx * nx + wz * nz;
                if (Math.Abs(denom) < 1e-9f)
                {
                    if (dist0 > apothem) return false;
                    continue;
                }
                float t = (apothem - dist0) / denom;
                if (denom < 0f) t0 = Math.Max(t0, t);
                else t1 = Math.Min(t1, t);
                if (t0 > t1 + EpsilonT) return false;
            }

            if (t1 - t0 < EpsilonT) return false; // frôlement : visible.
            t0 = Math.Max(0f, t0);
            t1 = Math.Min(1f, t1);
            if (t1 <= t0) return false;

            float ya = p0.y + (p1.y - p0.y) * t0;
            float yb = p0.y + (p1.y - p0.y) * t1;
            float lo = Math.Min(ya, yb);
            float hi = Math.Max(ya, yb);
            if (hi < yBase + EpsilonY || lo > yTop - EpsilonY) return false;
            tEnter = t0;
            return true;
        }

        /// <summary>
        /// Test exact segment ➔ boîte englobante alignée (méthode des dalles).
        /// Un simple frôlement (contact ponctuel) ne bloque pas (epsilons).
        /// </summary>
        private static bool SegmentHitsBounds(Vector3 p0, Vector3 p1, Bounds b, out float tEnter)
        {
            tEnter = 0f;
            float t0 = 0f, t1 = 1f;
            if (!Slab(p0.x, p1.x - p0.x, b.min.x, b.max.x, ref t0, ref t1)) return false;
            if (!Slab(p0.y, p1.y - p0.y, b.min.y, b.max.y, ref t0, ref t1)) return false;
            if (!Slab(p0.z, p1.z - p0.z, b.min.z, b.max.z, ref t0, ref t1)) return false;
            if (t1 - t0 < EpsilonT) return false;
            t0 = Math.Max(0f, t0);
            t1 = Math.Min(1f, t1);
            if (t1 <= t0) return false;
            tEnter = t0;
            return true;
        }

        private static bool Slab(float p0, float d, float min, float max, ref float t0, ref float t1)
        {
            if (Math.Abs(d) < 1e-9f)
                return p0 >= min && p0 <= max;
            float ta = (min - p0) / d;
            float tb = (max - p0) / d;
            if (ta > tb) { float tmp = ta; ta = tb; tb = tmp; }
            t0 = Math.Max(t0, ta);
            t1 = Math.Min(t1, tb);
            return t0 <= t1 + EpsilonT;
        }

        /// <summary>Conversion monde (x,z) ➔ case, miroir de TacticalHexGrid.</summary>
        private static HexCoordinates WorldXZToHex(float x, float z, float hexRadius)
        {
            float qFrac = ((float)Math.Sqrt(3) / 3f * x - 1f / 3f * z) / hexRadius;
            float rFrac = (2f / 3f * z) / hexRadius;
            float sFrac = -qFrac - rFrac;

            int q = (int)Math.Round(qFrac);
            int r = (int)Math.Round(rFrac);
            int s = (int)Math.Round(sFrac);

            float qDiff = Math.Abs(q - qFrac);
            float rDiff = Math.Abs(r - rFrac);
            float sDiff = Math.Abs(s - sFrac);

            if (qDiff > rDiff && qDiff > sDiff) q = -r - s;
            else if (rDiff > sDiff) r = -q - s;
            return new HexCoordinates(q, r);
        }

        private static Vector2 HexToCenterXZ(HexCoordinates coords, float hexRadius)
        {
            Vector3 w = coords.ToWorldPosition(hexRadius, 0f);
            return new Vector2(w.x, w.z);
        }
    }
}

using System;
using System.Collections.Generic;

namespace Killtime.Core.Combat
{
    /// <summary>
    /// Duo-Tech — validation géométrique pure (aucune dépendance Unity).
    /// Travaille sur des polylines 2D échantillonnées (plan XZ projeté) et des
    /// positions axiales. Utilisé par le WeaveController (dessin souris) et les tests.
    ///
    /// Règles verrouillées :
    /// - Trait 1 (Mina) tracé au complet, vecteur gardé en entier.
    /// - Onde en loop sur T1 à la vitesse réelle de Mina (prévisualisation).
    /// - Trait 2 (Lucas) tracé après, en anticipation de l'onde.
    /// </summary>
    [Serializable]
    public struct WeaveVec2
    {
        public float X;
        public float Y;

        public WeaveVec2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public static float Distance(WeaveVec2 a, WeaveVec2 b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }
    }

    public enum WeaveSyncGrade
    {
        Miss,    // Aucun croisement T1/T2 : le feu passe à côté.
        Risky,   // T2 colle l'onde (< 0.15s d'avance) : friendly fire sur Mina.
        Perfect, // T2 anticipe l'onde de 0.15s à 0.6s : bonus plein.
        Late     // T2 arrive après l'onde (> 0.6s de retard) : effet divisé.
    }

    public static class DuoTechGeometry
    {
        // Fenêtre de synchro (secondes d'avance de Lucas sur l'onde de Mina).
        public const float PerfectLeadMin = 0.15f;
        public const float PerfectLeadMax = 0.60f;

        /// <summary>
        /// Longueur d'une polyline.
        /// </summary>
        public static float PolylineLength(IList<WeaveVec2> pts)
        {
            if (pts == null || pts.Count < 2) return 0f;
            float len = 0f;
            for (int i = 1; i < pts.Count; i++)
                len += WeaveVec2.Distance(pts[i - 1], pts[i]);
            return len;
        }

        /// <summary>
        /// Écart perpendiculaire max des points ennemis au segment (a -> b).
        /// Sert au test "ennemis alignés" (Sillage Igné).
        /// </summary>
        public static float MaxDeviationFromSegment(WeaveVec2 a, WeaveVec2 b, IList<WeaveVec2> points)
        {
            if (points == null || points.Count == 0) return 0f;
            float abx = b.X - a.X;
            float aby = b.Y - a.Y;
            float abLen = (float)Math.Sqrt(abx * abx + aby * aby);
            if (abLen < 1e-5f)
            {
                float m = 0f;
                for (int i = 0; i < points.Count; i++)
                    m = Math.Max(m, WeaveVec2.Distance(a, points[i]));
                return m;
            }
            float max = 0f;
            for (int i = 0; i < points.Count; i++)
            {
                float cross = Math.Abs(abx * (a.Y - points[i].Y) - aby * (a.X - points[i].X));
                max = Math.Max(max, cross / abLen);
            }
            return max;
        }

        /// <summary>
        /// Test d'alignement : tous les points à moins de `tolerance` du segment.
        /// </summary>
        public static bool AreAligned(WeaveVec2 a, WeaveVec2 b, IList<WeaveVec2> points, float tolerance)
        {
            if (points == null || points.Count < 2) return false;
            return MaxDeviationFromSegment(a, b, points) <= tolerance;
        }

        /// <summary>
        /// Test d'isolement : aucun autre que la cible dans le rayon (cases -> unités).
        /// </summary>
        public static bool IsIsolated(int targetIndex, IList<int> allDistancesToTarget, int radius)
        {
            if (allDistancesToTarget == null) return false;
            for (int i = 0; i < allDistancesToTarget.Count; i++)
            {
                if (i == targetIndex) continue;
                if (allDistancesToTarget[i] <= radius) return false;
            }
            return true;
        }

        /// <summary>
        /// Test de groupe : tous les ennemis dans un cercle de `radius` cases autour du centroïde.
        /// Prend des distances axiales (entiers) au centroïde approché : le max doit être <= radius.
        /// </summary>
        public static bool AreGrouped(IList<int> distancesToCentroid, int radius)
        {
            if (distancesToCentroid == null || distancesToCentroid.Count < 2) return false;
            for (int i = 0; i < distancesToCentroid.Count; i++)
                if (distancesToCentroid[i] > radius) return false;
            return true;
        }

        /// <summary>
        /// Fermeture de boucle : distance(fin, début) / périmètre. Proche de 0 = boucle fermée.
        /// </summary>
        public static float LoopClosure(IList<WeaveVec2> pts)
        {
            if (pts == null || pts.Count < 3) return 1f;
            float perim = PolylineLength(pts);
            if (perim < 1e-5f) return 1f;
            return WeaveVec2.Distance(pts[0], pts[pts.Count - 1]) / perim;
        }

        public static bool IsLoopClosed(IList<WeaveVec2> pts, float maxClosure = 0.15f)
        {
            return LoopClosure(pts) <= maxClosure;
        }

        /// <summary>
        /// Recouvrement : fraction des échantillons de T2 à moins de `tolerance` de T1.
        /// Sert au Sillage (T2 de Lucas doit chevaucher T1 de Mina).
        /// </summary>
        public static float OverlapFraction(IList<WeaveVec2> t1, IList<WeaveVec2> t2, float tolerance)
        {
            if (t1 == null || t2 == null || t1.Count == 0 || t2.Count == 0) return 0f;
            float tol2 = tolerance * tolerance;
            int covered = 0;
            for (int i = 0; i < t2.Count; i++)
            {
                bool near = false;
                for (int j = 0; j < t1.Count; j++)
                {
                    float dx = t2[i].X - t1[j].X;
                    float dy = t2[i].Y - t1[j].Y;
                    if (dx * dx + dy * dy <= tol2) { near = true; break; }
                }
                if (near) covered++;
            }
            return (float)covered / t2.Count;
        }

        /// <summary>
        /// Point sur T1 à la distance curviligne `s` (pour l'onde).
        /// </summary>
        public static WeaveVec2 PointAtArcLength(IList<WeaveVec2> pts, float s)
        {
            if (pts == null || pts.Count == 0) return new WeaveVec2(0f, 0f);
            if (s <= 0f) return pts[0];
            float acc = 0f;
            for (int i = 1; i < pts.Count; i++)
            {
                float seg = WeaveVec2.Distance(pts[i - 1], pts[i]);
                if (acc + seg >= s)
                {
                    float t = seg > 1e-6f ? (s - acc) / seg : 0f;
                    return new WeaveVec2(
                        pts[i - 1].X + (pts[i].X - pts[i - 1].X) * t,
                        pts[i - 1].Y + (pts[i].Y - pts[i - 1].Y) * t);
                }
                acc += seg;
            }
            return pts[pts.Count - 1];
        }

        /// <summary>
        /// Croisement T1/T2 : paire d'échantillons la plus proche + distances curvilignes.
        /// Retourne faux si l'écart min dépasse `maxGap` (pas de croisement).
        /// </summary>
        public static bool TryFindCrossing(
            IList<WeaveVec2> t1, IList<WeaveVec2> t2, float maxGap,
            out float arcT1, out float arcT2)
        {
            arcT1 = 0f;
            arcT2 = 0f;
            if (t1 == null || t2 == null || t1.Count == 0 || t2.Count == 0) return false;

            float best = float.MaxValue;
            float bestA1 = 0f, bestA2 = 0f;
            float acc1 = 0f;
            for (int i = 0; i < t1.Count; i++)
            {
                if (i > 0) acc1 += WeaveVec2.Distance(t1[i - 1], t1[i]);
                float acc2 = 0f;
                for (int j = 0; j < t2.Count; j++)
                {
                    if (j > 0) acc2 += WeaveVec2.Distance(t2[j - 1], t2[j]);
                    float d = WeaveVec2.Distance(t1[i], t2[j]);
                    if (d < best) { best = d; bestA1 = acc1; bestA2 = acc2; }
                }
            }
            if (best > maxGap) return false;
            arcT1 = bestA1;
            arcT2 = bestA2;
            return true;
        }

        /// <summary>
        /// Grade de synchro : lead = tempsOnde(croisement) - tempsLucas(croisement).
        /// lead > 0 : Lucas frappe avant que Mina n'arrive (anticipation voulue).
        /// lead ~ 0 ou négatif : le feu colle Mina (friendly fire).
        /// lead énorme : le feu arrive trop tard (effet divisé).
        /// </summary>
        public static WeaveSyncGrade GradeSync(float arcT1, float arcT2, float minaSpeed, float lucasSpeed)
        {
            if (minaSpeed <= 0f || lucasSpeed <= 0f) return WeaveSyncGrade.Miss;
            float waveTime = arcT1 / minaSpeed;
            float lucasTime = arcT2 / lucasSpeed;
            float lead = waveTime - lucasTime;
            if (lead < PerfectLeadMin) return WeaveSyncGrade.Risky;
            if (lead <= PerfectLeadMax) return WeaveSyncGrade.Perfect;
            return WeaveSyncGrade.Late;
        }
    }
}

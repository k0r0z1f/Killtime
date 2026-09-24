using UnityEngine;

namespace Killtime.Story
{
    /// <summary>
    /// Primitives de rendu IMGUI partagées par le visualiseur (F12) et l'éditeur
    /// de carte monde : fond stylisé d'après "Hybris 2035.png", treillis hexagonal,
    /// nœuds et liaisons. Aucun état : toutes les méthodes sont statiques.
    /// </summary>
    public static class HybrisWorldMapDraw
    {
        private static Texture2D _whiteTex;
        public static Texture2D WhiteTex
        {
            get
            {
                if (_whiteTex == null)
                {
                    _whiteTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                    _whiteTex.SetPixel(0, 0, Color.white);
                    _whiteTex.Apply();
                }
                return _whiteTex;
            }
        }

        public static Vector2 NodeScreenPos(Rect mapRect, HybrisSectorNode node)
        {
            return new Vector2(
                mapRect.x + node.MapPos.x * mapRect.width,
                mapRect.y + node.MapPos.y * mapRect.height);
        }

        public static Vector2 NormToScreen(Rect mapRect, Vector2 norm)
        {
            return new Vector2(
                mapRect.x + norm.x * mapRect.width,
                mapRect.y + norm.y * mapRect.height);
        }

        public static Rect NormRect(Rect mapRect, float x, float y, float w, float h)
        {
            return new Rect(mapRect.x + x * mapRect.width, mapRect.y + y * mapRect.height,
                w * mapRect.width, h * mapRect.height);
        }

        public static void FillRect(Rect r, Color c)
        {
            Color prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, WhiteTex);
            GUI.color = prev;
        }

        public static void DrawLine(Vector2 a, Vector2 b, Color c, float width)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 0.5f) return;
            float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            Color prevColor = GUI.color;
            Matrix4x4 prevMatrix = GUI.matrix;
            GUI.color = c;
            GUIUtility.RotateAroundPivot(angle, a);
            GUI.DrawTexture(new Rect(a.x, a.y - width * 0.5f, len, width), WhiteTex);
            GUI.matrix = prevMatrix;
            GUI.color = prevColor;
        }

        /// <summary>Ellipse pleine par balayage horizontal (rendu organique sans texture).</summary>
        public static void DrawEllipseNorm(Rect mapRect, float cx, float cy, float rx, float ry, Color c)
        {
            float stepN = 0.006f;
            for (float y = cy - ry; y <= cy + ry; y += stepN)
            {
                float t = (y - cy) / Mathf.Max(0.0001f, ry);
                if (Mathf.Abs(t) > 1f) continue;
                float hw = rx * Mathf.Sqrt(1f - t * t);
                Rect r = new Rect(
                    mapRect.x + (cx - hw) * mapRect.width,
                    mapRect.y + y * mapRect.height,
                    2f * hw * mapRect.width,
                    Mathf.Max(1f, stepN * mapRect.height));
                FillRect(r, c);
            }
        }

        /// <summary>Points disposés en nid d'abeille (treillis hexagonal léger).</summary>
        public static void DrawHexDots(Rect mapRect, float step, Color c)
        {
            float h = step * 0.866f;
            int row = 0;
            for (float y = mapRect.y + 4; y < mapRect.yMax - 2; y += h, row++)
            {
                float offset = (row % 2 == 0) ? 0f : step * 0.5f;
                for (float x = mapRect.x + 4 + offset; x < mapRect.xMax - 2; x += step)
                    FillRect(new Rect(x, y, 2f, 2f), c);
            }
        }

        /// <summary>
        /// Fond de carte partagé : océan (Mer des Déportés), continents Ouest/Est,
        /// îles Vagas et Aurora, plage ouest du Continent Est, lacs, treillis et titres.
        /// </summary>
        public static void DrawBackdrop(Rect mapRect)
        {
            // Océan : Mer des Déportés.
            FillRect(mapRect, new Color(0.04f, 0.09f, 0.16f, 1f));

            // Continents (ellipses organiques par balayage).
            Color landWest = new Color(0.16f, 0.30f, 0.17f, 1f);
            Color landEast = new Color(0.18f, 0.30f, 0.16f, 1f);
            DrawEllipseNorm(mapRect, 0.30f, 0.55f, 0.155f, 0.315f, landWest);
            DrawEllipseNorm(mapRect, 0.35f, 0.32f, 0.115f, 0.115f, landWest);
            DrawEllipseNorm(mapRect, 0.75f, 0.60f, 0.105f, 0.255f, landEast);
            DrawEllipseNorm(mapRect, 0.52f, 0.68f, 0.033f, 0.048f, landWest);
            DrawEllipseNorm(mapRect, 0.63f, 0.10f, 0.048f, 0.033f, landWest);
            // Plage ouest du Continent Est (comme la vieille carte).
            DrawEllipseNorm(mapRect, 0.675f, 0.70f, 0.018f, 0.115f, new Color(0.55f, 0.48f, 0.30f, 1f));
            // Lacs (légende A-H de la vieille carte).
            Color lake = new Color(0.15f, 0.55f, 0.65f, 1f);
            DrawEllipseNorm(mapRect, 0.285f, 0.45f, 0.035f, 0.035f, lake);
            DrawEllipseNorm(mapRect, 0.72f, 0.72f, 0.025f, 0.035f, lake);
            DrawEllipseNorm(mapRect, 0.78f, 0.55f, 0.018f, 0.022f, lake);

            // Treillis hexagonal (points) : compromis lisibilité/perf en IMGUI.
            DrawHexDots(mapRect, 30f, new Color(1f, 1f, 1f, 0.10f));

            // Titres de masses.
            GUI.Label(new Rect(mapRect.x + 8, mapRect.y + 4, 200, 18), "<color=#7dd3fc><b>MER DES DÉPORTÉS</b></color>");
            GUI.Label(NormRect(mapRect, 0.20f, 0.88f, 0.22f, 0.05f), "<color=#86efac>Continent Ouest</color>");
            GUI.Label(NormRect(mapRect, 0.68f, 0.82f, 0.22f, 0.05f), "<color=#86efac>Continent Est</color>");
        }
    }
}

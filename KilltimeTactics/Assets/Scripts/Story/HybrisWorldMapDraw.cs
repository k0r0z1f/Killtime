using UnityEngine;

namespace Killtime.Story
{
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

        private static Texture2D _cachedHexGridTex;

        public static Vector2 ScreenToCanvas(Rect mapRect, Vector2 screenPos, Vector2 pan, float zoom)
        {
            Vector2 local = screenPos - mapRect.position;
            return (local - pan) / zoom;
        }

        public static Vector2 CanvasToScreen(Rect mapRect, Vector2 canvasPos, Vector2 pan, float zoom)
        {
            return mapRect.position + pan + canvasPos * zoom;
        }

        public static Vector2 NodeCanvasPos(Rect canvasRect, HybrisSectorNode node)
        {
            return new Vector2(
                canvasRect.x + node.MapPos.x * canvasRect.width,
                canvasRect.y + node.MapPos.y * canvasRect.height);
        }

        public static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float l2 = ab.sqrMagnitude;
            if (l2 < 0.0001f) return Vector2.Distance(p, a);
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2);
            Vector2 proj = a + t * ab;
            return Vector2.Distance(p, proj);
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

        public static void DrawDashedLine(Vector2 a, Vector2 b, Color c, float width, float dashLen = 8f, float gapLen = 5f)
        {
            Vector2 dir = b - a;
            float dist = dir.magnitude;
            if (dist < 1f) return;
            Vector2 unit = dir / dist;
            float covered = 0f;

            while (covered < dist)
            {
                float segment = Mathf.Min(dashLen, dist - covered);
                Vector2 pStart = a + unit * covered;
                Vector2 pEnd = pStart + unit * segment;
                DrawLine(pStart, pEnd, c, width);
                covered += dashLen + gapLen;
            }
        }

        private static void RasterizeLine(Color32[] buffer, int width, int height, int x0, int y0, int x1, int y1, Color32 col)
        {
            int dx = Mathf.Abs(x1 - x0);
            int dy = Mathf.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;

            while (true)
            {
                if (x0 >= 0 && x0 < width && y0 >= 0 && y0 < height)
                {
                    buffer[y0 * width + x0] = col;
                }

                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 > -dy)
                {
                    err -= dy;
                    x0 += sx;
                }
                if (e2 < dx)
                {
                    err += dx;
                    y0 += sy;
                }
            }
        }

        public static Texture2D GetOrCreateHexGridTexture(int texWidth = 1100, int texHeight = 720)
        {
            if (_cachedHexGridTex != null && _cachedHexGridTex.width == texWidth && _cachedHexGridTex.height == texHeight)
            {
                return _cachedHexGridTex;
            }

            if (_cachedHexGridTex != null)
            {
                Object.DestroyImmediate(_cachedHexGridTex);
            }

            _cachedHexGridTex = new Texture2D(texWidth, texHeight, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };

            var buffer = new Color32[texWidth * texHeight];
            Color32 clearCol = new Color32(0, 0, 0, 0);
            for (int i = 0; i < buffer.Length; i++) buffer[i] = clearCol;

            Color32 gridCol = new Color32(255, 255, 255, 32);
            float hexRadius = (texWidth / HybrisWorldMapData.HexLatticeScaleX) * 0.58f;
            int qMin = Mathf.FloorToInt(-0.55f * HybrisWorldMapData.HexLatticeScaleX);
            int qMax = Mathf.CeilToInt(0.55f * HybrisWorldMapData.HexLatticeScaleX);

            for (int q = qMin; q <= qMax; q++)
            {
                int rMin = Mathf.FloorToInt(-0.55f * HybrisWorldMapData.HexLatticeScaleY - q * 0.5f);
                int rMax = Mathf.CeilToInt(0.55f * HybrisWorldMapData.HexLatticeScaleY - q * 0.5f);

                for (int r = rMin; r <= rMax; r++)
                {
                    Vector2 norm = HybrisWorldMapData.AxialToNorm(q, r);
                    float cx = norm.x * texWidth;
                    float cy = (1f - norm.y) * texHeight;

                    Vector2Int[] corners = new Vector2Int[6];
                    for (int i = 0; i < 6; i++)
                    {
                        float angleDeg = 60f * i - 30f;
                        float angleRad = Mathf.Deg2Rad * angleDeg;
                        corners[i] = new Vector2Int(
                            Mathf.RoundToInt(cx + hexRadius * Mathf.Cos(angleRad)),
                            Mathf.RoundToInt(cy + hexRadius * Mathf.Sin(angleRad)));
                    }

                    for (int i = 0; i < 6; i++)
                    {
                        var c1 = corners[i];
                        var c2 = corners[(i + 1) % 6];
                        RasterizeLine(buffer, texWidth, texHeight, c1.x, c1.y, c2.x, c2.y, gridCol);
                    }
                }
            }

            _cachedHexGridTex.SetPixels32(buffer);
            _cachedHexGridTex.Apply(false, false);
            return _cachedHexGridTex;
        }

        public static void DrawAdaptiveScaleRuler(Rect viewRect, Rect canvasRect, float zoom)
        {
            float milesPerNorm = 300f / 0.175f;
            float milesPerPixel = milesPerNorm / Mathf.Max(1f, canvasRect.width);

            float targetMiles = 130f * milesPerPixel;
            float[] stops = { 1000f, 500f, 300f, 200f, 100f, 50f, 40f, 25f, 20f, 10f, 5f, 2f, 1f, 0.5f };
            float chosenMiles = stops[0];
            for (int i = 0; i < stops.Length; i++)
            {
                if (targetMiles <= stops[i]) chosenMiles = stops[i];
            }

            float barWidth = chosenMiles / milesPerPixel;
            if (barWidth < 45f) barWidth = 45f;
            if (barWidth > viewRect.width - 150f) barWidth = viewRect.width - 150f;

            float startX = 20f;
            float startY = viewRect.height - 30f;

            Color rulerCol = new Color(0.2f, 0.85f, 0.4f, 0.95f);
            Rect bgRect = new Rect(startX - 6f, startY - 17f, barWidth + 115f, 25f);
            FillRect(bgRect, new Color(0.04f, 0.07f, 0.11f, 0.88f));
            DrawLine(new Vector2(bgRect.x, bgRect.y), new Vector2(bgRect.xMax, bgRect.y), new Color(0.2f, 0.85f, 0.4f, 0.45f), 1f);

            DrawLine(new Vector2(startX, startY), new Vector2(startX + barWidth, startY), rulerCol, 2f);
            DrawLine(new Vector2(startX, startY - 5f), new Vector2(startX, startY + 5f), rulerCol, 1.5f);
            DrawLine(new Vector2(startX + barWidth * 0.5f, startY - 3f), new Vector2(startX + barWidth * 0.5f, startY + 3f), rulerCol, 1.2f);
            DrawLine(new Vector2(startX + barWidth, startY - 5f), new Vector2(startX + barWidth, startY + 5f), rulerCol, 1.5f);

            GUI.Label(new Rect(startX - 2f, startY - 17f, 25f, 16f), "<size=9><color=#86efac>0</color></size>");
            GUI.Label(new Rect(startX + barWidth - 18f, startY - 17f, 40f, 16f), $"<size=9><color=#86efac>{chosenMiles:0.#}</color></size>");
            GUI.Label(new Rect(startX + barWidth + 8f, startY - 8f, 95f, 18f), $"<size=10><b><color=#86efac>{chosenMiles:0.#} miles</color></b></size>");
        }

        public static void DrawHexGridVectorial(Rect canvasRect, Rect viewRect, float zoom)
        {
            Vector2 normMin = new Vector2(
                Mathf.Clamp01(-canvasRect.x / canvasRect.width),
                Mathf.Clamp01(-canvasRect.y / canvasRect.height));
            Vector2 normMax = new Vector2(
                Mathf.Clamp01((viewRect.width - canvasRect.x) / canvasRect.width),
                Mathf.Clamp01((viewRect.height - canvasRect.y) / canvasRect.height));

            Vector2Int axialMin = HybrisWorldMapData.NormToAxial(normMin);
            Vector2Int axialMax = HybrisWorldMapData.NormToAxial(normMax);

            int qMin = Mathf.Min(axialMin.x, axialMax.x) - 1;
            int qMax = Mathf.Max(axialMin.x, axialMax.x) + 1;
            int rMin = Mathf.Min(axialMin.y, axialMax.y) - 1;
            int rMax = Mathf.Max(axialMin.y, axialMax.y) + 1;

            float hexRadius = (canvasRect.width / HybrisWorldMapData.HexLatticeScaleX) * 0.58f;
            Color gridCol = new Color(0.4f, 0.75f, 1f, Mathf.Clamp(0.12f + (zoom / 45f) * 0.25f, 0.12f, 0.35f));

            for (int q = qMin; q <= qMax; q++)
            {
                for (int r = rMin; r <= rMax; r++)
                {
                    Vector2 norm = HybrisWorldMapData.AxialToNorm(q, r);
                    Vector2 center = new Vector2(canvasRect.x + norm.x * canvasRect.width, canvasRect.y + norm.y * canvasRect.height);

                    if (center.x < -hexRadius * 2f || center.x > viewRect.width + hexRadius * 2f ||
                        center.y < -hexRadius * 2f || center.y > viewRect.height + hexRadius * 2f)
                        continue;

                    Vector2[] corners = new Vector2[6];
                    for (int i = 0; i < 6; i++)
                    {
                        float angleDeg = 60f * i - 30f;
                        float angleRad = Mathf.Deg2Rad * angleDeg;
                        corners[i] = new Vector2(
                            center.x + hexRadius * Mathf.Cos(angleRad),
                            center.y + hexRadius * Mathf.Sin(angleRad));
                    }

                    for (int i = 0; i < 6; i++)
                    {
                        DrawLine(corners[i], corners[(i + 1) % 6], gridCol, 1.2f);
                    }

                    if (zoom >= 8.0f)
                    {
                        GUI.Label(new Rect(center.x - 45f, center.y - 8f, 90f, 16f),
                            $"<size=9><color=#60a5fa77>{q},{r}</color></size>",
                            new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter });
                    }
                }
            }
        }

        public static void DrawBackdrop(Rect canvasRect, Rect viewRect, float zoom, Texture2D parchmentTex = null, float parchmentAlpha = 0.88f, bool drawHexGrid = true)
        {
            FillRect(canvasRect, new Color(0.04f, 0.06f, 0.10f, 1f));

            if (parchmentTex != null)
            {
                Color prev = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(parchmentAlpha));
                GUI.DrawTexture(canvasRect, parchmentTex, ScaleMode.StretchToFill);
                GUI.color = prev;
            }

            Vector2 centerRose = new Vector2(canvasRect.x + 0.5f * canvasRect.width, canvasRect.y + 0.5f * canvasRect.height);
            Color axisColor = new Color(0.40f, 0.65f, 0.95f, 0.45f);

            DrawLine(new Vector2(canvasRect.x, centerRose.y), new Vector2(canvasRect.xMax, centerRose.y), axisColor, 1.5f);
            DrawLine(new Vector2(centerRose.x, canvasRect.y), new Vector2(centerRose.x, canvasRect.yMax), axisColor, 1.5f);

            DrawLine(new Vector2(centerRose.x - 14f, centerRose.y - 14f), new Vector2(centerRose.x + 14f, centerRose.y + 14f), axisColor, 1.2f);
            DrawLine(new Vector2(centerRose.x - 14f, centerRose.y + 14f), new Vector2(centerRose.x + 14f, centerRose.y - 14f), axisColor, 1.2f);

            GUI.Label(new Rect(canvasRect.x + 8f, centerRose.y - 18f, 140f, 20f), "<size=10><color=#93c5fd><b>0 (Équateur)</b></color></size>");
            GUI.Label(new Rect(centerRose.x + 6f, canvasRect.y + 6f, 180f, 20f), "<size=10><color=#93c5fd><b>0 Méridien (Longitude)</b></color></size>");

            if (drawHexGrid)
            {
                if (zoom <= 2.8f)
                {
                    var gridTex = GetOrCreateHexGridTexture(1100, 720);
                    if (gridTex != null) GUI.DrawTexture(canvasRect, gridTex, ScaleMode.StretchToFill);
                }
                else
                {
                    DrawHexGridVectorial(canvasRect, viewRect, zoom);
                }
            }

            DrawAdaptiveScaleRuler(viewRect, canvasRect, zoom);
        }

        public static void DrawBackdrop(Rect canvasRect, Texture2D parchmentTex = null, float parchmentAlpha = 0.88f, bool drawHexGrid = true)
        {
            DrawBackdrop(canvasRect, new Rect(0, 0, canvasRect.width, canvasRect.height), 1.0f, parchmentTex, parchmentAlpha, drawHexGrid);
        }

        public static void DrawTooltip(Rect screenRect, string title, string content)
        {
            Vector2 mp = Event.current.mousePosition;
            float w = 240f;
            float h = 70f;
            Rect r = new Rect(Mathf.Min(screenRect.xMax - w - 6, mp.x + 12), Mathf.Min(screenRect.yMax - h - 6, mp.y + 12), w, h);

            FillRect(r, new Color(0.06f, 0.10f, 0.16f, 0.95f));
            Color prev = GUI.color;
            GUI.color = new Color(0.3f, 0.7f, 1f, 0.85f);
            DrawLine(new Vector2(r.x, r.y), new Vector2(r.xMax, r.y), GUI.color, 2f);
            GUI.color = prev;

            GUILayout.BeginArea(new Rect(r.x + 6, r.y + 4, r.width - 12, r.height - 8));
            GUILayout.Label($"<b>{title}</b>", GUILayout.Height(18));
            GUILayout.Label($"<size=10>{content}</size>", GUILayout.Height(40));
            GUILayout.EndArea();
        }
    }
}
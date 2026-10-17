using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using Killtime.UI;
using Killtime.Story.Data;
using Killtime.Core.Character;
using Killtime.Core.Inventory;
using Killtime.Tactics;
using Killtime.Tactics.Grid;
using Killtime.Tactics.TurnSystem;
using Killtime.Audio;

namespace Killtime.Story
{
    public partial class ScenarioEditorDevWindow : FloatingWindow<ScenarioEditorDevWindow>
    {

        private Vector2 GraphPoint(Vector2 world)
        {
            return _graphPan + world * _zoom;
        }

        private Rect GraphRect(Rect worldRect)
        {
            Vector2 p = GraphPoint(worldRect.position);
            return new Rect(p.x, p.y, worldRect.width * _zoom, worldRect.height * _zoom);
        }

        private bool GraphPrimaryDown(Vector2 mouseWorld, Rect worldRect)
        {
            if (_pickerCapturesMouse) return false;
            Event evt = Event.current;
            return evt.type == EventType.MouseDown && evt.button == 0 && worldRect.Contains(mouseWorld);
        }

        private void GraphLabel(Rect worldRect, string text)
        {
            GUI.Label(GraphRect(worldRect), text);
        }

        private void GraphLabel(Rect worldRect, string text, GUIStyle style)
        {
            GUI.Label(GraphRect(worldRect), text, style);
        }

        private bool GraphButton(Rect worldRect, string text)
        {
            bool pressed = GUI.Button(GraphRect(worldRect), text);
            return pressed && !_pickerCapturesMouse;
        }

        private string GraphTextField(Rect worldRect, string text)
        {
            string result = GUI.TextField(GraphRect(worldRect), text);
            return _pickerCapturesMouse ? text : result;
        }

        private string GraphTextArea(Rect worldRect, string text)
        {
            string result = GUI.TextArea(GraphRect(worldRect), text);
            return _pickerCapturesMouse ? text : result;
        }

        private bool GraphToggle(Rect worldRect, bool value, string text)
        {
            bool result = GUI.Toggle(GraphRect(worldRect), value, text);
            return _pickerCapturesMouse ? value : result;
        }

        private int GraphToolbar(Rect worldRect, int selected, string[] contents)
        {
            int result = GUI.Toolbar(GraphRect(worldRect), selected, contents);
            return _pickerCapturesMouse ? selected : result;
        }

        private void DrawGraphSolidRect(Rect worldRect, Color color)
        {
            DrawSolidRect(GraphRect(worldRect), color);
        }

        private float DrawFloatInput(Rect worldRect, string key, float value, float min, float max)
        {
            if (!_floatInputBuffers.TryGetValue(key, out var state)
                || (Mathf.Abs(state.lastVal - value) > 0.0001f && !state.text.EndsWith(".") && !state.text.EndsWith(",")))
            {
                state = (value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture), value);
                _floatInputBuffers[key] = state;
            }

            string newText = GraphTextField(worldRect, state.text);
            if (newText != state.text)
            {
                float parsedVal = value;
                if (float.TryParse(newText.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsed))
                {
                    parsedVal = Mathf.Clamp(parsed, min, max);
                }
                _floatInputBuffers[key] = (newText, parsedVal);
                return parsedVal;
            }

            return value;
        }

        private static string NormalizeForSearch(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string decomposed = s.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
            var sb = new System.Text.StringBuilder(decomposed.Length);
            for (int i = 0; i < decomposed.Length; i++)
            {
                char ch = decomposed[i];
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark)
                    sb.Append(ch);
            }
            return sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
        }

        private Rect WorldToScreenRect(Rect canvasRect, Rect world)
        {
            Vector2 p = GraphPoint(world.position);
            return new Rect(canvasRect.x + p.x, canvasRect.y + p.y, world.width * _zoom, world.height * _zoom);
        }

        private void ComputeVisibleNodes(Rect canvasRect, out int fully, out int partial, out int total)
        {
            total = _data.Nodes.Count;
            fully = 0;
            partial = 0;
            if (total == 0) return;
            Rect viewWorld = GetVisibleWorldRect(canvasRect);
            for (int i = 0; i < total; i++)
            {
                Rect b = ComputeNodeBoundingBox(_data.Nodes[i]);
                // "Visible" = ENTIÈREMENT dans le champ. Un nœud à cheval sur le bord compte
                // comme partiel (c'est exactement le cas "tronqué" signalé).
                if (viewWorld.Overlaps(b))
                {
                    if (viewWorld.xMin <= b.xMin && viewWorld.yMin <= b.yMin
                        && viewWorld.xMax >= b.xMax && viewWorld.yMax >= b.yMax)
                        fully++;
                    else
                        partial++;
                }
            }
        }

        private Rect GetVisibleWorldRect(Rect canvasRect)
        {
            float zoom = Mathf.Max(0.0001f, _zoom);
            Vector2 wMin = (Vector2.zero - _graphPan) / zoom;
            Vector2 wMax = (new Vector2(canvasRect.width, canvasRect.height) - _graphPan) / zoom;
            return Rect.MinMaxRect(wMin.x, wMin.y, wMax.x, wMax.y);
        }

        private static bool IsWorldRectVisible(Rect content, Rect viewport)
        {
            return viewport.Overlaps(content);
        }

        private void DrawZoomControlsOverlay(Rect canvasRect)
        {
            // HUD ancré en HAUT-GAUCHE du canvas (zone toujours visible) et dessiné en GUI
            // absolu : aucun layout, aucun chevauchement, aucune dépendance au bas de fenêtre.
            const float hudH = 26f;
            float hudW = Mathf.Min(620f, canvasRect.width - 20f);
            if (hudW < 300f || canvasRect.height < 120f) return;
            Rect hud = new Rect(canvasRect.x + 10f, canvasRect.y + 8f, hudW, hudH);
            DrawSolidRect(hud, new Color(0.02f, 0.04f, 0.07f, 0.90f));

            float x = hud.x + 6f;
            float y = hud.y + 3f;
            const float h = 20f;
            Color prevBg = GUI.backgroundColor;

            GUI.Label(new Rect(x, y, 74f, h), $"<b>{Mathf.RoundToInt(_zoom * 100f)}%</b>");
            x += 78f;
            if (GUI.Button(new Rect(x, y, 26f, h), "−")) ZoomAroundCenter(_zoom - 0.15f);
            x += 30f;
            if (GUI.Button(new Rect(x, y, 26f, h), "+")) ZoomAroundCenter(_zoom + 0.15f);
            x += 30f;
            if (GUI.Button(new Rect(x, y, 48f, h), "100%")) ZoomAroundCenter(1.0f);
            x += 52f;
            GUI.backgroundColor = new Color(1.0f, 0.75f, 0.2f);
            if (GUI.Button(new Rect(x, y, 86f, h), "Cadrer Tout")) FocusAllNodes();
            GUI.backgroundColor = prevBg;
            x += 90f;

            // Stepper nœud par nœud : garantit qu'on peut voir TOUS les nœuds un par un
            // à zoom lisible, même quand la scène complète ne tient pas à l'écran.
            if (_data.Nodes.Count > 0)
            {
                _focusedNodeIndex = Mathf.Clamp(_focusedNodeIndex, 0, _data.Nodes.Count - 1);
                if (GUI.Button(new Rect(x, y, 26f, h), "◀")) FocusNodeByIndex(_focusedNodeIndex - 1);
                x += 30f;
                GUI.Label(new Rect(x, y, 52f, h), $"<b>{_focusedNodeIndex + 1}/{_data.Nodes.Count}</b>");
                x += 56f;
                if (GUI.Button(new Rect(x, y, 26f, h), "▶")) FocusNodeByIndex(_focusedNodeIndex + 1);
                x += 30f;
            }

            // Compteur live : preuve à l'écran que tous les nœuds sont joignables.
            // Vert = tous ENTIERS dans le champ. Orange = chevauchants ou hors champ
            // → Cadrer Tout ou ◀ ▶.
            float rem = hud.xMax - 4f - x;
            if (rem > 110f)
            {
                ComputeVisibleNodes(canvasRect, out int full, out int part, out int tot);
                if (full == tot)
                {
                    GUI.Label(new Rect(x, y, rem, h), $"<color=#7CFF9B><b>{full}/{tot} visibles</b></color>");
                }
                else
                {
                    GUI.Label(new Rect(x, y, rem, h), $"<color=#FFC94D><b>{full}/{tot} +{part} bords</b></color>");
                }
            }
        }

        private void DrawGraphGridBackground(Rect rect)
        {
            DrawSolidRect(rect, new Color(0.035f, 0.045f, 0.06f, 1f));
            float baseGrid = _zoom < 0.35f ? 128f : (_zoom < 0.65f ? 64f : 32f);
            float gridSize = baseGrid * _zoom;
            float offX = ((_graphPan.x % gridSize) + gridSize) % gridSize;
            float offY = ((_graphPan.y % gridSize) + gridSize) % gridSize;

            Color gridSub = new Color(1f, 1f, 1f, 0.025f);
            Color gridMain = new Color(0.0f, 0.8f, 1f, 0.06f);

            // Lignes majeures stables : indexées en espace monde, pas en espace écran
            // (l'ancien modulo écran faisait sauter les lignes fortes pendant le pan).
            float worldLeft = (0f - _graphPan.x) / _zoom;
            float worldTop = (0f - _graphPan.y) / _zoom;
            int idx0x = Mathf.FloorToInt(worldLeft / baseGrid);
            int idx0y = Mathf.FloorToInt(worldTop / baseGrid);
            int cols = Mathf.CeilToInt(rect.width / gridSize) + 2;
            int rows = Mathf.CeilToInt(rect.height / gridSize) + 2;
            for (int i = 0; i <= cols; i++)
            {
                float x = rect.x + offX + (i - 1) * gridSize;
                if (x < rect.x || x > rect.xMax) continue;
                bool isMajor = ((idx0x + i) % 4) == 0;
                DrawSolidRect(new Rect(x, rect.y, 1f, rect.height), isMajor ? gridMain : gridSub);
            }
            for (int j = 0; j <= rows; j++)
            {
                float y = rect.y + offY + (j - 1) * gridSize;
                if (y < rect.y || y > rect.yMax) continue;
                bool isMajor = ((idx0y + j) % 4) == 0;
                DrawSolidRect(new Rect(rect.x, y, rect.width, 1f), isMajor ? gridMain : gridSub);
            }
        }

        private static string TrimHover(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\n", " ").Replace("\r", "").Trim();
            return s.Length > max ? s.Substring(0, max) + "…" : s;
        }

        private void DrawBezierWire(Vector2 start, Vector2 end, Color color, float width = 3.5f, bool vertical = false)
        {
            // Wires sont également en espace monde; conversion unique au rendu.
            start = GraphPoint(start);
            end = GraphPoint(end);
            Vector2 startTan;
            Vector2 endTan;
            if (vertical)
            {
                float dy = end.y - start.y;
                float tangentDist = Mathf.Clamp(Mathf.Abs(dy) * 0.5f, 40f, 220f);
                startTan = start + new Vector2(0f, tangentDist);
                endTan = end - new Vector2(0f, -tangentDist);
            }
            else
            {
                float dx = end.x - start.x;
                float tangentDist = Mathf.Clamp(Mathf.Abs(dx) * 0.5f, 40f, 220f);
                startTan = start + new Vector2(tangentDist, 0f);
                endTan = end - new Vector2(tangentDist, 0f);
            }

            float approxLength = Vector2.Distance(start, startTan) + Vector2.Distance(startTan, endTan) + Vector2.Distance(endTan, end);
            float stepDist = _zoom < CardLodThreshold ? 5.5f : 2.0f;
            int minSteps = _zoom < CardLodThreshold ? 8 : 24;
            int maxSteps = _zoom < CardLodThreshold ? 45 : 180;
            int steps = Mathf.Clamp(Mathf.RoundToInt(approxLength / stepDist), minSteps, maxSteps);
            float dotSize = _zoom < CardLodThreshold ? Mathf.Max(2f, width * 0.75f) : width;

            Color prevCol = GUI.color;
            GUI.color = color;

            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float u = 1f - t;
                Vector2 p = (u * u * u) * start + (3f * u * u * t) * startTan + (3f * u * t * t) * endTan + (t * t * t) * end;
                GUI.DrawTexture(new Rect(p.x - dotSize * 0.5f, p.y - dotSize * 0.5f, dotSize, dotSize), PureWhiteTex);
            }

            GUI.color = prevCol;

            Vector2 mid = 0.125f * start + 0.375f * startTan + 0.375f * endTan + 0.125f * end;
            DrawSolidRect(new Rect(mid.x - 4f, mid.y - 4f, 8f, 8f), color);
        }

        private static void DrawSolidRect(Rect r, Color c)
        {
            Color prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, PureWhiteTex);
            GUI.color = prev;
        }
    }
}

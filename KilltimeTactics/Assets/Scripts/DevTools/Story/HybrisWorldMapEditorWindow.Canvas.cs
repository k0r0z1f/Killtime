using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.UI;
using Killtime.Core.World;

namespace Killtime.Story
{
    public partial class HybrisWorldMapEditorWindow : FloatingWindow<HybrisWorldMapEditorWindow>
    {
        public void FitToView(Rect viewRect)
        {
            if (viewRect.width <= 50f || viewRect.height <= 50f) return;
            float scaleX = viewRect.width / CanvasBaseWidth;
            float scaleY = viewRect.height / CanvasBaseHeight;
            _canvasZoom = Mathf.Min(scaleX, scaleY) * 0.98f;
            float fittedW = CanvasBaseWidth * _canvasZoom;
            float fittedH = CanvasBaseHeight * _canvasZoom;
            _canvasPan = new Vector2(
                (viewRect.width - fittedW) * 0.5f,
                (viewRect.height - fittedH) * 0.5f
            );
        }
        private void DrawMapCanvasTab()
        {
            GUILayout.BeginHorizontal();
            int newMode = GUILayout.Toolbar(_mapMode, _mapModes, GUILayout.Width(490));
            if (newMode != _mapMode)
            {
                _mapMode = newMode;
                _linkFrom = null;
                _dragNodeId = null;
                _rulerStart = null;
                _rulerEnd = null;
            }
            _snapToHex = GUILayout.Toggle(_snapToHex, "🧲 Snap", GUILayout.Width(75));
            _showHexGrid = GUILayout.Toggle(_showHexGrid, "⬡ Grille", GUILayout.Width(80));
            _useParchment = GUILayout.Toggle(_useParchment, "🗺️ Fond", GUILayout.Width(75));
            GUILayout.Label("Opacité:", GUILayout.Width(55));
            _parchmentOpacity = GUILayout.HorizontalSlider(_parchmentOpacity, 0f, 1f, GUILayout.Width(70));

            if (GUILayout.Button("🎯 Vue", GUILayout.Width(60)))
            {
                _hasAutoFittedView = false;
            }
            if (GUILayout.Button("4×", GUILayout.Width(40))) ApplyZoomPreset(4f);
            if (GUILayout.Button("15×", GUILayout.Width(44))) ApplyZoomPreset(15f);
            if (GUILayout.Button("43× (40mi)", GUILayout.Width(82))) ApplyZoomPreset(43f);

            float curViewW = _lastCanvasViewRect.width > 50f ? _lastCanvasViewRect.width : CanvasBaseWidth;
            float curVisMiles = (curViewW / Mathf.Max(1f, CanvasBaseWidth * _canvasZoom)) * (300f / 0.175f);
            GUILayout.Label($"Zoom: <b>{_canvasZoom:0.0}×</b> (~{Mathf.RoundToInt(curVisMiles)} mi)");
            GUILayout.EndHorizontal();

            Rect viewRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
                GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

            if (viewRect.width > 50f && viewRect.height > 50f)
            {
                _lastCanvasViewRect = viewRect;
                if (!_hasAutoFittedView)
                {
                    FitToView(viewRect);
                    _hasAutoFittedView = true;
                }

                HandleCanvasInput(viewRect);

                GUI.BeginClip(viewRect);
                Rect canvasRect = new Rect(_canvasPan.x, _canvasPan.y, CanvasBaseWidth * _canvasZoom, CanvasBaseHeight * _canvasZoom);

                Texture2D texToDraw = _useParchment ? _parchmentMapTex : null;
                HybrisWorldMapDraw.DrawBackdrop(canvasRect, viewRect, _canvasZoom, texToDraw, _parchmentOpacity, _showHexGrid);
                DrawLinksOnCanvas(canvasRect);
                DrawRubberBandLink(canvasRect);
                DrawNodesOnCanvas(canvasRect);
                DrawDragHUD(canvasRect);
                DrawRulerOverlay(canvasRect);

                GUI.EndClip();
            }

            DrawSelectedQuickInspector();
        }
        private void DrawRubberBandLink(Rect canvasRect)
        {
            if (string.IsNullOrEmpty(_linkFrom)) return;
            var originNode = FindInDoc(_linkFrom);
            if (originNode == null) return;

            Vector2 originPos = HybrisWorldMapDraw.NodeCanvasPos(canvasRect, originNode);
            Vector2 targetPos = _canvasLocalMouse;

            if (!string.IsNullOrEmpty(_hoverNodeId) && _hoverNodeId != _linkFrom)
            {
                var destNode = FindInDoc(_hoverNodeId);
                if (destNode != null) targetPos = HybrisWorldMapDraw.NodeCanvasPos(canvasRect, destNode);
            }

            HybrisWorldMapDraw.DrawDashedLine(originPos, targetPos, new Color(1f, 0.6f, 0.1f, 0.9f), 2.5f * _canvasZoom, 8f, 4f);
        }
        private void DrawDragHUD(Rect canvasRect)
        {
            if (string.IsNullOrEmpty(_dragNodeId) || !_hasDraggedNode) return;
            var node = FindInDoc(_dragNodeId);
            if (node == null) return;

            Vector2 p = HybrisWorldMapDraw.NodeCanvasPos(canvasRect, node);
            Rect hudRect = new Rect(p.x + 14f, p.y - 28f, 210f, 24f);
            HybrisWorldMapDraw.FillRect(hudRect, new Color(0.05f, 0.08f, 0.12f, 0.92f));
            HybrisWorldMapDraw.DrawLine(new Vector2(hudRect.x, hudRect.yMax), new Vector2(hudRect.xMax, hudRect.yMax), new Color(0.2f, 0.8f, 1f, 0.8f), 1.5f);
            GUI.Label(hudRect, $" <color=#38bdf8><b>{node.Name}</b></color> ➔ <b>(Q:{node.HexQ} R:{node.HexR} S:{node.HexS})</b>");
        }
        private void ApplyZoomPreset(float targetZoom)
        {
            float oldZoom = _canvasZoom;
            _canvasZoom = Mathf.Clamp(targetZoom, 0.30f, 50.0f);
            float viewW = _lastCanvasViewRect.width > 50f ? _lastCanvasViewRect.width : CanvasBaseWidth;
            float viewH = _lastCanvasViewRect.height > 50f ? _lastCanvasViewRect.height : CanvasBaseHeight;
            Vector2 screenCenter = new Vector2(viewW * 0.5f, viewH * 0.5f);
            Vector2 canvasCenter = (screenCenter - _canvasPan) / oldZoom;
            _canvasPan = screenCenter - canvasCenter * _canvasZoom;
        }
        public void ZoomToNode(HybrisSectorNode node, float targetZoom = 43.0f)
        {
            if (node == null) return;
            _canvasZoom = Mathf.Clamp(targetZoom, 0.30f, 50.0f);
            float viewW = _lastCanvasViewRect.width > 50f ? _lastCanvasViewRect.width : CanvasBaseWidth;
            float viewH = _lastCanvasViewRect.height > 50f ? _lastCanvasViewRect.height : CanvasBaseHeight;
            _canvasPan = new Vector2(
                (viewW * 0.5f) - (node.MapPos.x * CanvasBaseWidth * _canvasZoom),
                (viewH * 0.5f) - (node.MapPos.y * CanvasBaseHeight * _canvasZoom));
            SetStatus($"Focus résolution 40 miles sur {node.Name}.", Color.cyan);
        }
        private void DrawRulerOverlay(Rect canvasRect)
        {
            if (_mapMode != 4) return;

            Vector2? start = _rulerStart;
            Vector2? end = _rulerEnd.HasValue ? _rulerEnd : (_rulerStart.HasValue ? _canvasLocalMouse : (Vector2?)null);

            if (start.HasValue && end.HasValue)
            {
                Vector2 a = start.Value;
                Vector2 b = end.Value;
                HybrisWorldMapDraw.DrawLine(a, b, new Color(1f, 0.2f, 0.6f, 0.95f), 2.5f);

                float distNorm = Vector2.Distance(
                    new Vector2((a.x - canvasRect.x) / canvasRect.width, (a.y - canvasRect.y) / canvasRect.height),
                    new Vector2((b.x - canvasRect.x) / canvasRect.width, (b.y - canvasRect.y) / canvasRect.height));

                float miles = distNorm * (300f / 0.175f);
                int daysFoot = Mathf.Max(1, Mathf.RoundToInt(miles / 20f));
                int daysHorse = Mathf.Max(1, Mathf.RoundToInt(miles / 35f));
                Vector2 mid = (a + b) * 0.5f;

                Rect hudR = new Rect(mid.x + 10f, mid.y - 18f, 190f, 36f);
                HybrisWorldMapDraw.FillRect(hudR, new Color(0.05f, 0.08f, 0.12f, 0.92f));
                HybrisWorldMapDraw.DrawLine(new Vector2(hudR.x, hudR.yMax), new Vector2(hudR.xMax, hudR.yMax), new Color(1f, 0.2f, 0.6f, 0.8f), 1.5f);
                GUI.Label(hudR, $" <color=#f43f5e><b>{miles:0.0} miles</b></color>\n <size=9><color=#94a3b8>Marche: ~{daysFoot}j | Cheval: ~{daysHorse}j</color></size>");
            }
        }
        private void FocusOnNode(HybrisSectorNode node)
        {
            if (node == null) return;
            _canvasPan = new Vector2(
                (CanvasBaseWidth * 0.5f) - (node.MapPos.x * CanvasBaseWidth * _canvasZoom),
                (CanvasBaseHeight * 0.5f) - (node.MapPos.y * CanvasBaseHeight * _canvasZoom)
            );
            SetStatus($"Focus sur {node.Name}.", Color.cyan);
        }
        private void DrawLinksOnCanvas(Rect canvasRect)
        {
            for (int i = 0; i < _doc.Links.Count; i++)
            {
                var l = _doc.Links[i];
                if (l == null) continue;
                var a = FindInDoc(l.FromId);
                var b = FindInDoc(l.ToId);
                if (a == null || b == null) continue;

                Vector2 pa = HybrisWorldMapDraw.NodeCanvasPos(canvasRect, a);
                Vector2 pb = HybrisWorldMapDraw.NodeCanvasPos(canvasRect, b);
                bool sel = (i == _selectedLinkIndex);
                bool hover = (i == _hoverLinkIndex && !sel);

                Color c = sel ? new Color(1f, 0.85f, 0.2f, 1f)
                    : hover ? new Color(0.9f, 0.95f, 1f, 0.95f)
                    : l.IsSeaCrossing ? new Color(0.35f, 0.75f, 1f, 0.75f)
                    : new Color(0.6f, 0.7f, 0.75f, 0.65f);

                float width = Mathf.Clamp((sel ? 3.8f : (hover ? 2.8f : 2.0f)) * Mathf.Sqrt(_canvasZoom), 1.5f, 8f);

                if (l.IsSeaCrossing)
                    HybrisWorldMapDraw.DrawDashedLine(pa, pb, c, width, 10f * Mathf.Clamp(_canvasZoom, 0.5f, 3f), 6f * Mathf.Clamp(_canvasZoom, 0.5f, 3f));
                else
                    HybrisWorldMapDraw.DrawLine(pa, pb, c, width);

                if (_canvasZoom >= 3.0f && !string.IsNullOrEmpty(l.Label))
                {
                    Vector2 mid = (pa + pb) * 0.5f;
                    GUI.Label(new Rect(mid.x - 70f, mid.y - 12f, 140f, 24f),
                        $"<size=9><color=#e2e8f0><b>{l.Label}</b> ({l.Miles}mi)</color></size>",
                        new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter });
                }
            }
        }
        private void DrawNodesOnCanvas(Rect canvasRect)
        {
            foreach (var n in _doc.Nodes)
            {
                if (n == null) continue;
                Vector2 p = HybrisWorldMapDraw.NodeCanvasPos(canvasRect, n);
                bool isSel = _selectedNodeId == n.Id;
                bool isFrom = _linkFrom == n.Id;
                bool isHover = _hoverNodeId == n.Id && !isSel && !isFrom;
                bool isDragging = _dragNodeId == n.Id && _hasDraggedNode;

                Color pinCol = isSel ? Color.yellow : (isHover ? Color.cyan : Color.white);
                HybrisWorldMapDraw.DrawLine(new Vector2(p.x - 5f, p.y), new Vector2(p.x + 5f, p.y), pinCol, 1.5f);
                HybrisWorldMapDraw.DrawLine(new Vector2(p.x, p.y - 5f), new Vector2(p.x, p.y + 5f), pinCol, 1.5f);

                Color prevBg = GUI.backgroundColor;
                GUI.backgroundColor = isDragging ? new Color(1f, 0.9f, 0.2f, 0.95f)
                    : isFrom ? new Color(1f, 0.6f, 0.1f)
                    : isSel ? new Color(0.2f, 0.8f, 1f)
                    : isHover ? new Color(0.5f, 0.85f, 1f)
                    : GetBiomeButtonColor(n.Biome);

                string title = (!string.IsNullOrEmpty(n.RequiredFlag) ? "🔒 " : "") + n.Name;
                if (_canvasZoom >= 8.0f)
                {
                    title += $"\n<size=9>({n.HexQ},{n.HexR})</size>";
                }

                Vector2 textSize = GUI.skin.button.CalcSize(new GUIContent(title));
                float bw = Mathf.Max(95f, (textSize.x + 16f) * Mathf.Clamp(0.85f + (_canvasZoom - 1f) * 0.015f, 0.85f, 1.35f));
                float bh = Mathf.Max(22f, (textSize.y + 6f) * Mathf.Clamp(0.85f + (_canvasZoom - 1f) * 0.015f, 0.85f, 1.35f));
                Rect btnRect = new Rect(p.x - bw * 0.5f, p.y - bh * 0.5f, bw, bh);

                if (GUI.Button(btnRect, title))
                {
                    if (_mapMode == 2 && !string.IsNullOrEmpty(_linkFrom) && _linkFrom != n.Id)
                    {
                        RecordUndo("Création de liaison");
                        CreateLink(_linkFrom, n.Id);
                        _linkFrom = null;
                    }
                    else
                    {
                        _selectedNodeId = n.Id;
                        _selectedLinkIndex = -1;
                        SyncIdBuffer();
                    }
                }
                GUI.backgroundColor = prevBg;
            }
        }
        private Color GetBiomeButtonColor(HybrisBiome biome)
        {
            return biome switch
            {
                HybrisBiome.CiteHumaine => new Color(0.35f, 0.55f, 0.75f),
                HybrisBiome.CiteNaine => new Color(0.65f, 0.45f, 0.35f),
                HybrisBiome.CiteElfique => new Color(0.30f, 0.65f, 0.40f),
                HybrisBiome.TerresInterdites => new Color(0.70f, 0.25f, 0.25f),
                HybrisBiome.Sanctuaire => new Color(0.65f, 0.35f, 0.75f),
                HybrisBiome.Port => new Color(0.25f, 0.60f, 0.65f),
                _ => new Color(0.40f, 0.45f, 0.50f)
            };
        }
        private void DrawSelectedQuickInspector()
        {
            GUILayout.BeginHorizontal(GUI.skin.box, GUILayout.Height(34f));
            var sel = FindInDoc(_selectedNodeId);
            if (sel != null)
            {
                GUILayout.Label("Nom :", GUILayout.Width(42));
                string newName = GUILayout.TextField(sel.Name ?? "", GUILayout.Width(170));
                if (newName != sel.Name)
                {
                    RecordUndo("Renommer nom");
                    sel.Name = newName;
                }

                if (GUILayout.Button("✏️ Renommer ID (F2)", GUILayout.Width(130)))
                {
                    OpenRenameModal(sel);
                }

                GUILayout.Label($"Axial: <b>(Q:{sel.HexQ} R:{sel.HexR} S:{sel.HexS})</b> — {HybrisWorldMapData.BiomeLabel(sel.Biome)}", GUILayout.Width(290));
                if (GUILayout.Button("Fiche Complète ➔", GUILayout.Width(130))) _tab = 1;
                GUILayout.FlexibleSpace();
            }
            else if (_selectedLinkIndex >= 0 && _selectedLinkIndex < _doc.Links.Count)
            {
                var l = _doc.Links[_selectedLinkIndex];
                GUILayout.Label($"<b>Liaison : {l.FromId} ↔ {l.ToId}</b> ({l.Miles} mi, {l.Days} j)", GUILayout.Width(400));
                if (GUILayout.Button("Calculer Auto Géométrie", GUILayout.Width(170))) AutoCalculateLinkMetrics(l);
                if (GUILayout.Button("Modifier dans Liaisons ➔", GUILayout.Width(200))) _tab = 2;
                GUILayout.FlexibleSpace();
            }
            else
            {
                GUILayout.Label("<color=grey><i>Sélectionnez un secteur ou une liaison sur la carte pour afficher ses métriques Codex.</i></color>");
                GUILayout.FlexibleSpace();
            }
            GUILayout.EndHorizontal();
        }
    }
}

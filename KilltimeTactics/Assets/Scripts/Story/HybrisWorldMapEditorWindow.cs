using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.UI;
using Killtime.Core.World;

namespace Killtime.Story
{
    public class HybrisWorldMapEditorWindow : FloatingWindow<HybrisWorldMapEditorWindow>
    {
        protected override int WindowId => 993;
        protected override string Title => "Éditeur World Map — Overworld d'Hybris (Causalité Codex)";
        protected override Vector2 MinSize => new Vector2(1040f, 680f);
        protected override Rect DefaultRect => new Rect(
            20f, 35f,
            Mathf.Min(1400f, Mathf.Max(1040f, Screen.width - 30f)),
            Mathf.Min(920f, Mathf.Max(680f, Screen.height - 45f)));

        private readonly string[] _tabs = { "🗺️ Carte & Canevas", "📋 Secteurs", "🔗 Liaisons", "💾 Fichier", "🧪 Test & Validation" };
        private int _tab;

        private HybrisWorldMapSaveData _doc;
        private bool _dirty;
        private string _mapName = "hybris_overworld";
        private string _statusMsg = "";
        private Color _statusColor = Color.grey;

        private readonly List<string> _undoStack = new();
        private readonly List<string> _redoStack = new();

        private Vector2 _canvasPan = Vector2.zero;
        private float _canvasZoom = 1.0f;
        private bool _isPanning = false;
        private Vector2 _panStartMouse;
        private Vector2 _panStartOrigin;
        private const float CanvasBaseWidth = 1100f;
        private const float CanvasBaseHeight = 720f;
        private bool _hasAutoFittedView = false;

        private Texture2D _parchmentMapTex;
        private float _parchmentOpacity = 0.88f;
        private bool _showHexGrid = true;
        private bool _useParchment = true;

        private string _selectedNodeId;
        private int _selectedLinkIndex = -1;
        private string _idEditBuffer = "";
        private string _confirmDelete = null;

        private int _mapMode;
        private readonly string[] _mapModes = { "👆 Sélection", "✋ Déplacer", "🔗 Lier", "➕ Secteur", "📏 Règle Miles" };
        private string _linkFrom;
        private string _dragNodeId;
        private bool _hasDraggedNode;
        private Vector2 _dragStartMousePos;
        private Vector2 _dragNodeStartPosNorm;
        private string _hoverNodeId;
        private int _hoverLinkIndex = -1;
        private Vector2 _canvasLocalMouse;
        private bool _snapToHex = true;
        private Vector2? _rulerStart;
        private Vector2? _rulerEnd;

        private Vector2 _rightClickStartMouse;
        private bool _potentialRightClick;

        private struct ContextMenuItem
        {
            public string Label;
            public Action Action;
            public bool IsSeparator;
            public bool IsDisabled;
        }

        private bool _contextMenuVisible;
        private Vector2 _contextMenuWindowPos;
        private readonly List<ContextMenuItem> _contextMenuItems = new();

        private bool _showRenameModal;
        private string _renameModalTargetId;
        private string _renameModalNameBuffer = "";
        private string _renameModalIdBuffer = "";
        private Rect _lastCanvasViewRect;

        private string _newNodeName = "Nouveau secteur";
        private string _sectorSearch = "";
        private string _linkSearch = "";
        private string _scenarioFilter = "";
        private Vector2 _scrollList;
        private Vector2 _scrollForm;
        private Vector2 _scrollScenarios;

        private string _importBuffer = "";
        private bool _showRawJson;
        private Vector2 _scrollRaw;

        private List<WorldMapIssue> _issues;
        private bool _issuesStale = true;
        private Vector2 _scrollIssues;
        private List<KeyValuePair<string, string>> _scenarioCache;

        protected override void OnOpened()
        {
            if (_doc == null) LoadDocFromActive();
            if (_parchmentMapTex == null)
            {
                _parchmentMapTex = Resources.Load<Texture2D>("WorldMap/Hybris_Parchment_Map") 
                                ?? Resources.Load<Texture2D>("Hybris_Parchment_Map")
                                ?? Resources.Load<Texture2D>("hybris_map");
            }
            RefreshScenarioCache();
            _issuesStale = true;
            _hasAutoFittedView = false;
            ResetView();
        }

        private void ResetView()
        {
            _canvasPan = new Vector2(15f, 10f);
            _canvasZoom = 0.85f;
            _rulerStart = null;
            _rulerEnd = null;
            _isPanning = false;
            _dragNodeId = null;
        }

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

        private void LoadDocFromActive()
        {
            string targetMap = PlayerPrefs.GetString(HybrisWorldMapData.ActiveMapPrefKey, "hybris_overworld");
            if (string.Equals(targetMap, "factory_canon", StringComparison.OrdinalIgnoreCase))
            {
                targetMap = "hybris_overworld";
            }

            if (HybrisWorldMapRepository.TryLoad(targetMap, out var savedOnDisk))
            {
                _doc = savedOnDisk;
                _mapName = string.IsNullOrWhiteSpace(_doc.MapName) ? "hybris_overworld" : _doc.MapName;
            }
            else if (HybrisWorldMapRepository.TryLoad("hybris_overworld", out var defaultDoc))
            {
                _doc = defaultDoc;
                _mapName = string.IsNullOrWhiteSpace(_doc.MapName) ? "hybris_overworld" : _doc.MapName;
            }
            else
            {
                _doc = new HybrisWorldMapSaveData { MapName = "hybris_overworld" };
                foreach (var n in HybrisWorldMapData.ActiveNodes)
                {
                    if (n == null) continue;
                    _doc.Nodes.Add(new HybrisSectorNode(
                        n.Id, n.Name, n.Region, n.Description,
                        n.MapPos.x, n.MapPos.y, n.Biome, n.Danger,
                        n.LinkedScenarioId, n.RequiredFlag, n.GrantsFlagOnVisit, n.StartingUnlocked,
                        n.HexQ, n.HexR));
                }
                foreach (var l in HybrisWorldMapData.ActiveLinks)
                {
                    if (l == null) continue;
                    _doc.Links.Add(new HybrisSectorLink(
                        l.FromId, l.ToId, l.Miles, l.Days, l.Label, l.IsSeaCrossing, l.RequiresFlag));
                }
                _mapName = "hybris_overworld";
                _doc.MapName = "hybris_overworld";
            }

            _dirty = false;
            _undoStack.Clear();
            _redoStack.Clear();
            _selectedNodeId = _doc.Nodes.Count > 0 ? _doc.Nodes[0].Id : null;
            _selectedLinkIndex = -1;
            SyncIdBuffer();
            _issuesStale = true;
        }

        private void RecordUndo(string action = "")
        {
            _undoStack.Add(JsonUtility.ToJson(_doc));
            if (_undoStack.Count > 40) _undoStack.RemoveAt(0);
            _redoStack.Clear();
            _dirty = true;
            _issuesStale = true;
        }

        private void PerformUndo()
        {
            if (_undoStack.Count == 0) return;
            _redoStack.Add(JsonUtility.ToJson(_doc));
            string json = _undoStack[^1];
            _undoStack.RemoveAt(_undoStack.Count - 1);
            _doc = JsonUtility.FromJson<HybrisWorldMapSaveData>(json);
            _selectedLinkIndex = -1;
            SyncIdBuffer();
            _dirty = true;
            _issuesStale = true;
            SetStatus("Annulation effectuée.", Color.yellow);
        }

        private void PerformRedo()
        {
            if (_redoStack.Count == 0) return;
            _undoStack.Add(JsonUtility.ToJson(_doc));
            string json = _redoStack[^1];
            _redoStack.RemoveAt(_redoStack.Count - 1);
            _doc = JsonUtility.FromJson<HybrisWorldMapSaveData>(json);
            _selectedLinkIndex = -1;
            SyncIdBuffer();
            _dirty = true;
            _issuesStale = true;
            SetStatus("Rétablissement effectué.", Color.yellow);
        }

        private void RefreshScenarioCache()
        {
            _scenarioCache = new List<KeyValuePair<string, string>>();
            try
            {
                foreach (var def in ScenarioCatalog.All)
                {
                    if (def == null || string.IsNullOrWhiteSpace(def.Id)) continue;
                    _scenarioCache.Add(new KeyValuePair<string, string>(def.Id, def.Title ?? def.Id));
                }
                _scenarioCache.Sort((a, b) => string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase));
            }
            catch { }
        }

        protected override void DrawContent()
        {
            if (_doc == null) LoadDocFromActive();
            _doc.EnsureDefaults();

            DrawTopHeader();

            int newTab = GUILayout.Toolbar(_tab, _tabs);
            if (newTab != _tab)
            {
                _tab = newTab;
                _confirmDelete = null;
                _isPanning = false;
                _dragNodeId = null;
                if (_tab == 4) { _issuesStale = true; RefreshScenarioCache(); }
            }
            GUILayout.Space(4);

            switch (_tab)
            {
                case 0: DrawMapCanvasTab(); break;
                case 1: DrawSectorsTab(); break;
                case 2: DrawLinksTab(); break;
                case 3: DrawFileTab(); break;
                case 4: DrawTestValidationTab(); break;
            }

            DrawContextMenuOverlay();
            DrawRenameModalOverlay();
        }

        private void DrawTopHeader()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>🗺️ {_mapName}</b> ({_doc.Nodes.Count} secteurs, {_doc.Links.Count} liaisons)", GUILayout.Width(340));
            GUI.enabled = _undoStack.Count > 0;
            if (GUILayout.Button("↶ Undo", GUILayout.Width(65))) PerformUndo();
            GUI.enabled = _redoStack.Count > 0;
            if (GUILayout.Button("↷ Redo", GUILayout.Width(65))) PerformRedo();
            GUI.enabled = true;

            GUILayout.Label(_dirty
                ? "<color=orange>● modifications non sauvées</color>"
                : "<color=green>✓ synchronisé</color>", GUILayout.Width(170));

            if (GUILayout.Button("⚡ Synchroniser Registre Codex", GUILayout.Width(210)))
            {
                InjectCodexRegistry();
            }

            GUILayout.FlexibleSpace();
            if (!string.IsNullOrEmpty(_statusMsg))
            {
                Color prev = GUI.color;
                GUI.color = _statusColor;
                GUILayout.Label(_statusMsg);
                GUI.color = prev;
            }
            GUILayout.EndHorizontal();
        }

        private void SetStatus(string msg, Color c)
        {
            _statusMsg = msg;
            _statusColor = c;
        }

        private void InjectCodexRegistry()
        {
            RecordUndo("Synchroniser Registre Codex");
            int added = 0;
            foreach (var kvp in HybrisWorldMapRegistry.Locations)
            {
                var loc = kvp.Value;
                var existing = FindInDoc(loc.Code);
                if (existing == null)
                {
                    HybrisBiome biome = loc.Category switch
                    {
                        LandmarkCategory.MajorKingdom => HybrisBiome.CiteHumaine,
                        LandmarkCategory.Hydrology => HybrisBiome.Sanctuaire,
                        LandmarkCategory.MilitaryCamp => HybrisBiome.Montagne,
                        LandmarkCategory.TitanLair => HybrisBiome.TerresInterdites,
                        LandmarkCategory.SettlementOrPort => loc.DominantSpecies == "Taurien" ? HybrisBiome.CiteElfique :
                                                             loc.DominantSpecies == "Nain" ? HybrisBiome.CiteNaine : HybrisBiome.Port,
                        _ => HybrisBiome.Foret
                    };

                    int danger = loc.Category == LandmarkCategory.TitanLair ? 5 : (loc.Category == LandmarkCategory.MilitaryCamp ? 3 : 1);
                    var node = new HybrisSectorNode(
                        loc.Code, loc.CanonicalName, "Codex // " + loc.Category, loc.LoreNotes,
                        loc.NormalizedPos.x, loc.NormalizedPos.y, biome, danger,
                        "", "", "visited_" + loc.Code, loc.Code == "kingston" || loc.Category == LandmarkCategory.MajorKingdom,
                        loc.AxialCoords.Q, loc.AxialCoords.R);
                    _doc.Nodes.Add(node);
                    added++;
                }
            }
            _dirty = true;
            _issuesStale = true;
            SetStatus($"Synchronisation Codex : {added} nouveau(x) secteur(s) importé(s).", Color.green);
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

        private void HandleCanvasInput(Rect viewRect)
        {
            if (_showRenameModal) return;

            Event e = Event.current;
            if (e == null) return;

            if (_contextMenuVisible && e.type == EventType.MouseDown) return;

            Vector2 mp = e.mousePosition;
            bool inside = viewRect.Contains(mp);

            Rect canvasRect = new Rect(_canvasPan.x, _canvasPan.y, CanvasBaseWidth * _canvasZoom, CanvasBaseHeight * _canvasZoom);
            Vector2 localMapPos = mp - viewRect.position;
            _canvasLocalMouse = localMapPos;

            if (inside)
            {
                _hoverNodeId = HitNodeCanvas(canvasRect, localMapPos);
                _hoverLinkIndex = HitLinkCanvas(canvasRect, localMapPos);
            }
            else
            {
                _hoverNodeId = null;
                _hoverLinkIndex = -1;
            }

            if (inside && HandleKeyboardShortcuts(e, canvasRect, localMapPos)) return;

            if (e.type == EventType.MouseDown && inside)
            {
                _contextMenuVisible = false;
            }

            if (e.type == EventType.MouseUp || e.rawType == EventType.MouseUp)
            {
                if (e.button == 1 && _potentialRightClick)
                {
                    _potentialRightClick = false;
                    if (Vector2.Distance(mp, _rightClickStartMouse) < 6f)
                    {
                        OpenContextMenuAt(canvasRect, localMapPos, mp);
                        _isPanning = false;
                        e.Use();
                        return;
                    }
                }

                if (_dragNodeId != null)
                {
                    if (_hasDraggedNode)
                    {
                        SetStatus("Position du secteur mise à jour.", Color.green);
                    }
                    _dragNodeId = null;
                    _hasDraggedNode = false;
                    e.Use();
                    return;
                }

                if (_isPanning)
                {
                    _isPanning = false;
                    e.Use();
                    return;
                }
            }

            if (inside && e.type == EventType.ScrollWheel)
            {
                float oldZoom = _canvasZoom;
                float zoomFactor = Mathf.Pow(1.12f, -e.delta.y);
                _canvasZoom = Mathf.Clamp(_canvasZoom * zoomFactor, 0.30f, 50.0f);
                Vector2 mouseCanvasPos = (mp - viewRect.position - _canvasPan) / oldZoom;
                _canvasPan = (mp - viewRect.position) - mouseCanvasPos * _canvasZoom;
                e.Use();
                return;
            }

            if (_isPanning && e.type == EventType.MouseDrag)
            {
                if (Vector2.Distance(mp, _rightClickStartMouse) >= 6f)
                {
                    _potentialRightClick = false;
                }
                _canvasPan = _panStartOrigin + (mp - _panStartMouse);
                e.Use();
                return;
            }

            if (_dragNodeId != null && e.type == EventType.MouseDrag)
            {
                var n = FindInDoc(_dragNodeId);
                if (n != null)
                {
                    if (!_hasDraggedNode && Vector2.Distance(mp, _dragStartMousePos) > 4f)
                    {
                        RecordUndo("Déplacement de secteur");
                        _hasDraggedNode = true;
                    }

                    if (_hasDraggedNode)
                    {
                        float nx = Mathf.Clamp01((localMapPos.x - _canvasPan.x) / canvasRect.width);
                        float ny = Mathf.Clamp01((localMapPos.y - _canvasPan.y) / canvasRect.height);

                        if (_snapToHex)
                        {
                            var axial = HybrisWorldMapData.NormToAxial(new Vector2(nx, ny));
                            n.HexQ = axial.x;
                            n.HexR = axial.y;
                            n.MapPos = HybrisWorldMapData.AxialToNorm(axial.x, axial.y);
                        }
                        else
                        {
                            n.MapPos = new Vector2(nx, ny);
                            var axial = HybrisWorldMapData.NormToAxial(n.MapPos);
                            n.HexQ = axial.x;
                            n.HexR = axial.y;
                        }
                        _dirty = true;
                        _issuesStale = true;
                    }
                }
                e.Use();
                return;
            }

            if (inside)
            {
                if (e.type == EventType.MouseDown && (e.button == 1 || e.button == 2))
                {
                    _isPanning = true;
                    _panStartMouse = mp;
                    _panStartOrigin = _canvasPan;
                    _rightClickStartMouse = mp;
                    _potentialRightClick = (e.button == 1);
                    e.Use();
                    return;
                }

                if (e.type == EventType.MouseDown && e.button == 0)
                {
                    if (e.alt)
                    {
                        _isPanning = true;
                        _panStartMouse = mp;
                        _panStartOrigin = _canvasPan;
                        e.Use();
                        return;
                    }

                    string hitNode = HitNodeCanvas(canvasRect, localMapPos);
                    int hitLink = HitLinkCanvas(canvasRect, localMapPos);

                    if (hitNode != null && e.clickCount >= 2 && _mapMode == 0)
                    {
                        var nTarget = FindInDoc(hitNode);
                        if (nTarget != null)
                        {
                            ZoomToNode(nTarget, 43.0f);
                            e.Use();
                            return;
                        }
                    }

                    if (_mapMode == 3)
                    {
                        if (hitNode == null)
                        {
                            RecordUndo("Création de secteur");
                            float nx = Mathf.Clamp01((localMapPos.x - _canvasPan.x) / canvasRect.width);
                            float ny = Mathf.Clamp01((localMapPos.y - _canvasPan.y) / canvasRect.height);
                            CreateNodeAt(new Vector2(nx, ny));
                            e.Use();
                            return;
                        }
                    }
                    else if (_mapMode == 2)
                    {
                        if (hitNode == null)
                        {
                            _linkFrom = null;
                        }
                        else if (_linkFrom == null)
                        {
                            _linkFrom = hitNode;
                            _selectedNodeId = hitNode;
                            SyncIdBuffer();
                        }
                        else if (_linkFrom == hitNode)
                        {
                            _linkFrom = null;
                        }
                        else
                        {
                            RecordUndo("Création de liaison");
                            CreateLink(_linkFrom, hitNode);
                            _linkFrom = null;
                        }
                        e.Use();
                        return;
                    }
                    else if (_mapMode == 4)
                    {
                        if (!_rulerStart.HasValue) _rulerStart = localMapPos;
                        else if (!_rulerEnd.HasValue) _rulerEnd = localMapPos;
                        else { _rulerStart = localMapPos; _rulerEnd = null; }
                        e.Use();
                        return;
                    }
                    else
                    {
                        if (hitNode != null)
                        {
                            _selectedNodeId = hitNode;
                            _selectedLinkIndex = -1;
                            SyncIdBuffer();

                            _dragNodeId = hitNode;
                            _hasDraggedNode = false;
                            _dragStartMousePos = mp;
                            var nodeObj = FindInDoc(hitNode);
                            if (nodeObj != null) _dragNodeStartPosNorm = nodeObj.MapPos;

                            e.Use();
                            return;
                        }
                        else if (hitLink >= 0)
                        {
                            _selectedLinkIndex = hitLink;
                            _selectedNodeId = null;
                            e.Use();
                            return;
                        }
                        else
                        {
                            _selectedNodeId = null;
                            _selectedLinkIndex = -1;
                            _linkFrom = null;
                            _isPanning = true;
                            _panStartMouse = mp;
                            _panStartOrigin = _canvasPan;
                            e.Use();
                            return;
                        }
                    }
                }

                if (e.type == EventType.MouseDrag)
                {
                    e.Use();
                    return;
                }
            }
        }

        private bool HandleKeyboardShortcuts(Event e, Rect canvasRect, Vector2 localMapPos)
        {
            if (e.type != EventType.KeyDown) return false;

            if ((e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace))
            {
                if (!string.IsNullOrEmpty(_selectedNodeId))
                {
                    RecordUndo("Supprimer secteur");
                    DeleteNode(_selectedNodeId);
                    e.Use();
                    return true;
                }
                if (_selectedLinkIndex >= 0 && _selectedLinkIndex < _doc.Links.Count)
                {
                    RecordUndo("Supprimer liaison");
                    _doc.Links.RemoveAt(_selectedLinkIndex);
                    _selectedLinkIndex = -1;
                    _dirty = true;
                    _issuesStale = true;
                    SetStatus("Liaison supprimée.", Color.yellow);
                    e.Use();
                    return true;
                }
            }

            if (e.keyCode == KeyCode.F2 && !string.IsNullOrEmpty(_selectedNodeId))
            {
                var n = FindInDoc(_selectedNodeId);
                if (n != null)
                {
                    OpenRenameModal(n);
                    e.Use();
                    return true;
                }
            }

            if (e.keyCode == KeyCode.F && !string.IsNullOrEmpty(_selectedNodeId))
            {
                var n = FindInDoc(_selectedNodeId);
                if (n != null)
                {
                    FocusOnNode(n);
                    e.Use();
                    return true;
                }
            }

            if (e.control && e.keyCode == KeyCode.Z)
            {
                if (e.shift) PerformRedo();
                else PerformUndo();
                e.Use();
                return true;
            }

            if (e.control && e.keyCode == KeyCode.Y)
            {
                PerformRedo();
                e.Use();
                return true;
            }

            if (e.keyCode == KeyCode.Escape)
            {
                _linkFrom = null;
                _dragNodeId = null;
                _rulerStart = null;
                _rulerEnd = null;
                _contextMenuVisible = false;
                e.Use();
                return true;
            }

            return false;
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

        private string HitNodeCanvas(Rect canvasRect, Vector2 localPos)
        {
            string best = null;
            float bestD = 26f * _canvasZoom;
            foreach (var n in _doc.Nodes)
            {
                if (n == null) continue;
                Vector2 p = HybrisWorldMapDraw.NodeCanvasPos(canvasRect, n);
                float d = Vector2.Distance(p, localPos);
                if (d < bestD) { bestD = d; best = n.Id; }
            }
            return best;
        }

        private int HitLinkCanvas(Rect canvasRect, Vector2 localPos)
        {
            float bestDist = 14f * _canvasZoom;
            int bestIdx = -1;

            for (int i = 0; i < _doc.Links.Count; i++)
            {
                var l = _doc.Links[i];
                if (l == null) continue;
                var a = FindInDoc(l.FromId);
                var b = FindInDoc(l.ToId);
                if (a == null || b == null) continue;

                Vector2 pa = HybrisWorldMapDraw.NodeCanvasPos(canvasRect, a);
                Vector2 pb = HybrisWorldMapDraw.NodeCanvasPos(canvasRect, b);
                float dist = HybrisWorldMapDraw.DistanceToSegment(localPos, pa, pb);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestIdx = i;
                }
            }
            return bestIdx;
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

        private void DrawSectorsTab()
        {
            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical(GUILayout.Width(330));
            GUILayout.BeginHorizontal();
            GUILayout.Label("Recherche :", GUILayout.Width(80));
            _sectorSearch = GUILayout.TextField(_sectorSearch ?? "");
            GUILayout.EndHorizontal();

            if (GUILayout.Button("➕ Nouveau Secteur", GUILayout.Height(28)))
            {
                RecordUndo("Créer secteur");
                CreateNodeAt(new Vector2(0.5f, 0.5f));
            }

            _scrollList = GUILayout.BeginScrollView(_scrollList, GUILayout.ExpandHeight(true));
            foreach (var n in _doc.Nodes)
            {
                if (n == null) continue;
                if (!string.IsNullOrEmpty(_sectorSearch) &&
                    n.Name.IndexOf(_sectorSearch, StringComparison.OrdinalIgnoreCase) < 0 &&
                    n.Id.IndexOf(_sectorSearch, StringComparison.OrdinalIgnoreCase) < 0) continue;

                bool sel = _selectedNodeId == n.Id;
                GUI.backgroundColor = sel ? new Color(0.2f, 0.75f, 1f) : Color.white;
                if (GUILayout.Button($"{n.Name} ({n.Id})", GUILayout.Height(26)))
                {
                    _selectedNodeId = n.Id;
                    SyncIdBuffer();
                }
                GUI.backgroundColor = Color.white;
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            _scrollForm = GUILayout.BeginScrollView(_scrollForm, GUILayout.ExpandHeight(true));
            var node = FindInDoc(_selectedNodeId);
            if (node != null) DrawNodeForm(node);
            else GUILayout.Label("<color=grey>Sélectionnez un secteur.</color>");
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }

        private void DrawNodeForm(HybrisSectorNode node)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("<b>Identité & Coordonnées Axiales Codex</b>");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Id Unique :", GUILayout.Width(90));
            _idEditBuffer = GUILayout.TextField(_idEditBuffer);
            if (GUILayout.Button("Renommer", GUILayout.Width(90)))
            {
                RecordUndo("Renommer secteur");
                RenameNode(node, _idEditBuffer);
            }
            GUILayout.EndHorizontal();

            LabeledField("Nom affiché :", node.Name, v => { RecordUndo("Nom"); node.Name = v; });
            LabeledField("Région :", node.Region, v => { RecordUndo("Région"); node.Region = v; });
            LabeledField("Description :", node.Description, v => { RecordUndo("Desc"); node.Description = v; });

            GUILayout.BeginHorizontal();
            GUILayout.Label("Coordonnées Axiales :", GUILayout.Width(140));
            GUILayout.Label($"<b>Q = {node.HexQ} | R = {node.HexR} | S = {node.HexS}</b> (Invariance Q+R+S=0)");
            if (GUILayout.Button("Reprojeter vers Hex", GUILayout.Width(160)))
            {
                RecordUndo("Snap axial");
                var axial = HybrisWorldMapData.NormToAxial(node.MapPos);
                node.HexQ = axial.x;
                node.HexR = axial.y;
                node.MapPos = HybrisWorldMapData.AxialToNorm(node.HexQ, node.HexR);
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.Space(4);
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("<b>Biome & Dangerosité</b>");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Biome :", GUILayout.Width(90));
            if (GUILayout.Button("◀", GUILayout.Width(30)))
            {
                RecordUndo("Biome");
                int len = Enum.GetNames(typeof(HybrisBiome)).Length;
                node.Biome = (HybrisBiome)(((int)node.Biome + len - 1) % len);
            }
            GUILayout.Label($"<b>{HybrisWorldMapData.BiomeLabel(node.Biome)}</b>", GUILayout.Width(170));
            if (GUILayout.Button("▶", GUILayout.Width(30)))
            {
                RecordUndo("Biome");
                int len = Enum.GetNames(typeof(HybrisBiome)).Length;
                node.Biome = (HybrisBiome)(((int)node.Biome + 1) % len);
            }
            GUILayout.EndHorizontal();

            int danger = (int)GUILayout.HorizontalSlider(node.Danger, 0, 5);
            if (danger != node.Danger) { RecordUndo("Danger"); node.Danger = danger; }
            GUILayout.Label($"Danger : <b>{HybrisWorldMapData.DangerLabel(node.Danger)}</b>");
            GUILayout.EndVertical();

            GUILayout.Space(4);
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("<b>Liaison Narrative & Déverrouillages</b>");
            DrawScenarioPicker(node);
            LabeledField("Flag requis :", node.RequiredFlag, v => { RecordUndo("Flag requis"); node.RequiredFlag = v.Trim(); });
            LabeledField("Octroie à visite :", node.GrantsFlagOnVisit, v => { RecordUndo("Flag visit"); node.GrantsFlagOnVisit = v.Trim(); });
            bool startUnlocked = GUILayout.Toggle(node.StartingUnlocked, "★ Découvert dès le début de campagne");
            if (startUnlocked != node.StartingUnlocked) { RecordUndo("StartingUnlocked"); node.StartingUnlocked = startUnlocked; }
            GUILayout.EndVertical();

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(1f, 0.35f, 0.35f);
            if (_confirmDelete == "node:" + node.Id)
            {
                if (GUILayout.Button("⚠ Confirmer Suppression Immédiate", GUILayout.Height(30)))
                {
                    RecordUndo("Supprimer secteur");
                    DeleteNode(node.Id);
                    _confirmDelete = null;
                }
            }
            else if (GUILayout.Button("🗑 Supprimer Secteur", GUILayout.Height(30)))
            {
                _confirmDelete = "node:" + node.Id;
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();
        }

        private void DrawLinksTab()
        {
            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical(GUILayout.Width(340));
            GUILayout.BeginHorizontal();
            GUILayout.Label("Filtre :", GUILayout.Width(50));
            _linkSearch = GUILayout.TextField(_linkSearch ?? "");
            GUILayout.EndHorizontal();

            if (GUILayout.Button("➕ Nouvelle Liaison", GUILayout.Height(28)))
            {
                RecordUndo("Créer liaison");
                CreateLinkDefault();
            }

            _scrollList = GUILayout.BeginScrollView(_scrollList, GUILayout.ExpandHeight(true));
            for (int i = 0; i < _doc.Links.Count; i++)
            {
                var l = _doc.Links[i];
                if (l == null) continue;
                if (!string.IsNullOrEmpty(_linkSearch) &&
                    l.FromId.IndexOf(_linkSearch, StringComparison.OrdinalIgnoreCase) < 0 &&
                    l.ToId.IndexOf(_linkSearch, StringComparison.OrdinalIgnoreCase) < 0) continue;

                bool sel = (i == _selectedLinkIndex);
                GUI.backgroundColor = sel ? new Color(1f, 0.85f, 0.2f) : Color.white;
                if (GUILayout.Button($"{l.FromId} ↔ {l.ToId} ({l.Miles} mi)", GUILayout.Height(26)))
                {
                    _selectedLinkIndex = i;
                    _selectedNodeId = null;
                }
                GUI.backgroundColor = Color.white;
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            _scrollForm = GUILayout.BeginScrollView(_scrollForm, GUILayout.ExpandHeight(true));
            if (_selectedLinkIndex >= 0 && _selectedLinkIndex < _doc.Links.Count && _doc.Links[_selectedLinkIndex] != null)
                DrawLinkForm(_doc.Links[_selectedLinkIndex]);
            else
                GUILayout.Label("<color=grey>Sélectionnez une liaison.</color>");
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }

        private void DrawLinkForm(HybrisSectorLink link)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label($"<b>Liaison : {link.FromId} ↔ {link.ToId}</b>");
            if (GUILayout.Button("⚡ Auto-Calculer Miles et Jours selon l'Échelle Manuscrite", GUILayout.Height(30)))
            {
                RecordUndo("Auto calcul link");
                AutoCalculateLinkMetrics(link);
            }

            int miles = (int)GUILayout.HorizontalSlider(link.Miles, 1, 500);
            if (miles != link.Miles) { RecordUndo("Miles"); link.Miles = miles; }
            GUILayout.Label($"Distance : <b>{link.Miles} miles</b>");

            int days = (int)GUILayout.HorizontalSlider(link.Days, 0, 30);
            if (days != link.Days) { RecordUndo("Days"); link.Days = days; }
            GUILayout.Label($"Durée : <b>{link.Days} jour(s)</b>");

            LabeledField("Libellé :", link.Label, v => { RecordUndo("Label"); link.Label = v; });
            bool sea = GUILayout.Toggle(link.IsSeaCrossing, "⛵ Traversée maritime (Mer des Déportés)");
            if (sea != link.IsSeaCrossing) { RecordUndo("SeaCrossing"); link.IsSeaCrossing = sea; }
            LabeledField("Flag requis :", link.RequiresFlag, v => { RecordUndo("RequiresFlag"); link.RequiresFlag = v.Trim(); });

            GUILayout.Space(6);
            GUI.backgroundColor = new Color(1f, 0.3f, 0.3f);
            if (GUILayout.Button("🗑 Supprimer cette Liaison", GUILayout.Height(30)))
            {
                RecordUndo("Supprimer liaison");
                _doc.Links.RemoveAt(_selectedLinkIndex);
                _selectedLinkIndex = -1;
                SetStatus("Liaison supprimée.", Color.yellow);
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndVertical();
        }

        private void AutoCalculateLinkMetrics(HybrisSectorLink link)
        {
            var a = FindInDoc(link.FromId);
            var b = FindInDoc(link.ToId);
            if (a == null || b == null) return;

            float distNorm = Vector2.Distance(a.MapPos, b.MapPos);
            link.Miles = Mathf.Max(5, Mathf.RoundToInt(distNorm * (300f / 0.175f)));
            link.Days = Mathf.Max(1, Mathf.RoundToInt(link.Miles / (link.IsSeaCrossing ? 26f : 20f)));
            SetStatus($"Métriques calculées : {link.Miles} mi, {link.Days} j.", Color.green);
        }

        private void DrawFileTab()
        {
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(360));
            GUILayout.Label("<b>Fichier Local</b>");
            _mapName = GUILayout.TextField(_mapName ?? "");
            if (GUILayout.Button("💾 Sauvegarder dans WorldMaps/", GUILayout.Height(32))) SaveDoc();
            if (GUILayout.Button("🚀 Appliquer au Jeu Actif", GUILayout.Height(34))) ApplyDocToGame();

            GUILayout.Space(10);
            GUILayout.Label("Cartes sauvegardées sur disque :");
            foreach (var name in HybrisWorldMapRepository.ListMapNames())
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(name, GUILayout.ExpandWidth(true));
                if (GUILayout.Button("Charger", GUILayout.Width(70))) LoadNamedMap(name);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();

            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            GUILayout.Label("<b>Import / Export JSON</b>");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("📋 Exporter (copier JSON)", GUILayout.Height(30)))
            {
                _doc.EnsureDefaults();
                GUIUtility.systemCopyBuffer = JsonUtility.ToJson(_doc, true);
                SetStatus("JSON copié dans le presse-papier.", Color.green);
            }
            if (GUILayout.Button("🧹 Vider", GUILayout.Width(80), GUILayout.Height(30)))
                _importBuffer = "";
            GUILayout.EndHorizontal();

            GUILayout.Label("Coller un JSON de carte monde puis Importer :");
            _importBuffer = GUILayout.TextArea(_importBuffer ?? "", GUILayout.Height(90));
            if (GUILayout.Button("📥 Importer le JSON ci-dessus", GUILayout.Height(30)))
                ImportBuffer();

            _showRawJson = GUILayout.Toggle(_showRawJson, "Afficher l'inspecteur JSON brut");
            if (_showRawJson)
            {
                _scrollRaw = GUILayout.BeginScrollView(_scrollRaw, GUILayout.ExpandHeight(true));
                GUILayout.TextArea(JsonUtility.ToJson(_doc, true));
                GUILayout.EndScrollView();
            }
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        private void ImportBuffer()
        {
            if (HybrisWorldMapRepository.TryLoadFromJson(_importBuffer, out var data) && data != null)
            {
                RecordUndo("Importer JSON");
                _doc = data;
                _mapName = data.MapName;
                _selectedLinkIndex = -1;
                _selectedNodeId = _doc.Nodes.Count > 0 ? _doc.Nodes[0].Id : null;
                SyncIdBuffer();
                _issuesStale = true;
                SetStatus($"JSON importé ({data.Nodes.Count} secteurs, {data.Links.Count} liaisons).", Color.green);
            }
            else
            {
                SetStatus("JSON illisible ou structure non conforme.", Color.red);
            }
        }

        private void DrawTestValidationTab()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>Validation d'Intégrité</b>");
            if (GUILayout.Button("↻ Revalider", GUILayout.Width(110))) _issuesStale = true;
            GUILayout.EndHorizontal();

            var issues = GetIssues();
            int errors = HybrisWorldMapValidation.CountErrors(issues);
            GUILayout.Label(errors > 0 ? $"<color=red><b>{errors} erreur(s) critiques</b></color>" : "<color=green><b>✓ Carte 100% conforme</b></color>");

            _scrollIssues = GUILayout.BeginScrollView(_scrollIssues, GUILayout.Height(180));
            foreach (var i in issues)
            {
                string col = i.Severity == WorldMapIssueSeverity.Error ? "red" : "yellow";
                GUILayout.Label($"<color={col}>[{i.Severity}] {i.Code}</color> : {i.Message}");
            }
            GUILayout.EndScrollView();
        }

        private List<WorldMapIssue> GetIssues()
        {
            if (_issues == null || _issuesStale)
            {
                _issues = HybrisWorldMapValidation.Validate(_doc.Nodes, _doc.Links, id => ScenarioCatalog.Find(id) != null);
                _issuesStale = false;
            }
            return _issues;
        }

        private void SaveDoc()
        {
            if (string.IsNullOrWhiteSpace(_mapName)) _mapName = "hybris_overworld";
            _doc.MapName = _mapName;

            string path = HybrisWorldMapRepository.Save(_doc, _mapName);
            if (path != null)
            {
                _dirty = false;
                HybrisWorldMapData.ApplyRuntimeData(_doc.Clone(), persistPref: true);
                ScenarioDirector.EnsureInstance().RefreshWorldDefaults();
                SetStatus($"Sauvegardé et appliqué au jeu actif : {path}", Color.green);
            }
        }

        private void LoadNamedMap(string name)
        {
            if (HybrisWorldMapRepository.TryLoad(name, out var data))
            {
                _doc = data;
                _mapName = data.MapName;
                _dirty = false;
                _selectedNodeId = _doc.Nodes.Count > 0 ? _doc.Nodes[0].Id : null;
                SyncIdBuffer();
                SetStatus($"Carte « {name} » chargée.", Color.green);
            }
        }

        private void ApplyDocToGame()
        {
            var issues = GetIssues();
            if (HybrisWorldMapValidation.CountErrors(issues) > 0)
            {
                SetStatus("Impossible d'appliquer : des erreurs bloquantes subsistent.", Color.red);
                _tab = 4;
                return;
            }

            if (string.IsNullOrWhiteSpace(_mapName)) _mapName = "hybris_overworld";
            _doc.MapName = _mapName;

            HybrisWorldMapRepository.Save(_doc, _mapName);
            HybrisWorldMapData.ApplyRuntimeData(_doc.Clone(), persistPref: true);
            ScenarioDirector.EnsureInstance().RefreshWorldDefaults();
            _dirty = false;
            SetStatus($"Carte « {_mapName} » synchronisée sur disque et appliquée au jeu actif.", Color.green);
        }

        private void CreateNodeAt(Vector2 norm)
        {
            string id = "secteur_" + (_doc.Nodes.Count + 1);
            var axial = HybrisWorldMapData.NormToAxial(norm);
            var node = new HybrisSectorNode(id, _newNodeName, "Hybris", "", norm.x, norm.y, HybrisBiome.Foret, 1, "", "", "visited_" + id, false, axial.x, axial.y);
            _doc.Nodes.Add(node);
            _selectedNodeId = id;
            SyncIdBuffer();
            _dirty = true;
            SetStatus($"Secteur « {id} » créé.", Color.green);
        }

        private void CreateLink(string a, string b)
        {
            if (HasLink(a, b)) return;
            var l = new HybrisSectorLink(a, b, 50, 2, "Route");
            AutoCalculateLinkMetrics(l);
            _doc.Links.Add(l);
            _selectedLinkIndex = _doc.Links.Count - 1;
            _dirty = true;
        }

        private void CreateLinkDefault()
        {
            if (_doc.Nodes.Count < 2) return;
            string a = _selectedNodeId ?? _doc.Nodes[0].Id;
            foreach (var n in _doc.Nodes)
            {
                if (n.Id == a || HasLink(a, n.Id)) continue;
                CreateLink(a, n.Id);
                break;
            }
        }

        private bool HasLink(string a, string b)
        {
            return _doc.Links.Exists(l => l != null && l.Connects(a) && l.Connects(b));
        }

        private void DeleteNode(string id)
        {
            _doc.Nodes.RemoveAll(n => n != null && n.Id == id);
            _doc.Links.RemoveAll(l => l != null && l.Connects(id));
            _selectedNodeId = _doc.Nodes.Count > 0 ? _doc.Nodes[0].Id : null;
            _selectedLinkIndex = -1;
            SyncIdBuffer();
            _dirty = true;
        }

        private bool RenameNode(HybrisSectorNode node, string newId)
        {
            newId = (newId ?? "").Trim();
            if (string.IsNullOrEmpty(newId))
            {
                SetStatus("Erreur : l'identifiant unique ne peut pas être vide.", Color.red);
                return false;
            }
            if (!HybrisWorldMapValidation.IsValidId(newId))
            {
                SetStatus($"Erreur : « {newId} » est invalide (lettres, chiffres, '_' et '-' uniquement).", Color.red);
                return false;
            }
            if (FindInDoc(newId) != null && !string.Equals(newId, node.Id, StringComparison.OrdinalIgnoreCase))
            {
                SetStatus($"Erreur : un secteur avec l'ID « {newId} » existe déjà.", Color.red);
                return false;
            }

            string oldId = node.Id;
            node.Id = newId;

            if (node.GrantsFlagOnVisit == "visited_" + oldId)
            {
                node.GrantsFlagOnVisit = "visited_" + newId;
            }

            foreach (var l in _doc.Links)
            {
                if (l == null) continue;
                if (string.Equals(l.FromId, oldId, StringComparison.OrdinalIgnoreCase)) l.FromId = newId;
                if (string.Equals(l.ToId, oldId, StringComparison.OrdinalIgnoreCase)) l.ToId = newId;
            }

            if (_linkFrom == oldId) _linkFrom = newId;
            _selectedNodeId = newId;
            SyncIdBuffer();
            _dirty = true;
            _issuesStale = true;
            SetStatus($"Secteur renommé : « {newId} ».", Color.green);
            return true;
        }

        private void OpenRenameModal(HybrisSectorNode node)
        {
            if (node == null) return;
            _renameModalTargetId = node.Id;
            _renameModalNameBuffer = node.Name ?? "";
            _renameModalIdBuffer = node.Id ?? "";
            _showRenameModal = true;
        }

        private void DrawRenameModalOverlay()
        {
            if (!_showRenameModal) return;

            var node = FindInDoc(_renameModalTargetId);
            if (node == null)
            {
                _showRenameModal = false;
                return;
            }

            Event e = Event.current;

            if (e != null && e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                {
                    ConfirmRenameModal(node);
                    e.Use();
                    return;
                }
                if (e.keyCode == KeyCode.Escape)
                {
                    _showRenameModal = false;
                    e.Use();
                    return;
                }
            }

            float modalW = 420f;
            float modalH = 175f;
            float cx = _lastCanvasViewRect.width > 100f
                ? _lastCanvasViewRect.x + (_lastCanvasViewRect.width - modalW) * 0.5f
                : (Screen.width - modalW) * 0.5f;
            float cy = _lastCanvasViewRect.height > 100f
                ? _lastCanvasViewRect.y + (_lastCanvasViewRect.height - modalH) * 0.45f
                : (Screen.height - modalH) * 0.45f;
            Rect modalRect = new Rect(cx, cy, modalW, modalH);

            HybrisWorldMapDraw.FillRect(new Rect(0, 0, 5000f, 5000f), new Color(0f, 0f, 0f, 0.6f));
            HybrisWorldMapDraw.FillRect(modalRect, new Color(0.08f, 0.11f, 0.16f, 0.98f));
            HybrisWorldMapDraw.DrawLine(new Vector2(modalRect.x, modalRect.y), new Vector2(modalRect.xMax, modalRect.y), new Color(0.3f, 0.7f, 1f, 0.9f), 2f);
            HybrisWorldMapDraw.DrawLine(new Vector2(modalRect.x, modalRect.yMax), new Vector2(modalRect.xMax, modalRect.yMax), new Color(0.2f, 0.4f, 0.6f, 0.6f), 1f);
            HybrisWorldMapDraw.DrawLine(new Vector2(modalRect.x, modalRect.y), new Vector2(modalRect.x, modalRect.yMax), new Color(0.2f, 0.4f, 0.6f, 0.6f), 1f);
            HybrisWorldMapDraw.DrawLine(new Vector2(modalRect.xMax, modalRect.y), new Vector2(modalRect.xMax, modalRect.yMax), new Color(0.2f, 0.4f, 0.6f, 0.6f), 1f);

            GUILayout.BeginArea(new Rect(modalRect.x + 16f, modalRect.y + 12f, modalRect.width - 32f, modalRect.height - 24f));
            GUILayout.Label($"<b><size=13>Renommer le Secteur : {node.Name}</size></b>");
            GUILayout.Space(6);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Nom Affiché :", GUILayout.Width(110));
            _renameModalNameBuffer = GUILayout.TextField(_renameModalNameBuffer ?? "");
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("ID Unique :", GUILayout.Width(110));
            _renameModalIdBuffer = GUILayout.TextField(_renameModalIdBuffer ?? "");
            GUILayout.EndHorizontal();

            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.2f, 0.85f, 0.4f);
            if (GUILayout.Button("Valider (Entrée)", GUILayout.Height(28)))
            {
                ConfirmRenameModal(node);
            }

            GUI.backgroundColor = new Color(0.7f, 0.25f, 0.25f);
            if (GUILayout.Button("Annuler (Échap)", GUILayout.Height(28)))
            {
                _showRenameModal = false;
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void ConfirmRenameModal(HybrisSectorNode node)
        {
            RecordUndo("Renommage secteur");
            string newName = (_renameModalNameBuffer ?? "").Trim();
            string targetId = (_renameModalIdBuffer ?? "").Trim();

            node.Name = string.IsNullOrEmpty(newName) ? node.Name : newName;

            bool renameOk = true;
            if (!string.IsNullOrEmpty(targetId) && targetId != node.Id)
            {
                renameOk = RenameNode(node, targetId);
            }

            if (renameOk)
            {
                _dirty = true;
                _issuesStale = true;
                SetStatus($"Secteur mis à jour : « {node.Name} » ({node.Id}).", Color.green);
                _showRenameModal = false;
            }
        }

        private HybrisSectorNode FindInDoc(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || _doc == null) return null;
            return _doc.Nodes.Find(n => n != null && string.Equals(n.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        private void SyncIdBuffer()
        {
            _idEditBuffer = FindInDoc(_selectedNodeId)?.Id ?? "";
        }

        private void LabeledField(string label, string val, Action<string> onSet)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(110));
            string n = GUILayout.TextField(val ?? "");
            if (n != (val ?? "")) onSet(n);
            GUILayout.EndHorizontal();
        }

        private void DrawScenarioPicker(HybrisSectorNode node)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Scène Liée :", GUILayout.Width(110));
            string nv = GUILayout.TextField(node.LinkedScenarioId ?? "");
            if (nv != (node.LinkedScenarioId ?? "")) { RecordUndo("Scène liée"); node.LinkedScenarioId = nv.Trim(); }
            if (GUILayout.Button("✖", GUILayout.Width(28)))
            {
                RecordUndo("Vider scène liée");
                node.LinkedScenarioId = "";
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Catalogue :", GUILayout.Width(110));
            _scenarioFilter = GUILayout.TextField(_scenarioFilter ?? "", GUILayout.Width(150));
            if (GUILayout.Button("↻", GUILayout.Width(28))) RefreshScenarioCache();
            GUILayout.EndHorizontal();

            _scrollScenarios = GUILayout.BeginScrollView(_scrollScenarios, GUILayout.Height(86));
            if (_scenarioCache != null)
            {
                string f = (_scenarioFilter ?? "").Trim();
                int shown = 0;
                foreach (var kv in _scenarioCache)
                {
                    if (!string.IsNullOrEmpty(f)
                        && kv.Key.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0
                        && kv.Value.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (++shown > 60) { GUILayout.Label("<color=grey>… affinez le filtre …</color>"); break; }
                    bool isLinked = string.Equals(node.LinkedScenarioId, kv.Key, StringComparison.OrdinalIgnoreCase);
                    GUI.backgroundColor = isLinked ? new Color(0.3f, 0.65f, 0.4f) : Color.white;
                    if (GUILayout.Button($"{kv.Key} — {kv.Value}", GUILayout.Height(22)))
                    {
                        RecordUndo("Choisir scène catalogue");
                        node.LinkedScenarioId = kv.Key;
                    }
                    GUI.backgroundColor = Color.white;
                }
                if (shown == 0) GUILayout.Label("<color=grey>Aucune scène (filtre ou catalogue vide).</color>");
            }
            GUILayout.EndScrollView();
        }

        private void OpenContextMenuAt(Rect canvasRect, Vector2 localMapPos, Vector2 screenMousePos)
        {
            _contextMenuItems.Clear();
            _contextMenuWindowPos = screenMousePos;

            string hitNode = HitNodeCanvas(canvasRect, localMapPos);
            int hitLink = HitLinkCanvas(canvasRect, localMapPos);

            if (hitNode != null)
            {
                BuildContextMenuForNode(hitNode);
            }
            else if (hitLink >= 0)
            {
                BuildContextMenuForLink(hitLink);
            }
            else
            {
                BuildContextMenuForCanvas(canvasRect, localMapPos);
            }

            if (_contextMenuItems.Count > 0)
            {
                _contextMenuVisible = true;
            }
        }

        private void BuildContextMenuForNode(string nodeId)
        {
            var node = FindInDoc(nodeId);
            if (node == null) return;

            _selectedNodeId = nodeId;
            _selectedLinkIndex = -1;
            SyncIdBuffer();

            _contextMenuItems.Add(new ContextMenuItem { Label = $"<b>Secteur : {node.Name}</b>", IsDisabled = true });
            _contextMenuItems.Add(new ContextMenuItem { IsSeparator = true });

            _contextMenuItems.Add(new ContextMenuItem
            {
                Label = "🎯 Centrer la vue",
                Action = () => FocusOnNode(node)
            });

            _contextMenuItems.Add(new ContextMenuItem
            {
                Label = "✏️ Renommer le secteur (F2)",
                Action = () => OpenRenameModal(node)
            });

            _contextMenuItems.Add(new ContextMenuItem
            {
                Label = "🔗 Lier vers un autre secteur",
                Action = () =>
                {
                    _mapMode = 2;
                    _linkFrom = nodeId;
                    SetStatus($"Mode liaison activé depuis {node.Name}. Cliquez sur la cible.", Color.yellow);
                }
            });

            _contextMenuItems.Add(new ContextMenuItem
            {
                Label = "📋 Dupliquer le secteur",
                Action = () => DuplicateNode(node)
            });

            _contextMenuItems.Add(new ContextMenuItem
            {
                Label = "🧲 Reprojeter vers le centre Hex",
                Action = () =>
                {
                    RecordUndo("Snap axial");
                    var axial = HybrisWorldMapData.NormToAxial(node.MapPos);
                    node.HexQ = axial.x;
                    node.HexR = axial.y;
                    node.MapPos = HybrisWorldMapData.AxialToNorm(node.HexQ, node.HexR);
                    _dirty = true;
                    _issuesStale = true;
                }
            });

            _contextMenuItems.Add(new ContextMenuItem { IsSeparator = true });
            _contextMenuItems.Add(new ContextMenuItem
            {
                Label = "🗑 Supprimer le secteur",
                Action = () =>
                {
                    RecordUndo("Supprimer secteur");
                    DeleteNode(node.Id);
                }
            });
        }

        private void BuildContextMenuForLink(int linkIdx)
        {
            if (linkIdx < 0 || linkIdx >= _doc.Links.Count) return;
            var l = _doc.Links[linkIdx];
            if (l == null) return;

            _selectedLinkIndex = linkIdx;
            _selectedNodeId = null;

            _contextMenuItems.Add(new ContextMenuItem { Label = $"<b>Liaison : {l.FromId} ↔ {l.ToId}</b>", IsDisabled = true });
            _contextMenuItems.Add(new ContextMenuItem { IsSeparator = true });

            _contextMenuItems.Add(new ContextMenuItem
            {
                Label = "⚡ Recalculer Miles & Jours (Canon)",
                Action = () =>
                {
                    RecordUndo("Auto calcul liaison");
                    AutoCalculateLinkMetrics(l);
                    _dirty = true;
                }
            });

            _contextMenuItems.Add(new ContextMenuItem
            {
                Label = l.IsSeaCrossing ? "🚶 Convertir en Route Terrestre" : "⛵ Convertir en Traversée Maritime",
                Action = () =>
                {
                    RecordUndo("Type liaison");
                    l.IsSeaCrossing = !l.IsSeaCrossing;
                    AutoCalculateLinkMetrics(l);
                    _dirty = true;
                }
            });

            _contextMenuItems.Add(new ContextMenuItem { IsSeparator = true });
            _contextMenuItems.Add(new ContextMenuItem
            {
                Label = "🗑 Supprimer cette liaison",
                Action = () =>
                {
                    RecordUndo("Supprimer liaison");
                    _doc.Links.RemoveAt(linkIdx);
                    _selectedLinkIndex = -1;
                    _dirty = true;
                    _issuesStale = true;
                    SetStatus("Liaison supprimée.", Color.yellow);
                }
            });
        }

        private void BuildContextMenuForCanvas(Rect canvasRect, Vector2 localMapPos)
        {
            float nx = Mathf.Clamp01((localMapPos.x - _canvasPan.x) / canvasRect.width);
            float ny = Mathf.Clamp01((localMapPos.y - _canvasPan.y) / canvasRect.height);
            var axial = HybrisWorldMapData.NormToAxial(new Vector2(nx, ny));

            _contextMenuItems.Add(new ContextMenuItem { Label = $"<b>Position Axiale : (Q:{axial.x}, R:{axial.y}, S:{-axial.x - axial.y})</b>", IsDisabled = true });
            _contextMenuItems.Add(new ContextMenuItem { IsSeparator = true });

            _contextMenuItems.Add(new ContextMenuItem
            {
                Label = "➕ Créer un secteur ici",
                Action = () =>
                {
                    RecordUndo("Créer secteur");
                    CreateNodeAt(new Vector2(nx, ny));
                }
            });

            _contextMenuItems.Add(new ContextMenuItem
            {
                Label = "🎯 Recentrer / Ajuster vue",
                Action = () => _hasAutoFittedView = false
            });

            _contextMenuItems.Add(new ContextMenuItem
            {
                Label = _snapToHex ? "🔓 Désactiver Magnétisme Hex" : "🧲 Activer Magnétisme Hex",
                Action = () => _snapToHex = !_snapToHex
            });

            _contextMenuItems.Add(new ContextMenuItem
            {
                Label = _showHexGrid ? "Masquer Grille Hex" : "Afficher Grille Hex",
                Action = () => _showHexGrid = !_showHexGrid
            });
        }

        private void DuplicateNode(HybrisSectorNode source)
        {
            if (source == null) return;
            RecordUndo("Dupliquer secteur");
            string newId = source.Id + "_copie";
            int counter = 1;
            while (FindInDoc(newId) != null)
            {
                newId = $"{source.Id}_copie{++counter}";
            }

            Vector2 offsetPos = source.MapPos + new Vector2(0.02f, 0.02f);
            var axial = HybrisWorldMapData.NormToAxial(offsetPos);
            var clone = new HybrisSectorNode(
                newId, source.Name + " (Copie)", source.Region, source.Description,
                offsetPos.x, offsetPos.y, source.Biome, source.Danger,
                source.LinkedScenarioId, source.RequiredFlag, source.GrantsFlagOnVisit,
                source.StartingUnlocked, axial.x, axial.y);

            _doc.Nodes.Add(clone);
            _selectedNodeId = newId;
            SyncIdBuffer();
            _dirty = true;
            _issuesStale = true;
            SetStatus($"Secteur « {newId} » créé par duplication.", Color.green);
        }

        private void DrawContextMenuOverlay()
        {
            if (!_contextMenuVisible || _contextMenuItems.Count == 0) return;

            Event e = Event.current;
            float itemH = 22f;
            float sepH = 6f;
            float totalH = 10f;

            for (int i = 0; i < _contextMenuItems.Count; i++)
            {
                totalH += _contextMenuItems[i].IsSeparator ? sepH : itemH;
            }

            float menuW = 230f;
            Rect menuRect = new Rect(
                Mathf.Min(_contextMenuWindowPos.x, Screen.width - menuW - 10f),
                Mathf.Min(_contextMenuWindowPos.y, Screen.height - totalH - 10f),
                menuW,
                totalH
            );

            if (e != null && e.type == EventType.MouseDown && !menuRect.Contains(e.mousePosition))
            {
                _contextMenuVisible = false;
                e.Use();
                return;
            }

            HybrisWorldMapDraw.FillRect(menuRect, new Color(0.08f, 0.11f, 0.16f, 0.98f));
            HybrisWorldMapDraw.DrawLine(new Vector2(menuRect.x, menuRect.y), new Vector2(menuRect.xMax, menuRect.y), new Color(0.3f, 0.7f, 1f, 0.9f), 2f);
            HybrisWorldMapDraw.DrawLine(new Vector2(menuRect.x, menuRect.yMax), new Vector2(menuRect.xMax, menuRect.yMax), new Color(0.2f, 0.4f, 0.6f, 0.6f), 1f);
            HybrisWorldMapDraw.DrawLine(new Vector2(menuRect.x, menuRect.y), new Vector2(menuRect.x, menuRect.yMax), new Color(0.2f, 0.4f, 0.6f, 0.6f), 1f);
            HybrisWorldMapDraw.DrawLine(new Vector2(menuRect.xMax, menuRect.y), new Vector2(menuRect.xMax, menuRect.yMax), new Color(0.2f, 0.4f, 0.6f, 0.6f), 1f);

            float curY = menuRect.y + 5f;
            for (int i = 0; i < _contextMenuItems.Count; i++)
            {
                var item = _contextMenuItems[i];
                if (item.IsSeparator)
                {
                    HybrisWorldMapDraw.DrawLine(
                        new Vector2(menuRect.x + 8f, curY + sepH * 0.5f),
                        new Vector2(menuRect.xMax - 8f, curY + sepH * 0.5f),
                        new Color(1f, 1f, 1f, 0.18f),
                        1f);
                    curY += sepH;
                }
                else
                {
                    Rect r = new Rect(menuRect.x + 4f, curY, menuRect.width - 8f, itemH);
                    if (item.IsDisabled)
                    {
                        GUI.Label(r, item.Label);
                    }
                    else
                    {
                        bool hover = r.Contains(e.mousePosition);
                        if (hover) HybrisWorldMapDraw.FillRect(r, new Color(0.2f, 0.5f, 0.8f, 0.45f));

                        if (GUI.Button(r, item.Label, GUI.skin.label))
                        {
                            _contextMenuVisible = false;
                            item.Action?.Invoke();
                            if (e != null) e.Use();
                            return;
                        }
                    }
                    curY += itemH;
                }
            }
        }
    }
}
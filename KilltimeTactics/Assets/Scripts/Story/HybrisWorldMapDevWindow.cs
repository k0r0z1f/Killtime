using System.Collections.Generic;
using UnityEngine;
using Killtime.UI;

namespace Killtime.Story
{
    public class HybrisWorldMapDevWindow : FloatingWindow<HybrisWorldMapDevWindow>
    {
        protected override int WindowId => 992;
        protected override string Title => "Overworld Hybris — Réseau de Secteurs (Causalité Codex)";
        protected override Vector2 MinSize => new Vector2(980f, 640f);
        protected override Rect DefaultRect => new Rect(
            20f, 30f,
            Mathf.Min(1400f, Mathf.Max(980f, Screen.width - 40f)),
            Mathf.Min(900f, Mathf.Max(640f, Screen.height - 60f)));
        protected override KeyCode[] ToggleKeys => _toggleKeys;

        private static readonly KeyCode[] _toggleKeys = { KeyCode.F12 };

        private string _selectedNodeId;
        private bool _godMode;
        private bool _revealAllNames = true;
        private bool _showHexGrid = true;
        private bool _useParchment = true;
        private Texture2D _parchmentTex;

        private Vector2 _rightScroll;

        private Vector2 _canvasPan = Vector2.zero;
        private float _canvasZoom = 1.0f;
        private bool _isPanning = false;
        private Vector2 _panStartMouse;
        private Vector2 _panStartOrigin;
        private const float CanvasBaseWidth = 1100f;
        private const float CanvasBaseHeight = 720f;
        private bool _hasAutoFittedView = false;
        private Rect _lastCanvasViewRect;

        private WorldMapPathResult _currentRoute;
        private string _hoveredNodeId;
        private ScenarioDefinition _cachedSelectedScenario;

        protected override void OnOpened()
        {
            HybrisWorldMapData.EnsureInitialized();
            var director = ScenarioDirector.EnsureInstance();
            if (director.State == null) director.ResetCampaign();
            director.State.EnsureWorldDefaults();
            _selectedNodeId = director.State.PartyNodeId;

            if (_parchmentTex == null)
            {
                _parchmentTex = Resources.Load<Texture2D>("WorldMap/Hybris_Parchment_Map")
                             ?? Resources.Load<Texture2D>("Hybris_Parchment_Map")
                             ?? Resources.Load<Texture2D>("hybris_map");
            }

            _hasAutoFittedView = false;
            RecalculateRouteAndScenario();
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

        protected override void DrawContent()
        {
            var director = ScenarioDirector.EnsureInstance();
            if (director.State == null) director.ResetCampaign();
            director.State.EnsureWorldDefaults();

            DrawTopBar(director);

            GUILayout.BeginHorizontal(GUILayout.ExpandHeight(true));
            DrawMapCanvas(director);
            DrawInspectorPanel(director);
            GUILayout.EndHorizontal();
        }

        private void DrawTopBar(ScenarioDirector director)
        {
            var state = director.State;
            var party = HybrisWorldMapData.Find(state.PartyNodeId);

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>◎ Groupe : {party?.Name ?? state.PartyNodeId}</b>", GUILayout.Width(220));
            GUILayout.Label($"Jours de voyage : <b>{state.GetInt("jours_voyage")}</b>", GUILayout.Width(150));
            GUILayout.Label(HybrisWorldMapData.IsCustomized
                ? $"<color=cyan>🗺️ {HybrisWorldMapData.ActiveMapName} (rev {HybrisWorldMapData.ActiveRevision})</color>"
                : "<color=grey>🗺️ Canon Codex</color>", GUILayout.Width(150));
            
            _revealAllNames = GUILayout.Toggle(_revealAllNames, "👁️ Révéler Noms", GUILayout.Width(125));
            _showHexGrid = GUILayout.Toggle(_showHexGrid, "⬡ Grille Hex", GUILayout.Width(105));
            _useParchment = GUILayout.Toggle(_useParchment, "🗺️ Fond Carte", GUILayout.Width(110));

            GUILayout.FlexibleSpace();
            _godMode = GUILayout.Toggle(_godMode, "🛠️ God Mode", GUILayout.Width(105));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("🎯 Ajuster", GUILayout.Width(90), GUILayout.Height(24)))
            {
                _hasAutoFittedView = false;
            }
            if (GUILayout.Button("4×", GUILayout.Width(40), GUILayout.Height(24))) ApplyZoomPreset(4f);
            if (GUILayout.Button("15×", GUILayout.Width(44), GUILayout.Height(24))) ApplyZoomPreset(15f);
            if (GUILayout.Button("43× (40mi)", GUILayout.Width(82), GUILayout.Height(24))) ApplyZoomPreset(43f);

            float curViewW = _lastCanvasViewRect.width > 50f ? _lastCanvasViewRect.width : CanvasBaseWidth;
            float curVisMiles = (curViewW / Mathf.Max(1f, CanvasBaseWidth * _canvasZoom)) * (300f / 0.175f);
            GUILayout.Label($"Zoom: <b>{_canvasZoom:0.0}×</b> (~{Mathf.RoundToInt(curVisMiles)} mi)", GUILayout.Width(140));

            if (GUILayout.Button("🔓 Déverrouiller Tout", GUILayout.Height(24))) director.UnlockAllSectors();
            if (GUILayout.Button("⛵ Autoriser Expédition Est", GUILayout.Height(24))) director.AllowEasternExpedition();
            if (GUILayout.Button("↻ Recharger Fichier", GUILayout.Width(140), GUILayout.Height(24)))
            {
                HybrisWorldMapData.ForceReloadActive();
                director.State.EnsureWorldDefaults();
                _selectedNodeId = director.State.PartyNodeId;
                RecalculateRouteAndScenario();
            }
            if (GUILayout.Button("🛠️ Éditeur de Carte", GUILayout.Width(140), GUILayout.Height(24)))
            {
                CloseWindow();
                HybrisWorldMapEditorWindow.Open();
            }
            if (GUILayout.Button("↺ Réinitialiser", GUILayout.Width(100), GUILayout.Height(24)))
            {
                director.ResetWorldMap();
                _selectedNodeId = director.State.PartyNodeId;
                RecalculateRouteAndScenario();
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private void DrawMapCanvas(ScenarioDirector director)
        {
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

            Rect viewRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
                GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

            if (viewRect.width > 100f && viewRect.height > 100f)
            {
                _lastCanvasViewRect = viewRect;
                if (!_hasAutoFittedView)
                {
                    FitToView(viewRect);
                    _hasAutoFittedView = true;
                }

                HandleCanvasNavigation(viewRect);

                GUI.BeginClip(viewRect);
                Rect canvasRect = new Rect(_canvasPan.x, _canvasPan.y, CanvasBaseWidth * _canvasZoom, CanvasBaseHeight * _canvasZoom);

                Texture2D texToDraw = _useParchment ? _parchmentTex : null;
                HybrisWorldMapDraw.DrawBackdrop(canvasRect, viewRect, _canvasZoom, texToDraw, 0.88f, _showHexGrid);

                DrawLinks(canvasRect, director);
                DrawNodes(canvasRect, director);

                if (!string.IsNullOrEmpty(_hoveredNodeId))
                {
                    var hNode = HybrisWorldMapData.Find(_hoveredNodeId);
                    if (hNode != null)
                    {
                        HybrisWorldMapDraw.DrawTooltip(new Rect(0, 0, viewRect.width, viewRect.height),
                            hNode.Name, $"{HybrisWorldMapData.BiomeLabel(hNode.Biome)} — {HybrisWorldMapData.DangerLabel(hNode.Danger)}");
                    }
                }

                GUI.EndClip();
            }

            GUILayout.Label("<i>Navigation : Molette = Zoom • Clic droit / Alt+Clic = Pan • Clic secteur = Tracer Route</i>");
            GUILayout.EndVertical();
        }

        private void HandleCanvasNavigation(Rect viewRect)
        {
            Event e = Event.current;
            if (e == null) return;
            Vector2 mp = e.mousePosition;
            bool inside = viewRect.Contains(mp);

            if (e.type == EventType.MouseUp || e.rawType == EventType.MouseUp)
            {
                if (_isPanning)
                {
                    _isPanning = false;
                    e.Use();
                    return;
                }
                _isPanning = false;
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
                _canvasPan = _panStartOrigin + (mp - _panStartMouse);
                e.Use();
                return;
            }

            Rect canvasRect = new Rect(_canvasPan.x, _canvasPan.y, CanvasBaseWidth * _canvasZoom, CanvasBaseHeight * _canvasZoom);
            Vector2 localPos = mp - viewRect.position;

            _hoveredNodeId = null;
            if (inside)
            {
                foreach (var n in HybrisWorldMapData.ActiveNodes)
                {
                    if (n == null) continue;
                    Vector2 np = HybrisWorldMapDraw.NodeCanvasPos(canvasRect, n);
                    if (Vector2.Distance(localPos, np) < 30f * _canvasZoom)
                    {
                        _hoveredNodeId = n.Id;
                        break;
                    }
                }
            }

            if (inside)
            {
                if (e.type == EventType.MouseDown && (e.button == 1 || e.button == 2 || (e.button == 0 && e.alt)))
                {
                    _isPanning = true;
                    _panStartMouse = mp;
                    _panStartOrigin = _canvasPan;
                    e.Use();
                    return;
                }

                if (e.type == EventType.MouseDown && e.button == 0)
                {
                    if (!string.IsNullOrEmpty(_hoveredNodeId))
                    {
                        _selectedNodeId = _hoveredNodeId;
                        RecalculateRouteAndScenario();
                        if (e.clickCount >= 2)
                        {
                            var targetNode = HybrisWorldMapData.Find(_selectedNodeId);
                            if (targetNode != null)
                            {
                                ZoomToNode(targetNode, 43.0f, viewRect);
                            }
                        }
                        e.Use();
                        return;
                    }
                    else
                    {
                        _isPanning = true;
                        _panStartMouse = mp;
                        _panStartOrigin = _canvasPan;
                        e.Use();
                        return;
                    }
                }

                if (e.type == EventType.MouseDrag)
                {
                    e.Use();
                    return;
                }
            }
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

        private void ZoomToNode(HybrisSectorNode node, float targetZoom, Rect viewRect)
        {
            if (node == null) return;
            _canvasZoom = Mathf.Clamp(targetZoom, 0.30f, 50.0f);
            float viewW = viewRect.width > 50f ? viewRect.width : CanvasBaseWidth;
            float viewH = viewRect.height > 50f ? viewRect.height : CanvasBaseHeight;
            _canvasPan = new Vector2(
                (viewW * 0.5f) - (node.MapPos.x * CanvasBaseWidth * _canvasZoom),
                (viewH * 0.5f) - (node.MapPos.y * CanvasBaseHeight * _canvasZoom));
        }

        private void DrawLinks(Rect canvasRect, ScenarioDirector director)
        {
            var state = director.State;
            var pathLinks = _currentRoute?.Links ?? new List<HybrisSectorLink>();

            foreach (var l in HybrisWorldMapData.ActiveLinks)
            {
                if (l == null) continue;
                var a = HybrisWorldMapData.Find(l.FromId);
                var b = HybrisWorldMapData.Find(l.ToId);
                if (a == null || b == null) continue;

                Vector2 pa = HybrisWorldMapDraw.NodeCanvasPos(canvasRect, a);
                Vector2 pb = HybrisWorldMapDraw.NodeCanvasPos(canvasRect, b);

                bool isRoutePart = pathLinks.Contains(l);
                bool locked = !string.IsNullOrEmpty(l.RequiresFlag) && !state.HasFlag(l.RequiresFlag);

                Color c = isRoutePart ? new Color(1f, 0.85f, 0.1f, 0.95f)
                    : locked ? new Color(0.85f, 0.25f, 0.25f, 0.55f)
                    : l.IsSeaCrossing ? new Color(0.35f, 0.75f, 1f, 0.75f)
                    : new Color(0.60f, 0.85f, 0.65f, 0.65f);

                float width = Mathf.Clamp((isRoutePart ? 4.0f : 2.0f) * Mathf.Sqrt(_canvasZoom), 1.5f, 8f);

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

        private void DrawNodes(Rect canvasRect, ScenarioDirector director)
        {
            var state = director.State;

            foreach (var node in HybrisWorldMapData.ActiveNodes)
            {
                if (node == null) continue;
                Vector2 p = HybrisWorldMapDraw.NodeCanvasPos(canvasRect, node);
                bool isParty = state.PartyNodeId == node.Id;
                bool isSelected = _selectedNodeId == node.Id;
                bool isVisited = state.IsSectorVisited(node.Id);
                bool isUnlocked = state.IsSectorUnlocked(node.Id) || _godMode;
                bool locked = !string.IsNullOrEmpty(node.RequiredFlag) && !state.HasFlag(node.RequiredFlag);

                Color prev = GUI.backgroundColor;

                if (isParty)
                {
                    GUI.backgroundColor = new Color(0.95f, 0.80f, 0.15f);
                    GUI.Label(new Rect(p.x - 12f, p.y - 32f * _canvasZoom, 30f, 20f), "<color=yellow><b><size=16>◎</size></b></color>");
                }
                else if (isSelected)
                {
                    GUI.backgroundColor = new Color(0.25f, 0.80f, 1.0f);
                }
                else if (locked)
                {
                    GUI.backgroundColor = new Color(0.40f, 0.20f, 0.20f);
                }
                else if (isVisited)
                {
                    GUI.backgroundColor = new Color(0.30f, 0.65f, 0.40f);
                }
                else if (isUnlocked)
                {
                    GUI.backgroundColor = new Color(0.40f, 0.45f, 0.55f);
                }
                else
                {
                    GUI.backgroundColor = new Color(0.25f, 0.25f, 0.28f, 0.70f);
                }

                string displayName = (isUnlocked || _revealAllNames) ? node.Name : "???";
                string prefix = locked ? "🔒 " : (isParty ? "★ " : "");
                string fullLabel = prefix + displayName;

                Vector2 textSize = GUI.skin.button.CalcSize(new GUIContent(fullLabel));
                float bw = Mathf.Max(95f, (textSize.x + 16f) * Mathf.Clamp(_canvasZoom, 0.75f, 1.25f));
                float bh = Mathf.Max(22f, 24f * Mathf.Clamp(_canvasZoom, 0.75f, 1.25f));
                Rect r = new Rect(p.x - bw * 0.5f, p.y - bh * 0.5f, bw, bh);

                if (GUI.Button(r, fullLabel))
                {
                    _selectedNodeId = node.Id;
                    RecalculateRouteAndScenario();
                }

                GUI.backgroundColor = prev;
            }
        }

        private void DrawInspectorPanel(ScenarioDirector director)
        {
            var state = director.State;
            var node = HybrisWorldMapData.Find(_selectedNodeId);

            GUILayout.BeginVertical(GUILayout.Width(350), GUILayout.ExpandHeight(true));
            _rightScroll = GUILayout.BeginScrollView(_rightScroll, GUILayout.ExpandHeight(true));

            if (node == null)
            {
                GUILayout.Label("<color=grey>Sélectionnez un secteur sur la carte.</color>");
                GUILayout.EndScrollView();
                GUILayout.EndVertical();
                return;
            }

            bool isParty = state.PartyNodeId == node.Id;

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label($"<b><size=13>{node.Name}</size></b>");
            GUILayout.Label($"<color=orange>{node.Region}</color>");
            GUILayout.Label($"Coordonnées Axiales : <b>(Q={node.HexQ}, R={node.HexR}, S={node.HexS})</b>");
            GUILayout.Label($"{HybrisWorldMapData.BiomeLabel(node.Biome)} • {HybrisWorldMapData.DangerLabel(node.Danger)}");
            GUILayout.Label(node.Description);
            GUILayout.EndVertical();

            GUILayout.Space(6);
            GUILayout.Label("<b>Planification d'Itinéraire</b>");
            if (isParty)
            {
                GUILayout.Label("<color=yellow>◎ Le groupe est actuellement stationné dans ce secteur.</color>");
            }
            else if (_currentRoute != null && _currentRoute.IsReachable)
            {
                GUILayout.Label($"Distance : <b>{_currentRoute.TotalMiles} miles</b> | Durée : <b>{_currentRoute.TotalDays} jour(s)</b>");
                GUILayout.Label($"Étapes : {string.Join(" ➔ ", _currentRoute.Path)}");

                GUI.backgroundColor = new Color(0.2f, 0.85f, 0.4f);
                if (GUILayout.Button($"🚀 Parcourir l'Itinéraire ({_currentRoute.TotalDays} j)", GUILayout.Height(36)))
                {
                    director.TravelPath(_currentRoute.Path, _godMode);
                    RecalculateRouteAndScenario();
                }
                GUI.backgroundColor = Color.white;
            }
            else
            {
                GUILayout.Label($"<color=red>Route bloquée : {_currentRoute?.BlockedReason}</color>");
                if (_godMode && GUILayout.Button("🛠️ Téléporter (God Mode)", GUILayout.Height(30)))
                {
                    director.TeleportPartyTo(node.Id);
                    RecalculateRouteAndScenario();
                }
            }

            GUILayout.Space(6);
            if (!string.IsNullOrWhiteSpace(node.LinkedScenarioId))
            {
                GUILayout.Label("<b>Scène Tactique Associée</b>");
                if (_cachedSelectedScenario != null)
                {
                    GUILayout.Label($"<color=cyan>{_cachedSelectedScenario.Title}</color>");
                    if (GUILayout.Button("⚔️ Déployer Scène 3D", GUILayout.Height(32)))
                    {
                        StorySceneManager.EnsureInstance().StartScenario(_cachedSelectedScenario.Id);
                    }
                }
                else
                {
                    GUILayout.Label($"<color=grey>Scénario '{node.LinkedScenarioId}' introuvable.</color>");
                }
            }

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void RecalculateRouteAndScenario()
        {
            var director = ScenarioDirector.EnsureInstance();
            if (director?.State == null || string.IsNullOrEmpty(_selectedNodeId)) return;

            var node = HybrisWorldMapData.Find(_selectedNodeId);
            _cachedSelectedScenario = (!string.IsNullOrWhiteSpace(node?.LinkedScenarioId))
                ? ScenarioCatalog.Find(node.LinkedScenarioId)
                : null;

            _currentRoute = HybrisWorldMapData.FindShortestPath(
                director.State.PartyNodeId,
                _selectedNodeId,
                _godMode,
                director.State);
        }
    }
}
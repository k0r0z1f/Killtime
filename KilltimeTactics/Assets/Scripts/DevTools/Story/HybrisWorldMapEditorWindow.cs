using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.UI;
using Killtime.Core.World;

namespace Killtime.Story
{
    public partial class HybrisWorldMapEditorWindow : FloatingWindow<HybrisWorldMapEditorWindow>
    {
        protected override int WindowId => 993;
        protected override string Title => "Éditeur World Map — Overworld d'Hybris (F9)";
        protected override Vector2 MinSize => new Vector2(1040f, 680f);
        protected override Rect DefaultRect => new Rect(
            20f, 35f,
            Mathf.Min(1400f, Mathf.Max(1040f, Screen.width - 30f)),
            Mathf.Min(920f, Mathf.Max(680f, Screen.height - 45f)));
        protected override KeyCode[] ToggleKeys => _toggleKeys;

        private static readonly KeyCode[] _toggleKeys = { KeyCode.F9 };

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
    }
}

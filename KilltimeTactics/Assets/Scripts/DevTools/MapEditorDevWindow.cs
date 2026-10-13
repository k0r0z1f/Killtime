using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Core.Character;

namespace Killtime.UI
{
    /// <summary>
    /// Éditeur de carte tactique avec placement dynamique d'obstacles procéduraux,
    /// gestion des dalles de plafond et d'objets 3D personnalisés configurables (Sol, Plafond, Mur, Flottant).
    /// Façade : le contenu vit dans les partiels (Types, Brushes, Props, Grounds, Storage, UI).
    /// </summary>
    public partial class MapEditorDevWindow : FloatingWindow<MapEditorDevWindow>
    {
        protected override int WindowId => 890;
        protected override string Title => "Éditeur de Cartes 3D";
        protected override Vector2 MinSize => _minSize;
        protected override Rect DefaultRect => new Rect(120, 100, 560, 700);
        protected override KeyCode[] ToggleKeys => _toggleKeys;

        private static readonly KeyCode[] _toggleKeys = { KeyCode.F2 };
        private static readonly Vector2 _minSize = new Vector2(360, 220);

        [Header("Systèmes")]
        [SerializeField] private TacticalHexGrid _grid;
        [SerializeField] private HexGridVisualizer _gridVisualizer;
        [SerializeField] private CombatDevArena _arena;
        [SerializeField] private TurnManager _turnManager;

        private MapBrushType _activeBrush = MapBrushType.HalfCover;
        private string _mapNameInput = "Killzone_Alpha3";
        private string _statusMessage = "Prêt.";
        private string _gridRadiusInput = "6";
        private float _ceilingHeightInput = 3.5f;

        private Vector2 _scrollPos;
        private Vector2 _propsScrollPos;
        private Vector2 _groundTexturesScrollPos;
        private int _selectedTab = 0;
        private readonly string[] _tabTitles = { "🖌️ Couvertures & Plafond", "🏔️ Sols & Relief", "🏺 Objets 3D", "💾 Cartes", "📐 Grille" };

        private HexNode _inspectedNode;

        // --- Ground Creator & Sculpture du Relief ---
        private readonly List<GroundTextureRecord> _availableGroundTextures = new();
        private readonly List<string> _groundCategories = new();
        private int _selectedCategoryIndex = 0;
        private int _selectedGroundIndex = 0;
        private float _sculptStep = 0.25f;
        private float _targetElevation = 1.0f;
        private int _sculptRadius = 1;
        private bool _smoothSlopeTerrain = false;
        private float _groundTiling = 1.0f;

        // --- Gestionnaire d'Objets 3D & Ancrages ---
        private readonly List<string> _availablePropNames = new();
        private int _selectedPropIndex = 0;
        private float _propRotationY = 0f;
        private float _propScale = 1.0f;
        private CoverType _propCover = CoverType.Full;
        private bool _propIsWalkable = false;
        private PropPlacementType _propPlacementType = PropPlacementType.Plancher;
        private float _propHeightOffset = 0.0f;

        private Transform _propsRoot;
        private readonly Dictionary<string, GameObject> _spawnedPropInstances = new();
        private readonly Dictionary<string, MapPropData> _placedPropRecords = new();

        private static string GetPropKey(int q, int r, PropPlacementType type) => $"{q}_{r}_{type}";
        private static string GetPropKey(HexCoordinates coords, PropPlacementType type) => $"{coords.Q}_{coords.R}_{type}";

        private List<MapPropData> GetPropsAt(HexCoordinates coords)
        {
            List<MapPropData> list = new();
            foreach (var prop in _placedPropRecords.Values)
            {
                if (prop != null && prop.Q == coords.Q && prop.R == coords.R)
                {
                    list.Add(prop);
                }
            }
            return list;
        }

        private static string MapsDirectory => Path.Combine(Application.persistentDataPath, "Maps");

        protected override void OnOpened()
        {
            EnsureReferences();
            try { LoadDevPrefs(); } catch { /* ignore */ }
        }

        protected override void Awake()
        {
            base.Awake();
            EnsureReferences();
            EnsureDirectoryExists();
            RefreshAvailableProps();
            RefreshAvailableGroundTextures();
            EnsurePropsRoot();
            SyncPropsFromScene();
            if (_grid != null) _ceilingHeightInput = _grid.CeilingHeight;
            try { LoadDevPrefs(); } catch { /* ignore */ }
        }

        protected override void OnClosed()
        {
            try { CaptureDevPrefs(); DevUIPreferences.SaveNow(); } catch { /* ignore */ }
            _gridVisualizer?.SetCeilingVisualMode(CeilingVisualMode.InGame);
        }

        private void LoadDevPrefs()
        {
            var p = DevUIPreferences.Current;
            if (p == null) return;
            int brushCount = Enum.GetValues(typeof(MapBrushType)).Length;
            _activeBrush = (MapBrushType)Mathf.Clamp(p.MapBrush, 0, Math.Max(0, brushCount - 1));
            if (!string.IsNullOrEmpty(p.MapName)) _mapNameInput = p.MapName;
            if (!string.IsNullOrEmpty(p.GridRadiusInput)) _gridRadiusInput = p.GridRadiusInput;
            _ceilingHeightInput = Mathf.Clamp(p.CeilingHeight, 2f, 8f);
            _selectedTab = Mathf.Clamp(p.MapEditorTab, 0, _tabTitles.Length - 1);
            _sculptStep = Mathf.Clamp(p.SculptStep, 0.05f, 2f);
            _targetElevation = Mathf.Clamp(p.TargetElevation, -5f, 8f);
            _sculptRadius = Mathf.Clamp(p.SculptRadius, 1, 5);
            _smoothSlopeTerrain = p.SmoothSlope;
            _groundTiling = Mathf.Clamp(p.GroundTiling, 0.1f, 8f);
            _selectedPropIndex = Math.Max(0, p.SelectedPropIndex);
            if (_availablePropNames.Count > 0)
                _selectedPropIndex = Mathf.Clamp(_selectedPropIndex, 0, _availablePropNames.Count - 1);
            _propRotationY = p.PropRotationY;
            _propScale = Mathf.Clamp(p.PropScale, 0.2f, 3f);
            int coverCount = Enum.GetValues(typeof(CoverType)).Length;
            _propCover = (CoverType)Mathf.Clamp(p.PropCover, 0, Math.Max(0, coverCount - 1));
            _propIsWalkable = p.PropWalkable;
            int placeCount = Enum.GetValues(typeof(PropPlacementType)).Length;
            _propPlacementType = (PropPlacementType)Mathf.Clamp(p.PropPlacement, 0, Math.Max(0, placeCount - 1));
            _propHeightOffset = Mathf.Clamp(p.PropHeightOffset, -2f, 4f);
            _selectedGroundIndex = Math.Max(0, p.SelectedGroundIndex);
            _selectedCategoryIndex = Math.Max(0, p.SelectedCategoryIndex);
            if (_grid != null) _grid.CeilingHeight = _ceilingHeightInput;
            if (_gridVisualizer != null)
            {
                _gridVisualizer.CeilingOpacity = Mathf.Clamp(p.CeilingOpacity, 0.01f, 0.8f);
                _gridVisualizer.ShowCeilingInGame = p.ShowCeilingInGame;
                _gridVisualizer.SmoothSlopeTerrain = p.SmoothSlope;
                _gridVisualizer.GroundTiling = Mathf.Clamp(p.GroundTiling, 0.1f, 8f);
            }
        }

        private void CaptureDevPrefs()
        {
            var p = DevUIPreferences.Current;
            if (p == null) return;
            p.MapBrush = (int)_activeBrush;
            p.MapName = _mapNameInput ?? "Killzone_Alpha3";
            p.GridRadiusInput = _gridRadiusInput ?? "6";
            p.CeilingHeight = _ceilingHeightInput;
            p.MapEditorTab = _selectedTab;
            p.SculptStep = _sculptStep;
            p.TargetElevation = _targetElevation;
            p.SculptRadius = _sculptRadius;
            p.SmoothSlope = _smoothSlopeTerrain;
            p.GroundTiling = _groundTiling;
            p.SelectedPropIndex = _selectedPropIndex;
            p.PropRotationY = _propRotationY;
            p.PropScale = _propScale;
            p.PropCover = (int)_propCover;
            p.PropWalkable = _propIsWalkable;
            p.PropPlacement = (int)_propPlacementType;
            p.PropHeightOffset = _propHeightOffset;
            p.SelectedGroundIndex = _selectedGroundIndex;
            p.SelectedCategoryIndex = _selectedCategoryIndex;
            if (_grid != null) p.CeilingHeight = _grid.CeilingHeight;
            if (_gridVisualizer != null)
            {
                p.CeilingOpacity = _gridVisualizer.CeilingOpacity;
                p.ShowCeilingInGame = _gridVisualizer.ShowCeilingInGame;
                p.SmoothSlope = _gridVisualizer.SmoothSlopeTerrain;
                p.GroundTiling = _gridVisualizer.GroundTiling;
            }
            DevUIPreferences.MarkDirty();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            SyncPropsFromScene();
        }

        private void SyncPropsFromScene()
        {
            EnsurePropsRoot();
            if (_propsRoot == null) return;

            var propComps = _propsRoot.GetComponentsInChildren<MapPropInstance>(true);
            for (int i = 0; i < propComps.Length; i++)
            {
                var p = propComps[i];
                if (p != null && p.Data != null)
                {
                    string key = GetPropKey(p.Data.Q, p.Data.R, p.Data.PlacementType);
                    _placedPropRecords[key] = p.Data;
                    _spawnedPropInstances[key] = p.gameObject;
                }
            }
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            try { CaptureDevPrefs(); } catch { /* ignore */ }
            _gridVisualizer?.SetCeilingVisualMode(CeilingVisualMode.InGame);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            _gridVisualizer?.SetCeilingVisualMode(CeilingVisualMode.InGame);
        }

        private void EnsureReferences()
        {
            if (_grid == null) _grid = FindAnyObjectByType<TacticalHexGrid>();
            if (_gridVisualizer == null) _gridVisualizer = FindAnyObjectByType<HexGridVisualizer>();
            if (_arena == null) _arena = FindAnyObjectByType<CombatDevArena>();
            if (_turnManager == null) _turnManager = FindAnyObjectByType<TurnManager>();
        }

        private void EnsurePropsRoot()
        {
            if (_propsRoot == null)
            {
                var existing = GameObject.Find("[TacticalMap_CustomProps]");
                if (existing != null)
                {
                    _propsRoot = existing.transform;
                }
                else
                {
                    var go = new GameObject("[TacticalMap_CustomProps]");
                    _propsRoot = go.transform;
                }
            }
        }
    }
}

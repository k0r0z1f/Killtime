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
    public enum MapBrushType
    {
        Inspect,
        ClearGround,
        HalfCover,
        FullCover,
        ImpassableHole,
        AddCeiling,
        RemoveCeiling,
        PlaceProp3D,
        EraseProp3D,
        PaintGroundTexture,
        ClearGroundTexture,
        ElevateGround,
        LowerGround,
        SetElevation,
        SmoothElevation,
        ThreeQuartersCover
    }

    public enum PropPlacementType
    {
        Plancher,
        Plafond,
        Mur,
        Flottant
    }

    [Serializable]
    public class MapTileData
    {
        public int Q;
        public int R;
        public CoverType Cover;
        public bool IsWalkable;
        public bool HasCeiling;
        public float Elevation;
        public string GroundTexture;
    }

    [Serializable]
    public class GroundTextureRecord
    {
        public string RelativePath;
        public string Category;
        public string DisplayName;
        public Texture2D Texture;
    }

    [Serializable]
    public class MapPropData
    {
        public string PrefabName;
        public int Q;
        public int R;
        public float RotationY;
        public float Scale = 1.0f;
        public CoverType Cover = CoverType.Full;
        public bool IsWalkable = false;
        public PropPlacementType PlacementType = PropPlacementType.Plancher;
        public float HeightOffset = 0.0f;
    }

    public class MapPropInstance : MonoBehaviour
    {
        public MapPropData Data;
    }

    [Serializable]
    public class MapUnitData
    {
        public string UnitId;
        public CharacterSheet Sheet;
        public int Q;
        public int R;
        public bool IsPlayer;
        public int currentHealth;
        public int currentAP;
        public int essoufflement;
        public int activeStatus;
        public List<string> statusEffects = new();
    }

    [Serializable]
    public class TacticalMapSaveData
    {
        public string MapName = "Nouvelle_Carte";
        public int GridRadius = 8;
        public float CeilingHeight = 3.5f;
        public string SaveTimestamp = "";
        public List<MapTileData> ModifiedTiles = new();
        public List<MapPropData> PlacedProps = new();
        public List<MapUnitData> PlacedUnits = new();
        public List<Vector3> PlacedPixies = new();
    }

    /// <summary>
    /// Éditeur de carte tactique avec placement dynamique d'obstacles procéduraux,
    /// gestion des dalles de plafond et d'objets 3D personnalisés configurables (Sol, Plafond, Mur, Flottant).
    /// </summary>
    public class MapEditorDevWindow : FloatingWindow<MapEditorDevWindow>
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

        public void RefreshAvailableProps()
        {
            _availablePropNames.Clear();

#if UNITY_EDITOR
            UnityEditor.AssetDatabase.Refresh();
            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
            foreach (string guid in guids)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) continue;

                int resIndex = path.IndexOf("/Resources/Objects/", StringComparison.OrdinalIgnoreCase);
                if (resIndex >= 0)
                {
                    string relative = path.Substring(resIndex + "/Resources/Objects/".Length);
                    string ext = Path.GetExtension(relative);
                    if (!string.IsNullOrEmpty(ext))
                    {
                        relative = relative.Substring(0, relative.Length - ext.Length);
                    }
                    if (!_availablePropNames.Contains(relative))
                    {
                        _availablePropNames.Add(relative);
                    }
                }
            }
#endif

            if (_availablePropNames.Count == 0)
            {
                var prefabs = Resources.LoadAll<GameObject>("Objects");
                if (prefabs == null || prefabs.Length == 0)
                {
                    prefabs = Resources.LoadAll<GameObject>("objects");
                }

                if (prefabs != null)
                {
                    for (int i = 0; i < prefabs.Length; i++)
                    {
                        if (prefabs[i] != null && !_availablePropNames.Contains(prefabs[i].name))
                        {
                            _availablePropNames.Add(prefabs[i].name);
                        }
                    }
                }
            }
        }

        private GameObject LoadPropPrefab(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName)) return null;

#if UNITY_EDITOR
            string[] guids = UnityEditor.AssetDatabase.FindAssets($"{prefabName} t:Prefab", new[] { "Assets" });
            foreach (string guid in guids)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                {
                    string fName = Path.GetFileNameWithoutExtension(path);
                    if (string.Equals(fName, prefabName, StringComparison.OrdinalIgnoreCase))
                    {
                        var loaded = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                        if (loaded != null) return loaded;
                    }
                }
            }
#endif

            var prefab = Resources.Load<GameObject>($"Objects/{prefabName}")
                      ?? Resources.Load<GameObject>($"objects/{prefabName}")
                      ?? Resources.Load<GameObject>($"Characters/{prefabName}")
                      ?? Resources.Load<GameObject>(prefabName);

            if (prefab == null)
            {
                var allInObjects = Resources.LoadAll<GameObject>("Objects");
                for (int i = 0; i < allInObjects.Length; i++)
                {
                    if (allInObjects[i] != null && string.Equals(allInObjects[i].name, prefabName, StringComparison.OrdinalIgnoreCase))
                    {
                        return allInObjects[i];
                    }
                }
            }

            return prefab;
        }

        protected override void Update()
        {
            base.Update();

            if (_isOpen)
            {
                EnsureCeilingPropsRegistered();

                bool isSpecificCeilingMode = _activeBrush == MapBrushType.AddCeiling ||
                                             _activeBrush == MapBrushType.RemoveCeiling ||
                                             (_selectedTab == 1 && _propPlacementType == PropPlacementType.Plafond) ||
                                             (_selectedTab == 0 && _activeBrush == MapBrushType.Inspect && _inspectedNode != null && _inspectedNode.HasCeiling);

                var targetMode = isSpecificCeilingMode ? CeilingVisualMode.EditorCeilingMode : CeilingVisualMode.EditorNonCeilingMode;
                _gridVisualizer?.SetCeilingVisualMode(targetMode);

                HandlePaintingInput();
            }
        }

        private void EnsureCeilingPropsRegistered()
        {
            foreach (var kvp in _placedPropRecords)
            {
                var data = kvp.Value;
                if (data != null && data.PlacementType == PropPlacementType.Plafond)
                {
                    if (_spawnedPropInstances.TryGetValue(kvp.Key, out var inst) && inst != null)
                    {
                        if (!inst.TryGetComponent<TacticalCeilingProp>(out var prop))
                        {
                            prop = inst.AddComponent<TacticalCeilingProp>();
                            prop.Initialize();
                        }
                    }
                }
            }
        }

        private void HandlePaintingInput()
        {
            if (UnityEngine.Camera.main == null || _grid == null) return;

            Vector2 mouseScreenPos = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            // Toute fenêtre flottante ou panel HUD bloque la peinture (pas seulement notre fenêtre).
            // Évite de peindre la carte en cliquant dans le Créateur de Personnage, la Table des Règles, etc.
            if (Killtime.UI.FloatingWindowChrome.IsPointerOverAnyWindow(mouseScreenPos))
            {
                _gridVisualizer?.SetHoveredCoord(null, false);
                return;
            }

            bool isCeilingTargeting = (_activeBrush == MapBrushType.AddCeiling) ||
                                      (_activeBrush == MapBrushType.RemoveCeiling) ||
                                      (_selectedTab == 2 && _propPlacementType == PropPlacementType.Plafond) ||
                                      (_activeBrush == MapBrushType.Inspect && _inspectedNode != null && _inspectedNode.HasCeiling);

            Ray ray = UnityEngine.Camera.main.ScreenPointToRay(Input.mousePosition);
            HexNode node = null;

            if (isCeilingTargeting)
            {
                Plane ceilingPlane = new Plane(Vector3.up, new Vector3(0, _grid.CeilingHeight, 0));
                if (ceilingPlane.Raycast(ray, out float enter))
                {
                    Vector3 ceilingHitPos = ray.GetPoint(enter);
                    _grid.TryGetNodeAtWorldPosition(ceilingHitPos, out node);
                }
            }

            if (node == null && Physics.Raycast(ray, out RaycastHit hit))
            {
                _grid.TryGetNodeAtWorldPosition(hit.point, out node);
            }

            if (node != null)
            {
                _gridVisualizer?.SetHoveredCoord(node.Coordinates, isCeilingTargeting);

                if (Input.GetMouseButtonDown(0))
                {
                    // Clic carte 3D : repli auto des fenêtres flottantes en coins (fantômes).
                    Killtime.UI.FloatingWindowChrome.OnMapClicked();
                    if (_activeBrush == MapBrushType.Inspect)
                    {
                        _inspectedNode = node;
                        var propsOnNode = GetPropsAt(node.Coordinates);
                        string propInfo = "";
                        if (propsOnNode.Count > 0)
                        {
                            propInfo = " | Modèles: ";
                            for (int pIdx = 0; pIdx < propsOnNode.Count; pIdx++)
                            {
                                propInfo += $"{propsOnNode[pIdx].PrefabName} [{propsOnNode[pIdx].PlacementType}] ";
                            }
                        }
                        string ceilingInfo = node.HasCeiling ? " | Plafond: OUI" : " | Plafond: NON";
                        string groundTexInfo = !string.IsNullOrEmpty(node.GroundTexture) ? $" | Texture: {node.GroundTexture}" : "";
                        _statusMessage = $"Hex ({node.Coordinates.Q}, {node.Coordinates.R}) | Altitude: {node.Elevation:0.##}m | Couverture: {node.Cover}{ceilingInfo}{groundTexInfo}{propInfo}";
                    }
                    else if (_activeBrush == MapBrushType.PlaceProp3D)
                    {
                        PlaceCurrent3DProp(node);
                    }
                    else if (_activeBrush == MapBrushType.EraseProp3D)
                    {
                        RemovePropAt(node.Coordinates);
                    }
                    else
                    {
                        ApplyBrushWithRadius(node, _activeBrush);
                    }
                }
                else if (Input.GetMouseButton(0))
                {
                    if (_activeBrush == MapBrushType.PaintGroundTexture ||
                        _activeBrush == MapBrushType.ClearGroundTexture ||
                        _activeBrush == MapBrushType.ClearGround ||
                        _activeBrush == MapBrushType.HalfCover ||
                        _activeBrush == MapBrushType.ThreeQuartersCover ||
                        _activeBrush == MapBrushType.FullCover ||
                        _activeBrush == MapBrushType.ImpassableHole ||
                        _activeBrush == MapBrushType.AddCeiling ||
                        _activeBrush == MapBrushType.RemoveCeiling)
                    {
                        ApplyBrushWithRadius(node, _activeBrush);
                    }
                }
            }
            else
            {
                _gridVisualizer?.SetHoveredCoord(null, false);
            }
        }

        private void ApplyBrushWithRadius(HexNode centerNode, MapBrushType brush)
        {
            var nodes = GetNodesInBrushRadius(centerNode.Coordinates, _sculptRadius);
            for (int i = 0; i < nodes.Count; i++)
            {
                ApplyTerrainBrush(nodes[i], brush);
            }
        }

        private List<HexNode> GetNodesInBrushRadius(HexCoordinates center, int radius)
        {
            var list = new List<HexNode>();
            if (_grid == null) return list;

            if (radius <= 1)
            {
                var single = _grid.GetNode(center);
                if (single != null) list.Add(single);
                return list;
            }

            for (int q = -radius + 1; q < radius; q++)
            {
                int r1 = Mathf.Max(-radius + 1, -q - radius + 1);
                int r2 = Mathf.Min(radius - 1, -q + radius - 1);
                for (int r = r1; r <= r2; r++)
                {
                    var targetCoords = new HexCoordinates(center.Q + q, center.R + r);
                    var node = _grid.GetNode(targetCoords);
                    if (node != null) list.Add(node);
                }
            }
            return list;
        }

        private void ApplyTerrainBrush(HexNode node, MapBrushType brush)
        {
            bool modified = false;

            switch (brush)
            {
                case MapBrushType.ClearGround:
                    if (node.Cover != CoverType.None || !node.IsWalkable)
                    {
                        node.Cover = CoverType.None;
                        node.IsWalkable = true;
                        modified = true;
                    }
                    RemovePropAt(node.Coordinates, PropPlacementType.Plancher, false);
                    break;

                case MapBrushType.HalfCover:
                    if (node.Cover != CoverType.Half || !node.IsWalkable)
                    {
                        node.Cover = CoverType.Half;
                        node.IsWalkable = true;
                        modified = true;
                    }
                    break;

                case MapBrushType.FullCover:
                    if (node.Cover != CoverType.Full || node.IsWalkable)
                    {
                        node.Cover = CoverType.Full;
                        node.IsWalkable = false;
                        modified = true;
                    }
                    break;

                case MapBrushType.ThreeQuartersCover:
                    if (node.Cover != CoverType.ThreeQuarters || !node.IsWalkable)
                    {
                        node.Cover = CoverType.ThreeQuarters;
                        node.IsWalkable = true;
                        modified = true;
                    }
                    break;

                case MapBrushType.ImpassableHole:
                    if (node.Cover != CoverType.None || node.IsWalkable)
                    {
                        node.Cover = CoverType.None;
                        node.IsWalkable = false;
                        modified = true;
                    }
                    break;

                case MapBrushType.AddCeiling:
                    if (!node.HasCeiling)
                    {
                        node.HasCeiling = true;
                        modified = true;
                    }
                    break;

                case MapBrushType.RemoveCeiling:
                    if (node.HasCeiling)
                    {
                        node.HasCeiling = false;
                        modified = true;
                    }
                    break;

                case MapBrushType.PaintGroundTexture:
                    if (_availableGroundTextures.Count > 0 && _selectedGroundIndex < _availableGroundTextures.Count)
                    {
                        string selectedTexPath = _availableGroundTextures[_selectedGroundIndex].RelativePath;
                        if (node.GroundTexture != selectedTexPath)
                        {
                            node.GroundTexture = selectedTexPath;
                            modified = true;
                        }
                    }
                    break;

                case MapBrushType.ClearGroundTexture:
                    if (!string.IsNullOrEmpty(node.GroundTexture))
                    {
                        node.GroundTexture = "";
                        modified = true;
                    }
                    break;

                case MapBrushType.ElevateGround:
                    node.Elevation += _sculptStep;
                    node.WorldPosition = node.Coordinates.ToWorldPosition(_grid != null ? _grid.HexRadius : 1.0f, (_grid != null ? 0.05f : 0f) + node.Elevation);
                    modified = true;
                    break;

                case MapBrushType.LowerGround:
                    node.Elevation -= _sculptStep;
                    node.WorldPosition = node.Coordinates.ToWorldPosition(_grid != null ? _grid.HexRadius : 1.0f, (_grid != null ? 0.05f : 0f) + node.Elevation);
                    modified = true;
                    break;

                case MapBrushType.SetElevation:
                    node.Elevation = _targetElevation;
                    node.WorldPosition = node.Coordinates.ToWorldPosition(_grid != null ? _grid.HexRadius : 1.0f, (_grid != null ? 0.05f : 0f) + node.Elevation);
                    modified = true;
                    break;

                case MapBrushType.SmoothElevation:
                    if (_grid != null)
                    {
                        float sum = node.Elevation;
                        int count = 1;
                        for (int dir = 0; dir < 6; dir++)
                        {
                            var neighbor = _grid.GetNode(node.Coordinates.GetNeighbor(dir));
                            if (neighbor != null)
                            {
                                sum += neighbor.Elevation;
                                count++;
                            }
                        }
                        node.Elevation = sum / count;
                        node.WorldPosition = node.Coordinates.ToWorldPosition(_grid.HexRadius, 0.05f + node.Elevation);
                        modified = true;
                    }
                    break;
            }

            if (modified && _gridVisualizer != null)
            {
                _gridVisualizer.RefreshObstacles();
            }
        }

        public void RefreshAvailableGroundTextures()
        {
            _availableGroundTextures.Clear();
            _groundCategories.Clear();
            _groundCategories.Add("Tous");

            var loaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

#if UNITY_EDITOR
            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Resources/Grounds" });
            foreach (var guid in guids)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                int idx = path.IndexOf("Resources/Grounds/", StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    string rel = path.Substring(idx + "Resources/Grounds/".Length);
                    string ext = Path.GetExtension(rel);
                    if (!string.IsNullOrEmpty(ext)) rel = rel.Substring(0, rel.Length - ext.Length);

                    if (!loaded.Contains(rel))
                    {
                        var tex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                        if (tex != null)
                        {
                            string dir = Path.GetDirectoryName(rel).Replace('\\', '/');
                            string cat = string.IsNullOrEmpty(dir) ? "Racine" : dir;
                            if (!_groundCategories.Contains(cat)) _groundCategories.Add(cat);

                            _availableGroundTextures.Add(new GroundTextureRecord
                            {
                                RelativePath = rel,
                                Category = cat,
                                DisplayName = Path.GetFileName(rel),
                                Texture = tex
                            });
                            loaded.Add(rel);
                        }
                    }
                }
            }
#endif

            if (_availableGroundTextures.Count == 0)
            {
                var allTextures = Resources.LoadAll<Texture2D>("Grounds");
                for (int i = 0; i < allTextures.Length; i++)
                {
                    var tex = allTextures[i];
                    if (tex != null && !loaded.Contains(tex.name))
                    {
                        _availableGroundTextures.Add(new GroundTextureRecord
                        {
                            RelativePath = tex.name,
                            Category = "Grounds",
                            DisplayName = tex.name,
                            Texture = tex
                        });
                        loaded.Add(tex.name);
                    }
                }
            }
        }

        private void PlaceCurrent3DProp(HexNode node)
        {
            if (_availablePropNames.Count == 0)
            {
                _statusMessage = "Aucun préfab trouvé dans Assets/Resources/Objects.";
                return;
            }

            if (_propPlacementType == PropPlacementType.Plafond && !node.HasCeiling)
            {
                node.HasCeiling = true;
                _grid?.SetNodeCeiling(node.Coordinates, true);
                _gridVisualizer?.RefreshObstacles();
            }

            string prefabName = _availablePropNames[_selectedPropIndex];
            var prefab = LoadPropPrefab(prefabName);

            if (prefab == null)
            {
                _statusMessage = $"Échec de chargement du modèle '{prefabName}'.";
                return;
            }

            EnsurePropsRoot();
            RemovePropAt(node.Coordinates, _propPlacementType, false);

            string propKey = GetPropKey(node.Coordinates, _propPlacementType);

#if UNITY_EDITOR
            var instance = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, _propsRoot);
#else
            var instance = Instantiate(prefab, _propsRoot);
#endif
            instance.name = $"Prop_{prefabName}_{node.Coordinates.Q}_{node.Coordinates.R}_{_propPlacementType}";

            float ceilingHeight = _grid != null ? _grid.CeilingHeight : 3.5f;
            Vector3 worldPos = node.WorldPosition;

            switch (_propPlacementType)
            {
                case PropPlacementType.Plancher:
                    worldPos.y += _propHeightOffset;
                    break;
                case PropPlacementType.Plafond:
                    worldPos.y += ceilingHeight - _propHeightOffset;
                    break;
                case PropPlacementType.Mur:
                    worldPos.y += (ceilingHeight * 0.4f) + _propHeightOffset;
                    break;
                case PropPlacementType.Flottant:
                    worldPos.y += 1.8f + _propHeightOffset;
                    break;
            }

            instance.transform.position = worldPos;
            instance.transform.rotation = Quaternion.Euler(0f, _propRotationY, 0f);
            instance.transform.localScale = Vector3.Scale(prefab.transform.localScale, Vector3.one * _propScale);

            var record = new MapPropData
            {
                PrefabName = prefabName,
                Q = node.Coordinates.Q,
                R = node.Coordinates.R,
                RotationY = _propRotationY,
                Scale = _propScale,
                Cover = _propCover,
                IsWalkable = _propIsWalkable,
                PlacementType = _propPlacementType,
                HeightOffset = _propHeightOffset
            };

            var propInstComp = instance.GetComponent<MapPropInstance>() ?? instance.AddComponent<MapPropInstance>();
            propInstComp.Data = record;

            if (_propPlacementType == PropPlacementType.Plafond)
            {
                var ceilingProp = instance.GetComponent<TacticalCeilingProp>() ?? instance.AddComponent<TacticalCeilingProp>();
                ceilingProp.Initialize();
            }

            _spawnedPropInstances[propKey] = instance;
            _placedPropRecords[propKey] = record;

            if (_propPlacementType != PropPlacementType.Plafond || !_propIsWalkable)
            {
                node.Cover = _propCover;
                node.IsWalkable = _propIsWalkable;
            }
            node.HasCustomVisual = true;

            _gridVisualizer?.RefreshObstacles();
            try { PropObstacleRegistry.Rebuild(_grid != null ? _grid.HexRadius : 1f); } catch { /* ignore */ }
            _statusMessage = $"Modèle '{prefabName}' posé [{_propPlacementType}] en ({node.Coordinates.Q}, {node.Coordinates.R}).";
        }

        private void RemovePropAt(HexCoordinates coords, PropPlacementType? placementType = null, bool restoreNode = true)
        {
            List<string> keysToRemove = new();

            foreach (var kvp in _placedPropRecords)
            {
                if (kvp.Value.Q == coords.Q && kvp.Value.R == coords.R)
                {
                    if (!placementType.HasValue || kvp.Value.PlacementType == placementType.Value)
                    {
                        keysToRemove.Add(kvp.Key);
                    }
                }
            }

            for (int i = 0; i < keysToRemove.Count; i++)
            {
                string key = keysToRemove[i];
                if (_spawnedPropInstances.TryGetValue(key, out var instance) && instance != null)
                {
                    Destroy(instance);
                }
                _spawnedPropInstances.Remove(key);
                _placedPropRecords.Remove(key);
            }

            if (restoreNode && _grid != null)
            {
                bool hasRemainingProps = false;
                foreach (var p in _placedPropRecords.Values)
                {
                    if (p.Q == coords.Q && p.R == coords.R)
                    {
                        hasRemainingProps = true;
                        break;
                    }
                }

                if (!hasRemainingProps)
                {
                    var node = _grid.GetNode(coords);
                    if (node != null)
                    {
                        node.Cover = CoverType.None;
                        node.IsWalkable = true;
                        node.HasCustomVisual = false;
                    }
                }
                _gridVisualizer?.RefreshObstacles();
                try { PropObstacleRegistry.Rebuild(_grid != null ? _grid.HexRadius : 1f); } catch { /* ignore */ }
            }
        }

        private void ClearAllPlacedProps()
        {
            if (_propsRoot != null)
            {
                for (int i = _propsRoot.childCount - 1; i >= 0; i--)
                {
                    var child = _propsRoot.GetChild(i);
                    if (child != null) Destroy(child.gameObject);
                }
            }

            var strayProps = FindObjectsByType<MapPropInstance>();
            for (int i = 0; i < strayProps.Length; i++)
            {
                if (strayProps[i] != null) Destroy(strayProps[i].gameObject);
            }

            _spawnedPropInstances.Clear();
            _placedPropRecords.Clear();
        }

        protected override void DrawContent()
        {

            GUILayout.Space(6);
            int prevTab = _selectedTab;
            _selectedTab = GUILayout.Toolbar(_selectedTab, _tabTitles);
            if (_selectedTab != prevTab)
            {
                if (_selectedTab == 2)
                {
                    _activeBrush = MapBrushType.PlaceProp3D;
                    _statusMessage = "Mode Objets 3D actif : cliquez sur la carte pour poser l'objet sélectionné.";
                }
                else if (_selectedTab == 1)
                {
                    _activeBrush = MapBrushType.Inspect;
                    _statusMessage = "Mode Sols & Relief actif : choisissez une texture ou un outil de sculpture.";
                }
            }
            GUILayout.Space(6);

            if (!string.IsNullOrEmpty(_statusMessage))
            {
                GUI.color = Color.cyan;
                GUILayout.Label($"ℹ️ {_statusMessage}", GUI.skin.box);
                GUI.color = Color.white;
            }

            _scrollPos = GUILayout.BeginScrollView(_scrollPos);

            switch (_selectedTab)
            {
                case 0: DrawTerrainTab(); break;
                case 1: DrawGroundAndSculptTab(); break;
                case 2: DrawProps3DTab(); break;
                case 3: DrawStorageTab(); break;
                case 4: DrawGridSettingsTab(); break;
            }

            GUILayout.EndScrollView();

            if (GUI.changed)
            {
                try { CaptureDevPrefs(); } catch { /* ignore */ }
            }
        }

        private void DrawTerrainTab()
        {
            GUILayout.Label("<b>1. Pinceaux de Sol & Couvertures Géométriques :</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            DrawBrushOption(MapBrushType.Inspect, "🔍 Inspecter", "Affiche l'état complet (sol, couverture, plafond, objets).");
            DrawBrushOption(MapBrushType.ClearGround, "⬛ Sol Dégagé", "Restaure un sol plat et praticable.");
            DrawBrushOption(MapBrushType.HalfCover, "📦 Demi-Couverture", "Muret bas : cible à moitié visible, -1 à l'attaque (Livre VI §25.3).");
            DrawBrushOption(MapBrushType.ThreeQuartersCover, "🧱 Barricade Haute", "Couvert aux 3/4 : seul 1/4 visible, -2 à l'attaque (Livre VI §25.3).");
            DrawBrushOption(MapBrushType.FullCover, "🏛️ Couverture Totale", "Mur / pilier : cible non visible, attaque directe impossible.");
            DrawBrushOption(MapBrushType.ImpassableHole, "🕳️ Gouffre / Vide", "Supprime le passage au sol.");

            GUILayout.EndVertical();

            GUILayout.Space(8);
            GUILayout.Label("<b>2. Gestion des Dalles de Plafond (Transparent / Semi-Transparent) :</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            DrawBrushOption(MapBrushType.AddCeiling, "🏛️ Poser Dalle Plafond", "Place un plafond transparent (semi-transparent en édition).");
            DrawBrushOption(MapBrushType.RemoveCeiling, "🚫 Gomme Plafond", "Supprime le plafond sur les cellules peintes.");

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("🏛️ Couvrir Toute la Grille"))
            {
                _grid?.SetAllCeilings(true);
                _gridVisualizer?.RefreshObstacles();
                _statusMessage = "Plafond étendu sur l'intégralité de la grille.";
            }
            if (GUILayout.Button("🚫 Retirer Tous les Plafonds"))
            {
                _grid?.SetAllCeilings(false);
                _gridVisualizer?.RefreshObstacles();
                _statusMessage = "Tous les plafonds ont été retirés.";
            }
            GUILayout.EndHorizontal();

            if (_gridVisualizer != null)
            {
                GUILayout.Space(6);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"Opacité Plafond : {_gridVisualizer.CeilingOpacity * 100f:0}%", GUILayout.Width(170));
                _gridVisualizer.CeilingOpacity = GUILayout.HorizontalSlider(_gridVisualizer.CeilingOpacity, 0.05f, 0.80f);
                GUILayout.EndHorizontal();

                _gridVisualizer.ShowCeilingInGame = GUILayout.Toggle(_gridVisualizer.ShowCeilingInGame, "👁️ Afficher le Plafond en Jeu (Semi-Transparent)");
            }

            GUILayout.EndVertical();

            GUILayout.Space(8);
            GUILayout.Label("<b>3. Actions Globales :</b>");
            GUILayout.BeginHorizontal();

            if (GUILayout.Button("🧹 Vider Tous les Obstacles"))
            {
                _grid?.ClearAllCovers();
                ClearAllPlacedProps();
                _arena?.ClearLoadedMap();
                _gridVisualizer?.RefreshObstacles();
                _statusMessage = "Carte entièrement dégagée.";
            }

            if (GUILayout.Button("🔄 Rafraîchir les Visuels"))
            {
                _gridVisualizer?.RefreshObstacles();
                _statusMessage = "Rendu 3D synchronisé.";
            }

            GUILayout.EndHorizontal();

            if (_inspectedNode != null)
            {
                GUILayout.Space(8);
                GUILayout.Label("<b>4. Données de la Case Inspectée :</b>");
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label($"Position : <b>({_inspectedNode.Coordinates.Q}, {_inspectedNode.Coordinates.R})</b>");
                GUILayout.Label($"Couverture : {_inspectedNode.Cover}");
                GUILayout.Label($"Praticable : {_inspectedNode.IsWalkable}");
                GUILayout.Label($"Plafond : {(_inspectedNode.HasCeiling ? "<color=#00E5FF>OUI (Actif)</color>" : "NON")}");
                GUILayout.Label($"Occupé : {_inspectedNode.IsOccupied}");

                var unitOnNode = _arena != null ? _arena.GetUnitAtCoords(_inspectedNode.Coordinates) : null;
                if (unitOnNode != null && unitOnNode.Stats != null)
                {
                    string fColor = unitOnNode.IsPlayerControlled ? "#00E5FF" : "#FF5555";
                    string fName = unitOnNode.IsPlayerControlled ? "Joueur" : "Ennemi";
                    GUILayout.Label($"Avatar : <b>{unitOnNode.Stats.Name}</b> [<color={fColor}>{fName}</color>] (PV: {unitOnNode.Stats.CurrentHealth}/{unitOnNode.Stats.MaxHealth})");
                }

                var inspectedProps = GetPropsAt(_inspectedNode.Coordinates);
                for (int pIdx = 0; pIdx < inspectedProps.Count; pIdx++)
                {
                    var prop = inspectedProps[pIdx];
                    GUILayout.Label($"Objet 3D : <color=#00E5FF>{prop.PrefabName}</color> [{prop.PlacementType}] (Rot: {prop.RotationY:0}°, Échelle: {prop.Scale:0.##})");
                }
                GUILayout.EndVertical();
            }
        }

        private void DrawProps3DTab()
        {
            GUILayout.Label("<b>1. Modèles Disponibles (Assets/Resources/Objects) :</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Total trouvé(s) : <b>{_availablePropNames.Count}</b>");
            if (GUILayout.Button("🔄 Scanner le Dossier", GUILayout.Width(150)))
            {
                RefreshAvailableProps();
                _statusMessage = $"{_availablePropNames.Count} modèle(s) 3D chargé(s).";
            }
            GUILayout.EndHorizontal();

            if (_availablePropNames.Count == 0)
            {
                GUILayout.Label("<color=#FF6B6B>Aucun préfab dans Assets/Resources/Objects.\nGlissez vos modèles 3D (.prefab) dans ce dossier.</color>");
            }
            else
            {
                _propsScrollPos = GUILayout.BeginScrollView(_propsScrollPos, GUILayout.Height(120));
                for (int i = 0; i < _availablePropNames.Count; i++)
                {
                    bool isSelected = (_selectedPropIndex == i);
                    GUI.backgroundColor = isSelected ? new Color(0.1f, 0.7f, 1f) : Color.white;
                    if (GUILayout.Button($"📦 {_availablePropNames[i]}"))
                    {
                        _selectedPropIndex = i;
                        _activeBrush = MapBrushType.PlaceProp3D;
                        _statusMessage = $"Prêt à poser : {_availablePropNames[i]} [{_propPlacementType}] (Cliquez sur la carte)";
                    }
                    GUI.backgroundColor = Color.white;
                }
                GUILayout.EndScrollView();
            }

            GUILayout.EndVertical();

            if (_availablePropNames.Count > 0)
            {
                string activePropName = _availablePropNames[_selectedPropIndex];

                GUILayout.Space(6);
                GUILayout.Label($"<b>2. Configuration du Placement & Ancrage : <color=#00E5FF>{activePropName}</color></b>");
                GUILayout.BeginVertical(GUI.skin.box);

                GUILayout.BeginHorizontal();
                GUILayout.Label("Type d'Ancrage :", GUILayout.Width(130));
                var prevType = _propPlacementType;
                _propPlacementType = (PropPlacementType)GUILayout.Toolbar((int)_propPlacementType, Enum.GetNames(typeof(PropPlacementType)));
                if (_propPlacementType != prevType)
                {
                    if (_propPlacementType == PropPlacementType.Plafond)
                    {
                        _propIsWalkable = true;
                        _propCover = CoverType.None;
                        _propHeightOffset = 0f;
                    }
                    else if (_propPlacementType == PropPlacementType.Plancher)
                    {
                        _propIsWalkable = false;
                        _propCover = CoverType.Full;
                        _propHeightOffset = 0f;
                    }
                }
                GUILayout.EndHorizontal();

                if (_propPlacementType == PropPlacementType.Plafond)
                {
                    GUI.color = new Color(0.0f, 0.95f, 1.0f);
                    GUILayout.Label("<i>⚓ Ancrage sous plafond actif. Le plafond s'éclaire en semi-transparent. Pose requiert une dalle de plafond.</i>");
                    GUI.color = Color.white;
                }

                GUILayout.BeginHorizontal();
                GUILayout.Label($"Décalage Hauteur (Y) : {_propHeightOffset:0.##}m", GUILayout.Width(170));
                _propHeightOffset = GUILayout.HorizontalSlider(_propHeightOffset, -2.0f, 4.0f);
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label($"Orientation Y : {_propRotationY:0}°", GUILayout.Width(170));
                _propRotationY = GUILayout.HorizontalSlider(_propRotationY, 0f, 360f);
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("Pivoter (Hex 60°) :", GUILayout.Width(130));
                if (GUILayout.Button("0°")) _propRotationY = 0f;
                if (GUILayout.Button("60°")) _propRotationY = 60f;
                if (GUILayout.Button("120°")) _propRotationY = 120f;
                if (GUILayout.Button("180°")) _propRotationY = 180f;
                if (GUILayout.Button("240°")) _propRotationY = 240f;
                if (GUILayout.Button("300°")) _propRotationY = 300f;
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label($"Échelle : {_propScale:0.##}x", GUILayout.Width(170));
                _propScale = GUILayout.HorizontalSlider(_propScale, 0.2f, 3.0f);
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("Couverture conférée :", GUILayout.Width(130));
                _propCover = (CoverType)GUILayout.Toolbar((int)_propCover, Enum.GetNames(typeof(CoverType)));
                GUILayout.EndHorizontal();

                _propIsWalkable = GUILayout.Toggle(_propIsWalkable, "Praticable (Les unités peuvent traverser cette case)");

                GUILayout.Space(6);
                GUILayout.BeginHorizontal();

                bool isPlacing = (_activeBrush == MapBrushType.PlaceProp3D);
                GUI.backgroundColor = isPlacing ? new Color(0.2f, 0.85f, 0.4f) : Color.white;
                if (GUILayout.Button(isPlacing ? "🎯 MODE PLACEMENT ACTIF" : "Activer Pinceau de Pose", GUILayout.Height(32)))
                {
                    _activeBrush = MapBrushType.PlaceProp3D;
                }

                bool isErasing = (_activeBrush == MapBrushType.EraseProp3D);
                GUI.backgroundColor = isErasing ? new Color(0.9f, 0.25f, 0.25f) : Color.white;
                if (GUILayout.Button(isErasing ? "🗑️ MODE SUPPRESSION ACTIF" : "Gomme d'Objets 3D", GUILayout.Height(32)))
                {
                    _activeBrush = MapBrushType.EraseProp3D;
                }
                GUI.backgroundColor = Color.white;

                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }

            GUILayout.Space(6);
            GUILayout.Label($"<b>3. Objets 3D Déployés ({_placedPropRecords.Count}) :</b>");
            if (_placedPropRecords.Count == 0)
            {
                GUILayout.Label("<i>Aucun objet 3D posé pour l'instant.</i>");
            }
            else
            {
                GUILayout.BeginVertical(GUI.skin.box);
                foreach (var kvp in new Dictionary<string, MapPropData>(_placedPropRecords))
                {
                    string propKey = kvp.Key;
                    var data = kvp.Value;

                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"• <b>{data.PrefabName}</b> en ({data.Q}, {data.R}) [{data.PlacementType} | {data.Cover}]");
                    GUI.backgroundColor = Color.red;
                    if (GUILayout.Button("Suppr.", GUILayout.Width(60)))
                    {
                        RemovePropAt(new HexCoordinates(data.Q, data.R), data.PlacementType);
                    }
                    GUI.backgroundColor = Color.white;
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndVertical();
            }
        }

        private void DrawBrushOption(MapBrushType brush, string title, string description)
        {
            bool isSelected = (_activeBrush == brush);
            GUI.backgroundColor = isSelected ? new Color(0.1f, 0.7f, 1f) : Color.white;

            if (GUILayout.Button($"<b>{title}</b> — <color=#A0B0C0>{description}</color>", GUI.skin.button, GUILayout.Height(28)))
            {
                _activeBrush = brush;
                _statusMessage = $"Pinceau actif : {title}";
            }

            GUI.backgroundColor = Color.white;
        }

        private void DrawStorageTab()
        {
            GUILayout.Label("<b>Sauvegarde & Exportation de la Carte Active :</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Nom de la Carte :", GUILayout.Width(130));
            _mapNameInput = GUILayout.TextField(_mapNameInput);
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUI.backgroundColor = new Color(0.2f, 0.7f, 0.4f);
            if (GUILayout.Button("💾 Sauvegarder la Carte (Sol + Plafond + Objets 3D + Avatars)", GUILayout.Height(34)))
            {
                SaveCurrentMap(_mapNameInput);
            }
            GUI.backgroundColor = Color.white;

            GUILayout.EndVertical();

            GUILayout.Space(8);
            GUILayout.Label("<b>Cartes Enregistrées sur le Disque :</b>");

            var files = GetSavedMapFiles();
            if (files.Count == 0)
            {
                GUILayout.Label("<i>Aucune carte enregistrée dans le dossier Maps.</i>");
            }

            foreach (var file in files)
            {
                string fileName = Path.GetFileNameWithoutExtension(file);
                string summary = "";
                try
                {
                    string jsonPreview = File.ReadAllText(file);
                    var previewData = JsonUtility.FromJson<TacticalMapSaveData>(jsonPreview);
                    if (previewData != null)
                    {
                        int pCount = previewData.PlacedProps != null ? previewData.PlacedProps.Count : 0;
                        int uCount = previewData.PlacedUnits != null ? previewData.PlacedUnits.Count : 0;
                        int lxCount = previewData.PlacedPixies != null ? previewData.PlacedPixies.Count : 0;
                        summary = $"<color=#88AACC>[{uCount} PJ/PNJ | {pCount} Props | {lxCount} Lum.]</color>";
                    }
                }
                catch {}

                GUILayout.BeginHorizontal(GUI.skin.box);
                GUILayout.Label($"<b>{fileName}</b> {summary}", GUILayout.Width(280));

                GUI.backgroundColor = Color.cyan;
                if (GUILayout.Button("Charger", GUILayout.Width(75)))
                {
                    LoadMap(file);
                }

                GUI.backgroundColor = Color.red;
                if (GUILayout.Button("Suppr.", GUILayout.Width(60)))
                {
                    DeleteMap(file);
                }
                GUI.backgroundColor = Color.white;

                GUILayout.EndHorizontal();
            }
        }

        private void DrawGridSettingsTab()
        {
            GUILayout.Label("<b>Configuration de la Grille & Hauteur de Plafond :</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Rayon de la Grille (HexRadius) :", GUILayout.Width(210));
            _gridRadiusInput = GUILayout.TextField(_gridRadiusInput, GUILayout.Width(60));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Hauteur du Plafond : {_ceilingHeightInput:0.##}m", GUILayout.Width(210));
            _ceilingHeightInput = GUILayout.HorizontalSlider(_ceilingHeightInput, 2.0f, 8.0f);
            if (_grid != null && Mathf.Abs(_grid.CeilingHeight - _ceilingHeightInput) > 0.01f)
            {
                _grid.CeilingHeight = _ceilingHeightInput;
                _gridVisualizer?.RefreshObstacles();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            if (GUILayout.Button("📐 Régénérer la Grille avec ce Rayon", GUILayout.Height(32)))
            {
                if (int.TryParse(_gridRadiusInput, out int newRadius) && newRadius >= 3 && newRadius <= 25)
                {
                    RegenerateGridWithRadius(newRadius);
                }
                else
                {
                    _statusMessage = "Rayon invalide (doit être entre 3 et 25).";
                }
            }

            GUILayout.EndVertical();
        }

        private void RegenerateGridWithRadius(int radius)
        {
            if (_grid == null) return;

            ClearAllPlacedProps();
            _arena?.ClearLoadedMap();

            var radiusField = typeof(TacticalHexGrid).GetField("_gridRadius", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (radiusField != null)
            {
                radiusField.SetValue(_grid, radius);
            }

            _grid.CeilingHeight = _ceilingHeightInput;
            _grid.GenerateGrid();
            _gridVisualizer?.BuildVisualGrid();
            _statusMessage = $"Grille régénérée avec un rayon de {radius} cases et plafond à {_ceilingHeightInput:0.##}m.";
        }

        private void SaveCurrentMap(string mapName)
        {
            EnsureDirectoryExists();
            string safeName = string.Join("_", mapName.Split(Path.GetInvalidFileNameChars()));
            if (string.IsNullOrWhiteSpace(safeName)) safeName = "Map_Unnamed";

            SyncPropsFromScene();

            var data = new TacticalMapSaveData
            {
                MapName = safeName,
                GridRadius = _grid != null ? _grid.GridRadius : 8,
                CeilingHeight = _grid != null ? _grid.CeilingHeight : _ceilingHeightInput
            };

            if (_grid != null)
            {
                foreach (var kvp in _grid.Nodes)
                {
                    var node = kvp.Value;
                    if (node.Cover != CoverType.None || !node.IsWalkable || node.HasCeiling || Mathf.Abs(node.Elevation) > 0.001f || !string.IsNullOrEmpty(node.GroundTexture))
                    {
                        data.ModifiedTiles.Add(new MapTileData
                        {
                            Q = node.Coordinates.Q,
                            R = node.Coordinates.R,
                            Cover = node.Cover,
                            IsWalkable = node.IsWalkable,
                            HasCeiling = node.HasCeiling,
                            Elevation = node.Elevation,
                            GroundTexture = node.GroundTexture
                        });
                    }
                }
            }

            int ceilingPropsCount = 0;
            data.PlacedProps.Clear();
            foreach (var prop in _placedPropRecords.Values)
            {
                if (prop != null)
                {
                    data.PlacedProps.Add(prop);
                    if (prop.PlacementType == PropPlacementType.Plafond) ceilingPropsCount++;
                }
            }

            data.PlacedUnits.Clear();
            var sceneUnits = FindObjectsByType<TacticalUnit>();
            Array.Sort(sceneUnits, (a, b) => b.IsPlayerControlled.CompareTo(a.IsPlayerControlled));

            for (int i = 0; i < sceneUnits.Length; i++)
            {
                var u = sceneUnits[i];
                if (u == null || u.Stats == null) continue;

                var entry = new MapUnitData
                {
                    UnitId = u.gameObject.name,
                    Sheet = u.GetOrBuildSheet(),
                    Q = u.CurrentCoords.Q,
                    R = u.CurrentCoords.R,
                    IsPlayer = u.IsPlayerControlled,
                    currentHealth = u.Stats.CurrentHealth,
                    currentAP = u.Stats.CurrentActionPoints,
                    essoufflement = u.Stats.Essoufflement,
                    activeStatus = (int)u.Stats.ActiveStatus
                };

                if (u.Stats.ActiveStatus != StatusEffect.None)
                {
                    entry.statusEffects.Add(u.Stats.ActiveStatus.ToString());
                }

                data.PlacedUnits.Add(entry);
            }

            data.PlacedPixies.Clear();
            var activePixies = FindObjectsByType<Killtime.Tactics.Lighting.TacticalPixieLight>();
            for (int i = 0; i < activePixies.Length; i++)
            {
                if (activePixies[i] != null)
                {
                    data.PlacedPixies.Add(activePixies[i].transform.position);
                }
            }

            data.SaveTimestamp = DateTime.Now.ToString("dd/MM HH:mm");

            string fullPath = Path.Combine(MapsDirectory, $"{safeName}.json");
            string json = JsonUtility.ToJson(data, true);
            File.WriteAllText(fullPath, json);

            if (_arena != null)
            {
                _arena.RegisterLoadedMap(data, fullPath);
            }

            _statusMessage = $"Carte '{safeName}' sauvegardée ({data.ModifiedTiles.Count} tuiles, {data.PlacedProps.Count} objets 3D, {data.PlacedUnits.Count} avatar(s), {data.PlacedPixies.Count} pixie(s)).";
        }

        public void ApplyLoadedMap(TacticalMapSaveData data, string mapPath = null)
        {
            if (data == null || _grid == null) return;

            EnsureReferences();
            ClearAllPlacedProps();
            _grid.ResetGridState();

            if (data.CeilingHeight > 0)
            {
                _grid.CeilingHeight = data.CeilingHeight;
                _ceilingHeightInput = data.CeilingHeight;
            }

            if (_arena != null)
            {
                _arena.ClearAllUnits();
            }
            else
            {
                var existingUnits = FindObjectsByType<TacticalUnit>();
                for (int i = 0; i < existingUnits.Length; i++)
                {
                    if (existingUnits[i] != null)
                    {
                        var node = _grid.GetNode(existingUnits[i].CurrentCoords);
                        if (node != null) node.IsOccupied = false;
                        Destroy(existingUnits[i].gameObject);
                    }
                }
                _turnManager?.ClearUnits();
                _turnManager?.ResetCombatState();
            }

            for (int i = 0; i < data.ModifiedTiles.Count; i++)
            {
                var tile = data.ModifiedTiles[i];
                var coords = new HexCoordinates(tile.Q, tile.R);
                _grid.SetNodeCover(coords, tile.Cover, tile.IsWalkable);
                _grid.SetNodeCeiling(coords, tile.HasCeiling);
                _grid.SetNodeElevation(coords, tile.Elevation);
                _grid.SetNodeGroundTexture(coords, tile.GroundTexture);
            }

            EnsurePropsRoot();
            int loadedCount = 0;
            int ceilingLoadedCount = 0;

            for (int i = 0; i < data.PlacedProps.Count; i++)
            {
                var propData = data.PlacedProps[i];
                var coords = new HexCoordinates(propData.Q, propData.R);
                var node = _grid.GetNode(coords);

                if (node != null)
                {
                    node.HasCustomVisual = true;
                    if (propData.PlacementType != PropPlacementType.Plafond || !propData.IsWalkable)
                    {
                        node.Cover = propData.Cover;
                        node.IsWalkable = propData.IsWalkable;
                    }

                    if (propData.PlacementType == PropPlacementType.Plafond && !node.HasCeiling)
                    {
                        node.HasCeiling = true;
                        _grid.SetNodeCeiling(coords, true);
                    }

                    var prefab = LoadPropPrefab(propData.PrefabName);
                    if (prefab != null)
                    {
                        string propKey = GetPropKey(coords, propData.PlacementType);

#if UNITY_EDITOR
                        var instance = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, _propsRoot);
#else
                        var instance = Instantiate(prefab, _propsRoot);
#endif
                        instance.name = $"Prop_{propData.PrefabName}_{coords.Q}_{coords.R}_{propData.PlacementType}";

                        Vector3 propPos = node.WorldPosition;
                        switch (propData.PlacementType)
                        {
                            case PropPlacementType.Plancher:
                                propPos.y += propData.HeightOffset;
                                break;
                            case PropPlacementType.Plafond:
                                propPos.y += _grid.CeilingHeight - propData.HeightOffset;
                                break;
                            case PropPlacementType.Mur:
                                propPos.y += (_grid.CeilingHeight * 0.4f) + propData.HeightOffset;
                                break;
                            case PropPlacementType.Flottant:
                                propPos.y += 1.8f + propData.HeightOffset;
                                break;
                        }

                        instance.transform.position = propPos;
                        instance.transform.rotation = Quaternion.Euler(0f, propData.RotationY, 0f);
                        instance.transform.localScale = Vector3.Scale(prefab.transform.localScale, Vector3.one * propData.Scale);

                        var propInstComp = instance.GetComponent<MapPropInstance>() ?? instance.AddComponent<MapPropInstance>();
                        propInstComp.Data = propData;

                        if (propData.PlacementType == PropPlacementType.Plafond)
                        {
                            var ceilingProp = instance.GetComponent<TacticalCeilingProp>() ?? instance.AddComponent<TacticalCeilingProp>();
                            ceilingProp.Initialize();
                            ceilingLoadedCount++;
                        }

                        _spawnedPropInstances[propKey] = instance;
                        _placedPropRecords[propKey] = propData;
                        loadedCount++;
                    }
                    else
                    {
                        Debug.LogWarning($"[MapEditor] Modèle introuvable pour '{propData.PrefabName}' lors du chargement de la carte.");
                    }
                }
            }

            int unitLoadedCount = 0;
            if (data.PlacedUnits != null && data.PlacedUnits.Count > 0)
            {
                if (_arena != null)
                {
                    _arena.LoadUnitsFromMap(data.PlacedUnits);
                }
                else
                {
                    for (int i = 0; i < data.PlacedUnits.Count; i++)
                    {
                        var uData = data.PlacedUnits[i];
                        if (uData == null || uData.Sheet == null) continue;

                        var uCoords = new HexCoordinates(uData.Q, uData.R);
                        var go = new GameObject($"Unit_{uData.Sheet.Name.Replace(" ", "_")}");
                        var unit = go.AddComponent<TacticalUnit>();
                        unit.InitializeFromSheet(uData.Sheet, uCoords, _grid, uData.IsPlayer);
                        _turnManager?.RegisterUnit(unit);
                    }

                    _turnManager?.StartNewRound();
                }

                unitLoadedCount = data.PlacedUnits.Count;
            }

            var strayPixies = FindObjectsByType<Killtime.Tactics.Lighting.TacticalPixieLight>();
            for (int i = 0; i < strayPixies.Length; i++)
            {
                if (strayPixies[i] != null) Destroy(strayPixies[i].gameObject);
            }

            int loadedPixiesCount = 0;
            if (data.PlacedPixies != null)
            {
                for (int i = 0; i < data.PlacedPixies.Count; i++)
                {
                    var p = Killtime.Tactics.Lighting.TacticalPixieLight.SpawnPixie();
                    p.transform.position = data.PlacedPixies[i];
                    loadedPixiesCount++;
                }
            }

            _gridVisualizer?.RefreshObstacles();
            // Registre ligne de mire APRÈS les visuels : bornes des meshes affichés.
            try { PropObstacleRegistry.Rebuild(_grid != null ? _grid.HexRadius : 1f); } catch { /* ignore */ }
            _statusMessage = $"Carte '{data.MapName}' chargée ({loadedCount} objet(s) 3D, {unitLoadedCount} avatar(s), {loadedPixiesCount} pixie(s), Plafond: {_grid.CeilingHeight:0.##}m).";

            if (_arena != null)
            {
                _arena.RegisterLoadedMap(data, mapPath);
            }
        }

        private void LoadMap(string fullPath)
        {
            if (!File.Exists(fullPath) || _grid == null) return;

            string json = File.ReadAllText(fullPath);
            var data = JsonUtility.FromJson<TacticalMapSaveData>(json);

            ApplyLoadedMap(data, fullPath);
        }

        private void DeleteMap(string fullPath)
        {
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                _statusMessage = "Fichier de carte supprimé.";
            }
        }

        private static void EnsureDirectoryExists()
        {
            if (!Directory.Exists(MapsDirectory))
            {
                Directory.CreateDirectory(MapsDirectory);
            }
        }

        private static List<string> GetSavedMapFiles()
        {
            EnsureDirectoryExists();
            return new List<string>(Directory.GetFiles(MapsDirectory, "*.json"));
        }

        private void DrawGroundAndSculptTab()
        {
            GUILayout.Label("<b>1. Textures de Sol (Assets/Resources/Grounds) :</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Textures détectées : <b>{_availableGroundTextures.Count}</b>");
            if (GUILayout.Button("🔄 Scanner Grounds", GUILayout.Width(125)))
            {
                RefreshAvailableGroundTextures();
                _statusMessage = $"{_availableGroundTextures.Count} texture(s) trouvée(s) sous Assets/Resources/Grounds.";
            }
            if (GUILayout.Button("🛡️ Anti-Grésillement (Mipmaps)", GUILayout.Width(180)))
            {
                if (Application.isPlaying)
                {
                    _statusMessage = "Arrêtez le mode Play dans Unity pour générer les Mipmaps sur disque.";
                }
                else
                {
#if UNITY_EDITOR
                    HexGridVisualizer.BatchFixAllGroundTextureAssets();
                    RefreshAvailableGroundTextures();
                    _gridVisualizer?.RefreshObstacles();
                    _statusMessage = "Mipmaps et Anisotropie 16x appliqués sur le disque.";
#else
                    _statusMessage = "Cette commande de réimportation est réservée à l'Éditeur Unity.";
#endif
                }
            }
            GUILayout.EndHorizontal();

            if (_groundCategories.Count > 1)
            {
                GUILayout.Space(4);
                _selectedCategoryIndex = GUILayout.Toolbar(_selectedCategoryIndex, _groundCategories.ToArray());
            }

            string activeCat = (_selectedCategoryIndex >= 0 && _selectedCategoryIndex < _groundCategories.Count) ? _groundCategories[_selectedCategoryIndex] : "Tous";

            List<GroundTextureRecord> filteredTextures = new();
            for (int i = 0; i < _availableGroundTextures.Count; i++)
            {
                if (activeCat == "Tous" || _availableGroundTextures[i].Category.Equals(activeCat, StringComparison.OrdinalIgnoreCase))
                {
                    filteredTextures.Add(_availableGroundTextures[i]);
                }
            }

            if (filteredTextures.Count == 0)
            {
                GUILayout.Label("<i>Aucune texture trouvée dans cette catégorie.</i>");
            }
            else
            {
                GUILayout.Space(6);
                _groundTexturesScrollPos = GUILayout.BeginScrollView(_groundTexturesScrollPos, GUILayout.Height(135));

                int cols = 4;
                for (int i = 0; i < filteredTextures.Count; i += cols)
                {
                    GUILayout.BeginHorizontal();
                    for (int c = 0; c < cols; c++)
                    {
                        int index = i + c;
                        if (index < filteredTextures.Count)
                        {
                            var rec = filteredTextures[index];
                            bool isSelected = (_availableGroundTextures.Count > _selectedGroundIndex && _availableGroundTextures[_selectedGroundIndex].RelativePath == rec.RelativePath);

                            GUI.backgroundColor = isSelected ? new Color(0.0f, 0.85f, 1.0f) : Color.white;
                            GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(110), GUILayout.Height(105));

                            if (rec.Texture != null)
                            {
                                Rect previewRect = GUILayoutUtility.GetRect(100, 70);
                                GUI.DrawTexture(previewRect, rec.Texture, ScaleMode.ScaleToFit);
                            }

                            if (GUILayout.Button(rec.DisplayName, GUILayout.Height(22)))
                            {
                                _selectedGroundIndex = _availableGroundTextures.IndexOf(rec);
                                _activeBrush = MapBrushType.PaintGroundTexture;
                                _statusMessage = $"Pinceau Texture Actif : {rec.RelativePath}";
                            }
                            GUILayout.EndVertical();
                            GUI.backgroundColor = Color.white;
                        }
                    }
                    GUILayout.EndHorizontal();
                }

                GUILayout.EndScrollView();
            }

            if (_availableGroundTextures.Count > 0 && _selectedGroundIndex < _availableGroundTextures.Count)
            {
                var activeRec = _availableGroundTextures[_selectedGroundIndex];
                GUILayout.Space(6);
                GUILayout.Label($"Texture Active : <b><color=#00E5FF>{activeRec.RelativePath}</color></b>");

                GUILayout.BeginHorizontal();
                bool isPainting = (_activeBrush == MapBrushType.PaintGroundTexture);
                GUI.backgroundColor = isPainting ? new Color(0.1f, 0.7f, 1f) : Color.white;
                if (GUILayout.Button("🖌️ Peindre cette Texture (Clic Carte)", GUILayout.Height(30)))
                {
                    _activeBrush = MapBrushType.PaintGroundTexture;
                }

                bool isClearing = (_activeBrush == MapBrushType.ClearGroundTexture);
                GUI.backgroundColor = isClearing ? new Color(0.9f, 0.3f, 0.3f) : Color.white;
                if (GUILayout.Button("🧹 Gomme Texture", GUILayout.Height(30)))
                {
                    _activeBrush = MapBrushType.ClearGroundTexture;
                }
                GUI.backgroundColor = Color.white;
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("🪣 Remplir Toute la Grille"))
                {
                    if (_grid != null)
                    {
                        foreach (var node in _grid.Nodes.Values)
                        {
                            node.GroundTexture = activeRec.RelativePath;
                        }
                        _gridVisualizer?.RefreshObstacles();
                        _statusMessage = $"Texture '{activeRec.RelativePath}' appliquée à l'ensemble de la grille.";
                    }
                }
                if (GUILayout.Button("🧹 Effacer Toutes les Textures"))
                {
                    if (_grid != null)
                    {
                        foreach (var node in _grid.Nodes.Values)
                        {
                            node.GroundTexture = "";
                        }
                        _gridVisualizer?.RefreshObstacles();
                        _statusMessage = "Toutes les textures ont été retirées.";
                    }
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Tiling UV Texture : {_groundTiling:0.##}x", GUILayout.Width(170));
            float newTiling = GUILayout.HorizontalSlider(_groundTiling, 0.25f, 4.0f);
            if (Mathf.Abs(newTiling - _groundTiling) > 0.01f)
            {
                _groundTiling = newTiling;
                if (_gridVisualizer != null) _gridVisualizer.GroundTiling = _groundTiling;
            }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();

            GUILayout.Space(8);
            GUILayout.Label("<b>2. Sculpture du Relief (Altitude & Tranchées) :</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.BeginHorizontal();
            DrawBrushOption(MapBrushType.ElevateGround, "▲ Surélever (+)", $"+{_sculptStep:0.##}m par clic");
            DrawBrushOption(MapBrushType.LowerGround, "▼ Creuser (-)", $"-{_sculptStep:0.##}m par clic");
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            DrawBrushOption(MapBrushType.SetElevation, "▬ Niveau Fixe", $"Hauteur = {_targetElevation:0.##}m");
            DrawBrushOption(MapBrushType.SmoothElevation, "≈ Lisser Relief", "Moyenne avec voisins");
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Pas d'Élévation : <b>{_sculptStep:0.##}m</b>", GUILayout.Width(170));
            _sculptStep = GUILayout.HorizontalSlider(_sculptStep, 0.10f, 1.50f);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Hauteur Fixe Cible : <b>{_targetElevation:0.##}m</b>", GUILayout.Width(170));
            _targetElevation = GUILayout.HorizontalSlider(_targetElevation, -3.0f, 6.0f);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Rayon Pinceau : <b>{_sculptRadius} case(s)</b>", GUILayout.Width(170));
            _sculptRadius = (int)GUILayout.HorizontalSlider(_sculptRadius, 1, 4);
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            bool newSmooth = GUILayout.Toggle(_smoothSlopeTerrain, "🌊 Mode Pentes Lisses Continues (Sinon Marches / Terrasses)");
            if (newSmooth != _smoothSlopeTerrain)
            {
                _smoothSlopeTerrain = newSmooth;
                if (_gridVisualizer != null) _gridVisualizer.SmoothSlopeTerrain = _smoothSlopeTerrain;
            }

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Aplanir Tout le Sol (Y = 0)"))
            {
                if (_grid != null)
                {
                    foreach (var node in _grid.Nodes.Values)
                    {
                        node.Elevation = 0f;
                        node.WorldPosition = node.Coordinates.ToWorldPosition(_grid.HexRadius, 0.05f);
                    }
                    _gridVisualizer?.RefreshObstacles();
                    _statusMessage = "Tout le terrain a été aplani à Y = 0.";
                }
            }

            if (GUILayout.Button("🌋 Cratère Central"))
            {
                GenerateTerrainPreset(isCrater: true);
            }

            if (GUILayout.Button("⛰️ Colline Centrale"))
            {
                GenerateTerrainPreset(isCrater: false);
            }
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }

        private void GenerateTerrainPreset(bool isCrater)
        {
            if (_grid == null) return;
            float maxOffset = isCrater ? -1.8f : 2.2f;
            int maxRadius = _grid.GridRadius;

            foreach (var node in _grid.Nodes.Values)
            {
                int dist = node.Coordinates.DistanceTo(new HexCoordinates(0, 0));
                float t = Mathf.Clamp01(1f - ((float)dist / Mathf.Max(1, maxRadius)));
                node.Elevation = Mathf.SmoothStep(0f, maxOffset, t);
                node.WorldPosition = node.Coordinates.ToWorldPosition(_grid.HexRadius, 0.05f + node.Elevation);
            }

            _gridVisualizer?.RefreshObstacles();
            _statusMessage = isCrater ? "Cratère central généré." : "Colline centrale générée.";
        }
    }
}
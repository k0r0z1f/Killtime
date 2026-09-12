using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics;
using Killtime.Tactics.Grid;

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
        EraseProp3D
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
    public class TacticalMapSaveData
    {
        public string MapName = "Nouvelle_Carte";
        public int GridRadius = 8;
        public float CeilingHeight = 3.5f;
        public List<MapTileData> ModifiedTiles = new();
        public List<MapPropData> PlacedProps = new();
    }

    /// <summary>
    /// Éditeur de carte tactique avec placement dynamique d'obstacles procéduraux,
    /// gestion des dalles de plafond et d'objets 3D personnalisés configurables (Sol, Plafond, Mur, Flottant).
    /// </summary>
    public class MapEditorDevWindow : MonoBehaviour
    {
        public static MapEditorDevWindow Instance { get; private set; }

        [Header("Systèmes")]
        [SerializeField] private TacticalHexGrid _grid;
        [SerializeField] private HexGridVisualizer _gridVisualizer;
        [SerializeField] private CombatDevArena _arena;

        [Header("Affichage")]
        [SerializeField] private bool _isOpen = false;
        [SerializeField] private KeyCode _toggleKey = KeyCode.F2;

        private MapBrushType _activeBrush = MapBrushType.HalfCover;
        private string _mapNameInput = "Killzone_Alpha";
        private string _statusMessage = "Prêt.";
        private string _gridRadiusInput = "8";
        private float _ceilingHeightInput = 3.5f;

        private Rect _windowRect = new Rect(20, 60, 540, 680);
        private Vector2 _scrollPos;
        private Vector2 _propsScrollPos;
        private int _selectedTab = 0;
        private readonly string[] _tabTitles = { "🖌️ Sol & Plafond", "🏺 Objets 3D", "💾 Cartes", "📐 Grille" };

        private HexNode _inspectedNode;

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

        public static void Open()
        {
            if (Instance == null)
            {
                Instance = FindAnyObjectByType<MapEditorDevWindow>();
                if (Instance == null)
                {
                    var go = new GameObject("[UI] MapEditorDevWindow");
                    Instance = go.AddComponent<MapEditorDevWindow>();
                }
            }

            Instance._isOpen = true;
            Instance.EnsureReferences();
        }

        private void Awake()
        {
            if (Instance == null) Instance = this;
            EnsureReferences();
            EnsureDirectoryExists();
            RefreshAvailableProps();
            EnsurePropsRoot();
            SyncPropsFromScene();
            if (_grid != null) _ceilingHeightInput = _grid.CeilingHeight;
        }

        private void OnEnable()
        {
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

        private void OnDisable()
        {
            _gridVisualizer?.SetCeilingVisualMode(CeilingVisualMode.InGame);
        }

        private void OnDestroy()
        {
            _gridVisualizer?.SetCeilingVisualMode(CeilingVisualMode.InGame);
        }

        private void EnsureReferences()
        {
            if (_grid == null) _grid = FindAnyObjectByType<TacticalHexGrid>();
            if (_gridVisualizer == null) _gridVisualizer = FindAnyObjectByType<HexGridVisualizer>();
            if (_arena == null) _arena = FindAnyObjectByType<CombatDevArena>();
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

        public void CloseWindow()
        {
            _isOpen = false;
            _gridVisualizer?.SetCeilingVisualMode(CeilingVisualMode.InGame);
        }

        private void Update()
        {
            if (Input.GetKeyDown(_toggleKey))
            {
                if (_isOpen)
                {
                    CloseWindow();
                }
                else
                {
                    _isOpen = true;
                    EnsureReferences();
                    EnsurePropsRoot();
                    if (_availablePropNames.Count == 0) RefreshAvailableProps();
                }
            }

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

            Vector2 mouseScreen = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            if (_windowRect.Contains(mouseScreen))
            {
                _gridVisualizer?.SetHoveredCoord(null, false);
                return;
            }

            bool isCeilingTargeting = (_activeBrush == MapBrushType.AddCeiling) ||
                                      (_activeBrush == MapBrushType.RemoveCeiling) ||
                                      (_selectedTab == 1 && _propPlacementType == PropPlacementType.Plafond) ||
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
                        _statusMessage = $"Hex ({node.Coordinates.Q}, {node.Coordinates.R}) | Couverture: {node.Cover} | Praticable: {node.IsWalkable}{ceilingInfo}{propInfo}";
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
                        ApplyTerrainBrush(node, _activeBrush);
                    }
                }
                else if (Input.GetMouseButton(0))
                {
                    if (_activeBrush != MapBrushType.Inspect && _activeBrush != MapBrushType.PlaceProp3D && _activeBrush != MapBrushType.EraseProp3D)
                    {
                        ApplyTerrainBrush(node, _activeBrush);
                    }
                }
            }
            else
            {
                _gridVisualizer?.SetHoveredCoord(null, false);
            }
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
            }

            if (modified && _gridVisualizer != null)
            {
                _gridVisualizer.RefreshObstacles();
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
            _spawnedPropInstances.Clear();
            _placedPropRecords.Clear();
        }

        private void OnGUI()
        {
            if (!_isOpen) return;

            _windowRect.height = Mathf.Min(720, Screen.height - 60);
            _windowRect = GUI.Window(890, _windowRect, DrawWindowContent, "🗺️ Killtime — Créateur & Éditeur de Cartes 3D");
            GUI.BringWindowToFront(890);
        }

        private void DrawWindowContent(int windowId)
        {
            GUI.DragWindow(new Rect(0, 0, _windowRect.width - 65, 25));
            if (GUI.Button(new Rect(_windowRect.width - 60, 4, 55, 20), "Fermer"))
            {
                CloseWindow();
            }

            GUILayout.Space(6);
            int prevTab = _selectedTab;
            _selectedTab = GUILayout.Toolbar(_selectedTab, _tabTitles);
            if (_selectedTab != prevTab && _selectedTab == 1)
            {
                _activeBrush = MapBrushType.PlaceProp3D;
                _statusMessage = "Mode Objets 3D actif : cliquez sur la carte pour poser l'objet sélectionné.";
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
                case 1: DrawProps3DTab(); break;
                case 2: DrawStorageTab(); break;
                case 3: DrawGridSettingsTab(); break;
            }

            GUILayout.EndScrollView();
        }

        private void DrawTerrainTab()
        {
            GUILayout.Label("<b>1. Pinceaux de Sol & Couvertures Géométriques :</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            DrawBrushOption(MapBrushType.Inspect, "🔍 Inspecter", "Affiche l'état complet (sol, couverture, plafond, objets).");
            DrawBrushOption(MapBrushType.ClearGround, "⬛ Sol Dégagé", "Restaure un sol plat et praticable.");
            DrawBrushOption(MapBrushType.HalfCover, "📦 Demi-Couverture", "Pose un bloc de demi-couverture standard (+1 défense).");
            DrawBrushOption(MapBrushType.FullCover, "🏛️ Couverture Totale", "Pose un pilier bloquant vue et déplacement.");
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
            if (GUILayout.Button("💾 Sauvegarder la Carte (Sol + Plafond + Objets 3D)", GUILayout.Height(34)))
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
                GUILayout.BeginHorizontal(GUI.skin.box);
                GUILayout.Label(fileName, GUILayout.Width(220));

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
                    if (node.Cover != CoverType.None || !node.IsWalkable || node.HasCeiling)
                    {
                        data.ModifiedTiles.Add(new MapTileData
                        {
                            Q = node.Coordinates.Q,
                            R = node.Coordinates.R,
                            Cover = node.Cover,
                            IsWalkable = node.IsWalkable,
                            HasCeiling = node.HasCeiling
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

            string fullPath = Path.Combine(MapsDirectory, $"{safeName}.json");
            string json = JsonUtility.ToJson(data, true);
            File.WriteAllText(fullPath, json);

            _statusMessage = $"Carte '{safeName}' sauvegardée ({data.ModifiedTiles.Count} tuiles, {data.PlacedProps.Count} objets 3D dont {ceilingPropsCount} au plafond).";
        }

        private void LoadMap(string fullPath)
        {
            if (!File.Exists(fullPath) || _grid == null) return;

            string json = File.ReadAllText(fullPath);
            var data = JsonUtility.FromJson<TacticalMapSaveData>(json);

            ClearAllPlacedProps();
            _grid.ClearAllCovers();
            _grid.SetAllCeilings(false);

            if (data.CeilingHeight > 0)
            {
                _grid.CeilingHeight = data.CeilingHeight;
                _ceilingHeightInput = data.CeilingHeight;
            }

            for (int i = 0; i < data.ModifiedTiles.Count; i++)
            {
                var tile = data.ModifiedTiles[i];
                var coords = new HexCoordinates(tile.Q, tile.R);
                _grid.SetNodeCover(coords, tile.Cover, tile.IsWalkable);
                _grid.SetNodeCeiling(coords, tile.HasCeiling);
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

            _gridVisualizer?.RefreshObstacles();
            _statusMessage = $"Carte '{data.MapName}' chargée ({loadedCount}/{data.PlacedProps.Count} objet(s) 3D dont {ceilingLoadedCount} au plafond, Plafond: {_grid.CeilingHeight:0.##}m).";
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
    }
}
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
        PlaceProp3D,
        EraseProp3D
    }

    [Serializable]
    public class MapTileData
    {
        public int Q;
        public int R;
        public CoverType Cover;
        public bool IsWalkable;
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
    }

    [Serializable]
    public class TacticalMapSaveData
    {
        public string MapName = "Nouvelle_Carte";
        public int GridRadius = 8;
        public List<MapTileData> ModifiedTiles = new();
        public List<MapPropData> PlacedProps = new();
    }

    /// <summary>
    /// Éditeur de carte tactique avec placement dynamique d'obstacles procéduraux
    /// et d'objets 3D personnalisés issus du dossier Resources/Objects.
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

        private Rect _windowRect = new Rect(20, 60, 520, 640);
        private Vector2 _scrollPos;
        private Vector2 _propsScrollPos;
        private int _selectedTab = 0;
        private readonly string[] _tabTitles = { "🖌️ Sol", "🏺 Objets 3D", "💾 Cartes", "📐 Grille" };

        private HexNode _inspectedNode;

        // --- Gestionnaire d'Objets 3D ---
        private readonly List<string> _availablePropNames = new();
        private int _selectedPropIndex = 0;
        private float _propRotationY = 0f;
        private float _propScale = 1.0f;
        private CoverType _propCover = CoverType.Full;
        private bool _propIsWalkable = false;

        private Transform _propsRoot;
        private readonly Dictionary<HexCoordinates, GameObject> _spawnedPropInstances = new();
        private readonly Dictionary<HexCoordinates, MapPropData> _placedPropRecords = new();

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
#if UNITY_EDITOR
            string[] guids = UnityEditor.AssetDatabase.FindAssets($"{prefabName} t:Prefab", new[] { "Assets" });
            foreach (string guid in guids)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) &&
                    path.IndexOf($"/Resources/Objects/{prefabName}.prefab", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var loaded = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (loaded != null) return loaded;
                }
            }
#endif

            var prefab = Resources.Load<GameObject>($"Objects/{prefabName}")
                      ?? Resources.Load<GameObject>($"objects/{prefabName}")
                      ?? Resources.Load<GameObject>(prefabName);

            return prefab;
        }

        private void Update()
        {
            if (Input.GetKeyDown(_toggleKey))
            {
                _isOpen = !_isOpen;
                if (_isOpen)
                {
                    EnsureReferences();
                    EnsurePropsRoot();
                    if (_availablePropNames.Count == 0) RefreshAvailableProps();
                }
            }

            if (_isOpen)
            {
                HandlePaintingInput();
            }
        }

        private void HandlePaintingInput()
        {
            if (UnityEngine.Camera.main == null || _grid == null) return;

            Vector2 mouseScreen = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            if (_windowRect.Contains(mouseScreen)) return;

            Ray ray = UnityEngine.Camera.main.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out RaycastHit hit)) return;

            if (!_grid.TryGetNodeAtWorldPosition(hit.point, out var node)) return;

            if (Input.GetMouseButtonDown(0))
            {
                if (_activeBrush == MapBrushType.Inspect)
                {
                    _inspectedNode = node;
                    bool hasProp = _placedPropRecords.TryGetValue(node.Coordinates, out var propData);
                    string propInfo = hasProp ? $" | Modèle: {propData.PrefabName} (Rot: {propData.RotationY}°)" : "";
                    _statusMessage = $"Hex ({node.Coordinates.Q}, {node.Coordinates.R}) | Couverture: {node.Cover} | Praticable: {node.IsWalkable}{propInfo}";
                }
                else if (_activeBrush == MapBrushType.PlaceProp3D)
                {
                    PlaceCurrent3DProp(node);
                }
                else if (_activeBrush == MapBrushType.EraseProp3D)
                {
                    RemovePropAt(node.Coordinates);
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
                    RemovePropAt(node.Coordinates, false);
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

            string prefabName = _availablePropNames[_selectedPropIndex];
            var prefab = LoadPropPrefab(prefabName);

            if (prefab == null)
            {
                _statusMessage = $"Échec de chargement du modèle Resources/Objects/{prefabName}.";
                return;
            }

            EnsurePropsRoot();
            RemovePropAt(node.Coordinates, false);

            #if UNITY_EDITOR
            var instance = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, _propsRoot);
#else
            var instance = Instantiate(prefab, _propsRoot);
#endif
            instance.name = $"Prop_{prefabName}_{node.Coordinates.Q}_{node.Coordinates.R}";
            instance.transform.position = node.WorldPosition;
            instance.transform.rotation = Quaternion.Euler(0f, _propRotationY, 0f);
            instance.transform.localScale = Vector3.Scale(prefab.transform.localScale, Vector3.one * _propScale);

            _spawnedPropInstances[node.Coordinates] = instance;

            var record = new MapPropData
            {
                PrefabName = prefabName,
                Q = node.Coordinates.Q,
                R = node.Coordinates.R,
                RotationY = _propRotationY,
                Scale = _propScale,
                Cover = _propCover,
                IsWalkable = _propIsWalkable
            };
            _placedPropRecords[node.Coordinates] = record;

            node.Cover = _propCover;
            node.IsWalkable = _propIsWalkable;
            node.HasCustomVisual = true;

            _gridVisualizer?.RefreshObstacles();

            _statusMessage = $"Modèle '{prefabName}' posé en ({node.Coordinates.Q}, {node.Coordinates.R}).";
        }

        private void RemovePropAt(HexCoordinates coords, bool restoreNode = true)
        {
            if (_spawnedPropInstances.TryGetValue(coords, out var instance))
            {
                if (instance != null) Destroy(instance);
                _spawnedPropInstances.Remove(coords);
            }

            if (_placedPropRecords.ContainsKey(coords))
            {
                _placedPropRecords.Remove(coords);
                if (restoreNode && _grid != null)
                {
                    var node = _grid.GetNode(coords);
                    if (node != null)
                    {
                        node.Cover = CoverType.None;
                        node.IsWalkable = true;
                        node.HasCustomVisual = false;
                    }
                    _gridVisualizer?.RefreshObstacles();
                    _statusMessage = $"Objet retiré en ({coords.Q}, {coords.R}).";
                }
            }
        }

        private void ClearAllPlacedProps()
        {
            foreach (var kvp in _spawnedPropInstances)
            {
                if (kvp.Value != null) Destroy(kvp.Value);
            }
            _spawnedPropInstances.Clear();
            _placedPropRecords.Clear();
        }

        private void OnGUI()
        {
            if (!_isOpen) return;

            _windowRect.height = Mathf.Min(680, Screen.height - 80);
            _windowRect = GUI.Window(890, _windowRect, DrawWindowContent, "🗺️ Killtime — Créateur & Éditeur de Cartes 3D");
            GUI.BringWindowToFront(890);
        }

        private void DrawWindowContent(int windowId)
        {
            GUI.DragWindow(new Rect(0, 0, _windowRect.width - 65, 25));
            if (GUI.Button(new Rect(_windowRect.width - 60, 4, 55, 20), "Fermer"))
            {
                _isOpen = false;
            }

            GUILayout.Space(6);
            _selectedTab = GUILayout.Toolbar(_selectedTab, _tabTitles);
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
            GUILayout.Label("<b>1. Pinceaux de Sol & Obstacles Géométriques :</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            DrawBrushOption(MapBrushType.Inspect, "🔍 Inspecter", "Cliquez sur une case pour afficher ses données.");
            DrawBrushOption(MapBrushType.ClearGround, "⬛ Sol Dégagé", "Restaure un sol plat et praticable sans obstacle.");
            DrawBrushOption(MapBrushType.HalfCover, "📦 Demi-Couverture", "Pose un bloc de demi-couverture standard (+1 défense).");
            DrawBrushOption(MapBrushType.FullCover, "🏛️ Couverture Totale", "Pose un pilier bloquant totalement la vue et le passage.");
            DrawBrushOption(MapBrushType.ImpassableHole, "🕳️ Gouffre / Vide", "Supprime le passage au sol.");

            GUILayout.EndVertical();

            GUILayout.Space(8);
            GUILayout.Label("<b>2. Actions Globales :</b>");
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
                GUILayout.Label("<b>3. Données de la Case Inspectée :</b>");
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label($"Position : <b>({_inspectedNode.Coordinates.Q}, {_inspectedNode.Coordinates.R})</b>");
                GUILayout.Label($"Couverture : {_inspectedNode.Cover}");
                GUILayout.Label($"Praticable : {_inspectedNode.IsWalkable}");
                GUILayout.Label($"Occupé : {_inspectedNode.IsOccupied}");
                if (_placedPropRecords.TryGetValue(_inspectedNode.Coordinates, out var prop))
                {
                    GUILayout.Label($"Objet 3D : <color=#00E5FF>{prop.PrefabName}</color> (Rot: {prop.RotationY:0}°, Échelle: {prop.Scale:0.##})");
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
                _propsScrollPos = GUILayout.BeginScrollView(_propsScrollPos, GUILayout.Height(130));
                for (int i = 0; i < _availablePropNames.Count; i++)
                {
                    bool isSelected = (_selectedPropIndex == i);
                    GUI.backgroundColor = isSelected ? new Color(0.1f, 0.7f, 1f) : Color.white;
                    if (GUILayout.Button($"📦 {_availablePropNames[i]}"))
                    {
                        _selectedPropIndex = i;
                        _activeBrush = MapBrushType.PlaceProp3D;
                        _statusMessage = $"Prêt à poser : {_availablePropNames[i]} (Cliquez sur la carte)";
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
                GUILayout.Label($"<b>2. Configuration du Placement : <color=#00E5FF>{activePropName}</color></b>");
                GUILayout.BeginVertical(GUI.skin.box);

                GUILayout.BeginHorizontal();
                GUILayout.Label($"Orientation Y : {_propRotationY:0}°", GUILayout.Width(140));
                _propRotationY = GUILayout.HorizontalSlider(_propRotationY, 0f, 360f);
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("Pivoter (Hex 60°) :", GUILayout.Width(140));
                if (GUILayout.Button("0°")) _propRotationY = 0f;
                if (GUILayout.Button("60°")) _propRotationY = 60f;
                if (GUILayout.Button("120°")) _propRotationY = 120f;
                if (GUILayout.Button("180°")) _propRotationY = 180f;
                if (GUILayout.Button("240°")) _propRotationY = 240f;
                if (GUILayout.Button("300°")) _propRotationY = 300f;
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label($"Échelle : {_propScale:0.##}x", GUILayout.Width(140));
                _propScale = GUILayout.HorizontalSlider(_propScale, 0.2f, 3.0f);
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("Couverture conférée :", GUILayout.Width(140));
                _propCover = (CoverType)GUILayout.Toolbar((int)_propCover, Enum.GetNames(typeof(CoverType)));
                GUILayout.EndHorizontal();

                _propIsWalkable = GUILayout.Toggle(_propIsWalkable, "Praticable (Les unités peuvent marcher dessus)");

                GUILayout.Space(6);
                GUILayout.BeginHorizontal();

                bool isPlacing = (_activeBrush == MapBrushType.PlaceProp3D);
                GUI.backgroundColor = isPlacing ? new Color(0.2f, 0.85f, 0.4f) : Color.white;
                if (GUILayout.Button(isPlacing ? "🎯 MODE PLACEMENT ACTIF (Clic = Poser)" : "Activer le Pinceau de Pose", GUILayout.Height(34)))
                {
                    _activeBrush = MapBrushType.PlaceProp3D;
                }

                bool isErasing = (_activeBrush == MapBrushType.EraseProp3D);
                GUI.backgroundColor = isErasing ? new Color(0.9f, 0.25f, 0.25f) : Color.white;
                if (GUILayout.Button(isErasing ? "🗑️ MODE SUPPRESSION ACTIF" : "Gomme d'Objets 3D", GUILayout.Height(34)))
                {
                    _activeBrush = MapBrushType.EraseProp3D;
                }
                GUI.backgroundColor = Color.white;

                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }

            GUILayout.Space(6);
            GUILayout.Label($"<b>3. Objets 3D Posés en Scène ({_placedPropRecords.Count}) :</b>");
            if (_placedPropRecords.Count == 0)
            {
                GUILayout.Label("<i>Aucun objet 3D posé pour l'instant.</i>");
            }
            else
            {
                GUILayout.BeginVertical(GUI.skin.box);
                foreach (var kvp in new Dictionary<HexCoordinates, MapPropData>(_placedPropRecords))
                {
                    var coords = kvp.Key;
                    var data = kvp.Value;

                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"• <b>{data.PrefabName}</b> en ({coords.Q}, {coords.R}) [Rot: {data.RotationY:0}° | {data.Cover}]");
                    GUI.backgroundColor = Color.red;
                    if (GUILayout.Button("Suppr.", GUILayout.Width(60)))
                    {
                        RemovePropAt(coords);
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

            if (GUILayout.Button($"<b>{title}</b> — <color=#A0B0C0>{description}</color>", GUI.skin.button, GUILayout.Height(30)))
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
            if (GUILayout.Button("💾 Sauvegarder la Carte Intégrale (Sol + Objets 3D)", GUILayout.Height(34)))
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
            GUILayout.Label("<b>Configuration de la Grille Hexagonale :</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Rayon de la Grille (HexRadius) :", GUILayout.Width(200));
            _gridRadiusInput = GUILayout.TextField(_gridRadiusInput, GUILayout.Width(60));
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

            _grid.GenerateGrid();
            _gridVisualizer?.BuildVisualGrid();
            _statusMessage = $"Grille régénérée avec un rayon de {radius} cases.";
        }

        private void SaveCurrentMap(string mapName)
        {
            EnsureDirectoryExists();
            string safeName = string.Join("_", mapName.Split(Path.GetInvalidFileNameChars()));
            if (string.IsNullOrWhiteSpace(safeName)) safeName = "Map_Unnamed";

            var data = new TacticalMapSaveData
            {
                MapName = safeName,
                GridRadius = _grid != null ? _grid.GridRadius : 8
            };

            if (_grid != null)
            {
                foreach (var kvp in _grid.Nodes)
                {
                    var node = kvp.Value;
                    if (node.Cover != CoverType.None || !node.IsWalkable)
                    {
                        data.ModifiedTiles.Add(new MapTileData
                        {
                            Q = node.Coordinates.Q,
                            R = node.Coordinates.R,
                            Cover = node.Cover,
                            IsWalkable = node.IsWalkable
                        });
                    }
                }
            }

            foreach (var prop in _placedPropRecords.Values)
            {
                data.PlacedProps.Add(prop);
            }

            string fullPath = Path.Combine(MapsDirectory, $"{safeName}.json");
            string json = JsonUtility.ToJson(data, true);
            File.WriteAllText(fullPath, json);

            _statusMessage = $"Carte '{safeName}' sauvegardée ({data.ModifiedTiles.Count} tuiles, {data.PlacedProps.Count} objets 3D).";
        }

        private void LoadMap(string fullPath)
        {
            if (!File.Exists(fullPath) || _grid == null) return;

            string json = File.ReadAllText(fullPath);
            var data = JsonUtility.FromJson<TacticalMapSaveData>(json);

            ClearAllPlacedProps();
            _grid.ClearAllCovers();

            for (int i = 0; i < data.ModifiedTiles.Count; i++)
            {
                var tile = data.ModifiedTiles[i];
                var coords = new HexCoordinates(tile.Q, tile.R);
                _grid.SetNodeCover(coords, tile.Cover, tile.IsWalkable);
            }

            EnsurePropsRoot();
            for (int i = 0; i < data.PlacedProps.Count; i++)
            {
                var propData = data.PlacedProps[i];
                var coords = new HexCoordinates(propData.Q, propData.R);
                var node = _grid.GetNode(coords);

                if (node != null)
                {
                    node.HasCustomVisual = true;
                    node.Cover = propData.Cover;
                    node.IsWalkable = propData.IsWalkable;

                    var prefab = LoadPropPrefab(propData.PrefabName);
                    if (prefab != null)
                    {
#if UNITY_EDITOR
                        var instance = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, _propsRoot);
#else
                        var instance = Instantiate(prefab, _propsRoot);
#endif
                        instance.name = $"Prop_{propData.PrefabName}_{coords.Q}_{coords.R}";
                        instance.transform.position = node.WorldPosition;
                        instance.transform.rotation = Quaternion.Euler(0f, propData.RotationY, 0f);
                        instance.transform.localScale = Vector3.Scale(prefab.transform.localScale, Vector3.one * propData.Scale);

                        _spawnedPropInstances[coords] = instance;
                        _placedPropRecords[coords] = propData;
                    }
                }
            }

            _gridVisualizer?.RefreshObstacles();
            _statusMessage = $"Carte '{data.MapName}' chargée avec succès ({data.PlacedProps.Count} objet(s) 3D).";
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
using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Killtime.Tactics.Grid
{
    public enum CeilingVisualMode
    {
        InGame,
        EditorNonCeilingMode,
        EditorCeilingMode
    }

    /// <summary>
    /// Générateur et visualiseur procédural de la grille hexagonale 3D.
    /// Gère la détection automatique du pipeline (URP / Built-in), la génération des UVs,
    /// les obstacles 3D et les surbrillances dynamiques de portée/ciblage.
    /// </summary>
    public partial class HexGridVisualizer : MonoBehaviour
    {
        [Header("Grille Associée")]
        [SerializeField] private TacticalHexGrid _grid;

        [Header("Matériaux & Textures (Optionnel)")]
        [Tooltip("Matériau personnalisé pour les dalles. Si laissé vide, un matériau adapté à URP/Built-in est généré.")]
        [SerializeField] private Material _customTileMaterial;
        [Tooltip("Matériau personnalisé pour les obstacles (couvertures).")]
        [SerializeField] private Material _customObstacleMaterial;
        [Tooltip("Matériau personnalisé pour le plafond (doit être transparent).")]
        [SerializeField] private Material _customCeilingMaterial;

        [Header("Transparence du Plafond")]
        [SerializeField] [Range(0.01f, 0.80f)] private float _ceilingOpacity = 0.22f;
        [SerializeField] private bool _showCeilingInGame = false;

        public CeilingVisualMode CurrentCeilingMode { get; private set; } = CeilingVisualMode.InGame;

        public float CeilingOpacity
        {
            get => _ceilingOpacity;
            set
            {
                _ceilingOpacity = Mathf.Clamp(value, 0.01f, 0.80f);
                RefreshCeilingRenderers();
                try
                {
                    if (Killtime.UI.DevUIPreferences.Current != null)
                    {
                        Killtime.UI.DevUIPreferences.Current.CeilingOpacity = _ceilingOpacity;
                        Killtime.UI.DevUIPreferences.MarkDirty(1.2f);
                    }
                }
                catch { /* ignore */ }
            }
        }

        public bool ShowCeilingInGame
        {
            get => _showCeilingInGame;
            set
            {
                _showCeilingInGame = value;
                RefreshCeilingRenderers();
                try
                {
                    if (Killtime.UI.DevUIPreferences.Current != null)
                    {
                        Killtime.UI.DevUIPreferences.Current.ShowCeilingInGame = value;
                        Killtime.UI.DevUIPreferences.MarkDirty(1.2f);
                    }
                }
                catch { /* ignore */ }
            }
        }

        [Header("Esthétique des Dalles")]
        [SerializeField] private Color _defaultTileColor = new Color(0.12f, 0.14f, 0.18f, 1f);
        [SerializeField] private Color _borderTileColor = new Color(0.08f, 0.09f, 0.12f, 1f);
        [SerializeField] private Color _reachableTileColor = new Color(0.1f, 0.45f, 0.65f, 0.85f);
        [SerializeField] private Color _hoverTileColor = new Color(0.0f, 0.85f, 1.0f, 0.95f);
        [SerializeField] private Color _targetTileColor = new Color(0.9f, 0.2f, 0.25f, 0.95f);
        [SerializeField] private Color _pathTileColor = new Color(0.2f, 0.9f, 0.5f, 0.9f);

        private readonly Dictionary<HexCoordinates, MeshRenderer> _tileRenderers = new();
        private readonly Dictionary<HexCoordinates, MeshRenderer> _ceilingRenderers = new();
        private readonly Dictionary<HexCoordinates, GameObject> _obstacleObjects = new();
        private readonly Dictionary<HexCoordinates, GameObject> _zoneVisualObjects = new();
        private readonly List<Killtime.Core.Combat.SmokeZone> _activeZones = new();
        private readonly HashSet<HexCoordinates> _currentReachable = new();
        private readonly List<HexCoordinates> _currentPath = new();

        private readonly Dictionary<string, Material> _groundMaterialCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Texture2D> _groundTextureCache = new(StringComparer.OrdinalIgnoreCase);

        [Header("Sculpture & Rendu du Relief")]
        [SerializeField] private bool _smoothSlopeTerrain = false;
        [SerializeField] private float _groundTiling = 1.0f;

        public bool SmoothSlopeTerrain
        {
            get => _smoothSlopeTerrain;
            set
            {
                _smoothSlopeTerrain = value;
                RefreshObstacles();
                try
                {
                    if (Killtime.UI.DevUIPreferences.Current != null)
                    {
                        Killtime.UI.DevUIPreferences.Current.SmoothSlope = value;
                        Killtime.UI.DevUIPreferences.MarkDirty(1.2f);
                    }
                }
                catch { /* ignore */ }
            }
        }

        public float GroundTiling
        {
            get => _groundTiling;
            set
            {
                _groundTiling = Mathf.Max(0.1f, value);
                RefreshObstacles();
                try
                {
                    if (Killtime.UI.DevUIPreferences.Current != null)
                    {
                        Killtime.UI.DevUIPreferences.Current.GroundTiling = _groundTiling;
                        Killtime.UI.DevUIPreferences.MarkDirty(1.2f);
                    }
                }
                catch { /* ignore */ }
            }
        }

        private HexCoordinates? _currentHovered;
        private HexCoordinates? _currentTarget;
        private Transform _tilesParent;
        private Transform _ceilingsParent;
        private Transform _obstaclesParent;
        private Transform _zonesParent;
        private LineRenderer _pathLineRenderer;
        private LineRenderer _losLineRenderer;
        private readonly System.Collections.Generic.List<LineRenderer> _losRayRenderers = new();
        private readonly System.Collections.Generic.HashSet<HexCoordinates> _losBlockingCells = new();

        [Header("Brouillard de Guerre (Fog of War)")]
        [Tooltip("Couleur d'obscurité appliquée aux cases hors de vue de l'escouade.")]
        [SerializeField] private Color _fogHiddenTint = new Color(0.015f, 0.018f, 0.035f, 1f);
        private readonly System.Collections.Generic.HashSet<HexCoordinates> _fogVisible = new();
        private bool _fogEnabled = false;


        private Material _baseMaterial;
        private Material _obstacleMaterial;
        private Material _ceilingMaterial;
        private MaterialPropertyBlock _propBlock;
        private MaterialPropertyBlock _ceilingPropBlock;
        private bool _isCeilingPreviewActive = false;
        private Mesh _cachedHexMesh;

        public Material CeilingMaterial => _ceilingMaterial;

        public TacticalHexGrid Grid => _grid;

        private void Awake()
        {
            _propBlock = new MaterialPropertyBlock();
            _ceilingPropBlock = new MaterialPropertyBlock();
            InitializeMaterials();

            if (_grid == null)
            {
                _grid = GetComponent<TacticalHexGrid>() ?? FindAnyObjectByType<TacticalHexGrid>();
            }

            try { LoadVisualPrefs(); } catch { /* prefs optionnelles */ }

            SetupPathLineRenderer();
            SetupLineOfSightRenderer();
        }

        private void Start()
        {
            EnsureCameraAntiAliasing();
            BuildVisualGrid();
        }

        private void LoadVisualPrefs()
        {
            var p = Killtime.UI.DevUIPreferences.Current;
            if (p == null) return;
            // Affectation directe des champs pour éviter de re-marquer dirty au chargement.
            _ceilingOpacity = Mathf.Clamp(p.CeilingOpacity, 0.01f, 0.80f);
            _showCeilingInGame = p.ShowCeilingInGame;
            _smoothSlopeTerrain = p.SmoothSlope;
            _groundTiling = Mathf.Max(0.1f, p.GroundTiling);
        }

        private static void EnsureCameraAntiAliasing()
        {
            var cam = UnityEngine.Camera.main;
            if (cam == null) return;

            var urpData = cam.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            if (urpData != null)
            {
                urpData.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                urpData.antialiasingQuality = UnityEngine.Rendering.Universal.AntialiasingQuality.High;
            }
        }


        [ContextMenu("Reconstruire la Grille")]
        public void BuildVisualGrid()
        {
            if (_grid == null) return;

            if (_tilesParent != null) Destroy(_tilesParent.gameObject);
            if (_ceilingsParent != null) Destroy(_ceilingsParent.gameObject);
            if (_obstaclesParent != null) Destroy(_obstaclesParent.gameObject);
            if (_zonesParent != null) Destroy(_zonesParent.gameObject);
            _tileRenderers.Clear();
            _ceilingRenderers.Clear();
            _obstacleObjects.Clear();
            _zoneVisualObjects.Clear();

            _tilesParent = new GameObject("HexTiles").transform;
            _tilesParent.SetParent(transform, false);

            _ceilingsParent = new GameObject("HexCeilings").transform;
            _ceilingsParent.SetParent(transform, false);

            _obstaclesParent = new GameObject("HexObstacles").transform;
            _obstaclesParent.SetParent(transform, false);

            _zonesParent = new GameObject("HexZones").transform;
            _zonesParent.SetParent(transform, false);

            foreach (var kvp in _grid.Nodes)
            {
                var coords = kvp.Key;
                var node = kvp.Value;

                var tileObj = new GameObject($"Tile_{coords.Q}_{coords.R}");
                tileObj.transform.SetParent(_tilesParent, false);
                tileObj.transform.position = node.WorldPosition;

                var mesh = GenerateSculptedTileMesh(_grid, coords, node, _grid.HexRadius * 0.95f, _smoothSlopeTerrain, _groundTiling);

                var mf = tileObj.AddComponent<MeshFilter>();
                mf.sharedMesh = mesh;

                var mr = tileObj.AddComponent<MeshRenderer>();
                mr.sharedMaterial = GetOrCreateGroundMaterial(node.GroundTexture);
                _tileRenderers[coords] = mr;

                var col = tileObj.AddComponent<MeshCollider>();
                col.sharedMesh = mesh;

                UpdateCeilingVisual(coords, node);
                UpdateObstacleVisual(coords, node);
            }

            RefreshAllTileColors();
            RefreshCeilingRenderers();
        }

    }
}

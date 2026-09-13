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
    public class HexGridVisualizer : MonoBehaviour
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
        [SerializeField] private bool _showCeilingInGame = true;

        public CeilingVisualMode CurrentCeilingMode { get; private set; } = CeilingVisualMode.InGame;

        public float CeilingOpacity
        {
            get => _ceilingOpacity;
            set
            {
                _ceilingOpacity = Mathf.Clamp(value, 0.01f, 0.80f);
                RefreshCeilingRenderers();
            }
        }

        public bool ShowCeilingInGame
        {
            get => _showCeilingInGame;
            set
            {
                _showCeilingInGame = value;
                RefreshCeilingRenderers();
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
            }
        }

        public float GroundTiling
        {
            get => _groundTiling;
            set
            {
                _groundTiling = Mathf.Max(0.1f, value);
                RefreshObstacles();
            }
        }

        private HexCoordinates? _currentHovered;
        private HexCoordinates? _currentTarget;
        private Transform _tilesParent;
        private Transform _ceilingsParent;
        private Transform _obstaclesParent;
        private LineRenderer _pathLineRenderer;

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

            SetupPathLineRenderer();
        }

        private void Start()
        {
            EnsureCameraAntiAliasing();
            BuildVisualGrid();
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

        private void InitializeMaterials()
        {
            if (_customTileMaterial != null)
            {
                _baseMaterial = new Material(_customTileMaterial) { name = "HexGrid_BaseMat_Instance" };
            }
            else
            {
                _baseMaterial = CreatePipelineSafeMaterial("HexGrid_Tile_Mat", _defaultTileColor);
            }

            if (_customObstacleMaterial != null)
            {
                _obstacleMaterial = new Material(_customObstacleMaterial) { name = "HexGrid_ObstacleMat_Instance" };
            }
            else
            {
                _obstacleMaterial = new Material(_baseMaterial) { name = "HexGrid_Obstacle_Mat" };
            }

            _ceilingMaterial = CreatePipelineSafeTransparentMaterial("HexGrid_Ceiling_Mat");
        }

        private Material CreatePipelineSafeTransparentMaterial(string materialName)
        {
            if (_customCeilingMaterial != null)
            {
                return new Material(_customCeilingMaterial) { name = materialName };
            }

            Shader targetShader = Shader.Find("Sprites/Default")
                               ?? Shader.Find("Universal Render Pipeline/Particles/Unlit")
                               ?? Shader.Find("Legacy Shaders/Transparent/Diffuse")
                               ?? Shader.Find("Unlit/Transparent");

            if (targetShader == null)
            {
                var currentRP = GraphicsSettings.currentRenderPipeline;
                if (currentRP != null)
                {
                    targetShader = Shader.Find("Universal Render Pipeline/Unlit");
                }
                else
                {
                    targetShader = Shader.Find("Standard");
                }
            }

            Material mat = new Material(targetShader) { name = materialName };

            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1.0f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0.0f);
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0.0f);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", (float)CullMode.Off);

            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.DisableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");

            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)RenderQueue.Transparent + 100;

            Color defaultGlass = new Color(0.0f, 0.85f, 1.0f, _ceilingOpacity);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", defaultGlass);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", defaultGlass);

            return mat;
        }

        public static Mesh GenerateFlatPointyHexMesh(float radius)
        {
            var mesh = new Mesh { name = "ProceduralFlatPointyHex" };

            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var colors = new List<Color32>();

            vertices.Add(Vector3.zero);
            normals.Add(Vector3.up);
            uvs.Add(new Vector2(0.5f, 0.5f));
            colors.Add(new Color32(255, 255, 255, 255));

            for (int i = 0; i < 6; i++)
            {
                float angle = (30f + 60f * i) * Mathf.Deg2Rad;
                float x = radius * Mathf.Cos(angle);
                float z = radius * Mathf.Sin(angle);
                vertices.Add(new Vector3(x, 0f, z));
                normals.Add(Vector3.up);
                uvs.Add(new Vector2((x / (radius * 2f)) + 0.5f, (z / (radius * 2f)) + 0.5f));
                colors.Add(new Color32(255, 255, 255, 255));
            }

            for (int i = 1; i <= 6; i++)
            {
                int next = (i == 6) ? 1 : i + 1;
                triangles.Add(0);
                triangles.Add(i);
                triangles.Add(next);
            }

            for (int i = 1; i <= 6; i++)
            {
                int next = (i == 6) ? 1 : i + 1;
                triangles.Add(0);
                triangles.Add(next);
                triangles.Add(i);
            }

            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.RecalculateBounds();

            return mesh;
        }

        private Material CreatePipelineSafeMaterial(string materialName, Color defaultColor)
        {
            Shader targetShader = null;

            var currentRP = GraphicsSettings.currentRenderPipeline;
            if (currentRP != null)
            {
                string rpName = currentRP.GetType().Name;
                if (rpName.Contains("Universal") || rpName.Contains("URP"))
                {
                    targetShader = Shader.Find("Universal Render Pipeline/Lit")
                                ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                                ?? Shader.Find("Universal Render Pipeline/Unlit");
                }
                else if (rpName.Contains("HighDefinition") || rpName.Contains("HDRP"))
                {
                    targetShader = Shader.Find("HDRP/Lit")
                                ?? Shader.Find("HDRP/Unlit");
                }
            }

            if (targetShader == null)
            {
                targetShader = Shader.Find("Killtime/TacticalLit")
                            ?? Shader.Find("Universal Render Pipeline/Lit")
                            ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                            ?? Shader.Find("Universal Render Pipeline/Unlit")
                            ?? Shader.Find("Standard")
                            ?? Shader.Find("Unlit/Color")
                            ?? Shader.Find("Sprites/Default");
            }

            Material mat = new Material(targetShader) { name = materialName };

            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", defaultColor);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", defaultColor);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.05f);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.05f);

            return mat;
        }

        private void SetupPathLineRenderer()
        {
            var lineObj = new GameObject("PathPreviewLine");
            lineObj.transform.SetParent(transform);
            _pathLineRenderer = lineObj.AddComponent<LineRenderer>();
            _pathLineRenderer.material = _baseMaterial;
            _pathLineRenderer.startColor = new Color(0.2f, 0.9f, 0.5f, 0.8f);
            _pathLineRenderer.endColor = new Color(0.0f, 0.85f, 1.0f, 0.9f);
            _pathLineRenderer.startWidth = 0.12f;
            _pathLineRenderer.endWidth = 0.12f;
            _pathLineRenderer.positionCount = 0;
            _pathLineRenderer.useWorldSpace = true;
        }

        [ContextMenu("Reconstruire la Grille")]
        public void BuildVisualGrid()
        {
            if (_grid == null) return;

            if (_tilesParent != null) Destroy(_tilesParent.gameObject);
            if (_ceilingsParent != null) Destroy(_ceilingsParent.gameObject);
            if (_obstaclesParent != null) Destroy(_obstaclesParent.gameObject);
            _tileRenderers.Clear();
            _ceilingRenderers.Clear();
            _obstacleObjects.Clear();

            _tilesParent = new GameObject("HexTiles").transform;
            _tilesParent.SetParent(transform, false);

            _ceilingsParent = new GameObject("HexCeilings").transform;
            _ceilingsParent.SetParent(transform, false);

            _obstaclesParent = new GameObject("HexObstacles").transform;
            _obstaclesParent.SetParent(transform, false);

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

        public void RefreshObstacles()
        {
            if (_grid == null) return;

            float hexRadius = _grid.HexRadius * 0.95f;

            foreach (var kvp in _grid.Nodes)
            {
                var coords = kvp.Key;
                var node = kvp.Value;

                if (_tileRenderers.TryGetValue(coords, out var mr) && mr != null)
                {
                    mr.transform.position = node.WorldPosition;
                    var mesh = GenerateSculptedTileMesh(_grid, coords, node, hexRadius, _smoothSlopeTerrain, _groundTiling);

                    var mf = mr.GetComponent<MeshFilter>();
                    if (mf != null) mf.sharedMesh = mesh;

                    var col = mr.GetComponent<MeshCollider>();
                    if (col != null) col.sharedMesh = mesh;

                    mr.sharedMaterial = GetOrCreateGroundMaterial(node.GroundTexture);
                }

                UpdateCeilingVisual(coords, node);
                UpdateObstacleVisual(coords, node);
            }

            RefreshAllTileColors();
            RefreshCeilingRenderers();
        }

        private static readonly Dictionary<string, Texture2D> _allKnownGroundTextures = new(StringComparer.OrdinalIgnoreCase);

        public Material GetOrCreateGroundMaterial(string textureRelativePath)
        {
            if (string.IsNullOrEmpty(textureRelativePath))
            {
                return _baseMaterial;
            }

            if (_groundMaterialCache.TryGetValue(textureRelativePath, out var mat) && mat != null)
            {
                return mat;
            }

            var tex = LoadGroundTexture(textureRelativePath);
            if (tex == null)
            {
                return _baseMaterial;
            }

            mat = new Material(_baseMaterial)
            {
                name = $"GroundMat_{tex.name}"
            };

            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Trilinear;
            tex.anisoLevel = 16;

            mat.mainTexture = tex;
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);

            // Suppression du spéculaire pour éliminer les micro-étincelles sur les textures bruitées
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.0f);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.0f);
            if (mat.HasProperty("_SpecularHighlights")) mat.SetFloat("_SpecularHighlights", 0.0f);

            _groundMaterialCache[textureRelativePath] = mat;
            return mat;
        }

        public Texture2D LoadGroundTexture(string textureRelativePath)
        {
            if (string.IsNullOrEmpty(textureRelativePath)) return null;

            if (_groundTextureCache.TryGetValue(textureRelativePath, out var cached) && cached != null)
            {
                return cached;
            }

            string baseName = Path.GetFileNameWithoutExtension(textureRelativePath);
            if (_allKnownGroundTextures.TryGetValue(baseName, out var byBase) && byBase != null)
            {
                _groundTextureCache[textureRelativePath] = byBase;
                return byBase;
            }

            var loaded = Resources.Load<Texture2D>($"Grounds/{textureRelativePath}")
                      ?? Resources.Load<Texture2D>(textureRelativePath)
                      ?? Resources.Load<Texture2D>($"Grounds/{baseName}");

            if (loaded == null)
            {
                var allResourcesGrounds = Resources.LoadAll<Texture2D>("Grounds");
                for (int i = 0; i < allResourcesGrounds.Length; i++)
                {
                    var rTex = allResourcesGrounds[i];
                    if (rTex == null) continue;

                    rTex.wrapMode = TextureWrapMode.Repeat;
                    rTex.filterMode = FilterMode.Trilinear;
                    rTex.anisoLevel = 16;
                    _allKnownGroundTextures[rTex.name] = rTex;

                    if (string.Equals(rTex.name, baseName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(rTex.name, textureRelativePath, StringComparison.OrdinalIgnoreCase))
                    {
                        loaded = rTex;
                    }
                }
            }

            if (loaded != null)
            {
                loaded.wrapMode = TextureWrapMode.Repeat;
                loaded.filterMode = FilterMode.Trilinear;
                loaded.anisoLevel = 16;

                _groundTextureCache[textureRelativePath] = loaded;
                _allKnownGroundTextures[textureRelativePath] = loaded;
                _allKnownGroundTextures[baseName] = loaded;
                return loaded;
            }

            return null;
        }

#if UNITY_EDITOR
        public static bool EnsureTextureImporterSettings(string assetPath)
        {
            var importer = UnityEditor.AssetImporter.GetAtPath(assetPath) as UnityEditor.TextureImporter;
            if (importer == null) return false;

            bool changed = false;

            if (importer.textureType != UnityEditor.TextureImporterType.Default)
            {
                importer.textureType = UnityEditor.TextureImporterType.Default;
                changed = true;
            }

            if (!importer.mipmapEnabled)
            {
                importer.mipmapEnabled = true;
                importer.mipmapFilter = UnityEditor.TextureImporterMipFilter.BoxFilter;
                changed = true;
            }

            if (importer.filterMode != FilterMode.Trilinear)
            {
                importer.filterMode = FilterMode.Trilinear;
                changed = true;
            }

            if (importer.anisoLevel < 16)
            {
                importer.anisoLevel = 16;
                changed = true;
            }

            if (importer.wrapMode != TextureWrapMode.Repeat)
            {
                importer.wrapMode = TextureWrapMode.Repeat;
                changed = true;
            }

            if (changed)
            {
                importer.SaveAndReimport();
                return true;
            }

            return false;
        }

        [UnityEditor.MenuItem("Killtime/Générer Mipmaps Textures Sols (Anti-Grésillement)")]
        private static void MenuBatchFixAllGroundTextureAssets()
        {
            BatchFixAllGroundTextureAssets();
        }
#endif

        public static void BatchFixAllGroundTextureAssets()
        {
#if UNITY_EDITOR
            if (Application.isPlaying)
            {
                Debug.LogWarning("[HexGridVisualizer] Veuillez arrêter le mode Play avant d'exécuter la réimportation des assets sur disque.");
                return;
            }

            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Resources/Grounds" });
            int fixedCount = 0;

            for (int i = 0; i < guids.Length; i++)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[i]);
                if (EnsureTextureImporterSettings(path))
                {
                    fixedCount++;
                }
            }

            UnityEditor.AssetDatabase.Refresh();
            Debug.Log($"[HexGridVisualizer] Mipmaps & Anisotropie 16x générés avec succès sur le disque pour {fixedCount} texture(s).");
#endif
        }

        public static Mesh GenerateSculptedTileMesh(TacticalHexGrid grid, HexCoordinates coords, HexNode node, float radius, bool smoothSlope, float uvTiling)
        {
            var mesh = new Mesh { name = $"SculptedHex_{coords.Q}_{coords.R}" };

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            Vector3 centerWorld = node.WorldPosition;
            float worldTilingFactor = Mathf.Max(0.1f, radius * 2f * uvTiling);

            // 1. Calcul géométrique des 6 sommets supérieurs
            Vector3 centerTop = Vector3.zero;
            Vector3[] topCorners = new Vector3[6];

            for (int i = 0; i < 6; i++)
            {
                float angle = (30f + 60f * i) * Mathf.Deg2Rad;
                float x = radius * Mathf.Cos(angle);
                float z = radius * Mathf.Sin(angle);
                float y = 0f;

                if (smoothSlope && grid != null)
                {
                    var neighborA = grid.GetNode(coords.GetNeighbor(i));
                    var neighborB = grid.GetNode(coords.GetNeighbor((i + 1) % 6));
                    float eCur = node.Elevation;
                    float eA = neighborA != null ? neighborA.Elevation : eCur;
                    float eB = neighborB != null ? neighborB.Elevation : eCur;
                    float avgCorner = (eCur + eA + eB) / 3f;
                    y = avgCorner - eCur;
                }

                topCorners[i] = new Vector3(x, y, z);
            }

            // 2. FACE DU SOL (Horizontale, plane, normale vers le haut, enroulement horaire strict)
            int topCenterIdx = vertices.Count;
            vertices.Add(centerTop);
            normals.Add(Vector3.up);
            uvs.Add(new Vector2(0.5f, 0.5f));

            int topCornerStartIdx = vertices.Count;
            for (int i = 0; i < 6; i++)
            {
                vertices.Add(topCorners[i]);
                float u = (topCorners[i].x / (radius * 2f)) * uvTiling + 0.5f;
                float v = (topCorners[i].z / (radius * 2f)) * uvTiling + 0.5f;
                uvs.Add(new Vector2(u, v));

                if (smoothSlope)
                {
                    int prev = (i + 5) % 6;
                    Vector3 edgeA = topCorners[prev] - centerTop;
                    Vector3 edgeB = topCorners[i] - centerTop;
                    Vector3 n = Vector3.Cross(edgeA, edgeB).normalized;
                    if (n.y < 0f) n = -n;
                    normals.Add(n);
                }
                else
                {
                    normals.Add(Vector3.up);
                }
            }

            for (int i = 0; i < 6; i++)
            {
                int next = (i + 1) % 6;

                // Face supérieure unique (sens horaire strict, élimine tout Z-fighting coplanaire)
                triangles.Add(topCenterIdx);
                triangles.Add(topCornerStartIdx + next);
                triangles.Add(topCornerStartIdx + i);
            }

            // 3. ÉPAISSEUR DE DALLE (8cm standard, s'étire uniquement en falaise pour joindre le voisin le plus bas)
            float minNeighborElev = node.Elevation;
            if (grid != null)
            {
                for (int d = 0; d < 6; d++)
                {
                    var neighbor = grid.GetNode(coords.GetNeighbor(d));
                    if (neighbor != null && neighbor.Elevation < minNeighborElev)
                    {
                        minNeighborElev = neighbor.Elevation;
                    }
                }
            }

            float stepDrop = Mathf.Max(0f, node.Elevation - minNeighborElev);
            float tileThickness = 0.08f;
            float bottomLocalY = -(stepDrop + tileThickness);

            for (int i = 0; i < 6; i++)
            {
                int next = (i + 1) % 6;

                Vector3 vTL = topCorners[i];
                Vector3 vTR = topCorners[next];
                Vector3 vBR = new Vector3(topCorners[next].x, bottomLocalY, topCorners[next].z);
                Vector3 vBL = new Vector3(topCorners[i].x, bottomLocalY, topCorners[i].z);

                Vector3 sideNormal = Vector3.Cross(vTR - vTL, vBL - vTL).normalized;

                int baseIdx = vertices.Count;
                vertices.Add(vTL);
                vertices.Add(vTR);
                vertices.Add(vBR);
                vertices.Add(vBL);

                normals.Add(sideNormal);
                normals.Add(sideNormal);
                normals.Add(sideNormal);
                normals.Add(sideNormal);

                uvs.Add(new Vector2(0f, 1f));
                uvs.Add(new Vector2(1f, 1f));
                uvs.Add(new Vector2(1f, 0f));
                uvs.Add(new Vector2(0f, 0f));

                triangles.Add(baseIdx);
                triangles.Add(baseIdx + 1);
                triangles.Add(baseIdx + 2);

                triangles.Add(baseIdx);
                triangles.Add(baseIdx + 2);
                triangles.Add(baseIdx + 3);
            }

            // 4. DESSOUS DE LA DALLE
            int botCenterIdx = vertices.Count;
            vertices.Add(new Vector3(0, bottomLocalY, 0));
            normals.Add(Vector3.down);
            uvs.Add(new Vector2(0.5f, 0.5f));

            int botCornerStartIdx = vertices.Count;
            for (int i = 0; i < 6; i++)
            {
                vertices.Add(new Vector3(topCorners[i].x, bottomLocalY, topCorners[i].z));
                normals.Add(Vector3.down);
                uvs.Add(new Vector2(0.5f, 0.5f));
            }

            for (int i = 0; i < 6; i++)
            {
                int next = (i + 1) % 6;
                triangles.Add(botCenterIdx);
                triangles.Add(botCornerStartIdx + i);
                triangles.Add(botCornerStartIdx + next);
            }

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();

            return mesh;
        }

        public void SetCeilingVisualMode(CeilingVisualMode mode)
        {
            CurrentCeilingMode = mode;
            _isCeilingPreviewActive = (mode == CeilingVisualMode.EditorCeilingMode);
            RefreshCeilingRenderers();
        }

        public void SetCeilingPreviewActive(bool active)
        {
            SetCeilingVisualMode(active ? CeilingVisualMode.EditorCeilingMode : CeilingVisualMode.EditorNonCeilingMode);
        }

        private Mesh _cachedFlatCeilingMesh;

        private void UpdateCeilingVisual(HexCoordinates coords, HexNode node)
        {
            if (node.HasCeiling)
            {
                if (!_ceilingRenderers.TryGetValue(coords, out var mr) || mr == null)
                {
                    if (_cachedFlatCeilingMesh == null)
                    {
                        _cachedFlatCeilingMesh = GenerateFlatPointyHexMesh(_grid.HexRadius * 0.95f);
                    }

                    var ceilObj = new GameObject($"Ceiling_{coords.Q}_{coords.R}");
                    ceilObj.transform.SetParent(_ceilingsParent, false);
                    ceilObj.transform.position = node.WorldPosition + Vector3.up * _grid.CeilingHeight;
                    ceilObj.layer = 2; // Ignore Raycast : jamais cliquable

                    var mf = ceilObj.AddComponent<MeshFilter>();
                    mf.sharedMesh = _cachedFlatCeilingMesh;

                    mr = ceilObj.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = _ceilingMaterial;
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                    mr.receiveShadows = false;
                    mr.SetPropertyBlock(_ceilingPropBlock);
                    _ceilingRenderers[coords] = mr;
                }
            }
            else
            {
                if (_ceilingRenderers.TryGetValue(coords, out var mr))
                {
                    if (mr != null) Destroy(mr.gameObject);
                    _ceilingRenderers.Remove(coords);
                }
            }
        }

        private GameObject _ceilingSelectorObj;
        private MeshRenderer _ceilingSelectorRenderer;
        private bool _isHoveringCeiling = false;

        private void EnsureCeilingSelector()
        {
            if (_ceilingSelectorObj != null) return;

            _ceilingSelectorObj = new GameObject("CeilingHoverSelector");
            _ceilingSelectorObj.transform.SetParent(transform, false);
            _ceilingSelectorObj.layer = 2;

            var mf = _ceilingSelectorObj.AddComponent<MeshFilter>();
            mf.sharedMesh = GenerateFlatPointyHexMesh(_grid != null ? _grid.HexRadius * 0.98f : 0.98f);

            _ceilingSelectorRenderer = _ceilingSelectorObj.AddComponent<MeshRenderer>();
            _ceilingSelectorRenderer.sharedMaterial = _ceilingMaterial;
            _ceilingSelectorRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _ceilingSelectorRenderer.receiveShadows = false;

            _ceilingSelectorObj.SetActive(false);
        }

        private void RefreshCeilingRenderers()
        {
            EnsureCeilingSelector();

            float activeAlpha;
            Color defaultCeilCol;

            if (CurrentCeilingMode == CeilingVisualMode.EditorCeilingMode)
            {
                activeAlpha = Mathf.Clamp(_ceilingOpacity * 1.5f + 0.35f, 0.52f, 0.78f);
                defaultCeilCol = new Color(0.12f, 0.75f, 0.98f, activeAlpha);
            }
            else if (CurrentCeilingMode == CeilingVisualMode.EditorNonCeilingMode)
            {
                activeAlpha = Mathf.Clamp(_ceilingOpacity, 0.22f, 0.65f);
                defaultCeilCol = new Color(0.12f, 0.75f, 0.98f, activeAlpha);
            }
            else
            {
                activeAlpha = Mathf.Clamp(_ceilingOpacity * 0.85f, 0.18f, 0.55f);
                defaultCeilCol = new Color(0.10f, 0.70f, 0.95f, activeAlpha);
            }

            bool isVisible = (CurrentCeilingMode != CeilingVisualMode.InGame) || _showCeilingInGame;

            if (_ceilingMaterial != null)
            {
                if (_ceilingMaterial.HasProperty("_BaseColor")) _ceilingMaterial.SetColor("_BaseColor", defaultCeilCol);
                if (_ceilingMaterial.HasProperty("_Color")) _ceilingMaterial.SetColor("_Color", defaultCeilCol);
            }

            if (_ceilingPropBlock == null) _ceilingPropBlock = new MaterialPropertyBlock();
            _ceilingPropBlock.SetColor("_BaseColor", defaultCeilCol);
            _ceilingPropBlock.SetColor("_Color", defaultCeilCol);

            foreach (var kvp in _ceilingRenderers)
            {
                var coords = kvp.Key;
                var mr = kvp.Value;
                if (mr == null) continue;

                mr.enabled = isVisible;
                if (isVisible)
                {
                    if (_isHoveringCeiling && _currentHovered.HasValue && _currentHovered.Value.Equals(coords))
                    {
                        var hoverBlock = new MaterialPropertyBlock();
                        Color hoverCol = new Color(0.0f, 1.0f, 1.0f, Mathf.Clamp01(activeAlpha + 0.35f));
                        hoverBlock.SetColor("_BaseColor", hoverCol);
                        hoverBlock.SetColor("_Color", hoverCol);
                        mr.SetPropertyBlock(hoverBlock);
                    }
                    else
                    {
                        mr.SetPropertyBlock(_ceilingPropBlock);
                    }
                }
            }

            if (_ceilingSelectorObj != null)
            {
                if (isVisible && _isHoveringCeiling && _currentHovered.HasValue && _grid != null)
                {
                    var node = _grid.GetNode(_currentHovered.Value);
                    if (node != null)
                    {
                        _ceilingSelectorObj.transform.position = node.WorldPosition + Vector3.up * (_grid.CeilingHeight + 0.015f);

                        var selBlock = new MaterialPropertyBlock();
                        Color selCol = new Color(0.0f, 1.0f, 1.0f, 0.92f);
                        selBlock.SetColor("_BaseColor", selCol);
                        selBlock.SetColor("_Color", selCol);
                        _ceilingSelectorRenderer.SetPropertyBlock(selBlock);
                        _ceilingSelectorObj.SetActive(true);
                    }
                    else
                    {
                        _ceilingSelectorObj.SetActive(false);
                    }
                }
                else
                {
                    _ceilingSelectorObj.SetActive(false);
                }
            }

            TacticalCeilingProp.UpdateAllVisualModes(CurrentCeilingMode, activeAlpha, isVisible);
        }

        public void SetHoveredCoord(HexCoordinates? coord, bool isCeilingLevel = false)
        {
            if (_currentHovered.Equals(coord) && _isHoveringCeiling == isCeilingLevel) return;
            _currentHovered = coord;
            _isHoveringCeiling = isCeilingLevel;
            RefreshAllTileColors();
            RefreshCeilingRenderers();
        }

        private void UpdateObstacleVisual(HexCoordinates coords, HexNode node)
        {
            if (_obstacleObjects.TryGetValue(coords, out var existing))
            {
                Destroy(existing);
                _obstacleObjects.Remove(coords);
            }

            if (node.HasCustomVisual) return;

            if (node.Cover == CoverType.Half)
            {
                var halfCover = GameObject.CreatePrimitive(PrimitiveType.Cube);
                halfCover.name = $"CoverHalf_{coords.Q}_{coords.R}";
                halfCover.transform.SetParent(_obstaclesParent, false);
                halfCover.transform.position = node.WorldPosition + Vector3.up * 0.35f;
                halfCover.transform.localScale = new Vector3(_grid.HexRadius * 0.8f, 0.7f, _grid.HexRadius * 0.4f);

                var rend = halfCover.GetComponent<MeshRenderer>();
                rend.sharedMaterial = _obstacleMaterial;

                _propBlock.SetColor("_BaseColor", new Color(0.75f, 0.55f, 0.15f));
                _propBlock.SetColor("_Color", new Color(0.75f, 0.55f, 0.15f));
                rend.SetPropertyBlock(_propBlock);

                _obstacleObjects[coords] = halfCover;
            }
            else if (node.Cover == CoverType.Full)
            {
                var fullCover = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                fullCover.name = $"CoverFull_{coords.Q}_{coords.R}";
                fullCover.transform.SetParent(_obstaclesParent, false);
                fullCover.transform.position = node.WorldPosition + Vector3.up * 1.1f;
                fullCover.transform.localScale = new Vector3(_grid.HexRadius * 0.8f, 1.1f, _grid.HexRadius * 0.8f);

                var rend = fullCover.GetComponent<MeshRenderer>();
                rend.sharedMaterial = _obstacleMaterial;

                _propBlock.SetColor("_BaseColor", new Color(0.25f, 0.28f, 0.35f));
                _propBlock.SetColor("_Color", new Color(0.25f, 0.28f, 0.35f));
                rend.SetPropertyBlock(_propBlock);

                _obstacleObjects[coords] = fullCover;
            }
        }

        public void SetReachableCoords(IEnumerable<HexCoordinates> reachable)
        {
            _currentReachable.Clear();
            if (reachable != null)
            {
                foreach (var c in reachable) _currentReachable.Add(c);
            }
            RefreshAllTileColors();
        }

        public void SetHoveredCoord(HexCoordinates? coord)
        {
            if (_currentHovered.Equals(coord)) return;
            _currentHovered = coord;
            RefreshAllTileColors();
        }

        public void SetTargetCoord(HexCoordinates? coord)
        {
            if (_currentTarget.Equals(coord)) return;
            _currentTarget = coord;
            RefreshAllTileColors();
        }

        public void SetPathPreview(List<HexCoordinates> path)
        {
            _currentPath.Clear();
            if (path != null && path.Count > 1)
            {
                _currentPath.AddRange(path);
                if (_pathLineRenderer != null)
                {
                    _pathLineRenderer.positionCount = path.Count;
                    for (int i = 0; i < path.Count; i++)
                    {
                        var worldPos = path[i].ToWorldPosition(_grid.HexRadius, 0.15f);
                        _pathLineRenderer.SetPosition(i, worldPos);
                    }
                }
            }
            else
            {
                ClearPathPreview();
            }

            RefreshAllTileColors();
        }

        public void ClearPathPreview()
        {
            _currentPath.Clear();
            if (_pathLineRenderer != null)
            {
                _pathLineRenderer.positionCount = 0;
            }
            RefreshAllTileColors();
        }

        private void RefreshAllTileColors()
        {
            foreach (var kvp in _tileRenderers)
            {
                var coords = kvp.Key;
                var mr = kvp.Value;
                if (mr == null) continue;

                var node = _grid != null ? _grid.GetNode(coords) : null;
                bool isTextured = node != null && !string.IsNullOrEmpty(node.GroundTexture);

                Color c = isTextured ? Color.white : _defaultTileColor;

                if (node != null && !node.IsWalkable)
                {
                    c = isTextured ? new Color(0.7f, 0.25f, 0.25f, 1f) : new Color(0.18f, 0.1f, 0.1f, 1f);
                }
                else if (_currentHovered.HasValue && _currentHovered.Value.Equals(coords))
                {
                    c = isTextured ? Color.Lerp(Color.white, _hoverTileColor, 0.65f) : _hoverTileColor;
                }
                else if (_currentTarget.HasValue && _currentTarget.Value.Equals(coords))
                {
                    c = isTextured ? Color.Lerp(Color.white, _targetTileColor, 0.70f) : _targetTileColor;
                }
                else if (_currentPath.Contains(coords))
                {
                    c = isTextured ? Color.Lerp(Color.white, _pathTileColor, 0.60f) : _pathTileColor;
                }
                else if (_currentReachable.Contains(coords))
                {
                    c = isTextured ? Color.Lerp(Color.white, _reachableTileColor, 0.50f) : _reachableTileColor;
                }

                _propBlock.Clear();
                _propBlock.SetColor("_BaseColor", c);
                _propBlock.SetColor("_Color", c);

                if (isTextured)
                {
                    var tex = LoadGroundTexture(node.GroundTexture);
                    if (tex != null)
                    {
                        _propBlock.SetTexture("_BaseMap", tex);
                        _propBlock.SetTexture("_MainTex", tex);
                    }
                }

                mr.SetPropertyBlock(_propBlock);
            }
        }

        public static Mesh GeneratePointyHexMesh(float radius, float height)
        {
            var mesh = new Mesh { name = "ProceduralPointyHex" };

            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();

            // 1. Sommet central supérieur
            Vector3 centerTop = new Vector3(0, height * 0.5f, 0);
            vertices.Add(centerTop);
            normals.Add(Vector3.up);
            uvs.Add(new Vector2(0.5f, 0.5f));

            // 2. 6 Sommets périphériques supérieurs
            for (int i = 0; i < 6; i++)
            {
                float angle = (30f + 60f * i) * Mathf.Deg2Rad;
                float x = radius * Mathf.Cos(angle);
                float z = radius * Mathf.Sin(angle);
                vertices.Add(new Vector3(x, height * 0.5f, z));
                normals.Add(Vector3.up);
                uvs.Add(new Vector2((x / (radius * 2f)) + 0.5f, (z / (radius * 2f)) + 0.5f));
            }

            // Triangles supérieurs
            for (int i = 1; i <= 6; i++)
            {
                int next = (i == 6) ? 1 : i + 1;
                triangles.Add(0);
                triangles.Add(i);
                triangles.Add(next);
            }

            // 3. Sommet central inférieur
            int centerBottomIndex = vertices.Count;
            Vector3 centerBottom = new Vector3(0, -height * 0.5f, 0);
            vertices.Add(centerBottom);
            normals.Add(Vector3.down);
            uvs.Add(new Vector2(0.5f, 0.5f));

            // 4. 6 Sommets périphériques inférieurs
            int bottomStartIndex = vertices.Count;
            for (int i = 0; i < 6; i++)
            {
                float angle = (30f + 60f * i) * Mathf.Deg2Rad;
                float x = radius * Mathf.Cos(angle);
                float z = radius * Mathf.Sin(angle);
                vertices.Add(new Vector3(x, -height * 0.5f, z));
                normals.Add(Vector3.down);
                uvs.Add(new Vector2((x / (radius * 2f)) + 0.5f, (z / (radius * 2f)) + 0.5f));
            }

            // Triangles inférieurs
            for (int i = 0; i < 6; i++)
            {
                int curr = bottomStartIndex + i;
                int next = bottomStartIndex + ((i + 1) % 6);
                triangles.Add(centerBottomIndex);
                triangles.Add(next);
                triangles.Add(curr);
            }

            // 5. Flancs latéraux
            for (int i = 0; i < 6; i++)
            {
                int topCurr = 1 + i;
                int topNext = 1 + ((i + 1) % 6);
                int botCurr = bottomStartIndex + i;
                int botNext = bottomStartIndex + ((i + 1) % 6);

                triangles.Add(topCurr);
                triangles.Add(botCurr);
                triangles.Add(topNext);

                triangles.Add(topNext);
                triangles.Add(botCurr);
                triangles.Add(botNext);
            }

            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.RecalculateBounds();

            return mesh;
        }
    }
}
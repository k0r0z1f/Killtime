using System.Collections.Generic;
using UnityEngine;

namespace Killtime.Tactics.Grid
{
    /// <summary>
    /// Générateur et visualiseur procédural de la grille hexagonale 3D (Style Fallout / XCOM).
    /// Génère les dalles hexagonales, les obstacles 3D (demi-couvertures et couvertures totales),
    /// les colliders de clic et les surbrillances dynamiques (portée PA, curseur de survol, chemin).
    /// </summary>
    public class HexGridVisualizer : MonoBehaviour
    {
        [Header("Grille Associée")]
        [SerializeField] private TacticalHexGrid _grid;

        [Header("Esthétique")]
        [SerializeField] private Color _defaultTileColor = new Color(0.12f, 0.14f, 0.18f, 1f);
        [SerializeField] private Color _borderTileColor = new Color(0.08f, 0.09f, 0.12f, 1f);
        [SerializeField] private Color _reachableTileColor = new Color(0.1f, 0.45f, 0.65f, 0.85f);
        [SerializeField] private Color _hoverTileColor = new Color(0.0f, 0.85f, 1.0f, 0.95f);
        [SerializeField] private Color _targetTileColor = new Color(0.9f, 0.2f, 0.25f, 0.95f);
        [SerializeField] private Color _pathTileColor = new Color(0.2f, 0.9f, 0.5f, 0.9f);

        private readonly Dictionary<HexCoordinates, MeshRenderer> _tileRenderers = new();
        private readonly Dictionary<HexCoordinates, GameObject> _obstacleObjects = new();
        private readonly HashSet<HexCoordinates> _currentReachable = new();
        private readonly List<HexCoordinates> _currentPath = new();

        private HexCoordinates? _currentHovered;
        private HexCoordinates? _currentTarget;
        private Transform _tilesParent;
        private Transform _obstaclesParent;
        private LineRenderer _pathLineRenderer;

        private Material _baseMaterial;
        private MaterialPropertyBlock _propBlock;

        public TacticalHexGrid Grid => _grid;

        private void Awake()
        {
            _propBlock = new MaterialPropertyBlock();
            InitializeShader();

            if (_grid == null)
            {
                _grid = GetComponent<TacticalHexGrid>() ?? FindAnyObjectByType<TacticalHexGrid>();
            }

            SetupPathLineRenderer();
        }

        private void Start()
        {
            BuildVisualGrid();
        }

        private void InitializeShader()
        {
            // Méthode fiable : extraire le matériau par défaut d'une primitive Unity.
            // Cela garantit un shader URP valide même si Shader.Find() échoue au runtime.
            var tempPrimitive = GameObject.CreatePrimitive(PrimitiveType.Quad);
            tempPrimitive.hideFlags = HideFlags.HideAndDontSave;
            var defaultMat = tempPrimitive.GetComponent<MeshRenderer>().sharedMaterial;
            _baseMaterial = new Material(defaultMat) { name = "HexGrid_Mat_Runtime" };
            DestroyImmediate(tempPrimitive);
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

            // Nettoyage préalable
            if (_tilesParent != null) Destroy(_tilesParent.gameObject);
            if (_obstaclesParent != null) Destroy(_obstaclesParent.gameObject);
            _tileRenderers.Clear();
            _obstacleObjects.Clear();

            _tilesParent = new GameObject("HexTiles").transform;
            _tilesParent.SetParent(transform, false);

            _obstaclesParent = new GameObject("HexObstacles").transform;
            _obstaclesParent.SetParent(transform, false);

            Mesh sharedHexMesh = GeneratePointyHexMesh(_grid.HexRadius * 0.95f, 0.08f);

            foreach (var kvp in _grid.Nodes)
            {
                var coords = kvp.Key;
                var node = kvp.Value;

                var tileObj = new GameObject($"Tile_{coords.Q}_{coords.R}");
                tileObj.transform.SetParent(_tilesParent, false);
                tileObj.transform.position = node.WorldPosition;

                var mf = tileObj.AddComponent<MeshFilter>();
                mf.sharedMesh = sharedHexMesh;

                var mr = tileObj.AddComponent<MeshRenderer>();
                mr.sharedMaterial = _baseMaterial;
                _tileRenderers[coords] = mr;

                // Ajouter un MeshCollider pour le clic souris et le raycasting
                var col = tileObj.AddComponent<MeshCollider>();
                col.sharedMesh = sharedHexMesh;

                // Créer l'obstacle si nécessaire
                UpdateObstacleVisual(coords, node);
            }

            RefreshAllTileColors();
        }

        public void RefreshObstacles()
        {
            if (_grid == null) return;
            foreach (var kvp in _grid.Nodes)
            {
                UpdateObstacleVisual(kvp.Key, kvp.Value);
            }
            RefreshAllTileColors();
        }

        private void UpdateObstacleVisual(HexCoordinates coords, HexNode node)
        {
            if (_obstacleObjects.TryGetValue(coords, out var existing))
            {
                Destroy(existing);
                _obstacleObjects.Remove(coords);
            }

            if (node.Cover == CoverType.Half)
            {
                var halfCover = GameObject.CreatePrimitive(PrimitiveType.Cube);
                halfCover.name = $"CoverHalf_{coords.Q}_{coords.R}";
                halfCover.transform.SetParent(_obstaclesParent, false);
                halfCover.transform.position = node.WorldPosition + Vector3.up * 0.35f;
                halfCover.transform.localScale = new Vector3(_grid.HexRadius * 0.8f, 0.7f, _grid.HexRadius * 0.4f);

                var rend = halfCover.GetComponent<MeshRenderer>();
                // Garder le matériau URP par défaut de la primitive, appliquer la couleur via PropertyBlock
                _propBlock.SetColor("_BaseColor", new Color(0.75f, 0.55f, 0.15f)); // Ambre / Caisse tactique
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
                // Garder le matériau URP par défaut de la primitive
                _propBlock.SetColor("_BaseColor", new Color(0.25f, 0.28f, 0.35f)); // Pilier de béton renforcé
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

                Color c = _defaultTileColor;

                var node = _grid.GetNode(coords);
                if (node != null && !node.IsWalkable)
                {
                    c = new Color(0.18f, 0.1f, 0.1f, 1f); // Infranchissable
                }
                else if (_currentHovered.HasValue && _currentHovered.Value.Equals(coords))
                {
                    c = _hoverTileColor;
                }
                else if (_currentTarget.HasValue && _currentTarget.Value.Equals(coords))
                {
                    c = _targetTileColor;
                }
                else if (_currentPath.Contains(coords))
                {
                    c = _pathTileColor;
                }
                else if (_currentReachable.Contains(coords))
                {
                    c = _reachableTileColor;
                }

                _propBlock.SetColor("_BaseColor", c);
                _propBlock.SetColor("_Color", c);
                mr.SetPropertyBlock(_propBlock);
            }
        }

        /// <summary>
        /// Génère un maillage d'hexagone pointu (Pointy-Topped) avec biseau supérieur.
        /// </summary>
        public static Mesh GeneratePointyHexMesh(float radius, float height)
        {
            var mesh = new Mesh { name = "ProceduralPointyHex" };

            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var normals = new List<Vector3>();

            // Centre supérieur
            Vector3 centerTop = new Vector3(0, height * 0.5f, 0);
            vertices.Add(centerTop);
            normals.Add(Vector3.up);

            // 6 coins supérieurs
            for (int i = 0; i < 6; i++)
            {
                float angle = (30f + 60f * i) * Mathf.Deg2Rad;
                float x = radius * Mathf.Cos(angle);
                float z = radius * Mathf.Sin(angle);
                vertices.Add(new Vector3(x, height * 0.5f, z));
                normals.Add(Vector3.up);
            }

            // Triangles supérieurs
            for (int i = 1; i <= 6; i++)
            {
                int next = (i == 6) ? 1 : i + 1;
                triangles.Add(0);
                triangles.Add(i);
                triangles.Add(next);
            }

            // Centre inférieur
            int centerBottomIndex = vertices.Count;
            Vector3 centerBottom = new Vector3(0, -height * 0.5f, 0);
            vertices.Add(centerBottom);
            normals.Add(Vector3.down);

            // 6 coins inférieurs
            int bottomStartIndex = vertices.Count;
            for (int i = 0; i < 6; i++)
            {
                float angle = (30f + 60f * i) * Mathf.Deg2Rad;
                float x = radius * Mathf.Cos(angle);
                float z = radius * Mathf.Sin(angle);
                vertices.Add(new Vector3(x, -height * 0.5f, z));
                normals.Add(Vector3.down);
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

            // Flancs latéraux
            for (int i = 0; i < 6; i++)
            {
                int topCurr = 1 + i;
                int topNext = 1 + ((i + 1) % 6);
                int botCurr = bottomStartIndex + i;
                int botNext = bottomStartIndex + ((i + 1) % 6);

                // Triangle 1
                triangles.Add(topCurr);
                triangles.Add(botCurr);
                triangles.Add(topNext);

                // Triangle 2
                triangles.Add(topNext);
                triangles.Add(botCurr);
                triangles.Add(botNext);
            }

            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetNormals(normals);
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();

            return mesh;
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics.Units;

namespace Killtime.Tactics.Grid
{
    /// <summary>
    /// Gestionnaire de la grille hexagonale 3D pour le combat tactique.
    /// </summary>
    public class TacticalHexGrid : MonoBehaviour
    {
        [Header("Paramètres de la Grille")]
        [SerializeField] private int _gridRadius = 8;
        [SerializeField] private float _hexRadius = 1.0f;
        [SerializeField] private float _gridHeight = 0.05f;
        [SerializeField] private float _ceilingHeight = 3.5f;

        private readonly Dictionary<HexCoordinates, HexNode> _nodes = new();

        public IReadOnlyDictionary<HexCoordinates, HexNode> Nodes => _nodes;
        public float HexRadius => _hexRadius;
        public int GridRadius => _gridRadius;
        public float CeilingHeight
        {
            get => _ceilingHeight;
            set => _ceilingHeight = value;
        }

        public void SetNodeCeiling(HexCoordinates coords, bool hasCeiling)
        {
            if (_nodes.TryGetValue(coords, out var node))
            {
                node.HasCeiling = hasCeiling;
            }
        }

        public void SetAllCeilings(bool hasCeiling)
        {
            foreach (var node in _nodes.Values)
            {
                node.HasCeiling = hasCeiling;
            }
        }

        public void SetNodeCover(HexCoordinates coords, CoverType cover, bool isWalkable = true)
        {
            if (_nodes.TryGetValue(coords, out var node))
            {
                node.Cover = cover;
                node.IsWalkable = isWalkable;
            }
        }

        public void ClearAllCovers()
        {
            foreach (var node in _nodes.Values)
            {
                node.Cover = CoverType.None;
                node.IsWalkable = true;
            }
        }

        public void SetNodeElevation(HexCoordinates coords, float elevation)
        {
            if (_nodes.TryGetValue(coords, out var node))
            {
                node.Elevation = elevation;
                node.WorldPosition = coords.ToWorldPosition(_hexRadius, _gridHeight + elevation);
            }
        }

        public void SetNodeGroundTexture(HexCoordinates coords, string textureName)
        {
            if (_nodes.TryGetValue(coords, out var node))
            {
                node.GroundTexture = textureName ?? "";
            }
        }

        public void ResetGridState()
        {
            foreach (var node in _nodes.Values)
            {
                node.Cover = CoverType.None;
                node.IsWalkable = true;
                node.IsOccupied = false;
                node.HasCeiling = false;
                node.HasCustomVisual = false;
                node.Elevation = 0.0f;
                node.GroundTexture = "";
                node.WorldPosition = node.Coordinates.ToWorldPosition(_hexRadius, _gridHeight);
            }
        }

        /// <summary>
        /// Libère uniquement les marqueurs d'occupation (1 case = 1 avatar).
        /// À appeler avant tout (re)placement d'unités pour éviter les drapeaux
        /// fantômes laissés par des Destroy() différés à la fin de frame.
        /// </summary>
        public void ClearOccupancy()
        {
            foreach (var node in _nodes.Values)
            {
                node.IsOccupied = false;
            }
        }

        /// <summary>
        /// Vrai si la case existe, est praticable et libre de tout avatar.
        /// Vérifie le drapeau logique et la présence physique d'une unité vivante.
        /// </summary>
        public bool IsCellFree(HexCoordinates coords)
        {
            if (!_nodes.TryGetValue(coords, out var node)) return false;
            if (!node.IsWalkable || node.IsOccupied) return false;

            var all = FindObjectsByType<TacticalUnit>();
            for (int i = 0; i < all.Length; i++)
            {
                var u = all[i];
                if (u != null && u.Stats != null && u.Stats.IsAlive && u.CurrentCoords.Equals(coords))
                {
                    node.IsOccupied = true;
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Cherche la case libre la plus proche (BFS en anneaux) pour éviter
        /// tout empilement d'avatars sur la même case.
        /// </summary>
        public bool TryFindNearestFreeCell(HexCoordinates origin, out HexCoordinates freeCoords, int maxRadius = 8, HashSet<HexCoordinates> extraReserved = null)
        {
            freeCoords = origin;
            if (IsCellFree(origin) && (extraReserved == null || !extraReserved.Contains(origin)))
            {
                return true;
            }

            var visited = new HashSet<HexCoordinates> { origin };
            var frontier = new Queue<HexCoordinates>();
            frontier.Enqueue(origin);

            int guard = 0;
            while (frontier.Count > 0 && guard++ < 2000)
            {
                var current = frontier.Dequeue();
                if (current.DistanceTo(origin) > maxRadius) continue;

                for (int dir = 0; dir < 6; dir++)
                {
                    var neighbor = current.GetNeighbor(dir);
                    if (!visited.Add(neighbor)) continue;
                    if (neighbor.DistanceTo(origin) > maxRadius) continue;

                    if (IsCellFree(neighbor) && (extraReserved == null || !extraReserved.Contains(neighbor)))
                    {
                        freeCoords = neighbor;
                        return true;
                    }

                    // On traverse même les cases occupées pour explorer l'anneau suivant,
                    // mais jamais les cases inexistantes ou non praticables.
                    if (_nodes.TryGetValue(neighbor, out var node) && node.IsWalkable)
                    {
                        frontier.Enqueue(neighbor);
                    }
                }
            }

            return false;
        }

        private void Awake()
        {
            GenerateGrid();
        }

        [ContextMenu("Générer la Grille")]
        public void GenerateGrid()
        {
            _nodes.Clear();

            for (int q = -_gridRadius; q <= _gridRadius; q++)
            {
                int r1 = Mathf.Max(-_gridRadius, -q - _gridRadius);
                int r2 = Mathf.Min(_gridRadius, -q + _gridRadius);

                for (int r = r1; r <= r2; r++)
                {
                    var coords = new HexCoordinates(q, r);
                    var worldPos = coords.ToWorldPosition(_hexRadius, _gridHeight);
                    _nodes[coords] = new HexNode(coords, worldPos);
                }
            }
        }

        public HexNode GetNode(HexCoordinates coords)
        {
            _nodes.TryGetValue(coords, out var node);
            return node;
        }

        public bool TryGetNodeAtWorldPosition(Vector3 worldPosition, out HexNode node)
        {
            // Conversion approx inverse du monde vers l'hexagone le plus proche
            float qFrac = (Mathf.Sqrt(3) / 3f * worldPosition.x - 1f / 3f * worldPosition.z) / _hexRadius;
            float rFrac = (2f / 3f * worldPosition.z) / _hexRadius;
            float sFrac = -qFrac - rFrac;

            int q = Mathf.RoundToInt(qFrac);
            int r = Mathf.RoundToInt(rFrac);
            int s = Mathf.RoundToInt(sFrac);

            float qDiff = Mathf.Abs(q - qFrac);
            float rDiff = Mathf.Abs(r - rFrac);
            float sDiff = Mathf.Abs(s - sFrac);

            if (qDiff > rDiff && qDiff > sDiff)
            {
                q = -r - s;
            }
            else if (rDiff > sDiff)
            {
                r = -q - s;
            }

            return _nodes.TryGetValue(new HexCoordinates(q, r), out node);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1.0f, 0.4f);
            foreach (var kvp in _nodes)
            {
                var center = kvp.Value.WorldPosition;
                Gizmos.DrawWireSphere(center, _hexRadius * 0.4f);
            }
        }
    }
}

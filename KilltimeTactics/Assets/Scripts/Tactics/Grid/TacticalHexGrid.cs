using System.Collections.Generic;
using UnityEngine;

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

        private readonly Dictionary<HexCoordinates, HexNode> _nodes = new();

        public IReadOnlyDictionary<HexCoordinates, HexNode> Nodes => _nodes;
        public float HexRadius => _hexRadius;
        public int GridRadius => _gridRadius;

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

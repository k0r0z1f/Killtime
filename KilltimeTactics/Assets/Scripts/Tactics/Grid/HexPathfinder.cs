using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics.Units;

namespace Killtime.Tactics.Grid
{
    /// <summary>
    /// Algorithme de recherche de chemin A* adapté à la dépense des Points d'Action (PA).
    /// </summary>
    public class HexPathfinder
    {
        private readonly TacticalHexGrid _grid;

        public HexPathfinder(TacticalHexGrid grid)
        {
            _grid = grid;
        }

        /// <summary>
        /// Calcule le chemin le plus économe en PA entre deux coordonnées hexagonales.
        /// </summary>
        public List<HexCoordinates> FindPath(HexCoordinates start, HexCoordinates target, int availableAP, out int totalAPCost, TitanFootprintType footprint = TitanFootprintType.Single)
        {
            totalAPCost = 0;
            var path = new List<HexCoordinates>();

            var startNode = _grid.GetNode(start);
            var targetNode = _grid.GetNode(target);

            if (startNode == null || targetNode == null || !targetNode.IsWalkable)
            {
                return path;
            }

            var occupiedCoords = new HashSet<HexCoordinates>();
            var allUnits = Object.FindObjectsByType<TacticalUnit>();
            for (int i = 0; i < allUnits.Length; i++)
            {
                var u = allUnits[i];
                if (u != null && u.Stats != null && u.Stats.IsAlive && !u.Occupies(start))
                {
                    var uCoords = u.OccupiedCoords;
                    for (int c = 0; c < uCoords.Count; c++)
                    {
                        occupiedCoords.Add(uCoords[c]);
                    }
                }
            }

            if (!start.Equals(target))
            {
                var targetFootprintCells = TitanFootprint.GetOccupiedCoordinates(target, footprint);
                for (int i = 0; i < targetFootprintCells.Count; i++)
                {
                    var tc = targetFootprintCells[i];
                    var tNode = _grid.GetNode(tc);
                    if (tNode == null || !tNode.IsWalkable || tNode.IsOccupied || occupiedCoords.Contains(tc))
                    {
                        return path;
                    }
                }
            }

            var openSet = new List<HexCoordinates> { start };
            var cameFrom = new Dictionary<HexCoordinates, HexCoordinates>();
            var gScore = new Dictionary<HexCoordinates, int> { [start] = 0 };
            var fScore = new Dictionary<HexCoordinates, int> { [start] = start.DistanceTo(target) };

            while (openSet.Count > 0)
            {
                // Trouver le nœud avec le plus faible fScore
                HexCoordinates current = openSet[0];
                int lowestF = fScore.GetValueOrDefault(current, int.MaxValue);

                for (int i = 1; i < openSet.Count; i++)
                {
                    int f = fScore.GetValueOrDefault(openSet[i], int.MaxValue);
                    if (f < lowestF)
                    {
                        current = openSet[i];
                        lowestF = f;
                    }
                }

                if (current.Equals(target))
                {
                    // Reconstitution du chemin
                    path.Add(current);
                    while (cameFrom.ContainsKey(current))
                    {
                        current = cameFrom[current];
                        path.Insert(0, current);
                    }

                    totalAPCost = gScore[target];
                    return (totalAPCost <= availableAP) ? path : new List<HexCoordinates>();
                }

                openSet.Remove(current);

                for (int dir = 0; dir < 6; dir++)
                {
                    var neighbor = current.GetNeighbor(dir);
                    var neighborNode = _grid.GetNode(neighbor);

                    if (neighborNode == null || !neighborNode.IsWalkable)
                    {
                        continue;
                    }

                    bool footprintBlocked = false;
                    var footprintCells = TitanFootprint.GetOccupiedCoordinates(neighbor, footprint);
                    for (int fc = 0; fc < footprintCells.Count; fc++)
                    {
                        var cell = footprintCells[fc];
                        var n = _grid.GetNode(cell);
                        if (n == null || !n.IsWalkable || (n.IsOccupied && occupiedCoords.Contains(cell)))
                        {
                            footprintBlocked = true;
                            break;
                        }
                    }
                    if (footprintBlocked) continue;

                    int tentativeG = gScore[current] + neighborNode.ActionPointCost;

                    if (tentativeG > availableAP)
                    {
                        // Dépasse la réserve de PA actuelle
                        continue;
                    }

                    if (tentativeG < gScore.GetValueOrDefault(neighbor, int.MaxValue))
                    {
                        cameFrom[neighbor] = current;
                        gScore[neighbor] = tentativeG;
                        fScore[neighbor] = tentativeG + neighbor.DistanceTo(target);

                        if (!openSet.Contains(neighbor))
                        {
                            openSet.Add(neighbor);
                        }
                    }
                }
            }

            return path; // Chemin introuvable ou trop coûteux en PA
        }

        /// <summary>
        /// Renvoie l'ensemble de toutes les cellules atteignables avec la réserve de PA courante.
        /// </summary>
        public HashSet<HexCoordinates> GetReachableCoordinates(HexCoordinates center, int maxAP)
        {
            var reachable = new HashSet<HexCoordinates>();
            var costSoFar = new Dictionary<HexCoordinates, int> { [center] = 0 };
            var frontier = new Queue<HexCoordinates>();
            frontier.Enqueue(center);

            var occupiedCoords = new HashSet<HexCoordinates>();
            var allUnits = Object.FindObjectsByType<TacticalUnit>();
            for (int i = 0; i < allUnits.Length; i++)
            {
                var u = allUnits[i];
                if (u != null && u.Stats != null && u.Stats.IsAlive && !u.CurrentCoords.Equals(center))
                {
                    occupiedCoords.Add(u.CurrentCoords);
                }
            }

            while (frontier.Count > 0)
            {
                var current = frontier.Dequeue();
                int currentCost = costSoFar[current];

                for (int dir = 0; dir < 6; dir++)
                {
                    var neighbor = current.GetNeighbor(dir);
                    var node = _grid.GetNode(neighbor);

                    if (node == null || !node.IsWalkable || node.IsOccupied || occupiedCoords.Contains(neighbor)) continue;

                    int newCost = currentCost + node.ActionPointCost;
                    if (newCost <= maxAP && (!costSoFar.ContainsKey(neighbor) || newCost < costSoFar[neighbor]))
                    {
                        costSoFar[neighbor] = newCost;
                        reachable.Add(neighbor);
                        frontier.Enqueue(neighbor);
                    }
                }
            }

            return reachable;
        }
    }
}

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
        /// allowPassThroughOccupied (exploration) : on peut traverser les cases
        /// occupées pour passer de l'autre côté, mais jamais s'y arrêter
        /// (la destination doit toujours être libre).
        /// </summary>
        public List<HexCoordinates> FindPath(HexCoordinates start, HexCoordinates target, int availableAP, out int totalAPCost, TitanFootprintType footprint = TitanFootprintType.Single, bool allowPassThroughOccupied = false)
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
                        // Comme le contrôle de destination et les validateurs
                        // runtime : drapeau OU présence physique (un drapeau
                        // périmé ne doit jamais autoriser la traversée).
                        // Sauf faufile exploration : traverser oui, s'arrêter non.
                        if (n == null || !n.IsWalkable
                            || (!allowPassThroughOccupied && (n.IsOccupied || occupiedCoords.Contains(cell))))
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
        /// allowPassThroughOccupied (exploration) : les cases occupées ne sont
        /// pas des arrêts valides mais on peut les traverser pour aller plus loin.
        /// </summary>
        public HashSet<HexCoordinates> GetReachableCoordinates(HexCoordinates center, int maxAP, bool allowPassThroughOccupied = false)
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

                    if (node == null || !node.IsWalkable) continue;

                    bool occupied = node.IsOccupied || occupiedCoords.Contains(neighbor);
                    if (occupied && !allowPassThroughOccupied) continue;

                    int newCost = currentCost + node.ActionPointCost;
                    if (newCost <= maxAP && (!costSoFar.ContainsKey(neighbor) || newCost < costSoFar[neighbor]))
                    {
                        costSoFar[neighbor] = newCost;
                        // Faufile : on traverse mais on ne s'y arrête pas.
                        if (!occupied) reachable.Add(neighbor);
                        frontier.Enqueue(neighbor);
                    }
                }
            }

            return reachable;
        }

        /// <summary>
        /// Vrai si le chemin constitue une Charge / Sprint valide (RD-038) :
        /// longueur du trajet continu >= distance minimale (3 cases par défaut).
        /// </summary>
        public static bool IsChargePath(List<HexCoordinates> path, int minDistance = 3)
        {
            return path != null && (path.Count - 1) >= minDistance;
        }

        /// <summary>
        /// Vrai si la distance géométrique directe entre deux coordonnées permet une charge (>= minDistance).
        /// </summary>
        public static bool IsChargeDistance(HexCoordinates from, HexCoordinates to, int minDistance = 3)
        {
            return from.DistanceTo(to) >= minDistance;
        }

        /// <summary>
        /// Recherche le meilleur chemin d'assaut (RD-038 Charge) vers une cible adverse :
        /// - Atterrit sur une case libre adjacente à targetUnitCoords.
        /// - Longueur du trajet >= minDistance (3 cases par défaut pour conférer +2 dégâts et -1 défense).
        /// - Coût en PA de déplacement + coût d'attaque <= availableAP.
        /// </summary>
        public List<HexCoordinates> FindChargePath(
            HexCoordinates start,
            HexCoordinates targetUnitCoords,
            int availableAP,
            int attackCost,
            out int totalMovementCost,
            int minDistance = 3,
            TitanFootprintType footprint = TitanFootprintType.Single,
            bool allowPassThroughOccupied = false)
        {
            totalMovementCost = 0;
            List<HexCoordinates> bestPath = new List<HexCoordinates>();
            int bestMoveCost = int.MaxValue;

            if (_grid == null) return bestPath;
            int maxMoveAP = availableAP - attackCost;
            if (maxMoveAP < minDistance) return bestPath;

            for (int dir = 0; dir < 6; dir++)
            {
                var candidate = targetUnitCoords.GetNeighbor(dir);
                var node = _grid.GetNode(candidate);
                if (node == null || !node.IsWalkable) continue;

                // Si la case candidate est le point de départ, ce n'est pas une charge (distance 0)
                if (candidate.Equals(start)) continue;

                var path = FindPath(start, candidate, maxMoveAP, out int moveCost, footprint, allowPassThroughOccupied);
                if (path != null && path.Count > 0 && (path.Count - 1) >= minDistance)
                {
                    if (moveCost < bestMoveCost)
                    {
                        bestMoveCost = moveCost;
                        bestPath = path;
                    }
                }
            }

            if (bestPath.Count > 0)
            {
                totalMovementCost = bestMoveCost;
            }
            return bestPath;
        }

        /// <summary>
        /// Renvoie l'ensemble des coordonnées atteignables à distance de charge (>= minDistance)
        /// pour une réserve de PA donnée.
        /// </summary>
        public HashSet<HexCoordinates> GetChargeReachableCoordinates(
            HexCoordinates center,
            int maxAP,
            int minDistance = 3,
            bool allowPassThroughOccupied = false)
        {
            var reachable = GetReachableCoordinates(center, maxAP, allowPassThroughOccupied);
            reachable.RemoveWhere(coord => center.DistanceTo(coord) < minDistance);
            return reachable;
        }

        /// <summary>
        /// Vérifie si l'acteur est en mesure d'exécuter une charge complète sur la cible
        /// (distance >= minDistance, case d'arrêt libre au contact, budget PA suffisant pour déplacement + frappe).
        /// </summary>
        public bool CanChargeTarget(
            TacticalUnit actor,
            TacticalUnit target,
            int minDistance = 3,
            int attackCost = 2)
        {
            if (actor == null || target == null || actor == target) return false;
            if (actor.Stats == null || !actor.Stats.IsAlive) return false;
            if (target.Stats == null || !target.Stats.IsAlive) return false;

            int ap = actor.Stats.CurrentActionPoints;
            if (ap < attackCost + minDistance) return false;

            var path = FindChargePath(actor.CurrentCoords, target.CurrentCoords, ap, attackCost, out _, minDistance, actor.FootprintType);
            return path != null && path.Count > 0;
        }
    }
}

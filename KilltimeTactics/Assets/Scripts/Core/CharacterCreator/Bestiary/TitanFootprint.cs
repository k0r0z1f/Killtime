using System.Collections.Generic;
using UnityEngine;

namespace Killtime.Tactics.Grid
{
    public enum TitanFootprintType
    {
        Single = 0,
        Rosette7 = 1,
        Triangle3 = 2,
        Colossus19 = 3
    }

    /// <summary>
    /// Gestion mathématique des empreintes multi-hexagonales axiales (Livres X & XI).
    /// </summary>
    public static class TitanFootprint
    {
        private static readonly HexCoordinates[] RosetteOffsets = new HexCoordinates[]
        {
            new HexCoordinates(0, 0),
            new HexCoordinates(1, 0),
            new HexCoordinates(1, -1),
            new HexCoordinates(0, -1),
            new HexCoordinates(-1, 0),
            new HexCoordinates(-1, 1),
            new HexCoordinates(0, 1)
        };

        private static readonly HexCoordinates[] TriangleOffsets = new HexCoordinates[]
        {
            new HexCoordinates(0, 0),
            new HexCoordinates(1, 0),
            new HexCoordinates(0, 1)
        };

        private static readonly List<HexCoordinates> Colossus19Offsets = BuildColossus19Offsets();

        private static List<HexCoordinates> BuildColossus19Offsets()
        {
            var list = new List<HexCoordinates>();
            for (int q = -2; q <= 2; q++)
            {
                int r1 = Mathf.Max(-2, -q - 2);
                int r2 = Mathf.Min(2, -q + 2);
                for (int r = r1; r <= r2; r++)
                {
                    list.Add(new HexCoordinates(q, r));
                }
            }
            return list;
        }

        public static List<HexCoordinates> GetOccupiedCoordinates(HexCoordinates anchor, TitanFootprintType type)
        {
            var result = new List<HexCoordinates>();
            switch (type)
            {
                case TitanFootprintType.Rosette7:
                    for (int i = 0; i < RosetteOffsets.Length; i++)
                        result.Add(new HexCoordinates(anchor.Q + RosetteOffsets[i].Q, anchor.R + RosetteOffsets[i].R));
                    break;

                case TitanFootprintType.Triangle3:
                    for (int i = 0; i < TriangleOffsets.Length; i++)
                        result.Add(new HexCoordinates(anchor.Q + TriangleOffsets[i].Q, anchor.R + TriangleOffsets[i].R));
                    break;

                case TitanFootprintType.Colossus19:
                    for (int i = 0; i < Colossus19Offsets.Count; i++)
                        result.Add(new HexCoordinates(anchor.Q + Colossus19Offsets[i].Q, anchor.R + Colossus19Offsets[i].R));
                    break;

                case TitanFootprintType.Single:
                default:
                    result.Add(anchor);
                    break;
            }
            return result;
        }

        public static int MinDistance(HexCoordinates fromPos, HexCoordinates targetAnchor, TitanFootprintType targetFootprint)
        {
            if (targetFootprint == TitanFootprintType.Single)
                return fromPos.DistanceTo(targetAnchor);

            var cells = GetOccupiedCoordinates(targetAnchor, targetFootprint);
            int min = int.MaxValue;
            for (int i = 0; i < cells.Count; i++)
            {
                int d = fromPos.DistanceTo(cells[i]);
                if (d < min) min = d;
            }
            return min;
        }

        public static int MinDistanceBetweenUnits(HexCoordinates aAnchor, TitanFootprintType aType, HexCoordinates bAnchor, TitanFootprintType bType)
        {
            if (aType == TitanFootprintType.Single && bType == TitanFootprintType.Single)
                return aAnchor.DistanceTo(bAnchor);

            var aCells = GetOccupiedCoordinates(aAnchor, aType);
            var bCells = GetOccupiedCoordinates(bAnchor, bType);

            int min = int.MaxValue;
            for (int i = 0; i < aCells.Count; i++)
            {
                for (int j = 0; j < bCells.Count; j++)
                {
                    int d = aCells[i].DistanceTo(bCells[j]);
                    if (d < min) min = d;
                }
            }
            return min;
        }

        public static HexCoordinates GetClosestCell(HexCoordinates fromPos, HexCoordinates targetAnchor, TitanFootprintType targetFootprint)
        {
            if (targetFootprint == TitanFootprintType.Single)
                return targetAnchor;

            var cells = GetOccupiedCoordinates(targetAnchor, targetFootprint);
            HexCoordinates best = targetAnchor;
            int min = int.MaxValue;
            for (int i = 0; i < cells.Count; i++)
            {
                int d = fromPos.DistanceTo(cells[i]);
                if (d < min)
                {
                    min = d;
                    best = cells[i];
                }
            }
            return best;
        }

        public static int GetHexCount(TitanFootprintType type)
        {
            return type switch
            {
                TitanFootprintType.Single => 1,
                TitanFootprintType.Triangle3 => 3,
                TitanFootprintType.Rosette7 => 7,
                TitanFootprintType.Colossus19 => 19,
                _ => 1
            };
        }

        public static bool IsTitan(TitanFootprintType type)
        {
            return type == TitanFootprintType.Rosette7 || type == TitanFootprintType.Colossus19;
        }

        public static List<HexCoordinates> GetPerimeterCoordinates(HexCoordinates anchor, TitanFootprintType type)
        {
            var occupied = GetOccupiedCoordinates(anchor, type);
            var occupiedSet = new HashSet<HexCoordinates>(occupied);
            var perimeterSet = new HashSet<HexCoordinates>();

            for (int i = 0; i < occupied.Count; i++)
            {
                var cell = occupied[i];
                for (int d = 0; d < 6; d++)
                {
                    var neighbor = cell.GetNeighbor(d);
                    if (!occupiedSet.Contains(neighbor))
                    {
                        perimeterSet.Add(neighbor);
                    }
                }
            }

            return new List<HexCoordinates>(perimeterSet);
        }

        public static bool IsAdjacentToFootprint(HexCoordinates target, HexCoordinates anchor, TitanFootprintType type)
        {
            return MinDistance(target, anchor, type) <= 1;
        }

        public static List<HexCoordinates> GetStompZone(HexCoordinates impactCenter, TitanFootprintType stompType)
        {
            return GetOccupiedCoordinates(impactCenter, stompType);
        }

        public static List<HexCoordinates> GetBreathZone(HexCoordinates targetCenter, TitanFootprintType breathType)
        {
            return GetOccupiedCoordinates(targetCenter, breathType);
        }

        public static List<HexCoordinates> GetBreathConeCoordinates(HexCoordinates origin, int directionIndex, int length = 3)
        {
            var results = new HashSet<HexCoordinates>();
            int dir = ((directionIndex % 6) + 6) % 6;
            var fwd = HexCoordinates.Directions[dir];
            var leftDir = HexCoordinates.Directions[(dir + 4) % 6];
            var rightDir = HexCoordinates.Directions[(dir + 2) % 6];

            HexCoordinates currentCenter = origin;
            for (int step = 1; step <= length; step++)
            {
                currentCenter = new HexCoordinates(currentCenter.Q + fwd.Q, currentCenter.R + fwd.R);
                results.Add(currentCenter);

                HexCoordinates leftSpread = currentCenter;
                HexCoordinates rightSpread = currentCenter;
                for (int w = 1; w < step; w++)
                {
                    leftSpread = new HexCoordinates(leftSpread.Q + leftDir.Q, leftSpread.R + leftDir.R);
                    rightSpread = new HexCoordinates(rightSpread.Q + rightDir.Q, rightSpread.R + rightDir.R);
                    results.Add(leftSpread);
                    results.Add(rightSpread);
                }
            }

            return new List<HexCoordinates>(results);
        }

        public static List<HexCoordinates> GetSweptCoordinates(IEnumerable<HexCoordinates> path, TitanFootprintType type)
        {
            var swept = new HashSet<HexCoordinates>();
            if (path == null) return new List<HexCoordinates>();

            foreach (var step in path)
            {
                var cells = GetOccupiedCoordinates(step, type);
                for (int i = 0; i < cells.Count; i++)
                {
                    swept.Add(cells[i]);
                }
            }

            return new List<HexCoordinates>(swept);
        }

        public static bool CanTitanGrab(TitanFootprintType titanFootprint, TitanFootprintType targetFootprint, int minDistance)
        {
            if (!IsTitan(titanFootprint)) return false;
            if (minDistance > 1) return false;
            return GetHexCount(targetFootprint) < GetHexCount(titanFootprint);
        }
    }
}
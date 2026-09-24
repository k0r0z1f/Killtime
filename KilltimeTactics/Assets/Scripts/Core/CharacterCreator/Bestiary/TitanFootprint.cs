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
    }
}
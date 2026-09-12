using System;
using UnityEngine;

namespace Killtime.Tactics.Grid
{
    public enum CoverType
    {
        None = 0,
        Half = 1, // Déviation partielle / bonus de défense
        Full = 2  // Bloque la ligne de mire / tir impossible
    }

    [Serializable]
    public class HexNode
    {
        public HexCoordinates Coordinates;
        public Vector3 WorldPosition;
        public bool IsWalkable = true;
        public int ActionPointCost = 1; // 1 PA par défaut pour franchir une case
        public CoverType Cover = CoverType.None;
        public bool IsOccupied = false;
        public bool HasCustomVisual = false;
        public bool HasCeiling = false;

        public HexNode(HexCoordinates coords, Vector3 worldPos)
        {
            Coordinates = coords;
            WorldPosition = worldPos;
            IsWalkable = true;
            ActionPointCost = 1;
            Cover = CoverType.None;
            IsOccupied = false;
            HasCeiling = false;
        }
    }
}

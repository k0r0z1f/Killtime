using System;
using UnityEngine;

namespace Killtime.Tactics.Grid
{
    /// <summary>
    /// Coordonnées axiales et cubiques pour la grille hexagonale (Tradition Fallout 1 & 2).
    /// </summary>
    [Serializable]
    public struct HexCoordinates : IEquatable<HexCoordinates>
    {
        [SerializeField] private int _q;
        [SerializeField] private int _r;

        public int Q => _q;
        public int R => _r;
        public int S => -_q - _r; // q + r + s = 0

        public HexCoordinates(int q, int r)
        {
            _q = q;
            _r = r;
        }

        public static HexCoordinates FromOffset(int x, int z)
        {
            return new HexCoordinates(x - (z - (z & 1)) / 2, z);
        }

        public int DistanceTo(HexCoordinates other)
        {
            return (Math.Abs(Q - other.Q) + Math.Abs(R - other.R) + Math.Abs(S - other.S)) / 2;
        }

        /// <summary>
        /// Convertit les coordonnées hexagonales en coordonnées 3D mondiales (plan XZ).
        /// Rayon extérieur de l'hexagone = 1.0f par défaut.
        /// </summary>
        public Vector3 ToWorldPosition(float radius = 1.0f, float yOffset = 0.0f)
        {
            float x = radius * (Mathf.Sqrt(3) * Q + Mathf.Sqrt(3) / 2f * R);
            float z = radius * (3f / 2f * R);
            return new Vector3(x, yOffset, z);
        }

        public static readonly HexCoordinates[] Directions = new HexCoordinates[]
        {
            new HexCoordinates(1, 0),
            new HexCoordinates(1, -1),
            new HexCoordinates(0, -1),
            new HexCoordinates(-1, 0),
            new HexCoordinates(-1, 1),
            new HexCoordinates(0, 1)
        };

        public HexCoordinates GetNeighbor(int directionIndex)
        {
            var dir = Directions[directionIndex % 6];
            return new HexCoordinates(Q + dir.Q, R + dir.R);
        }

        public bool Equals(HexCoordinates other) => _q == other._q && _r == other._r;
        public override bool Equals(object obj) => obj is HexCoordinates other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(_q, _r);
        public override string ToString() => $"Hex({Q}, {R})";
    }
}

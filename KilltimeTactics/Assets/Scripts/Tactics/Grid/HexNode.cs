using System;
using UnityEngine;

namespace Killtime.Tactics.Grid
{
    /// <summary>
    /// Niveau d'exposition d'une cible vue par son attaquant (Livre VI §25.3).
    /// None = entièrement visible (0). Half = à moitié visible (-1 à l'attaque).
    /// ThreeQuarters = aux trois-quarts couvert, seul 1/4 visible (-2 à l'attaque).
    /// Full = totalement à couvert / non visible : attaque directe impossible.
    /// NOTE : Full garde volontairement la valeur 2 (compatibilité des cartes
    /// sauvegardées) ; ne jamais comparer par ordre numérique, utiliser CoverSystem.
    /// </summary>
    public enum CoverType
    {
        None = 0,
        Half = 1, // À moitié visible : -1 à l'attaque (muret, caisse basse)
        Full = 2,  // Non visible : attaque directe impossible (mur, pilier)
        ThreeQuarters = 3 // Aux 3/4 couvert : -2 à l'attaque (palissade haute, barricade)
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
        public float Elevation = 0.0f;
        public string GroundTexture = "";

        public HexNode(HexCoordinates coords, Vector3 worldPos)
        {
            Coordinates = coords;
            WorldPosition = worldPos;
            IsWalkable = true;
            ActionPointCost = 1;
            Cover = CoverType.None;
            IsOccupied = false;
            HasCeiling = false;
            Elevation = 0.0f;
            GroundTexture = "";
        }
    }
}

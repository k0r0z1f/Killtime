using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics.Grid;
using Killtime.Core.Character;

namespace Killtime.UI
{
    public enum MapBrushType
    {
        Inspect,
        ClearGround,
        HalfCover,
        FullCover,
        ImpassableHole,
        AddCeiling,
        RemoveCeiling,
        PlaceProp3D,
        EraseProp3D,
        PaintGroundTexture,
        ClearGroundTexture,
        ElevateGround,
        LowerGround,
        SetElevation,
        SmoothElevation,
        ThreeQuartersCover
    }

    public enum PropPlacementType
    {
        Plancher,
        Plafond,
        Mur,
        Flottant
    }

    [Serializable]
    public class MapTileData
    {
        public int Q;
        public int R;
        public CoverType Cover;
        public bool IsWalkable;
        public bool HasCeiling;
        public float Elevation;
        public string GroundTexture;
    }

    [Serializable]
    public class GroundTextureRecord
    {
        public string RelativePath;
        public string Category;
        public string DisplayName;
        public Texture2D Texture;
    }

    [Serializable]
    public class MapPropData
    {
        public string PrefabName;
        public int Q;
        public int R;
        public float RotationY;
        public float Scale = 1.0f;
        public CoverType Cover = CoverType.Full;
        public bool IsWalkable = false;
        public PropPlacementType PlacementType = PropPlacementType.Plancher;
        public float HeightOffset = 0.0f;
    }

    public class MapPropInstance : MonoBehaviour
    {
        public MapPropData Data;
    }

    [Serializable]
    public class MapUnitData
    {
        public string UnitId;
        public CharacterSheet Sheet;
        public int Q;
        public int R;
        public bool IsPlayer;
        public int currentHealth;
        public int currentAP;
        public int essoufflement;
        public int activeStatus;
        public List<string> statusEffects = new();
    }

    [Serializable]
    public class TacticalMapSaveData
    {
        public string MapName = "Nouvelle_Carte";
        public int GridRadius = 8;
        public float CeilingHeight = 3.5f;
        public string SaveTimestamp = "";
        public List<MapTileData> ModifiedTiles = new();
        public List<MapPropData> PlacedProps = new();
        public List<MapUnitData> PlacedUnits = new();
        public List<Vector3> PlacedPixies = new();
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Killtime.Tactics;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.UI;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteInEditMode]
public partial class AsteroidBaseGenerator : MonoBehaviour
{
    public enum RoomType
    {
        None,
        CommandCenter,
        CrewQuartersA,
        CrewQuartersB,
        ShipCargoBay,
        EngineeringRoom,
        ResourceStore,
        Corridor
    }

    public enum TileCategory
    {
        Floor,
        InteriorWall,
        RockBoundary,
        ShipBayGate
    }

    [System.Serializable]
    public struct AxialCoord
    {
        public int q;
        public int r;
        public int s;

        public AxialCoord(int q, int r)
        {
            this.q = q;
            this.r = r;
            this.s = -q - r;
        }

        public override bool Equals(object obj) => obj is AxialCoord other && q == other.q && r == other.r;
        public override int GetHashCode() => HashCode.Combine(q, r);
    }

    public class BaseNode : MonoBehaviour
    {
        public AxialCoord Coord;
        public RoomType Room;
        public TileCategory Category;
        public bool IsWalkable;
        public bool BlocksLoS;
    }

    [Header("Topologie Hexagonale")]
    [SerializeField] private float hexOuterRadius = 1.0f;
    [SerializeField] private float wallHeight = 4.2f;
    [SerializeField] private float floorThickness = 0.25f;
    [SerializeField] private bool generateCeilings = true;

    [Header("Morphologie Naturelle de l'Astéroïde")]
    [SerializeField] private float baseRadiusX = 42.0f;
    [SerializeField] private float baseRadiusZ = 36.0f;
    [SerializeField] private float baseRadiusY = 24.0f;
    [SerializeField] private float asteroidCenterOffsetY = 1.5f;
    [SerializeField] [Range(0.1f, 0.6f)] private float asymmetryPinch = 0.22f;
    [SerializeField] [Range(2f, 10f)] private float fbmAmplitude = 6.0f;
    [SerializeField] private int shellLatSegments = 32;
    [SerializeField] private int shellLonSegments = 48;
    [SerializeField] private int noiseSeed = 1775;

    [Header("Matériaux")]
    [SerializeField] private Material floorMaterial;
    [SerializeField] private Material wallMaterial;
    [SerializeField] private Material ceilingMaterial;
    [SerializeField] private Material rockMaterial;
    [SerializeField] private Material gateMaterial;

    [Header("Hiérarchie")]
    [SerializeField] private Transform interiorFloorContainer;
    [SerializeField] private Transform interiorWallsContainer;
    [SerializeField] private Transform interiorCeilingsContainer;
    [SerializeField] private Transform labelsContainer;
    [SerializeField] private Transform exteriorHullContainer;
    [SerializeField] private Transform spaceCosmosContainer;

    private readonly Dictionary<RoomType, (AxialCoord center, string displayName)> roomRegistry = new()
    {
        { RoomType.CommandCenter, (new AxialCoord(0, 0), "POSTE DE COMMANDEMENT") },
        { RoomType.ShipCargoBay, (new AxialCoord(12, 0), "HANGAR & CARGO BAY") },
        { RoomType.EngineeringRoom, (new AxialCoord(-11, 0), "SALLE DES MACHINES") },
        { RoomType.CrewQuartersA, (new AxialCoord(-4, -9), "QUARTIERS D'ÉQUIPAGE [A]") },
        { RoomType.CrewQuartersB, (new AxialCoord(6, -9), "QUARTIERS D'ÉQUIPAGE [B]") },
        { RoomType.ResourceStore, (new AxialCoord(-3, 10), "RÉSERVE DE VIVRES") }
    };

    [ContextMenu("Générer Base Astéroïde Complète")]
    public void GenerateBase()
    {
        ClearBase();

        // La base est authorée dans l'espace hex (HexToWorld) : forcer l'origine
        // pendant la génération, sinon les tuiles (positionnées en monde puis
        // reparentées) et les conteneurs héritent d'un décalage qui désaligne
        // ensuite le mobilier enfant (Placeholders ParentToEnvironment).
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        EnsureContainers();

        float targetCeilingOpacity = 0.22f;
        try
        {
            if (Killtime.UI.DevUIPreferences.Current != null)
                targetCeilingOpacity = Killtime.UI.DevUIPreferences.Current.CeilingOpacity;
        }
        catch { /* ignore */ }

        Material defaultFloorMat = floorMaterial != null ? floorMaterial : CreateDefaultMaterial(new Color(0.25f, 0.28f, 0.32f), 0.6f);
        Material defaultWallMat = wallMaterial != null ? wallMaterial : CreateDefaultMaterial(new Color(0.30f, 0.32f, 0.35f), 0.4f);
        Material defaultCeilingMat = ceilingMaterial != null ? ceilingMaterial : CreateTransparentCeilingMaterial(new Color(0.15f, 0.20f, 0.28f), targetCeilingOpacity);
        Material defaultRockMat = rockMaterial != null ? rockMaterial : CreateDefaultMaterial(new Color(0.38f, 0.35f, 0.33f), 0.15f);
        Material defaultGateMat = gateMaterial != null ? gateMaterial : CreateDefaultMaterial(new Color(0.92f, 0.55f, 0.1f), 0.2f, true);

        Mesh masterFloorMesh = GetOrCreatePersistentMesh(BuildSolidHexMesh(0.0f, -floorThickness), "Mesh_HexFloorSlab");
        Mesh masterCeilingMesh = GetOrCreatePersistentMesh(BuildSolidHexMesh(wallHeight, wallHeight + floorThickness, true), "Mesh_HexCeilingSlab");
        Mesh masterWallMesh = GetOrCreatePersistentMesh(BuildWallColumnMesh(wallHeight), "Mesh_HexWallColumn");
        Mesh masterRockWallMesh = GetOrCreatePersistentMesh(BuildWallColumnMesh(wallHeight * 1.35f), "Mesh_HexRockColumn");
        Mesh masterHullMesh = GetOrCreatePersistentMesh(BuildHullMesh(), "Mesh_AsteroidHull");

        Dictionary<AxialCoord, (RoomType room, TileCategory cat)> grid = GenerateRoomLayout();

        foreach (var kvp in grid)
        {
            AxialCoord coord = kvp.Key;
            RoomType room = kvp.Value.room;
            TileCategory cat = kvp.Value.cat;
            Vector3 worldPos = HexToWorld(coord);

            GameObject tileObj = new GameObject($"Hex_{room}_{coord.q}_{coord.r}");
            tileObj.transform.position = worldPos;

            switch (cat)
            {
                case TileCategory.Floor:
                    tileObj.transform.SetParent(interiorFloorContainer);
                    ApplyMeshRenderer(tileObj, masterFloorMesh, defaultFloorMat, GetRoomColor(room));

                    if (generateCeilings)
                    {
                        GameObject ceilObj = new GameObject($"Ceil_{room}_{coord.q}_{coord.r}");
                        ceilObj.transform.position = worldPos;
                        ceilObj.transform.SetParent(interiorCeilingsContainer);
                        ceilObj.layer = 2; // Layer 2 = Ignore Raycast (identique à HexGridVisualizer)

                        MeshFilter mfCeil = ceilObj.AddComponent<MeshFilter>();
                        mfCeil.sharedMesh = masterCeilingMesh;

                        MeshRenderer mrCeil = ceilObj.AddComponent<MeshRenderer>();

                        // Raccordement direct au matériau transparent natif du moteur
                        var visualizerComp = FindAnyObjectByType<Killtime.Tactics.Grid.HexGridVisualizer>();
                        mrCeil.sharedMaterial = (visualizerComp != null && visualizerComp.CeilingMaterial != null) 
                            ? visualizerComp.CeilingMaterial 
                            : defaultCeilingMat;

                        mrCeil.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                        mrCeil.receiveShadows = false;

                        var ceilingProp = ceilObj.GetComponent<TacticalCeilingProp>() ?? ceilObj.AddComponent<TacticalCeilingProp>();
                        ceilingProp.Initialize();

                        var gridComp = FindAnyObjectByType<Killtime.Tactics.Grid.TacticalHexGrid>();
                        if (gridComp != null)
                        {
                            gridComp.SetNodeCeiling(new Killtime.Tactics.Grid.HexCoordinates(coord.q, coord.r), true);
                        }
                    }
                    break;

                case TileCategory.ShipBayGate:
                    tileObj.transform.SetParent(interiorFloorContainer);
                    ApplyMeshRenderer(tileObj, masterFloorMesh, defaultGateMat, new Color(0.95f, 0.50f, 0.1f));
                    break;

                case TileCategory.InteriorWall:
                    tileObj.transform.SetParent(interiorWallsContainer);
                    ApplyMeshRenderer(tileObj, masterWallMesh, defaultWallMat, Color.white);
                    break;

                case TileCategory.RockBoundary:
                    tileObj.transform.SetParent(interiorWallsContainer);
                    ApplyMeshRenderer(tileObj, masterRockWallMesh, defaultRockMat, Color.white);
                    break;
            }
        }

        SpawnRoomLabels();
        InstantiateHullObject(masterHullMesh, defaultRockMat);
        BuildOrbitalDebris(defaultRockMat);

        var hexGrid = FindAnyObjectByType<Killtime.Tactics.Grid.TacticalHexGrid>();
        if (hexGrid != null)
        {
            hexGrid.CeilingHeight = wallHeight;
        }

        var visualizer = FindAnyObjectByType<Killtime.Tactics.Grid.HexGridVisualizer>();
        if (visualizer != null)
        {
            visualizer.RefreshObstacles();
        }
    }

    [ContextMenu("Sauvegarder Prefab dans Resources")]
    public void SaveToPrefab()
    {
#if UNITY_EDITOR
        // 1. Purge systématique des scripts manquants et des composants obsolètes
        PurgeMissingScripts(gameObject);

        // 2. Garantir un prefab centré : racine + conteneurs à zéro, sinon le
        // mobilier enfant (local, alignement hérité) est désaligné de ~12m
        // par rapport aux sols (ex: hangar).
        gameObject.transform.localPosition = Vector3.zero;
        gameObject.transform.localRotation = Quaternion.identity;
        string[] containerNames = { "Interior_Floors", "Interior_Partitions_And_Rock", "Interior_Ceilings", "Room_Scene_Labels", "Asteroid_3D_FullHull", "Space_Environment" };
        for (int i = 0; i < containerNames.Length; i++)
        {
            Transform c = transform.Find(containerNames[i]);
            if (c != null)
            {
                c.localPosition = Vector3.zero;
                c.localRotation = Quaternion.identity;
            }
        }
        if (interiorFloorContainer != null) { interiorFloorContainer.localPosition = Vector3.zero; interiorFloorContainer.localRotation = Quaternion.identity; }
        if (interiorWallsContainer != null) { interiorWallsContainer.localPosition = Vector3.zero; interiorWallsContainer.localRotation = Quaternion.identity; }
        if (interiorCeilingsContainer != null) { interiorCeilingsContainer.localPosition = Vector3.zero; interiorCeilingsContainer.localRotation = Quaternion.identity; }
        if (labelsContainer != null) { labelsContainer.localPosition = Vector3.zero; labelsContainer.localRotation = Quaternion.identity; }
        if (exteriorHullContainer != null) { exteriorHullContainer.localPosition = Vector3.zero; exteriorHullContainer.localRotation = Quaternion.identity; }
        if (spaceCosmosContainer != null) { spaceCosmosContainer.localPosition = Vector3.zero; spaceCosmosContainer.localRotation = Quaternion.identity; }

        string folder = "Assets/Resources/Prefabs/Environment";
        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
        }

        string prefabPath = $"{folder}/Asteroid_Base.prefab";
        PrefabUtility.SaveAsPrefabAssetAndConnect(gameObject, prefabPath, InteractionMode.UserAction);
        Debug.Log($"[PREFAB] Base enregistrée avec succès sous : {prefabPath}");
#endif
    }

#if UNITY_EDITOR
    private static void PurgeMissingScripts(GameObject root)
    {
        var allTransforms = root.GetComponentsInChildren<Transform>(true);
        int purged = 0;
        for (int i = 0; i < allTransforms.Length; i++)
        {
            purged += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(allTransforms[i].gameObject);

            // Élimine toute instance résiduelle de BaseNode
            var comps = allTransforms[i].GetComponents<Component>();
            for (int c = 0; c < comps.Length; c++)
            {
                if (comps[c] != null && comps[c].GetType().Name == "BaseNode")
                {
                    DestroyImmediate(comps[c]);
                    purged++;
                }
            }
        }

        if (purged > 0)
        {
            Debug.Log($"[AsteroidBase] 🧹 {purged} composant(s) manquant(s) / BaseNode nettoyé(s) avec succès avant écriture du préfab.");
        }
    }
#endif

    [ContextMenu("Corriger Plafonds du Préfab Existant (Sans Régénérer)")]
    public void PatchExistingCeilings()
    {
        Transform ceilingsParent = transform.Find("Interior_Ceilings");
        if (ceilingsParent == null && interiorCeilingsContainer != null)
        {
            ceilingsParent = interiorCeilingsContainer;
        }

        if (ceilingsParent == null)
        {
            Debug.LogWarning("[AsteroidBase] Conteneur 'Interior_Ceilings' introuvable sur cet objet.");
            return;
        }

        var visualizer = FindAnyObjectByType<Killtime.Tactics.Grid.HexGridVisualizer>();
        var grid = FindAnyObjectByType<Killtime.Tactics.Grid.TacticalHexGrid>();

        float targetOpacity = 0.22f;
        try
        {
            if (Killtime.UI.DevUIPreferences.Current != null)
                targetOpacity = Killtime.UI.DevUIPreferences.Current.CeilingOpacity;
        }
        catch { /* ignore */ }

        Material transparentMat = (visualizer != null && visualizer.CeilingMaterial != null)
            ? visualizer.CeilingMaterial
            : CreateTransparentCeilingMaterial(new Color(0.15f, 0.20f, 0.28f), targetOpacity);

        int patchedCount = 0;

        for (int i = 0; i < ceilingsParent.childCount; i++)
        {
            var child = ceilingsParent.GetChild(i);
            if (child == null) continue;

            child.gameObject.layer = 2;

            var col = child.GetComponent<Collider>();
            if (col != null)
            {
                DestroyImmediate(col);
            }

            var mr = child.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                mr.sharedMaterial = transparentMat;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
            }

            var prop = child.GetComponent<TacticalCeilingProp>() ?? child.gameObject.AddComponent<TacticalCeilingProp>();
            prop.Initialize();

            if (grid != null)
            {
                if (grid.TryGetNodeAtWorldPosition(child.position, out var node) && node != null)
                {
                    grid.SetNodeCeiling(node.Coordinates, true);
                }
            }

            patchedCount++;
        }

        if (grid != null)
        {
            grid.CeilingHeight = wallHeight;
        }

        if (visualizer != null)
        {
            visualizer.RefreshObstacles();
        }

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(gameObject);
        Debug.Log($"[AsteroidBase] ✅ {patchedCount} dalles de plafond corrigées en place sur le préfab existant.");
#endif
    }

    [ContextMenu("Nettoyer")]
    public void ClearBase()
    {
        List<GameObject> children = new List<GameObject>();
        for (int i = 0; i < transform.childCount; i++)
        {
            children.Add(transform.GetChild(i).gameObject);
        }

        foreach (var go in children)
        {
            DestroyImmediate(go);
        }

        interiorFloorContainer = null;
        interiorWallsContainer = null;
        interiorCeilingsContainer = null;
        labelsContainer = null;
        exteriorHullContainer = null;
        spaceCosmosContainer = null;
    }

    private void EnsureContainers()
    {
        // Parentage SANS conservation du monde (worldPositionStays=false) :
        // les conteneurs doivent être à zéro local, sinon les tuiles (espace
        // hex) et le mobilier enfant (local hérité) sont désalignés.
        if (interiorFloorContainer == null)
        {
            GameObject go = new GameObject("Interior_Floors");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            interiorFloorContainer = go.transform;
        }

        if (interiorWallsContainer == null)
        {
            GameObject go = new GameObject("Interior_Partitions_And_Rock");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            interiorWallsContainer = go.transform;
        }

        if (interiorCeilingsContainer == null)
        {
            GameObject go = new GameObject("Interior_Ceilings");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            interiorCeilingsContainer = go.transform;
        }

        if (labelsContainer == null)
        {
            GameObject go = new GameObject("Room_Scene_Labels");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            labelsContainer = go.transform;
        }

        if (exteriorHullContainer == null)
        {
            GameObject go = new GameObject("Asteroid_3D_FullHull");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            exteriorHullContainer = go.transform;
        }

        if (spaceCosmosContainer == null)
        {
            GameObject go = new GameObject("Space_Environment");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            spaceCosmosContainer = go.transform;
        }
    }
}

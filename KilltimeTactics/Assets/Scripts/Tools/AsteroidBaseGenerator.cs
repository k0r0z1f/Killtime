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
public class AsteroidBaseGenerator : MonoBehaviour
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

    private Mesh GetOrCreatePersistentMesh(Mesh generatedMesh, string assetName)
    {
#if UNITY_EDITOR
        if (Application.isPlaying) return generatedMesh;

        string folder = "Assets/Art/Meshes/AsteroidBase";
        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
        }

        string assetPath = $"{folder}/{assetName}.asset";
        Mesh diskMesh = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);

        if (diskMesh == null)
        {
            AssetDatabase.CreateAsset(generatedMesh, assetPath);
            AssetDatabase.SaveAssets();
            return generatedMesh;
        }
        else
        {
            diskMesh.Clear();
            diskMesh.vertices = generatedMesh.vertices;
            diskMesh.triangles = generatedMesh.triangles;
            diskMesh.uv = generatedMesh.uv;
            diskMesh.normals = generatedMesh.normals;
            diskMesh.bounds = generatedMesh.bounds;
            EditorUtility.SetDirty(diskMesh);
            AssetDatabase.SaveAssets();
            return diskMesh;
        }
#else
        return generatedMesh;
#endif
    }

    private void ApplyMeshRenderer(GameObject target, Mesh mesh, Material mat, Color tint)
    {
        MeshFilter mf = target.AddComponent<MeshFilter>();
        mf.sharedMesh = mesh;

        MeshRenderer mr = target.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;

        if (tint != Color.white)
        {
            MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", tint);
            mpb.SetColor("_Color", tint);
            mr.SetPropertyBlock(mpb);
        }

        MeshCollider mc = target.AddComponent<MeshCollider>();
        mc.sharedMesh = mesh;
    }

    private Mesh BuildSolidHexMesh(float topY, float botY, bool isCeiling = false)
    {
        Mesh mesh = new Mesh { name = isCeiling ? "HexCeilingSlab" : "HexFloorSlab" };
        List<Vector3> verts = new List<Vector3>();
        List<int> tris = new List<int>();

        Vector3[] topHex = new Vector3[6];
        Vector3[] botHex = new Vector3[6];

        for (int i = 0; i < 6; i++)
        {
            float angle = Mathf.Deg2Rad * (60.0f * i - 30.0f);
            topHex[i] = new Vector3(hexOuterRadius * Mathf.Cos(angle), topY, hexOuterRadius * Mathf.Sin(angle));
            botHex[i] = new Vector3(hexOuterRadius * Mathf.Cos(angle), botY, hexOuterRadius * Mathf.Sin(angle));
        }

        int topCenterIdx = verts.Count;
        verts.Add(new Vector3(0.0f, topY, 0.0f));
        int topStartIdx = verts.Count;
        for (int i = 0; i < 6; i++) verts.Add(topHex[i]);

        for (int i = 0; i < 6; i++)
        {
            int next = (i == 5) ? 0 : i + 1;
            tris.Add(topCenterIdx);
            tris.Add(topStartIdx + next);
            tris.Add(topStartIdx + i);
        }

        int botCenterIdx = verts.Count;
        verts.Add(new Vector3(0.0f, botY, 0.0f));
        int botStartIdx = verts.Count;
        for (int i = 0; i < 6; i++) verts.Add(botHex[i]);

        for (int i = 0; i < 6; i++)
        {
            int next = (i == 5) ? 0 : i + 1;
            tris.Add(botCenterIdx);
            tris.Add(botStartIdx + i);
            tris.Add(botStartIdx + next);
        }

        for (int i = 0; i < 6; i++)
        {
            int next = (i == 5) ? 0 : i + 1;
            int t0 = verts.Count; verts.Add(topHex[i]);
            int t1 = verts.Count; verts.Add(topHex[next]);
            int b0 = verts.Count; verts.Add(botHex[i]);
            int b1 = verts.Count; verts.Add(botHex[next]);

            tris.Add(t0); tris.Add(t1); tris.Add(b0);
            tris.Add(b0); tris.Add(t1); tris.Add(b1);
        }

        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private Mesh BuildWallColumnMesh(float height)
    {
        Mesh mesh = new Mesh { name = "HexColumn" };
        List<Vector3> verts = new List<Vector3>();
        List<int> tris = new List<int>();

        Vector3[] bHex = new Vector3[6];
        Vector3[] tHex = new Vector3[6];

        for (int i = 0; i < 6; i++)
        {
            float angle = Mathf.Deg2Rad * (60.0f * i - 30.0f);
            bHex[i] = new Vector3(hexOuterRadius * Mathf.Cos(angle), -floorThickness, hexOuterRadius * Mathf.Sin(angle));
            tHex[i] = new Vector3(bHex[i].x * 0.98f, height, bHex[i].z * 0.98f);
        }

        int topCenter = verts.Count;
        verts.Add(new Vector3(0f, height, 0f));
        int topStart = verts.Count;
        for (int i = 0; i < 6; i++) verts.Add(tHex[i]);

        for (int i = 0; i < 6; i++)
        {
            int next = (i == 5) ? 0 : i + 1;
            tris.Add(topCenter);
            tris.Add(topStart + next);
            tris.Add(topStart + i);
        }

        for (int i = 0; i < 6; i++)
        {
            int next = (i == 5) ? 0 : i + 1;
            int b0 = verts.Count; verts.Add(bHex[i]);
            int b1 = verts.Count; verts.Add(bHex[next]);
            int t0 = verts.Count; verts.Add(tHex[i]);
            int t1 = verts.Count; verts.Add(tHex[next]);

            tris.Add(b0); tris.Add(t0); tris.Add(b1);
            tris.Add(b1); tris.Add(t0); tris.Add(t1);
        }

        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private Mesh BuildHullMesh()
    {
        Mesh mesh = new Mesh { name = "AsteroidOrganicBody" };

        Vector3[] craterNormals = new Vector3[]
        {
            new Vector3(0.55f, 0.65f, 0.52f).normalized,
            new Vector3(-0.62f, -0.38f, 0.68f).normalized,
            new Vector3(-0.35f, 0.78f, -0.52f).normalized,
            new Vector3(0.25f, -0.72f, -0.64f).normalized,
            new Vector3(-0.80f, 0.15f, -0.58f).normalized
        };
        float[] craterRadii = new float[] { 0.42f, 0.35f, 0.38f, 0.30f, 0.32f };
        float[] craterDepths = new float[] { 4.8f, 3.6f, 4.2f, 3.0f, 3.4f };

        List<Vector3> verts = new List<Vector3>();
        List<int> tris = new List<int>();

        Vector3 EvalPoint(float phi, float theta)
        {
            float xNorm = Mathf.Cos(phi) * Mathf.Cos(theta);
            float yNorm = Mathf.Sin(phi);
            float zNorm = Mathf.Cos(phi) * Mathf.Sin(theta);
            Vector3 unitDir = new Vector3(xNorm, yNorm, zNorm);

            float potatoPinch = 1.0f - asymmetryPinch * Mathf.Abs(yNorm) + 0.12f * Mathf.Sin(theta * 2f);
            float elongationX = 1.0f + 0.15f * Mathf.Cos(theta);

            float fbm = SampleFBM(unitDir * 1.5f, noiseSeed) * fbmAmplitude;

            float craterDisplacement = 0f;
            for (int c = 0; c < craterNormals.Length; c++)
            {
                float dist = Vector3.Distance(unitDir, craterNormals[c]);
                if (dist < craterRadii[c])
                {
                    float t = dist / craterRadii[c];
                    float bowl = -Mathf.Cos(t * Mathf.PI * 0.5f) * craterDepths[c];
                    float rim = Mathf.Sin(t * Mathf.PI) * (craterDepths[c] * 0.28f);
                    craterDisplacement += (bowl + rim);
                }
            }

            float rx = (baseRadiusX * elongationX * potatoPinch) + fbm + craterDisplacement;
            float ry = (baseRadiusY * potatoPinch) + fbm + craterDisplacement;
            float rz = (baseRadiusZ * potatoPinch) + fbm + craterDisplacement;

            float finalX = rx * xNorm;
            float finalY = (ry * yNorm) + asteroidCenterOffsetY;
            float finalZ = rz * zNorm;

            bool isHangarGateZone = (finalX > 20.0f) && (Mathf.Abs(finalZ) < 8.5f) && (finalY >= -2.0f && finalY <= 9.5f);
            if (isHangarGateZone)
            {
                finalX = 34.0f;
            }

            return new Vector3(finalX, finalY, finalZ);
        }

        Vector3 northPole = EvalPoint(Mathf.PI * 0.5f, 0f);
        Vector3 southPole = EvalPoint(-Mathf.PI * 0.5f, 0f);

        int northPoleIdx = verts.Count; verts.Add(northPole);
        int southPoleIdx = verts.Count; verts.Add(southPole);

        int ringStartIdx = verts.Count;

        for (int lat = 1; lat < shellLatSegments; lat++)
        {
            float v = (float)lat / shellLatSegments;
            float phi = Mathf.PI * (0.5f - v);

            for (int lon = 0; lon < shellLonSegments; lon++)
            {
                float u = (float)lon / shellLonSegments;
                float theta = 2.0f * Mathf.PI * u;
                verts.Add(EvalPoint(phi, theta));
            }
        }

        for (int lon = 0; lon < shellLonSegments; lon++)
        {
            int nextLon = (lon + 1) % shellLonSegments;
            int r0 = ringStartIdx + lon;
            int r1 = ringStartIdx + nextLon;

            tris.Add(northPoleIdx);
            tris.Add(r0);
            tris.Add(r1);
        }

        for (int lat = 0; lat < shellLatSegments - 2; lat++)
        {
            int rowA = ringStartIdx + lat * shellLonSegments;
            int rowB = ringStartIdx + (lat + 1) * shellLonSegments;

            for (int lon = 0; lon < shellLonSegments; lon++)
            {
                int nextLon = (lon + 1) % shellLonSegments;

                int a0 = rowA + lon;
                int a1 = rowA + nextLon;
                int b0 = rowB + lon;
                int b1 = rowB + nextLon;

                Vector3 vA0 = verts[a0];
                bool skipFace = (vA0.x > 19.5f) && (Mathf.Abs(vA0.z) < 8.0f) && (vA0.y >= -1.0f && vA0.y <= 9.0f);
                if (skipFace) continue;

                tris.Add(a0);
                tris.Add(b0);
                tris.Add(a1);

                tris.Add(a1);
                tris.Add(b0);
                tris.Add(b1);
            }
        }

        int lastRowStart = ringStartIdx + (shellLatSegments - 2) * shellLonSegments;
        for (int lon = 0; lon < shellLonSegments; lon++)
        {
            int nextLon = (lon + 1) % shellLonSegments;
            int r0 = lastRowStart + lon;
            int r1 = lastRowStart + nextLon;

            tris.Add(southPoleIdx);
            tris.Add(r1);
            tris.Add(r0);
        }

        int extVertCount = verts.Count;
        for (int i = 0; i < extVertCount; i++)
        {
            verts.Add(verts[i]);
        }

        int extTrisCount = tris.Count;
        for (int t = 0; t < extTrisCount; t += 3)
        {
            int i0 = tris[t] + extVertCount;
            int i1 = tris[t + 1] + extVertCount;
            int i2 = tris[t + 2] + extVertCount;

            tris.Add(i0);
            tris.Add(i2);
            tris.Add(i1);
        }

        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private void InstantiateHullObject(Mesh hullMesh, Material rockMat)
    {
        GameObject hullObj = new GameObject("Asteroid_3D_Crust");
        hullObj.transform.SetParent(exteriorHullContainer);

        MeshFilter mf = hullObj.AddComponent<MeshFilter>();
        mf.sharedMesh = hullMesh;

        MeshRenderer mr = hullObj.AddComponent<MeshRenderer>();
        mr.sharedMaterial = rockMat;

        MeshCollider mc = hullObj.AddComponent<MeshCollider>();
        mc.sharedMesh = hullMesh;
    }

    private Dictionary<AxialCoord, (RoomType room, TileCategory cat)> GenerateRoomLayout()
    {
        var grid = new Dictionary<AxialCoord, (RoomType room, TileCategory cat)>();
        HashSet<AxialCoord> sealedWallPerimeters = new HashSet<AxialCoord>();

        AssignSealedRoom(grid, sealedWallPerimeters, roomRegistry[RoomType.CommandCenter].center, 3, RoomType.CommandCenter);
        AssignSealedRoom(grid, sealedWallPerimeters, roomRegistry[RoomType.ShipCargoBay].center, 5, RoomType.ShipCargoBay);
        AssignSealedRoom(grid, sealedWallPerimeters, roomRegistry[RoomType.EngineeringRoom].center, 3, RoomType.EngineeringRoom);
        AssignSealedRoom(grid, sealedWallPerimeters, roomRegistry[RoomType.CrewQuartersA].center, 3, RoomType.CrewQuartersA);
        AssignSealedRoom(grid, sealedWallPerimeters, roomRegistry[RoomType.CrewQuartersB].center, 3, RoomType.CrewQuartersB);
        AssignSealedRoom(grid, sealedWallPerimeters, roomRegistry[RoomType.ResourceStore].center, 3, RoomType.ResourceStore);

        CarveExternalCorridor(grid, sealedWallPerimeters, roomRegistry[RoomType.CommandCenter].center, roomRegistry[RoomType.ShipCargoBay].center);
        CarveExternalCorridor(grid, sealedWallPerimeters, roomRegistry[RoomType.CommandCenter].center, roomRegistry[RoomType.EngineeringRoom].center);
        CarveExternalCorridor(grid, sealedWallPerimeters, roomRegistry[RoomType.CommandCenter].center, roomRegistry[RoomType.CrewQuartersA].center);
        CarveExternalCorridor(grid, sealedWallPerimeters, roomRegistry[RoomType.CommandCenter].center, roomRegistry[RoomType.CrewQuartersB].center);
        CarveExternalCorridor(grid, sealedWallPerimeters, roomRegistry[RoomType.CommandCenter].center, roomRegistry[RoomType.ResourceStore].center);

        for (int r = -2; r <= 2; r++)
        {
            AxialCoord gateCoord = new AxialCoord(18, r);
            grid[gateCoord] = (RoomType.ShipCargoBay, TileCategory.ShipBayGate);
            sealedWallPerimeters.Remove(gateCoord);
        }

        List<AxialCoord> activeTiles = new List<AxialCoord>(grid.Keys);
        foreach (var coord in activeTiles)
        {
            foreach (var neighbor in GetHexNeighbors(coord))
            {
                if (!grid.ContainsKey(neighbor))
                {
                    if (neighbor.q >= 18) continue;
                    int dist = HexDistance(neighbor, roomRegistry[RoomType.CommandCenter].center);
                    grid[neighbor] = dist >= 13 ? (RoomType.None, TileCategory.RockBoundary) : (RoomType.None, TileCategory.InteriorWall);
                }
            }
        }

        return grid;
    }

    private void AssignSealedRoom(
        Dictionary<AxialCoord, (RoomType room, TileCategory cat)> grid,
        HashSet<AxialCoord> wallSet,
        AxialCoord center,
        int floorRadius,
        RoomType room)
    {
        for (int q = -(floorRadius + 1); q <= (floorRadius + 1); q++)
        {
            int r1 = Mathf.Max(-(floorRadius + 1), -q - (floorRadius + 1));
            int r2 = Mathf.Min((floorRadius + 1), -q + (floorRadius + 1));
            for (int r = r1; r <= r2; r++)
            {
                AxialCoord c = new AxialCoord(center.q + q, center.r + r);
                int dist = HexDistance(c, center);

                if (dist <= floorRadius)
                {
                    grid[c] = (room, TileCategory.Floor);
                }
                else if (dist == floorRadius + 1)
                {
                    if (!grid.ContainsKey(c) || grid[c].cat != TileCategory.Floor)
                    {
                        grid[c] = (room, TileCategory.InteriorWall);
                        wallSet.Add(c);
                    }
                }
            }
        }
    }

    private void CarveExternalCorridor(
        Dictionary<AxialCoord, (RoomType room, TileCategory cat)> grid,
        HashSet<AxialCoord> wallSet,
        AxialCoord start,
        AxialCoord end)
    {
        int dist = HexDistance(start, end);
        for (int i = 0; i <= dist; i++)
        {
            float t = dist == 0 ? 0f : (float)i / dist;
            float qf = Mathf.Lerp(start.q, end.q, t);
            float rf = Mathf.Lerp(start.r, end.r, t);
            AxialCoord c = RoundAxial(qf, rf);

            if (wallSet.Contains(c)) continue;

            if (!grid.ContainsKey(c) || grid[c].Item2 == TileCategory.InteriorWall || grid[c].Item2 == TileCategory.RockBoundary)
            {
                grid[c] = (RoomType.Corridor, TileCategory.Floor);
            }
        }
    }

    private void SpawnRoomLabels()
    {
        foreach (var kvp in roomRegistry)
        {
            Vector3 pos = HexToWorld(kvp.Value.center);
            pos.y = 0.30f;

            GameObject labelObj = new GameObject($"Label_{kvp.Key}");
            labelObj.transform.SetParent(labelsContainer);
            labelObj.transform.position = pos;
            labelObj.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

            TextMesh tm = labelObj.AddComponent<TextMesh>();
            tm.text = kvp.Value.displayName;
            tm.fontSize = 48;
            tm.characterSize = 0.09f;
            tm.alignment = TextAlignment.Center;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.color = Color.white;
            tm.fontStyle = FontStyle.Bold;
        }
    }

    private AxialCoord RoundAxial(float q, float r)
    {
        float s = -q - r;
        int rq = Mathf.RoundToInt(q);
        int rr = Mathf.RoundToInt(r);
        int rs = Mathf.RoundToInt(s);

        float qDiff = Mathf.Abs(rq - q);
        float rDiff = Mathf.Abs(rr - r);
        float sDiff = Mathf.Abs(rs - s);

        if (qDiff > rDiff && qDiff > sDiff) rq = -rr - rs;
        else if (rDiff > sDiff) rr = -rq - rs;

        return new AxialCoord(rq, rr);
    }

    private IEnumerable<AxialCoord> GetHexNeighbors(AxialCoord c)
    {
        yield return new AxialCoord(c.q + 1, c.r);
        yield return new AxialCoord(c.q + 1, c.r - 1);
        yield return new AxialCoord(c.q, c.r - 1);
        yield return new AxialCoord(c.q - 1, c.r);
        yield return new AxialCoord(c.q - 1, c.r + 1);
        yield return new AxialCoord(c.q, c.r + 1);
    }

    private Vector3 HexToWorld(AxialCoord coord)
    {
        float x = hexOuterRadius * (Mathf.Sqrt(3.0f) * coord.q + Mathf.Sqrt(3.0f) / 2.0f * coord.r);
        float z = hexOuterRadius * (1.5f * coord.r);
        return new Vector3(x, 0.0f, z);
    }

    private int HexDistance(AxialCoord a, AxialCoord b)
    {
        return (Mathf.Abs(a.q - b.q) + Mathf.Abs(a.r - b.r) + Mathf.Abs(a.s - b.s)) / 2;
    }

    private float SampleFBM(Vector3 p, int seedOffset)
    {
        float total = 0f;
        float amp = 1.0f;
        float freq = 0.65f;
        float maxVal = 0f;

        Vector3 offset = new Vector3(seedOffset * 0.17f, seedOffset * 0.23f, seedOffset * 0.31f);

        for (int o = 0; o < 3; o++)
        {
            Vector3 sp = (p + offset) * freq;
            float xy = Mathf.PerlinNoise(sp.x, sp.y);
            float yz = Mathf.PerlinNoise(sp.y + 31.4f, sp.z + 17.1f);
            float zx = Mathf.PerlinNoise(sp.z + 59.2f, sp.x + 83.1f);
            float noiseVal = (xy + yz + zx) / 3f;

            total += (noiseVal - 0.5f) * amp;
            maxVal += 0.5f * amp;
            amp *= 0.48f;
            freq *= 2.15f;
        }

        return total / maxVal;
    }

    private void BuildOrbitalDebris(Material rockMat)
    {
        GameObject debrisField = new GameObject("Orbital_Debris_Ring");
        debrisField.transform.SetParent(spaceCosmosContainer);

        UnityEngine.Random.InitState(noiseSeed);

        for (int i = 0; i < 48; i++)
        {
            float angle = UnityEngine.Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float dist = UnityEngine.Random.Range(52f, 105f);
            float y = UnityEngine.Random.Range(-25f, 35f);

            GameObject chunk = GameObject.CreatePrimitive(PrimitiveType.Cube);
            chunk.name = $"Debris_{i}";
            chunk.transform.SetParent(debrisField.transform);
            chunk.transform.position = new Vector3(Mathf.Cos(angle) * dist, y, Mathf.Sin(angle) * dist);
            chunk.transform.rotation = UnityEngine.Random.rotation;
            chunk.transform.localScale = new Vector3(
                UnityEngine.Random.Range(1.2f, 4.5f),
                UnityEngine.Random.Range(0.8f, 3.2f),
                UnityEngine.Random.Range(1.2f, 4.5f)
            );
            DestroyImmediate(chunk.GetComponent<Collider>());
            chunk.GetComponent<Renderer>().sharedMaterial = rockMat;
        }
    }

    private Color GetRoomColor(RoomType room)
    {
        return room switch
        {
            RoomType.CommandCenter => new Color(0.22f, 0.45f, 0.70f),
            RoomType.CrewQuartersA => new Color(0.25f, 0.55f, 0.48f),
            RoomType.CrewQuartersB => new Color(0.28f, 0.58f, 0.52f),
            RoomType.ShipCargoBay => new Color(0.65f, 0.50f, 0.28f),
            RoomType.EngineeringRoom => new Color(0.68f, 0.28f, 0.28f),
            RoomType.ResourceStore => new Color(0.48f, 0.62f, 0.25f),
            RoomType.Corridor => new Color(0.30f, 0.32f, 0.35f),
            _ => new Color(0.22f, 0.22f, 0.22f)
        };
    }

    private Material CreateDefaultMaterial(Color color, float smoothness, bool emission = false)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");

        Material mat = new Material(shader);

        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);

        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
        if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);

        if (emission)
        {
            mat.EnableKeyword("_EMISSION");
            if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", color * 1.5f);
        }

        return mat;
    }

    private Material CreateTransparentCeilingMaterial(Color baseColor, float alpha)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        bool isURP = (shader != null);
        if (!isURP) shader = Shader.Find("Standard");

        Material mat = new Material(shader);
        Color col = new Color(baseColor.r, baseColor.g, baseColor.b, alpha);

        if (isURP)
        {
            mat.SetFloat("_Surface", 1.0f);
            mat.SetFloat("_Blend", 0.0f);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", col);
        }
        else
        {
            mat.SetFloat("_Mode", 3.0f);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", col);
        }

        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.1f);
        if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.1f);

        return mat;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Camera cam = Camera.current;
        if (cam == null) return;

        foreach (var kvp in roomRegistry)
        {
            Vector3 worldPos = HexToWorld(kvp.Value.center);
            worldPos.y = wallHeight + 1.2f;

            float distToCam = Vector3.Distance(cam.transform.position, worldPos);
            if (distToCam > 75.0f) continue;

            GUIStyle style = new GUIStyle
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 11,
                fontStyle = FontStyle.Bold
            };
            style.normal.textColor = Color.cyan;
            Handles.Label(worldPos, kvp.Value.displayName, style);
        }
    }
#endif
}
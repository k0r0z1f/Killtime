#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using System.IO;

public static class HighPolySphereGenerator
{
    private const string MESH_FOLDER = "Assets/Models";
    private const string MESH_PATH = "Assets/Models/UVSphere_128x64.asset";

    [MenuItem("GameObject/3D Object/High-Poly Planet Sphere", false, 10)]
    public static void CreatePlanetSphere()
    {
        Mesh sphereMesh = GetOrCreateMeshAsset(1.0f, 128, 64);

        GameObject planetGo = new GameObject("Planet_HighPolySphere");
        MeshFilter filter = planetGo.AddComponent<MeshFilter>();
        MeshRenderer renderer = planetGo.AddComponent<MeshRenderer>();

        filter.sharedMesh = sphereMesh;
        renderer.sharedMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");

        if (Selection.activeTransform != null)
        {
            planetGo.transform.SetParent(Selection.activeTransform, false);
        }

        planetGo.transform.localPosition = Vector3.zero;
        planetGo.transform.localRotation = Quaternion.identity;
        planetGo.transform.localScale = Vector3.one;

        Selection.activeGameObject = planetGo;
        Undo.RegisterCreatedObjectUndo(planetGo, "Create High-Poly Planet Sphere");
    }

    [MenuItem("GameObject/3D Object/Restore Planet Prefab Meshes", false, 11)]
    public static void RestorePlanetMeshes()
    {
        Mesh sphereMesh = GetOrCreateMeshAsset(1.0f, 128, 64);

        GameObject target = Selection.activeGameObject;
        if (target == null)
        {
            target = GameObject.Find("Planet_Nefris");
            if (target == null) target = GameObject.Find("Planet_Root");
        }

        if (target == null)
        {
            Debug.LogError("Sélectionnez le GameObject de la planète (ex: Planet_Nefris) dans la Hierarchy avant de restaurer.");
            return;
        }

        target.transform.position = Vector3.zero;

        MeshFilter[] filters = target.GetComponentsInChildren<MeshFilter>(true);
        foreach (MeshFilter filter in filters)
        {
            filter.sharedMesh = sphereMesh;
            EditorUtility.SetDirty(filter);
        }

        PrefabUtility.RecordPrefabInstancePropertyModifications(target);
        Debug.Log($"Restoration terminée : Le mesh physique a été réassigné aux {filters.Length} couches de {target.name}.");
    }

    public static Mesh GetOrCreateMeshAsset(float radius, int lonSegments, int latRings)
    {
        if (!Directory.Exists(MESH_FOLDER))
        {
            Directory.CreateDirectory(MESH_FOLDER);
            AssetDatabase.Refresh();
        }

        Mesh existingMesh = AssetDatabase.LoadAssetAtPath<Mesh>(MESH_PATH);
        if (existingMesh != null)
        {
            return existingMesh;
        }

        Mesh newMesh = GenerateUVSphereMesh(radius, lonSegments, latRings);
        AssetDatabase.CreateAsset(newMesh, MESH_PATH);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        return newMesh;
    }

    private static Mesh GenerateUVSphereMesh(float radius, int lonSegments, int latRings)
    {
        Mesh mesh = new Mesh();
        mesh.name = $"UVSphere_{lonSegments}x{latRings}";
        mesh.indexFormat = IndexFormat.UInt32;

        int vertCount = (latRings + 1) * (lonSegments + 1);
        Vector3[] vertices = new Vector3[vertCount];
        Vector3[] normals = new Vector3[vertCount];
        Vector2[] uv = new Vector2[vertCount];

        int index = 0;
        for (int lat = 0; lat <= latRings; lat++)
        {
            float v = (float)lat / latRings;
            float phi = v * Mathf.PI;

            for (int lon = 0; lon <= lonSegments; lon++)
            {
                float u = (float)lon / lonSegments;
                float theta = u * (Mathf.PI * 2.0f);

                float x = Mathf.Sin(phi) * Mathf.Cos(theta);
                float y = Mathf.Cos(phi);
                float z = Mathf.Sin(phi) * Mathf.Sin(theta);

                Vector3 position = new Vector3(x, y, z) * radius;
                vertices[index] = position;
                normals[index] = new Vector3(x, y, z);
                uv[index] = new Vector2(1.0f - u, 1.0f - v);

                index++;
            }
        }

        int quadCount = latRings * lonSegments;
        int[] triangles = new int[quadCount * 6];
        int triIndex = 0;

        for (int lat = 0; lat < latRings; lat++)
        {
            for (int lon = 0; lon < lonSegments; lon++)
            {
                int current = lat * (lonSegments + 1) + lon;
                int next = current + lonSegments + 1;

                triangles[triIndex++] = current;
                triangles[triIndex++] = current + 1;
                triangles[triIndex++] = next;

                triangles[triIndex++] = next;
                triangles[triIndex++] = current + 1;
                triangles[triIndex++] = next + 1;
            }
        }

        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uv;
        mesh.triangles = triangles;
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();

        return mesh;
    }
}
#endif
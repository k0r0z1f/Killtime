#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;

public static class HighPolySphereGenerator
{
    [MenuItem("GameObject/3D Object/High-Poly Planet Sphere", false, 10)]
    public static void CreatePlanetSphere()
    {
        int longitudeSegments = 128;
        int latitudeRings = 64;
        float radius = 1.0f;

        Mesh sphereMesh = GenerateUVSphereMesh(radius, longitudeSegments, latitudeRings);

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
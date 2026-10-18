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

// AsteroidBaseGenerator.Mesh — maillages + instanciation (split move-only, voir facade).
public partial class AsteroidBaseGenerator : MonoBehaviour
{

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
}

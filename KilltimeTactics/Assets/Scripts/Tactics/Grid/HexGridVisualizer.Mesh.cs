using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Killtime.Tactics.Grid
{
    public partial class HexGridVisualizer : MonoBehaviour
    {
        public static Mesh GenerateFlatPointyHexMesh(float radius)
        {
            var mesh = new Mesh { name = "ProceduralFlatPointyHex" };

            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var colors = new List<Color32>();

            vertices.Add(Vector3.zero);
            normals.Add(Vector3.up);
            uvs.Add(new Vector2(0.5f, 0.5f));
            colors.Add(new Color32(255, 255, 255, 255));

            for (int i = 0; i < 6; i++)
            {
                float angle = (30f + 60f * i) * Mathf.Deg2Rad;
                float x = radius * Mathf.Cos(angle);
                float z = radius * Mathf.Sin(angle);
                vertices.Add(new Vector3(x, 0f, z));
                normals.Add(Vector3.up);
                uvs.Add(new Vector2((x / (radius * 2f)) + 0.5f, (z / (radius * 2f)) + 0.5f));
                colors.Add(new Color32(255, 255, 255, 255));
            }

            for (int i = 1; i <= 6; i++)
            {
                int next = (i == 6) ? 1 : i + 1;
                triangles.Add(0);
                triangles.Add(i);
                triangles.Add(next);
            }

            for (int i = 1; i <= 6; i++)
            {
                int next = (i == 6) ? 1 : i + 1;
                triangles.Add(0);
                triangles.Add(next);
                triangles.Add(i);
            }

            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.RecalculateBounds();

            return mesh;
        }

        public static Mesh GenerateSculptedTileMesh(TacticalHexGrid grid, HexCoordinates coords, HexNode node, float radius, bool smoothSlope, float uvTiling)
        {
            var mesh = new Mesh { name = $"SculptedHex_{coords.Q}_{coords.R}" };

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            Vector3 centerWorld = node.WorldPosition;
            float worldTilingFactor = Mathf.Max(0.1f, radius * 2f * uvTiling);

            // 1. Calcul géométrique des 6 sommets supérieurs
            Vector3 centerTop = Vector3.zero;
            Vector3[] topCorners = new Vector3[6];

            for (int i = 0; i < 6; i++)
            {
                float angle = (30f + 60f * i) * Mathf.Deg2Rad;
                float x = radius * Mathf.Cos(angle);
                float z = radius * Mathf.Sin(angle);
                float y = 0f;

                if (smoothSlope && grid != null)
                {
                    var neighborA = grid.GetNode(coords.GetNeighbor(i));
                    var neighborB = grid.GetNode(coords.GetNeighbor((i + 1) % 6));
                    float eCur = node.Elevation;
                    float eA = neighborA != null ? neighborA.Elevation : eCur;
                    float eB = neighborB != null ? neighborB.Elevation : eCur;
                    float avgCorner = (eCur + eA + eB) / 3f;
                    y = avgCorner - eCur;
                }

                topCorners[i] = new Vector3(x, y, z);
            }

            // 2. FACE DU SOL (Horizontale, plane, normale vers le haut, enroulement horaire strict)
            int topCenterIdx = vertices.Count;
            vertices.Add(centerTop);
            normals.Add(Vector3.up);
            uvs.Add(new Vector2(0.5f, 0.5f));

            int topCornerStartIdx = vertices.Count;
            for (int i = 0; i < 6; i++)
            {
                vertices.Add(topCorners[i]);
                float u = (topCorners[i].x / (radius * 2f)) * uvTiling + 0.5f;
                float v = (topCorners[i].z / (radius * 2f)) * uvTiling + 0.5f;
                uvs.Add(new Vector2(u, v));

                if (smoothSlope)
                {
                    int prev = (i + 5) % 6;
                    Vector3 edgeA = topCorners[prev] - centerTop;
                    Vector3 edgeB = topCorners[i] - centerTop;
                    Vector3 n = Vector3.Cross(edgeA, edgeB).normalized;
                    if (n.y < 0f) n = -n;
                    normals.Add(n);
                }
                else
                {
                    normals.Add(Vector3.up);
                }
            }

            for (int i = 0; i < 6; i++)
            {
                int next = (i + 1) % 6;

                // Face supérieure unique (sens horaire strict, élimine tout Z-fighting coplanaire)
                triangles.Add(topCenterIdx);
                triangles.Add(topCornerStartIdx + next);
                triangles.Add(topCornerStartIdx + i);
            }

            // 3. ÉPAISSEUR DE DALLE (8cm standard, s'étire uniquement en falaise pour joindre le voisin le plus bas)
            float minNeighborElev = node.Elevation;
            if (grid != null)
            {
                for (int d = 0; d < 6; d++)
                {
                    var neighbor = grid.GetNode(coords.GetNeighbor(d));
                    if (neighbor != null && neighbor.Elevation < minNeighborElev)
                    {
                        minNeighborElev = neighbor.Elevation;
                    }
                }
            }

            float stepDrop = Mathf.Max(0f, node.Elevation - minNeighborElev);
            float tileThickness = 0.08f;
            float bottomLocalY = -(stepDrop + tileThickness);

            for (int i = 0; i < 6; i++)
            {
                int next = (i + 1) % 6;

                Vector3 vTL = topCorners[i];
                Vector3 vTR = topCorners[next];
                Vector3 vBR = new Vector3(topCorners[next].x, bottomLocalY, topCorners[next].z);
                Vector3 vBL = new Vector3(topCorners[i].x, bottomLocalY, topCorners[i].z);

                Vector3 sideNormal = Vector3.Cross(vTR - vTL, vBL - vTL).normalized;

                int baseIdx = vertices.Count;
                vertices.Add(vTL);
                vertices.Add(vTR);
                vertices.Add(vBR);
                vertices.Add(vBL);

                normals.Add(sideNormal);
                normals.Add(sideNormal);
                normals.Add(sideNormal);
                normals.Add(sideNormal);

                uvs.Add(new Vector2(0f, 1f));
                uvs.Add(new Vector2(1f, 1f));
                uvs.Add(new Vector2(1f, 0f));
                uvs.Add(new Vector2(0f, 0f));

                triangles.Add(baseIdx);
                triangles.Add(baseIdx + 1);
                triangles.Add(baseIdx + 2);

                triangles.Add(baseIdx);
                triangles.Add(baseIdx + 2);
                triangles.Add(baseIdx + 3);
            }

            // 4. DESSOUS DE LA DALLE
            int botCenterIdx = vertices.Count;
            vertices.Add(new Vector3(0, bottomLocalY, 0));
            normals.Add(Vector3.down);
            uvs.Add(new Vector2(0.5f, 0.5f));

            int botCornerStartIdx = vertices.Count;
            for (int i = 0; i < 6; i++)
            {
                vertices.Add(new Vector3(topCorners[i].x, bottomLocalY, topCorners[i].z));
                normals.Add(Vector3.down);
                uvs.Add(new Vector2(0.5f, 0.5f));
            }

            for (int i = 0; i < 6; i++)
            {
                int next = (i + 1) % 6;
                triangles.Add(botCenterIdx);
                triangles.Add(botCornerStartIdx + i);
                triangles.Add(botCornerStartIdx + next);
            }

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();

            return mesh;
        }

        public static Mesh GeneratePointyHexMesh(float radius, float height)
        {
            var mesh = new Mesh { name = "ProceduralPointyHex" };

            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();

            // 1. Sommet central supérieur
            Vector3 centerTop = new Vector3(0, height * 0.5f, 0);
            vertices.Add(centerTop);
            normals.Add(Vector3.up);
            uvs.Add(new Vector2(0.5f, 0.5f));

            // 2. 6 Sommets périphériques supérieurs
            for (int i = 0; i < 6; i++)
            {
                float angle = (30f + 60f * i) * Mathf.Deg2Rad;
                float x = radius * Mathf.Cos(angle);
                float z = radius * Mathf.Sin(angle);
                vertices.Add(new Vector3(x, height * 0.5f, z));
                normals.Add(Vector3.up);
                uvs.Add(new Vector2((x / (radius * 2f)) + 0.5f, (z / (radius * 2f)) + 0.5f));
            }

            // Triangles supérieurs
            for (int i = 1; i <= 6; i++)
            {
                int next = (i == 6) ? 1 : i + 1;
                triangles.Add(0);
                triangles.Add(i);
                triangles.Add(next);
            }

            // 3. Sommet central inférieur
            int centerBottomIndex = vertices.Count;
            Vector3 centerBottom = new Vector3(0, -height * 0.5f, 0);
            vertices.Add(centerBottom);
            normals.Add(Vector3.down);
            uvs.Add(new Vector2(0.5f, 0.5f));

            // 4. 6 Sommets périphériques inférieurs
            int bottomStartIndex = vertices.Count;
            for (int i = 0; i < 6; i++)
            {
                float angle = (30f + 60f * i) * Mathf.Deg2Rad;
                float x = radius * Mathf.Cos(angle);
                float z = radius * Mathf.Sin(angle);
                vertices.Add(new Vector3(x, -height * 0.5f, z));
                normals.Add(Vector3.down);
                uvs.Add(new Vector2((x / (radius * 2f)) + 0.5f, (z / (radius * 2f)) + 0.5f));
            }

            // Triangles inférieurs
            for (int i = 0; i < 6; i++)
            {
                int curr = bottomStartIndex + i;
                int next = bottomStartIndex + ((i + 1) % 6);
                triangles.Add(centerBottomIndex);
                triangles.Add(next);
                triangles.Add(curr);
            }

            // 5. Flancs latéraux
            for (int i = 0; i < 6; i++)
            {
                int topCurr = 1 + i;
                int topNext = 1 + ((i + 1) % 6);
                int botCurr = bottomStartIndex + i;
                int botNext = bottomStartIndex + ((i + 1) % 6);

                triangles.Add(topCurr);
                triangles.Add(botCurr);
                triangles.Add(topNext);

                triangles.Add(topNext);
                triangles.Add(botCurr);
                triangles.Add(botNext);
            }

            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.RecalculateBounds();

            return mesh;
        }
    }
}

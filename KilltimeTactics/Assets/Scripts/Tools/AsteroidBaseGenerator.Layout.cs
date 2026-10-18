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

// AsteroidBaseGenerator.Layout — layout hex + labels + utilitaires (split move-only, voir facade).
public partial class AsteroidBaseGenerator : MonoBehaviour
{

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
}

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

// AsteroidBaseGenerator.Materials — debris + materiaux + gizmos (split move-only, voir facade).
public partial class AsteroidBaseGenerator : MonoBehaviour
{

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

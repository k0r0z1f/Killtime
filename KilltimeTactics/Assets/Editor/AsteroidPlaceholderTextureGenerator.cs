#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

public class AsteroidPlaceholderTextureGenerator : EditorWindow
{
    private const int TextureSize = 512;
    private const string TargetFolder = "Assets/Art/Textures/Placeholders";

    [MenuItem("Killtime/Générer Textures Placeholders Base")]
    public static void GenerateAllPlaceholders()
    {
        if (!Directory.Exists(TargetFolder))
        {
            Directory.CreateDirectory(TargetFolder);
            AssetDatabase.Refresh();
        }

        Texture2D floorTex = CreateFloorTexture();
        Texture2D wallTex = CreateWallTexture();
        Texture2D ceilingTex = CreateCeilingTexture();
        Texture2D rockTex = CreateRockTexture();
        Texture2D hazardTex = CreateHazardTexture();

        string floorPath = SaveTexture(floorTex, "Tex_Placeholder_Floor_Plates.png");
        string wallPath = SaveTexture(wallTex, "Tex_Placeholder_Wall_Bulkhead.png");
        string ceilingPath = SaveTexture(ceilingTex, "Tex_Placeholder_Ceiling_Grid.png");
        string rockPath = SaveTexture(rockTex, "Tex_Placeholder_Rock_Basalt.png");
        string hazardPath = SaveTexture(hazardTex, "Tex_Placeholder_Hangar_Hazard.png");

        AssetDatabase.Refresh();

        Material floorMat = CreateOrUpdateMaterial("Mat_Base_Floor", floorPath, 0.45f, 0.70f, new Vector2(1f, 1f));
        Material wallMat = CreateOrUpdateMaterial("Mat_Base_Wall", wallPath, 0.25f, 0.40f, new Vector2(1f, 2f));
        Material ceilingMat = CreateOrUpdateMaterial("Mat_Base_Ceiling", ceilingPath, 0.15f, 0.30f, new Vector2(2f, 2f));
        Material rockMat = CreateOrUpdateMaterial("Mat_Base_Rock", rockPath, 0.05f, 0.10f, new Vector2(6f, 6f));
        Material hazardMat = CreateOrUpdateMaterial("Mat_Base_ShipBayGate", hazardPath, 0.35f, 0.50f, new Vector2(1f, 1f), true);

        AsteroidBaseGenerator baseGen = Object.FindAnyObjectByType<AsteroidBaseGenerator>();
        if (baseGen != null)
        {
            SerializedObject so = new SerializedObject(baseGen);
            so.FindProperty("floorMaterial").objectReferenceValue = floorMat;
            so.FindProperty("wallMaterial").objectReferenceValue = wallMat;
            so.FindProperty("ceilingMaterial").objectReferenceValue = ceilingMat;
            so.FindProperty("rockMaterial").objectReferenceValue = rockMat;
            so.FindProperty("gateMaterial").objectReferenceValue = hazardMat;
            so.ApplyModifiedProperties();

            baseGen.GenerateBase();
            Debug.Log("[TEXTURES] Matériaux de substitution appliqués à AsteroidBaseGenerator.");
        }
        else
        {
            Debug.Log("[TEXTURES] Textures et matériaux générés dans " + TargetFolder);
        }
    }

    private static Texture2D CreateFloorTexture()
    {
        Texture2D tex = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, true);
        Color baseSteel = new Color(0.22f, 0.24f, 0.27f);
        Color groove = new Color(0.08f, 0.09f, 0.10f);
        Color rivet = new Color(0.45f, 0.48f, 0.52f);

        for (int y = 0; y < TextureSize; y++)
        {
            for (int x = 0; x < TextureSize; x++)
            {
                float noise = Mathf.PerlinNoise(x * 0.04f, y * 0.04f) * 0.08f;
                Color col = baseSteel + new Color(noise, noise, noise);

                bool isGrooveX = (x % 64 < 3) || (x % 64 > 61);
                bool isGrooveY = (y % 64 < 3) || (y % 64 > 61);

                if (isGrooveX || isGrooveY)
                {
                    col = groove;
                }
                else
                {
                    int localX = x % 64;
                    int localY = y % 64;
                    bool isCornerRivet = (localX == 6 || localX == 58) && (localY == 6 || localY == 58);
                    if (isCornerRivet)
                    {
                        col = rivet;
                    }
                }

                tex.SetPixel(x, y, col);
            }
        }

        tex.Apply();
        return tex;
    }

    private static Texture2D CreateWallTexture()
    {
        Texture2D tex = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, true);
        Color darkPanel = new Color(0.18f, 0.19f, 0.21f);
        Color lightPanel = new Color(0.26f, 0.28f, 0.31f);
        Color seam = new Color(0.06f, 0.07f, 0.08f);

        for (int y = 0; y < TextureSize; y++)
        {
            for (int x = 0; x < TextureSize; x++)
            {
                float wear = Mathf.PerlinNoise(x * 0.02f, y * 0.08f) * 0.05f;
                bool isSeam = (x % 128 < 4) || (y % 256 < 4);

                if (isSeam)
                {
                    tex.SetPixel(x, y, seam);
                }
                else
                {
                    bool stripe = ((x / 32) % 2 == 0);
                    Color c = stripe ? lightPanel : darkPanel;
                    c += new Color(wear, wear, wear);
                    tex.SetPixel(x, y, c);
                }
            }
        }

        tex.Apply();
        return tex;
    }

    private static Texture2D CreateCeilingTexture()
    {
        Texture2D tex = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, true);
        Color plate = new Color(0.12f, 0.13f, 0.14f);
        Color pit = new Color(0.03f, 0.03f, 0.04f);

        for (int y = 0; y < TextureSize; y++)
        {
            for (int x = 0; x < TextureSize; x++)
            {
                int px = x % 16;
                int py = y % 16;
                bool isHole = (px >= 6 && px <= 10) && (py >= 6 && py <= 10);
                tex.SetPixel(x, y, isHole ? pit : plate);
            }
        }

        tex.Apply();
        return tex;
    }

    private static Texture2D CreateRockTexture()
    {
        Texture2D tex = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, true);
        Color rockBase = new Color(0.16f, 0.14f, 0.13f);

        for (int y = 0; y < TextureSize; y++)
        {
            for (int x = 0; x < TextureSize; x++)
            {
                float n1 = Mathf.PerlinNoise(x * 0.015f, y * 0.015f);
                float n2 = Mathf.PerlinNoise((x + 100) * 0.06f, (y + 100) * 0.06f) * 0.5f;
                float n3 = Mathf.PerlinNoise((x + 200) * 0.20f, (y + 200) * 0.20f) * 0.25f;

                float total = (n1 + n2 + n3) / 1.75f;
                float crater = total < 0.32f ? 0.45f : 1.0f;

                Color c = (rockBase * total * crater);
                tex.SetPixel(x, y, c);
            }
        }

        tex.Apply();
        return tex;
    }

    private static Texture2D CreateHazardTexture()
    {
        Texture2D tex = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, true);
        Color hazardYellow = new Color(0.95f, 0.72f, 0.05f);
        Color hazardBlack = new Color(0.10f, 0.10f, 0.11f);

        for (int y = 0; y < TextureSize; y++)
        {
            for (int x = 0; x < TextureSize; x++)
            {
                bool stripe = (((x + y) / 32) % 2 == 0);
                tex.SetPixel(x, y, stripe ? hazardYellow : hazardBlack);
            }
        }

        tex.Apply();
        return tex;
    }

    private static string SaveTexture(Texture2D tex, string fileName)
    {
        string fullPath = Path.Combine(TargetFolder, fileName);
        byte[] bytes = tex.EncodeToPNG();
        File.WriteAllBytes(fullPath, bytes);
        AssetDatabase.ImportAsset(fullPath, ImportAssetOptions.ForceUpdate);

        TextureImporter importer = AssetImporter.GetAtPath(fullPath) as TextureImporter;
        if (importer != null)
        {
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
        }

        return fullPath;
    }

    private static Material CreateOrUpdateMaterial(
        string matName,
        string texturePath,
        float metallic,
        float smoothness,
        Vector2 scale,
        bool isEmissive = false)
    {
        string matPath = $"{TargetFolder}/{matName}.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);

        if (mat == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, matPath);
        }

        Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);

        if (mat.HasProperty("_BaseMap"))
        {
            mat.SetTexture("_BaseMap", tex);
            mat.SetTextureScale("_BaseMap", scale);
        }
        else if (mat.HasProperty("_MainTex"))
        {
            mat.SetTexture("_MainTex", tex);
            mat.SetTextureScale("_MainTex", scale);
        }

        if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);

        if (isEmissive)
        {
            mat.EnableKeyword("_EMISSION");
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.SetColor("_EmissionColor", new Color(0.95f, 0.72f, 0.05f) * 0.4f);
            }
        }

        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        return mat;
    }
}
#endif
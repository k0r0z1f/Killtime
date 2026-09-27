using UnityEngine;
using UnityEditor;
using System.IO;

public static class PlanetTextureProcessor
{
    [MenuItem("Assets/Killtime/Process Planet Texture (Correct 2x1 Ratio + Extract Maps)", true)]
    private static bool ValidateProcessTexture()
    {
        return Selection.activeObject is Texture2D;
    }

    [MenuItem("Assets/Killtime/Process Planet Texture (Correct 2x1 Ratio + Extract Maps)")]
    public static void ProcessSelectedTexture()
    {
        Texture2D sourceTex = Selection.activeObject as Texture2D;
        if (sourceTex == null) return;

        string path = AssetDatabase.GetAssetPath(sourceTex);
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;

        if (importer != null && !importer.isReadable)
        {
            importer.isReadable = true;
            importer.maxTextureSize = 4096;
            importer.SaveAndReimport();
        }

        int srcWidth = sourceTex.width;
        int srcHeight = sourceTex.height;

        float validWidthRatio = 0.91f;
        int croppedWidth = Mathf.FloorToInt(srcWidth * validWidthRatio);

        int dstHeight = Mathf.ClosestPowerOfTwo(srcHeight);
        if (dstHeight > 2048) dstHeight = 2048;
        int dstWidth = dstHeight * 2;

        Texture2D albedoTex = new Texture2D(dstWidth, dstHeight, TextureFormat.RGBA32, false);
        Texture2D emissionTex = new Texture2D(dstWidth, dstHeight, TextureFormat.RGBA32, false);
        Texture2D maskTex = new Texture2D(dstWidth, dstHeight, TextureFormat.RGBA32, false);

        Color[] albedoPixels = new Color[dstWidth * dstHeight];
        Color[] emissionPixels = new Color[dstWidth * dstHeight];
        Color[] maskPixels = new Color[dstWidth * dstHeight];

        for (int y = 0; y < dstHeight; y++)
        {
            float v = (float)y / (dstHeight - 1);
            int srcY = Mathf.Clamp(Mathf.FloorToInt(v * (srcHeight - 1)), 0, srcHeight - 1);

            for (int x = 0; x < dstWidth; x++)
            {
                float u = (float)x / (dstWidth - 1);
                int srcX = Mathf.Clamp(Mathf.FloorToInt(u * (croppedWidth - 1)), 0, croppedWidth - 1);

                Color pixel = sourceTex.GetPixel(srcX, srcY);
                int dstIndex = y * dstWidth + x;

                albedoPixels[dstIndex] = pixel;

                bool isFire = (pixel.r > 0.45f && pixel.g > 0.18f && pixel.b < 0.35f && (pixel.r - pixel.b) > 0.22f);
                float fireIntensity = isFire ? Mathf.Clamp01((pixel.r - pixel.b) * 1.6f) : 0.0f;
                emissionPixels[dstIndex] = isFire ? pixel * fireIntensity * 2.5f : Color.black;

                bool isOcean = (pixel.b > pixel.r && pixel.b > pixel.g * 0.85f && pixel.r < 0.35f);
                float oceanSpec = isOcean ? 1.0f : 0.0f;
                float bombardmentMask = isFire ? 1.0f : 0.0f;

                maskPixels[dstIndex] = new Color(0.0f, 1.0f, bombardmentMask, oceanSpec);
            }
        }

        albedoTex.SetPixels(albedoPixels);
        albedoTex.Apply();

        emissionTex.SetPixels(emissionPixels);
        emissionTex.Apply();

        maskTex.SetPixels(maskPixels);
        maskTex.Apply();

        string dir = Path.GetDirectoryName(path);
        string baseName = Path.GetFileNameWithoutExtension(path);

        string albedoPath = Path.Combine(dir, $"{baseName}_Albedo_Fixed.png");
        string emissionPath = Path.Combine(dir, $"{baseName}_Emission_Fixed.png");
        string maskPath = Path.Combine(dir, $"{baseName}_Mask_Fixed.png");

        File.WriteAllBytes(albedoPath, albedoTex.EncodeToPNG());
        File.WriteAllBytes(emissionPath, emissionTex.EncodeToPNG());
        File.WriteAllBytes(maskPath, maskTex.EncodeToPNG());

        AssetDatabase.Refresh();

        ConfigureImporter(albedoPath, false);
        ConfigureImporter(emissionPath, false);
        ConfigureImporter(maskPath, true);

        Debug.Log($"Traitement terminé. Textures 2:1 rectifiées générées ({dstWidth}x{dstHeight}).");
    }

    private static void ConfigureImporter(string path, bool isMask)
    {
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return;

        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = !isMask;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.mipmapEnabled = true;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.maxTextureSize = 4096;
        importer.SaveAndReimport();
    }
}
using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Killtime.Tactics.Grid
{
    public partial class HexGridVisualizer : MonoBehaviour
    {
        private void InitializeMaterials()
        {
            if (_customTileMaterial != null)
            {
                _baseMaterial = new Material(_customTileMaterial) { name = "HexGrid_BaseMat_Instance" };
            }
            else
            {
                _baseMaterial = CreatePipelineSafeMaterial("HexGrid_Tile_Mat", _defaultTileColor);
            }

            if (_customObstacleMaterial != null)
            {
                _obstacleMaterial = new Material(_customObstacleMaterial) { name = "HexGrid_ObstacleMat_Instance" };
            }
            else
            {
                _obstacleMaterial = new Material(_baseMaterial) { name = "HexGrid_Obstacle_Mat" };
            }

            _ceilingMaterial = CreatePipelineSafeTransparentMaterial("HexGrid_Ceiling_Mat");
        }

        private Material CreatePipelineSafeTransparentMaterial(string materialName)
        {
            if (_customCeilingMaterial != null)
            {
                return new Material(_customCeilingMaterial) { name = materialName };
            }

            Shader targetShader = Shader.Find("Sprites/Default")
                               ?? Shader.Find("Universal Render Pipeline/Particles/Unlit")
                               ?? Shader.Find("Legacy Shaders/Transparent/Diffuse")
                               ?? Shader.Find("Unlit/Transparent");

            if (targetShader == null)
            {
                var currentRP = GraphicsSettings.currentRenderPipeline;
                if (currentRP != null)
                {
                    targetShader = Shader.Find("Universal Render Pipeline/Unlit");
                }
                else
                {
                    targetShader = Shader.Find("Standard");
                }
            }

            Material mat = new Material(targetShader) { name = materialName };

            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1.0f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0.0f);
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0.0f);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", (float)CullMode.Off);

            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.DisableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");

            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)RenderQueue.Transparent + 100;

            Color defaultGlass = new Color(0.0f, 0.85f, 1.0f, _ceilingOpacity);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", defaultGlass);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", defaultGlass);

            return mat;
        }

        private Material CreatePipelineSafeMaterial(string materialName, Color defaultColor)
        {
            Shader targetShader = null;

            var currentRP = GraphicsSettings.currentRenderPipeline;
            if (currentRP != null)
            {
                string rpName = currentRP.GetType().Name;
                if (rpName.Contains("Universal") || rpName.Contains("URP"))
                {
                    targetShader = Shader.Find("Universal Render Pipeline/Lit")
                                ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                                ?? Shader.Find("Universal Render Pipeline/Unlit");
                }
                else if (rpName.Contains("HighDefinition") || rpName.Contains("HDRP"))
                {
                    targetShader = Shader.Find("HDRP/Lit")
                                ?? Shader.Find("HDRP/Unlit");
                }
            }

            if (targetShader == null)
            {
                targetShader = Shader.Find("Killtime/TacticalLit")
                            ?? Shader.Find("Universal Render Pipeline/Lit")
                            ?? Shader.Find("Universal Render Pipeline/Simple Lit")
                            ?? Shader.Find("Universal Render Pipeline/Unlit")
                            ?? Shader.Find("Standard")
                            ?? Shader.Find("Unlit/Color")
                            ?? Shader.Find("Sprites/Default");
            }

            Material mat = new Material(targetShader) { name = materialName };

            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", defaultColor);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", defaultColor);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.05f);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.05f);

            return mat;
        }

        private static readonly Dictionary<string, Texture2D> _allKnownGroundTextures = new(StringComparer.OrdinalIgnoreCase);

        public Material GetOrCreateGroundMaterial(string textureRelativePath)
        {
            if (string.IsNullOrEmpty(textureRelativePath))
            {
                return _baseMaterial;
            }

            if (_groundMaterialCache.TryGetValue(textureRelativePath, out var mat) && mat != null)
            {
                return mat;
            }

            var tex = LoadGroundTexture(textureRelativePath);
            if (tex == null)
            {
                return _baseMaterial;
            }

            mat = new Material(_baseMaterial)
            {
                name = $"GroundMat_{tex.name}"
            };

            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Trilinear;
            tex.anisoLevel = 16;

            mat.mainTexture = tex;
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);

            // Suppression du spéculaire pour éliminer les micro-étincelles sur les textures bruitées
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.0f);
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", 0.0f);
            if (mat.HasProperty("_SpecularHighlights")) mat.SetFloat("_SpecularHighlights", 0.0f);

            _groundMaterialCache[textureRelativePath] = mat;
            return mat;
        }

        public Texture2D LoadGroundTexture(string textureRelativePath)
        {
            if (string.IsNullOrEmpty(textureRelativePath)) return null;

            if (_groundTextureCache.TryGetValue(textureRelativePath, out var cached) && cached != null)
            {
                return cached;
            }

            string baseName = Path.GetFileNameWithoutExtension(textureRelativePath);
            if (_allKnownGroundTextures.TryGetValue(baseName, out var byBase) && byBase != null)
            {
                _groundTextureCache[textureRelativePath] = byBase;
                return byBase;
            }

            var loaded = Resources.Load<Texture2D>($"Grounds/{textureRelativePath}")
                      ?? Resources.Load<Texture2D>(textureRelativePath)
                      ?? Resources.Load<Texture2D>($"Grounds/{baseName}");

            if (loaded == null)
            {
                var allResourcesGrounds = Resources.LoadAll<Texture2D>("Grounds");
                for (int i = 0; i < allResourcesGrounds.Length; i++)
                {
                    var rTex = allResourcesGrounds[i];
                    if (rTex == null) continue;

                    rTex.wrapMode = TextureWrapMode.Repeat;
                    rTex.filterMode = FilterMode.Trilinear;
                    rTex.anisoLevel = 16;
                    _allKnownGroundTextures[rTex.name] = rTex;

                    if (string.Equals(rTex.name, baseName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(rTex.name, textureRelativePath, StringComparison.OrdinalIgnoreCase))
                    {
                        loaded = rTex;
                    }
                }
            }

            if (loaded != null)
            {
                loaded.wrapMode = TextureWrapMode.Repeat;
                loaded.filterMode = FilterMode.Trilinear;
                loaded.anisoLevel = 16;

                _groundTextureCache[textureRelativePath] = loaded;
                _allKnownGroundTextures[textureRelativePath] = loaded;
                _allKnownGroundTextures[baseName] = loaded;
                return loaded;
            }

            return null;
        }

#if UNITY_EDITOR
        public static bool EnsureTextureImporterSettings(string assetPath)
        {
            var importer = UnityEditor.AssetImporter.GetAtPath(assetPath) as UnityEditor.TextureImporter;
            if (importer == null) return false;

            bool changed = false;

            if (importer.textureType != UnityEditor.TextureImporterType.Default)
            {
                importer.textureType = UnityEditor.TextureImporterType.Default;
                changed = true;
            }

            if (!importer.mipmapEnabled)
            {
                importer.mipmapEnabled = true;
                importer.mipmapFilter = UnityEditor.TextureImporterMipFilter.BoxFilter;
                changed = true;
            }

            if (importer.filterMode != FilterMode.Trilinear)
            {
                importer.filterMode = FilterMode.Trilinear;
                changed = true;
            }

            if (importer.anisoLevel < 16)
            {
                importer.anisoLevel = 16;
                changed = true;
            }

            if (importer.wrapMode != TextureWrapMode.Repeat)
            {
                importer.wrapMode = TextureWrapMode.Repeat;
                changed = true;
            }

            if (changed)
            {
                importer.SaveAndReimport();
                return true;
            }

            return false;
        }

        [UnityEditor.MenuItem("Killtime/Générer Mipmaps Textures Sols (Anti-Grésillement)")]
        private static void MenuBatchFixAllGroundTextureAssets()
        {
            BatchFixAllGroundTextureAssets();
        }
#endif

        public static void BatchFixAllGroundTextureAssets()
        {
#if UNITY_EDITOR
            if (Application.isPlaying)
            {
                Debug.LogWarning("[HexGridVisualizer] Veuillez arrêter le mode Play avant d'exécuter la réimportation des assets sur disque.");
                return;
            }

            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Resources/Grounds" });
            int fixedCount = 0;

            for (int i = 0; i < guids.Length; i++)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[i]);
                if (EnsureTextureImporterSettings(path))
                {
                    fixedCount++;
                }
            }

            UnityEditor.AssetDatabase.Refresh();
            Debug.Log($"[HexGridVisualizer] Mipmaps & Anisotropie 16x générés avec succès sur le disque pour {fixedCount} texture(s).");
#endif
        }
    }
}

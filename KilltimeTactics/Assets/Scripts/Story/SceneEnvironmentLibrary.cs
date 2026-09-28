using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using Killtime.Tactics.Grid;
using Killtime.Story.Data;

namespace Killtime.Story.Scenes
{
    /// <summary>
    /// Construction et gestion des environnements 3D de scène.
    /// Garantit l'exclusion mutuelle : si un préfab complet ou un prop existe,
    /// aucun placeholder primitif temporaire ne peut être instancié en doublon.
    /// </summary>
    public static class SceneEnvironmentLibrary
    {
        public static bool Build(string environmentId, Transform parent, TacticalHexGrid grid, List<ScenePlaceholderData> placeholders = null, SceneLightingData lighting = null)
        {
            ApplySceneLighting(lighting);

            string cleanId = !string.IsNullOrWhiteSpace(environmentId) ? environmentId.Trim() : "";

            // 1. Résolution dynamique du préfab d'environnement (ex: "AsteroidBase" <-> "Asteroid_Base")
            GameObject envPrefab = !string.IsNullOrEmpty(cleanId) ? ResolvePrefab(cleanId) : null;

            // 2. Vérification si une instance du décor existe déjà en scène (ex: instanciée via PlacedProps)
            GameObject existingEnvInstance = FindExistingEnvironmentInstance(cleanId, envPrefab != null ? envPrefab.name : null);

            if (envPrefab != null || existingEnvInstance != null)
            {
                string targetName = envPrefab != null ? envPrefab.name : cleanId;
                string envRootName = $"[Environment] {targetName}";

                // Nettoyage des résidus orphelins portant un autre nom
                CleanStrayEnvironments(parent, envRootName);

                // Recherche des coordonnées 3D exactes sauvegardées dans le JSON
                Vector3 savedPos = Vector3.zero;
                Vector3 savedRot = Vector3.zero;
                Vector3 savedScale = Vector3.one;
                bool hasSavedTransform = false;

                if (placeholders != null)
                {
                    for (int p = 0; p < placeholders.Count; p++)
                    {
                        var ph = placeholders[p];
                        if (ph != null && (string.Equals(ph.Id, targetName, StringComparison.OrdinalIgnoreCase) ||
                                          string.Equals(ph.Id, cleanId, StringComparison.OrdinalIgnoreCase)))
                        {
                            savedPos = ph.Position;
                            savedRot = ph.EulerAngles;
                            savedScale = ph.Scale;
                            hasSavedTransform = true;
                            break;
                        }
                    }
                }

                GameObject activeEnvInstance = existingEnvInstance;

                if (activeEnvInstance == null)
                {
                    activeEnvInstance = UnityEngine.Object.Instantiate(envPrefab, parent);
                    activeEnvInstance.name = envRootName;
                }
                else if (parent != null && activeEnvInstance.transform.parent != parent)
                {
                    activeEnvInstance.name = envRootName;
                }

                // Application rigoureuse des coordonnées sauvegardées
                if (hasSavedTransform)
                {
                    activeEnvInstance.transform.position = savedPos;
                    activeEnvInstance.transform.rotation = Quaternion.Euler(savedRot);
                    activeEnvInstance.transform.localScale = savedScale;
                }
                else
                {
                    activeEnvInstance.transform.localPosition = Vector3.zero;
                    activeEnvInstance.transform.localRotation = Quaternion.identity;
                    activeEnvInstance.transform.localScale = Vector3.one;
                }

                // Un préfab d'environnement complet est chargé : les placeholders primitifs internes
                // (anciens conduits, tables de fortune) sont STRICTEMENT neutralisés.
                // Seuls les éléments extérieurs lointains (ex: orbite à plus de 100m) n'ayant aucun préfab sont autorisés.
                if (placeholders != null && placeholders.Count > 0)
                {
                    BuildExternalCosmicPlaceholdersOnly(parent, placeholders);
                }

                Debug.Log($"[SceneEnvironmentLibrary] ✅ Environnement complet chargé via préfab '{targetName}'. Placeholders internes neutralisés.");
                return true;
            }

            // 3. Fallback : Aucun préfab d'environnement global trouvé. Déploiement des placeholders individuels.
            if (placeholders != null && placeholders.Count > 0)
            {
                string envRootName = $"[Environment] {(string.IsNullOrEmpty(cleanId) ? "Custom" : cleanId)}";
                CleanStrayEnvironments(parent, envRootName);

                GameObject root = new GameObject(envRootName);
                if (parent != null) root.transform.SetParent(parent, false);

                BuildPlaceholdersFromData(root.transform, placeholders);
                return true;
            }

            Debug.LogWarning($"[SceneEnvironmentLibrary] Aucun préfab d'environnement ni aucun placeholder trouvé pour '{cleanId}'.");
            return false;
        }

        private static void BuildPlaceholdersFromData(Transform root, List<ScenePlaceholderData> placeholders)
        {
            if (root == null || placeholders == null) return;

            for (int i = 0; i < placeholders.Count; i++)
            {
                var p = placeholders[i];
                if (p == null || string.IsNullOrWhiteSpace(p.Id)) continue;

                // Si un objet portant cet identifiant existe déjà en scène (ex: chargé par PlacedProps), ne pas dupliquer
                if (IsObjectAlreadyInScene(p.Id))
                {
                    Debug.Log($"[SceneEnvironmentLibrary] ℹ️ '{p.Id}' existe déjà en scène. Placeholder ignoré.");
                    continue;
                }

                // Si un préfab physique existe pour ce placeholder, instancier le préfab (JAMAIS la primitive)
                GameObject prefab = ResolvePrefab(p.Id);
                if (prefab != null)
                {
                    DestroyDuplicateCustomProp(prefab.name);

                    GameObject instance = UnityEngine.Object.Instantiate(prefab, root);
                    instance.name = prefab.name;
                    instance.transform.position = p.Position;
                    instance.transform.rotation = Quaternion.Euler(p.EulerAngles);
                    instance.transform.localScale = p.Scale;

                    SetHierarchyLayerAndActive(instance, 0);

                    if (p.HasLight)
                    {
                        var lightComp = instance.GetComponent<Light>() ?? instance.AddComponent<Light>();
                        lightComp.type = p.LightType;
                        lightComp.color = p.LightColor;
                        lightComp.intensity = p.LightIntensity;
                        lightComp.range = p.LightRange;
                    }
                    continue;
                }

                // Fallback primitif uniquement si aucun préfab n'existe nulle part
                PrimitiveType pt = p.Primitive switch
                {
                    ScenePrimitiveKind.Cube => PrimitiveType.Cube,
                    ScenePrimitiveKind.Cylinder => PrimitiveType.Cylinder,
                    ScenePrimitiveKind.Capsule => PrimitiveType.Capsule,
                    ScenePrimitiveKind.Quad => PrimitiveType.Quad,
                    _ => PrimitiveType.Sphere
                };

                GameObject go = GameObject.CreatePrimitive(pt);
                go.name = p.Id;
                go.transform.SetParent(root, false);
                go.transform.position = p.Position;
                go.transform.rotation = Quaternion.Euler(p.EulerAngles);
                go.transform.localScale = p.Scale;

                Material mat = CreateMaterial(p.Color, p.Smoothness, p.IsEmissive, p.EmissionColor);
                Renderer r = go.GetComponent<Renderer>();
                if (r != null) r.sharedMaterial = mat;

                if (p.HasLight)
                {
                    var lightComp = go.AddComponent<Light>();
                    lightComp.type = p.LightType;
                    lightComp.color = p.LightColor;
                    lightComp.intensity = p.LightIntensity;
                    lightComp.range = p.LightRange;
                }
            }
        }

        private static void BuildExternalCosmicPlaceholdersOnly(Transform parent, List<ScenePlaceholderData> placeholders)
        {
            Transform externalRoot = parent != null ? parent.Find("[Environment_Distant]") : null;
            if (externalRoot == null)
            {
                GameObject extGo = new GameObject("[Environment_Distant]");
                if (parent != null) extGo.transform.SetParent(parent, false);
                externalRoot = extGo.transform;
            }

            for (int i = 0; i < placeholders.Count; i++)
            {
                var p = placeholders[i];
                if (p == null || string.IsNullOrWhiteSpace(p.Id)) continue;

                // Ignorer formellement tout objet local à la base (< 100m du centre)
                if (p.Position.magnitude < 100f) continue;

                if (IsObjectAlreadyInScene(p.Id)) continue;

                GameObject prefab = ResolvePrefab(p.Id);
                if (prefab != null)
                {
                    GameObject instance = UnityEngine.Object.Instantiate(prefab, externalRoot);
                    instance.name = prefab.name;
                    instance.transform.position = p.Position;
                    instance.transform.rotation = Quaternion.Euler(p.EulerAngles);
                    instance.transform.localScale = p.Scale;
                    SetHierarchyLayerAndActive(instance, 0);
                    continue;
                }

                // Primitive lointaine
                PrimitiveType pt = p.Primitive == ScenePrimitiveKind.Cube ? PrimitiveType.Cube : PrimitiveType.Sphere;
                GameObject go = GameObject.CreatePrimitive(pt);
                go.name = p.Id;
                go.transform.SetParent(externalRoot, false);
                go.transform.position = p.Position;
                go.transform.rotation = Quaternion.Euler(p.EulerAngles);
                go.transform.localScale = p.Scale;

                Material mat = CreateMaterial(p.Color, p.Smoothness, p.IsEmissive, p.EmissionColor);
                Renderer r = go.GetComponent<Renderer>();
                if (r != null) r.sharedMaterial = mat;
            }
        }

        public static GameObject ResolvePrefab(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;

            var candidates = GenerateNameVariations(id);

            for (int i = 0; i < candidates.Count; i++)
            {
                string name = candidates[i];
                if (string.IsNullOrWhiteSpace(name)) continue;

                var prefab = Resources.Load<GameObject>($"Prefabs/Environment/{name}")
                          ?? Resources.Load<GameObject>($"Environment/{name}")
                          ?? Resources.Load<GameObject>($"Prefabs/Props/{name}")
                          ?? Resources.Load<GameObject>($"Prefabs/{name}")
                          ?? Resources.Load<GameObject>($"Props/{name}")
                          ?? Resources.Load<GameObject>($"Environments/{name}")
                          ?? Resources.Load<GameObject>(name);

                if (prefab != null) return prefab;

#if UNITY_EDITOR
                string[] guids = UnityEditor.AssetDatabase.FindAssets($"{name} t:Prefab", new[] { "Assets" });
                foreach (string guid in guids)
                {
                    string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                    if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                    {
                        string fName = System.IO.Path.GetFileNameWithoutExtension(path);
                        if (string.Equals(fName, name, StringComparison.OrdinalIgnoreCase))
                        {
                            var loaded = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                            if (loaded != null) return loaded;
                        }
                    }
                }
#endif
            }

            return null;
        }

        private static List<string> GenerateNameVariations(string raw)
        {
            var results = new List<string>();
            if (string.IsNullOrWhiteSpace(raw)) return results;

            string clean = raw.Trim();
            results.Add(clean);

            // Suppression des tags de modèles
            string stripped = clean.Replace("_Model", "").Replace("Model_", "")
                                   .Replace("_model", "").Replace("model_", "")
                                   .Replace("_Prefab", "").Replace("Prefab_", "")
                                   .Replace("_prefab", "").Replace("prefab_", "").Trim();
            if (!results.Contains(stripped)) results.Add(stripped);

            // Conversion CamelCase <-> snake_case
            string withUnderscore = Regex.Replace(stripped, "(?<=[a-z])([A-Z])", "_$1");
            if (!results.Contains(withUnderscore)) results.Add(withUnderscore);

            string withoutUnderscore = stripped.Replace("_", "");
            if (!results.Contains(withoutUnderscore)) results.Add(withoutUnderscore);

            return results;
        }

        private static GameObject FindExistingEnvironmentInstance(string cleanId, string prefabName)
        {
            var candidates = new List<string>();
            if (!string.IsNullOrEmpty(cleanId)) candidates.AddRange(GenerateNameVariations(cleanId));
            if (!string.IsNullOrEmpty(prefabName)) candidates.AddRange(GenerateNameVariations(prefabName));

            for (int i = 0; i < candidates.Count; i++)
            {
                string name = candidates[i];
                var go = GameObject.Find($"[Environment] {name}")
                      ?? GameObject.Find(name)
                      ?? GameObject.Find($"{name}(Clone)")
                      ?? GameObject.Find($"Prop_{name}");

                if (go != null) return go;
            }

            return null;
        }

        private static bool IsObjectAlreadyInScene(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;

            var variations = GenerateNameVariations(id);
            for (int i = 0; i < variations.Count; i++)
            {
                string v = variations[i];
                if (GameObject.Find(v) != null || GameObject.Find($"{v}(Clone)") != null || GameObject.Find($"Prop_{v}") != null)
                {
                    return true;
                }
            }

            return false;
        }

        private static void CleanStrayEnvironments(Transform parent, string currentValidName)
        {
            if (parent != null)
            {
                for (int i = parent.childCount - 1; i >= 0; i--)
                {
                    var child = parent.GetChild(i);
                    if (child != null && child.name.StartsWith("[Environment]") && !string.Equals(child.name, currentValidName, StringComparison.OrdinalIgnoreCase))
                    {
                        UnityEngine.Object.DestroyImmediate(child.gameObject);
                    }
                }
            }

            var strayRoot = GameObject.Find(currentValidName);
            if (strayRoot != null && (parent == null || strayRoot.transform.parent != parent))
            {
                UnityEngine.Object.DestroyImmediate(strayRoot);
            }
        }

        public static void ApplySceneLighting(SceneLightingData lighting)
        {
            if (lighting == null || !lighting.OverrideLighting) return;

            var lights = UnityEngine.Object.FindObjectsByType<Light>();
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i] != null && lights[i].type == LightType.Directional)
                {
                    lights[i].transform.position = lighting.SunPosition;
                    lights[i].transform.rotation = Quaternion.Euler(lighting.SunEulerAngles);
                    lights[i].color = lighting.SunColor;
                    lights[i].intensity = lighting.SunIntensity;
                    lights[i].shadows = lighting.Shadows;
                    lights[i].shadowStrength = lighting.ShadowStrength;
                    lights[i].cullingMask = ~0;
                    break;
                }
            }

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = lighting.AmbientColor;
        }

        private static void SetHierarchyLayerAndActive(GameObject obj, int layer)
        {
            if (obj == null) return;
            obj.layer = layer;
            obj.SetActive(true);
            for (int i = 0; i < obj.transform.childCount; i++)
            {
                SetHierarchyLayerAndActive(obj.transform.GetChild(i).gameObject, layer);
            }
        }

        private static void DestroyDuplicateCustomProp(string prefabName)
        {
            if (string.IsNullOrWhiteSpace(prefabName)) return;

            var propsRoot = GameObject.Find("[TacticalMap_CustomProps]");
            if (propsRoot == null) return;

            for (int i = propsRoot.transform.childCount - 1; i >= 0; i--)
            {
                var child = propsRoot.transform.GetChild(i);
                if (child != null && child.name.IndexOf(prefabName, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
                }
            }
        }

        private static Material CreateMaterial(Color albedo, float smoothness, bool isEmissive, Color emissionColor)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Diffuse");
            Material m = new Material(sh);
            m.color = albedo;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", albedo);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);

            if (isEmissive)
            {
                m.EnableKeyword("_EMISSION");
                if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", emissionColor * 2.0f);
            }
            return m;
        }
    }
}
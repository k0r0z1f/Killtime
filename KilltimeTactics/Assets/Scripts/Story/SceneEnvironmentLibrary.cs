using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics.Grid;
using Killtime.Story.Data;

namespace Killtime.Story.Scenes
{
    /// <summary>
    /// Construction des environnements 3D par identifiant. La scène (JSON)
    /// déclare son environnement via StorySceneData.EnvironmentId ; les
    /// constructeurs restent du code car la géométrie 3D ne peut pas vivre
    /// dans un JSON narratif.
    /// </summary>
    public static class SceneEnvironmentLibrary
    {
        public static bool Build(string environmentId, Transform parent, TacticalHexGrid grid, List<ScenePlaceholderData> placeholders = null)
        {
            if (string.IsNullOrWhiteSpace(environmentId)) return false;

            string cleanId = environmentId.Trim();
            string envRootName = $"[Environment] {cleanId}";

            GameObject existingInScene = null;
            var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (activeScene.isLoaded)
            {
                var rootObjects = activeScene.GetRootGameObjects();
                for (int i = 0; i < rootObjects.Length; i++)
                {
                    var r = rootObjects[i];
                    if (r != null && (string.Equals(r.name, envRootName, StringComparison.OrdinalIgnoreCase)
                                   || string.Equals(r.name, cleanId, StringComparison.OrdinalIgnoreCase)))
                    {
                        existingInScene = r;
                        break;
                    }
                }
            }

            if (existingInScene == null)
            {
                existingInScene = GameObject.Find(envRootName) ?? GameObject.Find(cleanId);
            }

            if (existingInScene != null)
            {
                existingInScene.SetActive(true);
                if (parent != null && existingInScene.transform.parent != parent)
                {
                    existingInScene.transform.SetParent(parent, true);
                }
                return true;
            }

            if (parent != null)
            {
                Transform orphan = parent.Find(envRootName);
                if (orphan != null)
                {
                    UnityEngine.Object.Destroy(orphan.gameObject);
                }
            }

            GameObject prefab = Resources.Load<GameObject>($"Environments/{cleanId}") 
                             ?? Resources.Load<GameObject>($"Prefabs/Environments/{cleanId}")
                             ?? Resources.Load<GameObject>($"Maps/Environments/{cleanId}");

            if (prefab != null)
            {
                GameObject instance = UnityEngine.Object.Instantiate(prefab, parent);
                instance.name = envRootName;
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;
                instance.transform.localScale = Vector3.one;
                return true;
            }

            if (placeholders != null && placeholders.Count > 0)
            {
                GameObject root = new GameObject(envRootName);
                if (parent != null) root.transform.SetParent(parent, false);
                BuildPlaceholdersFromData(root.transform, placeholders);
                return true;
            }

            Debug.LogWarning($"[SceneEnvironmentLibrary] Environnement '{cleanId}' introuvable dans la scène active, aucun prefab sous 'Resources/Environments/{cleanId}' et aucun placeholder dans le JSON.");
            return false;
        }

        private static void BuildPlaceholdersFromData(Transform root, List<ScenePlaceholderData> placeholders)
        {
            if (root == null || placeholders == null) return;

            for (int i = 0; i < placeholders.Count; i++)
            {
                var p = placeholders[i];
                if (p == null) continue;

                PrimitiveType pt = p.Primitive switch
                {
                    ScenePrimitiveKind.Cube => PrimitiveType.Cube,
                    ScenePrimitiveKind.Cylinder => PrimitiveType.Cylinder,
                    ScenePrimitiveKind.Capsule => PrimitiveType.Capsule,
                    ScenePrimitiveKind.Quad => PrimitiveType.Quad,
                    _ => PrimitiveType.Sphere
                };

                GameObject go = GameObject.CreatePrimitive(pt);
                go.name = string.IsNullOrEmpty(p.Id) ? $"Placeholder_{i + 1}" : p.Id;
                go.transform.SetParent(root, false);
                go.transform.position = p.Position;
                go.transform.rotation = Quaternion.Euler(p.EulerAngles);
                go.transform.localScale = p.Scale;

                Material mat = CreateMaterial(p.Color, p.Smoothness, p.IsEmissive, p.EmissionColor);
                Renderer r = go.GetComponent<Renderer>();
                if (r != null) r.sharedMaterial = mat;
            }
        }

        private static Material CreateMaterial(Color albedo, float smoothness, bool isEmissive, Color emissionColor)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Diffuse");
            Material m = new Material(sh);
            m.color = albedo;
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
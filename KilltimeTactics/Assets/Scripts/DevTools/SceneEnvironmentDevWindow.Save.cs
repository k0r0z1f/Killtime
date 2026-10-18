// PARTIAL-SPLIT: SceneEnvironmentDevWindow.Save.cs - onglet sauvegarde JSON et cameras. Deplacement verbatim, logique inchangee.
using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Story;
using Killtime.Story.Data;
using Killtime.Story.Scenes;
using Killtime.CameraSystem;
using Killtime.Tactics.Grid;

namespace Killtime.UI
{
    public partial class SceneEnvironmentDevWindow : FloatingWindow<SceneEnvironmentDevWindow>
    {
        private void DrawSaveTab()
        {
            GUILayout.Label("<b>Sauvegarde Directe dans le Fichier JSON :</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            string path = StorySceneRepository.GetSceneFilePath(_activeData.SceneId);
            GUILayout.Label($"Fichier cible : <b>{path}</b>");

            GUILayout.Space(6);
            GUI.backgroundColor = new Color(0.2f, 0.85f, 0.45f);
            if (GUILayout.Button($"💾 Enregistrer les Modifications dans {_activeData.SceneId}.json", GUILayout.Height(38)))
            {
                SaveEnvironmentToJson();
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndVertical();
        }

        private void SaveEnvironmentToJson()
        {
            if (_activeData == null) return;

            string path = StorySceneRepository.GetSceneFilePath(_activeData.SceneId);
            try
            {
                string json = JsonUtility.ToJson(_activeData, true);
                File.WriteAllText(path, json);
                _statusMessage = $"✅ Fichier '{_activeData.SceneId}.json' mis à jour sur disque.";
                CombatHUD.Instance?.AddAdvancedLog($"💾 <b>Environnement sauvegardé :</b> {_activeData.SceneId}.json mis à jour.", LogCategory.MovementAndTurns, "[ENVIRONNEMENT]", Color.green);
            }
            catch (Exception ex)
            {
                _statusMessage = $"❌ Échec de la sauvegarde : {ex.Message}";
                Debug.LogError($"[SceneEnvironmentDevWindow] {ex.Message}");
            }
        }

        private GameObject FindSceneObjectUniversal(string targetId)
        {
            if (string.IsNullOrWhiteSpace(targetId)) return null;

            string cleanId = targetId.Replace("_Model", "").Replace("Model_", "").Trim();

            var direct = GameObject.Find(targetId) 
                      ?? GameObject.Find(cleanId)
                      ?? GameObject.Find($"{targetId}(Clone)")
                      ?? GameObject.Find($"{cleanId}(Clone)")
                      ?? GameObject.Find($"Prop_{targetId}")
                      ?? GameObject.Find($"Prop_{cleanId}")
                      ?? GameObject.Find($"[Environment] {targetId}")
                      ?? GameObject.Find($"[Environment] {cleanId}");
            if (direct != null) return direct;

            var propsRoot = GameObject.Find("[TacticalMap_CustomProps]");
            if (propsRoot != null)
            {
                for (int i = 0; i < propsRoot.transform.childCount; i++)
                {
                    var ch = propsRoot.transform.GetChild(i);
                    if (ch != null && (ch.name.IndexOf(targetId, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                      ch.name.IndexOf(cleanId, StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        return ch.gameObject;
                    }
                }
            }

            string envName = _activeData != null ? _activeData.EnvironmentId : "";
            if (!string.IsNullOrEmpty(envName))
            {
                var envRoot = GameObject.Find($"[Environment] {envName}") ?? GameObject.Find("[Environment]");
                if (envRoot != null)
                {
                    if (envRoot.name.IndexOf(targetId, StringComparison.OrdinalIgnoreCase) >= 0) return envRoot;
                    for (int i = 0; i < envRoot.transform.childCount; i++)
                    {
                        var ch = envRoot.transform.GetChild(i);
                        if (ch != null && (ch.name.IndexOf(targetId, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                          ch.name.IndexOf(cleanId, StringComparison.OrdinalIgnoreCase) >= 0))
                        {
                            return ch.gameObject;
                        }
                    }
                }
            }

            var rootObjects = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
            for (int i = 0; i < rootObjects.Length; i++)
            {
                var root = rootObjects[i];
                if (root == null) continue;
                if (root.name.IndexOf(targetId, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    root.name.IndexOf(cleanId, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return root;
                }
            }

            var allTransforms = FindObjectsByType<Transform>();
            for (int i = 0; i < allTransforms.Length; i++)
            {
                var t = allTransforms[i];
                if (t != null && (t.name.IndexOf(targetId, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 t.name.IndexOf(cleanId, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    return t.gameObject;
                }
            }

            return null;
        }

        private void ForceFrameObjectInView(GameObject sceneObj)
        {
            if (sceneObj == null || UnityEngine.Camera.main == null) return;

            _inspectedObject = sceneObj;

            var tcc = FindAnyObjectByType<TacticalCameraController>();
            if (tcc != null) tcc.enabled = false;

            var cd = FindAnyObjectByType<CinematicDirector>();
            if (cd != null) cd.SetTacticalControlEnabled(false);

            Camera cam = UnityEngine.Camera.main;
            cam.cullingMask = ~0;
            cam.farClipPlane = 15000f;
            cam.fieldOfView = 45f;

            float measuredRadius = 20f;
            var renderers = sceneObj.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                Bounds b = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
                measuredRadius = Mathf.Max(b.extents.x, b.extents.y, b.extents.z);
            }

            _cameraOrbitDistance = Mathf.Max(measuredRadius * 2.2f, 30f);
            // Préserve la précision depth au loin : near proportionnel à la distance
            // d'inspection (planète r=1000 -> recul ~2200m -> near ~11m).
            // Sans ça, far/near = 15000/0.3 = 50k -> z-fighting surface/nuages.
            cam.nearClipPlane = Mathf.Max(0.3f, _cameraOrbitDistance * 0.005f);
            UpdateLockedInspectionCamera();

            _statusMessage = $"Caméra braquée sur '{sceneObj.name}' à {_cameraOrbitDistance:0}m de recul.";
        }

        private void UpdateLockedInspectionCamera()
        {
            if (_inspectedObject == null || UnityEngine.Camera.main == null) return;

            Camera cam = UnityEngine.Camera.main;
            Vector3 targetCenter = _inspectedObject.transform.position;

            Vector3 offsetDir = (Vector3.zero - targetCenter).normalized;
            if (offsetDir == Vector3.zero) offsetDir = -Vector3.forward;

            Vector3 camPos = targetCenter + (offsetDir * _cameraOrbitDistance);
            cam.transform.position = camPos;
            cam.transform.rotation = Quaternion.LookRotation(targetCenter - camPos);
        }

        private void RestoreTacticalCamera()
        {
            _inspectedObject = null;

            if (UnityEngine.Camera.main != null)
                UnityEngine.Camera.main.nearClipPlane = 0.3f;

            var tcc = FindAnyObjectByType<TacticalCameraController>();
            if (tcc != null) tcc.enabled = true;

            var cd = FindAnyObjectByType<CinematicDirector>();
            if (cd != null) cd.ReleaseToTactical();

            _statusMessage = "Caméra tactique rétablie sur la grille.";
        }
    }
}

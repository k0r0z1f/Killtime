// PARTIAL-SPLIT: SceneEnvironmentDevWindow.Objects.cs - onglet objets et transforms. Deplacement verbatim, logique inchangee.
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
        private void DrawObjectsTab()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>1. Objets de l'Environnement ({_unifiedItems.Count}) :</b>");
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("🔄 Actualiser Liste", GUILayout.Width(130), GUILayout.Height(20)))
            {
                RebuildUnifiedItemList();
            }
            GUILayout.EndHorizontal();

            _selectedItemIndex = Mathf.Clamp(_selectedItemIndex, 0, _unifiedItems.Count - 1);
            var currentItem = _unifiedItems[_selectedItemIndex];

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("◀", GUILayout.Width(30), GUILayout.Height(24)))
            {
                _selectedItemIndex = (_selectedItemIndex - 1 + _unifiedItems.Count) % _unifiedItems.Count;
            }

            GUI.backgroundColor = _showObjectList ? new Color(0.2f, 0.8f, 1f) : Color.white;
            if (GUILayout.Button($"<b>{currentItem.DisplayLabel}</b> ({_selectedItemIndex + 1}/{_unifiedItems.Count}) ▼", GUILayout.Height(24)))
            {
                _showObjectList = !_showObjectList;
            }
            GUI.backgroundColor = Color.white;

            if (GUILayout.Button("▶", GUILayout.Width(30), GUILayout.Height(24)))
            {
                _selectedItemIndex = (_selectedItemIndex + 1) % _unifiedItems.Count;
            }
            GUILayout.EndHorizontal();

            if (_showObjectList)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                _objectListScroll = GUILayout.BeginScrollView(_objectListScroll, GUILayout.Height(150));
                for (int i = 0; i < _unifiedItems.Count; i++)
                {
                    bool isSelected = (i == _selectedItemIndex);
                    GUI.backgroundColor = isSelected ? new Color(0.2f, 0.8f, 1f) : Color.white;
                    if (GUILayout.Button(_unifiedItems[i].DisplayLabel))
                    {
                        _selectedItemIndex = i;
                        _showObjectList = false;
                    }
                    GUI.backgroundColor = Color.white;
                }
                GUILayout.EndScrollView();
                GUILayout.EndVertical();
            }

            if (currentItem.SceneObject == null)
            {
                currentItem.SceneObject = FindSceneObjectUniversal(currentItem.Id);
            }

            GUILayout.Space(6);
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>Édition : <color=#00E5FF>{currentItem.Id}</color></b> <color=grey>({currentItem.Source})</color>");
            GUILayout.FlexibleSpace();

            if (currentItem.SceneObject != null)
            {
                GUI.backgroundColor = new Color(0.2f, 0.85f, 0.4f);
                if (GUILayout.Button("🎯 Cadrer la Caméra", GUILayout.Width(140), GUILayout.Height(22)))
                {
                    ForceFrameObjectInView(currentItem.SceneObject);
                }
                GUI.backgroundColor = Color.white;
            }

            if (GUILayout.Button("🎥 Restaurer Caméra", GUILayout.Width(140), GUILayout.Height(22)))
            {
                RestoreTacticalCamera();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();

            if (currentItem.PlaceholderRef == null)
            {
                GUI.backgroundColor = new Color(0.25f, 0.75f, 1.0f);
                if (GUILayout.Button($"📥 Convertir en Placeholder", GUILayout.Height(24)))
                {
                    ConvertItemToPlaceholder(currentItem);
                }
                GUI.backgroundColor = Color.white;
            }

            GUI.backgroundColor = new Color(0.85f, 0.2f, 0.2f);
            if (GUILayout.Button($"🗑️ Supprimer définitivement du JSON", GUILayout.Height(24)))
            {
                DeleteCurrentItem(currentItem);
                GUI.backgroundColor = Color.white;
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
                return;
            }
            GUI.backgroundColor = Color.white;

            GUILayout.EndHorizontal();

            if (currentItem.SceneObject != null)
            {
                DrawSpatialImmersionCheck(currentItem.SceneObject);
            }
            GUILayout.EndVertical();

            GUILayout.Space(6);
            bool editingLocal = currentItem.PlaceholderRef != null && currentItem.PlaceholderRef.ParentToEnvironment;
            GUILayout.Label(editingLocal ? "<b>2. Position Locale au décor (X, Y, Z) — <color=#00E5FF>hérite l'alignement</color> :</b>" : "<b>2. Position Monde (X, Y, Z) :</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            Vector3 pos = currentItem.Position;
            pos.x = DrawWideAxisControl("Position X", pos.x, -2500f, 2500f, 10f);
            pos.y = DrawWideAxisControl("Position Y (Hauteur)", pos.y, -1000f, 1500f, 5f);
            pos.z = DrawWideAxisControl("Position Z", pos.z, -2500f, 2500f, 10f);

            Vector3 rot = currentItem.EulerAngles;
            Vector3 sca = currentItem.Scale;

            if (pos != currentItem.Position)
            {
                ApplyTransformChangesToItem(currentItem, pos, rot, sca);
            }
            GUILayout.EndVertical();

            GUILayout.Space(6);
            GUILayout.Label("<b>3. Rotation Euler (Pitch, Yaw, Roll) :</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            rot.x = DrawAngleControl("Pitch (X) Assiette", rot.x);
            rot.y = DrawAngleControl("Yaw (Y) Direction", rot.y);
            rot.z = DrawAngleControl("Roll (Z) Roulis", rot.z);

            if (rot != currentItem.EulerAngles)
            {
                ApplyTransformChangesToItem(currentItem, currentItem.Position, rot, sca);
            }
            GUILayout.EndVertical();

            GUILayout.Space(6);
            GUILayout.Label("<b>4. Échelle (Scale) :</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            float uniformScale = currentItem.Scale.x;
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Échelle Globale : <b>{uniformScale:0.####}x</b>", GUILayout.Width(170));
            float newScale = GUILayout.HorizontalSlider(uniformScale, 0.001f, 30f);
            if (Mathf.Abs(newScale - uniformScale) > 0.0001f)
            {
                ApplyTransformChangesToItem(currentItem, currentItem.Position, currentItem.EulerAngles, Vector3.one * newScale);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("0.016x")) ApplyScaleToItem(currentItem, 0.016f);
            if (GUILayout.Button("0.1x")) ApplyScaleToItem(currentItem, 0.1f);
            if (GUILayout.Button("1x")) ApplyScaleToItem(currentItem, 1.0f);
            if (GUILayout.Button("8x")) ApplyScaleToItem(currentItem, 8.0f);
            if (GUILayout.Button("16x")) ApplyScaleToItem(currentItem, 16.0f);
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }

        /// <summary>
        /// Vrai si l'objet vit sous la racine d'environnement ([Environment]*) :
        /// ses coordonnées JSON sont alors locales au décor (alignement hérité).
        /// </summary>
        private bool IsChildOfEnvironment(UnifiedEnvItem item)
        {
            if (item == null) return false;
            if (item.PlaceholderRef != null && item.PlaceholderRef.ParentToEnvironment) return true;
            var go = item.SceneObject;
            if (go == null && !string.IsNullOrWhiteSpace(item.Id))
            {
                go = FindSceneObjectUniversal(item.Id);
                if (go != null) item.SceneObject = go;
            }
            if (go == null) return false;
            var p = go.transform.parent;
            while (p != null)
            {
                if (p.name.StartsWith("[Environment]")) return true;
                p = p.parent;
            }
            return false;
        }

        private void ApplyTransformChangesToItem(UnifiedEnvItem item, Vector3 newPos, Vector3 newRot, Vector3 newScale)
        {
            item.Position = newPos;
            item.EulerAngles = newRot;
            item.Scale = newScale;

            if (item.SceneObject == null)
            {
                item.SceneObject = FindSceneObjectUniversal(item.Id);
            }

            // Référentiel local si placeholder enfant du décor : l'alignement est hérité.
            bool useLocal = IsChildOfEnvironment(item);

            if (item.SceneObject != null)
            {
                if (useLocal)
                {
                    item.SceneObject.transform.localPosition = newPos;
                    item.SceneObject.transform.localRotation = Quaternion.Euler(newRot);
                    item.SceneObject.transform.localScale = newScale;
                }
                else
                {
                    item.SceneObject.transform.position = newPos;
                    item.SceneObject.transform.rotation = Quaternion.Euler(newRot);
                    item.SceneObject.transform.localScale = newScale;
                }
            }

            // Assurer une entrée 3D continue dans EnvironmentPlaceholders pour TOUT objet édité
            if (item.PlaceholderRef == null && _activeData != null)
            {
                _activeData.EnvironmentPlaceholders ??= new List<ScenePlaceholderData>();

                for (int i = 0; i < _activeData.EnvironmentPlaceholders.Count; i++)
                {
                    var existingPh = _activeData.EnvironmentPlaceholders[i];
                    if (existingPh != null && string.Equals(existingPh.Id, item.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        item.PlaceholderRef = existingPh;
                        break;
                    }
                }

                if (item.PlaceholderRef == null)
                {
                    var newPh = new ScenePlaceholderData
                    {
                        Id = item.Id,
                        Primitive = ScenePrimitiveKind.Cube,
                        Position = newPos,
                        EulerAngles = newRot,
                        Scale = newScale,
                        Color = Color.white,
                        Smoothness = 0.2f,
                        ParentToEnvironment = useLocal
                    };
                    _activeData.EnvironmentPlaceholders.Add(newPh);
                    item.PlaceholderRef = newPh;
                }
            }

            if (item.PlaceholderRef != null)
            {
                item.PlaceholderRef.Position = newPos;
                item.PlaceholderRef.EulerAngles = newRot;
                item.PlaceholderRef.Scale = newScale;
                if (useLocal) item.PlaceholderRef.ParentToEnvironment = true;
            }

            if (item.Source == EnvItemSource.PlacedProp && _activeData.EmbeddedMap != null && _activeData.EmbeddedMap.PlacedProps != null)
            {
                var (q, r) = WorldToHex(newPos);
                for (int i = 0; i < _activeData.EmbeddedMap.PlacedProps.Count; i++)
                {
                    var p = _activeData.EmbeddedMap.PlacedProps[i];
                    if (p != null && (string.Equals(p.PrefabName, item.Id, StringComparison.OrdinalIgnoreCase) ||
                                      item.Id.IndexOf(p.PrefabName, StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        p.Q = q;
                        p.R = r;
                        p.HeightOffset = newPos.y;
                        p.RotationY = newRot.y;
                        p.Scale = newScale.x;
                        item.DisplayLabel = $"[Prop Carte] {p.PrefabName} ({p.Q},{p.R})";
                        break;
                    }
                }
            }
        }

        private void ConvertItemToPlaceholder(UnifiedEnvItem item)
        {
            if (item == null || _activeData == null) return;

            _activeData.EnvironmentPlaceholders ??= new List<ScenePlaceholderData>();

            // Objet parenté sous le décor : on stocke le transform LOCAL + le drapeau
            // ParentToEnvironment pour que le runtime le ré-instancie en enfant (alignement hérité).
            bool childOfEnv = IsChildOfEnvironment(item);
            Vector3 storePos = item.Position;
            Vector3 storeRot = item.EulerAngles;
            if (childOfEnv && item.SceneObject != null)
            {
                storePos = item.SceneObject.transform.localPosition;
                storeRot = item.SceneObject.transform.localRotation.eulerAngles;
                item.Position = storePos;
                item.EulerAngles = storeRot;
            }

            var newPlaceholder = new ScenePlaceholderData
            {
                Id = item.Id,
                Primitive = ScenePrimitiveKind.Cube,
                Position = storePos,
                EulerAngles = storeRot,
                Scale = item.Scale,
                Color = Color.white,
                Smoothness = 0.2f,
                ParentToEnvironment = childOfEnv
            };

            _activeData.EnvironmentPlaceholders.Add(newPlaceholder);
            item.PlaceholderRef = newPlaceholder;
            item.Source = EnvItemSource.Placeholder;
            item.DisplayLabel = $"[Placeholder] {item.Id}";

            if (_activeData.EmbeddedMap != null && _activeData.EmbeddedMap.PlacedProps != null)
            {
                _activeData.EmbeddedMap.PlacedProps.RemoveAll(p => p != null && string.Equals(p.PrefabName, item.Id, StringComparison.OrdinalIgnoreCase));
            }

            SaveEnvironmentToJson();
            _statusMessage = $"✅ '{item.Id}' converti en Placeholder JSON et retiré des PlacedProps. Sauvegardé dans {_activeData.SceneId}.json";
        }

        private void DeleteCurrentItem(UnifiedEnvItem item)
        {
            if (item == null || _activeData == null) return;

            string targetId = item.Id;

            // 1. Destruction physique de l'objet en scène et des conteneurs d'environnement
            if (item.SceneObject == null)
            {
                item.SceneObject = FindSceneObjectUniversal(targetId);
            }

            if (item.SceneObject != null)
            {
                DestroyImmediate(item.SceneObject);
            }

            var envContainer = GameObject.Find($"[Environment] {targetId}") 
                            ?? GameObject.Find($"[Environment] {_activeData.EnvironmentId}");
            if (envContainer != null)
            {
                DestroyImmediate(envContainer);
            }

            // 2. Purge de la clé racine EnvironmentId si l'objet est le décor global
            if (item.Source == EnvItemSource.EnvironmentRoot ||
                string.Equals(_activeData.EnvironmentId, targetId, StringComparison.OrdinalIgnoreCase))
            {
                _activeData.EnvironmentId = "";
            }

            // 3. Retrait de la collection des placeholders JSON
            if (_activeData.EnvironmentPlaceholders != null)
            {
                _activeData.EnvironmentPlaceholders.RemoveAll(p => p != null && (
                    string.Equals(p.Id, targetId, StringComparison.OrdinalIgnoreCase) ||
                    p == item.PlaceholderRef
                ));
            }

            // 4. Retrait des PlacedProps de carte si applicable
            if (_activeData.EmbeddedMap != null && _activeData.EmbeddedMap.PlacedProps != null)
            {
                _activeData.EmbeddedMap.PlacedProps.RemoveAll(p => p != null && (
                    string.Equals(p.PrefabName, targetId, StringComparison.OrdinalIgnoreCase) ||
                    targetId.IndexOf(p.PrefabName, StringComparison.OrdinalIgnoreCase) >= 0
                ));
            }

            // 5. Écriture immédiate du fichier JSON sur disque
            SaveEnvironmentToJson();

            _statusMessage = $"🗑️ '{targetId}' supprimé définitivement du JSON et de la scène.";
            CombatHUD.Instance?.AddAdvancedLog($"🗑️ <b>[ENVIRONNEMENT]</b> '{targetId}' supprimé du JSON et détruit de la scène.", LogCategory.MovementAndTurns, "[SUPPRESSION]", Color.red);

            // 6. Réactualisation immédiate de l'arborescence
            RebuildUnifiedItemList();
            _selectedItemIndex = Mathf.Clamp(_selectedItemIndex, 0, Mathf.Max(0, _unifiedItems.Count - 1));
        }

        private void ApplyScaleToItem(UnifiedEnvItem item, float scaleVal)
        {
            ApplyTransformChangesToItem(item, item.Position, item.EulerAngles, Vector3.one * scaleVal);
        }

        private void DrawSpatialImmersionCheck(GameObject go)
        {
            Vector3 stationPos = Vector3.zero;
            float distToStation = Vector3.Distance(stationPos, go.transform.position);

            float estimatedRadius = 8f;
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                Bounds b = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
                estimatedRadius = Mathf.Max(b.extents.x, b.extents.y, b.extents.z);
            }

            GUILayout.Label($"<color=grey>Distance Station ➔ Objet : <b>{distToStation:0.#} m</b> | Rayon mesuré : <b>{estimatedRadius:0.#} m</b></color>");

            if (distToStation < estimatedRadius && estimatedRadius > 50f)
            {
                GUI.color = Color.red;
                GUILayout.Label($"<b>⚠️ ALERTE : La station est À L'INTÉRIEUR de l'objet ({distToStation:0}m < rayon {estimatedRadius:0}m) !</b>");
                GUI.color = Color.white;
            }
        }

        private float DrawWideAxisControl(string label, float val, float min, float max, float step)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(140));

            if (GUILayout.Button("-", GUILayout.Width(24))) val -= step;
            string txt = GUILayout.TextField(val.ToString("0.##"), GUILayout.Width(75));
            float.TryParse(txt, out val);
            if (GUILayout.Button("+", GUILayout.Width(24))) val += step;

            val = GUILayout.HorizontalSlider(val, min, max);
            GUILayout.EndHorizontal();
            return val;
        }
    }
}

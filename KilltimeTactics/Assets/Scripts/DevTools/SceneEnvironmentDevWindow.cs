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
    public class SceneEnvironmentDevWindow : FloatingWindow<SceneEnvironmentDevWindow>
    {
        protected override int WindowId => 895;
        protected override string Title => "Éditeur d'Environnement & Lumières (Shift+F3)";
        protected override Vector2 MinSize => new Vector2(540f, 600f);
        protected override Rect DefaultRect => new Rect(60f, 60f, 600f, 760f);
        protected override KeyCode[] ToggleKeys => new[] { KeyCode.F3 };
        protected override bool RequireShift => true;

        private enum EnvItemSource
        {
            Placeholder,
            PlacedProp,
            EnvironmentRoot,
            SceneInstance
        }

        private class UnifiedEnvItem
        {
            public string Id;
            public string DisplayLabel;
            public EnvItemSource Source;
            public ScenePlaceholderData PlaceholderRef;
            public GameObject SceneObject;
            public Vector3 Position;
            public Vector3 EulerAngles;
            public Vector3 Scale;
        }

        private readonly string[] _tabs = { "🪐 Décor & Objets", "💡 Lumières & Soleil", "💾 Enregistrement JSON" };
        private int _selectedTab = 0;
        private Vector2 _scroll;
        private Vector2 _objectListScroll;
        private bool _showObjectList = false;

        private StorySceneData _activeData;
        private readonly List<UnifiedEnvItem> _unifiedItems = new();
        private int _selectedItemIndex = 0;

        private Light _directionalLight;
        private GameObject _inspectedObject;
        private float _cameraOrbitDistance = 45f;
        private string _statusMessage = "Prêt. Sélectionnez un objet ou ajustez l'éclairage en temps réel.";

        protected override void OnOpened()
        {
            RefreshActiveSceneReference();
            FindSceneLighting();
            RebuildUnifiedItemList();
        }

        private void RefreshActiveSceneReference()
        {
            var controller = FindAnyObjectByType<JsonStorySceneController>();
            if (controller != null && controller.SceneData != null)
            {
                _activeData = controller.SceneData;
                return;
            }

            var editor = ScenarioEditorDevWindow.Instance ?? FindAnyObjectByType<ScenarioEditorDevWindow>();
            if (editor != null && editor.ActiveSceneData != null)
            {
                _activeData = editor.ActiveSceneData;
            }
        }

        private void FindSceneLighting()
        {
            var lights = FindObjectsByType<Light>();
            Light candidate = null;

            for (int i = 0; i < lights.Length; i++)
            {
                var l = lights[i];
                if (l == null || l.type != LightType.Directional) continue;

                string lName = l.name.ToLowerInvariant();
                bool isInterior = l.transform.parent != null && l.transform.parent.name.IndexOf("Interior", StringComparison.OrdinalIgnoreCase) >= 0;

                if (!isInterior && (lName.Contains("sun") || lName.Contains("soleil") || lName.Contains("celestial") || lName.Contains("space")))
                {
                    _directionalLight = l;
                    return;
                }

                if (candidate == null && !isInterior)
                {
                    candidate = l;
                }
            }

            _directionalLight = candidate ?? (lights.Length > 0 ? Array.Find(lights, x => x != null && x.type == LightType.Directional) : null);
        }

        protected override void OnUpdate()
        {
            base.OnUpdate();

            if (_isOpen && _inspectedObject != null && UnityEngine.Camera.main != null)
            {
                UpdateLockedInspectionCamera();
            }
        }

        private void RebuildUnifiedItemList()
        {
            _unifiedItems.Clear();

            if (_activeData == null) return;

            var registeredIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (_activeData.EnvironmentPlaceholders != null)
            {
                for (int i = 0; i < _activeData.EnvironmentPlaceholders.Count; i++)
                {
                    var p = _activeData.EnvironmentPlaceholders[i];
                    if (p == null) continue;

                    string id = string.IsNullOrEmpty(p.Id) ? $"Objet #{i + 1}" : p.Id;
                    registeredIds.Add(id);

                    GameObject sceneGo = FindSceneObjectUniversal(id);

                    bool childOfEnv = p.ParentToEnvironment;
                    _unifiedItems.Add(new UnifiedEnvItem
                    {
                        Id = id,
                        DisplayLabel = childOfEnv ? $"[Placeholder] {id} (enfant décor · local)" : $"[Placeholder] {id}",
                        Source = EnvItemSource.Placeholder,
                        PlaceholderRef = p,
                        SceneObject = sceneGo,
                        Position = p.Position,
                        EulerAngles = p.EulerAngles,
                        Scale = p.Scale
                    });
                }
            }

            if (_activeData.EmbeddedMap != null && _activeData.EmbeddedMap.PlacedProps != null)
            {
                for (int i = 0; i < _activeData.EmbeddedMap.PlacedProps.Count; i++)
                {
                    var prop = _activeData.EmbeddedMap.PlacedProps[i];
                    if (prop == null || string.IsNullOrWhiteSpace(prop.PrefabName)) continue;

                    string name = prop.PrefabName.Trim();
                    if (registeredIds.Contains(name)) continue;

                    registeredIds.Add(name);

                    GameObject propGo = FindSceneObjectUniversal(name);

                    Vector3 pos = propGo != null ? propGo.transform.position : HexToWorld(prop.Q, prop.R, prop.HeightOffset);
                    Vector3 rot = propGo != null ? propGo.transform.eulerAngles : new Vector3(0f, prop.RotationY, 0f);
                    Vector3 sca = propGo != null ? propGo.transform.localScale : Vector3.one * prop.Scale;

                    _unifiedItems.Add(new UnifiedEnvItem
                    {
                        Id = name,
                        DisplayLabel = $"[Prop Carte] {name} ({prop.Q},{prop.R})",
                        Source = EnvItemSource.PlacedProp,
                        PlaceholderRef = null,
                        SceneObject = propGo,
                        Position = pos,
                        EulerAngles = rot,
                        Scale = sca
                    });
                }
            }

            if (!string.IsNullOrWhiteSpace(_activeData.EnvironmentId))
            {
                string envId = _activeData.EnvironmentId.Trim();
                if (!registeredIds.Contains(envId) && !registeredIds.Contains($"[Environment] {envId}"))
                {
                    registeredIds.Add(envId);

                    GameObject envGo = FindSceneObjectUniversal(envId);

                    Vector3 pos = envGo != null ? envGo.transform.position : Vector3.zero;
                    Vector3 rot = envGo != null ? envGo.transform.eulerAngles : Vector3.zero;
                    Vector3 sca = envGo != null ? envGo.transform.localScale : Vector3.one;

                    _unifiedItems.Add(new UnifiedEnvItem
                    {
                        Id = envId,
                        DisplayLabel = $"[Décor Global] {envId}",
                        Source = EnvItemSource.EnvironmentRoot,
                        PlaceholderRef = null,
                        SceneObject = envGo,
                        Position = pos,
                        EulerAngles = rot,
                        Scale = sca
                    });
                }
            }

            var envContainer = GameObject.Find($"[Environment] {_activeData.EnvironmentId}");
            if (envContainer != null)
            {
                for (int c = 0; c < envContainer.transform.childCount; c++)
                {
                    var child = envContainer.transform.GetChild(c);
                    if (child == null) continue;

                    string cName = child.name;
                    if (registeredIds.Contains(cName)) continue;

                    registeredIds.Add(cName);

                    _unifiedItems.Add(new UnifiedEnvItem
                    {
                        Id = cName,
                        DisplayLabel = $"[Instance Scène] {_activeData.EnvironmentId}/{cName}",
                        Source = EnvItemSource.SceneInstance,
                        PlaceholderRef = null,
                        SceneObject = child.gameObject,
                        Position = child.position,
                        EulerAngles = child.eulerAngles,
                        Scale = child.localScale
                    });
                }
            }

            // --- Arborescence : les enfants du décor (instances + placeholders
            // ParentToEnvironment) sont regroupés juste sous la racine d'environnement.
            int envRootIdx = _unifiedItems.FindIndex(u => u != null && u.Source == EnvItemSource.EnvironmentRoot);
            if (envRootIdx >= 0)
            {
                var children = new List<UnifiedEnvItem>();
                for (int i = _unifiedItems.Count - 1; i >= 0; i--)
                {
                    if (i == envRootIdx) continue;
                    var u = _unifiedItems[i];
                    if (u == null) continue;
                    bool isChild = u.Source == EnvItemSource.SceneInstance
                        || (u.Source == EnvItemSource.Placeholder && u.PlaceholderRef != null && u.PlaceholderRef.ParentToEnvironment);
                    if (isChild)
                    {
                        children.Add(u);
                        _unifiedItems.RemoveAt(i);
                        if (i < envRootIdx) envRootIdx--;
                    }
                }
                children.Reverse();
                for (int i = 0; i < children.Count; i++)
                {
                    var c = children[i];
                    if (!c.DisplayLabel.StartsWith("  └─"))
                        c.DisplayLabel = $"  └─ {c.DisplayLabel}";
                    _unifiedItems.Insert(envRootIdx + 1 + i, c);
                }
            }

            _selectedItemIndex = Mathf.Clamp(_selectedItemIndex, 0, Mathf.Max(0, _unifiedItems.Count - 1));
        }

        private Vector3 HexToWorld(int q, int r, float heightOffset)
        {
            float x = Mathf.Sqrt(3.0f) * q + Mathf.Sqrt(3.0f) / 2.0f * r;
            float z = 1.5f * r;
            return new Vector3(x, heightOffset, z);
        }

        private (int q, int r) WorldToHex(Vector3 worldPos)
        {
            float q = (Mathf.Sqrt(3.0f) / 3.0f * worldPos.x - 1.0f / 3.0f * worldPos.z);
            float r = (2.0f / 3.0f * worldPos.z);
            float s = -q - r;

            int rq = Mathf.RoundToInt(q);
            int rr = Mathf.RoundToInt(r);
            int rs = Mathf.RoundToInt(s);

            float qDiff = Mathf.Abs(rq - q);
            float rDiff = Mathf.Abs(rr - r);
            float sDiff = Mathf.Abs(rs - s);

            if (qDiff > rDiff && qDiff > sDiff) rq = -rr - rs;
            else if (rDiff > sDiff) rr = -rq - rs;

            return (rq, rr);
        }

        protected override void DrawContent()
        {
            if (_activeData == null)
            {
                RefreshActiveSceneReference();
            }

            GUILayout.Space(4);
            _selectedTab = GUILayout.Toolbar(_selectedTab, _tabs, GUILayout.Height(28));
            GUILayout.Space(6);

            if (!string.IsNullOrEmpty(_statusMessage))
            {
                GUI.color = Color.cyan;
                GUILayout.Label($"ℹ️ {_statusMessage}", GUI.skin.box);
                GUI.color = Color.white;
            }

            _scroll = GUILayout.BeginScrollView(_scroll);

            if (_unifiedItems.Count == 0)
            {
                RebuildUnifiedItemList();
            }

            if (_activeData == null || _unifiedItems.Count == 0)
            {
                GUILayout.Label("<color=yellow>Aucun objet d'environnement ou décor détecté dans la scène active.</color>");
                if (GUILayout.Button("🔄 Forcer la Recherche de Décor"))
                {
                    RefreshActiveSceneReference();
                    RebuildUnifiedItemList();
                }
                GUILayout.EndScrollView();
                return;
            }

            switch (_selectedTab)
            {
                case 0: DrawObjectsTab(); break;
                case 1: DrawLightingTab(); break;
                case 2: DrawSaveTab(); break;
            }

            GUILayout.EndScrollView();
        }

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

        private void DrawLightingTab()
        {
            FindSceneLighting();
            var lighting = _activeData.Lighting;

            string lightName = _directionalLight != null ? _directionalLight.name : "<color=red>Aucune</color>";
            GUILayout.Label($"<b>1. Directional Light // Source Principale (<color=#00E5FF>{lightName}</color>) :</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            lighting.OverrideLighting = GUILayout.Toggle(lighting.OverrideLighting, "<b>Activer le pilotage temps réel de la lumière</b>");

            if (_directionalLight == null)
            {
                GUILayout.Label("<color=red>⚠️ Aucune Directional Light trouvée dans la scène.</color>");
            }
            else
            {
                _directionalLight.cullingMask = ~0;

                GUILayout.Label("<b>Position Spatiale de la Source (X, Y, Z) :</b>");
                Vector3 sunPos = lighting.SunPosition;
                sunPos.x = DrawWideAxisControl("Pos X", sunPos.x, -2500f, 2500f, 10f);
                sunPos.y = DrawWideAxisControl("Pos Y", sunPos.y, -1000f, 2500f, 10f);
                sunPos.z = DrawWideAxisControl("Pos Z", sunPos.z, -2500f, 2500f, 10f);
                if (sunPos != lighting.SunPosition)
                {
                    lighting.SunPosition = sunPos;
                    if (lighting.OverrideLighting) _directionalLight.transform.position = sunPos;
                }

                GUILayout.Space(6);

                GUILayout.Label("<b>Orientation Complète du Faisceau (Angles Euler) :</b>");
                Vector3 sunRot = lighting.SunEulerAngles;
                sunRot.x = DrawAngleControl("Pitch (X) Assiette", sunRot.x);
                sunRot.y = DrawAngleControl("Yaw (Y) Direction", sunRot.y);
                sunRot.z = DrawAngleControl("Roll (Z) Roulis", sunRot.z);

                if (sunRot != lighting.SunEulerAngles)
                {
                    lighting.SunEulerAngles = sunRot;
                    if (lighting.OverrideLighting) _directionalLight.transform.rotation = Quaternion.Euler(sunRot);
                }

                GUILayout.Space(4);

                GUILayout.BeginHorizontal();
                var curItem = (_unifiedItems.Count > 0 && _selectedItemIndex < _unifiedItems.Count) ? _unifiedItems[_selectedItemIndex] : null;

                if (curItem != null && GUILayout.Button($"🎯 Viser {curItem.Id}", GUILayout.Height(24)))
                {
                    AimSunAtTarget(curItem.Position);
                }
                if (GUILayout.Button("🏠 Viser Station (0,0,0)", GUILayout.Height(24)))
                {
                    AimSunAtTarget(Vector3.zero);
                }
                if (GUILayout.Button("🔄 Inverser (180°)", GUILayout.Height(24)))
                {
                    InvertSunDirection();
                }
                if (GUILayout.Button("📐 Terminatrice Verticale (Pitch 0°)", GUILayout.Height(24)))
                {
                    Vector3 r = lighting.SunEulerAngles;
                    r.x = 0f;
                    lighting.SunEulerAngles = r;
                    lighting.OverrideLighting = true;
                    if (_directionalLight != null) _directionalLight.transform.rotation = Quaternion.Euler(r);
                    _statusMessage = "Faisceau aligné à Pitch 0° (terminatrice verticale méridienne).";
                }
                GUILayout.EndHorizontal();

                GUILayout.Space(6);

                GUILayout.BeginHorizontal();
                GUILayout.Label($"Intensité : <b>{lighting.SunIntensity:0.##} Lux</b>", GUILayout.Width(150));
                lighting.SunIntensity = GUILayout.HorizontalSlider(lighting.SunIntensity, 0.0f, 15.0f);
                string intStr = GUILayout.TextField(lighting.SunIntensity.ToString("0.##"), GUILayout.Width(50));
                float.TryParse(intStr, out lighting.SunIntensity);
                if (lighting.OverrideLighting) _directionalLight.intensity = lighting.SunIntensity;
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("Ombres :", GUILayout.Width(75));
                lighting.Shadows = (LightShadows)GUILayout.Toolbar((int)lighting.Shadows, new[] { "Aucune", "Nettes", "Douces" }, GUILayout.Height(20));
                if (lighting.OverrideLighting) _directionalLight.shadows = lighting.Shadows;
                GUILayout.EndHorizontal();

                if (lighting.Shadows != LightShadows.None)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"Force Ombres : <b>{lighting.ShadowStrength:0.##}</b>", GUILayout.Width(150));
                    lighting.ShadowStrength = GUILayout.HorizontalSlider(lighting.ShadowStrength, 0.0f, 1.0f);
                    if (lighting.OverrideLighting) _directionalLight.shadowStrength = lighting.ShadowStrength;
                    GUILayout.EndHorizontal();
                }

                GUILayout.Space(4);

                GUILayout.Label("Couleur Spectrale (RGB) :");
                Color col = lighting.SunColor;
                col.r = DrawColorSlider("Rouge", col.r);
                col.g = DrawColorSlider("Vert", col.g);
                col.b = DrawColorSlider("Bleu", col.b);

                if (col != lighting.SunColor)
                {
                    lighting.SunColor = col;
                    if (lighting.OverrideLighting) _directionalLight.color = col;
                }

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Blanc Solaire")) ApplySunColor(new Color(1f, 0.98f, 0.92f));
                if (GUILayout.Button("Écarlate Nefris")) ApplySunColor(new Color(1.0f, 0.45f, 0.15f));
                if (GUILayout.Button("Bleu Stellaire")) ApplySunColor(new Color(0.75f, 0.90f, 1.0f));
                if (GUILayout.Button("Ambre Toxique")) ApplySunColor(new Color(1.0f, 0.78f, 0.25f));
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();

            GUILayout.Space(8);
            GUILayout.Label("<b>2. Ambiance Globale (Éclairage Indirect / Ombres) :</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            Color amb = lighting.AmbientColor;
            amb.r = DrawColorSlider("Ambiance R", amb.r);
            amb.g = DrawColorSlider("Ambiance V", amb.g);
            amb.b = DrawColorSlider("Ambiance B", amb.b);

            if (amb != lighting.AmbientColor)
            {
                lighting.AmbientColor = amb;
                if (lighting.OverrideLighting)
                {
                    RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                    RenderSettings.ambientLight = amb;
                }
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Ambiance Sombre (0.15)")) ApplyAmbientColor(new Color(0.15f, 0.15f, 0.18f));
            if (GUILayout.Button("Ambiance Modérée (0.35)")) ApplyAmbientColor(new Color(0.32f, 0.35f, 0.42f));
            if (GUILayout.Button("Ambiance Chaude Nefris")) ApplyAmbientColor(new Color(0.40f, 0.25f, 0.18f));
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }

        private float DrawAngleControl(string label, float angle)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(160));

            if (GUILayout.Button("-45°", GUILayout.Width(40))) angle -= 45f;
            if (GUILayout.Button("-15°", GUILayout.Width(35))) angle -= 15f;
            if (GUILayout.Button("0°", GUILayout.Width(28))) angle = 0f;
            if (GUILayout.Button("+15°", GUILayout.Width(35))) angle += 15f;
            if (GUILayout.Button("+45°", GUILayout.Width(40))) angle += 45f;

            string txt = GUILayout.TextField(angle.ToString("0.#"), GUILayout.Width(55));
            float.TryParse(txt, out angle);

            angle = GUILayout.HorizontalSlider(angle, -180f, 180f);
            GUILayout.EndHorizontal();
            return NormalizeAngle(angle);
        }

        private float DrawColorSlider(string label, float val)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(100));
            val = GUILayout.HorizontalSlider(val, 0f, 1f);
            string txt = GUILayout.TextField(val.ToString("0.##"), GUILayout.Width(45));
            float.TryParse(txt, out val);
            GUILayout.EndHorizontal();
            return Mathf.Clamp01(val);
        }

        private void ApplySunColor(Color c)
        {
            _activeData.Lighting.SunColor = c;
            if (_activeData.Lighting.OverrideLighting && _directionalLight != null)
            {
                _directionalLight.color = c;
            }
        }

        private void ApplyAmbientColor(Color c)
        {
            _activeData.Lighting.AmbientColor = c;
            if (_activeData.Lighting.OverrideLighting)
            {
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = c;
            }
        }

        private void AimSunAtTarget(Vector3 targetPos)
        {
            FindSceneLighting();
            if (_directionalLight == null) return;

            Vector3 originPos = _activeData.Lighting.SunPosition;
            Vector3 aimDir = (targetPos - originPos).normalized;
            if (aimDir == Vector3.zero) aimDir = Vector3.down;

            Quaternion targetRot = Quaternion.LookRotation(aimDir);
            _activeData.Lighting.SunEulerAngles = targetRot.eulerAngles;
            _activeData.Lighting.OverrideLighting = true;

            _directionalLight.transform.rotation = targetRot;
            _statusMessage = $"Faisceau orienté vers ({targetPos.x:0}, {targetPos.y:0}, {targetPos.z:0}) — Pitch: {targetRot.eulerAngles.x:0}°, Yaw: {targetRot.eulerAngles.y:0}°";
        }

        private void InvertSunDirection()
        {
            FindSceneLighting();
            if (_directionalLight == null) return;

            Vector3 curRot = _directionalLight.transform.rotation.eulerAngles;
            curRot.y = NormalizeAngle(curRot.y + 180f);
            curRot.x = NormalizeAngle(-curRot.x);

            _activeData.Lighting.SunEulerAngles = curRot;
            _activeData.Lighting.OverrideLighting = true;
            _directionalLight.transform.rotation = Quaternion.Euler(curRot);

            _statusMessage = "Faisceau solaire inversé à 180°.";
        }

        private static float NormalizeAngle(float a)
        {
            while (a > 180f) a -= 360f;
            while (a < -180f) a += 360f;
            return a;
        }

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
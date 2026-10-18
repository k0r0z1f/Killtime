// PARTIAL-SPLIT: SceneEnvironmentDevWindow.cs - facade (champs, UnifiedEnvItem, dispatcher onglets, rebuild, hex). Deplacement verbatim, logique inchangee.
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
    }
}

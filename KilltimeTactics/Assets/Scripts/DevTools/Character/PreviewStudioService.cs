using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Animations;
using Killtime.Core.Character;
using Killtime.Tactics.Units;

namespace Killtime.DevTools.Character
{
    /// <summary>
    /// Studio d'aperçu 3D temps réel du créateur (RenderTexture + caméra isolée
    /// à 5000/-5000/5000 + PlayableGraph). Extrait à l'identique de
    /// CharacterDevWindow — la window ne garde que l'appel Tick/Render/Draw.
    /// Cycle de vie : Ensure() lazy, Cleanup() sur OnClosed/OnDisable/OnDestroy.
    /// </summary>
    public sealed class PreviewStudioService
    {
        public readonly string[] AnimationStates = {
            "Standing Idle",
            "Standing Idle To Fight Idle",
            "Fight Idle",
            "Fight Idle To Standing Idle",
            "Action Idle To Fight Idle",
            "Action Idle To Standing Idle",
            "Walking",
            "Roundkick",
            "Body Block",
            "Falling Back Death",
            "Rifle Idle",
            "Rifle Walk To Stop",
            "Firing Rifle",
            "Rifle Put Away"
        };

        public RenderTexture PreviewRT { get; private set; }
        public int SelectedAnimIndex { get; private set; }
        public float ModelYaw { get; set; } = 180f;
        public bool ShowAnimDropdown { get; set; }

        private Camera _previewCam;
        private GameObject _studioRoot;
        private GameObject _currentInstance;
        private Animator _animator;
        private PlayableGraph _graph;
        private string _lastModelName = "__UNINITIALIZED__";
        private string _lastGenderKey = "__UNINITIALIZED__";
        private bool _isDragging;
        private Vector2 _lastMousePos;

        public bool HasInstance => _currentInstance != null;

        public void SetSelectedAnimIndex(int index)
        {
            if (AnimationStates == null || AnimationStates.Length == 0) return;
            SelectedAnimIndex = Mathf.Clamp(index, 0, AnimationStates.Length - 1);
        }

        public void ResetForNewSheet()
        {
            SelectedAnimIndex = 0;
            ShowAnimDropdown = false;
            _lastModelName = "__UNINITIALIZED__";
            _lastGenderKey = "__UNINITIALIZED__";
        }

        public void InvalidateCache()
        {
            _lastModelName = "__UNINITIALIZED__";
            _lastGenderKey = "__UNINITIALIZED__";
        }

        public void Ensure()
        {
            if (PreviewRT == null)
            {
                PreviewRT = new RenderTexture(256, 256, 16, RenderTextureFormat.ARGB32)
                {
                    name = "CharacterModel_PreviewRT"
                };
                PreviewRT.Create();
            }

            if (_studioRoot == null)
            {
                _studioRoot = new GameObject("[Studio] CharacterModelPreview");
                _studioRoot.transform.position = new Vector3(5000f, -5000f, 5000f);

                var camGo = new GameObject("PreviewCamera");
                camGo.transform.SetParent(_studioRoot.transform, false);
                _previewCam = camGo.AddComponent<Camera>();
                _previewCam.clearFlags = CameraClearFlags.SolidColor;
                _previewCam.backgroundColor = new Color(0.02f, 0.04f, 0.07f, 1.0f);
                _previewCam.fieldOfView = 34f;
                _previewCam.nearClipPlane = 0.1f;
                _previewCam.farClipPlane = 50f;
                _previewCam.targetTexture = PreviewRT;
                _previewCam.enabled = false;

                var lightGo = new GameObject("PreviewLight");
                lightGo.transform.SetParent(_studioRoot.transform, false);
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.color = new Color(0.95f, 0.98f, 1.0f);
                light.intensity = 1.3f;
                lightGo.transform.rotation = Quaternion.Euler(30f, -35f, 0f);

                var rimLightGo = new GameObject("PreviewRimLight");
                rimLightGo.transform.SetParent(_studioRoot.transform, false);
                var rimLight = rimLightGo.AddComponent<Light>();
                rimLight.type = LightType.Directional;
                rimLight.color = new Color(0.0f, 0.85f, 1.0f);
                rimLight.intensity = 0.65f;
                rimLightGo.transform.rotation = Quaternion.Euler(-25f, 145f, 0f);
            }
        }

        /// <summary>Appelé depuis Update() quand l'onglet Création/Spawner est visible.</summary>
        public void Tick(CharacterSheet sheet)
        {
            if (sheet == null) return;
            UpdatePreviewModel(sheet.ModelPrefabName, AnimationClipResolver.ResolveGenderKey(sheet));
            Render();
        }

        public void UpdatePreviewModel(string modelName, string genderKey)
        {
            if (modelName == _lastModelName && genderKey == _lastGenderKey && _currentInstance != null) return;

            bool modelChanged = modelName != _lastModelName;
            Ensure();
            _lastModelName = modelName;
            _lastGenderKey = genderKey;

            // Même modèle mais genre différent -> pas besoin de ré-instancier,
            // on rejoue juste le clip genré.
            if (!modelChanged && _currentInstance != null)
            {
                Play(SelectedAnimIndex, out _);
                return;
            }

            if (_currentInstance != null)
            {
                UnityEngine.Object.Destroy(_currentInstance);
                _currentInstance = null;
            }

            GameObject prefab = null;

            if (!string.IsNullOrEmpty(modelName) && modelName != "(Procédural)")
            {
                string cleanName = modelName.StartsWith("Characters/") ? modelName.Substring("Characters/".Length) : modelName;
                prefab = Resources.Load<GameObject>($"Characters/{cleanName}") ?? Resources.Load<GameObject>(cleanName);

                if (prefab != null)
                {
                    _currentInstance = UnityEngine.Object.Instantiate(prefab, _studioRoot.transform);
                    _currentInstance.name = "Preview_" + cleanName;

                    var colliders = _currentInstance.GetComponentsInChildren<Collider>();
                    for (int i = 0; i < colliders.Length; i++) colliders[i].enabled = false;
                }
            }

            if (_currentInstance == null)
            {
                _currentInstance = BuildProceduralModel();
            }

            _currentInstance.transform.localPosition = Vector3.zero;
            _currentInstance.transform.localRotation = Quaternion.Euler(0f, ModelYaw, 0f);

            // Même normalisation qu'en jeu : l'aperçu affiche la taille réelle de l'unité.
            CharacterModelScaleNormalizer.NormalizeToUnitHeight(_currentInstance, 1f);

            SetupAnimator(_currentInstance, prefab);
            Play(SelectedAnimIndex, out _);

            FrameCamera();
        }

        public void UpdatePreviewModel(CharacterSheet sheet)
        {
            if (sheet == null) return;
            UpdatePreviewModel(sheet.ModelPrefabName, AnimationClipResolver.ResolveGenderKey(sheet));
        }

        public bool Play(int index, out string statusIfMissing)
        {
            statusIfMissing = null;
            if (index < 0 || index >= AnimationStates.Length) return false;
            SelectedAnimIndex = index;
            string stateName = AnimationStates[index];

            if (_animator == null) return false;

            if (_graph.IsValid())
            {
                _graph.Destroy();
            }

            AnimationClip clip = AnimationClipResolver.LoadClip(stateName, _lastGenderKey);
            if (clip != null)
            {
                AnimationPlayableUtilities.PlayClip(_animator, clip, out _graph);
                _graph.SetTimeUpdateMode(DirectorUpdateMode.UnscaledGameTime);
                _graph.Play();
                return true;
            }

            statusIfMissing = $"⚠️ Clip '{stateName}' introuvable dans Resources/Animations/{stateName}";
            return false;
        }

        public void PlayPrevious(out string statusIfMissing)
        {
            int next = (SelectedAnimIndex - 1 + AnimationStates.Length) % AnimationStates.Length;
            Play(next, out statusIfMissing);
        }

        public void PlayNext(out string statusIfMissing)
        {
            int next = (SelectedAnimIndex + 1) % AnimationStates.Length;
            Play(next, out statusIfMissing);
        }

        public string CurrentAnimName => AnimationStates != null && AnimationStates.Length > 0
            ? AnimationStates[Mathf.Clamp(SelectedAnimIndex, 0, AnimationStates.Length - 1)]
            : string.Empty;

        private void SetupAnimator(GameObject instance, GameObject sourcePrefab = null)
        {
            _animator = instance.GetComponentInChildren<Animator>();
            if (_animator == null)
            {
                _animator = instance.AddComponent<Animator>();
            }

            _animator.applyRootMotion = false;
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            _animator.updateMode = AnimatorUpdateMode.UnscaledTime;

            if (_animator.avatar == null && sourcePrefab != null)
            {
                var srcAnim = sourcePrefab.GetComponentInChildren<Animator>();
                if (srcAnim != null && srcAnim.avatar != null)
                {
                    _animator.avatar = srcAnim.avatar;
                }
            }
        }

        private GameObject BuildProceduralModel()
        {
            var root = new GameObject("Preview_Procedural");
            root.transform.SetParent(_studioRoot.transform, false);

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "BodyCapsule";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0, 1.0f, 0);
            body.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);
            var bCol = body.GetComponent<Collider>();
            if (bCol != null) UnityEngine.Object.Destroy(bCol);

            var visor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visor.name = "Visor";
            visor.transform.SetParent(root.transform, false);
            visor.transform.localPosition = new Vector3(0, 1.45f, 0.28f);
            visor.transform.localScale = new Vector3(0.45f, 0.15f, 0.25f);
            var vCol = visor.GetComponent<Collider>();
            if (vCol != null) UnityEngine.Object.Destroy(vCol);

            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "BaseRing";
            ring.transform.SetParent(root.transform, false);
            ring.transform.localPosition = new Vector3(0, 0.02f, 0);
            ring.transform.localScale = new Vector3(0.95f, 0.02f, 0.95f);
            var rCol = ring.GetComponent<Collider>();
            if (rCol != null) UnityEngine.Object.Destroy(rCol);

            return root;
        }

        private void FrameCamera()
        {
            if (_previewCam == null || _currentInstance == null) return;

            var renderers = _currentInstance.GetComponentsInChildren<Renderer>();
            Bounds bounds;

            if (renderers.Length > 0)
            {
                bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }
            }
            else
            {
                bounds = new Bounds(_currentInstance.transform.position + Vector3.up * 1f, Vector3.one * 1.8f);
            }

            float maxDimension = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            maxDimension = Mathf.Max(maxDimension, 1.2f);

            float distance = (maxDimension * 0.5f) / Mathf.Tan(_previewCam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            distance *= 1.45f;

            Vector3 targetFocus = bounds.center;
            _previewCam.transform.position = targetFocus + new Vector3(0f, maxDimension * 0.15f, distance);
            _previewCam.transform.LookAt(targetFocus);
        }

        public void Render()
        {
            if (_previewCam == null || _currentInstance == null) return;

            if (!_isDragging)
            {
                ModelYaw = (ModelYaw + Time.unscaledDeltaTime * 28f) % 360f;
            }

            _currentInstance.transform.localRotation = Quaternion.Euler(0f, ModelYaw, 0f);
            _previewCam.Render();
        }

        public void DrawViewport(float width, float height)
        {
            Rect rect = GUILayoutUtility.GetRect(width, height);
            Color prevBg = GUI.backgroundColor;

            GUI.backgroundColor = new Color(0.01f, 0.03f, 0.06f, 0.95f);
            GUI.Box(rect, GUIContent.none);
            GUI.backgroundColor = prevBg;

            if (PreviewRT != null)
            {
                GUI.DrawTexture(rect, PreviewRT, ScaleMode.ScaleToFit);
            }

            Event e = Event.current;
            if (rect.Contains(e.mousePosition))
            {
                if (e.type == EventType.MouseDown && (e.button == 0 || e.button == 1))
                {
                    _isDragging = true;
                    _lastMousePos = e.mousePosition;
                    e.Use();
                }
            }

            if (_isDragging)
            {
                if (e.type == EventType.MouseDrag)
                {
                    float deltaX = e.mousePosition.x - _lastMousePos.x;
                    ModelYaw -= deltaX * 1.4f;
                    _lastMousePos = e.mousePosition;
                    e.Use();
                }
                else if (e.type == EventType.MouseUp)
                {
                    _isDragging = false;
                    e.Use();
                }
            }

            Rect labelRect = new Rect(rect.x, rect.y + rect.height - 18, rect.width, 16);
            var footerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 9,
                alignment = TextAnchor.MiddleCenter
            };
            footerStyle.normal.textColor = new Color(0.0f, 0.9f, 1.0f, 0.7f);
            GUI.Label(labelRect, "↔ Glisser pour pivoter le modèle", footerStyle);
        }

        public void Cleanup()
        {
            if (_graph.IsValid())
            {
                _graph.Destroy();
            }

            _animator = null;

            if (_currentInstance != null)
            {
                UnityEngine.Object.Destroy(_currentInstance);
                _currentInstance = null;
            }

            if (_studioRoot != null)
            {
                UnityEngine.Object.Destroy(_studioRoot);
                _studioRoot = null;
            }

            if (PreviewRT != null)
            {
                PreviewRT.Release();
                UnityEngine.Object.Destroy(PreviewRT);
                PreviewRT = null;
            }

            _lastModelName = "__UNINITIALIZED__";
            _lastGenderKey = "__UNINITIALIZED__";
            _previewCam = null;
            _isDragging = false;
        }
    }
}

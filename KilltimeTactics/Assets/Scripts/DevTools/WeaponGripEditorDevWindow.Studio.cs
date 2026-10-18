using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Animations;
using Killtime.Core.Inventory;
using Killtime.Tactics.Units;

namespace Killtime.UI
{
    public partial class WeaponGripEditorDevWindow : FloatingWindow<WeaponGripEditorDevWindow>
    {
        // ================= GESTION DU STUDIO 3D =================
        private void EnsureStudio()
        {
            if (_studioRT == null)
            {
                _studioRT = new RenderTexture(512, 512, 16, RenderTextureFormat.ARGB32)
                {
                    name = "WeaponGripStudio_RT"
                };
                _studioRT.Create();
            }

            if (_studioRoot == null)
            {
                _studioRoot = new GameObject("[Studio] WeaponGripEditor");
                _studioRoot.transform.position = new Vector3(7000f, -5000f, 7000f);

                var camGo = new GameObject("StudioCamera");
                camGo.transform.SetParent(_studioRoot.transform, false);
                _studioCam = camGo.AddComponent<Camera>();
                _studioCam.clearFlags = CameraClearFlags.SolidColor;
                _studioCam.backgroundColor = new Color(0.012f, 0.025f, 0.045f, 1f);
                _studioCam.fieldOfView = 34f;
                _studioCam.nearClipPlane = 0.05f;
                _studioCam.farClipPlane = 50f;
                _studioCam.targetTexture = _studioRT;
                _studioCam.enabled = false;

                var keyLight = new GameObject("StudioKeyLight").AddComponent<Light>();
                keyLight.transform.SetParent(_studioRoot.transform, false);
                keyLight.type = LightType.Directional;
                keyLight.color = new Color(0.95f, 0.98f, 1.0f);
                keyLight.intensity = 1.3f;
                keyLight.transform.rotation = Quaternion.Euler(35f, -30f, 0f);

                var rimLight = new GameObject("StudioRimLight").AddComponent<Light>();
                rimLight.transform.SetParent(_studioRoot.transform, false);
                rimLight.type = LightType.Directional;
                rimLight.color = new Color(0.0f, 0.85f, 1.0f);
                rimLight.intensity = 0.7f;
                rimLight.transform.rotation = Quaternion.Euler(-25f, 140f, 0f);
            }

            if (_currentMannequinInstance == null)
            {
                SpawnMannequin(_mannequinModelName);
            }
        }

        private void SpawnMannequin(string modelName)
        {
            if (_currentWeaponInstance != null)
            {
                DestroyImmediate(_currentWeaponInstance);
                _currentWeaponInstance = null;
            }

            if (_currentMannequinInstance != null)
            {
                DestroyImmediate(_currentMannequinInstance);
                _currentMannequinInstance = null;
            }

            _mannequinModelName = modelName;
            GameObject prefab = null;

            if (!string.IsNullOrEmpty(modelName))
            {
                prefab = Resources.Load<GameObject>($"Characters/{modelName}")
                      ?? Resources.Load<GameObject>(modelName);
            }

            if (prefab != null)
            {
                _currentMannequinInstance = Instantiate(prefab, _studioRoot.transform);
                _currentMannequinInstance.name = "Mannequin_" + modelName;

                var colliders = _currentMannequinInstance.GetComponentsInChildren<Collider>();
                for (int i = 0; i < colliders.Length; i++) colliders[i].enabled = false;
            }
            else
            {
                _currentMannequinInstance = BuildProceduralMannequin();
            }

            _currentMannequinInstance.transform.localPosition = Vector3.zero;
            _currentMannequinInstance.transform.localRotation = Quaternion.identity;

            CharacterModelScaleNormalizer.NormalizeToUnitHeight(_currentMannequinInstance, 1.0f);

            SetupMannequinAnimator(_currentMannequinInstance, prefab);
            SetStudioAnimation(_selectedAnimIndex);
            SpawnWeaponInStudio();
        }

        private GameObject BuildProceduralMannequin()
        {
            var root = new GameObject("Mannequin_Procedural");
            root.transform.SetParent(_studioRoot.transform, false);

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "BodyCapsule";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0, 0.9f, 0);
            body.transform.localScale = new Vector3(0.65f, 0.9f, 0.65f);
            DestroyImmediate(body.GetComponent<Collider>());

            var rightArm = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            rightArm.name = "mixamorig:RightHand";
            rightArm.transform.SetParent(root.transform, false);
            rightArm.transform.localPosition = new Vector3(0.4f, 1.0f, 0.2f);
            rightArm.transform.localRotation = Quaternion.Euler(45f, 0f, 0f);
            rightArm.transform.localScale = new Vector3(0.12f, 0.2f, 0.12f);
            DestroyImmediate(rightArm.GetComponent<Collider>());

            return root;
        }

        private void SetupMannequinAnimator(GameObject instance, GameObject sourcePrefab)
        {
            _mannequinAnimator = instance.GetComponentInChildren<Animator>();
            if (_mannequinAnimator == null)
            {
                _mannequinAnimator = instance.AddComponent<Animator>();
            }

            _mannequinAnimator.applyRootMotion = false;
            _mannequinAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            _mannequinAnimator.updateMode = AnimatorUpdateMode.UnscaledTime;

            if (_mannequinAnimator.avatar == null && sourcePrefab != null)
            {
                var srcAnim = sourcePrefab.GetComponentInChildren<Animator>();
                if (srcAnim != null && srcAnim.avatar != null)
                {
                    _mannequinAnimator.avatar = srcAnim.avatar;
                }
            }
        }

        private void SetStudioAnimation(int index)
        {
            if (index < 0 || index >= _animationStates.Length) return;
            _selectedAnimIndex = index;
            string clipName = _animationStates[index];

            if (_mannequinAnimator == null) return;

            if (_playableGraph.IsValid())
            {
                _playableGraph.Destroy();
            }

            AnimationClip clip = LoadClip(clipName);
            if (clip != null)
            {
                AnimationPlayableUtilities.PlayClip(_mannequinAnimator, clip, out _playableGraph);
                _playableGraph.SetTimeUpdateMode(DirectorUpdateMode.UnscaledGameTime);
                _playableGraph.Play();
            }
        }

        private static AnimationClip LoadClip(string clipName)
        {
            var subClips = Resources.LoadAll<AnimationClip>($"Animations/{clipName}");
            if (subClips != null && subClips.Length > 0)
            {
                for (int i = 0; i < subClips.Length; i++)
                {
                    if (subClips[i] != null && !subClips[i].name.StartsWith("__preview__"))
                        return subClips[i];
                }
            }
            return Resources.Load<AnimationClip>($"Animations/{clipName}")
                ?? Resources.Load<AnimationClip>(clipName);
        }

        private void SwitchMannequin(string modelName)
        {
            SpawnMannequin(modelName);
            _statusMessage = $"Mannequin '{modelName}' chargé pour les tests.";
        }

        private void SpawnWeaponInStudio()
        {
            if (_currentWeaponInstance != null)
            {
                DestroyImmediate(_currentWeaponInstance);
                _currentWeaponInstance = null;
            }

            if (_currentMannequinInstance == null || _activeProfile == null) return;
            var currentItem = (_weaponList.Count > 0 && _selectedWeaponIndex < _weaponList.Count)
                ? _weaponList[_selectedWeaponIndex]
                : null;

            if (currentItem == null) return;

            Transform socket = ResolveStudioSocket(_activeProfile.Socket);

            if (!string.IsNullOrEmpty(currentItem.PrefabPath))
            {
                var prefab = Resources.Load<GameObject>($"Guns/{currentItem.PrefabPath}")
                          ?? Resources.Load<GameObject>(currentItem.PrefabPath)
                          ?? Resources.Load<GameObject>($"Objects/{currentItem.PrefabPath}");
                if (prefab != null)
                {
                    _currentWeaponInstance = Instantiate(prefab, socket, false);
                }
            }

            if (_currentWeaponInstance == null)
            {
                _currentWeaponInstance = ArmoryPlaceholderFactory.ResolveOrBuild(currentItem, socket);
                if (_currentWeaponInstance.transform.parent != socket)
                    _currentWeaponInstance.transform.SetParent(socket, false);
            }

            _currentWeaponInstance.name = "StudioWeapon_" + currentItem.Name;

            var cols = _currentWeaponInstance.GetComponentsInChildren<Collider>();
            for (int i = 0; i < cols.Length; i++) cols[i].enabled = false;
            var anims = _currentWeaponInstance.GetComponentsInChildren<Animator>();
            for (int i = 0; i < anims.Length; i++) anims[i].enabled = false;

            ApplyActiveGripTransformToStudioWeapon();
        }

        private void ApplyActiveGripTransformToStudioWeapon()
        {
            if (_currentWeaponInstance == null || _activeProfile == null || _currentMannequinInstance == null) return;

            var currentItem = (_weaponList.Count > 0 && _selectedWeaponIndex < _weaponList.Count)
                ? _weaponList[_selectedWeaponIndex]
                : null;

            Transform socket = _currentWeaponInstance.transform.parent;

            _currentWeaponInstance.transform.localPosition = WeaponGripService.ComputeWeaponLocalPosition(socket, _currentMannequinInstance.transform, _activeProfile, 1.0f);
            _currentWeaponInstance.transform.localRotation = WeaponGripService.ComputeWeaponLocalRotation(_currentWeaponInstance, socket, _currentMannequinInstance.transform, _activeProfile, currentItem);
            _currentWeaponInstance.transform.localScale = WeaponGripService.ComputeWeaponLocalScale(_currentWeaponInstance, socket, _activeProfile, currentItem, 1.0f, _currentMannequinInstance.transform);
        }

        private Transform ResolveStudioSocket(WeaponGripSocket socketType)
        {
            if (_currentMannequinInstance == null) return _studioRoot.transform;

            Animator anim = _mannequinAnimator;
            if (anim != null && anim.isHuman)
            {
                HumanBodyBones bone = socketType switch
                {
                    WeaponGripSocket.LeftHand => HumanBodyBones.LeftHand,
                    WeaponGripSocket.ChestTwoHands => HumanBodyBones.Chest,
                    WeaponGripSocket.Back => HumanBodyBones.UpperChest,
                    WeaponGripSocket.Holster => HumanBodyBones.Hips,
                    _ => HumanBodyBones.RightHand
                };
                var t = anim.GetBoneTransform(bone);
                if (t != null) return t;
            }

            string[] names = socketType switch
            {
                WeaponGripSocket.LeftHand => new[] { "LeftHand", "mixamorig:LeftHand", "Hand.L", "hand.l" },
                WeaponGripSocket.ChestTwoHands => new[] { "Chest", "Spine2", "mixamorig:Spine2", "Spine" },
                WeaponGripSocket.Back => new[] { "UpperChest", "Spine1", "mixamorig:Spine1", "Chest" },
                WeaponGripSocket.Holster => new[] { "Hips", "mixamorig:Hips", "Pelvis" },
                _ => new[] { "RightHand", "mixamorig:RightHand", "Hand.R", "hand.r" }
            };

            for (int i = 0; i < names.Length; i++)
            {
                var found = FindRecursive(_currentMannequinInstance.transform, names[i]);
                if (found != null) return found;
            }

            return _currentMannequinInstance.transform;
        }

        private static Transform FindRecursive(Transform parent, string targetName)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.name.IndexOf(targetName, StringComparison.OrdinalIgnoreCase) >= 0)
                    return child;
                var deep = FindRecursive(child, targetName);
                if (deep != null) return deep;
            }
            return null;
        }

        private void FocusCameraOnWeapon()
        {
            if (_currentWeaponInstance != null)
            {
                _camLookTarget = _currentWeaponInstance.transform.position;
                _camDistance = 1.15f;
            }
            else
            {
                FocusCameraOnHand(true);
            }
        }

        private void FocusCameraOnHand(bool isRightHand)
        {
            Transform socket = ResolveStudioSocket(isRightHand ? WeaponGripSocket.RightHand : WeaponGripSocket.LeftHand);
            _camLookTarget = socket != null ? socket.position : new Vector3(0f, 1.2f, 0f);
            _camDistance = 0.95f;
            _camPitch = 12f;
        }

        private void ResetCameraFullView()
        {
            _camLookTarget = new Vector3(0f, 0.95f, 0f);
            _camDistance = 2.4f;
            _camPitch = 10f;
            _camYaw = 160f;
        }

        private void HandleStudioOrbitInput(Rect vpRect)
        {
            Event e = Event.current;
            if (vpRect.Contains(e.mousePosition))
            {
                if (e.type == EventType.MouseDown && (e.button == 0 || e.button == 1))
                {
                    _isDraggingOrbit = true;
                    _lastMousePos = e.mousePosition;
                    e.Use();
                }
                else if (e.type == EventType.ScrollWheel)
                {
                    _camDistance = Mathf.Clamp(_camDistance + (e.delta.y * 0.12f), 0.35f, 4.5f);
                    e.Use();
                }
            }

            if (_isDraggingOrbit)
            {
                if (e.type == EventType.MouseDrag)
                {
                    Vector2 delta = e.mousePosition - _lastMousePos;
                    _camYaw += delta.x * 1.2f;
                    _camPitch = Mathf.Clamp(_camPitch - delta.y * 1.0f, -70f, 70f);
                    _lastMousePos = e.mousePosition;
                    e.Use();
                }
                else if (e.type == EventType.MouseUp)
                {
                    _isDraggingOrbit = false;
                    e.Use();
                }
            }
        }

        private void RenderStudio()
        {
            if (_studioCam == null) return;

            Quaternion rot = Quaternion.Euler(_camPitch, _camYaw, 0f);
            Vector3 camPos = _camLookTarget - (rot * Vector3.forward * _camDistance);

            _studioCam.transform.position = camPos;
            _studioCam.transform.rotation = rot;
            _studioCam.Render();
        }

        private void CleanupStudio()
        {
            if (_playableGraph.IsValid()) _playableGraph.Destroy();
            _mannequinAnimator = null;

            if (_currentWeaponInstance != null) { DestroyImmediate(_currentWeaponInstance); _currentWeaponInstance = null; }
            if (_currentMannequinInstance != null) { DestroyImmediate(_currentMannequinInstance); _currentMannequinInstance = null; }
            if (_studioRoot != null) { DestroyImmediate(_studioRoot); _studioRoot = null; }
            if (_studioRT != null) { _studioRT.Release(); DestroyImmediate(_studioRT); _studioRT = null; }
        }
    }
}
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
    public class WeaponGripEditorDevWindow : FloatingWindow<WeaponGripEditorDevWindow>
    {
        protected override int WindowId => 899;
        protected override string Title => "Éditeur d'Ancrage des Armes // Weapon Grip Lab";
        protected override Vector2 MinSize => new Vector2(860f, 580f);
        protected override Rect DefaultRect => new Rect(60f, 50f, 960f, 680f);
        protected override KeyCode[] ToggleKeys => _toggleKeys;

        private static readonly KeyCode[] _toggleKeys = { KeyCode.F10 };

        private readonly List<InventoryItem> _weaponList = new();
        private int _selectedWeaponIndex = 0;
        private string _weaponSearch = "";
        private Vector2 _weaponListScroll;
        private Vector2 _controlsScroll;

        private WeaponGripProfile _activeProfile;
        private bool _isDirty = false;
        private string _statusMessage = "Prêt. Modifiez l'ancrage et la longueur en temps réel.";

        private static WeaponGripProfile _clipboardProfile;

        // --- Studio 3D d'Inspection ---
        private RenderTexture _studioRT;
        private Camera _studioCam;
        private GameObject _studioRoot;
        private GameObject _currentMannequinInstance;
        private GameObject _currentWeaponInstance;
        private Animator _mannequinAnimator;
        private PlayableGraph _playableGraph;

        private float _camYaw = 160f;
        private float _camPitch = 15f;
        private float _camDistance = 1.8f;
        private Vector3 _camLookTarget = new Vector3(0f, 1.2f, 0f);
        private bool _isDraggingOrbit = false;
        private Vector2 _lastMousePos;

        private string _mannequinModelName = "Soldier";
        private readonly List<string> _availableMannequins = new();

        private int _selectedAnimIndex = 0;
        private readonly string[] _animationStates = {
            "Fight Idle",
            "Standing Idle",
            "Walking",
            "Melee Attack",
            "Roundkick",
            "Firing Rifle",
            "Rifle Idle",
            "Body Block",
            "Falling Back Death"
        };

        protected override void OnOpened()
        {
            WeaponGripService.ClearDimensionCache();
            WeaponGripService.EnsureInitialized();
            RefreshWeaponCatalog();
            RefreshAvailableMannequins();
            EnsureStudio();
            SelectWeaponByIndex(_selectedWeaponIndex);
        }

        protected override void Awake()
        {
            base.Awake();
            WeaponGripService.EnsureInitialized();
            RefreshWeaponCatalog();
            RefreshAvailableMannequins();
            EnsureStudio();
        }

        protected override void OnClosed()
        {
            CleanupStudio();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            CleanupStudio();
        }

        private void RefreshAvailableMannequins()
        {
            _availableMannequins.Clear();
            _availableMannequins.Add("Soldier");

            var prefabs = Resources.LoadAll<GameObject>("Characters");
            for (int i = 0; i < prefabs.Length; i++)
            {
                if (prefabs[i] != null && !_availableMannequins.Contains(prefabs[i].name))
                {
                    _availableMannequins.Add(prefabs[i].name);
                }
            }
        }

        private void RefreshWeaponCatalog()
        {
            _weaponList.Clear();
            var all = ArmoryCatalog.All;
            for (int i = 0; i < all.Count; i++)
            {
                var it = all[i];
                if (it != null && (it.Type == ItemType.Weapon || it.IsGrenade || it.IsLauncher))
                {
                    _weaponList.Add(it);
                }
            }

            int clubIdx = _weaponList.FindIndex(w => w.Name.IndexOf("Gourdin", StringComparison.OrdinalIgnoreCase) >= 0
                                                  || w.Name.IndexOf("Matraque", StringComparison.OrdinalIgnoreCase) >= 0);
            if (clubIdx >= 0) _selectedWeaponIndex = clubIdx;
        }

        private void SelectWeaponByIndex(int index)
        {
            if (_weaponList.Count == 0) return;
            _selectedWeaponIndex = Mathf.Clamp(index, 0, _weaponList.Count - 1);
            var item = _weaponList[_selectedWeaponIndex];
            _activeProfile = WeaponGripService.ResolveProfile(item).Clone();
            _activeProfile.Key = item.Name;

            if (_activeProfile.TargetWorldLength <= 0.05f)
            {
                _activeProfile.TargetWorldLength = WeaponGripService.GetDefaultWeaponLength(item);
            }

            _isDirty = false;
            SpawnWeaponInStudio();
            FocusCameraOnWeapon();
        }

        protected override void Update()
        {
            base.Update();
            if (_isOpen)
            {
                RenderStudio();
            }
        }

        protected override void DrawContent()
        {
            GUILayout.BeginHorizontal();

            // 1. Colonne de gauche : Catalogue & sélection de l'arme
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(260f), GUILayout.ExpandHeight(true));
            DrawWeaponSelectionColumn();
            GUILayout.EndVertical();

            // 2. Colonne centrale : Viewport 3D temps réel du Soldat avec l'arme en main
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(330f), GUILayout.ExpandHeight(true));
            DrawStudioViewport();
            GUILayout.EndVertical();

            // 3. Colonne de droite : Contrôles de précision (Socket, Position, Rotation, Échelle)
            GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            DrawTransformationControls();
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }

        private void DrawWeaponSelectionColumn()
        {
            GUILayout.Label("<b>Armes du Catalogue :</b>");

            GUILayout.BeginHorizontal();
            GUILayout.Label("🔍", GUILayout.Width(22));
            string newSearch = GUILayout.TextField(_weaponSearch);
            if (newSearch != _weaponSearch)
            {
                _weaponSearch = newSearch;
            }
            if (GUILayout.Button("✕", GUILayout.Width(22))) _weaponSearch = "";
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            _weaponListScroll = GUILayout.BeginScrollView(_weaponListScroll);

            string query = (_weaponSearch ?? "").Trim().ToLowerInvariant();

            for (int i = 0; i < _weaponList.Count; i++)
            {
                var w = _weaponList[i];
                if (!string.IsNullOrEmpty(query) && !w.Name.ToLowerInvariant().Contains(query)
                    && !(w.Category ?? "").ToLowerInvariant().Contains(query))
                {
                    continue;
                }

                bool isSelected = (_selectedWeaponIndex == i);
                GUI.backgroundColor = isSelected ? new Color(0f, 0.85f, 1f) : Color.white;

                string icon = w.IsGrenade ? "💣 " : (w.IsLauncher ? "🚀 " : (w.RangeInTiles > 2 ? "🔫 " : "🗡️ "));
                if (GUILayout.Button($"{icon}<b>{w.Name}</b>\n<color=grey>{w.Category} · {w.DlphCode}</color>", GUI.skin.button, GUILayout.Height(38)))
                {
                    SelectWeaponByIndex(i);
                }

                GUI.backgroundColor = Color.white;
            }

            GUILayout.EndScrollView();
        }

        private void DrawStudioViewport()
        {
            GUILayout.Label("<b>Inspection Soldat // Pose en Main :</b>");

            Rect vpRect = GUILayoutUtility.GetRect(320f, 320f, GUILayout.ExpandWidth(true));
            Color prevBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.015f, 0.035f, 0.065f, 1f);
            GUI.Box(vpRect, GUIContent.none);
            GUI.backgroundColor = prevBg;

            if (_studioRT != null)
            {
                GUI.DrawTexture(vpRect, _studioRT, ScaleMode.ScaleToFit);
            }

            HandleStudioOrbitInput(vpRect);

            // Commandes caméra studio
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("🔍 Main Droite")) FocusCameraOnHand(true);
            if (GUILayout.Button("🔍 Main Gauche")) FocusCameraOnHand(false);
            if (GUILayout.Button("👤 Corps Entier")) ResetCameraFullView();
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            // Sélecteur d'animation pour tester le grip en mouvement
            GUILayout.BeginHorizontal();
            GUILayout.Label("Animation :", GUILayout.Width(75));

            if (GUILayout.Button("◀", GUILayout.Width(24)))
            {
                int next = (_selectedAnimIndex - 1 + _animationStates.Length) % _animationStates.Length;
                SetStudioAnimation(next);
            }

            string animLabel = _animationStates[_selectedAnimIndex];
            GUI.color = Color.cyan;
            GUILayout.Label($"<b>{animLabel}</b>", GUILayout.ExpandWidth(true));
            GUI.color = Color.white;

            if (GUILayout.Button("▶", GUILayout.Width(24)))
            {
                int next = (_selectedAnimIndex + 1) % _animationStates.Length;
                SetStudioAnimation(next);
            }
            GUILayout.EndHorizontal();

            // Sélecteur de modèle mannequin
            GUILayout.BeginHorizontal();
            GUILayout.Label("Mannequin :", GUILayout.Width(75));
            int manIdx = _availableMannequins.IndexOf(_mannequinModelName);
            if (manIdx < 0) manIdx = 0;

            if (GUILayout.Button("◀", GUILayout.Width(24)))
            {
                manIdx = (manIdx - 1 + _availableMannequins.Count) % _availableMannequins.Count;
                SwitchMannequin(_availableMannequins[manIdx]);
            }

            GUILayout.Label($"<b>{_mannequinModelName}</b>", GUILayout.ExpandWidth(true));

            if (GUILayout.Button("▶", GUILayout.Width(24)))
            {
                manIdx = (manIdx + 1) % _availableMannequins.Count;
                SwitchMannequin(_availableMannequins[manIdx]);
            }
            GUILayout.EndHorizontal();
        }

        private void DrawTransformationControls()
        {
            if (_activeProfile == null)
            {
                GUILayout.Label("<i>Sélectionnez une arme à gauche pour éditer son ancrage.</i>");
                return;
            }

            var currentItem = (_weaponList.Count > 0 && _selectedWeaponIndex < _weaponList.Count)
                ? _weaponList[_selectedWeaponIndex]
                : null;

            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>Configuration : <color=#00E5FF>{_activeProfile.Key}</color></b>", GUILayout.ExpandWidth(true));
            if (_isDirty)
            {
                GUI.color = Color.yellow;
                GUILayout.Label("● Non sauvegardé", GUILayout.Width(115));
                GUI.color = Color.white;
            }
            GUILayout.EndHorizontal();

            _controlsScroll = GUILayout.BeginScrollView(_controlsScroll);

            // 1. Choix du Socket / Main d'ancrage
            GUILayout.Label("<b>1. Socket d'Ancrage :</b>");
            GUILayout.BeginHorizontal();
            var prevSocket = _activeProfile.Socket;
            _activeProfile.Socket = (WeaponGripSocket)GUILayout.Toolbar((int)_activeProfile.Socket, new[] { "Main Droite", "Main Gauche", "2M / Poitrine", "Dos", "Hanche" }, GUILayout.Height(24));
            if (_activeProfile.Socket != prevSocket)
            {
                _isDirty = true;
                if (_currentWeaponInstance != null)
                {
                    Transform newSocket = ResolveStudioSocket(_activeProfile.Socket);
                    _currentWeaponInstance.transform.SetParent(newSocket, false);
                    ApplyActiveGripTransformToStudioWeapon();
                }
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);

            // 2. Position Offset (Décalage métrique fin)
            GUILayout.Label("<b>2. Position de Poignée (Décalage Local) :</b>");
            _activeProfile.PositionOffset.x = DrawAxisControl("Axe X (Gauche / Droite)", _activeProfile.PositionOffset.x, -0.5f, 0.5f, 0.01f, 0.05f);
            _activeProfile.PositionOffset.y = DrawAxisControl("Axe Y (Bas / Haut)", _activeProfile.PositionOffset.y, -0.5f, 0.5f, 0.01f, 0.05f);
            _activeProfile.PositionOffset.z = DrawAxisControl("Axe Z (Arrière / Avant)", _activeProfile.PositionOffset.z, -0.5f, 0.5f, 0.01f, 0.05f);

            GUILayout.Space(6);

            // 3. Rotation Offset (Orientation précise du canon / de la lame)
            GUILayout.Label("<b>3. Rotation d'Orientation (Angles Euler) :</b>");
            _activeProfile.RotationOffset.x = DrawAngleControl("Pitch (X) Assiette", _activeProfile.RotationOffset.x, -180f, 180f);
            _activeProfile.RotationOffset.y = DrawAngleControl("Yaw (Y) Direction", _activeProfile.RotationOffset.y, -180f, 180f);
            _activeProfile.RotationOffset.z = DrawAngleControl("Roll (Z) Roulis", _activeProfile.RotationOffset.z, -180f, 180f);

            GUILayout.Space(6);

            // 4. Point de saisie le long du manche (Grip Pivot Offset)
            GUILayout.Label("<b>4. Point de Saisie du Manche (Grip Offset) :</b>");
            _activeProfile.GripPivotOffset.y = DrawAxisControl("Glissement Manche (Y)", _activeProfile.GripPivotOffset.y, -0.8f, 0.8f, 0.02f, 0.10f);

            GUILayout.Space(6);

            // 5. Longueur & Échelle de l'arme
            GUILayout.Label("<b>5. Taille & Longueur Réelle :</b>");

            float defaultLen = WeaponGripService.GetDefaultWeaponLength(currentItem);
            float currentDisplayLen = _activeProfile.TargetWorldLength > 0.02f
                ? _activeProfile.TargetWorldLength
                : defaultLen;

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Longueur Cible : <b>{currentDisplayLen:0.##} m</b>", GUILayout.Width(170));
            bool prevLenChanged = GUI.changed;
            GUI.changed = false;
            float newLen = GUILayout.HorizontalSlider(currentDisplayLen, 0.05f, 2.50f);
            if (GUI.changed)
            {
                _activeProfile.TargetWorldLength = newLen;
                _isDirty = true;
                ApplyActiveGripTransformToStudioWeapon();
            }
            GUI.changed |= prevLenChanged;
            if (GUILayout.Button("Défaut", GUILayout.Width(55)))
            {
                _activeProfile.TargetWorldLength = defaultLen;
                _isDirty = true;
                ApplyActiveGripTransformToStudioWeapon();
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Échelle Multiplicatrice : <b>{_activeProfile.ScaleMultiplier:0.##}x</b>", GUILayout.Width(170));
            bool prevScaleChanged = GUI.changed;
            GUI.changed = false;
            float newScale = GUILayout.HorizontalSlider(_activeProfile.ScaleMultiplier, 0.05f, 3.0f);
            if (GUI.changed)
            {
                _activeProfile.ScaleMultiplier = newScale;
                _isDirty = true;
                ApplyActiveGripTransformToStudioWeapon();
            }
            GUI.changed |= prevScaleChanged;
            if (GUILayout.Button("1.0x", GUILayout.Width(55)))
            {
                _activeProfile.ScaleMultiplier = 1.0f;
                _isDirty = true;
                ApplyActiveGripTransformToStudioWeapon();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(8);

            // Actions de Sauvegarde et Application
            GUILayout.BeginHorizontal();

            GUI.backgroundColor = new Color(0.2f, 0.85f, 0.4f);
            if (GUILayout.Button("💾 Enregistrer Profil", GUILayout.Height(30)))
            {
                SaveCurrentProfile();
            }

            GUI.backgroundColor = new Color(0.1f, 0.7f, 1f);
            if (GUILayout.Button("⚡ Appliquer en Scène", GUILayout.Height(30)))
            {
                SaveCurrentProfile();
                ApplyToActiveUnitsInScene();
            }

            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();

            if (GUILayout.Button("📋 Copier"))
            {
                _clipboardProfile = _activeProfile.Clone();
                _statusMessage = "Profil copié dans le presse-papier.";
            }

            GUI.enabled = (_clipboardProfile != null);
            if (GUILayout.Button("📋 Coller"))
            {
                if (_clipboardProfile != null)
                {
                    string keepKey = _activeProfile.Key;
                    _activeProfile = _clipboardProfile.Clone();
                    _activeProfile.Key = keepKey;
                    _isDirty = true;
                    ApplyActiveGripTransformToStudioWeapon();
                    _statusMessage = "Profil collé. Cliquez sur Enregistrer pour valider.";
                }
            }
            GUI.enabled = true;

            GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
            if (GUILayout.Button("↺ Rétablir Défaut"))
            {
                WeaponGripService.ResetProfileToDefault(_activeProfile.Key);
                SelectWeaponByIndex(_selectedWeaponIndex);
                _statusMessage = $"Profil '{_activeProfile.Key}' réinitialisé aux valeurs par défaut.";
            }
            GUI.backgroundColor = Color.white;

            GUILayout.EndHorizontal();

            GUILayout.EndScrollView();
        }

        private float DrawAxisControl(string label, float val, float min, float max, float fineStep, float coarseStep)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label} : <b>{val * 100f:+0.0;-0.0;0.0} cm</b>", GUILayout.Width(190));

            if (GUILayout.Button($"-{coarseStep * 100f:0}c", GUILayout.Width(38))) { val -= coarseStep; _isDirty = true; ApplyActiveGripTransformToStudioWeapon(); }
            if (GUILayout.Button($"-{fineStep * 100f:0}c", GUILayout.Width(34))) { val -= fineStep; _isDirty = true; ApplyActiveGripTransformToStudioWeapon(); }
            if (GUILayout.Button("0", GUILayout.Width(22))) { val = 0f; _isDirty = true; ApplyActiveGripTransformToStudioWeapon(); }
            if (GUILayout.Button($"+{fineStep * 100f:0}c", GUILayout.Width(34))) { val += fineStep; _isDirty = true; ApplyActiveGripTransformToStudioWeapon(); }
            if (GUILayout.Button($"+{coarseStep * 100f:0}c", GUILayout.Width(38))) { val += coarseStep; _isDirty = true; ApplyActiveGripTransformToStudioWeapon(); }

            GUILayout.EndHorizontal();

            bool prevChanged = GUI.changed;
            GUI.changed = false;
            float newVal = GUILayout.HorizontalSlider(val, min, max);
            if (GUI.changed)
            {
                val = newVal;
                _isDirty = true;
                ApplyActiveGripTransformToStudioWeapon();
            }
            GUI.changed |= prevChanged;

            return val;
        }

        private float DrawAngleControl(string label, float val, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label} : <b>{val:+0.0;-0.0;0.0}°</b>", GUILayout.Width(190));

            if (GUILayout.Button("-45°", GUILayout.Width(38))) { val = WrapAngle(val - 45f); _isDirty = true; ApplyActiveGripTransformToStudioWeapon(); }
            if (GUILayout.Button("-15°", GUILayout.Width(34))) { val = WrapAngle(val - 15f); _isDirty = true; ApplyActiveGripTransformToStudioWeapon(); }
            if (GUILayout.Button("0°", GUILayout.Width(26))) { val = 0f; _isDirty = true; ApplyActiveGripTransformToStudioWeapon(); }
            if (GUILayout.Button("+15°", GUILayout.Width(34))) { val = WrapAngle(val + 15f); _isDirty = true; ApplyActiveGripTransformToStudioWeapon(); }
            if (GUILayout.Button("+45°", GUILayout.Width(38))) { val = WrapAngle(val + 45f); _isDirty = true; ApplyActiveGripTransformToStudioWeapon(); }
            if (GUILayout.Button("180°", GUILayout.Width(40))) { val = WrapAngle(val + 180f); _isDirty = true; ApplyActiveGripTransformToStudioWeapon(); }

            GUILayout.EndHorizontal();

            bool prevChanged = GUI.changed;
            GUI.changed = false;
            float newVal = GUILayout.HorizontalSlider(val, min, max);
            if (GUI.changed)
            {
                val = newVal;
                _isDirty = true;
                ApplyActiveGripTransformToStudioWeapon();
            }
            GUI.changed |= prevChanged;

            return val;
        }

        private static float WrapAngle(float angle)
        {
            while (angle > 180f) angle -= 360f;
            while (angle < -180f) angle += 360f;
            return angle;
        }

        private void SaveCurrentProfile()
        {
            if (_activeProfile == null) return;
            GUI.FocusControl(null);
            WeaponGripService.SaveProfile(_activeProfile);
            _isDirty = false;
            _statusMessage = $"Profil '{_activeProfile.Key}' sauvegardé sur disque avec succès !";
        }

        private void ApplyToActiveUnitsInScene()
        {
            var units = FindObjectsByType<TacticalUnit>();
            for (int i = 0; i < units.Length; i++)
            {
                var u = units[i];
                if (u != null)
                {
                    u.NotifyInventoryChanged(saveToDisk: false);
                }
            }
            _statusMessage = "Profils de grip appliqués à toutes les unités de la scène.";
        }

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
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
        protected override int WindowId => 899;
        protected override string Title => "Éditeur d'Ancrage des Armes // Weapon Grip Lab (F5)";
        protected override Vector2 MinSize => new Vector2(860f, 580f);
        protected override Rect DefaultRect => new Rect(60f, 50f, 960f, 680f);
        protected override KeyCode[] ToggleKeys => _toggleKeys;

        private static readonly KeyCode[] _toggleKeys = { KeyCode.F5 };

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

    }
}
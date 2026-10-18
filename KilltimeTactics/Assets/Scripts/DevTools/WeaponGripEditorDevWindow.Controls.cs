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
            var units = TacticalUnitRegistry.AllUnits;
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (u != null)
                {
                    u.NotifyInventoryChanged(saveToDisk: false);
                }
            }
            _statusMessage = "Profils de grip appliqués à toutes les unités de la scène.";
        }
    }
}
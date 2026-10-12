using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Animations;
using Killtime.Core.Character;
using Killtime.Core.Character.Classes;
using Killtime.Core.Arcanotech;
using Killtime.Core.Inventory;
using Killtime.DevTools.Character;
using Killtime.Tactics;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;

namespace Killtime.UI
{
    public partial class CharacterDevWindow
    {
        // ================= TAB 0 : CRÉATION =================
        private void DrawCreationTab()
        {
            CharacterProgressionManager.SynchronizeProgression(_currentSheet);

            DrawIdentitySection();
            DrawAttributesSection();
            DrawVitalsSection();
            DrawProgressionSummarySection();

            GUILayout.Space(6);
            DrawClassPresetSection();

            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.2f, 0.6f, 1.0f);
            if (GUILayout.Button("➕ Nouveau Personnage", GUILayout.Height(34)))
            {
                CreateNewCharacter();
            }
            GUI.backgroundColor = new Color(0.2f, 0.7f, 0.3f);
            if (GUILayout.Button("💾 Sauvegarder cette fiche sur le Disque", GUILayout.Height(34)))
            {
                SaveCharacterAndSyncUnits(_currentSheet);
                _statusMessage = $"Fiche '{_currentSheet.Name}' sauvegardée et synchronisée avec la scène !";
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();
        }
        // ================= CLASSES DE BASE (chargement rapide + guide) =================

        /// <summary>
        /// Section 5 — Archétypes de classe : chargement en 1 clic + guide de build.
        /// Le chargement applique attributs + package de départ ; l'arbre reste
        /// 100% libre ensuite, les étapes futures sont highlightées en or.
        /// </summary>
        private void DrawClassPresetSection()
        {
            GUILayout.Label("<b>5. Archétypes de Classe — Chargement Rapide (arbre libre + guide surligné) :</b>");
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("<i>Chargez un preset au lieu de tout choisir à la main. L'arbre reste accessible partout comme un PJ normal ; les prochaines étapes du build sont highlightées en <color=#FFD166><b>or</b></color> dans la Voûte et ci-dessous.</i>");

            var all = CharacterClassCatalog.GetAll();
            var active = CharacterClassCatalog.GetActiveGuide(_currentSheet);

            if (active != null)
            {
                int done = active.GetAcquiredCount(_currentSheet);
                int total = active.BuildPath != null ? active.BuildPath.Count : 0;
                var next = active.GetNextSteps(_currentSheet);
                string nextLabel = next.Count > 0 ? string.Join(" • ", next.GetRange(0, Math.Min(3, next.Count))) : "Build terminé ★";
                GUILayout.BeginHorizontal(GUI.skin.box);
                GUILayout.Label($"<b>{active.IconGlyph} Guide actif : {active.DisplayName}</b> ({done}/{total}) — <color=#FFD166>Suivant : {nextLabel}</color>", GUILayout.ExpandWidth(true));
                GUI.backgroundColor = new Color(0.1f, 0.75f, 1.0f);
                if (GUILayout.Button("🌌 Voir Voûte", GUILayout.Width(110), GUILayout.Height(26)))
                    SkillTreeCosmosWindow.OpenForCharacter(_currentSheet);
                GUI.backgroundColor = Color.white;
                if (GUILayout.Button("❌ Quitter guide", GUILayout.Width(110), GUILayout.Height(26)))
                {
                    CharacterClassCatalog.ClearGuide(_currentSheet);
                    SaveCharacterAndSyncUnits(_currentSheet);
                    _statusMessage = "Guide de build désactivé — arbre totalement libre, preset conservé sur la fiche.";
                }
                GUILayout.EndHorizontal();
            }

            // Grille 2 colonnes de presets.
            for (int i = 0; i < all.Count; i += 2)
            {
                GUILayout.BeginHorizontal();
                for (int k = 0; k < 2; k++)
                {
                    int idx = i + k;
                    if (idx >= all.Count)
                    {
                        GUILayout.FlexibleSpace();
                        break;
                    }
                    var def = all[idx];
                    bool isActive = active != null && active.ClassId == def.ClassId;
                    Color prevBg = GUI.backgroundColor;
                    GUI.backgroundColor = isActive ? new Color(0.98f, 0.75f, 0.2f) : new Color(0.16f, 0.22f, 0.32f);
                    GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true));
                    GUILayout.Label($"{def.IconGlyph} <b>{def.DisplayName}</b>{(isActive ? " <color=#FFD166>[GUIDE ACTIF]</color>" : "")}");
                    GUILayout.Label($"<color=#94A3B8><i>{def.Tagline}</i></color>");
                    GUILayout.Label($"<color=#94A3B8>{def.Description}</color>");
                    if (GUILayout.Button(isActive ? "↻ Recharger ce preset" : "📥 Charger ce preset", GUILayout.Height(26)))
                    {
                        string keepName = _currentSheet.Name;
                        if (def.ApplyToSheet(_currentSheet, out string msg))
                        {
                            // Le chargement ne doit pas écraser le nom choisi par le joueur.
                            if (!string.IsNullOrWhiteSpace(keepName) && keepName != "Nouveau Personnage")
                                _currentSheet.Name = keepName;
                            SaveCharacterAndSyncUnits(_currentSheet);
                            _statusMessage = msg;
                        }
                        else _statusMessage = msg;
                    }
                    GUILayout.EndVertical();
                    GUI.backgroundColor = prevBg;
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
        }
        private void DrawModelSelector()
        {
            if (_availableModelNames.Count == 0)
            {
                RefreshAvailableModels();
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Modèle 3D :", GUILayout.Width(90));

            int currentIndex = 0;
            if (!string.IsNullOrEmpty(_currentSheet.ModelPrefabName))
            {
                currentIndex = _availableModelNames.IndexOf(_currentSheet.ModelPrefabName);
                if (currentIndex < 0)
                {
                    _availableModelNames.Add(_currentSheet.ModelPrefabName);
                    currentIndex = _availableModelNames.IndexOf(_currentSheet.ModelPrefabName);
                }
            }

            if (GUILayout.Button("◀", GUILayout.Width(28)))
            {
                currentIndex = (currentIndex - 1 + _availableModelNames.Count) % _availableModelNames.Count;
                _currentSheet.ModelPrefabName = (currentIndex == 0) ? "" : _availableModelNames[currentIndex];
            }

            string currentDisplay = string.IsNullOrEmpty(_currentSheet.ModelPrefabName) 
                ? "📦 (Procédural / Défaut)" 
                : $"🤖 {_currentSheet.ModelPrefabName}";

            GUI.color = string.IsNullOrEmpty(_currentSheet.ModelPrefabName) ? Color.gray : Color.cyan;
            GUILayout.Label($"<b>{currentDisplay}</b>", GUILayout.Width(190));
            GUI.color = Color.white;

            if (GUILayout.Button("▶", GUILayout.Width(28)))
            {
                currentIndex = (currentIndex + 1) % _availableModelNames.Count;
                _currentSheet.ModelPrefabName = (currentIndex == 0) ? "" : _availableModelNames[currentIndex];
            }

            if (GUILayout.Button(_showModelDropdown ? "▲ Liste" : "▼ Liste", GUILayout.Width(58)))
            {
                _showModelDropdown = !_showModelDropdown;
            }

            if (GUILayout.Button("🔄 Scan", GUILayout.Width(58)))
            {
                RefreshAvailableModels();
                _statusMessage = $"Modèles scannés dans Resources/Characters ({_availableModelNames.Count - 1} trouvé(s)).";
            }
            GUILayout.EndHorizontal();

            if (_showModelDropdown)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label("<color=#52687A><i>Modèles disponibles sous Assets/Resources/Characters :</i></color>");
                for (int i = 0; i < _availableModelNames.Count; i++)
                {
                    string mName = _availableModelNames[i];
                    bool isSelected = (i == 0 && string.IsNullOrEmpty(_currentSheet.ModelPrefabName)) || (_currentSheet.ModelPrefabName == mName);

                    GUI.backgroundColor = isSelected ? new Color(0.1f, 0.6f, 0.9f) : Color.white;
                    string itemLabel = (i == 0) ? "📦 (Procédural / Défaut)" : $"🤖 {mName}";

                    if (GUILayout.Button(itemLabel))
                    {
                        _currentSheet.ModelPrefabName = (i == 0) ? "" : mName;
                        _showModelDropdown = false;
                    }
                    GUI.backgroundColor = Color.white;
                }
                GUILayout.EndVertical();
            }

            GUILayout.Space(6);

            // Hub Visuel : Aperçu 3D du modèle sélectionné & Contrôleur d'animations
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            DrawPreviewViewport(210, 210);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            DrawAnimationStateSelector();
        }

        private void DrawAnimationStateSelector()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Animation :", GUILayout.Width(80));

            if (GUILayout.Button("◀", GUILayout.Width(28)))
            {
                int next = (_selectedPreviewAnimIndex - 1 + _previewAnimationStates.Length) % _previewAnimationStates.Length;
                PlayPreviewAnimation(next);
            }

            string currentAnimName = _previewAnimationStates[_selectedPreviewAnimIndex];
            GUI.color = Color.cyan;
            GUILayout.Label($"<b>🎭 {currentAnimName}</b>", GUILayout.Width(200));
            GUI.color = Color.white;

            if (GUILayout.Button("▶", GUILayout.Width(28)))
            {
                int next = (_selectedPreviewAnimIndex + 1) % _previewAnimationStates.Length;
                PlayPreviewAnimation(next);
            }

            if (GUILayout.Button(_showAnimDropdown ? "▲ Liste" : "▼ Liste", GUILayout.Width(58)))
            {
                _showAnimDropdown = !_showAnimDropdown;
            }

            if (GUILayout.Button("↺", GUILayout.Width(30)))
            {
                PlayPreviewAnimation(_selectedPreviewAnimIndex);
            }
            GUILayout.EndHorizontal();

            if (_showAnimDropdown)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label("<color=#52687A><i>États d'animation disponibles (Assets/Resources/Animations) :</i></color>");
                for (int i = 0; i < _previewAnimationStates.Length; i++)
                {
                    bool isSelected = (_selectedPreviewAnimIndex == i);
                    GUI.backgroundColor = isSelected ? new Color(0.0f, 0.85f, 1.0f) : Color.white;
                    if (GUILayout.Button($"🎬 {_previewAnimationStates[i]}"))
                    {
                        PlayPreviewAnimation(i);
                        _showAnimDropdown = false;
                    }
                    GUI.backgroundColor = Color.white;
                }
                GUILayout.EndVertical();
            }
        }

        private int DrawAttrRow(string label, int value, int attrIndex)
        {
            bool canDecrease = CanDecreaseAttribute(attrIndex);
            bool canIncrease = CanIncreaseAttribute(attrIndex);

            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(135));

            GUI.enabled = canDecrease;
            if (GUILayout.Button("-", GUILayout.Width(26), GUILayout.Height(20)))
            {
                value--;
            }
            GUI.enabled = true;

            GUILayout.Label($"<b>{value}</b>", GUILayout.Width(24));

            GUI.enabled = canIncrease;
            if (GUILayout.Button("+", GUILayout.Width(26), GUILayout.Height(20)))
            {
                value++;
            }
            GUI.enabled = true;

            GUILayout.EndHorizontal();
            return value;
        }
        private void DrawAttrXpUpgradeRow(string label, int attrIndex)
        {
            int currentBase = _currentSheet.GetBaseAttributeValue(attrIndex);
            int cost = CharacterProgressionManager.GetAttributeUpgradeCost(currentBase);
            int spendable = CharacterProgressionManager.GetSpendableForAttribute(_currentSheet, attrIndex);

            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(125));

            if (currentBase >= 10)
            {
                GUI.color = Color.green;
                GUILayout.Label("★ 10 (Plafond)", GUILayout.ExpandWidth(true));
                GUI.color = Color.white;
            }
            else
            {
                GUILayout.Label($"<color=#38BDF8>{currentBase} ➔ {currentBase + 1}</color>", GUILayout.Width(52));

                bool canAfford = spendable >= cost;
                GUI.enabled = canAfford;
                GUI.backgroundColor = canAfford ? new Color(0.2f, 0.85f, 0.45f) : Color.gray;

                if (GUILayout.Button($"Acheter ({cost} XP)", GUILayout.Height(20), GUILayout.ExpandWidth(true)))
                {
                    if (CharacterProgressionManager.UpgradeAttribute(_currentSheet, attrIndex, out string msg))
                    {
                        SaveCharacterAndSyncUnits(_currentSheet);
                        _statusMessage = msg;
                    }
                    else
                    {
                        _statusMessage = msg;
                    }
                }

                GUI.backgroundColor = Color.white;
                GUI.enabled = true;
            }

            GUILayout.EndHorizontal();
        }

        private bool CanDecreaseAttribute(int attrIndex)
        {
            return AttributeRulesService.CanDecrease(_currentSheet, attrIndex);
        }

        private bool CanIncreaseAttribute(int attrIndex)
        {
            return AttributeRulesService.CanIncrease(_currentSheet, attrIndex);
        }

        private bool IsCandidateAllocationValid(int[] candidate, CharacterProfileType profile, int magie)
        {
            return AttributeRulesService.IsCandidateAllocationValid(candidate, profile, magie);
        }

        private void ApplyDefaultProfileAttributes(CharacterSheet sheet)
        {
            AttributeRulesService.ApplyDefaultProfileAttributes(sheet);
        }

        private static int CountTrainedLevels(CharacterSheet sheet)
        {
            return AttributeRulesService.CountTrainedLevels(sheet);
        }

        private bool ValidateAttributeAllocation(CharacterSheet sheet, out string statusMessage)
        {
            return AttributeRulesService.ValidateAllocation(sheet, out statusMessage);
        }
    }
}

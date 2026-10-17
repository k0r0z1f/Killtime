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
        private void DrawSpecsCheatBar()
        {
            // 1. Barre de contrôle Dev & Cheat
            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.Label("<b>Maîtrises & Spécialisations du Codex :</b>", GUILayout.Width(260));

            Color prevCheatBg = GUI.backgroundColor;
            GUI.backgroundColor = _showHiddenModelCheats ? new Color(1f, 0.25f, 0.85f) : new Color(0.18f, 0.25f, 0.35f);
            if (GUILayout.Button(_showHiddenModelCheats ? "👁️ [DEV CHEAT] Secrets : VISIBLES" : "👁️ [DEV CHEAT] Secrets : CACHÉS", GUILayout.Width(240), GUILayout.Height(24)))
            {
                _showHiddenModelCheats = !_showHiddenModelCheats;
                _statusMessage = _showHiddenModelCheats
                    ? "Mode Dev Cheat activé : Toutes les arborescences et maîtrises secrètes sont affichées."
                    : "Mode standard restauré : Les maîtrises secrètes respectent leurs prérequis.";
            }
            GUI.backgroundColor = prevCheatBg;

            if (_showHiddenModelCheats)
            {
                GUI.backgroundColor = new Color(1f, 0.2f, 0.8f);
                if (GUILayout.Button("⚡ Tout Débloquer", GUILayout.Width(140), GUILayout.Height(24)))
                {
                    int unlockedCount = 0;
                    foreach (var specDetail in CharacterProgressionManager.GetAllSpecializations())
                    {
                        if (!_currentSheet.UnlockedSpecializations.Contains(specDetail.Name))
                        {
                            CharacterProgressionManager.UnlockSpecialization(_currentSheet, specDetail.Name, out _, forceFree: true);
                            unlockedCount++;
                        }
                    }
                    SaveCharacterAndSyncUnits(_currentSheet);
                    _statusMessage = $"[CHEAT] {unlockedCount} spécialisation(s) et maîtrise(s) débloquées instantanément !";
                }
                GUI.backgroundColor = Color.white;
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private void DrawSpecializationsSection(CharacterClassDefinition activeGuide)
        {
            // 2. Rendu complet de toutes les arborescences avec sous-branches et hiérarchie (filtré par fiche héroïque ou profil par défaut)
            var allSpecs = new List<CharacterProgressionManager.SpecializationDetail>(CharacterProgressionManager.GetAllSpecializations(_currentSheet));
            allSpecs.Sort((a, b) =>
            {
                int skillCmp = a.SourceSkill.CompareTo(b.SourceSkill);
                if (skillCmp != 0) return skillCmp;
                if (!a.IsImprovement && b.IsImprovement) return -1;
                if (a.IsImprovement && !b.IsImprovement) return 1;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });

            SkillType lastSkill = (SkillType)(-1);
            for (int sIdx = 0; sIdx < allSpecs.Count; sIdx++)
            {
                var detail = allSpecs[sIdx];
                if (detail == null) continue;

                if (detail.IsHidden && !_showHiddenModelCheats && !_currentSheet.UnlockedSpecializations.Contains(detail.Name))
                {
                    bool parentOwned = !string.IsNullOrEmpty(detail.ParentSpecialization) && _currentSheet.UnlockedSpecializations.Contains(detail.ParentSpecialization);
                    if (!parentOwned) continue;
                }

                bool isVol2Spec = detail.IsVolume2 || CharacterProgressionManager.IsVolume2Specialization(detail.Name);
                bool isVol2Locked = isVol2Spec && !CharacterProgressionManager.IsVolume2Unlocked();
                if (isVol2Locked && !_showHiddenModelCheats && !_currentSheet.UnlockedSpecializations.Contains(detail.Name))
                {
                    continue;
                }

                if (detail.SourceSkill != lastSkill)
                {
                    lastSkill = detail.SourceSkill;
                    GUILayout.Space(4);
                    string skillHeader = SkillDefinitions.GetDisplayName(lastSkill).ToUpperInvariant();
                    GUILayout.Label($"<color=#00E5FF><b>━━━ {skillHeader} ━━━</b></color>");
                }

                DrawSpecCard(detail, activeGuide, isVol2Locked);
            }
        }

        private void DrawSpecCard(CharacterProgressionManager.SpecializationDetail detail, CharacterClassDefinition activeGuide, bool isVol2Locked)
        {
            if (detail == null) return;
            bool owned = _currentSheet.UnlockedSpecializations.Contains(detail.Name);
            bool isSecret = detail.IsHidden;
            bool parentMet = string.IsNullOrEmpty(detail.ParentSpecialization) || _currentSheet.UnlockedSpecializations.Contains(detail.ParentSpecialization);

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();

            string indent = detail.IsImprovement ? "   ↳ " : "★ ";
            if (isSecret) indent = detail.IsImprovement ? "   ↳ 🔮 " : "🔮 ";

            string nameColor = owned ? "#00FF88" : (isSecret ? "#F43F5E" : (detail.IsImprovement ? "#E2E8F0" : "#FFE600"));
            string secretTag = isSecret ? " <color=#F43F5E>[SECRET]</color>" : "";
            // Highlight guide de build : NEXT = or fort, FUTURE = or discret.
            string guideTag = "";
            if (activeGuide != null && !owned)
            {
                var st = activeGuide.GetSpecState(_currentSheet, detail.Name);
                if (st == CharacterClassDefinition.GuideStepState.Next)
                    guideTag = " <color=#FFD166><b>◆ SUIVANT BUILD</b></color>";
                else if (st == CharacterClassDefinition.GuideStepState.Future)
                    guideTag = " <color=#8A6D2B>◇ build</color>";
            }
            else if (activeGuide != null && owned && activeGuide.BuildPath != null && activeGuide.BuildPath.Contains(detail.Name))
            {
                guideTag = " <color=#00FF88>★ build</color>";
            }
            GUILayout.Label($"{indent}<b><color={nameColor}>{detail.Name}</color></b>{secretTag}{guideTag}", GUILayout.ExpandWidth(true));

            if (owned)
            {
                GUI.color = Color.green;
                GUILayout.Label("✅ Acquis", GUILayout.Width(75));
                GUI.color = Color.white;
            }
            else if (isVol2Locked && !_showHiddenModelCheats)
            {
                GUI.color = new Color(1f, 0.45f, 0.2f);
                GUILayout.Label("🔒 Phase 2 / Vol. II Requis", GUILayout.Width(190));
                GUI.color = Color.white;
            }
            else if (!parentMet && !_showHiddenModelCheats)
            {
                GUI.color = new Color(0.6f, 0.6f, 0.6f);
                GUILayout.Label($"🔒 Req: {detail.ParentSpecialization}", GUILayout.Width(190));
                GUI.color = Color.white;
            }
            else
            {
                if (!isVol2Locked)
                {
                    bool canAfford = CharacterProgressionManager.GetSpendableFor(_currentSheet, detail.SourceSkill) >= CharacterProgressionManager.XP_COST_SPECIALIZATION;
                    GUI.enabled = canAfford;
                    GUI.backgroundColor = canAfford ? new Color(0.2f, 0.8f, 0.4f) : Color.gray;

                    if (GUILayout.Button("Débloquer (5 XP)", GUILayout.Width(130)))
                    {
                        if (CharacterProgressionManager.UnlockSpecialization(_currentSheet, detail.Name, out string msg, forceFree: false))
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
                else
                {
                    GUI.color = new Color(1f, 0.45f, 0.2f);
                    GUILayout.Label("🔒 Verrou Causal (Vol. II)", GUILayout.Width(170));
                    GUI.color = Color.white;
                }

                if (_showHiddenModelCheats)
                {
                    GUI.backgroundColor = new Color(1f, 0.2f, 0.8f);
                    if (GUILayout.Button("⚡ Cheat", GUILayout.Width(65)))
                    {
                        CharacterProgressionManager.UnlockSpecialization(_currentSheet, detail.Name, out string cheatMsg, forceFree: true);
                        SaveCharacterAndSyncUnits(_currentSheet);
                        _statusMessage = cheatMsg;
                    }
                    GUI.backgroundColor = Color.white;
                }
            }

            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(detail.Description) || !string.IsNullOrEmpty(detail.MechanicalEffect))
            {
                string effectText = !string.IsNullOrEmpty(detail.MechanicalEffect) ? $" — <i>Effet : {detail.MechanicalEffect}</i>" : "";
                GUILayout.Label($"<color=#94A3B8>{detail.Description}{effectText}</color>");
            }

            GUILayout.EndVertical();
        }
    }
}

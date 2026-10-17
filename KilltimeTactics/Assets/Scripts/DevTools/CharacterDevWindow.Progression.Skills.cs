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
        private void DrawSkillTreesSection(CharacterClassDefinition activeGuide)
        {
            GUILayout.Space(4);
            GUI.backgroundColor = new Color(0.1f, 0.75f, 1.0f);
            if (GUILayout.Button("🌌 OUVRIR LA VOÛTE CÉLESTE (Arbre Cosmique 3D / Constellations)", GUILayout.Height(30)))
            {
                SkillTreeCosmosWindow.OpenForCharacter(_currentSheet);
            }
            GUI.backgroundColor = Color.white;

            GUILayout.Space(6);
            GUILayout.Label("<b>Arbres de Compétences (5 XP liés/libre = +1 Palier, 10 si croisé, ou Gratuit ÉRU) :</b>");

            if (activeGuide != null)
            {
                int doneG = activeGuide.GetAcquiredCount(_currentSheet);
                int totalG = activeGuide.BuildPath != null ? activeGuide.BuildPath.Count : 0;
                var nextG = activeGuide.GetNextSteps(_currentSheet);
                string nextGLabel = nextG.Count > 0 ? string.Join(" • ", nextG.GetRange(0, Math.Min(3, nextG.Count))) : "Build terminé ★";
                GUILayout.BeginHorizontal(GUI.skin.box);
                GUILayout.Label($"{activeGuide.IconGlyph} <b>Guide : {activeGuide.DisplayName}</b> ({doneG}/{totalG}) — <color=#FFD166>Suivant : {nextGLabel}</color>", GUILayout.ExpandWidth(true));
                if (GUILayout.Button("❌ Quitter guide", GUILayout.Width(120)))
                {
                    CharacterClassCatalog.ClearGuide(_currentSheet);
                    SaveCharacterAndSyncUnits(_currentSheet);
                    _statusMessage = "Guide de build désactivé — arbre totalement libre.";
                }
                GUILayout.EndHorizontal();
            }

            foreach (var entry in _currentSheet.Skills)
            {
                bool isAccessible = CharacterProgressionManager.IsSkillAccessible(_currentSheet, entry.Skill);
                if (!isAccessible)
                {
                    DrawLockedSkillRow(entry);
                    continue;
                }

                DrawTrainableSkillRow(entry, activeGuide);
            }
        }

        private void DrawLockedSkillRow(SkillProgressionEntry entry)
        {
            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.Label($"<b>{SkillDefinitions.GetDisplayName(entry.Skill)}</b>", GUILayout.Width(200));
            GUI.color = new Color(0.95f, 0.4f, 0.4f);
            // Libellé héroïque délégué (voir MinaCharacter / LucasCharacter / ThomasCharacter / JohnCharacter / ErikaCharacter / VanceCharacter).
            string lockLabel = MinaCharacter.IsMina(_currentSheet)
                ? MinaCharacter.RestrictionLabel
                : (LucasCharacter.IsLucas(_currentSheet) 
                    ? LucasCharacter.RestrictionLabel 
                    : (ThomasCharacter.IsThomas(_currentSheet) 
                        ? ThomasCharacter.RestrictionLabel 
                        : (JohnCharacter.IsJohn(_currentSheet)
                            ? JohnCharacter.RestrictionLabel
                            : (ErikaCharacter.IsErika(_currentSheet)
                                ? ErikaCharacter.RestrictionLabel
                                : (VanceCharacter.IsVance(_currentSheet)
                                    ? VanceCharacter.RestrictionLabel
                                : "🔒 Inaccessible (Affinité héroïque)")))));
            GUILayout.Label(lockLabel, GUILayout.ExpandWidth(true));
            GUI.color = Color.white;
            GUILayout.EndHorizontal();
        }

        private void DrawTrainableSkillRow(SkillProgressionEntry entry, CharacterClassDefinition activeGuide)
        {
            GUILayout.BeginHorizontal(GUI.skin.box);
            string skillName = SkillDefinitions.GetDisplayName(entry.Skill);
            bool isMax = entry.TrainingLevel >= CharacterProgressionManager.MAX_SKILL_TRAINING;
            int baseRank = CharacterProgressionManager.GetBaseRankForSheet(_currentSheet, entry.Skill, false);
            int maxTicks = CharacterProgressionManager.GetProgressMax(baseRank);
            int spendable = CharacterProgressionManager.GetSpendableFor(_currentSheet, entry.Skill);
            bool isSkillNext = activeGuide != null && activeGuide.IsSkillNext(_currentSheet, entry.Skill);
            string skillGuideTag = isSkillNext ? $" <color=#FFD166><b>◆ BUILD +{activeGuide.GetTargetTraining(entry.Skill)}</b></color>" : "";

            GUILayout.Label($"<b>{skillName}</b> (+{entry.TrainingLevel}/{CharacterProgressionManager.MAX_SKILL_TRAINING}) | Base {baseRank} | <color=#00E5FF>{entry.ProgressTicks}/{maxTicks}</color> | Banque <color=yellow>{entry.ReserveXP}</color>{skillGuideTag}", GUILayout.Width(340));

            if (isMax)
            {
                GUI.color = Color.green;
                GUILayout.Label("✅ Plafond Max (+3)", GUILayout.Width(280));
                GUI.color = Color.white;
            }
            else
            {
                bool hasFree = _currentSheet.GetFreeTrainingsRemaining() > 0;
                GUI.enabled = hasFree;
                GUI.backgroundColor = hasFree ? new Color(0.35f, 0.85f, 0.45f) : Color.white;
                if (GUILayout.Button("Gratuit (ÉRU)", GUILayout.Width(110)))
                {
                    if (CharacterProgressionManager.TrainSkillFree(_currentSheet, entry.Skill, out string freeMsg))
                        _statusMessage = freeMsg;
                    else
                        _statusMessage = freeMsg;
                }
                GUI.backgroundColor = Color.white;
                GUI.enabled = true;

                bool canAfford = spendable >= CharacterProgressionManager.XP_COST_TRAINING;
                GUI.enabled = canAfford;
                if (GUILayout.Button($"Entraîner ({CharacterProgressionManager.XP_COST_TRAINING} XP)", GUILayout.Width(160)))
                {
                    if (CharacterProgressionManager.TrainSkill(_currentSheet, entry.Skill, out string msg))
                        _statusMessage = msg;
                    else
                        _statusMessage = msg;
                }
                GUI.enabled = true;
            }

            GUILayout.EndHorizontal();
        }
    }
}

using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using Killtime.UI;
using Killtime.Story.Data;
using Killtime.Core.Character;
using Killtime.Core.Inventory;
using Killtime.Tactics;
using Killtime.Tactics.Grid;
using Killtime.Tactics.TurnSystem;
using Killtime.Audio;

namespace Killtime.Story
{
    public partial class ScenarioEditorDevWindow : FloatingWindow<ScenarioEditorDevWindow>
    {
        private static string SKey(object o, string tag)
        {
            return (o == null ? "null" : o.GetHashCode().ToString("X8")) + ":" + tag;
        }
        private static bool SectionOpen(HashSet<string> expanded, object o, string tag)
        {
            return expanded != null && o != null && expanded.Contains(SKey(o, tag));
        }
        private bool FoldoutButton(Rect worldRect, object o, string tag, string label)
        {
            string key = SKey(o, tag);
            bool open = _expandedSections.Contains(key);
            if (GraphButton(worldRect, (open ? "▾ " : "▸ ") + label))
            {
                if (open) _expandedSections.Remove(key);
                else _expandedSections.Add(key);
                open = !open;
            }
            return open;
        }
        private static int MeasureEffectsBlock(System.Collections.Generic.List<ScenarioEffect> list, bool open, bool alwaysHeader)
        {
            int n = list != null ? list.Count : 0;
            if (!alwaysHeader && !open) return 0;
            return 1 + (open ? n : 0);
        }
        private void DrawEffectsBlock(ref float y, float x, float w, System.Collections.Generic.List<ScenarioEffect> list, object owner, string tag, string label, bool alwaysHeader)
        {
            if (list == null) return;
            bool open = SectionOpen(_expandedSections, owner, tag);
            if (alwaysHeader || open)
            {
                FoldoutButton(new Rect(x, y, Mathf.Max(60f, w - 70f), 20f), owner, tag, $"{label} ({list.Count})");
                if (GraphButton(new Rect(x + w - 66f, y, 66f, 20f), "+ Effet"))
                    list.Add(new ScenarioEffect { Type = ScenarioEffectType.AddFlag, Key = "nouveau_flag" });
                y += 20f;
                open = SectionOpen(_expandedSections, owner, tag);
            }
            if (!open) return;
            for (int i = 0; i < list.Count; i++)
            {
                var eff = list[i];
                if (eff == null) { list.RemoveAt(i); i--; continue; }
                float ex = x;
                eff.Type = (ScenarioEffectType)GraphToolbar(new Rect(ex, y, 128f, 18f), (int)eff.Type, new[] { "Route", "Flag", "Int", "Journal" });
                ex += 130f;
                float tailW = 22f;
                switch (eff.Type)
                {
                    case ScenarioEffectType.SetRoute:
                        eff.Route = (StoryRoute)GraphToolbar(new Rect(ex, y, Mathf.Max(40f, x + w - ex - tailW), 18f), (int)eff.Route, new[] { "None", "Vardis", "Indep", "Imper" });
                        break;
                    case ScenarioEffectType.AddFlag:
                        GraphLabel(new Rect(ex, y, 34f, 18f), "Clé:");
                        ex += 36f;
                        eff.Key = GraphTextField(new Rect(ex, y, Mathf.Max(30f, x + w - ex - tailW), 18f), eff.Key ?? "");
                        break;
                    case ScenarioEffectType.AddInteger:
                        GraphLabel(new Rect(ex, y, 32f, 18f), "Clé:");
                        ex += 34f;
                        eff.Key = GraphTextField(new Rect(ex, y, 62f, 18f), eff.Key ?? "");
                        ex += 64f;
                        GraphLabel(new Rect(ex, y, 30f, 18f), "Qté:");
                        ex += 32f;
                        string amtTxt = GraphTextField(new Rect(ex, y, Mathf.Max(28f, x + w - ex - tailW), 18f), eff.Amount.ToString());
                        int.TryParse(amtTxt, out eff.Amount);
                        break;
                    case ScenarioEffectType.AddJournal:
                        GraphLabel(new Rect(ex, y, 32f, 18f), "Txt:");
                        ex += 34f;
                        eff.Text = GraphTextField(new Rect(ex, y, Mathf.Max(30f, x + w - ex - tailW), 18f), eff.Text ?? "");
                        break;
                }
                if (GraphButton(new Rect(x + w - 20f, y, 20f, 18f), "✕"))
                {
                    list.RemoveAt(i);
                    break;
                }
                y += 20f;
            }
        }
        private static int MeasurePrereqDetail(ScenePrerequisiteData pre, bool open)
        {
            if (!open || pre == null) return 0;
            if (!pre.HasPrerequisite) return 1;
            return 3 + (pre.Kind == PrerequisiteKind.None ? 0 : 1);
        }
        private void DrawPrereqDetail(ref float y, float x, float w, ScenePrerequisiteData pre)
        {
            if (pre == null) return;
            float px = x;
            pre.HasPrerequisite = GraphToggle(new Rect(px, y, 62f, 20f), pre.HasPrerequisite, "🔒");
            px += 64f;
            GraphLabel(new Rect(px, y, 42f, 20f), "Type:");
            px += 44f;
            pre.Kind = (PrerequisiteKind)GraphToolbar(new Rect(px, y, Mathf.Max(60f, x + w - px), 20f), (int)pre.Kind, new[] { "None", "Compét.", "Spécia.", "Carac", "Flag", "Item", "Route" });
            y += 20f;
            if (!pre.HasPrerequisite) return;
            if (pre.Kind != PrerequisiteKind.None)
            {
                float qx = x + 12f;
                float qw = w - 12f;
                switch (pre.Kind)
                {
                    case PrerequisiteKind.SkillTraining:
                    {
                        int skillCount = Enum.GetValues(typeof(SkillType)).Length;
                        if (GraphButton(new Rect(qx, y, 18f, 18f), "◀"))
                            pre.RequiredSkill = (SkillType)(((int)pre.RequiredSkill - 1 + skillCount) % skillCount);
                        qx += 20f;
                        GraphLabel(new Rect(qx, y, 108f, 18f), $"<b>{SkillDefinitions.GetDisplayName(pre.RequiredSkill)}</b>");
                        qx += 110f;
                        if (GraphButton(new Rect(qx, y, 18f, 18f), "▶"))
                            pre.RequiredSkill = (SkillType)(((int)pre.RequiredSkill + 1) % skillCount);
                        qx += 20f;
                        GraphLabel(new Rect(qx, y, 52f, 18f), "Palier:");
                        qx += 54f;
                        string palTxt = GraphTextField(new Rect(qx, y, Mathf.Max(28f, x + w - qx), 18f), pre.MinTrainingLevel.ToString());
                        int.TryParse(palTxt, out pre.MinTrainingLevel);
                        break;
                    }
                    case PrerequisiteKind.Specialization:
                        GraphLabel(new Rect(qx, y, 62f, 18f), "Spécia.:");
                        pre.SpecializationName = GraphTextField(new Rect(qx + 64f, y, Mathf.Max(40f, x + w - qx - 64f), 18f), pre.SpecializationName ?? "");
                        break;
                    case PrerequisiteKind.AttributeThreshold:
                        GraphLabel(new Rect(qx, y, 52f, 18f), "Attribut:");
                        pre.AttributeName = GraphTextField(new Rect(qx + 54f, y, 76f, 18f), pre.AttributeName ?? "");
                        GraphLabel(new Rect(qx + 132f, y, 40f, 18f), "Min:");
                        string attrTxt = GraphTextField(new Rect(qx + 174f, y, Mathf.Max(28f, x + w - qx - 174f), 18f), pre.MinAttributeValue.ToString());
                        int.TryParse(attrTxt, out pre.MinAttributeValue);
                        break;
                    case PrerequisiteKind.CampaignFlag:
                        GraphLabel(new Rect(qx, y, 36f, 18f), "Clé:");
                        pre.FlagKey = GraphTextField(new Rect(qx + 38f, y, 104f, 18f), pre.FlagKey ?? "");
                        pre.FlagMustBeSet = GraphToggle(new Rect(qx + 144f, y, Mathf.Max(60f, x + w - qx - 144f), 18f), pre.FlagMustBeSet, "Posé");
                        break;
                    case PrerequisiteKind.ItemInInventory:
                        GraphLabel(new Rect(qx, y, 52f, 18f), "Objet:");
                        pre.ItemName = GraphTextField(new Rect(qx + 54f, y, Mathf.Max(40f, x + w - qx - 54f), 18f), pre.ItemName ?? "");
                        break;
                    case PrerequisiteKind.StoryRouteRequired:
                        GraphLabel(new Rect(qx, y, 52f, 18f), "Route:");
                        pre.RequiredRoute = (StoryRoute)GraphToolbar(new Rect(qx + 54f, y, Mathf.Max(60f, x + w - qx - 54f), 18f), (int)pre.RequiredRoute, new[] { "None", "Vardis", "Indep", "Imper" });
                        break;
                }
                y += 20f;
            }
            GraphLabel(new Rect(x + 12f, y, 62f, 18f), "Acteur:");
            pre.SpecificActorId = GraphTextField(new Rect(x + 76f, y, Mathf.Max(40f, x + w - x - 76f), 18f), pre.SpecificActorId ?? "");
            y += 20f;
        }
        private static int MeasureAmbienceDetail(SceneAmbienceData amb, bool open)
        {
            if (!open || amb == null) return 0;
            return 2 + (amb.ChangeMusic ? 1 : 0);
        }
        private void DrawAmbienceDetail(ref float y, float x, float w, SceneAmbienceData amb)
        {
            if (amb == null) return;
            GraphLabel(new Rect(x, y, 40f, 18f), "Cue:");
            amb.SoundCueId = GraphTextField(new Rect(x + 42f, y, Mathf.Max(60f, x + w - x - 42f - 70f), 18f), amb.SoundCueId ?? "");
            // DÉ-HARDCODÉ : secousse retirée (cartes 🎬 à effet). Rangée conservée (Cue + Alarme).
            amb.TriggerAlarm = GraphToggle(new Rect(x + w - 66f, y, 66f, 18f), amb.TriggerAlarm, "Alarme");
            y += 20f;
            amb.ChangeMusic = GraphToggle(new Rect(x, y, 110f, 20f), amb.ChangeMusic, "Musique");
            if (amb.ChangeMusic)
            {
                amb.MusicMood = (Killtime.Audio.MusicMood)GraphToolbar(new Rect(x + 112f, y, Mathf.Max(60f, x + w - x - 112f), 20f), (int)amb.MusicMood, new[] { "Explore", "Combat", "Stealth", "Mystery" });
                y += 20f;
                GraphLabel(new Rect(x + 12f, y, 70f, 18f), "Intensité:");
                amb.MusicIntensity = (Killtime.Audio.MusicIntensity)GraphToolbar(new Rect(x + 84f, y, Mathf.Max(60f, x + w - x - 84f), 18f), (int)amb.MusicIntensity, new[] { "Calm", "Low", "Mid", "High" });
                y += 20f;
            }
            else y += 20f;
        }
        private static int MeasureRewardsBlock(SceneDialogueRewardData rew)
        {
            if (rew == null) return 0;
            int m = rew.AdditionalItems != null ? rew.AdditionalItems.Count : 0;
            return 2 + m;
        }
        private void DrawRewardsBlock(ref float y, float x, float w, SceneDialogueRewardData rew, SceneDialogueChoiceData pickerChoice)
        {
            if (rew == null) return;
            StorySceneData.EnsureRewardDefaults(rew);
            GraphLabel(new Rect(x, y, 36f, 18f), "+XP");
            string xpTxt = GraphTextField(new Rect(x + 38f, y, 34f, 18f), rew.EarnXP.ToString());
            int.TryParse(xpTxt, out rew.EarnXP);
            GraphLabel(new Rect(x + 74f, y, 34f, 18f), "+CE");
            string ceTxt = GraphTextField(new Rect(x + 108f, y, 36f, 18f), rew.EarnCredits.ToString());
            int.TryParse(ceTxt, out rew.EarnCredits);
            GraphLabel(new Rect(x + 146f, y, 34f, 18f), "Obj:");
            rew.CompletionObjectiveId = GraphTextField(new Rect(x + 182f, y, Mathf.Max(40f, x + w - x - 182f), 18f), rew.CompletionObjectiveId ?? "");
            y += 20f;
            float ix = x;
            GraphLabel(new Rect(ix, y, 44f, 18f), "Item:");
            ix += 46f;
            bool hasPicker = pickerChoice != null;
            float tailW = (hasPicker ? 26f : 0f) + 16f + 32f + 2f + 24f;
            rew.ItemRewardName = GraphTextField(new Rect(ix, y, Mathf.Max(40f, x + w - ix - tailW), 18f), rew.ItemRewardName ?? "");
            ix = x + w - tailW;
            if (hasPicker)
            {
                if (GraphButton(new Rect(ix, y, 24f, 18f), "🔍")) OpenItemPicker(pickerChoice);
                ix += 26f;
            }
            GraphLabel(new Rect(ix, y, 14f, 18f), "x");
            ix += 16f;
            string qTxt = GraphTextField(new Rect(ix, y, 30f, 18f), rew.ItemRewardQuantity.ToString());
            int.TryParse(qTxt, out rew.ItemRewardQuantity);
            if (rew.ItemRewardQuantity < 1) rew.ItemRewardQuantity = 1;
            ix += 32f;
            if (GraphButton(new Rect(ix, y, 22f, 18f), "+"))
                rew.AdditionalItems.Add(new SceneItemRewardEntry { ItemName = "", Quantity = 1 });
            y += 20f;
            for (int ri = 0; ri < rew.AdditionalItems.Count; ri++)
            {
                var entry = rew.AdditionalItems[ri];
                if (entry == null) { rew.AdditionalItems.RemoveAt(ri); ri--; continue; }
                float jx = x + 12f;
                GraphLabel(new Rect(jx, y, 30f, 18f), $"#{ri + 2}");
                jx += 32f;
                float jtail = (hasPicker ? 26f : 0f) + 16f + 34f + 2f + 22f;
                entry.ItemName = GraphTextField(new Rect(jx, y, Mathf.Max(30f, x + w - jx - jtail), 18f), entry.ItemName ?? "");
                jx = x + w - jtail;
                if (hasPicker)
                {
                    if (GraphButton(new Rect(jx, y, 24f, 18f), "🔍")) OpenItemPicker(pickerChoice, ri);
                    jx += 26f;
                }
                GraphLabel(new Rect(jx, y, 14f, 18f), "x");
                jx += 16f;
                string eqTxt = GraphTextField(new Rect(jx, y, 32f, 18f), entry.Quantity.ToString());
                int.TryParse(eqTxt, out entry.Quantity);
                if (entry.Quantity < 1) entry.Quantity = 1;
                jx += 34f;
                GUI.backgroundColor = new Color(1f, 0.3f, 0.3f);
                if (GraphButton(new Rect(jx, y, 20f, 18f), "✕"))
                {
                    rew.AdditionalItems.RemoveAt(ri);
                    GUI.backgroundColor = Color.white;
                    break;
                }
                GUI.backgroundColor = Color.white;
                y += 20f;
            }
        }
        private static int MeasureAutoDetail(SceneAutoSkillCheckData auto, bool open)
        {
            if (!open || auto == null) return 0;
            if (!auto.HasAutoCheck) return 1;
            int m = auto.SuccessRewards?.AdditionalItems != null ? auto.SuccessRewards.AdditionalItems.Count : 0;
            int s = auto.SuccessEffects != null ? auto.SuccessEffects.Count : 0;
            int f = auto.FailureEffects != null ? auto.FailureEffects.Count : 0;
            return 11 + m + s + f;
        }
        private void DrawAutoDetail(ref float y, float x, float w, SceneAutoSkillCheckData auto)
        {
            if (auto == null) return;
            auto.HasAutoCheck = GraphToggle(new Rect(x, y, Mathf.Max(120f, w), 20f), auto.HasAutoCheck, "🎲 Test auto");
            y += 20f;
            if (!auto.HasAutoCheck) return;
            auto.SuccessRewards ??= new SceneDialogueRewardData();
            StorySceneData.EnsureRewardDefaults(auto.SuccessRewards);
            auto.SuccessEffects ??= new System.Collections.Generic.List<ScenarioEffect>();
            auto.FailureEffects ??= new System.Collections.Generic.List<ScenarioEffect>();
            int skillCount = Enum.GetValues(typeof(SkillType)).Length;
            float sx = x + 12f;
            if (GraphButton(new Rect(sx, y, 18f, 18f), "◀"))
                auto.RequiredSkill = (SkillType)(((int)auto.RequiredSkill - 1 + skillCount) % skillCount);
            sx += 20f;
            GraphLabel(new Rect(sx, y, 96f, 18f), $"<b>{SkillDefinitions.GetDisplayName(auto.RequiredSkill)}</b>");
            sx += 98f;
            if (GraphButton(new Rect(sx, y, 18f, 18f), "▶"))
                auto.RequiredSkill = (SkillType)(((int)auto.RequiredSkill + 1) % skillCount);
            sx += 20f;
            GraphLabel(new Rect(sx, y, 26f, 18f), "SD:");
            sx += 28f;
            string sdTxt = GraphTextField(new Rect(sx, y, 30f, 18f), auto.TargetDC.ToString());
            int.TryParse(sdTxt, out auto.TargetDC);
            sx += 32f;
            GraphLabel(new Rect(sx, y, 32f, 18f), "Act:");
            sx += 34f;
            auto.SpecificActorId = GraphTextField(new Rect(sx, y, Mathf.Max(30f, x + w - sx), 18f), auto.SpecificActorId ?? "");
            y += 20f;
            GUI.color = new Color(0.35f, 1f, 0.55f);
            GraphLabel(new Rect(x + 12f, y, 20f, 18f), "✔");
            GUI.color = Color.white;
            auto.SuccessSpeech = GraphTextField(new Rect(x + 34f, y, Mathf.Max(40f, x + w - x - 34f), 18f), auto.SuccessSpeech ?? "");
            y += 20f;
            GraphLabel(new Rect(x + 34f, y, 42f, 18f), "✔ →");
            auto.SuccessNextLineId = GraphTextField(new Rect(x + 78f, y, Mathf.Max(40f, x + w - x - 78f), 18f), auto.SuccessNextLineId ?? "");
            y += 20f;
            DrawRewardsBlock(ref y, x + 12f, w - 12f, auto.SuccessRewards, null);
            DrawEffectsBlock(ref y, x + 12f, w - 12f, auto.SuccessEffects, auto, "sfx", "fx ✔", true);
            GUI.color = new Color(1f, 0.45f, 0.4f);
            GraphLabel(new Rect(x + 12f, y, 20f, 18f), "✕");
            GUI.color = Color.white;
            auto.FailureSpeech = GraphTextField(new Rect(x + 34f, y, Mathf.Max(40f, x + w - x - 34f), 18f), auto.FailureSpeech ?? "");
            y += 20f;
            GraphLabel(new Rect(x + 34f, y, 42f, 18f), "✕ →");
            auto.FailureNextLineId = GraphTextField(new Rect(x + 78f, y, Mathf.Max(40f, x + w - x - 78f), 18f), auto.FailureNextLineId ?? "");
            y += 20f;
            DrawEffectsBlock(ref y, x + 12f, w - 12f, auto.FailureEffects, auto, "ffx", "fx ✕", true);
        }
        private static int MeasureChallengeDetail(SceneDialogueChallengeData chal, bool open)
        {
            if (!open || chal == null || !chal.HasChallenge) return 0;
            int m = chal.SuccessRewards?.AdditionalItems != null ? chal.SuccessRewards.AdditionalItems.Count : 0;
            int s = chal.SuccessEffects != null ? chal.SuccessEffects.Count : 0;
            int f = chal.FailureEffects != null ? chal.FailureEffects.Count : 0;
            return 5 + m + s + f;
        }
        private void DrawChallengeDetail(ref float y, float x, float w, SceneDialogueChoiceData choice)
        {
            var chal = choice != null ? choice.Challenge : null;
            if (chal == null || !chal.HasChallenge) return;
            chal.SuccessRewards ??= new SceneDialogueRewardData();
            StorySceneData.EnsureRewardDefaults(chal.SuccessRewards);
            chal.SuccessEffects ??= new System.Collections.Generic.List<ScenarioEffect>();
            chal.FailureEffects ??= new System.Collections.Generic.List<ScenarioEffect>();
            DrawRewardsBlock(ref y, x, w, chal.SuccessRewards, choice);
            DrawEffectsBlock(ref y, x, w, chal.SuccessEffects, chal, "sfx", "fx ✔", true);
            DrawEffectsBlock(ref y, x, w, chal.FailureEffects, chal, "ffx", "fx ✕", true);
            chal.FailureTriggersCombat = GraphToggle(new Rect(x, y, Mathf.Max(150f, w), 20f), chal.FailureTriggersCombat, "✕→Combat immédiat");
            y += 20f;
        }

        private void DrawDialogueCard(SceneDialogueLineData line, int index, SceneNodeData node, Vector2 mouseWorld)
        {
            if (string.IsNullOrEmpty(line.LineId)) line.LineId = $"line_{index + 1}";
            if (line.Choices == null) line.Choices = new System.Collections.Generic.List<SceneDialogueChoiceData>();
            if (line.Ambience == null) line.Ambience = new SceneAmbienceData();
            if (line.Prerequisite == null) line.Prerequisite = new ScenePrerequisiteData();
            if (line.AutoSkillCheck == null) line.AutoSkillCheck = new SceneAutoSkillCheckData();

            Vector2 size = GetDialogueCardSize(line);
            float cardW = size.x;
            float cardH = size.y;

            Rect cardRect = new Rect(line.GraphPosX, line.GraphPosY, cardW, cardH);
            Rect headerRect = new Rect(cardRect.x, cardRect.y, cardW, 26f);
            Rect closeRect = new Rect(headerRect.xMax - 22f, headerRect.y + 3f, 18f, 18f);
            Rect dragHeaderRect = new Rect(headerRect.x, headerRect.y, headerRect.width - 26f, headerRect.height);
            Rect resizeGripRect = new Rect(cardRect.xMax - 14f, cardRect.yMax - 14f, 14f, 14f);

            Event evt = Event.current;
            string dragKey = $"dialogue_{index}_{line.LineId}";
            string resizeKey = $"resize_{dragKey}";
            int dragControlId = GUIUtility.GetControlID(FocusType.Passive);
            int resizeControlId = GUIUtility.GetControlID(FocusType.Passive);

            // Redimensionnement de la carte
            bool lateResizeLine = IsLateResizeFor(line);
            if (GraphPrimaryDown(mouseWorld, resizeGripRect) || lateResizeLine)
            {
                GUIUtility.hotControl = resizeControlId;
                _resizingCardKey = resizeKey;
                if (lateResizeLine) { _lateResizeTarget = null; GUIUtility.keyboardControl = 0; }
                evt.Use();
            }
            else if ((evt.type == EventType.MouseDrag || evt.type == EventType.MouseMove) && GUIUtility.hotControl == resizeControlId && _resizingCardKey == resizeKey)
            {
                line.CardWidth = Mathf.Clamp(mouseWorld.x - cardRect.x, 320f, 750f);
                line.CardHeight = Mathf.Clamp(mouseWorld.y - cardRect.y, 160f, 650f);
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == resizeControlId)
            {
                GUIUtility.hotControl = 0;
                _resizingCardKey = null;
                evt.Use();
            }

            // Déplacement de la carte
            bool lateDragLine = IsLateDragFor(line);
            if (GraphPrimaryDown(mouseWorld, dragHeaderRect) || lateDragLine)
            {
                GUIUtility.hotControl = dragControlId;
                _draggingCardKey = dragKey;
                _dragOffset = (lateDragLine ? _lateDownWorld : mouseWorld) - new Vector2(line.GraphPosX, line.GraphPosY);
                if (lateDragLine) { _lateDragTarget = null; GUIUtility.keyboardControl = 0; }
                evt.Use();
            }
            else if ((evt.type == EventType.MouseDrag || evt.type == EventType.MouseMove) && GUIUtility.hotControl == dragControlId && _draggingCardKey == dragKey)
            {
                line.GraphPosX = mouseWorld.x - _dragOffset.x;
                line.GraphPosY = mouseWorld.y - _dragOffset.y;
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == dragControlId)
            {
                GUIUtility.hotControl = 0;
                _draggingCardKey = null;
                evt.Use();
            }

            // 1. Rendu d'arrière-plan de la Carte et de l'En-tête
            DrawGraphSolidRect(cardRect, new Color(0.08f, 0.11f, 0.15f, 0.95f));
            int chalSDCount = 0;
            int chalVSCount = 0;
            for (int hc0 = 0; hc0 < line.Choices.Count; hc0++)
            {
                var hx = line.Choices[hc0];
                if (hx != null && hx.Challenge != null && hx.Challenge.HasChallenge)
                {
                    if (hx.Challenge.IsOpposed) chalVSCount++;
                    else chalSDCount++;
                }
            }
            Color headerCol = line.Choices.Count > 0 ? new Color(0.35f, 0.24f, 0.05f) : (line.AutoSkillCheck != null && line.AutoSkillCheck.HasAutoCheck ? new Color(0.25f, 0.12f, 0.35f) : new Color(0.08f, 0.28f, 0.36f));
            if (chalVSCount > 0) headerCol = new Color(0.42f, 0.24f, 0.08f);
            DrawGraphSolidRect(headerRect, headerCol);

            // Bordure de carte nette
            DrawGraphSolidRect(new Rect(cardRect.x, cardRect.y, cardRect.width, 1f), headerCol * 1.4f);
            DrawGraphSolidRect(new Rect(cardRect.x, cardRect.yMax - 1f, cardRect.width, 1f), new Color(1f, 1f, 1f, 0.08f));
            DrawGraphSolidRect(new Rect(cardRect.x, cardRect.y, 1f, cardRect.height), new Color(1f, 1f, 1f, 0.08f));
            DrawGraphSolidRect(new Rect(cardRect.xMax - 1f, cardRect.y, 1f, cardRect.height), new Color(1f, 1f, 1f, 0.08f));

            // 2. Port d'Entrée : vert + gros si point d'entrée, cadre rouge si
            // plusieurs entrées (erreur utilisateur).
            DrawEntryInputPort(cardRect, Color.cyan,
                !DialogueIdHasIncoming(node, line.LineId),
                CountDialogueEntries(node) > 1);

            // 3. Titre & Locuteur
            string chalBadge = chalVSCount > 0 ? $" ⚔️x{chalVSCount}" : (chalSDCount > 0 ? $" 🎲x{chalSDCount}" : "");
            string cineBadge = (line.CinematicIds != null && line.CinematicIds.Count > 0) ? $" 🎬×{line.CinematicIds.Count}" : "";
            string headerSpeaker = line.IsNarration ? "📜 NARRATION" : $"◆ {line.SpeakerId}";
            GraphLabel(new Rect(headerRect.x + 8f, headerRect.y + 3f, cardW - 35f, 20f), $"<b>{headerSpeaker}</b> (<i>{line.LineId}</i>){chalBadge}{cineBadge}");

            // 4. Bouton Supprimer
            GUI.backgroundColor = new Color(0.85f, 0.22f, 0.22f, 1f);
            GUI.color = Color.white;
            if (GraphButton(closeRect, "✕"))
            {
                node.Dialogues.RemoveAt(index);
                PurgeSoundReferences(line != null ? line.LineId : "");
                if (_draggingCardKey == dragKey)
                {
                    _draggingCardKey = null;
                    GUIUtility.hotControl = 0;
                }
                GUI.backgroundColor = Color.white;
                evt.Use();
                return;
            }
            GUI.backgroundColor = Color.white;

            // 5. Poignée de redimensionnement
            GUI.color = new Color(1f, 1f, 1f, 0.4f);
            GraphLabel(resizeGripRect, "◢");
            GUI.color = Color.white;

            // Survol du header : mémorise une infobulle lisible (dessinée après la fermeture du clip).
            if (_zoom < 0.85f && !_isPanning && _draggingCardKey == null && _resizingCardKey == null && !_wireDraft.IsActive
                && headerRect.Contains(mouseWorld))
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine(line.Speech ?? "");
                if (line.Choices.Count > 0)
                {
                    sb.AppendLine($"✦ {line.Choices.Count} choix :");
                    for (int hc = 0; hc < Mathf.Min(4, line.Choices.Count); hc++)
                    {
                        var hch = line.Choices[hc];
                        if (hch == null) continue;
                        string hl = hch.Label ?? "";
                        if (hl.Length > 90) hl = hl.Substring(0, 90) + "…";
                        string tag = "";
                        if (hch.Challenge != null && hch.Challenge.HasChallenge)
                        {
                            tag = hch.Challenge.IsOpposed ? $" [⚔️ {hch.Challenge.GetShortLabel()}]" : $" [🎲 {hch.Challenge.GetShortLabel()}]";
                            if (!string.IsNullOrEmpty(hch.Challenge.SuccessConsequenceId)) tag += $" ✔→🎁{hch.Challenge.SuccessConsequenceId}";
                            if (!string.IsNullOrEmpty(hch.Challenge.FailureConsequenceId)) tag += $" ✕→🎁{hch.Challenge.FailureConsequenceId}";
                            var hrw = hch.Challenge.SuccessRewards;
                            if (hrw != null)
                            {
                                if (hrw.EarnXP > 0) tag += $" +{hrw.EarnXP}XP";
                                if (hrw.EarnCredits > 0) tag += $" +{hrw.EarnCredits}CE";
                                var itemBits = new System.Collections.Generic.List<string>();
                                if (!string.IsNullOrEmpty(hrw.ItemRewardName))
                                    itemBits.Add(hrw.ItemRewardQuantity > 1 ? $"{hrw.ItemRewardName} x{hrw.ItemRewardQuantity}" : hrw.ItemRewardName);
                                if (hrw.AdditionalItems != null)
                                {
                                    for (int hi = 0; hi < hrw.AdditionalItems.Count; hi++)
                                    {
                                        var he = hrw.AdditionalItems[hi];
                                        if (he == null || string.IsNullOrEmpty(he.ItemName)) continue;
                                        itemBits.Add(he.Quantity > 1 ? $"{he.ItemName} x{he.Quantity}" : he.ItemName);
                                    }
                                }
                                if (itemBits.Count > 0) tag += " [" + string.Join(", ", itemBits) + "]";
                            }
                            if (hch.Challenge.FailureTriggersCombat) tag += " ✕⚔";
                        }
                        if (hch.Prerequisite != null && hch.Prerequisite.HasPrerequisite) tag += $" 🔒{hch.Prerequisite.GetSummary()}";
                        sb.AppendLine($"• {hl}{tag}");
                    }
                }
                else if (!string.IsNullOrEmpty(line.NextLineId))
                {
                    sb.AppendLine($"➔ {line.NextLineId}");
                }
                if (line.Prerequisite != null && line.Prerequisite.HasPrerequisite)
                    sb.AppendLine($"🔒 Prérequis : {line.Prerequisite.GetSummary()}");
                if (line.AutoSkillCheck != null && line.AutoSkillCheck.HasAutoCheck)
                    sb.AppendLine($"🎲 Auto : {SkillDefinitions.GetDisplayName(line.AutoSkillCheck.RequiredSkill)} SD {line.AutoSkillCheck.TargetDC}");
                if (line.Ambience != null && line.Ambience.HasAmbience && !string.IsNullOrEmpty(line.Ambience.SoundCueId))
                    sb.AppendLine($"🔊 {line.Ambience.SoundCueId}");
                _hasHoverCard = true;
                _hoverTitle = line.IsNarration ? $"📜 NARRATION  ({line.LineId})" : $"◆ {line.SpeakerId}  ({line.LineId})";
                _hoverBody = sb.ToString().Trim();
                _hoverScreenPos = _graphMouseScreenPos;
            }

            float innerX = cardRect.x + 8f;
            float innerW = cardW - 16f;
            var cardChoiceOffsets = new List<float>();

            if (_zoom < CardLodThreshold)
            {
                // LOD rapide : silhouettes sans contrôles IMGUI
                DrawGraphSolidRect(new Rect(innerX, cardRect.y + 32f, innerW * 0.9f, 8f), new Color(1f, 1f, 1f, 0.09f));
                DrawGraphSolidRect(new Rect(innerX, cardRect.y + 44f, innerW * 0.65f, 6f), new Color(1f, 1f, 1f, 0.06f));
                if (line.Choices.Count > 0)
                {
                    GetDialogueChoiceRowOffsets(line, cardChoiceOffsets);
                    for (int c = 0; c < line.Choices.Count; c++)
                    {
                        float rowTop = (c < cardChoiceOffsets.Count) ? cardChoiceOffsets[c] : (100f + c * 22f);
                        DrawGraphSolidRect(new Rect(innerX, cardRect.y + rowTop + 4f, innerW - 14f, 8f), new Color(1f, 0.8f, 0.25f, 0.14f));
                    }
                }
            }
            else
            {
                // 6. Contenu ÉDITABLE en GUI ABSOLU (zoom lisible)
                float y = cardRect.y + 30f;

                GraphLabel(new Rect(innerX, y, 22f, 18f), "ID:");
                line.LineId = GraphTextField(new Rect(innerX + 24f, y, 64f, 18f), line.LineId ?? "");
                line.IsNarration = GraphToggle(new Rect(innerX + 92f, y, 58f, 18f), line.IsNarration, "📜 Narr");

                if (!line.IsNarration)
                {
                    GraphLabel(new Rect(innerX + 154f, y, 26f, 18f), "Loc:");
                    line.SpeakerId = GraphTextField(new Rect(innerX + 182f, y, 56f, 18f), line.SpeakerId ?? "");
                    Rect dropBtn = new Rect(innerX + 240f, y, 18f, 18f);
                    string dropKey = $"dlg_{line.LineId}";
                    if (GraphButton(dropBtn, "▼"))
                    {
                        if (_actorDropKey == dropKey) _actorDropKey = null;
                        else OpenActorDropdown(dropKey, dropBtn, line.SpeakerId ?? "", v => line.SpeakerId = v);
                    }
                    line.StageDirection = GraphTextField(new Rect(innerX + 260f, y, Mathf.Max(30f, innerW - 260f), 18f), line.StageDirection ?? "");
                }
                else
                {
                    line.SpeakerId = "";
                    GraphLabel(new Rect(innerX + 154f, y, 32f, 18f), "Dida:");
                    line.StageDirection = GraphTextField(new Rect(innerX + 188f, y, Mathf.Max(40f, innerW - 188f), 18f), line.StageDirection ?? "");
                }
                y += 20f;

                line.Speech = GraphTextArea(new Rect(innerX, y, innerW, 48f), line.Speech ?? "");
                y += 50f;

                // Choix Multiples / Liaison : première rangée à cardY+100, pas vertical de 22 + 20/ligne de défi (référence des wires).
                GetDialogueChoiceRowOffsets(line, cardChoiceOffsets);
                if (line.Choices.Count > 0)
                {
                    int skillCount = Enum.GetValues(typeof(SkillType)).Length;
                    for (int c = 0; c < line.Choices.Count; c++)
                    {
                        var ch = line.Choices[c];
                        if (ch == null) { y += 22f; continue; }
                        ch.Challenge ??= new SceneDialogueChallengeData();
                        ch.Prerequisite ??= new ScenePrerequisiteData();
                        ch.Effects ??= new System.Collections.Generic.List<ScenarioEffect>();
                        var chal = ch.Challenge;

                        // Rangée principale : [mode 30][label][➔][next 70][⋯][⊘].
                        string modeLabel = !chal.HasChallenge ? "–" : (chal.IsOpposed ? "VS" : "SD");
                        Color prevBg = GUI.backgroundColor;
                        GUI.backgroundColor = !chal.HasChallenge ? new Color(0.35f, 0.38f, 0.42f)
                            : (chal.IsOpposed ? new Color(1.0f, 0.55f, 0.2f) : new Color(1.0f, 0.78f, 0.25f));
                        if (GraphButton(new Rect(innerX, y, 30f, 20f), modeLabel))
                        {
                            if (!chal.HasChallenge) { chal.HasChallenge = true; chal.IsOpposed = false; if (chal.TargetDC <= 0) chal.TargetDC = 10; }
                            else if (!chal.IsOpposed) { chal.IsOpposed = true; }
                            else { chal.HasChallenge = false; chal.IsOpposed = false; }
                        }
                        GUI.backgroundColor = prevBg;

                        float labelW = Mathf.Max(40f, innerW - 32f - 144f);
                        ch.Label = GraphTextField(new Rect(innerX + 32f, y, labelW, 20f), ch.Label ?? "");
                        GraphLabel(new Rect(innerX + 32f + labelW + 2f, y, 16f, 20f), "➔");
                        ch.NextLineId = GraphTextField(new Rect(innerX + 32f + labelW + 20f, y, 70f, 20f), ch.NextLineId ?? "");
                        bool detAny = SectionOpen(_expandedSections, ch, "pre") || SectionOpen(_expandedSections, ch, "fx") || SectionOpen(_expandedSections, ch, "chd");
                        if (GraphButton(new Rect(innerX + 32f + labelW + 92f, y, 20f, 20f), detAny ? "▾" : "▸"))
                        {
                            _expandedSections.Remove(SKey(ch, "pre"));
                            _expandedSections.Remove(SKey(ch, "fx"));
                            _expandedSections.Remove(SKey(ch, "chd"));
                            if (!detAny)
                            {
                                _expandedSections.Add(SKey(ch, "pre"));
                                _expandedSections.Add(SKey(ch, "fx"));
                                _expandedSections.Add(SKey(ch, "chd"));
                            }
                        }
                        if (GraphButton(new Rect(innerX + 32f + labelW + 114f, y, 20f, 20f), "⊘")) ch.NextLineId = "";
                        y += 22f;

                        if (chal.HasChallenge && !chal.IsOpposed)
                        {
                            // Sous-rangée test SD : [<][compétence][>][SD][val][Acteur].
                            float sx = innerX + 12f;
                            if (GraphButton(new Rect(sx, y, 18f, 18f), "◀"))
                            {
                                int si = ((int)chal.RequiredSkill - 1 + skillCount) % skillCount;
                                chal.RequiredSkill = (SkillType)si;
                            }
                            sx += 20f;
                            GraphLabel(new Rect(sx, y, 95f, 18f), $"<b>{SkillDefinitions.GetDisplayName(chal.RequiredSkill)}</b>");
                            sx += 97f;
                            if (GraphButton(new Rect(sx, y, 18f, 18f), "▶"))
                            {
                                int si = ((int)chal.RequiredSkill + 1) % skillCount;
                                chal.RequiredSkill = (SkillType)si;
                            }
                            sx += 20f;
                            GraphLabel(new Rect(sx, y, 24f, 18f), "SD:");
                            sx += 26f;
                            string sdTxt = GraphTextField(new Rect(sx, y, 30f, 18f), chal.TargetDC.ToString());
                            int.TryParse(sdTxt, out chal.TargetDC);
                            sx += 32f;
                            GraphLabel(new Rect(sx, y, 28f, 18f), "Act:");
                            sx += 30f;
                            chal.SpecificActorId = GraphTextField(new Rect(sx, y, Mathf.Max(30f, innerX + innerW - sx), 18f), chal.SpecificActorId ?? "");
                            y += 20f;
                        }
                        else if (chal.HasChallenge && chal.IsOpposed)
                        {
                            // Sous-rangée 1 : compétence joueur vs compétence adverse.
                            float sx = innerX + 12f;
                            GraphLabel(new Rect(sx, y, 18f, 18f), "J:");
                            sx += 20f;
                            if (GraphButton(new Rect(sx, y, 18f, 18f), "◀"))
                            {
                                int si = ((int)chal.RequiredSkill - 1 + skillCount) % skillCount;
                                chal.RequiredSkill = (SkillType)si;
                            }
                            sx += 20f;
                            GraphLabel(new Rect(sx, y, 72f, 18f), $"<b>{SkillDefinitions.GetDisplayName(chal.RequiredSkill)}</b>");
                            sx += 74f;
                            if (GraphButton(new Rect(sx, y, 18f, 18f), "▶"))
                            {
                                int si = ((int)chal.RequiredSkill + 1) % skillCount;
                                chal.RequiredSkill = (SkillType)si;
                            }
                            sx += 20f;
                            GraphLabel(new Rect(sx, y, 22f, 18f), "vs");
                            sx += 24f;
                            if (GraphButton(new Rect(sx, y, 18f, 18f), "◀"))
                            {
                                int oi = ((int)chal.OpposedSkill - 1 + skillCount) % skillCount;
                                chal.OpposedSkill = (SkillType)oi;
                            }
                            sx += 20f;
                            GraphLabel(new Rect(sx, y, 72f, 18f), $"<b>{SkillDefinitions.GetDisplayName(chal.OpposedSkill)}</b>");
                            sx += 74f;
                            if (GraphButton(new Rect(sx, y, 18f, 18f), "▶"))
                            {
                                int oi = ((int)chal.OpposedSkill + 1) % skillCount;
                                chal.OpposedSkill = (SkillType)oi;
                            }
                            y += 20f;

                            // Sous-rangée 2 : opposant + bonus + SD de repli (total fixe si opposant introuvable).
                            float qx = innerX + 12f;
                            GraphLabel(new Rect(qx, y, 32f, 18f), "Adv:");
                            qx += 34f;
                            chal.OpposedActorId = GraphTextField(new Rect(qx, y, 80f, 18f), chal.OpposedActorId ?? "");
                            qx += 82f;
                            GraphLabel(new Rect(qx, y, 26f, 18f), "+B:");
                            qx += 28f;
                            string bTxt = GraphTextField(new Rect(qx, y, 30f, 18f), chal.OpposedBonus.ToString());
                            int.TryParse(bTxt, out chal.OpposedBonus);
                            qx += 32f;
                            GraphLabel(new Rect(qx, y, 44f, 18f), "SD fix:");
                            qx += 46f;
                            string fixTxt = GraphTextField(new Rect(qx, y, 30f, 18f), chal.TargetDC.ToString());
                            int.TryParse(fixTxt, out chal.TargetDC);
                            y += 20f;
                        }

                        if (chal.HasChallenge)
                        {
                            chal.SuccessEffects ??= new System.Collections.Generic.List<ScenarioEffect>();
                            chal.FailureEffects ??= new System.Collections.Generic.List<ScenarioEffect>();

                            // Sortie succès : vers carte Conséquence (prioritaire) + lien direct vers réplique.
                            {
                                float rx = innerX + 12f;
                                GUI.color = new Color(0.35f, 1f, 0.55f);
                                GraphLabel(new Rect(rx, y, 18f, 18f), "✔");
                                GUI.color = Color.white;
                                rx += 20f;
                                GraphLabel(new Rect(rx, y, 52f, 18f), "→Cons:");
                                rx += 54f;
                                chal.SuccessConsequenceId = GraphTextField(new Rect(rx, y, 84f, 18f), chal.SuccessConsequenceId ?? "");
                                rx += 86f;
                                GraphLabel(new Rect(rx, y, 14f, 18f), "→");
                                rx += 16f;
                                chal.SuccessNextLineId = GraphTextField(new Rect(rx, y, Mathf.Max(30f, innerX + innerW - rx), 18f), chal.SuccessNextLineId ?? "");
                                y += 20f;
                            }

                            // Sortie échec : vers carte Conséquence (prioritaire) + lien direct vers réplique.
                            {
                                float fx2 = innerX + 12f;
                                GUI.color = new Color(1f, 0.45f, 0.4f);
                                GraphLabel(new Rect(fx2, y, 18f, 18f), "✕");
                                GUI.color = Color.white;
                                fx2 += 20f;
                                GraphLabel(new Rect(fx2, y, 52f, 18f), "→Cons:");
                                fx2 += 54f;
                                chal.FailureConsequenceId = GraphTextField(new Rect(fx2, y, 84f, 18f), chal.FailureConsequenceId ?? "");
                                fx2 += 86f;
                                GraphLabel(new Rect(fx2, y, 14f, 18f), "→");
                                fx2 += 16f;
                                chal.FailureNextLineId = GraphTextField(new Rect(fx2, y, Mathf.Max(30f, innerX + innerW - fx2), 18f), chal.FailureNextLineId ?? "");
                                y += 20f;
                            }
                        }

                        // Méta du choix : prérequis et effets (résumé).
                        {
                            bool hasPre = ch.Prerequisite != null && ch.Prerequisite.HasPrerequisite;
                            int fxCount = ch.Effects != null ? ch.Effects.Count : 0;
                            int sfx = (chal.HasChallenge && chal.SuccessEffects != null) ? chal.SuccessEffects.Count : 0;
                            int ffx = (chal.HasChallenge && chal.FailureEffects != null) ? chal.FailureEffects.Count : 0;
                            if (hasPre || fxCount > 0 || sfx + ffx > 0)
                            {
                                string meta = "";
                                if (hasPre) meta += $"🔒 {ch.Prerequisite.GetSummary()}  ";
                                if (fxCount > 0) meta += $" +fx:{fxCount}";
                                if (sfx + ffx > 0) meta += $" 🎲fx:{sfx}/{ffx}";
                                GUI.color = new Color(1f, 1f, 1f, 0.55f);
                                GraphLabel(new Rect(innerX + 12f, y, Mathf.Max(40f, innerW - 12f), 18f), meta);
                                GUI.color = Color.white;
                                y += 20f;
                            }
                        }

                        // Détails repliables du choix (ex-inspecteur) : prérequis / effets / défi.
                        if (SectionOpen(_expandedSections, ch, "pre"))
                            DrawPrereqDetail(ref y, innerX + 12f, innerW - 12f, ch.Prerequisite);
                        DrawEffectsBlock(ref y, innerX + 12f, innerW - 12f, ch.Effects, ch, "fx", "+fx", false);
                        if (chal.HasChallenge && SectionOpen(_expandedSections, ch, "chd"))
                            DrawChallengeDetail(ref y, innerX + 12f, innerW - 12f, ch);
                    }
                }
                else
                {
                    GraphLabel(new Rect(innerX, y, 65f, 20f), "Liaison ➔ :");
                    line.NextLineId = GraphTextField(new Rect(innerX + 67f, y, Mathf.Max(60f, innerW - 67f - 26f), 20f), line.NextLineId ?? "");
                    if (GraphButton(new Rect(innerX + innerW - 22f, y, 22f, 20f), "⊘")) line.NextLineId = "";
                    y += 22f;
                }

                // Résumés de la réplique (rien de caché : prérequis / test auto / ambiance).
                if (line.Prerequisite != null && line.Prerequisite.HasPrerequisite)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.6f);
                    GraphLabel(new Rect(innerX + 12f, y, Mathf.Max(40f, innerW - 12f), 18f), $"🔒 Prérequis : {line.Prerequisite.GetSummary()}");
                    GUI.color = Color.white;
                    y += 18f;
                }
                if (line.AutoSkillCheck != null && line.AutoSkillCheck.HasAutoCheck)
                {
                    var auto = line.AutoSkillCheck;
                    GUI.color = new Color(0.7f, 0.85f, 1f, 0.65f);
                    GraphLabel(new Rect(innerX + 12f, y, Mathf.Max(40f, innerW - 12f), 18f),
                        $"🎲 Auto : {SkillDefinitions.GetDisplayName(auto.RequiredSkill)} SD {auto.TargetDC} ✔→{auto.SuccessNextLineId} ✕→{auto.FailureNextLineId}");
                    GUI.color = Color.white;
                    y += 18f;
                }
                if (line.Ambience != null && line.Ambience.HasAmbience)
                {
                    var amb = line.Ambience;
                    GUI.color = new Color(1f, 1f, 1f, 0.6f);
                    GraphLabel(new Rect(innerX + 12f, y, Mathf.Max(40f, innerW - 12f), 18f),
                        $"🔊 Ambiance : {amb.SoundCueId}{(amb.ChangeMusic ? $" ♪{amb.MusicMood}/{amb.MusicIntensity}" : "")}{(amb.TriggerAlarm ? " 🚨" : "")}");
                    GUI.color = Color.white;
                    y += 18f;
                }

                // Strip détails (ex-inspecteur) : 🔒 prérequis, 🔊 ambiance, 🎲 test auto.
                {
                    float stripX = innerX;
                    bool preOpen = FoldoutButton(new Rect(stripX, y, 46f, 20f), line, "pre", "🔒"); stripX += 48f;
                    bool ambOpen = FoldoutButton(new Rect(stripX, y, 46f, 20f), line, "amb", "🔊"); stripX += 48f;
                    bool autoOpen = FoldoutButton(new Rect(stripX, y, 46f, 20f), line, "auto", "🎲");
                    y += 20f;
                    if (preOpen) DrawPrereqDetail(ref y, innerX + 12f, innerW - 12f, line.Prerequisite);
                    if (ambOpen) DrawAmbienceDetail(ref y, innerX + 12f, innerW - 12f, line.Ambience);
                    if (autoOpen) DrawAutoDetail(ref y, innerX + 12f, innerW - 12f, line.AutoSkillCheck);
                }

                // Sortie cinématique 🎬 : fire-and-forget, ne bloque pas la carte suivante.
                DrawCineLinkRow(ref y, innerX, innerW, line.CinematicIds);
                DrawSoundLinkRow(ref y, innerX, innerW, line.LineId);

                // Rangée basse : +Choix / toggles (protégée du débordement par CardHeight auto).
                if (y + 20f <= cardRect.yMax - 4f)
                {
                    if (GraphButton(new Rect(innerX, y, 64f, 18f), "+ Choix"))
                    {
                        line.Choices.Add(new SceneDialogueChoiceData { ChoiceId = $"ch_{line.Choices.Count + 1}", Label = "Option..." });
                    }
                    line.Ambience.HasAmbience = GraphToggle(new Rect(innerX + 68f, y, 90f, 18f), line.Ambience.HasAmbience, "Ambiance");
                    line.Prerequisite.HasPrerequisite = GraphToggle(new Rect(innerX + 160f, y, Mathf.Max(60f, innerW - 160f), 18f), line.Prerequisite.HasPrerequisite, "Prérequis");
                }
                else if (GraphButton(new Rect(innerX, cardRect.yMax - 22f, 64f, 18f), "+ Choix"))
                {
                    line.Choices.Add(new SceneDialogueChoiceData { ChoiceId = $"ch_{line.Choices.Count + 1}", Label = "Option..." });
                }
            }

            // Port de Sortie Principal : rouge + gros si point de sortie (sans liaison).
            bool dlgIsExit = !DialogueHasOutgoing(line);
            Rect outPort = dlgIsExit
                ? new Rect(cardRect.xMax - 10f, cardRect.y + 16f, 20f, 20f)
                : new Rect(cardRect.xMax - 8f, cardRect.y + 18f, 16f, 16f);
            DrawGraphSolidRect(outPort, dlgIsExit ? ExitRed : new Color(0f, 0.85f, 1f));

            if (GraphPrimaryDown(mouseWorld, outPort))
            {
                _wireDraft = new WireConnectionDraft
                {
                    IsActive = true,
                    FromChoice = false,
                    SourceLine = line,
                    StartPos = outPort.center
                };
                evt.Use();
            }

            // Port de sortie cinématique 🎬 : flanc droit, rangée dédiée (magenta).
            DrawCineOutPort(new Vector2(cardRect.xMax, cardRect.y + CinePortTopCards), line.CinematicIds, mouseWorld);
            DrawSoundOutPort(new Vector2(cardRect.x + cardRect.width * 0.5f, cardRect.yMax), line.LineId, mouseWorld);

            // Ports de Sortie des Choix (alignés sur les rangées GUI : offsets réels avec défis).
            GetDialogueChoiceRowOffsets(line, cardChoiceOffsets);
            for (int c = 0; c < line.Choices.Count; c++)
            {
                var ch = line.Choices[c];
                float rowTop = (c >= 0 && c < cardChoiceOffsets.Count) ? cardChoiceOffsets[c] : (100f + c * 22f);
                Rect chOutPort = new Rect(cardRect.xMax - 7f, cardRect.y + rowTop + 3f, 14f, 14f);
                var chal = ch != null ? ch.Challenge : null;
                bool isChal = chal != null && chal.HasChallenge;
                bool isVs = isChal && chal.IsOpposed;
                DrawGraphSolidRect(chOutPort, isVs ? new Color(1f, 0.55f, 0.2f) : (isChal ? new Color(1f, 0.85f, 0.3f) : new Color(1f, 0.8f, 0.2f)));

                if (ch != null && GraphPrimaryDown(mouseWorld, chOutPort))
                {
                    _wireDraft = new WireConnectionDraft
                    {
                        IsActive = true,
                        FromChoice = true,
                        SourceLine = line,
                        SourceChoice = ch,
                        StartPos = chOutPort.center
                    };
                    evt.Use();
                }

                // Sorties du défi : ✔ (succès → conséquence) et ✕ (échec → conséquence).
                if (isChal)
                {
                    float linkTop = rowTop + 22f + GetChallengeConfigRows(chal) * 20f;
                    Rect okPort = new Rect(cardRect.xMax - 7f, cardRect.y + linkTop + 3f, 14f, 14f);
                    DrawGraphSolidRect(okPort, new Color(0.35f, 1f, 0.55f));
                    if (ch != null && GraphPrimaryDown(mouseWorld, okPort))
                    {
                        _wireDraft = new WireConnectionDraft
                        {
                            IsActive = true,
                            FromChallengeLink = true,
                            ChallengeLinkIsSuccess = true,
                            SourceChallenge = chal,
                            StartPos = okPort.center
                        };
                        evt.Use();
                    }
                    Rect koPort = new Rect(cardRect.xMax - 7f, cardRect.y + linkTop + 23f, 14f, 14f);
                    DrawGraphSolidRect(koPort, new Color(1f, 0.4f, 0.35f));
                    if (ch != null && GraphPrimaryDown(mouseWorld, koPort))
                    {
                        _wireDraft = new WireConnectionDraft
                        {
                            IsActive = true,
                            FromChallengeLink = true,
                            ChallengeLinkIsSuccess = false,
                            SourceChallenge = chal,
                            StartPos = koPort.center
                        };
                        evt.Use();
                    }
                }
            }
        }

        private void DrawEventCard(SceneEventData ev, int index, SceneNodeData node, Vector2 mouseWorld)
        {
            if (string.IsNullOrEmpty(ev.EventId)) ev.EventId = $"evt_{index + 1}";

            Vector2 size = GetEventCardSize(ev);
            float cardW = size.x;
            float cardH = size.y;

            Rect cardRect = new Rect(ev.GraphPosX, ev.GraphPosY, cardW, cardH);
            Rect headerRect = new Rect(cardRect.x, cardRect.y, cardW, 26f);
            Rect closeRect = new Rect(headerRect.xMax - 22f, headerRect.y + 3f, 18f, 18f);
            Rect dragHeaderRect = new Rect(headerRect.x, headerRect.y, headerRect.width - 26f, headerRect.height);
            Rect resizeGripRect = new Rect(cardRect.xMax - 14f, cardRect.yMax - 14f, 14f, 14f);

            Event evt = Event.current;
            string dragKey = $"event_{index}_{ev.EventId}";
            string resizeKey = $"resize_{dragKey}";
            int dragControlId = GUIUtility.GetControlID(FocusType.Passive);
            int resizeControlId = GUIUtility.GetControlID(FocusType.Passive);

            // Redimensionnement de la carte d'événement
            bool lateResizeEvent = IsLateResizeFor(ev);
            if (GraphPrimaryDown(mouseWorld, resizeGripRect) || lateResizeEvent)
            {
                GUIUtility.hotControl = resizeControlId;
                _resizingCardKey = resizeKey;
                if (lateResizeEvent) { _lateResizeTarget = null; GUIUtility.keyboardControl = 0; }
                evt.Use();
            }
            else if ((evt.type == EventType.MouseDrag || evt.type == EventType.MouseMove) && GUIUtility.hotControl == resizeControlId && _resizingCardKey == resizeKey)
            {
                ev.CardWidth = Mathf.Clamp(mouseWorld.x - cardRect.x, 280f, 750f);
                ev.CardHeight = Mathf.Clamp(mouseWorld.y - cardRect.y, 120f, 500f);
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == resizeControlId)
            {
                GUIUtility.hotControl = 0;
                _resizingCardKey = null;
                evt.Use();
            }

            // Déplacement de la carte d'événement
            bool lateDragEvent = IsLateDragFor(ev);
            if (GraphPrimaryDown(mouseWorld, dragHeaderRect) || lateDragEvent)
            {
                GUIUtility.hotControl = dragControlId;
                _draggingCardKey = dragKey;
                _dragOffset = (lateDragEvent ? _lateDownWorld : mouseWorld) - new Vector2(ev.GraphPosX, ev.GraphPosY);
                if (lateDragEvent) { _lateDragTarget = null; GUIUtility.keyboardControl = 0; }
                evt.Use();
            }
            else if ((evt.type == EventType.MouseDrag || evt.type == EventType.MouseMove) && GUIUtility.hotControl == dragControlId && _draggingCardKey == dragKey)
            {
                ev.GraphPosX = mouseWorld.x - _dragOffset.x;
                ev.GraphPosY = mouseWorld.y - _dragOffset.y;
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == dragControlId)
            {
                GUIUtility.hotControl = 0;
                _draggingCardKey = null;
                evt.Use();
            }

            // 1. Rendu d'arrière-plan de la Carte et de l'En-tête
            DrawGraphSolidRect(cardRect, new Color(0.12f, 0.08f, 0.12f, 0.95f));
            DrawGraphSolidRect(headerRect, new Color(0.40f, 0.15f, 0.15f));

            // Bordures
            DrawGraphSolidRect(new Rect(cardRect.x, cardRect.y, cardRect.width, 1f), new Color(0.85f, 0.3f, 0.3f, 0.8f));
            DrawGraphSolidRect(new Rect(cardRect.x, cardRect.yMax - 1f, cardRect.width, 1f), new Color(1f, 1f, 1f, 0.08f));
            DrawGraphSolidRect(new Rect(cardRect.x, cardRect.y, 1f, cardRect.height), new Color(1f, 1f, 1f, 0.08f));
            DrawGraphSolidRect(new Rect(cardRect.xMax - 1f, cardRect.y, 1f, cardRect.height), new Color(1f, 1f, 1f, 0.08f));

            // 2. Port d'Entrée : vert + gros si point d'entrée (sans lien entrant).
            DrawEntryInputPort(cardRect, new Color(0.85f, 0.4f, 1f),
                !EventIdHasIncoming(node, ev.EventId), false);

            // 3. Titre & Nature de l'Événement
            string evCineBadge = (ev.CinematicIds != null && ev.CinematicIds.Count > 0) ? $" 🎬×{ev.CinematicIds.Count}" : "";
            GraphLabel(new Rect(headerRect.x + 8f, headerRect.y + 3f, cardW - 35f, 20f), $"<b>▲ {ev.Title}</b> [{ev.Kind}]{evCineBadge}");

            // 4. Bouton Supprimer
            GUI.backgroundColor = new Color(0.85f, 0.22f, 0.22f, 1f);
            GUI.color = Color.white;
            if (GraphButton(closeRect, "✕"))
            {
                node.Events.RemoveAt(index);
                PurgeSoundReferences(ev != null ? ev.EventId : "");
                if (_draggingCardKey == dragKey)
                {
                    _draggingCardKey = null;
                    GUIUtility.hotControl = 0;
                }
                GUI.backgroundColor = Color.white;
                evt.Use();
                return;
            }
            GUI.backgroundColor = Color.white;

            // 5. Poignée de redimensionnement
            GUI.color = new Color(1f, 1f, 1f, 0.4f);
            GraphLabel(resizeGripRect, "◢");
            GUI.color = Color.white;

            // Survol du header : infobulle lisible (cf. cartes dialogue).
            if (_zoom < 0.85f && !_isPanning && _draggingCardKey == null && _resizingCardKey == null && !_wireDraft.IsActive
                && headerRect.Contains(mouseWorld))
            {
                _hasHoverCard = true;
                _hoverTitle = $"▲ {ev.Title}  ({ev.EventId}) [{ev.Kind}]";
                _hoverBody = string.IsNullOrEmpty(ev.Description) ? "(aucune description)" : ev.Description;
                _hoverScreenPos = _graphMouseScreenPos;
            }

            // Contenu éditable en coordonnées monde, converti en coordonnées écran par GraphRect().
            float innerX = cardRect.x + 8f;
            float innerW = cardW - 16f;

            if (_zoom < CardLodThreshold)
            {
                DrawGraphSolidRect(new Rect(innerX, cardRect.y + 32f, innerW * 0.85f, 8f), new Color(0.85f, 0.4f, 1f, 0.12f));
                DrawGraphSolidRect(new Rect(innerX, cardRect.y + 44f, innerW * 0.5f, 6f), new Color(1f, 1f, 1f, 0.06f));
            }
            else
            {
                float y = cardRect.y + 30f;

                GraphLabel(new Rect(innerX, y, 22f, 18f), "ID:");
                ev.EventId = GraphTextField(new Rect(innerX + 24f, y, 70f, 18f), ev.EventId ?? "");
                GraphLabel(new Rect(innerX + 98f, y, 35f, 18f), "Titre:");
                ev.Title = GraphTextField(new Rect(innerX + 135f, y, Mathf.Max(40f, innerW - 135f), 18f), ev.Title ?? "");
                y += 20f;

                GraphLabel(new Rect(innerX, y, 45f, 18f), "Action:");
                {
                    int kindCount = Enum.GetValues(typeof(SceneEventKind)).Length;
                    int camFocusKind = (int)SceneEventKind.CameraFocus;
                    int camShakeKind = (int)SceneEventKind.CameraShake;
                    // Kinds caméra DÉ-HARDCODÉS (cartes 🎬) : sautés au cycle.
                    int CycleKind(int cur, int dir)
                    {
                        for (int k = 0; k < kindCount; k++)
                        {
                            cur = (cur + dir + kindCount) % kindCount;
                            if (cur != camFocusKind && cur != camShakeKind) return cur;
                        }
                        return (int)ev.Kind;
                    }
                    if (GraphButton(new Rect(innerX + 47f, y, 20f, 18f), "◀"))
                        ev.Kind = (SceneEventKind)CycleKind((int)ev.Kind, -1);
                    string kindWarn = (int)ev.Kind == camFocusKind || (int)ev.Kind == camShakeKind ? " (obsolète → 🎬)" : "";
                    GraphLabel(new Rect(innerX + 69f, y, Mathf.Max(40f, innerW - 69f - 22f), 18f), $"<b>{ev.Kind}</b>{kindWarn}");
                    if (GraphButton(new Rect(innerX + innerW - 20f, y, 20f, 18f), "▶"))
                        ev.Kind = (SceneEventKind)CycleKind((int)ev.Kind, 1);
                }
                y += 22f;

                // Strip + détails (ex-inspecteur) : prérequis, ambiance, test auto, effets.
                {
                    float stripX = innerX;
                    bool preOpen = FoldoutButton(new Rect(stripX, y, 46f, 20f), ev, "pre", "🔒"); stripX += 48f;
                    bool ambOpen = FoldoutButton(new Rect(stripX, y, 46f, 20f), ev, "amb", "🔊"); stripX += 48f;
                    bool autoOpen = FoldoutButton(new Rect(stripX, y, 46f, 20f), ev, "auto", "🎲");
                    y += 20f;
                    if (preOpen) DrawPrereqDetail(ref y, innerX + 12f, innerW - 12f, ev.Prerequisite);
                    if (ambOpen) DrawAmbienceDetail(ref y, innerX + 12f, innerW - 12f, ev.Ambience);
                    if (autoOpen) DrawAutoDetail(ref y, innerX + 12f, innerW - 12f, ev.AutoSkillCheck);
                }
                DrawEffectsBlock(ref y, innerX + 12f, innerW - 12f, ev.Effects, ev, "fx", "+fx", true);

                // Sortie cinématique 🎬 : fire-and-forget.
                DrawCineLinkRow(ref y, innerX, innerW, ev.CinematicIds);
                DrawSoundLinkRow(ref y, innerX, innerW, ev.EventId);

                Color prevBg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(1f, 0.7f, 0.2f);
                if (GraphButton(new Rect(innerX, y, innerW, 20f), "▲ Tester en Direct"))
                {
                    var controller = FindAnyObjectByType<Scenes.JsonStorySceneController>();
                    controller?.ExecuteScenicEvent(ev);
                }
                GUI.backgroundColor = prevBg;
            }

            bool evtIsExit = !EventHasOutgoing(ev);
            Rect outPort = evtIsExit
                ? new Rect(cardRect.xMax - 10f, cardRect.y + 16f, 20f, 20f)
                : new Rect(cardRect.xMax - 7f, cardRect.y + 19f, 14f, 14f);
            DrawGraphSolidRect(outPort, evtIsExit ? ExitRed : new Color(0.85f, 0.4f, 1f));

            if (GraphPrimaryDown(mouseWorld, outPort))
            {
                _wireDraft = new WireConnectionDraft
                {
                    IsActive = true,
                    IsEvent = true,
                    SourceEvent = ev,
                    StartPos = outPort.center
                };
                evt.Use();
            }

            // Port de sortie cinématique 🎬 : flanc droit, rangée dédiée (magenta).
            DrawCineOutPort(new Vector2(cardRect.xMax, cardRect.y + CinePortTopCards), ev.CinematicIds, mouseWorld);
            DrawSoundOutPort(new Vector2(cardRect.x + cardRect.width * 0.5f, cardRect.yMax), ev.EventId, mouseWorld);
        }

        private void DrawConsequenceCard(SceneConsequenceData cons, int index, SceneNodeData node, Vector2 mouseWorld)
        {
            if (string.IsNullOrEmpty(cons.ConsequenceId)) cons.ConsequenceId = $"cons_{index + 1}";
            cons.Rewards ??= new SceneDialogueRewardData();
            StorySceneData.EnsureRewardDefaults(cons.Rewards);
            cons.Effects ??= new System.Collections.Generic.List<ScenarioEffect>();
            var rew = cons.Rewards;

            Vector2 size = GetConsequenceCardSize(cons);
            float cardW = size.x;
            float cardH = size.y;

            Rect cardRect = new Rect(cons.GraphPosX, cons.GraphPosY, cardW, cardH);
            Rect headerRect = new Rect(cardRect.x, cardRect.y, cardW, 26f);
            Rect closeRect = new Rect(headerRect.xMax - 22f, headerRect.y + 3f, 18f, 18f);
            Rect dragHeaderRect = new Rect(headerRect.x, headerRect.y, headerRect.width - 26f, headerRect.height);
            Rect resizeGripRect = new Rect(cardRect.xMax - 14f, cardRect.yMax - 14f, 14f, 14f);

            Event evt = Event.current;
            string dragKey = $"cons_{index}_{cons.ConsequenceId}";
            string resizeKey = $"resize_{dragKey}";
            int dragControlId = GUIUtility.GetControlID(FocusType.Passive);
            int resizeControlId = GUIUtility.GetControlID(FocusType.Passive);

            bool lateResizeCons = IsLateResizeFor(cons);
            if (GraphPrimaryDown(mouseWorld, resizeGripRect) || lateResizeCons)
            {
                GUIUtility.hotControl = resizeControlId;
                _resizingCardKey = resizeKey;
                if (lateResizeCons) { _lateResizeTarget = null; GUIUtility.keyboardControl = 0; }
                evt.Use();
            }
            else if ((evt.type == EventType.MouseDrag || evt.type == EventType.MouseMove) && GUIUtility.hotControl == resizeControlId && _resizingCardKey == resizeKey)
            {
                cons.CardWidth = Mathf.Clamp(mouseWorld.x - cardRect.x, 320f, 750f);
                cons.CardHeight = Mathf.Clamp(mouseWorld.y - cardRect.y, 160f, 650f);
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == resizeControlId)
            {
                GUIUtility.hotControl = 0;
                _resizingCardKey = null;
                evt.Use();
            }

            bool lateDragCons = IsLateDragFor(cons);
            if (GraphPrimaryDown(mouseWorld, dragHeaderRect) || lateDragCons)
            {
                GUIUtility.hotControl = dragControlId;
                _draggingCardKey = dragKey;
                _dragOffset = (lateDragCons ? _lateDownWorld : mouseWorld) - new Vector2(cons.GraphPosX, cons.GraphPosY);
                if (lateDragCons) { _lateDragTarget = null; GUIUtility.keyboardControl = 0; }
                evt.Use();
            }
            else if ((evt.type == EventType.MouseDrag || evt.type == EventType.MouseMove) && GUIUtility.hotControl == dragControlId && _draggingCardKey == dragKey)
            {
                cons.GraphPosX = mouseWorld.x - _dragOffset.x;
                cons.GraphPosY = mouseWorld.y - _dragOffset.y;
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == dragControlId)
            {
                GUIUtility.hotControl = 0;
                _draggingCardKey = null;
                evt.Use();
            }

            Color headerCol = new Color(0.30f, 0.42f, 0.12f);
            DrawGraphSolidRect(cardRect, new Color(0.10f, 0.13f, 0.08f, 0.95f));
            DrawGraphSolidRect(headerRect, headerCol);
            DrawGraphSolidRect(new Rect(cardRect.x, cardRect.y, cardRect.width, 1f), headerCol * 1.4f);
            DrawGraphSolidRect(new Rect(cardRect.x, cardRect.yMax - 1f, cardRect.width, 1f), new Color(1f, 1f, 1f, 0.08f));
            DrawGraphSolidRect(new Rect(cardRect.x, cardRect.y, 1f, cardRect.height), new Color(1f, 1f, 1f, 0.08f));
            DrawGraphSolidRect(new Rect(cardRect.xMax - 1f, cardRect.y, 1f, cardRect.height), new Color(1f, 1f, 1f, 0.08f));

            DrawEntryInputPort(cardRect, new Color(0.35f, 1f, 0.55f),
                !ConsIdHasIncoming(node, cons.ConsequenceId), false);

            string consCineBadge = (cons.CinematicIds != null && cons.CinematicIds.Count > 0) ? $" 🎬×{cons.CinematicIds.Count}" : "";
            GraphLabel(new Rect(headerRect.x + 8f, headerRect.y + 3f, cardW - 35f, 20f), $"<b>🎁 {cons.Title}</b> (<i>{cons.ConsequenceId}</i>){consCineBadge}");

            GUI.backgroundColor = new Color(0.85f, 0.22f, 0.22f, 1f);
            GUI.color = Color.white;
            if (GraphButton(closeRect, "✕"))
            {
                node.Consequences.RemoveAt(index);
                PurgeSoundReferences(cons != null ? cons.ConsequenceId : "");
                if (_draggingCardKey == dragKey)
                {
                    _draggingCardKey = null;
                    GUIUtility.hotControl = 0;
                }
                GUI.backgroundColor = Color.white;
                evt.Use();
                return;
            }
            GUI.backgroundColor = Color.white;

            GUI.color = new Color(1f, 1f, 1f, 0.4f);
            GraphLabel(resizeGripRect, "◢");
            GUI.color = Color.white;

            if (_zoom < 0.85f && !_isPanning && _draggingCardKey == null && _resizingCardKey == null && !_wireDraft.IsActive
                && !_pickerCapturesMouse && headerRect.Contains(mouseWorld))
            {
                _hasHoverCard = true;
                _hoverTitle = $"🎁 {cons.Title}  ({cons.ConsequenceId})";
                var hb = new System.Text.StringBuilder();
                if (cons.HasDialogue && !string.IsNullOrEmpty(cons.Speech)) hb.AppendLine(cons.Speech);
                hb.AppendLine(cons.GetSummary());
                _hoverBody = hb.ToString().Trim();
                _hoverScreenPos = _graphMouseScreenPos;
            }

            float innerX = cardRect.x + 8f;
            float innerW = cardW - 16f;
            float nextRowTop = GetConsLinkRowTop(cons);

            if (_zoom < CardLodThreshold)
            {
                DrawGraphSolidRect(new Rect(innerX, cardRect.y + 32f, innerW * 0.85f, 8f), new Color(0.35f, 1f, 0.55f, 0.14f));
                DrawGraphSolidRect(new Rect(innerX, cardRect.y + 44f, innerW * 0.6f, 6f), new Color(1f, 1f, 1f, 0.06f));
            }
            else
            {
                float y = cardRect.y + 30f;

                GraphLabel(new Rect(innerX, y, 22f, 18f), "ID:");
                cons.ConsequenceId = GraphTextField(new Rect(innerX + 24f, y, 80f, 18f), cons.ConsequenceId ?? "");
                GraphLabel(new Rect(innerX + 108f, y, 38f, 18f), "Titre:");
                cons.Title = GraphTextField(new Rect(innerX + 148f, y, Mathf.Max(40f, innerW - 148f), 18f), cons.Title ?? "");
                y += 22f;

                cons.HasDialogue = GraphToggle(new Rect(innerX, y, 110f, 18f), cons.HasDialogue, "💬 Dialogue");
                cons.TriggersCombat = GraphToggle(new Rect(innerX + 114f, y, 100f, 18f), cons.TriggersCombat, "⚔ Combat");
                GUI.color = new Color(1f, 1f, 1f, 0.55f);
                GraphLabel(new Rect(innerX + 218f, y, Mathf.Max(40f, innerW - 218f), 18f), cons.Effects.Count > 0 ? $"+fx:{cons.Effects.Count}" : "");
                GUI.color = Color.white;
                y += 20f;

                if (cons.HasDialogue)
                {
                    GraphLabel(new Rect(innerX, y, 30f, 18f), "Loc:");
                    cons.SpeakerId = GraphTextField(new Rect(innerX + 32f, y, 64f, 18f), cons.SpeakerId ?? "");
                    {
                        Rect dropBtn = new Rect(innerX + 98f, y, 20f, 18f);
                        string dropKey = $"cons_{cons.ConsequenceId}";
                        if (GraphButton(dropBtn, "▼"))
                        {
                            if (_actorDropKey == dropKey) _actorDropKey = null;
                            else OpenActorDropdown(dropKey, dropBtn, cons.SpeakerId ?? "", v => cons.SpeakerId = v);
                        }
                    }
                    cons.StageDirection = GraphTextField(new Rect(innerX + 122f, y, Mathf.Max(40f, innerW - 122f), 18f), cons.StageDirection ?? "");
                    y += 22f;
                    cons.Speech = GraphTextArea(new Rect(innerX, y, innerW, 42f), cons.Speech ?? "");
                    y += 44f;
                }

                cons.Effects ??= new System.Collections.Generic.List<ScenarioEffect>();
                DrawEffectsBlock(ref y, innerX, innerW, cons.Effects, cons, "fx", "+fx", true);

                nextRowTop = y - cardRect.y;
                GraphLabel(new Rect(innerX, y, 52f, 18f), "→Diag:");
                cons.NextLineId = GraphTextField(new Rect(innerX + 54f, y, 84f, 18f), cons.NextLineId ?? "");
                GraphLabel(new Rect(innerX + 142f, y, 52f, 18f), "→Cons:");
                cons.NextConsequenceId = GraphTextField(new Rect(innerX + 196f, y, Mathf.Max(40f, innerW - 196f), 18f), cons.NextConsequenceId ?? "");
                y += 22f;

                GraphLabel(new Rect(innerX, y, 30f, 18f), "+XP");
                string cxp = GraphTextField(new Rect(innerX + 32f, y, 34f, 18f), rew.EarnXP.ToString());
                int.TryParse(cxp, out rew.EarnXP);
                GraphLabel(new Rect(innerX + 70f, y, 30f, 18f), "+CE");
                string cce = GraphTextField(new Rect(innerX + 102f, y, 34f, 18f), rew.EarnCredits.ToString());
                int.TryParse(cce, out rew.EarnCredits);
                GraphLabel(new Rect(innerX + 140f, y, 30f, 18f), "Obj:");
                rew.CompletionObjectiveId = GraphTextField(new Rect(innerX + 172f, y, Mathf.Max(40f, innerW - 172f), 18f), rew.CompletionObjectiveId ?? "");
                y += 22f;

                // Ligne item principal.
                {
                    float ix = innerX + 12f;
                    GraphLabel(new Rect(ix, y, 20f, 18f), "🎁");
                    ix += 22f;
                    float tailW = 14f + 2f + 30f + 2f + 22f + 2f + 22f;
                    rew.ItemRewardName = GraphTextField(new Rect(ix, y, Mathf.Max(40f, innerX + innerW - ix - tailW), 18f), rew.ItemRewardName ?? "");
                    ix = innerX + innerW - tailW;
                    GraphLabel(new Rect(ix, y, 14f, 18f), "x");
                    ix += 16f;
                    string cq = GraphTextField(new Rect(ix, y, 30f, 18f), rew.ItemRewardQuantity.ToString());
                    int.TryParse(cq, out rew.ItemRewardQuantity);
                    if (rew.ItemRewardQuantity < 1) rew.ItemRewardQuantity = 1;
                    ix += 32f;
                    Color prevPkM = GUI.backgroundColor;
                    if (IsPickerOpenForCons(cons, -1)) GUI.backgroundColor = new Color(0.35f, 1f, 0.55f);
                    if (GraphButton(new Rect(ix, y, 22f, 18f), "🔍"))
                    {
                        if (IsPickerOpenForCons(cons, -1)) CloseItemPicker();
                        else OpenItemPicker(cons, -1);
                    }
                    GUI.backgroundColor = prevPkM;
                    ix += 24f;
                    if (GraphButton(new Rect(ix, y, 22f, 18f), "+"))
                    {
                        rew.AdditionalItems.Add(new SceneItemRewardEntry { ItemName = "", Quantity = 1 });
                    }
                    y += 20f;
                }

                // Lignes items supplémentaires.
                for (int ri = 0; ri < rew.AdditionalItems.Count; ri++)
                {
                    var entry = rew.AdditionalItems[ri];
                    if (entry == null) { rew.AdditionalItems.RemoveAt(ri); break; }
                    float ix = innerX + 12f;
                    GraphLabel(new Rect(ix, y, 20f, 18f), "🎁");
                    ix += 22f;
                    float tailW = 14f + 2f + 30f + 2f + 22f + 2f + 22f;
                    entry.ItemName = GraphTextField(new Rect(ix, y, Mathf.Max(40f, innerX + innerW - ix - tailW), 18f), entry.ItemName ?? "");
                    ix = innerX + innerW - tailW;
                    GraphLabel(new Rect(ix, y, 14f, 18f), "x");
                    ix += 16f;
                    string eq2 = GraphTextField(new Rect(ix, y, 30f, 18f), entry.Quantity.ToString());
                    int.TryParse(eq2, out entry.Quantity);
                    if (entry.Quantity < 1) entry.Quantity = 1;
                    ix += 32f;
                    Color prevPkC = GUI.backgroundColor;
                    if (IsPickerOpenForCons(cons, ri)) GUI.backgroundColor = new Color(0.35f, 1f, 0.55f);
                    if (GraphButton(new Rect(ix, y, 22f, 18f), "🔍"))
                    {
                        if (IsPickerOpenForCons(cons, ri)) CloseItemPicker();
                        else OpenItemPicker(cons, ri);
                    }
                    GUI.backgroundColor = prevPkC;
                    ix += 24f;
                    GUI.backgroundColor = new Color(0.85f, 0.25f, 0.25f);
                    if (GraphButton(new Rect(ix, y, 22f, 18f), "✕"))
                    {
                        rew.AdditionalItems.RemoveAt(ri);
                        GUI.backgroundColor = Color.white;
                        break;
                    }
                    GUI.backgroundColor = Color.white;
                    y += 20f;
                }

                // Sortie cinématique 🎬 : fire-and-forget.
                DrawCineLinkRow(ref y, innerX, innerW, cons.CinematicIds);
                DrawSoundLinkRow(ref y, innerX, innerW, cons.ConsequenceId);
            }

            // Ports de sortie : → réplique (cyan, rouge + gros si sortie) et → conséquence (vert).
            bool consIsExit = !ConsHasOutgoing(cons);
            Rect outLinePort = consIsExit
                ? new Rect(cardRect.xMax - 10f, cardRect.y + 16f, 20f, 20f)
                : new Rect(cardRect.xMax - 8f, cardRect.y + 18f, 16f, 16f);
            DrawGraphSolidRect(outLinePort, consIsExit ? ExitRed : new Color(0f, 0.85f, 1f));
            if (GraphPrimaryDown(mouseWorld, outLinePort))
            {
                _wireDraft = new WireConnectionDraft
                {
                    IsActive = true,
                    FromConsequence = true,
                    ConsLinkToCons = false,
                    SourceConsequence = cons,
                    StartPos = outLinePort.center
                };
                evt.Use();
            }

            Rect outConsPort = new Rect(cardRect.xMax - 7f, cardRect.y + nextRowTop + 3f, 14f, 14f);
            DrawGraphSolidRect(outConsPort, new Color(0.4f, 1.0f, 0.5f));
            if (GraphPrimaryDown(mouseWorld, outConsPort))
            {
                _wireDraft = new WireConnectionDraft
                {
                    IsActive = true,
                    FromConsequence = true,
                    ConsLinkToCons = true,
                    SourceConsequence = cons,
                    StartPos = outConsPort.center
                };
                evt.Use();
            }

            // Port de sortie cinématique 🎬 : flanc droit, rangée dédiée (magenta).
            DrawCineOutPort(new Vector2(cardRect.xMax, cardRect.y + CinePortTopCards), cons.CinematicIds, mouseWorld);
            DrawSoundOutPort(new Vector2(cardRect.x + cardRect.width * 0.5f, cardRect.yMax), cons.ConsequenceId, mouseWorld);
        }

        private void DrawTriggerCard(SceneTriggerData trg, int index, Vector2 mouseWorld)
        {
            if (trg == null) return;
            if (string.IsNullOrEmpty(trg.TriggerId)) trg.TriggerId = $"trig_{index + 1}";
            string targetDisplay = string.IsNullOrEmpty(trg.TargetNodeId) ? "(sans cible)" : trg.TargetNodeId;

            Vector2 size = GetTriggerCardSize(trg);
            float cardW = size.x;
            float cardH = size.y;

            Rect cardRect = new Rect(trg.GraphPosX, trg.GraphPosY, cardW, cardH);
            Rect headerRect = new Rect(cardRect.x, cardRect.y, cardW, 26f);
            Rect closeRect = new Rect(headerRect.xMax - 22f, headerRect.y + 3f, 18f, 18f);
            Rect dragHeaderRect = new Rect(headerRect.x, headerRect.y, headerRect.width - 26f, headerRect.height);
            Rect resizeGripRect = new Rect(cardRect.xMax - 14f, cardRect.yMax - 14f, 14f, 14f);

            Event evt = Event.current;
            string dragKey = $"trigger_{index}_{trg.TriggerId}";
            string resizeKey = $"resize_{dragKey}";
            int dragControlId = GUIUtility.GetControlID(FocusType.Passive);
            int resizeControlId = GUIUtility.GetControlID(FocusType.Passive);

            bool lateResizeTrigger = IsLateResizeFor(trg);
            if (GraphPrimaryDown(mouseWorld, resizeGripRect) || lateResizeTrigger)
            {
                GUIUtility.hotControl = resizeControlId;
                _resizingCardKey = resizeKey;
                if (lateResizeTrigger) { _lateResizeTarget = null; GUIUtility.keyboardControl = 0; }
                evt.Use();
            }
            else if ((evt.type == EventType.MouseDrag || evt.type == EventType.MouseMove) && GUIUtility.hotControl == resizeControlId && _resizingCardKey == resizeKey)
            {
                trg.CardWidth = Mathf.Clamp(mouseWorld.x - cardRect.x, 300f, 750f);
                trg.CardHeight = Mathf.Clamp(mouseWorld.y - cardRect.y, 180f, 500f);
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == resizeControlId)
            {
                GUIUtility.hotControl = 0;
                _resizingCardKey = null;
                evt.Use();
            }

            bool lateDragTrigger = IsLateDragFor(trg);
            if (GraphPrimaryDown(mouseWorld, dragHeaderRect) || lateDragTrigger)
            {
                GUIUtility.hotControl = dragControlId;
                _draggingCardKey = dragKey;
                _dragOffset = (lateDragTrigger ? _lateDownWorld : mouseWorld) - new Vector2(trg.GraphPosX, trg.GraphPosY);
                if (lateDragTrigger) { _lateDragTarget = null; GUIUtility.keyboardControl = 0; }
                evt.Use();
            }
            else if ((evt.type == EventType.MouseDrag || evt.type == EventType.MouseMove) && GUIUtility.hotControl == dragControlId && _draggingCardKey == dragKey)
            {
                trg.GraphPosX = mouseWorld.x - _dragOffset.x;
                trg.GraphPosY = mouseWorld.y - _dragOffset.y;
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == dragControlId)
            {
                GUIUtility.hotControl = 0;
                _draggingCardKey = null;
                evt.Use();
            }

            DrawGraphSolidRect(cardRect, new Color(0.13f, 0.10f, 0.05f, 0.95f));
            Color headerCol = new Color(0.45f, 0.32f, 0.08f);
            DrawGraphSolidRect(headerRect, headerCol);

            DrawGraphSolidRect(new Rect(cardRect.x, cardRect.y, cardRect.width, 1f), new Color(1f, 0.85f, 0.25f, 0.8f));
            DrawGraphSolidRect(new Rect(cardRect.x, cardRect.yMax - 1f, cardRect.width, 1f), new Color(1f, 1f, 1f, 0.08f));
            DrawGraphSolidRect(new Rect(cardRect.x, cardRect.y, 1f, cardRect.height), new Color(1f, 1f, 1f, 0.08f));
            DrawGraphSolidRect(new Rect(cardRect.xMax - 1f, cardRect.y, 1f, cardRect.height), new Color(1f, 1f, 1f, 0.08f));

            string trgCineBadge = (trg.CinematicIds != null && trg.CinematicIds.Count > 0) ? $" 🎬×{trg.CinematicIds.Count}" : "";
            GraphLabel(new Rect(headerRect.x + 8f, headerRect.y + 3f, cardW - 35f, 20f), $"<b>▼ {trg.Label}</b> [{trg.TriggerId}]{trgCineBadge}");

            GUI.backgroundColor = new Color(0.85f, 0.22f, 0.22f, 1f);
            GUI.color = Color.white;
            if (GraphButton(closeRect, "✕"))
            {
                if (_data.Triggers != null) _data.Triggers.RemoveAt(index);
                PurgeSoundReferences(trg != null ? trg.TriggerId : "");
                if (_draggingCardKey == dragKey)
                {
                    _draggingCardKey = null;
                    GUIUtility.hotControl = 0;
                }
                GUI.backgroundColor = Color.white;
                evt.Use();
                return;
            }
            GUI.backgroundColor = Color.white;

            GUI.color = new Color(1f, 1f, 1f, 0.4f);
            GraphLabel(resizeGripRect, "◢");
            GUI.color = Color.white;

            if (_zoom < 0.85f && !_isPanning && _draggingCardKey == null && _resizingCardKey == null && !_wireDraft.IsActive
                && headerRect.Contains(mouseWorld))
            {
                _hasHoverCard = true;
                _hoverTitle = $"▼ {trg.Label}  ({trg.TriggerId})";
                _hoverBody = $"{trg.GetSummary()}\n➔ {targetDisplay}";
                _hoverScreenPos = _graphMouseScreenPos;
            }

            float innerX = cardRect.x + 8f;
            float innerW = cardW - 16f;

            if (_zoom < CardLodThreshold)
            {
                DrawGraphSolidRect(new Rect(innerX, cardRect.y + 32f, innerW * 0.8f, 8f), new Color(1f, 0.85f, 0.25f, 0.14f));
                DrawGraphSolidRect(new Rect(innerX, cardRect.y + 44f, innerW * 0.5f, 6f), new Color(1f, 1f, 1f, 0.06f));
            }
            else
            {
                float y = cardRect.y + 30f;

                GraphLabel(new Rect(innerX, y, 24f, 18f), "ID:");
                trg.TriggerId = GraphTextField(new Rect(innerX + 26f, y, 100f, 18f), trg.TriggerId ?? "");
                GUI.color = new Color(1f, 0.85f, 0.25f);
                GraphLabel(new Rect(innerX + 130f, y, Mathf.Max(40f, innerW - 130f), 18f), $"➔ {targetDisplay}");
                GUI.color = Color.white;
                y += 20f;

                GraphLabel(new Rect(innerX, y, 42f, 18f), "Titre:");
                trg.Label = GraphTextField(new Rect(innerX + 44f, y, Mathf.Max(40f, innerW - 44f), 18f), trg.Label ?? "");
                y += 20f;

                trg.Kind = (SceneTriggerKind)GraphToolbar(new Rect(innerX, y, innerW, 18f), (int)trg.Kind, new[] { "Action", "Objectif", "Flag", "Acteur" });
                y += 22f;

                if (trg.Kind == SceneTriggerKind.InteractableActivated)
                {
                    GraphLabel(new Rect(innerX, y, 95f, 18f), "Action carte :");
                    float fldX = innerX + 97f;
                    bool hasInters = _data.Interactables != null && _data.Interactables.Count > 0;
                    float arrowsW = hasInters ? 44f : 0f;
                    trg.InteractableId = GraphTextField(new Rect(fldX, y, Mathf.Max(40f, innerW - 97f - arrowsW), 18f), trg.InteractableId ?? "");
                    if (hasInters)
                    {
                        int curIdx = _data.Interactables.FindIndex(a => a != null && string.Equals(a.InteractableId, trg.InteractableId, System.StringComparison.OrdinalIgnoreCase));
                        if (GraphButton(new Rect(innerX + innerW - 42f, y, 20f, 18f), "◀"))
                        {
                            curIdx = (curIdx < 0 ? _data.Interactables.Count - 1 : (curIdx - 1 + _data.Interactables.Count) % _data.Interactables.Count);
                            trg.InteractableId = _data.Interactables[curIdx]?.InteractableId ?? "";
                        }
                        if (GraphButton(new Rect(innerX + innerW - 20f, y, 20f, 18f), "▶"))
                        {
                            curIdx = (curIdx < 0 ? 0 : (curIdx + 1) % _data.Interactables.Count);
                            trg.InteractableId = _data.Interactables[curIdx]?.InteractableId ?? "";
                        }
                    }
                    y += 20f;
                }
                else if (trg.Kind == SceneTriggerKind.ObjectiveCompleted)
                {
                    GraphLabel(new Rect(innerX, y, 65f, 18f), "Objectif :");
                    trg.ObjectiveId = GraphTextField(new Rect(innerX + 67f, y, Mathf.Max(40f, innerW - 67f), 18f), trg.ObjectiveId ?? "");
                    y += 20f;
                }
                else if (trg.Kind == SceneTriggerKind.CampaignFlagSet)
                {
                    GraphLabel(new Rect(innerX, y, 42f, 18f), "Flag :");
                    trg.FlagKey = GraphTextField(new Rect(innerX + 44f, y, 150f, 18f), trg.FlagKey ?? "");
                    trg.FlagMustBeSet = GraphToggle(new Rect(innerX + 198f, y, Mathf.Max(60f, innerW - 198f), 18f), trg.FlagMustBeSet, "Présent");
                    y += 20f;
                }
                else
                {
                    GraphLabel(new Rect(innerX, y, 58f, 18f), "Acteur :");
                    trg.ActorId = GraphTextField(new Rect(innerX + 60f, y, 110f, 18f), trg.ActorId ?? "");
                    trg.ActorCondition = (SceneActorConditionKind)GraphToolbar(new Rect(innerX + 174f, y, Mathf.Max(60f, innerW - 174f), 18f), (int)trg.ActorCondition, new[] { "Statut", "PV < %" });
                    y += 22f;
                    if (trg.ActorCondition == SceneActorConditionKind.HasStatus)
                    {
                        GraphLabel(new Rect(innerX, y, 58f, 18f), "Statut :");
                        trg.StatusName = GraphTextField(new Rect(innerX + 60f, y, Mathf.Max(40f, innerW - 60f), 18f), trg.StatusName ?? "");
                    }
                    else
                    {
                        GraphLabel(new Rect(innerX, y, 45f, 18f), "PV <");
                        int.TryParse(GraphTextField(new Rect(innerX + 47f, y, 45f, 18f), trg.HPPercentThreshold.ToString()), out trg.HPPercentThreshold);
                        trg.HPPercentThreshold = Mathf.Clamp(trg.HPPercentThreshold, 1, 100);
                        GraphLabel(new Rect(innerX + 96f, y, Mathf.Max(30f, innerW - 96f), 18f), "% max");
                    }
                    y += 20f;
                }

                GraphLabel(new Rect(innerX, y, 68f, 18f), "Si nœud :");
                trg.SourceNodeId = GraphTextField(new Rect(innerX + 70f, y, 120f, 18f), trg.SourceNodeId ?? "");
                trg.OneShot = GraphToggle(new Rect(innerX + 194f, y, Mathf.Max(60f, innerW - 194f), 18f), trg.OneShot, "Unique");
                y += 20f;

                // Sortie cinématique 🎬 : fire-and-forget.
                DrawCineLinkRow(ref y, innerX, innerW, trg.CinematicIds);
                DrawSoundLinkRow(ref y, innerX, innerW, trg.TriggerId);

                if (y + 4f <= cardRect.yMax)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.55f);
                    GraphLabel(new Rect(innerX, y, innerW, 18f), trg.GetSummary());
                    GUI.color = Color.white;
                }
            }

            // Sortie seule : pas de port d'entrée sur un déclencheur.
            Rect outPort = new Rect(cardRect.xMax - 8f, cardRect.y + 18f, 16f, 16f);
            DrawGraphSolidRect(outPort, new Color(1f, 0.85f, 0.25f));

            if (GraphPrimaryDown(mouseWorld, outPort))
            {
                _wireDraft = new WireConnectionDraft
                {
                    IsActive = true,
                    IsTrigger = true,
                    SourceTrigger = trg,
                    StartPos = outPort.center
                };
                evt.Use();
            }

            // Port de sortie cinématique 🎬 : flanc droit, sous la sortie principale (magenta).
            DrawCineOutPort(new Vector2(cardRect.xMax, cardRect.y + CinePortTopCards), trg.CinematicIds, mouseWorld);
            DrawSoundOutPort(new Vector2(cardRect.x + cardRect.width * 0.5f, cardRect.yMax), trg.TriggerId, mouseWorld);
        }
    }
}

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
    }
}

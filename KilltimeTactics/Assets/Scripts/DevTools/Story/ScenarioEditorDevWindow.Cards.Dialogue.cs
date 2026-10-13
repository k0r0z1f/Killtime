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
    }
}

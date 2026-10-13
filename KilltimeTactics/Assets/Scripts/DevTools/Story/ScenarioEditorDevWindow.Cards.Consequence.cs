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
    }
}

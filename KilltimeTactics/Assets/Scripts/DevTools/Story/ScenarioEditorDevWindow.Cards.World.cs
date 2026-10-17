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
        private void DrawInteractableCard(SceneInteractableSpawnData it, int index, Vector2 mouseWorld)
        {
            if (it == null) return;
            if (string.IsNullOrEmpty(it.InteractableId)) it.InteractableId = MakeUniqueInteractableId();
            string targetDisplay = string.IsNullOrEmpty(it.TriggerNodeId) ? "(sans cible)" : it.TriggerNodeId;

            Vector2 size = GetInteractableCardSize(it);
            float cardW = size.x;
            float cardH = size.y;

            Rect cardRect = new Rect(it.GraphPosX, it.GraphPosY, cardW, cardH);
            Rect headerRect = new Rect(cardRect.x, cardRect.y, cardW, 26f);
            Rect closeRect = new Rect(headerRect.xMax - 22f, headerRect.y + 3f, 18f, 18f);
            Rect dragHeaderRect = new Rect(headerRect.x, headerRect.y, headerRect.width - 26f, headerRect.height);
            Rect resizeGripRect = new Rect(cardRect.xMax - 14f, cardRect.yMax - 14f, 14f, 14f);

            Event evt = Event.current;
            string dragKey = $"inter_{index}_{it.InteractableId}";
            string resizeKey = $"resize_{dragKey}";
            int dragControlId = GUIUtility.GetControlID(FocusType.Passive);
            int resizeControlId = GUIUtility.GetControlID(FocusType.Passive);

            bool lateResizeInter = IsLateResizeFor(it);
            if (GraphPrimaryDown(mouseWorld, resizeGripRect) || lateResizeInter)
            {
                GUIUtility.hotControl = resizeControlId;
                _resizingCardKey = resizeKey;
                if (lateResizeInter) { _lateResizeTarget = null; GUIUtility.keyboardControl = 0; }
                evt.Use();
            }
            else if ((evt.type == EventType.MouseDrag || evt.type == EventType.MouseMove) && GUIUtility.hotControl == resizeControlId && _resizingCardKey == resizeKey)
            {
                it.CardWidth = Mathf.Clamp(mouseWorld.x - cardRect.x, 300f, 750f);
                it.CardHeight = Mathf.Clamp(mouseWorld.y - cardRect.y, 200f, 500f);
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == resizeControlId)
            {
                GUIUtility.hotControl = 0;
                _resizingCardKey = null;
                evt.Use();
            }

            bool lateDragInter = IsLateDragFor(it);
            if (GraphPrimaryDown(mouseWorld, dragHeaderRect) || lateDragInter)
            {
                GUIUtility.hotControl = dragControlId;
                _draggingCardKey = dragKey;
                _dragOffset = (lateDragInter ? _lateDownWorld : mouseWorld) - new Vector2(it.GraphPosX, it.GraphPosY);
                if (lateDragInter) { _lateDragTarget = null; GUIUtility.keyboardControl = 0; }
                evt.Use();
            }
            else if ((evt.type == EventType.MouseDrag || evt.type == EventType.MouseMove) && GUIUtility.hotControl == dragControlId && _draggingCardKey == dragKey)
            {
                it.GraphPosX = mouseWorld.x - _dragOffset.x;
                it.GraphPosY = mouseWorld.y - _dragOffset.y;
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == dragControlId)
            {
                GUIUtility.hotControl = 0;
                _draggingCardKey = null;
                evt.Use();
            }

            DrawGraphSolidRect(cardRect, new Color(0.05f, 0.12f, 0.12f, 0.95f));
            Color headerCol = new Color(0.08f, 0.45f, 0.42f);
            DrawGraphSolidRect(headerRect, headerCol);

            DrawGraphSolidRect(new Rect(cardRect.x, cardRect.y, cardRect.width, 1f), new Color(0.2f, 0.95f, 0.85f, 0.8f));
            DrawGraphSolidRect(new Rect(cardRect.x, cardRect.yMax - 1f, cardRect.width, 1f), new Color(1f, 1f, 1f, 0.08f));
            DrawGraphSolidRect(new Rect(cardRect.x, cardRect.y, 1f, cardRect.height), new Color(1f, 1f, 1f, 0.08f));
            DrawGraphSolidRect(new Rect(cardRect.xMax - 1f, cardRect.y, 1f, cardRect.height), new Color(1f, 1f, 1f, 0.08f));

            string headerName = string.IsNullOrEmpty(it.DisplayName) ? "(sans nom)" : it.DisplayName;
            string itCineBadge = (it.CinematicIds != null && it.CinematicIds.Count > 0) ? $" 🎬×{it.CinematicIds.Count}" : "";
            GraphLabel(new Rect(headerRect.x + 8f, headerRect.y + 3f, cardW - 35f, 20f), $"<b>🔧 {headerName}</b> [{it.InteractableId}]{itCineBadge}");

            GUI.backgroundColor = new Color(0.85f, 0.22f, 0.22f, 1f);
            GUI.color = Color.white;
            if (GraphButton(closeRect, "✕"))
            {
                if (_data.Interactables != null) _data.Interactables.RemoveAt(index);
                PurgeSoundReferences(it != null ? it.InteractableId : "");
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
                _hoverTitle = $"🔧 {headerName}  ({it.InteractableId})";
                _hoverBody = $"{it.GetSummary()}\nAction : {it.ActionLabel}\n{(string.IsNullOrEmpty(it.SuccessLog) ? "(aucun log)" : it.SuccessLog)}";
                _hoverScreenPos = _graphMouseScreenPos;
            }

            float innerX = cardRect.x + 8f;
            float innerW = cardW - 16f;

            if (_zoom < CardLodThreshold)
            {
                DrawGraphSolidRect(new Rect(innerX, cardRect.y + 32f, innerW * 0.8f, 8f), new Color(0.2f, 0.95f, 0.85f, 0.14f));
                DrawGraphSolidRect(new Rect(innerX, cardRect.y + 44f, innerW * 0.55f, 6f), new Color(1f, 1f, 1f, 0.06f));
            }
            else
            {
                float y = cardRect.y + 30f;

                GraphLabel(new Rect(innerX, y, 24f, 18f), "ID:");
                it.InteractableId = GraphTextField(new Rect(innerX + 26f, y, 100f, 18f), it.InteractableId ?? "");
                GUI.color = new Color(0.2f, 0.95f, 0.85f);
                GraphLabel(new Rect(innerX + 130f, y, Mathf.Max(40f, innerW - 130f), 18f), $"➔ {targetDisplay}");
                GUI.color = Color.white;
                y += 20f;

                GraphLabel(new Rect(innerX, y, 36f, 18f), "Nom:");
                it.DisplayName = GraphTextField(new Rect(innerX + 38f, y, Mathf.Max(40f, innerW - 38f), 18f), it.DisplayName ?? "");
                y += 20f;

                GraphLabel(new Rect(innerX, y, 48f, 18f), "Action:");
                it.ActionLabel = GraphTextField(new Rect(innerX + 50f, y, Mathf.Max(40f, innerW - 50f), 18f), it.ActionLabel ?? "");
                y += 20f;

                GraphLabel(new Rect(innerX, y, 74f, 18f), "Description :");
                it.Description = GraphTextArea(new Rect(innerX + 76f, y, Mathf.Max(40f, innerW - 76f), 48f), it.Description ?? "");
                y += 50f;

                // Position hex + rayon sur une ligne compacte.
                GraphLabel(new Rect(innerX, y, 16f, 18f), "Q:");
                int.TryParse(GraphTextField(new Rect(innerX + 18f, y, 36f, 18f), it.Q.ToString()), out it.Q);
                GraphLabel(new Rect(innerX + 58f, y, 14f, 18f), "R:");
                int.TryParse(GraphTextField(new Rect(innerX + 72f, y, 36f, 18f), it.R.ToString()), out it.R);
                GraphLabel(new Rect(innerX + 112f, y, 44f, 18f), "Rayon:");
                int.TryParse(GraphTextField(new Rect(innerX + 158f, y, 32f, 18f), it.Radius.ToString()), out it.Radius);
                it.Radius = Mathf.Clamp(it.Radius, 0, 12);
                it.IsOneShot = GraphToggle(new Rect(innerX + 194f, y, Mathf.Max(60f, innerW - 194f), 18f), it.IsOneShot, "Unique");
                y += 20f;

                // Compétence requise + SD.
                GraphLabel(new Rect(innerX, y, 46f, 18f), "Test :");
                {
                    int skillCount = Enum.GetValues(typeof(SkillType)).Length;
                    int skillIdx = (int)it.RequiredSkill;
                    if (GraphButton(new Rect(innerX + 48f, y, 20f, 18f), "◀"))
                    {
                        skillIdx = (skillIdx - 1 + skillCount) % skillCount;
                        it.RequiredSkill = (SkillType)skillIdx;
                    }
                    GraphLabel(new Rect(innerX + 70f, y, Mathf.Max(40f, innerW - 70f - 72f), 18f), $"<b>{SkillDefinitions.GetDisplayName(it.RequiredSkill)}</b>");
                    if (GraphButton(new Rect(innerX + innerW - 70f, y, 20f, 18f), "▶"))
                    {
                        skillIdx = (skillIdx + 1) % skillCount;
                        it.RequiredSkill = (SkillType)skillIdx;
                    }
                    GraphLabel(new Rect(innerX + innerW - 48f, y, 20f, 18f), "SD:");
                    int.TryParse(GraphTextField(new Rect(innerX + innerW - 26f, y, 26f, 18f), it.SkillThreshold.ToString()), out it.SkillThreshold);
                }
                y += 20f;

                GraphLabel(new Rect(innerX, y, 68f, 18f), "→ Nœud :");
                it.TriggerNodeId = GraphTextField(new Rect(innerX + 70f, y, Mathf.Max(40f, innerW - 70f), 18f), it.TriggerNodeId ?? "");
                y += 20f;

                GraphLabel(new Rect(innerX, y, 66f, 18f), "Objectif :");
                it.CompletionObjectiveId = GraphTextField(new Rect(innerX + 68f, y, Mathf.Max(40f, innerW - 68f), 18f), it.CompletionObjectiveId ?? "");
                y += 20f;

                GraphLabel(new Rect(innerX, y, 66f, 18f), "Log ✔ :");
                it.SuccessLog = GraphTextField(new Rect(innerX + 68f, y, Mathf.Max(40f, innerW - 68f), 18f), it.SuccessLog ?? "");
                y += 20f;

                GraphLabel(new Rect(innerX, y, 66f, 18f), "Log ✗ :");
                it.FailureLog = GraphTextField(new Rect(innerX + 68f, y, Mathf.Max(40f, innerW - 68f), 18f), it.FailureLog ?? "");
                y += 20f;

                // Variantes de description (ex : brouilleur instable).
                DrawDescriptionVariantsBlock(ref y, innerX, innerW, it);

                // Actions explicites de la fenêtre flottante.
                DrawInteractableActionsBlock(ref y, innerX, innerW, it);

                // Sortie cinématique 🎬 : fire-and-forget.
                DrawCineLinkRow(ref y, innerX, innerW, it.CinematicIds);
                DrawSoundLinkRow(ref y, innerX, innerW, it.InteractableId);

                if (y + 4f <= cardRect.yMax)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.55f);
                    GraphLabel(new Rect(innerX, y, innerW, 18f), it.GetSummary());
                    GUI.color = Color.white;
                }
            }

            // Sortie seule : pas de port d'entrée sur un interactable.
            Rect outPort2 = new Rect(cardRect.xMax - 8f, cardRect.y + 18f, 16f, 16f);
            DrawGraphSolidRect(outPort2, new Color(0.2f, 0.95f, 0.85f));

            if (GraphPrimaryDown(mouseWorld, outPort2))
            {
                _wireDraft = new WireConnectionDraft
                {
                    IsActive = true,
                    IsInteractable = true,
                    SourceInteractable = it,
                    StartPos = outPort2.center
                };
                evt.Use();
            }

            // Port de sortie cinématique 🎬 : flanc droit, sous la sortie principale (magenta).
            DrawCineOutPort(new Vector2(cardRect.xMax, cardRect.y + CinePortTopCards), it.CinematicIds, mouseWorld);
            DrawSoundOutPort(new Vector2(cardRect.x + cardRect.width * 0.5f, cardRect.yMax), it.InteractableId, mouseWorld);
        }

        private void DrawDescriptionVariantsBlock(ref float y, float x, float w, SceneInteractableSpawnData it)
        {
            if (it.DescriptionVariants == null) it.DescriptionVariants = new System.Collections.Generic.List<string>();
            bool open = FoldoutButton(new Rect(x, y, Mathf.Max(60f, w - 70f), 20f), it, "descvar", $"Variantes ({it.DescriptionVariants.Count})");
            if (GraphButton(new Rect(x + w - 66f, y, 66f, 20f), "+ Var"))
                it.DescriptionVariants.Add("Nouvelle variante...");
            y += 20f;
            if (!open) return;
            for (int i = 0; i < it.DescriptionVariants.Count; i++)
            {
                it.DescriptionVariants[i] = GraphTextField(new Rect(x + 22f, y, Mathf.Max(30f, w - 52f), 18f), it.DescriptionVariants[i] ?? "");
                if (GraphButton(new Rect(x, y, 18f, 18f), "−"))
                {
                    it.DescriptionVariants.RemoveAt(i);
                    i--;
                    continue;
                }
                y += 20f;
            }
            y += 2f;
        }

        private void DrawInteractableActionsBlock(ref float y, float x, float w, SceneInteractableSpawnData it)
        {
            if (it.Actions == null) it.Actions = new System.Collections.Generic.List<SceneInteractableAction>();
            bool open = FoldoutButton(new Rect(x, y, Mathf.Max(60f, w - 70f), 20f), it, "actions", $"Actions ({it.Actions.Count})");
            if (GraphButton(new Rect(x + w - 66f, y, 66f, 20f), "+ Act"))
                it.Actions.Add(new SceneInteractableAction { ActionId = $"action_{it.Actions.Count + 1}" });
            y += 20f;
            if (!open) return;
            int skillCount = System.Enum.GetValues(typeof(SkillType)).Length;
            for (int i = 0; i < it.Actions.Count; i++)
            {
                var action = it.Actions[i];
                if (action == null) { it.Actions.RemoveAt(i); i--; continue; }
                float rowX = x;
                float rowW = w;
                GraphLabel(new Rect(rowX, y, 26f, 18f), "ID:");
                action.ActionId = GraphTextField(new Rect(rowX + 28f, y, 80f, 18f), action.ActionId ?? "");
                GraphLabel(new Rect(rowX + 112f, y, 34f, 18f), "Lbl:");
                action.Label = GraphTextField(new Rect(rowX + 148f, y, Mathf.Max(30f, rowW - 148f), 18f), action.Label ?? "");
                y += 20f;

                GraphLabel(new Rect(rowX, y, 26f, 18f), "Skill:");
                int skillIdx = action.RequiredSkill;
                if (GraphButton(new Rect(rowX + 30f, y, 18f, 18f), "◀"))
                {
                    skillIdx = (skillIdx - 1 + skillCount) % skillCount;
                    action.RequiredSkill = skillIdx;
                }
                string skillName = action.RequiredSkill >= 0 ? SkillDefinitions.GetDisplayName((SkillType)action.RequiredSkill) : "Libre";
                GraphLabel(new Rect(rowX + 50f, y, Mathf.Max(30f, rowW - 116f), 18f), $"<b>{skillName}</b>");
                if (GraphButton(new Rect(rowX + rowW - 64f, y, 18f, 18f), "▶"))
                {
                    skillIdx = (skillIdx + 1) % skillCount;
                    action.RequiredSkill = skillIdx;
                }
                GraphLabel(new Rect(rowX + rowW - 44f, y, 22f, 18f), "SD:");
                int.TryParse(GraphTextField(new Rect(rowX + rowW - 22f, y, 22f, 18f), action.SkillThreshold.ToString()), out action.SkillThreshold);
                y += 20f;

                GraphLabel(new Rect(rowX, y, 34f, 18f), "Log✔:");
                action.SuccessLog = GraphTextField(new Rect(rowX + 36f, y, Mathf.Max(30f, rowW - 36f), 18f), action.SuccessLog ?? "");
                y += 20f;

                GraphLabel(new Rect(rowX, y, 34f, 18f), "Log✗:");
                action.FailureLog = GraphTextField(new Rect(rowX + 36f, y, Mathf.Max(30f, rowW - 36f), 18f), action.FailureLog ?? "");
                y += 20f;

                GraphLabel(new Rect(rowX, y, 34f, 18f), "Obj:");
                action.CompletionObjectiveId = GraphTextField(new Rect(rowX + 36f, y, Mathf.Max(30f, rowW - 36f), 18f), action.CompletionObjectiveId ?? "");
                y += 20f;

                GraphLabel(new Rect(rowX, y, 38f, 18f), "Node:");
                action.TriggerNodeId = GraphTextField(new Rect(rowX + 40f, y, Mathf.Max(30f, rowW - 82f), 18f), action.TriggerNodeId ?? "");
                action.IsOneShot = GraphToggle(new Rect(rowX + rowW - 38f, y, 38f, 18f), action.IsOneShot, "1x");
                y += 20f;

                if (GraphButton(new Rect(rowX, y, 60f, 18f), "− Action"))
                {
                    it.Actions.RemoveAt(i);
                    i--;
                }
                y += 22f;
            }
            y += 2f;
        }
    }
}

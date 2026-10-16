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
        private void CountActorReferences(SceneActorSpawnData a, out int lines, out int events, out int triggers)
        {
            lines = 0; events = 0; triggers = 0;
            if (a == null || _data == null) return;
            bool Match(string v)
            {
                if (string.IsNullOrWhiteSpace(v)) return false;
                return string.Equals(v.Trim(), a.ActorId?.Trim(), System.StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrWhiteSpace(a.DisplayName) && string.Equals(v.Trim(), a.DisplayName.Trim(), System.StringComparison.OrdinalIgnoreCase));
            }
            if (_data.Nodes != null)
            {
                for (int n = 0; n < _data.Nodes.Count; n++)
                {
                    var node = _data.Nodes[n];
                    if (node == null) continue;
                    if (node.Dialogues != null)
                    {
                        for (int d = 0; d < node.Dialogues.Count; d++)
                        {
                            var line = node.Dialogues[d];
                            if (line == null) continue;
                            if (Match(line.SpeakerId)) lines++;
                        }
                    }
                    if (node.Consequences != null)
                    {
                        for (int k = 0; k < node.Consequences.Count; k++)
                        {
                            var cons = node.Consequences[k];
                            if (cons == null) continue;
                            if (cons.HasDialogue && Match(cons.SpeakerId)) lines++;
                        }
                    }
                    if (node.Events != null)
                    {
                        for (int e = 0; e < node.Events.Count; e++)
                        {
                            var ev = node.Events[e];
                            if (ev == null) continue;
                            if (Match(ev.TargetActorId)) events++;
                        }
                    }
                }
            }
            if (_data.Triggers != null)
            {
                for (int t = 0; t < _data.Triggers.Count; t++)
                {
                    var trg = _data.Triggers[t];
                    if (trg == null) continue;
                    if (trg.Kind == SceneTriggerKind.ActorCondition && Match(trg.ActorId)) triggers++;
                }
            }
        }
        private void DrawActorCard(SceneActorSpawnData a, int index, Vector2 mouseWorld)
        {
            if (a == null) return;
            if (string.IsNullOrEmpty(a.ActorId)) a.ActorId = MakeUniqueActorId();
            string spawnDisplay = a.SpawnInitially
                ? "● INIT"
                : (string.IsNullOrEmpty(a.SpawnOnNodeId) ? "○ (jamais spawné)" : $"○ si {a.SpawnOnNodeId}");

            Vector2 size = GetActorCardSize(a);
            float cardW = size.x;
            float cardH = size.y;

            Rect cardRect = new Rect(a.GraphPosX, a.GraphPosY, cardW, cardH);
            Rect headerRect = new Rect(cardRect.x, cardRect.y, cardW, 26f);
            Rect closeRect = new Rect(headerRect.xMax - 22f, headerRect.y + 3f, 18f, 18f);
            Rect dragHeaderRect = new Rect(headerRect.x, headerRect.y, headerRect.width - 26f, headerRect.height);
            Rect resizeGripRect = new Rect(cardRect.xMax - 14f, cardRect.yMax - 14f, 14f, 14f);

            Event evt = Event.current;
            string dragKey = $"actor_{index}_{a.ActorId}";
            string resizeKey = $"resize_{dragKey}";
            int dragControlId = GUIUtility.GetControlID(FocusType.Passive);
            int resizeControlId = GUIUtility.GetControlID(FocusType.Passive);

            bool lateResizeActor = IsLateResizeFor(a);
            if (GraphPrimaryDown(mouseWorld, resizeGripRect) || lateResizeActor)
            {
                GUIUtility.hotControl = resizeControlId;
                _resizingCardKey = resizeKey;
                if (lateResizeActor) { _lateResizeTarget = null; GUIUtility.keyboardControl = 0; }
                evt.Use();
            }
            else if ((evt.type == EventType.MouseDrag || evt.type == EventType.MouseMove) && GUIUtility.hotControl == resizeControlId && _resizingCardKey == resizeKey)
            {
                a.CardWidth = Mathf.Clamp(mouseWorld.x - cardRect.x, 300f, 750f);
                a.CardHeight = Mathf.Clamp(mouseWorld.y - cardRect.y, 220f, 500f);
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == resizeControlId)
            {
                GUIUtility.hotControl = 0;
                _resizingCardKey = null;
                evt.Use();
            }

            bool lateDragActor = IsLateDragFor(a);
            if (GraphPrimaryDown(mouseWorld, dragHeaderRect) || lateDragActor)
            {
                GUIUtility.hotControl = dragControlId;
                _draggingCardKey = dragKey;
                _dragOffset = (lateDragActor ? _lateDownWorld : mouseWorld) - new Vector2(a.GraphPosX, a.GraphPosY);
                if (lateDragActor) { _lateDragTarget = null; GUIUtility.keyboardControl = 0; }
                evt.Use();
            }
            else if ((evt.type == EventType.MouseDrag || evt.type == EventType.MouseMove) && GUIUtility.hotControl == dragControlId && _draggingCardKey == dragKey)
            {
                a.GraphPosX = mouseWorld.x - _dragOffset.x;
                a.GraphPosY = mouseWorld.y - _dragOffset.y;
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == dragControlId)
            {
                GUIUtility.hotControl = 0;
                _draggingCardKey = null;
                evt.Use();
            }

            Color factionCol = a.IsPlayer ? new Color(0.0f, 0.85f, 1.0f) : new Color(1.0f, 0.4f, 0.4f);
            Color headerCol = a.IsPlayer ? new Color(0.10f, 0.32f, 0.55f) : new Color(0.45f, 0.15f, 0.15f);
            DrawGraphSolidRect(cardRect, new Color(0.06f, 0.08f, 0.12f, 0.95f));
            DrawGraphSolidRect(headerRect, headerCol);

            DrawGraphSolidRect(new Rect(cardRect.x, cardRect.y, cardRect.width, 1f), new Color(factionCol.r, factionCol.g, factionCol.b, 0.8f));
            DrawGraphSolidRect(new Rect(cardRect.x, cardRect.yMax - 1f, cardRect.width, 1f), new Color(1f, 1f, 1f, 0.08f));
            DrawGraphSolidRect(new Rect(cardRect.x, cardRect.y, 1f, cardRect.height), new Color(1f, 1f, 1f, 0.08f));
            DrawGraphSolidRect(new Rect(cardRect.xMax - 1f, cardRect.y, 1f, cardRect.height), new Color(1f, 1f, 1f, 0.08f));

            // Port d'entrée visuel (spawn) : pas de drag, la cible se règle via "Si nœud".
            DrawGraphSolidRect(new Rect(cardRect.x - 7f, cardRect.y + 19f, 14f, 14f), factionCol);

            string headerName = string.IsNullOrEmpty(a.DisplayName) ? "(sans nom)" : a.DisplayName;
            string aCineBadge = (a.CinematicIds != null && a.CinematicIds.Count > 0) ? $" 🎬×{a.CinematicIds.Count}" : "";
            GraphLabel(new Rect(headerRect.x + 8f, headerRect.y + 3f, cardW - 35f, 20f), $"<b>👥 {headerName}</b> [{a.ActorId}]{aCineBadge}");

            GUI.backgroundColor = new Color(0.85f, 0.22f, 0.22f, 1f);
            GUI.color = Color.white;
            if (GraphButton(closeRect, "✕"))
            {
                string doomedActorId = a != null ? a.ActorId : "";
                if (_data.Actors != null) _data.Actors.RemoveAt(index);
                PurgeSoundReferences(doomedActorId);
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

            CountActorReferences(a, out int refLines, out int refEvents, out int refTriggers);
            if (_zoom < 0.85f && !_isPanning && _draggingCardKey == null && _resizingCardKey == null && !_wireDraft.IsActive
                && headerRect.Contains(mouseWorld))
            {
                _hasHoverCard = true;
                _hoverTitle = $"👥 {headerName}  ({a.ActorId})";
                string talkInfo = "(aucune sortie 💬)";
                if (a.TalkEntries != null && a.TalkEntries.Count > 0)
                {
                    var bits = new System.Collections.Generic.List<string>();
                    for (int bti = 0; bti < a.TalkEntries.Count; bti++)
                    {
                        var bte = a.TalkEntries[bti];
                        if (bte == null || string.IsNullOrWhiteSpace(bte.TargetId)) continue;
                        string who = string.IsNullOrWhiteSpace(bte.SpeakerId) ? "*" : bte.SpeakerId.Trim();
                        bits.Add($"{who}➔{bte.TargetId.Trim()}");
                    }
                    if (bits.Count > 0) talkInfo = "💬 " + string.Join(", ", bits);
                }
                _hoverBody = $"{a.GetSummary()}\nFiche : {(string.IsNullOrEmpty(a.CharacterSheetFileName) ? "(Défaut)" : a.CharacterSheetFileName)}\nRépliques : {refLines} · Cibles : {refEvents} · Triggers : {refTriggers}\nParler à : {talkInfo}";
                _hoverScreenPos = _graphMouseScreenPos;
            }

            float innerX = cardRect.x + 8f;
            float innerW = cardW - 16f;

            if (_zoom < CardLodThreshold)
            {
                DrawGraphSolidRect(new Rect(innerX, cardRect.y + 32f, innerW * 0.7f, 8f), new Color(factionCol.r, factionCol.g, factionCol.b, 0.18f));
                if (a.TalkEntries != null)
                {
                    for (int ti = 0; ti < a.TalkEntries.Count; ti++)
                    {
                        var te = a.TalkEntries[ti];
                        if (te == null) continue;
                        Vector2 talkCenter = GetActorTalkOutCenter(a, ti);
                        bool hasTalk = !string.IsNullOrWhiteSpace(te.TargetId);
                        Color col = hasTalk ? TalkPortCol : new Color(TalkPortCol.r, TalkPortCol.g, TalkPortCol.b, 0.35f);
                        Rect talkPort = new Rect(talkCenter.x - 7f, talkCenter.y - 7f, 14f, 14f);
                        DrawGraphSolidRect(talkPort, col);
                        if (GraphPrimaryDown(mouseWorld, talkPort))
                        {
                            _wireDraft = new WireConnectionDraft
                            {
                                IsActive = true,
                                IsActorTalk = true,
                                SourceActor = a,
                                SourceTalkIndex = ti,
                                StartPos = talkCenter
                            };
                            evt.Use();
                        }
                    }
                }
            }
            else
            {
                float y = cardRect.y + 30f;

                GraphLabel(new Rect(innerX, y, 24f, 18f), "ID:");
                a.ActorId = GraphTextField(new Rect(innerX + 26f, y, 100f, 18f), a.ActorId ?? "");
                GUI.color = a.SpawnInitially ? new Color(0.4f, 1f, 0.55f) : new Color(1f, 1f, 1f, 0.75f);
                GraphLabel(new Rect(innerX + 130f, y, Mathf.Max(40f, innerW - 130f), 18f), spawnDisplay);
                GUI.color = Color.white;
                y += 20f;

                GraphLabel(new Rect(innerX, y, 36f, 18f), "Nom:");
                a.DisplayName = GraphTextField(new Rect(innerX + 38f, y, Mathf.Max(40f, innerW - 38f), 18f), a.DisplayName ?? "");
                y += 20f;

                // Faction + position hex + orientation sur une ligne compacte.
                a.IsPlayer = GraphToggle(new Rect(innerX, y, 52f, 18f), a.IsPlayer, "PJ");
                GraphLabel(new Rect(innerX + 54f, y, 16f, 18f), "Q:");
                int.TryParse(GraphTextField(new Rect(innerX + 70f, y, 36f, 18f), a.Q.ToString()), out a.Q);
                GraphLabel(new Rect(innerX + 110f, y, 14f, 18f), "R:");
                int.TryParse(GraphTextField(new Rect(innerX + 124f, y, 36f, 18f), a.R.ToString()), out a.R);
                GraphLabel(new Rect(innerX + 164f, y, 30f, 18f), "Ori:");
                float.TryParse(GraphTextField(new Rect(innerX + 196f, y, 40f, 18f), a.FacingAngle.ToString("0")), out a.FacingAngle);
                y += 20f;

                // Spawn : initiale ou différé sur nœud (avec cycleur de nœuds).
                a.SpawnInitially = GraphToggle(new Rect(innerX, y, 78f, 18f), a.SpawnInitially, "Initiale");
                GraphLabel(new Rect(innerX + 80f, y, 58f, 18f), "Si nœud:");
                {
                    bool hasNodes = _data.Nodes != null && _data.Nodes.Count > 0;
                    float arrowsW = hasNodes ? 44f : 0f;
                    float fldW = Mathf.Max(40f, innerW - 80f - 58f - arrowsW);
                    a.SpawnOnNodeId = GraphTextField(new Rect(innerX + 140f, y, fldW, 18f), a.SpawnOnNodeId ?? "");
                    if (hasNodes)
                    {
                        int curIdx = _data.Nodes.FindIndex(n => n != null && string.Equals(n.NodeId, a.SpawnOnNodeId, System.StringComparison.OrdinalIgnoreCase));
                        if (GraphButton(new Rect(innerX + innerW - 42f, y, 20f, 18f), "◀"))
                        {
                            curIdx = (curIdx < 0 ? _data.Nodes.Count - 1 : (curIdx - 1 + _data.Nodes.Count) % _data.Nodes.Count);
                            a.SpawnOnNodeId = _data.Nodes[curIdx] != null ? _data.Nodes[curIdx].NodeId : "";
                        }
                        if (GraphButton(new Rect(innerX + innerW - 20f, y, 20f, 18f), "▶"))
                        {
                            curIdx = (curIdx < 0 ? 0 : (curIdx + 1) % _data.Nodes.Count);
                            a.SpawnOnNodeId = _data.Nodes[curIdx] != null ? _data.Nodes[curIdx].NodeId : "";
                        }
                    }
                }
                y += 20f;

                // Fiche personnage (cycleur catalogue des fiches disque + bouton fiche complète).
                GraphLabel(new Rect(innerX, y, 42f, 18f), "Fiche:");
                {
                    int charIdx = Mathf.Max(0, _availableCharacterFiles.IndexOf(a.CharacterSheetFileName));
                    if (GraphButton(new Rect(innerX + 44f, y, 20f, 18f), "◀"))
                    {
                        charIdx = (charIdx - 1 + _availableCharacterFiles.Count) % _availableCharacterFiles.Count;
                        a.CharacterSheetFileName = charIdx == 0 ? "" : _availableCharacterFiles[charIdx];
                    }
                    string charDisplay = string.IsNullOrEmpty(a.CharacterSheetFileName) ? "(Défaut)" : a.CharacterSheetFileName;
                    GraphLabel(new Rect(innerX + 66f, y, Mathf.Max(40f, innerW - 66f - 22f - 32f), 18f), $"<b>{charDisplay}</b>");
                    GUI.backgroundColor = new Color(0.2f, 0.6f, 1.0f);
                    if (GraphButton(new Rect(innerX + innerW - 20f - 30f, y, 28f, 18f), "📜"))
                    {
                        OpenActorSheet(a);
                    }
                    GUI.backgroundColor = Color.white;
                    if (GraphButton(new Rect(innerX + innerW - 20f, y, 20f, 18f), "▶"))
                    {
                        charIdx = (charIdx + 1) % _availableCharacterFiles.Count;
                        a.CharacterSheetFileName = charIdx == 0 ? "" : _availableCharacterFiles[charIdx];
                    }
                }
                y += 20f;

                GraphLabel(new Rect(innerX, y, 38f, 18f), "Mod:");
                a.ModelPrefabName = GraphTextField(new Rect(innerX + 40f, y, 110f, 18f), a.ModelPrefabName ?? "");
                GraphLabel(new Rect(innerX + 154f, y, 42f, 18f), "Arme:");
                a.EquippedWeaponName = GraphTextField(new Rect(innerX + 198f, y, Mathf.Max(40f, innerW - 198f), 18f), a.EquippedWeaponName ?? "");
                y += 20f;

                // Sorties "Parler à" 💬 : une rangée par interlocuteur (Qui). Le Qui
                // filtre celui qui parle à l'acteur (vide = n'importe qui) ; la cible
                // est une réplique (LineId) ou un nœud (NodeId, enclenché).
                // Drag du port cyan d'une rangée = lier sa cible.
                if (a.TalkEntries == null) a.TalkEntries = new System.Collections.Generic.List<SceneActorTalkEntry>();
                {
                    int talkCount = a.TalkEntries.Count;
                    GraphLabel(new Rect(innerX, y, Mathf.Max(40f, innerW - 30f), 18f), $"💬 Parler à ({talkCount})");
                    GUI.backgroundColor = new Color(0f, 0.75f, 1f);
                    if (GraphButton(new Rect(innerX + innerW - 26f, y, 26f, 18f), "+"))
                        a.TalkEntries.Add(new SceneActorTalkEntry());
                    GUI.backgroundColor = Color.white;
                    y += 20f;

                    for (int ti = 0; ti < a.TalkEntries.Count; ti++)
                    {
                        var te = a.TalkEntries[ti];
                        if (te == null) { a.TalkEntries.RemoveAt(ti); ti--; continue; }
                        GraphLabel(new Rect(innerX, y, 30f, 18f), "Qui:");
                        const float quiW = 104f;
                        te.SpeakerId = GraphTextField(new Rect(innerX + 32f, y, quiW, 18f), te.SpeakerId ?? "");
                        Rect dropBtn = new Rect(innerX + 34f + quiW, y, 20f, 18f);
                        string dropKey = $"actor_talk_{index}_{a.ActorId}_{ti}";
                        if (GraphButton(dropBtn, "▼"))
                        {
                            if (_actorDropKey == dropKey) _actorDropKey = null;
                            else OpenActorDropdown(dropKey, dropBtn, te.SpeakerId ?? "", v => te.SpeakerId = v);
                        }
                        float tgtX = innerX + 34f + quiW + 22f;
                        GraphLabel(new Rect(tgtX, y, 16f, 18f), "→");
                        const float delW = 22f;
                        te.TargetId = GraphTextField(new Rect(tgtX + 18f, y, Mathf.Max(30f, innerW - (tgtX + 18f - innerX) - delW - 2f), 18f), te.TargetId ?? "");
                        if (GraphButton(new Rect(innerX + innerW - delW, y, delW, 18f), "✕"))
                        {
                            a.TalkEntries.RemoveAt(ti);
                            ti--;
                            continue;
                        }
                        y += 20f;

                        // Port de sortie 💬 de la rangée (cyan), sur le flanc droit.
                        Vector2 talkCenter = GetActorTalkOutCenter(a, ti);
                        bool hasTalk = !string.IsNullOrWhiteSpace(te.TargetId);
                        Color col = hasTalk ? TalkPortCol : new Color(TalkPortCol.r, TalkPortCol.g, TalkPortCol.b, 0.35f);
                        Rect talkPort = new Rect(talkCenter.x - 7f, talkCenter.y - 7f, 14f, 14f);
                        DrawGraphSolidRect(talkPort, col);
                        if (GraphPrimaryDown(mouseWorld, talkPort))
                        {
                            _wireDraft = new WireConnectionDraft
                            {
                                IsActive = true,
                                IsActorTalk = true,
                                SourceActor = a,
                                SourceTalkIndex = ti,
                                StartPos = talkCenter
                            };
                            evt.Use();
                        }
                    }
                }

                // Sortie cinématique 🎬 : jouée au spawn différé (fire-and-forget).
                DrawCineLinkRow(ref y, innerX, innerW, a.CinematicIds);
                DrawSoundLinkRow(ref y, innerX, innerW, a.ActorId);

                if (y + 4f <= cardRect.yMax)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.55f);
                    string talkSummary = "";
                    if (a.TalkEntries != null && a.TalkEntries.Count > 0)
                    {
                        var sbits = new System.Collections.Generic.List<string>();
                        for (int sti = 0; sti < a.TalkEntries.Count; sti++)
                        {
                            var ste = a.TalkEntries[sti];
                            if (ste == null || string.IsNullOrWhiteSpace(ste.TargetId)) continue;
                            string who = string.IsNullOrWhiteSpace(ste.SpeakerId) ? "*" : ste.SpeakerId.Trim();
                            sbits.Add($"{who}➔{ste.TargetId.Trim()}");
                            if (sbits.Count >= 3) break;
                        }
                        if (sbits.Count > 0) talkSummary = " · 💬" + string.Join(", ", sbits);
                    }
                    GraphLabel(new Rect(innerX, y, innerW, 18f), $"{a.GetSummary()} · ◀{refLines} ▶{refEvents} ▼{refTriggers}{talkSummary}");
                    GUI.color = Color.white;
                }
            }

            // Port de sortie cinématique 🎬 : flanc droit (magenta). La carte acteur
            // garde son port d'entrée (spawn) à gauche : input + output 🎬 coexistent.
            DrawCineOutPort(new Vector2(cardRect.xMax, cardRect.y + ActorCinePortTop), a.CinematicIds, mouseWorld);
            DrawSoundOutPort(new Vector2(cardRect.x + cardRect.width * 0.5f, cardRect.yMax), a.ActorId, mouseWorld);
        }
        private CharacterSheet ResolveActorSheet(SceneActorSpawnData a, out string sourceLabel)
        {
            sourceLabel = "Fiche par défaut générée";
            if (a == null) return new CharacterSheet();

            if (!string.IsNullOrEmpty(a.CharacterSheetFileName))
            {
                try
                {
                    string charFolder = Path.Combine(Application.persistentDataPath, "Characters");
                    string sheetPath = Path.Combine(charFolder,
                        a.CharacterSheetFileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                            ? a.CharacterSheetFileName
                            : $"{a.CharacterSheetFileName}.json");
                    if (File.Exists(sheetPath))
                    {
                        var loaded = CharacterStorageService.LoadCharacter(sheetPath);
                        if (loaded != null)
                        {
                            // Preview : migre en mémoire les objets hors-catalogue
                            // (sans écrire sur le disque — la sauvegarde reste explicite).
                            ArmoryCatalog.MigrateLegacyScene01Items(loaded);
                            sourceLabel = $"Fichier '{a.CharacterSheetFileName}'";
                            return loaded;
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[ScenarioEditor] Fiche introuvable : '{sheetPath}' — repli embarqué/défaut.");
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ScenarioEditor] Lecture fiche '{a.CharacterSheetFileName}' : {ex.Message}");
                }
            }

            if (a.EmbeddedSheet != null && !string.IsNullOrEmpty(a.EmbeddedSheet.Name))
            {
                try
                {
                    var copy = JsonUtility.FromJson<CharacterSheet>(JsonUtility.ToJson(a.EmbeddedSheet));
                    if (copy != null)
                    {
                        sourceLabel = "Fiche embarquée (JSON scène)";
                        return copy;
                    }
                }
                catch { /* repli défaut ci-dessous */ }
            }

            // Fallback héroïque (sans JSON, miroir du runtime JsonStorySceneController) :
            // "john.json" / "Operative_John" sans fichier disque → fiche héroïque intégrée.
            if (IsJohnActor(a))
            {
                sourceLabel = "Fiche héroïque intégrée [John : Passeur du Creuset]";
                return JohnCharacter.BuildHeroicSheet();
            }

            // Fallback héroïque (sans JSON, miroir du runtime) :
            // "erika.json" / "Cleyan_Erika" sans fichier disque → fiche héroïque intégrée.
            if (IsErikaActor(a))
            {
                sourceLabel = "Fiche héroïque intégrée [Erika de Cleya : Flamme Cleyane]";
                return ErikaCharacter.BuildHeroicSheet();
            }

            // Fallback héroïque (sans JSON, miroir du runtime) :
            // "vance.json" / "Vance" / "Rebel_Commander" / "Commander_Vance" sans fichier disque → fiche héroïque intégrée.
            if (IsVanceActor(a))
            {
                sourceLabel = "Fiche héroïque intégrée [Vance : Autorité du Commandant]";
                return VanceCharacter.BuildHeroicSheet();
            }

            var sheet = new CharacterSheet
            {
                Name = string.IsNullOrEmpty(a.DisplayName) ? a.ActorId : a.DisplayName,
                BaseAttributes = new Attributes(3, 3, 3, 3, 2, 2, 1, 2, 0),
                BaseArmor = a.BaseArmor,
                Profile = a.IsPlayer ? CharacterProfileType.HerosPJ : CharacterProfileType.PnjNormal,
                ModelPrefabName = a.ModelPrefabName ?? ""
            };

            // Arme de la carte : clone catalogue si le nom existe, équivalent officiel
            // si ancien nom hors-catalogue, sinon placeholder (miroir du runtime).
            // Dotation déséquipée ; ajoutée seulement si aucune arme en poche (anti-doublon).
            if (!string.IsNullOrEmpty(a.EquippedWeaponName) && !ArmoryCatalog.SheetHasWeapon(sheet))
            {
                var weapon = ArmoryCatalog.ResolveSceneWeapon(a.EquippedWeaponName, a.ActorId, equipped: false);
                sheet.AddItem(weapon);
                if (sheet.GetSkill(weapon.AssociatedSkill).TrainingLevel == 0)
                    sheet.GetSkill(weapon.AssociatedSkill).TrainingLevel = 1;
            }
            return sheet;
        }

        /// <summary>
        /// Détecte l'acteur John (miroir du runtime) : "john.json", nom ou modèle contenant "John".
        /// </summary>
        private static bool IsJohnActor(SceneActorSpawnData a)
        {
            if (a == null) return false;
            if (!string.IsNullOrEmpty(a.CharacterSheetFileName)
                && a.CharacterSheetFileName.IndexOf("john", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (!string.IsNullOrEmpty(a.DisplayName)
                && a.DisplayName.IndexOf("john", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (!string.IsNullOrEmpty(a.ModelPrefabName)
                && a.ModelPrefabName.IndexOf("john", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (a.EmbeddedSheet != null && JohnCharacter.IsJohn(a.EmbeddedSheet))
                return true;
            return false;
        }

        /// <summary>
        /// Détecte l'actrice Erika (miroir du runtime) : "erika.json", nom ou modèle contenant "Erika".
        /// </summary>
        private static bool IsErikaActor(SceneActorSpawnData a)
        {
            if (a == null) return false;
            if (!string.IsNullOrEmpty(a.CharacterSheetFileName)
                && a.CharacterSheetFileName.IndexOf("erika", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (!string.IsNullOrEmpty(a.DisplayName)
                && a.DisplayName.IndexOf("erika", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (!string.IsNullOrEmpty(a.ModelPrefabName)
                && a.ModelPrefabName.IndexOf("erika", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (a.EmbeddedSheet != null && ErikaCharacter.IsErika(a.EmbeddedSheet))
                return true;
            return false;
        }

        /// <summary>
        /// Détecte le Commandant Vance (miroir du runtime) : "vance.json", nom exact
        /// "Vance"/"Commandant Vance" ("Adjudant de Vance" exclu),
        /// ou modèles exacts "Vance" / "Rebel_Commander" / "Commander_Vance".
        /// </summary>
        private static bool IsVanceActor(SceneActorSpawnData a)
        {
            if (a == null) return false;
            if (!string.IsNullOrEmpty(a.CharacterSheetFileName)
                && a.CharacterSheetFileName.IndexOf("vance", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (!string.IsNullOrEmpty(a.DisplayName)
                && (a.DisplayName.Equals("Vance", StringComparison.OrdinalIgnoreCase)
                    || a.DisplayName.Equals("Commandant Vance", StringComparison.OrdinalIgnoreCase)))
                return true;
            if (!string.IsNullOrEmpty(a.ModelPrefabName)
                && (a.ModelPrefabName.Equals("vance", StringComparison.OrdinalIgnoreCase)
                    || a.ModelPrefabName.Equals("rebel_commander", StringComparison.OrdinalIgnoreCase)
                    || a.ModelPrefabName.Equals("commander_vance", StringComparison.OrdinalIgnoreCase)))
                return true;
            if (a.EmbeddedSheet != null && VanceCharacter.IsVance(a.EmbeddedSheet))
                return true;
            return false;
        }

        private void OpenActorSheet(SceneActorSpawnData a)
        {
            if (a == null) return;
            CharacterSheet sheet = ResolveActorSheet(a, out string source);
            string ctx = $"👥 {a.DisplayName} [{a.ActorId}] · {source} · {a.GetSummary()}";
            CharacterDevWindow.OpenForSheet(sheet, ctx);
        }
        private void DrawCinematicCard(SceneCinematicData cine, int index, Vector2 mouseWorld)
        {
            if (cine == null) return;
            if (string.IsNullOrEmpty(cine.CinematicId)) cine.CinematicId = MakeUniqueCineId();
            cine.Shots ??= new System.Collections.Generic.List<SceneCinematicShotData>();

            Vector2 size = GetCinematicCardSize(cine);
            float cardW = size.x;
            float cardH = size.y;

            Rect cardRect = new Rect(cine.GraphPosX, cine.GraphPosY, cardW, cardH);
            Rect headerRect = new Rect(cardRect.x, cardRect.y, cardW, 26f);
            Rect closeRect = new Rect(headerRect.xMax - 22f, headerRect.y + 3f, 18f, 18f);
            Rect dragHeaderRect = new Rect(headerRect.x, headerRect.y, headerRect.width - 26f, headerRect.height);
            Rect resizeGripRect = new Rect(cardRect.xMax - 14f, cardRect.yMax - 14f, 14f, 14f);

            Event evt = Event.current;
            string dragKey = $"cine_{index}_{cine.CinematicId}";
            string resizeKey = $"resize_{dragKey}";
            int dragControlId = GUIUtility.GetControlID(FocusType.Passive);
            int resizeControlId = GUIUtility.GetControlID(FocusType.Passive);

            bool lateResizeCine = IsLateResizeFor(cine);
            if (GraphPrimaryDown(mouseWorld, resizeGripRect) || lateResizeCine)
            {
                GUIUtility.hotControl = resizeControlId;
                _resizingCardKey = resizeKey;
                if (lateResizeCine) { _lateResizeTarget = null; GUIUtility.keyboardControl = 0; }
                evt.Use();
            }
            else if ((evt.type == EventType.MouseDrag || evt.type == EventType.MouseMove) && GUIUtility.hotControl == resizeControlId && _resizingCardKey == resizeKey)
            {
                cine.CardWidth = Mathf.Clamp(mouseWorld.x - cardRect.x, 300f, 750f);
                cine.CardHeight = Mathf.Clamp(mouseWorld.y - cardRect.y, 120f, 600f);
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == resizeControlId)
            {
                GUIUtility.hotControl = 0;
                _resizingCardKey = null;
                evt.Use();
            }

            bool lateDragCine = IsLateDragFor(cine);
            if (GraphPrimaryDown(mouseWorld, dragHeaderRect) || lateDragCine)
            {
                GUIUtility.hotControl = dragControlId;
                _draggingCardKey = dragKey;
                _dragOffset = (lateDragCine ? _lateDownWorld : mouseWorld) - new Vector2(cine.GraphPosX, cine.GraphPosY);
                if (lateDragCine) { _lateDragTarget = null; GUIUtility.keyboardControl = 0; }
                evt.Use();
            }
            else if ((evt.type == EventType.MouseDrag || evt.type == EventType.MouseMove) && GUIUtility.hotControl == dragControlId && _draggingCardKey == dragKey)
            {
                cine.GraphPosX = mouseWorld.x - _dragOffset.x;
                cine.GraphPosY = mouseWorld.y - _dragOffset.y;
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == dragControlId)
            {
                GUIUtility.hotControl = 0;
                _draggingCardKey = null;
                evt.Use();
            }

            Color headerCol = new Color(0.45f, 0.12f, 0.38f);
            DrawGraphSolidRect(cardRect, new Color(0.12f, 0.06f, 0.10f, 0.95f));
            DrawGraphSolidRect(headerRect, headerCol);
            DrawGraphSolidRect(new Rect(cardRect.x, cardRect.y, cardRect.width, 1f), new Color(1f, 0.35f, 0.85f, 0.9f));
            DrawGraphSolidRect(new Rect(cardRect.x, cardRect.yMax - 1f, cardRect.width, 1f), new Color(1f, 1f, 1f, 0.08f));
            DrawGraphSolidRect(new Rect(cardRect.x, cardRect.y, 1f, cardRect.height), new Color(1f, 1f, 1f, 0.08f));
            DrawGraphSolidRect(new Rect(cardRect.xMax - 1f, cardRect.y, 1f, cardRect.height), new Color(1f, 1f, 1f, 0.08f));

            // Port d'entrée (magenta) : la carte est liée DEPUIS une autre carte. Pas de sortie.
            DrawGraphSolidRect(new Rect(cardRect.x - 7f, cardRect.y + 19f, 14f, 14f), CinePortCol);

            GraphLabel(new Rect(headerRect.x + 8f, headerRect.y + 3f, cardW - 35f, 20f), $"<b>🎬 {cine.Title}</b> [{cine.CinematicId}]");

            GUI.backgroundColor = new Color(0.85f, 0.22f, 0.22f, 1f);
            GUI.color = Color.white;
            if (GraphButton(closeRect, "✕"))
            {
                string doomedId = cine.CinematicId;
                if (_data.Cinematics != null) _data.Cinematics.RemoveAt(index);
                PurgeCinematicReferences(doomedId);
                PurgeSoundReferences(doomedId);
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
                _hoverTitle = $"🎬 {cine.Title}  ({cine.CinematicId})";
                var hb = new System.Text.StringBuilder();
                hb.AppendLine(cine.GetSummary());
                for (int hi = 0; hi < Mathf.Min(6, cine.Shots.Count); hi++)
                {
                    var hs = cine.Shots[hi];
                    if (hs == null) continue;
                    hb.AppendLine($"• {hs.Label} : {hs.GetSummary()}");
                }
                _hoverBody = hb.ToString().Trim();
                _hoverScreenPos = _graphMouseScreenPos;
            }

            float innerX = cardRect.x + 8f;
            float innerW = cardW - 16f;

            if (_zoom < CardLodThreshold)
            {
                DrawGraphSolidRect(new Rect(innerX, cardRect.y + 32f, innerW * 0.85f, 8f), new Color(1f, 0.35f, 0.85f, 0.16f));
                DrawGraphSolidRect(new Rect(innerX, cardRect.y + 44f, innerW * 0.5f, 6f), new Color(1f, 1f, 1f, 0.06f));
            }
            else
            {
                float y = cardRect.y + 30f;

                GraphLabel(new Rect(innerX, y, 24f, 18f), "ID:");
                cine.CinematicId = GraphTextField(new Rect(innerX + 26f, y, 92f, 18f), cine.CinematicId ?? "");
                GraphLabel(new Rect(innerX + 122f, y, 40f, 18f), "Titre:");
                cine.Title = GraphTextField(new Rect(innerX + 164f, y, Mathf.Max(40f, innerW - 164f), 18f), cine.Title ?? "");
                y += 22f;

                cine.Skippable = GraphToggle(new Rect(innerX, y, 96f, 18f), cine.Skippable, "Esc = skip");
                GraphLabel(new Rect(innerX + 100f, y, 44f, 18f), "Vit x");
                string speedTxt = GraphTextField(new Rect(innerX + 146f, y, 40f, 18f), cine.PlaybackSpeed.ToString("0.0"));
                float.TryParse(speedTxt, out cine.PlaybackSpeed);
                cine.PlaybackSpeed = Mathf.Clamp(cine.PlaybackSpeed, 0.1f, 4f);
                GUI.color = new Color(1f, 1f, 1f, 0.6f);
                GraphLabel(new Rect(innerX + 190f, y, Mathf.Max(40f, innerW - 190f), 18f), cine.GetSummary());
                GUI.color = Color.white;
                y += 20f;

                cine.Letterbox = GraphToggle(new Rect(innerX, y, 105f, 18f), cine.Letterbox, "Bandes noires");
                cine.HideSceneChat = GraphToggle(new Rect(innerX + 110f, y, 125f, 18f), cine.HideSceneChat, "Masquer dialogue");
                cine.InPlace = GraphToggle(new Rect(innerX + 240f, y, 95f, 18f), cine.InPlace, "Sur place");
                y += 20f;

                if (!cine.InPlace)
                {
                    GraphLabel(new Rect(innerX, y, 48f, 18f), "Début:");
                    cine.StartTransition = (CinematicCameraTransition)GraphToolbar(new Rect(innerX + 50f, y, 105f, 18f), (int)cine.StartTransition, new[] { "Téléport", "Fluide" });
                    GraphLabel(new Rect(innerX + 162f, y, 32f, 18f), "Fin:");
                    cine.EndTransition = (CinematicCameraTransition)GraphToolbar(new Rect(innerX + 196f, y, 105f, 18f), (int)cine.EndTransition, new[] { "Téléport", "Fluide" });
                }
                else
                {
                    GUI.color = new Color(1f, 0.82f, 0.35f, 0.9f);
                    GraphLabel(new Rect(innerX, y, innerW, 18f), "📷 <b>Sur place :</b> caméra fixe (secousse/effets en direct)");
                    GUI.color = Color.white;
                }
                y += 20f;

                for (int s = 0; s < cine.Shots.Count; s++)
                {
                    var shot = cine.Shots[s];
                    if (shot == null) { cine.Shots.RemoveAt(s); s--; continue; }

                    // Initialisation automatique de l'effet de secousse si la carte est déclarée sur place
                    if (cine.InPlace && shot.MoveEffect == CinematicCameraEffect.None)
                    {
                        shot.MoveEffect = CinematicCameraEffect.HandheldShake;
                        if (shot.ShakeIntensity < 0.05f) shot.ShakeIntensity = 0.25f;
                    }

                    if (GraphButton(new Rect(innerX, y, 22f, 18f), "◉"))
                    {
                        CinematicEditorDevWindow.Open();
                        if (CinematicEditorDevWindow.Instance != null)
                            CinematicEditorDevWindow.Instance.SelectCinematic(cine.CinematicId, s);
                    }
                    shot.Label = GraphTextField(new Rect(innerX + 24f, y, Mathf.Max(40f, innerW - 24f - 134f - 24f), 18f), shot.Label ?? "");
                    GraphLabel(new Rect(innerX + innerW - 134f - 22f, y, 16f, 18f), "s:");
                    string durTxt = GraphTextField(new Rect(innerX + innerW - 134f, y, 36f, 18f), shot.Duration.ToString("0.0"));
                    float.TryParse(durTxt, out shot.Duration);
                    shot.Duration = Mathf.Clamp(shot.Duration, 0.2f, 60f);
                    GraphLabel(new Rect(innerX + innerW - 94f, y, 24f, 18f), "+d:");
                    string delTxt = GraphTextField(new Rect(innerX + innerW - 70f, y, 36f, 18f), shot.StartDelay.ToString("0.0"));
                    float.TryParse(delTxt, out shot.StartDelay);
                    shot.StartDelay = Mathf.Clamp(shot.StartDelay, 0f, 60f);
                    GUI.backgroundColor = new Color(0.85f, 0.25f, 0.25f);
                    if (GraphButton(new Rect(innerX + innerW - 22f, y, 22f, 18f), "✕"))
                    {
                        cine.Shots.RemoveAt(s);
                        GUI.backgroundColor = Color.white;
                        break;
                    }
                    GUI.backgroundColor = Color.white;
                    y += 20f;
                }

                if (cine.Shots.Count == 0 && y + 18f <= cardRect.yMax)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.5f);
                    GraphLabel(new Rect(innerX, y, innerW, 18f), "<i>(aucun plan — + Plan pour commencer)</i>");
                    GUI.color = Color.white;
                }

                // Sortie "fin →" : à la fin de la lecture, enchaîner vers une carte
                // (cinématique, réplique, conséquence, événement ou nœud). Vide = fin simple.
                GraphLabel(new Rect(innerX, y, 62f, 18f), "Fin → :");
                cine.NextTargetId = GraphTextField(new Rect(innerX + 64f, y, Mathf.Max(40f, innerW - 64f - 26f), 18f), cine.NextTargetId ?? "");
                if (!string.IsNullOrEmpty(cine.NextTargetId) && GraphButton(new Rect(innerX + innerW - 22f, y, 22f, 18f), "⊘")) cine.NextTargetId = "";
                y += CineRowH;
                DrawSoundLinkRow(ref y, innerX, innerW, cine.CinematicId);

                float halfBtn = (innerW - 6f) * 0.5f;
                float btnY = Mathf.Min(y, cardRect.yMax - 22f);
                GUI.backgroundColor = new Color(1f, 0.35f, 0.85f);
                if (GraphButton(new Rect(innerX, btnY, halfBtn, 20f), "+ Plan"))
                {
                    int ns = cine.Shots.Count + 1;
                    cine.Shots.Add(new SceneCinematicShotData { ShotId = $"shot_{ns}", Label = $"Plan {ns}" });
                }
                GUI.backgroundColor = new Color(0.7f, 0.25f, 0.6f);
                if (GraphButton(new Rect(innerX + halfBtn + 6f, btnY, halfBtn, 20f), "⤴ Éditer (carte 3D)"))
                {
                    CinematicEditorDevWindow.Open();
                    if (CinematicEditorDevWindow.Instance != null)
                        CinematicEditorDevWindow.Instance.SelectCinematic(cine.CinematicId, Mathf.Max(0, cine.Shots.Count - 1));
                }
                GUI.backgroundColor = Color.white;
            }

            // Port de sortie "fin →" : flanc droit, rangée dédiée (magenta).
            // La carte garde son port d'entrée à gauche : input + output coexistent.
            DrawCineOutPort(GetCinematicOutCenter(cine), null, mouseWorld, cine);
            DrawSoundOutPort(new Vector2(cardRect.x + cardRect.width * 0.5f, cardRect.yMax), cine.CinematicId, mouseWorld);
        }
    }
}

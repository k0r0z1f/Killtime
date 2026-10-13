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

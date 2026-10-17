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

        // Frame de noeud : section details + dessin + drag (ex-Layout.cs, split sans perte).

        private static bool NodeHasCards(SceneNodeData node)
        {
            if (node == null) return false;
            return (node.Dialogues != null && node.Dialogues.Count > 0)
                || (node.Events != null && node.Events.Count > 0)
                || (node.Consequences != null && node.Consequences.Count > 0);
        }
        private bool IsNodeSecOpen(SceneNodeData node)
        {
            if (node == null) return false;
            if (_nodeSecState.TryGetValue(SKey(node, "sec"), out bool v)) return v;
            return !NodeHasCards(node);
        }
        private float GetNodeSectionHeight(SceneNodeData node)
        {
            if (node == null) return 24f;
            if (!IsNodeSecOpen(node)) return 24f;
            float h = 22f; // header ▾
            h += 22f + 22f + 20f + 18f + 54f + 22f + 22f; // id/titre, lieu/type, combat, narr, bouton, next
            h += CineRowH; // rangée sortie 🎬 (DrawCineLinkRow)
            h += 22f; // header choix majeurs
            if (node.Choices != null)
            {
                for (int c = 0; c < node.Choices.Count; c++)
                {
                    if (node.Choices[c] == null) continue;
                    h += 66f; // id/cible, libellé, conséquence
                    h += MeasureEffectsBlock(node.Choices[c].Effects, SectionOpen(_expandedSections, node.Choices[c], "nfx"), true) * 20f;
                }
            }
            h += 22f; // header objectifs
            if (node.Objectives != null)
            {
                for (int o = 0; o < node.Objectives.Count; o++)
                {
                    if (node.Objectives[o] == null) continue;
                    h += 44f; // ligne + détail
                }
            }
            h += MeasureEffectsBlock(node.EnterEffects, SectionOpen(_expandedSections, node, "nenter"), true) * 20f;
            return h;
        }
        private void DrawNodeSection(SceneNodeData node, float secX, float secY, float secW)
        {
            if (node == null) return;
            if (_zoom < CardLodThreshold)
            {
                float totalH = GetNodeSectionHeight(node);
                DrawGraphSolidRect(new Rect(secX, secY, secW, totalH), new Color(0.04f, 0.06f, 0.08f, 0.5f));
                DrawGraphSolidRect(new Rect(secX, secY, secW, 1f), new Color(1f, 1f, 1f, 0.08f));
                GraphLabel(new Rect(secX + 8f, secY + 2f, secW - 16f, 18f), $"<color=grey>⚙ Nœud : {node.Title} [{node.Kind}]</color>");
                return;
            }
            node.Choices ??= new System.Collections.Generic.List<ScenarioChoice>();
            node.Objectives ??= new System.Collections.Generic.List<ScenarioObjective>();
            node.EnterEffects ??= new System.Collections.Generic.List<ScenarioEffect>();
            bool open = IsNodeSecOpen(node);
            string secCineBadge = (node.CinematicIds != null && node.CinematicIds.Count > 0) ? $" 🎬×{node.CinematicIds.Count}" : "";
            if (!open)
            {
                if (GraphButton(new Rect(secX, secY, secW, 20f), $"▸ ⚙ Nœud : {node.Title}{secCineBadge}"))
                    _nodeSecState[SKey(node, "sec")] = true;
                return;
            }
            float y = secY;
            if (GraphButton(new Rect(secX, y, secW, 22f), $"▾ ⚙ Nœud : {node.Title}{secCineBadge}"))
                _nodeSecState[SKey(node, "sec")] = false;
            y += 22f;
            GraphLabel(new Rect(secX, y, 30f, 20f), "ID:");
            node.NodeId = GraphTextField(new Rect(secX + 32f, y, 130f, 20f), node.NodeId ?? "");
            GraphLabel(new Rect(secX + 166f, y, 45f, 20f), "Titre:");
            node.Title = GraphTextField(new Rect(secX + 213f, y, Mathf.Max(60f, secX + secW - secX - 213f), 20f), node.Title ?? "");
            y += 22f;
            GraphLabel(new Rect(secX, y, 45f, 20f), "Lieu:");
            node.Location = GraphTextField(new Rect(secX + 47f, y, 110f, 20f), node.Location ?? "");
            GraphLabel(new Rect(secX + 161f, y, 45f, 20f), "Type:");
            {
                int kindCount = Enum.GetValues(typeof(ScenarioNodeKind)).Length;
                if (GraphButton(new Rect(secX + 208f, y, 20f, 20f), "◀"))
                    node.Kind = (ScenarioNodeKind)(((int)node.Kind - 1 + kindCount) % kindCount);
                GraphLabel(new Rect(secX + 230f, y, 84f, 20f), $"<b>{node.Kind}</b>");
                if (GraphButton(new Rect(secX + 316f, y, 20f, 20f), "▶"))
                    node.Kind = (ScenarioNodeKind)(((int)node.Kind + 1) % kindCount);
            }
            y += 22f;
            node.TriggerCombatOnEnter = GraphToggle(new Rect(secX, y, Mathf.Max(200f, secW), 20f), node.TriggerCombatOnEnter, "⚔ Combat à l'entrée");
            y += 20f;
            GraphLabel(new Rect(secX, y, secW, 18f), "<color=grey>Narration :</color>");
            y += 18f;
            node.Body = GraphTextArea(new Rect(secX, y, secW, 54f), node.Body ?? "");
            y += 54f;
            GraphLabel(new Rect(secX, y, 70f, 20f), "Bouton :");
            node.ContinueLabel = GraphTextField(new Rect(secX + 72f, y, Mathf.Max(60f, secX + secW - secX - 72f), 20f), node.ContinueLabel ?? "");
            y += 22f;
            GraphLabel(new Rect(secX, y, 110f, 20f), "Nœud Suivant :");
            node.NextNodeId = GraphTextField(new Rect(secX + 112f, y, Mathf.Max(60f, secX + secW - secX - 112f), 20f), node.NextNodeId ?? "");
            y += 22f;
            // Sortie cinématique 🎬 du nœud : jouée à l'entrée (fire-and-forget).
            node.CinematicIds = StorySceneData.EnsureCineList(node.CinematicIds);
            DrawCineLinkRow(ref y, secX, secW, node.CinematicIds);
            GUI.backgroundColor = new Color(0.2f, 0.75f, 0.45f);
            if (GraphButton(new Rect(secX + secW - 92f, y, 92f, 20f), "+ Choix"))
                node.Choices.Add(new ScenarioChoice { Id = $"choice_{node.Choices.Count + 1}", Label = "Nouveau choix", NextNodeId = "" });
            GUI.backgroundColor = Color.white;
            GraphLabel(new Rect(secX, y, Mathf.Max(80f, secW - 96f), 20f), $"<b>Choix majeurs ({node.Choices.Count})</b>");
            y += 22f;
            for (int c = 0; c < node.Choices.Count; c++)
            {
                var chx = node.Choices[c];
                if (chx == null) continue;
                chx.Effects ??= new System.Collections.Generic.List<ScenarioEffect>();
                chx.Id = GraphTextField(new Rect(secX, y, 100f, 20f), chx.Id ?? "");
                GraphLabel(new Rect(secX + 102f, y, 20f, 20f), "➔");
                chx.NextNodeId = GraphTextField(new Rect(secX + 124f, y, 100f, 20f), chx.NextNodeId ?? "");
                GUI.backgroundColor = new Color(0.85f, 0.25f, 0.25f);
                if (GraphButton(new Rect(secX + 226f, y, 22f, 20f), "✕"))
                {
                    node.Choices.RemoveAt(c);
                    GUI.backgroundColor = Color.white;
                    break;
                }
                GUI.backgroundColor = Color.white;
                DrawGraphSolidRect(new Rect(secX + secW - 18f, y + 2f, 16f, 16f),
                    chx.Id.Contains("hostile") ? new Color(1.0f, 0.35f, 0.35f) : new Color(0.2f, 0.92f, 0.45f));
                y += 22f;
                GraphLabel(new Rect(secX, y, 70f, 20f), "Libellé :");
                chx.Label = GraphTextField(new Rect(secX + 72f, y, Mathf.Max(60f, secX + secW - secX - 72f), 20f), chx.Label ?? "");
                y += 22f;
                GraphLabel(new Rect(secX, y, 95f, 20f), "Conséquence :");
                chx.Consequence = GraphTextField(new Rect(secX + 97f, y, Mathf.Max(60f, secX + secW - secX - 97f), 20f), chx.Consequence ?? "");
                y += 22f;
                DrawEffectsBlock(ref y, secX + 12f, secW - 12f, chx.Effects, chx, "nfx", "+fx", true);
            }
            GUI.backgroundColor = new Color(0.2f, 0.92f, 0.45f);
            if (GraphButton(new Rect(secX + secW - 100f, y, 100f, 20f), "+ Objectif"))
                node.Objectives.Add(new ScenarioObjective { Id = MakeUniqueObjectiveId(node), Label = "Nouvel objectif...", Optional = false });
            GUI.backgroundColor = Color.white;
            GraphLabel(new Rect(secX, y, Mathf.Max(80f, secW - 104f), 20f), $"<b>Objectifs ({node.Objectives.Count})</b>");
            y += 22f;
            for (int o = 0; o < node.Objectives.Count; o++)
            {
                var obj = node.Objectives[o];
                if (obj == null) continue;
                Color prevObjBg = GUI.backgroundColor;
                GUI.backgroundColor = obj.Optional ? new Color(0.45f, 0.45f, 0.48f) : new Color(0.2f, 0.75f, 1f);
                if (GraphButton(new Rect(secX, y, 56f, 20f), obj.Optional ? "Opt." : "Princ."))
                    obj.Optional = !obj.Optional;
                GUI.backgroundColor = prevObjBg;
                obj.Label = GraphTextField(new Rect(secX + 60f, y, Mathf.Max(40f, secX + secW - secX - 60f - 26f), 20f), obj.Label ?? "");
                GUI.backgroundColor = new Color(0.85f, 0.25f, 0.25f);
                if (GraphButton(new Rect(secX + secW - 22f, y, 22f, 20f), "✕"))
                {
                    node.Objectives.RemoveAt(o);
                    GUI.backgroundColor = Color.white;
                    break;
                }
                GUI.backgroundColor = Color.white;
                y += 22f;
                GraphLabel(new Rect(secX, y, 30f, 20f), "ID:");
                obj.Id = GraphTextField(new Rect(secX + 32f, y, 90f, 20f), obj.Id ?? "");
                GraphLabel(new Rect(secX + 126f, y, 55f, 20f), "Détail:");
                obj.Detail = GraphTextField(new Rect(secX + 183f, y, Mathf.Max(40f, secX + secW - secX - 183f), 20f), obj.Detail ?? "");
                y += 22f;
            }
            DrawEffectsBlock(ref y, secX, secW, node.EnterEffects, node, "nenter", "Effets d'entrée", true);
        }

        private void DrawNodeFrame(SceneNodeData node, int nodeIndex, Vector2 mouseWorld)
        {
            Rect frameRect = ComputeNodeBoundingBox(node);
            Rect headerRect = new Rect(frameRect.x, frameRect.y, frameRect.width, 38f);
            // Zone boutons réelle : ⚙ + 90 + 82 + 95 + 26 + espacements ≈ 350px calée à droite.
            Rect actionsArea = new Rect(headerRect.xMax - 360f, headerRect.y + 5f, 350f, 26f);
            Rect nodeInSocket = new Rect(frameRect.x - 9f, frameRect.y + 10f, 18f, 18f);
            Rect nodeOutSocket = new Rect(frameRect.xMax - 9f, frameRect.y + 10f, 18f, 18f);
            // Port de sortie 🎬 du frame : flanc droit, sous NextNodeId, au-dessus des choix majeurs.
            Rect nodeCinePort = new Rect(frameRect.xMax - 7f, frameRect.yMin + NodeCinePortTop - 7f, 14f, 14f);

            Event evt = Event.current;
            int nodeControlId = GUIUtility.GetControlID(FocusType.Passive);

            // Glissement du Nœud Englobant dans l'espace monde (protection stricte des boutons d'actions
            // ET des ports de sortie : un port reste un port, pas une poignée de déplacement).
            bool isOverHeaderDragZone = headerRect.Contains(mouseWorld) && !actionsArea.Contains(mouseWorld)
                && !nodeOutSocket.Contains(mouseWorld);

            // Double-clic sur la frame = cadrer ce nœud en lisible (raccourci direct vers
            // le focus, sans passer par Aller à / ◀ ▶). Hors ports : double-cliquer un port
            // ne doit ni déplacer ni effacer le lien.
            if (evt.type == EventType.MouseDown && evt.button == 0 && evt.clickCount == 2 && !_pickerCapturesMouse && frameRect.Contains(mouseWorld)
                && !nodeOutSocket.Contains(mouseWorld) && !nodeCinePort.Contains(mouseWorld) && !IsOverNodeChoicePort(frameRect, node, mouseWorld))
            {
                _draggingNodeId = null;
                GUIUtility.hotControl = 0;
                FocusNodeByIndex(nodeIndex);
                evt.Use();
            }
            else if (evt.type == EventType.MouseDown && evt.button == 0 && evt.clickCount < 2 && !_pickerCapturesMouse && isOverHeaderDragZone)
            {
                GUIUtility.hotControl = nodeControlId;
                _draggingNodeId = node.NodeId;
                _dragNodeStartMouse = mouseWorld;
                evt.Use();
            }
            // Prise différée (pré-passe anti-vol) : le MouseDown sur ce header a été
            // avalé car une carte peinte avant recouvrait le point.
            else if (IsLateDragFor(node))
            {
                GUIUtility.hotControl = nodeControlId;
                _draggingNodeId = node.NodeId;
                _dragNodeStartMouse = _lateDownWorld;
                _lateDragTarget = null;
                GUIUtility.keyboardControl = 0;
                evt.Use();
            }
            else if ((evt.type == EventType.MouseDrag || evt.type == EventType.MouseMove) && GUIUtility.hotControl == nodeControlId && _draggingNodeId == node.NodeId)
            {
                Vector2 delta = mouseWorld - _dragNodeStartMouse;
                _dragNodeStartMouse = mouseWorld;

                node.GraphPosX += delta.x;
                node.GraphPosY += delta.y;

                for (int d = 0; d < node.Dialogues.Count; d++)
                {
                    node.Dialogues[d].GraphPosX += delta.x;
                    node.Dialogues[d].GraphPosY += delta.y;
                }
                for (int e = 0; e < node.Events.Count; e++)
                {
                    node.Events[e].GraphPosX += delta.x;
                    node.Events[e].GraphPosY += delta.y;
                }
                if (node.Consequences != null)
                {
                    for (int k = 0; k < node.Consequences.Count; k++)
                    {
                        if (node.Consequences[k] == null) continue;
                        node.Consequences[k].GraphPosX += delta.x;
                        node.Consequences[k].GraphPosY += delta.y;
                    }
                }
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == nodeControlId)
            {
                GUIUtility.hotControl = 0;
                _draggingNodeId = null;
                evt.Use();
            }

            Color frameBorderCol = node.Kind switch
            {
                ScenarioNodeKind.Briefing => new Color(0.0f, 0.85f, 1.0f, 0.75f),
                ScenarioNodeKind.Choice => new Color(1.0f, 0.78f, 0.15f, 0.75f),
                ScenarioNodeKind.Objective => new Color(0.2f, 0.92f, 0.45f, 0.75f),
                ScenarioNodeKind.Resolution => new Color(0.85f, 0.35f, 1.0f, 0.75f),
                _ => new Color(0.5f, 0.5f, 0.5f, 0.75f)
            };

            DrawGraphSolidRect(frameRect, new Color(0.025f, 0.04f, 0.065f, 0.75f));
            DrawGraphSolidRect(headerRect, new Color(frameBorderCol.r, frameBorderCol.g, frameBorderCol.b, 0.25f));

            DrawGraphSolidRect(new Rect(frameRect.x, frameRect.y, frameRect.width, 2.5f), frameBorderCol);
            DrawGraphSolidRect(new Rect(frameRect.x, frameRect.yMax - 1.5f, frameRect.width, 1.5f), frameBorderCol);
            DrawGraphSolidRect(new Rect(frameRect.x, frameRect.y, 1.5f, frameRect.height), frameBorderCol);
            DrawGraphSolidRect(new Rect(frameRect.xMax - 1.5f, frameRect.y, 1.5f, frameRect.height), frameBorderCol);

            DrawGraphSolidRect(nodeInSocket, frameBorderCol);

            GUI.color = Color.white;
            float titleW = Mathf.Max(90f, frameRect.width - 507f);
            GraphLabel(new Rect(headerRect.x + 16f, headerRect.y + 8f, titleW, 22f), $"<b>■ NŒUD #{nodeIndex + 1} : {node.Title.ToUpperInvariant()}</b> [{node.Kind}]");

            GUI.color = new Color(1f, 1f, 1f, 0.75f);
            float idLabelW = Mathf.Max(40f, Mathf.Min(130f, frameRect.width - titleW - 350f));
            GraphLabel(new Rect(headerRect.x + 16f + titleW + 4f, headerRect.y + 8f, idLabelW, 20f), $"ID: <i>{node.NodeId}</i>");
            GUI.color = Color.white;

            float btnY = headerRect.y + 7f;
            float btnH = 22f;
            float bx = headerRect.xMax - 10f;
            Color prevBg = GUI.backgroundColor;
            bool secOpen = IsNodeSecOpen(node);
            if (GraphButton(new Rect(bx - 26f, btnY, 26f, btnH), secOpen ? "⚙▾" : "⚙▸"))
                _nodeSecState[SKey(node, "sec")] = !secOpen;
            bx -= 26f + 6f;
            GUI.backgroundColor = new Color(0.85f, 0.2f, 0.2f);
            Rect delBtn = new Rect(bx - 26f, btnY, 26f, btnH);
            bool deleteNode = GraphButton(delBtn, "✕");
            bx -= 26f + 6f;
            GUI.backgroundColor = new Color(1f, 0.45f, 0.2f);
            Rect evtBtn = new Rect(bx - 95f, btnY, 95f, btnH);
            bool addEvt = GraphButton(evtBtn, "+ Événement");
            bx -= 95f + 6f;
            GUI.backgroundColor = new Color(0.35f, 0.85f, 0.45f);
            Rect consBtn = new Rect(bx - 82f, btnY, 82f, btnH);
            bool addCons = GraphButton(consBtn, "+ Conséq.");
            bx -= 82f + 6f;
            GUI.backgroundColor = new Color(0.2f, 0.75f, 1f);
            Rect dlgBtn = new Rect(bx - 90f, btnY, 90f, btnH);
            bool addDlg = GraphButton(dlgBtn, "+ Dialogue");
            GUI.backgroundColor = prevBg;

            if (addDlg)
            {
                ComputeFreeSpotForNewCard(node, out float freeX, out float freeY);
                node.Dialogues.Add(new SceneDialogueLineData
                {
                    LineId = MakeUniqueLineId(node),
                    SpeakerId = "",
                    Speech = "Nouvelle réplique...",
                    GraphPosX = freeX,
                    GraphPosY = freeY
                });
            }

            if (addEvt)
            {
                ComputeFreeSpotForNewCard(node, out float freeX, out float freeY);
                node.Events.Add(new SceneEventData
                {
                    EventId = MakeUniqueEventId(node),
                    Title = "Action Scénique",
                    GraphPosX = freeX,
                    GraphPosY = freeY
                });
            }

            if (addCons)
            {
                if (node.Consequences == null) node.Consequences = new System.Collections.Generic.List<SceneConsequenceData>();
                ComputeFreeSpotForNewCard(node, out float freeX, out float freeY);
                node.Consequences.Add(new SceneConsequenceData
                {
                    ConsequenceId = MakeUniqueConsId(node),
                    Title = "Récompense",
                    GraphPosX = freeX,
                    GraphPosY = freeY
                });
            }

            if (deleteNode)
            {
                if (node != null)
                {
                    if (node.Dialogues != null)
                        for (int d = 0; d < node.Dialogues.Count; d++)
                            if (node.Dialogues[d] != null) PurgeSoundReferences(node.Dialogues[d].LineId);
                    if (node.Events != null)
                        for (int e = 0; e < node.Events.Count; e++)
                            if (node.Events[e] != null) PurgeSoundReferences(node.Events[e].EventId);
                    if (node.Consequences != null)
                        for (int k = 0; k < node.Consequences.Count; k++)
                            if (node.Consequences[k] != null) PurgeSoundReferences(node.Consequences[k].ConsequenceId);
                }
                _data.Nodes.RemoveAt(nodeIndex);
                return;
            }

            // Sortie principale (NextNodeId) : toujours visible (atténuée si vide),
            // drag vers l'entrée d'un nœud = lier, drop dans le vide = menu créer.
            bool hasNext = !string.IsNullOrEmpty(node.NextNodeId);
            Color nextPortCol = hasNext ? new Color(1.0f, 0.65f, 0.15f) : new Color(1.0f, 0.65f, 0.15f, 0.35f);
            DrawGraphSolidRect(nodeOutSocket, nextPortCol);

            if (evt.clickCount < 2 && GraphPrimaryDown(mouseWorld, nodeOutSocket))
            {
                _wireDraft = new WireConnectionDraft
                {
                    IsActive = true,
                    IsNode = true,
                    SourceNode = node,
                    StartPos = GetNodeMainOutCenter(frameRect)
                };
                evt.Use();
            }

            // Port 🎬 du frame : drag vers une carte cinématique = lier (multi-liens, magenta).
            {
                bool hasCine = node.CinematicIds != null && node.CinematicIds.Count > 0;
                Color cinePortCol = hasCine ? CinePortCol : new Color(CinePortCol.r, CinePortCol.g, CinePortCol.b, 0.35f);
                DrawGraphSolidRect(nodeCinePort, cinePortCol);
                if (evt.clickCount < 2 && GraphPrimaryDown(mouseWorld, nodeCinePort))
                {
                    node.CinematicIds = StorySceneData.EnsureCineList(node.CinematicIds);
                    _wireDraft = new WireConnectionDraft
                    {
                        IsActive = true,
                        IsCinematicLink = true,
                        SourceCineList = node.CinematicIds,
                        StartPos = nodeCinePort.center
                    };
                    evt.Use();
                }
            }

            // Sorties choix majeurs : un port par choix sur le flanc droit, aux mêmes
            // positions que les origines des wires (GetNodeChoiceOutCenter).
            if (node.Choices != null)
            {
                for (int cp = 0; cp < node.Choices.Count; cp++)
                {
                    var chx = node.Choices[cp];
                    if (chx == null) continue;
                    Vector2 choiceCenter = GetNodeChoiceOutCenter(frameRect, cp);
                    Rect choicePort = GetNodeChoicePortRect(frameRect, cp);
                    bool hasChoiceLink = !string.IsNullOrEmpty(chx.NextNodeId);
                    Color baseChoiceCol = chx.Id.Contains("hostile") ? new Color(1.0f, 0.35f, 0.35f) : new Color(0.2f, 0.92f, 0.45f);
                    if (!hasChoiceLink) baseChoiceCol.a = 0.35f;
                    DrawGraphSolidRect(choicePort, baseChoiceCol);

                    if (evt.clickCount < 2 && GraphPrimaryDown(mouseWorld, choicePort))
                    {
                        _wireDraft = new WireConnectionDraft
                        {
                            IsActive = true,
                            IsNodeChoice = true,
                            NodeChoiceIndex = cp,
                            SourceNode = node,
                            StartPos = choiceCenter
                        };
                        evt.Use();
                    }
                }
            }

            // Section détails du nœud (ex-inspecteur + ex-inline) : sous les cartes,
            // incluse dans la bounding box.
            {
                float sectionH = GetNodeSectionHeight(node);
                float secW = frameRect.width - 32f;
                DrawNodeSection(node, frameRect.x + 16f, frameRect.yMax - 14f - sectionH, secW);
            }
        }
    }
}

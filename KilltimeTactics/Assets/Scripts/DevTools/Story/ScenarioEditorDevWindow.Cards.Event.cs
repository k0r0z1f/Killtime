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
        private void DrawEventCard(SceneEventData ev, int index, SceneNodeData node, Vector2 mouseWorld)
        {
            if (string.IsNullOrEmpty(ev.EventId)) ev.EventId = MakeUniqueEventId(node);

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
    }
}

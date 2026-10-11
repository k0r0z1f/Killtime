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
        private void DrawCineLinkRow(ref float y, float x, float w, List<string> list)
        {
            GraphLabel(new Rect(x, y, 26f, 18f), "🎬");
            bool has = list != null && list.Count > 0;
            GUI.color = has ? Color.white : new Color(1f, 1f, 1f, 0.45f);
            GraphLabel(new Rect(x + 28f, y, Mathf.Max(40f, w - 28f - 26f), 18f),
                has ? StorySceneData.GetCineSummary(list) : "(drag port → 🎬)");
            GUI.color = Color.white;
            if (has && GraphButton(new Rect(x + w - 22f, y, 22f, 18f), "⊘")) list.Clear();
            y += CineRowH;
        }
        private void DrawCineOutPort(Vector2 centerWorld, List<string> list, Vector2 mouseWorld, SceneCinematicData cineOwner = null)
        {
            bool has = cineOwner != null
                ? !string.IsNullOrWhiteSpace(cineOwner.NextTargetId)
                : (list != null && list.Count > 0);
            Color col = has ? CinePortCol : new Color(CinePortCol.r, CinePortCol.g, CinePortCol.b, 0.35f);
            Rect port = new Rect(centerWorld.x - 7f, centerWorld.y - 7f, 14f, 14f);
            DrawGraphSolidRect(port, col);
            if (cineOwner == null && list == null) return;
            if (GraphPrimaryDown(mouseWorld, port))
            {
                if (cineOwner != null)
                {
                    _wireDraft = new WireConnectionDraft
                    {
                        IsActive = true,
                        IsCinematicOut = true,
                        SourceCinematic = cineOwner,
                        StartPos = centerWorld
                    };
                }
                else
                {
                    _wireDraft = new WireConnectionDraft
                    {
                        IsActive = true,
                        IsCinematicLink = true,
                        SourceCineList = list,
                        StartPos = centerWorld
                    };
                }
                Event.current.Use();
            }
        }
        private void DrawSoundLinkRow(ref float y, float x, float w, string cardId)
        {
            var linked = _data != null ? _data.FindSoundsFrom(cardId) : null;
            bool has = linked != null && linked.Count > 0;
            GraphLabel(new Rect(x, y, 26f, 18f), "🔊");
            GUI.color = has ? Color.white : new Color(1f, 1f, 1f, 0.45f);
            string summary = "(drag port ↓ → 🔊)";
            if (has)
            {
                var names = new System.Collections.Generic.List<string>(linked.Count);
                for (int i = 0; i < linked.Count && names.Count < 3; i++)
                    if (linked[i] != null && !string.IsNullOrWhiteSpace(linked[i].SoundId)) names.Add(linked[i].SoundId);
                summary = "🔊→ " + string.Join(", ", names);
                if (linked.Count > names.Count) summary += $" (+{linked.Count - names.Count})";
            }
            GraphLabel(new Rect(x + 28f, y, Mathf.Max(40f, w - 28f - 26f), 18f), summary);
            GUI.color = Color.white;
            if (has && GraphButton(new Rect(x + w - 22f, y, 22f, 18f), "⊘"))
            {
                for (int i = 0; i < linked.Count; i++)
                    if (linked[i] != null) linked[i].SourceCardId = "";
            }
            y += SoundRowH;
        }
        private void DrawSoundOutPort(Vector2 centerWorld, string sourceCardId, Vector2 mouseWorld)
        {
            if (string.IsNullOrWhiteSpace(sourceCardId)) return;
            var linked = _data != null ? _data.FindSoundsFrom(sourceCardId) : null;
            bool has = linked != null && linked.Count > 0;
            Color col = has ? SoundPortCol : new Color(SoundPortCol.r, SoundPortCol.g, SoundPortCol.b, 0.35f);
            Rect port = new Rect(centerWorld.x - 7f, centerWorld.y - 7f, 14f, 14f);
            DrawGraphSolidRect(port, col);
            if (GraphPrimaryDown(mouseWorld, port))
            {
                _wireDraft = new WireConnectionDraft
                {
                    IsActive = true,
                    IsSoundLink = true,
                    SourceSoundLinkCardId = sourceCardId,
                    StartPos = centerWorld
                };
                Event.current.Use();
            }
        }
        private static Vector2 GetSoundInCenter(SceneSoundData snd)
        {
            return new Vector2(snd.GraphPosX + GetSoundCardSize(snd).x * 0.5f, snd.GraphPosY);
        }
        private static Vector2 GetSoundStopCenter(SceneSoundData snd)
        {
            var size = GetSoundCardSize(snd);
            return new Vector2(snd.GraphPosX + size.x * 0.5f, snd.GraphPosY + size.y);
        }

        private static Vector2 GetAmbienceInCenter(SceneAmbienceTrackData amb)
        {
            return new Vector2(amb.GraphPosX + GetAmbienceCardSize(amb).x * 0.5f, amb.GraphPosY);
        }

        private static Vector2 GetAmbienceStopCenter(SceneAmbienceTrackData amb)
        {
            var size = GetAmbienceCardSize(amb);
            return new Vector2(amb.GraphPosX + size.x * 0.5f, amb.GraphPosY + size.y);
        }
        private void DrawEntryInputPort(Rect cardRect, Color normalColor, bool isEntry, bool entryError)
        {
            if (!isEntry)
            {
                DrawGraphSolidRect(new Rect(cardRect.x - 7f, cardRect.y + 19f, 14f, 14f), normalColor);
                return;
            }
            if (entryError)
                DrawGraphSolidRect(new Rect(cardRect.x - 12f, cardRect.y + 14f, 24f, 24f), ExitRed);
            DrawGraphSolidRect(new Rect(cardRect.x - 10f, cardRect.y + 16f, 20f, 20f), EntryGreen);
        }
        private static Vector2 GetCinematicOutCenter(SceneCinematicData cine)
        {
            Vector2 s = GetCinematicCardSize(cine);
            return new Vector2(cine.GraphPosX + s.x, cine.GraphPosY + CinePortTopCards);
        }
        private bool TryGetSoundSourceBottom(string cardId, out Vector2 center)
        {
            center = Vector2.zero;
            if (string.IsNullOrWhiteSpace(cardId) || _data == null) return false;
            string want = cardId.Trim();
            if (_data.Nodes != null)
                for (int n = 0; n < _data.Nodes.Count; n++)
                {
                    var node = _data.Nodes[n];
                    if (node == null) continue;
                    if (node.Dialogues != null)
                    {
                        for (int d = 0; d < node.Dialogues.Count; d++)
                        {
                            var line = node.Dialogues[d];
                            if (line == null || !string.Equals(line.LineId, want, System.StringComparison.OrdinalIgnoreCase)) continue;
                            Vector2 s = GetDialogueCardSize(line);
                            center = new Vector2(line.GraphPosX + s.x * 0.5f, line.GraphPosY + s.y);
                            return true;
                        }
                    }
                    if (node.Events != null)
                        for (int e = 0; e < node.Events.Count; e++)
                        {
                            var ev = node.Events[e];
                            if (ev == null || !string.Equals(ev.EventId, want, System.StringComparison.OrdinalIgnoreCase)) continue;
                            Vector2 s = GetEventCardSize(ev);
                            center = new Vector2(ev.GraphPosX + s.x * 0.5f, ev.GraphPosY + s.y);
                            return true;
                        }
                    if (node.Consequences != null)
                        for (int k = 0; k < node.Consequences.Count; k++)
                        {
                            var cons = node.Consequences[k];
                            if (cons == null || !string.Equals(cons.ConsequenceId, want, System.StringComparison.OrdinalIgnoreCase)) continue;
                            Vector2 s = GetConsequenceCardSize(cons);
                            center = new Vector2(cons.GraphPosX + s.x * 0.5f, cons.GraphPosY + s.y);
                            return true;
                        }
                }
            if (_data.Triggers != null)
                for (int t = 0; t < _data.Triggers.Count; t++)
                {
                    var trg = _data.Triggers[t];
                    if (trg == null || !string.Equals(trg.TriggerId, want, System.StringComparison.OrdinalIgnoreCase)) continue;
                    Vector2 s = GetTriggerCardSize(trg);
                    center = new Vector2(trg.GraphPosX + s.x * 0.5f, trg.GraphPosY + s.y);
                    return true;
                }
            if (_data.Interactables != null)
                for (int i = 0; i < _data.Interactables.Count; i++)
                {
                    var it = _data.Interactables[i];
                    if (it == null || !string.Equals(it.InteractableId, want, System.StringComparison.OrdinalIgnoreCase)) continue;
                    Vector2 s = GetInteractableCardSize(it);
                    center = new Vector2(it.GraphPosX + s.x * 0.5f, it.GraphPosY + s.y);
                    return true;
                }
            if (_data.Actors != null)
                for (int a = 0; a < _data.Actors.Count; a++)
                {
                    var ac = _data.Actors[a];
                    if (ac == null || !string.Equals(ac.ActorId, want, System.StringComparison.OrdinalIgnoreCase)) continue;
                    Vector2 s = GetActorCardSize(ac);
                    center = new Vector2(ac.GraphPosX + s.x * 0.5f, ac.GraphPosY + s.y);
                    return true;
                }
            if (_data.Cinematics != null)
                for (int c = 0; c < _data.Cinematics.Count; c++)
                {
                    var cine = _data.Cinematics[c];
                    if (cine == null || !string.Equals(cine.CinematicId, want, System.StringComparison.OrdinalIgnoreCase)) continue;
                    Vector2 s = GetCinematicCardSize(cine);
                    center = new Vector2(cine.GraphPosX + s.x * 0.5f, cine.GraphPosY + s.y);
                    return true;
                }
            return false;
        }

        /// <summary>
        /// Cible d'arrêt (bas de carte) la plus proche du point de drop, tous types
        /// de cartes confondus. Utilisé par les liens d'arrêt son 🔊 (et extensible).
        /// </summary>
        private bool TryPickSoundStopTarget(Vector2 mousePos, out string pickedId)
        {
            // NB : pas de capture du paramètre out dans la fonction locale (CS1628)
            // → on accumule dans bestId puis on assigne à la fin.
            string bestId = null;
            pickedId = null;
            if (_data == null) return false;
            float best = 36f;
            void Consider(string id)
            {
                if (string.IsNullOrWhiteSpace(id)) return;
                if (TryGetSoundSourceBottom(id, out var b) && Vector2.Distance(b, mousePos) < best)
                {
                    best = Vector2.Distance(b, mousePos);
                    bestId = id;
                }
            }
            if (_data.Nodes != null)
                for (int n = 0; n < _data.Nodes.Count; n++)
                {
                    var nd = _data.Nodes[n];
                    if (nd == null) continue;
                    if (nd.Dialogues != null)
                        for (int d = 0; d < nd.Dialogues.Count; d++)
                            if (nd.Dialogues[d] != null) Consider(nd.Dialogues[d].LineId);
                    if (nd.Events != null)
                        for (int e = 0; e < nd.Events.Count; e++)
                            if (nd.Events[e] != null) Consider(nd.Events[e].EventId);
                    if (nd.Consequences != null)
                        for (int k = 0; k < nd.Consequences.Count; k++)
                            if (nd.Consequences[k] != null) Consider(nd.Consequences[k].ConsequenceId);
                }
            if (_data.Triggers != null)
                for (int t = 0; t < _data.Triggers.Count; t++)
                    if (_data.Triggers[t] != null) Consider(_data.Triggers[t].TriggerId);
            if (_data.Interactables != null)
                for (int i = 0; i < _data.Interactables.Count; i++)
                    if (_data.Interactables[i] != null) Consider(_data.Interactables[i].InteractableId);
            if (_data.Actors != null)
                for (int a = 0; a < _data.Actors.Count; a++)
                    if (_data.Actors[a] != null) Consider(_data.Actors[a].ActorId);
            if (_data.Cinematics != null)
                for (int c = 0; c < _data.Cinematics.Count; c++)
                    if (_data.Cinematics[c] != null) Consider(_data.Cinematics[c].CinematicId);
            pickedId = bestId;
            return !string.IsNullOrEmpty(pickedId);
        }

        private static string ActorTalkSocketKey(string actorId, int entryIndex)
        {
            return $"{actorId}#{entryIndex}";
        }
        private static void SetActorTalkTarget(SceneActorSpawnData a, int entryIndex, string targetId)
        {
            if (a?.TalkEntries == null || entryIndex < 0 || entryIndex >= a.TalkEntries.Count) return;
            var te = a.TalkEntries[entryIndex];
            if (te == null) return;
            te.TargetId = targetId ?? "";
        }
        private static Vector2 GetActorTalkOutCenter(SceneActorSpawnData a, int entryIndex)
        {
            Vector2 s = GetActorCardSize(a);
            return new Vector2(a.GraphPosX + s.x, a.GraphPosY + ActorTalkFirstRowTop + entryIndex * ActorTalkRowH + 9f);
        }
        private static Vector2 GetNodeMainOutCenter(Rect nodeBox)
        {
            return new Vector2(nodeBox.xMax, nodeBox.yMin + 18f);
        }

        private static Vector2 GetNodeChoiceOutCenter(Rect nodeBox, int choiceIndex)
        {
            return new Vector2(nodeBox.xMax, nodeBox.yMin + 60f + (choiceIndex * 36f) + 14f);
        }

        private static Rect GetNodeChoicePortRect(Rect nodeBox, int choiceIndex)
        {
            Vector2 c = GetNodeChoiceOutCenter(nodeBox, choiceIndex);
            return new Rect(c.x - 7f, c.y - 7f, 14f, 14f);
        }

        private static bool IsOverNodeChoicePort(Rect nodeBox, SceneNodeData node, Vector2 mouseWorld)
        {
            if (node == null || node.Choices == null) return false;
            for (int c = 0; c < node.Choices.Count; c++)
            {
                if (node.Choices[c] == null) continue;
                if (GetNodeChoicePortRect(nodeBox, c).Contains(mouseWorld)) return true;
            }
            return false;
        }

        private int FindNodeIndex(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId) || _data == null || _data.Nodes == null) return -1;
            return _data.Nodes.FindIndex(n => n != null && string.Equals(n.NodeId, nodeId, System.StringComparison.OrdinalIgnoreCase));
        }
        private struct OffscreenLinkTag
        {
            public string TargetNodeId;
            public int TargetNodeIndex;
            public string Label;
            public Color Color;
            public Vector2 WorldPos;
            public Rect ScreenRect;
        }

        private void CollectOffscreenLinkTags(Rect canvasRect)
        {
            _offscreenTags.Clear();
            if (_data == null || _data.Nodes == null || _pickerCapturesMouse) return;
            Rect view = GetVisibleWorldRect(canvasRect);
            float zoom = Mathf.Max(0.0001f, _zoom);
            float mX = 100f / zoom;
            float mY = 30f / zoom;

            void TryAddTag(Rect sourceRect, Vector2 targetCenter, string targetId, string label, Color col)
            {
                if (string.IsNullOrEmpty(targetId)) return;
                if (!sourceRect.Overlaps(view)) return;
                if (view.Contains(targetCenter)) return;
                int idx = FindNodeIndex(targetId);
                if (idx < 0) return;
                float cx = (view.xMin + mX < view.xMax - mX)
                    ? Mathf.Clamp(targetCenter.x, view.xMin + mX, view.xMax - mX)
                    : view.center.x;
                float cy = (view.yMin + mY < view.yMax - mY)
                    ? Mathf.Clamp(targetCenter.y, view.yMin + mY, view.yMax - mY)
                    : view.center.y;
                Vector2 worldPos = new Vector2(cx, cy);
                Vector2 screen = new Vector2(canvasRect.x, canvasRect.y) + GraphPoint(worldPos);
                const float tagW = 160f;
                const float tagH = 22f;
                float tx = Mathf.Clamp(screen.x - tagW * 0.5f, canvasRect.x + 6f, canvasRect.xMax - 6f - tagW);
                float ty = Mathf.Clamp(screen.y - tagH * 0.5f, canvasRect.y + 6f, canvasRect.yMax - 6f - tagH);
                _offscreenTags.Add(new OffscreenLinkTag
                {
                    TargetNodeId = targetId,
                    TargetNodeIndex = idx,
                    Label = label,
                    Color = col,
                    WorldPos = worldPos,
                    ScreenRect = new Rect(tx, ty, tagW, tagH)
                });
            }

            Vector2 NodeTargetCenter(string targetId, out bool found)
            {
                found = false;
                var target = _data.FindNode(targetId);
                if (target == null) return Vector2.zero;
                found = true;
                return ComputeNodeBoundingBox(target).center;
            }

            for (int n = 0; n < _data.Nodes.Count; n++)
            {
                var node = _data.Nodes[n];
                if (node == null) continue;
                Rect nodeBox = ComputeNodeBoundingBox(node);
                if (!string.IsNullOrEmpty(node.NextNodeId))
                {
                    Vector2 c = NodeTargetCenter(node.NextNodeId, out bool ok);
                    if (ok) TryAddTag(nodeBox, c, node.NextNodeId, $"■→ {node.NextNodeId}", new Color(1.0f, 0.6f, 0.1f));
                }
                if (node.Choices != null)
                {
                    for (int c = 0; c < node.Choices.Count; c++)
                    {
                        var ch = node.Choices[c];
                        if (ch == null || string.IsNullOrEmpty(ch.NextNodeId)) continue;
                        Vector2 cc = NodeTargetCenter(ch.NextNodeId, out bool ok);
                        if (!ok) continue;
                        Color wcol = ch.Id.Contains("hostile") ? new Color(1.0f, 0.35f, 0.35f) : new Color(0.2f, 0.92f, 0.45f);
                        TryAddTag(nodeBox, cc, ch.NextNodeId, $"◆→ {ch.NextNodeId}", wcol);
                    }
                }
            }

            if (_data.Triggers != null)
            {
                for (int t = 0; t < _data.Triggers.Count; t++)
                {
                    var trg = _data.Triggers[t];
                    if (trg == null || string.IsNullOrEmpty(trg.TargetNodeId)) continue;
                    Vector2 c = NodeTargetCenter(trg.TargetNodeId, out bool ok);
                    if (!ok) continue;
                    Vector2 ts = GetTriggerCardSize(trg);
                    TryAddTag(new Rect(trg.GraphPosX, trg.GraphPosY, ts.x, ts.y), c, trg.TargetNodeId, $"▼→ {trg.TargetNodeId}", new Color(1.0f, 0.85f, 0.25f));
                }
            }

            if (_data.Interactables != null)
            {
                for (int i = 0; i < _data.Interactables.Count; i++)
                {
                    var it = _data.Interactables[i];
                    if (it == null || string.IsNullOrEmpty(it.TriggerNodeId)) continue;
                    Vector2 c = NodeTargetCenter(it.TriggerNodeId, out bool ok);
                    if (!ok) continue;
                    Vector2 ins = GetInteractableCardSize(it);
                    TryAddTag(new Rect(it.GraphPosX, it.GraphPosY, ins.x, ins.y), c, it.TriggerNodeId, $"🔧→ {it.TriggerNodeId}", new Color(0.2f, 0.95f, 0.85f));
                }
            }
        }

        private void DrawOffscreenLinkTags()
        {
            if (_offscreenTags.Count == 0 || _pickerCapturesMouse) return;
            bool quiet = _wireDraft.IsActive;
            for (int i = 0; i < _offscreenTags.Count; i++)
            {
                var tag = _offscreenTags[i];
                DrawSolidRect(tag.ScreenRect, new Color(0.02f, 0.04f, 0.07f, 0.92f));
                DrawSolidRect(new Rect(tag.ScreenRect.x, tag.ScreenRect.y, tag.ScreenRect.width, 2f), tag.Color);
                if (quiet)
                {
                    GUI.Label(new Rect(tag.ScreenRect.x + 6f, tag.ScreenRect.y + 2f, tag.ScreenRect.width - 12f, tag.ScreenRect.height - 4f), $"<b>{tag.Label}</b>");
                }
                else if (GUI.Button(tag.ScreenRect, tag.Label))
                {
                    FocusNodeByIndex(tag.TargetNodeIndex);
                    Event.current.Use();
                    return;
                }
            }
        }
        private void DrawCineWireList(Vector2 from, List<string> ids)
        {
            if (ids == null || ids.Count == 0) return;
            for (int i = 0; i < ids.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(ids[i])) continue;
                if (_cachedCinematicInputSockets.TryGetValue(ids[i], out var targetIn))
                    DrawBezierWire(from, targetIn, new Color(1f, 0.35f, 0.85f, 0.95f), 3f);
            }
        }

        private void DrawGlobalSceneWires(Rect visibleWorld)
        {
            for (int n = 0; n < _data.Nodes.Count; n++)
            {
                var node = _data.Nodes[n];
                Rect nodeBox = ComputeNodeBoundingBox(node);

                // Sortie 🎬 du frame de nœud : flanc droit, sous NextNodeId, au-dessus des choix.
                DrawCineWireList(new Vector2(nodeBox.xMax, nodeBox.yMin + NodeCinePortTop), node.CinematicIds);

                if (!string.IsNullOrEmpty(node.NextNodeId)
                    && _cachedNodeInputSockets.TryGetValue(node.NextNodeId, out var targetNodeIn))
                {
                    DrawBezierWire(GetNodeMainOutCenter(nodeBox), targetNodeIn, new Color(1.0f, 0.6f, 0.1f, 0.95f), 4.5f);
                }

                for (int c = 0; c < node.Choices.Count; c++)
                {
                    var choice = node.Choices[c];
                    if (!string.IsNullOrEmpty(choice.NextNodeId)
                        && _cachedNodeInputSockets.TryGetValue(choice.NextNodeId, out var choiceNodeIn))
                    {
                        Vector2 choiceOut = GetNodeChoiceOutCenter(nodeBox, c);
                        Color choiceWireCol = choice.Id.Contains("hostile") ? new Color(1.0f, 0.35f, 0.35f, 0.95f) : new Color(0.2f, 0.92f, 0.45f, 0.95f);
                        DrawBezierWire(choiceOut, choiceNodeIn, choiceWireCol, 3.5f);
                    }
                }

                // Wires Nœud → Acteur (apparition différée : SpawnOnNodeId) : la source
                // est le flanc droit du nœud (yMin+34, sous le wire NextNodeId), la cible
                // le port d'entrée de la carte acteur. Couleur faction si effectif,
                // gris atténué si l'acteur spawn déjà à l'initiale (SpawnOnNodeId redondant).
                if (_data.Actors != null && !string.IsNullOrEmpty(node.NodeId))
                {
                    for (int ai = 0; ai < _data.Actors.Count; ai++)
                    {
                        var a = _data.Actors[ai];
                        if (a == null || string.IsNullOrEmpty(a.SpawnOnNodeId)) continue;
                        if (!string.Equals(a.SpawnOnNodeId, node.NodeId, System.StringComparison.OrdinalIgnoreCase)) continue;
                        if (!_cachedActorInSockets.TryGetValue(a.ActorId, out var actorIn)) continue;
                        Vector2 actorOut = new Vector2(nodeBox.xMax, nodeBox.yMin + 34f);
                        Color actorWireCol = !a.SpawnInitially
                            ? (a.IsPlayer ? new Color(0.0f, 0.85f, 1.0f, 0.95f) : new Color(1.0f, 0.4f, 0.4f, 0.95f))
                            : new Color(0.6f, 0.6f, 0.6f, 0.5f);
                        DrawBezierWire(actorOut, actorIn, actorWireCol, 2.5f);
                    }
                }

                DrawConnectedGraphWires(node, visibleWorld);
            }

            if (_data.Triggers != null)
            {
                for (int t = 0; t < _data.Triggers.Count; t++)
                {
                    var trg = _data.Triggers[t];
                    if (trg == null) continue;
                    if (!string.IsNullOrEmpty(trg.TargetNodeId)
                        && _cachedTriggerOutSockets.TryGetValue(trg.TriggerId, out var trgOut)
                        && _cachedNodeInputSockets.TryGetValue(trg.TargetNodeId, out var targetIn))
                    {
                        DrawBezierWire(trgOut, targetIn, new Color(1.0f, 0.85f, 0.25f, 0.95f), 3.5f);
                    }
                    // Sortie 🎬 du déclencheur : flanc droit, sous la sortie principale.
                    Vector2 tsize = GetTriggerCardSize(trg);
                    DrawCineWireList(new Vector2(trg.GraphPosX + tsize.x, trg.GraphPosY + CinePortTopCards), trg.CinematicIds);
                }
            }

            // Wires Interactables → Nœud cible (TriggerNodeId) : teal distinct des triggers.
            if (_data.Interactables != null)
            {
                for (int i = 0; i < _data.Interactables.Count; i++)
                {
                    var it = _data.Interactables[i];
                    if (it == null) continue;
                    if (!string.IsNullOrEmpty(it.TriggerNodeId)
                        && _cachedInteractableOutSockets.TryGetValue(it.InteractableId, out var itOut)
                        && _cachedNodeInputSockets.TryGetValue(it.TriggerNodeId, out var targetIn))
                    {
                        DrawBezierWire(itOut, targetIn, new Color(0.2f, 0.95f, 0.85f, 0.95f), 3.5f);
                    }
                    Vector2 isize = GetInteractableCardSize(it);
                    DrawCineWireList(new Vector2(it.GraphPosX + isize.x, it.GraphPosY + CinePortTopCards), it.CinematicIds);
                }
            }

            // Wires Acteurs → Cinématiques (sortie 🎬 : flanc droit de la carte acteur).
            if (_data.Actors != null)
            {
                for (int i = 0; i < _data.Actors.Count; i++)
                {
                    var a = _data.Actors[i];
                    if (a == null) continue;
                    Vector2 asize = GetActorCardSize(a);
                    DrawCineWireList(new Vector2(a.GraphPosX + asize.x, a.GraphPosY + ActorCinePortTop), a.CinematicIds);
                    // Wires sorties "Parler à" 💬 (TalkEntries) vers réplique (cyan)
                    // ou nœud enclenché (cyan).
                    if (a.TalkEntries != null)
                    {
                        for (int ti = 0; ti < a.TalkEntries.Count; ti++)
                        {
                            var te = a.TalkEntries[ti];
                            if (te == null || string.IsNullOrWhiteSpace(te.TargetId)) continue;
                            string tid = te.TargetId.Trim();
                            if (!_cachedActorTalkOutSockets.TryGetValue(ActorTalkSocketKey(a.ActorId, ti), out var talkOut)) continue;
                            if (_cachedInputSockets.TryGetValue(tid, out var talkIn))
                                DrawBezierWire(talkOut, talkIn, new Color(0f, 0.85f, 1f, 0.95f), 3f);
                            else if (_cachedNodeInputSockets.TryGetValue(tid, out var nodeIn))
                                DrawBezierWire(talkOut, nodeIn, new Color(0f, 0.85f, 1f, 0.95f), 3f);
                        }
                    }
                }
            }

            // Wires "fin →" des cartes 🎬 vers leur carte cible (cinématique, réplique,
            // conséquence, événement ou nœud). Magenta comme les entrées 🎬.
            if (_data.Cinematics != null)
            {
                for (int i = 0; i < _data.Cinematics.Count; i++)
                {
                    var c = _data.Cinematics[i];
                    if (c == null || string.IsNullOrWhiteSpace(c.NextTargetId)) continue;
                    string target = c.NextTargetId.Trim();
                    Vector2 from = GetCinematicOutCenter(c);
                    if (_cachedCinematicInputSockets.TryGetValue(target, out var cineIn))
                        DrawBezierWire(from, cineIn, new Color(1f, 0.35f, 0.85f, 0.95f), 3f);
                    else if (_cachedInputSockets.TryGetValue(target, out var cardIn))
                        DrawBezierWire(from, cardIn, new Color(1f, 0.35f, 0.85f, 0.95f), 3f);
                    else if (_cachedNodeInputSockets.TryGetValue(target, out var nodeIn))
                        DrawBezierWire(from, nodeIn, new Color(1f, 0.35f, 0.85f, 0.95f), 3f);
                }
            }

            // Wires son 🔊 : du BAS des cartes sources vers l'entrée (haut) des
            // cartes son. Ambre. Les liens pendants (source introuvable) sont ignorés.
            if (_data.Sounds != null)
            {
                for (int i = 0; i < _data.Sounds.Count; i++)
                {
                    var snd = _data.Sounds[i];
                    if (snd == null || string.IsNullOrWhiteSpace(snd.SourceCardId)) continue;
                    if (!TryGetSoundSourceBottom(snd.SourceCardId, out var from)) continue;
                    if (!_cachedSoundInputSockets.TryGetValue(snd.SoundId, out var sndIn)) continue;
                    DrawBezierWire(from, sndIn, new Color(1f, 0.6f, 0.1f, 0.95f), 3f, true);
                }
                // Wires d'ARRÊT son 🔊 : du BAS de la carte son vers le BAS de la
                // carte cible. Rouge. Miroir de l'arrêt d'ambiance.
                for (int i = 0; i < _data.Sounds.Count; i++)
                {
                    var snd = _data.Sounds[i];
                    if (snd == null || string.IsNullOrWhiteSpace(snd.StopSourceCardId)) continue;
                    if (!TryGetSoundSourceBottom(snd.StopSourceCardId, out var stopTarget)) continue;
                    if (!_cachedSoundStopSockets.TryGetValue(snd.SoundId, out var sndStopOut)) continue;
                    DrawBezierWire(sndStopOut, stopTarget, new Color(0.95f, 0.3f, 0.3f, 0.95f), 3f, true);
                }
            }

            // Wires ambiance 🌌 :
            // 1. Démarrage : du BAS de la carte source vers le HAUT de la carte ambiance.
            // 2. Arrêt : du BAS de la carte ambiance vers le BAS de la carte d'extinction.
            if (_data.Ambiences != null)
            {
                for (int i = 0; i < _data.Ambiences.Count; i++)
                {
                    var amb = _data.Ambiences[i];
                    if (amb == null) continue;
                    if (!string.IsNullOrWhiteSpace(amb.StartSourceCardId)
                        && TryGetSoundSourceBottom(amb.StartSourceCardId, out var startFrom)
                        && _cachedAmbienceInputSockets.TryGetValue(amb.AmbienceId, out var ambIn))
                    {
                        DrawBezierWire(startFrom, ambIn, new Color(0.18f, 0.88f, 0.72f, 0.95f), 3.2f, true);
                    }
                    if (!string.IsNullOrWhiteSpace(amb.StopSourceCardId)
                        && TryGetSoundSourceBottom(amb.StopSourceCardId, out var stopTarget)
                        && _cachedAmbienceStopSockets.TryGetValue(amb.AmbienceId, out var ambStopOut))
                    {
                        DrawBezierWire(ambStopOut, stopTarget, new Color(0.95f, 0.35f, 0.55f, 0.95f), 3.2f, true);
                    }
                }
            }
        }

        private bool SceneContainsLine(string id)
        {
            if (string.IsNullOrEmpty(id) || _data == null || _data.Nodes == null) return false;
            for (int n = 0; n < _data.Nodes.Count; n++)
            {
                var node = _data.Nodes[n];
                if (node == null || node.Dialogues == null) continue;
                for (int d = 0; d < node.Dialogues.Count; d++)
                {
                    var line = node.Dialogues[d];
                    if (line != null && string.Equals(line.LineId, id, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            return false;
        }

        private bool SceneContainsConsequence(string id)
        {
            if (string.IsNullOrEmpty(id) || _data == null || _data.Nodes == null) return false;
            for (int n = 0; n < _data.Nodes.Count; n++)
            {
                var node = _data.Nodes[n];
                if (node == null || node.Consequences == null) continue;
                for (int k = 0; k < node.Consequences.Count; k++)
                {
                    var cons = node.Consequences[k];
                    if (cons != null && string.Equals(cons.ConsequenceId, id, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            return false;
        }

        private void FinalizeGlobalWireDraft(Vector2 mousePos, Vector2 mouseScreenPos)
        {
            // Cible nœud la plus proche du drop (auto-lien exclu), sinon tag de bord
            // hors-champ sous le curseur. Faux si rien d'acceptable sous la souris.
            bool TryPickNodeTarget(Vector2 at, string selfId, out string picked)
            {
                picked = null;
                float best = 36f;
                foreach (var kvp in _cachedNodeInputSockets)
                {
                    if (string.Equals(kvp.Key, selfId, System.StringComparison.OrdinalIgnoreCase)) continue;
                    float dist = Vector2.Distance(kvp.Value, at);
                    if (dist < best)
                    {
                        best = dist;
                        picked = kvp.Key;
                    }
                }
                if (!string.IsNullOrEmpty(picked)) return true;
                for (int i = 0; i < _offscreenTags.Count; i++)
                {
                    var tag = _offscreenTags[i];
                    if (string.Equals(tag.TargetNodeId, selfId, System.StringComparison.OrdinalIgnoreCase)) continue;
                    if (tag.ScreenRect.Contains(_graphMouseScreenPos))
                    {
                        picked = tag.TargetNodeId;
                        return true;
                    }
                }
                return false;
            }

            void PlayWireSoundLocal()
            {
                if (Killtime.Audio.KilltimeAudioManager.Instance != null)
                {
                    Killtime.Audio.KilltimeAudioManager.Instance.PlayUI(Killtime.Audio.SoundId.UI_Filter, 0.6f);
                }
            }

            bool IsClickWithoutDrag()
            {
                return Vector2.Distance(mousePos, _wireDraft.StartPos) * _zoom <= 6f;
            }

            // Drop dans le vide (style éditeur moderne) : ouvre le menu contextuel de
            // création rapide au lieu d'effacer / d'ignorer. Simple clic = annulation.
            void OpenMenuForVoid()
            {
                if (IsClickWithoutDrag()) return;
                OpenQuickCreateMenu(_wireDraft, mousePos, mouseScreenPos);
            }

            // Carte → cinématique 🎬 : drop sur l'entrée d'une carte cinématique =
            // ajouter le lien (multi-liens autorisés), drop dans le vide = menu
            // contextuel (créer une 🎬 et lier). On n'efface jamais d'un simple raté.
            if (_wireDraft.IsCinematicLink && _wireDraft.SourceCineList != null)
            {
                float bestCine = 34f;
                string pickedCine = null;
                foreach (var kvp in _cachedCinematicInputSockets)
                {
                    float dist = Vector2.Distance(kvp.Value, mousePos);
                    if (dist < bestCine)
                    {
                        bestCine = dist;
                        pickedCine = kvp.Key;
                    }
                }
                if (!string.IsNullOrEmpty(pickedCine))
                {
                    StorySceneData.AddCineLink(_wireDraft.SourceCineList, pickedCine);
                    PlayWireSoundLocal();
                }
                else
                {
                    OpenMenuForVoid();
                }
                return;
            }

            // Branchement son 🔊 : drop sur l'entrée d'une carte son (ou sur son corps) = la lier à la
            // carte source (SourceCardId) ; drop dans le vide = créer une carte son
            // branchée ici (position du drop). On n'efface jamais d'un simple raté.
            if (_wireDraft.IsSoundLink && !string.IsNullOrWhiteSpace(_wireDraft.SourceSoundLinkCardId))
            {
                if (IsClickWithoutDrag()) return;
                string srcId = _wireDraft.SourceSoundLinkCardId;
                float bestSnd = 34f;
                string pickedSnd = null;
                foreach (var kvp in _cachedSoundInputSockets)
                {
                    float dist = Vector2.Distance(kvp.Value, mousePos);
                    if (dist < bestSnd)
                    {
                        bestSnd = dist;
                        pickedSnd = kvp.Key;
                    }
                }
                if (string.IsNullOrEmpty(pickedSnd) && _data != null && _data.Sounds != null)
                {
                    for (int s = 0; s < _data.Sounds.Count; s++)
                    {
                        var sc = _data.Sounds[s];
                        if (sc == null) continue;
                        Vector2 sz = GetSoundCardSize(sc);
                        if (new Rect(sc.GraphPosX, sc.GraphPosY, sz.x, sz.y).Contains(mousePos))
                        {
                            pickedSnd = sc.SoundId;
                            break;
                        }
                    }
                }
                if (!string.IsNullOrEmpty(pickedSnd))
                {
                    var snd = _data != null ? _data.FindSound(pickedSnd) : null;
                    if (snd != null) snd.SourceCardId = srcId;
                    PlayWireSoundLocal();
                }
                else
                {
                    // Possibilité de brancher l'entrée d'une carte ambiance depuis le bas d'une carte source
                    float bestAmb = 34f;
                    string pickedAmb = null;
                    foreach (var kvp in _cachedAmbienceInputSockets)
                    {
                        float dist = Vector2.Distance(kvp.Value, mousePos);
                        if (dist < bestAmb)
                        {
                            bestAmb = dist;
                            pickedAmb = kvp.Key;
                        }
                    }
                    if (!string.IsNullOrEmpty(pickedAmb))
                    {
                        var amb = _data != null ? _data.FindAmbience(pickedAmb) : null;
                        if (amb != null) amb.StartSourceCardId = srcId;
                        PlayWireSoundLocal();
                        return;
                    }

                    if (_data != null)
                    {
                        _data.Sounds ??= new System.Collections.Generic.List<SceneSoundData>();
                        int sndIdx = _data.Sounds.Count + 1;
                        var created = new SceneSoundData
                        {
                            // Unicité garantie (les suppressions rendent Count+1 réutilisable).
                            SoundId = MakeUniqueSoundId(),
                            Title = $"Son {sndIdx}",
                            SourceCardId = srcId,
                            GraphPosX = mousePos.x - SndCardDefaultW * 0.5f,
                            GraphPosY = mousePos.y - 30f
                        };
                        _data.Sounds.Add(created);
                        PlayWireSoundLocal();
                    }
                }
                return;
            }

            // Liaison arrêt ambiance 🌌 : depuis le bas de la carte ambiance vers le bas d'une carte cible
            if (_wireDraft.IsAmbienceStopLink && _wireDraft.SourceAmbienceTrack != null)
            {
                if (IsClickWithoutDrag()) return;
                var amb = _wireDraft.SourceAmbienceTrack;
                string pickedTarget = null;
                float bestStopDist = 36f;

                if (_data.Nodes != null)
                {
                    for (int n = 0; n < _data.Nodes.Count; n++)
                    {
                        var nd = _data.Nodes[n];
                        if (nd == null) continue;
                        if (nd.Dialogues != null)
                            for (int d = 0; d < nd.Dialogues.Count; d++)
                            {
                                var l = nd.Dialogues[d];
                                if (l != null && TryGetSoundSourceBottom(l.LineId, out var b) && Vector2.Distance(b, mousePos) < bestStopDist)
                                { bestStopDist = Vector2.Distance(b, mousePos); pickedTarget = l.LineId; }
                            }
                        if (nd.Events != null)
                            for (int e = 0; e < nd.Events.Count; e++)
                            {
                                var ev = nd.Events[e];
                                if (ev != null && TryGetSoundSourceBottom(ev.EventId, out var b) && Vector2.Distance(b, mousePos) < bestStopDist)
                                { bestStopDist = Vector2.Distance(b, mousePos); pickedTarget = ev.EventId; }
                            }
                        if (nd.Consequences != null)
                            for (int k = 0; k < nd.Consequences.Count; k++)
                            {
                                var cs = nd.Consequences[k];
                                if (cs != null && TryGetSoundSourceBottom(cs.ConsequenceId, out var b) && Vector2.Distance(b, mousePos) < bestStopDist)
                                { bestStopDist = Vector2.Distance(b, mousePos); pickedTarget = cs.ConsequenceId; }
                            }
                    }
                }

                if (!string.IsNullOrEmpty(pickedTarget))
                {
                    amb.StopSourceCardId = pickedTarget;
                    PlayWireSoundLocal();
                }
                return;
            }

            // Liaison arrêt son 🔊 : depuis le bas de la carte son vers le bas d'une
            // carte cible (tous types : réplique, event, conséquence, déclencheur,
            // interactable, acteur, cinématique). Miroir de l'arrêt d'ambiance.
            if (_wireDraft.IsSoundStopLink && _wireDraft.SourceSoundStop != null)
            {
                if (IsClickWithoutDrag()) return;
                var sndStop = _wireDraft.SourceSoundStop;
                if (_data.Sounds == null || !_data.Sounds.Contains(sndStop)) return;
                if (TryPickSoundStopTarget(mousePos, out string pickedStop))
                {
                    sndStop.StopSourceCardId = pickedStop;
                    PlayWireSoundLocal();
                }
                return;
            }

            // Sortie "fin →" d'une carte 🎬 : drop sur une carte (cinématique, réplique,
            // conséquence, événement ou nœud) = chaîner. Auto-lien refusé (boucle infinie).
            // Drop dans le vide = menu contextuel (créer + chaîner, ou rompre le lien).
            if (_wireDraft.IsCinematicOut && _wireDraft.SourceCinematic != null)
            {
                var src = _wireDraft.SourceCinematic;
                // Simple clic sans drag = annulation (pas d'effacement accidentel).
                if (IsClickWithoutDrag()) return;
                if (_data.Cinematics == null || !_data.Cinematics.Contains(src)) return;
                string picked = null;
                float best = 34f;
                foreach (var kvp in _cachedCinematicInputSockets)
                {
                    if (string.Equals(kvp.Key, src.CinematicId, StringComparison.OrdinalIgnoreCase)) continue;
                    float dist = Vector2.Distance(kvp.Value, mousePos);
                    if (dist < best) { best = dist; picked = kvp.Key; }
                }
                if (string.IsNullOrEmpty(picked))
                {
                    best = 34f;
                    foreach (var kvp in _cachedInputSockets)
                    {
                        float dist = Vector2.Distance(kvp.Value, mousePos);
                        if (dist < best) { best = dist; picked = kvp.Key; }
                    }
                }
                if (string.IsNullOrEmpty(picked))
                {
                    best = 36f;
                    foreach (var kvp in _cachedNodeInputSockets)
                    {
                        float dist = Vector2.Distance(kvp.Value, mousePos);
                        if (dist < best) { best = dist; picked = kvp.Key; }
                    }
                }
                if (!string.IsNullOrEmpty(picked))
                {
                    src.NextTargetId = picked;
                    PlayWireSoundLocal();
                }
                else
                {
                    OpenMenuForVoid();
                }
                return;
            }

            // Nœud → nœud (sortie principale NextNodeId) : drop sur l'entrée d'un
            // nœud = lier (auto-lien refusé), drop sur un tag de bord = lier la cible
            // hors-champ, drop dans le vide = menu (créer un nœud, ou rompre le lien).
            if (_wireDraft.IsNode && _wireDraft.SourceNode != null)
            {
                var src = _wireDraft.SourceNode;
                // Simple clic sans drag = annulation (pas d'effacement accidentel).
                if (IsClickWithoutDrag()) return;
                if (_data.Nodes.Contains(src))
                {
                    if (TryPickNodeTarget(mousePos, src.NodeId, out string picked))
                    {
                        src.NextNodeId = picked;
                        PlayWireSoundLocal();
                    }
                    else
                    {
                        OpenMenuForVoid();
                    }
                }
                return;
            }
            // Choix majeur → nœud : même routage vers Choices[i].NextNodeId.
            if (_wireDraft.IsNodeChoice && _wireDraft.SourceNode != null)
            {
                var src = _wireDraft.SourceNode;
                int ci = _wireDraft.NodeChoiceIndex;
                bool valid = _data.Nodes.Contains(src) && src.Choices != null && ci >= 0 && ci < src.Choices.Count && src.Choices[ci] != null;
                // Simple clic sans drag = annulation (pas d'effacement accidentel).
                if (!valid) return;
                if (IsClickWithoutDrag()) return;
                if (TryPickNodeTarget(mousePos, src.NodeId, out string picked))
                {
                    src.Choices[ci].NextNodeId = picked;
                    PlayWireSoundLocal();
                }
                else
                {
                    OpenMenuForVoid();
                }
                return;
            }
            // Déclencheur : déposé sur l'entrée d'un nœud, sa sortie entre dans ce nœud.
            // Vide = menu (créer un nœud cible).
            if (_wireDraft.IsTrigger && _wireDraft.SourceTrigger != null)
            {
                if (IsClickWithoutDrag()) return;
                foreach (var kvp in _cachedNodeInputSockets)
                {
                    if (Vector2.Distance(kvp.Value, mousePos) < 36f)
                    {
                        _wireDraft.SourceTrigger.TargetNodeId = kvp.Key;
                        PlayWireSoundLocal();
                        return;
                    }
                }
                OpenMenuForVoid();
                return;
            }
            // Interactable : même routage, la cible est TriggerNodeId (saut runtime existant).
            // Vide = menu (créer un nœud cible).
            if (_wireDraft.IsInteractable && _wireDraft.SourceInteractable != null)
            {
                if (IsClickWithoutDrag()) return;
                foreach (var kvp in _cachedNodeInputSockets)
                {
                    if (Vector2.Distance(kvp.Value, mousePos) < 36f)
                    {
                        _wireDraft.SourceInteractable.TriggerNodeId = kvp.Key;
                        PlayWireSoundLocal();
                        return;
                    }
                }
                OpenMenuForVoid();
                return;
            }
            // Acteur 👥 → réplique 💬 / nœud 🟧 : drop sur l'entrée d'une réplique
            // (LineId) ou d'un nœud (NodeId, enclenché) = lier la rangée
            // (TalkEntries[i].TargetId). Vide = menu (créer + lier).
            // Simple clic = annulation.
            if (_wireDraft.IsActorTalk && _wireDraft.SourceActor != null)
            {
                if (IsClickWithoutDrag()) return;
                var srcActor = _wireDraft.SourceActor;
                int srcIdx = _wireDraft.SourceTalkIndex;
                if (_data.Actors == null || !_data.Actors.Contains(srcActor)
                    || srcActor.TalkEntries == null || srcIdx < 0
                    || srcIdx >= srcActor.TalkEntries.Count || srcActor.TalkEntries[srcIdx] == null) return;
                float bestTalk = 32f;
                string pickedTalk = null;
                foreach (var kvp in _cachedInputSockets)
                {
                    if (!SceneContainsLine(kvp.Key)) continue;
                    float dist = Vector2.Distance(kvp.Value, mousePos);
                    if (dist < bestTalk)
                    {
                        bestTalk = dist;
                        pickedTalk = kvp.Key;
                    }
                }
                if (!string.IsNullOrEmpty(pickedTalk))
                {
                    srcActor.TalkEntries[srcIdx].TargetId = pickedTalk;
                    PlayWireSoundLocal();
                }
                else if (TryPickNodeTarget(mousePos, "", out string pickedNode) && _data.FindNode(pickedNode) != null)
                {
                    srcActor.TalkEntries[srcIdx].TargetId = pickedNode;
                    PlayWireSoundLocal();
                }
                else
                {
                    OpenMenuForVoid();
                }
                return;
            }

            string targetId = null;

            // 1. Socket de dialogue/événement/conséquence LA PLUS PROCHE (pas la première
            // du dictionnaire : en graphe dense plusieurs sockets sont dans le rayon).
            float bestDist = 32f;
            foreach (var kvp in _cachedInputSockets)
            {
                float dist = Vector2.Distance(kvp.Value, mousePos);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    targetId = kvp.Key;
                }
            }

            // 2. Test de collision avec les sockets de Nœud complet
            // (conservé pour compat : un drop précis sur un nœud lie quand même,
            // mais le menu quick-create ne propose un nœud que pour les sorties
            // globales — les sorties de cartes créent des cartes sœurs).
            if (string.IsNullOrEmpty(targetId))
            {
                float bestNodeDist = 36f;
                foreach (var kvp in _cachedNodeInputSockets)
                {
                    float dist = Vector2.Distance(kvp.Value, mousePos);
                    if (dist < bestNodeDist)
                    {
                        bestNodeDist = dist;
                        targetId = kvp.Key;
                    }
                }
            }

            // Vide = menu contextuel (créer une carte sœur + lier).
            if (string.IsNullOrEmpty(targetId))
            {
                OpenMenuForVoid();
                return;
            }

            if (_wireDraft.IsEvent && _wireDraft.SourceEvent != null)
            {
                _wireDraft.SourceEvent.NextEventId = targetId;
            }
            else if (_wireDraft.FromChallengeLink && _wireDraft.SourceChallenge != null)
            {
                // La sortie ✔/✕ accepte une conséquence (prioritaire) ou un dialogue normal.
                // Le wire fait foi : le champ concurrent est effacé pour éviter toute ambiguïté.
                if (SceneContainsConsequence(targetId))
                {
                    if (_wireDraft.ChallengeLinkIsSuccess)
                    {
                        _wireDraft.SourceChallenge.SuccessConsequenceId = targetId;
                        _wireDraft.SourceChallenge.SuccessNextLineId = "";
                    }
                    else
                    {
                        _wireDraft.SourceChallenge.FailureConsequenceId = targetId;
                        _wireDraft.SourceChallenge.FailureNextLineId = "";
                    }
                }
                else if (SceneContainsLine(targetId))
                {
                    if (_wireDraft.ChallengeLinkIsSuccess)
                    {
                        _wireDraft.SourceChallenge.SuccessNextLineId = targetId;
                        _wireDraft.SourceChallenge.SuccessConsequenceId = "";
                    }
                    else
                    {
                        _wireDraft.SourceChallenge.FailureNextLineId = targetId;
                        _wireDraft.SourceChallenge.FailureConsequenceId = "";
                    }
                }
            }
            else if (_wireDraft.FromConsequence && _wireDraft.SourceConsequence != null)
            {
                // Les deux sorties (→Diag et →Cons) acceptent répliques comme conséquences.
                // Le wire fait foi : le champ concurrent est effacé pour éviter toute ambiguïté.
                if (SceneContainsConsequence(targetId))
                {
                    _wireDraft.SourceConsequence.NextConsequenceId = targetId;
                    _wireDraft.SourceConsequence.NextLineId = "";
                }
                else if (SceneContainsLine(targetId))
                {
                    _wireDraft.SourceConsequence.NextLineId = targetId;
                    _wireDraft.SourceConsequence.NextConsequenceId = "";
                }
            }
            else if (_wireDraft.FromChoice && _wireDraft.SourceChoice != null)
            {
                _wireDraft.SourceChoice.NextLineId = targetId;
            }
            else if (_wireDraft.SourceLine != null)
            {
                _wireDraft.SourceLine.NextLineId = targetId;
            }

            if (Killtime.Audio.KilltimeAudioManager.Instance != null)
            {
                Killtime.Audio.KilltimeAudioManager.Instance.PlayUI(Killtime.Audio.SoundId.UI_Filter, 0.6f);
            }
        }

        private void DrawConnectedGraphWires(SceneNodeData node, Rect visibleWorld)
        {
            for (int d = 0; d < node.Dialogues.Count; d++)
            {
                var line = node.Dialogues[d];
                Vector2 size = GetDialogueCardSize(line);
                Vector2 cardPos = new Vector2(line.GraphPosX, line.GraphPosY);

                if (!string.IsNullOrEmpty(line.NextLineId)
                    && _cachedInputSockets.TryGetValue(line.NextLineId, out var targetIn))
                {
                    Vector2 outSocket = new Vector2(cardPos.x + size.x, cardPos.y + 26f);
                    DrawBezierWire(outSocket, targetIn, new Color(0.0f, 0.85f, 1.0f, 0.95f), 3.5f);
                }

                // Sortie 🎬 de la réplique : flanc droit, rangée dédiée.
                DrawCineWireList(new Vector2(cardPos.x + size.x, cardPos.y + CinePortTopCards), line.CinematicIds);

                var choiceRowYs = new List<float>();
                GetDialogueChoiceRowOffsets(line, choiceRowYs);
                for (int c = 0; c < line.Choices.Count; c++)
                {
                    var ch = line.Choices[c];
                    if (ch == null) continue;
                    if (!string.IsNullOrEmpty(ch.NextLineId)
                        && _cachedInputSockets.TryGetValue(ch.NextLineId, out var chTargetIn))
                    {
                        // Aligné sur la rangée GUI réelle (cf. DrawDialogueCard : +100 + extras).
                        float rowY = (c >= 0 && c < choiceRowYs.Count)
                            ? cardPos.y + choiceRowYs[c] + 10f
                            : cardPos.y + 110f + (c * 22f);
                        Vector2 chOutSocket = new Vector2(cardPos.x + size.x, rowY);
                        bool wVs = ch.Challenge != null && ch.Challenge.HasChallenge && ch.Challenge.IsOpposed;
                        bool wChal = ch.Challenge != null && ch.Challenge.HasChallenge;
                        DrawBezierWire(chOutSocket, chTargetIn, wVs ? new Color(1f, 0.55f, 0.2f, 0.95f) : (wChal ? new Color(1f, 0.85f, 0.3f, 0.95f) : new Color(1.0f, 0.8f, 0.2f, 0.95f)), 3.0f);
                    }

                    // Liens du défi : ✔ et ✕ vers cartes Conséquence OU répliques directes.
                    if (ch.Challenge != null && ch.Challenge.HasChallenge)
                    {
                        float linkTop = (c >= 0 && c < choiceRowYs.Count)
                            ? choiceRowYs[c] + 22f + GetChallengeConfigRows(ch.Challenge) * 20f
                            : 122f + (c * 22f);
                        string okId = !string.IsNullOrEmpty(ch.Challenge.SuccessConsequenceId)
                            ? ch.Challenge.SuccessConsequenceId : ch.Challenge.SuccessNextLineId;
                        if (!string.IsNullOrEmpty(okId)
                            && _cachedInputSockets.TryGetValue(okId, out var okIn))
                        {
                            DrawBezierWire(new Vector2(cardPos.x + size.x, cardPos.y + linkTop + 10f), okIn, new Color(0.35f, 1f, 0.55f, 0.95f), 3.0f);
                        }
                        string koId = !string.IsNullOrEmpty(ch.Challenge.FailureConsequenceId)
                            ? ch.Challenge.FailureConsequenceId : ch.Challenge.FailureNextLineId;
                        if (!string.IsNullOrEmpty(koId)
                            && _cachedInputSockets.TryGetValue(koId, out var koIn))
                        {
                            DrawBezierWire(new Vector2(cardPos.x + size.x, cardPos.y + linkTop + 30f), koIn, new Color(1f, 0.4f, 0.35f, 0.95f), 3.0f);
                        }
                    }
                }

                if (line.AutoSkillCheck != null && line.AutoSkillCheck.HasAutoCheck)
                {
                    if (!string.IsNullOrEmpty(line.AutoSkillCheck.SuccessNextLineId)
                        && _cachedInputSockets.TryGetValue(line.AutoSkillCheck.SuccessNextLineId, out var sIn))
                    {
                        DrawBezierWire(new Vector2(cardPos.x + size.x, cardPos.y + 70f), sIn, Color.green, 2.5f);
                    }
                    if (!string.IsNullOrEmpty(line.AutoSkillCheck.FailureNextLineId)
                        && _cachedInputSockets.TryGetValue(line.AutoSkillCheck.FailureNextLineId, out var fIn))
                    {
                        DrawBezierWire(new Vector2(cardPos.x + size.x, cardPos.y + 90f), fIn, new Color(1f, 0.35f, 0.35f), 2.5f);
                    }
                }
            }

            for (int e = 0; e < node.Events.Count; e++)
            {
                var ev = node.Events[e];
                Vector2 size = GetEventCardSize(ev);
                Vector2 cardPos = new Vector2(ev.GraphPosX, ev.GraphPosY);
                if (!string.IsNullOrEmpty(ev.NextEventId)
                    && _cachedInputSockets.TryGetValue(ev.NextEventId, out var targetIn))
                {
                    Vector2 outSocket = new Vector2(cardPos.x + size.x, cardPos.y + 26f);
                    DrawBezierWire(outSocket, targetIn, new Color(0.85f, 0.45f, 1.0f, 0.95f), 3.5f);
                }
                // Sortie 🎬 de l'événement.
                DrawCineWireList(new Vector2(cardPos.x + size.x, cardPos.y + CinePortTopCards), ev.CinematicIds);
            }

            if (node.Consequences != null)
            {
                for (int k = 0; k < node.Consequences.Count; k++)
                {
                    var cons = node.Consequences[k];
                    if (cons == null) continue;
                    Vector2 size = GetConsequenceCardSize(cons);
                    Vector2 cardPos = new Vector2(cons.GraphPosX, cons.GraphPosY);
                    if (!string.IsNullOrEmpty(cons.NextLineId)
                        && _cachedInputSockets.TryGetValue(cons.NextLineId, out var lineIn))
                    {
                        DrawBezierWire(new Vector2(cardPos.x + size.x, cardPos.y + 26f), lineIn, new Color(0.0f, 0.85f, 1.0f, 0.95f), 3.0f);
                    }
                    if (!string.IsNullOrEmpty(cons.NextConsequenceId)
                        && _cachedInputSockets.TryGetValue(cons.NextConsequenceId, out var consIn))
                    {
                        float nextRowTop = GetConsLinkRowTop(cons);
                        DrawBezierWire(new Vector2(cardPos.x + size.x, cardPos.y + nextRowTop + 10f), consIn, new Color(0.4f, 1.0f, 0.5f, 0.95f), 3.0f);
                    }
                    // Sortie 🎬 de la conséquence.
                    DrawCineWireList(new Vector2(cardPos.x + size.x, cardPos.y + CinePortTopCards), cons.CinematicIds);
                }
            }
        }
    }
}

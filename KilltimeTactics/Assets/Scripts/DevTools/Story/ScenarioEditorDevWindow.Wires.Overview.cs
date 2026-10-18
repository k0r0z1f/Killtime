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

    }
}

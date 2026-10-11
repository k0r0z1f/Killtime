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

        private void LayoutCardsInsideNode(SceneNodeData node)
        {
            if (node == null) return;
            float cardStartX = node.GraphPosX + 20f;
            float cardStartY = node.GraphPosY + 48f;
            const int maxCardsPerRow = 3;
            const float gapCardsX = 18f;
            const float gapCardsY = 18f;

            float curY = cardStartY;
            int totalDialogues = node.Dialogues != null ? node.Dialogues.Count : 0;

            for (int rowStart = 0; rowStart < totalDialogues; rowStart += maxCardsPerRow)
            {
                int rowEnd = Mathf.Min(rowStart + maxCardsPerRow, totalDialogues);
                float rowHeight = 0f;
                for (int d = rowStart; d < rowEnd; d++)
                {
                    Vector2 s = GetDialogueCardSize(node.Dialogues[d]);
                    rowHeight = Mathf.Max(rowHeight, s.y);
                }

                for (int d = rowStart; d < rowEnd; d++)
                {
                    int col = d - rowStart;
                    node.Dialogues[d].GraphPosX = cardStartX + col * (DlgCardDefaultW + gapCardsX);
                    node.Dialogues[d].GraphPosY = curY;
                }

                curY += rowHeight + gapCardsY;
            }

            int totalEvents = node.Events != null ? node.Events.Count : 0;
            float curEvtY = curY;
            if (totalEvents > 0)
            {
                float evtStartY = (totalDialogues > 0) ? curY + 10f : cardStartY;
                curEvtY = evtStartY;

                for (int rowStart = 0; rowStart < totalEvents; rowStart += maxCardsPerRow)
                {
                    int rowEnd = Mathf.Min(rowStart + maxCardsPerRow, totalEvents);
                    float rowHeight = 0f;
                    for (int e = rowStart; e < rowEnd; e++)
                    {
                        Vector2 s = GetEventCardSize(node.Events[e]);
                        rowHeight = Mathf.Max(rowHeight, s.y);
                    }

                    for (int e = rowStart; e < rowEnd; e++)
                    {
                        int col = e - rowStart;
                        node.Events[e].GraphPosX = cardStartX + col * (EvtCardDefaultW + gapCardsX);
                        node.Events[e].GraphPosY = curEvtY;
                    }

                    curEvtY += rowHeight + 14f;
                }
            }

            int totalCons = node.Consequences != null ? node.Consequences.Count : 0;
            if (totalCons > 0)
            {
                float consStartY = (totalDialogues > 0 || totalEvents > 0) ? curEvtY + 10f : cardStartY;
                float curConsY = consStartY;

                for (int rowStart = 0; rowStart < totalCons; rowStart += maxCardsPerRow)
                {
                    int rowEnd = Mathf.Min(rowStart + maxCardsPerRow, totalCons);
                    float rowHeight = 0f;
                    for (int k = rowStart; k < rowEnd; k++)
                    {
                        Vector2 s = GetConsequenceCardSize(node.Consequences[k]);
                        rowHeight = Mathf.Max(rowHeight, s.y);
                    }

                    for (int k = rowStart; k < rowEnd; k++)
                    {
                        int col = k - rowStart;
                        node.Consequences[k].GraphPosX = cardStartX + col * (ConsCardDefaultW + gapCardsX);
                        node.Consequences[k].GraphPosY = curConsY;
                    }

                    curConsY += rowHeight + 14f;
                }
            }
        }
        private void ComputeFreeSpotForNewCard(SceneNodeData node, out float x, out float y)
        {
            float baseX = (node != null ? node.GraphPosX : 60f) + 20f;
            float baseY = (node != null ? node.GraphPosY : 60f) + 48f;
            if (node == null)
            {
                x = baseX;
                y = baseY;
                return;
            }

            bool has = false;
            float minLeft = float.MaxValue;
            float maxBottom = float.MinValue;

            if (node.Dialogues != null)
            {
                for (int i = 0; i < node.Dialogues.Count; i++)
                {
                    var d = node.Dialogues[i];
                    if (d == null) continue;
                    Vector2 s = GetDialogueCardSize(d);
                    minLeft = Mathf.Min(minLeft, d.GraphPosX);
                    maxBottom = Mathf.Max(maxBottom, d.GraphPosY + s.y);
                    has = true;
                }
            }
            if (node.Events != null)
            {
                for (int i = 0; i < node.Events.Count; i++)
                {
                    var e = node.Events[i];
                    if (e == null) continue;
                    Vector2 s = GetEventCardSize(e);
                    minLeft = Mathf.Min(minLeft, e.GraphPosX);
                    maxBottom = Mathf.Max(maxBottom, e.GraphPosY + s.y);
                    has = true;
                }
            }
            if (node.Consequences != null)
            {
                for (int i = 0; i < node.Consequences.Count; i++)
                {
                    var k = node.Consequences[i];
                    if (k == null) continue;
                    Vector2 s = GetConsequenceCardSize(k);
                    minLeft = Mathf.Min(minLeft, k.GraphPosX);
                    maxBottom = Mathf.Max(maxBottom, k.GraphPosY + s.y);
                    has = true;
                }
            }

            if (!has)
            {
                x = baseX;
                y = baseY;
                return;
            }

            x = (minLeft != float.MaxValue) ? minLeft : baseX;
            y = maxBottom + 18f;
        }
        private static bool DialogueIdHasIncoming(SceneNodeData node, string lineId)
        {
            if (node == null || string.IsNullOrEmpty(lineId)) return false;
            if (node.Dialogues != null)
            {
                for (int i = 0; i < node.Dialogues.Count; i++)
                {
                    var l = node.Dialogues[i];
                    if (l == null) continue;
                    if (string.Equals(l.NextLineId, lineId, System.StringComparison.OrdinalIgnoreCase)) return true;
                    if (l.Choices != null)
                    {
                        for (int c = 0; c < l.Choices.Count; c++)
                        {
                            var ch = l.Choices[c];
                            if (ch == null) continue;
                            if (string.Equals(ch.NextLineId, lineId, System.StringComparison.OrdinalIgnoreCase)) return true;
                            var chal = ch.Challenge;
                            if (chal != null && chal.HasChallenge)
                            {
                                if (string.Equals(chal.SuccessNextLineId, lineId, System.StringComparison.OrdinalIgnoreCase)) return true;
                                if (string.Equals(chal.FailureNextLineId, lineId, System.StringComparison.OrdinalIgnoreCase)) return true;
                            }
                        }
                    }
                    if (l.AutoSkillCheck != null && l.AutoSkillCheck.HasAutoCheck)
                    {
                        if (string.Equals(l.AutoSkillCheck.SuccessNextLineId, lineId, System.StringComparison.OrdinalIgnoreCase)) return true;
                        if (string.Equals(l.AutoSkillCheck.FailureNextLineId, lineId, System.StringComparison.OrdinalIgnoreCase)) return true;
                    }
                }
            }
            if (node.Consequences != null)
            {
                for (int k = 0; k < node.Consequences.Count; k++)
                {
                    var cons = node.Consequences[k];
                    if (cons == null) continue;
                    if (string.Equals(cons.NextLineId, lineId, System.StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            return false;
        }

        private static bool ConsIdHasIncoming(SceneNodeData node, string consId)
        {
            if (node == null || string.IsNullOrEmpty(consId)) return false;
            if (node.Dialogues != null)
            {
                for (int i = 0; i < node.Dialogues.Count; i++)
                {
                    var l = node.Dialogues[i];
                    if (l == null || l.Choices == null) continue;
                    for (int c = 0; c < l.Choices.Count; c++)
                    {
                        var ch = l.Choices[c];
                        if (ch == null) continue;
                        if (string.Equals(ch.NextLineId, consId, System.StringComparison.OrdinalIgnoreCase)) return true;
                        var chal = ch.Challenge;
                        if (chal != null && chal.HasChallenge)
                        {
                            if (string.Equals(chal.SuccessConsequenceId, consId, System.StringComparison.OrdinalIgnoreCase)) return true;
                            if (string.Equals(chal.FailureConsequenceId, consId, System.StringComparison.OrdinalIgnoreCase)) return true;
                        }
                    }
                }
            }
            if (node.Consequences != null)
            {
                for (int k = 0; k < node.Consequences.Count; k++)
                {
                    var cons = node.Consequences[k];
                    if (cons == null) continue;
                    if (string.Equals(cons.NextConsequenceId, consId, System.StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            return false;
        }

        private static bool EventIdHasIncoming(SceneNodeData node, string evtId)
        {
            if (node == null || string.IsNullOrEmpty(evtId) || node.Events == null) return false;
            for (int i = 0; i < node.Events.Count; i++)
            {
                var e = node.Events[i];
                if (e == null) continue;
                if (string.Equals(e.NextEventId, evtId, System.StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static int CountDialogueEntries(SceneNodeData node)
        {
            if (node == null || node.Dialogues == null) return 0;
            int count = 0;
            for (int i = 0; i < node.Dialogues.Count; i++)
            {
                var l = node.Dialogues[i];
                if (l == null || string.IsNullOrEmpty(l.LineId)) continue;
                if (!DialogueIdHasIncoming(node, l.LineId)) count++;
            }
            return count;
        }

        private static bool DialogueHasOutgoing(SceneDialogueLineData line)
        {
            if (line == null) return false;
            if (!string.IsNullOrEmpty(line.NextLineId)) return true;
            if (line.Choices != null)
            {
                for (int c = 0; c < line.Choices.Count; c++)
                {
                    var ch = line.Choices[c];
                    if (ch == null) continue;
                    if (!string.IsNullOrEmpty(ch.NextLineId)) return true;
                    var chal = ch.Challenge;
                    if (chal != null && chal.HasChallenge)
                    {
                        if (!string.IsNullOrEmpty(chal.SuccessNextLineId)) return true;
                        if (!string.IsNullOrEmpty(chal.FailureNextLineId)) return true;
                        if (!string.IsNullOrEmpty(chal.SuccessConsequenceId)) return true;
                        if (!string.IsNullOrEmpty(chal.FailureConsequenceId)) return true;
                    }
                }
            }
            if (line.AutoSkillCheck != null && line.AutoSkillCheck.HasAutoCheck)
            {
                if (!string.IsNullOrEmpty(line.AutoSkillCheck.SuccessNextLineId)) return true;
                if (!string.IsNullOrEmpty(line.AutoSkillCheck.FailureNextLineId)) return true;
            }
            return false;
        }

        private static bool ConsHasOutgoing(SceneConsequenceData cons)
        {
            if (cons == null) return false;
            return !string.IsNullOrEmpty(cons.NextLineId) || !string.IsNullOrEmpty(cons.NextConsequenceId);
        }

        private static bool EventHasOutgoing(SceneEventData ev)
        {
            if (ev == null) return false;
            return !string.IsNullOrEmpty(ev.NextEventId);
        }

        private static Vector2 GetSoundCardSize(SceneSoundData snd)
        {
            if (snd == null) return new Vector2(SndCardDefaultW, SndCardDefaultH);
            float w = snd.CardWidth > 100f ? snd.CardWidth : SndCardDefaultW;
            float h = snd.CardHeight > 80f ? snd.CardHeight : SndCardDefaultH;
            // ID + Titre + Son + Durée/Délai/Vol + Trigger + Pitch/Loop + Fades +
            // Prio/Cooldown + Spatial + Source + Stop + Tester/Stopper.
            float needed = 30f + 20f * 8f + 22f * 3f + 10f;
            if (h < needed) h = needed;
            return new Vector2(w, h);
        }

        private static Vector2 GetAmbienceCardSize(SceneAmbienceTrackData amb)
        {
            if (amb == null) return new Vector2(320f, 240f);
            float w = amb.CardWidth > 120f ? amb.CardWidth : 320f;
            float h = amb.CardHeight > 100f ? amb.CardHeight : 240f;
            float needed = 250f;
            if (h < needed) h = needed;
            return new Vector2(w, h);
        }

        private bool IsMouseOverAnyNodeOrCard(Vector2 mouseWorld)
        {
            for (int n = 0; n < _data.Nodes.Count; n++)
            {
                var node = _data.Nodes[n];
                Rect nodeBox = ComputeNodeBoundingBox(node);
                if (nodeBox.Contains(mouseWorld)) return true;

                for (int d = 0; d < node.Dialogues.Count; d++)
                {
                    var dlg = node.Dialogues[d];
                    Vector2 s = GetDialogueCardSize(dlg);
                    if (new Rect(dlg.GraphPosX, dlg.GraphPosY, s.x, s.y).Contains(mouseWorld)) return true;
                }

                for (int e = 0; e < node.Events.Count; e++)
                {
                    var ev = node.Events[e];
                    Vector2 s = GetEventCardSize(ev);
                    if (new Rect(ev.GraphPosX, ev.GraphPosY, s.x, s.y).Contains(mouseWorld)) return true;
                }

                if (node.Consequences != null)
                {
                    for (int k = 0; k < node.Consequences.Count; k++)
                    {
                        var cs = node.Consequences[k];
                        if (cs == null) continue;
                        Vector2 s = GetConsequenceCardSize(cs);
                        if (new Rect(cs.GraphPosX, cs.GraphPosY, s.x, s.y).Contains(mouseWorld)) return true;
                    }
                }
            }
            if (_data.Triggers != null)
            {
                for (int t = 0; t < _data.Triggers.Count; t++)
                {
                    var trg = _data.Triggers[t];
                    if (trg == null) continue;
                    Vector2 s = GetTriggerCardSize(trg);
                    if (new Rect(trg.GraphPosX, trg.GraphPosY, s.x, s.y).Contains(mouseWorld)) return true;
                }
            }
            if (_data.Interactables != null)
            {
                for (int i = 0; i < _data.Interactables.Count; i++)
                {
                    var it = _data.Interactables[i];
                    if (it == null) continue;
                    Vector2 s = GetInteractableCardSize(it);
                    if (new Rect(it.GraphPosX, it.GraphPosY, s.x, s.y).Contains(mouseWorld)) return true;
                }
            }
            if (_data.Actors != null)
            {
                for (int i = 0; i < _data.Actors.Count; i++)
                {
                    var a = _data.Actors[i];
                    if (a == null) continue;
                    Vector2 s = GetActorCardSize(a);
                    if (new Rect(a.GraphPosX, a.GraphPosY, s.x, s.y).Contains(mouseWorld)) return true;
                }
            }
            if (_data.Cinematics != null)
            {
                for (int i = 0; i < _data.Cinematics.Count; i++)
                {
                    var c = _data.Cinematics[i];
                    if (c == null) continue;
                    Vector2 s = GetCinematicCardSize(c);
                    if (new Rect(c.GraphPosX, c.GraphPosY, s.x, s.y).Contains(mouseWorld)) return true;
                }
            }
            if (_data.Sounds != null)
            {
                for (int i = 0; i < _data.Sounds.Count; i++)
                {
                    var snd = _data.Sounds[i];
                    if (snd == null) continue;
                    Vector2 s = GetSoundCardSize(snd);
                    if (new Rect(snd.GraphPosX, snd.GraphPosY, s.x, s.y).Contains(mouseWorld)) return true;
                }
            }
            if (_data.Ambiences != null)
            {
                for (int i = 0; i < _data.Ambiences.Count; i++)
                {
                    var amb = _data.Ambiences[i];
                    if (amb == null) continue;
                    Vector2 s = GetAmbienceCardSize(amb);
                    if (new Rect(amb.GraphPosX, amb.GraphPosY, s.x, s.y).Contains(mouseWorld)) return true;
                }
            }
            return false;
        }

        private System.Collections.Generic.Dictionary<string, int> ComputeNodeDepths()
        {
            var depths = new System.Collections.Generic.Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
            if (_data == null || _data.Nodes == null || _data.Nodes.Count == 0) return depths;

            string rootId = _data.FirstNodeId;
            if (string.IsNullOrEmpty(rootId) || !_data.Nodes.Exists(n => n != null && string.Equals(n.NodeId, rootId, System.StringComparison.OrdinalIgnoreCase)))
            {
                rootId = _data.Nodes[0].NodeId;
            }

            var queue = new System.Collections.Generic.Queue<string>();
            depths[rootId] = 0;
            queue.Enqueue(rootId);

            while (queue.Count > 0)
            {
                string currentId = queue.Dequeue();
                int depth = depths[currentId];
                var node = _data.Nodes.Find(x => x != null && string.Equals(x.NodeId, currentId, System.StringComparison.OrdinalIgnoreCase));
                if (node == null) continue;

                var targets = new System.Collections.Generic.List<string>();
                if (!string.IsNullOrEmpty(node.NextNodeId)) targets.Add(node.NextNodeId);
                if (node.Choices != null)
                {
                    for (int c = 0; c < node.Choices.Count; c++)
                        if (node.Choices[c] != null && !string.IsNullOrEmpty(node.Choices[c].NextNodeId))
                            targets.Add(node.Choices[c].NextNodeId);
                }
                if (node.Dialogues != null)
                {
                    for (int d = 0; d < node.Dialogues.Count; d++)
                    {
                        var dlg = node.Dialogues[d];
                        if (dlg == null) continue;
                        if (!string.IsNullOrEmpty(dlg.NextLineId) && _data.Nodes.Exists(n => n != null && string.Equals(n.NodeId, dlg.NextLineId, System.StringComparison.OrdinalIgnoreCase)))
                            targets.Add(dlg.NextLineId);
                        if (dlg.Choices != null)
                        {
                            for (int dc = 0; dc < dlg.Choices.Count; dc++)
                                if (dlg.Choices[dc] != null && !string.IsNullOrEmpty(dlg.Choices[dc].NextLineId) && _data.Nodes.Exists(n => n != null && string.Equals(n.NodeId, dlg.Choices[dc].NextLineId, System.StringComparison.OrdinalIgnoreCase)))
                                    targets.Add(dlg.Choices[dc].NextLineId);
                        }
                    }
                }

                for (int i = 0; i < targets.Count; i++)
                {
                    if (!depths.ContainsKey(targets[i]))
                    {
                        depths[targets[i]] = depth + 1;
                        queue.Enqueue(targets[i]);
                    }
                }
            }

            int currentMax = 0;
            foreach (var kvp in depths) currentMax = System.Math.Max(currentMax, kvp.Value);

            for (int n = 0; n < _data.Nodes.Count; n++)
            {
                var node = _data.Nodes[n];
                if (node == null || string.IsNullOrEmpty(node.NodeId)) continue;
                if (!depths.ContainsKey(node.NodeId))
                {
                    currentMax++;
                    depths[node.NodeId] = currentMax;
                }
            }

            return depths;
        }

        private void AutoLayoutGlobalScene(Rect? canvasRect = null)
        {
            bool hasNodes = _data.Nodes.Count > 0;
            bool hasGlobals = (_data.Triggers != null && _data.Triggers.Count > 0)
                || (_data.Interactables != null && _data.Interactables.Count > 0)
                || (_data.Actors != null && _data.Actors.Count > 0)
                || (_data.Cinematics != null && _data.Cinematics.Count > 0);
            if (!hasNodes && !hasGlobals) return;

            for (int n = 0; n < _data.Nodes.Count; n++)
            {
                var node = _data.Nodes[n];
                EnsureDialogueGraphIntegrity(node);
                StorySceneData.EnsureNodeLists(node);
            }

            // Normalisation immédiate de tous les dialogues internes en 3 colonnes régulières
            for (int n = 0; n < _data.Nodes.Count; n++)
            {
                LayoutCardsInsideNode(_data.Nodes[n]);
            }

            const float startX = 60f;
            const float startY = 60f;
            const float gapX = 50f;
            const float gapY = 28f;

            float maxSceneH = 0f;
            if (hasNodes)
            {
                // Placement structuré en flux 2D compact
                var rootNode = _data.Nodes[0];
                rootNode.GraphPosX = startX;
                rootNode.GraphPosY = startY;
                LayoutCardsInsideNode(rootNode);
                Rect rootBox = ComputeNodeBoundingBox(rootNode);

                float col2X = rootBox.xMax + gapX;
                float col2CurY = startY;
                maxSceneH = rootBox.height;

                for (int n = 1; n < _data.Nodes.Count; n++)
                {
                    var node = _data.Nodes[n];
                    node.GraphPosX = col2X;
                    node.GraphPosY = col2CurY;
                    LayoutCardsInsideNode(node);
                    Rect b = ComputeNodeBoundingBox(node);

                    col2CurY += b.height + gapY;
                    maxSceneH = Mathf.Max(maxSceneH, col2CurY - startY);
                }
            }

            if (_data.Triggers != null && _data.Triggers.Count > 0)
            {
                float trigX = startX;
                float trigY = startY + maxSceneH + 36f;
                for (int t = 0; t < _data.Triggers.Count; t++)
                {
                    var trg = _data.Triggers[t];
                    if (trg == null) continue;
                    trg.GraphPosX = trigX;
                    trg.GraphPosY = trigY;
                    Vector2 s = GetTriggerCardSize(trg);
                    trigX += s.x + 20f;
                }
                // Les cartes Interactables vivent sur la rangée sous les déclencheurs.
                if (_data.Interactables != null && _data.Interactables.Count > 0)
                {
                    GetSceneContentBounds(out float _, out float _, out float _, out float trigMaxY);
                    float interX = startX;
                    float interY = trigMaxY + 30f;
                    for (int i = 0; i < _data.Interactables.Count; i++)
                    {
                        var it = _data.Interactables[i];
                        if (it == null) continue;
                        it.GraphPosX = interX;
                        it.GraphPosY = interY;
                        Vector2 s = GetInteractableCardSize(it);
                        interX += s.x + 20f;
                    }
                }
            }
            else if (_data.Interactables != null && _data.Interactables.Count > 0)
            {
                float interX = startX;
                float interY = startY + maxSceneH + 36f;
                for (int i = 0; i < _data.Interactables.Count; i++)
                {
                    var it = _data.Interactables[i];
                    if (it == null) continue;
                    it.GraphPosX = interX;
                    it.GraphPosY = interY;
                    Vector2 s = GetInteractableCardSize(it);
                    interX += s.x + 20f;
                }
            }
            // Les cartes Acteurs vivent sur la rangée sous les interactables.
            if (_data.Actors != null && _data.Actors.Count > 0)
            {
                float actorY = startY + maxSceneH + 36f;
                if ((_data.Triggers != null && _data.Triggers.Count > 0) || (_data.Interactables != null && _data.Interactables.Count > 0))
                {
                    GetSceneContentBounds(out float _, out float _, out float _, out float placedMaxY);
                    actorY = placedMaxY + 30f;
                }
                float actorX = startX;
                for (int i = 0; i < _data.Actors.Count; i++)
                {
                    var a = _data.Actors[i];
                    if (a == null) continue;
                    a.GraphPosX = actorX;
                    a.GraphPosY = actorY;
                    Vector2 s = GetActorCardSize(a);
                    actorX += s.x + 20f;
                }
            }
            // Les cartes Cinématiques vivent sur la rangée sous les acteurs (entrée seule, sortie 🎬).
            if (_data.Cinematics != null && _data.Cinematics.Count > 0)
            {
                float cineY = startY + maxSceneH + 36f;
                if ((_data.Triggers != null && _data.Triggers.Count > 0) || (_data.Interactables != null && _data.Interactables.Count > 0) || (_data.Actors != null && _data.Actors.Count > 0))
                {
                    GetSceneContentBounds(out float _, out float _, out float _, out float placedMaxYCine);
                    cineY = placedMaxYCine + 30f;
                }
                float cineX = startX;
                for (int i = 0; i < _data.Cinematics.Count; i++)
                {
                    var c = _data.Cinematics[i];
                    if (c == null) continue;
                    c.GraphPosX = cineX;
                    c.GraphPosY = cineY;
                    Vector2 s = GetCinematicCardSize(c);
                    cineX += s.x + 20f;
                }
            }

            if (_data.Sounds != null && _data.Sounds.Count > 0)
            {
                GetSceneContentBounds(out float _, out float _, out float _, out float placedMaxYSnd);
                float sndX = startX;
                float sndY = placedMaxYSnd + 30f;
                for (int i = 0; i < _data.Sounds.Count; i++)
                {
                    var snd = _data.Sounds[i];
                    if (snd == null) continue;
                    snd.GraphPosX = sndX;
                    snd.GraphPosY = sndY;
                    Vector2 s = GetSoundCardSize(snd);
                    sndX += s.x + 20f;
                }
            }

            if (_data.Ambiences != null && _data.Ambiences.Count > 0)
            {
                GetSceneContentBounds(out float _, out float _, out float _, out float placedMaxYAmb);
                float ambX = startX;
                float ambY = placedMaxYAmb + 30f;
                for (int i = 0; i < _data.Ambiences.Count; i++)
                {
                    var amb = _data.Ambiences[i];
                    if (amb == null) continue;
                    amb.GraphPosX = ambX;
                    amb.GraphPosY = ambY;
                    Vector2 s = GetAmbienceCardSize(amb);
                    ambX += s.x + 20f;
                }
            }

            _focusedNodeIndex = 0;
            FocusAllNodes(canvasRect);
        }

        private void AutoLayoutMosaic(Rect? canvasRect = null)
        {
            bool hasNodes = _data.Nodes.Count > 0;
            bool hasGlobals = (_data.Triggers != null && _data.Triggers.Count > 0)
                || (_data.Interactables != null && _data.Interactables.Count > 0)
                || (_data.Actors != null && _data.Actors.Count > 0)
                || (_data.Cinematics != null && _data.Cinematics.Count > 0);
            if (!hasNodes && !hasGlobals) return;
            Vector2 view = ResolveViewSize(canvasRect);
            float maxRowWidth = Mathf.Max(1800f, view.x / Mathf.Clamp(_zoom, 0.35f, 1f));
            const float gapX = 50f;
            const float gapY = 36f;
            const float startX = 60f;
            const float startY = 60f;

            float curX = startX;
            float curY = startY;
            float currentRowHeight = 0f;
            var placedBoxes = new List<Rect>();

            for (int n = 0; n < _data.Nodes.Count; n++)
            {
                var node = _data.Nodes[n];
                EnsureDialogueGraphIntegrity(node);
                StorySceneData.EnsureNodeLists(node);

                node.GraphPosX = curX;
                node.GraphPosY = curY;
                LayoutCardsInsideNode(node);

                Rect box = ComputeNodeBoundingBox(node);
                if (curX + box.width > startX + maxRowWidth && curX > startX)
                {
                    curX = startX;
                    curY += currentRowHeight + gapY;
                    currentRowHeight = 0f;

                    node.GraphPosX = curX;
                    node.GraphPosY = curY;
                    LayoutCardsInsideNode(node);
                    box = ComputeNodeBoundingBox(node);
                }

                Rect candidate = new Rect(curX, curY, box.width, box.height);
                bool collision;
                int guard = 0;
                do
                {
                    collision = false;
                    for (int p = 0; p < placedBoxes.Count && guard < 64; p++)
                    {
                        if (placedBoxes[p].Overlaps(candidate))
                        {
                            candidate.y = placedBoxes[p].yMax + gapY;
                            collision = true;
                            guard++;
                            break;
                        }
                    }
                } while (collision);

                node.GraphPosX = candidate.x;
                node.GraphPosY = candidate.y;
                LayoutCardsInsideNode(node);
                box = ComputeNodeBoundingBox(node);

                placedBoxes.Add(box);
                curX += box.width + gapX;
                currentRowHeight = Mathf.Max(currentRowHeight, box.height);
            }

            if (_data.Triggers != null && _data.Triggers.Count > 0)
            {
                GetSceneContentBounds(out float _, out float _, out float _, out float maxY);
                float trigX = startX;
                float trigY = maxY + 50f;
                for (int t = 0; t < _data.Triggers.Count; t++)
                {
                    var trg = _data.Triggers[t];
                    if (trg == null) continue;
                    trg.GraphPosX = trigX;
                    trg.GraphPosY = trigY;
                    Vector2 s = GetTriggerCardSize(trg);
                    trigX += s.x + 30f;
                }
            }
            if (_data.Interactables != null && _data.Interactables.Count > 0)
            {
                GetSceneContentBounds(out float _, out float _, out float _, out float maxY2);
                float interX = startX;
                float interY = maxY2 + 50f;
                for (int i = 0; i < _data.Interactables.Count; i++)
                {
                    var it = _data.Interactables[i];
                    if (it == null) continue;
                    it.GraphPosX = interX;
                    it.GraphPosY = interY;
                    Vector2 s = GetInteractableCardSize(it);
                    interX += s.x + 30f;
                }
            }
            if (_data.Actors != null && _data.Actors.Count > 0)
            {
                GetSceneContentBounds(out float _, out float _, out float _, out float maxY3);
                float actorX = startX;
                float actorY = maxY3 + 50f;
                for (int i = 0; i < _data.Actors.Count; i++)
                {
                    var a = _data.Actors[i];
                    if (a == null) continue;
                    a.GraphPosX = actorX;
                    a.GraphPosY = actorY;
                    Vector2 s = GetActorCardSize(a);
                    actorX += s.x + 30f;
                }
            }
            if (_data.Cinematics != null && _data.Cinematics.Count > 0)
            {
                GetSceneContentBounds(out float _, out float _, out float _, out float maxY4);
                float cineX = startX;
                float cineY = maxY4 + 50f;
                for (int i = 0; i < _data.Cinematics.Count; i++)
                {
                    var c = _data.Cinematics[i];
                    if (c == null) continue;
                    c.GraphPosX = cineX;
                    c.GraphPosY = cineY;
                    Vector2 s = GetCinematicCardSize(c);
                    cineX += s.x + 30f;
                }
            }

            if (_data.Sounds != null && _data.Sounds.Count > 0)
            {
                GetSceneContentBounds(out float _, out float _, out float _, out float maxY5);
                float sndX = startX;
                float sndY = maxY5 + 50f;
                for (int i = 0; i < _data.Sounds.Count; i++)
                {
                    var snd = _data.Sounds[i];
                    if (snd == null) continue;
                    snd.GraphPosX = sndX;
                    snd.GraphPosY = sndY;
                    Vector2 s = GetSoundCardSize(snd);
                    sndX += s.x + 30f;
                }
            }

            _focusedNodeIndex = 0;
            FocusAllNodes(canvasRect);
        }
        private static float DlgCardDefaultH(int choiceCount) => 215f + choiceCount * 28f;
        private static int GetChallengeConfigRows(SceneDialogueChallengeData ch)
        {
            if (ch == null || !ch.HasChallenge) return 0;
            return ch.IsOpposed ? 2 : 1;
        }
        private int GetChoiceDetailRows(SceneDialogueChoiceData ch)
        {
            if (ch == null) return 0;
            int rows = 0;
            var chal = ch.Challenge;
            if (chal != null && chal.HasChallenge)
                rows += GetChallengeConfigRows(chal) + 2; // config + liens ✔/✕
            bool hasPre = ch.Prerequisite != null && ch.Prerequisite.HasPrerequisite;
            bool hasFx = ch.Effects != null && ch.Effects.Count > 0;
            if (hasPre || hasFx) rows += 1; // méta 🔒/fx
            rows += MeasurePrereqDetail(ch.Prerequisite, SectionOpen(_expandedSections, ch, "pre"));
            rows += MeasureEffectsBlock(ch.Effects, SectionOpen(_expandedSections, ch, "fx"), false);
            if (chal != null)
                rows += MeasureChallengeDetail(chal, SectionOpen(_expandedSections, ch, "chd"));
            return rows;
        }
        private void GetDialogueChoiceRowOffsets(SceneDialogueLineData line, List<float> outOffsets)
        {
            outOffsets.Clear();
            if (line == null || line.Choices == null) return;
            float y = 100f;
            for (int c = 0; c < line.Choices.Count; c++)
            {
                outOffsets.Add(y);
                y += 22f;
                y += GetChoiceDetailRows(line.Choices[c]) * 20f;
            }
        }

        private float GetDialogueCardNeededHeight(SceneDialogueLineData line)
        {
            int cc = line != null && line.Choices != null ? line.Choices.Count : 0;
            float needed = 132f + cc * 22f + 24f;
            if (line != null && line.Choices != null)
            {
                for (int c = 0; c < line.Choices.Count; c++)
                {
                    needed += GetChoiceDetailRows(line.Choices[c]) * 20f;
                }
                needed += GetDialogueLineSummaryRows(line) * 18f;
            }
            needed += 20f; // strip détails 🔒🔊🎲
            needed += CineRowH; // rangée sortie 🎬 (DrawCineLinkRow)
            needed += SoundRowH; // rangée sortie 🔊 (DrawSoundLinkRow)
            if (line != null)
            {
                needed += MeasurePrereqDetail(line.Prerequisite, SectionOpen(_expandedSections, line, "pre")) * 20f;
                needed += MeasureAmbienceDetail(line.Ambience, SectionOpen(_expandedSections, line, "amb")) * 20f;
                needed += MeasureAutoDetail(line.AutoSkillCheck, SectionOpen(_expandedSections, line, "auto")) * 20f;
            }
            return needed;
        }
        private static int GetDialogueLineSummaryRows(SceneDialogueLineData line)
        {
            if (line == null) return 0;
            int rows = 0;
            if (line.Prerequisite != null && line.Prerequisite.HasPrerequisite) rows++;
            if (line.AutoSkillCheck != null && line.AutoSkillCheck.HasAutoCheck) rows++;
            if (line.Ambience != null && line.Ambience.HasAmbience) rows++;
            return rows;
        }

        private Vector2 GetDialogueCardSize(SceneDialogueLineData line)
        {
            if (line == null) return new Vector2(DlgCardDefaultW, DlgCardDefaultH(0));
            float w = line.CardWidth > 100f ? line.CardWidth : DlgCardDefaultW;
            float h = line.CardHeight > 80f ? line.CardHeight : DlgCardDefaultH(line.Choices != null ? line.Choices.Count : 0);
            // Hauteur minimale pour que le contenu GUI absolu ne déborde jamais de la carte.
            float needed = GetDialogueCardNeededHeight(line);
            if (needed > h) h = needed;
            return new Vector2(w, h);
        }

        private float GetEventCardNeededHeight(SceneEventData ev)
        {
            // ID 20 + Kind 22 + strip 20 + détails + rangée 🎬 20 + rangée 🔊 20 + Tester 20 + marges.
            float needed = 124f + CineRowH + SoundRowH;
            if (ev != null)
            {
                needed += MeasurePrereqDetail(ev.Prerequisite, SectionOpen(_expandedSections, ev, "pre")) * 20f;
                needed += MeasureAmbienceDetail(ev.Ambience, SectionOpen(_expandedSections, ev, "amb")) * 20f;
                needed += MeasureAutoDetail(ev.AutoSkillCheck, SectionOpen(_expandedSections, ev, "auto")) * 20f;
                needed += MeasureEffectsBlock(ev.Effects, SectionOpen(_expandedSections, ev, "fx"), true) * 20f;
            }
            return needed;
        }

        private Vector2 GetEventCardSize(SceneEventData ev)
        {
            if (ev == null) return new Vector2(EvtCardDefaultW, EvtCardDefaultH);
            float w = ev.CardWidth > 100f ? ev.CardWidth : EvtCardDefaultW;
            float h = ev.CardHeight > 80f ? ev.CardHeight : EvtCardDefaultH;
            float needed = GetEventCardNeededHeight(ev);
            if (needed > h) h = needed;
            return new Vector2(w, h);
        }
        private float GetConsequenceCardNeededHeight(SceneConsequenceData cons)
        {
            float needed = 30f + 22f + 20f + 22f + 22f + 20f + CineRowH + SoundRowH + 10f;
            if (cons != null)
            {
                if (cons.HasDialogue) needed += 22f + 44f;
                int extras = cons.Rewards?.AdditionalItems != null ? cons.Rewards.AdditionalItems.Count : 0;
                needed += Mathf.Max(0, extras) * 20f;
            }
            needed += MeasureEffectsBlock(cons != null ? cons.Effects : null, SectionOpen(_expandedSections, cons, "fx"), true) * 20f;
            return needed;
        }

        private Vector2 GetConsequenceCardSize(SceneConsequenceData cons)
        {
            if (cons == null) return new Vector2(ConsCardDefaultW, ConsCardDefaultH);
            float w = cons.CardWidth > 100f ? cons.CardWidth : ConsCardDefaultW;
            float h = cons.CardHeight > 80f ? cons.CardHeight : ConsCardDefaultH;
            float needed = GetConsequenceCardNeededHeight(cons);
            if (needed > h) h = needed;
            return new Vector2(w, h);
        }
        private float GetConsLinkRowTop(SceneConsequenceData cons)
        {
            float top = 72f + ((cons != null && cons.HasDialogue) ? 66f : 0f);
            top += MeasureEffectsBlock(cons != null ? cons.Effects : null, SectionOpen(_expandedSections, cons, "fx"), true) * 20f;
            return top;
        }

        private static Vector2 GetTriggerCardSize(SceneTriggerData trg)
        {
            if (trg == null) return new Vector2(TrigCardDefaultW, TrigCardDefaultH);
            float w = trg.CardWidth > 100f ? trg.CardWidth : TrigCardDefaultW;
            float h = trg.CardHeight > 80f ? trg.CardHeight : TrigCardDefaultH;
            float needed = (trg.Kind == SceneTriggerKind.ActorCondition ? 238f : 196f) + CineRowH + SoundRowH;
            if (h < needed) h = needed;
            return new Vector2(w, h);
        }
        private static Vector2 GetCinematicCardSize(SceneCinematicData cine)
        {
            if (cine == null) return new Vector2(CineCardDefaultW, CineCardDefaultH);
            float w = cine.CardWidth > 100f ? cine.CardWidth : CineCardDefaultW;
            float h = cine.CardHeight > 80f ? cine.CardHeight : CineCardDefaultH;
            int n = cine.Shots != null ? cine.Shots.Count : 0;
            float needed = 30f + 22f + 20f + 20f + 20f + 20f * n + CineRowH + SoundRowH + 22f + 10f;
            if (h < needed) h = needed;
            return new Vector2(w, h);
        }

        private static Vector2 GetInteractableCardSize(SceneInteractableSpawnData it)
        {
            if (it == null) return new Vector2(InterCardDefaultW, InterCardDefaultH);
            float w = it.CardWidth > 100f ? it.CardWidth : InterCardDefaultW;
            float h = it.CardHeight > 80f ? it.CardHeight : InterCardDefaultH;
            if (h < InterCardDefaultH + SoundRowH) h = InterCardDefaultH + SoundRowH;
            return new Vector2(w, h);
        }

        private static Vector2 GetActorCardSize(SceneActorSpawnData a)
        {
            if (a == null) return new Vector2(ActorCardDefaultW, ActorCardDefaultH);
            float w = a.CardWidth > 100f ? a.CardWidth : ActorCardDefaultW;
            float h = a.CardHeight > 80f ? a.CardHeight : ActorCardDefaultH;
            int n = a.TalkEntries != null ? a.TalkEntries.Count : 0;
            // En-tête 💬 + une rangée par sortie : la carte grandit avec le
            // nombre d'interlocuteurs (défaut = place pour 1 sortie).
            float needed = ActorCardDefaultH + SoundRowH + ActorTalkRowH * Mathf.Max(0, n - 1);
            if (h < needed) h = needed;
            return new Vector2(w, h);
        }

        private bool GetSceneContentBounds(out float minX, out float minY, out float maxX, out float maxY)
        {
            minX = float.MaxValue; minY = float.MaxValue;
            maxX = float.MinValue; maxY = float.MinValue;
            bool hasNodes = _data != null && _data.Nodes != null && _data.Nodes.Count > 0;
            bool hasTriggers = _data != null && _data.Triggers != null && _data.Triggers.Count > 0;
            bool hasInters = _data != null && _data.Interactables != null && _data.Interactables.Count > 0;
            bool hasActors = _data != null && _data.Actors != null && _data.Actors.Count > 0;
            bool hasCines = _data != null && _data.Cinematics != null && _data.Cinematics.Count > 0;
            bool hasSounds = _data != null && _data.Sounds != null && _data.Sounds.Count > 0;
            bool hasAmbiences = _data != null && _data.Ambiences != null && _data.Ambiences.Count > 0;
            if (!hasNodes && !hasTriggers && !hasInters && !hasActors && !hasCines && !hasSounds && !hasAmbiences) return false;
            for (int i = 0; i < _data.Nodes.Count; i++)
            {
                Rect b = ComputeNodeBoundingBox(_data.Nodes[i]);
                minX = Mathf.Min(minX, b.xMin);
                minY = Mathf.Min(minY, b.yMin);
                maxX = Mathf.Max(maxX, b.xMax);
                maxY = Mathf.Max(maxY, b.yMax);
            }
            if (_data.Triggers != null)
            {
                for (int t = 0; t < _data.Triggers.Count; t++)
                {
                    var trg = _data.Triggers[t];
                    if (trg == null) continue;
                    Vector2 tsize = GetTriggerCardSize(trg);
                    minX = Mathf.Min(minX, trg.GraphPosX);
                    minY = Mathf.Min(minY, trg.GraphPosY);
                    maxX = Mathf.Max(maxX, trg.GraphPosX + tsize.x);
                    maxY = Mathf.Max(maxY, trg.GraphPosY + tsize.y);
                }
            }
            if (_data.Interactables != null)
            {
                for (int i = 0; i < _data.Interactables.Count; i++)
                {
                    var it = _data.Interactables[i];
                    if (it == null) continue;
                    Vector2 isize = GetInteractableCardSize(it);
                    minX = Mathf.Min(minX, it.GraphPosX);
                    minY = Mathf.Min(minY, it.GraphPosY);
                    maxX = Mathf.Max(maxX, it.GraphPosX + isize.x);
                    maxY = Mathf.Max(maxY, it.GraphPosY + isize.y);
                }
            }
            if (_data.Actors != null)
            {
                for (int i = 0; i < _data.Actors.Count; i++)
                {
                    var a = _data.Actors[i];
                    if (a == null) continue;
                    Vector2 asize = GetActorCardSize(a);
                    minX = Mathf.Min(minX, a.GraphPosX);
                    minY = Mathf.Min(minY, a.GraphPosY);
                    maxX = Mathf.Max(maxX, a.GraphPosX + asize.x);
                    maxY = Mathf.Max(maxY, a.GraphPosY + asize.y);
                }
            }
            if (_data.Cinematics != null)
            {
                for (int i = 0; i < _data.Cinematics.Count; i++)
                {
                    var c = _data.Cinematics[i];
                    if (c == null) continue;
                    Vector2 csize = GetCinematicCardSize(c);
                    minX = Mathf.Min(minX, c.GraphPosX);
                    minY = Mathf.Min(minY, c.GraphPosY);
                    maxX = Mathf.Max(maxX, c.GraphPosX + csize.x);
                    maxY = Mathf.Max(maxY, c.GraphPosY + csize.y);
                }
            }
            if (_data.Sounds != null)
            {
                for (int i = 0; i < _data.Sounds.Count; i++)
                {
                    var snd = _data.Sounds[i];
                    if (snd == null) continue;
                    Vector2 ssize = GetSoundCardSize(snd);
                    minX = Mathf.Min(minX, snd.GraphPosX);
                    minY = Mathf.Min(minY, snd.GraphPosY);
                    maxX = Mathf.Max(maxX, snd.GraphPosX + ssize.x);
                    maxY = Mathf.Max(maxY, snd.GraphPosY + ssize.y);
                }
            }
            if (_data.Ambiences != null)
            {
                for (int i = 0; i < _data.Ambiences.Count; i++)
                {
                    var amb = _data.Ambiences[i];
                    if (amb == null) continue;
                    Vector2 asize = GetAmbienceCardSize(amb);
                    minX = Mathf.Min(minX, amb.GraphPosX);
                    minY = Mathf.Min(minY, amb.GraphPosY);
                    maxX = Mathf.Max(maxX, amb.GraphPosX + asize.x);
                    maxY = Mathf.Max(maxY, amb.GraphPosY + asize.y);
                }
            }
            return (maxX > minX) && (maxY > minY);
        }

        private void AutoLayoutNodeGraph(SceneNodeData node)
        {
            float curX = 60f;
            float curY = 80f;
            for (int i = 0; i < node.Dialogues.Count; i++)
            {
                node.Dialogues[i].GraphPosX = curX;
                node.Dialogues[i].GraphPosY = curY;
                Vector2 s = GetDialogueCardSize(node.Dialogues[i]);
                curX += Mathf.Max(370f, s.x + 35f);
                if ((i + 1) % 3 == 0)
                {
                    curX = 60f;
                    curY += 290f;
                }
            }

            curX = 60f;
            curY += 340f;
            for (int e = 0; e < node.Events.Count; e++)
            {
                node.Events[e].GraphPosX = curX;
                node.Events[e].GraphPosY = curY;
                Vector2 s = GetEventCardSize(node.Events[e]);
                curX += Mathf.Max(370f, s.x + 35f);
            }
        }

        private void ComputeNewNodeSpawn(out float x, out float y)
        {
            x = 60f;
            y = 60f;
            if (_data.Nodes.Count > 0)
            {
                if (GetSceneContentBounds(out float _, out float _, out float maxX, out float _))
                {
                    x = maxX + 60f;
                    y = 60f;
                }
                else
                {
                    var lastBox = ComputeNodeBoundingBox(_data.Nodes[_data.Nodes.Count - 1]);
                    x = lastBox.xMax + 60f;
                    y = lastBox.yMin;
                }
            }
        }

        private Rect ComputeNodeBoundingBox(SceneNodeData node)
        {
            float nx = SanitizeCoord(node.GraphPosX, 60f);
            float ny = SanitizeCoord(node.GraphPosY, 60f);
            float minX = nx;
            float minY = ny;
            float maxX = nx + 560f;
            float maxY = ny + 220f;

            bool hasEntries = false;
            if (node.Dialogues != null && node.Dialogues.Count > 0)
            {
                for (int i = 0; i < node.Dialogues.Count; i++)
                {
                    var d = node.Dialogues[i];
                    Vector2 s = GetDialogueCardSize(d);
                    if (!hasEntries)
                    {
                        minX = d.GraphPosX;
                        minY = d.GraphPosY;
                        maxX = d.GraphPosX + s.x;
                        maxY = d.GraphPosY + s.y;
                        hasEntries = true;
                    }
                    else
                    {
                        minX = Mathf.Min(minX, d.GraphPosX);
                        minY = Mathf.Min(minY, d.GraphPosY);
                        maxX = Mathf.Max(maxX, d.GraphPosX + s.x);
                        maxY = Mathf.Max(maxY, d.GraphPosY + s.y);
                    }
                }
            }

            if (node.Events != null && node.Events.Count > 0)
            {
                for (int i = 0; i < node.Events.Count; i++)
                {
                    var e = node.Events[i];
                    if (e == null) continue;
                    Vector2 s = GetEventCardSize(e);
                    if (!hasEntries)
                    {
                        minX = e.GraphPosX;
                        minY = e.GraphPosY;
                        maxX = e.GraphPosX + s.x;
                        maxY = e.GraphPosY + s.y;
                        hasEntries = true;
                    }
                    else
                    {
                        minX = Mathf.Min(minX, e.GraphPosX);
                        minY = Mathf.Min(minY, e.GraphPosY);
                        maxX = Mathf.Max(maxX, e.GraphPosX + s.x);
                        maxY = Mathf.Max(maxY, e.GraphPosY + s.y);
                    }
                }
            }

            if (node.Consequences != null && node.Consequences.Count > 0)
            {
                for (int i = 0; i < node.Consequences.Count; i++)
                {
                    var k = node.Consequences[i];
                    if (k == null) continue;
                    Vector2 s = GetConsequenceCardSize(k);
                    if (!hasEntries)
                    {
                        minX = k.GraphPosX;
                        minY = k.GraphPosY;
                        maxX = k.GraphPosX + s.x;
                        maxY = k.GraphPosY + s.y;
                        hasEntries = true;
                    }
                    else
                    {
                        minX = Mathf.Min(minX, k.GraphPosX);
                        minY = Mathf.Min(minY, k.GraphPosY);
                        maxX = Mathf.Max(maxX, k.GraphPosX + s.x);
                        maxY = Mathf.Max(maxY, k.GraphPosY + s.y);
                    }
                }
            }

            if (!hasEntries)
            {
                float w = node.FrameWidth > 400f ? node.FrameWidth : 560f;
                // Frame vide : header 38 + section détails (ex-inspecteur + ex-inline).
                float sh = GetNodeSectionHeight(node);
                return new Rect(nx, ny, w, Mathf.Max(120f, 38f + 10f + sh + 14f));
            }

            float pad = 14f;
            float topHeaderH = 34f;
            minX = Mathf.Min(nx, minX);
            minY = Mathf.Min(ny + topHeaderH, minY);
            Rect r = new Rect(minX - pad, minY - topHeaderH - pad, (maxX - minX) + pad * 2f, (maxY - minY) + topHeaderH + pad * 2f);
            if (r.width < 560f) r.width = 560f;
            // Section détails du nœud sous les cartes.
            r.height += 10f + GetNodeSectionHeight(node) + 14f;
            return r;
        }
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
                node.Objectives.Add(new ScenarioObjective { Id = $"obj_{node.Objectives.Count + 1}", Label = "Nouvel objectif...", Optional = false });
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
                int nextIdx = node.Dialogues.Count + 1;
                ComputeFreeSpotForNewCard(node, out float freeX, out float freeY);
                node.Dialogues.Add(new SceneDialogueLineData
                {
                    LineId = $"line_{nodeIndex + 1}_{nextIdx}",
                    SpeakerId = "",
                    Speech = "Nouvelle réplique...",
                    GraphPosX = freeX,
                    GraphPosY = freeY
                });
            }

            if (addEvt)
            {
                int nextIdx = node.Events.Count + 1;
                ComputeFreeSpotForNewCard(node, out float freeX, out float freeY);
                node.Events.Add(new SceneEventData
                {
                    EventId = $"evt_{nodeIndex + 1}_{nextIdx}",
                    Title = "Action Scénique",
                    GraphPosX = freeX,
                    GraphPosY = freeY
                });
            }

            if (addCons)
            {
                if (node.Consequences == null) node.Consequences = new System.Collections.Generic.List<SceneConsequenceData>();
                int nextIdx = node.Consequences.Count + 1;
                ComputeFreeSpotForNewCard(node, out float freeX, out float freeY);
                node.Consequences.Add(new SceneConsequenceData
                {
                    ConsequenceId = $"cons_{nodeIndex + 1}_{nextIdx}",
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

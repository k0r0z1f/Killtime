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

        // Graphe : topologie entrees/sorties + bounds + hit-test (ex-Layout.cs, split sans perte).

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
    }
}

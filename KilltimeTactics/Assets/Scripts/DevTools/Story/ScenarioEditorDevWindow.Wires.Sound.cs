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
                    // Cas nœud (RD-119) : le runtime évalue les stops/starts sur
                    // node.Id (EvaluateLinkedAmbiences + FireLinkedSounds), le bas
                    // du cadre nœud est donc une cible d'arrêt/démarrage valide.
                    if (string.Equals(node.NodeId, want, System.StringComparison.OrdinalIgnoreCase))
                    {
                        Rect nodeBox = ComputeNodeBoundingBox(node);
                        center = new Vector2(nodeBox.xMin + nodeBox.width * 0.5f, nodeBox.yMax);
                        return true;
                    }
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
        /// de cartes confondus (nœuds inclus, cf. RD-119 : le runtime évalue les
        /// stops sur les ids de nœuds). Utilisé par les liens d'arrêt son 🔊
        /// et d'arrêt ambiance 🌌 (parité).
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
                    Consider(nd.NodeId);
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

    }
}

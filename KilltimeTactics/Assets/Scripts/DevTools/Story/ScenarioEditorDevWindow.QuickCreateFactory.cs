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

        private SceneNodeData FindParentNodeOfLine(SceneDialogueLineData line)
        {
            if (_data?.Nodes == null || line == null) return null;
            for (int n = 0; n < _data.Nodes.Count; n++)
            {
                var node = _data.Nodes[n];
                if (node?.Dialogues == null) continue;
                for (int d = 0; d < node.Dialogues.Count; d++)
                    if (ReferenceEquals(node.Dialogues[d], line)) return node;
            }
            return null;
        }

        private SceneNodeData FindParentNodeOfChoice(SceneDialogueChoiceData choice)
        {
            if (_data?.Nodes == null || choice == null) return null;
            for (int n = 0; n < _data.Nodes.Count; n++)
            {
                var node = _data.Nodes[n];
                if (node?.Dialogues == null) continue;
                for (int d = 0; d < node.Dialogues.Count; d++)
                {
                    var line = node.Dialogues[d];
                    if (line?.Choices == null) continue;
                    for (int c = 0; c < line.Choices.Count; c++)
                        if (ReferenceEquals(line.Choices[c], choice)) return node;
                }
            }
            return null;
        }

        private SceneNodeData FindParentNodeOfChallenge(SceneDialogueChallengeData chal)
        {
            if (_data?.Nodes == null || chal == null) return null;
            for (int n = 0; n < _data.Nodes.Count; n++)
            {
                var node = _data.Nodes[n];
                if (node?.Dialogues == null) continue;
                for (int d = 0; d < node.Dialogues.Count; d++)
                {
                    var line = node.Dialogues[d];
                    if (line?.Choices == null) continue;
                    for (int c = 0; c < line.Choices.Count; c++)
                        if (line.Choices[c] != null && ReferenceEquals(line.Choices[c].Challenge, chal)) return node;
                }
            }
            return null;
        }

        private SceneNodeData FindParentNodeOfEvent(SceneEventData ev)
        {
            if (_data?.Nodes == null || ev == null) return null;
            for (int n = 0; n < _data.Nodes.Count; n++)
            {
                var node = _data.Nodes[n];
                if (node?.Events == null) continue;
                for (int e = 0; e < node.Events.Count; e++)
                    if (ReferenceEquals(node.Events[e], ev)) return node;
            }
            return null;
        }

        private SceneNodeData FindParentNodeOfConsequence(SceneConsequenceData cons)
        {
            if (_data?.Nodes == null || cons == null) return null;
            for (int n = 0; n < _data.Nodes.Count; n++)
            {
                var node = _data.Nodes[n];
                if (node?.Consequences == null) continue;
                for (int k = 0; k < node.Consequences.Count; k++)
                    if (ReferenceEquals(node.Consequences[k], cons)) return node;
            }
            return null;
        }

        private SceneNodeData FindParentNodeForDraft(WireConnectionDraft draft)
        {
            if (draft.SourceLine != null)
            {
                var p = FindParentNodeOfLine(draft.SourceLine);
                if (p != null) return p;
            }
            if (draft.SourceChoice != null)
            {
                var p = FindParentNodeOfChoice(draft.SourceChoice);
                if (p != null) return p;
            }
            if (draft.SourceChallenge != null)
            {
                var p = FindParentNodeOfChallenge(draft.SourceChallenge);
                if (p != null) return p;
            }
            if (draft.SourceEvent != null)
            {
                var p = FindParentNodeOfEvent(draft.SourceEvent);
                if (p != null) return p;
            }
            if (draft.SourceConsequence != null)
            {
                var p = FindParentNodeOfConsequence(draft.SourceConsequence);
                if (p != null) return p;
            }
            return null;
        }

        private string MakeUniqueLineId(SceneNodeData parent)
        {
            int nodeIdx = parent != null && _data?.Nodes != null ? Mathf.Max(0, _data.Nodes.IndexOf(parent)) : 0;
            int baseCount = 0;
            if (parent?.Dialogues != null)
                for (int i = 0; i < parent.Dialogues.Count; i++)
                    if (parent.Dialogues[i] != null && !string.IsNullOrEmpty(parent.Dialogues[i].LineId))
                        baseCount++;
            for (int attempt = 1; attempt < 500; attempt++)
            {
                string cand = $"line_{nodeIdx + 1}_{baseCount + attempt}";
                if (!SceneContainsLine(cand) && !_cachedInputSockets.ContainsKey(cand)) return cand;
            }
            return $"line_{nodeIdx + 1}_{baseCount + 1}_{UnityEngine.Random.Range(1000, 9999)}";
        }

        private string MakeUniqueEventId(SceneNodeData parent)
        {
            int nodeIdx = parent != null && _data?.Nodes != null ? Mathf.Max(0, _data.Nodes.IndexOf(parent)) : 0;
            int baseCount = 0;
            if (parent?.Events != null)
                for (int i = 0; i < parent.Events.Count; i++)
                    if (parent.Events[i] != null && !string.IsNullOrEmpty(parent.Events[i].EventId))
                        baseCount++;
            for (int attempt = 1; attempt < 500; attempt++)
            {
                string cand = $"evt_{nodeIdx + 1}_{baseCount + attempt}";
                bool exists = false;
                if (_data?.Nodes != null)
                    for (int n = 0; n < _data.Nodes.Count && !exists; n++)
                    {
                        var nd = _data.Nodes[n];
                        if (nd?.Events == null) continue;
                        for (int e = 0; e < nd.Events.Count; e++)
                            if (nd.Events[e] != null && string.Equals(nd.Events[e].EventId, cand, StringComparison.OrdinalIgnoreCase)) { exists = true; break; }
                    }
                if (!exists && !_cachedInputSockets.ContainsKey(cand)) return cand;
            }
            return $"evt_{nodeIdx + 1}_{baseCount + 1}_{UnityEngine.Random.Range(1000, 9999)}";
        }

        private string MakeUniqueConsId(SceneNodeData parent)
        {
            int nodeIdx = parent != null && _data?.Nodes != null ? Mathf.Max(0, _data.Nodes.IndexOf(parent)) : 0;
            int baseCount = 0;
            if (parent?.Consequences != null)
                for (int i = 0; i < parent.Consequences.Count; i++)
                    if (parent.Consequences[i] != null && !string.IsNullOrEmpty(parent.Consequences[i].ConsequenceId))
                        baseCount++;
            for (int attempt = 1; attempt < 500; attempt++)
            {
                string cand = $"cons_{nodeIdx + 1}_{baseCount + attempt}";
                if (!SceneContainsConsequence(cand) && !_cachedInputSockets.ContainsKey(cand)) return cand;
            }
            return $"cons_{nodeIdx + 1}_{baseCount + 1}_{UnityEngine.Random.Range(1000, 9999)}";
        }

        private string MakeUniqueNodeId()
        {
            int baseCount = 0;
            if (_data?.Nodes != null)
                for (int i = 0; i < _data.Nodes.Count; i++)
                    if (_data.Nodes[i] != null && !string.IsNullOrEmpty(_data.Nodes[i].NodeId))
                        baseCount++;
            for (int attempt = 1; attempt < 1000; attempt++)
            {
                string cand = $"node_{baseCount + attempt}";
                if (_data.FindNode(cand) == null && !_cachedNodeInputSockets.ContainsKey(cand)) return cand;
            }
            return $"node_{baseCount + 1}_{UnityEngine.Random.Range(1000, 9999)}";
        }

        private string MakeUniqueCineId()
        {
            int baseCount = 0;
            if (_data?.Cinematics != null)
                for (int i = 0; i < _data.Cinematics.Count; i++)
                    if (_data.Cinematics[i] != null && !string.IsNullOrEmpty(_data.Cinematics[i].CinematicId))
                        baseCount++;
            for (int attempt = 1; attempt < 1000; attempt++)
            {
                string cand = $"cine_{baseCount + attempt}";
                if (_data.FindCinematic(cand) == null && !_cachedCinematicInputSockets.ContainsKey(cand)) return cand;
            }
            return $"cine_{baseCount + 1}_{UnityEngine.Random.Range(1000, 9999)}";
        }

        private string MakeUniqueSoundId()
        {
            int baseCount = 0;
            if (_data?.Sounds != null)
                for (int i = 0; i < _data.Sounds.Count; i++)
                    if (_data.Sounds[i] != null && !string.IsNullOrWhiteSpace(_data.Sounds[i].SoundId))
                        baseCount++;
            for (int attempt = 1; attempt < 1000; attempt++)
            {
                string cand = $"snd_{baseCount + attempt}";
                if (_data.FindSound(cand) == null && !_cachedSoundInputSockets.ContainsKey(cand)) return cand;
            }
            return $"snd_{baseCount + 1}_{UnityEngine.Random.Range(1000, 9999)}";
        }

        private string MakeUniqueAmbienceId()
        {
            int baseCount = 0;
            if (_data?.Ambiences != null)
                for (int i = 0; i < _data.Ambiences.Count; i++)
                    if (_data.Ambiences[i] != null && !string.IsNullOrWhiteSpace(_data.Ambiences[i].AmbienceId))
                        baseCount++;
            for (int attempt = 1; attempt < 1000; attempt++)
            {
                string cand = $"amb_{baseCount + attempt}";
                if (_data.FindAmbience(cand) == null && !_cachedAmbienceInputSockets.ContainsKey(cand)) return cand;
            }
            return $"amb_{baseCount + 1}_{UnityEngine.Random.Range(1000, 9999)}";
        }

        private string MakeUniqueTriggerId()
        {
            int baseCount = 0;
            if (_data?.Triggers != null)
                for (int i = 0; i < _data.Triggers.Count; i++)
                    if (_data.Triggers[i] != null && !string.IsNullOrEmpty(_data.Triggers[i].TriggerId))
                        baseCount++;
            for (int attempt = 1; attempt < 1000; attempt++)
            {
                string cand = $"trig_{baseCount + attempt}";
                bool exists = false;
                if (_data?.Triggers != null)
                    for (int t = 0; t < _data.Triggers.Count; t++)
                        if (_data.Triggers[t] != null && string.Equals(_data.Triggers[t].TriggerId, cand, StringComparison.OrdinalIgnoreCase)) { exists = true; break; }
                if (!exists && !_cachedTriggerOutSockets.ContainsKey(cand)) return cand;
            }
            return $"trig_{baseCount + 1}_{UnityEngine.Random.Range(1000, 9999)}";
        }

        private string MakeUniqueInteractableId()
        {
            int baseCount = 0;
            if (_data?.Interactables != null)
                for (int i = 0; i < _data.Interactables.Count; i++)
                    if (_data.Interactables[i] != null && !string.IsNullOrEmpty(_data.Interactables[i].InteractableId))
                        baseCount++;
            for (int attempt = 1; attempt < 1000; attempt++)
            {
                string cand = $"obj_{baseCount + attempt}";
                bool exists = false;
                if (_data?.Interactables != null)
                    for (int i = 0; i < _data.Interactables.Count; i++)
                        if (_data.Interactables[i] != null && string.Equals(_data.Interactables[i].InteractableId, cand, StringComparison.OrdinalIgnoreCase)) { exists = true; break; }
                if (!exists && !_cachedInteractableOutSockets.ContainsKey(cand)) return cand;
            }
            return $"obj_{baseCount + 1}_{UnityEngine.Random.Range(1000, 9999)}";
        }

        private string MakeUniqueActorId()
        {
            int baseCount = 0;
            if (_data?.Actors != null)
                for (int i = 0; i < _data.Actors.Count; i++)
                    if (_data.Actors[i] != null && !string.IsNullOrEmpty(_data.Actors[i].ActorId))
                        baseCount++;
            for (int attempt = 1; attempt < 1000; attempt++)
            {
                string cand = $"actor_{baseCount + attempt}";
                if (_data.FindActor(cand) == null && !_cachedActorInSockets.ContainsKey(cand)) return cand;
            }
            return $"actor_{baseCount + 1}_{UnityEngine.Random.Range(1000, 9999)}";
        }

        private string MakeUniqueObjectiveId(SceneNodeData parent)
        {
            int baseCount = 0;
            if (parent?.Objectives != null)
                for (int i = 0; i < parent.Objectives.Count; i++)
                    if (parent.Objectives[i] != null && !string.IsNullOrEmpty(parent.Objectives[i].Id))
                        baseCount++;
            for (int attempt = 1; attempt < 500; attempt++)
            {
                string cand = $"obj_{baseCount + attempt}";
                bool exists = false;
                if (parent?.Objectives != null)
                    for (int i = 0; i < parent.Objectives.Count; i++)
                        if (parent.Objectives[i] != null && string.Equals(parent.Objectives[i].Id, cand, StringComparison.OrdinalIgnoreCase)) { exists = true; break; }
                if (!exists) return cand;
            }
            return $"obj_{baseCount + 1}_{UnityEngine.Random.Range(1000, 9999)}";
        }

        private SceneNodeData FindBestHostNodeForCinematicCard(Vector2 dropWorld)
        {
            if (_data?.Nodes == null || _data.Nodes.Count == 0) return null;
            for (int n = 0; n < _data.Nodes.Count; n++)
            {
                var node = _data.Nodes[n];
                if (node == null) continue;
                Rect box = ComputeNodeBoundingBox(node);
                if (box.Contains(dropWorld)) return node;
            }
            SceneNodeData best = null;
            float bestDist = float.MaxValue;
            for (int n = 0; n < _data.Nodes.Count; n++)
            {
                var node = _data.Nodes[n];
                if (node == null) continue;
                Rect box = ComputeNodeBoundingBox(node);
                float d = Vector2.Distance(box.center, dropWorld);
                if (d < bestDist) { bestDist = d; best = node; }
            }
            return bestDist <= 600f ? best : null;
        }

        private void PlayQuickCreateLinkSound()
        {
            if (Killtime.Audio.KilltimeAudioManager.Instance != null)
                Killtime.Audio.KilltimeAudioManager.Instance.PlayUI(Killtime.Audio.SoundId.UI_Filter, 0.6f);
        }

        private void CreateQuickCreateCard(QuickCreateKind kind)
        {
            var draft = _quickCreateDraft;
            Vector2 drop = _quickCreateWorldPos;
            if (!IsQuickCreateDraftStillValid(draft)) { CloseQuickCreateMenu(); return; }

            // --- Sorties globales vers un nouveau nœud ---
            if (kind == QuickCreateKind.Node)
            {
                string newNodeId = MakeUniqueNodeId();
                var newNode = new SceneNodeData
                {
                    NodeId = newNodeId,
                    Title = "Nouveau Nœud",
                    GraphPosX = drop.x,
                    GraphPosY = drop.y - 18f
                };
                _data.Nodes.Add(newNode);
                if (draft.IsNode && draft.SourceNode != null) draft.SourceNode.NextNodeId = newNodeId;
                else if (draft.IsNodeChoice && draft.SourceNode != null && draft.SourceNode.Choices != null && draft.NodeChoiceIndex >= 0 && draft.NodeChoiceIndex < draft.SourceNode.Choices.Count && draft.SourceNode.Choices[draft.NodeChoiceIndex] != null) draft.SourceNode.Choices[draft.NodeChoiceIndex].NextNodeId = newNodeId;
                else if (draft.IsTrigger && draft.SourceTrigger != null) draft.SourceTrigger.TargetNodeId = newNodeId;
                else if (draft.IsInteractable && draft.SourceInteractable != null) draft.SourceInteractable.TriggerNodeId = newNodeId;
                else if (draft.IsCinematicOut && draft.SourceCinematic != null) draft.SourceCinematic.NextTargetId = newNodeId;
                else if (draft.IsActorTalk && draft.SourceActor != null) SetActorTalkTarget(draft.SourceActor, draft.SourceTalkIndex, newNodeId);
                PlayQuickCreateLinkSound();
                CloseQuickCreateMenu();
                return;
            }

            // --- Nouvelle cinématique globale ---
            if (kind == QuickCreateKind.Cinematic)
            {
                _data.Cinematics ??= new List<SceneCinematicData>();
                string newCineId = MakeUniqueCineId();
                var cine = new SceneCinematicData
                {
                    CinematicId = newCineId,
                    Title = $"Cinématique {_data.Cinematics.Count + 1}",
                    GraphPosX = drop.x + 30f,
                    GraphPosY = drop.y - 26f
                };
                cine.Shots.Add(new SceneCinematicShotData { ShotId = "shot_1", Label = "Plan 1" });
                _data.Cinematics.Add(cine);
                if (draft.IsCinematicLink && draft.SourceCineList != null) StorySceneData.AddCineLink(draft.SourceCineList, newCineId);
                else if (draft.IsCinematicOut && draft.SourceCinematic != null) draft.SourceCinematic.NextTargetId = newCineId;
                PlayQuickCreateLinkSound();
                CloseQuickCreateMenu();
                return;
            }

            // --- Nouvelle carte son / ambiance (globale, non liée : à brancher au fil) ---
            if (kind == QuickCreateKind.Sound)
            {
                _data.Sounds ??= new List<SceneSoundData>();
                string newSndId = MakeUniqueSoundId();
                _data.Sounds.Add(new SceneSoundData
                {
                    SoundId = newSndId,
                    Title = $"Son {_data.Sounds.Count + 1}",
                    GraphPosX = drop.x - SndCardDefaultW * 0.5f,
                    GraphPosY = drop.y - 30f
                });
                PlayQuickCreateLinkSound();
                CloseQuickCreateMenu();
                return;
            }
            if (kind == QuickCreateKind.Ambience)
            {
                _data.Ambiences ??= new List<SceneAmbienceTrackData>();
                string newAmbId = MakeUniqueAmbienceId();
                _data.Ambiences.Add(new SceneAmbienceTrackData
                {
                    AmbienceId = newAmbId,
                    Title = $"Ambiance {_data.Ambiences.Count + 1}",
                    PresetId = "amb_asteroid_base",
                    GraphPosX = drop.x - 160f,
                    GraphPosY = drop.y - 30f
                });
                PlayQuickCreateLinkSound();
                CloseQuickCreateMenu();
                return;
            }

            // --- Nouvelles cartes sœurs (même nœud que la source) ---
            SceneNodeData parent = FindParentNodeForDraft(draft);
            if (parent == null && (draft.IsCinematicOut || draft.IsActorTalk))
            {
                parent = FindBestHostNodeForCinematicCard(drop);
                if (parent == null)
                {
                    string hostNodeId = MakeUniqueNodeId();
                    parent = new SceneNodeData
                    {
                        NodeId = hostNodeId,
                        Title = "Nouveau Nœud",
                        GraphPosX = drop.x - 40f,
                        GraphPosY = drop.y - 60f
                    };
                    _data.Nodes.Add(parent);
                }
            }
            if (parent == null) { CloseQuickCreateMenu(); return; }
            parent.Dialogues ??= new List<SceneDialogueLineData>();
            parent.Events ??= new List<SceneEventData>();
            parent.Consequences ??= new List<SceneConsequenceData>();

            Vector2 cardPos = new Vector2(drop.x + 30f, drop.y - 26f);
            string newId = "";
            if (kind == QuickCreateKind.Dialogue)
            {
                newId = MakeUniqueLineId(parent);
                parent.Dialogues.Add(new SceneDialogueLineData
                {
                    LineId = newId,
                    SpeakerId = "",
                    Speech = "Nouvelle réplique...",
                    GraphPosX = cardPos.x,
                    GraphPosY = cardPos.y
                });
            }
            else if (kind == QuickCreateKind.Event)
            {
                newId = MakeUniqueEventId(parent);
                parent.Events.Add(new SceneEventData
                {
                    EventId = newId,
                    Title = "Action Scénique",
                    GraphPosX = cardPos.x,
                    GraphPosY = cardPos.y
                });
            }
            else
            {
                newId = MakeUniqueConsId(parent);
                parent.Consequences.Add(new SceneConsequenceData
                {
                    ConsequenceId = newId,
                    Title = "Récompense",
                    GraphPosX = cardPos.x,
                    GraphPosY = cardPos.y
                });
            }

            // Câblage auto vers la nouvelle carte (le wire fait foi, les champs
            // concurrents sont effacés comme lors d'un drop classique).
            if (draft.IsActorTalk && draft.SourceActor != null)
            {
                if (kind == QuickCreateKind.Dialogue) SetActorTalkTarget(draft.SourceActor, draft.SourceTalkIndex, newId);
            }
            else if (draft.IsCinematicOut && draft.SourceCinematic != null)
            {
                draft.SourceCinematic.NextTargetId = newId;
            }
            else if (draft.IsEvent && draft.SourceEvent != null)
            {
                draft.SourceEvent.NextEventId = newId;
            }
            else if (draft.FromChallengeLink && draft.SourceChallenge != null)
            {
                bool toCons = kind == QuickCreateKind.Consequence;
                if (draft.ChallengeLinkIsSuccess)
                {
                    draft.SourceChallenge.SuccessConsequenceId = toCons ? newId : "";
                    draft.SourceChallenge.SuccessNextLineId = toCons ? "" : newId;
                }
                else
                {
                    draft.SourceChallenge.FailureConsequenceId = toCons ? newId : "";
                    draft.SourceChallenge.FailureNextLineId = toCons ? "" : newId;
                }
            }
            else if (draft.FromConsequence && draft.SourceConsequence != null)
            {
                if (kind == QuickCreateKind.Consequence)
                {
                    draft.SourceConsequence.NextConsequenceId = newId;
                    draft.SourceConsequence.NextLineId = "";
                }
                else
                {
                    draft.SourceConsequence.NextLineId = newId;
                    draft.SourceConsequence.NextConsequenceId = "";
                }
            }
            else if (draft.FromChoice && draft.SourceChoice != null)
            {
                draft.SourceChoice.NextLineId = newId;
            }
            else if (draft.SourceLine != null)
            {
                draft.SourceLine.NextLineId = newId;
            }

            PlayQuickCreateLinkSound();
            CloseQuickCreateMenu();
        }
    }
}

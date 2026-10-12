using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Killtime.Tactics;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Core.Character;
using Killtime.Core.Dice;
using Killtime.Core.Inventory;
using Killtime.CameraSystem;
using Killtime.UI;
using Killtime.Audio;
using Killtime.Story.Data;


namespace Killtime.Story.Scenes
{
    public partial class JsonStorySceneController
    {
        private bool CurrentLineHasPendingChoice()
        {
            if (State._activeReactionChoice != null) return true;
            var dataNode = State._sceneData != null && State._director != null ? State._sceneData.FindNode(State._director.CurrentNode?.Id) : null;
            if (dataNode == null || dataNode.Dialogues == null) return false;
            if (State._currentDialogueIndex < 0 || State._currentDialogueIndex >= dataNode.Dialogues.Count) return false;
            var line = dataNode.Dialogues[State._currentDialogueIndex];
            return line != null && line.Choices != null && line.Choices.Count > 0;
        }

        private bool IsTriggerConditionMet(SceneTriggerData trigger)
        {
            if (trigger == null) return false;
            switch (trigger.Kind)
            {
                case SceneTriggerKind.InteractableActivated:
                    if (string.IsNullOrEmpty(trigger.InteractableId) || !State._activatedInteractableIds.Contains(trigger.InteractableId))
                        return false;
                    // Trigger scopé sur un nœud : l'activation doit dater de ce nœud,
                    // sinon une utilisation précoce skippe le nœud dès son entrée.
                    if (!string.IsNullOrEmpty(trigger.SourceNodeId)
                        && (!State._activationNodeIds.TryGetValue(trigger.InteractableId, out string atNode)
                            || !string.Equals(atNode, trigger.SourceNodeId, StringComparison.OrdinalIgnoreCase)))
                        return false;
                    return true;

                case SceneTriggerKind.ObjectiveCompleted:
                    return !string.IsNullOrEmpty(trigger.ObjectiveId) && State._director != null && State._director.IsObjectiveComplete(trigger.ObjectiveId);

                case SceneTriggerKind.CampaignFlagSet:
                {
                    if (string.IsNullOrEmpty(trigger.FlagKey) || State._director == null || State._director.State == null) return false;
                    bool has = State._director.State.HasFlag(trigger.FlagKey);
                    return trigger.FlagMustBeSet ? has : !has;
                }

                case SceneTriggerKind.ActorCondition:
                {
                    if (string.IsNullOrEmpty(trigger.ActorId)) return false;
                    if (!State._spawnedActors.TryGetValue(trigger.ActorId, out var unit) || unit == null || unit.Stats == null) return false;
                    if (trigger.ActorCondition == SceneActorConditionKind.HasStatus)
                    {
                        if (!System.Enum.TryParse<StatusEffect>(trigger.StatusName, true, out var status) || status == StatusEffect.None) return false;
                        return unit.Stats.ActiveStatus.HasFlag(status);
                    }
                    int max = Mathf.Max(1, unit.Stats.MaxHealth);
                    float pct = (float)unit.Stats.CurrentHealth / max * 100f;
                    return pct < Mathf.Clamp(trigger.HPPercentThreshold, 1, 100);
                }

                default:
                    return false;
            }
        }

        private bool TryFireReadyTrigger()
        {
            if (State._sceneData == null || State._sceneData.Triggers == null || State._director == null) return false;
            string currentId = State._director.CurrentNode?.Id;
            if (string.IsNullOrEmpty(currentId)) return false;

            for (int t = 0; t < State._sceneData.Triggers.Count; t++)
            {
                var trg = State._sceneData.Triggers[t];
                if (trg == null || string.IsNullOrEmpty(trg.TargetNodeId)) continue;
                if (string.Equals(trg.TargetNodeId, currentId, StringComparison.OrdinalIgnoreCase)) continue;
                if (trg.OneShot && State._firedTriggerIds.Contains(trg.TriggerId)) continue;
                if (!string.IsNullOrEmpty(trg.SourceNodeId)
                    && !string.Equals(trg.SourceNodeId, currentId, StringComparison.OrdinalIgnoreCase)) continue;
                if (!IsTriggerConditionMet(trg)) continue;
                if (!State._director.GoToNode(trg.TargetNodeId)) continue;

                // Cinématiques liées au déclencheur : non-bloquantes (le saut a déjà eu lieu).
                FireLinkedCinematics(trg.CinematicIds);
                // Cartes son 🔊 : carte instantanée → début et fin ensemble.
                FireLinkedSounds(trg.TriggerId, SoundTriggerPoint.AtLinkedStart);
                FireLinkedSounds(trg.TriggerId, SoundTriggerPoint.AtLinkedEnd);
                EvaluateLinkedAmbiences(trg.TriggerId);

                if (trg.OneShot) State._firedTriggerIds.Add(trg.TriggerId);
                var target = State._sceneData.FindNode(trg.TargetNodeId);
                CombatHUD.Instance?.AddAdvancedLog($"⚡ <b>Déclencheur :</b> {trg.Label} ➔ {(target != null ? target.Title : trg.TargetNodeId)}", LogCategory.MovementAndTurns, "[ÉVÉNEMENT]", Color.yellow);
                return true;
            }
            return false;
        }

        private void UpdateNodeState()
        {
            if (State._director == null) return;
            var node = State._director.CurrentNode;
            if (node == null) return;

            if (State._lastNodeId != node.Id)
            {
                State._lastNodeId = node.Id;
                // Cartes son 🔊 : fin du nœud précédent + clôture de sa réplique
                // en cours (elle appartenait à l'ancien nœud), avant d'entrer.
                FireLinkedSounds(State._activeSoundNodeId, SoundTriggerPoint.AtLinkedEnd);
                FireLinkedSounds(State._stagedSoundLineId, SoundTriggerPoint.AtLinkedEnd);
                State._stagedSoundLineId = "";
                // Kill-switch inter-nœuds (opt-in par scène) : coupe les instances
                // suivies (loops, longues durées) avant d'entrer dans le nœud.
                if (KilltimeAudioManager.Instance != null && State._sceneData != null
                    && State._sceneData.AudioSettings != null && State._sceneData.AudioSettings.StopAllSoundsOnNodeChange)
                    KilltimeAudioManager.Instance.StopAllSceneSounds(0.2f);
                State._activeSoundNodeId = node.Id;
                State._activeReactionChoice = null;
                State._activeReactionPromptLine = null;
                State._activeChallengeSummary = "";
                State._activeChallengeRewards = "";

                bool isReturn = State._isReturningFromTalk;
                State._isReturningFromTalk = false;

                var dataNode = State._sceneData != null ? State._sceneData.FindNode(node.Id) : null;

                if (isReturn && !string.IsNullOrEmpty(State._pendingTalkLineId))
                {
                    // Retour d'une conversation "Parler à" : reprise directe de la réplique
                    // d'origine SANS rejouer l'intro, les événements ou cinématiques prédécesseurs.
                    string pendingTalk = State._pendingTalkLineId;
                    State._pendingTalkLineId = "";
                    int targetIdx = dataNode != null ? dataNode.FindDialogueIndex(pendingTalk) : -1;
                    State._currentDialogueIndex = targetIdx >= 0 ? targetIdx : 0;
                    StageCurrentLine();
                }
                else
                {
                    // Entrée standard dans un nouveau nœud
                    State._currentDialogueIndex = FindEntryDialogueIndex(dataNode);
                    bool hasPredecessor = FirePredecessorCinematics(node.Id);
                    if (dataNode != null && State._currentDialogueIndex >= 0 && State._currentDialogueIndex < dataNode.Dialogues.Count)
                    {
                        var entryLine = dataNode.Dialogues[State._currentDialogueIndex];
                        if (entryLine != null && FirePredecessorCinematics(entryLine.LineId))
                            hasPredecessor = true;
                    }

                    State._resolvedCardIdsThisNode.Clear();
                    State._nodeEnterCoords.Clear();
                    for (int p = 0; p < State._playerParty.Count; p++)
                    {
                        var u = State._playerParty[p];
                        if (u != null) State._nodeEnterCoords[u] = u.CurrentCoords;
                    }
                    if (dataNode != null)
                    {
                        SpawnActorsForNode(node.Id, dataNode.TriggerCombatOnEnter);
                        FireLinkedCinematics(dataNode.CinematicIds);
                        // Cartes son 🔊 : début du nœud (la fin part à la transition suivante).
                        FireLinkedSounds(node.Id, SoundTriggerPoint.AtLinkedStart);
                        EvaluateLinkedAmbiences(node.Id);
                        ExecuteNodeEvents(dataNode);

                        if (dataNode.TriggerCombatOnEnter && State._turnManager != null && State._turnManager.IsInExploration)
                        {
                            State._turnManager.EnterCombatMode();
                        }

                        for (int p = 0; p < State._playerParty.Count; p++)
                        {
                            var u = State._playerParty[p];
                            if (u != null && !State._nodeEnterCoords.ContainsKey(u))
                                State._nodeEnterCoords[u] = u.CurrentCoords;
                        }
                    }

                    if (!hasPredecessor)
                    {
                        StageCurrentLine();
                    }

                    if (!string.IsNullOrEmpty(State._pendingTalkLineId))
                    {
                        string pendingTalk = State._pendingTalkLineId;
                        State._pendingTalkLineId = "";
                        if (dataNode != null && dataNode.FindDialogueIndex(pendingTalk) >= 0)
                            TryAdvanceToId(pendingTalk, null);
                    }
                }
            }

            // Déclencheurs d'entrée : évalués en continu pendant l'exploration
            // des nœuds Objectif (actions carte, conditions d'acteurs, flags...).
            // Les nœuds de dialogue/choix les évaluent à leur sortie (boutons).
            if (node.Kind == ScenarioNodeKind.Objective
                && State._activeReactionChoice == null
                && !CurrentLineHasPendingChoice())
            {
                TryFireReadyTrigger();
            }
        }

        // Mise en scène de la réplique courante : prérequis, cinématiques liées (cartes 🎬),
        // ambiance sonore/musicale, tests auto. DÉ-HARDCODÉ : plus aucun focus caméra direct
        // (l'ancien CameraFocusActorId/Pitch/Distance est ignoré — migré en cartes 🎬 à tracking).
        private void StageCurrentLine()
        {
            var dataNode = State._sceneData.FindNode(State._director.CurrentNode?.Id);
            if (dataNode == null || dataNode.Dialogues.Count == 0 || State._currentDialogueIndex >= dataNode.Dialogues.Count) return;

            var line = dataNode.Dialogues[State._currentDialogueIndex];

            // Évaluation des prérequis : si non satisfaits, passage immédiat à la réplique suivante
            if (line.Prerequisite != null && line.Prerequisite.HasPrerequisite)
            {
                if (!line.Prerequisite.IsMet(State._playerParty, State._turnManager?.ActiveUnit, State._director))
                {
                    CombatHUD.Instance?.AddAdvancedLog($"<color=grey>[PRÉREQUIS NON SATISFAIT] Réplique '{line.LineId}' ignorée ({line.Prerequisite.GetSummary()}).</color>", LogCategory.MovementAndTurns, "[NARRATION]", Color.gray);
                    AdvanceDialogueOrNode();
                    return;
                }
            }

            // Cinématiques liées à la réplique : non-bloquantes (le dialogue continue aussitôt).
            FireLinkedCinematics(line.CinematicIds);

            // Cartes son 🔊 : fin de la réplique précédente (changement de mise en
            // scène), puis début de celle-ci. La réplique recalée au préréquis ne
            // devient jamais "staged" : aucune fin parasite.
            if (!string.Equals(State._stagedSoundLineId, line.LineId, StringComparison.OrdinalIgnoreCase))
            {
                FireLinkedSounds(State._stagedSoundLineId, SoundTriggerPoint.AtLinkedEnd);
                State._stagedSoundLineId = line.LineId;
            }
            FireLinkedSounds(line.LineId, SoundTriggerPoint.AtLinkedStart);
            EvaluateLinkedAmbiences(line.LineId);

            // Gestion de l'ambiance et des cues audio.
            // Arbitrage legacy (opt-out par scène) : si des cartes 🔊 sont liées à
            // cette réplique, le cue legacy est ignoré (évite le double déclenchement).
            bool preferCards = State._sceneData == null || State._sceneData.AudioSettings == null || State._sceneData.AudioSettings.PreferCardsOverLegacy;
            string legacyCue = (preferCards && State._sceneData != null && State._sceneData.HasLinkedSounds(line.LineId)) ? "" : line.SoundCueId;
            ApplyAmbienceSettings(line.Ambience, legacyCue);

            // Gestion des tests de compétence automatiques
            if (line.AutoSkillCheck != null && line.AutoSkillCheck.HasAutoCheck)
            {
                ExecuteAutoSkillCheck(line);
            }
        }

        // Entrée du nœud = première carte dialogue sans lien entrant (routage explicite).
        // Repli sur 0 si aucune (cycle) ou liste vide.
        // Logique : SceneDialogueService.FindEntryDialogueIndex (API conservée, utilisée par UpdateNodeState).
        public static int FindEntryDialogueIndex(SceneNodeData dataNode)
            => SceneDialogueService.FindEntryDialogueIndex(dataNode);
    }
}

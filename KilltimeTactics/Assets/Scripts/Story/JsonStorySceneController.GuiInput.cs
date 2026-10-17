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
        private Rect ComputeDialogueRect()
        {
            float boxWidth = Mathf.Min(920f, Screen.width - 40f);
            float baseHeight = 150f;
            try
            {
                bool isReactionActive = State._activeReactionChoice != null && State._activeReactionPromptLine != null;
                var dataNode = State._sceneData != null && State._director != null ? State._sceneData.FindNode(State._director.CurrentNode?.Id) : null;
                bool hasDialogues = dataNode != null && dataNode.Dialogues != null && dataNode.Dialogues.Count > 0 && State._currentDialogueIndex >= 0 && State._currentDialogueIndex < dataNode.Dialogues.Count;
                var line = hasDialogues ? dataNode.Dialogues[State._currentDialogueIndex] : null;
                bool hasChoices = !isReactionActive && line != null && line.Choices != null && line.Choices.Count > 0;
                if (hasChoices) baseHeight += line.Choices.Count * 34f;
                else if (isReactionActive) baseHeight += 20f;
            }
            catch { /* ignore */ }
            if (Screen.width <= 0 || Screen.height <= 0) return Rect.zero;
            float boxHeight = Mathf.Min(baseHeight, Screen.height - 120f);
            if (boxWidth <= 0f || boxHeight <= 0f) return Rect.zero;
            return new Rect((Screen.width - boxWidth) * 0.5f, Screen.height - boxHeight - 20f, boxWidth, boxHeight);
        }

        private bool ShouldShowSceneChat()
        {
            if (!State._isSceneInitialized) return false;
            if (CombatHUD.IsPaused) return false;
            if (StorySceneManager.Instance != null && StorySceneManager.Instance.IsTransitioning) return false;

            var director = FindAnyObjectByType<CinematicDirector>();
            if (director != null && director.IsPlayingSceneCinematic)
            {
                if (director.CurrentPlayingSceneCinematic == null || director.CurrentPlayingSceneCinematic.HideSceneChat)
                {
                    return false;
                }
            }

            return State._director != null && State._director.CurrentNode != null;
        }

        private void UpdateChatAnimation()
        {
            float target = ShouldShowSceneChat() ? 1f : 0f;
            if (!Mathf.Approximately(State._chatAnimProgress, target))
            {
                State._chatAnimProgress = Mathf.MoveTowards(State._chatAnimProgress, target, Time.unscaledDeltaTime * 2.85f);
            }
        }

        private void HandlePartyInput()
        {
            if (State._turnManager == null || !State._turnManager.IsInExploration) return;

            if (Input.GetKeyDown(KeyCode.Alpha1) && State._playerParty.Count > 0) SelectOperative(State._playerParty[0]);
            if (Input.GetKeyDown(KeyCode.Alpha2) && State._playerParty.Count > 1) SelectOperative(State._playerParty[1]);
            if (Input.GetKeyDown(KeyCode.Alpha3) && State._playerParty.Count > 2) SelectOperative(State._playerParty[2]);
            if (Input.GetKeyDown(KeyCode.Alpha4) && State._playerParty.Count > 3) SelectOperative(State._playerParty[3]);
            if (Input.GetKeyDown(KeyCode.Tab) && State._playerParty.Count > 1) CyclePartyMember();

            if (Input.GetKeyDown(KeyCode.E) && State._turnManager.ActiveUnit != null)
            {
                InteractWithNearest(State._turnManager.ActiveUnit);
            }
        }

        private void SelectOperative(TacticalUnit unit)
        {
            if (unit == null || State._arena == null) return;
            State._arena.SwitchActiveExplorer(unit);
        }

        private void CyclePartyMember()
        {
            int idx = State._playerParty.IndexOf(State._turnManager.ActiveUnit);
            int next = (idx + 1) % State._playerParty.Count;
            SelectOperative(State._playerParty[next]);
        }

        private void InteractWithNearest(TacticalUnit unit)
        {
            for (int i = 0; i < State._interactables.Count; i++)
            {
                var target = State._interactables[i];
                if (target == null) continue;
                var data = FindInteractableData(target);
                if (data == null) continue;
                if (target.CanInteract(unit))
                {
                    OpenInteractableWindow(unit, data);
                    return;
                }
            }
        }

        private void HandleDialoguesInput()
        {
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))
            {
                if (State._activeReactionChoice != null)
                {
                    ConfirmReactionAndAdvance();
                    return;
                }

                var dataNode = State._sceneData != null && State._director != null ? State._sceneData.FindNode(State._director.CurrentNode?.Id) : null;
                if (dataNode != null && State._currentDialogueIndex >= 0 && State._currentDialogueIndex < dataNode.Dialogues.Count)
                {
                    var currentLine = dataNode.Dialogues[State._currentDialogueIndex];
                    if (currentLine != null && currentLine.Choices.Count > 0)
                    {
                        return;
                    }
                }

                AdvanceDialogueOrNode();
            }
        }
    }
}

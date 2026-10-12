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
        // Octroi d'objet de récompense — logique : SceneDialogueService.GrantRewardItem.
        private void GrantRewardItem(CharacterSheet sheet, string itemName, ItemType itemType, int quantity, ref string rewardsSummary)
            => SceneDialogueService.GrantRewardItem(sheet, itemName, itemType, quantity, ref rewardsSummary);

        private void ApplyConsequencePayload(SceneConsequenceData cons, out string rewardsSummary)
        {
            rewardsSummary = "";
            if (cons == null) return;

            // Marque la carte comme résolue pour ce nœud (anti double-payload via chaîne).
            if (!string.IsNullOrEmpty(cons.ConsequenceId)) State._resolvedCardIdsThisNode.Add(cons.ConsequenceId);

            // Cinématiques liées à la conséquence : non-bloquantes.
            FireLinkedCinematics(cons.CinematicIds);

            TacticalUnit unit = null;
            if (State._turnManager != null && State._turnManager.ActiveUnit != null && State._turnManager.ActiveUnit.IsPlayerControlled)
                unit = State._turnManager.ActiveUnit;
            else if (State._playerParty.Count > 0)
                unit = State._playerParty[0];
            var sheet = unit != null ? unit.Sheet : null;

            var rew = cons.Rewards;
            if (rew != null && sheet != null)
            {
                if (rew.EarnCredits > 0)
                {
                    sheet.EarnCredits(rew.EarnCredits);
                    rewardsSummary += $"+{rew.EarnCredits} CE  ";
                }
                if (rew.EarnXP > 0)
                {
                    sheet.AvailableXP += rew.EarnXP;
                    sheet.TotalEarnedXP += rew.EarnXP;
                    rewardsSummary += $"+{rew.EarnXP} XP  ";
                }
                if (!string.IsNullOrEmpty(rew.ItemRewardName))
                    GrantRewardItem(sheet, rew.ItemRewardName, rew.ItemRewardType, rew.ItemRewardQuantity, ref rewardsSummary);
                if (rew.AdditionalItems != null)
                {
                    for (int i = 0; i < rew.AdditionalItems.Count; i++)
                    {
                        var extra = rew.AdditionalItems[i];
                        if (extra == null || string.IsNullOrEmpty(extra.ItemName)) continue;
                        GrantRewardItem(sheet, extra.ItemName, extra.ItemRewardType, extra.Quantity, ref rewardsSummary);
                    }
                }
                if (!string.IsNullOrEmpty(rew.CompletionObjectiveId))
                {
                    State._director?.SetObjectiveComplete(rew.CompletionObjectiveId, true);
                    rewardsSummary += "✔ Objectif validé  ";
                }
            }

            if (cons.Effects != null && cons.Effects.Count > 0 && State._director != null)
                State._director.ApplyEffects(cons.Effects);

            string who = sheet != null ? sheet.Name : "Escouade";
            string fxTag = cons.Effects != null && cons.Effects.Count > 0 ? $" (+{cons.Effects.Count} effet(s))" : "";
            CombatHUD.Instance?.AddAdvancedLog(
                $"🎁 <b>[CONSÉQUENCE]</b> {who} — <b>{cons.Title}</b>{(string.IsNullOrEmpty(rewardsSummary) ? "" : $" : {rewardsSummary}")}{fxTag}",
                LogCategory.MovementAndTurns,
                "[BUTIN]",
                Color.yellow
            );

            if (cons.TriggersCombat)
            {
                SpawnActorsForNode(State._director?.CurrentNode?.Id, true);
                State._turnManager?.EnterCombatMode();
                CombatHUD.Instance?.AddAdvancedLog("🚨 <b>CONSÉQUENCE :</b> Combat déclenché !", LogCategory.Combat, "[ALERTE]", Color.red);
            }
        }

        // Exécute une conséquence et sa chaîne (cons → cons). Retourne true si le flux a
        // progressé (saut de réplique, affichage dialogue, avance d'index).
        private bool ExecuteConsequenceChain(SceneConsequenceData first, SceneDialogueLineData promptLine, HashSet<string> visited, bool firePredecessors = true)
        {
            if (first == null) return false;
            visited ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // Cartes son 🔊 : chaîne instantanée → début et fin ensemble.
            FireLinkedSounds(first.ConsequenceId, SoundTriggerPoint.AtLinkedStart);
            FireLinkedSounds(first.ConsequenceId, SoundTriggerPoint.AtLinkedEnd);
            EvaluateLinkedAmbiences(first.ConsequenceId);
            var dataNode = State._sceneData.FindNode(State._director.CurrentNode?.Id);
            if (dataNode == null) return false;

            // Cinématiques placées en amont de la conséquence d'entrée (sauf arrivée
            // via chaîne "fin →", dont la cinématique vient de se terminer).
            if (firePredecessors) FirePredecessorCinematics(first.ConsequenceId);

            var cons = first;
            int guard = 0;
            while (cons != null && guard++ < 8)
            {
                if (!visited.Add(cons.ConsequenceId)) return false;

                ApplyConsequencePayload(cons, out string rewardsSummary);
                string onwardId = !string.IsNullOrEmpty(cons.NextLineId) ? cons.NextLineId : cons.NextConsequenceId;

                if (cons.HasDialogue && !string.IsNullOrWhiteSpace(cons.Speech))
                {
                    var displayPrompt = new SceneDialogueLineData
                    {
                        SpeakerId = !string.IsNullOrEmpty(cons.SpeakerId) ? cons.SpeakerId : (promptLine != null ? promptLine.SpeakerId : "???"),
                        StageDirection = cons.StageDirection ?? ""
                    };
                    SetupReactionDisplay(displayPrompt, cons.Speech, cons.StageDirection, onwardId, $"🎁 {cons.Title}", rewardsSummary);
                    return true;
                }

                if (!string.IsNullOrEmpty(cons.NextConsequenceId))
                {
                    var next = dataNode.FindConsequence(cons.NextConsequenceId);
                    if (next != null) { cons = next; continue; }
                }
                if (!string.IsNullOrEmpty(cons.NextLineId))
                {
                    int idx = dataNode.FindDialogueIndex(cons.NextLineId);
                    if (idx >= 0)
                    {
                        bool changed = State._currentDialogueIndex != idx;
                        State._currentDialogueIndex = idx;
                        if (changed) FirePredecessorCinematics(cons.NextLineId);
                        StageCurrentLine();
                        return true;
                    }
                }

                // Cul-de-sac : avancer sans rejouer le lien d'origine (skipLink).
                AdvanceDialogueOrNodeInternal(visited, true);
                return true;
            }
            return true;
        }
    }
}

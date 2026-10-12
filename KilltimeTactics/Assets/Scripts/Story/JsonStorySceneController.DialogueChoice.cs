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
        private void SelectDialogueChoice(SceneDialogueChoiceData choice, SceneDialogueLineData promptLine)
        {
            if (choice == null || promptLine == null) return;

            if (choice.Challenge != null && choice.Challenge.HasChallenge)
            {
                ExecuteSkillChallenge(choice, promptLine);
                return;
            }

            if (choice.Effects != null && choice.Effects.Count > 0 && State._director != null)
            {
                State._director.ApplyEffects(choice.Effects);
            }

            CombatHUD.Instance?.AddAdvancedLog($"💬 [Choix] {choice.Label}", LogCategory.MovementAndTurns, "[DIALOGUE]", Color.white);

            // Routage direct vers fenêtre normale : pas d'écran de réaction intermédiaire.
            // Choix sans cible = fin du nœud (routage explicite, pas de séquentiel).
            if (!string.IsNullOrEmpty(choice.NextLineId))
            {
                if (TryAdvanceToId(choice.NextLineId, null)) return;
            }

            AdvanceDialogueOrNode();
        }

        private void ExecuteSkillChallenge(SceneDialogueChoiceData choice, SceneDialogueLineData promptLine)
        {
            var challenge = choice.Challenge;

            TacticalUnit actorUnit = null;
            if (!string.IsNullOrEmpty(challenge.SpecificActorId) && State._spawnedActors.TryGetValue(challenge.SpecificActorId, out var specificUnit))
            {
                actorUnit = specificUnit;
            }
            else if (State._turnManager != null && State._turnManager.ActiveUnit != null && State._turnManager.ActiveUnit.IsPlayerControlled)
            {
                actorUnit = State._turnManager.ActiveUnit;
            }
            else if (State._playerParty.Count > 0)
            {
                actorUnit = State._playerParty[0];
            }

            var sheet = actorUnit != null ? actorUnit.Sheet : null;
            string actorName = sheet != null ? sheet.Name : "Protagoniste";
            Attributes effectiveAttr = sheet != null ? sheet.GetEffectiveAttributes() : new Attributes();

            int rank = SkillDefinitions.GetBaseRank(challenge.RequiredSkill, effectiveAttr, false);
            int steps = SkillDefinitions.CharacteristicSteps(rank);
            int training = sheet != null ? sheet.GetSkill(challenge.RequiredSkill).TrainingLevel : 0;
            DiceType skillDie = SkillDefinitions.DieFromTotalSteps(steps + training);

            string skillName = SkillDefinitions.GetDisplayName(challenge.RequiredSkill);
            var roller = new DiceRoller();

            DiceRollResult result;
            string vsLabel;
            string oppSkillName = "";
            string oppName = "";
            int oppTotal = 0;

            if (challenge.IsOpposed)
            {
                // Défi opposé aveugle (Livre II §7) : mises masquées des deux camps,
                // les deux dés sont lancés ensemble et révélés simultanément.
                TacticalUnit oppUnit = null;
                if (!string.IsNullOrEmpty(challenge.OpposedActorId))
                    State._spawnedActors.TryGetValue(challenge.OpposedActorId, out oppUnit);
                else if (promptLine != null && !string.IsNullOrEmpty(promptLine.SpeakerId))
                    State._spawnedActors.TryGetValue(promptLine.SpeakerId, out oppUnit);

                oppSkillName = SkillDefinitions.GetDisplayName(challenge.OpposedSkill);
                if (oppUnit != null && oppUnit.Sheet != null)
                {
                    var oppSheet = oppUnit.Sheet;
                    oppName = !string.IsNullOrEmpty(oppSheet.Name) ? oppSheet.Name : challenge.OpposedActorId;
                    Attributes oppAttr = oppSheet.GetEffectiveAttributes();
                    int oppRank = SkillDefinitions.GetBaseRank(challenge.OpposedSkill, oppAttr, false);
                    int oppTraining = oppSheet.GetSkill(challenge.OpposedSkill).TrainingLevel;
                    DiceType oppDie = SkillDefinitions.DieFromTotalSteps(SkillDefinitions.CharacteristicSteps(oppRank) + oppTraining);
                    var oppRoll = roller.Roll(oppDie, modifier: challenge.OpposedBonus, targetDC: 0);
                    oppTotal = oppRoll.Total;
                    // Mémorise le brut pour le log.
                    result = roller.Roll(skillDie, modifier: 0, targetDC: oppTotal);
                    vsLabel = $"{actorName} [{skillName} {skillDie}={result.RawRoll} → {result.Total}] vs {oppName} [{oppSkillName} {oppDie}={oppRoll.RawRoll}{(challenge.OpposedBonus != 0 ? $" +{challenge.OpposedBonus}" : "")} → {oppTotal}]";
                }
                else
                {
                    oppName = !string.IsNullOrEmpty(challenge.OpposedActorId) ? challenge.OpposedActorId : "Opposition";
                    oppTotal = challenge.TargetDC + challenge.OpposedBonus;
                    result = roller.Roll(skillDie, modifier: 0, targetDC: oppTotal);
                    vsLabel = $"{actorName} [{skillName} {skillDie}={result.RawRoll} → {result.Total}] vs {oppName} [{oppSkillName} fixe {oppTotal}]";
                }
            }
            else
            {
                result = roller.Roll(skillDie, modifier: 0, targetDC: challenge.TargetDC);
                vsLabel = $"{actorName} teste <b>{skillName}</b> ({skillDie}) contre SD {challenge.TargetDC}";
            }

            string outcomeTag = result.IsSuccess ? (result.IsCriticalSuccess ? "RÉUSSITE CRITIQUE" : "SUCCÈS") : (result.IsCriticalFailure ? "ÉCHEC CRITIQUE" : "ÉCHEC");
            Color outcomeCol = result.IsSuccess ? (result.IsCriticalSuccess ? new Color(1f, 0.85f, 0.2f) : Color.green) : (result.IsCriticalFailure ? Color.red : new Color(1f, 0.5f, 0.2f));

            string defiTag = challenge.IsOpposed ? "DÉFI OPPOSÉ AVEUGLE" : "DÉFI";
            string detailRes = challenge.IsOpposed
                ? $"Révélation simultanée (mises cachées): <b>{result.Total} vs {oppTotal}</b> (Diff: {result.Differential:+0;-0;0})"
                : $"Résultat: <b>{result.Total}</b> (Brut {result.RawRoll}, Diff: {result.Differential:+0;-0;0})";

            CombatHUD.Instance?.AddAdvancedLog(
                $"{(challenge.IsOpposed ? "⚔️" : "🎲")} <b>[{defiTag}]</b> {vsLabel} ➔ {detailRes} — <color=#{ColorUtility.ToHtmlStringRGB(outcomeCol)}>{outcomeTag}</color>",
                LogCategory.Combat,
                "[DÉFI]",
                outcomeCol
            );

            string floatVs = challenge.IsOpposed ? $"{result.Total} vs {oppTotal}" : $"{result.Total} vs SD {challenge.TargetDC}";
            actorUnit?.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText(
                $"{outcomeTag} ({floatVs})",
                outcomeCol
            );

            if (result.IsSuccess)
            {
                if (KilltimeAudioManager.Instance != null)
                {
                    KilltimeAudioManager.Instance.PlayUI(SoundId.UI_Filter, 0.7f);
                }

                if (challenge.SuccessEffects != null && challenge.SuccessEffects.Count > 0)
                {
                    State._director?.ApplyEffects(challenge.SuccessEffects);
                }
                if (choice.Effects != null && choice.Effects.Count > 0)
                {
                    State._director?.ApplyEffects(choice.Effects);
                }

                string rewardsSummary = "";
                if (challenge.SuccessRewards != null)
                {
                    var rew = challenge.SuccessRewards;
                    if (rew.EarnCredits > 0 && sheet != null)
                    {
                        sheet.EarnCredits(rew.EarnCredits);
                        rewardsSummary += $"+{rew.EarnCredits} CE  ";
                    }
                    if (rew.EarnXP > 0 && sheet != null)
                    {
                        sheet.AvailableXP += rew.EarnXP;
                        sheet.TotalEarnedXP += rew.EarnXP;
                        rewardsSummary += $"+{rew.EarnXP} XP  ";
                    }
                    if (!string.IsNullOrEmpty(rew.ItemRewardName) && sheet != null)
                    {
                        GrantRewardItem(sheet, rew.ItemRewardName, rew.ItemRewardType, rew.ItemRewardQuantity, ref rewardsSummary);
                    }
                    if (rew.AdditionalItems != null)
                    {
                        for (int ri = 0; ri < rew.AdditionalItems.Count; ri++)
                        {
                            var extra = rew.AdditionalItems[ri];
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

                if (!string.IsNullOrEmpty(rewardsSummary))
                {
                    CombatHUD.Instance?.AddAdvancedLog($"🎁 <b>Récompenses obtenues :</b> {rewardsSummary}", LogCategory.MovementAndTurns, "[BUTIN]", Color.yellow);
                }

                string targetLineId = !string.IsNullOrWhiteSpace(challenge.SuccessConsequenceId) ? challenge.SuccessConsequenceId
                    : (!string.IsNullOrWhiteSpace(challenge.SuccessNextLineId) ? challenge.SuccessNextLineId : choice.NextLineId);
                string successVs = challenge.IsOpposed ? $"{result.Total} vs {oppTotal}" : $"{result.Total} vs SD {challenge.TargetDC}";
                string successSkill = challenge.IsOpposed ? $"{skillName} vs {oppSkillName}" : skillName;

                // Routage direct vers fenêtre normale (pas de réaction intermédiaire).
                CombatHUD.Instance?.AddAdvancedLog($"✔ {outcomeTag} ({successVs}) — {successSkill}{(string.IsNullOrEmpty(rewardsSummary) ? "" : $" 🎁 {rewardsSummary}")}", LogCategory.Combat, "[DÉFI]", Color.green);
                if (!TryAdvanceToId(targetLineId, null))
                    AdvanceDialogueOrNode();
            }
            else
            {
                if (KilltimeAudioManager.Instance != null)
                {
                    KilltimeAudioManager.Instance.Play(SoundId.Impact_DeepBoom, 0.6f);
                }
                // Conservé : micro-secousse d'IMPACT (feedback d'échec, pas un cadrage
                // ni un travel — les défis n'ont pas de sortie vers cartes 🎬).
                GrenadeCameraShake.Shake(0.2f);

                if (challenge.FailureEffects != null && challenge.FailureEffects.Count > 0)
                {
                    State._director?.ApplyEffects(challenge.FailureEffects);
                }

                if (challenge.FailureTriggersCombat)
                {
                    SpawnActorsForNode(State._director?.CurrentNode?.Id, true);
                    State._turnManager?.EnterCombatMode();
                    CombatHUD.Instance?.AddAdvancedLog("🚨 <b>ÉCHEC CRITIQUE :</b> Alerte déclenchée ! Déploiement d'urgence hostile.", LogCategory.Combat, "[ALERTE]", Color.red);
                }

                string targetLineId = !string.IsNullOrWhiteSpace(challenge.FailureConsequenceId) ? challenge.FailureConsequenceId
                    : (!string.IsNullOrWhiteSpace(challenge.FailureNextLineId) ? challenge.FailureNextLineId : choice.NextLineId);
                string failVs = challenge.IsOpposed ? $"{result.Total} vs {oppTotal}" : $"{result.Total} vs SD {challenge.TargetDC}";
                string failSkill = challenge.IsOpposed ? $"{skillName} vs {oppSkillName}" : skillName;

                // Routage direct vers fenêtre normale (pas de réaction intermédiaire).
                CombatHUD.Instance?.AddAdvancedLog($"✕ {outcomeTag} ({failVs}) — {failSkill}", LogCategory.Combat, "[DÉFI]", new Color(1f, 0.5f, 0.2f));
                if (!TryAdvanceToId(targetLineId, null))
                    AdvanceDialogueOrNode();
            }
        }

        private void SetupReactionDisplay(SceneDialogueLineData promptLine, string speech, string stageDirection, string nextLineId, string challengeSummary = "", string rewardsText = "")
        {
            State._activeReactionChoice = new SceneDialogueChoiceData
            {
                ReactionSpeech = speech,
                ReactionStageDirection = stageDirection,
                NextLineId = nextLineId
            };
            State._activeReactionPromptLine = promptLine;
            State._activeChallengeSummary = challengeSummary;
            State._activeChallengeRewards = rewardsText;

            // DÉ-HARDCODÉ : plus de focus caméra direct ici (migré en cartes 🎬 à tracking).

            if (KilltimeAudioManager.Instance != null)
            {
                KilltimeAudioManager.Instance.PlayUI(SoundId.UI_Filter, 0.45f);
            }

            string stage = !string.IsNullOrEmpty(stageDirection) ? $" <i>({stageDirection})</i>" : "";
            CombatHUD.Instance?.AddAdvancedLog($"<b>[{promptLine.SpeakerId}]</b>{stage} {speech}", LogCategory.MovementAndTurns, "[RÉPONSE]", Color.cyan);
        }

        private void ConfirmReactionAndAdvance()
        {
            if (State._activeReactionChoice == null) return;

            var choice = State._activeReactionChoice;
            State._activeReactionChoice = null;
            State._activeReactionPromptLine = null;
            State._activeChallengeSummary = "";
            State._activeChallengeRewards = "";

            // File d'attente du chat milieu (ex : holotable puis datapad) :
            // afficher le message suivant avant de reprendre le flux normal.
            if (State._pendingMiddleChat.Count > 0)
            {
                ShowMiddleChatMessage(State._pendingMiddleChat.Dequeue());
                return;
            }

            // Sentinelle info interactable (hérité) : dismiss pur, sans avancer
            // dialogue ni nœud — on retrouve l'état sous-jacent inchangé.
            if (string.Equals(choice.NextLineId, InteractableInfoReturnSentinel, StringComparison.Ordinal))
            {
                return;
            }

            if (!string.IsNullOrEmpty(choice.NextLineId))
            {
                if (TryAdvanceToId(choice.NextLineId, null)) return;
            }

            // Sans cible = fin du nœud (routage explicite).
            AdvanceDialogueOrNodeInternal(null, true);
        }
    }
}

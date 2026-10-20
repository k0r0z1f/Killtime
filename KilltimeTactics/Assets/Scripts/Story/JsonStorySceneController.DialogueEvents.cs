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
        private void ExecuteAutoSkillCheck(SceneDialogueLineData line)
        {
            var check = line.AutoSkillCheck;
            TacticalUnit actorUnit = null;

            if (!string.IsNullOrEmpty(check.SpecificActorId) && State._spawnedActors.TryGetValue(check.SpecificActorId, out var specificUnit))
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

            int training = sheet != null ? sheet.GetSkill(check.RequiredSkill).TrainingLevel : 0;
            // RD-203 (Livre II §7.2) : rang effectif = base (caracs) + entraînements.
            int baseRank = SkillDefinitions.GetBaseRank(check.RequiredSkill, effectiveAttr, false);
            int skillRank = baseRank + training;
            string skillName = SkillDefinitions.GetDisplayName(check.RequiredSkill);

            // RD-203 (Livre II §7) : seuil couvert par le rang → pas de jet, succès immédiat tracé.
            if (SkillAutoSuccess.IsAutoSuccess(skillRank, check.TargetDC))
            {
                string autoLog = SkillAutoSuccess.BuildAutoSuccessLog(skillName, skillRank, check.TargetDC);
                CombatHUD.Instance?.AddAdvancedLog(
                    $"🎲 <b>[TEST AUTO]</b> {actorName} — {autoLog} ➔ <b>SUCCÈS AUTOMATIQUE</b>",
                    LogCategory.Combat,
                    "[PASSİF]",
                    Color.green
                );

                actorUnit?.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText(
                    $"SUCCÈS AUTO [{SkillAutoSuccess.RankLabel(SkillAutoSuccess.ForSkillRank(skillRank))}]", Color.green);

                if (check.SuccessEffects != null && check.SuccessEffects.Count > 0)
                {
                    State._director?.ApplyEffects(check.SuccessEffects);
                }

                if (!string.IsNullOrEmpty(check.SuccessSpeech))
                {
                    line.Speech = check.SuccessSpeech;
                    if (!string.IsNullOrEmpty(check.SuccessStageDirection)) line.StageDirection = check.SuccessStageDirection;
                }

                if (!string.IsNullOrEmpty(check.SuccessNextLineId))
                {
                    line.NextLineId = check.SuccessNextLineId;
                }
                return;
            }

            int steps = SkillDefinitions.CharacteristicSteps(baseRank);
            DiceType die = SkillDefinitions.DieFromTotalSteps(steps + training);

            var roller = new DiceRoller();
            var result = roller.Roll(die, modifier: 0, targetDC: check.TargetDC);

            string outcomeStr = result.IsSuccess ? "SUCCÈS PASSİF" : "ÉCHEC PASSİF";
            Color col = result.IsSuccess ? Color.green : new Color(1.0f, 0.45f, 0.2f);

            CombatHUD.Instance?.AddAdvancedLog(
                $"🎲 <b>[TEST AUTO]</b> {actorName} teste <b>{skillName}</b> ({die}) contre SD {check.TargetDC} ➔ Résultat: <b>{result.Total}</b> ({result.Differential:+0;-0;0}) — <color=#{ColorUtility.ToHtmlStringRGB(col)}>{outcomeStr}</color>",
                LogCategory.Combat,
                "[PASSİF]",
                col
            );

            actorUnit?.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText($"{outcomeStr} ({result.Total} vs SD {check.TargetDC})", col);

            if (result.IsSuccess)
            {
                if (check.SuccessEffects != null && check.SuccessEffects.Count > 0)
                {
                    State._director?.ApplyEffects(check.SuccessEffects);
                }

                if (!string.IsNullOrEmpty(check.SuccessSpeech))
                {
                    line.Speech = check.SuccessSpeech;
                    if (!string.IsNullOrEmpty(check.SuccessStageDirection)) line.StageDirection = check.SuccessStageDirection;
                }

                if (!string.IsNullOrEmpty(check.SuccessNextLineId))
                {
                    line.NextLineId = check.SuccessNextLineId;
                }
            }
            else
            {
                if (check.FailureEffects != null && check.FailureEffects.Count > 0)
                {
                    State._director?.ApplyEffects(check.FailureEffects);
                }

                if (!string.IsNullOrEmpty(check.FailureSpeech))
                {
                    line.Speech = check.FailureSpeech;
                    if (!string.IsNullOrEmpty(check.FailureStageDirection)) line.StageDirection = check.FailureStageDirection;
                }

                if (!string.IsNullOrEmpty(check.FailureNextLineId))
                {
                    line.NextLineId = check.FailureNextLineId;
                }
            }
        }

        public void ExecuteNodeEvents(SceneNodeData nodeData)
        {
            if (nodeData == null || nodeData.Events.Count == 0) return;

            for (int i = 0; i < nodeData.Events.Count; i++)
            {
                ExecuteScenicEvent(nodeData.Events[i]);
            }
        }

        public void ExecuteScenicEvent(SceneEventData evt, bool firePredecessors = true)
        {
            if (evt == null) return;

            // Cinématiques placées en amont de l'événement (sauf arrivée via chaîne).
            if (firePredecessors) FirePredecessorCinematics(evt.EventId);

            // Prérequis de l'événement
            if (evt.Prerequisite != null && evt.Prerequisite.HasPrerequisite)
            {
                if (!evt.Prerequisite.IsMet(State._playerParty, State._turnManager?.ActiveUnit, State._director))
                {
                    CombatHUD.Instance?.AddAdvancedLog($"<color=grey>[ÉVÉNEMENT IGNORÉ] '{evt.Title}' ({evt.Prerequisite.GetSummary()}).</color>", LogCategory.MovementAndTurns, "[ÉVÉNEMENT]", Color.gray);
                    return;
                }
            }

            // Marque la carte comme résolue pour ce nœud (anti double-payload via chaîne).
            if (!string.IsNullOrEmpty(evt.EventId)) State._resolvedCardIdsThisNode.Add(evt.EventId);

            // Cinématiques liées à l'événement : non-bloquantes.
            FireLinkedCinematics(evt.CinematicIds);

            // Cartes son 🔊 : carte instantanée → début et fin ensemble
            // (le délai de chaque son les sépare dans le temps).
            FireLinkedSounds(evt.EventId, SoundTriggerPoint.AtLinkedStart);
            FireLinkedSounds(evt.EventId, SoundTriggerPoint.AtLinkedEnd);
            EvaluateLinkedAmbiences(evt.EventId);

            // Ambiance & Audio
            ApplyAmbienceSettings(evt.Ambience, "");

            // Action spécifique de l'événement
            switch (evt.Kind)
            {
                case SceneEventKind.TriggerCombat:
                    SpawnActorsForNode(State._director?.CurrentNode?.Id, true);
                    State._turnManager?.EnterCombatMode();
                    CombatHUD.Instance?.AddAdvancedLog($"⚡ <b>ÉVÉNEMENT :</b> Combat déclenché [{evt.Title}].", LogCategory.Combat, "[COMBAT]", Color.red);
                    break;

                case SceneEventKind.SpawnEnemies:
                    SpawnActorsForNode(State._director?.CurrentNode?.Id, true);
                    State._turnManager?.EnterCombatMode();
                    CombatHUD.Instance?.AddAdvancedLog($"⚡ <b>ÉVÉNEMENT :</b> Déploiement d'unités [{evt.Title}].", LogCategory.Combat, "[RENFORTS]", Color.red);
                    break;

                // DÉ-HARDCODÉS : kinds remplacés par des cartes 🎬 (tracking / effet relatif).
                // Ignorés avec un avertissement de migration (1x par event pour ne pas spammer).
                case SceneEventKind.CameraFocus:
                    CombatHUD.Instance?.AddAdvancedLog($"<color=grey>[OBSOLÈTE] Événement '{evt.Title}' : kind CameraFocus ignoré — migrer vers une carte 🎬 à tracking.</color>", LogCategory.MovementAndTurns, "[ÉVÉNEMENT]", Color.gray);
                    break;

                case SceneEventKind.CameraShake:
                    CombatHUD.Instance?.AddAdvancedLog($"<color=grey>[OBSOLÈTE] Événement '{evt.Title}' : kind CameraShake ignoré — migrer vers une carte 🎬 à effet.</color>", LogCategory.MovementAndTurns, "[ÉVÉNEMENT]", Color.gray);
                    break;

                case SceneEventKind.ObjectiveUpdate:
                    if (!string.IsNullOrEmpty(evt.CompletionObjectiveId))
                    {
                        State._director?.SetObjectiveComplete(evt.CompletionObjectiveId, true);
                    }
                    break;

                case SceneEventKind.GrantRewards:
                    if (evt.Rewards != null && State._playerParty.Count > 0 && State._playerParty[0].Sheet != null)
                    {
                        var s = State._playerParty[0].Sheet;
                        if (evt.Rewards.EarnCredits > 0) s.EarnCredits(evt.Rewards.EarnCredits);
                        if (evt.Rewards.EarnXP > 0) { s.AvailableXP += evt.Rewards.EarnXP; s.TotalEarnedXP += evt.Rewards.EarnXP; }
                        if (!string.IsNullOrEmpty(evt.Rewards.ItemRewardName))
                            s.AddItem(new InventoryItem { Name = evt.Rewards.ItemRewardName, Type = evt.Rewards.ItemRewardType });
                    }
                    break;
            }

            // Effets de campagne de l'événement
            if (evt.Effects != null && evt.Effects.Count > 0)
            {
                State._director?.ApplyEffects(evt.Effects);
            }

            CombatHUD.Instance?.AddAdvancedLog($"⚡ <b>ÉVÉNEMENT EXÉCUTÉ :</b> {evt.Title}", LogCategory.MovementAndTurns, "[ÉVÉNEMENT]", Color.yellow);
        }

        private void AdvanceDialogueOrNode()
        {
            AdvanceDialogueOrNodeInternal(null, false);
        }

        private void AdvanceDialogueOrNodeInternal(HashSet<string> visited, bool skipLink = false)
        {
            var dataNode = State._sceneData.FindNode(State._director.CurrentNode?.Id);
            if (dataNode != null && State._currentDialogueIndex >= 0 && State._currentDialogueIndex < dataNode.Dialogues.Count)
            {
                var currentLine = dataNode.Dialogues[State._currentDialogueIndex];
                if (!skipLink && !string.IsNullOrEmpty(currentLine.NextLineId))
                {
                    if (TryAdvanceToId(currentLine.NextLineId, visited)) return;
                }

                // Liaison vide ou cible introuvable = fin du nœud (pas de fallback
                // séquentiel : le routage est explicite via les fenêtres normales).
            }

            var node = State._director.CurrentNode;
            if (node == null) return;

            if (node.Kind == ScenarioNodeKind.Resolution)
            {
                if (TryConsumePendingJump()) return;
                if (TryReturnFromTalk()) return;
                if (!TryFireReadyTrigger()) State._director.CompleteActiveScenario();
            }
            else if (node.Kind == ScenarioNodeKind.Objective && !State._director.AreRequiredObjectivesComplete())
            {
                if (TryReturnFromTalk()) return;
                if (!TryFireReadyTrigger())
                    CombatHUD.Instance?.AddAdvancedLog("🎯 <b>Objectifs requis incomplets.</b> Terminez les objectifs principaux pour poursuivre.", LogCategory.MovementAndTurns, "[OBJECTIF]", Color.yellow);
            }
            else
            {
                if (TryConsumePendingJump()) return;
                if (TryReturnFromTalk()) return;
                if (!TryFireReadyTrigger() && !State._director.Continue()) State._director.CompleteActiveScenario();
            }
        }

        // Avance vers une réplique ou une conséquence (carte 🎁). Retourne false si l'id
        // est vide, introuvable, ou si une chaîne de conséquences boucle (anti-blocage).
        // Avance vers une réplique ou une conséquence, en jouant d'abord les
        // cinématiques placées en amont (sauf si firePredecessors=false : arrivée
        // via chaîne "fin →", dont la cinématique vient de se terminer).
        private bool TryAdvanceToId(string id, HashSet<string> visited, bool firePredecessors = true)
        {
            if (string.IsNullOrEmpty(id)) return false;
            var dataNode = State._sceneData.FindNode(State._director.CurrentNode?.Id);
            if (dataNode == null) return false;

            int targetIdx = dataNode.FindDialogueIndex(id);
            if (targetIdx >= 0)
            {
                // Garde anti-boucle : ré-atterrir sur la réplique courante (ex : fin
                // de sa propre cinématique amont) ne rejoue ni les amonts ni la mise
                // en scène (qui rejouerait les cinématiques d'entrée de la réplique).
                bool changed = State._currentDialogueIndex != targetIdx;
                State._currentDialogueIndex = targetIdx;
                if (changed && firePredecessors) FirePredecessorCinematics(id);
                StageCurrentLine();
                return true;
            }

            var cons = dataNode.FindConsequence(id);
            if (cons != null)
            {
                SceneDialogueLineData prompt = null;
                if (State._currentDialogueIndex >= 0 && State._currentDialogueIndex < dataNode.Dialogues.Count)
                    prompt = dataNode.Dialogues[State._currentDialogueIndex];
                return ExecuteConsequenceChain(cons, prompt, visited ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase), firePredecessors);
            }

            return false;
        }

        /// <summary>
        /// Cinématiques placées EN AMONT d'une carte (leur sortie "fin →" pointe vers
        /// cardId) : jouées fire-and-forget quand la carte est atteinte. L'auto-lien
        /// est exclu (l'éditeur l'interdit déjà). Les doublons sont dédupliqués.
        /// Amorçage automatique : si une autre cinématique pointe vers un prédécesseur
        /// (chaîne cine_11 ─fin→ cine_1 ─fin→ ligne), c'est la TÊTE de chaîne qui
        /// démarre (voir FindCinematicChainHead) — la première carte de la chaîne,
        /// le reste suit via Fin→. Sans amont, comportement inchangé.
        /// </summary>
        private bool FirePredecessorCinematics(string cardId)
        {
            if (string.IsNullOrWhiteSpace(cardId) || State._sceneData == null || State._sceneData.Cinematics == null) return false;
            List<string> ids = null;
            for (int i = 0; i < State._sceneData.Cinematics.Count; i++)
            {
                var c = State._sceneData.Cinematics[i];
                if (c == null || string.IsNullOrWhiteSpace(c.NextTargetId)) continue;
                if (!string.Equals(c.NextTargetId.Trim(), cardId.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(c.CinematicId, cardId, StringComparison.OrdinalIgnoreCase)) continue;
                // Remonte la chaîne : la tête démarre, le reste suit via Fin→.
                string head = c.CinematicId;
                try { head = State._sceneData.FindCinematicChainHead(c.CinematicId) ?? c.CinematicId; }
                catch { head = c.CinematicId; }
                ids ??= new List<string>();
                bool dup = false;
                for (int k = 0; k < ids.Count; k++)
                    if (string.Equals(ids[k], head, StringComparison.OrdinalIgnoreCase)) { dup = true; break; }
                if (!dup) ids.Add(head);
            }
            if (ids != null && ids.Count > 0)
            {
                FireLinkedCinematics(ids);
                return true;
            }
            return false;
        }
    }
}

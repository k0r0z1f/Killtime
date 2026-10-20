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
        /// <summary>
        /// Résolution d'une action d'interactable depuis la fenêtre flottante 887.
        /// Gère les tests de compétence, la consommation one-shot, les objectifs,
        /// les logs et le saut différé si conversation en cours.
        /// </summary>
        private void AttemptInteractableAction(TacticalUnit unit, SceneInteractableSpawnData data, SceneInteractableAction action)
        {
            if (data == null || action == null) return;

            var prop = TryGetSpawnedInteractable(data.InteractableId, out var p) ? p : null;
            if (prop != null && !prop.CanInteract(unit))
            {
                string actorName = unit?.Stats?.Name ?? "Escouade";
                string msg = $"{actorName} est trop éloigné de {data.DisplayName}.";
                CombatHUD.Instance?.AddAdvancedLog(msg, LogCategory.MovementAndTurns, "[ACTION]", Color.yellow);
                InteractableWindow.Instance?.SetResult(false, msg, "");
                return;
            }

            bool success = true;
            string resultLog = action.SuccessLog;
            if (action.RequiredSkill >= 0 && action.SkillThreshold > 0 && unit != null && unit.Stats != null)
            {
                var skill = (SkillType)action.RequiredSkill;
                // RD-203 (Livre II §7.2) : rang effectif = base (caracs) + entraînements.
                // isOffensive:true comme le chemin jet ci-dessous (même base GetSkillDie).
                int skillRank = unit.Stats.GetSkillRank(skill, isOffensive: true);
                // RD-203 (Livre II §7) : seuil couvert par le rang → pas de jet, succès immédiat tracé.
                if (SkillAutoSuccess.IsAutoSuccess(skillRank, action.SkillThreshold))
                {
                    string autoLog = SkillAutoSuccess.BuildAutoSuccessLog(
                        SkillDefinitions.GetDisplayName(skill), skillRank, action.SkillThreshold);
                    CombatHUD.Instance?.AddAdvancedLog(
                        $"✔ {autoLog}", LogCategory.MovementAndTurns, "[ACTION]", Color.green);
                    resultLog = !string.IsNullOrWhiteSpace(action.SuccessLog)
                        ? action.SuccessLog
                        : $"Succès automatique {SkillDefinitions.GetDisplayName(skill)} (seuil auto {SkillAutoSuccess.AutoThresholdFor(skillRank)} vs SD {action.SkillThreshold}) !";
                }
                else
                {
                    var die = unit.Stats.GetSkillDie(skill, isOffensive: true);
                    int mod = unit.Stats.GetSkillModifier(skill, isOffensive: true);
                    var roller = new DiceRoller();
                    var roll = roller.Roll(die, mod);

                    if (roll.Total < action.SkillThreshold)
                    {
                        success = false;
                        resultLog = !string.IsNullOrWhiteSpace(action.FailureLog)
                            ? action.FailureLog
                            : $"Échec du test {SkillDefinitions.GetDisplayName(skill)} ({roll.Total} vs SD {action.SkillThreshold}).";
                        if (KilltimeAudioManager.Instance != null)
                            KilltimeAudioManager.Instance.PlayUI(SoundId.UI_Denied, 0.7f);
                    }
                    else
                    {
                        resultLog = !string.IsNullOrWhiteSpace(action.SuccessLog)
                            ? action.SuccessLog
                            : $"Succès {SkillDefinitions.GetDisplayName(skill)} validé ({roll.Total} vs SD {action.SkillThreshold}) !";
                    }
                }
            }

            if (success)
            {
                HandleInteractableResolved(unit, data, action, true, resultLog);
            }
            else
            {
                string actorName = unit != null && unit.Stats != null && !string.IsNullOrEmpty(unit.Stats.Name)
                    ? unit.Stats.Name
                    : (!string.IsNullOrEmpty(data.DisplayName) ? data.DisplayName : "Escouade");
                CombatHUD.Instance?.AddAdvancedLog($"❌ {actorName} : {resultLog}", LogCategory.MovementAndTurns, "[ACTION]", Color.yellow);
                InteractableWindow.Instance?.SetResult(false, resultLog, "");
            }
        }

        /// <summary>
        /// Point central de résolution d'une action d'interactable (succès).
        /// Conserve les cinématiques/sons/objectifs et le saut différé.
        /// </summary>
        private void HandleInteractableResolved(TacticalUnit unit, SceneInteractableSpawnData data,
            SceneInteractableAction action, bool success, string resultLog)
        {
            if (data == null || action == null) return;

            if (!string.IsNullOrEmpty(data.InteractableId))
            {
                MarkInteractableActivated(data.InteractableId);
            }

            // Cinématiques liées à l'interactable : non-bloquantes.
            FireLinkedCinematics(data.CinematicIds);
            // Cartes son 🔊 : carte instantanée → début et fin ensemble.
            FireLinkedSounds(data.InteractableId, SoundTriggerPoint.AtLinkedStart);
            FireLinkedSounds(data.InteractableId, SoundTriggerPoint.AtLinkedEnd);
            EvaluateLinkedAmbiences(data.InteractableId);

            if (!string.IsNullOrEmpty(action.CompletionObjectiveId))
            {
                State._director?.SetObjectiveComplete(action.CompletionObjectiveId, true);
            }

            string actorName = unit != null && unit.Stats != null && !string.IsNullOrEmpty(unit.Stats.Name)
                ? unit.Stats.Name
                : (!string.IsNullOrEmpty(data.DisplayName) ? data.DisplayName : "Escouade");
            if (!string.IsNullOrWhiteSpace(resultLog))
            {
                CombatHUD.Instance?.AddAdvancedLog($"✔ {actorName} : {resultLog}", LogCategory.MovementAndTurns, "[ACTION]", Color.yellow);
            }

            // Consommation one-shot au niveau action.
            if (action.IsOneShot && !string.IsNullOrWhiteSpace(data.InteractableId) && !string.IsNullOrWhiteSpace(action.ActionId))
            {
                State._consumedActionKeys.Add($"{data.InteractableId}/{action.ActionId}");
            }

            // Pour les interactables legacy (Actions vide), on garde le popup monde.
            bool isLegacy = data.Actions == null || data.Actions.Count == 0;
            bool hasJump = !string.IsNullOrWhiteSpace(action.TriggerNodeId);
            if (isLegacy && (!string.IsNullOrWhiteSpace(resultLog) || hasJump))
            {
                ShowInteractablePopup(data, hasJump, action.TriggerNodeId);
            }

            if (hasJump)
            {
                if (IsConversationBusy())
                {
                    State._pendingNodeJump = action.TriggerNodeId.Trim();
                    State._pendingNodeJumpSource = data.InteractableId ?? "";
                    CombatHUD.Instance?.AddAdvancedLog($"⚡ <b>Suite disponible :</b> {data.DisplayName} ➔ {action.TriggerNodeId} (finissez la conversation, puis [Suivre]).", LogCategory.MovementAndTurns, "[ACTION]", Color.yellow);
                }
                else if (!ExecuteInteractableNodeJump(action.TriggerNodeId))
                {
                    RemoveInteractableJumpOffer(data.InteractableId);
                }
            }

            InteractableWindow.Instance?.SetResult(true, resultLog, action.TriggerNodeId);

            if (unit != null)
            {
                unit.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText($"✔ {action.Label}", Color.green);
            }
        }

        /// <summary>
        /// Compatibilité legacy : l'interaction directe (OnInteractionTriggered)
        /// reste disponible pour les tests et les objets sans Actions explicites.
        /// </summary>
        private void HandleInteractableResolvedLegacy(TacticalUnit unit, SceneInteractableSpawnData data)
        {
            if (data == null) return;
            var legacy = new SceneInteractableAction
            {
                ActionId = "legacy",
                Label = data.ActionLabel,
                RequiredSkill = (int)data.RequiredSkill,
                SkillThreshold = data.SkillThreshold,
                SuccessLog = data.SuccessLog,
                FailureLog = data.FailureLog,
                CompletionObjectiveId = data.CompletionObjectiveId,
                TriggerNodeId = data.TriggerNodeId,
                IsOneShot = data.IsOneShot
            };
            HandleInteractableResolved(unit, data, legacy, true, data.SuccessLog);
        }

        /// <summary>
        /// Conversation en cours = une réplique est affichée (ou un choix / une
        /// réaction attend une validation). Hors dialogue (nœud objectif sans
        /// répliques), le saut reste immédiat comme avant.
        /// </summary>
        private bool IsConversationBusy()
        {
            // Choix en attente ou réaction affichée : dialogue non terminé.
            if (CurrentLineHasPendingChoice()) return true;
            var dataNode = State._sceneData != null && State._director != null ? State._sceneData.FindNode(State._director.CurrentNode?.Id) : null;
            if (dataNode == null || dataNode.Dialogues == null || dataNode.Dialogues.Count == 0) return false;
            return State._currentDialogueIndex >= 0 && State._currentDialogueIndex < dataNode.Dialogues.Count;
        }

        private SceneInteractableSpawnData FindInteractableData(TacticalInteractable prop)
        {
            if (prop == null) return null;
            return FindInteractableDataById(prop.InteractableId);
        }

        private SceneInteractableSpawnData FindInteractableDataById(string interactableId)
        {
            if (State._sceneData == null || State._sceneData.Interactables == null || string.IsNullOrWhiteSpace(interactableId)) return null;
            for (int i = 0; i < State._sceneData.Interactables.Count; i++)
            {
                var data = State._sceneData.Interactables[i];
                if (data != null && string.Equals(data.InteractableId, interactableId, StringComparison.OrdinalIgnoreCase))
                    return data;
            }
            return null;
        }

        private void OpenInteractableWindow(TacticalUnit unit, SceneInteractableSpawnData data)
        {
            InteractableWindow.Open(data, unit,
                onAttemptAction: (u, d, a) => AttemptInteractableAction(u, d, a),
                onJump: (nodeId) => ExecuteInteractableNodeJump(nodeId),
                isActionConsumed: (key) => State._consumedActionKeys.Contains(key));
        }

        private void CheckProximityObjectives()
        {
            if (State._director == null) return;
            var node = State._director.CurrentNode;
            if (node == null || node.Kind != ScenarioNodeKind.Objective) return;

            // Garde anti-skip d'entrée : Update() appelle CheckProximity AVANT
            // UpdateNodeState. Sur la frame d'arrivée sur un nouveau nœud,
            // State._nodeEnterCoords contient encore les positions d'entrée du nœud
            // PRÉCÉDENT. Si le joueur a bougé pendant le nœud précédent
            // (ex : 12 dialogues = le temps de se pré-positionner sur
            // l'holotable, qui est déjà à portée du spawn), moved=true et la
            // validation passe instantanément, puis le trigger saute la
            // narration. Avec un nœud intermédiaire vide (pas le temps de
            // bouger), moved=false + wasInRange=true => skip, par chance.
            // On refresh donc le snapshot et on saute la validation sur la
            // frame de transition, quelle que soit la longueur du chaînage.
            if (!string.Equals(State._lastNodeId, node.Id, StringComparison.OrdinalIgnoreCase))
            {
                State._nodeEnterCoords.Clear();
                for (int p = 0; p < State._playerParty.Count; p++)
                {
                    var u = State._playerParty[p];
                    if (u != null) State._nodeEnterCoords[u] = u.CurrentCoords;
                }
                return;
            }

            for (int p = 0; p < State._playerParty.Count; p++)
            {
                var member = State._playerParty[p];
                if (member == null) continue;

                for (int i = 0; i < State._sceneData.Interactables.Count; i++)
                {
                    var it = State._sceneData.Interactables[i];
                    if (!string.IsNullOrEmpty(it.CompletionObjectiveId) && !State._director.IsObjectiveComplete(it.CompletionObjectiveId))
                    {
                        var target = new HexCoordinates(it.Q, it.R);
                        if (member.CurrentCoords.DistanceTo(target) <= it.Radius)
                        {
                            // Pas de validation gratuite : un membre immobile depuis l'entrée
                            // du nœud et déjà à portée (spawn sur l'objectif) ne valide pas.
                            // Il faut s'éloigner/revenir, bouger, ou activer à la main (E).
                            bool hasEntry = State._nodeEnterCoords.TryGetValue(member, out var enterCoords);
                            bool moved = !hasEntry || !enterCoords.Equals(member.CurrentCoords);
                            bool wasInRange = hasEntry && enterCoords.DistanceTo(target) <= it.Radius;
                            if (!moved && wasInRange) continue;
                            State._director.SetObjectiveComplete(it.CompletionObjectiveId, true);
                            if (!string.IsNullOrEmpty(it.InteractableId))
                            {
                                MarkInteractableActivated(it.InteractableId);
                            }
                            string memberName = member.Stats != null ? member.Stats.Name : "Escouade";
                            if (!string.IsNullOrWhiteSpace(it.SuccessLog))
                            {
                                CombatHUD.Instance?.AddAdvancedLog($"✔ {memberName} : {it.SuccessLog}", LogCategory.MovementAndTurns, "[ACTION]", Color.yellow);
                                // Validation par proximité : info non-bloquante (log +
                                // popup monde), jamais de hijack du chat central ni
                                // de saut immédiat (le saut se fait à la touche E).
                                ShowInteractablePopup(it, false);
                            }
                            member.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText($"✔ Objectif Validé", Color.green);
                        }
                    }
                }
            }
        }

        private void MarkInteractableActivated(string interactableId)
        {
            if (string.IsNullOrEmpty(interactableId)) return;
            State._activatedInteractableIds.Add(interactableId);
            try { State._activationNodeIds[interactableId] = State._director != null && State._director.CurrentNode != null ? State._director.CurrentNode.Id : ""; }
            catch { State._activationNodeIds[interactableId] = ""; }
        }
    }
}

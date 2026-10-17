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
        private void ShowInteractablePopup(SceneInteractableSpawnData data, bool hasJump, string triggerNodeId = null)
        {
            if (data == null) return;
            string targetNode = (triggerNodeId ?? data.TriggerNodeId ?? "").Trim();
            for (int i = State._activePopups.Count - 1; i >= 0; i--)
            {
                var p = State._activePopups[i];
                if (p != null && string.Equals(p.InteractableId, data.InteractableId, StringComparison.OrdinalIgnoreCase))
                    State._activePopups.RemoveAt(i);
            }
            State._activePopups.Add(new InteractablePopup
            {
                InteractableId = data.InteractableId ?? "",
                DisplayName = data.DisplayName ?? "",
                Message = data.SuccessLog ?? "",
                TargetNodeId = hasJump ? targetNode : "",
                HasJump = hasJump && !string.IsNullOrWhiteSpace(targetNode),
                CreatedUnscaledTime = Time.unscaledTime
            });
        }

        private void RemoveInteractableJumpOffer(string interactableId)
        {
            for (int i = State._activePopups.Count - 1; i >= 0; i--)
            {
                var p = State._activePopups[i];
                if (p == null || !p.HasJump) continue;
                if (string.IsNullOrEmpty(interactableId)
                    || string.Equals(p.InteractableId, interactableId, StringComparison.OrdinalIgnoreCase))
                    State._activePopups.RemoveAt(i);
            }
        }

        /// <summary>
        /// Exécute le saut vers le nœud cible (consentement explicite ou sortie
        /// naturelle). Retourne false si la cible est introuvable.
        /// </summary>
        private bool ExecuteInteractableNodeJump(string targetNodeId)
        {
            string target = (targetNodeId ?? "").Trim();
            if (string.IsNullOrEmpty(target) || State._sceneData == null) return false;
            if (State._sceneData.FindNode(target) == null)
            {
                // Cible peut être un id de choix scénario (fallback Choose historique).
                if (State._director != null && State._director.Choose(target))
                {
                    ClearPendingNodeJump();
                    return true;
                }
                Debug.LogWarning($"[JsonStorySceneController] Interactable : nœud cible introuvable '{target}'.");
                CombatHUD.Instance?.AddAdvancedLog($"<color=grey>[ACTION] Cible introuvable : '{target}'.</color>", LogCategory.MovementAndTurns, "[ACTION]", Color.gray);
                return false;
            }
            string currentId = State._director != null && State._director.CurrentNode != null ? State._director.CurrentNode.Id : "";
            if (string.Equals(currentId, target, StringComparison.OrdinalIgnoreCase))
            {
                // Saut vers le nœud courant : pas de MoveTo (sans effet), on
                // remet juste en scène la réplique et on solde le différé.
                ClearPendingNodeJump();
                StageCurrentLine();
                return true;
            }
            bool ok = State._director != null && State._director.GoToNode(target);
            if (!ok && State._director != null) ok = State._director.Choose(target);
            if (ok)
            {
                CombatHUD.Instance?.AddAdvancedLog($"⚡ <b>Saut :</b> ➔ {target}", LogCategory.MovementAndTurns, "[ACTION]", Color.yellow);
                ClearPendingNodeJump();
                RemoveInteractableJumpOffer(null);
            }
            return ok;
        }

        private void ClearPendingNodeJump()
        {
            State._pendingNodeJump = "";
            State._pendingNodeJumpSource = "";
        }

        /// <summary>
        /// Consomme le saut différé à la sortie naturelle du nœud (bouton
        /// Continuer / fin de dialogue). Prioritaire sur les triggers et le
        /// chaînage normal : c'était l'intention directe du joueur (touche E).
        /// Jamais sur un nœud Objectif aux requis incomplets (pas de fuite).
        /// </summary>
        private bool TryConsumePendingJump()
        {
            if (string.IsNullOrWhiteSpace(State._pendingNodeJump)) return false;
            var node = State._director != null ? State._director.CurrentNode : null;
            if (node != null && node.Kind == ScenarioNodeKind.Objective && !State._director.AreRequiredObjectivesComplete())
                return false;
            return ExecuteInteractableNodeJump(State._pendingNodeJump);
        }

        private void FollowPopupJump(InteractablePopup popup)
        {
            if (popup == null || string.IsNullOrWhiteSpace(popup.TargetNodeId)) return;
            ExecuteInteractableNodeJump(popup.TargetNodeId);
        }

        private void DismissPopup(InteractablePopup popup)
        {
            if (popup == null) return;
            State._activePopups.Remove(popup);
            if (popup.HasJump
                && !string.IsNullOrEmpty(State._pendingNodeJumpSource)
                && string.Equals(State._pendingNodeJumpSource, popup.InteractableId, StringComparison.OrdinalIgnoreCase))
            {
                ClearPendingNodeJump();
                CombatHUD.Instance?.AddAdvancedLog("<color=grey>[ACTION] Suite ignorée (reste sur place).</color>", LogCategory.MovementAndTurns, "[ACTION]", Color.gray);
            }
        }

        private void PruneInteractablePopups()
        {
            if (State._activePopups.Count == 0) return;
            float now = Time.unscaledTime;
            // Souris en coordonnées GUI (origine en haut à gauche, comme les Rect IMGUI).
            Vector2 mouseGui = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            for (int i = State._activePopups.Count - 1; i >= 0; i--)
            {
                var p = State._activePopups[i];
                if (p == null) { State._activePopups.RemoveAt(i); continue; }
                // Les offres de saut persistent jusqu'à [Suivre]/[Rester].
                if (p.HasJump) continue;
                // Survol du popup : le timer de fondu est mis en pause (la
                // création est repoussée d'une frame, donc le compteur fige
                // tant que la souris reste dessus).
                if (p.LastScreenRect.width > 0f && p.LastScreenRect.Contains(mouseGui))
                {
                    p.CreatedUnscaledTime += Time.unscaledDeltaTime;
                    continue;
                }
                if (now - p.CreatedUnscaledTime >= InteractablePopupAutoCloseDelay + InteractablePopupFadeDuration)
                    State._activePopups.RemoveAt(i);
            }
        }

        /// <summary>
        /// HÉRITÉ (plus aucun appelant) : l'ancien chemin envoyait le SuccessLog
        /// dans le chat central via le slot Réaction, ce qui cassait le flot de
        /// conversation (une réplique sautée au Continuer). Conservé pour compat,
        /// le chemin actuel est log latéral + popup monde (ShowInteractablePopup).
        /// </summary>
        private void ShowInteractableSuccessInMiddleChat(TacticalUnit unit, SceneInteractableSpawnData data)
        {
            if (unit == null || data == null) return;
            if (string.IsNullOrWhiteSpace(data.SuccessLog)) return;

            string speaker = !string.IsNullOrEmpty(unit.Stats?.Name)
                ? unit.Stats.Name
                : (!string.IsNullOrEmpty(data.DisplayName) ? data.DisplayName : "Escouade");
            var msg = new PendingMiddleChatMessage
            {
                Speaker = speaker,
                StageDirection = data.ActionLabel ?? "",
                Speech = data.SuccessLog,
                ActionLabel = data.ActionLabel ?? "",
                DisplayName = data.DisplayName ?? "",
                Rewards = !string.IsNullOrEmpty(data.CompletionObjectiveId) ? "✔ Objectif validé" : ""
            };
            if (State._activeReactionChoice != null)
            {
                State._pendingMiddleChat.Enqueue(msg);
                return;
            }
            ShowMiddleChatMessage(msg);
        }

        // Sentinelle : info interactable non-destructive. Le Continuer déjà
        // présent (Suivant dialogue, bouton objectif...) doit rester visible
        // et suivre le prochain chat : on dismiss sans avancer dialogue/nœud,
        // on retrouve l'état sous-jacent (State._currentDialogueIndex inchangé).
        private const string InteractableInfoReturnSentinel = "__INTERACTABLE_INFO__";

        private void ShowMiddleChatMessage(PendingMiddleChatMessage msg)
        {
            var prompt = new SceneDialogueLineData
            {
                SpeakerId = msg.Speaker,
                StageDirection = msg.StageDirection
            };
            SetupReactionDisplay(prompt, msg.Speech, msg.ActionLabel, InteractableInfoReturnSentinel, $"🔧 {msg.DisplayName}", msg.Rewards);
        }
    }
}

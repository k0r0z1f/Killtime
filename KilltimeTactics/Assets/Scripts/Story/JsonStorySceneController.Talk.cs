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
        // Résolution d'affichage du locuteur — logique : SceneTalkService.ResolveTalkSpeakerDisplay.
        private string ResolveTalkSpeakerDisplay(string talkSpeakerId, SceneActorSpawnData targetActor, TacticalUnit targetUnit)
            => SceneTalkService.ResolveTalkSpeakerDisplay(State._sceneData != null ? State._sceneData.Actors : null, talkSpeakerId, targetActor, targetUnit);

        /// <summary>
        /// Action "Parler à" (menu contextuel carte) : enclenche la sortie 💬 de
        /// la carte 👥 Acteur de la cible qui correspond à celui qui parle.
        /// - L'entrée dont le Qui correspond au talker (ActorId / DisplayName /
        ///   nom, insensible à la casse) gagne ; sinon l'entrée sans Qui (défaut,
        ///   n'importe qui).
        /// - Cible = NodeId → le nœud est enclenché (saut).
        /// - Cible = LineId du nœud courant → saut dialogue normal (choix /
        ///   défis / chaînage préservés, via TryAdvanceToId).
        /// - Cible = LineId d'un autre nœud → saut vers ce nœud (enclenché) puis
        ///   réplique en attente (State._pendingTalkLineId).
        /// - Sans entrée correspondante : bavardage d'ambiance par défaut.
        /// </summary>
        public bool TalkToActor(TacticalUnit talker, TacticalUnit target)
        {
            if (target == null || target.Stats == null || !target.Stats.IsAlive) return false;
            string talkerName = (talker != null && talker.Stats != null && !string.IsNullOrWhiteSpace(talker.Stats.Name))
                ? talker.Stats.Name : "Escouade";
            var actorData = FindActorDataForUnit(target);
            var entries = actorData != null ? actorData.TalkEntries : null;

            SceneActorTalkEntry match = null;
            SceneActorTalkEntry fallback = null;
            if (entries != null && entries.Count > 0)
            {
                var talkerActor = FindActorDataForUnit(talker);
                string k1 = talkerActor != null ? (talkerActor.ActorId ?? "").Trim() : "";
                string k2 = talkerActor != null ? (talkerActor.DisplayName ?? "").Trim() : "";
                string k3 = talker != null && talker.Stats != null ? (talker.Stats.Name ?? "").Trim() : "";
                for (int i = 0; i < entries.Count; i++)
                {
                    var e = entries[i];
                    if (e == null || string.IsNullOrWhiteSpace(e.TargetId)) continue;
                    string who = (e.SpeakerId ?? "").Trim();
                    if (string.IsNullOrEmpty(who)) { fallback ??= e; continue; }
                    if ((!string.IsNullOrEmpty(k1) && string.Equals(who, k1, StringComparison.OrdinalIgnoreCase))
                        || (!string.IsNullOrEmpty(k2) && string.Equals(who, k2, StringComparison.OrdinalIgnoreCase))
                        || (!string.IsNullOrEmpty(k3) && string.Equals(who, k3, StringComparison.OrdinalIgnoreCase)))
                    {
                        match = e;
                        break;
                    }
                }
                match ??= fallback;
            }

            if (match == null)
            {
                string speaker = ResolveTalkSpeakerDisplay("", actorData, target);
                string targetName = target.Stats.Name;
                CombatHUD.Instance?.AddAdvancedLog($"💬 <b>{talkerName}</b> parle à <b>{targetName}</b> — <b>[{speaker}]</b> <i>hoche la tête — rien à signaler pour l'instant.</i> <color=grey>(carte 👥 sans sortie 💬 pour {talkerName} : ajoutez une rangée Parler à dans l'éditeur)</color>", LogCategory.MovementAndTurns, "[DIALOGUE]", Color.cyan);
                target.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("💬 …", Color.cyan);
                return true;
            }

            string targetId = (match.TargetId ?? "").Trim();

            // Origine de la conversation (avant tout saut) : sert à armer le
            // retour vers la conversation principale (ArmTalkReturn).
            string originNodeId = State._director != null && State._director.CurrentNode != null ? State._director.CurrentNode.Id : "";
            string originLineId = "";
            var originDataNode = !string.IsNullOrEmpty(originNodeId) && State._sceneData != null ? State._sceneData.FindNode(originNodeId) : null;
            if (originDataNode != null && originDataNode.Dialogues != null
                && State._currentDialogueIndex >= 0 && State._currentDialogueIndex < originDataNode.Dialogues.Count)
                originLineId = originDataNode.Dialogues[State._currentDialogueIndex]?.LineId ?? "";

            // 1. Cible = nœud : le nœud est enclenché (saut).
            if (State._sceneData != null && State._sceneData.FindNode(targetId) != null)
            {
                CombatHUD.Instance?.AddAdvancedLog($"💬 <b>{talkerName}</b> parle à <b>{target.Stats.Name}</b> ➔ nœud <b>{targetId}</b>", LogCategory.MovementAndTurns, "[DIALOGUE]", Color.cyan);
                State._pendingTalkLineId = "";
                if (string.Equals(originNodeId, targetId, StringComparison.OrdinalIgnoreCase))
                {
                    StageCurrentLine();
                    return true;
                }
                if (State._director != null && State._director.GoToNode(targetId))
                {
                    ArmTalkReturn(originNodeId, originLineId, targetId);
                    return true;
                }
                return false;
            }

            // 2. Cible = réplique : retrouver son nœud propriétaire.
            SceneDialogueLineData line = null;
            SceneNodeData ownerNode = null;
            if (State._sceneData != null && State._sceneData.Nodes != null)
            {
                for (int n = 0; n < State._sceneData.Nodes.Count && line == null; n++)
                {
                    var node = State._sceneData.Nodes[n];
                    if (node == null) continue;
                    var l = node.FindDialogue(targetId);
                    if (l != null) { line = l; ownerNode = node; }
                }
            }
            if (line == null)
            {
                Debug.LogWarning($"[JsonStorySceneController] Parler à : cible introuvable '{targetId}' (ni nœud, ni réplique).");
                CombatHUD.Instance?.AddAdvancedLog($"<color=grey>[PARLER] Cible introuvable : '{targetId}'.</color>", LogCategory.MovementAndTurns, "[DIALOGUE]", Color.gray);
                target.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("💬 …?", Color.gray);
                return false;
            }

            if (line.Prerequisite != null && line.Prerequisite.HasPrerequisite
                && !line.Prerequisite.IsMet(State._playerParty, talker, State._director))
            {
                CombatHUD.Instance?.AddAdvancedLog($"<color=grey>[PARLER] Prérequis non satisfait pour '{targetId}' ({line.Prerequisite.GetSummary()}).</color>", LogCategory.MovementAndTurns, "[DIALOGUE]", Color.gray);
                return false;
            }

            if (string.IsNullOrWhiteSpace(line.SpeakerId))
            {
                line.SpeakerId = ResolveTalkSpeakerDisplay("", actorData, target);
            }

            // Cinématiques / ambiance / tests auto : joués par StageCurrentLine
            // lors du staging (même nœud ou nœud enclenché) — pas ici (sinon
            // doublons). Seul le repli direct ci-dessous les joue lui-même.
            if (ownerNode != null && string.Equals(ownerNode.NodeId, originNodeId, StringComparison.OrdinalIgnoreCase))
            {
                CombatHUD.Instance?.AddAdvancedLog($"💬 <b>{talkerName}</b> parle à <b>{target.Stats.Name}</b> ➔ <b>{targetId}</b>", LogCategory.MovementAndTurns, "[DIALOGUE]", Color.cyan);
                State._pendingTalkLineId = "";
                ArmTalkReturn(originNodeId, originLineId, originNodeId);
                return TryAdvanceToId(targetId, null, false);
            }

            // Réplique d'un autre nœud : on enclenche le nœud puis la réplique
            // en attente (consommée par UpdateNodeState après l'entrée).
            if (ownerNode != null && State._director != null && State._director.GoToNode(ownerNode.NodeId))
            {
                CombatHUD.Instance?.AddAdvancedLog($"💬 <b>{talkerName}</b> parle à <b>{target.Stats.Name}</b> ➔ nœud <b>{ownerNode.NodeId}</b> ➔ <b>{targetId}</b>", LogCategory.MovementAndTurns, "[DIALOGUE]", Color.cyan);
                State._pendingTalkLineId = targetId;
                ArmTalkReturn(originNodeId, originLineId, ownerNode.NodeId);
                return true;
            }

            // Repli sans saut (saut refusé) : affichage direct, sans changer de nœud.
            State._pendingTalkLineId = "";
            FireLinkedCinematics(line.CinematicIds);
            ApplyAmbienceSettings(line.Ambience, line.SoundCueId);
            string speakerShow = string.IsNullOrWhiteSpace(line.SpeakerId)
                ? ResolveTalkSpeakerDisplay("", actorData, target) : line.SpeakerId;
            string stage = !string.IsNullOrWhiteSpace(line.StageDirection) ? $" <i>({line.StageDirection})</i>" : "";
            CombatHUD.Instance?.AddAdvancedLog($"💬 <b>{talkerName}</b> parle à <b>{target.Stats.Name}</b> — <b>[{speakerShow}]</b>{stage} « {line.Speech} »", LogCategory.MovementAndTurns, "[DIALOGUE]", Color.cyan);
            target.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText($"💬 {line.Speech}", Color.cyan);
            if (line.Choices != null && line.Choices.Count > 0)
                CombatHUD.Instance?.AddAdvancedLog($"<color=grey>[PARLER] {line.Choices.Count} choix disponibles dans le nœud '{ownerNode?.NodeId}'.</color>", LogCategory.MovementAndTurns, "[DIALOGUE]", Color.gray);
            else if (!string.IsNullOrWhiteSpace(line.NextLineId))
            {
                var next = ownerNode != null ? ownerNode.FindDialogue(line.NextLineId) : null;
                if (next != null && !string.IsNullOrWhiteSpace(next.Speech))
                    CombatHUD.Instance?.AddAdvancedLog($"💬 <b>[{next.SpeakerId}]</b> « {next.Speech} »", LogCategory.MovementAndTurns, "[DIALOGUE]", Color.cyan);
            }
            return true;
        }

        private bool HasActiveTalkReturn()
        {
            if (string.IsNullOrWhiteSpace(State._talkReturnNodeId)) return false;
            string currentId = State._director != null && State._director.CurrentNode != null ? State._director.CurrentNode.Id : "";
            return string.Equals(currentId, State._talkLandingNodeId, StringComparison.OrdinalIgnoreCase);
        }

        // Mémorise le retour vers la conversation principale après un saut
        // "Parler à" (origine = nœud + réplique courants AVANT le saut).
        private void ArmTalkReturn(string originNodeId, string originLineId, string landingNodeId)
        {
            State._talkReturnNodeId = "";
            State._talkReturnLineId = "";
            State._talkLandingNodeId = "";
            if (string.IsNullOrEmpty(originNodeId) || string.IsNullOrEmpty(landingNodeId)) return;
            State._talkReturnNodeId = originNodeId;
            State._talkReturnLineId = originLineId ?? "";
            State._talkLandingNodeId = landingNodeId;
        }

        // Fin naturelle du nœud d'atterrissage "Parler à" : revient au nœud
        // d'origine (et à sa réplique) sans déclencher les prédécesseurs ni réinitialiser le nœud.
        private bool TryReturnFromTalk()
        {
            if (string.IsNullOrWhiteSpace(State._talkReturnNodeId)) return false;
            string retNode = State._talkReturnNodeId;
            string retLine = State._talkReturnLineId;
            string landing = State._talkLandingNodeId;
            State._talkReturnNodeId = "";
            State._talkReturnLineId = "";
            State._talkLandingNodeId = "";
            if (State._director == null || State._sceneData == null) return false;
            string currentId = State._director.CurrentNode?.Id;
            if (!string.Equals(currentId, landing, StringComparison.OrdinalIgnoreCase)) return false;
            if (State._sceneData.FindNode(retNode) == null) return false;

            CombatHUD.Instance?.AddAdvancedLog($"💬 <b>Retour</b> vers la conversation (<b>{retNode}</b>).", LogCategory.MovementAndTurns, "[DIALOGUE]", Color.cyan);

            // Cas intra-nœud
            if (string.Equals(currentId, retNode, StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(retLine))
                {
                    TryAdvanceToId(retLine, null, false);
                }
                return true;
            }

            // Cas inter-nœuds : armement de l'immunité aux cinématiques d'intro
            State._isReturningFromTalk = true;
            if (!string.IsNullOrEmpty(retLine)) State._pendingTalkLineId = retLine;
            if (!State._director.GoToNode(retNode))
            {
                State._isReturningFromTalk = false;
                return false;
            }
            return true;
        }
    }
}

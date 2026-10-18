using System;
using System.Collections.Generic;
using Killtime.Core.Character;

namespace Killtime.Story.Data
{
    /// <summary>
    /// Schéma verrouillé RD-058 : valide un scénario (dialogue / choix / test
    /// opposé / combat / conséquence) avant production de masse. Retourne la
    /// liste des erreurs bloquantes (vide = valide). Pur, sans dépendance Unity.
    /// </summary>
    public static class StorySceneValidator
    {
        public const int MinChallengeDC = 2;
        public const int MaxChallengeDC = 30;

        public static bool IsValid(StorySceneData data) => Validate(data).Count == 0;

        public static List<string> Validate(StorySceneData data)
        {
            var errors = new List<string>();
            if (data == null) { errors.Add("scene: données nulles."); return errors; }

            if (data.Version < 1)
                errors.Add($"scene '{data.SceneId}': Version={data.Version} invalide (attendu >= 1).");
            if (string.IsNullOrWhiteSpace(data.SceneId))
                errors.Add("scene: SceneId vide.");
            if (string.IsNullOrWhiteSpace(data.Title))
                errors.Add($"scene '{data.SceneId}': Title vide.");
            if (data.Nodes == null || data.Nodes.Count == 0)
            {
                errors.Add($"scene '{data.SceneId}': aucun nœud.");
                return errors;
            }
            if (!string.IsNullOrWhiteSpace(data.NextSceneId)
                && string.Equals(data.NextSceneId.Trim(), data.SceneId.Trim(), StringComparison.OrdinalIgnoreCase))
                errors.Add($"scene '{data.SceneId}': NextSceneId pointe vers elle-même.");

            var nodeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in data.Nodes)
            {
                if (n == null) { errors.Add($"scene '{data.SceneId}': nœud nul."); continue; }
                if (string.IsNullOrWhiteSpace(n.NodeId))
                    errors.Add("scene: NodeId vide.");
                else if (!nodeIds.Add(n.NodeId.Trim()))
                    errors.Add($"scene '{data.SceneId}': NodeId dupliqué '{n.NodeId}'.");
                if (!string.IsNullOrWhiteSpace(n.NodeId) && HasBadIdChars(n.NodeId))
                    errors.Add($"nœud '{n.NodeId}': identifiant malformé (guillemets/espaces).");
            }

            if (string.IsNullOrWhiteSpace(data.FirstNodeId))
                errors.Add($"scene '{data.SceneId}': FirstNodeId vide.");
            else if (!nodeIds.Contains(data.FirstNodeId.Trim()))
                errors.Add($"scene '{data.SceneId}': FirstNodeId '{data.FirstNodeId}' introuvable.");

            var actorIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var speakerNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var a in data.Actors ?? new List<SceneActorSpawnData>())
            {
                if (a == null) continue;
                if (string.IsNullOrWhiteSpace(a.ActorId))
                    errors.Add($"scene '{data.SceneId}': ActorId vide.");
                else if (!actorIds.Add(a.ActorId.Trim()))
                    errors.Add($"scene '{data.SceneId}': ActorId dupliqué '{a.ActorId}'.");
                if (!string.IsNullOrWhiteSpace(a.ActorId)) speakerNames.Add(a.ActorId.Trim());
                if (!string.IsNullOrWhiteSpace(a.DisplayName)) speakerNames.Add(a.DisplayName.Trim());
            }

            var interactableIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var it in data.Interactables ?? new List<SceneInteractableSpawnData>())
            {
                if (it == null) continue;
                if (string.IsNullOrWhiteSpace(it.InteractableId))
                    errors.Add($"scene '{data.SceneId}': InteractableId vide.");
                else if (!interactableIds.Add(it.InteractableId.Trim()))
                    errors.Add($"scene '{data.SceneId}': InteractableId dupliqué '{it.InteractableId}'.");
            }

            var cineIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in data.Cinematics ?? new List<SceneCinematicData>())
            {
                if (c == null) continue;
                if (string.IsNullOrWhiteSpace(c.CinematicId))
                    errors.Add($"scene '{data.SceneId}': CinematicId vide.");
                else if (!cineIds.Add(c.CinematicId.Trim()))
                    errors.Add($"scene '{data.SceneId}': CinematicId dupliqué '{c.CinematicId}'.");
            }

            var triggerIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in data.Triggers ?? new List<SceneTriggerData>())
            {
                if (t == null) continue;
                if (string.IsNullOrWhiteSpace(t.TriggerId))
                    errors.Add($"scene '{data.SceneId}': TriggerId vide.");
                else if (!triggerIds.Add(t.TriggerId.Trim()))
                    errors.Add($"scene '{data.SceneId}': TriggerId dupliqué '{t.TriggerId}'.");
            }

            // Référentiel global des répliques (seul « Parler à » résout global,
            // voir JsonStorySceneController.Talk.cs) + objectifs scène-globale.
            var lineIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var objectiveIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var n in data.Nodes)
            {
                if (n == null) continue;
                foreach (var dl in n.Dialogues ?? new List<SceneDialogueLineData>())
                {
                    if (dl == null || string.IsNullOrWhiteSpace(dl.LineId)) continue;
                    lineIds.Add(dl.LineId.Trim());
                }
                foreach (var o in n.Objectives ?? new List<ScenarioObjective>())
                {
                    if (o == null || string.IsNullOrWhiteSpace(o.Id)) continue;
                    objectiveIds.Add(o.Id.Trim());
                }
            }

            foreach (var n in data.Nodes)
            {
                if (n == null) continue;
                ValidateNode(data.SceneId, n, nodeIds,
                    objectiveIds, actorIds, speakerNames, cineIds, errors);
            }

            foreach (var t in data.Triggers ?? new List<SceneTriggerData>())
            {
                if (t == null) continue;
                CheckNodeRef(errors, t.TriggerId, "trigger", t.SourceNodeId, "SourceNodeId", nodeIds);
                CheckNodeRef(errors, t.TriggerId, "trigger", t.TargetNodeId, "TargetNodeId", nodeIds);
                if (t.Kind == SceneTriggerKind.InteractableActivated && !string.IsNullOrWhiteSpace(t.InteractableId)
                    && !interactableIds.Contains(t.InteractableId.Trim()))
                    errors.Add($"trigger '{t.TriggerId}': InteractableId '{t.InteractableId}' introuvable.");
                if (t.Kind == SceneTriggerKind.ObjectiveCompleted && !string.IsNullOrWhiteSpace(t.ObjectiveId)
                    && !objectiveIds.Contains(t.ObjectiveId.Trim()))
                    errors.Add($"trigger '{t.TriggerId}': ObjectiveId '{t.ObjectiveId}' introuvable.");
                CheckCineLinks(errors, $"trigger '{t.TriggerId}'", t.CinematicIds, cineIds);
            }

            foreach (var a in data.Actors ?? new List<SceneActorSpawnData>())
            {
                if (a == null) continue;
                CheckNodeRef(errors, a.ActorId, "acteur", a.SpawnOnNodeId, "SpawnOnNodeId", nodeIds);
                foreach (var talk in a.TalkEntries ?? new List<SceneActorTalkEntry>())
                {
                    if (talk == null || string.IsNullOrWhiteSpace(talk.TargetId)) continue;
                    string target = talk.TargetId.Trim();
                    if (!nodeIds.Contains(target) && !lineIds.Contains(target))
                        errors.Add($"acteur '{a.ActorId}': TalkEntry cible '{talk.TargetId}' (ni nœud ni réplique).");
                }
                CheckCineLinks(errors, $"acteur '{a.ActorId}'", a.CinematicIds, cineIds);
            }

            foreach (var it in data.Interactables ?? new List<SceneInteractableSpawnData>())
            {
                if (it == null) continue;
                CheckNodeRef(errors, it.InteractableId, "interactable", it.TriggerNodeId, "TriggerNodeId", nodeIds);
                if (!string.IsNullOrWhiteSpace(it.CompletionObjectiveId)
                    && !objectiveIds.Contains(it.CompletionObjectiveId.Trim()))
                    errors.Add($"interactable '{it.InteractableId}': CompletionObjectiveId '{it.CompletionObjectiveId}' introuvable.");
                var actionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var act in it.Actions ?? new List<SceneInteractableAction>())
                {
                    if (act == null) continue;
                    if (!string.IsNullOrWhiteSpace(act.ActionId) && !actionIds.Add(act.ActionId.Trim()))
                        errors.Add($"interactable '{it.InteractableId}': ActionId dupliqué '{act.ActionId}'.");
                    if (!string.IsNullOrWhiteSpace(act.CompletionObjectiveId)
                        && !objectiveIds.Contains(act.CompletionObjectiveId.Trim()))
                        errors.Add($"interactable '{it.InteractableId}' action '{act.ActionId}': CompletionObjectiveId '{act.CompletionObjectiveId}' introuvable.");
                }
                CheckCineLinks(errors, $"interactable '{it.InteractableId}'", it.CinematicIds, cineIds);
            }

            return errors;
        }

        static void ValidateNode(string sceneId, SceneNodeData n,
            HashSet<string> nodeIds, HashSet<string> objectiveIds, HashSet<string> actorIds,
            HashSet<string> speakerNames, HashSet<string> cineIds,
            List<string> errors)
        {
            CheckNodeRef(errors, n.NodeId, "nœud", n.NextNodeId, "NextNodeId", nodeIds);

            // Portée nœud (comme le runtime TryAdvanceToId : FindDialogueIndex /
            // FindConsequence sur le nœud courant). Seul « Parler à » résout global.
            var nodeLineIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dl in n.Dialogues ?? new List<SceneDialogueLineData>())
            {
                if (dl == null || string.IsNullOrWhiteSpace(dl.LineId)) continue;
                nodeLineIds.Add(dl.LineId.Trim());
            }
            var nodeConsIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var cs in n.Consequences ?? new List<SceneConsequenceData>())
            {
                if (cs == null || string.IsNullOrWhiteSpace(cs.ConsequenceId)) continue;
                nodeConsIds.Add(cs.ConsequenceId.Trim());
            }
            var nodeEventIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in n.Events ?? new List<SceneEventData>())
            {
                if (e == null || string.IsNullOrWhiteSpace(e.EventId)) continue;
                nodeEventIds.Add(e.EventId.Trim());
            }

            var localLines = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dl in n.Dialogues ?? new List<SceneDialogueLineData>())
            {
                if (dl == null) { errors.Add($"nœud '{n.NodeId}': réplique nulle."); continue; }
                if (string.IsNullOrWhiteSpace(dl.LineId))
                    errors.Add($"nœud '{n.NodeId}': LineId vide.");
                else if (!localLines.Add(dl.LineId.Trim()))
                    errors.Add($"nœud '{n.NodeId}': LineId dupliqué '{dl.LineId}'.");
                if (!string.IsNullOrWhiteSpace(dl.SpeakerId) && !speakerNames.Contains(dl.SpeakerId.Trim()))
                    errors.Add($"réplique '{dl.LineId}': SpeakerId '{dl.SpeakerId}' (ni acteur ni nom connu).");
                CheckLineTarget(errors, dl.LineId, "réplique", dl.NextLineId, nodeLineIds, nodeConsIds);
                ValidateAutoCheck(errors, dl.LineId, "réplique", dl.AutoSkillCheck,
                    nodeLineIds, nodeConsIds, actorIds);
                var choiceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var c in dl.Choices ?? new List<SceneDialogueChoiceData>())
                {
                    if (c == null) continue;
                    if (!string.IsNullOrWhiteSpace(c.ChoiceId) && !choiceIds.Add(c.ChoiceId.Trim()))
                        errors.Add($"réplique '{dl.LineId}': ChoiceId dupliqué '{c.ChoiceId}'.");
                    CheckLineTarget(errors, c.ChoiceId ?? dl.LineId, "choix", c.NextLineId, nodeLineIds, nodeConsIds);
                    ValidateChallenge(errors, c.ChoiceId ?? dl.LineId, "choix", c.Challenge,
                        nodeLineIds, nodeConsIds, actorIds);
                }
                CheckCineLinks(errors, $"réplique '{dl.LineId}'", dl.CinematicIds, cineIds);
            }

            var localEvents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in n.Events ?? new List<SceneEventData>())
            {
                if (e == null) { errors.Add($"nœud '{n.NodeId}': événement nul."); continue; }
                if (string.IsNullOrWhiteSpace(e.EventId))
                    errors.Add($"nœud '{n.NodeId}': EventId vide.");
                else if (!localEvents.Add(e.EventId.Trim()))
                    errors.Add($"nœud '{n.NodeId}': EventId dupliqué '{e.EventId}'.");
                if (!string.IsNullOrWhiteSpace(e.NextEventId) && !nodeEventIds.Contains(e.NextEventId.Trim()))
                    errors.Add($"événement '{e.EventId}': NextEventId '{e.NextEventId}' introuvable dans le nœud.");
                ValidateChallenge(errors, e.EventId, "événement", e.Challenge, nodeLineIds, nodeConsIds, actorIds);
                ValidateAutoCheck(errors, e.EventId, "événement", e.AutoSkillCheck, nodeLineIds, nodeConsIds, actorIds);
                CheckObjective(errors, $"événement '{e.EventId}'", e.CompletionObjectiveId, objectiveIds);
                if (e.Rewards != null)
                    CheckObjective(errors, $"événement '{e.EventId}' (récompense)", e.Rewards.CompletionObjectiveId, objectiveIds);
                foreach (var c in e.Choices ?? new List<SceneDialogueChoiceData>())
                {
                    if (c == null) continue;
                    CheckLineTarget(errors, c.ChoiceId ?? e.EventId, "choix d'événement", c.NextLineId, nodeLineIds, nodeConsIds);
                    ValidateChallenge(errors, c.ChoiceId ?? e.EventId, "choix d'événement", c.Challenge,
                        nodeLineIds, nodeConsIds, actorIds);
                }
                CheckCineLinks(errors, $"événement '{e.EventId}'", e.CinematicIds, cineIds);
            }

            var localCons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var cs in n.Consequences ?? new List<SceneConsequenceData>())
            {
                if (cs == null) { errors.Add($"nœud '{n.NodeId}': conséquence nulle."); continue; }
                if (string.IsNullOrWhiteSpace(cs.ConsequenceId))
                    errors.Add($"nœud '{n.NodeId}': ConsequenceId vide.");
                else if (!localCons.Add(cs.ConsequenceId.Trim()))
                    errors.Add($"nœud '{n.NodeId}': ConsequenceId dupliqué '{cs.ConsequenceId}'.");
                CheckLineTarget(errors, cs.ConsequenceId, "conséquence", cs.NextLineId, nodeLineIds, nodeConsIds);
                if (!string.IsNullOrWhiteSpace(cs.NextConsequenceId) && !nodeConsIds.Contains(cs.NextConsequenceId.Trim()))
                    errors.Add($"conséquence '{cs.ConsequenceId}': NextConsequenceId '{cs.NextConsequenceId}' introuvable dans le nœud.");
                if (cs.Rewards != null)
                    CheckObjective(errors, $"conséquence '{cs.ConsequenceId}'", cs.Rewards.CompletionObjectiveId, objectiveIds);
                CheckCineLinks(errors, $"conséquence '{cs.ConsequenceId}'", cs.CinematicIds, cineIds);
            }

            var localObj = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var o in n.Objectives ?? new List<ScenarioObjective>())
            {
                if (o == null || string.IsNullOrWhiteSpace(o.Id)) continue;
                if (!localObj.Add(o.Id.Trim()))
                    errors.Add($"nœud '{n.NodeId}': Objective Id dupliqué '{o.Id}'.");
            }
            CheckCineLinks(errors, $"nœud '{n.NodeId}'", n.CinematicIds, cineIds);
        }

        static void ValidateChallenge(List<string> errors, string ownerId, string ownerKind,
            SceneDialogueChallengeData ch, HashSet<string> lineIds, HashSet<string> consIds,
            HashSet<string> actorIds)
        {
            if (ch == null || !ch.HasChallenge) return;
            if (!Enum.IsDefined(typeof(SkillType), ch.RequiredSkill))
                errors.Add($"{ownerKind} '{ownerId}': RequiredSkill invalide.");
            if (ch.TargetDC < MinChallengeDC || ch.TargetDC > MaxChallengeDC)
                errors.Add($"{ownerKind} '{ownerId}': TargetDC={ch.TargetDC} hors borne [{MinChallengeDC}-{MaxChallengeDC}].");
            if (ch.IsOpposed && !Enum.IsDefined(typeof(SkillType), ch.OpposedSkill))
                errors.Add($"{ownerKind} '{ownerId}': OpposedSkill invalide.");
            if (!string.IsNullOrWhiteSpace(ch.SpecificActorId) && !actorIds.Contains(ch.SpecificActorId.Trim()))
                errors.Add($"{ownerKind} '{ownerId}': SpecificActorId '{ch.SpecificActorId}' introuvable.");
            if (ch.IsOpposed && !string.IsNullOrWhiteSpace(ch.OpposedActorId) && !actorIds.Contains(ch.OpposedActorId.Trim()))
                errors.Add($"{ownerKind} '{ownerId}': OpposedActorId '{ch.OpposedActorId}' introuvable.");
            CheckConsTarget(errors, ownerId, ownerKind, "SuccessConsequenceId", ch.SuccessConsequenceId, consIds);
            CheckConsTarget(errors, ownerId, ownerKind, "FailureConsequenceId", ch.FailureConsequenceId, consIds);
            CheckLineTarget(errors, ownerId, ownerKind, ch.SuccessNextLineId, lineIds, consIds, "SuccessNextLineId");
            CheckLineTarget(errors, ownerId, ownerKind, ch.FailureNextLineId, lineIds, consIds, "FailureNextLineId");
        }

        static void ValidateAutoCheck(List<string> errors, string ownerId, string ownerKind,
            SceneAutoSkillCheckData ac, HashSet<string> lineIds, HashSet<string> consIds,
            HashSet<string> actorIds)
        {
            if (ac == null || !ac.HasAutoCheck) return;
            if (!Enum.IsDefined(typeof(SkillType), ac.RequiredSkill))
                errors.Add($"{ownerKind} '{ownerId}': AutoCheck RequiredSkill invalide.");
            if (ac.TargetDC < MinChallengeDC || ac.TargetDC > MaxChallengeDC)
                errors.Add($"{ownerKind} '{ownerId}': AutoCheck TargetDC={ac.TargetDC} hors borne [{MinChallengeDC}-{MaxChallengeDC}].");
            if (!string.IsNullOrWhiteSpace(ac.SpecificActorId) && !actorIds.Contains(ac.SpecificActorId.Trim()))
                errors.Add($"{ownerKind} '{ownerId}': AutoCheck SpecificActorId '{ac.SpecificActorId}' introuvable.");
            CheckLineTarget(errors, ownerId, ownerKind, ac.SuccessNextLineId, lineIds, consIds, "AutoSuccessNextLineId");
            CheckLineTarget(errors, ownerId, ownerKind, ac.FailureNextLineId, lineIds, consIds, "AutoFailureNextLineId");
        }

        static void CheckNodeRef(List<string> errors, string owner, string ownerKind,
            string refId, string field, HashSet<string> nodeIds)
        {
            if (string.IsNullOrWhiteSpace(refId)) return;
            if (!nodeIds.Contains(refId.Trim()))
                errors.Add($"{ownerKind} '{owner}': {field} '{refId}' introuvable.");
        }

        static void CheckLineTarget(List<string> errors, string owner, string ownerKind,
            string refId, HashSet<string> lineIds, HashSet<string> consIds, string field = "NextLineId")
        {
            if (string.IsNullOrWhiteSpace(refId)) return;
            string t = refId.Trim();
            if (!lineIds.Contains(t) && !consIds.Contains(t))
                errors.Add($"{ownerKind} '{owner}': {field} '{refId}' (ni réplique ni conséquence).");
        }

        static void CheckConsTarget(List<string> errors, string owner, string ownerKind,
            string field, string refId, HashSet<string> consIds)
        {
            if (string.IsNullOrWhiteSpace(refId)) return;
            if (!consIds.Contains(refId.Trim()))
                errors.Add($"{ownerKind} '{owner}': {field} '{refId}' introuvable.");
        }

        static void CheckObjective(List<string> errors, string owner, string refId, HashSet<string> objectiveIds)
        {
            if (string.IsNullOrWhiteSpace(refId)) return;
            if (!objectiveIds.Contains(refId.Trim()))
                errors.Add($"{owner}: CompletionObjectiveId '{refId}' introuvable.");
        }

        static void CheckCineLinks(List<string> errors, string owner, List<string> links, HashSet<string> cineIds)
        {
            foreach (var id in links ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(id)) continue;
                if (!cineIds.Contains(id.Trim()))
                    errors.Add($"{owner}: CinematicId '{id}' introuvable.");
            }
        }

        static bool HasBadIdChars(string id)
        {
            for (int i = 0; i < id.Length; i++)
            {
                char c = id[i];
                if (c == '\'' || c == '"' || char.IsControl(c)) return true;
            }
            return false;
        }
    }
}

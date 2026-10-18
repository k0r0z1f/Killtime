using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Core.Character;
using Killtime.Tactics.Grid;

namespace Killtime.Story.Data
{
    [Serializable]
    public class SceneInteractableAction
    {
        public string ActionId = "action_1";
        public string Label = "Interagir";
        public int RequiredSkill = -1;      // -1 = action libre, sans jet
        public int SkillThreshold = 0;
        public string SuccessLog = "";
        public string FailureLog = "";
        public string CompletionObjectiveId = "";
        public string TriggerNodeId = "";
        public bool IsOneShot = false;
    }

    [Serializable]
    public class SceneInteractableSpawnData
    {
        public string InteractableId = "nouvel_interactable";
        public string DisplayName = "Nouvel interactable";
        public string ActionLabel = "Interagir";
        public int Q = 4;
        public int R = 0;
        public int Radius = 1;
        public SkillType RequiredSkill = SkillType.IngenierieArcanotech;
        public int SkillThreshold = 10;
        public string CompletionObjectiveId = "";
        public string SuccessLog = "";
        public string FailureLog = "";
        public bool IsOneShot = true;
        public string TriggerNodeId = "";
        // Corps de la fenêtre flottante affiché avant toute action.
        public string Description = "";
        // Variantes optionnelles tirées à l'ouverture (ex : brouilleur instable).
        public List<string> DescriptionVariants = new();
        // Actions disponibles dans la fenêtre flottante. Si vide, les champs
        // legacy ActionLabel/RequiredSkill/SkillThreshold/SuccessLog/...
        // construisent une action unique pour la rétro-compatibilité.
        public List<SceneInteractableAction> Actions = new();
        // Sortie cinématique non-bloquante : jouée (fire-and-forget) à l'activation.
        public List<string> CinematicIds = new();
        // Position/taille dans le graphe nodal (éditeur uniquement, comme les Triggers).
        // 0/0 = jamais placé → l'éditeur auto-dispose sous les déclencheurs au chargement.
        public float GraphPosX = 0f;
        public float GraphPosY = 0f;
        public float CardWidth = 340f;
        public float CardHeight = 252f;

        public string GetSummary()
        {
            string where = $"hex ({Q}, {R}) r{Radius}";
            int actionCount = Actions != null ? Actions.Count : 0;
            string test = actionCount > 0
                ? $"{actionCount} action(s)"
                : $"SD {SkillThreshold} · {SkillDefinitions.GetDisplayName(RequiredSkill)}";
            string target = string.IsNullOrEmpty(TriggerNodeId) ? "(aucun saut)" : $"➔ {TriggerNodeId}";
            return $"{where} · {test} {target}";
        }

        /// <summary>
        /// Retourne les actions explicites, ou construit une action unique depuis
        /// les champs legacy (compatibilité JSON existant).
        /// </summary>
        public List<SceneInteractableAction> GetEffectiveActions()
        {
            if (Actions != null && Actions.Count > 0) return Actions;
            return new List<SceneInteractableAction>
            {
                new SceneInteractableAction
                {
                    ActionId = "legacy",
                    Label = ActionLabel,
                    RequiredSkill = (int)RequiredSkill,
                    SkillThreshold = SkillThreshold,
                    SuccessLog = SuccessLog,
                    FailureLog = FailureLog,
                    CompletionObjectiveId = CompletionObjectiveId,
                    TriggerNodeId = TriggerNodeId,
                    IsOneShot = IsOneShot
                }
            };
        }
    }

}
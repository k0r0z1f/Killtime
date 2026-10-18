using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Core.Character;
using Killtime.Tactics.Grid;

namespace Killtime.Story.Data
{
    [Serializable]
    public class SceneItemRewardEntry
    {
        public string ItemName = "";
        public Killtime.Core.Inventory.ItemType ItemRewardType = Killtime.Core.Inventory.ItemType.Misc;
        public int Quantity = 1;
    }

    [Serializable]
    public class SceneDialogueRewardData
    {
        public int EarnCredits = 0;
        public int EarnXP = 0;
        public string ItemRewardName = "";
        public Killtime.Core.Inventory.ItemType ItemRewardType = Killtime.Core.Inventory.ItemType.Misc;
        public int ItemRewardQuantity = 1;
        public List<SceneItemRewardEntry> AdditionalItems = new();
        public string CompletionObjectiveId = "";
    }

    [Serializable]
    public class SceneDialogueChallengeData
    {
        public bool HasChallenge = false;
        public SkillType RequiredSkill = SkillType.Communication;
        public int TargetDC = 10;
        public string SpecificActorId = "";

        // Défi opposé : jet joueur vs jet opposant (au lieu de SD fixe).
        // Si OpposedActorId est vide ou introuvable au runtime, TargetDC + OpposedBonus sert de total adverse fixe.
        public bool IsOpposed = false;
        public SkillType OpposedSkill = SkillType.Communication;
        public string OpposedActorId = "";
        public int OpposedBonus = 0;

        // Sorties vers des cartes Conséquence (prioritaires sur les liens directs ci-dessous).
        public string SuccessConsequenceId = "";
        public string FailureConsequenceId = "";

        // OBSOLÈTE (routage direct vers fenêtres normales) : conservés pour compat JSON,
        // non édités, non lus par le runtime des choix.
        public string SuccessSpeech = "";
        public string SuccessStageDirection = "";
        public string SuccessNextLineId = "";
        public SceneDialogueRewardData SuccessRewards = new();
        public List<ScenarioEffect> SuccessEffects = new();

        public string FailureSpeech = "";
        public string FailureStageDirection = "";
        public string FailureNextLineId = "";
        public bool FailureTriggersCombat = false;
        public List<ScenarioEffect> FailureEffects = new();

        public string GetShortLabel()
        {
            if (!HasChallenge) return "";
            if (IsOpposed)
                return $"VS {SkillDefinitions.GetDisplayName(RequiredSkill)} vs {SkillDefinitions.GetDisplayName(OpposedSkill)}";
            return $"SD {TargetDC} · {SkillDefinitions.GetDisplayName(RequiredSkill)}";
        }
    }

    [Serializable]
    public class SceneDialogueChoiceData
    {
        public string ChoiceId = "choice_1";
        public string Label = "Option de dialogue...";
        // OBSOLÈTES (routage direct vers fenêtres normales) : conservés pour compat JSON.
        public string ReactionSpeech = "";
        public string ReactionStageDirection = "";
        public string NextLineId = "";
        public List<ScenarioEffect> Effects = new();
        public SceneDialogueChallengeData Challenge = new();
        public ScenePrerequisiteData Prerequisite = new();
    }

    [Serializable]
    public class SceneDialogueLineData
    {
        public string LineId = "";
        public string SpeakerId = "";
        public bool IsNarration = false;
        public string StageDirection = "sourire en coin";
        public string Speech = "Nous partons dès que le signal est validé.";
        // HÉRITÉ (dé-hardcodé) : les focus caméra des répliques passent par des cartes 🎬
        // (plan avec TrackFocusActor). Conservés pour compat JSON, ignorés au runtime.
        public string CameraFocusActorId = "";
        public float CameraPitch = 42f;
        public float CameraDistance = 9.5f;
        public string SoundCueId = "UI_Filter";
        public string NextLineId = "";
        // Sortie cinématique non-bloquante : jouée (fire-and-forget) à l'affichage de la réplique,
        // sans attendre la fin pour enclencher la carte suivante.
        public List<string> CinematicIds = new();
        public float GraphPosX = 0f;
        public float GraphPosY = 0f;
        public float CardWidth = 340f;
        public float CardHeight = 185f;
        public List<SceneDialogueChoiceData> Choices = new();
        public ScenePrerequisiteData Prerequisite = new();
        public SceneAmbienceData Ambience = new();
        public SceneAutoSkillCheckData AutoSkillCheck = new();
    }

    public enum SceneEventKind
    {
        Normal,
        TriggerCombat,
        SpawnEnemies,
        // HÉRITÉS (dé-hardcodés) : remplacés par des cartes 🎬 (tracking / effet relatif).
        // Conservés pour compat JSON, ignorés au runtime, masqués du cycleur éditeur.
        CameraFocus,
        CameraShake,
        ObjectiveUpdate,
        GrantRewards,
        EnvironmentAction
    }

    [Serializable]
    public class SceneEventData
    {
        public string EventId = "event_1";
        public string Title = "Nouvel Événement";
        public string Description = "";
        public SceneEventKind Kind = SceneEventKind.Normal;
        // HÉRITÉS (dé-hardcodés, ne servaient qu'au kind CameraFocus) : compat JSON uniquement.
        public string TargetActorId = "";
        public float CameraPitch = 42f;
        public float CameraDistance = 9.5f;
        public float GraphPosX = 0f;
        public float GraphPosY = 0f;
        public float CardWidth = 340f;
        public float CardHeight = 145f;

        public ScenePrerequisiteData Prerequisite = new();
        public SceneAmbienceData Ambience = new();
        public SceneAutoSkillCheckData AutoSkillCheck = new();
        public SceneDialogueChallengeData Challenge = new();
        public List<SceneDialogueChoiceData> Choices = new();
        public List<ScenarioEffect> Effects = new();
        public SceneDialogueRewardData Rewards = new();
        public string NextEventId = "";
        public string CompletionObjectiveId = "";
        // Sortie cinématique non-bloquante : jouée (fire-and-forget) à l'exécution de l'événement.
        public List<string> CinematicIds = new();
    }

    [Serializable]
    public class SceneConsequenceData
    {
        public string ConsequenceId = "cons_1";
        public string Title = "Conséquence";

        // Dialogue optionnel affiché à l'exécution (comme une réplique normale).
        public bool HasDialogue = false;
        public string SpeakerId = "";
        public string StageDirection = "";
        public string Speech = "";

        // Charge utile : récompense et/ou punition.
        public SceneDialogueRewardData Rewards = new();
        public List<ScenarioEffect> Effects = new();
        public bool TriggersCombat = false;

        // Chaînage : vers une réplique et/ou vers une autre conséquence.
        public string NextLineId = "";
        public string NextConsequenceId = "";
        // Sortie cinématique non-bloquante : jouée (fire-and-forget) à l'exécution de la conséquence.
        public List<string> CinematicIds = new();

        public float GraphPosX = 0f;
        public float GraphPosY = 0f;
        public float CardWidth = 360f;
        public float CardHeight = 0f;

        public string GetSummary()
        {
            var bits = new List<string>();
            if (Rewards != null)
            {
                if (Rewards.EarnXP > 0) bits.Add($"+{Rewards.EarnXP}XP");
                if (Rewards.EarnCredits > 0) bits.Add($"+{Rewards.EarnCredits}CE");
                if (!string.IsNullOrEmpty(Rewards.ItemRewardName))
                    bits.Add(Rewards.ItemRewardQuantity > 1 ? $"{Rewards.ItemRewardName} x{Rewards.ItemRewardQuantity}" : Rewards.ItemRewardName);
                if (Rewards.AdditionalItems != null)
                {
                    for (int i = 0; i < Rewards.AdditionalItems.Count; i++)
                    {
                        var extra = Rewards.AdditionalItems[i];
                        if (extra == null || string.IsNullOrEmpty(extra.ItemName)) continue;
                        bits.Add(extra.Quantity > 1 ? $"{extra.ItemName} x{extra.Quantity}" : extra.ItemName);
                    }
                }
            }
            if (Effects != null && Effects.Count > 0) bits.Add($"fx:{Effects.Count}");
            if (TriggersCombat) bits.Add("⚔");
            string payload = bits.Count > 0 ? string.Join(", ", bits) : "(vide)";
            string target = !string.IsNullOrEmpty(NextLineId) ? $"➔ {NextLineId}"
                : (!string.IsNullOrEmpty(NextConsequenceId) ? $"➔ {NextConsequenceId}" : "(fin)");
            return $"{payload} {target}";
        }
    }

}
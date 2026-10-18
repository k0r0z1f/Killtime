using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Core.Character;
using Killtime.Tactics.Grid;

namespace Killtime.Story.Data
{
    public enum PrerequisiteKind
    {
        None,
        SkillTraining,
        Specialization,
        AttributeThreshold,
        CampaignFlag,
        ItemInInventory,
        StoryRouteRequired
    }

    public enum SceneTriggerKind
    {
        InteractableActivated,
        ObjectiveCompleted,
        CampaignFlagSet,
        ActorCondition
    }

    public enum SceneActorConditionKind
    {
        HasStatus,
        HPBelowPercent
    }

    [Serializable]
    public class SceneTriggerData
    {
        public string TriggerId = "trigger_1";
        public string Label = "Nouveau déclencheur";
        public SceneTriggerKind Kind = SceneTriggerKind.CampaignFlagSet;
        public string SourceNodeId = "";
        public string TargetNodeId = "";
        public string InteractableId = "";
        public string ObjectiveId = "";
        public string FlagKey = "";
        public bool FlagMustBeSet = true;
        public string ActorId = "";
        public SceneActorConditionKind ActorCondition = SceneActorConditionKind.HasStatus;
        public string StatusName = "Inconscient";
        public int HPPercentThreshold = 30;
        public bool OneShot = true;
        // Sortie cinématique non-bloquante : jouée (fire-and-forget) quand le trigger tire.
        public List<string> CinematicIds = new();
        public float GraphPosX = 0f;
        public float GraphPosY = 0f;
        public float CardWidth = 340f;
        public float CardHeight = 215f;

        public string GetSummary()
        {
            string when = string.IsNullOrEmpty(SourceNodeId) ? "toujours actif" : $"si nœud={SourceNodeId}";
            string what = Kind switch
            {
                SceneTriggerKind.InteractableActivated => $"action carte [{InteractableId}]",
                SceneTriggerKind.ObjectiveCompleted => $"objectif [{ObjectiveId}] accompli",
                SceneTriggerKind.CampaignFlagSet => $"{(FlagMustBeSet ? "flag" : "non-flag")} [{FlagKey}]",
                SceneTriggerKind.ActorCondition => ActorCondition == SceneActorConditionKind.HasStatus
                    ? $"{ActorId} subit [{StatusName}]"
                    : $"{ActorId} PV < {HPPercentThreshold}%",
                _ => "condition"
            };
            return $"{what} ({when})";
        }
    }

    [Serializable]
    public class ScenePrerequisiteData
    {
        public bool HasPrerequisite = false;
        public PrerequisiteKind Kind = PrerequisiteKind.None;
        public SkillType RequiredSkill = SkillType.Observation;
        public int MinTrainingLevel = 1;
        public string SpecializationName = "";
        public string AttributeName = "Intelligence";
        public int MinAttributeValue = 4;
        public string FlagKey = "";
        public bool FlagMustBeSet = true;
        public string ItemName = "";
        public StoryRoute RequiredRoute = StoryRoute.None;
        public string SpecificActorId = "";

        public bool IsMet(List<Killtime.Tactics.Units.TacticalUnit> party, Killtime.Tactics.Units.TacticalUnit activeUnit, ScenarioDirector director)
        {
            if (!HasPrerequisite || Kind == PrerequisiteKind.None) return true;

            Killtime.Tactics.Units.TacticalUnit targetUnit = activeUnit;
            if (!string.IsNullOrEmpty(SpecificActorId) && party != null)
            {
                targetUnit = party.Find(u => u != null && string.Equals(u.Stats.Name, SpecificActorId, StringComparison.OrdinalIgnoreCase));
            }

            switch (Kind)
            {
                case PrerequisiteKind.SkillTraining:
                    if (party != null && string.IsNullOrEmpty(SpecificActorId))
                    {
                        foreach (var u in party)
                            if (u != null && u.Sheet != null && u.Sheet.GetSkill(RequiredSkill).TrainingLevel >= MinTrainingLevel)
                                return true;
                        return false;
                    }
                    return targetUnit != null && targetUnit.Sheet != null && targetUnit.Sheet.GetSkill(RequiredSkill).TrainingLevel >= MinTrainingLevel;

                case PrerequisiteKind.Specialization:
                    if (party != null && string.IsNullOrEmpty(SpecificActorId))
                    {
                        foreach (var u in party)
                            if (u != null && u.Sheet != null && u.Sheet.UnlockedSpecializations.Contains(SpecializationName))
                                return true;
                        return false;
                    }
                    return targetUnit != null && targetUnit.Sheet != null && targetUnit.Sheet.UnlockedSpecializations.Contains(SpecializationName);

                case PrerequisiteKind.AttributeThreshold:
                    if (targetUnit == null || targetUnit.Sheet == null) return false;
                    var attr = targetUnit.Sheet.GetEffectiveAttributes();
                    int val = AttributeName.ToUpperInvariant() switch
                    {
                        "FORCE" or "FOR" => attr.Force,
                        "AGILITE" or "AGI" => attr.Agilite,
                        "CONSTITUTION" or "CON" => attr.Constitution,
                        "RAPIDITE" or "RAP" => attr.Rapidite,
                        "INTELLIGENCE" or "INT" => attr.Intelligence,
                        "ERUDITION" or "ERU" => attr.Erudition,
                        "CHARISME" or "CHA" => attr.Charisme,
                        "INSTINCT" or "INS" => attr.Instinct,
                        "MAGIE" or "MAG" => attr.Magie,
                        _ => 0
                    };
                    return val >= MinAttributeValue;

                case PrerequisiteKind.CampaignFlag:
                    if (director == null || director.State == null) return false;
                    bool has = director.State.HasFlag(FlagKey);
                    return FlagMustBeSet ? has : !has;

                case PrerequisiteKind.ItemInInventory:
                    if (party != null && string.IsNullOrEmpty(SpecificActorId))
                    {
                        foreach (var u in party)
                            if (u != null && u.Sheet != null && u.Sheet.Inventory != null && u.Sheet.Inventory.Exists(i => i != null && string.Equals(i.Name, ItemName, StringComparison.OrdinalIgnoreCase)))
                                return true;
                        return false;
                    }
                    return targetUnit != null && targetUnit.Sheet != null && targetUnit.Sheet.Inventory != null && targetUnit.Sheet.Inventory.Exists(i => i != null && string.Equals(i.Name, ItemName, StringComparison.OrdinalIgnoreCase));

                case PrerequisiteKind.StoryRouteRequired:
                    return director != null && director.State != null && director.State.Route == RequiredRoute;

                default:
                    return true;
            }
        }

        public string GetSummary()
        {
            if (!HasPrerequisite || Kind == PrerequisiteKind.None) return "Aucun";
            return Kind switch
            {
                PrerequisiteKind.SkillTraining => $"{SkillDefinitions.GetDisplayName(RequiredSkill)} ≥ Nv.{MinTrainingLevel}",
                PrerequisiteKind.Specialization => $"Spécialisation '{SpecializationName}'",
                PrerequisiteKind.AttributeThreshold => $"{AttributeName} ≥ {MinAttributeValue}",
                PrerequisiteKind.CampaignFlag => FlagMustBeSet ? $"Flag [{FlagKey}]" : $"Non-Flag [{FlagKey}]",
                PrerequisiteKind.ItemInInventory => $"Item '{ItemName}'",
                PrerequisiteKind.StoryRouteRequired => $"Route {RequiredRoute}",
                _ => "Condition"
            };
        }
    }

    [Serializable]
    public class SceneAmbienceData
    {
        public bool HasAmbience = false;
        public string SoundCueId = "";
        public bool ChangeMusic = false;
        public Killtime.Audio.MusicMood MusicMood = Killtime.Audio.MusicMood.Explore;
        public Killtime.Audio.MusicIntensity MusicIntensity = Killtime.Audio.MusicIntensity.Calm;
        // HÉRITÉ (dé-hardcodé) : les secousses d'ambiance passent par des cartes 🎬
        // (plan relatif + effet HandheldShake). Conservé pour compat JSON, ignoré au runtime.
        public float CameraShakeIntensity = 0f;
        public bool TriggerAlarm = false;
    }

    [Serializable]
    public class SceneAutoSkillCheckData
    {
        public bool HasAutoCheck = false;
        public SkillType RequiredSkill = SkillType.Observation;
        public int TargetDC = 10;
        public string SpecificActorId = "";
        public string SuccessSpeech = "";
        public string SuccessStageDirection = "";
        public string SuccessNextLineId = "";
        public SceneDialogueRewardData SuccessRewards = new();
        public List<ScenarioEffect> SuccessEffects = new();
        public string FailureSpeech = "";
        public string FailureStageDirection = "";
        public string FailureNextLineId = "";
        public List<ScenarioEffect> FailureEffects = new();
    }

}
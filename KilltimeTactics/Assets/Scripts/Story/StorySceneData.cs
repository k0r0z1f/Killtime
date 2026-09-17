using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Core.Character;
using Killtime.Tactics.Grid;

namespace Killtime.Story.Data
{
    [Serializable]
    public class SceneActorSpawnData
    {
        public string ActorId = "acteur";
        public string DisplayName = "Nouvel acteur";
        public string CharacterSheetFileName = "";
        public int Q = 0;
        public int R = -1;
        public bool IsPlayer = true;
        public float FacingAngle = 0f;
        public Color PrimaryColor = new Color(0.2f, 0.5f, 0.9f);
        public Color AccentColor = Color.cyan;
        public string ModelPrefabName = "";
        public string EquippedWeaponName = "";
        public int BaseArmor = 1;
        public bool StartInCombatStance = false;
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
        public bool IsOneShot = true;
        public string TriggerNodeId = "";
    }

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

    [Serializable]
    public class SceneDialogueRewardData
    {
        public int EarnCredits = 0;
        public int EarnXP = 0;
        public string ItemRewardName = "";
        public Killtime.Core.Inventory.ItemType ItemRewardType = Killtime.Core.Inventory.ItemType.Misc;
        public string CompletionObjectiveId = "";
    }

    [Serializable]
    public class SceneDialogueChallengeData
    {
        public bool HasChallenge = false;
        public SkillType RequiredSkill = SkillType.Communication;
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
        public bool FailureTriggersCombat = false;
        public List<ScenarioEffect> FailureEffects = new();
    }

    [Serializable]
    public class SceneDialogueChoiceData
    {
        public string ChoiceId = "choice_1";
        public string Label = "Option de dialogue...";
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
        public string StageDirection = "sourire en coin";
        public string Speech = "Nous partons dès que le signal est validé.";
        public string CameraFocusActorId = "";
        public float CameraPitch = 42f;
        public float CameraDistance = 9.5f;
        public string SoundCueId = "UI_Filter";
        public string NextLineId = "";
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
    }

    [Serializable]
    public class SceneNodeData
    {
        public string NodeId = "nouveau_noeud";
        public ScenarioNodeKind Kind = ScenarioNodeKind.Briefing;
        public string Title = "Nouveau nœud";
        public string Location = "";
        public string Body = "Texte descriptif ou récapitulatif du segment...";
        public string ContinueLabel = "Passer à l'action";
        public string NextNodeId = "";
        public bool TriggerCombatOnEnter = false;
        public float GraphPosX = 0f;
        public float GraphPosY = 0f;
        public float FrameWidth = 0f;
        public float FrameHeight = 0f;
        public List<SceneDialogueLineData> Dialogues = new();
        public List<SceneEventData> Events = new();
        public List<ScenarioObjective> Objectives = new();
        public List<ScenarioChoice> Choices = new();
        public List<ScenarioEffect> EnterEffects = new();

        public SceneDialogueLineData FindDialogue(string lineId)
        {
            if (string.IsNullOrEmpty(lineId)) return null;
            return Dialogues.Find(d => d != null && string.Equals(d.LineId, lineId, StringComparison.OrdinalIgnoreCase));
        }

        public int FindDialogueIndex(string lineId)
        {
            if (string.IsNullOrEmpty(lineId)) return -1;
            return Dialogues.FindIndex(d => d != null && string.Equals(d.LineId, lineId, StringComparison.OrdinalIgnoreCase));
        }

        public SceneEventData FindEvent(string eventId)
        {
            if (string.IsNullOrEmpty(eventId)) return null;
            return Events.Find(e => e != null && string.Equals(e.EventId, eventId, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Serializable]
    public class StorySceneData
    {
        public int Version = 1;
        public string SceneId = "nouvelle_scene";
        public string Volume = "";
        public string Title = "Nouvelle scène";
        public string CanonReference = "";
        public string LinkedMapName = "";
        public string FirstNodeId = "";
        public string EnvironmentId = "";

        public Killtime.UI.TacticalMapSaveData EmbeddedMap;
        public List<SceneActorSpawnData> Actors = new();
        public List<SceneInteractableSpawnData> Interactables = new();
        public List<SceneNodeData> Nodes = new();
        public List<SceneTriggerData> Triggers = new();

        public SceneNodeData FindNode(string nodeId)
        {
            return Nodes.Find(n => n != null && n.NodeId == nodeId);
        }

        public static void EnsureNodeLists(SceneNodeData node)
        {
            if (node == null) return;
            node.Dialogues ??= new List<SceneDialogueLineData>();
            node.Events ??= new List<SceneEventData>();
            node.Objectives ??= new List<ScenarioObjective>();
            node.Choices ??= new List<ScenarioChoice>();
            node.EnterEffects ??= new List<ScenarioEffect>();
        }

        public static void EnsureDeepDefaults(StorySceneData data)
        {
            if (data == null) return;
            data.Actors ??= new List<SceneActorSpawnData>();
            data.Interactables ??= new List<SceneInteractableSpawnData>();
            data.Nodes ??= new List<SceneNodeData>();
            data.Triggers ??= new List<SceneTriggerData>();

            for (int n = 0; n < data.Nodes.Count; n++)
            {
                var node = data.Nodes[n];
                if (node == null) continue;
                EnsureNodeLists(node);

                for (int d = 0; d < node.Dialogues.Count; d++)
                {
                    var line = node.Dialogues[d];
                    if (line == null) continue;
                    line.Prerequisite ??= new ScenePrerequisiteData();
                    line.Ambience ??= new SceneAmbienceData();
                    line.AutoSkillCheck ??= new SceneAutoSkillCheckData();
                    line.AutoSkillCheck.SuccessRewards ??= new SceneDialogueRewardData();
                    line.AutoSkillCheck.SuccessEffects ??= new List<ScenarioEffect>();
                    line.AutoSkillCheck.FailureEffects ??= new List<ScenarioEffect>();
                    line.Choices ??= new List<SceneDialogueChoiceData>();

                    for (int c = 0; c < line.Choices.Count; c++)
                    {
                        var choice = line.Choices[c];
                        if (choice == null) continue;
                        choice.Prerequisite ??= new ScenePrerequisiteData();
                        choice.Challenge ??= new SceneDialogueChallengeData();
                        choice.Challenge.SuccessRewards ??= new SceneDialogueRewardData();
                        choice.Challenge.SuccessEffects ??= new List<ScenarioEffect>();
                        choice.Challenge.FailureEffects ??= new List<ScenarioEffect>();
                        choice.Effects ??= new List<ScenarioEffect>();
                    }
                }

                for (int e = 0; e < node.Events.Count; e++)
                {
                    var evt = node.Events[e];
                    if (evt == null) continue;
                    evt.Prerequisite ??= new ScenePrerequisiteData();
                    evt.Ambience ??= new SceneAmbienceData();
                    evt.AutoSkillCheck ??= new SceneAutoSkillCheckData();
                    evt.AutoSkillCheck.SuccessRewards ??= new SceneDialogueRewardData();
                    evt.AutoSkillCheck.SuccessEffects ??= new List<ScenarioEffect>();
                    evt.AutoSkillCheck.FailureEffects ??= new List<ScenarioEffect>();
                    evt.Challenge ??= new SceneDialogueChallengeData();
                    evt.Challenge.SuccessRewards ??= new SceneDialogueRewardData();
                    evt.Challenge.SuccessEffects ??= new List<ScenarioEffect>();
                    evt.Challenge.FailureEffects ??= new List<ScenarioEffect>();
                    evt.Effects ??= new List<ScenarioEffect>();
                    evt.Rewards ??= new SceneDialogueRewardData();
                }
            }
        }

        public SceneActorSpawnData FindActor(string actorId)
        {
            return Actors.Find(a => a != null && string.Equals(a.ActorId, actorId, StringComparison.OrdinalIgnoreCase));
        }

    }
}
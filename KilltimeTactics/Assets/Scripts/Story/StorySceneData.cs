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
        public bool SpawnInitially = true;
        public string SpawnOnNodeId = "";
        // Sortie cinématique non-bloquante : jouée (fire-and-forget) au spawn différé de l'acteur.
        public List<string> CinematicIds = new();
        public CharacterSheet EmbeddedSheet = new();
        // Position/taille dans le graphe nodal (éditeur uniquement, comme les Triggers).
        // 0/0 = jamais placé → l'éditeur auto-dispose sur la rangée acteurs au chargement.
        public float GraphPosX = 0f;
        public float GraphPosY = 0f;
        public float CardWidth = 340f;
        public float CardHeight = 208f;

        public string GetSummary()
        {
            string faction = IsPlayer ? "[PJ]" : "[PNJ]";
            string where = $"hex ({Q}, {R}) ∠{FacingAngle:0}°";
            string spawn = SpawnInitially ? "init" : (string.IsNullOrEmpty(SpawnOnNodeId) ? "(jamais spawné)" : $"si {SpawnOnNodeId}");
            return $"{faction} {where} · {spawn}";
        }
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
            string test = $"SD {SkillThreshold} · {SkillDefinitions.GetDisplayName(RequiredSkill)}";
            string target = string.IsNullOrEmpty(TriggerNodeId) ? "(aucun saut)" : $"➔ {TriggerNodeId}";
            return $"{where} · {test} {target}";
        }
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
        public string StageDirection = "sourire en coin";
        public string Speech = "Nous partons dès que le signal est validé.";
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

    // ================= CINÉMATIQUES =================
    // Une cinématique = une carte globale 🎬 avec UNE entrée (liée depuis n'importe
    // quelle autre carte) et AUCUNE sortie. Son exécution est fire-and-forget :
    // elle ne bloque jamais l'enclenchement de la carte suivante.
    public enum CinematicEase
    {
        Linear,
        Smooth,
        EaseIn,
        EaseOut,
        EaseInOut,
        Punch
    }

    public enum CinematicCameraEffect
    {
        None,
        HandheldShake,
        PushIn,
        PullOut,
        OrbitLeft,
        OrbitRight,
        FovPunch,
        DutchSway
    }

    [Serializable]
    public class SceneCinematicActorPose
    {
        public string ActorId = "";
        public int Q = 0;
        public int R = 0;
        public float FacingAngle = 0f;
    }

    [Serializable]
    public class SceneCinematicPropPose
    {
        public string InteractableId = "";
        public int Q = 0;
        public int R = 0;
    }

    [Serializable]
    public class SceneCinematicShotData
    {
        public string ShotId = "shot_1";
        public string Label = "Plan 1";
        public float Duration = 2.5f;
        // Voyage caméra automatique : interpolation Start → End sur Duration.
        public Vector3 CamStartPos = new Vector3(0f, 9f, -9f);
        public Vector3 CamStartEuler = new Vector3(45f, 0f, 0f);
        public float CamStartFov = 60f;
        public Vector3 CamEndPos = new Vector3(0f, 4f, -5f);
        public Vector3 CamEndEuler = new Vector3(38f, 12f, 0f);
        public float CamEndFov = 48f;
        public CinematicEase Ease = CinematicEase.Smooth;
        public CinematicCameraEffect MoveEffect = CinematicCameraEffect.None;
        public float ShakeIntensity = 0.15f;
        public string FocusActorId = "";
        // Sous-titre optionnel affiché pendant le plan.
        public string SpeakerId = "";
        public string Speech = "";
        public string SoundCueId = "";
        public bool Letterbox = true;
        // Trajectoires des pions : positions de départ et d'arrivée du plan.
        // Éditées dans le mode éditeur cinématique (capture depuis la carte de combat).
        public List<SceneCinematicActorPose> StartPoses = new();
        public List<SceneCinematicActorPose> EndPoses = new();
        public List<SceneCinematicPropPose> StartProps = new();
        public List<SceneCinematicPropPose> EndProps = new();

        public string GetSummary()
        {
            int moves = 0;
            if (StartPoses != null) moves += StartPoses.Count;
            if (EndPoses != null) moves += EndPoses.Count;
            string sub = string.IsNullOrWhiteSpace(Speech) ? "" : " 💬";
            string fx = MoveEffect == CinematicCameraEffect.None ? "" : $" ✦{MoveEffect}";
            return $"{Duration:0.0}s {Ease}{fx} · {moves} poses{sub}";
        }
    }

    [Serializable]
    public class SceneCinematicData
    {
        public string CinematicId = "cine_1";
        public string Title = "Nouvelle cinématique";
        public bool Skippable = true;
        public float PlaybackSpeed = 1f;
        public float GraphPosX = 0f;
        public float GraphPosY = 0f;
        public float CardWidth = 360f;
        public float CardHeight = 0f;
        public List<SceneCinematicShotData> Shots = new();

        public string GetSummary()
        {
            int n = Shots != null ? Shots.Count : 0;
            float total = 0f;
            if (Shots != null)
                for (int i = 0; i < Shots.Count; i++)
                    if (Shots[i] != null) total += Mathf.Max(0.1f, Shots[i].Duration);
            if (PlaybackSpeed > 0.01f) total /= PlaybackSpeed;
            return n == 0 ? "(aucun plan)" : $"{n} plan(s) · ~{total:0.0}s";
        }
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
        // Sortie cinématique non-bloquante : jouée (fire-and-forget) à l'entrée du nœud.
        public List<string> CinematicIds = new();
        public float GraphPosX = 0f;
        public float GraphPosY = 0f;
        public float FrameWidth = 0f;
        public float FrameHeight = 0f;
        public List<SceneDialogueLineData> Dialogues = new();
        public List<SceneEventData> Events = new();
        public List<SceneConsequenceData> Consequences = new();
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

        public SceneConsequenceData FindConsequence(string consequenceId)
        {
            if (string.IsNullOrEmpty(consequenceId)) return null;
            return Consequences.Find(c => c != null && string.Equals(c.ConsequenceId, consequenceId, StringComparison.OrdinalIgnoreCase));
        }

        public int FindConsequenceIndex(string consequenceId)
        {
            if (string.IsNullOrEmpty(consequenceId)) return -1;
            return Consequences.FindIndex(c => c != null && string.Equals(c.ConsequenceId, consequenceId, StringComparison.OrdinalIgnoreCase));
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
        public string NextSceneId = "";

        public Killtime.UI.TacticalMapSaveData EmbeddedMap;
        public List<SceneActorSpawnData> Actors = new();
        public List<SceneInteractableSpawnData> Interactables = new();
        public List<SceneNodeData> Nodes = new();
        public List<SceneTriggerData> Triggers = new();
        // Cartes cinématiques globales 🎬 : entrée seule, aucune sortie, lecture non-bloquante.
        // Sérialisées dans le JSON de la scène comme tout le reste.
        public List<SceneCinematicData> Cinematics = new();

        public SceneNodeData FindNode(string nodeId)
        {
            return Nodes.Find(n => n != null && n.NodeId == nodeId);
        }

        public SceneCinematicData FindCinematic(string cinematicId)
        {
            if (string.IsNullOrEmpty(cinematicId) || Cinematics == null) return null;
            return Cinematics.Find(c => c != null && string.Equals(c.CinematicId, cinematicId, StringComparison.OrdinalIgnoreCase));
        }

        // --- Helpers sorties cinématiques (listes partagées par tous les types de cartes) ---
        public static List<string> EnsureCineList(List<string> list)
        {
            if (list == null) return new List<string>();
            for (int i = list.Count - 1; i >= 0; i--)
                if (string.IsNullOrWhiteSpace(list[i])) list.RemoveAt(i);
            return list;
        }

        public static void AddCineLink(List<string> list, string cinematicId)
        {
            if (list == null || string.IsNullOrWhiteSpace(cinematicId)) return;
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i], cinematicId, StringComparison.OrdinalIgnoreCase)) return;
            list.Add(cinematicId);
        }

        public static string GetCineSummary(List<string> list)
        {
            if (list == null || list.Count == 0) return "(aucune)";
            var clean = new List<string>(list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(list[i])) continue;
                clean.Add(list[i]);
                if (clean.Count >= 3) break;
            }
            string s = string.Join(", ", clean);
            if (list.Count > clean.Count) s += $" (+{list.Count - clean.Count})";
            return $"🎬→ {s}";
        }

        public static void EnsureNodeLists(SceneNodeData node)
        {
            if (node == null) return;
            node.Dialogues ??= new List<SceneDialogueLineData>();
            node.Events ??= new List<SceneEventData>();
            node.Consequences ??= new List<SceneConsequenceData>();
            node.Objectives ??= new List<ScenarioObjective>();
            node.Choices ??= new List<ScenarioChoice>();
            node.EnterEffects ??= new List<ScenarioEffect>();
        }

        public static void EnsureRewardDefaults(SceneDialogueRewardData rewards)
        {
            if (rewards == null) return;
            rewards.AdditionalItems ??= new List<SceneItemRewardEntry>();
            for (int i = rewards.AdditionalItems.Count - 1; i >= 0; i--)
            {
                if (rewards.AdditionalItems[i] == null)
                    rewards.AdditionalItems.RemoveAt(i);
            }
            if (rewards.ItemRewardQuantity < 1) rewards.ItemRewardQuantity = 1;
        }

        public static void EnsureDeepDefaults(StorySceneData data)
        {
            if (data == null) return;
            data.Actors ??= new List<SceneActorSpawnData>();
            data.Interactables ??= new List<SceneInteractableSpawnData>();
            data.Nodes ??= new List<SceneNodeData>();
            data.Triggers ??= new List<SceneTriggerData>();
            data.Cinematics ??= new List<SceneCinematicData>();
            for (int ci = 0; ci < data.Cinematics.Count; ci++)
            {
                var cine = data.Cinematics[ci];
                if (cine == null) continue;
                if (string.IsNullOrWhiteSpace(cine.CinematicId)) cine.CinematicId = $"cine_{ci + 1}";
                if (cine.PlaybackSpeed < 0.1f) cine.PlaybackSpeed = 1f;
                cine.Shots ??= new List<SceneCinematicShotData>();
                for (int s = 0; s < cine.Shots.Count; s++)
                {
                    var shot = cine.Shots[s];
                    if (shot == null) continue;
                    if (string.IsNullOrWhiteSpace(shot.ShotId)) shot.ShotId = $"shot_{s + 1}";
                    if (shot.Duration < 0.1f) shot.Duration = 0.1f;
                    shot.StartPoses ??= new List<SceneCinematicActorPose>();
                    shot.EndPoses ??= new List<SceneCinematicActorPose>();
                    shot.StartProps ??= new List<SceneCinematicPropPose>();
                    shot.EndProps ??= new List<SceneCinematicPropPose>();
                }
            }
            for (int t = 0; t < data.Triggers.Count; t++)
            {
                if (data.Triggers[t] == null) continue;
                data.Triggers[t].CinematicIds = EnsureCineList(data.Triggers[t].CinematicIds);
            }
            for (int ii = 0; ii < data.Interactables.Count; ii++)
            {
                if (data.Interactables[ii] == null) continue;
                data.Interactables[ii].CinematicIds = EnsureCineList(data.Interactables[ii].CinematicIds);
            }
            for (int ai = 0; ai < data.Actors.Count; ai++)
            {
                if (data.Actors[ai] == null) continue;
                data.Actors[ai].CinematicIds = EnsureCineList(data.Actors[ai].CinematicIds);
            }

            for (int a = 0; a < data.Actors.Count; a++)
            {
                var actor = data.Actors[a];
                if (actor == null) continue;
                actor.EmbeddedSheet ??= new CharacterSheet();
                actor.EmbeddedSheet.Skills ??= new List<SkillProgressionEntry>();
                actor.EmbeddedSheet.UnlockedSpecializations ??= new List<string>();
                actor.EmbeddedSheet.LearnedSpells ??= new();
                actor.EmbeddedSheet.Inventory ??= new List<Killtime.Core.Inventory.InventoryItem>();
            }

            for (int n = 0; n < data.Nodes.Count; n++)
            {
                var node = data.Nodes[n];
                if (node == null) continue;
                EnsureNodeLists(node);
                node.CinematicIds = EnsureCineList(node.CinematicIds);

                for (int d = 0; d < node.Dialogues.Count; d++)
                {
                    var line = node.Dialogues[d];
                    if (line == null) continue;
                    line.CinematicIds = EnsureCineList(line.CinematicIds);
                    line.Prerequisite ??= new ScenePrerequisiteData();
                    line.Ambience ??= new SceneAmbienceData();
                    line.AutoSkillCheck ??= new SceneAutoSkillCheckData();
                    line.AutoSkillCheck.SuccessRewards ??= new SceneDialogueRewardData();
                    EnsureRewardDefaults(line.AutoSkillCheck.SuccessRewards);
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
                        EnsureRewardDefaults(choice.Challenge.SuccessRewards);
                        choice.Challenge.SuccessEffects ??= new List<ScenarioEffect>();
                        choice.Challenge.FailureEffects ??= new List<ScenarioEffect>();
                        choice.Effects ??= new List<ScenarioEffect>();
                    }
                }

                for (int e = 0; e < node.Events.Count; e++)
                {
                    var evt = node.Events[e];
                    if (evt == null) continue;
                    evt.CinematicIds = EnsureCineList(evt.CinematicIds);
                    evt.Prerequisite ??= new ScenePrerequisiteData();
                    evt.Ambience ??= new SceneAmbienceData();
                    evt.AutoSkillCheck ??= new SceneAutoSkillCheckData();
                    evt.AutoSkillCheck.SuccessRewards ??= new SceneDialogueRewardData();
                    EnsureRewardDefaults(evt.AutoSkillCheck.SuccessRewards);
                    evt.AutoSkillCheck.SuccessEffects ??= new List<ScenarioEffect>();
                    evt.AutoSkillCheck.FailureEffects ??= new List<ScenarioEffect>();
                    evt.Challenge ??= new SceneDialogueChallengeData();
                    evt.Challenge.SuccessRewards ??= new SceneDialogueRewardData();
                    EnsureRewardDefaults(evt.Challenge.SuccessRewards);
                    evt.Challenge.SuccessEffects ??= new List<ScenarioEffect>();
                    evt.Challenge.FailureEffects ??= new List<ScenarioEffect>();
                    evt.Effects ??= new List<ScenarioEffect>();
                    evt.Rewards ??= new SceneDialogueRewardData();
                    EnsureRewardDefaults(evt.Rewards);

                    for (int k = 0; k < node.Consequences.Count; k++)
                    {
                        var cons = node.Consequences[k];
                        if (cons == null) continue;
                        cons.CinematicIds = EnsureCineList(cons.CinematicIds);
                        cons.Rewards ??= new SceneDialogueRewardData();
                        EnsureRewardDefaults(cons.Rewards);
                        cons.Effects ??= new List<ScenarioEffect>();
                    }
                }

                // Nœuds sans événement : les conséquences existent quand même (boucle ci-dessus
                // imbriquée aux événements) → sécurise leurs listes cinématiques ici aussi.
                if (node.Consequences != null)
                {
                    for (int k = 0; k < node.Consequences.Count; k++)
                    {
                        var cons = node.Consequences[k];
                        if (cons == null) continue;
                        cons.CinematicIds = EnsureCineList(cons.CinematicIds);
                    }
                }
            }
        }

        public SceneActorSpawnData FindActor(string actorId)
        {
            return Actors.Find(a => a != null && string.Equals(a.ActorId, actorId, StringComparison.OrdinalIgnoreCase));
        }

    }
}
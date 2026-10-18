using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Core.Character;
using Killtime.Tactics.Grid;

namespace Killtime.Story.Data
{
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

        public SceneLightingData Lighting = new();
        public List<ScenePlaceholderData> EnvironmentPlaceholders = new();
        public Killtime.UI.TacticalMapSaveData EmbeddedMap;
        public List<SceneActorSpawnData> Actors = new();
        public List<SceneInteractableSpawnData> Interactables = new();
        public List<SceneNodeData> Nodes = new();
        public List<SceneTriggerData> Triggers = new();
        // Cartes cinématiques globales 🎬 : entrée seule, aucune sortie, lecture non-bloquante.
        // Sérialisées dans le JSON de la scène comme tout le reste.
        public List<SceneCinematicData> Cinematics = new();
        // Cartes son/fx 🔊 : branchées depuis le bas des cartes (sauf frames de nœuds),
        // elles déclenchent un son au début/fin de leur carte liée. Voir SceneSoundData.
        public List<SceneSoundData> Sounds = new();
        // Cartes d'ambiance 🌌 : configurées depuis le catalogue JSON d'ambiances,
        // déclenchées par l'entrée supérieure et coupées par la sortie inférieure.
        public List<SceneAmbienceTrackData> Ambiences = new();
        // Réglages audio globaux de la scène (master, mute, kill-switches).
        public SceneAudioSettings AudioSettings = new();

        public SceneNodeData FindNode(string nodeId)
        {
            return Nodes.Find(n => n != null && n.NodeId == nodeId);
        }

        public SceneCinematicData FindCinematic(string cinematicId)
        {
            if (string.IsNullOrEmpty(cinematicId) || Cinematics == null) return null;
            return Cinematics.Find(c => c != null && string.Equals(c.CinematicId, cinematicId, StringComparison.OrdinalIgnoreCase));
        }

        public SceneSoundData FindSound(string soundId)
        {
            if (string.IsNullOrEmpty(soundId) || Sounds == null) return null;
            return Sounds.Find(s => s != null && string.Equals(s.SoundId, soundId, StringComparison.OrdinalIgnoreCase));
        }

        public SceneAmbienceTrackData FindAmbience(string ambienceId)
        {
            if (string.IsNullOrEmpty(ambienceId) || Ambiences == null) return null;
            return Ambiences.Find(a => a != null && string.Equals(a.AmbienceId, ambienceId, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Ambiances qui démarrent depuis une carte source (fil bas → entrée haute ambiance).
        /// </summary>
        public List<SceneAmbienceTrackData> FindAmbiencesStartingFrom(string sourceCardId)
        {
            var result = new List<SceneAmbienceTrackData>();
            if (string.IsNullOrWhiteSpace(sourceCardId) || Ambiences == null) return result;
            for (int i = 0; i < Ambiences.Count; i++)
            {
                var a = Ambiences[i];
                if (a == null || string.IsNullOrWhiteSpace(a.StartSourceCardId)) continue;
                if (string.Equals(a.StartSourceCardId.Trim(), sourceCardId.Trim(), StringComparison.OrdinalIgnoreCase))
                    result.Add(a);
            }
            return result;
        }

        /// <summary>
        /// Ambiances qui s'arrêtent lorsqu'une carte cible est atteinte (fil bas ambiance → bas carte).
        /// </summary>
        public List<SceneAmbienceTrackData> FindAmbiencesStoppingFrom(string sourceCardId)
        {
            var result = new List<SceneAmbienceTrackData>();
            if (string.IsNullOrWhiteSpace(sourceCardId) || Ambiences == null) return result;
            for (int i = 0; i < Ambiences.Count; i++)
            {
                var a = Ambiences[i];
                if (a == null || string.IsNullOrWhiteSpace(a.StopSourceCardId)) continue;
                if (string.Equals(a.StopSourceCardId.Trim(), sourceCardId.Trim(), StringComparison.OrdinalIgnoreCase))
                    result.Add(a);
            }
            return result;
        }

        /// <summary>
        /// Sons liés depuis une carte source (fil bas → carte 🔊) : le lien vit sur
        /// les cartes sons (SourceCardId), dans l'ordre du graphe.
        /// </summary>
        public List<SceneSoundData> FindSoundsFrom(string sourceCardId)
        {
            var result = new List<SceneSoundData>();
            if (string.IsNullOrWhiteSpace(sourceCardId) || Sounds == null) return result;
            for (int i = 0; i < Sounds.Count; i++)
            {
                var s = Sounds[i];
                if (s == null || string.IsNullOrWhiteSpace(s.SourceCardId)) continue;
                if (string.Equals(s.SourceCardId.Trim(), sourceCardId.Trim(), StringComparison.OrdinalIgnoreCase))
                    result.Add(s);
            }
            return result;
        }

        /// <summary>
        /// Sons dont l'ARRÊT explicite est branché sur une carte cible
        /// (fil bas du son → bas de la carte). Miroir de FindAmbiencesStoppingFrom.
        /// </summary>
        public List<SceneSoundData> FindSoundsStoppingFrom(string sourceCardId)
        {
            var result = new List<SceneSoundData>();
            if (string.IsNullOrWhiteSpace(sourceCardId) || Sounds == null) return result;
            for (int i = 0; i < Sounds.Count; i++)
            {
                var s = Sounds[i];
                if (s == null || string.IsNullOrWhiteSpace(s.StopSourceCardId)) continue;
                if (string.Equals(s.StopSourceCardId.Trim(), sourceCardId.Trim(), StringComparison.OrdinalIgnoreCase))
                    result.Add(s);
            }
            return result;
        }

        /// <summary>
        /// Vrai si au moins une carte son démarre depuis cette carte (arbitrage legacy).
        /// </summary>
        public bool HasLinkedSounds(string sourceCardId)
        {
            if (string.IsNullOrWhiteSpace(sourceCardId) || Sounds == null) return false;
            for (int i = 0; i < Sounds.Count; i++)
            {
                var s = Sounds[i];
                if (s == null || s.Muted) continue;
                if (string.IsNullOrWhiteSpace(s.SourceCardId) || string.IsNullOrWhiteSpace(s.SoundCueId)) continue;
                if (string.Equals(s.SourceCardId.Trim(), sourceCardId.Trim(), StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
        /// <summary>
        /// Tête d'une chaîne cinématique (amorçage automatique) : remonte les
        /// liens "fin →" entre cinématiques jusqu'à la première carte de la chaîne.
        /// Ex : cine_11 ─fin→ cine_1 ─fin→ line_vance_01 : la tête de cine_1 est
        /// cine_11, c'est elle qui doit démarrer (le reste suit via Fin→).
        /// Règles de repli (aucun changement de comportement) : aucun amont → soi-même ;
        /// plusieurs amonts (embranchement ambigu) → le prédécesseur direct ;
        /// boucle → arrêt sans tourner en rond. Insensible à la casse, tolère les espaces.
        /// </summary>
        public string FindCinematicChainHead(string cinematicId)
        {
            if (string.IsNullOrWhiteSpace(cinematicId) || Cinematics == null) return cinematicId;
            string cur = cinematicId.Trim();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { cur };
            while (true)
            {
                string upstream = null;
                int count = 0;
                for (int i = 0; i < Cinematics.Count; i++)
                {
                    var c = Cinematics[i];
                    if (c == null || string.IsNullOrWhiteSpace(c.CinematicId)) continue;
                    string cid = c.CinematicId.Trim();
                    if (string.Equals(cid, cur, StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.IsNullOrWhiteSpace(c.NextTargetId)) continue;
                    if (!string.Equals(c.NextTargetId.Trim(), cur, StringComparison.OrdinalIgnoreCase)) continue;
                    if (seen.Contains(cid)) continue;
                    count++;
                    if (count == 1) upstream = c.CinematicId;
                    else break;
                }
                if (count != 1) return cur;
                seen.Add(upstream.Trim());
                cur = upstream;
            }
        }

        /// <summary>
        /// Détecte si la cinématique d'ouverture de la scène démarre par un plan supraluminique (warp).
        /// Si oui, le ciel spatial est initialisé au warp maximal (100% contracté au centre)
        /// dès le premier frame pour qu'aucun flash de voûte normale n'apparaisse avant l'étirement.
        /// </summary>
        public bool HasOpeningSuperluminalWarp()
        {
            if (Cinematics == null || Cinematics.Count == 0) return false;
            for (int i = 0; i < Cinematics.Count; i++)
            {
                var c = Cinematics[i];
                if (c == null || c.Shots == null || c.Shots.Count == 0) continue;
                string headId = FindCinematicChainHead(c.CinematicId);
                var headCine = FindCinematic(headId);
                if (headCine != null && headCine.Shots != null && headCine.Shots.Count > 0 && headCine.Shots[0] != null && headCine.Shots[0].SuperluminalWarp)
                {
                    return true;
                }
            }
            return false;
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
            data.Sounds ??= new List<SceneSoundData>();
            data.Ambiences ??= new List<SceneAmbienceTrackData>();
            data.Lighting ??= new SceneLightingData();
            data.EnvironmentPlaceholders ??= new List<ScenePlaceholderData>();
            data.AudioSettings ??= new SceneAudioSettings();
            if (data.AudioSettings.MasterVolumeScale < 0f) data.AudioSettings.MasterVolumeScale = 0f;
            else if (data.AudioSettings.MasterVolumeScale > 2f) data.AudioSettings.MasterVolumeScale = 2f;
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
                    if (shot.StartDelay < 0f) shot.StartDelay = 0f;
                    if (shot.SoundDuration < 0f) shot.SoundDuration = 0f;
                    if (shot.TrackDistance < 0.5f) shot.TrackDistance = 0.5f;
                    else if (shot.TrackDistance > 35f) shot.TrackDistance = 35f;
                    shot.StartPoses ??= new List<SceneCinematicActorPose>();
                    shot.EndPoses ??= new List<SceneCinematicActorPose>();
                    shot.StartProps ??= new List<SceneCinematicPropPose>();
                    shot.EndProps ??= new List<SceneCinematicPropPose>();
                }
            }
            for (int i = 0; i < data.Sounds.Count; i++)
            {
                var snd = data.Sounds[i];
                if (snd == null) continue;
                if (string.IsNullOrWhiteSpace(snd.SoundId)) snd.SoundId = $"snd_{i + 1}";
                if (snd.DelaySeconds < 0f) snd.DelaySeconds = 0f;
                if (snd.DurationSeconds < 0f) snd.DurationSeconds = 0f;
                if (snd.VolumeScale < 0f) snd.VolumeScale = 0f;
                else if (snd.VolumeScale > 2f) snd.VolumeScale = 2f;
                if (snd.FadeInDuration < 0f) snd.FadeInDuration = 0f;
                else if (snd.FadeInDuration > 20f) snd.FadeInDuration = 20f;
                if (snd.FadeOutDuration < 0f) snd.FadeOutDuration = 0f;
                else if (snd.FadeOutDuration > 20f) snd.FadeOutDuration = 20f;
                if (snd.PitchScale < 0.25f) snd.PitchScale = 1f;
                else if (snd.PitchScale > 3f) snd.PitchScale = 3f;
                if (snd.Priority < 0) snd.Priority = 0;
                else if (snd.Priority > 255) snd.Priority = 255;
                if (snd.CooldownSeconds < 0f) snd.CooldownSeconds = 0f;
                else if (snd.CooldownSeconds > 30f) snd.CooldownSeconds = 30f;
                if (snd.Radius < 1f) snd.Radius = 15f;
                else if (snd.Radius > 120f) snd.Radius = 120f;
                // Hors-limites (JSON édité à la main) : GUI.Toolbar planterait
                // tout le graphe — repli silencieux sur le début.
                if (!System.Enum.IsDefined(typeof(SoundTriggerPoint), snd.TriggerPoint))
                    snd.TriggerPoint = SoundTriggerPoint.AtLinkedStart;
                if (snd.CardWidth < 100f) snd.CardWidth = 300f;
                if (snd.CardHeight < 80f) snd.CardHeight = 340f;
            }
            for (int i = 0; i < data.Ambiences.Count; i++)
            {
                var amb = data.Ambiences[i];
                if (amb == null) continue;
                if (string.IsNullOrWhiteSpace(amb.AmbienceId)) amb.AmbienceId = $"amb_{i + 1}";
                if (string.IsNullOrWhiteSpace(amb.PresetId)) amb.PresetId = "amb_asteroid_base";
                if (amb.VolumeScale < 0f) amb.VolumeScale = 0f;
                else if (amb.VolumeScale > 2f) amb.VolumeScale = 2f;
                if (amb.FadeInDuration < 0f) amb.FadeInDuration = 0f;
                if (amb.FadeOutDuration < 0f) amb.FadeOutDuration = 0f;
                if (amb.Radius < 1f) amb.Radius = 15f;
                if (amb.CardWidth < 100f) amb.CardWidth = 320f;
                if (amb.CardHeight < 80f) amb.CardHeight = 240f;
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
                data.Actors[ai].TalkEntries ??= new List<SceneActorTalkEntry>();
                for (int te = data.Actors[ai].TalkEntries.Count - 1; te >= 0; te--)
                    if (data.Actors[ai].TalkEntries[te] == null)
                        data.Actors[ai].TalkEntries.RemoveAt(te);
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
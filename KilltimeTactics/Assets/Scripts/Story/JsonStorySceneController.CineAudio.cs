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
        /// Déclenche les cinématiques liées à une carte en FIRE-AND-FORGET :
        /// retour immédiat, la carte suivante s'enclenche sans attendre la fin
        /// de la cinématique. Les ids inconnus sont ignorés avec un avertissement.
        /// À la FIN de chaque cinématique, sa sortie "fin →" (NextTargetId) est
        /// résolue via ExecuteCinematicChain (chaînage explicite : cine, nœud,
        /// réplique, conséquence ou événement). chainVisited casse les boucles A→B→A.
        /// logWarnings=false : mode silencieux pour les tests (aucun warning console).
        /// </summary>
        public void FireLinkedCinematics(List<string> cinematicIds, HashSet<string> chainVisited = null, bool logWarnings = true)
        {
            if (cinematicIds == null || cinematicIds.Count == 0 || State._sceneData == null) return;
            var director = FindAnyObjectByType<CinematicDirector>();
            if (director == null)
            {
                if (logWarnings) Debug.LogWarning("[JsonStorySceneController] CinematicDirector introuvable : cinématiques ignorées (ajoutez CinematicDirector sur la caméra).");
                // Erreur VISIBLE en jeu (1x par session) : sinon la cinématique "ne joue
                // pas" sans aucune explication (symptôme trompeur).
                if (logWarnings && !_warnedMissingCinematicDirector)
                {
                    _warnedMissingCinematicDirector = true;
                    CombatHUD.Instance?.AddAdvancedLog("<color=red><b>[CINÉMATIQUE] Aucun CinematicDirector dans la scène : cinématique ignorée (ajoutez CinematicDirector sur la caméra).</b></color>", LogCategory.MovementAndTurns, "[CINÉMATIQUE]", Color.red);
                }
                return;
            }
            bool firedOnce = false;
            for (int i = 0; i < cinematicIds.Count; i++)
            {
                string id = cinematicIds[i];
                if (string.IsNullOrWhiteSpace(id)) continue;
                var cine = State._sceneData.FindCinematic(id);
                if (cine == null)
                {
                    if (logWarnings) Debug.LogWarning($"[JsonStorySceneController] Cinématique introuvable : '{id}'.");
                    continue;
                }
                var captured = cine;
                var visited = chainVisited;
                // Batch même-frame : la 1re préempte la ciné en cours (raccord
                // fluide), les suivantes s'enfilent derrière (pas de cut en cascade).
                bool preempt = !firedOnce;
                firedOnce = true;
                director.PlaySceneCinematic(captured, () => ExecuteCinematicChain(captured, visited), preempt);
                // Sons branchés au début de la cinématique (fil bas → carte 🔊).
                FireLinkedSounds(id, SoundTriggerPoint.AtLinkedStart);
                EvaluateLinkedAmbiences(id);
                _warnedMissingCinematicDirector = false;
                CombatHUD.Instance?.AddAdvancedLog($"🎬 <b>Cinématique :</b> {cine.Title} [{cine.CinematicId}]", LogCategory.MovementAndTurns, "[CINÉMATIQUE]", new Color(1f, 0.5f, 0.9f));
            }
        }

        /// <summary>
        /// Déclenche les cartes son 🔊 liées depuis une carte (fil bas du graphe) :
        /// celles configurées sur `point` (début/fin) démarrent avec leur délai,
        /// en fire-and-forget (n'attend jamais). Idéal pour nappes, stingers,
        /// ponctuations de répliques et transitions de nœuds.
        /// Contrôle total : master/mute scène, mute carte, cooldown anti-spam,
        /// pitch, loop + fades, priorité, spatialisation 3D, suivi d'instance
        /// (stop explicite via StopSourceCardId, kill-switch, monitor).
        /// </summary>
        public void FireLinkedSounds(string sourceCardId, SoundTriggerPoint point)
        {
            if (string.IsNullOrWhiteSpace(sourceCardId) || State._sceneData == null || State._sceneData.Sounds == null) return;
            var audio = State._sceneData.AudioSettings;
            float master = audio != null ? Mathf.Clamp(audio.MasterVolumeScale, 0f, 2f) : 1f;
            bool sceneMuted = audio != null && audio.Muted;
            for (int i = 0; i < State._sceneData.Sounds.Count; i++)
            {
                var snd = State._sceneData.Sounds[i];
                if (snd == null || snd.TriggerPoint != point) continue;
                if (string.IsNullOrWhiteSpace(snd.SourceCardId) || string.IsNullOrWhiteSpace(snd.SoundCueId)) continue;
                if (!string.Equals(snd.SourceCardId.Trim(), sourceCardId.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
                if (snd.Muted || sceneMuted) continue;
                // Cooldown anti-spam (trigger répété, chaîne fin→ rejouée).
                if (snd.CooldownSeconds > 0.01f)
                {
                    string coolKey = string.IsNullOrWhiteSpace(snd.SoundId) ? snd.SoundCueId : snd.SoundId;
                    if (State._soundCooldowns.TryGetValue(coolKey, out float last) && Time.unscaledTime - last < snd.CooldownSeconds)
                        continue;
                    State._soundCooldowns[coolKey] = Time.unscaledTime;
                }
                float volume = Mathf.Clamp(snd.VolumeScale, 0f, 2f) * master;
                if (volume <= 0.001f) continue;
                var queued = new QueuedSceneSound
                {
                    InstanceKey = string.IsNullOrWhiteSpace(snd.SoundId) ? null : snd.SoundId.Trim(),
                    CueId = snd.SoundCueId,
                    Volume = volume,
                    Generation = State._sceneGeneration,
                    Delay = Mathf.Max(0f, snd.DelaySeconds),
                    Duration = Mathf.Max(0f, snd.DurationSeconds),
                    Pitch = Mathf.Clamp(snd.PitchScale <= 0f ? 1f : snd.PitchScale, 0.25f, 3f),
                    Loop = snd.Loop,
                    FadeIn = Mathf.Max(0f, snd.FadeInDuration),
                    FadeOut = Mathf.Max(0f, snd.FadeOutDuration),
                    Priority = Mathf.Clamp(snd.Priority, 0, 255),
                    Spatial = snd.Spatialized,
                    Radius = Mathf.Clamp(snd.Radius < 1f ? 15f : snd.Radius, 1f, 120f),
                    WorldPos = Vector3.zero
                };
                if (queued.Spatial && State._grid != null)
                {
                    var node = State._grid.GetNode(new HexCoordinates(snd.Q, snd.R));
                    if (node != null) queued.WorldPos = node.WorldPosition;
                }
                try { StartCoroutine(PlaySoundDelayed(queued)); }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[JsonStorySceneController] Son '{snd.SoundId}' non démarré : {ex.Message}");
                }
            }
        }

        private struct QueuedSceneSound
        {
            public string InstanceKey;
            public string CueId;
            public int Generation;
            public float Volume;
            public float Delay;
            public float Duration;
            public float Pitch;
            public bool Loop;
            public float FadeIn;
            public float FadeOut;
            public int Priority;
            public bool Spatial;
            public Vector3 WorldPos;
            public float Radius;
        }

        private System.Collections.IEnumerator PlaySoundDelayed(QueuedSceneSound q)
        {
            float waited = 0f;
            while (waited < q.Delay)
            {
                while (CombatHUD.IsPaused) yield return null;
                waited += Mathf.Min(Time.unscaledDeltaTime, 0.1f);
                yield return null;
            }
            while (CombatHUD.IsPaused) yield return null;
            if (string.IsNullOrWhiteSpace(q.CueId)) yield break;
            // Scène changée pendant le délai : la file appartient à l'ancienne scène.
            if (q.Generation != State._sceneGeneration) yield break;
            // Mute scène activé pendant le délai : on annule (pas de son fantôme).
            if (State._sceneData != null && State._sceneData.AudioSettings != null && State._sceneData.AudioSettings.Muted) yield break;

            string cue = q.CueId.Trim();
            bool isSwell = CinematicDirector.IsSwellCue(cue);
            if (isSwell && CinematicDirector.IsSwellPlaying())
            {
                yield break;
            }

            AudioSource src = null;
            try
            {
                // 1) Banque sons/fx (recettes JSON) prioritaire : id insensible à la casse.
                var bankEntry = SoundBankCatalog.Find(cue);
                if (bankEntry != null)
                {
                    float targetDur = q.Duration > 0.05f ? q.Duration : bankEntry.duration;
                    var bankClip = ProceduralAudioFactory.GetBankClip(bankEntry, targetDur);
                    if (bankClip != null)
                    {
                        if (KilltimeAudioManager.Instance != null)
                            src = KilltimeAudioManager.Instance.PlayBankSound(
                                bankEntry.id, bankClip, q.Volume * bankEntry.volume,
                                q.Pitch, q.Loop, q.FadeIn, q.Priority,
                                q.Spatial, q.WorldPos, q.Radius, q.InstanceKey);
                        if (isSwell && src != null)
                        {
                            CinematicDirector.RegisterActiveSwell(src, bankEntry.id);
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[JsonStorySceneController] Recette banque '{cue}' sans clip.");
                    }
                }
                // 2) Repli : SoundId procédural historique.
                else if (System.Enum.TryParse<SoundId>(cue, out var soundEnum))
                {
                    if (KilltimeAudioManager.Instance != null)
                        src = KilltimeAudioManager.Instance.PlayUI(soundEnum, q.Volume);
                    if (isSwell && src != null)
                    {
                        CinematicDirector.RegisterActiveSwell(src, cue);
                    }
                }
                else
                {
                    Debug.LogWarning($"[JsonStorySceneController] SoundCueId inconnu : '{q.CueId}'.");
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[JsonStorySceneController] Échec lecture son '{q.CueId}' : {ex.Message}");
            }

            if (src != null && q.Duration > 0.05f)
            {
                float playTimer = 0f;
                while (playTimer < q.Duration && src != null && src.isPlaying)
                {
                    if (CombatHUD.IsPaused)
                    {
                        src.Pause();
                        while (CombatHUD.IsPaused) yield return null;
                        src.UnPause();
                    }
                    playTimer += Time.unscaledDeltaTime;
                    yield return null;
                }
                // Arrêt : fade-out si la carte en prévoit un (et instance suivie),
                // sinon stop net comme avant (non-régression).
                if (src != null && (!isSwell || playTimer >= q.Duration))
                {
                    if (!string.IsNullOrWhiteSpace(q.InstanceKey) && q.FadeOut > 0.05f && KilltimeAudioManager.Instance != null)
                        KilltimeAudioManager.Instance.StopSceneSound(q.InstanceKey, q.FadeOut);
                    else
                        src.Stop();
                }
            }
        }

        /// <summary>
        /// Arrêts explicites de sons 🔊 branchés sur une carte cible
        /// (fil bas du son → bas de la carte, StopSourceCardId).
        /// Appelé depuis EvaluateLinkedAmbiences (point de passage unique : tous les
        /// sites d'évaluation — ciné, spawn, interactable, trigger, event,
        /// conséquence, nœud, réplique — propagent les stops sans autre modif).
        /// </summary>
        public void EvaluateLinkedSoundStops(string cardId)
        {
            if (string.IsNullOrWhiteSpace(cardId) || State._sceneData == null || KilltimeAudioManager.Instance == null) return;
            var toStop = State._sceneData.FindSoundsStoppingFrom(cardId);
            for (int i = 0; i < toStop.Count; i++)
            {
                var snd = toStop[i];
                if (snd == null || string.IsNullOrWhiteSpace(snd.SoundId)) continue;
                KilltimeAudioManager.Instance.StopSceneSound(snd.SoundId, Mathf.Max(0f, snd.FadeOutDuration));
            }
        }

        /// <summary>
        /// Évalue les cartes d'ambiance 🌌 liées : démarre celles branchées en StartSourceCardId
        /// et stoppe celles branchées en StopSourceCardId.
        /// Point de passage unique : propage aussi les arrêts explicites de sons 🔊
        /// (EvaluateLinkedSoundStops) pour que chaque site d'évaluation existant
        /// (ciné, spawn, interactable, trigger, event, conséquence, nœud, réplique)
        /// les reçoive sans autre modification.
        /// </summary>
        public void EvaluateLinkedAmbiences(string cardId)
        {
            EvaluateLinkedSoundStops(cardId);
            if (string.IsNullOrWhiteSpace(cardId) || State._sceneData == null || KilltimeAudioManager.Instance == null) return;

            var toStop = State._sceneData.FindAmbiencesStoppingFrom(cardId);
            for (int i = 0; i < toStop.Count; i++)
            {
                var amb = toStop[i];
                if (amb == null) continue;
                KilltimeAudioManager.Instance.StopSceneAmbience(amb.AmbienceId, amb.FadeOutDuration);
            }

            var toStart = State._sceneData.FindAmbiencesStartingFrom(cardId);
            for (int i = 0; i < toStart.Count; i++)
            {
                var amb = toStart[i];
                if (amb == null) continue;
                Vector3 worldPos = Vector3.zero;
                if (amb.Spatialized && State._grid != null)
                {
                    var node = State._grid.GetNode(new HexCoordinates(amb.Q, amb.R));
                    if (node != null) worldPos = node.WorldPosition;
                }
                KilltimeAudioManager.Instance.PlaySceneAmbience(
                    amb.AmbienceId,
                    amb.PresetId,
                    amb.VolumeScale,
                    amb.FadeInDuration,
                    amb.Spatialized,
                    worldPos,
                    amb.Radius
                );
            }
        }

        /// <summary>
        /// Chaînage "fin →" d'une carte cinématique : résout NextTargetId à la fin
        /// de la lecture (skip inclus ; jamais en cas d'abort — Stop n'invoque pas).
        /// Ordre : autre cinématique (enchaînée, anti-boucle) → nœud (saut, sauf
        /// vers le nœud courant) → réplique/conséquence du nœud courant → événement
        /// (exécution ponctuelle, tous nœuds). Id inconnu : avertissement, rien d'autre.
        /// </summary>
        private void ExecuteCinematicChain(SceneCinematicData cine, HashSet<string> visited)
        {
            if (cine == null || State._sceneData == null) return;
            // Sons branchés à la fin de la cinématique (fil bas → carte 🔊),
            // + arrêts explicites branchés sur elle (■→) : la fin de chaîne n'est
            // pas un site d'évaluation d'ambiances, les stops sons y sont donc
            // propagés directement. Jamais en cas d'abort (Stop n'invoque pas ce callback).
            FireLinkedSounds(cine.CinematicId, SoundTriggerPoint.AtLinkedEnd);
            EvaluateLinkedSoundStops(cine.CinematicId);
            string targetId = (cine.NextTargetId ?? "").Trim();
            if (string.IsNullOrEmpty(targetId)) return;

            // 1. Autre cinématique : enchaîner (anti-boucle A→B→A).
            var nextCine = State._sceneData.FindCinematic(targetId);
            if (nextCine != null)
            {
                visited ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!visited.Add(targetId))
                {
                    Debug.LogWarning($"[JsonStorySceneController] Boucle de cinématiques détectée vers '{targetId}' : chaîne interrompue.");
                    CombatHUD.Instance?.AddAdvancedLog($"<color=grey>[CINÉMATIQUE] Boucle détectée vers '{targetId}' : chaîne interrompue.</color>", LogCategory.MovementAndTurns, "[CINÉMATIQUE]", Color.gray);
                    return;
                }
                CombatHUD.Instance?.AddAdvancedLog($"🎬 <b>Chaîne :</b> {cine.CinematicId} ─fin→ {targetId}", LogCategory.MovementAndTurns, "[CINÉMATIQUE]", new Color(1f, 0.5f, 0.9f));
                FireLinkedCinematics(new List<string> { targetId }, visited);
                return;
            }

            // 2. Nœud : saut (si cible = nœud courant, mise en scène de la réplique d'entrée).
            if (State._sceneData.FindNode(targetId) != null)
            {
                string currentId = State._director != null && State._director.CurrentNode != null ? State._director.CurrentNode.Id : "";
                if (string.Equals(currentId, targetId, StringComparison.OrdinalIgnoreCase))
                {
                    StageCurrentLine();
                    return;
                }
                CombatHUD.Instance?.AddAdvancedLog($"🎬 <b>Chaîne :</b> {cine.CinematicId} ─fin→ nœud {targetId}", LogCategory.MovementAndTurns, "[CINÉMATIQUE]", new Color(1f, 0.5f, 0.9f));
                State._director?.GoToNode(targetId);
                return;
            }

            // 3. Réplique / conséquence du nœud courant (routage explicite existant).
            // firePredecessors=false : la cinématique amont vient de se terminer, inutile
            // (et bouclant) de la rejouer — idem pour l'événement ci-dessous.
            // Une conséquence déjà résolue ce nœud stoppe la chaîne (anti double-payload).
            var dataNodeNow = State._director != null ? State._sceneData.FindNode(State._director.CurrentNode?.Id) : null;
            if (dataNodeNow != null && dataNodeNow.FindConsequence(targetId) != null
                && State._resolvedCardIdsThisNode.Contains(targetId))
            {
                return;
            }
            if (TryAdvanceToId(targetId, null, false)) return;

            // 4. Événement (tous nœuds) : exécution ponctuelle, sauf déjà résolu ce
            // nœud (l'entrée l'a déjà joué + sa cinématique amont vient de finir).
            if (State._sceneData.Nodes != null)
            {
                for (int n = 0; n < State._sceneData.Nodes.Count; n++)
                {
                    var node = State._sceneData.Nodes[n];
                    if (node == null) continue;
                    var evt = node.FindEvent(targetId);
                    if (evt != null)
                    {
                        if (State._resolvedCardIdsThisNode.Contains(targetId)) return;
                        ExecuteScenicEvent(evt, false);
                        return;
                    }
                }
            }

            Debug.LogWarning($"[JsonStorySceneController] Cible 'fin →' introuvable : '{targetId}' (cinématique '{cine.CinematicId}').");
        }

        private void ApplyAmbienceSettings(SceneAmbienceData ambience, string fallbackSoundCueId)
        {
            string sound = ambience != null && !string.IsNullOrEmpty(ambience.SoundCueId) ? ambience.SoundCueId : fallbackSoundCueId;
            if (!string.IsNullOrEmpty(sound) && KilltimeAudioManager.Instance != null)
            {
                if (Enum.TryParse<SoundId>(sound, out var soundEnum))
                {
                    KilltimeAudioManager.Instance.PlayUI(soundEnum, 0.55f);
                }
            }

            if (ambience != null && ambience.HasAmbience)
            {
                if (ambience.ChangeMusic && KilltimeAudioManager.Instance != null)
                {
                    KilltimeAudioManager.Instance.PlayMusic(ambience.MusicMood, ambience.MusicIntensity, false);
                }

                // DÉ-HARDCODÉ : l'ancien CameraShakeIntensity est ignoré (migré en cartes 🎬
                // à plan relatif + effet HandheldShake). Son / musique / alarme conservés.

                if (ambience.TriggerAlarm)
                {
                    CombatHUD.Instance?.AddAdvancedLog("🚨 <b>ALERTE SYSTÈME :</b> Sirènes enclenchées dans le secteur.", LogCategory.Combat, "[ALARME]", Color.red);
                }
            }
        }
    }
}

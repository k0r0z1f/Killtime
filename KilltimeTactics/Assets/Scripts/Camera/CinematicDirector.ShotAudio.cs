using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Audio;
using Killtime.Story.Data;
using Killtime.Story.Scenes;
using Killtime.Tactics;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.UI;

namespace Killtime.CameraSystem
{
    public partial class CinematicDirector : MonoBehaviour
    {

        public static bool IsSwellCue(string cue)
        {
            if (string.IsNullOrWhiteSpace(cue)) return false;
            string c = cue.Trim();
            if (string.Equals(c, "Cinematic_OpeningSwell", StringComparison.OrdinalIgnoreCase)
                || string.Equals(c, "HBO", StringComparison.OrdinalIgnoreCase)
                || string.Equals(c, "Cinematic_WarpSwell", StringComparison.OrdinalIgnoreCase)
                || string.Equals(c, "Cinematic_ApproachSwell", StringComparison.OrdinalIgnoreCase)
                || string.Equals(c, "WarpSwell", StringComparison.OrdinalIgnoreCase)
                || string.Equals(c, "ApproachSwell", StringComparison.OrdinalIgnoreCase))
                return true;
            var entry = SoundBankCatalog.Find(c);
            return entry != null && string.Equals(entry.kind, "swell", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsSwellPlaying()
        {
            if (_activeSwellAudioSource == null) return false;
            try
            {
                return _activeSwellAudioSource.isPlaying || CombatHUD.IsPaused;
            }
            catch { return false; }
        }

        public static void RegisterActiveSwell(AudioSource src, string cueId)
        {
            if (src == null) return;
            _activeSwellAudioSource = src;
            _activeSwellCueId = cueId ?? "";
        }

        private void PlayShotAudio(SceneCinematicShotData shot)
        {
            if (shot == null || string.IsNullOrWhiteSpace(shot.SoundCueId) || KilltimeAudioManager.Instance == null) return;
            string cue = shot.SoundCueId.Trim();
            var bankEntry = SoundBankCatalog.Find(cue);
            string canonicalId = bankEntry != null ? bankEntry.id : cue;

            bool isSwell = IsSwellCue(canonicalId);
            if (isSwell)
            {
                if (IsSwellPlaying())
                {
                    // Swell déjà en cours (lancé par carte son ou plan précédent) : ne jamais recommencer
                    return;
                }
            }

            float targetDur = shot.SoundDuration > 0.05f ? shot.SoundDuration : 0f;

            // 1. Recherche prioritaire de la durée éditée sur une carte son liée
            if (targetDur <= 0.05f && CurrentPlayingSceneCinematic != null)
            {
                var controller = FindAnyObjectByType<JsonStorySceneController>();
                if (controller != null && controller.SceneData != null && controller.SceneData.Sounds != null)
                {
                    for (int si = 0; si < controller.SceneData.Sounds.Count; si++)
                    {
                        var snd = controller.SceneData.Sounds[si];
                        if (snd != null && string.Equals(snd.SourceCardId, CurrentPlayingSceneCinematic.CinematicId, StringComparison.OrdinalIgnoreCase)
                            && IsSwellCue(snd.SoundCueId) && snd.DurationSeconds > 0.05f)
                        {
                            targetDur = snd.DurationSeconds;
                            break;
                        }
                    }
                }
            }

            // 2. Durée configurée dans la banque sonore
            if (targetDur <= 0.05f && bankEntry != null && bankEntry.duration > 0.05f)
            {
                targetDur = bankEntry.duration;
            }

            // 3. Durée calculée de la séquence en dernier recours pour les swells sans durée
            if (targetDur <= 0.05f && isSwell && CurrentPlayingSceneCinematic != null)
            {
                targetDur = GetSequenceTotalDuration(CurrentPlayingSceneCinematic);
            }

            if (isSwell && _swellSequenceConsumed)
            {
                return;
            }

            bool isAudioActive = _activeShotAudioSource != null && (_activeShotAudioSource.isPlaying || _isAudioPausedByDirector || _activeShotAudioSource.time > 0.01f);

            if (isAudioActive && !string.IsNullOrEmpty(_activeShotSoundCueId))
            {
                if (IsSameSwellSequence(_activeShotSoundCueId, canonicalId))
                {
                    if (shot.PersistSound && _activeAudioLimitRoutine != null)
                    {
                        StopCoroutine(_activeAudioLimitRoutine);
                        _activeAudioLimitRoutine = null;
                    }
                    else if (!shot.PersistSound && targetDur > 0.05f && _activeAudioLimitRoutine == null)
                    {
                        float remaining = Mathf.Max(0.1f, targetDur - _activeShotAudioSource.time);
                        _activeAudioLimitRoutine = StartCoroutine(LimitAudioSourceDuration(_activeShotAudioSource, remaining));
                    }
                    return;
                }
                else
                {
                    _activeShotAudioSource.Stop();
                    _activeShotAudioSource = null;
                    _activeShotSoundCueId = "";
                }
            }

            try
            {
                if (bankEntry != null)
                {
                    var bankClip = ProceduralAudioFactory.GetBankClip(bankEntry, targetDur);
                    if (bankClip != null)
                    {
                        var src = KilltimeAudioManager.Instance.PlayBankUI(bankEntry.id, bankClip, bankEntry.volume);
                        _activeShotAudioSource = src;
                        _activeShotSoundCueId = canonicalId;
                        if (isSwell)
                        {
                            _swellSequenceConsumed = true;
                            RegisterActiveSwell(src, canonicalId);
                        }
                        if (src != null && targetDur > 0.05f && !shot.PersistSound)
                        {
                            if (_activeAudioLimitRoutine != null) StopCoroutine(_activeAudioLimitRoutine);
                            _activeAudioLimitRoutine = StartCoroutine(LimitAudioSourceDuration(src, targetDur));
                        }
                    }
                    return;
                }

                if (Enum.TryParse<SoundId>(cue, out var soundEnum))
                {
                    var src = KilltimeAudioManager.Instance.PlayUI(soundEnum, 0.7f);
                    _activeShotAudioSource = src;
                    _activeShotSoundCueId = canonicalId;
                    if (isSwell)
                    {
                        _swellSequenceConsumed = true;
                        RegisterActiveSwell(src, canonicalId);
                    }
                    if (src != null && targetDur > 0.05f && !shot.PersistSound)
                    {
                        if (_activeAudioLimitRoutine != null) StopCoroutine(_activeAudioLimitRoutine);
                        _activeAudioLimitRoutine = StartCoroutine(LimitAudioSourceDuration(src, targetDur));
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CinematicDirector] Échec lecture audio plan '{cue}' : {ex.Message}");
            }
        }

        private static IEnumerator LimitAudioSourceDuration(AudioSource src, float duration)
        {
            if (src == null || duration <= 0.05f) yield break;
            float playTimer = 0f;
            while (playTimer < duration && src != null && src.isPlaying)
            {
                playTimer += Time.unscaledDeltaTime;
                yield return null;
            }
            if (src != null) src.Stop();
        }

        private void PlayNextQueuedCinematic()
        {
            if (_sceneCinematicQueue.Count == 0 || IsPlayingSceneCinematic) return;
            var next = _sceneCinematicQueue.Dequeue();
            if (next.Data == null)
            {
                // Paranoïa (l'enqueue filtre déjà les requêtes vides) : ne jamais
                // avaler un callback ni caler la file.
                try { next.OnComplete?.Invoke(); } catch { /* ignore */ }
                PlayNextQueuedCinematic();
                return;
            }
            _sceneCinematicOnComplete = next.OnComplete;
            _activeSceneCinematicRoutine = StartCoroutine(SceneCinematicRoutine(next.Data));
        }
    }
}

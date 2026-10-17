using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using Killtime.Core.Combat;

namespace Killtime.Audio
{
    /// <summary>KilltimeAudioManager — Ambiances — boucle + pistes de scene (partial, move-only).</summary>
    public partial class KilltimeAudioManager : MonoBehaviour
    {
        public void StartAmbience(float volumeScale = 1f)
        {
            if (_ambienceSource == null) return;
            if (_ambienceSource.isPlaying) return;
            // Nappe procédurale 6s bouclable (vent + grave), sans clic.
            _ambienceSource.clip = ProceduralAudioFactory.GetAmbienceLoop();
            _ambienceSource.loop = true;
            _ambienceSource.volume = _ambienceVolume * volumeScale * _masterVolume;
            _ambienceSource.Play();
        }

        public void StopAmbience()
        {
            if (_ambienceSource != null && _ambienceSource.isPlaying)
            {
                _ambienceSource.Stop();
            }
        }

        public void PlaySceneAmbience(string instanceId, string cueOrPresetId, float volumeScale, float fadeInDuration, bool spatial, Vector3 worldPos, float radius)
        {
            if (string.IsNullOrWhiteSpace(instanceId)) return;
            string key = instanceId.Trim();

            AudioClip clip = null;
            var preset = AmbienceCatalog.Find(cueOrPresetId);
            string soundCue = preset != null ? preset.soundCueId : cueOrPresetId;

            var bankEntry = SoundBankCatalog.Find(soundCue);
            if (bankEntry != null)
            {
                clip = ProceduralAudioFactory.GetBankClip(bankEntry, bankEntry.duration > 0.05f ? bankEntry.duration : 6f);
            }
            if (clip == null && System.Enum.TryParse<SoundId>(soundCue, out var sEnum))
            {
                clip = ProceduralAudioFactory.GetClip(sEnum);
            }
            if (clip == null)
            {
                clip = ProceduralAudioFactory.GetAmbienceLoop();
            }

            if (!_activeSceneAmbienceSources.TryGetValue(key, out var src) || src == null)
            {
                var go = new GameObject($"[AmbienceTrack] {key}");
                go.transform.SetParent(transform, false);
                src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.loop = true;
                src.dopplerLevel = 0f;
                _activeSceneAmbienceSources[key] = src;
            }

            src.clip = clip;
            src.spatialBlend = spatial ? 1f : 0f;
            if (spatial)
            {
                src.rolloffMode = AudioRolloffMode.Linear;
                src.minDistance = Mathf.Max(1.5f, radius * 0.25f);
                src.maxDistance = Mathf.Max(radius, 4f);
                src.transform.position = worldPos;
            }

            float targetVol = (_muted ? 0f : 1f) * _masterVolume * _ambienceVolume * volumeScale;

            if (_activeSceneAmbienceFades.TryGetValue(key, out var activeRoutine) && activeRoutine != null)
            {
                StopCoroutine(activeRoutine);
            }

            if (!src.isPlaying)
            {
                src.volume = 0f;
                src.Play();
            }

            _activeSceneAmbienceFades[key] = StartCoroutine(FadeSceneAmbienceRoutine(key, src, targetVol, Mathf.Max(0.05f, fadeInDuration), false));
        }

        public void StopSceneAmbience(string instanceId, float fadeOutDuration)
        {
            if (string.IsNullOrWhiteSpace(instanceId)) return;
            string key = instanceId.Trim();
            if (!_activeSceneAmbienceSources.TryGetValue(key, out var src) || src == null) return;

            if (_activeSceneAmbienceFades.TryGetValue(key, out var activeRoutine) && activeRoutine != null)
            {
                StopCoroutine(activeRoutine);
            }

            _activeSceneAmbienceFades[key] = StartCoroutine(FadeSceneAmbienceRoutine(key, src, 0f, Mathf.Max(0.05f, fadeOutDuration), true));
        }

        public void StopAllSceneAmbiences(float fadeOutDuration = 0.5f)
        {
            var keys = new List<string>(_activeSceneAmbienceSources.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                StopSceneAmbience(keys[i], fadeOutDuration);
            }
        }

        private IEnumerator FadeSceneAmbienceRoutine(string key, AudioSource src, float targetVol, float duration, bool destroyOnComplete)
        {
            if (src == null) yield break;
            float startVol = src.volume;
            float elapsed = 0f;

            while (elapsed < duration && src != null)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                src.volume = Mathf.Lerp(startVol, targetVol, t);
                yield return null;
            }

            if (src != null)
            {
                src.volume = targetVol;
                if (destroyOnComplete)
                {
                    src.Stop();
                    _activeSceneAmbienceSources.Remove(key);
                    _activeSceneAmbienceFades.Remove(key);
                    Destroy(src.gameObject);
                }
            }
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using Killtime.Core.Combat;

namespace Killtime.Audio
{
    /// <summary>KilltimeAudioManager — Resolution — RMS, defauts, cache, cooldowns, volumes (partial, move-only).</summary>
    public partial class KilltimeAudioManager : MonoBehaviour
    {
        public float GetActiveMusicRms()
        {
            if (_activeMusic == null || !_activeMusic.isPlaying) return 0f;
            try
            {
                _activeMusic.GetOutputData(_rmsBuffer, 0);
                float sum = 0f;
                for (int i = 0; i < _rmsBuffer.Length; i++)
                {
                    sum += _rmsBuffer[i] * _rmsBuffer[i];
                }
                return Mathf.Sqrt(sum / _rmsBuffer.Length);
            }
            catch (System.Exception)
            {
                return 0f;
            }
        }

        public static SoundDefinition CreateDefaultDefinition(SoundId id)
        {
            var def = new SoundDefinition { Id = id };
            int code = (int)id;
            if (code >= 100 && code < 200) { def.Category = SoundCategory.UI; def.Volume = 0.65f; def.Cooldown = 0.03f; def.SpatialBlend = 0f; }
            else if (code >= 200 && code < 300) { def.Category = SoundCategory.SFX; def.Volume = 0.85f; def.Cooldown = 0.06f; def.SpatialBlend = 0.35f; }
            else if (code >= 300 && code < 380) { def.Category = SoundCategory.SFX; def.Volume = 1.0f; def.Cooldown = 0.01f; def.SpatialBlend = 0.35f; }
            else if (code >= 380 && code < 400) { def.Category = SoundCategory.Cinematic; def.Volume = 0.95f; def.Cooldown = 0.1f; def.SpatialBlend = 0f; }
            else { def.Category = SoundCategory.SFX; def.Volume = 0.85f; def.Cooldown = 0.04f; def.SpatialBlend = 0.35f; }

            if (id == SoundId.Attack_Impact_Crit || id == SoundId.Impact_DeepBoom || id == SoundId.Death_Instant)
                def.Volume = 1.15f;
            if (id == SoundId.UI_Hover || id == SoundId.Move_PathTick)
                def.Cooldown = 0.04f;
            return def;
        }

        private SoundDefinition Resolve(SoundId id)
        {
            var fromBank = _bank != null ? _bank.Get(id) : null;
            if (fromBank != null) return fromBank;
            return GetCachedDefault(id);
        }

        private static readonly Dictionary<SoundId, SoundDefinition> _defaultCache = new();

        private static SoundDefinition GetCachedDefault(SoundId id)
        {
            if (_defaultCache.TryGetValue(id, out var def) && def != null) return def;
            def = CreateDefaultDefinition(id);
            _defaultCache[id] = def;
            return def;
        }

        private static AudioClip PickClip(SoundDefinition def)
        {
            if (def.Clips != null && def.Clips.Count > 0)
            {
                // Pioche aléatoire parmi les overrides, ignore les slots vides.
                for (int tries = 0; tries < 3; tries++)
                {
                    var c = def.Clips[Random.Range(0, def.Clips.Count)];
                    if (c != null) return c;
                }
            }
            return ProceduralAudioFactory.GetClip(def.Id);
        }

        private bool CheckCooldown(SoundDefinition def)
        {
            if (def.Cooldown <= 0f) return true;
            float now = Time.unscaledTime;
            if (_lastPlayTime.TryGetValue(def.Id, out float last) && now - last < def.Cooldown)
                return false;
            _lastPlayTime[def.Id] = now;
            return true;
        }

        private void ConfigureAndPlay(AudioSource src, SoundDefinition def, AudioClip clip,
            float volumeScale, float pitchScale, Vector3 pos, bool is3D, SoundCategory? forceCategory = null)
        {
            var cat = forceCategory ?? def.Category;
            src.Stop();
            src.clip = clip;
            src.loop = def.Loop;
            src.priority = def.Priority;
            src.spatialBlend = is3D ? Mathf.Clamp(def.SpatialBlend, 0.20f, 0.45f) : 0f;
            if (is3D)
            {
                src.minDistance = 18f;
                src.maxDistance = Mathf.Max(def.MaxDistance, 85f);
                src.transform.position = pos;
            }

            float catVol = CategoryVolume(cat);
            float duck = cat == SoundCategory.Music ? (1f - 0.5f * _pauseDuck)
                       : cat == SoundCategory.Ambience ? (1f - 0.4f * _cinematicDuck)
                       : 1f;
            src.volume = (_muted ? 0f : 1f) * _masterVolume * catVol * def.Volume * volumeScale * Mathf.Max(0f, duck);

            float pitchJitter = Random.Range(def.PitchMin, def.PitchMax);
            float slowFactor = (cat == SoundCategory.Music || cat == SoundCategory.Ambience || cat == SoundCategory.UI || cat == SoundCategory.Cinematic) ? 1f
                : Mathf.Lerp(1f, _slowMoSfxPitch, _slowMoWeight);
            // En pause (timeScale ~0) on garde un pitch valide : AudioListener.pause gère le silence.
            src.pitch = Mathf.Clamp(pitchScale * pitchJitter * slowFactor, 0.1f, 3f);
            src.Play();
        }

        private float CategoryVolume(SoundCategory cat)
        {
            return cat switch
            {
                SoundCategory.Music => _musicVolume,
                SoundCategory.Ambience => _ambienceVolume,
                SoundCategory.SFX => _sfxVolume,
                SoundCategory.UI => _uiVolume,
                SoundCategory.Cinematic => _cinematicVolume,
                _ => 1f
            };
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace Killtime.Audio
{
    /// <summary>
    /// Usine procédurale : génère un AudioClip de substitution pour chaque SoundId / MusicMood.
    /// 100% runtime, déterministe (seed fixe), mono 22050 Hz pour rester léger sur WebGL.
    /// Quand un vrai clip est présent dans le SoundBank, il est prioritaire (mode hybride).
    /// Façade : caches, constantes, helpers partagés. Implémentations dans les partiels.
    /// </summary>
    public static partial class ProceduralAudioFactory
    {
        public const int SampleRate = 22050;
        private const float MusicDur = 16.0f;

        private static readonly Dictionary<SoundId, AudioClip> _sfxCache = new();
        private static readonly Dictionary<int, AudioClip> _musicCache = new();
        private static readonly Dictionary<MusicMood, AudioClip> _stingerCache = new();
        private static AudioClip _ambienceCache;
        // Cache génératif : fenêtré (3 dernières variations) pour ne pas saturer
        // la mémoire quand generation s'incrémente à l'infini.
        private static readonly Dictionary<string, AudioClip> _generativeCache = new();
        private static readonly System.Collections.Generic.Queue<string> _generativeLru = new();

        public static AudioClip GetClip(SoundId id)
        {
            if (id == SoundId.None) return null;
            if (_sfxCache.TryGetValue(id, out var cached) && IsClipUsable(cached)) return cached;
            var clip = Generate(id);
            if (clip != null) _sfxCache[id] = clip;
            return clip;
        }

        private static readonly Dictionary<string, AudioClip> _bankCache = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Clip d'une entrée de banque (recette JSON) : kind "swell" = synthèse
        /// via le moteur générique (avec durée optionnellement surchargée), kind "ref" = alias vers un SoundId existant.
        /// </summary>
        public static AudioClip GetBankClip(SoundBankEntry entry, float durationOverride = 0f)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.id)) return null;
            float dur = durationOverride > 0.1f ? durationOverride : entry.duration;
            string key = entry.id.Trim() + (durationOverride > 0.1f ? "_" + dur.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "s" : "");
            if (_bankCache.TryGetValue(key, out var cached) && IsClipUsable(cached)) return cached;
            AudioClip clip = null;
            try
            {
                if (string.Equals(entry.kind, "ref", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(entry.refSoundId)
                    && Enum.TryParse<SoundId>(entry.refSoundId.Trim(), out var refId))
                {
                    clip = GetClip(refId);
                }
                else
                {
                    clip = PlanetSwell("Bank_" + key, dur, entry.volume, entry.seed,
                        entry.startHot, entry.bloom, entry.releaseEnd,
                        entry.shepardOctaves, entry.shepardGain, entry.brass, entry.brassGain);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ProceduralAudioFactory] Recette banque '{key}' illisible : {ex.Message}");
            }
            if (clip != null) _bankCache[key] = clip;
            return clip;
        }

        /// <summary>Un clip procédural n'est réutilisable que s'il est vivant,
        /// chargé et non vide. Sinon on régénère (anti "piste muette").</summary>
        private static bool IsClipUsable(AudioClip clip)
        {
            if (clip == null) return false;
            try
            {
                if (clip.samples <= 0) return false;
                if (clip.loadState != AudioDataLoadState.Loaded) return false;
            }
            catch (System.Exception)
            {
                return false; // objet Unity détruit (fake-null) ou illisible
            }
            return true;
        }

        public static void ClearCache()
        {
            _sfxCache.Clear();
            _bankCache.Clear();
            _musicCache.Clear();
            _stingerCache.Clear();
            _generativeCache.Clear();
            _generativeLru.Clear();
            _generativeStrikeCache.Clear();
            lock (_genTaskLock)
            {
                _generativePendingTasks.Clear();
                _generativeTaskLru.Clear();
            }
            _ambienceCache = null;
        }

        private static float MidiToFreq(int midi) => 440f * Mathf.Pow(2f, (midi - 69) / 12f);

        private static void LoopCrossfade(float[] s, int fade)
        {
            int n = s.Length;
            fade = Math.Min(fade, n / 4);
            for (int i = 0; i < fade; i++)
            {
                float t = (float)i / fade;
                int j = n - fade + i;
                if (j >= 0 && j < n)
                {
                    float mixed = s[i] * t + s[j] * (1f - t);
                    s[i] = mixed;
                    s[j] = mixed;
                }
            }
        }

        private static void Normalize(float[] s, float peak)
        {
            float max = 0.0001f;
            for (int i = 0; i < s.Length; i++) max = Math.Max(max, Math.Abs(s[i]));
            float g = peak / max;
            if (g > 4f) g = 4f;
            for (int i = 0; i < s.Length; i++) s[i] *= g;
        }

        private static AudioClip MakeClip(string name, float[] samples)
        {
            var clip = AudioClip.Create("PROC_" + name, samples.Length, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }
}


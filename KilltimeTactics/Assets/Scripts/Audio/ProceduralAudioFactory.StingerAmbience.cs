using System;
using System.Collections.Generic;
using UnityEngine;

namespace Killtime.Audio
{
    /// <summary>Partiel stingers + nappe d'ambiance.</summary>
    public static partial class ProceduralAudioFactory
    {
        public static AudioClip GetStinger(MusicMood mood)
        {
            if (mood == MusicMood.None) return null;
            if (_stingerCache.TryGetValue(mood, out var cached) && IsClipUsable(cached)) return cached;
            var clip = GenerateStinger(mood);
            if (clip != null) _stingerCache[mood] = clip;
            return clip;
        }

        public static AudioClip GetAmbienceLoop()
        {
            if (IsClipUsable(_ambienceCache)) return _ambienceCache;
            _ambienceCache = GenerateAmbience();
            return _ambienceCache;
        }

        private static AudioClip GenerateStinger(MusicMood mood)
        {
            float dur = (mood == MusicMood.Victory || mood == MusicMood.CombatBoss) ? 1.8f : 1.5f;
            int n = (int)(SampleRate * dur);
            float[] s = new float[n];
            switch (mood)
            {
                case MusicMood.Victory:
                    StingerNotes(s, new[] { 72, 76, 79, 84 }, 0.22f, 0.5f);
                    break;
                case MusicMood.Defeat:
                    StingerNotes(s, new[] { 57, 53, 52 }, 0.4f, 0.55f);
                    AddSubRumble(s, 41.2f, 0.15f);
                    break;
                case MusicMood.Combat:
                    StingerHit(s, 0.9f);
                    break;
                case MusicMood.CombatBoss:
                    StingerNotes(s, new[] { 50, 53, 57, 62, 69 }, 0.10f, 0.6f);
                    AddSubRumble(s, 36.7f, 0.22f);
                    StingerHit(s, 1.0f);
                    break;
                case MusicMood.Tension:
                    for (int i = 0; i < n; i++)
                    {
                        float t = (float)i / n;
                        float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t * 1.05f));
                        s[i] += (Mathf.Sin(2f * Mathf.PI * 466f * i / SampleRate)
                               + Mathf.Sin(2f * Mathf.PI * 494f * i / SampleRate)) * 0.28f * env;
                        s[i] += Mathf.Sin(2f * Mathf.PI * 55f * i / SampleRate) * 0.25f * env;
                    }
                    break;
                default:
                    StingerNotes(s, new[] { 69, 76 }, 0.5f, 0.4f);
                    break;
            }
            Normalize(s, 0.85f);
            return MakeClip("Stinger_" + mood, s);
        }

        private static void StingerNotes(float[] s, int[] midis, float stepSec, float vol)
        {
            for (int k = 0; k < midis.Length; k++)
            {
                int start = (int)(k * stepSec * SampleRate);
                float f = MidiToFreq(midis[k]);
                for (int i = start; i < s.Length; i++)
                {
                    float t = (i - start) / (float)SampleRate;
                    float env = Mathf.Exp(-3.2f * t);
                    s[i] += (Mathf.Sin(2f * Mathf.PI * f * t)
                           + 0.35f * Mathf.Sin(2f * Mathf.PI * f * 2f * t)) * env * vol * 0.5f;
                }
            }
        }

        private static void StingerHit(float[] s, float vol)
        {
            var rng = new System.Random(777);
            float lp = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                float t = i / (float)SampleRate;
                float env = Mathf.Exp(-6f * t);
                float body = Mathf.Sin(2f * Mathf.PI * 70f * Mathf.Exp(-2.5f * t) * t);
                float noise = (float)(rng.NextDouble() * 2 - 1);
                lp += 0.3f * (noise - lp);
                s[i] += (body * 0.9f + lp * Mathf.Exp(-20f * t) * 0.9f) * env * vol * 0.8f;
                s[i] += 0.25f * Mathf.Sin(2f * Mathf.PI * 1244f * t) * Mathf.Exp(-12f * t) * vol;
            }
        }

        private static AudioClip GenerateAmbience()
        {
            const float dur = 6f;
            int n = (int)(SampleRate * dur);
            float[] s = new float[n];
            var rng = new System.Random(20250);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float lfo = 0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 0.09f * t);
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                // Lit sonore très sombre : filtre presque fermé (aucun sifflement)
                // et niveau bas — c'est un fond, pas un effet.
                lp += (0.02f + 0.04f * lfo) * (noise - lp);
                s[i] += lp * 0.25f;
                s[i] += Mathf.Sin(2f * Mathf.PI * 55f * t) * 0.05f;
                s[i] += Mathf.Sin(2f * Mathf.PI * 660f * t + Mathf.Sin(t * 1.7f)) * 0.005f;
            }
            LoopCrossfade(s, SampleRate);
            Normalize(s, 0.15f);
            return MakeClip("Ambience_Wind", s);
        }
    }
}


using System;
using System.Collections.Generic;
using UnityEngine;

namespace Killtime.Audio
{
    /// <summary>Partiel moteur musical adaptatif (boucles strictes + progressions).</summary>
    public static partial class ProceduralAudioFactory
    {
        public static int GetTrackCount(MusicMood mood)
        {
            return mood switch
            {
                MusicMood.Combat => 3,
                _ => 1
            };
        }

        public static AudioClip GetMusicLoop(MusicMood mood) => GetMusicLoop(mood, 0, 1);

        public static AudioClip GetMusicLoop(MusicMood mood, MusicIntensity level)
            => GetMusicLoop(mood, 0, (int)level);

        public static AudioClip GetMusicLoop(MusicMood mood, int level) => GetMusicLoop(mood, 0, level);

        public static AudioClip GetMusicLoop(MusicMood mood, int trackIndex, int level)
        {
            if (mood == MusicMood.None) return null;
            level = Mathf.Clamp(level, 0, 2);
            int trackCount = GetTrackCount(mood);
            trackIndex = Mathf.Clamp(trackIndex, 0, Mathf.Max(0, trackCount - 1));

            int stemMask = KilltimeAudioManager.Instance != null ? KilltimeAudioManager.Instance.StemOverridesMask : 0;
            int key = ((int)mood * 10000) + (trackIndex * 1000) + (level * 100) + (stemMask & 0xFF);
            // Un clip en cache peut devenir vide/déchargé côté Unity (vu en console :
            // 2e lecture de Combat T0 rendue avec 0 spl). On ne rejoue jamais un
            // clip vide : on régénère et on écrase le cache.
            if (_musicCache.TryGetValue(key, out var cached) && IsClipUsable(cached)) return cached;
            var clip = GenerateMusic(mood, trackIndex, level);
            if (clip != null) _musicCache[key] = clip;
            return clip;
        }

        // =====================================================================
        // MOTEUR MUSICAL ADAPTATIF (16.0s STRICTES 4/4)
        // =====================================================================
        private static AudioClip GenerateMusic(MusicMood mood, int trackIndex, int level)
        {
            int n = (int)(SampleRate * MusicDur);
            float[] s = new float[n];
            float[] sidechain = new float[n];
            var rng = new System.Random((int)mood * 9173 + trackIndex * 1451 + level * 239 + 71);

            GetProgression(mood, trackIndex, out int[][] chords, out int[] roots, out float bpm,
                out float padVol, out float drumAmount, out float arpRate, out float arpVol);

            int stemMask = KilltimeAudioManager.Instance != null ? KilltimeAudioManager.Instance.StemOverridesMask : 0;
            StemOverrideState drumOverride = (StemOverrideState)((stemMask >> ((int)ProceduralStemType.Drums * 2)) & 0x3);
            StemOverrideState bassOverride = (StemOverrideState)((stemMask >> ((int)ProceduralStemType.Bass * 2)) & 0x3);
            StemOverrideState padOverride = (StemOverrideState)((stemMask >> ((int)ProceduralStemType.Pad * 2)) & 0x3);
            StemOverrideState arpOverride = (StemOverrideState)((stemMask >> ((int)ProceduralStemType.Arp * 2)) & 0x3);
            StemOverrideState leadOverride = (StemOverrideState)((stemMask >> ((int)ProceduralStemType.Lead * 2)) & 0x3);
            StemOverrideState choirOverride = (StemOverrideState)((stemMask >> ((int)ProceduralStemType.Choir * 2)) & 0x3);
            StemOverrideState staccatoOverride = (StemOverrideState)((stemMask >> ((int)ProceduralStemType.Staccato * 2)) & 0x3);
            StemOverrideState subAirOverride = (StemOverrideState)((stemMask >> ((int)ProceduralStemType.SubAir * 2)) & 0x3);

            float density = level / 2f;
            float drumVol = drumAmount * (0.35f + 0.65f * density);

            bool playDrums = (drumOverride == StemOverrideState.ForceActive) || (drumOverride == StemOverrideState.Auto && drumVol > 0.04f && level >= 1);
            if (playDrums && drumOverride != StemOverrideState.Muted)
            {
                AddProductionDrums(s, sidechain, rng, bpm, Mathf.Max(0.5f, drumVol), density, level);
            }

            if (padOverride != StemOverrideState.Muted)
            {
                AddMasterChordProgression(s, sidechain, chords, bpm, padVol * (1f + 0.25f * level), level);
            }

            bool playBass = (bassOverride == StemOverrideState.ForceActive) || (bassOverride == StemOverrideState.Auto && level >= 1);
            if (playBass && bassOverride != StemOverrideState.Muted)
            {
                AddMasterBassLine(s, sidechain, roots, chords, bpm, 0.22f + 0.08f * level, level);
            }

            bool playArp = (arpOverride == StemOverrideState.ForceActive) || (arpOverride == StemOverrideState.Auto && level >= 1 && arpVol > 0.01f);
            if (playArp && arpOverride != StemOverrideState.Muted)
            {
                AddMasterArpProgression(s, chords, bpm, arpRate, arpVol * (0.7f + 0.45f * level), level);
            }

            bool playSubAir = subAirOverride != StemOverrideState.Muted;
            bool playStaccato = (staccatoOverride == StemOverrideState.ForceActive) || (staccatoOverride == StemOverrideState.Auto);
            if (staccatoOverride == StemOverrideState.Muted) playStaccato = false;

            bool playChoir = (choirOverride == StemOverrideState.ForceActive) || (choirOverride == StemOverrideState.Auto);
            if (choirOverride == StemOverrideState.Muted) playChoir = false;

            bool playLead = (leadOverride == StemOverrideState.ForceActive) || (leadOverride == StemOverrideState.Auto && level >= 1);
            if (leadOverride == StemOverrideState.Muted) playLead = false;

            switch (mood)
            {
                case MusicMood.Explore:
                    if (playSubAir)
                    {
                        AddSubRumble(s, 36.7f, 0.06f);
                        AddChronoShimmer(s, 587.33f, 0.035f);
                    }
                    break;

                case MusicMood.Combat:
                    if (trackIndex == 0)
                    {
                        if (playSubAir) AddSubRumble(s, 65.4f, 0.12f + 0.04f * level);
                        if (playStaccato) AddPulseStaccatoStrings(s, chords, bpm, 0.20f + 0.06f * level);
                        AddAnvilBeat(s, bpm, 0.14f + 0.04f * level);
                        if (level >= 1) AddIndustrialClang(s, bpm, 0.14f + 0.04f * level);
                        if (level >= 2 && playStaccato) AddCyberDjentChug(s, sidechain, rng, roots, bpm, 0.18f, level);
                    }
                    else if (trackIndex == 1)
                    {
                        if (playSubAir) AddSubRumble(s, 55.0f, 0.10f + 0.04f * level);
                        if (playStaccato) AddPulseStaccatoStrings(s, chords, bpm, 0.14f + 0.05f * level);
                        if (level >= 1) AddAnvilBeat(s, bpm, 0.11f + 0.04f * level);
                    }
                    else
                    {
                        if (playSubAir) AddSubRumble(s, 48.0f, 0.12f + 0.05f * level);
                        if (playStaccato) AddCyberDjentChug(s, sidechain, rng, roots, bpm, 0.26f + 0.08f * level, level);
                        AddNeuroCyberBass(s, roots, bpm, 0.16f + 0.06f * level);
                        if (level >= 1) AddIndustrialClang(s, bpm, 0.15f);
                    }
                    break;

                case MusicMood.CombatBoss:
                    if (playSubAir) AddSubRumble(s, 36.7f, 0.10f + 0.06f * level);
                    AddHeavyWarPercussion(s, rng, bpm, 0.18f + 0.08f * level, level);
                    if (playChoir) AddArcanotechChoir(s, chords, bpm, 0.14f + 0.06f * level);
                    if (playLead) AddCyberneticAcidLead(s, roots, bpm, 0.12f + 0.05f * level, level);
                    if (level >= 2) AddIndustrialClang(s, bpm, 0.16f);
                    break;

                case MusicMood.Tension:
                    if (level >= 1) AddBiologicalHeartbeat(s, 60f, 0.26f + 0.08f * level);
                    AddRiser(s, 0.08f + 0.06f * level);
                    if (playSubAir) AddSubRumble(s, 34.6f, 0.10f);
                    break;

                case MusicMood.Victory:
                    if (playSubAir)
                    {
                        AddSubRumble(s, 32.7f, 0.08f);
                        AddChronoShimmer(s, 1046.5f, 0.05f * level);
                    }
                    break;

                case MusicMood.Defeat:
                    if (playSubAir) AddSubRumble(s, 27.5f, 0.12f);
                    AddDyingHarmonicFade(s, 0.15f);
                    break;
            }

            LoopCrossfade(s, SampleRate / 2);
            ApplyAnalogMastering(s, 0.58f);
            return MakeClip($"Music_{mood}_T{trackIndex}_L{level}", s);
        }

        private static void GetProgression(MusicMood mood, int trackIndex, out int[][] chords, out int[] roots,
            out float bpm, out float padVol, out float drumAmount, out float arpRate, out float arpVol)
        {
            switch (mood)
            {
                case MusicMood.Combat:
                    if (trackIndex == 0)
                    {
                        chords = new[]
                        {
                            new[] { 40, 47, 52, 55, 59 },
                            new[] { 43, 47, 50, 55, 59 },
                            new[] { 36, 48, 52, 55, 60 },
                            new[] { 38, 50, 53, 57, 62 },
                            new[] { 33, 45, 52, 57, 60 },
                            new[] { 36, 48, 55, 59, 64 },
                            new[] { 41, 48, 53, 57, 60 },
                            new[] { 35, 47, 53, 59, 62 },
                        };
                        roots = new[] { 40, 43, 36, 38, 33, 36, 41, 35 };
                        bpm = 120f; padVol = 0.26f; drumAmount = 1.25f; arpRate = 4f; arpVol = 0.20f;
                    }
                    else if (trackIndex == 1)
                    {
                        chords = new[]
                        {
                            new[] { 45, 52, 57, 60, 64 },
                            new[] { 41, 48, 53, 57, 64 },
                            new[] { 48, 55, 59, 62, 64 },
                            new[] { 50, 57, 60, 64, 66 },
                            new[] { 41, 48, 52, 57, 60 },
                            new[] { 43, 50, 55, 59, 62 },
                            new[] { 50, 53, 57, 60, 64 },
                            new[] { 47, 53, 56, 62, 65 },
                        };
                        roots = new[] { 45, 41, 48, 50, 41, 43, 50, 47 };
                        bpm = 120f; padVol = 0.22f; drumAmount = 1.15f; arpRate = 4f; arpVol = 0.15f;
                    }
                    else
                    {
                        chords = new[]
                        {
                            new[] { 26, 38, 45, 50, 53, 57 },
                            new[] { 29, 41, 48, 53, 56, 60 },
                            new[] { 32, 44, 50, 56, 59, 62 },
                            new[] { 31, 43, 50, 55, 58, 62 },
                            new[] { 26, 38, 45, 50, 53, 57 },
                            new[] { 27, 39, 46, 51, 54, 58 },
                            new[] { 24, 36, 43, 48, 51, 55 },
                            new[] { 25, 37, 43, 49, 52, 55 },
                        };
                        roots = new[] { 26, 29, 32, 31, 26, 27, 24, 25 };
                        bpm = 120f; padVol = 0.14f; drumAmount = 1.25f; arpRate = 8f; arpVol = 0.12f;
                    }
                    break;

                case MusicMood.CombatBoss:
                    chords = new[]
                    {
                        new[] { 38, 50, 53, 57, 62, 64 },
                        new[] { 34, 46, 53, 58, 62, 64 },
                        new[] { 31, 43, 50, 55, 58, 62 },
                        new[] { 33, 45, 52, 57, 61, 64 },
                        new[] { 41, 48, 53, 57, 60, 65 },
                        new[] { 39, 51, 55, 58, 62, 65 },
                        new[] { 40, 47, 52, 55, 58, 64 },
                        new[] { 33, 45, 49, 52, 57, 61 },
                    };
                    roots = new[] { 38, 34, 31, 33, 41, 39, 40, 33 };
                    bpm = 120f; padVol = 0.18f; drumAmount = 1.15f; arpRate = 6f; arpVol = 0.13f;
                    break;

                case MusicMood.Tension:
                    chords = new[]
                    {
                        new[] { 39, 51, 54, 58, 62 },
                        new[] { 38, 50, 53, 56, 62 },
                        new[] { 34, 46, 51, 54, 58 },
                        new[] { 37, 49, 53, 55, 61 },
                    };
                    roots = new[] { 39, 38, 34, 37 };
                    bpm = 60f; padVol = 0.18f; drumAmount = 0.45f; arpRate = 1.5f; arpVol = 0.08f;
                    break;

                case MusicMood.Victory:
                    chords = new[]
                    {
                        new[] { 36, 48, 52, 55, 59, 62 },
                        new[] { 36, 48, 55, 59, 64 },
                        new[] { 38, 50, 54, 57, 62, 66 },
                        new[] { 38, 50, 57, 62, 67 },
                        new[] { 40, 52, 55, 59, 62, 66 },
                        new[] { 40, 47, 55, 59, 64 },
                        new[] { 35, 47, 55, 59, 62 },
                        new[] { 36, 48, 60, 64, 67 },
                    };
                    roots = new[] { 36, 36, 38, 38, 40, 40, 35, 36 };
                    bpm = 120f; padVol = 0.17f; drumAmount = 0.85f; arpRate = 4f; arpVol = 0.12f;
                    break;

                case MusicMood.Defeat:
                    chords = new[]
                    {
                        new[] { 33, 45, 48, 52, 55, 59 },
                        new[] { 29, 41, 48, 54, 57 },
                        new[] { 29, 41, 45, 50, 53, 57 },
                        new[] { 28, 40, 46, 52, 53, 56 },
                    };
                    roots = new[] { 33, 29, 29, 28 };
                    bpm = 60f; padVol = 0.19f; drumAmount = 0.20f; arpRate = 1f; arpVol = 0.08f;
                    break;

                default:
                    chords = new[]
                    {
                        new[] { 38, 50, 53, 57, 60, 64 },
                        new[] { 34, 46, 53, 57, 60, 64 },
                        new[] { 31, 43, 50, 55, 57, 62 },
                        new[] { 40, 48, 52, 55, 62 },
                    };
                    roots = new[] { 38, 34, 31, 40 };
                    bpm = 60f; padVol = 0.18f; drumAmount = 0.25f; arpRate = 2f; arpVol = 0.09f;
                    break;
            }
        }
    }
}


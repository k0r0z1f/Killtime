using System;
using System.Collections.Generic;
using UnityEngine;

namespace Killtime.Audio
{
    /// <summary>
    /// Usine procédurale : génère un AudioClip de substitution pour chaque SoundId / MusicMood.
    /// 100% runtime, déterministe (seed fixe), mono 22050 Hz pour rester léger sur WebGL.
    /// Quand un vrai clip est présent dans le SoundBank, il est prioritaire (mode hybride).
    /// </summary>
    public static class ProceduralAudioFactory
    {
        public const int SampleRate = 22050;
        private const float MusicDur = 16.0f;

        private static readonly Dictionary<SoundId, AudioClip> _sfxCache = new();
        private static readonly Dictionary<int, AudioClip> _musicCache = new();
        private static readonly Dictionary<MusicMood, AudioClip> _stingerCache = new();
        private static AudioClip _ambienceCache;

        public static AudioClip GetClip(SoundId id)
        {
            if (id == SoundId.None) return null;
            if (_sfxCache.TryGetValue(id, out var cached) && cached != null) return cached;
            var clip = Generate(id);
            if (clip != null) _sfxCache[id] = clip;
            return clip;
        }

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

            int key = ((int)mood * 100) + (trackIndex * 10) + level;
            if (_musicCache.TryGetValue(key, out var cached) && cached != null) return cached;
            var clip = GenerateMusic(mood, trackIndex, level);
            if (clip != null) _musicCache[key] = clip;
            return clip;
        }

        public static AudioClip GetStinger(MusicMood mood)
        {
            if (mood == MusicMood.None) return null;
            if (_stingerCache.TryGetValue(mood, out var cached) && cached != null) return cached;
            var clip = GenerateStinger(mood);
            if (clip != null) _stingerCache[mood] = clip;
            return clip;
        }

        public static AudioClip GetAmbienceLoop()
        {
            if (_ambienceCache != null) return _ambienceCache;
            _ambienceCache = GenerateAmbience();
            return _ambienceCache;
        }

        public static void ClearCache()
        {
            _sfxCache.Clear();
            _musicCache.Clear();
            _stingerCache.Clear();
            _ambienceCache = null;
        }

        // =====================================================================
        // SFX TACTIQUES
        // =====================================================================
        private static AudioClip Generate(SoundId id)
        {
            switch (id)
            {
                case SoundId.UI_Click: return Blip("UI_Click", 880f, 660f, 0.07f, 0.5f);
                case SoundId.UI_Hover: return Blip("UI_Hover", 1320f, 1560f, 0.035f, 0.22f);
                case SoundId.UI_Open: return Sweep("UI_Open", 420f, 980f, 0.12f, 0.5f);
                case SoundId.UI_Close: return Sweep("UI_Close", 980f, 380f, 0.12f, 0.5f);
                case SoundId.UI_Denied: return Buzz("UI_Denied", 160f, 0.18f, 0.55f);
                case SoundId.UI_Toggle: return Blip("UI_Toggle", 520f, 780f, 0.08f, 0.45f);
                case SoundId.UI_Filter: return Blip("UI_Filter", 700f, 1050f, 0.06f, 0.4f);

                case SoundId.VATS_Open: return Sweep("VATS_Open", 300f, 1200f, 0.22f, 0.5f, true);
                case SoundId.VATS_Close: return Sweep("VATS_Close", 1200f, 280f, 0.2f, 0.5f, true);
                case SoundId.VATS_TargetChange: return Blip("VATS_Tick", 1050f, 1400f, 0.05f, 0.4f);
                case SoundId.VATS_Lock: return LockSound();
                case SoundId.VATS_Fire: return ShotSound("VATS_Fire", 0.28f, 0.9f);

                case SoundId.Move_Footstep: return Thud("Footstep", 90f, 0.11f, 0.5f, 1234);
                case SoundId.Move_Dash: return NoiseWhoosh("Dash", 0.22f, 0.5f);
                case SoundId.Move_Denied: return Buzz("Move_Denied", 130f, 0.15f, 0.5f);
                case SoundId.Move_PathTick: return Blip("PathTick", 1500f, 1700f, 0.03f, 0.18f);
                case SoundId.Turn_Start: return Chime("Turn_Start", new[] { 523f, 784f }, 0.3f);
                case SoundId.Turn_End: return Chime("Turn_End", new[] { 784f, 523f }, 0.25f);
                case SoundId.Round_Start: return GongLike("Round_Start");

                case SoundId.Attack_Whoosh: return NoiseWhoosh("Whoosh", 0.25f, 0.65f);
                case SoundId.Attack_Impact_Hit: return Punch("Hit", false);
                case SoundId.Attack_Impact_Crit: return Punch("Crit", true);
                case SoundId.Attack_Miss: return NoiseWhoosh("Miss", 0.18f, 0.35f, true);
                case SoundId.Defense_Parry: return Metallic("Parry", 2093f, 1567f, 0.22f);
                case SoundId.Defense_Dodge: return NoiseWhoosh("Dodge", 0.14f, 0.4f, true);
                case SoundId.Defense_Block: return Thud("Block", 180f, 0.16f, 0.7f, 777);
                case SoundId.Armor_Absorb: return Metallic("Armor", 820f, 587f, 0.2f, 0.5f);
                case SoundId.Trauma_Shock: return ShockSound();
                case SoundId.Status_Expired: return Sweep("StatusOff", 900f, 420f, 0.18f, 0.4f);

                case SoundId.Hurt_Light: return Hurt(220f, 0.16f, 0.6f);
                case SoundId.Hurt_Heavy: return Hurt(140f, 0.3f, 0.85f);
                case SoundId.KO_Fall: return FallSound();
                case SoundId.Death_Instant: return DeathSound();
                case SoundId.Miracle_Saved: return Chime("Miracle", new[] { 659f, 880f, 1318f }, 0.6f);
                case SoundId.LastBreath: return Sweep("LastBreath", 500f, 180f, 0.7f, 0.6f);
                case SoundId.Heal: return Chime("Heal", new[] { 440f, 554f, 659f, 880f }, 0.5f);
                case SoundId.Breath_Emergency: return BreathSound();

                case SoundId.Dice_Roll: return DiceSound();
                case SoundId.PA_Consume: return Blip("PA-", 600f, 420f, 0.07f, 0.4f);
                case SoundId.PA_Refill: return Blip("PA+", 420f, 840f, 0.1f, 0.45f);
                case SoundId.Rewind_Time: return RewindSound();
                case SoundId.Snapshot_Tick: return Blip("Snap", 2000f, 2400f, 0.04f, 0.25f);

                case SoundId.Cinematic_WhooshIn: return NoiseWhoosh("CineIn", 0.6f, 0.7f);
                case SoundId.Cinematic_WhooshOut: return NoiseWhoosh("CineOut", 0.5f, 0.6f, true);
                case SoundId.SlowMo_Enter: return Sweep("SlowIn", 800f, 120f, 0.5f, 0.65f);
                case SoundId.SlowMo_Exit: return Sweep("SlowOut", 120f, 900f, 0.35f, 0.6f);
                case SoundId.Impact_DeepBoom: return BoomSound();

                case SoundId.Spawn_Deploy: return Sweep("Spawn", 200f, 700f, 0.3f, 0.55f, true);
                case SoundId.Arena_Reset: return Sweep("Reset", 700f, 200f, 0.35f, 0.5f, true);
                case SoundId.Victory_Stinger: return Chime("Victory", new[] { 523f, 659f, 784f, 1046f }, 0.9f);
                case SoundId.Defeat_Stinger: return Chime("Defeat", new[] { 392f, 311f, 233f, 155f }, 1.1f);

                case SoundId.Spell_Charge: return Sweep("Charge", 150f, 900f, 0.6f, 0.5f, true);
                case SoundId.Spell_Cast: return SpellCast();
                case SoundId.Spell_Fizzle: return NoiseBurst("Fizzle", 0.3f, 0.45f);
                case SoundId.Psychic_Whisper: return WhisperSound();

                default: return Blip(id.ToString(), 440f, 660f, 0.1f, 0.4f);
            }
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

            float density = level / 2f;
            float drumVol = drumAmount * (0.35f + 0.65f * density);

            if (drumVol > 0.04f && level >= 1)
            {
                AddProductionDrums(s, sidechain, rng, bpm, drumVol, density, level);
            }

            AddMasterChordProgression(s, sidechain, chords, bpm, padVol * (1f + 0.25f * level), level);

            if (level >= 1)
            {
                AddMasterBassLine(s, sidechain, roots, chords, bpm, 0.22f + 0.08f * level, level);
            }

            if (level >= 1 && arpVol > 0.01f)
            {
                AddMasterArpProgression(s, chords, bpm, arpRate, arpVol * (0.7f + 0.45f * level), level);
            }

            switch (mood)
            {
                case MusicMood.Explore:
                    AddSubRumble(s, 36.7f, 0.06f);
                    AddChronoShimmer(s, 587.33f, 0.035f);
                    break;

                case MusicMood.Combat:
                    if (trackIndex == 0)
                    {
                        AddSubRumble(s, 41.2f, 0.08f + 0.05f * level);
                        if (level >= 2) AddIndustrialClang(s, bpm, 0.12f);
                    }
                    else if (trackIndex == 1)
                    {
                        AddSubRumble(s, 55.0f, 0.09f + 0.04f * level);
                        AddPulseStaccatoStrings(s, chords, bpm, 0.13f + 0.05f * level);
                        if (level >= 1) AddAnvilBeat(s, bpm, 0.11f + 0.04f * level);
                    }
                    else
                    {
                        AddSubRumble(s, 36.7f, 0.12f + 0.05f * level);
                        AddCyberDjentChug(s, sidechain, rng, roots, bpm, 0.26f + 0.08f * level, level);
                        AddNeuroCyberBass(s, roots, bpm, 0.16f + 0.06f * level);
                        if (level >= 1) AddIndustrialClang(s, bpm, 0.15f);
                    }
                    break;

                case MusicMood.CombatBoss:
                    AddSubRumble(s, 36.7f, 0.10f + 0.06f * level);
                    AddHeavyWarPercussion(s, rng, bpm, 0.18f + 0.08f * level, level);
                    AddArcanotechChoir(s, chords, bpm, 0.14f + 0.06f * level);
                    if (level >= 1) AddCyberneticAcidLead(s, roots, bpm, 0.12f + 0.05f * level, level);
                    if (level >= 2) AddIndustrialClang(s, bpm, 0.16f);
                    break;

                case MusicMood.Tension:
                    if (level >= 1) AddBiologicalHeartbeat(s, 60f, 0.26f + 0.08f * level);
                    AddRiser(s, 0.08f + 0.06f * level);
                    AddSubRumble(s, 34.6f, 0.10f);
                    break;

                case MusicMood.Victory:
                    AddSubRumble(s, 32.7f, 0.08f);
                    AddChronoShimmer(s, 1046.5f, 0.05f * level);
                    break;

                case MusicMood.Defeat:
                    AddSubRumble(s, 27.5f, 0.12f);
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
                            new[] { 40, 47, 54, 55, 59 },
                            new[] { 40, 47, 52, 55, 59 },
                            new[] { 36, 48, 54, 55, 59 },
                            new[] { 36, 47, 52, 55, 60 },
                            new[] { 33, 45, 52, 55, 59 },
                            new[] { 33, 48, 52, 57, 60 },
                            new[] { 41, 47, 53, 57, 60 },
                            new[] { 35, 47, 53, 56, 59 },
                        };
                        roots = new[] { 40, 40, 36, 36, 33, 33, 41, 35 };
                        bpm = 120f; padVol = 0.16f; drumAmount = 1.0f; arpRate = 4f; arpVol = 0.11f;
                    }
                    else if (trackIndex == 1)
                    {
                        chords = new[]
                        {
                            new[] { 33, 45, 52, 59, 60, 64 },
                            new[] { 29, 41, 48, 53, 59, 64 },
                            new[] { 36, 48, 55, 59, 62, 64 },
                            new[] { 38, 50, 57, 60, 64, 66 },
                            new[] { 29, 41, 48, 52, 57, 60 },
                            new[] { 31, 43, 50, 55, 59, 62 },
                            new[] { 38, 50, 53, 57, 60, 64 },
                            new[] { 40, 47, 53, 56, 62, 65 },
                        };
                        roots = new[] { 33, 29, 36, 38, 29, 31, 38, 40 };
                        bpm = 120f; padVol = 0.17f; drumAmount = 1.05f; arpRate = 4f; arpVol = 0.12f;
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
                lp += (0.04f + 0.22f * lfo) * (noise - lp);
                s[i] += lp * 0.55f;
                s[i] += Mathf.Sin(2f * Mathf.PI * 55f * t) * 0.06f;
                s[i] += Mathf.Sin(2f * Mathf.PI * 660f * t + Mathf.Sin(t * 1.7f)) * 0.012f;
            }
            LoopCrossfade(s, SampleRate);
            Normalize(s, 0.4f);
            return MakeClip("Ambience_Wind", s);
        }

        // =====================================================================
        // MOTEUR DE PRODUCTION MUSICALE AVANCÉE
        // =====================================================================
        private static void AddProductionDrums(float[] s, float[] sidechain, System.Random rng,
            float bpm, float vol, float density01, int level)
        {
            float beatSec = 60f / bpm;
            float sixteenth = beatSec / 4f;
            int totalBeats = (int)(MusicDur / beatSec);

            for (int b = 0; b < totalBeats; b++)
            {
                bool isKick = (b % 4 == 0) || (level >= 2 && (b % 4 == 2 || (b % 8 == 7)));
                if (!isKick) continue;

                int start = (int)(b * beatSec * SampleRate);
                int kickSamples = Math.Min((int)(0.28f * SampleRate), s.Length - start);
                float phase = 0f;

                for (int i = 0; i < kickSamples; i++)
                {
                    float t = i / (float)SampleRate;
                    float f = 42f + 118f * Mathf.Exp(-28f * t);
                    phase += 2f * Mathf.PI * f / SampleRate;

                    float subBody = Mathf.Sin(phase) * Mathf.Exp(-7.5f * t);
                    float click = (float)(rng.NextDouble() * 2 - 1) * Mathf.Exp(-55f * t) * 0.35f;

                    float sample = (float)Math.Tanh((subBody + click) * 1.6) * vol * 0.95f;
                    s[start + i] += sample;

                    float scEnv = Mathf.Exp(-14f * t);
                    int idx = start + i;
                    if (idx < sidechain.Length)
                        sidechain[idx] = Mathf.Max(sidechain[idx], scEnv);
                }
            }

            for (int b = 0; b < totalBeats; b++)
            {
                if (b % 4 != 2) continue;
                int start = (int)(b * beatSec * SampleRate);
                int snSamples = Math.Min((int)(0.24f * SampleRate), s.Length - start);
                float lp = 0f;

                for (int i = 0; i < snSamples; i++)
                {
                    float t = i / (float)SampleRate;
                    float noise = (float)(rng.NextDouble() * 2 - 1);
                    lp += 0.38f * (noise - lp);

                    float tone = Mathf.Sin(2f * Mathf.PI * 185f * t) * Mathf.Exp(-14f * t);
                    float snap = (noise - lp) * Mathf.Exp(-12f * t);

                    s[start + i] += (tone * 0.45f + snap * 0.75f) * vol * 0.85f;
                }
            }

            int total16ths = (int)(MusicDur / sixteenth);
            for (int h = 0; h < total16ths; h++)
            {
                if (density01 < 0.4f && h % 2 == 1) continue;
                int start = (int)(h * sixteenth * SampleRate);
                int hatLen = Math.Min((int)(0.045f * SampleRate), s.Length - start);
                float accent = (h % 4 == 0) ? 0.32f : (h % 2 == 0 ? 0.22f : 0.14f);
                float hp = 0f;

                for (int i = 0; i < hatLen; i++)
                {
                    float t = i / (float)SampleRate;
                    float noise = (float)(rng.NextDouble() * 2 - 1);
                    hp += 0.08f * (noise - hp);
                    s[start + i] += (noise - hp) * Mathf.Exp(-55f * t) * vol * accent;
                }
            }
        }

        private static void AddMasterChordProgression(float[] s, float[] sidechain, int[][] chordsMidi,
            float bpm, float vol, int level)
        {
            float chordDur = MusicDur / chordsMidi.Length;
            int chordSamples = (int)(chordDur * SampleRate);

            float[][] chordFreqs = new float[chordsMidi.Length][];
            for (int c = 0; c < chordsMidi.Length; c++)
            {
                chordFreqs[c] = new float[chordsMidi[c].Length];
                for (int k = 0; k < chordsMidi[c].Length; k++)
                    chordFreqs[c][k] = MidiToFreq(chordsMidi[c][k]);
            }

            for (int i = 0; i < s.Length; i++)
            {
                int c = Math.Min(chordsMidi.Length - 1, i / chordSamples);
                float localT = (i - c * chordSamples) / (float)SampleRate;

                float attack = Mathf.Clamp01(localT / 0.55f);
                attack = attack * attack * (3f - 2f * attack);
                float release = Mathf.Clamp01((chordDur - localT) / 0.65f);
                release = release * release * (3f - 2f * release);

                float t = i / (float)SampleRate;
                float lfoChorus = 0.003f * Mathf.Sin(2f * Mathf.PI * 0.22f * t);
                float lfoFilter = 0.85f + 0.15f * Mathf.Sin(2f * Mathf.PI * 0.09f * t);

                float v = 0f;
                var freqs = chordFreqs[c];
                for (int k = 0; k < freqs.Length; k++)
                {
                    float f = freqs[k];
                    float ph1 = 2f * Mathf.PI * (f * (1f + lfoChorus)) * localT;
                    float ph2 = 2f * Mathf.PI * (f * (1f - lfoChorus * 1.15f)) * localT;
                    v += (Mathf.Sin(ph1) + 0.8f * Mathf.Sin(ph2) + 0.25f * Mathf.Sin(ph1 * 2f));
                }

                float duck = (level >= 1 && sidechain != null && i < sidechain.Length) ? (1.0f - 0.65f * sidechain[i]) : 1.0f;
                float finalSample = v * (vol / (freqs.Length * 1.25f)) * attack * release * lfoFilter * duck;

                s[i] += (float)Math.Tanh(finalSample * 1.2);
            }
        }

        private static void AddMasterBassLine(float[] s, float[] sidechain, int[] roots, int[][] chords,
            float bpm, float vol, int level)
        {
            float sixteenth = 60f / bpm / 4f;
            int total16ths = (int)(MusicDur / sixteenth);
            float chordDur = MusicDur / roots.Length;

            for (int step = 0; step < total16ths; step++)
            {
                bool playNote = (level >= 2) ? (step % 2 == 0 || (step % 8 == 3 || step % 8 == 6))
                                             : (step % 4 == 0 || step % 8 == 6);
                if (!playNote) continue;

                float stepTime = step * sixteenth;
                int chordIdx = Math.Min(roots.Length - 1, (int)(stepTime / chordDur));
                int rootMidi = roots[chordIdx];

                int octaveShift = (step % 8 == 6) ? 7 : ((step % 16 == 10) ? 12 : 0);
                float f = MidiToFreq(rootMidi - 12 + octaveShift);

                int start = (int)(stepTime * SampleRate);
                int noteSamples = Math.Min((int)(sixteenth * 1.9f * SampleRate), s.Length - start);

                for (int i = 0; i < noteSamples; i++)
                {
                    float t = i / (float)SampleRate;
                    float env = Mathf.Exp(-8.5f * t);

                    float ph = 2f * Mathf.PI * f * t;
                    float osc = Mathf.Sin(ph) * 0.9f + Mathf.Sign(Mathf.Sin(ph)) * 0.22f * env;

                    int idx = start + i;
                    float duck = (sidechain != null && idx < sidechain.Length) ? (1.0f - 0.75f * sidechain[idx]) : 1.0f;

                    s[idx] += (float)Math.Tanh(osc * 1.5) * env * vol * 0.75f * duck;
                }
            }
        }

        private static void AddMasterArpProgression(float[] s, int[][] chordsMidi, float bpm,
            float notesPerSec, float vol, int level)
        {
            float chordDur = MusicDur / chordsMidi.Length;
            float stepDur = 1f / Mathf.Max(1f, notesPerSec);
            int totalSteps = (int)(MusicDur / stepDur);

            int delaySamples = Math.Max(1, (int)(SampleRate * (60f / bpm * 0.75f)));
            float[] delayBuffer = new float[delaySamples + 1];
            int delayIdx = 0;
            float feedback = 0.38f;

            for (int step = 0; step < totalSteps; step++)
            {
                float stepTime = step * stepDur;
                int chordIdx = Math.Min(chordsMidi.Length - 1, (int)(stepTime / chordDur));
                var chord = chordsMidi[chordIdx];

                int noteInChord = step % chord.Length;
                int oct = ((step / chord.Length) % 2) * 12;
                float f = MidiToFreq(chord[noteInChord] + 12 + oct);

                int start = (int)(stepTime * SampleRate);
                int noteSamples = Math.Min((int)(stepDur * 2.5f * SampleRate), s.Length - start);

                for (int i = 0; i < noteSamples; i++)
                {
                    float t = i / (float)SampleRate;
                    float env = Mathf.Exp(-6f * t / (stepDur * 0.7f));
                    float osc = Mathf.Sin(2f * Mathf.PI * f * t) + 0.35f * Mathf.Sin(2f * Mathf.PI * f * 2f * t);

                    float direct = osc * env * vol * 0.5f;

                    int targetSampleIdx = start + i;
                    float echo = delayBuffer[delayIdx] * feedback;
                    delayBuffer[delayIdx] = direct + echo * 0.4f;
                    delayIdx = (delayIdx + 1) % delaySamples;

                    s[targetSampleIdx] += direct + echo;
                }
            }
        }

        private static void AddArcanotechChoir(float[] s, int[][] chordsMidi, float bpm, float vol)
        {
            float chordDur = MusicDur / chordsMidi.Length;
            int chordSamples = (int)(chordDur * SampleRate);

            for (int i = 0; i < s.Length; i++)
            {
                int c = Math.Min(chordsMidi.Length - 1, i / chordSamples);
                float localT = (i - c * chordSamples) / (float)SampleRate;

                float attack = Mathf.Clamp01(localT / 0.45f);
                attack = attack * attack * (3f - 2f * attack);
                float release = Mathf.Clamp01((chordDur - localT) / 0.55f);
                release = release * release * (3f - 2f * release);

                float t = i / (float)SampleRate;
                float vibrato = 1f + 0.0035f * Mathf.Sin(2f * Mathf.PI * 5.1f * t);

                float sample = 0f;
                var chord = chordsMidi[c];
                for (int k = 0; k < chord.Length; k++)
                {
                    float f = MidiToFreq(chord[k] + 12) * vibrato;
                    float o1 = Mathf.Sin(2f * Mathf.PI * f * localT);
                    float o2 = Mathf.Sin(2f * Mathf.PI * (f * 2f) * localT) * 0.55f;
                    float o3 = Mathf.Sin(2f * Mathf.PI * (f * 3f) * localT) * 0.30f;
                    float o4 = Mathf.Sin(2f * Mathf.PI * (f * 4f) * localT) * 0.15f;
                    sample += (o1 + o2 + o3 + o4);
                }

                sample = (sample / (chord.Length * 1.1f)) * attack * release * vol;
                s[i] += (float)Math.Tanh(sample * 1.4);
            }
        }

        private static void AddHeavyWarPercussion(float[] s, System.Random rng, float bpm, float vol, int level)
        {
            float beatSec = 60f / bpm;
            int totalBeats = (int)(MusicDur / beatSec);

            for (int b = 0; b < totalBeats; b++)
            {
                bool isDownbeat = (b % 4 == 0);
                bool isTaikoSync = (level >= 1 && (b % 8 == 3 || b % 8 == 6));
                if (!isDownbeat && !isTaikoSync) continue;

                int start = (int)(b * beatSec * SampleRate);
                float hitVol = isDownbeat ? vol : vol * 0.65f;
                int durSamples = Math.Min((int)(0.42f * SampleRate), s.Length - start);

                float lp = 0f;
                for (int i = 0; i < durSamples; i++)
                {
                    float t = i / (float)SampleRate;
                    float pitch = 56f * Mathf.Exp(-8f * t) + 30f;
                    float sub = Mathf.Sin(2f * Mathf.PI * pitch * t) * Mathf.Exp(-5f * t);

                    float noise = (float)(rng.NextDouble() * 2 - 1);
                    lp += 0.16f * (noise - lp);
                    float strike = lp * Mathf.Exp(-22f * t);

                    s[start + i] += (sub * 1.15f + strike * 0.75f) * hitVol;
                }
            }
        }

        private static void AddCyberneticAcidLead(float[] s, int[] roots, float bpm, float vol, int level)
        {
            float sixteenth = 60f / bpm / 4f;
            int total16ths = (int)(MusicDur / sixteenth);
            float chordDur = MusicDur / roots.Length;

            for (int step = 0; step < total16ths; step++)
            {
                int pat = step % 8;
                if (pat == 1 || pat == 4 || pat == 6) continue;

                float stepTime = step * sixteenth;
                int chordIdx = Math.Min(roots.Length - 1, (int)(stepTime / chordDur));
                int root = roots[chordIdx];

                int[] melodyOffsets = { 0, 12, 3, 7, 10, 12, 15, 14 };
                int noteMidi = root + melodyOffsets[pat];
                float f = MidiToFreq(noteMidi);

                int start = (int)(stepTime * SampleRate);
                int len = Math.Min((int)(sixteenth * 1.5f * SampleRate), s.Length - start);

                float cycleT = stepTime / MusicDur;
                float cutoff = Mathf.Lerp(1.3f, 4.2f, 0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * cycleT));

                for (int i = 0; i < len; i++)
                {
                    float t = i / (float)SampleRate;
                    float env = Mathf.Exp(-11f * t);

                    float ph = (f * t) % 1f;
                    float saw = (2f * ph - 1f);
                    float res = Mathf.Sin(2f * Mathf.PI * f * cutoff * t) * env;

                    float sample = (saw * 0.55f + res * 0.45f) * env * vol * 0.42f;
                    s[start + i] += (float)Math.Tanh(sample * 1.6);
                }
            }
        }

        private static void AddPulseStaccatoStrings(float[] s, int[][] chordsMidi, float bpm, float vol)
        {
            float sixteenth = 60f / bpm / 4f;
            int total16ths = (int)(MusicDur / sixteenth);
            float chordDur = MusicDur / chordsMidi.Length;

            for (int step = 0; step < total16ths; step++)
            {
                float stepTime = step * sixteenth;
                int chordIdx = Math.Min(chordsMidi.Length - 1, (int)(stepTime / chordDur));
                var chord = chordsMidi[chordIdx];

                int noteMidi = chord[step % chord.Length] + 12;
                float f = MidiToFreq(noteMidi);

                int start = (int)(stepTime * SampleRate);
                int len = Math.Min((int)(sixteenth * 1.25f * SampleRate), s.Length - start);
                float accent = (step % 4 == 0) ? 1.0f : ((step % 2 == 0) ? 0.76f : 0.52f);

                for (int i = 0; i < len; i++)
                {
                    float t = i / (float)SampleRate;
                    float env = Mathf.Exp(-24f * t);
                    float osc = Mathf.Sin(2f * Mathf.PI * f * t) + 0.35f * Mathf.Sin(2f * Mathf.PI * (f * 2f) * t);
                    s[start + i] += osc * env * vol * accent * 0.72f;
                }
            }
        }

        private static void AddAnvilBeat(float[] s, float bpm, float vol)
        {
            float beatSec = 60f / bpm;
            int totalBeats = (int)(MusicDur / beatSec);

            for (int b = 0; b < totalBeats; b++)
            {
                if (b % 2 != 1) continue;
                int start = (int)(b * beatSec * SampleRate);
                int len = Math.Min((int)(0.18f * SampleRate), s.Length - start);

                for (int i = 0; i < len; i++)
                {
                    float t = i / (float)SampleRate;
                    float env = Mathf.Exp(-26f * t);
                    float clang = Mathf.Sin(2f * Mathf.PI * 1860f * t)
                                + 0.65f * Mathf.Sin(2f * Mathf.PI * 2740f * t)
                                + 0.35f * Mathf.Sin(2f * Mathf.PI * 4120f * t);
                    s[start + i] += clang * env * vol * 0.48f;
                }
            }
        }

        private static void AddCyberDjentChug(float[] s, float[] sidechain, System.Random rng, int[] roots, float bpm, float vol, int level)
        {
            float sixteenth = 60f / bpm / 4f;
            int total16ths = (int)(MusicDur / sixteenth);
            float chordDur = MusicDur / roots.Length;

            for (int step = 0; step < total16ths; step++)
            {
                int pat = step % 16;
                bool hit = (pat == 0 || pat == 2 || pat == 3 || pat == 5 || pat == 7 || pat == 8 || pat == 10 || pat == 11 || pat == 13 || pat == 14);
                if (level == 0 && (pat == 3 || pat == 7 || pat == 11 || pat == 14)) hit = false;
                if (!hit) continue;

                float stepTime = step * sixteenth;
                int chordIdx = Math.Min(roots.Length - 1, (int)(stepTime / chordDur));
                int root = roots[chordIdx];
                float f = MidiToFreq(root);

                int start = (int)(stepTime * SampleRate);
                int len = Math.Min((int)(sixteenth * 1.6f * SampleRate), s.Length - start);
                float accent = (pat == 0 || pat == 8) ? 1.0f : ((pat == 3 || pat == 10) ? 0.82f : 0.65f);

                for (int i = 0; i < len; i++)
                {
                    float t = i / (float)SampleRate;
                    float env = Mathf.Exp(-19f * t);

                    float rawSaw = 2f * ((f * t) % 1f) - 1f;
                    float rawSq = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * f * t));
                    float fifthSaw = 2f * ((f * 1.5f * t) % 1f) - 1f;
                    float octSaw = 2f * ((f * 2f * t) % 1f) - 1f;

                    float raw = rawSaw * 0.45f + rawSq * 0.25f + fifthSaw * 0.20f + octSaw * 0.10f;
                    float drive = 5.2f;
                    float distorted = (float)Math.Tanh(raw * drive);

                    float pickAttack = (float)(rng.NextDouble() * 2.0 - 1.0) * Mathf.Exp(-75f * t) * 0.35f;

                    int idx = start + i;
                    float duck = (sidechain != null && idx < sidechain.Length) ? (1.0f - 0.55f * sidechain[idx]) : 1.0f;

                    s[idx] += (distorted + pickAttack) * env * vol * accent * duck;
                }
            }
        }

        private static void AddNeuroCyberBass(float[] s, int[] roots, float bpm, float vol)
        {
            float chordDur = MusicDur / roots.Length;
            int chordSamples = (int)(chordDur * SampleRate);

            for (int i = 0; i < s.Length; i++)
            {
                int c = Math.Min(roots.Length - 1, i / chordSamples);
                float localT = (i - c * chordSamples) / (float)SampleRate;
                float t = i / (float)SampleRate;

                int root = roots[c];
                float f = MidiToFreq(root);

                float detune1 = f * 0.993f;
                float detune2 = f * 1.007f;

                float saw1 = 2f * ((detune1 * localT) % 1f) - 1f;
                float saw2 = 2f * ((detune2 * localT) % 1f) - 1f;

                float lfoFilter = 0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 1.4f * t);
                float raw = (saw1 + saw2) * 0.5f;

                float folded = Mathf.Sin(raw * (2.2f + 2.8f * lfoFilter));
                float sub = Mathf.Sin(2f * Mathf.PI * f * localT) * 0.6f;

                s[i] += (float)Math.Tanh((folded * 0.7f + sub) * 1.35) * vol * 0.65f;
            }
        }

        private static void AddBiologicalHeartbeat(float[] s, float bpm, float vol)
        {
            float beatSec = 60f / bpm;
            int totalBeats = (int)(MusicDur / beatSec);

            for (int b = 0; b < totalBeats; b++)
            {
                float[] offsets = { 0.0f, 0.22f };
                float[] gains = { 1.0f, 0.62f };

                for (int p = 0; p < 2; p++)
                {
                    int start = (int)((b * beatSec + offsets[p]) * SampleRate);
                    int pulseSamples = Math.Min((int)(0.28f * SampleRate), s.Length - start);

                    for (int i = 0; i < pulseSamples; i++)
                    {
                        float t = i / (float)SampleRate;
                        float sub = Mathf.Sin(2f * Mathf.PI * (48f * Mathf.Exp(-3.5f * t)) * t);
                        float env = Mathf.Exp(-14f * t);
                        s[start + i] += sub * env * vol * gains[p];
                    }
                }
            }
        }

        private static void AddSubRumble(float[] s, float freq, float vol)
        {
            for (int i = 0; i < s.Length; i++)
            {
                float t = i / (float)SampleRate;
                float lfo = 0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * 0.15f * t);
                s[i] += Mathf.Sin(2f * Mathf.PI * freq * t) * lfo * vol;
            }
        }

        private static void AddChronoShimmer(float[] s, float freq, float vol)
        {
            for (int i = 0; i < s.Length; i++)
            {
                float t = i / (float)SampleRate;
                float shimmer = Mathf.Sin(2f * Mathf.PI * freq * t + Mathf.Sin(t * 5f))
                              * Mathf.Sin(2f * Mathf.PI * (freq * 1.5f) * t);
                s[i] += shimmer * vol * (0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 0.08f * t));
            }
        }

        private static void AddIndustrialClang(float[] s, float bpm, float vol)
        {
            float beatSec = 60f / bpm;
            int totalBeats = (int)(MusicDur / beatSec);

            for (int b = 0; b < totalBeats; b++)
            {
                if (b % 8 != 6) continue;
                int start = (int)(b * beatSec * SampleRate);
                int samples = Math.Min((int)(0.35f * SampleRate), s.Length - start);

                for (int i = 0; i < samples; i++)
                {
                    float t = i / (float)SampleRate;
                    float metallic = Mathf.Sin(2f * Mathf.PI * 840f * t)
                                   + 0.6f * Mathf.Sin(2f * Mathf.PI * 1380f * t)
                                   + 0.35f * Mathf.Sin(2f * Mathf.PI * 2240f * t);
                    s[start + i] += metallic * Mathf.Exp(-16f * t) * vol;
                }
            }
        }

        private static void AddDyingHarmonicFade(float[] s, float vol)
        {
            for (int i = 0; i < s.Length; i++)
            {
                float t = i / (float)s.Length;
                float pitchDrop = Mathf.Lerp(1.0f, 0.45f, t * t);
                s[i] += Mathf.Sin(2f * Mathf.PI * (110f * pitchDrop) * (i / (float)SampleRate))
                      * Mathf.Exp(-2.5f * t) * vol;
            }
        }

        private static void AddRiser(float[] s, float vol)
        {
            var rng = new System.Random(5150);
            float lp = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                float t = i / (float)s.Length;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += Mathf.Lerp(0.03f, 0.4f, t) * (noise - lp);
                float env = t * t;
                s[i] += lp * env * vol * 1.6f;
            }
        }

        private static void ApplyAnalogMastering(float[] s, float targetPeak)
        {
            float max = 0.0001f;
            for (int i = 0; i < s.Length; i++) max = Math.Max(max, Math.Abs(s[i]));
            float scale = targetPeak / max;
            if (scale > 3.5f) scale = 3.5f;

            for (int i = 0; i < s.Length; i++)
            {
                s[i] = (float)Math.Tanh(s[i] * scale * 0.95);
            }
        }

        // =====================================================================
        // PRIMITIVES DE SYNTHÈSE SONORE
        // =====================================================================
        private static AudioClip Blip(string name, float f0, float f1, float dur, float vol)
        {
            int n = (int)(SampleRate * dur);
            float[] s = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float f = Mathf.Lerp(f0, f1, t);
                float env = Mathf.Sin(Mathf.PI * t);
                s[i] = Mathf.Sin(2f * Mathf.PI * f * i / SampleRate) * env * vol;
            }
            return MakeClip(name, s);
        }

        private static AudioClip Sweep(string name, float f0, float f1, float dur, float vol, bool addShimmer = false)
        {
            int n = (int)(SampleRate * dur);
            float[] s = new float[n];
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float f = Mathf.Lerp(f0, f1, t * t * (3f - 2f * t));
                phase += 2f * Mathf.PI * f / SampleRate;
                float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t));
                float v = Mathf.Sin(phase) * env * vol;
                if (addShimmer) v += 0.3f * Mathf.Sin(phase * 2.02f) * env * vol;
                s[i] = v;
            }
            Normalize(s, 0.8f);
            return MakeClip(name, s);
        }

        private static AudioClip Buzz(string name, float freq, float dur, float vol)
        {
            int n = (int)(SampleRate * dur);
            float[] s = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float sq = Mathf.Sign(Mathf.Sin(2f * Mathf.PI * freq * i / SampleRate));
                float env = (1f - t) * (1f - t);
                s[i] = (sq * 0.6f + 0.4f * Mathf.Sin(2f * Mathf.PI * freq * 2f * i / SampleRate)) * env * vol;
            }
            return MakeClip(name, s);
        }

        private static AudioClip Thud(string name, float freq, float dur, float vol, int seed)
        {
            int n = (int)(SampleRate * dur);
            float[] s = new float[n];
            var rng = new System.Random(seed);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float env = Mathf.Exp(-6f * t);
                float tone = Mathf.Sin(2f * Mathf.PI * freq * Mathf.Exp(-2f * t) * i / SampleRate);
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += 0.18f * (noise - lp);
                s[i] = (tone * 0.8f + lp * 0.5f) * env * vol;
            }
            return MakeClip(name, s);
        }

        private static AudioClip NoiseWhoosh(string name, float dur, float vol, bool reverse = false)
        {
            int n = (int)(SampleRate * dur);
            float[] s = new float[n];
            var rng = new System.Random(name.GetHashCode());
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                if (reverse) t = 1f - t;
                float env = Mathf.Sin(Mathf.PI * t);
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                float cutoff = Mathf.Lerp(0.04f, 0.5f, t);
                lp += cutoff * (noise - lp);
                s[i] = lp * env * vol * 1.6f;
            }
            Normalize(s, 0.75f);
            return MakeClip(name, s);
        }

        private static AudioClip NoiseBurst(string name, float dur, float vol)
        {
            int n = (int)(SampleRate * dur);
            float[] s = new float[n];
            var rng = new System.Random(name.GetHashCode() + 7);
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                s[i] = (float)(rng.NextDouble() * 2.0 - 1.0) * Mathf.Exp(-4f * t) * vol;
            }
            return MakeClip(name, s);
        }

        private static AudioClip Punch(string name, bool crit)
        {
            float dur = crit ? 0.4f : 0.25f;
            int n = (int)(SampleRate * dur);
            float[] s = new float[n];
            var rng = new System.Random(crit ? 999 : 111);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float env = Mathf.Exp(-9f * t);
                float body = Mathf.Sin(2f * Mathf.PI * (crit ? 70f : 95f) * Mathf.Exp(-3f * t) * i / SampleRate);
                float snap = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += 0.3f * (snap - lp);
                float crack = lp * Mathf.Exp(-22f * t);
                s[i] = (body * 0.9f + crack * (crit ? 1.2f : 0.8f)) * env;
                if (crit) s[i] += 0.35f * Mathf.Sin(2f * Mathf.PI * 1244f * i / SampleRate) * Mathf.Exp(-14f * t);
            }
            Normalize(s, 0.9f);
            return MakeClip("Punch_" + name, s);
        }

        private static AudioClip Metallic(string name, float f1, float f2, float dur, float vol = 0.6f)
        {
            int n = (int)(SampleRate * dur);
            float[] s = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float env = Mathf.Exp(-7f * t);
                s[i] = (Mathf.Sin(2f * Mathf.PI * f1 * i / SampleRate)
                      + 0.6f * Mathf.Sin(2f * Mathf.PI * f2 * i / SampleRate)
                      + 0.3f * Mathf.Sin(2f * Mathf.PI * f1 * 2.76f * i / SampleRate))
                      * env * vol * 0.5f;
            }
            return MakeClip("Metal_" + name, s);
        }

        private static AudioClip Chime(string name, float[] freqs, float dur)
        {
            int n = (int)(SampleRate * dur);
            float[] s = new float[n];
            for (int k = 0; k < freqs.Length; k++)
            {
                int start = (int)(n * k / (freqs.Length + 1));
                for (int i = start; i < n; i++)
                {
                    float t = (float)(i - start) / (n - start);
                    float env = Mathf.Exp(-4f * t);
                    s[i] += Mathf.Sin(2f * Mathf.PI * freqs[k] * (i - start) / SampleRate) * env * 0.4f;
                }
            }
            Normalize(s, 0.7f);
            return MakeClip("Chime_" + name, s);
        }

        private static AudioClip GongLike(string name)
        {
            int n = (int)(SampleRate * 1.2f);
            float[] s = new float[n];
            float[] partials = { 98f, 147f, 220f, 277f, 392f };
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float env = Mathf.Exp(-3f * t) * Mathf.Min(1f, t * 30f);
                float v = 0f;
                for (int k = 0; k < partials.Length; k++)
                    v += Mathf.Sin(2f * Mathf.PI * partials[k] * i / SampleRate) * (1f / (k + 1));
                s[i] = v * env * 0.35f;
            }
            Normalize(s, 0.8f);
            return MakeClip(name, s);
        }

        private static AudioClip Hurt(float baseFreq, float dur, float vol)
        {
            int n = (int)(SampleRate * dur);
            float[] s = new float[n];
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float f = baseFreq * (1f + 0.8f * Mathf.Exp(-8f * t)) + 30f * Mathf.Sin(t * 20f);
                phase += 2f * Mathf.PI * f / SampleRate;
                s[i] = (Mathf.Sin(phase) * 0.7f + Mathf.Sin(phase * 2.1f) * 0.3f) * Mathf.Exp(-5f * t) * vol;
            }
            return MakeClip("Hurt", s);
        }

        private static AudioClip FallSound()
        {
            int n = (int)(SampleRate * 0.55f);
            float[] s = new float[n];
            var rng = new System.Random(4242);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float env = t < 0.7f ? Mathf.Sin(Mathf.PI * t / 0.7f * 0.5f) : Mathf.Exp(-12f * (t - 0.7f));
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += 0.12f * (noise - lp);
                float thump = Mathf.Sin(2f * Mathf.PI * 75f * i / SampleRate) * Mathf.Exp(-6f * Math.Abs(t - 0.7f) * 4f);
                s[i] = (lp * 0.6f + thump * 0.8f) * env * 0.8f;
            }
            return MakeClip("Fall", s);
        }

        private static AudioClip DeathSound()
        {
            int n = (int)(SampleRate * 0.9f);
            float[] s = new float[n];
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float f = Mathf.Lerp(300f, 55f, t * t);
                phase += 2f * Mathf.PI * f / SampleRate;
                s[i] = Mathf.Sin(phase) * Mathf.Exp(-3.5f * t) * 0.8f;
            }
            return MakeClip("Death", s);
        }

        private static AudioClip BoomSound()
        {
            int n = (int)(SampleRate * 1.1f);
            float[] s = new float[n];
            var rng = new System.Random(9001);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float env = Mathf.Exp(-4.5f * t) * Mathf.Min(1f, t * 60f);
                float sub = Mathf.Sin(2f * Mathf.PI * 48f * Mathf.Exp(-1.5f * t) * i / SampleRate);
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += 0.08f * (noise - lp);
                s[i] = (sub * 1f + lp * 0.7f) * env;
            }
            Normalize(s, 0.95f);
            return MakeClip("DeepBoom", s);
        }

        private static AudioClip ShockSound()
        {
            int n = (int)(SampleRate * 0.5f);
            float[] s = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float dissonance = Mathf.Sin(2f * Mathf.PI * 466f * i / SampleRate)
                                 + Mathf.Sin(2f * Mathf.PI * 494f * i / SampleRate);
                s[i] = dissonance * 0.4f * Mathf.Exp(-5f * t) + 0.25f * Mathf.Sin(2f * Mathf.PI * 55f * i / SampleRate) * Mathf.Exp(-3f * t);
            }
            return MakeClip("Shock", s);
        }

        private static AudioClip BreathSound()
        {
            int n = (int)(SampleRate * 0.7f);
            float[] s = new float[n];
            var rng = new System.Random(31337);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float env = Mathf.Sin(Mathf.PI * t);
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += 0.25f * (noise - lp);
                s[i] = lp * env * 0.5f;
            }
            return MakeClip("Breath", s);
        }

        private static AudioClip DiceSound()
        {
            int n = (int)(SampleRate * 0.35f);
            float[] s = new float[n];
            var rng = new System.Random(2024);
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                s[i] = 0f;
                for (int k = 0; k < 3; k++)
                {
                    float ck = k * 0.28f;
                    if (t >= ck)
                    {
                        float lt = (t - ck) / 0.12f;
                        if (lt < 1f) s[i] += (float)(rng.NextDouble() * 2 - 1) * Mathf.Exp(-18f * lt) * 0.6f;
                    }
                }
            }
            return MakeClip("Dice", s);
        }

        private static AudioClip RewindSound()
        {
            int n = (int)(SampleRate * 0.8f);
            float[] s = new float[n];
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float f = Mathf.Lerp(1800f, 200f, t);
                phase += 2f * Mathf.PI * f / SampleRate;
                float env = Mathf.Sin(Mathf.PI * t);
                float gate = 0.6f + 0.4f * Mathf.Sign(Mathf.Sin(2f * Mathf.PI * 30f * i / SampleRate));
                s[i] = Mathf.Sin(phase) * env * gate * 0.5f;
            }
            return MakeClip("Rewind", s);
        }

        private static AudioClip LockSound()
        {
            int n = (int)(SampleRate * 0.22f);
            float[] s = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                s[i] = 0f;
                if (t < 0.05f) s[i] += Mathf.Sin(2f * Mathf.PI * 1500f * i / SampleRate) * Mathf.Exp(-60f * t) * 0.6f;
                float t2 = t - 0.09f;
                if (t2 > 0) s[i] += Mathf.Sin(2f * Mathf.PI * 2100f * i / SampleRate) * Mathf.Exp(-40f * t2) * 0.6f;
            }
            return MakeClip("Lock", s);
        }

        private static AudioClip ShotSound(string name, float dur, float vol)
        {
            int n = (int)(SampleRate * dur);
            float[] s = new float[n];
            var rng = new System.Random(name.GetHashCode());
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float env = Mathf.Exp(-10f * t);
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += 0.5f * (noise - lp);
                float body = Mathf.Sin(2f * Mathf.PI * 160f * Mathf.Exp(-4f * t) * i / SampleRate);
                s[i] = (lp * 0.9f + body * 0.6f) * env * vol;
            }
            Normalize(s, 0.9f);
            return MakeClip(name, s);
        }

        private static AudioClip SpellCast()
        {
            int n = (int)(SampleRate * 0.7f);
            float[] s = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float env = Mathf.Sin(Mathf.PI * t);
                float v = Mathf.Sin(2f * Mathf.PI * 880f * Mathf.Exp(t) * i / SampleRate)
                        + 0.5f * Mathf.Sin(2f * Mathf.PI * 1320f * i / SampleRate + t * 9f);
                s[i] = v * env * 0.35f;
            }
            Normalize(s, 0.75f);
            return MakeClip("SpellCast", s);
        }

        private static AudioClip WhisperSound()
        {
            int n = (int)(SampleRate * 1f);
            float[] s = new float[n];
            var rng = new System.Random(666);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float env = Mathf.Sin(Mathf.PI * t);
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += (0.05f + 0.2f * Mathf.Sin(t * 12f) * 0.5f + 0.15f) * (noise - lp);
                s[i] = lp * env * 0.5f;
            }
            return MakeClip("Whisper", s);
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
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Killtime.Audio
{
    /// <summary>Partiel SFX tactiques : dispatch Generate + primitives de synthèse.</summary>
    public static partial class ProceduralAudioFactory
    {
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
                case SoundId.Weapon_Laser_Fire: return LaserShotSound();
                case SoundId.Weapon_Laser_Impact: return LaserImpactSound();
                case SoundId.Grenade_Pin: return Metallic("Pin", 3200f, 4200f, 0.09f, 0.5f);
                case SoundId.Grenade_Throw: return NoiseWhoosh("Throw", 0.22f, 0.5f);
                case SoundId.Grenade_Bounce: return GrenadeBounceSound();
                case SoundId.Grenade_Explosion_Frag: return GrenadeExplosionSound(false);
                case SoundId.Grenade_Explosion_Heavy: return GrenadeExplosionSound(true);
                case SoundId.Grenade_Flash: return GrenadeFlashSound();
                case SoundId.Grenade_Smoke: return GrenadeSmokeSound();
                case SoundId.Grenade_Shrapnel: return ShrapnelSound();
                case SoundId.Launcher_Thump: return Thud("Thump", 130f, 0.22f, 0.9f, 5150);
                case SoundId.Grenade_Gas: return GrenadeSmokeSound();

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
                case SoundId.Cinematic_WarpSwell: return PlanetSwell("WarpSwell", 5f, 0.8f, 90211, startHot: false, bloom: false, releaseEnd: false, shepardOctaves: -1f, shepardGain: 0.20f, brass: true, brassGain: 0.22f);
                case SoundId.Cinematic_ApproachSwell: return PlanetSwell("ApproachSwell", 10f, 0.85f, 90211, startHot: true, bloom: true, releaseEnd: true, shepardOctaves: -1f, shepardGain: 0.20f, brass: true, brassGain: 0.22f);

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

        /// <summary>
        /// Montée statique façon logo (HBO) : souffle d'air qui s'amplifie pendant
        /// que la caméra s'approche, scintillement montant, puis bloom d'accord
        /// résolu et relâche douce. 100% déterministe (seed fixe).
        /// Paire soudée sans redémarrage : le warp part du silence et SOUTIENT
        /// jusqu'au bout (pas de relâche), l'approche démarre à chaud au même
        /// niveau et ne se relâche qu'à la fin (fin de cine_1 → dialogue).
        /// Moteur générique : les presets de la banque JSON (SoundBankCatalog)
        /// passent par ici via GetBankClip — aucun son nommé n'est hardcodé.
        /// </summary>
        private static AudioClip PlanetSwell(string name, float dur, float vol, int seed, bool startHot, bool bloom, bool releaseEnd,
            float shepardOctaves, float shepardGain, bool brass, float brassGain)
        {
            int n = (int)(SampleRate * dur);
            float[] s = new float[n];
            var rng = new System.Random(seed);
            float lpAir = 0f;
            float lpBrass = 0f;
            float phaseLow = 0f;
            float phaseShim = 0f;
            float phaseBrassRoot = 0f;
            float phaseBrassFifth = 0f;
            // Illusion de Shepard : 5 partiels en octaves qui glissent sans fin
            // (la fréquence semble monter toujours). Fenêtre gaussienne autour de 620 Hz.
            float octaves = shepardOctaves > 0f ? shepardOctaves : dur / 7.5f;
            float[] shepPhase = new float[5];
            // Micro-fondus anti-clic (15 ms), inaudibles comme "redémarrage".
            int edge = Mathf.Max(1, (int)(SampleRate * 0.015f));
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                // Enveloppe : crescendo depuis silence OU arche chaude qui ne
                // retombe qu'en fin de plan (jamais de trou à la jointure).
                float rise = t * t * (3f - 2f * t);
                float env;
                if (!startHot)
                {
                    env = Mathf.Pow(rise, 1.5f);
                }
                else
                {
                    float arch = 1f + 0.12f * Mathf.Sin(Mathf.PI * t);
                    env = releaseEnd ? arch * (1f - Mathf.SmoothStep(0.88f, 1f, t) * 0.95f) : arch;
                }
                float edgeFade = startHot
                    ? Mathf.Clamp01((float)(n - 1 - i) / edge)
                    : Mathf.Clamp01(Mathf.Min((float)i / edge, (float)(n - 1 - i) / edge));
                env *= edgeFade;
                // 1) Air/statique : passe-bas qui s'ouvre avec l'approche.
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                float cutoff = Mathf.Lerp(0.03f, 0.35f, startHot ? Mathf.Max(rise, 0.55f) : rise);
                lpAir += cutoff * (noise - lpAir);
                // 2) Rumble grave qui monte légèrement (55 → 82 Hz).
                float fLow = Mathf.Lerp(55f, 82f, startHot ? 1f : rise);
                phaseLow += 2f * Mathf.PI * fLow / SampleRate;
                float rumble = Mathf.Sin(phaseLow) * 0.55f + Mathf.Sin(phaseLow * 0.5f) * 0.3f;
                // 3) Scintillement : émerge avec le swell, déjà présent à chaud.
                float fShim = Mathf.Lerp(880f, 1320f, startHot ? 1f : rise);
                phaseShim += 2f * Mathf.PI * fShim / SampleRate;
                float shimGain = startHot ? Mathf.Lerp(0.8f, 1f, rise) : rise * rise;
                float shimmer = (Mathf.Sin(phaseShim) + 0.4f * Mathf.Sin(phaseShim * 2.02f)) * shimGain;
                // 4) Bloom résolu (quinte + octave), seulement côté approche.
                float bloomGate = bloom ? Mathf.SmoothStep(0.62f, 0.88f, t) : 0f;
                float bloomV = (Mathf.Sin(phaseLow * 4f) * 0.5f + Mathf.Sin(phaseLow * 6f) * 0.3f) * bloomGate;
                // 5) Shepard : raccordement exact de la fréquence atteinte en fin de warp
                float shep = 0f;
                float shepOctaveOffset = startHot ? (5.0f / 7.5f) : 0f;
                for (int k = 0; k < 5; k++)
                {
                    float f = 110f * Mathf.Pow(2f, (k - 2) + shepOctaveOffset + t * octaves);
                    shepPhase[k] += 2f * Mathf.PI * f / SampleRate;
                    float logDist = Mathf.Log(f / 620f) / Mathf.Log(2f);
                    float w = Mathf.Exp(-logDist * logDist / 2.42f);
                    shep += Mathf.Sin(shepPhase[k]) * w;
                }
                shep *= shepardGain * Mathf.Clamp01(startHot ? 1.0f : (0.25f + rise));
                // 6) Cuivres : sous départ à chaud, maintenir les cuivres au niveau plein atteint
                float brassGate = startHot ? 1.0f : Mathf.SmoothStep(0.35f, 0.8f, t);
                float vib = 1f + 0.006f * Mathf.Sin(2f * Mathf.PI * 5.2f * i / SampleRate);
                phaseBrassRoot += 2f * Mathf.PI * fLow * 2f * vib / SampleRate;
                phaseBrassFifth += 2f * Mathf.PI * fLow * 3f * vib / SampleRate;
                float saw = 0f;
                if (brass)
                {
                    for (int h = 1; h <= 8; h++)
                        saw += (Mathf.Sin(phaseBrassRoot * h) + 0.6f * Mathf.Sin(phaseBrassFifth * h)) / h;
                    saw *= brassGain;
                }
                float brassCut = Mathf.Lerp(0.04f, 0.5f, Mathf.Clamp01(env * 1.2f));
                lpBrass += brassCut * (saw - lpBrass);
                float brassSignal = lpBrass * brassGate;
                s[i] = (lpAir * 1.1f + rumble * 0.5f + shimmer * 0.35f + bloomV * 0.5f + shep + brassSignal) * env * vol;
            }
            Normalize(s, 0.8f);
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

        private static AudioClip LaserShotSound()
        {
            int n = (int)(SampleRate * 0.16f);
            float[] s = new float[n];
            float phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float env = Mathf.Exp(-14f * t);
                float f = 2600f * Mathf.Exp(-22f * t) + 240f;
                phase += 2f * Mathf.PI * f / SampleRate;

                float sine = Mathf.Sin(phase);
                float square = Mathf.Sign(sine) * 0.25f;
                s[i] = (sine * 0.75f + square) * env * 0.85f;
            }
            Normalize(s, 0.88f);
            return MakeClip("LaserShot", s);
        }

        private static AudioClip LaserImpactSound()
        {
            int n = (int)(SampleRate * 0.13f);
            float[] s = new float[n];
            var rng = new System.Random(8812);
            float hp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float env = Mathf.Exp(-18f * t);
                float f = 900f * Mathf.Exp(-28f * t) + 110f;
                float pop = Mathf.Sin(2f * Mathf.PI * f * t);

                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                hp += 0.45f * (noise - hp);
                float hiss = (noise - hp) * Mathf.Exp(-12f * t);

                s[i] = (pop * 0.6f + hiss * 0.65f) * env;
            }
            Normalize(s, 0.85f);
            return MakeClip("LaserImpact", s);
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

        private static AudioClip GrenadeBounceSound()
        {
            int n = (int)(SampleRate * 0.18f);
            float[] s = new float[n];
            var rng = new System.Random(1915);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float env = Mathf.Exp(-22f * t);
                float tone = Mathf.Sin(2f * Mathf.PI * 620f * Mathf.Exp(-6f * t) * t);
                float noise = (float)(rng.NextDouble() * 2 - 1) * Mathf.Exp(-30f * t) * 0.4f;
                s[i] = (tone * 0.6f + noise) * env * 0.7f;
            }
            // Second rebond atténué à 90ms.
            int second = (int)(SampleRate * 0.09f);
            for (int i = second; i < n; i++)
            {
                float t = (i - second) / (float)SampleRate;
                s[i] += Mathf.Sin(2f * Mathf.PI * 480f * t) * Mathf.Exp(-28f * t) * 0.3f;
            }
            Normalize(s, 0.7f);
            return MakeClip("Bounce", s);
        }

        private static AudioClip GrenadeExplosionSound(bool heavy)
        {
            float dur = heavy ? 1.6f : 1.1f;
            int n = (int)(SampleRate * dur);
            float[] s = new float[n];
            var rng = new System.Random(heavy ? 1942 : 1968);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float tn = i / (float)n;
                // Attaque quasi instantanée puis décroissance.
                float env = Mathf.Exp(-(heavy ? 3.2f : 4.5f) * tn) * Mathf.Min(1f, t * 220f);
                float sub = Mathf.Sin(2f * Mathf.PI * (heavy ? 42f : 52f) * Mathf.Exp(-1.8f * tn) * t);
                float noise = (float)(rng.NextDouble() * 2 - 1);
                lp += (heavy ? 0.06f : 0.10f) * (noise - lp);
                float crack = (noise - lp * 0.4f) * Mathf.Exp(-9f * tn);
                float ring = Mathf.Sin(2f * Mathf.PI * 2400f * t) * Mathf.Exp(-16f * tn) * 0.25f;
                s[i] = (sub * 1.1f + lp * 0.8f + crack * 0.9f + ring) * env;
            }
            Normalize(s, heavy ? 1.0f : 0.95f);
            return MakeClip(heavy ? "GrenadeHeavy" : "GrenadeFrag", s);
        }

        private static AudioClip GrenadeFlashSound()
        {
            int n = (int)(SampleRate * 0.7f);
            float[] s = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float env = Mathf.Exp(-6f * t);
                float bang = Mathf.Sin(2f * Mathf.PI * 3400f * t) * 0.5f
                           + Mathf.Sin(2f * Mathf.PI * 5100f * t) * 0.3f
                           + Mathf.Sin(2f * Mathf.PI * 180f * t) * 0.6f;
                s[i] = bang * env * 0.8f;
            }
            Normalize(s, 0.9f);
            return MakeClip("Flash", s);
        }

        private static AudioClip GrenadeSmokeSound()
        {
            float dur = 1.4f;
            int n = (int)(SampleRate * dur);
            float[] s = new float[n];
            var rng = new System.Random(1960);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t * 1.1f));
                float noise = (float)(rng.NextDouble() * 2 - 1);
                lp += 0.12f * (noise - lp);
                s[i] = lp * env * 0.6f;
            }
            Normalize(s, 0.5f);
            return MakeClip("Smoke", s);
        }

        private static AudioClip ShrapnelSound()
        {
            int n = (int)(SampleRate * 0.5f);
            float[] s = new float[n];
            var rng = new System.Random(7777);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float env = Mathf.Exp(-7f * t);
                // Sifflements d'éclats : 3 partiels descendants + bruit métallique.
                float whistle = Mathf.Sin(2f * Mathf.PI * (2800f * Mathf.Exp(-4f * t) + 400f) * t) * 0.35f
                              + Mathf.Sin(2f * Mathf.PI * (1900f * Mathf.Exp(-5f * t) + 300f) * t) * 0.25f;
                float noise = (float)(rng.NextDouble() * 2 - 1) * Mathf.Exp(-10f * t) * 0.5f;
                s[i] = (whistle + noise) * env;
            }
            Normalize(s, 0.65f);
            return MakeClip("Shrapnel", s);
        }
    }
}


using System;
using System.Collections.Generic;
using UnityEngine;

namespace Killtime.Audio
{
    /// <summary>Partiel couches de production musicale (Add* + mastering).</summary>
    public static partial class ProceduralAudioFactory
    {
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
                float finalSample = v * (vol / Mathf.Max(1f, Mathf.Sqrt(freqs.Length * 1.4f))) * attack * release * lfoFilter * duck;

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

                while (rootMidi < 34) rootMidi += 12;
                while (rootMidi > 46) rootMidi -= 12;

                int octaveShift = (step % 8 == 6) ? 7 : ((step % 16 == 10) ? 12 : 0);
                float f = MidiToFreq(rootMidi + octaveShift);

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
    }
}


using System;
using UnityEngine;

namespace Killtime.Audio
{
    public static partial class GenerativeMusicEngine
    {
        // ================= SYNTHÉTISEURS, FORMANTS, FX & DSP =================

        private static void AddGenerativeBass(float[] s, float[] sc, System.Random rng,
            GenerativeMusicParams p, GenerativeSectionRole role, int[] bassRoots, int[][] chords,
            float chordDur, float beatSec, float vol)
        {
            float sixteenth = beatSec / 4f;
            int total16 = (int)((chordDur * bassRoots.Length) / sixteenth);
            bool chip = (p.preset == GenerativeStylePreset.Retro16Bit);
            bool growl = (p.intensity01 > 0.58f || role == GenerativeSectionRole.Climax) && !chip;

            for (int step = 0; step < total16; step++)
            {
                float stepT = step * sixteenth;
                int m16 = step % 16;
                bool play;
                if (role == GenerativeSectionRole.Breakdown)
                    play = (m16 == 0) || (m16 == 10 && p.intensity01 > 0.4f);
                else if (p.intensity01 > 0.72f || role == GenerativeSectionRole.Climax)
                    play = (m16 % 2 == 0) || m16 == 3 || m16 == 6 || m16 == 11 || m16 == 14;
                else if (p.intensity01 > 0.42f)
                    play = (m16 % 4 == 0) || m16 == 6 || m16 == 10 || m16 == 14;
                else
                    play = (m16 == 0) || m16 == 6 || m16 == 10;

                if (!play) continue;
                if (m16 != 0 && rng.NextDouble() < p.chaos01 * 0.10) continue;

                int chordIdx = Math.Min(bassRoots.Length - 1, (int)(stepT / chordDur));
                int root = bassRoots[chordIdx];
                while (root < 31) root += 12;
                while (root > 43) root -= 12;

                int intervalJump = 0;
                if ((m16 == 6 || m16 == 14) && rng.NextDouble() < 0.65) intervalJump = 12; // octave
                else if (m16 == 11 && chords != null && chordIdx < chords.Length && chords[chordIdx].Length > 2)
                    intervalJump = 7; // quinte d'appui
                float f = MidiFreq(root + intervalJump);
                float dt = f / SampleRate;

                int start = (int)(stepT * SampleRate);
                if (start < 0 || start >= s.Length) continue;
                float noteDur = (role == GenerativeSectionRole.Breakdown) ? sixteenth * 5.5f : sixteenth * 2.15f;
                int len = Math.Min((int)(noteDur * SampleRate), s.Length - start);

                float phSub = 0f, phSaw1 = 0f, phSaw2 = 0.25f;
                float ic1 = 0f, ic2 = 0f; // états filtre SVF 2-pôles
                float baseCutoffHz = Mathf.Lerp(180f, 1150f, p.brightness01);
                float resQ = growl ? 2.2f : 1.3f;

                for (int i = 0; i < len; i++)
                {
                    float t = i / (float)SampleRate;
                    float ampEnv = Mathf.Exp(-7.2f * t) * Smooth01(t / 0.004f);
                    float fltEnv = Mathf.Exp(-14f * t);

                    phSub += 2f * Mathf.PI * f / SampleRate;
                    phSaw1 += dt * 0.996f; if (phSaw1 >= 1f) phSaw1 -= 1f;
                    phSaw2 += dt * 1.004f; if (phSaw2 >= 1f) phSaw2 -= 1f;

                    float rawHarmonic;
                    if (chip)
                    {
                        rawHarmonic = PolyBlepSquare(phSaw1, dt) * 0.65f;
                    }
                    else if (growl)
                    {
                        // Reese + FM mordante filtrée par enveloppe résonante
                        float fm = Mathf.Sin(phSub * 2f) * (1.4f + p.tension01 * 1.8f) * fltEnv;
                        float reese = (PolyBlepSaw(phSaw1, dt) + PolyBlepSaw(phSaw2, dt)) * 0.45f;
                        rawHarmonic = reese + Mathf.Sin(phSub + fm) * 0.45f;
                    }
                    else
                    {
                        rawHarmonic = PolyBlepSaw(phSaw1, dt) * 0.50f;
                    }

                    float cutHz = Mathf.Clamp(baseCutoffHz + 1600f * fltEnv * (0.4f + 0.6f * p.intensity01), 120f, 4200f);
                    float filteredMid = SvfLowPass(rawHarmonic, cutHz, resQ, ref ic1, ref ic2);
                    float cleanSub = Mathf.Sin(phSub) * 0.78f;

                    int idx = start + i;
                    float duck = (idx < sc.Length) ? (1f - 0.74f * sc[idx]) : 1f;
                    s[idx] += (float)Math.Tanh((cleanSub + filteredMid * 0.85f) * 1.55) * ampEnv * vol * duck;
                }
            }
        }

        private static void AddGenerativePad(float[] s, float[] sc, GenerativeMusicParams p,
            GenerativeSectionRole role, int[][] chords, float chordDur, float vol)
        {
            int chordSamples = Math.Max(1, (int)(chordDur * SampleRate));
            float baseCutHz = Mathf.Lerp(420f, 3200f, p.brightness01);
            if (role == GenerativeSectionRole.Breakdown) baseCutHz *= 0.75f;
            else if (role == GenerativeSectionRole.Climax) baseCutHz *= 1.25f;

            float ic1 = 0f, ic2 = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                int c = Math.Min(chords.Length - 1, i / chordSamples);
                float localT = (i - c * chordSamples) / (float)SampleRate;
                float atk = Smooth01(localT / 0.48f);
                float rel = Smooth01((chordDur - localT) / 0.55f);
                float t = i / (float)SampleRate;

                float lfoBreath = 0.82f + 0.18f * Mathf.Sin(2f * Mathf.PI * 0.18f * t);
                float detune = 0.0038f + 0.0012f * Mathf.Sin(2f * Mathf.PI * 0.23f * t);
                float v = 0f;
                var chord = chords[c];

                for (int k = 0; k < chord.Length; k++)
                {
                    float f = MidiFreq(chord[k]);
                    float dt = f / SampleRate;
                    float ph0 = (f * localT) % 1f;
                    float ph1 = (f * (1f + detune) * localT + 0.17f * k) % 1f;
                    float ph2 = (f * (1f - detune) * localT + 0.53f * k) % 1f;
                    // Supersaw PolyBLEP sans repliement spectral + fondamentale chaude
                    float sawMix = PolyBlepSaw(ph0, dt) * 0.26f
                                 + PolyBlepSaw(ph1, dt) * 0.24f
                                 + PolyBlepSaw(ph2, dt) * 0.24f
                                 + Mathf.Sin(2f * Mathf.PI * ph0) * 0.36f;
                    v += sawMix;
                }
                v /= Mathf.Max(1f, Mathf.Sqrt(chord.Length * 1.15f));

                float cutHz = Mathf.Clamp(baseCutHz * lfoBreath, 280f, 5200f);
                float warmPad = SvfLowPass(v, cutHz, 0.85f, ref ic1, ref ic2);
                float duck = (i < sc.Length) ? (1f - 0.58f * sc[i]) : 1f;
                s[i] += (float)Math.Tanh(warmPad * 1.35) * atk * rel * vol * 2.15f * duck;
            }
        }

        private static void AddStaccatoLayer(float[] s, float[] sc, System.Random rng, GenerativeMusicParams p,
            GenerativeSectionRole role, int[][] chords, float chordDur, float beatSec, float vol)
        {
            if (role == GenerativeSectionRole.Breakdown) return;
            float sixteenth = beatSec / 4f;
            int total = (int)((chordDur * chords.Length) / sixteenth);

            for (int step = 0; step < total; step++)
            {
                int m4 = step % 4;
                if (m4 == 1 && rng.NextDouble() < 0.55) continue;
                if (m4 == 3 && p.intensity01 < 0.6f && rng.NextDouble() < 0.5) continue;

                float stepT = step * sixteenth;
                int ci = Math.Min(chords.Length - 1, (int)(stepT / chordDur));
                var chord = chords[ci];
                // Alterne basse d'accord et notes supérieures façon ostinato cordes spiccato
                int voiceIdx = (m4 == 0) ? 0 : (step % chord.Length);
                int note = chord[voiceIdx] + (m4 == 0 ? 0 : 12);
                float f = MidiFreq(note);
                float dt = f / SampleRate;

                int start = (int)(stepT * SampleRate);
                if (start < 0 || start >= s.Length) continue;
                int len = Math.Min((int)(sixteenth * 1.15f * SampleRate), s.Length - start);
                float acc = (m4 == 0) ? 1.0f : (m4 == 2 ? 0.78f : 0.56f);
                float ph = 0f, ic1 = 0f, ic2 = 0f;

                for (int i = 0; i < len; i++)
                {
                    float t = i / (float)SampleRate;
                    float env = Mathf.Exp(-20f * t) * Smooth01(t / 0.005f);
                    ph += dt; if (ph >= 1f) ph -= 1f;
                    float bow = PolyBlepSaw(ph, dt) * 0.55f + Mathf.Sin(2f * Mathf.PI * ph) * 0.45f;
                    float flt = SvfLowPass(bow, 1400f + 1800f * env, 0.9f, ref ic1, ref ic2);
                    int idx = start + i;
                    float duck = (idx < sc.Length) ? (1f - 0.45f * sc[idx]) : 1f;
                    s[idx] += flt * env * vol * acc * duck;
                }
            }
        }

        /// <summary>
        /// Arpège euclidien cristallin avec écho synchronisé rebouclé sur la mesure (zéro coupure en fin de boucle).
        /// </summary>
        private static void AddEuclideanArp(float[] s, float[] sc, System.Random rng, GenerativeMusicParams p,
            GenerativeSectionRole role, int[][] chords, float chordDur, float beatSec, float vol, int[] motif)
        {
            float sixteenth = beatSec / 4f;
            int total = (int)((chordDur * chords.Length) / sixteenth);
            int chipBonus = (p.preset == GenerativeStylePreset.Retro16Bit) ? 2 : 0;
            int pulses = Mathf.Clamp(Mathf.RoundToInt(5 + p.intensity01 * 7 + p.chaos01 * 2) + chipBonus, 4, 14);
            if (role == GenerativeSectionRole.Breakdown) pulses = Mathf.Min(pulses, 6);
            int rotation = ((p.generation * 3) % 16 + 16) % 16;

            // Delay croche pointée (3 doubles-croches) avec écriture circulaire dans le buffer de boucle
            int delaySpl = Math.Max(1, (int)(SampleRate * sixteenth * 3f));
            bool chip = (p.preset == GenerativeStylePreset.Retro16Bit);

            for (int step = 0; step < total; step++)
            {
                if (!EuclidHit(step + rotation, 16, pulses)) continue;
                float stepT = step * sixteenth;
                int ci = Math.Min(chords.Length - 1, (int)(stepT / chordDur));
                var chord = chords[ci];

                // Arpège ordonné (montant/descendant) enrichi par le motif principal
                int patternIdx = (step % 6 < 4) ? (step % chord.Length) : (chord.Length - 1 - (step % chord.Length));
                int note = chord[Mathf.Clamp(patternIdx, 0, chord.Length - 1)] + 12;
                if (motif != null && motif.Length > 0 && step % 4 == 0 && motif[step % motif.Length] > 0)
                    note = motif[step % motif.Length];

                float f = MidiFreq(note);
                float dt = f / SampleRate;
                int start = (int)(stepT * SampleRate);
                if (start < 0 || start >= s.Length) continue;
                int len = (int)(sixteenth * 1.9f * SampleRate);
                float ph = 0f, ic1 = 0f, ic2 = 0f;

                for (int i = 0; i < len; i++)
                {
                    float t = i / (float)SampleRate;
                    float env = Mathf.Exp(-10.5f * t) * Smooth01(t / 0.003f);
                    ph += dt; if (ph >= 1f) ph -= 1f;
                    float osc = chip
                        ? PolyBlepSquare(ph, dt) * 0.65f
                        : (PolyBlepSaw(ph, dt) * 0.45f + Mathf.Sin(2f * Mathf.PI * ph) * 0.55f);
                    float pluck = SvfLowPass(osc, 900f + 3200f * env * (0.5f + 0.5f * p.brightness01), 1.2f, ref ic1, ref ic2);
                    float direct = pluck * env * vol * 0.58f;

                    int idx0 = (start + i) % s.Length;
                    int idx1 = (start + i + delaySpl) % s.Length;
                    int idx2 = (start + i + delaySpl * 2) % s.Length;
                    float duck = (idx0 < sc.Length) ? (1f - 0.40f * sc[idx0]) : 1f;
                    s[idx0] += direct * duck;
                    s[idx1] += direct * 0.34f * duck;
                    s[idx2] += direct * 0.14f * duck;
                }
            }
        }

        /// <summary>
        /// Lead soliste expressif : portamento legato entre notes conjointes, vibrato progressif,
        /// contre-chant harmonisé à la tierce/sixte en Climax, et delay stéréo-like bouclé.
        /// </summary>
        private static void AddLeadCallResponse(float[] s, float[] sc, System.Random rng, GenerativeMusicParams p,
            GenerativeSectionRole role, int[] motifA, int[] motifB, int[] counterMotif,
            int[][] chords, float chordDur, float barSec, float beatSec, float vol)
        {
            float eighth = beatSec / 2f;
            int bars = p.bars;
            bool chip = (p.preset == GenerativeStylePreset.Retro16Bit);
            bool useHarmony = (role == GenerativeSectionRole.Climax || p.intensity01 > 0.72f);
            int delaySpl = Math.Max(1, (int)(SampleRate * beatSec * 0.75f)); // delay croche pointée
            float prevFreq = -1f;

            for (int bar = 0; bar < bars; bar++)
            {
                if (role == GenerativeSectionRole.Breakdown && bar < 2) continue;
                bool secondHalf = bar >= bars / 2;
                int[] motif = secondHalf ? motifB : motifA;
                float barT = bar * barSec;

                for (int st = 0; st < 8; st++)
                {
                    int mIdx = (st + (bar % 2) * 8) % motif.Length;
                    int note = motif[mIdx];
                    if (note < 0)
                    {
                        prevFreq = -1f;
                        continue;
                    }

                    // Tenue longue si le pas suivant est un silence (note chantée qui respire)
                    int nextNote = motif[(mIdx + 1) % motif.Length];
                    float sustainMult = (nextNote < 0) ? 1.85f : 1.12f;

                    float tt = barT + st * eighth;
                    int start = (int)(tt * SampleRate);
                    if (start < 0 || start >= s.Length) continue;
                    int len = (int)(eighth * sustainMult * SampleRate);

                    float targetFreq = MidiFreq(note);
                    float startFreq = (prevFreq > 0f && Math.Abs(targetFreq - prevFreq) / targetFreq < 0.35f)
                        ? prevFreq : targetFreq;
                    prevFreq = targetFreq;

                    int harmNote = (useHarmony && counterMotif != null && mIdx < counterMotif.Length) ? counterMotif[mIdx] : -1;
                    float harmFreq = harmNote > 0 ? MidiFreq(harmNote) : 0f;

                    float vibRate = 5.4f + (float)rng.NextDouble() * 0.6f;
                    float vibDepth = 0.0045f + p.tension01 * 0.0035f;
                    float vel = vol * ((st % 2 == 0) ? 1.0f : 0.78f);
                    float phLead = 0f, phSub = 0f, phHarm = 0f;
                    float ic1 = 0f, ic2 = 0f;
                    float baseCutHz = Mathf.Lerp(1100f, 4400f, p.brightness01);

                    for (int i = 0; i < len; i++)
                    {
                        float t = i / (float)SampleRate;
                        float normT = (float)i / Math.Max(1, len);
                        // Portamento rapide (45ms) depuis la note précédente si legato
                        float glide = Mathf.Exp(-38f * t);
                        float curFreq = Mathf.Lerp(targetFreq, startFreq, glide);

                        // Vibrato progressif (apparaît après l'attaque comme un vrai instrumentiste)
                        float vibOnset = Smooth01((t - 0.06f) / 0.14f);
                        float vib = 1f + vibDepth * vibOnset * Mathf.Sin(2f * Mathf.PI * vibRate * t);
                        float dt = (curFreq * vib) / SampleRate;

                        phLead += dt; if (phLead >= 1f) phLead -= 1f;
                        phSub += dt * 0.5f; if (phSub >= 1f) phSub -= 1f;

                        float env = Smooth01(t / 0.012f) * Mathf.Exp(-4.2f * t / sustainMult) * Smooth01((1f - normT) / 0.08f);
                        float osc;
                        if (chip)
                        {
                            osc = PolyBlepSquare(phLead, dt) * 0.62f + PolyBlepSquare(phSub, dt * 0.5f) * 0.22f;
                        }
                        else
                        {
                            osc = PolyBlepSaw(phLead, dt) * 0.52f
                                + PolyBlepSquare(phLead, dt) * 0.24f
                                + Mathf.Sin(2f * Mathf.PI * phSub) * 0.24f;
                        }

                        if (harmFreq > 0f && !chip)
                        {
                            float dtH = (harmFreq * vib) / SampleRate;
                            phHarm += dtH; if (phHarm >= 1f) phHarm -= 1f;
                            osc += PolyBlepSaw(phHarm, dtH) * 0.30f;
                        }

                        float cutHz = Mathf.Clamp(baseCutHz + 1800f * env, 800f, 6500f);
                        float shaped = SvfLowPass(osc, cutHz, 1.15f, ref ic1, ref ic2);
                        float sample = (float)Math.Tanh(shaped * 1.45) * env * vel;

                        int idx0 = (start + i) % s.Length;
                        int idxEcho = (start + i + delaySpl) % s.Length;
                        float duck = (idx0 < sc.Length) ? (1f - 0.45f * sc[idx0]) : 1f;
                        s[idx0] += sample * duck;
                        s[idxEcho] += sample * 0.26f * duck;
                    }
                }
            }
        }

        /// <summary>
        /// Chœur Arcanotech à formants vocaliques (morphing continu entre voyelles "Ah" et "Oh").
        /// </summary>
        private static void AddChoirBed(float[] s, float[] sc, GenerativeMusicParams p,
            GenerativeSectionRole role, int[][] chords, float chordDur, float vol)
        {
            int chordSamples = Math.Max(1, (int)(chordDur * SampleRate));
            float f1Ic1 = 0f, f1Ic2 = 0f, f2Ic1 = 0f, f2Ic2 = 0f, f3Ic1 = 0f, f3Ic2 = 0f;

            for (int i = 0; i < s.Length; i++)
            {
                int c = Math.Min(chords.Length - 1, i / chordSamples);
                float localT = (i - c * chordSamples) / (float)SampleRate;
                float atk = Smooth01(localT / 0.52f);
                float rel = Smooth01((chordDur - localT) / 0.58f);
                float t = i / (float)SampleRate;

                float vib = 1f + 0.0036f * Mathf.Sin(2f * Mathf.PI * 5.2f * t);
                var chord = chords[c];
                float glottal = 0f;

                for (int k = 0; k < chord.Length; k++)
                {
                    float f = MidiFreq(chord[k] + (k % 2 == 0 ? 12 : 0)) * vib;
                    float dt = f / SampleRate;
                    float ph = (f * localT + 0.21f * k) % 1f;
                    glottal += PolyBlepSaw(ph, dt) * 0.65f + Mathf.Sin(2f * Mathf.PI * ph) * 0.35f;
                }
                glottal /= Mathf.Max(1f, chord.Length * 0.85f);

                // Morphing lent de voyelle : "Oh" (450 / 800 / 2600 Hz) <-> "Ah" (720 / 1150 / 2500 Hz)
                float vowel = 0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 0.14f * t);
                float form1 = Mathf.Lerp(450f, 720f, vowel);
                float form2 = Mathf.Lerp(800f, 1150f, vowel);
                float form3 = 2520f;

                float v1 = SvfBandPass(glottal, form1, 4.2f, ref f1Ic1, ref f1Ic2);
                float v2 = SvfBandPass(glottal, form2, 4.8f, ref f2Ic1, ref f2Ic2);
                float v3 = SvfBandPass(glottal, form3, 5.5f, ref f3Ic1, ref f3Ic2);

                float choir = (v1 * 1.0f + v2 * 0.72f + v3 * 0.28f + glottal * 0.18f);
                float duck = (i < sc.Length) ? (1f - 0.50f * sc[i]) : 1f;
                s[i] += (float)Math.Tanh(choir * 1.55) * atk * rel * vol * 1.65f * duck;
            }
        }

        // ================= FX DE STRUCTURE & MASTERING =================

        private static void AddSectionFx(float[] s, float[] sc, System.Random rng,
            GenerativeMusicParams p, GenerativeSectionRole role, int keyRoot, float barSec, float beatSec, bool isBreak)
        {
            ImpactHit(s, rng, 0f, keyRoot, 0.78f + 0.30f * p.intensity01);
            float riseStart = barSec * (p.bars / 2 - 1);
            RiserFx(s, rng, riseStart, barSec, 0.10f + 0.10f * p.intensity01);

            float fillStart = barSec * (p.bars - 1);
            if (!isBreak)
            {
                SnareRoll(s, rng, fillStart + barSec * 0.5f, barSec * 0.5f, 0.34f + 0.34f * p.intensity01);
                TomDescent(s, rng, fillStart + barSec * 0.6f, 0.35f);
            }
            if (role == GenerativeSectionRole.BridgeB || role == GenerativeSectionRole.Climax)
                RiserFx(s, rng, fillStart, barSec * 0.92f, 0.12f + 0.08f * p.tension01);
            DownlifterFx(s, rng, fillStart + barSec * 0.85f, barSec * 0.15f, 0.12f);
        }

        private static void AddStrikeAccent(float[] s, float[] sc, System.Random rng,
            GenerativeMusicParams p, int keyRoot, float barSec, float beatSec, float strike)
        {
            float tt = barSec * (p.bars / 2);
            int start = (int)(tt * SampleRate);
            if (start < 0 || start >= s.Length) return;
            int len = Math.Min((int)(0.9f * SampleRate), s.Length - start);
            float fBase = Mathf.Clamp(MidiFreq(keyRoot), 36f, 55f);
            float phase = 0f;
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)SampleRate;
                float f = fBase * (1f + 1.8f * Mathf.Exp(-3.2f * t));
                phase += 2f * Mathf.PI * f / SampleRate;
                s[start + i] += Mathf.Sin(phase) * Mathf.Exp(-4f * t) * strike * 0.72f;
            }
            TaikoHit(s, rng, tt, strike * 0.60f);
            CrashHit(s, rng, tt, strike * 0.35f);
        }

        private static void ImpactHit(float[] s, System.Random rng, float tSec, int keyRoot, float vol)
        {
            int start = (int)(tSec * SampleRate);
            if (start < 0 || start >= s.Length) return;
            int len = Math.Min((int)(1.2f * SampleRate), s.Length - start);
            float fBase = Mathf.Clamp(MidiFreq(keyRoot), 38f, 55f);
            float lp = 0f, phase = 0f;
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)SampleRate;
                float env = Mathf.Exp(-4.5f * t) * Smooth01(t / 0.008f);
                float f = fBase * (1f + 0.9f * Mathf.Exp(-6f * t));
                phase += 2f * Mathf.PI * f / SampleRate;
                float sub = Mathf.Sin(phase);
                float nz = (float)(rng.NextDouble() * 2 - 1);
                lp += 0.08f * (nz - lp);
                s[start + i] += (sub * 1.1f + lp * 0.68f) * env * vol;
            }
        }

        private static void RiserFx(float[] s, System.Random rng, float tSec, float durSec, float vol)
        {
            int start = (int)(tSec * SampleRate);
            if (start < 0 || start >= s.Length) return;
            int len = Math.Min((int)(durSec * SampleRate), s.Length - start);
            float ic1 = 0f, ic2 = 0f;
            for (int i = 0; i < len; i++)
            {
                float k = (float)i / Math.Max(1, len);
                float nz = (float)(rng.NextDouble() * 2 - 1);
                float cutHz = Mathf.Lerp(260f, 3800f, k * k);
                float swept = SvfBandPass(nz, cutHz, 2.8f, ref ic1, ref ic2);
                s[start + i] += swept * k * k * vol * 1.85f;
            }
        }

        private static void DownlifterFx(float[] s, System.Random rng, float tSec, float durSec, float vol)
        {
            int start = (int)(tSec * SampleRate);
            if (start < 0 || start >= s.Length) return;
            int len = Math.Min((int)(durSec * SampleRate), s.Length - start);
            float lp = 0f;
            for (int i = 0; i < len; i++)
            {
                float k = 1f - (float)i / Math.Max(1, len);
                float nz = (float)(rng.NextDouble() * 2 - 1);
                lp += Mathf.Lerp(0.03f, 0.4f, k) * (nz - lp);
                s[start + i] += lp * k * vol * 1.4f;
            }
        }

        private static void AddSubDrone(float[] s, float[] sc, int keyRoot, float vol)
        {
            float f = MidiFreq(Mathf.Clamp(keyRoot, 28, 41));
            for (int i = 0; i < s.Length; i++)
            {
                float t = i / (float)SampleRate;
                float lfo = 0.78f + 0.22f * Mathf.Sin(2f * Mathf.PI * 0.14f * t);
                float duck = (i < sc.Length) ? (1f - 0.65f * sc[i]) : 1f;
                s[i] += Mathf.Sin(2f * Mathf.PI * f * t) * lfo * vol * duck;
            }
        }

        private static void AddAirShimmer(float[] s, System.Random rng, GenerativeMusicParams p, int keyRoot, float vol)
        {
            float f = MidiFreq(keyRoot + 36);
            float fFifth = f * 1.4983f;
            for (int i = 0; i < s.Length; i++)
            {
                float t = i / (float)SampleRate;
                float bell = Mathf.Sin(2f * Mathf.PI * f * t + 0.35f * Mathf.Sin(t * 4.3f)) * 0.6f
                           + Mathf.Sin(2f * Mathf.PI * fFifth * t) * 0.4f;
                s[i] += bell * vol * (0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 0.09f * t));
            }
        }

        // ================= PRIMITIVES DSP (POLYBLEP & FILTRE SVF) =================

        private static float PolyBlep(float t, float dt)
        {
            if (dt <= 0f) return 0f;
            if (t < dt)
            {
                t /= dt;
                return t + t - t * t - 1f;
            }
            if (t > 1f - dt)
            {
                t = (t - 1f) / dt;
                return t * t + t + t + 1f;
            }
            return 0f;
        }

        private static float PolyBlepSaw(float phase01, float dt)
        {
            float v = 2f * phase01 - 1f;
            return v - PolyBlep(phase01, dt);
        }

        private static float PolyBlepSquare(float phase01, float dt)
        {
            float v = phase01 < 0.5f ? 1f : -1f;
            v += PolyBlep(phase01, dt);
            v -= PolyBlep((phase01 + 0.5f) % 1f, dt);
            return v;
        }

        /// <summary>Filtre State Variable (Cytomic SVF) passe-bas 2-pôles stable à 22050 Hz.</summary>
        private static float SvfLowPass(float input, float cutoffHz, float q, ref float ic1eq, ref float ic2eq)
        {
            float fc = Mathf.Clamp(cutoffHz, 40f, SampleRate * 0.44f);
            float g = Mathf.Tan(Mathf.PI * fc / SampleRate);
            float k = 1f / Mathf.Max(0.5f, q);
            float a1 = 1f / (1f + g * (g + k));
            float a2 = g * a1;
            float a3 = g * a2;
            float v3 = input - ic2eq;
            float v1 = a1 * ic1eq + a2 * v3;
            float v2 = ic2eq + a2 * ic1eq + a3 * v3;
            ic1eq = 2f * v1 - ic1eq;
            ic2eq = 2f * v2 - ic2eq;
            return v2;
        }

        /// <summary>Filtre State Variable (Cytomic SVF) passe-bande 2-pôles pour formants vocaliques et risers.</summary>
        private static float SvfBandPass(float input, float cutoffHz, float q, ref float ic1eq, ref float ic2eq)
        {
            float fc = Mathf.Clamp(cutoffHz, 60f, SampleRate * 0.44f);
            float g = Mathf.Tan(Mathf.PI * fc / SampleRate);
            float k = 1f / Mathf.Max(0.5f, q);
            float a1 = 1f / (1f + g * (g + k));
            float a2 = g * a1;
            float a3 = g * a2;
            float v3 = input - ic2eq;
            float v1 = a1 * ic1eq + a2 * v3;
            float v2 = ic2eq + a2 * ic1eq + a3 * v3;
            ic1eq = 2f * v1 - ic1eq;
            ic2eq = 2f * v2 - ic2eq;
            return v1;
        }

        private static bool EuclidHit(int step, int steps, int pulses)
        {
            int pos = ((step % steps) + steps) % steps;
            return ((pos * pulses) % steps) < pulses;
        }

        private static float MidiFreq(int midi) => 440f * Mathf.Pow(2f, (midi - 69) / 12f);

        private static float Smooth01(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        /// <summary>
        /// Jointure de boucle sans clic qui préserve intégralement l'attaque du temps 1 (s[0]) :
        /// micro-rampe d'entrée de 1.5ms et fondu de la queue de boucle (s[n-fade..n-1]) vers s[0].
        /// </summary>
        private static void LoopCrossfade(float[] s, int fade)
        {
            int n = s.Length;
            fade = Math.Min(fade, n / 8);
            if (fade < 16) return;
            int headRamp = Math.Min(32, fade / 4);
            for (int i = 0; i < headRamp; i++)
            {
                s[i] *= (float)i / headRamp;
            }
            for (int i = 0; i < fade; i++)
            {
                float t = Smooth01((float)i / fade);
                int j = n - fade + i;
                s[j] *= (1f - t);
            }
        }

        private static void Master(float[] s, float targetPeak)
        {
            // 1. Suppression d'offset DC (passe-haut à ~18 Hz) pour un headroom symétrique
            float dcX = 0f, dcY = 0f;
            float max = 0.0001f;
            for (int i = 0; i < s.Length; i++)
            {
                float x = s[i];
                dcY = x - dcX + 0.995f * dcY;
                dcX = x;
                s[i] = dcY;
                max = Math.Max(max, Math.Abs(dcY));
            }
            // 2. Normalisation + soft-clipping analogique
            float scale = Mathf.Min(3.5f, targetPeak / max);
            for (int i = 0; i < s.Length; i++)
                s[i] = (float)Math.Tanh(s[i] * scale * 0.95);
        }
    }
}

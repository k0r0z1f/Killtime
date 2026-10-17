using System;
using UnityEngine;

namespace Killtime.Audio
{
    public static partial class GenerativeMusicEngine
    {
        // ================= BATTERIE & PERCUSSIONS =================

        private static void AddGenerativeDrums(float[] s, float[] sc, System.Random rng,
            GenerativeMusicParams p, GenerativeDrumStyle style, GenerativeSectionRole role,
            int keyRoot, float beatSec, float barSec, float vol)
        {
            if (vol < 0.03f) return;
            int bars = p.bars;
            int stepsPerBar = GetStepsPerBar(p.beatsPerBar);
            int fullBeats = Mathf.Max(3, stepsPerBar / 4);
            float sixteenth = beatSec / 4f;
            float kickBaseFreq = Mathf.Clamp(MidiFreq(keyRoot), 41f, 55f); // accorde le sub du kick sur la tonique

            for (int bar = 0; bar < bars; bar++)
            {
                float barT = bar * barSec;
                bool isFillBar = (bar == bars - 1) || (bar == bars / 2 - 1 && role == GenerativeSectionRole.Climax);
                bool halfDropped = (role == GenerativeSectionRole.Breakdown && bar < 2);

                // Signatures impaires (5/4 = 3+2 temps, 7/8 = 2+2+3 croches) : ostinato asymétrique dédié
                if (!halfDropped && (stepsPerBar == 20 || stepsPerBar == 14))
                {
                    if (stepsPerBar == 20) // 5/4 (12 + 8 doubles-croches)
                    {
                        KickHit(s, sc, rng, barT, vol, kickBaseFreq);
                        SnareHit(s, rng, barT + beatSec * 1.5f, vol * 0.88f);
                        KickHit(s, sc, rng, barT + beatSec * 3.0f, vol * 0.92f, kickBaseFreq); // appui du groupe de 2
                        SnareHit(s, rng, barT + beatSec * 4.0f, vol * 0.94f);
                        if (p.intensity01 > 0.55f)
                            TaikoHit(s, rng, barT + beatSec * 2.5f, vol * 0.50f);
                    }
                    else // 7/8 (4 + 4 + 6 doubles-croches)
                    {
                        KickHit(s, sc, rng, barT, vol, kickBaseFreq);
                        SnareHit(s, rng, barT + sixteenth * 4f, vol * 0.88f);
                        KickHit(s, sc, rng, barT + sixteenth * 8f, vol * 0.92f, kickBaseFreq);
                        SnareHit(s, rng, barT + sixteenth * 11f, vol * 0.92f);
                    }
                    if (isFillBar) SnareRoll(s, rng, barT + barSec * 0.72f, barSec * 0.28f, vol * 0.88f);
                    SixteenthHats(s, rng, barT, sixteenth, stepsPerBar, vol * 0.31f, p);
                    if (bar == 0 || (bar == bars / 2 && role == GenerativeSectionRole.Climax))
                        CrashHit(s, rng, barT, vol * 0.52f);
                    continue;
                }

                switch (style)
                {
                    case GenerativeDrumStyle.WarEnsemble:
                        if (!halfDropped)
                        {
                            TaikoHit(s, rng, barT, vol * 1.0f);
                            if (bar % 2 == 1 || role == GenerativeSectionRole.Climax)
                                TaikoHit(s, rng, barT + beatSec * 2f, vol * 0.78f);
                            if (p.intensity01 > 0.3f && fullBeats >= 4)
                                TaikoHit(s, rng, barT + beatSec * 3.5f, vol * 0.52f);
                            if (p.intensity01 > 0.52f)
                                TaikoHit(s, rng, barT + beatSec * 1.5f, vol * 0.44f);
                        }
                        ShakerRun(s, rng, barT, barSec, sixteenth, stepsPerBar, vol * 0.30f, p);
                        if (isFillBar) TomDescent(s, rng, barT + barSec * 0.75f, vol * 0.85f);
                        break;

                    case GenerativeDrumStyle.HalfTimeTrap:
                        if (!halfDropped)
                        {
                            KickHit(s, sc, rng, barT, vol, kickBaseFreq);
                            if (p.intensity01 > 0.45f)
                                KickHit(s, sc, rng, barT + beatSec * 1.75f, vol * 0.78f, kickBaseFreq);
                            if (fullBeats >= 4 && rng.NextDouble() < 0.45 + p.chaos01 * 0.35)
                                KickHit(s, sc, rng, barT + beatSec * 2.75f, vol * 0.82f, kickBaseFreq);
                            SnareHit(s, rng, barT + beatSec * 2f, vol * 0.96f);
                            if (bar % 2 == 1 && fullBeats >= 4)
                                SnareHit(s, rng, barT + beatSec * 3.75f, vol * 0.42f);
                            if (isFillBar) SnareRoll(s, rng, barT + barSec * 0.5f, barSec * 0.5f, vol * 0.82f);
                        }
                        TripletHats(s, rng, barT, beatSec, fullBeats, vol * 0.35f, p);
                        if (bar == 0 || (bar == bars / 2 && role == GenerativeSectionRole.Climax))
                            CrashHit(s, rng, barT, vol * 0.50f);
                        break;

                    case GenerativeDrumStyle.DoubleTime:
                        if (!halfDropped)
                        {
                            KickHit(s, sc, rng, barT, vol, kickBaseFreq);
                            KickHit(s, sc, rng, barT + beatSec * 1.5f, vol * 0.82f, kickBaseFreq);
                            if (fullBeats >= 4)
                            {
                                if (p.intensity01 > 0.55f)
                                    KickHit(s, sc, rng, barT + beatSec * 2.5f, vol * 0.88f, kickBaseFreq);
                                else
                                    KickHit(s, sc, rng, barT + beatSec * 2f, vol * 0.88f, kickBaseFreq);
                                if (role == GenerativeSectionRole.Climax && bar % 2 == 1)
                                    KickHit(s, sc, rng, barT + beatSec * 3.5f, vol * 0.76f, kickBaseFreq);
                                SnareHit(s, rng, barT + beatSec * 3f, vol * 0.94f);
                                SnareHit(s, rng, barT + beatSec * 2.25f, vol * 0.34f);
                                if (p.chaos01 > 0.3f)
                                    SnareHit(s, rng, barT + beatSec * 3.75f, vol * 0.38f);
                            }
                            SnareHit(s, rng, barT + beatSec, vol * 0.92f);
                            if (isFillBar) SnareRoll(s, rng, barT + barSec * 0.75f, barSec * 0.25f, vol);
                        }
                        SixteenthHats(s, rng, barT, sixteenth, stepsPerBar, vol * 0.32f, p);
                        if (bar == 0 || (bar == bars / 2 && role == GenerativeSectionRole.Climax))
                            CrashHit(s, rng, barT, vol * 0.55f);
                        break;

                    case GenerativeDrumStyle.Blast:
                        if (!halfDropped)
                        {
                            for (int b = 0; b < fullBeats; b++)
                            {
                                KickHit(s, sc, rng, barT + b * beatSec, vol * (b == 0 ? 1f : 0.82f), kickBaseFreq);
                                if (b % 2 == 1 && p.intensity01 > 0.75f)
                                    KickHit(s, sc, rng, barT + b * beatSec + beatSec * 0.75f, vol * 0.68f, kickBaseFreq);
                                SnareHit(s, rng, barT + b * beatSec + beatSec * 0.5f, vol * 0.78f);
                            }
                            if (isFillBar)
                            {
                                SnareRoll(s, rng, barT + barSec * 0.75f, barSec * 0.25f, vol);
                                TomDescent(s, rng, barT + barSec * 0.75f, vol * 0.70f);
                            }
                        }
                        SixteenthHats(s, rng, barT, sixteenth, stepsPerBar, vol * 0.28f, p);
                        if (bar % 2 == 0) CrashHit(s, rng, barT, vol * 0.42f);
                        break;

                    default: // TribalTechno
                        if (!halfDropped)
                        {
                            for (int b = 0; b < fullBeats; b++)
                                KickHit(s, sc, rng, barT + b * beatSec, vol * (b == 0 ? 1f : 0.86f), kickBaseFreq);
                            SnareHit(s, rng, barT + beatSec, vol * 0.86f);
                            if (fullBeats >= 4)
                            {
                                SnareHit(s, rng, barT + beatSec * 3f, vol * 0.88f);
                                TaikoHit(s, rng, barT + beatSec * 2.5f, vol * 0.42f);
                                if (p.chaos01 > 0.35f && bar % 2 == 1)
                                    TaikoHit(s, rng, barT + beatSec * 3.75f, vol * 0.48f);
                            }
                            if (isFillBar) SnareRoll(s, rng, barT + barSec * 0.75f, barSec * 0.25f, vol * 0.90f);
                        }
                        else
                        {
                            ShakerRun(s, rng, barT, barSec, sixteenth, stepsPerBar, vol * 0.36f, p);
                        }
                        OffbeatHats(s, rng, barT, beatSec, fullBeats, vol * 0.34f, p);
                        if (bar == 0 || (bar == bars / 2 && role == GenerativeSectionRole.Climax))
                            CrashHit(s, rng, barT, vol * 0.54f);
                        break;
                }
            }
        }

        private static void KickHit(float[] s, float[] sc, System.Random rng, float tSec, float vol, float subFreq = 45f)
        {
            int start = (int)(tSec * SampleRate);
            if (start >= s.Length) return;
            // Temps 1 verrouillé au sample près ; humanisation très fine (±2.5ms) sur les autres
            if (tSec > 0.01f)
                start += (int)((rng.NextDouble() * 2 - 1) * 0.0025 * SampleRate);
            start = Mathf.Clamp(start, 0, s.Length - 1);
            int len = Math.Min((int)(0.30f * SampleRate), s.Length - start);
            float phase = 0f;
            float vel = vol * (0.91f + (float)rng.NextDouble() * 0.16f);
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)SampleRate;
                // Enveloppe de pitch à 2 étages : punch initial rapide (beater) + corps sub accordé
                float f = subFreq + 135f * Mathf.Exp(-38f * t) + 28f * Mathf.Exp(-9f * t);
                phase += 2f * Mathf.PI * f / SampleRate;
                float body = Mathf.Sin(phase) * Mathf.Exp(-7.8f * t);
                float harmonic = Mathf.Sin(phase * 2f) * Mathf.Exp(-24f * t) * 0.25f;
                float click = (float)(rng.NextDouble() * 2 - 1) * Mathf.Exp(-70f * t) * 0.28f;
                s[start + i] += (float)Math.Tanh((body + harmonic + click) * 1.65) * vel;
                int idx = start + i;
                if (idx < sc.Length) sc[idx] = Mathf.Max(sc[idx], Mathf.Exp(-13.5f * t));
            }
        }

        private static void SnareHit(float[] s, System.Random rng, float tSec, float vol)
        {
            int start = (int)(tSec * SampleRate) + (int)((rng.NextDouble() * 2 - 1) * 0.0025 * SampleRate);
            start = Mathf.Clamp(start, 0, s.Length - 1);
            int len = Math.Min((int)(0.23f * SampleRate), s.Length - start);
            float lp = 0f, hp = 0f;
            float vel = vol * (0.90f + (float)rng.NextDouble() * 0.18f);
            float ph1 = 0f, ph2 = 0f;
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)SampleRate;
                float f1 = 182f + 60f * Mathf.Exp(-35f * t);
                float f2 = 318f + 45f * Mathf.Exp(-35f * t);
                ph1 += 2f * Mathf.PI * f1 / SampleRate;
                ph2 += 2f * Mathf.PI * f2 / SampleRate;
                float tone = (Mathf.Sin(ph1) * 0.68f + Mathf.Sin(ph2) * 0.32f) * Mathf.Exp(-16f * t);

                float nz = (float)(rng.NextDouble() * 2 - 1);
                lp += 0.45f * (nz - lp);
                hp += 0.08f * (lp - hp);
                float wires = (lp - hp) * Mathf.Exp(-11.5f * t);
                s[start + i] += (float)Math.Tanh((tone * 0.52f + wires * 0.85f) * 1.35) * vel * 0.86f;
            }
        }

        private static void SnareRoll(float[] s, System.Random rng, float tSec, float durSec, float vol)
        {
            int hits = Math.Max(4, (int)(durSec / 0.06f));
            for (int h = 0; h < hits; h++)
            {
                float k = (float)h / hits;
                float tt = tSec + k * durSec;
                SnareHit(s, rng, tt, vol * (0.38f + 0.62f * k * k));
            }
        }

        private static void SixteenthHats(float[] s, System.Random rng, float barT, float sixteenth, int stepsPerBar, float vol, GenerativeMusicParams p)
        {
            float swing = (p.preset == GenerativeStylePreset.Electronic || p.preset == GenerativeStylePreset.DrumAndBass)
                ? 0.03f : 0.09f;
            for (int h = 0; h < stepsPerBar; h++)
            {
                if (p.intensity01 < 0.5f && h % 2 == 1 && rng.NextDouble() < 0.55) continue;
                float swingOffset = (h % 2 == 1) ? sixteenth * swing : 0f;
                bool openHat = (h == 2 || h == 10 || h == stepsPerBar - 2) && p.intensity01 > 0.48f;
                float gain = (h % 4 == 0) ? 0.95f : (h % 2 == 0 ? 0.72f : 0.44f);
                HatTick(s, rng, barT + h * sixteenth + swingOffset, vol * gain, openHat);
            }
        }

        private static void OffbeatHats(float[] s, System.Random rng, float barT, float beatSec, int fullBeats, float vol, GenerativeMusicParams p)
        {
            for (int b = 0; b < fullBeats; b++)
            {
                // Contre-temps ouvert caractéristique techno/electro
                HatTick(s, rng, barT + b * beatSec + beatSec * 0.5f, vol * 0.95f, open: true);
                if (p.intensity01 > 0.45f)
                    HatTick(s, rng, barT + b * beatSec, vol * 0.45f, open: false);
                if (p.intensity01 > 0.60f)
                    HatTick(s, rng, barT + b * beatSec + beatSec * 0.25f, vol * 0.48f, open: false);
                if (p.intensity01 > 0.76f)
                    HatTick(s, rng, barT + b * beatSec + beatSec * 0.75f, vol * 0.52f, open: false);
            }
        }

        private static void TripletHats(float[] s, System.Random rng, float barT, float beatSec, int fullBeats, float vol, GenerativeMusicParams p)
        {
            float trip = beatSec / 3f;
            for (int b = 0; b < fullBeats; b++)
            {
                for (int k = 0; k < 3; k++)
                {
                    if (k == 1 && rng.NextDouble() < 0.35 - p.chaos01 * 0.2) continue;
                    bool open = (b == 1 || b == fullBeats - 1) && k == 2;
                    HatTick(s, rng, barT + b * beatSec + k * trip, vol * (k == 0 ? 0.92f : 0.56f), open);
                }
            }
        }

        /// <summary>
        /// Charleston à synthèse métallique inharmonique + bruit passe-haut (fermée ou ouverte).
        /// </summary>
        private static void HatTick(float[] s, System.Random rng, float tSec, float vol, bool open = false)
        {
            int start = (int)(tSec * SampleRate);
            if (start < 0 || start >= s.Length) return;
            float dur = open ? 0.13f : 0.045f;
            float decay = open ? 20f : 58f;
            int len = Math.Min((int)(dur * SampleRate), s.Length - start);
            float hp = 0f;
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)SampleRate;
                float nz = (float)(rng.NextDouble() * 2 - 1);
                hp += 0.11f * (nz - hp);
                // Partiels métalliques inharmoniques (ratios type 808/cymbale)
                float metal = Mathf.Sin(2f * Mathf.PI * 3420f * t) * 0.28f
                            + Mathf.Sin(2f * Mathf.PI * 4970f * t) * 0.24f
                            + Mathf.Sin(2f * Mathf.PI * 6850f * t) * 0.18f;
                s[start + i] += ((nz - hp) * 0.68f + metal * 0.32f) * Mathf.Exp(-decay * t) * vol;
            }
        }

        private static void ShakerRun(float[] s, System.Random rng, float barT, float barSec, float sixteenth, int stepsPerBar, float vol, GenerativeMusicParams p)
        {
            int eighthSteps = stepsPerBar / 2;
            for (int h = 0; h < eighthSteps; h++)
            {
                float tt = barT + h * sixteenth * 2f;
                int start = (int)(tt * SampleRate);
                if (start < 0 || start >= s.Length) continue;
                int len = Math.Min((int)(0.09f * SampleRate), s.Length - start);
                float lp = 0f, hp = 0f;
                float accent = (h % 2 == 0) ? 1f : 0.68f;
                for (int i = 0; i < len; i++)
                {
                    float t = (float)i / len;
                    float nz = (float)(rng.NextDouble() * 2 - 1);
                    lp += 0.32f * (nz - lp);
                    hp += 0.09f * (lp - hp);
                    s[start + i] += (lp - hp) * Mathf.Sin(Mathf.PI * t) * vol * accent;
                }
            }
        }

        private static void TaikoHit(float[] s, System.Random rng, float tSec, float vol)
        {
            int start = (int)(tSec * SampleRate);
            if (start < 0 || start >= s.Length) return;
            int len = Math.Min((int)(0.42f * SampleRate), s.Length - start);
            float lp = 0f;
            float phase = 0f;
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)SampleRate;
                float pitch = 62f * Mathf.Exp(-9f * t) + 36f;
                phase += 2f * Mathf.PI * pitch / SampleRate;
                float sub = (Mathf.Sin(phase) + 0.30f * Mathf.Sin(phase * 1.58f) * Mathf.Exp(-14f * t)) * Mathf.Exp(-5.2f * t);
                float nz = (float)(rng.NextDouble() * 2 - 1);
                lp += 0.18f * (nz - lp);
                s[start + i] += (float)Math.Tanh((sub * 1.15f + lp * Mathf.Exp(-22f * t) * 0.65f) * 1.25) * vol;
            }
        }

        private static void TomDescent(float[] s, System.Random rng, float tSec, float vol)
        {
            float[] freqs = { 210f, 168f, 138f, 102f };
            for (int k = 0; k < freqs.Length; k++)
            {
                float tt = tSec + k * 0.105f;
                int start = (int)(tt * SampleRate);
                if (start < 0 || start >= s.Length) continue;
                int len = Math.Min((int)(0.22f * SampleRate), s.Length - start);
                float phase = 0f;
                for (int i = 0; i < len; i++)
                {
                    float t = i / (float)SampleRate;
                    float f = freqs[k] * (1f + 0.35f * Mathf.Exp(-22f * t));
                    phase += 2f * Mathf.PI * f / SampleRate;
                    s[start + i] += Mathf.Sin(phase) * Mathf.Exp(-10.5f * t) * vol * 0.75f;
                }
            }
        }

        private static void CrashHit(float[] s, System.Random rng, float tSec, float vol)
        {
            int start = (int)(tSec * SampleRate);
            if (start < 0 || start >= s.Length) return;
            int len = Math.Min((int)(1.2f * SampleRate), s.Length - start);
            float hp = 0f;
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)SampleRate;
                float nz = (float)(rng.NextDouble() * 2 - 1);
                hp += 0.06f * (nz - hp);
                float shimmer = Mathf.Sin(2f * Mathf.PI * 4850f * t) * 0.22f
                              + Mathf.Sin(2f * Mathf.PI * 6720f * t) * 0.16f
                              + Mathf.Sin(2f * Mathf.PI * 8430f * t) * 0.10f;
                s[start + i] += ((nz - hp) * 0.68f + shimmer * Mathf.Exp(-3.8f * t)) * Mathf.Exp(-2.9f * t) * vol;
            }
        }
    }
}

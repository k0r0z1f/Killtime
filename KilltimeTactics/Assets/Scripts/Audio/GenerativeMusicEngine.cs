using System;
using UnityEngine;

namespace Killtime.Audio
{
    /// <summary>
    /// Styles de batterie du moteur génératif (le mode Auto choisit depuis l'énergie/tension).
    /// </summary>
    public enum GenerativeDrumStyle
    {
        Auto = 0,
        WarEnsemble = 1,   // taikos lourds, peu de kick électronique, très organique
        TribalTechno = 2,  // four-on-floor + percussions syncopées
        DoubleTime = 3,    // double-kick / dnb-ish, hats 16e
        HalfTimeTrap = 4,  // halftime lourd, snare temps 3, hats en triolets fantômes
        Blast = 5          // blast / djent, max densité
    }

    /// <summary>Modes mélodiques (mineurs, pour rester Killtime / sombre).</summary>
    public enum GenerativeScale
    {
        NaturalMinor = 0,
        Dorian = 1,
        Phrygian = 2,
        HarmonicMinor = 3
    }

    /// <summary>
    /// Preset de style du moteur génératif. Auto = comportement actuel
    /// (piloté par énergie/tension). Les autres presets biaisent BPM,
    /// batterie, gamme et timbres tout en gardant la réactivité combat.
    /// </summary>
    public enum GenerativeStylePreset
    {
        Auto = 0,
        Electronic = 1,
        DrumAndBass = 2,
        Retro16Bit = 3,
        ModernCinematic = 4
    }

    public enum ProceduralStemType
    {
        Drums = 0,
        Bass = 1,
        Pad = 2,
        Arp = 3,
        Lead = 4,
        Choir = 5,
        Staccato = 6,
        SubAir = 7
    }

    public enum StemOverrideState
    {
        Auto = 0,
        ForceActive = 1,
        Muted = 2
    }

    /// <summary>
    /// Paramètres d'une boucle générative. Chaque (seed, generation) donne une
    /// variation inédite mais cohérente (même famille motivique, même tonalité
    /// sauf modulation de climax). Le scheduler incrémente generation à chaque
    /// boucle => musique infinie, jamais deux fois la même.
    /// </summary>
    [Serializable]
    public struct GenerativeMusicParams
    {
        public int seed;            // base fixe par campagne/scène (garde l'identité)
        public int generation;      // incrémenté à chaque boucle (variation infinie)
        public float intensity01;   // 0..1 énergie combat (densité, couches)
        public float tension01;     // 0..1 danger (PV bas, dernier carré)
        public float brightness01;  // 0..1 clarté du mix (cutoff lead/pad)
        public float chaos01;       // 0..1 ornementation / surprises rythmiques
        public int rootMidi;        // fondamentale (ex 33 = A1). 26..45 conseillé
        public GenerativeScale scale;
        public GenerativeDrumStyle drumStyle;
        public GenerativeStylePreset preset; // Auto = pilotage énergie/tension actuel
        public float bpm;           // 96..180, morphé par l'énergie
        public int bars;            // 4..16, 8 par défaut
        public float strike01;      // accent réactif ponctuel (coup critique / kill)
        public int stemOverrides;   // Masque 16-bit des 8 stems (2 bits par stem : 0=Auto, 1=ForceActive, 2=Muted)

        public static GenerativeMusicParams DefaultCombat(int seed = 1337)
        {
            return new GenerativeMusicParams
            {
                seed = seed,
                generation = 0,
                intensity01 = 0.6f,
                tension01 = 0.4f,
                brightness01 = 0.55f,
                chaos01 = 0.35f,
                rootMidi = 33,
                scale = GenerativeScale.NaturalMinor,
                drumStyle = GenerativeDrumStyle.Auto,
                preset = GenerativeStylePreset.Auto,
                bpm = 128f,
                bars = 8,
                strike01 = 0f
            };
        }

        public GenerativeMusicParams WithVariation(int newGeneration, float intensity, float tension, float strike = 0f)
        {
            var c = this;
            c.generation = newGeneration;
            c.intensity01 = Mathf.Clamp01(intensity);
            c.tension01 = Mathf.Clamp01(tension);
            c.strike01 = Mathf.Clamp01(strike);
            return c;
        }
    }

    /// <summary>
    /// Moteur de musique générative infinie : AUCUNE chanson préfaite.
    /// Compose barre par barre (harmonie fonctionnelle, motif call-response,
    /// batterie euclidienne humanisée, structure A/B/break, fills, risers).
    ///
    /// Contrats :
    /// - déterministe par (seed, generation) : rejouable, testable ;
    /// - bouclable sans clic (crossfade de boucle + delays synchronisés au beat) ;
    /// - mono 22050 Hz comme le reste de l'usine (WebGL léger) ;
    /// - pur C# + Mathf : pas d'alloc frame, pas de dépendance Play.
    /// </summary>
    public static class GenerativeMusicEngine
    {
        public const int SampleRate = 22050;

        public static float EstimateDuration(GenerativeMusicParams p)
        {
            int bars = Mathf.Clamp(p.bars, 4, 16);
            float bpm = Mathf.Clamp(p.bpm, 90f, 180f);
            return bars * 4f * 60f / bpm;
        }

        public static float[] Render(GenerativeMusicParams p)
        {
            p.intensity01 = Mathf.Clamp01(p.intensity01);
            p.tension01 = Mathf.Clamp01(p.tension01);
            p.brightness01 = Mathf.Clamp01(p.brightness01);
            p.chaos01 = Mathf.Clamp01(p.chaos01);
            p.bpm = Mathf.Clamp(p.bpm, 90f, 180f);
            p.bars = Mathf.Clamp(p.bars, 4, 16);
            p.rootMidi = Mathf.Clamp(p.rootMidi, 24, 48);
            bool chip = (p.preset == GenerativeStylePreset.Retro16Bit);

            int rngSeed = unchecked(p.seed * 73856093 ^ (p.generation + 1) * 19349663 ^ 83492791);
            var rng = new System.Random(rngSeed);

            float bpm = p.bpm;
            float beatSec = 60f / bpm;
            float barSec = beatSec * 4f;
            float totalSec = barSec * p.bars;
            int n = (int)(SampleRate * totalSec);
            float[] s = new float[n];
            float[] sidechain = new float[n];

            // --- 1. Tonalité / modulation de climax toutes les 4 générations ---
            int keyShift = 0;
            if (p.tension01 > 0.62f && (p.generation % 4 == 3))
                keyShift = (rng.NextDouble() < 0.5) ? 1 : 2; // montée dramatique
            else if (p.generation % 8 == 7 && rng.NextDouble() < 0.4)
                keyShift = -2; // respiration : redescente
            int keyRoot = p.rootMidi + keyShift;

            int[] scaleSteps = GetScaleIntervals(p.scale);
            // Style batterie résolu (Auto -> énergie/tension)
            GenerativeDrumStyle style = ResolveStyle(p, rng);

            // --- 2. Progression harmonique fonctionnelle (1 accord / 2 bars, 1/bar si intense) ---
            int chordsPerLoop = (p.intensity01 > 0.72f) ? p.bars : Mathf.Max(2, p.bars / 2);
            int[][] chords; int[] bassRoots;
            BuildProgression(rng, p, keyRoot, scaleSteps, chordsPerLoop, out chords, out bassRoots);
            float chordDur = totalSec / chordsPerLoop;

            // --- 3. Motif mélodique call (A) / response (B) ---
            int[] motifA = BuildMotif(rng, p, keyRoot, scaleSteps, 16);
            int[] motifB = VaryMotif(rng, motifA, scaleSteps, p.chaos01);

            // --- 4. Variante de structure : 1 boucle sur ~5 = break d'air ---
            bool isBreakLoop = (p.generation % 5 == 4) && p.intensity01 < 0.85f;
            float drumGate = isBreakLoop ? 0.25f : 1f; // break : batterie filtrée, pad devant

            StemOverrideState drumOverride = (StemOverrideState)((p.stemOverrides >> ((int)ProceduralStemType.Drums * 2)) & 0x3);
            StemOverrideState bassOverride = (StemOverrideState)((p.stemOverrides >> ((int)ProceduralStemType.Bass * 2)) & 0x3);
            StemOverrideState padOverride = (StemOverrideState)((p.stemOverrides >> ((int)ProceduralStemType.Pad * 2)) & 0x3);
            StemOverrideState arpOverride = (StemOverrideState)((p.stemOverrides >> ((int)ProceduralStemType.Arp * 2)) & 0x3);
            StemOverrideState leadOverride = (StemOverrideState)((p.stemOverrides >> ((int)ProceduralStemType.Lead * 2)) & 0x3);
            StemOverrideState choirOverride = (StemOverrideState)((p.stemOverrides >> ((int)ProceduralStemType.Choir * 2)) & 0x3);
            StemOverrideState staccatoOverride = (StemOverrideState)((p.stemOverrides >> ((int)ProceduralStemType.Staccato * 2)) & 0x3);
            StemOverrideState subAirOverride = (StemOverrideState)((p.stemOverrides >> ((int)ProceduralStemType.SubAir * 2)) & 0x3);

            // --- 5. Batterie (pose le sidechain d'abord) ---
            if (drumOverride != StemOverrideState.Muted)
            {
                float drumVol = (drumOverride == StemOverrideState.ForceActive)
                    ? Mathf.Max(0.6f, (0.55f + 0.65f * p.intensity01) * drumGate)
                    : (0.55f + 0.65f * p.intensity01) * drumGate;
                AddGenerativeDrums(s, sidechain, rng, p, style, beatSec, barSec, drumVol);
            }

            // --- 6. Basse FM growl / sub (duckée) ---
            if (bassOverride != StemOverrideState.Muted)
            {
                float bassVol = (bassOverride == StemOverrideState.ForceActive)
                    ? 0.38f
                    : (0.30f + 0.14f * p.intensity01);
                AddGenerativeBass(s, sidechain, rng, p, bassRoots, chordDur, beatSec, bassVol);
            }

            // --- 7. Pad supersaw + staccato strings (duckés) ---
            if (padOverride != StemOverrideState.Muted)
            {
                float padVol = (padOverride == StemOverrideState.ForceActive)
                    ? 0.28f
                    : (0.20f + 0.10f * (1f - p.intensity01) + 0.06f * p.tension01);
                AddGenerativePad(s, sidechain, p, chords, chordDur, padVol);
            }

            bool playStaccato = (staccatoOverride == StemOverrideState.ForceActive) || (staccatoOverride == StemOverrideState.Auto && p.intensity01 > 0.35f);
            if (playStaccato && staccatoOverride != StemOverrideState.Muted)
            {
                float staccVol = (staccatoOverride == StemOverrideState.ForceActive) ? 0.16f : (0.10f + 0.10f * p.intensity01);
                AddStaccatoLayer(s, rng, p, chords, chordDur, beatSec, staccVol);
            }

            // --- 8. Arpège euclidien + lead call-response ---
            bool playArp = (arpOverride == StemOverrideState.ForceActive) || (arpOverride == StemOverrideState.Auto && p.intensity01 > 0.25f);
            if (playArp && arpOverride != StemOverrideState.Muted)
            {
                float arpVol = (arpOverride == StemOverrideState.ForceActive) ? 0.15f : (0.08f + 0.10f * p.intensity01);
                AddEuclideanArp(s, rng, p, chords, chordDur, beatSec, arpVol, motifA);
            }

            bool playLead = (leadOverride == StemOverrideState.ForceActive) || (leadOverride == StemOverrideState.Auto && (p.intensity01 > 0.45f || p.tension01 > 0.5f));
            if (playLead && leadOverride != StemOverrideState.Muted)
            {
                float leadVol = (leadOverride == StemOverrideState.ForceActive) ? 0.16f : (0.10f + 0.09f * p.intensity01);
                AddLeadCallResponse(s, rng, p, motifA, motifB, barSec, beatSec, leadVol);
            }

            // --- 9. Chœur arcanotech en tension haute (jamais en chip 16-bit) ---
            bool playChoir = (choirOverride == StemOverrideState.ForceActive) || (choirOverride == StemOverrideState.Auto && p.tension01 > 0.45f && !chip);
            if (playChoir && choirOverride != StemOverrideState.Muted)
            {
                float choirVol = (choirOverride == StemOverrideState.ForceActive) ? 0.12f : (0.05f + 0.09f * p.tension01);
                AddChoirBed(s, p, chords, chordDur, choirVol);
            }

            // --- 10. Structure FX : impact bar 0, riser mi-parcours, fill bar finale ---
            AddSectionFx(s, sidechain, rng, p, barSec, beatSec, isBreakLoop);
            if (p.strike01 > 0.05f)
                AddStrikeAccent(s, sidechain, rng, p, barSec, beatSec, p.strike01);

            // Sub profond + air
            if (subAirOverride != StemOverrideState.Muted)
            {
                AddSubDrone(s, keyRoot, 0.07f + 0.03f * p.tension01);
                AddAirShimmer(s, rng, p, 0.015f + 0.02f * p.brightness01);
            }

            // --- 11. Boucle propre + mastering ---
            LoopCrossfade(s, Mathf.Min(SampleRate / 2, n / 6));
            Master(s, 0.60f);
            return s;
        }

        // ================= THÉORIE =================

        private static int[] GetScaleIntervals(GenerativeScale scale)
        {
            switch (scale)
            {
                case GenerativeScale.Dorian: return new[] { 0, 2, 3, 5, 7, 9, 10 };
                case GenerativeScale.Phrygian: return new[] { 0, 1, 3, 5, 7, 8, 10 };
                case GenerativeScale.HarmonicMinor: return new[] { 0, 2, 3, 5, 7, 8, 11 };
                default: return new[] { 0, 2, 3, 5, 7, 8, 10 };
            }
        }

        private static GenerativeDrumStyle ResolveStyle(GenerativeMusicParams p, System.Random rng)
        {
            if (p.drumStyle != GenerativeDrumStyle.Auto) return p.drumStyle;
            float e = p.intensity01, t = p.tension01;
            if (e < 0.32f) return GenerativeDrumStyle.WarEnsemble;
            if (e < 0.58f) return (t > 0.6f) ? GenerativeDrumStyle.HalfTimeTrap : GenerativeDrumStyle.TribalTechno;
            if (e < 0.82f) return (rng.NextDouble() < 0.5 + (t - 0.5f) * 0.4) ? GenerativeDrumStyle.DoubleTime : GenerativeDrumStyle.TribalTechno;
            return (t > 0.55f) ? GenerativeDrumStyle.Blast : GenerativeDrumStyle.DoubleTime;
        }

        /// <summary>Construit une progression fonctionnelle mineure avec emprunts selon tension/chaos.</summary>
        private static void BuildProgression(System.Random rng, GenerativeMusicParams p, int keyRoot,
            int[] scaleSteps, int count, out int[][] chords, out int[] bassRoots)
        {
            // Degrés (offsets depuis la tonique) : i=0, bII=1, III=3, iv=5, v/V=7, VI=8, VII=10
            int[][] banksLow = {
                new[]{ 0, 8, 3, 10 }, new[]{ 0, 5, 8, 7 }, new[]{ 0, 10, 8, 7 },
            };
            int[][] banksMid = {
                new[]{ 0, 10, 8, 10 }, new[]{ 0, 8, 5, 7 }, new[]{ 0, 3, 8, 10 }, new[]{ 0, 1, 0, 10 },
            };
            int[][] banksHigh = {
                new[]{ 0, 1, 0, 7 }, new[]{ 0, 7, 8, 10 }, new[]{ 0, 8, 1, 10 }, new[]{ 0, 10, 1, 7 },
            };
            int[][] bank = p.intensity01 < 0.4f ? banksLow : (p.intensity01 < 0.7f ? banksMid : banksHigh);
            int[] degrees = bank[rng.Next(bank.Length)];

            // Chaos : substitution triton (VI -> bV=6) ou échange modal ponctuel
            if (p.chaos01 > 0.55f && rng.NextDouble() < (p.chaos01 - 0.4f))
            {
                int sub = rng.Next(degrees.Length);
                degrees[sub] = (rng.NextDouble() < 0.5) ? 6 : 1;
            }

            chords = new int[count][];
            bassRoots = new int[count];
            for (int i = 0; i < count; i++)
            {
                int deg = degrees[i % degrees.Length];
                int chordRoot = keyRoot + deg;
                // Quinte du chordRoot, tierce selon fonction : V majeur si harmonique ou tension haute
                bool isDominant = (deg == 7);
                bool majorThird = isDominant && (p.scale == GenerativeScale.HarmonicMinor || p.tension01 > 0.55f || rng.NextDouble() < 0.25 + p.tension01 * 0.3);
                int third = majorThird ? 4 : 3;
                int seventh = (majorThird || deg == 7) ? 10 : (deg == 3 ? 11 : 10);
                var tones = new System.Collections.Generic.List<int>
                {
                    chordRoot + 12, chordRoot + 12 + third, chordRoot + 12 + 7, chordRoot + 12 + seventh
                };
                if (p.brightness01 > 0.45f) tones.Add(chordRoot + 24 + 2); // 9e
                if (p.intensity01 > 0.6f) tones.Add(chordRoot + 12 - 12 + 0); // doublement basse
                // Conduction des voix : recentre autour de 55..72
                int[] arr = tones.ToArray();
                for (int k = 0; k < arr.Length; k++)
                {
                    while (arr[k] < 48) arr[k] += 12;
                    while (arr[k] > 74) arr[k] -= 12;
                }
                chords[i] = arr;
                bassRoots[i] = chordRoot;
            }
        }

        /// <summary>Motif de 16 pas : marche aléatoire sur la gamme + leaps sur accents + silences.</summary>
        private static int[] BuildMotif(System.Random rng, GenerativeMusicParams p, int keyRoot, int[] steps, int len)
        {
            int[] motif = new int[len];
            int scalePos = rng.Next(0, steps.Length) + steps.Length; // 2e octave souvent
            double restProb = Mathf.Lerp(0.35f, 0.08f, p.intensity01);
            for (int i = 0; i < len; i++)
            {
                bool accent = (i % 4 == 0);
                if (!accent && rng.NextDouble() < restProb) { motif[i] = -1; continue; } // silence
                if (accent && rng.NextDouble() < 0.55) scalePos = steps.Length + rng.Next(0, 3); // leap tonique/tierce/quinte
                else
                {
                    int move = rng.Next(-2, 3);
                    if (rng.NextDouble() < p.chaos01 * 0.35) move = rng.Next(-4, 5); // saut surprise
                    scalePos = Mathf.Clamp(scalePos + move, 0, steps.Length * 2 - 1);
                }
                int oct = scalePos / steps.Length;
                int note = keyRoot + 24 + steps[scalePos % steps.Length] + oct * 12;
                // Passing chromatique (hors gamme) quand chaos haut : tension passagère
                if (rng.NextDouble() < p.chaos01 * 0.10) note += (rng.NextDouble() < 0.5 ? 1 : -1);
                motif[i] = Mathf.Clamp(note, 45, 84);
            }
            return motif;
        }

        private static int[] VaryMotif(System.Random rng, int[] src, int[] steps, float chaos)
        {
            int[] dst = (int[])src.Clone();
            for (int i = 0; i < dst.Length; i++)
            {
                if (dst[i] < 0) { if (rng.NextDouble() < 0.25 + chaos * 0.3) dst[i] = src[(i + 4) % src.Length]; continue; }
                double r = rng.NextDouble();
                if (r < 0.15 + chaos * 0.25) dst[i] += (rng.NextDouble() < 0.5 ? 12 : -12); // octave
                else if (r < 0.30 + chaos * 0.3) dst[i] += rng.Next(-2, 3); // ornement
                else if (r < 0.34) dst[i] = -1; // trou respirant
                dst[i] = Mathf.Clamp(dst[i], 45, 86);
            }
            // Rétrograde partiel sur la 2e moitié quand chaos élevé : vence "jamais entendu"
            if (chaos > 0.5 && rng.NextDouble() < 0.5)
                Array.Reverse(dst, dst.Length / 2, dst.Length - dst.Length / 2);
            return dst;
        }

        // ================= BATTERIE =================

        private static void AddGenerativeDrums(float[] s, float[] sc, System.Random rng,
            GenerativeMusicParams p, GenerativeDrumStyle style, float beatSec, float barSec, float vol)
        {
            if (vol < 0.03f) return;
            int bars = p.bars;
            float sixteenth = beatSec / 4f;

            for (int bar = 0; bar < bars; bar++)
            {
                float barT = bar * barSec;
                bool isFillBar = (bar == bars - 1);
                bool halfDropped = (p.generation % 5 == 4) && bar < 2; // break : 2 premières bars allégées

                switch (style)
                {
                    case GenerativeDrumStyle.WarEnsemble:
                        if (!halfDropped) {
                            TaikoHit(s, rng, barT, vol * 1.0f);
                            if (bar % 2 == 1) TaikoHit(s, rng, barT + beatSec * 2f, vol * 0.7f);
                            if (p.intensity01 > 0.3f) TaikoHit(s, rng, barT + beatSec * 3.5f, vol * 0.45f);
                        }
                        ShakerRun(s, rng, barT, barSec, sixteenth, vol * 0.30f, p);
                        if (isFillBar) TomDescent(s, rng, barT + barSec * 0.75f, vol * 0.8f);
                        break;

                    case GenerativeDrumStyle.HalfTimeTrap:
                        if (!halfDropped) {
                            KickHit(s, sc, rng, barT, vol);
                            if (rng.NextDouble() < 0.4 + p.chaos01 * 0.4) KickHit(s, sc, rng, barT + beatSec * 2.75f, vol * 0.8f);
                            SnareHit(s, rng, barT + beatSec * 2f, vol * 0.95f);
                            if (isFillBar) SnareRoll(s, rng, barT + barSec * 0.5f, barSec * 0.5f, vol * 0.8f);
                        }
                        TripletHats(s, rng, barT, beatSec, vol * 0.35f, p);
                        if (bar == 0) CrashHit(s, rng, barT, vol * 0.5f);
                        break;

                    case GenerativeDrumStyle.DoubleTime:
                        if (!halfDropped) {
                            KickHit(s, sc, rng, barT, vol);
                            KickHit(s, sc, rng, barT + beatSec, vol * 0.85f);
                            if (p.intensity01 > 0.6f) KickHit(s, sc, rng, barT + beatSec * 2.5f, vol * 0.8f);
                            KickHit(s, sc, rng, barT + beatSec * 2f, vol * 0.9f);
                            KickHit(s, sc, rng, barT + beatSec * 3f, vol * 0.85f);
                            SnareHit(s, rng, barT + beatSec, vol * 0.9f);
                            SnareHit(s, rng, barT + beatSec * 3f, vol * 0.9f);
                            if (isFillBar) SnareRoll(s, rng, barT + barSec * 0.75f, barSec * 0.25f, vol);
                        }
                        SixteenthHats(s, rng, barT, sixteenth, vol * 0.32f, p);
                        if (bar == 0) CrashHit(s, rng, barT, vol * 0.55f);
                        break;

                    case GenerativeDrumStyle.Blast:
                        if (!halfDropped) {
                            for (int b = 0; b < 4; b++) {
                                KickHit(s, sc, rng, barT + b * beatSec, vol * (b == 0 ? 1f : 0.8f));
                                SnareHit(s, rng, barT + b * beatSec + beatSec * 0.5f, vol * 0.75f);
                            }
                            if (isFillBar) SnareRoll(s, rng, barT + barSec * 0.75f, barSec * 0.25f, vol);
                        }
                        SixteenthHats(s, rng, barT, sixteenth, vol * 0.28f, p);
                        if (bar % 2 == 0) CrashHit(s, rng, barT, vol * 0.4f);
                        break;

                    default: // TribalTechno
                        if (!halfDropped) {
                            for (int b = 0; b < 4; b++) KickHit(s, sc, rng, barT + b * beatSec, vol * (b == 0 ? 1f : 0.85f));
                            SnareHit(s, rng, barT + beatSec, vol * 0.85f);
                            SnareHit(s, rng, barT + beatSec * 3f, vol * 0.85f);
                            TaikoHit(s, rng, barT + beatSec * 2.5f, vol * 0.4f);
                            if (p.chaos01 > 0.4f && rng.NextDouble() < 0.4)
                                TaikoHit(s, rng, barT + beatSec * 3.75f, vol * 0.5f);
                            if (isFillBar) { SnareRoll(s, rng, barT + barSec * 0.75f, barSec * 0.25f, vol * 0.9f); }
                        } else {
                            ShakerRun(s, rng, barT, barSec, sixteenth, vol * 0.35f, p);
                        }
                        OffbeatHats(s, rng, barT, beatSec, vol * 0.34f, p);
                        if (bar == 0) CrashHit(s, rng, barT, vol * 0.55f);
                        break;
                }
            }
        }

        private static void KickHit(float[] s, float[] sc, System.Random rng, float tSec, float vol)
        {
            int start = (int)(tSec * SampleRate);
            if (start >= s.Length) return;
            // Humanisation timing ±4ms
            start += (int)((rng.NextDouble() * 2 - 1) * 0.004 * SampleRate);
            start = Mathf.Clamp(start, 0, s.Length - 1);
            int len = Math.Min((int)(0.30f * SampleRate), s.Length - start);
            float phase = 0f;
            float vel = vol * (0.88f + (float)rng.NextDouble() * 0.24f);
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)SampleRate;
                float f = 43f + 120f * Mathf.Exp(-30f * t);
                phase += 2f * Mathf.PI * f / SampleRate;
                float body = Mathf.Sin(phase) * Mathf.Exp(-8f * t);
                float click = (float)(rng.NextDouble() * 2 - 1) * Mathf.Exp(-60f * t) * 0.3f;
                s[start + i] += (float)Math.Tanh((body + click) * 1.7) * vel;
                int idx = start + i;
                if (idx < sc.Length) sc[idx] = Mathf.Max(sc[idx], Mathf.Exp(-13f * t));
            }
        }

        private static void SnareHit(float[] s, System.Random rng, float tSec, float vol)
        {
            int start = (int)(tSec * SampleRate) + (int)((rng.NextDouble() * 2 - 1) * 0.003 * SampleRate);
            start = Mathf.Clamp(start, 0, s.Length - 1);
            int len = Math.Min((int)(0.22f * SampleRate), s.Length - start);
            float lp = 0f; float vel = vol * (0.88f + (float)rng.NextDouble() * 0.24f);
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)SampleRate;
                float nz = (float)(rng.NextDouble() * 2 - 1);
                lp += 0.38f * (nz - lp);
                float tone = Mathf.Sin(2f * Mathf.PI * 186f * t) * Mathf.Exp(-15f * t);
                s[start + i] += (tone * 0.45f + (nz - lp) * Mathf.Exp(-12f * t) * 0.8f) * vel * 0.85f;
            }
        }

        private static void SnareRoll(float[] s, System.Random rng, float tSec, float durSec, float vol)
        {
            int hits = Math.Max(4, (int)(durSec / 0.06f));
            for (int h = 0; h < hits; h++)
            {
                float k = (float)h / hits;
                float tt = tSec + k * durSec;
                SnareHit(s, rng, tt, vol * (0.4f + 0.6f * k)); // crescendo
            }
        }

        private static void SixteenthHats(float[] s, System.Random rng, float barT, float sixteenth, float vol, GenerativeMusicParams p)
        {
            for (int h = 0; h < 16; h++)
            {
                if (p.intensity01 < 0.5f && h % 2 == 1 && rng.NextDouble() < 0.6) continue; // allégé si calme
                bool ghost = (h % 4 != 0) && rng.NextDouble() < p.chaos01 * 0.5;
                float tt = barT + h * sixteenth + (ghost ? sixteenth * 0.33f : 0f);
                HatTick(s, rng, tt, vol * ((h % 4 == 0) ? 1f : (h % 2 == 0 ? 0.7f : 0.45f)));
            }
        }

        private static void OffbeatHats(float[] s, System.Random rng, float barT, float beatSec, float vol, GenerativeMusicParams p)
        {
            for (int b = 0; b < 4; b++)
            {
                HatTick(s, rng, barT + b * beatSec + beatSec * 0.5f, vol);
                if (p.intensity01 > 0.55f) HatTick(s, rng, barT + b * beatSec + beatSec * 0.25f, vol * 0.5f);
                if (p.intensity01 > 0.75f) HatTick(s, rng, barT + b * beatSec + beatSec * 0.75f, vol * 0.5f);
            }
        }

        private static void TripletHats(float[] s, System.Random rng, float barT, float beatSec, float vol, GenerativeMusicParams p)
        {
            float trip = beatSec / 3f;
            for (int b = 0; b < 4; b++)
                for (int k = 0; k < 3; k++)
                {
                    if (k == 1 && rng.NextDouble() < 0.35 - p.chaos01 * 0.2) continue;
                    HatTick(s, rng, barT + b * beatSec + k * trip, vol * (k == 0 ? 0.9f : 0.55f));
                }
        }

        private static void HatTick(float[] s, System.Random rng, float tSec, float vol)
        {
            int start = (int)(tSec * SampleRate);
            if (start < 0 || start >= s.Length) return;
            int len = Math.Min((int)(0.045f * SampleRate), s.Length - start);
            float hp = 0f;
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)SampleRate;
                float nz = (float)(rng.NextDouble() * 2 - 1);
                hp += 0.08f * (nz - hp);
                s[start + i] += (nz - hp) * Mathf.Exp(-55f * t) * vol;
            }
        }

        private static void ShakerRun(float[] s, System.Random rng, float barT, float barSec, float sixteenth, float vol, GenerativeMusicParams p)
        {
            for (int h = 0; h < 8; h++)
            {
                float tt = barT + h * sixteenth * 2f;
                int start = (int)(tt * SampleRate);
                if (start < 0 || start >= s.Length) continue;
                int len = Math.Min((int)(0.09f * SampleRate), s.Length - start);
                float lp = 0f;
                for (int i = 0; i < len; i++)
                {
                    float t = (float)i / len;
                    float nz = (float)(rng.NextDouble() * 2 - 1);
                    lp += 0.25f * (nz - lp);
                    s[start + i] += lp * Mathf.Sin(Mathf.PI * t) * vol;
                }
            }
        }

        private static void TaikoHit(float[] s, System.Random rng, float tSec, float vol)
        {
            int start = (int)(tSec * SampleRate);
            if (start < 0 || start >= s.Length) return;
            int len = Math.Min((int)(0.42f * SampleRate), s.Length - start);
            float lp = 0f;
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)SampleRate;
                float pitch = 58f * Mathf.Exp(-8f * t) + 30f;
                float sub = Mathf.Sin(2f * Mathf.PI * pitch * t) * Mathf.Exp(-5f * t);
                float nz = (float)(rng.NextDouble() * 2 - 1);
                lp += 0.16f * (nz - lp);
                s[start + i] += (sub * 1.15f + lp * Mathf.Exp(-22f * t) * 0.7f) * vol;
            }
        }

        private static void TomDescent(float[] s, System.Random rng, float tSec, float vol)
        {
            float[] freqs = { 220f, 174f, 146f, 110f };
            for (int k = 0; k < freqs.Length; k++)
            {
                float tt = tSec + k * 0.11f;
                int start = (int)(tt * SampleRate);
                if (start < 0 || start >= s.Length) continue;
                int len = Math.Min((int)(0.22f * SampleRate), s.Length - start);
                for (int i = 0; i < len; i++)
                {
                    float t = i / (float)SampleRate;
                    s[start + i] += Mathf.Sin(2f * Mathf.PI * freqs[k] * Mathf.Exp(-3f * t) * t)
                        * Mathf.Exp(-10f * t) * vol * 0.7f;
                }
            }
        }

        private static void CrashHit(float[] s, System.Random rng, float tSec, float vol)
        {
            int start = (int)(tSec * SampleRate);
            if (start < 0 || start >= s.Length) return;
            int len = Math.Min((int)(1.1f * SampleRate), s.Length - start);
            float hp = 0f;
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)SampleRate;
                float nz = (float)(rng.NextDouble() * 2 - 1);
                hp += 0.06f * (nz - hp);
                float shimmer = Mathf.Sin(2f * Mathf.PI * 5200f * t) * 0.2f + Mathf.Sin(2f * Mathf.PI * 7800f * t) * 0.12f;
                s[start + i] += ((nz - hp) * 0.7f + shimmer * Mathf.Exp(-4f * t)) * Mathf.Exp(-3.2f * t) * vol;
            }
        }

        // ================= BASSE / PAD / ARP / LEAD =================

        private static void AddGenerativeBass(float[] s, float[] sc, System.Random rng,
            GenerativeMusicParams p, int[] bassRoots, float chordDur, float beatSec, float vol)
        {
            float sixteenth = beatSec / 4f;
            int total16 = (int)((chordDur * bassRoots.Length) / sixteenth);
            for (int step = 0; step < total16; step++)
            {
                float stepT = step * sixteenth;
                // Grille rythmique : dense si intense, aérée + syncopée si calme
                int m16 = step % 16;
                bool play;
                if (p.intensity01 > 0.75f) play = (m16 % 2 == 0) || m16 == 3 || m16 == 6 || m16 == 11 || m16 == 14;
                else if (p.intensity01 > 0.45f) play = (m16 % 4 == 0) || m16 == 6 || m16 == 11 || m16 == 14;
                else play = (m16 == 0) || m16 == 7 || m16 == 10;
                if (!play) continue;
                if (rng.NextDouble() < p.chaos01 * 0.12) continue; // trou funk
                if (rng.NextDouble() < p.chaos01 * 0.10) stepT += sixteenth * 0.5f; // retard syncopé

                int chordIdx = Math.Min(bassRoots.Length - 1, (int)(stepT / chordDur));
                int root = bassRoots[chordIdx];
                while (root < 30) root += 12;
                while (root > 42) root -= 12;
                int octJump = (m16 == 6 || m16 == 14) && rng.NextDouble() < 0.6 ? 12 : 0;
                if (rng.NextDouble() < p.chaos01 * 0.08) octJump = 7; // quinte surprise
                float f = MidiFreq(root + octJump);

                int start = (int)(stepT * SampleRate);
                if (start < 0 || start >= s.Length) continue;
                int len = Math.Min((int)(sixteenth * 2.1f * SampleRate), s.Length - start);
                bool growl = p.intensity01 > 0.62f && p.preset != GenerativeStylePreset.Retro16Bit;
                for (int i = 0; i < len; i++)
                {
                    float t = i / (float)SampleRate;
                    float env = Mathf.Exp(-8f * t);
                    float ph = 2f * Mathf.PI * f * t;
                    float osc;
                    if (growl)
                    {
                        // FM growl : porteuse + modulante
                        float mod = Mathf.Sin(2f * Mathf.PI * f * 2.02f * t) * (2.5f + p.tension01 * 3f);
                        osc = Mathf.Sin(ph + mod) * 0.7f + Mathf.Sin(ph * 0.5f) * 0.5f;
                    }
                    else osc = Mathf.Sin(ph) * 0.9f + Mathf.Sign(Mathf.Sin(ph)) * 0.18f * env;
                    int idx = start + i;
                    float duck = (idx < sc.Length) ? (1f - 0.72f * sc[idx]) : 1f;
                    s[idx] += (float)Math.Tanh(osc * 1.6) * env * vol * duck;
                }
            }
        }

        private static void AddGenerativePad(float[] s, float[] sc, GenerativeMusicParams p,
            int[][] chords, float chordDur, float vol)
        {
            int chordSamples = (int)(chordDur * SampleRate);
            float cutoff = Mathf.Lerp(0.12f, 0.85f, p.brightness01);
            float lpState = 0f;
            for (int i = 0; i < s.Length; i++)
            {
                int c = Math.Min(chords.Length - 1, i / Math.Max(1, chordSamples));
                float localT = (i - c * chordSamples) / (float)SampleRate;
                float atk = Smooth01(localT / 0.6f);
                float rel = Smooth01((chordDur - localT) / 0.7f);
                float t = i / (float)SampleRate;
                float chorus = 0.003f * Mathf.Sin(2f * Mathf.PI * 0.21f * t);
                float v = 0f;
                var chord = chords[c];
                for (int k = 0; k < chord.Length; k++)
                {
                    float f = MidiFreq(chord[k]);
                    // supersaw 3 voix désaccordées
                    float ph = 2f * Mathf.PI * f * localT;
                    float saw1 = 2f * ((f * (1f + chorus) * localT) % 1f) - 1f;
                    float saw2 = 2f * ((f * (1f - chorus) * localT) % 1f) - 1f;
                    float sine = Mathf.Sin(ph);
                    v += saw1 * 0.30f + saw2 * 0.30f + sine * 0.40f;
                }
                v /= Mathf.Max(1f, Mathf.Sqrt(chord.Length));
                // Filtre passe-bas one-pole (brillance) + duck
                lpState += cutoff * 0.25f * (v - lpState);
                float duck = (i < sc.Length) ? (1f - 0.6f * sc[i]) : 1f;
                s[i] += (float)Math.Tanh(lpState * 1.3) * atk * rel * vol * 2.2f * duck;
            }
        }

        private static void AddStaccatoLayer(float[] s, System.Random rng, GenerativeMusicParams p,
            int[][] chords, float chordDur, float beatSec, float vol)
        {
            float sixteenth = beatSec / 4f;
            int total = (int)((chordDur * chords.Length) / sixteenth);
            for (int step = 0; step < total; step++)
            {
                if (step % 2 == 1 && rng.NextDouble() < 0.45) continue;
                float stepT = step * sixteenth;
                int ci = Math.Min(chords.Length - 1, (int)(stepT / chordDur));
                var chord = chords[ci];
                int note = chord[step % chord.Length] + 12;
                float f = MidiFreq(note);
                int start = (int)(stepT * SampleRate);
                if (start < 0 || start >= s.Length) continue;
                int len = Math.Min((int)(sixteenth * 1.1f * SampleRate), s.Length - start);
                float acc = (step % 4 == 0) ? 1f : 0.6f;
                for (int i = 0; i < len; i++)
                {
                    float t = i / (float)SampleRate;
                    float env = Mathf.Exp(-22f * t);
                    s[start + i] += (Mathf.Sin(2f * Mathf.PI * f * t)
                        + 0.3f * Mathf.Sin(4f * Mathf.PI * f * t)) * env * vol * acc;
                }
            }
        }

        /// <summary>Arpège euclidien : n'impulsions réparties sur 16 pas, rotation par accord.</summary>
        private static void AddEuclideanArp(float[] s, System.Random rng, GenerativeMusicParams p,
            int[][] chords, float chordDur, float beatSec, float vol, int[] motif)
        {
            float sixteenth = beatSec / 4f;
            int total = (int)((chordDur * chords.Length) / sixteenth);
            int chipBonus = (p.preset == GenerativeStylePreset.Retro16Bit) ? 2 : 0; // chip-arp plus bavard
            int pulses = Mathf.Clamp(Mathf.RoundToInt(4 + p.intensity01 * 8 + p.chaos01 * 3) + chipBonus, 4, 14);
            int rotation = (p.generation * 3) % 16;
            // Delay synchronisé à la croche (divise la mesure => bouclable)
            int delaySpl = Math.Max(1, (int)(SampleRate * beatSec * 0.5f));
            float[] dl = new float[delaySpl + 1];
            int dlIdx = 0;
            for (int step = 0; step < total; step++)
            {
                if (!EuclidHit(step + rotation, 16, pulses)) continue;
                float stepT = step * sixteenth;
                int ci = Math.Min(chords.Length - 1, (int)(stepT / chordDur));
                var chord = chords[ci];
                int note = chord[(step + ci) % chord.Length] + 12;
                if (motif != null && motif.Length > 0 && rng.NextDouble() < 0.3)
                {
                    int m = motif[step % motif.Length];
                    if (m > 0) note = m;
                }
                float f = MidiFreq(note);
                int start = (int)(stepT * SampleRate);
                if (start < 0 || start >= s.Length) continue;
                int len = Math.Min((int)(sixteenth * 2.2f * SampleRate), s.Length - start);
                for (int i = 0; i < len; i++)
                {
                    float t = i / (float)SampleRate;
                    float env = Mathf.Exp(-9f * t);
                    float osc = Mathf.Sin(2f * Mathf.PI * f * t) + 0.3f * Mathf.Sin(4f * Mathf.PI * f * t);
                    float direct = osc * env * vol * 0.5f;
                    float echo = dl[dlIdx] * 0.34f;
                    dl[dlIdx] = direct + echo * 0.35f;
                    dlIdx = (dlIdx + 1) % delaySpl;
                    s[start + i] += direct + echo;
                }
            }
        }

        private static void AddLeadCallResponse(float[] s, System.Random rng, GenerativeMusicParams p,
            int[] motifA, int[] motifB, float barSec, float beatSec, float vol)
        {
            float eighth = beatSec / 2f;
            int bars = p.bars;
            for (int bar = 0; bar < bars; bar++)
            {
                // Le lead se tait pendant le break (bars 0-1 des boucles break) : respiration
                if ((p.generation % 5 == 4) && bar < 2 && rng.NextDouble() < 0.8) continue;
                bool secondHalf = bar >= bars / 2;
                int[] motif = secondHalf ? motifB : motifA;
                float barT = bar * barSec;
                for (int st = 0; st < 8; st++)
                {
                    int note = motif[(st + bar * 2) % motif.Length];
                    if (note < 0) continue;
                    float tt = barT + st * eighth;
                    // Push syncopé : 12% des notes jouées un 16e en avance
                    if (rng.NextDouble() < p.chaos01 * 0.12) tt -= beatSec / 4f;
                    int start = (int)(tt * SampleRate);
                    if (start < 0 || start >= s.Length) continue;
                    int len = Math.Min((int)(eighth * 1.8f * SampleRate), s.Length - start);
                    float f = MidiFreq(note);
                    float vibRate = 5.2f + (float)rng.NextDouble();
                    float vibDepth = 0.004f + p.tension01 * 0.004f;
                    float vel = vol * (0.8f + (float)rng.NextDouble() * 0.4f) * ((st % 2 == 0) ? 1f : 0.7f);
                    for (int i = 0; i < len; i++)
                    {
                        float t = i / (float)SampleRate;
                        float env = Mathf.Exp(-6.5f * t) * Mathf.Min(1f, t * 60f + 0.15f);
                        float vib = 1f + vibDepth * Mathf.Sin(2f * Mathf.PI * vibRate * t);
                        float ph = 2f * Mathf.PI * f * vib * t;
                        float saw = 2f * ((f * vib * t) % 1f) - 1f;
                        float osc = (p.preset == GenerativeStylePreset.Retro16Bit)
                            ? (Mathf.Sign(Mathf.Sin(ph)) * 0.55f + saw * 0.25f) // square 16-bit
                            : (Mathf.Sin(ph) * 0.6f + saw * 0.4f);
                        // Brillance : mélange sine/saw déjà filtré par vol ; saturation douce
                        s[start + i] += (float)Math.Tanh(osc * 1.5) * env * vel;
                    }
                }
            }
        }

        private static void AddChoirBed(float[] s, GenerativeMusicParams p, int[][] chords, float chordDur, float vol)
        {
            int chordSamples = (int)(chordDur * SampleRate);
            for (int i = 0; i < s.Length; i++)
            {
                int c = Math.Min(chords.Length - 1, i / Math.Max(1, chordSamples));
                float localT = (i - c * chordSamples) / (float)SampleRate;
                float atk = Smooth01(localT / 0.5f);
                float rel = Smooth01((chordDur - localT) / 0.6f);
                float t = i / (float)SampleRate;
                float vib = 1f + 0.0035f * Mathf.Sin(2f * Mathf.PI * 5.1f * t);
                float v = 0f;
                var chord = chords[c];
                for (int k = 0; k < chord.Length; k++)
                {
                    float f = MidiFreq(chord[k] + 12) * vib;
                    v += Mathf.Sin(2f * Mathf.PI * f * localT)
                        + 0.5f * Mathf.Sin(4f * Mathf.PI * f * localT)
                        + 0.22f * Mathf.Sin(6f * Mathf.PI * f * localT);
                }
                s[i] += (float)Math.Tanh(v / (chord.Length * 1.2f) * 1.4) * atk * rel * vol;
            }
        }

        // ================= FX DE STRUCTURE =================

        private static void AddSectionFx(float[] s, float[] sc, System.Random rng,
            GenerativeMusicParams p, float barSec, float beatSec, bool isBreak)
        {
            // Impact + crash barre 0
            ImpactHit(s, rng, 0f, 0.8f + 0.3f * p.intensity01);
            // Riser vers la mi-parcours (bar/2)
            float riseStart = barSec * (p.bars / 2 - 1);
            RiserFx(s, rng, riseStart, barSec, 0.10f + 0.10f * p.intensity01);
            // Fill + downlifter fin de boucle
            float fillStart = barSec * (p.bars - 1);
            if (!isBreak)
            {
                SnareRoll(s, rng, fillStart + barSec * 0.5f, barSec * 0.5f, 0.35f + 0.35f * p.intensity01);
                TomDescent(s, rng, fillStart + barSec * 0.6f, 0.35f);
            }
            DownlifterFx(s, rng, fillStart + barSec * 0.85f, barSec * 0.15f, 0.12f);
        }

        private static void AddStrikeAccent(float[] s, float[] sc, System.Random rng,
            GenerativeMusicParams p, float barSec, float beatSec, float strike)
        {
            // Sub drop + taiko sur la downbeat de la 2e moitié : le coup "colle" à la musique
            float tt = barSec * (p.bars / 2);
            int start = (int)(tt * SampleRate);
            if (start < 0 || start >= s.Length) return;
            int len = Math.Min((int)(0.9f * SampleRate), s.Length - start);
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)SampleRate;
                float f = 90f * Mathf.Exp(-2.8f * t) + 32f;
                s[start + i] += Mathf.Sin(2f * Mathf.PI * f * t) * Mathf.Exp(-4f * t) * strike * 0.7f;
            }
            TaikoHit(s, rng, tt, strike * 0.6f);
            CrashHit(s, rng, tt, strike * 0.35f);
        }

        private static void ImpactHit(float[] s, System.Random rng, float tSec, float vol)
        {
            int start = (int)(tSec * SampleRate);
            if (start < 0 || start >= s.Length) return;
            int len = Math.Min((int)(1.2f * SampleRate), s.Length - start);
            float lp = 0f;
            for (int i = 0; i < len; i++)
            {
                float t = i / (float)SampleRate;
                float env = Mathf.Exp(-4.5f * t) * Mathf.Min(1f, t * 160f + 0.05f);
                float sub = Mathf.Sin(2f * Mathf.PI * 48f * Mathf.Exp(-1.5f * t) * t);
                float nz = (float)(rng.NextDouble() * 2 - 1);
                lp += 0.08f * (nz - lp);
                s[start + i] += (sub * 1.1f + lp * 0.7f) * env * vol;
            }
        }

        private static void RiserFx(float[] s, System.Random rng, float tSec, float durSec, float vol)
        {
            int start = (int)(tSec * SampleRate);
            if (start < 0 || start >= s.Length) return;
            int len = Math.Min((int)(durSec * SampleRate), s.Length - start);
            float lp = 0f;
            for (int i = 0; i < len; i++)
            {
                float k = (float)i / Math.Max(1, len);
                float nz = (float)(rng.NextDouble() * 2 - 1);
                lp += Mathf.Lerp(0.03f, 0.42f, k) * (nz - lp);
                s[start + i] += lp * k * k * vol * 1.7f;
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

        private static void AddSubDrone(float[] s, int keyRoot, float vol)
        {
            float f = MidiFreq(Mathf.Clamp(keyRoot - 12, 20, 40));
            for (int i = 0; i < s.Length; i++)
            {
                float t = i / (float)SampleRate;
                float lfo = 0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * 0.14f * t);
                s[i] += Mathf.Sin(2f * Mathf.PI * f * t) * lfo * vol;
            }
        }

        private static void AddAirShimmer(float[] s, System.Random rng, GenerativeMusicParams p, float vol)
        {
            float f = MidiFreq(p.rootMidi + 36);
            for (int i = 0; i < s.Length; i++)
            {
                float t = i / (float)SampleRate;
                s[i] += Mathf.Sin(2f * Mathf.PI * f * t + Mathf.Sin(t * 4.7f))
                    * Mathf.Sin(2f * Mathf.PI * f * 1.5f * t) * vol
                    * (0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * 0.08f * t));
            }
        }

        // ================= UTILS =================

        private static bool EuclidHit(int step, int steps, int pulses)
        {
            // Pulsations réparties au mieux (Bjorklund simplifié via modulo premier)
            return ((step * pulses) % steps) < pulses;
        }

        private static float MidiFreq(int midi) => 440f * Mathf.Pow(2f, (midi - 69) / 12f);
        private static float Smooth01(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }

        private static void LoopCrossfade(float[] s, int fade)
        {
            int n = s.Length;
            fade = Math.Min(fade, n / 4);
            if (fade < 16) return;
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

        private static void Master(float[] s, float targetPeak)
        {
            float max = 0.0001f;
            for (int i = 0; i < s.Length; i++) max = Math.Max(max, Math.Abs(s[i]));
            float scale = targetPeak / max;
            if (scale > 3.5f) scale = 3.5f;
            for (int i = 0; i < s.Length; i++) s[i] = (float)Math.Tanh(s[i] * scale * 0.95);
        }
    }
}

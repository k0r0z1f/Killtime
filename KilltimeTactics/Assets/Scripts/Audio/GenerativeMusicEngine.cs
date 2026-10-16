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

    /// <summary>
    /// Rôle dramatique de la boucle dans le cycle musical (A / A' / Pont B / Climax / Breakdown).
    /// Garde l'identité du thème principal tout en racontant une histoire sur la durée.
    /// </summary>
    public enum GenerativeSectionRole
    {
        ThemeA = 0,      // Exposition claire du motif racine et de la progression d'ancrage
        ThemeAVar = 1,   // Variation ornée + réponse syncopée
        BridgeB = 2,     // Excursion harmonique (relatif majeur / sous-dominante) + développement
        Climax = 3,      // Montée dramatique, contre-chant harmonisé à la tierce/sixte, drive max
        Breakdown = 4    // Respiration atmosphérique (batterie filtrée, focus pad/chœur/sub)
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
    /// variation inédite mais cohérente : même thème motivique racine issu de seed,
    /// développé selon la section (A / A' / B / Climax / Breakdown).
    /// </summary>
    [Serializable]
    public struct GenerativeMusicParams
    {
        public int seed;            // base fixe par combat/scène (identité du thème principal)
        public int generation;      // incrémenté à chaque boucle (développement A/A'/B/Climax)
        public float intensity01;   // 0..1 énergie combat (densité, couches)
        public float tension01;     // 0..1 danger (PV bas, dernier carré)
        public float brightness01;  // 0..1 clarté du mix (cutoff filtres SVF lead/pad/basse)
        public float chaos01;       // 0..1 ornementation / surprises rythmiques
        public int rootMidi;        // fondamentale (ex 33 = A1). 26..45 conseillé
        public GenerativeScale scale;
        public GenerativeDrumStyle drumStyle;
        public GenerativeStylePreset preset; // Auto = pilotage énergie/tension actuel
        public float bpm;           // 96..180, quantifié pour éviter les décrochages
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
    /// Architecture à double RNG (mémoire thématique par seed + développement par generation),
    /// conduction des voix fluide (voice-leading), synthèse anti-aliasée PolyBLEP,
    /// filtres résonants SVF 2-pôles, chœur à formants vocaliques et delays bouclés sans couture.
    /// </summary>
    public static partial class GenerativeMusicEngine
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

            // Double RNG :
            // - themeRng dépend UNIQUEMENT de (seed, scale) -> garde la mélodie & les accords identifiables ;
            // - varRng dépend de (seed, generation) -> pilote les variations, fills et contre-chants.
            int themeSeed = unchecked(p.seed * 73856093 ^ ((int)p.scale + 1) * 49979693 ^ 0x5A17C3);
            int varSeed = unchecked(p.seed * 73856093 ^ (p.generation + 1) * 19349663 ^ 83492791);
            var themeRng = new System.Random(themeSeed);
            var varRng = new System.Random(varSeed);

            GenerativeSectionRole role = GetSectionRole(p);
            bool isBreakLoop = (role == GenerativeSectionRole.Breakdown);

            float bpm = p.bpm;
            float beatSec = 60f / bpm;
            float barSec = beatSec * 4f;
            float totalSec = barSec * p.bars;
            int n = (int)(SampleRate * totalSec);
            float[] s = new float[n];
            float[] sidechain = new float[n];

            // --- 1. Tonalité & modulation dramatique sur Climax ---
            int keyShift = 0;
            if (role == GenerativeSectionRole.Climax && p.tension01 > 0.58f)
                keyShift = (p.generation % 8 == 7) ? 2 : 0; // transposition héroïque/dramatique fin de grand cycle
            int keyRoot = p.rootMidi + keyShift;

            int[] scaleSteps = GetScaleIntervals(p.scale);
            GenerativeDrumStyle style = ResolveStyle(p, varRng);

            // --- 2. Progression harmonique à conduction de voix fluide (Voice-Leading) ---
            int chordsPerLoop = (p.intensity01 > 0.72f || role == GenerativeSectionRole.Climax)
                ? p.bars
                : Mathf.Max(4, p.bars / 2);
            BuildProgression(themeRng, varRng, p, role, keyRoot, scaleSteps, chordsPerLoop, out int[][] chords, out int[] bassRoots);
            float chordDur = totalSec / chordsPerLoop;

            // --- 3. Thème mélodique ancré sur les notes d'accord + développement A / B / Contre-chant ---
            int[] coreMotif = BuildThemeMotif(themeRng, p, keyRoot, scaleSteps, chords, 16);
            int[] motifA = DevelopMotif(varRng, coreMotif, scaleSteps, keyRoot, role, p.chaos01, isSecondHalf: false);
            int[] motifB = DevelopMotif(varRng, coreMotif, scaleSteps, keyRoot, role, p.chaos01, isSecondHalf: true);
            int[] counterMotif = BuildCounterMotif(varRng, motifA, motifB, scaleSteps, keyRoot, role);

            float drumGate = isBreakLoop ? 0.28f : 1f;

            StemOverrideState drumOverride = (StemOverrideState)((p.stemOverrides >> ((int)ProceduralStemType.Drums * 2)) & 0x3);
            StemOverrideState bassOverride = (StemOverrideState)((p.stemOverrides >> ((int)ProceduralStemType.Bass * 2)) & 0x3);
            StemOverrideState padOverride = (StemOverrideState)((p.stemOverrides >> ((int)ProceduralStemType.Pad * 2)) & 0x3);
            StemOverrideState arpOverride = (StemOverrideState)((p.stemOverrides >> ((int)ProceduralStemType.Arp * 2)) & 0x3);
            StemOverrideState leadOverride = (StemOverrideState)((p.stemOverrides >> ((int)ProceduralStemType.Lead * 2)) & 0x3);
            StemOverrideState choirOverride = (StemOverrideState)((p.stemOverrides >> ((int)ProceduralStemType.Choir * 2)) & 0x3);
            StemOverrideState staccatoOverride = (StemOverrideState)((p.stemOverrides >> ((int)ProceduralStemType.Staccato * 2)) & 0x3);
            StemOverrideState subAirOverride = (StemOverrideState)((p.stemOverrides >> ((int)ProceduralStemType.SubAir * 2)) & 0x3);

            // --- 4. Batterie (pose l'enveloppe de sidechain) ---
            if (drumOverride != StemOverrideState.Muted)
            {
                float drumVol = (drumOverride == StemOverrideState.ForceActive)
                    ? Mathf.Max(0.62f, (0.56f + 0.62f * p.intensity01) * drumGate)
                    : (0.56f + 0.62f * p.intensity01) * drumGate;
                AddGenerativeDrums(s, sidechain, varRng, p, style, role, keyRoot, beatSec, barSec, drumVol);
            }

            // --- 5. Basse Reese / FM résonante (duckée par le kick) ---
            if (bassOverride != StemOverrideState.Muted)
            {
                float bassVol = (bassOverride == StemOverrideState.ForceActive)
                    ? 0.40f
                    : (0.31f + 0.14f * p.intensity01);
                AddGenerativeBass(s, sidechain, varRng, p, role, bassRoots, chords, chordDur, beatSec, bassVol);
            }

            // --- 6. Pad Supersaw PolyBLEP + filtre SVF résonant (ducké) ---
            if (padOverride != StemOverrideState.Muted)
            {
                float padVol = (padOverride == StemOverrideState.ForceActive)
                    ? 0.30f
                    : (0.21f + 0.10f * (1f - p.intensity01) + 0.06f * p.tension01);
                AddGenerativePad(s, sidechain, p, role, chords, chordDur, padVol);
            }

            // --- 7. Cordes Staccato / Ostinato rythmique ---
            bool playStaccato = (staccatoOverride == StemOverrideState.ForceActive)
                || (staccatoOverride == StemOverrideState.Auto && (p.intensity01 > 0.35f || role == GenerativeSectionRole.Climax));
            if (playStaccato && staccatoOverride != StemOverrideState.Muted)
            {
                float staccVol = (staccatoOverride == StemOverrideState.ForceActive) ? 0.17f : (0.10f + 0.10f * p.intensity01);
                AddStaccatoLayer(s, sidechain, varRng, p, role, chords, chordDur, beatSec, staccVol);
            }

            // --- 8. Arpège euclidien à delay bouclé + Lead legato à contre-chant ---
            bool playArp = (arpOverride == StemOverrideState.ForceActive)
                || (arpOverride == StemOverrideState.Auto && (p.intensity01 > 0.22f || isBreakLoop));
            if (playArp && arpOverride != StemOverrideState.Muted)
            {
                float arpVol = (arpOverride == StemOverrideState.ForceActive) ? 0.16f : (0.09f + 0.10f * p.intensity01);
                AddEuclideanArp(s, sidechain, varRng, p, role, chords, chordDur, beatSec, arpVol, motifA);
            }

            bool playLead = (leadOverride == StemOverrideState.ForceActive)
                || (leadOverride == StemOverrideState.Auto && (p.intensity01 > 0.40f || p.tension01 > 0.48f || role == GenerativeSectionRole.Climax));
            if (playLead && leadOverride != StemOverrideState.Muted)
            {
                float leadVol = (leadOverride == StemOverrideState.ForceActive) ? 0.18f : (0.11f + 0.09f * p.intensity01);
                AddLeadCallResponse(s, sidechain, varRng, p, role, motifA, motifB, counterMotif, chords, chordDur, barSec, beatSec, leadVol);
            }

            // --- 9. Chœur arcanotech à formants vocaliques ---
            bool playChoir = (choirOverride == StemOverrideState.ForceActive)
                || (choirOverride == StemOverrideState.Auto && (p.tension01 > 0.42f || role == GenerativeSectionRole.Climax || role == GenerativeSectionRole.Breakdown) && !chip);
            if (playChoir && choirOverride != StemOverrideState.Muted)
            {
                float choirBoost = (role == GenerativeSectionRole.Climax || role == GenerativeSectionRole.Breakdown) ? 0.04f : 0f;
                float choirVol = (choirOverride == StemOverrideState.ForceActive) ? 0.15f : (0.06f + 0.09f * p.tension01 + choirBoost);
                AddChoirBed(s, sidechain, p, role, chords, chordDur, choirVol);
            }

            // --- 10. Ponctuations de structure & accents de combat ---
            AddSectionFx(s, sidechain, varRng, p, role, keyRoot, barSec, beatSec, isBreakLoop);
            if (p.strike01 > 0.05f)
                AddStrikeAccent(s, sidechain, varRng, p, keyRoot, barSec, beatSec, p.strike01);

            if (subAirOverride != StemOverrideState.Muted)
            {
                AddSubDrone(s, sidechain, keyRoot, 0.065f + 0.03f * p.tension01);
                AddAirShimmer(s, varRng, p, keyRoot, 0.016f + 0.022f * p.brightness01);
            }

            // --- 11. De-click de jointure de boucle (préserve 100% du punch du temps 1) + Mastering ---
            int seamSamples = Mathf.Min((int)(SampleRate * 0.015f), n / 32);
            LoopCrossfade(s, seamSamples);
            Master(s, 0.62f);
            return s;
        }

        /// <summary>
        /// Génère un accent musical court (~0.85s) accordé sur la tonique et la gamme de la boucle en cours.
        /// Permet à NotifyCombatStrike de sonner IMMÉDIATEMENT dans la tonalité du morceau.
        /// </summary>
        public static float[] RenderReactiveStrike(int rootMidi, GenerativeScale scale, float intensity01)
        {
            intensity01 = Mathf.Clamp01(intensity01);
            int n = (int)(SampleRate * 0.85f);
            float[] s = new float[n];
            int[] steps = GetScaleIntervals(scale);
            int root = Mathf.Clamp(rootMidi, 28, 45);
            float fRoot = MidiFreq(root);
            float fFifth = MidiFreq(root + 7);
            float fColor = MidiFreq(root + 12 + steps[Mathf.Min(2, steps.Length - 1)]); // tierce mineure à l'octave

            var rng = new System.Random(root * 997 + (int)scale * 31);
            float phSub = 0f, ph1 = 0f, ph2 = 0f, ph3 = 0f;
            float lpNoise = 0f;

            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float pitchDrop = 1f + 0.65f * Mathf.Exp(-28f * t);
                phSub += 2f * Mathf.PI * (fRoot * pitchDrop) / SampleRate;
                ph1 += (fRoot * 2f) / SampleRate; if (ph1 >= 1f) ph1 -= 1f;
                ph2 += (fFifth * 2f) / SampleRate; if (ph2 >= 1f) ph2 -= 1f;
                ph3 += (fColor * 2f) / SampleRate; if (ph3 >= 1f) ph3 -= 1f;

                float sub = Mathf.Sin(phSub) * Mathf.Exp(-4.2f * t);
                float dt = (fRoot * 2f) / SampleRate;
                float brass = (PolyBlepSaw(ph1, dt) * 0.5f + PolyBlepSaw(ph2, dt * 1.5f) * 0.3f + PolyBlepSaw(ph3, dt * 2.4f) * 0.25f)
                    * Mathf.Exp(-5.5f * t) * Smooth01(t / 0.015f);

                float nz = (float)(rng.NextDouble() * 2.0 - 1.0);
                lpNoise += 0.14f * (nz - lpNoise);
                float transient = lpNoise * Mathf.Exp(-32f * t) * 0.55f;

                float gain = 0.55f + 0.40f * intensity01;
                s[i] = (float)Math.Tanh((sub * 1.15f + brass * 0.75f + transient) * 1.6f) * gain;
            }
            Master(s, 0.58f);
            return s;
        }
    }
}

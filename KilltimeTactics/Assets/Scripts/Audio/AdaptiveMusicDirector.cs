using UnityEngine;

namespace Killtime.Audio
{
    /// <summary>
    /// Cerveau de la musique adaptative : pur C#, sans dépendance Unity Play,
    /// donc testable en EditMode et réutilisable hors arène.
    ///
    /// Principe :
    /// - le mood (Explore/Combat/Tension/...) = direction horizontale (où va le récit) ;
    /// - l'intensité 0..1 = direction verticale (combien de couches : pad -> basse/arp -> batterie).
    /// L'intensité est quantifiée en 3 variantes procédurales (Calm/Tense/Intense)
    /// pour rester légère sur WebGL (pas de stems temps réel, pas de mixage multi-sources).
    /// </summary>
    public static class AdaptiveMusicDirector
    {
        // Seuils d'hystérésis : on bascule en Tension sous 0.22, on n'en sort qu'au-dessus de 0.32.
        // Évite le yoyo Combat <-> Tension quand les PV oscillent autour du seuil.
        public const float CriticalHpEnter = 0.22f;
        public const float CriticalHpExit = 0.32f;

        public static float IntensityTo01(MusicIntensity level)
        {
            return level switch
            {
                MusicIntensity.Calm => 0.15f,
                MusicIntensity.Intense => 0.85f,
                _ => 0.5f,
            };
        }

        public static MusicIntensity Quantize(float intensity01)
        {
            if (intensity01 > 0.66f) return MusicIntensity.Intense;
            if (intensity01 > 0.33f) return MusicIntensity.Tense;
            return MusicIntensity.Calm;
        }

        public static int QuantizeLevel(float intensity01) => (int)Quantize(intensity01);

        /// <summary>
        /// Calcule l'intensité 0..1 depuis l'état tactique.
        /// - Base 0.35 (combat installé) + danger PV + pression numérique + durée.
        /// - Bonus "dernier carré" si un seul allié tient face à plusieurs ennemis.
        /// - Plancher 0.75 sous 25% de PV : le mix doit sonner l'alarme.
        /// </summary>
        public static float ComputeIntensity(float allyHpRatio01, int alliesAlive, int enemiesAlive, int turnIndex)
        {
            allyHpRatio01 = Mathf.Clamp01(allyHpRatio01);
            float intensity = 0.35f;
            intensity += 0.30f * (1f - allyHpRatio01);
            intensity += 0.15f * Mathf.Clamp01(enemiesAlive / 4f);
            intensity += 0.10f * Mathf.Clamp01(turnIndex / 8f);
            if (alliesAlive == 1 && enemiesAlive >= 2)
                intensity += 0.15f;
            if (allyHpRatio01 < 0.25f)
                intensity = Mathf.Max(intensity, 0.75f);
            return Mathf.Clamp01(intensity);
        }

        /// <summary>
        /// Mappe l'état tactique vers les paramètres du moteur génératif infini.
        /// - BPM 104 -> 150 selon intensité, +8 si tension critique (urgence) ;
        /// - style batterie : War (calme) -> Tribal -> DoubleTime -> Blast (dernier carré) ;
        /// - gamme : Phrygien si danger extrême, Dorian si on domine, Naturel sinon ;
        /// - seed dérivé du tour pour garder une identité par combat tout en variant.
        /// Le scheduler incrémente generation à chaque boucle => jamais répétitif.
        /// </summary>
        public static GenerativeMusicParams ComputeGenerativeParams(
            float allyHpRatio01, int alliesAlive, int enemiesAlive, int turnIndex,
            int baseSeed, int generation, float strike01 = 0f,
            float brightness01 = 0.55f, float chaos01 = 0.35f,
            GenerativeStylePreset preset = GenerativeStylePreset.Auto)
        {
            allyHpRatio01 = Mathf.Clamp01(allyHpRatio01);
            float intensity = ComputeIntensity(allyHpRatio01, alliesAlive, enemiesAlive, turnIndex);
            float tension = Mathf.Clamp01(1f - allyHpRatio01 * 0.85f - Mathf.Clamp01(alliesAlive / 4f) * 0.15f);
            if (alliesAlive == 1 && enemiesAlive >= 2) tension = Mathf.Max(tension, 0.8f);
            if (allyHpRatio01 < 0.25f) tension = Mathf.Max(tension, 0.75f);

            float bpm, bright = brightness01, chaos = chaos01 + tension * 0.15f;
            GenerativeDrumStyle style;
            GenerativeScale scale;
            switch (preset)
            {
                case GenerativeStylePreset.Electronic:
                    bpm = 126f + intensity * 8f;
                    style = intensity > 0.8f ? GenerativeDrumStyle.DoubleTime : GenerativeDrumStyle.TribalTechno;
                    scale = allyHpRatio01 > 0.5f ? GenerativeScale.Dorian : GenerativeScale.NaturalMinor;
                    bright = Mathf.Max(bright, 0.65f);
                    break;
                case GenerativeStylePreset.DrumAndBass:
                    bpm = 164f + tension * 10f;
                    style = tension > 0.6f ? GenerativeDrumStyle.Blast : GenerativeDrumStyle.DoubleTime;
                    scale = tension > 0.5f ? GenerativeScale.HarmonicMinor : GenerativeScale.NaturalMinor;
                    bright = Mathf.Max(bright, 0.7f);
                    chaos += 0.1f;
                    break;
                case GenerativeStylePreset.Retro16Bit:
                    bpm = 108f + intensity * 12f;
                    style = intensity < 0.35f ? GenerativeDrumStyle.WarEnsemble : GenerativeDrumStyle.TribalTechno;
                    scale = tension > 0.7f ? GenerativeScale.HarmonicMinor : GenerativeScale.Dorian;
                    bright = Mathf.Min(bright, 0.55f);
                    break;
                case GenerativeStylePreset.ModernCinematic:
                    bpm = 116f + intensity * 16f;
                    if (intensity < 0.3f) style = GenerativeDrumStyle.WarEnsemble;
                    else if (intensity > 0.85f && tension > 0.6f) style = GenerativeDrumStyle.Blast;
                    else if (intensity > 0.55f) style = GenerativeDrumStyle.DoubleTime;
                    else style = GenerativeDrumStyle.TribalTechno;
                    scale = tension > 0.6f ? GenerativeScale.Phrygian : GenerativeScale.NaturalMinor;
                    break;
                default:
                    bpm = Mathf.Lerp(104f, 142f, intensity) + tension * 8f;
                    style = GenerativeDrumStyle.Auto;
                    if (intensity < 0.3f) style = GenerativeDrumStyle.WarEnsemble;
                    else if (intensity > 0.85f && tension > 0.6f) style = GenerativeDrumStyle.Blast;
                    scale = GenerativeScale.NaturalMinor;
                    if (tension > 0.72f) scale = GenerativeScale.Phrygian;
                    else if (intensity > 0.6f && allyHpRatio01 > 0.6f) scale = GenerativeScale.Dorian;
                    else if (tension > 0.55f) scale = GenerativeScale.HarmonicMinor;
                    break;
            }
            bpm = Mathf.Clamp(bpm, 96f, 180f);

            // Seed stable par combat (baseSeed) + léger hash du tour pour colorer
            // sans casser la famille motivique (generation porte la variation).
            int seed = unchecked(baseSeed * 31 + turnIndex / 4);

            return new GenerativeMusicParams
            {
                seed = seed,
                generation = generation,
                intensity01 = intensity,
                tension01 = tension,
                brightness01 = Mathf.Clamp01(bright),
                chaos01 = Mathf.Clamp01(chaos),
                rootMidi = tension > 0.7f ? 31 : 33, // G1 si danger (plus grave), A1 sinon
                scale = scale,
                drumStyle = style,
                preset = preset,
                bpm = bpm,
                bars = 8,
                strike01 = Mathf.Clamp01(strike01)
            };
        }

        /// <summary>
        /// Cible complète (mood + intensité) avec hystérésis sur l'axe Tension.
        /// - Bataille terminée (un camp à 0) : on garde le mood courant,
        ///   c'est l'arène qui imposera Victory/Defeat + stinger.
        /// - Hors critique : Combat. En critique : Tension (signal danger).
        /// - Si on vient déjà de Tension, on y reste jusqu'à CriticalHpExit.
        /// </summary>
        public static (MusicMood mood, float intensity) ComputeTarget(
            float allyHpRatio01, int alliesAlive, int enemiesAlive, int turnIndex,
            MusicMood currentMood, float currentIntensity01)
        {
            allyHpRatio01 = Mathf.Clamp01(allyHpRatio01);
            float intensity = ComputeIntensity(allyHpRatio01, alliesAlive, enemiesAlive, turnIndex);

            // Combat plié : ne pas deviner Victory/Defeat ici (l'arène tranche).
            if (alliesAlive <= 0 || enemiesAlive <= 0)
                return (currentMood == MusicMood.None ? MusicMood.Combat : currentMood, intensity);

            MusicMood mood;
            if (currentMood == MusicMood.Tension)
                mood = allyHpRatio01 < CriticalHpExit ? MusicMood.Tension : MusicMood.Combat;
            else
                mood = allyHpRatio01 <= CriticalHpEnter ? MusicMood.Tension : MusicMood.Combat;

            return (mood, intensity);
        }
    }
}

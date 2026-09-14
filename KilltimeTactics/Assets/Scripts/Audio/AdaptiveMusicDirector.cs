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

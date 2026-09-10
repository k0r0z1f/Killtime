using System;
using UnityEngine;

namespace Killtime.Core.Character
{
    /// <summary>
    /// Les 8 Attributs fondamentaux et valeurs sensorielles du Système RP (Livre I, Chap. 2 & 3).
    /// </summary>
    [Serializable]
    public struct Attributes
    {
        [Header("Attributs Physiques")]
        public int Force;         // FOR : Puissance musculaire, impact, charge
        public int Agilite;       // AGI : Dextérité, coordination, esquive
        public int Constitution;  // CON : Santé, encaissement, seuils de mort
        public int Rapidite;      // RAP : Vitesse d'exécution et réflexes

        [Header("Attributs Mentaux & Sociaux")]
        public int Intelligence;  // INT : Raisonnement, logique, déduction
        public int Erudition;     // ÉRU : Savoirs, médecine, nombre de compétences entraînées
        public int Charisme;      // CHA : Magnétisme, présence, négociation
        public int Instinct;      // INS : Sagesse, sixième sens, intuition

        [Header("Spécial & Sensoriel")]
        public int Magie;         // MAG : Résonance avec la Cinquième Force (0 si non-éveillé)
        public int Vision;        // Déterminé au d6 (min selon l'espèce)
        public int Ouie;          // Déterminé au d6 (min selon l'espèce)
        public int PointsMiracle;  // Destin héroïque (1 de base pour un PJ)

        /// <summary>
        /// Constructeur complet avec les 8 attributs officiels du Livre I.
        /// </summary>
        public Attributes(int @for, int agi, int con, int rap, int @int, int eru, int cha, int ins, int mag = 0, int vision = 3, int ouie = 3, int miracle = 1)
        {
            Force = Math.Clamp(@for, 1, 10);
            Agilite = Math.Clamp(agi, 1, 10);
            Constitution = Math.Clamp(con, 1, 10);
            Rapidite = Math.Clamp(rap, 1, 10);
            Intelligence = Math.Clamp(@int, 1, 10);
            Erudition = Math.Clamp(eru, 1, 10);
            Charisme = Math.Clamp(cha, 1, 10);
            Instinct = Math.Clamp(ins, 1, 10);
            Magie = Math.Clamp(mag, 0, 10);
            Vision = Math.Clamp(vision, 1, 6);
            Ouie = Math.Clamp(ouie, 1, 6);
            PointsMiracle = Math.Max(0, miracle);
        }

        /// <summary>
        /// Constructeur de compatibilité (6 attributs historiques).
        /// </summary>
        public Attributes(int agi, int @int, int rap, int con, int @for, int cha)
            : this(@for: @for, agi: agi, con: con, rap: rap, @int: @int, eru: @int, cha: cha, ins: agi, mag: 0, vision: 3, ouie: 3, miracle: 1)
        {
        }

        /// <summary>
        /// Renvoie la valeur minimale parmi les 8 attributs principaux.
        /// </summary>
        public int GetMinimumAttribute()
        {
            int min = Force;
            if (Agilite < min) min = Agilite;
            if (Constitution < min) min = Constitution;
            if (Rapidite < min) min = Rapidite;
            if (Intelligence < min) min = Intelligence;
            if (Erudition < min) min = Erudition;
            if (Charisme < min) min = Charisme;
            if (Instinct < min) min = Instinct;
            return min;
        }

        /// <summary>
        /// Formule officielle des Points d'Action (Livre I, Chapitre 4) :
        /// PA = Max(Agilité, Intelligence) + Rapidité + Min(Principaux)
        /// </summary>
        public int CalculateBaseActionPoints()
        {
            int maxAgiInt = Math.Max(Agilite, Intelligence);
            int minAttr = GetMinimumAttribute();
            return maxAgiInt + Rapidite + minAttr;
        }

        /// <summary>
        /// Capacité d'Encaissement : Constitution × 2
        /// </summary>
        public int CalculateEncaissement() => Constitution * 2;

        /// <summary>
        /// Maximum Létal : Constitution × 5
        /// </summary>
        public int CalculateLethalMaximum() => Constitution * 5;
    }
}
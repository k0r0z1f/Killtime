using System;

namespace Killtime.Core.Character
{
    /// <summary>
    /// Les 6 Attributs fondamentaux d'un protagoniste du Système RP (Livre I).
    /// </summary>
    [Serializable]
    public struct Attributes
    {
        public int Agilite;       // Agi : Souplesse, esquive, coordination fine
        public int Intelligence;  // Int : Raisonnement, mémoire, calcul arcanique
        public int Rapidite;      // Rap : Vitesse d'exécution et réflexes
        public int Constitution;  // Con : Robustesse organique, seuils d'encaissement et de mort
        public int Force;         // For : Puissance musculaire, charge, impact physique
        public int Charisme;      // Cha : Présence, magnétisme, ascendant social

        public Attributes(int agi, int @int, int rap, int con, int @for, int cha)
        {
            Agilite = agi;
            Intelligence = @int;
            Rapidite = rap;
            Constitution = con;
            Force = @for;
            Charisme = cha;
        }

        /// <summary>
        /// Renvoie l'attribut le plus faible parmi les 6 attributs fondamentaux.
        /// </summary>
        public int GetMinimumAttribute()
        {
            int min = Agilite;
            if (Intelligence < min) min = Intelligence;
            if (Rapidite < min) min = Rapidite;
            if (Constitution < min) min = Constitution;
            if (Force < min) min = Force;
            if (Charisme < min) min = Charisme;
            return min;
        }

        /// <summary>
        /// Formule officielle de calcul des Points d'Action (PA) à chaque tour de 10 secondes :
        /// PA = Max(Agilité, Intelligence) + Rapidité + Min(Principaux)
        /// </summary>
        public int CalculateBaseActionPoints()
        {
            int maxAgiInt = Math.Max(Agilite, Intelligence);
            int minAttr = GetMinimumAttribute();
            return maxAgiInt + Rapidite + minAttr;
        }

        /// <summary>
        /// Capacité d'absorption physique avant traumatisme ou dégâts profonds.
        /// Valeur officielle : Constitution × 2
        /// </summary>
        public int CalculateEncaissement()
        {
            return Constitution * 2;
        }

        /// <summary>
        /// Seuil de mort clinique ou d'inconscience totale.
        /// Valeur officielle : Constitution × 5
        /// </summary>
        public int CalculateLethalMaximum()
        {
            return Constitution * 5;
        }
    }
}

using System;

namespace Killtime.Core.Character
{
    /// <summary>
    /// Compendium officiel des 16 États & Altérations de statut du Système RP (Livre VII).
    /// </summary>
    [Flags]
    public enum StatusEffect
    {
        None = 0,
        Destabilise = 1 << 0,  // -2 aux épreuves, vitesse rampante (coût PA doublé pour marcher)
        Etourdi = 1 << 1,      // -1 sur tous les jets
        Immobilise = 1 << 2,   // Mouvement impossible
        Paralyse = 1 << 3,     // Incapacité totale (aucune action/réaction)
        Sonne = 1 << 4,        // Tombe à terre, aucune action
        ATerre = 1 << 5,       // -1 attaque, vitesse rampante
        Ralenti = 1 << 6,      // Toute action coûte le double de PA
        Agonisant = 1 << 7,    // Panique nerveuse, fuite aléatoire
        Inconscient = 1 << 8,  // Coma clinique ou KO total
        Aveugle = 1 << 9,      // -4 aux attaques à distance, vision nulle
        Sourd = 1 << 10,       // Perte d'initiative réflexe
        Asphyxie = 1 << 11,    // Perte continue de PA et suffocation
        Empoisonne = 1 << 12,  // Dégâts toxiques par tour
        EnFeu = 1 << 13,       // Dégâts thermiques continus
        Saignement = 1 << 14,  // Hémorragie (perte de PV par tour sans compression)
        ChronoFracture = 1 << 15 // Déphasage temporel (Livre V)
    }
}

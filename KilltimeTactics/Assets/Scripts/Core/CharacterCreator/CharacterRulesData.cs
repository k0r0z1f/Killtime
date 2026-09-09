using System;
using UnityEngine;

namespace Killtime.Core.Character
{
    public enum SpeciesType
    {
        Humain,     // Multiples mondes : polyvalent, aucun malus, sens min 3
        Drakka,     // Hybris Ouest : +1 FOR, +1 CON, -1 INT, -1 CHA, sens min 3
        Taurien,    // Hybris Forêt : +1 AGI, +1 RAP, magie normale, sens min 3
        Cleien,     // Cléia : -1 FOR, -1 CON, +1 INT, +1 ÉRU, +1 INS, sens min 4
        Mikyai,     // Karkjiue : +1 FOR, +1 CON, -1 ÉRU, -1 CHA, sens min 3
        Vardien     // Vardis & Frontières : +1 CON, +1 INS, sens min 3
    }

    public enum CharacterProfileType
    {
        HerosPJ,    // 1 attribut à 5, 1 à 4, 15 pts secondaires (17 si Magie > 0), max 3 restant
        PnjNormal,  // 2 attributs à 4, 15 pts secondaires (17 si Magie > 0), max 3 restant
        PnjSbire,   // "Petite Merde" : 12 pts au total à répartir, max 3 par carac
        PnjBoss     // Boss de scénario : budget étendu libre (Livre XI)
    }

    public static class SpeciesRules
    {
        public static (int dFor, int dAgi, int dCon, int dRap, int dInt, int dEru, int dCha, int dIns, int minSense) GetModifiers(SpeciesType species)
        {
            return species switch
            {
                SpeciesType.Humain => (0, 0, 0, 0, 0, 0, 0, 0, 3),
                SpeciesType.Drakka => (1, 0, 1, 0, -1, 0, -1, 0, 3),
                SpeciesType.Taurien => (0, 1, 0, 1, 0, 0, 0, 0, 3),
                SpeciesType.Cleien => (-1, 0, -1, 0, 1, 1, 0, 1, 4),
                SpeciesType.Mikyai => (1, 0, 1, 0, 0, -1, -1, 0, 3),
                SpeciesType.Vardien => (0, 0, 1, 0, 0, 0, 0, 1, 3),
                _ => (0, 0, 0, 0, 0, 0, 0, 0, 3)
            };
        }
    }
}
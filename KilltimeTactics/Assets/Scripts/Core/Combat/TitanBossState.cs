using System.Collections.Generic;

namespace Killtime.Core.Character
{
    /// <summary>
    /// États tactiques des Boss Titans (Livre XII §49-50 — RD-087).
    /// Pur C# testable (aucune dépendance Unity) : compteurs par combat,
    /// réinitialisés par <see cref="LivreXIPNJState.ClearCombatState"/>.
    /// Clés = références CharacterStats (comme LivreXIPNJState, RD-088).
    /// </summary>
    public static class TitanBossState
    {
        /// <summary>Round courant (synchronisé par le TurnManager ; les tests l'écrivent directement).</summary>
        public static int CurrentRound = 1;

        // Bête d'Hybris — Mémoire des Flammes : nombre d'attaques de feu déjà
        // subies dans le combat courant (réduction = 2 x ce compteur).
        private static readonly Dictionary<CharacterStats, int> _flameHits = new();

        // Disciple de Terre — Choc Thermique : round du dernier coup de froid
        // subi + fissuration de cuirasse en cours (armure d'origine à restaurer).
        private static readonly Dictionary<CharacterStats, int> _lastColdRound = new();
        private static readonly Dictionary<CharacterStats, int> _armorCrackedUntilRound = new();
        private static readonly Dictionary<CharacterStats, int> _armorBeforeCrack = new();

        // Minulican — Absorption Thermique : total converti dans le combat (traçabilité).
        private static readonly Dictionary<CharacterStats, int> _absorbedTotal = new();

        // ------------------------------------------------------------------
        // Mémoire des Flammes
        // ------------------------------------------------------------------

        public static int GetFlameHits(CharacterStats beast)
        {
            return beast != null && _flameHits.TryGetValue(beast, out int n) ? n : 0;
        }

        public static void RegisterFlameHit(CharacterStats beast)
        {
            if (beast == null) return;
            _flameHits[beast] = GetFlameHits(beast) + 1;
        }

        // ------------------------------------------------------------------
        // Choc Thermique (froid puis feu, fenêtre 2 tours)
        // ------------------------------------------------------------------

        public static void RegisterColdHit(CharacterStats victim, int round)
        {
            if (victim == null) return;
            _lastColdRound[victim] = round;
        }

        public static bool HasRecentColdHit(CharacterStats victim, int round, int windowRounds)
        {
            if (victim == null) return false;
            if (!_lastColdRound.TryGetValue(victim, out int coldRound)) return false;
            int delta = round - coldRound;
            return delta >= 0 && delta <= windowRounds;
        }

        public static void ClearColdHit(CharacterStats victim)
        {
            if (victim != null) _lastColdRound.Remove(victim);
        }

        public static bool IsArmorCracked(CharacterStats victim, int round)
        {
            return victim != null
                && _armorCrackedUntilRound.TryGetValue(victim, out int until)
                && round <= until;
        }

        public static void CrackArmor(CharacterStats victim, int round, int durationRounds)
        {
            if (victim == null) return;
            if (!_armorBeforeCrack.ContainsKey(victim))
                _armorBeforeCrack[victim] = victim.BaseArmorAbsorption;
            _armorCrackedUntilRound[victim] = round + durationRounds;
            victim.BaseArmorAbsorption = 0;
        }

        /// <summary>
        /// Restaure l'armure d'origine si la fissuration a expiré. Retourne vrai
        /// si une restauration a eu lieu. Appelé à chaque coup subi + tick de tour.
        /// </summary>
        public static bool UpdateArmorCrack(CharacterStats victim, int round)
        {
            if (victim == null) return false;
            if (!_armorCrackedUntilRound.TryGetValue(victim, out int until)) return false;
            if (round <= until) return false;
            _armorCrackedUntilRound.Remove(victim);
            if (_armorBeforeCrack.TryGetValue(victim, out int original))
            {
                _armorBeforeCrack.Remove(victim);
                victim.BaseArmorAbsorption = original;
            }
            return true;
        }

        // ------------------------------------------------------------------
        // Absorption Thermique (traçabilité)
        // ------------------------------------------------------------------

        public static void AddAbsorbed(CharacterStats minulican, int amount)
        {
            if (minulican == null || amount <= 0) return;
            _absorbedTotal[minulican] = GetAbsorbedTotal(minulican) + amount;
        }

        public static int GetAbsorbedTotal(CharacterStats minulican)
        {
            return minulican != null && _absorbedTotal.TryGetValue(minulican, out int n) ? n : 0;
        }

        // ------------------------------------------------------------------

        public static void ClearCombatState()
        {
            _flameHits.Clear();
            _lastColdRound.Clear();
            _armorCrackedUntilRound.Clear();
            _armorBeforeCrack.Clear();
            _absorbedTotal.Clear();
            CurrentRound = 1;
        }
    }
}

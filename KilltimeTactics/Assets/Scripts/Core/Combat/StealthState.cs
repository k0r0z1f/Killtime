using System.Collections.Generic;
using Killtime.Core.Character;

namespace Killtime.Core.Combat
{
    /// <summary>
    /// Gestionnaire d'état de la Furtivité et de l'Embuscade (RD-049, Système du Codex) :
    /// - Une unité non détectée / en furtivité est considérée invisible dans le brouillard de guerre.
    /// - Première attaque depuis la furtivité : +2 à l'attaque et aucune réaction adverse autorisée.
    /// - Toute attaque portée, tir bruyant ou détection rompt immédiatement la furtivité.
    /// Keyé sur CharacterStats pour rester dans la couche Core (agnostique Unity).
    /// </summary>
    public static class StealthState
    {
        public const int StealthAttackBonus = 2;
        public const int EnterStealthAPCost = 2;

        private static readonly HashSet<CharacterStats> _stealthedUnits = new();

        public static bool IsStealthed(CharacterStats unit)
        {
            if (unit == null || !unit.IsAlive) return false;
            return _stealthedUnits.Contains(unit);
        }

        public static void SetStealth(CharacterStats unit, bool stealthed)
        {
            if (unit == null) return;
            if (stealthed)
            {
                if (unit.IsAlive) _stealthedUnits.Add(unit);
            }
            else
            {
                _stealthedUnits.Remove(unit);
            }
        }

        public static bool BreakStealth(CharacterStats unit)
        {
            if (unit == null) return false;
            return _stealthedUnits.Remove(unit);
        }

        public static void Cancel(CharacterStats unit)
        {
            if (unit == null) return;
            _stealthedUnits.Remove(unit);
        }

        public static void ClearAll()
        {
            _stealthedUnits.Clear();
        }
    }
}
using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics.Units;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Objectives;
using Killtime.Core.Character;
using Killtime.Core.Combat;
using Killtime.Core.Dice;
using Killtime.Core.Rules;

namespace Killtime.Tactics.TurnSystem
{
    public partial class TurnManager : MonoBehaviour
    {
        // Jet d'initiative (Livre I §4.3) : DiceRoller dédié (injectable pour tests
        // déterministes). Les résultats sont stockés sur unit.Stats afin de vivre avec
        // l'unité (détruite → relevé détruit, aucun dictionnaire d'objets à purger).
        private DiceRoller _diceRoller = new DiceRoller();
        private bool _needsInitiativeRoll = true;

        public void SetDiceSeed(int seed) => _diceRoller = new DiceRoller(seed);
        public void SetDiceRoller(DiceRoller roller) => _diceRoller = roller ?? new DiceRoller();

        public bool HasInitiativeRolled(TacticalUnit unit)
        {
            return unit != null && unit.Stats != null
                && unit.Stats.InitiativeRollTotal != int.MinValue;
        }

        public int GetInitiativeTotal(TacticalUnit unit)
        {
            if (unit == null || unit.Stats == null) return int.MinValue;
            return unit.Stats.InitiativeRollTotal != int.MinValue
                ? unit.Stats.InitiativeRollTotal
                : int.MinValue;
        }

        public DiceType GetInitiativeDieFor(TacticalUnit unit)
        {
            if (unit == null || unit.Stats == null) return DiceType.D2;
            return unit.Stats.InitiativeRollTotal != int.MinValue
                ? unit.Stats.InitiativeDie
                : unit.Stats.GetInitiativeDie();
        }

        /// <summary>
        /// RD-084 (Livre VI §25.2) : initiative de synchronisation d'un assaut
        /// groupé = celle du membre le plus lent (minimum des totaux roulés).
        /// Les unités sans jet enregistré sont ignorées ; int.MinValue si aucune.
        /// </summary>
        public int GetSlowestInitiativeFor(System.Collections.Generic.IReadOnlyList<TacticalUnit> units)
        {
            if (units == null || units.Count == 0) return int.MinValue;
            int slowest = int.MaxValue;
            bool found = false;
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (u == null || u.Stats == null) continue;
                if (u.Stats.InitiativeRollTotal == int.MinValue) continue;
                found = true;
                if (u.Stats.InitiativeRollTotal < slowest) slowest = u.Stats.InitiativeRollTotal;
            }
            return found ? slowest : int.MinValue;
        }

        /// <summary>
        /// Roule l'initiative d'une unité si absente des relevés (renfort mid-combat).
        /// Résultat stocké sur unit.Stats : il meurt avec l'unité, aucune purge nécessaire.
        /// </summary>
        private void EnsureInitiativeFor(TacticalUnit unit)
        {
            if (unit == null || unit.Stats == null) return;
            if (HasInitiativeRolled(unit)) return;

            var die = unit.Stats.GetInitiativeDie();
            unit.Stats.InitiativeDie = die;
            unit.Stats.InitiativeRollTotal = _diceRoller.Roll(die, 0, 0).RawRoll;
        }

        /// <summary>
        /// Force un nouveau jet d'initiative général (ex: bouton dev, nouveau combat).
        /// </summary>
        public void RerollInitiative()
        {
            _needsInitiativeRoll = true;
            StartNewRound();
        }

        /// <summary>
        /// Jet d'initiative général (Livre I §4.3) : chaque unité lance son dé
        /// (max RAP/AGI/INT → paliers), tri décroissant, tie-break RAP puis AGI/INT.
        /// Résultats stockés sur unit.Stats ; l'ordre reste figé pour les rounds suivants.
        /// </summary>
        private void RollInitiativeAndSort()
        {
            _allUnits.RemoveAll(u => u == null);

            for (int i = 0; i < _allUnits.Count; i++)
            {
                var u = _allUnits[i];
                if (u == null || u.Stats == null) continue;
                var die = u.Stats.GetInitiativeDie();
                u.Stats.InitiativeDie = die;
                u.Stats.InitiativeRollTotal = _diceRoller.Roll(die, 0, 0).RawRoll;
            }

            _allUnits.Sort((a, b) =>
            {
                if (a == null || a.Stats == null) return 1;
                if (b == null || b.Stats == null) return -1;

                bool aIsTitan = a.FootprintType != TitanFootprintType.Single;
                bool bIsTitan = b.FootprintType != TitanFootprintType.Single;
                if (aIsTitan != bIsTitan) return bIsTitan.CompareTo(aIsTitan);

                int initA = GetInitiativeTotal(a);
                int initB = GetInitiativeTotal(b);
                if (initA != initB) return initB.CompareTo(initA);
                int rapCmp = b.Stats.Attributes.Rapidite.CompareTo(a.Stats.Attributes.Rapidite);
                if (rapCmp != 0) return rapCmp;
                int agiCmp = b.Stats.Attributes.Agilite.CompareTo(a.Stats.Attributes.Agilite);
                if (agiCmp != 0) return agiCmp;
                int intCmp = b.Stats.Attributes.Intelligence.CompareTo(a.Stats.Attributes.Intelligence);
                if (intCmp != 0) return intCmp;
                return string.Compare(a.Stats.Name, b.Stats.Name, StringComparison.Ordinal);
            });

            _needsInitiativeRoll = false;

            var results = new List<InitiativeRollResult>(_allUnits.Count);
            for (int i = 0; i < _allUnits.Count; i++)
            {
                var u = _allUnits[i];
                if (u == null || u.Stats == null) continue;
                results.Add(new InitiativeRollResult
                {
                    Unit = u,
                    Die = u.Stats.InitiativeDie,
                    RawRoll = u.Stats.InitiativeRollTotal,
                    Total = u.Stats.InitiativeRollTotal
                });
            }
            OnInitiativeRolled?.Invoke(results);
        }
    }
}

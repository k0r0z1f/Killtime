using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics.Units;
using Killtime.Tactics.Grid;

namespace Killtime.Tactics.TurnSystem
{
    /// <summary>
    /// Gestionnaire de la séquence de tour (Le Tour de 10 Secondes du Livre VI).
    /// </summary>
    public class TurnManager : MonoBehaviour
    {
        [Header("Références")]
        [SerializeField] private TacticalHexGrid _grid;
        [SerializeField] private List<TacticalUnit> _allUnits = new();

        public int CurrentRound { get; private set; } = 1;
        public TacticalUnit ActiveUnit { get; private set; }
        private int _activeUnitIndex = 0;

        public event Action<TacticalUnit> OnTurnStarted;
        public event Action<int> OnRoundStarted;

        private void Start()
        {
            if (_allUnits.Count > 0)
            {
                StartNewRound();
            }
        }

        public void RegisterUnit(TacticalUnit unit)
        {
            if (!_allUnits.Contains(unit))
            {
                _allUnits.Add(unit);
            }
        }

        public void StartNewRound()
        {
            // Tri par initiative selon Rapidité (Livre I & VI)
            _allUnits.Sort((a, b) => b.Stats.Attributes.Rapidite.CompareTo(a.Stats.Attributes.Rapidite));

            foreach (var unit in _allUnits)
            {
                if (unit.Stats.IsAlive)
                {
                    unit.Stats.ResetTurn();
                }
            }

            _activeUnitIndex = 0;
            OnRoundStarted?.Invoke(CurrentRound);
            StartUnitTurn();
        }

        public void EndCurrentTurn()
        {
            _activeUnitIndex++;

            // Trouver la prochaine unité en vie
            while (_activeUnitIndex < _allUnits.Count && !_allUnits[_activeUnitIndex].Stats.IsAlive)
            {
                _activeUnitIndex++;
            }

            if (_activeUnitIndex >= _allUnits.Count)
            {
                CurrentRound++;
                StartNewRound();
            }
            else
            {
                StartUnitTurn();
            }
        }

        private void StartUnitTurn()
        {
            ActiveUnit = _allUnits[_activeUnitIndex];
            OnTurnStarted?.Invoke(ActiveUnit);
        }
    }
}

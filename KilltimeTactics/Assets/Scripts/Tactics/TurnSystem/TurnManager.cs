using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics.Units;
using Killtime.Tactics.Grid;

namespace Killtime.Tactics.TurnSystem
{
    public enum CombatOutcome
    {
        InProgress,
        Victory, // Tous les ennemis neutralisés
        Defeat   // Tous les héros/joueurs neutralisés
    }

    /// <summary>
    /// Gestionnaire de la séquence de tour (Le Tour de 10 Secondes du Livre VI).
    /// Évalue en temps réel les conditions de victoire ou défaite.
    /// </summary>
    public class TurnManager : MonoBehaviour
    {
        [Header("Références")]
        [SerializeField] private TacticalHexGrid _grid;
        [SerializeField] private List<TacticalUnit> _allUnits = new();

        public int CurrentRound { get; private set; } = 1;
        public TacticalUnit ActiveUnit { get; private set; }
        public bool IsCombatOver { get; private set; } = false;
        public CombatOutcome CurrentOutcome { get; private set; } = CombatOutcome.InProgress;

        private int _activeUnitIndex = 0;

        public event Action<TacticalUnit> OnTurnStarted;
        public event Action<int> OnRoundStarted;
        public event Action<CombatOutcome> OnCombatEnded;

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

        public void ClearUnits()
        {
            _allUnits.Clear();
            ActiveUnit = null;
            _activeUnitIndex = 0;
        }

        public void ResetCombatState()
        {
            IsCombatOver = false;
            CurrentOutcome = CombatOutcome.InProgress;
            CurrentRound = 1;
            _activeUnitIndex = 0;
            _allUnits.RemoveAll(u => u == null);
        }

        public bool CheckCombatOver()
        {
            if (IsCombatOver) return true;
            if (_allUnits.Count == 0) return false;

            int playerUnitsAlive = 0;
            int enemyUnitsAlive = 0;

            foreach (var unit in _allUnits)
            {
                if (unit == null || unit.Stats == null) continue;

                if (unit.Stats.IsAlive)
                {
                    if (unit.IsPlayerControlled)
                        playerUnitsAlive++;
                    else
                        enemyUnitsAlive++;
                }
            }

            if (playerUnitsAlive == 0 || enemyUnitsAlive == 0)
            {
                IsCombatOver = true;
                CurrentOutcome = (playerUnitsAlive > 0) ? CombatOutcome.Victory : CombatOutcome.Defeat;
                OnCombatEnded?.Invoke(CurrentOutcome);
                return true;
            }

            return false;
        }

        public void StartNewRound()
        {
            _allUnits.RemoveAll(u => u == null);

            if (CheckCombatOver()) return;

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
            if (CheckCombatOver()) return;

            _activeUnitIndex++;

            // Trouver la prochaine unité en vie
            while (_activeUnitIndex < _allUnits.Count && !_allUnits[_activeUnitIndex].Stats.IsAlive)
            {
                _activeUnitIndex++;
            }

            if (_activeUnitIndex >= _allUnits.Count)
            {
                if (CheckCombatOver()) return;

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

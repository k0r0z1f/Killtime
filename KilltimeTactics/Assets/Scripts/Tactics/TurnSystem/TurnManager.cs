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
            if (unit == null || _allUnits.Contains(unit)) return;

            _allUnits.Add(unit);

            if (unit.Stats != null && unit.Stats.IsAlive)
            {
                unit.Stats.ResetTurn();
            }

            if (IsCombatOver)
            {
                IsCombatOver = false;
                CurrentOutcome = CombatOutcome.InProgress;
                StartNewRound();
                return;
            }

            if (ActiveUnit == null || !_allUnits.Contains(ActiveUnit))
            {
                StartNewRound();
                return;
            }

            InsertUnitIntoCurrentRound(unit);
        }

        private void InsertUnitIntoCurrentRound(TacticalUnit newUnit)
        {
            int currentIndex = _allUnits.IndexOf(ActiveUnit);
            if (currentIndex < 0) return;

            int remainingStart = currentIndex + 1;
            int remainingCount = _allUnits.Count - remainingStart;

            if (remainingCount > 1)
            {
                _allUnits.Sort(remainingStart, remainingCount, Comparer<TacticalUnit>.Create((a, b) =>
                {
                    int rapA = (a != null && a.Stats != null) ? a.Stats.Attributes.Rapidite : 0;
                    int rapB = (b != null && b.Stats != null) ? b.Stats.Attributes.Rapidite : 0;
                    return rapB.CompareTo(rapA);
                }));
            }

            _activeUnitIndex = _allUnits.IndexOf(ActiveUnit);
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

            // Sauter immédiatement les unités mortes au début du round
            while (_activeUnitIndex < _allUnits.Count && !_allUnits[_activeUnitIndex].Stats.IsAlive)
            {
                _activeUnitIndex++;
            }

            if (_activeUnitIndex >= _allUnits.Count)
            {
                if (CheckCombatOver()) return;

                CurrentRound++;
                StartNewRound();
                return;
            }

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
            if (_activeUnitIndex < 0 || _activeUnitIndex >= _allUnits.Count) return;

            ActiveUnit = _allUnits[_activeUnitIndex];

            // Sécurité absolue : si l'unité est morte ou invalide, passer immédiatement le tour
            if (ActiveUnit == null || ActiveUnit.Stats == null || !ActiveUnit.Stats.IsAlive)
            {
                EndCurrentTurn();
                return;
            }

            OnTurnStarted?.Invoke(ActiveUnit);
        }
    }
}

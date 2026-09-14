using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics.Units;
using Killtime.Tactics.Grid;
using Killtime.Core.Character;
using Killtime.Core.Dice;

namespace Killtime.Tactics.TurnSystem
{
    public enum CombatOutcome
    {
        InProgress,
        Victory, // Tous les ennemis neutralisés
        Defeat   // Tous les héros/joueurs neutralisés
    }

    /// <summary>
    /// Résultat d'un jet d'initiative (Livre I §4.3).
    /// </summary>
    [Serializable]
    public struct InitiativeRollResult
    {
        public TacticalUnit Unit;
        public DiceType Die;
        public int RawRoll;
        public int Total;
    }

    /// <summary>
    /// Gestionnaire de la séquence de tour (Le Tour de 10 Secondes du Livre VI).
    /// Initiative (Livre I §4.3) : jet au début du combat sur max(RAP, AGI, INT),
    /// ordre conservé ensuite. Réévalue en temps réel les conditions de victoire ou défaite.
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

        // Jet d'initiative (Livre I §4.3) : DiceRoller dédié (injectable pour tests
        // déterministes). Les résultats sont stockés sur unit.Stats afin de vivre avec
        // l'unité (détruite → relevé détruit, aucun dictionnaire d'objets à purger).
        private DiceRoller _diceRoller = new DiceRoller();
        private bool _needsInitiativeRoll = true;

        public event Action<TacticalUnit> OnTurnStarted;
        public event Action<int> OnRoundStarted;
        public event Action<CombatOutcome> OnCombatEnded;
        public event Action<TacticalUnit, List<StatusEffect>> OnUnitStatusExpired;
        public event Action<IReadOnlyList<InitiativeRollResult>> OnInitiativeRolled;

        /// <summary>
        /// Ordre de passage actuel (après jet d'initiative).
        /// </summary>
        public IReadOnlyList<TacticalUnit> TurnOrder => _allUnits.AsReadOnly();

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
                CurrentRound = 1;
                _needsInitiativeRoll = true;
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

            // Renfort mid-combat (choix mixte) : pas de re-jet général.
            // On roule l'initiative du nouvel arrivant puis on l'insère parmi les
            // unités restantes selon (initiative, puis RAP en tie-break).
            EnsureInitiativeFor(newUnit);

            int remainingStart = currentIndex + 1;
            int remainingCount = _allUnits.Count - remainingStart;

            if (remainingCount > 1)
            {
                _allUnits.Sort(remainingStart, remainingCount, Comparer<TacticalUnit>.Create((a, b) =>
                {
                    int initA = GetInitiativeTotal(a);
                    int initB = GetInitiativeTotal(b);
                    if (initA != initB) return initB.CompareTo(initA);
                    int rapA = (a != null && a.Stats != null) ? a.Stats.Attributes.Rapidite : 0;
                    int rapB = (b != null && b.Stats != null) ? b.Stats.Attributes.Rapidite : 0;
                    return rapB.CompareTo(rapA);
                }));
            }

            _activeUnitIndex = _allUnits.IndexOf(ActiveUnit);
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

        public void ClearUnits()
        {
            _allUnits.Clear();
            ActiveUnit = null;
            _activeUnitIndex = 0;
            _needsInitiativeRoll = true;
        }

        public void UnregisterUnit(TacticalUnit unit)
        {
            if (unit == null) return;

            bool wasActive = (ActiveUnit == unit);
            int idx = _allUnits.IndexOf(unit);
            if (idx >= 0)
            {
                _allUnits.RemoveAt(idx);
            }

            if (CheckCombatOver()) return;

            if (wasActive)
            {
                _activeUnitIndex = idx;
                while (_activeUnitIndex < _allUnits.Count
                    && (_allUnits[_activeUnitIndex] == null
                        || _allUnits[_activeUnitIndex].Stats == null
                        || !_allUnits[_activeUnitIndex].Stats.IsAlive))
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
            else if (ActiveUnit != null)
            {
                _activeUnitIndex = _allUnits.IndexOf(ActiveUnit);
            }
        }

        public void ResetCombatState()
        {
            IsCombatOver = false;
            CurrentOutcome = CombatOutcome.InProgress;
            CurrentRound = 1;
            _activeUnitIndex = 0;
            _allUnits.RemoveAll(u => u == null);
            _needsInitiativeRoll = true;
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

            if (_needsInitiativeRoll || CurrentRound <= 1)
            {
                // Début de combat / reset : jet d'initiative général (Livre I §4.3).
                RollInitiativeAndSort();
            }
            else
            {
                // Rounds suivants : ordre conservé (pas de re-tri, pas de re-jet).
                // Les renforts sans relevé reçoivent un jet individuel (sans reshuffle).
                for (int i = 0; i < _allUnits.Count; i++)
                {
                    EnsureInitiativeFor(_allUnits[i]);
                }
            }

            foreach (var unit in _allUnits)
            {
                if (unit != null && unit.Stats != null && unit.Stats.IsAlive)
                {
                    unit.Stats.ResetTurn();
                }
            }

            _activeUnitIndex = 0;

            // Sauter immédiatement les unités mortes au début du round
            while (_activeUnitIndex < _allUnits.Count
                && (_allUnits[_activeUnitIndex] == null
                    || _allUnits[_activeUnitIndex].Stats == null
                    || !_allUnits[_activeUnitIndex].Stats.IsAlive))
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

            if (ActiveUnit != null && ActiveUnit.Stats != null)
            {
                var expired = ActiveUnit.Stats.TickTurnStatusDurations();
                if (expired.Count > 0)
                {
                    var vis = ActiveUnit.GetComponent<TacticalUnitVisual>();
                    for (int i = 0; i < expired.Count; i++)
                    {
                        vis?.SpawnFloatingText($"[{expired[i]}] Dissipé", new Color(0.4f, 0.9f, 1.0f));
                    }
                    OnUnitStatusExpired?.Invoke(ActiveUnit, expired);
                }
            }

            _activeUnitIndex++;

            // Trouver la prochaine unité en vie
            while (_activeUnitIndex < _allUnits.Count
                && (_allUnits[_activeUnitIndex] == null
                    || _allUnits[_activeUnitIndex].Stats == null
                    || !_allUnits[_activeUnitIndex].Stats.IsAlive))
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

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Core.Combat;

namespace Killtime.Tactics.AI
{
    public enum CombatAIMode
    {
        Normal,   // IA uniquement sur les ennemis/PNJ
        FullAuto  // IA sur l'intégralité des unités (100% automatisé)
    }

    /// <summary>
    /// Contrôleur d'Intelligence Artificielle tactique.
    /// Orchestre les déplacements et les attaques chirurgicales selon le mode actif.
    /// </summary>
    public class TacticalAIController : MonoBehaviour
    {
        [Header("Systèmes")]
        [SerializeField] private TurnManager _turnManager;
        [SerializeField] private TacticalHexGrid _grid;
        [SerializeField] private CombatDevArena _arena;

        [Header("Paramètres IA")]
        [SerializeField] private CombatAIMode _mode = CombatAIMode.Normal;
        [SerializeField] private bool _isAIEnabled = true;
        [SerializeField] private float _actionDelay = 0.45f;

        public CombatAIMode Mode
        {
            get => _mode;
            set => _mode = value;
        }

        public bool IsAIEnabled
        {
            get => _isAIEnabled;
            set => _isAIEnabled = value;
        }

        public float ActionDelay
        {
            get => _actionDelay;
            set => _actionDelay = Mathf.Max(0.05f, value);
        }

        private HexPathfinder _pathfinder;
        private Coroutine _activeTurnRoutine;

        private void Awake()
        {
            EnsureDependencies();
        }

        private void Start()
        {
            EnsureDependencies();
            if (_grid != null)
            {
                _pathfinder = new HexPathfinder(_grid);
            }

            if (_turnManager != null)
            {
                _turnManager.OnTurnStarted += HandleTurnStarted;
                _turnManager.OnCombatEnded += HandleCombatEnded;
            }
        }

        private void OnDestroy()
        {
            if (_turnManager != null)
            {
                _turnManager.OnTurnStarted -= HandleTurnStarted;
                _turnManager.OnCombatEnded -= HandleCombatEnded;
            }
            StopAITurn();
        }

        private void HandleCombatEnded(CombatOutcome outcome)
        {
            StopAITurn();
        }

        private void EnsureDependencies()
        {
            if (_turnManager == null) _turnManager = GetComponent<TurnManager>() ?? FindAnyObjectByType<TurnManager>();
            if (_grid == null) _grid = GetComponent<TacticalHexGrid>() ?? FindAnyObjectByType<TacticalHexGrid>();
            if (_arena == null) _arena = GetComponent<CombatDevArena>() ?? FindAnyObjectByType<CombatDevArena>();
        }

        public void StopAITurn()
        {
            if (_activeTurnRoutine != null)
            {
                StopCoroutine(_activeTurnRoutine);
                _activeTurnRoutine = null;
            }
        }

        private void HandleTurnStarted(TacticalUnit unit)
        {
            StopAITurn();

            if (_turnManager != null && _turnManager.IsCombatOver) return;
            if (!_isAIEnabled || unit == null || unit.Stats == null || !unit.Stats.IsAlive) return;

            bool shouldControl = (_mode == CombatAIMode.FullAuto) || (!unit.IsPlayerControlled);

            if (shouldControl)
            {
                _activeTurnRoutine = StartCoroutine(ExecuteAITurn(unit));
            }
        }

        private IEnumerator ExecuteAITurn(TacticalUnit unit)
        {
            yield return new WaitForSeconds(_actionDelay);

            if (_pathfinder == null && _grid != null)
            {
                _pathfinder = new HexPathfinder(_grid);
            }

            while (unit.Stats.IsAlive && unit.Stats.CurrentActionPoints >= 2)
            {
                var target = FindBestTarget(unit);
                if (target == null) break;

                int distance = unit.CurrentCoords.DistanceTo(target.CurrentCoords);

                // 1. Cible à portée de contact (Distance = 1 hexagone)
                if (distance == 1)
                {
                    _arena?.SelectTarget(target);
                    yield return new WaitForSeconds(0.15f);

                    BodyPart targetPart = BodyPart.Torse;
                    bool cancelPenalty = false;

                    // Si l'unité a au moins 4 PA, visée chirurgicale de la tête avec annulation de malus
                    if (unit.Stats.CurrentActionPoints >= 4)
                    {
                        targetPart = BodyPart.Tete;
                        cancelPenalty = true;
                    }

                    _arena?.ExecuteAttack(targetPart, cancelPenalty);
                    yield return new WaitForSeconds(_actionDelay + 0.35f);
                    continue;
                }

                // 2. Cible hors de portée : calcul d'approche vers une case adjacente libre
                var approachCoord = FindBestApproachHex(unit.CurrentCoords, target.CurrentCoords, unit.Stats.CurrentActionPoints);
                if (approachCoord.HasValue)
                {
                    var path = _pathfinder.FindPath(unit.CurrentCoords, approachCoord.Value, unit.Stats.CurrentActionPoints, out int apCost);
                    if (path != null && path.Count > 1)
                    {
                        yield return StartCoroutine(unit.MoveAlongPath(path, _grid, apCost));
                        yield return new WaitForSeconds(_actionDelay);
                        continue;
                    }
                }

                // Aucun mouvement ni attaque rentable possible
                break;
            }

            // Recours au souffle d'urgence si adjacent à un ennemi et 0 ou 1 PA restant
            if (unit.Stats.IsAlive && unit.Stats.CurrentActionPoints < 2 && unit.Stats.Essoufflement < unit.Stats.Attributes.Constitution)
            {
                var target = FindBestTarget(unit);
                if (target != null && unit.CurrentCoords.DistanceTo(target.CurrentCoords) == 1)
                {
                    if (unit.Stats.TakeEmergencyBreath(2))
                    {
                        yield return new WaitForSeconds(_actionDelay);
                        _arena?.SelectTarget(target);
                        _arena?.ExecuteAttack(BodyPart.Torse, false);
                        yield return new WaitForSeconds(_actionDelay);
                    }
                }
            }

            yield return new WaitForSeconds(0.2f);
            _activeTurnRoutine = null;
            _turnManager?.EndCurrentTurn();
        }

        private TacticalUnit FindBestTarget(TacticalUnit actor)
        {
            var allUnits = FindObjectsByType<TacticalUnit>();
            TacticalUnit bestTarget = null;
            int shortestDistance = int.MaxValue;

            foreach (var potential in allUnits)
            {
                if (potential == null || potential == actor || !potential.Stats.IsAlive)
                {
                    continue;
                }

                // Définition de l'hostilité selon l'allégeance
                bool isHostile = actor.IsPlayerControlled ? (!potential.IsPlayerControlled) : potential.IsPlayerControlled;

                // En mode FullAuto, si aucun joueur n'est en vie, confrontation générale
                if (_mode == CombatAIMode.FullAuto && !isHostile && potential.IsPlayerControlled != actor.IsPlayerControlled)
                {
                    isHostile = true;
                }

                if (!isHostile) continue;

                int dist = actor.CurrentCoords.DistanceTo(potential.CurrentCoords);
                if (dist < shortestDistance)
                {
                    shortestDistance = dist;
                    bestTarget = potential;
                }
            }

            return bestTarget;
        }

        private HexCoordinates? FindBestApproachHex(HexCoordinates start, HexCoordinates targetCoords, int availableAP)
        {
            HexCoordinates? bestHex = null;
            int lowestCost = int.MaxValue;

            for (int dir = 0; dir < 6; dir++)
            {
                var neighbor = targetCoords.GetNeighbor(dir);
                var node = _grid.GetNode(neighbor);

                if (node == null || !node.IsWalkable || (node.IsOccupied && !neighbor.Equals(start)))
                {
                    continue;
                }

                var path = _pathfinder.FindPath(start, neighbor, availableAP, out int apCost);
                if (path != null && path.Count > 0 && apCost < lowestCost)
                {
                    lowestCost = apCost;
                    bestHex = neighbor;
                }
            }

            return bestHex;
        }
    }
}
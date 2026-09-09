using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Core.Combat;
using Killtime.Core.Dice;
using Killtime.CameraSystem;
using Killtime.UI;
using Killtime.WebGL;

namespace Killtime.Tactics
{
    /// <summary>
    /// Contrôleur d'affrontement tactique reliant la grille, les unités, la caméra cinématique,
    /// le HUD et le moteur de règles C# Killtime.
    /// </summary>
    public class CombatEncounterController : MonoBehaviour
    {
        [Header("Systèmes")]
        [SerializeField] private TacticalHexGrid _grid;
        [SerializeField] private TurnManager _turnManager;
        [SerializeField] private TacticalCameraController _cameraController;
        [SerializeField] private CinematicDirector _cinematicDirector;
        [SerializeField] private CombatHUD _hud;

        [Header("Unités en scène")]
        [SerializeField] private TacticalUnit _playerUnit;
        [SerializeField] private TacticalUnit _enemyUnit;

        private HexPathfinder _pathfinder;
        private CombatCalculator _combatCalculator;
        private DiceRoller _diceRoller;

        private void Awake()
        {
            if (_grid == null) _grid = GetComponent<TacticalHexGrid>() ?? FindAnyObjectByType<TacticalHexGrid>();
            if (_turnManager == null) _turnManager = GetComponent<TurnManager>() ?? FindAnyObjectByType<TurnManager>();
            if (_cameraController == null) _cameraController = FindAnyObjectByType<TacticalCameraController>();
            if (_cinematicDirector == null) _cinematicDirector = FindAnyObjectByType<CinematicDirector>();
            if (_hud == null) _hud = FindAnyObjectByType<CombatHUD>();
        }

        private void Start()
        {
            _diceRoller = new DiceRoller();
            _combatCalculator = new CombatCalculator(_diceRoller);
            if (_grid != null) _pathfinder = new HexPathfinder(_grid);

            // Initialiser les positions sur la grille
            if (_playerUnit != null && _grid != null && _turnManager != null)
            {
                _playerUnit.InitializePosition(new HexCoordinates(0, 0), _grid);
                _turnManager.RegisterUnit(_playerUnit);
            }

            if (_enemyUnit != null && _grid != null && _turnManager != null)
            {
                _enemyUnit.InitializePosition(new HexCoordinates(3, -1), _grid);
                _turnManager.RegisterUnit(_enemyUnit);
            }

            // Liaison des événements UI
            if (_hud != null)
            {
                _hud.OnAttackRequested += HandleAttackRequested;
            }

            if (_turnManager != null)
            {
                _turnManager.OnTurnStarted += HandleTurnStarted;
            }
        }

        private void HandleTurnStarted(TacticalUnit unit)
        {
            if (_cameraController != null)
            {
                _cameraController.FocusOn(unit.transform);
            }

            _hud?.AddCombatLog($"--- Nouveau tour pour {unit.Stats.Name} ({unit.Stats.CurrentActionPoints} PA) ---");
        }

        private void Update()
        {
            HandleMouseInteraction();
        }

        private void HandleMouseInteraction()
        {
            if (_turnManager.ActiveUnit == null || !_turnManager.ActiveUnit.IsPlayerControlled || _turnManager.ActiveUnit.IsMoving)
            {
                return;
            }

            if (Input.GetMouseButtonDown(0))
            {
                Ray ray = UnityEngine.Camera.main.ScreenPointToRay(Input.mousePosition);
                if (Physics.Raycast(ray, out RaycastHit hit))
                {
                    if (_grid.TryGetNodeAtWorldPosition(hit.point, out var clickedNode))
                    {
                        var start = _turnManager.ActiveUnit.CurrentCoords;
                        var target = clickedNode.Coordinates;

                        if (!start.Equals(target) && clickedNode.IsWalkable && !clickedNode.IsOccupied)
                        {
                            var path = _pathfinder.FindPath(start, target, _turnManager.ActiveUnit.Stats.CurrentActionPoints, out int apCost);
                            if (path.Count > 0)
                            {
                                _hud?.AddCombatLog($"{_turnManager.ActiveUnit.Stats.Name} se déplace de {path.Count - 1} cases (Coût: {apCost} PA).");
                                StartCoroutine(_turnManager.ActiveUnit.MoveAlongPath(path, _grid, apCost));
                            }
                            else
                            {
                                _hud?.AddCombatLog("Déplacement impossible : PA insuffisants ou chemin bloqué !");
                            }
                        }
                    }
                }
            }
        }

        private void HandleAttackRequested(BodyPart part, bool cancelPenaltyWithAP)
        {
            var attacker = _turnManager.ActiveUnit;
            var target = (attacker == _playerUnit) ? _enemyUnit : _playerUnit;

            if (target == null || !target.Stats.IsAlive)
            {
                _hud?.AddCombatLog("Aucune cible valide à portée !");
                return;
            }

            // Lancer le plan cinématique dynamique
            _cinematicDirector.PlayCinematicKillshot(
                attacker.transform,
                target.transform,
                onStrikePoint: () =>
                {
                    // Résolution mathématique du coup au moment de l'impact
                    var result = _combatCalculator.ResolveTargetedAttack(
                        attacker: attacker.Stats,
                        defender: target.Stats,
                        targetedPart: part,
                        attackDie: DiceType.D6,
                        attackModifier: attacker.Stats.Attributes.Agilite,
                        defenseDie: DiceType.D4,
                        defenseModifier: target.Stats.Attributes.Agilite,
                        weaponBaseDamage: 4,
                        cancelPenaltyWithAP: cancelPenaltyWithAP,
                        defenderArmor: target.Stats.BaseArmorAbsorption
                    );

                    _hud?.AddCombatLog(result.CombatLog);

                    // Notification WebGL
                    WebBridgeManager.Instance?.SendCombatEvent(
                        result.IsHit ? "HIT" : "MISS",
                        result.CombatLog,
                        result.FinalDamageApplied
                    );
                },
                onComplete: () =>
                {
                    // Fin de la transition cinématique
                }
            );
        }
    }
}

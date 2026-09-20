using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Core.Combat;
using Killtime.Core.Dice;
using Killtime.Core.Character;
using Killtime.CameraSystem;
using Killtime.UI;
using Killtime.WebGL;
using Killtime.Audio;

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

            // Mode FullAuto : l'IA contrôle aussi les alliés, aucun déplacement manuel.
            var ai = GetComponent<AI.TacticalAIController>() ?? FindAnyObjectByType<AI.TacticalAIController>();
            if (ai != null && ai.Mode == AI.CombatAIMode.FullAuto)
            {
                return;
            }

            // Fenêtre flottante/HUD survolée : pas de déplacement 3D (clic réservé à l'UI).
            if (FloatingWindowChrome.IsPointerOverAnyWindow())
            {
                return;
            }

            if (Input.GetMouseButtonDown(0))
            {
                // Clic carte 3D : repli auto des fenêtres flottantes en coins (fantômes).
                FloatingWindowChrome.OnMapClicked();
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

            var attVisual = attacker.GetComponent<TacticalUnitVisual>();
            var defVisual = target.GetComponent<TacticalUnitVisual>();

            Vector3 combatDir = target.transform.position - attacker.transform.position;
            combatDir.y = 0f;
            if (combatDir != Vector3.zero)
            {
                attacker.transform.rotation = Quaternion.LookRotation(combatDir);
                if (target.Stats.CanDefendActively())
                {
                    target.transform.rotation = Quaternion.LookRotation(-combatDir);
                }
            }

            // Lancer le plan cinématique dynamique
            _cinematicDirector.PlayCinematicKillshot(
                attacker.transform,
                target.transform,
                onStrikePoint: () =>
                {
                    // Résolution mathématique du coup au moment de l'impact (duel aveugle Livre VI §24.1 :
                    // déclarations masquées PA/PE des deux camps, puis révélation simultanée).
                    // Au contact : meilleure compétence de mêlée de l'attaquant
                    // (jamais de Maniement d'Arme imposé à un mains-nues entraîné).
                    // Règle Livre VI §26.2 : si l'attaquant fait un tir de portée alors que
                    // N'IMPORTE QUEL ennemi est au contact de l'attaquant (pas forcément
                    // la cible), Canon Entravé (-2) s'applique. IsCanonEntrave balaie déjà
                    // tous les ennemis à distance 1, indépendamment de la cible visée.
                    SkillType encounterAttackSkill = attacker.Stats.GetBestMeleeAttackSkill();
                    _combatCalculator.SetContactDistanceState(attacker.IsCanonEntrave());
                    // Livre VI §25.3 : couvert total (non visible) => attaque impossible.
                    CoverType encounterCover = CoverType.None;
                    if (_grid != null && attacker.CurrentCoords.DistanceTo(target.CurrentCoords) > 1)
                        encounterCover = CoverSystem.EvaluateCover(attacker.CurrentCoords, target.CurrentCoords, _grid);
                    if (encounterCover == CoverType.Full)
                    {
                        _hud?.AddCombatLog($"🛡️ <b>Couvert total</b> : {target.Stats.Name} est non visible pour {attacker.Stats.Name} — attaque impossible ! Déplacez-vous pour retrouver une ligne de mire.");
                        return;
                    }
                    var result = _combatCalculator.ResolveTargetedAttack(
                        attacker: attacker.Stats,
                        defender: target.Stats,
                        targetedPart: part,
                        attackDie: attacker.Stats.GetSkillDie(encounterAttackSkill, true),
                        attackModifier: attacker.Stats.GetStatusModifier(encounterAttackSkill, isOffensive: true),
                        defenseDie: target.Stats.GetSkillDie(SkillType.Esquive),
                        defenseModifier: target.Stats.GetStatusModifier(SkillType.Esquive, isOffensive: false),
                        attackSkill: encounterAttackSkill,
                        weaponBaseDamage: 4,
                        cancelPenaltyWithAP: cancelPenaltyWithAP,
                        defenderArmor: target.Stats.BaseArmorAbsorption,
                        cover: encounterCover
                    );

                    _hud?.AddCombatLog(result.CombatLog);

                    if (KilltimeAudioManager.Instance != null)
                        KilltimeAudioManager.Instance.PlayCombatResult(
                            result.IsHit, result.IsBlocked, result.IsCritical,
                            result.ArmorAbsorbed > 0, result.ExceededEncaissement,
                            result.FatalResolution.ToString(), target.transform.position);

                    if (!target.Stats.IsAlive || target.Stats.ActiveStatus.HasFlag(StatusEffect.Inconscient))
                    {
                        defVisual?.TriggerFallingBackDeath();
                    }

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
                },
                onActionStart: () => attVisual?.TriggerRoundkick(),
                onDefenseStart: () =>
                {
                    if (target.Stats.CanDefendActively())
                    {
                        defVisual?.TriggerBodyBlock();
                    }
                }
            );
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Core.Character;
using Killtime.Core.Combat;
using Killtime.Core.Dice;
using Killtime.Core.Chrono;
using Killtime.CameraSystem;
using Killtime.WebGL;

namespace Killtime.Tactics
{
    public enum ArenaLayoutType
    {
        TheDuelRing,
        TacticalBarricades,
        KillzoneChokepoint
    }

    /// <summary>
    /// Contrôleur central de l'Arène de Développement Combat.
    /// Orchestre la grille, les mannequins d'entraînement (sparring dummies),
    /// les presets d'obstacles, la causalité chronomantique et les outils de test développeur.
    /// </summary>
    public class CombatDevArena : MonoBehaviour
    {
        [Header("Composants de Scène")]
        [SerializeField] private TacticalHexGrid _grid;
        [SerializeField] private HexGridVisualizer _gridVisualizer;
        [SerializeField] private TurnManager _turnManager;
        [SerializeField] private TacticalCameraController _cameraController;
        [SerializeField] private CinematicDirector _cinematicDirector;

        [Header("Configuration")]
        [SerializeField] private ArenaLayoutType _initialLayout = ArenaLayoutType.TacticalBarricades;
        [SerializeField] private bool _enableCinematicKillcam = true;

        public TacticalUnit PlayerUnit { get; private set; }
        public List<TacticalUnit> SparringDummies { get; private set; } = new();
        public TacticalUnit CurrentTarget { get; private set; }

        public bool InfiniteAP { get; set; } = false;
        public CombatCalculator Calculator => _combatCalculator;
        public TimelineBranch Timeline => _timelineBranch;
        public ArenaLayoutType CurrentLayout => _currentLayout;

        public event Action<string> OnCombatLogMessage;
        public event Action<TacticalUnit> OnTargetChanged;

        private HexPathfinder _pathfinder;
        private CombatCalculator _combatCalculator;
        private DiceRoller _diceRoller;
        private TimelineBranch _timelineBranch;
        private ArenaLayoutType _currentLayout;
        private AI.TacticalAIController _aiController;

        public AI.TacticalAIController AIController => _aiController;

        private void Awake()
        {
            _diceRoller = new DiceRoller();
            _combatCalculator = new CombatCalculator(_diceRoller);
            _timelineBranch = new TimelineBranch(TimelineId.Timeline0_Prime);

            EnsureDependencies();
            EnsureAIController();
        }

        private void EnsureAIController()
        {
            _aiController = GetComponent<AI.TacticalAIController>() ?? FindAnyObjectByType<AI.TacticalAIController>();
            if (_aiController == null)
            {
                _aiController = gameObject.AddComponent<AI.TacticalAIController>();
            }
        }

        private void Start()
        {
            _pathfinder = new HexPathfinder(_grid);

            // 1. Initialiser le layout d'obstacles
            ApplyArenaLayout(_initialLayout);

            // 2. Générer les unités
            SetupArenaUnits();

            // 3. Liaison avec le TurnManager
            _turnManager.OnTurnStarted += HandleTurnStarted;

            // 4. Sélectionner le premier mannequin par défaut
            if (SparringDummies.Count > 0)
            {
                SelectTarget(SparringDummies[0]);
            }

            // 5. Enregistrer le snapshot initial du Fleuve du Temps
            RecordChronoSnapshot("Début de l'affrontement (Temps t0)");

            Log("⚔️ <b>Arène de Combat Killtime Initialisée !</b> Prête pour les tests.");
        }

        private void EnsureDependencies()
        {
            if (_grid == null) _grid = GetComponent<TacticalHexGrid>() ?? FindAnyObjectByType<TacticalHexGrid>();
            if (_gridVisualizer == null) _gridVisualizer = GetComponent<HexGridVisualizer>() ?? FindAnyObjectByType<HexGridVisualizer>();
            if (_turnManager == null) _turnManager = GetComponent<TurnManager>() ?? FindAnyObjectByType<TurnManager>();
            if (_cameraController == null) _cameraController = FindAnyObjectByType<TacticalCameraController>();
            if (_cinematicDirector == null) _cinematicDirector = FindAnyObjectByType<CinematicDirector>();
        }

        private void Update()
        {
            HandleMouseInteraction();
            UpdateReachableHighlights();

            if (InfiniteAP && _turnManager.ActiveUnit != null)
            {
                _turnManager.ActiveUnit.Stats.CurrentActionPoints = _turnManager.ActiveUnit.Stats.MaxActionPoints;
            }
        }

        private void SetupArenaUnits()
        {
            // Détruire les anciennes unités si déjà existantes
            if (PlayerUnit != null) Destroy(PlayerUnit.gameObject);
            foreach (var d in SparringDummies) if (d != null) Destroy(d.gameObject);
            SparringDummies.Clear();

            // 1. Unité Joueur : Roger (Chrono-Opératif)
            PlayerUnit = CreateUnit("Roger (Opératif)", new HexCoordinates(0, 0), 
                new Attributes(4, 3, 4, 4, 3, 2), 
                baseArmor: 1, isPlayer: true);

            // 2. Mannequin 1 : Sac de Frappe (Tank lourd)
            var dummyTank = CreateUnit("Sac de Frappe (Tank)", new HexCoordinates(3, -1), 
                new Attributes(1, 1, 1, 6, 4, 1), 
                baseArmor: 2, isPlayer: false);
            SparringDummies.Add(dummyTank);

            // 3. Mannequin 2 : Duelliste Agile (Haute Esquive)
            var dummyAgile = CreateUnit("Duelliste Agile (Esquive)", new HexCoordinates(2, 2), 
                new Attributes(6, 3, 5, 3, 2, 2), 
                baseArmor: 0, isPlayer: false);
            SparringDummies.Add(dummyAgile);

            // 4. Mannequin 3 : Garde Blindé (Armure Lourde)
            var dummyArmored = CreateUnit("Garde Blindé (Armure)", new HexCoordinates(-3, 2), 
                new Attributes(2, 2, 2, 5, 4, 1), 
                baseArmor: 4, isPlayer: false);
            SparringDummies.Add(dummyArmored);

            // Enregistrement au gestionnaire de tours
            _turnManager.RegisterUnit(PlayerUnit);
            foreach (var d in SparringDummies)
            {
                _turnManager.RegisterUnit(d);
            }

            // Démarre le premier tour pour assigner l'unité active
            _turnManager.StartNewRound();
        }

        private TacticalUnit CreateUnit(string unitName, HexCoordinates coords, Attributes attributes, int baseArmor, bool isPlayer)
        {
            var go = new GameObject($"Unit_{unitName.Replace(" ", "_")}");
            var unit = go.AddComponent<TacticalUnit>();
            unit.ConfigureStats(unitName, attributes, baseArmor, isPlayer);
            unit.InitializePosition(coords, _grid);

            var visual = unit.GetComponent<TacticalUnitVisual>();
            if (visual != null)
            {
                if (isPlayer)
                {
                    visual.SetColor(new Color(0.15f, 0.45f, 0.85f), new Color(0.0f, 0.95f, 1.0f));
                }
                else
                {
                    visual.SetColor(new Color(0.85f, 0.25f, 0.2f), new Color(1.0f, 0.75f, 0.1f));
                }
            }

            return unit;
        }

        public void ApplyArenaLayout(ArenaLayoutType layout)
        {
            _currentLayout = layout;
            _grid.ClearAllCovers();

            switch (layout)
            {
                case ArenaLayoutType.TheDuelRing:
                    // Arène dégagée, demi-couvertures en périphérie
                    _grid.SetNodeCover(new HexCoordinates(4, 0), CoverType.Half, true);
                    _grid.SetNodeCover(new HexCoordinates(-4, 0), CoverType.Half, true);
                    _grid.SetNodeCover(new HexCoordinates(0, 4), CoverType.Half, true);
                    _grid.SetNodeCover(new HexCoordinates(0, -4), CoverType.Half, true);
                    break;

                case ArenaLayoutType.TacticalBarricades:
                    // Couvertures tactiques réparties pour tester les angles de tir
                    _grid.SetNodeCover(new HexCoordinates(1, 0), CoverType.Half, true);
                    _grid.SetNodeCover(new HexCoordinates(1, -2), CoverType.Full, false);
                    _grid.SetNodeCover(new HexCoordinates(-1, 2), CoverType.Half, true);
                    _grid.SetNodeCover(new HexCoordinates(-2, 0), CoverType.Full, false);
                    _grid.SetNodeCover(new HexCoordinates(2, 1), CoverType.Half, true);
                    _grid.SetNodeCover(new HexCoordinates(-1, -2), CoverType.Half, true);
                    break;

                case ArenaLayoutType.KillzoneChokepoint:
                    // Mur de piliers créant un goulot d'étranglement central
                    _grid.SetNodeCover(new HexCoordinates(0, 1), CoverType.Full, false);
                    _grid.SetNodeCover(new HexCoordinates(0, 2), CoverType.Full, false);
                    _grid.SetNodeCover(new HexCoordinates(0, -1), CoverType.Full, false);
                    _grid.SetNodeCover(new HexCoordinates(0, -2), CoverType.Full, false);
                    _grid.SetNodeCover(new HexCoordinates(1, -1), CoverType.Half, true);
                    _grid.SetNodeCover(new HexCoordinates(-1, 1), CoverType.Half, true);
                    break;
            }

            if (_gridVisualizer != null)
            {
                _gridVisualizer.RefreshObstacles();
            }

            Log($"📐 Layout d'arène configuré : <b>{layout}</b>");
        }

        private void HandleTurnStarted(TacticalUnit unit)
        {
            if (_cameraController != null)
            {
                _cameraController.FocusOn(unit.transform);
            }

            Log($"--- Tour de 10s : <b>{unit.Stats.Name}</b> (PA: {unit.Stats.CurrentActionPoints}/{unit.Stats.MaxActionPoints}) ---");
            RecordChronoSnapshot($"Début du tour de {unit.Stats.Name}");
        }

        private void HandleMouseInteraction()
        {
            if (UnityEngine.Camera.main == null || _gridVisualizer == null) return;

            Ray ray = UnityEngine.Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                if (_grid.TryGetNodeAtWorldPosition(hit.point, out var hoveredNode))
                {
                    _gridVisualizer.SetHoveredCoord(hoveredNode.Coordinates);

                    var activeUnit = _turnManager.ActiveUnit;
                    if (activeUnit != null && activeUnit.IsPlayerControlled && !activeUnit.IsMoving)
                    {
                        var start = activeUnit.CurrentCoords;
                        var target = hoveredNode.Coordinates;

                        // Vérifier si un clic est effectué
                        if (Input.GetMouseButtonDown(0))
                        {
                            // Clic sur une unité ennemie -> Sélection de cible
                            var clickedUnit = GetUnitAtCoords(target);
                            if (clickedUnit != null && clickedUnit != activeUnit)
                            {
                                SelectTarget(clickedUnit);
                                return;
                            }

                            // Déplacement
                            if (!start.Equals(target) && hoveredNode.IsWalkable && !hoveredNode.IsOccupied)
                            {
                                var path = _pathfinder.FindPath(start, target, activeUnit.Stats.CurrentActionPoints, out int apCost);
                                if (path.Count > 0)
                                {
                                    Log($"{activeUnit.Stats.Name} avance de {path.Count - 1} cases (Coût: {apCost} PA).");
                                    _gridVisualizer.ClearPathPreview();
                                    StartCoroutine(activeUnit.MoveAlongPath(path, _grid, apCost));
                                    RecordChronoSnapshot($"Déplacement vers ({target.Q}, {target.R})");
                                }
                            }
                        }
                        else
                        {
                            // Aperçu du chemin au survol
                            if (!start.Equals(target) && hoveredNode.IsWalkable && !hoveredNode.IsOccupied)
                            {
                                var path = _pathfinder.FindPath(start, target, activeUnit.Stats.CurrentActionPoints, out _);
                                _gridVisualizer.SetPathPreview(path);
                            }
                            else
                            {
                                _gridVisualizer.ClearPathPreview();
                            }
                        }
                    }
                }
            }
            else
            {
                _gridVisualizer.SetHoveredCoord(null);
                _gridVisualizer.ClearPathPreview();
            }
        }

        private void UpdateReachableHighlights()
        {
            var active = _turnManager.ActiveUnit;
            if (active != null && active.IsPlayerControlled && !active.IsMoving && _gridVisualizer != null)
            {
                var reachable = _pathfinder.GetReachableCoordinates(active.CurrentCoords, active.Stats.CurrentActionPoints);
                _gridVisualizer.SetReachableCoords(reachable);
            }
            else if (_gridVisualizer != null)
            {
                _gridVisualizer.SetReachableCoords(null);
            }
        }

        public TacticalUnit GetUnitAtCoords(HexCoordinates coords)
        {
            if (PlayerUnit != null && PlayerUnit.CurrentCoords.Equals(coords)) return PlayerUnit;
            foreach (var d in SparringDummies)
            {
                if (d != null && d.CurrentCoords.Equals(coords)) return d;
            }
            return null;
        }

        public void SelectTarget(TacticalUnit target)
        {
            CurrentTarget = target;
            if (_gridVisualizer != null && target != null)
            {
                _gridVisualizer.SetTargetCoord(target.CurrentCoords);
            }
            OnTargetChanged?.Invoke(target);
            Log($"🎯 Cible verrouillée : <b>{target.Stats.Name}</b> ({target.Stats.CurrentHealth}/{target.Stats.MaxHealth} PV)");
        }

        public void CycleTarget()
        {
            if (SparringDummies.Count == 0) return;
            int idx = SparringDummies.IndexOf(CurrentTarget);
            int nextIdx = (idx + 1) % SparringDummies.Count;
            SelectTarget(SparringDummies[nextIdx]);
        }

        /// <summary>
        /// Déclenche un tir ciblé selon les règles complètes du Livre VI (Chap. 26).
        /// </summary>
        public void ExecuteAttack(BodyPart targetedPart, bool cancelPenaltyWithAP, DiceType attackDie = DiceType.D6, DiceType defenseDie = DiceType.D4, int weaponBaseDamage = 5)
        {
            var attacker = _turnManager.ActiveUnit;
            var defender = CurrentTarget;

            if (attacker == null || defender == null)
            {
                Log("⚠️ Impossible d'attaquer : aucune cible active.");
                return;
            }

            if (!defender.Stats.IsAlive)
            {
                Log($"⚠️ {defender.Stats.Name} est déjà hors de combat !");
                return;
            }

            Action resolveAction = () =>
            {
                var result = _combatCalculator.ResolveTargetedAttack(
                    attacker: attacker.Stats,
                    defender: defender.Stats,
                    targetedPart: targetedPart,
                    attackDie: attackDie,
                    attackModifier: attacker.Stats.Attributes.Agilite,
                    defenseDie: defenseDie,
                    defenseModifier: defender.Stats.Attributes.Agilite,
                    weaponBaseDamage: weaponBaseDamage,
                    cancelPenaltyWithAP: cancelPenaltyWithAP,
                    defenderArmor: defender.Stats.BaseArmorAbsorption
                );

                Log(result.CombatLog);

                // Effets visuels sur l'unité cible
                var defVisual = defender.GetComponent<TacticalUnitVisual>();
                if (defVisual != null)
                {
                    if (result.IsHit)
                    {
                        defVisual.TriggerHitFlash();
                        defVisual.SpawnFloatingText($"-{result.FinalDamageApplied} PV", Color.red);

                        if (result.WasDeflected)
                        {
                            defVisual.SpawnFloatingText($"DÉVIATION ➔ {result.ActualHitPart}", Color.yellow);
                        }
                        if (result.ExceededEncaissement)
                        {
                            defVisual.SpawnFloatingText($"ENC. DÉPASSÉ! [{result.InflictedStatus}]", Color.magenta);
                        }
                    }
                    else if (result.IsBlocked)
                    {
                        defVisual.SpawnFloatingText("PARADE / ESQUIVE!", Color.cyan);
                    }
                }

                // Pont WebGL
                WebBridgeManager.Instance?.SendCombatEvent(
                    result.IsHit ? "HIT" : "MISS",
                    result.CombatLog,
                    result.FinalDamageApplied
                );

                RecordChronoSnapshot($"Attaque sur {defender.Stats.Name} ({targetedPart})");
                _turnManager?.CheckCombatOver();
            };

            if (_enableCinematicKillcam && _cinematicDirector != null)
            {
                _cinematicDirector.PlayCinematicKillshot(
                    attacker.transform,
                    defender.transform,
                    onStrikePoint: resolveAction,
                    onComplete: () => { }
                );
            }
            else
            {
                resolveAction();
            }
        }

        public void RecordChronoSnapshot(string description)
        {
            var snap = new TacticalTimeSnapshot(_turnManager.CurrentRound, 0.0f, description);
            if (PlayerUnit != null && PlayerUnit.Stats != null)
            {
                snap.RecordUnit(PlayerUnit.Stats.Name, PlayerUnit.Stats, PlayerUnit.CurrentCoords.Q, PlayerUnit.CurrentCoords.R);
            }
            foreach (var d in SparringDummies)
            {
                if (d != null && d.Stats != null)
                {
                    snap.RecordUnit(d.Stats.Name, d.Stats, d.CurrentCoords.Q, d.CurrentCoords.R);
                }
            }

            _timelineBranch.PushSnapshot(snap);
        }

        public void RewindLastSnapshot()
        {
            var snap = _timelineBranch.RewindLastAction();
            if (snap == null)
            {
                Log("⏳ Fleuve du Temps : Aucun snapshot antérieur disponible.");
                return;
            }

            RestoreSnapshot(snap);
            Log($"⏪ <b>Rembobinage Temporel (Livre V)</b> : Retour au snapshot '{snap.ActionDescription}' (Round {snap.RoundNumber})");
        }

        private void RestoreSnapshot(TacticalTimeSnapshot snap)
        {
            if (snap.UnitStates.TryGetValue(PlayerUnit.Stats.Name, out var pSnap))
            {
                RestoreUnitState(PlayerUnit, pSnap);
            }

            foreach (var d in SparringDummies)
            {
                if (snap.UnitStates.TryGetValue(d.Stats.Name, out var dSnap))
                {
                    RestoreUnitState(d, dSnap);
                }
            }
        }

        private void RestoreUnitState(TacticalUnit unit, UnitTimeSnapshot snap)
        {
            unit.Stats.CurrentHealth = snap.Health;
            unit.Stats.CurrentActionPoints = snap.ActionPoints;
            unit.Stats.Essoufflement = snap.Essoufflement;
            unit.Stats.ActiveStatus = snap.Status;

            var targetCoords = new HexCoordinates(snap.GridCoordQ, snap.GridCoordR);
            unit.TeleportTo(targetCoords, _grid);
        }

        public void RefillAPAll()
        {
            if (PlayerUnit != null) PlayerUnit.Stats.CurrentActionPoints = PlayerUnit.Stats.MaxActionPoints;
            foreach (var d in SparringDummies) if (d != null) d.Stats.CurrentActionPoints = d.Stats.MaxActionPoints;
            Log("⚡ Points d'Action réinitialisés au maximum pour toutes les unités.");
        }

        public void HealAndCureAll()
        {
            if (PlayerUnit != null)
            {
                PlayerUnit.Stats.CurrentHealth = PlayerUnit.Stats.MaxHealth;
                PlayerUnit.Stats.ActiveStatus = StatusEffect.None;
                PlayerUnit.Stats.Essoufflement = 0;
            }
            foreach (var d in SparringDummies)
            {
                if (d != null)
                {
                    d.Stats.CurrentHealth = d.Stats.MaxHealth;
                    d.Stats.ActiveStatus = StatusEffect.None;
                    d.Stats.Essoufflement = 0;
                }
            }
            Log("❤️ <b>Soin Intégral :</b> Tous les PV restaurés et statuts négatifs dissipés.");
        }

        public void KillCurrentTarget()
        {
            if (CurrentTarget != null)
            {
                CurrentTarget.Stats.CurrentHealth = 0;
                CurrentTarget.Stats.ActiveStatus |= StatusEffect.Inconscient;
                var vis = CurrentTarget.GetComponent<TacticalUnitVisual>();
                vis?.SpawnFloatingText("K.O. TOTAL!", Color.red);
                Log($"💀 {CurrentTarget.Stats.Name} a été terrassé par commande développeur.");
            }
        }

        public void ResetArena()
        {
            _aiController?.StopAITurn();
            _turnManager?.ResetCombatState();
            SetupArenaUnits();
            if (SparringDummies.Count > 0) SelectTarget(SparringDummies[0]);
            ApplyArenaLayout(_currentLayout);
            RecordChronoSnapshot("Réinitialisation de l'Arène");
            Log("🔄 Arène entièrement réinitialisée.");
        }

        private void Log(string message)
        {
            OnCombatLogMessage?.Invoke($"[{DateTime.Now:HH:mm:ss}] {message}");
        }
    }
}

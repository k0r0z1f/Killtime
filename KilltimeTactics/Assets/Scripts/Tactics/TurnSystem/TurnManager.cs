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
    /// <summary>
    /// Façade : état sérialisé, propriétés publiques, events et lifecycle Unity.
    /// Logique répartie en partiels : .Core (registre/modes), .Initiative,
    /// .Rounds, .Objectives (RD-050), .Delayed (RD-048), .Multi (sync VTT).
    /// Types : voir TurnManager.Types.cs.
    /// </summary>
    public partial class TurnManager : MonoBehaviour
    {
        [Header("Références")]
        [SerializeField] private TacticalHexGrid _grid;
        [SerializeField] private List<TacticalUnit> _allUnits = new();

        [Header("Objectifs Tactiques (RD-050)")]
        [SerializeField] private List<CombatObjective> _objectives = new();
        [SerializeField] private bool _allowAnnihilationFallback = true;

        public TurnSystemMode SystemMode { get; private set; } = TurnSystemMode.Exploration;
        public bool IsInExploration => SystemMode == TurnSystemMode.Exploration;

        public int CurrentRound { get; private set; } = 1;
        public TacticalUnit ActiveUnit { get; private set; }
        public bool IsCombatOver { get; private set; } = false;
        public CombatOutcome CurrentOutcome { get; private set; } = CombatOutcome.InProgress;
        public string CombatEndReason { get; private set; } = "";
        public bool AllowAnnihilationFallback { get => _allowAnnihilationFallback; set => _allowAnnihilationFallback = value; }
        public bool HasCustomObjectives => _objectives != null && _objectives.Count > 0;
        public IReadOnlyList<CombatObjective> Objectives => _objectives.AsReadOnly();

        /// <summary>
        /// Quand vrai, RegisterUnit se contente d'ajouter sans démarrer de round.
        /// Utilisé par CombatDevArena pendant les enregistrements en masse (Reset/Setup/Load)
        /// pour éviter un double StartNewRound qui lance l'IA en prématuré puis la coupe,
        /// laissant _isAttackInProgress / cinématique en état bloqué (surtout en FullAuto).
        /// </summary>
        public bool SuspendAutoStart { get; set; } = false;

        private int _activeUnitIndex = 0;

        public event Action<TacticalUnit> OnTurnStarted;
        public event Action<int> OnRoundStarted;
        public event Action<CombatOutcome> OnCombatEnded;
        public event Action<TacticalUnit, List<StatusEffect>> OnUnitStatusExpired;
        public event Action<TacticalUnit, DotTickResult> OnUnitDotTick;
        public event Action<IReadOnlyList<InitiativeRollResult>> OnInitiativeRolled;
        public event Action<CombatObjective> OnObjectiveCompleted;
        public event Action<CombatObjective> OnObjectiveFailed;
        public event Action<CombatObjective> OnObjectiveUpdated;

        /// <summary>
        /// Ordre de passage actuel (après jet d'initiative).
        /// </summary>
        public IReadOnlyList<TacticalUnit> TurnOrder => _allUnits.AsReadOnly();

        private void OnEnable()
        {
            TacticalInteractable.OnAnyInteractionTriggered += HandleAnyInteractableTriggered;
            TacticalUnit.OnAnyUnitMoveCompleted += HandleAnyUnitMoveCompleted;
        }

        private void OnDisable()
        {
            TacticalInteractable.OnAnyInteractionTriggered -= HandleAnyInteractableTriggered;
            TacticalUnit.OnAnyUnitMoveCompleted -= HandleAnyUnitMoveCompleted;
        }

        private void Start()
        {
            if (IsMultiplayerPlayerClient()) return;
            if (_allUnits.Count > 0 && SystemMode == TurnSystemMode.CombatTurnBased)
            {
                StartNewRound();
            }
        }
    }
}

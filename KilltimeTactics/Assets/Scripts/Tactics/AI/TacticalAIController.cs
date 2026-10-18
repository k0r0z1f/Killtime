using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Core.Combat;
using Killtime.Core.Dice;
using Killtime.Core.Character;
using Killtime.Core.Arcanotech;
using Killtime.Core.Inventory;
using Killtime.Core.Rules;
using Killtime.CameraSystem;

namespace Killtime.Tactics.AI
{
    public enum CombatAIMode
    {
        Normal,   // IA uniquement sur les ennemis/PNJ
        FullAuto  // IA sur l'intégralité des unités
    }

    public enum AIPersonality
    {
        Balanced,   // Équilibre attaque, réserve minimale (1 PA), couverture et survie
        Aggressive, // Dépense 100% PA/PE, all-in létal, 0 réserve
        Tactician,  // Flanking prioritaire, ciblage chirurgical, intimidation, sorts et hit-and-run
        Survivor    // Priorité absolue au couvert, repli, soins d'urgence et attrition
    }

    public enum TacticalPosture
    {
        AllInLethal,      // Opportunité d'élimination : injection maximale PA/PE
        CalculatedStrike, // Attaque mesurée franchissant l'encaissement adverse
        FlankAndSuppress, // Manœuvre de contournement de couvert avant tir
        SupportAndHeal,   // Assistance d'urgence à un allié au contact
        HitAndRun,        // Frappe puis décrochage vers un couvert
        TacticalRetreat,  // Danger mortel : repli vers couvert maximal et rupture de vue
        AlertedPatrol     // RD-053 : Patrouille alertée (suspicion bruit/lampe, convergence vers la source)
    }

    /// <summary>
    /// Contrôleur d'IA Tactique Militaire Opérationnelle (Killtime Engine - Livres I à VIII).
    /// Intègre la théorie des jeux du duel aveugle, l'évaluation de couverture 3D (Cyrus-Beck)
    /// et la modulation prédictive des points d'action et d'énergie.
    /// </summary>
    public partial class TacticalAIController : MonoBehaviour
    {
        [Header("Systèmes")]
        [SerializeField] private TurnManager _turnManager;
        [SerializeField] private TacticalHexGrid _grid;
        [SerializeField] private CombatDevArena _arena;
        [SerializeField] private CinematicDirector _cinematicDirector;

        [Header("Paramètres de Doctrine")]
        [SerializeField] private CombatAIMode _mode = CombatAIMode.Normal;
        [SerializeField] private AIPersonality _defaultPersonality = AIPersonality.Balanced;
        [SerializeField] private bool _isAIEnabled = true;
        [SerializeField] [Range(0.05f, 6.0f)] private float _actionDelay = 0.8f;

        [Header("Paramètres de Survie & Économie")]
        [SerializeField] [Range(0.15f, 0.5f)] private float _retreatHealthRatio = 0.28f;
        [SerializeField] [Range(0, 2)] private int _baseDefensiveAPReserve = 1;

        public CombatAIMode Mode
        {
            get => _mode;
            set
            {
                if (_mode == value) return;
                _mode = value;
                try
                {
                    if (Killtime.UI.DevUIPreferences.Current != null)
                    {
                        Killtime.UI.DevUIPreferences.Current.AiMode = (int)_mode;
                        Killtime.UI.DevUIPreferences.MarkDirty();
                    }
                }
                catch { }

                NotifySettingsChangedIfGM();
                TriggerAITurnIfApplicable();
            }
        }

        public bool IsAIEnabled
        {
            get => _isAIEnabled;
            set
            {
                if (_isAIEnabled == value) return;
                _isAIEnabled = value;
                try
                {
                    if (Killtime.UI.DevUIPreferences.Current != null)
                    {
                        Killtime.UI.DevUIPreferences.Current.AiEnabled = value;
                        Killtime.UI.DevUIPreferences.MarkDirty();
                    }
                }
                catch { }

                NotifySettingsChangedIfGM();
                if (_isAIEnabled) TriggerAITurnIfApplicable();
                else StopAITurn();
            }
        }

        public float ActionDelay
        {
            get => _actionDelay;
            set
            {
                float clamped = Mathf.Clamp(value, 0.05f, 10.0f);
                if (Mathf.Abs(_actionDelay - clamped) < 0.01f) return;
                _actionDelay = clamped;
                try
                {
                    if (Killtime.UI.DevUIPreferences.Current != null)
                    {
                        Killtime.UI.DevUIPreferences.Current.AiActionDelay = _actionDelay;
                        Killtime.UI.DevUIPreferences.MarkDirty();
                    }
                }
                catch { }
                NotifySettingsChangedIfGM();
            }
        }

        public AIPersonality DefaultPersonality
        {
            get => _defaultPersonality;
            set
            {
                if (_defaultPersonality == value) return;
                _defaultPersonality = value;
                try
                {
                    if (Killtime.UI.DevUIPreferences.Current != null)
                    {
                        Killtime.UI.DevUIPreferences.Current.AiPersonality = (int)_defaultPersonality;
                        Killtime.UI.DevUIPreferences.MarkDirty();
                    }
                }
                catch { }
                NotifySettingsChangedIfGM();
            }
        }

        public float RetreatHealthRatio
        {
            get => _retreatHealthRatio;
            set
            {
                float clamped = Mathf.Clamp(value, 0.05f, 0.6f);
                if (Mathf.Abs(_retreatHealthRatio - clamped) < 0.01f) return;
                _retreatHealthRatio = clamped;
                try
                {
                    if (Killtime.UI.DevUIPreferences.Current != null)
                    {
                        Killtime.UI.DevUIPreferences.Current.AiRetreatRatio = _retreatHealthRatio;
                        Killtime.UI.DevUIPreferences.MarkDirty();
                    }
                }
                catch { }
                NotifySettingsChangedIfGM();
            }
        }

        public int DefensiveAPReserve
        {
            get => _baseDefensiveAPReserve;
            set
            {
                int clamped = Mathf.Clamp(value, 0, 2);
                if (_baseDefensiveAPReserve == clamped) return;
                _baseDefensiveAPReserve = clamped;
                try
                {
                    if (Killtime.UI.DevUIPreferences.Current != null)
                    {
                        Killtime.UI.DevUIPreferences.Current.AiDefensiveReserve = _baseDefensiveAPReserve;
                        Killtime.UI.DevUIPreferences.MarkDirty();
                    }
                }
                catch { }
                NotifySettingsChangedIfGM();
            }
        }

        public static bool IsInMultiplayerNonGM()
        {
            var room = Killtime.Multi.VTTRoomManager.Instance;
            return room != null && room.InRoom && !room.IsGM;
        }

        public void ApplyGMSettings(Killtime.Multi.VTTRoomSettingsPayload settings)
        {
            if (settings == null) return;
            _isAIEnabled = settings.isAIEnabled;
            _mode = (CombatAIMode)Mathf.Clamp(settings.aiMode, 0, Enum.GetValues(typeof(CombatAIMode)).Length - 1);
            _defaultPersonality = (AIPersonality)Mathf.Clamp(settings.aiPersonality, 0, Enum.GetValues(typeof(AIPersonality)).Length - 1);
            _actionDelay = Mathf.Clamp(settings.aiActionDelay, 0.05f, 10f);
            _retreatHealthRatio = Mathf.Clamp(settings.aiRetreatRatio, 0.05f, 0.6f);
            _baseDefensiveAPReserve = Mathf.Clamp(settings.aiDefensiveReserve, 0, 2);

            try
            {
                if (Killtime.UI.DevUIPreferences.Current != null)
                {
                    var p = Killtime.UI.DevUIPreferences.Current;
                    p.AiMode = (int)_mode;
                    p.AiPersonality = (int)_defaultPersonality;
                    p.AiEnabled = _isAIEnabled;
                    p.AiActionDelay = _actionDelay;
                    p.AiRetreatRatio = _retreatHealthRatio;
                    p.AiDefensiveReserve = _baseDefensiveAPReserve;
                    Killtime.UI.DevUIPreferences.MarkDirty();
                }
            }
            catch { }

            if (IsInMultiplayerNonGM())
            {
                StopAITurn();
            }
            else if (_isAIEnabled)
            {
                TriggerAITurnIfApplicable();
            }
        }

        private float _lastBroadcastTime;
        private void NotifySettingsChangedIfGM()
        {
            var room = Killtime.Multi.VTTRoomManager.Instance;
            if (room != null && room.InRoom && room.IsGM)
            {
                if (Time.unscaledTime - _lastBroadcastTime < 0.15f) return;
                _lastBroadcastTime = Time.unscaledTime;
                Killtime.Multi.VTTTableSync.Instance?.BroadcastRoomSettings();
            }
        }

        private HexPathfinder _pathfinder;
        private Coroutine _activeTurnRoutine;
        private readonly List<TacticalUnit> _cachedUnits = new();

        private static TacticalUnit _squadMarkedEnemyOfPlayers;
        private static TacticalUnit _squadMarkedEnemyOfAI;
        private static HexCoordinates? _squadLastHeardNoiseCoords;
        private static int _squadNoiseAlertRound = -1;
        private static bool _squadNoiseFromPlayer = true;

        // RD-115 : perception injectable (défaut = brouillard live). Les tests
        // substituent un fake IAIPerception sans singleton ni scène.
        private IAIPerception _perception;
        public IAIPerception Perception
        {
            get => _perception ?? FogOfWarPerception.Default;
            set => _perception = value;
        }

        public static void AlertSquadToNoise(HexCoordinates coords, int currentRound, bool isPlayerShooter)
        {
            _squadLastHeardNoiseCoords = coords;
            _squadNoiseAlertRound = currentRound;
            _squadNoiseFromPlayer = isPlayerShooter;
        }

        private void Awake()
        {
            EnsureDependencies();
            try { LoadDevUIPrefs(); } catch { }
        }

        private void Start()
        {
            EnsureDependencies();
            try { LoadDevUIPrefs(); } catch { }
            if (_grid != null) _pathfinder = new HexPathfinder(_grid);

            if (_turnManager != null)
            {
                _turnManager.OnTurnStarted -= HandleTurnStarted;
                _turnManager.OnTurnStarted += HandleTurnStarted;

                _turnManager.OnCombatEnded -= HandleCombatEnded;
                _turnManager.OnCombatEnded += HandleCombatEnded;
            }

            var room = Killtime.Multi.VTTRoomManager.Instance;
            if (room != null)
            {
                room.OnJoined -= HandleRoomStateChanged;
                room.OnJoined += HandleRoomStateChanged;
                room.OnRoleChanged -= HandleRoomRoleChanged;
                room.OnRoleChanged += HandleRoomRoleChanged;
                room.OnLeft -= HandleRoomLeft;
                room.OnLeft += HandleRoomLeft;
            }

            TriggerAITurnIfApplicable();
        }

        public void TriggerAITurnIfApplicable()
        {
            if (IsInMultiplayerNonGM())
            {
                StopAITurn();
                return;
            }
            if (_turnManager != null && _turnManager.IsInExploration) return;
            LoadDevUIPrefs();
            if (!_isAIEnabled || _turnManager == null || _turnManager.IsCombatOver) return;
            var active = _turnManager.ActiveUnit;
            if (active == null || active.Stats == null || !active.Stats.IsAlive) return;

            bool shouldControl = (_mode == CombatAIMode.FullAuto) || (!active.IsPlayerControlled);
            if (shouldControl && _activeTurnRoutine == null)
            {
                _activeTurnRoutine = StartCoroutine(ExecuteAITurn(active));
            }
        }

        private void OnDestroy()
        {
            if (_turnManager != null)
            {
                _turnManager.OnTurnStarted -= HandleTurnStarted;
                _turnManager.OnCombatEnded -= HandleCombatEnded;
            }
            var room = Killtime.Multi.VTTRoomManager.Instance;
            if (room != null)
            {
                room.OnJoined -= HandleRoomStateChanged;
                room.OnRoleChanged -= HandleRoomRoleChanged;
                room.OnLeft -= HandleRoomLeft;
            }
            StopAITurn();
        }

        private void HandleRoomStateChanged()
        {
            if (IsInMultiplayerNonGM()) StopAITurn();
            else if (_isAIEnabled) TriggerAITurnIfApplicable();
        }

        private void HandleRoomRoleChanged(string targetId, string role)
        {
            if (IsInMultiplayerNonGM()) StopAITurn();
            else if (_isAIEnabled) TriggerAITurnIfApplicable();
        }

        private void HandleRoomLeft(string reason)
        {
            if (_isAIEnabled) TriggerAITurnIfApplicable();
        }

        private void HandleCombatEnded(CombatOutcome outcome)
        {
            StopAITurn();
            _squadMarkedEnemyOfPlayers = null;
            _squadMarkedEnemyOfAI = null;
            _squadLastHeardNoiseCoords = null;
            _squadNoiseAlertRound = -1;
            LivreXIPNJState.ClearCombatState();
        }

        private void EnsureDependencies()
        {
            if (_turnManager == null) _turnManager = GetComponent<TurnManager>() ?? FindAnyObjectByType<TurnManager>();
            if (_grid == null) _grid = GetComponent<TacticalHexGrid>() ?? FindAnyObjectByType<TacticalHexGrid>();
            if (_arena == null) _arena = GetComponent<CombatDevArena>() ?? FindAnyObjectByType<CombatDevArena>();
            if (_cinematicDirector == null) _cinematicDirector = GetComponent<CinematicDirector>() ?? FindAnyObjectByType<CinematicDirector>();
        }

        public void LoadDevUIPrefs()
        {
            var p = Killtime.UI.DevUIPreferences.Current;
            if (p == null) return;
            int modeCount = Enum.GetValues(typeof(CombatAIMode)).Length;
            int persCount = Enum.GetValues(typeof(AIPersonality)).Length;
            _mode = (CombatAIMode)Mathf.Clamp(p.AiMode, 0, Mathf.Max(0, modeCount - 1));
            _defaultPersonality = (AIPersonality)Mathf.Clamp(p.AiPersonality, 0, Mathf.Max(0, persCount - 1));
            _isAIEnabled = p.AiEnabled;
            _actionDelay = Mathf.Clamp(p.AiActionDelay, 0.05f, 10f);
            _retreatHealthRatio = Mathf.Clamp(p.AiRetreatRatio, 0.05f, 0.6f);
            _baseDefensiveAPReserve = Mathf.Clamp(p.AiDefensiveReserve, 0, 2);
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

            if (IsInMultiplayerNonGM()) return;
            if (_turnManager != null && (_turnManager.IsCombatOver || _turnManager.IsInExploration)) return;
            LoadDevUIPrefs();
            if (!_isAIEnabled || unit == null || unit.Stats == null) return;

            if (!unit.Stats.IsAlive || unit.Stats.IsSurrendered)
            {
                _turnManager?.EndCurrentTurn();
                return;
            }

            if (_defaultPersonality == AIPersonality.Survivor && unit.Stats != null)
            {
                unit.Stats.IsSurvivor = true;
            }

            bool shouldControl = (_mode == CombatAIMode.FullAuto) || (!unit.IsPlayerControlled);
            if (shouldControl)
            {
                _activeTurnRoutine = StartCoroutine(ExecuteAITurn(unit));
            }
        }

        // =========================================================================
        // BOUCLE DÉCISIONNELLE STRATÉGIQUE (TACTICAL AI BRAIN)
        // RD-115 : la chaîne if/continue est devenue des plans d'intentions
        // (AITurnPlanner) ; ce contrôleur ne fait que marcher les plans et
        // exécuter les coroutines (voir TacticalAIController.TurnPlan.cs).
        // Ordre verbatim : pré-cible (1-4), sans cible (5), engagement
        // (6-11), frappe sur snapshot frais post-contact (12-25).
        // =========================================================================
        private IEnumerator ExecuteAITurn(TacticalUnit unit)
        {
            yield return new WaitForSeconds(_actionDelay);

            if (_pathfinder == null && _grid != null) _pathfinder = new HexPathfinder(_grid);
            UpdateUnitCache();

            // RD-031 : posture d'opportunité pour les tours adverses (réaction au
            // contact rompu : grappler → Blocage, rapide → Poursuite, esquiveur →
            // Balayage, défaut → Frappe gratuite).
            try { OpportunityState.SetStance(unit.Stats, ChooseOpportunityStance(unit)); }
            catch { /* posture optionnelle */ }

            var visual = unit.GetComponent<TacticalUnitVisual>();
            int loopGuard = 0;
            const int maxSteps = 10;
            bool tookBreathThisTurn = false;

            // RD-088 : Maintien et résolution continue de la morsure hydraulique au début de tour
            CheckAndApplyHydraulicBite(unit, visual);

            var stepState = new AIPlanStepState();

            while (unit.Stats.IsAlive && loopGuard++ < maxSteps && unit.Stats.CurrentActionPoints > 0)
            {
                while (Killtime.UI.CombatHUD.IsPaused)
                {
                    yield return null;
                }

                // Phases 1-4 (avant EvaluateBestTarget, verbatim).
                yield return StartCoroutine(WalkPlan(
                    AITurnPlanner.BuildPreTargetPlan(
                        unit.Stats.IsSurrendered,
                        unit.Stats.ActiveStatus.HasFlag(StatusEffect.Agonisant)),
                    unit, visual, null, stepState));
                if (stepState.Outcome == AIPlanStepOutcome.EndTurnImmediate)
                    yield break;
                if (stepState.Outcome == AIPlanStepOutcome.EndTurn)
                    break;
                if (stepState.Outcome == AIPlanStepOutcome.ContinueTurn)
                    continue;

                // Phase 5 : évaluation de cible puis plan sans cible le cas échéant.
                var target = EvaluateBestTarget(unit);
                if (target == null)
                {
                    yield return StartCoroutine(WalkPlan(
                        AITurnPlanner.BuildNoTargetPlan(), unit, visual, null, stepState));
                    if (stepState.Outcome == AIPlanStepOutcome.ContinueTurn)
                        continue;
                    break;
                }

                // Phases 6-11 : plan d'engagement sur snapshot pur.
                var frame = BuildPlanFrame(unit, target, tookBreathThisTurn);
                yield return StartCoroutine(WalkPlan(
                    AITurnPlanner.BuildEngagementPlan(BuildPlanSnapshot(unit, frame)),
                    unit, visual, frame, stepState));
                tookBreathThisTurn = frame.TookBreathThisTurn;
                if (stepState.Outcome == AIPlanStepOutcome.ContinueTurn)
                    continue;
                if (stepState.Outcome == AIPlanStepOutcome.EndTurn)
                    break;

                // Phases 12-25 : snapshot FRAIS post-contact (l'original
                // recalcule dist/LOS/portée après TEEP/dégagement avant les
                // seuils charge/bousculade/lancer/flanking/attaque).
                RefreshFrameForStrike(unit, frame);
                yield return StartCoroutine(WalkPlan(
                    AITurnPlanner.BuildStrikePlan(BuildPlanSnapshot(unit, frame)),
                    unit, visual, frame, stepState));
                tookBreathThisTurn = frame.TookBreathThisTurn;
                if (stepState.Outcome == AIPlanStepOutcome.ContinueTurn)
                    continue;
                break;
            }

            yield return new WaitForSeconds(Mathf.Min(0.4f, _actionDelay * 0.25f));
            _activeTurnRoutine = null;
            _turnManager?.EndCurrentTurn();
        }

        // =========================================================================
        // ANALYSE DE POSTURE & ÉCONOMIE DES MISES
        // =========================================================================
        private TacticalPosture EvaluateTacticalPosture(TacticalUnit actor, TacticalUnit target, int dist)
        {
            float hpRatio = (float)actor.Stats.CurrentHealth / Mathf.Max(1, actor.Stats.MaxHealth);
            bool canKill = CanKillTargetThisTurn(actor, target);

            // Détection de mission de soutien médical prioritaire (présence d'un
            // allié à mourant/blessé au contact ; le seuil vit dans AIPersonalityProfile).
            bool hasWoundedAllyAdjacent = false;
            if (_defaultPersonality != AIPersonality.Aggressive && actor.Stats.CurrentActionPoints >= AIPersonalityProfile.SupportMinAP)
            {
                for (int i = 0; i < _cachedUnits.Count; i++)
                {
                    var ally = _cachedUnits[i];
                    if (ally == null || ally == actor || ally.Stats == null) continue;
                    if (ally.IsPlayerControlled != actor.IsPlayerControlled) continue;
                    if (actor.CurrentCoords.DistanceTo(ally.CurrentCoords) <= 1)
                    {
                        if (!ally.Stats.IsAlive && ally.Stats.LastFatalBlowResolution != FatalBlowResolution.InstantDeath)
                        { hasWoundedAllyAdjacent = true; break; }
                        if (ally.Stats.IsAlive && (float)ally.Stats.CurrentHealth / Mathf.Max(1, ally.Stats.MaxHealth) <= AIPersonalityProfile.WoundedAllyHpRatio)
                        { hasWoundedAllyAdjacent = true; break; }
                    }
                }
            }

            CoverType targetCover = GetCoverLevel(actor.CurrentCoords, target.CurrentCoords, target.FootprintType, actor.FootprintType, actor);
            return TacticalPostureEvaluator.Evaluate(
                hpRatio, _retreatHealthRatio, canKill, hasWoundedAllyAdjacent,
                actor.Stats.CurrentActionPoints, _defaultPersonality, dist, targetCover);
        }

        private int GetRequiredDefensiveReserve(TacticalUnit actor, TacticalPosture posture)
        {
            // RD-IA03 : respecte le réglage 0-2 (avant : écrasé à 1 max).
            return AIPersonalityProfile.GetDefensiveReserve(_defaultPersonality, posture, _baseDefensiveAPReserve);
        }

        private bool CanKillTargetThisTurn(TacticalUnit actor, TacticalUnit target)
        {
            int maxPotentialAP = actor.Stats.CurrentActionPoints;
            if (CanTakeBreathSafely(actor)) maxPotentialAP += 2;

            // RD-IA04 : distance d'empreinte (les Titans faussaient l'estimation).
            int dist = TitanFootprint.MinDistanceBetweenUnits(actor.CurrentCoords, actor.FootprintType, target.CurrentCoords, target.FootprintType);
            bool isRanged = HasRangedWeapon(actor, out int maxRange, out _, out _);

            int apToReach = 0;
            if (isRanged)
            {
                apToReach = (dist <= maxRange && HasLineOfSight(actor.CurrentCoords, target.CurrentCoords, target.FootprintType, actor.FootprintType, actor)) ? 0 : Mathf.Max(0, dist - maxRange);
            }
            else
            {
                apToReach = (dist > 1) ? (dist - 1) : 0;
            }

            int apForAttacks = maxPotentialAP - apToReach;
            if (apForAttacks < 2) return false;

            int targetHp = target.Stats.CurrentHealth;
            int weaponDmg = 5;
            var weapon = actor.Sheet?.GetEquippedWeapon();
            if (weapon != null && weapon.BaseDamage > 0) weaponDmg = weapon.BaseDamage + (weapon.LoadedHD ? 1 : 0);

            int estimatedDmg = Mathf.Max(4, (weaponDmg + 3 - target.Stats.BaseArmorAbsorption - target.Stats.GetWornArmorBonus()) * 2);
            return targetHp <= estimatedDmg;
        }

        private bool CanTakeBreathSafely(TacticalUnit unit)
        {
            return unit.Stats.Essoufflement < unit.Stats.Attributes.Constitution;
        }

        // =========================================================================
        // SOUTIEN MÉDICAL & COMMANDEMENT TACTIQUE (LIVRES III & VII)
        // =========================================================================
    }
}

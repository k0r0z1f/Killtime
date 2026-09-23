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
        TacticalRetreat   // Danger mortel : repli vers couvert maximal et rupture de vue
    }

    /// <summary>
    /// Contrôleur d'IA Tactique Militaire Opérationnelle (Killtime Engine - Livres I à VIII).
    /// Intègre la théorie des jeux du duel aveugle, l'évaluation de couverture 3D (Cyrus-Beck)
    /// et la modulation prédictive des points d'action et d'énergie.
    /// </summary>
    public class TacticalAIController : MonoBehaviour
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

            if (!unit.Stats.IsAlive)
            {
                _turnManager?.EndCurrentTurn();
                return;
            }

            bool shouldControl = (_mode == CombatAIMode.FullAuto) || (!unit.IsPlayerControlled);
            if (shouldControl)
            {
                _activeTurnRoutine = StartCoroutine(ExecuteAITurn(unit));
            }
        }

        // =========================================================================
        // BOUCLE DÉCISIONNELLE STRATÉGIQUE (TACTICAL AI BRAIN)
        // =========================================================================
        private IEnumerator ExecuteAITurn(TacticalUnit unit)
        {
            yield return new WaitForSeconds(_actionDelay);

            if (_pathfinder == null && _grid != null) _pathfinder = new HexPathfinder(_grid);
            UpdateUnitCache();

            var visual = unit.GetComponent<TacticalUnitVisual>();
            int loopGuard = 0;
            const int maxSteps = 10;
            bool tookBreathThisTurn = false;

            while (unit.Stats.IsAlive && loopGuard++ < maxSteps && unit.Stats.CurrentActionPoints > 0)
            {
                while (Killtime.UI.CombatHUD.IsPaused)
                {
                    yield return null;
                }

                // 1. SOUTIEN MÉDICAL D'URGENCE (Livre VII)
                if (TryExecuteMedicalSupport(unit, visual))
                {
                    yield return new WaitForSeconds(_actionDelay);
                    continue;
                }

                var target = EvaluateBestTarget(unit);
                if (target == null) break;

                int dist = unit.CurrentCoords.DistanceTo(target.CurrentCoords);
                var posture = EvaluateTacticalPosture(unit, target, dist);

                bool isRanged = HasRangedWeapon(unit, out int maxRange, out int minRange, out SkillType attackSkill);
                bool hasLOS = HasLineOfSight(unit.CurrentCoords, target.CurrentCoords);
                CoverType targetCover = GetCoverLevel(unit.CurrentCoords, target.CurrentCoords);
                bool inAttackRange = isRanged ? (dist <= maxRange && hasLOS) : (dist == 1);

                // 2. RETRAITE TACTIQUE SOUS COUVERT (Livre VI §25)
                if (posture == TacticalPosture.TacticalRetreat)
                {
                    visual?.SpawnFloatingText("🛡️ [REPLI] Rupture sous Couvert", new Color(1.0f, 0.35f, 0.35f));
                    yield return StartCoroutine(ExecuteCoveredRetreat(unit, target));
                    yield return new WaitForSeconds(_actionDelay);
                    break;
                }

                // 2b. TECHNIQUES DE SPÉCIALISATION (Livre III) — même registre
                // que le menu contextuel joueur : Clé, Analyse, Rugissement,
                // Regard, Commandement, Tenir la Ligne ! (priorités par doctrine).
                if (TryExecuteCombatTechnique(unit, target, posture, dist))
                {
                    yield return new WaitForSeconds(_actionDelay);
                    continue;
                }

                // 3. CONTACT DIRECT (Distance == 1) : RUPTURE MARTIALE OU DÉGAGEMENT
                if (isRanged && dist == 1)
                {
                    bool hasMartialArts = unit.Stats.HasSpecialization("Arts Martiaux");
                    DiceType unarmedDie = unit.Stats.GetSkillDie(SkillType.MainsNues, true);

                    if (hasMartialArts && unit.Stats.CanAttack(unarmedDie) && unit.Stats.CurrentActionPoints >= 2)
                    {
                        visual?.SpawnFloatingText("🥋 [TEEP] Rupture d'Engagement", Color.cyan);
                        yield return StartCoroutine(ExecuteTacticalAttack(unit, target, posture, isRanged: false, SkillType.MainsNues));
                        yield return new WaitForSeconds(_actionDelay);

                        dist = unit.CurrentCoords.DistanceTo(target.CurrentCoords);
                        hasLOS = HasLineOfSight(unit.CurrentCoords, target.CurrentCoords);
                        inAttackRange = (dist <= maxRange && hasLOS);

                        if (inAttackRange && dist > 1 && unit.Stats.CurrentActionPoints >= 2)
                        {
                            continue;
                        }
                    }
                    else if (unit.Stats.CurrentActionPoints >= 3)
                    {
                        visual?.SpawnFloatingText("🔄 [DÉGAGEMENT] Recul Tactique", new Color(0.2f, 0.85f, 1.0f));
                        yield return StartCoroutine(ExecuteDisengageStep(unit, target));
                        yield return new WaitForSeconds(_actionDelay);

                        dist = unit.CurrentCoords.DistanceTo(target.CurrentCoords);
                        hasLOS = HasLineOfSight(unit.CurrentCoords, target.CurrentCoords);
                        inAttackRange = (dist <= maxRange && hasLOS);
                    }
                }

                // 4. GRENADES DE ZONE (Livre VIII §31.3)
                if (TryThrowGrenadeAI(unit, posture))
                {
                    float waitElapsed = 0f;
                    while (_arena != null && _arena.IsResolving && waitElapsed < 6f)
                    {
                        yield return null;
                        waitElapsed += Time.deltaTime;
                    }
                    yield return new WaitForSeconds(_actionDelay);
                    continue;
                }

                // 5. SORTS ARCANOTECH (Livre IV)
                if (!isRanged && dist > 1 && TryCastBestSpell(unit, target))
                {
                    yield return new WaitForSeconds(_actionDelay);
                    continue;
                }

                // 6. INTIMIDATION & PROVOCATION (Livre III)
                if (TryExecuteIntimidation(unit, target, dist, visual))
                {
                    yield return new WaitForSeconds(_actionDelay);
                    continue;
                }

                // 7. MANŒUVRE DE FLANKING & PRISE DE COUVERT AVANT ATTAQUE (Livre VI §25.3)
                if (unit.Stats.CurrentActionPoints >= 3 && (targetCover == CoverType.ThreeQuarters || !inAttackRange))
                {
                    int reserve = GetRequiredDefensiveReserve(unit, posture);
                    int moveBudget = Mathf.Max(1, unit.Stats.CurrentActionPoints - 2 - reserve);

                    var optimalCombatHex = FindOptimalCombatHex(unit, target, isRanged, maxRange, moveBudget);
                    if (optimalCombatHex.HasValue && !optimalCombatHex.Value.Equals(unit.CurrentCoords))
                    {
                        var path = _pathfinder.FindPath(unit.CurrentCoords, optimalCombatHex.Value, moveBudget, out int cost);
                        if (path != null && path.Count > 1)
                        {
                            visual?.SpawnFloatingText(isRanged ? $"🎯 Débordement (-{cost} PA)" : $"⚡ Percée (-{cost} PA)", new Color(0.2f, 0.85f, 1.0f));
                            yield return StartCoroutine(unit.MoveAlongPath(path, _grid, cost));
                            yield return new WaitForSeconds(_actionDelay);
                            continue;
                        }
                    }
                }

                // 8. ATTAQUE PRÉDICTIVE (DUEL AVEUGLE CODEX)
                if (inAttackRange)
                {
                    DiceType attackDie = unit.Stats.GetSkillDie(attackSkill, true);

                    if (unit.Stats.CanAttack(attackDie) && unit.Stats.CurrentActionPoints >= 2)
                    {
                        yield return StartCoroutine(ExecuteTacticalAttack(unit, target, posture, isRanged, attackSkill));

                        if (posture == TacticalPosture.HitAndRun && unit.Stats.CurrentActionPoints >= 1)
                        {
                            visual?.SpawnFloatingText("💨 [RETRAIT] Décrochage", new Color(0.9f, 0.7f, 0.2f));
                            yield return StartCoroutine(ExecuteDisengageStep(unit, target));
                            break;
                        }

                        yield return new WaitForSeconds(_actionDelay);
                        continue;
                    }
                    else if (unit.Stats.CanAttack(attackDie) && unit.Stats.CurrentActionPoints < 2 && !tookBreathThisTurn && CanTakeBreathSafely(unit))
                    {
                        tookBreathThisTurn = true;
                        unit.Stats.TakeEmergencyBreath(2);
                        visual?.SpawnFloatingText("🫁 Souffle d'Attaque (+2 PA, +1 ESS)", Color.yellow);
                        yield return new WaitForSeconds(_actionDelay);
                        continue;
                    }

                    break;
                }

                // 9. APPROCHE VERS LA LIGNE DE TIR OU ENGAGEMENT
                int availableAP = unit.Stats.CurrentActionPoints;
                if (availableAP >= 1)
                {
                    int reserve = GetRequiredDefensiveReserve(unit, posture);
                    int moveBudget = Mathf.Max(1, availableAP - reserve);

                    var pathToFiringPos = isRanged
                        ? FindPathTowardsRangedPosition(unit, target, maxRange, moveBudget)
                        : FindPathTowardsTarget(unit, target, moveBudget);

                    if (pathToFiringPos != null && pathToFiringPos.Count > 1)
                    {
                        int steps = pathToFiringPos.Count - 1;
                        string moveMsg = isRanged ? $"Position de tir (-{steps} PA)" : $"Avance tactique (-{steps} PA)";
                        visual?.SpawnFloatingText(moveMsg, new Color(0.2f, 0.85f, 1.0f));
                        yield return StartCoroutine(unit.MoveAlongPath(pathToFiringPos, _grid, steps));
                        yield return new WaitForSeconds(_actionDelay);
                        continue;
                    }
                }

                // 10. ORDRE TACTIQUE EN FIN DE TOUR (Livre III)
                if (unit.Stats.CurrentActionPoints >= 2 && TryDelegateTacticalOrder(unit, visual))
                {
                    yield return new WaitForSeconds(_actionDelay);
                }

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
            bool isMortalDanger = hpRatio <= _retreatHealthRatio;

            if (isMortalDanger && !CanKillTargetThisTurn(actor, target))
            {
                return TacticalPosture.TacticalRetreat;
            }

            if (CanKillTargetThisTurn(actor, target))
            {
                return TacticalPosture.AllInLethal;
            }

            // Détection de mission de soutien médical prioritaire
            if (_defaultPersonality != AIPersonality.Aggressive && actor.Stats.CurrentActionPoints >= 3)
            {
                for (int i = 0; i < _cachedUnits.Count; i++)
                {
                    var ally = _cachedUnits[i];
                    if (ally == null || ally == actor || ally.Stats == null) continue;
                    if (ally.IsPlayerControlled != actor.IsPlayerControlled) continue;
                    if (actor.CurrentCoords.DistanceTo(ally.CurrentCoords) <= 1)
                    {
                        if (!ally.Stats.IsAlive && ally.Stats.LastFatalBlowResolution != FatalBlowResolution.InstantDeath)
                            return TacticalPosture.SupportAndHeal;
                        if (ally.Stats.IsAlive && (float)ally.Stats.CurrentHealth / Mathf.Max(1, ally.Stats.MaxHealth) <= 0.35f)
                            return TacticalPosture.SupportAndHeal;
                    }
                }
            }

            if ((_defaultPersonality == AIPersonality.Tactician || _defaultPersonality == AIPersonality.Survivor)
                && dist == 1 && actor.Stats.CurrentActionPoints >= 4)
            {
                return TacticalPosture.HitAndRun;
            }

            CoverType targetCover = GetCoverLevel(actor.CurrentCoords, target.CurrentCoords);
            if (targetCover == CoverType.ThreeQuarters || targetCover == CoverType.Full)
            {
                return TacticalPosture.FlankAndSuppress;
            }

            return TacticalPosture.CalculatedStrike;
        }

        private int GetRequiredDefensiveReserve(TacticalUnit actor, TacticalPosture posture)
        {
            if (posture == TacticalPosture.AllInLethal) return 0;
            if (_defaultPersonality == AIPersonality.Aggressive) return 0;
            return Mathf.Clamp(_baseDefensiveAPReserve, 0, 1);
        }

        private bool CanKillTargetThisTurn(TacticalUnit actor, TacticalUnit target)
        {
            int maxPotentialAP = actor.Stats.CurrentActionPoints;
            if (CanTakeBreathSafely(actor)) maxPotentialAP += 2;

            int dist = actor.CurrentCoords.DistanceTo(target.CurrentCoords);
            bool isRanged = HasRangedWeapon(actor, out int maxRange, out _, out _);

            int apToReach = 0;
            if (isRanged)
            {
                apToReach = (dist <= maxRange && HasLineOfSight(actor.CurrentCoords, target.CurrentCoords)) ? 0 : Mathf.Max(0, dist - maxRange);
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
            if (weapon != null && weapon.BaseDamage > 0) weaponDmg = weapon.BaseDamage;

            int estimatedDmg = Mathf.Max(4, (weaponDmg + 3 - target.Stats.BaseArmorAbsorption) * 2);
            return targetHp <= estimatedDmg;
        }

        private bool CanTakeBreathSafely(TacticalUnit unit)
        {
            return unit.Stats.Essoufflement < unit.Stats.Attributes.Constitution;
        }

        // =========================================================================
        // SOUTIEN MÉDICAL & COMMANDEMENT TACTIQUE (LIVRES III & VII)
        // =========================================================================
        private bool TryExecuteMedicalSupport(TacticalUnit actor, TacticalUnitVisual visual)
        {
            // Chirurgie : Suture Réflexe (Livre III) — soins d'urgence à 2 PA au lieu de 3.
            int healCost = actor.Stats.HasSpecialization("Chirurgie : Suture Réflexe") ? 2 : 3;
            if (actor.Stats.CurrentActionPoints < healCost) return false;
            if (_defaultPersonality == AIPersonality.Aggressive) return false;

            for (int i = 0; i < _cachedUnits.Count; i++)
            {
                var ally = _cachedUnits[i];
                if (ally == null || ally == actor || ally.Stats == null) continue;
                if (ally.IsPlayerControlled != actor.IsPlayerControlled) continue;

                int dist = actor.CurrentCoords.DistanceTo(ally.CurrentCoords);
                if (dist > 1) continue;

                // Réanimation proscrite si mort instantanée irréversible (boîte crânienne détruite, Livre VII §30)
                if (!ally.Stats.IsAlive && ally.Stats.LastFatalBlowResolution != FatalBlowResolution.InstantDeath && actor.Stats.CurrentActionPoints >= 4)
                {
                    if (actor.Stats.ConsumeActionPoints(4))
                    {
                        ally.Stats.IsDead = false;
                        ally.Stats.CurrentHealth = 1;
                        ally.Stats.ActiveStatus &= ~StatusEffect.Inconscient;
                        ally.Stats.ActiveStatus &= ~StatusEffect.Agonisant;
                        visual?.SpawnFloatingText("⚡ [RÉANIMATION] Défibrillation (-4 PA)", Color.cyan);
                        ally.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("RÉANIMÉ ! (1 PV)", Color.green);
                        _arena?.Log($"⚡ <b>{actor.Stats.Name}</b> réanime <b>{ally.Stats.Name}</b> au contact (-4 PA) !");
                        return true;
                    }
                }

                float allyHpRatio = (float)ally.Stats.CurrentHealth / Mathf.Max(1, ally.Stats.MaxHealth);
                bool hasBleed = (ally.Stats.ActiveStatus & StatusEffect.Saignement) != 0;

                if (ally.Stats.IsAlive && (allyHpRatio <= 0.35f || hasBleed) && actor.Stats.CurrentActionPoints >= healCost)
                {
                    if (actor.Stats.ConsumeActionPoints(healCost))
                    {
                        int healAmount = ally.Stats.Attributes.Constitution * 2;
                        ally.Stats.CurrentHealth = Mathf.Min(ally.Stats.MaxHealth, ally.Stats.CurrentHealth + healAmount);
                        ally.Stats.ActiveStatus &= ~StatusEffect.Saignement;
                        visual?.SpawnFloatingText($"🩹 [SOINS] Suture Tactique (-{healCost} PA)", Color.green);
                        ally.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText($"+{healAmount} PV", Color.green);
                        _arena?.Log($"🩹 <b>{actor.Stats.Name}</b> soigne <b>{ally.Stats.Name}</b> (+{healAmount} PV) !");
                        return true;
                    }
                }
            }

            return false;
        }

        private bool TryExecuteIntimidation(TacticalUnit actor, TacticalUnit target, int dist, TacticalUnitVisual visual)
        {
            if (dist > 6 || actor.Stats.CurrentActionPoints < 2) return false;
            if ((target.Stats.ActiveStatus & StatusEffect.Destabilise) != 0) return false;
            if (_defaultPersonality != AIPersonality.Tactician && _defaultPersonality != AIPersonality.Aggressive) return false;

            int actorCha = actor.Stats.Attributes.Charisme;
            int targetIns = target.Stats.Attributes.Instinct;
            if (actorCha < targetIns) return false;

            if (actor.Stats.ConsumeActionPoints(2))
            {
                var calc = new CombatCalculator();
                var duel = calc.ResolveOpposedCheck(actor.Stats, SkillType.Intimidation, target.Stats, SkillType.Intuition, 0, 0, defenderAutoStakes: true);
                _arena?.Log($"🗣️ <b>{actor.Stats.Name}</b> lance une provocation militaire sur <b>{target.Stats.Name}</b> :\n   {duel.CombatLog}");

                if (duel.AttackerWins)
                {
                    visual?.SpawnFloatingText("🗣️ Provocation Efficace (-2 PA)", Color.cyan);
                    target.Stats.ActiveStatus |= StatusEffect.Destabilise;
                    var tgtVis = target.GetComponent<TacticalUnitVisual>();
                    tgtVis?.TriggerHitFlash();
                    tgtVis?.SpawnFloatingText("DÉSTABILISÉ ! (-2)", Color.yellow);

                    if (target.Stats.CurrentActionPoints > 0)
                    {
                        target.Stats.ConsumeActionPoints(1);
                        tgtVis?.SpawnFloatingText("-1 PA Réaction", new Color(1f, 0.6f, 0.2f));
                    }
                }
                else
                {
                    visual?.SpawnFloatingText("Provocation sans effet", Color.gray);
                }

                return true;
            }

            return false;
        }

        private bool TryDelegateTacticalOrder(TacticalUnit actor, TacticalUnitVisual visual)
        {
            // Mener (Commandement) (Livre III) : version de zone — galvanise toute
            // l'escouade à ≤3 cases d'un coup, strictement meilleure que l'ordre
            // mono-cible quand au moins un allié peut en profiter.
            if (actor.Stats.HasSpecialization(Killtime.Tactics.CombatUI.CombatTechniqueRegistry.SpecMener))
            {
                if (Killtime.Tactics.CombatUI.CombatTechniqueRegistry.ExecuteMenerZone(actor, _arena))
                {
                    return true;
                }
            }

            for (int i = 0; i < _cachedUnits.Count; i++)
            {
                var ally = _cachedUnits[i];
                if (ally == null || ally == actor || ally.Stats == null || !ally.Stats.IsAlive) continue;
                if (ally.IsPlayerControlled != actor.IsPlayerControlled) continue;

                if (actor.CurrentCoords.DistanceTo(ally.CurrentCoords) <= 6 && ally.Stats.CurrentActionPoints < ally.Stats.MaxActionPoints)
                {
                    if (actor.Stats.ConsumeActionPoints(2))
                    {
                        ally.Stats.CurrentActionPoints = Mathf.Min(ally.Stats.MaxActionPoints, ally.Stats.CurrentActionPoints + 1);
                        visual?.SpawnFloatingText("📢 Ordre Tactique (-2 PA)", new Color(0.2f, 0.85f, 1.0f));
                        ally.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("+1 PA Reçu", Color.cyan);
                        _arena?.Log($"📢 <b>{actor.Stats.Name}</b> transmet 1 PA d'action réflexe à <b>{ally.Stats.Name}</b> !");
                        return true;
                    }
                }
            }

            return false;
        }

        // =========================================================================
        // TECHNIQUES DE SPÉCIALISATION (LIVRE III) — même registre que le menu
        // joueur (CombatTechniqueRegistry) : Clé d'Articulation, Analyse de Faille,
        // Rugissement de Terreur, Regard de Prédateur, Commandement, Tenir la Ligne !
        // Priorités modulées par la personnalité et la posture tactique.
        // =========================================================================
        private bool HasWoundedAllyInRange(TacticalUnit actor, int range)
        {
            for (int i = 0; i < _cachedUnits.Count; i++)
            {
                var ally = _cachedUnits[i];
                if (ally == null || ally == actor || ally.Stats == null || !ally.Stats.IsAlive) continue;
                if (ally.IsPlayerControlled != actor.IsPlayerControlled) continue;
                if (actor.CurrentCoords.DistanceTo(ally.CurrentCoords) > range) continue;

                float ratio = (float)ally.Stats.CurrentHealth / Mathf.Max(1, ally.Stats.MaxHealth);
                if (ratio <= 0.6f) return true;
                if ((ally.Stats.ActiveStatus & (StatusEffect.Destabilise | StatusEffect.Etourdi | StatusEffect.Saignement)) != 0)
                    return true;
            }
            return false;
        }

        private bool HasThreatenedAllyNear(TacticalUnit actor, TacticalUnit threat, int range)
        {
            if (actor == null) return false;
            bool allyNearby = false;
            for (int i = 0; i < _cachedUnits.Count; i++)
            {
                var ally = _cachedUnits[i];
                if (ally == null || ally == actor || ally.Stats == null || !ally.Stats.IsAlive) continue;
                if (ally.IsPlayerControlled != actor.IsPlayerControlled) continue;
                if (actor.CurrentCoords.DistanceTo(ally.CurrentCoords) > range) continue;

                allyNearby = true;
                float ratio = (float)ally.Stats.CurrentHealth / Mathf.Max(1, ally.Stats.MaxHealth);
                if (ratio <= 0.4f) return true;
            }
            // Provocation préventive : un allié proche + une menace encore dangereuse.
            return allyNearby && threat != null && threat.Stats != null && threat.Stats.CurrentActionPoints >= 3;
        }

        private bool TryExecuteCombatTechnique(TacticalUnit actor, TacticalUnit target, TacticalPosture posture, int dist)
        {
            if (actor?.Stats == null || target?.Stats == null) return false;
            if (_arena == null || _arena.IsResolving) return false;

            bool isAggressive = (_defaultPersonality == AIPersonality.Aggressive);
            bool isTacticien = (_defaultPersonality == AIPersonality.Tactician);
            int pa = actor.Stats.CurrentActionPoints;

            // 1. TENIR LA LIGNE ! — blindage d'escouade si un allié proche souffre.
            if (!isAggressive && pa >= 2 && HasWoundedAllyInRange(actor, Killtime.Tactics.CombatUI.CombatTechniqueRegistry.AuraRange))
            {
                if (Killtime.Tactics.CombatUI.CombatTechniqueRegistry.ExecuteTenir(actor, _arena)) return true;
            }

            // 2. RUGISSEMENT DE TERREUR — zone dès 2 ennemis secouables
            // (dès 1 si Tacticien avec réserve d'attaque derrière).
            if (pa >= 2)
            {
                int shakable = Killtime.Tactics.CombatUI.CombatTechniqueRegistry.CountEnemiesInRange(actor, 3, onlyNotDestabilised: true);
                if (shakable >= 2 || (isTacticien && shakable >= 1 && pa >= 4))
                {
                    if (Killtime.Tactics.CombatUI.CombatTechniqueRegistry.ExecuteRugissement(actor, _arena)) return true;
                }
            }

            // 3. REGARD DE PRÉDATEUR — détourne la menace d'un allié en danger.
            if ((isTacticien || _defaultPersonality == AIPersonality.Survivor)
                && pa >= 4 && dist <= 6
                && actor.Stats.Attributes.Charisme >= target.Stats.Attributes.Instinct
                && HasThreatenedAllyNear(actor, target, 3))
            {
                if (Killtime.Tactics.CombatUI.CombatTechniqueRegistry.ExecuteRegard(actor, target, _arena)) return true;
            }

            // 4. ANALYSE DE FAILLE — expose les cibles coriaces avant de frapper.
            if ((isTacticien || _defaultPersonality == AIPersonality.Balanced)
                && pa >= 4 && dist >= 2 && dist <= 10
                && (target.Stats.EncaissementThreshold >= 3 || target.Stats.CurrentHealth >= 8))
            {
                if (Killtime.Tactics.CombatUI.CombatTechniqueRegistry.ExecuteAnalyse(actor, target, _arena)) return true;
            }

            // 5. MENER / COMMANDEMENT — galvanise l'escouade à court de PA.
            if (!isAggressive && pa >= 2)
            {
                int alliesMissing = Killtime.Tactics.CombatUI.CombatTechniqueRegistry.CountAlliesInRange(actor, 3, onlyMissingPA: true);
                if (alliesMissing >= 2 || (posture == TacticalPosture.SupportAndHeal && alliesMissing >= 1))
                {
                    if (Killtime.Tactics.CombatUI.CombatTechniqueRegistry.ExecuteMenerZone(actor, _arena)) return true;
                }
            }

            // 6. CLÉ D'ARTICULATION — finition (3 bruts) ou neutralisation d'une menace.
            if (dist <= 1 && pa >= 2)
            {
                bool finisher = target.Stats.CurrentHealth <= Killtime.Tactics.CombatUI.CombatTechniqueRegistry.CleRawDamage;
                bool neutralise = !isAggressive && pa >= 4 && target.Stats.CurrentActionPoints >= 4;
                if ((finisher || neutralise) && Killtime.Tactics.CombatUI.CombatTechniqueRegistry.ExecuteCle(actor, target, _arena)) return true;
            }

            return false;
        }

        // =========================================================================
        // RECHERCHE DE COUVERT & DÉBORDEMENT GÉOMÉTRIQUE (FLANKING 3D)
        // =========================================================================
        private HexCoordinates? FindOptimalCombatHex(TacticalUnit actor, TacticalUnit target, bool isRanged, int maxRange, int moveBudget)
        {
            if (_grid == null || _pathfinder == null || moveBudget <= 0) return null;

            var reachable = _pathfinder.GetReachableCoordinates(actor.CurrentCoords, moveBudget);
            if (reachable == null || reachable.Count == 0) return null;

            // Pré-filtrage unique des alliés en ligne de tir (élimine les milliers de raycasts redondants)
            List<TacticalUnit> firingAllies = null;
            if (isRanged)
            {
                for (int i = 0; i < _cachedUnits.Count; i++)
                {
                    var ally = _cachedUnits[i];
                    if (ally == null || ally == actor || ally.Stats == null || !ally.Stats.IsAlive) continue;
                    if (ally.IsPlayerControlled != actor.IsPlayerControlled) continue;
                    int allyDist = ally.CurrentCoords.DistanceTo(target.CurrentCoords);
                    if (allyDist < 2 || allyDist > 10) continue;
                    if (CoverSystem.EvaluateCover(ally.CurrentCoords, target.CurrentCoords, _grid) == CoverType.Full) continue;

                    firingAllies ??= new List<TacticalUnit>();
                    firingAllies.Add(ally);
                }
            }

            HexCoordinates? bestHex = null;
            float highestScore = float.MinValue;

            foreach (var hex in reachable)
            {
                var node = _grid.GetNode(hex);
                if (node == null || !node.IsWalkable || (node.IsOccupied && !hex.Equals(actor.CurrentCoords))) continue;

                int dist = hex.DistanceTo(target.CurrentCoords);
                if (isRanged && (dist < 2 || dist > maxRange)) continue;
                if (!isRanged && dist != 1) continue;

                CoverType targetCoverFromHex = CoverSystem.EvaluateCover(hex, target.CurrentCoords, _grid);
                if (targetCoverFromHex == CoverType.Full) continue;

                float score = 50f;

                // Flanking offensif : suppression du couvert adverse
                if (targetCoverFromHex == CoverType.None) score += 45f;
                else if (targetCoverFromHex == CoverType.Half) score += 20f;

                // Couvert défensif : protection contre la riposte
                CoverType defCoverAgainstTarget = CoverSystem.EvaluateCover(target.CurrentCoords, hex, _grid);
                if (defCoverAgainstTarget == CoverType.ThreeQuarters) score += 40f;
                else if (defCoverAgainstTarget == CoverType.Half) score += 25f;

                // Coordination d'Escouade (Livre VI §25.3) : Verrouillage en Tir Croisé optimisé
                if (isRanged && firingAllies != null)
                {
                    score += EvaluatePrecomputedCrossfireBonus(target, hex, firingAllies);
                }

                // Distance optimale pour tireur (évite le contact et maximise la ligne de mire)
                if (isRanged && dist >= 3 && dist <= 6) score += 15f;

                // Modulateurs doctrinaux
                if (_defaultPersonality == AIPersonality.Survivor) score += (defCoverAgainstTarget != CoverType.None ? 25f : -15f);
                if (_defaultPersonality == AIPersonality.Aggressive) score += (targetCoverFromHex == CoverType.None ? 30f : 0f);

                // Évaluation du coût sans instancier d'A* redondant dans la boucle
                score -= actor.CurrentCoords.DistanceTo(hex) * 3.5f;

                if (score > highestScore)
                {
                    highestScore = score;
                    bestHex = hex;
                }
            }

            return bestHex;
        }

        private IEnumerator ExecuteCoveredRetreat(TacticalUnit actor, TacticalUnit threat)
        {
            if (CanTakeBreathSafely(actor) && actor.Stats.CurrentActionPoints < 3)
            {
                actor.Stats.TakeEmergencyBreath(2);
                var visual = actor.GetComponent<TacticalUnitVisual>();
                visual?.SpawnFloatingText("🫁 Souffle de Rupture (+2 PA)", Color.red);
                yield return new WaitForSeconds(_actionDelay);
            }

            int ap = actor.Stats.CurrentActionPoints;
            var retreatHex = FindBestCoveredRetreatHex(actor.CurrentCoords, threat.CurrentCoords, ap);

            if (retreatHex.HasValue && !retreatHex.Value.Equals(actor.CurrentCoords))
            {
                var path = _pathfinder.FindPath(actor.CurrentCoords, retreatHex.Value, ap, out int cost);
                if (path != null && path.Count > 1)
                {
                    yield return StartCoroutine(actor.MoveAlongPath(path, _grid, cost));
                }
            }
        }

        private HexCoordinates? FindBestCoveredRetreatHex(HexCoordinates current, HexCoordinates threatCoords, int apBudget)
        {
            if (_pathfinder == null) return null;
            var reachable = _pathfinder.GetReachableCoordinates(current, apBudget);
            if (reachable == null || reachable.Count == 0) return null;

            HexCoordinates? bestHex = null;
            float highestScore = float.MinValue;

            foreach (var hex in reachable)
            {
                var node = _grid.GetNode(hex);
                if (node == null || !node.IsWalkable || (node.IsOccupied && !hex.Equals(current))) continue;

                int dist = hex.DistanceTo(threatCoords);
                CoverType coverFromThreat = CoverSystem.EvaluateCover(threatCoords, hex, _grid);

                float score = dist * 10f;
                if (coverFromThreat == CoverType.Full) score += 65f;
                else if (coverFromThreat == CoverType.ThreeQuarters) score += 45f;
                else if (coverFromThreat == CoverType.Half) score += 25f;

                if (score > highestScore)
                {
                    highestScore = score;
                    bestHex = hex;
                }
            }

            return bestHex;
        }

        private IEnumerator ExecuteDisengageStep(TacticalUnit actor, TacticalUnit threat)
        {
            int ap = actor.Stats.CurrentActionPoints;
            if (ap <= 0) yield break;

            var retreatHex = FindBestCoveredRetreatHex(actor.CurrentCoords, threat.CurrentCoords, 1);
            if (retreatHex.HasValue && !retreatHex.Value.Equals(actor.CurrentCoords))
            {
                var path = _pathfinder.FindPath(actor.CurrentCoords, retreatHex.Value, 1, out int cost);
                if (path != null && path.Count > 1)
                {
                    yield return StartCoroutine(actor.MoveAlongPath(path, _grid, cost));
                }
            }
        }

        // =========================================================================
        // ATTAQUE PRÉDICTIVE & INJECTION OPTIMISÉE DES MISES
        // =========================================================================
        private struct TacticalAttackPlan
        {
            public BodyPart TargetedPart;
            public bool CancelPenalty;
            public int BonusAP;
            public int BonusPE;
            public double ExpectedNetDamage;
            public bool ExceedsEncaissement;
            public bool IsLethal;
            public float UtilityScore;
        }

        private IEnumerator ExecuteTacticalAttack(TacticalUnit actor, TacticalUnit target, TacticalPosture posture, bool isRanged, SkillType attackSkill)
        {
            _arena?.SelectTarget(target);

            SkillType defenseSkill = target.Stats.Attributes.Agilite >= target.Stats.Attributes.Force
                ? SkillType.Esquive
                : SkillType.DefenseCorporelle;

            TacticalAttackPlan plan = CalculateBestAttackPlan(actor, target, posture, isRanged, attackSkill, defenseSkill);

            var visual = actor.GetComponent<TacticalUnitVisual>();
            string partLabel = plan.TargetedPart == BodyPart.Tete ? "[VISÉE] Tête" :
                               plan.TargetedPart == BodyPart.BrasDroit ? "[VISÉE] Désarmement" :
                               plan.TargetedPart == BodyPart.Jambes ? "[VISÉE] Jambes" : "[VISÉE] Torse";
            if (isRanged) partLabel = $"🎯 Tir {partLabel}";
            if (plan.BonusAP > 0 || plan.BonusPE > 0) partLabel += $" (+{plan.BonusAP} PA / +{plan.BonusPE} PE)";

            while (Killtime.UI.CombatHUD.IsPaused)
            {
                yield return null;
            }

            visual?.SpawnFloatingText(partLabel, plan.TargetedPart == BodyPart.Tete ? Color.red : Color.cyan);
            yield return new WaitForSeconds(Mathf.Min(0.5f, _actionDelay * 0.35f));

            int weaponDmg = 5;
            var equippedWeapon = actor.Sheet?.GetEquippedWeapon();
            if (equippedWeapon != null && equippedWeapon.BaseDamage > 0) weaponDmg = equippedWeapon.BaseDamage;

            _arena?.ExecuteAttack(
                targetedPart: plan.TargetedPart,
                cancelPenaltyWithAP: plan.CancelPenalty,
                attackSkill: attackSkill,
                defenseSkill: defenseSkill,
                defenderWantsToDefend: true,
                attackerSpecialization: null,
                defenderSpecialization: null,
                weaponBaseDamage: weaponDmg,
                attackerBonusAP: plan.BonusAP,
                defenderBonusAP: -1,
                attackDie: null,
                defenseDie: null,
                explicitTarget: target,
                attackerPE: plan.BonusPE,
                defenderPE: 0,
                explicitAttacker: actor
            );

            if (_arena != null)
            {
                float waitElapsed = 0f;
                while (_arena.IsResolving && (waitElapsed < 6f || CombatUI.CombatContextMenuUI.IsDefenseOpen))
                {
                    yield return null;
                    if (!CombatUI.CombatContextMenuUI.IsDefenseOpen)
                    {
                        waitElapsed += Time.deltaTime;
                    }
                }
            }
            else if (_cinematicDirector != null)
            {
                float waitElapsed = 0f;
                while (_cinematicDirector.CurrentMode == CameraMode.CinematicAction && waitElapsed < 5f)
                {
                    yield return null;
                    waitElapsed += Time.deltaTime;
                }
            }
        }

        private TacticalAttackPlan CalculateBestAttackPlan(
            TacticalUnit actor, TacticalUnit target, TacticalPosture posture,
            bool isRanged, SkillType attackSkill, SkillType defenseSkill)
        {
            DiceType attackDie = actor.Stats.GetSkillDie(attackSkill, true);
            DiceType defenseDie = target.Stats.GetSkillDie(defenseSkill, false);

            int attStatusMod = actor.Stats.GetStatusModifier(attackSkill, true);
            int defStatusMod = target.Stats.GetStatusModifier(defenseSkill, false);

            CoverType targetCover = isRanged ? GetCoverLevel(actor.CurrentCoords, target.CurrentCoords) : CoverType.None;
            int coverPen = CoverSystem.AttackPenalty(targetCover);

            int weaponDmg = 5;
            var equippedWeapon = actor.Sheet?.GetEquippedWeapon();
            if (equippedWeapon != null && equippedWeapon.BaseDamage > 0) weaponDmg = equippedWeapon.BaseDamage;

            int targetArmor = target.Stats.BaseArmorAbsorption;
            int targetEncaissement = target.Stats.EncaissementThreshold;
            int targetHp = target.Stats.CurrentHealth;

            int availableAP = actor.Stats.CurrentActionPoints;
            int reserveNeeded = GetRequiredDefensiveReserve(actor, posture);
            int spendablePE = actor.Stats.GetSpendablePE();

            double baseAttExpected = CombatCalculator.DieAverage(attackDie) + attStatusMod + coverPen;
            double baseDefExpected = target.Stats.CanDefendActively()
                ? CombatCalculator.DieAverage(defenseDie) + defStatusMod
                : 0.0;

            BodyPart[] partsToEvaluate = { BodyPart.Torse, BodyPart.Tete, BodyPart.Jambes, BodyPart.BrasDroit };
            TacticalAttackPlan bestPlan = default;
            bestPlan.UtilityScore = float.MinValue;

            for (int p = 0; p < partsToEvaluate.Length; p++)
            {
                BodyPart part = partsToEvaluate[p];
                var info = BodyPartInfo.GetInfo(part);

                for (int cancel = 0; cancel < 2; cancel++)
                {
                    bool cancelAim = (cancel == 1);
                    if (!isRanged && cancelAim) continue;
                    if (info.DifficultyModifier == 0 && cancelAim) continue;

                    int baseCost = cancelAim ? 3 : 2;
                    if (availableAP < baseCost) continue;

                    // Sanctuarisation de 2 PA si une seconde attaque est possible ce tour (Livre II & VI)
                    int maxAttacks = actor.Stats.GetMaxAttacksAllowed(attackDie);
                    bool canAttackAgain = (actor.Stats.AttacksThisTurn + 1 < maxAttacks) && (availableAP >= baseCost + 2 + reserveNeeded);
                    int apReservedForNextAttack = canAttackAgain ? 2 : 0;
                    int spendableBonusAP = Mathf.Max(0, availableAP - baseCost - reserveNeeded - apReservedForNextAttack);

                    int aimMod = cancelAim ? 0 : info.DifficultyModifier;
                    double currentAttRoll = baseAttExpected + aimMod;

                    // Optimisation de l'injection : quel bonus assure le franchissement d'encaissement ?
                    int chosenBonusAP = 0;
                    int chosenBonusPE = 0;

                    for (int injectAP = 0; injectAP <= Mathf.Min(spendableBonusAP, 3); injectAP++)
                    {
                        double diff = (currentAttRoll + injectAP) - baseDefExpected;
                        if (diff <= 0) continue;

                        double raw = weaponDmg + diff;
                        double net = Math.Max(0, raw - targetArmor);

                        chosenBonusAP = injectAP;
                        if (net > targetEncaissement || net >= targetHp)
                        {
                            break;
                        }
                    }

                    // Injection de PE si létalité accessible ou provocation de choc
                    if (spendablePE > 0 && posture == TacticalPosture.AllInLethal)
                    {
                        double testDiff = (currentAttRoll + chosenBonusAP + 1) - baseDefExpected;
                        if (testDiff > 0 && (weaponDmg + testDiff - targetArmor) >= targetHp)
                        {
                            chosenBonusPE = 1;
                        }
                    }

                    double finalDiff = (currentAttRoll + chosenBonusAP + chosenBonusPE) - baseDefExpected;
                    double finalRaw = finalDiff > 0 ? (weaponDmg + finalDiff) : 0;
                    double finalNet = Math.Max(0, finalRaw - targetArmor);

                    bool exceedsEnc = finalNet > targetEncaissement;
                    bool isLethal = finalNet >= targetHp;

                    float score = (float)finalNet * 8f;

                    if (isLethal) score += 150f;
                    if (exceedsEnc) score += 60f;

                    if (part == BodyPart.Tete)
                    {
                        if (exceedsEnc) score += 80f; // Mort instantanée
                        if (!cancelAim) score -= 25f; // Malus de -2 pénalisant
                    }
                    else if (part == BodyPart.Jambes)
                    {
                        if (target.Stats.Attributes.Rapidite > 4 && (target.Stats.ActiveStatus & StatusEffect.ATerre) == 0)
                        {
                            score += 45f; // Clouage tactique des unités rapides
                        }
                    }
                    else if (part == BodyPart.BrasDroit)
                    {
                        if (target.Stats.CurrentActionPoints >= 3) score += 30f;
                    }

                    score -= (baseCost + chosenBonusAP) * 4f;
                    score -= chosenBonusPE * 10f;

                    if (score > bestPlan.UtilityScore)
                    {
                        bestPlan.TargetedPart = part;
                        bestPlan.CancelPenalty = cancelAim;
                        bestPlan.BonusAP = chosenBonusAP;
                        bestPlan.BonusPE = chosenBonusPE;
                        bestPlan.ExpectedNetDamage = finalNet;
                        bestPlan.ExceedsEncaissement = exceedsEnc;
                        bestPlan.IsLethal = isLethal;
                        bestPlan.UtilityScore = score;
                    }
                }
            }

            return bestPlan;
        }

        // =========================================================================
        // SORTS ARCANOTECH (LIVRE IV)
        // =========================================================================
        private bool TryCastBestSpell(TacticalUnit actor, TacticalUnit target)
        {
            if (actor.Sheet == null || actor.Sheet.LearnedSpells == null || actor.Sheet.LearnedSpells.Count == 0) return false;
            int dist = actor.CurrentCoords.DistanceTo(target.CurrentCoords);

            NythariteSpell bestSpell = null;
            int highestDmg = -1;

            for (int i = 0; i < actor.Sheet.LearnedSpells.Count; i++)
            {
                var spell = actor.Sheet.LearnedSpells[i];
                if (spell == null) continue;
                if (spell.RangeInTiles >= dist && actor.Stats.CurrentActionPoints >= spell.ActionPointCost)
                {
                    if (spell.BaseArcaneDamage > highestDmg)
                    {
                        highestDmg = spell.BaseArcaneDamage;
                        bestSpell = spell;
                    }
                }
            }

            if (bestSpell != null)
            {
                var dice = new DiceRoller();
                var vis = target.GetComponent<TacticalUnitVisual>();
                var actorVis = actor.GetComponent<TacticalUnitVisual>();
                actorVis?.SpawnFloatingText($"🔮 {bestSpell.Name} (-{bestSpell.ActionPointCost} PA)", Color.cyan);

                if (bestSpell.Cast(actor.Stats, target.Stats, dice, out _))
                {
                    vis?.TriggerHitFlash();
                    vis?.SpawnFloatingText($"-{bestSpell.BaseArcaneDamage} Arcanique", Color.magenta);
                }

                return true;
            }

            return false;
        }

        // =========================================================================
        // GRENADES TACTIQUES DE ZONE (LIVRE VIII §31.3)
        // =========================================================================
        private bool TryThrowGrenadeAI(TacticalUnit actor, TacticalPosture posture)
        {
            if (_arena == null || actor?.Stats == null || actor.Sheet == null) return false;
            if (_arena.IsResolving) return false;
            var throwables = CombatDevArena.GetThrowableGrenades(actor.Sheet);
            if (throwables.Count == 0) return false;
            DiceType atkDie = actor.Stats.GetSkillDie(SkillType.Ballistique, true);
            if (!actor.Stats.CanAttack(atkDie)) return false;

            UpdateUnitCache();
            var launcher = CombatDevArena.GetAnyLauncher(actor.Sheet);

            InventoryItem bestGrenade = null;
            TacticalUnit bestCell = null;
            bool bestUseLauncher = false;
            float bestScore = float.MinValue;
            int bestCost = 99;

            for (int gi = 0; gi < throwables.Count; gi++)
            {
                var g = throwables[gi];
                if (g == null) continue;
                bool isUtility = (g.GrenadeKind ?? "").Contains("Flash") || (g.GrenadeKind ?? "").Contains("Fumi")
                    || (g.GrenadeKind ?? "").Contains("Gaz") && g.BaseDamage <= 2;

                for (int li = 0; li < 2; li++)
                {
                    bool useL = (li == 1);
                    InventoryItem l = useL ? launcher : null;
                    if (useL && (launcher == null || !g.LauncherCompatible)) continue;
                    int maxRange = GrenadeRules.ComputeMaxRange(g, l);
                    int cost = GrenadeRules.ComputeAPCost(l, false);
                    if (actor.Stats.CurrentActionPoints < cost) continue;
                    float expected = g.BaseDamage + g.DamageDiceCount * 5.5f;

                    for (int i = 0; i < _cachedUnits.Count; i++)
                    {
                        var enemy = _cachedUnits[i];
                        if (enemy == null || enemy == actor || enemy.Stats == null || !enemy.Stats.IsAlive) continue;
                        bool hostile = actor.IsPlayerControlled ? !enemy.IsPlayerControlled : enemy.IsPlayerControlled;
                        if (_mode == CombatAIMode.FullAuto && enemy.IsPlayerControlled != actor.IsPlayerControlled) hostile = true;
                        if (!hostile) continue;

                        int dThrow = actor.CurrentCoords.DistanceTo(enemy.CurrentCoords);
                        if (dThrow > maxRange) continue;
                        if (actor.CurrentCoords.DistanceTo(enemy.CurrentCoords) <= Mathf.Max(0, g.BlastRadius)) continue;

                        int foes = 0, allies = 0;
                        for (int j = 0; j < _cachedUnits.Count; j++)
                        {
                            var u = _cachedUnits[j];
                            if (u == null || u.Stats == null || !u.Stats.IsAlive) continue;
                            if (u.CurrentCoords.DistanceTo(enemy.CurrentCoords) > Mathf.Max(0, g.BlastRadius)) continue;
                            bool uHostile = actor.IsPlayerControlled ? !u.IsPlayerControlled : u.IsPlayerControlled;
                            if (_mode == CombatAIMode.FullAuto && u.IsPlayerControlled != actor.IsPlayerControlled) uHostile = true;
                            if (u == actor || !uHostile) allies += (u == actor) ? 2 : 1;
                            else foes++;
                        }

                        if (allies > 0 && posture != TacticalPosture.AllInLethal) continue;
                        float score = foes * (10f + expected) - allies * 25f + (isUtility ? -6f : 0f);
                        if (enemy.Stats.CurrentHealth <= expected) score += 10f;

                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestGrenade = g;
                            bestCell = enemy;
                            bestUseLauncher = useL;
                            bestCost = cost;
                        }
                    }
                }
            }

            if (bestGrenade == null || bestCell == null || bestScore < 18f) return false;
            if (actor.Stats.CurrentActionPoints < bestCost) return false;

            var visual = actor.GetComponent<TacticalUnitVisual>();
            visual?.SpawnFloatingText(
                bestUseLauncher ? $"💣 [IA] Tir {bestGrenade.Name}" : $"💣 [IA] Lancer {bestGrenade.Name}",
                new Color(1f, 0.6f, 0.15f));
            int bonus = (posture == TacticalPosture.AllInLethal) ? Mathf.Min(2, actor.Stats.CurrentActionPoints - bestCost) : 0;
            _arena.ExecuteGrenadeThrow(bestCell.CurrentCoords, bestGrenade.ItemId, bestUseLauncher, false, Mathf.Max(0, bonus));
            return true;
        }

        // =========================================================================
        // RECHERCHE DE CHEMIN ET TOPOLOGIE DE GRILLE
        // =========================================================================
        private List<HexCoordinates> FindPathTowardsTarget(TacticalUnit actor, TacticalUnit target, int apBudget)
        {
            if (_pathfinder == null || _grid == null || apBudget <= 0) return null;

            HexCoordinates bestNeighbor = target.CurrentCoords;
            int minDistance = int.MaxValue;

            for (int dir = 0; dir < 6; dir++)
            {
                var n = target.CurrentCoords.GetNeighbor(dir);
                var nNode = _grid.GetNode(n);
                if (nNode != null && nNode.IsWalkable && (!nNode.IsOccupied || n.Equals(actor.CurrentCoords)))
                {
                    int d = actor.CurrentCoords.DistanceTo(n);
                    if (d < minDistance)
                    {
                        minDistance = d;
                        bestNeighbor = n;
                    }
                }
            }

            if (actor.CurrentCoords.DistanceTo(target.CurrentCoords) <= 1) return null;

            var fullPath = _pathfinder.FindPath(actor.CurrentCoords, bestNeighbor, 50, out _);
            if (fullPath == null || fullPath.Count <= 1) return null;

            var truncatedPath = new List<HexCoordinates> { fullPath[0] };
            int accumulatedCost = 0;

            for (int i = 1; i < fullPath.Count; i++)
            {
                var step = fullPath[i];
                var stepNode = _grid.GetNode(step);
                int stepCost = stepNode != null ? stepNode.ActionPointCost : 1;

                if (accumulatedCost + stepCost > apBudget) break;
                if (stepNode != null && (stepNode.IsOccupied || !stepNode.IsWalkable)) break;

                accumulatedCost += stepCost;
                truncatedPath.Add(step);
            }

            return truncatedPath.Count > 1 ? truncatedPath : null;
        }

        private List<HexCoordinates> FindPathTowardsRangedPosition(TacticalUnit actor, TacticalUnit target, int maxRange, int apBudget)
        {
            if (_pathfinder == null || _grid == null || apBudget <= 0) return null;

            int curDist = actor.CurrentCoords.DistanceTo(target.CurrentCoords);
            if (curDist <= maxRange && HasLineOfSight(actor.CurrentCoords, target.CurrentCoords)) return null;

            HexCoordinates bestNeighbor = target.CurrentCoords;
            int minDistance = int.MaxValue;

            for (int dir = 0; dir < 6; dir++)
            {
                var n = target.CurrentCoords.GetNeighbor(dir);
                var nNode = _grid.GetNode(n);
                if (nNode != null && nNode.IsWalkable && (!nNode.IsOccupied || n.Equals(actor.CurrentCoords)))
                {
                    int d = actor.CurrentCoords.DistanceTo(n);
                    if (d < minDistance)
                    {
                        minDistance = d;
                        bestNeighbor = n;
                    }
                }
            }

            var fullPath = _pathfinder.FindPath(actor.CurrentCoords, bestNeighbor, 50, out _);
            if (fullPath == null || fullPath.Count <= 1) return null;

            int targetStepIndex = fullPath.Count - 1;
            for (int i = 1; i < fullPath.Count; i++)
            {
                int d = fullPath[i].DistanceTo(target.CurrentCoords);
                if (d <= maxRange && HasLineOfSight(fullPath[i], target.CurrentCoords))
                {
                    targetStepIndex = i;
                    break;
                }
            }

            var truncatedPath = new List<HexCoordinates> { fullPath[0] };
            int accumulatedCost = 0;

            for (int i = 1; i <= targetStepIndex; i++)
            {
                var step = fullPath[i];
                var stepNode = _grid.GetNode(step);
                int stepCost = stepNode != null ? stepNode.ActionPointCost : 1;

                if (accumulatedCost + stepCost > apBudget) break;
                if (stepNode != null && (stepNode.IsOccupied || !stepNode.IsWalkable)) break;

                accumulatedCost += stepCost;
                truncatedPath.Add(step);
            }

            return truncatedPath.Count > 1 ? truncatedPath : null;
        }

        private bool HasRangedWeapon(TacticalUnit unit, out int maxRange, out int minRange, out SkillType attackSkill)
        {
            maxRange = 1;
            minRange = 1;
            attackSkill = unit != null && unit.Stats != null ? unit.Stats.GetBestMeleeAttackSkill() : SkillType.MainsNues;

            if (unit == null || unit.Stats == null) return false;

            var sheet = unit.Sheet ?? unit.GetOrBuildSheet();
            var weapon = sheet?.GetEquippedWeapon();

            if (weapon != null && weapon.Type == ItemType.Weapon)
            {
                if (weapon.AssociatedSkill == SkillType.Ballistique || weapon.RangeInTiles > 1 || TacticalUnitVisual.IsRifleWeapon(weapon))
                {
                    maxRange = Mathf.Max(2, weapon.RangeInTiles);
                    minRange = 1;
                    attackSkill = SkillType.Ballistique;
                    return true;
                }
            }

            var visual = unit.GetComponent<TacticalUnitVisual>();
            if (visual != null && visual.HasRifleEquipped())
            {
                maxRange = 10;
                minRange = 1;
                attackSkill = SkillType.Ballistique;
                return true;
            }

            int balRank = unit.Stats.GetSkillRank(SkillType.Ballistique, true);
            int meleeRank = unit.Stats.GetSkillRank(attackSkill, true);
            if (balRank > meleeRank + 2)
            {
                maxRange = 8;
                minRange = 1;
                attackSkill = SkillType.Ballistique;
                return true;
            }

            return false;
        }

        private bool HasLineOfSight(HexCoordinates from, HexCoordinates to)
        {
            if (_grid == null) return true;
            return CoverSystem.EvaluateCover(from, to, _grid) != CoverType.Full;
        }

        private CoverType GetCoverLevel(HexCoordinates from, HexCoordinates to)
        {
            if (_grid == null) return CoverType.None;
            return CoverSystem.EvaluateCover(from, to, _grid);
        }

        private TacticalUnit EvaluateBestTarget(TacticalUnit actor)
        {
            TacticalUnit bestTarget = null;
            float highestScore = float.MinValue;
            bool isRanged = HasRangedWeapon(actor, out int maxRange, out _, out _);

            // Regard de Prédateur (Livre III) : provoqué → l'unité DOIT attaquer
            // son provocateur tant que la marque est active et qu'il est en vie.
            if (SkillTechniqueState.TryGetTaunter(actor.Stats, out var taunter) && taunter != null)
            {
                for (int i = 0; i < _cachedUnits.Count; i++)
                {
                    var provoker = _cachedUnits[i];
                    if (provoker != null && provoker.Stats == taunter && provoker.Stats.IsAlive)
                    {
                        return provoker;
                    }
                }
            }

            for (int i = 0; i < _cachedUnits.Count; i++)
                {
                var potential = _cachedUnits[i];
                if (potential == null || potential == actor || potential.Stats == null || !potential.Stats.IsAlive) continue;

                bool isHostile = actor.IsPlayerControlled ? !potential.IsPlayerControlled : potential.IsPlayerControlled;
                if (_mode == CombatAIMode.FullAuto && potential.IsPlayerControlled != actor.IsPlayerControlled) isHostile = true;
                if (!isHostile) continue;

                int dist = actor.CurrentCoords.DistanceTo(potential.CurrentCoords);
                float curHp = potential.Stats.CurrentHealth;

                float score = 80f;

                if (isRanged)
                {
                    bool inRangedZone = (dist <= maxRange);
                    bool hasLOS = HasLineOfSight(actor.CurrentCoords, potential.CurrentCoords);

                    if (inRangedZone && hasLOS)
                    {
                        score += 50f;
                        if (dist >= 2 && dist <= maxRange) score += 15f;
                        CoverType targetCover = GetCoverLevel(actor.CurrentCoords, potential.CurrentCoords);
                        if (targetCover == CoverType.Half) score -= 10f;
                        else if (targetCover == CoverType.ThreeQuarters) score -= 25f;
                    }
                    else if (inRangedZone && !hasLOS)
                    {
                        score += 5f;
                    }
                    else
                    {
                        score -= (dist - maxRange) * 6f;
                    }
                }
                else
                {
                    score -= (dist * 8f);
                    if (dist == 1) score += 45f;
                }

                // Opportunisme létal & Seuil d'encaissement (Livre I)
                score += (1.0f - (curHp / Mathf.Max(1, potential.Stats.MaxHealth))) * 40f;
                int encaissement = potential.Stats.EncaissementThreshold;
                if (curHp <= encaissement) score += 30f;
                if (curHp <= 6) score += 50f;

                // Coordination d'Escouade (Livre III) : Focus-Fire
                TacticalUnit squadTarget = GetSquadMarkedTarget(actor);
                if (squadTarget != null && squadTarget == potential)
                {
                    score += 45f;
                }

                // Exploitation des brèches défensives créées par les alliés
                if ((potential.Stats.ActiveStatus & StatusEffect.Destabilise) != 0) score += 20f;
                if ((potential.Stats.ActiveStatus & StatusEffect.ATerre) != 0) score += 25f;

                // Analyse de Faille (Livre III) : focus-fire d'escouade sur la
                // cible dont un allié a exposé la faille (encaissement ignoré).
                if (SkillTechniqueState.IsFlawExposed(potential.Stats)) score += 30f;

                // Priorité de menace active
                if (potential.Stats.CurrentActionPoints >= 4) score += 15f;

                if (score > highestScore)
                {
                    highestScore = score;
                    bestTarget = potential;
                }
            }

            if (bestTarget != null)
            {
                SetSquadMarkedTarget(actor, bestTarget);
            }

            return bestTarget;
        }

        private void UpdateUnitCache()
        {
            _cachedUnits.Clear();
            _cachedUnits.AddRange(FindObjectsByType<TacticalUnit>());
        }

        private TacticalUnit GetSquadMarkedTarget(TacticalUnit actor)
        {
            var target = actor.IsPlayerControlled ? _squadMarkedEnemyOfPlayers : _squadMarkedEnemyOfAI;
            if (target != null && target.Stats != null && target.Stats.IsAlive)
            {
                return target;
            }
            return null;
        }

        private void SetSquadMarkedTarget(TacticalUnit actor, TacticalUnit target)
        {
            if (actor.IsPlayerControlled)
            {
                _squadMarkedEnemyOfPlayers = target;
            }
            else
            {
                _squadMarkedEnemyOfAI = target;
            }
        }

        private float EvaluatePrecomputedCrossfireBonus(TacticalUnit target, HexCoordinates candidateHex, List<TacticalUnit> firingAllies)
        {
            if (_grid == null || target == null || firingAllies == null || firingAllies.Count == 0) return 0f;

            var targetNode = _grid.GetNode(target.CurrentCoords);
            Vector3 targetPos = targetNode != null ? targetNode.WorldPosition : target.transform.position;

            var candNode = _grid.GetNode(candidateHex);
            Vector3 candPos = candNode != null ? candNode.WorldPosition : candidateHex.ToWorldPosition(_grid.HexRadius, targetPos.y);

            Vector3 toCandidate = (candPos - targetPos);
            toCandidate.y = 0f;
            if (toCandidate.sqrMagnitude < 0.01f) return 0f;
            toCandidate.Normalize();

            float bestCrossfireScore = 0f;

            for (int i = 0; i < firingAllies.Count; i++)
            {
                var ally = firingAllies[i];
                if (ally == null || !ally.Stats.IsAlive) continue;

                Vector3 toAlly = (ally.transform.position - targetPos);
                toAlly.y = 0f;
                if (toAlly.sqrMagnitude < 0.01f) continue;
                toAlly.Normalize();

                float angle = Vector3.Angle(toCandidate, toAlly);

                // Embuscade en L (50° à 130°) : la cible ne peut être abritée des deux tireurs simultanément
                if (angle >= 50f && angle <= 130f)
                {
                    float angleQuality = 1.0f - (Mathf.Abs(90f - angle) / 40f);
                    float score = 35f * Mathf.Clamp01(angleQuality);
                    if (score > bestCrossfireScore)
                    {
                        bestCrossfireScore = score;
                    }
                }
            }

            return bestCrossfireScore;
        }
    }
}
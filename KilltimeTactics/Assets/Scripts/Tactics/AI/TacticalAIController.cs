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
        private static HexCoordinates? _squadLastHeardNoiseCoords;
        private static int _squadNoiseAlertRound = -1;
        private static bool _squadNoiseFromPlayer = true;

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

            while (unit.Stats.IsAlive && loopGuard++ < maxSteps && unit.Stats.CurrentActionPoints > 0)
            {
                while (Killtime.UI.CombatHUD.IsPaused)
                {
                    yield return null;
                }

                // RD-047 : Reddition (Unité désarmée, hors combat)
                if (unit.Stats.IsSurrendered)
                {
                    visual?.SpawnFloatingText("🏳️ [REDDITION] Dépôt des armes", Color.white);
                    _turnManager?.EndCurrentTurn();
                    yield break;
                }

                // RD-047 : Déroute & Fuite Panique (StatusEffect.Agonisant)
                if (unit.Stats.ActiveStatus.HasFlag(StatusEffect.Agonisant))
                {
                    visual?.SpawnFloatingText("😱 [DÉROUTE] Fuite Panique !", new Color(0.95f, 0.2f, 0.85f));
                    yield return StartCoroutine(ExecutePanickedFlee(unit));
                    yield return new WaitForSeconds(_actionDelay);
                    break;
                }

                // 1. SOUTIEN MÉDICAL D'URGENCE (Livre VII)
                if (TryExecuteMedicalSupport(unit, visual))
                {
                    yield return new WaitForSeconds(_actionDelay);
                    continue;
                }

                // 1b. SOUTIEN TACTIQUE (corde, entraves, traînée, radio,
                // maintenance, garde du corps) — registre joueur, côté alliés.
                if (TryExecuteAllySupport(unit, visual))
                {
                    yield return new WaitForSeconds(_actionDelay);
                    continue;
                }

                var target = EvaluateBestTarget(unit);
                if (target == null)
                {
                    // RD-IA01 : face au vide (furtifs non révélés), tentative
                    // d'Observation active (1 PA) avant d'abandonner le tour.
                    if (TryActiveObservationAI(unit, visual))
                    {
                        yield return new WaitForSeconds(_actionDelay);
                        continue;
                    }
                    // RD-053 : Patrouille alertée — bruit / lampe attire l'IA vers la dernière position suspecte
                    if (HasAlertedPatrolTarget(unit, out HexCoordinates suspiciousTarget, out string patrolReason))
                    {
                        yield return StartCoroutine(ExecuteAlertedPatrol(unit, suspiciousTarget, patrolReason, visual));
                        yield return new WaitForSeconds(_actionDelay);
                        continue;
                    }
                    break;
                }

                int dist = TitanFootprint.MinDistanceBetweenUnits(unit.CurrentCoords, unit.FootprintType, target.CurrentCoords, target.FootprintType);
                var posture = EvaluateTacticalPosture(unit, target, dist);

                bool isRanged = HasRangedWeapon(unit, out int maxRange, out int minRange, out SkillType attackSkill);
                if (isRanged && !ChargeState.CanFireRanged(unit.Stats))
                {
                    isRanged = false; // RD-038 : Le sprint bloque le tir ce tour
                }
                bool hasLOS = HasLineOfSight(unit.CurrentCoords, target.CurrentCoords, target.FootprintType, unit.FootprintType, unit);
                CoverType targetCover = GetCoverLevel(unit.CurrentCoords, target.CurrentCoords, target.FootprintType, unit.FootprintType, unit);
                bool inAttackRange = isRanged ? (dist <= maxRange && hasLOS) : (dist <= 1);

                // 2. RETRAITE TACTIQUE SOUS COUVERT (Livre VI §25)
                if (posture == TacticalPosture.TacticalRetreat)
                {
                    visual?.SpawnFloatingText("🛡️ [REPLI] Rupture sous Couvert", new Color(1.0f, 0.35f, 0.35f));
                    yield return StartCoroutine(ExecuteCoveredRetreat(unit, target));
                    yield return new WaitForSeconds(_actionDelay);
                    // RD-IA02 : après repli, verrouille la position en guet si le
                    // surplus le permet (au lieu de finir le tour inactif).
                    if (isRanged && _arena != null
                        && !OverwatchState.IsWatching(unit.Stats)
                        && unit.Stats.CurrentActionPoints >= CoreRulesConfig.Instance.OverwatchAPCost + GetRequiredDefensiveReserve(unit, posture))
                    {
                        if (_arena.TryEnterOverwatch(unit, out _))
                            yield return new WaitForSeconds(_actionDelay);
                    }
                    break;
                }

                // 2a. SOUTIEN : rejoindre l'allié blessé (la posture
                // SupportAndHeal ne servait à rien au-delà du contact).
                if (posture == TacticalPosture.SupportAndHeal)
                {
                    var allyPath = GetWoundedAllyPath(unit);
                    if (allyPath != null && allyPath.Count > 1)
                    {
                        int rawAllyCost = 0;
                        for (int pi = 1; pi < allyPath.Count; pi++)
                        {
                            var anode = _grid != null ? _grid.GetNode(allyPath[pi]) : null;
                            rawAllyCost += anode != null ? anode.ActionPointCost : 1;
                        }
                        int allyApCost = unit.ComputeMovementAPCost(rawAllyCost);
                        visual?.SpawnFloatingText("🩹 Soutien allié", Color.green);
                        EnsureSafeDisengageForMove(unit, allyPath.Count - 1);
                        yield return StartCoroutine(unit.MoveAlongPath(allyPath, _grid, allyApCost));
                        yield return new WaitForSeconds(_actionDelay);
                        continue;
                    }
                }

                // 2b. TECHNIQUES DE SPÉCIALISATION (Livre III) — même registre
                // que le menu contextuel joueur : Clé, Analyse, Rugissement,
                // Regard, Commandement, Tenir la Ligne ! (priorités par doctrine).
                if (TryExecuteCombatTechnique(unit, target, posture, dist))
                {
                    yield return new WaitForSeconds(_actionDelay);
                    continue;
                }

                // 2c. MANŒUVRES AVANCÉES PNJ & AUTOMATES (Livre XI §44 & §47 - RD-088)
                if (TryExecuteLivreXIManeuver(unit, target, posture, dist, visual))
                {
                    yield return new WaitForSeconds(_actionDelay);
                    if (OverwatchState.IsWatching(unit.Stats))
                    {
                        break;
                    }
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

                        dist = TitanFootprint.MinDistanceBetweenUnits(unit.CurrentCoords, unit.FootprintType, target.CurrentCoords, target.FootprintType);
                        hasLOS = HasLineOfSight(unit.CurrentCoords, target.CurrentCoords, target.FootprintType, unit.FootprintType, unit);
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

                        dist = TitanFootprint.MinDistanceBetweenUnits(unit.CurrentCoords, unit.FootprintType, target.CurrentCoords, target.FootprintType);
                        hasLOS = HasLineOfSight(unit.CurrentCoords, target.CurrentCoords, target.FootprintType, unit.FootprintType, unit);
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

                // 5. SORTS ARCANOTECH (Livre IV) — rentables à distance comme au
                // contact (la sélection du sort gère portée, taxe focus et CdV).
                if (TryCastBestSpell(unit, target, dist, hasLOS))
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

                // 6b. ENTRETIEN & BUFFS PERSONNELS (bouclier, soins, dopants,
                // ramassage d'arme, canalisation, furtivité) — même registre
                // que le menu joueur, joués avant de frapper.
                if (TryExecuteSelfUpkeep(unit, posture, dist, inAttackRange, visual))
                {
                    yield return new WaitForSeconds(_actionDelay);
                    continue;
                }

                // 6c. BALISE LASER (Lampe + Batterie, 1 PA) : expose la faille
                // des cibles blindées avant la frappe (encaissement ignoré).
                if (TryExecuteLaserDesignation(unit, target, dist, visual))
                {
                    yield return new WaitForSeconds(_actionDelay);
                    continue;
                }

                // 6d. CHARGE AU CONTACT (RD-038, Livre VI) : 3+ cases + frappe
                // d'assaut (+2 dégâts) au lieu d'approche + attaque séparées.
                if (!isRanged && dist > 1 && TryExecuteChargeAttack(unit, target, visual))
                {
                    float waitElapsed = 0f;
                    while (_arena != null && _arena.IsResolving && waitElapsed < 8f)
                    {
                        yield return null;
                        waitElapsed += Time.deltaTime;
                    }
                    yield return new WaitForSeconds(_actionDelay);
                    continue;
                }

                // 6e. FRAPPE TITANESQUE (RD-052) : les colosses écrasent en zone
                // (Souffle / Piétinement) ou saisissent au contact.
                if (TryExecuteTitanSlam(unit, target, posture, dist, visual))
                {
                    yield return new WaitForSeconds(_actionDelay);
                    continue;
                }

                // 6f. BOUSCULADE / COUP DE BOUCLIER (2 PA, contact) : contrôle
                // de position quand la frappe classique est peu rentable.
                if (dist <= 1 && TryExecuteShove(unit, target, targetCover, visual))
                {
                    yield return new WaitForSeconds(_actionDelay);
                    continue;
                }

                // 6g. LANCER D'ARME (2 PA, 2-4 cases) : ultime recours quand le
                // tir est impossible (chargeur vide sans réserve, enrayage sec).
                if (dist >= 2 && TryExecuteWeaponThrow(unit, target, dist, visual))
                {
                    float waitElapsed2 = 0f;
                    while (_arena != null && _arena.IsResolving && waitElapsed2 < 6f)
                    {
                        yield return null;
                        waitElapsed2 += Time.deltaTime;
                    }
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
                            EnsureSafeDisengageForMove(unit, path.Count - 1);
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
                        EnsureSafeDisengageForMove(unit, steps);
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

                // 10b. GUET / OVERWATCH (RD-030) : sans autre action rentable et
                // avec du surplus au-delà de la réserve défensive, l'IA à distance
                // réserve un tir de réaction au lieu de finir inactive.
                if (isRanged && _arena != null
                    && !OverwatchState.IsWatching(unit.Stats)
                    && unit.Stats.CurrentActionPoints >= CoreRulesConfig.Instance.OverwatchAPCost + GetRequiredDefensiveReserve(unit, posture))
                {
                    if (_arena.TryEnterOverwatch(unit, out _))
                    {
                        yield return new WaitForSeconds(_actionDelay);
                    }
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

            CoverType targetCover = GetCoverLevel(actor.CurrentCoords, target.CurrentCoords, target.FootprintType, actor.FootprintType, actor);
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
            if (posture == TacticalPosture.AlertedPatrol) return 1;
            // RD-IA03 : respecte le réglage 0-2 (avant : écrasé à 1 max).
            return Mathf.Clamp(_baseDefensiveAPReserve, 0, 2);
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
                        // RD-083 : résurrection horodatée (Poids du Trépas §29.3).
                        int convalescence = ally.Stats.ApplyResurrection(1);
                        visual?.SpawnFloatingText("⚡ [RÉANIMATION] Défibrillation (-4 PA)", Color.cyan);
                        ally.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("RÉANIMÉ ! (1 PV)", Color.green);
                        _arena?.Log($"⚡ <b>{actor.Stats.Name}</b> réanime <b>{ally.Stats.Name}</b> au contact (-4 PA) ! <b>Poids du Trépas</b> : -1 ec pendant {convalescence}j réels.");
                        return true;
                    }
                }

                float allyHpRatio = (float)ally.Stats.CurrentHealth / Mathf.Max(1, ally.Stats.MaxHealth);
                bool hasBleed = (ally.Stats.ActiveStatus & StatusEffect.Saignement) != 0;
                bool hasSouffrant = (ally.Stats.ActiveStatus & StatusEffect.Souffrant) != 0;
                bool hasPoison = (ally.Stats.ActiveStatus & StatusEffect.Empoisonne) != 0;
                bool hasChirKit = SmokeScreen.FindItemByName(actor.Stats, "Chirurgie") != null;

                if (ally.Stats.IsAlive && (allyHpRatio <= 0.35f || hasBleed || hasSouffrant || (hasPoison && hasChirKit)) && actor.Stats.CurrentActionPoints >= healCost)
                {
                    if (actor.Stats.ConsumeActionPoints(healCost))
                    {
                        int healAmount = ally.Stats.Attributes.Constitution * (hasChirKit ? 3 : 2);
                        // RD-083 : suture tactique = Soins Majeurs (lève Souffrant, CON×2/×3).
                        var major = ally.Stats.ApplyMajorCare(healAmount);
                        if (hasChirKit)
                        {
                            ally.Stats.RemoveStatus(StatusEffect.Empoisonne);
                            ally.Stats.RemoveStatus(StatusEffect.EnFeu);
                            ally.Stats.RemoveStatus(StatusEffect.Asphyxie);
                        }
                        visual?.SpawnFloatingText($"🩹 [SOINS] Suture Tactique (-{healCost} PA)", Color.green);
                        ally.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText($"+{major.Healed} PV{(hasChirKit ? " (Kit)" : "")}{(major.SouffrantLifted ? " — Souffrant LEVÉ" : "")}", Color.green);
                        _arena?.Log($"🩹 <b>{actor.Stats.Name}</b> soigne <b>{ally.Stats.Name}</b> (+{major.Healed} PV{(hasChirKit ? ", Kit Chirurgie" : "")}{(major.SouffrantLifted ? " ➔ <b>Souffrant LEVÉ</b>" : (major.HadSouffrant ? " ➔ Souffrant persiste" : ""))}) !");
                        return true;
                    }
                }
            }

            return false;
        }

        private bool TryExecuteIntimidation(TacticalUnit actor, TacticalUnit target, int dist, TacticalUnitVisual visual)
        {
            int maxTauntRange = SmokeScreen.HasItemByName(actor.Stats, "Radio") ? 12 : 6;
            if (dist > maxTauntRange || actor.Stats.CurrentActionPoints < 2) return false;
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

            int orderRange = SmokeScreen.HasItemByName(actor.Stats, "Radio") ? 12 : 6;
            for (int i = 0; i < _cachedUnits.Count; i++)
            {
                var ally = _cachedUnits[i];
                if (ally == null || ally == actor || ally.Stats == null || !ally.Stats.IsAlive) continue;
                if (ally.IsPlayerControlled != actor.IsPlayerControlled) continue;

                if (actor.CurrentCoords.DistanceTo(ally.CurrentCoords) <= orderRange && ally.Stats.CurrentActionPoints < ally.Stats.MaxActionPoints)
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
        // MANŒUVRES AVANCÉES PNJ & AUTOMATES (LIVRE XI §44 & §47 - RD-088)
        // =========================================================================
        private void CheckAndApplyHydraulicBite(TacticalUnit unit, TacticalUnitVisual visual)
        {
            if (unit?.Stats == null || !unit.Stats.IsAlive) return;
            var victimStats = LivreXIPNJState.GetVictimOf(unit.Stats);
            if (victimStats == null) return;

            TacticalUnit victimUnit = null;
            for (int i = 0; i < _cachedUnits.Count; i++)
            {
                if (_cachedUnits[i] != null && _cachedUnits[i].Stats == victimStats)
                {
                    victimUnit = _cachedUnits[i];
                    break;
                }
            }

            if (victimUnit == null || !victimStats.IsAlive || unit.CurrentCoords.DistanceTo(victimUnit.CurrentCoords) > 1)
            {
                LivreXIPNJState.ReleaseHydraulicBite(unit.Stats);
                return;
            }

            int dmg = 2;
            int rem = victimStats.AbsorbShield(dmg);
            if (rem > 0)
            {
                if (victimStats.CurrentHealth - rem <= 0)
                    victimStats.EvaluateFatalBlow(BodyPart.Jambes, rem);
                else
                    victimStats.CurrentHealth -= rem;
            }

            victimStats.ApplyStatus(StatusEffect.Immobilise, 1);
            visual?.SpawnFloatingText("🦷 Broyage Hydraulique (-2 PV auto)", new Color(1f, 0.25f, 0.1f));
            var tgtVis = victimUnit.GetComponent<TacticalUnitVisual>();
            tgtVis?.TriggerHitFlash();
            tgtVis?.SpawnFloatingText("💥 -2 PV (Morsure Hydraulique)", Color.red);
            _arena?.Log($"🦷 <b>{unit.Stats.Name}</b> broie les os de <b>{victimStats.Name}</b> avec sa mâchoire verrouillée (-2 PV automatiques) !");
        }

        private bool TryExecuteLivreXIManeuver(TacticalUnit actor, TacticalUnit target, TacticalPosture posture, int dist, TacticalUnitVisual visual)
        {
            if (actor?.Stats == null || !actor.Stats.IsAlive) return false;
            int ap = actor.Stats.CurrentActionPoints;

            // 1. BANDIT #3 (Livre XI §44)
            bool isBandit3 = actor.Stats.HasSpecialization(LivreXISpecializations.TirPrepareInterruption)
                || actor.Stats.HasSpecialization(LivreXISpecializations.PriseOtage)
                || (actor.Stats.Name != null && (actor.Stats.Name.IndexOf("Bandit #3", StringComparison.OrdinalIgnoreCase) >= 0 || actor.Stats.Name.IndexOf("Bandit 3", StringComparison.OrdinalIgnoreCase) >= 0));

            if (isBandit3)
            {
                // Prise d'otage (6 PA, arme sur tempe de cible adjacente à terre ou essoufflée)
                if (ap >= 6 && target != null && target.Stats != null && target.Stats.IsAlive && dist <= 1)
                {
                    bool isEligibleTarget = target.Stats.ActiveStatus.HasFlag(StatusEffect.ATerre)
                        || target.Stats.Essoufflement >= (target.Stats.Attributes.Constitution / 2)
                        || target.Stats.Essoufflement > 0;

                    if (isEligibleTarget && !LivreXIPNJState.IsHostage(target.Stats))
                    {
                        if (actor.Stats.ConsumeActionPoints(6))
                        {
                            target.Stats.ApplyStatus(StatusEffect.Immobilise, 2);
                            target.Stats.ApplyStatus(StatusEffect.Destabilise, 2);
                            LivreXIPNJState.RegisterHostage(actor.Stats, target.Stats);

                            visual?.SpawnFloatingText("🛑 [PRISE D'OTAGE] Arme sur tempe (-6 PA)", Color.red);
                            var tgtVis = target.GetComponent<TacticalUnitVisual>();
                            tgtVis?.TriggerHitFlash();
                            tgtVis?.SpawnFloatingText("🛑 EN OTAGE ! (Immobilisé)", Color.red);
                            _arena?.Log($"🛑 <b>{actor.Stats.Name}</b> applique son arme sur la tempe de <b>{target.Stats.Name}</b> (-6 PA, <b>PRISE D'OTAGE</b>) ! La cible est immobilisée.");
                            return true;
                        }
                    }
                }

                // Tir préparé en interruption (réserve 4 PA pour tir réflexe dès rupture de couvert ou canalisation)
                if (ap >= 4 && !OverwatchState.IsWatching(actor.Stats))
                {
                    bool isRanged = HasRangedWeapon(actor, out int maxRange, out _, out _);
                    if (isRanged)
                    {
                        CoverType targetCover = target != null ? GetCoverLevel(actor.CurrentCoords, target.CurrentCoords, target.FootprintType, actor.FootprintType, actor) : CoverType.None;
                        bool targetProtected = (targetCover == CoverType.Half || targetCover == CoverType.ThreeQuarters || targetCover == CoverType.Full);

                        if (targetProtected || dist > maxRange / 2 || ap == 4)
                        {
                            if (OverwatchState.TryEnter(actor.Stats, maxRange, 4, out _))
                            {
                                LivreXIPNJState.RegisterPreparedInterruption(actor.Stats);
                                visual?.SpawnFloatingText("🎯 [BANDIT #3] Tir Préparé (-4 PA)", Color.yellow);
                                _arena?.Log($"🎯 <b>{actor.Stats.Name}</b> réserve 4 PA pour un <b>Tir Préparé en Interruption</b> (réflexe immédiat dès rupture de couvert adverse) !");
                                return true;
                            }
                        }
                    }
                }
            }

            // 2. ILLUMO (Livre XI §47)
            bool isIllumo = actor.Stats.HasSpecialization(LivreXISpecializations.CombustionSpontanee)
                || actor.Stats.HasSpecialization(LivreXISpecializations.FouleeCendres)
                || (actor.Stats.Name != null && actor.Stats.Name.IndexOf("Illumo", StringComparison.OrdinalIgnoreCase) >= 0);

            if (isIllumo)
            {
                // Foulée de cendres (2 PA, téléport 6 cases + Aveuglé sur ennemis au départ)
                bool shouldStride = (dist <= 1 && ap >= 2) || (actor.Stats.CurrentHealth < actor.Stats.MaxHealth * 0.45f && ap >= 2);
                if (shouldStride && _grid != null)
                {
                    HexCoordinates? escapeHex = FindCinderStrideHex(actor, target, 6);
                    if (escapeHex.HasValue && !escapeHex.Value.Equals(actor.CurrentCoords))
                    {
                        if (actor.Stats.ConsumeActionPoints(2))
                        {
                            HexCoordinates origin = actor.CurrentCoords;

                            for (int i = 0; i < _cachedUnits.Count; i++)
                            {
                                var other = _cachedUnits[i];
                                if (other == null || other == actor || other.Stats == null || !other.Stats.IsAlive) continue;
                                if (other.IsPlayerControlled == actor.IsPlayerControlled && _mode != CombatAIMode.FullAuto) continue;

                                if (origin.DistanceTo(other.CurrentCoords) <= 1)
                                {
                                    other.Stats.ApplyStatus(StatusEffect.Aveugle, 1);
                                    other.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("🕶️ AVEUGLÉ (Cendres)", Color.yellow);
                                }
                            }

                            actor.TeleportTo(escapeHex.Value, _grid);

                            visual?.SpawnFloatingText("💨 [ILLUMO] Foulée de Cendres (-2 PA)", new Color(0.85f, 0.85f, 0.85f));
                            _arena?.Log($"💨 <b>{actor.Stats.Name}</b> s'évanouit dans une <b>Foulée de Cendres</b> (-2 PA) ➔ Téléportation à 6 cases, déflagration de cendres aveuglante !");
                            return true;
                        }
                    }
                }

                // Combustion spontanée sans jet projectile (4 PA, 18m = 9 cases, 5 bruts + EnFeu)
                if (ap >= 4 && target != null && target.Stats != null && target.Stats.IsAlive)
                {
                    int combustionMaxRange = 9;
                    if (dist <= combustionMaxRange && HasLineOfSight(actor.CurrentCoords, target.CurrentCoords))
                    {
                        if (actor.Stats.ConsumeActionPoints(4))
                        {
                            int rawDmg = 5;
                            int rem = target.Stats.AbsorbShield(rawDmg);
                            if (rem > 0)
                            {
                                if (target.Stats.CurrentHealth - rem <= 0)
                                    target.Stats.EvaluateFatalBlow(BodyPart.Torse, rem);
                                else
                                    target.Stats.CurrentHealth -= rem;
                            }

                            target.Stats.ApplyStatus(StatusEffect.EnFeu, durationInTurns: 2, residualDamage: 3);

                            visual?.SpawnFloatingText("🔥 [ILLUMO] Combustion Spontanée (-4 PA)", new Color(1f, 0.4f, 0f));
                            var tgtVis = target.GetComponent<TacticalUnitVisual>();
                            tgtVis?.TriggerHitFlash();
                            tgtVis?.SpawnFloatingText("🔥 -5 Dégâts Bruts [EN FEU]", Color.red);
                            _arena?.Log($"🔥 <b>{actor.Stats.Name}</b> déclenche une <b>Combustion Spontanée</b> sur <b>{target.Stats.Name}</b> (-4 PA, 5 bruts sans jet projectile) ➔ Statut [EnFeu] appliqué !");
                            return true;
                        }
                    }
                }
            }

            // 3. DRONES & MOLOSSES (Livre XI §47)
            bool isMolosse = actor.Stats.HasSpecialization(LivreXISpecializations.MorsureHydraulique)
                || (actor.Stats.Name != null && actor.Stats.Name.IndexOf("Molosse", StringComparison.OrdinalIgnoreCase) >= 0);

            if (isMolosse && dist <= 1 && ap >= 2 && target != null && target.Stats != null && target.Stats.IsAlive && !LivreXIPNJState.IsBittenByHydraulicJaw(target.Stats))
            {
                if (actor.Stats.ConsumeActionPoints(2))
                {
                    int dmg = 2;
                    int rem = target.Stats.AbsorbShield(dmg);
                    if (rem > 0)
                    {
                        if (target.Stats.CurrentHealth - rem <= 0)
                            target.Stats.EvaluateFatalBlow(BodyPart.Jambes, rem);
                        else
                            target.Stats.CurrentHealth -= rem;
                    }

                    target.Stats.ApplyStatus(StatusEffect.Immobilise, 2);
                    LivreXIPNJState.RegisterHydraulicBite(actor.Stats, target.Stats);

                    visual?.SpawnFloatingText("🦷 [MOLOSSE] Morsure Hydraulique (-2 PA)", new Color(1f, 0.25f, 0.1f));
                    var tgtVis = target.GetComponent<TacticalUnitVisual>();
                    tgtVis?.TriggerHitFlash();
                    tgtVis?.SpawnFloatingText("🔒 MÂCHOIRE VERROUILLÉE (Immobilisé, -2 PV)", Color.red);
                    _arena?.Log($"🦷 <b>{actor.Stats.Name}</b> referme sa <b>Morsure Hydraulique</b> sur <b>{target.Stats.Name}</b> (-2 PA) ➔ Mâchoire verrouillée : Immobilisé + 2 dégâts auto/tour !");
                    return true;
                }
            }

            bool isDrone = actor.Stats.HasSpecialization(LivreXISpecializations.FlashAveuglant)
                || actor.Stats.HasSpecialization(LivreXISpecializations.BaliseAppelReseau)
                || (actor.Stats.Name != null && actor.Stats.Name.IndexOf("Drone", StringComparison.OrdinalIgnoreCase) >= 0);

            if (isDrone)
            {
                // Balise d'appel de renforts réseau (3 PA, transmission télémétrique globale)
                if (ap >= 3 && !LivreXIPNJState.HasBeaconTriggered(actor.Stats))
                {
                    if (actor.Stats.ConsumeActionPoints(3))
                    {
                        LivreXIPNJState.SetBeaconTriggered(actor.Stats);
                        int round = _turnManager != null ? _turnManager.CurrentRound : 1;
                        AlertSquadToNoise(actor.CurrentCoords, round, actor.IsPlayerControlled);

                        for (int i = 0; i < _cachedUnits.Count; i++)
                        {
                            var ally = _cachedUnits[i];
                            if (ally == null || ally.Stats == null || !ally.Stats.IsAlive) continue;
                            if (ally.IsPlayerControlled != actor.IsPlayerControlled) continue;

                            ally.Stats.CurrentActionPoints = Mathf.Min(ally.Stats.MaxActionPoints, ally.Stats.CurrentActionPoints + 1);
                            ally.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("📡 LIAISON RÉSEAU (+1 PA)", Color.cyan);
                        }

                        visual?.SpawnFloatingText("📡 [DRONE] Balise Appel Réseau (-3 PA)", Color.cyan);
                        _arena?.Log($"📡 <b>{actor.Stats.Name}</b> active sa <b>Balise d'Appel Réseau</b> (-3 PA) : télémétrie transmise, coordination d'escouade (+1 PA) !");
                        return true;
                    }
                }

                // Flash aveuglant (2 PA, rayon de 3 hexagones)
                if (ap >= 2)
                {
                    int enemiesBlinded = 0;
                    for (int i = 0; i < _cachedUnits.Count; i++)
                    {
                        var enemy = _cachedUnits[i];
                        if (enemy == null || enemy.Stats == null || !enemy.Stats.IsAlive) continue;
                        if (enemy.IsPlayerControlled == actor.IsPlayerControlled && _mode != CombatAIMode.FullAuto) continue;

                        int d = actor.CurrentCoords.DistanceTo(enemy.CurrentCoords);
                        if (d <= 3 && !enemy.Stats.ActiveStatus.HasFlag(StatusEffect.Aveugle))
                        {
                            enemiesBlinded++;
                        }
                    }

                    if (enemiesBlinded > 0)
                    {
                        if (actor.Stats.ConsumeActionPoints(2))
                        {
                            for (int i = 0; i < _cachedUnits.Count; i++)
                            {
                                var enemy = _cachedUnits[i];
                                if (enemy == null || enemy.Stats == null || !enemy.Stats.IsAlive) continue;
                                if (enemy.IsPlayerControlled == actor.IsPlayerControlled && _mode != CombatAIMode.FullAuto) continue;

                                int d = actor.CurrentCoords.DistanceTo(enemy.CurrentCoords);
                                if (d <= 3)
                                {
                                    enemy.Stats.ApplyStatus(StatusEffect.Aveugle, 1);
                                    enemy.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("🕶️ AVEUGLÉ (Flash Drone)", Color.yellow);
                                }
                            }

                            visual?.SpawnFloatingText("⚡ [DRONE] Flash Aveuglant (-2 PA)", Color.white);
                            _arena?.Log($"⚡ <b>{actor.Stats.Name}</b> déclenche un <b>Flash Aveuglant</b> stroboscopique (-2 PA) ➔ Ennemis éblouis (-4 Tir à distance, vision nulle) !");
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private HexCoordinates? FindCinderStrideHex(TacticalUnit actor, TacticalUnit threat, int maxDist)
        {
            if (_grid == null) return null;
            HexCoordinates bestHex = actor.CurrentCoords;
            float bestScore = float.MinValue;

            for (int dq = -maxDist; dq <= maxDist; dq++)
            {
                int rMin = Mathf.Max(-maxDist, -dq - maxDist);
                int rMax = Mathf.Min(maxDist, -dq + maxDist);

                for (int dr = rMin; dr <= rMax; dr++)
                {
                    var hex = new HexCoordinates(actor.CurrentCoords.Q + dq, actor.CurrentCoords.R + dr);
                    int dist = actor.CurrentCoords.DistanceTo(hex);
                    if (dist < 3 || dist > maxDist) continue;

                    var node = _grid.GetNode(hex);
                    if (node == null || !node.IsWalkable || node.IsOccupied) continue;

                    int dThreat = threat != null ? hex.DistanceTo(threat.CurrentCoords) : dist;
                    float score = dThreat * 10f;
                    CoverType cover = threat != null ? CoverSystem.EvaluateCover(threat.CurrentCoords, hex, _grid) : CoverType.None;
                    if (cover == CoverType.Half) score += 20f;
                    if (cover == CoverType.ThreeQuarters || cover == CoverType.Full) score += 35f;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestHex = hex;
                    }
                }
            }

            return bestScore > float.MinValue ? bestHex : null;
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

        /// <summary>RD-084 : projette une liste d'unités vers leurs stats (assaut groupé).</summary>
        private static List<CharacterStats> ToStatsList(List<TacticalUnit> units)
        {
            var stats = new List<CharacterStats>(units != null ? units.Count : 0);
            if (units == null) return stats;
            for (int i = 0; i < units.Count; i++)
            {
                if (units[i] != null && units[i].Stats != null) stats.Add(units[i].Stats);
            }
            return stats;
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

            // 7. LUTTE, GRAPPLE & ÉTRANGLEMENT (RD-039)
            if (dist <= 1 && pa >= 2)
            {
                if (Killtime.Core.Combat.GrappleState.IsGrappling(actor.Stats, target.Stats))
                {
                    _arena.ExecuteStrangulation(actor, target);
                    return true;
                }

                if (Killtime.Core.Combat.GrappleState.IsGrappling(target.Stats, actor.Stats))
                {
                    _arena.ExecuteGrappleEscape(actor, target);
                    return true;
                }

                bool isGrapplerProfile = actor.Stats.Attributes.Force >= 5 || actor.Stats.Attributes.Constitution >= 5;
                // RD-IA05 : règle de gabarit (menu joueur) : l'attaquant doit
                // être au moins aussi massif que sa victime.
                bool sizeOk = TitanFootprint.GetHexCount(actor.FootprintType) >= TitanFootprint.GetHexCount(target.FootprintType);
                if (isGrapplerProfile && sizeOk && !target.Stats.ActiveStatus.HasFlag(StatusEffect.Immobilise) && pa >= 4
                    && !actor.Stats.ActiveStatus.HasFlag(StatusEffect.Immobilise))
                {
                    _arena.ExecuteGrapple(actor, target);
                    return true;
                }
            }

            // 8. ASSAUT GROUPÉ & POURSUITE (RD-084, Livre VI §25.1-25.2)
            if (pa >= 2 && _arena != null)
            {
                // Assaut groupé : cible blindée (armure/encaissement élevés) + alliés
                // à portée. Seul le membre le plus lent déclenche (synchro §25.2).
                int targetArmor = target.Stats.BaseArmorAbsorption + target.Stats.GetWornArmorBonus();
                if ((targetArmor >= 4 || target.Stats.EncaissementThreshold >= 6)
                    && _arena.TryResolveCombinedSkill(actor, target, out var combinedSkill))
                {
                    var members = _arena.CollectCombinedAttackers(actor, target, combinedSkill);
                    int minMembers = CoreRulesConfig.Instance != null ? CoreRulesConfig.Instance.CombinedAttackMinMembers : 2;
                    if (members.Count >= minMembers && members.Contains(actor))
                    {
                        CharacterStats slowest = GroupAssaultState.GetSlowestMember(ToStatsList(members));
                        if (slowest == actor.Stats)
                        {
                            _arena.ExecuteCombinedAttack(actor, target, BodyPart.Torse);
                            return true;
                        }
                    }
                }

                // Poursuite : cible en fuite à distance (proie affaiblie ou concours
                // déjà engagé). Concours d'Athlétisme, 4 faillites = capture.
                int distP = actor.CurrentCoords.DistanceTo(target.CurrentCoords);
                int pursuitCost = CoreRulesConfig.Instance != null ? CoreRulesConfig.Instance.PursuitAPCost : 1;
                if (distP >= 2 && pa > pursuitCost
                    && !PursuitState.IsCaught(target.Stats)
                    && (target.Stats.CurrentHealth <= target.Stats.MaxHealth * 0.35f
                        || PursuitState.GetFailures(target.Stats) > 0))
                {
                    _arena.ExecutePursuitRound(target, new List<TacticalUnit> { actor });
                    return true;
                }
            }

            // 9. TECHNIQUES HÉROÏQUES (RD-063 : Thomas-0 & Lucas-0) — même
            // registre que le menu joueur, ignorées jusque-là par l'IA.
            if (_arena != null)
            {
                // Arrêt Vectoriel (1 PA, ≤4) : figement prioritaire des menaces.
                if (pa >= 1 && dist <= 4
                    && Killtime.Tactics.CombatUI.CombatTechniqueRegistry.CanUseArretVectoriel(actor, target)
                    && Killtime.Tactics.CombatUI.CombatTechniqueRegistry.ExecuteArretVectoriel(actor, target, _arena)) return true;
                // Chute de Pression (2 PA, ≤4) : 4 absolus + Déstabilisé.
                if (pa >= 2 && dist <= 4
                    && Killtime.Tactics.CombatUI.CombatTechniqueRegistry.CanUseChuteDePression(actor, target)
                    && Killtime.Tactics.CombatUI.CombatTechniqueRegistry.ExecuteChuteDePression(actor, target, _arena)) return true;
                // Suggestions Brèves (2 PA, ≤4) : draine 1 PA adverse.
                if (pa >= 2 && dist <= 4 && target.Stats.CurrentActionPoints >= 2
                    && Killtime.Tactics.CombatUI.CombatTechniqueRegistry.CanUseSuggestionsBreves(actor, target)
                    && Killtime.Tactics.CombatUI.CombatTechniqueRegistry.ExecuteSuggestionsBreves(actor, target, _arena)) return true;
                // Commandement Tectonique (2 PA) : +1 PA de manœuvre aux alliés.
                if (pa >= 2
                    && Killtime.Tactics.CombatUI.CombatTechniqueRegistry.CanUseCommandementTectonique(actor)
                    && Killtime.Tactics.CombatUI.CombatTechniqueRegistry.ExecuteCommandementTectonique(actor, _arena)) return true;
                // Pas de Retraite ! (2 PA) : purge panique/étourdi des alliés.
                if (pa >= 2 && HasPanickedAllyInRange(actor, Killtime.Tactics.CombatUI.CombatTechniqueRegistry.AuraRange)
                    && Killtime.Tactics.CombatUI.CombatTechniqueRegistry.CanUsePasDeRetraite(actor)
                    && Killtime.Tactics.CombatUI.CombatTechniqueRegistry.ExecutePasDeRetraite(actor, _arena)) return true;
            }

            return false;
        }

        // =========================================================================
        // RD-IA : MANŒUVRES COMPLÉMENTAIRES (même registre que le menu joueur)
        // Charge, bousculade, frappe titanesque, lancer d'arme, balise laser,
        // entretien personnel, soutien allié, observation active.
        // =========================================================================

        /// <summary>RD-IA01 : Observation active quand aucune cible visible.</summary>
        private bool TryActiveObservationAI(TacticalUnit actor, TacticalUnitVisual visual)
        {
            var fog = Killtime.Tactics.Visibility.FogOfWarManager.Instance;
            if (fog == null || actor?.Stats == null || actor.Stats.CurrentActionPoints < 1) return false;

            TacticalUnit nearestHidden = null;
            int nearestDist = int.MaxValue;
            for (int i = 0; i < _cachedUnits.Count; i++)
            {
                var u = _cachedUnits[i];
                if (u == null || u == actor || u.Stats == null || !u.Stats.IsAlive || u.Stats.IsSurrendered) continue;
                bool hostile = actor.IsPlayerControlled ? !u.IsPlayerControlled : u.IsPlayerControlled;
                if (_mode == CombatAIMode.FullAuto && u.IsPlayerControlled != actor.IsPlayerControlled) hostile = true;
                if (!hostile) continue;
                int d = TitanFootprint.MinDistanceBetweenUnits(actor.CurrentCoords, actor.FootprintType, u.CurrentCoords, u.FootprintType);
                if (d < nearestDist)
                {
                    nearestDist = d;
                    nearestHidden = u;
                }
            }
            if (nearestHidden == null) return false;

            if (fog.TryActiveObservation(actor, nearestHidden, out string log))
            {
                visual?.SpawnFloatingText("👁️ Menace repérée !", Color.cyan);
            }
            else
            {
                visual?.SpawnFloatingText("👁️ Observation (1 PA)", Color.gray);
            }
            _arena?.Log(log);
            return true;
        }

        /// <summary>RD-IA02 : chemin vers l'allié à soigner quand la posture
        /// SupportAndHeal est active mais personne n'est au contact.</summary>
        private List<HexCoordinates> GetWoundedAllyPath(TacticalUnit actor)
        {
            TacticalUnit bestAlly = null;
            int bestDist = int.MaxValue;
            bool bestDead = false;
            for (int i = 0; i < _cachedUnits.Count; i++)
            {
                var ally = _cachedUnits[i];
                if (ally == null || ally == actor || ally.Stats == null) continue;
                if (ally.IsPlayerControlled != actor.IsPlayerControlled) continue;
                int d = TitanFootprint.MinDistanceBetweenUnits(actor.CurrentCoords, actor.FootprintType, ally.CurrentCoords, ally.FootprintType);
                if (d <= 1) continue;

                bool dead = !ally.Stats.IsAlive;
                bool wounded = ally.Stats.IsAlive
                    && ((float)ally.Stats.CurrentHealth / Mathf.Max(1, ally.Stats.MaxHealth) <= 0.35f
                        || (ally.Stats.ActiveStatus & (StatusEffect.Saignement | StatusEffect.Souffrant)) != 0);
                if (!dead && !wounded) continue;
                if (dead && (ally.Stats.LastFatalBlowResolution == FatalBlowResolution.InstantDeath || actor.Stats.CurrentActionPoints < 4)) continue;
                if (d < bestDist)
                {
                    bestDist = d;
                    bestAlly = ally;
                    bestDead = dead;
                }
            }
            if (bestAlly == null || _pathfinder == null || _grid == null) return null;

            int careCost = bestDead ? 4 : 3;
            int moveBudget = Mathf.Max(0, actor.Stats.CurrentActionPoints - careCost - GetRequiredDefensiveReserve(actor, TacticalPosture.SupportAndHeal));
            if (moveBudget <= 0) return null;

            return FindPathTowardsTarget(actor, bestAlly, moveBudget);
        }

        private bool HasPanickedAllyInRange(TacticalUnit actor, int range)
        {
            for (int i = 0; i < _cachedUnits.Count; i++)
            {
                var ally = _cachedUnits[i];
                if (ally == null || ally == actor || ally.Stats == null || !ally.Stats.IsAlive) continue;
                if (ally.IsPlayerControlled != actor.IsPlayerControlled) continue;
                if (actor.CurrentCoords.DistanceTo(ally.CurrentCoords) > range) continue;
                var fx = ally.Stats.ActiveStatus;
                if (fx.HasFlag(StatusEffect.Destabilise) || fx.HasFlag(StatusEffect.Etourdi) || fx.HasFlag(StatusEffect.Agonisant))
                    return true;
            }
            return false;
        }

        /// <summary>RD-031 : sécurise les longs déplacements qui rompent le
        // contact (Décrochage payé) au lieu de subir les réactions.</summary>
        private void EnsureSafeDisengageForMove(TacticalUnit actor, int steps)
        {
            if (actor?.Stats == null || steps <= 0) return;
            if (OpportunityState.IsExemptFromProvoking(actor.Stats)) return;
            if (OpportunityState.HasSafeDisengage(actor.Stats)) return;
            if (OpportunityState.IsCancelledByStatus(actor.Stats)) return;

            bool inContact = false;
            for (int i = 0; i < _cachedUnits.Count; i++)
            {
                var u = _cachedUnits[i];
                if (u == null || u == actor || u.Stats == null || !u.Stats.IsAlive) continue;
                bool hostile = actor.IsPlayerControlled ? !u.IsPlayerControlled : u.IsPlayerControlled;
                if (_mode == CombatAIMode.FullAuto && u.IsPlayerControlled != actor.IsPlayerControlled) hostile = true;
                if (!hostile) continue;
                if (TitanFootprint.MinDistanceBetweenUnits(actor.CurrentCoords, actor.FootprintType, u.CurrentCoords, u.FootprintType) <= 1)
                {
                    inContact = true;
                    break;
                }
            }
            if (!inContact) return;

            int disCost = CoreRulesConfig.Instance != null ? CoreRulesConfig.Instance.DisengageAPCost : 1;
            if (actor.Stats.CurrentActionPoints >= disCost + 1
                && OpportunityState.RequestSafeDisengage(actor.Stats, disCost, out _))
            {
                actor.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("💨 Décrochage IA", new Color(0.4f, 0.9f, 1.0f));
            }
        }

        private static bool AIHasShield(TacticalUnit actor)
        {
            var inv = actor?.Sheet?.Inventory;
            if (inv == null) return false;
            for (int i = 0; i < inv.Count; i++)
            {
                var it = inv[i];
                if (it == null || !it.IsEquipped) continue;
                if (!string.IsNullOrEmpty(it.Name) && it.Name.IndexOf("Bouclier", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                if (!string.IsNullOrEmpty(it.Category) && it.Category.IndexOf("Bouclier", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private static InventoryItem AIFindByName(TacticalUnit actor, string fragment)
        {
            var inv = actor?.Sheet?.Inventory;
            if (inv == null || string.IsNullOrEmpty(fragment)) return null;
            for (int i = 0; i < inv.Count; i++)
            {
                var it = inv[i];
                if (it == null || string.IsNullOrEmpty(it.Name)) continue;
                if (it.Name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0) return it;
            }
            return null;
        }

        private static InventoryItem AIFindBestHeal(TacticalUnit actor)
        {
            var inv = actor?.Sheet?.Inventory;
            if (inv == null) return null;
            InventoryItem best = null;
            for (int i = 0; i < inv.Count; i++)
            {
                var it = inv[i];
                if (it == null || it.Type != ItemType.Consumable || it.HealingAmount <= 0) continue;
                if (best == null || it.HealingAmount > best.HealingAmount) best = it;
            }
            return best;
        }

        private bool CanShootNow(TacticalUnit actor)
        {
            if (actor?.Stats == null) return false;
            if (!HasRangedWeapon(actor, out _, out _, out _)) return false;
            var mag = actor.Sheet?.GetEquippedWeapon();
            if (mag != null && mag.AmmoCapacity > 0)
                return !mag.Jammed && mag.AmmoRemaining > 0;
            return ChargeState.CanFireRanged(actor.Stats);
        }

        // =========================================================================
        // SOUTIEN TACTIQUE ALLIÉ (corde, entraves, traînée, radio, maintenance,
        // consommables, garde du corps) — registre joueur, sans la partie soins.
        // =========================================================================
        private bool TryExecuteAllySupport(TacticalUnit actor, TacticalUnitVisual visual)
        {
            if (actor?.Stats == null || !actor.Stats.IsAlive) return false;
            if (_defaultPersonality == AIPersonality.Aggressive) return false;

            for (int i = 0; i < _cachedUnits.Count; i++)
            {
                var ally = _cachedUnits[i];
                if (ally == null || ally == actor || ally.Stats == null) continue;
                if (ally.IsPlayerControlled != actor.IsPlayerControlled) continue;

                int d = TitanFootprint.MinDistanceBetweenUnits(actor.CurrentCoords, actor.FootprintType, ally.CurrentCoords, ally.FootprintType);
                int pa = actor.Stats.CurrentActionPoints;
                var tgtVis = ally.GetComponent<TacticalUnitVisual>();

                // Corde : Relever un allié À Terre (1 PA, contact).
                if (d <= 1 && pa >= 1 && ally.Stats.IsAlive
                    && ally.Stats.ActiveStatus.HasFlag(StatusEffect.ATerre)
                    && SmokeScreen.HasItemByName(actor.Stats, "Corde"))
                {
                    if (!actor.Stats.ConsumeActionPoints(1)) return false;
                    ally.Stats.RemoveStatus(StatusEffect.ATerre);
                    visual?.SpawnFloatingText("🪢 Allié relevé (-1 PA)", Color.green);
                    tgtVis?.SpawnFloatingText("🪢 RELEVÉ", Color.green);
                    _arena?.Log($"🪢 <b>{actor.Stats.Name}</b> relève <b>{ally.Stats.Name}</b> à la corde.");
                    return true;
                }

                // Clé / Crochetage : Libérer un allié entravé (1 PA, contact).
                if (d <= 1 && pa >= 1 && ally.Stats.IsAlive
                    && ally.Stats.ActiveStatus.HasFlag(StatusEffect.Immobilise)
                    && (SmokeScreen.HasItemByName(actor.Stats, "Clé Magnétique") || SmokeScreen.HasItemByName(actor.Stats, "Crochetage")))
                {
                    if (!actor.Stats.ConsumeActionPoints(1)) return false;
                    ally.Stats.RemoveStatus(StatusEffect.Immobilise);
                    visual?.SpawnFloatingText("🔓 Allié libéré (-1 PA)", Color.green);
                    tgtVis?.SpawnFloatingText("🔓 LIBÉRÉ", Color.green);
                    _arena?.Log($"🔓 <b>{actor.Stats.Name}</b> libère <b>{ally.Stats.Name}</b> de ses entraves.");
                    return true;
                }

                // Traînée d'évacuation (2 PA, contact : immobilisé / à terre / inconscient).
                if (d <= 1 && pa >= 2 && _arena != null
                    && (ally.Stats.ActiveStatus.HasFlag(StatusEffect.Immobilise)
                        || ally.Stats.ActiveStatus.HasFlag(StatusEffect.Inconscient)
                        || ally.Stats.ActiveStatus.HasFlag(StatusEffect.ATerre))
                    && _arena.CanDragBody(actor, ally))
                {
                    _arena.ExecuteDragBody(actor, ally);
                    return true;
                }

                // Consommable de soin sur allié au contact (1 PA).
                if (d <= 1 && pa >= 1 && ally.Stats.IsAlive
                    && (float)ally.Stats.CurrentHealth / Mathf.Max(1, ally.Stats.MaxHealth) <= 0.5f)
                {
                    var dose = AIFindBestHeal(actor);
                    if (dose != null)
                    {
                        if (!actor.Stats.ConsumeActionPoints(1)) return false;
                        string usedName = dose.Name;
                        int usedHeal = dose.HealingAmount;
                        bool usedBandage = usedName.IndexOf("Bandage", StringComparison.OrdinalIgnoreCase) >= 0;
                        actor.Sheet?.ConsumeOne(dose.ItemId);
                        actor.NotifyInventoryChanged(true);
                        int healed = ally.Stats.Heal(usedHeal);
                        if (usedBandage) ally.Stats.RemoveStatus(StatusEffect.Saignement);
                        visual?.SpawnFloatingText($"💉 {usedName} (-1 PA)", Color.green);
                        tgtVis?.SpawnFloatingText($"+{healed} PV", Color.green);
                        _arena?.Log($"💉 <b>{actor.Stats.Name}</b> utilise <b>{usedName}</b> sur <b>{ally.Stats.Name}</b> (+{healed} PV).");
                        return true;
                    }
                }

                // Radio : Ralliement à distance (1 PA, ≤12, Déstabilisé/Étourdi).
                if (d <= 12 && pa >= 1 && ally.Stats.IsAlive
                    && (ally.Stats.ActiveStatus.HasFlag(StatusEffect.Destabilise) || ally.Stats.ActiveStatus.HasFlag(StatusEffect.Etourdi))
                    && SmokeScreen.HasItemByName(actor.Stats, "Radio"))
                {
                    if (!actor.Stats.ConsumeActionPoints(1)) return false;
                    ally.Stats.RemoveStatus(StatusEffect.Destabilise);
                    ally.Stats.RemoveStatus(StatusEffect.Etourdi);
                    visual?.SpawnFloatingText("📻 Ralliement (-1 PA)", Color.cyan);
                    tgtVis?.SpawnFloatingText("📻 RALLIÉ", Color.cyan);
                    _arena?.Log($"📻 <b>{actor.Stats.Name}</b> rallie <b>{ally.Stats.Name}</b> par radio.");
                    return true;
                }

                // Boîte à Outils : maintenance sur allié (1 PA, contact).
                if (d <= 1 && pa >= 1 && ally.Stats.IsAlive && _arena != null
                    && SmokeScreen.HasItemByName(actor.Stats, "Boîte à Outils")
                    && (ally.Sheet?.GetEquippedWeapon()?.Jammed == true
                        || (ally.Stats.MaxShieldHP > 0 && ally.Stats.CurrentShieldHP < ally.Stats.MaxShieldHP)))
                {
                    if (!actor.Stats.ConsumeActionPoints(1)) return false;
                    var tgtWeapon = ally.Sheet?.GetEquippedWeapon();
                    if (tgtWeapon != null && tgtWeapon.Jammed)
                    {
                        tgtWeapon.Jammed = false;
                        ally.NotifyInventoryChanged(true);
                        tgtVis?.SpawnFloatingText("🔧 DÉSENRAYÉE", Color.cyan);
                        _arena.Log($"🔧 <b>{actor.Stats.Name}</b> désenraye <b>{tgtWeapon.Name}</b> de <b>{ally.Stats.Name}</b> (-1 PA).");
                    }
                    else
                    {
                        int before = ally.Stats.CurrentShieldHP;
                        ally.Stats.CurrentShieldHP = Mathf.Min(ally.Stats.MaxShieldHP, ally.Stats.CurrentShieldHP + 10);
                        tgtVis?.SpawnFloatingText($"🔧 +{ally.Stats.CurrentShieldHP - before} Bouclier", Color.cyan);
                        _arena.Log($"🔧 <b>{actor.Stats.Name}</b> répare le champ de <b>{ally.Stats.Name}</b> (-1 PA).");
                    }
                    return true;
                }

                // Garde du corps (0 PA, contact) sur allié fragile.
                if (d <= 1 && ally.Stats.IsAlive
                    && !SkillTechniqueState.IsGuardedBy(ally.Stats, actor.Stats)
                    && ((float)ally.Stats.CurrentHealth / Mathf.Max(1, ally.Stats.MaxHealth) <= 0.4f
                        || ally.Stats.ActiveStatus.HasFlag(StatusEffect.Destabilise))
                    && Killtime.Tactics.CombatUI.CombatTechniqueRegistry.CanSetBodyguard(actor, ally))
                {
                    if (Killtime.Tactics.CombatUI.CombatTechniqueRegistry.ExecuteSetBodyguard(actor, ally, _arena))
                        return true;
                }
            }

            return false;
        }

        // =========================================================================
        // ENTRETIEN PERSONNEL (bouclier, soins, dopants, arme, canalisation,
        // furtivité, souffle) — registre joueur, une action par itération.
        // =========================================================================
        private bool TryExecuteSelfUpkeep(TacticalUnit actor, TacticalPosture posture, int dist, bool inAttackRange, TacticalUnitVisual visual)
        {
            if (actor?.Stats == null || !actor.Stats.IsAlive) return false;
            int pa = actor.Stats.CurrentActionPoints;
            int reserve = GetRequiredDefensiveReserve(actor, posture);

            // 0 PA : Seconde Respiration (1x/combat, -2 ESS).
            if (actor.Stats.Essoufflement > 0 && !actor.Stats.HasUsedSecondeRespiration
                && actor.Stats.HasSpecialization("Seconde Respiration"))
            {
                actor.Stats.HasUsedSecondeRespiration = true;
                actor.Stats.RecoverBreath(2);
                visual?.SpawnFloatingText("Seconde Respiration (-2 ESS)", Color.cyan);
                _arena?.Log($"🌬️ <b>{actor.Stats.Name}</b> déclenche sa <b>Seconde Respiration</b> (-2 ESS, 0 PA) !");
                return true;
            }

            // Garde du corps (0 PA) : traitée côté alliés, rappelée ici pour les
            // tours où aucun soin n'est dû mais un allié reste exposé.
            if (TryExecuteAllySupport(actor, visual)) return true;

            // Arme : ramasser au sol ou équiper un remplaçant si désarmé.
            var equipped = actor.Sheet?.GetEquippedWeapon();
            if (equipped == null && pa >= DroppedWeaponPickup.PickupCostPA())
            {
                if (DroppedWeaponPickup.TryPickupNearest(actor, out string pickupMsg))
                {
                    _arena?.Log($"⚔ <b>{actor.Stats.Name}</b> : {pickupMsg}");
                    return true;
                }
                if (actor.Sheet?.Inventory != null)
                {
                    for (int i = 0; i < actor.Sheet.Inventory.Count; i++)
                    {
                        var cand = actor.Sheet.Inventory[i];
                        if (cand == null || cand.Type != ItemType.Weapon || cand.IsEquipped) continue;
                        if (cand.IsThrowableGrenade() || cand.IsLauncher) continue;
                        int cost = DroppedWeaponPickup.PickupCostPA();
                        if (cost > 0 && !actor.Stats.ConsumeActionPoints(cost)) return false;
                        if (actor.Sheet.EquipItem(cand.ItemId))
                        {
                            actor.NotifyInventoryChanged(true);
                            visual?.SpawnFloatingText($"🔄 {cand.Name} équipée", Color.cyan);
                            _arena?.Log($"🔄 <b>{actor.Stats.Name}</b> équipe <b>{cand.Name}</b>.");
                            return true;
                        }
                    }
                }
            }

            if (pa < 1) return false;

            // Bouclier : cellule nytharite via l'arène, sinon boîte/moteur.
            if (actor.Stats.MaxShieldHP > 0 && actor.Stats.CurrentShieldHP < actor.Stats.MaxShieldHP && _arena != null)
            {
                if (SmokeScreen.HasItemByName(actor.Stats, "Cellule Nytharite Standard"))
                {
                    if (_arena.TryRechargeShieldWithCell(actor, false, out string msg)) { _arena.Log(msg); return true; }
                }
                else if (SmokeScreen.HasItemByName(actor.Stats, "Cellule Nytharite Pure"))
                {
                    if (_arena.TryRechargeShieldWithCell(actor, true, out string msg)) { _arena.Log(msg); return true; }
                }
                else if (SmokeScreen.HasItemByName(actor.Stats, "Boîte à Outils"))
                {
                    if (!actor.Stats.ConsumeActionPoints(1)) return false;
                    int before = actor.Stats.CurrentShieldHP;
                    actor.Stats.CurrentShieldHP = Mathf.Min(actor.Stats.MaxShieldHP, actor.Stats.CurrentShieldHP + 10);
                    visual?.SpawnFloatingText($"🔧 +{actor.Stats.CurrentShieldHP - before} Bouclier", Color.cyan);
                    _arena.Log($"🔧 <b>{actor.Stats.Name}</b> répare son champ (-1 PA).");
                    return true;
                }
                else if (SmokeScreen.HasItemByName(actor.Stats, "Moteur"))
                {
                    if (!actor.Stats.ConsumeActionPoints(1)) return false;
                    int before = actor.Stats.CurrentShieldHP;
                    actor.Stats.CurrentShieldHP = Mathf.Min(actor.Stats.MaxShieldHP, actor.Stats.CurrentShieldHP + 10);
                    visual?.SpawnFloatingText($"⚙️ +{actor.Stats.CurrentShieldHP - before} Bouclier", Color.cyan);
                    _arena.Log($"⚙️ <b>{actor.Stats.Name}</b> suralimente son champ (-1 PA).");
                    return true;
                }
            }

            // Soin personnel par consommable (≤50% PV).
            if ((float)actor.Stats.CurrentHealth / Mathf.Max(1, actor.Stats.MaxHealth) <= 0.5f)
            {
                var dose = AIFindBestHeal(actor);
                if (dose != null)
                {
                    if (!actor.Stats.ConsumeActionPoints(1)) return false;
                    string usedName = dose.Name;
                    int usedHeal = dose.HealingAmount;
                    bool usedBandage = usedName.IndexOf("Bandage", StringComparison.OrdinalIgnoreCase) >= 0;
                    actor.Sheet?.ConsumeOne(dose.ItemId);
                    actor.NotifyInventoryChanged(true);
                    int healed = actor.Stats.Heal(usedHeal);
                    if (usedBandage) actor.Stats.RemoveStatus(StatusEffect.Saignement);
                    visual?.SpawnFloatingText($"💉 {usedName} (+{healed} PV)", Color.green);
                    _arena?.Log($"💉 <b>{actor.Stats.Name}</b> utilise <b>{usedName}</b> (+{healed} PV).");
                    return true;
                }
            }

            // Attelle : retire Ralenti (fracture immobilisée).
            if (actor.Stats.ActiveStatus.HasFlag(StatusEffect.Ralenti) && AIFindByName(actor, "Attelle") != null)
            {
                var dose = AIFindByName(actor, "Attelle");
                if (!actor.Stats.ConsumeActionPoints(1)) return false;
                actor.Sheet?.ConsumeOne(dose.ItemId);
                actor.NotifyInventoryChanged(true);
                actor.Stats.RemoveStatus(StatusEffect.Ralenti);
                visual?.SpawnFloatingText("🦴 Fracture immobilisée", Color.green);
                _arena?.Log($"🦴 <b>{actor.Stats.Name}</b> pose une attelle (Ralenti retiré).");
                return true;
            }

            // Antidouleurs : +2 Encaissement, purge Étourdi/Déstabilisé.
            if ((actor.Stats.ActiveStatus.HasFlag(StatusEffect.Etourdi) || actor.Stats.ActiveStatus.HasFlag(StatusEffect.Destabilise))
                && AIFindByName(actor, "Antidouleur") != null)
            {
                var dose = AIFindByName(actor, "Antidouleur");
                if (!actor.Stats.ConsumeActionPoints(1)) return false;
                actor.Sheet?.ConsumeOne(dose.ItemId);
                actor.NotifyInventoryChanged(true);
                actor.Stats.AddEncaissementBonus(2);
                actor.Stats.RemoveStatus(StatusEffect.Etourdi);
                actor.Stats.RemoveStatus(StatusEffect.Destabilise);
                visual?.SpawnFloatingText("💊 Dopé (+2 Encaissement)", new Color(1f, 0.6f, 0.2f));
                _arena?.Log($"💊 <b>{actor.Stats.Name}</b> prend <b>{dose.Name}</b> (+2 Encaissement).");
                return true;
            }

            // Speed : 1 PA → +3 PA quand la réserve est basse.
            if (pa >= 1 && pa < 3 && AIFindByName(actor, "Speed") != null)
            {
                var dose = AIFindByName(actor, "Speed");
                if (!actor.Stats.ConsumeActionPoints(1)) return false;
                actor.Sheet?.ConsumeOne(dose.ItemId);
                actor.NotifyInventoryChanged(true);
                actor.Stats.CurrentActionPoints += 3;
                actor.Stats.ApplyStatus(StatusEffect.Rapide, 3);
                visual?.SpawnFloatingText("💨 SPEED (+3 PA)", Color.yellow);
                _arena?.Log($"💨 <b>{actor.Stats.Name}</b> prend <b>{dose.Name}</b> (+3 PA, Rapide).");
                return true;
            }

            // Harmonisation psi (Résonateur → Survolté +1 EC) avant sort ou tir EC.
            if (!actor.Stats.ActiveStatus.HasFlag(StatusEffect.Survolte)
                && SmokeScreen.HasItemByName(actor.Stats, "Résonateur")
                && (actor.Sheet?.LearnedSpells.Count > 0 || (actor.Sheet?.GetEquippedWeapon()?.AttackBonusEc ?? 0) > 0))
            {
                if (!actor.Stats.ConsumeActionPoints(1)) return false;
                actor.Stats.ApplyStatus(StatusEffect.Survolte, 1);
                visual?.SpawnFloatingText("🔮 SURVOLTÉ (+1 EC)", Color.magenta);
                _arena?.Log($"🔮 <b>{actor.Stats.Name}</b> s'harmonise avec son <b>Résonateur Nytharite</b> (+1 EC).");
                return true;
            }

            // Canaliser le coup (+2 au jet) quand la frappe suit derrière.
            if (inAttackRange && (_defaultPersonality == AIPersonality.Tactician || _defaultPersonality == AIPersonality.Balanced)
                && !ChannelingState.IsChanneling(actor.Stats)
                && !ChannelingState.IsCancelledByStatus(actor.Stats)
                && pa >= CoreRulesConfig.Instance.ChannelStartAPCost + 2 + reserve
                && _arena != null && _arena.TryBeginChanneling(actor, "Coup en progression", out _))
            {
                return true;
            }

            // Furtivité (2 PA, +2 embuscade) pour les doctrines prudentes à distance.
            if (!StealthState.IsStealthed(actor.Stats) && dist > 2
                && (_defaultPersonality == AIPersonality.Survivor || _defaultPersonality == AIPersonality.Tactician)
                && pa >= StealthState.EnterStealthAPCost + 2 + reserve
                && _arena != null && _arena.TryEnterStealth(actor, out _))
            {
                return true;
            }

            // Reprendre son souffle : 1 PA → -1 ESS (Second Souffle : -2).
            int ess = actor.Stats.Essoufflement;
            if (ess > 0 && ess >= Mathf.Max(1, actor.Stats.Attributes.Constitution / 2) && pa >= 2 + reserve)
            {
                if (!actor.Stats.ConsumeActionPoints(1)) return false;
                int recovered = actor.Stats.HasSpecialization("Course d'Endurance : Second Souffle") ? 2 : 1;
                actor.Stats.RecoverBreath(recovered);
                visual?.SpawnFloatingText($"Souffle repris (-{recovered} ESS)", Color.green);
                return true;
            }

            return false;
        }

        // =========================================================================
        // BALISE LASER, CHARGE, FRAPPE TITANESQUE, BOUSCULADE, LANCER D'ARME
        // =========================================================================
        private bool TryExecuteLaserDesignation(TacticalUnit actor, TacticalUnit target, int dist, TacticalUnitVisual visual)
        {
            if (actor?.Stats == null || target?.Stats == null || !target.Stats.IsAlive) return false;
            if (dist < 1 || dist > 12) return false;
            if (!SmokeScreen.HasItemByName(actor.Stats, "Lampe") || !SmokeScreen.HasItemByName(actor.Stats, "Batterie")) return false;
            if (SkillTechniqueState.IsFlawExposed(target.Stats)) return false;
            if (!HasLineOfSight(actor.CurrentCoords, target.CurrentCoords, target.FootprintType, actor.FootprintType, actor)) return false;
            if (target.Stats.EncaissementThreshold < 3 && target.Stats.CurrentHealth < 8) return false;
            if (actor.Stats.CurrentActionPoints < 1 + 2 + GetRequiredDefensiveReserve(actor, TacticalPosture.CalculatedStrike)) return false;
            if (!actor.Stats.ConsumeActionPoints(1)) return false;

            SkillTechniqueState.ApplyExposedFlaw(actor.Stats, target.Stats);
            visual?.SpawnFloatingText("🔦 Faille désignée (-1 PA)", Color.yellow);
            target.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("🔦 DÉSIGNÉ", Color.yellow);
            _arena?.Log($"🔦 <b>{actor.Stats.Name}</b> désigne <b>{target.Stats.Name}</b> à la balise (faille exposée).");
            _arena?.RecordChronoSnapshot($"Balise : {actor.Stats.Name} -> {target.Stats.Name}");
            return true;
        }

        private bool TryExecuteChargeAttack(TacticalUnit actor, TacticalUnit target, TacticalUnitVisual visual)
        {
            if (_arena == null || _arena.IsResolving || _pathfinder == null) return false;
            if (actor?.Stats == null || target?.Stats == null || !target.Stats.IsAlive) return false;
            if (_defaultPersonality == AIPersonality.Survivor) return false;
            if (GrappleState.IsGrappled(actor.Stats)) return false;
            if (!_pathfinder.CanChargeTarget(actor, target, 3, 2)) return false;

            visual?.SpawnFloatingText("⚡ Charge d'assaut !", new Color(1f, 0.85f, 0.2f));
            _arena.ExecuteChargeAttack(actor, target, BodyPart.Torse, 0, 0);
            return true;
        }

        private bool TryExecuteTitanSlam(TacticalUnit actor, TacticalUnit target, TacticalPosture posture, int dist, TacticalUnitVisual visual)
        {
            if (_arena != null && _arena.IsResolving) return false;
            if (actor?.Stats == null || !actor.Stats.IsAlive) return false;
            if (target?.Stats == null || !target.Stats.IsAlive) return false;
            if (!TitanFootprint.IsTitan(actor.FootprintType)) return false;
            int pa = actor.Stats.CurrentActionPoints;
            bool allIn = (posture == TacticalPosture.AllInLethal) || _defaultPersonality == AIPersonality.Aggressive;

            // Saisie colossale au contact (2 PA) : broyage + Immobilisé.
            if (dist <= 1 && pa >= 2
                && !actor.Stats.ActiveStatus.HasFlag(StatusEffect.Immobilise)
                && TitanFootprint.CanTitanGrab(actor.FootprintType, target.FootprintType, dist)
                && (target.Stats.CurrentActionPoints >= 3 || target.Stats.CurrentHealth <= Mathf.Max(3, actor.Stats.Attributes.Force) || allIn))
            {
                if (!actor.Stats.ConsumeActionPoints(2)) return false;
                int grabDmg = Mathf.Max(3, actor.Stats.Attributes.Force);
                if (target.Stats.CurrentHealth - grabDmg <= 0) target.Stats.EvaluateFatalBlow(BodyPart.Torse, grabDmg);
                else target.Stats.CurrentHealth -= grabDmg;

                var tgtVis = target.GetComponent<TacticalUnitVisual>();
                tgtVis?.TriggerHitFlash();
                if (target.Stats.IsAlive)
                {
                    GrappleState.SetGrapple(actor.Stats, target.Stats);
                    target.Stats.ApplyStatus(StatusEffect.Destabilise, 1);
                    tgtVis?.SpawnFloatingText($"-{grabDmg} SAISIE [IMMOBILISÉ]", Color.yellow);
                }
                else
                {
                    tgtVis?.TriggerFallingBackDeath();
                    _arena?.AutoTargetNextAlive();
                }
                visual?.SpawnFloatingText("✊ SAISIE TITANESQUE", Color.cyan);
                _arena?.Log($"✊ <b>{actor.Stats.Name}</b> saisit <b>{target.Stats.Name}</b> (-2 PA) : {grabDmg} dégâts de broyage !");
                _arena?.RecordChronoSnapshot($"Saisie Titanesque : {actor.Stats.Name} -> {target.Stats.Name}");
                return true;
            }

            // Piétinement / Souffle : zones multi-cibles (jamais d'alliés dedans).
            if (pa >= 3)
            {
                // Piétinement (cible à ≤2 cases).
                if (dist <= 2)
                {
                    var zone = TitanFootprint.GetStompZone(target.CurrentCoords, actor.FootprintType);
                    if (CountZoneTargets(actor, zone, out int foes, out int allies) && foes >= (allIn ? 1 : 2) && allies == 0)
                    {
                        ExecuteTitanZoneHit(actor, target, zone, Mathf.Max(4, actor.Stats.Attributes.Force + 2), 3, StatusEffect.ATerre, 1, "💥 PIÉTINEMENT", "Piétinement de Zone", visual);
                        return true;
                    }
                }
                // Souffle (cible à ≤8 cases).
                if (dist <= 8)
                {
                    var zone = TitanFootprint.GetBreathZone(target.CurrentCoords, actor.FootprintType);
                    int breathDmg = Mathf.Max(5, (actor.Stats.Attributes.Magie > 0 ? actor.Stats.Attributes.Magie : actor.Stats.Attributes.Constitution) + 2);
                    if (CountZoneTargets(actor, zone, out int foes, out int allies)
                        && (foes >= 2 || (foes >= 1 && (allIn || target.Stats.CurrentHealth <= breathDmg))) && allies == 0)
                    {
                        ExecuteTitanZoneHit(actor, target, zone, breathDmg, 3, StatusEffect.EnFeu, 2, "🔥 SOUFFLE", "Souffle Titanesque", visual);
                        return true;
                    }
                }
            }

            return false;
        }

        private bool CountZoneTargets(TacticalUnit actor, List<HexCoordinates> zone, out int foes, out int allies)
        {
            foes = 0;
            allies = 0;
            if (zone == null || zone.Count == 0) return false;
            var zoneSet = new HashSet<HexCoordinates>(zone);
            for (int i = 0; i < _cachedUnits.Count; i++)
            {
                var u = _cachedUnits[i];
                if (u == null || u == actor || u.Stats == null || !u.Stats.IsAlive) continue;
                var cells = u.OccupiedCoords;
                bool inZone = false;
                for (int c = 0; c < cells.Count; c++)
                {
                    if (zoneSet.Contains(cells[c]))
                    {
                        inZone = true;
                        break;
                    }
                }
                if (!inZone) continue;
                bool hostile = actor.IsPlayerControlled ? !u.IsPlayerControlled : u.IsPlayerControlled;
                if (_mode == CombatAIMode.FullAuto && u.IsPlayerControlled != actor.IsPlayerControlled) hostile = true;
                if (hostile) foes++;
                else allies++;
            }
            return foes > 0;
        }

        private void ExecuteTitanZoneHit(TacticalUnit actor, TacticalUnit target, List<HexCoordinates> zone, int dmg, int apCost, StatusEffect zoneStatus, int statusTurns, string icon, string label, TacticalUnitVisual visual)
        {
            if (!actor.Stats.ConsumeActionPoints(apCost)) return;
            var zoneSet = new HashSet<HexCoordinates>(zone);
            int hitCount = 0;
            for (int i = 0; i < _cachedUnits.Count; i++)
            {
                var u = _cachedUnits[i];
                if (u == null || u == actor || u.Stats == null || !u.Stats.IsAlive) continue;
                bool inZone = false;
                var cells = u.OccupiedCoords;
                for (int c = 0; c < cells.Count; c++)
                {
                    if (zoneSet.Contains(cells[c]))
                    {
                        inZone = true;
                        break;
                    }
                }
                if (!inZone) continue;

                hitCount++;
                if (u.Stats.CurrentHealth - dmg <= 0) u.Stats.EvaluateFatalBlow(BodyPart.Torse, dmg);
                else u.Stats.CurrentHealth -= dmg;
                u.Stats.ApplyStatus(zoneStatus, statusTurns);
                u.Stats.ApplyStatus(StatusEffect.Destabilise, 1);
                var uVis = u.GetComponent<TacticalUnitVisual>();
                uVis?.TriggerHitFlash();
                uVis?.SpawnFloatingText($"-{dmg} {icon}", Color.red);
                if (!u.Stats.IsAlive)
                {
                    uVis?.TriggerFallingBackDeath();
                    if (u == target) _arena?.AutoTargetNextAlive();
                }
            }
            visual?.SpawnFloatingText($"{icon} {label} ({hitCount})", new Color(1f, 0.45f, 0.1f));
            _arena?.Log($"{icon} <b>{actor.Stats.Name}</b> : <b>{label} ({zone.Count} hex)</b> (-{apCost} PA) — {hitCount} unité(s) ({dmg} dégâts) !");
            _arena?.RecordChronoSnapshot($"{label} : {actor.Stats.Name} ({zone.Count} hex)");
        }

        private bool TryExecuteShove(TacticalUnit actor, TacticalUnit target, CoverType targetCover, TacticalUnitVisual visual)
        {
            if (_arena == null || _arena.IsResolving) return false;
            if (actor?.Stats == null || target?.Stats == null || !target.Stats.IsAlive) return false;
            if (actor.Stats.CurrentActionPoints < 2) return false;
            if (GrappleState.IsGrappled(actor.Stats)) return false;
            if ((target.Stats.ActiveStatus & StatusEffect.ATerre) != 0) return false;
            if (TitanFootprint.GetHexCount(target.FootprintType) > TitanFootprint.GetHexCount(actor.FootprintType) + 6) return false;

            bool wantShove = (targetCover == CoverType.ThreeQuarters)
                || _defaultPersonality == AIPersonality.Survivor;
            if (!wantShove && _defaultPersonality == AIPersonality.Tactician
                && target.Stats.BaseArmorAbsorption + target.Stats.GetWornArmorBonus() >= 5)
            {
                wantShove = true;
            }
            if (!wantShove) return false;

            bool shieldBash = AIHasShield(actor);
            SkillType checkSkill = shieldBash ? SkillType.DefenseCorporelle : SkillType.Athletisme;
            if (!actor.Stats.CanAttack(actor.Stats.GetSkillDie(checkSkill, true))) return false;

            visual?.SpawnFloatingText(shieldBash ? "🛡️ Coup de bouclier !" : "💨 Bousculade !", Color.cyan);
            _arena.ExecuteShove(actor, target, shieldBash, 0, 0);
            return true;
        }

        private bool TryExecuteWeaponThrow(TacticalUnit actor, TacticalUnit target, int dist, TacticalUnitVisual visual)
        {
            if (_arena == null || _arena.IsResolving) return false;
            if (actor?.Stats == null || actor.Sheet == null) return false;
            if (dist < 2 || dist > 4 || actor.Stats.CurrentActionPoints < 2) return false;
            var w = actor.Sheet.GetEquippedWeapon();
            if (w == null || w.IsThrowableGrenade() || w.IsLauncher) return false;
            // Le tir direct reste meilleur quand il est disponible.
            if (CanShootNow(actor)) return false;

            visual?.SpawnFloatingText($"🗡️ Lancer : {w.Name} !", Color.cyan);
            _arena.ExecuteWeaponThrow(actor, target);
            return true;
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
                    int allyDist = TitanFootprint.MinDistanceBetweenUnits(ally.CurrentCoords, ally.FootprintType, target.CurrentCoords, target.FootprintType);
                    if (allyDist < 2 || allyDist > 10) continue;
                    if (GetCoverLevel(ally.CurrentCoords, target.CurrentCoords, target.FootprintType, ally.FootprintType, ally) == CoverType.Full) continue;

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

                // RD-IA09 : distance d'empreinte (les Titans faussaient le filtre).
                int dist = TitanFootprint.MinDistanceBetweenUnits(hex, actor.FootprintType, target.CurrentCoords, target.FootprintType);
                if (isRanged && (dist < 2 || dist > maxRange)) continue;
                if (!isRanged && dist != 1) continue;

                // RD-IA09 : couvert + fumée réels d'empreinte (avant : centre à
                // centre sans fumée, incohérent avec HasLineOfSight).
                CoverType targetCoverFromHex = GetCoverLevel(hex, target.CurrentCoords, target.FootprintType, actor.FootprintType, actor);
                if (targetCoverFromHex == CoverType.Full) continue;

                float score = 50f;

                // Flanking offensif : suppression du couvert adverse
                if (targetCoverFromHex == CoverType.None) score += 45f;
                else if (targetCoverFromHex == CoverType.Half) score += 20f;

                // Couvert défensif : protection contre la riposte
                CoverType defCoverAgainstTarget = GetCoverLevel(target.CurrentCoords, hex, actor.FootprintType, target.FootprintType, actor);
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

        // =========================================================================
        // RD-053 : PATROUILLE ALERTÉE & CONVERGENCE VERS LE BRUIT / LUMIÈRE
        // =========================================================================
        private bool HasAlertedPatrolTarget(TacticalUnit actor, out HexCoordinates suspiciousTarget, out string patrolReason)
        {
            suspiciousTarget = default;
            patrolReason = "Bruit suspect";

            if (actor?.Stats == null || !actor.Stats.IsAlive || actor.Stats.CurrentActionPoints <= 0) return false;
            if (_grid == null || _pathfinder == null) return false;

            int currentRound = _turnManager != null ? _turnManager.CurrentRound : 1;
            bool isNoiseHostile = actor.IsPlayerControlled ? !_squadNoiseFromPlayer : _squadNoiseFromPlayer;

            if (_squadLastHeardNoiseCoords.HasValue && isNoiseHostile && (_squadNoiseAlertRound >= currentRound - 1))
            {
                int noiseDist = actor.CurrentCoords.DistanceTo(_squadLastHeardNoiseCoords.Value);
                if (noiseDist > 1)
                {
                    suspiciousTarget = _squadLastHeardNoiseCoords.Value;
                    patrolReason = "Tir bruyant entendu";
                    return true;
                }
                else
                {
                    _squadLastHeardNoiseCoords = null;
                }
            }

            var fog = Killtime.Tactics.Visibility.FogOfWarManager.Instance;
            if (fog != null)
            {
                for (int i = 0; i < _cachedUnits.Count; i++)
                {
                    var enemy = _cachedUnits[i];
                    if (enemy == null || enemy == actor || enemy.Stats == null || !enemy.Stats.IsAlive) continue;
                    bool hostile = actor.IsPlayerControlled ? !enemy.IsPlayerControlled : enemy.IsPlayerControlled;
                    if (_mode == CombatAIMode.FullAuto && enemy.IsPlayerControlled != actor.IsPlayerControlled) hostile = true;
                    if (!hostile) continue;

                    int d = actor.CurrentCoords.DistanceTo(enemy.CurrentCoords);
                    if (d <= 1) continue;

                    if (fog.IsNoiseActive(enemy.gameObject.name))
                    {
                        var lastPos = fog.GetLastKnownPosition(enemy.gameObject.name);
                        suspiciousTarget = lastPos ?? enemy.CurrentCoords;
                        patrolReason = "Tir bruyant localisé";
                        return true;
                    }

                    if (fog.IsNight && enemy.HasFlashlight)
                    {
                        suspiciousTarget = enemy.CurrentCoords;
                        patrolReason = "Faisceau lumineux repéré";
                        return true;
                    }
                }
            }

            return false;
        }

        private IEnumerator ExecuteAlertedPatrol(TacticalUnit actor, HexCoordinates targetCoords, string reason, TacticalUnitVisual visual)
        {
            int reserve = GetRequiredDefensiveReserve(actor, TacticalPosture.AlertedPatrol);
            int moveBudget = Mathf.Max(1, actor.Stats.CurrentActionPoints - reserve);

            var path = FindPathTowardsCoords(actor.CurrentCoords, targetCoords, moveBudget, out int rawCost);
            if (path != null && path.Count > 1)
            {
                int apCost = actor.ComputeMovementAPCost(rawCost);
                visual?.SpawnFloatingText($"🚨 [PATROUILLE] {reason} (-{apCost} PA)", new Color(1f, 0.55f, 0.1f));
                _arena?.Log($"🚨 <b>{actor.Stats.Name}</b> passe en <b>Patrouille Alertée</b> ({reason}) ➔ convergence vers {targetCoords}.");
                yield return StartCoroutine(actor.MoveAlongPath(path, _grid, apCost));
            }
        }

        private List<HexCoordinates> FindPathTowardsCoords(HexCoordinates start, HexCoordinates destination, int apBudget, out int totalRawCost)
        {
            totalRawCost = 0;
            if (_pathfinder == null || _grid == null || apBudget <= 0) return null;

            HexCoordinates targetNodeCoords = destination;
            var destNode = _grid.GetNode(destination);
            if (destNode == null || !destNode.IsWalkable || destNode.IsOccupied)
            {
                int minDistance = int.MaxValue;
                for (int dir = 0; dir < 6; dir++)
                {
                    var n = destination.GetNeighbor(dir);
                    var nNode = _grid.GetNode(n);
                    if (nNode != null && nNode.IsWalkable && !nNode.IsOccupied)
                    {
                        int d = start.DistanceTo(n);
                        if (d < minDistance)
                        {
                            minDistance = d;
                            targetNodeCoords = n;
                        }
                    }
                }
            }

            var fullPath = _pathfinder.FindPath(start, targetNodeCoords, 50, out _);
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

            totalRawCost = accumulatedCost;
            return truncatedPath.Count > 1 ? truncatedPath : null;
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

        // RD-047 : Fuite panique désordonnée pour unité en Déroute (Agonisant)
        private IEnumerator ExecutePanickedFlee(TacticalUnit actor)
        {
            if (CanTakeBreathSafely(actor) && actor.Stats.CurrentActionPoints < 3)
            {
                actor.Stats.TakeEmergencyBreath(2);
                var visual = actor.GetComponent<TacticalUnitVisual>();
                visual?.SpawnFloatingText("🫁 Souffle de Panique (+2 PA)", Color.red);
                yield return new WaitForSeconds(_actionDelay);
            }

            int ap = actor.Stats.CurrentActionPoints;
            if (ap <= 0 || _pathfinder == null || _grid == null) yield break;

            TacticalUnit nearestThreat = null;
            int minThreatDist = int.MaxValue;
            for (int i = 0; i < _cachedUnits.Count; i++)
            {
                var u = _cachedUnits[i];
                if (u == null || u == actor || u.Stats == null || !u.Stats.IsAlive || u.Stats.IsSurrendered) continue;
                bool hostile = actor.IsPlayerControlled ? !u.IsPlayerControlled : u.IsPlayerControlled;
                if (_mode == CombatAIMode.FullAuto && u.IsPlayerControlled != actor.IsPlayerControlled) hostile = true;
                if (!hostile) continue;
                int d = actor.CurrentCoords.DistanceTo(u.CurrentCoords);
                if (d < minThreatDist)
                {
                    minThreatDist = d;
                    nearestThreat = u;
                }
            }

            HexCoordinates? fleeHex = nearestThreat != null
                ? FindBestPanickedFleeHex(actor.CurrentCoords, nearestThreat.CurrentCoords, ap)
                : null;

            if (fleeHex.HasValue && !fleeHex.Value.Equals(actor.CurrentCoords))
            {
                var path = _pathfinder.FindPath(actor.CurrentCoords, fleeHex.Value, ap, out int cost);
                if (path != null && path.Count > 1)
                {
                    actor.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("💨 Fuite éperdue !", new Color(1f, 0.4f, 0.2f));
                    yield return StartCoroutine(actor.MoveAlongPath(path, _grid, cost));
                }
            }
        }

        private HexCoordinates? FindBestPanickedFleeHex(HexCoordinates current, HexCoordinates threatCoords, int apBudget)
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
                float score = dist * 10f + UnityEngine.Random.Range(0f, 4f);
                CoverType cover = CoverSystem.EvaluateCover(threatCoords, hex, _grid);
                if (cover != CoverType.None) score += 15f;

                if (score > highestScore)
                {
                    highestScore = score;
                    bestHex = hex;
                }
            }

            return bestHex;
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

            // RD-031 : le retrait rompt le contact et provoque (sauf spé esquive).
            // Quand les PA le permettent, sécurise le pas par un Décrochage payé
            // (reste ≥ 1 PA pour marcher après).
            if (actor.Stats != null
                && !OpportunityState.IsExemptFromProvoking(actor.Stats)
                && !OpportunityState.HasSafeDisengage(actor.Stats))
            {
                int disCost = CoreRulesConfig.Instance.DisengageAPCost;
                if (ap >= disCost + 1
                    && OpportunityState.RequestSafeDisengage(actor.Stats, disCost, out _))
                {
                    actor.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("💨 Décrochage IA", new Color(0.4f, 0.9f, 1.0f));
                }
            }

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

        /// <summary>
        /// RD-031 : choisit la posture d'opportunité de l'IA pour ce round.
        /// Grappler (Athlétisme) → Blocage ; rapide (RAP 5+) → Poursuite ;
        /// esquiveur → Balayage ; défaut → Frappe gratuite.
        /// </summary>
        private OpportunityReactionType ChooseOpportunityStance(TacticalUnit unit)
        {
            if (unit == null || unit.Stats == null) return OpportunityReactionType.Strike;
            if (unit.Sheet != null && unit.Sheet.GetSkill(SkillType.Athletisme).TrainingLevel >= 1)
                return OpportunityReactionType.Block;
            if (unit.Stats.Attributes.Rapidite >= 5)
                return OpportunityReactionType.Follow;
            if (unit.Sheet != null && unit.Sheet.GetSkill(SkillType.Esquive).TrainingLevel >= 1)
                return OpportunityReactionType.Trip;
            return OpportunityReactionType.Strike;
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

            // RD-033 : maintenance munitions avant le tir (désenrayer, recharger).
            // Sans coup disponible, l'attaque est annulée (repli mêlée/déplacement de l'appelant).
            if (isRanged && actor != null && actor.Sheet != null)
            {
                var mag = actor.Sheet.GetEquippedWeapon();
                if (mag != null && mag.AmmoCapacity > 0)
                {
                    if (mag.Jammed)
                    {
                        if (_arena != null && _arena.TryClearJam(actor, out string jamMsg))
                        {
                            _arena.Log(jamMsg);
                            yield return new WaitForSeconds(Mathf.Min(0.4f, _actionDelay * 0.3f));
                        }
                        else
                        {
                            _arena?.Log($"⚠️ {actor.Stats.Name} ne peut ni tirer ni désenrayer.");
                            yield break;
                        }
                    }
                    if (mag.AmmoRemaining <= 0)
                    {
                        bool reloaded = false;
                        if (_arena != null)
                        {
                            // Standard de préférence, HD seulement si c'est la seule réserve.
                            if (WeaponAmmo.StockShots(actor.Sheet, mag.AmmoType, false) > 0)
                                reloaded = _arena.TryReloadWeapon(actor, false, out _);
                            else if (mag.HeavyAmmo && WeaponAmmo.StockShots(actor.Sheet, mag.AmmoType, true) > 0)
                                reloaded = _arena.TryReloadWeapon(actor, true, out _);
                        }
                        if (reloaded)
                        {
                            _arena?.Log($"🔋 <b>{actor.Stats.Name}</b> recharge <b>{mag.Name}</b> [{mag.AmmoRemaining}/{mag.AmmoCapacity}].");
                            yield return new WaitForSeconds(Mathf.Min(0.4f, _actionDelay * 0.3f));
                        }
                        else
                        {
                            _arena?.Log($"⚠️ {actor.Stats.Name} : chargeur vide, aucune réserve ({mag.AmmoType}).");
                            yield break;
                        }
                    }
                }
            }

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
            if (equippedWeapon != null && equippedWeapon.BaseDamage > 0) weaponDmg = equippedWeapon.BaseDamage + (equippedWeapon.LoadedHD ? 1 : 0);

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

            // RD-IA07 : couvert réel d'empreinte + fumée (avant : Single/single).
            CoverType targetCover = isRanged ? GetCoverLevel(actor.CurrentCoords, target.CurrentCoords, target.FootprintType, actor.FootprintType, actor) : CoverType.None;
            int coverPen = CoverSystem.AttackPenalty(targetCover);

            int nightPen = 0;
            var fowManager = Killtime.Tactics.Visibility.FogOfWarManager.Instance;
            if (isRanged && fowManager != null && fowManager.IsNight && !CombatCalculator.HasLampOrThermal(actor.Stats))
            {
                nightPen = -1;
            }

            // RD-IA07 : modificateurs de duel aveugle ignorés par l'estimation :
            // furtivité (+2, sans réaction), canalisation (+2), bonus arme EC,
            // hauteur RD-036 et canon entravé au contact (-2).
            int stealthBonus = StealthState.IsStealthed(actor.Stats) ? StealthState.StealthAttackBonus : 0;
            int channelBonus = ChannelingState.IsChanneling(actor.Stats) ? ChannelingState.PeekAttackBonus(actor.Stats) : 0;
            int weaponEcBonus = 0;
            int elevationMod = 0;
            var equippedForRuling = actor.Sheet?.GetEquippedWeapon();
            if (equippedForRuling != null) weaponEcBonus = Mathf.Max(0, equippedForRuling.AttackBonusEc);
            try
            {
                if (_arena != null)
                {
                    var ruling = _arena.GetElevationRuling(actor, target, attackSkill, equippedForRuling, !isRanged);
                    elevationMod = ruling.AttackMod;
                }
            }
            catch { /* estimation optionnelle */ }
            int entravePen = 0;
            try
            {
                if (actor.IsCanonEntrave() && (attackSkill == SkillType.Ballistique || attackSkill == SkillType.ProjectilesTir))
                    entravePen = -2;
            }
            catch { /* ignore */ }

            int weaponDmg = 5;
            var equippedWeapon = actor.Sheet?.GetEquippedWeapon();
            if (equippedWeapon != null && equippedWeapon.BaseDamage > 0) weaponDmg = equippedWeapon.BaseDamage + (equippedWeapon.LoadedHD ? 1 : 0);

            int targetArmor = target.Stats.BaseArmorAbsorption + target.Stats.GetWornArmorBonus();
            int targetEncaissement = target.Stats.EncaissementThreshold;
            int targetHp = target.Stats.CurrentHealth;

            int availableAP = actor.Stats.CurrentActionPoints;
            int reserveNeeded = GetRequiredDefensiveReserve(actor, posture);
            int spendablePE = actor.Stats.GetSpendablePE();

            double baseAttExpected = CombatCalculator.DieAverage(attackDie) + attStatusMod + coverPen + nightPen
                + stealthBonus + channelBonus + weaponEcBonus + elevationMod + entravePen;
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

                    // Injection de PE si létalité accessible ou provocation de choc.
                    // RD-IA07 : 1 PE vaut DuelPEBonusPerPoint (config), pas +1 fixe.
                    if (spendablePE > 0 && posture == TacticalPosture.AllInLethal)
                    {
                        int peValue = CoreRulesConfig.Instance != null ? CoreRulesConfig.Instance.DuelPEBonusPerPoint : 1;
                        double testDiff = (currentAttRoll + chosenBonusAP + peValue) - baseDefExpected;
                        if (testDiff > 0 && (weaponDmg + testDiff - targetArmor) >= targetHp)
                        {
                            chosenBonusPE = 1;
                        }
                    }

                    double finalDiff = (currentAttRoll + chosenBonusAP + chosenBonusPE * (CoreRulesConfig.Instance != null ? CoreRulesConfig.Instance.DuelPEBonusPerPoint : 1)) - baseDefExpected;
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
        private bool TryCastBestSpell(TacticalUnit actor, TacticalUnit target, int dist, bool hasLOS)
        {
            if (actor.Sheet == null || actor.Sheet.LearnedSpells == null || actor.Sheet.LearnedSpells.Count == 0) return false;
            if (target == null || target.Stats == null || !target.Stats.IsAlive) return false;
            // RD-IA06 : sans ligne de vue, aucun sort ciblé (avant : cast à
            // l'aveugle à travers les murs).
            if (!hasLOS) return false;

            // RD-IA06 : coût réel = PA du sort + taxe focus (+2 sans focus).
            int focusTax = Killtime.Core.Arcanotech.ArcanotechWorkshop.FocusTax(actor.Sheet);
            NythariteSpell bestSpell = null;
            int bestNetScore = int.MinValue;

            for (int i = 0; i < actor.Sheet.LearnedSpells.Count; i++)
            {
                var spell = actor.Sheet.LearnedSpells[i];
                if (spell == null) continue;
                int realCost = spell.ActionPointCost + focusTax;
                if (spell.RangeInTiles < dist || actor.Stats.CurrentActionPoints < realCost) continue;
                // Score : dégâts nets estimés après armure, au prorata du coût.
                int targetArmor = target.Stats.BaseArmorAbsorption + target.Stats.GetWornArmorBonus();
                int net = spell.BaseArcaneDamage - targetArmor;
                int score = net * 10 - realCost * 3;
                if (target.Stats.CurrentHealth <= System.Math.Max(1, net)) score += 60;
                if (score > bestNetScore)
                {
                    bestNetScore = score;
                    bestSpell = spell;
                }
            }

            if (bestSpell != null && bestNetScore > 0)
            {
                var dice = new DiceRoller();
                var vis = target.GetComponent<TacticalUnitVisual>();
                var actorVis = actor.GetComponent<TacticalUnitVisual>();
                actorVis?.SpawnFloatingText($"🔮 {bestSpell.Name} (-{bestSpell.ActionPointCost + focusTax} PA)", Color.cyan);

                if (bestSpell.Cast(actor.Stats, target.Stats, dice, out _, dist))
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

                        // Brouillard symétrique (§25.4) : pas de grenade sur un ennemi
                        // invisible à 360° (sauf révélé au bruit / Observation).
                        if (_grid != null && actor.Stats != null && enemy.Stats != null)
                        {
                            var gfog = Killtime.Tactics.Visibility.FogOfWarManager.Instance;
                            int grange = gfog != null ? gfog.CurrentSightRange
                                : Killtime.Tactics.Visibility.FogOfWarSystem.LongSightRange;
                            var gvp = Killtime.Tactics.Visibility.FogOfWarSystem.ResolveObserverParams(actor.Stats, grange);
                            float gyaw = Killtime.Tactics.Visibility.FogOfWarManager.FacingYawDeg(actor);
                            bool gseen = Killtime.Tactics.Visibility.FogOfWarSystem.IsCellVisible(
                                actor.CurrentCoords, gyaw, enemy.CurrentCoords,
                                c => _grid.GetNode(c), gvp, _grid.HexRadius);
                            bool gestealthed = Killtime.Core.Combat.StealthState.IsStealthed(enemy.Stats);
                            if (!gseen || (gestealthed && dThrow > 1 && !gvp.Thermal))
                            {
                                bool grevealed = gfog != null
                                    && (gfog.IsNoiseActive(enemy.gameObject.name)
                                        || gfog.IsManuallyRevealed(enemy.gameObject.name));
                                if (!grevealed) continue;
                            }
                        }
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

            // RD-IA10 : anneau d'approche autour de TOUTE l'empreinte (les
            // voisins du centre d'un Titan sont à l'intérieur de son corps).
            HexCoordinates bestNeighbor = target.CurrentCoords;
            int minDistance = int.MaxValue;
            var targetCells = target.OccupiedCoords;
            var candidates = new HashSet<HexCoordinates>();
            for (int c = 0; c < targetCells.Count; c++)
            {
                for (int dir = 0; dir < 6; dir++)
                    candidates.Add(targetCells[c].GetNeighbor(dir));
            }
            foreach (var n in candidates)
            {
                if (target.Occupies(n)) continue;
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

            if (TitanFootprint.MinDistanceBetweenUnits(actor.CurrentCoords, actor.FootprintType, target.CurrentCoords, target.FootprintType) <= 1) return null;

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

            int curDist = TitanFootprint.MinDistanceBetweenUnits(actor.CurrentCoords, actor.FootprintType, target.CurrentCoords, target.FootprintType);
            if (curDist <= maxRange && HasLineOfSight(actor.CurrentCoords, target.CurrentCoords, target.FootprintType, actor.FootprintType, actor)) return null;

            // RD-IA10 : anneau d'approche autour de toute l'empreinte cible.
            HexCoordinates bestNeighbor = target.CurrentCoords;
            int minDistance = int.MaxValue;
            var targetCells = target.OccupiedCoords;
            var candidates = new HashSet<HexCoordinates>();
            for (int c = 0; c < targetCells.Count; c++)
            {
                for (int dir = 0; dir < 6; dir++)
                    candidates.Add(targetCells[c].GetNeighbor(dir));
            }
            foreach (var n in candidates)
            {
                if (target.Occupies(n)) continue;
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
                int d = TitanFootprint.MinDistanceBetweenUnits(fullPath[i], actor.FootprintType, target.CurrentCoords, target.FootprintType);
                if (d <= maxRange && HasLineOfSight(fullPath[i], target.CurrentCoords, target.FootprintType, actor.FootprintType, actor))
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

            // RD-IA08 : suppression du repli "rang Balistique > mêlée +2 ⇒ tir
            // à 8 cases" : sans arme à distance équipée, l'unité ne tire pas
            // (l'ancien code faisait "tirer" des PNJ désarmés en duel
            // Ballistique fantôme). Le contact / les sorts prennent le relais.
            return false;
        }

        private bool HasLineOfSight(HexCoordinates from, HexCoordinates to, TitanFootprintType toFootprint = TitanFootprintType.Single, TitanFootprintType fromFootprint = TitanFootprintType.Single, TacticalUnit viewer = null)
        {
            if (_grid == null) return true;
            // RD-IA08 : CdV unifiée avec le couvert (inclut la fumée RD-041).
            // Avant : EvaluateCover direct ⇒ l'IA "voyait" à travers la fumée
            // pour attaquer mais pas pour évaluer le couvert (incohérent).
            return GetCoverLevel(from, to, toFootprint, fromFootprint, viewer) != CoverType.Full;
        }

        private CoverType GetCoverLevel(HexCoordinates from, HexCoordinates to, TitanFootprintType toFootprint = TitanFootprintType.Single, TitanFootprintType fromFootprint = TitanFootprintType.Single, TacticalUnit viewer = null)
        {
            if (_grid == null) return CoverType.None;
            CoverType baseCover = CoverSystem.EvaluateCover(from, to, _grid, toFootprint, fromFootprint);
            // RD-041 : la fumée coupe aussi la visée de l'IA (sauf thermique).
            if (baseCover != CoverType.Full && _arena != null
                && Killtime.Core.Combat.SmokeScreen.SegmentSmoked(from, to, _arena.SmokeZones)
                && (viewer == null || !CombatDevArena.SeesThroughSmoke(viewer)))
            {
                return CoverType.Full;
            }
            return baseCover;
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

            // RD-039 : En lutte active (victime ou grappler) → priorité absolue au partenaire de lutte.
            if (GrappleState.TryGetGrappler(actor.Stats, out var holdingGrappler) && holdingGrappler != null)
            {
                for (int i = 0; i < _cachedUnits.Count; i++)
                {
                    var gUnit = _cachedUnits[i];
                    if (gUnit != null && gUnit.Stats == holdingGrappler && gUnit.Stats.IsAlive)
                    {
                        return gUnit;
                    }
                }
            }
            if (GrappleState.TryGetGrappledVictim(actor.Stats, out var heldVictim) && heldVictim != null)
            {
                for (int i = 0; i < _cachedUnits.Count; i++)
                {
                    var vUnit = _cachedUnits[i];
                    if (vUnit != null && vUnit.Stats == heldVictim && vUnit.Stats.IsAlive)
                    {
                        return vUnit;
                    }
                }
            }

            for (int i = 0; i < _cachedUnits.Count; i++)
            {
                var potential = _cachedUnits[i];
                if (potential == null || potential == actor || potential.Stats == null || !potential.Stats.IsAlive || potential.Stats.IsSurrendered) continue;

                bool isHostile = actor.IsPlayerControlled ? !potential.IsPlayerControlled : potential.IsPlayerControlled;
                if (_mode == CombatAIMode.FullAuto && potential.IsPlayerControlled != actor.IsPlayerControlled) isHostile = true;
                if (!isHostile) continue;

                // Brouillard de guerre symétrique (§25.4) : l'IA ne cible que ce que
                // l'acteur voit réellement à 360° (portée d'ambiance + murs
                // Full), sauf cible révélée au bruit (tirs) ou par Observation.
                // Le contact (dist <= 1) reste toujours visible des deux côtés.
                if (_grid != null && actor.Stats != null && potential.Stats != null)
                {
                    var fog = Killtime.Tactics.Visibility.FogOfWarManager.Instance;
                    bool isNight = fog != null && fog.IsNight;
                    int range = fog != null ? fog.CurrentSightRange
                        : Killtime.Tactics.Visibility.FogOfWarSystem.LongSightRange;
                    var vp = Killtime.Tactics.Visibility.FogOfWarSystem.ResolveObserverParams(actor.Stats, range, isNight);
                    float actorYaw = Killtime.Tactics.Visibility.FogOfWarManager.FacingYawDeg(actor);
                    bool seen = Killtime.Tactics.Visibility.FogOfWarSystem.IsCellVisible(
                        actor.CurrentCoords, actorYaw, potential.CurrentCoords,
                        c => _grid.GetNode(c), vp, _grid.HexRadius);

                    // RD-053 : Lampe dans la nuit — la lumière trahit la position même au-delà de la portée nocturne
                    if (!seen && isNight && potential.HasFlashlight && HasLineOfSight(actor.CurrentCoords, potential.CurrentCoords, potential.FootprintType, actor.FootprintType, actor))
                    {
                        seen = true;
                    }

                    bool stealthed = Killtime.Core.Combat.StealthState.IsStealthed(potential.Stats);
                    // RD-IA09 : distance d'empreinte (contact Titan = 1, pas 3).
                    int targetDist = TitanFootprint.MinDistanceBetweenUnits(actor.CurrentCoords, actor.FootprintType, potential.CurrentCoords, potential.FootprintType);
                    if (!seen || (stealthed && targetDist > 1 && !vp.Thermal))
                    {
                        bool revealed = fog != null
                            && (fog.IsNoiseActive(potential.gameObject.name)
                                || fog.IsManuallyRevealed(potential.gameObject.name));
                        if (!revealed) continue;
                    }
                }

                int dist = TitanFootprint.MinDistanceBetweenUnits(actor.CurrentCoords, actor.FootprintType, potential.CurrentCoords, potential.FootprintType);
                float curHp = potential.Stats.CurrentHealth;

                float score = 80f;

                if (isRanged)
                {
                    bool inRangedZone = (dist <= maxRange);
                    bool hasLOS = HasLineOfSight(actor.CurrentCoords, potential.CurrentCoords, potential.FootprintType, actor.FootprintType, actor);

                    if (inRangedZone && hasLOS)
                    {
                        score += 50f;
                        if (dist >= 2 && dist <= maxRange) score += 15f;
                        CoverType targetCover = GetCoverLevel(actor.CurrentCoords, potential.CurrentCoords, potential.FootprintType, actor.FootprintType, actor);
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

                // RD-053 : Nuit / Lampe / Bruit attire l'IA
                var fogInstance = Killtime.Tactics.Visibility.FogOfWarManager.Instance;
                if (fogInstance != null)
                {
                    if (fogInstance.IsNight && potential.HasFlashlight) score += 25f;
                    if (fogInstance.IsNoiseActive(potential.gameObject.name)) score += 20f;
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
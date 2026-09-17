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
        Balanced,   // Équilibre attaque soutenue, réserve minimale (1 PA) et survie
        Aggressive, // Dépense 100% de ses PA, injection maximale, aucune réserve
        Tactician,  // Privilégie la visée des membres, 1 PA de réserve, sorts et hit-and-run
        Survivor    // Repli si PV critiques, sinon attrition
    }

    public enum TacticalPosture
    {
        AllInLethal,      // Opportunité d'élimination : dépense et injection maximales
        CalculatedStrike, // Attaque engagée avec injection des PA excédentaires
        HitAndRun,        // Frappe puis décrochage d'une case
        TacticalRetreat   // Danger mortel : repli vers la distance maximale
    }

    /// <summary>
    /// Contrôleur d'IA Tactique Militaire.
    /// Exploite activement l'économie des PA en sachant qu'ils sont réinitialisés à chaque tour.
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
        [SerializeField] [Range(0.15f, 0.4f)] private float _retreatHealthRatio = 0.25f;
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
                catch { /* prefs optionnelles */ }

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
                catch { /* ignore */ }

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
                catch { /* ignore */ }
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
                catch { /* ignore */ }
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
                catch { /* ignore */ }
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
                catch { /* ignore */ }
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
            catch { /* ignore */ }

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

        private void Awake()
        {
            EnsureDependencies();
            try { LoadDevUIPrefs(); } catch { /* prefs optionnelles */ }
        }

        private void Start()
        {
            EnsureDependencies();
            try { LoadDevUIPrefs(); } catch { /* prefs optionnelles */ }
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

        private void HandleCombatEnded(CombatOutcome outcome) => StopAITurn();

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
        // BOUCLE DÉCISIONNELLE STRATÉGIQUE (AI BRAIN)
        // =========================================================================
        private IEnumerator ExecuteAITurn(TacticalUnit unit)
        {
            yield return new WaitForSeconds(_actionDelay);

            if (_pathfinder == null && _grid != null) _pathfinder = new HexPathfinder(_grid);
            UpdateUnitCache();

            var visual = unit.GetComponent<TacticalUnitVisual>();
            int loopGuard = 0;
            const int maxSteps = 8;

            while (unit.Stats.IsAlive && loopGuard++ < maxSteps && unit.Stats.CurrentActionPoints > 0)
            {
                while (Killtime.UI.CombatHUD.IsPaused)
                {
                    yield return null;
                }

                var target = EvaluateBestTarget(unit);
                if (target == null) break;

                int dist = unit.CurrentCoords.DistanceTo(target.CurrentCoords);
                var posture = EvaluateTacticalPosture(unit, target, dist);

                bool isRanged = HasRangedWeapon(unit, out int maxRange, out int minRange, out SkillType attackSkill);
                bool hasLOS = HasLineOfSight(unit.CurrentCoords, target.CurrentCoords);
                bool inAttackRange = isRanged ? (dist <= maxRange && hasLOS) : (dist == 1);

                // -------------------------------------------------------------
                // 1. RETRAITE CRITIQUE (Danger mortel)
                // -------------------------------------------------------------
                if (posture == TacticalPosture.TacticalRetreat)
                {
                    visual?.SpawnFloatingText("[REPLI] Manoeuvre d'Urgence", new Color(1.0f, 0.3f, 0.3f));
                    yield return StartCoroutine(ExecuteRetreat(unit, target));
                    yield return new WaitForSeconds(_actionDelay);
                    break;
                }

                // -------------------------------------------------------------
                // 2. CONTACT DIRECT (Distance == 1) : Kick Martial ou Dégagement
                // -------------------------------------------------------------
                if (isRanged && dist == 1)
                {
                    bool hasMartialArts = unit.Stats.HasSpecialization("Arts Martiaux");
                    DiceType unarmedDie = unit.Stats.GetSkillDie(SkillType.MainsNues, true);

                    if (hasMartialArts && unit.Stats.CanAttack(unarmedDie) && unit.Stats.CurrentActionPoints >= 2)
                    {
                        visual?.SpawnFloatingText("🥋 [TEEP] Frappe Martiale de Rupture", Color.cyan);
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
                        visual?.SpawnFloatingText("[DÉGAGEMENT] Recul Tactique", new Color(0.2f, 0.85f, 1.0f));
                        yield return StartCoroutine(ExecuteDisengageStep(unit, target));
                        yield return new WaitForSeconds(_actionDelay);

                        dist = unit.CurrentCoords.DistanceTo(target.CurrentCoords);
                        hasLOS = HasLineOfSight(unit.CurrentCoords, target.CurrentCoords);
                        inAttackRange = (dist <= maxRange && hasLOS);
                    }
                }

                // -------------------------------------------------------------
                // 3. SORTS ARCANOTECH À DISTANCE (Livre IV)
                // -------------------------------------------------------------
                if (!isRanged && dist > 1 && TryCastBestSpell(unit, target))
                {
                    yield return new WaitForSeconds(_actionDelay);
                    continue;
                }

                // -------------------------------------------------------------
                // 3b. GRENADES DE ZONE (Livre VIII §31.3) : cluster >= 2 hostiles,
                // jamais de suicide (lanceur exclu si souffle ami), 1 lancer / tour.
                // -------------------------------------------------------------
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

                // -------------------------------------------------------------
                // 4. ATTAQUE (Portée Balistique ou Contact Melee)
                // -------------------------------------------------------------
                if (inAttackRange)
                {
                    DiceType attackDie = unit.Stats.GetSkillDie(attackSkill, true);

                    if (unit.Stats.CanAttack(attackDie) && unit.Stats.CurrentActionPoints >= 2)
                    {
                        yield return StartCoroutine(ExecuteTacticalAttack(unit, target, posture, isRanged, attackSkill));

                        if (posture == TacticalPosture.HitAndRun && unit.Stats.CurrentActionPoints >= 1)
                        {
                            visual?.SpawnFloatingText("[RETRAIT] Décrochage", new Color(0.9f, 0.7f, 0.2f));
                            yield return StartCoroutine(ExecuteDisengageStep(unit, target));
                            break;
                        }

                        yield return new WaitForSeconds(_actionDelay);
                        continue;
                    }
                    else if (unit.Stats.CanAttack(attackDie) && unit.Stats.CurrentActionPoints < 2 && CanTakeBreathSafely(unit))
                    {
                        unit.Stats.TakeEmergencyBreath(2);
                        visual?.SpawnFloatingText("🫁 Souffle d'Attaque (+2 PA)", Color.yellow);
                        yield return new WaitForSeconds(_actionDelay);
                        continue;
                    }

                    break;
                }

                // -------------------------------------------------------------
                // 5. APPROCHE VERS LA LIGNE DE TIR OU CONTACT
                // -------------------------------------------------------------
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
                        string moveMsg = isRanged ? $"Mise en position de tir (-{steps} PA)" : $"Avance tactique (-{steps} PA)";
                        visual?.SpawnFloatingText(moveMsg, new Color(0.2f, 0.85f, 1.0f));
                        yield return StartCoroutine(unit.MoveAlongPath(pathToFiringPos, _grid, steps));
                        yield return new WaitForSeconds(_actionDelay);
                        continue;
                    }
                }

                break;
            }

            yield return new WaitForSeconds(Mathf.Min(0.4f, _actionDelay * 0.25f));
            _activeTurnRoutine = null;
            _turnManager?.EndCurrentTurn();
        }

        // =========================================================================
        // ÉVALUATION DE LA POSTURE TACTIQUE
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

            if ((_defaultPersonality == AIPersonality.Tactician || _defaultPersonality == AIPersonality.Survivor)
                && dist == 1 && actor.Stats.CurrentActionPoints >= 4)
            {
                return TacticalPosture.HitAndRun;
            }

            return TacticalPosture.CalculatedStrike;
        }

        private int GetRequiredDefensiveReserve(TacticalUnit actor, TacticalPosture posture)
        {
            if (posture == TacticalPosture.AllInLethal) return 0;
            if (_defaultPersonality == AIPersonality.Aggressive) return 0;

            // 1 PA suffit pour réagir (BaseReactionAPCost). Tout garder au-delà est un gâchis net.
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
                if (dist <= maxRange && HasLineOfSight(actor.CurrentCoords, target.CurrentCoords))
                {
                    apToReach = 0;
                }
                else
                {
                    apToReach = Mathf.Max(0, dist - maxRange);
                }
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

            int estimatedDmg = Mathf.Max(4, (weaponDmg + 2 - target.Stats.BaseArmorAbsorption) * 2);

            return targetHp <= estimatedDmg;
        }

        private bool CanTakeBreathSafely(TacticalUnit unit)
        {
            return unit.Stats.Essoufflement < unit.Stats.Attributes.Constitution;
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

                if (bestSpell.Cast(actor.Stats, target.Stats, dice, out string log))
                {
                    vis?.TriggerHitFlash();
                    vis?.SpawnFloatingText($"-{bestSpell.BaseArcaneDamage} Arcanique", Color.magenta);
                }

                return true;
            }

            return false;
        }

        // =========================================================================
        // GRENADES DE ZONE (LIVRE VIII §31.3)
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
                // L'utilitaire (flash/fumi) ne vaut le coup qu'en posture calculée + cible groupée.
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
                        // Jamais de suicide : le lanceur reste hors de son propre souffle.
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
                        float score = foes * (10f + expected) - allies * 22f + (isUtility ? -6f : 0f);
                        // Bonus opportuniste : cible affaiblie dans la zone.
                        if (enemy.Stats.CurrentHealth <= expected) score += 8f;
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

            // Seuil : au moins 2 hostiles (ou 1 + létal), jamais pour un seul éclaireur isolé.
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
        // RETRAITE STRATÉGIQUE & DÉCROCHAGE
        // =========================================================================
        private IEnumerator ExecuteRetreat(TacticalUnit actor, TacticalUnit nearestThreat)
        {
            if (CanTakeBreathSafely(actor) && actor.Stats.CurrentActionPoints < 3)
            {
                actor.Stats.TakeEmergencyBreath(2);
                var visual = actor.GetComponent<TacticalUnitVisual>();
                visual?.SpawnFloatingText("🫁 Sprint de Survie (+2 PA)", Color.red);
                yield return new WaitForSeconds(_actionDelay);
            }

            int ap = actor.Stats.CurrentActionPoints;
            var retreatHex = FindBestRetreatHex(actor.CurrentCoords, nearestThreat.CurrentCoords, ap);

            if (retreatHex.HasValue && !retreatHex.Value.Equals(actor.CurrentCoords))
            {
                var path = _pathfinder.FindPath(actor.CurrentCoords, retreatHex.Value, ap, out int cost);
                if (path != null && path.Count > 1)
                {
                    yield return StartCoroutine(actor.MoveAlongPath(path, _grid, cost));
                }
            }
        }

        private IEnumerator ExecuteDisengageStep(TacticalUnit actor, TacticalUnit threat)
        {
            int ap = actor.Stats.CurrentActionPoints;
            if (ap <= 0) yield break;

            var retreatHex = FindBestRetreatHex(actor.CurrentCoords, threat.CurrentCoords, 1);
            if (retreatHex.HasValue && !retreatHex.Value.Equals(actor.CurrentCoords))
            {
                var path = _pathfinder.FindPath(actor.CurrentCoords, retreatHex.Value, 1, out int cost);
                if (path != null && path.Count > 1)
                {
                    yield return StartCoroutine(actor.MoveAlongPath(path, _grid, cost));
                }
            }
        }

        private HexCoordinates? FindBestRetreatHex(HexCoordinates current, HexCoordinates threatCoords, int apBudget)
        {
            HexCoordinates? bestHex = null;
            float maxThreatDistance = current.DistanceTo(threatCoords);

            for (int dir = 0; dir < 6; dir++)
            {
                var step = current.GetNeighbor(dir);
                var node = _grid.GetNode(step);
                if (node == null || !node.IsWalkable || node.IsOccupied) continue;

                int dist = step.DistanceTo(threatCoords);
                if (dist > maxThreatDistance)
                {
                    var path = _pathfinder.FindPath(current, step, apBudget, out int cost);
                    if (path != null && path.Count > 0 && cost <= apBudget)
                    {
                        maxThreatDistance = dist;
                        bestHex = step;
                    }
                }
            }

            return bestHex;
        }

        // =========================================================================
        // ATTAQUE TACTIQUE & INJECTION DES PA (LIVRE VI)
        // =========================================================================
        private IEnumerator ExecuteTacticalAttack(TacticalUnit actor, TacticalUnit target, TacticalPosture posture, bool isRanged, SkillType attackSkill)
        {
            _arena?.SelectTarget(target);

            int availableAP = actor.Stats.CurrentActionPoints;
            int reserveNeeded = GetRequiredDefensiveReserve(actor, posture);

            DiceType attackDie = actor.Stats.GetSkillDie(attackSkill, true);
            int maxAttacks = actor.Stats.GetMaxAttacksAllowed(attackDie);
            bool canAttackAgainLater = (actor.Stats.AttacksThisTurn + 1 < maxAttacks) && (availableAP >= 4 + reserveNeeded);

            BodyPart chosenPart = BodyPart.Torse;
            bool cancelPenalty = false;

            if (isRanged)
            {
                if (posture == TacticalPosture.AllInLethal && availableAP >= 3)
                {
                    chosenPart = BodyPart.Tete;
                    cancelPenalty = true;
                }
                else if (target.CurrentCoords.DistanceTo(actor.CurrentCoords) <= 3 && availableAP >= 3)
                {
                    chosenPart = BodyPart.Jambes;
                    cancelPenalty = true;
                }
                else if (availableAP >= 3)
                {
                    chosenPart = BodyPart.Torse;
                    cancelPenalty = true;
                }
            }
            else
            {
                if (posture == TacticalPosture.AllInLethal && availableAP >= 3)
                {
                    chosenPart = BodyPart.Tete;
                    cancelPenalty = true;
                }
                else if (_defaultPersonality == AIPersonality.Tactician && availableAP >= 3 && target.Stats.CurrentActionPoints > 2)
                {
                    chosenPart = BodyPart.BrasDroit;
                    cancelPenalty = true;
                }
            }

            // Séquence officielle Livre VI §24.1 : le bonus PA est une intention post-tirage
            // (appliquée après le jet d'attaque, +1 / PA). L'IA réserve ici un budget max,
            // débité seulement après tirage dans la limite des PA restants.
            int baseCost = cancelPenalty ? 3 : 2;
            int apAfterBase = availableAP - baseCost;

            int bonusAP = 0;
            if (canAttackAgainLater)
            {
                int spendableForBonus = apAfterBase - 2 - reserveNeeded;
                bonusAP = Mathf.Clamp(spendableForBonus, 0, 3);
            }
            else
            {
                int spendableForBonus = apAfterBase - reserveNeeded;
                bonusAP = Mathf.Clamp(spendableForBonus, 0, 4);
            }

            var visual = actor.GetComponent<TacticalUnitVisual>();
            string partLabel = chosenPart == BodyPart.Tete ? "[VISÉE] Tête" :
                               chosenPart == BodyPart.BrasDroit ? "[VISÉE] Désarmement" :
                               chosenPart == BodyPart.Jambes ? "[VISÉE] Jambes" : "[VISÉE] Torse";
            if (isRanged) partLabel = $"🎯 Tir {partLabel}";
            if (bonusAP > 0) partLabel += $" (+{bonusAP} PA Inj.)";

            while (Killtime.UI.CombatHUD.IsPaused)
            {
                yield return null;
            }

            visual?.SpawnFloatingText(partLabel, chosenPart == BodyPart.Tete ? Color.red : Color.cyan);
            yield return new WaitForSeconds(Mathf.Min(0.5f, _actionDelay * 0.35f));

            SkillType defenseSkill = target.Stats.Attributes.Agilite >= target.Stats.Attributes.Force
                ? SkillType.Esquive
                : SkillType.DefenseCorporelle;

            _arena?.ExecuteAttack(
                targetedPart: chosenPart,
                cancelPenaltyWithAP: cancelPenalty,
                attackSkill: attackSkill,
                defenseSkill: defenseSkill,
                defenderWantsToDefend: true,
                attackerBonusAP: bonusAP
            );

            if (_arena != null)
            {
                float waitElapsed = 0f;
                while (_arena.IsResolving && waitElapsed < 6f)
                {
                    yield return null;
                    waitElapsed += Time.deltaTime;
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

        // =========================================================================
        // APPROCHE FLUIDE PAR TRONÇONNAGE DU CHEMIN
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

            if (actor.CurrentCoords.DistanceTo(target.CurrentCoords) <= 1)
            {
                return null;
            }

            // Tracé global sans restriction de budget immédiat (capé à 50)
            var fullPath = _pathfinder.FindPath(actor.CurrentCoords, bestNeighbor, 50, out _);
            if (fullPath == null || fullPath.Count <= 1)
            {
                return null;
            }

            // Tronçonnage à la limite stricte du budget de marche alloué ce tour
            var truncatedPath = new List<HexCoordinates> { fullPath[0] };
            int accumulatedCost = 0;

            for (int i = 1; i < fullPath.Count; i++)
            {
                var step = fullPath[i];
                var stepNode = _grid.GetNode(step);
                int stepCost = stepNode != null ? stepNode.ActionPointCost : 1;

                if (accumulatedCost + stepCost > apBudget)
                {
                    break;
                }

                if (stepNode != null && (stepNode.IsOccupied || !stepNode.IsWalkable))
                {
                    break;
                }

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
            int dist = from.DistanceTo(to);
            if (dist <= 1) return true;

            for (int i = 1; i < dist; i++)
            {
                float t = (float)i / dist;
                float q = Mathf.Lerp(from.Q, to.Q, t);
                float r = Mathf.Lerp(from.R, to.R, t);
                float s = -q - r;

                int rq = Mathf.RoundToInt(q);
                int rr = Mathf.RoundToInt(r);
                int rs = Mathf.RoundToInt(s);

                float qDiff = Mathf.Abs(rq - q);
                float rDiff = Mathf.Abs(rr - r);
                float sDiff = Mathf.Abs(rs - s);

                if (qDiff > rDiff && qDiff > sDiff)
                    rq = -rr - rs;
                else if (rDiff > sDiff)
                    rr = -rq - rs;

                var node = _grid.GetNode(new HexCoordinates(rq, rr));
                if (node != null && node.Cover == CoverType.Full)
                {
                    return false;
                }
            }

            return true;
        }

        private List<HexCoordinates> FindPathTowardsRangedPosition(TacticalUnit actor, TacticalUnit target, int maxRange, int apBudget)
        {
            if (_pathfinder == null || _grid == null || apBudget <= 0) return null;

            int curDist = actor.CurrentCoords.DistanceTo(target.CurrentCoords);
            if (curDist <= maxRange && HasLineOfSight(actor.CurrentCoords, target.CurrentCoords))
            {
                return null;
            }

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
            if (fullPath == null || fullPath.Count <= 1)
            {
                return null;
            }

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

                if (accumulatedCost + stepCost > apBudget)
                {
                    break;
                }

                if (stepNode != null && (stepNode.IsOccupied || !stepNode.IsWalkable))
                {
                    break;
                }

                accumulatedCost += stepCost;
                truncatedPath.Add(step);
            }

            return truncatedPath.Count > 1 ? truncatedPath : null;
        }

        private TacticalUnit EvaluateBestTarget(TacticalUnit actor)
        {
            TacticalUnit bestTarget = null;
            float highestScore = float.MinValue;
            bool isRanged = HasRangedWeapon(actor, out int maxRange, out _, out _);

            for (int i = 0; i < _cachedUnits.Count; i++)
            {
                var potential = _cachedUnits[i];
                if (potential == null || potential == actor || !potential.Stats.IsAlive) continue;

                bool isHostile = actor.IsPlayerControlled ? (!potential.IsPlayerControlled) : potential.IsPlayerControlled;
                if (_mode == CombatAIMode.FullAuto && potential.IsPlayerControlled != actor.IsPlayerControlled) isHostile = true;
                if (!isHostile) continue;

                int dist = actor.CurrentCoords.DistanceTo(potential.CurrentCoords);
                float curHp = potential.Stats.CurrentHealth;

                float score = 70f;

                if (isRanged)
                {
                    bool inRangedZone = (dist <= maxRange);
                    bool hasLOS = HasLineOfSight(actor.CurrentCoords, potential.CurrentCoords);

                    if (inRangedZone && hasLOS)
                    {
                        score += 50f;
                        if (dist >= 2 && dist <= maxRange) score += 15f;
                    }
                    else if (inRangedZone && !hasLOS)
                    {
                        score += 15f;
                    }
                    else
                    {
                        score -= (dist - maxRange) * 5f;
                    }
                }
                else
                {
                    score -= (dist * 7f);
                    if (dist == 1) score += 40f;
                }

                score += (1.0f - (curHp / Mathf.Max(1, potential.Stats.MaxHealth))) * 35f;
                if (curHp <= 6) score += 45f;

                if (score > highestScore)
                {
                    highestScore = score;
                    bestTarget = potential;
                }
            }

            return bestTarget;
        }

        private void UpdateUnitCache()
        {
            _cachedUnits.Clear();
            _cachedUnits.AddRange(FindObjectsByType<TacticalUnit>());
        }
    }
}
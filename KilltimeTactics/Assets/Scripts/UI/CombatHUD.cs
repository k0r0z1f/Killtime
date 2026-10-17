// Facade du HUD tactique (split en partiels : voir CombatHUD.*.cs).
// API publique inchangee : Instance, IsPaused, Telemetry, EnsureInstance,
// AddCombatLog/AddAdvancedLog, TogglePause/SetPause, events d'attaque/grenade.
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using Killtime.Core.Combat;
using Killtime.Core.Character;
using Killtime.Core.Dice;
using Killtime.Audio;
using Killtime.Tactics;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Tactics.Objectives;

namespace Killtime.UI
{

    public partial class CombatHUD : MonoBehaviour
    {
        public static CombatHUD Instance { get; private set; }
        public static bool IsPaused { get; private set; } = false;

        [Header("Systèmes")]
        [SerializeField] private TurnManager _turnManager;
        [SerializeField] private CombatDevArena _arena;

        public static bool IsPointerOverChat()
        {
            if (Instance == null) return false;
            if (!Instance._isLogDrawerExpanded) return false;

            float drawerWidth = Mathf.Clamp(Screen.width * 0.44f, 480f, 660f);
            float drawerHeight = Mathf.Clamp(Screen.height * 0.35f, 220f, 320f);
            float margin = 20f;
            Rect terminal = new Rect(Screen.width - drawerWidth - margin,
                Screen.height - drawerHeight - margin, drawerWidth, drawerHeight);

            Vector2 mouseGui = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            return terminal.Contains(mouseGui);
        }

        /// <summary>
        /// Teste si la souris survole un panel HUD interactif (cartes, ribbon, matrice VATS,
        /// terminal/feed, pause, modales). Les logs éphémères (display only) ne bloquent pas.
        /// Rects synchronisés avec Draw*() — toute modification des Draw doit mettre à jour ici.
        /// Enregistré comme ExtraBlocker dans FloatingWindowChrome pour bloquer clics/zoom 3D.
        /// </summary>
        public static bool IsPointerOverHUD(Vector2 mouseGui)
        {
            if (Instance == null || !Instance.isActiveAndEnabled) return false;

            float pauseW = 80f;
            float margin = 20f;
            Rect pauseRect = new Rect(Screen.width - pauseW - margin, 22f, pauseW, 24f);

            // Pastille Pause (toujours visible en haut à droite)
            if (pauseRect.Contains(mouseGui))
                return true;

            bool paused = IsPaused;
            bool combatOver = Instance._turnManager != null && Instance._turnManager.IsCombatOver;

            if (paused)
            {
                // Modale de stase (centre)
                if (new Rect((Screen.width - 390f) * 0.5f, (Screen.height - 178f) * 0.46f, 390f, 178f).Contains(mouseGui))
                    return true;
                // Terminal étendu ou bouton FEED
                if (Instance._isLogDrawerExpanded)
                {
                    float dw = Mathf.Clamp(Screen.width * 0.44f, 480f, 660f);
                    float dh = Mathf.Clamp(Screen.height * 0.35f, 220f, 320f);
                    if (new Rect(Screen.width - dw - 20f, Screen.height - dh - 20f, dw, dh).Contains(mouseGui))
                        return true;
                }
                else
                {
                    if (new Rect(Screen.width - 92f - 20f, Screen.height - 30f - 20f, 92f, 22f).Contains(mouseGui))
                        return true;
                }
                return false;
            }

            if (combatOver)
            {
                if (Instance._hideOutcomeCardForReview)
                {
                    Rect reviewPill = new Rect((Screen.width - 190f) * 0.5f, 22f, 190f, 26f);
                    if (reviewPill.Contains(mouseGui))
                        return true;
                }
                else
                {
                    float ow = Mathf.Clamp(Screen.width * 0.46f, 520f, 620f);
                    bool isPlayerClient = TurnManager.IsMultiplayerPlayerClient();
                    float oh = isPlayerClient ? 285f : 335f;
                    if (new Rect((Screen.width - ow) * 0.5f, (Screen.height - oh) * 0.44f, ow, oh).Contains(mouseGui))
                        return true;
                }
                if (Instance._isLogDrawerExpanded)
                {
                    float dw = Mathf.Clamp(Screen.width * 0.44f, 480f, 660f);
                    float dh = Mathf.Clamp(Screen.height * 0.35f, 220f, 320f);
                    if (new Rect(Screen.width - dw - 20f, Screen.height - dh - 20f, dw, dh).Contains(mouseGui))
                        return true;
                }
                else
                {
                    if (new Rect(Screen.width - 92f - 20f, Screen.height - 30f - 20f, 92f, 22f).Contains(mouseGui))
                        return true;
                }
                return false;
            }

            var activeUnit = Instance._turnManager != null ? Instance._turnManager.ActiveUnit : (Instance._arena != null ? Instance._arena.PlayerUnit : null);
            if (activeUnit == null || activeUnit.Stats == null) return false;

            // Menu contextuel spatialisé sur l'unité active
            if (Instance._showActiveUnitContextMenu
                && Instance._activeUnitContextMenuRect.width > 0f
                && Instance._activeUnitContextMenuRect.height > 0f
                && Instance._activeUnitContextMenuRect.Contains(mouseGui))
                return true;

            // Carte joueur (haut-gauche)
            float pw = Mathf.Clamp(Screen.width * 0.27f, 250f, 340f);
            if (new Rect(24f, 22f, pw, 62f).Contains(mouseGui))
                return true;

            // Carte cible (haut-droite, positionnée à gauche de la pause sans chevauchement)
            var target = Instance._arena != null ? Instance._arena.CurrentTarget : null;
            if (target != null && target.Stats != null && target.Stats.IsAlive)
            {
                float tw = Mathf.Clamp(Screen.width * 0.20f, 200f, 280f);
                float tx = pauseRect.x - 12f - tw;
                if (new Rect(tx, 22f, tw, 56f).Contains(mouseGui))
                    return true;
            }

            // Matrice de ciblage VATS (si ouverte ou en animation)
            if (Instance._showAnatomyDrawer || Instance._anatomyOpenAnim > 0.02f)
            {
                float w = Mathf.Clamp(Screen.width * 0.34f, 330f, 460f);
                float h = 216f;
                float y = Screen.height - h - 24f;
                if (new Rect((Screen.width - w) * 0.5f, y, w, h).Contains(mouseGui))
                    return true;
            }

            // Panneau grenades (si ouvert ou en animation)
            if (Instance._showGrenadeDrawer || Instance._grenadeOpenAnim > 0.02f)
            {
                float w = Mathf.Clamp(Screen.width * 0.38f, 360f, 500f);
                float h = 264f;
                float y = Screen.height - h - 24f;
                if (new Rect((Screen.width - w) * 0.5f, y, w, h).Contains(mouseGui))
                    return true;
            }

            // Terminal étendu ou bouton FEED (bas-droite)
            if (Instance._isLogDrawerExpanded)
            {
                float dw = Mathf.Clamp(Screen.width * 0.44f, 480f, 660f);
                float dh = Mathf.Clamp(Screen.height * 0.35f, 220f, 320f);
                if (new Rect(Screen.width - dw - 20f, Screen.height - dh - 20f, dw, dh).Contains(mouseGui))
                    return true;
            }
            else
            {
                if (new Rect(Screen.width - 92f - 20f, Screen.height - 30f - 20f, 92f, 22f).Contains(mouseGui))
                    return true;
            }

            return false;
        }

        public static CombatTelemetry Telemetry { get; } = new();

        public static void RecordCombatAction(string attacker, string defender, bool isAttackerPlayer, bool isDefenderPlayer,
            bool isHit, bool isCrit, bool isBlocked, int rawDmg, int armorAbsorbed, int finalDmg,
            bool causedShock, bool isFatal, int apCost = 0)
        {
            Telemetry.RecordAttack(attacker, defender, isAttackerPlayer, isDefenderPlayer, isHit, isCrit, isBlocked,
                rawDmg, armorAbsorbed, finalDmg, causedShock, isFatal, apCost);
        }

        public static void ResetTelemetry()
        {
            Telemetry.Reset();
        }

        // Animation / micro-interactions
        private float _hudPulse;
        private float _lastLogCount;
        private float _logFlash;

        public event Action<BodyPart, bool> OnAttackRequested;
        public event Action OnEndTurnRequested;
        public event Action<string, bool, bool, int> OnGrenadeRequested;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInitialize()
        {
            EnsureInstance();
        }

        public static CombatHUD EnsureInstance()
        {
            if (Instance != null) return Instance;
            var existing = FindAnyObjectByType<CombatHUD>();
            if (existing != null)
            {
                Instance = existing;
                return Instance;
            }
            var go = new GameObject("[UI] MinimalistTacticalHUD");
            Instance = go.AddComponent<CombatHUD>();
            return Instance;
        }

        private Func<Rect> _playerCardZone;
        private Func<Rect> _targetCardZone;
        private Func<Rect> _pausePillZone;
        private Func<Rect> _bottomFeedZone;

        private void Awake()
        {
            if (Instance == null) Instance = this;
            EnsureReferences();
            InitProceduralTextures();
            FloatingWindowChrome.RegisterExtraBlocker(IsPointerOverHUD);
            RegisterHudExclusionZones();
        }

        private void OnEnable()
        {
            FloatingWindowChrome.RegisterExtraBlocker(IsPointerOverHUD);
            RegisterHudExclusionZones();
        }

        private void OnDisable()
        {
            FloatingWindowChrome.UnregisterExtraBlocker(IsPointerOverHUD);
            UnregisterHudExclusionZones();
        }

        private void RegisterHudExclusionZones()
        {
            _playerCardZone ??= () => new Rect(24f, 16f, Mathf.Clamp(Screen.width * 0.27f, 250f, 340f), 72f);
            _pausePillZone ??= () => new Rect(Screen.width - 80f - 20f, 16f, 80f, 32f);
            _targetCardZone ??= () =>
            {
                if (_arena == null || _arena.CurrentTarget == null || _arena.CurrentTarget.Stats == null || !_arena.CurrentTarget.Stats.IsAlive)
                    return Rect.zero;

                float tw = Mathf.Clamp(Screen.width * 0.20f, 200f, 280f);
                float tx = Screen.width - 80f - 20f - 12f - tw;
                return new Rect(tx, 16f, tw, 66f);
            };
            _bottomFeedZone ??= () => _isLogDrawerExpanded
                ? new Rect(Screen.width - Mathf.Clamp(Screen.width * 0.44f, 480f, 660f) - 20f, Screen.height - Mathf.Clamp(Screen.height * 0.35f, 220f, 320f) - 20f, Mathf.Clamp(Screen.width * 0.44f, 480f, 660f), Mathf.Clamp(Screen.height * 0.35f, 220f, 320f))
                : new Rect(Screen.width - 92f - 20f, Screen.height - 30f - 20f, 92f, 26f);

            FloatingWindowChrome.RegisterExclusionZone(_playerCardZone);
            FloatingWindowChrome.RegisterExclusionZone(_targetCardZone);
            FloatingWindowChrome.RegisterExclusionZone(_pausePillZone);
            FloatingWindowChrome.RegisterExclusionZone(_bottomFeedZone);
        }

        private void UnregisterHudExclusionZones()
        {
            if (_playerCardZone != null) FloatingWindowChrome.UnregisterExclusionZone(_playerCardZone);
            if (_targetCardZone != null) FloatingWindowChrome.UnregisterExclusionZone(_targetCardZone);
            if (_pausePillZone != null) FloatingWindowChrome.UnregisterExclusionZone(_pausePillZone);
            if (_bottomFeedZone != null) FloatingWindowChrome.UnregisterExclusionZone(_bottomFeedZone);
        }

        private void Start()
        {
            BindEventListeners();
        }

        private void EnsureReferences()
        {
            if (_turnManager == null) _turnManager = FindAnyObjectByType<TurnManager>();
            if (_arena == null) _arena = FindAnyObjectByType<CombatDevArena>();
        }

        private void BindEventListeners()
        {
            EnsureReferences();

            if (_arena != null)
            {
                _arena.OnCombatLogMessage -= HandleArenaLogMessage;
                _arena.OnCombatLogMessage += HandleArenaLogMessage;
            }

            if (_turnManager != null)
            {
                _turnManager.OnRoundStarted -= HandleRoundStarted;
                _turnManager.OnRoundStarted += HandleRoundStarted;

                _turnManager.OnTurnStarted -= HandleTurnStarted;
                _turnManager.OnTurnStarted += HandleTurnStarted;

                _turnManager.OnCombatEnded -= HandleCombatEnded;
                _turnManager.OnCombatEnded += HandleCombatEnded;
            }
        }

        private void UnbindEventListeners()
        {
            if (_arena != null)
            {
                _arena.OnCombatLogMessage -= HandleArenaLogMessage;
            }

            if (_turnManager != null)
            {
                _turnManager.OnRoundStarted -= HandleRoundStarted;
                _turnManager.OnTurnStarted -= HandleTurnStarted;
                _turnManager.OnCombatEnded -= HandleCombatEnded;
            }
        }

        private void HandleArenaLogMessage(string message) => AddAdvancedLog(message);

        private void HandleRoundStarted(int roundNumber)
        {
            if (roundNumber <= 1)
            {
                Telemetry.Reset();
                _hideOutcomeCardForReview = false;
            }
            _displayedRoundNumber = roundNumber;
            _roundBannerTimer = RoundBannerDuration;
            if (KilltimeAudioManager.Instance != null)
            {
                KilltimeAudioManager.Instance.Play(SoundId.Round_Start, 0.85f);
            }
            AddAdvancedLog($"⏳ <b>CYCLE DE PHASE : ROUND {roundNumber:00}</b>", LogCategory.MovementAndTurns, "[PHASE]", ColorCyanAccent);
        }

        private void HandleTurnStarted(TacticalUnit unit)
        {
            Color teamCol = unit.IsPlayerControlled ? ColorCyanAccent : ColorCrimson;
            AddAdvancedLog($"Engagement : <b>{unit.Stats.Name}</b> (PA: {unit.Stats.CurrentActionPoints}/{unit.Stats.MaxActionPoints})", LogCategory.MovementAndTurns, "[INIT]", teamCol);
            _showActiveUnitContextMenu = unit != null && unit.IsPlayerControlled && (_turnManager != null && !_turnManager.IsInExploration);
        }

        private void HandleCombatEnded(CombatOutcome outcome)
        {
            _showActiveUnitContextMenu = false;
            _activeUnitContextMenuRect = Rect.zero;
            string outcomeStr = outcome == CombatOutcome.Victory ? "🏆 ENGAGEMENT REMPORTÉ" : "💀 SIGNAUX VITAUX ROMPUS";
            Color col = outcome == CombatOutcome.Victory ? Color.green : ColorCrimson;
            AddAdvancedLog(outcomeStr, LogCategory.Combat, "[RÉSOLUTION]", col);
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.P))
            {
                TogglePause();
            }

            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))
            {
                _showAnatomyDrawer = !_showAnatomyDrawer;
                if (_showAnatomyDrawer) _showGrenadeDrawer = false;
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayUI(_showAnatomyDrawer ? SoundId.VATS_Open : SoundId.VATS_Close, 0.7f);
            }

            if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2))
            {
                _showGrenadeDrawer = !_showGrenadeDrawer;
                if (_showGrenadeDrawer) _showAnatomyDrawer = false;
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayUI(_showGrenadeDrawer ? SoundId.VATS_Open : SoundId.VATS_Close, 0.7f);
            }

            if (Input.GetKeyDown(KeyCode.L))
            {
                _isLogDrawerExpanded = !_isLogDrawerExpanded;
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayUI(SoundId.UI_Toggle, 0.6f);
            }

            if (Input.GetKeyDown(KeyCode.N))
            {
                TacticalUnitVisual.ShowOverheadHUD = !TacticalUnitVisual.ShowOverheadHUD;
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayUI(SoundId.UI_Toggle, 0.6f);
            }

            if (Input.GetKeyDown(KeyCode.Space))
            {
                if (_turnManager != null && !_turnManager.IsInExploration)
                {
                    TriggerEndTurn();
                }
            }

            float dt = Time.unscaledDeltaTime;
            _hudPulse += dt;
            _anatomyOpenAnim = Mathf.MoveTowards(_anatomyOpenAnim, _showAnatomyDrawer ? 1f : 0f, dt * 10f);
            _grenadeOpenAnim = Mathf.MoveTowards(_grenadeOpenAnim, _showGrenadeDrawer ? 1f : 0f, dt * 10f);
            _logFlash = Mathf.MoveTowards(_logFlash, 0f, dt * 2.5f);
            if (_roundBannerTimer > 0f)
            {
                _roundBannerTimer -= dt;
            }
            if (_lastLogCount != _logEntries.Count)
            {
                _lastLogCount = _logEntries.Count;
                _logFlash = 1f;
            }

            UpdateDynamicGauges();
        }

        private void OnDestroy()
        {
            FloatingWindowChrome.UnregisterExtraBlocker(IsPointerOverHUD);
            UnregisterHudExclusionZones();
            UnbindEventListeners();
            if (IsPaused)
            {
                Time.timeScale = _prePauseTimeScale > 0.001f ? _prePauseTimeScale : 1.0f;
                AudioListener.pause = false;
                IsPaused = false;
            }
        }

        private void OnGUI()
        {
            InitProceduralTextures();
            EnsureStyles();

            // 1. Bouton Pause ultra-discret en haut à droite
            DrawTopRightPausePill();

            if (IsPaused)
            {
                DrawStasisModal();
                DrawAdvancedLogSystem(isPauseActive: true);
                return;
            }

            bool inStoryScene = Killtime.Story.StorySceneManager.Instance != null
                && Killtime.Story.StorySceneManager.Instance.CurrentSceneController != null;

            if (_turnManager != null && _turnManager.IsCombatOver && !inStoryScene)
            {
                DrawOutcomeCard();
                DrawAdvancedLogSystem(isPauseActive: true);
                return;
            }

            // Flux d'informations toujours actif (forme réduite [FEED] ou tiroir étendu)
            DrawAdvancedLogSystem(isPauseActive: false);

            var activeUnit = _turnManager != null ? _turnManager.ActiveUnit : (_arena != null ? _arena.PlayerUnit : null);
            if (activeUnit == null || activeUnit.Stats == null) return;

            // 2. HUD minimaliste : Joueur & Cible
            DrawTacticalPlayerCard(activeUnit);
            DrawTacticalTargetCard();

            // 3. Matrice de ciblage anatomique (VATS épuré)
            if (_showAnatomyDrawer)
            {
                DrawSurgicalTargetingMatrix(activeUnit);
            }

            // 4. Panneau grenades (visée de case, lanceur, souffle)
            if (_showGrenadeDrawer)
            {
                DrawGrenadeTargetingPanel(activeUnit);
            }

            // 5. Cue central d'avertissement de début de round
            DrawRoundStartBanner();

            // 6. Suivi des objectifs tactiques (RD-050)
            DrawObjectivesTracker();
        }

    }
}

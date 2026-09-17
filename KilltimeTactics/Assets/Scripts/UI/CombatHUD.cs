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
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;

namespace Killtime.UI
{
    public enum LogCategory
    {
        All,
        Combat,
        ReactionAndAP,
        VitalityAndTrauma,
        MovementAndTurns
    }

    public class CombatLogEntry
    {
        public string Timestamp;
        public string RawMessage;
        public LogCategory Category;
        public string HeaderTag;
        public Color TagColor;
        public float CreatedRealtime;
    }

    public class CombatHUD : MonoBehaviour
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

        // --- Couleurs de la Palette Tactique (Dark Cybernetic) ---
        private static readonly Color ColorBgBase = new Color(0.03f, 0.05f, 0.08f, 0.72f);
        private static readonly Color ColorBgHover = new Color(0.05f, 0.09f, 0.14f, 0.85f);
        private static readonly Color ColorCyanAccent = new Color(0.0f, 0.90f, 1.0f, 0.85f);
        private static readonly Color ColorCyanDim = new Color(0.0f, 0.90f, 1.0f, 0.25f);
        private static readonly Color ColorAmber = new Color(1.0f, 0.72f, 0.15f, 0.95f);
        private static readonly Color ColorCrimson = new Color(1.0f, 0.22f, 0.32f, 0.95f);
        private static readonly Color ColorGhostDamage = new Color(1.0f, 0.4f, 0.2f, 0.75f);
        private static readonly Color ColorTextBright = new Color(0.94f, 0.97f, 1.0f, 0.98f);
        private static readonly Color ColorTextMuted = new Color(0.52f, 0.64f, 0.75f, 0.80f);

        public class CombatTelemetry
        {
            public int TotalAttacks;
            public int SuccessfulHits;
            public int CriticalHits;
            public int DeflectedOrParried;
            public int DamageDealtByAllies;
            public int DamageTakenByAllies;
            public int ArmorAbsorbedByAllies;
            public int ArmorAbsorbedByEnemies;
            public int TraumaShocks;
            public int EnemiesNeutralized;
            public int AlliesDown;
            public string TopDamageDealer = "—";
            public int TopDamageAmount = 0;
            private readonly Dictionary<string, int> _damageByUnit = new(StringComparer.OrdinalIgnoreCase);

            public void Reset()
            {
                TotalAttacks = 0;
                SuccessfulHits = 0;
                CriticalHits = 0;
                DeflectedOrParried = 0;
                DamageDealtByAllies = 0;
                DamageTakenByAllies = 0;
                ArmorAbsorbedByAllies = 0;
                ArmorAbsorbedByEnemies = 0;
                TraumaShocks = 0;
                EnemiesNeutralized = 0;
                AlliesDown = 0;
                TopDamageDealer = "—";
                TopDamageAmount = 0;
                _damageByUnit.Clear();
            }

            public void RecordAttack(string attacker, string defender, bool isAttackerPlayer, bool isDefenderPlayer,
                bool isHit, bool isCrit, bool isBlocked, int rawDmg, int armorAbsorbed, int finalDmg,
                bool causedShock, bool isFatal, int apCost)
            {
                TotalAttacks++;
                if (isHit)
                {
                    SuccessfulHits++;
                    if (isCrit) CriticalHits++;
                    if (causedShock) TraumaShocks++;

                    if (isAttackerPlayer)
                    {
                        DamageDealtByAllies += finalDmg;
                        ArmorAbsorbedByEnemies += armorAbsorbed;
                        if (!string.IsNullOrEmpty(attacker))
                        {
                            int cur = _damageByUnit.TryGetValue(attacker, out int d) ? d : 0;
                            cur += finalDmg;
                            _damageByUnit[attacker] = cur;
                            if (cur > TopDamageAmount)
                            {
                                TopDamageAmount = cur;
                                TopDamageDealer = attacker;
                            }
                        }
                    }
                    if (isDefenderPlayer)
                    {
                        DamageTakenByAllies += finalDmg;
                        ArmorAbsorbedByAllies += armorAbsorbed;
                    }

                    if (isFatal)
                    {
                        if (isDefenderPlayer) AlliesDown++;
                        else EnemiesNeutralized++;
                    }
                }
                else if (isBlocked)
                {
                    DeflectedOrParried++;
                }
            }
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

        private bool _hideOutcomeCardForReview = false;
        private readonly List<CombatLogEntry> _logEntries = new();
        private Vector2 _logScroll;
        private LogCategory _activeCategory = LogCategory.All;
        private bool _isLogDrawerExpanded = false;
        private bool _scrollLock = false;
        // Demande de retour en bas reportée au prochain Draw (où la hauteur exacte
        // du contenu est connue). Évite le float.MaxValue fragile et garantit que
        // le verrou (_scrollLock) n'est jamais contourné : quand il est actif,
        // seul un clamp de sécurité s'applique, jamais un retour en bas.
        private bool _scrollToBottomPending = false;

        // --- Tooltip des dés du feed : 1 s de survol sur une mention (D6, D12…)
        // --- => formule de calcul concise du niveau de dé.
        private const float DiceTooltipDelay = 1.0f;
        private readonly Dictionary<string, DiceLineLayout> _diceLayoutCache = new();
        private readonly List<DiceHitRect> _diceHitRects = new();
        private string _diceHoverKey;
        private float _diceHoverStart;
        private DiceHitRect _diceHoverHit;
        private bool _diceHoverValid;

        private BodyPart _selectedBodyPart = BodyPart.Torse;
        private bool _cancelPenaltyWithAP = false;
        private bool _showAnatomyDrawer = false;
        private float _prePauseTimeScale = 1.0f;

        // --- Grenades (Livre VIII §31.3) : viser la case de la cible verrouillée,
        // --- lancer main (2 PA / 8 cases) ou lanceur (3 PA / 20 cases), visée +1 PA.
        private bool _showGrenadeDrawer = false;
        private float _grenadeOpenAnim;
        private string _selectedGrenadeId;
        private bool _grenadeUseLauncher = true;
        private bool _grenadeAimed = false;
        private int _grenadeBonusPA;

        // Gauges dynamiques lissées
        private float _smoothPlayerHealth = -1f;
        private float _ghostPlayerHealth = -1f;
        private float _smoothPlayerAP = -1f;
        private float _smoothTargetHealth = -1f;

        // Animation / micro-interactions
        private float _hudPulse;
        private float _anatomyOpenAnim;
        private float _lastLogCount;
        private float _logFlash;

        // Textures procédurales
        private static Texture2D _pixelTex;
        private static Texture2D _gradientTex;

        // Styles mis en cache
        private static GUIStyle _hudNameStyle;
        private static GUIStyle _hudSubStyle;
        private static GUIStyle _hudStatStyle;
        private static GUIStyle _btnFlatNormal;
        private static GUIStyle _floatingLogStyle;
        private static GUIStyle _terminalHeaderStyle;
        private static GUIStyle _terminalBodyStyle;
        private static GUIStyle _terminalDiceStyle;

        public event Action<BodyPart, bool> OnAttackRequested;
        public event Action OnEndTurnRequested;
        public event Action<string, bool, bool, int> OnGrenadeRequested;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInitialize()
        {
            if (Instance == null && FindAnyObjectByType<CombatHUD>() == null)
            {
                var go = new GameObject("[UI] MinimalistTacticalHUD");
                go.AddComponent<CombatHUD>();
            }
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
            AddAdvancedLog($"⏳ <b>CYCLE DE PHASE : ROUND {roundNumber:00}</b>", LogCategory.MovementAndTurns, "[PHASE]", ColorCyanAccent);
        }

        private void HandleTurnStarted(TacticalUnit unit)
        {
            Color teamCol = unit.IsPlayerControlled ? ColorCyanAccent : ColorCrimson;
            AddAdvancedLog($"Engagement : <b>{unit.Stats.Name}</b> (PA: {unit.Stats.CurrentActionPoints}/{unit.Stats.MaxActionPoints})", LogCategory.MovementAndTurns, "[INIT]", teamCol);
        }

        private void HandleCombatEnded(CombatOutcome outcome)
        {
            string outcomeStr = outcome == CombatOutcome.Victory ? "🏆 ENGAGEMENT REMPORTÉ" : "💀 SIGNAUX VITAUX ROMPUS";
            Color col = outcome == CombatOutcome.Victory ? Color.green : ColorCrimson;
            AddAdvancedLog(outcomeStr, LogCategory.Combat, "[RÉSOLUTION]", col);
        }

        private static void InitProceduralTextures()
        {
            if (_pixelTex == null)
            {
                _pixelTex = new Texture2D(1, 1);
                _pixelTex.SetPixel(0, 0, Color.white);
                _pixelTex.Apply();
            }

            if (_gradientTex == null)
            {
                int w = 64;
                _gradientTex = new Texture2D(w, 1, TextureFormat.RGBA32, false);
                for (int x = 0; x < w; x++)
                {
                    float alpha = Mathf.SmoothStep(1.0f, 0.0f, (float)x / (w - 1));
                    _gradientTex.SetPixel(x, 0, new Color(1f, 1f, 1f, alpha));
                }
                _gradientTex.Apply();
            }
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
            if (_lastLogCount != _logEntries.Count)
            {
                _lastLogCount = _logEntries.Count;
                _logFlash = 1f;
            }

            UpdateDynamicGauges();
        }

        private void UpdateDynamicGauges()
        {
            if (Time.timeScale <= 0.0001f) return;

            var unit = _turnManager != null ? _turnManager.ActiveUnit : (_arena != null ? _arena.PlayerUnit : null);
            if (unit != null && unit.Stats != null)
            {
                if (_smoothPlayerHealth < 0)
                {
                    _smoothPlayerHealth = unit.Stats.CurrentHealth;
                    _ghostPlayerHealth = unit.Stats.CurrentHealth;
                    _smoothPlayerAP = unit.Stats.CurrentActionPoints;
                }

                _smoothPlayerHealth = Mathf.Lerp(_smoothPlayerHealth, unit.Stats.CurrentHealth, Time.unscaledDeltaTime * 12f);
                _ghostPlayerHealth = Mathf.Lerp(_ghostPlayerHealth, _smoothPlayerHealth, Time.unscaledDeltaTime * 2.5f);
                _smoothPlayerAP = Mathf.Lerp(_smoothPlayerAP, unit.Stats.CurrentActionPoints, Time.unscaledDeltaTime * 14f);
            }

            var target = _arena != null ? _arena.CurrentTarget : null;
            if (target != null && target.Stats != null)
            {
                if (_smoothTargetHealth < 0) _smoothTargetHealth = target.Stats.CurrentHealth;
                _smoothTargetHealth = Mathf.Lerp(_smoothTargetHealth, target.Stats.CurrentHealth, Time.unscaledDeltaTime * 10f);
            }
        }

        public void AddCombatLog(string message) => AddAdvancedLog(message);

        public void AddAdvancedLog(string message, LogCategory forcedCategory = LogCategory.All, string forcedTag = null, Color? forcedColor = null)
        {
            string timeStr = DateTime.Now.ToString("HH:mm:ss");
            LogCategory resolvedCategory = forcedCategory;
            string tag = forcedTag;
            Color tagCol = forcedColor ?? ColorCyanAccent;

            if (forcedCategory == LogCategory.All)
            {
                if (message.Contains("attaque") || message.Contains("Touché") || message.Contains("DÉVIATION") || message.Contains("PARADE") || message.Contains("ESQUIVE"))
                {
                    resolvedCategory = LogCategory.Combat;
                    tag = tag ?? (message.Contains("PARADE") || message.Contains("ESQUIVE") ? "[PARADE]" : "[BALISTIQUE]");
                    tagCol = message.Contains("PARADE") ? ColorCyanAccent : ColorCrimson;
                }
                else if (message.Contains("PA") || message.Contains("Points d'Action") || message.Contains("souffle"))
                {
                    resolvedCategory = LogCategory.ReactionAndAP;
                    tag = tag ?? "[RÉACTION]";
                    tagCol = ColorAmber;
                }
                else if (message.Contains("PV") || message.Contains("Dégâts") || message.Contains("CHOC") || message.Contains("K.O.") || message.Contains("MORT"))
                {
                    resolvedCategory = LogCategory.VitalityAndTrauma;
                    tag = tag ?? (message.Contains("MORT") ? "[CRITIQUE]" : "[IMPACT]");
                    tagCol = ColorCrimson;
                }
                else if (message.Contains("avance") || message.Contains("Tour") || message.Contains("Round"))
                {
                    resolvedCategory = LogCategory.MovementAndTurns;
                    tag = tag ?? "[PHASE]";
                    tagCol = new Color(0.7f, 0.85f, 1.0f);
                }
                else
                {
                    resolvedCategory = LogCategory.Combat;
                    tag = tag ?? "[SYSTÈME]";
                    tagCol = ColorTextMuted;
                }
            }

            _logEntries.Add(new CombatLogEntry
            {
                Timestamp = timeStr,
                RawMessage = message,
                Category = resolvedCategory,
                HeaderTag = tag,
                TagColor = tagCol,
                CreatedRealtime = Time.unscaledTime
            });

            if (_logEntries.Count > 100) _logEntries.RemoveAt(0);

            // Verrou actif => on ne touche jamais au scroll : l'utilisateur reste
            // exactement où il lit, même si des logs arrivent en rafale.
            // Verrou inactif => on demande un recalage en bas, appliqué dans le Draw
            // avec la hauteur réelle du contenu (pas de float.MaxValue).
            if (!_scrollLock)
            {
                _scrollToBottomPending = true;
            }
        }

        public void TogglePause() => SetPause(!IsPaused);

        public void SetPause(bool pause)
        {
            if (IsPaused == pause) return;

            IsPaused = pause;
            if (IsPaused)
            {
                _prePauseTimeScale = Time.timeScale > 0.001f ? Time.timeScale : 1.0f;
                Time.timeScale = 0f;
                AudioListener.pause = true;
                if (KilltimeAudioManager.Instance != null)
                {
                    KilltimeAudioManager.Instance.EnterPauseMode();
                    KilltimeAudioManager.Instance.PlayUI(SoundId.UI_Open, 0.7f);
                }
                AddAdvancedLog("⏸ Stase tactique engagée.", LogCategory.MovementAndTurns, "[STASE]", ColorCyanAccent);
            }
            else
            {
                Time.timeScale = _prePauseTimeScale;
                AudioListener.pause = false;
                if (KilltimeAudioManager.Instance != null)
                {
                    KilltimeAudioManager.Instance.ExitPauseMode();
                    KilltimeAudioManager.Instance.PlayUI(SoundId.UI_Close, 0.7f);
                }
                AddAdvancedLog("▶ Reprise de la dynamique causale.", LogCategory.MovementAndTurns, "[REPRISE]", Color.green);
            }

            // Diffusion multijoueur si GM
            var room = Killtime.Multi.VTTRoomManager.Instance;
            if (room != null && room.InRoom && room.IsGM)
            {
                var payload = new Killtime.Multi.VTTTurnControlPayload
                {
                    action = pause ? Killtime.Multi.VTTProtocol.TurnActionPause : Killtime.Multi.VTTProtocol.TurnActionResume,
                    round = Killtime.Multi.VTTTableSync.Instance != null ? Killtime.Multi.VTTTableSync.Instance.CurrentRound : 1
                };
                Killtime.Multi.VTTTableSync.Instance?.BroadcastTurnControl(payload);
                if (pause)
                {
                    Killtime.Multi.VTTTableSync.Instance?.BroadcastFullCombatState();
                }
            }
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
        }

        private void EnsureStyles()
        {
            if (_hudNameStyle != null) return;

            _hudNameStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                richText = false,
                wordWrap = false
            };
            _hudNameStyle.normal.textColor = ColorTextBright;

            _hudSubStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 9,
                fontStyle = FontStyle.Normal,
                alignment = TextAnchor.MiddleLeft,
                richText = true
            };
            _hudSubStyle.normal.textColor = ColorTextMuted;

            _hudStatStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 9,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight
            };
            _hudStatStyle.normal.textColor = ColorTextBright;

            _btnFlatNormal = new GUIStyle(GUI.skin.button)
            {
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(4, 4, 1, 1),
                margin = new RectOffset(0, 0, 0, 0),
                border = new RectOffset(0, 0, 0, 0)
            };
            _btnFlatNormal.normal.textColor = ColorTextBright;

            _floatingLogStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                fontStyle = FontStyle.Normal,
                alignment = TextAnchor.MiddleRight,
                wordWrap = false
            };

            _terminalHeaderStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };
            _terminalHeaderStyle.normal.textColor = ColorCyanAccent;

            _terminalBodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                alignment = TextAnchor.UpperLeft,
                wordWrap = true,
                richText = true
            };
            _terminalBodyStyle.normal.textColor = new Color(0.86f, 0.90f, 0.95f, 0.90f);

            _terminalDiceStyle = new GUIStyle(_terminalBodyStyle)
            {
                fontStyle = FontStyle.Bold
            };
            _terminalDiceStyle.normal.textColor = ColorCyanAccent;
        }

        // =========================================================================
        // CARTE JOUEUR — FICHE DE STATUT DISCRÈTE
        // =========================================================================
        /// <summary>
        /// Tronque un texte avec "…" pour qu'il tienne dans availWidth (mesuré avec le style).
        /// Évite les retours à la ligne et les chevauchements dans les cartes HUD à largeur fixe.
        /// </summary>
        private static string TruncateToFit(string text, GUIStyle style, float availWidth)
        {
            if (string.IsNullOrEmpty(text) || availWidth <= 0f) return "";
            if (style.CalcSize(new GUIContent(text)).x <= availWidth) return text;
            const string ellipsis = "…";
            float ellW = style.CalcSize(new GUIContent(ellipsis)).x;
            int len = text.Length;
            while (len > 1 && style.CalcSize(new GUIContent(text.Substring(0, len) + ellipsis)).x > availWidth)
            {
                len--;
            }
            if (len <= 1) return ellipsis;
            // Garantit que même avec l'ellipse ça passe (polices proportionnelles).
            string candidate = text.Substring(0, len) + ellipsis;
            while (candidate.Length > 2 && style.CalcSize(new GUIContent(candidate)).x > availWidth + ellW * 0.5f)
            {
                candidate = candidate.Substring(0, candidate.Length - 2) + ellipsis;
            }
            return candidate;
        }

        private void DrawTacticalPlayerCard(TacticalUnit unit)
        {
            float width = Mathf.Clamp(Screen.width * 0.27f, 250f, 340f);
            Rect rect = new Rect(24f, 22f, width, 62f);
            bool hovered = rect.Contains(Event.current.mousePosition);

            DrawSoftPanel(rect, new Color(0.025f, 0.045f, 0.065f, hovered ? 0.70f : 0.56f));
            DrawAccentLine(new Rect(rect.x, rect.y, rect.width, 2f),
                Color.Lerp(ColorCyanDim, ColorCyanAccent, hovered ? 1f : 0.62f), 0.78f);

            // Ligne titre : [Nom tronqué] [SOUFF.?] [ROUND nn] — sans chevauchement.
            const float roundW = 78f;
            Rect roundRect = new Rect(rect.x + width - 14f - roundW, rect.y + 8, roundW, 16);
            float nameRight = roundRect.x - 4f;
            Rect souffRect = new Rect();
            bool hasSouff = unit.Stats.Essoufflement > 0;
            if (hasSouff)
            {
                const float souffW = 52f;
                souffRect = new Rect(nameRight - souffW, rect.y + 8, souffW, 16);
                nameRight = souffRect.x - 6f;
            }
            float nameAvail = Mathf.Max(20f, nameRight - (rect.x + 14f));
            string name = TruncateToFit(unit.Stats.Name.ToUpperInvariant(), _hudNameStyle, nameAvail);
            GUI.Label(new Rect(rect.x + 14, rect.y + 7, nameAvail, 19), name, _hudNameStyle);

            int round = _turnManager != null ? _turnManager.CurrentRound : 1;
            GUI.color = ColorTextMuted;
            string roundLabel = (_turnManager != null && _turnManager.IsInExploration) ? "EXPLORE" : $"ROUND {round:00}";
            GUI.Label(roundRect, roundLabel, _hudSubStyle);
            GUI.color = Color.white;

            if (hasSouff)
            {
                GUI.color = ColorAmber;
                GUI.Label(souffRect, "SOUFF.", _hudSubStyle);
                GUI.color = Color.white;
            }

            float maxHp = Mathf.Max(1f, unit.Stats.MaxHealth);
            float hpRatio = Mathf.Clamp01(_smoothPlayerHealth / maxHp);
            float ghostRatio = Mathf.Clamp01(_ghostPlayerHealth / maxHp);
            Rect hp = new Rect(rect.x + 14, rect.y + 30, width - 28, 6);

            DrawSolidRect(hp, new Color(0.08f, 0.12f, 0.16f, 0.72f));
            if (ghostRatio > hpRatio)
                DrawSolidRect(new Rect(hp.x, hp.y, hp.width * ghostRatio, hp.height),
                    new Color(ColorGhostDamage.r, ColorGhostDamage.g, ColorGhostDamage.b, 0.26f));

            Color hpColor = hpRatio > 0.35f
                ? ColorCyanAccent
                : hpRatio > 0.20f
                    ? ColorAmber
                    : Color.Lerp(ColorCrimson, Color.white, Mathf.PingPong(_hudPulse * 3.5f, 1f));
            DrawSolidRect(new Rect(hp.x, hp.y, hp.width * hpRatio, hp.height), hpColor);

            int maxAp = Mathf.Max(1, unit.Stats.MaxActionPoints);
            int curAp = Mathf.Clamp(unit.Stats.CurrentActionPoints, 0, maxAp);
            float apY = rect.y + 45f;

            GUI.color = ColorTextMuted;
            GUI.Label(new Rect(rect.x + 14, apY - 2, 26, 16), "PA", _hudSubStyle);
            GUI.color = Color.white;

            // Compteur à droite, pips dans l'espace restant — jamais de chevauchement même à 13+ PA.
            const float countW = 46f;
            Rect countRect = new Rect(rect.x + width - 12f - countW, apY - 3, countW, 17);
            float pipsStart = rect.x + 44f;
            float pipsAvail = Mathf.Max(0f, countRect.x - 6f - pipsStart);
            float pipW = maxAp > 0
                ? Mathf.Clamp((pipsAvail - (maxAp - 1) * 3f) / maxAp, 3f, 15f)
                : 15f;
            for (int i = 0; i < maxAp; i++)
            {
                float x = pipsStart + i * (pipW + 3f);
                if (x + pipW > countRect.x - 2f) break; // sécurité : ne jamais écrire sous le compteur
                bool filled = i < curAp;
                float pulse = filled ? 0.76f + 0.24f * Mathf.Sin(_hudPulse * 4f + i) : 0f;
                DrawSolidRect(new Rect(x, apY + 2f, pipW, 4f),
                    filled ? new Color(ColorCyanAccent.r, ColorCyanAccent.g, ColorCyanAccent.b, pulse)
                           : new Color(0.13f, 0.20f, 0.26f, 0.38f));
            }

            GUI.color = ColorTextBright;
            GUI.Label(countRect, $"{curAp}/{maxAp}", _hudStatStyle);
            GUI.color = Color.white;
        }

        // =========================================================================
        // CARTE CIBLE — POSITIONNÉE À GAUCHE DU BOUTON PAUSE
        // =========================================================================
        private void DrawTacticalTargetCard()
        {
            if (_arena == null || _arena.CurrentTarget == null || !_arena.CurrentTarget.Stats.IsAlive) return;

            var target = _arena.CurrentTarget;
            float pauseW = 80f;
            float margin = 20f;
            float width = Mathf.Clamp(Screen.width * 0.20f, 200f, 280f);
            float x = Screen.width - margin - pauseW - 12f - width;
            Rect rect = new Rect(x, 22f, width, 56f);
            bool hovered = rect.Contains(Event.current.mousePosition);

            DrawSoftPanel(rect, new Color(0.055f, 0.025f, 0.035f, hovered ? 0.70f : 0.54f));
            DrawAccentLine(new Rect(rect.x + rect.width * 0.55f, rect.y,
                rect.width * 0.45f, 2f), ColorCrimson, 0.9f);

            GUI.color = ColorCrimson;
            const float armW = 58f;
            Rect armRect = new Rect(rect.x + width - 12f - armW, rect.y + 8, armW, 15);
            float targetNameAvail = Mathf.Max(20f, armRect.x - 4f - (rect.x + 12f));
            GUI.Label(new Rect(rect.x + 12, rect.y + 7, targetNameAvail, 19),
                TruncateToFit(target.Stats.Name.ToUpperInvariant(), _hudNameStyle, targetNameAvail), _hudNameStyle);
            GUI.color = ColorTextMuted;
            GUI.Label(armRect, $"ARM {target.Stats.BaseArmorAbsorption}", _hudSubStyle);

            float maxHp = Mathf.Max(1f, target.Stats.MaxHealth);
            float ratio = Mathf.Clamp01(_smoothTargetHealth / maxHp);
            Rect bar = new Rect(rect.x + 12, rect.y + 33, width - 24, 4);
            DrawSolidRect(bar, new Color(0.20f, 0.06f, 0.09f, 0.70f));
            DrawSolidRect(new Rect(bar.x, bar.y, bar.width * ratio, bar.height), ColorCrimson);

            GUI.color = ColorTextMuted;
            GUI.Label(new Rect(rect.x + 12, rect.y + 40, width - 24, 12), "CIBLE ACTIVE", _hudSubStyle);
            GUI.color = Color.white;
        }

        // =========================================================================
        // MATRICE DE CIBLAGE — DRAWER COMPACT
        // =========================================================================
        private void DrawSurgicalTargetingMatrix(TacticalUnit unit)
        {
            float width = Mathf.Clamp(Screen.width * 0.34f, 330f, 460f);
            float height = 216f;
            float targetY = Screen.height - height - 24f;
            float y = Mathf.Lerp(Screen.height + 10f, targetY, _anatomyOpenAnim);
            Rect rect = new Rect((Screen.width - width) * 0.5f, y, width, height);

            DrawSoftPanel(rect, new Color(0.018f, 0.032f, 0.050f, 0.95f));
            DrawAccentLine(new Rect(rect.x, rect.y, rect.width, 2f), ColorCyanAccent, 0.9f);

            GUI.Label(new Rect(rect.x + 14, rect.y + 10, width - 60, 19),
                "CIBLAGE // ZONES VULNÉRABLES", _hudNameStyle);
            GUI.color = ColorTextMuted;
            GUI.Label(new Rect(rect.x + 14, rect.y + 29, width - 55, 14),
                "Sélectionnez un point d'impact.", _hudSubStyle);
            GUI.color = Color.white;

            Rect close = new Rect(rect.x + width - 34, rect.y + 8, 22, 22);
            bool closeHover = close.Contains(Event.current.mousePosition);
            GUI.color = closeHover ? ColorCrimson : ColorTextMuted;
            if (GUI.Button(close, "×", _btnFlatNormal))
            {
                _showAnatomyDrawer = false;
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayUI(SoundId.VATS_Close, 0.6f);
                GUI.color = Color.white;
                return;
            }
            GUI.color = Color.white;

            BodyPart[] parts = { BodyPart.Tete, BodyPart.Torse, BodyPart.BrasDroit, BodyPart.Jambes };
            float rowY = rect.y + 50f;
            float rowH = 27f;

            foreach (var p in parts)
            {
                var info = BodyPartInfo.GetInfo(p);
                bool selected = _selectedBodyPart == p;
                Rect row = new Rect(rect.x + 12f, rowY, width - 24f, rowH);
                bool hovered = row.Contains(Event.current.mousePosition);

                if (selected)
                {
                    DrawSolidRect(row, new Color(0f, 0.85f, 1f, 0.10f));
                    DrawSolidRect(new Rect(row.x, row.y, 2f, row.height), ColorCyanAccent);
                }
                else if (hovered)
                    DrawSolidRect(row, new Color(1f, 1f, 1f, 0.045f));

                GUI.color = selected ? ColorCyanAccent : (hovered ? ColorTextBright : ColorTextMuted);
                GUI.Label(new Rect(row.x + 10, row.y + 5, 100, 17),
                    info.DisplayName.ToUpperInvariant(), _hudSubStyle);

                GUI.color = ColorTextMuted;
                GUI.Label(new Rect(row.x + 112, row.y + 5, row.width - 122, 17),
                    $"MOD {info.DifficultyModifier:+0;-0;0}   CRIT ×{info.CriticalDamageMultiplier:0.##}",
                    _hudStatStyle);
                GUI.color = Color.white;

                if (GUI.Button(row, GUIContent.none, GUIStyle.none))
                {
                    if (_selectedBodyPart != p && KilltimeAudioManager.Instance != null)
                        KilltimeAudioManager.Instance.PlayUI(SoundId.VATS_TargetChange, 0.55f);
                    _selectedBodyPart = p;
                }

                rowY += rowH + 2f;
            }

            GUI.color = ColorTextMuted;
            _cancelPenaltyWithAP = GUI.Toggle(
                new Rect(rect.x + 14f, rowY + 1f, width - 28f, 18f),
                _cancelPenaltyWithAP,
                "Visée compensée  ·  +1 PA", _hudSubStyle);
            GUI.color = Color.white;

            // Au contact : meilleure mêlée de l'attaquant (ex : Mains Nues entraînées),
            // à distance : Ballistique. Jamais de Maniement d'Arme imposé par défaut.
            var hudTarget = _arena != null ? _arena.CurrentTarget : null;
            SkillType hudAttackSkill = (hudTarget != null
                && unit.CurrentCoords.DistanceTo(hudTarget.CurrentCoords) <= 1)
                ? unit.Stats.GetBestMeleeAttackSkill()
                : SkillType.Ballistique;

            int apCost = _cancelPenaltyWithAP ? 3 : 2;
            bool canAttack = unit.Stats.CurrentActionPoints >= apCost && unit.Stats.CanAttack(unit.Stats.GetSkillDie(hudAttackSkill, true));

            Rect fire = new Rect(rect.x + 12f, rect.y + height - 38f, width - 24f, 28f);
            if (DrawTacticalButton(fire,
                canAttack ? $"TIR  ·  {apCost} PA" : "TIR INDISPONIBLE",
                null, false, canAttack ? ColorCrimson : ColorTextMuted, canAttack))
            {
                _showAnatomyDrawer = false;
                _arena?.ExecuteAttack(_selectedBodyPart, _cancelPenaltyWithAP, attackSkill: hudAttackSkill);
                OnAttackRequested?.Invoke(_selectedBodyPart, _cancelPenaltyWithAP);
            }
        }

        // =========================================================================
        // PANNEAU GRENADES — VISÉE DE CASE + LANCEUR + SOUFFLE
        // =========================================================================
        private void DrawGrenadeTargetingPanel(TacticalUnit unit)
        {
            float width = Mathf.Clamp(Screen.width * 0.38f, 360f, 500f);
            float height = 264f;
            float targetY = Screen.height - height - 24f;
            float y = Mathf.Lerp(Screen.height + 10f, targetY, _grenadeOpenAnim);
            Rect rect = new Rect((Screen.width - width) * 0.5f, y, width, height);

            DrawSoftPanel(rect, new Color(0.030f, 0.028f, 0.020f, 0.95f));
            DrawAccentLine(new Rect(rect.x, rect.y, rect.width, 2f), ColorAmber, 0.9f);

            GUI.Label(new Rect(rect.x + 14, rect.y + 10, width - 60, 19),
                "GRENADE // SOUFFLE DE ZONE", _hudNameStyle);
            var tgt = _arena != null ? _arena.CurrentTarget : null;
            string tgtLabel = (tgt != null && tgt.Stats != null && tgt.Stats.IsAlive)
                ? $"Case visée : ({tgt.CurrentCoords.Q},{tgt.CurrentCoords.R}) — {tgt.Stats.Name}"
                : "Aucune cible verrouillée (sélectionnez un mannequin).";
            GUI.color = ColorTextMuted;
            GUI.Label(new Rect(rect.x + 14, rect.y + 29, width - 55, 14), tgtLabel, _hudSubStyle);
            GUI.color = Color.white;

            Rect close = new Rect(rect.x + width - 34, rect.y + 8, 22, 22);
            bool closeHover = close.Contains(Event.current.mousePosition);
            GUI.color = closeHover ? ColorCrimson : ColorTextMuted;
            if (GUI.Button(close, "×", _btnFlatNormal))
            {
                _showGrenadeDrawer = false;
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayUI(SoundId.VATS_Close, 0.6f);
                GUI.color = Color.white;
                return;
            }
            GUI.color = Color.white;

            var sheet = unit.GetOrBuildSheet();
            var throwables = CombatDevArena.GetThrowableGrenades(sheet);
            var launcher = CombatDevArena.GetAnyLauncher(sheet);
            int maxRange = 8;
            try
            {
                var sel = throwables.Find(g => g != null && g.ItemId == _selectedGrenadeId) ?? (throwables.Count > 0 ? throwables[0] : null);
                if (sel != null)
                {
                    _selectedGrenadeId = sel.ItemId;
                    maxRange = Killtime.Core.Combat.GrenadeRules.ComputeMaxRange(sel, (_grenadeUseLauncher ? launcher : null));
                }
            }
            catch { }

            float rowY = rect.y + 50f;
            // Sélecteur de grenade (cycle).
            Rect selRect = new Rect(rect.x + 12f, rowY, width - 24f, 26f);
            string selName = "— aucune grenade (marché : Grenades) —";
            string selSub = "";
            var current = throwables.Find(g => g != null && g.ItemId == _selectedGrenadeId);
            if (current == null && throwables.Count > 0) { current = throwables[0]; _selectedGrenadeId = current.ItemId; }
            if (current != null)
            {
                int stock = current.IsStackable ? Mathf.Max(1, current.Quantity) : 1;
                selName = $"{current.Name} x{stock}";
                selSub = $"{current.Era} • {current.GrenadeKind} • R{current.BlastRadius} • {current.BaseDamage}+{current.DamageDiceCount}d10";
            }
            if (GUI.Button(selRect, GUIContent.none, GUIStyle.none))
            {
                if (throwables.Count > 1)
                {
                    int idx = throwables.FindIndex(g => g != null && g.ItemId == _selectedGrenadeId);
                    int next = (idx + 1) % throwables.Count;
                    _selectedGrenadeId = throwables[next].ItemId;
                    if (KilltimeAudioManager.Instance != null)
                        KilltimeAudioManager.Instance.PlayUI(SoundId.VATS_TargetChange, 0.55f);
                }
            }
            GUI.color = ColorAmber;
            GUI.Label(new Rect(selRect.x + 8, selRect.y + 1, selRect.width - 16, 15), selName.ToUpperInvariant(), _hudSubStyle);
            GUI.color = ColorTextMuted;
            GUI.Label(new Rect(selRect.x + 8, selRect.y + 13, selRect.width - 16, 12), selSub, _hudSubStyle);
            GUI.color = Color.white;
            DrawSolidRect(new Rect(selRect.x, selRect.yMax - 1, selRect.width, 1), new Color(1f, 0.72f, 0.15f, 0.25f));

            rowY += 30f;
            // Toggles lanceur + visée.
            GUI.color = ColorTextMuted;
            bool hasLauncher = launcher != null;
            GUI.enabled = hasLauncher;
            bool wantLauncher = GUI.Toggle(new Rect(rect.x + 14f, rowY, (width - 28f) * 0.55f, 18f),
                _grenadeUseLauncher && hasLauncher,
                hasLauncher ? $"Lanceur : {launcher.Name} (+{launcher.LauncherRangeBonus})" : "Lanceur : — (main 8 cases)", _hudSubStyle);
            GUI.enabled = true;
            if (wantLauncher != _grenadeUseLauncher)
            {
                _grenadeUseLauncher = wantLauncher && hasLauncher;
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayUI(SoundId.UI_Toggle, 0.55f);
            }
            _grenadeAimed = GUI.Toggle(
                new Rect(rect.x + 14f + (width - 28f) * 0.58f, rowY, (width - 28f) * 0.42f, 18f),
                _grenadeAimed, "Visée +1 PA", _hudSubStyle);
            GUI.color = Color.white;

            rowY += 20f;
            // Injection PA post-tirage (+1 / PA).
            GUI.color = ColorTextMuted;
            GUI.Label(new Rect(rect.x + 14f, rowY, 120f, 18f), $"Injection : +{_grenadeBonusPA} PA", _hudSubStyle);
            GUI.color = Color.white;
            float newBonus = GUI.HorizontalSlider(new Rect(rect.x + 140f, rowY + 4f, width - 154f, 14f), _grenadeBonusPA, 0f, 4f);
            int rounded = Mathf.RoundToInt(newBonus);
            if (rounded != _grenadeBonusPA) _grenadeBonusPA = rounded;

            rowY += 22f;
            int dist = -1;
            if (tgt != null) dist = unit.CurrentCoords.DistanceTo(tgt.CurrentCoords);
            int baseCost = (_grenadeUseLauncher && hasLauncher)
                ? Killtime.Core.Combat.GrenadeRules.LauncherShotAPCost
                : Killtime.Core.Combat.GrenadeRules.HandThrowAPCost;
            int apCost = baseCost + (_grenadeAimed ? 1 : 0) + _grenadeBonusPA;
            string rangeTxt = dist >= 0 ? $"Dist {dist} / Max {maxRange}" : $"Max {maxRange}";
            bool inRange = dist < 0 || dist <= maxRange;
            bool canThrow = current != null && tgt != null && inRange
                && unit.Stats.CurrentActionPoints >= apCost
                && unit.Stats.CanAttack(unit.Stats.GetSkillDie(SkillType.Ballistique, true));
            GUI.color = inRange ? ColorTextMuted : ColorCrimson;
            GUI.Label(new Rect(rect.x + 14f, rowY, width - 28f, 15f),
                $"{rangeTxt} • Coût {apCost} PA • Blast R{(current != null ? current.BlastRadius : 0)}", _hudSubStyle);
            GUI.color = Color.white;

            Rect launch = new Rect(rect.x + 12f, rect.y + height - 38f, width - 24f, 28f);
            if (DrawTacticalButton(launch,
                !canThrow ? "LANCER INDISPONIBLE" : $"💣 LANCER  ·  {apCost} PA",
                null, false, canThrow ? ColorAmber : ColorTextMuted, canThrow))
            {
                var targetUnit = _arena != null ? _arena.CurrentTarget : null;
                if (targetUnit != null && current != null)
                {
                    _showGrenadeDrawer = false;
                    var coords = targetUnit.CurrentCoords;
                    var gid = current.ItemId;
                    bool useL = _grenadeUseLauncher && hasLauncher;
                    bool aim = _grenadeAimed;
                    int bonus = _grenadeBonusPA;
                    _arena.ExecuteGrenadeThrow(coords, gid, useL, aim, bonus);
                    OnGrenadeRequested?.Invoke(gid, useL, aim, bonus);
                }
            }
        }

        private void TriggerEndTurn()
        {
            _showAnatomyDrawer = false;
            _showGrenadeDrawer = false;
            if (KilltimeAudioManager.Instance != null)
                KilltimeAudioManager.Instance.PlayUI(SoundId.Turn_End, 0.7f);

            if (TurnManager.IsMultiplayerPlayerClient())
            {
                Killtime.Multi.VTTTableSync.Instance?.RequestEndTurn();
            }
            else
            {
                _turnManager?.EndCurrentTurn();
            }
            OnEndTurnRequested?.Invoke();
        }


        // =========================================================================
        // SYSTÈME DE LOGS — FEED LÉGER
        // =========================================================================
        /// <summary>Segment de ligne du feed après découpe (tags rich text préservés).</summary>
        private struct DiceLineSegment
        {
            public string Text; // texte à dessiner, tags inclus
            public float X, Y, W, H; // relatifs à l'origine du message
            public bool IsDie;
            public DiceType Die;
            // Contexte du jet (si la mention "Attaque/Défense {comp} : {dé}" a été reconnue) :
            public bool HasContext;
            public DieMentionRole Role;
            public string SkillName;
            public string Attacker;
            public string Defender;
            // Rôle du jet d'après la phase ("Phase 1/2 … lance {dé}"), pour hériter
            // ensuite du contexte du jet opposé de même rôle et même dé.
            public bool HasPhaseRole;
            public DieMentionRole PhaseRole;
        }

        /// <summary>Mise en page pré-calculée (et mise en cache) d'une ligne du feed.</summary>
        private class DiceLineLayout
        {
            public readonly List<DiceLineSegment> Segments = new();
            public float Height;
            public bool HasDice;
        }

        /// <summary>Zone survolable d'une mention de dé, en coordonnées du contenu scrollé.</summary>
        private struct DiceHitRect
        {
            public Rect Rect;
            public DiceType Die;
            public string Key;
            public bool HasContext;
            public DieMentionRole Role;
            public string SkillName;
            public string Attacker;
            public string Defender;
        }

        private static readonly Regex DiceTagStripRegex = new Regex("<[^>]*>", RegexOptions.Compiled);

        /// <summary>
        /// Hauteur d'une entrée du feed : layout segmenté si elle mentionne des dés
        /// (même mesure que la passe de dessin), sinon mesure Unity inchangée.
        /// </summary>
        private float GetLogEntryHeight(CombatLogEntry entry, float textW)
        {
            var layout = GetDiceLineLayout(entry.RawMessage, textW);
            if (layout.HasDice) return layout.Height;
            return Mathf.Max(20f, _terminalBodyStyle.CalcHeight(new GUIContent(entry.RawMessage), textW));
        }

        /// <summary>
        /// Découpe une ligne du feed en segments positionnés (retours à la ligne manuels),
        /// en repérant les mentions de dés. Résultat mis en cache (clé = message + largeur).
        /// </summary>
        private DiceLineLayout GetDiceLineLayout(string message, float availW)
        {
            var layout = new DiceLineLayout { Height = 20f };
            if (string.IsNullOrEmpty(message)) return layout;
            string key = message + "\n" + ((int)availW);
            if (_diceLayoutCache.TryGetValue(key, out var cached))
                return cached;
            layout = BuildDiceLineLayout(message, availW);
            if (_diceLayoutCache.Count > 300)
                _diceLayoutCache.Clear();
            _diceLayoutCache[key] = layout;
            return layout;
        }

        private DiceLineLayout BuildDiceLineLayout(string message, float availW)
        {
            var layout = new DiceLineLayout();
            float lineH = Mathf.Max(14f, _terminalBodyStyle.CalcSize(new GUIContent("Mg")).y);
            float spaceW = _terminalBodyStyle.CalcSize(new GUIContent(" ")).x;
            // Contexte des jets : noms des protagonistes (1re ligne) + pour chaque dé,
            // regard-arrière "Attaque/Défense {comp} :" (aucun décalage possible même
            // si un dé isolé apparaît ailleurs dans le message).
            string plainMessage = DiceTagStripRegex.Replace(message, "");
            string firstLine = plainMessage;
            int nl = plainMessage.IndexOf('\n');
            if (nl >= 0) firstLine = plainMessage.Substring(0, nl);
            var parties = DiceLogParser.ParseParties(firstLine);
            float y = 0f;
            string pendingTags = "";
            var linePlain = new StringBuilder(256);
            string[] lines = message.Split('\n');
            for (int li = 0; li < lines.Length; li++)
            {
                float x = 0f;
                bool firstOnLine = true;
                linePlain.Length = 0;
                string[] tokens = lines[li].Split(' ');
                for (int ti = 0; ti < tokens.Length; ti++)
                {
                    string raw = tokens[ti];
                    if (raw.Length == 0) continue; // espaces multiples : repliés
                    string plain = DiceTagStripRegex.Replace(raw, "");
                    if (plain.Length == 0) { pendingTags += raw; continue; } // tag seul : reporté
                    string tok = pendingTags + raw;
                    pendingTags = "";
                    bool isDie = DiceTypeHints.TryParseLogToken(plain, out DiceType die);
                    float w = (isDie ? _terminalDiceStyle : _terminalBodyStyle).CalcSize(new GUIContent(tok)).x;
                    if (!firstOnLine && x + w > availW)
                    {
                        y += lineH;
                        x = 0f;
                        firstOnLine = true;
                    }
                    if (isDie) layout.HasDice = true;
                    var seg = new DiceLineSegment
                    {
                        Text = tok,
                        X = x,
                        Y = y,
                        W = w,
                        H = lineH,
                        IsDie = isDie,
                        Die = die
                    };
                    if (isDie && parties.HasParties
                        && DiceLogParser.TryParseTrailingMention(linePlain.ToString(), out var dieRole, out var dieSkill))
                    {
                        seg.HasContext = true;
                        seg.Role = dieRole;
                        seg.SkillName = dieSkill;
                        seg.Attacker = parties.Attacker;
                        seg.Defender = parties.Defender;
                    }
                    else if (isDie && DiceLogParser.TryParseTrailingPhaseRole(linePlain.ToString(), out var phaseRole))
                    {
                        seg.HasPhaseRole = true;
                        seg.PhaseRole = phaseRole;
                    }
                    layout.Segments.Add(seg);
                    linePlain.Append(plain).Append(' ');
                    x += w + spaceW;
                    firstOnLine = false;
                }
                y += lineH;
            }
            // Passe 2 : les dés des phases ("Phase 1/2 … lance {dé} → brut…", sans
            // mention de compétence) héritent du contexte du jet opposé de même
            // rôle et même dé (même variable dans CombatCalculator).
            for (int i = 0; i < layout.Segments.Count; i++)
            {
                var s = layout.Segments[i];
                if (!s.IsDie || s.HasContext || !s.HasPhaseRole) continue;
                for (int j = 0; j < layout.Segments.Count; j++)
                {
                    var c = layout.Segments[j];
                    if (c.IsDie && c.HasContext && c.Die == s.Die && c.Role == s.PhaseRole)
                    {
                        s.HasContext = true;
                        s.Role = c.Role;
                        s.SkillName = c.SkillName;
                        s.Attacker = c.Attacker;
                        s.Defender = c.Defender;
                        layout.Segments[i] = s;
                        break;
                    }
                }
            }
            layout.Height = Mathf.Max(20f, y);
            return layout;
        }

        /// <summary>Dessine une ligne contenant des mentions de dés, segment par segment.</summary>
        private void DrawDiceLineSegments(DiceLineLayout layout, CombatLogEntry entry, float yEntry)
        {
            int msgHash = entry.RawMessage != null ? entry.RawMessage.GetHashCode() : 0;
            for (int s = 0; s < layout.Segments.Count; s++)
            {
                var seg = layout.Segments[s];
                Rect r = new Rect(116f + seg.X, yEntry + seg.Y, seg.W + 2f, seg.H);
                GUI.Label(r, seg.Text, seg.IsDie ? _terminalDiceStyle : _terminalBodyStyle);
                // Seuls les dés à contexte expliqué sont survolables : aucun tooltip
                // générique ("D4 : 1-4 + mod") n'est affiché pour les autres.
                if (seg.IsDie && seg.HasContext)
                {
                    string key = msgHash + ":" + s;
                    _diceHitRects.Add(new DiceHitRect
                    {
                        Rect = r,
                        Die = seg.Die,
                        Key = key,
                        HasContext = seg.HasContext,
                        Role = seg.Role,
                        SkillName = seg.SkillName,
                        Attacker = seg.Attacker,
                        Defender = seg.Defender
                    });
                    if (key == _diceHoverKey)
                        DrawAccentLine(new Rect(r.x, r.y + r.height - 2f, seg.W, 1f), ColorCyanAccent, 0.9f);
                }
            }
        }

        /// <summary>
        /// Survol des mentions de dés : après 1 s immobile sur un dé, affiche sa
        /// formule de calcul (très concise) dans un tooltip près du curseur.
        /// </summary>
        private void DrawDiceHoverTooltip(Rect scrollRect)
        {
            Vector2 mouse = Event.current.mousePosition;
            Vector2 local = new Vector2(mouse.x - scrollRect.x + _logScroll.x,
                mouse.y - scrollRect.y + _logScroll.y);

            string hitKey = null;
            DiceHitRect hit = default;
            for (int i = 0; i < _diceHitRects.Count; i++)
            {
                if (_diceHitRects[i].Rect.Contains(local))
                {
                    hitKey = _diceHitRects[i].Key;
                    hit = _diceHitRects[i];
                    break;
                }
            }

            float now = Time.realtimeSinceStartup;
            if (hitKey == null || hitKey != _diceHoverKey)
            {
                _diceHoverKey = hitKey;
                _diceHoverStart = now;
                _diceHoverHit = hit;
                _diceHoverValid = hitKey != null;
                if (hitKey != null && hit.HasContext
                    && !TryResolveDieContext(hit, out _, out _, out string failReason))
                {
                    Debug.LogWarning($"[DiceTooltip] Aucun tooltip ({failReason}) "
                        + $"pour '{hit.SkillName}' ({hit.Role}) : {hit.Attacker} ➔ {hit.Defender}.");
                }
            }

            if (!_diceHoverValid || Event.current.type != EventType.Repaint)
                return;
            if (now - _diceHoverStart < DiceTooltipDelay)
                return;

            string text = BuildDieBreakdownText(_diceHoverHit);
            if (string.IsNullOrEmpty(text)) return;
            float padX = 10f, padY = 6f;
            float maxLine = 0f;
            foreach (string ln in text.Split('\n'))
                maxLine = Mathf.Max(maxLine, _terminalBodyStyle.CalcSize(new GUIContent(ln)).x);
            float w = Mathf.Clamp(maxLine + padX * 2f, 140f, 320f);
            float h = _terminalBodyStyle.CalcHeight(new GUIContent(text), w - padX * 2f) + padY * 2f;
            float x = Mathf.Clamp(mouse.x + 16f, 8f, Mathf.Max(8f, Screen.width - w - 8f));
            float y = mouse.y - h - 14f;
            if (y < 8f) y = mouse.y + 20f;
            Rect box = new Rect(x, y, w, h);

            DrawSoftPanel(box, new Color(0.01f, 0.02f, 0.035f, 0.96f));
            DrawAccentLine(new Rect(box.x, box.y, box.width, 2f), ColorCyanAccent, 0.9f);
            GUI.color = ColorTextBright;
            GUI.Label(new Rect(box.x + padX, box.y + padY, w - padX * 2f, h - padY * 2f), text, _terminalBodyStyle);
            GUI.color = Color.white;
        }

        /// <summary>
        /// Retrouve une unité du combat par son nom tel que loggé : exact, puis
        /// insensible à la casse, puis par contenance (surnoms/tronquages).
        /// </summary>
        private TacticalUnit FindUnitByName(string name)
        {
            if (string.IsNullOrEmpty(name) || _turnManager == null) return null;
            var order = _turnManager.TurnOrder;
            for (int i = 0; i < order.Count; i++)
            {
                var u = order[i];
                if (u != null && u.Stats != null && u.Stats.Name == name) return u;
            }
            for (int i = 0; i < order.Count; i++)
            {
                var u = order[i];
                if (u != null && u.Stats != null
                    && string.Equals(u.Stats.Name, name, StringComparison.OrdinalIgnoreCase)) return u;
            }
            for (int i = 0; i < order.Count; i++)
            {
                var u = order[i];
                if (u == null || u.Stats == null || string.IsNullOrEmpty(u.Stats.Name)) continue;
                if (u.Stats.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf(u.Stats.Name, StringComparison.OrdinalIgnoreCase) >= 0) return u;
            }
            return null;
        }

        private static string TrainingPips(int training)
        {
            int full = Mathf.Clamp(training, 0, CharacterProgressionManager.MAX_SKILL_TRAINING);
            string pips = new string('●', full)
                + new string('○', CharacterProgressionManager.MAX_SKILL_TRAINING - full);
            if (training > CharacterProgressionManager.MAX_SKILL_TRAINING) pips += "+";
            return pips;
        }

        /// <summary>
        /// Résout le contexte d'un dé survolé (compétence + unité). False + raison
        /// si le tooltip doit retomber sur la formule concise du dé.
        /// </summary>
        private bool TryResolveDieContext(DiceHitRect hit, out SkillType skill, out TacticalUnit unit, out string failReason)
        {
            skill = default;
            unit = null;
            failReason = null;
            if (!hit.HasContext) { failReason = "mention Attaque/Défense non reconnue"; return false; }
            if (!SkillDefinitions.TryParseDisplayName(hit.SkillName, out skill))
            {
                failReason = $"compétence '{hit.SkillName}' non reconnue";
                return false;
            }
            bool isOffensive = hit.Role == DieMentionRole.Attack;
            string unitName = isOffensive ? hit.Attacker : hit.Defender;
            unit = FindUnitByName(unitName);
            if (unit == null || unit.Stats == null)
            {
                failReason = $"unité '{unitName}' introuvable au tour en cours";
                return false;
            }
            return true;
        }

        /// <summary>
        /// Décompose pourquoi le dé est de ce type : compétence, rang de base
        /// (caracs), paliers, entraînements (icônes + niveau) et total.
        /// Null si le contexte est introuvable (aucun tooltip affiché dans ce cas).
        /// </summary>
        private string BuildDieBreakdownText(DiceHitRect hit)
        {
            if (!TryResolveDieContext(hit, out SkillType skill, out TacticalUnit unit, out _))
                return null;
            bool isOffensive = hit.Role == DieMentionRole.Attack;
            string unitName = isOffensive ? hit.Attacker : hit.Defender;

            var stats = unit.Stats;
            int rank = SkillDefinitions.GetBaseRank(skill, stats.Attributes, isOffensive);
            int steps = SkillDefinitions.CharacteristicSteps(rank);
            int training = stats.Sheet != null ? stats.Sheet.GetSkill(skill).TrainingLevel : 0;
            int total = steps + training;
            string label = DiceTypeHints.GetShortLabel(hit.Die);

            string text = $"{SkillDefinitions.GetDisplayName(skill)} — {unitName} : {label}\n"
                + $"Base {SkillDefinitions.DescribeBaseRank(skill, stats.Attributes, isOffensive)} → +{steps}\n"
                + $"Entraînement {TrainingPips(training)} {training}/{CharacterProgressionManager.MAX_SKILL_TRAINING} → +{training}\n"
                + $"Total {total} → {label}";
            if (stats.IsMagicAugmented(skill, isOffensive)) text += "\n★ Corps Augmenté";
            return text;
        }

        private void DrawAdvancedLogSystem(bool isPauseActive)
        {
            float drawerWidth = Mathf.Clamp(Screen.width * 0.44f, 480f, 660f);
            float drawerHeight = Mathf.Clamp(Screen.height * 0.35f, 220f, 320f);
            float margin = 20f;

            if (!_isLogDrawerExpanded)
            {
                if (!isPauseActive)
                {
                    DrawFloatingEphemeralLogs();
                }

                Rect toggle = new Rect(Screen.width - 92f - margin, Screen.height - 30f - margin, 92f, 22f);
                bool hover = toggle.Contains(Event.current.mousePosition);
                DrawSoftPanel(toggle, new Color(0.02f, 0.035f, 0.05f, hover ? 0.68f : 0.40f), false);
                DrawAccentLine(new Rect(toggle.x, toggle.y + toggle.height - 1f, toggle.width, 1f),
                    hover ? ColorCyanAccent : ColorCyanDim, 1f);

                GUI.color = hover ? ColorTextBright : ColorTextMuted;
                if (GUI.Button(toggle, _logEntries.Count > 0 ? $"FEED  {_logEntries.Count}" : "FEED", _btnFlatNormal))
                    _isLogDrawerExpanded = true;
                GUI.color = Color.white;
                return;
            }

            Rect terminal = new Rect(Screen.width - drawerWidth - margin,
                Screen.height - drawerHeight - margin, drawerWidth, drawerHeight);

            DrawSoftPanel(terminal, new Color(0.012f, 0.025f, 0.040f, 0.94f));
            DrawAccentLine(new Rect(terminal.x + 16, terminal.y, terminal.width - 32, 2f),
                ColorCyanAccent, 0.85f);

            GUI.Label(new Rect(terminal.x + 14, terminal.y + 10, terminal.width - 265, 18),
                "FLUX TACTIQUE // DÉTAIL DES ACTIONS", _terminalHeaderStyle);

            Rect lockBtnRect = new Rect(terminal.x + terminal.width - 246, terminal.y + 8, 108, 20);
            bool lockHover = lockBtnRect.Contains(Event.current.mousePosition);
            DrawSoftPanel(lockBtnRect, _scrollLock
                ? new Color(ColorAmber.r, ColorAmber.g, ColorAmber.b, 0.22f)
                : new Color(1f, 1f, 1f, lockHover ? 0.05f : 0.018f), false);
            DrawAccentLine(new Rect(lockBtnRect.x, lockBtnRect.y + lockBtnRect.height - 1f, lockBtnRect.width, 1f),
                _scrollLock ? ColorAmber : (lockHover ? ColorCyanAccent : ColorCyanDim), 1f);

            GUI.color = _scrollLock ? ColorAmber : (lockHover ? ColorTextBright : ColorTextMuted);
            if (GUI.Button(lockBtnRect, _scrollLock ? "🔒 SCROLL LOCK" : "🔓 AUTO-SCROLL", _btnFlatNormal))
            {
                _scrollLock = !_scrollLock;
                if (!_scrollLock)
                {
                    // Déverrouillage => retour immédiat en bas (consommé plus bas
                    // dans ce même Draw, une fois totalContentHeight connu).
                    _scrollToBottomPending = true;
                }
                else
                {
                    // Verrouillage => on annule toute demande en cours pour ne pas
                    // sauter en bas juste après avoir verrouillé.
                    _scrollToBottomPending = false;
                }
            }

            GUI.color = ColorTextMuted;
            if (GUI.Button(new Rect(terminal.x + terminal.width - 132, terminal.y + 8, 60, 20),
                "EFFACER", _btnFlatNormal))
            {
                _logEntries.Clear();
                _logScroll.y = 0f;
                _scrollToBottomPending = false;
            }

            if (GUI.Button(new Rect(terminal.x + terminal.width - 66, terminal.y + 8, 56, 20),
                "FERMER", _btnFlatNormal))
            {
                _isLogDrawerExpanded = false;
                GUIUtility.hotControl = 0;
                GUIUtility.keyboardControl = 0;
            }
            GUI.color = Color.white;

            Rect filters = new Rect(terminal.x + 12, terminal.y + 34, terminal.width - 24, 20);
            float fx = filters.x;
            DrawFilterPill(new Rect(fx, filters.y, 48, 18), "TOUT", LogCategory.All); fx += 51;
            DrawFilterPill(new Rect(fx, filters.y, 58, 18), "COMBAT", LogCategory.Combat); fx += 61;
            DrawFilterPill(new Rect(fx, filters.y, 42, 18), "PA", LogCategory.ReactionAndAP); fx += 45;
            DrawFilterPill(new Rect(fx, filters.y, 68, 18), "TRAUMA", LogCategory.VitalityAndTrauma); fx += 71;
            DrawFilterPill(new Rect(fx, filters.y, 52, 18), "PHASE", LogCategory.MovementAndTurns);

            float textW = terminal.width - 145f;
            float totalContentHeight = 0f;
            for (int i = 0; i < _logEntries.Count; i++)
            {
                var entry = _logEntries[i];
                if (_activeCategory != LogCategory.All && entry.Category != _activeCategory) continue;
                float h = GetLogEntryHeight(entry, textW);
                totalContentHeight += h + 6f;
            }

            Rect scrollRect = new Rect(terminal.x + 12, terminal.y + 58, terminal.width - 24, terminal.height - 70);
            float maxScrollY = Mathf.Max(0f, totalContentHeight + 10f - scrollRect.height);
            if (_scrollLock)
            {
                // Verrou : on reste exactement où l'utilisateur a scrollé.
                // On clamp au cas où le contenu a rétréci (filtre, EFFACER) et on
                // oublie toute demande de retour en bas.
                _scrollToBottomPending = false;
                _logScroll.y = Mathf.Clamp(_logScroll.y, 0f, maxScrollY);
            }
            else if (_scrollToBottomPending)
            {
                _logScroll.y = maxScrollY;
                _scrollToBottomPending = false;
            }
            _logScroll = GUI.BeginScrollView(scrollRect, _logScroll,
                new Rect(0, 0, terminal.width - 42, Mathf.Max(scrollRect.height, totalContentHeight + 10f)));

            float y = 2f;
            _diceHitRects.Clear();
            for (int i = 0; i < _logEntries.Count; i++)
            {
                var entry = _logEntries[i];
                if (_activeCategory != LogCategory.All && entry.Category != _activeCategory) continue;

                var layout = GetDiceLineLayout(entry.RawMessage, textW);
                float msgH = layout.HasDice ? layout.Height
                    : Mathf.Max(20f, _terminalBodyStyle.CalcHeight(new GUIContent(entry.RawMessage), textW));

                GUI.color = entry.TagColor;
                GUI.Label(new Rect(0, y, 70, 18), entry.HeaderTag, _terminalHeaderStyle);
                GUI.color = ColorTextMuted;
                GUI.Label(new Rect(72, y, 42, 18), entry.Timestamp, _hudSubStyle);
                GUI.color = ColorTextBright;
                if (layout.HasDice)
                    DrawDiceLineSegments(layout, entry, y);
                else
                    GUI.Label(new Rect(116, y, textW, msgH), entry.RawMessage, _terminalBodyStyle);
                GUI.color = Color.white;

                y += msgH + 6f;
            }
            GUI.EndScrollView();
            DrawDiceHoverTooltip(scrollRect);
        }

        private void DrawFilterPill(Rect r, string label, LogCategory cat)
        {
            bool selected = _activeCategory == cat;
            bool hover = r.Contains(Event.current.mousePosition);
            DrawSoftPanel(r,
                selected ? new Color(0f, 0.75f, 0.9f, 0.15f)
                         : new Color(1f, 1f, 1f, hover ? 0.05f : 0.018f), false);

            GUI.color = selected ? ColorCyanAccent : (hover ? ColorTextBright : ColorTextMuted);
            if (GUI.Button(r, label, _btnFlatNormal))
            {
                if (_activeCategory != cat)
                {
                    _activeCategory = cat;
                    // Changement de filtre : en mode auto-scroll on repart en bas,
                    // en mode verrou on reste où l'utilisateur lit.
                    if (!_scrollLock) _scrollToBottomPending = true;
                }
            }
            GUI.color = Color.white;
        }

        // =========================================================================
        // BOUTON PAUSE EN HAUT À DROITE
        // =========================================================================
        private void DrawFloatingEphemeralLogs()
        {
            float width = Mathf.Clamp(Screen.width * 0.42f, 420f, 600f);
            float yOffset = Screen.height - 58f;
            float now = Time.unscaledTime;
            int drawn = 0;

            for (int i = _logEntries.Count - 1; i >= 0 && drawn < 3; i--)
            {
                var entry = _logEntries[i];
                float age = now - entry.CreatedRealtime;
                if (age > 5.5f) continue;

                float alpha = Mathf.Clamp01(age < 0.4f ? 1f : 1f - ((age - 3.5f) / 2f));
                float slide = Mathf.SmoothStep(18f, 0f, Mathf.Clamp01(age / 0.35f));

                Rect line = new Rect(Screen.width - width - 24f + slide, yOffset, width, 18f);
                Color fade = ColorTextBright;
                fade.a = alpha * 0.92f;
                _floatingLogStyle.normal.textColor = fade;

                string displayLine = entry.RawMessage.Contains('\n') 
                    ? entry.RawMessage.Split('\n')[0] 
                    : entry.RawMessage;

                GUI.Label(line, $"{entry.HeaderTag}  {displayLine}", _floatingLogStyle);

                yOffset -= 20f;
                drawn++;
            }
        }

        private void DrawTopRightPausePill()
        {
            float width = 80f;
            float margin = 20f;
            Rect rect = new Rect(Screen.width - width - margin, 22f, width, 24f);
            bool hovered = rect.Contains(Event.current.mousePosition);

            DrawSoftPanel(rect,
                new Color(0.02f, 0.035f, 0.05f, hovered || IsPaused ? 0.68f : 0.36f),
                false);

            Color accent = IsPaused ? ColorAmber : (hovered ? ColorCyanAccent : ColorCyanDim);
            DrawAccentLine(new Rect(rect.x, rect.y + rect.height - 1f, rect.width, 1f), accent, 1f);

            GUI.color = accent;
            if (GUI.Button(rect, IsPaused ? "REPRENDRE" : "PAUSE", _btnFlatNormal))
                TogglePause();
            GUI.color = Color.white;
        }

        private void DrawStasisModal()
        {
            float width = 390f;
            float height = 178f;

            DrawSolidRect(new Rect(0, 0, Screen.width, Screen.height),
                new Color(0.005f, 0.010f, 0.018f, 0.42f));

            Rect rect = new Rect((Screen.width - width) * 0.5f,
                (Screen.height - height) * 0.46f, width, height);

            DrawSoftPanel(rect, new Color(0.018f, 0.032f, 0.05f, 0.97f));
            DrawAccentLine(new Rect(rect.x, rect.y, rect.width, 2f), ColorCyanAccent, 0.95f);

            GUI.color = ColorCyanAccent;
            GUI.Label(new Rect(rect.x + 22, rect.y + 20, width - 44, 22),
                "TEMPS SUSPENDU", _hudNameStyle);

            GUI.color = ColorTextMuted;
            GUI.Label(new Rect(rect.x + 22, rect.y + 46, width - 44, 36),
                "Le champ tactique est figé. Reprise instantanée à la prochaine commande.",
                _terminalBodyStyle);
            GUI.color = Color.white;

            Rect resume = new Rect(rect.x + 22, rect.y + 92, width - 44, 30f);
            if (DrawTacticalButton(resume, "REPRENDRE L'ENGAGEMENT", null, false, ColorCyanAccent))
                SetPause(false);

            Rect rewind = new Rect(rect.x + 22, rect.y + 130, (width - 50f) * 0.5f, 24f);
            if (DrawTacticalButton(rewind, "REMBOBINER", null, false, ColorCyanAccent))
                _arena?.RewindLastSnapshot();

            Rect reset = new Rect(rewind.xMax + 6f, rewind.y, rewind.width, 24f);
            if (DrawTacticalButton(reset, "RÉINITIALISER", null, false, ColorCrimson))
            {
                SetPause(false);
                _arena?.ResetArena();
            }
        }

        private void DrawOutcomeCard()
        {
            if (_hideOutcomeCardForReview)
            {
                Rect reviewPill = new Rect((Screen.width - 190f) * 0.5f, 22f, 190f, 26f);
                DrawSoftPanel(reviewPill, new Color(0.018f, 0.032f, 0.05f, 0.92f), false);
                DrawAccentLine(new Rect(reviewPill.x, reviewPill.y + reviewPill.height - 1f, reviewPill.width, 1f), ColorCyanAccent, 1f);
                GUI.color = ColorCyanAccent;
                if (GUI.Button(reviewPill, "📊 RAPPORT D'ENGAGEMENT", _btnFlatNormal))
                {
                    _hideOutcomeCardForReview = false;
                }
                GUI.color = Color.white;
                return;
            }

            bool isVictory = _turnManager.CurrentOutcome == CombatOutcome.Victory;
            Color accent = isVictory ? ColorCyanAccent : ColorCrimson;
            bool isPlayerClient = TurnManager.IsMultiplayerPlayerClient();

            float width = Mathf.Clamp(Screen.width * 0.46f, 520f, 620f);
            float height = isPlayerClient ? 285f : 335f;

            DrawSolidRect(new Rect(0, 0, Screen.width, Screen.height),
                new Color(0.005f, 0.010f, 0.018f, 0.55f));

            Rect rect = new Rect((Screen.width - width) * 0.5f,
                (Screen.height - height) * 0.44f, width, height);

            DrawSoftPanel(rect, new Color(0.014f, 0.025f, 0.042f, 0.98f));
            DrawAccentLine(new Rect(rect.x, rect.y, rect.width, 2f), accent, 1f);

            int roundCount = _turnManager != null ? _turnManager.CurrentRound : 1;
            int causalSeconds = roundCount * 10;

            string title = isPlayerClient
                ? (isVictory ? "🏆 ENGAGEMENT REMPORTÉ" : "💀 SIGNAUX VITAUX ROMPUS")
                : (isVictory ? "🏆 ENGAGEMENT TERMINÉ (GM)" : "💀 SIGNAUX VITAUX ROMPUS (GM)");

            string subtitle = isVictory
                ? "Menace neutralisée. Télémétrie causale synchronisée avec les 11 Livres du Codex."
                : "L'escouade a succombé au combat. Données vitales archivées.";

            GUI.color = accent;
            GUI.Label(new Rect(rect.x + 20, rect.y + 16, width - 40, 20), title, _hudNameStyle);
            GUI.color = ColorTextMuted;
            GUI.Label(new Rect(rect.x + 20, rect.y + 36, width - 40, 16), subtitle, _hudSubStyle);
            GUI.color = Color.white;

            var allUnits = FindObjectsByType<TacticalUnit>();
            int liveAllies = 0, deadAllies = 0, deadEnemies = 0;
            for (int i = 0; i < allUnits.Length; i++)
            {
                var u = allUnits[i];
                if (u == null || u.Stats == null) continue;
                if (u.IsPlayerControlled)
                {
                    if (u.Stats.IsAlive) liveAllies++;
                    else deadAllies++;
                }
                else
                {
                    if (!u.Stats.IsAlive) deadEnemies++;
                }
            }

            int finalNeutralized = Mathf.Max(Telemetry.EnemiesNeutralized, deadEnemies);
            int finalAlliesDown = Mathf.Max(Telemetry.AlliesDown, deadAllies);
            int totalAttacks = Telemetry.TotalAttacks;
            int hits = Telemetry.SuccessfulHits;
            int hitPct = totalAttacks > 0 ? Mathf.RoundToInt(((float)hits / totalAttacks) * 100f) : 0;

            float gridY = rect.y + 60f;
            float cellW = (width - 48f) / 3f;
            float cellH = 46f;
            float gapX = 4f;
            float gapY = 4f;

            DrawMetricCell(new Rect(rect.x + 20f, gridY, cellW, cellH), "CYCLES CAUSAUX", $"ROUND {roundCount:00}", $"{causalSeconds}s de combat (L. VI)", ColorCyanAccent);
            DrawMetricCell(new Rect(rect.x + 20f + cellW + gapX, gridY, cellW, cellH), "PRÉCISION BALISTIQUE", $"{hitPct}%", $"{hits}/{totalAttacks} touches ({Telemetry.CriticalHits} crits)", ColorAmber);
            DrawMetricCell(new Rect(rect.x + 20f + (cellW + gapX) * 2f, gridY, cellW, cellH), "DÉGÂTS INFLIGÉS", $"{Telemetry.DamageDealtByAllies} PV", $"{Telemetry.ArmorAbsorbedByEnemies} absorbés armure", Color.green);

            float row2Y = gridY + cellH + gapY;
            DrawMetricCell(new Rect(rect.x + 20f, row2Y, cellW, cellH), "DÉGÂTS SUBIS", $"{Telemetry.DamageTakenByAllies} PV", $"{Telemetry.ArmorAbsorbedByAllies} encaissés", ColorCrimson);
            DrawMetricCell(new Rect(rect.x + 20f + cellW + gapX, row2Y, cellW, cellH), "NEUTRALISATIONS", $"{finalNeutralized} cibles", $"{Telemetry.TraumaShocks} chocs traumatiques", Color.Lerp(ColorCrimson, ColorAmber, 0.5f));
            DrawMetricCell(new Rect(rect.x + 20f + (cellW + gapX) * 2f, row2Y, cellW, cellH), "BILAN ESCOUADE", $"{liveAllies} vivant(s)", $"{finalAlliesDown} hors de combat", liveAllies > 0 ? ColorCyanAccent : ColorCrimson);

            float mvpY = row2Y + cellH + gapY;
            Rect mvpRect = new Rect(rect.x + 20f, mvpY, width - 40f, 24f);
            DrawSolidRect(mvpRect, new Color(0.025f, 0.045f, 0.07f, 0.70f));
            DrawAccentLine(new Rect(mvpRect.x, mvpRect.y, 2f, mvpRect.height), ColorCyanDim, 1f);

            GUI.color = ColorTextMuted;
            GUI.Label(new Rect(mvpRect.x + 8f, mvpY + 4f, 120f, 16f), "IMPACT MAJEUR :", _hudSubStyle);
            GUI.color = ColorTextBright;
            string mvpText = Telemetry.TopDamageAmount > 0
                ? $"<b>{Telemetry.TopDamageDealer}</b> ({Telemetry.TopDamageAmount} PV infligés) • {Telemetry.DeflectedOrParried} parades/esquives réussies"
                : "Aucune frappe décisive enregistrée ce cycle.";
            GUI.Label(new Rect(mvpRect.x + 115f, mvpY + 4f, mvpRect.width - 125f, 16f), mvpText, _hudSubStyle);
            GUI.color = Color.white;

            DrawAccentLine(new Rect(rect.x + 20f, mvpRect.yMax + 8f, width - 40f, 1f), ColorCyanDim, 0.45f);

            if (isPlayerClient)
            {
                float bottomY = mvpRect.yMax + 14f;
                Rect statusRect = new Rect(rect.x + 20f, bottomY, width - 165f, 30f);
                DrawSolidRect(statusRect, new Color(0.02f, 0.05f, 0.08f, 0.75f));
                DrawAccentLine(new Rect(statusRect.x, statusRect.y, 2f, statusRect.height), ColorAmber, 0.9f);

                GUI.color = ColorAmber;
                GUI.Label(new Rect(statusRect.x + 8f, bottomY + 3f, statusRect.width - 16f, 14f), "● EN ATTENTE DU MAÎTRE DU JEU", _hudSubStyle);
                GUI.color = ColorTextMuted;
                GUI.Label(new Rect(statusRect.x + 8f, bottomY + 15f, statusRect.width - 16f, 12f), "Le MJ administre le flux temporel et la réinitialisation.", _hudSubStyle);
                GUI.color = Color.white;

                Rect observeBtn = new Rect(statusRect.xMax + 6f, bottomY, 119f, 30f);
                if (DrawTacticalButton(observeBtn, "OBSERVER", null, false, ColorCyanAccent))
                {
                    _hideOutcomeCardForReview = true;
                }
            }
            else
            {
                float bottomY = mvpRect.yMax + 14f;
                float btnW = (width - 40f - 12f) / 3f;

                Rect rewindBtn = new Rect(rect.x + 20f, bottomY, btnW, 30f);
                if (DrawTacticalButton(rewindBtn, "REMBOBINER", null, false, ColorCyanAccent))
                {
                    _arena?.RewindLastSnapshot();
                }

                Rect resetBtn = new Rect(rewindBtn.xMax + 6f, bottomY, btnW, 30f);
                if (DrawTacticalButton(resetBtn, "NOUVEL ESSAI", null, false, accent))
                {
                    _arena?.ResetArena();
                }

                Rect observeBtn = new Rect(resetBtn.xMax + 6f, bottomY, btnW, 30f);
                if (DrawTacticalButton(observeBtn, "OBSERVER", null, false, ColorTextMuted))
                {
                    _hideOutcomeCardForReview = true;
                }
            }
        }

        private static void DrawMetricCell(Rect r, string label, string mainValue, string subValue, Color accentColor)
        {
            DrawSolidRect(r, new Color(0.02f, 0.038f, 0.06f, 0.85f));
            DrawAccentLine(new Rect(r.x, r.y, 2f, r.height), accentColor, 0.8f);

            var labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 8,
                alignment = TextAnchor.UpperLeft,
                fontStyle = FontStyle.Normal
            };
            labelStyle.normal.textColor = ColorTextMuted;

            var valStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft,
                fontStyle = FontStyle.Bold
            };
            valStyle.normal.textColor = accentColor;

            var subStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 8,
                alignment = TextAnchor.LowerLeft,
                fontStyle = FontStyle.Normal
            };
            subStyle.normal.textColor = new Color(0.72f, 0.82f, 0.92f, 0.85f);

            GUI.Label(new Rect(r.x + 8f, r.y + 2f, r.width - 12f, 12f), label, labelStyle);
            GUI.Label(new Rect(r.x + 8f, r.y + 12f, r.width - 12f, 18f), mainValue, valStyle);
            GUI.Label(new Rect(r.x + 8f, r.y + 28f, r.width - 12f, 14f), subValue, subStyle);
        }

        // =========================================================================
        // PRIMITIVES DE RENDU GRAPHIQUE ÉPURÉ (Vector-Like HUD)
        // =========================================================================

        /// <summary>
        /// Dessine un cadre tactique moderne avec des encoches aux coins (Bracketed Corners).
        /// </summary>
        private static void DrawSoftPanel(Rect r, Color bg, bool withShadow = true)
        {
            if (withShadow)
                DrawSolidRect(new Rect(r.x, r.y + 2f, r.width, r.height),
                    new Color(0f, 0f, 0f, bg.a * 0.24f));

            DrawSolidRect(r, bg);
            DrawAccentLine(new Rect(r.x, r.y, r.width, 1f),
                new Color(1f, 1f, 1f, 0.035f), 1f);
        }

        private static void DrawAccentLine(Rect r, Color color, float alpha)
        {
            Color c = color;
            c.a *= alpha;
            DrawSolidRect(r, c);
        }

        private static void DrawTacticalFrame(Rect r, Color bg, Color border, float cornerLen, float thickness)
        {
            DrawSoftPanel(r, bg);
            DrawAccentLine(new Rect(r.x, r.y, cornerLen, thickness), border, 0.70f);
            DrawAccentLine(new Rect(r.x, r.y, thickness, cornerLen), border, 0.70f);
            DrawAccentLine(new Rect(r.x + r.width - cornerLen, r.y, cornerLen, thickness), border, 0.70f);
            DrawAccentLine(new Rect(r.x + r.width - thickness, r.y, thickness, cornerLen), border, 0.70f);
        }

        private static bool DrawTacticalButton(Rect r, string label, string badge, bool active, Color accent, bool enabled = true)
        {
            bool hovered = enabled && r.Contains(Event.current.mousePosition);

            DrawSolidRect(r, active
                ? new Color(accent.r, accent.g, accent.b, 0.13f)
                : new Color(1f, 1f, 1f, hovered ? 0.055f : 0.018f));

            float lineHeight = (active || hovered) ? 2f : 1f;
            DrawAccentLine(new Rect(r.x, r.y + r.height - lineHeight, r.width, lineHeight),
                accent,
                enabled ? (active ? 0.90f : hovered ? 0.55f : 0.16f) : 0.08f);

            GUI.color = enabled
                ? (active || hovered ? ColorTextBright : new Color(0.88f, 0.92f, 0.97f, 0.84f))
                : new Color(0.40f, 0.45f, 0.50f, 0.42f);

            bool clicked = enabled && GUI.Button(r, label, _btnFlatNormal);

            if (!string.IsNullOrEmpty(badge))
            {
                GUI.color = enabled ? ColorTextMuted : new Color(0.35f, 0.40f, 0.45f, 0.35f);
                GUI.Label(new Rect(r.x + r.width - 56f, r.y + 8f, 50f, 14f), badge, _hudStatStyle);
            }

            GUI.color = Color.white;
            return clicked;
        }

        private static void DrawSolidRect(Rect r, Color c)
        {
            Color prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, _pixelTex);
            GUI.color = prev;
        }
    }
}
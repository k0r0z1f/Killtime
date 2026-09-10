using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Core.Combat;
using Killtime.Core.Character;
using Killtime.Core.Dice;
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

        private readonly List<CombatLogEntry> _logEntries = new();
        private Vector2 _logScroll;
        private LogCategory _activeCategory = LogCategory.All;
        private bool _isLogDrawerExpanded = false;

        private BodyPart _selectedBodyPart = BodyPart.Torse;
        private bool _cancelPenaltyWithAP = false;
        private bool _showAnatomyDrawer = false;
        private float _prePauseTimeScale = 1.0f;

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

        public event Action<BodyPart, bool> OnAttackRequested;
        public event Action OnEndTurnRequested;
        public event Action OnEmergencyBreathRequested;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInitialize()
        {
            if (Instance == null && FindAnyObjectByType<CombatHUD>() == null)
            {
                var go = new GameObject("[UI] MinimalistTacticalHUD");
                go.AddComponent<CombatHUD>();
            }
        }

        private void Awake()
        {
            if (Instance == null) Instance = this;
            EnsureReferences();
            InitProceduralTextures();
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
            }

            if (Input.GetKeyDown(KeyCode.L))
            {
                _isLogDrawerExpanded = !_isLogDrawerExpanded;
            }

            if (Input.GetKeyDown(KeyCode.Space))
            {
                TriggerEndTurn();
            }

            float dt = Time.unscaledDeltaTime;
            _hudPulse += dt;
            _anatomyOpenAnim = Mathf.MoveTowards(_anatomyOpenAnim, _showAnatomyDrawer ? 1f : 0f, dt * 10f);
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
            _logScroll.y = float.MaxValue;
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
                AddAdvancedLog("⏸ Stase tactique engagée.", LogCategory.MovementAndTurns, "[STASE]", ColorCyanAccent);
            }
            else
            {
                Time.timeScale = _prePauseTimeScale;
                AudioListener.pause = false;
                AddAdvancedLog("▶ Reprise de la dynamique causale.", LogCategory.MovementAndTurns, "[REPRISE]", Color.green);
            }
        }

        private void OnDestroy()
        {
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

            if (_turnManager != null && _turnManager.IsCombatOver)
            {
                DrawOutcomeCard();
                DrawAdvancedLogSystem(isPauseActive: true);
                return;
            }

            var activeUnit = _turnManager != null ? _turnManager.ActiveUnit : (_arena != null ? _arena.PlayerUnit : null);
            if (activeUnit == null || activeUnit.Stats == null) return;

            // 2. HUD minimaliste : Joueur & Cible
            DrawTacticalPlayerCard(activeUnit);
            DrawTacticalTargetCard();

            // 3. Ruban d'actions en bas d'écran (Action Ribbon flottant)
            DrawFloatingActionRibbon(activeUnit);

            // 4. Matrice de ciblage anatomique (VATS épuré)
            if (_showAnatomyDrawer)
            {
                DrawSurgicalTargetingMatrix(activeUnit);
            }

            // 5. Flux d'informations (Logs)
            DrawAdvancedLogSystem(isPauseActive: false);
        }

        private void EnsureStyles()
        {
            if (_hudNameStyle != null) return;

            _hudNameStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                richText = false
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
                wordWrap = true
            };
            _terminalBodyStyle.normal.textColor = new Color(0.86f, 0.90f, 0.95f, 0.90f);
        }

        // =========================================================================
        // CARTE JOUEUR — FICHE DE STATUT DISCRÈTE
        // =========================================================================
        private void DrawTacticalPlayerCard(TacticalUnit unit)
        {
            float width = Mathf.Clamp(Screen.width * 0.27f, 250f, 340f);
            Rect rect = new Rect(24f, 22f, width, 62f);
            bool hovered = rect.Contains(Event.current.mousePosition);

            DrawSoftPanel(rect, new Color(0.025f, 0.045f, 0.065f, hovered ? 0.70f : 0.56f));
            DrawAccentLine(new Rect(rect.x, rect.y, rect.width, 2f),
                Color.Lerp(ColorCyanDim, ColorCyanAccent, hovered ? 1f : 0.62f), 0.78f);

            GUI.Label(new Rect(rect.x + 14, rect.y + 7, width - 150, 19),
                unit.Stats.Name.ToUpperInvariant(), _hudNameStyle);

            int round = _turnManager != null ? _turnManager.CurrentRound : 1;
            GUI.color = ColorTextMuted;
            GUI.Label(new Rect(rect.x + width - 88, rect.y + 8, 74, 16),
                $"ROUND {round:00}", _hudSubStyle);
            GUI.color = Color.white;

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
            GUI.Label(new Rect(rect.x + 14, apY - 2, 32, 16), "PA", _hudSubStyle);
            GUI.color = Color.white;

            float pipW = Mathf.Min(15f, (width - 98f) / maxAp);
            for (int i = 0; i < maxAp; i++)
            {
                float x = rect.x + 44f + i * (pipW + 3f);
                bool filled = i < curAp;
                float pulse = filled ? 0.76f + 0.24f * Mathf.Sin(_hudPulse * 4f + i) : 0f;
                DrawSolidRect(new Rect(x, apY + 2f, pipW, 4f),
                    filled ? new Color(ColorCyanAccent.r, ColorCyanAccent.g, ColorCyanAccent.b, pulse)
                           : new Color(0.13f, 0.20f, 0.26f, 0.38f));
            }

            GUI.color = ColorTextBright;
            GUI.Label(new Rect(rect.x + width - 50, apY - 3, 38, 17),
                $"{curAp}/{maxAp}", _hudStatStyle);
            GUI.color = Color.white;

            if (unit.Stats.Essoufflement > 0)
            {
                GUI.color = ColorAmber;
                GUI.Label(new Rect(rect.x + width - 138, rect.y + 8, 45, 16), "SOUFF.", _hudSubStyle);
                GUI.color = Color.white;
            }
        }

        // =========================================================================
        // CARTE CIBLE — PRESQUE INVISIBLE HORS CONTEXTE
        // =========================================================================
        private void DrawTacticalTargetCard()
        {
            if (_arena == null || _arena.CurrentTarget == null || !_arena.CurrentTarget.Stats.IsAlive) return;

            var target = _arena.CurrentTarget;
            float width = Mathf.Clamp(Screen.width * 0.22f, 210f, 300f);
            Rect rect = new Rect(Screen.width - width - 24f, 22f, width, 56f);
            bool hovered = rect.Contains(Event.current.mousePosition);

            DrawSoftPanel(rect, new Color(0.055f, 0.025f, 0.035f, hovered ? 0.70f : 0.54f));
            DrawAccentLine(new Rect(rect.x + rect.width * 0.55f, rect.y,
                rect.width * 0.45f, 2f), ColorCrimson, 0.9f);

            GUI.color = ColorCrimson;
            GUI.Label(new Rect(rect.x + 12, rect.y + 7, width - 80, 19),
                target.Stats.Name.ToUpperInvariant(), _hudNameStyle);
            GUI.color = ColorTextMuted;
            GUI.Label(new Rect(rect.x + width - 66, rect.y + 8, 54, 15),
                $"ARM {target.Stats.BaseArmorAbsorption}", _hudSubStyle);

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
        // RUBAN D'ACTIONS — DOCK MINIMAL
        // =========================================================================
        private void DrawFloatingActionRibbon(TacticalUnit unit)
        {
            float width = Mathf.Clamp(Screen.width * 0.40f, 360f, 520f);
            float height = 46f;
            float bottom = 18f + (_showAnatomyDrawer ? 214f : 0f);
            Rect dock = new Rect((Screen.width - width) * 0.5f,
                Screen.height - height - bottom, width, height);

            DrawSoftPanel(dock, new Color(0.02f, 0.035f, 0.05f, 0.72f));
            DrawAccentLine(new Rect(dock.x + 28f, dock.y, dock.width - 56f, 1f), ColorCyanDim, 1f);

            float gap = 5f;
            float bw = (width - 14f - gap * 2f) / 3f;
            float y = dock.y + 6f;

            Rect vats = new Rect(dock.x + 7f, y, bw, 34f);
            if (DrawTacticalButton(vats, "CIBLAGE", "1", _showAnatomyDrawer, ColorCyanAccent))
                _showAnatomyDrawer = !_showAnatomyDrawer;

            Rect breath = new Rect(vats.xMax + gap, y, bw, 34f);
            bool canBreath = unit.Stats.Essoufflement < unit.Stats.Attributes.Constitution;
            if (DrawTacticalButton(breath, "SOUFFLE", "+2", false, ColorAmber, canBreath))
            {
                if (unit.Stats.TakeEmergencyBreath(2))
                {
                    AddAdvancedLog($"{unit.Stats.Name} force sa ventilation d'urgence (+2 PA) !",
                        LogCategory.ReactionAndAP, "[SOUFFLE]", ColorAmber);
                    OnEmergencyBreathRequested?.Invoke();
                }
            }

            Rect endTurn = new Rect(breath.xMax + gap, y, bw, 34f);
            if (DrawTacticalButton(endTurn, "PASSER", "ESPACE", false, ColorCrimson))
                TriggerEndTurn();
        }

        // =========================================================================
        // MATRICE DE CIBLAGE — DRAWER COMPACT
        // =========================================================================
        private void DrawSurgicalTargetingMatrix(TacticalUnit unit)
        {
            float width = Mathf.Clamp(Screen.width * 0.34f, 330f, 460f);
            float height = 216f;
            float targetY = Screen.height - height - 76f;
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
                    _selectedBodyPart = p;

                rowY += rowH + 2f;
            }

            GUI.color = ColorTextMuted;
            _cancelPenaltyWithAP = GUI.Toggle(
                new Rect(rect.x + 14f, rowY + 1f, width - 28f, 18f),
                _cancelPenaltyWithAP,
                "Visée compensée  ·  +1 PA", _hudSubStyle);
            GUI.color = Color.white;

            int apCost = _cancelPenaltyWithAP ? 3 : 2;
            bool canAttack = unit.Stats.CurrentActionPoints >= apCost && unit.Stats.CanAttack(DiceType.D6);

            Rect fire = new Rect(rect.x + 12f, rect.y + height - 38f, width - 24f, 28f);
            if (DrawTacticalButton(fire,
                canAttack ? $"TIR  ·  {apCost} PA" : "TIR INDISPONIBLE",
                null, false, canAttack ? ColorCrimson : ColorTextMuted, canAttack))
            {
                _showAnatomyDrawer = false;
                _arena?.ExecuteAttack(_selectedBodyPart, _cancelPenaltyWithAP);
                OnAttackRequested?.Invoke(_selectedBodyPart, _cancelPenaltyWithAP);
            }
        }

        private void TriggerEndTurn()
        {
            _showAnatomyDrawer = false;
            _turnManager?.EndCurrentTurn();
            OnEndTurnRequested?.Invoke();
        }


        // =========================================================================
        // SYSTÈME DE LOGS — FEED LÉGER
        // =========================================================================
        private void DrawAdvancedLogSystem(bool isPauseActive)
        {
            float drawerWidth = Mathf.Clamp(Screen.width * 0.34f, 390f, 520f);
            float drawerHeight = Mathf.Clamp(Screen.height * 0.31f, 190f, 270f);
            float margin = 20f;

            if (!_isLogDrawerExpanded && !isPauseActive)
            {
                DrawFloatingEphemeralLogs();

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

            GUI.Label(new Rect(terminal.x + 14, terminal.y + 10, terminal.width - 150, 18),
                "FLUX TACTIQUE", _terminalHeaderStyle);

            GUI.color = ColorTextMuted;
            if (GUI.Button(new Rect(terminal.x + terminal.width - 130, terminal.y + 8, 58, 20),
                "EFFACER", _btnFlatNormal))
                _logEntries.Clear();

            if (GUI.Button(new Rect(terminal.x + terminal.width - 68, terminal.y + 8, 56, 20),
                "FERMER", _btnFlatNormal))
                _isLogDrawerExpanded = false;
            GUI.color = Color.white;

            Rect filters = new Rect(terminal.x + 12, terminal.y + 34, terminal.width - 24, 20);
            float fx = filters.x;
            DrawFilterPill(new Rect(fx, filters.y, 48, 18), "TOUT", LogCategory.All); fx += 51;
            DrawFilterPill(new Rect(fx, filters.y, 58, 18), "COMBAT", LogCategory.Combat); fx += 61;
            DrawFilterPill(new Rect(fx, filters.y, 42, 18), "PA", LogCategory.ReactionAndAP); fx += 45;
            DrawFilterPill(new Rect(fx, filters.y, 68, 18), "TRAUMA", LogCategory.VitalityAndTrauma); fx += 71;
            DrawFilterPill(new Rect(fx, filters.y, 52, 18), "PHASE", LogCategory.MovementAndTurns);

            Rect scrollRect = new Rect(terminal.x + 12, terminal.y + 58, terminal.width - 24, terminal.height - 70);
            _logScroll = GUI.BeginScrollView(scrollRect, _logScroll,
                new Rect(0, 0, terminal.width - 38, Mathf.Max(1f, _logEntries.Count * 23f)));

            float y = 2f;
            for (int i = 0; i < _logEntries.Count; i++)
            {
                var entry = _logEntries[i];
                if (_activeCategory != LogCategory.All && entry.Category != _activeCategory) continue;

                GUI.color = entry.TagColor;
                GUI.Label(new Rect(0, y, 72, 18), entry.HeaderTag, _terminalHeaderStyle);
                GUI.color = ColorTextMuted;
                GUI.Label(new Rect(75, y, 42, 18), entry.Timestamp, _hudSubStyle);
                GUI.color = ColorTextBright;
                GUI.Label(new Rect(120, y, terminal.width - 160, 18), entry.RawMessage, _terminalBodyStyle);
                GUI.color = Color.white;
                y += 23f;
            }
            GUI.EndScrollView();
        }

        private void DrawFilterPill(Rect r, string label, LogCategory cat)
        {
            bool selected = _activeCategory == cat;
            bool hover = r.Contains(Event.current.mousePosition);
            DrawSoftPanel(r,
                selected ? new Color(0f, 0.75f, 0.9f, 0.15f)
                         : new Color(1f, 1f, 1f, hover ? 0.05f : 0.018f), false);

            GUI.color = selected ? ColorCyanAccent : (hover ? ColorTextBright : ColorTextMuted);
            if (GUI.Button(r, label, _btnFlatNormal)) _activeCategory = cat;
            GUI.color = Color.white;
        }

        // =========================================================================
        // BOUTON PAUSE EN HAUT À DROITE
        // =========================================================================
        private void DrawFloatingEphemeralLogs()
        {
            float width = Mathf.Clamp(Screen.width * 0.34f, 360f, 500f);
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
                GUI.Label(line, $"{entry.HeaderTag}  {entry.RawMessage}", _floatingLogStyle);

                yOffset -= 20f;
                drawn++;
            }
        }

        private void DrawTopRightPausePill()
        {
            float width = 84f;
            Rect rect = new Rect(Screen.width - width - 20f, 20f, width, 22f);
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
            bool isVictory = _turnManager.CurrentOutcome == CombatOutcome.Victory;
            Color accent = isVictory ? ColorCyanAccent : ColorCrimson;
            float width = 420f;
            float height = 164f;

            DrawSolidRect(new Rect(0, 0, Screen.width, Screen.height),
                new Color(0.005f, 0.010f, 0.018f, 0.45f));

            Rect rect = new Rect((Screen.width - width) * 0.5f,
                (Screen.height - height) * 0.44f, width, height);

            DrawSoftPanel(rect, new Color(0.018f, 0.032f, 0.05f, 0.97f));
            DrawAccentLine(new Rect(rect.x, rect.y, rect.width, 2f), accent, 1f);

            GUI.color = accent;
            GUI.Label(new Rect(rect.x + 24, rect.y + 22, width - 48, 25),
                isVictory ? "ENGAGEMENT TERMINÉ" : "ENGAGEMENT PERDU", _hudNameStyle);

            GUI.color = ColorTextMuted;
            GUI.Label(new Rect(rect.x + 24, rect.y + 53, width - 48, 28),
                isVictory ? "Menace neutralisée. Le champ est sécurisé."
                          : "Signaux vitaux rompus. Repositionnement recommandé.",
                _terminalBodyStyle);
            GUI.color = Color.white;

            Rect rewind = new Rect(rect.x + 24, rect.y + 103, (width - 54f) * 0.5f, 30f);
            if (DrawTacticalButton(rewind, "REMBOBINER", null, false, ColorCyanAccent))
                _arena?.RewindLastSnapshot();

            Rect reset = new Rect(rewind.xMax + 6f, rewind.y, rewind.width, 30f);
            if (DrawTacticalButton(reset, "NOUVEL ESSAI", null, false, accent))
                _arena?.ResetArena();
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
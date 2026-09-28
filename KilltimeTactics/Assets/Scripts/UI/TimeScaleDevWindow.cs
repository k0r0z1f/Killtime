using UnityEngine;

namespace Killtime.UI
{
    /// <summary>
    /// Fenêtre de gestion de la vitesse du jeu (ex-ralentisseur de scène F10).
    /// Plage 0.01x (100x plus lent) à 100x (100x plus rapide), slider logarithmique
    /// + presets + saisie directe. Pilote Time.timeScale / Time.fixedDeltaTime et
    /// persiste la consigne dans DevUIPreferences.GlobalTimeScale.
    /// Compatibilités : la pause CombatHUD (timeScale 0) conserve la consigne pour
    /// la reprise, les cinématiques forcent un 0.45x temporaire puis restaurent
    /// cette consigne via CinematicDirector.
    /// </summary>
    public class TimeScaleDevWindow : FloatingWindow<TimeScaleDevWindow>
    {
        protected override int WindowId => 997;
        protected override string Title => "Vitesse du Jeu (F10)";
        protected override Vector2 MinSize => new Vector2(320f, 300f);
        protected override Rect DefaultRect => new Rect(600f, 100f, 360f, 430f);
        protected override KeyCode[] ToggleKeys => _toggleKeys;

        private static readonly KeyCode[] _toggleKeys = { KeyCode.F10 };

        public const float MinScale = 0.01f;
        public const float MaxScale = 100f;
        private const float LogMin = -2f; // log10(0.01)
        private const float LogMax = 2f;  // log10(100)

        /// <summary>Consigne désirée (persistée). Jamais 0 : la pause utilise Time.timeScale = 0 séparément.</summary>
        public static float CurrentTimeScale { get; private set; } = 1f;

        /// <summary>Dernier ralenti &lt; 1x (compatibilité avec l'ancien toggle F10 1x / ralenti).</summary>
        public static float LastSlowedScale { get; private set; } = 0.25f;

        private string _inputText = "1";
        private Vector2 _scrollPos;

        private static Rect ClosedPillRect => new Rect(130f, Screen.height - 52f, 120f, 22f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void ApplySavedPrefAfterSceneLoad()
        {
            try
            {
                float saved = ReadTimeScalePref();
                if (!Mathf.Approximately(saved, CurrentTimeScale))
                {
                    CurrentTimeScale = saved;
                    if (saved < 1f) LastSlowedScale = saved;
                }
                // N'applique au moteur que si personne n'a déjà pris la main
                // (pause à 0 ou cinématique à 0.45) : ne jamais écraser un état live.
                if (!CombatHUD.IsPaused && Time.timeScale > 0.001f && Mathf.Abs(Time.timeScale - 0.45f) > 0.001f)
                    ApplyToUnity(saved);
            }
            catch { /* l'application ne doit jamais échouer au chargement */ }
        }

        protected override void Awake()
        {
            base.Awake();
            try
            {
                CurrentTimeScale = ReadTimeScalePref();
                if (CurrentTimeScale < 1f) LastSlowedScale = CurrentTimeScale;
                _inputText = FormatNumber(CurrentTimeScale);
                if (CombatHUD.IsPaused)
                    CombatHUD.UpdatePrePauseTimeScale(CurrentTimeScale);
                else
                    ApplyToUnity(CurrentTimeScale);
            }
            catch { /* prefs optionnelles */ }
            // Comme CombatDevToolbar : la pilule fermée bloque aussi les clics 3D.
            FloatingWindowChrome.RegisterWindow(997,
                () => _isOpen ? _windowRect : ClosedPillRect,
                () => isActiveAndEnabled);
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            FloatingWindowChrome.RegisterWindow(997,
                () => _isOpen ? _windowRect : ClosedPillRect,
                () => isActiveAndEnabled);
        }

        protected override void OnOpened()
        {
            try
            {
                CurrentTimeScale = ReadTimeScalePref();
                if (Mathf.Abs(Time.timeScale) > 0.001f && Mathf.Abs(Time.timeScale - 0.45f) > 0.02f
                    && Time.timeScale >= MinScale && Time.timeScale <= MaxScale)
                    CurrentTimeScale = Time.timeScale;
                _inputText = FormatNumber(CurrentTimeScale);
            }
            catch { /* ignore */ }
        }

        protected override void OnUpdate()
        {
            base.OnUpdate();
            // Shift+F10 : reset express 1x même fenêtre fermée (base exige sans modificateur pour le toggle).
            bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool ctrlHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (shiftHeld && !ctrlHeld && Input.GetKeyDown(KeyCode.F10))
                SetTimeScale(1f);
        }

        /// <summary>
        /// Applique une vitesse : clamp 0.01–100, persiste, pousse vers le moteur
        /// sauf en pause (la consigne est alors mémorisée pour la reprise).
        /// </summary>
        public static void SetTimeScale(float scale)
        {
            scale = Mathf.Clamp(scale, MinScale, MaxScale);
            CurrentTimeScale = scale;
            if (scale < 1f) LastSlowedScale = scale;
            try
            {
                var p = DevUIPreferences.Current;
                if (p != null)
                {
                    p.GlobalTimeScale = scale;
                    DevUIPreferences.MarkDirty();
                }
            }
            catch { /* prefs optionnelles */ }

            if (CombatHUD.IsPaused)
            {
                // Reste en stase (0) mais la reprise utilisera la nouvelle consigne.
                CombatHUD.UpdatePrePauseTimeScale(scale);
                return;
            }
            ApplyToUnity(scale);
        }

        public static float ReadTimeScalePref()
        {
            try
            {
                var p = DevUIPreferences.Current;
                if (p != null) return Mathf.Clamp(p.GlobalTimeScale, MinScale, MaxScale);
            }
            catch { /* ignore */ }
            return 1f;
        }

        public static void ResetToNormal() => SetTimeScale(1f);

        private static void ApplyToUnity(float scale)
        {
            try
            {
                Time.timeScale = scale;
                // fixedDeltaTime suit l'échelle mais reste borné : à 100x, 2s par pas
                // physiqueTunneliserait tout ; 0.05s suffit pour un fast-forward stable.
                Time.fixedDeltaTime = Mathf.Clamp(0.02f * scale, 0.0002f, 0.05f);
            }
            catch { /* ignore */ }
        }

        protected override void DrawClosedState()
        {
            Color prevBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.02f, 0.035f, 0.05f, 0.65f);
            string label = $"⏳ {FormatScale(CurrentTimeScale)} (F10)";
            if (GUI.Button(ClosedPillRect, label))
                OpenInstance();
            GUI.backgroundColor = prevBg;
        }

        protected override void DrawContent()
        {
            _scrollPos = GUILayout.BeginScrollView(_scrollPos);

            // --- Statut ---
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUI.color = CurrentTimeScale < 1f ? new Color(1f, 0.6f, 0.2f)
                : CurrentTimeScale > 1f ? new Color(0.4f, 0.9f, 1f) : Color.white;
            GUILayout.Label($"<b><size=16>⏳ {FormatScale(CurrentTimeScale)}</size></b>  <i>{ModeLabel(CurrentTimeScale)}</i>");
            GUI.color = Color.white;
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("▶ 1×", GUILayout.Width(52), GUILayout.Height(26)))
            {
                SetTimeScale(1f);
                _inputText = FormatNumber(1f);
            }
            GUILayout.EndHorizontal();

            float live = Time.timeScale;
            if (CombatHUD.IsPaused)
                GUILayout.Label("<i>⏸ Pause active — la nouvelle vitesse s'appliquera à la reprise.</i>");
            else if (Mathf.Abs(live - CurrentTimeScale) > Mathf.Max(0.001f, CurrentTimeScale * 0.01f))
                GUILayout.Label($"<i>Réel moteur : {FormatScale(live)}{(Mathf.Abs(live - 0.45f) < 0.01f ? " (🎬 cinématique temporaire)" : "")} — consigne restaurée ensuite.</i>");
            GUILayout.EndVertical();

            GUILayout.Space(6);

            // --- Slider logarithmique ---
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("<b>Échelle logarithmique (0.01× → 100×) :</b>");
            float log = Mathf.Log10(Mathf.Clamp(CurrentTimeScale, MinScale, MaxScale));
            float newLog = GUILayout.HorizontalSlider(log, LogMin, LogMax);
            if (Mathf.Abs(newLog) < 0.02f) newLog = 0f; // aimant 1×
            if (Mathf.Abs(newLog - log) > 0.0005f)
            {
                SetTimeScale(Mathf.Pow(10f, newLog));
                _inputText = FormatNumber(CurrentTimeScale);
            }
            GUILayout.BeginHorizontal();
            GUILayout.Label("0.01×", GUILayout.Width(40));
            GUILayout.FlexibleSpace();
            GUILayout.Label("1×");
            GUILayout.FlexibleSpace();
            GUILayout.Label("100×", GUILayout.Width(40));
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.Space(6);

            // --- Saisie directe ---
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("<b>Valeur exacte :</b>");
            GUILayout.BeginHorizontal();
            _inputText = GUILayout.TextField(_inputText, GUILayout.Width(90));
            if (GUILayout.Button("Appliquer", GUILayout.Width(90)))
            {
                string normalized = (_inputText ?? "").Trim().Replace(',', '.');
                if (float.TryParse(normalized, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float parsed))
                {
                    SetTimeScale(parsed);
                    _inputText = FormatNumber(CurrentTimeScale);
                }
            }
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("−", GUILayout.Width(30))) { SetTimeScale(CurrentTimeScale / 1.25f); _inputText = FormatNumber(CurrentTimeScale); }
            if (GUILayout.Button("+", GUILayout.Width(30))) { SetTimeScale(CurrentTimeScale * 1.25f); _inputText = FormatNumber(CurrentTimeScale); }
            GUILayout.EndHorizontal();
            GUILayout.Label("<i>± pas fins (×/÷ 1.25). Plage 0.01 à 100.</i>");
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.Space(6);

            // --- Presets ralenti ---
            GUILayout.Label("<b>🐌 Ralenti :</b>");
            GUILayout.BeginHorizontal();
            DrawPresetButton(0.01f);
            DrawPresetButton(0.05f);
            DrawPresetButton(0.1f);
            DrawPresetButton(0.25f);
            DrawPresetButton(0.5f);
            GUILayout.EndHorizontal();

            // --- Presets accéléré ---
            GUILayout.Label("<b>⚡ Accéléré :</b>");
            GUILayout.BeginHorizontal();
            DrawPresetButton(1f);
            DrawPresetButton(2f);
            DrawPresetButton(5f);
            DrawPresetButton(10f);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            DrawPresetButton(25f);
            DrawPresetButton(50f);
            DrawPresetButton(100f);
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label("<i>Ralentit/accélère tout (anims, cinématiques, IA, projectiles). F10 = fenêtre, Shift+F10 = 1×. Audio : seul le bullet-time &lt; 0.7× adoucit les SFX, la musique reste à pitch 1.</i>");

            GUILayout.EndScrollView();
        }

        private void DrawPresetButton(float scale)
        {
            bool active = Mathf.Approximately(CurrentTimeScale, scale)
                || Mathf.Abs(CurrentTimeScale - scale) < scale * 0.001f;
            Color prev = GUI.backgroundColor;
            if (active) GUI.backgroundColor = new Color(0.2f, 0.7f, 1f);
            if (GUILayout.Button(FormatScale(scale), GUILayout.Height(26)))
            {
                SetTimeScale(scale);
                _inputText = FormatNumber(CurrentTimeScale);
            }
            GUI.backgroundColor = prev;
        }

        public static string FormatScale(float scale)
        {
            return $"{FormatNumber(scale)}×";
        }

        public static string FormatNumber(float scale)
        {
            string s = scale >= 10f ? $"{scale:0.#}" : $"{scale:0.##}";
            return s.Replace(',', '.');
        }

        private static string ModeLabel(float scale)
        {
            if (scale < 0.99f) return "ralenti";
            if (scale > 1.01f) return "accéléré";
            return "normal";
        }
    }
}

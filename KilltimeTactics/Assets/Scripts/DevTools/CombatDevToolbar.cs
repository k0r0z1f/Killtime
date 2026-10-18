using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics;
using Killtime.Tactics.AI;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Tactics.Objectives;
using Killtime.Core.Chrono;
using Killtime.Core.Combat;
using Killtime.Core.Dice;
using Killtime.Core.Character;
using Killtime.CameraSystem;
using Killtime.Multi;
using Killtime.Story;

namespace Killtime.UI
{
    /// <summary>
    /// Barre d'outils et banc d'essai de développement pour le combat tactique.
    /// Conforme à l'Axiome Fondateur du Codex : Tout jet découle d'une compétence.
    /// </summary>
    public partial class CombatDevToolbar : FloatingWindow<CombatDevToolbar>
    {
        protected override int WindowId => 999;
        protected override string Title => "Dev Arena — Combat & VATS";
        protected override Vector2 MinSize => _minSize;
        protected override Rect DefaultRect => new Rect(20f, 100f, 560f, Mathf.Min(700f, Screen.height - 116f));
        protected override KeyCode[] ToggleKeys => _toggleKeys;

        private static readonly KeyCode[] _toggleKeys = { KeyCode.BackQuote, KeyCode.Tab };
        private static readonly Vector2 _minSize = new Vector2(360, 220);

        /// <summary>
        /// Bouton de réouverture quand la fenêtre est fermée : sous la carte joueur HUD
        /// (carte en 24,22,340x62 → bas à y=84, zone d'exclusion jusqu'à 88),
        /// donc y=96 garantit 8px sans chevauchement.
        /// </summary>
        private static Rect ClosedButtonRect => new Rect(24f, 96f, 180f, 28f);

        [Header("Contrôleur")]
        [SerializeField] private CombatDevArena _arena;
        [SerializeField] private TurnManager _turnManager;
        [SerializeField] private CinematicDirector _cinematicDirector;

        private readonly List<string> _logs = new();
        private Vector2 _logScroll;
        private Vector2 _toolsScroll;
        private bool _scrollLock = false;
        // Même pattern robuste que CombatHUD : la demande de retour en bas est
        // consommée dans le Draw, juste avant le BeginScrollView. Quand le verrou
        // est actif, _logScroll n'est jamais modifié : la lecture reste figée.
        private bool _logScrollToBottomPending = false;

        private BodyPart _selectedPart = BodyPart.Torse;
        private bool _cancelPenaltyWithAP = false;
        private int _attackerBonusAP = 0;
        private int _defenderBonusAP = 0;
        private int _attackerPE = 0;
        private int _defenderPE = 0;
        private SkillType _attackSkill = SkillType.Ballistique;
        private SkillType _defenseSkill = SkillType.Esquive;
        private bool _defenderWantsToDefend = true;
        private int _weaponDamage = 5;

        // Vitesse du jeu : pilotée par TimeScaleDevWindow (F10, 0.01x–100x).
        // Les wrappers CurrentDevTimeScale / SetDevTimeScale / ReadDevTimeScalePref
        // ci-dessous délèguent vers cette fenêtre (compat cinématiques / pause).

        private int _selectedTab = 0;
        private readonly string[] _tabNames = { "🎯 Tir Ciblé (VATS)", "🛠️ Outils & Cheats", "⏳ Chronomancie", "📜 Logs" };

        private static bool _devFullbrightActive = false;
        private static Light _devFullbrightLight;
        private static Color _cachedAmbientLight;
        private static UnityEngine.Rendering.AmbientMode _cachedAmbientMode;
        private const string DevLightName = "__DEV_FULLBRIGHT_LIGHT__";

        public static void ToggleDevFullbrightLight()
        {
            _devFullbrightActive = !_devFullbrightActive;

            if (_devFullbrightLight == null)
            {
                GameObject lightGo = GameObject.Find(DevLightName);
                if (lightGo == null)
                {
                    lightGo = new GameObject(DevLightName);
                    lightGo.transform.rotation = Quaternion.Euler(55f, -35f, 0f);
                }
                _devFullbrightLight = lightGo.GetComponent<Light>() ?? lightGo.AddComponent<Light>();
                _devFullbrightLight.type = LightType.Directional;
                _devFullbrightLight.cullingMask = ~0;
            }

            if (_devFullbrightActive)
            {
                _cachedAmbientLight = RenderSettings.ambientLight;
                _cachedAmbientMode = RenderSettings.ambientMode;

                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(0.72f, 0.72f, 0.72f, 1.0f);

                _devFullbrightLight.color = Color.white;
                _devFullbrightLight.intensity = 2.4f;
                _devFullbrightLight.shadows = LightShadows.None;
                _devFullbrightLight.enabled = true;

                CombatHUD.Instance?.AddAdvancedLog("💡 <b>[DEV] Lumière Globale : ACTIVÉE (Touche L)</b>", LogCategory.MovementAndTurns, "[ÉCLAIRAGE]", Color.yellow);
            }
            else
            {
                if (_devFullbrightLight != null)
                {
                    _devFullbrightLight.enabled = false;
                }

                RenderSettings.ambientMode = _cachedAmbientMode;
                RenderSettings.ambientLight = _cachedAmbientLight;

                CombatHUD.Instance?.AddAdvancedLog("💡 <b>[DEV] Lumière Globale : DÉSACTIVÉE (Touche L)</b>", LogCategory.MovementAndTurns, "[ÉCLAIRAGE]", Color.gray);
            }
        }

        protected override void Awake()
        {
            base.Awake();
            if (_arena == null) _arena = FindAnyObjectByType<CombatDevArena>();
            if (_turnManager == null) _turnManager = FindAnyObjectByType<TurnManager>();
            if (_cinematicDirector == null) _cinematicDirector = FindAnyObjectByType<CinematicDirector>();

            try { LoadDevPrefs(); } catch { /* prefs optionnelles */ }

            if (_arena != null)
            {
                _arena.OnCombatLogMessage += AddLog;
            }

            // Bloque les clics/déplacements/zoom 3D quand la souris survole la fenêtre (ou le bouton de réouverture).
            // Bouton fermé sous la carte joueur HUD : voir ClosedButtonRect (sans chevauchement).
            FloatingWindowChrome.RegisterWindow(999,
                () => _isOpen ? _windowRect : ClosedButtonRect,
                () => isActiveAndEnabled);
        }

        protected override void OnOpened()
        {
            try { LoadDevPrefs(); } catch { /* ignore */ }
        }

        protected override void OnClosed()
        {
            try { CaptureDevPrefs(); DevUIPreferences.SaveNow(); } catch { /* ignore */ }
        }

        private void LoadDevPrefs()
        {
            var p = DevUIPreferences.Current;
            if (p == null) return;
            int partCount = Enum.GetValues(typeof(BodyPart)).Length;
            _selectedPart = (BodyPart)Mathf.Clamp(p.VatsSelectedPart, 0, Math.Max(0, partCount - 1));
            _cancelPenaltyWithAP = p.VatsCancelPenalty;
            _attackerBonusAP = Mathf.Clamp(p.VatsAttackerBonus, 0, 10);
            _defenderBonusAP = Mathf.Clamp(p.VatsDefenderBonus, 0, 10);
            _attackSkill = SafeSkill(p.VatsAttackSkill, SkillType.Ballistique);
            _defenseSkill = SafeSkill(p.VatsDefenseSkill, SkillType.Esquive);
            _defenderWantsToDefend = p.VatsDefenderWantsToDefend;
            _weaponDamage = Mathf.Clamp(p.VatsWeaponDamage, 1, 30);
            _selectedTab = Mathf.Clamp(p.CombatToolbarTab, 0, _tabNames.Length - 1);
            _scrollLock = p.CombatLogScrollLock;
            Application.runInBackground = p.RunInBackground;
            // Vitesse du jeu volontairement non restaurée : session uniquement (toujours 1x au redémarrage).
        }

        private static SkillType SafeSkill(int raw, SkillType fallback)
        {
            try
            {
                if (Enum.IsDefined(typeof(SkillType), raw)) return (SkillType)raw;
            }
            catch { /* ignore */ }
            return fallback;
        }

        private void CaptureDevPrefs()
        {
            var p = DevUIPreferences.Current;
            if (p == null) return;
            p.VatsSelectedPart = (int)_selectedPart;
            p.VatsCancelPenalty = _cancelPenaltyWithAP;
            p.VatsAttackerBonus = _attackerBonusAP;
            p.VatsDefenderBonus = _defenderBonusAP;
            p.VatsAttackSkill = (int)_attackSkill;
            p.VatsDefenseSkill = (int)_defenseSkill;
            p.VatsDefenderWantsToDefend = _defenderWantsToDefend;
            p.VatsWeaponDamage = _weaponDamage;
            p.CombatToolbarTab = _selectedTab;
            p.CombatLogScrollLock = _scrollLock;
            p.RunInBackground = Application.runInBackground;
            // Vitesse du jeu volontairement non sauvegardée : session uniquement.
            if (_arena != null)
            {
                p.InfiniteAP = _arena.InfiniteAP;
                p.ArenaLayout = (int)_arena.CurrentLayout;
                try { p.EnableCinematicKillcam = _arena.EnableCinematicKillcam; } catch { /* ignore */ }
                try { p.ShowCoverLineOfSight = _arena.ShowCoverLineOfSight; } catch { /* ignore */ }
            }
            try
            {
                var ai = (_arena != null ? _arena.AIController : null) ?? FindAnyObjectByType<TacticalAIController>();
                if (ai != null)
                {
                    p.AiMode = (int)ai.Mode;
                    p.AiEnabled = ai.IsAIEnabled;
                    p.AiActionDelay = ai.ActionDelay;
                    try
                    {
                        p.AiPersonality = (int)ai.DefaultPersonality;
                        p.AiRetreatRatio = ai.RetreatHealthRatio;
                        p.AiDefensiveReserve = ai.DefensiveAPReserve;
                    }
                    catch { /* ignore */ }
                }
            }
            catch { /* ignore */ }
            DevUIPreferences.MarkDirty();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            FloatingWindowChrome.RegisterWindow(999,
                () => _isOpen ? _windowRect : ClosedButtonRect,
                () => isActiveAndEnabled);
        }

        private void LateUpdate()
        {
            bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool ctrlHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);

            if (shiftHeld && !ctrlHeld)
            {
                if (Input.GetKeyDown(KeyCode.F1)) EnsureDevWindow(ScenarioDevWindow.Instance, ScenarioDevWindow.Open);
                else if (Input.GetKeyDown(KeyCode.F2)) EnsureDevWindow(ScenarioEditorDevWindow.Instance, ScenarioEditorDevWindow.Open);
                else if (Input.GetKeyDown(KeyCode.F3)) EnsureDevWindow(SceneEnvironmentDevWindow.Instance, SceneEnvironmentDevWindow.Open);
                else if (Input.GetKeyDown(KeyCode.F4)) EnsureDevWindow(CinematicEditorDevWindow.Instance, CinematicEditorDevWindow.Open);
            }
            else if (ctrlHeld && !shiftHeld)
            {
                if (Input.GetKeyDown(KeyCode.F1)) EnsureDevWindow(VTTRoomWindow.Instance, VTTRoomWindow.Open);
                else if (Input.GetKeyDown(KeyCode.F2)) EnsureDevWindow(Killtime.Multi.Video.VTTVideoRoomWindow.Instance, Killtime.Multi.Video.VTTVideoRoomWindow.Open);
            }
            else if (!shiftHeld && !ctrlHeld)
            {
                if (Input.GetKeyDown(KeyCode.F1)) EnsureDevWindow(CharacterDevWindow.Instance, CharacterDevWindow.Open);
                else if (Input.GetKeyDown(KeyCode.F2)) EnsureDevWindow(MapEditorDevWindow.Instance, MapEditorDevWindow.Open);
                else if (Input.GetKeyDown(KeyCode.F3)) EnsureDevWindow(CoreRulesDevTableWindow.Instance, CoreRulesDevTableWindow.Open);
                else if (Input.GetKeyDown(KeyCode.F4) || Input.GetKeyDown(KeyCode.I)) EnsureDevWindow(InventoryDevWindow.Instance, InventoryDevWindow.Open);
                else if (Input.GetKeyDown(KeyCode.F5)) EnsureDevWindow(WeaponGripEditorDevWindow.Instance, WeaponGripEditorDevWindow.Open);
                else if (Input.GetKeyDown(KeyCode.F6) || Input.GetKeyDown(KeyCode.B)) EnsureDevWindow(BestiaryDevWindow.Instance, BestiaryDevWindow.Open);
                else if (Input.GetKeyDown(KeyCode.F7)) EnsureDevWindow(RiverOfTimeDevWindow.Instance, RiverOfTimeDevWindow.Open);
                else if (Input.GetKeyDown(KeyCode.F8)) EnsureDevWindow(Killtime.Story.HybrisWorldMapDevWindow.Instance, Killtime.Story.HybrisWorldMapDevWindow.Open);
                else if (Input.GetKeyDown(KeyCode.F9)) EnsureDevWindow(Killtime.Story.HybrisWorldMapEditorWindow.Instance, Killtime.Story.HybrisWorldMapEditorWindow.Open);
                else if (Input.GetKeyDown(KeyCode.F10))
                {
                    EnsureDevWindow(TimeScaleDevWindow.Instance, TimeScaleDevWindow.Open);
                }
                else if (Input.GetKeyDown(KeyCode.F12)) EnsureDevWindow(ArmoryDevWindow.Instance, ArmoryDevWindow.Open);
                else if (Input.GetKeyDown(KeyCode.L) && GUIUtility.keyboardControl == 0)
                {
                    ToggleDevFullbrightLight();
                }
            }
        }

        /// <summary>
        /// Vitesse du jeu 0.01x–100x, pilotée par TimeScaleDevWindow (F10).
        /// Ralentit/accélère tout : animations, cinématiques, IA, projectiles.
        /// La pause (CombatHUD) reste compatible : elle restaure la consigne
        /// pré-pause, pas forcément 1x. Wrappers conservés pour les appelants
        /// existants (CinematicDirector, prefs).
        /// </summary>
        public static float CurrentDevTimeScale => TimeScaleDevWindow.CurrentTimeScale;

        public static void SetDevTimeScale(float scale) => TimeScaleDevWindow.SetTimeScale(scale);

        /// <summary>
        /// Valeur dev lue sans dépendre de la fenêtre (utilisée par le directeur
        /// cinématique pour restaurer la bonne vitesse après un plan d'action).
        /// </summary>
        public static float ReadDevTimeScalePref() => TimeScaleDevWindow.ReadTimeScalePref();

        /// <summary>
        /// Crée+ouvre une fenêtre dev si elle n'existe pas encore (ou si désactivée).
        /// Si elle existe et est active, son propre Update a déjà traité la touche : ne rien faire.
        /// </summary>
        private static void EnsureDevWindow(MonoBehaviour instance, System.Action open)
        {
            if (instance == null)
            {
                open();
            }
            else if (!instance.isActiveAndEnabled)
            {
                instance.gameObject.SetActive(true);
                instance.enabled = true;
                open();
            }
        }

        public void AddLog(string msg)
        {
            _logs.Add(msg);
            if (_logs.Count > 50) _logs.RemoveAt(0);
            // Verrou actif => ne jamais toucher au scroll. Sinon on reporte la
            // demande au Draw (hauteur réelle connue là-bas).
            if (!_scrollLock)
            {
                _logScrollToBottomPending = true;
            }
        }

        protected override void DrawClosedState()
        {
            if (GUI.Button(ClosedButtonRect, "🛠️ Ouvrir Dev Arena"))
            {
                OpenInstance();
            }
        }

        protected override void DrawContent()
        {
            var activeUnit = _turnManager != null ? _turnManager.ActiveUnit : (_arena != null ? _arena.PlayerUnit : null);

            DrawUnitStatusHeader(activeUnit);

            GUILayout.Space(6);
            int newTab = GUILayout.Toolbar(_selectedTab, _tabNames);
            if (newTab != _selectedTab)
            {
                _selectedTab = newTab;
                // En revenant sur les logs en auto-scroll, on repart en bas.
                if (_selectedTab == 3 && !_scrollLock) _logScrollToBottomPending = true;
            }
            GUILayout.Space(8);

            // L'onglet Logs a son propre scroll interne : on ne l'imbrique PAS dans
            // le scroll vertical des outils, sinon la molette pilote les deux et le
            // verrou semble ne pas bloquer l'auto-scroll.
            if (_selectedTab == 3)
            {
                DrawLogsTab();
                if (GUI.changed)
                {
                    try { CaptureDevPrefs(); } catch { /* ignore */ }
                }
                return;
            }

            _toolsScroll = GUILayout.BeginScrollView(_toolsScroll);

            switch (_selectedTab)
            {
                case 0:
                    DrawTargetedAttackTab(activeUnit);
                    break;
                case 1:
                    DrawCheatsAndToolsTab();
                    break;
                case 2:
                    DrawChronomancyTab();
                    break;
            }

            GUILayout.EndScrollView();

            if (GUI.changed)
            {
                try { CaptureDevPrefs(); } catch { /* ignore */ }
            }
        }
    }
}

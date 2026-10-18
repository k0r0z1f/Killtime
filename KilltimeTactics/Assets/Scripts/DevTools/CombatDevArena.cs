using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Audio;
using Killtime.Core.Character;
using Killtime.Core.Combat;
using Killtime.Core.Dice;
using Killtime.Core.Chrono;
using Killtime.Core.Inventory;
using Killtime.Core.Rules;
using Killtime.CameraSystem;
using Killtime.WebGL;
using Killtime.UI;
using Killtime.Tactics.Arena;

namespace Killtime.Tactics
{
    public enum ArenaLayoutType
    {
        TheDuelRing,
        TacticalBarricades,
        KillzoneChokepoint
    }

    /// <summary>
    /// Contrôleur central de l'Arène de Développement Combat.
    /// Orchestre la grille, les mannequins d'entraînement (sparring dummies),
    /// les presets d'obstacles, la causalité chronomantique et les outils de test développeur.
    /// </summary>
    public partial class CombatDevArena : MonoBehaviour, IArenaHost
    {
        [Header("Composants de Scène")]
        [SerializeField] private TacticalHexGrid _grid;
        [SerializeField] private HexGridVisualizer _gridVisualizer;
        [SerializeField] private TurnManager _turnManager;
        [SerializeField] private TacticalCameraController _cameraController;
        [SerializeField] private CinematicDirector _cinematicDirector;

        private bool IsStorySceneActive()
        {
            var sceneMgr = Killtime.Story.StorySceneManager.Instance ?? FindAnyObjectByType<Killtime.Story.StorySceneManager>();
            return sceneMgr != null && sceneMgr.CurrentSceneController != null;
        }

        [Header("Configuration")]
        [SerializeField] private ArenaLayoutType _initialLayout = ArenaLayoutType.TacticalBarricades;
        [SerializeField] private bool _enableCinematicKillcam = true;
        [SerializeField] private bool _showCoverLineOfSight = true;
        [SerializeField] private bool _enterCombatOnMapLoad = true;
        [SerializeField] private bool _autoEndTurnOnEmptyPA = true;
        [SerializeField] private float _autoEndTurnGraceDelay = 1.5f;

        [Header("Carte par défaut (Killzone_Alpha3)")]
        [Tooltip("Chemin Resources (sans extension) de la carte chargée à l'ouverture du jeu.")]
        [SerializeField] private string _defaultMapResourcePath = "Maps/Killzone_Alpha3";
        [Tooltip("Nom du fichier disque prioritaire dans Application.persistentDataPath/Maps.")]
        [SerializeField] private string _defaultMapFileName = "Killzone_Alpha3.json";
        [Tooltip("Si vrai, la carte ci-dessus remplace l'arène procédurale à l'ouverture.")]
        [SerializeField] private bool _loadDefaultMapOnStart = true;

        // État unités adossé à ArenaState (mêmes instances partagées avec les services).
        private ArenaState ArenaState => _arenaState ??= new ArenaState();

        public TacticalUnit PlayerUnit { get => ArenaState.PlayerUnit; private set => ArenaState.PlayerUnit = value; }
        public List<TacticalUnit> SparringDummies => ArenaState.SparringDummies;
        public TacticalUnit CurrentTarget { get => ArenaState.CurrentTarget; private set => ArenaState.CurrentTarget = value; }

        private List<TacticalUnit> _additionalPlayers => ArenaState.AdditionalPlayers;
        private List<CustomSpawnRecord> _customSpawns => ArenaState.CustomSpawns;

        public IReadOnlyList<TacticalUnit> AdditionalPlayers => _additionalPlayers;
        public IReadOnlyList<CustomSpawnRecord> CustomSpawns => _customSpawns;

        public bool InfiniteAP { get; set; } = false;

        /// <summary>
        /// Enclenche le mode combat (initiative + tours) au chargement d'une carte.
        /// Sans cela, StartNewRound() ne fait rien en mode exploration et l'arène
        /// reste figée hors combat (pas de tour actif, pas de fin de tour).
        /// </summary>
        public bool EnterCombatOnMapLoad
        {
            get => _enterCombatOnMapLoad;
            set => _enterCombatOnMapLoad = value;
        }

        /// <summary>
        /// Passe automatiquement au tour suivant quand l'allié actif n'a plus de PA
        /// (après un court délai de grâce). Ne coupe jamais une résolution, un
        /// déplacement ou une décision en cours, et n'interfère pas avec l'IA FullAuto.
        /// </summary>
        public bool AutoEndTurnOnEmptyPA
        {
            get => _autoEndTurnOnEmptyPA;
            set => _autoEndTurnOnEmptyPA = value;
        }

        private float _emptyPATimer;

        /// <summary>
        /// Affiche le « raycast » de détection du couvert (Livre VI §25.3) entre
        /// l'unité active et la cible courante : vert à découvert, jaune moitié (-1),
        /// orange 3/4 (-2), rouge couvert total (attaque impossible).
        /// </summary>
        public bool ShowCoverLineOfSight
        {
            get => _showCoverLineOfSight;
            set
            {
                _showCoverLineOfSight = value;
                if (!value) _gridVisualizer?.ClearLineOfSight();
                try
                {
                    if (Killtime.UI.DevUIPreferences.Current != null)
                    {
                        Killtime.UI.DevUIPreferences.Current.ShowCoverLineOfSight = value;
                        Killtime.UI.DevUIPreferences.MarkDirty();
                    }
                }
                catch { /* prefs optionnelles */ }
            }
        }

        public bool EnableCinematicKillcam
        {
            get => _enableCinematicKillcam;
            set
            {
                _enableCinematicKillcam = value;
                try
                {
                    if (Killtime.UI.DevUIPreferences.Current != null)
                    {
                        Killtime.UI.DevUIPreferences.Current.EnableCinematicKillcam = value;
                        Killtime.UI.DevUIPreferences.MarkDirty();
                    }
                }
                catch { /* prefs optionnelles */ }
            }
        }

        [Serializable]
        public class CustomSpawnRecord
        {
            public CharacterSheet Sheet;
            public HexCoordinates SpawnCoords;
            public bool IsPlayer;
        }
        public CombatCalculator Calculator => _combatCalculator;
        public HexPathfinder Pathfinder => _pathfinder;
        public TimelineBranch Timeline => _chronoService != null ? _chronoService.ActiveTimeline : _timelineBranch;
        public TimelineId ActiveChronoBranch => _chronoService != null ? _chronoService.ActiveBranchId : TimelineId.Timeline0_Prime;
        public ArenaLayoutType CurrentLayout => _currentLayout;

        public event Action<string> OnCombatLogMessage;
        public event Action<TacticalUnit> OnTargetChanged;

        private HexPathfinder _pathfinder;
        private CombatCalculator _combatCalculator;
        private DiceRoller _diceRoller;
        private TimelineBranch _timelineBranch;
        private ArenaLayoutType _currentLayout { get => ArenaState.CurrentLayout; set => ArenaState.CurrentLayout = value; }
        private AI.TacticalAIController _aiController;
        private int _adaptiveTurnCount { get => ArenaState.AdaptiveTurnCount; set => ArenaState.AdaptiveTurnCount = value; }

        // Socle Arena V2 (services + façade) : état partagé, garde de résolution,
        // service fumée et magasin de carte. Initialisés dans Awake().
        private ArenaState _arenaState;
        private ArenaResolutionGuard _guard;
        private ArenaSmokeService _smokeService;
        private readonly ArenaMapStore _mapStore = new();
        private ArenaContext _arenaContext;
        private ArenaUnitStore _unitStore;
        private ArenaChronoDebugService _chronoService;
        private ArenaTargetingService _targeting;
        private ArenaTurnEventsService _turnEvents;
        private ArenaOverwatchService _overwatch;
        private ArenaGrenadeService _grenades;
        private ArenaMeleeService _melee;
        private ArenaAttackService _attack;

        public AI.TacticalAIController AIController => _aiController;

        public TacticalMapSaveData CurrentLoadedMap => _mapStore.CurrentLoadedMap;
        public string CurrentLoadedMapPath => _mapStore.CurrentLoadedMapPath;
        public bool HasLoadedMap => _mapStore.HasLoadedMap;
        /// <summary>
        /// Émis lorsqu'une carte devient la carte active de l'arène. Le VTT s'en
        /// sert pour publier le chargement du GM sans coupler l'arène au réseau.
        /// </summary>
        public event Action<TacticalMapSaveData> OnLoadedMapRegistered;

        /// <summary>Vrai pendant une passe d'armes OU un lancer de grenade (anti double-clic / attente IA).</summary>
        public bool IsResolving => _guard != null && _guard.IsResolving;

        /// <summary>Zones persistantes actives (fumée, gaz, feu...). Purgées au reset, tickées en début de tour.</summary>
        private List<SmokeZone> _smokeZones => ArenaState.SmokeZones;
        public IReadOnlyList<SmokeZone> SmokeZones => _smokeZones;


        private void Awake()
        {
            _diceRoller = new DiceRoller();
            _combatCalculator = new CombatCalculator(_diceRoller);
            _timelineBranch = new TimelineBranch(TimelineId.Timeline0_Prime);
            _arenaState ??= new ArenaState();
            _guard ??= new ArenaResolutionGuard();
            _smokeService ??= new ArenaSmokeService(_diceRoller, this, () => CurrentTarget);

            Killtime.Core.Arcanotech.ArcanotechWorkshop.DistanceToHeroProvider = (stats, heroName) =>
            {
                if (stats == null || string.IsNullOrEmpty(heroName)) return -1;
                TacticalUnit origin = null;
                TacticalUnit hero = null;
                var allUnits = TacticalUnitRegistry.AllUnits;
                for (int i = 0; i < allUnits.Count; i++)
                {
                    var u = allUnits[i];
                    if (u != null && u.Stats == stats) origin = u;
                    if (u != null && u.Stats != null && u.Stats.IsAlive && !string.IsNullOrEmpty(u.Stats.Name) && u.Stats.Name.IndexOf(heroName, StringComparison.OrdinalIgnoreCase) >= 0)
                        hero = u;
                }
                if (origin != null && hero != null)
                {
                    return origin.CurrentCoords.DistanceTo(hero.CurrentCoords);
                }
                return -1;
            };

            EnsureDependencies();
            EnsureAIController();
            _arenaContext = new ArenaContext
            {
                Grid = _grid,
                Visualizer = _gridVisualizer,
                TurnManager = _turnManager,
                Camera = _cameraController,
                Cinematic = _cinematicDirector,
                AI = _aiController,
                State = ArenaState,
                Guard = _guard,
                Map = _mapStore,
                Host = this,
                GetEnterCombatOnMapLoad = () => _enterCombatOnMapLoad,
                IsStoryScene = () => IsStorySceneActive(),
            };
            _unitStore = new ArenaUnitStore(_arenaContext)
            {
                SnapshotRecorder = RecordChronoSnapshot,
                PersistLayoutPref = SaveArenaLayoutPref,
            };
            _chronoService = new ArenaChronoDebugService(_arenaContext, _unitStore, _timelineBranch);
            _targeting = new ArenaTargetingService(_arenaContext, _smokeService)
            {
                OverwatchTrigger = CheckOverwatchTriggers,
            };
            _turnEvents = new ArenaTurnEventsService(_arenaContext, _smokeService, _targeting)
            {
                SnapshotRecorder = RecordChronoSnapshot,
            };
            _arenaContext.Calculator = _combatCalculator;
            _arenaContext.Dice = _diceRoller;
            _arenaContext.GetInfiniteAP = () => InfiniteAP;
            _arenaContext.GetKillcamEnabled = () => _enableCinematicKillcam;
            _overwatch = new ArenaOverwatchService(_arenaContext, _combatCalculator, _targeting, _smokeService)
            {
                SnapshotRecorder = RecordChronoSnapshot,
                ShotConsumer = ConsumeShotAndCheckJam,
            };
            _targeting.OverwatchTrigger = _overwatch.CheckOverwatchTriggers;
            _grenades = new ArenaGrenadeService(_arenaContext, _unitStore, _targeting, _overwatch)
            {
                SnapshotRecorder = RecordChronoSnapshot,
                BlastKnockback = TryApplyBlastKnockback,
            };
            _melee = new ArenaMeleeService(_arenaContext, _overwatch)
            {
                SnapshotRecorder = RecordChronoSnapshot,
                ChargeStrike = (actor, target, part, skill, ap, pe) => ExecuteAttack(
                    part, cancelPenaltyWithAP: false, attackSkill: skill,
                    attackerBonusAP: ap, attackerPE: pe,
                    explicitTarget: target, explicitAttacker: actor),
            };
            _grenades.BlastKnockback = _melee.TryApplyBlastKnockback;
            _overwatch.ShotConsumer = _melee.ConsumeShotAndCheckJam;
            _attack = new ArenaAttackService(_arenaContext, _targeting, _overwatch, _melee)
            {
                SnapshotRecorder = RecordChronoSnapshot,
            };
            try { LoadDevUIPrefs(); } catch { /* prefs optionnelles */ }
        }

        public void LoadDevUIPrefs()
        {
            var p = Killtime.UI.DevUIPreferences.Current;
            if (p == null) return;
            if (p.ArenaLayout >= 0 && Enum.IsDefined(typeof(ArenaLayoutType), p.ArenaLayout))
            {
                _currentLayout = (ArenaLayoutType)p.ArenaLayout;
            }
            InfiniteAP = p.InfiniteAP;
            _enableCinematicKillcam = p.EnableCinematicKillcam;
            _showCoverLineOfSight = p.ShowCoverLineOfSight;
            if (!_showCoverLineOfSight) _gridVisualizer?.ClearLineOfSight();
            _aiController?.LoadDevUIPrefs();
        }

        private void SaveArenaLayoutPref()
        {
            try
            {
                var p = Killtime.UI.DevUIPreferences.Current;
                if (p == null) return;
                p.ArenaLayout = (int)_currentLayout;
                p.InfiniteAP = InfiniteAP;
                p.EnableCinematicKillcam = _enableCinematicKillcam;
                p.ShowCoverLineOfSight = _showCoverLineOfSight;
                Killtime.UI.DevUIPreferences.MarkDirty();
            }
            catch { /* ignore */ }
        }

        private void EnsureAIController()
        {
            _aiController = GetComponent<AI.TacticalAIController>() ?? FindAnyObjectByType<AI.TacticalAIController>();
            if (_aiController == null)
            {
                _aiController = gameObject.AddComponent<AI.TacticalAIController>();
            }
        }

        private void Start()
        {
            _pathfinder = new HexPathfinder(_grid);
            if (_arenaContext != null) _arenaContext.Pathfinder = _pathfinder;
            TacticalUnit.OnAnyUnitMoved += HandleTargetMovedFollow;
            TacticalUnit.OnAnyUnitTeleported += HandleTargetTeleportedFollow;
            // RD-031 : réactions d'opportunité pas-à-pas (blocage avant pas,
            // frappe/poursuite/balayage après pas si contact rompu).
            TacticalUnit.OnAnyUnitStepCompleted += HandleUnitStepOpportunity;
            TacticalUnit.StepBlockCheck = CheckOpportunityBlock;

            // 1-2. Carte par défaut (Killzone_Alpha3) prioritaire sur l'arène procédurale.
            // L'ancienne arène (TacticalBarricades + mannequins) ne sert plus que de repli
            // si le JSON embarqué / disque est introuvable ou illisible.
            bool defaultMapLoaded = false;
            if (_loadDefaultMapOnStart)
            {
                defaultMapLoaded = TryLoadDefaultStartupMap();
            }

            if (!defaultMapLoaded)
            {
                // Repli : ancien comportement procédural.
                ApplyArenaLayout(_initialLayout);
                SetupArenaUnits();
            }

            // 3. Liaison avec le TurnManager
            if (_turnManager != null)
            {
                _turnManager.OnTurnStarted -= HandleTurnStarted;
                _turnManager.OnCombatEnded -= HandleCombatEnded;
                _turnManager.OnUnitStatusExpired -= HandleUnitStatusExpired;
                _turnManager.OnInitiativeRolled -= HandleInitiativeRolled;
                _turnManager.OnRoundStarted -= HandleRoundStarted;
                _turnManager.OnTurnStarted += HandleTurnStarted;
                _turnManager.OnCombatEnded += HandleCombatEnded;
                _turnManager.OnUnitStatusExpired += HandleUnitStatusExpired;
                _turnManager.OnInitiativeRolled += HandleInitiativeRolled;
                _turnManager.OnRoundStarted += HandleRoundStarted;
            }

            // 4. Sélectionner le premier mannequin par défaut
            if (CurrentTarget == null && SparringDummies.Count > 0)
            {
                SelectTarget(SparringDummies[0]);
            }

            // 5. Enregistrer le snapshot initial du Fleuve du Temps
            RecordChronoSnapshot(defaultMapLoaded
                ? $"Ouverture sur la carte '{CurrentLoadedMap?.MapName}' (Temps t0)"
                : "Début de l'affrontement (Temps t0)");

            if (KilltimeAudioManager.Instance != null)
            {
                _adaptiveTurnCount = 0;
                KilltimeAudioManager.Instance.PlayMusic(MusicMood.Combat, 0.5f, true);
                KilltimeAudioManager.Instance.Play(SoundId.Round_Start, 0.8f);
                UpdateAdaptiveMusic();
            }
            Log(defaultMapLoaded
                ? $"🗺️ <b>Carte '{CurrentLoadedMap?.MapName}'</b> chargée à l'ouverture. Prête pour les tests."
                : "⚔️ <b>Arène de Combat Killtime Initialisée !</b> Prête pour les tests.");
            _aiController?.TriggerAITurnIfApplicable();
        }


        private void EnsureDependencies()
        {
            if (_grid == null) _grid = GetComponent<TacticalHexGrid>() ?? FindAnyObjectByType<TacticalHexGrid>();
            if (_gridVisualizer == null) _gridVisualizer = GetComponent<HexGridVisualizer>() ?? FindAnyObjectByType<HexGridVisualizer>();
            if (_turnManager == null) _turnManager = GetComponent<TurnManager>() ?? FindAnyObjectByType<TurnManager>();
            if (_cameraController == null) _cameraController = FindAnyObjectByType<TacticalCameraController>();
            if (_cinematicDirector == null) _cinematicDirector = FindAnyObjectByType<CinematicDirector>();

            var hud = FindAnyObjectByType<CombatHUD>();
            if (hud == null)
            {
                var hudGo = new GameObject("[UI] TacticalFloatingHUD");
                hudGo.AddComponent<CombatHUD>();
            }
        }

        private void Update()
        {
            _targeting.HandleMouseInteraction();
            _targeting.UpdateReachableHighlights();
            UpdateCoverLineOfSight();
            UpdateAutoEndTurn();

            if (InfiniteAP && _turnManager.ActiveUnit != null)
            {
                _turnManager.ActiveUnit.Stats.CurrentActionPoints = _turnManager.ActiveUnit.Stats.MaxActionPoints;
            }
        }

        /// <summary>
        /// Passe au tour suivant quand l'allié actif manuel n'a plus de PA : après un
        /// court délai de grâce à 0 PA (laisse le temps de prendre un souffle ou de
        /// finir un geste), fin de tour automatique avec log. Garde-fous : combat en
        /// cours uniquement, unité alliée vivante pilotée manuellement (jamais en
        /// FullAuto), jamais pendant une résolution, un déplacement, une défense à
        /// déclarer, une pause, ni côté client multijoueur.
        /// </summary>
        private void UpdateAutoEndTurn()
        {
            if (!_autoEndTurnOnEmptyPA || _turnManager == null || Killtime.UI.CombatHUD.IsPaused)
                return;

            var active = _turnManager.ActiveUnit;
            bool idleEmpty =
                !_turnManager.IsInExploration
                && !_turnManager.IsCombatOver
                && active != null && active.Stats != null && active.Stats.IsAlive
                && active.IsPlayerControlled
                && (_aiController == null || _aiController.Mode != AI.CombatAIMode.FullAuto)
                && !TurnManager.IsMultiplayerPlayerClient()
                && !IsResolving
                && !active.IsMoving
                && !CombatUI.CombatContextMenuUI.IsDefenseOpen
                && active.Stats.CurrentActionPoints <= 0;

            if (!idleEmpty)
            {
                _emptyPATimer = 0f;
                return;
            }

            _emptyPATimer += Time.deltaTime;
            if (_emptyPATimer >= _autoEndTurnGraceDelay)
            {
                _emptyPATimer = 0f;
                Log($"⏭️ <b>{active.Stats.Name}</b> n'a plus de PA : tour suivant !");
                _turnManager.EndCurrentTurn();
            }
        }
        /// <summary>
        /// Rafraîchit chaque frame la ligne de visée de détection du couvert entre
        /// l'unité active et la cible courante (bon marché : balayage de quelques cases).
        /// Au changement de cible : log diagnostic (fraction visible, cases et
        /// accessoires bloqueurs, taille du registre 3D).
        /// </summary>
        private void UpdateCoverLineOfSight()
        {
            _targeting.UpdateCoverLineOfSight(_showCoverLineOfSight);
        }








































        // =====================================================================
        // RD-031 : OPPORTUNITÉ / DÉSENGAGEMENT (Livre VI §24.4) — voir ArenaOverwatchService.
        // =====================================================================




        // =====================================================================
        // RD-048 : RETARDER / TENIR / CANALISATION (Livre VI — Tours).
        // =====================================================================










        // =====================================================================
        // GRENADES : viser (case) -> lancer (main / lance-grenade) -> dispersion ->
        // impact -> explosion + shrapnels + statuts (Livre VIII §31.3, Livre VI §24).
        // =====================================================================






        // (ResolveGrenadeBlast déplacé dans ArenaGrenadeService.)

        // =====================================================================
        // ARMES : lancer 2 PA + désarmement Bras Droit fait tomber (Livre VI §24).
        // =====================================================================

        public const int WeaponThrowAPCost = 2;
        public const int WeaponThrowMaxRange = 4;
        public const int WeaponSwapAPCost = 1;






        // =========================================================================
        // RD-039 : LUTTE, GRAPPLE, ÉTRANGLEMENT & TRAÎNÉE DE CORPS
        // =========================================================================

        public const int GrappleAPCost = 2;
        public const int StrangulationAPCost = 2;
        public const int DragBodyAPCost = 2;









        // =========================================================================
        // RD-084 : ATTAQUES COMBINÉES DE GROUPE (§25.2) & POURSUITE (§25.1)
        // =========================================================================























        public void Log(string message)
        {
            OnCombatLogMessage?.Invoke($"[{DateTime.Now:HH:mm:ss}] {message}");
        }

        // IArenaHost : seul point d'accès des services au MonoBehaviour (V2 : F2/F4).
        public Coroutine ArenaStartCoroutine(IEnumerator routine) => StartCoroutine(routine);
        public void ArenaLog(string message) => Log(message);
        public void RequestAutoTargetNextAlive() => AutoTargetNextAlive();
        public void RequestSelectTarget(TacticalUnit unit) => SelectTarget(unit);
        public void NotifyTargetChanged(TacticalUnit unit) => OnTargetChanged?.Invoke(unit);
        public void RequestStopAllCoroutines() => StopAllCoroutines();
        public void RequestMusicUpdate() => UpdateAdaptiveMusic();
        public void RequestCombatOverCheck() => _turnManager?.CheckCombatOver();

        private void OnDisable()
        {
            TacticalUnit.OnAnyUnitMoved -= HandleTargetMovedFollow;
            TacticalUnit.OnAnyUnitTeleported -= HandleTargetTeleportedFollow;
            // RD-031 : retire le hook d'opportunité (aucun blocage fantôme).
            TacticalUnit.OnAnyUnitStepCompleted -= HandleUnitStepOpportunity;
            if (TacticalUnit.StepBlockCheck != null && TacticalUnit.StepBlockCheck.Method?.DeclaringType == GetType())
                TacticalUnit.StepBlockCheck = null;
        }
    }
}

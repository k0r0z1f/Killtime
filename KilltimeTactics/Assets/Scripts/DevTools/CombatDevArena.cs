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
    public class CombatDevArena : MonoBehaviour, IArenaHost
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
        public TimelineBranch Timeline => _timelineBranch;
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

        /// <summary>
        /// Charge la carte par défaut à l'ouverture du jeu.
        /// Priorité : 1) disque persistant Maps/Killzone_Alpha3.json (version éditée),
        /// 2) JSON embarqué Resources/Maps/Killzone_Alpha3.json (build + projet),
        /// 3) StreamingAssets/Maps/Killzone_Alpha3.json (optionnel).
        /// Retourne vrai si une carte a été appliquée (tuiles + props + unités).
        /// </summary>
        private bool TryLoadDefaultStartupMap()
        {
            try
            {
                if (_grid == null) EnsureDependencies();
                if (_grid == null) return false;

                TacticalMapSaveData mapData = null;
                string mapPath = null;

                // 1) Disque persistant : l'édition F2 du joueur reste prioritaire.
                if (!string.IsNullOrEmpty(_defaultMapFileName))
                {
                    try
                    {
                        string persistentPath = Path.Combine(Application.persistentDataPath, "Maps", _defaultMapFileName);
                        if (File.Exists(persistentPath))
                        {
                            string diskJson = File.ReadAllText(persistentPath);
                            var diskData = JsonUtility.FromJson<TacticalMapSaveData>(diskJson);
                            if (diskData != null)
                            {
                                mapData = diskData;
                                mapPath = persistentPath;
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[CombatDevArena] Lecture carte disque impossible : {e.Message}");
                    }
                }

                // 2) JSON embarqué dans le projet (Resources/Maps/Killzone_Alpha3.json).
                if (mapData == null && !string.IsNullOrEmpty(_defaultMapResourcePath))
                {
                    try
                    {
                        var textAsset = Resources.Load<TextAsset>(_defaultMapResourcePath);
                        if (textAsset != null && !string.IsNullOrEmpty(textAsset.text))
                        {
                            var resData = JsonUtility.FromJson<TacticalMapSaveData>(textAsset.text);
                            if (resData != null)
                            {
                                mapData = resData;
                                mapPath = null;
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[CombatDevArena] Lecture carte Resources impossible : {e.Message}");
                    }
                }

                // 3) StreamingAssets en dernier recours (builds standalone).
                if (mapData == null && !string.IsNullOrEmpty(_defaultMapFileName))
                {
                    try
                    {
                        string saPath = Path.Combine(Application.streamingAssetsPath, "Maps", _defaultMapFileName);
                        if (File.Exists(saPath))
                        {
                            string saJson = File.ReadAllText(saPath);
                            var saData = JsonUtility.FromJson<TacticalMapSaveData>(saJson);
                            if (saData != null)
                            {
                                mapData = saData;
                                mapPath = saPath;
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[CombatDevArena] Lecture carte StreamingAssets impossible : {e.Message}");
                    }
                }

                if (mapData == null)
                {
                    Debug.LogWarning("[CombatDevArena] Carte par défaut introuvable : repli arène procédurale.");
                    return false;
                }

                // Le rayon embarqué (6) tient dans la grille de scène (8). Si un jour
                // la carte exige plus large, on régénère au lieu de tronquer.
                if (mapData.GridRadius > _grid.GridRadius)
                {
                    try
                    {
                        var radiusField = typeof(TacticalHexGrid).GetField("_gridRadius",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (radiusField != null) radiusField.SetValue(_grid, mapData.GridRadius);
                        _grid.GenerateGrid();
                        _gridVisualizer?.BuildVisualGrid();
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[CombatDevArena] Régénération grille impossible : {e.Message}");
                    }
                }

                // Application via l'éditeur (props 3D + unités + plafond) si présent,
                // sinon application minimale directe sur la grille + unités.
                var mapEditor = MapEditorDevWindow.Instance ?? FindAnyObjectByType<MapEditorDevWindow>();
                if (mapEditor == null)
                {
                    try { mapEditor = new GameObject("[UI] MapEditorDevWindow").AddComponent<MapEditorDevWindow>(); }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[CombatDevArena] Création MapEditor impossible : {e.Message}");
                    }
                }

                if (mapEditor != null)
                {
                    mapEditor.ApplyLoadedMap(mapData, mapPath);
                }
                else
                {
                    _grid.ResetGridState();
                    if (mapData.CeilingHeight > 0) _grid.CeilingHeight = mapData.CeilingHeight;
                    if (mapData.ModifiedTiles != null)
                    {
                        for (int i = 0; i < mapData.ModifiedTiles.Count; i++)
                        {
                            var tile = mapData.ModifiedTiles[i];
                            if (tile == null) continue;
                            var coords = new HexCoordinates(tile.Q, tile.R);
                            _grid.SetNodeCover(coords, tile.Cover, tile.IsWalkable);
                            _grid.SetNodeCeiling(coords, tile.HasCeiling);
                            _grid.SetNodeElevation(coords, tile.Elevation);
                            _grid.SetNodeGroundTexture(coords, tile.GroundTexture);
                        }
                    }
                    LoadUnitsFromMap(mapData.PlacedUnits);
                    _gridVisualizer?.RefreshObstacles();
                    RegisterLoadedMap(mapData, mapPath);
                }

                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CombatDevArena] Chargement carte par défaut échoué : {e.Message}. Repli procédural.");
                return false;
            }
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

        private void SetupArenaUnits()
        {
            _unitStore.SetupArenaUnits();
        }

        public TacticalUnit SpawnCustomCharacter(CharacterSheet sheet, HexCoordinates coords, bool isPlayer)
        {
            return _unitStore.SpawnCustomCharacter(sheet, coords, isPlayer);
        }

        /// <summary>
        /// Résout une case libre : la case demandée si libre, sinon la plus proche.
        /// Garantit 1 case = 1 avatar (jamais d'empilement).
        /// </summary>
        private HexCoordinates ResolveFreeCoords(HexCoordinates desired, HashSet<HexCoordinates> extraReserved = null)
        {
            return _unitStore.ResolveFreeCoords(desired, extraReserved);
        }

        public void ClearCustomSpawns()
        {
            _unitStore.ClearCustomSpawns();
        }

        public void RegisterLoadedMap(TacticalMapSaveData mapData, string mapPath = null)
        {
            _mapStore.Register(mapData, mapPath);
            try { OnLoadedMapRegistered?.Invoke(mapData); }
            catch (Exception e) { Debug.LogException(e); }
        }

        public void ClearLoadedMap()
        {
            _mapStore.Clear();
        }

        private void RestoreLoadedMap()
        {
            if (CurrentLoadedMap == null) return;

            // Carte ré-initialisée : purge les objets au sol avant re-spawn.
            DroppedWeaponPickup.ClearAllDropped();
            _smokeZones.Clear();
            _gridVisualizer?.UpdatePersistentZones(_smokeZones);
            CombatUI.DroppedWeaponContextMenuUI.Instance?.CloseMenu();
            var mapEditor = MapEditorDevWindow.Instance ?? FindAnyObjectByType<MapEditorDevWindow>();
            if (mapEditor != null)
            {
                mapEditor.ApplyLoadedMap(CurrentLoadedMap, CurrentLoadedMapPath);
            }
            else
            {
                LoadUnitsFromMap(CurrentLoadedMap.PlacedUnits);
            }

            RecordChronoSnapshot($"Réinitialisation de la Carte '{CurrentLoadedMap.MapName}'");
            if (KilltimeAudioManager.Instance != null)
            {
                KilltimeAudioManager.Instance.Play(SoundId.Arena_Reset, 0.8f);
                _adaptiveTurnCount = 0;
                KilltimeAudioManager.Instance.PlayMusic(MusicMood.Combat, 0, 0.5f, forceRestart: true);
                UpdateAdaptiveMusic();
            }
            Log($"🔄 Carte '<b>{CurrentLoadedMap.MapName}</b>' entièrement réinitialisée.");
        }

        public void ClearAllUnits()
        {
            _unitStore.ClearAllUnits();
        }

        public void LoadUnitsFromMap(List<MapUnitData> mapUnits)
        {
            _unitStore.LoadUnitsFromMap(mapUnits);
        }

        public void ApplyArenaLayout(ArenaLayoutType layout)
        {
            _unitStore.ApplyArenaLayout(layout);
        }

        private void HandleUnitStatusExpired(TacticalUnit unit, List<StatusEffect> expiredList)
        {
            _turnEvents.HandleUnitStatusExpired(unit, expiredList);
        }

        private void HandleInitiativeRolled(IReadOnlyList<InitiativeRollResult> results)
        {
            _turnEvents.HandleInitiativeRolled(results);
        }

        private void HandleCombatEnded(CombatOutcome outcome)
        {
            _turnEvents.HandleCombatEnded(outcome);
        }

        private void HandleTurnStarted(TacticalUnit unit)
        {
            _turnEvents.HandleTurnStarted(unit);
        }

        /// <summary>Décompte des zones à chaque round ; dissipation loggée.</summary>
        private void HandleRoundStarted(int round)
        {
            _turnEvents.HandleRoundStarted(round);
        }

        /// <summary>Case couverte par un écran de fumée actif ?</summary>
        public bool IsSmoked(HexCoordinates coords)
        {
            if (_smokeService != null) return _smokeService.IsSmoked(coords, _smokeZones);
            return SmokeScreen.IsCellSmoked(coords, _smokeZones);
        }

        /// <summary>Thermique (spécialisation ou Jumelles) : voit à travers la fumée.</summary>
        public static bool SeesThroughSmoke(TacticalUnit viewer)
        {
            return ArenaSmokeService.SeesThroughSmoke(viewer);
        }

        /// <summary>
        /// RD-045 : journalise le tick DoT résiduel du début de tour (déjà appliqué
        /// par TurnManager.RefreshAndNotifyActiveUnitTurnStart) et gère la mort.
        /// </summary>
        public void LogDotTick(TacticalUnit unit)
        {
            _smokeService?.LogDotTick(unit);
        }

        /// Effets d'environnement en début de tour : dégâts (ignore armure, barrière
        /// d'abord) + statuts de la zone. Masque : filtre les gaz (dégâts /2).
        /// </summary>
        public void TickSmokeForUnit(TacticalUnit unit)
        {
            _smokeService?.TickSmokeForUnit(unit, _smokeZones);
        }

        /// <summary>
        /// Grappin Magnétique (2 PA, 2-4 cases) : harpon 3D, À Terre si Diff ≥ 2.
        /// Le grappin revient (non consommé). Requiert l'outil en poche.
        /// </summary>
        /// <summary>
        /// Attaque directe contre un obstacle ou élément du décor (Livre VI).
        /// Applique les dégâts selon le matériau et le type de frappe (Contondant, Tranchant, Perforant, Thermique...).
        /// </summary>
        public bool ExecuteAttackOnObstacle(
            HexCoordinates targetCoords,
            SkillType attackSkill = SkillType.ManiementArmes,
            TacticalUnit explicitAttacker = null)
        {
            return _attack.ExecuteAttackOnObstacle(targetCoords, attackSkill, explicitAttacker);
        }

        public void ExecuteGrapnel(TacticalUnit attacker, TacticalUnit defender)
        {
            _attack.ExecuteGrapnel(attacker, defender);
        }

        /// <summary>
        /// Vrai si l'unité active est un allié contrôlé manuellement par le joueur.
        /// En mode FullAuto, l'IA contrôle aussi les alliés : aucun sélecteur de
        /// tuiles de mouvement (portée bleue / aperçu de chemin / clic) ne doit apparaître.
        /// En mode Normal (semi-auto), les alliés restent manuels : les sélecteurs s'affichent.
        /// </summary>
        private bool IsPlayerManualControl(TacticalUnit unit)
        {
            return _targeting != null ? _targeting.IsPlayerManualControl(unit) : unit != null && unit.IsPlayerControlled && !unit.IsMoving;
        }

        public void RegisterSceneParty(TacticalUnit player, IEnumerable<TacticalUnit> allies)
        {
            _unitStore.RegisterSceneParty(player, allies);
        }

        private void HandleMouseInteraction()
        {
            _targeting.HandleMouseInteraction();
        }

        private void UpdateReachableHighlights()
        {
            _targeting.UpdateReachableHighlights();
        }

        public TacticalUnit GetUnitAtCoords(HexCoordinates coords)
        {
            return _targeting.GetUnitAtCoords(coords);
        }

        public void SelectTarget(TacticalUnit target)
        {
            _targeting.SelectTarget(target);
        }

        public void AutoTargetOpponent(TacticalUnit activeUnit)
        {
            _targeting.AutoTargetOpponent(activeUnit);
        }

        private void HandleTargetMovedFollow(TacticalUnit unit, List<HexCoordinates> path, int apCost)
        {
            _targeting.HandleTargetMovedFollow(unit, path, apCost);
        }

        private void HandleTargetTeleportedFollow(TacticalUnit unit, HexCoordinates coords)
        {
            _targeting.HandleTargetTeleportedFollow(unit, coords);
        }

        public void CycleTarget()
        {
            _targeting.CycleTarget();
        }

        public void RegisterHostileUnit(TacticalUnit unit)
        {
            _unitStore.RegisterHostileUnit(unit);
        }

        public void AutoTargetNextAlive()
        {
            _targeting.AutoTargetNextAlive();
        }

        public void SwitchActiveExplorer(TacticalUnit unit)
        {
            _targeting.SwitchActiveExplorer(unit);
        }

        /// <summary>
        /// Déclenche une passe d'armes ou tir ciblé selon le duel aveugle officiel (Livres II §7 + VI §24.1) :
        /// déclaration attaquant (mise PA/PE cachée) -> déclaration défenseur à l'aveugle
        /// -> révélation simultanée -> résolution normale. Conforme à l'Axiome Fondateur : résolution par compétence.
        /// defenderBonusAP &lt; 0 = défense réactive auto (besoin réel après révélation de l'attaque).
        /// </summary>
        /// <summary>
        /// Livre VI §25.3 : couvert de la cible vue par l'attaquant (None au contact,
        /// Full si non visible). Utilisé par le HUD (affichage) et ExecuteAttack (blocage).
        /// </summary>
        public CoverType GetCoverToTarget(TacticalUnit attacker, TacticalUnit defender)
        {
            return _targeting.GetCoverToTarget(attacker, defender);
        }

        /// <summary>
        /// RD-036 Hauteur / contrebas : élévation d'une unité (0 si grille
        /// absente ou case inconnue — les arènes sans relief restent à 0).
        /// </summary>
        public float GetUnitElevation(TacticalUnit unit)
        {
            return _targeting.GetUnitElevation(unit);
        }

        /// <summary>
        /// RD-036 : ruling hauteur/contrebas attaquant ➔ défenseur pour le type
        /// d'attaque donné (palier 1.5 m, table mêlée + gate, ±1 balistique,
        /// laser/énergie exempté). Voir ElevationAdvantage.
        /// </summary>
        public ElevationRuling GetElevationRuling(TacticalUnit attacker, TacticalUnit defender, SkillType attackSkill, InventoryItem weapon, bool isMeleeAttack)
        {
            return _targeting.GetElevationRuling(attacker, defender, attackSkill, weapon, isMeleeAttack);
        }

        /// <summary>
        /// RD-030 Guet / Overwatch : met l'unité en guet (2 PA, rayon = portée de
        /// l'arme à distance équipée). Le tir de réaction partira automatiquement
        /// pendant le tour adverse (CheckOverwatchTriggers).
        /// </summary>
        public bool TryEnterOverwatch(TacticalUnit unit, out string message)
        {
            return _overwatch.TryEnterOverwatch(unit, out message);
        }

        /// <summary>
        /// RD-030 : après qu'une unité a agi en `atCoords` (fin de déplacement,
        /// téléportation, attaque), chaque guetteur ennemi dont le rayon couvre
        /// cette case lâche son tir de réaction en duel aveugle automatique.
        /// 1 tir par guet, couvert total = pas de tir, munitions consommées.
        /// </summary>
        public void CheckOverwatchTriggers(TacticalUnit mover, HexCoordinates atCoords)
        {
            _overwatch.CheckOverwatchTriggers(mover, atCoords);
        }

        // =====================================================================
        // RD-031 : OPPORTUNITÉ / DÉSENGAGEMENT (Livre VI §24.4) — voir ArenaOverwatchService.
        // =====================================================================

        /// <summary>
        /// RD-031 Blocage (hook TacticalUnit.StepBlockCheck, avant chaque pas) :
        /// chaque briseur en posture Blocage peut tenter de retenir le fuyard
        /// (duel opposé Athlétisme, 1 PA). Succès = pas annulé, sans grapple ;
        /// les PA du fuyard sont remboursés par MoveAlongPath. Vrai = bloqué.
        /// </summary>
        public bool CheckOpportunityBlock(TacticalUnit mover, HexCoordinates from, HexCoordinates to)
        {
            return _overwatch.CheckOpportunityBlock(mover, from, to);
        }

        /// <summary>
        /// RD-031 (hook TacticalUnit.OnAnyUnitStepCompleted, après chaque pas) :
        /// chaque briseur non-bloqueur réagit selon sa posture — Frappe (gratuite),
        /// Balayage (gratuit, Jambes), Poursuite (1 PA, suit d'1 case).
        /// </summary>
        private void HandleUnitStepOpportunity(TacticalUnit mover, HexCoordinates from, HexCoordinates to, bool provoked)
        {
            _overwatch.HandleUnitStepOpportunity(mover, from, to, provoked);
        }

        /// <summary>
        /// RD-031 Décrochage (Livre VI §26.2) : paie 1 PA pour que le prochain
        /// déplacement de l'unité ne provoque aucune réaction d'opportunité.
        /// </summary>
        public bool TryRequestSafeDisengage(TacticalUnit unit, out string message)
        {
            return _overwatch.TryRequestSafeDisengage(unit, out message);
        }

        // =====================================================================
        // RD-048 : RETARDER / TENIR / CANALISATION (Livre VI — Tours).
        // =====================================================================

        /// <summary>
        /// RD-048 Tenir : l'unité active passe son tour en conservant ses PA et
        /// se déclare en attente (0 PA). Interruption ultérieure via le
        /// TurnManager (TryResumeHeldUnit, rejouée juste après l'unité active).
        /// </summary>
        public bool TryHoldAction(TacticalUnit unit, out string message)
        {
            return _overwatch.TryHoldAction(unit, out message);
        }

        /// <summary>
        /// RD-048 Retarder : l'unité active repousse son tour (initiative
        /// -pénalité, 1/round), réinsérée plus tard dans le round en cours.
        /// </summary>
        public bool TryDelayTurn(TacticalUnit unit, out string message)
        {
            return _overwatch.TryDelayTurn(unit, out message);
        }

        /// <summary>
        /// RD-048 Canalisation / Action en Progression : étale l'effort sur
        /// plusieurs temps (coût 1 PA, +2 au jet à l'attaque, -1 en défense,
        /// interrompue si dégât net &gt; encaissement).
        /// </summary>
        public bool TryBeginChanneling(TacticalUnit unit, string label, out string message)
        {
            return _overwatch.TryBeginChanneling(unit, label, out message);
        }

        /// <summary>
        /// RD-048 : notifie une canalisation rompue par des dégâts hors duel
        /// (grenades, zone, DoT) et affiche le retour visuel. Les duels directs
        /// tracent déjà l'interruption dans leur propre journal de combat.
        /// </summary>
        public void NotifyChannelDamage(TacticalUnit defender, int finalDamageApplied)
        {
            _overwatch.NotifyChannelDamage(defender, finalDamageApplied);
        }

        /// <summary>
        /// RD-048 : retour visuel d'une interruption tracée par le duel
        /// (direct, guet, opportunité, assaut groupé). Le journal contient déjà
        /// la ligne « CANALISATION INTERROMPUE ».
        /// </summary>
        private void HandleChannelInterruptionFx(TacticalUnit defender, bool interrupted)
        {
            _overwatch.HandleChannelInterruptionFx(defender, interrupted);
        }

        /// <summary>
        /// RD-049 Furtivité / Stealth : fait passer l'unité en furtivité (2 PA).
        /// Considéré invisible dans le brouillard de guerre, confère +2 à la
        /// prochaine attaque et interdit toute réaction adverse (embuscade).
        /// </summary>
        public bool TryEnterStealth(TacticalUnit unit, out string message)
        {
            return _overwatch.TryEnterStealth(unit, out message);
        }

        public void ExecuteAttack(
            BodyPart targetedPart, 
            bool cancelPenaltyWithAP, 
            SkillType attackSkill = SkillType.Ballistique,
            SkillType defenseSkill = SkillType.Esquive,
            bool defenderWantsToDefend = true,
            string attackerSpecialization = null,
            string defenderSpecialization = null,
            int weaponBaseDamage = 5,
            int attackerBonusAP = 0,
            int defenderBonusAP = -1,
            DiceType? attackDie = null,
            DiceType? defenseDie = null,
            TacticalUnit explicitTarget = null,
            int attackerPE = 0,
            int defenderPE = 0,
            TacticalUnit explicitAttacker = null)
        {
            _attack.ExecuteAttack(targetedPart, cancelPenaltyWithAP, attackSkill, defenseSkill,
                defenderWantsToDefend, attackerSpecialization, defenderSpecialization, weaponBaseDamage,
                attackerBonusAP, defenderBonusAP, attackDie, defenseDie, explicitTarget,
                attackerPE, defenderPE, explicitAttacker);
        }

        private System.Collections.IEnumerator ExecuteAttackRoutine(Action triggerKick, Action triggerDefense, Action resolveImpact)
        {
            return _attack.ExecuteAttackRoutine(triggerKick, triggerDefense, resolveImpact);
        }

        private IEnumerator ExecuteAttackWithPlayerDefenseRoutine(
            TacticalUnit attacker,
            TacticalUnit defender,
            BodyPart targetedPart,
            bool cancelPenaltyWithAP,
            SkillType attackSkill,
            SkillType defenseSkill,
            int weaponBaseDamage,
            int attackerBonusAP,
            int attackerPE,
            string attackerSpecialization,
            string defenderSpecialization,
            Action triggerKick,
            Action triggerDefense)
        {
            return _attack.ExecuteAttackWithPlayerDefenseRoutine(attacker, defender, targetedPart,
                cancelPenaltyWithAP, attackSkill, defenseSkill, weaponBaseDamage, attackerBonusAP,
                attackerPE, attackerSpecialization, defenderSpecialization, triggerKick, triggerDefense);
        }

        // =====================================================================
        // GRENADES : viser (case) -> lancer (main / lance-grenade) -> dispersion ->
        // impact -> explosion + shrapnels + statuts (Livre VIII §31.3, Livre VI §24).
        // =====================================================================

        /// <summary>Inventaire lançable de l'unité (grenades main + compatibles lanceur).</summary>
        public static List<InventoryItem> GetThrowableGrenades(CharacterSheet sheet)
        {
            return ArenaGrenadeService.GetThrowableGrenades(sheet);
        }

        public static InventoryItem GetEquippedLauncher(CharacterSheet sheet)
        {
            return ArenaGrenadeService.GetEquippedLauncher(sheet);
        }

        public static InventoryItem GetAnyLauncher(CharacterSheet sheet)
        {
            return ArenaGrenadeService.GetAnyLauncher(sheet);
        }

        /// <summary>
        /// Déclenche un lancer de grenade sur une CASE (pas une cible anatomique) :
        /// paie les PA (2 main / 3 lanceur + 1 visée + bonus), consomme 1 grenade du stock,
        /// anime le lancer, disperse en cas d'échec Ballistique, puis fait détoner la zone.
        /// </summary>
        public void ExecuteGrenadeThrow(
            HexCoordinates targetCoords,
            string grenadeItemId,
            bool useLauncher,
            bool aimed,
            int attackerBonusAP = 0)
        {
            _grenades.ExecuteGrenadeThrow(targetCoords, grenadeItemId, useLauncher, aimed, attackerBonusAP);
        }

        public void ExecuteWeaponThrow(TacticalUnit attacker, TacticalUnit defender)
        {
            _grenades.ExecuteWeaponThrow(attacker, defender);
        }

        // (ResolveGrenadeBlast déplacé dans ArenaGrenadeService.)

        // =====================================================================
        // ARMES : lancer 2 PA + désarmement Bras Droit fait tomber (Livre VI §24).
        // =====================================================================

        public const int WeaponThrowAPCost = 2;
        public const int WeaponThrowMaxRange = 4;
        public const int WeaponSwapAPCost = 1;

        /// <summary>
        /// RD-033 : décompte le coup tiré puis enraye sur critique adverse.
        /// Appelé après chaque duel où un coup est réellement parti (jet attaquant lancé).
        /// </summary>
        public void ConsumeShotAndCheckJam(TacticalUnit attacker, InventoryItem weapon, DamageResult result, bool hdShot)
        {
            _melee.ConsumeShotAndCheckJam(attacker, weapon, result, hdShot);
        }

        /// <summary>
        /// RD-033 : recharge le chargeur depuis la réserve (éjecte le reste d'un autre
        /// type). useHD exige HeavyAmmo (sniper/Deglazer). Ne compte pas comme attaque.
        /// </summary>
        public bool TryReloadWeapon(TacticalUnit unit, bool useHD, out string message)
        {
            return _melee.TryReloadWeapon(unit, useHD, out message);
        }

        /// <summary>RD-033 : désenraye l'arme équipée (1 PA, gratuit 0 PA avec Kit d'Entretien d'Armes).</summary>
        public bool TryClearJam(TacticalUnit unit, out string message)
        {
            return _melee.TryClearJam(unit, out message);
        }

        /// <summary>
        /// Recharge d'urgence de la barrière de champ de force (1 PA) via Cellule Nytharite (Standard 10 PV / Pure 25 PV).
        /// </summary>
        public bool TryRechargeShieldWithCell(TacticalUnit unit, bool usePureCell, out string message)
        {
            return _melee.TryRechargeShieldWithCell(unit, usePureCell, out message);
        }

        /// <summary>
        /// Désarmement Bras Droit : sur touché au bras porteur, différentiel élevé
        /// arrache l'arme qui tombe au sol (2 cases max). Seuils Livre III :
        /// base 4, Croc de Désarmement 3, Désarmement Fleuret 2.
        /// </summary>
        public void TryDisarmDefender(TacticalUnit attacker, TacticalUnit defender, BodyPart targetedPart, DamageResult result)
        {
            _melee.TryDisarmDefender(attacker, defender, targetedPart, result);
        }

        // =========================================================================
        // RD-039 : LUTTE, GRAPPLE, ÉTRANGLEMENT & TRAÎNÉE DE CORPS
        // =========================================================================

        public const int GrappleAPCost = 2;
        public const int StrangulationAPCost = 2;
        public const int DragBodyAPCost = 2;

        /// <summary>
        /// RD-039 : Exécute une prise de lutte au corps-à-corps (2 PA).
        /// Duel opposé aveugle Athlétisme vs Athlétisme. Succès = cible Immobilisée et saisie en lutte.
        /// </summary>
        public void ExecuteGrapple(TacticalUnit actor, TacticalUnit target)
        {
            _melee.ExecuteGrapple(actor, target);
        }

        /// <summary>
        /// RD-039 : Tente de se libérer d'une étreinte de lutte (2 PA).
        /// Duel opposé aveugle Athlétisme/MainsNues/Acrobatie (victime) vs Athlétisme (grappler).
        /// Succès = prise brisée et statut Immobilisé dissipé.
        /// </summary>
        public void ExecuteGrappleEscape(TacticalUnit victim, TacticalUnit grappler)
        {
            _melee.ExecuteGrappleEscape(victim, grappler);
        }

        /// <summary>
        /// RD-039 : Étranglement au contact sur cible saisie ou immobilisée (2 PA).
        /// Duel opposé aveugle Athlétisme vs Endurance. Succès = Asphyxie (2 tours, DoT + drain PA).
        /// </summary>
        public void ExecuteStrangulation(TacticalUnit actor, TacticalUnit target)
        {
            _melee.ExecuteStrangulation(actor, target);
        }

        /// <summary>
        /// RD-040 : Exécute une Bousculade générique ou un Coup de bouclier (2 PA).
        /// Duel opposé aveugle de Force (Athlétisme / Mains Nues / Défense Corporelle).
        /// Victoire : recul forcé d'1 case (Knockback). Si un mur ou un obstacle bloque le recul, la cible chute À Terre.
        /// </summary>
        public void ExecuteShove(TacticalUnit attacker, TacticalUnit defender, bool isShieldBash, int attackerBonusAP = 0, int attackerPE = 0)
        {
            _melee.ExecuteShove(attacker, defender, isShieldBash, attackerBonusAP, attackerPE);
        }

        /// <summary>
        /// RD-039 : Vrai si le dragger peut traîner le corps de la victime d'1 case.
        /// </summary>
        public bool CanDragBody(TacticalUnit dragger, TacticalUnit victim)
        {
            return _melee.CanDragBody(dragger, victim);
        }

        /// <summary>
        /// RD-039 : Traîne le corps d'une cible saisie, immobilisée ou inconsciente d'1 case (2 PA).
        /// Le porteur recule ou pivote vers une case libre adjacente et tire la victime
        /// dans la case qu'il vient de libérer.
        /// </summary>
        public bool ExecuteDragBody(TacticalUnit dragger, TacticalUnit victim)
        {
            return _melee.ExecuteDragBody(dragger, victim);
        }

        /// <summary>Refoulement radial : repousse la cible sur le voisin le plus loin du souffle.</summary>
        public bool TryApplyBlastKnockback(TacticalUnit defender, HexCoordinates blastCoords)
        {
            return _melee.TryApplyBlastKnockback(defender, blastCoords);
        }

        /// <summary>
        /// Exécute une manœuvre de Charge (RD-038, Livre VI) :
        /// - Déplacement d'au moins 3 cases vers un voisin libre de la cible.
        /// - Débite les PA de déplacement (+ surcoût éventuel de manœuvre).
        /// - À l'arrivée au contact, exécute immédiatement l'attaque de mêlée (+2 dégâts, -1 défense jusqu'au prochain tour personnel).
        /// - Bloque le tir pour le reste du tour en cours.
        /// </summary>
        public void ExecuteChargeAttack(TacticalUnit actor, TacticalUnit target, BodyPart targetedPart, int attackerBonusAP = 0, int attackerPE = 0)
        {
            _melee.ExecuteChargeAttack(actor, target, targetedPart, attackerBonusAP, attackerPE);
        }

        // =========================================================================
        // RD-084 : ATTAQUES COMBINÉES DE GROUPE (§25.2) & POURSUITE (§25.1)
        // =========================================================================

        /// <summary>
        /// RD-084 : Collecte les participants d'un assaut groupé : alliés de
        /// l'acteur (même camp) vivants, avec assez de PA, à portée de la cible
        /// pour la compétence partagée (contact ≤ 1 en mêlée, portée d'arme au tir).
        /// </summary>
        public List<TacticalUnit> CollectCombinedAttackers(TacticalUnit actor, TacticalUnit target, SkillType attackSkill)
        {
            return _melee.CollectCombinedAttackers(actor, target, attackSkill);
        }

        /// <summary>
        /// RD-084 : Résout la compétence partagée de l'assaut (mêlée au contact,
        /// Ballistique à distance si l'acteur a une arme à distance).
        /// </summary>
        public bool TryResolveCombinedSkill(TacticalUnit actor, TacticalUnit target, out SkillType attackSkill)
        {
            return _melee.TryResolveCombinedSkill(actor, target, out attackSkill);
        }

        /// <summary>
        /// RD-084 : Exécute un assaut groupé (Livre VI §25.2). Les participants
        /// sont synchronisés sur l'initiative du membre le plus lent, leurs
        /// résultats offensifs sommés face à l'unique défense de la cible.
        /// </summary>
        public void ExecuteCombinedAttack(TacticalUnit actor, TacticalUnit target, BodyPart targetedPart = BodyPart.Torse)
        {
            _melee.ExecuteCombinedAttack(actor, target, targetedPart);
        }
        /// <summary>
        /// RD-084 : Exécute un round de course-poursuite (Livre VI §25.1) :
        /// concours d'Athlétisme proie vs poursuivants. Succès proie = distance
        /// maintenue (≥ 1 case), faillite = terrain gagné. À 4 faillites cumulées :
        /// arrêt et capture immédiate (Immobilisé).
        /// </summary>
        public void ExecutePursuitRound(TacticalUnit prey, List<TacticalUnit> pursuers)
        {
            _melee.ExecutePursuitRound(prey, pursuers);
        }

        public void RecordChronoSnapshot(string description)
        {
            _chronoService.RecordChronoSnapshot(description);
        }

        public void RewindLastSnapshot()
        {
            _chronoService.RewindLastSnapshot();
        }

        public void RefillAPAll()
        {
            _chronoService.RefillAPAll();
        }

        public void HealAndCureAll()
        {
            _chronoService.HealAndCureAll();
        }

        public void KillCurrentTarget()
        {
            _chronoService.KillCurrentTarget();
        }

        public void RemoveUnit(TacticalUnit unit)
        {
            _chronoService.RemoveUnit(unit);
        }

        public void ResetArena()
        {
            CombatHUD.ResetTelemetry();

            // 0. Nettoyer les armes/objets au sol (gourdins, lames...) du combat précédent.
            DroppedWeaponPickup.ClearAllDropped();
            _smokeZones.Clear();
            _gridVisualizer?.UpdatePersistentZones(_smokeZones);
            CombatUI.DroppedWeaponContextMenuUI.Instance?.CloseMenu();

            // 1. Interrompre toutes les coroutines actives et rétablir le temps réel
            StopAllCoroutines();
            _guard.ForceReset();
            _aiController?.StopAITurn();
            _cinematicDirector?.ResetCinematicState();

            // 2. Fermer les menus contextuels et purger la sélection
            CombatUI.CombatContextMenuUI.Instance?.CloseMenu();
            CombatUI.TacticalSelectionManager.Instance?.ClearSelection();

            // 3. Purger les nœuds de la grille et le gestionnaire de tours
            _grid?.ResetGridState();
            _turnManager?.ClearUnits();
            _turnManager?.ResetCombatState();

            // 4. Nettoyer les surbrillances visuelles
            if (_gridVisualizer != null)
            {
                _gridVisualizer.ClearPathPreview();
                _gridVisualizer.SetHoveredCoord(null);
                _gridVisualizer.SetTargetCoord(null);
                _gridVisualizer.SetReachableCoords(null);
            }

            // 5. Si une carte personnalisée est chargée, la rétablir fidèlement
            if (HasLoadedMap)
            {
                RestoreLoadedMap();
                return;
            }

            // 6. Recréer les unités par défaut
            SetupArenaUnits();

            // 7. Réappliquer le layout d'obstacles et cibler le premier mannequin
            ApplyArenaLayout(_currentLayout);
            if (SparringDummies.Count > 0)
            {
                SelectTarget(SparringDummies[0]);
            }

            // 8. Recentrer la caméra sur le joueur
            if (_cameraController != null && PlayerUnit != null)
            {
                _cameraController.FocusOn(PlayerUnit.transform);
            }

            if (Killtime.Multi.VTTTableSync.Instance != null 
                && !Killtime.Multi.VTTTableSync.Instance.IsApplyingRemoteAction 
                && Killtime.Multi.VTTRoomManager.Instance != null 
                && Killtime.Multi.VTTRoomManager.Instance.InRoom 
                && Killtime.Multi.VTTRoomManager.Instance.IsGM)
            {
                Killtime.Multi.VTTTableSync.Instance.BroadcastArenaReset((int)_currentLayout);
            }

            RecordChronoSnapshot("Réinitialisation de l'Arène");    
            if (KilltimeAudioManager.Instance != null)
            {
                KilltimeAudioManager.Instance.Play(SoundId.Arena_Reset, 0.8f);
                _adaptiveTurnCount = 0;
                KilltimeAudioManager.Instance.PlayMusic(MusicMood.Combat, 0, 0.5f, forceRestart: true);
                UpdateAdaptiveMusic();
            }
            Log("🔄 Arène entièrement réinitialisée et stabilisée.");
        }

        /// <summary>
        /// Pilote la musique adaptative depuis l'état tactique réel :
        /// ratio PV alliés, survivants par camp, avancement des tours.
        /// Appelé sur événements (tour, dégâts, reset) — jamais chaque frame.
        /// </summary>
        private void UpdateAdaptiveMusic()
        {
            var mgr = KilltimeAudioManager.Instance;
            if (mgr == null) return;
            try
            {
                int alliesAlive = 0, enemiesAlive = 0;
                float hpSum = 0f, maxSum = 0f;
                foreach (var u in TacticalUnitRegistry.AllUnits)
                {
                    if (u == null || u.Stats == null) continue;
                    bool alive = u.Stats.IsAlive && !u.Stats.IsSurrendered;
                    if (u.IsPlayerControlled)
                    {
                        if (alive) alliesAlive++;
                        hpSum += Mathf.Max(0, u.Stats.CurrentHealth);
                        maxSum += Mathf.Max(1, u.Stats.MaxHealth);
                    }
                    else if (alive) enemiesAlive++;
                }
                float ratio = maxSum > 0f ? Mathf.Clamp01(hpSum / maxSum) : 1f;
                mgr.NotifyBattleState(ratio, alliesAlive, enemiesAlive, _adaptiveTurnCount);
            }
            catch (System.Exception) { /* audio jamais bloquant */ }
        }

        public bool TryApplyKnockback(TacticalUnit attacker, TacticalUnit defender)
        {
            return _melee.TryApplyKnockback(attacker, defender);
        }

        public TacticalUnit SpawnThomas(HexCoordinates coords, bool isPlayer = true)
        {
            return _unitStore.SpawnThomas(coords, isPlayer);
        }

        public TacticalUnit SpawnJohn(HexCoordinates coords, bool isPlayer = true)
        {
            return _unitStore.SpawnJohn(coords, isPlayer);
        }

        public TacticalUnit SpawnErika(HexCoordinates coords, bool isPlayer = true)
        {
            return _unitStore.SpawnErika(coords, isPlayer);
        }

        public TacticalUnit SpawnVance(HexCoordinates coords, bool isPlayer = true)
        {
            return _unitStore.SpawnVance(coords, isPlayer);
        }

        public void SpawnHeroicTrio(HexCoordinates centerCoords)
        {
            _unitStore.SpawnHeroicTrio(centerCoords);
        }

        public void SpawnHeroicQuatuor(HexCoordinates centerCoords)
        {
            _unitStore.SpawnHeroicQuatuor(centerCoords);
        }

        public void SpawnHeroicQuintet(HexCoordinates centerCoords)
        {
            _unitStore.SpawnHeroicQuintet(centerCoords);
        }

        public void SpawnHeroicSextet(HexCoordinates centerCoords)
        {
            _unitStore.SpawnHeroicSextet(centerCoords);
        }

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

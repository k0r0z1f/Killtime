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
    public class CombatDevArena : MonoBehaviour
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

        public TacticalUnit PlayerUnit { get; private set; }
        public List<TacticalUnit> SparringDummies { get; private set; } = new();
        public TacticalUnit CurrentTarget { get; private set; }

        private readonly List<TacticalUnit> _additionalPlayers = new();
        private readonly List<CustomSpawnRecord> _customSpawns = new();

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
        private ArenaLayoutType _currentLayout;
        private AI.TacticalAIController _aiController;
        private int _adaptiveTurnCount;

        public AI.TacticalAIController AIController => _aiController;

        public TacticalMapSaveData CurrentLoadedMap => _currentLoadedMap;
        public string CurrentLoadedMapPath => _currentLoadedMapPath;
        public bool HasLoadedMap => _currentLoadedMap != null;
        /// <summary>
        /// Émis lorsqu'une carte devient la carte active de l'arène. Le VTT s'en
        /// sert pour publier le chargement du GM sans coupler l'arène au réseau.
        /// </summary>
        public event Action<TacticalMapSaveData> OnLoadedMapRegistered;

        private TacticalMapSaveData _currentLoadedMap;
        private string _currentLoadedMapPath;
        private bool _isAttackInProgress = false;
        /// <summary>Vrai pendant une passe d'armes OU un lancer de grenade (anti double-clic / attente IA).</summary>
        public bool IsResolving => _isAttackInProgress;

        /// <summary>Zones persistantes actives (fumée, gaz, feu...). Purgées au reset, tickées en début de tour.</summary>
        private readonly List<SmokeZone> _smokeZones = new List<SmokeZone>();
        public IReadOnlyList<SmokeZone> SmokeZones => _smokeZones;

        private void Awake()
        {
            _diceRoller = new DiceRoller();
            _combatCalculator = new CombatCalculator(_diceRoller);
            _timelineBranch = new TimelineBranch(TimelineId.Timeline0_Prime);

            Killtime.Core.Arcanotech.ArcanotechWorkshop.DistanceToHeroProvider = (stats, heroName) =>
            {
                if (stats == null || string.IsNullOrEmpty(heroName)) return -1;
                var allUnits = FindObjectsByType<TacticalUnit>();
                TacticalUnit origin = null;
                TacticalUnit hero = null;
                for (int i = 0; i < allUnits.Length; i++)
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
                ? $"Ouverture sur la carte '{_currentLoadedMap?.MapName}' (Temps t0)"
                : "Début de l'affrontement (Temps t0)");

            if (KilltimeAudioManager.Instance != null)
            {
                _adaptiveTurnCount = 0;
                KilltimeAudioManager.Instance.PlayMusic(MusicMood.Combat, 0.5f, true);
                KilltimeAudioManager.Instance.Play(SoundId.Round_Start, 0.8f);
                UpdateAdaptiveMusic();
            }
            Log(defaultMapLoaded
                ? $"🗺️ <b>Carte '{_currentLoadedMap?.MapName}'</b> chargée à l'ouverture. Prête pour les tests."
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
            HandleMouseInteraction();
            UpdateReachableHighlights();
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
        private TacticalUnit _losLoggedAttacker;
        private TacticalUnit _losLoggedTarget;

        /// <summary>
        /// Rafraîchit chaque frame la ligne de visée de détection du couvert entre
        /// l'unité active et la cible courante (bon marché : balayage de quelques cases).
        /// Au changement de cible : log diagnostic (fraction visible, cases et
        /// accessoires bloqueurs, taille du registre 3D).
        /// </summary>
        private void UpdateCoverLineOfSight()
        {
            if (!_showCoverLineOfSight || _gridVisualizer == null)
            {
                _gridVisualizer?.ClearLineOfSight();
                _losLoggedAttacker = null;
                _losLoggedTarget = null;
                return;
            }

            var active = _turnManager != null ? _turnManager.ActiveUnit : null;
            if (active != null && CurrentTarget != null && CurrentTarget.Stats != null && CurrentTarget.Stats.IsAlive)
            {
                _gridVisualizer.ShowLineOfSight(active.CurrentCoords, CurrentTarget.CurrentCoords, _smokeZones, SeesThroughSmoke(active));
                if (active != _losLoggedAttacker || CurrentTarget != _losLoggedTarget)
                {
                    _losLoggedAttacker = active;
                    _losLoggedTarget = CurrentTarget;
                    LogCoverDiagnostics(active, CurrentTarget);
                }
            }
            else
            {
                _gridVisualizer.ClearLineOfSight();
                _losLoggedAttacker = null;
                _losLoggedTarget = null;
            }
        }

        private void LogCoverDiagnostics(TacticalUnit attacker, TacticalUnit defender)
        {
            if (_grid == null) return;
            CoverScanResult scan;
            try { scan = CoverSystem.ScanCover(attacker.CurrentCoords, defender.CurrentCoords, _grid, smokeZones: _smokeZones, seesThroughSmoke: SeesThroughSmoke(attacker)); }
            catch (Exception e)
            {
                Log($"🎯 Ligne de mire : échec du scan ({e.Message}).");
                return;
            }

            var blockers = new System.Collections.Generic.List<string>();
            if (scan.Rays != null)
            {
                var seen = new System.Collections.Generic.HashSet<HexCoordinates>();
                for (int i = 0; i < scan.Rays.Count && blockers.Count < 4; i++)
                {
                    var ray = scan.Rays[i];
                    if (!ray.Blocked || !ray.HasBlockingCell || !seen.Add(ray.BlockingCell)) continue;
                    string tag = $"({ray.BlockingCell.Q},{ray.BlockingCell.R})";
                    var node = _grid.GetNode(ray.BlockingCell);
                    if (node != null && node.Cover != CoverType.None) tag += $"[{node.Cover}]";
                    if (PropObstacleRegistry.TryGet(ray.BlockingCell, out PropObstacle prop))
                        tag += $"[Prop '{prop.PropName}' h={prop.MaxY - prop.MinY:0.##}m r={prop.Radius:0.##}]";
                    blockers.Add(tag);
                }
            }

            Log($"🎯 <b>Ligne de mire {attacker.Stats.Name} ➔ {defender.Stats.Name}</b> :"
                + $" {CoverSystem.Label(scan.Cover)} — {scan.VisibleRays}/{scan.TestedRays} rayons"
                + (blockers.Count > 0 ? $" — bloqueurs : {string.Join(", ", blockers)}" : " — aucun bloqueur")
                + $" — registre 3D : {PropObstacleRegistry.Count} obstacle(s).");
        }

        private void SetupArenaUnits()
        {
            // 0. Combat (ré)initialisé : aucun gourdin / objet du combat précédent ne survit.
            DroppedWeaponPickup.ClearAllDropped();
            _smokeZones.Clear();
            _gridVisualizer?.UpdatePersistentZones(_smokeZones);
            CombatUI.DroppedWeaponContextMenuUI.Instance?.CloseMenu();
            // 1. Purger et neutraliser immédiatement les instances actives (anti-fantômes même frame)
            if (PlayerUnit != null)
            {
                PlayerUnit.gameObject.SetActive(false);
                PlayerUnit.gameObject.name = "[Disposed]";
                Destroy(PlayerUnit.gameObject);
                PlayerUnit = null;
            }

            for (int i = 0; i < SparringDummies.Count; i++)
            {
                var d = SparringDummies[i];
                if (d != null)
                {
                    d.gameObject.SetActive(false);
                    d.gameObject.name = "[Disposed]";
                    Destroy(d.gameObject);
                }
            }
            SparringDummies.Clear();

            for (int i = 0; i < _additionalPlayers.Count; i++)
            {
                var p = _additionalPlayers[i];
                if (p != null)
                {
                    p.gameObject.SetActive(false);
                    p.gameObject.name = "[Disposed]";
                    Destroy(p.gameObject);
                }
            }
            _additionalPlayers.Clear();

            _grid?.ClearOccupancy();

            // Suspendre le démarrage auto du TurnManager pendant l'enregistrement en masse :
            // sinon le 1er RegisterUnit déclenche StartNewRound (et l'IA en FullAuto),
            // puis le StartNewRound final coupe cette IA en plein vol et laisse
            // _isAttackInProgress / cinématique bloqués. Un seul démarrage à la fin.
            bool prevSuspend = _turnManager != null && _turnManager.SuspendAutoStart;
            if (_turnManager != null) _turnManager.SuspendAutoStart = true;
            try
            {
            // 2. Unités de base de l'arène
            PlayerUnit = CreateUnit("Roger (Opératif)", new HexCoordinates(0, 0), 
                new Attributes(4, 3, 4, 4, 3, 2), 
                baseArmor: 1, isPlayer: true);
            PlayerUnit.Sheet.GetSkill(SkillType.Ballistique).TrainingLevel = 1;
            PlayerUnit.Sheet.GetSkill(SkillType.ManiementArmes).TrainingLevel = 1;
            PlayerUnit.Sheet.GetSkill(SkillType.Esquive).TrainingLevel = 1;
            PlayerUnit.Sheet.UnlockedSpecializations.Add("Maniement de l'Épée");
            PlayerUnit.Sheet.UnlockedSpecializations.Add("Pistolet & Tir Rapide");

            var dummyTank = CreateUnit("Sac de Frappe (Tank)", new HexCoordinates(3, -1), 
                new Attributes(1, 1, 1, 6, 4, 1), 
                baseArmor: 2, isPlayer: false);
            dummyTank.Sheet.GetSkill(SkillType.DefenseCorporelle).TrainingLevel = 1;
            dummyTank.Sheet.UnlockedSpecializations.Add("Bloquer");
            SparringDummies.Add(dummyTank);

            var dummyAgile = CreateUnit("Duelliste Agile (Esquive)", new HexCoordinates(2, 2), 
                new Attributes(6, 3, 5, 3, 2, 2), 
                baseArmor: 0, isPlayer: false);
            dummyAgile.Sheet.GetSkill(SkillType.Esquive).TrainingLevel = 2;
            SparringDummies.Add(dummyAgile);

            var dummyArmored = CreateUnit("Garde Blindé (Armure)", new HexCoordinates(-3, 2), 
                new Attributes(2, 2, 2, 5, 4, 1), 
                baseArmor: 4, isPlayer: false);
            dummyArmored.Sheet.GetSkill(SkillType.DefenseCorporelle).TrainingLevel = 2;
            dummyArmored.Sheet.UnlockedSpecializations.Add("Bloquer");
            SparringDummies.Add(dummyArmored);

            _turnManager.RegisterUnit(PlayerUnit);
            PlayerUnit.GetComponent<TacticalUnitVisual>()?.SetCombatStance(true);

            foreach (var d in SparringDummies)
            {
                _turnManager.RegisterUnit(d);
                d.GetComponent<TacticalUnitVisual>()?.SetCombatStance(true);
            }

            // 3. Recréer et réintégrer toutes les unités personnalisées spawnées
            // (relocalisées si leur case d'origine est désormais occupée : jamais d'empilement).
            for (int i = 0; i < _customSpawns.Count; i++)
            {
                var record = _customSpawns[i];
                if (record != null && record.Sheet != null)
                {
                    var finalCoords = ResolveFreeCoords(record.SpawnCoords);
                    var go = new GameObject($"Unit_{record.Sheet.Name.Replace(" ", "_")}");
                    var customUnit = go.AddComponent<TacticalUnit>();
                    customUnit.InitializeFromSheet(record.Sheet, finalCoords, _grid, record.IsPlayer);
                    customUnit.GetComponent<TacticalUnitVisual>()?.SetCombatStance(true);

                    if (record.IsPlayer)
                    {
                        _additionalPlayers.Add(customUnit);
                    }
                    else
                    {
                        SparringDummies.Add(customUnit);
                    }

                    _turnManager.RegisterUnit(customUnit);
                }
            }
            }
            finally
            {
                if (_turnManager != null) _turnManager.SuspendAutoStart = prevSuspend;
            }

            // 4. Démarre le premier tour pour assigner l'unité active
            // État verrouillé avant démarrage : aucune attaque/cinématique orpheline.
            _isAttackInProgress = false;
            _aiController?.StopAITurn();
            if (!IsStorySceneActive()) _cinematicDirector?.ResetCinematicState();
            if (_enterCombatOnMapLoad) _turnManager?.EnterCombatMode(PlayerUnit);
            _turnManager?.StartNewRound();
            _aiController?.TriggerAITurnIfApplicable();
        }

        public TacticalUnit SpawnCustomCharacter(CharacterSheet sheet, HexCoordinates coords, bool isPlayer)
        {
            if (sheet == null || _grid == null) return null;

            // Scène 01 : aucun objet hors-catalogue — migre les vieilles dotations
            // (ex. Pistolet Balistique Lourd) vers leurs équivalents Armurerie/Marché.
            ArmoryCatalog.MigrateLegacyScene01Items(sheet);

            var spawnNode = _grid.GetNode(coords);
            if (spawnNode == null || !spawnNode.IsWalkable)
            {
                Log($"⚠️ Déploiement refusé : case ({coords.Q}, {coords.R}) inexistante ou impraticable.");
                return null;
            }

            // 1 case = 1 avatar : relocalise au lieu d'empiler.
            var finalCoords = ResolveFreeCoords(coords);
            var finalNode = _grid.GetNode(finalCoords);
            if (finalNode == null || !finalNode.IsWalkable || finalNode.IsOccupied)
            {
                Log($"⚠️ Déploiement refusé : aucune case libre autour de ({coords.Q}, {coords.R}).");
                return null;
            }

            var record = new CustomSpawnRecord
            {
                Sheet = sheet,
                SpawnCoords = finalCoords,
                IsPlayer = isPlayer
            };
            _customSpawns.Add(record);

            var go = new GameObject($"Unit_{sheet.Name.Replace(" ", "_")}");
            var unit = go.AddComponent<TacticalUnit>();
            unit.SetFootprint(sheet.Footprint);
            unit.InitializeFromSheet(sheet, finalCoords, _grid, isPlayer);
            unit.GetComponent<TacticalUnitVisual>()?.SetCombatStance(true);

            if (isPlayer)
            {
                _additionalPlayers.Add(unit);
            }
            else
            {
                SparringDummies.Add(unit);
                if (CurrentTarget == null || !CurrentTarget.Stats.IsAlive)
                {
                    SelectTarget(unit);
                }
            }

            _turnManager?.RegisterUnit(unit);
            RecordChronoSnapshot($"Apparition de {unit.Stats.Name} en ({finalCoords.Q}, {finalCoords.R})");
            if (KilltimeAudioManager.Instance != null)
                KilltimeAudioManager.Instance.PlayAt(SoundId.Spawn_Deploy, unit.transform.position, 0.85f);
            if (!finalCoords.Equals(coords))
                Log($"⚡ Unité persistante déployée : <b>{sheet.Name}</b> en ({finalCoords.Q}, {finalCoords.R}) (demandé {coords.Q}, {coords.R} occupé).");
            else
                Log($"⚡ Unité persistante déployée : <b>{sheet.Name}</b> en ({coords.Q}, {coords.R})");

            return unit;
        }

        /// <summary>
        /// Résout une case libre : la case demandée si libre, sinon la plus proche.
        /// Garantit 1 case = 1 avatar (jamais d'empilement).
        /// </summary>
        private HexCoordinates ResolveFreeCoords(HexCoordinates desired, HashSet<HexCoordinates> extraReserved = null)
        {
            if (_grid == null) return desired;
            if (_grid.IsCellFree(desired) && (extraReserved == null || !extraReserved.Contains(desired)))
            {
                return desired;
            }
            if (_grid.TryFindNearestFreeCell(desired, out var free, 8, extraReserved))
            {
                return free;
            }
            return desired;
        }

        public void ClearCustomSpawns()
        {
            _customSpawns.Clear();
        }

        public void RegisterLoadedMap(TacticalMapSaveData mapData, string mapPath = null)
        {
            _currentLoadedMap = mapData;
            _currentLoadedMapPath = mapPath;
            try { OnLoadedMapRegistered?.Invoke(mapData); }
            catch (Exception e) { Debug.LogException(e); }
        }

        public void ClearLoadedMap()
        {
            _currentLoadedMap = null;
            _currentLoadedMapPath = null;
        }

        private void RestoreLoadedMap()
        {
            if (_currentLoadedMap == null) return;

            // Carte ré-initialisée : purge les objets au sol avant re-spawn.
            DroppedWeaponPickup.ClearAllDropped();
            _smokeZones.Clear();
            _gridVisualizer?.UpdatePersistentZones(_smokeZones);
            CombatUI.DroppedWeaponContextMenuUI.Instance?.CloseMenu();
            var mapEditor = MapEditorDevWindow.Instance ?? FindAnyObjectByType<MapEditorDevWindow>();
            if (mapEditor != null)
            {
                mapEditor.ApplyLoadedMap(_currentLoadedMap, _currentLoadedMapPath);
            }
            else
            {
                LoadUnitsFromMap(_currentLoadedMap.PlacedUnits);
            }

            RecordChronoSnapshot($"Réinitialisation de la Carte '{_currentLoadedMap.MapName}'");
            if (KilltimeAudioManager.Instance != null)
            {
                KilltimeAudioManager.Instance.Play(SoundId.Arena_Reset, 0.8f);
                _adaptiveTurnCount = 0;
                KilltimeAudioManager.Instance.PlayMusic(MusicMood.Combat, 0, 0.5f, forceRestart: true);
                UpdateAdaptiveMusic();
            }
            Log($"🔄 Carte '<b>{_currentLoadedMap.MapName}</b>' entièrement réinitialisée.");
        }

        public void ClearAllUnits()
        {
            StopAllCoroutines();
            _isAttackInProgress = false;
            _aiController?.StopAITurn();
            if (!IsStorySceneActive()) _cinematicDirector?.ResetCinematicState();

            CombatUI.CombatContextMenuUI.Instance?.CloseMenu();
            CombatUI.DroppedWeaponContextMenuUI.Instance?.CloseMenu();
            DroppedWeaponPickup.ClearAllDropped();
            _smokeZones.Clear();
            _gridVisualizer?.UpdatePersistentZones(_smokeZones);
            CombatUI.TacticalSelectionManager.Instance?.ClearSelection();

            var allUnits = FindObjectsByType<TacticalUnit>();
            for (int i = 0; i < allUnits.Length; i++)
            {
                var u = allUnits[i];
                if (u != null)
                {
                    if (_grid != null)
                    {
                        var node = _grid.GetNode(u.CurrentCoords);
                        if (node != null) node.IsOccupied = false;
                    }
                    u.gameObject.SetActive(false);
                    u.gameObject.name = "[Disposed]";
                    Destroy(u.gameObject);
                }
            }

            PlayerUnit = null;
            SparringDummies.Clear();
            _additionalPlayers.Clear();
            _customSpawns.Clear();
            CurrentTarget = null;

            _turnManager?.ClearUnits();
            _turnManager?.ResetCombatState();
            _grid?.ClearOccupancy();
        }

        public void LoadUnitsFromMap(List<MapUnitData> mapUnits)
        {
            ClearAllUnits();
            // ClearAllUnits détruit en différé : on repart d'une occupation vierge.
            _grid?.ClearOccupancy();

            if (mapUnits == null || mapUnits.Count == 0)
            {
                RecordChronoSnapshot("Chargement de carte (0 avatar)");
                Log("👥 Aucun avatar défini sur cette carte.");
                return;
            }

            var reserved = new HashSet<HexCoordinates>();
            int relocated = 0;
            bool prevSuspendLoad = _turnManager != null && _turnManager.SuspendAutoStart;
            if (_turnManager != null) _turnManager.SuspendAutoStart = true;
            try
            {
            for (int i = 0; i < mapUnits.Count; i++)
            {
                var unitData = mapUnits[i];
                if (unitData == null || unitData.Sheet == null) continue;

                CharacterSheet effectiveSheet = unitData.Sheet;
                string sheetId = unitData.Sheet.SheetId;
                if (!string.IsNullOrEmpty(sheetId))
                {
                    string idPrefix = sheetId.Length >= 8 ? sheetId[..8] : sheetId;
                    var diskFiles = CharacterStorageService.GetSavedCharacterFiles();
                    for (int f = 0; f < diskFiles.Count; f++)
                    {
                        if (diskFiles[f].IndexOf(idPrefix, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            var diskSheet = CharacterStorageService.LoadCharacter(diskFiles[f]);
                            if (diskSheet != null)
                            {
                                effectiveSheet = diskSheet;
                            }
                            break;
                        }
                    }
                }

                unitData.Sheet = effectiveSheet;

                var requested = new HexCoordinates(unitData.Q, unitData.R);
                var requestedNode = _grid != null ? _grid.GetNode(requested) : null;
                if (requestedNode == null || !requestedNode.IsWalkable)
                {
                    Log($"⚠️ Avatar '<b>{unitData.Sheet.Name}</b>' ignoré : case ({requested.Q}, {requested.R}) invalide.");
                    continue;
                }

                // Déduplique : jamais deux avatars sur la même case.
                var coords = ResolveFreeCoords(requested, reserved);
                var node = _grid.GetNode(coords);
                if (node == null || !node.IsWalkable || node.IsOccupied || reserved.Contains(coords))
                {
                    Log($"⚠️ Avatar '<b>{unitData.Sheet.Name}</b>' ignoré : aucune case libre autour de ({requested.Q}, {requested.R}).");
                    continue;
                }
                if (!coords.Equals(requested)) relocated++;
                reserved.Add(coords);

                string unitObjName = !string.IsNullOrEmpty(unitData.UnitId) 
                    ? unitData.UnitId 
                    : $"Unit_{unitData.Sheet.Name.Replace(" ", "_")}";

                var go = new GameObject(unitObjName);
                var unit = go.AddComponent<TacticalUnit>();
                unit.InitializeFromSheet(unitData.Sheet, coords, _grid, unitData.IsPlayer);
                unit.GetComponent<TacticalUnitVisual>()?.SetCombatStance(true);

                if (unitData.currentHealth > 0 && unit.Stats != null)
                {
                    unit.Stats.CurrentHealth = unitData.currentHealth;
                    unit.Stats.CurrentActionPoints = unitData.currentAP;
                    unit.Stats.Essoufflement = unitData.essoufflement;
                    if (unitData.activeStatus != 0)
                    {
                        unit.Stats.ActiveStatus = (StatusEffect)unitData.activeStatus;
                    }
                }

                if (unitData.IsPlayer)
                {
                    if (PlayerUnit == null)
                    {
                        PlayerUnit = unit;
                    }
                    else
                    {
                        _additionalPlayers.Add(unit);
                    }
                }
                else
                {
                    SparringDummies.Add(unit);
                }

                _customSpawns.Add(new CustomSpawnRecord
                {
                    Sheet = unitData.Sheet,
                    SpawnCoords = coords,
                    IsPlayer = unitData.IsPlayer
                });

                _turnManager?.RegisterUnit(unit);
            }
            }
            finally
            {
                if (_turnManager != null) _turnManager.SuspendAutoStart = prevSuspendLoad;
            }

            if (SparringDummies.Count > 0)
            {
                SelectTarget(SparringDummies[0]);
            }

            if (!IsStorySceneActive() && _cameraController != null)
            {
                if (PlayerUnit != null)
                    _cameraController.FocusOn(PlayerUnit.transform);
                else if (SparringDummies.Count > 0)
                    _cameraController.FocusOn(SparringDummies[0].transform);
            }

            // État verrouillé avant démarrage : évite un _isAttackInProgress orphelin
            // qui ferait ignorer toutes les attaques suivantes (IA bloquée en FullAuto).
            _isAttackInProgress = false;
            _aiController?.StopAITurn();
            if (!IsStorySceneActive()) _cinematicDirector?.ResetCinematicState();

            bool isRemoteClient = TurnManager.IsMultiplayerPlayerClient()
                || (Killtime.Multi.VTTTableSync.Instance != null && Killtime.Multi.VTTTableSync.Instance.IsApplyingRemoteAction);

            if (!isRemoteClient)
            {
                // La carte par défaut (et tout chargement de carte) démarre en combat :
                // StartNewRound() seul ne suffit pas, il est inopérant en exploration.
                if (_enterCombatOnMapLoad) _turnManager?.EnterCombatMode(PlayerUnit);
                _turnManager?.StartNewRound();
                _aiController?.TriggerAITurnIfApplicable();
                if (_enterCombatOnMapLoad && _turnManager != null && !_turnManager.IsInExploration)
                    Log("⚔️ <b>Mode combat enclenché</b> : initiative tirée, tours actifs.");
            }

            RecordChronoSnapshot($"Chargement des unités de la carte ({reserved.Count} avatars)");
            string relocateInfo = relocated > 0 ? $" ({relocated} relocalisé(s) anti-empilement)" : "";
            Log($"👥 <b>{reserved.Count} avatar(s)</b> chargé(s) depuis la configuration de carte{relocateInfo}.");

            // Les accessoires 3D (piliers, caisses) font obstacle à la ligne de mire :
            // (re)construit le registre depuis les instances en scène.
            try
            {
                PropObstacleRegistry.Rebuild(_grid != null ? _grid.HexRadius : 1f);
                Log($"🧱 <b>{PropObstacleRegistry.Count} obstacle(s) 3D</b> enregistré(s) pour la ligne de mire.");
            }
            catch (Exception e) { Debug.LogWarning($"[CombatDevArena] Registre obstacles 3D : {e.Message}"); }
        }

        private TacticalUnit CreateUnit(string unitName, HexCoordinates coords, Attributes attributes, int baseArmor, bool isPlayer)
        {
            var safeCoords = ResolveFreeCoords(coords);
            var go = new GameObject($"Unit_{unitName.Replace(" ", "_")}");
            var unit = go.AddComponent<TacticalUnit>();
            unit.ConfigureStats(unitName, attributes, baseArmor, isPlayer);
            unit.InitializePosition(safeCoords, _grid);

            var visual = unit.GetComponent<TacticalUnitVisual>();
            if (visual != null)
            {
                if (isPlayer)
                {
                    visual.SetColor(new Color(0.15f, 0.45f, 0.85f), new Color(0.0f, 0.95f, 1.0f));
                }
                else
                {
                    visual.SetColor(new Color(0.85f, 0.25f, 0.2f), new Color(1.0f, 0.75f, 0.1f));
                }
            }

            return unit;
        }

        public void ApplyArenaLayout(ArenaLayoutType layout)
        {
            ClearLoadedMap();
            _currentLayout = layout;
            _grid.ClearAllCovers();

            switch (layout)
            {
                case ArenaLayoutType.TheDuelRing:
                    // Arène dégagée, demi-couvertures en périphérie
                    _grid.SetNodeCover(new HexCoordinates(4, 0), CoverType.Half, true);
                    _grid.SetNodeCover(new HexCoordinates(-4, 0), CoverType.Half, true);
                    _grid.SetNodeCover(new HexCoordinates(0, 4), CoverType.Half, true);
                    _grid.SetNodeCover(new HexCoordinates(0, -4), CoverType.Half, true);
                    break;

                case ArenaLayoutType.TacticalBarricades:
                    // Couvertures tactiques réparties pour tester les angles de tir
                    // (Half -1, ThreeQuarters -2, Full = non visible, Livre VI §25.3)
                    _grid.SetNodeCover(new HexCoordinates(1, 0), CoverType.Half, true);
                    _grid.SetNodeCover(new HexCoordinates(1, -2), CoverType.Full, false);
                    _grid.SetNodeCover(new HexCoordinates(-1, 2), CoverType.Half, true);
                    _grid.SetNodeCover(new HexCoordinates(-2, 0), CoverType.Full, false);
                    _grid.SetNodeCover(new HexCoordinates(2, 1), CoverType.Half, true);
                    _grid.SetNodeCover(new HexCoordinates(-1, -2), CoverType.Half, true);
                    _grid.SetNodeCover(new HexCoordinates(2, -1), CoverType.ThreeQuarters, true);
                    _grid.SetNodeCover(new HexCoordinates(-2, 1), CoverType.ThreeQuarters, true);
                    break;

                case ArenaLayoutType.KillzoneChokepoint:
                    // Mur de piliers créant un goulot d'étranglement central
                    _grid.SetNodeCover(new HexCoordinates(0, 1), CoverType.Full, false);
                    _grid.SetNodeCover(new HexCoordinates(0, 2), CoverType.Full, false);
                    _grid.SetNodeCover(new HexCoordinates(0, -1), CoverType.Full, false);
                    _grid.SetNodeCover(new HexCoordinates(0, -2), CoverType.Full, false);
                    _grid.SetNodeCover(new HexCoordinates(1, -1), CoverType.Half, true);
                    _grid.SetNodeCover(new HexCoordinates(-1, 1), CoverType.Half, true);
                    break;
            }

            if (_gridVisualizer != null)
            {
                _gridVisualizer.RefreshObstacles();
            }

            // Pas d'accessoires 3D sur les layouts procéduraux : purge le registre
            // (sinon des piliers d'une carte précédente bloqueraient encore).
            try { PropObstacleRegistry.Rebuild(_grid != null ? _grid.HexRadius : 1f); }
            catch (Exception e) { Debug.LogWarning($"[CombatDevArena] Registre obstacles 3D : {e.Message}"); }

            SaveArenaLayoutPref();
            Log($"📐 Layout d'arène configuré : <b>{layout}</b>");
        }

        private void HandleUnitStatusExpired(TacticalUnit unit, List<StatusEffect> expiredList)
        {
            for (int i = 0; i < expiredList.Count; i++)
            {
                Log($"⏳ <b>{unit.Stats.Name}</b> : L'altération <b>[{expiredList[i]}]</b> s'est dissipée (Fin de tour).");
            }
        }

        private void HandleInitiativeRolled(IReadOnlyList<InitiativeRollResult> results)
        {
            Log("🎲 <b>Jet d'Initiative (Livre I §4.3)</b> — Ordre de passage établi :");
            for (int i = 0; i < results.Count; i++)
            {
                var r = results[i];
                if (r.Unit == null || r.Unit.Stats == null) continue;
                int baseValue = r.Unit.Stats.GetInitiativeBaseValue();
                Log($"{i + 1}. <b>{r.Unit.Stats.Name}</b> — Dé: {r.Die} (Base {baseValue}) | Jet: {r.RawRoll}");
            }
        }

        private void HandleCombatEnded(CombatOutcome outcome)
        {
            var units = FindObjectsByType<TacticalUnit>();
            for (int i = 0; i < units.Length; i++)
            {
                var vis = units[i].GetComponent<TacticalUnitVisual>();
                vis?.SetCombatStance(false);
                // Champs rechargés hors combat (Livre VIII §32.2).
                if (units[i] != null && units[i].Stats != null) units[i].Stats.RefillShield();
            }

            if (KilltimeAudioManager.Instance != null)
            {
                if (outcome == CombatOutcome.Victory)
                {
                    KilltimeAudioManager.Instance.Play(SoundId.Victory_Stinger);
                    KilltimeAudioManager.Instance.PlayStinger(MusicMood.Victory);
                    KilltimeAudioManager.Instance.PlayMusic(MusicMood.Victory, MusicIntensity.Intense, true);
                }
                else if (outcome == CombatOutcome.Defeat)
                {
                    KilltimeAudioManager.Instance.Play(SoundId.Defeat_Stinger);
                    KilltimeAudioManager.Instance.PlayStinger(MusicMood.Defeat);
                    KilltimeAudioManager.Instance.PlayMusic(MusicMood.Defeat, MusicIntensity.Calm, true);
                }
            }
        }

        private void HandleTurnStarted(TacticalUnit unit)
        {
            if (!IsStorySceneActive() && _cameraController != null && unit != null)
            {
                _cameraController.FocusOn(unit.transform);
            }

            if (_turnManager != null && _turnManager.IsInExploration)
            {
                return;
            }

            var visual = unit != null ? unit.GetComponent<TacticalUnitVisual>() : null;
            visual?.SetCombatStance(true);

            if (CurrentTarget == null || CurrentTarget == unit || !CurrentTarget.Stats.IsAlive)
            {
                AutoTargetOpponent(unit);
            }

            if (KilltimeAudioManager.Instance != null && unit != null)
                KilltimeAudioManager.Instance.PlayAt(SoundId.Turn_Start, unit.transform.position, 0.6f);

            _adaptiveTurnCount++;
            UpdateAdaptiveMusic();

            if (unit != null)
            {
                // Moteur Arcanique de Poche : régénération passive de bouclier (+2 PV)
                if (unit.Stats.MaxShieldHP > 0 && unit.Stats.CurrentShieldHP < unit.Stats.MaxShieldHP
                    && SmokeScreen.HasItemByName(unit.Stats, "Moteur"))
                {
                    unit.Stats.CurrentShieldHP = Math.Min(unit.Stats.MaxShieldHP, unit.Stats.CurrentShieldHP + 2);
                    visual?.SpawnFloatingText("⚙️ Moteur Arcanique (+2 Bouclier)", Color.cyan);
                    Log($"⚙️ <b>Moteur Arcanique de Poche</b> : flux continu vers le champ de force de <b>{unit.Stats.Name}</b> (+2 PV Bouclier).");
                }

                Log($"--- Tour de 10s : <b>{unit.Stats.Name}</b> (PA: {unit.Stats.CurrentActionPoints}/{unit.Stats.MaxActionPoints}) ---");
                RecordChronoSnapshot($"Début du tour de {unit.Stats.Name}");
                // RD-045 : DoT résiduel déjà appliqué par TurnManager (début de tour
                // personnel) — ici log + gestion létale, sans re-tick.
                LogDotTick(unit);
                // Zones persistantes : l'unité subit son environnement en début de tour.
                TickSmokeForUnit(unit);
            }
        }

        /// <summary>Décompte des zones à chaque round ; dissipation loggée.</summary>
        private void HandleRoundStarted(int round)
        {
            if (_smokeZones.Count == 0) return;
            for (int i = _smokeZones.Count - 1; i >= 0; i--)
            {
                var z = _smokeZones[i];
                z.TurnsLeft--;
                if (z.TurnsLeft <= 0)
                {
                    Log($"🌫️ La zone <b>{z.Kind}</b> en ({z.Center.Q},{z.Center.R}) se dissipe.");
                    _smokeZones.RemoveAt(i);
                }
                else
                {
                    _smokeZones[i] = z;
                }
            }
            _gridVisualizer?.UpdatePersistentZones(_smokeZones);
        }

        /// <summary>Case couverte par un écran de fumée actif ?</summary>
        public bool IsSmoked(HexCoordinates coords)
        {
            return SmokeScreen.IsCellSmoked(coords, _smokeZones);
        }

        /// <summary>Thermique (spécialisation ou Jumelles) : voit à travers la fumée.</summary>
        public static bool SeesThroughSmoke(TacticalUnit viewer)
        {
            var st = viewer?.Stats;
            if (st == null) return false;
            try
            {
                if (st.HasSpecialization(Killtime.Tactics.Visibility.FogOfWarSystem.SpecThermal)) return true;
            }
            catch { /* fiche partielle */ }
            return SmokeScreen.HasGoggles(st);
        }

        /// <summary>
        /// RD-045 : journalise le tick DoT résiduel du début de tour (déjà appliqué
        /// par TurnManager.RefreshAndNotifyActiveUnitTurnStart) et gère la mort.
        /// </summary>
        public void LogDotTick(TacticalUnit unit)
        {
            if (unit == null || unit.Stats == null) return;
            var tick = unit.Stats.LastDotTick;
            if (tick == null || !tick.HasTick) return;
            var parts = new System.Collections.Generic.List<string>();
            foreach (var kv in tick.DamageByStatus)
            {
                if (kv.Value > 0) parts.Add($"{kv.Value} PV [{kv.Key}]");
            }
            string detail = parts.Count > 0 ? string.Join(" + ", parts) : "effet";
            string extra = "";
            if (tick.ShieldAbsorbed > 0) extra += $", bouclier -{tick.ShieldAbsorbed}";
            if (tick.PaDrained > 0) extra += $", -{tick.PaDrained} PA (asphyxie)";
            Log($"🩸 <b>DoT résiduel</b> : {unit.Stats.Name} subit <b>{detail}</b> (dégressif -1/tour{extra}).");
            for (int i = 0; i < tick.ClearedStatuses.Count; i++)
                Log($"✅ <b>{unit.Stats.Name}</b> : [{tick.ClearedStatuses[i]}] dissipé (résiduels épuisés).");
            // Consomme le tick pour ne pas le re-journaliser (téléport/move).
            unit.Stats.LastDotTick = new Killtime.Core.Character.DotTickResult();
            if (!unit.Stats.IsAlive)
            {
                unit.GetComponent<TacticalUnitVisual>()?.TriggerFallingBackDeath();
                if (CurrentTarget == unit) AutoTargetNextAlive();
                UpdateAdaptiveMusic();
                _turnManager?.CheckCombatOver();
            }
        }

        /// Effets d'environnement en début de tour : dégâts (ignore armure, barrière
        /// d'abord) + statuts de la zone. Masque : filtre les gaz (dégâts /2).
        /// </summary>
        public void TickSmokeForUnit(TacticalUnit unit)
        {
            if (unit == null || unit.Stats == null || !unit.Stats.IsAlive) return;
            if (_smokeZones.Count == 0) return;
            for (int i = 0; i < _smokeZones.Count; i++)
            {
                var z = _smokeZones[i];
                if (z.TurnsLeft <= 0) continue;
                if (unit.CurrentCoords.DistanceTo(z.Center) > Math.Max(0, z.Radius)) continue;

                bool masked = SmokeScreen.HasGasMask(unit.Stats) && SmokeScreen.IsGas(z.Kind);
                StatusEffect fx = z.Statuses;
                if (masked) fx = SmokeScreen.FilterGasStatuses(fx, z.Kind, z.Arcanotech);
                if (fx != StatusEffect.None)
                {
                    unit.Stats.ApplyStatus(fx, 1);
                    unit.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText(
                        $"[{GrenadeCalculator.StatusesToLabel(fx)}]{(masked ? " 😷" : "")}", new Color(1f, 0.4f, 0.95f));
                }

                int dmg = SmokeScreen.ZoneDamagePerTurn(z.Kind);
                if (masked) dmg = SmokeScreen.HalveGasDamage(dmg, z.Kind, z.Arcanotech);
                if (dmg > 0)
                {
                    int shieldRemainder = unit.Stats.AbsorbShield(dmg);
                    int shieldAbs = Math.Max(0, dmg - shieldRemainder);
                    int finalDmg = Math.Max(0, shieldRemainder);
                    if (!SmokeScreen.ZoneIgnoresArmor(z.Kind))
                    {
                        int totalArmor = unit.Stats.BaseArmorAbsorption + unit.Stats.GetWornArmorBonus();
                        int armorAbs = Math.Min(totalArmor, finalDmg);
                        finalDmg = Math.Max(0, finalDmg - armorAbs);
                    }

                    if (finalDmg > 0)
                    {
                        if (unit.Stats.CurrentHealth - finalDmg <= 0)
                        {
                            var fatalRes = unit.Stats.EvaluateFatalBlow(BodyPart.Torse, finalDmg);
                            if (fatalRes == FatalBlowResolution.EligibleForLastBreath)
                            {
                                if (!unit.IsPlayerControlled) unit.Stats.ChooseSombrer();
                                else unit.Stats.ChooseLastBreath();
                            }
                        }
                        else
                        {
                            unit.Stats.CurrentHealth -= finalDmg;
                            var mRes = unit.Stats.CheckMoraleAtThreshold(_diceRoller, CoreRulesConfig.Instance.StandardTargetDC);
                            if (mRes.Triggered)
                            {
                                Log(mRes.Log);
                                var uVis = unit.GetComponent<TacticalUnitVisual>();
                                if (mRes.Surrendered)
                                {
                                    uVis?.SpawnFloatingText("🏳️ REDDITION !", Color.white);
                                    uVis?.DropHeldItemsWithPhysics();
                                    uVis?.SetCombatStance(false);
                                }
                                else if (mRes.Routed)
                                {
                                    uVis?.SpawnFloatingText("😱 DÉROUTE ! [Agonisant]", new Color(0.95f, 0.2f, 0.85f));
                                }
                            }
                        }
                    }

                    unit.GetComponent<TacticalUnitVisual>()?.TriggerHitFlash();
                    unit.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText($"-{finalDmg} PV ({z.Kind})", new Color(1f, 0.30f, 0.20f));
                    Log($"🌫️ <b>Zone {z.Kind}</b> : {unit.Stats.Name} subit <b>{finalDmg} PV</b> (bruts {dmg}{(shieldAbs > 0 ? $", bouclier -{shieldAbs}" : "")}{(masked ? ", masque 😷" : "")}).");
                    if (!unit.Stats.IsAlive)
                    {
                        unit.GetComponent<TacticalUnitVisual>()?.TriggerFallingBackDeath();
                        if (CurrentTarget == unit) AutoTargetNextAlive();
                        UpdateAdaptiveMusic();
                        _turnManager?.CheckCombatOver();
                    }
                }
                else if (fx != StatusEffect.None)
                {
                    Log($"🌫️ <b>Zone {z.Kind}</b> : {unit.Stats.Name} [{GrenadeCalculator.StatusesToLabel(fx)}]{(masked ? " (masque 😷)" : "")}.");
                }
            }
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
            if (_isAttackInProgress) return false;
            var attacker = explicitAttacker != null ? explicitAttacker : (_turnManager != null ? _turnManager.ActiveUnit : PlayerUnit);
            if (attacker == null || attacker.Stats == null || !attacker.Stats.IsAlive) return false;
            if (attacker.IsMoving) return false;

            int cost = CoreRulesConfig.Instance != null ? CoreRulesConfig.Instance.BaseAttackAPCost : 2;
            if (!InfiniteAP && attacker.Stats.CurrentActionPoints < cost)
            {
                Log($"⚠️ PA insuffisants : attaque de décor = {cost} PA (reste {attacker.Stats.CurrentActionPoints}).");
                return false;
            }

            var weapon = attacker.Sheet?.GetEquippedWeapon();
            int dmg = weapon != null && weapon.BaseDamage > 0 ? weapon.BaseDamage : 5;
            bool isMelee = SkillDefinitions.IsMeleeAttackSkill(attackSkill) || (weapon != null && weapon.RangeInTiles <= 1);
            int dist = attacker.CurrentCoords.DistanceTo(targetCoords);
            int maxReach = weapon != null ? Mathf.Max(1, weapon.RangeInTiles) : 1;

            if (dist > maxReach)
            {
                Log($"⚠️ Obstacle hors de portée ({dist} cases > max {maxReach}).");
                return false;
            }

            ObstacleDamageType dmgType = ObstacleDamageType.Perforant;
            if (isMelee)
            {
                dmgType = (attackSkill == SkillType.ManiementArmes && attacker.Stats.HasSpecialization("Marteau de Guerre"))
                    ? ObstacleDamageType.Contondant
                    : (attacker.Stats.HasSpecialization("Hache de Guerre") ? ObstacleDamageType.Tranchant : ObstacleDamageType.Contondant);
            }
            else if (weapon != null && weapon.Category.Contains("Laser"))
            {
                dmgType = ObstacleDamageType.Thermique;
            }

            if (!InfiniteAP) attacker.Stats.ConsumeActionPoints(cost);
            attacker.Stats.RegisterAttack();

            Vector3 dir = targetCoords.ToWorldPosition(_grid != null ? _grid.HexRadius : 1f, 0f) - attacker.transform.position;
            dir.y = 0f;
            if (dir != Vector3.zero) attacker.transform.rotation = Quaternion.LookRotation(dir);

            var attVis = attacker.GetComponent<TacticalUnitVisual>();
            attVis?.TriggerRoundkick();

            if (PropObstacleRegistry.ApplyDamage(targetCoords, dmg, dmgType, _grid, _gridVisualizer, out var obsRes))
            {
                Log($"🎯 <b>{attacker.Stats.Name}</b> attaque le décor en ({targetCoords.Q}, {targetCoords.R}) (-{cost} PA) :\n   {obsRes.Log}");
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayAt(SoundId.Trauma_Shock, attacker.transform.position, 0.7f);
                return true;
            }
            else
            {
                Log($"🎯 <b>{attacker.Stats.Name}</b> frappe en ({targetCoords.Q}, {targetCoords.R}) : aucun obstacle destructible.");
                return false;
            }
        }

        public void ExecuteGrapnel(TacticalUnit attacker, TacticalUnit defender)
        {
            if (_isAttackInProgress)
            {
                Log("⚠️ Lancer ignoré : une résolution est déjà en cours.");
                return;
            }
            if (TurnManager.IsMultiplayerPlayerClient())
            {
                Log("⚠️ Grappin réservé au GM en multijoueur (pas encore synchronisé VTT).");
                return;
            }
            if (attacker == null) attacker = _turnManager != null ? _turnManager.ActiveUnit : PlayerUnit;
            if (attacker == null || attacker.Stats == null) { Log("⚠️ Aucun attaquant pour le grappin."); return; }
            if (defender == null || defender.Stats == null || !defender.Stats.IsAlive) { Log("⚠️ Aucune cible valide pour le grappin."); return; }
            if (attacker.IsMoving) { Log($"⚠️ <b>{attacker.Stats.Name}</b> est en déplacement : attendez l'arrivée !"); return; }
            if (SmokeScreen.FindItemByName(attacker.Stats, "Grappin") == null)
            {
                Log($"⚠️ <b>{attacker.Stats.Name}</b> n'a pas de Grappin Magnétique en poche !");
                return;
            }
            int dist = attacker.CurrentCoords.DistanceTo(defender.CurrentCoords);
            if (dist < 2 || dist > 4)
            {
                Log($"⚠️ <b>Grappin impossible</b> : {dist} cases (portée 2-4).");
                return;
            }
            DiceType atkDie = attacker.Stats.GetSkillDie(SkillType.Ballistique, true);
            if (!attacker.Stats.CanAttack(atkDie)) { Log($"⚠️ <b>{attacker.Stats.Name}</b> a épuisé son quota d'attaque ce tour !"); return; }
            if (!InfiniteAP && attacker.Stats.CurrentActionPoints < 2)
            {
                Log($"⚠️ PA insuffisants : grappin = 2 PA (reste {attacker.Stats.CurrentActionPoints}).");
                return;
            }

            Vector3 dir = defender.transform.position - attacker.transform.position;
            dir.y = 0f;
            if (dir != Vector3.zero) attacker.transform.rotation = Quaternion.LookRotation(dir);
            attacker.GetComponent<TacticalUnitVisual>()?.TriggerGrenadeThrow();

            _combatCalculator.SetContactDistanceState(false);
            CoverType cover = GetCoverToTarget(attacker, defender);
            var throwDuel = _combatCalculator.BeginSkillDuel(
                attacker.Stats, defender.Stats, BodyPart.Torse,
                SkillType.Ballistique, SkillType.Esquive, 3, false,
                true, null, null,
                defender.Stats.BaseArmorAbsorption, out string throwError, cover);
            if (throwDuel == null)
            {
                Log(throwError ?? "⚠️ Grappin impossible : PA insuffisants.");
                return;
            }
            _combatCalculator.DeclareAttackerStakes(throwDuel, 0, 0);
            if (throwDuel.DefenderWantsToDefend && !throwDuel.IsDefenderIncapacitated && throwDuel.CanDefenderReact)
                _combatCalculator.AutoDeclareDefenderStakes(throwDuel);
            else
                _combatCalculator.DeclareDefenderStakes(throwDuel, SkillType.Esquive, false, 0, 0);
            var result = _combatCalculator.ResolveBlindDuel(throwDuel);
            attacker.Stats.RegisterAttack();

            Log($"🪝 <b>HARPON</b> : {attacker.Stats.Name} harponne {defender.Stats.Name} ({dist} cases, -2 PA)\n   {result.CombatLog}");
            HandleChannelInterruptionFx(defender, result.ChannelInterrupted);
            var defVis = defender.GetComponent<TacticalUnitVisual>();
            if (result.IsHit) defVis?.TriggerHitFlash();
            if (result.IsHit && result.Differential >= 2)
            {
                defender.Stats.ApplyStatus(StatusEffect.ATerre, 1);
                defVis?.SpawnFloatingText("[À TERRE] happé !", Color.yellow);
                Log($"🪝 Le grappin fait chuter <b>{defender.Stats.Name}</b> (<b>À Terre</b>, Diff +{result.Differential}).");
            }
            if (!defender.Stats.IsAlive) AutoTargetNextAlive();
            UpdateAdaptiveMusic();
            _turnManager?.CheckCombatOver();
        }

        /// <summary>
        /// Vrai si l'unité active est un allié contrôlé manuellement par le joueur.
        /// En mode FullAuto, l'IA contrôle aussi les alliés : aucun sélecteur de
        /// tuiles de mouvement (portée bleue / aperçu de chemin / clic) ne doit apparaître.
        /// En mode Normal (semi-auto), les alliés restent manuels : les sélecteurs s'affichent.
        /// </summary>
        private bool IsPlayerManualControl(TacticalUnit unit)
        {
            if (unit == null || !unit.IsPlayerControlled || unit.IsMoving) return false;
            if (_turnManager != null && _turnManager.IsInExploration) return true;
            if (_aiController == null) EnsureAIController();
            if (_aiController != null && _aiController.Mode == AI.CombatAIMode.FullAuto) return false;
            return true;
        }

        public void RegisterSceneParty(TacticalUnit player, IEnumerable<TacticalUnit> allies)
        {
            PlayerUnit = player;
            _additionalPlayers.Clear();
            if (allies != null)
            {
                foreach (var ally in allies)
                {
                    if (ally != null && ally != player)
                    {
                        _additionalPlayers.Add(ally);
                    }
                }
            }
        }

        private void HandleMouseInteraction()
        {
            if (CombatHUD.IsPaused)
            {
                // Sur pause : seule la fonction dock des fenêtres flottantes reste active.
                // Clic carte 3D (hors UI/HUD) => repli, sans sélection ni déplacement.
                if (Input.GetMouseButtonDown(0)
                    && !Killtime.UI.FloatingWindowChrome.IsPointerOverAnyWindow())
                {
                    Killtime.UI.FloatingWindowChrome.OnMapClicked();
                }
                return;
            }
            if (UnityEngine.Camera.main == null || _gridVisualizer == null) return;

            // Filet : unité active éventée (transition de scène, acteur détruit)
            // = clics 3D totalement morts. On reprend le premier allié vivant.
            if (_turnManager != null && _turnManager.IsInExploration && _turnManager.ActiveUnit == null)
            {
                var fallback = (PlayerUnit != null && PlayerUnit.Stats != null && PlayerUnit.Stats.IsAlive)
                    ? PlayerUnit : null;
                if (fallback == null)
                {
                    for (int i = 0; i < _additionalPlayers.Count; i++)
                    {
                        var p = _additionalPlayers[i];
                        if (p != null && p.Stats != null && p.Stats.IsAlive) { fallback = p; break; }
                    }
                }
                if (fallback != null) _turnManager.SetActiveUnitExplicit(fallback);
            }

            // Souris sur fenêtre flottante/HUD : interaction exclusive avec l'UI, pas la carte 3D.
            // Évite clics fantômes (mouvement/sélection) et nettoie les survols derrière la fenêtre.
            if (Killtime.UI.FloatingWindowChrome.IsPointerOverAnyWindow())
            {
                _gridVisualizer.SetHoveredCoord(null);
                _gridVisualizer.ClearPathPreview();
                return;
            }

            // Tissage Duo-Tech en cours (dessin T1/T2 ou exécution) : la souris
            // appartient au tissage. Ni survol, ni aperçu de chemin, ni clic de
            // déplacement — sinon le drag de dessin déplacerait l'unité active.
            if (DuoTech.DuoTechWeaveController.AnyWeaving)
            {
                _gridVisualizer.SetHoveredCoord(null);
                _gridVisualizer.ClearPathPreview();
                return;
            }

            Ray ray = UnityEngine.Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                if (_grid.TryGetNodeAtWorldPosition(hit.point, out var hoveredNode))
                {
                    _gridVisualizer.SetHoveredCoord(hoveredNode.Coordinates);

                    var activeUnit = _turnManager.ActiveUnit;
                    if (IsPlayerManualControl(activeUnit))
                    {
                        var start = activeUnit.CurrentCoords;
                        var target = hoveredNode.Coordinates;

                        var hitUnit = hit.collider.GetComponentInParent<TacticalUnit>() ?? GetUnitAtCoords(target);

                        if (Input.GetMouseButtonDown(1))
                        {
                            if (hitUnit != null)
                            {
                                if (hitUnit == activeUnit)
                                {
                                    CombatHUD.Instance?.ToggleActiveUnitContextMenu();
                                    return;
                                }
                                else if (!hitUnit.IsPlayerControlled)
                                {
                                    SelectTarget(hitUnit);
                                }
                            }
                        }

                        if (Input.GetMouseButtonDown(0))
                        {
                            Killtime.UI.FloatingWindowChrome.OnMapClicked();
                            var clickedUnit = hitUnit;

                            if (clickedUnit == activeUnit)
                            {
                                CombatHUD.Instance?.ToggleActiveUnitContextMenu();
                                return;
                            }

                            if (clickedUnit != null && clickedUnit != activeUnit)
                            {
                                if (_turnManager != null && _turnManager.IsInExploration && clickedUnit.IsPlayerControlled)
                                {
                                    SwitchActiveExplorer(clickedUnit);
                                    return;
                                }

                                SelectTarget(clickedUnit);
                                return;
                            }

                            bool isTargetOccupied = clickedUnit != null || hoveredNode.IsOccupied;
                            if (!start.Equals(target) && hoveredNode.IsWalkable && !isTargetOccupied)
                            {
                                // Anti-chevauchement : en exploration on peut changer
                                // d'opérateur pendant qu'un autre marche encore ; deux
                                // trajets simultanés se croisent et empilent les unités
                                // (validations faites sur des positions périmées).
                                if (IsAnotherPlayerUnitMoving(activeUnit))
                                {
                                    Log($"⏳ <b>{activeUnit.Stats.Name}</b> attend la fin du déplacement en cours.");
                                    activeUnit.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("⏳ Un allié marche...", new Color(1f, 0.8f, 0.3f));
                                    return;
                                }
                                // Hors combat : déplacement libre (ni calcul ni coût de PA)
                                // + faufile (on traverse les personnages sans s'y arrêter).
                                bool isExploration = _turnManager != null && _turnManager.IsInExploration;
                                bool traverse = isExploration;
                                int availableAP = isExploration ? ExplorationFreeAP : activeUnit.Stats.CurrentActionPoints;
                                bool hasInvisibleSteps = !isExploration && activeUnit.Stats.HasSpecialization("Protocole des Pas Invisibles") && activeUnit.Stats.MovesThisTurn == 0;
                                if (hasInvisibleSteps) availableAP += 1;

                                if (!activeUnit.Stats.CanMove())
                                {
                                    Log($"⚠️ <b>{activeUnit.Stats.Name}</b> ne peut pas se déplacer ({activeUnit.Stats.ActiveStatus}).");
                                    activeUnit.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("IMMOBILISÉ", Color.yellow);
                                    return;
                                }

                                // Si l'unité maintenait une prise de lutte et se déplace seule (hors traînée), la prise cède
                                if (Killtime.Core.Combat.GrappleState.IsGrappling(activeUnit.Stats))
                                {
                                    Killtime.Core.Combat.GrappleState.ReleaseGrapple(activeUnit.Stats);
                                    Log($"🥋 <b>{activeUnit.Stats.Name}</b> rompt sa prise de lutte pour se déplacer.");
                                }

                                var path = _pathfinder.FindPath(start, target, availableAP, out int apCost, activeUnit.FootprintType, traverse);

                                if (path.Count > 0)
                                {
                                    int effectiveCost = isExploration ? 0 : activeUnit.ComputeMovementAPCost(apCost);
                                    int remainingAP = activeUnit.Stats.CurrentActionPoints - effectiveCost;
                                    string moveTag = isExploration ? "Déplacement libre" : (hasInvisibleSteps ? $"Déplacement (-{effectiveCost} PA / Pas Invisibles)" : $"Déplacement (-{effectiveCost} PA)");
                                    if (isExploration)
                                        Log($"🚶 <b>{activeUnit.Stats.Name}</b> avance de {path.Count - 1} case(s) vers ({target.Q}, {target.R})");
                                    else
                                        Log($"🚶 <b>{activeUnit.Stats.Name}</b> avance de {path.Count - 1} case(s) vers ({target.Q}, {target.R}) [Coût: -{effectiveCost} PA | Restant: {remainingAP} PA]");
                                    activeUnit.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText(moveTag, new Color(0.2f, 0.85f, 1.0f));

                                    _gridVisualizer.ClearPathPreview();
                                    if (KilltimeAudioManager.Instance != null)
                                        KilltimeAudioManager.Instance.PlayAt(SoundId.Move_Dash, activeUnit.transform.position, 0.5f);
                                    StartCoroutine(activeUnit.MoveAlongPath(path, _grid, effectiveCost, traverse));
                                }
                            }
                        }
                        else
                        {
                            // Aperçu du chemin au survol
                            if (!start.Equals(target) && hoveredNode.IsWalkable && !hoveredNode.IsOccupied)
                            {
                                bool previewTraverse = _turnManager != null && _turnManager.IsInExploration;
                                int previewAP = previewTraverse ? ExplorationFreeAP : activeUnit.Stats.CurrentActionPoints;
                                if (!previewTraverse && activeUnit.Stats.HasSpecialization("Protocole des Pas Invisibles") && activeUnit.Stats.MovesThisTurn == 0)
                                    previewAP += 1;

                                var path = _pathfinder.FindPath(start, target, previewAP, out _, activeUnit.FootprintType, previewTraverse);
                                _gridVisualizer.SetPathPreview(path);
                            }
                            else
                            {
                                _gridVisualizer.ClearPathPreview();
                            }
                        }
                    }
                    else
                    {
                        // Mode FullAuto (IA contrôle les alliés) ou tour ennemi :
                        // aucun sélecteur de mouvement (ni aperçu de chemin, ni clic).
                        _gridVisualizer.ClearPathPreview();
                    }
                }
            }
            else
            {
                _gridVisualizer.SetHoveredCoord(null);
                _gridVisualizer.ClearPathPreview();
            }
        }

        private void UpdateReachableHighlights()
        {
            var active = _turnManager.ActiveUnit;
            if (IsPlayerManualControl(active) && _gridVisualizer != null)
            {
                if (!active.Stats.CanMove())
                {
                    _gridVisualizer.SetReachableCoords(null);
                    return;
                }

                bool reachTraverse = _turnManager != null && _turnManager.IsInExploration;
                int reachAP = reachTraverse ? ExplorationFreeAP : active.Stats.CurrentActionPoints;
                if (!reachTraverse && active.Stats.HasSpecialization("Protocole des Pas Invisibles") && active.Stats.MovesThisTurn == 0)
                    reachAP += 1;

                var reachable = _pathfinder.GetReachableCoordinates(active.CurrentCoords, reachAP, reachTraverse);
                _gridVisualizer.SetReachableCoords(reachable);
            }
            else if (_gridVisualizer != null)
            {
                _gridVisualizer.SetReachableCoords(null);
            }
        }

        /// <summary>
        /// Budget PA fictif des déplacements hors combat : libres et illimités.
        /// Largement au-dessus de toute carte (rayon 8 ≈ 217 cases).
        /// </summary>
        private const int ExplorationFreeAP = 100000;

        /// <summary>
        /// Vrai si un autre avatar contrôlé par le joueur est en train de marcher.
        /// Verrou anti-chevauchement pour les ordres de déplacement manuels.
        /// </summary>
        private bool IsAnotherPlayerUnitMoving(TacticalUnit activeUnit)
        {
            var all = FindObjectsByType<TacticalUnit>();
            for (int i = 0; i < all.Length; i++)
            {
                var u = all[i];
                if (u == null || u == activeUnit || !u.IsPlayerControlled || !u.IsMoving) continue;
                return true;
            }
            return false;
        }

        public TacticalUnit GetUnitAtCoords(HexCoordinates coords)
        {
            if (PlayerUnit != null && PlayerUnit.Stats != null && PlayerUnit.Stats.IsAlive && PlayerUnit.CurrentCoords.Equals(coords)) return PlayerUnit;
            for (int i = 0; i < _additionalPlayers.Count; i++)
            {
                var p = _additionalPlayers[i];
                if (p != null && p.Stats != null && p.Stats.IsAlive && p.CurrentCoords.Equals(coords)) return p;
            }
            for (int i = 0; i < SparringDummies.Count; i++)
            {
                var d = SparringDummies[i];
                if (d != null && d.Stats != null && d.Stats.IsAlive && d.CurrentCoords.Equals(coords)) return d;
            }

            var all = FindObjectsByType<TacticalUnit>();
            for (int i = 0; i < all.Length; i++)
            {
                var u = all[i];
                if (u != null && u.Stats != null && u.Stats.IsAlive && u.CurrentCoords.Equals(coords))
                {
                    return u;
                }
            }
            return null;
        }

        public void SelectTarget(TacticalUnit target)
        {
            if (target == null) return;
            if (CurrentTarget == target) return;

            CurrentTarget = target;
            if (_gridVisualizer != null)
            {
                _gridVisualizer.SetTargetCoord(target.CurrentCoords);
            }
            OnTargetChanged?.Invoke(target);
            if (KilltimeAudioManager.Instance != null)
                KilltimeAudioManager.Instance.PlayUI(SoundId.VATS_Lock, 0.7f);
            Log($"🎯 Cible verrouillée : <b>{target.Stats.Name}</b> ({target.Stats.CurrentHealth}/{target.Stats.MaxHealth} PV)");

            if (Killtime.Multi.VTTTableSync.Instance != null 
                && !Killtime.Multi.VTTTableSync.Instance.IsApplyingRemoteAction 
                && Killtime.Multi.VTTRoomManager.Instance != null 
                && Killtime.Multi.VTTRoomManager.Instance.InRoom)
            {
                if (Killtime.Multi.VTTRoomManager.Instance.IsGM)
                {
                    Killtime.Multi.VTTTableSync.Instance.BroadcastTargetSelection(target);
                }
                else
                {
                    Killtime.Multi.VTTTableSync.Instance.RequestTargetSelection(target);
                }
            }
        }

        public void AutoTargetOpponent(TacticalUnit activeUnit)
        {
            if (activeUnit == null) return;

            if (activeUnit.IsPlayerControlled)
            {
                AutoTargetNextAlive();
            }
            else
            {
                if (PlayerUnit != null && PlayerUnit.Stats != null && PlayerUnit.Stats.IsAlive && !PlayerUnit.Stats.IsSurrendered)
                {
                    SelectTarget(PlayerUnit);
                    return;
                }
                for (int i = 0; i < _additionalPlayers.Count; i++)
                {
                    var p = _additionalPlayers[i];
                    if (p != null && p.Stats != null && p.Stats.IsAlive && !p.Stats.IsSurrendered)
                    {
                        SelectTarget(p);
                        return;
                    }
                }
            }
        }

        private void HandleTargetMovedFollow(TacticalUnit unit, List<HexCoordinates> path, int apCost)
        {
            if (CurrentTarget != null && CurrentTarget == unit && _gridVisualizer != null && path != null && path.Count > 0)
            {
                _gridVisualizer.SetTargetCoord(path[path.Count - 1]);
            }
            if (unit != null && unit.Stats != null && unit.Stats.IsAlive && _smokeZones.Count > 0)
            {
                TickSmokeForUnit(unit);
            }
            // RD-030 : l'arrivée en case adverse déclenche les tirs de guet en portée.
            if (unit != null && path != null && path.Count > 1)
            {
                CheckOverwatchTriggers(unit, path[path.Count - 1]);
            }
        }

        private void HandleTargetTeleportedFollow(TacticalUnit unit, HexCoordinates coords)
        {
            if (CurrentTarget != null && CurrentTarget == unit && _gridVisualizer != null)
            {
                _gridVisualizer.SetTargetCoord(coords);
            }
            if (unit != null && unit.Stats != null && unit.Stats.IsAlive && _smokeZones.Count > 0)
            {
                TickSmokeForUnit(unit);
            }
            // RD-030 : la réapparition déclenche les tirs de guet en portée.
            if (unit != null)
            {
                CheckOverwatchTriggers(unit, coords);
            }
        }

        public void CycleTarget()
        {
            if (SparringDummies.Count == 0) return;
            int idx = SparringDummies.IndexOf(CurrentTarget);
            int nextIdx = (idx + 1) % SparringDummies.Count;
            if (KilltimeAudioManager.Instance != null)
                KilltimeAudioManager.Instance.PlayUI(SoundId.VATS_TargetChange, 0.6f);
            SelectTarget(SparringDummies[nextIdx]);
        }

        public void RegisterHostileUnit(TacticalUnit unit)
        {
            if (unit == null || SparringDummies.Contains(unit)) return;
            SparringDummies.Add(unit);
            if (CurrentTarget == null || !CurrentTarget.Stats.IsAlive)
            {
                SelectTarget(unit);
            }
        }

        public void AutoTargetNextAlive()
        {
            for (int i = 0; i < SparringDummies.Count; i++)
            {
                var dummy = SparringDummies[i];
                if (dummy != null && dummy.Stats != null && dummy.Stats.IsAlive && !dummy.Stats.IsSurrendered)
                {
                    SelectTarget(dummy);
                    return;
                }
            }

            if (_turnManager != null)
            {
                var units = _turnManager.TurnOrder;
                for (int i = 0; i < units.Count; i++)
                {
                    var u = units[i];
                    if (u != null && !u.IsPlayerControlled && u.Stats != null && u.Stats.IsAlive && !u.Stats.IsSurrendered)
                    {
                        SelectTarget(u);
                        return;
                    }
                }
            }
        }

        public void SwitchActiveExplorer(TacticalUnit unit)
        {
            if (unit == null) return;
            _turnManager?.SetActiveUnitExplicit(unit);
            if (_cameraController != null)
            {
                _cameraController.FocusOn(unit.transform);
            }
            var vis = unit.GetComponent<TacticalUnitVisual>();
            vis?.SpawnFloatingText($"Opérateur : {unit.Stats.Name}", Color.cyan);
            if (KilltimeAudioManager.Instance != null)
                KilltimeAudioManager.Instance.PlayUI(SoundId.UI_Filter, 0.5f);
            Log($"🕹️ Contrôle basculé sur <b>{unit.Stats.Name}</b>.");
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
            if (attacker == null || defender == null || _grid == null) return CoverType.None;
            if (TitanFootprint.MinDistanceBetweenUnits(attacker.CurrentCoords, attacker.FootprintType, defender.CurrentCoords, defender.FootprintType) <= 1) return CoverType.None;
            return CoverSystem.EvaluateCover(
                attacker.CurrentCoords,
                defender.CurrentCoords,
                _grid,
                defender.FootprintType,
                attacker.FootprintType,
                _smokeZones,
                SeesThroughSmoke(attacker));
        }

        /// <summary>
        /// RD-036 Hauteur / contrebas : élévation d'une unité (0 si grille
        /// absente ou case inconnue — les arènes sans relief restent à 0).
        /// </summary>
        public float GetUnitElevation(TacticalUnit unit)
        {
            if (unit == null || _grid == null) return 0f;
            try
            {
                var node = _grid.GetNode(unit.CurrentCoords);
                if (node != null) return node.Elevation;
            }
            catch { /* ignore */ }
            return 0f;
        }

        /// <summary>
        /// RD-036 : ruling hauteur/contrebas attaquant ➔ défenseur pour le type
        /// d'attaque donné (palier 1.5 m, table mêlée + gate, ±1 balistique,
        /// laser/énergie exempté). Voir ElevationAdvantage.
        /// </summary>
        public ElevationRuling GetElevationRuling(TacticalUnit attacker, TacticalUnit defender, SkillType attackSkill, InventoryItem weapon, bool isMeleeAttack)
        {
            return ElevationAdvantage.Resolve(
                GetUnitElevation(attacker), GetUnitElevation(defender),
                isMeleeAttack, attackSkill, weapon);
        }

        /// <summary>
        /// RD-030 Guet / Overwatch : met l'unité en guet (2 PA, rayon = portée de
        /// l'arme à distance équipée). Le tir de réaction partira automatiquement
        /// pendant le tour adverse (CheckOverwatchTriggers).
        /// </summary>
        public bool TryEnterOverwatch(TacticalUnit unit, out string message)
        {
            message = "";
            if (unit == null || unit.Stats == null) return false;
            var weapon = unit.Sheet?.GetEquippedWeapon();
            int range = weapon != null ? Mathf.Max(1, weapon.RangeInTiles) : 1;
            if (!_combatCalculator.TryEnterOverwatch(unit.Stats, range, out string error))
            {
                message = $"⚠️ {error}";
                Log(message);
                return false;
            }
            var vis = unit.GetComponent<TacticalUnitVisual>();
            vis?.SpawnFloatingText($"👁️ EN GUET ({range} cases, -{CoreRulesConfig.Instance.OverwatchAPCost} PA)", Color.cyan);
            message = $"👁️ <b>{unit.Stats.Name}</b> se met en <b>guet</b> (tir de réaction réservé, rayon {range} cases).";
            Log(message);
            RecordChronoSnapshot($"Guet : {unit.Stats.Name} (rayon {range})");
            return true;
        }

        /// <summary>
        /// RD-030 : après qu'une unité a agi en `atCoords` (fin de déplacement,
        /// téléportation, attaque), chaque guetteur ennemi dont le rayon couvre
        /// cette case lâche son tir de réaction en duel aveugle automatique.
        /// 1 tir par guet, couvert total = pas de tir, munitions consommées.
        /// </summary>
        public void CheckOverwatchTriggers(TacticalUnit mover, HexCoordinates atCoords)
        {
            if (mover == null || mover.Stats == null || !mover.Stats.IsAlive) return;
            if (_turnManager != null && _turnManager.IsInExploration) return;
            if (_turnManager == null) return;
            // Autorité sim : seul le GM/hôte résout les tirs de réaction (sinon
            // client + GM appliqueraient les dégâts en double). Le GM diffuse
            // l'état complet après chaque tir (convergence clients).
            if (TurnManager.IsMultiplayerPlayerClient()) return;

            var order = _turnManager.TurnOrder;
            for (int i = 0; i < order.Count; i++)
            {
                var watcher = order[i];
                if (watcher == null || watcher == mover || watcher.Stats == null) continue;
                if (!watcher.Stats.IsAlive) continue;
                if (watcher.IsPlayerControlled == mover.IsPlayerControlled) continue;
                if (!OverwatchState.IsWatching(watcher.Stats)) continue;
                if (!OverwatchState.TryGetRange(watcher.Stats, out int range)) continue;

                int dist = TitanFootprint.MinDistanceBetweenUnits(
                    watcher.CurrentCoords, watcher.FootprintType, atCoords, mover.FootprintType);
                if (dist > range) continue;

                CoverType cover = CoverType.None;
                if (dist > 1 && _grid != null)
                {
                    cover = CoverSystem.EvaluateCover(
                        watcher.CurrentCoords, atCoords, _grid,
                        mover.FootprintType, watcher.FootprintType,
                        _smokeZones, SeesThroughSmoke(watcher));
                }
                if (cover == CoverType.Full) continue;

                var weapon = watcher.Sheet?.GetEquippedWeapon();
                SkillType owSkill = (weapon != null && weapon.AssociatedSkill == SkillType.ProjectilesTir)
                    ? SkillType.ProjectilesTir
                    : SkillType.Ballistique;
                bool usesAmmo = WeaponAmmo.UsesAmmo(weapon, owSkill);
                if (weapon != null && usesAmmo && (weapon.Jammed || weapon.AmmoRemaining <= 0)) continue;

                int dmg = (weapon != null && weapon.BaseDamage > 0) ? weapon.BaseDamage : 5;
                int bonusEc = weapon != null ? Math.Max(0, weapon.AttackBonusEc) : 0;
                bool hdShot = usesAmmo && weapon != null && weapon.LoadedHD;
                if (hdShot) dmg += 1;

                // RD-036 : le tir de réaction suit la même règle hauteur que le
                // tir direct (balistique ±1, laser exempté — voir ElevationAdvantage).
                ElevationRuling owRuling = GetElevationRuling(watcher, mover, owSkill, weapon, false);
                DamageResult? result = _combatCalculator.ResolveOverwatchFire(
                    watcher.Stats, mover.Stats, dmg, cover, bonusEc, BodyPart.Torse, owSkill,
                    owRuling.AttackMod, owRuling.Label);
                if (result == null) continue;

                if (usesAmmo && weapon != null && result.Value.AttackRoll.RawRoll > 0)
                    ConsumeShotAndCheckJam(watcher, weapon, result.Value, hdShot);

                try { Killtime.Tactics.Visibility.FogOfWarManager.Instance?.NotifyLoudShot(watcher); }
                catch { /* fog optionnel */ }

                var watchVis = watcher.GetComponent<TacticalUnitVisual>();
                var moverVis = mover.GetComponent<TacticalUnitVisual>();
                watchVis?.SpawnFloatingText("👁️ TIR DE RÉACTION", new Color(0.2f, 0.9f, 1.0f));
                Log(result.Value.CombatLog);
                HandleChannelInterruptionFx(mover, result.Value.ChannelInterrupted);
                if (result.Value.IsHit)
                {
                    moverVis?.TriggerHitFlash();
                    moverVis?.SpawnFloatingText($"-{result.Value.FinalDamageApplied} PV (guet)", new Color(1.0f, 0.25f, 0.25f));
                    if (!mover.Stats.IsAlive)
                    {
                        moverVis?.TriggerFallingBackDeath();
                        if (result.Value.FatalResolution == FatalBlowResolution.EligibleForLastBreath)
                        {
                            if (!mover.IsPlayerControlled) mover.Stats.ChooseSombrer();
                            else mover.Stats.ChooseLastBreath();
                        }
                        if (CurrentTarget == mover) AutoTargetNextAlive();
                    }
                }
                else if (result.Value.IsBlocked)
                {
                    moverVis?.SpawnFloatingText("[PARADE] Tir de guet neutralisé", new Color(0.2f, 0.9f, 1.0f));
                }
                RecordChronoSnapshot($"Guet : {watcher.Stats.Name} -> {mover.Stats.Name}");
                UpdateAdaptiveMusic();
                _turnManager?.CheckCombatOver();

                if (Killtime.Multi.VTTTableSync.Instance != null
                    && !Killtime.Multi.VTTTableSync.Instance.IsApplyingRemoteAction
                    && Killtime.Multi.VTTRoomManager.Instance != null
                    && Killtime.Multi.VTTRoomManager.Instance.InRoom
                    && Killtime.Multi.VTTRoomManager.Instance.IsGM)
                {
                    Killtime.Multi.VTTTableSync.Instance.BroadcastFullCombatState();
                }

                if (!mover.Stats.IsAlive) break;
            }
        }

        // =====================================================================
        // RD-031 : OPPORTUNITÉ / DÉSENGAGEMENT (Livre VI §24.4).
        // Quitter le contact (distance ≤ portée → > portée) expose à 1 réaction
        // par voisin ennemi au contact (1 réaction/round chacun, posture au choix :
        // Frappe gratuite, Poursuite 1 PA, Blocage 1 PA, Balayage gratuit).
        // Spé esquive (Roulade de Décrochage, Acrobatie d'Évitement) ou Décrochage
        // payé (1 PA) : aucun déclenchement. Autorité sim = GM/hôte (comme le guet).
        // =====================================================================

        /// <summary>
        /// Voisins ennemis dont le contact est ROMPU par ce pas (≤ portée avant,
        /// > portée après). Rester au contact (pas latéral) ne provoque rien.
        /// </summary>
        private List<TacticalUnit> FindContactBreakers(TacticalUnit mover, HexCoordinates from, HexCoordinates to)
        {
            var result = new List<TacticalUnit>();
            if (mover == null || mover.Stats == null || _turnManager == null) return result;
            int reach = Mathf.Max(0, CoreRulesConfig.Instance.OpportunityReach);
            var order = _turnManager.TurnOrder;
            for (int i = 0; i < order.Count; i++)
            {
                var u = order[i];
                if (u == null || u == mover || u.Stats == null || !u.Stats.IsAlive) continue;
                if (u.IsPlayerControlled == mover.IsPlayerControlled) continue;
                int dFrom = TitanFootprint.MinDistanceBetweenUnits(from, mover.FootprintType, u.CurrentCoords, u.FootprintType);
                if (dFrom > reach) continue;
                int dTo = TitanFootprint.MinDistanceBetweenUnits(to, mover.FootprintType, u.CurrentCoords, u.FootprintType);
                if (dTo <= reach) continue;
                result.Add(u);
            }
            return result;
        }

        /// <summary>
        /// RD-031 Blocage (hook TacticalUnit.StepBlockCheck, avant chaque pas) :
        /// chaque briseur en posture Blocage peut tenter de retenir le fuyard
        /// (duel opposé Athlétisme, 1 PA). Succès = pas annulé, sans grapple ;
        /// les PA du fuyard sont remboursés par MoveAlongPath. Vrai = bloqué.
        /// </summary>
        public bool CheckOpportunityBlock(TacticalUnit mover, HexCoordinates from, HexCoordinates to)
        {
            if (mover == null || mover.Stats == null || !mover.Stats.IsAlive) return false;
            if (_turnManager == null || _turnManager.IsInExploration) return false;
            if (TurnManager.IsMultiplayerPlayerClient()) return false;

            var breakers = FindContactBreakers(mover, from, to);
            if (breakers.Count == 0) return false;

            int blockCost = CoreRulesConfig.Instance.OpportunityBlockAPCost;
            for (int i = 0; i < breakers.Count; i++)
            {
                var blocker = breakers[i];
                if (blocker == null || blocker.Stats == null || !blocker.Stats.IsAlive) continue;
                if (OpportunityState.GetStance(blocker.Stats) != OpportunityReactionType.Block) continue;
                if (!OpportunityState.CanReact(blocker.Stats)) continue;
                // Estimation du coût réel (Ralenti x2 + malus armure) avant engagement.
                int need = blockCost + blocker.Stats.GetWornApMalus();
                if (blocker.Stats.ActiveStatus.HasFlag(StatusEffect.Ralenti))
                    need *= CoreRulesConfig.Instance.RalentiAPMultiplier;
                if (blocker.Stats.CurrentActionPoints < need) continue;
                if (!OpportunityState.TryConsumeReaction(blocker.Stats)) continue;
                if (!blocker.Stats.ConsumeActionPoints(blockCost))
                {
                    OpportunityState.CancelReaction(blocker.Stats);
                    continue;
                }

                var opposed = _combatCalculator.ResolveOpportunityBlock(blocker.Stats, mover.Stats);
                Log(opposed.CombatLog);
                var bVis = blocker.GetComponent<TacticalUnitVisual>();
                bVis?.SpawnFloatingText(opposed.AttackerWins ? "🛡️ BLOCAGE !" : "🛡️ Blocage manqué", new Color(0.4f, 0.8f, 1.0f));
                mover.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText(
                    opposed.AttackerWins ? "⛔ Retenu au contact" : "💨 Esquive le blocage", new Color(1.0f, 0.8f, 0.3f));
                RecordChronoSnapshot($"Opportunité blocage : {blocker.Stats.Name} -> {mover.Stats.Name}");
                BroadcastOpportunityState();
                if (opposed.AttackerWins) return true;
                // Échec : réaction + PA dépensés, le fuyard passe (autres bloqueurs tentent).
            }
            return false;
        }

        /// <summary>
        /// RD-031 (hook TacticalUnit.OnAnyUnitStepCompleted, après chaque pas) :
        /// chaque briseur non-bloqueur réagit selon sa posture — Frappe (gratuite),
        /// Balayage (gratuit, Jambes), Poursuite (1 PA, suit d'1 case).
        /// </summary>
        private void HandleUnitStepOpportunity(TacticalUnit mover, HexCoordinates from, HexCoordinates to, bool provoked)
        {
            if (!provoked) return;
            if (mover == null || mover.Stats == null || !mover.Stats.IsAlive) return;
            if (_turnManager == null || _turnManager.IsInExploration) return;
            if (TurnManager.IsMultiplayerPlayerClient()) return;

            var breakers = FindContactBreakers(mover, from, to);
            for (int i = 0; i < breakers.Count; i++)
            {
                var reactor = breakers[i];
                if (reactor == null || reactor.Stats == null || !reactor.Stats.IsAlive) continue;
                if (!OpportunityState.CanReact(reactor.Stats)) continue;

                var stance = OpportunityState.GetStance(reactor.Stats);
                switch (stance)
                {
                    case OpportunityReactionType.Follow:
                        TryOpportunityFollow(reactor, mover, from);
                        break;
                    case OpportunityReactionType.Trip:
                        if (IsOpportunityMeleeReachable(reactor, mover)
                            && OpportunityState.TryConsumeReaction(reactor.Stats))
                            ResolveOpportunityMelee(reactor, mover, true);
                        break;
                    case OpportunityReactionType.Block:
                        // Déjà résolu avant le pas ( CheckOpportunityBlock ) : le pas a eu
                        // lieu donc le blocage a échoué ou manquait de PA — rien à faire.
                        break;
                    default:
                        if (IsOpportunityMeleeReachable(reactor, mover)
                            && OpportunityState.TryConsumeReaction(reactor.Stats))
                            ResolveOpportunityMelee(reactor, mover, false);
                        break;
                }

                if (!mover.Stats.IsAlive) break;
            }
        }

        /// <summary>
        /// RD-031 Poursuite : suit le fuyard d'1 case (case libérée `from`) pour
        /// 1 PA de réaction. Sans grapple : simple suivi, pas d'attaque.
        /// Le coût est débité UNE fois, par MoveAlongPath (malus armure / Ralenti inclus).
        /// </summary>
        private void TryOpportunityFollow(TacticalUnit follower, TacticalUnit mover, HexCoordinates vacated)
        {
            if (follower == null || mover == null || _grid == null) return;
            if (follower.IsMoving) return;
            int followCost = CoreRulesConfig.Instance.OpportunityFollowAPCost;
            int need = followCost + follower.Stats.GetWornApMalus();
            if (follower.Stats.ActiveStatus.HasFlag(StatusEffect.Ralenti))
                need *= CoreRulesConfig.Instance.RalentiAPMultiplier;
            if (follower.Stats.CurrentActionPoints < need) return;
            if (!_grid.IsFootprintFree(vacated, follower.FootprintType, follower)) return;
            if (!OpportunityState.TryConsumeReaction(follower.Stats)) return;

            var path = new List<HexCoordinates> { follower.CurrentCoords, vacated };
            Log($"👣 <b>OPPORTUNITÉ — poursuite</b> : {follower.Stats.Name} suit {mover.Stats.Name} en {vacated} (-{followCost} PA réaction).");
            follower.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("👣 POURSUITE", new Color(0.4f, 0.9f, 1.0f));
            RecordChronoSnapshot($"Opportunité poursuite : {follower.Stats.Name} -> {mover.Stats.Name}");
            // Mouvement de réaction : coût débité par MoveAlongPath (une seule fois),
            // sans provoquer d'opportunité à son tour (pas de ping-pong).
            // Note : l'arrivée peut toujours déclencher un GUET adverse (RD-030).
            StartCoroutine(follower.MoveAlongPath(path, _grid, followCost, false, false));
            BroadcastOpportunityState();
        }

        /// <summary>
        /// RD-036 : la frappe d'opportunité ne part que si le fuyard est
        /// verticalement atteignable (table mêlée par paliers). Vérifié AVANT
        /// de consommer la réaction : sinon le fuyard est simplement hors
        /// d'atteinte et le réacteur garde sa réaction.
        /// </summary>
        private bool IsOpportunityMeleeReachable(TacticalUnit reactor, TacticalUnit mover)
        {
            if (reactor == null || mover == null) return false;
            var weapon = reactor.Sheet?.GetEquippedWeapon();
            int range = weapon != null ? Math.Max(1, weapon.RangeInTiles) : 1;
            if (!ElevationAdvantage.IsMeleeReachable(GetUnitElevation(reactor), GetUnitElevation(mover), range))
            {
                Log($"⛰️ <b>OPPORTUNITÉ manquée</b> : {mover.Stats.Name} est hors d'atteinte verticale pour {reactor.Stats.Name} — réaction conservée.");
                return false;
            }
            return true;
        }

        /// <summary>
        /// RD-031 Frappe / Balayage : coup de mêlée en duel aveugle auto (réaction
        /// seule, 0 PA par défaut — voir OpportunityStrikeIsFree). Balayage = visée
        /// Jambes (À Terre + Ralenti si choc).
        /// </summary>
        private void ResolveOpportunityMelee(TacticalUnit reactor, TacticalUnit mover, bool trip)
        {
            var cfg = CoreRulesConfig.Instance;
            SkillType skill = CombatCalculator.ResolveOpportunitySkill(reactor.Stats);
            var weapon = reactor.Sheet?.GetEquippedWeapon();
            // RD-036 : modificateur hauteur de la frappe (l'atteignabilité a été
            // vérifiée par l'appelant AVANT de consommer la réaction).
            ElevationRuling oppRuling = ElevationAdvantage.Resolve(
                GetUnitElevation(reactor), GetUnitElevation(mover), true, skill, weapon);
            if (!cfg.OpportunityStrikeIsFree)
            {
                if (!reactor.Stats.ConsumeActionPoints(cfg.BaseReactionAPCost))
                {
                    OpportunityState.CancelReaction(reactor.Stats);
                    return;
                }
            }

            int dmg = (weapon != null && weapon.BaseDamage > 0) ? weapon.BaseDamage : 5;

            DamageResult? result = trip
                ? _combatCalculator.ResolveOpportunityTrip(reactor.Stats, mover.Stats, dmg, skill, oppRuling.AttackMod, oppRuling.Label)
                : _combatCalculator.ResolveOpportunityStrike(reactor.Stats, mover.Stats, dmg, skill, BodyPart.Torse, oppRuling.AttackMod, oppRuling.Label);
            if (result == null) return;

            Log(result.Value.CombatLog);
            HandleChannelInterruptionFx(mover, result.Value.ChannelInterrupted);
            var moverVis = mover.GetComponent<TacticalUnitVisual>();
            reactor.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText(
                trip ? "🦵 BALAYAGE !" : "⚔️ OPPORTUNITÉ !", new Color(1.0f, 0.55f, 0.15f));
            if (result.Value.IsHit)
            {
                moverVis?.TriggerHitFlash();
                moverVis?.SpawnFloatingText($"-{result.Value.FinalDamageApplied} PV (opportunité)", new Color(1.0f, 0.25f, 0.25f));
                if (!mover.Stats.IsAlive)
                {
                    moverVis?.TriggerFallingBackDeath();
                    if (result.Value.FatalResolution == FatalBlowResolution.EligibleForLastBreath)
                    {
                        if (!mover.IsPlayerControlled) mover.Stats.ChooseSombrer();
                        else mover.Stats.ChooseLastBreath();
                    }
                    if (CurrentTarget == mover) AutoTargetNextAlive();
                }
            }
            else if (result.Value.IsBlocked)
            {
                moverVis?.SpawnFloatingText("[PARADE] Opportunité neutralisée", new Color(0.2f, 0.9f, 1.0f));
            }
            RecordChronoSnapshot($"Opportunité {(trip ? "balayage" : "frappe")} : {reactor.Stats.Name} -> {mover.Stats.Name}");
            UpdateAdaptiveMusic();
            _turnManager?.CheckCombatOver();
            BroadcastOpportunityState();
        }

        /// <summary>
        /// RD-031 Décrochage (Livre VI §26.2) : paie 1 PA pour que le prochain
        /// déplacement de l'unité ne provoque aucune réaction d'opportunité.
        /// </summary>
        public bool TryRequestSafeDisengage(TacticalUnit unit, out string message)
        {
            message = "";
            if (unit == null || unit.Stats == null) return false;
            if (!OpportunityState.RequestSafeDisengage(unit.Stats, CoreRulesConfig.Instance.DisengageAPCost, out string error))
            {
                message = $"⚠️ {error}";
                Log(message);
                return false;
            }
            unit.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText(
                $"💨 DÉCROCHAGE (-{CoreRulesConfig.Instance.DisengageAPCost} PA)", Color.cyan);
            message = $"💨 <b>{unit.Stats.Name}</b> prépare son <b>décrochage</b> : prochain déplacement sans réaction d'opportunité.";
            Log(message);
            RecordChronoSnapshot($"Décrochage : {unit.Stats.Name}");
            return true;
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
            message = "";
            if (unit == null || unit.Stats == null) return false;
            if (_turnManager == null) return false;
            if (unit != _turnManager.ActiveUnit)
            {
                message = $"⚠️ Seule l'unité active ({_turnManager.ActiveUnit?.Stats?.Name ?? "—"}) peut tenir son action.";
                Log(message);
                return false;
            }
            if (!_turnManager.TryHoldActiveUnit(out message))
            {
                Log(message);
                return false;
            }
            unit.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("⏸️ EN ATTENTE", Color.cyan);
            Log(message);
            RecordChronoSnapshot($"Tient l'action : {unit.Stats.Name}");
            BroadcastOpportunityState();
            return true;
        }

        /// <summary>
        /// RD-048 Retarder : l'unité active repousse son tour (initiative
        /// -pénalité, 1/round), réinsérée plus tard dans le round en cours.
        /// </summary>
        public bool TryDelayTurn(TacticalUnit unit, out string message)
        {
            message = "";
            if (unit == null || unit.Stats == null) return false;
            if (_turnManager == null) return false;
            if (unit != _turnManager.ActiveUnit)
            {
                message = $"⚠️ Seule l'unité active ({_turnManager.ActiveUnit?.Stats?.Name ?? "—"}) peut retarder son tour.";
                Log(message);
                return false;
            }
            if (!_turnManager.TryDelayActiveUnit(out message))
            {
                Log(message);
                return false;
            }
            unit.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("⏳ RETARDÉ", new Color(1.0f, 0.8f, 0.3f));
            Log(message);
            RecordChronoSnapshot($"Retarde : {unit.Stats.Name}");
            BroadcastOpportunityState();
            return true;
        }

        /// <summary>
        /// RD-048 Canalisation / Action en Progression : étale l'effort sur
        /// plusieurs temps (coût 1 PA, +2 au jet à l'attaque, -1 en défense,
        /// interrompue si dégât net &gt; encaissement).
        /// </summary>
        public bool TryBeginChanneling(TacticalUnit unit, string label, out string message)
        {
            message = "";
            if (unit == null || unit.Stats == null) return false;
            if (!unit.Stats.IsAlive)
            {
                message = $"{unit.Stats.Name} est hors de combat : canalisation impossible.";
                Log(message);
                return false;
            }
            if (!_combatCalculator.TryBeginChanneling(unit.Stats, label, out string error))
            {
                message = $"⚠️ {error}";
                Log(message);
                return false;
            }
            unit.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText($"⏳ CANALISE : {label}", new Color(0.7f, 0.5f, 1.0f));
            message = $"⏳ <b>{unit.Stats.Name}</b> canalise <b>« {label} »</b> (-{CoreRulesConfig.Instance.ChannelStartAPCost} PA) : +{CoreRulesConfig.Instance.ChannelAttackBonus} au jet à l'attaque, {CoreRulesConfig.Instance.ChannelDefensePenalty} en défense, interrompue si dégât &gt; encaissement.";
            Log(message);
            RecordChronoSnapshot($"Canalisation : {unit.Stats.Name} (« {label} »)");
            BroadcastOpportunityState();
            return true;
        }

        /// <summary>
        /// RD-048 : notifie une canalisation rompue par des dégâts hors duel
        /// (grenades, zone, DoT) et affiche le retour visuel. Les duels directs
        /// tracent déjà l'interruption dans leur propre journal de combat.
        /// </summary>
        public void NotifyChannelDamage(TacticalUnit defender, int finalDamageApplied)
        {
            if (defender == null || defender.Stats == null) return;
            if (ChannelingState.NotifyDamage(defender.Stats, finalDamageApplied, out string label))
            {
                defender.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("⏳ CANALISATION ROMPUE", new Color(1.0f, 0.4f, 0.2f));
                Log($"⏳ <b>CANALISATION INTERROMPUE</b> : {defender.Stats.Name} subit {finalDamageApplied} dégâts nets (&gt; encaissement {defender.Stats.EncaissementThreshold}) — « {label} » perdue !");
                RecordChronoSnapshot($"Canalisation rompue : {defender.Stats.Name}");
            }
        }

        /// <summary>
        /// RD-048 : retour visuel d'une interruption tracée par le duel
        /// (direct, guet, opportunité, assaut groupé). Le journal contient déjà
        /// la ligne « CANALISATION INTERROMPUE ».
        /// </summary>
        private void HandleChannelInterruptionFx(TacticalUnit defender, bool interrupted)
        {
            if (!interrupted || defender == null) return;
            defender.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("⏳ CANALISATION ROMPUE", new Color(1.0f, 0.4f, 0.2f));
        }

        /// <summary>
        /// RD-049 Furtivité / Stealth : fait passer l'unité en furtivité (2 PA).
        /// Considéré invisible dans le brouillard de guerre, confère +2 à la
        /// prochaine attaque et interdit toute réaction adverse (embuscade).
        /// </summary>
        public bool TryEnterStealth(TacticalUnit unit, out string message)
        {
            message = "";
            if (unit == null || unit.Stats == null) return false;
            if (!unit.Stats.IsAlive)
            {
                message = $"{unit.Stats.Name} est hors de combat : furtivité impossible.";
                Log(message);
                return false;
            }
            if (StealthState.IsStealthed(unit.Stats))
            {
                message = $"{unit.Stats.Name} est déjà en furtivité.";
                Log(message);
                return false;
            }
            int cost = StealthState.EnterStealthAPCost;
            if (!InfiniteAP && unit.Stats.CurrentActionPoints < cost)
            {
                message = $"⚠️ PA insuffisants : furtivité = {cost} PA (reste {unit.Stats.CurrentActionPoints}).";
                Log(message);
                return false;
            }
            if (!InfiniteAP) unit.Stats.ConsumeActionPoints(cost);

            StealthState.SetStealth(unit.Stats, true);
            var vis = unit.GetComponent<TacticalUnitVisual>();
            vis?.SpawnFloatingText($"🥷 FURTIVITÉ (-{cost} PA)", new Color(0.2f, 0.95f, 0.75f));
            message = $"🥷 <b>{unit.Stats.Name}</b> passe en <b>furtivité</b> (-{cost} PA) : invisible, +2 prochaine attaque, cible surprise.";
            Log(message);
            Killtime.Tactics.Visibility.FogOfWarManager.Instance?.RefreshFog("stealth");
            RecordChronoSnapshot($"Furtivité : {unit.Stats.Name}");
            return true;
        }

        /// <summary>Diffuse l'état après une réaction (GM seul, comme le guet).</summary>
        private void BroadcastOpportunityState()
        {
            try
            {
                if (Killtime.Multi.VTTTableSync.Instance != null
                    && !Killtime.Multi.VTTTableSync.Instance.IsApplyingRemoteAction
                    && Killtime.Multi.VTTRoomManager.Instance != null
                    && Killtime.Multi.VTTRoomManager.Instance.InRoom
                    && Killtime.Multi.VTTRoomManager.Instance.IsGM)
                {
                    Killtime.Multi.VTTTableSync.Instance.BroadcastFullCombatState();
                }
            }
            catch { /* VTT optionnel */ }
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
            if (_isAttackInProgress)
            {
                Log("⚠️ Attaque ignorée : une résolution est déjà en cours (anti double-clic).");
                return;
            }

            // Déclaration de l'attaquant : mise PA (+ PE) engagée avant tout jet, cachée.
            if (attackerBonusAP <= 0 && CombatUI.CombatContextMenuUI.CurrentInjectedAP > 0)
            {
                attackerBonusAP = CombatUI.CombatContextMenuUI.CurrentInjectedAP;
            }
            if (attackerPE <= 0 && CombatUI.CombatContextMenuUI.CurrentInjectedPE > 0)
            {
                attackerPE = CombatUI.CombatContextMenuUI.CurrentInjectedPE;
            }

            // Cohérence affichage/exécution : le menu contextuel calcule les coûts depuis
            // GetActiveActor() (unité active en combat). Si l'appelant fournit l'attaquant
            // explicite (menu radial), il fait foi ; sinon repli sur l'unité active.
            // Avant, l'exécution ignorait toujours l'acteur du menu, d'où un attaquant à
            // 0 PA pour la cible de derrière alors que le menu affichait une unité pleine.
            var attacker = explicitAttacker != null ? explicitAttacker : _turnManager.ActiveUnit;
            var defender = explicitTarget != null ? explicitTarget : CurrentTarget;

            if (defender != null && CurrentTarget != defender)
            {
                SelectTarget(defender);
            }

            if (_turnManager != null && _turnManager.IsInExploration)
            {
                Log("🚨 <b>COUP DE FEU EN EXPLORATION !</b> Passage immédiat en combat tactique.");
                _turnManager.EnterCombatMode(attacker);
            }

            if (TurnManager.IsMultiplayerPlayerClient())
            {
                if (attacker != null && defender != null)
                {
                    Killtime.Multi.VTTTableSync.Instance?.RequestAttack(
                        attacker,
                        defender,
                        targetedPart,
                        cancelPenaltyWithAP,
                        attackSkill,
                        defenseSkill,
                        attackerBonusAP,
                        attackerPE
                    );
                }
                return;
            }

            if (attacker == null || defender == null)
            {
                Log("⚠️ Impossible d'attaquer : aucune cible active.");
                return;
            }

            if (!defender.Stats.IsAlive)
            {
                Log($"⚠️ {defender.Stats.Name} est déjà hors de combat !");
                return;
            }

            // Garde anti-état transitoire : les CurrentCoords ne sont fiables qu'une fois
            // le déplacement terminé (MoveAlongPath met à jour case par case). Attaquer
            // en plein mouvement figeait une distance/skill obsolète (ex : mêlée jouée
            // alors que l'unité venait de sortir de portée).
            if (attacker.IsMoving)
            {
                Log($"⚠️ <b>{attacker.Stats.Name}</b> est en déplacement : attendez l'arrivée avant d'attaquer !");
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayUI(SoundId.UI_Denied, 0.6f);
                return;
            }

            int distanceToTarget = TitanFootprint.MinDistanceBetweenUnits(attacker.CurrentCoords, attacker.FootprintType, defender.CurrentCoords, defender.FootprintType);
            var activeWeapon = attacker.Sheet?.GetEquippedWeapon();
            bool hasRifleVisual = attacker.GetComponent<TacticalUnitVisual>()?.HasRifleEquipped() == true;
            bool hasRangedArmamentForCorrection = (activeWeapon != null && activeWeapon.RangeInTiles > 1) || hasRifleVisual;

            // Auto-correction autoritaire : une compétence de mêlée figée (menu dév,
            // menu contextuel, HUD dessiné avant fin de déplacement) ne doit jamais
            // produire une animation de coup de poing à distance. Hors de portée de
            // contact avec une arme à distance disponible => bascule en Ballistique.
            if (distanceToTarget > 1 && hasRangedArmamentForCorrection
                && SkillDefinitions.IsMeleeAttackSkill(attackSkill))
            {
                Log($"🔫 <b>{attacker.Stats.Name}</b> hors de contact ({distanceToTarget} cases) : bascule automatique en Tir (Ballistique).");
                attackSkill = SkillType.Ballistique;
            }

            bool isMeleeAttack = (SkillDefinitions.IsMeleeAttackSkill(attackSkill)
                               || (activeWeapon != null && activeWeapon.RangeInTiles <= 1));

            int maxReach = activeWeapon != null ? Mathf.Max(1, activeWeapon.RangeInTiles) : 1;
            if (hasRifleVisual && maxReach < 6) maxReach = 8;

            // RD-036 Hauteur / contrebas (palier 1.5 m) : gate verticale en
            // mêlée (inatteignable au-delà d'1 palier sans allonge, 2 avec),
            // bonus/malus au jet sinon. Le tir n'a pas de gate (vue tranche).
            ElevationRuling elevationRuling = GetElevationRuling(attacker, defender, attackSkill, activeWeapon, isMeleeAttack);
            if (isMeleeAttack && !elevationRuling.Reachable)
            {
                Log($"⛰️ <b>Hors d'atteinte verticale</b> : {defender.Stats.Name} est {elevationRuling.Label} pour {attacker.Stats.Name} — rapprochez-vous du même palier ou utilisez une arme à allonge / à distance !");
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayUI(SoundId.UI_Denied, 0.6f);
                return;
            }
            if (elevationRuling.AttackMod != 0)
            {
                Log($"⛰️ <b>Hauteur</b> : {attacker.Stats.Name} ➔ {defender.Stats.Name} {elevationRuling.Label} ({elevationRuling.AttackMod:+0;-0} attaque).");
            }

            if (isMeleeAttack && distanceToTarget > maxReach)
            {
                Log($"⚠️ <b>Attaque de contact impossible</b> : {defender.Stats.Name} est hors de portée ({distanceToTarget} cases > portée max {maxReach}) !");
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayUI(SoundId.UI_Denied, 0.6f);
                return;
            }

            bool isRangedAttackSkill = (attackSkill == SkillType.Ballistique || attackSkill == SkillType.ProjectilesTir);
            bool hasRangedArmament = (activeWeapon != null && activeWeapon.RangeInTiles > 1) || hasRifleVisual;

            // RD-038 : Le sprint bloque tout tir à distance ce tour
            if ((isRangedAttackSkill || (!isMeleeAttack && distanceToTarget > 1)) && !ChargeState.CanFireRanged(attacker.Stats))
            {
                Log($"⚠️ <b>Tir impossible</b> : {attacker.Stats.Name} a sprinté ce tour ! (RD-038 : Le sprint bloque le tir)");
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayUI(SoundId.UI_Denied, 0.6f);
                return;
            }

            if (isRangedAttackSkill && !hasRangedArmament && distanceToTarget > 1)
            {
                Log($"⚠️ <b>Tir impossible</b> : {attacker.Stats.Name} ne possède aucune arme à distance ({distanceToTarget} cases) !");
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayUI(SoundId.UI_Denied, 0.6f);
                return;
            }

            if (!isMeleeAttack && distanceToTarget > maxReach)
            {
                Log($"⚠️ <b>Tir impossible</b> : {defender.Stats.Name} est hors de portée ({distanceToTarget} cases > portée max {maxReach}) !");
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayUI(SoundId.UI_Denied, 0.6f);
                return;
            }

            // Livre VI §25.3 — Couvert & Visibilité : la cible totalement à couvert
            // (non visible pour l'attaquant) ne peut pas être attaquée directement.
            CoverType attackCover = GetCoverToTarget(attacker, defender);
            if (attackCover == CoverType.Full)
            {
                Log($"🛡️ <b>Couvert total</b> : {defender.Stats.Name} est non visible pour {attacker.Stats.Name} — attaque impossible ! Déplacez-vous pour retrouver une ligne de mire.");
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayUI(SoundId.UI_Denied, 0.6f);
                return;
            }

            DiceType resolvedAttackDie = attackDie ?? attacker.Stats.GetSkillDie(attackSkill, true);
            DiceType resolvedDefenseDie = defenseDie ?? defender.Stats.GetSkillDie(defenseSkill);

            int maxAttacks = attacker.Stats.GetMaxAttacksAllowed(resolvedAttackDie);
            if (!attacker.Stats.CanAttack(resolvedAttackDie))
            {
                Log($"⚠️ <b>{attacker.Stats.Name}</b> a déjà épuisé son quota d'attaque ce tour ({attacker.Stats.AttacksThisTurn}/{maxAttacks}) ! Requis pour 2 attaques : palier 2d6, 2d8, 2d10 ou 2d12.");
                return;
            }

            // RD-033 Munitions : un tir consomme 1 coup ; vide ou enrayé = refus sans PA.
            // Mêlée au contact avec l'arme (coup de crosse) : aucun coup consommé.
            bool ammoFiredHere = WeaponAmmo.UsesAmmo(activeWeapon, attackSkill);
            if (ammoFiredHere)
            {
                if (activeWeapon.Jammed)
                {
                    Log($"🔧 <b>Enrayée</b> : {activeWeapon.Name} de {attacker.Stats.Name} est enrayée ! Désenrayez (1 PA).");
                    if (KilltimeAudioManager.Instance != null)
                        KilltimeAudioManager.Instance.PlayUI(SoundId.UI_Denied, 0.6f);
                    return;
                }
                if (activeWeapon.AmmoRemaining <= 0)
                {
                    Log($"🔋 <b>Chargeur vide</b> : {activeWeapon.Name} ({attacker.Stats.Name}) — rechargez ({activeWeapon.ReloadAPCost} PA).");
                    if (KilltimeAudioManager.Instance != null)
                        KilltimeAudioManager.Instance.PlayUI(SoundId.UI_Denied, 0.6f);
                    return;
                }
            }

            // Duel aveugle Livres II §7 + VI §24.1 : les mises (PA bonus + PE) se déclarent
            // AVANT les jets et restent cachées jusqu'à la révélation simultanée.
            // Bonus explicite (>= 0) : mise déclarée du défenseur (fenêtre paramétrage).
            // Bonus auto (< 0) : déclaration aveugle auto (estimation sans voir le jet adverse).
            bool autoReactiveDefense = defenderBonusAP < 0;
            int resolvedDefenderBonus = autoReactiveDefense ? 0 : Mathf.Max(0, defenderBonusAP);

            Vector3 combatDir = defender.transform.position - attacker.transform.position;
            combatDir.y = 0f;
            if (combatDir != Vector3.zero)
            {
                attacker.transform.rotation = Quaternion.LookRotation(combatDir);
                if (defender.Stats.CanDefendActively())
                {
                    defender.transform.rotation = Quaternion.LookRotation(-combatDir);
                }
            }

            var attVisual = attacker.GetComponent<TacticalUnitVisual>();
            var defVisual = defender.GetComponent<TacticalUnitVisual>();
            bool isMeleeStrike = SkillDefinitions.IsMeleeAttackSkill(attackSkill);

            // Brouillard de guerre asymétrique : un tir bruyant révèle le tireur
            // dans le rayon d'Ouïe jusqu'à la fin du round suivant (sauf mur Full).
            // La mêlée reste silencieuse (pas de révélation).
            if (!isMeleeStrike)
            {
                try { Killtime.Tactics.Visibility.FogOfWarManager.Instance?.NotifyLoudShot(attacker); }
                catch { /* fog optionnel */ }
            }

            bool isAttackerStealthed = StealthState.IsStealthed(attacker.Stats);

            // Recalcul au moment du déclenchement : la distance a pu changer entre le clic
            // et l'impact (fin de déplacement, cinématique). Le visuel doit refléter la
            // position réelle, pas celle figée au clic.
            System.Func<bool> isMeleeAtTrigger = () =>
            {
                if (attacker == null || defender == null) return isMeleeStrike;
                int freshDist = attacker.CurrentCoords.DistanceTo(defender.CurrentCoords);
                bool skillIsMelee = SkillDefinitions.IsMeleeAttackSkill(attackSkill);
                return skillIsMelee && freshDist <= 1;
            };

            Action triggerKickAction = () =>
            {
                if (isMeleeAtTrigger())
                {
                    attVisual?.TriggerRoundkick(forceKick: true);
                }
                else
                {
                    attVisual?.TriggerRoundkick();
                }

                if (KilltimeAudioManager.Instance != null && attacker != null)
                    KilltimeAudioManager.Instance.PlayAt(SoundId.Attack_Whoosh, attacker.transform.position, 0.7f);
            };

            Action triggerDefenseAction = () =>
            {
                if (!isAttackerStealthed && defender != null && defender.Stats != null && defender.Stats.CanDefendActively())
                {
                    defVisual?.TriggerBodyBlock();
                    if (KilltimeAudioManager.Instance != null)
                        KilltimeAudioManager.Instance.PlayAt(SoundId.Defense_Block, defender.transform.position, 0.5f);
                }
            };

            TacticalUnit aiInterceptionProtector = null;
            if (!isAttackerStealthed && (!defender.IsPlayerControlled || (_aiController != null && _aiController.Mode == AI.CombatAIMode.FullAuto)))
            {
                var aiProtector = Killtime.Tactics.CombatUI.CombatTechniqueRegistry.FindBestAIProtector(defender);
                if (aiProtector != null)
                {
                    if (Killtime.Tactics.CombatUI.CombatTechniqueRegistry.ExecuteIntercept(aiProtector, defender, this))
                    {
                        aiInterceptionProtector = aiProtector;
                        defender = aiProtector;
                        defVisual = defender.GetComponent<TacticalUnitVisual>();
                    }
                }
            }

            bool hasPlayerProtector = !isAttackerStealthed && Killtime.Tactics.CombatUI.CombatTechniqueRegistry.HasPlayerControlledProtector(defender);
            bool isInteractivePlayerDefense = !isAttackerStealthed
                && (defender.IsPlayerControlled || hasPlayerProtector) 
                && (_aiController == null || _aiController.Mode != AI.CombatAIMode.FullAuto)
                && defenderBonusAP < 0
                && !TurnManager.IsMultiplayerPlayerClient();

            if (isInteractivePlayerDefense)
            {
                _isAttackInProgress = true;
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayUI(SoundId.VATS_Fire, 0.85f);

                StartCoroutine(ExecuteAttackWithPlayerDefenseRoutine(
                    attacker, defender, targetedPart, cancelPenaltyWithAP,
                    attackSkill, defenseSkill, weaponBaseDamage, attackerBonusAP, attackerPE,
                    attackerSpecialization, defenderSpecialization, triggerKickAction, triggerDefenseAction));
                return;
            }

            Action resolveAction = () =>
            {
                attacker.Stats.RegisterAttack();

                int effectiveWeaponDamage = weaponBaseDamage;
                var equippedWeapon = attacker.Sheet?.GetEquippedWeapon();
                if (equippedWeapon != null && equippedWeapon.BaseDamage > 0)
                {
                    effectiveWeaponDamage = equippedWeapon.BaseDamage;
                }
                int weaponBonusEc = equippedWeapon != null ? Math.Max(0, equippedWeapon.AttackBonusEc) : 0;
                // RD-033 : charge Haute Densité chambrée = +1 dégât (sniper/Deglazer).
                bool hdShot = ammoFiredHere && equippedWeapon != null && equippedWeapon.LoadedHD;
                if (hdShot) effectiveWeaponDamage += 1;

                _combatCalculator.SetContactDistanceState(attacker.IsCanonEntrave());

                // Recalcul frais au moment de l'impact (positions réelles) : si la
                // cible est passée derrière un couvert total entre le clic et la
                // frappe, BeginDuel refusera et le log l'expliquera (aucun jet).
                CoverType freshCover = GetCoverToTarget(attacker, defender);

                DamageResult result;
                AttackDuel duel = null;
                if (autoReactiveDefense)
                {
                    string duelError = null;
                    if (attackDie.HasValue || defenseDie.HasValue)
                    {
                        DiceType reactiveDefDie = resolvedDefenseDie;
                        if (!SkillDefinitions.IsExclusivelyDefensive(defenseSkill))
                        {
                            bool hasDefSpec = (!string.IsNullOrEmpty(defenderSpecialization) && defender.Stats.HasSpecialization(defenderSpecialization))
                                            || SkillDefinitions.HasDefensiveSpecialization(defender.Stats.Sheet, defenseSkill);
                            if (!defenseDie.HasValue && !hasDefSpec)
                                reactiveDefDie = SkillDefinitions.StepDownDie(reactiveDefDie);
                        }
                        duel = _combatCalculator.BeginDuel(
                            attacker.Stats, defender.Stats, targetedPart,
                            resolvedAttackDie, attacker.Stats.GetSkillModifier(attackSkill, isOffensive: true),
                            reactiveDefDie, defender.Stats.GetSkillModifier(defenseSkill, isOffensive: false),
                            effectiveWeaponDamage, cancelPenaltyWithAP, defenderWantsToDefend,
                            defender.Stats.BaseArmorAbsorption, attackSkill, defenseSkill, out duelError, freshCover, weaponBonusEc,
                            false, elevationRuling.AttackMod, elevationRuling.Label);
                    }
                    else
                    {
                        duel = _combatCalculator.BeginSkillDuel(
                            attacker.Stats, defender.Stats, targetedPart,
                            attackSkill, defenseSkill, effectiveWeaponDamage, cancelPenaltyWithAP,
                            defenderWantsToDefend, attackerSpecialization, defenderSpecialization,
                            defender.Stats.BaseArmorAbsorption, out duelError, freshCover, weaponBonusEc,
                            false, elevationRuling.AttackMod, elevationRuling.Label);
                    }
                    if (duel == null)
                    {
                        Log(duelError ?? "⚠️ Attaque impossible : PA insuffisants pour le coût de base.");
                        return;
                    }
                    else
                    {
                        if (aiInterceptionProtector != null)
                        {
                            duel.DefenderBasePaid = true;
                            duel.CanDefenderReact = !duel.IsDefenderIncapacitated;
                        }

                        // Duel aveugle auto : déclaration attaquant (mise cachée), puis
                        // déclaration aveugle auto du défenseur (estimation sans voir le jet),
                        // puis révélation simultanée. Aucun résultat révélé entre les deux.
                        _combatCalculator.DeclareAttackerStakes(duel, attackerBonusAP, attackerPE);
                        if (duel.DefenderWantsToDefend && !duel.IsDefenderIncapacitated && duel.CanDefenderReact)
                        {
                            _combatCalculator.AutoDeclareDefenderStakes(duel, defenderSpecialization);
                        }
                        else
                        {
                            _combatCalculator.DeclareDefenderStakes(duel, defenseSkill, false, 0, 0, defenderSpecialization);
                        }
                        result = _combatCalculator.ResolveBlindDuel(duel);
                    }
                }
                else if (attackDie.HasValue || defenseDie.HasValue)
                {
                    result = _combatCalculator.ResolveTargetedAttack(
                        attacker: attacker.Stats,
                        defender: defender.Stats,
                        targetedPart: targetedPart,
                        attackDie: resolvedAttackDie,
                        attackModifier: attacker.Stats.GetSkillModifier(attackSkill, isOffensive: true),
                        defenseDie: resolvedDefenseDie,
                        defenseModifier: defender.Stats.GetSkillModifier(defenseSkill, isOffensive: false),
                        weaponBaseDamage: effectiveWeaponDamage,
                        cancelPenaltyWithAP: cancelPenaltyWithAP,
                        defenderArmor: defender.Stats.BaseArmorAbsorption,
                        attackerBonusAP: attackerBonusAP,
                        defenderBonusAP: resolvedDefenderBonus,
                        defenderWantsToDefend: defenderWantsToDefend,
                        attackSkill: attackSkill,
                        defenseSkill: defenseSkill,
                        attackerPE: attackerPE,
                        defenderPE: defenderPE,
                        cover: freshCover,
                        weaponBonusEc: weaponBonusEc
                    );
                }
                else
                {
                    result = _combatCalculator.ResolveTargetedAttack(
                        attacker: attacker.Stats,
                        defender: defender.Stats,
                        targetedPart: targetedPart,
                        attackSkill: attackSkill,
                        defenseSkill: defenseSkill,
                        weaponBaseDamage: effectiveWeaponDamage,
                        cancelPenaltyWithAP: cancelPenaltyWithAP,
                        defenderWantsToDefend: defenderWantsToDefend,
                        attackerSpecialization: attackerSpecialization,
                        defenderSpecialization: defenderSpecialization,
                        defenderArmor: defender.Stats.BaseArmorAbsorption,
                        attackerBonusAP: attackerBonusAP,
                        defenderBonusAP: resolvedDefenderBonus,
                        attackerPE: attackerPE,
                        defenderPE: defenderPE,
                        cover: freshCover,
                        weaponBonusEc: weaponBonusEc
                    );
                }

                Log(result.CombatLog);
                HandleChannelInterruptionFx(defender, result.ChannelInterrupted);

                // Dégâts collatéraux sur l'obstacle si l'attaque est absorbée par le couvert ou neutralisée
                if (result.IsBlocked && freshCover != CoverType.None && _grid != null)
                {
                    ObstacleDamageType directDmgType = ObstacleDamageType.Perforant;
                    if (isMeleeAtTrigger())
                    {
                        directDmgType = (attackSkill == SkillType.ManiementArmes && attacker.Stats.HasSpecialization("Marteau de Guerre"))
                            ? ObstacleDamageType.Contondant
                            : (attacker.Stats.HasSpecialization("Hache de Guerre") ? ObstacleDamageType.Tranchant : ObstacleDamageType.Contondant);
                    }
                    else if (equippedWeapon != null && equippedWeapon.Category.Contains("Laser"))
                    {
                        directDmgType = ObstacleDamageType.Thermique;
                    }

                    if (CoverSystem.ScanCover(attacker.CurrentCoords, defender.CurrentCoords, _grid, smokeZones: _smokeZones, seesThroughSmoke: SeesThroughSmoke(attacker)).Rays is var rays && rays != null)
                    {
                        for (int rIdx = 0; rIdx < rays.Count; rIdx++)
                        {
                            if (rays[rIdx].Blocked && rays[rIdx].HasBlockingCell)
                            {
                                if (PropObstacleRegistry.ApplyDamage(rays[rIdx].BlockingCell, effectiveWeaponDamage, directDmgType, _grid, _gridVisualizer, out var obsRes))
                                {
                                    Log($"   🛡️ <b>IMPACT COLLATÉRAL</b> : {obsRes.Log}");
                                }
                                break;
                            }
                        }
                    }
                }

                TryDisarmDefender(attacker, defender, targetedPart, result);
                if (ammoFiredHere && equippedWeapon != null && result.AttackRoll.RawRoll > 0)
                    ConsumeShotAndCheckJam(attacker, equippedWeapon, result, hdShot);

                var laserWeapon = attacker.GetComponentInChildren<LaserRifleWeapon>();
                // Un roundkick / une frappe au corps-à-corps (Mains Nues, Maniement,
                // Perçantes) ne doit jamais déclencher le rayon laser + sons de tir, même si un
                // laser est équipé visuellement. Seul un vrai tir (Ballistique) tire au laser.
                // Vérification sur positions fraîches : si l'attaquant s'est éloigné entre le
                // clic et l'impact, on tire au laser ; s'il est revenu au contact avec une
                // compétence de mêlée, on reste en coup de poing.
                bool freshMeleeAtImpact = isMeleeAtTrigger();
                if (attackSkill == SkillType.Ballistique && !freshMeleeAtImpact && laserWeapon != null && defender != null)
                {
                    Vector3 targetCenter = defender.transform.position + Vector3.up * 1.15f;
                    laserWeapon.FireLaser(targetCenter, result.IsHit);
                }

                // Télémétrie d'engagement
                bool isTargetDeadOrDown = !defender.Stats.IsAlive || result.FatalResolution != FatalBlowResolution.None;
                CombatHUD.RecordCombatAction(
                    attacker.Stats.Name,
                    defender.Stats.Name,
                    attacker.IsPlayerControlled,
                    defender.IsPlayerControlled,
                    result.IsHit,
                    result.IsCritical,
                    result.IsBlocked,
                    result.RawDamage,
                    result.ArmorAbsorbed,
                    result.FinalDamageApplied,
                    result.ExceededEncaissement,
                    isTargetDeadOrDown,
                    (cancelPenaltyWithAP ? 3 : 2) + attackerBonusAP
                );

                // SFX combat : mapping complet du résultat (hybride procédural/clips).
                if (KilltimeAudioManager.Instance != null && defender != null)
                {
                    KilltimeAudioManager.Instance.PlayCombatResult(result, defender.transform.position);
                    KilltimeAudioManager.Instance.Play(SoundId.Dice_Roll, 0.35f);
                }

                // Affichage du texte flottant sur l'attaquant à l'impact exact
                if (attVisual != null)
                {
                    int totalCost = (cancelPenaltyWithAP ? 3 : 2) + attackerBonusAP;
                    if (isAttackerStealthed)
                    {
                        attVisual.SpawnFloatingText($"🥷 EMBUSCADE (+2 Attaque)", new Color(0.2f, 0.95f, 0.75f));
                    }
                    else
                    {
                        attVisual.SpawnFloatingText($"Attaque {targetedPart} (-{totalCost} PA)", new Color(0.3f, 0.8f, 1.0f));
                    }
                }

                // Textes flottants et retours visuels sur le défenseur à l'impact exact
                if (defVisual != null)
                {
                    if (result.IsHit)
                    {
                        defVisual.TriggerHitFlash();

                        if (result.IsCritical)
                        {
                            defVisual.SpawnFloatingText("[CRITIQUE] COUP DÉCISIF !", new Color(1.0f, 0.85f, 0.1f));
                        }

                        if (isAttackerStealthed)
                        {
                            defVisual.SpawnFloatingText("SURPRIS ! (0 réaction)", Color.yellow);
                        }

                        if (result.WasDeflected)
                        {
                            defVisual.SpawnFloatingText($"[DÉVIATION] -> {BodyPartInfo.GetInfo(result.ActualHitPart).DisplayName} (Diff 0)", Color.yellow);
                        }
                        else
                        {
                            string diffSign = result.Differential > 0 ? $"+{result.Differential}" : $"{result.Differential}";
                            defVisual.SpawnFloatingText($"[TOUCHÉ] {BodyPartInfo.GetInfo(result.ActualHitPart).DisplayName} (Diff {diffSign})", new Color(0.9f, 0.9f, 1.0f));
                        }

                        string dmgText = (result.ArmorAbsorbed > 0 || result.ShieldAbsorbed > 0) 
                            ? $"-{result.FinalDamageApplied} PV (Bruts {result.RawDamage} | Armure -{result.ArmorAbsorbed}" + (result.ShieldAbsorbed > 0 ? $" | Bouclier -{result.ShieldAbsorbed}" : "") + ")" 
                            : $"-{result.FinalDamageApplied} PV";
                        defVisual.SpawnFloatingText(dmgText, new Color(1.0f, 0.25f, 0.25f));

                        if (result.ExceededEncaissement)
                        {
                            defVisual.SpawnFloatingText($"[CHOC] TRAUMATIQUE ! [{result.InflictedStatus}]", new Color(1.0f, 0.4f, 0.95f));
                        }

                        if (result.CausedKnockback)
                        {
                            TryApplyKnockback(attacker, defender);
                        }

                        if (result.MoraleResult.Triggered)
                        {
                            if (result.MoraleResult.Surrendered)
                            {
                                defVisual.SpawnFloatingText("🏳️ REDDITION !", Color.white);
                                defVisual.DropHeldItemsWithPhysics();
                            }
                            else if (result.MoraleResult.Routed)
                            {
                                defVisual.SpawnFloatingText("😱 DÉROUTE ! [Agonisant]", new Color(0.95f, 0.2f, 0.85f));
                            }
                            else
                            {
                                defVisual.SpawnFloatingText("🧠 Sang-froid préservé", Color.cyan);
                            }
                        }

                        if (result.FatalResolution == FatalBlowResolution.InstantDeath)
                        {
                            defVisual.SpawnFloatingText("[MORTEL] INSTANTANÉ !", Color.black);
                            defVisual.TriggerFallingBackDeath();
                        }
                        else if (result.FatalResolution == FatalBlowResolution.MiracleSaved)
                        {
                            defVisual.SpawnFloatingText("[MIRACLE] DESTIN SAUVÉ ! (1 PV)", new Color(1.0f, 0.85f, 0.1f));
                            defVisual.TriggerFallingBackDeath();
                        }
                        else if (result.FatalResolution == FatalBlowResolution.ForcedUnconscious)
                        {
                            defVisual.SpawnFloatingText("[K.O.] SYNCOPE TRAUMATIQUE !", Color.magenta);
                            defVisual.TriggerFallingBackDeath();
                        }
                        else if (result.FatalResolution == FatalBlowResolution.EligibleForLastBreath)
                        {
                            if (!defender.IsPlayerControlled)
                            {
                                defender.Stats.ChooseSombrer();
                                defVisual.SpawnFloatingText("[SYNCOPE] CHUTE HORS COMBAT (0 PV)", Color.cyan);
                                defVisual.TriggerFallingBackDeath();
                            }
                            else
                            {
                                defender.Stats.ChooseLastBreath();
                                defVisual.SpawnFloatingText("[SURVIE] DERNIER SOUFFLE (0 PV)", new Color(1.0f, 0.5f, 0.1f));
                            }
                        }
                    }
                    else if (result.IsBlocked)
                    {
                        defVisual.SpawnFloatingText($"[PARADE] Neutralisation (Diff {result.Differential})", new Color(0.2f, 0.9f, 1.0f));
                    }
                }

                // Pont WebGL
                WebBridgeManager.Instance?.SendCombatEvent(
                    result.IsHit ? "HIT" : "MISS",
                    result.CombatLog,
                    result.FinalDamageApplied
                );

                RecordChronoSnapshot($"Attaque sur {defender.Stats.Name} ({targetedPart})");

                // Diffusion VTT Multijoueur si GM
                if (Killtime.Multi.VTTTableSync.Instance != null 
                    && !Killtime.Multi.VTTTableSync.Instance.IsApplyingRemoteAction 
                    && Killtime.Multi.VTTRoomManager.Instance != null 
                    && Killtime.Multi.VTTRoomManager.Instance.InRoom 
                    && Killtime.Multi.VTTRoomManager.Instance.IsGM)
                {
                    var actionPayload = new Killtime.Multi.VTTCombatActionPayload
                    {
                        action = "attack",
                        actorId = Killtime.Multi.VTTTableSync.UnitIdOf(attacker),
                        targetId = Killtime.Multi.VTTTableSync.UnitIdOf(defender),
                        targetedPart = (int)targetedPart,
                        actualHitPart = (int)result.ActualHitPart,
                        attackSkill = (int)attackSkill,
                        defenseSkill = (int)defenseSkill,
                        isHit = result.IsHit,
                        isCritical = result.IsCritical,
                        wasDeflected = result.WasDeflected,
                        finalDamageApplied = result.FinalDamageApplied,
                        rawDamage = result.RawDamage,
                        armorAbsorbed = result.ArmorAbsorbed,
                        differential = result.Differential,
                        inflictedStatus = result.InflictedStatus.ToString(),
                        isMeleeStrike = freshMeleeAtImpact,
                        attackerCostAP = (cancelPenaltyWithAP ? 3 : 2) + attackerBonusAP,
                        defenderCostAP = (duel != null && duel.DefenderBasePaid ? duel.BaseDefenseCost : 0) + (duel != null ? duel.DefenderBonusPAApplied : resolvedDefenderBonus),
                        attackerPE = duel != null ? duel.AttackerPEApplied : 0,
                        defenderPE = duel != null ? duel.DefenderPEApplied : 0,
                        attackerNewAP = attacker.Stats.CurrentActionPoints,
                        defenderNewAP = defender.Stats.CurrentActionPoints,
                        defenderNewHealth = defender.Stats.CurrentHealth,
                        defenderNewActiveStatus = (int)defender.Stats.ActiveStatus,
                        defenderIsDead = defender.Stats.IsDead || !defender.Stats.IsAlive,
                        actorRotationY = attacker.transform.rotation.eulerAngles.y,
                        defenderRotationY = defender.transform.rotation.eulerAngles.y,
                        defenderNewQ = defender.CurrentCoords.Q,
                        defenderNewR = defender.CurrentCoords.R,
                        combatLog = result.CombatLog
                    };
                    Killtime.Multi.VTTTableSync.Instance.BroadcastCombatAction(actionPayload);
                    Killtime.Multi.VTTTableSync.Instance.BroadcastFullCombatState();
                }

                if (!defender.Stats.IsAlive || defender.Stats.IsSurrendered)
                {
                    AutoTargetNextAlive();
                }

                // RD-049 : rupture de furtivité répercutée immédiatement sur le brouillard
                if (isAttackerStealthed)
                {
                    Killtime.Tactics.Visibility.FogOfWarManager.Instance?.RefreshFog("stealth-broken");
                }

                // RD-030 : l'attaque en zone surveillée expose l'attaquant aux
                // tirs de guet ennemis (duel aveugle auto, tour adverse).
                if (attacker != null && attacker.Stats != null && attacker.Stats.IsAlive)
                {
                    CheckOverwatchTriggers(attacker, attacker.CurrentCoords);
                }

                // Les PV ont changé : réévalue mood + intensité AVANT le verdict final
                // (si le combat est plié, CheckCombatOver -> HandleCombatEnded impose Victory/Defeat après).
                UpdateAdaptiveMusic();
                _turnManager?.CheckCombatOver();
            };

            _isAttackInProgress = true;
            if (KilltimeAudioManager.Instance != null)
                KilltimeAudioManager.Instance.PlayUI(SoundId.VATS_Fire, 0.85f);

            bool freeLookActive = _cinematicDirector != null && _cinematicDirector.IsFreeLook;
            if (_enableCinematicKillcam && _cinematicDirector != null && !freeLookActive)
            {
                _cinematicDirector.PlayCinematicKillshot(
                    attacker.transform,
                    defender.transform,
                    onStrikePoint: resolveAction,
                    onComplete: () => { _isAttackInProgress = false; },
                    onActionStart: triggerKickAction,
                    onDefenseStart: triggerDefenseAction
                );
            }
            else
            {
                StartCoroutine(ExecuteAttackRoutine(triggerKickAction, triggerDefenseAction, resolveAction));
            }
        }

        private System.Collections.IEnumerator ExecuteAttackRoutine(Action triggerKick, Action triggerDefense, Action resolveImpact)
        {
            triggerKick?.Invoke();
            yield return new WaitForSeconds(0.06f);
            triggerDefense?.Invoke();
            yield return new WaitForSeconds(0.16f);
            resolveImpact?.Invoke();
            yield return new WaitForSeconds(0.65f);
            _isAttackInProgress = false;
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
            _isAttackInProgress = true;

            int effectiveWeaponDamage = weaponBaseDamage;
            var equippedWeapon = attacker.Sheet?.GetEquippedWeapon();
            if (equippedWeapon != null && equippedWeapon.BaseDamage > 0)
            {
                effectiveWeaponDamage = equippedWeapon.BaseDamage;
            }
            int weaponBonusEc2 = equippedWeapon != null ? Math.Max(0, equippedWeapon.AttackBonusEc) : 0;
            // RD-033 : charge Haute Densité chambrée = +1 dégât (sniper/Deglazer).
            bool hdShot2 = WeaponAmmo.UsesAmmo(equippedWeapon, attackSkill) && equippedWeapon.LoadedHD;
            if (hdShot2) effectiveWeaponDamage += 1;

            _combatCalculator.SetContactDistanceState(attacker.IsCanonEntrave());

            // Duel aveugle : Begin (base attaque) puis DÉCLARATION attaquant immédiate
            // (mise cachée, aucun jet lancé). La fenêtre défenseur s'ouvre ensuite en
            // aveugle : le défenseur ne voit ni jet ni mise adverse.
            // Livre VI §25.3 : couvert évalué sur positions réelles (Full => refus).
            // RD-036 : ruling hauteur recalculé sur positions réelles (même gate
            // que ExecuteAttack — la routine n'est atteinte qu'après le gate, le
            // mod suffit ici).
            CoverType defenseCover = GetCoverToTarget(attacker, defender);
            bool isMeleeHere = SkillDefinitions.IsMeleeAttackSkill(attackSkill)
                || (equippedWeapon != null && equippedWeapon.RangeInTiles <= 1);
            ElevationRuling defenseRuling = GetElevationRuling(attacker, defender, attackSkill, equippedWeapon, isMeleeHere);
            var duel = _combatCalculator.BeginSkillDuel(
                attacker.Stats, defender.Stats, targetedPart,
                attackSkill, defenseSkill, effectiveWeaponDamage, cancelPenaltyWithAP,
                false, attackerSpecialization, defenderSpecialization,
                defender.Stats.BaseArmorAbsorption, out string duelError, defenseCover, weaponBonusEc2,
                false, defenseRuling.AttackMod, defenseRuling.Label);

            if (duel == null)
            {
                Log(duelError ?? "⚠️ Attaque impossible : PA insuffisants pour le coût de base.");
                _isAttackInProgress = false;
                yield break;
            }

            _combatCalculator.DeclareAttackerStakes(duel, attackerBonusAP, attackerPE);

            bool playerDecided = false;
            SkillType finalDefSkill = defenseSkill;
            bool finalWantsDefend = true;
            int finalBonusAP = 0;
            int finalPE = 0;

            // La défense se paramètre dans le menu contextuel radial, centré sur le
            // défenseur comme après un clic droit sur lui (aucun modal séparé).
            var menu = CombatUI.CombatContextMenuUI.EnsureInstance();
            menu.OpenDefenseMenu(duel, attacker, defender, (chosenSkill, wantsDef, bonusAP, pe) =>
            {
                if (menu.InterceptedBy != null)
                {
                    defender = menu.InterceptedBy;
                }
                finalDefSkill = chosenSkill;
                finalWantsDefend = wantsDef;
                finalBonusAP = bonusAP;
                finalPE = pe;
                playerDecided = true;
            });

            while (!playerDecided)
            {
                // Garde-fou : si le menu de défense a disparu sans décision (changement
                // de scène, unité détruite...), on résout en encaissement passif plutôt
                // que de bloquer toutes les attaques suivantes pour toujours.
                if (!CombatUI.CombatContextMenuUI.IsDefenseOpen)
                {
                    finalDefSkill = defenseSkill;
                    finalWantsDefend = false;
                    finalBonusAP = 0;
                    finalPE = 0;
                    playerDecided = true;
                    Log($"🛡️ <b>{defender.Stats.Name}</b> ne répond pas : encaissement passif (0 PA).");
                    break;
                }
                yield return null;
            }

            if (menu.InterceptedBy != null)
            {
                defender = menu.InterceptedBy;
            }

            // Déclaration aveugle du défenseur (mise débitée, rien révélé).
            _combatCalculator.DeclareDefenderStakes(duel, finalDefSkill, finalWantsDefend, finalBonusAP, finalPE, defenderSpecialization);

            // RÉVÉLATION SIMULTANÉE : les deux dés sont lancés ensemble.
            DamageResult result = _combatCalculator.ResolveBlindDuel(duel);

            triggerKick?.Invoke();
            yield return new WaitForSeconds(0.06f);
            if (finalWantsDefend)
            {
                var curDefVisual = defender != null ? defender.GetComponent<TacticalUnitVisual>() : null;
                if (defender != null && defender.Stats != null && defender.Stats.CanDefendActively())
                {
                    curDefVisual?.TriggerBodyBlock();
                    if (KilltimeAudioManager.Instance != null)
                        KilltimeAudioManager.Instance.PlayAt(SoundId.Defense_Block, defender.transform.position, 0.5f);
                }
            }
            yield return new WaitForSeconds(0.16f);

            attacker.Stats.RegisterAttack();
            Log(result.CombatLog);
            HandleChannelInterruptionFx(defender, result.ChannelInterrupted);

            // Dégâts collatéraux sur l'obstacle si l'attaque est absorbée par le couvert ou neutralisée
            if (result.IsBlocked && defenseCover != CoverType.None && _grid != null)
            {
                ObstacleDamageType directDmgType = ObstacleDamageType.Perforant;
                if (isMeleeHere)
                {
                    directDmgType = (attackSkill == SkillType.ManiementArmes && attacker.Stats.HasSpecialization("Marteau de Guerre"))
                        ? ObstacleDamageType.Contondant
                        : (attacker.Stats.HasSpecialization("Hache de Guerre") ? ObstacleDamageType.Tranchant : ObstacleDamageType.Contondant);
                }
                else if (equippedWeapon != null && equippedWeapon.Category.Contains("Laser"))
                {
                    directDmgType = ObstacleDamageType.Thermique;
                }

                if (CoverSystem.ScanCover(attacker.CurrentCoords, defender.CurrentCoords, _grid, smokeZones: _smokeZones, seesThroughSmoke: SeesThroughSmoke(attacker)).Rays is var rays && rays != null)
                {
                    for (int rIdx = 0; rIdx < rays.Count; rIdx++)
                    {
                        if (rays[rIdx].Blocked && rays[rIdx].HasBlockingCell)
                        {
                            if (PropObstacleRegistry.ApplyDamage(rays[rIdx].BlockingCell, effectiveWeaponDamage, directDmgType, _grid, _gridVisualizer, out var obsRes))
                            {
                                Log($"   🛡️ <b>IMPACT COLLATÉRAL</b> : {obsRes.Log}");
                            }
                            break;
                        }
                    }
                }
            }

            TryDisarmDefender(attacker, defender, targetedPart, result);
            if (WeaponAmmo.UsesAmmo(equippedWeapon, attackSkill) && equippedWeapon != null && result.AttackRoll.RawRoll > 0)
                ConsumeShotAndCheckJam(attacker, equippedWeapon, result, hdShot2);

            var laserWeapon = attacker.GetComponentInChildren<LaserRifleWeapon>();
            bool skillIsMelee = SkillDefinitions.IsMeleeAttackSkill(attackSkill);
            int freshDistAtImpact = (attacker != null && defender != null) ? attacker.CurrentCoords.DistanceTo(defender.CurrentCoords) : 1;
            bool isMeleeStrike = skillIsMelee && freshDistAtImpact <= 1;
            if (attackSkill == SkillType.Ballistique && !isMeleeStrike && laserWeapon != null && defender != null)
            {
                Vector3 targetCenter = defender.transform.position + Vector3.up * 1.15f;
                laserWeapon.FireLaser(targetCenter, result.IsHit);
            }

            bool isTargetDeadOrDown = !defender.Stats.IsAlive || result.FatalResolution != FatalBlowResolution.None;
            CombatHUD.RecordCombatAction(
                attacker.Stats.Name,
                defender.Stats.Name,
                attacker.IsPlayerControlled,
                defender.IsPlayerControlled,
                result.IsHit,
                result.IsCritical,
                result.IsBlocked,
                result.RawDamage,
                result.ArmorAbsorbed,
                result.FinalDamageApplied,
                result.ExceededEncaissement,
                isTargetDeadOrDown,
                (cancelPenaltyWithAP ? 3 : 2) + attackerBonusAP
            );

            if (KilltimeAudioManager.Instance != null && defender != null)
            {
                KilltimeAudioManager.Instance.PlayCombatResult(result, defender.transform.position);
                KilltimeAudioManager.Instance.Play(SoundId.Dice_Roll, 0.35f);
            }

            var attVisual = attacker.GetComponent<TacticalUnitVisual>();
            var defVisual = defender.GetComponent<TacticalUnitVisual>();

            if (attVisual != null)
            {
                int totalCost = (cancelPenaltyWithAP ? 3 : 2) + attackerBonusAP;
                attVisual.SpawnFloatingText($"Attaque {targetedPart} (-{totalCost} PA)", new Color(0.3f, 0.8f, 1.0f));
            }

            if (defVisual != null)
            {
                if (result.IsHit)
                {
                    defVisual.TriggerHitFlash();

                    if (result.IsCritical)
                    {
                        defVisual.SpawnFloatingText("[CRITIQUE] COUP DÉCISIF !", new Color(1.0f, 0.85f, 0.1f));
                    }

                    if (result.WasDeflected)
                    {
                        defVisual.SpawnFloatingText($"[DÉVIATION] -> {BodyPartInfo.GetInfo(result.ActualHitPart).DisplayName} (Diff 0)", Color.yellow);
                    }
                    else
                    {
                        string diffSign = result.Differential > 0 ? $"+{result.Differential}" : $"{result.Differential}";
                        defVisual.SpawnFloatingText($"[TOUCHÉ] {BodyPartInfo.GetInfo(result.ActualHitPart).DisplayName} (Diff {diffSign})", new Color(0.9f, 0.9f, 1.0f));
                    }

                    string dmgText = (result.ArmorAbsorbed > 0 || result.ShieldAbsorbed > 0) 
                        ? $"-{result.FinalDamageApplied} PV (Bruts {result.RawDamage} | Armure -{result.ArmorAbsorbed}" + (result.ShieldAbsorbed > 0 ? $" | Bouclier -{result.ShieldAbsorbed}" : "") + ")" 
                        : $"-{result.FinalDamageApplied} PV";
                    defVisual.SpawnFloatingText(dmgText, new Color(1.0f, 0.25f, 0.25f));

                    if (result.ExceededEncaissement)
                    {
                        defVisual.SpawnFloatingText($"[CHOC] TRAUMATIQUE ! [{result.InflictedStatus}]", new Color(1.0f, 0.4f, 0.95f));
                    }

                    if (result.CausedKnockback)
                    {
                        TryApplyKnockback(attacker, defender);
                    }

                    if (result.MoraleResult.Triggered)
                    {
                        if (result.MoraleResult.Surrendered)
                        {
                            defVisual.SpawnFloatingText("🏳️ REDDITION !", Color.white);
                            defVisual.DropHeldItemsWithPhysics();
                        }
                        else if (result.MoraleResult.Routed)
                        {
                            defVisual.SpawnFloatingText("😱 DÉROUTE ! [Agonisant]", new Color(0.95f, 0.2f, 0.85f));
                        }
                        else
                        {
                            defVisual.SpawnFloatingText("🧠 Sang-froid préservé", Color.cyan);
                        }
                    }

                    if (result.FatalResolution == FatalBlowResolution.InstantDeath)
                    {
                        defVisual.SpawnFloatingText("[MORTEL] INSTANTANÉ !", Color.black);
                        defVisual.TriggerFallingBackDeath();
                    }
                    else if (result.FatalResolution == FatalBlowResolution.MiracleSaved)
                    {
                        defVisual.SpawnFloatingText("[MIRACLE] DESTIN SAUVÉ ! (1 PV)", new Color(1.0f, 0.85f, 0.1f));
                        defVisual.TriggerFallingBackDeath();
                    }
                    else if (result.FatalResolution == FatalBlowResolution.ForcedUnconscious)
                    {
                        defVisual.SpawnFloatingText("[K.O.] SYNCOPE TRAUMATIQUE !", Color.magenta);
                        defVisual.TriggerFallingBackDeath();
                    }
                    else if (result.FatalResolution == FatalBlowResolution.EligibleForLastBreath)
                    {
                        if (!defender.IsPlayerControlled)
                        {
                            defender.Stats.ChooseSombrer();
                            defVisual.SpawnFloatingText("[SYNCOPE] CHUTE HORS COMBAT (0 PV)", Color.cyan);
                            defVisual.TriggerFallingBackDeath();
                        }
                        else
                        {
                            defender.Stats.ChooseLastBreath();
                            defVisual.SpawnFloatingText("[SURVIE] DERNIER SOUFFLE (0 PV)", new Color(1.0f, 0.5f, 0.1f));
                        }
                    }
                }
                else if (result.IsBlocked)
                {
                    defVisual.SpawnFloatingText($"[PARADE] Neutralisation (Diff {result.Differential})", new Color(0.2f, 0.9f, 1.0f));
                }
            }

            WebBridgeManager.Instance?.SendCombatEvent(
                result.IsHit ? "HIT" : "MISS",
                result.CombatLog,
                result.FinalDamageApplied
            );

            RecordChronoSnapshot($"Attaque sur {defender.Stats.Name} ({targetedPart})");

            if (!defender.Stats.IsAlive)
            {
                AutoTargetNextAlive();
            }

            UpdateAdaptiveMusic();
            _turnManager?.CheckCombatOver();

            yield return new WaitForSeconds(0.55f);
            _isAttackInProgress = false;
        }

        // =====================================================================
        // GRENADES : viser (case) -> lancer (main / lance-grenade) -> dispersion ->
        // impact -> explosion + shrapnels + statuts (Livre VIII §31.3, Livre VI §24).
        // =====================================================================

        /// <summary>Inventaire lançable de l'unité (grenades main + compatibles lanceur).</summary>
        public static List<InventoryItem> GetThrowableGrenades(CharacterSheet sheet)
        {
            var list = new List<InventoryItem>();
            if (sheet?.Inventory == null) return list;
            for (int i = 0; i < sheet.Inventory.Count; i++)
            {
                var it = sheet.Inventory[i];
                if (it != null && it.IsThrowableGrenade()) list.Add(it);
            }
            return list;
        }

        public static InventoryItem GetEquippedLauncher(CharacterSheet sheet)
        {
            if (sheet?.Inventory == null) return null;
            for (int i = 0; i < sheet.Inventory.Count; i++)
            {
                var it = sheet.Inventory[i];
                if (it != null && it.IsLauncher && it.IsEquipped) return it;
            }
            return null;
        }

        public static InventoryItem GetAnyLauncher(CharacterSheet sheet)
        {
            if (sheet?.Inventory == null) return null;
            var eq = GetEquippedLauncher(sheet);
            if (eq != null) return eq;
            for (int i = 0; i < sheet.Inventory.Count; i++)
            {
                var it = sheet.Inventory[i];
                if (it != null && it.IsLauncher) return it;
            }
            return null;
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
            if (_isAttackInProgress)
            {
                Log("⚠️ Lancer ignoré : une résolution est déjà en cours (anti double-clic).");
                return;
            }

            var attacker = _turnManager != null ? _turnManager.ActiveUnit : PlayerUnit;

            if (TurnManager.IsMultiplayerPlayerClient())
            {
                if (attacker != null)
                {
                    Killtime.Multi.VTTTableSync.Instance?.RequestGrenade(
                        attacker,
                        targetCoords,
                        grenadeItemId,
                        useLauncher,
                        aimed,
                        attackerBonusAP
                    );
                }
                return;
            }
            if (attacker == null || attacker.Stats == null)
            {
                Log("⚠️ Impossible de lancer : aucune unité active.");
                return;
            }
            if (!attacker.Stats.IsAlive)
            {
                Log($"⚠️ {attacker.Stats.Name} est hors de combat !");
                return;
            }

            var sheet = attacker.GetOrBuildSheet();
            InventoryItem grenade = null;
            if (!string.IsNullOrEmpty(grenadeItemId) && sheet.Inventory != null)
                grenade = sheet.Inventory.Find(i => i != null && i.ItemId == grenadeItemId && i.IsThrowableGrenade());
            if (grenade == null)
            {
                var throwables = GetThrowableGrenades(sheet);
                grenade = throwables.Count > 0 ? throwables[0] : null;
            }
            if (grenade == null)
            {
                Log($"⚠️ {attacker.Stats.Name} n'a aucune grenade en inventaire ! Achetez-en au marché (Grenades).");
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayUI(SoundId.UI_Denied, 0.6f);
                return;
            }

            InventoryItem launcher = null;
            if (useLauncher)
            {
                launcher = GetAnyLauncher(sheet);
                if (launcher == null)
                {
                    Log($"⚠️ Aucun lance-grenades en inventaire : lancer à la main ({grenade.Name}).");
                    useLauncher = false;
                }
                else if (!grenade.LauncherCompatible)
                {
                    Log($"⚠️ {grenade.Name} incompatible avec un lanceur (poudre noire / inerte) : lancer à la main.");
                    launcher = null;
                    useLauncher = false;
                }
            }

            int maxRange = GrenadeRules.ComputeMaxRange(grenade, launcher);
            int dist = attacker.CurrentCoords.DistanceTo(targetCoords);
            if (dist > maxRange)
            {
                Log($"⚠️ Hors de portée : {dist} cases > {maxRange} ({(launcher != null ? launcher.Name : "main")}). Rapprochez-vous ou équipez un lanceur.");
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayUI(SoundId.Move_Denied, 0.6f);
                return;
            }

            int baseCost = GrenadeRules.ComputeAPCost(launcher, aimed);
            // Milkor MGL : cadence de zone, -1 PA par tir (ApCostModifier du lanceur utilisé).
            if (launcher != null && launcher.ApCostModifier != 0)
                baseCost = Math.Max(1, baseCost + launcher.ApCostModifier);
            int bonusWant = Mathf.Max(0, attackerBonusAP);
            DiceType atkDie = attacker.Stats.GetSkillDie(SkillType.Ballistique, true);
            if (!attacker.Stats.CanAttack(atkDie))
            {
                int maxAtt = attacker.Stats.GetMaxAttacksAllowed(atkDie);
                Log($"⚠️ <b>{attacker.Stats.Name}</b> a épuisé son quota d'attaque ({attacker.Stats.AttacksThisTurn}/{maxAtt}) !");
                return;
            }
            if (!InfiniteAP && attacker.Stats.CurrentActionPoints < baseCost)
            {
                Log($"⚠️ PA insuffisants : lancer {(launcher != null ? "au lanceur" : "à la main")}{(aimed ? " visé" : "")} = {baseCost} PA (reste {attacker.Stats.CurrentActionPoints}).");
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayUI(SoundId.UI_Denied, 0.6f);
                return;
            }

            // Débit PA de base (malus armures inclus) puis bonus d'injection
            // (mises exemptées du malus, comme les passes d'armes).
            if (!InfiniteAP)
            {
                attacker.Stats.ConsumeActionPoints(baseCost);
                int applied = 0;
                for (int i = 0; i < bonusWant; i++)
                {
                    if (attacker.Stats.ConsumeActionPoints(1, true)) applied++;
                    else break;
                }
                bonusWant = applied;
            }
            attacker.Stats.RegisterAttack();

            // Consomme 1 grenade du stock (décrémente la pile ou retire l'item).
            var grenadeDef = grenade.Clone();
            if (grenade.IsStackable && grenade.Quantity > 1)
            {
                grenade.Quantity--;
            }
            else
            {
                sheet.RemoveItem(grenade.ItemId);
            }
            attacker.NotifyInventoryChanged(true);

            // Orientation + animation de lancer.
            var targetNode = _grid != null ? _grid.GetNode(targetCoords) : null;
            Vector3 targetWorld = targetNode != null ? targetNode.WorldPosition
                : targetCoords.ToWorldPosition(_grid != null ? _grid.HexRadius : 1f, 0f);
            Vector3 dir = targetWorld - attacker.transform.position;
            dir.y = 0f;
            if (dir != Vector3.zero) attacker.transform.rotation = Quaternion.LookRotation(dir);
            var attVisual = attacker.GetComponent<TacticalUnitVisual>();
            attVisual?.TriggerGrenadeThrow();

            if (KilltimeAudioManager.Instance != null)
            {
                KilltimeAudioManager.Instance.PlayGrenadeThrow(attacker.transform.position, useLauncher);
            }

            // Brouillard de guerre : l'explosion révèle le lanceur (tirs bruyants).
            try { Killtime.Tactics.Visibility.FogOfWarManager.Instance?.NotifyLoudShot(attacker); }
            catch { /* fog optionnel */ }

            // Jet de précision immédiat (pour connaître le point de chute avant l'arc visuel).
            var calc = new GrenadeCalculator(_diceRoller);
            var throwOutcome = calc.ResolveThrow(attacker.Stats, dist, grenadeDef, launcher, bonusWant, aimed);
            var blastCoords = ResolveScatterCoords(targetCoords, throwOutcome);

            if (KilltimeAudioManager.Instance != null)
                KilltimeAudioManager.Instance.PlayUI(SoundId.VATS_Fire, 0.85f);
            attVisual?.SpawnFloatingText(
                $"💣 {grenadeDef.Name} (-{baseCost + bonusWant} PA)", new Color(1f, 0.72f, 0.15f));

            _isAttackInProgress = true;
            StartCoroutine(GrenadeThrowRoutine(attacker, grenadeDef, launcher, targetCoords, blastCoords, throwOutcome, dist, maxRange, baseCost + bonusWant));
        }

        private HexCoordinates ResolveScatterCoords(HexCoordinates target, GrenadeThrowOutcome outcome)
        {
            if (outcome.IsOnTarget || outcome.ScatterDistance <= 0 || _grid == null) return target;
            var cur = target;
            for (int i = 0; i < outcome.ScatterDistance; i++)
            {
                var next = cur.GetNeighbor(Mathf.Max(0, outcome.ScatterDirIndex));
                if (_grid.GetNode(next) == null) break;
                cur = next;
            }
            return cur;
        }

        private IEnumerator GrenadeThrowRoutine(
            TacticalUnit attacker, InventoryItem grenadeDef, InventoryItem launcher,
            HexCoordinates aimedCoords, HexCoordinates blastCoords,
            GrenadeThrowOutcome throwOutcome, int dist, int maxRange, int totalPa)
        {
            Vector3 start = attacker.transform.position + Vector3.up * 1.4f;
            var blastNode = _grid != null ? _grid.GetNode(blastCoords) : null;
            Vector3 blastPos = blastNode != null ? blastNode.WorldPosition
                : blastCoords.ToWorldPosition(_grid != null ? _grid.HexRadius : 1f, 0f);
            blastPos.y = (blastNode != null ? blastNode.WorldPosition.y : 0f) + 0.05f;

            bool impacted = false;
            bool withLauncher = launcher != null && launcher.IsLauncher;
            GrenadeProjectile.Launch(start, blastPos, grenadeDef, withLauncher, () => { impacted = true; });

            float timeout = 4f;
            float waited = 0f;
            while (!impacted && waited < timeout)
            {
                waited += Time.deltaTime;
                yield return null;
            }

            GrenadeExplosionFX.Detonate(blastPos, grenadeDef, Mathf.Max(0, grenadeDef.BlastRadius), _grid != null ? _grid.HexRadius : 1f);

            if (KilltimeAudioManager.Instance != null)
            {
                bool heavy = grenadeDef.DamageDiceCount >= 3 || grenadeDef.BaseDamage >= 12;
                KilltimeAudioManager.Instance.PlayGrenadeDetonation(blastPos, grenadeDef.GrenadeKind, heavy);
            }

            ResolveGrenadeBlast(attacker, grenadeDef, launcher, aimedCoords, blastCoords, throwOutcome, dist, maxRange, totalPa, blastPos);

            yield return new WaitForSeconds(0.55f);
            _isAttackInProgress = false;
        }

        private void ResolveGrenadeBlast(
            TacticalUnit attacker, InventoryItem grenadeDef, InventoryItem launcher,
            HexCoordinates aimedCoords, HexCoordinates blastCoords,
            GrenadeThrowOutcome throwOutcome, int dist, int maxRange, int totalPa, Vector3 blastPos)
        {
            var calc = new GrenadeCalculator(_diceRoller);
            int diceTotal = calc.RollBlastDice(Mathf.Max(0, grenadeDef.DamageDiceCount), out _, out string diceDetail);
            int blastR = Mathf.Max(0, grenadeDef.BlastRadius);
            bool isMortar = launcher != null && (launcher.Name ?? "").Contains("Mortier");

            var allUnits = FindObjectsByType<TacticalUnit>();
            var hits = new List<GrenadeHitResult>();
            int totalDealt = 0;

            for (int i = 0; i < allUnits.Length; i++)
            {
                var u = allUnits[i];
                if (u == null || u.Stats == null || !u.Stats.IsAlive) continue;
                int d = u.CurrentCoords.DistanceTo(blastCoords);
                if (d > blastR) continue;
                int cover = 0;
                var node = _grid != null ? _grid.GetNode(u.CurrentCoords) : null;
                if (node != null)
                {
                    cover = GrenadeRules.ToCoverLevel(node.Cover);
                    // Le mortier lobé passe par-dessus les couverts partiels (Half, 3/4), jamais Full.
                    if (isMortar && (node.Cover == CoverType.Half || node.Cover == CoverType.ThreeQuarters))
                        cover = 0;
                }
                var hit = calc.ResolveHitOnTarget(u.Stats, grenadeDef, diceTotal, d, cover, d == 0);
                hits.Add(hit);
                totalDealt += hit.FinalDamage;
            }

            // Dégradation structurelle de l'environnement par le souffle de la grenade
            var damagedObstacles = new List<ObstacleDamageResult>();
            if (_grid != null && blastR >= 0)
            {
                ObstacleDamageType envDmgType = ObstacleDamageType.Explosif;
                if (grenadeDef != null && !string.IsNullOrEmpty(grenadeDef.GrenadeKind))
                {
                    string k = grenadeDef.GrenadeKind.ToLowerInvariant();
                    if (k.Contains("incend") || k.Contains("thermite") || k.Contains("feu")) envDmgType = ObstacleDamageType.Thermique;
                    else if (k.Contains("plasma")) envDmgType = ObstacleDamageType.Plasma;
                    else if (k.Contains("sonique") || k.Contains("flash")) envDmgType = ObstacleDamageType.Sonique;
                }

                for (int q = -blastR; q <= blastR; q++)
                {
                    int r1 = Mathf.Max(-blastR, -q - blastR);
                    int r2 = Mathf.Min(blastR, -q + blastR);
                    for (int r = r1; r <= r2; r++)
                    {
                        var targetCell = new HexCoordinates(blastCoords.Q + q, blastCoords.R + r);
                        int d = blastCoords.DistanceTo(targetCell);
                        int cellRaw = calc.ComputeRawDamage(grenadeDef, diceTotal, d);
                        if (cellRaw > 0 && PropObstacleRegistry.ApplyDamage(targetCell, cellRaw, envDmgType, _grid, _gridVisualizer, out var obsRes))
                        {
                            damagedObstacles.Add(obsRes);
                        }
                    }
                }
            }

            // --- Zone persistante (RD-041/042/043) : fumée, gaz, feu... ---
            string zoneNote = "";
            if (grenadeDef != null && grenadeDef.ZoneDurationTurns > 0 && blastR > 0)
            {
                _smokeZones.Add(new SmokeZone
                {
                    Center = blastCoords,
                    Radius = blastR,
                    Kind = grenadeDef.GrenadeKind ?? "",
                    Statuses = GrenadeCalculator.ParseStatuses(grenadeDef.GrenadeStatuses ?? ""),
                    TurnsLeft = Math.Max(1, grenadeDef.ZoneDurationTurns),
                    Arcanotech = SmokeScreen.IsArcanotechEra(grenadeDef.Era)
                });
                _gridVisualizer?.UpdatePersistentZones(_smokeZones);
                zoneNote = $" 🌫️ <b>Zone {grenadeDef.GrenadeKind}</b> R{blastR} persistante {grenadeDef.ZoneDurationTurns} round(s) en ({blastCoords.Q},{blastCoords.R})"
                    + (SmokeScreen.BlocksSight(grenadeDef.GrenadeKind) ? " — <b>bloque la visée</b> (sauf thermique)" : "")
                    + (SmokeScreen.ZoneDamagePerTurn(grenadeDef.GrenadeKind) > 0 ? " — dégâts/round" : "");
            }

            // --- Log tactique ---
            string via = (launcher != null && launcher.IsLauncher) ? $" au <b>{launcher.Name}</b>" : " à la main";
            string aimTag = throwOutcome.DistancePenalty != 0 ? $" (malus dist {throwOutcome.DistancePenalty})" : "";
            string scatterTag = throwOutcome.IsOnTarget
                ? $"pile sur <b>({blastCoords.Q},{blastCoords.R})</b>"
                : $"dispersée en <b>({blastCoords.Q},{blastCoords.R})</b> (visé ({aimedCoords.Q},{aimedCoords.R}), déviation {throwOutcome.ScatterDistance})";
            string log = $"💣 <b>GRENADE</b> : {attacker.Stats.Name} lance <b>{grenadeDef.Name}</b> [{grenadeDef.Era} / {grenadeDef.GrenadeKind}]{via} à {dist} cases (max {maxRange}, -{totalPa} PA){aimTag} ➔ {scatterTag}\n";
            log += $"   🎲 Lancer : {throwOutcome.LogFragment}\n";
            log += $"   💥 Souffle R{blastR} : [{grenadeDef.BaseDamage} + {diceDetail} ({grenadeDef.DamageDiceCount}d10) = {grenadeDef.BaseDamage + diceTotal}] x falloff (-25%/case, min 25%) + shrapnels +{grenadeDef.ShrapnelDamage} ➔ {hits.Count} cible(s) dans la zone.";
            if (hits.Count == 0) log += " <i>Souffle dans le vide.</i>";
            if (damagedObstacles.Count > 0)
            {
                for (int oIdx = 0; oIdx < damagedObstacles.Count; oIdx++)
                {
                    log += $"\n   • {damagedObstacles[oIdx].Log}";
                }
            }
            if (!string.IsNullOrEmpty(zoneNote)) log += "\n  " + zoneNote;

            // --- Retours visuels/sonores par cible ---
            for (int i = 0; i < hits.Count; i++)
            {
                var h = hits[i];
                var unit = FindUnitByName(h.TargetName);
                string allyTag = "";
                if (unit != null && unit != attacker && unit.IsPlayerControlled == attacker.IsPlayerControlled)
                    allyTag = " ⚠️<b>TIR ALLIÉ</b>";
                string hpTag = unit != null ? unit.Stats.CurrentHealth + "/" + unit.Stats.MaxHealth + " PV" : "?";
                log += $"\n   • <b>{h.TargetName}</b>{allyTag} à {h.DistanceFromBlast} case(s) : bruts {h.RawDamage} − couv {h.CoverReduction} − armure {h.ArmorAbsorbed}{(h.ShieldAbsorbed > 0 ? $" − bouclier {h.ShieldAbsorbed}" : "")}{(h.Masked ? " 😷<i>masque</i>" : "")} ➔ <color=#FF3B5C><b>{h.FinalDamage} PV</b></color> ({hpTag})";
                if (h.InflictedStatus != StatusEffect.None)
                    log += $" | [{GrenadeCalculator.StatusesToLabel(h.InflictedStatus)}]";
                if (h.ExceededEncaissement) log += " | ⚡<b>CHOC</b>";
                if (h.FatalResolution == FatalBlowResolution.InstantDeath) log += " | 💀<b>MORT</b>";
                else if (h.FatalResolution == FatalBlowResolution.MiracleSaved) log += " | 🔮<b>MIRACLE</b>";
                else if (h.FatalResolution == FatalBlowResolution.ForcedUnconscious) log += " | 💥<b>K.O.</b>";
                else if (h.FatalResolution == FatalBlowResolution.EligibleForLastBreath) log += " | ⚡<b>0 PV</b>";

                if (unit == null) continue;
                var vis = unit.GetComponent<TacticalUnitVisual>();
                bool isGrenadeFatal = (unit != null && !unit.Stats.IsAlive) || h.FatalResolution != FatalBlowResolution.None;
                CombatHUD.RecordCombatAction(
                    attacker.Stats.Name,
                    h.TargetName,
                    attacker.IsPlayerControlled,
                    unit != null && unit.IsPlayerControlled,
                    h.FinalDamage > 0,
                    false,
                    false,
                    h.RawDamage,
                    h.CoverReduction + h.ArmorAbsorbed + h.ShieldAbsorbed,
                    h.FinalDamage,
                    h.ExceededEncaissement,
                    isGrenadeFatal,
                    0
                );

                if (h.FinalDamage > 0)
                {
                    vis?.TriggerHitFlash();
                    vis?.SpawnFloatingText($"-{h.FinalDamage} PV (💣)", new Color(1f, 0.30f, 0.20f));

                    if (unit.Stats.IsAlive && unit.Stats.CurrentHealth > 0)
                    {
                        var mRes = unit.Stats.CheckMoraleAtThreshold(_diceRoller, CoreRulesConfig.Instance.StandardTargetDC);
                        if (mRes.Triggered)
                        {
                            log += $"\n   • {mRes.Log}";
                            if (mRes.Surrendered)
                            {
                                vis?.SpawnFloatingText("🏳️ REDDITION !", Color.white);
                                vis?.DropHeldItemsWithPhysics();
                                vis?.SetCombatStance(false);
                            }
                            else if (mRes.Routed)
                            {
                                vis?.SpawnFloatingText("😱 DÉROUTE ! [Agonisant]", new Color(0.95f, 0.2f, 0.85f));
                            }
                        }
                    }
                    if (h.InflictedStatus != StatusEffect.None)
                        vis?.SpawnFloatingText($"[{GrenadeCalculator.StatusesToLabel(h.InflictedStatus)}]", new Color(1f, 0.4f, 0.95f));
                    if (KilltimeAudioManager.Instance != null)
                    {
                        KilltimeAudioManager.Instance.PlayAt(SoundId.Hurt_Heavy, unit.transform.position, 0.8f);
                        if (h.ExceededEncaissement)
                            KilltimeAudioManager.Instance.PlayAt(SoundId.Trauma_Shock, unit.transform.position, 0.8f);
                    }
                    // Refoulement radial pour les gros calibres au contact du souffle.
                    bool heavy = grenadeDef.DamageDiceCount >= 3 || grenadeDef.BaseDamage >= 12;
                    if (heavy && h.DistanceFromBlast <= 1)
                        TryApplyBlastKnockback(unit, blastCoords);
                }
                else
                {
                    vis?.SpawnFloatingText(h.CoverReduction > 0 ? $"[COUVERT] Souffle absorbé (-{h.CoverReduction})" : "[SOUFFLE] 0 PV", new Color(0.4f, 0.85f, 1f));
                }

                if (h.FatalResolution == FatalBlowResolution.InstantDeath)
                {
                    vis?.SpawnFloatingText("[MORTEL] INSTANTANÉ !", Color.black);
                    vis?.TriggerFallingBackDeath();
                    if (KilltimeAudioManager.Instance != null)
                        KilltimeAudioManager.Instance.PlayAt(SoundId.Death_Instant, unit.transform.position, 0.9f);
                }
                else if (h.FatalResolution == FatalBlowResolution.MiracleSaved)
                {
                    vis?.SpawnFloatingText("[MIRACLE] DESTIN SAUVÉ ! (1 PV)", new Color(1f, 0.85f, 0.1f));
                    vis?.TriggerFallingBackDeath();
                    if (KilltimeAudioManager.Instance != null)
                        KilltimeAudioManager.Instance.PlayAt(SoundId.Miracle_Saved, unit.transform.position, 0.9f);
                }
                else if (h.FatalResolution == FatalBlowResolution.ForcedUnconscious)
                {
                    vis?.SpawnFloatingText("[K.O.] SYNCOPE TRAUMATIQUE !", Color.magenta);
                    vis?.TriggerFallingBackDeath();
                    if (KilltimeAudioManager.Instance != null)
                        KilltimeAudioManager.Instance.PlayAt(SoundId.KO_Fall, unit.transform.position, 0.9f);
                }
                else if (h.FatalResolution == FatalBlowResolution.EligibleForLastBreath)
                {
                    if (!unit.IsPlayerControlled)
                    {
                        unit.Stats.ChooseSombrer();
                        vis?.SpawnFloatingText("[SYNCOPE] CHUTE HORS COMBAT (0 PV)", Color.cyan);
                        vis?.TriggerFallingBackDeath();
                    }
                    else
                    {
                        unit.Stats.ChooseLastBreath();
                        vis?.SpawnFloatingText("[SURVIE] DERNIER SOUFFLE (0 PV)", new Color(1f, 0.5f, 0.1f));
                    }
                    if (KilltimeAudioManager.Instance != null)
                        KilltimeAudioManager.Instance.PlayAt(SoundId.LastBreath, unit.transform.position, 0.9f);
                }
            }

            Log(log);

            WebBridgeManager.Instance?.SendCombatEvent("GRENADE", log, totalDealt);
            RecordChronoSnapshot($"Grenade {grenadeDef.Name} en ({blastCoords.Q},{blastCoords.R})");

            // Diffusion VTT Multijoueur si GM
            if (Killtime.Multi.VTTTableSync.Instance != null 
                && !Killtime.Multi.VTTTableSync.Instance.IsApplyingRemoteAction 
                && Killtime.Multi.VTTRoomManager.Instance != null 
                && Killtime.Multi.VTTRoomManager.Instance.InRoom 
                && Killtime.Multi.VTTRoomManager.Instance.IsGM)
            {
                var actionPayload = new Killtime.Multi.VTTCombatActionPayload
                {
                    action = "grenade",
                    actorId = Killtime.Multi.VTTTableSync.UnitIdOf(attacker),
                    destQ = blastCoords.Q,
                    destR = blastCoords.R,
                    grenadeItemId = grenadeDef != null ? grenadeDef.ItemId : "",
                    useLauncher = launcher != null && launcher.IsLauncher,
                    aimed = throwOutcome.IsOnTarget,
                    blastRadius = grenadeDef != null ? grenadeDef.BlastRadius : 0,
                    attackerCostAP = totalPa,
                    combatLog = log
                };
                Killtime.Multi.VTTTableSync.Instance.BroadcastCombatAction(actionPayload);
            }

            if (CurrentTarget != null && !CurrentTarget.Stats.IsAlive) AutoTargetNextAlive();
            UpdateAdaptiveMusic();
            _turnManager?.CheckCombatOver();
        }

        // =====================================================================
        // ARMES : lancer 2 PA + désarmement Bras Droit fait tomber (Livre VI §24).
        // =====================================================================

        public const int WeaponThrowAPCost = 2;
        public const int WeaponThrowMaxRange = 4;
        public const int WeaponSwapAPCost = 1;

        /// <summary>
        /// Lancer d'arme équipée (2 PA) : Hachette de Jet et armes de fortune.
        /// Duel Ballistique à 2-4 cases, dégâts = BaseDamage + différentiel + EC,
        /// puis l'arme quitte l'inventaire et tombe à la case de la cible.
        /// </summary>
        public void ExecuteWeaponThrow(TacticalUnit attacker, TacticalUnit defender)
        {
            if (_isAttackInProgress)
            {
                Log("⚠️ Lancer ignoré : une résolution est déjà en cours.");
                return;
            }
            if (TurnManager.IsMultiplayerPlayerClient())
            {
                Log("⚠️ Lancer d'arme réservé au GM en multijoueur (pas encore synchronisé VTT).");
                return;
            }
            if (attacker == null) attacker = _turnManager != null ? _turnManager.ActiveUnit : PlayerUnit;
            if (attacker == null || attacker.Stats == null) { Log("⚠️ Aucun attaquant pour le lancer."); return; }
            if (defender == null || defender.Stats == null || !defender.Stats.IsAlive) { Log("⚠️ Aucune cible valide pour le lancer."); return; }
            if (attacker.IsMoving) { Log($"⚠️ <b>{attacker.Stats.Name}</b> est en déplacement : attendez l'arrivée !"); return; }

            var sheet = attacker.GetOrBuildSheet();
            var weapon = sheet?.GetEquippedWeapon();
            if (weapon == null) { Log($"⚠️ <b>{attacker.Stats.Name}</b> n'a aucune arme équipée à lancer !"); return; }
            if (weapon.IsThrowableGrenade()) { Log("⚠️ Grenades : utilisez l'action Grenade (2 PA), pas le lancer d'arme."); return; }

            int dist = attacker.CurrentCoords.DistanceTo(defender.CurrentCoords);
            if (dist < 2 || dist > WeaponThrowMaxRange)
            {
                Log($"⚠️ <b>Lancer impossible</b> : {dist} cases (portée {2}-{WeaponThrowMaxRange}). Au contact, frappez ; au-delà, rapprochez-vous.");
                return;
            }

            DiceType atkDie = attacker.Stats.GetSkillDie(SkillType.Ballistique, true);
            if (!attacker.Stats.CanAttack(atkDie)) { Log($"⚠️ <b>{attacker.Stats.Name}</b> a épuisé son quota d'attaque ce tour !"); return; }
            if (!InfiniteAP && attacker.Stats.CurrentActionPoints < WeaponThrowAPCost)
            {
                Log($"⚠️ PA insuffisants : lancer = {WeaponThrowAPCost} PA (reste {attacker.Stats.CurrentActionPoints}).");
                return;
            }

            string weaponName = weapon.Name;
            int weaponDmg = Math.Max(1, weapon.BaseDamage);
            int weaponEc = Math.Max(0, weapon.AttackBonusEc);
            string weaponId = weapon.ItemId;

            Vector3 dir = defender.transform.position - attacker.transform.position;
            dir.y = 0f;
            if (dir != Vector3.zero) attacker.transform.rotation = Quaternion.LookRotation(dir);
            attacker.GetComponent<TacticalUnitVisual>()?.TriggerGrenadeThrow();

            _combatCalculator.SetContactDistanceState(false);
            CoverType cover = GetCoverToTarget(attacker, defender);
            // Duel aveugle manuel (pas de double débit PA) : Begin consomme la base 2 PA,
            // échec => on avorte sans perdre l'arme ni le quota d'attaque.
            // RD-036 : le lancer suit la règle hauteur du tir (balistique ±1, laser exempté).
            ElevationRuling throwRuling = GetElevationRuling(attacker, defender, SkillType.Ballistique, weapon, false);
            var throwDuel = _combatCalculator.BeginSkillDuel(
                attacker.Stats, defender.Stats, BodyPart.Torse,
                SkillType.Ballistique, SkillType.Esquive, weaponDmg, false,
                true, null, null,
                defender.Stats.BaseArmorAbsorption, out string throwError, cover, weaponEc,
                false, throwRuling.AttackMod, throwRuling.Label);
            if (throwDuel == null)
            {
                Log(throwError ?? "⚠️ Lancer impossible : PA insuffisants.");
                return;
            }
            _combatCalculator.DeclareAttackerStakes(throwDuel, 0, 0);
            if (throwDuel.DefenderWantsToDefend && !throwDuel.IsDefenderIncapacitated && throwDuel.CanDefenderReact)
                _combatCalculator.AutoDeclareDefenderStakes(throwDuel);
            else
                _combatCalculator.DeclareDefenderStakes(throwDuel, SkillType.Esquive, false, 0, 0);
            var result = _combatCalculator.ResolveBlindDuel(throwDuel);
            attacker.Stats.RegisterAttack();

            Log($"🗡️ <b>LANCER</b> : {attacker.Stats.Name} lance <b>{weaponName}</b> sur {defender.Stats.Name} ({dist} cases, -{WeaponThrowAPCost} PA)\n   {result.CombatLog}");
            HandleChannelInterruptionFx(defender, result.ChannelInterrupted);

            // L'arme quitte la main dans tous les cas (touché ou manqué) et tombe à la case cible.
            try
            {
                var toDrop = sheet.Inventory?.Find(i => i != null && i.ItemId == weaponId);
                if (toDrop != null) sheet.RemoveItem(weaponId);
                else toDrop = weapon;
                toDrop.IsEquipped = false;
                attacker.NotifyInventoryChanged(true);
                Vector3 spawnPos = defender.transform.position;
                var node = _grid != null ? _grid.GetNode(defender.CurrentCoords) : null;
                if (node != null) spawnPos = node.WorldPosition;
                DroppedWeaponPickup.SpawnAt(toDrop, spawnPos, attacker.Stats.Name);
                Log($"⚔ <b>{weaponName}</b> tombe au sol en ({defender.CurrentCoords.Q},{defender.CurrentCoords.R}).");
            }
            catch (Exception e) { Log($"⚠️ Dépôt de l'arme lancée impossible : {e.Message}"); }

            var defVis = defender.GetComponent<TacticalUnitVisual>();
            if (result.IsHit) defVis?.TriggerHitFlash();
            if (!defender.Stats.IsAlive) AutoTargetNextAlive();
            UpdateAdaptiveMusic();
            _turnManager?.CheckCombatOver();
        }

        /// <summary>
        /// RD-033 : décompte le coup tiré puis enraye sur critique adverse.
        /// Appelé après chaque duel où un coup est réellement parti (jet attaquant lancé).
        /// </summary>
        public void ConsumeShotAndCheckJam(TacticalUnit attacker, InventoryItem weapon, DamageResult result, bool hdShot)
        {
            if (attacker == null || weapon == null || weapon.AmmoCapacity <= 0) return;
            weapon.AmmoRemaining = Math.Max(0, weapon.AmmoRemaining - 1);
            if (weapon.AmmoRemaining <= 0) weapon.LoadedHD = false;
            string ammoNote = $" [🔋{weapon.AmmoRemaining}/{weapon.AmmoCapacity}{(weapon.LoadedHD ? "+HD" : "")}]";
            attacker.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText(
                hdShot ? $"Tir HD +1D{ammoNote}" : $"Coup parti{ammoNote}", new Color(1f, 0.72f, 0.15f));
            // Enrayement sur critique adverse (parade/tir contré magistral).
            if (result.DefenseRoll.IsCriticalSuccess && !weapon.Jammed)
            {
                weapon.Jammed = true;
                attacker.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("🔧 ENRAYÉE !", Color.red);
                Log($"🔧 <b>ENRAYEMENT</b> : {weapon.Name} de {attacker.Stats.Name} s'enraye sur la parade critique ! Désenrayez (1 PA).");
            }
            if (weapon.AmmoRemaining <= 0)
                Log($"🔋 <b>Chargeur vide</b> : {weapon.Name} — rechargez ({weapon.ReloadAPCost} PA).");
            attacker.NotifyInventoryChanged(true);
        }

        /// <summary>
        /// RD-033 : recharge le chargeur depuis la réserve (éjecte le reste d'un autre
        /// type). useHD exige HeavyAmmo (sniper/Deglazer). Ne compte pas comme attaque.
        /// </summary>
        public bool TryReloadWeapon(TacticalUnit unit, bool useHD, out string message)
        {
            message = "";
            if (unit == null || unit.Stats == null) return false;
            var sheet = unit.GetOrBuildSheet();
            var weapon = sheet?.GetEquippedWeapon();
            if (weapon == null || weapon.AmmoCapacity <= 0) { message = "Aucune arme à chargeur équipée."; return false; }
            if (weapon.Jammed) { message = $"🔧 {weapon.Name} est enrayée : désenrayez d'abord (1 PA)."; return false; }
            if (useHD && !weapon.HeavyAmmo) { message = $"⛔ {weapon.Name} n'accepte pas la Haute Densité (sniper/Deglazer)."; return false; }
            int need = weapon.AmmoCapacity - weapon.AmmoRemaining;
            if (need <= 0) { message = $"🔋 {weapon.Name} déjà plein ({weapon.AmmoCapacity})."; return false; }
            int stock = WeaponAmmo.StockShots(sheet, weapon.AmmoType, useHD);
            if (stock <= 0) { message = $"⚠️ Aucune réserve {(useHD ? "Haute Densité" : weapon.AmmoType)} — achetez au marché."; return false; }
            int cost = Math.Max(1, weapon.ReloadAPCost);
            if (!InfiniteAP && unit.Stats.CurrentActionPoints < cost) { message = $"⚠️ PA insuffisants : recharger = {cost} PA."; return false; }
            int taken = WeaponAmmo.TakeShots(sheet, weapon.AmmoType, useHD, Math.Min(need, stock));
            if (taken <= 0) { message = "⚠️ Réserve introuvable."; return false; }
            int ejected = (weapon.LoadedHD != useHD) ? weapon.AmmoRemaining : 0;
            weapon.AmmoRemaining = Math.Min(weapon.AmmoCapacity, taken);
            weapon.LoadedHD = useHD;
            if (!InfiniteAP) unit.Stats.ConsumeActionPoints(cost);
            unit.NotifyInventoryChanged(true);
            message = $"🔋 <b>{unit.Stats.Name}</b> recharge <b>{weapon.Name}</b>{(useHD ? " <b>HD +1D</b>" : "")} : +{taken} coups"
                + (ejected > 0 ? $" ({ejected} éjectés)" : "")
                + (taken < need ? " — réserve épuisée" : "")
                + $" [{weapon.AmmoRemaining}/{weapon.AmmoCapacity}] (-{cost} PA).";
            return true;
        }

        /// <summary>RD-033 : désenraye l'arme équipée (1 PA, gratuit 0 PA avec Kit d'Entretien d'Armes).</summary>
        public bool TryClearJam(TacticalUnit unit, out string message)
        {
            message = "";
            if (unit == null || unit.Stats == null) return false;
            var weapon = unit.GetOrBuildSheet()?.GetEquippedWeapon();
            if (weapon == null || !weapon.Jammed) { message = "Arme non enrayée."; return false; }
            bool hasKit = SmokeScreen.HasItemByName(unit.Stats, "Kit d'Entretien");
            int cost = hasKit ? 0 : 1;
            if (!InfiniteAP && cost > 0 && unit.Stats.CurrentActionPoints < cost) { message = $"⚠️ PA insuffisants : désenrayer = {cost} PA."; return false; }
            weapon.Jammed = false;
            if (!InfiniteAP && cost > 0) unit.Stats.ConsumeActionPoints(cost);
            unit.NotifyInventoryChanged(true);
            message = hasKit
                ? $"🔧 <b>{unit.Stats.Name}</b> désenraye <b>{weapon.Name}</b> avec son <b>Kit d'Entretien d'Armes</b> (0 PA) !"
                : $"🔧 <b>{unit.Stats.Name}</b> désenraye <b>{weapon.Name}</b> (-1 PA).";
            return true;
        }

        /// <summary>
        /// Recharge d'urgence de la barrière de champ de force (1 PA) via Cellule Nytharite (Standard 10 PV / Pure 25 PV).
        /// </summary>
        public bool TryRechargeShieldWithCell(TacticalUnit unit, bool usePureCell, out string message)
        {
            message = "";
            if (unit == null || unit.Stats == null) return false;
            if (unit.Stats.MaxShieldHP <= 0) { message = "Aucun champ de force équipé."; return false; }
            if (unit.Stats.CurrentShieldHP >= unit.Stats.MaxShieldHP) { message = "Barrière énergétique déjà au maximum."; return false; }
            if (!InfiniteAP && unit.Stats.CurrentActionPoints < 1) { message = "⚠️ PA insuffisants (1 PA requis)."; return false; }

            string cellName = usePureCell ? "Cellule Nytharite Pure" : "Cellule Nytharite Standard";
            var sheet = unit.GetOrBuildSheet();
            var cell = SmokeScreen.FindItemByName(unit.Stats, cellName);
            if (cell == null) { message = $"⚠️ Aucune {cellName} en inventaire."; return false; }

            int restoreAmount = usePureCell ? 25 : 10;
            int before = unit.Stats.CurrentShieldHP;
            unit.Stats.CurrentShieldHP = Math.Min(unit.Stats.MaxShieldHP, unit.Stats.CurrentShieldHP + restoreAmount);
            int restored = unit.Stats.CurrentShieldHP - before;

            sheet.ConsumeOne(cell.ItemId);
            unit.NotifyInventoryChanged(true);
            if (!InfiniteAP) unit.Stats.ConsumeActionPoints(1);

            var vis = unit.GetComponent<TacticalUnitVisual>();
            vis?.SpawnFloatingText($"🔮 +{restored} Bouclier", Color.cyan);
            message = $"🔮 <b>{unit.Stats.Name}</b> consomme une <b>{cellName}</b> : +{restored} PV de bouclier [{unit.Stats.CurrentShieldHP}/{unit.Stats.MaxShieldHP}] (-1 PA).";
            return true;
        }

        /// <summary>
        /// Désarmement Bras Droit : sur touché au bras porteur, différentiel élevé
        /// arrache l'arme qui tombe au sol (2 cases max). Seuils Livre III :
        /// base 4, Croc de Désarmement 3, Désarmement Fleuret 2.
        /// </summary>
        public void TryDisarmDefender(TacticalUnit attacker, TacticalUnit defender, BodyPart targetedPart, DamageResult result)
        {
            if (attacker == null || defender == null) return;
            if (targetedPart != BodyPart.BrasDroit && result.TargetPart != BodyPart.BrasDroit) return;
            if (!result.IsHit) return;
            if (defender.Stats == null || !defender.Stats.IsAlive) return;
            var sheet = defender.GetOrBuildSheet();
            var held = sheet?.GetEquippedWeapon();
            if (held == null) return;

            int threshold = 4;
            try
            {
                if (attacker.Stats != null)
                {
                    if (attacker.Stats.HasSpecialization("Escrime : Désarmement Fleuret")) threshold = 2;
                    else if (attacker.Stats.HasSpecialization("Hache de Guerre : Croc de Désarmement")) threshold = 3;
                }
            }
            catch { }
            if (result.Differential < threshold) return;

            try
            {
                string droppedName = held.Name;
                string droppedId = held.ItemId;
                var toDrop = sheet.Inventory?.Find(i => i != null && i.ItemId == droppedId) ?? held;
                sheet.RemoveItem(droppedId);
                toDrop.IsEquipped = false;
                defender.NotifyInventoryChanged(true);
                Vector3 spawnPos = defender.transform.position;
                var node = _grid != null ? _grid.GetNode(defender.CurrentCoords) : null;
                if (node != null) spawnPos = node.WorldPosition;
                // L'arme vole à la case du porteur (2 cases au sol selon Livre VI, ici stessa case).
                DroppedWeaponPickup.SpawnAt(toDrop, spawnPos, defender.Stats.Name);
                defender.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText($"🗡️ DÉSARMÉ ! {droppedName}", Color.yellow);
                Log($"🗡️ <b>DÉSARMEMENT</b> : {attacker.Stats.Name} arrache <b>{droppedName}</b> à {defender.Stats.Name} (Diff +{result.Differential} ≥ {threshold}) — tombe au sol !");
                RecordChronoSnapshot($"Désarmement : {attacker.Stats.Name} -> {defender.Stats.Name}");
            }
            catch (Exception e) { Log($"⚠️ Désarmement impossible : {e.Message}"); }
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
            if (actor == null || target == null || actor.Stats == null || target.Stats == null) return;
            if (!actor.Stats.IsAlive || !target.Stats.IsAlive) return;

            int dist = actor.CurrentCoords.DistanceTo(target.CurrentCoords);
            if (dist > 1)
            {
                Log($"⚠️ <b>Lutte impossible</b> : cible hors de portée de contact ({dist} cases > 1).");
                return;
            }

            if (!InfiniteAP && actor.Stats.CurrentActionPoints < GrappleAPCost)
            {
                Log($"⚠️ PA insuffisants : la prise de lutte exige {GrappleAPCost} PA (reste {actor.Stats.CurrentActionPoints}).");
                return;
            }

            if (!InfiniteAP) actor.Stats.ConsumeActionPoints(GrappleAPCost);

            int injectedAP = CombatUI.CombatContextMenuUI.CurrentInjectedAP;
            int injectedPE = CombatUI.CombatContextMenuUI.CurrentInjectedPE;

            var duel = _combatCalculator.ResolveGrappleDuel(
                actor.Stats, target.Stats,
                attackerBonusAP: injectedAP, attackerPE: injectedPE,
                defenderAutoStakes: true);

            var actVis = actor.GetComponent<TacticalUnitVisual>();
            var tgtVis = target.GetComponent<TacticalUnitVisual>();

            if (duel.AttackerWins)
            {
                actVis?.SpawnFloatingText("🥋 PRISE EN LUTTE (-2 PA)", Color.cyan);
                tgtVis?.TriggerHitFlash();
                tgtVis?.SpawnFloatingText("IMMOBILISÉ", new Color(1f, 0.45f, 0.15f));
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayAt(SoundId.Trauma_Shock, target.transform.position, 0.8f);
            }
            else
            {
                actVis?.SpawnFloatingText("Prise manquée (-2 PA)", Color.gray);
                tgtVis?.SpawnFloatingText("ESQUIVÉ", Color.cyan);
            }

            Log($"🥋 <b>{actor.Stats.Name}</b> tente une prise de lutte sur <b>{target.Stats.Name}</b> (-{GrappleAPCost} PA) :\n   {duel.CombatLog}");
            RecordChronoSnapshot($"Lutte : {actor.Stats.Name} -> {target.Stats.Name}");
            _turnManager?.CheckCombatOver();
        }

        /// <summary>
        /// RD-039 : Tente de se libérer d'une étreinte de lutte (2 PA).
        /// Duel opposé aveugle Athlétisme/MainsNues/Acrobatie (victime) vs Athlétisme (grappler).
        /// Succès = prise brisée et statut Immobilisé dissipé.
        /// </summary>
        public void ExecuteGrappleEscape(TacticalUnit victim, TacticalUnit grappler)
        {
            if (victim == null || grappler == null || victim.Stats == null || grappler.Stats == null) return;
            if (!victim.Stats.IsAlive || !grappler.Stats.IsAlive) return;

            if (!InfiniteAP && victim.Stats.CurrentActionPoints < GrappleAPCost)
            {
                Log($"⚠️ PA insuffisants : se dégager exige {GrappleAPCost} PA (reste {victim.Stats.CurrentActionPoints}).");
                return;
            }

            if (!InfiniteAP) victim.Stats.ConsumeActionPoints(GrappleAPCost);

            int injectedAP = CombatUI.CombatContextMenuUI.CurrentInjectedAP;
            int injectedPE = CombatUI.CombatContextMenuUI.CurrentInjectedPE;

            var duel = _combatCalculator.ResolveGrappleEscapeDuel(
                victim.Stats, grappler.Stats,
                victimBonusAP: injectedAP, victimPE: injectedPE,
                grapplerAutoStakes: true);

            var vicVis = victim.GetComponent<TacticalUnitVisual>();
            var grpVis = grappler.GetComponent<TacticalUnitVisual>();

            if (duel.AttackerWins)
            {
                vicVis?.SpawnFloatingText("🔓 DÉGAGÉ ! (-2 PA)", Color.green);
                grpVis?.SpawnFloatingText("Prise rompue", Color.gray);
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayAt(SoundId.Trauma_Shock, victim.transform.position, 0.8f);
            }
            else
            {
                vicVis?.SpawnFloatingText("Maintenu en lutte (-2 PA)", Color.red);
            }

            Log($"🔓 <b>{victim.Stats.Name}</b> tente de se libérer de la prise de <b>{grappler.Stats.Name}</b> (-{GrappleAPCost} PA) :\n   {duel.CombatLog}");
            RecordChronoSnapshot($"Dégagement lutte : {victim.Stats.Name} -> {grappler.Stats.Name}");
            _turnManager?.CheckCombatOver();
        }

        /// <summary>
        /// RD-039 : Étranglement au contact sur cible saisie ou immobilisée (2 PA).
        /// Duel opposé aveugle Athlétisme vs Endurance. Succès = Asphyxie (2 tours, DoT + drain PA).
        /// </summary>
        public void ExecuteStrangulation(TacticalUnit actor, TacticalUnit target)
        {
            if (actor == null || target == null || actor.Stats == null || target.Stats == null) return;
            if (!actor.Stats.IsAlive || !target.Stats.IsAlive) return;

            int dist = actor.CurrentCoords.DistanceTo(target.CurrentCoords);
            if (dist > 1)
            {
                Log($"⚠️ <b>Étranglement impossible</b> : hors de portée de contact ({dist} cases > 1).");
                return;
            }

            if (!InfiniteAP && actor.Stats.CurrentActionPoints < StrangulationAPCost)
            {
                Log($"⚠️ PA insuffisants : l'étranglement exige {StrangulationAPCost} PA (reste {actor.Stats.CurrentActionPoints}).");
                return;
            }

            if (!InfiniteAP) actor.Stats.ConsumeActionPoints(StrangulationAPCost);

            int injectedAP = CombatUI.CombatContextMenuUI.CurrentInjectedAP;
            int injectedPE = CombatUI.CombatContextMenuUI.CurrentInjectedPE;

            var duel = _combatCalculator.ResolveStrangulationDuel(
                actor.Stats, target.Stats,
                attackerBonusAP: injectedAP, attackerPE: injectedPE,
                defenderAutoStakes: true);

            var actVis = actor.GetComponent<TacticalUnitVisual>();
            var tgtVis = target.GetComponent<TacticalUnitVisual>();

            if (duel.AttackerWins)
            {
                actVis?.SpawnFloatingText("🫁 ÉTRANGLEMENT (-2 PA)", new Color(0.2f, 0.95f, 0.75f));
                tgtVis?.TriggerHitFlash();
                tgtVis?.SpawnFloatingText("ASPHYXIE (-1 PA/tour)", new Color(0.2f, 0.95f, 0.75f));
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayAt(SoundId.Trauma_Shock, target.transform.position, 0.9f);
                if (!target.Stats.IsAlive) AutoTargetNextAlive();
            }
            else
            {
                actVis?.SpawnFloatingText("Étranglement repoussé (-2 PA)", Color.gray);
                tgtVis?.SpawnFloatingText("RÉSISTÉ", Color.cyan);
            }

            Log($"🫁 <b>{actor.Stats.Name}</b> étrangle <b>{target.Stats.Name}</b> (-{StrangulationAPCost} PA) :\n   {duel.CombatLog}");
            RecordChronoSnapshot($"Étranglement : {actor.Stats.Name} -> {target.Stats.Name}");
            _turnManager?.CheckCombatOver();
        }

        /// <summary>
        /// RD-040 : Exécute une Bousculade générique ou un Coup de bouclier (2 PA).
        /// Duel opposé aveugle de Force (Athlétisme / Mains Nues / Défense Corporelle).
        /// Victoire : recul forcé d'1 case (Knockback). Si un mur ou un obstacle bloque le recul, la cible chute À Terre.
        /// </summary>
        public void ExecuteShove(TacticalUnit attacker, TacticalUnit defender, bool isShieldBash, int attackerBonusAP = 0, int attackerPE = 0)
        {
            if (_isAttackInProgress)
            {
                Log("⚠️ Action ignorée : une résolution est déjà en cours.");
                return;
            }
            if (attacker == null || defender == null || attacker.Stats == null || defender.Stats == null) return;
            if (!attacker.Stats.IsAlive || !defender.Stats.IsAlive) return;

            if (attacker.IsMoving)
            {
                Log($"⚠️ <b>{attacker.Stats.Name}</b> est en déplacement : attendez l'arrivée !");
                return;
            }

            int dist = TitanFootprint.MinDistanceBetweenUnits(attacker.CurrentCoords, attacker.FootprintType, defender.CurrentCoords, defender.FootprintType);
            if (dist > 1)
            {
                Log($"⚠️ <b>Bousculade impossible</b> : cible hors de portée de contact ({dist} cases > 1).");
                return;
            }

            if (TurnManager.IsMultiplayerPlayerClient())
            {
                Log("⚠️ Bousculade réservée au GM en multijoueur (pas encore synchronisée VTT).");
                return;
            }

            SkillType checkSkill = isShieldBash ? SkillType.DefenseCorporelle : SkillType.Athletisme;
            DiceType atkDie = attacker.Stats.GetSkillDie(checkSkill, true);
            if (!attacker.Stats.CanAttack(atkDie))
            {
                int maxAttacks = attacker.Stats.GetMaxAttacksAllowed(atkDie);
                Log($"⚠️ <b>{attacker.Stats.Name}</b> a déjà épuisé son quota d'attaque ce tour ({attacker.Stats.AttacksThisTurn}/{maxAttacks}) !");
                return;
            }

            const int shoveCost = 2;
            if (!InfiniteAP && attacker.Stats.CurrentActionPoints < shoveCost)
            {
                Log($"⚠️ PA insuffisants : la bousculade exige {shoveCost} PA (reste {attacker.Stats.CurrentActionPoints}).");
                return;
            }

            if (!InfiniteAP) attacker.Stats.ConsumeActionPoints(shoveCost);

            int injectedAP = attackerBonusAP > 0 ? attackerBonusAP : CombatUI.CombatContextMenuUI.CurrentInjectedAP;
            int injectedPE = attackerPE > 0 ? attackerPE : CombatUI.CombatContextMenuUI.CurrentInjectedPE;

            Vector3 combatDir = defender.transform.position - attacker.transform.position;
            combatDir.y = 0f;
            if (combatDir != Vector3.zero)
            {
                attacker.transform.rotation = Quaternion.LookRotation(combatDir);
                if (defender.Stats.CanDefendActively())
                {
                    defender.transform.rotation = Quaternion.LookRotation(-combatDir);
                }
            }

            var attVis = attacker.GetComponent<TacticalUnitVisual>();
            var defVis = defender.GetComponent<TacticalUnitVisual>();

            if (isShieldBash)
            {
                attVis?.TriggerBodyBlock();
            }
            else
            {
                attVis?.TriggerRoundkick();
            }

            if (KilltimeAudioManager.Instance != null)
                KilltimeAudioManager.Instance.PlayAt(SoundId.Attack_Whoosh, attacker.transform.position, 0.7f);

            var result = _combatCalculator.ResolveShoveDuel(
                attacker.Stats, defender.Stats, isShieldBash,
                attackerBonusAP: injectedAP, attackerPE: injectedPE,
                defenderAutoStakes: true);

            attacker.Stats.RegisterAttack();
            Log(result.CombatLog);

            string actionTag = isShieldBash ? "COUP DE BOUCLIER" : "BOUSCULADE";
            if (result.IsHit)
            {
                attVis?.SpawnFloatingText($"💨 {actionTag} (-{shoveCost} PA)", Color.cyan);
                defVis?.TriggerHitFlash();

                if (result.FinalDamageApplied > 0)
                {
                    defVis?.SpawnFloatingText($"-{result.FinalDamageApplied} PV", new Color(1.0f, 0.25f, 0.25f));
                }

                if (result.CausedKnockback)
                {
                    TryApplyKnockback(attacker, defender);
                }

                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayAt(SoundId.Trauma_Shock, defender.transform.position, 0.8f);

                if (result.FatalResolution == FatalBlowResolution.InstantDeath || result.FatalResolution == FatalBlowResolution.ForcedUnconscious)
                {
                    defVis?.TriggerFallingBackDeath();
                }
                else if (result.FatalResolution == FatalBlowResolution.EligibleForLastBreath)
                {
                    if (!defender.IsPlayerControlled)
                    {
                        defender.Stats.ChooseSombrer();
                        defVis?.SpawnFloatingText("[SYNCOPE] CHUTE HORS COMBAT (0 PV)", Color.cyan);
                        defVis?.TriggerFallingBackDeath();
                    }
                    else
                    {
                        defender.Stats.ChooseLastBreath();
                        defVis?.SpawnFloatingText("[SURVIE] DERNIER SOUFFLE (0 PV)", new Color(1.0f, 0.5f, 0.1f));
                    }
                }

                if (!defender.Stats.IsAlive)
                {
                    AutoTargetNextAlive();
                }
            }
            else
            {
                attVis?.SpawnFloatingText($"{actionTag} contenue (-{shoveCost} PA)", Color.gray);
                defVis?.SpawnFloatingText("RÉSISTÉ", Color.cyan);
            }

            RecordChronoSnapshot($"{actionTag} : {attacker.Stats.Name} -> {defender.Stats.Name}");
            UpdateAdaptiveMusic();
            _turnManager?.CheckCombatOver();

            if (Killtime.Multi.VTTTableSync.Instance != null 
                && !Killtime.Multi.VTTTableSync.Instance.IsApplyingRemoteAction 
                && Killtime.Multi.VTTRoomManager.Instance != null 
                && Killtime.Multi.VTTRoomManager.Instance.InRoom 
                && Killtime.Multi.VTTRoomManager.Instance.IsGM)
            {
                Killtime.Multi.VTTTableSync.Instance.BroadcastFullCombatState();
            }
        }

        /// <summary>
        /// RD-039 : Vrai si le dragger peut traîner le corps de la victime d'1 case.
        /// </summary>
        public bool CanDragBody(TacticalUnit dragger, TacticalUnit victim)
        {
            if (dragger == null || victim == null || dragger == victim) return false;
            if (dragger.Stats == null || victim.Stats == null) return false;
            if (!dragger.Stats.IsAlive) return false;
            if (!InfiniteAP && dragger.Stats.CurrentActionPoints < DragBodyAPCost) return false;
            if (dragger.CurrentCoords.DistanceTo(victim.CurrentCoords) > 1) return false;

            bool isDraggable = Killtime.Core.Combat.GrappleState.IsGrappling(dragger.Stats, victim.Stats)
                || victim.Stats.ActiveStatus.HasFlag(StatusEffect.Immobilise)
                || victim.Stats.ActiveStatus.HasFlag(StatusEffect.Inconscient)
                || victim.Stats.ActiveStatus.HasFlag(StatusEffect.ATerre)
                || !victim.Stats.IsAlive;

            if (!isDraggable) return false;
            if (_grid == null) return false;

            for (int i = 0; i < 6; i++)
            {
                var n = dragger.CurrentCoords.GetNeighbor(i);
                if (n.Equals(victim.CurrentCoords)) continue;
                if (_grid.IsFootprintFree(n, dragger.FootprintType, dragger)) return true;
            }
            return false;
        }

        /// <summary>
        /// RD-039 : Traîne le corps d'une cible saisie, immobilisée ou inconsciente d'1 case (2 PA).
        /// Le porteur recule ou pivote vers une case libre adjacente et tire la victime
        /// dans la case qu'il vient de libérer.
        /// </summary>
        public bool ExecuteDragBody(TacticalUnit dragger, TacticalUnit victim)
        {
            if (!CanDragBody(dragger, victim)) return false;

            HexCoordinates bestHex = default;
            bool found = false;
            int bestDist = -1;

            for (int i = 0; i < 6; i++)
            {
                var n = dragger.CurrentCoords.GetNeighbor(i);
                if (n.Equals(victim.CurrentCoords)) continue;
                if (_grid.IsFootprintFree(n, dragger.FootprintType, dragger))
                {
                    int d = n.DistanceTo(victim.CurrentCoords);
                    if (d > bestDist)
                    {
                        bestDist = d;
                        bestHex = n;
                        found = true;
                    }
                }
            }

            if (!found) return false;

            if (!InfiniteAP) dragger.Stats.ConsumeActionPoints(DragBodyAPCost);

            HexCoordinates vacated = dragger.CurrentCoords;

            var draggerPath = new List<HexCoordinates> { dragger.CurrentCoords, bestHex };
            var victimPath = new List<HexCoordinates> { victim.CurrentCoords, vacated };

            StartCoroutine(dragger.MoveAlongPath(draggerPath, _grid, 0, false, false));
            StartCoroutine(victim.MoveAlongPath(victimPath, _grid, 0, false, false));

            dragger.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("🤼 TRAÎNÉE (-2 PA)", Color.cyan);
            victim.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("TRAÎNÉ", Color.yellow);

            if (KilltimeAudioManager.Instance != null)
                KilltimeAudioManager.Instance.PlayAt(SoundId.Move_Footstep, dragger.transform.position, 0.7f);

            Log($"🤼 <b>{dragger.Stats.Name}</b> traîne le corps de <b>{victim.Stats.Name}</b> d'1 case vers {vacated} (-{DragBodyAPCost} PA) !");
            RecordChronoSnapshot($"Traînée corps : {dragger.Stats.Name} -> {victim.Stats.Name}");
            return true;
        }

        private TacticalUnit FindUnitByName(string unitName)
        {
            if (string.IsNullOrEmpty(unitName)) return null;
            if (PlayerUnit != null && PlayerUnit.Stats != null && PlayerUnit.Stats.Name == unitName) return PlayerUnit;
            for (int i = 0; i < _additionalPlayers.Count; i++)
                if (_additionalPlayers[i] != null && _additionalPlayers[i].Stats != null && _additionalPlayers[i].Stats.Name == unitName)
                    return _additionalPlayers[i];
            for (int i = 0; i < SparringDummies.Count; i++)
                if (SparringDummies[i] != null && SparringDummies[i].Stats != null && SparringDummies[i].Stats.Name == unitName)
                    return SparringDummies[i];
            var all = FindObjectsByType<TacticalUnit>();
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && all[i].Stats != null && all[i].Stats.Name == unitName)
                    return all[i];
            return null;
        }

        /// <summary>Refoulement radial : repousse la cible sur le voisin le plus loin du souffle.</summary>
        public bool TryApplyBlastKnockback(TacticalUnit defender, HexCoordinates blastCoords)
        {
            if (defender == null || _grid == null) return false;
            HexCoordinates best = defender.CurrentCoords;
            int bestDist = defender.CurrentCoords.DistanceTo(blastCoords);
            for (int dir = 0; dir < 6; dir++)
            {
                var n = defender.CurrentCoords.GetNeighbor(dir);
                var node = _grid.GetNode(n);
                if (node == null || !node.IsWalkable || node.IsOccupied) continue;
                int d = n.DistanceTo(blastCoords);
                if (d > bestDist) { bestDist = d; best = n; }
            }
            if (!best.Equals(defender.CurrentCoords) && defender.TeleportTo(best, _grid))
            {
                Killtime.Core.Combat.GrappleState.ReleaseGrapple(defender.Stats);
                var vis = defender.GetComponent<TacticalUnitVisual>();
                vis?.SpawnFloatingText("[SOUFFLE] Projeté !", Color.yellow);
                Log($"💨 <b>SOUFFLE</b> : {defender.Stats.Name} est projeté(e) en ({best.Q}, {best.R}) !");
                return true;
            }
            defender.Stats.ApplyStatus(StatusEffect.Destabilise, 1);
            return false;
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
            if (actor == null || target == null || actor == target) return;
            if (actor.Stats == null || !actor.Stats.IsAlive || target.Stats == null || !target.Stats.IsAlive) return;

            if (_isAttackInProgress)
            {
                Log("⚠️ Charge ignorée : une action est déjà en cours.");
                return;
            }

            int attackCost = CoreRulesConfig.Instance != null ? CoreRulesConfig.Instance.BaseAttackAPCost : 2;
            int ap = actor.Stats.CurrentActionPoints;
            if (_pathfinder == null) _pathfinder = new HexPathfinder(_grid);

            int minDistance = CoreRulesConfig.Instance != null ? CoreRulesConfig.Instance.ChargeMinDistance : 3;
            var path = _pathfinder.FindChargePath(actor.CurrentCoords, target.CurrentCoords, ap, attackCost, out int moveCost, minDistance, actor.FootprintType);
            if (path == null || path.Count <= 1 || (path.Count - 1) < minDistance)
            {
                Log($"⚠️ <b>Charge impossible</b> : aucun chemin de {minDistance}+ cases disponible vers {target.Stats.Name} pour {actor.Stats.Name}.");
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayUI(SoundId.UI_Denied, 0.6f);
                return;
            }

            int extraCost = CoreRulesConfig.Instance != null ? CoreRulesConfig.Instance.ChargeManeuverExtraAPCost : 0;
            int effectiveCost = actor.ComputeMovementAPCost(moveCost) + extraCost;
            if (actor.Stats.CurrentActionPoints < effectiveCost + attackCost)
            {
                Log($"⚠️ <b>Charge impossible</b> : PA insuffisants ({actor.Stats.CurrentActionPoints} PA disponibles pour un coût de {effectiveCost + attackCost} PA).");
                return;
            }

            Log($"⚡ <b>{actor.Stats.Name}</b> s'élance en <b>CHARGE D'ASSAUT</b> sur {target.Stats.Name} ({path.Count - 1} cases, coût déplacement {effectiveCost} PA) !");
            StartCoroutine(ChargeAndStrikeRoutine(actor, target, targetedPart, path, effectiveCost, attackCost, attackerBonusAP, attackerPE));
        }

        private IEnumerator ChargeAndStrikeRoutine(
            TacticalUnit actor,
            TacticalUnit target,
            BodyPart targetedPart,
            List<HexCoordinates> path,
            int moveCost,
            int attackCost,
            int attackerBonusAP,
            int attackerPE)
        {
            _isAttackInProgress = true;
            if (KilltimeAudioManager.Instance != null)
                KilltimeAudioManager.Instance.PlayAt(SoundId.Move_Dash, actor.transform.position, 0.7f);

            // Exécute le déplacement jusqu'au contact
            yield return StartCoroutine(actor.MoveAlongPath(path, _grid, moveCost));

            // Attend la fin effective du mouvement
            while (actor.IsMoving)
            {
                yield return null;
            }

            _isAttackInProgress = false;

            // Si l'attaquant a survécu et est toujours au contact de la cible
            if (actor.Stats != null && actor.Stats.IsAlive && target.Stats != null && target.Stats.IsAlive)
            {
                int dist = actor.CurrentCoords.DistanceTo(target.CurrentCoords);
                if (dist <= 1)
                {
                    var weapon = actor.Sheet?.GetEquippedWeapon();
                    SkillType meleeSkill = weapon != null
                        ? SkillDefinitions.ResolveBaseSkill(weapon.AssociatedSkill)
                        : SkillType.MainsNues;

                    ExecuteAttack(targetedPart, cancelPenaltyWithAP: false,
                        attackSkill: meleeSkill,
                        attackerBonusAP: attackerBonusAP,
                        attackerPE: attackerPE,
                        explicitTarget: target,
                        explicitAttacker: actor);
                }
                else
                {
                    Log($"⚠️ <b>Charge interrompue</b> : {actor.Stats.Name} n'a pas pu atteindre le contact de {target.Stats.Name}.");
                }
            }
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
            var members = new List<TacticalUnit>();
            if (actor == null || target == null) return members;
            if (_turnManager == null) return members;

            int cost = CoreRulesConfig.Instance != null ? CoreRulesConfig.Instance.CombinedAttackAPCost : 2;
            bool isRanged = (attackSkill == SkillType.Ballistique || attackSkill == SkillType.ProjectilesTir);

            var order = _turnManager.TurnOrder;
            for (int i = 0; i < order.Count; i++)
            {
                var u = order[i];
                if (u == null || u.Stats == null || !u.Stats.IsAlive) continue;
                if (u == target) continue;
                if (u.IsPlayerControlled != actor.IsPlayerControlled) continue;
                if (u.Stats.CurrentActionPoints < cost) continue;

                int dist = TitanFootprint.MinDistanceBetweenUnits(u.CurrentCoords, u.FootprintType, target.CurrentCoords, target.FootprintType);
                if (isRanged)
                {
                    var w = u.Sheet?.GetEquippedWeapon();
                    int maxRange = (w != null && w.RangeInTiles > 1) ? w.RangeInTiles : 1;
                    if (dist > maxRange) continue;
                    if (!ChargeState.CanFireRanged(u.Stats)) continue;
                }
                else
                {
                    if (dist > 1) continue;
                }
                members.Add(u);
            }

            // L'acteur en tête s'il est éligible (pour l'ordre du log).
            if (members.Contains(actor))
            {
                members.Remove(actor);
                members.Insert(0, actor);
            }
            return members;
        }

        /// <summary>
        /// RD-084 : Résout la compétence partagée de l'assaut (mêlée au contact,
        /// Ballistique à distance si l'acteur a une arme à distance).
        /// </summary>
        public bool TryResolveCombinedSkill(TacticalUnit actor, TacticalUnit target, out SkillType attackSkill)
        {
            attackSkill = SkillType.MainsNues;
            if (actor == null || target == null) return false;
            int dist = TitanFootprint.MinDistanceBetweenUnits(actor.CurrentCoords, actor.FootprintType, target.CurrentCoords, target.FootprintType);
            if (dist <= 1)
            {
                var weapon = actor.Sheet?.GetEquippedWeapon();
                attackSkill = weapon != null
                    ? SkillDefinitions.ResolveBaseSkill(weapon.AssociatedSkill)
                    : SkillType.MainsNues;
                // Arme à distance au contact : coup de crosse d'urgence à mains nues.
                if (attackSkill == SkillType.Ballistique || attackSkill == SkillType.ProjectilesTir)
                    attackSkill = SkillType.MainsNues;
                return true;
            }
            var ranged = actor.Sheet?.GetEquippedWeapon();
            if (ranged != null && ranged.RangeInTiles > 1)
            {
                attackSkill = SkillType.Ballistique;
                return true;
            }
            return false;
        }

        /// <summary>
        /// RD-084 : Exécute un assaut groupé (Livre VI §25.2). Les participants
        /// sont synchronisés sur l'initiative du membre le plus lent, leurs
        /// résultats offensifs sommés face à l'unique défense de la cible.
        /// </summary>
        public void ExecuteCombinedAttack(TacticalUnit actor, TacticalUnit target, BodyPart targetedPart = BodyPart.Torse)
        {
            if (actor == null || target == null || actor.Stats == null || target.Stats == null) return;
            if (!actor.Stats.IsAlive || !target.Stats.IsAlive) return;
            if (actor == target) return;

            if (!TryResolveCombinedSkill(actor, target, out SkillType attackSkill))
            {
                Log($"⚠️ <b>Assaut groupé impossible</b> : {actor.Stats.Name} hors de portée de {target.Stats.Name} (ni contact ni arme à distance).");
                return;
            }

            var members = CollectCombinedAttackers(actor, target, attackSkill);
            int minMembers = CoreRulesConfig.Instance != null ? Math.Max(2, CoreRulesConfig.Instance.CombinedAttackMinMembers) : 2;
            if (members.Count < minMembers)
            {
                Log($"⚠️ <b>Assaut groupé impossible</b> : {members.Count} assaillant(s) à portée, {minMembers} minimum requis (Livre VI §25.2).");
                return;
            }

            int slowest = _turnManager != null ? _turnManager.GetSlowestInitiativeFor(members) : GroupAssaultState.GetSlowestInitiative(ToStatsList(members));

            // Dégâts d'arme par participant (arme équipée ou 5 à mains nues).
            var weapons = new List<int>(members.Count);
            for (int i = 0; i < members.Count; i++)
            {
                var w = members[i].Sheet?.GetEquippedWeapon();
                weapons.Add((w != null && w.BaseDamage > 0) ? w.BaseDamage : 5);
            }

            // Mise injectée du menu (attribuée à l'acteur, tête de colonne).
            var bonusAP = new List<int>(members.Count);
            var bonusPE = new List<int>(members.Count);
            for (int i = 0; i < members.Count; i++) { bonusAP.Add(0); bonusPE.Add(0); }
            int injectedAP = CombatUI.CombatContextMenuUI.CurrentInjectedAP;
            int injectedPE = CombatUI.CombatContextMenuUI.CurrentInjectedPE;
            if (members[0] == actor)
            {
                bonusAP[0] = Math.Max(0, injectedAP);
                bonusPE[0] = Math.Max(0, injectedPE);
            }

            var stats = ToStatsList(members);
            var combined = _combatCalculator.ResolveCombinedAttack(
                stats, target.Stats, targetedPart,
                attackSkill, SkillType.Esquive,
                weaponBaseDamages: weapons,
                attackerBonusAP: bonusAP, attackerPE: bonusPE,
                defenderAutoStakes: true);

            var actVis = actor.GetComponent<TacticalUnitVisual>();
            var tgtVis = target.GetComponent<TacticalUnitVisual>();

            if (!combined.IsValid && !combined.IsHit)
            {
                Log($"⚔️ <b>{actor.Stats.Name}</b> appelle l'assaut groupé sur <b>{target.Stats.Name}</b> :\n   {combined.CombatLog}");
                return;
            }

            if (combined.IsHit)
            {
                actVis?.SpawnFloatingText($"⚔️ ASSAUT GROUPÉ x{members.Count} (ini {slowest})", Color.cyan);
                tgtVis?.TriggerHitFlash();
                tgtVis?.SpawnFloatingText($"-{combined.FinalDamageApplied} PV (sommation)", new Color(1.0f, 0.25f, 0.25f));
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayAt(SoundId.Attack_Whoosh, target.transform.position, 0.9f);

                if (combined.FatalResolution == FatalBlowResolution.InstantDeath || combined.FatalResolution == FatalBlowResolution.ForcedUnconscious)
                {
                    tgtVis?.TriggerFallingBackDeath();
                }
                else if (combined.FatalResolution == FatalBlowResolution.EligibleForLastBreath)
                {
                    if (!target.IsPlayerControlled)
                    {
                        target.Stats.ChooseSombrer();
                        tgtVis?.SpawnFloatingText("[SYNCOPE] CHUTE HORS COMBAT (0 PV)", Color.cyan);
                        tgtVis?.TriggerFallingBackDeath();
                    }
                    else
                    {
                        target.Stats.ChooseLastBreath();
                        tgtVis?.SpawnFloatingText("[SURVIE] DERNIER SOUFFLE (0 PV)", new Color(1.0f, 0.5f, 0.1f));
                    }
                }

                if (!target.Stats.IsAlive) AutoTargetNextAlive();
            }
            else
            {
                actVis?.SpawnFloatingText($"Assaut groupé paré x{members.Count}", Color.gray);
                tgtVis?.SpawnFloatingText("PARÉ", Color.cyan);
            }

            Log($"⚔️ <b>{actor.Stats.Name}</b> déclenche un <b>ASSAUT GROUPÉ x{members.Count}</b> sur <b>{target.Stats.Name}</b> (synchro initiative la plus lente : {slowest}) :\n   {combined.CombatLog}");
            HandleChannelInterruptionFx(target, combined.ChannelInterrupted);
            RecordChronoSnapshot($"Assaut groupé x{members.Count} : {actor.Stats.Name} -> {target.Stats.Name}");
            UpdateAdaptiveMusic();
            _turnManager?.CheckCombatOver();
        }

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

        /// <summary>
        /// RD-084 : Exécute un round de course-poursuite (Livre VI §25.1) :
        /// concours d'Athlétisme proie vs poursuivants. Succès proie = distance
        /// maintenue (≥ 1 case), faillite = terrain gagné. À 4 faillites cumulées :
        /// arrêt et capture immédiate (Immobilisé).
        /// </summary>
        public void ExecutePursuitRound(TacticalUnit prey, List<TacticalUnit> pursuers)
        {
            if (prey == null || prey.Stats == null || !prey.Stats.IsAlive) return;
            if (pursuers == null || pursuers.Count == 0) return;

            var alivePursuers = new List<TacticalUnit>();
            for (int i = 0; i < pursuers.Count; i++)
            {
                var p = pursuers[i];
                if (p != null && p.Stats != null && p.Stats.IsAlive && p != prey) alivePursuers.Add(p);
            }
            if (alivePursuers.Count == 0)
            {
                Log($"⚠️ <b>Poursuite impossible</b> : aucun poursuivant valide pour {prey.Stats.Name}.");
                return;
            }

            int cost = CoreRulesConfig.Instance != null ? CoreRulesConfig.Instance.PursuitAPCost : 1;
            if (!InfiniteAP)
            {
                if (prey.Stats.CurrentActionPoints < cost)
                {
                    Log($"⚠️ PA insuffisants : la fuite exige {cost} PA (reste {prey.Stats.CurrentActionPoints}).");
                    return;
                }
                for (int i = 0; i < alivePursuers.Count; i++)
                {
                    if (alivePursuers[i].Stats.CurrentActionPoints < cost)
                    {
                        Log($"⚠️ PA insuffisants : {alivePursuers[i].Stats.Name} ne peut poursuivre ({alivePursuers[i].Stats.CurrentActionPoints}/{cost} PA).");
                        return;
                    }
                }
                prey.Stats.ConsumeActionPoints(cost);
                for (int i = 0; i < alivePursuers.Count; i++) alivePursuers[i].Stats.ConsumeActionPoints(cost);
            }

            int injectedAP = CombatUI.CombatContextMenuUI.CurrentInjectedAP;
            int injectedPE = CombatUI.CombatContextMenuUI.CurrentInjectedPE;

            var pursuerStats = new List<CharacterStats>(alivePursuers.Count);
            for (int i = 0; i < alivePursuers.Count; i++) pursuerStats.Add(alivePursuers[i].Stats);

            var round = _combatCalculator.ResolvePursuitRound(
                prey.Stats, pursuerStats,
                preyBonusAP: Math.Max(0, injectedAP), preyPE: Math.Max(0, injectedPE),
                pursuersAutoStakes: true);

            var preyVis = prey.GetComponent<TacticalUnitVisual>();
            if (round.Caught)
            {
                preyVis?.SpawnFloatingText("🛑 RATTRAPÉ ET ACCULÉ !", Color.red);
                preyVis?.TriggerHitFlash();
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayAt(SoundId.Trauma_Shock, prey.transform.position, 0.9f);
            }
            else if (round.FailuresAdded > 0)
            {
                preyVis?.SpawnFloatingText($"🏃 Poursuivi ! ({round.TotalFailures}/{round.FailuresToCapture})", new Color(1.0f, 0.6f, 0.1f));
            }
            else
            {
                preyVis?.SpawnFloatingText("🏃 DISTANCE MAINTENUE", Color.green);
            }

            string pursuerNames = "";
            for (int i = 0; i < alivePursuers.Count; i++)
            {
                pursuerNames += (i > 0 ? ", " : "") + alivePursuers[i].Stats.Name;
            }
            Log($"🏃 <b>Course-poursuite</b> : {prey.Stats.Name} vs {pursuerNames} (-{cost} PA chacun) :\n   {round.CombatLog}");
            RecordChronoSnapshot($"Poursuite : {prey.Stats.Name} ({round.TotalFailures}/{round.FailuresToCapture})");
            _turnManager?.CheckCombatOver();
        }

        public void RecordChronoSnapshot(string description)
        {
            var snap = new TacticalTimeSnapshot(_turnManager.CurrentRound, 0.0f, description);
            if (PlayerUnit != null && PlayerUnit.Stats != null)
            {
                snap.RecordUnit(PlayerUnit.Stats.Name, PlayerUnit.Stats, PlayerUnit.CurrentCoords.Q, PlayerUnit.CurrentCoords.R);
            }
            foreach (var p in _additionalPlayers)
            {
                if (p != null && p.Stats != null)
                {
                    snap.RecordUnit(p.Stats.Name, p.Stats, p.CurrentCoords.Q, p.CurrentCoords.R);
                }
            }
            foreach (var d in SparringDummies)
            {
                if (d != null && d.Stats != null)
                {
                    snap.RecordUnit(d.Stats.Name, d.Stats, d.CurrentCoords.Q, d.CurrentCoords.R);
                }
            }

            _timelineBranch.PushSnapshot(snap);
        }

        public void RewindLastSnapshot()
        {
            var snap = _timelineBranch.RewindLastAction();
            if (snap == null)
            {
                Log("⏳ Fleuve du Temps : Aucun snapshot antérieur disponible.");
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayUI(SoundId.UI_Denied, 0.7f);
                return;
            }

            RestoreSnapshot(snap);
            if (KilltimeAudioManager.Instance != null)
                KilltimeAudioManager.Instance.Play(SoundId.Rewind_Time, 0.9f);
            Log($"⏪ <b>Rembobinage Temporel (Livre V)</b> : Retour au snapshot '{snap.ActionDescription}' (Round {snap.RoundNumber})");
        }

        private void RestoreSnapshot(TacticalTimeSnapshot snap)
        {
            if (snap.UnitStates.TryGetValue(PlayerUnit.Stats.Name, out var pSnap))
            {
                RestoreUnitState(PlayerUnit, pSnap);
            }

            foreach (var p in _additionalPlayers)
            {
                if (p != null && snap.UnitStates.TryGetValue(p.Stats.Name, out var extraSnap))
                {
                    RestoreUnitState(p, extraSnap);
                }
            }

            foreach (var d in SparringDummies)
            {
                if (d != null && snap.UnitStates.TryGetValue(d.Stats.Name, out var dSnap))
                {
                    RestoreUnitState(d, dSnap);
                }
            }
        }

        private void RestoreUnitState(TacticalUnit unit, UnitTimeSnapshot snap)
        {
            unit.Stats.CurrentHealth = snap.Health;
            unit.Stats.CurrentActionPoints = snap.ActionPoints;
            unit.Stats.Essoufflement = snap.Essoufflement;
            unit.Stats.ActiveStatus = snap.Status;

            var targetCoords = new HexCoordinates(snap.GridCoordQ, snap.GridCoordR);
            // Rembobinage : ne restaure la position que si elle est libre,
            // sinon relocalise à côté plutôt que d'empiler deux avatars.
            if (unit.CurrentCoords.Equals(targetCoords)) return;
            if (!unit.TeleportTo(targetCoords, _grid))
            {
                var fallback = ResolveFreeCoords(targetCoords);
                if (!fallback.Equals(targetCoords))
                {
                    unit.TeleportTo(fallback, _grid);
                    Log($"⏪ <b>{unit.Stats.Name}</b> relocalisé en ({fallback.Q}, {fallback.R}) : case d'origine occupée.");
                }
            }
        }

        public void RefillAPAll()
        {
            if (PlayerUnit != null) PlayerUnit.Stats.CurrentActionPoints = PlayerUnit.Stats.MaxActionPoints;
            foreach (var p in _additionalPlayers) if (p != null) p.Stats.CurrentActionPoints = p.Stats.MaxActionPoints;
            foreach (var d in SparringDummies) if (d != null) d.Stats.CurrentActionPoints = d.Stats.MaxActionPoints;
            if (KilltimeAudioManager.Instance != null) KilltimeAudioManager.Instance.PlayUI(SoundId.PA_Refill, 0.8f);
            Log("⚡ Points d'Action réinitialisés au maximum pour toutes les unités.");
        }

        public void HealAndCureAll()
        {
            if (PlayerUnit != null)
            {
                PlayerUnit.Stats.CurrentHealth = PlayerUnit.Stats.MaxHealth;
                PlayerUnit.Stats.ClearAllStatus();
                PlayerUnit.Stats.Essoufflement = 0;
            }
            foreach (var p in _additionalPlayers)
            {
                if (p != null)
                {
                    p.Stats.CurrentHealth = p.Stats.MaxHealth;
                    p.Stats.ClearAllStatus();
                    p.Stats.Essoufflement = 0;
                }
            }
            foreach (var d in SparringDummies)
            {
                if (d != null)
                {
                    d.Stats.CurrentHealth = d.Stats.MaxHealth;
                    d.Stats.ClearAllStatus();
                    d.Stats.Essoufflement = 0;
                }
            }
            Log("❤️ <b>Soin Intégral :</b> Tous les PV restaurés et statuts négatifs dissipés.");
            if (KilltimeAudioManager.Instance != null) KilltimeAudioManager.Instance.Play(SoundId.Heal, 0.85f);
        }

        public void KillCurrentTarget()
        {
            if (CurrentTarget != null)
            {
                CurrentTarget.Stats.CurrentHealth = 0;
                CurrentTarget.Stats.IsDead = true;
                CurrentTarget.Stats.ActiveStatus |= StatusEffect.Inconscient | StatusEffect.ATerre;
                var vis = CurrentTarget.GetComponent<TacticalUnitVisual>();
                vis?.TriggerFallingBackDeath();
                vis?.SpawnFloatingText("K.O. TOTAL!", Color.red);
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayAt(SoundId.Death_Instant, CurrentTarget.transform.position, 0.9f);
                Log($"💀 {CurrentTarget.Stats.Name} a été terrassé par commande développeur.");

                if (Killtime.Multi.VTTTableSync.Instance != null 
                    && Killtime.Multi.VTTRoomManager.Instance != null 
                    && Killtime.Multi.VTTRoomManager.Instance.InRoom 
                    && Killtime.Multi.VTTRoomManager.Instance.IsGM)
                {
                    Killtime.Multi.VTTTableSync.Instance.BroadcastFullCombatState();
                }
            }
        }

        public void RemoveUnit(TacticalUnit unit)
        {
            if (unit == null) return;

            string unitName = unit.Stats != null ? unit.Stats.Name : unit.name;

            CombatUI.TacticalSelectionManager.Instance?.ClearSelection();

            if (CurrentTarget == unit)
            {
                CurrentTarget = null;
            }

            if (PlayerUnit == unit)
            {
                PlayerUnit = null;
            }

            SparringDummies.Remove(unit);
            _additionalPlayers.Remove(unit);
            _customSpawns.RemoveAll(r => r != null && r.Sheet != null && r.Sheet.Name == unitName);

            _turnManager?.UnregisterUnit(unit);

            if (_grid != null)
            {
                var node = _grid.GetNode(unit.CurrentCoords);
                if (node != null)
                {
                    node.IsOccupied = false;
                }
            }

            if (CurrentTarget == null)
            {
                AutoTargetNextAlive();
            }

            Log($"🗑️ <b>{unitName}</b> a été retiré(e) du champ de bataille.");
            Destroy(unit.gameObject);
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
            _isAttackInProgress = false;
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
                var units = FindObjectsByType<TacticalUnit>();
                int alliesAlive = 0, enemiesAlive = 0;
                float hpSum = 0f, maxSum = 0f;
                foreach (var u in units)
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
            if (attacker == null || defender == null || _grid == null) return false;

            int dq = defender.CurrentCoords.Q - attacker.CurrentCoords.Q;
            int dr = defender.CurrentCoords.R - attacker.CurrentCoords.R;

            bool isTeepDeRupture = attacker.Stats != null && attacker.Stats.HasSpecialization("Teep de Rupture");
            bool isBedrock = attacker.Stats != null && attacker.Stats.HasSpecialization("Impact de Bedrock");
            int pushDistance = 1;
            if (isTeepDeRupture)
            {
                if (attacker.Stats.HasSpecialization("Teep de Rupture : Brise-Châssis Titan")) pushDistance = 4;
                else if (attacker.Stats.HasSpecialization("Teep de Rupture : Onde de Choc Linéaire")) pushDistance = 3;
                else pushDistance = 2;
            }
            else if (isBedrock)
            {
                pushDistance = 2;
            }

            HexCoordinates currentDest = defender.CurrentCoords;
            int tilesMoved = 0;

            for (int step = 1; step <= pushDistance; step++)
            {
                var nextCoords = new HexCoordinates(defender.CurrentCoords.Q + dq * step, defender.CurrentCoords.R + dr * step);
                var nextNode = _grid.GetNode(nextCoords);

                if (nextNode != null && nextNode.IsWalkable && !nextNode.IsOccupied)
                {
                    currentDest = nextCoords;
                    tilesMoved++;
                }
                else
                {
                    if (tilesMoved > 0)
                    {
                        defender.TeleportTo(currentDest, _grid);
                    }
                    var defVis = defender.GetComponent<TacticalUnitVisual>();
                    if (isTeepDeRupture)
                    {
                        bool isDevastating = attacker.Stats.HasSpecialization("Teep de Rupture : Impact Dévastateur");
                        int shockDamage = isDevastating ? 6 : 3;
                        defender.Stats.ApplyStatus(StatusEffect.Sonne, 1);
                        defender.Stats.ApplyStatus(StatusEffect.ATerre, 1);
                        if (isDevastating) defender.Stats.ApplyStatus(StatusEffect.Destabilise, 2);
                        defender.Stats.CurrentHealth = Math.Max(0, defender.Stats.CurrentHealth - shockDamage);
                        defVis?.TriggerHitFlash();
                        string shockLabel = isDevastating ? $"[TEEP DÉVASTATEUR] SONNÉ (-{shockDamage} PV) !" : $"[TEEP PAROI] SONNÉ (-{shockDamage} PV) !";
                        defVis?.SpawnFloatingText(shockLabel, Color.red);
                        Log($"💥 <b>TEEP DE RUPTURE (Collision)</b> : {defender.Stats.Name} est projeté(e) contre un obstacle, subit {shockDamage} dégâts de choc et [Sonné / À Terre] !");
                    }
                    else
                    {
                        defender.Stats.ApplyStatus(StatusEffect.ATerre, 1);
                        defender.Stats.ApplyStatus(StatusEffect.Destabilise, 1);
                        defVis?.SpawnFloatingText("[IMPACT PAROI] À TERRE !", Color.red);
                        Log($"💥 <b>IMPACT CONTRE OBSTACLE</b> : {defender.Stats.Name} heurte la paroi et s'écroule <b>À Terre</b> !");
                    }
                    return tilesMoved > 0;
                }
            }

            if (tilesMoved > 0)
            {
                defender.TeleportTo(currentDest, _grid);
                Killtime.Core.Combat.GrappleState.ReleaseGrapple(defender.Stats);
                var defVis = defender.GetComponent<TacticalUnitVisual>();
                string teepTag = isTeepDeRupture ? "[TEEP DE RUPTURE]" : (isBedrock ? "[IMPACT DE BEDROCK]" : "[REFOULEMENT]");
                defVis?.SpawnFloatingText($"{teepTag} Repoussé ({tilesMoved} cases) !", Color.yellow);
                Log($"💨 <b>{teepTag}</b> : {defender.Stats.Name} est repoussé(e) de {tilesMoved} case(s) en ({currentDest.Q}, {currentDest.R}) !");
                return true;
            }

            return false;
        }

        public TacticalUnit SpawnThomas(HexCoordinates coords, bool isPlayer = true)
        {
            var sheet = ThomasCharacter.BuildHeroicSheet();
            return SpawnCustomCharacter(sheet, coords, isPlayer);
        }

        public TacticalUnit SpawnJohn(HexCoordinates coords, bool isPlayer = true)
        {
            var sheet = JohnCharacter.BuildHeroicSheet();
            return SpawnCustomCharacter(sheet, coords, isPlayer);
        }

        public TacticalUnit SpawnErika(HexCoordinates coords, bool isPlayer = true)
        {
            var sheet = ErikaCharacter.BuildHeroicSheet();
            return SpawnCustomCharacter(sheet, coords, isPlayer);
        }

        public TacticalUnit SpawnVance(HexCoordinates coords, bool isPlayer = true)
        {
            var sheet = VanceCharacter.BuildHeroicSheet();
            return SpawnCustomCharacter(sheet, coords, isPlayer);
        }

        public void SpawnHeroicTrio(HexCoordinates centerCoords)
        {
            SpawnCustomCharacter(ThomasCharacter.BuildHeroicSheet(), centerCoords, true);
            SpawnCustomCharacter(MinaCharacter.BuildHeroicSheet(), centerCoords.GetNeighbor(0), true);
            SpawnCustomCharacter(LucasCharacter.BuildHeroicSheet(), centerCoords.GetNeighbor(3), true);
            Log("⚡ <b>Trio Tri-Fusion déployé</b> : Thomas-0, Mina-0 et Lucas-0 sur la grille.");
        }

        public void SpawnHeroicQuatuor(HexCoordinates centerCoords)
        {
            SpawnHeroicTrio(centerCoords);
            SpawnCustomCharacter(JohnCharacter.BuildHeroicSheet(), centerCoords.GetNeighbor(1), true);
            Log("🛰️ <b>Quatuor déployé</b> : Trio Tri-Fusion + John (Passeur) sur la grille.");
        }

        public void SpawnHeroicQuintet(HexCoordinates centerCoords)
        {
            SpawnHeroicQuatuor(centerCoords);
            SpawnCustomCharacter(ErikaCharacter.BuildHeroicSheet(), centerCoords.GetNeighbor(2), true);
            Log("🔥 <b>Quintette déployé</b> : Quatuor + Erika de Cleya (Flamme Cleyane) sur la grille.");
        }

        public void SpawnHeroicSextet(HexCoordinates centerCoords)
        {
            SpawnHeroicQuintet(centerCoords);
            SpawnCustomCharacter(VanceCharacter.BuildHeroicSheet(), centerCoords.GetNeighbor(4), true);
            Log("🛡️ <b>Sextette déployé</b> : Quintette + Commandant Vance (Autorité du Commandant) sur la grille.");
        }

        public void Log(string message)
        {
            OnCombatLogMessage?.Invoke($"[{DateTime.Now:HH:mm:ss}] {message}");
        }

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

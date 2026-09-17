using System;
using System.Collections;
using System.Collections.Generic;
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

        [Header("Configuration")]
        [SerializeField] private ArenaLayoutType _initialLayout = ArenaLayoutType.TacticalBarricades;
        [SerializeField] private bool _enableCinematicKillcam = true;

        public TacticalUnit PlayerUnit { get; private set; }
        public List<TacticalUnit> SparringDummies { get; private set; } = new();
        public TacticalUnit CurrentTarget { get; private set; }

        private readonly List<TacticalUnit> _additionalPlayers = new();
        private readonly List<CustomSpawnRecord> _customSpawns = new();

        public IReadOnlyList<TacticalUnit> AdditionalPlayers => _additionalPlayers;
        public IReadOnlyList<CustomSpawnRecord> CustomSpawns => _customSpawns;

        public bool InfiniteAP { get; set; } = false;

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

        private void Awake()
        {
            _diceRoller = new DiceRoller();
            _combatCalculator = new CombatCalculator(_diceRoller);
            _timelineBranch = new TimelineBranch(TimelineId.Timeline0_Prime);

            EnsureDependencies();
            EnsureAIController();
            try { LoadDevUIPrefs(); } catch { /* prefs optionnelles */ }
        }

        private void LoadDevUIPrefs()
        {
            var p = Killtime.UI.DevUIPreferences.Current;
            if (p == null) return;
            InfiniteAP = p.InfiniteAP;
            int layoutCount = Enum.GetValues(typeof(ArenaLayoutType)).Length;
            int savedLayout = Mathf.Clamp(p.ArenaLayout, 0, Mathf.Max(0, layoutCount - 1));
            _initialLayout = (ArenaLayoutType)savedLayout;
            _enableCinematicKillcam = p.EnableCinematicKillcam;
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

            // 1. Initialiser le layout d'obstacles
            ApplyArenaLayout(_initialLayout);

            // 2. Générer les unités
            SetupArenaUnits();

            // 3. Liaison avec le TurnManager
            _turnManager.OnTurnStarted += HandleTurnStarted;
            _turnManager.OnCombatEnded += HandleCombatEnded;
            _turnManager.OnUnitStatusExpired += HandleUnitStatusExpired;
            _turnManager.OnInitiativeRolled += HandleInitiativeRolled;

            TacticalUnit.OnAnyUnitMoved += HandleTargetMovedFollow;
            TacticalUnit.OnAnyUnitTeleported += HandleTargetTeleportedFollow;

            // 4. Sélectionner le premier mannequin par défaut
            if (SparringDummies.Count > 0)
            {
                SelectTarget(SparringDummies[0]);
            }

            // 5. Enregistrer le snapshot initial du Fleuve du Temps
            RecordChronoSnapshot("Début de l'affrontement (Temps t0)");

            if (KilltimeAudioManager.Instance != null)
            {
                _adaptiveTurnCount = 0;
                KilltimeAudioManager.Instance.PlayMusic(MusicMood.Combat, 0.5f, true);
                KilltimeAudioManager.Instance.Play(SoundId.Round_Start, 0.8f);
                UpdateAdaptiveMusic();
            }
            Log("⚔️ <b>Arène de Combat Killtime Initialisée !</b> Prête pour les tests.");
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
            HandleMouseInteraction();
            UpdateReachableHighlights();

            if (InfiniteAP && _turnManager.ActiveUnit != null)
            {
                _turnManager.ActiveUnit.Stats.CurrentActionPoints = _turnManager.ActiveUnit.Stats.MaxActionPoints;
            }
        }

        private void SetupArenaUnits()
        {
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
            _cinematicDirector?.ResetCinematicState();
            _turnManager.StartNewRound();
            _aiController?.TriggerAITurnIfApplicable();
        }

        public TacticalUnit SpawnCustomCharacter(CharacterSheet sheet, HexCoordinates coords, bool isPlayer)
        {
            if (sheet == null || _grid == null) return null;

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
            _cinematicDirector?.ResetCinematicState();

            CombatUI.CombatContextMenuUI.Instance?.CloseMenu();
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

            if (_cameraController != null)
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
            _cinematicDirector?.ResetCinematicState();

            bool isRemoteClient = TurnManager.IsMultiplayerPlayerClient()
                || (Killtime.Multi.VTTTableSync.Instance != null && Killtime.Multi.VTTTableSync.Instance.IsApplyingRemoteAction);

            if (!isRemoteClient)
            {
                _turnManager?.StartNewRound();
                _aiController?.TriggerAITurnIfApplicable();
            }

            RecordChronoSnapshot($"Chargement des unités de la carte ({reserved.Count} avatars)");
            string relocateInfo = relocated > 0 ? $" ({relocated} relocalisé(s) anti-empilement)" : "";
            Log($"👥 <b>{reserved.Count} avatar(s)</b> chargé(s) depuis la configuration de carte{relocateInfo}.");
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
                    _grid.SetNodeCover(new HexCoordinates(1, 0), CoverType.Half, true);
                    _grid.SetNodeCover(new HexCoordinates(1, -2), CoverType.Full, false);
                    _grid.SetNodeCover(new HexCoordinates(-1, 2), CoverType.Half, true);
                    _grid.SetNodeCover(new HexCoordinates(-2, 0), CoverType.Full, false);
                    _grid.SetNodeCover(new HexCoordinates(2, 1), CoverType.Half, true);
                    _grid.SetNodeCover(new HexCoordinates(-1, -2), CoverType.Half, true);
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
            if (_cameraController != null && unit != null)
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
                Log($"--- Tour de 10s : <b>{unit.Stats.Name}</b> (PA: {unit.Stats.CurrentActionPoints}/{unit.Stats.MaxActionPoints}) ---");
                RecordChronoSnapshot($"Début du tour de {unit.Stats.Name}");
            }
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
            if (CombatHUD.IsPaused) return;
            if (UnityEngine.Camera.main == null || _gridVisualizer == null) return;

            // Souris sur fenêtre flottante/HUD : interaction exclusive avec l'UI, pas la carte 3D.
            // Évite clics fantômes (mouvement/sélection) et nettoie les survols derrière la fenêtre.
            if (Killtime.UI.FloatingWindowChrome.IsPointerOverAnyWindow())
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

                        if (Input.GetMouseButtonDown(0))
                        {
                            Killtime.UI.FloatingWindowChrome.OnMapClicked();
                            var clickedUnit = GetUnitAtCoords(target);

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
                                bool inExploration = _turnManager != null && _turnManager.IsInExploration;
                                int availableAP = inExploration ? 99 : activeUnit.Stats.CurrentActionPoints;
                                var path = _pathfinder.FindPath(start, target, availableAP, out int apCost);

                                if (path.Count > 0)
                                {
                                    int finalCost = inExploration ? 0 : apCost;
                                    if (!inExploration)
                                    {
                                        int remainingAP = activeUnit.Stats.CurrentActionPoints - apCost;
                                        Log($"🚶 <b>{activeUnit.Stats.Name}</b> avance de {path.Count - 1} case(s) vers ({target.Q}, {target.R}) [Coût: -{apCost} PA | Restant: {remainingAP} PA]");
                                        activeUnit.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText($"Déplacement (-{apCost} PA)", new Color(0.2f, 0.85f, 1.0f));
                                    }
                                    else
                                    {
                                        activeUnit.Stats.CurrentActionPoints = activeUnit.Stats.MaxActionPoints;
                                    }

                                    _gridVisualizer.ClearPathPreview();
                                    if (KilltimeAudioManager.Instance != null)
                                        KilltimeAudioManager.Instance.PlayAt(SoundId.Move_Dash, activeUnit.transform.position, 0.5f);
                                    StartCoroutine(activeUnit.MoveAlongPath(path, _grid, finalCost));
                                }
                            }
                        }
                        else
                        {
                            // Aperçu du chemin au survol
                            if (!start.Equals(target) && hoveredNode.IsWalkable && !hoveredNode.IsOccupied)
                            {
                                var path = _pathfinder.FindPath(start, target, activeUnit.Stats.CurrentActionPoints, out _);
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
                var reachable = _pathfinder.GetReachableCoordinates(active.CurrentCoords, active.Stats.CurrentActionPoints);
                _gridVisualizer.SetReachableCoords(reachable);
            }
            else if (_gridVisualizer != null)
            {
                _gridVisualizer.SetReachableCoords(null);
            }
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
                if (PlayerUnit != null && PlayerUnit.Stats != null && PlayerUnit.Stats.IsAlive)
                {
                    SelectTarget(PlayerUnit);
                    return;
                }
                for (int i = 0; i < _additionalPlayers.Count; i++)
                {
                    var p = _additionalPlayers[i];
                    if (p != null && p.Stats != null && p.Stats.IsAlive)
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
        }

        private void HandleTargetTeleportedFollow(TacticalUnit unit, HexCoordinates coords)
        {
            if (CurrentTarget != null && CurrentTarget == unit && _gridVisualizer != null)
            {
                _gridVisualizer.SetTargetCoord(coords);
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

        public void AutoTargetNextAlive()
        {
            for (int i = 0; i < SparringDummies.Count; i++)
            {
                var dummy = SparringDummies[i];
                if (dummy != null && dummy.Stats != null && dummy.Stats.IsAlive)
                {
                    SelectTarget(dummy);
                    return;
                }
            }
        }

        /// <summary>
        /// Déclenche une passe d'armes ou tir ciblé selon la séquence officielle du Livre VI §24.1 :
        /// attaque (jet puis PA bonus après tirage) -> défense (jet puis PA bonus après tirage)
        /// -> résolution normale. Conforme à l'Axiome Fondateur : résolution par compétence.
        /// defenderBonusAP &lt; 0 = défense réactive auto (besoin réel après révélation de l'attaque).
        /// </summary>
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
            DiceType? defenseDie = null)
        {
            if (_isAttackInProgress)
            {
                Log("⚠️ Attaque ignorée : une résolution est déjà en cours (anti double-clic).");
                return;
            }

            var attacker = _turnManager.ActiveUnit;
            var defender = CurrentTarget;

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
                        attackerBonusAP
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

            DiceType resolvedAttackDie = attackDie ?? attacker.Stats.GetSkillDie(attackSkill, true);
            DiceType resolvedDefenseDie = defenseDie ?? defender.Stats.GetSkillDie(defenseSkill);

            int maxAttacks = attacker.Stats.GetMaxAttacksAllowed(resolvedAttackDie);
            if (!attacker.Stats.CanAttack(resolvedAttackDie))
            {
                Log($"⚠️ <b>{attacker.Stats.Name}</b> a déjà épuisé son quota d'attaque ce tour ({attacker.Stats.AttacksThisTurn}/{maxAttacks}) ! Requis pour 2 attaques : palier 2d6, 2d8, 2d10 ou 2d12.");
                return;
            }

            // Séquence officielle Livre VI §24.1 : les PA bonus s'injectent APRÈS les tirages.
            // Bonus explicite (>= 0) : appliqué post-tirage dans le calculateur.
            // Bonus auto (< 0) : défense réactive — calculée APRÈS révélation du total final
            // attaquant et du brut défensif (besoin = attaque finale - défense brute + 1).
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
            bool isMeleeStrike = (attackSkill == SkillType.MainsNues || attackSkill == SkillType.ManiementArmes || attackSkill == SkillType.ArmesContondantes || attackSkill == SkillType.ArmesPercantes);

            Action triggerKickAction = () =>
            {
                if (isMeleeStrike)
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
                if (defender != null && defender.Stats != null && defender.Stats.CanDefendActively())
                {
                    defVisual?.TriggerBodyBlock();
                    if (KilltimeAudioManager.Instance != null)
                        KilltimeAudioManager.Instance.PlayAt(SoundId.Defense_Block, defender.transform.position, 0.5f);
                }
            };

            Action resolveAction = () =>
            {
                attacker.Stats.RegisterAttack();

                int effectiveWeaponDamage = weaponBaseDamage;
                var equippedWeapon = attacker.Sheet?.GetEquippedWeapon();
                if (equippedWeapon != null && equippedWeapon.BaseDamage > 0)
                {
                    effectiveWeaponDamage = equippedWeapon.BaseDamage;
                }

                // Règle Livre VI §26.2 : le malus Canon Entravé dépend de la présence d'UN
                // ennemi au contact de l'attaquant (IsCanonEntrave), pas de la distance
                // à la cible visée. Un tir de portée sur cible lointaine reste à -2
                // tant qu'un adversaire conscient colle l'attaquant.
                _combatCalculator.SetContactDistanceState(attacker.IsCanonEntrave());

                DamageResult result;
                if (autoReactiveDefense)
                {
                    // Duel séquentiel réactif : attaque -> PA attaquant post-tirage ->
                    // défense -> PA défenseur post-tirage (besoin réel) -> résolution normale.
                    AttackDuel duel = null;
                    string duelError = null;
                    if (attackDie.HasValue || defenseDie.HasValue)
                    {
                        // Applique la règle non-exclusive sur le dé défensif explicite si besoin.
                        DiceType reactiveDefDie = resolvedDefenseDie;
                        if (!SkillDefinitions.IsExclusivelyDefensive(defenseSkill))
                        {
                            bool hasDefSpec = (!string.IsNullOrEmpty(defenderSpecialization) && defender.Stats.HasSpecialization(defenderSpecialization))
                                            || SkillDefinitions.HasDefensiveSpecialization(defender.Stats.Sheet, defenseSkill);
                            // Ne rétrograde que si le dé a été dérivé de la compétence (pas de dé forcé custom).
                            if (!defenseDie.HasValue && !hasDefSpec)
                                reactiveDefDie = SkillDefinitions.StepDownDie(reactiveDefDie);
                        }
                        duel = _combatCalculator.BeginDuel(
                            attacker.Stats, defender.Stats, targetedPart,
                            resolvedAttackDie, attacker.Stats.GetSkillModifier(attackSkill, isOffensive: true),
                            reactiveDefDie, defender.Stats.GetSkillModifier(defenseSkill, isOffensive: false),
                            effectiveWeaponDamage, cancelPenaltyWithAP, defenderWantsToDefend,
                            defender.Stats.BaseArmorAbsorption, attackSkill, defenseSkill, out duelError);
                    }
                    else
                    {
                        duel = _combatCalculator.BeginSkillDuel(
                            attacker.Stats, defender.Stats, targetedPart,
                            attackSkill, defenseSkill, weaponBaseDamage, cancelPenaltyWithAP,
                            defenderWantsToDefend, attackerSpecialization, defenderSpecialization,
                            defender.Stats.BaseArmorAbsorption, out duelError);
                    }
                    if (duel == null)
                    {
                        // Échec du coût de base (PA insuffisants) : BeginDuel n'a rien débité.
                        Log(duelError ?? "⚠️ Attaque impossible : PA insuffisants pour le coût de base.");
                        return;
                    }
                    else
                    {
                        _combatCalculator.RollAttackerRaw(duel);
                        _combatCalculator.AddAttackerBonusPA(duel, attackerBonusAP);
                        _combatCalculator.RollDefenderRaw(duel);
                        int reactiveBonus = _combatCalculator.ComputeReactiveDefenseBonus(duel);
                        _combatCalculator.AddDefenderBonusPA(duel, reactiveBonus);
                        result = _combatCalculator.FinishDuel(duel);
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
                        defenseSkill: defenseSkill
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
                        weaponBaseDamage: weaponBaseDamage,
                        cancelPenaltyWithAP: cancelPenaltyWithAP,
                        defenderWantsToDefend: defenderWantsToDefend,
                        attackerSpecialization: attackerSpecialization,
                        defenderSpecialization: defenderSpecialization,
                        defenderArmor: defender.Stats.BaseArmorAbsorption,
                        attackerBonusAP: attackerBonusAP,
                        defenderBonusAP: resolvedDefenderBonus
                    );
                }

                Log(result.CombatLog);

                var laserWeapon = attacker.GetComponentInChildren<LaserRifleWeapon>();
                // Un roundkick / une frappe au corps-à-corps (Mains Nues, Maniement, Contondantes,
                // Perçantes) ne doit jamais déclencher le rayon laser + sons de tir, même si un
                // laser est équipé visuellement. Seul un vrai tir (Ballistique) tire au laser.
                if (attackSkill == SkillType.Ballistique && !isMeleeStrike && laserWeapon != null && defender != null)
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
                    KilltimeAudioManager.Instance.PlayCombatResult(
                        result.IsHit, result.IsBlocked, result.IsCritical,
                        result.ArmorAbsorbed > 0, result.ExceededEncaissement,
                        result.FatalResolution.ToString(), defender.transform.position);
                    KilltimeAudioManager.Instance.Play(SoundId.Dice_Roll, 0.35f);
                }

                // Affichage du texte flottant sur l'attaquant à l'impact exact
                if (attVisual != null)
                {
                    int totalCost = (cancelPenaltyWithAP ? 3 : 2) + attackerBonusAP;
                    attVisual.SpawnFloatingText($"Attaque {targetedPart} (-{totalCost} PA)", new Color(0.3f, 0.8f, 1.0f));
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

                        if (result.WasDeflected)
                        {
                            defVisual.SpawnFloatingText($"[DÉVIATION] -> {BodyPartInfo.GetInfo(result.ActualHitPart).DisplayName} (Diff 0)", Color.yellow);
                        }
                        else
                        {
                            string diffSign = result.Differential > 0 ? $"+{result.Differential}" : $"{result.Differential}";
                            defVisual.SpawnFloatingText($"[TOUCHÉ] {BodyPartInfo.GetInfo(result.ActualHitPart).DisplayName} (Diff {diffSign})", new Color(0.9f, 0.9f, 1.0f));
                        }

                        string dmgText = result.ArmorAbsorbed > 0 
                            ? $"-{result.FinalDamageApplied} PV (Bruts {result.RawDamage} | Armure -{result.ArmorAbsorbed})" 
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
                        isMeleeStrike = isMeleeStrike,
                        attackerCostAP = (cancelPenaltyWithAP ? 3 : 2) + attackerBonusAP,
                        defenderCostAP = resolvedDefenderBonus,
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

                if (!defender.Stats.IsAlive)
                {
                    AutoTargetNextAlive();
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

            // Débit PA de base puis bonus d'injection (plafonné au reste, comme les passes d'armes).
            if (!InfiniteAP)
            {
                attacker.Stats.ConsumeActionPoints(baseCost);
                int applied = 0;
                for (int i = 0; i < bonusWant; i++)
                {
                    if (attacker.Stats.ConsumeActionPoints(1)) applied++;
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
                    if (node.Cover == CoverType.Full) cover = 2;
                    else if (node.Cover == CoverType.Half) cover = isMortar ? 0 : 1;
                }
                var hit = calc.ResolveHitOnTarget(u.Stats, grenadeDef, diceTotal, d, cover, d == 0);
                hits.Add(hit);
                totalDealt += hit.FinalDamage;
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

            // --- Retours visuels/sonores par cible ---
            for (int i = 0; i < hits.Count; i++)
            {
                var h = hits[i];
                var unit = FindUnitByName(h.TargetName);
                string allyTag = "";
                if (unit != null && unit != attacker && unit.IsPlayerControlled == attacker.IsPlayerControlled)
                    allyTag = " ⚠️<b>TIR ALLIÉ</b>";
                string hpTag = unit != null ? unit.Stats.CurrentHealth + "/" + unit.Stats.MaxHealth + " PV" : "?";
                log += $"\n   • <b>{h.TargetName}</b>{allyTag} à {h.DistanceFromBlast} case(s) : bruts {h.RawDamage} − couv {h.CoverReduction} − armure {h.ArmorAbsorbed} ➔ <color=#FF3B5C><b>{h.FinalDamage} PV</b></color> ({hpTag})";
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
                    h.CoverReduction + h.ArmorAbsorbed,
                    h.FinalDamage,
                    h.ExceededEncaissement,
                    isGrenadeFatal,
                    0
                );

                if (h.FinalDamage > 0)
                {
                    vis?.TriggerHitFlash();
                    vis?.SpawnFloatingText($"-{h.FinalDamage} PV (💣)", new Color(1f, 0.30f, 0.20f));
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
                var vis = defender.GetComponent<TacticalUnitVisual>();
                vis?.SpawnFloatingText("[SOUFFLE] Projeté !", Color.yellow);
                Log($"💨 <b>SOUFFLE</b> : {defender.Stats.Name} est projeté(e) en ({best.Q}, {best.R}) !");
                return true;
            }
            defender.Stats.ApplyStatus(StatusEffect.Destabilise, 1);
            return false;
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
                    bool alive = u.Stats.IsAlive;
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

            var targetCoords = new HexCoordinates(defender.CurrentCoords.Q + dq, defender.CurrentCoords.R + dr);
            var node = _grid.GetNode(targetCoords);

            if (node != null && node.IsWalkable && !node.IsOccupied)
            {
                defender.TeleportTo(targetCoords, _grid);
                var defVis = defender.GetComponent<TacticalUnitVisual>();
                defVis?.SpawnFloatingText("[REFOULEMENT] Repoussé !", Color.yellow);
                Log($"💨 <b>REFOULEMENT (Push-Kick)</b> : {defender.Stats.Name} est repoussé(e) en ({targetCoords.Q}, {targetCoords.R}) !");
                return true;
            }
            else
            {
                defender.Stats.ApplyStatus(StatusEffect.Destabilise, 1);
                var defVis = defender.GetComponent<TacticalUnitVisual>();
                defVis?.SpawnFloatingText("[IMPACT PAROI] Déstabilisé !", Color.red);
                Log($"💥 <b>IMPACT CONTRE OBSTACLE</b> : {defender.Stats.Name} heurte la paroi et subit [Déstabilisé] !");
                return false;
            }
        }

        public void Log(string message)
        {
            OnCombatLogMessage?.Invoke($"[{DateTime.Now:HH:mm:ss}] {message}");
        }

        private void OnDisable()
        {
            TacticalUnit.OnAnyUnitMoved -= HandleTargetMovedFollow;
            TacticalUnit.OnAnyUnitTeleported -= HandleTargetTeleportedFollow;
        }
    }
}

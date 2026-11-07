using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Audio;
using Killtime.Core.Character;
using Killtime.Core.Inventory;
using Killtime.Tactics.AI;
using Killtime.UI;

namespace Killtime.Tactics.Arena
{
    /// <summary>
    /// Création, spawn, chargement et suppression des unités de l'arène.
    /// Logique déplacée verbatim depuis CombatDevArena (SetupArenaUnits,
    /// SpawnCustomCharacter, ClearAllUnits, LoadUnitsFromMap, CreateUnit,
    /// ApplyArenaLayout, Register*, FindUnitByName, spawns héroïques).
    /// Opère sur ArenaState (mêmes listes que la façade) via ArenaContext.
    /// Callbacks vers la façade : SnapshotRecorder (chrono), PersistLayoutPref.
    /// </summary>
    public sealed class ArenaUnitStore
    {
        private readonly ArenaContext _ctx;
        private ArenaState _state => _ctx.State;
        private IArenaHost _host => _ctx.Host;

        /// <summary>Enregistre un snapshot chrono (fourni par la façade).</summary>
        public Action<string> SnapshotRecorder;

        /// <summary>Persiste les prefs de layout (fourni par la façade).</summary>
        public Action PersistLayoutPref;

        public ArenaUnitStore(ArenaContext ctx)
        {
            _ctx = ctx;
        }

        public void SetupArenaUnits()
        {
            var grid = _ctx.Grid;
            var visualizer = _ctx.Visualizer;
            var turnManager = _ctx.TurnManager;
            // 0. Combat (ré)initialisé : aucun gourdin / objet du combat précédent ne survit.
            DroppedWeaponPickup.ClearAllDropped();
            _state.SmokeZones.Clear();
            visualizer?.UpdatePersistentZones(_state.SmokeZones);
            CombatUI.DroppedWeaponContextMenuUI.Instance?.CloseMenu();
            // 1. Purger et neutraliser immédiatement les instances actives (anti-fantômes même frame)
            if (_state.PlayerUnit != null)
            {
                _state.PlayerUnit.gameObject.SetActive(false);
                _state.PlayerUnit.gameObject.name = "[Disposed]";
                UnityEngine.Object.Destroy(_state.PlayerUnit.gameObject);
                _state.PlayerUnit = null;
            }

            for (int i = 0; i < _state.SparringDummies.Count; i++)
            {
                var d = _state.SparringDummies[i];
                if (d != null)
                {
                    d.gameObject.SetActive(false);
                    d.gameObject.name = "[Disposed]";
                    UnityEngine.Object.Destroy(d.gameObject);
                }
            }
            _state.SparringDummies.Clear();

            for (int i = 0; i < _state.AdditionalPlayers.Count; i++)
            {
                var p = _state.AdditionalPlayers[i];
                if (p != null)
                {
                    p.gameObject.SetActive(false);
                    p.gameObject.name = "[Disposed]";
                    UnityEngine.Object.Destroy(p.gameObject);
                }
            }
            _state.AdditionalPlayers.Clear();

            grid?.ClearOccupancy();

            // Suspendre le démarrage auto du TurnManager pendant l'enregistrement en masse :
            // sinon le 1er RegisterUnit déclenche StartNewRound (et l'IA en FullAuto),
            // puis le StartNewRound final coupe cette IA en plein vol et laisse
            // le guard / cinématique bloqués. Un seul démarrage à la fin.
            bool prevSuspend = turnManager != null && turnManager.SuspendAutoStart;
            if (turnManager != null) turnManager.SuspendAutoStart = true;
            try
            {
            // 2. Unités de base de l'arène
            _state.PlayerUnit = CreateUnit("Roger (Opératif)", new HexCoordinates(0, 0),
                new Attributes(4, 3, 4, 4, 3, 2),
                baseArmor: 1, isPlayer: true);
            _state.PlayerUnit.Sheet.GetSkill(SkillType.Ballistique).TrainingLevel = 1;
            _state.PlayerUnit.Sheet.GetSkill(SkillType.ManiementArmes).TrainingLevel = 1;
            _state.PlayerUnit.Sheet.GetSkill(SkillType.Esquive).TrainingLevel = 1;
            _state.PlayerUnit.Sheet.UnlockedSpecializations.Add("Maniement de l'Épée");
            _state.PlayerUnit.Sheet.UnlockedSpecializations.Add("Pistolet & Tir Rapide");

            var dummyTank = CreateUnit("Sac de Frappe (Tank)", new HexCoordinates(3, -1),
                new Attributes(1, 1, 1, 6, 4, 1),
                baseArmor: 2, isPlayer: false);
            dummyTank.Sheet.GetSkill(SkillType.DefenseCorporelle).TrainingLevel = 1;
            dummyTank.Sheet.UnlockedSpecializations.Add("Bloquer");
            _state.SparringDummies.Add(dummyTank);

            var dummyAgile = CreateUnit("Duelliste Agile (Esquive)", new HexCoordinates(2, 2),
                new Attributes(6, 3, 5, 3, 2, 2),
                baseArmor: 0, isPlayer: false);
            dummyAgile.Sheet.GetSkill(SkillType.Esquive).TrainingLevel = 2;
            _state.SparringDummies.Add(dummyAgile);

            var dummyArmored = CreateUnit("Garde Blindé (Armure)", new HexCoordinates(-3, 2),
                new Attributes(2, 2, 2, 5, 4, 1),
                baseArmor: 4, isPlayer: false);
            dummyArmored.Sheet.GetSkill(SkillType.DefenseCorporelle).TrainingLevel = 2;
            dummyArmored.Sheet.UnlockedSpecializations.Add("Bloquer");
            _state.SparringDummies.Add(dummyArmored);

            turnManager.RegisterUnit(_state.PlayerUnit);
            _state.PlayerUnit.GetComponent<TacticalUnitVisual>()?.SetCombatStance(true);

            foreach (var d in _state.SparringDummies)
            {
                turnManager.RegisterUnit(d);
                d.GetComponent<TacticalUnitVisual>()?.SetCombatStance(true);
            }

            // 3. Recréer et réintégrer toutes les unités personnalisées spawnées
            // (relocalisées si leur case d'origine est désormais occupée : jamais d'empilement).
            for (int i = 0; i < _state.CustomSpawns.Count; i++)
            {
                var record = _state.CustomSpawns[i];
                if (record != null && record.Sheet != null)
                {
                    var finalCoords = ResolveFreeCoords(record.SpawnCoords);
                    var go = new GameObject($"Unit_{record.Sheet.Name.Replace(" ", "_")}");
                    var customUnit = go.AddComponent<TacticalUnit>();
                    customUnit.InitializeFromSheet(record.Sheet, finalCoords, grid, record.IsPlayer);
                    customUnit.GetComponent<TacticalUnitVisual>()?.SetCombatStance(true);

                    if (record.IsPlayer)
                    {
                        _state.AdditionalPlayers.Add(customUnit);
                    }
                    else
                    {
                        _state.SparringDummies.Add(customUnit);
                    }

                    turnManager.RegisterUnit(customUnit);
                }
            }
            }
            finally
            {
                if (turnManager != null) turnManager.SuspendAutoStart = prevSuspend;
            }

            // 4. Démarre le premier tour pour assigner l'unité active
            // État verrouillé avant démarrage : aucune attaque/cinématique orpheline.
            _ctx.Guard.ForceReset();
            _ctx.AI?.StopAITurn();
            if (!_ctx.IsStoryScene()) _ctx.Cinematic?.ResetCinematicState();
            if (_ctx.GetEnterCombatOnMapLoad()) turnManager?.EnterCombatMode(_state.PlayerUnit);
            turnManager?.StartNewRound();
            _ctx.AI?.TriggerAITurnIfApplicable();
        }

        public TacticalUnit SpawnCustomCharacter(CharacterSheet sheet, HexCoordinates coords, bool isPlayer)
        {
            var grid = _ctx.Grid;
            if (sheet == null || grid == null) return null;

            // Scène 01 : aucun objet hors-catalogue — migre les vieilles dotations
            // (ex. Pistolet Balistique Lourd) vers leurs équivalents Armurerie/Marché.
            ArmoryCatalog.MigrateLegacyScene01Items(sheet);

            var spawnNode = grid.GetNode(coords);
            if (spawnNode == null || !spawnNode.IsWalkable)
            {
                _host.ArenaLog($"⚠️ Déploiement refusé : case ({coords.Q}, {coords.R}) inexistante ou impraticable.");
                return null;
            }

            // 1 case = 1 avatar : relocalise au lieu d'empiler.
            var finalCoords = ResolveFreeCoords(coords);
            var finalNode = grid.GetNode(finalCoords);
            if (finalNode == null || !finalNode.IsWalkable || finalNode.IsOccupied)
            {
                _host.ArenaLog($"⚠️ Déploiement refusé : aucune case libre autour de ({coords.Q}, {coords.R}).");
                return null;
            }

            var record = new CombatDevArena.CustomSpawnRecord
            {
                Sheet = sheet,
                SpawnCoords = finalCoords,
                IsPlayer = isPlayer
            };
            _state.CustomSpawns.Add(record);

            var go = new GameObject($"Unit_{sheet.Name.Replace(" ", "_")}");
            var unit = go.AddComponent<TacticalUnit>();
            unit.SetFootprint(sheet.Footprint);
            unit.InitializeFromSheet(sheet, finalCoords, grid, isPlayer);
            unit.GetComponent<TacticalUnitVisual>()?.SetCombatStance(true);

            if (isPlayer)
            {
                _state.AdditionalPlayers.Add(unit);
            }
            else
            {
                _state.SparringDummies.Add(unit);
                if (_state.CurrentTarget == null || !_state.CurrentTarget.Stats.IsAlive)
                {
                    _host.RequestSelectTarget(unit);
                }
            }

            _ctx.TurnManager?.RegisterUnit(unit);
            SnapshotRecorder?.Invoke($"Apparition de {unit.Stats.Name} en ({finalCoords.Q}, {finalCoords.R})");
            if (KilltimeAudioManager.Instance != null)
                KilltimeAudioManager.Instance.PlayAt(SoundId.Spawn_Deploy, unit.transform.position, 0.85f);
            if (!finalCoords.Equals(coords))
                _host.ArenaLog($"⚡ Unité persistante déployée : <b>{sheet.Name}</b> en ({finalCoords.Q}, {finalCoords.R}) (demandé {coords.Q}, {coords.R} occupé).");
            else
                _host.ArenaLog($"⚡ Unité persistante déployée : <b>{sheet.Name}</b> en ({coords.Q}, {coords.R})");

            return unit;
        }

        /// <summary>
        /// Résout une case libre : la case demandée si libre, sinon la plus proche.
        /// Garantit 1 case = 1 avatar (jamais d'empilement).
        /// </summary>
        public HexCoordinates ResolveFreeCoords(HexCoordinates desired, HashSet<HexCoordinates> extraReserved = null)
        {
            var grid = _ctx.Grid;
            if (grid == null) return desired;
            if (grid.IsCellFree(desired) && (extraReserved == null || !extraReserved.Contains(desired)))
            {
                return desired;
            }
            if (grid.TryFindNearestFreeCell(desired, out var free, 8, extraReserved))
            {
                return free;
            }
            return desired;
        }

        public void ClearCustomSpawns()
        {
            _state.CustomSpawns.Clear();
        }

        public void ClearAllUnits()
        {
            var grid = _ctx.Grid;
            var visualizer = _ctx.Visualizer;
            var turnManager = _ctx.TurnManager;
            _host.RequestStopAllCoroutines();
            _ctx.Guard.ForceReset();
            _ctx.AI?.StopAITurn();
            if (!_ctx.IsStoryScene()) _ctx.Cinematic?.ResetCinematicState();

            CombatUI.CombatContextMenuUI.Instance?.CloseMenu();
            CombatUI.DroppedWeaponContextMenuUI.Instance?.CloseMenu();
            DroppedWeaponPickup.ClearAllDropped();
            _state.SmokeZones.Clear();
            visualizer?.UpdatePersistentZones(_state.SmokeZones);
            CombatUI.TacticalSelectionManager.Instance?.ClearSelection();

            // Capture le snapshot avant de vider le registre, sinon la boucle
            // de destruction itérerait sur une liste vide.
            var allUnits = TacticalUnitRegistry.AllUnits;
            TacticalUnitRegistry.Clear();
            for (int i = 0; i < allUnits.Count; i++)
            {
                var u = allUnits[i];
                if (u != null)
                {
                    if (grid != null)
                    {
                        var node = grid.GetNode(u.CurrentCoords);
                        if (node != null) node.IsOccupied = false;
                    }
                    u.gameObject.SetActive(false);
                    u.gameObject.name = "[Disposed]";
                    UnityEngine.Object.Destroy(u.gameObject);
                }
            }

            _state.PlayerUnit = null;
            _state.SparringDummies.Clear();
            _state.AdditionalPlayers.Clear();
            _state.CustomSpawns.Clear();
            _state.CurrentTarget = null;

            turnManager?.ClearUnits();
            turnManager?.ResetCombatState();
            grid?.ClearOccupancy();
        }

        public void LoadUnitsFromMap(List<MapUnitData> mapUnits)
        {
            var grid = _ctx.Grid;
            var turnManager = _ctx.TurnManager;
            ClearAllUnits();
            // ClearAllUnits détruit en différé : on repart d'une occupation vierge.
            grid?.ClearOccupancy();

            if (mapUnits == null || mapUnits.Count == 0)
            {
                SnapshotRecorder?.Invoke("Chargement de carte (0 avatar)");
                _host.ArenaLog("👥 Aucun avatar défini sur cette carte.");
                return;
            }

            var reserved = new HashSet<HexCoordinates>();
            int relocated = 0;
            bool prevSuspendLoad = turnManager != null && turnManager.SuspendAutoStart;
            if (turnManager != null) turnManager.SuspendAutoStart = true;
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
                var requestedNode = grid != null ? grid.GetNode(requested) : null;
                if (requestedNode == null || !requestedNode.IsWalkable)
                {
                    _host.ArenaLog($"⚠️ Avatar '<b>{unitData.Sheet.Name}</b>' ignoré : case ({requested.Q}, {requested.R}) invalide.");
                    continue;
                }

                // Déduplique : jamais deux avatars sur la même case.
                var coords = ResolveFreeCoords(requested, reserved);
                var node = grid.GetNode(coords);
                if (node == null || !node.IsWalkable || node.IsOccupied || reserved.Contains(coords))
                {
                    _host.ArenaLog($"⚠️ Avatar '<b>{unitData.Sheet.Name}</b>' ignoré : aucune case libre autour de ({requested.Q}, {requested.R}).");
                    continue;
                }
                if (!coords.Equals(requested)) relocated++;
                reserved.Add(coords);

                string unitObjName = !string.IsNullOrEmpty(unitData.UnitId)
                    ? unitData.UnitId
                    : $"Unit_{unitData.Sheet.Name.Replace(" ", "_")}";

                var go = new GameObject(unitObjName);
                var unit = go.AddComponent<TacticalUnit>();
                unit.InitializeFromSheet(unitData.Sheet, coords, grid, unitData.IsPlayer);
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
                    if (_state.PlayerUnit == null)
                    {
                        _state.PlayerUnit = unit;
                    }
                    else
                    {
                        _state.AdditionalPlayers.Add(unit);
                    }
                }
                else
                {
                    _state.SparringDummies.Add(unit);
                }

                _state.CustomSpawns.Add(new CombatDevArena.CustomSpawnRecord
                {
                    Sheet = unitData.Sheet,
                    SpawnCoords = coords,
                    IsPlayer = unitData.IsPlayer
                });

                turnManager?.RegisterUnit(unit);
            }
            }
            finally
            {
                if (turnManager != null) turnManager.SuspendAutoStart = prevSuspendLoad;
            }

            if (_state.SparringDummies.Count > 0)
            {
                _host.RequestSelectTarget(_state.SparringDummies[0]);
            }

            if (!_ctx.IsStoryScene() && _ctx.Camera != null)
            {
                if (_state.PlayerUnit != null)
                    _ctx.Camera.FocusOn(_state.PlayerUnit.transform);
                else if (_state.SparringDummies.Count > 0)
                    _ctx.Camera.FocusOn(_state.SparringDummies[0].transform);
            }

            // État verrouillé avant démarrage : évite un guard orphelin
            // qui ferait ignorer toutes les attaques suivantes (IA bloquée en FullAuto).
            _ctx.Guard.ForceReset();
            _ctx.AI?.StopAITurn();
            if (!_ctx.IsStoryScene()) _ctx.Cinematic?.ResetCinematicState();

            bool isRemoteClient = TurnManager.IsMultiplayerPlayerClient()
                || (Killtime.Multi.VTTTableSync.Instance != null && Killtime.Multi.VTTTableSync.Instance.IsApplyingRemoteAction);

            if (!isRemoteClient)
            {
                // La carte par défaut (et tout chargement de carte) démarre en combat :
                // StartNewRound() seul ne suffit pas, il est inopérant en exploration.
                if (_ctx.GetEnterCombatOnMapLoad()) turnManager?.EnterCombatMode(_state.PlayerUnit);
                turnManager?.StartNewRound();
                _ctx.AI?.TriggerAITurnIfApplicable();
                if (_ctx.GetEnterCombatOnMapLoad() && turnManager != null && !turnManager.IsInExploration)
                    _host.ArenaLog("⚔️ <b>Mode combat enclenché</b> : initiative tirée, tours actifs.");
            }

            SnapshotRecorder?.Invoke($"Chargement des unités de la carte ({reserved.Count} avatars)");
            string relocateInfo = relocated > 0 ? $" ({relocated} relocalisé(s) anti-empilement)" : "";
            _host.ArenaLog($"👥 <b>{reserved.Count} avatar(s)</b> chargé(s) depuis la configuration de carte{relocateInfo}.");

            // Les accessoires 3D (piliers, caisses) font obstacle à la ligne de mire :
            // (re)construit le registre depuis les instances en scène.
            try
            {
                PropObstacleRegistry.Rebuild(grid != null ? grid.HexRadius : 1f);
                _host.ArenaLog($"🧱 <b>{PropObstacleRegistry.Count} obstacle(s) 3D</b> enregistré(s) pour la ligne de mire.");
            }
            catch (Exception e) { Debug.LogWarning($"[ArenaUnitStore] Registre obstacles 3D : {e.Message}"); }
        }

        private TacticalUnit CreateUnit(string unitName, HexCoordinates coords, Attributes attributes, int baseArmor, bool isPlayer)
        {
            var grid = _ctx.Grid;
            var safeCoords = ResolveFreeCoords(coords);
            var go = new GameObject($"Unit_{unitName.Replace(" ", "_")}");
            var unit = go.AddComponent<TacticalUnit>();
            unit.ConfigureStats(unitName, attributes, baseArmor, isPlayer);
            unit.InitializePosition(safeCoords, grid);

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
            var grid = _ctx.Grid;
            var visualizer = _ctx.Visualizer;
            _ctx.Map.Clear();
            _state.CurrentLayout = layout;
            grid.ClearAllCovers();

            switch (layout)
            {
                case ArenaLayoutType.TheDuelRing:
                    // Arène dégagée, demi-couvertures en périphérie
                    grid.SetNodeCover(new HexCoordinates(4, 0), CoverType.Half, true);
                    grid.SetNodeCover(new HexCoordinates(-4, 0), CoverType.Half, true);
                    grid.SetNodeCover(new HexCoordinates(0, 4), CoverType.Half, true);
                    grid.SetNodeCover(new HexCoordinates(0, -4), CoverType.Half, true);
                    break;

                case ArenaLayoutType.TacticalBarricades:
                    // Couvertures tactiques réparties pour tester les angles de tir
                    // (Half -1, ThreeQuarters -2, Full = non visible, Livre V §21.3)
                    grid.SetNodeCover(new HexCoordinates(1, 0), CoverType.Half, true);
                    grid.SetNodeCover(new HexCoordinates(1, -2), CoverType.Full, false);
                    grid.SetNodeCover(new HexCoordinates(-1, 2), CoverType.Half, true);
                    grid.SetNodeCover(new HexCoordinates(-2, 0), CoverType.Full, false);
                    grid.SetNodeCover(new HexCoordinates(2, 1), CoverType.Half, true);
                    grid.SetNodeCover(new HexCoordinates(-1, -2), CoverType.Half, true);
                    grid.SetNodeCover(new HexCoordinates(2, -1), CoverType.ThreeQuarters, true);
                    grid.SetNodeCover(new HexCoordinates(-2, 1), CoverType.ThreeQuarters, true);
                    break;

                case ArenaLayoutType.KillzoneChokepoint:
                    // Mur de piliers créant un goulot d'étranglement central
                    grid.SetNodeCover(new HexCoordinates(0, 1), CoverType.Full, false);
                    grid.SetNodeCover(new HexCoordinates(0, 2), CoverType.Full, false);
                    grid.SetNodeCover(new HexCoordinates(0, -1), CoverType.Full, false);
                    grid.SetNodeCover(new HexCoordinates(0, -2), CoverType.Full, false);
                    grid.SetNodeCover(new HexCoordinates(1, -1), CoverType.Half, true);
                    grid.SetNodeCover(new HexCoordinates(-1, 1), CoverType.Half, true);
                    break;
            }

            if (visualizer != null)
            {
                visualizer.RefreshObstacles();
            }

            // Pas d'accessoires 3D sur les layouts procéduraux : purge le registre
            // (sinon des piliers d'une carte précédente bloqueraient encore).
            try { PropObstacleRegistry.Rebuild(grid != null ? grid.HexRadius : 1f); }
            catch (Exception e) { Debug.LogWarning($"[ArenaUnitStore] Registre obstacles 3D : {e.Message}"); }

            PersistLayoutPref?.Invoke();
            _host.ArenaLog($"📐 Layout d'arène configuré : <b>{layout}</b>");
        }

        public void RegisterSceneParty(TacticalUnit player, IEnumerable<TacticalUnit> allies)
        {
            _state.PlayerUnit = player;
            _state.AdditionalPlayers.Clear();
            if (allies != null)
            {
                foreach (var ally in allies)
                {
                    if (ally != null && ally != player)
                    {
                        _state.AdditionalPlayers.Add(ally);
                    }
                }
            }
        }

        public void RegisterHostileUnit(TacticalUnit unit)
        {
            if (unit == null || _state.SparringDummies.Contains(unit)) return;
            _state.SparringDummies.Add(unit);
            if (_state.CurrentTarget == null || !_state.CurrentTarget.Stats.IsAlive)
            {
                _host.RequestSelectTarget(unit);
            }
        }

        public TacticalUnit FindUnitByName(string unitName)
        {
            if (string.IsNullOrEmpty(unitName)) return null;
            if (_state.PlayerUnit != null && _state.PlayerUnit.Stats != null && _state.PlayerUnit.Stats.Name == unitName) return _state.PlayerUnit;
            for (int i = 0; i < _state.AdditionalPlayers.Count; i++)
                if (_state.AdditionalPlayers[i] != null && _state.AdditionalPlayers[i].Stats != null && _state.AdditionalPlayers[i].Stats.Name == unitName)
                    return _state.AdditionalPlayers[i];
            for (int i = 0; i < _state.SparringDummies.Count; i++)
                if (_state.SparringDummies[i] != null && _state.SparringDummies[i].Stats != null && _state.SparringDummies[i].Stats.Name == unitName)
                    return _state.SparringDummies[i];
            var all = TacticalUnitRegistry.AllUnits;
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && all[i].Stats != null && all[i].Stats.Name == unitName)
                    return all[i];
            return null;
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
            _host.ArenaLog("⚡ <b>Trio Tri-Fusion déployé</b> : Thomas-0, Mina-0 et Lucas-0 sur la grille.");
        }

        public void SpawnHeroicQuatuor(HexCoordinates centerCoords)
        {
            SpawnHeroicTrio(centerCoords);
            SpawnCustomCharacter(JohnCharacter.BuildHeroicSheet(), centerCoords.GetNeighbor(1), true);
            _host.ArenaLog("🛰️ <b>Quatuor déployé</b> : Trio Tri-Fusion + John (Passeur) sur la grille.");
        }

        public void SpawnHeroicQuintet(HexCoordinates centerCoords)
        {
            SpawnHeroicQuatuor(centerCoords);
            SpawnCustomCharacter(ErikaCharacter.BuildHeroicSheet(), centerCoords.GetNeighbor(2), true);
            _host.ArenaLog("🔥 <b>Quintette déployé</b> : Quatuor + Erika de Cleya (Flamme Cleyane) sur la grille.");
        }

        public void SpawnHeroicSextet(HexCoordinates centerCoords)
        {
            SpawnHeroicQuintet(centerCoords);
            SpawnCustomCharacter(VanceCharacter.BuildHeroicSheet(), centerCoords.GetNeighbor(4), true);
            _host.ArenaLog("🛡️ <b>Sextette déployé</b> : Quintette + Commandant Vance (Autorité du Commandant) sur la grille.");
        }
    }
}

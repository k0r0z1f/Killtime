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
    // Carte, layouts, spawns arene (RD-152).
    public partial class CombatDevArena : MonoBehaviour, IArenaHost
    {
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
            PurgeArenaVehicles();
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
    }
}

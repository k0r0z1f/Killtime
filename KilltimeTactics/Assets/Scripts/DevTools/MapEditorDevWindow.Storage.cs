using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Killtime.Tactics;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Core.Character;

namespace Killtime.UI
{
    /// <summary>
    /// Persistance JSON + régénération de grille (extrait sans modification depuis MapEditorDevWindow.cs).
    /// </summary>
    public partial class MapEditorDevWindow
    {
        private void RegenerateGridWithRadius(int radius)
        {
            if (_grid == null) return;

            ClearAllPlacedProps();
            _arena?.ClearLoadedMap();

            var radiusField = typeof(TacticalHexGrid).GetField("_gridRadius", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (radiusField != null)
            {
                radiusField.SetValue(_grid, radius);
            }

            _grid.CeilingHeight = _ceilingHeightInput;
            _grid.GenerateGrid();
            _gridVisualizer?.BuildVisualGrid();
            _statusMessage = $"Grille régénérée avec un rayon de {radius} cases et plafond à {_ceilingHeightInput:0.##}m.";
        }

        private void SaveCurrentMap(string mapName)
        {
            EnsureDirectoryExists();
            string safeName = string.Join("_", mapName.Split(Path.GetInvalidFileNameChars()));
            if (string.IsNullOrWhiteSpace(safeName)) safeName = "Map_Unnamed";

            SyncPropsFromScene();

            var data = new TacticalMapSaveData
            {
                MapName = safeName,
                GridRadius = _grid != null ? _grid.GridRadius : 8,
                CeilingHeight = _grid != null ? _grid.CeilingHeight : _ceilingHeightInput
            };

            if (_grid != null)
            {
                foreach (var kvp in _grid.Nodes)
                {
                    var node = kvp.Value;
                    if (node.Cover != CoverType.None || !node.IsWalkable || node.HasCeiling || Mathf.Abs(node.Elevation) > 0.001f || !string.IsNullOrEmpty(node.GroundTexture))
                    {
                        data.ModifiedTiles.Add(new MapTileData
                        {
                            Q = node.Coordinates.Q,
                            R = node.Coordinates.R,
                            Cover = node.Cover,
                            IsWalkable = node.IsWalkable,
                            HasCeiling = node.HasCeiling,
                            Elevation = node.Elevation,
                            GroundTexture = node.GroundTexture
                        });
                    }
                }
            }

            int ceilingPropsCount = 0;
            data.PlacedProps.Clear();
            foreach (var prop in _placedPropRecords.Values)
            {
                if (prop != null)
                {
                    data.PlacedProps.Add(prop);
                    if (prop.PlacementType == PropPlacementType.Plafond) ceilingPropsCount++;
                }
            }

            data.PlacedUnits.Clear();
            var sceneUnits = new List<TacticalUnit>(TacticalUnitRegistry.AllUnits);
            sceneUnits.Sort((a, b) => b.IsPlayerControlled.CompareTo(a.IsPlayerControlled));

            for (int i = 0; i < sceneUnits.Count; i++)
            {
                var u = sceneUnits[i];
                if (u == null || u.Stats == null) continue;

                var entry = new MapUnitData
                {
                    UnitId = u.gameObject.name,
                    Sheet = u.GetOrBuildSheet(),
                    Q = u.CurrentCoords.Q,
                    R = u.CurrentCoords.R,
                    IsPlayer = u.IsPlayerControlled,
                    currentHealth = u.Stats.CurrentHealth,
                    currentAP = u.Stats.CurrentActionPoints,
                    essoufflement = u.Stats.Essoufflement,
                    activeStatus = (int)u.Stats.ActiveStatus
                };

                if (u.Stats.ActiveStatus != StatusEffect.None)
                {
                    entry.statusEffects.Add(u.Stats.ActiveStatus.ToString());
                }

                data.PlacedUnits.Add(entry);
            }

            data.PlacedPixies.Clear();
            var activePixies = FindObjectsByType<Killtime.Tactics.Lighting.TacticalPixieLight>();
            for (int i = 0; i < activePixies.Length; i++)
            {
                if (activePixies[i] != null)
                {
                    data.PlacedPixies.Add(activePixies[i].transform.position);
                }
            }

            data.SaveTimestamp = DateTime.Now.ToString("dd/MM HH:mm");

            string fullPath = Path.Combine(MapsDirectory, $"{safeName}.json");
            string json = JsonUtility.ToJson(data, true);
            File.WriteAllText(fullPath, json);

            if (_arena != null)
            {
                _arena.RegisterLoadedMap(data, fullPath);
            }

            _statusMessage = $"Carte '{safeName}' sauvegardée ({data.ModifiedTiles.Count} tuiles, {data.PlacedProps.Count} objets 3D, {data.PlacedUnits.Count} avatar(s), {data.PlacedPixies.Count} pixie(s)).";
        }

        public void ApplyLoadedMap(TacticalMapSaveData data, string mapPath = null)
        {
            if (data == null || _grid == null) return;

            EnsureReferences();
            ClearAllPlacedProps();
            _grid.ResetGridState();

            if (data.CeilingHeight > 0)
            {
                _grid.CeilingHeight = data.CeilingHeight;
                _ceilingHeightInput = data.CeilingHeight;
            }

            if (_arena != null)
            {
                _arena.ClearAllUnits();
            }
            else
            {
                Killtime.Tactics.Units.DroppedWeaponPickup.ClearAllDropped();
                var existingUnits = TacticalUnitRegistry.AllUnits;
                for (int i = 0; i < existingUnits.Count; i++)
                {
                    if (existingUnits[i] != null)
                    {
                        var node = _grid.GetNode(existingUnits[i].CurrentCoords);
                        if (node != null) node.IsOccupied = false;
                        Destroy(existingUnits[i].gameObject);
                    }
                }
                _turnManager?.ClearUnits();
                _turnManager?.ResetCombatState();
            }

            for (int i = 0; i < data.ModifiedTiles.Count; i++)
            {
                var tile = data.ModifiedTiles[i];
                var coords = new HexCoordinates(tile.Q, tile.R);
                _grid.SetNodeCover(coords, tile.Cover, tile.IsWalkable);
                _grid.SetNodeCeiling(coords, tile.HasCeiling);
                _grid.SetNodeElevation(coords, tile.Elevation);
                _grid.SetNodeGroundTexture(coords, tile.GroundTexture);
            }

            EnsurePropsRoot();
            int loadedCount = 0;
            int ceilingLoadedCount = 0;

            for (int i = 0; i < data.PlacedProps.Count; i++)
            {
                var propData = data.PlacedProps[i];
                var coords = new HexCoordinates(propData.Q, propData.R);
                var node = _grid.GetNode(coords);

                Vector3 propPos;
                if (node != null)
                {
                    node.HasCustomVisual = true;
                    if (propData.PlacementType != PropPlacementType.Plafond || !propData.IsWalkable)
                    {
                        node.Cover = propData.Cover;
                        node.IsWalkable = propData.IsWalkable;
                    }

                    if (propData.PlacementType == PropPlacementType.Plafond && !node.HasCeiling)
                    {
                        node.HasCeiling = true;
                        _grid.SetNodeCeiling(coords, true);
                    }

                    propPos = node.WorldPosition;
                }
                else
                {
                    propPos = coords.ToWorldPosition(_grid != null ? _grid.HexRadius : 1.0f, 0.0f);
                }

                var prefab = LoadPropPrefab(propData.PrefabName);
                if (prefab != null)
                {
                    string propKey = GetPropKey(coords, propData.PlacementType);

#if UNITY_EDITOR
                    var instance = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, _propsRoot);
#else
                    var instance = Instantiate(prefab, _propsRoot);
#endif
                    instance.name = $"Prop_{propData.PrefabName}_{coords.Q}_{coords.R}_{propData.PlacementType}";

                    switch (propData.PlacementType)
                    {
                        case PropPlacementType.Plancher:
                            propPos.y += propData.HeightOffset;
                            break;
                        case PropPlacementType.Plafond:
                            propPos.y += _grid.CeilingHeight - propData.HeightOffset;
                            break;
                        case PropPlacementType.Mur:
                            propPos.y += (_grid.CeilingHeight * 0.4f) + propData.HeightOffset;
                            break;
                        case PropPlacementType.Flottant:
                            propPos.y += 1.8f + propData.HeightOffset;
                            break;
                    }

                    instance.transform.position = propPos;
                    instance.transform.rotation = Quaternion.Euler(0f, propData.RotationY, 0f);
                    instance.transform.localScale = Vector3.Scale(prefab.transform.localScale, Vector3.one * propData.Scale);

                    var propInstComp = instance.GetComponent<MapPropInstance>() ?? instance.AddComponent<MapPropInstance>();
                    propInstComp.Data = propData;

                    if (propData.PlacementType == PropPlacementType.Plafond)
                    {
                        var ceilingProp = instance.GetComponent<TacticalCeilingProp>() ?? instance.AddComponent<TacticalCeilingProp>();
                        ceilingProp.Initialize();
                        ceilingLoadedCount++;
                    }

                    _spawnedPropInstances[propKey] = instance;
                    _placedPropRecords[propKey] = propData;
                    loadedCount++;
                }
                else
                {
                    Debug.LogWarning($"[MapEditor] Modèle introuvable pour '{propData.PrefabName}' lors du chargement de la carte.");
                }
            }

            int unitLoadedCount = 0;
            if (data.PlacedUnits != null && data.PlacedUnits.Count > 0)
            {
                if (_arena != null)
                {
                    _arena.LoadUnitsFromMap(data.PlacedUnits);
                }
                else
                {
                    for (int i = 0; i < data.PlacedUnits.Count; i++)
                    {
                        var uData = data.PlacedUnits[i];
                        if (uData == null || uData.Sheet == null) continue;

                        var uCoords = new HexCoordinates(uData.Q, uData.R);
                        var go = new GameObject($"Unit_{uData.Sheet.Name.Replace(" ", "_")}");
                        var unit = go.AddComponent<TacticalUnit>();
                        unit.InitializeFromSheet(uData.Sheet, uCoords, _grid, uData.IsPlayer);
                        _turnManager?.RegisterUnit(unit);
                    }

                    _turnManager?.StartNewRound();
                }

                unitLoadedCount = data.PlacedUnits.Count;
            }

            var strayPixies = FindObjectsByType<Killtime.Tactics.Lighting.TacticalPixieLight>();
            for (int i = 0; i < strayPixies.Length; i++)
            {
                if (strayPixies[i] != null) Destroy(strayPixies[i].gameObject);
            }

            int loadedPixiesCount = 0;
            if (data.PlacedPixies != null)
            {
                for (int i = 0; i < data.PlacedPixies.Count; i++)
                {
                    var p = Killtime.Tactics.Lighting.TacticalPixieLight.SpawnPixie();
                    p.transform.position = data.PlacedPixies[i];
                    loadedPixiesCount++;
                }
            }

            _gridVisualizer?.RefreshObstacles();
            // Registre ligne de mire APRÈS les visuels : bornes des meshes affichés.
            try { PropObstacleRegistry.Rebuild(_grid != null ? _grid.HexRadius : 1f); } catch { /* ignore */ }
            _statusMessage = $"Carte '{data.MapName}' chargée ({loadedCount} objet(s) 3D, {unitLoadedCount} avatar(s), {loadedPixiesCount} pixie(s), Plafond: {_grid.CeilingHeight:0.##}m).";

            if (_arena != null)
            {
                _arena.RegisterLoadedMap(data, mapPath);
            }
        }

        private void LoadMap(string fullPath)
        {
            if (!File.Exists(fullPath) || _grid == null) return;

            string json = File.ReadAllText(fullPath);
            var data = JsonUtility.FromJson<TacticalMapSaveData>(json);

            ApplyLoadedMap(data, fullPath);
        }

        private void DeleteMap(string fullPath)
        {
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                _statusMessage = "Fichier de carte supprimé.";
            }
        }

        private static void EnsureDirectoryExists()
        {
            if (!Directory.Exists(MapsDirectory))
            {
                Directory.CreateDirectory(MapsDirectory);
            }
        }

        private static List<string> GetSavedMapFiles()
        {
            EnsureDirectoryExists();
            return new List<string>(Directory.GetFiles(MapsDirectory, "*.json"));
        }
    }
}

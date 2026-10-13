using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Killtime.Tactics;
using Killtime.Tactics.Grid;

namespace Killtime.UI
{
    /// <summary>
    /// Catalogue + instanciation des objets 3D (extrait sans modification depuis MapEditorDevWindow.cs).
    /// </summary>
    public partial class MapEditorDevWindow
    {
        public void RefreshAvailableProps()
        {
            _availablePropNames.Clear();

#if UNITY_EDITOR
            UnityEditor.AssetDatabase.Refresh();
            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
            foreach (string guid in guids)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) continue;

                int resIndex = path.IndexOf("/Resources/Objects/", StringComparison.OrdinalIgnoreCase);
                if (resIndex >= 0)
                {
                    string relative = path.Substring(resIndex + "/Resources/Objects/".Length);
                    string ext = Path.GetExtension(relative);
                    if (!string.IsNullOrEmpty(ext))
                    {
                        relative = relative.Substring(0, relative.Length - ext.Length);
                    }
                    if (!_availablePropNames.Contains(relative))
                    {
                        _availablePropNames.Add(relative);
                    }
                }
            }
#endif

            if (_availablePropNames.Count == 0)
            {
                var prefabs = Resources.LoadAll<GameObject>("Objects");
                if (prefabs == null || prefabs.Length == 0)
                {
                    prefabs = Resources.LoadAll<GameObject>("objects");
                }

                if (prefabs != null)
                {
                    for (int i = 0; i < prefabs.Length; i++)
                    {
                        if (prefabs[i] != null && !_availablePropNames.Contains(prefabs[i].name))
                        {
                            _availablePropNames.Add(prefabs[i].name);
                        }
                    }
                }
            }
        }

        private GameObject LoadPropPrefab(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName)) return null;

#if UNITY_EDITOR
            string[] guids = UnityEditor.AssetDatabase.FindAssets($"{prefabName} t:Prefab", new[] { "Assets" });
            foreach (string guid in guids)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                {
                    string fName = Path.GetFileNameWithoutExtension(path);
                    if (string.Equals(fName, prefabName, StringComparison.OrdinalIgnoreCase))
                    {
                        var loaded = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                        if (loaded != null) return loaded;
                    }
                }
            }
#endif

            var prefab = Resources.Load<GameObject>($"Environment/{prefabName}")
                      ?? Resources.Load<GameObject>($"Environments/{prefabName}")
                      ?? Resources.Load<GameObject>($"Prefabs/Environment/{prefabName}")
                      ?? Resources.Load<GameObject>($"Prefabs/Environments/{prefabName}")
                      ?? Resources.Load<GameObject>($"Objects/{prefabName}")
                      ?? Resources.Load<GameObject>($"objects/{prefabName}")
                      ?? Resources.Load<GameObject>($"Props/{prefabName}")
                      ?? Resources.Load<GameObject>($"props/{prefabName}")
                      ?? Resources.Load<GameObject>($"Prefabs/Props/{prefabName}")
                      ?? Resources.Load<GameObject>($"Prefabs/Objects/{prefabName}")
                      ?? Resources.Load<GameObject>($"Prefabs/{prefabName}")
                      ?? Resources.Load<GameObject>($"Models/{prefabName}")
                      ?? Resources.Load<GameObject>($"Characters/{prefabName}")
                      ?? Resources.Load<GameObject>(prefabName);

            if (prefab == null)
            {
                var allInObjects = Resources.LoadAll<GameObject>("Objects");
                for (int i = 0; i < allInObjects.Length; i++)
                {
                    if (allInObjects[i] != null && string.Equals(allInObjects[i].name, prefabName, StringComparison.OrdinalIgnoreCase))
                    {
                        return allInObjects[i];
                    }
                }
            }

            return prefab;
        }

        private void PlaceCurrent3DProp(HexNode node)
        {
            if (_availablePropNames.Count == 0)
            {
                _statusMessage = "Aucun préfab trouvé dans Assets/Resources/Objects.";
                return;
            }

            if (_propPlacementType == PropPlacementType.Plafond && !node.HasCeiling)
            {
                node.HasCeiling = true;
                _grid?.SetNodeCeiling(node.Coordinates, true);
                _gridVisualizer?.RefreshObstacles();
            }

            string prefabName = _availablePropNames[_selectedPropIndex];
            var prefab = LoadPropPrefab(prefabName);

            if (prefab == null)
            {
                _statusMessage = $"Échec de chargement du modèle '{prefabName}'.";
                return;
            }

            EnsurePropsRoot();
            RemovePropAt(node.Coordinates, _propPlacementType, false);

            string propKey = GetPropKey(node.Coordinates, _propPlacementType);

#if UNITY_EDITOR
            var instance = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab, _propsRoot);
#else
            var instance = Instantiate(prefab, _propsRoot);
#endif
            instance.name = $"Prop_{prefabName}_{node.Coordinates.Q}_{node.Coordinates.R}_{_propPlacementType}";

            float ceilingHeight = _grid != null ? _grid.CeilingHeight : 3.5f;
            Vector3 worldPos = node.WorldPosition;

            switch (_propPlacementType)
            {
                case PropPlacementType.Plancher:
                    worldPos.y += _propHeightOffset;
                    break;
                case PropPlacementType.Plafond:
                    worldPos.y += ceilingHeight - _propHeightOffset;
                    break;
                case PropPlacementType.Mur:
                    worldPos.y += (ceilingHeight * 0.4f) + _propHeightOffset;
                    break;
                case PropPlacementType.Flottant:
                    worldPos.y += 1.8f + _propHeightOffset;
                    break;
            }

            instance.transform.position = worldPos;
            instance.transform.rotation = Quaternion.Euler(0f, _propRotationY, 0f);
            instance.transform.localScale = Vector3.Scale(prefab.transform.localScale, Vector3.one * _propScale);

            var record = new MapPropData
            {
                PrefabName = prefabName,
                Q = node.Coordinates.Q,
                R = node.Coordinates.R,
                RotationY = _propRotationY,
                Scale = _propScale,
                Cover = _propCover,
                IsWalkable = _propIsWalkable,
                PlacementType = _propPlacementType,
                HeightOffset = _propHeightOffset
            };

            var propInstComp = instance.GetComponent<MapPropInstance>() ?? instance.AddComponent<MapPropInstance>();
            propInstComp.Data = record;

            if (_propPlacementType == PropPlacementType.Plafond)
            {
                var ceilingProp = instance.GetComponent<TacticalCeilingProp>() ?? instance.AddComponent<TacticalCeilingProp>();
                ceilingProp.Initialize();
            }

            _spawnedPropInstances[propKey] = instance;
            _placedPropRecords[propKey] = record;

            if (_propPlacementType != PropPlacementType.Plafond || !_propIsWalkable)
            {
                node.Cover = _propCover;
                node.IsWalkable = _propIsWalkable;
            }
            node.HasCustomVisual = true;

            _gridVisualizer?.RefreshObstacles();
            try { PropObstacleRegistry.Rebuild(_grid != null ? _grid.HexRadius : 1f); } catch { /* ignore */ }
            _statusMessage = $"Modèle '{prefabName}' posé [{_propPlacementType}] en ({node.Coordinates.Q}, {node.Coordinates.R}).";
        }

        private void RemovePropAt(HexCoordinates coords, PropPlacementType? placementType = null, bool restoreNode = true)
        {
            List<string> keysToRemove = new();

            foreach (var kvp in _placedPropRecords)
            {
                if (kvp.Value.Q == coords.Q && kvp.Value.R == coords.R)
                {
                    if (!placementType.HasValue || kvp.Value.PlacementType == placementType.Value)
                    {
                        keysToRemove.Add(kvp.Key);
                    }
                }
            }

            for (int i = 0; i < keysToRemove.Count; i++)
            {
                string key = keysToRemove[i];
                if (_spawnedPropInstances.TryGetValue(key, out var instance) && instance != null)
                {
                    Destroy(instance);
                }
                _spawnedPropInstances.Remove(key);
                _placedPropRecords.Remove(key);
            }

            if (restoreNode && _grid != null)
            {
                bool hasRemainingProps = false;
                foreach (var p in _placedPropRecords.Values)
                {
                    if (p.Q == coords.Q && p.R == coords.R)
                    {
                        hasRemainingProps = true;
                        break;
                    }
                }

                if (!hasRemainingProps)
                {
                    var node = _grid.GetNode(coords);
                    if (node != null)
                    {
                        node.Cover = CoverType.None;
                        node.IsWalkable = true;
                        node.HasCustomVisual = false;
                    }
                }
                _gridVisualizer?.RefreshObstacles();
                try { PropObstacleRegistry.Rebuild(_grid != null ? _grid.HexRadius : 1f); } catch { /* ignore */ }
            }
        }

        private void ClearAllPlacedProps()
        {
            if (_propsRoot != null)
            {
                for (int i = _propsRoot.childCount - 1; i >= 0; i--)
                {
                    var child = _propsRoot.GetChild(i);
                    if (child != null) Destroy(child.gameObject);
                }
            }

            var strayProps = FindObjectsByType<MapPropInstance>();
            for (int i = 0; i < strayProps.Length; i++)
            {
                if (strayProps[i] != null) Destroy(strayProps[i].gameObject);
            }

            _spawnedPropInstances.Clear();
            _placedPropRecords.Clear();
        }
    }
}

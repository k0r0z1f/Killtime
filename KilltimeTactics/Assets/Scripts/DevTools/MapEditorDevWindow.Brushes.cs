using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics;
using Killtime.Tactics.Grid;

namespace Killtime.UI
{
    /// <summary>
    /// Pinceaux terrain + interaction 3D (extrait sans modification depuis MapEditorDevWindow.cs).
    /// </summary>
    public partial class MapEditorDevWindow
    {
        protected override void Update()
        {
            base.Update();

            if (_isOpen)
            {
                EnsureCeilingPropsRegistered();

                bool isSpecificCeilingMode = _activeBrush == MapBrushType.AddCeiling ||
                                             _activeBrush == MapBrushType.RemoveCeiling ||
                                             (_selectedTab == 1 && _propPlacementType == PropPlacementType.Plafond) ||
                                             (_selectedTab == 0 && _activeBrush == MapBrushType.Inspect && _inspectedNode != null && _inspectedNode.HasCeiling);

                var targetMode = isSpecificCeilingMode ? CeilingVisualMode.EditorCeilingMode : CeilingVisualMode.EditorNonCeilingMode;
                _gridVisualizer?.SetCeilingVisualMode(targetMode);

                HandlePaintingInput();
            }
        }

        private void EnsureCeilingPropsRegistered()
        {
            foreach (var kvp in _placedPropRecords)
            {
                var data = kvp.Value;
                if (data != null && data.PlacementType == PropPlacementType.Plafond)
                {
                    if (_spawnedPropInstances.TryGetValue(kvp.Key, out var inst) && inst != null)
                    {
                        if (!inst.TryGetComponent<TacticalCeilingProp>(out var prop))
                        {
                            prop = inst.AddComponent<TacticalCeilingProp>();
                            prop.Initialize();
                        }
                    }
                }
            }
        }

        private void HandlePaintingInput()
        {
            if (UnityEngine.Camera.main == null || _grid == null) return;

            Vector2 mouseScreenPos = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            // Toute fenêtre flottante ou panel HUD bloque la peinture (pas seulement notre fenêtre).
            // Évite de peindre la carte en cliquant dans le Créateur de Personnage, la Table des Règles, etc.
            if (Killtime.UI.FloatingWindowChrome.IsPointerOverAnyWindow(mouseScreenPos))
            {
                _gridVisualizer?.SetHoveredCoord(null, false);
                return;
            }

            bool isCeilingTargeting = (_activeBrush == MapBrushType.AddCeiling) ||
                                      (_activeBrush == MapBrushType.RemoveCeiling) ||
                                      (_selectedTab == 2 && _propPlacementType == PropPlacementType.Plafond) ||
                                      (_activeBrush == MapBrushType.Inspect && _inspectedNode != null && _inspectedNode.HasCeiling);

            Ray ray = UnityEngine.Camera.main.ScreenPointToRay(Input.mousePosition);
            HexNode node = null;

            if (isCeilingTargeting)
            {
                Plane ceilingPlane = new Plane(Vector3.up, new Vector3(0, _grid.CeilingHeight, 0));
                if (ceilingPlane.Raycast(ray, out float enter))
                {
                    Vector3 ceilingHitPos = ray.GetPoint(enter);
                    _grid.TryGetNodeAtWorldPosition(ceilingHitPos, out node);
                }
            }

            if (node == null && Physics.Raycast(ray, out RaycastHit hit))
            {
                _grid.TryGetNodeAtWorldPosition(hit.point, out node);
            }

            if (node != null)
            {
                _gridVisualizer?.SetHoveredCoord(node.Coordinates, isCeilingTargeting);

                if (Input.GetMouseButtonDown(0))
                {
                    // Clic carte 3D : repli auto des fenêtres flottantes en coins (fantômes).
                    Killtime.UI.FloatingWindowChrome.OnMapClicked();
                    if (_activeBrush == MapBrushType.Inspect)
                    {
                        _inspectedNode = node;
                        var propsOnNode = GetPropsAt(node.Coordinates);
                        string propInfo = "";
                        if (propsOnNode.Count > 0)
                        {
                            propInfo = " | Modèles: ";
                            for (int pIdx = 0; pIdx < propsOnNode.Count; pIdx++)
                            {
                                propInfo += $"{propsOnNode[pIdx].PrefabName} [{propsOnNode[pIdx].PlacementType}] ";
                            }
                        }
                        string ceilingInfo = node.HasCeiling ? " | Plafond: OUI" : " | Plafond: NON";
                        string groundTexInfo = !string.IsNullOrEmpty(node.GroundTexture) ? $" | Texture: {node.GroundTexture}" : "";
                        _statusMessage = $"Hex ({node.Coordinates.Q}, {node.Coordinates.R}) | Altitude: {node.Elevation:0.##}m | Couverture: {node.Cover}{ceilingInfo}{groundTexInfo}{propInfo}";
                    }
                    else if (_activeBrush == MapBrushType.PlaceProp3D)
                    {
                        PlaceCurrent3DProp(node);
                    }
                    else if (_activeBrush == MapBrushType.EraseProp3D)
                    {
                        RemovePropAt(node.Coordinates);
                    }
                    else
                    {
                        ApplyBrushWithRadius(node, _activeBrush);
                    }
                }
                else if (Input.GetMouseButton(0))
                {
                    if (_activeBrush == MapBrushType.PaintGroundTexture ||
                        _activeBrush == MapBrushType.ClearGroundTexture ||
                        _activeBrush == MapBrushType.ClearGround ||
                        _activeBrush == MapBrushType.HalfCover ||
                        _activeBrush == MapBrushType.ThreeQuartersCover ||
                        _activeBrush == MapBrushType.FullCover ||
                        _activeBrush == MapBrushType.ImpassableHole ||
                        _activeBrush == MapBrushType.AddCeiling ||
                        _activeBrush == MapBrushType.RemoveCeiling)
                    {
                        ApplyBrushWithRadius(node, _activeBrush);
                    }
                }
            }
            else
            {
                _gridVisualizer?.SetHoveredCoord(null, false);
            }
        }

        private void ApplyBrushWithRadius(HexNode centerNode, MapBrushType brush)
        {
            var nodes = GetNodesInBrushRadius(centerNode.Coordinates, _sculptRadius);
            for (int i = 0; i < nodes.Count; i++)
            {
                ApplyTerrainBrush(nodes[i], brush);
            }
        }

        private List<HexNode> GetNodesInBrushRadius(HexCoordinates center, int radius)
        {
            var list = new List<HexNode>();
            if (_grid == null) return list;

            if (radius <= 1)
            {
                var single = _grid.GetNode(center);
                if (single != null) list.Add(single);
                return list;
            }

            for (int q = -radius + 1; q < radius; q++)
            {
                int r1 = Mathf.Max(-radius + 1, -q - radius + 1);
                int r2 = Mathf.Min(radius - 1, -q + radius - 1);
                for (int r = r1; r <= r2; r++)
                {
                    var targetCoords = new HexCoordinates(center.Q + q, center.R + r);
                    var node = _grid.GetNode(targetCoords);
                    if (node != null) list.Add(node);
                }
            }
            return list;
        }

        private void ApplyTerrainBrush(HexNode node, MapBrushType brush)
        {
            bool modified = false;

            switch (brush)
            {
                case MapBrushType.ClearGround:
                    if (node.Cover != CoverType.None || !node.IsWalkable)
                    {
                        node.Cover = CoverType.None;
                        node.IsWalkable = true;
                        modified = true;
                    }
                    RemovePropAt(node.Coordinates, PropPlacementType.Plancher, false);
                    break;

                case MapBrushType.HalfCover:
                    if (node.Cover != CoverType.Half || !node.IsWalkable)
                    {
                        node.Cover = CoverType.Half;
                        node.IsWalkable = true;
                        modified = true;
                    }
                    break;

                case MapBrushType.FullCover:
                    if (node.Cover != CoverType.Full || node.IsWalkable)
                    {
                        node.Cover = CoverType.Full;
                        node.IsWalkable = false;
                        modified = true;
                    }
                    break;

                case MapBrushType.ThreeQuartersCover:
                    if (node.Cover != CoverType.ThreeQuarters || !node.IsWalkable)
                    {
                        node.Cover = CoverType.ThreeQuarters;
                        node.IsWalkable = true;
                        modified = true;
                    }
                    break;

                case MapBrushType.ImpassableHole:
                    if (node.Cover != CoverType.None || node.IsWalkable)
                    {
                        node.Cover = CoverType.None;
                        node.IsWalkable = false;
                        modified = true;
                    }
                    break;

                case MapBrushType.AddCeiling:
                    if (!node.HasCeiling)
                    {
                        node.HasCeiling = true;
                        modified = true;
                    }
                    break;

                case MapBrushType.RemoveCeiling:
                    if (node.HasCeiling)
                    {
                        node.HasCeiling = false;
                        modified = true;
                    }
                    break;

                case MapBrushType.PaintGroundTexture:
                    if (_availableGroundTextures.Count > 0 && _selectedGroundIndex < _availableGroundTextures.Count)
                    {
                        string selectedTexPath = _availableGroundTextures[_selectedGroundIndex].RelativePath;
                        if (node.GroundTexture != selectedTexPath)
                        {
                            node.GroundTexture = selectedTexPath;
                            modified = true;
                        }
                    }
                    break;

                case MapBrushType.ClearGroundTexture:
                    if (!string.IsNullOrEmpty(node.GroundTexture))
                    {
                        node.GroundTexture = "";
                        modified = true;
                    }
                    break;

                case MapBrushType.ElevateGround:
                    node.Elevation += _sculptStep;
                    node.WorldPosition = node.Coordinates.ToWorldPosition(_grid != null ? _grid.HexRadius : 1.0f, (_grid != null ? 0.05f : 0f) + node.Elevation);
                    modified = true;
                    break;

                case MapBrushType.LowerGround:
                    node.Elevation -= _sculptStep;
                    node.WorldPosition = node.Coordinates.ToWorldPosition(_grid != null ? _grid.HexRadius : 1.0f, (_grid != null ? 0.05f : 0f) + node.Elevation);
                    modified = true;
                    break;

                case MapBrushType.SetElevation:
                    node.Elevation = _targetElevation;
                    node.WorldPosition = node.Coordinates.ToWorldPosition(_grid != null ? _grid.HexRadius : 1.0f, (_grid != null ? 0.05f : 0f) + node.Elevation);
                    modified = true;
                    break;

                case MapBrushType.SmoothElevation:
                    if (_grid != null)
                    {
                        float sum = node.Elevation;
                        int count = 1;
                        for (int dir = 0; dir < 6; dir++)
                        {
                            var neighbor = _grid.GetNode(node.Coordinates.GetNeighbor(dir));
                            if (neighbor != null)
                            {
                                sum += neighbor.Elevation;
                                count++;
                            }
                        }
                        node.Elevation = sum / count;
                        node.WorldPosition = node.Coordinates.ToWorldPosition(_grid.HexRadius, 0.05f + node.Elevation);
                        modified = true;
                    }
                    break;
            }

            if (modified && _gridVisualizer != null)
            {
                _gridVisualizer.RefreshObstacles();
            }
        }

        private void GenerateTerrainPreset(bool isCrater)
        {
            if (_grid == null) return;
            float maxOffset = isCrater ? -1.8f : 2.2f;
            int maxRadius = _grid.GridRadius;

            foreach (var node in _grid.Nodes.Values)
            {
                int dist = node.Coordinates.DistanceTo(new HexCoordinates(0, 0));
                float t = Mathf.Clamp01(1f - ((float)dist / Mathf.Max(1, maxRadius)));
                node.Elevation = Mathf.SmoothStep(0f, maxOffset, t);
                node.WorldPosition = node.Coordinates.ToWorldPosition(_grid.HexRadius, 0.05f + node.Elevation);
            }

            _gridVisualizer?.RefreshObstacles();
            _statusMessage = isCrater ? "Cratère central généré." : "Colline centrale générée.";
        }
    }
}

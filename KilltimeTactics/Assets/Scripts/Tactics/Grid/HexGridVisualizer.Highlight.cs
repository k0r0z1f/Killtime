using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Killtime.Tactics.Grid
{
    public partial class HexGridVisualizer : MonoBehaviour
    {
        /// <summary>
        /// Brouillard de guerre asymétrique (§21.4) : cases hors du champ 360° de
        /// l'escouade (portée d'ambiance + murs Full) plongées dans l'obscurité.
        /// Appelé par FogOfWarManager ; le MJ n'active jamais ce masque.
        /// </summary>
        public void SetFogOfWar(System.Collections.Generic.HashSet<HexCoordinates> visibleCells, bool enabled)
        {
            _fogEnabled = enabled;
            _fogVisible.Clear();
            if (enabled && visibleCells != null)
            {
                foreach (var c in visibleCells) _fogVisible.Add(c);
            }
            RefreshAllTileColors();
        }

        public void ClearFogOfWar()
        {
            if (!_fogEnabled && _fogVisible.Count == 0) return;
            _fogEnabled = false;
            _fogVisible.Clear();
            RefreshAllTileColors();
        }

        public bool IsFogEnabled => _fogEnabled;

        public bool IsCellLitByFog(HexCoordinates coords)
        {
            return !_fogEnabled || _fogVisible.Contains(coords);
        }

        public void SetReachableCoords(IEnumerable<HexCoordinates> reachable)        {
            _currentReachable.Clear();
            if (reachable != null)
            {
                foreach (var c in reachable) _currentReachable.Add(c);
            }
            RefreshAllTileColors();
        }

        public void SetHoveredCoord(HexCoordinates? coord)
        {
            if (_currentHovered.Equals(coord)) return;
            _currentHovered = coord;
            RefreshAllTileColors();
        }

        public void SetTargetCoord(HexCoordinates? coord)
        {
            if (_currentTarget.Equals(coord)) return;
            _currentTarget = coord;
            RefreshAllTileColors();
        }

        public void SetPathPreview(List<HexCoordinates> path)
        {
            _currentPath.Clear();
            if (path != null && path.Count > 1)
            {
                _currentPath.AddRange(path);
                if (_pathLineRenderer != null)
                {
                    _pathLineRenderer.positionCount = path.Count;
                    for (int i = 0; i < path.Count; i++)
                    {
                        var worldPos = path[i].ToWorldPosition(_grid.HexRadius, 0.15f);
                        _pathLineRenderer.SetPosition(i, worldPos);
                    }
                }
            }
            else
            {
                ClearPathPreview();
            }

            RefreshAllTileColors();
        }

        public void ClearPathPreview()
        {
            _currentPath.Clear();
            if (_pathLineRenderer != null)
            {
                _pathLineRenderer.positionCount = 0;
            }
            RefreshAllTileColors();
        }

        private void RefreshAllTileColors()
        {
            foreach (var kvp in _tileRenderers)
            {
                var coords = kvp.Key;
                var mr = kvp.Value;
                if (mr == null) continue;

                var node = _grid != null ? _grid.GetNode(coords) : null;
                bool isTextured = node != null && !string.IsNullOrEmpty(node.GroundTexture);

                Color c = isTextured ? Color.white : _defaultTileColor;

                for (int zi = 0; zi < _activeZones.Count; zi++)
                {
                    var z = _activeZones[zi];
                    if (coords.DistanceTo(z.Center) <= Math.Max(0, z.Radius))
                    {
                        Color zCol = Killtime.Core.Combat.SmokeScreen.ZoneThemeColor(z.Kind);
                        zCol.a = 1f;
                        c = isTextured ? Color.Lerp(Color.white, zCol, 0.45f) : Color.Lerp(_defaultTileColor, zCol, 0.55f);
                        break;
                    }
                }

                if (node != null && !node.IsWalkable)
                {
                    c = isTextured ? new Color(0.7f, 0.25f, 0.25f, 1f) : new Color(0.18f, 0.1f, 0.1f, 1f);
                }
                else if (_currentHovered.HasValue && _currentHovered.Value.Equals(coords))
                {
                    c = isTextured ? Color.Lerp(Color.white, _hoverTileColor, 0.65f) : _hoverTileColor;
                }
                else if (_currentTarget.HasValue && _currentTarget.Value.Equals(coords))
                {
                    c = isTextured ? Color.Lerp(Color.white, _targetTileColor, 0.70f) : _targetTileColor;
                }
                else if (_currentPath.Contains(coords))
                {
                    c = isTextured ? Color.Lerp(Color.white, _pathTileColor, 0.60f) : _pathTileColor;
                }
                else if (_currentReachable.Contains(coords))
                {
                    c = isTextured ? Color.Lerp(Color.white, _reachableTileColor, 0.50f) : _reachableTileColor;
                }

                // Cases bloquantes du cône de visée : teinte magenta prioritaire pour
                // trancher d'un coup d'œil (vue de dessus) ce qui bloque vraiment.
                if (_losBlockingCells.Contains(coords))
                {
                    c = Color.Lerp(c, new Color(1f, 0.1f, 0.6f, 1f), 0.75f);
                }

                // Brouillard de guerre (§21.4) : obscurité stricte hors du champ
                // 360° de l'escouade (portée d'ambiance + murs Full). Prioritaire sur
                // toute autre teinte (portée, chemin, couvert) : l'invisible
                // reste illisible, même au survol.
                if (_fogEnabled && !_fogVisible.Contains(coords))
                {
                    c = isTextured ? Color.Lerp(Color.white, _fogHiddenTint, 0.92f) : _fogHiddenTint;
                }

                _propBlock.Clear();
                _propBlock.SetColor("_BaseColor", c);
                _propBlock.SetColor("_Color", c);

                if (isTextured)
                {
                    var tex = LoadGroundTexture(node.GroundTexture);
                    if (tex != null)
                    {
                        _propBlock.SetTexture("_BaseMap", tex);
                        _propBlock.SetTexture("_MainTex", tex);
                    }
                }

                mr.SetPropertyBlock(_propBlock);
            }
        }
    }
}

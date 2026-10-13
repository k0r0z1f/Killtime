using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Killtime.Tactics.Grid
{
    public partial class HexGridVisualizer : MonoBehaviour
    {
        public void SetCeilingVisualMode(CeilingVisualMode mode)
        {
            CurrentCeilingMode = mode;
            _isCeilingPreviewActive = (mode == CeilingVisualMode.EditorCeilingMode);
            RefreshCeilingRenderers();
        }

        public void SetCeilingPreviewActive(bool active)
        {
            SetCeilingVisualMode(active ? CeilingVisualMode.EditorCeilingMode : CeilingVisualMode.EditorNonCeilingMode);
        }

        private Mesh _cachedFlatCeilingMesh;

        private void UpdateCeilingVisual(HexCoordinates coords, HexNode node)
        {
            if (node.HasCeiling)
            {
                if (!_ceilingRenderers.TryGetValue(coords, out var mr) || mr == null)
                {
                    if (_cachedFlatCeilingMesh == null)
                    {
                        _cachedFlatCeilingMesh = GenerateFlatPointyHexMesh(_grid.HexRadius * 0.95f);
                    }

                    var ceilObj = new GameObject($"Ceiling_{coords.Q}_{coords.R}");
                    ceilObj.transform.SetParent(_ceilingsParent, false);
                    ceilObj.transform.position = node.WorldPosition + Vector3.up * _grid.CeilingHeight;
                    ceilObj.layer = 2; // Ignore Raycast : jamais cliquable

                    var mf = ceilObj.AddComponent<MeshFilter>();
                    mf.sharedMesh = _cachedFlatCeilingMesh;

                    mr = ceilObj.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = _ceilingMaterial;
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                    mr.receiveShadows = false;
                    mr.SetPropertyBlock(_ceilingPropBlock);
                    _ceilingRenderers[coords] = mr;
                }
            }
            else
            {
                if (_ceilingRenderers.TryGetValue(coords, out var mr))
                {
                    if (mr != null) Destroy(mr.gameObject);
                    _ceilingRenderers.Remove(coords);
                }
            }
        }

        private GameObject _ceilingSelectorObj;
        private MeshRenderer _ceilingSelectorRenderer;
        private bool _isHoveringCeiling = false;

        private void EnsureCeilingSelector()
        {
            if (_ceilingSelectorObj != null) return;

            _ceilingSelectorObj = new GameObject("CeilingHoverSelector");
            _ceilingSelectorObj.transform.SetParent(transform, false);
            _ceilingSelectorObj.layer = 2;

            var mf = _ceilingSelectorObj.AddComponent<MeshFilter>();
            mf.sharedMesh = GenerateFlatPointyHexMesh(_grid != null ? _grid.HexRadius * 0.98f : 0.98f);

            _ceilingSelectorRenderer = _ceilingSelectorObj.AddComponent<MeshRenderer>();
            _ceilingSelectorRenderer.sharedMaterial = _ceilingMaterial;
            _ceilingSelectorRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _ceilingSelectorRenderer.receiveShadows = false;

            _ceilingSelectorObj.SetActive(false);
        }

        private void RefreshCeilingRenderers()
        {
            EnsureCeilingSelector();

            float activeAlpha;
            Color defaultCeilCol;

            if (CurrentCeilingMode == CeilingVisualMode.EditorCeilingMode)
            {
                activeAlpha = Mathf.Clamp(_ceilingOpacity * 1.5f + 0.35f, 0.52f, 0.78f);
                defaultCeilCol = new Color(0.12f, 0.75f, 0.98f, activeAlpha);
            }
            else if (CurrentCeilingMode == CeilingVisualMode.EditorNonCeilingMode)
            {
                activeAlpha = Mathf.Clamp(_ceilingOpacity, 0.22f, 0.65f);
                defaultCeilCol = new Color(0.12f, 0.75f, 0.98f, activeAlpha);
            }
            else
            {
                activeAlpha = Mathf.Clamp(_ceilingOpacity * 0.85f, 0.18f, 0.55f);
                defaultCeilCol = new Color(0.10f, 0.70f, 0.95f, activeAlpha);
            }

            bool isVisible = (CurrentCeilingMode != CeilingVisualMode.InGame) || _showCeilingInGame;

            if (_ceilingMaterial != null)
            {
                if (_ceilingMaterial.HasProperty("_BaseColor")) _ceilingMaterial.SetColor("_BaseColor", defaultCeilCol);
                if (_ceilingMaterial.HasProperty("_Color")) _ceilingMaterial.SetColor("_Color", defaultCeilCol);
            }

            if (_ceilingPropBlock == null) _ceilingPropBlock = new MaterialPropertyBlock();
            _ceilingPropBlock.SetColor("_BaseColor", defaultCeilCol);
            _ceilingPropBlock.SetColor("_Color", defaultCeilCol);

            foreach (var kvp in _ceilingRenderers)
            {
                var coords = kvp.Key;
                var mr = kvp.Value;
                if (mr == null) continue;

                mr.enabled = isVisible;
                if (isVisible)
                {
                    if (_isHoveringCeiling && _currentHovered.HasValue && _currentHovered.Value.Equals(coords))
                    {
                        var hoverBlock = new MaterialPropertyBlock();
                        Color hoverCol = new Color(0.0f, 1.0f, 1.0f, Mathf.Clamp01(activeAlpha + 0.35f));
                        hoverBlock.SetColor("_BaseColor", hoverCol);
                        hoverBlock.SetColor("_Color", hoverCol);
                        mr.SetPropertyBlock(hoverBlock);
                    }
                    else
                    {
                        mr.SetPropertyBlock(_ceilingPropBlock);
                    }
                }
            }

            if (_ceilingSelectorObj != null)
            {
                if (isVisible && _isHoveringCeiling && _currentHovered.HasValue && _grid != null)
                {
                    var node = _grid.GetNode(_currentHovered.Value);
                    if (node != null)
                    {
                        _ceilingSelectorObj.transform.position = node.WorldPosition + Vector3.up * (_grid.CeilingHeight + 0.015f);

                        var selBlock = new MaterialPropertyBlock();
                        Color selCol = new Color(0.0f, 1.0f, 1.0f, 0.92f);
                        selBlock.SetColor("_BaseColor", selCol);
                        selBlock.SetColor("_Color", selCol);
                        _ceilingSelectorRenderer.SetPropertyBlock(selBlock);
                        _ceilingSelectorObj.SetActive(true);
                    }
                    else
                    {
                        _ceilingSelectorObj.SetActive(false);
                    }
                }
                else
                {
                    _ceilingSelectorObj.SetActive(false);
                }
            }

            TacticalCeilingProp.UpdateAllVisualModes(CurrentCeilingMode, activeAlpha, isVisible);
        }

        public void SetHoveredCoord(HexCoordinates? coord, bool isCeilingLevel = false)
        {
            if (_currentHovered.Equals(coord) && _isHoveringCeiling == isCeilingLevel) return;
            _currentHovered = coord;
            _isHoveringCeiling = isCeilingLevel;
            RefreshAllTileColors();
            RefreshCeilingRenderers();
        }
    }
}

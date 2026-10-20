using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Killtime.Tactics.Grid
{
    public partial class HexGridVisualizer : MonoBehaviour
    {
        public void RefreshObstacles()
        {
            if (_grid == null) return;

            float hexRadius = _grid.HexRadius * 0.95f;

            foreach (var kvp in _grid.Nodes)
            {
                var coords = kvp.Key;
                var node = kvp.Value;

                if (_tileRenderers.TryGetValue(coords, out var mr) && mr != null)
                {
                    mr.transform.position = node.WorldPosition;
                    var mesh = GenerateSculptedTileMesh(_grid, coords, node, hexRadius, _smoothSlopeTerrain, _groundTiling);

                    var mf = mr.GetComponent<MeshFilter>();
                    if (mf != null) mf.sharedMesh = mesh;

                    var col = mr.GetComponent<MeshCollider>();
                    if (col != null) col.sharedMesh = mesh;

                    mr.sharedMaterial = GetOrCreateGroundMaterial(node.GroundTexture);
                }

                UpdateCeilingVisual(coords, node);
                UpdateObstacleVisual(coords, node);
            }

            RefreshAllTileColors();
            RefreshCeilingRenderers();

            // Registre ligne de mire synchronisé à la source : les bornes testées
            // sont toujours celles des meshes affichés (sculpture, pose, layout).
            try { PropObstacleRegistry.Rebuild(_grid.HexRadius); }
            catch { /* ignore */ }
        }

        private void UpdateObstacleVisual(HexCoordinates coords, HexNode node)
        {
            if (_obstacleObjects.TryGetValue(coords, out var existing))
            {
                Destroy(existing);
                _obstacleObjects.Remove(coords);
            }

            if (node.HasCustomVisual) return;

            if (node.Cover == CoverType.Half)
            {
                var halfCover = GameObject.CreatePrimitive(PrimitiveType.Cube);
                halfCover.name = $"CoverHalf_{coords.Q}_{coords.R}";
                halfCover.transform.SetParent(_obstaclesParent, false);
                halfCover.transform.position = node.WorldPosition + Vector3.up * 0.35f;
                halfCover.transform.localScale = new Vector3(_grid.HexRadius * 0.8f, 0.7f, _grid.HexRadius * 0.4f);

                var rend = halfCover.GetComponent<MeshRenderer>();
                rend.sharedMaterial = _obstacleMaterial;

                _propBlock.SetColor("_BaseColor", new Color(0.75f, 0.55f, 0.15f));
                _propBlock.SetColor("_Color", new Color(0.75f, 0.55f, 0.15f));
                rend.SetPropertyBlock(_propBlock);

                _obstacleObjects[coords] = halfCover;
            }
            else if (node.Cover == CoverType.ThreeQuarters)
            {
                // Livre V §21.3 : barricade haute (3/4 couvert, -2) — plus haute
                // que le muret Half, moins massive que le mur Full.
                var tqCover = GameObject.CreatePrimitive(PrimitiveType.Cube);
                tqCover.name = $"CoverThreeQuarters_{coords.Q}_{coords.R}";
                tqCover.transform.SetParent(_obstaclesParent, false);
                tqCover.transform.position = node.WorldPosition + Vector3.up * 0.6f;
                tqCover.transform.localScale = new Vector3(_grid.HexRadius * 0.8f, 1.2f, _grid.HexRadius * 0.4f);

                var rend = tqCover.GetComponent<MeshRenderer>();
                rend.sharedMaterial = _obstacleMaterial;

                _propBlock.SetColor("_BaseColor", new Color(0.85f, 0.45f, 0.12f));
                _propBlock.SetColor("_Color", new Color(0.85f, 0.45f, 0.12f));
                rend.SetPropertyBlock(_propBlock);

                _obstacleObjects[coords] = tqCover;
            }
            else if (node.Cover == CoverType.Full)
            {
                var fullCover = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                fullCover.name = $"CoverFull_{coords.Q}_{coords.R}";
                fullCover.transform.SetParent(_obstaclesParent, false);
                fullCover.transform.position = node.WorldPosition + Vector3.up * 1.1f;
                fullCover.transform.localScale = new Vector3(_grid.HexRadius * 0.8f, 1.1f, _grid.HexRadius * 0.8f);

                var rend = fullCover.GetComponent<MeshRenderer>();
                rend.sharedMaterial = _obstacleMaterial;

                _propBlock.SetColor("_BaseColor", new Color(0.25f, 0.28f, 0.35f));
                _propBlock.SetColor("_Color", new Color(0.25f, 0.28f, 0.35f));
                rend.SetPropertyBlock(_propBlock);

                _obstacleObjects[coords] = fullCover;
            }
        }

        /// <summary>
        /// Bornes monde réelles des meshes d'obstacles affichés (murets, barricades,
        /// murs) : la vérité visible. Sert au cône de visée pour garantir que tout
        /// rayon traversant un mesh affiché est bloqué (Livre V §21.3).
        /// </summary>
        public System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<HexCoordinates, Bounds>> GetObstacleMeshBounds()
        {
            var list = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<HexCoordinates, Bounds>>();
            if (_obstacleObjects == null) return list;
            foreach (var kvp in _obstacleObjects)
            {
                if (kvp.Value == null) continue;
                var rend = kvp.Value.GetComponent<MeshRenderer>();
                if (rend == null) continue;
                Bounds b = rend.bounds;
                if (b.size.y < 0.01f) continue;
                list.Add(new System.Collections.Generic.KeyValuePair<HexCoordinates, Bounds>(kvp.Key, b));
            }
            return list;
        }
    }
}

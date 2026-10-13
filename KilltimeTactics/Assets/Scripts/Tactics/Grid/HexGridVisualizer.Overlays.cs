using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Killtime.Tactics.Grid
{
    public partial class HexGridVisualizer : MonoBehaviour
    {
        private void SetupPathLineRenderer()
        {
            var lineObj = new GameObject("PathPreviewLine");
            lineObj.transform.SetParent(transform);
            _pathLineRenderer = lineObj.AddComponent<LineRenderer>();
            _pathLineRenderer.material = _baseMaterial;
            _pathLineRenderer.startColor = new Color(0.2f, 0.9f, 0.5f, 0.8f);
            _pathLineRenderer.endColor = new Color(0.0f, 0.85f, 1.0f, 0.9f);
            _pathLineRenderer.startWidth = 0.12f;
            _pathLineRenderer.endWidth = 0.12f;
            _pathLineRenderer.positionCount = 0;
            _pathLineRenderer.useWorldSpace = true;
        }

        private void SetupLineOfSightRenderer()
        {
            var lineObj = new GameObject("LineOfSightDebug");
            lineObj.transform.SetParent(transform);
            _losLineRenderer = lineObj.AddComponent<LineRenderer>();
            _losLineRenderer.material = LosUnlitMaterial() ?? _baseMaterial;
            _losLineRenderer.startColor = new Color(0.2f, 1f, 0.6f, 0.9f);
            _losLineRenderer.endColor = new Color(0.2f, 1f, 0.6f, 0.9f);
            _losLineRenderer.startWidth = 0.09f;
            _losLineRenderer.endWidth = 0.09f;
            _losLineRenderer.positionCount = 0;
            _losLineRenderer.useWorldSpace = true;
        }

        // Matériau non éclairé partagé : les couleurs vert/rouge des rayons restent
        // lisibles quelles que soient la lumière et la teinte des tuiles.
        private static Material _losUnlitMaterial;

        private static Material LosUnlitMaterial()
        {
            if (_losUnlitMaterial == null)
            {
                try
                {
                    var shader = Shader.Find("Sprites/Default");
                    if (shader != null) _losUnlitMaterial = new Material(shader);
                }
                catch { _losUnlitMaterial = null; }
            }
            return _losUnlitMaterial;
        }

        /// <summary>
        /// Affiche les « raycasts » de détection du couvert (Livre VI §25.3) : le cône
        /// tiré depuis le meilleur coin de l'attaquant vers les 28 échantillons de la
        /// cible — rayon vert = passe, rouge = bloqué. Couleur de synthèse : vert à
        /// découvert, jaune moitié (-1), orange 3/4 (-2), rouge couvert total.
        /// Au contact (couvert ignoré) : simple segment vert.
        /// </summary>
        public void ShowLineOfSight(
            HexCoordinates from,
            HexCoordinates to,
            IReadOnlyList<Killtime.Core.Combat.SmokeZone> smokeZones = null,
            bool seesThroughSmoke = false)
        {
            if (_losLineRenderer == null || _grid == null)
            {
                ClearLineOfSight();
                return;
            }

            if (from.DistanceTo(to) <= 1)
            {
                DrawLosFan(new System.Collections.Generic.List<CoverRayHit>
                {
                    new CoverRayHit { Origin = EyePosition(from), Target = EyePosition(to), Blocked = false }
                }, CoverType.None);
                return;
            }

            CoverScanResult scan = CoverSystem.ScanCover(
                from,
                to,
                _grid,
                smokeZones: smokeZones ?? _activeZones,
                seesThroughSmoke: seesThroughSmoke);
            DrawLosFan(scan.Rays, scan.Cover);
        }

        private void DrawLosFan(System.Collections.Generic.List<CoverRayHit> rays, CoverType cover)
        {
            HideLosFan();
            _losBlockingCells.Clear();
            if (_losLineRenderer == null) return;

            // Éventail des rayons : vert = passe ; rouge = bloqué, tracé en rouge
            // jusqu'au point d'impact puis en ombre atténuée derrière l'obstacle.
            EnsureLosRayPool(rays.Count * 2);
            int li = 0;
            for (int i = 0; i < rays.Count && li < _losRayRenderers.Count; i++)
            {
                var ray = rays[i];
                if (ray.Blocked && ray.HasBlockingCell)
                    _losBlockingCells.Add(ray.BlockingCell);
                if (ray.Blocked && ray.HasHitPoint)
                {
                    var lr = _losRayRenderers[li++];
                    lr.gameObject.SetActive(true);
                    lr.startWidth = 0.035f;
                    lr.endWidth = 0.035f;
                    lr.positionCount = 2;
                    lr.SetPosition(0, ray.Origin);
                    lr.SetPosition(1, ray.HitPoint);
                    Color red = new Color(1f, 0.3f, 0.35f, 0.9f);
                    lr.startColor = red;
                    lr.endColor = red;

                    if (li < _losRayRenderers.Count)
                    {
                        var shadow = _losRayRenderers[li++];
                        shadow.gameObject.SetActive(true);
                        shadow.startWidth = 0.02f;
                        shadow.endWidth = 0.02f;
                        shadow.positionCount = 2;
                        shadow.SetPosition(0, ray.HitPoint);
                        shadow.SetPosition(1, ray.Target);
                        Color shade = new Color(0.5f, 0.12f, 0.14f, 0.3f);
                        shadow.startColor = shade;
                        shadow.endColor = shade;
                    }
                }
                else
                {
                    var lr = _losRayRenderers[li++];
                    lr.gameObject.SetActive(true);
                    lr.startWidth = 0.035f;
                    lr.endWidth = 0.035f;
                    lr.positionCount = 2;
                    lr.SetPosition(0, ray.Origin);
                    lr.SetPosition(1, ray.Target);
                    Color rc = ray.Blocked
                        ? new Color(1f, 0.3f, 0.35f, 0.85f)
                        : new Color(0.35f, 1f, 0.6f, 0.7f);
                    lr.startColor = rc;
                    lr.endColor = rc;
                }
            }

            // 2) Ligne de synthèse (origine ➔ centre cible), couleur du couvert.
            if (rays.Count > 0)
            {
                _losLineRenderer.positionCount = 2;
                _losLineRenderer.SetPosition(0, rays[0].Origin);
                Vector3 mid = Vector3.zero;
                for (int i = 0; i < rays.Count; i++) mid += rays[i].Target;
                _losLineRenderer.SetPosition(1, mid / rays.Count);
                Color c = CoverLineColor(cover);
                _losLineRenderer.startColor = c;
                _losLineRenderer.endColor = c;
            }
            else
            {
                _losLineRenderer.positionCount = 0;
            }

            // 3) Teinte les cases bloquantes (magenta) : en vue de dessus, on voit
            // exactement quels hexagones le cône traverse — arbitre perspective.
            RefreshAllTileColors();
        }

        private void EnsureLosRayPool(int count)
        {
            while (_losRayRenderers.Count < count)
            {
                var rayObj = new GameObject($"LineOfSightRay_{_losRayRenderers.Count}");
                rayObj.transform.SetParent(transform);
                var lr = rayObj.AddComponent<LineRenderer>();
                lr.material = LosUnlitMaterial() ?? _baseMaterial;
                lr.startWidth = 0.035f;
                lr.endWidth = 0.035f;
                lr.positionCount = 0;
                lr.useWorldSpace = true;
                _losRayRenderers.Add(lr);
            }
        }

        private void HideLosFan()
        {
            for (int i = 0; i < _losRayRenderers.Count; i++)
            {
                if (_losRayRenderers[i] != null)
                {
                    _losRayRenderers[i].positionCount = 0;
                    _losRayRenderers[i].gameObject.SetActive(false);
                }
            }
        }

        public void ClearLineOfSight()
        {
            if (_losLineRenderer != null)
                _losLineRenderer.positionCount = 0;
            HideLosFan();
            if (_losBlockingCells.Count > 0)
            {
                _losBlockingCells.Clear();
                RefreshAllTileColors();
            }
        }

        private Vector3 EyePosition(HexCoordinates coords)
        {
            var node = _grid != null ? _grid.GetNode(coords) : null;
            Vector3 basePos = node != null
                ? node.WorldPosition
                : coords.ToWorldPosition(_grid != null ? _grid.HexRadius : 1f, 0.05f);
            return basePos + Vector3.up * 1.35f;
        }

        private static Color CoverLineColor(CoverType cover)
        {
            return cover switch
            {
                CoverType.Half => new Color(1f, 0.85f, 0.2f, 0.95f),
                CoverType.ThreeQuarters => new Color(1f, 0.55f, 0.15f, 0.95f),
                CoverType.Full => new Color(1f, 0.25f, 0.3f, 0.95f),
                _ => new Color(0.2f, 1f, 0.6f, 0.9f),
            };
        }

        /// <summary>
        /// Met à jour les surfaces de zones persistantes affichées au sol.
        /// </summary>
        public void UpdatePersistentZones(IReadOnlyList<Killtime.Core.Combat.SmokeZone> zones)
        {
            _activeZones.Clear();
            if (zones != null)
            {
                for (int i = 0; i < zones.Count; i++)
                {
                    if (zones[i].TurnsLeft > 0)
                        _activeZones.Add(zones[i]);
                }
            }

            if (_zonesParent == null)
            {
                _zonesParent = new GameObject("HexZones").transform;
                _zonesParent.SetParent(transform, false);
            }

            foreach (var kvp in _zoneVisualObjects)
            {
                if (kvp.Value != null) Destroy(kvp.Value);
            }
            _zoneVisualObjects.Clear();

            if (_grid == null || _cachedFlatCeilingMesh == null)
            {
                _cachedFlatCeilingMesh = GenerateFlatPointyHexMesh(_grid != null ? _grid.HexRadius * 0.95f : 0.95f);
            }

            var coveredCoords = new HashSet<HexCoordinates>();
            for (int zi = 0; zi < _activeZones.Count; zi++)
            {
                var zone = _activeZones[zi];
                int rad = Math.Max(0, zone.Radius);
                Color zoneCol = Killtime.Core.Combat.SmokeScreen.ZoneThemeColor(zone.Kind);

                for (int q = -rad; q <= rad; q++)
                {
                    int r1 = Mathf.Max(-rad, -q - rad);
                    int r2 = Mathf.Min(rad, -q + rad);
                    for (int r = r1; r <= r2; r++)
                    {
                        var c = new HexCoordinates(zone.Center.Q + q, zone.Center.R + r);
                        if (!coveredCoords.Add(c)) continue;

                        var node = _grid.GetNode(c);
                        if (node == null) continue;

                        var overlay = new GameObject($"ZoneOverlay_{c.Q}_{c.R}");
                        overlay.transform.SetParent(_zonesParent, false);
                        overlay.transform.position = node.WorldPosition + Vector3.up * 0.035f;
                        overlay.layer = 2;

                        var mf = overlay.AddComponent<MeshFilter>();
                        mf.sharedMesh = _cachedFlatCeilingMesh;

                        var mr = overlay.AddComponent<MeshRenderer>();
                        mr.sharedMaterial = _ceilingMaterial;
                        mr.shadowCastingMode = ShadowCastingMode.Off;
                        mr.receiveShadows = false;

                        var block = new MaterialPropertyBlock();
                        block.SetColor("_BaseColor", zoneCol);
                        block.SetColor("_Color", zoneCol);
                        mr.SetPropertyBlock(block);

                        _zoneVisualObjects[c] = overlay;
                    }
                }
            }

            RefreshAllTileColors();
        }
    }
}

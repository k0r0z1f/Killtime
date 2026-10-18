using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Audio;
using Killtime.CameraSystem;
using Killtime.Tactics.Units;
using Killtime.Tactics.Grid;
using Killtime.Tactics.CombatUI;
using Killtime.Core.Character;
using Killtime.Core.Combat;

namespace Killtime.Tactics.DuoTech
{
    // Split move-only de DuoTechWeaveController — Highlight : GetIncludedTargets, GetDuoTechHighlightColor, UpdateTargetHighlights, ApplyTargetHighlight, RemoveTargetHighlight, ClearAllTargetHighlights, CreateGroundTargetRing.
    public partial class DuoTechWeaveController : MonoBehaviour
    {
        // ============================ HIGHLIGHT DES CIBLES ============================

        private List<TacticalUnit> GetIncludedTargets()
        {
            var caught = new List<TacticalUnit>();
            if (_def == null || _t1.Count < 2) return caught;
            var enemies = EnemiesAlive();

            switch (_def.Id)
            {
                case DuoTechId.SillageIgne:
                    for (int i = 0; i < enemies.Count; i++)
                    {
                        if (DistToPolyXZ(enemies[i].transform.position, _t1) <= SillageCatchDist)
                            caught.Add(enemies[i]);
                    }
                    break;

                case DuoTechId.LacetNytharite:
                    if (_t1.Count >= 3)
                    {
                        Vector3 centroid = Vector3.zero;
                        for (int i = 0; i < _t1.Count; i++) centroid += _t1[i];
                        centroid /= Mathf.Max(1, _t1.Count);

                        TacticalUnit best = null;
                        float bestD = float.MaxValue;
                        for (int i = 0; i < enemies.Count; i++)
                        {
                            float d = Vector3.Distance(
                                new Vector3(enemies[i].transform.position.x, 0f, enemies[i].transform.position.z),
                                new Vector3(centroid.x, 0f, centroid.z));
                            if (d < bestD) { bestD = d; best = enemies[i]; }
                        }
                        if (best != null && bestD <= LassoTargetDist)
                            caught.Add(best);
                    }
                    break;

                case DuoTechId.FournaiseRetardement:
                    if (_t1.Count >= 3)
                    {
                        Vector3 centroid = Vector3.zero;
                        for (int i = 0; i < _t1.Count; i++) centroid += _t1[i];
                        centroid /= Mathf.Max(1, _t1.Count);

                        TacticalUnit best = null;
                        float bestD = float.MaxValue;
                        for (int i = 0; i < enemies.Count; i++)
                        {
                            float d = Vector3.Distance(
                                new Vector3(enemies[i].transform.position.x, 0f, enemies[i].transform.position.z),
                                new Vector3(centroid.x, 0f, centroid.z));
                            if (d < bestD) { bestD = d; best = enemies[i]; }
                        }
                        if (best != null && bestD <= FournaiseTargetDist)
                            caught.Add(best);
                    }
                    break;

                case DuoTechId.TrempeInversee:
                    // Groupés : tous les ennemis à portée du centroïde T1.
                    if (_t1.Count >= 3)
                    {
                        Vector3 centroid = Vector3.zero;
                        for (int i = 0; i < _t1.Count; i++) centroid += _t1[i];
                        centroid /= Mathf.Max(1, _t1.Count);
                        Vector2 c2 = new Vector2(centroid.x, centroid.z);
                        for (int i = 0; i < enemies.Count; i++)
                        {
                            Vector2 e2 = new Vector2(enemies[i].transform.position.x, enemies[i].transform.position.z);
                            if (Vector2.Distance(e2, c2) <= TrempeCatchDist)
                                caught.Add(enemies[i]);
                        }
                    }
                    break;

                case DuoTechId.RailgunArtisanale:
                    // Cible dure au bout du rail (fin de T1).
                    if (_t1.Count >= 2)
                    {
                        Vector3 end = _t1[_t1.Count - 1];
                        TacticalUnit best = null;
                        float bestD = float.MaxValue;
                        for (int i = 0; i < enemies.Count; i++)
                        {
                            float d = Vector3.Distance(
                                new Vector3(enemies[i].transform.position.x, 0f, enemies[i].transform.position.z),
                                new Vector3(end.x, 0f, end.z));
                            if (d < bestD) { bestD = d; best = enemies[i]; }
                        }
                        if (best != null && bestD <= RailgunTargetDist)
                            caught.Add(best);
                    }
                    break;

                case DuoTechId.CisailleEntropique:
                    // Intersection du X : croisement T1/T2 si T2 tracé, sinon proximité T1.
                    if (_t1.Count >= 2)
                    {
                        var w1 = ToVec2(_t1);
                        if (_t2.Count >= 2)
                        {
                            var w2 = ToVec2(_t2);
                            if (DuoTechGeometry.TryFindCrossingPoint(w1, w2, CisailleCrossGap, out WeaveVec2 cross))
                            {
                                for (int i = 0; i < enemies.Count; i++)
                                {
                                    var ep = new WeaveVec2(enemies[i].transform.position.x, enemies[i].transform.position.z);
                                    if (WeaveVec2.Distance(ep, cross) <= CisailleCatchDist)
                                        caught.Add(enemies[i]);
                                }
                            }
                        }
                        else
                        {
                            for (int i = 0; i < enemies.Count; i++)
                            {
                                if (DistToPolyXZ(enemies[i].transform.position, _t1) <= SillageCatchDist)
                                    caught.Add(enemies[i]);
                            }
                        }
                    }
                    break;

                case DuoTechId.Parallaxe:
                    // Opposition 180° : Mina et Lucas de part et d'autre de la cible.
                    if (_mina != null && _lucas != null)
                    {
                        var minaP = new WeaveVec2(_mina.transform.position.x, _mina.transform.position.z);
                        var lucasP = new WeaveVec2(_lucas.transform.position.x, _lucas.transform.position.z);
                        for (int i = 0; i < enemies.Count; i++)
                        {
                            var tp = new WeaveVec2(enemies[i].transform.position.x, enemies[i].transform.position.z);
                            if (WeaveVec2.Distance(minaP, tp) > ParallaxeTargetDist * 3f
                                && WeaveVec2.Distance(lucasP, tp) > ParallaxeTargetDist * 3f)
                                continue;
                            if (DuoTechGeometry.AreOpposed180(minaP, lucasP, tp, ParallaxeMinDot))
                                caught.Add(enemies[i]);
                        }
                        // Repli : si aucune opposition stricte, surligne le plus proche du centroïde T1.
                        if (caught.Count == 0 && _t1.Count >= 3)
                        {
                            Vector3 centroid = Vector3.zero;
                            for (int k = 0; k < _t1.Count; k++) centroid += _t1[k];
                            centroid /= Mathf.Max(1, _t1.Count);
                            TacticalUnit best = null;
                            float bestD = float.MaxValue;
                            for (int i = 0; i < enemies.Count; i++)
                            {
                                float d = Vector3.Distance(
                                    new Vector3(enemies[i].transform.position.x, 0f, enemies[i].transform.position.z),
                                    new Vector3(centroid.x, 0f, centroid.z));
                                if (d < bestD) { bestD = d; best = enemies[i]; }
                            }
                            if (best != null && bestD <= ParallaxeTargetDist)
                                caught.Add(best);
                        }
                    }
                    break;

                case DuoTechId.MirageDeuxTemps:
                    // Défensif : aucune cible ennemie à surligner.
                    break;

                case DuoTechId.UppercutThermobarique:
                    if (_t1.Count >= 3)
                    {
                        Vector3 centroid = Vector3.zero;
                        for (int i = 0; i < _t1.Count; i++) centroid += _t1[i];
                        centroid /= Mathf.Max(1, _t1.Count);
                        TacticalUnit best = null;
                        float bestD = float.MaxValue;
                        for (int i = 0; i < enemies.Count; i++)
                        {
                            float d = Vector3.Distance(
                                new Vector3(enemies[i].transform.position.x, 0f, enemies[i].transform.position.z),
                                new Vector3(centroid.x, 0f, centroid.z));
                            if (d < bestD) { bestD = d; best = enemies[i]; }
                        }
                        if (best != null && bestD <= UppercutTargetDist)
                            caught.Add(best);
                    }
                    break;
            }

            return caught;
        }

        private Color GetDuoTechHighlightColor()
        {
            if (_def == null) return new Color(1f, 0.45f, 0.1f);
            switch (_def.Id)
            {
                case DuoTechId.SillageIgne:
                    return new Color(1f, 0.35f, 0.15f); // Flamme éclatante
                case DuoTechId.LacetNytharite:
                    return new Color(0.2f, 0.9f, 1f);   // Cyan Nytharite
                case DuoTechId.FournaiseRetardement:
                    return new Color(1f, 0.8f, 0.2f);   // Ambre thermique
                case DuoTechId.TrempeInversee:
                    return new Color(0.5f, 0.9f, 1f);   // Gel inversé
                case DuoTechId.RailgunArtisanale:
                    return new Color(0.7f, 0.4f, 1f);   // Arc artisanal
                case DuoTechId.CisailleEntropique:
                    return new Color(1f, 0.2f, 0.4f);   // Entropie
                case DuoTechId.Parallaxe:
                    return new Color(1f, 0.85f, 0.3f);  // Parallaxe or
                case DuoTechId.MirageDeuxTemps:
                    return new Color(0.7f, 0.7f, 0.8f);  // Spectral
                case DuoTechId.UppercutThermobarique:
                    return new Color(1f, 0.5f, 0.1f);   // Thermobarique
                default:
                    return new Color(1f, 0.45f, 0.1f);
            }
        }

        private void UpdateTargetHighlights()
        {
            var included = GetIncludedTargets();
            Color hlColor = GetDuoTechHighlightColor();

            // Retrait des unités qui ne sont plus dans la trajectoire
            for (int i = _highlightedTargets.Count - 1; i >= 0; i--)
            {
                var u = _highlightedTargets[i];
                if (!included.Contains(u))
                {
                    RemoveTargetHighlight(u);
                    _highlightedTargets.RemoveAt(i);
                }
            }

            // Ajout des nouvelles unités incluses
            for (int i = 0; i < included.Count; i++)
            {
                var u = included[i];
                if (!_highlightedTargets.Contains(u))
                {
                    _highlightedTargets.Add(u);
                    ApplyTargetHighlight(u, hlColor);
                }
            }

            // Mise à jour de la pulsation et position des anneaux holographiques au sol
            float pulseScale = 0.84f + 0.05f * Mathf.Sin(Time.time * 6f);
            for (int i = 0; i < _highlightedTargets.Count; i++)
            {
                var u = _highlightedTargets[i];
                if (u != null && _targetRings.TryGetValue(u, out var ringObj) && ringObj != null)
                {
                    ringObj.transform.position = u.transform.position + Vector3.up * 0.04f;
                    ringObj.transform.localScale = new Vector3(pulseScale, 1f, pulseScale);
                }
            }
        }

        private void ApplyTargetHighlight(TacticalUnit unit, Color color)
        {
            if (unit == null) return;

            // 1. Surbrillance du corps et des textures de l'avatar
            var vis = unit.GetComponent<TacticalUnitVisual>();
            vis?.SetDuoTechTargetHighlight(true, color);

            // 2. Anneau ciblage holographique au sol avec shader standard + passe X-Ray
            if (!_targetRings.ContainsKey(unit))
            {
                var ringGo = CreateGroundTargetRing(unit, color);
                _targetRings[unit] = ringGo;
            }
        }

        private void RemoveTargetHighlight(TacticalUnit unit)
        {
            if (unit == null) return;

            var vis = unit.GetComponent<TacticalUnitVisual>();
            vis?.SetDuoTechTargetHighlight(false, Color.white);

            if (_targetRings.TryGetValue(unit, out var ringGo) && ringGo != null)
            {
                Destroy(ringGo);
            }
            _targetRings.Remove(unit);
        }

        private void ClearAllTargetHighlights()
        {
            for (int i = 0; i < _highlightedTargets.Count; i++)
            {
                var u = _highlightedTargets[i];
                if (u != null)
                {
                    var vis = u.GetComponent<TacticalUnitVisual>();
                    vis?.SetDuoTechTargetHighlight(false, Color.white);
                }
            }
            _highlightedTargets.Clear();

            foreach (var kvp in _targetRings)
            {
                if (kvp.Value != null) Destroy(kvp.Value);
            }
            _targetRings.Clear();
        }

        private GameObject CreateGroundTargetRing(TacticalUnit unit, Color color)
        {
            var root = new GameObject($"DuoTargetRing_{unit.name}");
            root.transform.position = unit.transform.position + Vector3.up * 0.08f;

            int segments = 36;
            float radius = 0.95f;
            Vector3[] points = new Vector3[segments];
            for (int i = 0; i < segments; i++)
            {
                float rad = (i / (float)segments) * Mathf.PI * 2f;
                points[i] = new Vector3(Mathf.Cos(rad) * radius, 0f, Mathf.Sin(rad) * radius);
            }

            // 1. Anneau X-Ray visible en premier plan en transparence (Queue 3100)
            var occGo = new GameObject("Ring_Occluded");
            occGo.transform.SetParent(root.transform, false);
            var lrOcc = occGo.AddComponent<LineRenderer>();
            lrOcc.useWorldSpace = false;
            lrOcc.loop = true;
            lrOcc.positionCount = segments;
            lrOcc.SetPositions(points);
            lrOcc.startWidth = 0.10f;
            lrOcc.endWidth = 0.10f;
            lrOcc.numCapVertices = 4;
            lrOcc.numCornerVertices = 4;

            lrOcc.sortingOrder = 100;

            Color occColor = new Color(
                Mathf.Min(1f, color.r * 1.15f), 
                Mathf.Min(1f, color.g * 1.15f), 
                Mathf.Min(1f, color.b * 1.15f), 
                0.55f);
            lrOcc.startColor = occColor;
            lrOcc.endColor = occColor;

            var baseOccMat = Resources.Load<Material>("Materials/DuoTechLine_Occluded");
            Shader occShader = (baseOccMat != null && baseOccMat.shader != null)
                            ? baseOccMat.shader
                            : (Shader.Find("Killtime/DuoTechLineOccluded") ?? Shader.Find("Sprites/Default"));
            var occMat = baseOccMat != null ? new Material(baseOccMat) : new Material(occShader);
            occMat.SetColor("_BaseColor", Color.white);
            if (occMat.HasProperty("_Color")) occMat.SetColor("_Color", Color.white);
            occMat.renderQueue = 3100;
            lrOcc.material = occMat;

            // 2. Anneau visible en vue directe (Queue 3110)
            var frontGo = new GameObject("Ring_Front");
            frontGo.transform.SetParent(root.transform, false);
            var lrFront = frontGo.AddComponent<LineRenderer>();
            lrFront.useWorldSpace = false;
            lrFront.loop = true;
            lrFront.positionCount = segments;
            lrFront.SetPositions(points);
            lrFront.startWidth = 0.10f;
            lrFront.endWidth = 0.10f;
            lrFront.numCapVertices = 4;
            lrFront.numCornerVertices = 4;
            lrFront.sortingOrder = 101;
            Color solidColor = new Color(color.r, color.g, color.b, 1f);
            lrFront.startColor = solidColor;
            lrFront.endColor = solidColor;

            var baseFrontMat = Resources.Load<Material>("Materials/DuoTechLine_Front");
            Shader frontShader = (baseFrontMat != null && baseFrontMat.shader != null)
                              ? baseFrontMat.shader
                              : (Shader.Find("Killtime/DuoTechLine") ?? Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"));
            var frontMat = baseFrontMat != null ? new Material(baseFrontMat) : new Material(frontShader);
            frontMat.SetColor("_BaseColor", Color.white);
            if (frontMat.HasProperty("_Color")) frontMat.SetColor("_Color", Color.white);
            frontMat.renderQueue = 3110;
            lrFront.material = frontMat;

            return root;
        }
    }
}

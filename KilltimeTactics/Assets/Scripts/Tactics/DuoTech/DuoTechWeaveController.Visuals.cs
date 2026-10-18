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
    // Split move-only de DuoTechWeaveController — Visuals : BuildVisuals, BuildWaveSpherePair, BuildLinePair, ClearLine, GroundPoint, SampleInto, PathLength, FinishT1, AnimateWave, UpdateSyncTrace, IsCurrentDrawingSyncPerfect, PointAlong.
    public partial class DuoTechWeaveController : MonoBehaviour
    {
        private void BuildVisuals()
        {
            // T1 (Mina) : magenta / rose arcanique vibrant (Sillage Igné) ou émeraude
            Color t1Color = (_def != null && _def.Id == DuoTechId.SillageIgne)
                ? new Color(1f, 0.15f, 0.85f, 0.95f)
                : new Color(0.2f, 1f, 0.65f, 0.95f);

            // T2 (Lucas) : ambre / feu solaire éclatant
            Color t2Color = (_def != null && _def.Id == DuoTechId.SillageIgne)
                ? new Color(1f, 0.55f, 0.1f, 0.95f)
                : new Color(1f, 0.45f, 0.1f, 0.95f);

            (_lineT1_Front, _lineT1_Occluded) = BuildLinePair("DuoWeaveLine_T1", t1Color, 0.22f);
            (_lineT2_Front, _lineT2_Occluded) = BuildLinePair("DuoWeaveLine_T2", t2Color, 0.22f);

            // 1. Onde cyan existante (Mina)
            (_waveMarker, _waveMarkerOccluded) = BuildWaveSpherePair(
                "DuoWaveMarker_Cyan", 
                new Color(0.35f, 0.95f, 1f, 1f), 
                0.36f);

            // 2. Onde verte de synchronisme parfait (anticipation pour T2 Lucas)
            (_waveMarkerGreen, _waveMarkerGreenOccluded) = BuildWaveSpherePair(
                "DuoWaveMarker_Green", 
                new Color(0.25f, 1f, 0.45f, 1f), 
                0.36f);

            // 3. Ruban vert traçant la zone de synchronisme parfait sur la ligne
            (_lineSyncTrace_Front, _lineSyncTrace_Occluded) = BuildLinePair(
                "DuoSyncTrace", 
                new Color(0.25f, 1f, 0.45f, 0.95f), 
                0.28f);
        }

        private (GameObject front, GameObject occluded) BuildWaveSpherePair(string name, Color color, float size)
        {
            // Sphère occluse en transparence (Queue 3100, sortingOrder 100, ZTest Always)
            var occGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            occGo.name = name + "_Occluded";
            occGo.transform.localScale = Vector3.one * (size * 1.05f);
            occGo.transform.SetParent(transform);
            var rendOcc = occGo.GetComponent<Renderer>();
            if (rendOcc != null)
            {
                rendOcc.sortingOrder = 100;
                var baseOccMat = Resources.Load<Material>("Materials/DuoTechLine_Occluded");
                Shader sOcc = (baseOccMat != null && baseOccMat.shader != null)
                    ? baseOccMat.shader
                    : (Shader.Find("Killtime/DuoTechLineOccluded") ?? Shader.Find("Sprites/Default"));
                if (sOcc != null)
                {
                    var matOcc = baseOccMat != null ? new Material(baseOccMat) : new Material(sOcc);
                    Color occC = new Color(color.r, color.g, color.b, 0.55f);
                    matOcc.SetColor("_BaseColor", occC);
                    if (matOcc.HasProperty("_Color")) matOcc.SetColor("_Color", occC);
                    matOcc.renderQueue = 3100;
                    rendOcc.material = matOcc;
                }
            }
            var colOcc = occGo.GetComponent<Collider>();
            if (colOcc != null) Destroy(colOcc);
            occGo.SetActive(false);

            // Sphère visible en vue directe (Queue 3110, sortingOrder 101, ZTest LEqual)
            var frontGo = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            frontGo.name = name;
            frontGo.transform.localScale = Vector3.one * size;
            frontGo.transform.SetParent(transform);
            var rend = frontGo.GetComponent<Renderer>();
            if (rend != null)
            {
                rend.sortingOrder = 101;
                var baseFrontMat = Resources.Load<Material>("Materials/DuoTechLine_Front");
                Shader s = (baseFrontMat != null && baseFrontMat.shader != null)
                    ? baseFrontMat.shader
                    : (Shader.Find("Killtime/DuoTechLine") ?? Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Standard"));
                if (s != null)
                {
                    var mat = baseFrontMat != null ? new Material(baseFrontMat) : new Material(s);
                    mat.SetColor("_BaseColor", color);
                    if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
                    mat.renderQueue = 3110;
                    rend.material = mat;
                }
            }
            var col = frontGo.GetComponent<Collider>();
            if (col != null) Destroy(col);
            frontGo.SetActive(false);

            return (frontGo, occGo);
        }

        private (LineRenderer front, LineRenderer occluded) BuildLinePair(string name, Color color, float width)
        {
            // 1. Ligne X-Ray en premier plan (Queue 3100, ZTest Always) - peinte en transparence sur tout le parcours
            var occludedGo = new GameObject(name + "_Occluded");
            occludedGo.transform.SetParent(transform);
            var lrOccluded = occludedGo.AddComponent<LineRenderer>();
            lrOccluded.positionCount = 0;
            lrOccluded.startWidth = width;
            lrOccluded.endWidth = width;
            lrOccluded.numCapVertices = 4;
            lrOccluded.numCornerVertices = 4;
            lrOccluded.useWorldSpace = true;
            lrOccluded.sortingOrder = 100;

            Color occludedColor = new Color(
                Mathf.Min(1f, color.r * 1.15f), 
                Mathf.Min(1f, color.g * 1.15f), 
                Mathf.Min(1f, color.b * 1.15f), 
                0.55f);
            lrOccluded.startColor = occludedColor;
            lrOccluded.endColor = occludedColor;

            var baseOccMat = Resources.Load<Material>("Materials/DuoTechLine_Occluded");
            Shader occludedShader = (baseOccMat != null && baseOccMat.shader != null)
                                 ? baseOccMat.shader
                                 : (Shader.Find("Killtime/DuoTechLineOccluded") ?? Shader.Find("Sprites/Default"));
            var occludedMat = baseOccMat != null ? new Material(baseOccMat) : new Material(occludedShader);
            occludedMat.SetColor("_BaseColor", Color.white);
            if (occludedMat.HasProperty("_Color")) occludedMat.SetColor("_Color", Color.white);
            occludedMat.renderQueue = 3100;
            lrOccluded.material = occludedMat;

            // 2. Ligne en vue directe (Queue 3110, ZTest LEqual) - peinte par-dessus en 100% opaque là où c'est dégagé
            var frontGo = new GameObject(name + "_Front");
            frontGo.transform.SetParent(transform);
            var lrFront = frontGo.AddComponent<LineRenderer>();
            lrFront.positionCount = 0;
            lrFront.startWidth = width;
            lrFront.endWidth = width;
            lrFront.numCapVertices = 4;
            lrFront.numCornerVertices = 4;
            lrFront.useWorldSpace = true;
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

            return (lrFront, lrOccluded);
        }

        private static void ClearLine(LineRenderer front, LineRenderer occluded)
        {
            if (front != null) front.positionCount = 0;
            if (occluded != null) occluded.positionCount = 0;
        }

        private bool GroundPoint(out Vector3 point)
        {
            point = Vector3.zero;
            if (_cam == null)
            {
                _cam = Camera.main != null ? Camera.main : FindAnyObjectByType<Camera>();
                if (_cam == null) return false;
            }

            var ray = _cam.ScreenPointToRay(Input.mousePosition);
            if (_ground.Raycast(ray, out float dist))
            {
                // Projection pure et directe sur le plan horizontal : pas de déviation ni de saut vers les corps
                point = ray.GetPoint(dist) + Vector3.up * 0.25f;
                return true;
            }
            return false;
        }

        private void SampleInto(List<Vector3> pts, LineRenderer lrFront, LineRenderer lrOccluded)
        {
            if (!GroundPoint(out Vector3 p)) return;
            if (pts.Count == 0 || Vector3.Distance(pts[pts.Count - 1], p) >= SampleStep)
            {
                pts.Add(p);
                Vector3[] arr = pts.ToArray();
                if (lrFront != null)
                {
                    lrFront.positionCount = pts.Count;
                    lrFront.SetPositions(arr);
                }
                if (lrOccluded != null)
                {
                    lrOccluded.positionCount = pts.Count;
                    lrOccluded.SetPositions(arr);
                }
            }
        }

        private static float PathLength(List<Vector3> pts)
        {
            float len = 0f;
            for (int i = 1; i < pts.Count; i++)
                len += Vector3.Distance(pts[i - 1], pts[i]);
            return len;
        }

        private void FinishT1()
        {
            _t1Length = PathLength(_t1);
            if (_t1.Count < 2 || _t1Length < MinLength)
            {
                _arena?.Log("Duo-Tech : T1 trop court — recommencez le trait Mina (clic-drag).");
                _t1.Clear();
                ClearLine(_lineT1_Front, _lineT1_Occluded);
                ClearLine(_lineSyncTrace_Front, _lineSyncTrace_Occluded);
                ClearAllTargetHighlights();
                return;
            }
            _waveArc = 0f;
            _waveMarker.SetActive(true);
            if (_waveMarkerOccluded != null) _waveMarkerOccluded.SetActive(true);
            if (_waveMarkerGreen != null) _waveMarkerGreen.SetActive(true);
            if (_waveMarkerGreenOccluded != null) _waveMarkerGreenOccluded.SetActive(true);
            _phase = WeavePhase.WaitingT2;
            _arena?.Log("🌊 Onde Mina en loop — tracez <b>T2 (Lucas)</b> en anticipation du passage !");
        }

        private void AnimateWave()
        {
            if (_t1.Count < 2 || _waveMarker == null) return;
            float speed = _mina != null ? Mathf.Max(1f, _mina.MoveSpeed) : 4f;
            _waveArc += speed * Time.deltaTime;
            if (_t1Length > 0f) _waveArc %= _t1Length;

            // 1. Onde cyan existante (Mina)
            Vector3 posCyan = PointAlong(_t1, _waveArc) + Vector3.up * 0.3f;
            _waveMarker.transform.position = posCyan;
            if (_waveMarkerOccluded != null) _waveMarkerOccluded.transform.position = posCyan;

            // 2. Onde verte de synchronisme parfait (anticipation optimale pour T2 Lucas)
            if (_waveMarkerGreen != null && _t1Length > 0.1f)
            {
                float leadMin = DuoTechGeometry.PerfectLeadMin; // 0.15s
                float leadMax = DuoTechGeometry.PerfectLeadMax; // 0.60s
                float leadOpt = (leadMin + leadMax) * 0.5f;     // 0.375s d'anticipation

                float greenArc = (_waveArc + speed * leadOpt) % _t1Length;
                Vector3 posGreen = PointAlong(_t1, greenArc) + Vector3.up * 0.32f;
                _waveMarkerGreen.transform.position = posGreen;
                if (_waveMarkerGreenOccluded != null)
                    _waveMarkerGreenOccluded.transform.position = posGreen;

                // Pulsation dynamique et amplification si synchro parfaite en cours de tracé
                bool isPerfect = _phase == WeavePhase.DrawingT2 && IsCurrentDrawingSyncPerfect();
                float pulseFreq = isPerfect ? 14f : 8f;
                float pulseAmp = isPerfect ? 0.25f : 0.14f;
                float baseScale = isPerfect ? 0.42f : 0.36f;
                float pulse = 1f + pulseAmp * Mathf.Sin(Time.time * pulseFreq);

                _waveMarkerGreen.transform.localScale = Vector3.one * (baseScale * pulse);
                if (_waveMarkerGreenOccluded != null)
                    _waveMarkerGreenOccluded.transform.localScale = Vector3.one * ((baseScale + 0.03f) * pulse);

                // Tracé vert de la zone de synchronisme optimal sur la ligne
                UpdateSyncTrace(_waveArc + speed * leadMin, _waveArc + speed * leadMax);
            }
        }

        private void UpdateSyncTrace(float arcStartRaw, float arcEndRaw)
        {
            if (_t1.Count < 2 || _t1Length <= 0.1f) return;
            _syncTracePts.Clear();

            float arcStart = arcStartRaw % _t1Length;
            float span = arcEndRaw - arcStartRaw;

            int steps = 10;
            float step = span / steps;

            for (int i = 0; i <= steps; i++)
            {
                float a = arcStart + i * step;
                if (a > _t1Length) a = _t1Length;
                _syncTracePts.Add(PointAlong(_t1, a) + Vector3.up * 0.26f);
            }

            Vector3[] arr = _syncTracePts.ToArray();
            if (_lineSyncTrace_Front != null)
            {
                _lineSyncTrace_Front.positionCount = arr.Length;
                _lineSyncTrace_Front.SetPositions(arr);
            }
            if (_lineSyncTrace_Occluded != null)
            {
                _lineSyncTrace_Occluded.positionCount = arr.Length;
                _lineSyncTrace_Occluded.SetPositions(arr);
            }
        }

        private bool IsCurrentDrawingSyncPerfect()
        {
            if (_t2.Count < 2 || _t1.Count < 2) return false;
            var w1 = ToVec2(_t1);
            var w2 = ToVec2(_t2);
            if (!DuoTechGeometry.TryFindCrossing(w1, w2, SillageCrossGap, out float a1, out float a2))
                return false;
            float overlap = DuoTechGeometry.OverlapFraction(w1, w2, SillageOverlapTol);
            if (overlap < 0.25f) return false;
            float minaSpeed = Mathf.Max(1f, _mina != null ? _mina.MoveSpeed : 4f);
            var grade = DuoTechGeometry.GradeSync(a1, a2, minaSpeed, LucasBeamSpeed);
            return grade == WeaveSyncGrade.Perfect;
        }

        private static Vector3 PointAlong(List<Vector3> pts, float s)
        {
            if (pts.Count == 0) return Vector3.zero;
            if (s <= 0f) return pts[0];
            float acc = 0f;
            for (int i = 1; i < pts.Count; i++)
            {
                float seg = Vector3.Distance(pts[i - 1], pts[i]);
                if (acc + seg >= s)
                {
                    float t = seg > 1e-6f ? (s - acc) / seg : 0f;
                    return Vector3.Lerp(pts[i - 1], pts[i], t);
                }
                acc += seg;
            }
            return pts[pts.Count - 1];
        }
    }
}

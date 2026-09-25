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
    /// <summary>
    /// Duo-Tech — tissage souris (T1 Mina + onde loop + T2 Lucas).
    ///
    /// Séquence verrouillée :
    /// 1. Clic-drag = Trait 1 (Mina). Vecteur complet gardé (List<Vector3>).
    /// 2. Relâcher = l'onde démarre en loop sur T1 à la vitesse réelle de Mina
    ///    (partner.MoveSpeed). Prévisualisation du passage.
    /// 3. Nouveau clic-drag = Trait 2 (Lucas), tracé APRÈS l'onde, en anticipation.
    /// 4. Relâcher T2 = résolution immédiate (forme + synchro + dégâts).
    ///
    /// Clic droit / Échap = annulation (PA déjà payés perdus, pas de refund).
    /// La session n'expire jamais d'elle-même (ni fin de tour, ni fin de round).
    /// </summary>
    public class DuoTechWeaveController : MonoBehaviour
    {
        private enum WeavePhase { Idle, DrawingT1, WaitingT2, DrawingT2, Executing }

        private const float SampleStep = 0.15f;
        private const float MinLength = 1.5f;
        private const float LucasBeamSpeed = 12f;

        // Tolérances monde (1 case ≈ 1.7 unité).
        private const float SillageStraightTol = 0.6f;
        private const float SillageCatchDist = 1.0f;
        private const float SillageOverlapTol = 0.8f;
        private const float SillageCrossGap = 1.2f;
        private const float LassoTargetDist = 2.5f;
        private const float FournaiseTargetDist = 2.2f;
        private const int FournaiseIsolationRadius = 2;

        private WeavePhase _phase = WeavePhase.Idle;
        private DuoTechDef _def;
        private TacticalUnit _initiator;
        private TacticalUnit _mina;
        private TacticalUnit _lucas;
        private CombatDevArena _arena;

        private readonly List<Vector3> _t1 = new();
        private readonly List<Vector3> _t2 = new();

        private LineRenderer _lineT1_Front;
        private LineRenderer _lineT1_Occluded;
        private LineRenderer _lineT2_Front;
        private LineRenderer _lineT2_Occluded;
        private GameObject _waveMarker;
        private GameObject _waveMarkerOccluded;
        private GameObject _waveMarkerGreen;
        private GameObject _waveMarkerGreenOccluded;
        private LineRenderer _lineSyncTrace_Front;
        private LineRenderer _lineSyncTrace_Occluded;
        private readonly List<Vector3> _syncTracePts = new();
        private float _waveArc;
        private float _t1Length;

        // Cibles actuellement incluses dans la trajectoire du Duo-Tech
        private readonly List<TacticalUnit> _highlightedTargets = new();
        private readonly Dictionary<TacticalUnit, GameObject> _targetRings = new();

        private Camera _cam;
        private readonly Plane _ground = new(Vector3.up, Vector3.zero);

        public bool IsWeaving => _phase != WeavePhase.Idle;

        private static DuoTechWeaveController _active;

        /// <summary>
        /// Vrai pendant dessin ET exécution : la sélection de cases et le
        /// déplacement doivent rester désactivés tant que c'est vrai.
        /// </summary>
        public static bool AnyWeaving => _active != null && _active.IsWeaving;

        public void BeginSession(DuoTechDef def, TacticalUnit initiator, TacticalUnit partner, CombatDevArena arena)
        {
            if (IsWeaving)
            {
                arena?.Log("Duo-Tech : un tissage est déjà en cours — terminez-le ou annulez (clic droit).");
                return;
            }
            _def = def;
            _initiator = initiator;
            _arena = arena;
            _mina = DuoTechRegistry.IsMinaMember(initiator.Stats) ? initiator : partner;
            _lucas = DuoTechRegistry.IsLucasMember(initiator.Stats) ? initiator : partner;

            _t1.Clear();
            _t2.Clear();
            _waveArc = 0f;
            _phase = WeavePhase.DrawingT1;
            _active = this;

            _cam = Camera.main != null ? Camera.main : FindAnyObjectByType<Camera>();
            BuildVisuals();
            _arena?.Log($"✏️ <b>{def.Name}</b> : tracez <b>T1 (Mina, { MinaName() })</b> en clic-drag sur le sol, puis relâchez.");
        }

        public void CancelSession()
        {
            if (!IsWeaving) return;
            _arena?.Log("Duo-Tech annulé (PA déjà payés perdus).");
            EndSession();
        }

        private string MinaName() => _mina != null && _mina.Stats != null ? _mina.Stats.Name : "Mina";

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

        private void Update()
        {
            if (!IsWeaving) return;
            // Exécution (dash + cinématique) : aucune entrée, annulation impossible.
            if (_phase == WeavePhase.Executing) return;

            if (Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.Escape))
            {
                CancelSession();
                return;
            }

            switch (_phase)
            {
                case WeavePhase.DrawingT1:
                    if (Input.GetMouseButtonDown(0))
                    {
                        _t1.Clear();
                        ClearLine(_lineT1_Front, _lineT1_Occluded);
                        ClearLine(_lineSyncTrace_Front, _lineSyncTrace_Occluded);
                        if (_waveMarker != null) _waveMarker.SetActive(false);
                        if (_waveMarkerOccluded != null) _waveMarkerOccluded.SetActive(false);
                        if (_waveMarkerGreen != null) _waveMarkerGreen.SetActive(false);
                        if (_waveMarkerGreenOccluded != null) _waveMarkerGreenOccluded.SetActive(false);
                        ClearAllTargetHighlights();
                    }
                    if (Input.GetMouseButton(0)) SampleInto(_t1, _lineT1_Front, _lineT1_Occluded);
                    if (Input.GetMouseButtonUp(0)) FinishT1();
                    break;
                case WeavePhase.WaitingT2:
                    AnimateWave();
                    if (Input.GetMouseButtonDown(0))
                    {
                        _t2.Clear();
                        ClearLine(_lineT2_Front, _lineT2_Occluded);
                        _phase = WeavePhase.DrawingT2;
                    }
                    break;
                case WeavePhase.DrawingT2:
                    AnimateWave();
                    if (Input.GetMouseButton(0)) SampleInto(_t2, _lineT2_Front, _lineT2_Occluded);
                    if (Input.GetMouseButtonUp(0)) Resolve();
                    break;
            }

            // Mise à jour en temps réel des cibles incluses dans la trajectoire
            UpdateTargetHighlights();
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

        // ============================ RÉSOLUTION ============================

        private static List<WeaveVec2> ToVec2(List<Vector3> pts)
        {
            var r = new List<WeaveVec2>(pts.Count);
            for (int i = 0; i < pts.Count; i++) r.Add(new WeaveVec2(pts[i].x, pts[i].z));
            return r;
        }

        private List<TacticalUnit> EnemiesAlive()
        {
            var all = new List<TacticalUnit>(
                UnityEngine.Object.FindObjectsByType<TacticalUnit>(FindObjectsInactive.Exclude));
            var r = new List<TacticalUnit>();
            bool playerInitiator = _initiator != null ? _initiator.IsPlayerControlled : true;
            for (int i = 0; i < all.Count; i++)
            {
                var u = all[i];
                if (u == null || u.Stats == null || !u.Stats.IsAlive) continue;
                if (u.IsPlayerControlled == playerInitiator) continue;
                r.Add(u);
            }
            return r;
        }

        private static float DistToPolyXZ(Vector3 p, List<Vector3> poly)
        {
            if (poly == null || poly.Count == 0) return float.MaxValue;
            Vector2 p2 = new Vector2(p.x, p.z);
            float best = float.MaxValue;
            for (int i = 0; i < poly.Count; i++)
            {
                float d = Vector2.Distance(p2, new Vector2(poly[i].x, poly[i].z));
                if (d < best) best = d;
            }
            for (int i = 1; i < poly.Count; i++)
            {
                Vector2 a = new Vector2(poly[i - 1].x, poly[i - 1].z);
                Vector2 b = new Vector2(poly[i].x, poly[i].z);
                Vector2 ab = b - a;
                float lenSq = ab.sqrMagnitude;
                float d;
                if (lenSq < 1e-6f)
                {
                    d = Vector2.Distance(p2, a);
                }
                else
                {
                    float t = Mathf.Clamp01(Vector2.Dot(p2 - a, ab) / lenSq);
                    d = Vector2.Distance(p2, a + t * ab);
                }
                if (d < best) best = d;
            }
            return best;
        }

        private void Resolve()
        {
            if (_t2.Count < 2 || PathLength(_t2) < MinLength)
            {
                _arena?.Log("Duo-Tech : T2 trop court — recommencez le trait Lucas (clic-drag).");
                _t2.Clear();
                ClearLine(_lineT2_Front, _lineT2_Occluded);
                _phase = WeavePhase.WaitingT2;
                return;
            }

            System.Action<string> log = msg => _arena?.Log(msg);
            switch (_def.Id)
            {
                case DuoTechId.SillageIgne:
                    // Séquence animée (dash + cinématique) : termine elle-même la session.
                    _phase = WeavePhase.Executing;
                    if (_waveMarker != null) _waveMarker.SetActive(false);
                    if (_waveMarkerOccluded != null) _waveMarkerOccluded.SetActive(false);
                    if (_waveMarkerGreen != null) _waveMarkerGreen.SetActive(false);
                    if (_waveMarkerGreenOccluded != null) _waveMarkerGreenOccluded.SetActive(false);
                    ClearLine(_lineSyncTrace_Front, _lineSyncTrace_Occluded);
                    ResolveSillage(log);
                    break;
                case DuoTechId.LacetNytharite:
                    ResolveLacet(log);
                    _arena?.RecordChronoSnapshot($"{_def.Name} résolu : {_mina.Stats.Name} + {_lucas.Stats.Name}");
                    EndSession();
                    break;
                case DuoTechId.FournaiseRetardement:
                    ResolveFournaise(log);
                    _arena?.RecordChronoSnapshot($"{_def.Name} résolu : {_mina.Stats.Name} + {_lucas.Stats.Name}");
                    EndSession();
                    break;
            }
        }

        private void ResolveSillage(System.Action<string> log)
        {
            var w1 = ToVec2(_t1);
            var w2 = ToVec2(_t2);

            float dev = DuoTechGeometry.MaxDeviationFromSegment(w1[0], w1[w1.Count - 1], w1);
            if (dev > SillageStraightTol * 2f)
            {
                StartCoroutine(DashThenFinish(
                    $"🔥 Sillage : T1 pas rectiligne (écart {dev:F2}) — Mina trébuche, dash seul sans feu (PA perdus)."));
                return;
            }

            var caughtUnits = new List<TacticalUnit>();
            var enemies = EnemiesAlive();
            for (int i = 0; i < enemies.Count; i++)
            {
                if (DistToPolyXZ(enemies[i].transform.position, _t1) <= SillageCatchDist)
                    caughtUnits.Add(enemies[i]);
            }
            if (caughtUnits.Count == 0)
            {
                StartCoroutine(DashThenFinish(
                    "🔥 Sillage : aucun ennemi sur la trajectoire — dash dans le vide (PA perdus)."));
                return;
            }

            WeaveSyncGrade grade;
            if (!DuoTechGeometry.TryFindCrossing(w1, w2, SillageCrossGap, out float a1, out float a2))
            {
                grade = WeaveSyncGrade.Miss;
            }
            else
            {
                float overlap = DuoTechGeometry.OverlapFraction(w1, w2, SillageOverlapTol);
                if (overlap < 0.25f) grade = WeaveSyncGrade.Miss;
                else grade = DuoTechGeometry.GradeSync(a1, a2, Mathf.Max(1f, _mina.MoveSpeed), LucasBeamSpeed);
            }

            StartCoroutine(SillageCinematic(caughtUnits, grade));
        }

        /// <summary>
        /// Cas raté : Mina dash quand même (vite mais visible), puis fin de session.
        /// </summary>
        private IEnumerator DashThenFinish(string msg)
        {
            yield return DashMina();
            _arena?.Log(msg);
            _arena?.RecordChronoSnapshot($"{_def.Name} résolu : {_mina.Stats.Name} + {_lucas.Stats.Name}");
            EndSession();
        }

        /// <summary>
        /// Cas touché : dash animé de Mina, puis plan cinématique centré sur Lucas
        /// et la première cible — le jet de feu part au déclenchement, les dégâts
        /// tombent au point d'impact, la session se ferme à la fin du plan.
        /// Sans directeur cinématique : repli simple (jet, lecture, dégâts, fin).
        /// </summary>
        private IEnumerator SillageCinematic(List<TacticalUnit> caughtUnits, WeaveSyncGrade grade)
        {
            yield return DashMina();

            var caughtStats = new List<CharacterStats>(caughtUnits.Count);
            for (int i = 0; i < caughtUnits.Count; i++)
                caughtStats.Add(caughtUnits[i].Stats);

            var flamePath = new List<Vector3>(_t2.Count + 1);
            flamePath.Add(_lucas.transform.position + Vector3.up * 1.2f);
            flamePath.AddRange(_t2);

            TacticalUnit primary = null;
            for (int i = 0; i < caughtUnits.Count; i++)
            {
                if (caughtUnits[i] != null && caughtUnits[i].Stats.IsAlive) { primary = caughtUnits[i]; break; }
            }

            var director = FindAnyObjectByType<CinematicDirector>();
            if (director == null || primary == null)
            {
                // Repli sans cinéma : jet, lecture, dégâts, fin.
                DuoTechFlameJet.Spawn(flamePath);
                yield return new WaitForSeconds(0.7f);
                DuoTechRegistry.ResolveSillage(_mina.Stats, _lucas.Stats, caughtStats, grade,
                    msg => _arena?.Log(msg));
                SpawnHitFx(caughtStats, grade);
                _arena?.RecordChronoSnapshot($"{_def.Name} résolu : {_mina.Stats.Name} + {_lucas.Stats.Name}");
                EndSession();
                yield break;
            }

            director.PlayCinematicKillshot(
                _lucas.transform,
                primary.transform,
                onStrikePoint: () =>
                {
                    DuoTechRegistry.ResolveSillage(_mina.Stats, _lucas.Stats, caughtStats, grade,
                        msg => _arena?.Log(msg));
                    SpawnHitFx(caughtStats, grade);
                },
                onComplete: () =>
                {
                    _arena?.RecordChronoSnapshot($"{_def.Name} résolu : {_mina.Stats.Name} + {_lucas.Stats.Name}");
                    EndSession();
                },
                onActionStart: () => DuoTechFlameJet.Spawn(flamePath));
        }

        /// <summary>
        /// Dash de Mina le long de T1 : vite (~0.25-0.6s selon la distance) mais
        /// visible, avec whoosh. L'état grille est recalé à l'arrivée via
        /// TeleportTo (cases traversées ignorées : le dash passe À TRAVERS).
        /// </summary>
        private IEnumerator DashMina()
        {
            if (_mina == null || _t1.Count < 2) yield break;

            Transform tf = _mina.transform;
            Vector3 startPos = tf.position;
            float len = Mathf.Max(0.1f, _t1Length);

            if (KilltimeAudioManager.Instance != null)
                KilltimeAudioManager.Instance.PlayAt(SoundId.Move_Dash, startPos, 0.7f);

            Vector3 firstDir = _t1[1] - _t1[0];
            firstDir.y = 0f;
            if (firstDir != Vector3.zero)
                tf.rotation = Quaternion.LookRotation(firstDir.normalized);

            float dur = Mathf.Clamp(len / 16f, 0.25f, 0.6f);
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                Vector3 p = PointAlong(_t1, len * Mathf.Clamp01(t / dur));
                Vector3 dir = p - tf.position;
                dir.y = 0f;
                if (dir.sqrMagnitude > 0.0004f)
                    tf.rotation = Quaternion.Slerp(tf.rotation, Quaternion.LookRotation(dir.normalized), 0.4f);
                // Glisse au ras du sol, à hauteur du pion (pas plaquée aux tuiles).
                tf.position = new Vector3(p.x, startPos.y, p.z);
                yield return null;
            }

            var grid = FindAnyObjectByType<TacticalHexGrid>();
            bool arrived = false;
            HexCoordinates dest = default;
            bool slid = false;
            if (grid != null && grid.TryGetNodeAtWorldPosition(_t1[_t1.Count - 1], out HexNode node))
            {
                dest = node.Coordinates;
                arrived = _mina.TeleportTo(dest, grid);
                if (!arrived)
                {
                    // Arrivée occupée (typiquement un ennemi traversé par le dash) :
                    // Mina glisse sur la case libre la plus proche au lieu de rebondir
                    // au départ. Un dash réussi la laisse TOUJOURS à destination.
                    if (grid.TryFindNearestFreeCell(dest, out HexCoordinates free, 8))
                    {
                        dest = free;
                        arrived = _mina.TeleportTo(dest, grid);
                        slid = arrived;
                    }
                }
            }

            if (arrived)
            {
                _arena?.Log(slid
                    ? $"💨 {MinaName()} : arrivée occupée, glissade en {dest} !"
                    : $"💨 {MinaName()} dash en {dest} !");
            }
            else
            {
                tf.position = startPos;
                _arena?.Log($"💨 {MinaName()} : arrivée hors grille, reste sur place.");
            }
        }

        private void ResolveLacet(System.Action<string> log)
        {
            var w1 = ToVec2(_t1);
            bool closed = DuoTechGeometry.IsLoopClosed(w1);

            Vector3 centroid = Vector3.zero;
            for (int i = 0; i < _t1.Count; i++) centroid += _t1[i];
            centroid /= Mathf.Max(1, _t1.Count);

            TacticalUnit best = null;
            float bestD = float.MaxValue;
            var enemies = EnemiesAlive();
            for (int i = 0; i < enemies.Count; i++)
            {
                float d = Vector3.Distance(
                    new Vector3(enemies[i].transform.position.x, 0f, enemies[i].transform.position.z),
                    new Vector3(centroid.x, 0f, centroid.z));
                if (d < bestD) { bestD = d; best = enemies[i]; }
            }
            if (best == null || bestD > LassoTargetDist)
            {
                log("🪢 Lacet : aucune cible dans le lasso (PA perdus).");
                return;
            }
            DuoTechRegistry.ResolveLacet(best.Stats, closed, log);
            best.GetComponent<TacticalUnitVisual>()?.TriggerHitFlash();
        }

        private void ResolveFournaise(System.Action<string> log)
        {
            var w1 = ToVec2(_t1);
            bool closed = DuoTechGeometry.IsLoopClosed(w1);

            Vector3 centroid = Vector3.zero;
            for (int i = 0; i < _t1.Count; i++) centroid += _t1[i];
            centroid /= Mathf.Max(1, _t1.Count);

            TacticalUnit best = null;
            float bestD = float.MaxValue;
            var enemies = EnemiesAlive();
            for (int i = 0; i < enemies.Count; i++)
            {
                float d = Vector3.Distance(
                    new Vector3(enemies[i].transform.position.x, 0f, enemies[i].transform.position.z),
                    new Vector3(centroid.x, 0f, centroid.z));
                if (d < bestD) { bestD = d; best = enemies[i]; }
            }
            if (best == null || bestD > FournaiseTargetDist)
            {
                log("♨️ Fournaise : aucune cible dans le triangle (PA perdus).");
                return;
            }

            int neighbours = 0;
            for (int i = 0; i < enemies.Count; i++)
            {
                if (enemies[i] == best) continue;
                if (enemies[i].CurrentCoords.DistanceTo(best.CurrentCoords) <= FournaiseIsolationRadius)
                    neighbours++;
            }
            if (neighbours > 0)
                log($"♨️ Fournaise : {best.Stats.Name} n'est pas isolé ({neighbours} à ≤{FournaiseIsolationRadius} cases) — triangle strict exigé.");

            DuoTechRegistry.ResolveFournaise(best.Stats, closed && neighbours == 0, log);
            best.GetComponent<TacticalUnitVisual>()?.TriggerHitFlash();
        }

        private void SpawnHitFx(List<CharacterStats> targets, WeaveSyncGrade grade)
        {
            var enemies = EnemiesAlive();
            for (int i = 0; i < enemies.Count; i++)
            {
                if (targets.Contains(enemies[i].Stats))
                    enemies[i].GetComponent<TacticalUnitVisual>()?.TriggerHitFlash();
            }
            var mVis = _mina.GetComponent<TacticalUnitVisual>();
            if (grade == WeaveSyncGrade.Risky) mVis?.TriggerHitFlash();
            if (grade == WeaveSyncGrade.Perfect)
                mVis?.SpawnFloatingText("SILLAGE PARFAIT !", new Color(1f, 0.6f, 0.1f));
        }

        private void EndSession()
        {
            _phase = WeavePhase.Idle;
            if (_active == this) _active = null;
            _def = null;
            _t1.Clear();
            _t2.Clear();
            if (_lineT1_Front != null) Destroy(_lineT1_Front.gameObject);
            if (_lineT1_Occluded != null) Destroy(_lineT1_Occluded.gameObject);
            if (_lineT2_Front != null) Destroy(_lineT2_Front.gameObject);
            if (_lineT2_Occluded != null) Destroy(_lineT2_Occluded.gameObject);
            if (_lineSyncTrace_Front != null) Destroy(_lineSyncTrace_Front.gameObject);
            if (_lineSyncTrace_Occluded != null) Destroy(_lineSyncTrace_Occluded.gameObject);
            if (_waveMarker != null) Destroy(_waveMarker);
            if (_waveMarkerOccluded != null) Destroy(_waveMarkerOccluded);
            if (_waveMarkerGreen != null) Destroy(_waveMarkerGreen);
            if (_waveMarkerGreenOccluded != null) Destroy(_waveMarkerGreenOccluded);
            _lineT1_Front = null;
            _lineT1_Occluded = null;
            _lineT2_Front = null;
            _lineT2_Occluded = null;
            _lineSyncTrace_Front = null;
            _lineSyncTrace_Occluded = null;
            _waveMarker = null;
            _waveMarkerOccluded = null;
            _waveMarkerGreen = null;
            _waveMarkerGreenOccluded = null;
            ClearAllTargetHighlights();
        }

        private void OnDisable()
        {
            // Sécurité : pas de lignes ou anneaux fantômes si l'objet est désactivé mid-tissage.
            if (_lineT1_Front != null) Destroy(_lineT1_Front.gameObject);
            if (_lineT1_Occluded != null) Destroy(_lineT1_Occluded.gameObject);
            if (_lineT2_Front != null) Destroy(_lineT2_Front.gameObject);
            if (_lineT2_Occluded != null) Destroy(_lineT2_Occluded.gameObject);
            if (_lineSyncTrace_Front != null) Destroy(_lineSyncTrace_Front.gameObject);
            if (_lineSyncTrace_Occluded != null) Destroy(_lineSyncTrace_Occluded.gameObject);
            if (_waveMarker != null) Destroy(_waveMarker);
            if (_waveMarkerOccluded != null) Destroy(_waveMarkerOccluded);
            if (_waveMarkerGreen != null) Destroy(_waveMarkerGreen);
            if (_waveMarkerGreenOccluded != null) Destroy(_waveMarkerGreenOccluded);
            ClearAllTargetHighlights();
            if (_active == this) _active = null;
        }
    }
}

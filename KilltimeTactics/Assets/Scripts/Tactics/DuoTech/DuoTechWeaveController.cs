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

        private LineRenderer _lineT1;
        private LineRenderer _lineT2;
        private GameObject _waveMarker;
        private float _waveArc;
        private float _t1Length;

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
            _lineT1 = BuildLine(new Color(0.2f, 1f, 0.6f), 0.2f);
            _lineT2 = BuildLine(new Color(1f, 0.45f, 0.1f), 0.2f);
            _waveMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _waveMarker.transform.localScale = Vector3.one * 0.35f;
            var rend = _waveMarker.GetComponent<Renderer>();
            if (rend != null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                if (shader != null)
                {
                    rend.material = new Material(shader);
                    rend.material.color = new Color(0.4f, 1f, 0.8f);
                }
            }
            var col = _waveMarker.GetComponent<Collider>();
            if (col != null) Destroy(col);
            _waveMarker.SetActive(false);
        }

        private LineRenderer BuildLine(Color color, float width)
        {
            var go = new GameObject("DuoWeaveLine");
            go.transform.SetParent(transform);
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = 0;
            lr.startWidth = width;
            lr.endWidth = width;
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Standard");
            if (shader != null)
            {
                lr.material = new Material(shader);
                lr.material.color = color;
                // Les traits du tissage passent par-dessus tuiles et unités.
                lr.material.renderQueue = 4000;
            }
            lr.useWorldSpace = true;
            return lr;
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
                    if (Input.GetMouseButtonDown(0)) _t1.Clear();
                    if (Input.GetMouseButton(0)) SampleInto(_t1, _lineT1);
                    if (Input.GetMouseButtonUp(0)) FinishT1();
                    break;
                case WeavePhase.WaitingT2:
                    AnimateWave();
                    if (Input.GetMouseButtonDown(0))
                    {
                        _t2.Clear();
                        _phase = WeavePhase.DrawingT2;
                    }
                    break;
                case WeavePhase.DrawingT2:
                    AnimateWave();
                    if (Input.GetMouseButton(0)) SampleInto(_t2, _lineT2);
                    if (Input.GetMouseButtonUp(0)) Resolve();
                    break;
            }
        }

        private bool GroundPoint(out Vector3 point)
        {
            point = Vector3.zero;
            if (_cam == null) return false;
            var ray = _cam.ScreenPointToRay(Input.mousePosition);
            if (_ground.Raycast(ray, out float dist))
            {
                // Traits surélevés : visibles par-dessus les tuiles, sous les corps.
                point = ray.GetPoint(dist) + Vector3.up * 0.25f;
                return true;
            }
            return false;
        }

        private void SampleInto(List<Vector3> pts, LineRenderer lr)
        {
            if (!GroundPoint(out Vector3 p)) return;
            if (pts.Count == 0 || Vector3.Distance(pts[pts.Count - 1], p) >= SampleStep)
            {
                pts.Add(p);
                lr.positionCount = pts.Count;
                lr.SetPositions(pts.ToArray());
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
                _lineT1.positionCount = 0;
                return;
            }
            _waveArc = 0f;
            _waveMarker.SetActive(true);
            _phase = WeavePhase.WaitingT2;
            _arena?.Log("🌊 Onde Mina en loop — tracez <b>T2 (Lucas)</b> en anticipation du passage !");
        }

        private void AnimateWave()
        {
            if (_t1.Count < 2 || _waveMarker == null) return;
            float speed = _mina != null ? Mathf.Max(1f, _mina.MoveSpeed) : 4f;
            _waveArc += speed * Time.deltaTime;
            if (_t1Length > 0f) _waveArc %= _t1Length;
            _waveMarker.transform.position = PointAlong(_t1, _waveArc) + Vector3.up * 0.3f;
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
            for (int i = 0; i < all.Count; i++)
            {
                var u = all[i];
                if (u == null || u.Stats == null || !u.Stats.IsAlive) continue;
                if (u.IsPlayerControlled == _initiator.IsPlayerControlled) continue;
                r.Add(u);
            }
            return r;
        }

        private static float DistToPolyXZ(Vector3 p, List<Vector3> poly)
        {
            float best = float.MaxValue;
            for (int i = 0; i < poly.Count; i++)
            {
                float d = Vector3.Distance(
                    new Vector3(p.x, 0f, p.z),
                    new Vector3(poly[i].x, 0f, poly[i].z));
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
                _lineT2.positionCount = 0;
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
            if (_lineT1 != null) Destroy(_lineT1.gameObject);
            if (_lineT2 != null) Destroy(_lineT2.gameObject);
            if (_waveMarker != null) Destroy(_waveMarker);
            _lineT1 = null;
            _lineT2 = null;
            _waveMarker = null;
        }

        private void OnDisable()
        {
            // Sécurité : pas de lignes fantômes si l'objet est désactivé mid-tissage.
            if (_lineT1 != null) Destroy(_lineT1.gameObject);
            if (_lineT2 != null) Destroy(_lineT2.gameObject);
            if (_waveMarker != null) Destroy(_waveMarker);
            if (_active == this) _active = null;
        }
    }
}

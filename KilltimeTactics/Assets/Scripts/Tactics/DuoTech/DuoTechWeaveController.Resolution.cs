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
    // Split move-only de DuoTechWeaveController — Resolution : ToVec2, EnemiesAlive, DistToPolyXZ, Resolve, ResolveSillage, DashThenFinish, SillageCinematic, DashMina, ResolveLacet, ResolveFournaise.
    public partial class DuoTechWeaveController : MonoBehaviour
    {
        // ============================ RÉSOLUTION ============================

        private static List<WeaveVec2> ToVec2(List<Vector3> pts)
        {
            var r = new List<WeaveVec2>(pts.Count);
            for (int i = 0; i < pts.Count; i++) r.Add(new WeaveVec2(pts[i].x, pts[i].z));
            return r;
        }

        private List<TacticalUnit> EnemiesAlive()
        {
            var r = new List<TacticalUnit>();
            bool playerInitiator = _initiator != null ? _initiator.IsPlayerControlled : true;
            foreach (var u in TacticalUnitRegistry.GetAliveUnits())
            {
                if (u == null) continue;
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
                case DuoTechId.TrempeInversee:
                    ResolveTrempe(log);
                    _arena?.RecordChronoSnapshot($"{_def.Name} résolu : {_mina.Stats.Name} + {_lucas.Stats.Name}");
                    EndSession();
                    break;
                case DuoTechId.RailgunArtisanale:
                    ResolveRailgun(log);
                    _arena?.RecordChronoSnapshot($"{_def.Name} résolu : {_mina.Stats.Name} + {_lucas.Stats.Name}");
                    EndSession();
                    break;
                case DuoTechId.CisailleEntropique:
                    ResolveCisaille(log);
                    _arena?.RecordChronoSnapshot($"{_def.Name} résolu : {_mina.Stats.Name} + {_lucas.Stats.Name}");
                    EndSession();
                    break;
                case DuoTechId.Parallaxe:
                    ResolveParallaxe(log);
                    _arena?.RecordChronoSnapshot($"{_def.Name} résolu : {_mina.Stats.Name} + {_lucas.Stats.Name}");
                    EndSession();
                    break;
                case DuoTechId.MirageDeuxTemps:
                    // Replacement animé (dash Mina vers fin T1) : termine lui-même la session.
                    _phase = WeavePhase.Executing;
                    if (_waveMarker != null) _waveMarker.SetActive(false);
                    if (_waveMarkerOccluded != null) _waveMarkerOccluded.SetActive(false);
                    if (_waveMarkerGreen != null) _waveMarkerGreen.SetActive(false);
                    if (_waveMarkerGreenOccluded != null) _waveMarkerGreenOccluded.SetActive(false);
                    ClearLine(_lineSyncTrace_Front, _lineSyncTrace_Occluded);
                    ResolveMirage(log);
                    break;
                case DuoTechId.UppercutThermobarique:
                    ResolveUppercut(log);
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
    }
}

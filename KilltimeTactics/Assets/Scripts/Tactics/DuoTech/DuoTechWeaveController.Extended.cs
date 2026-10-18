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
    // Split move-only de DuoTechWeaveController — Extended (RD-085) : CentroidOfT1, BestEnemyNear, ResolveTrempe, ResolveRailgun, ResolveCisaille, ResolveParallaxe, ResolveMirage, MirageThenFinish, ResolveUppercut, SpawnHitFx.
    public partial class DuoTechWeaveController : MonoBehaviour
    {
        // ============================ RD-085 (Livre VI §25.5) ============================
        // Tissage binaire (comme Lacet/Fournaise) : condition de placement
        // validée = Parfaite table, sinon Ratée table (PA perdus). Le Semi
        // n'existe qu'aux dés via DuoTechRegistry.TableOutcome + Resolve*.

        private Vector3 CentroidOfT1()
        {
            Vector3 centroid = Vector3.zero;
            for (int i = 0; i < _t1.Count; i++) centroid += _t1[i];
            return centroid / Mathf.Max(1, _t1.Count);
        }

        private TacticalUnit BestEnemyNear(Vector3 centroid, float maxDist, List<TacticalUnit> enemies, out float bestD)
        {
            TacticalUnit best = null;
            bestD = float.MaxValue;
            for (int i = 0; i < enemies.Count; i++)
            {
                float d = Vector3.Distance(
                    new Vector3(enemies[i].transform.position.x, 0f, enemies[i].transform.position.z),
                    new Vector3(centroid.x, 0f, centroid.z));
                if (d < bestD) { bestD = d; best = enemies[i]; }
            }
            if (best == null || bestD > maxDist) return null;
            return best;
        }

        private void ResolveTrempe(System.Action<string> log)
        {
            var enemies = EnemiesAlive();
            if (_t1.Count < 3)
            {
                DuoTechRegistry.ResolveTrempe(null, DuoTechOutcome.Miss, log);
                return;
            }
            Vector3 centroid = CentroidOfT1();
            var caught = new List<TacticalUnit>();
            Vector2 c2 = new Vector2(centroid.x, centroid.z);
            for (int i = 0; i < enemies.Count; i++)
            {
                Vector2 e2 = new Vector2(enemies[i].transform.position.x, enemies[i].transform.position.z);
                if (Vector2.Distance(e2, c2) <= TrempeCatchDist)
                    caught.Add(enemies[i]);
            }
            if (caught.Count < 2)
            {
                log("❄️ Trempe : pas de groupe (≥ 2 ennemis à ≤ 2 cases d'un point exigés) — le gel ne prend pas (PA perdus).");
                DuoTechRegistry.ResolveTrempe(null, DuoTechOutcome.Miss, log);
                return;
            }
            // Groupés en cases hexa autour du centroïde (≤ 2 cases d'un point).
            float avgQ = 0f, avgR = 0f;
            for (int i = 0; i < caught.Count; i++)
            {
                avgQ += caught[i].CurrentCoords.Q;
                avgR += caught[i].CurrentCoords.R;
            }
            avgQ /= caught.Count;
            avgR /= caught.Count;
            var centerHex = new Killtime.Tactics.Grid.HexCoordinates(Mathf.RoundToInt(avgQ), Mathf.RoundToInt(avgR));
            var dists = new List<int>(caught.Count);
            for (int i = 0; i < caught.Count; i++)
                dists.Add(caught[i].CurrentCoords.DistanceTo(centerHex));
            if (!DuoTechGeometry.AreGrouped(dists, 2))
            {
                log("❄️ Trempe : cibles trop dispersées (≤ 2 cases d'un point exigées) — le gel ne prend pas (PA perdus).");
                DuoTechRegistry.ResolveTrempe(null, DuoTechOutcome.Miss, log);
                return;
            }
            var stats = new List<CharacterStats>(caught.Count);
            for (int i = 0; i < caught.Count; i++) stats.Add(caught[i].Stats);
            DuoTechRegistry.ResolveTrempe(stats, DuoTechOutcome.Perfect, log);
            for (int i = 0; i < caught.Count; i++)
                caught[i].GetComponent<TacticalUnitVisual>()?.TriggerHitFlash();
        }

        private void ResolveRailgun(System.Action<string> log)
        {
            var w1 = ToVec2(_t1);
            var enemies = EnemiesAlive();
            if (_t1.Count < 2)
            {
                log("🛤️ <b>Railgun Artisanale</b> [Ratée] : le couloir se referme (PA perdus).");
                return;
            }
            float dev = DuoTechGeometry.MaxDeviationFromSegment(w1[0], w1[w1.Count - 1], w1);
            float len = 0f;
            for (int i = 1; i < _t1.Count; i++)
                len += Vector3.Distance(_t1[i - 1], _t1[i]);
            if (dev > RailgunStraightTol * 2f)
            {
                log($"🛤️ Railgun : rail pas rectiligne (écart {dev:F2}) — le couloir se referme (PA perdus).");
                if (enemies.Count > 0) DuoTechRegistry.ResolveRailgun(enemies[0].Stats, DuoTechOutcome.Miss, log);
                else log("🛤️ <b>Railgun Artisanale</b> [Ratée] : le couloir se referme (PA perdus).");
                return;
            }
            if (len < RailgunMinLengthWorld)
            {
                log($"🛤️ Railgun : couloir trop court ({len / CaseWorld:F1} cases < 4) — le couloir se referme (PA perdus).");
                if (enemies.Count > 0) DuoTechRegistry.ResolveRailgun(enemies[0].Stats, DuoTechOutcome.Miss, log);
                else log("🛤️ <b>Railgun Artisanale</b> [Ratée] : le couloir se referme (PA perdus).");
                return;
            }
            Vector3 end = _t1[_t1.Count - 1];
            TacticalUnit best = BestEnemyNear(end, RailgunTargetDist, enemies, out _);
            if (best == null)
            {
                log("🛤️ Railgun : aucune cible dure au bout du couloir (PA perdus).");
                return;
            }
            // Couloir vide : personne d'autre dans la bande (alliés + ennemis sauf cible).
            var wStart = new WeaveVec2(_t1[0].x, _t1[0].z);
            var wEnd = new WeaveVec2(end.x, end.z);
            var obstacles = new List<WeaveVec2>();
            int targetIdx = -1;
            foreach (var u in TacticalUnitRegistry.GetAliveUnits())
            {
                if (u == null) continue;
                if (u == _mina || u == _lucas) continue;
                var p = new WeaveVec2(u.transform.position.x, u.transform.position.z);
                if (u == best) { targetIdx = obstacles.Count; }
                obstacles.Add(p);
            }
            if (!DuoTechGeometry.IsStraightCorridor(wStart, wEnd, RailgunMinLengthWorld, RailgunWidthTol, obstacles, targetIdx))
            {
                log("🛤️ Railgun : couloir obstrué — le couloir se referme (PA perdus).");
                DuoTechRegistry.ResolveRailgun(best.Stats, DuoTechOutcome.Miss, log);
                return;
            }
            DuoTechRegistry.ResolveRailgun(best.Stats, DuoTechOutcome.Perfect, log);
            best.GetComponent<TacticalUnitVisual>()?.TriggerHitFlash();
        }

        private void ResolveCisaille(System.Action<string> log)
        {
            var w1 = ToVec2(_t1);
            var w2 = ToVec2(_t2);
            if (!DuoTechGeometry.TryFindCrossingPoint(w1, w2, CisailleCrossGap, out WeaveVec2 cross))
            {
                log("✖️ Cisaille : aucun croisement en X — les branches se désynchronisent (PA perdus).");
                DuoTechRegistry.ResolveCisaille(null, DuoTechOutcome.Miss, log);
                return;
            }
            var enemies = EnemiesAlive();
            var caughtStats = new List<CharacterStats>();
            var caughtUnits = new List<TacticalUnit>();
            for (int i = 0; i < enemies.Count; i++)
            {
                var ep = new WeaveVec2(enemies[i].transform.position.x, enemies[i].transform.position.z);
                if (WeaveVec2.Distance(ep, cross) <= CisailleCatchDist)
                {
                    caughtStats.Add(enemies[i].Stats);
                    caughtUnits.Add(enemies[i]);
                }
            }
            if (caughtStats.Count == 0)
            {
                log("✖️ Cisaille : personne à l'intersection du X (PA perdus).");
                DuoTechRegistry.ResolveCisaille(null, DuoTechOutcome.Miss, log);
                return;
            }
            DuoTechRegistry.ResolveCisaille(caughtStats, DuoTechOutcome.Perfect, log);
            for (int i = 0; i < caughtUnits.Count; i++)
                caughtUnits[i].GetComponent<TacticalUnitVisual>()?.TriggerHitFlash();
        }

        private void ResolveParallaxe(System.Action<string> log)
        {
            var enemies = EnemiesAlive();
            if (_mina == null || _lucas == null || enemies.Count == 0)
            {
                log("⧉ Parallaxe : binôme ou cible introuvable (PA perdus).");
                return;
            }
            Vector3 centroid = _t1.Count >= 3 ? CentroidOfT1() : _t1.Count > 0 ? _t1[_t1.Count - 1] : _mina.transform.position;
            TacticalUnit best = BestEnemyNear(centroid, ParallaxeTargetDist, enemies, out _);
            if (best == null)
            {
                // Repli : cible ennemie la plus proche de la paire.
                float bd = float.MaxValue;
                for (int i = 0; i < enemies.Count; i++)
                {
                    float d = Vector3.Distance(enemies[i].transform.position, centroid);
                    if (d < bd) { bd = d; best = enemies[i]; }
                }
                if (best == null)
                {
                    log("⧉ Parallaxe : aucune cible (PA perdus).");
                    return;
                }
            }
            var minaP = new WeaveVec2(_mina.transform.position.x, _mina.transform.position.z);
            var lucasP = new WeaveVec2(_lucas.transform.position.x, _lucas.transform.position.z);
            var tp = new WeaveVec2(best.transform.position.x, best.transform.position.z);
            if (!DuoTechGeometry.AreOpposed180(minaP, lucasP, tp, ParallaxeMinDot))
            {
                log($"⧉ Parallaxe : {best.Stats.Name} n'est pas en opposition 180° — la cible esquive entre les deux temps (PA perdus).");
                DuoTechRegistry.ResolveParallaxe(best.Stats, DuoTechOutcome.Miss, log);
                return;
            }
            DuoTechRegistry.ResolveParallaxe(best.Stats, DuoTechOutcome.Perfect, log);
            best.GetComponent<TacticalUnitVisual>()?.TriggerHitFlash();
        }

        private void ResolveMirage(System.Action<string> log)
        {
            float len = 0f;
            for (int i = 1; i < _t1.Count; i++)
                len += Vector3.Distance(_t1[i - 1], _t1[i]);
            if (len > MirageMaxLengthWorld)
            {
                log($"🌫️ Mirage : replacement trop long ({len / CaseWorld:F1} cases > 3) — la pré-vision se brouille (PA perdus).");
                DuoTechRegistry.ResolveMirage(_mina != null ? _mina.Stats : null, DuoTechOutcome.Miss, log);
                _arena?.RecordChronoSnapshot($"{_def.Name} raté : {(_mina != null ? _mina.Stats.Name : "Mina")} + {(_lucas != null ? _lucas.Stats.Name : "Lucas")}");
                EndSession();
                return;
            }
            StartCoroutine(MirageThenFinish(log));
        }

        private System.Collections.IEnumerator MirageThenFinish(System.Action<string> log)
        {
            yield return DashMina();
            DuoTechRegistry.ResolveMirage(_mina != null ? _mina.Stats : null, DuoTechOutcome.Perfect, log);
            _mina?.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("MIRAGE !", new Color(0.7f, 0.7f, 0.9f));
            _arena?.RecordChronoSnapshot($"{_def.Name} résolu : {_mina.Stats.Name} + {_lucas.Stats.Name}");
            EndSession();
        }

        private void ResolveUppercut(System.Action<string> log)
        {
            var enemies = EnemiesAlive();
            Vector3 centroid = _t1.Count > 0 ? CentroidOfT1() : (_mina != null ? _mina.transform.position : Vector3.zero);
            TacticalUnit best = BestEnemyNear(centroid, UppercutTargetDist, enemies, out _);
            if (best == null)
            {
                log("⬆️ Uppercut : aucune cible à portée (whiff : +1 ESS chacun, PA perdus).");
                DuoTechRegistry.ResolveUppercut(
                    _mina != null ? _mina.Stats : null,
                    _lucas != null ? _lucas.Stats : null,
                    null, DuoTechOutcome.Miss, log);
                return;
            }
            if (!DuoTechRegistry.IsUppercutCondition(best.Stats))
            {
                string hp = $"{best.Stats.CurrentHealth}/{best.Stats.MaxHealth}";
                log($"⬆️ Uppercut : {best.Stats.Name} n'est pas entravée à ≤ 50% PV ({hp}, entrave : {DuoTechRegistry.IsEntraveeForUppercut(best.Stats)}) — whiff : +1 ESS chacun (PA perdus).");
                DuoTechRegistry.ResolveUppercut(
                    _mina != null ? _mina.Stats : null,
                    _lucas != null ? _lucas.Stats : null,
                    best.Stats, DuoTechOutcome.Miss, log);
                best.GetComponent<TacticalUnitVisual>()?.TriggerHitFlash();
                return;
            }
            DuoTechRegistry.ResolveUppercut(
                _mina != null ? _mina.Stats : null,
                _lucas != null ? _lucas.Stats : null,
                best.Stats, DuoTechOutcome.Perfect, log);
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
    }
}

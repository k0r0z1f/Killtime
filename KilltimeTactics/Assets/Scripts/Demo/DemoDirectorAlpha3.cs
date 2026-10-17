// RD-027 + RD-028 : metteur en scène de la démo 60s hors-lore (Killzone_Alpha3).
// 6 beats : cadrage -> mouvement A* -> tir + couvert -> duel aveugle ->
// ramassage arme -> Sillage Igné. Overlay : watermark PLACEHOLDER +
// télémétrie PA/PE/PV + ligne de mire forcée. Aucune touche F volée :
// panneau OnGUI avec boutons. Auto-inactif hors Killzone_Alpha3.
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Core.Character;
using Killtime.Core.Combat;
using Killtime.Core.Inventory;
using Killtime.Tactics;
using Killtime.Tactics.CombatUI;
using Killtime.Tactics.Grid;
using Killtime.Tactics.TurnSystem;
using Killtime.Tactics.Units;

namespace Killtime.Demo
{
    public sealed class DemoDirectorAlpha3 : MonoBehaviour
    {
        private CombatDevArena _arena;
        private TurnManager _turnManager;
        private TacticalHexGrid _grid;

        private bool _playing;
        private bool _stopRequested;
        private string _beatLabel = "Prêt.";
        private int _beatIndex;
        private const int TotalBeats = 6;

        private bool _savedLos;
        private bool _savedInfiniteAp;

        private void Awake()
        {
            EnsureRefs();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBoot()
        {
            if (FindAnyObjectByType<DemoDirectorAlpha3>() != null) return;
            if (FindAnyObjectByType<CombatDevArena>() == null) return;
            new GameObject("DemoDirectorAlpha3").AddComponent<DemoDirectorAlpha3>();
        }

        private void EnsureRefs()
        {
            if (_arena == null) _arena = FindAnyObjectByType<CombatDevArena>();
            if (_turnManager == null) _turnManager = FindAnyObjectByType<TurnManager>();
            if (_grid == null) _grid = FindAnyObjectByType<TacticalHexGrid>();
        }

        private bool IsDemoMap()
        {
            if (_arena == null) return false;
            try
            {
                string path = _arena.CurrentLoadedMapPath ?? "";
                return path.IndexOf("Killzone_Alpha3", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        private void OnGUI()
        {
            EnsureRefs();
            if (!IsDemoMap() || _grid == null) return;

            DrawWatermark();
            DrawPanel();
            if (_playing) DrawTelemetry();
        }

        private void DrawWatermark()
        {
            var r = new Rect((Screen.width - 420f) * 0.5f, 8f, 420f, 20f);
            GUI.Label(r, "DÉMO HORS-LORE · Killzone_Alpha3 · PLACEHOLDER");
        }

        private void DrawPanel()
        {
            var r = new Rect(12f, Screen.height - 92f, 330f, 80f);
            GUI.Box(r, $"DÉMO 60s  [{_beatIndex}/{TotalBeats}]");
            GUI.Label(new Rect(r.x + 10f, r.y + 22f, r.width - 20f, 24f), _beatLabel);
            if (!_playing)
            {
                if (GUI.Button(new Rect(r.x + 10f, r.y + 50f, 150f, 22f), "▶ Jouer la démo"))
                    StartDemo();
            }
            else if (GUI.Button(new Rect(r.x + 10f, r.y + 50f, 150f, 22f), "■ Stop"))
            {
                _stopRequested = true;
            }
        }

        private void DrawTelemetry()
        {
            try
            {
                var active = _turnManager != null ? _turnManager.ActiveUnit : null;
                if (active == null || active.Stats == null) return;
                var s = active.Stats;
                string target = "";
                var tgt = _arena.CurrentTarget;
                if (tgt != null && tgt.Stats != null)
                    target = $" · Cible {tgt.Stats.Name} à {active.CurrentCoords.DistanceTo(tgt.CurrentCoords)} cases";
                var r = new Rect(12f, 34f, 360f, 22f);
                GUI.Box(r, $"{s.Name} · PA {s.CurrentActionPoints}/{s.MaxActionPoints}"
                    + $" · PE {s.Essoufflement} · PV {s.CurrentHealth}/{s.MaxHealth}{target}");
            }
            catch { /* overlay best-effort */ }
        }

        private void StartDemo()
        {
            EnsureRefs();
            if (_playing || _arena == null) return;
            _playing = true;
            _stopRequested = false;
            _beatIndex = 0;
            StartCoroutine(RunDemo());
        }

        private IEnumerator RunDemo()
        {
            _savedLos = _arena.ShowCoverLineOfSight;
            _savedInfiniteAp = _arena.InfiniteAP;
            try
            {
                yield return BeatSetup();
                if (CheckStop()) yield break;
                yield return BeatMove();
                if (CheckStop()) yield break;
                yield return BeatCoverShot();
                if (CheckStop()) yield break;
                yield return BeatBlindDuel();
                if (CheckStop()) yield break;
                yield return BeatPickup();
                if (CheckStop()) yield break;
                yield return BeatSillage();
                if (CheckStop()) yield break;
                _beatIndex = TotalBeats;
                _beatLabel = "FIN — greybox assumé, aucun asset final.";
                _arena.Log("🎬 Démo 60s terminée (RD-027). Capture OBS : RD-029.");
                yield return WaitSeconds(4f);
            }
            finally
            {
                try
                {
                    _arena.ShowCoverLineOfSight = _savedLos;
                    _arena.InfiniteAP = _savedInfiniteAp;
                }
                catch { /* restore best-effort */ }
                _playing = false;
            }
        }

        private bool CheckStop()
        {
            if (!_stopRequested) return false;
            _beatLabel = "Démo arrêtée.";
            try { _arena.Log("🎬 Démo interrompue."); } catch { }
            return true;
        }

        private IEnumerator WaitSeconds(float s)
        {
            float t = 0f;
            while (t < s)
            {
                if (_stopRequested) yield break;
                t += Time.deltaTime;
                yield return null;
            }
        }

        private IEnumerator BeatSetup()
        {
            _beatIndex = 0;
            _beatLabel = "① CADRAGE — Q/E rotation, molette zoom.";
            try
            {
                _arena.ShowCoverLineOfSight = true;
                _arena.InfiniteAP = true;
                if (_turnManager != null && _turnManager.IsInExploration)
                    _turnManager.EnterCombatMode();
                EnsureLucas();
                _arena.Log("🎬 Démo 60s : ligne de mire forcée, PA illimités, Lucas garanti.");
            }
            catch (Exception e) { _arena.Log($"🎬 Setup partiel : {e.Message}"); }
            yield return WaitSeconds(3f);
        }

        private IEnumerator BeatMove()
        {
            _beatIndex = 1;
            _beatLabel = "② MOUVEMENT — A* hexa, coût PA affiché.";
            IEnumerator moveRoutine = null;
            try
            {
                var mina = FindMina();
                var dest = FreeTileNear(new HexCoordinates(1, 0));
                if (mina != null && dest.HasValue)
                {
                    var path = _arena.Pathfinder.FindPath(
                        mina.CurrentCoords, dest.Value, 99, out int cost, mina.FootprintType);
                    if (path != null && path.Count > 1)
                    {
                        int effective = mina.ComputeMovementAPCost(cost);
                        _arena.Log($"🚶 Démo : {mina.Stats.Name} avance (-{effective} PA).");
                        moveRoutine = mina.MoveAlongPath(path, _grid, effective);
                    }
                }
            }
            catch (Exception e) { _arena.Log($"🎬 Mouvement ignoré : {e.Message}"); }
            if (moveRoutine != null) yield return moveRoutine;
            yield return WaitSeconds(2f);
        }

        private IEnumerator BeatCoverShot()
        {
            _beatIndex = 2;
            _beatLabel = "③ TIR + COUVERT — vert/jaune/orange/rouge.";
            bool attacked = false;
            try
            {
                var mina = FindMina();
                var foe = NearestEnemy(mina);
                if (mina != null && foe != null)
                {
                    _arena.Log($"🎯 Démo : {mina.Stats.Name} ➔ {foe.Stats.Name} (couvert pris en compte).");
                    _arena.ExecuteAttack(BodyPart.Torse, false,
                        SkillType.Ballistique, SkillType.Esquive, true,
                        weaponBaseDamage: 5, explicitTarget: foe, explicitAttacker: mina);
                    attacked = true;
                }
            }
            catch (Exception e) { _arena.Log($"🎬 Tir ignoré : {e.Message}"); }
            if (attacked) yield return WaitResolving(10f);
            yield return WaitSeconds(2f);
        }

        private IEnumerator BeatBlindDuel()
        {
            _beatIndex = 3;
            _beatLabel = "④ DUEL AVEUGLE — défense annulée.";
            bool attacked = false;
            try
            {
                var mina = FindMina();
                var foe = NearestEnemy(mina);
                if (mina != null && foe != null && foe.Stats.IsAlive)
                {
                    _arena.Log($"🎯 Démo : duel aveugle sur {foe.Stats.Name} (sans défense).");
                    _arena.ExecuteAttack(BodyPart.Torse, false,
                        SkillType.Ballistique, SkillType.Esquive, false,
                        weaponBaseDamage: 5, explicitTarget: foe, explicitAttacker: mina);
                    attacked = true;
                }
            }
            catch (Exception e) { _arena.Log($"🎬 Duel ignoré : {e.Message}"); }
            if (attacked) yield return WaitResolving(10f);
            yield return WaitSeconds(2f);
        }

        private IEnumerator BeatPickup()
        {
            _beatIndex = 4;
            _beatLabel = "⑤ RAMASSAGE — 1 PA au contact.";
            try
            {
                var mina = FindMina();
                if (mina != null)
                {
                    var item = new InventoryItem
                    {
                        Name = "Pistolet de dotation (démo)",
                        Type = ItemType.Weapon,
                        BaseDamage = 5,
                        RangeInTiles = 8
                    };
                    Vector3 pos = mina.transform.position + new Vector3(0.5f, 0.3f, 0.5f);
                    var pickup = DroppedWeaponPickup.SpawnAt(item, pos, "Démo");
                    if (pickup != null && pickup.TryPickup(mina))
                        _arena.Log($"🎒 Démo : {mina.Stats.Name} ramasse {item.Name}.");
                    else
                        _arena.Log("🎒 Démo : arme au sol (ramassage manuel au contact).");
                }
            }
            catch (Exception e) { _arena.Log($"🎬 Ramassage ignoré : {e.Message}"); }
            yield return WaitSeconds(3f);
        }

        private IEnumerator BeatSillage()
        {
            _beatIndex = 5;
            _beatLabel = "⑥ SILLAGE IGNÉ — dash Mina + feu Lucas.";
            IEnumerator dashRoutine = null;
            bool ready = false;
            TacticalUnit mina = null;
            TacticalUnit lucas = null;
            List<TacticalUnit> foes = null;
            try
            {
                mina = FindMina();
                lucas = FindLucas();
                foes = AliveEnemies();
                if (mina != null && lucas != null && foes.Count > 0)
                {
                    var dash = FreeTileNear(foes[0].CurrentCoords);
                    if (dash.HasValue)
                    {
                        var path = _arena.Pathfinder.FindPath(
                            mina.CurrentCoords, dash.Value, 99, out int cost, mina.FootprintType);
                        if (path != null && path.Count > 1)
                            dashRoutine = mina.MoveAlongPath(path, _grid, mina.ComputeMovementAPCost(cost));
                    }
                    ready = true;
                }
                else _arena.Log("🎬 Sillage ignoré : binôme ou cible manquant.");
            }
            catch (Exception e) { _arena.Log($"🎬 Sillage ignoré : {e.Message}"); ready = false; }
            if (dashRoutine != null) yield return dashRoutine;
            if (ready && mina != null && lucas != null && foes != null)
            {
                try
                {
                    var stats = new List<CharacterStats>();
                    foreach (var f in foes) stats.Add(f.Stats);
                    DuoTechRegistry.ResolveSillage(
                        mina.Stats, lucas.Stats, stats, WeaveSyncGrade.Perfect, _arena.Log);
                }
                catch (Exception e) { _arena.Log($"🎬 Sillage ignoré : {e.Message}"); }
            }
            yield return WaitSeconds(3f);
        }

        private IEnumerator WaitResolving(float timeout)
        {
            float t = 0f;
            while (t < timeout)
            {
                if (_stopRequested) yield break;
                try { if (!_arena.IsResolving) yield break; }
                catch { yield break; }
                t += Time.deltaTime;
                yield return null;
            }
        }

        private void EnsureLucas()
        {
            if (FindLucas() != null) return;
            var mina = FindMina();
            var anchor = mina != null ? mina.CurrentCoords : new HexCoordinates(0, 0);
            var spot = FreeTileNear(anchor) ?? anchor;
            var sheet = LucasCharacter.BuildHeroicSheet();
            var lucas = _arena.SpawnCustomCharacter(sheet, spot, true);
            if (lucas != null) _arena.Log("🎬 Démo : Lucas rejoint l'escouade (binôme Sillage).");
        }

        private TacticalUnit FindMina()
        {
            var all = TacticalUnitRegistry.AllUnits;
            for (int i = 0; i < all.Count; i++)
            {
                var u = all[i];
                if (u != null && u.Stats != null && u.Stats.IsAlive && u.IsPlayerControlled
                    && MinaCharacter.IsMina(u.Stats.Sheet)) return u;
            }
            return _arena.PlayerUnit;
        }

        private TacticalUnit FindLucas()
        {
            var all = TacticalUnitRegistry.AllUnits;
            for (int i = 0; i < all.Count; i++)
            {
                var u = all[i];
                if (u != null && u.Stats != null && u.Stats.IsAlive
                    && LucasCharacter.IsLucas(u.Stats.Sheet)) return u;
            }
            return null;
        }

        private TacticalUnit NearestEnemy(TacticalUnit from)
        {
            if (from == null) return null;
            TacticalUnit best = null;
            int bestD = int.MaxValue;
            var all = TacticalUnitRegistry.AllUnits;
            for (int i = 0; i < all.Count; i++)
            {
                var u = all[i];
                if (u == null || u.Stats == null || !u.Stats.IsAlive || u.IsPlayerControlled) continue;
                int d = from.CurrentCoords.DistanceTo(u.CurrentCoords);
                if (d < bestD) { bestD = d; best = u; }
            }
            return best;
        }

        private List<TacticalUnit> AliveEnemies()
        {
            var list = new List<TacticalUnit>();
            var all = TacticalUnitRegistry.AllUnits;
            for (int i = 0; i < all.Count; i++)
            {
                var u = all[i];
                if (u != null && u.Stats != null && u.Stats.IsAlive && !u.IsPlayerControlled)
                    list.Add(u);
            }
            return list;
        }

        private HexCoordinates? FreeTileNear(HexCoordinates center)
        {
            var candidates = new List<HexCoordinates> { center };
            for (int r = 1; r <= 3; r++)
                for (int dq = -r; dq <= r; dq++)
                    for (int dr = -r; dr <= r; dr++)
                        candidates.Add(new HexCoordinates(center.Q + dq, center.R + dr));
            var occupied = new HashSet<HexCoordinates>();
            var all = TacticalUnitRegistry.AllUnits;
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null) occupied.Add(all[i].CurrentCoords);
            foreach (var c in candidates)
            {
                if (occupied.Contains(c)) continue;
                try
                {
                    var node = _grid.GetNode(c);
                    if (node != null && node.IsWalkable) return c;
                }
                catch { /* case hors grille */ }
            }
            return null;
        }
    }
}

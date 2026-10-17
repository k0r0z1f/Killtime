using UnityEngine;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Audio;
using Killtime.Core.Character;
using Killtime.Core.Chrono;

namespace Killtime.Tactics.Arena
{
    /// <summary>
    /// Fleuve du Temps (snapshots / rewind) + cheats développeur (Refill, Heal,
    /// Kill, Remove). Logique déplacée verbatim depuis CombatDevArena.
    /// La résolution de case libre est déléguée à ArenaUnitStore (pas de doublon).
    /// </summary>
    public sealed class ArenaChronoDebugService
    {
        private readonly ArenaContext _ctx;
        private readonly ArenaUnitStore _units;
        private readonly TimelineBranch _timeline;
        private ArenaState _state => _ctx.State;
        private IArenaHost _host => _ctx.Host;

        public ArenaChronoDebugService(ArenaContext ctx, ArenaUnitStore units, TimelineBranch timeline)
        {
            _ctx = ctx;
            _units = units;
            _timeline = timeline;
        }

        public void RecordChronoSnapshot(string description)
        {
            var snap = new TacticalTimeSnapshot(_ctx.TurnManager.CurrentRound, 0.0f, description);
            if (_state.PlayerUnit != null && _state.PlayerUnit.Stats != null)
            {
                snap.RecordUnit(_state.PlayerUnit.Stats.Name, _state.PlayerUnit.Stats, _state.PlayerUnit.CurrentCoords.Q, _state.PlayerUnit.CurrentCoords.R);
            }
            foreach (var p in _state.AdditionalPlayers)
            {
                if (p != null && p.Stats != null)
                {
                    snap.RecordUnit(p.Stats.Name, p.Stats, p.CurrentCoords.Q, p.CurrentCoords.R);
                }
            }
            foreach (var d in _state.SparringDummies)
            {
                if (d != null && d.Stats != null)
                {
                    snap.RecordUnit(d.Stats.Name, d.Stats, d.CurrentCoords.Q, d.CurrentCoords.R);
                }
            }

            _timeline.PushSnapshot(snap);
        }

        public void RewindLastSnapshot()
        {
            var snap = _timeline.RewindLastAction();
            if (snap == null)
            {
                _host.ArenaLog("⏳ Fleuve du Temps : Aucun snapshot antérieur disponible.");
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayUI(SoundId.UI_Denied, 0.7f);
                return;
            }

            RestoreSnapshot(snap);
            if (KilltimeAudioManager.Instance != null)
                KilltimeAudioManager.Instance.Play(SoundId.Rewind_Time, 0.9f);
            _host.ArenaLog($"⏪ <b>Rembobinage Temporel (Livre V)</b> : Retour au snapshot '{snap.ActionDescription}' (Round {snap.RoundNumber})");
        }

        private void RestoreSnapshot(TacticalTimeSnapshot snap)
        {
            if (snap.UnitStates.TryGetValue(_state.PlayerUnit.Stats.Name, out var pSnap))
            {
                RestoreUnitState(_state.PlayerUnit, pSnap);
            }

            foreach (var p in _state.AdditionalPlayers)
            {
                if (p != null && snap.UnitStates.TryGetValue(p.Stats.Name, out var extraSnap))
                {
                    RestoreUnitState(p, extraSnap);
                }
            }

            foreach (var d in _state.SparringDummies)
            {
                if (d != null && snap.UnitStates.TryGetValue(d.Stats.Name, out var dSnap))
                {
                    RestoreUnitState(d, dSnap);
                }
            }
        }

        private void RestoreUnitState(TacticalUnit unit, UnitTimeSnapshot snap)
        {
            unit.Stats.CurrentHealth = snap.Health;
            unit.Stats.CurrentActionPoints = snap.ActionPoints;
            unit.Stats.Essoufflement = snap.Essoufflement;
            unit.Stats.ActiveStatus = snap.Status;

            var targetCoords = new HexCoordinates(snap.GridCoordQ, snap.GridCoordR);
            // Rembobinage : ne restaure la position que si elle est libre,
            // sinon relocalise à côté plutôt que d'empiler deux avatars.
            if (unit.CurrentCoords.Equals(targetCoords)) return;
            if (!unit.TeleportTo(targetCoords, _ctx.Grid))
            {
                var fallback = _units.ResolveFreeCoords(targetCoords);
                if (!fallback.Equals(targetCoords))
                {
                    unit.TeleportTo(fallback, _ctx.Grid);
                    _host.ArenaLog($"⏪ <b>{unit.Stats.Name}</b> relocalisé en ({fallback.Q}, {fallback.R}) : case d'origine occupée.");
                }
            }
        }

        public void RefillAPAll()
        {
            if (_state.PlayerUnit != null) _state.PlayerUnit.Stats.CurrentActionPoints = _state.PlayerUnit.Stats.MaxActionPoints;
            foreach (var p in _state.AdditionalPlayers) if (p != null) p.Stats.CurrentActionPoints = p.Stats.MaxActionPoints;
            foreach (var d in _state.SparringDummies) if (d != null) d.Stats.CurrentActionPoints = d.Stats.MaxActionPoints;
            if (KilltimeAudioManager.Instance != null) KilltimeAudioManager.Instance.PlayUI(SoundId.PA_Refill, 0.8f);
            _host.ArenaLog("⚡ Points d'Action réinitialisés au maximum pour toutes les unités.");
        }

        public void HealAndCureAll()
        {
            if (_state.PlayerUnit != null)
            {
                _state.PlayerUnit.Stats.CurrentHealth = _state.PlayerUnit.Stats.MaxHealth;
                _state.PlayerUnit.Stats.ClearAllStatus();
                _state.PlayerUnit.Stats.Essoufflement = 0;
            }
            foreach (var p in _state.AdditionalPlayers)
            {
                if (p != null)
                {
                    p.Stats.CurrentHealth = p.Stats.MaxHealth;
                    p.Stats.ClearAllStatus();
                    p.Stats.Essoufflement = 0;
                }
            }
            foreach (var d in _state.SparringDummies)
            {
                if (d != null)
                {
                    d.Stats.CurrentHealth = d.Stats.MaxHealth;
                    d.Stats.ClearAllStatus();
                    d.Stats.Essoufflement = 0;
                }
            }
            _host.ArenaLog("❤️ <b>Soin Intégral :</b> Tous les PV restaurés et statuts négatifs dissipés.");
            if (KilltimeAudioManager.Instance != null) KilltimeAudioManager.Instance.Play(SoundId.Heal, 0.85f);
        }

        public void KillCurrentTarget()
        {
            if (_state.CurrentTarget != null)
            {
                _state.CurrentTarget.Stats.CurrentHealth = 0;
                _state.CurrentTarget.Stats.IsDead = true;
                _state.CurrentTarget.Stats.ActiveStatus |= StatusEffect.Inconscient | StatusEffect.ATerre;
                var vis = _state.CurrentTarget.GetComponent<TacticalUnitVisual>();
                vis?.TriggerFallingBackDeath();
                vis?.SpawnFloatingText("K.O. TOTAL!", Color.red);
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayAt(SoundId.Death_Instant, _state.CurrentTarget.transform.position, 0.9f);
                _host.ArenaLog($"💀 {_state.CurrentTarget.Stats.Name} a été terrassé par commande développeur.");

                if (Killtime.Multi.VTTTableSync.Instance != null
                    && Killtime.Multi.VTTRoomManager.Instance != null
                    && Killtime.Multi.VTTRoomManager.Instance.InRoom
                    && Killtime.Multi.VTTRoomManager.Instance.IsGM)
                {
                    Killtime.Multi.VTTTableSync.Instance.BroadcastFullCombatState();
                }
            }
        }

        public void RemoveUnit(TacticalUnit unit)
        {
            if (unit == null) return;

            string unitName = unit.Stats != null ? unit.Stats.Name : unit.name;

            CombatUI.TacticalSelectionManager.Instance?.ClearSelection();

            if (_state.CurrentTarget == unit)
            {
                _state.CurrentTarget = null;
            }

            if (_state.PlayerUnit == unit)
            {
                _state.PlayerUnit = null;
            }

            _state.SparringDummies.Remove(unit);
            _state.AdditionalPlayers.Remove(unit);
            _state.CustomSpawns.RemoveAll(r => r != null && r.Sheet != null && r.Sheet.Name == unitName);

            _ctx.TurnManager?.UnregisterUnit(unit);

            if (_ctx.Grid != null)
            {
                var node = _ctx.Grid.GetNode(unit.CurrentCoords);
                if (node != null)
                {
                    node.IsOccupied = false;
                }
            }

            if (_state.CurrentTarget == null)
            {
                _host.RequestAutoTargetNextAlive();
            }

            _host.ArenaLog($"🗑️ <b>{unitName}</b> a été retiré(e) du champ de bataille.");
            Object.Destroy(unit.gameObject);
        }
    }
}

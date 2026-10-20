using UnityEngine;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Audio;
using Killtime.Core.Character;
using Killtime.Core.Chrono;
using Killtime.Core.Rules;

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
        private readonly System.Collections.Generic.Dictionary<TimelineId, TimelineBranch> _branches;
        public TimelineId ActiveBranchId { get; private set; } = TimelineId.Timeline0_Prime;
        private ArenaState _state => _ctx.State;
        private IArenaHost _host => _ctx.Host;

        /// <summary>Historique de la branche active (0/A/B/C/D).</summary>
        public TimelineBranch ActiveTimeline => _branches != null && _branches.TryGetValue(ActiveBranchId, out var b) ? b : _timeline;

        public ArenaChronoDebugService(ArenaContext ctx, ArenaUnitStore units, TimelineBranch timeline)
        {
            _ctx = ctx;
            _units = units;
            _timeline = timeline;
            _branches = new System.Collections.Generic.Dictionary<TimelineId, TimelineBranch>
            {
                { TimelineId.Timeline0_Prime, timeline ?? new TimelineBranch(TimelineId.Timeline0_Prime) },
                { TimelineId.TimelineA_Alpha, new TimelineBranch(TimelineId.TimelineA_Alpha) },
                { TimelineId.TimelineB_Beta, new TimelineBranch(TimelineId.TimelineB_Beta) },
                { TimelineId.TimelineC_Gamma, new TimelineBranch(TimelineId.TimelineC_Gamma) },
                { TimelineId.TimelineD_Delta, new TimelineBranch(TimelineId.TimelineD_Delta) },
            };
            ActiveBranchId = TimelineId.Timeline0_Prime;
        }

        public bool SwitchBranch(TimelineId id)
        {
            if (!_branches.ContainsKey(id)) return false;
            ActiveBranchId = id;
            _host.ArenaLog($"🌊 <b>Fleuve du Temps</b> : ligne causale <b>{id}</b> active ({ActiveTimeline.Count} snapshots).");
            return true;
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

            ActiveTimeline.PushSnapshot(snap);
        }

        public void RewindToSnapshot(int index)
        {
            var snap = ActiveTimeline.RewindTo(index);
            if (snap == null)
            {
                _host.ArenaLog("⏳ Fleuve du Temps : index de snapshot invalide.");
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayUI(SoundId.UI_Denied, 0.7f);
                return;
            }

            RestoreSnapshot(snap);
            if (KilltimeAudioManager.Instance != null)
                KilltimeAudioManager.Instance.Play(SoundId.Rewind_Time, 0.9f);
            _host.ArenaLog($"⏪ <b>Rembobinage Temporel (Livre IX)</b> : Retour au snapshot #{index} '{snap.ActionDescription}' (Round {snap.RoundNumber}, ligne {ActiveBranchId})");
        }

        public void RewindLastSnapshot()
        {
            var snap = ActiveTimeline.RewindLastAction();
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
            _host.ArenaLog($"⏪ <b>Rembobinage Temporel (Livre IX)</b> : Retour au snapshot '{snap.ActionDescription}' (Round {snap.RoundNumber})");
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

        // =====================================================================
        // RD-187 : SOCLE CHEATS CIBLÉS (dev-only, aucune règle modifiée).
        // Set-PV (dont seuil moral 28 %), set-PA/PE par unité, statuts on/off,
        // enrayement forcé, purge chrono par branche + zones persistantes.
        // =====================================================================

        /// <summary>Durée de test pour les statuts non persistants posés via cheat.</summary>
        private const int DebugStatusTestDuration = 99;

        /// <summary>Toutes les unités debuggables : joueur + alliés + mannequins.</summary>
        public System.Collections.Generic.List<TacticalUnit> AllDebugUnits()
        {
            var list = new System.Collections.Generic.List<TacticalUnit>();
            if (_state.PlayerUnit != null) list.Add(_state.PlayerUnit);
            foreach (var p in _state.AdditionalPlayers) if (p != null && !list.Contains(p)) list.Add(p);
            foreach (var d in _state.SparringDummies) if (d != null && !list.Contains(d)) list.Add(d);
            return list;
        }

        public bool SetUnitHealth(TacticalUnit unit, int hp)
        {
            if (unit == null || unit.Stats == null) return false;
            int max = Mathf.Max(1, unit.Stats.MaxHealth);
            int v = Mathf.Clamp(hp, 0, max);
            unit.Stats.CurrentHealth = v;
            if (v <= 0)
            {
                unit.Stats.IsDead = true;
                unit.Stats.ActiveStatus |= StatusEffect.Inconscient | StatusEffect.ATerre;
            }
            else if (unit.Stats.IsDead)
            {
                unit.Stats.IsDead = false;
                unit.Stats.RemoveStatus(StatusEffect.Inconscient);
            }
            _host.ArenaLog($"🎯 <b>{unit.Stats.Name}</b> : PV fixés à <b>{v}/{max}</b> (cheat dev).");
            return true;
        }

        public bool SetUnitAP(TacticalUnit unit, int ap)
        {
            if (unit == null || unit.Stats == null) return false;
            int max = Mathf.Max(0, unit.Stats.MaxActionPoints);
            int v = Mathf.Clamp(ap, 0, max);
            unit.Stats.CurrentActionPoints = v;
            _host.ArenaLog($"🎯 <b>{unit.Stats.Name}</b> : PA fixés à <b>{v}/{max}</b> (cheat dev).");
            return true;
        }

        public static int GetMaxPEFor(CharacterStats stats)
        {
            if (stats == null) return 10;
            int max = stats.Attributes.Constitution;
            try
            {
                if (stats.HasSpecialization("Restructuration Neurale : Volonté d'Inflexible")) max += 2;
            }
            catch { /* spécialisations optionnelles */ }
            return Mathf.Max(1, max);
        }

        public bool SetUnitPE(TacticalUnit unit, int pe)
        {
            if (unit == null || unit.Stats == null) return false;
            int max = GetMaxPEFor(unit.Stats);
            int v = Mathf.Clamp(pe, 0, max);
            unit.Stats.Essoufflement = v;
            _host.ArenaLog($"🎯 <b>{unit.Stats.Name}</b> : PE (Essoufflement) fixés à <b>{v}/{max}</b> (cheat dev).");
            return true;
        }

        public bool SetUnitStatus(TacticalUnit unit, StatusEffect effect, bool on)
        {
            if (unit == null || unit.Stats == null || effect == StatusEffect.None) return false;
            if (on) unit.Stats.ApplyStatus(effect, DebugStatusTestDuration);
            else unit.Stats.RemoveStatus(effect);
            _host.ArenaLog($"🎯 <b>{unit.Stats.Name}</b> : statut <b>{effect}</b> {(on ? "appliqué" : "retiré")} (cheat dev).");
            return true;
        }

        public bool ClearUnitStatuses(TacticalUnit unit)
        {
            if (unit == null || unit.Stats == null) return false;
            unit.Stats.ClearAllStatus();
            _host.ArenaLog($"🎯 <b>{unit.Stats.Name}</b> : tous les statuts purgés (cheat dev).");
            return true;
        }

        public bool ForceJam(TacticalUnit unit, bool jammed)
        {
            if (unit == null || unit.Stats == null) return false;
            var sheet = unit.GetOrBuildSheet();
            var weapon = sheet != null ? sheet.GetEquippedWeapon() : null;
            if (weapon == null)
            {
                _host.ArenaLog($"🎯 <b>{unit.Stats.Name}</b> : aucune arme équipée à {(jammed ? "enrayer" : "désenrayer")}.");
                return false;
            }
            weapon.Jammed = jammed;
            unit.NotifyInventoryChanged(true);
            _host.ArenaLog($"🎯 <b>{unit.Stats.Name}</b> : <b>{weapon.Name}</b> {(jammed ? "ENRAYÉE 🔧" : "désenrayée ✔")} (cheat dev).");
            return true;
        }

        public int GetBranchCount(TimelineId id)
        {
            if (_branches != null && _branches.TryGetValue(id, out var b) && b != null) return b.Count;
            return 0;
        }

        public void PurgeChronoBranch(TimelineId id)
        {
            if (_branches != null && _branches.TryGetValue(id, out var b) && b != null)
            {
                int n = b.Count;
                b.Clear();
                _host.ArenaLog($"🌊 <b>Fleuve du Temps</b> : branche <b>{id}</b> purgée ({n} snapshot(s) supprimé(s), cheat dev).");
            }
        }

        public void PurgeAllChronoBranches()
        {
            int total = 0;
            if (_branches != null)
            {
                foreach (var kv in _branches)
                {
                    if (kv.Value == null) continue;
                    total += kv.Value.Count;
                    kv.Value.Clear();
                }
            }
            _host.ArenaLog($"🌊 <b>Fleuve du Temps</b> : toutes les branches purgées ({total} snapshot(s) supprimé(s), cheat dev).");
        }

        public void PurgeZones()
        {
            int n = _state.SmokeZones.Count;
            _state.SmokeZones.Clear();
            try { _ctx.Visualizer?.UpdatePersistentZones(_state.SmokeZones); }
            catch { /* visualiseur optionnel */ }
            _host.ArenaLog($"🌫️ <b>Zones persistantes</b> purgées ({n} zone(s) supprimée(s), cheat dev).");
        }

        // RD-191 : DoT calibrés / soins / résurrection (dev-only, 0 règle modifiée).
        public bool ApplyDebugDot(TacticalUnit unit, StatusEffect dot, int pool, bool aggravating)
        {
            if (unit == null || unit.Stats == null) return false;
            if (!CharacterStats.IsDotStatus(dot))
            {
                _host.ArenaLog($"☠️ <b>DoT dev</b> : <b>{dot}</b> n'est pas un DoT (Saignement/Empoisonne/EnFeu/Asphyxie requis).");
                return false;
            }
            int v = Mathf.Max(1, pool);
            unit.Stats.ApplyResidualDamage(dot, v, aggravating);
            _host.ArenaLog($"☠️ <b>DoT dev</b> : <b>{unit.Stats.Name}</b> ← <b>{dot}</b> pool <b>{v}</b>{(aggravating ? " (aggravé +1/tour)" : "")}.");
            return true;
        }

        public int DebugHeal(TacticalUnit unit, int amount)
        {
            if (unit == null || unit.Stats == null) return 0;
            int healed = unit.Stats.Heal(Mathf.Max(0, amount));
            _host.ArenaLog($"💉 <b>Soin dev</b> : <b>{unit.Stats.Name}</b> +{healed} PV — <b>Souffrant persiste</b> (soin ordinaire ne lève jamais).");
            return healed;
        }

        public MajorCareResult DebugMajorCare(TacticalUnit unit, int amount)
        {
            var empty = new MajorCareResult { RequestedAmount = Mathf.Max(0, amount), Log = string.Empty };
            if (unit == null || unit.Stats == null) return empty;
            var res = unit.Stats.ApplyMajorCare(Mathf.Max(0, amount));
            _host.ArenaLog($"🩹 <b>Soins majeurs dev</b> : {res.Log}");
            return res;
        }

        public int DebugResurrect(TacticalUnit unit, int healthAfter = 1)
        {
            if (unit == null || unit.Stats == null) return 0;
            int duration = unit.Stats.ApplyResurrection(Mathf.Max(1, healthAfter));
            int conval = unit.Stats.GetResurrectionSequelaeRemainingDays();
            _host.ArenaLog($"🕊️ <b>Résurrection dev</b> : <b>{unit.Stats.Name}</b> rappelé(e) à <b>{unit.Stats.CurrentHealth} PV</b> — convalescence <b>{duration}j</b> (reste {conval}j), séquelle <b>-1 ec</b> (Poids du Trépas).");
            return duration;
        }

        public DotTickResult DebugForceDotTick(TacticalUnit unit)
        {
            var empty = new DotTickResult();
            if (unit == null || unit.Stats == null) return empty;
            var res = unit.Stats.TickTurnStatus();
            _host.ArenaLog($"☠️ <b>Tick DoT dev</b> : <b>{unit.Stats.Name}</b> — dégâts <b>{res.TotalDamage}</b> (bouclier {res.ShieldAbsorbed}, PA drainés {res.PaDrained}).");
            return res;
        }

        // RD-192 : Moral / reddition / déroute à la demande (dev-only, 0 règle modifiée).
        // Jet réel CheckMoraleAtThreshold (Max CHA/INS vs SD, seuil 28 %) — les toggles
        // 🎲 crit/raté RD-187 pilotent l'issue ; forçages directs pour le terrain.
        public MoraleCheckResult DebugForceMoraleCheck(TacticalUnit unit)
        {
            var empty = new MoraleCheckResult();
            if (unit == null || unit.Stats == null) return empty;
            var st = unit.Stats;
            bool wasTested = st.HasTestedMorale;
            st.HasTestedMorale = false;
            var res = st.CheckMoraleAtThreshold(_ctx.Dice, CoreRulesConfig.Instance.StandardTargetDC);
            if (!res.Triggered) st.HasTestedMorale = wasTested;
            var vis = unit.GetComponent<TacticalUnitVisual>();
            if (!res.Triggered)
            {
                int threshold = Mathf.CeilToInt(st.MaxHealth * 0.28f);
                _host.ArenaLog($"🧠 <b>Jet moral dev</b> : <b>{st.Name}</b> non déclenché (PV {st.CurrentHealth}/{st.MaxHealth}, seuil 28 % = {threshold}) — baisser les PV (bouton 28 %) puis réessayer.");
                return res;
            }
            _host.ArenaLog($"🧠 <b>Jet moral dev</b> : {res.Log}");
            if (res.Surrendered)
            {
                vis?.SpawnFloatingText("🏳️ REDDITION !", Color.white);
                vis?.DropHeldItemsWithPhysics();
                vis?.SetCombatStance(false);
            }
            else if (res.Routed)
            {
                vis?.SpawnFloatingText("😱 DÉROUTE ! [Agonisant]", new Color(0.95f, 0.2f, 0.85f));
            }
            else
            {
                vis?.SpawnFloatingText("🧠 Sang-froid préservé", Color.cyan);
            }
            return res;
        }

        public bool DebugForceSurrender(TacticalUnit unit)
        {
            if (unit == null || unit.Stats == null) return false;
            var st = unit.Stats;
            st.HasTestedMorale = true;
            st.IsSurrendered = true;
            st.RemoveStatus(StatusEffect.Agonisant);
            var vis = unit.GetComponent<TacticalUnitVisual>();
            vis?.SpawnFloatingText("🏳️ REDDITION !", Color.white);
            vis?.DropHeldItemsWithPhysics();
            vis?.SetCombatStance(false);
            _host.ArenaLog($"🏳️ <b>Reddition forcée dev</b> : <b>{st.Name}</b> dépose les armes (exclu du ciblage offensif).");
            return true;
        }

        public bool DebugForceRout(TacticalUnit unit)
        {
            if (unit == null || unit.Stats == null) return false;
            var st = unit.Stats;
            st.HasTestedMorale = true;
            st.IsSurrendered = false;
            st.ApplyStatus(StatusEffect.Agonisant, -1);
            var vis = unit.GetComponent<TacticalUnitVisual>();
            vis?.SpawnFloatingText("😱 DÉROUTE ! [Agonisant]", new Color(0.95f, 0.2f, 0.85f));
            _host.ArenaLog($"😱 <b>Déroute forcée dev</b> : <b>{st.Name}</b> ← [Agonisant] (fuite panique ExecutePanickedFlee).");
            return true;
        }

        public bool DebugRearmMorale(TacticalUnit unit)
        {
            if (unit == null || unit.Stats == null) return false;
            var st = unit.Stats;
            st.RearmMorale();
            int threshold = Mathf.CeilToInt(st.MaxHealth * 0.28f);
            _host.ArenaLog($"🔄 <b>Moral réarmé dev</b> : <b>{st.Name}</b> (PV {st.CurrentHealth}/{st.MaxHealth}, seuil 28 % = {threshold}) — prochain passage sous le seuil re-teste.");
            return true;
        }

        public bool DebugSetSurvivor(TacticalUnit unit, bool survivor)
        {
            if (unit == null || unit.Stats == null) return false;
            unit.Stats.IsSurvivor = survivor;
            _host.ArenaLog($"🎭 <b>Profil dev</b> : <b>{unit.Stats.Name}</b> Survivor = <b>{(survivor ? "OUI (reddition)" : "NON (déroute)")}</b>.");
            return true;
        }
    }
}

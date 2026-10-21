using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Audio;
using Killtime.Core.Character;
using Killtime.Core.Combat;

namespace Killtime.Tactics.Arena
{
    /// <summary>
    /// Réactions aux événements du TurnManager (initiative, tours, rounds,
    /// fin de combat, expiration de statuts). Logique déplacée verbatim
    /// depuis CombatDevArena. Les abonnements RESTENT en façade (F3).
    /// Le ciblage passe par ArenaTargetingService (pas de cycle).
    /// </summary>
    public sealed class ArenaTurnEventsService
    {
        private readonly ArenaContext _ctx;
        private readonly ArenaSmokeService _smoke;
        private readonly ArenaTargetingService _targeting;
        private ArenaState _state => _ctx.State;
        private IArenaHost _host => _ctx.Host;

        /// <summary>Enregistre un snapshot chrono (fourni par la façade).</summary>
        public Action<string> SnapshotRecorder;

        public ArenaTurnEventsService(ArenaContext ctx, ArenaSmokeService smoke, ArenaTargetingService targeting)
        {
            _ctx = ctx;
            _smoke = smoke;
            _targeting = targeting;
        }

        public void HandleUnitStatusExpired(TacticalUnit unit, List<StatusEffect> expiredList)
        {
            for (int i = 0; i < expiredList.Count; i++)
            {
                _host.ArenaLog($"⏳ <b>{unit.Stats.Name}</b> : L'altération <b>[{expiredList[i]}]</b> s'est dissipée (Fin de tour).");
            }
        }

        public void HandleInitiativeRolled(IReadOnlyList<InitiativeRollResult> results)
        {
            _host.ArenaLog("🎲 <b>Jet d'Initiative (Livre I §4.3)</b> — Ordre de passage établi :");
            for (int i = 0; i < results.Count; i++)
            {
                var r = results[i];
                if (r.Unit == null || r.Unit.Stats == null) continue;
                int baseValue = r.Unit.Stats.GetInitiativeBaseValue();
                _host.ArenaLog($"{i + 1}. <b>{r.Unit.Stats.Name}</b> — Dé: {r.Die} (Base {baseValue}) | Jet: {r.RawRoll}");
            }
        }

        public void HandleCombatEnded(CombatOutcome outcome)
        {
            var units = TacticalUnitRegistry.AllUnits;
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                var vis = u.GetComponent<TacticalUnitVisual>();
                vis?.SetCombatStance(false);
                // Champs rechargés hors combat (Livre VIII §32.2).
                if (u != null && u.Stats != null) u.Stats.RefillShield();
            }

            if (KilltimeAudioManager.Instance != null)
            {
                if (outcome == CombatOutcome.Victory)
                {
                    KilltimeAudioManager.Instance.Play(SoundId.Victory_Stinger);
                    KilltimeAudioManager.Instance.PlayStinger(MusicMood.Victory);
                    KilltimeAudioManager.Instance.PlayMusic(MusicMood.Victory, MusicIntensity.Intense, true);
                }
                else if (outcome == CombatOutcome.Defeat)
                {
                    KilltimeAudioManager.Instance.Play(SoundId.Defeat_Stinger);
                    KilltimeAudioManager.Instance.PlayStinger(MusicMood.Defeat);
                    KilltimeAudioManager.Instance.PlayMusic(MusicMood.Defeat, MusicIntensity.Calm, true);
                }
            }
        }

        public void HandleTurnStarted(TacticalUnit unit)
        {
            if (!_ctx.IsStoryScene() && _ctx.Camera != null && unit != null)
            {
                _ctx.Camera.FocusOn(unit.transform);
            }

            if (_ctx.TurnManager != null && _ctx.TurnManager.IsInExploration)
            {
                return;
            }

            var visual = unit != null ? unit.GetComponent<TacticalUnitVisual>() : null;
            visual?.SetCombatStance(true);

            if (_state.CurrentTarget == null || _state.CurrentTarget == unit || !_state.CurrentTarget.Stats.IsAlive)
            {
                _targeting.AutoTargetOpponent(unit);
            }

            if (KilltimeAudioManager.Instance != null && unit != null)
                KilltimeAudioManager.Instance.PlayAt(SoundId.Turn_Start, unit.transform.position, 0.6f);

            _state.AdaptiveTurnCount++;
            _host.RequestMusicUpdate();

            if (unit != null)
            {
                // Moteur Arcanique de Poche : régénération passive de bouclier (+2 PV)
                if (unit.Stats.MaxShieldHP > 0 && unit.Stats.CurrentShieldHP < unit.Stats.MaxShieldHP
                    && SmokeScreen.HasItemByName(unit.Stats, "Moteur"))
                {
                    unit.Stats.CurrentShieldHP = Math.Min(unit.Stats.MaxShieldHP, unit.Stats.CurrentShieldHP + 2);
                    visual?.SpawnFloatingText("⚙️ Moteur Arcanique (+2 Bouclier)", Color.cyan);
                    _host.ArenaLog($"⚙️ <b>Moteur Arcanique de Poche</b> : flux continu vers le champ de force de <b>{unit.Stats.Name}</b> (+2 PV Bouclier).");
                }

                _host.ArenaLog($"--- Tour de 10s : <b>{unit.Stats.Name}</b> (PA: {unit.Stats.CurrentActionPoints}/{unit.Stats.MaxActionPoints}) ---");
                SnapshotRecorder?.Invoke($"Début du tour de {unit.Stats.Name}");
                // RD-045 : DoT résiduel déjà appliqué par TurnManager (début de tour
                // personnel) — ici log + gestion létale, sans re-tick.
                _smoke.LogDotTick(unit);
                // Zones persistantes : l'unité subit son environnement en début de tour.
                _smoke.TickSmokeForUnit(unit, _state.SmokeZones);
            }
        }

        /// <summary>Décompte des zones à chaque round ; dissipation loggée.</summary>
        public void HandleRoundStarted(int round)
        {
            if (_state.SmokeZones.Count == 0) return;
            for (int i = _state.SmokeZones.Count - 1; i >= 0; i--)
            {
                var z = _state.SmokeZones[i];
                z.TurnsLeft--;
                if (z.TurnsLeft <= 0)
                {
                    _host.ArenaLog($"🌫️ La zone <b>{z.Kind}</b> en ({z.Center.Q},{z.Center.R}) se dissipe.");
                    _state.SmokeZones.RemoveAt(i);
                }
                else
                {
                    _state.SmokeZones[i] = z;
                }
            }
            _ctx.Visualizer?.UpdatePersistentZones(_state.SmokeZones);
        }
    }
}

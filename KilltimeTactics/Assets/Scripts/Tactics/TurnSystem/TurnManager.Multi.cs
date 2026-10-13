using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics.Units;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Objectives;
using Killtime.Core.Character;
using Killtime.Core.Combat;
using Killtime.Core.Dice;
using Killtime.Core.Rules;

namespace Killtime.Tactics.TurnSystem
{
    public partial class TurnManager : MonoBehaviour
    {
        public static bool IsMultiplayerPlayerClient()
        {
            var room = Killtime.Multi.VTTRoomManager.Instance;
            return room != null && room.InRoom && !room.IsGM;
        }

        public static bool IsMultiplayerGM()
        {
            var room = Killtime.Multi.VTTRoomManager.Instance;
            return room != null && room.InRoom && room.IsGM;
        }

        private void BroadcastRoundStartIfGM()
        {
            if (!IsMultiplayerGM()) return;
            var entries = new List<Killtime.Multi.VTTInitiativeEntry>(_allUnits.Count);
            for (int i = 0; i < _allUnits.Count; i++)
            {
                var u = _allUnits[i];
                if (u == null || u.Stats == null) continue;
                entries.Add(new Killtime.Multi.VTTInitiativeEntry
                {
                    unitId = u.gameObject.name,
                    die = u.Stats.InitiativeDie.ToString(),
                    rawRoll = u.Stats.InitiativeRollTotal,
                    total = u.Stats.InitiativeRollTotal,
                    rapidite = u.Stats.Attributes.Rapidite,
                    agilite = u.Stats.Attributes.Agilite,
                    intelligence = u.Stats.Attributes.Intelligence
                });
            }

            var payload = new Killtime.Multi.VTTTurnControlPayload
            {
                action = "round_start",
                round = CurrentRound,
                turnOrder = entries
            };
            Killtime.Multi.VTTTableSync.Instance?.BroadcastTurnControl(payload);
        }

        public void ApplyRemoteRoundStart(int round, List<Killtime.Multi.VTTInitiativeEntry> entries)
        {
            CurrentRound = round;
            IsCombatOver = false;
            CurrentOutcome = CombatOutcome.InProgress;
            _needsInitiativeRoll = false;

            if (entries != null && entries.Count > 0)
            {
                var map = new Dictionary<string, Killtime.Multi.VTTInitiativeEntry>();
                for (int i = 0; i < entries.Count; i++)
                {
                    if (entries[i] != null && !string.IsNullOrEmpty(entries[i].unitId))
                    {
                        map[entries[i].unitId] = entries[i];
                    }
                }

                for (int i = 0; i < _allUnits.Count; i++)
                {
                    var u = _allUnits[i];
                    if (u != null && u.Stats != null && map.TryGetValue(u.gameObject.name, out var entry))
                    {
                        if (Enum.TryParse<DiceType>(entry.die, out var d))
                        {
                            u.Stats.InitiativeDie = d;
                        }
                        u.Stats.InitiativeRollTotal = entry.total;
                    }
                }

                _allUnits.Sort((a, b) =>
                {
                    if (a == null) return 1;
                    if (b == null) return -1;
                    int initA = GetInitiativeTotal(a);
                    int initB = GetInitiativeTotal(b);
                    if (initA != initB) return initB.CompareTo(initA);
                    int rapA = (a.Stats != null) ? a.Stats.Attributes.Rapidite : 0;
                    int rapB = (b.Stats != null) ? b.Stats.Attributes.Rapidite : 0;
                    return rapB.CompareTo(rapA);
                });
            }

            OnRoundStarted?.Invoke(CurrentRound);
        }

        public void ApplyRemoteTurnStart(string activeUnitId)
        {
            if (string.IsNullOrEmpty(activeUnitId)) return;
            for (int i = 0; i < _allUnits.Count; i++)
            {
                var u = _allUnits[i];
                if (u != null && u.gameObject.name == activeUnitId)
                {
                    _activeUnitIndex = i;
                    ActiveUnit = u;
                    RefreshAndNotifyActiveUnitTurnStart(ActiveUnit);
                    OnTurnStarted?.Invoke(ActiveUnit);
                    return;
                }
            }
        }

        public void ApplyRemoteEndTurn(string activeUnitId)
        {
            if (ActiveUnit != null && ActiveUnit.Stats != null)
            {
                var expired = ActiveUnit.Stats.TickTurnStatusDurations();
                if (expired.Count > 0)
                {
                    var vis = ActiveUnit.GetComponent<TacticalUnitVisual>();
                    for (int i = 0; i < expired.Count; i++)
                    {
                        vis?.SpawnFloatingText($"[{expired[i]}] Dissipé", new Color(0.4f, 0.9f, 1.0f));
                    }
                    OnUnitStatusExpired?.Invoke(ActiveUnit, expired);
                }
            }
        }

        public void ApplyRemoteCombatEnded(string outcomeStr)
        {
            IsCombatOver = true;
            if (Enum.TryParse<CombatOutcome>(outcomeStr, out var outcome))
            {
                CurrentOutcome = outcome;
            }
            else
            {
                CurrentOutcome = CombatOutcome.InProgress;
            }
            OnCombatEnded?.Invoke(CurrentOutcome);
        }

        public void ApplyRemoteStateSync(int round, string activeUnitId, string outcomeStr)
        {
            if (round > 0) CurrentRound = round;
            if (!string.IsNullOrEmpty(activeUnitId))
            {
                for (int i = 0; i < _allUnits.Count; i++)
                {
                    if (_allUnits[i] != null && _allUnits[i].gameObject.name == activeUnitId)
                    {
                        ActiveUnit = _allUnits[i];
                        _activeUnitIndex = i;
                        break;
                    }
                }
            }
            if (!string.IsNullOrEmpty(outcomeStr) && Enum.TryParse<CombatOutcome>(outcomeStr, out var outcome))
            {
                CurrentOutcome = outcome;
                IsCombatOver = (outcome != CombatOutcome.InProgress);
            }
        }
    }
}

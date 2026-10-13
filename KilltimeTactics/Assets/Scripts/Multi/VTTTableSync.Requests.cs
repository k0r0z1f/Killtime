using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics;
using Killtime.Tactics.AI;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Core.Combat;
using Killtime.Core.Dice;
using Killtime.Core.Character;
using Killtime.CameraSystem;
using Killtime.Audio;
using Killtime.UI;

namespace Killtime.Multi
{
    public partial class VTTTableSync : MonoBehaviour
    {
        public void BroadcastCombatAction(VTTCombatActionPayload action)
        {
            if (_applyingRemote) return;
            if (action == null || _room == null || !_room.InRoom || !_room.IsGM) return;
            _room.SendTableOp(VTTProtocol.OpCombatAction, JsonUtility.ToJson(action));
        }

        public void BroadcastTurnControl(VTTTurnControlPayload turnControl)
        {
            if (_applyingRemote) return;
            if (turnControl == null || _room == null || !_room.InRoom || !_room.IsGM) return;
            _room.SendTableOp(VTTProtocol.OpTurnControl, JsonUtility.ToJson(turnControl));
        }

        public void BroadcastRoomSettings()
        {
            if (_room == null || !_room.InRoom || !_room.IsGM) return;
            if (_aiController == null) _aiController = FindAnyObjectByType<TacticalAIController>();
            if (_aiController == null) return;

            var payload = new VTTRoomSettingsPayload
            {
                isAIEnabled = _aiController.IsAIEnabled,
                aiMode = (int)_aiController.Mode,
                aiPersonality = (int)_aiController.DefaultPersonality,
                aiActionDelay = _aiController.ActionDelay,
                aiRetreatRatio = _aiController.RetreatHealthRatio,
                aiDefensiveReserve = _aiController.DefensiveAPReserve,
                activeMapName = ActiveMapName ?? ""
            };
            _room.SendTableOp(VTTProtocol.OpRoomSettings, JsonUtility.ToJson(payload));
        }

        public void BroadcastArenaReset(int layoutType = -1)
        {
            if (_applyingRemote) return;
            if (_room == null || !_room.InRoom || !_room.IsGM) return;

            var payload = new VTTTurnControlPayload
            {
                action = VTTProtocol.TurnActionArenaReset,
                round = 1,
                targetQ = layoutType
            };
            _room.SendTableOp(VTTProtocol.OpTurnControl, JsonUtility.ToJson(payload));
        }

        public void RequestAttack(TacticalUnit attacker, TacticalUnit target, BodyPart part, bool cancelPenalty, SkillType atkSkill, SkillType defSkill, int bonusAP, int attackerPE = 0)
        {
            if (_room == null || !_room.InRoom || attacker == null || target == null) return;
            try { Killtime.Tactics.Visibility.FogOfWarManager.Instance?.LocalClaimedUnitIds.Add(UnitIdOf(attacker)); }
            catch { /* ignore */ }
            var req = new VTTActionRequestPayload
            {
                action = "attack",
                actorId = UnitIdOf(attacker),
                targetId = UnitIdOf(target),
                targetedPart = (int)part,
                cancelPenaltyWithAP = cancelPenalty,
                attackSkill = (int)atkSkill,
                defenseSkill = (int)defSkill,
                attackerBonusAP = bonusAP,
                attackerPE = attackerPE
            };
            _room.SendTableOp(VTTProtocol.OpActionRequest, JsonUtility.ToJson(req));
        }

        public void RequestGrenade(TacticalUnit attacker, HexCoordinates coords, string grenadeId, bool launcher, bool aim, int bonusAP)
        {
            if (_room == null || !_room.InRoom || attacker == null) return;
            var req = new VTTActionRequestPayload
            {
                action = "grenade",
                actorId = UnitIdOf(attacker),
                targetQ = coords.Q,
                targetR = coords.R,
                grenadeItemId = grenadeId ?? "",
                useLauncher = launcher,
                aimed = aim,
                attackerBonusAP = bonusAP
            };
            _room.SendTableOp(VTTProtocol.OpActionRequest, JsonUtility.ToJson(req));
        }

        public void RequestBreath(int apGain = 2)
        {
            if (_room == null || !_room.InRoom) return;
            var actor = _turnManager != null ? _turnManager.ActiveUnit : (_arena != null ? _arena.PlayerUnit : null);
            if (actor == null) return;
            var req = new VTTActionRequestPayload
            {
                action = "breath",
                actorId = UnitIdOf(actor),
                breathGainAP = apGain
            };
            _room.SendTableOp(VTTProtocol.OpActionRequest, JsonUtility.ToJson(req));
        }

        public void RequestEndTurn()
        {
            if (_room == null || !_room.InRoom) return;
            var actor = _turnManager != null ? _turnManager.ActiveUnit : (_arena != null ? _arena.PlayerUnit : null);
            var req = new VTTActionRequestPayload
            {
                action = "end_turn",
                actorId = actor != null ? UnitIdOf(actor) : ""
            };
            _room.SendTableOp(VTTProtocol.OpActionRequest, JsonUtility.ToJson(req));
        }

        public void RequestTargetSelection(TacticalUnit target)
        {
            if (_room == null || !_room.InRoom || target == null) return;

            string targetId = UnitIdOf(target);
            if (_lastRequestedTargetId == targetId) return;
            _lastRequestedTargetId = targetId;

            var req = new VTTActionRequestPayload
            {
                action = "select_target",
                targetId = targetId,
                targetQ = target.CurrentCoords.Q,
                targetR = target.CurrentCoords.R
            };
            _room.SendTableOp(VTTProtocol.OpActionRequest, JsonUtility.ToJson(req));
        }

        private void HandleActionRequestFromPlayer(VTTOpEnvelope op, string payloadJson)
        {
            VTTActionRequestPayload req;
            try { req = JsonUtility.FromJson<VTTActionRequestPayload>(payloadJson); }
            catch (Exception e)
            {
                Debug.LogWarning($"[VTT] action_request illisible : {e.Message}");
                return;
            }
            if (req == null || string.IsNullOrEmpty(req.action)) return;

            var actor = FindUnit(req.actorId);
            if (actor == null && _turnManager != null) actor = _turnManager.ActiveUnit;
            if (actor == null) return;

            switch (req.action)
            {
                case "attack":
                    var target = FindUnit(req.targetId);
                    if (target == null && _arena != null) target = _arena.CurrentTarget;
                    if (target != null && _arena != null)
                    {
                        _arena.SelectTarget(target);
                        _arena.ExecuteAttack(
                            (BodyPart)req.targetedPart,
                            req.cancelPenaltyWithAP,
                            (SkillType)req.attackSkill,
                            (SkillType)req.defenseSkill,
                            defenderWantsToDefend: true,
                            weaponBaseDamage: 5,
                            attackerBonusAP: req.attackerBonusAP,
                            attackerPE: req.attackerPE
                        );
                    }
                    break;

                case "grenade":
                    if (_arena != null)
                    {
                        _arena.ExecuteGrenadeThrow(
                            new HexCoordinates(req.targetQ, req.targetR),
                            req.grenadeItemId,
                            req.useLauncher,
                            req.aimed,
                            req.attackerBonusAP
                        );
                    }
                    break;

                case "breath":
                    if (actor.Stats != null && actor.Stats.TakeEmergencyBreath(req.breathGainAP))
                    {
                        var vis = actor.GetComponent<TacticalUnitVisual>();
                        vis?.SpawnFloatingText($"🫁 Souffle d'Urgence (+{req.breathGainAP} PA)", Color.yellow);
                        if (KilltimeAudioManager.Instance != null)
                            KilltimeAudioManager.Instance.PlayAt(SoundId.Breath_Emergency, actor.transform.position, 0.85f);

                        var actionPayload = new VTTCombatActionPayload
                        {
                            action = "breath",
                            actorId = UnitIdOf(actor),
                            breathGainAP = req.breathGainAP,
                            attackerNewAP = actor.Stats.CurrentActionPoints
                        };
                        BroadcastCombatAction(actionPayload);
                        BroadcastFullCombatState();
                    }
                    break;

                case "end_turn":
                    if (_turnManager != null)
                    {
                        _turnManager.EndCurrentTurn();
                    }
                    break;

                case "select_target":
                    var tgt = FindUnit(req.targetId);
                    if (tgt != null && _arena != null)
                    {
                        _arena.SelectTarget(tgt);
                    }
                    break;
            }
        }

        private string _lastBroadcastTargetId;
        private string _lastRequestedTargetId;

        public void BroadcastTargetSelection(TacticalUnit target)
        {
            if (_applyingRemote) return;
            if (_room == null || !_room.InRoom || !_room.IsGM || target == null) return;

            string targetId = UnitIdOf(target);
            if (_lastBroadcastTargetId == targetId) return;
            _lastBroadcastTargetId = targetId;

            var payload = new VTTTurnControlPayload
            {
                action = VTTProtocol.TurnActionTargetSync,
                round = _turnManager != null ? _turnManager.CurrentRound : 1,
                targetUnitId = targetId,
                targetQ = target.CurrentCoords.Q,
                targetR = target.CurrentCoords.R
            };
            _room.SendTableOp(VTTProtocol.OpTurnControl, JsonUtility.ToJson(payload));
        }

        public void BroadcastFullCombatState()
        {
            if (_room == null || !_room.InRoom || !_room.IsGM) return;
            if (_turnManager == null) _turnManager = FindAnyObjectByType<TurnManager>();
            if (_turnManager == null) return;

            var curTarget = _arena != null ? _arena.CurrentTarget : null;

            var payload = new VTTTurnControlPayload
            {
                action = VTTProtocol.TurnActionStateSync,
                round = _turnManager.CurrentRound,
                activeUnitId = _turnManager.ActiveUnit != null ? UnitIdOf(_turnManager.ActiveUnit) : "",
                targetUnitId = curTarget != null ? UnitIdOf(curTarget) : "",
                targetQ = curTarget != null ? curTarget.CurrentCoords.Q : 0,
                targetR = curTarget != null ? curTarget.CurrentCoords.R : 0,
                outcome = _turnManager.CurrentOutcome.ToString(),
                // Brouillard §25.4 : le MJ impose sa portée d'ambiance à la table.
                sightRange = Killtime.Tactics.Visibility.FogOfWarManager.Instance != null
                    ? Killtime.Tactics.Visibility.FogOfWarManager.Instance.CurrentSightRange
                    : Killtime.Tactics.Visibility.FogOfWarSystem.LongSightRange,
            };

            var allUnits = TacticalUnitRegistry.AllUnits;
            for (int i = 0; i < allUnits.Count; i++)
            {
                var u = allUnits[i];
                if (u == null || u.Stats == null) continue;
                var entry = new VTTUnitStateSyncEntry
                {
                    unitId = UnitIdOf(u),
                    q = u.CurrentCoords.Q,
                    r = u.CurrentCoords.R,
                    rotationY = u.transform.rotation.eulerAngles.y,
                    currentHealth = u.Stats.CurrentHealth,
                    maxHealth = u.Stats.MaxHealth,
                    currentAP = u.Stats.CurrentActionPoints,
                    maxAP = u.Stats.MaxActionPoints,
                    essoufflement = u.Stats.Essoufflement,
                    isAlive = u.Stats.IsAlive,
                    isDead = u.Stats.IsDead,
                    activeStatus = (int)u.Stats.ActiveStatus,
                    // Brouillard §25.4 : le hub filtre ce snapshot par
                    // destinataire (portée d'ambiance 360° + murs Full).
                    vision = Mathf.Clamp(u.Stats.Attributes.Vision, 1, 6),
                    ouie = Mathf.Clamp(u.Stats.Attributes.Ouie, 1, 6),
                    facingYaw = u.transform.rotation.eulerAngles.y,
                    pano = u.Stats.HasSpecialization(Killtime.Tactics.Visibility.FogOfWarSystem.SpecPanoramic) ? 1 : 0,
                    thermal = u.Stats.HasSpecialization(Killtime.Tactics.Visibility.FogOfWarSystem.SpecThermal) ? 1 : 0,
                    isPlayer = u.IsPlayerControlled ? 1 : 0,
                };
                if (u.Stats.ActiveStatus != StatusEffect.None)
                {
                    entry.statusEffects.Add(u.Stats.ActiveStatus.ToString());
                }
                payload.unitStates.Add(entry);
            }

            _room.SendTableOp(VTTProtocol.OpTurnControl, JsonUtility.ToJson(payload));
        }

        public void SendChat(string text)
        {
            if (_room != null) _room.SendChat(text);
        }

        public void BroadcastDice(int roll, string formula = "", string detail = "", string reason = "", bool isCritical = false, bool isFumble = false)
        {
            if (_room == null || !_room.InRoom) return;
            string opJson = VTTProtocol.BuildDiceOp(roll, formula, detail, reason, isCritical, isFumble);
            _room.Net?.Send(opJson);
        }

        public void BroadcastSceneControl(VTTSceneControlPayload payload)
        {
            if (_room == null || !_room.InRoom || !_room.IsGM || payload == null) return;
            _room.SendTableOp(VTTProtocol.OpSceneControl, JsonUtility.ToJson(payload));
        }
    }
}

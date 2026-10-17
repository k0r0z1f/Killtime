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
        // =========================================================================
        // REJEU DU CONTRÔLE DE TOUR (ROUND, INITIATIVE, TOURS)
        // =========================================================================

        private void ApplyRemoteTurnControl(VTTOpEnvelope op, string payloadJson)
        {
            if (_room != null && op.fromRole != VTTProtocol.RoleGM) return;

            _applyingRemote = true;
            try
            {
                VTTTurnControlPayload payload;
                try { payload = JsonUtility.FromJson<VTTTurnControlPayload>(payloadJson); }
                catch (Exception e)
                {
                    Debug.LogWarning($"[VTT] turn_control illisible : {e.Message}");
                    return;
                }
                if (payload == null || string.IsNullOrEmpty(payload.action)) return;

                if (_turnManager == null) _turnManager = FindAnyObjectByType<TurnManager>();
                if (_turnManager == null) return;

                switch (payload.action)
                {
                case VTTProtocol.TurnActionRoundStart:
                    _turnManager.ApplyRemoteRoundStart(payload.round, payload.turnOrder);
                    _hud?.AddCombatLog($"⚔️ <b>--- ROUND {payload.round} COMMENCÉ ---</b>");
                    break;
                case VTTProtocol.TurnActionTurnStart:
                    _turnManager.ApplyRemoteTurnStart(payload.activeUnitId);
                    var active = FindUnit(payload.activeUnitId);
                    if (active != null)
                    {
                        if (_cameraController == null) _cameraController = FindAnyObjectByType<TacticalCameraController>();
                        _cameraController?.FocusOn(active.transform);
                        _hud?.AddCombatLog($"--- Tour de <b>{active.Stats.Name}</b> ({active.Stats.CurrentActionPoints} PA) ---");
                    }

                    if (!string.IsNullOrEmpty(payload.targetUnitId))
                    {
                        var tgt = FindUnit(payload.targetUnitId);
                        if (tgt != null && _arena != null) _arena.SelectTarget(tgt);
                    }
                    else if (payload.targetQ != 0 || payload.targetR != 0)
                    {
                        var gVis = _arena != null ? _arena.GetComponent<HexGridVisualizer>() : FindAnyObjectByType<HexGridVisualizer>();
                        gVis?.SetTargetCoord(new HexCoordinates(payload.targetQ, payload.targetR));
                    }
                    break;
                case VTTProtocol.TurnActionTargetSync:
                    if (!string.IsNullOrEmpty(payload.targetUnitId))
                    {
                        var tgtSync = FindUnit(payload.targetUnitId);
                        if (tgtSync != null && _arena != null) _arena.SelectTarget(tgtSync);
                    }
                    else if (payload.targetQ != 0 || payload.targetR != 0)
                    {
                        var gVis = _arena != null ? _arena.GetComponent<HexGridVisualizer>() : FindAnyObjectByType<HexGridVisualizer>();
                        gVis?.SetTargetCoord(new HexCoordinates(payload.targetQ, payload.targetR));
                    }
                    break;
                case VTTProtocol.TurnActionArenaReset:
                    _applyingRemote = true;
                    try
                    {
                        if (_arena != null)
                        {
                            _arena.ClearAllUnits();
                            if (payload.targetQ >= 0)
                            {
                                _arena.ApplyArenaLayout((ArenaLayoutType)payload.targetQ);
                            }
                            _arena.ResetArena();
                        }
                        RebuildRegistry();
                        _hud?.AddCombatLog("🔄 L'Hôte (MJ) a réinitialisé le champ de bataille.");
                    }
                    finally
                    {
                        _applyingRemote = false;
                    }
                    break;
                case VTTProtocol.TurnActionEndTurn:
                    _turnManager.ApplyRemoteEndTurn(payload.activeUnitId);
                    break;
                case VTTProtocol.TurnActionCombatEnded:
                    _turnManager.ApplyRemoteCombatEnded(payload.outcome);
                    _hud?.AddCombatLog($"🏆 Combat terminé : {payload.outcome}");
                    break;
                case VTTProtocol.TurnActionPause:
                    CombatHUD.Instance?.SetPause(true);
                    _actionQueue.Clear();
                    _hud?.AddCombatLog("⏸ Le Maître du Jeu a mis le combat en pause.");
                    break;
                case VTTProtocol.TurnActionResume:
                    CombatHUD.Instance?.SetPause(false);
                    _hud?.AddCombatLog("▶ Le Maître du Jeu a repris le combat.");
                    break;
                case VTTProtocol.TurnActionStateSync:
                    _turnManager.ApplyRemoteStateSync(payload.round, payload.activeUnitId, payload.outcome);
                    ApplyRemoteStateSync(payload);
                    break;
                }

                try { OnRemoteTurnControlApplied?.Invoke(payload); }
                catch (Exception e) { Debug.LogException(e); }
            }
            finally
            {
                _applyingRemote = false;
            }
        }

        private void ApplyRemoteStateSync(VTTTurnControlPayload payload)
        {
            // Brouillard §25.4 : la portée d'ambiance du MJ fait foi pour toute la table.
            if (payload.sightRange > 0)
            {
                try { Killtime.Tactics.Visibility.FogOfWarManager.Instance?.ApplyGMSightRange(payload.sightRange); }
                catch { /* ignore */ }
            }
            if (payload.unitStates == null) return;
            var grid = _grid != null ? _grid : FindAnyObjectByType<TacticalHexGrid>();

            for (int i = 0; i < payload.unitStates.Count; i++)
            {
                var s = payload.unitStates[i];
                if (s == null) continue;
                var unit = FindUnit(s.unitId);
                if (unit == null) continue;

                if (unit.Stats != null)
                {
                    unit.Stats.CurrentHealth = s.currentHealth;
                    unit.Stats.CurrentActionPoints = s.currentAP;
                    unit.Stats.Essoufflement = s.essoufflement;
                    unit.Stats.IsDead = s.isDead || (!s.isAlive && s.currentHealth <= 0);

                    if (s.activeStatus != 0)
                    {
                        unit.Stats.ActiveStatus = (StatusEffect)s.activeStatus;
                    }
                    else if (s.statusEffects != null && s.statusEffects.Count > 0)
                    {
                        StatusEffect flags = StatusEffect.None;
                        for (int k = 0; k < s.statusEffects.Count; k++)
                        {
                            if (Enum.TryParse<StatusEffect>(s.statusEffects[k], out var parsed))
                            {
                                flags |= parsed;
                            }
                        }
                        unit.Stats.ActiveStatus = flags;
                    }
                    else
                    {
                        unit.Stats.ActiveStatus = StatusEffect.None;
                    }

                    if (unit.Stats.IsDead || !s.isAlive || unit.Stats.CurrentHealth <= 0)
                    {
                        unit.Stats.ActiveStatus |= StatusEffect.Inconscient | StatusEffect.ATerre;
                    }
                }

                unit.transform.rotation = Quaternion.Euler(0f, s.rotationY, 0f);

                var targetCoords = new HexCoordinates(s.q, s.r);
                if (grid != null && !unit.CurrentCoords.Equals(targetCoords))
                {
                    unit.TeleportTo(targetCoords, grid);
                }

                var vis = unit.GetComponent<TacticalUnitVisual>();
                if (unit.Stats != null)
                {
                    if (!s.isAlive || unit.Stats.CurrentHealth <= 0 || unit.Stats.IsDead || unit.Stats.ActiveStatus.HasFlag(StatusEffect.Inconscient))
                    {
                        unit.Stats.CurrentHealth = 0;
                        unit.Stats.IsDead = true;
                        vis?.TriggerFallingBackDeath();
                    }
                    vis?.RefreshEquippedWeaponVisual();
                }
            }
        }

        // =========================================================================
        // RÉGLAGES DE LA SALLE (IA ET PARAMÈTRES)
        // =========================================================================

        private void ApplyRemoteRoomSettings(VTTOpEnvelope op, string payloadJson)
        {
            if (_room != null && op.fromRole != VTTProtocol.RoleGM) return;
            try
            {
                var settings = JsonUtility.FromJson<VTTRoomSettingsPayload>(payloadJson);
                if (settings != null)
                {
                    if (_aiController == null) _aiController = FindAnyObjectByType<TacticalAIController>();
                    _aiController?.ApplyGMSettings(settings);
                    try { OnRemoteRoomSettingsApplied?.Invoke(settings); }
                    catch (Exception e) { Debug.LogException(e); }
                    Debug.Log($"[VTT] Paramètres d'IA et de table appliqués depuis le GM ({op.fromName}).");
                }
            }
            catch (Exception e) { Debug.LogException(e); }
        }

        private void ApplyRemoteSceneControl(VTTOpEnvelope op, string payloadJson)
        {
            if (_room != null && op.fromRole != VTTProtocol.RoleGM) return;
            try
            {
                var payload = JsonUtility.FromJson<VTTSceneControlPayload>(payloadJson);
                if (payload == null) return;
                try { OnRemoteSceneControlApplied?.Invoke(payload); }
                catch (Exception e) { Debug.LogException(e); }
                Debug.Log($"[VTT] Contrôle de scène reçu du GM ({op.fromName}) : {payload.action}");
            }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}

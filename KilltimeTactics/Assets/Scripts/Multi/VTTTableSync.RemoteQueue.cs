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
        // GESTIONNAIRE DE FILE D'ATTENTE D'ACTIONS SÉQUENTIELLES (FIFO)
        // =========================================================================

        private void EnqueueRemoteAction(IEnumerator actionRoutine)
        {
            if (actionRoutine == null) return;
            // Si la pause est active, ne pas empiler d'animations
            if (CombatHUD.IsPaused)
            {
                _actionQueue.Clear();
                return;
            }

            _actionQueue.Enqueue(actionRoutine);
            if (!_isProcessingQueue)
            {
                StartCoroutine(ProcessActionQueue());
            }
        }

        private IEnumerator ProcessActionQueue()
        {
            _isProcessingQueue = true;
            while (_actionQueue.Count > 0)
            {
                // Si la pause est enclenchée, vider la file immédiatement
                if (CombatHUD.IsPaused)
                {
                    _actionQueue.Clear();
                    break;
                }

                // Évite tout engorgement : si plus de 5 actions s'accumulent, on purge le retard
                if (_actionQueue.Count > 5)
                {
                    Debug.LogWarning($"[VTT] Engorgement évité ({_actionQueue.Count} actions en attente) : purge et rattrapage en direct.");
                    _actionQueue.Clear();
                    break;
                }

                var action = _actionQueue.Dequeue();
                if (action != null)
                {
                    yield return StartCoroutine(action);
                }
            }
            _isProcessingQueue = false;
        }

        // =========================================================================
        // REJEU DU MOUVEMENT DISTANT
        // =========================================================================

        private IEnumerator HandleRemoteMoveOpRoutine(VTTOpEnvelope op, string payloadJson)
        {
            _applyingRemote = true;
            try
            {
                VTTCombatActionPayload payload = null;
                try { payload = JsonUtility.FromJson<VTTCombatActionPayload>(payloadJson); } catch { /* ignore */ }

                string unitId = payload != null && !string.IsNullOrEmpty(payload.actorId)
                    ? payload.actorId
                    : ExtractString(payloadJson, "unitId");

                if (string.IsNullOrEmpty(unitId)) yield break;

                var unit = FindUnit(unitId);
                if (unit == null)
                {
                    Debug.LogWarning($"[VTT] unit_move ignorée : unité '{unitId}' introuvable.");
                    yield break;
                }

                int destQ = payload != null ? payload.destQ : ExtractInt(payloadJson, "destQ", ExtractInt(payloadJson, "q", unit.CurrentCoords.Q));
                int destR = payload != null ? payload.destR : ExtractInt(payloadJson, "destR", ExtractInt(payloadJson, "r", unit.CurrentCoords.R));

                if (payload != null && payload.path != null && payload.path.Count > 0)
                {
                    destQ = payload.path[payload.path.Count - 1].q;
                    destR = payload.path[payload.path.Count - 1].r;
                }

                var targetCoords = new HexCoordinates(destQ, destR);

                var grid = _grid != null ? _grid : FindAnyObjectByType<TacticalHexGrid>();
                if (grid == null) yield break;

                bool fastForward = _teleportRemoteMoves || _actionQueue.Count > 3 || CombatHUD.IsPaused;

                if (!fastForward && payload != null && payload.path != null && payload.path.Count > 1)
                {
                    var hexPath = new List<HexCoordinates>(payload.path.Count);
                    for (int i = 0; i < payload.path.Count; i++)
                    {
                        hexPath.Add(new HexCoordinates(payload.path[i].q, payload.path[i].r));
                    }

                    if (!unit.CurrentCoords.Equals(hexPath[0]))
                    {
                        unit.TeleportTo(hexPath[0], grid);
                    }

                    var vis = unit.GetComponent<TacticalUnitVisual>();
                    vis?.SpawnFloatingText($"Déplacement (-{payload.apCost} PA)", new Color(0.2f, 0.85f, 1.0f));

                    yield return StartCoroutine(unit.MoveAlongPath(hexPath, grid, payload.apCost));

                    if (!unit.CurrentCoords.Equals(targetCoords))
                    {
                        unit.TeleportTo(targetCoords, grid);
                    }
                }
                else
                {
                    unit.TeleportTo(targetCoords, grid);
                }

                try { OnRemoteMoveApplied?.Invoke(unit, op.fromName ?? op.from, op.fromRole ?? ""); }
                catch (Exception e) { Debug.LogException(e); }
            }
            finally
            {
                _applyingRemote = false;
            }
        }

        // =========================================================================
        // REJEU D'ACTION DE COMBAT (ATTAQUE CIBLÉE, GRENADE, SORT, SOUFFLE)
        // =========================================================================

        private IEnumerator HandleRemoteCombatActionRoutine(VTTOpEnvelope op, string payloadJson)
        {
            VTTCombatActionPayload payload;
            try { payload = JsonUtility.FromJson<VTTCombatActionPayload>(payloadJson); }
            catch (Exception e)
            {
                Debug.LogWarning($"[VTT] combat_action illisible : {e.Message}");
                yield break;
            }
            if (payload == null || string.IsNullOrEmpty(payload.action)) yield break;

            switch (payload.action)
            {
                case "attack":
                    yield return StartCoroutine(ExecuteRemoteAttackRoutine(payload));
                    break;
                case "move":
                    yield return StartCoroutine(HandleRemoteMoveOpRoutine(op, payloadJson));
                    break;
                case "grenade":
                    yield return StartCoroutine(ExecuteRemoteGrenadeRoutine(payload));
                    break;
                case "spell":
                    yield return StartCoroutine(ExecuteRemoteSpellRoutine(payload));
                    break;
                case "breath":
                    yield return StartCoroutine(ExecuteRemoteBreathRoutine(payload));
                    break;
            }

            try { OnRemoteCombatActionApplied?.Invoke(payload); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}

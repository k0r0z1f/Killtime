using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Core.Combat;

namespace Killtime.Multi
{
    /// <summary>
    /// Parité plateau VTT (RD-089) : objets au sol (DroppedWeaponPickup) via
    /// drop_weapon / pickup_weapon / kick_weapon + zones persistantes
    /// (SmokeZone) via state_sync. Le GM est l'autorité : il diffuse, les
    /// clients appliquent sans re-simuler (pose exacte, impulsion miroir).
    /// </summary>
    public partial class VTTTableSync : MonoBehaviour
    {
        public event Action<VTTDroppedWeaponPayload> OnRemoteGroundOpApplied;

        private void SubscribeGroundSync()
        {
            DroppedWeaponPickup.OnWeaponDropped += HandleLocalWeaponDropped;
            DroppedWeaponPickup.OnWeaponPickedUp += HandleLocalWeaponPickedUp;
            DroppedWeaponPickup.OnWeaponKicked += HandleLocalWeaponKicked;
            DroppedWeaponPickup.OnWeaponDestroyed += HandleLocalWeaponDestroyed;
        }

        private void UnsubscribeGroundSync()
        {
            DroppedWeaponPickup.OnWeaponDropped -= HandleLocalWeaponDropped;
            DroppedWeaponPickup.OnWeaponPickedUp -= HandleLocalWeaponPickedUp;
            DroppedWeaponPickup.OnWeaponKicked -= HandleLocalWeaponKicked;
            DroppedWeaponPickup.OnWeaponDestroyed -= HandleLocalWeaponDestroyed;
        }

        private bool CanBroadcastGround()
        {
            if (_applyingRemote) return false;
            return _room != null && _room.InRoom && _room.IsGM;
        }

        private void HandleLocalWeaponDropped(DroppedWeaponPickup pickup)
        {
            if (!CanBroadcastGround()) return;
            var payload = VTTGroundSyncCodec.ToPayload(pickup);
            if (payload == null || string.IsNullOrEmpty(payload.dropId)) return;
            _room.SendTableOp(VTTProtocol.OpDropWeapon, JsonUtility.ToJson(payload));
        }

        private void HandleLocalWeaponPickedUp(DroppedWeaponPickup pickup, TacticalUnit actor)
        {
            if (!CanBroadcastGround()) return;
            var payload = VTTGroundSyncCodec.ToPayload(pickup, actor != null ? UnitIdOf(actor) : "");
            if (payload == null || string.IsNullOrEmpty(payload.dropId)) return;
            _room.SendTableOp(VTTProtocol.OpPickupWeapon, JsonUtility.ToJson(payload));
        }

        private void HandleLocalWeaponKicked(DroppedWeaponPickup pickup, Vector3 kickerPos, float force)
        {
            if (!CanBroadcastGround()) return;
            var payload = VTTGroundSyncCodec.ToPayload(pickup);
            if (payload == null || string.IsNullOrEmpty(payload.dropId)) return;
            payload.kx = kickerPos.x;
            payload.ky = kickerPos.y;
            payload.kz = kickerPos.z;
            payload.force = force;
            _room.SendTableOp(VTTProtocol.OpKickWeapon, JsonUtility.ToJson(payload));
        }

        private void HandleLocalWeaponDestroyed(DroppedWeaponPickup pickup, TacticalUnit actor)
        {
            // Destruction = retrait pur (sans transfert d'inventaire) : réutilise
            // le canal pickup avec actorId vide, compris par l'application distante.
            if (!CanBroadcastGround()) return;
            if (pickup == null || string.IsNullOrEmpty(pickup.DropId)) return;
            var payload = new VTTDroppedWeaponPayload
            {
                dropId = pickup.DropId,
                formerOwner = pickup.FormerOwnerName ?? "",
                actorId = ""
            };
            _room.SendTableOp(VTTProtocol.OpPickupWeapon, JsonUtility.ToJson(payload));
        }

        private static DroppedWeaponPickup FindPickup(string dropId)
        {
            if (string.IsNullOrEmpty(dropId)) return null;
            var all = DroppedWeaponPickup.AllDropped;
            for (int i = 0; i < all.Count; i++)
            {
                var p = all[i];
                if (p != null && p.DropId == dropId) return p;
            }
            return null;
        }

        private IEnumerator HandleRemoteGroundOpRoutine(string op, string payloadJson)
        {
            _applyingRemote = true;
            DroppedWeaponPickup.SuppressEvents = true;
            try
            {
                VTTDroppedWeaponPayload payload;
                try { payload = JsonUtility.FromJson<VTTDroppedWeaponPayload>(payloadJson); }
                catch (Exception e)
                {
                    Debug.LogWarning($"[VTT] {op} illisible : {e.Message}");
                    yield break;
                }
                if (payload == null || string.IsNullOrEmpty(payload.dropId)) yield break;

                if (op == VTTProtocol.OpDropWeapon)
                {
                    if (FindPickup(payload.dropId) != null) yield break;
                    if (payload.item == null)
                    {
                        Debug.LogWarning($"[VTT] drop_weapon '{payload.dropId}' sans item : ignoré.");
                        yield break;
                    }
                    var spawned = DroppedWeaponPickup.SpawnAt(
                        payload.item, new Vector3(payload.x, payload.y, payload.z), payload.formerOwner ?? "");
                    if (spawned != null)
                    {
                        spawned.DropId = payload.dropId;
                        spawned.SnapToRest(new Vector3(payload.x, payload.y, payload.z));
                    }
                }
                else if (op == VTTProtocol.OpPickupWeapon)
                {
                    var existing = FindPickup(payload.dropId);
                    if (existing == null) yield break;
                    string label = existing.DroppedItem != null ? existing.DroppedItem.Name : payload.dropId;
                    try { Destroy(existing.gameObject); }
                    catch { /* ignore */ }
                    // actorId vide = destruction (retrait pur), sinon ramassage.
                    _hud?.AddCombatLog(string.IsNullOrEmpty(payload.actorId)
                        ? $"🗑 <b>{label}</b> retiré du plateau."
                        : $"⚔ <b>{label}</b> ramassée.");
                }
                else if (op == VTTProtocol.OpKickWeapon)
                {
                    var target = FindPickup(payload.dropId);
                    if (target == null) yield break;
                    target.ApplyKickImpulse(new Vector3(payload.kx, payload.ky, payload.kz),
                        payload.force > 0f ? payload.force : 4.5f);
                }

                try { OnRemoteGroundOpApplied?.Invoke(payload); }
                catch (Exception e) { Debug.LogException(e); }
            }
            finally
            {
                DroppedWeaponPickup.SuppressEvents = false;
                _applyingRemote = false;
            }
        }

        /// <summary>
        /// Réconciliation state_sync (RD-089) : remplace les zones persistantes
        /// par le snapshot GM + aligne les objets au sol (spawn manquants,
        /// purge ramassés). Les existants appariés restent en place (pas de pop).
        /// Ignorée si le GM n'émet pas le snapshot sol (groundSync &lt;= 0,
        /// build antérieur) : on ne touche à rien plutôt que d'effacer.
        /// </summary>
        private void ApplyRemoteGroundState(VTTTurnControlPayload payload)
        {
            if (payload == null || payload.groundSync <= 0) return;

            if (payload.smokeZones != null && _arena != null)
            {
                _arena.ApplyRemoteSmokeZones(VTTGroundSyncCodec.FromEntries(payload.smokeZones));
            }

            if (payload.droppedWeapons == null) return;
            DroppedWeaponPickup.SuppressEvents = true;
            try
            {
                var local = DroppedWeaponPickup.AllDropped;
                var localIds = new List<string>(local.Count);
                for (int i = 0; i < local.Count; i++)
                {
                    if (local[i] != null && !string.IsNullOrEmpty(local[i].DropId))
                        localIds.Add(local[i].DropId);
                }
                var stale = VTTGroundSyncCodec.FindStaleDropIds(localIds, payload.droppedWeapons);
                for (int i = 0; i < stale.Count; i++)
                {
                    var p = FindPickup(stale[i]);
                    if (p == null) continue;
                    try { Destroy(p.gameObject); }
                    catch { /* ignore */ }
                }

                var missing = VTTGroundSyncCodec.FindMissingDrops(localIds, payload.droppedWeapons);
                for (int i = 0; i < missing.Count; i++)
                {
                    var entry = missing[i];
                    var spawned = DroppedWeaponPickup.SpawnAt(
                        entry.item, new Vector3(entry.x, entry.y, entry.z), entry.formerOwner ?? "");
                    if (spawned != null)
                    {
                        spawned.DropId = entry.dropId;
                        spawned.SnapToRest(new Vector3(entry.x, entry.y, entry.z));
                    }
                }
            }
            finally
            {
                DroppedWeaponPickup.SuppressEvents = false;
            }
        }
    }
}

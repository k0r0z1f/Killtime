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
        private void HandleRoomJoined()
        {
            if (_room == null) return;
            if (_room.IsGM)
            {
                PublishCurrentMap();
                BroadcastRoomSettings();
                BroadcastFullCombatState();
            }
            else
            {
                _room.SendTableOp(VTTProtocol.OpMapRequest, "{}");
            }
        }

        private void HandleLocalMapRegistered(TacticalMapSaveData map)
        {
            if (map == null) return;
            ActiveMapName = map.MapName;
            if (!_applyingRemoteMap)
            {
                PublishMap(map);
                BroadcastRoomSettings();
                StartCoroutine(DeferredCombatStateBroadcast());
            }
        }

        private IEnumerator DeferredCombatStateBroadcast()
        {
            yield return null;
            yield return new WaitForSeconds(0.1f);
            BroadcastFullCombatState();
        }

        public void PublishCurrentMap()
        {
            if (_arena == null) _arena = FindAnyObjectByType<CombatDevArena>();
            if (_arena != null && _arena.CurrentLoadedMap != null)
            {
                PublishMap(_arena.CurrentLoadedMap);
            }
        }

        private void CaptureLiveUnitsIntoMap(TacticalMapSaveData map)
        {
            if (map == null) return;
            var liveUnits = TacticalUnitRegistry.AllUnits;
            if (liveUnits.Count == 0) return;

            map.PlacedUnits ??= new List<MapUnitData>();
            map.PlacedUnits.Clear();

            for (int i = 0; i < liveUnits.Count; i++)
            {
                var u = liveUnits[i];
                if (u == null || u.Stats == null) continue;
                var entry = new MapUnitData
                {
                    UnitId = u.gameObject.name,
                    Sheet = u.GetOrBuildSheet(),
                    Q = u.CurrentCoords.Q,
                    R = u.CurrentCoords.R,
                    IsPlayer = u.IsPlayerControlled,
                    currentHealth = u.Stats.CurrentHealth,
                    currentAP = u.Stats.CurrentActionPoints,
                    essoufflement = u.Stats.Essoufflement,
                    activeStatus = (int)u.Stats.ActiveStatus
                };
                if (u.Stats.ActiveStatus != StatusEffect.None)
                {
                    entry.statusEffects.Add(u.Stats.ActiveStatus.ToString());
                }
                map.PlacedUnits.Add(entry);
            }
        }

        private void PublishMap(TacticalMapSaveData map)
        {
            if (map == null || _room == null || !_room.InRoom || !_room.IsGM) return;

            CaptureLiveUnitsIntoMap(map);

            string mapJson = JsonUtility.ToJson(map);
            if (System.Text.Encoding.UTF8.GetByteCount(mapJson) > 480 * 1024)
            {
                Debug.LogWarning($"[VTT] Carte '{map.MapName}' non publiée : dépasse 480 Ko.");
                return;
            }
            ActiveMapName = map.MapName;
            _room.SendTableOp(VTTProtocol.OpMapLoad, "{\"map\":" + mapJson + "}");
        }

        private void ApplyRemoteMap(VTTOpEnvelope op, string payloadJson)
        {
            if (_room != null && op.fromRole != VTTProtocol.RoleGM)
            {
                Debug.LogWarning("[VTT] map_load ignoré : l'émetteur n'est pas GM.");
                return;
            }
            string mapJson = ExtractObject(payloadJson, "map");
            if (string.IsNullOrEmpty(mapJson)) return;

            TacticalMapSaveData map;
            try { map = JsonUtility.FromJson<TacticalMapSaveData>(mapJson); }
            catch (Exception e)
            {
                Debug.LogWarning($"[VTT] map_load illisible : {e.Message}");
                return;
            }
            if (map == null || string.IsNullOrEmpty(map.MapName)) return;

            try
            {
                _applyingRemoteMap = true;
                var editor = MapEditorDevWindow.Instance ?? FindAnyObjectByType<MapEditorDevWindow>();
                if (editor == null)
                {
                    var go = new GameObject("[Map] Remote Loader");
                    editor = go.AddComponent<MapEditorDevWindow>();
                    editor.CloseWindow();
                }
                editor.ApplyLoadedMap(map);
                ActiveMapName = map.MapName;
                RebuildRegistry();
                try { OnRemoteMapApplied?.Invoke(map.MapName, op.fromName ?? op.from); }
                catch (Exception e) { Debug.LogException(e); }
            }
            finally
            {
                _applyingRemoteMap = false;
            }
        }
    }
}

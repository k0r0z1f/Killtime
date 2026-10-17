using System.Collections.Generic;
using UnityEngine;
using Killtime.Core.Character;
using Killtime.Core.Combat;
using Killtime.Core.Inventory;
using Killtime.Tactics.Units;

namespace Killtime.Multi
{
    /// <summary>
    /// Conversions pures plateau <-> payloads VTT (RD-089) : zones persistantes
    /// (SmokeZone) et objets au sol (DroppedWeaponPickup). Aucune dépendance
    /// scène/réseau : testable en EditMode sans arène.
    /// </summary>
    public static class VTTGroundSyncCodec
    {
        public static VTTSmokeZoneEntry ToEntry(SmokeZone zone)
        {
            return new VTTSmokeZoneEntry
            {
                q = zone.Center.Q,
                r = zone.Center.R,
                radius = zone.Radius,
                kind = zone.Kind ?? "",
                statuses = (int)zone.Statuses,
                turnsLeft = zone.TurnsLeft,
                arcanotech = zone.Arcanotech
            };
        }

        public static SmokeZone FromEntry(VTTSmokeZoneEntry entry)
        {
            return new SmokeZone
            {
                Center = new Tactics.Grid.HexCoordinates(entry != null ? entry.q : 0, entry != null ? entry.r : 0),
                Radius = entry != null ? entry.radius : 0,
                Kind = entry != null && entry.kind != null ? entry.kind : "",
                Statuses = entry != null ? (StatusEffect)entry.statuses : StatusEffect.None,
                TurnsLeft = entry != null ? entry.turnsLeft : 0,
                Arcanotech = entry != null && entry.arcanotech
            };
        }

        public static List<VTTSmokeZoneEntry> ToEntries(IReadOnlyList<SmokeZone> zones)
        {
            var list = new List<VTTSmokeZoneEntry>(zones != null ? zones.Count : 0);
            if (zones == null) return list;
            for (int i = 0; i < zones.Count; i++)
            {
                var z = zones[i];
                if (z.TurnsLeft <= 0) continue;
                list.Add(ToEntry(z));
            }
            return list;
        }

        public static List<SmokeZone> FromEntries(List<VTTSmokeZoneEntry> entries)
        {
            var list = new List<SmokeZone>(entries != null ? entries.Count : 0);
            if (entries == null) return list;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i] == null) continue;
                var z = FromEntry(entries[i]);
                if (z.TurnsLeft <= 0) continue;
                list.Add(z);
            }
            return list;
        }

        public static VTTDroppedWeaponPayload ToPayload(DroppedWeaponPickup pickup, string actorId = "")
        {
            if (pickup == null) return null;
            Vector3 pos = pickup.transform.position;
            return new VTTDroppedWeaponPayload
            {
                dropId = pickup.DropId ?? "",
                formerOwner = pickup.FormerOwnerName ?? "",
                item = pickup.DroppedItem,
                x = pos.x,
                y = pos.y,
                z = pos.z,
                actorId = actorId ?? ""
            };
        }

        public static List<VTTDroppedWeaponPayload> ToPayloads(IReadOnlyList<DroppedWeaponPickup> pickups)
        {
            var list = new List<VTTDroppedWeaponPayload>(pickups != null ? pickups.Count : 0);
            if (pickups == null) return list;
            for (int i = 0; i < pickups.Count; i++)
            {
                var p = pickups[i];
                if (p == null || p.DroppedItem == null) continue;
                if (string.IsNullOrEmpty(p.DropId)) continue;
                list.Add(ToPayload(p));
            }
            return list;
        }

        /// <summary>
        /// DropIds locaux absents du snapshot GM : à détruire (déjà ramassés côté GM).
        /// </summary>
        public static List<string> FindStaleDropIds(
            IReadOnlyList<string> localIds, List<VTTDroppedWeaponPayload> remote)
        {
            var stale = new List<string>();
            if (localIds == null) return stale;
            for (int i = 0; i < localIds.Count; i++)
            {
                string id = localIds[i] ?? "";
                if (string.IsNullOrEmpty(id)) continue;
                bool found = false;
                if (remote != null)
                {
                    for (int k = 0; k < remote.Count; k++)
                    {
                        if (remote[k] != null && remote[k].dropId == id) { found = true; break; }
                    }
                }
                if (!found) stale.Add(id);
            }
            return stale;
        }

        /// <summary>
        /// Entrées GM sans équivalent local : à spawner (drop manqué / reconnexion).
        /// </summary>
        public static List<VTTDroppedWeaponPayload> FindMissingDrops(
            IReadOnlyList<string> localIds, List<VTTDroppedWeaponPayload> remote)
        {
            var missing = new List<VTTDroppedWeaponPayload>();
            if (remote == null) return missing;
            for (int i = 0; i < remote.Count; i++)
            {
                var entry = remote[i];
                if (entry == null || string.IsNullOrEmpty(entry.dropId) || entry.item == null) continue;
                bool found = false;
                if (localIds != null)
                {
                    for (int k = 0; k < localIds.Count; k++)
                    {
                        if (localIds[k] == entry.dropId) { found = true; break; }
                    }
                }
                if (!found) missing.Add(entry);
            }
            return missing;
        }
    }
}

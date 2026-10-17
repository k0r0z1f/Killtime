#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Killtime.Core.Character;
using Killtime.Core.Combat;
using Killtime.Core.Inventory;
using Killtime.Tactics.Grid;

namespace Killtime.Multi.Tests
{
    [TestFixture]
    public class VTTGroundSyncTests
    {
        [Test]
        public void SmokeZone_RoundtripsThroughEntry()
        {
            var zone = new SmokeZone
            {
                Center = new HexCoordinates(3, -2),
                Radius = 2,
                Kind = "Fumigène CM-6",
                Statuses = StatusEffect.Aveugle | StatusEffect.Asphyxie,
                TurnsLeft = 2,
                Arcanotech = false
            };
            var entry = VTTGroundSyncCodec.ToEntry(zone);
            Assert.AreEqual(3, entry.q);
            Assert.AreEqual(-2, entry.r);
            Assert.AreEqual(2, entry.radius);
            Assert.AreEqual("Fumigène CM-6", entry.kind);
            Assert.AreEqual((int)(StatusEffect.Aveugle | StatusEffect.Asphyxie), entry.statuses);
            Assert.AreEqual(2, entry.turnsLeft);
            Assert.IsFalse(entry.arcanotech);

            var back = VTTGroundSyncCodec.FromEntry(entry);
            Assert.AreEqual(zone.Center, back.Center);
            Assert.AreEqual(zone.Radius, back.Radius);
            Assert.AreEqual(zone.Kind, back.Kind);
            Assert.AreEqual(zone.Statuses, back.Statuses);
            Assert.AreEqual(zone.TurnsLeft, back.TurnsLeft);
            Assert.AreEqual(zone.Arcanotech, back.Arcanotech);
        }

        [Test]
        public void ToEntries_SkipsExpiredZones()
        {
            var zones = new List<SmokeZone>
            {
                new SmokeZone { Center = new HexCoordinates(0, 0), Radius = 1, Kind = "Feu", TurnsLeft = 2 },
                new SmokeZone { Center = new HexCoordinates(1, 0), Radius = 1, Kind = "Feu", TurnsLeft = 0 },
            };
            var entries = VTTGroundSyncCodec.ToEntries(zones);
            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual(0, entries[0].q);

            var back = VTTGroundSyncCodec.FromEntries(new List<VTTSmokeZoneEntry>
            {
                new VTTSmokeZoneEntry { q = 1, r = 1, radius = 1, kind = "Gaz", turnsLeft = 3 },
                new VTTSmokeZoneEntry { q = 2, r = 2, radius = 1, kind = "Gaz", turnsLeft = 0 },
                null
            });
            Assert.AreEqual(1, back.Count);
            Assert.AreEqual(1, back[0].Center.Q);
        }

        [Test]
        public void DroppedWeaponPayload_SerializesNestedItem()
        {
            var payload = new VTTDroppedWeaponPayload
            {
                dropId = "abc123",
                formerOwner = "Roger",
                item = new InventoryItem { ItemId = "item-1", Name = "Fusil Laser", Type = ItemType.Weapon, BaseDamage = 8 },
                x = 1.5f,
                y = 0.2f,
                z = -3.25f,
                actorId = "Mina"
            };
            string json = JsonUtility.ToJson(payload);
            var back = JsonUtility.FromJson<VTTDroppedWeaponPayload>(json);
            Assert.IsNotNull(back);
            Assert.AreEqual("abc123", back.dropId);
            Assert.AreEqual("Roger", back.formerOwner);
            Assert.IsNotNull(back.item);
            Assert.AreEqual("item-1", back.item.ItemId);
            Assert.AreEqual("Fusil Laser", back.item.Name);
            Assert.AreEqual(ItemType.Weapon, back.item.Type);
            Assert.AreEqual(8, back.item.BaseDamage);
            Assert.AreEqual(1.5f, back.x, 0.001f);
            Assert.AreEqual(-3.25f, back.z, 0.001f);
            Assert.AreEqual("Mina", back.actorId);
        }

        [Test]
        public void StateSync_CarriesGroundSnapshot()
        {
            var sync = new VTTTurnControlPayload
            {
                action = "state_sync",
                round = 2,
                smokeZones = new List<VTTSmokeZoneEntry>
                {
                    new VTTSmokeZoneEntry { q = 0, r = 0, radius = 2, kind = "Fumigène", turnsLeft = 2 }
                },
                droppedWeapons = new List<VTTDroppedWeaponPayload>
                {
                    new VTTDroppedWeaponPayload
                    {
                        dropId = "d1",
                        item = new InventoryItem { ItemId = "i1", Name = "Gourdin" },
                        x = 1f, y = 0f, z = 2f
                    }
                }
            };
            string json = JsonUtility.ToJson(sync);
            var back = JsonUtility.FromJson<VTTTurnControlPayload>(json);
            Assert.IsNotNull(back);
            Assert.AreEqual(1, back.smokeZones.Count);
            Assert.AreEqual("Fumigène", back.smokeZones[0].kind);
            Assert.AreEqual(2, back.smokeZones[0].turnsLeft);
            Assert.AreEqual(1, back.droppedWeapons.Count);
            Assert.AreEqual("d1", back.droppedWeapons[0].dropId);
            Assert.AreEqual("Gourdin", back.droppedWeapons[0].item.Name);
        }

        [Test]
        public void Reconcile_FindsStaleAndMissing()
        {
            var localIds = new List<string> { "keep", "stale" };
            var remote = new List<VTTDroppedWeaponPayload>
            {
                new VTTDroppedWeaponPayload { dropId = "keep", item = new InventoryItem { Name = "A" } },
                new VTTDroppedWeaponPayload { dropId = "fresh", item = new InventoryItem { Name = "B" } },
                new VTTDroppedWeaponPayload { dropId = "noitem" },
                null
            };
            var stale = VTTGroundSyncCodec.FindStaleDropIds(localIds, remote);
            Assert.AreEqual(1, stale.Count);
            Assert.AreEqual("stale", stale[0]);

            var missing = VTTGroundSyncCodec.FindMissingDrops(localIds, remote);
            Assert.AreEqual(1, missing.Count);
            Assert.AreEqual("fresh", missing[0].dropId);
        }

        [Test]
        public void GroundBuilders_ProduceGMOnlyOps()
        {
            var payload = new VTTDroppedWeaponPayload { dropId = "d1", actorId = "Mina" };
            string drop = VTTProtocol.BuildDropWeaponOp(payload);
            Assert.AreEqual("op", VTTProtocol.PeekType(drop));
            StringAssert.Contains("\"op\":\"drop_weapon\"", drop);
            StringAssert.Contains("\"dropId\":\"d1\"", drop);

            string pickup = VTTProtocol.BuildPickupWeaponOp(payload);
            StringAssert.Contains("\"op\":\"pickup_weapon\"", pickup);

            var kick = new VTTDroppedWeaponPayload { dropId = "d1", kx = 1f, force = 4.5f };
            string kickOp = VTTProtocol.BuildKickWeaponOp(kick);
            StringAssert.Contains("\"op\":\"kick_weapon\"", kickOp);
            StringAssert.Contains("\"force\":4.5", kickOp);

            Assert.AreEqual("drop_weapon", VTTProtocol.OpDropWeapon);
            Assert.AreEqual("pickup_weapon", VTTProtocol.OpPickupWeapon);
            Assert.AreEqual("kick_weapon", VTTProtocol.OpKickWeapon);
            Assert.IsTrue(VTTProtocol.IsGMOp(VTTProtocol.OpDropWeapon));
            Assert.IsTrue(VTTProtocol.IsGMOp(VTTProtocol.OpPickupWeapon));
            Assert.IsTrue(VTTProtocol.IsGMOp(VTTProtocol.OpKickWeapon));
        }

        [Test]
        public void StateSync_GroundMarkerDistinguishesLegacyGM()
        {
            // Ancien build : groundSync absent = 0 -> le client ignore la passe sol.
            var legacy = JsonUtility.FromJson<VTTTurnControlPayload>("{\"action\":\"state_sync\",\"round\":1}");
            Assert.AreEqual(0, legacy.groundSync);

            var fresh = new VTTTurnControlPayload { action = "state_sync", groundSync = 1 };
            string json = JsonUtility.ToJson(fresh);
            StringAssert.Contains("\"groundSync\":1", json);
        }

        [Test]
        public void DestroyPickup_UsesPickupOpWithEmptyActor()
        {
            // Destruction = retrait pur : même op pickup, actorId vide.
            var destroy = new VTTDroppedWeaponPayload { dropId = "d9", actorId = "" };
            string op = VTTProtocol.BuildPickupWeaponOp(destroy);
            StringAssert.Contains("\"op\":\"pickup_weapon\"", op);
            StringAssert.Contains("\"dropId\":\"d9\"", op);
            StringAssert.Contains("\"actorId\":\"\"", op);
        }

        [Test]
        public void ActionRequest_CarriesDropIdForGroundActions()
        {
            foreach (string action in new[] { "pickup", "kick", "destroy_pickup" })
            {
                var req = new VTTActionRequestPayload
                {
                    action = action,
                    actorId = "Mina",
                    dropId = "d7"
                };
                string op = VTTProtocol.BuildActionRequestOp(req);
                Assert.AreEqual("op", VTTProtocol.PeekType(op));
                StringAssert.Contains("\"op\":\"action_request\"", op);
                StringAssert.Contains($"\"action\":\"{action}\"", op);
                StringAssert.Contains("\"dropId\":\"d7\"", op);

                var back = JsonUtility.FromJson<VTTActionRequestPayload>(
                    VTTProtocol.ExtractObject(op, "payload"));
                Assert.AreEqual(action, back.action);
                Assert.AreEqual("Mina", back.actorId);
                Assert.AreEqual("d7", back.dropId);
            }
        }
    }
}
#endif

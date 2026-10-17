#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Killtime.Multi.Tests
{
    /// <summary>
    /// Brouillard asymétrique VTT : revendication d'avatars (unit_claim) et
    /// champs de vision véhiculés (vision / facingYaw / pano / thermal) dans
    /// les paquets unit_move et state_sync (filtrage serveur anti map-hack).
    /// </summary>
    [TestFixture]
    public class VTTVisibilityFilterTests
    {
        [Test]
        public void UnitClaimOp_SerializesUnitIds()
        {
            string op = VTTProtocol.BuildUnitClaimOp(new List<string> { "Roger", "Mina_1" });
            Assert.AreEqual("op", VTTProtocol.PeekType(op));
            StringAssert.Contains("\"op\":\"unit_claim\"", op);
            StringAssert.Contains("Roger", op);
            StringAssert.Contains("Mina_1", op);

            string payloadJson = VTTProtocol.ExtractObject(op, "payload");
            var payload = JsonUtility.FromJson<VTTUnitClaimPayload>(payloadJson);
            Assert.IsNotNull(payload);
            Assert.AreEqual(2, payload.unitIds.Count);
            Assert.AreEqual("Roger", payload.unitIds[0]);
            Assert.AreEqual("Mina_1", payload.unitIds[1]);
        }

        [Test]
        public void UnitClaimOp_EmptyList_IsValid()
        {
            string op = VTTProtocol.BuildUnitClaimOp(null);
            Assert.AreEqual("op", VTTProtocol.PeekType(op));
            string payloadJson = VTTProtocol.ExtractObject(op, "payload");
            var payload = JsonUtility.FromJson<VTTUnitClaimPayload>(payloadJson);
            Assert.IsNotNull(payload);
            Assert.IsNotNull(payload.unitIds);
            Assert.AreEqual(0, payload.unitIds.Count);
        }

        [Test]
        public void MovePayload_CarriesVisionAndFacing()
        {
            var payload = new VTTCombatActionPayload
            {
                action = "move",
                actorId = "Roger",
                destQ = 3,
                destR = -1,
                apCost = 2,
                path = new List<VTTCoord> { new VTTCoord(1, 0), new VTTCoord(3, -1) },
                facingYaw = 90f,
                vision = 4,
                pano = 0,
                thermal = 1,
            };
            string json = JsonUtility.ToJson(payload);
            var back = JsonUtility.FromJson<VTTCombatActionPayload>(json);
            Assert.AreEqual(90f, back.facingYaw, 1e-4f);
            Assert.AreEqual(4, back.vision);
            Assert.AreEqual(0, back.pano);
            Assert.AreEqual(1, back.thermal);
            Assert.AreEqual(3, back.destQ);
        }

        [Test]
        public void StateSyncEntry_CarriesVisionFields()
        {
            var payload = new VTTTurnControlPayload
            {
                action = "state_sync",
                round = 2,
                sightRange = 12,
                unitStates = new List<VTTUnitStateSyncEntry>
                {
                    new VTTUnitStateSyncEntry
                    {
                        unitId = "Roger",
                        q = 2,
                        r = -1,
                        vision = 5,
                        ouie = 3,
                        facingYaw = 45f,
                        pano = 1,
                        thermal = 0,
                        isPlayer = 1,
                        currentHealth = 20,
                        maxHealth = 24,
                        currentAP = 6,
                        maxAP = 8,
                        isAlive = true,
                    }
                }
            };
            string json = JsonUtility.ToJson(payload);
            var back = JsonUtility.FromJson<VTTTurnControlPayload>(json);
            Assert.AreEqual(1, back.unitStates.Count);
            Assert.AreEqual(5, back.unitStates[0].vision);
            Assert.AreEqual(3, back.unitStates[0].ouie);
            Assert.AreEqual(45f, back.unitStates[0].facingYaw, 1e-4f);
            Assert.AreEqual(1, back.unitStates[0].pano);
            Assert.AreEqual(0, back.unitStates[0].thermal);
            Assert.AreEqual(1, back.unitStates[0].isPlayer);
            Assert.AreEqual(12, back.sightRange);
        }

        [Test]
        public void UnitClaim_IsNotAGMOp()
        {
            // Les joueurs déclarent librement leurs avatars ; le hub consomme.
            Assert.IsFalse(VTTProtocol.IsGMOp(VTTProtocol.OpUnitClaim));
        }
    }
}
#endif

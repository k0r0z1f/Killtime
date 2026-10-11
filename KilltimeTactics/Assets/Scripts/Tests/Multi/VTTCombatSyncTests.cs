#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Killtime.Multi.Tests
{
    [TestFixture]
    public class VTTCombatSyncTests
    {
        [Test]
        public void CombatActionPayload_Attack_SerializesAndDeserializesCorrectly()
        {
            var payload = new VTTCombatActionPayload
            {
                action = "attack",
                actorId = "Roger",
                targetId = "Dummy_1",
                targetedPart = 2, // Torse
                actualHitPart = 2,
                attackSkill = 5,
                defenseSkill = 6,
                isHit = true,
                isCritical = true,
                wasDeflected = false,
                finalDamageApplied = 14,
                rawDamage = 16,
                armorAbsorbed = 2,
                differential = 4,
                inflictedStatus = "Choc Traumatique",
                isMeleeStrike = false,
                attackerCostAP = 3,
                defenderCostAP = 1,
                attackerNewAP = 5,
                defenderNewAP = 2,
                defenderNewHealth = 18,
                combatLog = "Roger tire sur le Torse et inflige 14 dégâts (Critique !)"
            };

            string json = JsonUtility.ToJson(payload);
            Assert.IsNotNull(json);
            StringAssert.Contains("\"action\":\"attack\"", json);
            StringAssert.Contains("\"actorId\":\"Roger\"", json);
            StringAssert.Contains("\"targetId\":\"Dummy_1\"", json);
            StringAssert.Contains("\"finalDamageApplied\":14", json);

            var deserialized = JsonUtility.FromJson<VTTCombatActionPayload>(json);
            Assert.IsNotNull(deserialized);
            Assert.AreEqual("attack", deserialized.action);
            Assert.AreEqual("Roger", deserialized.actorId);
            Assert.AreEqual("Dummy_1", deserialized.targetId);
            Assert.AreEqual(2, deserialized.targetedPart);
            Assert.IsTrue(deserialized.isHit);
            Assert.IsTrue(deserialized.isCritical);
            Assert.IsFalse(deserialized.wasDeflected);
            Assert.AreEqual(14, deserialized.finalDamageApplied);
            Assert.AreEqual("Choc Traumatique", deserialized.inflictedStatus);
            Assert.AreEqual(18, deserialized.defenderNewHealth);
            Assert.AreEqual("Roger tire sur le Torse et inflige 14 dégâts (Critique !)", deserialized.combatLog);
        }

        [Test]
        public void CombatActionPayload_MoveWithPath_SerializesAndDeserializes()
        {
            var payload = new VTTCombatActionPayload
            {
                action = "move",
                actorId = "Roger",
                destQ = 3,
                destR = -1,
                apCost = 2,
                path = new List<VTTCoord>
                {
                    new VTTCoord(1, 0),
                    new VTTCoord(2, 0),
                    new VTTCoord(3, -1)
                }
            };

            string json = JsonUtility.ToJson(payload);
            var deserialized = JsonUtility.FromJson<VTTCombatActionPayload>(json);

            Assert.IsNotNull(deserialized);
            Assert.AreEqual("move", deserialized.action);
            Assert.AreEqual("Roger", deserialized.actorId);
            Assert.AreEqual(3, deserialized.destQ);
            Assert.AreEqual(-1, deserialized.destR);
            Assert.AreEqual(2, deserialized.apCost);
            Assert.AreEqual(3, deserialized.path.Count);
            Assert.AreEqual(1, deserialized.path[0].q);
            Assert.AreEqual(0, deserialized.path[0].r);
            Assert.AreEqual(3, deserialized.path[2].q);
            Assert.AreEqual(-1, deserialized.path[2].r);
        }

        [Test]
        public void TurnControlPayload_RoundStartWithInitiative_SerializesAndDeserializes()
        {
            var payload = new VTTTurnControlPayload
            {
                action = "round_start",
                round = 2,
                activeUnitId = "Roger",
                turnOrder = new List<VTTInitiativeEntry>
                {
                    new VTTInitiativeEntry
                    {
                        unitId = "Roger",
                        die = "TwoD6",
                        rawRoll = 10,
                        total = 10,
                        rapidite = 4,
                        agilite = 3,
                        intelligence = 3
                    },
                    new VTTInitiativeEntry
                    {
                        unitId = "Dummy_1",
                        die = "D8",
                        rawRoll = 6,
                        total = 6,
                        rapidite = 2,
                        agilite = 2,
                        intelligence = 2
                    }
                }
            };

            string json = JsonUtility.ToJson(payload);
            var deserialized = JsonUtility.FromJson<VTTTurnControlPayload>(json);

            Assert.IsNotNull(deserialized);
            Assert.AreEqual("round_start", deserialized.action);
            Assert.AreEqual(2, deserialized.round);
            Assert.AreEqual("Roger", deserialized.activeUnitId);
            Assert.AreEqual(2, deserialized.turnOrder.Count);
            Assert.AreEqual("Roger", deserialized.turnOrder[0].unitId);
            Assert.AreEqual(10, deserialized.turnOrder[0].total);
            Assert.AreEqual("Dummy_1", deserialized.turnOrder[1].unitId);
            Assert.AreEqual(6, deserialized.turnOrder[1].total);
        }

        [Test]
        public void TurnControlPayload_StateSync_SerializesAndDeserializes()
        {
            var payload = new VTTTurnControlPayload
            {
                action = "state_sync",
                round = 3,
                activeUnitId = "Roger",
                unitStates = new List<VTTUnitStateSyncEntry>
                {
                    new VTTUnitStateSyncEntry
                    {
                        unitId = "Roger",
                        q = 2,
                        r = -1,
                        currentHealth = 24,
                        maxHealth = 28,
                        currentAP = 6,
                        maxAP = 8,
                        essoufflement = 1,
                        isAlive = true,
                        statusEffects = new List<string> { "Destabilise" }
                    }
                }
            };

            string json = JsonUtility.ToJson(payload);
            var deserialized = JsonUtility.FromJson<VTTTurnControlPayload>(json);

            Assert.IsNotNull(deserialized);
            Assert.AreEqual("state_sync", deserialized.action);
            Assert.AreEqual(1, deserialized.unitStates.Count);
            Assert.AreEqual("Roger", deserialized.unitStates[0].unitId);
            Assert.AreEqual(24, deserialized.unitStates[0].currentHealth);
            Assert.AreEqual(6, deserialized.unitStates[0].currentAP);
            Assert.AreEqual(1, deserialized.unitStates[0].statusEffects.Count);
            Assert.AreEqual("Destabilise", deserialized.unitStates[0].statusEffects[0]);
        }

        [Test]
        public void RoomSettingsPayload_SerializesAndDeserializes()
        {
            var payload = new VTTRoomSettingsPayload
            {
                isAIEnabled = true,
                aiMode = 1, // FullAuto
                aiPersonality = 2, // Tactician
                aiActionDelay = 0.45f,
                aiRetreatRatio = 0.20f,
                aiDefensiveReserve = 1,
                activeMapName = "Bunker_Alpha"
            };

            string json = JsonUtility.ToJson(payload);
            var deserialized = JsonUtility.FromJson<VTTRoomSettingsPayload>(json);

            Assert.IsNotNull(deserialized);
            Assert.IsTrue(deserialized.isAIEnabled);
            Assert.AreEqual(1, deserialized.aiMode);
            Assert.AreEqual(2, deserialized.aiPersonality);
            Assert.AreEqual(0.45f, deserialized.aiActionDelay, 0.001f);
            Assert.AreEqual(0.20f, deserialized.aiRetreatRatio, 0.001f);
            Assert.AreEqual(1, deserialized.aiDefensiveReserve);
            Assert.AreEqual("Bunker_Alpha", deserialized.activeMapName);
        }

        [Test]
        public void Protocol_BuildBuilders_EmbedValidOpStructures()
        {
            var combatAction = new VTTCombatActionPayload { action = "attack", actorId = "A", targetId = "B" };
            string combatOp = VTTProtocol.BuildCombatActionOp(combatAction);
            Assert.AreEqual("op", VTTProtocol.PeekType(combatOp));
            StringAssert.Contains("\"op\":\"combat_action\"", combatOp);
            StringAssert.Contains("\"actorId\":\"A\"", combatOp);

            var turnControl = new VTTTurnControlPayload { action = "round_start", round = 2 };
            string turnOp = VTTProtocol.BuildTurnControlOp(turnControl);
            Assert.AreEqual("op", VTTProtocol.PeekType(turnOp));
            StringAssert.Contains("\"op\":\"turn_control\"", turnOp);
            StringAssert.Contains("\"round\":2", turnOp);

            var roomSettings = new VTTRoomSettingsPayload { isAIEnabled = false, aiMode = 0 };
            string settingsOp = VTTProtocol.BuildRoomSettingsOp(roomSettings);
            Assert.AreEqual("op", VTTProtocol.PeekType(settingsOp));
            StringAssert.Contains("\"op\":\"room_settings\"", settingsOp);
            StringAssert.Contains("\"isAIEnabled\":false", settingsOp);
        }

        [Test]
        public void TurnControlPayload_PauseAndResume_SerializeAndDeserialize()
        {
            var pausePayload = new VTTTurnControlPayload { action = VTTProtocol.TurnActionPause };
            string pauseJson = JsonUtility.ToJson(pausePayload);
            var desPause = JsonUtility.FromJson<VTTTurnControlPayload>(pauseJson);
            Assert.IsNotNull(desPause);
            Assert.AreEqual(VTTProtocol.TurnActionPause, desPause.action);

            var resumePayload = new VTTTurnControlPayload { action = VTTProtocol.TurnActionResume };
            string resumeJson = JsonUtility.ToJson(resumePayload);
            var desResume = JsonUtility.FromJson<VTTTurnControlPayload>(resumeJson);
            Assert.IsNotNull(desResume);
            Assert.AreEqual(VTTProtocol.TurnActionResume, desResume.action);

            string pauseOp = VTTProtocol.BuildTurnControlOp(pausePayload);
            Assert.AreEqual("op", VTTProtocol.PeekType(pauseOp));
            StringAssert.Contains("\"action\":\"pause\"", pauseOp);
        }
    }
}
#endif

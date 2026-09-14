#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using NUnit.Framework;

namespace Killtime.Multi.Tests
{
    [TestFixture]
    public class VTTProtocolTests
    {
        [Test]
        public void DefaultHubUrl_IsPublicWssEndpoint()
        {
            StringAssert.StartsWith("wss://", VTTProtocol.DefaultHubUrl);
            StringAssert.EndsWith("/ws/vtt", VTTProtocol.DefaultHubUrl);
        }

        [Test]
        public void PeekType_ExtractsTypeField()
        {
            Assert.AreEqual("hello", VTTProtocol.PeekType("{\"type\":\"hello\",\"username\":\"A\"}"));
            Assert.AreEqual("op", VTTProtocol.PeekType("{\"type\":\"op\",\"op\":\"chat\",\"payload\":{}}"));
            Assert.AreEqual("", VTTProtocol.PeekType("{}"));
            Assert.AreEqual("", VTTProtocol.PeekType(""));
        }

        [Test]
        public void BuildJoinRoom_NormalizesCode()
        {
            string json = VTTProtocol.BuildJoinRoom("  ab12cd ", "Mina");
            StringAssert.Contains("\"code\":\"AB12CD\"", json);
            StringAssert.Contains("\"username\":\"Mina\"", json);
            Assert.AreEqual("AB12CD", VTTProtocol.NormalizeCode("  ab12cd "));
        }

        [Test]
        public void BuildOp_EmbedsPayloadVerbatim()
        {
            string json = VTTProtocol.BuildUnitMoveOp("Roger", 3, -1);
            StringAssert.Contains("\"type\":\"op\"", json);
            StringAssert.Contains("\"op\":\"unit_move\"", json);
            StringAssert.Contains("\"unitId\":\"Roger\"", json);
            StringAssert.Contains("\"q\":3", json);
            StringAssert.Contains("\"r\":-1", json);
        }

        [Test]
        public void BuildSetRole_AndKick_ContainTarget()
        {
            StringAssert.Contains("\"targetId\":\"c_abc\"", VTTProtocol.BuildSetRole("c_abc", "gm"));
            StringAssert.Contains("\"role\":\"gm\"", VTTProtocol.BuildSetRole("c_abc", "gm"));
            StringAssert.Contains("kicked_by_gm", VTTProtocol.BuildKick("c_xyz"));
        }

        [Test]
        public void BuildCreateRoom_WithMetadata_ContainsAllFields()
        {
            string json = VTTProtocol.BuildCreateRoom("Alex", null, "Table du Jeudi", "public", 6);
            StringAssert.Contains("\"tableName\":\"Table du Jeudi\"", json);
            StringAssert.Contains("\"visibility\":\"public\"", json);
            StringAssert.Contains("\"maxPlayers\":6", json);
        }

        [Test]
        public void BuildCreateRoom_LegacyCall_OmitsOptionalFields()
        {
            string json = VTTProtocol.BuildCreateRoom("Alex");
            Assert.IsFalse(json.Contains("tableName"));
            Assert.IsFalse(json.Contains("visibility"));
            Assert.IsFalse(json.Contains("maxPlayers"));
        }

        [Test]
        public void BuildSetVisibility_OnlyAcceptsKnownValues()
        {
            StringAssert.Contains("\"visibility\":\"private\"", VTTProtocol.BuildSetVisibility("private"));
            StringAssert.Contains("\"visibility\":\"public\"", VTTProtocol.BuildSetVisibility("public"));
            // Valeur inconnue -> repli public (même règle que le hub).
            StringAssert.Contains("\"visibility\":\"public\"", VTTProtocol.BuildSetVisibility("xyz"));
        }

        [Test]
        public void BuildDirectoryUrl_ConvertsWsToHttpAndStripsPath()
        {
            Assert.AreEqual(
                "http://localhost:3000/api/vtt/rooms?build=0.3.1",
                VTTRoomDirectory.BuildDirectoryUrl("ws://localhost:3000/ws/vtt", "0.3.1"));
            Assert.AreEqual(
                "https://mondomaine.fr/api/vtt/rooms?build=0.3.1",
                VTTRoomDirectory.BuildDirectoryUrl("wss://mondomaine.fr/ws/vtt", "0.3.1"));
            Assert.AreEqual(
                "http://localhost:3000/api/vtt/rooms",
                VTTRoomDirectory.BuildDirectoryUrl("ws://localhost:3000/ws/vtt", null));
        }

        [Test]
        public void DirectoryResponse_ParsesRoomList()
        {
            string json = "{\"rooms\":[{\"code\":\"ABC123\",\"tableName\":\"T\",\"buildVersion\":\"0.3.1\"," +
                "\"gmName\":\"Alex\",\"players\":2,\"maxPlayers\":6,\"createdAt\":1,\"lastActive\":2}],\"count\":1,\"build\":\"0.3.1\"}";
            var resp = UnityEngine.JsonUtility.FromJson<VTTRoomDirectoryResponse>(json);
            Assert.IsNotNull(resp);
            Assert.AreEqual(1, resp.rooms.Count);
            Assert.AreEqual("ABC123", resp.rooms[0].code);
            Assert.AreEqual(2, resp.rooms[0].players);
        }

        [Test]
        public void Escape_NeutralizesQuotes()
        {
            string json = VTTProtocol.BuildChatOp("il dit \"go\" \\ fin");
            // Le JSON doit rester un objet valide avec le champ op présent.
            Assert.AreEqual("op", VTTProtocol.PeekType(json));
            StringAssert.Contains("\\\"go\\\"", json);
        }
    }
}
#endif

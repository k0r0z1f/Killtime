#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using Killtime.Multi;
using Killtime.Story;
using UnityEngine;

namespace Killtime.Tests
{
    [TestFixture]
    public class WorldMapTests
    {
        private static HybrisWorldMapSaveData MiniMap()
        {
            var doc = new HybrisWorldMapSaveData { MapName = "test_mini" };
            doc.Nodes.Add(new HybrisSectorNode("kingston", "Kingston", "Centre", "", 0.3f, 0.5f, HybrisBiome.CiteHumaine, 1));
            doc.Nodes.Add(new HybrisSectorNode("haliriel", "Haliriel", "Sud", "", 0.3f, 0.7f, HybrisBiome.CiteElfique, 1));
            doc.Links.Add(new HybrisSectorLink("kingston", "haliriel", 80, 4, "Route"));
            return doc;
        }

        [Test]
        public void CanonMap_ValidatesWithoutErrors()
        {
            var canon = HybrisWorldMapSaveData.FromCanon();
            Assert.AreEqual(14, canon.Nodes.Count);
            Assert.AreEqual(16, canon.Links.Count);
            var issues = HybrisWorldMapValidation.Validate(canon.Nodes, canon.Links, id => true);
            Assert.AreEqual(0, HybrisWorldMapValidation.CountErrors(issues));
        }

        [Test]
        public void SaveData_Clone_IsIndependent()
        {
            var doc = MiniMap();
            var clone = doc.Clone();
            Assert.IsNotNull(clone);
            Assert.AreEqual(2, clone.Nodes.Count);
            clone.Nodes[0].Name = "Modifié";
            Assert.AreEqual("Kingston", doc.Nodes[0].Name);
        }

        [Test]
        public void Validation_DuplicateId_IsError()
        {
            var doc = MiniMap();
            doc.Nodes.Add(new HybrisSectorNode("kingston", "Doublon", "", "", 0.5f, 0.5f, HybrisBiome.Foret, 1));
            var issues = HybrisWorldMapValidation.Validate(doc.Nodes, doc.Links);
            Assert.IsTrue(issues.Exists(i => i.Code == "node_duplicate_id" && i.Severity == WorldMapIssueSeverity.Error));
        }

        [Test]
        public void Validation_UnknownLinkEnd_IsError()
        {
            var doc = MiniMap();
            doc.Links.Add(new HybrisSectorLink("kingston", "nulle_part", 10, 1, "X"));
            var issues = HybrisWorldMapValidation.Validate(doc.Nodes, doc.Links);
            Assert.IsTrue(issues.Exists(i => i.Code == "link_unknown_to" && i.Severity == WorldMapIssueSeverity.Error));
        }

        [Test]
        public void Validation_SelfLink_IsError()
        {
            var doc = MiniMap();
            doc.Links.Add(new HybrisSectorLink("haliriel", "haliriel", 5, 1, "Boucle"));
            var issues = HybrisWorldMapValidation.Validate(doc.Nodes, doc.Links);
            Assert.IsTrue(issues.Exists(i => i.Code == "link_self"));
        }

        [Test]
        public void Validation_OrphanAndUnreachable_AreWarnings()
        {
            var doc = MiniMap();
            doc.Nodes.Add(new HybrisSectorNode("perdue", "Perdue", "", "", 0.9f, 0.9f, HybrisBiome.Jungle, 2));
            var issues = HybrisWorldMapValidation.Validate(doc.Nodes, doc.Links);
            Assert.IsTrue(issues.Exists(i => i.Code == "node_orphan" && i.Severity == WorldMapIssueSeverity.Warning));
            Assert.IsTrue(issues.Exists(i => i.Code == "node_unreachable" && i.Severity == WorldMapIssueSeverity.Warning));
        }

        [Test]
        public void Validation_BadId_IsError()
        {
            Assert.IsTrue(HybrisWorldMapValidation.IsValidId("kingston_2"));
            Assert.IsTrue(HybrisWorldMapValidation.IsValidId("ile-nord"));
            Assert.IsFalse(HybrisWorldMapValidation.IsValidId("avec espace"));
            Assert.IsFalse(HybrisWorldMapValidation.IsValidId(""));
            Assert.IsFalse(HybrisWorldMapValidation.IsValidId(null));
        }

        [Test]
        public void Repository_RoundTrip_PreservesData()
        {
            const string name = "test_worldmap_tmp";
            try
            {
                var doc = MiniMap();
                doc.MapName = name;
                string path = HybrisWorldMapRepository.Save(doc, name);
                Assert.IsNotNull(path);
                Assert.IsTrue(HybrisWorldMapRepository.TryLoad(name, out var loaded));
                Assert.IsNotNull(loaded);
                Assert.AreEqual(2, loaded.Nodes.Count);
                Assert.AreEqual(1, loaded.Links.Count);
                Assert.AreEqual("kingston", loaded.Nodes[0].Id);
                Assert.AreEqual(80, loaded.Links[0].Miles);
            }
            finally
            {
                HybrisWorldMapRepository.Delete(name);
            }
        }

        [Test]
        public void RuntimeOverride_ApplyFindAndClear()
        {
            try
            {
                Assert.IsFalse(HybrisWorldMapData.IsCustomized);
                var doc = MiniMap();
                doc.MapName = "test_override";
                HybrisWorldMapData.ApplyRuntimeData(doc);
                Assert.IsTrue(HybrisWorldMapData.IsCustomized);
                Assert.AreEqual("test_override", HybrisWorldMapData.ActiveMapName);
                Assert.IsNotNull(HybrisWorldMapData.Find("haliriel"));
                Assert.IsNull(HybrisWorldMapData.Find("brum_forges"));
                Assert.IsNotNull(HybrisWorldMapData.FindLink("kingston", "haliriel"));
            }
            finally
            {
                HybrisWorldMapData.ClearRuntimeData();
            }
            Assert.IsFalse(HybrisWorldMapData.IsCustomized);
            Assert.AreEqual("canon", HybrisWorldMapData.ActiveMapName);
            Assert.IsNotNull(HybrisWorldMapData.Find("brum_forges"));
        }

        [Test]
        public void VTT_WorldMapStateOp_Serializes()
        {
            string op = VTTProtocol.BuildWorldMapStateOp("hybris_overworld", 3, "kingston",
                new List<string> { "kingston", "haliriel" },
                new List<string> { "kingston" }, 6);
            Assert.AreEqual("op", VTTProtocol.PeekType(op));
            StringAssert.Contains("\"op\":\"worldmap\"", op);
            StringAssert.Contains("\"action\":\"state\"", op);
            StringAssert.Contains("\"partyNodeId\":\"kingston\"", op);
            StringAssert.Contains("\"revision\":3", op);
            Assert.IsTrue(VTTProtocol.IsGMOp(VTTProtocol.OpWorldMap));
        }

        [Test]
        public void VTT_WorldMapPingSectorOp_Serializes()
        {
            string op = VTTProtocol.BuildWorldMapPingSectorOp("haliriel", "#00FF00", "Rassemblement");
            Assert.AreEqual("op", VTTProtocol.PeekType(op));
            StringAssert.Contains("\"action\":\"ping_sector\"", op);
            StringAssert.Contains("\"sectorId\":\"haliriel\"", op);
            var payload = JsonUtility.FromJson<VTTWorldMapPayload>(
                VTTProtocol.ExtractObject(op, "payload"));
            Assert.IsNotNull(payload);
            Assert.AreEqual("haliriel", payload.sectorId);
            Assert.AreEqual("#00FF00", payload.colorHex);
        }
    }
}
#endif

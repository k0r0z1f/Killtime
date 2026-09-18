#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using System.IO;
using NUnit.Framework;
using Killtime.Story;
using Killtime.Story.Data;
using UnityEngine;

namespace Killtime.Tests
{
    [TestFixture]
    public class StorySceneChainingTests
    {
        [SetUp]
        public void Setup()
        {
            // Enregistrer des scènes de test dans le catalogue
            var s1 = new ScenarioDefinition
            {
                Id = "test_volume_1_scene_01",
                Volume = "Volume I",
                Title = "Première Scène",
                FirstNodeId = "node_start"
            };
            var s2 = new ScenarioDefinition
            {
                Id = "test_volume_1_scene_02",
                Volume = "Volume I",
                Title = "Deuxième Scène",
                FirstNodeId = "node_start"
            };
            var s3 = new ScenarioDefinition
            {
                Id = "test_volume_1_scene_03",
                Volume = "Volume I",
                Title = "Troisième Scène",
                NextSceneId = "custom_branch_scene",
                FirstNodeId = "node_start"
            };
            var sCustom = new ScenarioDefinition
            {
                Id = "custom_branch_scene",
                Volume = "Volume I",
                Title = "Branche Spéciale",
                FirstNodeId = "node_start"
            };

            ScenarioCatalog.Register(s1);
            ScenarioCatalog.Register(s2);
            ScenarioCatalog.Register(s3);
            ScenarioCatalog.Register(sCustom);
        }

        [TearDown]
        public void Teardown()
        {
            ScenarioCatalog.Unregister("test_volume_1_scene_01");
            ScenarioCatalog.Unregister("test_volume_1_scene_02");
            ScenarioCatalog.Unregister("test_volume_1_scene_03");
            ScenarioCatalog.Unregister("custom_branch_scene");
        }

        [Test]
        public void ScenarioCatalog_FromSceneData_TransfersNextSceneId()
        {
            var data = new StorySceneData
            {
                SceneId = "data_test_scene",
                Volume = "Vol 1",
                Title = "Titre Test",
                NextSceneId = "target_next_scene"
            };

            var def = ScenarioCatalog.FromSceneData(data);
            Assert.IsNotNull(def);
            Assert.AreEqual("target_next_scene", def.NextSceneId);
        }

        [Test]
        public void GetNextScenarioId_UsesExplicitNextSceneId_WhenSpecified()
        {
            string next = ScenarioCatalog.GetNextScenarioId("test_volume_1_scene_03");
            Assert.AreEqual("custom_branch_scene", next);
        }

        [Test]
        public void GetNextScenarioId_DeducesSequentialScene_WhenEmpty()
        {
            string next = ScenarioCatalog.GetNextScenarioId("test_volume_1_scene_01");
            Assert.AreEqual("test_volume_1_scene_02", next);
        }

        [Test]
        public void GetNextScenarioId_DeducesCrossPrefixScene_WhenNextNumberMatches()
        {
            var s4 = new ScenarioDefinition { Id = "volume_1_scene_04", Volume = "Volume I" };
            var s5 = new ScenarioDefinition { Id = "nouvelle_scene_5", Volume = "Volume I" };
            ScenarioCatalog.Register(s4);
            ScenarioCatalog.Register(s5);
            try
            {
                string next = ScenarioCatalog.GetNextScenarioId("volume_1_scene_04");
                Assert.AreEqual("nouvelle_scene_5", next);
            }
            finally
            {
                ScenarioCatalog.Unregister("volume_1_scene_04");
                ScenarioCatalog.Unregister("nouvelle_scene_5");
            }
        }

        [Test]
        public void ScenarioDirector_CompleteActiveScenario_FiresScenarioCompletedEvent()
        {
            var go = new GameObject("TestDirectorGo");
            var director = go.AddComponent<ScenarioDirector>();

            director.StartScenario("test_volume_1_scene_01");

            ScenarioDefinition completedDef = null;
            director.ScenarioCompleted += def => completedDef = def;

            director.CompleteActiveScenario();

            Assert.IsNotNull(completedDef);
            Assert.AreEqual("test_volume_1_scene_01", completedDef.Id);
            Assert.IsTrue(director.State.ActiveScenarioCompleted);

            Object.DestroyImmediate(go);
        }

        [Test]
        public void StorySceneRepository_SortSceneFilesByChain_OrdersAccordingToChaining()
        {
            string tempDir = Path.Combine(Application.temporaryCachePath, "TestChainOrder_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string p1 = Path.Combine(tempDir, "scene_a.json");
                string p2 = Path.Combine(tempDir, "scene_b.json");
                string p3 = Path.Combine(tempDir, "scene_c.json");

                // Chaînage : C -> A -> B
                File.WriteAllText(p3, JsonUtility.ToJson(new StorySceneData { SceneId = "scene_c", NextSceneId = "scene_a" }, true));
                File.WriteAllText(p1, JsonUtility.ToJson(new StorySceneData { SceneId = "scene_a", NextSceneId = "scene_b" }, true));
                File.WriteAllText(p2, JsonUtility.ToJson(new StorySceneData { SceneId = "scene_b", NextSceneId = "" }, true));

                var sorted = StorySceneRepository.SortSceneFilesByChain(new[] { p1, p2, p3 });

                Assert.AreEqual(3, sorted.Count);
                Assert.AreEqual(p3, sorted[0]);
                Assert.AreEqual(p1, sorted[1]);
                Assert.AreEqual(p2, sorted[2]);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }

        [Test]
        public void StorySceneRepository_UpdateSceneNextSceneId_UpdatesFileOnDisk()
        {
            string tempDir = Path.Combine(Application.temporaryCachePath, "TestUpdateNext_" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                string filePath = Path.Combine(tempDir, "test_update.json");
                File.WriteAllText(filePath, JsonUtility.ToJson(new StorySceneData { SceneId = "test_scene", NextSceneId = "old_target" }, true));

                bool updated = StorySceneRepository.UpdateSceneNextSceneId(filePath, "new_target_scene");
                Assert.IsTrue(updated);

                string readNext = StorySceneRepository.GetNextSceneIdFromPath(filePath);
                Assert.AreEqual("new_target_scene", readNext);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }
    }
}
#endif

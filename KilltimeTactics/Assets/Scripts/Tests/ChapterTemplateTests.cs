#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Killtime.Story.Data;
using Killtime.Story.Scenes;
using Killtime.Tactics.Grid;

namespace Killtime.Tests
{
    /// <summary>RD-055 — Template scène unique clonable : défauts, presets, mapping F7, duplication.</summary>
    [TestFixture]
    public class ChapterTemplateTests
    {
        [Test]
        public void CreateDefault_IsValid()
        {
            var config = ChapterTemplateLibrary.CreateDefault("ch_01_test");
            var errors = new List<string>();
            Assert.IsTrue(config.Validate(errors), string.Join(" | ", errors));
            Assert.AreEqual("ch_01_test", config.ChapterId);
            Assert.AreEqual(8, config.GridRadius);
            Assert.AreEqual(4, config.PlayerSpawns.Count);
            Assert.AreEqual(2, config.EnemySpawns.Count);
            Assert.AreEqual(1, config.Triggers.Count);
        }

        [Test]
        public void LightingPresets_AllOverrideAndVisionRanges()
        {
            foreach (ChapterLightingPreset preset in Enum.GetValues(typeof(ChapterLightingPreset)))
            {
                var lighting = ChapterLightingPresets.ToLightingData(preset);
                Assert.IsNotNull(lighting);
                Assert.IsTrue(lighting.OverrideLighting, preset.ToString());
                int vision = ChapterLightingPresets.VisionRange(preset);
                if (ChapterLightingPresets.IsInterior(preset))
                    Assert.AreEqual(6, vision, preset.ToString());
                else
                    Assert.AreEqual(12, vision, preset.ToString());
            }

            var day = ChapterLightingPresets.ToLightingData(ChapterLightingPreset.DaylightExterior);
            var night = ChapterLightingPresets.ToLightingData(ChapterLightingPreset.NightExterior);
            Assert.Greater(day.SunIntensity, night.SunIntensity);
            Assert.AreNotEqual(day.AmbientColor, night.AmbientColor);
        }

        [Test]
        public void ToStorySceneData_MapsGridCeilingSpawnsTriggersF7()
        {
            var config = ChapterTemplateLibrary.CreateDefault("ch_02_test");
            config.GridRadius = 10;
            config.CeilingHeight = 4.2f;
            var data = config.ToStorySceneData();

            Assert.AreEqual("ch_02_test", data.SceneId);
            Assert.IsNotNull(data.EmbeddedMap);
            Assert.AreEqual(10, data.EmbeddedMap.GridRadius);
            Assert.AreEqual(4.2f, data.EmbeddedMap.CeilingHeight, 0.001f);
            Assert.AreEqual("ch_02_test", data.EmbeddedMap.MapName);
            Assert.IsTrue(data.Lighting.OverrideLighting);

            // Spawns : 4 PJ initiaux + 2 ennemis en renfort au nœud action.
            Assert.AreEqual(6, data.Actors.Count);
            int initial = 0;
            foreach (var a in data.Actors)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(a.ActorId));
                if (a.SpawnInitially) initial++;
                else Assert.AreEqual("node_action", a.SpawnOnNodeId);
            }
            Assert.AreEqual(4, initial);

            // Trame F7 : briefing -> action (combat), 1 interactable, 1 trigger.
            Assert.AreEqual("node_briefing", data.FirstNodeId);
            Assert.AreEqual(2, data.Nodes.Count);
            var action = data.FindNode("node_action");
            Assert.IsNotNull(action);
            Assert.IsTrue(action.TriggerCombatOnEnter);
            Assert.AreEqual(1, data.Interactables.Count);
            Assert.AreEqual("node_action", data.Interactables[0].TriggerNodeId);
            Assert.AreEqual(1, data.Triggers.Count);
            Assert.AreEqual("obj_principal", data.Triggers[0].ObjectiveId);
        }

        [Test]
        public void CloneForNewChapter_RenamesKeepsStructureClearsChain()
        {
            var source = ChapterTemplateLibrary.CreateDefault("ch_00_modele");
            source.NextSceneId = "ch_02_suite";
            var clone = ChapterTemplateLibrary.CloneForNewChapter(source, "ch_03_nouveau", "Nouveau titre");

            Assert.AreEqual("ch_03_nouveau", clone.ChapterId);
            Assert.AreEqual("Nouveau titre", clone.Title);
            Assert.AreEqual("", clone.NextSceneId);
            Assert.AreEqual(4, clone.PlayerSpawns.Count);
            Assert.AreEqual(2, clone.EnemySpawns.Count);
            Assert.AreEqual("ch_00_modele", source.ChapterId, "La source ne doit pas être mutée.");
        }

        [Test]
        public void Validate_RejectsBadRadiusDuplicateSpawnsAndBadTrigger()
        {
            var config = ChapterTemplateLibrary.CreateDefault("ch_04_test");
            var errors = new List<string>();

            config.GridRadius = 99;
            Assert.IsFalse(config.Validate(errors));
            Assert.IsTrue(errors.Exists(e => e.Contains("GridRadius")));
            config.GridRadius = 8;

            config.EnemySpawns[0].ActorId = "PJ_1"; // doublon insensible à la casse
            Assert.IsFalse(config.Validate(errors));
            Assert.IsTrue(errors.Exists(e => e.Contains("double")));
            config.EnemySpawns[0].ActorId = "ennemi_1";

            config.Triggers[0].ObjectiveId = "";
            Assert.IsFalse(config.Validate(errors));
            config.Triggers[0].ObjectiveId = "obj_principal";

            config.EnemySpawns[0].Q = 50;
            Assert.IsFalse(config.Validate(errors), "Spawn hors grille.");
            config.EnemySpawns[0].Q = 0;

            Assert.IsTrue(config.Validate(errors), string.Join(" | ", errors));
        }

        [Test]
        public void JsonRoundTrip_PreservesSceneIdGridCeiling()
        {
            var config = ChapterTemplateLibrary.CreateDefault("ch_05_test");
            var data = config.ToStorySceneData();
            string json = JsonUtility.ToJson(data, true);
            Assert.IsFalse(string.IsNullOrWhiteSpace(json));

            var reloaded = JsonUtility.FromJson<StorySceneData>(json);
            Assert.IsNotNull(reloaded);
            StorySceneData.EnsureDeepDefaults(reloaded);
            Assert.AreEqual("ch_05_test", reloaded.SceneId);
            Assert.AreEqual(8, reloaded.EmbeddedMap.GridRadius);
            Assert.AreEqual(3.5f, reloaded.EmbeddedMap.CeilingHeight, 0.001f);
            Assert.AreEqual(6, reloaded.Actors.Count);
            Assert.AreEqual(2, reloaded.Nodes.Count);
        }

        [Test]
        public void ApplyGridAndLighting_SetsGridAndCeiling()
        {
            var go = new GameObject("[Test] ChapterTemplateGrid");
            var grid = go.AddComponent<TacticalHexGrid>();
            try
            {
                var config = ChapterTemplateLibrary.CreateDefault("ch_06_test");
                config.GridRadius = 6;
                config.CeilingHeight = 4.2f;
                Assert.IsTrue(ChapterTemplateLibrary.ApplyGridAndLighting(config, grid));
                Assert.AreEqual(6, grid.GridRadius);
                Assert.AreEqual(4.2f, grid.CeilingHeight, 0.001f);

                Assert.IsFalse(ChapterTemplateLibrary.ApplyGridAndLighting(null, grid));
                Assert.IsFalse(ChapterTemplateLibrary.ApplyGridAndLighting(config, null));
                var bad = ChapterTemplateLibrary.CreateDefault("bad test id with spaces");
                bad.GridRadius = 4;
                Assert.IsFalse(ChapterTemplateLibrary.ApplyGridAndLighting(bad, grid));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ToStorySceneData_Interactable_HasDescriptionAndLegacyCompatibleAction()
        {
            var config = ChapterTemplateLibrary.CreateDefault("ch_07_test");
            var data = config.ToStorySceneData();

            Assert.AreEqual(1, data.Interactables.Count);
            var it = data.Interactables[0];
            Assert.IsFalse(string.IsNullOrWhiteSpace(it.Description));
            Assert.IsNotNull(it.Actions);
            Assert.AreEqual(1, it.Actions.Count);
            Assert.AreEqual("node_action", it.Actions[0].TriggerNodeId);
            Assert.AreEqual("node_action", it.TriggerNodeId);
        }
    }
}
#endif

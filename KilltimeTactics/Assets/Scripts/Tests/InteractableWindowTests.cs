#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Killtime.Core.Character;
using Killtime.Story.Data;
using Killtime.Story.Scenes;

namespace Killtime.Tests
{
    /// <summary>
    /// Fenêtre flottante générique des interactables : fallback legacy,
    /// succès sans jet, consommation one-shot au niveau action.
    /// </summary>
    [TestFixture]
    public class InteractableWindowTests
    {
        [Test]
        public void FallbackActions_LegacyData_BuildsOneAction()
        {
            var data = new SceneInteractableSpawnData
            {
                InteractableId = "obj_test",
                ActionLabel = "Ouvrir",
                RequiredSkill = SkillType.IngenierieArcanotech,
                SkillThreshold = 6,
                SuccessLog = "Log succès",
                FailureLog = "Log échec",
                CompletionObjectiveId = "obj_done",
                TriggerNodeId = "node_next",
                IsOneShot = true,
                Actions = new List<SceneInteractableAction>()
            };

            var effective = data.GetEffectiveActions();

            Assert.AreEqual(1, effective.Count);
            Assert.AreEqual("Ouvrir", effective[0].Label);
            Assert.AreEqual((int)SkillType.IngenierieArcanotech, effective[0].RequiredSkill);
            Assert.AreEqual(6, effective[0].SkillThreshold);
            Assert.AreEqual("Log succès", effective[0].SuccessLog);
            Assert.AreEqual("Log échec", effective[0].FailureLog);
            Assert.AreEqual("obj_done", effective[0].CompletionObjectiveId);
            Assert.AreEqual("node_next", effective[0].TriggerNodeId);
            Assert.IsTrue(effective[0].IsOneShot);
        }

        [Test]
        public void FallbackActions_ExplicitActions_Preserved()
        {
            var data = new SceneInteractableSpawnData
            {
                Actions = new List<SceneInteractableAction>
                {
                    new SceneInteractableAction { ActionId = "a1", Label = "A1" },
                    new SceneInteractableAction { ActionId = "a2", Label = "A2" }
                }
            };

            var effective = data.GetEffectiveActions();

            Assert.AreEqual(2, effective.Count);
            Assert.AreEqual("A1", effective[0].Label);
            Assert.AreEqual("A2", effective[1].Label);
        }

        [Test]
        public void AttemptInteractableAction_FreeAction_SucceedsWithoutRollAndConsumesOnce()
        {
            var go = new GameObject("TestController");
            var controller = go.AddComponent<JsonStorySceneController>();
            try
            {
                var data = new StorySceneData { SceneId = "test_free_action" };
                var interactable = new SceneInteractableSpawnData
                {
                    InteractableId = "obj_test",
                    DisplayName = "Test",
                    Actions = new List<SceneInteractableAction>()
                };
                data.Interactables.Add(interactable);
                controller.SetSceneData(data);

                var action = new SceneInteractableAction
                {
                    ActionId = "legacy",
                    Label = "Ouvrir",
                    RequiredSkill = -1,
                    SkillThreshold = 0,
                    SuccessLog = "Réussi sans jet",
                    IsOneShot = true
                };

                var attemptMethod = typeof(JsonStorySceneController).GetMethod("AttemptInteractableAction",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(attemptMethod);

                // Première utilisation : succès et consommation.
                Assert.DoesNotThrow(() => attemptMethod.Invoke(controller, new object[] { null, interactable, action }));

                var consumedField = typeof(JsonStorySceneController).GetField("_consumedActionKeys",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                var consumed = consumedField.GetValue(controller) as HashSet<string>;
                Assert.IsNotNull(consumed);
                Assert.IsTrue(consumed.Contains("obj_test/legacy"));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
#endif

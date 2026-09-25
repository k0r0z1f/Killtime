#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Killtime.CameraSystem;
using Killtime.Story.Data;
using Killtime.Story.Scenes;
using Killtime.Tactics;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;

namespace Killtime.Tests
{
    [TestFixture]
    public class SceneCinematicTests
    {
        [Test]
        public void EnsureDeepDefaults_InitializesCinematicsAndLinks()
        {
            var data = new StorySceneData
            {
                SceneId = "cine_defaults",
                Nodes = new List<SceneNodeData> { new SceneNodeData { NodeId = "node_1" } }
            };
            data.Nodes[0].Dialogues.Add(new SceneDialogueLineData { LineId = "line_1" });
            data.Nodes[0].Events.Add(new SceneEventData { EventId = "evt_1" });
            data.Nodes[0].Consequences.Add(new SceneConsequenceData { ConsequenceId = "cons_1" });
            data.Triggers.Add(new SceneTriggerData { TriggerId = "trig_1" });
            data.Interactables.Add(new SceneInteractableSpawnData { InteractableId = "obj_1" });
            data.Actors.Add(new SceneActorSpawnData { ActorId = "actor_1" });
            data.Cinematics.Add(new SceneCinematicData { CinematicId = "cine_1" });

            // Simule un vieux JSON : tout à null.
            data.Cinematics = null;
            data.Nodes[0].CinematicIds = null;
            data.Nodes[0].Dialogues[0].CinematicIds = null;
            data.Nodes[0].Events[0].CinematicIds = null;
            data.Nodes[0].Consequences[0].CinematicIds = null;
            data.Triggers[0].CinematicIds = null;
            data.Interactables[0].CinematicIds = null;
            data.Actors[0].CinematicIds = null;

            StorySceneData.EnsureDeepDefaults(data);

            Assert.IsNotNull(data.Cinematics);
            Assert.IsNotNull(data.Nodes[0].CinematicIds);
            Assert.IsNotNull(data.Nodes[0].Dialogues[0].CinematicIds);
            Assert.IsNotNull(data.Nodes[0].Events[0].CinematicIds);
            Assert.IsNotNull(data.Nodes[0].Consequences[0].CinematicIds);
            Assert.IsNotNull(data.Triggers[0].CinematicIds);
            Assert.IsNotNull(data.Interactables[0].CinematicIds);
            Assert.IsNotNull(data.Actors[0].CinematicIds);
        }

        [Test]
        public void FindCinematic_IsCaseInsensitive()
        {
            var data = new StorySceneData { SceneId = "cine_find" };
            data.Cinematics.Add(new SceneCinematicData { CinematicId = "Cine_Intro", Title = "Intro" });

            Assert.IsNotNull(data.FindCinematic("cine_intro"));
            Assert.IsNotNull(data.FindCinematic("CINE_INTRO"));
            Assert.IsNull(data.FindCinematic("cine_missing"));
            Assert.IsNull(data.FindCinematic(""));
        }

        [Test]
        public void AddCineLink_DedupesAndIgnoresBlanks()
        {
            var list = new List<string>();
            StorySceneData.AddCineLink(list, "cine_1");
            StorySceneData.AddCineLink(list, "CINE_1");
            StorySceneData.AddCineLink(list, "");
            StorySceneData.AddCineLink(list, null);
            StorySceneData.AddCineLink(null, "cine_2");

            Assert.AreEqual(1, list.Count);
            Assert.AreEqual("cine_1", list[0]);
            Assert.AreEqual("🎬→ cine_1", StorySceneData.GetCineSummary(list));
            Assert.AreEqual("(aucune)", StorySceneData.GetCineSummary(new List<string>()));
            Assert.AreEqual("(aucune)", StorySceneData.GetCineSummary(null));
        }

        [Test]
        public void CinematicData_GetSummaryCountsShotsAndPoses()
        {
            var cine = new SceneCinematicData { CinematicId = "cine_sum", PlaybackSpeed = 1f };
            Assert.AreEqual("(aucun plan)", cine.GetSummary());

            cine.Shots.Add(new SceneCinematicShotData
            {
                ShotId = "shot_1",
                Duration = 2f,
                StartPoses = new List<SceneCinematicActorPose> { new SceneCinematicActorPose { ActorId = "a" } },
                EndPoses = new List<SceneCinematicActorPose> { new SceneCinematicActorPose { ActorId = "a" } }
            });
            StringAssert.Contains("1 plan", cine.GetSummary());
            StringAssert.Contains("2 poses", cine.Shots[0].GetSummary());
        }

        [Test]
        public void CinematicJson_RoundtripsShotsAndLinks()
        {
            var data = new StorySceneData { SceneId = "cine_json", Title = "T" };
            var node = new SceneNodeData { NodeId = "node_1" };
            node.CinematicIds.Add("cine_1");
            node.Dialogues.Add(new SceneDialogueLineData { LineId = "l1", CinematicIds = new List<string> { "cine_1" } });
            data.Nodes.Add(node);
            var cine = new SceneCinematicData { CinematicId = "cine_1", Title = "Intro", PlaybackSpeed = 2f };
            cine.Shots.Add(new SceneCinematicShotData
            {
                ShotId = "shot_1",
                Label = "Ouverture",
                Duration = 3f,
                CamStartPos = new Vector3(0f, 9f, -9f),
                CamEndPos = new Vector3(1f, 4f, -5f),
                Ease = CinematicEase.EaseInOut,
                MoveEffect = CinematicCameraEffect.PushIn,
                StartPoses = new List<SceneCinematicActorPose> { new SceneCinematicActorPose { ActorId = "lucas", Q = 1, R = 2, FacingAngle = 90f } },
                EndPoses = new List<SceneCinematicActorPose> { new SceneCinematicActorPose { ActorId = "lucas", Q = 3, R = 4, FacingAngle = 180f } }
            });
            data.Cinematics.Add(cine);

            string json = JsonUtility.ToJson(data, true);
            var loaded = JsonUtility.FromJson<StorySceneData>(json);
            StorySceneData.EnsureDeepDefaults(loaded);

            Assert.AreEqual(1, loaded.Cinematics.Count);
            var back = loaded.FindCinematic("CINE_1");
            Assert.IsNotNull(back);
            Assert.AreEqual(1, back.Shots.Count);
            Assert.AreEqual(CinematicEase.EaseInOut, back.Shots[0].Ease);
            Assert.AreEqual(CinematicCameraEffect.PushIn, back.Shots[0].MoveEffect);
            Assert.AreEqual(3f, back.Shots[0].Duration, 0.001f);
            Assert.AreEqual("lucas", back.Shots[0].StartPoses[0].ActorId);
            Assert.AreEqual(3, back.Shots[0].EndPoses[0].Q);
            Assert.AreEqual(1, loaded.Nodes[0].CinematicIds.Count);
            Assert.AreEqual("cine_1", loaded.Nodes[0].Dialogues[0].CinematicIds[0]);
        }

        [Test]
        public void CinematicEase_ClampsAndHitsBounds()
        {
            Assert.AreEqual(0f, CinematicDirector.ApplyCinematicEase(CinematicEase.Linear, 0f), 0.0001f);
            Assert.AreEqual(0.5f, CinematicDirector.ApplyCinematicEase(CinematicEase.Linear, 0.5f), 0.0001f);
            Assert.AreEqual(1f, CinematicDirector.ApplyCinematicEase(CinematicEase.Linear, 1f), 0.0001f);
            Assert.AreEqual(0f, CinematicDirector.ApplyCinematicEase(CinematicEase.Smooth, 0f), 0.0001f);
            Assert.AreEqual(1f, CinematicDirector.ApplyCinematicEase(CinematicEase.Smooth, 1f), 0.0001f);
            Assert.AreEqual(1f, CinematicDirector.ApplyCinematicEase(CinematicEase.EaseOut, 1f), 0.0001f);
            Assert.AreEqual(0f, CinematicDirector.ApplyCinematicEase(CinematicEase.EaseInOut, -2f), 0.0001f);
            Assert.AreEqual(1f, CinematicDirector.ApplyCinematicEase(CinematicEase.EaseInOut, 5f), 0.0001f);
        }

        [Test]
        public void CinematicDriveLock_DefaultOff_AndBlocksNothingByDefault()
        {
            var go = new GameObject("TestCineUnit");
            var unit = go.AddComponent<TacticalUnit>();
            try
            {
                // Par défaut le verrou est levé : MoveAlongPath garde son comportement normal.
                Assert.IsFalse(unit.CinematicDriveActive);
                unit.CinematicDriveActive = true;
                Assert.IsTrue(unit.CinematicDriveActive);
                unit.CinematicDriveActive = false;
                Assert.IsFalse(unit.CinematicDriveActive);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void PropRelocate_UpdatesLogicCoordsEvenWithoutGrid()
        {
            var go = new GameObject("TestCineProp");
            var prop = go.AddComponent<TacticalInteractable>();
            try
            {
                prop.Configure("Console", "Examiner", new HexCoordinates(1, 1), 1);
                // Sans grille : la logique suit quand même (le visuel ne peut pas être résolu).
                prop.Relocate(new HexCoordinates(3, 4), null);
                Assert.AreEqual(3, prop.Coordinates.Q);
                Assert.AreEqual(4, prop.Coordinates.R);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void FireLinkedCinematics_EmptyListIsNoOp()
        {
            var go = new GameObject("TestCineController");
            var controller = go.AddComponent<JsonStorySceneController>();
            try
            {
                controller.SetSceneData(new StorySceneData { SceneId = "cine_noop" });
                Assert.DoesNotThrow(() => controller.FireLinkedCinematics(null));
                Assert.DoesNotThrow(() => controller.FireLinkedCinematics(new List<string>()));
                // Id inconnu : avertissement, pas d'exception (non-bloquant).
                Assert.DoesNotThrow(() => controller.FireLinkedCinematics(new List<string> { "cine_missing" }));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
#endif

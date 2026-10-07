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
        public void LegacyCameraFields_StillDeserializeForCompat()
        {
            // Vieux JSON avec focus/shake hardcodés : les champs survivent au parse
            // (compat), même si le runtime les ignore au profit des cartes 🎬.
            string legacy = "{\"SceneId\":\"legacy_cam\",\"Nodes\":[{\"NodeId\":\"n1\",\"Dialogues\":[{" +
                "\"LineId\":\"l1\",\"CameraFocusActorId\":\"actor_x\",\"CameraPitch\":25.0,\"CameraDistance\":4.0," +
                "\"Ambience\":{\"HasAmbience\":true,\"CameraShakeIntensity\":0.5}}]," +
                "\"Events\":[{\"EventId\":\"e1\",\"Kind\":4,\"TargetActorId\":\"actor_x\"}]}]}";
            var loaded = JsonUtility.FromJson<StorySceneData>(legacy);
            StorySceneData.EnsureDeepDefaults(loaded);
            Assert.AreEqual("actor_x", loaded.Nodes[0].Dialogues[0].CameraFocusActorId);
            Assert.AreEqual(0.5f, loaded.Nodes[0].Dialogues[0].Ambience.CameraShakeIntensity, 0.001f);
            Assert.AreEqual(SceneEventKind.CameraShake, loaded.Nodes[0].Events[0].Kind);
            // Les sorties 🎬 existent et sont vides (prêtes pour la migration).
            Assert.IsNotNull(loaded.Nodes[0].Dialogues[0].CinematicIds);
            Assert.IsNotNull(loaded.Nodes[0].Events[0].CinematicIds);
        }

        [Test]
        public void CinematicShot_NewModesDefaultOff()
        {
            var shot = new SceneCinematicShotData();
            Assert.IsFalse(shot.RelativeToCurrent);
            Assert.IsFalse(shot.TrackFocusActor);
            Assert.AreEqual(7f, shot.TrackDistance, 0.001f);
            Assert.AreEqual(45f, shot.TrackYaw, 0.001f);
            // Effet supraluminique : désactivé par défaut (voûte normale).
            Assert.IsFalse(shot.SuperluminalWarp);
        }

        [Test]
        public void CinematicShot_WarpSummaryAndRoundtrip()
        {
            var shot = new SceneCinematicShotData { ShotId = "shot_warp", Duration = 20f };
            StringAssert.DoesNotContain("warp", shot.GetSummary());
            shot.SuperluminalWarp = true;
            StringAssert.Contains("warp", shot.GetSummary());

            var data = new StorySceneData { SceneId = "cine_warp" };
            var cine = new SceneCinematicData { CinematicId = "cine_warp_1" };
            cine.Shots.Add(shot);
            data.Cinematics.Add(cine);

            var loaded = JsonUtility.FromJson<StorySceneData>(JsonUtility.ToJson(data, true));
            StorySceneData.EnsureDeepDefaults(loaded);

            var back = loaded.FindCinematic("cine_warp_1");
            Assert.IsNotNull(back);
            Assert.IsTrue(back.Shots[0].SuperluminalWarp);
        }

        [Test]
        public void CinematicShot_LegacyJsonWithoutWarpDefaultsOff()
        {
            // Vieux JSON sans le champ (ex : scène 01 avant l'effet) : pas de crash,
            // warp désactivé (comportement identique à avant).
            string legacy = "{\"SceneId\":\"legacy_warp\",\"Cinematics\":[{\"CinematicId\":\"cine_1\",\"Shots\":[{\"ShotId\":\"shot_1\",\"Duration\":20.0}]}]}";
            var loaded = JsonUtility.FromJson<StorySceneData>(legacy);
            StorySceneData.EnsureDeepDefaults(loaded);
            Assert.IsNotNull(loaded.FindCinematic("cine_1"));
            Assert.IsFalse(loaded.FindCinematic("cine_1").Shots[0].SuperluminalWarp);
        }

        [Test]
        public void CinematicJson_RoundtripsTrackingAndRelative()
        {
            var data = new StorySceneData { SceneId = "cine_modes" };
            var cine = new SceneCinematicData { CinematicId = "cine_track" };
            cine.Shots.Add(new SceneCinematicShotData
            {
                ShotId = "shot_1",
                TrackFocusActor = true,
                TrackDistance = 4f,
                TrackYaw = 45f,
                FocusActorId = "actor_lucas"
            });
            var cine2 = new SceneCinematicData { CinematicId = "cine_rel" };
            cine2.Shots.Add(new SceneCinematicShotData
            {
                ShotId = "shot_1",
                RelativeToCurrent = true,
                MoveEffect = CinematicCameraEffect.HandheldShake,
                ShakeIntensity = 0.4f
            });
            data.Cinematics.Add(cine);
            data.Cinematics.Add(cine2);

            var loaded = JsonUtility.FromJson<StorySceneData>(JsonUtility.ToJson(data, true));
            StorySceneData.EnsureDeepDefaults(loaded);

            var back = loaded.FindCinematic("cine_track");
            Assert.IsTrue(back.Shots[0].TrackFocusActor);
            Assert.AreEqual(4f, back.Shots[0].TrackDistance, 0.001f);
            var back2 = loaded.FindCinematic("cine_rel");
            Assert.IsTrue(back2.Shots[0].RelativeToCurrent);
            Assert.AreEqual(CinematicCameraEffect.HandheldShake, back2.Shots[0].MoveEffect);
        }

        [Test]
        public void CinematicOutput_NextTargetDefaultsEmptyAndRoundtrips()
        {
            var cine = new SceneCinematicData { CinematicId = "cine_out" };
            Assert.AreEqual("", cine.NextTargetId);

            var data = new StorySceneData { SceneId = "cine_chain" };
            cine.NextTargetId = "line_next";
            data.Cinematics.Add(cine);

            var loaded = JsonUtility.FromJson<StorySceneData>(JsonUtility.ToJson(data, true));
            StorySceneData.EnsureDeepDefaults(loaded);
            Assert.AreEqual("line_next", loaded.FindCinematic("cine_out").NextTargetId);
        }

        [Test]
        public void ExecuteCinematicChain_UnknownTargetIsTolerated()
        {
            // Cible inexistante : aucun saut, aucune exception (contrat non-bloquant).
            var go = new GameObject("TestCineChain");
            var controller = go.AddComponent<JsonStorySceneController>();
            try
            {
                var data = new StorySceneData { SceneId = "cine_chain_tol" };
                data.Nodes.Add(new SceneNodeData { NodeId = "node_1" });
                controller.SetSceneData(data);
                // Pas de directeur actif : la chaîne ne doit rien faire mais ne pas lever.
                Assert.DoesNotThrow(() => controller.FireLinkedCinematics(new List<string> { "cine_missing" }));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void CinematicChainHead_ReturnsEarliestUpstream()
        {
            // cine_11 ─fin→ cine_1 ─fin→ line : la tête de cine_1 est cine_11
            // (c'est elle qui doit démarrer, le reste suit via Fin→).
            var data = new StorySceneData { SceneId = "cine_head" };
            data.Cinematics.Add(new SceneCinematicData { CinematicId = "cine_1", NextTargetId = "line_vance_01" });
            data.Cinematics.Add(new SceneCinematicData { CinematicId = "cine_11", NextTargetId = "cine_1" });

            Assert.AreEqual("cine_11", data.FindCinematicChainHead("cine_1"));
            Assert.AreEqual("cine_11", data.FindCinematicChainHead("cine_11"));
            Assert.AreEqual("CINE_11", data.FindCinematicChainHead("CINE_1").ToUpperInvariant());
        }

        [Test]
        public void CinematicChainHead_NoUpstreamOrAmbiguousKeepsDirect()
        {
            var data = new StorySceneData { SceneId = "cine_head_direct" };
            data.Cinematics.Add(new SceneCinematicData { CinematicId = "cine_1", NextTargetId = "line_x" });
            // Sans amont : soi-même (comportement inchangé).
            Assert.AreEqual("cine_1", data.FindCinematicChainHead("cine_1"));
            // Inconnu / vide : toléré, renvoyé tel quel.
            Assert.AreEqual("cine_missing", data.FindCinematicChainHead("cine_missing"));
            Assert.AreEqual("", data.FindCinematicChainHead(""));
            // Embranchement ambigu (2 amonts) : garde le direct (ancien comportement).
            data.Cinematics.Add(new SceneCinematicData { CinematicId = "cine_a", NextTargetId = "cine_1" });
            data.Cinematics.Add(new SceneCinematicData { CinematicId = "cine_b", NextTargetId = "cine_1" });
            Assert.AreEqual("cine_1", data.FindCinematicChainHead("cine_1"));
        }

        [Test]
        public void CinematicChainHead_CycleTerminates()
        {
            // Boucle A→B→A : termine sans tourner en rond.
            var data = new StorySceneData { SceneId = "cine_head_cycle" };
            data.Cinematics.Add(new SceneCinematicData { CinematicId = "cine_a", NextTargetId = "cine_b" });
            data.Cinematics.Add(new SceneCinematicData { CinematicId = "cine_b", NextTargetId = "cine_a" });
            Assert.DoesNotThrow(() => data.FindCinematicChainHead("cine_a"));
            Assert.DoesNotThrow(() => data.FindCinematicChainHead("cine_b"));
        }

        [Test]
        public void SoundCard_DefaultsAndSummary()
        {
            var snd = new SceneSoundData();
            Assert.AreEqual("snd_1", snd.SoundId);
            Assert.AreEqual(0f, snd.DelaySeconds, 0.001f);
            Assert.AreEqual(SoundTriggerPoint.AtLinkedStart, snd.TriggerPoint);
            Assert.AreEqual(0.8f, snd.VolumeScale, 0.001f);
            Assert.AreEqual("", snd.SourceCardId);
            StringAssert.Contains("non liée", snd.GetSummary());

            snd.SoundCueId = "UI_Click";
            snd.TriggerPoint = SoundTriggerPoint.AtLinkedEnd;
            snd.DelaySeconds = 1.5f;
            snd.SourceCardId = "line_1";
            StringAssert.Contains("fin", snd.GetSummary());
            StringAssert.Contains("line_1", snd.GetSummary());
        }

        [Test]
        public void SoundCard_JsonRoundtripsAndFindsBySource()
        {
            var data = new StorySceneData { SceneId = "snd_json" };
            data.Sounds.Add(new SceneSoundData
            {
                SoundId = "snd_1",
                Title = "Stinger",
                SoundCueId = "Victory_Stinger",
                DelaySeconds = 0.5f,
                TriggerPoint = SoundTriggerPoint.AtLinkedEnd,
                VolumeScale = 1.2f,
                SourceCardId = "line_1"
            });
            data.Sounds.Add(new SceneSoundData { SoundId = "snd_2", SourceCardId = "line_1" });
            data.Sounds.Add(new SceneSoundData { SoundId = "snd_3", SourceCardId = "evt_9" });

            var loaded = JsonUtility.FromJson<StorySceneData>(JsonUtility.ToJson(data, true));
            StorySceneData.EnsureDeepDefaults(loaded);

            var back = loaded.FindSound("SND_1");
            Assert.IsNotNull(back);
            Assert.AreEqual("Victory_Stinger", back.SoundCueId);
            Assert.AreEqual(0.5f, back.DelaySeconds, 0.001f);
            Assert.AreEqual(SoundTriggerPoint.AtLinkedEnd, back.TriggerPoint);
            Assert.AreEqual(1.2f, back.VolumeScale, 0.001f);

            var fromLine = loaded.FindSoundsFrom("line_1");
            Assert.AreEqual(2, fromLine.Count);
            Assert.AreEqual(0, loaded.FindSoundsFrom("evt_missing").Count);
            Assert.AreEqual(0, loaded.FindSoundsFrom("").Count);
        }

        [Test]
        public void SoundCard_LegacyJsonWithoutSoundsDefaultsEmpty()
        {
            // Vieux JSON sans "Sounds" : liste vide, pas de crash, clamps appliqués.
            string legacy = "{\"SceneId\":\"legacy_snd\",\"Sounds\":[{\"SoundId\":\"\",\"DelaySeconds\":-3.0,\"VolumeScale\":9.0}]}";
            var loaded = JsonUtility.FromJson<StorySceneData>(legacy);
            StorySceneData.EnsureDeepDefaults(loaded);
            Assert.IsNotNull(loaded.Sounds);
            Assert.AreEqual("snd_1", loaded.Sounds[0].SoundId);
            Assert.AreEqual(0f, loaded.Sounds[0].DelaySeconds, 0.001f);
            Assert.AreEqual(2f, loaded.Sounds[0].VolumeScale, 0.001f);

            string legacy2 = "{\"SceneId\":\"legacy_snd2\"}";
            var loaded2 = JsonUtility.FromJson<StorySceneData>(legacy2);
            StorySceneData.EnsureDeepDefaults(loaded2);
            Assert.IsNotNull(loaded2.Sounds);
            Assert.AreEqual(0, loaded2.Sounds.Count);
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

        [Test]
        public void CinematicData_HasLetterboxAndHideSceneChatFlags()
        {
            var cine = new SceneCinematicData { CinematicId = "cine_flags" };
            // Par défaut, cinématiques immersives = bandes noires et masquer chat
            Assert.IsTrue(cine.Letterbox);
            Assert.IsTrue(cine.HideSceneChat);

            // Désactivation pour les cinématiques de focus/dialogue en direct
            cine.Letterbox = false;
            cine.HideSceneChat = false;
            Assert.IsFalse(cine.Letterbox);
            Assert.IsFalse(cine.HideSceneChat);
        }
    }
}
#endif

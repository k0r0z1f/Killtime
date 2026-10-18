#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using NUnit.Framework;
using Killtime.Core.Character;
using Killtime.Story;
using Killtime.Story.Data;

namespace Killtime.Tests
{
    [TestFixture]
    public class StorySceneValidatorTests
    {
        static StorySceneData BuildValidScene()
        {
            var data = new StorySceneData
            {
                Version = 1,
                SceneId = "scene_test",
                Title = "Scène test",
                FirstNodeId = "node_a",
                NextSceneId = "scene_next"
            };
            data.Actors.Add(new SceneActorSpawnData { ActorId = "actor_john", DisplayName = "John" });
            data.Cinematics.Add(new SceneCinematicData { CinematicId = "cine_1" });
            var nodeA = new SceneNodeData { NodeId = "node_a", NextNodeId = "node_b" };
            nodeA.CinematicIds.Add("cine_1");
            nodeA.Objectives.Add(new ScenarioObjective { Id = "obj_1", Label = "Objectif" });
            nodeA.Dialogues.Add(new SceneDialogueLineData
            {
                LineId = "line_1",
                SpeakerId = "John",
                NextLineId = "line_2"
            });
            var line2 = new SceneDialogueLineData { LineId = "line_2", SpeakerId = "John" };
            line2.Choices.Add(new SceneDialogueChoiceData
            {
                ChoiceId = "ch_1",
                NextLineId = "cons_1",
                Challenge = new SceneDialogueChallengeData
                {
                    HasChallenge = true,
                    RequiredSkill = SkillType.Communication,
                    TargetDC = 9,
                    SuccessConsequenceId = "cons_1"
                }
            });
            nodeA.Dialogues.Add(line2);
            nodeA.Consequences.Add(new SceneConsequenceData
            {
                ConsequenceId = "cons_1",
                NextLineId = "line_1",
                Rewards = new SceneDialogueRewardData { CompletionObjectiveId = "obj_1" }
            });
            data.Nodes.Add(nodeA);
            data.Nodes.Add(new SceneNodeData { NodeId = "node_b" });
            data.Triggers.Add(new SceneTriggerData
            {
                TriggerId = "trig_1",
                Kind = SceneTriggerKind.CampaignFlagSet,
                SourceNodeId = "node_a",
                TargetNodeId = "node_b",
                FlagKey = "flag_x"
            });
            data.Interactables.Add(new SceneInteractableSpawnData
            {
                InteractableId = "inter_1",
                TriggerNodeId = "node_b"
            });
            return data;
        }

        [Test]
        public void ValidScene_Passes()
        {
            var errors = StorySceneValidator.Validate(BuildValidScene());
            Assert.IsEmpty(errors, string.Join("\n", errors));
        }

        [Test]
        public void NullData_Fails() => Assert.IsNotEmpty(StorySceneValidator.Validate(null));

        [Test]
        public void EmptyNodes_Fails()
        {
            var d = BuildValidScene();
            d.Nodes.Clear();
            Assert.IsNotEmpty(StorySceneValidator.Validate(d));
        }

        [Test]
        public void MissingFirstNode_Fails()
        {
            var d = BuildValidScene();
            d.FirstNodeId = "node_missing";
            var errors = StorySceneValidator.Validate(d);
            Assert.IsTrue(errors.Exists(e => e.Contains("FirstNodeId")));
        }

        [Test]
        public void DuplicateNodeId_Fails()
        {
            var d = BuildValidScene();
            d.Nodes.Add(new SceneNodeData { NodeId = "node_a" });
            Assert.IsNotEmpty(StorySceneValidator.Validate(d));
        }

        [Test]
        public void DanglingNextNodeId_Fails()
        {
            var d = BuildValidScene();
            d.Nodes[0].NextNodeId = "node_missing";
            var errors = StorySceneValidator.Validate(d);
            Assert.IsTrue(errors.Exists(e => e.Contains("NextNodeId")));
        }

        [Test]
        public void SelfNextSceneId_Fails()
        {
            var d = BuildValidScene();
            d.NextSceneId = "scene_test";
            Assert.IsNotEmpty(StorySceneValidator.Validate(d));
        }

        [Test]
        public void DanglingDialogueNextLine_Fails()
        {
            var d = BuildValidScene();
            d.Nodes[0].Dialogues[0].NextLineId = "line_missing";
            Assert.IsNotEmpty(StorySceneValidator.Validate(d));
        }

        [Test]
        public void DuplicateLineId_Fails()
        {
            var d = BuildValidScene();
            d.Nodes[0].Dialogues.Add(new SceneDialogueLineData { LineId = "line_1" });
            Assert.IsNotEmpty(StorySceneValidator.Validate(d));
        }

        [Test]
        public void DanglingChallengeConsequence_Fails()
        {
            var d = BuildValidScene();
            d.Nodes[0].Dialogues[1].Choices[0].Challenge.SuccessConsequenceId = "cons_missing";
            Assert.IsNotEmpty(StorySceneValidator.Validate(d));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(31)]
        public void InvalidChallengeDC_Fails(int dc)
        {
            var d = BuildValidScene();
            d.Nodes[0].Dialogues[1].Choices[0].Challenge.TargetDC = dc;
            Assert.IsNotEmpty(StorySceneValidator.Validate(d));
        }

        [Test]
        public void UnknownSpecificActor_Fails()
        {
            var d = BuildValidScene();
            d.Nodes[0].Dialogues[1].Choices[0].Challenge.SpecificActorId = "actor_ghost";
            Assert.IsNotEmpty(StorySceneValidator.Validate(d));
        }

        [Test]
        public void DanglingTriggerTarget_Fails()
        {
            var d = BuildValidScene();
            d.Triggers[0].TargetNodeId = "node_missing";
            Assert.IsNotEmpty(StorySceneValidator.Validate(d));
        }

        [Test]
        public void DanglingTalkTarget_Fails()
        {
            var d = BuildValidScene();
            d.Actors[0].TalkEntries.Add(new SceneActorTalkEntry { SpeakerId = "John", TargetId = "line_missing" });
            Assert.IsNotEmpty(StorySceneValidator.Validate(d));
        }

        [Test]
        public void DanglingCinematicLink_Fails()
        {
            var d = BuildValidScene();
            d.Nodes[0].CinematicIds.Add("cine_missing");
            Assert.IsNotEmpty(StorySceneValidator.Validate(d));
        }

        [Test]
        public void DanglingObjectiveRef_Fails()
        {
            var d = BuildValidScene();
            d.Nodes[0].Consequences[0].Rewards.CompletionObjectiveId = "obj_missing";
            Assert.IsNotEmpty(StorySceneValidator.Validate(d));
        }

        [Test]
        public void UnknownSpeaker_Fails()
        {
            var d = BuildValidScene();
            d.Nodes[0].Dialogues[0].SpeakerId = "Inconnu";
            Assert.IsNotEmpty(StorySceneValidator.Validate(d));
        }

        [Test]
        public void CrossNodeLineLink_Fails()
        {
            // Le runtime résout les liens de répliques au nœud courant
            // (TryAdvanceToId) : un lien inter-nœuds serait perdu en jeu.
            var d = BuildValidScene();
            d.Nodes[1].Dialogues.Add(new SceneDialogueLineData { LineId = "line_far", SpeakerId = "John" });
            d.Nodes[0].Dialogues[0].NextLineId = "line_far";
            Assert.IsNotEmpty(StorySceneValidator.Validate(d));
        }

        [Test]
        public void TalkToOtherNodeLine_Passes()
        {
            // « Parler à » résout global (balaye tous les nœuds) : cible
            // inter-nœuds légitime, cf. JsonStorySceneController.Talk.cs.
            var d = BuildValidScene();
            d.Nodes[1].Dialogues.Add(new SceneDialogueLineData { LineId = "line_far", SpeakerId = "John" });
            d.Actors[0].TalkEntries.Add(new SceneActorTalkEntry { SpeakerId = "John", TargetId = "line_far" });
            var errors = StorySceneValidator.Validate(d);
            Assert.IsEmpty(errors, string.Join("\n", errors));
        }
    }
}
#endif

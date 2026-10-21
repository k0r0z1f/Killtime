#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEngine;
using Killtime.Tactics.Visual;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Killtime.Tests
{
    /// <summary>
    /// RD-207 (amélioration) : regard tête par vrai constraint Rigging.
    /// Sans tête humanoïde : désactivé gracieusement, jamais de crash.
    /// </summary>
    [TestFixture]
    public class MiniatureLookRigTests
    {
        [Test]
        public void AttachTo_Is_Null_Safe_And_Idempotent()
        {
            Assert.IsNull(MiniatureLookRig.AttachTo(null));
            var go = new GameObject("RD207_Look");
            try
            {
                var first = MiniatureLookRig.AttachTo(go);
                Assert.IsNotNull(first);
                Assert.AreSame(first, MiniatureLookRig.AttachTo(go));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Without_Humanoid_Head_Disables_Gracefully()
        {
            var go = new GameObject("RD207_LookNoHead");
            try
            {
                go.AddComponent<Animator>();
                var rig = MiniatureLookRig.AttachTo(go);
                Assert.IsFalse(rig.enabled);
                var builderType = System.Type.GetType(
                    "UnityEngine.Animations.Rigging.RigBuilder, Unity.Animation.Rigging");
                Assert.IsTrue(builderType == null
                    || go.GetComponentInChildren(builderType) == null);
                Assert.DoesNotThrow(() => rig.SetAlive(false));
                Assert.DoesNotThrow(() => rig.SetAlive(true));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void AttachTo_Is_Unique_Per_Unit_Hierarchy()
        {
            var root = new GameObject("RD207_LookRoot");
            var child = new GameObject("RD207_LookChild");
            try
            {
                child.transform.SetParent(root.transform, false);
                var first = MiniatureLookRig.AttachTo(root);
                Assert.AreSame(first, MiniatureLookRig.AttachTo(child));
                Assert.AreEqual(1, root.GetComponentsInChildren<MiniatureLookRig>().Length);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Look_Weight_Stays_Usable()
        {
            Assert.GreaterOrEqual(MiniatureLookRig.BaseWeight, 0f);
            Assert.LessOrEqual(MiniatureLookRig.BaseWeight + MiniatureLookRig.JitterRange, 1f);
        }

        [Test]
        public void Double_Build_Creates_Nothing_Twice()
        {
            var go = new GameObject("RD207_LookDouble");
            try
            {
                go.AddComponent<Animator>();
                var rig = MiniatureLookRig.AttachTo(go);
                rig.EnsureBuilt();
                rig.EnsureBuilt();
                Assert.IsFalse(rig.enabled);
                Assert.AreEqual(0, go.transform.childCount);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void PickGazePoint_Tracks_Nearest_Visible_Enemy()
        {
            var self = Vector3.zero;
            var fallback = new Vector3(0f, 1.5f, 3f);
            var candidates = new System.Collections.Generic.List<MiniatureSeeds.GazeCandidate>
            {
                new MiniatureSeeds.GazeCandidate(new Vector3(0f, 1.5f, 10f), false, true, true),
                new MiniatureSeeds.GazeCandidate(new Vector3(0f, 1.5f, 2f), false, true, true),
                new MiniatureSeeds.GazeCandidate(new Vector3(0f, 1.5f, 1f), true, true, true),
            };
            Assert.AreEqual(new Vector3(0f, 1.5f, 2f),
                MiniatureSeeds.PickGazePoint(self, true, candidates, fallback));
        }

        [Test]
        public void PickGazePoint_Skips_Dead_Hidden_And_Allies()
        {
            var self = Vector3.zero;
            var fallback = new Vector3(0f, 1.5f, 3f);
            var candidates = new System.Collections.Generic.List<MiniatureSeeds.GazeCandidate>
            {
                new MiniatureSeeds.GazeCandidate(new Vector3(0f, 1.5f, 1f), true, true, true),
                new MiniatureSeeds.GazeCandidate(new Vector3(0f, 1.5f, 2f), false, false, true),
                new MiniatureSeeds.GazeCandidate(new Vector3(0f, 1.5f, 3f), false, true, false),
            };
            Assert.AreEqual(fallback,
                MiniatureSeeds.PickGazePoint(self, true, candidates, fallback));
        }

#if UNITY_EDITOR
        [Test]
        public void Nominal_Path_Builds_Rig_Layers_And_Constraint()
        {
            var root = HumanoidTestRig.Create();
            try
            {
                var animator = root.GetComponent<Animator>();
                Assert.IsNotNull(animator.GetBoneTransform(HumanBodyBones.Head));
                var rig = MiniatureLookRig.AttachTo(root);
                Assert.IsTrue(rig.enabled);
                Assert.IsTrue(rig.HasRigSetup);
                Assert.IsTrue(rig.HasLookTarget);
                Assert.AreEqual(1, rig.RigLayerCount);
                Assert.AreEqual(1, rig.AimSourceCount);
                Assert.GreaterOrEqual(rig.AimWeight, MiniatureLookRig.BaseWeight);
                Assert.LessOrEqual(rig.AimWeight,
                    MiniatureLookRig.BaseWeight + MiniatureLookRig.JitterRange);
                rig.SetAlive(false);
                Assert.AreEqual(0f, rig.RigWeight);
                rig.SetAlive(true);
                Assert.AreEqual(1f, rig.RigWeight);
                rig.EnsureBuilt();
                int lookCount = 0, rigCount = 0;
                foreach (Transform child in root.transform)
                {
                    if (child.name == "LookTarget") lookCount++;
                    if (child.name == "MiniatureRig") rigCount++;
                }
                Assert.AreEqual(1, lookCount);
                Assert.AreEqual(1, rigCount);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Builder_Sits_On_Animator_Object_When_Nested()
        {
            var root = new GameObject("RD207_Nested");
            var model = HumanoidTestRig.Create();
            try
            {
                model.transform.SetParent(root.transform, false);
                var rig = MiniatureLookRig.AttachTo(root);
                Assert.IsTrue(rig.enabled);
                Assert.IsTrue(rig.HasRigSetup);
                Assert.IsTrue(rig.BuilderOnAnimator);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>Squelette humanoïde minimal (AvatarBuilder) pour le chemin nominal.</summary>
        private static class HumanoidTestRig
        {
            private static readonly (string name, string parent, Vector3 pos)[] Bones =
            {
                ("Hips", null, new Vector3(0f, 1f, 0f)),
                ("Spine", "Hips", new Vector3(0f, 0.1f, 0f)),
                ("Chest", "Spine", new Vector3(0f, 0.15f, 0f)),
                ("Neck", "Chest", new Vector3(0f, 0.15f, 0f)),
                ("Head", "Neck", new Vector3(0f, 0.12f, 0f)),
                ("LeftShoulder", "Chest", new Vector3(0.08f, 0.1f, 0f)),
                ("LeftUpperArm", "LeftShoulder", new Vector3(0.1f, 0f, 0f)),
                ("LeftLowerArm", "LeftUpperArm", new Vector3(0.28f, 0f, 0f)),
                ("LeftHand", "LeftLowerArm", new Vector3(0.26f, 0f, 0f)),
                ("RightShoulder", "Chest", new Vector3(-0.08f, 0.1f, 0f)),
                ("RightUpperArm", "RightShoulder", new Vector3(-0.1f, 0f, 0f)),
                ("RightLowerArm", "RightUpperArm", new Vector3(-0.28f, 0f, 0f)),
                ("RightHand", "RightLowerArm", new Vector3(-0.26f, 0f, 0f)),
                ("LeftUpperLeg", "Hips", new Vector3(0.1f, -0.08f, 0f)),
                ("LeftLowerLeg", "LeftUpperLeg", new Vector3(0f, -0.45f, 0f)),
                ("LeftFoot", "LeftLowerLeg", new Vector3(0f, -0.45f, 0f)),
                ("LeftToes", "LeftFoot", new Vector3(0f, -0.02f, 0.12f)),
                ("RightUpperLeg", "Hips", new Vector3(-0.1f, -0.08f, 0f)),
                ("RightLowerLeg", "RightUpperLeg", new Vector3(0f, -0.45f, 0f)),
                ("RightFoot", "RightLowerLeg", new Vector3(0f, -0.45f, 0f)),
                ("RightToes", "RightFoot", new Vector3(0f, -0.02f, 0.12f)),
            };

            public static GameObject Create()
            {
                var root = new GameObject("RD207_Humanoid");
                var map = new System.Collections.Generic.Dictionary<string, Transform>();
                var skeleton = new SkeletonBone[Bones.Length];
                var human = new HumanBone[Bones.Length];
                for (int i = 0; i < Bones.Length; i++)
                {
                    var go = new GameObject(Bones[i].name);
                    Transform parent = Bones[i].parent == null
                        ? root.transform : map[Bones[i].parent];
                    go.transform.SetParent(parent, false);
                    go.transform.localPosition = Bones[i].pos;
                    map[Bones[i].name] = go.transform;
                    skeleton[i] = new SkeletonBone
                    {
                        name = Bones[i].name,
                        position = Bones[i].pos,
                        rotation = Quaternion.identity,
                        scale = Vector3.one,
                    };
                    human[i] = new HumanBone
                    {
                        boneName = Bones[i].name,
                        humanName = Bones[i].name,
                    };
                }
                var desc = new HumanDescription
                {
                    human = human,
                    skeleton = skeleton,
                    upperArmTwist = 0.5f,
                    lowerArmTwist = 0.5f,
                    upperLegTwist = 0.5f,
                    lowerLegTwist = 0.5f,
                    armStretch = 0.05f,
                    legStretch = 0.05f,
                    feetSpacing = 0f,
                    hasTranslationDoF = false,
                };
                Avatar avatar = AvatarBuilder.BuildHumanAvatar(root, desc);
                Assert.IsTrue(avatar.isValid);
                Assert.IsTrue(avatar.isHuman);
                var animator = root.AddComponent<Animator>();
                animator.avatar = avatar;
                return root;
            }
        }
#endif
    }
}
#endif

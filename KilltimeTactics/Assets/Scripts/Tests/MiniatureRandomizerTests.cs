#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEngine;
using Killtime.Tactics.Visual;

namespace Killtime.Tests
{
    /// <summary>
    /// RD-207 : fondation Animation Rigging + randomizer miniature.
    /// Pur + fumée EditMode (sans package Rigging : no-op gracieux).
    /// </summary>
    [TestFixture]
    public class MiniatureRandomizerTests
    {
        [Test]
        public void Speed_Stays_In_Range_085_115()
        {
            for (int seed = 1; seed <= 50; seed++)
            {
                float speed = MiniatureSeeds.ComputeSpeedForSeed(seed);
                Assert.GreaterOrEqual(speed, MiniatureSeeds.MinSpeed);
                Assert.LessOrEqual(speed, MiniatureSeeds.MaxSpeed);
            }
        }

        [Test]
        public void Speed_Is_Deterministic_Per_Seed()
        {
            Assert.AreEqual(
                MiniatureSeeds.ComputeSpeedForSeed(12345),
                MiniatureSeeds.ComputeSpeedForSeed(12345));
        }

        [Test]
        public void Two_Clones_Same_Asset_Differ()
        {
            bool anyDifference = false;
            for (int seed = 1; seed <= 20 && !anyDifference; seed++)
            {
                if (!Mathf.Approximately(
                        MiniatureSeeds.ComputeSpeedForSeed(seed),
                        MiniatureSeeds.ComputeSpeedForSeed(seed + 1000)))
                    anyDifference = true;
                if (!Mathf.Approximately(
                        MiniatureSeeds.ComputeJitterForSeed(seed, 0),
                        MiniatureSeeds.ComputeJitterForSeed(seed + 1000, 0)))
                    anyDifference = true;
            }
            Assert.IsTrue(anyDifference, "2 clones du même asset doivent différer.");
        }

        [Test]
        public void Scale_Stays_In_Range_097_103_And_Deterministic()
        {
            for (int seed = 1; seed <= 20; seed++)
            {
                float scale = MiniatureSeeds.ComputeScaleForSeed(seed);
                Assert.GreaterOrEqual(scale, MiniatureSeeds.MinScale);
                Assert.LessOrEqual(scale, MiniatureSeeds.MaxScale);
            }
            Assert.AreEqual(MiniatureSeeds.ComputeScaleForSeed(777),
                MiniatureSeeds.ComputeScaleForSeed(777));
        }

        [Test]
        public void Reapply_Does_Not_Stack_Pose()
        {
            var go = new GameObject("RD207_Reapply");
            try
            {
                var animator = go.AddComponent<Animator>();
                var randomizer = MiniatureRandomizer.AttachTo(go);
                randomizer.Apply(111);
                randomizer.Apply(222);
                var model = animator.transform;
                float expectedYaw = (MiniatureSeeds.ComputeJitterForSeed(222, 103) - 0.5f) * 8f;
                Quaternion expectedRot = Quaternion.Euler(0f, expectedYaw, 0f);
                Assert.AreEqual(expectedRot.x, model.localRotation.x, 1e-5f);
                Assert.AreEqual(expectedRot.y, model.localRotation.y, 1e-5f);
                Assert.AreEqual(expectedRot.z, model.localRotation.z, 1e-5f);
                Assert.AreEqual(MiniatureSeeds.ComputeScaleForSeed(222), model.localScale.x, 1e-5f);
                Assert.AreEqual(MiniatureSeeds.ComputeSpeedForSeed(222), animator.speed);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Default_Style_Is_Living()
        {
            Assert.AreEqual(MiniatureStyle.Living, MiniatureRandomizer.CurrentStyle);
        }

        [Test]
        public void AttachTo_Is_Idempotent_And_Safe_Without_Rigging()
        {
            var go = new GameObject("RD207_Test");
            try
            {
                go.AddComponent<Animator>();
                var first = MiniatureRandomizer.AttachTo(go);
                Assert.IsNotNull(first);
                Assert.AreSame(first, MiniatureRandomizer.AttachTo(go));
                Assert.IsNull(MiniatureRandomizer.AttachTo(null));
                float speed = go.GetComponent<Animator>().speed;
                Assert.GreaterOrEqual(speed, MiniatureSeeds.MinSpeed);
                Assert.LessOrEqual(speed, MiniatureSeeds.MaxSpeed);
                int seed = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(go);
                Assert.AreEqual(MiniatureSeeds.ComputeSpeedForSeed(seed), speed);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void AttachTo_Is_Unique_Per_Unit_Hierarchy()
        {
            var root = new GameObject("RD207_Root");
            var child = new GameObject("RD207_Child");
            try
            {
                child.transform.SetParent(root.transform, false);
                var first = MiniatureRandomizer.AttachTo(root);
                Assert.AreSame(first, MiniatureRandomizer.AttachTo(child));
                Assert.AreEqual(1, root.GetComponentsInChildren<MiniatureRandomizer>().Length);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
#endif

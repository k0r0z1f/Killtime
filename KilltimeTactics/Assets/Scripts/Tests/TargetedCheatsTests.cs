#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using NUnit.Framework;
using Killtime.Core.Chrono;
using Killtime.Core.Dice;

namespace Killtime.Tests
{
    /// <summary>
    /// RD-187 Socle cheats ciblés : rig dés dev-only (forcer critique / raté,
    /// restauré après) + purge chrono par branche. Pur Core, aucune règle modifiée.
    /// </summary>
    [TestFixture]
    public class TargetedCheatsTests
    {
        [TearDown]
        public void RestoreRng()
        {
            DiceRoller.ClearDevRig();
        }

        [Test]
        public void DevRig_OffByDefault()
        {
            Assert.IsFalse(DiceRoller.DevForceCritical);
            Assert.IsFalse(DiceRoller.DevForceFumble);
        }

        [Test]
        public void DevRig_ForceCrit_ReturnsMaxAndCritSuccess()
        {
            DiceRoller.DevForceCritical = true;
            var roller = new DiceRoller(123);
            var r6 = roller.Roll(DiceType.D6, 0, 10);
            Assert.AreEqual(6, r6.RawRoll);
            Assert.IsTrue(r6.IsCriticalSuccess);
            Assert.IsTrue(r6.IsSuccess);
            Assert.IsFalse(r6.IsCriticalFailure);

            var r20 = roller.Roll(DiceType.D20, 2, 10);
            Assert.AreEqual(20, r20.RawRoll);
            Assert.AreEqual(22, r20.Total);
            Assert.IsTrue(r20.IsCriticalSuccess);

            var r2d6 = roller.Roll(DiceType.TwoD6, 0, 10);
            Assert.AreEqual(12, r2d6.RawRoll);
            Assert.IsTrue(r2d6.IsCriticalSuccess);
        }

        [Test]
        public void DevRig_ForceFumble_ReturnsMinAndCritFail()
        {
            DiceRoller.DevForceFumble = true;
            var roller = new DiceRoller(456);
            var r6 = roller.Roll(DiceType.D6, 5, 4);
            Assert.AreEqual(1, r6.RawRoll);
            Assert.IsTrue(r6.IsCriticalFailure);
            Assert.IsFalse(r6.IsSuccess);

            var r2d12p10 = roller.Roll(DiceType.TwoD12Plus10, 0, 10);
            Assert.AreEqual(12, r2d12p10.RawRoll);
            Assert.IsTrue(r2d12p10.IsCriticalFailure);
        }

        [Test]
        public void DevRig_RollRaw_RespectsRig()
        {
            var roller = new DiceRoller(789);
            DiceRoller.DevForceCritical = true;
            Assert.AreEqual(8, roller.RollRaw(DiceType.D8));
            DiceRoller.DevForceCritical = false;
            DiceRoller.DevForceFumble = true;
            Assert.AreEqual(1, roller.RollRaw(DiceType.D8));
        }

        [Test]
        public void DevRig_Clear_RestoresNormalBehavior()
        {
            DiceRoller.DevForceCritical = true;
            DiceRoller.ClearDevRig();
            Assert.IsFalse(DiceRoller.DevForceCritical);
            Assert.IsFalse(DiceRoller.DevForceFumble);
            var roller = new DiceRoller(42);
            int raw = roller.RollRaw(DiceType.D6);
            Assert.GreaterOrEqual(raw, 1);
            Assert.LessOrEqual(raw, 6);
        }

        [Test]
        public void ChronoPurge_Clear_EmptiesBranchOnly()
        {
            var prime = new TimelineBranch(TimelineId.Timeline0_Prime);
            var alpha = new TimelineBranch(TimelineId.TimelineA_Alpha);
            prime.PushSnapshot(new TacticalTimeSnapshot(1, 0f, "A"));
            prime.PushSnapshot(new TacticalTimeSnapshot(2, 0f, "B"));
            alpha.PushSnapshot(new TacticalTimeSnapshot(1, 0f, "Alpha-A"));

            prime.Clear();

            Assert.AreEqual(0, prime.Count);
            Assert.IsNull(prime.GetLatestSnapshot());
            Assert.AreEqual(1, alpha.Count);
            Assert.AreEqual("Alpha-A", alpha.GetLatestSnapshot().ActionDescription);
        }
    }
}
#endif

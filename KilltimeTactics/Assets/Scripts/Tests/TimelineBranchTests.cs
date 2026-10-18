#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using NUnit.Framework;
using Killtime.Core.Chrono;

namespace Killtime.Tests
{
    /// <summary>
    /// RD-070 Fleuve 3D : snapshots rembobinables + lignes causales 0/A/B/C/D.
    /// Pur Core (aucune dépendance Unity runtime) : push / rewind ciblé / branches.
    /// </summary>
    [TestFixture]
    public class TimelineBranchTests
    {
        private static TacticalTimeSnapshot Snap(int round, string desc)
        {
            return new TacticalTimeSnapshot(round, 0f, desc);
        }

        [Test]
        public void Push_CountAndLatest()
        {
            var branch = new TimelineBranch(TimelineId.Timeline0_Prime);
            Assert.AreEqual(0, branch.Count);
            Assert.IsNull(branch.GetLatestSnapshot());
            branch.PushSnapshot(Snap(1, "Ouverture"));
            branch.PushSnapshot(Snap(2, "Tir"));
            Assert.AreEqual(2, branch.Count);
            Assert.AreEqual("Tir", branch.GetLatestSnapshot().ActionDescription);
        }

        [Test]
        public void RewindTo_TruncatesToIndex()
        {
            var branch = new TimelineBranch(TimelineId.Timeline0_Prime);
            branch.PushSnapshot(Snap(1, "A"));
            branch.PushSnapshot(Snap(2, "B"));
            branch.PushSnapshot(Snap(3, "C"));
            var restored = branch.RewindTo(0);
            Assert.IsNotNull(restored);
            Assert.AreEqual("A", restored.ActionDescription);
            Assert.AreEqual(1, branch.Count);
            Assert.AreEqual("A", branch.GetLatestSnapshot().ActionDescription);
        }

        [Test]
        public void RewindTo_InvalidIndex_ReturnsNullAndKeepsHistory()
        {
            var branch = new TimelineBranch(TimelineId.Timeline0_Prime);
            branch.PushSnapshot(Snap(1, "A"));
            Assert.IsNull(branch.RewindTo(-1));
            Assert.IsNull(branch.RewindTo(5));
            Assert.AreEqual(1, branch.Count);
        }

        [Test]
        public void Branches_AreIsolatedById()
        {
            var prime = new TimelineBranch(TimelineId.Timeline0_Prime);
            var alpha = new TimelineBranch(TimelineId.TimelineA_Alpha);
            prime.PushSnapshot(Snap(1, "Prime-A"));
            Assert.AreEqual(1, prime.Count);
            Assert.AreEqual(0, alpha.Count);
            Assert.IsNull(alpha.GetLatestSnapshot());
            alpha.PushSnapshot(Snap(1, "Alpha-A"));
            Assert.AreEqual("Prime-A", prime.GetLatestSnapshot().ActionDescription);
            Assert.AreEqual("Alpha-A", alpha.GetLatestSnapshot().ActionDescription);
        }

        [Test]
        public void RewindLast_PreservesFirstSnapshot()
        {
            var branch = new TimelineBranch(TimelineId.Timeline0_Prime);
            branch.PushSnapshot(Snap(1, "A"));
            var same = branch.RewindLastAction();
            Assert.IsNotNull(same);
            Assert.AreEqual("A", same.ActionDescription);
            Assert.AreEqual(1, branch.Count);
        }
    }
}
#endif

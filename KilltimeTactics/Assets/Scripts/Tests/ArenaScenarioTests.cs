#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using NUnit.Framework;
using Killtime.Tactics;
using Killtime.Core.Combat;

namespace Killtime.Tests
{
    /// <summary>
    /// RD-198 : scénarios terrain 1-clic + carnet d'écarts — helpers purs,
    /// sans scène. Aucune règle modifiée.
    /// </summary>
    [TestFixture]
    public class ArenaScenarioTests
    {
        [Test]
        public void PostureFromIndex_CoversAllFour()
        {
            Assert.AreEqual(OpportunityReactionType.Strike, CombatDevArena.ScenarioPostureFromIndex(0));
            Assert.AreEqual(OpportunityReactionType.Follow, CombatDevArena.ScenarioPostureFromIndex(1));
            Assert.AreEqual(OpportunityReactionType.Block, CombatDevArena.ScenarioPostureFromIndex(2));
            Assert.AreEqual(OpportunityReactionType.Trip, CombatDevArena.ScenarioPostureFromIndex(3));
            Assert.AreEqual(OpportunityReactionType.Strike, CombatDevArena.ScenarioPostureFromIndex(99));
        }

        [Test]
        public void PostureLabel_MatchesCoreStanceLabel()
        {
            for (int i = 0; i < 4; i++)
                Assert.AreEqual(
                    OpportunityState.StanceLabel(CombatDevArena.ScenarioPostureFromIndex(i)),
                    CombatDevArena.ScenarioPostureLabel(i));
        }

        [Test]
        public void ChargeSetup_RequiresThreePlus()
        {
            Assert.IsFalse(CombatDevArena.IsChargeSetupDistance(0));
            Assert.IsFalse(CombatDevArena.IsChargeSetupDistance(2));
            Assert.IsTrue(CombatDevArena.IsChargeSetupDistance(3));
            Assert.IsTrue(CombatDevArena.IsChargeSetupDistance(6));
        }

        [Test]
        public void GapReport_ContainsAllCoveredTasks()
        {
            string report = CombatDevArena.BuildGapReport(new[] { "🎯 tir test" });
            StringAssert.Contains("RD-171", report);
            StringAssert.Contains("RD-174", report);
            StringAssert.Contains("RD-175", report);
            StringAssert.Contains("RD-184", report);
            StringAssert.Contains("RD-185", report);
            StringAssert.Contains("RD-186", report);
            StringAssert.Contains("🎯 tir test", report);
        }

        [Test]
        public void GapReport_EmptyLogs_NotesIt()
        {
            string report = CombatDevArena.BuildGapReport(null);
            StringAssert.Contains("RD-171", report);
        }

        [Test]
        public void Checklists_MentionKeyItems()
        {
            StringAssert.Contains("Maj+F9", CombatDevArena.RadialChecklistText());
            StringAssert.Contains("6 beats", CombatDevArena.DemoChecklistText());
            StringAssert.Contains("watermark", CombatDevArena.DemoChecklistText().ToLower());
        }
    }
}
#endif

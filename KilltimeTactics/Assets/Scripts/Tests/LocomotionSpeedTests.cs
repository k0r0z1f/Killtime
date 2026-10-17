#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using NUnit.Framework;
using Killtime.Tactics.Units;

namespace Killtime.Tests
{
    /// <summary>
    /// RD-025 : paliers de locomotion pilotés par le paramètre Animator "Speed" (m/s).
    /// Base MoveSpeed 4.0 = Walk, Élan 5.5/6.2 = Run, Élan 7.0/8.5 = Sprint.
    /// </summary>
    [TestFixture]
    public class LocomotionSpeedTests
    {
        [TestCase(-1f, LocomotionBand.Idle)]
        [TestCase(0f, LocomotionBand.Idle)]
        [TestCase(0.05f, LocomotionBand.Idle)]
        [TestCase(0.1f, LocomotionBand.Walk)]
        [TestCase(1.35f, LocomotionBand.Walk)]
        [TestCase(4f, LocomotionBand.Walk)]
        [TestCase(4.99f, LocomotionBand.Walk)]
        [TestCase(5f, LocomotionBand.Run)]
        [TestCase(6.2f, LocomotionBand.Run)]
        [TestCase(6.99f, LocomotionBand.Run)]
        [TestCase(7f, LocomotionBand.Sprint)]
        [TestCase(8.5f, LocomotionBand.Sprint)]
        [TestCase(20f, LocomotionBand.Sprint)]
        public void Band_Matches_Spec_Thresholds(float speedMps, LocomotionBand expected)
        {
            Assert.AreEqual(expected, TacticalUnitVisual.GetLocomotionBand(speedMps));
        }

        [Test]
        public void Thresholds_Match_MoveSpeed_Specs()
        {
            Assert.AreEqual(LocomotionBand.Walk, TacticalUnitVisual.GetLocomotionBand(4.0f));
            Assert.AreEqual(LocomotionBand.Run, TacticalUnitVisual.GetLocomotionBand(5.5f));
            Assert.AreEqual(LocomotionBand.Run, TacticalUnitVisual.GetLocomotionBand(6.2f));
            Assert.AreEqual(LocomotionBand.Sprint, TacticalUnitVisual.GetLocomotionBand(7.0f));
            Assert.AreEqual(LocomotionBand.Sprint, TacticalUnitVisual.GetLocomotionBand(8.5f));
        }
    }
}
#endif

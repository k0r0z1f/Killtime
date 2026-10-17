#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using NUnit.Framework;
using System.Collections.Generic;
using Killtime.Core.Combat;
using Killtime.Tactics.Grid;

namespace Killtime.Tactics.AI.Tests
{
    /// <summary>
    /// RD-105 : non-régression du Duo-Tech automatique IA (Sillage Igné).
    /// Pur EditMode : seul AIDuoTechSolver est exercé (aucune scène/arène).
    /// Ligne de test : Mina (0,0), ennemis sur l'axe +Q, Lucas en appui.
    /// </summary>
    [TestFixture]
    public class AIDuoTechSolverTests
    {
        private static List<HexCoordinates> Enemies(params (int q, int r)[] cells)
        {
            var r = new List<HexCoordinates>(cells.Length);
            for (int i = 0; i < cells.Length; i++)
                r.Add(new HexCoordinates(cells[i].q, cells[i].r));
            return r;
        }

        [Test]
        public void AlignedTwoEnemies_LucasAtRange_PlansPerfect()
        {
            var foes = Enemies((2, 0), (4, 0));
            bool ok = AIDuoTechSolver.TryPlanSillage(
                new HexCoordinates(0, 0), new HexCoordinates(1, 2), foes, out var plan);
            Assert.IsTrue(ok);
            Assert.AreEqual(2, plan.TargetIndexes.Count);
            Assert.AreEqual(WeaveSyncGrade.Perfect, plan.Grade);
            Assert.AreEqual(new HexCoordinates(4, 0), plan.DashDest);
        }

        [Test]
        public void ScatteredEnemies_NoPlan()
        {
            var foes = Enemies((2, 0), (0, 2));
            bool ok = AIDuoTechSolver.TryPlanSillage(
                new HexCoordinates(0, 0), new HexCoordinates(1, 2), foes, out _);
            Assert.IsFalse(ok);
        }

        [Test]
        public void SingleEnemy_NoPlan()
        {
            var foes = Enemies((2, 0));
            bool ok = AIDuoTechSolver.TryPlanSillage(
                new HexCoordinates(0, 0), new HexCoordinates(1, 2), foes, out _);
            Assert.IsFalse(ok);
        }

        [Test]
        public void LucasOnDashLine_RiskyGrade()
        {
            var foes = Enemies((2, 0), (4, 0));
            bool ok = AIDuoTechSolver.TryPlanSillage(
                new HexCoordinates(0, 0), new HexCoordinates(1, 0), foes, out var plan);
            Assert.IsTrue(ok);
            Assert.AreEqual(WeaveSyncGrade.Risky, plan.Grade);
        }

        [Test]
        public void LucasFarFromLine_LateGrade()
        {
            var foes = Enemies((2, 0), (4, 0));
            bool ok = AIDuoTechSolver.TryPlanSillage(
                new HexCoordinates(0, 0), new HexCoordinates(0, 6), foes, out var plan);
            Assert.IsTrue(ok);
            Assert.AreEqual(WeaveSyncGrade.Late, plan.Grade);
        }

        [Test]
        public void FiveAlignedEnemies_CappedAtFourNearest()
        {
            var foes = Enemies((1, 0), (2, 0), (3, 0), (4, 0), (5, 0));
            bool ok = AIDuoTechSolver.TryPlanSillage(
                new HexCoordinates(0, 0), new HexCoordinates(1, 2), foes, out var plan);
            Assert.IsTrue(ok);
            Assert.AreEqual(4, plan.TargetIndexes.Count);
            Assert.AreEqual(new HexCoordinates(4, 0), plan.DashDest);
        }

        [Test]
        public void ShouldExecute_RiskyOnlyWhenAllIn()
        {
            Assert.IsTrue(AIDuoTechSolver.ShouldExecuteSillage(WeaveSyncGrade.Perfect, false));
            Assert.IsTrue(AIDuoTechSolver.ShouldExecuteSillage(WeaveSyncGrade.Late, false));
            Assert.IsFalse(AIDuoTechSolver.ShouldExecuteSillage(WeaveSyncGrade.Risky, false));
            Assert.IsTrue(AIDuoTechSolver.ShouldExecuteSillage(WeaveSyncGrade.Risky, true));
        }
    }
}
#endif

#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using NUnit.Framework;
using Killtime.Tactics;
using Killtime.Tactics.CombatUI;
using Killtime.Core.Combat;

namespace Killtime.Tests
{
    /// <summary>
    /// RD-194 : Duo-Tech MJ sans souris — mappings toolbar/arène (purs,
    /// sans scène) + couverture des 12 Duo supportés. Aucune règle modifiée.
    /// </summary>
    [TestFixture]
    public class DuoTechArenaTests
    {
        [Test]
        public void AllTwelveDefs_AreSupportedByArena()
        {
            var defs = DuoTechRegistry.GetAllDefs();
            Assert.AreEqual(12, defs.Count);
            for (int i = 0; i < defs.Count; i++)
                Assert.IsTrue(CombatDevArena.IsDuoSupported(defs[i].Id), defs[i].Id.ToString());
        }

        [Test]
        public void TrioAndThomasPairs_Classified()
        {
            Assert.IsTrue(CombatDevArena.IsTrioDuo(DuoTechId.TriFusion));
            Assert.IsFalse(CombatDevArena.IsTrioDuo(DuoTechId.SillageIgne));
            Assert.IsTrue(CombatDevArena.IsThomasPairDuo(DuoTechId.RempartTellurique));
            Assert.IsTrue(CombatDevArena.IsThomasPairDuo(DuoTechId.ZeroTectonique));
            Assert.IsFalse(CombatDevArena.IsThomasPairDuo(DuoTechId.Parallaxe));
        }

        [Test]
        public void OutcomeFromIndex_MapsPerfectPartialMiss()
        {
            Assert.AreEqual(DuoTechOutcome.Perfect, CombatDevArena.DuoTechOutcomeFromIndex(0));
            Assert.AreEqual(DuoTechOutcome.Partial, CombatDevArena.DuoTechOutcomeFromIndex(1));
            Assert.AreEqual(DuoTechOutcome.Miss, CombatDevArena.DuoTechOutcomeFromIndex(2));
            Assert.AreEqual(DuoTechOutcome.Miss, CombatDevArena.DuoTechOutcomeFromIndex(99));
            Assert.AreEqual("Parfaite", CombatDevArena.DuoTechOutcomeLabel(DuoTechOutcome.Perfect));
            Assert.AreEqual("Semi", CombatDevArena.DuoTechOutcomeLabel(DuoTechOutcome.Partial));
            Assert.AreEqual("Ratée", CombatDevArena.DuoTechOutcomeLabel(DuoTechOutcome.Miss));
        }

        [Test]
        public void SillageGradeFromIndex_CoversAllFourGrades()
        {
            Assert.AreEqual(WeaveSyncGrade.Perfect, CombatDevArena.SillageGradeFromIndex(0));
            Assert.AreEqual(WeaveSyncGrade.Risky, CombatDevArena.SillageGradeFromIndex(1));
            Assert.AreEqual(WeaveSyncGrade.Late, CombatDevArena.SillageGradeFromIndex(2));
            Assert.AreEqual(WeaveSyncGrade.Miss, CombatDevArena.SillageGradeFromIndex(3));
            StringAssert.Contains("Risky", CombatDevArena.SillageGradeLabel(WeaveSyncGrade.Risky));
            StringAssert.Contains("Perfect", CombatDevArena.SillageGradeLabel(WeaveSyncGrade.Perfect));
            StringAssert.Contains("Late", CombatDevArena.SillageGradeLabel(WeaveSyncGrade.Late));
        }

        [Test]
        public void SillageGradeFromOutcome_PartialMapsToRisky()
        {
            Assert.AreEqual(WeaveSyncGrade.Perfect, CombatDevArena.SillageGradeFromOutcome(DuoTechOutcome.Perfect));
            Assert.AreEqual(WeaveSyncGrade.Risky, CombatDevArena.SillageGradeFromOutcome(DuoTechOutcome.Partial));
            Assert.AreEqual(WeaveSyncGrade.Miss, CombatDevArena.SillageGradeFromOutcome(DuoTechOutcome.Miss));
        }

        [Test]
        public void CostPreview_ShowsTotalAndRoles()
        {
            var def = DuoTechRegistry.GetDef(DuoTechId.SillageIgne);
            string preview = CombatDevArena.DuoTechCostPreview(def);
            StringAssert.Contains("8 PA", preview);
            Assert.IsNotNull(CombatDevArena.DuoTechCostPreview(null));
        }

        [Test]
        public void TableOutcome_MatchesToolbarSelector()
        {
            Assert.AreEqual(DuoTechOutcome.Perfect,
                DuoTechRegistry.TableOutcome(true, true));
            Assert.AreEqual(CombatDevArena.DuoTechOutcomeFromIndex(1),
                DuoTechRegistry.TableOutcome(true, false));
            Assert.AreEqual(CombatDevArena.DuoTechOutcomeFromIndex(2),
                DuoTechRegistry.TableOutcome(false, false));
        }
    }
}
#endif

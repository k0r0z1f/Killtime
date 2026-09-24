#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using NUnit.Framework;
using System.Collections.Generic;
using Killtime.Core.Character;
using Killtime.Core.Combat;
using Killtime.Tactics.CombatUI;

namespace Killtime.Tests
{
    [TestFixture]
    public class DuoTechTests
    {
        private static CharacterStats MakeLucas()
        {
            // Fiche héroïque : PA = max(3,5)+4+2 = 11.
            var attr = new Attributes(@for: 2, agi: 3, con: 3, rap: 4, @int: 5, eru: 3, cha: 3, ins: 3, mag: 5);
            return new CharacterStats("Lucas", attr);
        }

        private static CharacterStats MakeMina()
        {
            // PA = max(5,3)+4+2 = 11.
            var attr = new Attributes(@for: 3, agi: 5, con: 3, rap: 4, @int: 3, eru: 2, cha: 3, ins: 3, mag: 5);
            return new CharacterStats("Mina", attr);
        }

        private static CharacterStats MakeSbire(string name = "Sbire")
        {
            var attr = new Attributes(@for: 2, agi: 2, con: 2, rap: 2, @int: 2, eru: 2, cha: 2, ins: 2);
            return new CharacterStats(name, attr);
        }

        // ==================== IDENTITÉ ====================

        [Test]
        public void TestDuo_RequiresLucasPlusMina()
        {
            var lucas = MakeLucas();
            var mina = MakeMina();
            var sbire = MakeSbire();

            Assert.IsTrue(DuoTechRegistry.AreComplementary(lucas, mina));
            Assert.IsFalse(DuoTechRegistry.AreComplementary(lucas, lucas));
            Assert.IsFalse(DuoTechRegistry.AreComplementary(lucas, sbire));
            Assert.IsFalse(DuoTechRegistry.AreComplementary(mina, mina));
        }

        // ==================== PAIEMENT IMMÉDIAT ====================

        [Test]
        public void TestDuo_SillagePayment_DebitsBothImmediately()
        {
            var lucas = MakeLucas(); // 11 PA
            var mina = MakeMina();   // 11 PA
            var def = DuoTechRegistry.GetDef(DuoTechId.SillageIgne); // 1 + 3 + 4 = 8

            Assert.IsTrue(DuoTechRegistry.TryDeclarePayment(lucas, mina, def, 1));
            Assert.AreEqual(11 - (1 + 4), lucas.CurrentActionPoints);
            Assert.AreEqual(11 - 3, mina.CurrentActionPoints);
        }

        [Test]
        public void TestDuo_PartnerWithoutPA_RefusesAndRefundsInitiator()
        {
            var lucas = MakeLucas();
            var mina = MakeMina();
            mina.CurrentActionPoints = 1; // < 3 requis pour Sillage
            var def = DuoTechRegistry.GetDef(DuoTechId.SillageIgne);

            Assert.IsFalse(DuoTechRegistry.TryDeclarePayment(lucas, mina, def, 1));
            Assert.AreEqual(11, lucas.CurrentActionPoints); // remboursé
            Assert.AreEqual(1, mina.CurrentActionPoints);   // intact
        }

        [Test]
        public void TestDuo_PaymentDoesNotConsumeMainAction()
        {
            var lucas = MakeLucas();
            var mina = MakeMina();
            var def = DuoTechRegistry.GetDef(DuoTechId.LacetNytharite);

            int attacksBefore = lucas.AttacksThisTurn;
            Assert.IsTrue(DuoTechRegistry.TryDeclarePayment(mina, lucas, def, 1));
            Assert.AreEqual(attacksBefore, lucas.AttacksThisTurn); // action unique : pas de RegisterAttack
        }

        // ==================== LIMITE 1 DUO / ROUND ====================

        [Test]
        public void TestDuo_SecondDuoSameRound_RefusedWithoutPayment()
        {
            var lucas = MakeLucas();
            var mina = MakeMina();
            var sillage = DuoTechRegistry.GetDef(DuoTechId.SillageIgne);
            var lacet = DuoTechRegistry.GetDef(DuoTechId.LacetNytharite);

            Assert.IsTrue(DuoTechRegistry.TryDeclarePayment(lucas, mina, sillage, 1));
            int lucasPA = lucas.CurrentActionPoints;
            int minaPA = mina.CurrentActionPoints;

            // Même initiateur, autre duo, même round : refusé, PA intacts.
            Assert.IsFalse(DuoTechRegistry.TryDeclarePayment(lucas, mina, lacet, 1));
            Assert.AreEqual(lucasPA, lucas.CurrentActionPoints);
            Assert.AreEqual(minaPA, mina.CurrentActionPoints);

            // L'autre moitié comme initiatrice : refusé aussi (limite par participant).
            Assert.IsFalse(DuoTechRegistry.TryDeclarePayment(mina, lucas, lacet, 1));
        }

        [Test]
        public void TestDuo_NextRound_AllowedAgain()
        {
            var lucas = MakeLucas();
            var mina = MakeMina();
            var sillage = DuoTechRegistry.GetDef(DuoTechId.SillageIgne);
            var lacet = DuoTechRegistry.GetDef(DuoTechId.LacetNytharite);

            Assert.IsTrue(DuoTechRegistry.TryDeclarePayment(lucas, mina, sillage, 1));
            lucas.ResetTurn();
            mina.ResetTurn();
            Assert.IsTrue(DuoTechRegistry.TryDeclarePayment(mina, lucas, lacet, 2));
        }

        [Test]
        public void TestDuo_ExplorationRoundZero_NoLimit()
        {
            var lucas = MakeLucas();
            var mina = MakeMina();
            var lacet = DuoTechRegistry.GetDef(DuoTechId.LacetNytharite);

            Assert.IsTrue(DuoTechRegistry.TryDeclarePayment(lucas, mina, lacet, 0));
            lucas.ResetTurn();
            mina.ResetTurn();
            Assert.IsTrue(DuoTechRegistry.TryDeclarePayment(lucas, mina, lacet, 0));
        }

        // ==================== GÉOMÉTRIE ====================

        [Test]
        public void TestDuo_AlignedEnemies_Validated()
        {
            var a = new WeaveVec2(0f, 0f);
            var b = new WeaveVec2(6f, 0f);
            var pts = new List<WeaveVec2>
            {
                new WeaveVec2(1f, 0.1f),
                new WeaveVec2(3f, -0.1f),
                new WeaveVec2(5f, 0.05f)
            };
            Assert.IsTrue(DuoTechGeometry.AreAligned(a, b, pts, 0.6f));
        }

        [Test]
        public void TestDuo_ScatteredEnemies_Rejected()
        {
            var a = new WeaveVec2(0f, 0f);
            var b = new WeaveVec2(6f, 0f);
            var pts = new List<WeaveVec2>
            {
                new WeaveVec2(1f, 0.1f),
                new WeaveVec2(3f, 2.5f),
                new WeaveVec2(5f, 0.05f)
            };
            Assert.IsFalse(DuoTechGeometry.AreAligned(a, b, pts, 0.6f));
        }

        [Test]
        public void TestDuo_LoopClosure_Detected()
        {
            var loop = new List<WeaveVec2>
            {
                new WeaveVec2(0f, 0f), new WeaveVec2(2f, 0f),
                new WeaveVec2(2f, 2f), new WeaveVec2(0f, 2f),
                new WeaveVec2(0.1f, 0.1f)
            };
            Assert.IsTrue(DuoTechGeometry.IsLoopClosed(loop));

            var open = new List<WeaveVec2>
            {
                new WeaveVec2(0f, 0f), new WeaveVec2(3f, 0f), new WeaveVec2(6f, 0f)
            };
            Assert.IsFalse(DuoTechGeometry.IsLoopClosed(open));
        }

        [Test]
        public void TestDuo_SyncGrade_Boundaries()
        {
            // waveTime = arcT1 / minaSpeed ; lucasTime = arcT2 / lucasSpeed.
            // Perfect : wave 1.0s, lucas 0.7s -> lead 0.3s.
            Assert.AreEqual(WeaveSyncGrade.Perfect, DuoTechGeometry.GradeSync(4f, 8.4f, 4f, 12f));
            // Risky : wave 1.0s, lucas 0.95s -> lead 0.05s.
            Assert.AreEqual(WeaveSyncGrade.Risky, DuoTechGeometry.GradeSync(4f, 11.4f, 4f, 12f));
            // Late : wave 2.0s, lucas 0.5s -> lead 1.5s.
            Assert.AreEqual(WeaveSyncGrade.Late, DuoTechGeometry.GradeSync(8f, 6f, 4f, 12f));
        }

        [Test]
        public void TestDuo_SillagePerfect_AppliesDashPlusFireAndBurn()
        {
            var lucas = MakeLucas();
            var mina = MakeMina();
            var e1 = MakeSbire("E1");
            var e2 = MakeSbire("E2");
            int hp1 = e1.CurrentHealth, hp2 = e2.CurrentHealth;

            var logs = new List<string>();
            DuoTechRegistry.ResolveSillage(mina, lucas,
                new List<CharacterStats> { e1, e2 }, WeaveSyncGrade.Perfect, logs.Add);

            Assert.AreEqual(hp1 - 7, e1.CurrentHealth); // 4 dash + 3 feu
            Assert.AreEqual(hp2 - 7, e2.CurrentHealth);
            Assert.IsTrue(e1.ActiveStatus.HasFlag(StatusEffect.EnFeu));
        }

        [Test]
        public void TestDuo_SillageRisky_BurnsMina()
        {
            var lucas = MakeLucas();
            var mina = MakeMina();
            int minaHp = mina.CurrentHealth;
            var e1 = MakeSbire("E1");

            var logs = new List<string>();
            DuoTechRegistry.ResolveSillage(mina, lucas,
                new List<CharacterStats> { e1 }, WeaveSyncGrade.Risky, logs.Add);

            Assert.AreEqual(minaHp - 3, mina.CurrentHealth); // friendly fire
        }

        [Test]
        public void TestDuo_Lacet_ImmobilisesOnlyIfClosed()
        {
            var target = MakeSbire("Cible");
            var logs = new List<string>();

            DuoTechRegistry.ResolveLacet(target, true, logs.Add);
            Assert.IsTrue(target.ActiveStatus.HasFlag(StatusEffect.Immobilise));

            var target2 = MakeSbire("Cible2");
            DuoTechRegistry.ResolveLacet(target2, false, logs.Add);
            Assert.IsFalse(target2.ActiveStatus.HasFlag(StatusEffect.Immobilise));
        }

        [Test]
        public void TestDuo_Fournaise_StunsAndDrainsPA()
        {
            var target = MakeSbire("Isole");
            target.ResetTurn();
            int pa = target.CurrentActionPoints;
            var logs = new List<string>();

            DuoTechRegistry.ResolveFournaise(target, true, logs.Add);
            Assert.IsTrue(target.ActiveStatus.HasFlag(StatusEffect.Etourdi));
            Assert.AreEqual(pa - 2, target.CurrentActionPoints);
        }
    }
}
#endif

#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using NUnit.Framework;
using System.Collections.Generic;
using Killtime.Core.Character;
using Killtime.Core.Combat;
using Killtime.Core.Dice;

namespace Killtime.Tests
{
    /// <summary>
    /// RD-084 : Attaques combinées de groupe (Livre VI §25.2) & Poursuite (§25.1).
    /// Synchro sur le plus lent + sommation vs défense unique (armure 1×).
    /// Concours Athlétisme proie vs poursuivants : maintien à 1 case, +1 faillite
    /// par échec, capture immédiate aux 4 faillites cumulées.
    /// </summary>
    [TestFixture]
    public class GroupAssaultPursuitTests
    {
        private static CharacterStats MakeStats(string name, int force = 5, int agi = 4, int con = 5, int ap = 6, int hp = 100)
        {
            var attr = new Attributes(force, agi, con, 3, 3, 2, 2, 2);
            var sheet = new CharacterSheet
            {
                Name = name,
                BaseAttributes = attr,
                UnlockedSpecializations = new System.Collections.Generic.List<string>()
            };
            var stats = sheet.ToCombatStats();
            stats.Sheet = sheet;
            stats.CurrentActionPoints = ap;
            stats.CurrentHealth = hp;
            return stats;
        }

        [SetUp]
        public void SetUp()
        {
            GroupAssaultState.ClearAll();
            PursuitState.ClearAll();
        }

        [TearDown]
        public void TearDown()
        {
            GroupAssaultState.ClearAll();
            PursuitState.ClearAll();
        }

        [Test]
        public void GroupAssault_SlowestInitiative_ReturnsMinimum()
        {
            var a = MakeStats("Rapide"); a.InitiativeRollTotal = 9;
            var b = MakeStats("Lourd"); b.InitiativeRollTotal = 2;
            var c = MakeStats("Moyen"); c.InitiativeRollTotal = 5;

            var members = new List<CharacterStats> { a, b, c };
            Assert.AreEqual(2, GroupAssaultState.GetSlowestInitiative(members));
            Assert.AreEqual(b, GroupAssaultState.GetSlowestMember(members));
        }

        [Test]
        public void GroupAssault_ValidateGroup_RequiresTwoMembers()
        {
            var solo = new List<CharacterStats> { MakeStats("Solo") };
            Assert.IsFalse(GroupAssaultState.TryValidateGroup(solo, 2, 2, out string error));
            StringAssert.Contains("minimum", error);

            var duo = new List<CharacterStats> { MakeStats("A"), MakeStats("B") };
            Assert.IsTrue(GroupAssaultState.TryValidateGroup(duo, 2, 2, out _));
        }

        [Test]
        public void CombinedAttack_SummationFacesUniqueDefense()
        {
            // Ordre des jets : attaquant A (8), attaquant B (7), défense unique (3).
            var calc = new CombatCalculator(new QueueDiceRoller(8, 7, 3));
            var a = MakeStats("A");
            var b = MakeStats("B");
            var target = MakeStats("Cible");

            var res = calc.ResolveCombinedAttack(
                new List<CharacterStats> { a, b }, target, BodyPart.Torse,
                SkillType.MainsNues, SkillType.Esquive);

            Assert.IsTrue(res.IsValid);
            Assert.IsTrue(res.IsHit);
            Assert.AreEqual(15, res.SummedAttackTotal);
            Assert.AreEqual(3, res.UniqueDefenseTotal);
            Assert.AreEqual(12, res.Differential);
            // 5+5 armes + 12 dépassement = 22 bruts, armure 0 → 22 nets.
            Assert.AreEqual(22, res.FinalDamageApplied);
            Assert.AreEqual(78, target.CurrentHealth);
            StringAssert.Contains("SOMMATION", res.CombatLog);
        }

        [Test]
        public void CombinedAttack_BlockedWhenDefenseHolds()
        {
            var calc = new CombatCalculator(new QueueDiceRoller(2, 2, 12));
            var a = MakeStats("A");
            var b = MakeStats("B");
            var target = MakeStats("Cible");
            int hpBefore = target.CurrentHealth;

            var res = calc.ResolveCombinedAttack(
                new List<CharacterStats> { a, b }, target, BodyPart.Torse,
                SkillType.MainsNues, SkillType.Esquive);

            Assert.IsFalse(res.IsHit);
            Assert.AreEqual(hpBefore, target.CurrentHealth);
            StringAssert.Contains("PARÉ", res.CombatLog);
        }

        [Test]
        public void CombinedAttack_ArmorAppliedOnce_Pulverizes()
        {
            // Cible blindée (8 armure) : deux frappes séparées rebondiraient (7-8 ≤ 0),
            // la sommation traverse (10+8=18 bruts − 8 = 10 nets).
            var calc = new CombatCalculator(new QueueDiceRoller(6, 6, 4));
            var a = MakeStats("A");
            var b = MakeStats("B");
            var target = MakeStats("Cible");
            target.BaseArmorAbsorption = 8;

            var res = calc.ResolveCombinedAttack(
                new List<CharacterStats> { a, b }, target, BodyPart.Torse,
                SkillType.MainsNues, SkillType.Esquive);

            Assert.IsTrue(res.IsHit);
            Assert.AreEqual(12, res.SummedAttackTotal);
            Assert.AreEqual(8, res.Differential);
            Assert.AreEqual(18, res.RawDamage);
            Assert.AreEqual(8, res.ArmorAbsorbed);
            Assert.AreEqual(10, res.FinalDamageApplied);
        }

        [Test]
        public void Pursuit_SuccessMaintainsDistance()
        {
            var calc = new CombatCalculator(new QueueDiceRoller(10, 3));
            var prey = MakeStats("Proie");
            var hunter = MakeStats("Chasseur");

            var round = calc.ResolvePursuitRound(prey, new List<CharacterStats> { hunter });

            Assert.AreEqual(1, round.Entries.Count);
            Assert.IsTrue(round.Entries[0].PreyWins);
            Assert.AreEqual(0, round.FailuresAdded);
            Assert.AreEqual(0, round.TotalFailures);
            Assert.IsFalse(round.Caught);
            StringAssert.Contains("DISTANCE MAINTENUE", round.CombatLog);
        }

        [Test]
        public void Pursuit_FailureAddsCumulativeFailures()
        {
            var calc = new CombatCalculator(new QueueDiceRoller(3, 10, 2, 11));
            var prey = MakeStats("Proie");
            var hunter = MakeStats("Chasseur");

            var r1 = calc.ResolvePursuitRound(prey, new List<CharacterStats> { hunter });
            Assert.IsFalse(r1.Entries[0].PreyWins);
            Assert.AreEqual(1, r1.TotalFailures);
            Assert.IsFalse(r1.Caught);

            var r2 = calc.ResolvePursuitRound(prey, new List<CharacterStats> { hunter });
            Assert.AreEqual(2, r2.TotalFailures);
            Assert.IsFalse(r2.Caught);
        }

        [Test]
        public void Pursuit_CaptureAtFourFailures()
        {
            // 4 rounds : proie basse (2), poursuivant haut (10) → 4 faillites = capture.
            var calc = new CombatCalculator(new QueueDiceRoller(2, 10, 2, 10, 2, 10, 2, 10));
            var prey = MakeStats("Proie");
            var hunter = MakeStats("Chasseur");

            PursuitRoundResult last = null;
            for (int i = 0; i < 4; i++)
                last = calc.ResolvePursuitRound(prey, new List<CharacterStats> { hunter });

            Assert.AreEqual(4, last.TotalFailures);
            Assert.IsTrue(last.Caught);
            Assert.IsTrue(PursuitState.IsCaught(prey));
            Assert.IsTrue(prey.ActiveStatus.HasFlag(StatusEffect.Immobilise));
            StringAssert.Contains("RATTRAPÉ", last.CombatLog);
        }

        [Test]
        public void PursuitState_ReleaseAndPurge()
        {
            var prey = MakeStats("Proie");
            var hunter = MakeStats("Chasseur");

            PursuitState.RegisterFailure(prey, hunter);
            Assert.AreEqual(1, PursuitState.GetFailures(prey));

            Assert.IsTrue(PursuitState.ReleasePrey(prey));
            Assert.AreEqual(0, PursuitState.GetFailures(prey));

            PursuitState.RegisterFailure(prey, hunter);
            PursuitState.RemoveUnit(hunter);
            PursuitState.OnUnitTurnStart(prey);
            Assert.AreEqual(0, PursuitState.ActiveContestCount);
        }

        /// <summary>
        /// Mock séquentiel : chaque appel à Roll consomme la valeur suivante.
        /// </summary>
        private class QueueDiceRoller : DiceRoller
        {
            private readonly Queue<int> _values;

            public QueueDiceRoller(params int[] values) : base(0)
            {
                _values = new Queue<int>(values);
            }

            public override DiceRollResult Roll(DiceType dieType, int modifier = 0, int targetDC = 10)
            {
                int val = _values.Count > 0 ? _values.Dequeue() : 5;
                return new DiceRollResult
                {
                    DieType = dieType,
                    RawRoll = val,
                    Modifier = modifier,
                    Total = val + modifier,
                    TargetDC = targetDC,
                    Differential = (val + modifier) - targetDC,
                    IsSuccess = (val + modifier) >= targetDC,
                    IsCriticalSuccess = false,
                    IsCriticalFailure = false
                };
            }
        }
    }
}
#endif

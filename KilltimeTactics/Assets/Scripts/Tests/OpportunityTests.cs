#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using NUnit.Framework;
using Killtime.Core.Character;
using Killtime.Core.Combat;
using Killtime.Core.Dice;

namespace Killtime.Tests
{
    /// <summary>
    /// RD-031 Attaque d'opportunité / désengagement : quitter le contact provoque
    /// 1 réaction (frappe gratuite, poursuite 1 PA, blocage 1 PA, balayage gratuit),
    /// sauf spé esquive ou Décrochage payé. 1 réaction/round, rechargée au tour personnel.
    /// </summary>
    [TestFixture]
    public class OpportunityTests
    {
        private static CharacterStats MakeStats(string name)
        {
            var attr = new Attributes(4, 4, 3, 3, 3, 2, 2, 2);
            var sheet = new CharacterSheet { Name = name, BaseAttributes = attr };
            var stats = sheet.ToCombatStats();
            stats.Sheet = sheet;
            return stats;
        }

        [SetUp]
        public void SetUp() => OpportunityState.ClearAll();

        [TearDown]
        public void TearDown() => OpportunityState.ClearAll();

        [Test]
        public void Stance_DefaultsToStrike_CanChange()
        {
            var unit = MakeStats("Veilleur");
            Assert.AreEqual(OpportunityReactionType.Strike, OpportunityState.GetStance(unit));
            OpportunityState.SetStance(unit, OpportunityReactionType.Follow);
            Assert.AreEqual(OpportunityReactionType.Follow, OpportunityState.GetStance(unit));
            OpportunityState.SetStance(unit, OpportunityReactionType.Block);
            Assert.AreEqual("Blocage", OpportunityState.StanceLabel(OpportunityState.GetStance(unit)));
        }

        [Test]
        public void Reaction_OncePerRound_RefreshedOnTurnStart()
        {
            var reactor = MakeStats("Réacteur");
            Assert.IsTrue(OpportunityState.CanReact(reactor));
            Assert.IsTrue(OpportunityState.TryConsumeReaction(reactor));
            Assert.IsTrue(OpportunityState.HasReacted(reactor));
            Assert.IsFalse(OpportunityState.CanReact(reactor));
            Assert.IsFalse(OpportunityState.TryConsumeReaction(reactor));

            OpportunityState.OnUnitTurnStart(reactor);
            Assert.IsFalse(OpportunityState.HasReacted(reactor));
            Assert.IsTrue(OpportunityState.CanReact(reactor));
        }

        [Test]
        public void Reaction_RefusedWhenStunned()
        {
            var stunned = MakeStats("Sonné");
            stunned.ApplyStatus(StatusEffect.Etourdi, 1);
            Assert.IsFalse(OpportunityState.CanReact(stunned));
            Assert.IsFalse(OpportunityState.TryConsumeReaction(stunned));
        }

        [Test]
        public void Strike_IsFree_LogsOpportunite()
        {
            var reactor = MakeStats("Frappeur");
            var target = MakeStats("Fuyard");
            var calc = new CombatCalculator(new DiceRoller(7));
            Assert.IsTrue(OpportunityState.TryConsumeReaction(reactor));
            int paBefore = reactor.CurrentActionPoints;

            var result = calc.ResolveOpportunityStrike(
                reactor, target, 5,
                CombatCalculator.ResolveOpportunitySkill(reactor));
            Assert.IsTrue(result.HasValue);
            StringAssert.Contains("OPPORTUNITÉ", result.Value.CombatLog);
            Assert.AreEqual(paBefore, reactor.CurrentActionPoints);
            Assert.IsTrue(OpportunityState.HasReacted(reactor));
        }

        [Test]
        public void Trip_TargetsLegs_LogsBalayage()
        {
            var reactor = MakeStats("Faucheur");
            var target = MakeStats("Fuyard");
            var calc = new CombatCalculator(new DiceRoller(7));

            var result = calc.ResolveOpportunityTrip(
                reactor, target, 5,
                CombatCalculator.ResolveOpportunitySkill(reactor));
            Assert.IsTrue(result.HasValue);
            Assert.AreEqual(BodyPart.Jambes, result.Value.TargetPart);
            StringAssert.Contains("balayage", result.Value.CombatLog);
        }

        [Test]
        public void Block_ResolvesOpposedAthletics_LogsBlocage()
        {
            var blocker = MakeStats("Bloqueur");
            var mover = MakeStats("Fuyard");
            var calc = new CombatCalculator(new DiceRoller(7));

            var opposed = calc.ResolveOpportunityBlock(blocker, mover);
            Assert.AreEqual(SkillType.Athletisme, opposed.AttackSkill);
            StringAssert.Contains("blocage", opposed.CombatLog);
        }

        [Test]
        public void ExemptSpec_DoesNotProvoke()
        {
            var plain = MakeStats("Fantassin");
            Assert.IsTrue(OpportunityState.TryBeginProvokingMove(plain));

            var roller = MakeStats("Roulade");
            roller.Sheet.UnlockedSpecializations.Add("Roulade de Décrochage");
            Assert.IsTrue(OpportunityState.IsExemptFromProvoking(roller));
            Assert.IsFalse(OpportunityState.TryBeginProvokingMove(roller));

            // Branche héritée (préfixe) : exemptée aussi.
            var branch = MakeStats("Branche");
            branch.Sheet.UnlockedSpecializations.Add("Roulade de Décrochage : Pas Déphasé");
            Assert.IsFalse(OpportunityState.TryBeginProvokingMove(branch));

            var acrobat = MakeStats("Acrobate");
            acrobat.Sheet.UnlockedSpecializations.Add("Acrobatie d'Évitement");
            Assert.IsFalse(OpportunityState.TryBeginProvokingMove(acrobat));
        }

        [Test]
        public void SafeDisengage_CostsPA_SkipsOneMove()
        {
            var mover = MakeStats("Fuyard");
            int before = mover.CurrentActionPoints;

            Assert.IsTrue(OpportunityState.RequestSafeDisengage(mover, 1, out string error), error);
            Assert.AreEqual(before - 1, mover.CurrentActionPoints);
            Assert.IsTrue(OpportunityState.HasSafeDisengage(mover));

            // Premier déplacement : consommé, ne provoque pas.
            Assert.IsFalse(OpportunityState.TryBeginProvokingMove(mover));
            Assert.IsFalse(OpportunityState.HasSafeDisengage(mover));

            // Déplacement suivant : provoque à nouveau.
            Assert.IsTrue(OpportunityState.TryBeginProvokingMove(mover));
        }

        [Test]
        public void SafeDisengage_RefusedTwice_ExpiresOnTurnStart()
        {
            var mover = MakeStats("Fuyard");
            Assert.IsTrue(OpportunityState.RequestSafeDisengage(mover, 1, out _));
            Assert.IsFalse(OpportunityState.RequestSafeDisengage(mover, 1, out _));

            OpportunityState.OnUnitTurnStart(mover);
            Assert.IsFalse(OpportunityState.HasSafeDisengage(mover));
        }
    }
}
#endif

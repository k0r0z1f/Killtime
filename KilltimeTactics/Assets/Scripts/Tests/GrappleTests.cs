#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using NUnit.Framework;
using Killtime.Core.Character;
using Killtime.Core.Combat;
using Killtime.Core.Dice;

namespace Killtime.Tests
{
    /// <summary>
    /// RD-039 : Lutte / grapple / traînée corps (Livre VI).
    /// Prise au corps-à-corps (Athlétisme vs Athlétisme) -> Immobilisé.
    /// Dégagement (duel opposé) vs Relâcher (0 PA).
    /// Étranglement -> Asphyxie (drain PA + dégâts résiduels).
    /// Traînée de corps -> 1 case pour 2 PA.
    /// </summary>
    [TestFixture]
    public class GrappleTests
    {
        private static CharacterStats MakeStats(string name, int force = 5, int agi = 4, int con = 5, int ap = 6, int hp = 20)
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
            GrappleState.ClearAll();
        }

        [TearDown]
        public void TearDown()
        {
            GrappleState.ClearAll();
        }

        [Test]
        public void GrappleState_SetAndRelease_UpdatesHoldAndStatus()
        {
            var grappler = MakeStats("Lutteur");
            var victim = MakeStats("Cible");

            Assert.IsFalse(GrappleState.IsGrappling(grappler));
            Assert.IsFalse(GrappleState.IsGrappled(victim));

            GrappleState.SetGrapple(grappler, victim);

            Assert.IsTrue(GrappleState.IsGrappling(grappler));
            Assert.IsTrue(GrappleState.IsGrappled(victim));
            Assert.IsTrue(GrappleState.IsGrappling(grappler, victim));
            Assert.IsTrue(victim.ActiveStatus.HasFlag(StatusEffect.Immobilise));

            Assert.IsTrue(GrappleState.TryGetGrappledVictim(grappler, out var foundVictim));
            Assert.AreEqual(victim, foundVictim);

            Assert.IsTrue(GrappleState.TryGetGrappler(victim, out var foundGrappler));
            Assert.AreEqual(grappler, foundGrappler);

            // Relâcher la prise
            bool released = GrappleState.ReleaseGrapple(grappler);
            Assert.IsTrue(released);
            Assert.IsFalse(GrappleState.IsGrappling(grappler));
            Assert.IsFalse(GrappleState.IsGrappled(victim));
            Assert.IsFalse(victim.ActiveStatus.HasFlag(StatusEffect.Immobilise));
        }

        [Test]
        public void GrappleState_IncapacitationBreaksGrapple()
        {
            var grappler = MakeStats("Lutteur");
            var victim = MakeStats("Cible");

            GrappleState.SetGrapple(grappler, victim);
            Assert.IsTrue(GrappleState.IsGrappling(grappler, victim));

            // Si le grappler est étourdi ou assommé, la prise ne peut plus être maintenue
            grappler.ApplyStatus(StatusEffect.Etourdi, 1);
            Assert.IsFalse(GrappleState.CanMaintainGrapple(grappler, victim));
            Assert.IsFalse(GrappleState.IsGrappling(grappler));
            Assert.IsFalse(GrappleState.IsGrappled(victim));
        }

        [Test]
        public void GrappleDuel_Success_InflictsImmobiliseAndBindsState()
        {
            // Déterminisme : attaquant fait un gros jet, défenseur fait un jet bas
            var roller = new MockDeterministicDiceRoller(attackerRoll: 10, defenderRoll: 2);
            var calc = new CombatCalculator(roller);

            var attacker = MakeStats("Attaquant", force: 6, con: 6);
            var defender = MakeStats("Défenseur", force: 3, con: 3);

            var duel = calc.ResolveGrappleDuel(attacker, defender);

            Assert.IsTrue(duel.AttackerWins);
            Assert.IsTrue(duel.Differential > 0);
            Assert.IsTrue(GrappleState.IsGrappling(attacker, defender));
            Assert.IsTrue(defender.ActiveStatus.HasFlag(StatusEffect.Immobilise));
            StringAssert.Contains("PRISE RÉUSSIE", duel.CombatLog);
        }

        [Test]
        public void GrappleDuel_Failure_DoesNotBindOrImmobilise()
        {
            // Attaquant rate le jet opposé
            var roller = new MockDeterministicDiceRoller(attackerRoll: 2, defenderRoll: 12);
            var calc = new CombatCalculator(roller);

            var attacker = MakeStats("Attaquant", force: 3, con: 3);
            var defender = MakeStats("Défenseur", force: 6, con: 6);

            var duel = calc.ResolveGrappleDuel(attacker, defender);

            Assert.IsFalse(duel.AttackerWins);
            Assert.IsTrue(duel.Differential < 0);
            Assert.IsFalse(GrappleState.IsGrappling(attacker, defender));
            Assert.IsFalse(defender.ActiveStatus.HasFlag(StatusEffect.Immobilise));
            StringAssert.Contains("ÉCHEC", duel.CombatLog);
        }

        [Test]
        public void GrappleEscapeDuel_Success_DissipatesImmobilise()
        {
            var grappler = MakeStats("Grappler", force: 5, con: 5);
            var victim = MakeStats("Victime", force: 5, agi: 6);

            GrappleState.SetGrapple(grappler, victim);
            Assert.IsTrue(victim.ActiveStatus.HasFlag(StatusEffect.Immobilise));

            // La victime remporte le jet opposé pour se dégager
            var roller = new MockDeterministicDiceRoller(attackerRoll: 10, defenderRoll: 2);
            var calc = new CombatCalculator(roller);

            var duel = calc.ResolveGrappleEscapeDuel(victim, grappler);

            Assert.IsTrue(duel.AttackerWins);
            Assert.IsFalse(GrappleState.IsGrappling(grappler));
            Assert.IsFalse(GrappleState.IsGrappled(victim));
            Assert.IsFalse(victim.ActiveStatus.HasFlag(StatusEffect.Immobilise));
            StringAssert.Contains("PRISE BRISÉE", duel.CombatLog);
        }

        [Test]
        public void GrappleEscapeDuel_Failure_MaintainsImmobilise()
        {
            var grappler = MakeStats("Grappler", force: 6, con: 6);
            var victim = MakeStats("Victime", force: 3, agi: 3);

            GrappleState.SetGrapple(grappler, victim);

            // Échec du dégagement
            var roller = new MockDeterministicDiceRoller(attackerRoll: 1, defenderRoll: 10);
            var calc = new CombatCalculator(roller);

            var duel = calc.ResolveGrappleEscapeDuel(victim, grappler);

            Assert.IsFalse(duel.AttackerWins);
            Assert.IsTrue(GrappleState.IsGrappling(grappler, victim));
            Assert.IsTrue(victim.ActiveStatus.HasFlag(StatusEffect.Immobilise));
            StringAssert.Contains("ÉCHEC DU DÉGAGEMENT", duel.CombatLog);
        }

        [Test]
        public void Strangulation_Success_InflictsAsphyxieAndDamage()
        {
            var attacker = MakeStats("Étrangleur", force: 6);
            var defender = MakeStats("Victime", con: 3, hp: 20);

            var roller = new MockDeterministicDiceRoller(attackerRoll: 10, defenderRoll: 3);
            var calc = new CombatCalculator(roller);

            int hpBefore = defender.CurrentHealth;
            var duel = calc.ResolveStrangulationDuel(attacker, defender);

            Assert.IsTrue(duel.AttackerWins);
            Assert.IsTrue(defender.ActiveStatus.HasFlag(StatusEffect.Asphyxie));
            Assert.IsTrue(defender.CurrentHealth < hpBefore);
            StringAssert.Contains("SUFFOCATION", duel.CombatLog);
            StringAssert.Contains("ASPHYXIE", duel.CombatLog);
        }

        [Test]
        public void CanMove_ReturnsFalse_WhenImmobilisedOrIncapacitated()
        {
            var stats = MakeStats("Coureur");
            Assert.IsTrue(stats.CanMove());

            stats.ApplyStatus(StatusEffect.Immobilise, 1);
            Assert.IsFalse(stats.CanMove());

            stats.RemoveStatus(StatusEffect.Immobilise);
            Assert.IsTrue(stats.CanMove());

            stats.ApplyStatus(StatusEffect.Paralyse, 1);
            Assert.IsFalse(stats.CanMove());

            stats.RemoveStatus(StatusEffect.Paralyse);
            Assert.IsTrue(stats.CanMove());

            stats.CurrentHealth = 0;
            stats.ActiveStatus |= StatusEffect.Inconscient;
            Assert.IsFalse(stats.CanMove());
            // RD-039 : Un combattant saisi en lutte ne peut jamais se déplacer
            var opponent = MakeStats("Opposant");
            GrappleState.SetGrapple(opponent, stats);
            Assert.IsFalse(stats.CanMove());

            // Même si le flag Immobilise avait été temporairement retiré, la prise l'interdit toujours
            stats.RemoveStatus(StatusEffect.Immobilise);
            Assert.IsFalse(stats.CanMove());

            GrappleState.ReleaseGrapple(stats);
            Assert.IsTrue(stats.CanMove());
        }

        [Test]
        public void TurnManager_OnUnitTurnStart_PurgesInvalidGrapple()
        {
            var grappler = MakeStats("Grappler");
            var victim = MakeStats("Victime");

            GrappleState.SetGrapple(grappler, victim);
            Assert.IsTrue(GrappleState.IsGrappling(grappler, victim));

            // Grappler tué
            grappler.CurrentHealth = 0;
            grappler.ActiveStatus |= StatusEffect.Inconscient;

            GrappleState.OnUnitTurnStart(victim);
            Assert.IsFalse(GrappleState.IsGrappled(victim));
            Assert.IsFalse(victim.ActiveStatus.HasFlag(StatusEffect.Immobilise));
        }

        [Test]
        public void TurnManager_OnUnitTurnStart_MaintainsImmobilise_WhenHoldActive()
        {
            var grappler = MakeStats("Grappler");
            var victim = MakeStats("Victime");

            GrappleState.SetGrapple(grappler, victim);
            Assert.IsTrue(victim.ActiveStatus.HasFlag(StatusEffect.Immobilise));

            // Simule l'expiration du flag à la fin du tour précédent
            victim.RemoveStatus(StatusEffect.Immobilise);
            Assert.IsFalse(victim.ActiveStatus.HasFlag(StatusEffect.Immobilise));

            // Au début du tour de la victime, la prise toujours valide réapplique Immobilisé
            GrappleState.OnUnitTurnStart(victim);
            Assert.IsTrue(GrappleState.IsGrappled(victim));
            Assert.IsTrue(victim.ActiveStatus.HasFlag(StatusEffect.Immobilise));
        }

        /// <summary>
        /// Mock de DiceRoller injectant des valeurs fixes pour tester les résolutions opposées de manière déterministe.
        /// </summary>
        private class MockDeterministicDiceRoller : DiceRoller
        {
            private readonly int _att;
            private readonly int _def;
            private bool _isFirstCall = true;

            public MockDeterministicDiceRoller(int attackerRoll, int defenderRoll) : base(0)
            {
                _att = attackerRoll;
                _def = defenderRoll;
            }

            public override DiceRollResult Roll(DiceType dieType, int modifier = 0, int targetDC = 10)
            {
                int val = _isFirstCall ? _att : _def;
                _isFirstCall = !_isFirstCall;

                return new DiceRollResult
                {
                    DieType = dieType,
                    RawRoll = val,
                    Modifier = modifier,
                    Total = val + modifier,
                    TargetDC = targetDC,
                    Differential = (val + modifier) - targetDC,
                    IsSuccess = (val + modifier) >= targetDC,
                    IsCriticalSuccess = val >= 10,
                    IsCriticalFailure = val <= 1
                };
            }
        }
    }
}
#endif

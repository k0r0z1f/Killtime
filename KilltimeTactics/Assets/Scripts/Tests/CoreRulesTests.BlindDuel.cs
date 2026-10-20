#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEngine;
using Killtime.Core.Character;
using Killtime.Core.Dice;
using Killtime.Core.Combat;
using Killtime.Core.Arcanotech;
using Killtime.Core.Chrono;
using Killtime.Tactics.Grid;

namespace Killtime.Tests
{
    public partial class CoreRulesTests
    {
        [Test]
        public void TestBlindDuel_StakesDeclaredBeforeAnyRoll_ThenSimultaneousReveal()
        {
            // Duel aveugle Livres II §7 + VI §20.1 : déclaration attaquant (mise cachée)
            // -> déclaration défenseur à l'aveugle -> révélation simultanée.
            var diceRoller = new DiceRoller(7);
            var calc = new CombatCalculator(diceRoller);

            var attAttr = new Attributes(4, 4, 3, 3, 3, 2, 2, 2);
            var defAttr = new Attributes(2, 3, 4, 2, 2, 2, 2, 2);
            var attSheet = new CharacterSheet { Name = "Attaquant", BaseAttributes = attAttr };
            var defSheet = new CharacterSheet { Name = "Défenseur", BaseAttributes = defAttr };
            var attStats = attSheet.ToCombatStats();
            var defStats = defSheet.ToCombatStats();

            int attPABefore = attStats.CurrentActionPoints;
            int defPABefore = defStats.CurrentActionPoints;

            // Étape 0 : Begin ne lance aucun dé, ne prélève que la base d'attaque.
            var duel = calc.BeginSkillDuel(
                attStats, defStats, BodyPart.Torse,
                SkillType.ManiementArmes, SkillType.Esquive,
                5, false, true, null, null, 0, out string err);
            Assert.IsNotNull(duel, err);
            Assert.IsTrue(string.IsNullOrEmpty(err));
            Assert.AreEqual(attPABefore - 2, attStats.CurrentActionPoints);
            Assert.AreEqual(defPABefore, defStats.CurrentActionPoints);
            Assert.IsFalse(duel.AttackerStakesDeclared);
            Assert.IsFalse(duel.HasResolved);

            // Étape 1 : déclaration attaquant (2 PA + 1 PE), toujours sans aucun jet.
            int attCommitted = calc.DeclareAttackerStakes(duel, 2, 1);
            Assert.AreEqual(2 + 1 * Killtime.Core.Rules.CoreRulesConfig.Instance.DuelPEBonusPerPoint, attCommitted);
            Assert.IsTrue(duel.AttackerStakesDeclared);
            Assert.IsFalse(duel.HasResolved);
            Assert.AreEqual(attPABefore - 2 - 2, attStats.CurrentActionPoints);
            Assert.AreEqual(1, attStats.Essoufflement);

            // Étape 2 : déclaration aveugle du défenseur (base + 3 PA + 0 PE).
            int defCommitted = calc.DeclareDefenderStakes(duel, SkillType.Esquive, true, 3, 0);
            Assert.AreEqual(3, defCommitted);
            Assert.IsTrue(duel.DefenderBasePaid);
            Assert.AreEqual(defPABefore - 1 - 3, defStats.CurrentActionPoints);

            // Étape 3 : révélation simultanée.
            var result = calc.ResolveBlindDuel(duel);
            Assert.IsTrue(duel.HasResolved);
            Assert.AreEqual(duel.AttackFinalRoll.Total - duel.DefenseFinalRoll.Total, result.Differential);
            StringAssert.Contains("Phase 1", result.CombatLog);
            StringAssert.Contains("Phase 2", result.CombatLog);
            StringAssert.Contains("SIMULTAN", result.CombatLog.ToUpper());

            // API classique : mêmes totaux via déclarations aveugles (enchère aveugle préalable).
            attStats.ResetTurn();
            defStats.ResetTurn();
            attStats.Essoufflement = 0;
            defStats.Essoufflement = 0;
            var classic = calc.ResolveTargetedAttack(
                attacker: attStats, defender: defStats, targetedPart: BodyPart.Torse,
                attackSkill: SkillType.ManiementArmes, defenseSkill: SkillType.Esquive,
                weaponBaseDamage: 5, cancelPenaltyWithAP: false, defenderWantsToDefend: true,
                attackerBonusAP: 2, defenderBonusAP: 1);
            // PA totaux = bases (2+1) + mises (2+1).
            Assert.AreEqual(attStats.MaxActionPoints - 4, attStats.CurrentActionPoints);
            Assert.AreEqual(defStats.MaxActionPoints - 2, defStats.CurrentActionPoints);
            StringAssert.Contains("Phase 1", classic.CombatLog);
            StringAssert.Contains("Phase 2", classic.CombatLog);
        }

        [Test]
        public void TestBlindDuel_AutoDeclareNeverSeesAttackerRoll()
        {
            var diceRoller = new DiceRoller(11);
            var calc = new CombatCalculator(diceRoller);
            var attAttr = new Attributes(4, 4, 3, 3, 3, 2, 2, 2);
            var defAttr = new Attributes(2, 3, 4, 2, 2, 2, 2, 2);
            var attSheet = new CharacterSheet { Name = "Att", BaseAttributes = attAttr };
            var defSheet = new CharacterSheet { Name = "Def", BaseAttributes = defAttr };
            var attStats = attSheet.ToCombatStats();
            var defStats = defSheet.ToCombatStats();

            var duel = calc.BeginSkillDuel(
                attStats, defStats, BodyPart.Torse,
                SkillType.ManiementArmes, SkillType.Esquive,
                5, false, true, null, null, 0, out string duelErr);
            Assert.IsNotNull(duel, duelErr);
            calc.DeclareAttackerStakes(duel, 4, 0);
            // La mise adverse est cachée : l'estimation ignore tout bonus attaquant.
            int expectedAttack = (int)System.Math.Round(CombatCalculator.DieAverage(duel.AttackDie)) + duel.AttackBaseMod;
            int expectedDefense = (int)System.Math.Round(CombatCalculator.DieAverage(duel.DefenseDie)) + duel.DefenseBaseMod;
            int expectedNeed = System.Math.Max(0, expectedAttack - expectedDefense + 1);
            int affordable = System.Math.Max(0, defStats.CurrentActionPoints - duel.BaseDefenseCost);
            int expectedCommitted = System.Math.Min(expectedNeed, affordable);

            int committed = calc.AutoDeclareDefenderStakes(duel);
            Assert.AreEqual(expectedCommitted, committed);
            Assert.IsTrue(duel.DefenderStakesDeclared);
            // Aucun jet lancé avant la résolution.
            Assert.IsFalse(duel.HasResolved);

            var result = calc.ResolveBlindDuel(duel);
            Assert.IsTrue(duel.HasResolved);
            Assert.AreEqual(duel.AttackFinalRoll.Total - duel.DefenseFinalRoll.Total, result.Differential);
        }

        [Test]
        public void TestOpposedCheck_BlindStakesAndSimultaneousReveal()
        {
            var diceRoller = new DiceRoller(5);
            var calc = new CombatCalculator(diceRoller);
            var attAttr = new Attributes(4, 4, 3, 3, 3, 2, 2, 2);
            var defAttr = new Attributes(2, 3, 4, 2, 2, 2, 2, 2);
            var attSheet = new CharacterSheet { Name = "Meneur", BaseAttributes = attAttr };
            var defSheet = new CharacterSheet { Name = "Cible", BaseAttributes = defAttr };
            var attStats = attSheet.ToCombatStats();
            var defStats = defSheet.ToCombatStats();

            int attPABefore = attStats.CurrentActionPoints;
            int defPABefore = defStats.CurrentActionPoints;

            var check = calc.ResolveOpposedCheck(
                attStats, SkillType.Intimidation, defStats, SkillType.Intuition,
                attackerBonusAP: 2, attackerPE: 1, defenderBonusAP: 1, defenderPE: 0);

            Assert.AreEqual(2, check.AttackerBonusPAApplied);
            Assert.AreEqual(1, check.AttackerPEApplied);
            Assert.AreEqual(1, check.DefenderBonusPAApplied);
            Assert.AreEqual(attPABefore - 2, attStats.CurrentActionPoints);
            Assert.AreEqual(1, attStats.Essoufflement);
            Assert.AreEqual(defPABefore - 1, defStats.CurrentActionPoints);
            Assert.AreEqual(check.AttackRoll.Total - check.DefenseRoll.Total, check.Differential);
            Assert.AreEqual(check.Differential >= 0, check.AttackerWins);
            StringAssert.Contains("AVEUGLE", check.CombatLog.ToUpper());
            StringAssert.Contains("SIMULTAN", check.CombatLog.ToUpper());
        }
    }
}
#endif
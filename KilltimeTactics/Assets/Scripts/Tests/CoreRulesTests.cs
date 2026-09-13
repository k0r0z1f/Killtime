#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using NUnit.Framework;
using Killtime.Core.Character;
using Killtime.Core.Dice;
using Killtime.Core.Combat;
using Killtime.Core.Arcanotech;
using Killtime.Core.Chrono;

namespace Killtime.Tests
{
    [TestFixture]
    public class CoreRulesTests
    {
        [Test]
        public void TestRogerAttributesAndActionPoints_MatchesCodexLivreI()
        {
            var rogerAttr = new Attributes(agi: 3, @int: 2, rap: 3, con: 4, @for: 3, cha: 1);

            Assert.AreEqual(7, rogerAttr.CalculateBaseActionPoints());
            Assert.AreEqual(8, rogerAttr.CalculateEncaissement());
            Assert.AreEqual(20, rogerAttr.CalculateLethalMaximum());
        }

        [Test]
        public void TestSkillDieStepProgression_AndCanonicalMapping()
        {
            var attr = new Attributes(@for: 5, agi: 5, con: 4, rap: 3, @int: 2, eru: 3, cha: 1, ins: 3);
            var sheet = new CharacterSheet { BaseAttributes = attr };

            int baseRank = SkillDefinitions.GetBaseRank(SkillType.Ballistique, attr);
            Assert.AreEqual(5, baseRank);
            Assert.AreEqual(DiceType.D6, sheet.GetSkill(SkillType.Ballistique).CalculateSkillDie(baseRank));

            sheet.GetSkill(SkillType.Ballistique).TrainingLevel = 1;
            Assert.AreEqual(DiceType.D8, sheet.GetSkill(SkillType.Ballistique).CalculateSkillDie(baseRank));

            sheet.GetSkill(SkillType.Ballistique).TrainingLevel = 6;
            Assert.AreEqual(DiceType.TwoD6, sheet.GetSkill(SkillType.Ballistique).CalculateSkillDie(baseRank));
        }

        [Test]
        public void TestAttackOpposedByDefenseSkill_AndUnreactiveTargetChoice()
        {
            var diceRoller = new DiceRoller(42);
            var calc = new CombatCalculator(diceRoller);

            var attAttr = new Attributes(4, 4, 3, 3, 3, 2, 2, 2);
            var defAttr = new Attributes(2, 3, 4, 2, 2, 2, 2, 2);

            var attSheet = new CharacterSheet { Name = "Attaquant", BaseAttributes = attAttr };
            var defSheet = new CharacterSheet { Name = "Défenseur", BaseAttributes = defAttr };

            var attStats = attSheet.ToCombatStats();
            var defStats = defSheet.ToCombatStats();
            attStats.Sheet = attSheet;
            defStats.Sheet = defSheet;

            // 1. Cible décidant de NE PAS se défendre (Défense passive : 0 PA consommé, défense = 0)
            int initialDefAP = defStats.CurrentActionPoints;
            var resultPassive = calc.ResolveTargetedAttack(
                attacker: attStats,
                defender: defStats,
                targetedPart: BodyPart.Torse,
                attackSkill: SkillType.Ballistique,
                defenseSkill: SkillType.Esquive,
                weaponBaseDamage: 5,
                cancelPenaltyWithAP: false,
                defenderWantsToDefend: false
            );

            Assert.IsTrue(resultPassive.IsHit);
            Assert.IsFalse(resultPassive.IsBlocked);
            Assert.AreEqual(0, resultPassive.DefenseRoll.Total);
            Assert.AreEqual(initialDefAP, defStats.CurrentActionPoints);

            // 2. Cible décidant de se défendre activement
            attStats.ResetTurn();
            defStats.ResetTurn();
            var resultActive = calc.ResolveTargetedAttack(
                attacker: attStats,
                defender: defStats,
                targetedPart: BodyPart.Torse,
                attackSkill: SkillType.ManiementArmes,
                defenseSkill: SkillType.Esquive,
                weaponBaseDamage: 5,
                cancelPenaltyWithAP: false,
                defenderWantsToDefend: true
            );

            Assert.Greater(resultActive.DefenseRoll.Total, 0);
            Assert.Less(defStats.CurrentActionPoints, defStats.MaxActionPoints);
        }

        [Test]
        public void TestDefensiveSpecialization_CancelsNonExclusiveDiePenalty()
        {
            var attr = new Attributes(4, 4, 4, 3, 2, 2, 2, 2);
            var sheet = new CharacterSheet { BaseAttributes = attr };
            var stats = sheet.ToCombatStats();
            stats.Sheet = sheet;

            DiceType baseDie = stats.GetSkillDie(SkillType.ManiementArmes);
            Assert.AreEqual(DiceType.D6, baseDie);

            // Sans la spé "Maniement de l'Épée", le dé chute d'un niveau en parade
            Assert.IsFalse(SkillDefinitions.HasDefensiveSpecialization(sheet, SkillType.ManiementArmes));
            DiceType penalized = SkillDefinitions.StepDownDie(baseDie);
            Assert.AreEqual(DiceType.D4, penalized);

            // Avec la spé, aucune rétrogradation
            sheet.UnlockedSpecializations.Add("Maniement de l'Épée");
            Assert.IsTrue(SkillDefinitions.HasDefensiveSpecialization(sheet, SkillType.ManiementArmes));
        }

        [Test]
        public void TestEmergencyBreath_IncreasesActionPointsAndEssoufflement()
        {
            var attr = new Attributes(3, 2, 3, 4, 3, 1);
            var stats = new CharacterStats("Testeur", attr);

            stats.ConsumeActionPoints(7);
            Assert.AreEqual(0, stats.CurrentActionPoints);
            Assert.AreEqual(0, stats.Essoufflement);

            bool success = stats.TakeEmergencyBreath(2);
            Assert.IsTrue(success);
            Assert.AreEqual(2, stats.CurrentActionPoints);
            Assert.AreEqual(1, stats.Essoufflement);
        }

        [Test]
        public void TestNythariteSpell_CreationCostEqualsActionPointCost_MatchesLivreIV()
        {
            var spell = new NythariteSpell("Décharge Cinétique", PsychicDiscipline.Telekinesie, PsychicStage.Stade2_AccesProfond, costXpPa: 4);

            Assert.AreEqual(4, spell.CreationXpCost);
            Assert.AreEqual(4, spell.ActionPointCost);
        }

        [Test]
        public void TestStatusConditions_ApplyStrictCodexPenalties_AndIncapacitateDefense()
        {
            var attr = new Attributes(4, 4, 3, 3, 3, 2, 2, 2);
            var sheet = new CharacterSheet { Name = "Guerrier", BaseAttributes = attr };
            var stats = sheet.ToCombatStats();
            stats.Sheet = sheet;

            int baseMod = stats.GetSkillModifier(SkillType.ManiementArmes, isOffensive: true);
            Assert.AreEqual(4, baseMod);

            // 1. Déstabilisé (-2)
            stats.ActiveStatus |= StatusEffect.Destabilise;
            Assert.AreEqual(2, stats.GetSkillModifier(SkillType.ManiementArmes, isOffensive: true));

            // 2. Étourdi (-1) => Cumul à -3
            stats.ActiveStatus |= StatusEffect.Etourdi;
            Assert.AreEqual(1, stats.GetSkillModifier(SkillType.ManiementArmes, isOffensive: true));

            // 3. À Terre (-1 attaque, -2 défense)
            stats.ActiveStatus |= StatusEffect.ATerre;
            Assert.AreEqual(0, stats.GetSkillModifier(SkillType.ManiementArmes, isOffensive: true));
            Assert.AreEqual(-1, stats.GetSkillModifier(SkillType.Esquive, isOffensive: false));

            // 4. Inconscient / K.O. : Parade impossible et interdiction d'attaquer
            stats.ActiveStatus |= StatusEffect.Inconscient;
            Assert.IsFalse(stats.CanDefendActively());
            Assert.IsFalse(stats.CanAttack(DiceType.D6));
        }
    }
}
#endif
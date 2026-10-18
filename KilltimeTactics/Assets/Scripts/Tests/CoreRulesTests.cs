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
    [TestFixture]
    public partial class CoreRulesTests
    {
        [Test]
        public void TestCanonEntrave_AppliesPenaltyToBallistique_UnlessCompensatedOrMartialArts()
        {
            var diceRoller = new DiceRoller(1234);
            var calc = new CombatCalculator(diceRoller);

            var attAttr = new Attributes(4, 4, 3, 3, 3, 2, 2, 2);
            var defAttr = new Attributes(2, 3, 4, 2, 2, 2, 2, 2);

            var attSheet = new CharacterSheet { Name = "Tireur", BaseAttributes = attAttr };
            var defSheet = new CharacterSheet { Name = "Cible", BaseAttributes = defAttr };

            var attStats = attSheet.ToCombatStats();
            var defStats = defSheet.ToCombatStats();

            // 1. Tir au contact sans compensation : malus net de -2
            calc.SetContactDistanceState(true);
            var resultPenalized = calc.ResolveTargetedAttack(
                attacker: attStats,
                defender: defStats,
                targetedPart: BodyPart.Torse,
                attackSkill: SkillType.Ballistique,
                defenseSkill: SkillType.Esquive,
                weaponBaseDamage: 6,
                cancelPenaltyWithAP: false,
                defenderWantsToDefend: false
            );

            Assert.IsTrue(resultPenalized.IsCanonEntrave);
            StringAssert.Contains("Canon Entravé : -2", resultPenalized.CombatLog);

            // 2. Frappe martiale avec Arts Martiaux au contact : pas de malus
            attStats.ResetTurn();
            attSheet.UnlockedSpecializations.Add("Arts Martiaux");

            var resultMartial = calc.ResolveTargetedAttack(
                attacker: attStats,
                defender: defStats,
                targetedPart: BodyPart.Torse,
                attackSkill: SkillType.MainsNues,
                defenseSkill: SkillType.Esquive,
                weaponBaseDamage: 4,
                cancelPenaltyWithAP: false,
                defenderWantsToDefend: false
            );

            Assert.IsFalse(resultMartial.IsCanonEntrave);
            StringAssert.Contains("Arts Martiaux : Exemption Totale", resultMartial.CombatLog);
            calc.SetContactDistanceState(false);
        }

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
            // Paliers : carac 5 (+2) + 6 entraînements = 8 niveaux → 2d10.
            Assert.AreEqual(DiceType.TwoD10, sheet.GetSkill(SkillType.Ballistique).CalculateSkillDie(baseRank));
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

            // Paliers : moyenne (4+4+1)/2 = 4 → +1 niveau → d4.
            DiceType baseDie = stats.GetSkillDie(SkillType.ManiementArmes);
            Assert.AreEqual(DiceType.D4, baseDie);

            // Sans la spé "Maniement de l'Épée", le dé chute d'un niveau en parade
            Assert.IsFalse(SkillDefinitions.HasDefensiveSpecialization(sheet, SkillType.ManiementArmes));
            DiceType penalized = SkillDefinitions.StepDownDie(baseDie);
            Assert.AreEqual(DiceType.D2, penalized);

            // Avec la spé, aucune rétrogradation
            sheet.UnlockedSpecializations.Add("Maniement de l'Épée");
            Assert.IsTrue(SkillDefinitions.HasDefensiveSpecialization(sheet, SkillType.ManiementArmes));
        }

        [Test]
        public void TestCombatStats_KeepsSheetTrainingsAndSpecializations()
        {
            // Non-régression : les entraînements et spés de la fiche (ex: Mina,
            // Mains Nues +3 + Arts Martiaux) doivent survivre à ToCombatStats.
            // Stats réelles de Mina : FOR 3, AGI 5, MAG 5 (éveillée → Corps Augmenté).
            var attr = new Attributes(@for: 3, agi: 5, con: 3, rap: 4, @int: 3, eru: 2, cha: 3, ins: 3, mag: 5);
            var sheet = new CharacterSheet { Name = "Mina", BaseAttributes = attr };
            sheet.GetSkill(SkillType.MainsNues).TrainingLevel = 3;
            sheet.UnlockedSpecializations.Add("Arts Martiaux");

            var stats = sheet.ToCombatStats();

            Assert.IsTrue(object.ReferenceEquals(sheet, stats.Sheet));
            // Hors attaque (paliers) : valeur 4 → +1 ; +3 entraînements = 4 niveaux → D10.
            Assert.AreEqual(DiceType.D10, stats.GetSkillDie(SkillType.MainsNues));
            Assert.AreEqual(7, stats.GetSkillRank(SkillType.MainsNues));
            Assert.IsTrue(stats.HasSpecialization("Arts Martiaux"));
            Assert.IsTrue(SkillDefinitions.HasDefensiveSpecialization(stats.Sheet, SkillType.MainsNues));

            // En mêlée, Mina frappe en Mains Nues (Corps Augmenté → D12, voir test dédié)
            // et non au Maniement d'Arme (valeur 4 → +1 → D4).
            Assert.AreEqual(DiceType.D4, stats.GetSkillDie(SkillType.ManiementArmes));
            Assert.AreEqual(SkillType.MainsNues, stats.GetBestMeleeAttackSkill());
        }
    }
}
#endif
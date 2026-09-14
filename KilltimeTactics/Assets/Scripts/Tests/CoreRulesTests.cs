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

        [Test]
        public void TestMagicAugmentedBody_ReplacesForceWithMagicOnOffensiveUnarmed()
        {
            // Règle générale « Corps Augmenté » : Mains Nues offensif, MAG > 0 →
            // FOR substituée par MAG (meilleure des deux), entraînements inchangés.
            var attr = new Attributes(@for: 3, agi: 5, con: 3, rap: 4, @int: 3, eru: 2, cha: 3, ins: 3, mag: 5);
            var sheet = new CharacterSheet { Name = "Mina", BaseAttributes = attr };
            sheet.GetSkill(SkillType.MainsNues).TrainingLevel = 3;
            var stats = sheet.ToCombatStats();

            Assert.IsTrue(stats.IsMagicAugmented(SkillType.MainsNues, true));
            Assert.IsFalse(stats.IsMagicAugmented(SkillType.MainsNues, false));
            Assert.IsFalse(stats.IsMagicAugmented(SkillType.ManiementArmes, true));

            // Offensif (paliers) : MAG 5 → +2 ; +3 entraînements = 5 niveaux → D12.
            Assert.AreEqual(8, stats.GetSkillRank(SkillType.MainsNues, true));
            Assert.AreEqual(DiceType.D12, stats.GetSkillDie(SkillType.MainsNues, true));
            // Défensif : inchangé, valeur 4 → +1 ; +3 = 4 niveaux → D10.
            Assert.AreEqual(7, stats.GetSkillRank(SkillType.MainsNues, false));
            Assert.AreEqual(DiceType.D10, stats.GetSkillDie(SkillType.MainsNues, false));

            // Non-éveillé (MAG 0) : offensif = défensif, aucune augmentation.
            var plainAttr = new Attributes(@for: 5, agi: 5, con: 4, rap: 3, @int: 2, eru: 3, cha: 1, ins: 3, mag: 0);
            var plainSheet = new CharacterSheet { Name = "Brute", BaseAttributes = plainAttr };
            plainSheet.GetSkill(SkillType.MainsNues).TrainingLevel = 1;
            var plainStats = plainSheet.ToCombatStats();

            Assert.IsFalse(plainStats.IsMagicAugmented(SkillType.MainsNues, true));
            Assert.AreEqual(plainStats.GetSkillDie(SkillType.MainsNues, false),
                plainStats.GetSkillDie(SkillType.MainsNues, true));
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

            // RÈGLE CODEX : aucun mod carac ajouté. Base = 0, seuls les états modifient.
            int baseMod = stats.GetSkillModifier(SkillType.ManiementArmes, isOffensive: true);
            Assert.AreEqual(0, baseMod);

            // 1. Déstabilisé (-2)
            stats.ActiveStatus |= StatusEffect.Destabilise;
            Assert.AreEqual(-2, stats.GetSkillModifier(SkillType.ManiementArmes, isOffensive: true));

            // 2. Étourdi (-1) => Cumul à -3
            stats.ActiveStatus |= StatusEffect.Etourdi;
            Assert.AreEqual(-3, stats.GetSkillModifier(SkillType.ManiementArmes, isOffensive: true));

            // 3. À Terre (-1 attaque, -2 défense)
            stats.ActiveStatus |= StatusEffect.ATerre;
            Assert.AreEqual(-4, stats.GetSkillModifier(SkillType.ManiementArmes, isOffensive: true));
            Assert.AreEqual(-5, stats.GetSkillModifier(SkillType.Esquive, isOffensive: false));

            // 4. Inconscient / K.O. : Parade impossible et interdiction d'attaquer
            stats.ActiveStatus |= StatusEffect.Inconscient;
            Assert.IsFalse(stats.CanDefendActively());
            Assert.IsFalse(stats.CanAttack(DiceType.D6));
        }

        [Test]
        public void TestStatusConditions_ExpireAfterOneTurn_UnlessPersistent()
        {
            var attr = new Attributes(4, 4, 3, 3, 3, 2, 2, 2);
            var sheet = new CharacterSheet { Name = "Guerrier", BaseAttributes = attr };
            var stats = sheet.ToCombatStats();
            stats.Sheet = sheet;

            // 1. Application d'un statut temporaire (1 tour par défaut) et d'un statut persistant
            stats.ApplyStatus(StatusEffect.Destabilise, 1);
            stats.ApplyStatus(StatusEffect.Inconscient);

            Assert.IsTrue(stats.ActiveStatus.HasFlag(StatusEffect.Destabilise));
            Assert.IsTrue(stats.ActiveStatus.HasFlag(StatusEffect.Inconscient));

            // 2. Fin de tour : Décrémentation
            var expired = stats.TickTurnStatusDurations();

            Assert.IsTrue(expired.Contains(StatusEffect.Destabilise));
            Assert.IsFalse(stats.ActiveStatus.HasFlag(StatusEffect.Destabilise));
            Assert.IsTrue(stats.ActiveStatus.HasFlag(StatusEffect.Inconscient));
        }
    }
}
#endif
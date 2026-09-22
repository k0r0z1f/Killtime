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
    public class CoreRulesTests
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
        public void TestSkillDiceExamples_MainsNuesD10_EsquiveD8_MatchesCodex()
        {
            // Exemple canonique du Codex : Mains Nues +2 entr., FOR 5 (+2) = 4 niveaux -> d10.
            // Esquive 1 entr., réf. 5 (+2) = 3 niveaux -> d8.
            var attr = new Attributes(@for: 5, agi: 5, con: 4, rap: 5, @int: 2, eru: 3, cha: 1, ins: 3);
            var sheet = new CharacterSheet { BaseAttributes = attr };
            sheet.GetSkill(SkillType.MainsNues).TrainingLevel = 2;
            sheet.GetSkill(SkillType.Esquive).TrainingLevel = 1;
            var stats = sheet.ToCombatStats();

            // Mains Nues offensif : (5+5+1)/2 = 5 -> +2 paliers + 2 entr. = 4 -> D10.
            Assert.AreEqual(DiceType.D10, stats.GetSkillDie(SkillType.MainsNues, true));
            // Esquive : (5+5+1)/2 = 5 -> +2 paliers + 1 entr. = 3 -> D8.
            Assert.AreEqual(DiceType.D8, stats.GetSkillDie(SkillType.Esquive, false));
        }

        [Test]
        public void TestBlindDuel_StakesDeclaredBeforeAnyRoll_ThenSimultaneousReveal()
        {
            // Duel aveugle Livres II §7 + VI §24.1 : déclaration attaquant (mise cachée)
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
        public void TestCover_AttackPenaltiesAndFullBlock_MatchesLivreVI253()
        {
            // Livre VI §25.3 : moitié visible -1, 3/4 couvert -2, non visible = impossible.
            var diceRoller = new DiceRoller(99);
            var calc = new CombatCalculator(diceRoller);
            var cfg = Killtime.Core.Rules.CoreRulesConfig.Instance;
            Assert.AreEqual(-1, cfg.HalfCoverAttackPenalty);
            Assert.AreEqual(-2, cfg.ThreeQuartersCoverAttackPenalty);
            Assert.IsTrue(cfg.FullCoverBlocksAttack);

            var attAttr = new Attributes(4, 4, 3, 3, 3, 2, 2, 2);
            var defAttr = new Attributes(2, 3, 4, 2, 2, 2, 2, 2);
            var attSheet = new CharacterSheet { Name = "Attaquant", BaseAttributes = attAttr };
            var defSheet = new CharacterSheet { Name = "Défenseur", BaseAttributes = defAttr };
            var attStats = attSheet.ToCombatStats();
            var defStats = defSheet.ToCombatStats();

            var duelNone = calc.BeginSkillDuel(
                attStats, defStats, BodyPart.Torse,
                SkillType.Ballistique, SkillType.Esquive,
                5, false, false, null, null, 0, out string errNone, CoverType.None);
            Assert.IsNotNull(duelNone, errNone);
            Assert.AreEqual(0, duelNone.CoverAttackPenalty);

            attStats.ResetTurn(); defStats.ResetTurn();
            var duelHalf = calc.BeginSkillDuel(
                attStats, defStats, BodyPart.Torse,
                SkillType.Ballistique, SkillType.Esquive,
                5, false, false, null, null, 0, out string errHalf, CoverType.Half);
            Assert.IsNotNull(duelHalf, errHalf);
            Assert.AreEqual(-1, duelHalf.CoverAttackPenalty);
            Assert.AreEqual(duelNone.AttackBaseMod - 1, duelHalf.AttackBaseMod);

            attStats.ResetTurn(); defStats.ResetTurn();
            var duelTQ = calc.BeginSkillDuel(
                attStats, defStats, BodyPart.Torse,
                SkillType.Ballistique, SkillType.Esquive,
                5, false, false, null, null, 0, out string errTQ, CoverType.ThreeQuarters);
            Assert.IsNotNull(duelTQ, errTQ);
            Assert.AreEqual(-2, duelTQ.CoverAttackPenalty);
            Assert.AreEqual(duelNone.AttackBaseMod - 2, duelTQ.AttackBaseMod);

            // Couvert total : Begin refuse (aucun PA dépensé au-delà du contrôle).
            attStats.ResetTurn(); defStats.ResetTurn();
            int paBefore = attStats.CurrentActionPoints;
            var duelFull = calc.BeginSkillDuel(
                attStats, defStats, BodyPart.Torse,
                SkillType.Ballistique, SkillType.Esquive,
                5, false, false, null, null, 0, out string errFull, CoverType.Full);
            Assert.IsNull(duelFull);
            StringAssert.Contains("attaque impossible", errFull.ToLower());
            Assert.AreEqual(paBefore, attStats.CurrentActionPoints);

            // API classique : résultat marqué Bloqué-Par-Couvert, log explicite.
            attStats.ResetTurn(); defStats.ResetTurn();
            var blocked = calc.ResolveTargetedAttack(
                attacker: attStats, defender: defStats, targetedPart: BodyPart.Torse,
                attackSkill: SkillType.Ballistique, defenseSkill: SkillType.Esquive,
                weaponBaseDamage: 5, cancelPenaltyWithAP: false, defenderWantsToDefend: false,
                cover: CoverType.Full);
            Assert.IsTrue(blocked.BlockedByCover);
            Assert.IsFalse(blocked.IsHit);
            StringAssert.Contains("COUVERT", blocked.CombatLog.ToUpper());

            // Malus tracé dans le log d'un tir à moitié couvert.
            attStats.ResetTurn(); defStats.ResetTurn();
            var halfRes = calc.ResolveTargetedAttack(
                attacker: attStats, defender: defStats, targetedPart: BodyPart.Torse,
                attackSkill: SkillType.Ballistique, defenseSkill: SkillType.Esquive,
                weaponBaseDamage: 5, cancelPenaltyWithAP: false, defenderWantsToDefend: false,
                cover: CoverType.Half);
            Assert.AreEqual(CoverType.Half, halfRes.Cover);
            Assert.AreEqual(-1, halfRes.CoverAttackPenalty);
            StringAssert.Contains("Couvert", halfRes.CombatLog);

            // Au contact, les combattants se voient : le couvert est ignoré.
            calc.SetContactDistanceState(true);
            try
            {
                attStats.ResetTurn(); defStats.ResetTurn();
                var duelContact = calc.BeginSkillDuel(
                    attStats, defStats, BodyPart.Torse,
                    SkillType.Ballistique, SkillType.Esquive,
                    5, false, false, null, null, 0, out string errContact, CoverType.Full);
                Assert.IsNotNull(duelContact, errContact);
                Assert.AreEqual(CoverType.None, duelContact.Cover);
                Assert.AreEqual(0, duelContact.CoverAttackPenalty);
            }
            finally
            {
                calc.SetContactDistanceState(false);
            }
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

        [Test]
        public void TestMina_InvisibleSteps_AppliesMoveDiscountOnFirstMoveOnly()
        {
            var attr = new Attributes(@for: 3, agi: 5, con: 3, rap: 4, @int: 3, eru: 2, cha: 3, ins: 3, mag: 5);
            var sheet = new CharacterSheet { Name = "Mina", BaseAttributes = attr };
            sheet.UnlockedSpecializations.Add("Protocole des Pas Invisibles");

            var go = new GameObject("TestUnit_Mina");
            var unit = go.AddComponent<Killtime.Tactics.Units.TacticalUnit>();
            unit.ConfigureStats("Mina", attr, 1, true);
            unit.GetOrBuildSheet().UnlockedSpecializations.Add("Protocole des Pas Invisibles");

            Assert.AreEqual(0, unit.Stats.MovesThisTurn);
            Assert.AreEqual(0, unit.ComputeMovementAPCost(1));
            Assert.AreEqual(2, unit.ComputeMovementAPCost(3));

            unit.Stats.RegisterMove();
            Assert.AreEqual(1, unit.Stats.MovesThisTurn);
            Assert.AreEqual(1, unit.ComputeMovementAPCost(1));
            Assert.AreEqual(3, unit.ComputeMovementAPCost(3));

            unit.Stats.ResetTurn();
            Assert.AreEqual(0, unit.Stats.MovesThisTurn);
            Assert.AreEqual(0, unit.ComputeMovementAPCost(1));

            Object.DestroyImmediate(go);
        }

        [Test]
        public void TestMina_FibresResilientes_And_Homeostasie()
        {
            var attr = new Attributes(@for: 3, agi: 5, con: 3, rap: 4, @int: 3, eru: 2, cha: 3, ins: 3, mag: 5);
            var sheet = new CharacterSheet { Name = "Mina", BaseAttributes = attr };

            var statsNormal = sheet.ToCombatStats();
            Assert.AreEqual(6, statsNormal.EncaissementThreshold); // CON 3 * 2 = 6

            sheet.UnlockedSpecializations.Add("Fibres Résilientes");
            sheet.UnlockedSpecializations.Add("Homéostasie Accélérée");
            var statsMina = sheet.ToCombatStats();
            Assert.AreEqual(8, statsMina.EncaissementThreshold); // (CON 3 * 2) + 2 = 8

            statsMina.CurrentHealth = 10;
            statsMina.ApplyStatus(StatusEffect.Saignement);
            Assert.IsTrue(statsMina.ActiveStatus.HasFlag(StatusEffect.Saignement));

            statsMina.ResetTurn();
            Assert.AreEqual(11, statsMina.CurrentHealth); // +1 PV régénéré
            Assert.IsFalse(statsMina.ActiveStatus.HasFlag(StatusEffect.Saignement)); // Saignement purgé
        }

        [Test]
        public void TestMina_Volume2_SpecializationStoryLock()
        {
            var attr = new Attributes(@for: 3, agi: 5, con: 3, rap: 4, @int: 3, eru: 2, cha: 3, ins: 3, mag: 5);
            var sheet = new CharacterSheet { Name = "Mina", BaseAttributes = attr, AvailableXP = 20 };

            Assert.IsTrue(CharacterProgressionManager.IsVolume2Specialization("Barricade de Racines"));
            Assert.IsFalse(CharacterProgressionManager.IsVolume2Specialization("Teep de Rupture"));

            bool unlockedWithoutFlag = CharacterProgressionManager.UnlockSpecialization(sheet, "Barricade de Racines", out string msgLock);
            Assert.IsFalse(unlockedWithoutFlag);
            StringAssert.Contains("VERROU CAUSAL", msgLock);

            bool unlockedPhase1 = CharacterProgressionManager.UnlockSpecialization(sheet, "Teep de Rupture", out string msgPhase1);
            Assert.IsTrue(unlockedPhase1, msgPhase1);
            Assert.IsTrue(sheet.UnlockedSpecializations.Contains("Teep de Rupture"));

            // Sous-amélioration : impossible d'acquérir sans la spécialisation parente
            bool subWithoutParent = CharacterProgressionManager.UnlockSpecialization(sheet, "Maniement de l'Épée : Riposte Éclair", out string msgNoParent);
            Assert.IsFalse(subWithoutParent);
            StringAssert.Contains("requiert la spécialisation parente", msgNoParent);

            // Acquisition de la spécialisation parente puis de son amélioration
            sheet.AvailableXP = 20;
            bool parentUnlocked = CharacterProgressionManager.UnlockSpecialization(sheet, "Maniement de l'Épée", out _);
            Assert.IsTrue(parentUnlocked);

            bool subWithParent = CharacterProgressionManager.UnlockSpecialization(sheet, "Maniement de l'Épée : Riposte Éclair", out string msgWithParent);
            Assert.IsTrue(subWithParent, msgWithParent);
            Assert.IsTrue(sheet.UnlockedSpecializations.Contains("Maniement de l'Épée : Riposte Éclair"));
        }

        [Test]
        public void TestWeaponSpecializationTrees_AxeHastBow()
        {
            var sheet = new CharacterSheet { Name = "Roger", AvailableXP = 40 };

            // Sources de compétence : Hache → Maniement, Hast → Perçantes, Arc → Ballistique.
            Assert.IsTrue(CharacterProgressionManager.TryGetSpecializationSource("Hache de Guerre", out var axeSrc));
            Assert.AreEqual(SkillType.ManiementArmes, axeSrc);
            Assert.IsTrue(CharacterProgressionManager.TryGetSpecializationSource("Hache de Guerre : Brise-Garde", out var axeSub));
            Assert.AreEqual(SkillType.ManiementArmes, axeSub);
            Assert.IsTrue(CharacterProgressionManager.TryGetSpecializationSource("Arme de Hast : Mur de Piques", out var hastSrc));
            Assert.AreEqual(SkillType.ArmesPercantes, hastSrc);
            Assert.IsTrue(CharacterProgressionManager.TryGetSpecializationSource("Tir à l'Arc : Tir en Cloche", out var bowSrc));
            Assert.AreEqual(SkillType.Ballistique, bowSrc);

            // Filiation des améliorations.
            Assert.IsTrue(CharacterProgressionManager.TryGetParentSpecialization("Hache de Guerre : Fente du Bûcheron", out var axeParent));
            Assert.AreEqual("Hache de Guerre", axeParent);
            Assert.IsTrue(CharacterProgressionManager.TryGetParentSpecialization("Arme de Hast : Phalange d'Acier", out var hastParent));
            Assert.AreEqual("Arme de Hast : Mur de Piques", hastParent);
            Assert.IsTrue(CharacterProgressionManager.TryGetParentSpecialization("Tir à l'Arc : Pluie d'Acier", out var bowParent));
            Assert.AreEqual("Tir à l'Arc : Tir en Cloche", bowParent);

            // Prérequis : pas de Brise-Garde sans Fente du Bûcheron.
            Assert.IsFalse(CharacterProgressionManager.UnlockSpecialization(sheet, "Hache de Guerre : Brise-Garde", out string noParent));
            StringAssert.Contains("requiert la spécialisation parente", noParent);

            // Chaîne complète Hache (5 XP par maîtrise, payés en XP libre).
            Assert.IsTrue(CharacterProgressionManager.UnlockSpecialization(sheet, "Hache de Guerre", out _));
            Assert.IsTrue(CharacterProgressionManager.UnlockSpecialization(sheet, "Hache de Guerre : Fente du Bûcheron", out _));
            Assert.IsTrue(CharacterProgressionManager.UnlockSpecialization(sheet, "Hache de Guerre : Brise-Garde", out string ok));
            Assert.IsTrue(sheet.UnlockedSpecializations.Contains("Hache de Guerre : Brise-Garde"), ok);

            // Parade : la Hache compte comme spécialisation défensive du Maniement d'Arme.
            Assert.IsTrue(SkillDefinitions.HasDefensiveSpecialization(sheet, SkillType.ManiementArmes));
        }

        [Test]
        public void TestAthleticsBranch_CourseAndCrossing()
        {
            var sheet = new CharacterSheet { Name = "Roger", AvailableXP = 40 };

            // Sources : tout l'arbre relève de l'Athlétisme.
            Assert.IsTrue(CharacterProgressionManager.TryGetSpecializationSource("Course d'Endurance", out var courseSrc));
            Assert.AreEqual(SkillType.Athletisme, courseSrc);
            Assert.IsTrue(CharacterProgressionManager.TryGetSpecializationSource("Course d'Endurance : Cœur de Marathon", out var heartSrc));
            Assert.AreEqual(SkillType.Athletisme, heartSrc);
            Assert.IsTrue(CharacterProgressionManager.TryGetSpecializationSource("Franchissement : Nage de Combat", out var swimSrc));
            Assert.AreEqual(SkillType.Athletisme, swimSrc);

            // Filiation et secret du capstone.
            Assert.IsTrue(CharacterProgressionManager.TryGetParentSpecialization("Course d'Endurance : Second Souffle", out var breathParent));
            Assert.AreEqual("Course d'Endurance", breathParent);
            Assert.IsTrue(CharacterProgressionManager.TryGetParentSpecialization("Franchissement : Escalade Assurée", out var climbParent));
            Assert.AreEqual("Franchissement", climbParent);
            Assert.IsTrue(CharacterProgressionManager.IsHiddenSpecialization("Course d'Endurance : Cœur de Marathon"));
            Assert.IsFalse(CharacterProgressionManager.IsHiddenSpecialization("Franchissement"));

            // Prérequis : pas de Second Souffle sans Course d'Endurance.
            Assert.IsFalse(CharacterProgressionManager.UnlockSpecialization(sheet, "Course d'Endurance : Second Souffle", out string noParent));
            StringAssert.Contains("requiert la spécialisation parente", noParent);

            // Chaîne complète (5 XP par maîtrise, payés en XP libre).
            Assert.IsTrue(CharacterProgressionManager.UnlockSpecialization(sheet, "Course d'Endurance", out _));
            Assert.IsTrue(CharacterProgressionManager.UnlockSpecialization(sheet, "Course d'Endurance : Second Souffle", out string ok));
            Assert.IsTrue(sheet.UnlockedSpecializations.Contains("Course d'Endurance : Second Souffle"), ok);
            Assert.IsTrue(CharacterProgressionManager.UnlockSpecialization(sheet, "Franchissement", out _));
            Assert.IsTrue(sheet.UnlockedSpecializations.Contains("Franchissement"));
        }

        [Test]
        public void TestEnduranceAndIntuitionBranches_RegistryAndUnlocks()
        {
            var sheet = new CharacterSheet { Name = "Roger", AvailableXP = 40 };

            // Sources : Endurance Physique vs Intuition.
            Assert.IsTrue(CharacterProgressionManager.TryGetSpecializationSource("Condition de Fer : Mur de Chair", out var wallSrc));
            Assert.AreEqual(SkillType.EndurancePhysique, wallSrc);
            Assert.IsTrue(CharacterProgressionManager.TryGetSpecializationSource("Trempe de Fer : Esprit de Granit", out var gritSrc));
            Assert.AreEqual(SkillType.EndurancePhysique, gritSrc);
            Assert.IsTrue(CharacterProgressionManager.TryGetSpecializationSource("Sens du Danger : Premiers Réflexes", out var reflexSrc));
            Assert.AreEqual(SkillType.Intuition, reflexSrc);
            Assert.IsTrue(CharacterProgressionManager.TryGetSpecializationSource("Instinct du Chasseur : Piégeur", out var trapSrc));
            Assert.AreEqual(SkillType.Intuition, trapSrc);

            // Filiation et secrets des capstones.
            Assert.IsTrue(CharacterProgressionManager.TryGetParentSpecialization("Condition de Fer : Dur à Cuire", out var toughParent));
            Assert.AreEqual("Condition de Fer", toughParent);
            Assert.IsTrue(CharacterProgressionManager.TryGetParentSpecialization("Trempe de Fer : Ignorer la Douleur", out var painParent));
            Assert.AreEqual("Trempe de Fer", painParent);
            Assert.IsTrue(CharacterProgressionManager.IsHiddenSpecialization("Condition de Fer : Mur de Chair"));
            Assert.IsTrue(CharacterProgressionManager.IsHiddenSpecialization("Sens du Danger : Clairvoyance du Vétéran"));
            Assert.IsFalse(CharacterProgressionManager.IsHiddenSpecialization("Trempe de Fer"));

            // Prérequis : pas de Mur de Chair sans Dur à Cuire.
            Assert.IsFalse(CharacterProgressionManager.UnlockSpecialization(sheet, "Condition de Fer : Mur de Chair", out string noParent));
            StringAssert.Contains("requiert la spécialisation parente", noParent);

            // Chaînes complètes (5 XP par maîtrise, payés en XP libre).
            Assert.IsTrue(CharacterProgressionManager.UnlockSpecialization(sheet, "Condition de Fer", out _));
            Assert.IsTrue(CharacterProgressionManager.UnlockSpecialization(sheet, "Condition de Fer : Dur à Cuire", out _));
            Assert.IsTrue(CharacterProgressionManager.UnlockSpecialization(sheet, "Condition de Fer : Mur de Chair", out string wallOk));
            Assert.IsTrue(sheet.UnlockedSpecializations.Contains("Condition de Fer : Mur de Chair"), wallOk);
            Assert.IsTrue(CharacterProgressionManager.UnlockSpecialization(sheet, "Sens du Danger", out _));
            Assert.IsTrue(CharacterProgressionManager.UnlockSpecialization(sheet, "Sens du Danger : Premiers Réflexes", out string reflexOk));
            Assert.IsTrue(sheet.UnlockedSpecializations.Contains("Sens du Danger : Premiers Réflexes"), reflexOk);

            // Effets mécaniques : Sens du Danger intègre l'Instinct à l'initiative.
            // RAP 5, AGI 4, INT 2, INS 8 → sans la spé : base 5 (D6) ; avec : base 8 (D8, D10 avec Premiers Réflexes).
            var keenAttr = new Attributes(@for: 4, agi: 4, con: 4, rap: 5, @int: 2, eru: 2, cha: 2, ins: 8);
            var plain = new CharacterSheet { Name = "Plaine", BaseAttributes = keenAttr };
            Assert.AreEqual(5, plain.ToCombatStats().GetInitiativeBaseValue());
            Assert.AreEqual(DiceType.D6, plain.ToCombatStats().GetInitiativeDie());

            var scout = new CharacterSheet { Name = "Éclaireur", BaseAttributes = keenAttr, AvailableXP = 40 };
            Assert.IsTrue(CharacterProgressionManager.UnlockSpecialization(scout, "Sens du Danger", out _));
            Assert.AreEqual(8, scout.ToCombatStats().GetInitiativeBaseValue());
            Assert.IsTrue(CharacterProgressionManager.UnlockSpecialization(scout, "Sens du Danger : Premiers Réflexes", out string reflexOk2));
            Assert.IsTrue(scout.UnlockedSpecializations.Contains("Sens du Danger : Premiers Réflexes"), reflexOk2);
            Assert.AreEqual(DiceType.D10, scout.ToCombatStats().GetInitiativeDie());
        }

        [Test]
        public void TestExhaustiveSpecializationTree_BranchingAndCheatUnlocks()
        {
            var sheet = new CharacterSheet { AvailableXP = 0 };

            // Vérification de la complétude du catalogue
            var allSpecs = new System.Collections.Generic.List<CharacterProgressionManager.SpecializationDetail>(CharacterProgressionManager.GetAllSpecializations());
            Assert.GreaterOrEqual(allSpecs.Count, 65, "L'arborescence doit comporter au moins 65 maîtrises et spécialisations réparties sur les 7 Piliers.");

            // Échec du déblocage normal d'un secret sans XP et sans prérequis
            bool normalUnlock = CharacterProgressionManager.UnlockSpecialization(sheet, "Arts Martiaux : Paume de Brum'korath", out string failMsg);
            Assert.IsFalse(normalUnlock);
            StringAssert.Contains("Prérequis", failMsg);

            // Déblocage immédiat via le mode Dev Cheat
            bool cheatUnlock = CharacterProgressionManager.UnlockSpecialization(sheet, "Arts Martiaux : Paume de Brum'korath", out string cheatMsg, forceFree: true);
            Assert.IsTrue(cheatUnlock);
            Assert.IsTrue(sheet.UnlockedSpecializations.Contains("Arts Martiaux : Paume de Brum'korath"));
            StringAssert.Contains("[CHEAT]", cheatMsg);

            // Vérification de la détection des secrets cachés
            Assert.IsTrue(CharacterProgressionManager.IsHiddenSpecialization("Maniement de l'Épée : Lame de Ligne Temporelle Zéro"));
            Assert.IsFalse(CharacterProgressionManager.IsHiddenSpecialization("Arts Martiaux : Enchaînement Fluide"));
        }

        [Test]
        public void TestMina_ExclusivePrimordialMagic_And_BlockedOtherMagic()
        {
            var minaSheet = new CharacterSheet
            {
                Name = "Mina",
                ModelPrefabName = "Mina",
                AvailableXP = 40
            };

            var defaultSheet = new CharacterSheet
            {
                Name = "Roger",
                AvailableXP = 40
            };

            Assert.IsTrue(CharacterProgressionManager.IsMina(minaSheet));
            Assert.IsFalse(CharacterProgressionManager.IsMina(defaultSheet));

            // Mina n'a pas accès à la Magie Élémentale ni à la Magie de l'Esprit
            Assert.IsTrue(CharacterProgressionManager.IsSkillAccessible(minaSheet, SkillType.MagiePrimale));
            Assert.IsFalse(CharacterProgressionManager.IsSkillAccessible(minaSheet, SkillType.MagieElementale));
            Assert.IsFalse(CharacterProgressionManager.IsSkillAccessible(minaSheet, SkillType.MagieEsprit));

            bool trainElem = CharacterProgressionManager.TrainSkill(minaSheet, SkillType.MagieElementale, out string msgElem);
            Assert.IsFalse(trainElem);
            StringAssert.Contains("RESTRICTION D'ÂME", msgElem);

            bool trainEsprit = CharacterProgressionManager.TrainSkill(minaSheet, SkillType.MagieEsprit, out string msgEsprit);
            Assert.IsFalse(trainEsprit);
            StringAssert.Contains("RESTRICTION D'ÂME", msgEsprit);

            bool unlockElemSpec = CharacterProgressionManager.UnlockSpecialization(minaSheet, "Incinération Pyrocinétique", out string msgSpecElem);
            Assert.IsFalse(unlockElemSpec);
            StringAssert.Contains("RESTRICTION D'ÂME", msgSpecElem);

            // Le personnage par défaut conserve l'accès complet
            Assert.IsTrue(CharacterProgressionManager.IsSkillAccessible(defaultSheet, SkillType.MagieElementale));
            Assert.IsTrue(CharacterProgressionManager.IsSkillAccessible(defaultSheet, SkillType.MagieEsprit));

            // Les maîtrises exclusives de Mina sont bloquées pour le personnage par défaut
            bool defaultMinaSpec = CharacterProgressionManager.UnlockSpecialization(defaultSheet, "Éveil Chlorophyllien", out string msgDefMina);
            Assert.IsFalse(defaultMinaSpec);
            StringAssert.Contains("AFFINITÉ BIOLOGIQUE EXCLUSIVE", msgDefMina);

            // Mina peut débloquer ses maîtrises primordiales étendues (Volume II)
            bool minaPrimale = CharacterProgressionManager.UnlockSpecialization(minaSheet, "Barricade de Racines", out _, forceFree: true);
            Assert.IsTrue(minaPrimale);
            Assert.IsTrue(minaSheet.UnlockedSpecializations.Contains("Barricade de Racines"));
        }
    }
}
#endif
#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using Killtime.Core.Character;
using Killtime.Core.Combat;
using Killtime.Core.Dice;
using Killtime.Core.Rules;
using Killtime.Tactics.Grid;

namespace Killtime.Tests
{
    /// <summary>
    /// RD-038 : Charge / sprint + attaque (Livre VI §Manœuvres Mobiles).
    /// - Charge 3+ cases : +2 dégâts d'impact sur frappe au contact (mêlée).
    /// - Malus défensif : -1 en défense jusqu'au début du prochain tour personnel.
    /// - Sprint : tout déplacement de 3+ cases bloque tout tir à distance ce tour.
    /// - Intégration HexPathfinder (IsChargePath, IsChargeDistance, FindChargePath).
    /// </summary>
    [TestFixture]
    public class ChargeSprintTests
    {
        private static CharacterStats MakeStats(string name, int force = 5, int agi = 4, int con = 5, int ap = 6, int hp = 20)
        {
            var attr = new Attributes(force, agi, con, 3, 3, 2, 2, 2);
            var sheet = new CharacterSheet
            {
                Name = name,
                BaseAttributes = attr,
                UnlockedSpecializations = new List<string>()
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
            ChargeState.ClearAll();
            CoreRulesConfig.Instance.ResetToCodexDefaults();
        }

        [TearDown]
        public void TearDown()
        {
            ChargeState.ClearAll();
            CoreRulesConfig.Instance.ResetToCodexDefaults();
        }

        [Test]
        public void ChargeState_MovementAtOrAboveThreshold_TriggersSprintAndCharge()
        {
            var unit = MakeStats("Assaillant");

            Assert.IsFalse(ChargeState.HasSprinted(unit));
            Assert.IsTrue(ChargeState.CanFireRanged(unit));
            Assert.IsFalse(ChargeState.HasChargeBonus(unit));
            Assert.IsFalse(ChargeState.HasDefensePenalty(unit));
            Assert.AreEqual(0, ChargeState.GetDefenseModifier(unit));

            // Déplacement de 3 cases (seuil atteint)
            bool triggered = ChargeState.ApplyMovement(unit, 3);
            Assert.IsTrue(triggered);

            Assert.IsTrue(ChargeState.HasSprinted(unit), "Déplacement 3+ cases doit marquer comme ayant sprinté.");
            Assert.IsFalse(ChargeState.CanFireRanged(unit), "Le sprint doit bloquer le tir à distance ce tour.");
            Assert.IsTrue(ChargeState.HasChargeBonus(unit), "La charge 3+ cases doit conférer le bonus d'attaque.");
            Assert.IsTrue(ChargeState.HasDefensePenalty(unit), "La charge doit appliquer le malus défensif.");
            Assert.AreEqual(-1, ChargeState.GetDefenseModifier(unit), "Modificateur défensif doit être -1.");
        }

        [Test]
        public void ChargeState_MovementBelowThreshold_DoesNotTrigger()
        {
            var unit = MakeStats("Marcheur");

            // Déplacement de 1 case
            bool step1 = ChargeState.ApplyMovement(unit, 1);
            Assert.IsFalse(step1);
            Assert.IsFalse(ChargeState.HasSprinted(unit));
            Assert.IsTrue(ChargeState.CanFireRanged(unit));
            Assert.IsFalse(ChargeState.HasChargeBonus(unit));
            Assert.IsFalse(ChargeState.HasDefensePenalty(unit));

            // Déplacement de 2 cases
            bool step2 = ChargeState.ApplyMovement(unit, 2);
            Assert.IsFalse(step2);
            Assert.IsFalse(ChargeState.HasSprinted(unit));
            Assert.IsTrue(ChargeState.CanFireRanged(unit));
            Assert.IsFalse(ChargeState.HasChargeBonus(unit));
            Assert.IsFalse(ChargeState.HasDefensePenalty(unit));
        }

        [Test]
        public void ChargeAttack_GrantsPlusTwoDamage_AndConsumesBonus()
        {
            // Déterminisme : attaquant 8, défenseur 4
            var roller = new MockDiceRoller(8, 4);
            var calc = new CombatCalculator(roller);

            var attacker = MakeStats("Lancier", force: 5);
            var defender = MakeStats("Cible", con: 5, hp: 25);

            // Applique une charge de 3 cases
            ChargeState.ApplyMovement(attacker, 3);
            Assert.IsTrue(ChargeState.HasChargeBonus(attacker));

            // Attaque standard mains nues
            var duel = calc.BeginSkillDuel(
                attacker, defender, BodyPart.Torse,
                SkillType.MainsNues, SkillType.Esquive,
                weaponBaseDamage: 4, cancelPenaltyWithAP: false,
                defenderWantsToDefend: true, attackerSpecialization: null, defenderSpecialization: null,
                defenderArmor: 0, out string error);

            Assert.IsNotNull(duel, error);
            calc.DeclareAttackerStakes(duel, 0, 0);
            calc.DeclareDefenderStakes(duel, SkillType.Esquive, true, 0, 0);

            var result = calc.ResolveBlindDuel(duel);

            Assert.IsTrue(result.IsHit);
            // Dégâts bruts attendus : arme 4 + diff (att 8+mod vs def 4+mod-1) + charge (+2)
            Assert.IsTrue(result.CombatLog.Contains("⚡ Charge"), "Le log de combat doit mentionner le bonus de charge.");
            Assert.IsFalse(ChargeState.HasChargeBonus(attacker), "Le bonus d'attaque de charge doit être consommé.");
            Assert.IsTrue(ChargeState.HasDefensePenalty(attacker), "Le malus défensif doit persister après l'attaque.");
        }

        [Test]
        public void ChargeDefensePenalty_PersistsUntilNextPersonalTurn()
        {
            var charger = MakeStats("Chargeur");
            ChargeState.ApplyMovement(charger, 3);

            Assert.IsTrue(ChargeState.HasDefensePenalty(charger));
            Assert.AreEqual(-1, ChargeState.GetDefenseModifier(charger));

            // Tour personnel suivant de l'unité
            ChargeState.OnUnitTurnStart(charger);

            Assert.IsFalse(ChargeState.HasDefensePenalty(charger), "Le malus défensif doit être effacé au début du prochain tour.");
            Assert.IsFalse(ChargeState.HasSprinted(charger), "L'interdiction de tir doit être levée au nouveau tour.");
            Assert.IsTrue(ChargeState.CanFireRanged(charger));
            Assert.AreEqual(0, ChargeState.GetDefenseModifier(charger));
        }

        [Test]
        public void Sprint_BlocksRangedShot_InCombatCalculator()
        {
            var shooter = MakeStats("Tireur");
            var target = MakeStats("Cible");
            var calc = new CombatCalculator(new DiceRoller(5));

            // Avant sprint : tir autorisé
            var duelBefore = calc.BeginSkillDuel(
                shooter, target, BodyPart.Torse,
                SkillType.Ballistique, SkillType.Esquive,
                weaponBaseDamage: 5, cancelPenaltyWithAP: false,
                defenderWantsToDefend: true, attackerSpecialization: null, defenderSpecialization: null,
                defenderArmor: 0, out string errBefore);
            Assert.IsNotNull(duelBefore, errBefore);

            // Réinitialise les PA pour le test suivant
            shooter.ResetTurn();

            // L'unité sprinte (3 cases)
            ChargeState.ApplyMovement(shooter, 3);
            Assert.IsTrue(ChargeState.HasSprinted(shooter));
            Assert.IsFalse(ChargeState.CanFireRanged(shooter));

            // Tentative de tir après sprint : rejeté
            var duelAfter = calc.BeginSkillDuel(
                shooter, target, BodyPart.Torse,
                SkillType.Ballistique, SkillType.Esquive,
                weaponBaseDamage: 5, cancelPenaltyWithAP: false,
                defenderWantsToDefend: true, attackerSpecialization: null, defenderSpecialization: null,
                defenderArmor: 0, out string errAfter);

            Assert.IsNull(duelAfter);
            Assert.IsTrue(errAfter.Contains("sprinté"), $"Message d'erreur attendu pour tir bloqué, reçu : {errAfter}");

            // En revanche, une frappe de contact reste autorisée
            var meleeDuel = calc.BeginSkillDuel(
                shooter, target, BodyPart.Torse,
                SkillType.MainsNues, SkillType.Esquive,
                weaponBaseDamage: 4, cancelPenaltyWithAP: false,
                defenderWantsToDefend: true, attackerSpecialization: null, defenderSpecialization: null,
                defenderArmor: 0, out string meleeErr);

            Assert.IsNotNull(meleeDuel, meleeErr);
        }

        [Test]
        public void HexPathfinder_IsChargePath_And_IsChargeDistance()
        {
            var p0 = new HexCoordinates(0, 0);
            var p1 = new HexCoordinates(1, 0);
            var p2 = new HexCoordinates(2, 0);
            var p3 = new HexCoordinates(3, 0);

            // Trajet de 3 cases (4 coordonnées dans la liste)
            var path3 = new List<HexCoordinates> { p0, p1, p2, p3 };
            Assert.IsTrue(HexPathfinder.IsChargePath(path3, 3));

            // Trajet de 2 cases (3 coordonnées dans la liste)
            var path2 = new List<HexCoordinates> { p0, p1, p2 };
            Assert.IsFalse(HexPathfinder.IsChargePath(path2, 3));

            // Trajet vide ou 1 seule case
            Assert.IsFalse(HexPathfinder.IsChargePath(null, 3));
            Assert.IsFalse(HexPathfinder.IsChargePath(new List<HexCoordinates> { p0 }, 3));

            // Test de distance
            Assert.IsTrue(HexPathfinder.IsChargeDistance(p0, p3, 3));
            Assert.IsFalse(HexPathfinder.IsChargeDistance(p0, p2, 3));
        }

        [Test]
        public void CharacterStats_Integration_TracksAndResetsState()
        {
            var stats = MakeStats("Roger", ap: 8);

            Assert.IsFalse(stats.HasSprintedThisTurn);
            Assert.IsFalse(stats.HasChargeBonus);
            Assert.IsFalse(stats.HasChargeDefensePenalty);

            // Déplacement de 4 cases via RegisterMove
            stats.RegisterMove(4);

            Assert.IsTrue(stats.HasSprintedThisTurn);
            Assert.IsTrue(stats.HasChargeBonus);
            Assert.IsTrue(stats.HasChargeDefensePenalty);
            Assert.AreEqual(4, stats.TilesMovedThisTurn);
            Assert.AreEqual(1, stats.MovesThisTurn);

            // ResetTurn (début de tour suivant)
            stats.ResetTurn();

            Assert.IsFalse(stats.HasSprintedThisTurn);
            Assert.IsFalse(stats.HasChargeBonus);
            Assert.IsFalse(stats.HasChargeDefensePenalty);
            Assert.AreEqual(0, stats.TilesMovedThisTurn);
            Assert.AreEqual(0, stats.MovesThisTurn);
        }

        [Test]
        public void CoreRulesConfig_CustomizableParameters()
        {
            CoreRulesConfig.Instance.ChargeMinDistance = 4;
            CoreRulesConfig.Instance.ChargeDamageBonus = 3;
            CoreRulesConfig.Instance.ChargeDefensePenalty = -2;

            var unit = MakeStats("Testeur");

            // 3 cases : ne déclenche plus car le seuil est passé à 4
            ChargeState.ApplyMovement(unit, 3);
            Assert.IsFalse(ChargeState.HasChargeBonus(unit));

            // 4 cases : déclenche avec la nouvelle configuration
            ChargeState.ApplyMovement(unit, 4);
            Assert.IsTrue(ChargeState.HasChargeBonus(unit));
            Assert.AreEqual(-2, ChargeState.GetDefenseModifier(unit));
        }

        [Test]
        public void ChargeAttack_WhenBlocked_StillConsumesChargeBonus()
        {
            // Attaquant 2, Défenseur 8 -> échec / parade nette (differential < 0)
            var roller = new MockDiceRoller(2, 8);
            var calc = new CombatCalculator(roller);

            var attacker = MakeStats("Chargeur", force: 4);
            var defender = MakeStats("Défenseur", con: 5, hp: 20);

            ChargeState.ApplyMovement(attacker, 3);
            Assert.IsTrue(ChargeState.HasChargeBonus(attacker));

            var duel = calc.BeginSkillDuel(
                attacker, defender, BodyPart.Torse,
                SkillType.MainsNues, SkillType.Esquive,
                weaponBaseDamage: 3, cancelPenaltyWithAP: false,
                defenderWantsToDefend: true, attackerSpecialization: null, defenderSpecialization: null,
                defenderArmor: 0, out string error);

            Assert.IsNotNull(duel, error);
            calc.DeclareAttackerStakes(duel, 0, 0);
            calc.DeclareDefenderStakes(duel, SkillType.Esquive, true, 0, 0);

            var result = calc.ResolveBlindDuel(duel);

            Assert.IsFalse(result.IsHit);
            Assert.IsTrue(result.IsBlocked);
            // L'élan de la charge a été consommé sur l'assaut malgré la parade
            Assert.IsFalse(ChargeState.HasChargeBonus(attacker), "Le bonus d'attaque doit être consommé même si l'attaque a été parée/esquivée.");
            // Le malus défensif persiste
            Assert.IsTrue(ChargeState.HasDefensePenalty(attacker));
        }

        private class MockDiceRoller : DiceRoller
        {
            private readonly int _att;
            private readonly int _def;
            private bool _isFirst = true;

            public MockDiceRoller(int att, int def) : base(0)
            {
                _att = att;
                _def = def;
            }

            public override DiceRollResult Roll(DiceType dieType, int modifier = 0, int targetDC = 10)
            {
                int val = _isFirst ? _att : _def;
                _isFirst = !_isFirst;

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

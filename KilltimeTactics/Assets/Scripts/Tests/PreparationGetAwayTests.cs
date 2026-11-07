#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEngine;
using Killtime.Core.Character;
using Killtime.Core.Combat;
using Killtime.Core.Dice;

namespace Killtime.Tests
{
    /// <summary>
    /// RD-202 Préparation + Dégagement / GetAway (Livre V §20.3) :
    /// - Préparation (1 PA) : +2 au jet de l'action OU de la défense suivante,
    ///   one-shot consommée au jet suivant (attaque comme parade active).
    /// - Dégagement : action principale (attaque OU parade résolue) suivie
    ///   d'une fuite à +1 PA (surcharge, trajet payé à part).
    /// Anti-doublons : pas de cumul avec la canalisation RD-048 (exclusion
    /// mutuelle), fuite DISTINCTE du Décrochage préventif RD-031 (provoque).
    /// </summary>
    [TestFixture]
    public class PreparationGetAwayTests
    {
        private static CharacterStats MakeStats(string name, int con = 3, int ap = 6)
        {
            var attr = new Attributes(4, 4, con, 3, 3, 2, 2, 2);
            var sheet = new CharacterSheet { Name = name, BaseAttributes = attr };
            var stats = sheet.ToCombatStats();
            stats.Sheet = sheet;
            stats.CurrentActionPoints = ap;
            return stats;
        }

        [SetUp]
        public void SetUp()
        {
            Killtime.Core.Rules.CoreRulesConfig.Instance.ResetToCodexDefaults();
            PreparationState.ClearAll();
            GetAwayState.ClearAll();
            ChannelingState.ClearAll();
            OpportunityState.ClearAll();
        }

        [TearDown]
        public void TearDown()
        {
            PreparationState.ClearAll();
            GetAwayState.ClearAll();
            ChannelingState.ClearAll();
            OpportunityState.ClearAll();
            Killtime.Core.Rules.CoreRulesConfig.Instance.ResetToCodexDefaults();
        }

        // ================= PRÉPARATION =================

        [Test]
        public void Prep_BeginCostsPA_ArmsOneShotBonus()
        {
            var unit = MakeStats("Prêt", ap: 6);
            var calc = new CombatCalculator(new DiceRoller(7));

            Assert.IsTrue(calc.TryBeginPreparation(unit, out string error), error);
            Assert.IsTrue(PreparationState.IsPrepared(unit));
            Assert.AreEqual(5, unit.CurrentActionPoints);
            Assert.AreEqual(2, PreparationState.PeekBonus(unit));
        }

        [Test]
        public void Prep_RefusedWhenAlreadyPrepared()
        {
            var unit = MakeStats("Prêt");
            var calc = new CombatCalculator(new DiceRoller(7));

            Assert.IsTrue(calc.TryBeginPreparation(unit, out _));
            Assert.IsFalse(calc.TryBeginPreparation(unit, out string error));
            StringAssert.Contains("déjà", error);
        }

        [Test]
        public void Prep_RefusedWithoutPA()
        {
            var unit = MakeStats("Prêt", ap: 0);
            var calc = new CombatCalculator(new DiceRoller(7));

            Assert.IsFalse(calc.TryBeginPreparation(unit, out string error));
            StringAssert.Contains("PA", error);
            Assert.IsFalse(PreparationState.IsPrepared(unit));
        }

        [Test]
        public void Prep_NoStackWithChanneling_BothWays()
        {
            var caster = MakeStats("Lanceur");
            var calc = new CombatCalculator(new DiceRoller(7));

            // Canalisation d'abord -> préparation refusée (pas de cumul).
            Assert.IsTrue(calc.TryBeginChanneling(caster, "Sort", out _));
            Assert.IsFalse(calc.TryBeginPreparation(caster, out string error));
            StringAssert.Contains("cumule", error);
            Assert.IsFalse(PreparationState.IsPrepared(caster));

            var other = MakeStats("Autre");
            Assert.IsTrue(calc.TryBeginPreparation(other, out _));
            Assert.IsFalse(calc.TryBeginChanneling(other, "Sort", out string channelError));
            StringAssert.Contains("préparé", channelError);
        }

        [Test]
        public void Prep_AttackBonusAppliedThenConsumed_Logged()
        {
            var attacker = MakeStats("Assaillant");
            var defender = MakeStats("Cible");
            var calc = new CombatCalculator(new QueueDiceRoller(8, 3));
            Assert.IsTrue(calc.TryBeginPreparation(attacker, out _));

            var duel = calc.BeginSkillDuel(
                attacker, defender, BodyPart.Torse,
                SkillType.MainsNues, SkillType.Esquive,
                5, false, true, null, null, 0, out string error);
            Assert.IsNotNull(duel, error);
            Assert.IsTrue(duel.IsPreparationAttack);
            Assert.AreEqual(2, duel.PreparationAttackBonus);

            calc.DeclareAttackerStakes(duel, 0, 0);
            calc.AutoDeclareDefenderStakes(duel);
            var result = calc.ResolveBlindDuel(duel);

            // One-shot : bonus consommé au jet suivant, même paré.
            Assert.IsFalse(PreparationState.IsPrepared(attacker));
            StringAssert.Contains("Préparation", result.CombatLog);
        }

        [Test]
        public void Prep_DefenseBonusAppliedThenConsumed_Logged()
        {
            var attacker = MakeStats("Assaillant");
            var defender = MakeStats("Mur");
            var calc = new CombatCalculator(new QueueDiceRoller(2, 9));
            Assert.IsTrue(calc.TryBeginPreparation(defender, out _));

            var duel = calc.BeginSkillDuel(
                attacker, defender, BodyPart.Torse,
                SkillType.MainsNues, SkillType.Esquive,
                5, false, true, null, null, 0, out string error);
            Assert.IsNotNull(duel, error);
            Assert.IsTrue(duel.IsPreparationDefense);
            Assert.AreEqual(2, duel.PreparationDefenseBonus);

            calc.DeclareAttackerStakes(duel, 0, 0);
            calc.AutoDeclareDefenderStakes(duel);
            var result = calc.ResolveBlindDuel(duel);

            Assert.IsFalse(PreparationState.IsPrepared(defender));
            StringAssert.Contains("Préparation", result.CombatLog);
        }

        [Test]
        public void Prep_PassiveSoakPreservesBonus()
        {
            // Encaissement passif (0 PA, aucun jet) : la préparation survit.
            var attacker = MakeStats("Assaillant");
            var defender = MakeStats("Passif");
            var calc = new CombatCalculator(new QueueDiceRoller(9, 0));
            Assert.IsTrue(calc.TryBeginPreparation(defender, out _));

            var result = calc.ResolveTargetedAttack(
                attacker: attacker, defender: defender,
                targetedPart: BodyPart.Torse,
                attackSkill: SkillType.MainsNues, defenseSkill: SkillType.Esquive,
                weaponBaseDamage: 5,
                cancelPenaltyWithAP: false, defenderWantsToDefend: false);

            Assert.IsTrue(result.IsHit);
            Assert.IsTrue(PreparationState.IsPrepared(defender));
        }

        [Test]
        public void Prep_ExpiresOnOwnerTurnStart()
        {
            var unit = MakeStats("Prêt");
            var calc = new CombatCalculator(new DiceRoller(7));
            Assert.IsTrue(calc.TryBeginPreparation(unit, out _));

            PreparationState.OnUnitTurnStart(unit);
            Assert.IsFalse(PreparationState.IsPrepared(unit));
        }

        [Test]
        public void Prep_ConfigCustomBonusApplies()
        {
            Killtime.Core.Rules.CoreRulesConfig.Instance.PreparationBonus = 3;
            var attacker = MakeStats("Assaillant");
            var defender = MakeStats("Cible");
            var calc = new CombatCalculator(new QueueDiceRoller(8, 3));
            Assert.IsTrue(calc.TryBeginPreparation(attacker, out _));

            var duel = calc.BeginSkillDuel(
                attacker, defender, BodyPart.Torse,
                SkillType.MainsNues, SkillType.Esquive,
                5, false, true, null, null, 0, out string error);
            Assert.IsNotNull(duel, error);
            Assert.AreEqual(3, duel.PreparationAttackBonus);
        }

        // ================= DÉGAGEMENT / GETAWAY =================

        [Test]
        public void GetAway_WindowOpenedByResolvedDuel_AttackerAndActiveDefender()
        {
            var attacker = MakeStats("Assaillant");
            var defender = MakeStats("Pareur");
            var calc = new CombatCalculator(new QueueDiceRoller(8, 3));

            var result = calc.ResolveTargetedAttack(
                attacker: attacker, defender: defender,
                targetedPart: BodyPart.Torse,
                attackSkill: SkillType.MainsNues, defenseSkill: SkillType.Esquive,
                weaponBaseDamage: 5,
                cancelPenaltyWithAP: false, defenderWantsToDefend: true);

            Assert.IsTrue(GetAwayState.HasWindow(attacker));
            Assert.IsTrue(GetAwayState.HasWindow(defender));
        }

        [Test]
        public void GetAway_PassiveDefenderGetsNoWindow()
        {
            var attacker = MakeStats("Assaillant");
            var defender = MakeStats("Passif");
            var calc = new CombatCalculator(new QueueDiceRoller(8, 0));

            calc.ResolveTargetedAttack(
                attacker: attacker, defender: defender,
                targetedPart: BodyPart.Torse,
                attackSkill: SkillType.MainsNues, defenseSkill: SkillType.Esquive,
                weaponBaseDamage: 5,
                cancelPenaltyWithAP: false, defenderWantsToDefend: false);

            Assert.IsTrue(GetAwayState.HasWindow(attacker));
            Assert.IsFalse(GetAwayState.HasWindow(defender));
        }

        [Test]
        public void GetAway_MoveCostsExtraPA_OneShot()
        {
            var attacker = MakeStats("Assaillant", ap: 6);
            var defender = MakeStats("Cible");
            var calc = new CombatCalculator(new QueueDiceRoller(8, 3));
            calc.ResolveTargetedAttack(
                attacker: attacker, defender: defender,
                targetedPart: BodyPart.Torse,
                attackSkill: SkillType.MainsNues, defenseSkill: SkillType.Esquive,
                weaponBaseDamage: 5,
                cancelPenaltyWithAP: false, defenderWantsToDefend: true);

            // Attaque (2 PA) : 6 -> 4, puis surcharge de fuite (+1 PA) : 4 -> 3.
            Assert.AreEqual(4, attacker.CurrentActionPoints);
            Assert.IsTrue(calc.TryGetAwayMove(attacker, out string error), error);
            Assert.AreEqual(3, attacker.CurrentActionPoints);
            Assert.IsFalse(GetAwayState.HasWindow(attacker));

            // Fenêtre consommée : second appel refusé.
            Assert.IsFalse(calc.TryGetAwayMove(attacker, out string second));
            StringAssert.Contains("attaque ou une parade", second);
        }

        [Test]
        public void GetAway_RefusedWithoutWindow()
        {
            var fresh = MakeStats("Neuf");
            var calc = new CombatCalculator(new DiceRoller(7));

            Assert.IsFalse(calc.TryGetAwayMove(fresh, out string error));
            StringAssert.Contains("attaque ou une parade", error);
            Assert.AreEqual(6, fresh.CurrentActionPoints);
        }

        [Test]
        public void GetAway_RefusedWithoutPA_KeepsWindow()
        {
            var attacker = MakeStats("Assaillant");
            var defender = MakeStats("Cible");
            var calc = new CombatCalculator(new QueueDiceRoller(8, 3));
            calc.ResolveTargetedAttack(
                attacker: attacker, defender: defender,
                targetedPart: BodyPart.Torse,
                attackSkill: SkillType.MainsNues, defenseSkill: SkillType.Esquive,
                weaponBaseDamage: 5,
                cancelPenaltyWithAP: false, defenderWantsToDefend: true);

            attacker.CurrentActionPoints = 0;
            Assert.IsFalse(calc.TryGetAwayMove(attacker, out string error));
            StringAssert.Contains("PA", error);
            // Échec : la fenêtre est conservée pour un nouvel essai.
            Assert.IsTrue(GetAwayState.HasWindow(attacker));
        }

        [Test]
        public void GetAway_DistinctFromDisengage_StillProvokes()
        {
            var attacker = MakeStats("Assaillant", ap: 6);
            var defender = MakeStats("Cible");
            var calc = new CombatCalculator(new QueueDiceRoller(8, 3));
            calc.ResolveTargetedAttack(
                attacker: attacker, defender: defender,
                targetedPart: BodyPart.Torse,
                attackSkill: SkillType.MainsNues, defenseSkill: SkillType.Esquive,
                weaponBaseDamage: 5,
                cancelPenaltyWithAP: false, defenderWantsToDefend: true);

            Assert.IsTrue(calc.TryGetAwayMove(attacker, out _));

            // Le dégagement n'arme PAS de décrochage : la fuite provoque.
            Assert.IsFalse(OpportunityState.HasSafeDisengage(attacker));
            Assert.IsTrue(OpportunityState.TryBeginProvokingMove(attacker));

            // ... et le décrochage préventif reste disponible indépendamment.
            Assert.IsTrue(OpportunityState.RequestSafeDisengage(attacker, 1, out _));
            Assert.IsTrue(OpportunityState.HasSafeDisengage(attacker));
        }

        [Test]
        public void GetAway_WindowExpiresOnOwnerTurnStart()
        {
            var attacker = MakeStats("Assaillant");
            var defender = MakeStats("Cible");
            var calc = new CombatCalculator(new QueueDiceRoller(8, 3));
            calc.ResolveTargetedAttack(
                attacker: attacker, defender: defender,
                targetedPart: BodyPart.Torse,
                attackSkill: SkillType.MainsNues, defenseSkill: SkillType.Esquive,
                weaponBaseDamage: 5,
                cancelPenaltyWithAP: false, defenderWantsToDefend: true);

            Assert.IsTrue(GetAwayState.HasWindow(attacker));
            GetAwayState.OnUnitTurnStart(attacker);
            Assert.IsFalse(GetAwayState.HasWindow(attacker));
        }

        /// <summary>Mock séquentiel : chaque appel à Roll consomme la valeur suivante.</summary>
        private class QueueDiceRoller : DiceRoller
        {
            private readonly System.Collections.Generic.Queue<int> _values;

            public QueueDiceRoller(params int[] values) : base(0)
            {
                _values = new System.Collections.Generic.Queue<int>(values);
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

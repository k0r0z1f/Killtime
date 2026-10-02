#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using NUnit.Framework;
using Killtime.Core.Character;
using Killtime.Core.Combat;
using Killtime.Core.Dice;
using Killtime.Tactics.CombatUI;

namespace Killtime.Tests
{
    /// <summary>
    /// RD-032 : Garde du corps / Interception allié (Livre VI §24.5).
    /// Tenir la Ligne : prendre le coup à la place au contact (<= 1 case),
    /// coût 1 PA réaction. Consomme l'unique réaction du round.
    /// </summary>
    [TestFixture]
    public class BodyguardTests
    {
        private static CharacterStats MakeStats(string name, int ap = 5, int hp = 20, int armor = 2)
        {
            var attr = new Attributes(4, 4, 3, 3, 3, 2, 2, 2);
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
            stats.BaseArmorAbsorption = armor;
            return stats;
        }

        [SetUp]
        public void SetUp()
        {
            OpportunityState.ClearAll();
            SkillTechniqueState.ClearAll();
        }

        [TearDown]
        public void TearDown()
        {
            OpportunityState.ClearAll();
            SkillTechniqueState.ClearAll();
        }

        [Test]
        public void Constants_InterceptCostIsOnePA_RangeIsOne()
        {
            Assert.AreEqual(1, CombatTechniqueRegistry.InterceptCost);
            Assert.AreEqual(1, CombatTechniqueRegistry.InterceptRange);
            Assert.AreEqual("Mener : Tenir la Ligne !", CombatTechniqueRegistry.SpecTenir);
            Assert.AreEqual("Garde du corps", CombatTechniqueRegistry.SpecGardeDuCorps);
        }

        [Test]
        public void CanIntercept_RequiresSpecTenir_OrLineDonor_OrBodyguard()
        {
            var protector = MakeStats("Protecteur", ap: 4);
            var ally = MakeStats("Allie", ap: 4);

            // Sans spé ni lien : refusé
            Assert.IsFalse(CombatTechniqueRegistry.CanInterceptStats(protector, ally, 1));

            // Avec spé "Mener : Tenir la Ligne !" : accepté
            protector.Sheet.UnlockedSpecializations.Add(CombatTechniqueRegistry.SpecTenir);
            Assert.IsTrue(CombatTechniqueRegistry.CanInterceptStats(protector, ally, 1));
        }

        [Test]
        public void CanIntercept_AcceptedWithLineBonusDonor()
        {
            var leader = MakeStats("Leader", ap: 4);
            var ally = MakeStats("Allie", ap: 4);

            // Pas de spé Tenir directement, mais le leader applique le bonus de ligne Tenir
            SkillTechniqueState.ApplyLineBonus(leader, ally, 2);
            Assert.IsTrue(SkillTechniqueState.TryGetLineBonusDonor(ally, out var donor));
            Assert.AreEqual(leader, donor);

            Assert.IsTrue(CombatTechniqueRegistry.CanInterceptStats(leader, ally, 1));
        }

        [Test]
        public void CanIntercept_AcceptedWithBodyguardLink()
        {
            var bodyguard = MakeStats("Garde", ap: 4);
            var vip = MakeStats("VIP", ap: 4);

            SkillTechniqueState.SetBodyguard(bodyguard, vip);
            Assert.IsTrue(SkillTechniqueState.IsGuardedBy(vip, bodyguard));

            Assert.IsTrue(CombatTechniqueRegistry.CanInterceptStats(bodyguard, vip, 1));
        }

        [Test]
        public void CanIntercept_RefusedIfDistanceGreaterThanOne()
        {
            var protector = MakeStats("Protecteur", ap: 4);
            var ally = MakeStats("Allie", ap: 4);
            protector.Sheet.UnlockedSpecializations.Add(CombatTechniqueRegistry.SpecTenir);

            Assert.IsTrue(CombatTechniqueRegistry.CanInterceptStats(protector, ally, 1));
            Assert.IsFalse(CombatTechniqueRegistry.CanInterceptStats(protector, ally, 2));
            Assert.IsFalse(CombatTechniqueRegistry.CanInterceptStats(protector, ally, 3));
        }

        [Test]
        public void CanIntercept_RefusedIfInsufficientAP()
        {
            var protector = MakeStats("Protecteur", ap: 0);
            var ally = MakeStats("Allie", ap: 4);
            protector.Sheet.UnlockedSpecializations.Add(CombatTechniqueRegistry.SpecTenir);

            Assert.IsFalse(CombatTechniqueRegistry.CanInterceptStats(protector, ally, 1));

            protector.CurrentActionPoints = 1;
            Assert.IsTrue(CombatTechniqueRegistry.CanInterceptStats(protector, ally, 1));
        }

        [Test]
        public void CanIntercept_RefusedIfAlreadyReactedThisRound()
        {
            var protector = MakeStats("Protecteur", ap: 4);
            var ally = MakeStats("Allie", ap: 4);
            protector.Sheet.UnlockedSpecializations.Add(CombatTechniqueRegistry.SpecTenir);

            Assert.IsTrue(CombatTechniqueRegistry.CanInterceptStats(protector, ally, 1));

            // Consomme la réaction du round
            Assert.IsTrue(OpportunityState.TryConsumeReaction(protector));
            Assert.IsTrue(OpportunityState.HasReacted(protector));

            Assert.IsFalse(CombatTechniqueRegistry.CanInterceptStats(protector, ally, 1));

            // Nouveau tour : réaction rechargée
            OpportunityState.OnUnitTurnStart(protector);
            Assert.IsTrue(CombatTechniqueRegistry.CanInterceptStats(protector, ally, 1));
        }

        [Test]
        public void CanIntercept_RefusedIfStatusIncapacitated()
        {
            var protector = MakeStats("Protecteur", ap: 4);
            var ally = MakeStats("Allie", ap: 4);
            protector.Sheet.UnlockedSpecializations.Add(CombatTechniqueRegistry.SpecTenir);

            protector.ApplyStatus(StatusEffect.Etourdi, 1);
            Assert.IsFalse(CombatTechniqueRegistry.CanInterceptStats(protector, ally, 1));

            protector.RemoveStatus(StatusEffect.Etourdi);
            protector.ApplyStatus(StatusEffect.Paralyse, 1);
            Assert.IsFalse(CombatTechniqueRegistry.CanInterceptStats(protector, ally, 1));

            protector.RemoveStatus(StatusEffect.Paralyse);
            Assert.IsTrue(CombatTechniqueRegistry.CanInterceptStats(protector, ally, 1));
        }

        [Test]
        public void ExecuteIntercept_ConsumesOneAP_AndReaction()
        {
            var protector = MakeStats("Protecteur", ap: 5);
            var ally = MakeStats("Allie", ap: 5);
            protector.Sheet.UnlockedSpecializations.Add(CombatTechniqueRegistry.SpecTenir);

            Assert.IsTrue(CombatTechniqueRegistry.ExecuteInterceptStats(protector, ally, out string error), error);

            Assert.AreEqual(4, protector.CurrentActionPoints);
            Assert.IsTrue(OpportunityState.HasReacted(protector));
            Assert.IsFalse(OpportunityState.CanReact(protector));

            // Deuxième tentative : refusée car réaction déjà consommée
            Assert.IsFalse(CombatTechniqueRegistry.ExecuteInterceptStats(protector, ally, out error));
        }

        [Test]
        public void RedirectDuelToProtector_UpdatesDefenderAndArmor_MarksBasePaid()
        {
            var attacker = MakeStats("Tireur", ap: 5);
            var target = MakeStats("Cible", ap: 5, armor: 0);
            var protector = MakeStats("Protecteur", ap: 5, armor: 4);
            protector.Sheet.UnlockedSpecializations.Add(CombatTechniqueRegistry.SpecTenir);

            var calc = new CombatCalculator(new DiceRoller(7));
            var duel = calc.BeginSkillDuel(
                attacker, target, BodyPart.Torse,
                SkillType.Ballistique, SkillType.Esquive,
                weaponBaseDamage: 5, cancelPenaltyWithAP: false,
                defenderWantsToDefend: true, attackerSpecialization: null, defenderSpecialization: null,
                defenderArmor: target.BaseArmorAbsorption, out string error);

            Assert.IsNotNull(duel, error);
            Assert.AreEqual(target, duel.Defender);
            Assert.AreEqual(0, duel.DefenderArmor);

            calc.RedirectDuelToProtector(duel, protector);

            Assert.AreEqual(protector, duel.Defender);
            Assert.AreEqual(4, duel.DefenderArmor);
            Assert.IsTrue(duel.DefenderBasePaid);
        }

        [Test]
        public void ResolveInterceptedAttack_DamagesProtectorInsteadOfAlly()
        {
            var attacker = MakeStats("Attaquant", ap: 5);
            var ally = MakeStats("Allie", ap: 5, hp: 10, armor: 0);
            var protector = MakeStats("Protecteur", ap: 5, hp: 20, armor: 2);
            protector.Sheet.UnlockedSpecializations.Add(CombatTechniqueRegistry.SpecTenir);

            int allyHpBefore = ally.CurrentHealth;
            int protHpBefore = protector.CurrentHealth;

            var calc = new CombatCalculator(new DiceRoller(12)); // Gros dé attaquant
            var result = calc.ResolveInterceptedAttack(
                attacker, protector, BodyPart.Torse,
                SkillType.Ballistique, weaponBaseDamage: 6);

            Assert.IsTrue(result.IsHit);
            StringAssert.Contains("[INTERCEPTION]", result.CombatLog);
            StringAssert.Contains("Protecteur", result.CombatLog);

            // L'allié n'a subi aucun dégât
            Assert.AreEqual(allyHpBefore, ally.CurrentHealth);
            // Le protecteur a absorbé l'impact
            Assert.IsTrue(protector.CurrentHealth < protHpBefore);
        }

        [Test]
        public void BodyguardLink_PurgesWhenProtectorOrAllyDies()
        {
            var protector = MakeStats("Garde", ap: 5);
            var ally = MakeStats("VIP", ap: 5);

            SkillTechniqueState.SetBodyguard(protector, ally);
            Assert.IsTrue(SkillTechniqueState.TryGetBodyguard(ally, out var found));
            Assert.AreEqual(protector, found);

            // Protecteur meurt
            protector.CurrentHealth = 0;
            SkillTechniqueState.OnUnitTurnStart(ally);

            Assert.IsFalse(SkillTechniqueState.TryGetBodyguard(ally, out _));
        }

        [Test]
        public void ExecuteIntercept_WithRalenti_ConsumesTwoAP_NotFour()
        {
            var protector = MakeStats("RalentiProtector", ap: 2);
            var ally = MakeStats("Allie", ap: 5);
            protector.Sheet.UnlockedSpecializations.Add(CombatTechniqueRegistry.SpecTenir);
            protector.ApplyStatus(StatusEffect.Ralenti, 2);

            Assert.IsTrue(CombatTechniqueRegistry.CanInterceptStats(protector, ally, 1));
            Assert.IsTrue(CombatTechniqueRegistry.ExecuteInterceptStats(protector, ally, out string error), error);

            // Coût de 1 PA * 2 (Ralenti) = 2 PA consommés, exactement 0 restant (pas de double-multiplication à 4)
            Assert.AreEqual(0, protector.CurrentActionPoints);
            Assert.IsTrue(OpportunityState.HasReacted(protector));
        }

        [Test]
        public void RedirectDuelToProtector_DoesNotDoubleChargeReactionBaseCost_AllowsActiveDefenseWithZeroRemainingAP()
        {
            var attacker = MakeStats("Attaquant", ap: 5);
            var ally = MakeStats("Allie", ap: 5);
            var protector = MakeStats("Protecteur", ap: 1);
            protector.Sheet.UnlockedSpecializations.Add(CombatTechniqueRegistry.SpecTenir);

            // 1. Interception : consomme son unique 1 PA
            Assert.IsTrue(CombatTechniqueRegistry.ExecuteInterceptStats(protector, ally, out string error), error);
            Assert.AreEqual(0, protector.CurrentActionPoints);

            // 2. Début du duel contre l'allié initial
            var calc = new CombatCalculator(new DiceRoller(5));
            var duel = calc.BeginSkillDuel(
                attacker, ally, BodyPart.Torse,
                SkillType.Ballistique, SkillType.Esquive,
                weaponBaseDamage: 5, cancelPenaltyWithAP: false,
                defenderWantsToDefend: true, attackerSpecialization: null, defenderSpecialization: null,
                defenderArmor: ally.BaseArmorAbsorption, out string duelErr);
            Assert.IsNotNull(duel, duelErr);

            // 3. Redirection vers le protecteur
            calc.RedirectDuelToProtector(duel, protector);
            Assert.AreEqual(protector, duel.Defender);
            Assert.IsTrue(duel.DefenderBasePaid);
            Assert.IsTrue(duel.CanDefenderReact);

            // 4. Déclaration aveugle de défense du protecteur : ne doit PAS débiter 1 PA supplémentaire ni échouer avec 0 PA restant
            calc.DeclareDefenderStakes(duel, SkillType.Esquive, true, 0, 0);

            Assert.AreEqual(0, protector.CurrentActionPoints);
            Assert.IsTrue(duel.DefenderWantsToDefend);
            Assert.IsTrue(duel.CanDefenderReact);
            Assert.IsTrue(duel.DefenderBasePaid);
            Assert.IsTrue(duel.DefenseDie >= DiceType.D2);
        }
    }
}
#endif

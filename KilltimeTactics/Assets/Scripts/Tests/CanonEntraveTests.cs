#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using NUnit.Framework;
using Killtime.Core.Character;
using Killtime.Core.Combat;
using Killtime.Core.Dice;
using Killtime.Core.Inventory;
using Killtime.Tactics.AI;

namespace Killtime.Tests
{
    /// <summary>
    /// RD-201 — Canon Entravé (Livre V §22.2) : les 4 options de tir au
    /// contact d'un conscient (distance 1). Une fixture dédiée.
    /// </summary>
    [TestFixture]
    public class CanonEntraveTests
    {
        private static InventoryItem MakeRifle(bool twoHands = true)
        {
            return new InventoryItem
            {
                Name = "Fusil d'essai",
                Type = ItemType.Weapon,
                IsEquipped = true,
                EquipSlot = twoHands ? ItemEquipSlot.TwoHands : ItemEquipSlot.MainHand,
                AssociatedSkill = SkillType.Ballistique,
                RangeInTiles = 8,
                BaseDamage = 8,
                AmmoCapacity = 10,
                AmmoRemaining = 5
            };
        }

        private static InventoryItem MakeKnife()
        {
            return new InventoryItem
            {
                Name = "Couteau",
                Type = ItemType.Weapon,
                IsEquipped = true,
                EquipSlot = ItemEquipSlot.MainHand,
                AssociatedSkill = SkillType.ManiementArmes,
                RangeInTiles = 1,
                BaseDamage = 4,
                AmmoCapacity = 0
            };
        }

        private static CharacterStats MakeShooter(bool twoHands = true)
        {
            var attr = new Attributes(@for: 3, agi: 4, con: 3, rap: 3, @int: 2, eru: 2, cha: 1, ins: 2, mag: 0);
            var sheet = new CharacterSheet { Name = "Tireur", BaseAttributes = attr };
            sheet.Inventory.Add(MakeRifle(twoHands));
            return sheet.ToCombatStats();
        }

        private static CharacterStats MakeBrawler()
        {
            var attr = new Attributes(@for: 4, agi: 4, con: 3, rap: 3, @int: 2, eru: 2, cha: 1, ins: 2, mag: 0);
            var sheet = new CharacterSheet { Name = "Bagarreur", BaseAttributes = attr };
            return sheet.ToCombatStats();
        }

        private static CharacterStats MakeTarget(int con = 3, bool unconscious = false)
        {
            var attr = new Attributes(@for: 2, agi: 3, con: con, rap: 2, @int: 2, eru: 2, cha: 1, ins: 2, mag: 0);
            var sheet = new CharacterSheet { Name = "Cible", BaseAttributes = attr };
            var stats = sheet.ToCombatStats();
            if (unconscious) stats.ApplyStatus(StatusEffect.Inconscient, 1);
            return stats;
        }

        // 1. Tir de rétention : 2 PA, malus -2 conservé.
        [Test]
        public void RetentionShot_Costs2PA_AndKeepsMinus2()
        {
            var calc = new CombatCalculator(new DiceRoller(101));
            calc.SetContactDistanceState(true);
            var att = MakeShooter();
            var def = MakeTarget();

            var result = calc.ResolveTargetedAttack(
                attacker: att, defender: def, targetedPart: BodyPart.Torse,
                attackSkill: SkillType.Ballistique, defenseSkill: SkillType.Esquive,
                weaponBaseDamage: 8, cancelPenaltyWithAP: false, defenderWantsToDefend: false);

            Assert.IsTrue(result.IsCanonEntrave);
            StringAssert.Contains("Canon Entravé : -2", result.CombatLog);
            Assert.AreEqual(2, att.MaxActionPoints - att.CurrentActionPoints);
            calc.SetContactDistanceState(false);
        }

        // 2. Tir visé : 3 PA, malus annulé, visée membre autorisée.
        [Test]
        public void AimedShot_Costs3PA_CancelsEntrave_AllowsLimbAim()
        {
            var calc = new CombatCalculator(new DiceRoller(102));
            calc.SetContactDistanceState(true);
            var att = MakeShooter();
            var def = MakeTarget();

            var result = calc.ResolveTargetedAttack(
                attacker: att, defender: def, targetedPart: BodyPart.BrasDroit,
                attackSkill: SkillType.Ballistique, defenseSkill: SkillType.Esquive,
                weaponBaseDamage: 8, cancelPenaltyWithAP: true, defenderWantsToDefend: false);

            Assert.IsTrue(result.IsCanonEntrave);
            StringAssert.Contains("Annulé (+1 PA)", result.CombatLog);
            Assert.AreEqual(3, att.MaxActionPoints - att.CurrentActionPoints);
            calc.SetContactDistanceState(false);
        }

        // 3. Crosse : Maniement, 2 PA, sans malus d'entrave, sans munition.
        [Test]
        public void Buttstroke_MeleeNoPenaltyNoAmmo_2PA()
        {
            var calc = new CombatCalculator(new DiceRoller(103));
            calc.SetContactDistanceState(true);
            var att = MakeShooter();
            var def = MakeTarget();
            var rifle = att.Sheet.GetEquippedWeapon();
            int ammoBefore = rifle.AmmoRemaining;

            var result = calc.ResolveTargetedAttack(
                attacker: att, defender: def, targetedPart: BodyPart.Torse,
                attackSkill: SkillType.ManiementArmes, defenseSkill: SkillType.Esquive,
                weaponBaseDamage: 3, cancelPenaltyWithAP: false,
                defenderWantsToDefend: false, isButtstroke: true);

            Assert.IsTrue(result.IsButtstroke);
            Assert.IsFalse(result.IsCanonEntrave);
            StringAssert.Contains("Crosse", result.CombatLog);
            Assert.AreEqual(2, att.MaxActionPoints - att.CurrentActionPoints);
            Assert.IsFalse(WeaponAmmo.UsesAmmo(rifle, SkillType.ManiementArmes));
            Assert.AreEqual(ammoBefore, rifle.AmmoRemaining);
            calc.SetContactDistanceState(false);
        }

        // 3b. Crosse : Déstabilisé si bruts > CON x2 (Jambes isole la règle
        // du choc anatomique, qui donnerait À Terre/Ralenti et non Déstabilisé).
        [Test]
        public void Buttstroke_Destabilises_WhenBrutExceedsConX2()
        {
            var calc = new CombatCalculator(new DiceRoller(104));
            calc.SetContactDistanceState(true);
            try
            {
                DiceRoller.DevForceAttackCrit = true;
                DiceRoller.DevForcedDifferential = 8;

                var att = MakeShooter();
                var def = MakeTarget(con: 2); // Encaissement 4 ; bruts (3+8) x1 = 11 > 4.
                var result = calc.ResolveTargetedAttack(
                    attacker: att, defender: def, targetedPart: BodyPart.Jambes,
                    attackSkill: SkillType.ManiementArmes, defenseSkill: SkillType.Esquive,
                    weaponBaseDamage: 3, cancelPenaltyWithAP: false,
                    defenderWantsToDefend: false, isButtstroke: true);

                Assert.IsTrue(result.IsHit);
                Assert.IsTrue(def.ActiveStatus.HasFlag(StatusEffect.Destabilise),
                    "La crosse à 11 bruts contre CON x2 = 4 doit Déstabiliser.");
            }
            finally
            {
                DiceRoller.ClearDevRig();
                calc.SetContactDistanceState(false);
            }
        }

        // 3c. Crosse : pas de Déstabilisé si bruts ≤ CON x2.
        [Test]
        public void Buttstroke_NoDestabilise_WhenBrutWithinConX2()
        {
            var calc = new CombatCalculator(new DiceRoller(105));
            calc.SetContactDistanceState(true);
            try
            {
                DiceRoller.DevForceAttackFumble = true;
                DiceRoller.DevForcedDifferential = 0;

                var att = MakeShooter();
                var def = MakeTarget(con: 6); // Encaissement 12 ; bruts 3 + 0 = 3 ≤ 12.
                var result = calc.ResolveTargetedAttack(
                    attacker: att, defender: def, targetedPart: BodyPart.Torse,
                    attackSkill: SkillType.ManiementArmes, defenseSkill: SkillType.Esquive,
                    weaponBaseDamage: 3, cancelPenaltyWithAP: false,
                    defenderWantsToDefend: false, isButtstroke: true);

                Assert.IsFalse(def.ActiveStatus.HasFlag(StatusEffect.Destabilise));
                Assert.AreEqual(StatusEffect.None, result.InflictedStatus);
            }
            finally
            {
                DiceRoller.ClearDevRig();
                calc.SetContactDistanceState(false);
            }
        }

        // 4. Mains Nues : exemption d'entrave + Refoulement sur choc.
        [Test]
        public void UnarmedStrike_Exempt_AndRefoules_OnTrauma()
        {
            var calc = new CombatCalculator(new DiceRoller(106));
            calc.SetContactDistanceState(true);
            try
            {
                DiceRoller.DevForceAttackCrit = true;
                DiceRoller.DevForcedDifferential = 8;

                var att = MakeBrawler();
                var def = MakeTarget(con: 2); // Encaissement 4 ; bruts (4+8) = 12 > 4.
                var result = calc.ResolveTargetedAttack(
                    attacker: att, defender: def, targetedPart: BodyPart.Torse,
                    attackSkill: SkillType.MainsNues, defenseSkill: SkillType.Esquive,
                    weaponBaseDamage: 4, cancelPenaltyWithAP: false,
                    defenderWantsToDefend: false);

                Assert.IsFalse(result.IsCanonEntrave);
                Assert.IsTrue(result.ExceededEncaissement);
                Assert.IsTrue(result.CausedKnockback, "Choc traumatique à mains nues => Refoulement.");
                Assert.AreEqual(2, att.MaxActionPoints - att.CurrentActionPoints);
            }
            finally
            {
                DiceRoller.ClearDevRig();
                calc.SetContactDistanceState(false);
            }
        }

        // 4b. Mains Nues + Arts Martiaux : mention d'exemption totale au log.
        [Test]
        public void UnarmedStrike_MartialArts_LogsTotalExemption()
        {
            var calc = new CombatCalculator(new DiceRoller(107));
            calc.SetContactDistanceState(true);
            var att = MakeBrawler();
            att.Sheet.UnlockedSpecializations.Add("Arts Martiaux");
            var def = MakeTarget();

            var result = calc.ResolveTargetedAttack(
                attacker: att, defender: def, targetedPart: BodyPart.Torse,
                attackSkill: SkillType.MainsNues, defenseSkill: SkillType.Esquive,
                weaponBaseDamage: 4, cancelPenaltyWithAP: false,
                defenderWantsToDefend: false);

            Assert.IsFalse(result.IsCanonEntrave);
            StringAssert.Contains("Exemption Totale", result.CombatLog);
            calc.SetContactDistanceState(false);
        }

        // Règles pures : arme longue.
        [Test]
        public void HasLongWeapon_MatchesEntravePredicate()
        {
            Assert.IsTrue(CanonEntraveRules.HasLongWeapon((InventoryItem)MakeRifle(true)));
            var pistol = MakeRifle(false);
            Assert.IsTrue(CanonEntraveRules.HasLongWeapon(pistol));
            Assert.IsFalse(CanonEntraveRules.HasLongWeapon(MakeKnife()));
            Assert.IsFalse(CanonEntraveRules.HasLongWeapon((InventoryItem)null));
            Assert.IsFalse(CanonEntraveRules.HasLongWeapon(MakeBrawler()));
            Assert.IsTrue(CanonEntraveRules.HasLongWeapon(MakeShooter()));
        }

        // Règles pures : situation d'entrave (contact + conscient + arme longue).
        [Test]
        public void IsEntraveSituation_RequiresContact_Conscious_LongWeapon()
        {
            var shooter = MakeShooter();
            var target = MakeTarget();
            var ko = MakeTarget(unconscious: true);
            var brawler = MakeBrawler();

            Assert.IsTrue(CanonEntraveRules.IsEntraveSituation(shooter, target, 1));
            Assert.IsFalse(CanonEntraveRules.IsEntraveSituation(shooter, target, 2));
            Assert.IsFalse(CanonEntraveRules.IsEntraveSituation(shooter, ko, 1));
            Assert.IsFalse(CanonEntraveRules.IsEntraveSituation(brawler, target, 1));
            Assert.IsFalse(CanonEntraveRules.IsEntraveSituation(null, target, 1));
            Assert.IsFalse(CanonEntraveRules.IsEntraveSituation(shooter, null, 1));
        }

        // Règles pures : dégâts de crosse (4 à deux mains, 3 sinon).
        [Test]
        public void ButtstrokeBaseDamage_TwoHands4_Otherwise3()
        {
            Assert.AreEqual(4, CanonEntraveRules.ButtstrokeBaseDamage(MakeRifle(true)));
            Assert.AreEqual(3, CanonEntraveRules.ButtstrokeBaseDamage(MakeRifle(false)));
            Assert.AreEqual(3, CanonEntraveRules.ButtstrokeBaseDamage(MakeKnife()));
            Assert.AreEqual(3, CanonEntraveRules.ButtstrokeBaseDamage(null));
        }

        // Règles pures : tir possible.
        [Test]
        public void CanFireShot_MirrorsAmmoAndSprintRules()
        {
            var loaded = MakeRifle();
            Assert.IsTrue(CanonEntraveRules.CanFireShot(loaded, false));
            Assert.IsFalse(CanonEntraveRules.CanFireShot(loaded, true));

            var empty = MakeRifle();
            empty.AmmoRemaining = 0;
            Assert.IsFalse(CanonEntraveRules.CanFireShot(empty, false));

            var jammed = MakeRifle();
            jammed.Jammed = true;
            Assert.IsFalse(CanonEntraveRules.CanFireShot(jammed, false));

            Assert.IsTrue(CanonEntraveRules.CanFireShot(null, true));
            Assert.IsTrue(CanonEntraveRules.CanFireShot(MakeKnife(), true));
        }

        // Règles pures : choix de l'option de contact (IA).
        [Test]
        public void ChooseContactOption_PrefersShot_ThenButtstroke_ThenUnarmed()
        {
            Assert.AreEqual(CanonEntraveOption.None, CanonEntraveRules.ChooseContactOption(1, true, true, true));
            Assert.AreEqual(CanonEntraveOption.AimedShot, CanonEntraveRules.ChooseContactOption(3, true, true, true));
            Assert.AreEqual(CanonEntraveOption.RetentionShot, CanonEntraveRules.ChooseContactOption(2, true, true, true));
            Assert.AreEqual(CanonEntraveOption.Buttstroke, CanonEntraveRules.ChooseContactOption(5, false, true, true));
            Assert.AreEqual(CanonEntraveOption.Buttstroke, CanonEntraveRules.ChooseContactOption(2, false, true, false));
            Assert.AreEqual(CanonEntraveOption.UnarmedStrike, CanonEntraveRules.ChooseContactOption(2, false, false, true));
            Assert.AreEqual(CanonEntraveOption.None, CanonEntraveRules.ChooseContactOption(2, false, false, false));
        }

        // IA : le bonus d'annulation d'entrave rend le tir visé compétitif.
        [Test]
        public void AttackPlanEvaluator_EntraveCancelBonus_EnablesAimedShot()
        {
            AttackPlanInput BaseInput(double cancelBonus)
            {
                return new AttackPlanInput
                {
                    BaseAttExpected = 11,
                    BaseDefExpected = 12,
                    WeaponDmg = 6,
                    TargetArmor = 0,
                    TargetEncaissement = 5,
                    TargetHp = 30,
                    AvailableAP = 3,
                    ReserveNeeded = 0,
                    SpendablePE = 0,
                    PeValue = 1,
                    MaxAttacks = 1,
                    AttacksThisTurn = 0,
                    IsRanged = true,
                    Posture = TacticalPosture.CalculatedStrike,
                    TargetRapidite = 3,
                    TargetHasATerre = false,
                    TargetCurrentAP = 0,
                    EntraveCancelBonus = cancelBonus
                };
            }

            var plain = AttackPlanEvaluator.Evaluate(
                BaseInput(0), p => BodyPartInfo.GetInfo(p).DifficultyModifier);
            Assert.IsFalse(plain.CancelPenalty);
            Assert.AreEqual(BodyPart.Torse, plain.TargetedPart);

            var compensated = AttackPlanEvaluator.Evaluate(
                BaseInput(2), p => BodyPartInfo.GetInfo(p).DifficultyModifier);
            Assert.IsTrue(compensated.CancelPenalty);
            Assert.AreEqual(BodyPart.Tete, compensated.TargetedPart);
        }
    }
}
#endif

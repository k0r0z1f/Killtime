#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using NUnit.Framework;
using Killtime.Core.Character;
using Killtime.Core.Combat;
using Killtime.Core.Inventory;

namespace Killtime.Tests
{
    /// <summary>
    /// RD-036 Hauteur / contrebas : quantification par paliers de 1.5 m,
    /// table mêlée validée (gate verticale + bonus), tir balistique ±1 capé,
    /// armes laser/énergie exemptées.
    /// </summary>
    [TestFixture]
    public class ElevationAdvantageTests
    {
        private static InventoryItem MeleeWeapon(int range, string category = "Épées Métal")
        {
            return new InventoryItem
            {
                Name = "Test",
                Category = category,
                RangeInTiles = range,
                AssociatedSkill = SkillType.ManiementArmes
            };
        }

        private static InventoryItem RangedWeapon(string category, string ammo)
        {
            return new InventoryItem
            {
                Name = "Test",
                Category = category,
                RangeInTiles = 8,
                AssociatedSkill = SkillType.Ballistique,
                AmmoType = ammo
            };
        }

        [Test]
        public void Level_ZeroAndMicroRelief_NoLevel()
        {
            Assert.AreEqual(0, ElevationAdvantage.LevelFromHeightDiff(0f));
            Assert.AreEqual(0, ElevationAdvantage.LevelFromHeightDiff(0.25f));
            Assert.AreEqual(0, ElevationAdvantage.LevelFromHeightDiff(0.5f));
            Assert.AreEqual(0, ElevationAdvantage.LevelFromHeightDiff(0.74f));
            Assert.AreEqual(0, ElevationAdvantage.LevelFromHeightDiff(-0.74f));
        }

        [Test]
        public void Level_Thresholds_RoundHalfAway()
        {
            Assert.AreEqual(1, ElevationAdvantage.LevelFromHeightDiff(0.75f));
            Assert.AreEqual(1, ElevationAdvantage.LevelFromHeightDiff(1.5f));
            Assert.AreEqual(-1, ElevationAdvantage.LevelFromHeightDiff(-0.75f));
            Assert.AreEqual(-1, ElevationAdvantage.LevelFromHeightDiff(-1.5f));
            Assert.AreEqual(2, ElevationAdvantage.LevelFromHeightDiff(3.0f));
            Assert.AreEqual(-2, ElevationAdvantage.LevelFromHeightDiff(-3.0f));
        }

        [Test]
        public void Melee_ShortWeapon_ValidatedTable()
        {
            // +3 et plus : impossible
            Assert.IsFalse(ElevationAdvantage.EvaluateMelee(3, 1).Reachable);
            Assert.IsFalse(ElevationAdvantage.EvaluateMelee(5, 1).Reachable);
            // +2 : impossible sans allonge
            Assert.IsFalse(ElevationAdvantage.EvaluateMelee(2, 1).Reachable);
            // +1 : +1 atteignable
            var plus1 = ElevationAdvantage.EvaluateMelee(1, 1);
            Assert.IsTrue(plus1.Reachable);
            Assert.AreEqual(1, plus1.AttackMod);
            // 0 : normal
            var zero = ElevationAdvantage.EvaluateMelee(0, 1);
            Assert.IsTrue(zero.Reachable);
            Assert.AreEqual(0, zero.AttackMod);
            // -1 : -1 limite
            var minus1 = ElevationAdvantage.EvaluateMelee(-1, 1);
            Assert.IsTrue(minus1.Reachable);
            Assert.AreEqual(-1, minus1.AttackMod);
            // -2 : impossible sans allonge
            Assert.IsFalse(ElevationAdvantage.EvaluateMelee(-2, 1).Reachable);
            // -3 et plus : impossible
            Assert.IsFalse(ElevationAdvantage.EvaluateMelee(-3, 1).Reachable);
        }

        [Test]
        public void Melee_ReachWeapon_ValidatedTable()
        {
            Assert.IsFalse(ElevationAdvantage.EvaluateMelee(3, 2).Reachable);
            var plus2 = ElevationAdvantage.EvaluateMelee(2, 2);
            Assert.IsTrue(plus2.Reachable);
            Assert.AreEqual(1, plus2.AttackMod);
            var plus1 = ElevationAdvantage.EvaluateMelee(1, 2);
            Assert.IsTrue(plus1.Reachable);
            Assert.AreEqual(2, plus1.AttackMod);
            Assert.AreEqual(0, ElevationAdvantage.EvaluateMelee(0, 2).AttackMod);
            Assert.AreEqual(-1, ElevationAdvantage.EvaluateMelee(-1, 2).AttackMod);
            var minus2 = ElevationAdvantage.EvaluateMelee(-2, 2);
            Assert.IsTrue(minus2.Reachable);
            Assert.AreEqual(-2, minus2.AttackMod);
            Assert.IsFalse(ElevationAdvantage.EvaluateMelee(-3, 2).Reachable);
        }

        [Test]
        public void Ranged_Conventional_CappedAtPlusMinusOne()
        {
            Assert.AreEqual(1, ElevationAdvantage.EvaluateRanged(1, false).AttackMod);
            Assert.AreEqual(1, ElevationAdvantage.EvaluateRanged(4, false).AttackMod);
            Assert.AreEqual(0, ElevationAdvantage.EvaluateRanged(0, false).AttackMod);
            Assert.AreEqual(-1, ElevationAdvantage.EvaluateRanged(-1, false).AttackMod);
            Assert.AreEqual(-1, ElevationAdvantage.EvaluateRanged(-4, false).AttackMod);
            Assert.IsTrue(ElevationAdvantage.EvaluateRanged(4, false).Reachable);
        }

        [Test]
        public void Ranged_Laser_NoModifier()
        {
            Assert.AreEqual(0, ElevationAdvantage.EvaluateRanged(1, true).AttackMod);
            Assert.AreEqual(0, ElevationAdvantage.EvaluateRanged(-2, true).AttackMod);
            Assert.IsTrue(ElevationAdvantage.IsEnergyRangedWeapon(RangedWeapon("Fusils Laser", "Charge Laser")));
            Assert.IsTrue(ElevationAdvantage.IsEnergyRangedWeapon(RangedWeapon("Divers", "Charge Laser")));
            Assert.IsFalse(ElevationAdvantage.IsEnergyRangedWeapon(RangedWeapon("Brut / Hast / Arc", "Carquois Flèches")));
            Assert.IsFalse(ElevationAdvantage.IsEnergyRangedWeapon(RangedWeapon("Grenades", "")));
            // Les épées laser restent de la mêlée : pas d'exemption énergie.
            Assert.IsFalse(ElevationAdvantage.IsEnergyRangedWeapon(MeleeWeapon(1, "Épées Laser")));
        }

        [Test]
        public void Resolve_Melee_UsesTable()
        {
            var r = ElevationAdvantage.Resolve(1.5f, 0f, true, SkillType.ManiementArmes, MeleeWeapon(1));
            Assert.AreEqual(ElevationAttackKind.Melee, r.Kind);
            Assert.AreEqual(1, r.Level);
            Assert.IsTrue(r.Reachable);
            Assert.AreEqual(1, r.AttackMod);

            var reach = ElevationAdvantage.Resolve(1.5f, 0f, true, SkillType.ManiementArmes, MeleeWeapon(2));
            Assert.AreEqual(2, reach.AttackMod);

            var blocked = ElevationAdvantage.Resolve(3.0f, 0f, true, SkillType.ManiementArmes, MeleeWeapon(1));
            Assert.IsFalse(blocked.Reachable);
        }

        [Test]
        public void Resolve_RangedBallistic_UsesCappedRule()
        {
            var high = ElevationAdvantage.Resolve(1.5f, 0f, false, SkillType.Ballistique,
                RangedWeapon("Brut / Hast / Arc", "Carquois Flèches"));
            Assert.AreEqual(ElevationAttackKind.Ranged, high.Kind);
            Assert.AreEqual(1, high.AttackMod);

            var laser = ElevationAdvantage.Resolve(1.5f, 0f, false, SkillType.Ballistique,
                RangedWeapon("Fusils Laser", "Charge Laser"));
            Assert.AreEqual(0, laser.AttackMod);
        }

        [Test]
        public void Resolve_NonMartialSkill_NoModifier()
        {
            var r = ElevationAdvantage.Resolve(3.0f, 0f, false, SkillType.MagieElementale, null);
            Assert.AreEqual(ElevationAttackKind.None, r.Kind);
            Assert.AreEqual(0, r.AttackMod);
            Assert.IsTrue(r.Reachable);
        }

        [Test]
        public void IsMeleeReachable_SpotChecks()
        {
            Assert.IsTrue(ElevationAdvantage.IsMeleeReachable(1.5f, 0f, 1));
            Assert.IsFalse(ElevationAdvantage.IsMeleeReachable(3.0f, 0f, 1));
            Assert.IsTrue(ElevationAdvantage.IsMeleeReachable(3.0f, 0f, 2));
            Assert.IsFalse(ElevationAdvantage.IsMeleeReachable(0f, 4.5f, 2));
        }
    }
}
#endif

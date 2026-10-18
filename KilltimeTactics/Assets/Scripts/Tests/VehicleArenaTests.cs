#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using Killtime.Core.Character;
using Killtime.Core.Combat;
using Killtime.Core.Inventory;
using Killtime.Core.Rules;
using Killtime.Tactics;

namespace Killtime.Tests
{
    /// <summary>
    /// RD-195 : branchement véhicules en arène (Livre VI §26.3).
    /// Labels/previews purs de CombatDevArena + séquences déléguées
    /// (embarquer 2 PA, écrasement mobile, frappe +100 lourde, épave =
    /// débarquement forcé). Aucune scène requise (pas de figurine ici).
    /// </summary>
    [TestFixture]
    public class VehicleArenaTests
    {
        private static CharacterStats MakeStats(string name, int ap = 8, int hp = 20)
        {
            var sheet = new CharacterSheet
            {
                Name = name,
                BaseAttributes = new Attributes(5, 4, 5, 3, 3, 2, 2, 2),
                UnlockedSpecializations = new List<string>()
            };
            var stats = sheet.ToCombatStats();
            stats.Sheet = sheet;
            stats.CurrentActionPoints = ap;
            stats.CurrentHealth = hp;
            return stats;
        }

        private static InventoryItem MakeWeapon(string name, int baseDamage, bool heavyAmmo = false)
        {
            return new InventoryItem
            {
                Name = name,
                Type = ItemType.Weapon,
                BaseDamage = baseDamage,
                HeavyAmmo = heavyAmmo
            };
        }

        [SetUp]
        public void SetUp()
        {
            VehicleState.ClearAll();
            CoreRulesConfig.Instance.ResetToCodexDefaults();
        }

        [TearDown]
        public void TearDown()
        {
            VehicleState.ClearAll();
            CoreRulesConfig.Instance.ResetToCodexDefaults();
        }

        [Test]
        public void Labels_CoverAllScales()
        {
            Assert.IsNotEmpty(CombatDevArena.VehicleLabelFor(VehicleScale.Velo));
            Assert.IsNotEmpty(CombatDevArena.VehicleLabelFor(VehicleScale.Charrette));
            Assert.IsNotEmpty(CombatDevArena.VehicleLabelFor(VehicleScale.JetTank));
            Assert.IsNotEmpty(CombatDevArena.VehicleLabelFor(VehicleScale.Croiseur));
            Assert.IsNotEmpty(CombatDevArena.VehicleLabelFor(VehicleScale.Station));
        }

        [Test]
        public void StrikePreview_ShowsHeavyBonus()
        {
            string heavy = CombatDevArena.FormatStrikePreview(10, 100);
            StringAssert.Contains("+100", heavy);
            StringAssert.Contains("110", heavy);
            string light = CombatDevArena.FormatStrikePreview(4, 0);
            StringAssert.Contains("légère", light);
        }

        [Test]
        public void DefaultTargets_EssentialVsSuperficial()
        {
            Assert.IsTrue(VehicleMechanics.IsEssential(CombatDevArena.DefaultEssentialTarget));
            Assert.IsFalse(VehicleMechanics.IsEssential(CombatDevArena.DefaultSuperficialTarget));
        }

        [Test]
        public void EmbarkFlow_CostsTwoPA_RefusedOnWreck()
        {
            var rider = MakeStats("Pilote", ap: 8);
            VehicleState.Register("veh-arene-1", VehicleScale.JetTank, hpOverride: 100);
            Assert.IsTrue(VehicleState.TryEmbark(rider, "veh-arene-1", out _));
            Assert.AreEqual(6, rider.CurrentActionPoints);
            VehicleState.DamageSubsystem("veh-arene-1", VehicleSubsystem.Cockpit, 100, out _);
            Assert.IsTrue(VehicleState.IsDestroyed("veh-arene-1"));
            Assert.IsFalse(VehicleState.IsEmbarked(rider)); // débarquement forcé, équipage survivant
            Assert.IsTrue(rider.IsAlive);
            var late = MakeStats("Retardataire", ap: 8);
            Assert.IsFalse(VehicleState.TryEmbark(late, "veh-arene-1", out string wreckLog));
            StringAssert.Contains("épave", wreckLog);
        }

        [Test]
        public void RunOverFlow_MobileDamagesAndKnocksDown()
        {
            var walker = MakeStats("Piéton", hp: 20);
            VehicleState.Register("veh-arene-2", VehicleScale.JetTank);
            Assert.IsTrue(VehicleState.TryRunOver("veh-arene-2", walker, true, out string log));
            Assert.AreEqual(20 - VehicleMechanics.RunOverDamageFor(VehicleScale.JetTank), walker.CurrentHealth);
            Assert.IsTrue(walker.ActiveStatus.HasFlag(StatusEffect.ATerre));
            StringAssert.Contains("CRASEMENT", log.Replace("É", "E"));
        }

        [Test]
        public void StrikeFlow_HeavyBonusIncludedAndVisible()
        {
            VehicleState.Register("veh-arene-3", VehicleScale.JetTank, hpOverride: 1000);
            var canon = MakeWeapon("Canon Lourd", 10);
            int bonus = VehicleMechanics.ComputeHeavyBonus(VehicleMechanics.IsHeavyWeapon(canon), true);
            Assert.AreEqual(100, bonus);
            bool ok = SpecialManeuverService.StrikeVehicleSubsystem(
                "veh-arene-3", VehicleSubsystem.Moteur, 10, canon, out var hit);
            Assert.IsTrue(ok);
            Assert.AreEqual(110, hit.DamageDealt);
            string preview = CombatDevArena.FormatStrikePreview(10, bonus);
            Assert.IsTrue(preview.Contains(hit.DamageDealt.ToString()));
        }
    }
}
#endif

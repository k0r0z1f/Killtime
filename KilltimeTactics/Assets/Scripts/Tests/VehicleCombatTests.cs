#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using Killtime.Core.Character;
using Killtime.Core.Combat;
using Killtime.Core.Inventory;
using Killtime.Core.Rules;

namespace Killtime.Tests
{
    /// <summary>
    /// RD-051 : Combat de Véhicules &amp; Sous-systèmes (Livre VI §26.3).
    /// Couvre au niveau pur (VehicleMechanics + VehicleState) : paliers par
    /// gabarit, clamp, essentiel vs superficiel, +100 armes lourdes,
    /// embarquement 2 PA / refus sans PA, écrasement, destruction à 0 PV
    /// essentiel. Choix documentés : PV par défaut = Max du palier, équipage
    /// survivant à la destruction (débarquement forcé), débarquement 0 PA,
    /// écrasement = dégâts fixes sans jet + À Terre si mobile.
    /// </summary>
    [TestFixture]
    public class VehicleCombatTests
    {
        private static CharacterStats MakeStats(string name, int ap = 8, int hp = 20)
        {
            var attr = new Attributes(5, 4, 5, 3, 3, 2, 2, 2);
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

        private static InventoryItem MakeWeapon(string name, int baseDamage, bool heavyAmmo = false, int dice = 0)
        {
            return new InventoryItem
            {
                Name = name,
                Type = ItemType.Weapon,
                BaseDamage = baseDamage,
                HeavyAmmo = heavyAmmo,
                DamageDiceCount = dice
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

        // ------------------------------------------------------------------
        // Paliers par gabarit
        // ------------------------------------------------------------------

        [Test]
        public void ScaleBands_MatchCodexThresholds()
        {
            Assert.AreEqual(10, VehicleMechanics.HpBandFor(VehicleScale.Velo).Max);
            Assert.AreEqual(1, VehicleMechanics.HpBandFor(VehicleScale.Velo).Min);
            var charrette = VehicleMechanics.HpBandFor(VehicleScale.Charrette);
            Assert.AreEqual(11, charrette.Min);
            Assert.AreEqual(50, charrette.Max);
            var jet = VehicleMechanics.HpBandFor(VehicleScale.JetTank);
            Assert.AreEqual(51, jet.Min);
            Assert.AreEqual(1000, jet.Max);
            var croiseur = VehicleMechanics.HpBandFor(VehicleScale.Croiseur);
            Assert.AreEqual(1001, croiseur.Min);
            Assert.AreEqual(10000, croiseur.Max);
            var station = VehicleMechanics.HpBandFor(VehicleScale.Station);
            Assert.AreEqual(10001, station.Min);
            Assert.Greater(station.Max, 1000000);
        }

        [Test]
        public void ClampHp_StaysInsideBand_DefaultIsBandMax()
        {
            Assert.AreEqual(10, VehicleMechanics.ClampHp(VehicleScale.Velo, 999));
            Assert.AreEqual(1, VehicleMechanics.ClampHp(VehicleScale.Velo, -5));
            Assert.AreEqual(50, VehicleMechanics.ClampHp(VehicleScale.Charrette, 51));
            Assert.AreEqual(10, VehicleMechanics.DefaultHpFor(VehicleScale.Velo));
            Assert.AreEqual(1000, VehicleMechanics.DefaultHpFor(VehicleScale.JetTank));
            // Config custom : palier redimensionné.
            CoreRulesConfig.Instance.VehicleHpVeloMax = 6;
            Assert.AreEqual(6, VehicleMechanics.HpBandFor(VehicleScale.Velo).Max);
            Assert.AreEqual(6, VehicleMechanics.ClampHp(VehicleScale.Velo, 999));
        }

        // ------------------------------------------------------------------
        // Essentiel vs superficiel
        // ------------------------------------------------------------------

        [Test]
        public void IsEssential_EssentialVsSuperficial()
        {
            Assert.IsTrue(VehicleMechanics.IsEssential(VehicleSubsystem.Cockpit));
            Assert.IsTrue(VehicleMechanics.IsEssential(VehicleSubsystem.Moteur));
            Assert.IsTrue(VehicleMechanics.IsEssential(VehicleSubsystem.Cadre));
            Assert.IsTrue(VehicleMechanics.IsEssential(VehicleSubsystem.Rails));
            Assert.IsFalse(VehicleMechanics.IsEssential(VehicleSubsystem.Guidon));
            Assert.IsFalse(VehicleMechanics.IsEssential(VehicleSubsystem.CanonsTourelles));
            Assert.IsFalse(VehicleMechanics.IsEssential(VehicleSubsystem.Passagers));
            Assert.IsFalse(VehicleMechanics.IsEssential(VehicleSubsystem.Queue));
            Assert.IsFalse(VehicleMechanics.IsEssential(VehicleSubsystem.Roues));
        }

        [Test]
        public void EssentialHit_AtZeroHp_DestroysVehicle()
        {
            VehicleState.Register("tank-1", VehicleScale.JetTank, hpOverride: 60);
            bool ok = VehicleState.DamageSubsystem("tank-1", VehicleSubsystem.Moteur, 60, out var hit);
            Assert.IsTrue(ok);
            Assert.AreEqual(0, hit.CurrentHp);
            Assert.IsTrue(hit.IsEssentialHit);
            Assert.IsTrue(hit.Destroyed);
            Assert.IsFalse(hit.Disabled);
            Assert.IsTrue(VehicleState.IsDestroyed("tank-1"));
            StringAssert.Contains("DÉTRUIT", hit.LogFragment);
        }

        [Test]
        public void SuperficialHit_AtZeroHp_DisablesWithoutDestroying()
        {
            VehicleState.Register("bike-1", VehicleScale.Velo, hpOverride: 10);
            bool ok = VehicleState.DamageSubsystem("bike-1", VehicleSubsystem.Roues, 10, out var hit);
            Assert.IsTrue(ok);
            Assert.AreEqual(0, hit.CurrentHp);
            Assert.IsFalse(hit.IsEssentialHit);
            Assert.IsFalse(hit.Destroyed);
            Assert.IsTrue(hit.Disabled);
            Assert.IsFalse(VehicleState.IsDestroyed("bike-1"));
            // L'engin roule encore : on peut taper un autre sous-système.
            Assert.IsTrue(VehicleState.DamageSubsystem("bike-1", VehicleSubsystem.Guidon, 3, out var hit2));
            Assert.AreEqual(7, hit2.CurrentHp);
        }

        // ------------------------------------------------------------------
        // Armes lourdes +100
        // ------------------------------------------------------------------

        [Test]
        public void HeavyBonus_Plus100OnlyWhenHeavyAndVehicle()
        {
            Assert.AreEqual(100, VehicleMechanics.ComputeHeavyBonus(true, true));
            Assert.AreEqual(0, VehicleMechanics.ComputeHeavyBonus(true, false));
            Assert.AreEqual(0, VehicleMechanics.ComputeHeavyBonus(false, true));
            Assert.AreEqual(110, VehicleMechanics.ApplyHeavyDamage(10, MakeWeapon("Canon Lourd", 10), true));
            Assert.AreEqual(10, VehicleMechanics.ApplyHeavyDamage(10, MakeWeapon("Canon Lourd", 10), false));
        }

        [Test]
        public void IsHeavyWeapon_FlagsAndKeywords()
        {
            Assert.IsTrue(VehicleMechanics.IsHeavyWeapon(MakeWeapon("Fusil de Sniper Lourd", 5, heavyAmmo: true)));
            Assert.IsTrue(VehicleMechanics.IsHeavyWeapon(MakeWeapon("Canon de Bord", 14)));
            Assert.IsTrue(VehicleMechanics.IsHeavyWeapon(MakeWeapon("Mortier Léger", 6, dice: 3)));
            Assert.IsTrue(VehicleMechanics.IsHeavyWeapon(MakeWeapon("Lance-Roquettes", 8)));
            Assert.IsFalse(VehicleMechanics.IsHeavyWeapon(MakeWeapon("Pistolet 9mm", 4)));
            Assert.IsFalse(VehicleMechanics.IsHeavyWeapon(null));
        }

        [Test]
        public void HeavyBonus_ConfigurableViaCoreRules()
        {
            CoreRulesConfig.Instance.HeavyVsVehicleBonus = 50;
            Assert.AreEqual(50, VehicleMechanics.ComputeHeavyBonus(true, true));
            // Les wrappers moteurs suivent la même config.
            Assert.AreEqual(60, DamageEngineService.ApplyHeavyBonusVsVehicle(10, MakeWeapon("Canon Lourd", 10), true));
            Assert.AreEqual(60, GrenadeCalculator.ApplyHeavyBonusVsVehicle(10, MakeWeapon("Obus Thermobarique", 8), true));
            Assert.AreEqual(10, DamageEngineService.ApplyHeavyBonusVsVehicle(10, MakeWeapon("Pistolet 9mm", 4), true));
        }

        // ------------------------------------------------------------------
        // Embarquement 2 PA
        // ------------------------------------------------------------------

        [Test]
        public void Embark_CostsTwoPA_AndTracksCrew()
        {
            var rider = MakeStats("Pilote", ap: 8);
            VehicleState.Register("jet-1", VehicleScale.JetTank);
            Assert.IsTrue(VehicleState.TryEmbark(rider, "jet-1", out string log));
            Assert.AreEqual(6, rider.CurrentActionPoints);
            Assert.IsTrue(VehicleState.IsEmbarked(rider));
            Assert.AreEqual("jet-1", VehicleState.GetVehicleOf(rider));
            StringAssert.Contains("Embarquement", log);
            // Débarquement gratuit.
            Assert.IsTrue(VehicleState.Disembark(rider, out _));
            Assert.AreEqual(6, rider.CurrentActionPoints);
            Assert.IsFalse(VehicleState.IsEmbarked(rider));
        }

        [Test]
        public void Embark_RefusedWithoutPA_OnWreck_OrDouble()
        {
            var poor = MakeStats("Piéton", ap: 1);
            VehicleState.Register("cart-1", VehicleScale.Charrette);
            Assert.IsFalse(VehicleState.TryEmbark(poor, "cart-1", out string log));
            Assert.AreEqual(1, poor.CurrentActionPoints);
            Assert.IsFalse(VehicleState.IsEmbarked(poor));
            StringAssert.Contains("IMPOSSIBLE", log);

            Assert.IsFalse(VehicleState.TryEmbark(poor, "inconnu", out _));

            var rider = MakeStats("Pilote", ap: 8);
            VehicleState.Register("wreck-1", VehicleScale.Velo);
            VehicleState.DamageSubsystem("wreck-1", VehicleSubsystem.Cadre, 999, out _);
            Assert.IsTrue(VehicleState.IsDestroyed("wreck-1"));
            Assert.IsFalse(VehicleState.TryEmbark(rider, "wreck-1", out string wreckLog));
            StringAssert.Contains("épave", wreckLog);

            Assert.IsTrue(VehicleState.TryEmbark(rider, "cart-1", out _));
            Assert.IsFalse(VehicleState.TryEmbark(rider, "cart-1", out _));
        }

        [Test]
        public void DestroyedVehicle_ForceDisembarksCrew()
        {
            var rider = MakeStats("Pilote", ap: 8);
            VehicleState.Register("jet-2", VehicleScale.JetTank, hpOverride: 100);
            Assert.IsTrue(VehicleState.TryEmbark(rider, "jet-2", out _));
            VehicleState.DamageSubsystem("jet-2", VehicleSubsystem.Cockpit, 100, out _);
            Assert.IsTrue(VehicleState.IsDestroyed("jet-2"));
            Assert.IsFalse(VehicleState.IsEmbarked(rider));
            Assert.IsTrue(rider.IsAlive); // l'équipage survit (choix RD-051).
        }

        // ------------------------------------------------------------------
        // Écrasement
        // ------------------------------------------------------------------

        [Test]
        public void RunOver_MovingVehicle_DamagesAndKnocksDown()
        {
            var walker = MakeStats("Piéton", hp: 20);
            VehicleState.Register("tank-2", VehicleScale.JetTank);
            int expected = VehicleMechanics.RunOverDamageFor(VehicleScale.JetTank);
            Assert.IsTrue(VehicleState.TryRunOver("tank-2", walker, true, out string log));
            Assert.AreEqual(20 - expected, walker.CurrentHealth);
            Assert.IsTrue(walker.ActiveStatus.HasFlag(StatusEffect.ATerre));
            StringAssert.Contains("ÉCRASEMENT", log);
        }

        [Test]
        public void RunOver_StationaryVehicle_NoDamage()
        {
            var walker = MakeStats("Piéton", hp: 20);
            VehicleState.Register("cart-2", VehicleScale.Charrette);
            Assert.IsFalse(VehicleState.TryRunOver("cart-2", walker, false, out _));
            Assert.AreEqual(20, walker.CurrentHealth);
            Assert.IsFalse(walker.ActiveStatus.HasFlag(StatusEffect.ATerre));
        }

        [Test]
        public void RunOver_LethalDamage_TriggersFatalBlow()
        {
            // Station = 25 dégâts fixes : un piéton à 5 PV doit passer par
            // EvaluateFatalBlow (pas de limbo 0 PV vivant/non-mort).
            var walker = MakeStats("Piéton", hp: 5);
            VehicleState.Register("station-1", VehicleScale.Station);
            Assert.IsTrue(VehicleState.TryRunOver("station-1", walker, true, out string log));
            Assert.AreEqual(0, walker.CurrentHealth);
            Assert.IsFalse(walker.IsAlive);
            Assert.AreNotEqual(FatalBlowResolution.None, walker.LastFatalBlowResolution);
            StringAssert.Contains("ÉCRASEMENT", log);
        }

        [Test]
        public void StrikeVehicleSubsystem_AppliesHeavyBonus()
        {
            VehicleState.Register("tank-3", VehicleScale.JetTank, hpOverride: 1000);
            bool ok = SpecialManeuverService.StrikeVehicleSubsystem(
                "tank-3", VehicleSubsystem.Cadre, 10, MakeWeapon("Canon Lourd", 10), out var hit);
            Assert.IsTrue(ok);
            Assert.AreEqual(110, hit.DamageDealt);
            Assert.AreEqual(890, hit.CurrentHp);
            Assert.IsFalse(VehicleState.DamageSubsystem("inconnu", VehicleSubsystem.Cadre, 5, out _));
        }
    }
}
#endif

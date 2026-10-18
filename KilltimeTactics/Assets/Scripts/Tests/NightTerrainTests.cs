#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using NUnit.Framework;
using Killtime.Core.Character;
using Killtime.Core.Combat;
using Killtime.Core.Inventory;
using Killtime.Tactics.Visibility;

namespace Killtime.Tests
{
    /// <summary>
    /// RD-190 Nuit / lampe / bruit : toggles terrain (dev-only, 0 règle modifiée).
    /// Couvre le pur : clamp nocturne paramétrable (défaut 4 = NightSightRange),
    /// exemptions lampe/thermique, et HasLampOrThermal (porte du malus -1 duel).
    /// </summary>
    [TestFixture]
    public class NightTerrainTests
    {
        private static CharacterStats MakeStats(params string[] specs)
        {
            var attrs = new Attributes(@for: 3, agi: 3, con: 4, rap: 3, @int: 2, eru: 2, cha: 1, ins: 2, mag: 0, vision: 3, ouie: 3);
            var sheet = new CharacterSheet { Name = "Test", BaseAttributes = attrs };
            if (specs != null)
                for (int i = 0; i < specs.Length; i++) sheet.UnlockedSpecializations.Add(specs[i]);
            return new CharacterStats("Test", attrs, 0, sheet);
        }

        private static CharacterStats MakeStatsWithItem(string itemName)
        {
            var st = MakeStats();
            st.Sheet.Inventory.Add(new InventoryItem { Name = itemName });
            return st;
        }

        [Test]
        public void Night_ClampsTo4ByDefault()
        {
            var st = MakeStats();
            var p = FogOfWarSystem.ResolveObserverParams(st, FogOfWarSystem.LongSightRange, isNight: true);
            Assert.AreEqual(4, p.Range);
        }

        [Test]
        public void Night_RespectsCustomNightRange()
        {
            var st = MakeStats();
            Assert.AreEqual(6, FogOfWarSystem.ResolveObserverParams(st, 12, isNight: true, nightRange: 6).Range);
            Assert.AreEqual(2, FogOfWarSystem.ResolveObserverParams(st, 12, isNight: true, nightRange: 2).Range);
            // Le jour ignore le paramètre nuit.
            Assert.AreEqual(12, FogOfWarSystem.ResolveObserverParams(st, 12, isNight: false, nightRange: 2).Range);
            // Le clamp ne remonte jamais au-dessus de la portée jour.
            Assert.AreEqual(3, FogOfWarSystem.ResolveObserverParams(st, 3, isNight: true, nightRange: 6).Range);
        }

        [Test]
        public void Night_ThermalKeepsDayRange()
        {
            var st = MakeStats(FogOfWarSystem.SpecThermal);
            Assert.AreEqual(12, FogOfWarSystem.ResolveObserverParams(st, 12, isNight: true).Range);
        }

        [Test]
        public void Night_LampInInventoryKeepsDayRange()
        {
            var st = MakeStatsWithItem("Lampe Torche");
            var p = FogOfWarSystem.ResolveObserverParams(st, 12, isNight: true);
            Assert.AreEqual(12, p.Range);
            Assert.IsTrue(p.HasLight);
        }

        [Test]
        public void HasLampOrThermal_PlainFalse()
        {
            Assert.IsFalse(DuelSetupService.HasLampOrThermal(MakeStats()));
        }

        [Test]
        public void HasLampOrThermal_LampTrue()
        {
            Assert.IsTrue(DuelSetupService.HasLampOrThermal(MakeStatsWithItem("Lampe Torche")));
        }

        [Test]
        public void HasLampOrThermal_ThermalSpecTrue()
        {
            Assert.IsTrue(DuelSetupService.HasLampOrThermal(MakeStats(FogOfWarSystem.SpecThermal)));
        }

        [Test]
        public void HasLampOrThermal_ThermalGogglesTrue()
        {
            Assert.IsTrue(DuelSetupService.HasLampOrThermal(MakeStatsWithItem("Jumelles Thermiques X-9")));
        }
    }
}
#endif

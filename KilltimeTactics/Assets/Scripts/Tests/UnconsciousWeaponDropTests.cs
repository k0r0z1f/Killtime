#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using NUnit.Framework;
using Killtime.Core.Character;
using Killtime.Core.Inventory;

namespace Killtime.Tests
{
    [TestFixture]
    public class UnconsciousWeaponDropTests
    {
        [Test]
        public void ExtractEquippedItemsForGroundDrop_RemovesArmeEquipee_EtLaRetourne()
        {
            var sheet = new CharacterSheet();
            var sword = new InventoryItem { Name = "Épée Métal", Type = ItemType.Weapon, IsEquipped = true, WeightKg = 1.5f };
            var dagger = new InventoryItem { Name = "Dague", Type = ItemType.Weapon, IsEquipped = false };
            sheet.AddItem(sword);
            sheet.AddItem(dagger);

            var dropped = sheet.ExtractEquippedItemsForGroundDrop();

            Assert.AreEqual(1, dropped.Count);
            Assert.AreEqual("Épée Métal", dropped[0].Name);
            Assert.IsFalse(dropped[0].IsEquipped, "L'arme au sol ne doit plus être marquée équipée");
            Assert.IsNull(sheet.GetEquippedWeapon(), "Plus aucune arme équipée après K.O.");
            Assert.AreEqual(1, sheet.Inventory.Count, "L'arme lâchée est retirée de l'inventaire (exemplaire unique au sol)");
            Assert.AreEqual("Dague", sheet.Inventory[0].Name);
        }

        [Test]
        public void ExtractEquippedItemsForGroundDrop_SansArme_RetourneVide()
        {
            var sheet = new CharacterSheet();
            sheet.AddItem(new InventoryItem { Name = "Ration", Type = ItemType.Consumable, IsEquipped = false });

            var dropped = sheet.ExtractEquippedItemsForGroundDrop();

            Assert.IsNotNull(dropped);
            Assert.AreEqual(0, dropped.Count);
            Assert.AreEqual(1, sheet.Inventory.Count);
        }

        [Test]
        public void ExtractEquippedItemsForGroundDrop_NeTouchePas_ConsommablesEquipes()
        {
            var sheet = new CharacterSheet();
            var grenade = new InventoryItem { Name = "Grenade Frag", Type = ItemType.Weapon, Category = "Grenades", IsEquipped = true };
            sheet.AddItem(grenade);

            // Les grenades sont des armes lançables : elles tombent aussi (main ouverte).
            var dropped = sheet.ExtractEquippedItemsForGroundDrop();
            Assert.AreEqual(1, dropped.Count);
        }

        [Test]
        public void Inconscient_RendUniteIncapableDeGarderSonArme_Logique()
        {
            // Garde logique : le visuel (TacticalUnitVisual) appelle
            // ExtractEquippedItemsForGroundDrop dès que IsAlive==false ou Inconscient.
            var attrs = new Attributes(@for: 3, agi: 3, con: 4, rap: 3, @int: 2, eru: 2, cha: 1, ins: 2, mag: 0);
            var sheet = new CharacterSheet { Name = "K.O. Test" };
            var rifle = new InventoryItem { Name = "Fusil Laser", Type = ItemType.Weapon, IsEquipped = true, WeightKg = 2.8f };
            sheet.AddItem(rifle);
            var stats = new CharacterStats("K.O. Test", attrs, 0, sheet);

            // K.O. via statut (Livre VII) : plus vivant => doit lâcher.
            stats.ApplyStatus(StatusEffect.Inconscient);
            Assert.IsFalse(stats.IsAlive, "Inconscient => IsAlive faux");
            var dropped = sheet.ExtractEquippedItemsForGroundDrop();
            Assert.AreEqual(1, dropped.Count, "Le K.O. doit extraire l'arme pour chute physique");
        }
    }
}
#endif

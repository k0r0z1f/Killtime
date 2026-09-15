#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using NUnit.Framework;
using Killtime.Core.Character;
using Killtime.Core.Inventory;

namespace Killtime.Tests
{
    [TestFixture]
    public class InventorySystemTests
    {
        [Test]
        public void CharacterSheet_CanAddEquipAndUnequipWeapon()
        {
            var sheet = new CharacterSheet();
            var rifle = new InventoryItem
            {
                Name = "Fusil Gauss",
                PrefabPath = "Guns/GaussRifle",
                Type = ItemType.Weapon,
                BaseDamage = 10,
                IsEquipped = false
            };

            sheet.AddItem(rifle);
            Assert.AreEqual(1, sheet.Inventory.Count);
            Assert.IsNull(sheet.GetEquippedWeapon());

            bool equipped = sheet.EquipItem(rifle.ItemId);
            Assert.IsTrue(equipped);
            Assert.IsNotNull(sheet.GetEquippedWeapon());
            Assert.AreEqual("Fusil Gauss", sheet.GetEquippedWeapon().Name);
            Assert.AreEqual(10, sheet.GetEquippedWeapon().BaseDamage);

            sheet.UnequipItem(rifle.ItemId);
            Assert.IsNull(sheet.GetEquippedWeapon());
        }

        [Test]
        public void CharacterStorageService_SavesAndLoadsCharacterWithInventory()
        {
            var sheet = new CharacterSheet
            {
                Name = "Guerrier Testeur",
                BaseArmor = 2
            };

            var rifle = new InventoryItem
            {
                Name = "Fusil Gauss MK2",
                PrefabPath = "Guns/GaussRifle",
                Type = ItemType.Weapon,
                BaseDamage = 11,
                RangeInTiles = 12,
                WeightKg = 3.2f,
                IsEquipped = true
            };

            var knife = new InventoryItem
            {
                Name = "Dague Combat",
                Type = ItemType.Weapon,
                BaseDamage = 4,
                IsEquipped = false
            };

            sheet.AddItem(rifle);
            sheet.AddItem(knife);

            string path = CharacterStorageService.SaveCharacter(sheet);
            Assert.IsTrue(System.IO.File.Exists(path));

            var loaded = CharacterStorageService.LoadCharacter(path);
            Assert.IsNotNull(loaded);
            Assert.AreEqual("Guerrier Testeur", loaded.Name);
            Assert.AreEqual(2, loaded.Inventory.Count);

            var equipped = loaded.GetEquippedWeapon();
            Assert.IsNotNull(equipped);
            Assert.AreEqual("Fusil Gauss MK2", equipped.Name);
            Assert.AreEqual(11, equipped.BaseDamage);
            Assert.AreEqual(12, equipped.RangeInTiles);

            CharacterStorageService.DeleteCharacter(path);
        }

        [Test]
        public void CharacterSheet_EquippingNewWeapon_UnequipsPreviousWeapon()
        {
            var sheet = new CharacterSheet();
            var pistol = new InventoryItem { Name = "Pistolet 9mm", Type = ItemType.Weapon };
            var shotgun = new InventoryItem { Name = "Pompe Lourd", Type = ItemType.Weapon };

            sheet.AddItem(pistol);
            sheet.AddItem(shotgun);

            sheet.EquipItem(pistol.ItemId);
            Assert.AreEqual("Pistolet 9mm", sheet.GetEquippedWeapon().Name);

            sheet.EquipItem(shotgun.ItemId);
            Assert.AreEqual("Pompe Lourd", sheet.GetEquippedWeapon().Name);
            Assert.IsFalse(pistol.IsEquipped);
            Assert.IsTrue(shotgun.IsEquipped);
        }

        [Test]
        public void InventoryItem_Clone_CreatesDistinctInstance()
        {
            var original = new InventoryItem
            {
                Name = "Revolver Magnum",
                PrefabPath = "Guns/Magnum",
                BaseDamage = 9
            };

            var clone = original.Clone();
            Assert.AreNotEqual(original.ItemId, clone.ItemId);
            Assert.AreEqual(original.Name, clone.Name);
            Assert.AreEqual(original.BaseDamage, clone.BaseDamage);
            Assert.AreEqual(original.PrefabPath, clone.PrefabPath);
        }

        [Test]
        public void ArmoryCatalog_ContainsExhaustiveLivreVIII()
        {
            var all = ArmoryCatalog.All;
            Assert.GreaterOrEqual(all.Count, 60, "Le catalogue doit couvrir tout l'inventaire possible (armes, munitions, armures, pharma, outils, quête).");
            Assert.IsNotNull(ArmoryCatalog.GetByName("Deglazer"));
            Assert.IsNotNull(ArmoryCatalog.GetByName("Épée Laser Suprême 6D"));
            Assert.IsNotNull(ArmoryCatalog.GetByName("Armure Lourde 7 Optimisée (1 PA)"));
            Assert.IsNotNull(ArmoryCatalog.GetByName("Seringue de soins 10 PV"));
            Assert.IsNotNull(ArmoryCatalog.GetByName("Charge Laser Standard (x10)"));
            Assert.IsNotNull(ArmoryCatalog.GetByName("Kit de Chirurgie de Campagne"));

            var deglazer = ArmoryCatalog.GetByName("Deglazer");
            Assert.AreEqual(7, deglazer.BaseDamage);
            Assert.AreEqual(1, deglazer.AttackBonusEc);
            Assert.AreEqual(200000, deglazer.PriceCE);
            Assert.AreEqual(ItemType.Weapon, deglazer.Type);
        }

        [Test]
        public void ArmoryCatalog_BuyAndSell_UpdatesCredits()
        {
            var sheet = new CharacterSheet { Name = "Marchand Test", CreditsCE = 50000 };
            bool bought = ArmoryCatalog.BuyForSheet(sheet, "Épée Métal Courte", out string buyMsg);
            Assert.IsTrue(bought, buyMsg);
            Assert.AreEqual(49500, sheet.CreditsCE);
            Assert.AreEqual(1, sheet.Inventory.Count);

            string id = sheet.Inventory[0].ItemId;
            bool sold = ArmoryCatalog.SellFromSheet(sheet, id, out string sellMsg);
            Assert.IsTrue(sold, sellMsg);
            Assert.AreEqual(0, sheet.Inventory.Count);
            Assert.AreEqual(49500 + 250, sheet.CreditsCE); // revente 50% de 500 CE

            // Nettoyage : supprime la fiche sauvegardée par Buy/Sell (préfixe SheetId).
            try
            {
                string prefix = sheet.SheetId.Length >= 8 ? sheet.SheetId.Substring(0, 8) : sheet.SheetId;
                foreach (var f in CharacterStorageService.GetSavedCharacterFiles())
                    if (f.Contains(prefix)) CharacterStorageService.DeleteCharacter(f);
            }
            catch { /* nettoyage best-effort */ }
        }

        [Test]
        public void InventoryItem_Clone_PreservesMarketFields()
        {
            var def = ArmoryCatalog.GetByName("Champ de Force Personnel 2");
            Assert.IsNotNull(def);
            var clone = def.Clone();
            Assert.AreEqual(def.PriceCE, clone.PriceCE);
            Assert.AreEqual(def.ShieldHP, clone.ShieldHP);
            Assert.AreEqual(def.PlaceholderKind, clone.PlaceholderKind);
            Assert.AreNotEqual(def.ItemId, clone.ItemId);
        }
    }
}
#endif
#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using NUnit.Framework;
using System.Collections.Generic;
using Killtime.Core.Character;
using Killtime.Core.Inventory;

namespace Killtime.Tests
{
    [TestFixture]
    public class ArmoryJsonTests
    {
        private const string SampleJson = @"{
  ""version"": 1,
  ""source"": ""test"",
  ""items"": [
    { ""name"": ""Deglazer Test"", ""dlph"": ""+1ec 7D"", ""dmg"": 7, ""weightKg"": 1.8,
      ""rangeTiles"": 20, ""priceCE"": 200000, ""type"": ""Weapon"", ""skill"": ""Ballistique"",
      ""slot"": ""TwoHands"", ""category"": ""Fusils Laser"", ""placeholder"": ""Deglazer"",
      ""prefab"": ""SciFiGunHeavy_White"", ""desc"": ""Prisme nytharite."", ""rarity"": ""Legendaire"",
      ""armor"": 0, ""shield"": 0, ""heal"": 0, ""bonusEc"": 1, ""stackable"": false, ""qty"": 1,
      ""apMod"": 0, ""isGrenade"": false, ""isLauncher"": false, ""era"": """", ""kind"": """",
      ""blast"": 0, ""dice"": 0, ""shrapnel"": 0, ""statuses"": """", ""zoneTurns"": 0,
      ""launcherCompatible"": false, ""launcherRangeBonus"": 0, ""accuracyBonus"": 0 },
    { ""name"": ""Grenade Test"", ""dlph"": ""10D"", ""dmg"": 10, ""weightKg"": 0.23,
      ""rangeTiles"": 8, ""priceCE"": 3500, ""type"": ""Weapon"", ""skill"": ""Ballistique"",
      ""slot"": ""MainHand"", ""category"": ""Grenades"", ""placeholder"": ""Grenade"",
      ""prefab"": """", ""desc"": ""Zone."", ""rarity"": ""Militaire"",
      ""armor"": 0, ""shield"": 0, ""heal"": 0, ""bonusEc"": 0, ""stackable"": true, ""qty"": 1,
      ""apMod"": 0, ""isGrenade"": true, ""isLauncher"": false, ""era"": ""Moderne"",
      ""kind"": ""Fragmentation"", ""blast"": 2, ""dice"": 2, ""shrapnel"": 2,
      ""statuses"": ""Destabilise,Saignement"", ""zoneTurns"": 0,
      ""launcherCompatible"": true, ""launcherRangeBonus"": 0, ""accuracyBonus"": 0 }
  ]
}";

        [Test]
        public void ArmoryJson_ParseSample_PreservesAllFields()
        {
            bool ok = ArmoryJson.TryParse(SampleJson, out var items, out string err);
            Assert.IsTrue(ok, err);
            Assert.AreEqual(2, items.Count);

            var deg = items[0];
            Assert.AreEqual("Deglazer Test", deg.Name);
            Assert.AreEqual(7, deg.BaseDamage);
            Assert.AreEqual(1, deg.AttackBonusEc);
            Assert.AreEqual(200000, deg.PriceCE);
            Assert.AreEqual(1.8f, deg.WeightKg);
            Assert.AreEqual(20, deg.RangeInTiles);
            Assert.AreEqual(ItemType.Weapon, deg.Type);
            Assert.AreEqual(ItemEquipSlot.TwoHands, deg.EquipSlot);
            Assert.AreEqual("SciFiGunHeavy_White", deg.PrefabPath);
            StringAssert.Contains("+1ec", deg.Description);
            StringAssert.Contains("200000 CE", deg.Description);

            var gr = items[1];
            Assert.IsTrue(gr.IsGrenade);
            Assert.IsTrue(gr.IsStackable);
            Assert.AreEqual(2, gr.BlastRadius);
            Assert.AreEqual(2, gr.DamageDiceCount);
            Assert.IsTrue(gr.IsThrowableGrenade());
            StringAssert.Contains("Blast 2", gr.Description);
        }

        [Test]
        public void ArmoryJson_RoundTrip_PreservesFields()
        {
            ArmoryJson.TryParse(SampleJson, out var items, out _);
            var entry = ArmoryJson.ToEntry(items[0]);
            Assert.AreEqual("Deglazer Test", entry.name);
            Assert.AreEqual(1, entry.bonusEc);
            Assert.AreEqual("Legendaire", entry.rarity);

            bool ok = ArmoryJson.TryToItem(entry, out var back, out string err);
            Assert.IsTrue(ok, err);
            Assert.AreEqual(items[0].Name, back.Name);
            Assert.AreEqual(items[0].BaseDamage, back.BaseDamage);
            Assert.AreEqual(items[0].AttackBonusEc, back.AttackBonusEc);
            Assert.AreEqual(items[0].Description, back.Description);
        }

        [Test]
        public void ArmoryJson_BaseDesc_StripsSuffix_AndRefreshIsIdempotent()
        {
            ArmoryJson.TryParse(SampleJson, out var items, out _);
            var deg = items[0];
            Assert.AreEqual("Prisme nytharite.", ArmoryJson.BaseDesc(deg));
            string before = deg.Description;
            ArmoryJson.RefreshDescription(deg);
            Assert.AreEqual(before, deg.Description);
        }

        [Test]
        public void ArmoryJson_RejectsBadEnumDuplicateAndEmpty()
        {
            string badSkill = SampleJson.Replace(@"""skill"": ""Ballistique""", @"""skill"": ""TirLaser""");
            Assert.IsFalse(ArmoryJson.TryParse(badSkill, out _, out string err1));
            StringAssert.Contains("compétence", err1);

            string dupe = SampleJson.Replace(@"""name"": ""Grenade Test""", @"""name"": ""Deglazer Test""");
            Assert.IsFalse(ArmoryJson.TryParse(dupe, out _, out string err2));
            StringAssert.Contains("Doublon", err2);

            Assert.IsFalse(ArmoryJson.TryParse(@"{""version"":1,""items"":[]}", out _, out _));
        }

        [Test]
        public void ArmoryJson_ValidateAll_CatchesUnknownCategory()
        {
            ArmoryJson.TryParse(SampleJson, out var items, out _);
            Assert.IsTrue(ArmoryJson.ValidateAll(items, out _));
            items[0].Category = "Bidule Inconnu";
            Assert.IsFalse(ArmoryJson.ValidateAll(items, out string msg));
            StringAssert.Contains("catégorie", msg);
        }

        [Test]
        public void ArmoryCatalog_LoadsFullMarketplace_FromJsonOrFallback()
        {
            var all = ArmoryCatalog.All;
            Assert.GreaterOrEqual(all.Count, 110, "Le marketplace entier doit être enregistré (JSON ou repli codé).");
            Assert.IsFalse(string.IsNullOrEmpty(ArmoryCatalog.LoadedFrom));
            var names = new HashSet<string>();
            foreach (var it in all) names.Add(it.Name);
            Assert.IsTrue(names.Contains("Deglazer"));
            Assert.IsTrue(names.Contains("M79 Thumper (1961)"));
            Assert.IsTrue(names.Contains("M18 Fumigène"));
            Assert.IsTrue(names.Contains("Seringue de soins 10 PV"));
            Assert.IsTrue(names.Contains("Armure Lourde 7 Optimisée (1 PA)"));
            Assert.IsTrue(names.Contains("Charge Laser Standard (x10)"));
        }

        [Test]
        public void ArmoryJson_AmmoFields_RoundTrip()
        {
            var weapon = new InventoryItem
            {
                Name = "Fusil Test",
                DlphCode = "4D",
                BaseDamage = 4,
                WeightKg = 1.35f,
                RangeInTiles = 20,
                PriceCE = 20000,
                Type = ItemType.Weapon,
                AssociatedSkill = SkillType.Ballistique,
                EquipSlot = ItemEquipSlot.TwoHands,
                Category = "Fusils Laser",
                PlaceholderKind = "RifleLaser",
                Description = "Banc d'essai. [4D — 20000 CE]",
                Rarity = ItemRarity.Militaire,
                AmmoCapacity = 20,
                AmmoType = "Charge Laser",
                AmmoRemaining = 7,
                LoadedHD = true,
                HeavyAmmo = true,
                ReloadAPCost = 2
            };
            var entry = ArmoryJson.ToEntry(weapon);
            Assert.AreEqual(20, entry.ammoCapacity);
            Assert.AreEqual("Charge Laser", entry.ammoType);
            Assert.AreEqual(2, entry.reloadCost);
            Assert.IsTrue(entry.heavyAmmo);

            bool ok = ArmoryJson.TryToItem(entry, out var back, out string err);
            Assert.IsTrue(ok, err);
            Assert.AreEqual(20, back.AmmoCapacity);
            Assert.AreEqual(20, back.AmmoRemaining);
            Assert.AreEqual("Charge Laser", back.AmmoType);
            Assert.IsFalse(back.LoadedHD);
            Assert.IsFalse(back.Jammed);
            Assert.IsTrue(back.Description.Contains("20000 CE"));
        }

        [Test]
        public void ArmoryJson_AmmoValidation()
        {
            var okItem = new InventoryItem { Name = "OK", Category = "Fusils Laser", RangeInTiles = 20, AmmoCapacity = 10, AmmoType = "Charge Laser" };
            Assert.IsTrue(ArmoryJson.ValidateAll(new List<InventoryItem> { okItem }, out _));
            var badItem = new InventoryItem { Name = "KO", Category = "Fusils Laser", RangeInTiles = 20, AmmoCapacity = 10, AmmoType = "" };
            Assert.IsFalse(ArmoryJson.ValidateAll(new List<InventoryItem> { badItem }, out string msg));
            StringAssert.Contains("munition", msg);
        }

        [Test]
        public void WeaponAmmo_StockAndTake_IsExact()
        {
            var sheet = new CharacterSheet();
            sheet.AddItem(new InventoryItem { Name = "Charge Laser Standard (x10)", Type = ItemType.Ammunition, Quantity = 5 });
            sheet.AddItem(new InventoryItem { Name = "Charge Laser Haute Densité (x10)", Type = ItemType.Ammunition, Quantity = 3 });
            sheet.AddItem(new InventoryItem { Name = "Carquois Flèches (x20)", Type = ItemType.Ammunition, Quantity = 7 });

            Assert.AreEqual(5, Killtime.Core.Combat.WeaponAmmo.StockShots(sheet, "Charge Laser", false));
            Assert.AreEqual(3, Killtime.Core.Combat.WeaponAmmo.StockShots(sheet, "Charge Laser", true));
            Assert.AreEqual(7, Killtime.Core.Combat.WeaponAmmo.StockShots(sheet, "Carquois Flèches", false));

            int taken = Killtime.Core.Combat.WeaponAmmo.TakeShots(sheet, "Charge Laser", false, 4);
            Assert.AreEqual(4, taken);
            Assert.AreEqual(1, Killtime.Core.Combat.WeaponAmmo.StockShots(sheet, "Charge Laser", false));

            // Prélèvement multi-piles exact, sans gaspillage.
            sheet.AddItem(new InventoryItem { Name = "Charge Laser Standard (x10)", Type = ItemType.Ammunition, Quantity = 10 });
            taken = Killtime.Core.Combat.WeaponAmmo.TakeShots(sheet, "Charge Laser", false, 7);
            Assert.AreEqual(7, taken);
            Assert.AreEqual(4, Killtime.Core.Combat.WeaponAmmo.StockShots(sheet, "Charge Laser", false));
        }

        [Test]
        public void WeaponAmmo_UsesAmmo_OnlyForShots()
        {
            var gun = new InventoryItem { Name = "Fusil", Type = ItemType.Weapon, AmmoCapacity = 20, AmmoType = "Charge Laser" };
            Assert.IsTrue(Killtime.Core.Combat.WeaponAmmo.UsesAmmo(gun, SkillType.Ballistique));
            Assert.IsFalse(Killtime.Core.Combat.WeaponAmmo.UsesAmmo(gun, SkillType.ManiementArmes));
            var sword = new InventoryItem { Name = "Épée", Type = ItemType.Weapon, AmmoCapacity = 0 };
            Assert.IsFalse(Killtime.Core.Combat.WeaponAmmo.UsesAmmo(sword, SkillType.Ballistique));
            Assert.IsFalse(Killtime.Core.Combat.WeaponAmmo.UsesAmmo(null, SkillType.Ballistique));
        }

        [Test]
        public void ArmoryCatalog_AmmoSpecs_LivreVIII()
        {
            var sniper = ArmoryCatalog.GetByName("Fusil de Sniper Lourd");
            Assert.IsNotNull(sniper);
            Assert.AreEqual(10, sniper.AmmoCapacity);
            Assert.AreEqual("Charge Laser", sniper.AmmoType);
            Assert.AreEqual(10, sniper.AmmoRemaining);
            Assert.IsTrue(sniper.HeavyAmmo);

            var deglazer = ArmoryCatalog.GetByName("Deglazer");
            Assert.IsTrue(deglazer.HeavyAmmo);
            Assert.AreEqual(10, deglazer.AmmoCapacity);

            var pistol = ArmoryCatalog.GetByName("Fusil Laser Léger");
            Assert.AreEqual(10, pistol.AmmoCapacity);
            Assert.IsFalse(pistol.HeavyAmmo);

            var assault = ArmoryCatalog.GetByName("Fusil d'Assaut Laser");
            Assert.AreEqual(20, assault.AmmoCapacity);

            var bow = ArmoryCatalog.GetByName("Arc Composite Renforcé");
            Assert.AreEqual(1, bow.AmmoCapacity);
            Assert.AreEqual("Carquois Flèches", bow.AmmoType);
            Assert.AreEqual(2, bow.ReloadAPCost);

            var crossbow = ArmoryCatalog.GetByName("Arbalète Lourde");
            Assert.AreEqual(3, crossbow.ReloadAPCost);

            var sword = ArmoryCatalog.GetByName("Épée Métal Courte");
            Assert.AreEqual(0, sword.AmmoCapacity);
        }

        [Test]
        public void ArmoryCatalog_BuyAmmo_GrantsShots()
        {
            var sheet = new CharacterSheet { Name = "Marchand Test", CreditsCE = 50000 };
            try
            {
                Assert.IsTrue(ArmoryCatalog.BuyForSheet(sheet, "Charge Laser Standard (x10)", out string m1), m1);
                Assert.AreEqual(10, sheet.Inventory[sheet.Inventory.Count - 1].Quantity);
                Assert.IsTrue(ArmoryCatalog.BuyForSheet(sheet, "Carquois Flèches (x20)", out string m2), m2);
                Assert.AreEqual(20, sheet.Inventory[sheet.Inventory.Count - 1].Quantity);
                Assert.AreEqual(30, Killtime.Core.Combat.WeaponAmmo.StockShots(sheet, "Charge Laser", false)
                    + Killtime.Core.Combat.WeaponAmmo.StockShots(sheet, "Carquois Flèches", false));
            }
            finally
            {
                try
                {
                    string prefix = sheet.SheetId.Length >= 8 ? sheet.SheetId.Substring(0, 8) : sheet.SheetId;
                    foreach (var f in CharacterStorageService.GetSavedCharacterFiles())
                        if (f.Contains(prefix)) CharacterStorageService.DeleteCharacter(f);
                }
                catch { /* nettoyage best-effort */ }
            }
        }

        [Test]
        public void ArmorCatalog_Specs_LivreVIII()
        {
            var legere = ArmoryCatalog.GetByName("Armure Légère 2");
            Assert.IsNotNull(legere);
            Assert.AreEqual(2, legere.ArmorProtection);
            Assert.AreEqual(0, legere.ApCostModifier);

            var lourde6 = ArmoryCatalog.GetByName("Armure Lourde 6 (2 PA)");
            Assert.IsNotNull(lourde6);
            Assert.AreEqual(6, lourde6.ArmorProtection);
            Assert.AreEqual(2, lourde6.ApCostModifier);

            var opti = ArmoryCatalog.GetByName("Armure Lourde 7 Optimisée (1 PA)");
            Assert.AreEqual(7, opti.ArmorProtection);
            Assert.AreEqual(1, opti.ApCostModifier);

            var space = ArmoryCatalog.GetByName("Spacesuit Lourd 3-6");
            Assert.AreEqual(6, space.ArmorProtection);
            Assert.AreEqual(2, space.ApCostModifier);

            var champ3 = ArmoryCatalog.GetByName("Champ de Force Personnel 3");
            Assert.IsNotNull(champ3);
            Assert.AreEqual(30, champ3.ShieldHP);

            var milkor = ArmoryCatalog.GetByName("Milkor MGL Mk1");
            Assert.IsNotNull(milkor);
            Assert.AreEqual(-1, milkor.ApCostModifier);
        }

        [Test]
        public void Armor_EquipExclusivity_AndWornBonus()
        {
            var sheet = new CharacterSheet();
            var a = new InventoryItem { Name = "Légère", Type = ItemType.Armor, ArmorProtection = 2 };
            var b = new InventoryItem { Name = "Lourde", Type = ItemType.Armor, ArmorProtection = 5, ApCostModifier = 1 };
            sheet.AddItem(a);
            sheet.AddItem(b);
            Assert.IsTrue(sheet.EquipItem(a.ItemId));
            Assert.AreEqual(a, sheet.GetEquippedArmor());
            Assert.IsTrue(sheet.EquipItem(b.ItemId));
            Assert.AreEqual(b, sheet.GetEquippedArmor());
            Assert.IsFalse(a.IsEquipped);

            var stats = sheet.ToCombatStats();
            Assert.AreEqual(5, stats.GetWornArmorBonus());
            Assert.AreEqual(1, stats.GetWornApMalus());
        }

        [Test]
        public void Shield_AbsorbRefillRecalc()
        {
            var sheet = new CharacterSheet();
            var champ = new InventoryItem { Name = "Champ", Type = ItemType.Arcanotech, ShieldHP = 20 };
            sheet.AddItem(champ);
            Assert.IsTrue(sheet.EquipItem(champ.ItemId));
            Assert.AreEqual(champ, sheet.GetEquippedShield());

            var stats = sheet.ToCombatStats();
            stats.RecalcShieldMax();
            Assert.AreEqual(20, stats.MaxShieldHP);
            Assert.AreEqual(20, stats.CurrentShieldHP);

            int rest = stats.AbsorbShield(7);
            Assert.AreEqual(0, rest);
            Assert.AreEqual(13, stats.CurrentShieldHP);
            rest = stats.AbsorbShield(30);
            Assert.AreEqual(17, rest);
            Assert.AreEqual(0, stats.CurrentShieldHP);

            stats.RefillShield();
            Assert.AreEqual(20, stats.CurrentShieldHP);

            sheet.UnequipItem(champ.ItemId);
            stats.RecalcShieldMax();
            Assert.AreEqual(0, stats.MaxShieldHP);
            Assert.AreEqual(0, stats.CurrentShieldHP);
        }

        [Test]
        public void Armor_ApMalus_AppliesToActions_NotStakes()
        {
            var sheet = new CharacterSheet();
            var lourde = new InventoryItem { Name = "Lourde", Type = ItemType.Armor, ArmorProtection = 3, ApCostModifier = 1 };
            sheet.AddItem(lourde);
            sheet.EquipItem(lourde.ItemId);
            var stats = sheet.ToCombatStats();
            stats.CurrentActionPoints = 10;

            Assert.IsTrue(stats.ConsumeActionPoints(2));
            Assert.AreEqual(7, stats.CurrentActionPoints); // 2 + 1 malus
            Assert.IsTrue(stats.ConsumeActionPoints(1, true));
            Assert.AreEqual(6, stats.CurrentActionPoints); // mise exempte

            sheet.UnequipItem(lourde.ItemId);
            Assert.IsTrue(stats.ConsumeActionPoints(2));
            Assert.AreEqual(4, stats.CurrentActionPoints); // sans malus
        }

        [Test]
        public void ArmoryCatalog_BuyArmor_AutoEquips()
        {
            var sheet = new CharacterSheet { Name = "Marchand Test", CreditsCE = 100000 };
            try
            {
                Assert.IsTrue(ArmoryCatalog.BuyForSheet(sheet, "Armure Légère 2", out string m1), m1);
                Assert.IsNotNull(sheet.GetEquippedArmor());
                Assert.AreEqual("Armure Légère 2", sheet.GetEquippedArmor().Name);
                Assert.IsTrue(ArmoryCatalog.BuyForSheet(sheet, "Champ de Force Personnel 1", out string m2), m2);
                Assert.IsNotNull(sheet.GetEquippedShield());
                var stats = sheet.ToCombatStats();
                stats.RecalcShieldMax();
                Assert.AreEqual(10, stats.MaxShieldHP);
                Assert.AreEqual(2, stats.GetWornArmorBonus());
            }
            finally
            {
                try
                {
                    string prefix = sheet.SheetId.Length >= 8 ? sheet.SheetId.Substring(0, 8) : sheet.SheetId;
                    foreach (var f in CharacterStorageService.GetSavedCharacterFiles())
                        if (f.Contains(prefix)) CharacterStorageService.DeleteCharacter(f);
                }
                catch { /* nettoyage best-effort */ }
            }
        }

        [Test]
        public void Equipment_GasMask_FiltersTearGasAndPoisons_EvenWithArmorEquipped()
        {
            var sheet = new CharacterSheet();
            var armor = new InventoryItem { Name = "Armure Légère 2", Type = ItemType.Armor, ArmorProtection = 2 };
            var mask = new InventoryItem { Name = "Masque Filtrant", Type = ItemType.Armor, ArmorProtection = 0 };
            sheet.AddItem(armor);
            sheet.AddItem(mask);
            sheet.EquipItem(armor.ItemId);

            var stats = sheet.ToCombatStats();
            Assert.IsTrue(Killtime.Core.Combat.SmokeScreen.HasGasMask(stats));

            var gasStatuses = StatusEffect.Asphyxie | StatusEffect.Aveugle | StatusEffect.Empoisonne;
            var filtered = Killtime.Core.Combat.SmokeScreen.FilterGasStatuses(gasStatuses, "Gaz", false);
            Assert.AreEqual(StatusEffect.None, filtered);

            var toxicDamage = Killtime.Core.Combat.SmokeScreen.HalveGasDamage(6, "Gaz", false);
            Assert.AreEqual(0, toxicDamage);
        }

        [Test]
        public void Equipment_ThermalGoggles_ActivatesThermalVision()
        {
            var sheet = new CharacterSheet();
            sheet.AddItem(new InventoryItem { Name = "Jumelles Thermiques", Type = ItemType.Misc });
            var stats = sheet.ToCombatStats();

            var visionParams = Killtime.Tactics.Visibility.FogOfWarSystem.ResolveObserverParams(stats, 12);
            Assert.IsTrue(visionParams.Thermal);
        }

        [Test]
        public void Equipment_ArcanotechGas_DamageHalvedInsteadOfNullified()
        {
            var toxicDamage = Killtime.Core.Combat.SmokeScreen.HalveGasDamage(6, "Gaz", true);
            Assert.AreEqual(3, toxicDamage);

            var gasStatuses = StatusEffect.Asphyxie | StatusEffect.Empoisonne;
            var filtered = Killtime.Core.Combat.SmokeScreen.FilterGasStatuses(gasStatuses, "Gaz", true);
            Assert.IsTrue(filtered.HasFlag(StatusEffect.Empoisonne));
            Assert.IsFalse(filtered.HasFlag(StatusEffect.Asphyxie));
        }
    }
}
#endif

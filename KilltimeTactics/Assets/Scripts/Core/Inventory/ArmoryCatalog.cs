using System;
using System.Collections.Generic;
using Killtime.Core.Character;

namespace Killtime.Core.Inventory
{
    /// <summary>
    /// Catalogue exhaustif de l'Armurerie Killtime (Livre VIII, Chap. 30-32).
    /// Source unique pour : Inventaire PJ (F1/F5), Marché, placeholders 3D.
    /// Les seuls prefabs réels aujourd'hui sont sous Resources/Guns (fusils sci-fi) :
    /// tout le reste passe par <see cref="ArmoryPlaceholderFactory"/> (PlaceholderKind).
    /// Notation DLPH : [D]égâts - [L]ivres (0.45kg) - [P]ortée (m=cases) - [H] mains.
    /// </summary>
    public static class ArmoryCatalog
    {
        public const float LivresToKg = 0.45f;
        public const int MaxTacticalRange = 20;

        public static readonly string[] Categories =
        {
            "Épées Métal",
            "Brut / Hast / Arc",
            "Épées Laser",
            "Fusils Laser",
            "Grenades",
            "Lance-Grenades",
            "Munitions & Charges",
            "Armures Légères",
            "Armures Lourdes",
            "Champs & Combinaisons",
            "Pharma & Rations",
            "Médical & Outils",
            "Survie & Terrain",
            "Arcanotech",
            "Divers & Quête"
        };

        private static List<InventoryItem> _all;
        public static List<InventoryItem> All
        {
            get
            {
                if (_all == null) BuildCatalog();
                return _all;
            }
        }

        public static InventoryItem GetByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            for (int i = 0; i < All.Count; i++)
                if (string.Equals(All[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    return All[i];
            return null;
        }

        /// <summary>Prix de rachat marchand : 50% du prix catalogue, min 5 CE.</summary>
        public static int GetSellPrice(InventoryItem item)
        {
            if (item == null) return 0;
            if (item.PriceCE <= 0) return 5;
            return Math.Max(5, item.PriceCE / 2);
        }

        public static bool BuyForSheet(CharacterSheet sheet, string itemName, out string message)
        {
            message = "";
            if (sheet == null) { message = "Aucune fiche active."; return false; }
            var def = GetByName(itemName);
            if (def == null) { message = $"'{itemName}' introuvable au catalogue."; return false; }
            if (!sheet.CanAfford(def.PriceCE))
            {
                message = $"Crédits insuffisants : {def.Name} coûte {def.PriceCE} CE (solde {sheet.CreditsCE}).";
                return false;
            }
            sheet.SpendCredits(def.PriceCE);
            var copy = def.Clone();
            // Les consommables stackables ne s'équipent jamais auto.
            if (copy.Type == ItemType.Weapon)
                copy.IsEquipped = (sheet.GetEquippedWeapon() == null);
            else
                copy.IsEquipped = false;
            sheet.AddItem(copy);
            CharacterStorageService.SaveCharacter(sheet);
            message = $"'{def.Name}' acheté pour {def.PriceCE} CE. Solde : {sheet.CreditsCE} CE.";
            return true;
        }

        public static bool SellFromSheet(CharacterSheet sheet, string itemId, out string message)
        {
            message = "";
            if (sheet == null || sheet.Inventory == null) { message = "Aucune fiche active."; return false; }
            var item = sheet.Inventory.Find(i => i != null && i.ItemId == itemId);
            if (item == null) { message = "Objet introuvable."; return false; }
            int price = GetSellPrice(item);
            bool wasEquipped = item.IsEquipped;
            sheet.RemoveItem(itemId);
            sheet.EarnCredits(price);
            CharacterStorageService.SaveCharacter(sheet);
            message = $"'{item.Name}' revendu {price} CE{(wasEquipped ? " (était équipé)" : "")}. Solde : {sheet.CreditsCE} CE.";
            return true;
        }

        private static void Add(
            string name, string dlph, int dmg, float livres, int porteeM, string mains,
            int price, ItemType type, SkillType skill, string category, string placeholder,
            string desc, ItemRarity rarity = ItemRarity.Courant,
            string prefabPath = "", int armor = 0, int shield = 0, int heal = 0,
            int bonusEc = 0, bool stackable = false, int apMod = 0)
        {
            int rangeTiles = porteeM <= MaxTacticalRange ? Math.Max(1, porteeM) : MaxTacticalRange;
            // Grenades : portée de lancer tactique (30m théorique -> 8 cases jouables).
            if (category == "Grenades") rangeTiles = 8;

            _all.Add(new InventoryItem
            {
                Name = name,
                DlphCode = dlph,
                BaseDamage = dmg,
                WeightKg = (float)Math.Round(livres * LivresToKg, 2),
                RangeInTiles = rangeTiles,
                PriceCE = price,
                Type = type,
                AssociatedSkill = skill,
                EquipSlot = mains == "2H" ? ItemEquipSlot.TwoHands
                          : (type == ItemType.Weapon ? ItemEquipSlot.MainHand : ItemEquipSlot.None),
                Category = category,
                PlaceholderKind = placeholder,
                PrefabPath = prefabPath ?? "",
                Description = $"{desc} [{dlph} — {price} CE]",
                Rarity = rarity,
                ArmorProtection = armor,
                ShieldHP = shield,
                HealingAmount = heal,
                AttackBonusEc = bonusEc,
                IsStackable = stackable,
                Quantity = 1,
                ApCostModifier = apMod,
                IsEquipped = false
            });
        }

        private static void AddGrenade(
            string name, string dlph, int flatDmg, int diceNd10, float livres,
            int price, string era, string kind, int blastRadius, int shrapnel,
            string statuses, int zoneTurns, string desc,
            ItemRarity rarity = ItemRarity.Courant, int accuracyBonus = 0,
            bool launcherCompatible = true)
        {
            // Grenades : stackables (consommables de zone), lançables main (8 cases) ou lanceur.
            // Notation DLPH étendue : [FlatD + Nd10 - BlastR - 1H].
            _all.Add(new InventoryItem
            {
                Name = name,
                DlphCode = dlph,
                BaseDamage = flatDmg,
                WeightKg = (float)Math.Round(livres * LivresToKg, 2),
                RangeInTiles = 8,
                PriceCE = price,
                Type = ItemType.Weapon,
                AssociatedSkill = SkillType.Ballistique,
                EquipSlot = ItemEquipSlot.MainHand,
                Category = "Grenades",
                PlaceholderKind = "Grenade",
                PrefabPath = "",
                Description = $"{desc} [{dlph} — {price} CE — {era} / {kind} — Blast {blastRadius} — Shrap {shrapnel}]",
                Rarity = rarity,
                ArmorProtection = 0,
                ShieldHP = 0,
                HealingAmount = 0,
                AttackBonusEc = 0,
                IsStackable = true,
                Quantity = 1,
                ApCostModifier = 0,
                IsEquipped = false,
                IsGrenade = true,
                IsLauncher = false,
                Era = era,
                GrenadeKind = kind,
                BlastRadius = blastRadius,
                DamageDiceCount = diceNd10,
                ShrapnelDamage = shrapnel,
                GrenadeStatuses = statuses ?? "",
                ZoneDurationTurns = zoneTurns,
                LauncherCompatible = launcherCompatible,
                LauncherRangeBonus = 0,
                AccuracyBonus = accuracyBonus
            });
        }

        private static void AddLauncher(
            string name, string dlph, float livres, int price,
            int rangeBonus, int accuracyBonus, string desc,
            ItemRarity rarity = ItemRarity.Militaire, int apMod = 0)
        {
            // Lance-grenades : armes 2H, Ballistique, portée 8 + bonus (plafond 20 en tactique).
            int rangeTiles = Math.Min(MaxTacticalRange, 8 + Math.Max(0, rangeBonus));
            _all.Add(new InventoryItem
            {
                Name = name,
                DlphCode = dlph,
                BaseDamage = 0,
                WeightKg = (float)Math.Round(livres * LivresToKg, 2),
                RangeInTiles = rangeTiles,
                PriceCE = price,
                Type = ItemType.Weapon,
                AssociatedSkill = SkillType.Ballistique,
                EquipSlot = ItemEquipSlot.TwoHands,
                Category = "Lance-Grenades",
                PlaceholderKind = "GrenadeLauncher",
                PrefabPath = "",
                Description = $"{desc} [{dlph} — {price} CE — Lance-grenades : +{rangeBonus} cases, précision {(accuracyBonus >= 0 ? "+" : "")}{accuracyBonus}]",
                Rarity = rarity,
                ArmorProtection = 0,
                ShieldHP = 0,
                HealingAmount = 0,
                AttackBonusEc = 0,
                IsStackable = false,
                Quantity = 1,
                ApCostModifier = apMod,
                IsEquipped = false,
                IsGrenade = false,
                IsLauncher = true,
                Era = "",
                GrenadeKind = "",
                BlastRadius = 0,
                DamageDiceCount = 0,
                ShrapnelDamage = 0,
                GrenadeStatuses = "",
                ZoneDurationTurns = 0,
                LauncherCompatible = false,
                LauncherRangeBonus = rangeBonus,
                AccuracyBonus = accuracyBonus
            });
        }

        /// <summary>
        /// Rétro-compat : les 5 grenades génériques historiques deviennent des
        /// grenades modernes stackables avec profil de zone complet.
        /// </summary>
        private static void PatchLegacyGrenades()
        {
            for (int i = 0; i < _all.Count; i++)
            {
                var it = _all[i];
                if (it == null || it.Category != "Grenades" || it.IsGrenade) continue;
                it.IsGrenade = true;
                it.IsStackable = true;
                it.Era = "Moderne";
                it.LauncherCompatible = true;
                it.BlastRadius = 2;
                it.ShrapnelDamage = 2;
                it.GrenadeStatuses = "Destabilise,Saignement";
                it.ZoneDurationTurns = 0;
                it.AccuracyBonus = 0;
                if (it.Name.Contains("1d10")) { it.GrenadeKind = "Fragmentation"; it.DamageDiceCount = 1; it.BlastRadius = 1; it.ShrapnelDamage = 1; }
                else if (it.Name.Contains("2d10")) { it.GrenadeKind = "Fragmentation"; it.DamageDiceCount = 2; }
                else if (it.Name.Contains("3d10")) { it.GrenadeKind = "Fragmentation"; it.DamageDiceCount = 3; it.ShrapnelDamage = 3; }
                else if (it.Name.Contains("4d10")) { it.GrenadeKind = "Explosive"; it.DamageDiceCount = 4; it.BlastRadius = 2; it.ShrapnelDamage = 2; it.GrenadeStatuses = "Destabilise,ATerre"; }
                else if (it.Name.Contains("5d10")) { it.GrenadeKind = "Thermobarique"; it.DamageDiceCount = 5; it.BlastRadius = 3; it.ShrapnelDamage = 2; it.GrenadeStatuses = "EnFeu,Asphyxie,Destabilise"; }
                else { it.GrenadeKind = "Fragmentation"; it.DamageDiceCount = 2; }
                if (it.PlaceholderKind != "Grenade") it.PlaceholderKind = "Grenade";
            }
        }

        private static void BuildCatalog()
        {
            _all = new List<InventoryItem>(140);

            // ===== 30.1 Épées & Armes de Poing en Métal (ManiementArmes, 1-2 cases) =====
            Add("Épée Métal Courte", "3D - 3L - 1P - 1H", 3, 3f, 1, "1H", 500, ItemType.Weapon, SkillType.ManiementArmes, "Épées Métal", "SwordMetal", "Glaive, arming sword, Ulfberht. Entrée de gamme fiable.");
            Add("Épée Métal Standard", "4D - 3L - 1P - 1H", 4, 3f, 1, "1H", 750, ItemType.Weapon, SkillType.ManiementArmes, "Épées Métal", "SwordMetal", "Épée bâtarde, katana traditionnel. Standard des milices.");
            Add("Épée Métal Supérieure", "5D - 3L - 1P - 1H", 5, 3f, 1, "1H", 1000, ItemType.Weapon, SkillType.ManiementArmes, "Épées Métal", "SwordMetal", "Acier replié de maître, katana affûté.");
            Add("Épée Métal Longue (2H)", "4D - 5L - 1P - 2H", 4, 5f, 1, "2H", 1000, ItemType.Weapon, SkillType.ManiementArmes, "Épées Métal", "SwordMetalLong", "Longsword, claymore d'assaut.");
            Add("Épée Métal de Guerre (2H)", "5D - 5L - 1P - 2H", 5, 5f, 1, "2H", 1250, ItemType.Weapon, SkillType.ManiementArmes, "Épées Métal", "SwordMetalLong", "Zweihänder de ligne, flamberge.");
            Add("Épée Lourde de Brèche (2H)", "6D - 5L - 1P - 2H", 6, 5f, 1, "2H", 1500, ItemType.Weapon, SkillType.ManiementArmes, "Épées Métal", "SwordMetalLong", "Espadon géant, acier lourd renforcé.", ItemRarity.Militaire);
            Add("Grand Espadon Maître", "7D - 6L - 2P - 2H", 7, 6f, 2, "2H", 3000, ItemType.Weapon, SkillType.ManiementArmes, "Épées Métal", "SwordMetalLong", "Allonge 2 cases, frappe dévastatrice.", ItemRarity.Militaire);

            // ===== 30.2 Concasseuses, Haches, Hast, Arc =====
            Add("Gourdin / Matraque", "2D - 3L - 1P - 1H", 2, 3f, 1, "1H", 50, ItemType.Weapon, SkillType.ManiementArmes, "Brut / Hast / Arc", "Club", "Arme improvisée contondante (spécialisation Armes Contondantes). Quasi gratuite.");
            Add("Hache de Bataille", "3-4D - 4L - 1P - 1H", 4, 4f, 1, "1H", 600, ItemType.Weapon, SkillType.ManiementArmes, "Brut / Hast / Arc", "Axe", "Pénétration osseuse supérieure (spécialisation Hache de Guerre).");
            Add("Hachette de Jet", "3D - 2L - 1P - 1H", 3, 2f, 1, "1H", 350, ItemType.Weapon, SkillType.ManiementArmes, "Brut / Hast / Arc", "Axe", "Équilibrée pour le lancer à courte portée (spécialisation Hache de Guerre).");
            Add("Hache de Guerre Lourde (2H)", "6D - 5L - 1P - 2H", 6, 5f, 1, "2H", 3500, ItemType.Weapon, SkillType.ManiementArmes, "Brut / Hast / Arc", "Axe", "Bardiche d'assaut, tranche les plates (spécialisation Hache de Guerre).", ItemRarity.Militaire);
            Add("Marteau Métal Lourd", "6D - 5L - 1P - 2H", 6, 5f, 1, "2H", 5000, ItemType.Weapon, SkillType.ManiementArmes, "Brut / Hast / Arc", "Hammer", "Broyeur de blindages métalliques (spécialisation Armes Contondantes).", ItemRarity.Militaire);
            Add("Pique & Hallebarde", "4D - 6L - 2P - 2H", 4, 6f, 2, "2H", 800, ItemType.Weapon, SkillType.ArmesPercantes, "Brut / Hast / Arc", "Spear", "Allonge tactique 2m, arrêt de charge (spécialisation Arme de Hast).");
            Add("Hallebarde Lourde", "5D - 6L - 2P - 2H", 5, 6f, 2, "2H", 1500, ItemType.Weapon, SkillType.ArmesPercantes, "Brut / Hast / Arc", "Spear", "Bec de faucon + pointe d'estoc, fauche et crochète (spécialisation Arme de Hast).", ItemRarity.Militaire);
            Add("Arc Composite Renforcé", "5D - 2L - 500P - 2H", 5, 2f, 500, "2H", 1000, ItemType.Weapon, SkillType.Ballistique, "Brut / Hast / Arc", "Bow", "Portée 500m (plafonnée à 20 cases en tactique), perforation longue distance (spécialisation Tir à l'Arc).");
            Add("Arbalète Lourde", "6D - 3L - 400P - 2H", 6, 3f, 400, "2H", 2500, ItemType.Weapon, SkillType.Ballistique, "Brut / Hast / Arc", "Bow", "Rechargement lent, trait dévastateur silencieux (spécialisation Tir à l'Arc).", ItemRarity.Militaire);

            // ===== 31.1 Épées Laser (confinement plasma, jamais d'usure) =====
            Add("Épée Laser Standard 3D", "3D - 1L - 1P - 1H", 3, 1f, 1, "1H", 6000, ItemType.Weapon, SkillType.ManiementArmes, "Épées Laser", "LaserSword", "Ultra-légère, dégainement quasi instantané.", ItemRarity.Militaire);
            Add("Épée Laser Militaire 4D", "4D - 1L - 1P - 1H", 4, 1f, 1, "1H", 20000, ItemType.Weapon, SkillType.ManiementArmes, "Épées Laser", "LaserSword", "Équipement d'officier de la garde impériale.", ItemRarity.Militaire);
            Add("Épée Laser Commando 5D", "5D - 1L - 1P - 1H", 5, 1f, 1, "1H", 40000, ItemType.Weapon, SkillType.ManiementArmes, "Épées Laser", "LaserSword", "Puissance chirurgicale à une main, coupe l'acier.", ItemRarity.Elite);
            Add("Épée Laser Deux Mains 4D", "4D - 2L - 1P - 2H", 4, 2f, 1, "2H", 18000, ItemType.Weapon, SkillType.ManiementArmes, "Épées Laser", "LaserSwordLong", "Faisceau large, excellente assise de parade (+1).", ItemRarity.Militaire);
            Add("Épée Laser Lourde 5D", "5D - 2L - 1P - 2H", 5, 2f, 1, "2H", 35000, ItemType.Weapon, SkillType.ManiementArmes, "Épées Laser", "LaserSwordLong", "Générateur double chambre, perforation lourde.", ItemRarity.Elite);
            Add("Épée Laser Suprême 6D", "6D - 2L - 1P - 2H", 6, 2f, 1, "2H", 60000, ItemType.Weapon, SkillType.ManiementArmes, "Épées Laser", "LaserSwordLong", "Arc plasmique dense, brûlures résiduelles.", ItemRarity.Elite);
            Add("Sabre Long à Allonge 4D", "4D - 3L - 2P - 1H", 4, 3f, 2, "1H", 75000, ItemType.Weapon, SkillType.ManiementArmes, "Épées Laser", "LaserSword", "Portée 2m à une main, régulateur cinétique.", ItemRarity.Elite);
            Add("Grand Sabre de Brèche 5D", "5D - 4L - 2P - 2H", 5, 4f, 2, "2H", 80000, ItemType.Weapon, SkillType.ManiementArmes, "Épées Laser", "LaserSwordLong", "Conçu pour découper les portes de sas.", ItemRarity.Elite);
            Add("Espadon Laser Titan 6D", "6D - 4L - 2P - 2H", 6, 4f, 2, "2H", 100000, ItemType.Weapon, SkillType.ManiementArmes, "Épées Laser", "LaserSwordLong", "Summum du corps-à-corps énergétique impérial.", ItemRarity.Legendaire);

            // ===== 31.2 Fusils Laser (prefabs réels quand disponibles, sinon placeholder) =====
            Add("Fusil Laser Léger", "3D - 2L - 500P - 1H", 3, 2f, 500, "1H", 10000, ItemType.Weapon, SkillType.Ballistique, "Fusils Laser", "PistolLaser", "Pistolet lourd compact. Trait lumineux instantané.", ItemRarity.Militaire, "SciFiGunLight_Yellow");
            Add("Fusil Laser de Précision", "4D - 2L - 750P - 1H", 4, 2f, 750, "1H", 25000, ItemType.Weapon, SkillType.Ballistique, "Fusils Laser", "PistolLaser", "Visée optique intégrée.", ItemRarity.Militaire, "SciFiGunLight_Blue");
            Add("Fusil d'Assaut Laser", "4D - 3L - 500P - 2H", 4, 3f, 500, "2H", 20000, ItemType.Weapon, SkillType.Ballistique, "Fusils Laser", "RifleLaser", "Cadence soutenue. Canon entravé au contact (Livre VI §26.2).", ItemRarity.Militaire, "SciFiGunHeavy_Blue");
            Add("Fusil de Sniper Lourd", "5D - 4L - 1000P - 2H", 5, 4f, 1000, "2H", 50000, ItemType.Weapon, SkillType.Ballistique, "Fusils Laser", "SniperLaser", "Portée 1km (20 cases en tactique).", ItemRarity.Elite, "SciFiGunHeavy_Black");
            Add("Deglazer", "+1ec 7D - 4L - 1000P - 2H", 7, 4f, 1000, "2H", 200000, ItemType.Weapon, SkillType.Ballistique, "Fusils Laser", "Deglazer", "Arcanotech de guerre : prisme nytharite pure, +1ec au jet, 7 dégâts à 1km !", ItemRarity.Legendaire, "SciFiGunHeavy_White", bonusEc: 1);

            // Variantes lourdes issues des prefabs sci-fi restants (fluff, mêmes règles)
            Add("Fusil Lourd Carbone", "4D - 3L - 500P - 2H", 4, 3f, 500, "2H", 22000, ItemType.Weapon, SkillType.Ballistique, "Fusils Laser", "RifleLaser", "Variante lourde carbone (skin prefab).", ItemRarity.Militaire, "SciFiGunHeavy_Rad");
            Add("Fusil Lourd Ivoire", "4D - 3L - 500P - 2H", 4, 3f, 500, "2H", 22000, ItemType.Weapon, SkillType.Ballistique, "Fusils Laser", "RifleLaser", "Variante lourde ivoire (skin prefab).", ItemRarity.Militaire, "SciFiGunHeavy_Yellow");
            Add("Pistolet Léger Carbone", "3D - 2L - 500P - 1H", 3, 2f, 500, "1H", 11000, ItemType.Weapon, SkillType.Ballistique, "Fusils Laser", "PistolLaser", "Variante légère carbone (skin prefab).", ItemRarity.Militaire, "SciFiGunLight_Black");
            Add("Pistolet Léger Ivoire", "3D - 2L - 500P - 1H", 3, 2f, 500, "1H", 11000, ItemType.Weapon, SkillType.Ballistique, "Fusils Laser", "PistolLaser", "Variante légère ivoire (skin prefab).", ItemRarity.Courant, "SciFiGunLight_White");
            Add("Pistolet Léger Irradié", "3D - 2L - 500P - 1H", 3, 2f, 500, "1H", 13000, ItemType.Weapon, SkillType.Ballistique, "Fusils Laser", "PistolLaser", "Variante irradiée (skin prefab).", ItemRarity.Militaire, "SciFiGunLight_Rad");

            // ===== 31.3 Grenades (10 + Nd10 dégâts de zone, lancer 1H) =====
            Add("Grenade Légère 1d10", "10D - 0.5L - 30P - 1H", 10, 0.5f, 30, "1H", 2000, ItemType.Weapon, SkillType.Ballistique, "Grenades", "Grenade", "10 + 1d10 dégâts de zone. Lancer 8 cases en tactique.");
            Add("Grenade Tactique 2d10", "10D - 0.5L - 30P - 1H", 10, 0.5f, 30, "1H", 3500, ItemType.Weapon, SkillType.Ballistique, "Grenades", "Grenade", "10 + 2d10 dégâts de zone.", ItemRarity.Militaire);
            Add("Grenade à Fragmentation 3d10", "10D - 0.5L - 30P - 1H", 10, 0.5f, 30, "1H", 5000, ItemType.Weapon, SkillType.Ballistique, "Grenades", "Grenade", "10 + 3d10 dégâts de zone.", ItemRarity.Militaire);
            Add("Grenade de Brèche 4d10", "10D - 1.0L - 30P - 1H", 10, 1f, 30, "1H", 6500, ItemType.Weapon, SkillType.Ballistique, "Grenades", "Grenade", "10 + 4d10 dégâts de zone, perce les portes.", ItemRarity.Elite);
            Add("Grenade Thermobarrique 5d10", "10D - 1.0L - 30P - 1H", 10, 1f, 30, "1H", 7500, ItemType.Weapon, SkillType.Ballistique, "Grenades", "Grenade", "10 + 5d10 dégâts de zone. Vide les pièces.", ItemRarity.Elite);

            // ===== 31.3b Grenades multi-époques (main 2 PA / 8 cases, lanceur 3 PA / 20 cases) =====
            // Poudre Noire (rustiques, -1 précision, pas chères)
            AddGrenade("Pot à Feu (XVe)", "6D+1d10 - Blast1 - 1H", 6, 1, 1.5f, 300, "Poudre Noire", "Explosive", 1, 0, "EnFeu", 0, "Céramique emplie de poudre noire et poix. Incendie la zone.", ItemRarity.Courant, accuracyBonus: -1, launcherCompatible: false);
            AddGrenade("Grenade Cloutée XVIIe", "8D+1d10 - Blast1 - 1H", 8, 1, 1.2f, 450, "Poudre Noire", "Fragmentation", 1, 2, "Saignement", 0, "Sphère de fonte cloutée à mèche. Éclats irréguliers.", ItemRarity.Courant, accuracyBonus: -1);
            // Grande Guerre
            AddGrenade("Mills Bomb Mk1 (1915)", "10D+1d10 - Blast1 - 1H", 10, 1, 1.4f, 800, "Grande Guerre", "Fragmentation", 1, 2, "Destabilise,Saignement", 0, "Citron segmenté britannique. Référence des tranchées.", ItemRarity.Militaire);
            AddGrenade("Stielhandgranate M1915", "10D+1d10 - Blast1 - 1H", 10, 1, 1.6f, 850, "Grande Guerre", "Explosive", 1, 1, "Destabilise,ATerre", 0, "À manche : +1 précision au lancer, souffle directionnel.", ItemRarity.Militaire, accuracyBonus: 1);
            // Seconde Guerre
            AddGrenade("Mk2 Pineapple (1942)", "10D+2d10 - Blast2 - 1H", 10, 2, 1.3f, 1500, "Seconde Guerre", "Fragmentation", 2, 3, "Destabilise,Saignement", 0, "Ananas US. Éclats réguliers, zone fiable.", ItemRarity.Militaire);
            AddGrenade("F1 Soviétique (1942)", "10D+2d10 - Blast2 - 1H", 10, 2, 1.3f, 1400, "Seconde Guerre", "Fragmentation", 2, 4, "Destabilise,Saignement", 0, "Gros éclats longue portée. Dangereuse pour le lanceur au contact.", ItemRarity.Militaire);
            AddGrenade("Stielhandgranate M24", "10D+2d10 - Blast2 - 1H", 10, 2, 1.7f, 1600, "Seconde Guerre", "Explosive", 2, 1, "Destabilise,ATerre", 0, "Pilon à manche, souffle puissant anti retranchement.", ItemRarity.Militaire, accuracyBonus: 1);
            // Guerre Froide
            AddGrenade("M67 (1968)", "10D+2d10 - Blast2 - 1H", 10, 2, 0.9f, 2500, "Guerre Froide", "Fragmentation", 2, 3, "Destabilise,Saignement", 0, "Bille d'acier US, standard OTAN. Précise et équilibrée.", ItemRarity.Militaire, accuracyBonus: 1);
            AddGrenade("RGD-5 Soviétique", "10D+2d10 - Blast2 - 1H", 10, 2, 0.7f, 2200, "Guerre Froide", "Fragmentation", 2, 2, "Destabilise,Saignement", 0, "Légère, lancer lointain, souffle + éclats.", ItemRarity.Militaire);
            AddGrenade("M18 Fumigène", "0D - Blast2 - Fumi", 0, 0, 1.2f, 600, "Guerre Froide", "Fumigene", 2, 0, "Aveugle", 2, "Rideau de fumée colorée 2 tours. Aveugle la zone, 0 dégât.", ItemRarity.Courant);
            // Moderne
            AddGrenade("M84 Flashbang", "0D - Blast2 - Flash", 2, 0, 0.6f, 1800, "Moderne", "Flash", 2, 0, "Aveugle,Sourd,Etourdi", 0, "9 bangs assourdissants. Neutralise sans tuer (2 flat symbolique).", ItemRarity.Militaire);
            AddGrenade("AN-M14 Incendiaire", "8D+2d10 - Blast1 - Feu", 8, 2, 2.0f, 2800, "Moderne", "Incendiaire", 1, 0, "EnFeu", 2, "Thermite 2200°C. Incendie 2 tours, perce les blindés légers.", ItemRarity.Elite);
            AddGrenade("Grenade IEM M-EMP", "4D+1d10 - Blast2 - IEM", 4, 1, 0.8f, 4500, "Moderne", "EMP", 2, 0, "Paralyse,Destabilise", 0, "Impulsion électromagnétique. Paralyse drones, champs et visières.", ItemRarity.Elite);
            AddGrenade("Grenade Lacrymogène CM-6", "2D+1d10 - Blast2 - Gaz", 2, 1, 0.7f, 1200, "Moderne", "Gaz", 2, 0, "Aveugle,Asphyxie,Etourdi", 2, "Gaz CS. Zone 2 tours, contrôle de foule.", ItemRarity.Militaire);
            // Futuriste
            AddGrenade("Grenade Plasma SG-7", "12D+3d10 - Blast2", 12, 3, 0.7f, 12000, "Futuriste", "Plasma", 2, 2, "EnFeu,Destabilise", 0, "Confinement plasma industriel. Surchauffe + brûlures résiduelles.", ItemRarity.Elite);
            AddGrenade("Grenade Cryo C-9", "8D+2d10 - Blast2", 8, 2, 0.8f, 11000, "Futuriste", "Cryo", 2, 0, "Ralenti,Immobilise", 1, "Azote pressurisé. Ralentit puis fige la zone 1 tour.", ItemRarity.Elite);
            AddGrenade("Grenade Sonique S-3", "4D+1d10 - Blast2", 4, 1, 0.6f, 9000, "Futuriste", "Flash", 2, 0, "Sourd,Etourdi,Sonne", 0, "Canon sonore focalisé. Sonne les cibles non protégées.", ItemRarity.Elite);
            // Arcanotech (Nytharite impériale, Livres IV/VIII/XI)
            AddGrenade("Grenade Nytharite F-31", "12D+4d10 - Blast3", 12, 4, 1.0f, 25000, "Arcanotech", "Plasma", 3, 3, "EnFeu,Destabilise", 0, "Prisme nytharite pure. Signature de la garde impériale.", ItemRarity.Legendaire);
            AddGrenade("Grenade Graviton G-0", "10D+3d10 - Blast2", 10, 3, 1.2f, 30000, "Arcanotech", "Graviton", 2, 0, "ATerre,Ralenti,Immobilise", 1, "Puits gravifique bref. Cloue au sol puis relâche.", ItemRarity.Legendaire);
            AddGrenade("Grenade Stase Chrono T-0", "0D+1d10 - Blast2", 0, 1, 0.8f, 35000, "Arcanotech", "Gaz", 2, 0, "Paralyse,ChronoFracture", 2, "Bulle de Fleuve figé 2 tours (Livre V). Paralyse temporelle.", ItemRarity.Prototype);
            AddGrenade("Grenade Pestilentielle Karkjiue", "6D+2d10 - Blast3 - Gaz", 6, 2, 1.0f, 18000, "Arcanotech", "Gaz", 3, 0, "Empoisonne,Saignement", 3, "Spores de Karkjiue (Livre XI). Nuage persistant 3 tours.", ItemRarity.Legendaire);

            // ===== 31.3c Lance-grenades (équiper en 2H, 3 PA / tir, +portée/précision) =====
            AddLauncher("M79 Thumper (1961)", "LG +12P +1Prec - 2H", 6f, 8000, 12, 1, "Fusil casse-buse 40mm coup par coup. Le classique du fantassin.", ItemRarity.Militaire);
            AddLauncher("M203 Sous-Canon", "LG +10P +1Prec - 2H", 3f, 12000, 10, 1, "Module 40mm sous fusil d'assaut. Tir tendu précis.", ItemRarity.Militaire);
            AddLauncher("Milkor MGL Mk1", "LG +8P +0Prec - 2H", 12f, 20000, 8, 0, "Barillet 6 coups 40mm. Cadence de zone (-1 PA par tir).", ItemRarity.Elite, apMod: -1);
            AddLauncher("Mortier Léger 60mm", "LG +12P -1Prec - 2H", 30f, 25000, 12, -1, "Tir courbe d'escouade. Ignore les murets (pas la couverture Full).", ItemRarity.Elite);
            AddLauncher("Lance Plasma Nytharite LP-9", "LG +12P +2Prec - 2H", 5f, 60000, 12, 2, "Thumper arcanotech : bobines nytharite, +2 précision, 20 cases.", ItemRarity.Legendaire);

            // ===== 32.1 Armures Légères (0 PA malus) =====
            Add("Armure Légère 1", "Prot 1 - 0 PA", 0, 4f, 1, "1H", 2000, ItemType.Armor, SkillType.EndurancePhysique, "Armures Légères", "ArmorLight", "Fibres balistiques souples. Aucun malus PA.", armor: 1);
            Add("Armure Légère 2", "Prot 2 - 0 PA", 0, 5f, 1, "1H", 5000, ItemType.Armor, SkillType.EndurancePhysique, "Armures Légères", "ArmorLight", "Polymères souples. Mobilité totale.", ItemRarity.Militaire, armor: 2);
            Add("Armure Légère 3", "Prot 3 - 0 PA", 0, 6f, 1, "1H", 12000, ItemType.Armor, SkillType.EndurancePhysique, "Armures Légères", "ArmorLight", "Tissage aramide haute densité.", ItemRarity.Militaire, armor: 3);
            Add("Armure Légère 4", "Prot 4 - 0 PA", 0, 7f, 1, "1H", 20000, ItemType.Armor, SkillType.EndurancePhysique, "Armures Légères", "ArmorLight", "Meilleure légère du marché.", ItemRarity.Elite, armor: 4);

            // ===== 32.1 Armures Lourdes (malus PA permanent) =====
            Add("Armure Lourde 3 (1 PA)", "Prot 3 - 1 PA", 0, 22f, 1, "1H", 8000, ItemType.Armor, SkillType.EndurancePhysique, "Armures Lourdes", "ArmorHeavy", "Plaques céramo-métal. +1 PA à toute action.", ItemRarity.Militaire, armor: 3, apMod: 1);
            Add("Armure Lourde 4 (1 PA)", "Prot 4 - 1 PA", 0, 26f, 1, "1H", 12000, ItemType.Armor, SkillType.EndurancePhysique, "Armures Lourdes", "ArmorHeavy", "Exosquelette léger.", ItemRarity.Militaire, armor: 4, apMod: 1);
            Add("Armure Lourde 5 (1 PA)", "Prot 5 - 1 PA", 0, 30f, 1, "1H", 20000, ItemType.Armor, SkillType.EndurancePhysique, "Armures Lourdes", "ArmorHeavy", "Blindage de ligne.", ItemRarity.Elite, armor: 5, apMod: 1);
            Add("Armure Lourde 6 (2 PA)", "Prot 6 - 2 PA", 0, 35f, 1, "1H", 50000, ItemType.Armor, SkillType.EndurancePhysique, "Armures Lourdes", "ArmorHeavy", "Blindage extrême, inertie lourde.", ItemRarity.Elite, armor: 6, apMod: 2);
            Add("Armure Lourde 7 (2 PA)", "Prot 7 - 2 PA", 0, 40f, 1, "1H", 90000, ItemType.Armor, SkillType.EndurancePhysique, "Armures Lourdes", "ArmorHeavy", "Forteresse portable.", ItemRarity.Legendaire, armor: 7, apMod: 2);
            Add("Armure Lourde 7 Optimisée (1 PA)", "Prot 7 - 1 PA", 0, 30f, 1, "1H", 125000, ItemType.Armor, SkillType.EndurancePhysique, "Armures Lourdes", "ArmorHeavy", "Prot 7 pour seulement 1 PA. Prototype.", ItemRarity.Prototype, armor: 7, apMod: 1);

            // ===== 32.2 Champs de Force & Combinaisons =====
            Add("Champ de Force Personnel 1", "Barrière 10 PV", 0, 1f, 1, "1H", 75000, ItemType.Arcanotech, SkillType.IngenierieArcanotech, "Champs & Combinaisons", "ShieldGen", "Absorbe les dégâts avant la chair ; zéro encombrement.", ItemRarity.Elite, shield: 10);
            Add("Champ de Force Personnel 2", "Barrière 20 PV", 0, 1f, 1, "1H", 125000, ItemType.Arcanotech, SkillType.IngenierieArcanotech, "Champs & Combinaisons", "ShieldGen", "Recharge auto hors combat après 1 minute.", ItemRarity.Elite, shield: 20);
            Add("Champ de Force Personnel 3", "Barrière 30 PV", 0, 1f, 1, "1H", 150000, ItemType.Arcanotech, SkillType.IngenierieArcanotech, "Champs & Combinaisons", "ShieldGen", "Protège radiations et vide spatial.", ItemRarity.Legendaire, shield: 30);
            Add("Champ de Force Personnel 4", "Barrière 40 PV", 0, 1f, 1, "1H", 200000, ItemType.Arcanotech, SkillType.IngenierieArcanotech, "Champs & Combinaisons", "ShieldGen", "Modèle d'élite de la flotte impériale.", ItemRarity.Legendaire, shield: 40);
            Add("Champ de Force Personnel 5", "Barrière 50 PV", 0, 1f, 1, "1H", 250000, ItemType.Arcanotech, SkillType.IngenierieArcanotech, "Champs & Combinaisons", "ShieldGen", "Forteresse cinétique portable impénétrable.", ItemRarity.Prototype, shield: 50);
            Add("Spacesuit Léger 2-3", "Blindage 2-3 - 0 PA", 0, 15f, 1, "1H", 70000, ItemType.Armor, SkillType.EndurancePhysique, "Champs & Combinaisons", "ArmorHeavy", "Survie hostile et gravité zéro.", ItemRarity.Elite, armor: 3);
            Add("Spacesuit Lourd 3-6", "Blindage 3-6 - 1-2 PA", 0, 40f, 1, "1H", 400000, ItemType.Armor, SkillType.EndurancePhysique, "Champs & Combinaisons", "ArmorHeavy", "Mécha lourd opérations extrêmes.", ItemRarity.Prototype, armor: 6, apMod: 2);

            // ===== 32.3 Pharma & Rations (consommables) =====
            Add("Antidouleurs (8 tabs)", "Dopage 2h", 0, 0.2f, 1, "1H", 500, ItemType.Consumable, SkillType.PremiersSoins, "Pharma & Rations", "Pills", "+5 seuil Létal, +2 Encaissement pendant 2h. Continue au-delà des limites.", stackable: true);
            Add("Speed (4 tabs)", "Rapide +3 PA 4h", 0, 0.2f, 1, "1H", 500, ItemType.Consumable, SkillType.PremiersSoins, "Pharma & Rations", "Pills", "État Rapide : +3 PA déplacements par tour, 4h.", stackable: true);
            Add("Seringue de soins 5 PV", "Soin 5 PV - 1 PA", 0, 0.2f, 1, "1H", 900, ItemType.Consumable, SkillType.PremiersSoins, "Pharma & Rations", "Syringe", "Injection 1 PA, restaure 5 PV.", stackable: true, heal: 5);
            Add("Seringue de soins 10 PV", "Soin 10 PV - 1 PA", 0, 0.2f, 1, "1H", 2000, ItemType.Consumable, SkillType.PremiersSoins, "Pharma & Rations", "Syringe", "Injection 1 PA, restaure 10 PV.", ItemRarity.Militaire, stackable: true, heal: 10);
            Add("Seringue de soins 20 PV", "Soin 20 PV - 1 PA", 0, 0.3f, 1, "1H", 5000, ItemType.Consumable, SkillType.PremiersSoins, "Pharma & Rations", "Syringe", "Injection 1 PA, restaure 20 PV.", ItemRarity.Elite, stackable: true, heal: 20);
            Add("Seringue généralisée 10 PV", "Soin zone 10 PV", 0, 0.3f, 1, "1H", 2500, ItemType.Consumable, SkillType.PremiersSoins, "Pharma & Rations", "Syringe", "Soigne l'ensemble des membres blessés (10 PV).", ItemRarity.Militaire, stackable: true, heal: 10);
            Add("Meal Pack", "Ration", 0, 1f, 1, "1H", 10, ItemType.Consumable, SkillType.NatureSurvie, "Pharma & Rations", "Ration", "Ration standard.", stackable: true);
            Add("Meal Pack + Water", "Ration + Hydra", 0, 2f, 1, "1H", 15, ItemType.Consumable, SkillType.NatureSurvie, "Pharma & Rations", "Ration", "Ration + hydratation.", stackable: true);
            Add("Water Blob 1L", "Hydratation 1L", 0, 2.2f, 1, "1H", 5, ItemType.Consumable, SkillType.NatureSurvie, "Pharma & Rations", "Ration", "Poche d'eau 1L.", stackable: true);

            // ===== Arcanotech divers (cellules, kits) =====
            Add("Cellule Nytharite Standard", "Charge arcanotech", 0, 0.5f, 1, "1H", 1500, ItemType.Arcanotech, SkillType.IngenierieArcanotech, "Arcanotech", "Cell", "Recharge épées laser et champs de force.", ItemRarity.Militaire, stackable: true);
            Add("Cellule Nytharite Pure", "Charge élite", 0, 0.5f, 1, "1H", 8000, ItemType.Arcanotech, SkillType.IngenierieArcanotech, "Arcanotech", "Cell", "Charge du Deglazer et Titan.", ItemRarity.Elite, stackable: true);
            Add("Kit d'Entretien d'Armes", "Atelier portable", 0, 3f, 1, "1H", 1200, ItemType.Misc, SkillType.Artisanat, "Arcanotech", "Cell", "Nettoyage, recalibrage optique, graissage.", stackable: false);
            Add("Résonateur Nytharite (focus)", "Focus psi", 0, 0.5f, 1, "1H", 12000, ItemType.Arcanotech, SkillType.MagieElementale, "Arcanotech", "Cell", "Canalise la 5e Force. Requis pour sorts (XP=PA, Livre IV).", ItemRarity.Elite);
            Add("Moteur Arcanique de Poche", "Générateur", 0, 4f, 1, "1H", 25000, ItemType.Arcanotech, SkillType.IngenierieArcanotech, "Arcanotech", "Cell", "Alimente un champ ou un atelier de campagne.", ItemRarity.Legendaire);

            // ===== Munitions & Charges (ItemType.Ammunition, stackables) =====
            Add("Charge Laser Standard (x10)", "Munition laser", 0, 1f, 1, "1H", 300, ItemType.Ammunition, SkillType.Ballistique, "Munitions & Charges", "Cell", "10 tirs fusil laser standard.", stackable: true);
            Add("Charge Laser Haute Densité (x10)", "Munition élite", 0, 1.2f, 1, "1H", 900, ItemType.Ammunition, SkillType.Ballistique, "Munitions & Charges", "Cell", "10 tirs +1 Dégât (sniper, Deglazer).", ItemRarity.Militaire, stackable: true);
            Add("Carquois Flèches (x20)", "Munition arc", 0, 2f, 1, "1H", 150, ItemType.Ammunition, SkillType.Ballistique, "Munitions & Charges", "Cell", "20 flèches arc composite.", stackable: true);
            Add("Grenade d'Exercice (inerte)", "Entraînement", 0, 0.5f, 1, "1H", 100, ItemType.Ammunition, SkillType.Ballistique, "Munitions & Charges", "Grenade", "Inerte, pour drill au lancer.", stackable: true);
            Add("Batterie Universelle", "Énergie", 0, 0.8f, 1, "1H", 250, ItemType.Ammunition, SkillType.IngenierieArcanotech, "Munitions & Charges", "Cell", "Alimente lampes, radios, viseurs.", stackable: true);

            // ===== Médical & Outils (soins hors seringues, artisanat, crochetage) =====
            Add("Bandage Compressif", "Soin 2 PV", 0, 0.3f, 1, "1H", 80, ItemType.Consumable, SkillType.PremiersSoins, "Médical & Outils", "Ration", "Stoppe Saignement léger, +2 PV (1 PA).", stackable: true, heal: 2);
            Add("Attelle Rigide", "Fracture", 0, 0.8f, 1, "1H", 150, ItemType.Consumable, SkillType.PremiersSoins, "Médical & Outils", "Ration", "Immobilise un membre (retire Ralenti).", stackable: true);
            Add("Kit de Chirurgie de Campagne", "Bloc opératoire", 0, 6f, 1, "1H", 4500, ItemType.Misc, SkillType.MedecineAvancee, "Médical & Outils", "Cell", "Permet chirurgie hors bloc (Livre VII §29).", ItemRarity.Militaire);
            Add("Kit de Crochetage", "Subterfuge", 0, 0.5f, 1, "1H", 400, ItemType.Misc, SkillType.Subterfuge, "Médical & Outils", "Cell", "Rossignols + sonde. Bonus matériel +1 crochetage.", ItemRarity.Militaire);
            Add("Boîte à Outils Arcanotech", "Réparation", 0, 8f, 1, "1H", 1800, ItemType.Misc, SkillType.IngenierieArcanotech, "Médical & Outils", "Cell", "Répare armes, armures, moteurs (Artisanat/Ingénierie).");
            Add("Menottes Magnétiques", "Entrave", 0, 0.6f, 1, "1H", 600, ItemType.Misc, SkillType.Subterfuge, "Médical & Outils", "Cell", "Immobilisé tant que menotté (évasion FOR/AGI).", ItemRarity.Militaire);

            // ===== Survie & Terrain (portée, lumière, comm) =====
            Add("Corde 20m", "Escalade", 0, 3f, 1, "1H", 60, ItemType.Misc, SkillType.Athletisme, "Survie & Terrain", "Ration", "Escalade, ligotage, pièges.", stackable: false);
            Add("Lampe Frontale + Balise", "Éclairage", 0, 0.4f, 1, "1H", 120, ItemType.Misc, SkillType.Observation, "Survie & Terrain", "Cell", "Annule malus Obscurité proche (nécessite Batterie).");
            Add("Jumelles Thermiques", "Observation x4", 0, 1.5f, 1, "1H", 2500, ItemType.Misc, SkillType.Observation, "Survie & Terrain", "Cell", "+1 Observation à distance, vision nuit.", ItemRarity.Militaire);
            Add("Radio Tactique", "Comms 5km", 0, 0.8f, 1, "1H", 800, ItemType.Misc, SkillType.Communication, "Survie & Terrain", "Cell", "Coordination d'escouade (Tactique/Leadership).", ItemRarity.Militaire);
            Add("Tente Pressurisée 2 Places", "Bivouac", 0, 12f, 1, "1H", 1500, ItemType.Misc, SkillType.NatureSurvie, "Survie & Terrain", "Ration", "Abri hostile, recycle air 48h.");
            Add("Masque Filtrant", "Anti-gaz", 0, 0.6f, 1, "1H", 350, ItemType.Armor, SkillType.EndurancePhysique, "Survie & Terrain", "ArmorLight", "Immunité gaz lacrymo/toxiques courants.", armor: 0);
            Add("Sac à Dos Renforcé", "Contenant 30kg", 0, 2f, 1, "1H", 300, ItemType.Misc, SkillType.Athletisme, "Survie & Terrain", "Ration", "Contenant : +30kg de capacité de portage.");
            Add("Grappin Magnétique", "Franchissement", 0, 2.5f, 1, "1H", 900, ItemType.Misc, SkillType.Athletisme, "Survie & Terrain", "Cell", "Portée 15m, treuil 150kg.", ItemRarity.Militaire);

            // ===== Divers & Quête (butin, preuves, crédits physiques) =====
            Add("Holocarte Chiffrée", "Donnée", 0, 0.1f, 1, "1H", 0, ItemType.Misc, SkillType.Academie, "Divers & Quête", "Cell", "Preuve / quête. Valeur selon acheteur. Prix 0 = invendable au marché standard.");
            Add("Clé Magnétique Générique", "Accès", 0, 0.1f, 1, "1H", 200, ItemType.Misc, SkillType.Subterfuge, "Divers & Quête", "Cell", "Ouvre portes civiles standard.", stackable: false);
            Add("Lingot de Troque", "Monnaie", 0, 2.2f, 1, "1H", 1000, ItemType.Misc, SkillType.Communication, "Divers & Quête", "Cell", "Revendable 1000 CE partout. Monnaie de troc.", stackable: true);
            Add("Trophée de Chasse", "Butin", 0, 3f, 1, "1H", 400, ItemType.Misc, SkillType.NatureSurvie, "Divers & Quête", "Ration", "Butin faune d'Hybris (Livre XI). Prix selon taxidermiste.");
            Add("Éclat de Nytharite Brut", "Composant", 0, 0.5f, 1, "1H", 3000, ItemType.Arcanotech, SkillType.Artisanat, "Divers & Quête", "Cell", "Composant sorts/objets. Base atelier (Livre IV).", ItemRarity.Elite, stackable: true);

            PatchLegacyGrenades();
        }
    }
}

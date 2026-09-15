using System;
using Killtime.Core.Character;

namespace Killtime.Core.Inventory
{
    public enum ItemType
    {
        Weapon,
        Ammunition,
        Consumable,
        Armor,
        Arcanotech,
        Misc
    }

    public enum ItemEquipSlot
    {
        None,
        MainHand,
        OffHand,
        TwoHands,
        Holster
    }

    public enum ItemRarity
    {
        Courant,
        Militaire,
        Elite,
        Legendaire,
        Prototype
    }

    [Serializable]
    public class InventoryItem
    {
        public string ItemId = Guid.NewGuid().ToString("N");
        public string Name = "Arme Tactique";
        public string PrefabPath = "";
        public ItemType Type = ItemType.Weapon;
        public ItemEquipSlot EquipSlot = ItemEquipSlot.MainHand;
        public bool IsEquipped = false;

        public int BaseDamage = 6;
        public int RangeInTiles = 8;
        public int ApCostModifier = 0;
        public SkillType AssociatedSkill = SkillType.Ballistique;
        public float WeightKg = 1.2f;
        public string Description = "Arme balistique conventionnelle.";

        // === Extension Armurerie / Marché (Livre VIII) ===
        // Ajoutés avec valeurs par défaut : les JSON existants restent chargeables.
        public int PriceCE = 500;
        public string DlphCode = "";
        public string Category = "Divers";
        public ItemRarity Rarity = ItemRarity.Courant;
        public string PlaceholderKind = "";
        public int ArmorProtection = 0;
        public int ShieldHP = 0;
        public int HealingAmount = 0;
        public int AttackBonusEc = 0;
        public bool IsStackable = false;
        public int Quantity = 1;

        // === Extension Grenades & Lanceurs (Livre VIII §31.3 + Arcanotech) ===
        // Rétro-compatibles : les JSON existants gardent les défauts (non-grenade).
        // IsGrenade : consommable de zone lancé à la main ou via lanceur.
        // IsLauncher : arme qui propulse les grenades (bonus portée/précision).
        public bool IsGrenade = false;
        public bool IsLauncher = false;
        // Ère historique / technologique (Poudre Noire -> Arcanotech). Libre pour le fluff + filtre marché.
        public string Era = "";
        // Famille d'effet : Fragmentation, Explosive, Incendiaire, Fumigene, Flash,
        // Gaz, EMP, Plasma, Cryo, Thermobarique, Graviton, Exercice...
        public string GrenadeKind = "";
        // Rayon de souffle en cases hexagonales (0 = effet ciblé sans zone, ex: flash direct).
        public int BlastRadius = 0;
        // Dégâts de zone : BaseDamage (flat, ex 10) + Nd10 (ex 2 -> 2d10). Nd10 stocké ici.
        public int DamageDiceCount = 0;
        // Dégâts de shrapnels secondaires (éclats) : flat additionnel à distance > 0, 0 si aucun.
        public int ShrapnelDamage = 0;
        // Statuts infligés dans la zone (Livre VII) : "EnFeu,Saignement,Aveugle..." (CSV, vide = aucun).
        public string GrenadeStatuses = "";
        // Durée de l'effet de zone persistant en tours (fumigène, feu, gaz). 0 = instantané.
        public int ZoneDurationTurns = 0;
        // Grenade : accepte un lanceur ? Lanceur : bonus de portée en cases.
        public bool LauncherCompatible = false;
        public int LauncherRangeBonus = 0;
        // Malus/bonus de dispersion : cases de déviation évitées (-) ou précision (+).
        // Positif = plus précis (réduit la dispersion), négatif = rustique.
        public int AccuracyBonus = 0;

        public InventoryItem Clone()
        {
            return new InventoryItem
            {
                ItemId = Guid.NewGuid().ToString("N"),
                Name = this.Name,
                PrefabPath = this.PrefabPath,
                Type = this.Type,
                EquipSlot = this.EquipSlot,
                IsEquipped = this.IsEquipped,
                BaseDamage = this.BaseDamage,
                RangeInTiles = this.RangeInTiles,
                ApCostModifier = this.ApCostModifier,
                AssociatedSkill = this.AssociatedSkill,
                WeightKg = this.WeightKg,
                Description = this.Description,
                PriceCE = this.PriceCE,
                DlphCode = this.DlphCode,
                Category = this.Category,
                Rarity = this.Rarity,
                PlaceholderKind = this.PlaceholderKind,
                ArmorProtection = this.ArmorProtection,
                ShieldHP = this.ShieldHP,
                HealingAmount = this.HealingAmount,
                AttackBonusEc = this.AttackBonusEc,
                IsStackable = this.IsStackable,
                Quantity = this.Quantity,
                IsGrenade = this.IsGrenade,
                IsLauncher = this.IsLauncher,
                Era = this.Era,
                GrenadeKind = this.GrenadeKind,
                BlastRadius = this.BlastRadius,
                DamageDiceCount = this.DamageDiceCount,
                ShrapnelDamage = this.ShrapnelDamage,
                GrenadeStatuses = this.GrenadeStatuses,
                ZoneDurationTurns = this.ZoneDurationTurns,
                LauncherCompatible = this.LauncherCompatible,
                LauncherRangeBonus = this.LauncherRangeBonus,
                AccuracyBonus = this.AccuracyBonus
            };
        }

        /// <summary>
        /// Vrai si cet item est une grenade lançable (main ou lanceur).
        /// Règle : catégorie Grenades OU flag IsGrenade (nouveau catalogue multi-époques).
        /// </summary>
        public bool IsThrowableGrenade()
        {
            if (IsGrenade) return true;
            if (Type == ItemType.Weapon && Category == "Grenades") return true;
            return false;
        }
    }
}
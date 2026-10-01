using System;
using Killtime.Core.Character;
using Killtime.Core.Inventory;

namespace Killtime.Core.Combat
{
    /// <summary>
    /// Comptabilité des munitions RD-033 (pur C#, testable sans Unity).
    /// Réserves comptées en COUPS : Quantity d'une Charge Laser = tirs restants
    /// (10 à l'achat), d'un Carquois = flèches restantes (20 à l'achat).
    /// Prélèvement exact, zéro gaspillage.
    /// </summary>
    public static class WeaponAmmo
    {
        /// <summary>Vrai si l'arme consomme des munitions pour ce jet (tirs uniquement).</summary>
        public static bool UsesAmmo(InventoryItem weapon, SkillType attackSkill)
        {
            if (weapon == null || weapon.AmmoCapacity <= 0) return false;
            return attackSkill == SkillType.Ballistique || attackSkill == SkillType.ProjectilesTir;
        }

        public static bool IsHighDensity(InventoryItem ammo)
        {
            return ammo != null && !string.IsNullOrEmpty(ammo.Name) && ammo.Name.Contains("Haute Densité");
        }

        public static bool MatchesType(InventoryItem ammo, string ammoType)
        {
            if (ammo == null || ammo.Type != ItemType.Ammunition) return false;
            if (string.IsNullOrEmpty(ammoType) || string.IsNullOrEmpty(ammo.Name)) return false;
            return ammo.Name.Contains(ammoType);
        }

        /// <summary>Coups d'une charge neuve (affichage / achat).</summary>
        public static int ShotsPerNewUnit(string ammoName)
        {
            if (string.IsNullOrEmpty(ammoName)) return 1;
            if (ammoName.Contains("Carquois")) return 20;
            if (ammoName.Contains("Charge Laser")) return 10;
            return 1;
        }

        /// <summary>Total des coups en réserve pour ce type (HD incluse ou non selon hd).</summary>
        public static int StockShots(CharacterSheet sheet, string ammoType, bool hd)
        {
            if (sheet?.Inventory == null || string.IsNullOrEmpty(ammoType)) return 0;
            int total = 0;
            for (int i = 0; i < sheet.Inventory.Count; i++)
            {
                var it = sheet.Inventory[i];
                if (it == null || !MatchesType(it, ammoType)) continue;
                if (IsHighDensity(it) != hd) continue;
                total += Math.Max(0, it.Quantity);
            }
            return total;
        }

        /// <summary>Retire jusqu'à count coups du stock, retourne le nombre réellement pris.</summary>
        public static int TakeShots(CharacterSheet sheet, string ammoType, bool hd, int count)
        {
            if (sheet?.Inventory == null || count <= 0) return 0;
            int taken = 0;
            for (int i = sheet.Inventory.Count - 1; i >= 0 && taken < count; i--)
            {
                var it = sheet.Inventory[i];
                if (it == null || !MatchesType(it, ammoType)) continue;
                if (IsHighDensity(it) != hd) continue;
                if (it.Quantity <= 0)
                {
                    sheet.Inventory.RemoveAt(i);
                    continue;
                }
                int take = Math.Min(count - taken, it.Quantity);
                it.Quantity -= take;
                taken += take;
                if (it.Quantity <= 0) sheet.Inventory.RemoveAt(i);
            }
            return taken;
        }
    }
}

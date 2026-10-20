using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Tactics.Grid;
using Killtime.Core.Combat;
using Killtime.Core.Character;
using Killtime.Core.Dice;
using Killtime.Core.Inventory;
using Killtime.UI;

namespace Killtime.Tactics.CombatUI
{
    /// <summary>
    /// Helpers consommables / outils marketplace du registre d'actions (extrait sans modification depuis CombatActionRegistry.cs).
    /// Résolus à l'exécution pour suivre le stock réel (Quantity / ConsumeOne).
    /// </summary>
    public static partial class CombatActionRegistry
    {
        // Helpers consommables marketplace (Livre VII §28.3). Résolus à l'exécution
        // pour suivre le stock réel (Quantity / ConsumeOne).
        private static InventoryItem FindBestHealingConsumable(TacticalUnit actor)
        {
            if (actor?.Sheet?.Inventory == null) return null;
            InventoryItem best = null;
            for (int i = 0; i < actor.Sheet.Inventory.Count; i++)
            {
                var it = actor.Sheet.Inventory[i];
                if (it == null) continue;
                if (it.Type != ItemType.Consumable) continue;
                if (it.HealingAmount <= 0) continue;
                if (best == null || it.HealingAmount > best.HealingAmount) best = it;
            }
            return best;
        }

        private static InventoryItem FindConsumableByName(TacticalUnit actor, string nameFragment)
        {
            if (actor?.Sheet?.Inventory == null || string.IsNullOrEmpty(nameFragment)) return null;
            for (int i = 0; i < actor.Sheet.Inventory.Count; i++)
            {
                var it = actor.Sheet.Inventory[i];
                if (it == null) continue;
                if (!string.IsNullOrEmpty(it.Name) && it.Name.Contains(nameFragment)) return it;
            }
            return null;
        }

        private static int CountConsumableStock(TacticalUnit actor, string itemName)
        {
            if (actor?.Sheet?.Inventory == null) return 0;
            int total = 0;
            for (int i = 0; i < actor.Sheet.Inventory.Count; i++)
            {
                var it = actor.Sheet.Inventory[i];
                if (it == null) continue;
                if (it.Name == itemName) total += it.IsStackable ? System.Math.Max(1, it.Quantity) : 1;
            }
            return total;
        }

        // Helpers outils marketplace (possession sauf mention, §28-33).
        private static InventoryItem FindTool(TacticalUnit actor, string fragment)
        {
            var inv = actor?.Sheet?.Inventory;
            if (inv == null || string.IsNullOrEmpty(fragment)) return null;
            for (int i = 0; i < inv.Count; i++)
            {
                var it = inv[i];
                if (it == null || string.IsNullOrEmpty(it.Name)) continue;
                if (it.Name.Contains(fragment)) return it;
            }
            return null;
        }

        private static bool HasTool(TacticalUnit actor, string fragment) => FindTool(actor, fragment) != null;

        private static bool ConsumeTool(TacticalUnit actor, string fragment)
        {
            var tool = FindTool(actor, fragment);
            if (tool == null || actor?.Sheet == null) return false;
            return actor.Sheet.ConsumeOne(tool.ItemId);
        }

        private static bool HasWeakenedStatus(CharacterStats stats)
        {
            if (stats == null) return false;
            var fx = stats.ActiveStatus;
            return fx.HasFlag(StatusEffect.ATerre) || fx.HasFlag(StatusEffect.Destabilise)
                || fx.HasFlag(StatusEffect.Sonne) || fx.HasFlag(StatusEffect.Etourdi)
                || fx.HasFlag(StatusEffect.Paralyse);
        }

        public static bool HasEquippedShield(TacticalUnit actor)
        {
            var inv = actor?.Sheet?.Inventory;
            if (inv == null) return false;
            for (int i = 0; i < inv.Count; i++)
            {
                var it = inv[i];
                if (it == null || !it.IsEquipped) continue;
                if (!string.IsNullOrEmpty(it.Name) && it.Name.IndexOf("Bouclier", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                if (!string.IsNullOrEmpty(it.Category) && it.Category.IndexOf("Bouclier", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }
    }
}

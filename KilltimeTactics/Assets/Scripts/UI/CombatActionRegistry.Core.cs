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
    /// Helpers portée / munitions du registre d'actions (extrait sans modification depuis CombatActionRegistry.cs).
    /// </summary>
    public static partial class CombatActionRegistry
    {
        private static int GetDistance(TacticalUnit a, TacticalUnit b)
        {
            if (a == null || b == null) return int.MaxValue;
            if (a.FootprintType == TitanFootprintType.Single && b.FootprintType == TitanFootprintType.Single)
                return a.CurrentCoords.DistanceTo(b.CurrentCoords);
            return TitanFootprint.MinDistanceBetweenUnits(a.CurrentCoords, a.FootprintType, b.CurrentCoords, b.FootprintType);
        }

        public static bool HasRangedWeaponEquipped(TacticalUnit actor)
        {
            if (actor == null) return false;
            var weapon = actor.Sheet?.GetEquippedWeapon();
            return weapon != null && weapon.RangeInTiles > 1;
        }

        public static int GetAttackMaxRange(TacticalUnit actor)
        {
            if (actor == null) return 1;
            var weapon = actor.Sheet?.GetEquippedWeapon();
            if (weapon != null && weapon.RangeInTiles > 0) return weapon.RangeInTiles;
            return 1; // Mains nues par défaut = portée de contact 1 case (Codex Livre V)
        }

        public static bool IsTargetInRange(TacticalUnit actor, TacticalUnit target)
        {
            if (actor == null || target == null) return false;
            int distance = GetDistance(actor, target);
            int maxRange = GetAttackMaxRange(actor);

            // Arme de contact ou mains nues (portée 1)
            if (maxRange <= 1 || !HasRangedWeaponEquipped(actor))
            {
                return distance <= 1;
            }

            // Arme à distance équipée
            return distance <= maxRange;
        }

        public static bool HasEnoughAP(TacticalUnit actor, int apCost)
        {
            return actor != null && actor.Stats != null && actor.Stats.CurrentActionPoints >= apCost;
        }

        private static bool CanAttackTarget(TacticalUnit actor, TacticalUnit target, int apCost)
        {
            if (actor == null || target == null) return false;
            if (target.Stats == null || !target.Stats.IsAlive) return false;
            if (actor.Stats == null || actor.Stats.CurrentActionPoints < apCost) return false;

            int distance = GetDistance(actor, target);
            int maxRange = GetAttackMaxRange(actor);

            // Combat à mains nues ou arme de contact : contact immédiat requis (<= 1 case)
            if (maxRange <= 1 || !HasRangedWeaponEquipped(actor))
            {
                return distance <= 1;
            }

            // RD-038 : Le sprint bloque tout tir à distance ce tour
            if (!ChargeState.CanFireRanged(actor.Stats))
            {
                return false;
            }

            return distance <= maxRange;
        }

        private static SkillType ResolveContactSkill(TacticalUnit actor, TacticalUnit target)
        {
            if (actor == null) return SkillType.MainsNues;

            // Hors de portée de contact avec une arme à distance disponible => tir.
            // Évite le cas "mêlée jouée à distance" quand le menu a été ouvert au contact
            // puis l'unité s'est déplacée avant de valider.
            if (actor != null && target != null)
            {
                int dist = GetDistance(actor, target);
                if (dist > 1 && HasRangedWeaponEquipped(actor))
                {
                    return SkillType.Ballistique;
                }
            }

            var weapon = actor.Sheet?.GetEquippedWeapon();

            if (weapon != null)
            {
                // Valeur legacy ArmesContondantes rabattue sur Maniement d'Arme.
                return SkillDefinitions.ResolveBaseSkill(weapon.AssociatedSkill);
            }

            // Absence d'inventaire ou d'arme équipée : combat au corps-à-corps à mains nues
            return SkillType.MainsNues;
        }

        /// <summary>RD-033 : ce duel consomme-t-il un coup du chargeur ? (même règle que l'arène).</summary>
        private static bool FiresShot(TacticalUnit actor, TacticalUnit target)
        {
            if (actor == null) return false;
            var weapon = actor.Sheet?.GetEquippedWeapon();
            if (weapon == null || weapon.AmmoCapacity <= 0) return false;
            return WeaponAmmo.UsesAmmo(weapon, ResolveContactSkill(actor, target));
        }

        /// <summary>RD-033 : tir possible (chargeur non vide, arme non enrayée) ?</summary>
        private static bool CanFireShot(TacticalUnit actor, TacticalUnit target)
        {
            if (actor == null) return false;
            // RD-038 : Le sprint bloque tout tir ce tour
            if (FiresShot(actor, target) && !ChargeState.CanFireRanged(actor.Stats)) return false;
            var weapon = actor.Sheet?.GetEquippedWeapon();
            if (weapon == null || weapon.AmmoCapacity <= 0) return true;
            if (!FiresShot(actor, target)) return true;
            return !weapon.Jammed && weapon.AmmoRemaining > 0;
        }

        /// <summary>RD-033 : compteur chargeur pour les libellés ([🔋X/Y], [VIDE], [ENRAYÉE]).</summary>
        private static string AmmoTag(TacticalUnit actor, TacticalUnit target)
        {
            if (actor == null) return "";
            if (FiresShot(actor, target) && !ChargeState.CanFireRanged(actor.Stats))
            {
                return " [SPRINT : TIR BLOQUÉ]";
            }
            var weapon = actor.Sheet?.GetEquippedWeapon();
            if (weapon == null || weapon.AmmoCapacity <= 0) return "";
            if (!FiresShot(actor, target)) return "";
            if (weapon.Jammed) return " [ENRAYÉE]";
            return weapon.AmmoRemaining > 0
                ? $" [🔋{weapon.AmmoRemaining}/{weapon.AmmoCapacity}{(weapon.LoadedHD ? "+HD" : "")}]"
                : " [VIDE]";
        }
    }
}

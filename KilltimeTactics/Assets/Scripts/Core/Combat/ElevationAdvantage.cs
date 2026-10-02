using System;
using Killtime.Core.Character;
using Killtime.Core.Inventory;

namespace Killtime.Core.Combat
{
    /// <summary>
    /// RD-036 Bonus hauteur / contrebas (palier 1 case 3D = 1.5 m).
    /// Module pur (aucune dépendance Unity) : quantification du dénivelé +
    /// règle validée — mêlée (tableau par paliers, gate verticale), tir
    /// balistique conventionnel (±1 capé), armes laser/énergie exemptées.
    /// Le couvert 3D émergent reste géré par CoverSystem ; ce module ne fait
    /// que le bonus/malus au jet + l'atteignabilité verticale en mêlée.
    /// </summary>
    public enum ElevationAttackKind
    {
        None,
        Melee,
        Ranged
    }

    public struct ElevationRuling
    {
        public ElevationAttackKind Kind;
        public int Level;
        public float HeightDiff;
        public bool Reachable;
        public int AttackMod;
        public string Label;
    }

    public static class ElevationAdvantage
    {
        /// <summary>Hauteur d'un palier vertical : 1 case 3D = 1.5 m.</summary>
        public const float LevelHeightMeters = 1.5f;

        /// <summary>
        /// Palier de dénivelé : round(dh / 1.5). |dh| &lt; 0.75 m = palier
        /// (les micro-reliefs 0.25/0.5 m des maps actuelles ne déclenchent rien).
        /// </summary>
        public static int LevelFromHeightDiff(float dh)
        {
            return (int)Math.Round(dh / LevelHeightMeters, MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// Vrai si l'arme à distance est à énergie (laser) : aucun modificateur
        /// de hauteur (trajectoire quasi-rectiligne, seule la ligne de vue compte).
        /// Les épées laser restent des armes DE MÊLÉE (levier physique) et suivent
        /// la table mêlée : seule la catégorie "Fusils Laser" / munition laser exempte.
        /// </summary>
        public static bool IsEnergyRangedWeapon(InventoryItem weapon)
        {
            if (weapon == null) return false;
            if (weapon.Category == "Fusils Laser") return true;
            if (!string.IsNullOrEmpty(weapon.AmmoType)
                && weapon.AmmoType.IndexOf("Laser", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return false;
        }

        /// <summary>
        /// Table mêlée validée (niveau = Eatt - Edef en paliers de 1.5 m).
        /// rangeInTiles &gt;= 2 = allonge (pique, espadon à allonge...).
        /// </summary>
        public static ElevationRuling EvaluateMelee(int level, int rangeInTiles)
        {
            var ruling = new ElevationRuling
            {
                Kind = ElevationAttackKind.Melee,
                Level = level,
                HeightDiff = level * LevelHeightMeters,
                Reachable = true,
                AttackMod = 0,
                Label = ""
            };
            bool hasReach = rangeInTiles >= 2;

            if (level >= 3)
            {
                ruling.Reachable = false;
                ruling.Label = "[Hors d'atteinte : +3 paliers]";
                return ruling;
            }
            if (level == 2)
            {
                if (!hasReach)
                {
                    ruling.Reachable = false;
                    ruling.Label = "[Hors d'atteinte : +2 paliers sans allonge]";
                    return ruling;
                }
                ruling.AttackMod = 1;
                ruling.Label = "[Surplomb +2 paliers, allonge +1]";
                return ruling;
            }
            if (level == 1)
            {
                ruling.AttackMod = hasReach ? 2 : 1;
                ruling.Label = hasReach ? "[Surplomb +1 palier, allonge +2]" : "[Surplomb +1 palier +1]";
                return ruling;
            }
            if (level == 0) return ruling;
            if (level == -1)
            {
                ruling.AttackMod = -1;
                ruling.Label = "[Contrebas -1 palier -1]";
                return ruling;
            }
            if (level == -2)
            {
                if (!hasReach)
                {
                    ruling.Reachable = false;
                    ruling.Label = "[Hors d'atteinte : -2 paliers sans allonge]";
                    return ruling;
                }
                ruling.AttackMod = -2;
                ruling.Label = "[Contrebas -2 paliers, allonge limite -2]";
                return ruling;
            }
            ruling.Reachable = false;
            ruling.Label = "[Hors d'atteinte : -3 paliers et plus]";
            return ruling;
        }

        /// <summary>
        /// Tir balistique conventionnel : +1 en surplomb, -1 en contrebas,
        /// capé à ±1 (pas de cumul par palier). Énergie = aucun modificateur.
        /// Le tir n'a jamais de gate verticale (la ligne de vue tranche).
        /// </summary>
        public static ElevationRuling EvaluateRanged(int level, bool isEnergyWeapon)
        {
            var ruling = new ElevationRuling
            {
                Kind = ElevationAttackKind.Ranged,
                Level = level,
                HeightDiff = level * LevelHeightMeters,
                Reachable = true,
                AttackMod = 0,
                Label = ""
            };
            if (isEnergyWeapon) return ruling;
            if (level >= 1)
            {
                ruling.AttackMod = 1;
                ruling.Label = "[Surplomb +1]";
            }
            else if (level <= -1)
            {
                ruling.AttackMod = -1;
                ruling.Label = "[Contrebas -1]";
            }
            return ruling;
        }

        /// <summary>
        /// Point d'entrée unique : quantifie dh puis applique la table selon le
        /// type d'attaque. isMeleeAttack suit la même définition que l'arène
        /// (skill de mêlée OU arme de contact). Les compétences non-martiales
        /// (magie, techniques) ne sont pas modifiées.
        /// </summary>
        public static ElevationRuling Resolve(
            float attackerElevation,
            float defenderElevation,
            bool isMeleeAttack,
            SkillType attackSkill,
            InventoryItem weapon)
        {
            float dh = attackerElevation - defenderElevation;
            int level = LevelFromHeightDiff(dh);
            int range = weapon != null ? Math.Max(1, weapon.RangeInTiles) : 1;

            if (isMeleeAttack)
            {
                var ruling = EvaluateMelee(level, range);
                ruling.HeightDiff = dh;
                return ruling;
            }

            if (attackSkill == SkillType.Ballistique)
            {
                var ruling = EvaluateRanged(level, IsEnergyRangedWeapon(weapon));
                ruling.HeightDiff = dh;
                return ruling;
            }

            return new ElevationRuling
            {
                Kind = ElevationAttackKind.None,
                Level = level,
                HeightDiff = dh,
                Reachable = true,
                AttackMod = 0,
                Label = ""
            };
        }

        /// <summary>
        /// Garde rapide pour les réactions de mêlée (opportunité) : vrai si le
        /// coup est verticalement atteignable, sans calculer de modificateur.
        /// </summary>
        public static bool IsMeleeReachable(float attackerElevation, float defenderElevation, int rangeInTiles)
        {
            return EvaluateMelee(LevelFromHeightDiff(attackerElevation - defenderElevation), rangeInTiles).Reachable;
        }
    }
}

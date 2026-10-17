using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Tactics.DuoTech;
using Killtime.Core.Character;
using Killtime.Core.Combat;

namespace Killtime.Tactics.CombatUI
{
    /// <summary>
    /// Identité du binôme (Lucas/Mina) + déclaration et paiement immédiat atomique. — extrait sans modification depuis DuoTechRegistry.cs.
    /// </summary>
    public static partial class DuoTechRegistry
    {
        // =====================================================================
        // IDENTITÉ (complémentarité Lucas / Mina exigée)
        // =====================================================================

        public static bool IsLucasMember(CharacterStats s)
        {
            if (s == null) return false;
            if (s.Sheet != null) return LucasCharacter.IsLucas(s.Sheet);
            return !string.IsNullOrEmpty(s.Name)
                && s.Name.IndexOf("Lucas", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsMinaMember(CharacterStats s)
        {
            if (s == null) return false;
            if (s.Sheet != null) return MinaCharacter.IsMina(s.Sheet);
            return !string.IsNullOrEmpty(s.Name)
                && s.Name.IndexOf("Mina", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsDuoMember(CharacterStats s) => IsLucasMember(s) || IsMinaMember(s);

        /// <summary>
        /// Les deux moitiés doivent être complémentaires : un Lucas + une Mina.
        /// </summary>
        public static bool AreComplementary(CharacterStats a, CharacterStats b)
        {
            if (a == null || b == null || ReferenceEquals(a, b)) return false;
            return (IsLucasMember(a) && IsMinaMember(b))
                || (IsMinaMember(a) && IsLucasMember(b));
        }

        public static int RoleCost(DuoTechDef def, CharacterStats s)
        {
            if (def == null || s == null) return int.MaxValue;
            if (IsLucasMember(s)) return def.LucasCost;
            if (IsMinaMember(s)) return def.MinaCost;
            return int.MaxValue;
        }

        // =====================================================================
        // DÉCLARATION + PAIEMENT IMMÉDIAT (atomique, sans dette)
        // =====================================================================

        public static bool CanDeclare(CharacterStats initiator, CharacterStats partner, DuoTechDef def, int currentRound, out string why)
        {
            why = string.Empty;
            if (def == null) { why = "Duo inconnu."; return false; }
            if (initiator == null || partner == null) { why = "Binôme incomplet."; return false; }
            if (!AreComplementary(initiator, partner)) { why = "Duo-Tech = 1 Lucas + 1 Mina."; return false; }
            if (!initiator.IsAlive) { why = $"{initiator.Name} est hors de combat."; return false; }
            if (!partner.IsAlive) { why = $"{partner.Name} est hors de combat."; return false; }

            // Limite : 1 duo par personnage par round (initiateur OU partenaire).
            // currentRound <= 0 = hors combat (exploration) : pas de limite.
            if (currentRound > 0)
            {
                if (initiator.LastDuoTechRound >= currentRound)
                {
                    why = $"{initiator.Name} a déjà tissé ce round (1 duo max).";
                    return false;
                }
                if (partner.LastDuoTechRound >= currentRound)
                {
                    why = $"{partner.Name} a déjà tissé ce round (1 duo max).";
                    return false;
                }
            }

            int initNeed = def.DeclareCost + RoleCost(def, initiator);
            int partNeed = RoleCost(def, partner);
            if (initiator.CurrentActionPoints < initNeed)
            {
                why = $"{initiator.Name} : {initiator.CurrentActionPoints} PA < {initNeed} requis.";
                return false;
            }
            if (partner.CurrentActionPoints < partNeed)
            {
                why = $"{partner.Name} : {partner.CurrentActionPoints} PA < {partNeed} requis (paiement immédiat).";
                return false;
            }
            return true;
        }

        /// <summary>
        /// Débite initiateur (départ + rôle) puis partenaire (rôle). Échec du
        /// second = remboursement du premier. Succès = duo armé, sans expiration,
        /// et les DEUX participants sont tamponnés au round courant (1 duo max).
        /// Ne touche PAS aux actions principales (action unique).
        /// </summary>
        public static bool TryDeclarePayment(CharacterStats initiator, CharacterStats partner, DuoTechDef def, int currentRound)
        {
            if (!CanDeclare(initiator, partner, def, currentRound, out _)) return false;

            int initNeed = def.DeclareCost + RoleCost(def, initiator);
            int partNeed = RoleCost(def, partner);

            if (!initiator.ConsumeActionPoints(initNeed)) return false;
            if (!partner.ConsumeActionPoints(partNeed))
            {
                // Remboursement (Ralenti x2 déjà appliqué à la consommation :
                // on restaure au brut, plafond au max — pas de création de PA).
                initiator.CurrentActionPoints = Math.Min(
                    initiator.MaxActionPoints,
                    initiator.CurrentActionPoints + initNeed);
                return false;
            }

            if (currentRound > 0)
            {
                initiator.LastDuoTechRound = currentRound;
                partner.LastDuoTechRound = currentRound;
            }
            return true;
        }

    }
}

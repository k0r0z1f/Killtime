using System;
using Killtime.Core.Character;
using Killtime.Core.Dice;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Visibility;

namespace Killtime.Core.Combat
{
    /// <summary>
    /// Corps-à-corps RD-039/040 (lutte, étranglement, bousculade) — extrait sans modification depuis SpecialManeuverService.cs.
    /// </summary>
    public sealed partial class SpecialManeuverService
    {
        // =====================================================================
        // RD-039 : LUTTE, GRAPPLE & ÉTRANGLEMENT (Livre VI — Corps-à-corps)
        // =====================================================================

        /// <summary>
        /// RD-039 : Tentative de prise au corps-à-corps (Lutte / Grapple).
        /// Duel opposé aveugle Athlétisme (ou Mains Nues) vs Athlétisme (ou Défense Corporelle).
        /// Victoire de l'attaquant (Diff >= 0) : cible saisie et Immobilisée (1 tour).
        /// </summary>
        public OpposedCheckResult ResolveGrappleDuel(
            CharacterStats attacker,
            CharacterStats defender,
            int attackerBonusAP = 0,
            int attackerPE = 0,
            int defenderBonusAP = 0,
            int defenderPE = 0,
            bool defenderAutoStakes = false)
        {
            if (attacker == null) throw new ArgumentNullException(nameof(attacker));
            if (defender == null) throw new ArgumentNullException(nameof(defender));

            SkillType atkSkill = attacker.GetSkillDie(SkillType.Athletisme, true) >= attacker.GetSkillDie(SkillType.MainsNues, true)
                ? SkillType.Athletisme
                : SkillType.MainsNues;

            SkillType defSkill = defender.GetSkillDie(SkillType.Athletisme, false) >= defender.GetSkillDie(SkillType.DefenseCorporelle, false)
                ? SkillType.Athletisme
                : SkillType.DefenseCorporelle;

            var opposed = _resolution.ResolveOpposedCheck(
                attacker, atkSkill,
                defender, defSkill,
                attackerBonusAP, attackerPE,
                defenderBonusAP, defenderPE,
                defenderAutoStakes);

            if (opposed.AttackerWins)
            {
                GrappleState.SetGrapple(attacker, defender);
                opposed.CombatLog = $"🥋 <b>LUTTE — PRISE RÉUSSIE</b> : {attacker.Name} ceinture et verrouille {defender.Name} (Diff {opposed.Differential:+0;-0;0}) — Cible <b>IMMOBILISÉE</b> !\n" + opposed.CombatLog;
            }
            else
            {
                opposed.CombatLog = $"🥋 <b>LUTTE — ÉCHEC</b> : {defender.Name} repousse la saisie de {attacker.Name} (Diff {opposed.Differential:+0;-0;0}).\n" + opposed.CombatLog;
            }

            return opposed;
        }

        /// <summary>
        /// RD-039 : Se libérer d'une étreinte de lutte (Lutte — Dégagement).
        /// Duel opposé aveugle Athlétisme/Acrobatie/MainsNues (victime) vs Athlétisme (grappler).
        /// Victoire de la victime (Diff >= 0) : prise rompue, statut Immobilisé retiré.
        /// </summary>
        public OpposedCheckResult ResolveGrappleEscapeDuel(
            CharacterStats victim,
            CharacterStats grappler,
            int victimBonusAP = 0,
            int victimPE = 0,
            int grapplerBonusAP = 0,
            int grapplerPE = 0,
            bool grapplerAutoStakes = false)
        {
            if (victim == null) throw new ArgumentNullException(nameof(victim));
            if (grappler == null) throw new ArgumentNullException(nameof(grappler));

            SkillType escaperSkill = SkillType.Athletisme;
            var bestDie = victim.GetSkillDie(SkillType.Athletisme, true);
            var mnDie = victim.GetSkillDie(SkillType.MainsNues, true);
            if (mnDie > bestDie) { bestDie = mnDie; escaperSkill = SkillType.MainsNues; }
            var acroDie = victim.GetSkillDie(SkillType.Acrobatie, true);
            if (acroDie > bestDie) { escaperSkill = SkillType.Acrobatie; }

            var opposed = _resolution.ResolveOpposedCheck(
                victim, escaperSkill,
                grappler, SkillType.Athletisme,
                victimBonusAP, victimPE,
                grapplerBonusAP, grapplerPE,
                grapplerAutoStakes);

            if (opposed.AttackerWins)
            {
                GrappleState.ReleaseGrapple(victim);
                opposed.CombatLog = $"🔓 <b>LUTTE — PRISE BRISÉE</b> : {victim.Name} se dégage de l'étreinte de {grappler.Name} (Diff {opposed.Differential:+0;-0;0}) — Statut Immobilisé dissipé !\n" + opposed.CombatLog;
            }
            else
            {
                opposed.CombatLog = $"🔒 <b>LUTTE — ÉCHEC DU DÉGAGEMENT</b> : {grappler.Name} maintient fermement {victim.Name} en lutte (Diff {opposed.Differential:+0;-0;0}).\n" + opposed.CombatLog;
            }

            return opposed;
        }

        /// <summary>
        /// RD-039 : Étranglement au corps-à-corps sur cible saisie ou immobilisée.
        /// Duel opposé aveugle Athlétisme (attaquant) vs Endurance Physique (défenseur).
        /// Victoire de l'attaquant (Diff >= 0) : dégâts bruts au cou + statut Asphyxie (2 tours, drain PA).
        /// </summary>
        public OpposedCheckResult ResolveStrangulationDuel(
            CharacterStats attacker,
            CharacterStats defender,
            int attackerBonusAP = 0,
            int attackerPE = 0,
            int defenderBonusAP = 0,
            int defenderPE = 0,
            bool defenderAutoStakes = false)
        {
            if (attacker == null) throw new ArgumentNullException(nameof(attacker));
            if (defender == null) throw new ArgumentNullException(nameof(defender));

            SkillType atkSkill = attacker.GetSkillDie(SkillType.Athletisme, true) >= attacker.GetSkillDie(SkillType.MainsNues, true)
                ? SkillType.Athletisme
                : SkillType.MainsNues;

            SkillType defSkill = defender.GetSkillDie(SkillType.EndurancePhysique, false) >= defender.GetSkillDie(SkillType.Athletisme, false)
                ? SkillType.EndurancePhysique
                : SkillType.Athletisme;

            var opposed = _resolution.ResolveOpposedCheck(
                attacker, atkSkill,
                defender, defSkill,
                attackerBonusAP, attackerPE,
                defenderBonusAP, defenderPE,
                defenderAutoStakes);

            if (opposed.AttackerWins)
            {
                int rawDmg = Math.Max(2, 1 + opposed.Differential / 2);
                int remaining = defender.CurrentHealth - rawDmg;
                if (remaining <= 0)
                {
                    defender.EvaluateFatalBlow(BodyPart.CouTrachee, rawDmg);
                }
                else
                {
                    defender.CurrentHealth = remaining;
                }

                defender.ApplyStatus(StatusEffect.Asphyxie, 2);
                defender.ApplyResidualDamage(StatusEffect.Asphyxie, 2);

                opposed.CombatLog = $"🫁 <b>ÉTRANGLEMENT — SUFFOCATION</b> : {attacker.Name} compresse la trachée de {defender.Name} (Diff {opposed.Differential:+0;-0;0}) — {rawDmg} dégâts bruts au cou + <b>ASPHYXIE</b> (2 tours, pool 2 résiduels, -1 PA/tour) !\n" + opposed.CombatLog;
            }
            else
            {
                opposed.CombatLog = $"🫁 <b>ÉTRANGLEMENT — RÉSISTÉ</b> : {defender.Name} résiste à l'étranglement de {attacker.Name} (Diff {opposed.Differential:+0;-0;0}).\n" + opposed.CombatLog;
            }

            return opposed;
        }

        // =====================================================================
        // RD-040 : BOUSCULADE GÉNÉRIQUE & COUP DE BOUCLIER (Livre VI — Corps-à-corps)
        // =====================================================================

        /// <summary>
        /// RD-040 : Résolution de la Bousculade générique ou du Coup de bouclier (2 PA).
        /// Duel opposé aveugle basé sur la Force (Athlétisme / Mains Nues vs Défense Corporelle / Athlétisme).
        /// Victoire de l'attaquant (Diff >= 0) : recul forcé d'1 case (CausedKnockback = true).
        /// Si le recul heurte un obstacle ou un mur, la cible chute À Terre (résolu dans l'arène).
        /// </summary>
        public DamageResult ResolveShoveDuel(
            CharacterStats attacker,
            CharacterStats defender,
            bool isShieldBash,
            int attackerBonusAP = 0,
            int attackerPE = 0,
            int defenderBonusAP = 0,
            int defenderPE = 0,
            bool defenderAutoStakes = false)
        {
            if (attacker == null) throw new ArgumentNullException(nameof(attacker));
            if (defender == null) throw new ArgumentNullException(nameof(defender));

            SkillType atkSkill;
            if (isShieldBash)
            {
                atkSkill = attacker.GetSkillDie(SkillType.DefenseCorporelle, true) >= attacker.GetSkillDie(SkillType.Athletisme, true)
                    ? SkillType.DefenseCorporelle
                    : SkillType.Athletisme;
            }
            else
            {
                atkSkill = attacker.GetSkillDie(SkillType.Athletisme, true) >= attacker.GetSkillDie(SkillType.MainsNues, true)
                    ? SkillType.Athletisme
                    : SkillType.MainsNues;
            }

            SkillType defSkill = defender.GetSkillDie(SkillType.DefenseCorporelle, false) >= defender.GetSkillDie(SkillType.Athletisme, false)
                ? SkillType.DefenseCorporelle
                : SkillType.Athletisme;

            var opposed = _resolution.ResolveOpposedCheck(
                attacker, atkSkill,
                defender, defSkill,
                attackerBonusAP, attackerPE,
                defenderBonusAP, defenderPE,
                defenderAutoStakes);

            bool success = opposed.AttackerWins;
            int diff = opposed.Differential;

            int rawDmg = 0;
            int absorbed = 0;
            int finalDmg = 0;

            if (success && isShieldBash)
            {
                rawDmg = 3 + Math.Max(0, diff / 2);
                int totalArmor = defender.BaseArmorAbsorption + defender.GetWornArmorBonus();
                absorbed = Math.Min(totalArmor, rawDmg);
                int postArmor = Math.Max(0, rawDmg - absorbed);
                int shieldRem = defender.AbsorbShield(postArmor);
                finalDmg = Math.Max(0, shieldRem);

                if (defender.CurrentHealth - finalDmg <= 0)
                {
                    defender.EvaluateFatalBlow(BodyPart.Torse, finalDmg);
                }
                else
                {
                    defender.CurrentHealth -= finalDmg;
                }
            }

            string actionLabel = isShieldBash ? "COUP DE BOUCLIER" : "BOUSCULADE";
            string log = success
                ? $"💨 <b>{actionLabel} — RÉUSSIE</b> : {attacker.Name} repousse violemment {defender.Name} (Diff {diff:+0;-0;0}) ➔ <b>Recul forcé d'1 case (Knockback) !</b>"
                    + (isShieldBash && finalDmg > 0 ? $"\n   ⚔️ Impact : {rawDmg} bruts &minus; {absorbed} armure = <color=#FF3B5C>{finalDmg} Dégâts Nets</color> ({defender.CurrentHealth}/{defender.MaxHealth} PV)." : "")
                : $"🛡️ <b>{actionLabel} — ÉCHEC</b> : {defender.Name} résiste et absorbe la poussée de {attacker.Name} (Diff {diff:+0;-0;0}).";

            log += $"\n{opposed.CombatLog}";

            return new DamageResult
            {
                IsHit = success,
                IsBlocked = !success,
                Differential = diff,
                AttackRoll = opposed.AttackRoll,
                DefenseRoll = opposed.DefenseRoll,
                TargetPart = BodyPart.Torse,
                ActualHitPart = BodyPart.Torse,
                RawDamage = rawDmg,
                ArmorAbsorbed = absorbed,
                FinalDamageApplied = finalDmg,
                FatalResolution = (success && isShieldBash && defender.CurrentHealth <= 0) ? defender.EvaluateFatalBlow(BodyPart.Torse, finalDmg) : FatalBlowResolution.None,
                CausedKnockback = success,
                CombatLog = log
            };
        }
    }
}

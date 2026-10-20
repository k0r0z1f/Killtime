using System;
using Killtime.Core.Character;
using Killtime.Core.Dice;
using Killtime.Tactics.Grid;

namespace Killtime.Core.Combat
{
    /// <summary>
    /// RD-204 (Livre VI §24.4 & Livre V) : marges manquantes —
    /// Possession (Diff 10+ voie Esprit, contrôle 1 tour),
    /// Attirer / Repousser en épreuve (Diff 1+, Refoulement déjà codé).
    /// Duels opposés aveugles, même assiette que lutte/bousculade RD-039/040.
    /// </summary>
    public sealed partial class SpecialManeuverService
    {
        /// <summary>
        /// Vrai si la cible relève du seuil boss (immunité partielle à cadrer) :
        /// profil PnjBoss, pointure Titan, ou nom/spécialisation de boss.
        /// </summary>
        public static bool IsPossessionBossLike(CharacterStats defender)
        {
            if (defender == null) return false;
            try
            {
                if (defender.Sheet != null && defender.Sheet.Profile == CharacterProfileType.PnjBoss)
                    return true;
            }
            catch { }
            string name = defender.Name ?? "";
            if (name.IndexOf("boss", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("titan", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("bête", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("bete", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("minulican", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("disciple", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            try
            {
                if (defender.HasSpecialization("Boss") || defender.HasSpecialization("Titan"))
                    return true;
                var specs = defender.Sheet?.UnlockedSpecializations;
                if (specs != null)
                {
                    for (int i = 0; i < specs.Count; i++)
                    {
                        string s = specs[i] ?? "";
                        if (s.IndexOf("boss", StringComparison.OrdinalIgnoreCase) >= 0
                            || s.IndexOf("titan", StringComparison.OrdinalIgnoreCase) >= 0)
                            return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private static int PossessThreshold(CharacterStats defender)
        {
            var cfg = Rules.CoreRulesConfig.Instance;
            int std = cfg != null && cfg.PossessDifferentialThreshold > 0
                ? cfg.PossessDifferentialThreshold : 10;
            int boss = cfg != null && cfg.PossessBossThreshold > 0
                ? cfg.PossessBossThreshold : 15;
            return IsPossessionBossLike(defender) ? boss : std;
        }

        private static int ForceMoveThreshold()
        {
            var cfg = Rules.CoreRulesConfig.Instance;
            return cfg != null && cfg.ForceMoveDifferentialThreshold > 0
                ? cfg.ForceMoveDifferentialThreshold : 1;
        }

        /// <summary>
        /// RD-193 (dev-only) : applique le différentiel forcé au défi opposé
        /// (miroir du rig de ResolveBlindDuel, qui ne couvre que les duels).
        /// Recalcule le total défensif (attaque − forcé) puis restaure via ClearDevRig.
        /// </summary>
        private static void ApplyDevForcedDifferential(OpposedCheckResult opposed)
        {
            if (opposed == null || !DiceRoller.DevForcedDifferential.HasValue) return;
            int forced = DiceRoller.DevForcedDifferential.Value;
            var d = opposed.DefenseRoll;
            d.Total = DiceRoller.DevApplyForcedDifferential(opposed.AttackRoll.Total, forced);
            d.Differential = d.Total - d.TargetDC;
            opposed.DefenseRoll = d;
            opposed.Differential = forced;
        }

        /// <summary>
        /// RD-204 Possession (Livre VI §24.4) : duel opposé aveugle
        /// MagieEsprit (contrôleur) vs MagieEsprit (cible).
        /// Diff ≥ seuil (10, 15 si boss) : contrôle 1 tour (PossessionState).
        /// </summary>
        public OpposedCheckResult ResolvePossessionDuel(
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

            var opposed = _resolution.ResolveOpposedCheck(
                attacker, SkillType.MagieEsprit,
                defender, SkillType.MagieEsprit,
                attackerBonusAP, attackerPE,
                defenderBonusAP, defenderPE,
                defenderAutoStakes);

            ApplyDevForcedDifferential(opposed);

            int threshold = PossessThreshold(defender);
            bool boss = IsPossessionBossLike(defender);
            bool success = opposed.AttackerWins && opposed.Differential >= threshold;
            // Le rig force le différentiel : l'issue suit le seuil, pas le signe.
            if (DiceRoller.DevForcedDifferential.HasValue)
                success = opposed.Differential >= threshold;

            if (success)
            {
                PossessionState.SetPossession(attacker, defender);
                opposed.AttackerWins = true;
                opposed.CombatLog = $"🧠 <b>POSSESSION — CONTRÔLE (Livre VI §24.4)</b> : {attacker.Name} s'empare de l'esprit de {defender.Name} (Diff {opposed.Differential:+0;-0;0} ≥ {threshold}{(boss ? ", seuil boss" : "")}) — Cible <b>CONTRÔLÉE 1 tour</b> !\n" + opposed.CombatLog;
            }
            else
            {
                opposed.AttackerWins = false;
                opposed.CombatLog = $"🧠 <b>POSSESSION — RÉSISTÉE</b> : {defender.Name} repousse l'emprise de {attacker.Name} (Diff {opposed.Differential:+0;-0;0} < {threshold}{(boss ? ", seuil boss" : "")}).\n" + opposed.CombatLog;
            }

            return opposed;
        }

        private DamageResult ResolveForceMoveDuel(
            CharacterStats attacker,
            CharacterStats defender,
            bool isPull,
            int attackerBonusAP,
            int attackerPE,
            int defenderBonusAP,
            int defenderPE,
            bool defenderAutoStakes)
        {
            SkillType atkSkill = attacker.GetSkillDie(SkillType.Athletisme, true) >= attacker.GetSkillDie(SkillType.MainsNues, true)
                ? SkillType.Athletisme
                : SkillType.MainsNues;

            SkillType defSkill = defender.GetSkillDie(SkillType.DefenseCorporelle, false) >= defender.GetSkillDie(SkillType.Athletisme, false)
                ? SkillType.DefenseCorporelle
                : SkillType.Athletisme;

            var opposed = _resolution.ResolveOpposedCheck(
                attacker, atkSkill,
                defender, defSkill,
                attackerBonusAP, attackerPE,
                defenderBonusAP, defenderPE,
                defenderAutoStakes);

            ApplyDevForcedDifferential(opposed);

            int threshold = ForceMoveThreshold();
            bool success = opposed.AttackerWins && opposed.Differential >= threshold;
            if (DiceRoller.DevForcedDifferential.HasValue)
                success = opposed.Differential >= threshold;

            int diff = opposed.Differential;
            string label = isPull ? "ATTIRER" : "REPOUSSER";
            string icon = isPull ? "🧲" : "💨";
            string effect = isPull
                ? "Rapprochée forcée d'1 case (Attraction) !"
                : "Recul forcé d'1 case (Knockback) !";
            string log = success
                ? $"{icon} <b>{label} — RÉUSSI</b> : {attacker.Name} déplace {defender.Name} en épreuve (Diff {diff:+0;-0;0} ≥ {threshold}) ➔ <b>{effect}</b>"
                : $"{icon} <b>{label} — ÉCHEC</b> : {defender.Name} tient bon face à {attacker.Name} (Diff {diff:+0;-0;0} < {threshold}).";
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
                RawDamage = 0,
                ArmorAbsorbed = 0,
                FinalDamageApplied = 0,
                FatalResolution = FatalBlowResolution.None,
                CausedKnockback = !isPull && success,
                CausedPull = isPull && success,
                CombatLog = log
            };
        }

        /// <summary>
        /// RD-204 Repousser en épreuve : duel opposé aveugle
        /// (Athlétisme/Mains Nues vs Défense Corporelle/Athlétisme).
        /// Diff ≥ 1 : recul forcé d'1 case (CausedKnockback, résolu en arène
        /// comme tout refoulement). Sans dégât.
        /// </summary>
        public DamageResult ResolvePushDuel(
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
            return ResolveForceMoveDuel(attacker, defender, false,
                attackerBonusAP, attackerPE, defenderBonusAP, defenderPE, defenderAutoStakes);
        }

        /// <summary>
        /// RD-204 Attirer en épreuve : miroir de Repousser (même duel, Diff ≥ 1).
        /// Succès : rapprochée forcée d'1 case (CausedPull, application arène
        /// à brancher — suivi à part, comme le knockback générique).
        /// </summary>
        public DamageResult ResolvePullDuel(
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
            return ResolveForceMoveDuel(attacker, defender, true,
                attackerBonusAP, attackerPE, defenderBonusAP, defenderPE, defenderAutoStakes);
        }
    }
}

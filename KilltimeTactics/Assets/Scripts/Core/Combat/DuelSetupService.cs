using System;
using Killtime.Core.Character;
using Killtime.Core.Dice;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Visibility;

namespace Killtime.Core.Combat
{
    /// <summary>
    /// Étape 0-2 du duel aveugle : préparation (Begin), déclarations de mises
    /// (attaquant puis défenseur à l'aveugle). Aucun jet lancé ici.
    /// Corps copiés à l'identique depuis l'ancien CombatCalculator.
    /// </summary>
    public sealed class DuelSetupService
    {
        private readonly CombatDuelContext _ctx;

        public DuelSetupService(CombatDuelContext ctx)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
        }

        /// <summary>
        /// RD-053 : Vérifie si un combattant dispose d'une source lumineuse (lampe/torche)
        /// ou d'une vision thermique (spécialisation ou lunettes).
        /// </summary>
        public static bool HasLampOrThermal(CharacterStats stats)
        {
            if (stats == null) return false;
            if (stats.HasSpecialization(FogOfWarSystem.SpecThermal)) return true;
            if (SmokeScreen.HasGoggles(stats)) return true;

            var inv = stats.Sheet?.Inventory;
            if (inv != null)
            {
                for (int i = 0; i < inv.Count; i++)
                {
                    var it = inv[i];
                    if (it == null || string.IsNullOrEmpty(it.Name)) continue;
                    if (it.Name.IndexOf("Lampe", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        it.Name.IndexOf("Torche", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        it.Name.IndexOf("Flashlight", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        it.Name.IndexOf("Thermique", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        it.Name.IndexOf("Vision Nocturne", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        // =====================================================================
        // API DUEL AVEUGLE PAS-À-PAS (pour UI interactive / IA / tests)
        // Déclaration attaquant -> déclaration défenseur -> résolution simultanée.
        // =====================================================================

        /// <summary>
        /// Étape 0 : prépare le duel aveugle. Paie le coût de base d'attaque uniquement.
        /// Aucun jet n'est lancé, aucune mise n'est engagée, rien n'est révélé.
        /// Retourne null + message d'erreur si l'attaque est impossible (PA insuffisants,
        /// ou cible totalement à couvert / non visible, Livre V §21.3).
        /// </summary>
        public AttackDuel BeginDuel(
            CharacterStats attacker,
            CharacterStats defender,
            BodyPart targetedPart,
            DiceType attackDie,
            int attackModifier,
            DiceType defenseDie,
            int defenseModifier,
            int weaponBaseDamage,
            bool cancelPenaltyWithAP,
            bool defenderWantsToDefend,
            int defenderArmor,
            SkillType attackSkill,
            SkillType defenseSkill,
            out string error,
            CoverType cover = CoverType.None,
            int weaponBonusEc = 0,
            bool skipAttackerCost = false,
            int elevationAttackMod = 0,
            string elevationLabel = null,
            bool isNight = false)
        {
            error = null;
            var targetInfo = BodyPartInfo.GetInfo(targetedPart);
            var cfg = Rules.CoreRulesConfig.Instance;

            // Livre V §21.3 : cible totalement à couvert / non visible => attaque impossible.
            // Au contact, les combattants se voient : le couvert est ignoré.
            CoverType effectiveCover = cover;
            if (_ctx.IsAtContactDistance && cfg.CoverIgnoredAtContactDistance)
                effectiveCover = CoverType.None;
            if (effectiveCover == CoverType.Full && cfg.FullCoverBlocksAttack)
            {
                error = $"{attacker.Name} ne voit pas {defender.Name} (couvert total) — attaque impossible ! Déplacez-vous pour retrouver une ligne de mire.";
                return null;
            }

            bool isStealthAttack = StealthState.IsStealthed(attacker);
            int stealthBonus = isStealthAttack ? StealthState.StealthAttackBonus : 0;
            // RD-048 : attaque en canalisant = +2 au jet (consommé à la résolution).
            bool isChannelAttack = ChannelingState.IsChanneling(attacker);
            int channelBonus = isChannelAttack ? ChannelingState.PeekAttackBonus(attacker) : 0;

            bool isRangedShot = (attackSkill == SkillType.Ballistique || attackSkill == SkillType.ProjectilesTir);
            bool effectiveNight = isNight;
            if (!effectiveNight)
            {
                try
                {
                    var fow = FogOfWarManager.Instance;
                    if (fow != null && fow.IsNight) effectiveNight = true;
                }
                catch { /* ignore */ }
            }

            int nightMod = (isRangedShot && effectiveNight && !HasLampOrThermal(attacker)) ? -1 : 0;

            var duel = new AttackDuel
            {
                Attacker = attacker,
                Defender = defender,
                TargetedPart = targetedPart,
                AttackSkill = attackSkill,
                DefenseSkill = defenseSkill,
                AttackDie = attackDie,
                DefenseDie = defenseDie,
                AttackStatusMod = attackModifier,
                DefenseStatusMod = defenseModifier,
                WeaponBaseDamage = weaponBaseDamage,
                DefenderArmor = defenderArmor,
                CancelPenaltyWithAP = cancelPenaltyWithAP,
                DefenderWantsToDefend = isStealthAttack ? false : defenderWantsToDefend,
                IsRangedAttack = isRangedShot,
                IsMartialArtsStrike = (attackSkill == SkillType.MainsNues) && attacker.HasSpecialization("Arts Martiaux"),
                DefenderUnreactivePenalty = cfg.UnreactiveDefensePenalty,
                Cover = effectiveCover,
                CoverAttackPenalty = effectiveCover == CoverType.Half ? cfg.HalfCoverAttackPenalty
                    : effectiveCover == CoverType.ThreeQuarters ? cfg.ThreeQuartersCoverAttackPenalty : 0,
                BlockedByCover = false,
                IsStealthAttack = isStealthAttack,
                StealthAttackBonus = stealthBonus,
                IsChannelAttack = isChannelAttack,
                ChannelAttackBonus = channelBonus,
                IsNightAttack = effectiveNight,
                NightAttackMod = nightMod
            };

            // Modificateur de base d'attaque (hors mise déclarée) : états + visée + canon entravé + couvert + bonus arme EC + hauteur RD-036 + stealth RD-049 + nuit RD-053 + canalisation RD-048.
            int baseAtt = attackModifier + duel.CoverAttackPenalty + Math.Max(0, weaponBonusEc) + elevationAttackMod + stealthBonus + nightMod + channelBonus;
            duel.WeaponBonusEc = Math.Max(0, weaponBonusEc);
            duel.ElevationAttackMod = elevationAttackMod;
            duel.ElevationLabel = elevationLabel ?? "";
            if (duel.IsRangedAttack && !cancelPenaltyWithAP)
            {
                baseAtt += targetInfo.DifficultyModifier;
            }
            if (_ctx.IsAtContactDistance && duel.IsRangedAttack)
            {
                duel.AppliedCanonEntrave = true;
                if (!cancelPenaltyWithAP)
                {
                    baseAtt -= 2;
                }
            }
            duel.AttackBaseMod = baseAtt;

            // Déclaration implicite du coût de base d'attaque (sans mise bonus).
            duel.BaseAttackCost = cfg.BaseAttackAPCost + (cancelPenaltyWithAP ? cfg.CancelAimPenaltyAPCost : 0);

            // Arts Martiaux : Enchaînement Fluide (Livre III) — la seconde attaque
            // de mêlée à mains nues du tour coûte 1 PA de moins (minimum 1).
            if (attackSkill == SkillType.MainsNues
                && attacker.AttacksThisTurn >= 1
                && attacker.HasSpecialization("Arts Martiaux : Enchaînement Fluide"))
            {
                duel.BaseAttackCost = Math.Max(1, duel.BaseAttackCost - 1);
            }

            if (!skipAttackerCost && !attacker.ConsumeActionPoints(duel.BaseAttackCost))
            {
                error = $"{attacker.Name} n'a pas assez de PA ({attacker.CurrentActionPoints}/{duel.BaseAttackCost}) pour attaquer !";
                return null;
            }

            // RD-038 : Le sprint bloque tout tir à distance ce tour
            if (isRangedShot && !ChargeState.CanFireRanged(attacker))
            {
                error = $"{attacker.Name} a sprinté ce tour : tir impossible (RD-038).";
                return null;
            }

            // Éligibilité du Défenseur SANS prélèvement ni révélation.
            // Sa mise sera engagée à sa déclaration aveugle (DeclareDefenderStakes).
            duel.BaseDefenseCost = cfg.BaseReactionAPCost;
            duel.IsDefenderIncapacitated = !defender.CanDefendActively();
            bool effectiveWants = !isStealthAttack && defenderWantsToDefend && !duel.IsDefenderIncapacitated;
            duel.CanDefenderReact = false;
            duel.DefenderBasePaid = false;
            if (effectiveWants)
            {
                // Éligible si assez de PA pour la réaction de base (Ralenti x2 inclus).
                int required = duel.BaseDefenseCost;
                if (defender.ActiveStatus.HasFlag(StatusEffect.Ralenti))
                    required *= cfg.RalentiAPMultiplier;
                if (defender.CurrentActionPoints >= required)
                {
                    duel.CanDefenderReact = true;
                }
            }

            int defBase = defenseModifier;
            if (effectiveWants && !duel.CanDefenderReact)
            {
                defBase += cfg.UnreactiveDefensePenalty;
            }
            if (ChargeState.HasDefensePenalty(defender))
            {
                defBase += cfg.ChargeDefensePenalty;
            }
            // RD-048 : punition de progression (-1 en défense tant qu'on canalise).
            defBase += ChannelingState.GetDefenseModifier(defender);
            duel.DefenseBaseMod = defBase;

            return duel;
        }

        /// <summary>
        /// Étape 0 (variante compétences) : calcule les dés via GetSkillDie + règle non-exclusive,
        /// puis paie le coût de base d'attaque uniquement. Aucun jet, aucune mise, rien de révélé.
        /// </summary>
        public AttackDuel BeginSkillDuel(
            CharacterStats attacker,
            CharacterStats defender,
            BodyPart targetedPart,
            SkillType attackSkill,
            SkillType defenseSkill,
            int weaponBaseDamage,
            bool cancelPenaltyWithAP,
            bool defenderWantsToDefend,
            string attackerSpecialization,
            string defenderSpecialization,
            int defenderArmor,
            out string error,
            CoverType cover = CoverType.None,
            int weaponBonusEc = 0,
            bool skipAttackerCost = false,
            int elevationAttackMod = 0,
            string elevationLabel = null,
            bool isNight = false)
        {
            DiceType attackDie = attacker.GetSkillDie(attackSkill, true);
            int attackModifier = attacker.GetStatusModifier(attackSkill, isOffensive: true);
            DiceType defenseDie = defender.GetSkillDie(defenseSkill);
            int defenseModifier = defender.GetStatusModifier(defenseSkill, isOffensive: false);
            if (!SkillDefinitions.IsExclusivelyDefensive(defenseSkill))
            {
                bool hasDefSpec = (!string.IsNullOrEmpty(defenderSpecialization) && defender.HasSpecialization(defenderSpecialization))
                                || SkillDefinitions.HasDefensiveSpecialization(defender.Sheet, defenseSkill);
                if (!hasDefSpec) defenseDie = SkillDefinitions.StepDownDie(defenseDie);
            }
            return BeginDuel(attacker, defender, targetedPart, attackDie, attackModifier,
                defenseDie, defenseModifier, weaponBaseDamage, cancelPenaltyWithAP,
                defenderWantsToDefend, defenderArmor, attackSkill, defenseSkill, out error, cover, weaponBonusEc, skipAttackerCost, elevationAttackMod, elevationLabel, isNight);
        }

        /// <summary>
        /// Étape 1 : DÉCLARATION de l'attaquant. Engage la mise bonus (PA + PE) AVANT tout jet.
        /// 1 PA = +1, 1 PE = +DuelPEBonusPerPoint (plafond Constitution). Mise cachée.
        /// Retourne le bonus total engagé (PA appliqués + PE appliqués x valeur).
        /// </summary>
        public int DeclareAttackerStakes(AttackDuel duel, int bonusPA, int pePoints)
        {
            if (duel == null) throw new ArgumentNullException(nameof(duel));
            if (duel.AttackerStakesDeclared) return CombatDuelShared.CommittedAttackBonus(duel);
            var cfg = Rules.CoreRulesConfig.Instance;
            duel.AttackerBonusPAApplied = CombatDuelShared.SpendBonusPA(duel.Attacker, Math.Max(0, bonusPA));
            duel.AttackerPEApplied = duel.Attacker.SpendDuelPE(Math.Max(0, pePoints));
            duel.AttackerStakesDeclared = true;
            return duel.AttackerBonusPAApplied + duel.AttackerPEApplied * cfg.DuelPEBonusPerPoint;
        }

        /// <summary>
        /// Étape 2 : DÉCLARATION AVEUGLE du défenseur. Choisit sa compétence (ou le passif)
        /// et engage sa mise (base réaction + PA bonus + PE) SANS avoir vu ni le jet ni la
        /// mise de l'attaquant. Encaissement passif = 0 PA, 0 PE, aucun jet.
        /// Échec de paiement de la base -> réflexe à -2, 0 PA dépensé.
        /// Retourne le bonus total engagé.
        /// </summary>
        public int DeclareDefenderStakes(
            AttackDuel duel,
            SkillType defenseSkill,
            bool wantsToDefend,
            int bonusPA,
            int pePoints,
            string defenderSpecialization = null)
        {
            if (duel == null) throw new ArgumentNullException(nameof(duel));
            if (duel.DefenderStakesDeclared) return CombatDuelShared.CommittedDefenseBonus(duel);
            var cfg = Rules.CoreRulesConfig.Instance;

            duel.DefenseSkill = defenseSkill;
            duel.DefenderWantsToDefend = !duel.IsStealthAttack && wantsToDefend && !duel.IsDefenderIncapacitated;
            duel.DefenderStakesDeclared = true;
            duel.DefenderBonusPAApplied = 0;
            duel.DefenderPEApplied = 0;

            if (duel.IsStealthAttack || !duel.DefenderWantsToDefend)
            {
                // Encaissement passif : 0 PA, 0 PE, aucun jet à la résolution.
                duel.DefenderWantsToDefend = false;
                duel.CanDefenderReact = false;
                return 0;
            }

            // Recalcule le dé de la compétence déclarée (règle non-exclusive).
            duel.DefenseDie = duel.Defender.GetSkillDie(defenseSkill);
            if (!SkillDefinitions.IsExclusivelyDefensive(defenseSkill))
            {
                bool hasDefSpec = (!string.IsNullOrEmpty(defenderSpecialization) && duel.Defender.HasSpecialization(defenderSpecialization))
                                || SkillDefinitions.HasDefensiveSpecialization(duel.Defender.Sheet, defenseSkill);
                if (!hasDefSpec) duel.DefenseDie = SkillDefinitions.StepDownDie(duel.DefenseDie);
            }
            int statusMod = duel.Defender.GetStatusModifier(defenseSkill, isOffensive: false);
            if (ChargeState.HasDefensePenalty(duel.Defender))
            {
                statusMod += cfg.ChargeDefensePenalty;
            }
            // RD-048 : punition de progression en canalisation.
            statusMod += ChannelingState.GetDefenseModifier(duel.Defender);
            duel.DefenseStatusMod = statusMod;
            duel.DefenseBaseMod = statusMod;
            duel.CanDefenderReact = true;

            // Base réaction : débitée à la déclaration (sauf si déjà acquittée, ex: interception RD-032) ; échec -> réflexe à -2.
            if (!duel.DefenderBasePaid)
            {
                if (duel.Defender.ConsumeActionPoints(duel.BaseDefenseCost))
                {
                    duel.DefenderBasePaid = true;
                }
                else
                {
                    duel.DefenseBaseMod += cfg.UnreactiveDefensePenalty;
                    duel.CanDefenderReact = false;
                    return 0;
                }
            }

            duel.DefenderBonusPAApplied = CombatDuelShared.SpendBonusPA(duel.Defender, Math.Max(0, bonusPA));
            duel.DefenderPEApplied = duel.Defender.SpendDuelPE(Math.Max(0, pePoints));
            return CombatDuelShared.CommittedDefenseBonus(duel);
        }

        /// <summary>
        /// Déclaration aveugle automatique (IA / défense réactive sans fenêtre).
        /// Estime le besoin SANS voir le jet adverse : compare les espérances des dés
        /// (moyenne + modificateurs + mises attaquant inconnues = 0) et engage le
        /// complément dans la limite des PA restants après la base. Jamais de PE auto.
        /// </summary>
        public int AutoDeclareDefenderStakes(AttackDuel duel, string defenderSpecialization = null)
        {
            if (duel == null) throw new ArgumentNullException(nameof(duel));
            if (!duel.DefenderWantsToDefend || duel.IsDefenderIncapacitated || !duel.CanDefenderReact)
            {
                return DeclareDefenderStakes(duel, duel.DefenseSkill, false, 0, 0, defenderSpecialization);
            }
            int expectedAttack = (int)Math.Round(CombatDuelShared.DieAverage(duel.AttackDie)) + duel.AttackBaseMod;
            // La mise adverse est cachée : l'estimation l'ignore (0).
            int expectedDefense = (int)Math.Round(CombatDuelShared.DieAverage(duel.DefenseDie)) + duel.DefenseBaseMod;
            int need = expectedAttack - expectedDefense + 1;
            if (need < 0) need = 0;
            int affordable = Math.Max(0, duel.Defender.CurrentActionPoints - duel.BaseDefenseCost);
            int committed = Math.Min(need, affordable);
            return DeclareDefenderStakes(duel, duel.DefenseSkill, true, committed, 0, defenderSpecialization);
        }
    }
}

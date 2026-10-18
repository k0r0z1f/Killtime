using System;
using Killtime.Core.Character;
using Killtime.Core.Dice;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Visibility;

namespace Killtime.Core.Combat
{
    /// <summary>
    /// Étape 3 du duel aveugle : jets simultanés + révélation, défis opposés,
    /// interception garde du corps RD-032, épreuves simples et tirs de zone.
    /// Ordonne le setup (mises) puis le moteur de dégâts.
    /// Corps copiés à l'identique depuis l'ancien CombatCalculator.
    /// </summary>
    public sealed class DuelResolutionService
    {
        private readonly CombatDuelContext _ctx;
        private readonly DuelSetupService _setup;
        private readonly DamageEngineService _damage;
        private readonly CombatProgressionService _progression;

        public DuelResolutionService(
            CombatDuelContext ctx,
            DuelSetupService setup,
            DamageEngineService damage,
            CombatProgressionService progression)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            _setup = setup ?? throw new ArgumentNullException(nameof(setup));
            _damage = damage ?? throw new ArgumentNullException(nameof(damage));
            _progression = progression ?? throw new ArgumentNullException(nameof(progression));
        }

        /// <summary>
        /// Résolution officielle du Livre VI en duel aveugle.
        /// Les mises (PA bonus + PE) sont déclarées AVANT les jets et restent cachées
        /// jusqu'à la révélation simultanée.
        /// </summary>
        public DamageResult ResolveTargetedAttack(
            CharacterStats attacker,
            CharacterStats defender,
            BodyPart targetedPart,
            SkillType attackSkill,
            SkillType defenseSkill,
            int weaponBaseDamage = 5,
            bool cancelPenaltyWithAP = false,
            bool defenderWantsToDefend = true,
            string attackerSpecialization = null,
            string defenderSpecialization = null,
            int defenderArmor = 0,
            int attackerBonusAP = 0,
            int defenderBonusAP = 0,
            int attackerPE = 0,
            int defenderPE = 0,
            CoverType cover = CoverType.None,
            int weaponBonusEc = 0,
            bool isNight = false)
        {
            // RÈGLE CODEX : aucun mod de caractéristique ajouté. Malus d'états uniquement.
            // Le dé offensif applique le Corps Augmenté (Mains Nues : MAG substituable à FOR).
            DiceType attackDie = attacker.GetSkillDie(attackSkill, true);
            int attackModifier = attacker.GetStatusModifier(attackSkill, isOffensive: true);

            DiceType defenseDie = defender.GetSkillDie(defenseSkill);
            int defenseModifier = defender.GetStatusModifier(defenseSkill, isOffensive: false);

            // Règle de Défense Non-Exclusive (Livre III) :
            // Parer avec une arme ou mains nues sans la spé dédiée fait chuter le dé d'un niveau.
            // La spécialisation annule ce malus (aucun +1 flat, conforme au Codex).
            if (!SkillDefinitions.IsExclusivelyDefensive(defenseSkill))
            {
                bool hasDefSpec = (!string.IsNullOrEmpty(defenderSpecialization) && defender.HasSpecialization(defenderSpecialization))
                                || SkillDefinitions.HasDefensiveSpecialization(defender.Sheet, defenseSkill);

                if (!hasDefSpec)
                {
                    defenseDie = SkillDefinitions.StepDownDie(defenseDie);
                }
            }

            return ResolveTargetedAttackInternal(
                attacker: attacker,
                defender: defender,
                targetedPart: targetedPart,
                attackDie: attackDie,
                attackModifier: attackModifier,
                defenseDie: defenseDie,
                defenseModifier: defenseModifier,
                weaponBaseDamage: weaponBaseDamage,
                cancelPenaltyWithAP: cancelPenaltyWithAP,
                defenderWantsToDefend: defenderWantsToDefend,
                defenderArmor: defenderArmor,
                attackerBonusAP: attackerBonusAP,
                defenderBonusAP: defenderBonusAP,
                attackerPE: attackerPE,
                defenderPE: defenderPE,
                attackSkill: attackSkill,
                defenseSkill: defenseSkill,
                defenderSpecialization: defenderSpecialization,
                cover: cover,
                weaponBonusEc: weaponBonusEc,
                isNight: isNight
            );
        }

        /// <summary>
        /// Surcharge de compatibilité descendante avec injection directe de dés.
        /// Les mises PA/PE restent interprétées comme des déclarations aveugles préalables.
        /// </summary>
        public DamageResult ResolveTargetedAttack(
            CharacterStats attacker,
            CharacterStats defender,
            BodyPart targetedPart,
            DiceType attackDie,
            int attackModifier,
            DiceType defenseDie,
            int defenseModifier,
            int weaponBaseDamage,
            bool cancelPenaltyWithAP = false,
            int defenderArmor = 0,
            int attackerBonusAP = 0,
            int defenderBonusAP = 0,
            bool defenderWantsToDefend = true,
            SkillType attackSkill = SkillType.ManiementArmes,
            SkillType defenseSkill = SkillType.Esquive,
            int attackerPE = 0,
            int defenderPE = 0,
            CoverType cover = CoverType.None,
            int weaponBonusEc = 0,
            bool isNight = false)
        {
            return ResolveTargetedAttackInternal(
                attacker: attacker,
                defender: defender,
                targetedPart: targetedPart,
                attackDie: attackDie,
                attackModifier: attackModifier,
                defenseDie: defenseDie,
                defenseModifier: defenseModifier,
                weaponBaseDamage: weaponBaseDamage,
                cancelPenaltyWithAP: cancelPenaltyWithAP,
                defenderWantsToDefend: defenderWantsToDefend,
                defenderArmor: defenderArmor,
                attackerBonusAP: attackerBonusAP,
                defenderBonusAP: defenderBonusAP,
                attackerPE: attackerPE,
                defenderPE: defenderPE,
                attackSkill: attackSkill,
                defenseSkill: defenseSkill,
                defenderSpecialization: null,
                cover: cover,
                weaponBonusEc: weaponBonusEc,
                isNight: isNight
            );
        }

        private DamageResult ResolveTargetedAttackInternal(
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
            int attackerBonusAP,
            int defenderBonusAP,
            int attackerPE,
            int defenderPE,
            SkillType attackSkill,
            SkillType defenseSkill,
            string defenderSpecialization = null,
            CoverType cover = CoverType.None,
            int weaponBonusEc = 0,
            bool isNight = false)
        {
            // --- Duel aveugle : base attaque -> mise attaquant (cachée) ->
            // --- mise défenseur à l'aveugle -> révélation simultanée -> résolution normale.
            var duel = _setup.BeginDuel(
                attacker, defender, targetedPart,
                attackDie, attackModifier, defenseDie, defenseModifier,
                weaponBaseDamage, cancelPenaltyWithAP, defenderWantsToDefend,
                defenderArmor, attackSkill, defenseSkill, out string error, cover, weaponBonusEc, isNight: isNight);
            if (duel == null)
            {
                var cfg0 = Rules.CoreRulesConfig.Instance;
                bool coverBlocked = cover == CoverType.Full && cfg0.FullCoverBlocksAttack;
                return new DamageResult
                {
                    IsHit = false,
                    IsBlocked = coverBlocked,
                    Cover = cover,
                    CoverAttackPenalty = 0,
                    BlockedByCover = coverBlocked,
                    CombatLog = error
                };
            }

            _setup.DeclareAttackerStakes(duel, attackerBonusAP, attackerPE);
            if (duel.DefenderWantsToDefend && !duel.IsDefenderIncapacitated && duel.CanDefenderReact)
            {
                _setup.DeclareDefenderStakes(duel, defenseSkill, true, defenderBonusAP, defenderPE, defenderSpecialization);
            }
            else
            {
                _setup.DeclareDefenderStakes(duel, defenseSkill, false, 0, 0, defenderSpecialization);
            }

            return ResolveBlindDuel(duel);
        }

        /// <summary>
        /// Étape 3 : RÉSOLUTION. Les deux dés sont lancés SIMULTANÉMENT avec les mises
        /// déclarées, puis révélés ensemble (différentiel, dégâts, états).
        /// La déclaration du défenseur defaults au passif si elle n'a pas eu lieu.
        /// </summary>
        public DamageResult ResolveBlindDuel(AttackDuel duel)
        {
            if (duel == null) throw new ArgumentNullException(nameof(duel));
            if (duel.BlockedByCover || duel.Cover == CoverType.Full)
            {
                return new DamageResult
                {
                    IsHit = false,
                    IsBlocked = true,
                    BlockedByCover = true,
                    Cover = duel.Cover,
                    CoverAttackPenalty = duel.CoverAttackPenalty,
                    CombatLog = $"🛡️ <b>COUVERT TOTAL</b> : {duel.Attacker.Name} ne voit pas {duel.Defender.Name} — attaque impossible ! Déplacez-vous pour retrouver une ligne de mire."
                };
            }
            if (!duel.AttackerStakesDeclared)
                throw new InvalidOperationException("DeclareAttackerStakes doit précéder ResolveBlindDuel.");
            if (!duel.DefenderStakesDeclared)
            {
                _setup.DeclareDefenderStakes(duel, duel.DefenseSkill, false, 0, 0);
            }

            var cfg = Rules.CoreRulesConfig.Instance;
            int attCommitted = CombatDuelShared.CommittedAttackBonus(duel);
            int defCommitted = (duel.DefenderWantsToDefend && !duel.IsDefenderIncapacitated && duel.CanDefenderReact && duel.DefenderBasePaid)
                ? CombatDuelShared.CommittedDefenseBonus(duel)
                : 0;

            duel.AttackFinalRoll = RollAttackDevAware(duel.AttackDie, duel.AttackBaseMod + attCommitted, cfg.StandardTargetDC);

            bool effectiveDefend = duel.DefenderWantsToDefend && !duel.IsDefenderIncapacitated;
            if (!effectiveDefend)
            {
                duel.DefenseFinalRoll = new DiceRollResult
                {
                    DieType = duel.DefenseDie,
                    RawRoll = 0,
                    Modifier = 0,
                    Total = 0,
                    TargetDC = cfg.StandardTargetDC,
                    Differential = -cfg.StandardTargetDC,
                    IsSuccess = false,
                    IsCriticalSuccess = false,
                    IsCriticalFailure = false
                };
            }
            else
            {
                duel.DefenseFinalRoll = RollDefenseDevAware(duel.DefenseDie, duel.DefenseBaseMod + defCommitted, cfg.StandardTargetDC);
            }

            // RD-193 (dev-only, terrain VATS) : différentiel net forcé.
            // Null par défaut = révélation live verbatim. Quand positionné, le total
            // adverse est réécrit (bruts/critiques affichés inchangés) puis restauré
            // via DiceRoller.ClearDevRig() — aucune règle modifiée.
            if (DiceRoller.DevForcedDifferential.HasValue)
            {
                int forced = DiceRoller.DevForcedDifferential.Value;
                if (!effectiveDefend)
                {
                    var a = duel.AttackFinalRoll;
                    a.Total = forced;
                    a.Differential = forced - a.TargetDC;
                    duel.AttackFinalRoll = a;
                }
                else
                {
                    var d = duel.DefenseFinalRoll;
                    d.Total = DiceRoller.DevApplyForcedDifferential(duel.AttackFinalRoll.Total, forced);
                    d.Differential = d.Total - d.TargetDC;
                    duel.DefenseFinalRoll = d;
                }
            }

            duel.HasResolved = true;
            return _damage.ResolveDuelDamage(duel);
        }

        /// <summary>
        /// RD-193 (dev-only) : jet d'attaque sensible au rig par camp.
        /// Priorité : flag de camp, puis flags globaux RD-187, puis jet live.
        /// Tous à false par défaut = Roll() verbatim.
        /// </summary>
        private DiceRollResult RollAttackDevAware(DiceType die, int modifier, int targetDC)
        {
            if (DiceRoller.DevForceAttackCrit) return DiceRoller.DevForcedResult(die, modifier, targetDC, true);
            if (DiceRoller.DevForceAttackFumble) return DiceRoller.DevForcedResult(die, modifier, targetDC, false);
            return _ctx.DiceRoller.Roll(die, modifier, targetDC);
        }

        /// <summary>
        /// RD-193 (dev-only) : jet de défense sensible au rig par camp (miroir attaque).
        /// </summary>
        private DiceRollResult RollDefenseDevAware(DiceType die, int modifier, int targetDC)
        {
            if (DiceRoller.DevForceDefenseCrit) return DiceRoller.DevForcedResult(die, modifier, targetDC, true);
            if (DiceRoller.DevForceDefenseFumble) return DiceRoller.DevForcedResult(die, modifier, targetDC, false);
            return _ctx.DiceRoller.Roll(die, modifier, targetDC);
        }

        /// <summary>
        /// Défi opposé aveugle générique (Intimidation, story, etc.) : chaque camp déclare
        /// sa mise (PA + PE) avant les jets, les deux dés sont lancés simultanément.
        /// Sans coûts de base. Retourne les totaux, le différentiel et un log de révélation.
        /// </summary>
        public OpposedCheckResult ResolveOpposedCheck(
            CharacterStats attacker,
            SkillType attackSkill,
            CharacterStats defender,
            SkillType defenseSkill,
            int attackerBonusAP = 0,
            int attackerPE = 0,
            int defenderBonusAP = 0,
            int defenderPE = 0,
            bool defenderAutoStakes = false)
        {
            if (attacker == null) throw new ArgumentNullException(nameof(attacker));
            if (defender == null) throw new ArgumentNullException(nameof(defender));
            var cfg = Rules.CoreRulesConfig.Instance;

            DiceType attDie = attacker.GetSkillDie(attackSkill, true);
            DiceType defDie = defender.GetSkillDie(defenseSkill, false);
            int attStatus = attacker.GetStatusModifier(attackSkill, isOffensive: true);
            int defStatus = defender.GetStatusModifier(defenseSkill, isOffensive: false);
            if (!SkillDefinitions.IsExclusivelyDefensive(defenseSkill)
                && !SkillDefinitions.HasDefensiveSpecialization(defender.Sheet, defenseSkill))
            {
                defDie = SkillDefinitions.StepDownDie(defDie);
            }

            // Déclarations aveugles : débits immédiats, montants cachés jusqu'au jet.
            int attPA = CombatDuelShared.SpendBonusPA(attacker, Math.Max(0, attackerBonusAP));
            int attPE = attacker.SpendDuelPE(Math.Max(0, attackerPE));
            int defPA = 0;
            int defPE = 0;
            bool defenderPlays = defender.IsAlive && defender.CanDefendActively();
            if (defenderPlays)
            {
                if (defenderAutoStakes)
                {
                    // Estimation aveugle (mise adverse inconnue = 0) : complément au besoin
                    // estimé, dans la limite des PA disponibles. Jamais de PE auto.
                    int expectedAtt = (int)Math.Round(CombatDuelShared.DieAverage(attDie)) + attStatus;
                    int expectedDef = (int)Math.Round(CombatDuelShared.DieAverage(defDie)) + defStatus;
                    int need = Math.Max(0, expectedAtt - expectedDef + 1);
                    defPA = CombatDuelShared.SpendBonusPA(defender, Math.Min(need, Math.Max(0, defender.CurrentActionPoints)));
                }
                else
                {
                    defPA = CombatDuelShared.SpendBonusPA(defender, Math.Max(0, defenderBonusAP));
                    defPE = defender.SpendDuelPE(Math.Max(0, defenderPE));
                }
            }

            int attTotal = attStatus + attPA + attPE * cfg.DuelPEBonusPerPoint;
            int defTotal = defStatus + defPA + defPE * cfg.DuelPEBonusPerPoint;

            var attRoll = _ctx.DiceRoller.Roll(attDie, attTotal, cfg.StandardTargetDC);
            DiceRollResult defRoll = defenderPlays
                ? _ctx.DiceRoller.Roll(defDie, defTotal, cfg.StandardTargetDC)
                : new DiceRollResult
                {
                    DieType = defDie, RawRoll = 0, Modifier = 0, Total = 0,
                    TargetDC = cfg.StandardTargetDC, Differential = -cfg.StandardTargetDC,
                    IsSuccess = false, IsCriticalSuccess = false, IsCriticalFailure = false
                };

            int differential = attRoll.Total - defRoll.Total;
            string log = $"⚔️ <b>DÉFI OPPOSÉ AVEUGLE</b> : {attacker.Name} [{SkillDefinitions.GetDisplayName(attackSkill)}, mise cachée {attPA} PA + {attPE} PE] vs {defender.Name} [{SkillDefinitions.GetDisplayName(defenseSkill)}, mise cachée {(defenderPlays ? $"{defPA} PA + {defPE} PE" : "passif")}]\n"
                + $"   🎲 <b>RÉVÉLATION SIMULTANÉE :</b> {attRoll.Total} vs {defRoll.Total} ➔ <b>Différentiel {(differential >= 0 ? "+" : "")}{differential}</b> — <b>{(differential >= 0 ? "L'INITIATIVE L'EMPORTE" : "L'OPPOSITION L'EMPORTE")}</b>";

            // Livre I §5 : le gagnant du différentiel coche 1 case de progression.
            log += _progression.ApplyOpposedProgression(attacker, attackSkill, true, defender, defenseSkill, defenderPlays, differential);

            return new OpposedCheckResult
            {
                Attacker = attacker,
                Defender = defender,
                AttackSkill = attackSkill,
                DefenseSkill = defenseSkill,
                AttackRoll = attRoll,
                DefenseRoll = defRoll,
                AttackerBonusPAApplied = attPA,
                AttackerPEApplied = attPE,
                DefenderBonusPAApplied = defPA,
                DefenderPEApplied = defPE,
                Differential = differential,
                AttackerWins = differential >= 0,
                CombatLog = log
            };
        }

        // =====================================================================
        // RD-032 : GARDE DU CORPS / INTERCEPTION ALLIÉ (Livre VI §24.5)
        // Coût 1 PA réaction : le protecteur au contact (distance <= 1) prend
        // le coup à la place de l'allié ciblé. Le duel est réorienté vers lui.
        // =====================================================================

        /// <summary>
        /// Réoriente un duel d'attaque vers un protecteur qui s'interpose pour
        /// prendre le coup à la place de l'allié (RD-032, Livre VI §24.5).
        /// Le protecteur devient le défenseur du duel avec son armure et ses défenses.
        /// </summary>
        public AttackDuel RedirectDuelToProtector(
            AttackDuel duel,
            CharacterStats protector,
            SkillType? defenseSkill = null,
            string protectorSpecialization = null)
        {
            if (duel == null || protector == null) return duel;

            duel.Defender = protector;
            duel.DefenderArmor = protector.BaseArmorAbsorption;
            duel.IsDefenderIncapacitated = OpportunityState.IsCancelledByStatus(protector);
            duel.CanDefenderReact = !duel.IsDefenderIncapacitated;
            // La base de réaction (1 PA) a déjà été payée lors de l'interception.
            duel.DefenderBasePaid = true;

            SkillType defSkill = defenseSkill ?? (protector.Attributes.Agilite >= protector.Attributes.Force
                ? SkillType.Esquive
                : SkillType.DefenseCorporelle);
            duel.DefenseSkill = defSkill;
            duel.DefenseDie = protector.GetSkillDie(defSkill);
            if (!SkillDefinitions.IsExclusivelyDefensive(defSkill))
            {
                bool hasDefSpec = (!string.IsNullOrEmpty(protectorSpecialization) && protector.HasSpecialization(protectorSpecialization))
                                || SkillDefinitions.HasDefensiveSpecialization(protector.Sheet, defSkill);
                if (!hasDefSpec) duel.DefenseDie = SkillDefinitions.StepDownDie(duel.DefenseDie);
            }
            int statusMod = protector.GetStatusModifier(defSkill, isOffensive: false);
            if (ChargeState.HasDefensePenalty(protector))
            {
                statusMod += Rules.CoreRulesConfig.Instance.ChargeDefensePenalty;
            }
            // RD-048 : punition de progression en canalisation.
            statusMod += ChannelingState.GetDefenseModifier(protector);
            duel.DefenseStatusMod = statusMod;
            duel.DefenseBaseMod = statusMod;

            return duel;
        }

        /// <summary>
        /// Résout une attaque interceptée en duel aveugle : le protecteur prend le coup
        /// à la place au contact (coût 1 PA réaction).
        /// </summary>
        public DamageResult ResolveInterceptedAttack(
            CharacterStats attacker,
            CharacterStats protector,
            BodyPart targetedPart,
            SkillType attackSkill,
            int weaponBaseDamage,
            int attackerBonusAP = 0,
            int attackerPE = 0,
            CoverType cover = CoverType.None,
            int weaponBonusEc = 0)
        {
            var duel = _setup.BeginSkillDuel(
                attacker, protector, targetedPart,
                attackSkill, SkillType.Esquive,
                weaponBaseDamage, false, true,
                null, null, protector.BaseArmorAbsorption,
                out _, cover, weaponBonusEc, skipAttackerCost: false);

            if (duel == null)
            {
                return new DamageResult { CombatLog = "⚠️ Attaque interceptée impossible." };
            }

            duel.DefenderBasePaid = true;
            _setup.DeclareAttackerStakes(duel, attackerBonusAP, attackerPE);
            _setup.AutoDeclareDefenderStakes(duel);
            var result = ResolveBlindDuel(duel);
            result.CombatLog = $"🛡️ <b>[INTERCEPTION] {protector.Name}</b> prend le coup à la place !\n" + result.CombatLog;
            return result;
        }

        /// <summary>
        /// Épreuve simple hors duel (Livre II §7) : jet de compétence vs DC, avec
        /// progression organique automatique sur réussite (Livre I §5).
        /// 1 réussite = 1 case dans CETTE compétence ; piste pleine (cases = base) = +1 XP lié.
        /// </summary>
        public DiceRollResult ResolveSkillCheck(CharacterStats user, SkillType skill, int targetDC, bool isOffensive = false, int extraModifier = 0)
        {
            if (user == null) throw new ArgumentNullException(nameof(user));
            var cfg = Rules.CoreRulesConfig.Instance;
            DiceType die = user.GetSkillDie(skill, isOffensive);
            int mod = user.GetStatusModifier(skill, isOffensive) + extraModifier;
            var roll = _ctx.DiceRoller.Roll(die, mod, targetDC);
            if (roll.IsSuccess && !roll.IsCriticalFailure && user.Sheet != null)
                CharacterProgressionManager.RegisterSuccessfulSkillUse(user.Sheet, skill, out _, out _, isOffensive);
            return roll;
        }

        /// <summary>
        /// RD-034 : Résout un tir de cône, rafale ou suppression balistique avec dispersion,
        /// friendly fire et falloff de distance.
        /// </summary>
        public AreaFireResult ResolveAreaFireShot(
            CharacterStats attacker,
            HexCoordinates attackerPos,
            HexCoordinates targetPos,
            AreaFireProfile profile,
            System.Collections.Generic.IReadOnlyList<(CharacterStats stats, HexCoordinates pos, bool isAlly)> combatantsOnField,
            int attackerBonusAP = 0,
            int attackerPE = 0,
            CoverType fieldCover = CoverType.None)
        {
            var calc = new AreaFireCalculator(_ctx.DiceRoller);
            return calc.ResolveAreaFire(attacker, attackerPos, targetPos, profile, combatantsOnField, attackerBonusAP, attackerPE, fieldCover);
        }
    }
}

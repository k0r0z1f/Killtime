using System;
using Killtime.Core.Character;
using Killtime.Core.Dice;
using Killtime.Tactics.Grid;

namespace Killtime.Core.Combat
{
    /// <summary>
    /// Duel aveugle officiel (Livres II §7 + VI §24.1) :
    /// 1. L'attaquant DÉCLARE sa cible, son action (compétence + zone visée) et sa mise
    ///    (coût de base + PA bonus + PE), débités aussitôt. Résultat CACHÉ.
    /// 2. La cible DÉCLARE à l'aveugle son action opposée (compétence défensive ou
    ///    encaissement passif) et sa mise (base réaction + PA bonus + PE), sans avoir vu
    ///    ni le jet ni la mise de l'attaquant.
    /// 3. RÉSOLUTION : les deux dés sont lancés simultanément et révélés ensemble
    ///    (différentiel, dégâts, armure, états).
    /// Les dés découlent uniquement du niveau de compétence (paliers carac + entraînements
    /// depuis d2). Ex : Mains Nues +2 entr., FOR 5 (+2) = 4 niveaux -> d10 ;
    /// Esquive 1 entr., réf. 5 (+2) = 3 niveaux -> d8.
    /// 1 PA engagé = +1 au total, 1 PE (Essoufflement, plafond Constitution) = +1 au total.
    /// </summary>
    public sealed class AttackDuel
    {
        public CharacterStats Attacker;
        public CharacterStats Defender;
        public BodyPart TargetedPart;
        public SkillType AttackSkill;
        public SkillType DefenseSkill;
        public DiceType AttackDie;
        public DiceType DefenseDie;
        public int AttackStatusMod;
        public int DefenseStatusMod;
        public int WeaponBaseDamage;
        public int DefenderArmor;
        public bool CancelPenaltyWithAP;
        public bool DefenderWantsToDefend;

        // Coûts de base (attaquant débité au Begin, défenseur à sa déclaration)
        public int BaseAttackCost;
        public int BaseDefenseCost;
        public bool CanDefenderReact;
        public bool IsDefenderIncapacitated;
        public bool DefenderBasePaid;

        // Mises déclarées à l'aveugle (débitées à la déclaration, avant tout jet)
        public bool AttackerStakesDeclared;
        public int AttackerBonusPAApplied;
        public int AttackerPEApplied;
        public bool DefenderStakesDeclared;
        public int DefenderBonusPAApplied;
        public int DefenderPEApplied;

        // Modificateurs situationnels (hors mises déclarées)
        public int AttackBaseMod;
        public int DefenseBaseMod;
        public bool IsRangedAttack;
        public bool IsMartialArtsStrike;
        public bool AppliedCanonEntrave;

        // Couvert & visibilité (Livre VI §25.3) : Half -1, ThreeQuarters -2,
        // Full = cible non visible => attaque impossible (BeginDuel refuse).
        public CoverType Cover;
        public int CoverAttackPenalty;
        public bool BlockedByCover;

        // Jets uniques, lancés simultanément à la résolution avec les mises déclarées
        public DiceRollResult AttackFinalRoll;
        public DiceRollResult DefenseFinalRoll;
        public bool HasResolved;
        public int DefenderUnreactivePenalty;
    }

    /// <summary>
    /// Résultat générique d'un défi opposé aveugle (hors combat : Intimidation, story).
    /// </summary>
    public sealed class OpposedCheckResult
    {
        public CharacterStats Attacker;
        public CharacterStats Defender;
        public SkillType AttackSkill;
        public SkillType DefenseSkill;
        public DiceRollResult AttackRoll;
        public DiceRollResult DefenseRoll;
        public int AttackerBonusPAApplied;
        public int AttackerPEApplied;
        public int DefenderBonusPAApplied;
        public int DefenderPEApplied;
        public int Differential;
        public bool AttackerWins;
        public string CombatLog;
    }

    /// <summary>
    /// Résolution mathématique intégrale des passes d'armes et tirs ciblés (Livre VI).
    /// Conforme à l'Axiome Fondateur : Tout jet découle d'une compétence.
    /// L'attaque est opposée par une compétence défensive, sauf décision de non-défense.
    /// Séquence impérative : déclaration attaquant (mise cachée) -> déclaration défenseur
    /// à l'aveugle -> révélation simultanée -> résolution normale. Aucun résultat n'est
    /// révélé avant que les deux mises soient engagées.
    /// </summary>
    public class CombatCalculator
    {
        private readonly DiceRoller _diceRoller;
        private readonly Random _random;
        private bool _currentCombatIsAtContactDistance = false;

        public CombatCalculator(DiceRoller diceRoller = null)
        {
            _diceRoller = diceRoller ?? new DiceRoller();
            _random = new Random();
        }

        public void SetContactDistanceState(bool isAtContact)
        {
            _currentCombatIsAtContactDistance = isAtContact;
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
            CoverType cover = CoverType.None)
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
                cover: cover
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
            CoverType cover = CoverType.None)
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
                cover: cover
            );
        }

        // =====================================================================
        // API DUEL AVEUGLE PAS-À-PAS (pour UI interactive / IA / tests)
        // Déclaration attaquant -> déclaration défenseur -> résolution simultanée.
        // =====================================================================

        /// <summary>
        /// Étape 0 : prépare le duel aveugle. Paie le coût de base d'attaque uniquement.
        /// Aucun jet n'est lancé, aucune mise n'est engagée, rien n'est révélé.
        /// Retourne null + message d'erreur si l'attaque est impossible (PA insuffisants,
        /// ou cible totalement à couvert / non visible, Livre VI §25.3).
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
            CoverType cover = CoverType.None)
        {
            error = null;
            var targetInfo = BodyPartInfo.GetInfo(targetedPart);
            var cfg = Rules.CoreRulesConfig.Instance;

            // Livre VI §25.3 : cible totalement à couvert / non visible => attaque impossible.
            // Au contact, les combattants se voient : le couvert est ignoré.
            CoverType effectiveCover = cover;
            if (_currentCombatIsAtContactDistance && cfg.CoverIgnoredAtContactDistance)
                effectiveCover = CoverType.None;
            if (effectiveCover == CoverType.Full && cfg.FullCoverBlocksAttack)
            {
                error = $"{attacker.Name} ne voit pas {defender.Name} (couvert total) — attaque impossible ! Déplacez-vous pour retrouver une ligne de mire.";
                return null;
            }

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
                DefenderWantsToDefend = defenderWantsToDefend,
                IsRangedAttack = (attackSkill == SkillType.Ballistique || attackSkill == SkillType.ProjectilesTir),
                IsMartialArtsStrike = (attackSkill == SkillType.MainsNues) && attacker.HasSpecialization("Arts Martiaux"),
                DefenderUnreactivePenalty = cfg.UnreactiveDefensePenalty,
                Cover = effectiveCover,
                CoverAttackPenalty = effectiveCover == CoverType.Half ? cfg.HalfCoverAttackPenalty
                    : effectiveCover == CoverType.ThreeQuarters ? cfg.ThreeQuartersCoverAttackPenalty : 0,
                BlockedByCover = false
            };

            // Modificateur de base d'attaque (hors mise déclarée) : états + visée + canon entravé + couvert.
            int baseAtt = attackModifier + duel.CoverAttackPenalty;
            if (duel.IsRangedAttack && !cancelPenaltyWithAP)
            {
                baseAtt += targetInfo.DifficultyModifier;
            }
            if (_currentCombatIsAtContactDistance && duel.IsRangedAttack)
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
            if (!attacker.ConsumeActionPoints(duel.BaseAttackCost))
            {
                error = $"{attacker.Name} n'a pas assez de PA ({attacker.CurrentActionPoints}/{duel.BaseAttackCost}) pour attaquer !";
                return null;
            }

            // Éligibilité du Défenseur SANS prélèvement ni révélation.
            // Sa mise sera engagée à sa déclaration aveugle (DeclareDefenderStakes).
            duel.BaseDefenseCost = cfg.BaseReactionAPCost;
            duel.IsDefenderIncapacitated = !defender.CanDefendActively();
            bool effectiveWants = defenderWantsToDefend && !duel.IsDefenderIncapacitated;
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
            CoverType cover = CoverType.None)
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
                defenderWantsToDefend, defenderArmor, attackSkill, defenseSkill, out error, cover);
        }

        /// <summary>
        /// Étape 1 : DÉCLARATION de l'attaquant. Engage la mise bonus (PA + PE) AVANT tout jet.
        /// 1 PA = +1, 1 PE = +DuelPEBonusPerPoint (plafond Constitution). Mise cachée.
        /// Retourne le bonus total engagé (PA appliqués + PE appliqués x valeur).
        /// </summary>
        public int DeclareAttackerStakes(AttackDuel duel, int bonusPA, int pePoints)
        {
            if (duel == null) throw new ArgumentNullException(nameof(duel));
            if (duel.AttackerStakesDeclared) return CommittedAttackBonus(duel);
            var cfg = Rules.CoreRulesConfig.Instance;
            duel.AttackerBonusPAApplied = SpendBonusPA(duel.Attacker, Math.Max(0, bonusPA));
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
            if (duel.DefenderStakesDeclared) return CommittedDefenseBonus(duel);
            var cfg = Rules.CoreRulesConfig.Instance;

            duel.DefenseSkill = defenseSkill;
            duel.DefenderWantsToDefend = wantsToDefend && !duel.IsDefenderIncapacitated;
            duel.DefenderStakesDeclared = true;
            duel.DefenderBonusPAApplied = 0;
            duel.DefenderPEApplied = 0;
            duel.DefenderBasePaid = false;

            if (!duel.DefenderWantsToDefend)
            {
                // Encaissement passif : 0 PA, 0 PE, aucun jet à la résolution.
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
            duel.DefenseStatusMod = statusMod;
            duel.DefenseBaseMod = statusMod;
            duel.CanDefenderReact = true;

            // Base réaction : débitée à la déclaration ; échec -> réflexe à -2.
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

            duel.DefenderBonusPAApplied = SpendBonusPA(duel.Defender, Math.Max(0, bonusPA));
            duel.DefenderPEApplied = duel.Defender.SpendDuelPE(Math.Max(0, pePoints));
            return CommittedDefenseBonus(duel);
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
            int expectedAttack = (int)Math.Round(DieAverage(duel.AttackDie)) + duel.AttackBaseMod;
            // La mise adverse est cachée : l'estimation l'ignore (0).
            int expectedDefense = (int)Math.Round(DieAverage(duel.DefenseDie)) + duel.DefenseBaseMod;
            int need = expectedAttack - expectedDefense + 1;
            if (need < 0) need = 0;
            int affordable = Math.Max(0, duel.Defender.CurrentActionPoints - duel.BaseDefenseCost);
            int committed = Math.Min(need, affordable);
            return DeclareDefenderStakes(duel, duel.DefenseSkill, true, committed, 0, defenderSpecialization);
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
                DeclareDefenderStakes(duel, duel.DefenseSkill, false, 0, 0);
            }

            var cfg = Rules.CoreRulesConfig.Instance;
            int attCommitted = CommittedAttackBonus(duel);
            int defCommitted = (duel.DefenderWantsToDefend && !duel.IsDefenderIncapacitated && duel.CanDefenderReact && duel.DefenderBasePaid)
                ? CommittedDefenseBonus(duel)
                : 0;

            duel.AttackFinalRoll = _diceRoller.Roll(duel.AttackDie, duel.AttackBaseMod + attCommitted, cfg.StandardTargetDC);

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
                duel.DefenseFinalRoll = _diceRoller.Roll(duel.DefenseDie, duel.DefenseBaseMod + defCommitted, cfg.StandardTargetDC);
            }

            duel.HasResolved = true;
            return ResolveDuelDamage(duel);
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
            int attPA = SpendBonusPA(attacker, Math.Max(0, attackerBonusAP));
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
                    int expectedAtt = (int)Math.Round(DieAverage(attDie)) + attStatus;
                    int expectedDef = (int)Math.Round(DieAverage(defDie)) + defStatus;
                    int need = Math.Max(0, expectedAtt - expectedDef + 1);
                    defPA = SpendBonusPA(defender, Math.Min(need, Math.Max(0, defender.CurrentActionPoints)));
                }
                else
                {
                    defPA = SpendBonusPA(defender, Math.Max(0, defenderBonusAP));
                    defPE = defender.SpendDuelPE(Math.Max(0, defenderPE));
                }
            }

            int attTotal = attStatus + attPA + attPE * cfg.DuelPEBonusPerPoint;
            int defTotal = defStatus + defPA + defPE * cfg.DuelPEBonusPerPoint;

            var attRoll = _diceRoller.Roll(attDie, attTotal, cfg.StandardTargetDC);
            DiceRollResult defRoll = defenderPlays
                ? _diceRoller.Roll(defDie, defTotal, cfg.StandardTargetDC)
                : new DiceRollResult
                {
                    DieType = defDie, RawRoll = 0, Modifier = 0, Total = 0,
                    TargetDC = cfg.StandardTargetDC, Differential = -cfg.StandardTargetDC,
                    IsSuccess = false, IsCriticalSuccess = false, IsCriticalFailure = false
                };

            int differential = attRoll.Total - defRoll.Total;
            string log = $"⚔️ <b>DÉFI OPPOSÉ AVEUGLE</b> : {attacker.Name} [{SkillDefinitions.GetDisplayName(attackSkill)}, mise cachée {attPA} PA + {attPE} PE] vs {defender.Name} [{SkillDefinitions.GetDisplayName(defenseSkill)}, mise cachée {(defenderPlays ? $"{defPA} PA + {defPE} PE" : "passif")}]\n"
                + $"   🎲 <b>RÉVÉLATION SIMULTANÉE :</b> {attRoll.Total} vs {defRoll.Total} ➔ <b>Différentiel {(differential >= 0 ? "+" : "")}{differential}</b> — <b>{(differential >= 0 ? "L'INITIATIVE L'EMPORTE" : "L'OPPOSITION L'EMPORTE")}</b>";

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

        /// <summary>Espérance d'un dé (pour l'estimation aveugle de l'IA, mise adverse inconnue).</summary>
        public static double DieAverage(DiceType die)
        {
            return die switch
            {
                DiceType.D2 => 1.5,
                DiceType.D3 => 2.0,
                DiceType.D4 => 2.5,
                DiceType.D6 => 3.5,
                DiceType.D8 => 4.5,
                DiceType.D10 => 5.5,
                DiceType.D12 => 6.5,
                DiceType.D20 => 10.5,
                DiceType.TwoD6 => 7.0,
                DiceType.TwoD8 => 9.0,
                DiceType.TwoD10 => 11.0,
                DiceType.TwoD12 => 13.0,
                DiceType.TwoD12Plus10 => 23.0,
                _ => 3.5,
            };
        }

        private int CommittedAttackBonus(AttackDuel duel)
        {
            var cfg = Rules.CoreRulesConfig.Instance;
            return duel.AttackerBonusPAApplied + duel.AttackerPEApplied * cfg.DuelPEBonusPerPoint;
        }

        private int CommittedDefenseBonus(AttackDuel duel)
        {
            var cfg = Rules.CoreRulesConfig.Instance;
            return duel.DefenderBonusPAApplied + duel.DefenderPEApplied * cfg.DuelPEBonusPerPoint;
        }

        private static int SpendBonusPA(CharacterStats stats, int requested)
        {
            int applied = 0;
            for (int i = 0; i < requested; i++)
            {
                // 1 PA bonus = +1 au jet ; Ralenti / Dernier Souffle gérés dans ConsumeActionPoints.
                if (stats.ConsumeActionPoints(1)) applied++;
                else break;
            }
            return applied;
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
            CoverType cover = CoverType.None)
        {
            // --- Duel aveugle : base attaque -> mise attaquant (cachée) ->
            // --- mise défenseur à l'aveugle -> révélation simultanée -> résolution normale.
            var duel = BeginDuel(
                attacker, defender, targetedPart,
                attackDie, attackModifier, defenseDie, defenseModifier,
                weaponBaseDamage, cancelPenaltyWithAP, defenderWantsToDefend,
                defenderArmor, attackSkill, defenseSkill, out string error, cover);
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

            DeclareAttackerStakes(duel, attackerBonusAP, attackerPE);
            if (duel.DefenderWantsToDefend && !duel.IsDefenderIncapacitated && duel.CanDefenderReact)
            {
                DeclareDefenderStakes(duel, defenseSkill, true, defenderBonusAP, defenderPE, defenderSpecialization);
            }
            else
            {
                DeclareDefenderStakes(duel, defenseSkill, false, 0, 0, defenderSpecialization);
            }

            return ResolveBlindDuel(duel);
        }

        private DamageResult ResolveDuelDamage(AttackDuel duel)
        {
            var attacker = duel.Attacker;
            var defender = duel.Defender;
            var targetedPart = duel.TargetedPart;
            var attackSkill = duel.AttackSkill;
            var defenseSkill = duel.DefenseSkill;
            var targetInfo = BodyPartInfo.GetInfo(targetedPart);
            var cfg = Rules.CoreRulesConfig.Instance;

            var attackRoll = duel.AttackFinalRoll;
            int attCommittedPA = duel.AttackerBonusPAApplied;
            int attCommittedPE = duel.AttackerPEApplied;
            int defCommittedPA = duel.DefenderBonusPAApplied;
            int defCommittedPE = duel.DefenderPEApplied;

            DiceRollResult defenseRoll;
            int differential;
            string defRollStr;

            bool effectiveDefenderWantsToDefend = duel.DefenderWantsToDefend && !duel.IsDefenderIncapacitated;

            if (!effectiveDefenderWantsToDefend)
            {
                defenseRoll = duel.DefenseFinalRoll;
                differential = attackRoll.Total;
                string inCapReason = duel.IsDefenderIncapacitated
                    ? $"<b>CIBLE HORS D'ÉTAT ({defender.ActiveStatus}) ➔ Parade impossible (0 PA)</b>"
                    : "<b>ENCAISSEMENT PASSIF AVEUGLE (0 PA engagé, aucune parade, jet adverse jamais vu)</b>";
                defRollStr = inCapReason;
            }
            else
            {
                defenseRoll = duel.DefenseFinalRoll;
                differential = attackRoll.Total - defenseRoll.Total;

                string statusDefInfo = defender.GetStatusBreakdownString(defenseSkill, isOffensive: false);

                string defPaText;
                if (duel.CanDefenderReact && duel.DefenderBasePaid)
                {
                    defPaText = $" + Mise {defCommittedPA} PA + {defCommittedPE} PE (aveugle)";
                }
                else
                {
                    defPaText = $" {cfg.UnreactiveDefensePenalty} (0 PA)";
                }
                string critDefText = defenseRoll.IsCriticalSuccess ? " <color=#00E5FF>[CRITIQUE !]</color>" : "";
                string defStatusDetail = !string.IsNullOrEmpty(statusDefInfo) ? $" {statusDefInfo}" : " 0";
                // Les deux jets sont révélés ensemble : aucune injection après tirage.
                defRollStr = $"{SkillDefinitions.GetDisplayName(defenseSkill)} : {duel.DefenseDie} [Tirage {defenseRoll.RawRoll} + États {duel.DefenseStatusMod} ({defStatusDetail}){defPaText} = Total {defenseRoll.Total}]{critDefText}";
            }

            string statusAttInfo = attacker.GetStatusBreakdownString(attackSkill, isOffensive: true);
            string statusAttDetail = !string.IsNullOrEmpty(statusAttInfo) ? statusAttInfo : "0";
            string aimText = duel.CancelPenaltyWithAP ? "Visée compensée (+1 PA base)" : $"Malus Visée {targetInfo.DifficultyModifier}";
            string paAttText = (attCommittedPA > 0 || attCommittedPE > 0) ? $" + Mise {attCommittedPA} PA + {attCommittedPE} PE (aveugle)" : " + Mise 0 (aveugle)";
            string critAttText = attackRoll.IsCriticalSuccess ? " <color=#FFE600>[CRITIQUE !]</color>" : "";
            string entraveText = duel.AppliedCanonEntrave
                ? (duel.CancelPenaltyWithAP ? " [Canon Entravé : Annulé (+1 PA)]" : " [Canon Entravé : -2 Tir]")
                : (duel.IsMartialArtsStrike ? " [Arts Martiaux : Exemption Totale]" : "");
            string coverText = duel.Cover == CoverType.Half
                ? $" [Couvert : moitié visible {duel.CoverAttackPenalty}]"
                : duel.Cover == CoverType.ThreeQuarters
                    ? $" [Couvert : 3/4 couvert {duel.CoverAttackPenalty}]"
                    : "";

            int normalRef = (attacker.Attributes.Force + attacker.Attributes.Agilite + 1) / 2;
            string augText = (attacker.IsMagicAugmented(attackSkill, true)
                    && attacker.Attributes.Magie > normalRef)
                ? $" [Corps Augmenté : MAG {attacker.Attributes.Magie} (+{SkillDefinitions.CharacteristicSteps(attacker.Attributes.Magie)} paliers)]"
                : "";
            string attackRollStr = $"{SkillDefinitions.GetDisplayName(attackSkill)} : {duel.AttackDie} [Tirage {attackRoll.RawRoll} + États {duel.AttackStatusMod} ({statusAttDetail}) + ({aimText}){paAttText}{entraveText}{coverText} = Total {attackRoll.Total}]{augText}{critAttText}";

            // Traçabilité Livre VI §24.1 (duel aveugle) : déclarations masquées puis révélation.
            string phaseAtt = $"Phase 1 — Déclaration attaquant : {attacker.Name} désigne {defender.Name} ({targetInfo.DisplayName}), annonce {SkillDefinitions.GetDisplayName(attackSkill)}, engage {duel.BaseAttackCost} PA + mise cachée {attCommittedPA} PA + {attCommittedPE} PE. Résultat caché.";
            string phaseDef = !effectiveDefenderWantsToDefend
                ? $"Phase 2 — Déclaration défenseur (aveugle) : {defender.Name} encaisse (0 PA, 0 PE), sans voir le jet adverse."
                : ((duel.CanDefenderReact && duel.DefenderBasePaid)
                    ? $"Phase 2 — Déclaration défenseur (aveugle) : {defender.Name} annonce {SkillDefinitions.GetDisplayName(defenseSkill)}, engage 1 PA + mise cachée {defCommittedPA} PA + {defCommittedPE} PE, sans voir le jet adverse."
                    : $"Phase 2 — Déclaration défenseur (aveugle) : {defender.Name} sans réaction (0 PA) → {cfg.UnreactiveDefensePenalty} réflexe.");

            // 4. Résolution du Différentiel
            if (effectiveDefenderWantsToDefend && differential < 0)
            {
                string blockLog = $"🛡️ <b>PARADE / ESQUIVE</b> : {attacker.Name} attaque, mais {defender.Name} neutralise l'assaut !\n";
                blockLog += $"   {phaseAtt}\n";
                blockLog += $"   {phaseDef}\n";
                blockLog += $"   🎲 <b>RÉVÉLATION SIMULTANÉE :</b> Attaque {attackRollStr} vs Défense {defRollStr}\n";
                blockLog += $"   ⚖️ <b>Différentiel Net :</b> <color=#00E5FF>{differential}</color> ➔ <b>0 dégât infligé</b> (Attaque neutralisée).";

                return new DamageResult
                {
                    IsHit = false,
                    IsBlocked = true,
                    AttackRoll = attackRoll,
                    DefenseRoll = defenseRoll,
                    Differential = differential,
                    Cover = duel.Cover,
                    CoverAttackPenalty = duel.CoverAttackPenalty,
                    BlockedByCover = false,
                    CombatLog = blockLog
                };
            }

            bool wasDeflected = effectiveDefenderWantsToDefend && (differential == 0);
            BodyPart actualHitPart = wasDeflected ? GetAdjacentBodyPart(targetedPart) : targetedPart;
            var actualInfo = BodyPartInfo.GetInfo(actualHitPart);

            int rawDamage = duel.WeaponBaseDamage;
            string dmgFormula = $"{duel.WeaponBaseDamage} (Arme)";

            if (differential > 0)
            {
                rawDamage += differential;
                dmgFormula += $" + {differential} (Diff &Delta;)";
            }

            if (attackRoll.IsCriticalSuccess)
            {
                rawDamage *= actualInfo.CriticalDamageMultiplier;
                dmgFormula += $" x{actualInfo.CriticalDamageMultiplier} (Critique {actualInfo.DisplayName})";
            }

            int totalArmor = duel.DefenderArmor + defender.BaseArmorAbsorption;
            int absorbed = Math.Min(totalArmor, rawDamage);
            int finalDamage = Math.Max(0, rawDamage - absorbed);
            int prevHp = defender.CurrentHealth;

            bool exceededEncaissement = finalDamage > defender.EncaissementThreshold;
            StatusEffect inflictedStatus = StatusEffect.None;

            if (exceededEncaissement || attackRoll.IsCriticalSuccess)
            {
                inflictedStatus = DetermineInflictedStatus(actualHitPart);
                defender.ActiveStatus |= inflictedStatus;
            }

            FatalBlowResolution fatalRes = FatalBlowResolution.None;
            if (defender.CurrentHealth - finalDamage <= 0)
            {
                fatalRes = defender.EvaluateFatalBlow(actualHitPart, finalDamage);
            }
            else
            {
                defender.CurrentHealth -= finalDamage;
            }

            string headerTag = !effectiveDefenderWantsToDefend
                ? (duel.IsDefenderIncapacitated ? "💥 <b>FRAPPE SUR CIBLE NEUTRALISÉE</b>" : "🎯 <b>TOUCHÉ SUR CIBLE SANS DÉFENSE</b>")
                : (wasDeflected
                    ? "⚠️ <b>DÉVIATION BALISTIQUE</b>"
                    : (attackRoll.IsCriticalSuccess ? "💥 <b>COUP CRITIQUE CHIRURGICAL</b>" : "🎯 <b>TOUCHÉ CHIRURGICAL</b>"));

            string hitPartStr = wasDeflected
                ? $"Visait <b>{targetInfo.DisplayName}</b> ➔ Dévie sur <b>{actualInfo.DisplayName}</b> (Diff 0)"
                : $"Frappe sur <b>{actualInfo.DisplayName}</b>";

            string log = $"{headerTag} : {attacker.Name} ➔ {defender.Name} ({hitPartStr})\n";
            log += $"   {phaseAtt}\n";
            log += $"   {phaseDef}\n";
            log += $"   🎲 <b>RÉVÉLATION SIMULTANÉE :</b> Attaque {attackRollStr} vs Défense {defRollStr} ➔ <b>Différentiel Net : <color=#00E5FF>{(differential > 0 ? $"+{differential}" : "0")}</color></b>\n";
            log += $"   ⚔️ <b>Dégâts :</b> [{dmgFormula} = {rawDamage} Bruts] &minus; [Armure {totalArmor} (Absorbé: {absorbed})] ➔ <b><color=#FF3B5C>{finalDamage} Dégâts Nets</color></b>\n";
            log += $"   ❤️ <b>Vitalité {defender.Name} :</b> {prevHp} ➔ <b>{defender.CurrentHealth}/{defender.MaxHealth} PV</b>";

            if (exceededEncaissement)
            {
                log += $" | ⚡ <b>CHOC TRAUMATIQUE</b> (&gt; Encaissement {defender.EncaissementThreshold}) ➔ [{inflictedStatus}]";
            }

            if (fatalRes == FatalBlowResolution.InstantDeath)
            {
                log += " | 💀 <b>MORT INSTANTANÉE (Tête détruite)</b>";
            }
            else if (fatalRes == FatalBlowResolution.MiracleSaved)
            {
                log += " | 🔮 <b>POINT DE MIRACLE DÉPENSÉ (Survie à 1 PV)</b>";
            }
            else if (fatalRes == FatalBlowResolution.ForcedUnconscious)
            {
                log += " | 💥 <b>SYNCOPE TRAUMATIQUE (K.O.)</b>";
            }
            else if (fatalRes == FatalBlowResolution.EligibleForLastBreath)
            {
                log += " | ⚡ <b>0 PV (Choix Sombrer ou Dernier Souffle)</b>";
            }

            bool causedKnockback = duel.IsMartialArtsStrike && (exceededEncaissement || attackRoll.IsCriticalSuccess || differential >= 4);

            return new DamageResult
            {
                IsHit = true,
                IsBlocked = false,
                IsCritical = attackRoll.IsCriticalSuccess,
                AttackRoll = attackRoll,
                DefenseRoll = defenseRoll,
                TargetPart = targetedPart,
                ActualHitPart = actualHitPart,
                WasDeflected = wasDeflected,
                Differential = differential,
                RawDamage = rawDamage,
                ArmorAbsorbed = absorbed,
                FinalDamageApplied = finalDamage,
                ExceededEncaissement = exceededEncaissement,
                InflictedStatus = inflictedStatus,
                FatalResolution = fatalRes,
                CausedKnockback = causedKnockback,
                IsCanonEntrave = duel.AppliedCanonEntrave,
                Cover = duel.Cover,
                CoverAttackPenalty = duel.CoverAttackPenalty,
                BlockedByCover = false,
                CombatLog = log
            };
        }

        private BodyPart GetAdjacentBodyPart(BodyPart targeted)
        {
            return targeted switch
            {
                BodyPart.Tete or BodyPart.YeuxVisage => _random.Next(0, 2) == 0 ? BodyPart.CouTrachee : BodyPart.Torse,
                BodyPart.CouTrachee => _random.Next(0, 2) == 0 ? BodyPart.BrasDroit : BodyPart.Torse,
                BodyPart.CoeurPoumons => BodyPart.Torse,
                BodyPart.Torse => _random.Next(0, 2) == 0 ? BodyPart.BrasGauche : BodyPart.Jambes,
                BodyPart.BrasDroit or BodyPart.BrasGauche => BodyPart.Torse,
                BodyPart.Jambes => BodyPart.Torse,
                _ => BodyPart.Torse
            };
        }

        private StatusEffect DetermineInflictedStatus(BodyPart hitPart)
        {
            return hitPart switch
            {
                BodyPart.Tete => StatusEffect.Sonne | StatusEffect.Etourdi,
                BodyPart.YeuxVisage => StatusEffect.Aveugle | StatusEffect.Saignement,
                BodyPart.CouTrachee => StatusEffect.Asphyxie,
                BodyPart.CoeurPoumons => StatusEffect.Agonisant | StatusEffect.Saignement,
                BodyPart.Torse => StatusEffect.Destabilise,
                BodyPart.BrasDroit or BodyPart.BrasGauche => StatusEffect.Destabilise,
                BodyPart.Jambes => StatusEffect.ATerre | StatusEffect.Ralenti,
                _ => StatusEffect.Destabilise
            };
        }
    }
}

using System;
using Killtime.Core.Character;
using Killtime.Core.Dice;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Visibility;

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
        // RD-049 : Furtivité / Embuscade (+2 attaque, pas de réaction adverse)
        public bool IsStealthAttack;
        public int StealthAttackBonus;
        // Bonus d'arme arcanotech au jet (ex: Deglazer +1ec, Livre VIII §31.2).
        // Additionné à AttackBaseMod au BeginDuel, tracé dans le log.
        public int WeaponBonusEc;
        // RD-036 Hauteur / contrebas (palier 1.5 m) : bonus/malus vertical
        // calculé par ElevationAdvantage, additionné à AttackBaseMod.
        public int ElevationAttackMod;
        public string ElevationLabel;
        // Couvert & visibilité (Livre VI §25.3) : Half -1, ThreeQuarters -2,
        // Full = cible non visible => attaque impossible (BeginDuel refuse).
        public CoverType Cover;
        public int CoverAttackPenalty;
        public bool BlockedByCover;

        // RD-053 : Nuit / Obscurité (-1 tir sans lampe ni thermique)
        public bool IsNightAttack;
        public int NightAttackMod;

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

            bool isStealthAttack = StealthState.IsStealthed(attacker);
            int stealthBonus = isStealthAttack ? StealthState.StealthAttackBonus : 0;

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
                IsNightAttack = effectiveNight,
                NightAttackMod = nightMod
            };

            // Modificateur de base d'attaque (hors mise déclarée) : états + visée + canon entravé + couvert + bonus arme EC + hauteur RD-036 + stealth RD-049 + nuit RD-053.
            int baseAtt = attackModifier + duel.CoverAttackPenalty + Math.Max(0, weaponBonusEc) + elevationAttackMod + stealthBonus + nightMod;
            duel.WeaponBonusEc = Math.Max(0, weaponBonusEc);
            duel.ElevationAttackMod = elevationAttackMod;
            duel.ElevationLabel = elevationLabel ?? "";
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

        // =====================================================================
        // RD-030 : GUET / OVERWATCH — tir de réaction en duel aveugle auto.
        // Le coût d'entrée (2 PA) est payé à la pose : le tir est GRATUIT
        // (skipAttackerCost). Mise attaquant 0/0 (tir réflexe), défense auto.
        // =====================================================================

        /// <summary>
        /// Entre en guet : réserve un tir de réaction (coût config, rayon figé).
        /// </summary>
        public bool TryEnterOverwatch(CharacterStats watcher, int range, out string error)
        {
            var cfg = Rules.CoreRulesConfig.Instance;
            return OverwatchState.TryEnter(watcher, range, cfg.OverwatchAPCost, out error, cfg.OverwatchShotsPerWatch);
        }

        /// <summary>
        /// Tir de réaction du guetteur sur une cible dans son rayon : duel aveugle
        /// automatique (Ballistique vs Esquive, mises 0, défense auto), puis le guet
        /// est consommé (1 tir). Retourne null si le guet n'est pas valide.
        /// </summary>
        public DamageResult? ResolveOverwatchFire(
            CharacterStats watcher,
            CharacterStats target,
            int weaponBaseDamage,
            CoverType cover = CoverType.None,
            int weaponBonusEc = 0,
            BodyPart targetedPart = BodyPart.Torse,
            SkillType attackSkill = SkillType.Ballistique,
            int elevationAttackMod = 0,
            string elevationLabel = null)
        {
            if (watcher == null || target == null) return null;
            if (!OverwatchState.IsWatching(watcher)) return null;
            if (!target.IsAlive) return null;
            if (attackSkill != SkillType.Ballistique && attackSkill != SkillType.ProjectilesTir)
                attackSkill = SkillType.Ballistique;

            var duel = BeginSkillDuel(
                watcher, target, targetedPart,
                attackSkill, SkillType.Esquive,
                weaponBaseDamage, false, true,
                null, null, target.BaseArmorAbsorption,
                out _, cover, weaponBonusEc, true, elevationAttackMod, elevationLabel);
            if (duel == null) return null;

            DeclareAttackerStakes(duel, 0, 0);
            AutoDeclareDefenderStakes(duel);
            var result = ResolveBlindDuel(duel);
            OverwatchState.ConsumeShot(watcher);
            result.CombatLog = $"👁️ <b>GUET — tir de réaction</b> : {watcher.Name} ➔ {target.Name} (rayon réservé, duel aveugle auto)\n" + result.CombatLog;
            return result;
        }

        // =====================================================================
        // RD-031 : OPPORTUNITÉ / DÉSENGAGEMENT — réactions au contact rompu.
        // La frappe et le balayage sont GRATUITS (skipAttackerCost, réaction seule
        // consommée par l'appelant via OpportunityState). Mise attaquant 0/0
        // (coup réflexe), défense auto. Le blocage est un défi opposé Athlétisme
        // vs Athlétisme (Livre VI §25.1, course-poursuite) : à l'attaquant (bloqueur).
        // =====================================================================

        /// <summary>
        /// Choisit la compétence de mêlée du frappeur : arme de contact équipée
        /// (compétence associée rabattue) ou Mains Nues à défaut.
        /// </summary>
        public static SkillType ResolveOpportunitySkill(CharacterStats reactor)
        {
            if (reactor != null && reactor.Sheet != null)
            {
                var weapon = reactor.Sheet.GetEquippedWeapon();
                // Arme de contact : compétence associée. Arme longue / à distance :
                // coup de crosse d'urgence résolu à Mains Nues (V1).
                if (weapon != null && weapon.RangeInTiles <= 1 && weapon.RangeInTiles > 0)
                    return SkillDefinitions.ResolveBaseSkill(weapon.AssociatedSkill);
            }
            return SkillType.MainsNues;
        }

        /// <summary>
        /// Frappe d'opportunité : quitte le contact ⇒ 1 frappe gratuite de mêlée
        /// en duel aveugle auto (mises 0, défense auto). Ne consomme que la réaction
        /// (appelant : OpportunityState.TryConsumeReaction). Retourne null si invalide.
        /// </summary>
        public DamageResult? ResolveOpportunityStrike(
            CharacterStats reactor,
            CharacterStats target,
            int weaponBaseDamage,
            SkillType attackSkill,
            BodyPart targetedPart = BodyPart.Torse,
            int elevationAttackMod = 0,
            string elevationLabel = null)
        {
            if (reactor == null || target == null) return null;
            if (!reactor.IsAlive || !target.IsAlive) return null;
            if (!SkillDefinitions.IsMeleeAttackSkill(SkillDefinitions.ResolveBaseSkill(attackSkill)))
                attackSkill = SkillType.MainsNues;

            var duel = BeginSkillDuel(
                reactor, target, targetedPart,
                attackSkill, SkillType.Esquive,
                weaponBaseDamage, false, true,
                null, null, target.BaseArmorAbsorption,
                out _, CoverType.None, 0, true, elevationAttackMod, elevationLabel);
            if (duel == null) return null;

            DeclareAttackerStakes(duel, 0, 0);
            AutoDeclareDefenderStakes(duel);
            var result = ResolveBlindDuel(duel);
            result.CombatLog = $"⚔️ <b>OPPORTUNITÉ — frappe</b> : {reactor.Name} ➔ {target.Name} (contact rompu, duel aveugle auto, gratuit)\n" + result.CombatLog;
            return result;
        }

        /// <summary>
        /// Balayage d'opportunité (variante) : frappe gratuite visant les Jambes
        /// (À Terre + Ralenti en cas de choc, Livre VI §26). Même coût qu'une frappe.
        /// </summary>
        public DamageResult? ResolveOpportunityTrip(
            CharacterStats reactor,
            CharacterStats target,
            int weaponBaseDamage,
            SkillType attackSkill,
            int elevationAttackMod = 0,
            string elevationLabel = null)
        {
            var result = ResolveOpportunityStrike(reactor, target, weaponBaseDamage, attackSkill, BodyPart.Jambes, elevationAttackMod, elevationLabel);
            if (result.HasValue)
            {
                var r = result.Value;
                r.CombatLog = r.CombatLog.Replace(
                    "⚔️ <b>OPPORTUNITÉ — frappe</b>",
                    "🦵 <b>OPPORTUNITÉ — balayage</b>");
                return r;
            }
            return result;
        }

        /// <summary>
        /// Blocage d'opportunité : duel opposé aveugle Athlétisme (bloqueur) vs
        /// Athlétisme (fuyard), mises 0, défense auto. Le coût de réaction (1 PA
        /// config) est payé par l'appelant AVANT l'appel. Victoire du bloqueur
        /// (différentiel ≥ 0) = le pas est annulé, sans grapple : le fuyard reste
        /// sur sa case et ses PA de déplacement sont remboursés.
        /// </summary>
        public OpposedCheckResult ResolveOpportunityBlock(
            CharacterStats blocker,
            CharacterStats mover)
        {
            var opposed = ResolveOpposedCheck(
                blocker, SkillType.Athletisme,
                mover, SkillType.Athletisme,
                attackerBonusAP: 0, attackerPE: 0,
                defenderAutoStakes: true);
            opposed.CombatLog = $"🛡️ <b>OPPORTUNITÉ — blocage</b> : {blocker.Name} retient {mover.Name} (Athlétisme vs Athlétisme, aveugle) — "
                + (opposed.AttackerWins ? "<b>BLOQUÉ</b>, le pas est annulé." : "<b>ESQUIVÉ</b>, le fuyard passe.")
                + "\n" + opposed.CombatLog;
            return opposed;
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
            var duel = BeginSkillDuel(
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
            DeclareAttackerStakes(duel, attackerBonusAP, attackerPE);
            AutoDeclareDefenderStakes(duel);
            var result = ResolveBlindDuel(duel);
            result.CombatLog = $"🛡️ <b>[INTERCEPTION] {protector.Name}</b> prend le coup à la place !\n" + result.CombatLog;
            return result;
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

            // Livre I §5 : le gagnant du différentiel coche 1 case de progression.
            log += ApplyOpposedProgression(attacker, attackSkill, true, defender, defenseSkill, defenderPlays, differential);

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

            var opposed = ResolveOpposedCheck(
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

            var opposed = ResolveOpposedCheck(
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

            var opposed = ResolveOpposedCheck(
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

            var opposed = ResolveOpposedCheck(
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
                // Mises exemptées du malus armures (ce sont des modificateurs, pas des actions).
                if (stats.ConsumeActionPoints(1, true)) applied++;
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
            CoverType cover = CoverType.None,
            int weaponBonusEc = 0,
            bool isNight = false)
        {
            // --- Duel aveugle : base attaque -> mise attaquant (cachée) ->
            // --- mise défenseur à l'aveugle -> révélation simultanée -> résolution normale.
            var duel = BeginDuel(
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
            // RD-038 Charge 3+ cases : consommation de l'élan cinétique sur cette frappe de contact
            bool isMeleeAttackForCharge = SkillDefinitions.IsMeleeAttackSkill(duel.AttackSkill) || duel.AttackSkill == SkillType.MainsNues;
            bool chargeApplied = isMeleeAttackForCharge && ChargeState.ConsumeChargeBonus(attacker);

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
                string inCapReason = duel.IsStealthAttack
                    ? "<b>EMBUSCADE DEPUIS LE STEALTH (Cible surprise : 0 PA, aucune réaction adverse)</b>"
                    : (duel.IsDefenderIncapacitated
                        ? $"<b>CIBLE HORS D'ÉTAT ({defender.ActiveStatus}) ➔ Parade impossible (0 PA)</b>"
                        : "<b>ENCAISSEMENT PASSIF AVEUGLE (0 PA engagé, aucune parade, jet adverse jamais vu)</b>");
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
                if (ChargeState.HasDefensePenalty(defender))
                {
                    defStatusDetail += $" [Malus Charge : {cfg.ChargeDefensePenalty} Déf]";
                }
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
            string ecText = duel.WeaponBonusEc > 0 ? $" [+{duel.WeaponBonusEc}ec arme]" : "";
            string elevationText = duel.ElevationAttackMod != 0 ? $" [Hauteur {duel.ElevationAttackMod:+0;-0} {duel.ElevationLabel}]" : "";
            string stealthText = duel.IsStealthAttack ? $" [🥷 Embuscade/Stealth : +{duel.StealthAttackBonus}]" : "";
            string nightText = duel.NightAttackMod != 0 ? $" [Nuit : {duel.NightAttackMod} Tir (sans lampe/thermique)]" : "";

            int normalRef = (attacker.Attributes.Force + attacker.Attributes.Agilite + 1) / 2;
            string augText = (attacker.IsMagicAugmented(attackSkill, true)
                    && attacker.Attributes.Magie > normalRef)
                ? $" [Corps Augmenté : MAG {attacker.Attributes.Magie} (+{SkillDefinitions.CharacteristicSteps(attacker.Attributes.Magie)} paliers)]"
                : "";
            string attackRollStr = $"{SkillDefinitions.GetDisplayName(attackSkill)} : {duel.AttackDie} [Tirage {attackRoll.RawRoll} + États {duel.AttackStatusMod} ({statusAttDetail}) + ({aimText}){paAttText}{entraveText}{coverText}{ecText}{elevationText}{stealthText}{nightText} = Total {attackRoll.Total}]{augText}{critAttText}";

            // Traçabilité Livre VI §24.1 (duel aveugle) : déclarations masquées puis révélation.
            string phaseAtt = $"Phase 1 — Déclaration attaquant : {attacker.Name} désigne {defender.Name} ({targetInfo.DisplayName}), annonce {SkillDefinitions.GetDisplayName(attackSkill)}, engage {duel.BaseAttackCost} PA + mise cachée {attCommittedPA} PA + {attCommittedPE} PE. Résultat caché.";
            string phaseDef = duel.IsStealthAttack
                ? $"Phase 2 — Déclaration défenseur (surpris) : {defender.Name} subit l'embuscade depuis le stealth (aucune réaction adverse possible)."
                : (!effectiveDefenderWantsToDefend
                    ? $"Phase 2 — Déclaration défenseur (aveugle) : {defender.Name} encaisse (0 PA, 0 PE), sans voir le jet adverse."
                    : ((duel.CanDefenderReact && duel.DefenderBasePaid)
                        ? $"Phase 2 — Déclaration défenseur (aveugle) : {defender.Name} annonce {SkillDefinitions.GetDisplayName(defenseSkill)}, engage 1 PA + mise cachée {defCommittedPA} PA + {defCommittedPE} PE, sans voir le jet adverse{(ChargeState.HasDefensePenalty(defender) ? " [Malus Charge -1]" : "")}."
                        : $"Phase 2 — Déclaration défenseur (aveugle) : {defender.Name} sans réaction (0 PA) → {cfg.UnreactiveDefensePenalty} réflexe{(ChargeState.HasDefensePenalty(defender) ? " [Malus Charge -1]" : "")}."));

            // 4. Résolution du Différentiel
            if (effectiveDefenderWantsToDefend && differential < 0)
            {
                string blockLog = $"🛡️ <b>PARADE / ESQUIVE</b> : {attacker.Name} attaque, mais {defender.Name} neutralise l'assaut !\n";
                blockLog += $"   {phaseAtt}\n";
                blockLog += $"   {phaseDef}\n";
                blockLog += $"   🎲 <b>RÉVÉLATION SIMULTANÉE :</b> Attaque {attackRollStr} vs Défense {defRollStr}\n";
                blockLog += $"   ⚖️ <b>Différentiel Net :</b> <color=#00E5FF>{differential}</color> ➔ <b>0 dégât infligé</b> (Attaque neutralisée).";

                bool defHasCrit = defenseRoll.IsCriticalSuccess;
                CriticalConsequence defCrit = default;
                if (defHasCrit)
                {
                    defCrit = _diceRoller.RollCriticalConsequence(duel.DefenseDie, CriticalEffectCategory.Defensive);
                    blockLog += $"\n   ✨ <b>[CONSÉQUENCE CRITIQUE DÉFENSIVE §8.2]</b> Relance {duel.DefenseDie} ➔ <b>{defCrit.RawRoll}</b> (Palier {defCrit.Threshold} : <i>{defCrit.Title}</i>) — {defCrit.Description}";
                    ApplyDefensiveCriticalEffect(defCrit, defender, attacker);
                }

                // Livre I §5 : le défenseur a RÉUSSI sa compétence → +1 case de progression.
                blockLog += ApplySkillProgression(defender, defenseSkill, false);

                // RD-049 : toute attaque rompt la furtivité
                StealthState.BreakStealth(attacker);

                return new DamageResult
                {
                    IsHit = false,
                    IsBlocked = true,
                    IsCritical = defHasCrit,
                    HasCriticalConsequence = defHasCrit,
                    CriticalConsequence = defCrit,
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
            int deflectionRoll = 0;
            string deflectionMedical = null;
            BodyPart actualHitPart = targetedPart;
            if (wasDeflected)
            {
                actualHitPart = ResolveAnatomicalDeflection(targetedPart, out deflectionRoll, out deflectionMedical);
            }
            var actualInfo = BodyPartInfo.GetInfo(actualHitPart);

            int rawDamage = duel.WeaponBaseDamage;
            string dmgFormula = $"{duel.WeaponBaseDamage} (Arme)";

            if (differential > 0)
            {
                rawDamage += differential;
                dmgFormula += $" + {differential} (Diff &Delta;)";
            }

            CriticalConsequence attCrit = default;
            bool attHasCrit = attackRoll.IsCriticalSuccess;
            if (attHasCrit)
            {
                rawDamage *= actualInfo.CriticalDamageMultiplier;
                dmgFormula += $" x{actualInfo.CriticalDamageMultiplier} (Critique {actualInfo.DisplayName})";
                attCrit = _diceRoller.RollCriticalConsequence(duel.AttackDie, CriticalEffectCategory.Offensive);
                if (attCrit.Threshold == 3 || attCrit.Threshold == 4)
                {
                    rawDamage += 1;
                    dmgFormula += $" + 1 ({attCrit.Title} §8.2)";
                }
                else if (attCrit.Threshold == 5)
                {
                    rawDamage += 2;
                    dmgFormula += $" + 2 ({attCrit.Title} §8.2)";
                }
            }

            int totalArmor = duel.DefenderArmor + defender.BaseArmorAbsorption + defender.GetWornArmorBonus();
            if (attacker.HasSpecialization("Leviers Densifiés : Pointe Cristalline") && duel.AttackSkill == SkillType.MainsNues)
            {
                totalArmor = Math.Max(0, totalArmor - 3);
            }
            else if (attacker.HasSpecialization("Leviers Densifiés : Perforation Osseuse") && duel.AttackSkill == SkillType.MainsNues)
            {
                totalArmor = Math.Max(0, totalArmor - 2);
            }
            else if (attacker.HasSpecialization("Leviers Densifiés") && duel.AttackSkill == SkillType.MainsNues)
            {
                totalArmor = Math.Max(0, totalArmor - 1);
            }

            if (attacker.HasSpecialization("Perforation de Silicate : Cœur d'Adamas") && duel.AttackSkill == SkillType.MainsNues)
            {
                totalArmor = Math.Max(0, totalArmor - 4);
            }
            else if (attacker.HasSpecialization("Perforation de Silicate : Fracture Sismique") && duel.AttackSkill == SkillType.MainsNues)
            {
                totalArmor = Math.Max(0, totalArmor - 3);
            }
            else if (attacker.HasSpecialization("Perforation de Silicate") && duel.AttackSkill == SkillType.MainsNues)
            {
                totalArmor = Math.Max(0, totalArmor - 2);
            }

            // Arts Martiaux : Brise-Blindage (Livre III) — impact focalisé
            // destructeur d'alliages : les attaques à mains nues ignorent 2 armure.
            if (attacker.HasSpecialization("Arts Martiaux : Brise-Blindage") && duel.AttackSkill == SkillType.MainsNues)
            {
                totalArmor = Math.Max(0, totalArmor - 2);
            }

            if (attacker.HasSpecialization("Fente de Rupture : Transpercement Traversant") && duel.AttackSkill == SkillType.ManiementArmes)
            {
                totalArmor = Math.Max(0, totalArmor - 4);
            }
            else if (attacker.HasSpecialization("Fente de Rupture : Pointe Chirurgicale") && duel.AttackSkill == SkillType.ManiementArmes)
            {
                totalArmor = Math.Max(0, totalArmor - 3);
            }
            else if (attacker.HasSpecialization("Fente de Rupture") && duel.AttackSkill == SkillType.ManiementArmes)
            {
                totalArmor = Math.Max(0, totalArmor - 2);
            }

            // Arme de Hast : perforation à l'estoc (Armes Perçantes).
            if (attacker.HasSpecialization("Arme de Hast : Phalange d'Acier") && duel.AttackSkill == SkillType.ArmesPercantes)
            {
                totalArmor = Math.Max(0, totalArmor - 4);
            }
            else if (attacker.HasSpecialization("Arme de Hast : Mur de Piques") && duel.AttackSkill == SkillType.ArmesPercantes)
            {
                totalArmor = Math.Max(0, totalArmor - 3);
            }
            else if (attacker.HasSpecialization("Arme de Hast : Arrêt de Charge") && duel.AttackSkill == SkillType.ArmesPercantes)
            {
                totalArmor = Math.Max(0, totalArmor - 2);
            }

            // Fiche héroïque : Avatar (voir MinaCharacter.GetAvatarDamageBonus).
            rawDamage += MinaCharacter.GetAvatarDamageBonus(attacker, duel.AttackSkill);

            // Marteau de Guerre et Hache de Guerre : branches du Maniement d'Arme.
            // La valeur legacy ArmesContondantes est rabattue sur ManiementArmes.
            SkillType meleeSkill = SkillDefinitions.ResolveBaseSkill(duel.AttackSkill);

            if (attacker.HasSpecialization("Corps Augmenté : Résonance Cinétique Pure") && duel.AttackSkill == SkillType.MainsNues)
            {
                rawDamage += 2;
                dmgFormula += " + 2 (Résonance Cinétique)";
            }

            if (attacker.HasSpecialization("Impact de Bedrock : Enclume Mortelle") && meleeSkill == SkillType.ManiementArmes)
            {
                totalArmor = 0;
                rawDamage += 6;
                dmgFormula += " + 6 (Enclume Mortelle, Armure Ignorée)";
            }

            if (attacker.HasSpecialization("Marteau de Guerre : Cataclysme de Fer") && meleeSkill == SkillType.ManiementArmes)
            {
                rawDamage += 5;
            }
            else if (attacker.HasSpecialization("Marteau de Guerre : Brise-Crâne") && meleeSkill == SkillType.ManiementArmes)
            {
                rawDamage += 4;
            }
            else if (attacker.HasSpecialization("Marteau de Guerre : Écrasement Osseux") && meleeSkill == SkillType.ManiementArmes)
            {
                rawDamage += 3;
            }
            else if (attacker.HasSpecialization("Marteau de Guerre") && meleeSkill == SkillType.ManiementArmes)
            {
                rawDamage += 2;
            }

            if (attacker.HasSpecialization("Hache de Guerre : Exécution du Bourreau") && meleeSkill == SkillType.ManiementArmes)
            {
                rawDamage += 5;
            }
            else if (attacker.HasSpecialization("Hache de Guerre : Brise-Garde") && meleeSkill == SkillType.ManiementArmes)
            {
                rawDamage += 4;
            }
            else if (attacker.HasSpecialization("Hache de Guerre : Fente du Bûcheron") && meleeSkill == SkillType.ManiementArmes)
            {
                rawDamage += 3;
            }
            else if (attacker.HasSpecialization("Hache de Guerre") && meleeSkill == SkillType.ManiementArmes)
            {
                rawDamage += 2;
            }

            // RD-038 Charge 3+ cases : +2 dégâts sur la frappe de contact (mêlée / mains nues)
            if (chargeApplied)
            {
                int chargeBonus = cfg.ChargeDamageBonus;
                rawDamage += chargeBonus;
                dmgFormula += $" + {chargeBonus} (⚡ Charge)";
            }

            bool isThomasSymbioticImmune = ThomasCharacter.IsImmuneToCasterMagic(defender.Sheet, attacker.Sheet);
            if (isThomasSymbioticImmune)
            {
                rawDamage = 0;
                totalArmor = 0;
            }

            int absorbed = Math.Min(totalArmor, rawDamage);
            int finalDamage = isThomasSymbioticImmune ? 0 : Math.Max(0, rawDamage - absorbed);
            // Champs de force portés (Livre VIII §32.2) : la barrière absorbe avant la
            // chair — aucun choc traumatique tant qu'elle encaisse tout.
            int shieldRemainder = defender.AbsorbShield(finalDamage);
            int shieldAbsorbed = Math.Max(0, finalDamage - shieldRemainder);
            finalDamage = Math.Max(0, shieldRemainder);
            int prevHp = defender.CurrentHealth;

            bool exceededEncaissement = finalDamage > defender.EncaissementThreshold;
            StatusEffect inflictedStatus = StatusEffect.None;
            bool failleExploitee = false;

            // Arts Martiaux : Rupture Ligamentaire (Livre III) — la cible doit être
            // Déstabilisée AVANT ce coup (le statut infligé par ce même coup ne compte pas).
            bool wasDestabilisedBefore = (defender.ActiveStatus & StatusEffect.Destabilise) != 0;

            if (!isThomasSymbioticImmune && (exceededEncaissement || attackRoll.IsCriticalSuccess))
            {
                inflictedStatus = DetermineInflictedStatus(actualHitPart);
                defender.ActiveStatus |= inflictedStatus;
            }

            bool critCausedKnockback = false;
            FatalBlowResolution critFatalOverride = FatalBlowResolution.None;
            if (!isThomasSymbioticImmune && attHasCrit)
            {
                ApplyOffensiveCriticalEffect(attCrit, attacker, defender, ref inflictedStatus, ref critCausedKnockback, ref critFatalOverride);
            }

            // Analyse de Faille (Livre III) : la faille exposée est consommée à la
            // touche — la prochaine attaque ignore le seuil d'encaissement adverse.
            if (!isThomasSymbioticImmune && !exceededEncaissement && SkillTechniqueState.ConsumeExposedFlaw(defender))
            {
                exceededEncaissement = true;
                inflictedStatus |= DetermineInflictedStatus(actualHitPart);
                defender.ActiveStatus |= inflictedStatus;
                failleExploitee = true;
            }

            // Arts Martiaux : Balayage Rotatif (Livre III) — sur différentiel net
            // Delta >= 2 à mains nues, la cible chute À Terre (même sans choc).
            if (!isThomasSymbioticImmune && duel.AttackSkill == SkillType.MainsNues && differential >= 2
                && attacker.HasSpecialization("Arts Martiaux : Balayage Rotatif"))
            {
                inflictedStatus |= StatusEffect.ATerre;
                defender.ActiveStatus |= StatusEffect.ATerre;
            }

            // Thomas-0 : Impact de Bedrock — fracture d'armure de -2 et Sonné sur Delta >= 2
            if (differential >= 2 && attacker.HasSpecialization("Impact de Bedrock") && meleeSkill == SkillType.ManiementArmes)
            {
                defender.BaseArmorAbsorption = Math.Max(0, defender.BaseArmorAbsorption - 2);
                inflictedStatus |= StatusEffect.Sonne;
                defender.ApplyStatus(StatusEffect.Sonne, 1);
            }

            // Mina-0 : Floraison Cinétique — ronces réflexes sur engagement d'essoufflement au contact
            if (defender.HasSpecialization("Éveil Chlorophyllien : Floraison Cinétique") && defCommittedPE > 0 && _currentCombatIsAtContactDistance)
            {
                int thorns = defCommittedPE * 3;
                attacker.CurrentHealth = Math.Max(0, attacker.CurrentHealth - thorns);
            }

            // Arts Martiaux : Rupture Ligamentaire (Livre III) — cible Déstabilisée
            // avant le coup → torsion destructrice : Paralysé pendant 1 tour.
            if (!isThomasSymbioticImmune && duel.AttackSkill == SkillType.MainsNues && wasDestabilisedBefore
                && attacker.HasSpecialization("Arts Martiaux : Rupture Ligamentaire"))
            {
                inflictedStatus |= StatusEffect.Paralyse;
                defender.ApplyStatus(StatusEffect.Paralyse, 1);
            }

            FatalBlowResolution fatalRes = FatalBlowResolution.None;
            bool wasDisintegrated = false;

            if (defender.CurrentHealth - finalDamage <= 0)
            {
                fatalRes = defender.EvaluateFatalBlow(actualHitPart, finalDamage);
            }
            else
            {
                defender.CurrentHealth -= finalDamage;
            }

            // Palier 24 (Cible Inconsciente) : K.O. immédiat si le coup n'est pas déjà mortel ou miraculé
            if (critFatalOverride == FatalBlowResolution.ForcedUnconscious && fatalRes == FatalBlowResolution.None)
            {
                fatalRes = FatalBlowResolution.ForcedUnconscious;
                defender.CurrentHealth = 0;
            }

            // =====================================================================
            // RD-082 (Livre VII §28.4) : DÉCLENCHEMENT DES EFFETS SUR MARGE ÉLEVÉE
            // =====================================================================
            bool bleedApplied = false;
            int bleedDoT = 0;
            bool fireApplied = false;
            int fireTurns = 0;
            bool stunApplied = false;

            if (!isThomasSymbioticImmune && differential > 0)
            {
                // 4) Désintégrer sur Diff >= 20 et Spé Destruction 6 (pulvérisation totale prioritaire)
                if (differential >= 20 && HasDestructionTier6(attacker))
                {
                    defender.CurrentHealth = 0;
                    defender.IsDead = true;
                    defender.ActiveStatus |= StatusEffect.Inconscient;
                    fatalRes = FatalBlowResolution.InstantDeath;
                    wasDisintegrated = true;
                }
                else if (!defender.IsDead)
                {
                    var equippedWep = attacker.Sheet?.GetEquippedWeapon();

                    // 1) Saigner sur Diff >= 5 avec arme tranchante / perçante (DoT résiduel = rang d'entraînement)
                    if (differential >= 5 && IsSlashingOrPiercingAttack(duel.AttackSkill, equippedWep, attacker))
                    {
                        int trainingRank = attacker.Sheet != null ? attacker.Sheet.GetSkill(duel.AttackSkill).TrainingLevel : 0;
                        bleedDoT = Math.Max(1, trainingRank);
                        defender.ApplyResidualDamage(StatusEffect.Saignement, bleedDoT);
                        inflictedStatus |= StatusEffect.Saignement;
                        bleedApplied = true;
                    }

                    // 2) Brûler sur Diff >= 5 avec feu / plasma (EnFeu 1-3 tours)
                    if (differential >= 5 && IsFireOrPlasmaAttack(duel.AttackSkill, equippedWep, attacker))
                    {
                        fireTurns = Math.Clamp(1 + (differential - 5) / 3, 1, 3);
                        defender.ApplyStatus(StatusEffect.EnFeu, fireTurns, residualDamage: 2 * fireTurns);
                        inflictedStatus |= StatusEffect.EnFeu;
                        fireApplied = true;
                    }

                    // 3) Assommer sur Diff >= 10 et visée crânienne (Inconscient 1 tour direct)
                    bool isCranialTarget = (targetedPart == BodyPart.Tete || targetedPart == BodyPart.YeuxVisage
                        || actualHitPart == BodyPart.Tete || actualHitPart == BodyPart.YeuxVisage);
                    if (differential >= 10 && isCranialTarget)
                    {
                        defender.ApplyStatus(StatusEffect.Inconscient, 1);
                        inflictedStatus |= StatusEffect.Inconscient;
                        if (fatalRes == FatalBlowResolution.None)
                        {
                            fatalRes = FatalBlowResolution.ForcedUnconscious;
                        }
                        stunApplied = true;
                    }
                }
            }

            // RD-047 : Test de moral au seuil critique de 28% PV
            MoraleCheckResult moraleRes = default;
            if (!wasDisintegrated && defender.IsAlive && !defender.HasTestedMorale && defender.CurrentHealth > 0)
            {
                moraleRes = defender.CheckMoraleAtThreshold(_diceRoller, cfg.StandardTargetDC);
            }

            string headerTag = duel.IsStealthAttack
                ? "🥷 <b>EMBUSCADE DEPUIS LE STEALTH</b>"
                : (!effectiveDefenderWantsToDefend
                    ? (duel.IsDefenderIncapacitated ? "💥 <b>FRAPPE SUR CIBLE NEUTRALISÉE</b>" : "🎯 <b>TOUCHÉ SUR CIBLE SANS DÉFENSE</b>")
                    : (wasDeflected
                        ? "⚠️ <b>DÉVIATION BALISTIQUE</b>"
                        : (attackRoll.IsCriticalSuccess ? "💥 <b>COUP CRITIQUE CHIRURGICAL</b>" : "🎯 <b>TOUCHÉ CHIRURGICAL</b>")));

            string hitPartStr = wasDeflected
                ? $"Visait <b>{targetInfo.DisplayName}</b> ➔ Dévie au d6 [Tirage {deflectionRoll} : {deflectionMedical}] sur <b>{actualInfo.DisplayName}</b> (Diff 0)"
                : $"Frappe sur <b>{actualInfo.DisplayName}</b>";

            string log = $"{headerTag} : {attacker.Name} ➔ {defender.Name} ({hitPartStr})\n";
            log += $"   {phaseAtt}\n";
            log += $"   {phaseDef}\n";
            log += $"   🎲 <b>RÉVÉLATION SIMULTANÉE :</b> Attaque {attackRollStr} vs Défense {defRollStr} ➔ <b>Différentiel Net : <color=#00E5FF>{(differential > 0 ? $"+{differential}" : "0")}</color></b>\n";
            if (attHasCrit)
            {
                log += $"   💥 <b>[CONSÉQUENCE CRITIQUE §8.2]</b> Relance {duel.AttackDie} ➔ <b>{attCrit.RawRoll}</b> (Palier {attCrit.Threshold} : <i>{attCrit.Title}</i>) — {attCrit.Description}\n";
            }
            log += $"   ⚔️ <b>Dégâts :</b> [{dmgFormula} = {rawDamage} Bruts] &minus; [Armure {totalArmor} (Absorbé: {absorbed})]"
                + (shieldAbsorbed > 0 ? $" &minus; [🔮Bouclier {shieldAbsorbed}]" : "")
                + $" ➔ <b><color=#FF3B5C>{finalDamage} Dégâts Nets</color></b>\n";
            log += $"   ❤️ <b>Vitalité {defender.Name} :</b> {prevHp} ➔ <b>{defender.CurrentHealth}/{defender.MaxHealth} PV</b>";

            if (exceededEncaissement)
            {
                log += $" | ⚡ <b>CHOC TRAUMATIQUE</b> (&gt; Encaissement {defender.EncaissementThreshold}) ➔ [{inflictedStatus}]";
            }

            if (failleExploitee)
            {
                log += " | 📡 <b>FAILLE EXPOSÉE EXPLOITÉE</b> (Analyse de Faille : encaissement ignoré)";
            }

            if (isThomasSymbioticImmune)
            {
                log += " | 🛡️ <b>ANCRE SYMBIOTIQUE</b> (0 dégât : neutralité de fréquence face aux alliés)";
            }

            if (bleedApplied)
            {
                log += $" | 🩸 <b>HÉMORRAGIE (Livre VII §28.4)</b> : Saignement (DoT résiduel {bleedDoT})";
            }

            if (fireApplied)
            {
                log += $" | 🔥 <b>COMBUSTION (Livre VII §28.4)</b> : En Feu pendant {fireTurns} tour(s)";
            }

            if (stunApplied)
            {
                log += " | 😵 <b>ASSOMMEMENT NET (Livre VII §28.4)</b> : Inconscient 1 tour direct";
            }

            if (wasDisintegrated)
            {
                log += " | ⚛️ <b>DÉSINTÉGRATION TOTALE (Livre VII §28.4 : Diff >= 20 & Destruction 6)</b> : Cible pulvérisée en poussière !";
            }
            else if (fatalRes == FatalBlowResolution.InstantDeath)
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

            if (moraleRes.Triggered)
            {
                log += "\n   " + moraleRes.Log;
            }

            bool causedKnockback = !isThomasSymbioticImmune && (critCausedKnockback
                || (duel.IsMartialArtsStrike && (exceededEncaissement || attackRoll.IsCriticalSuccess || differential >= 4))
                || (attacker.HasSpecialization("Teep de Rupture") && duel.AttackSkill == SkillType.MainsNues && differential >= 0));

            // Livre I §5 : l'attaquant a RÉUSSI (touché, même dévié diff 0) → +1 case de progression.
            log += ApplySkillProgression(attacker, attackSkill, true);

            // RD-049 : toute attaque rompt la furtivité
            StealthState.BreakStealth(attacker);

            return new DamageResult
            {
                IsHit = true,
                IsBlocked = false,
                IsCritical = attHasCrit,
                HasCriticalConsequence = attHasCrit,
                CriticalConsequence = attCrit,
                AttackRoll = attackRoll,
                DefenseRoll = defenseRoll,
                TargetPart = targetedPart,
                ActualHitPart = actualHitPart,
                WasDeflected = wasDeflected,
                DeflectionRoll = deflectionRoll,
                DeflectionMedicalConsequence = deflectionMedical,
                WasDisintegrated = wasDisintegrated,
                Differential = differential,
                RawDamage = rawDamage,
                ArmorAbsorbed = absorbed,
                ShieldAbsorbed = shieldAbsorbed,
                FinalDamageApplied = finalDamage,
                ExceededEncaissement = exceededEncaissement,
                InflictedStatus = inflictedStatus,
                FatalResolution = fatalRes,
                CausedKnockback = causedKnockback,
                IsCanonEntrave = duel.AppliedCanonEntrave,
                Cover = duel.Cover,
                CoverAttackPenalty = duel.CoverAttackPenalty,
                BlockedByCover = false,
                CombatLog = log,
                MoraleResult = moraleRes
            };
        }

        private static void ApplyOffensiveCriticalEffect(
            CriticalConsequence crit,
            CharacterStats attacker,
            CharacterStats defender,
            ref StatusEffect inflictedStatus,
            ref bool causedKnockback,
            ref FatalBlowResolution fatalRes)
        {
            switch (crit.Threshold)
            {
                case 1:
                    inflictedStatus |= StatusEffect.ATerre;
                    defender.ActiveStatus |= StatusEffect.ATerre;
                    break;
                case 2:
                    causedKnockback = true;
                    break;
                case 6:
                    inflictedStatus |= StatusEffect.Etourdi;
                    defender.ApplyStatus(StatusEffect.Etourdi, 1);
                    break;
                case 8:
                    ClearOneNegativeStatus(attacker);
                    break;
                case 9:
                    attacker.Essoufflement = Math.Max(0, attacker.Essoufflement - 1);
                    break;
                case 10:
                    inflictedStatus |= StatusEffect.Ralenti;
                    defender.ApplyStatus(StatusEffect.Ralenti, 1);
                    break;
                case 12:
                    inflictedStatus |= StatusEffect.Destabilise;
                    defender.ApplyStatus(StatusEffect.Destabilise, 1);
                    break;
                case 16:
                    attacker.CurrentActionPoints = Math.Min(attacker.MaxActionPoints, attacker.CurrentActionPoints + 2);
                    break;
                case 20:
                    defender.ApplyResidualDamage(StatusEffect.Saignement, 3);
                    inflictedStatus |= StatusEffect.Saignement;
                    defender.ActiveStatus |= StatusEffect.Saignement;
                    break;
                case 24:
                    if (Enum.TryParse<StatusEffect>("Inconscient", out var inc) || Enum.TryParse<StatusEffect>("Inconscience", out inc))
                    {
                        inflictedStatus |= inc;
                        defender.ActiveStatus |= inc;
                    }
                    fatalRes = FatalBlowResolution.ForcedUnconscious;
                    break;
            }
        }

        private static void ApplyDefensiveCriticalEffect(
            CriticalConsequence crit,
            CharacterStats defender,
            CharacterStats attacker)
        {
            switch (crit.Threshold)
            {
                case 1:
                case 24:
                    attacker.ActiveStatus |= StatusEffect.ATerre;
                    break;
                case 6:
                    attacker.ApplyStatus(StatusEffect.Etourdi, 1);
                    break;
                case 8:
                    ClearOneNegativeStatus(defender);
                    break;
                case 9:
                    defender.Essoufflement = Math.Max(0, defender.Essoufflement - 1);
                    break;
                case 10:
                    attacker.ApplyStatus(StatusEffect.Ralenti, 1);
                    break;
                case 12:
                    attacker.ApplyStatus(StatusEffect.Destabilise, 1);
                    break;
                case 16:
                    defender.CurrentActionPoints = Math.Min(defender.MaxActionPoints, defender.CurrentActionPoints + 2);
                    break;
                case 20:
                    // Contre-attaque automatique : riposte réflexe infligeant 3 dégâts bruts à l'assaillant
                    attacker.CurrentHealth = Math.Max(0, attacker.CurrentHealth - 3);
                    break;
            }
        }

        private static void ClearOneNegativeStatus(CharacterStats target)
        {
            if (target == null) return;
            if ((target.ActiveStatus & StatusEffect.Destabilise) != 0) target.ActiveStatus &= ~StatusEffect.Destabilise;
            else if ((target.ActiveStatus & StatusEffect.Etourdi) != 0) target.ActiveStatus &= ~StatusEffect.Etourdi;
            else if ((target.ActiveStatus & StatusEffect.Ralenti) != 0) target.ActiveStatus &= ~StatusEffect.Ralenti;
            else if ((target.ActiveStatus & StatusEffect.ATerre) != 0) target.ActiveStatus &= ~StatusEffect.ATerre;
            else if ((target.ActiveStatus & StatusEffect.Sonne) != 0) target.ActiveStatus &= ~StatusEffect.Sonne;
        }

        /// <summary>
        /// Progression organique (Livre I §5) : sur réussite, coche +1 case dans la
        /// compétence utilisée. Retourne le suffixe de log (vide si rien / échec / sans fiche).
        /// </summary>
        private static string ApplySkillProgression(CharacterStats user, SkillType skill, bool isOffensive)
        {
            if (user == null || user.Sheet == null) return string.Empty;
            if (!CharacterProgressionManager.RegisterSuccessfulSkillUse(user.Sheet, skill, out string prog, out _, isOffensive))
                return string.Empty;
            if (string.IsNullOrEmpty(prog)) return string.Empty;
            return $"\n   {prog}";
        }

        private static string ApplyOpposedProgression(CharacterStats attacker, SkillType attackSkill, bool attOffensive, CharacterStats defender, SkillType defenseSkill, bool defenderPlays, int differential)
        {
            if (differential >= 0)
                return ApplySkillProgression(attacker, attackSkill, attOffensive);
            if (defenderPlays)
                return ApplySkillProgression(defender, defenseSkill, false);
            return string.Empty;
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
            var roll = _diceRoller.Roll(die, mod, targetDC);
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
            var calc = new AreaFireCalculator(_diceRoller);
            return calc.ResolveAreaFire(attacker, attackerPos, targetPos, profile, combatantsOnField, attackerBonusAP, attackerPE, fieldCover);
        }

        /// <summary>
        /// Rétro-compatibilité : redirige vers la table d6 exacte du Livre VI §26.1.
        /// </summary>
        private BodyPart GetAdjacentBodyPart(BodyPart targeted)
        {
            return ResolveAnatomicalDeflection(targeted, out _, out _);
        }

        public static bool IsUpperBodyPart(BodyPart part)
        {
            return part == BodyPart.Tete
                || part == BodyPart.YeuxVisage
                || part == BodyPart.CouTrachee
                || part == BodyPart.CoeurPoumons
                || part == BodyPart.Torse;
        }

        /// <summary>
        /// Table de déviation d4/d6 chirurgicale officielle (Livre VI §26.1).
        /// </summary>
        public BodyPart ResolveAnatomicalDeflection(BodyPart targeted, out int rollDie, out string medicalConsequence)
        {
            rollDie = _diceRoller != null ? _diceRoller.Roll(DiceType.D6, 0, 0).RawRoll : _random.Next(1, 7);
            if (rollDie < 1 || rollDie > 6) rollDie = _random.Next(1, 7);

            bool isUpper = IsUpperBodyPart(targeted);

            if (isUpper)
            {
                switch (rollDie)
                {
                    case 1:
                        medicalConsequence = "Cécité partielle, hémorragie faciale, désorientation";
                        return BodyPart.YeuxVisage;
                    case 2:
                        medicalConsequence = "Risque d'asphyxie, perte d'essoufflement immédiate";
                        return BodyPart.CouTrachee;
                    case 3:
                        medicalConsequence = "Commotion, épreuve de Constitution pour rester conscient";
                        return BodyPart.Tete;
                    case 4:
                        medicalConsequence = "Dégâts massifs, perforation, incapacité respiratoire";
                        return BodyPart.CoeurPoumons;
                    case 5:
                    case 6:
                    default:
                        medicalConsequence = "Encaissement standard";
                        return BodyPart.Torse;
                }
            }
            else
            {
                switch (rollDie)
                {
                    case 1:
                        medicalConsequence = "Impact claviculaire / épaule, désorientation";
                        return targeted == BodyPart.BrasGauche ? BodyPart.BrasGauche : (targeted == BodyPart.BrasDroit ? BodyPart.BrasDroit : BodyPart.Torse);
                    case 2:
                        medicalConsequence = "Impact bras droit (porteur d'arme)";
                        return BodyPart.BrasDroit;
                    case 3:
                        medicalConsequence = "Impact bras gauche (bouclier / parade)";
                        return BodyPart.BrasGauche;
                    case 4:
                        medicalConsequence = "Abdomen et côtes heurtés, incapacité respiratoire";
                        return BodyPart.Torse;
                    case 5:
                    case 6:
                    default:
                        medicalConsequence = "Encaissement standard, perte de motricité si jambe brisée";
                        return BodyPart.Jambes;
                }
            }
        }

        /// <summary>
        /// RD-082 (Livre VII §28.4) : Vérifie si l'attaque utilise une arme tranchante ou perçante.
        /// </summary>
        public static bool IsSlashingOrPiercingAttack(SkillType skill, Killtime.Core.Inventory.InventoryItem weapon, CharacterStats attacker)
        {
            SkillType baseSkill = SkillDefinitions.ResolveBaseSkill(skill);
            if (baseSkill == SkillType.ArmesPercantes) return true;

            if (weapon != null)
            {
                string desc = $"{weapon.Name} {weapon.Description} {weapon.Category}".ToLowerInvariant();
                if (desc.Contains("marteau") || desc.Contains("masse") || desc.Contains("gourdin") || desc.Contains("matraque") || desc.Contains("bâton") || desc.Contains("baton"))
                {
                    return false;
                }

                if (desc.Contains("tranchant") || desc.Contains("perçant") || desc.Contains("percant") ||
                    desc.Contains("épée") || desc.Contains("epee") || desc.Contains("lame") ||
                    desc.Contains("dague") || desc.Contains("hache") || desc.Contains("lance") ||
                    desc.Contains("pique") || desc.Contains("couteau") || desc.Contains("rapière") ||
                    desc.Contains("rapiere") || desc.Contains("baïonnette") || desc.Contains("baionnette") ||
                    desc.Contains("flèche") || desc.Contains("fleche") || desc.Contains("carreau"))
                {
                    return true;
                }
            }

            if (baseSkill == SkillType.ManiementArmes)
            {
                if (attacker != null && (attacker.HasSpecialization("Armes Contondantes") || attacker.HasSpecialization("Marteau de Guerre")))
                {
                    return false;
                }
                return true;
            }

            return false;
        }

        /// <summary>
        /// RD-082 (Livre VII §28.4) : Vérifie si l'attaque utilise une énergie thermique, feu ou plasma.
        /// </summary>
        public static bool IsFireOrPlasmaAttack(SkillType skill, Killtime.Core.Inventory.InventoryItem weapon, CharacterStats attacker)
        {
            if (skill == SkillType.MagieElementale || skill == SkillType.MagiePrimale)
            {
                if (attacker != null && (attacker.HasSpecialization("Feu") || attacker.HasSpecialization("Flamme") || attacker.HasSpecialization("Pyromancie") || attacker.HasSpecialization("Thermique")))
                    return true;
            }

            if (weapon != null)
            {
                string desc = $"{weapon.Name} {weapon.Description} {weapon.Category} {weapon.AmmoType} {weapon.GrenadeKind}".ToLowerInvariant();
                if (desc.Contains("feu") || desc.Contains("flamme") || desc.Contains("plasma") ||
                    desc.Contains("incendiaire") || desc.Contains("thermique") || desc.Contains("pyro") ||
                    desc.Contains("laser") || desc.Contains("brûl") || desc.Contains("brul") ||
                    desc.Contains("thermobarique"))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// RD-082 (Livre VII §28.4) : Vérifie si l'assaillant possède la spécialisation Destruction de Rang 6.
        /// </summary>
        public static bool HasDestructionTier6(CharacterStats attacker)
        {
            if (attacker == null) return false;
            if (attacker.HasSpecialization("Destruction 6")
                || attacker.HasSpecialization("Destruction : Rang 6")
                || attacker.HasSpecialization("Destruction : Stade 6")
                || attacker.HasSpecialization("Voie de la Destruction : Rang 6")
                || attacker.HasSpecialization("Voie de la Destruction 6")
                || attacker.HasSpecialization("Destruction"))
            {
                return true;
            }

            if (attacker.Sheet?.UnlockedSpecializations != null)
            {
                for (int i = 0; i < attacker.Sheet.UnlockedSpecializations.Count; i++)
                {
                    string spec = attacker.Sheet.UnlockedSpecializations[i];
                    if (string.IsNullOrEmpty(spec)) continue;
                    if (spec.IndexOf("Destruction", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (spec.Contains("6") || spec.IndexOf("VI", StringComparison.OrdinalIgnoreCase) >= 0 || spec.Equals("Destruction", StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
            }

            return false;
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

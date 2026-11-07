using System;
using Killtime.Core.Character;
using Killtime.Core.Dice;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Visibility;

namespace Killtime.Core.Combat
{
    /// <summary>
    /// Résolution mathématique intégrale des passes d'armes et tirs ciblés (Livre V).
    /// Conforme à l'Axiome Fondateur : Tout jet découle d'une compétence.
    /// L'attaque est opposée par une compétence défensive, sauf décision de non-défense.
    /// Séquence impérative : déclaration attaquant (mise cachée) -> déclaration défenseur
    /// à l'aveugle -> révélation simultanée -> résolution normale. Aucun résultat n'est
    /// révélé avant que les deux mises soient engagées.
    ///
    /// FAÇADE (refactor services composés) : l'API publique est inchangée, chaque appel
    /// est délégué au service propriétaire. Ordre : DuelSetupService (mises) ->
    /// DuelResolutionService (jets) -> DamageEngineService (dégâts),
    /// SpecialManeuverService (guet / opportunité / lutte / groupe),
    /// CombatProgressionService (progressions, tables anatomiques RD-082).
    /// </summary>
    public class CombatCalculator
    {
        private readonly CombatDuelContext _ctx;
        private readonly CombatProgressionService _progression;
        private readonly DamageEngineService _damage;
        private readonly DuelSetupService _setup;
        private readonly DuelResolutionService _resolution;
        private readonly SpecialManeuverService _maneuvers;

        public CombatCalculator(DiceRoller diceRoller = null)
        {
            _ctx = new CombatDuelContext(diceRoller);
            _progression = new CombatProgressionService(_ctx);
            _damage = new DamageEngineService(_ctx, _progression);
            _setup = new DuelSetupService(_ctx);
            _resolution = new DuelResolutionService(_ctx, _setup, _damage, _progression);
            _maneuvers = new SpecialManeuverService(_ctx, _setup, _resolution, _progression);
        }

        public void SetContactDistanceState(bool isAtContact)
        {
            _ctx.IsAtContactDistance = isAtContact;
        }

        /// <summary>
        /// RD-053 : Vérifie si un combattant dispose d'une source lumineuse (lampe/torche)
        /// ou d'une vision thermique (spécialisation ou lunettes).
        /// </summary>
        public static bool HasLampOrThermal(CharacterStats stats) => DuelSetupService.HasLampOrThermal(stats);

        /// <summary>
        /// Résolution officielle du Livre V en duel aveugle.
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
            bool isNight = false,
            bool isButtstroke = false) =>
            _resolution.ResolveTargetedAttack(attacker, defender, targetedPart, attackSkill, defenseSkill,
                weaponBaseDamage, cancelPenaltyWithAP, defenderWantsToDefend, attackerSpecialization,
                defenderSpecialization, defenderArmor, attackerBonusAP, defenderBonusAP, attackerPE,
                defenderPE, cover, weaponBonusEc, isNight, isButtstroke);

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
            bool isNight = false,
            bool isButtstroke = false) =>
            _resolution.ResolveTargetedAttack(attacker, defender, targetedPart, attackDie, attackModifier,
                defenseDie, defenseModifier, weaponBaseDamage, cancelPenaltyWithAP, defenderArmor,
                attackerBonusAP, defenderBonusAP, defenderWantsToDefend, attackSkill, defenseSkill,
                attackerPE, defenderPE, cover, weaponBonusEc, isNight, isButtstroke);

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
            bool isNight = false,
            bool isButtstroke = false) =>
            _setup.BeginDuel(attacker, defender, targetedPart, attackDie, attackModifier, defenseDie,
                defenseModifier, weaponBaseDamage, cancelPenaltyWithAP, defenderWantsToDefend, defenderArmor,
                attackSkill, defenseSkill, out error, cover, weaponBonusEc, skipAttackerCost,
                elevationAttackMod, elevationLabel, isNight, isButtstroke);

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
            bool isNight = false,
            bool isButtstroke = false) =>
            _setup.BeginSkillDuel(attacker, defender, targetedPart, attackSkill, defenseSkill, weaponBaseDamage,
                cancelPenaltyWithAP, defenderWantsToDefend, attackerSpecialization, defenderSpecialization,
                defenderArmor, out error, cover, weaponBonusEc, skipAttackerCost, elevationAttackMod,
                elevationLabel, isNight, isButtstroke);

        /// <summary>
        /// Étape 1 : DÉCLARATION de l'attaquant. Engage la mise bonus (PA + PE) AVANT tout jet.
        /// 1 PA = +1, 1 PE = +DuelPEBonusPerPoint (plafond Constitution). Mise cachée.
        /// Retourne le bonus total engagé (PA appliqués + PE appliqués x valeur).
        /// </summary>
        public int DeclareAttackerStakes(AttackDuel duel, int bonusPA, int pePoints) =>
            _setup.DeclareAttackerStakes(duel, bonusPA, pePoints);

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
            string defenderSpecialization = null) =>
            _setup.DeclareDefenderStakes(duel, defenseSkill, wantsToDefend, bonusPA, pePoints, defenderSpecialization);

        /// <summary>
        /// Déclaration aveugle automatique (IA / défense réactive sans fenêtre).
        /// Estime le besoin SANS voir le jet adverse : compare les espérances des dés
        /// (moyenne + modificateurs + mises attaquant inconnues = 0) et engage le
        /// complément dans la limite des PA restants après la base. Jamais de PE auto.
        /// </summary>
        public int AutoDeclareDefenderStakes(AttackDuel duel, string defenderSpecialization = null) =>
            _setup.AutoDeclareDefenderStakes(duel, defenderSpecialization);

        /// <summary>
        /// Entre en guet : réserve un tir de réaction (coût config, rayon figé).
        /// </summary>
        public bool TryEnterOverwatch(CharacterStats watcher, int range, out string error) =>
            _maneuvers.TryEnterOverwatch(watcher, range, out error);

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
            string elevationLabel = null) =>
            _maneuvers.ResolveOverwatchFire(watcher, target, weaponBaseDamage, cover, weaponBonusEc,
                targetedPart, attackSkill, elevationAttackMod, elevationLabel);

        /// <summary>
        /// Choisit la compétence de mêlée du frappeur : arme de contact équipée
        /// (compétence associée rabattue) ou Mains Nues à défaut.
        /// </summary>
        public static SkillType ResolveOpportunitySkill(CharacterStats reactor) =>
            SpecialManeuverService.ResolveOpportunitySkill(reactor);

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
            string elevationLabel = null) =>
            _maneuvers.ResolveOpportunityStrike(reactor, target, weaponBaseDamage, attackSkill,
                targetedPart, elevationAttackMod, elevationLabel);

        /// <summary>
        /// Balayage d'opportunité (variante) : frappe gratuite visant les Jambes
        /// (À Terre + Ralenti en cas de choc, Livre V §22). Même coût qu'une frappe.
        /// </summary>
        public DamageResult? ResolveOpportunityTrip(
            CharacterStats reactor,
            CharacterStats target,
            int weaponBaseDamage,
            SkillType attackSkill,
            int elevationAttackMod = 0,
            string elevationLabel = null) =>
            _maneuvers.ResolveOpportunityTrip(reactor, target, weaponBaseDamage, attackSkill,
                elevationAttackMod, elevationLabel);

        /// <summary>
        /// Blocage d'opportunité : duel opposé aveugle Athlétisme (bloqueur) vs
        /// Athlétisme (fuyard), mises 0, défense auto. Le coût de réaction (1 PA
        /// config) est payé par l'appelant AVANT l'appel. Victoire du bloqueur
        /// (différentiel ≥ 0) = le pas est annulé, sans grapple : le fuyard reste
        /// sur sa case et ses PA de déplacement sont remboursés.
        /// </summary>
        public OpposedCheckResult ResolveOpportunityBlock(
            CharacterStats blocker,
            CharacterStats mover) =>
            _maneuvers.ResolveOpportunityBlock(blocker, mover);

        /// <summary>
        /// Tient son action : passe en attente (coût config, 0 PA par défaut).
        /// </summary>
        public bool TryHoldAction(CharacterStats unit, out string error) =>
            _maneuvers.TryHoldAction(unit, out error);

        /// <summary>
        /// Retarde son tour : initiative réduite de la pénalité (config, -2).
        /// La réinsertion dans l'ordre est effectuée par le TurnManager.
        /// </summary>
        public bool TryDelayInitiative(CharacterStats unit, out int newTotal, out string error) =>
            _maneuvers.TryDelayInitiative(unit, out newTotal, out error);

        /// <summary>
        /// Entre en canalisation / action en progression (coût config, 1 PA).
        /// </summary>
        public bool TryBeginChanneling(CharacterStats unit, string label, out string error) =>
            _maneuvers.TryBeginChanneling(unit, label, out error);

        /// <summary>
        /// RD-202 Préparation d'Action (Livre V §20.3) : 1 PA -> +2 au jet de
        /// l'action ou de la défense suivante (attaque OU parade). One-shot,
        /// consommée au jet suivant. Ne cumule pas avec la canalisation.
        /// </summary>
        public bool TryBeginPreparation(CharacterStats unit, out string error) =>
            _maneuvers.TryBeginPreparation(unit, out error);

        /// <summary>
        /// RD-202 Dégagement (Livre V §20.3) : fuite immédiate après une attaque
        /// ou une parade résolue (+1 PA de surcharge, trajet payé à part).
        /// DISTINCT du Décrochage RD-031 (préventif, sans réaction).
        /// </summary>
        public bool TryGetAwayMove(CharacterStats unit, out string error) =>
            _maneuvers.TryGetAwayMove(unit, out error);

        /// <summary>
        /// Vérifie l'interruption de canalisation après des dégâts nets
        /// (après armure/bouclier) : vrai si la canalisation est rompue.
        /// </summary>
        public bool CheckChannelInterruption(CharacterStats defender, int finalDamageApplied, out string interruptedLabel) =>
            _maneuvers.CheckChannelInterruption(defender, finalDamageApplied, out interruptedLabel);

        /// <summary>
        /// Réoriente un duel d'attaque vers un protecteur qui s'interpose pour
        /// prendre le coup à la place de l'allié (RD-032, Livre V §20.5).
        /// Le protecteur devient le défenseur du duel avec son armure et ses défenses.
        /// </summary>
        public AttackDuel RedirectDuelToProtector(
            AttackDuel duel,
            CharacterStats protector,
            SkillType? defenseSkill = null,
            string protectorSpecialization = null) =>
            _resolution.RedirectDuelToProtector(duel, protector, defenseSkill, protectorSpecialization);

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
            int weaponBonusEc = 0) =>
            _resolution.ResolveInterceptedAttack(attacker, protector, targetedPart, attackSkill,
                weaponBaseDamage, attackerBonusAP, attackerPE, cover, weaponBonusEc);

        /// <summary>
        /// Étape 3 : RÉSOLUTION. Les deux dés sont lancés SIMULTANÉMENT avec les mises
        /// déclarées, puis révélés ensemble (différentiel, dégâts, états).
        /// La déclaration du défenseur defaults au passif si elle n'a pas eu lieu.
        /// </summary>
        public DamageResult ResolveBlindDuel(AttackDuel duel) =>
            _resolution.ResolveBlindDuel(duel);

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
            bool defenderAutoStakes = false) =>
            _resolution.ResolveOpposedCheck(attacker, attackSkill, defender, defenseSkill, attackerBonusAP,
                attackerPE, defenderBonusAP, defenderPE, defenderAutoStakes);

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
            bool defenderAutoStakes = false) =>
            _maneuvers.ResolveGrappleDuel(attacker, defender, attackerBonusAP, attackerPE,
                defenderBonusAP, defenderPE, defenderAutoStakes);

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
            bool grapplerAutoStakes = false) =>
            _maneuvers.ResolveGrappleEscapeDuel(victim, grappler, victimBonusAP, victimPE,
                grapplerBonusAP, grapplerPE, grapplerAutoStakes);

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
            bool defenderAutoStakes = false) =>
            _maneuvers.ResolveStrangulationDuel(attacker, defender, attackerBonusAP, attackerPE,
                defenderBonusAP, defenderPE, defenderAutoStakes);

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
            bool defenderAutoStakes = false) =>
            _maneuvers.ResolveShoveDuel(attacker, defender, isShieldBash, attackerBonusAP, attackerPE,
                defenderBonusAP, defenderPE, defenderAutoStakes);

        /// <summary>
        /// RD-204 Possession (Livre VI §24.4) : duel opposé aveugle MagieEsprit
        /// vs MagieEsprit. Diff ≥ 10 (15 si boss) = contrôle 1 tour (PossessionState).
        /// </summary>
        public OpposedCheckResult ResolvePossessionDuel(
            CharacterStats attacker,
            CharacterStats defender,
            int attackerBonusAP = 0,
            int attackerPE = 0,
            int defenderBonusAP = 0,
            int defenderPE = 0,
            bool defenderAutoStakes = false) =>
            _maneuvers.ResolvePossessionDuel(attacker, defender, attackerBonusAP, attackerPE,
                defenderBonusAP, defenderPE, defenderAutoStakes);

        /// <summary>
        /// RD-204 Repousser en épreuve : duel opposé aveugle, Diff ≥ 1 = recul
        /// forcé d'1 case (CausedKnockback, résolu en arène). Sans dégât.
        /// </summary>
        public DamageResult ResolvePushDuel(
            CharacterStats attacker,
            CharacterStats defender,
            int attackerBonusAP = 0,
            int attackerPE = 0,
            int defenderBonusAP = 0,
            int defenderPE = 0,
            bool defenderAutoStakes = false) =>
            _maneuvers.ResolvePushDuel(attacker, defender, attackerBonusAP, attackerPE,
                defenderBonusAP, defenderPE, defenderAutoStakes);

        /// <summary>
        /// RD-204 Attirer en épreuve : miroir de Repousser, Diff ≥ 1 =
        /// rapprochée forcée d'1 case (CausedPull, application arène à brancher).
        /// </summary>
        public DamageResult ResolvePullDuel(
            CharacterStats attacker,
            CharacterStats defender,
            int attackerBonusAP = 0,
            int attackerPE = 0,
            int defenderBonusAP = 0,
            int defenderPE = 0,
            bool defenderAutoStakes = false) =>
            _maneuvers.ResolvePullDuel(attacker, defender, attackerBonusAP, attackerPE,
                defenderBonusAP, defenderPE, defenderAutoStakes);

        /// <summary>
        /// RD-084 : Attaque combinée de groupe (Livre V §21.2). Les participants
        /// se synchronisent sur l'initiative du membre le plus lent, chacun
        /// effectue son épreuve (dé de sa compétence + états + mises aveugles),
        /// les résultats totaux s'additionnent face à l'UNIQUE défense de la cible.
        /// L'armure et le bouclier ne sont appliqués qu'UNE fois sur le total
        /// sommationné : le dépassement massif pulvérise les défenses.
        /// Chaque attaquant paie le coût de base (config) + ses mises ; la cible
        /// paie sa réaction de base + ses mises (ou encaisse à 0 sans réaction).
        /// </summary>
        public CombinedAttackResult ResolveCombinedAttack(
            System.Collections.Generic.IList<CharacterStats> attackers,
            CharacterStats defender,
            BodyPart targetedPart,
            SkillType attackSkill,
            SkillType defenseSkill,
            int weaponBaseDamagePerAttacker = 5,
            System.Collections.Generic.IList<int> attackerBonusAP = null,
            System.Collections.Generic.IList<int> attackerPE = null,
            int defenderBonusAP = 0,
            int defenderPE = 0,
            bool defenderWantsToDefend = true,
            bool defenderAutoStakes = false) =>
            _maneuvers.ResolveCombinedAttack(attackers, defender, targetedPart, attackSkill, defenseSkill,
                weaponBaseDamagePerAttacker, attackerBonusAP, attackerPE, defenderBonusAP, defenderPE,
                defenderWantsToDefend, defenderAutoStakes);

        /// <summary>
        /// RD-084 : Variante à dégâts d'arme différenciés par participant
        /// (arme équipée de chacun, 5 à mains nues). La sommation reste comparée
        /// à l'unique défense, armure appliquée une seule fois.
        /// </summary>
        public CombinedAttackResult ResolveCombinedAttack(
            System.Collections.Generic.IList<CharacterStats> attackers,
            CharacterStats defender,
            BodyPart targetedPart,
            SkillType attackSkill,
            SkillType defenseSkill,
            System.Collections.Generic.IList<int> weaponBaseDamages,
            System.Collections.Generic.IList<int> attackerBonusAP = null,
            System.Collections.Generic.IList<int> attackerPE = null,
            int defenderBonusAP = 0,
            int defenderPE = 0,
            bool defenderWantsToDefend = true,
            bool defenderAutoStakes = false) =>
            _maneuvers.ResolveCombinedAttack(attackers, defender, targetedPart, attackSkill, defenseSkill,
                weaponBaseDamages, attackerBonusAP, attackerPE, defenderBonusAP, defenderPE,
                defenderWantsToDefend, defenderAutoStakes);

        /// <summary>
        /// RD-084 : Round de course-poursuite (Livre V §21.1). La proie effectue
        /// UNE unique épreuve d'Athlétisme (mises une seule fois), chaque
        /// poursuivant effectue la sienne et se compare à ce total :
        /// réussite de la proie = distance maintenue (≥ 1 case), faillite =
        /// le poursuivant gagne du terrain (+1 faillite cumulée). À 4 faillites
        /// cumulées : arrêt et capture immédiate (PursuitState + Immobilisé).
        /// </summary>
        public PursuitRoundResult ResolvePursuitRound(
            CharacterStats prey,
            System.Collections.Generic.IList<CharacterStats> pursuers,
            int preyBonusAP = 0,
            int preyPE = 0,
            bool pursuersAutoStakes = true,
            System.Collections.Generic.IList<int> pursuerBonusAP = null,
            System.Collections.Generic.IList<int> pursuerPE = null) =>
            _maneuvers.ResolvePursuitRound(prey, pursuers, preyBonusAP, preyPE, pursuersAutoStakes,
                pursuerBonusAP, pursuerPE);

        /// <summary>Espérance d'un dé (pour l'estimation aveugle de l'IA, mise adverse inconnue).</summary>
        public static double DieAverage(DiceType die) => CombatDuelShared.DieAverage(die);

        /// <summary>
        /// Épreuve simple hors duel (Livre II §7) : jet de compétence vs DC, avec
        /// progression organique automatique sur réussite (Livre I §5).
        /// 1 réussite = 1 case dans CETTE compétence ; piste pleine (cases = base) = +1 XP lié.
        /// </summary>
        public DiceRollResult ResolveSkillCheck(CharacterStats user, SkillType skill, int targetDC, bool isOffensive = false, int extraModifier = 0) =>
            _resolution.ResolveSkillCheck(user, skill, targetDC, isOffensive, extraModifier);

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
            CoverType fieldCover = CoverType.None) =>
            _resolution.ResolveAreaFireShot(attacker, attackerPos, targetPos, profile, combatantsOnField,
                attackerBonusAP, attackerPE, fieldCover);

        public static bool IsUpperBodyPart(BodyPart part) => CombatProgressionService.IsUpperBodyPart(part);

        /// <summary>
        /// Table de déviation d4/d6 chirurgicale officielle (Livre V §22.1).
        /// </summary>
        public BodyPart ResolveAnatomicalDeflection(BodyPart targeted, out int rollDie, out string medicalConsequence) =>
            _progression.ResolveAnatomicalDeflection(targeted, out rollDie, out medicalConsequence);

        /// <summary>
        /// RD-082 (Livre VI §24.4) : Vérifie si l'attaque utilise une arme tranchante ou perçante.
        /// </summary>
        public static bool IsSlashingOrPiercingAttack(SkillType skill, Killtime.Core.Inventory.InventoryItem weapon, CharacterStats attacker) =>
            CombatProgressionService.IsSlashingOrPiercingAttack(skill, weapon, attacker);

        /// <summary>
        /// RD-082 (Livre VI §24.4) : Vérifie si l'attaque utilise une énergie thermique, feu ou plasma.
        /// </summary>
        public static bool IsFireOrPlasmaAttack(SkillType skill, Killtime.Core.Inventory.InventoryItem weapon, CharacterStats attacker) =>
            CombatProgressionService.IsFireOrPlasmaAttack(skill, weapon, attacker);

        /// <summary>
        /// RD-082 (Livre VI §24.4) : Vérifie si l'assaillant possède la spécialisation Destruction de Rang 6.
        /// </summary>
        public static bool HasDestructionTier6(CharacterStats attacker) =>
            CombatProgressionService.HasDestructionTier6(attacker);
    }
}

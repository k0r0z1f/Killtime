using System;
using Killtime.Core.Character;
using Killtime.Core.Dice;

namespace Killtime.Core.Combat
{
    /// <summary>
    /// Duel séquentiel officiel (Livres II §7.3 + VI §24.1) :
    /// 1. L'attaquant désigne sa cible, paie le coût de base, lance le dé de sa compétence,
    ///    puis PEUT AJOUTER des PA bonus APRÈS avoir vu son résultat (+1 / PA).
    /// 2. La cible paie sa réaction, lance le dé de sa compétence défensive,
    ///    puis PEUT AJOUTER des PA bonus APRÈS avoir vu son résultat (+1 / PA).
    /// 3. L'attaque se conclut normalement (différentiel, dégâts, armure, états).
    /// Les dés découlent uniquement du niveau de compétence (paliers carac + entraînements
    /// depuis d2). Ex : Mains Nues +2 entr., FOR 5 (+2) = 4 niveaux -> d10 ;
    /// Esquive 1 entr., réf. 5 (+2) = 3 niveaux -> d8.
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

        // Coûts de base déjà débités
        public int BaseAttackCost;
        public int BaseDefenseCost;
        public bool CanDefenderReact;
        public bool IsDefenderIncapacitated;

        // Modificateurs situationnels (hors PA bonus)
        public int AttackBaseMod;
        public int DefenseBaseMod;
        public bool IsRangedAttack;
        public bool IsMartialArtsStrike;
        public bool AppliedCanonEntrave;

        // Jets bruts (sans PA bonus) puis finaux (avec PA bonus post-tirage)
        public DiceRollResult AttackRawRoll;
        public DiceRollResult DefenseRawRoll;
        public DiceRollResult AttackFinalRoll;
        public DiceRollResult DefenseFinalRoll;
        public bool HasAttackRaw;
        public bool HasDefenseRaw;
        public int AttackerBonusPAApplied;
        public int DefenderBonusPAApplied;
        public int DefenderUnreactivePenalty;
    }

    /// <summary>
    /// Résolution mathématique intégrale des passes d'armes et tirs ciblés (Livre VI).
    /// Conforme à l'Axiome Fondateur : Tout jet découle d'une compétence.
    /// L'attaque est opposée par une compétence défensive, sauf décision de non-défense.
    /// Séquence impérative : attaque (jet puis PA après tirage) -> défense (jet puis PA
    /// après tirage) -> résolution normale. Aucune enchère aveugle préalable.
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
        /// Résolution officielle du Livre VI axée sur les compétences d'attaque et de défense.
        /// Les PA bonus sont injectés APRÈS les tirages (attaquant d'abord, défenseur ensuite),
        /// dans la limite des PA restants après paiement des coûts de base.
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
            int defenderBonusAP = 0)
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
                attackSkill: attackSkill,
                defenseSkill: defenseSkill
            );
        }

        /// <summary>
        /// Surcharge de compatibilité descendante avec injection directe de dés.
        /// Les bonus PA restent interprétés comme des injections post-tirage.
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
            SkillType defenseSkill = SkillType.Esquive)
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
                attackSkill: attackSkill,
                defenseSkill: defenseSkill
            );
        }

        // =====================================================================
        // API SÉQUENTIELLE PAS-À-PAS (pour UI interactive / IA réactive / tests)
        // =====================================================================

        /// <summary>
        /// Étape 0 : prépare le duel, paie les coûts de base (attaque 2/3 PA, réaction 1 PA).
        /// Ne lance aucun dé, ne dépense aucun PA bonus. Les bonus seront ajoutés après tirages.
        /// Retourne null + message d'erreur si l'attaque est impossible (PA insuffisants).
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
            out string error)
        {
            error = null;
            var targetInfo = BodyPartInfo.GetInfo(targetedPart);
            var cfg = Rules.CoreRulesConfig.Instance;

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
                DefenderUnreactivePenalty = cfg.UnreactiveDefensePenalty
            };

            // Modificateur de base d'attaque (hors PA bonus post-tirage) : états + visée + canon entravé.
            int baseAtt = attackModifier;
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

            // 1. Dépense PA de base de l'Attaquant (sans bonus : le bonus vient après le tirage).
            duel.BaseAttackCost = cfg.BaseAttackAPCost + (cancelPenaltyWithAP ? cfg.CancelAimPenaltyAPCost : 0);
            if (!attacker.ConsumeActionPoints(duel.BaseAttackCost))
            {
                error = $"{attacker.Name} n'a pas assez de PA ({attacker.CurrentActionPoints}/{duel.BaseAttackCost}) pour attaquer !";
                return null;
            }

            // 2. Décision & Dépense PA de base du Défenseur (sans bonus).
            duel.BaseDefenseCost = cfg.BaseReactionAPCost;
            duel.IsDefenderIncapacitated = !defender.CanDefendActively();
            bool effectiveWants = defenderWantsToDefend && !duel.IsDefenderIncapacitated;
            duel.CanDefenderReact = false;
            if (effectiveWants)
            {
                // Tente de payer la réaction de base ; échec -> réflexe à -2, 0 PA dépensé.
                if (defender.ConsumeActionPoints(duel.BaseDefenseCost))
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
        /// puis paie les coûts de base. Les PA bonus restent à injecter après tirages.
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
            out string error)
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
                defenderWantsToDefend, defenderArmor, attackSkill, defenseSkill, out error);
        }

        /// <summary>Étape 1 : l'attaquant lance son dé (sans PA bonus). Total brut fixé.</summary>
        public DiceRollResult RollAttackerRaw(AttackDuel duel)
        {
            var cfg = Rules.CoreRulesConfig.Instance;
            duel.AttackRawRoll = _diceRoller.Roll(duel.AttackDie, duel.AttackBaseMod, cfg.StandardTargetDC);
            duel.AttackFinalRoll = duel.AttackRawRoll;
            duel.HasAttackRaw = true;
            duel.AttackerBonusPAApplied = 0;
            return duel.AttackRawRoll;
        }

        /// <summary>
        /// Étape 1bis : après avoir vu son résultat, l'attaquant peut ajouter des PA (+1 / PA).
        /// Débités aussitôt, dans la limite des PA restants. Retourne le bonus réellement appliqué.
        /// </summary>
        public int AddAttackerBonusPA(AttackDuel duel, int requestedBonus)
        {
            if (!duel.HasAttackRaw) throw new InvalidOperationException("RollAttackerRaw doit précéder l'injection des PA attaquant.");
            int applied = SpendBonusPA(duel.Attacker, Math.Max(0, requestedBonus));
            duel.AttackerBonusPAApplied += applied;
            duel.AttackFinalRoll = WithAddedBonus(duel.AttackRawRoll, duel.AttackBaseMod, applied);
            return applied;
        }

        /// <summary>Étape 2 : la cible lance son dé défensif (sans PA bonus). Total brut fixé.</summary>
        public DiceRollResult RollDefenderRaw(AttackDuel duel)
        {
            var cfg = Rules.CoreRulesConfig.Instance;
            if (!duel.DefenderWantsToDefend || duel.IsDefenderIncapacitated)
            {
                duel.DefenseRawRoll = new DiceRollResult
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
                duel.DefenseFinalRoll = duel.DefenseRawRoll;
                duel.HasDefenseRaw = true;
                duel.DefenderBonusPAApplied = 0;
                return duel.DefenseRawRoll;
            }
            duel.DefenseRawRoll = _diceRoller.Roll(duel.DefenseDie, duel.DefenseBaseMod, cfg.StandardTargetDC);
            duel.DefenseFinalRoll = duel.DefenseRawRoll;
            duel.HasDefenseRaw = true;
            duel.DefenderBonusPAApplied = 0;
            return duel.DefenseRawRoll;
        }

        /// <summary>
        /// Étape 2bis : après avoir vu son résultat (et le total final attaquant), la cible peut
        /// ajouter des PA (+1 / PA). Sans réaction active ou sans défense voulue : ignoré (0).
        /// </summary>
        public int AddDefenderBonusPA(AttackDuel duel, int requestedBonus)
        {
            if (!duel.HasDefenseRaw) throw new InvalidOperationException("RollDefenderRaw doit précéder l'injection des PA défenseur.");
            if (!duel.DefenderWantsToDefend || duel.IsDefenderIncapacitated || !duel.CanDefenderReact)
                return 0;
            int applied = SpendBonusPA(duel.Defender, Math.Max(0, requestedBonus));
            duel.DefenderBonusPAApplied += applied;
            duel.DefenseFinalRoll = WithAddedBonus(duel.DefenseRawRoll, duel.DefenseBaseMod, applied);
            return applied;
        }

        /// <summary>Étape 3 : l'attaque se conclut normalement (différentiel, dégâts, états).</summary>
        public DamageResult FinishDuel(AttackDuel duel)
        {
            return ResolveDuelDamage(duel);
        }

        /// <summary>
        /// Calcule le bonus défensif réactif minimal pour tenter de neutraliser l'attaque,
        /// après révélation du total final attaquant (IA / auto-défense).
        /// Besoin = attaque finale - défense brute + 1 (pour passer en différentiel négatif).
        /// Plafonné aux PA restants après la réaction de base. 0 si défense passive/incapable.
        /// </summary>
        public int ComputeReactiveDefenseBonus(AttackDuel duel)
        {
            if (!duel.HasAttackRaw || !duel.HasDefenseRaw) return 0;
            if (!duel.DefenderWantsToDefend || duel.IsDefenderIncapacitated || !duel.CanDefenderReact) return 0;
            int need = duel.AttackFinalRoll.Total - duel.DefenseRawRoll.Total + 1;
            if (need <= 0) return 0;
            return Math.Min(need, Math.Max(0, duel.Defender.CurrentActionPoints));
        }

        private static DiceRollResult WithAddedBonus(DiceRollResult raw, int baseMod, int bonusPA)
        {
            int total = raw.RawRoll + baseMod + bonusPA;
            int diffVsDc = total - raw.TargetDC;
            bool success = (diffVsDc >= 0) || raw.IsCriticalSuccess;
            if (raw.IsCriticalFailure) success = false;
            return new DiceRollResult
            {
                DieType = raw.DieType,
                RawRoll = raw.RawRoll,
                Modifier = baseMod + bonusPA,
                Total = total,
                TargetDC = raw.TargetDC,
                Differential = diffVsDc,
                IsSuccess = success,
                IsCriticalSuccess = raw.IsCriticalSuccess,
                IsCriticalFailure = raw.IsCriticalFailure
            };
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
            SkillType attackSkill,
            SkillType defenseSkill)
        {
            // --- Séquence officielle : base -> jet attaquant -> PA attaquant post-tirage ->
            // --- jet défenseur -> PA défenseur post-tirage -> résolution normale.
            var duel = BeginDuel(
                attacker, defender, targetedPart,
                attackDie, attackModifier, defenseDie, defenseModifier,
                weaponBaseDamage, cancelPenaltyWithAP, defenderWantsToDefend,
                defenderArmor, attackSkill, defenseSkill, out string error);
            if (duel == null)
            {
                return new DamageResult
                {
                    IsHit = false,
                    IsBlocked = false,
                    CombatLog = error
                };
            }

            RollAttackerRaw(duel);
            int appliedAtt = AddAttackerBonusPA(duel, attackerBonusAP);

            RollDefenderRaw(duel);
            int appliedDef = 0;
            if (duel.DefenderWantsToDefend && !duel.IsDefenderIncapacitated && duel.CanDefenderReact)
            {
                appliedDef = AddDefenderBonusPA(duel, defenderBonusAP);
            }

            // Pour la traçabilité des logs, on conserve les bonus réellement débités.
            duel.AttackerBonusPAApplied = appliedAtt;
            duel.DefenderBonusPAApplied = appliedDef;

            return ResolveDuelDamage(duel);
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
            var attackRaw = duel.AttackRawRoll;
            int safeAttackerBonus = duel.AttackerBonusPAApplied;
            int actualDefenderBonus = duel.DefenderBonusPAApplied;

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
                    : "<b>DÉFENSE PASSIVE (0 PA engagé, aucune parade)</b>";
                defRollStr = inCapReason;
            }
            else
            {
                defenseRoll = duel.DefenseFinalRoll;
                differential = attackRoll.Total - defenseRoll.Total;

                string statusDefInfo = defender.GetStatusBreakdownString(defenseSkill, isOffensive: false);

                string defPaText;
                if (duel.CanDefenderReact)
                {
                    defPaText = actualDefenderBonus > 0 ? $" + PA {actualDefenderBonus} (après tirage)" : " + PA 0 (après tirage)";
                }
                else
                {
                    defPaText = $" {cfg.UnreactiveDefensePenalty} (0 PA)";
                }
                string critDefText = defenseRoll.IsCriticalSuccess ? " <color=#00E5FF>[CRITIQUE !]</color>" : "";
                string defStatusDetail = !string.IsNullOrEmpty(statusDefInfo) ? $" {statusDefInfo}" : " 0";
                // Affiche brut puis final pour prouver l'injection post-tirage.
                defRollStr = $"{SkillDefinitions.GetDisplayName(defenseSkill)} : {duel.DefenseDie} [Tirage {duel.DefenseRawRoll.RawRoll} + États {duel.DefenseStatusMod} ({defStatusDetail}){defPaText} = Total {defenseRoll.Total}]{critDefText}";
            }

            string statusAttInfo = attacker.GetStatusBreakdownString(attackSkill, isOffensive: true);
            string statusAttDetail = !string.IsNullOrEmpty(statusAttInfo) ? statusAttInfo : "0";
            string aimText = duel.CancelPenaltyWithAP ? "Visée compensée (+1 PA base)" : $"Malus Visée {targetInfo.DifficultyModifier}";
            string paAttText = safeAttackerBonus > 0 ? $" + PA {safeAttackerBonus} (après tirage)" : " + PA 0 (après tirage)";
            string critAttText = attackRoll.IsCriticalSuccess ? " <color=#FFE600>[CRITIQUE !]</color>" : "";
            string entraveText = duel.AppliedCanonEntrave
                ? (duel.CancelPenaltyWithAP ? " [Canon Entravé : Annulé (+1 PA)]" : " [Canon Entravé : -2 Tir]")
                : (duel.IsMartialArtsStrike ? " [Arts Martiaux : Exemption Totale]" : "");

            int normalRef = (attacker.Attributes.Force + attacker.Attributes.Agilite + 1) / 2;
            string augText = (attacker.IsMagicAugmented(attackSkill, true)
                    && attacker.Attributes.Magie > normalRef)
                ? $" [Corps Augmenté : MAG {attacker.Attributes.Magie} (+{SkillDefinitions.CharacteristicSteps(attacker.Attributes.Magie)} paliers)]"
                : "";
            string attackRollStr = $"{SkillDefinitions.GetDisplayName(attackSkill)} : {duel.AttackDie} [Tirage {attackRaw.RawRoll} + États {duel.AttackStatusMod} ({statusAttDetail}) + ({aimText}){paAttText}{entraveText} = Total {attackRoll.Total}]{augText}{critAttText}";

            // Séquences explicites phase par phase (traçabilité Livre VI §24.1).
            string phaseAtt = $"Phase 1 — Attaque : {attacker.Name} désigne {defender.Name} ({targetInfo.DisplayName}), paie {duel.BaseAttackCost} PA, lance {duel.AttackDie} → brut {attackRaw.Total}, PA post-tirage +{safeAttackerBonus} → final {attackRoll.Total}.";
            string phaseDef = !effectiveDefenderWantsToDefend
                ? $"Phase 2 — Défense : {defender.Name} ne se défend pas → défense 0."
                : (duel.CanDefenderReact
                    ? $"Phase 2 — Défense : {defender.Name} paie 1 PA, lance {duel.DefenseDie} → brut {duel.DefenseRawRoll.Total}, PA post-tirage +{actualDefenderBonus} → final {defenseRoll.Total}."
                    : $"Phase 2 — Défense : {defender.Name} sans réaction (0 PA) → {cfg.UnreactiveDefensePenalty} réflexe, total {defenseRoll.Total}.");

            // 4. Résolution du Différentiel
            if (effectiveDefenderWantsToDefend && differential < 0)
            {
                string blockLog = $"🛡️ <b>PARADE / ESQUIVE</b> : {attacker.Name} attaque, mais {defender.Name} neutralise l'assaut !\n";
                blockLog += $"   {phaseAtt}\n";
                blockLog += $"   {phaseDef}\n";
                blockLog += $"   🎲 <b>Compétences :</b> Attaque {attackRollStr} vs Défense {defRollStr}\n";
                blockLog += $"   ⚖️ <b>Différentiel Net :</b> <color=#00E5FF>{differential}</color> ➔ <b>0 dégât infligé</b> (Attaque neutralisée).";

                return new DamageResult
                {
                    IsHit = false,
                    IsBlocked = true,
                    AttackRoll = attackRoll,
                    DefenseRoll = defenseRoll,
                    Differential = differential,
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
            log += $"   🎲 <b>Compétences :</b> Attaque {attackRollStr} vs Défense {defRollStr} ➔ <b>Différentiel Net : <color=#00E5FF>{(differential > 0 ? $"+{differential}" : "0")}</color></b>\n";
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

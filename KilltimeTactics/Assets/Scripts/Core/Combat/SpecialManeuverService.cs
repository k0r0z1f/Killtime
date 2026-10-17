using System;
using Killtime.Core.Character;
using Killtime.Core.Dice;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Visibility;

namespace Killtime.Core.Combat
{
    /// <summary>
    /// Manœuvres spéciales : guet RD-030, opportunités RD-031, tenir/retarder/
    /// canalisation RD-048, lutte RD-039, bousculade RD-040, assaut groupé et
    /// poursuite RD-084. Compose setup + résolution, jamais l'inverse.
    /// Corps copiés à l'identique depuis l'ancien CombatCalculator.
    /// </summary>
    public sealed class SpecialManeuverService
    {
        private readonly CombatDuelContext _ctx;
        private readonly DuelSetupService _setup;
        private readonly DuelResolutionService _resolution;
        private readonly CombatProgressionService _progression;

        public SpecialManeuverService(
            CombatDuelContext ctx,
            DuelSetupService setup,
            DuelResolutionService resolution,
            CombatProgressionService progression)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            _setup = setup ?? throw new ArgumentNullException(nameof(setup));
            _resolution = resolution ?? throw new ArgumentNullException(nameof(resolution));
            _progression = progression ?? throw new ArgumentNullException(nameof(progression));
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

            var duel = _setup.BeginSkillDuel(
                watcher, target, targetedPart,
                attackSkill, SkillType.Esquive,
                weaponBaseDamage, false, true,
                null, null, target.BaseArmorAbsorption,
                out _, cover, weaponBonusEc, true, elevationAttackMod, elevationLabel);
            if (duel == null) return null;

            _setup.DeclareAttackerStakes(duel, 0, 0);
            _setup.AutoDeclareDefenderStakes(duel);
            var result = _resolution.ResolveBlindDuel(duel);
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

            var duel = _setup.BeginSkillDuel(
                reactor, target, targetedPart,
                attackSkill, SkillType.Esquive,
                weaponBaseDamage, false, true,
                null, null, target.BaseArmorAbsorption,
                out _, CoverType.None, 0, true, elevationAttackMod, elevationLabel);
            if (duel == null) return null;

            _setup.DeclareAttackerStakes(duel, 0, 0);
            _setup.AutoDeclareDefenderStakes(duel);
            var result = _resolution.ResolveBlindDuel(duel);
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
            var opposed = _resolution.ResolveOpposedCheck(
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
        // RD-048 : RETARDER / TENIR / CANALISATION (Livre VI — Tours).
        // - Tenir : 0 PA, passe en attente (interruption ultérieure).
        // - Retarder : 0 PA, initiative -pénalité (réinsertion par TurnManager).
        // - Canalisation : coût config, +2 au jet à l'attaque, -1 en défense,
        //   interrompue si dégât net > encaissement.
        // =====================================================================

        /// <summary>
        /// Tient son action : passe en attente (coût config, 0 PA par défaut).
        /// </summary>
        public bool TryHoldAction(CharacterStats unit, out string error)
        {
            var cfg = Rules.CoreRulesConfig.Instance;
            return DelayedTurnState.TryHold(unit, cfg != null ? cfg.HoldAPCost : 0, out error);
        }

        /// <summary>
        /// Retarde son tour : initiative réduite de la pénalité (config, -2).
        /// La réinsertion dans l'ordre est effectuée par le TurnManager.
        /// </summary>
        public bool TryDelayInitiative(CharacterStats unit, out int newTotal, out string error)
        {
            var cfg = Rules.CoreRulesConfig.Instance;
            return DelayedTurnState.TryDelay(unit, cfg != null ? cfg.DelayInitiativePenalty : 2, out newTotal, out error);
        }

        /// <summary>
        /// Entre en canalisation / action en progression (coût config, 1 PA).
        /// </summary>
        public bool TryBeginChanneling(CharacterStats unit, string label, out string error)
        {
            var cfg = Rules.CoreRulesConfig.Instance;
            return ChannelingState.TryBeginChannel(unit, label, cfg != null ? cfg.ChannelStartAPCost : 1, out error);
        }

        /// <summary>
        /// Vérifie l'interruption de canalisation après des dégâts nets
        /// (après armure/bouclier) : vrai si la canalisation est rompue.
        /// </summary>
        public bool CheckChannelInterruption(CharacterStats defender, int finalDamageApplied, out string interruptedLabel)
        {
            return ChannelingState.NotifyDamage(defender, finalDamageApplied, out interruptedLabel);
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

        // =====================================================================
        // RD-084 : ATTAQUES COMBINÉES DE GROUPE (Livre VI §25.2) & POURSUITE (§25.1)
        // =====================================================================

        /// <summary>
        /// RD-084 : Attaque combinée de groupe (Livre VI §25.2). Les participants
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
            bool defenderAutoStakes = false)
        {
            System.Collections.Generic.List<int> uniform = null;
            if (attackers != null)
            {
                uniform = new System.Collections.Generic.List<int>(attackers.Count);
                for (int i = 0; i < attackers.Count; i++) uniform.Add(weaponBaseDamagePerAttacker);
            }
            return ResolveCombinedAttack(attackers, defender, targetedPart, attackSkill, defenseSkill,
                uniform, attackerBonusAP, attackerPE, defenderBonusAP, defenderPE,
                defenderWantsToDefend, defenderAutoStakes);
        }

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
            bool defenderAutoStakes = false)
        {
            var result = new CombinedAttackResult
            {
                IsValid = false,
                Defender = defender,
                TargetedPart = targetedPart,
                AttackSkill = attackSkill,
                DefenseSkill = defenseSkill
            };
            var cfg = Rules.CoreRulesConfig.Instance;
            // Livre VI §25.2 : « Deux combattants ou plus » — plancher à 2
            // même si la config est abaissée (évite les groupes vides/dégénérés).
            int minMembers = cfg != null ? Math.Max(2, cfg.CombinedAttackMinMembers) : 2;
            int baseCost = cfg != null ? cfg.CombinedAttackAPCost : 2;

            if (attackers == null || attackers.Count < minMembers)
            {
                result.Error = $"Attaque combinée impossible : {minMembers} attaquants minimum requis.";
                result.CombatLog = $"⚠️ {result.Error}";
                return result;
            }
            if (defender == null || !defender.IsAlive)
            {
                result.Error = "Attaque combinée impossible : cible hors de combat.";
                result.CombatLog = $"⚠️ {result.Error}";
                return result;
            }

            var members = new System.Collections.Generic.List<CharacterStats>(attackers.Count);
            for (int i = 0; i < attackers.Count; i++)
            {
                var a = attackers[i];
                if (a == null || !a.IsAlive || a == defender)
                {
                    result.Error = $"Attaque combinée impossible : participant invalide ({(a == null ? "null" : a.Name)}).";
                    result.CombatLog = $"⚠️ {result.Error}";
                    return result;
                }
                members.Add(a);
            }

            if (!GroupAssaultState.TryValidateGroup(members, baseCost, minMembers, out string groupError))
            {
                result.Error = groupError;
                result.CombatLog = $"⚠️ {groupError}";
                return result;
            }

            // Synchronisation sur le membre le plus lent (traçabilité TurnManager).
            result.SlowestInitiative = GroupAssaultState.RegisterSynchronizedGroup(members);
            result.SlowestMember = GroupAssaultState.GetSlowestMember(members);
            result.Attackers.AddRange(members);

            // Coûts de base débités aussitôt (un par participant).
            for (int i = 0; i < members.Count; i++)
            {
                if (!members[i].ConsumeActionPoints(baseCost))
                {
                    result.Error = $"{members[i].Name} n'a plus assez de PA pour frapper à l'unisson !";
                    result.CombatLog = $"⚠️ {result.Error}";
                    return result;
                }
            }

            // Épreuves offensives individuelles (mises aveugles par participant).
            int summedAttack = 0;
            int summedBase = 0;
            for (int i = 0; i < members.Count; i++)
            {
                var a = members[i];
                DiceType die = a.GetSkillDie(attackSkill, true);
                int status = a.GetStatusModifier(attackSkill, isOffensive: true);
                int bAP = (attackerBonusAP != null && i < attackerBonusAP.Count) ? Math.Max(0, attackerBonusAP[i]) : 0;
                int bPE = (attackerPE != null && i < attackerPE.Count) ? Math.Max(0, attackerPE[i]) : 0;
                int paidPA = CombatDuelShared.SpendBonusPA(a, bAP);
                int paidPE = a.SpendDuelPE(bPE);
                int committed = paidPA + paidPE * cfg.DuelPEBonusPerPoint;
                var roll = _ctx.DiceRoller.Roll(die, status + committed, cfg.StandardTargetDC);
                result.AttackRolls.Add(roll);
                summedAttack += roll.Total;
                int wBase = (weaponBaseDamages != null && i < weaponBaseDamages.Count) ? weaponBaseDamages[i] : 5;
                summedBase += Math.Max(0, wBase);
            }
            result.SummedAttackTotal = summedAttack;
            result.SummedWeaponBase = summedBase;

            // Unique défense de la cible (réaction de base + mises, ou passif à 0).
            bool wants = defenderWantsToDefend && defender.CanDefendActively();
            bool basePaid = false;
            int defCommitted = 0;
            DiceType defDie = defender.GetSkillDie(defenseSkill);
            int defStatus = defender.GetStatusModifier(defenseSkill, isOffensive: false);
            if (!SkillDefinitions.IsExclusivelyDefensive(defenseSkill)
                && !SkillDefinitions.HasDefensiveSpecialization(defender.Sheet, defenseSkill))
            {
                defDie = SkillDefinitions.StepDownDie(defDie);
            }
            if (wants)
            {
                if (defender.ConsumeActionPoints(cfg.BaseReactionAPCost))
                {
                    basePaid = true;
                    if (defenderAutoStakes)
                    {
                        int expectedDef = (int)Math.Round(CombatDuelShared.DieAverage(defDie)) + defStatus;
                        int need = Math.Max(0, summedAttack - expectedDef + 1);
                        int affordable = Math.Max(0, defender.CurrentActionPoints);
                        defCommitted = CombatDuelShared.SpendBonusPA(defender, Math.Min(need, affordable));
                    }
                    else
                    {
                        int paidPA = CombatDuelShared.SpendBonusPA(defender, Math.Max(0, defenderBonusAP));
                        int paidPE = defender.SpendDuelPE(Math.Max(0, defenderPE));
                        defCommitted = paidPA + paidPE * cfg.DuelPEBonusPerPoint;
                    }
                }
                else
                {
                    wants = false;
                }
            }

            DiceRollResult defRoll;
            if (wants && basePaid)
            {
                int penalty = 0;
                if (ChargeState.HasDefensePenalty(defender)) penalty += cfg.ChargeDefensePenalty;
                // RD-048 : punition de progression en canalisation.
                penalty += ChannelingState.GetDefenseModifier(defender);
                defRoll = _ctx.DiceRoller.Roll(defDie, defStatus + penalty + defCommitted, cfg.StandardTargetDC);
                result.DefenderDefended = true;
            }
            else
            {
                defRoll = new DiceRollResult
                {
                    DieType = defDie, RawRoll = 0, Modifier = 0, Total = 0,
                    TargetDC = cfg.StandardTargetDC, Differential = -cfg.StandardTargetDC,
                    IsSuccess = false, IsCriticalSuccess = false, IsCriticalFailure = false
                };
                result.DefenderDefended = false;
            }
            result.DefenseRoll = defRoll;
            result.UniqueDefenseTotal = result.DefenderDefended ? defRoll.Total : 0;

            int differential = result.DefenderDefended
                ? summedAttack - defRoll.Total
                : summedAttack;
            result.Differential = differential;
            result.IsHit = differential >= 0;

            if (!result.IsHit)
            {
                string blockLog = $"🛡️ <b>ASSAUT GROUPÉ PARÉ</b> : {members.Count} assaillants synchronisés (lenteur : {(result.SlowestMember != null ? result.SlowestMember.Name : "?")}, ini {result.SlowestInitiative}) mais {defender.Name} neutralise la sommation !\n";
                blockLog += $"   ⚔️ Sommation : {summedAttack} vs Défense unique {defRoll.Total} ➔ <b>Différentiel {differential}</b> — <b>0 dégât</b>.";
                blockLog += _progression.ApplySkillProgression(defender, defenseSkill, false);
                for (int i = 0; i < members.Count; i++) StealthState.BreakStealth(members[i]);
                result.CombatLog = blockLog;
                result.FatalResolution = FatalBlowResolution.None;
                result.IsValid = true;
                return result;
            }

            // Dépassement massif : armure + bouclier appliqués UNE SEULE FOIS.
            int rawDamage = summedBase + Math.Max(0, differential);
            int totalArmor = defender.BaseArmorAbsorption + defender.GetWornArmorBonus();
            int absorbed = Math.Min(totalArmor, rawDamage);
            int shieldRemainder = defender.AbsorbShield(Math.Max(0, rawDamage - absorbed));
            int shieldAbsorbed = Math.Max(0, Math.Max(0, rawDamage - absorbed) - shieldRemainder);
            int finalDamage = Math.Max(0, shieldRemainder);
            result.RawDamage = rawDamage;
            result.ArmorAbsorbed = absorbed;
            result.ShieldAbsorbed = shieldAbsorbed;
            result.FinalDamageApplied = finalDamage;

            bool exceeded = finalDamage > defender.EncaissementThreshold;
            result.ExceededEncaissement = exceeded;
            StatusEffect inflicted = StatusEffect.None;
            // RD-048 : capture avant statuts (purge silencieuse par K.O. sinon).
            bool combinedWasChanneling = ChannelingState.IsChanneling(defender);
            string combinedPriorLabel = null;
            if (combinedWasChanneling) ChannelingState.TryGetLabel(defender, out combinedPriorLabel);
            if (exceeded)
            {
                inflicted = _progression.DetermineInflictedStatus(targetedPart);
                defender.ActiveStatus |= inflicted;
            }
            result.InflictedStatus = inflicted;

            FatalBlowResolution fatalRes = FatalBlowResolution.None;
            if (defender.CurrentHealth - finalDamage <= 0)
            {
                fatalRes = defender.EvaluateFatalBlow(targetedPart, finalDamage);
            }
            else
            {
                defender.CurrentHealth -= finalDamage;
            }
            result.FatalResolution = fatalRes;

            MoraleCheckResult moraleRes = default;
            if (defender.IsAlive && !defender.HasTestedMorale && defender.CurrentHealth > 0)
            {
                moraleRes = defender.CheckMoraleAtThreshold(_ctx.DiceRoller, cfg.StandardTargetDC);
            }
            result.MoraleResult = moraleRes;

            // RD-048 : la sommation interrompt aussi la canalisation (dégât net > encaissement).
            bool combinedChannelInterrupted = false;
            string combinedChannelLabel = null;
            if (combinedWasChanneling)
            {
                if (ChannelingState.IsChanneling(defender))
                {
                    if (!defender.IsAlive || exceeded)
                    {
                        if (ChannelingState.NotifyDamage(defender, finalDamage, out combinedChannelLabel))
                            combinedChannelInterrupted = true;
                    }
                }
                else
                {
                    combinedChannelInterrupted = true;
                    combinedChannelLabel = combinedPriorLabel;
                    ChannelingState.Cancel(defender);
                }
            }
            result.ChannelInterrupted = combinedChannelInterrupted;
            result.InterruptedChannelLabel = combinedChannelLabel;

            var info = BodyPartInfo.GetInfo(targetedPart);
            string log = $"⚔️ <b>ASSAUT GROUPÉ ({members.Count})</b> : synchronisés sur {(result.SlowestMember != null ? result.SlowestMember.Name : "?")} (initiative la plus lente : {result.SlowestInitiative}) ➔ {defender.Name} ({info.DisplayName})\n";
            for (int i = 0; i < members.Count; i++)
            {
                var r = result.AttackRolls[i];
                log += $"   🗡️ {members[i].Name} : {attackSkill} [{r.DieType} tirage {r.RawRoll} + mod {r.Modifier} = <b>{r.Total}</b>]\n";
            }
            log += $"   ➕ <b>SOMMATION OFFENSIVE : {summedAttack}</b> vs <b>DÉFENSE UNIQUE : {(result.DefenderDefended ? defRoll.Total.ToString() : "0 (passif)")}</b> ➔ <b>Différentiel +{differential}</b>\n";
            log += $"   ⚔️ Dégâts : [{summedBase} (armes cumulées) + {Math.Max(0, differential)} (dépassement) = {rawDamage} bruts] − [Armure {totalArmor} (1×, absorbé {absorbed})]";
            if (shieldAbsorbed > 0) log += $" − [🔮Bouclier {shieldAbsorbed}]";
            log += $" ➔ <b><color=#FF3B5C>{finalDamage} Dégâts Nets</color></b>\n";
            log += $"   ❤️ Vitalité {defender.Name} : ➔ <b>{defender.CurrentHealth}/{defender.MaxHealth} PV</b>";
            if (exceeded) log += $" | ⚡ <b>CHOC TRAUMATIQUE</b> (> Encaissement {defender.EncaissementThreshold}) ➔ [{inflicted}]";
            if (combinedChannelInterrupted) log += $" | ⏳ <b>CANALISATION INTERROMPUE</b> ({finalDamage} dégâts nets &gt; encaissement {defender.EncaissementThreshold}) : « {combinedChannelLabel} » perdue !";
            if (fatalRes == FatalBlowResolution.InstantDeath) log += " | 💀 <b>MORT INSTANTANÉE</b>";
            else if (fatalRes == FatalBlowResolution.ForcedUnconscious) log += " | 💥 <b>SYNCOPE TRAUMATIQUE (K.O.)</b>";
            else if (fatalRes == FatalBlowResolution.EligibleForLastBreath) log += " | ⚡ <b>0 PV (Sombrer ou Dernier Souffle)</b>";
            if (moraleRes.Triggered) log += "\n   " + moraleRes.Log;
            for (int i = 0; i < members.Count; i++)
            {
                log += _progression.ApplySkillProgression(members[i], attackSkill, true);
                StealthState.BreakStealth(members[i]);
            }
            result.CombatLog = log;
            result.IsValid = true;
            return result;
        }

        /// <summary>
        /// RD-084 : Round de course-poursuite (Livre VI §25.1). La proie effectue
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
            System.Collections.Generic.IList<int> pursuerPE = null)
        {
            var result = new PursuitRoundResult { Prey = prey };
            var cfg = Rules.CoreRulesConfig.Instance;
            PursuitState.FailuresToCapture = cfg != null ? cfg.PursuitFailuresToCapture : 4;
            PursuitState.MinKeptDistance = cfg != null ? cfg.PursuitMinDistance : 1;
            result.FailuresToCapture = PursuitState.FailuresToCapture;

            var alive = new System.Collections.Generic.List<CharacterStats>();
            if (pursuers != null)
            {
                for (int i = 0; i < pursuers.Count; i++)
                {
                    var p = pursuers[i];
                    if (p != null && p != prey && p.IsAlive) alive.Add(p);
                }
            }

            if (prey == null || !prey.IsAlive)
            {
                result.CombatLog = "⚠️ Poursuite impossible : proie hors de combat.";
                result.TotalFailures = 0;
                return result;
            }
            if (alive.Count == 0)
            {
                result.CombatLog = $"⚠️ Poursuite impossible : aucun poursuivant valide pour {prey.Name}.";
                result.TotalFailures = PursuitState.GetFailures(prey);
                return result;
            }

            // Unique épreuve de la proie (notée puis opposée à chacun).
            DiceType preyDie = prey.GetSkillDie(SkillType.Athletisme, true);
            int preyStatus = prey.GetStatusModifier(SkillType.Athletisme, isOffensive: true);
            int preyPA = CombatDuelShared.SpendBonusPA(prey, Math.Max(0, preyBonusAP));
            int preyPEPaid = prey.SpendDuelPE(Math.Max(0, preyPE));
            int preyCommitted = preyPA + preyPEPaid * cfg.DuelPEBonusPerPoint;
            var preyRoll = _ctx.DiceRoller.Roll(preyDie, preyStatus + preyCommitted, cfg.StandardTargetDC);
            result.PreyRoll = preyRoll;

            PursuitState.TrackPursuers(prey, alive);
            int added = 0;
            string log = $"🏃 <b>COURSE-POURSUITE</b> : {prey.Name} fuit ({alive.Count} poursuivant(s)) — Athlétisme vs Athlétisme\n";
            log += $"   🦌 Proie {prey.Name} : [d{preyDie} tirage {preyRoll.RawRoll} + mod {preyRoll.Modifier} = <b>{preyRoll.Total}</b>]\n";

            for (int i = 0; i < alive.Count; i++)
            {
                var p = alive[i];
                DiceType pDie = p.GetSkillDie(SkillType.Athletisme, true);
                int pStatus = p.GetStatusModifier(SkillType.Athletisme, isOffensive: true);
                int paidPA = 0;
                int paidPE = 0;
                if (pursuersAutoStakes)
                {
                    int expectedPrey = preyRoll.Total;
                    int expectedSelf = (int)Math.Round(CombatDuelShared.DieAverage(pDie)) + pStatus;
                    int need = Math.Max(0, expectedPrey - expectedSelf + 1);
                    paidPA = CombatDuelShared.SpendBonusPA(p, Math.Min(need, Math.Max(0, p.CurrentActionPoints)));
                }
                else
                {
                    int wantPA = (pursuerBonusAP != null && i < pursuerBonusAP.Count) ? Math.Max(0, pursuerBonusAP[i]) : 0;
                    int wantPE = (pursuerPE != null && i < pursuerPE.Count) ? Math.Max(0, pursuerPE[i]) : 0;
                    paidPA = CombatDuelShared.SpendBonusPA(p, wantPA);
                    paidPE = p.SpendDuelPE(wantPE);
                }
                int committed = paidPA + paidPE * cfg.DuelPEBonusPerPoint;
                var pRoll = _ctx.DiceRoller.Roll(pDie, pStatus + committed, cfg.StandardTargetDC);
                int diff = preyRoll.Total - pRoll.Total;
                bool preyWins = diff >= 0;

                var entry = new PursuitDuelEntry
                {
                    Pursuer = p,
                    PursuerRoll = pRoll,
                    Differential = diff,
                    PreyWins = preyWins
                };
                result.Entries.Add(entry);

                if (preyWins)
                {
                    PursuitState.RegisterSuccess(prey, p);
                    log += $"   ✅ vs {p.Name} [{pRoll.Total}] : Diff {diff:+0;-0;0} — <b>DISTANCE MAINTENUE</b> (bloqué à ≥ {PursuitState.MinKeptDistance} case).\n";
                }
                else
                {
                    PursuitState.RegisterFailure(prey, p);
                    added++;
                    log += $"   ❌ vs {p.Name} [{pRoll.Total}] : Diff {diff:+0;-0;0} — <b>TERRAIN GAGNÉ</b> par le poursuivant ! (faillites : {PursuitState.GetFailures(prey)}/{PursuitState.FailuresToCapture})\n";
                }
                log += _progression.ApplyOpposedProgression(prey, SkillType.Athletisme, true, p, SkillType.Athletisme, true, diff);
            }

            result.FailuresAdded = added;
            result.TotalFailures = PursuitState.GetFailures(prey);
            result.Caught = PursuitState.IsCaught(prey);
            if (result.Caught)
            {
                log += $"   🛑 <b>RÈGLE D'ARRÊT AUX {PursuitState.FailuresToCapture} FAILLITES</b> : {prey.Name} est <b>DÉFINITIVEMENT RATTRAPÉ(E) ET ACCULÉ(E)</b> — arrêt et capture immédiate ! [Immobilisé]";
            }
            result.CombatLog = log;
            return result;
        }
    }
}

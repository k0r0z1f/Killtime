using System;
using Killtime.Core.Character;
using Killtime.Core.Dice;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Visibility;

namespace Killtime.Core.Combat
{
    /// <summary>
    /// Réactions RD-030/031 + tours RD-048 (guet, opportunité, tenir/retarder/canalisation) — extrait sans modification depuis SpecialManeuverService.cs.
    /// </summary>
    public sealed partial class SpecialManeuverService
    {
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
        // vs Athlétisme (Livre V §21.1, course-poursuite) : à l'attaquant (bloqueur).
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
        /// (À Terre + Ralenti en cas de choc, Livre V §22). Même coût qu'une frappe.
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
        // RD-048 : RETARDER / TENIR / CANALISATION (Livre V — Tours).
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
        /// RD-202 Préparation d'Action (Livre V §20.3) : 1 PA -> +2 au jet de
        /// l'action ou de la défense suivante (attaque OU parade). One-shot :
        /// consommée au jet suivant, sans malus, sans persistance, sans
        /// interruption. Ne cumule pas avec la canalisation.
        /// </summary>
        public bool TryBeginPreparation(CharacterStats unit, out string error)
        {
            var cfg = Rules.CoreRulesConfig.Instance;
            return PreparationState.TryPrepare(unit, cfg != null ? cfg.PreparationAPCost : 1, out error);
        }

        /// <summary>
        /// RD-202 Dégagement (Livre V §20.3) : déplacement de fuite suivant
        /// immédiatement une attaque ou une parade résolue (+1 PA de surcharge,
        /// trajet payé à part). Refuse sans fenêtre (aucune action résolue),
        /// sans PA ou neutralisé. DISTINCT du Décrochage RD-031 (préventif,
        /// sans réaction) : la fuite peut provoquer.
        /// </summary>
        public bool TryGetAwayMove(CharacterStats unit, out string error)
        {
            var cfg = Rules.CoreRulesConfig.Instance;
            return GetAwayState.TryGetAwayMove(unit, cfg != null ? cfg.GetAwayExtraAPCost : 1, out error);
        }

        /// <summary>
        /// Vérifie l'interruption de canalisation après des dégâts nets
        /// (après armure/bouclier) : vrai si la canalisation est rompue.
        /// </summary>
        public bool CheckChannelInterruption(CharacterStats defender, int finalDamageApplied, out string interruptedLabel)
        {
            return ChannelingState.NotifyDamage(defender, finalDamageApplied, out interruptedLabel);
        }
    }
}

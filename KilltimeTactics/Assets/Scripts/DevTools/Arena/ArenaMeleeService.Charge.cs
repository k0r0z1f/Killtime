using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Audio;
using Killtime.Core.Character;
using Killtime.Core.Combat;
using Killtime.Core.Dice;
using Killtime.Core.Inventory;
using Killtime.Core.Rules;
using Killtime.UI;

namespace Killtime.Tactics.Arena
{
    /// <summary>
    /// Charge d'assaut RD-038 (manoeuvre + frappe).
    /// Split move-only : contenu verbatim depuis ArenaMeleeService.cs.
    /// </summary>
    public sealed partial class ArenaMeleeService
    {
        /// <summary>
        /// Exécute une manœuvre de Charge (RD-038, Livre VI) :
        /// - Déplacement d'au moins 3 cases vers un voisin libre de la cible.
        /// - Débite les PA de déplacement (+ surcoût éventuel de manœuvre).
        /// - À l'arrivée au contact, exécute immédiatement l'attaque de mêlée (+2 dégâts, -1 défense jusqu'au prochain tour personnel).
        /// - Bloque le tir pour le reste du tour en cours.
        /// </summary>
        public void ExecuteChargeAttack(TacticalUnit actor, TacticalUnit target, BodyPart targetedPart, int attackerBonusAP = 0, int attackerPE = 0)
        {
            if (actor == null || target == null || actor == target) return;
            if (actor.Stats == null || !actor.Stats.IsAlive || target.Stats == null || !target.Stats.IsAlive) return;

            if (_ctx.Guard.IsResolving)
            {
                _host.ArenaLog("⚠️ Charge ignorée : une action est déjà en cours.");
                return;
            }

            int attackCost = CoreRulesConfig.Instance != null ? CoreRulesConfig.Instance.BaseAttackAPCost : 2;
            int ap = actor.Stats.CurrentActionPoints;
            var pathfinder = _ctx.Pathfinder;
            if (pathfinder == null) return;

            int minDistance = CoreRulesConfig.Instance != null ? CoreRulesConfig.Instance.ChargeMinDistance : 3;
            var path = pathfinder.FindChargePath(actor.CurrentCoords, target.CurrentCoords, ap, attackCost, out int moveCost, minDistance, actor.FootprintType);
            if (path == null || path.Count <= 1 || (path.Count - 1) < minDistance)
            {
                _host.ArenaLog($"⚠️ <b>Charge impossible</b> : aucun chemin de {minDistance}+ cases disponible vers {target.Stats.Name} pour {actor.Stats.Name}.");
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayUI(SoundId.UI_Denied, 0.6f);
                return;
            }

            int extraCost = CoreRulesConfig.Instance != null ? CoreRulesConfig.Instance.ChargeManeuverExtraAPCost : 0;
            int effectiveCost = actor.ComputeMovementAPCost(moveCost) + extraCost;
            if (actor.Stats.CurrentActionPoints < effectiveCost + attackCost)
            {
                _host.ArenaLog($"⚠️ <b>Charge impossible</b> : PA insuffisants ({actor.Stats.CurrentActionPoints} PA disponibles pour un coût de {effectiveCost + attackCost} PA).");
                return;
            }

            _host.ArenaLog($"⚡ <b>{actor.Stats.Name}</b> s'élance en <b>CHARGE D'ASSAUT</b> sur {target.Stats.Name} ({path.Count - 1} cases, coût déplacement {effectiveCost} PA) !");
            _host.ArenaStartCoroutine(ChargeAndStrikeRoutine(actor, target, targetedPart, path, effectiveCost, attackCost, attackerBonusAP, attackerPE));
        }

        private IEnumerator ChargeAndStrikeRoutine(
            TacticalUnit actor,
            TacticalUnit target,
            BodyPart targetedPart,
            List<HexCoordinates> path,
            int moveCost,
            int attackCost,
            int attackerBonusAP,
            int attackerPE)
        {
            var grid = _ctx.Grid;
            _ctx.Guard.TryEnter();
            if (KilltimeAudioManager.Instance != null)
                KilltimeAudioManager.Instance.PlayAt(SoundId.Move_Dash, actor.transform.position, 0.7f);

            // Exécute le déplacement jusqu'au contact
            yield return _host.ArenaStartCoroutine(actor.MoveAlongPath(path, grid, moveCost));

            // Attend la fin effective du mouvement
            while (actor.IsMoving)
            {
                yield return null;
            }

            _ctx.Guard.ForceReset();

            // Si l'attaquant a survécu et est toujours au contact de la cible
            if (actor.Stats != null && actor.Stats.IsAlive && target.Stats != null && target.Stats.IsAlive)
            {
                int dist = actor.CurrentCoords.DistanceTo(target.CurrentCoords);
                if (dist <= 1)
                {
                    var weapon = actor.Sheet?.GetEquippedWeapon();
                    SkillType meleeSkill = weapon != null
                        ? SkillDefinitions.ResolveBaseSkill(weapon.AssociatedSkill)
                        : SkillType.MainsNues;

                    ChargeStrike?.Invoke(actor, target, targetedPart, meleeSkill, attackerBonusAP, attackerPE);
                }
                else
                {
                    _host.ArenaLog($"⚠️ <b>Charge interrompue</b> : {actor.Stats.Name} n'a pas pu atteindre le contact de {target.Stats.Name}.");
                }
            }
        }
    }
}

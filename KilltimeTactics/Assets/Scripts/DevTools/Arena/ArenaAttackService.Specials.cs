using System;
using System.Collections;
using UnityEngine;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Audio;
using Killtime.Core.Character;
using Killtime.Core.Combat;
using Killtime.Core.Dice;
using Killtime.Core.Rules;
using Killtime.WebGL;
using Killtime.UI;
using Killtime.CameraSystem;
using Killtime.Tactics.AI;

namespace Killtime.Tactics.Arena
{
    /// <summary>
    /// Attaque de décor (ExecuteAttackOnObstacle) + grappin magnétique (ExecuteGrapnel).
    /// Split move-only : contenu verbatim depuis ArenaAttackService.cs.
    /// </summary>
    public sealed partial class ArenaAttackService
    {
        public bool ExecuteAttackOnObstacle(
            HexCoordinates targetCoords,
            SkillType attackSkill = SkillType.ManiementArmes,
            TacticalUnit explicitAttacker = null)
        {
            var turnManager = _ctx.TurnManager;
            var grid = _ctx.Grid;
            var visualizer = _ctx.Visualizer;
            if (_ctx.Guard.IsResolving) return false;
            var attacker = explicitAttacker != null ? explicitAttacker : (turnManager != null ? turnManager.ActiveUnit : _state.PlayerUnit);
            if (attacker == null || attacker.Stats == null || !attacker.Stats.IsAlive) return false;
            if (attacker.IsMoving) return false;

            int cost = CoreRulesConfig.Instance != null ? CoreRulesConfig.Instance.BaseAttackAPCost : 2;
            if (!_ctx.GetInfiniteAP() && attacker.Stats.CurrentActionPoints < cost)
            {
                _host.ArenaLog($"⚠️ PA insuffisants : attaque de décor = {cost} PA (reste {attacker.Stats.CurrentActionPoints}).");
                return false;
            }

            var weapon = attacker.Sheet?.GetEquippedWeapon();
            int dmg = weapon != null && weapon.BaseDamage > 0 ? weapon.BaseDamage : 5;
            bool isMelee = SkillDefinitions.IsMeleeAttackSkill(attackSkill) || (weapon != null && weapon.RangeInTiles <= 1);
            int dist = attacker.CurrentCoords.DistanceTo(targetCoords);
            int maxReach = weapon != null ? Mathf.Max(1, weapon.RangeInTiles) : 1;

            if (dist > maxReach)
            {
                _host.ArenaLog($"⚠️ Obstacle hors de portée ({dist} cases > max {maxReach}).");
                return false;
            }

            ObstacleDamageType dmgType = ObstacleDamageType.Perforant;
            if (isMelee)
            {
                dmgType = (attackSkill == SkillType.ManiementArmes && attacker.Stats.HasSpecialization("Marteau de Guerre"))
                    ? ObstacleDamageType.Contondant
                    : (attacker.Stats.HasSpecialization("Hache de Guerre") ? ObstacleDamageType.Tranchant : ObstacleDamageType.Contondant);
            }
            else if (weapon != null && weapon.Category.Contains("Laser"))
            {
                dmgType = ObstacleDamageType.Thermique;
            }

            if (!_ctx.GetInfiniteAP()) attacker.Stats.ConsumeActionPoints(cost);
            attacker.Stats.RegisterAttack();

            Vector3 dir = targetCoords.ToWorldPosition(grid != null ? grid.HexRadius : 1f, 0f) - attacker.transform.position;
            dir.y = 0f;
            if (dir != Vector3.zero) attacker.transform.rotation = Quaternion.LookRotation(dir);

            var attVis = attacker.GetComponent<TacticalUnitVisual>();
            attVis?.TriggerRoundkick();

            if (PropObstacleRegistry.ApplyDamage(targetCoords, dmg, dmgType, grid, visualizer, out var obsRes))
            {
                _host.ArenaLog($"🎯 <b>{attacker.Stats.Name}</b> attaque le décor en ({targetCoords.Q}, {targetCoords.R}) (-{cost} PA) :\n   {obsRes.Log}");
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayAt(SoundId.Trauma_Shock, attacker.transform.position, 0.7f);
                return true;
            }
            else
            {
                _host.ArenaLog($"🎯 <b>{attacker.Stats.Name}</b> frappe en ({targetCoords.Q}, {targetCoords.R}) : aucun obstacle destructible.");
                return false;
            }
        }

        public void ExecuteGrapnel(TacticalUnit attacker, TacticalUnit defender)
        {
            var turnManager = _ctx.TurnManager;
            var calculator = _ctx.Calculator;
            if (_ctx.Guard.IsResolving)
            {
                _host.ArenaLog("⚠️ Lancer ignoré : une résolution est déjà en cours.");
                return;
            }
            if (TurnManager.IsMultiplayerPlayerClient())
            {
                _host.ArenaLog("⚠️ Grappin réservé au GM en multijoueur (pas encore synchronisé VTT).");
                return;
            }
            if (attacker == null) attacker = turnManager != null ? turnManager.ActiveUnit : _state.PlayerUnit;
            if (attacker == null || attacker.Stats == null) { _host.ArenaLog("⚠️ Aucun attaquant pour le grappin."); return; }
            if (defender == null || defender.Stats == null || !defender.Stats.IsAlive) { _host.ArenaLog("⚠️ Aucune cible valide pour le grappin."); return; }
            if (attacker.IsMoving) { _host.ArenaLog($"⚠️ <b>{attacker.Stats.Name}</b> est en déplacement : attendez l'arrivée !"); return; }
            if (SmokeScreen.FindItemByName(attacker.Stats, "Grappin") == null)
            {
                _host.ArenaLog($"⚠️ <b>{attacker.Stats.Name}</b> n'a pas de Grappin Magnétique en poche !");
                return;
            }
            int dist = attacker.CurrentCoords.DistanceTo(defender.CurrentCoords);
            if (dist < 2 || dist > 4)
            {
                _host.ArenaLog($"⚠️ <b>Grappin impossible</b> : {dist} cases (portée 2-4).");
                return;
            }
            DiceType atkDie = attacker.Stats.GetSkillDie(SkillType.Ballistique, true);
            if (!attacker.Stats.CanAttack(atkDie)) { _host.ArenaLog($"⚠️ <b>{attacker.Stats.Name}</b> a épuisé son quota d'attaque ce tour !"); return; }
            if (!_ctx.GetInfiniteAP() && attacker.Stats.CurrentActionPoints < 2)
            {
                _host.ArenaLog($"⚠️ PA insuffisants : grappin = 2 PA (reste {attacker.Stats.CurrentActionPoints}).");
                return;
            }

            Vector3 dir = defender.transform.position - attacker.transform.position;
            dir.y = 0f;
            if (dir != Vector3.zero) attacker.transform.rotation = Quaternion.LookRotation(dir);
            attacker.GetComponent<TacticalUnitVisual>()?.TriggerGrenadeThrow();

            calculator.SetContactDistanceState(false);
            CoverType cover = _targeting.GetCoverToTarget(attacker, defender);
            var throwDuel = calculator.BeginSkillDuel(
                attacker.Stats, defender.Stats, BodyPart.Torse,
                SkillType.Ballistique, SkillType.Esquive, 3, false,
                true, null, null,
                defender.Stats.BaseArmorAbsorption, out string throwError, cover);
            if (throwDuel == null)
            {
                _host.ArenaLog(throwError ?? "⚠️ Grappin impossible : PA insuffisants.");
                return;
            }
            calculator.DeclareAttackerStakes(throwDuel, 0, 0);
            if (throwDuel.DefenderWantsToDefend && !throwDuel.IsDefenderIncapacitated && throwDuel.CanDefenderReact)
                calculator.AutoDeclareDefenderStakes(throwDuel);
            else
                calculator.DeclareDefenderStakes(throwDuel, SkillType.Esquive, false, 0, 0);
            var result = calculator.ResolveBlindDuel(throwDuel);
            attacker.Stats.RegisterAttack();

            _host.ArenaLog($"🪝 <b>HARPON</b> : {attacker.Stats.Name} harponne {defender.Stats.Name} ({dist} cases, -2 PA)\n   {result.CombatLog}");
            _overwatch.HandleChannelInterruptionFx(defender, result.ChannelInterrupted);
            var defVis = defender.GetComponent<TacticalUnitVisual>();
            if (result.IsHit) defVis?.TriggerHitFlash();
            if (result.IsHit && result.Differential >= 2)
            {
                defender.Stats.ApplyStatus(StatusEffect.ATerre, 1);
                defVis?.SpawnFloatingText("[À TERRE] happé !", Color.yellow);
                _host.ArenaLog($"🪝 Le grappin fait chuter <b>{defender.Stats.Name}</b> (<b>À Terre</b>, Diff +{result.Differential}).");
            }
            if (!defender.Stats.IsAlive) _host.RequestAutoTargetNextAlive();
            _host.RequestMusicUpdate();
            _host.RequestCombatOverCheck();
        }
    }
}

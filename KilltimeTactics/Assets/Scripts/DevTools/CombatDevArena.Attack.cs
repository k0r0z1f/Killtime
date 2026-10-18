using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Audio;
using Killtime.Core.Character;
using Killtime.Core.Combat;
using Killtime.Core.Dice;
using Killtime.Core.Chrono;
using Killtime.Core.Inventory;
using Killtime.Core.Rules;
using Killtime.CameraSystem;
using Killtime.WebGL;
using Killtime.UI;
using Killtime.Tactics.Arena;

namespace Killtime.Tactics
{
    // Passe d'armes, grenades, lancers, munitions (RD-152).
    public partial class CombatDevArena : MonoBehaviour, IArenaHost
    {
        public void ExecuteAttack(
            BodyPart targetedPart, 
            bool cancelPenaltyWithAP, 
            SkillType attackSkill = SkillType.Ballistique,
            SkillType defenseSkill = SkillType.Esquive,
            bool defenderWantsToDefend = true,
            string attackerSpecialization = null,
            string defenderSpecialization = null,
            int weaponBaseDamage = 5,
            int attackerBonusAP = 0,
            int defenderBonusAP = -1,
            DiceType? attackDie = null,
            DiceType? defenseDie = null,
            TacticalUnit explicitTarget = null,
            int attackerPE = 0,
            int defenderPE = 0,
            TacticalUnit explicitAttacker = null)
        {
            _attack.ExecuteAttack(targetedPart, cancelPenaltyWithAP, attackSkill, defenseSkill,
                defenderWantsToDefend, attackerSpecialization, defenderSpecialization, weaponBaseDamage,
                attackerBonusAP, defenderBonusAP, attackDie, defenseDie, explicitTarget,
                attackerPE, defenderPE, explicitAttacker);
        }

        private System.Collections.IEnumerator ExecuteAttackRoutine(Action triggerKick, Action triggerDefense, Action resolveImpact)
        {
            return _attack.ExecuteAttackRoutine(triggerKick, triggerDefense, resolveImpact);
        }

        private IEnumerator ExecuteAttackWithPlayerDefenseRoutine(
            TacticalUnit attacker,
            TacticalUnit defender,
            BodyPart targetedPart,
            bool cancelPenaltyWithAP,
            SkillType attackSkill,
            SkillType defenseSkill,
            int weaponBaseDamage,
            int attackerBonusAP,
            int attackerPE,
            string attackerSpecialization,
            string defenderSpecialization,
            Action triggerKick,
            Action triggerDefense)
        {
            return _attack.ExecuteAttackWithPlayerDefenseRoutine(attacker, defender, targetedPart,
                cancelPenaltyWithAP, attackSkill, defenseSkill, weaponBaseDamage, attackerBonusAP,
                attackerPE, attackerSpecialization, defenderSpecialization, triggerKick, triggerDefense);
        }

        /// <summary>Inventaire lançable de l'unité (grenades main + compatibles lanceur).</summary>
        public static List<InventoryItem> GetThrowableGrenades(CharacterSheet sheet)
        {
            return ArenaGrenadeService.GetThrowableGrenades(sheet);
        }

        public static InventoryItem GetEquippedLauncher(CharacterSheet sheet)
        {
            return ArenaGrenadeService.GetEquippedLauncher(sheet);
        }

        public static InventoryItem GetAnyLauncher(CharacterSheet sheet)
        {
            return ArenaGrenadeService.GetAnyLauncher(sheet);
        }

        /// <summary>
        /// Déclenche un lancer de grenade sur une CASE (pas une cible anatomique) :
        /// paie les PA (2 main / 3 lanceur + 1 visée + bonus), consomme 1 grenade du stock,
        /// anime le lancer, disperse en cas d'échec Ballistique, puis fait détoner la zone.
        /// </summary>
        public void ExecuteGrenadeThrow(
            HexCoordinates targetCoords,
            string grenadeItemId,
            bool useLauncher,
            bool aimed,
            int attackerBonusAP = 0)
        {
            _grenades.ExecuteGrenadeThrow(targetCoords, grenadeItemId, useLauncher, aimed, attackerBonusAP);
        }

        public void ExecuteWeaponThrow(TacticalUnit attacker, TacticalUnit defender)
        {
            _grenades.ExecuteWeaponThrow(attacker, defender);
        }

        /// <summary>
        /// RD-033 : décompte le coup tiré puis enraye sur critique adverse.
        /// Appelé après chaque duel où un coup est réellement parti (jet attaquant lancé).
        /// </summary>
        public void ConsumeShotAndCheckJam(TacticalUnit attacker, InventoryItem weapon, DamageResult result, bool hdShot)
        {
            _melee.ConsumeShotAndCheckJam(attacker, weapon, result, hdShot);
        }

        /// <summary>
        /// RD-033 : recharge le chargeur depuis la réserve (éjecte le reste d'un autre
        /// type). useHD exige HeavyAmmo (sniper/Deglazer). Ne compte pas comme attaque.
        /// </summary>
        public bool TryReloadWeapon(TacticalUnit unit, bool useHD, out string message)
        {
            return _melee.TryReloadWeapon(unit, useHD, out message);
        }

        /// <summary>RD-033 : désenraye l'arme équipée (1 PA, gratuit 0 PA avec Kit d'Entretien d'Armes).</summary>
        public bool TryClearJam(TacticalUnit unit, out string message)
        {
            return _melee.TryClearJam(unit, out message);
        }

        /// <summary>
        /// Recharge d'urgence de la barrière de champ de force (1 PA) via Cellule Nytharite (Standard 10 PV / Pure 25 PV).
        /// </summary>
        public bool TryRechargeShieldWithCell(TacticalUnit unit, bool usePureCell, out string message)
        {
            return _melee.TryRechargeShieldWithCell(unit, usePureCell, out message);
        }

        /// <summary>
        /// Désarmement Bras Droit : sur touché au bras porteur, différentiel élevé
        /// arrache l'arme qui tombe au sol (2 cases max). Seuils Livre III :
        /// base 4, Croc de Désarmement 3, Désarmement Fleuret 2.
        /// </summary>
        public void TryDisarmDefender(TacticalUnit attacker, TacticalUnit defender, BodyPart targetedPart, DamageResult result)
        {
            _melee.TryDisarmDefender(attacker, defender, targetedPart, result);
        }
    }
}

#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

namespace Killtime.EditorTools
{
    /// <summary>
    /// Parametres + etats/transitions du PlayerAnimator — extrait sans modification depuis PlayerAnimatorLinker.cs (RD-152).
    /// </summary>
    public static partial class PlayerAnimatorLinker
    {
        private static void EnsureRequiredParameters(AnimatorController controller)
        {
            EnsureParameter(controller, "IsInCombat", AnimatorControllerParameterType.Bool, true);
            EnsureParameter(controller, "IsMoving", AnimatorControllerParameterType.Bool, false);
            EnsureParameter(controller, "TriggerAction", AnimatorControllerParameterType.Trigger);
            EnsureParameter(controller, "TriggerRoundkick", AnimatorControllerParameterType.Trigger);
            EnsureParameter(controller, "TriggerMeleeAttack", AnimatorControllerParameterType.Trigger);
            EnsureParameter(controller, "TriggerFiringRifle", AnimatorControllerParameterType.Trigger);
            EnsureParameter(controller, "TriggerBodyBlock", AnimatorControllerParameterType.Trigger);
            EnsureParameter(controller, "TriggerFallingBackDeath", AnimatorControllerParameterType.Trigger);
            EnsureParameter(controller, "IsKO", AnimatorControllerParameterType.Bool, false);
            EnsureParameter(controller, "WalkSpeedMultiplier", AnimatorControllerParameterType.Float, 1f);
            EnsureParameter(controller, "Speed", AnimatorControllerParameterType.Float, 0f);
            EnsureParameter(controller, "WeaponType", AnimatorControllerParameterType.Int, 0);
        }

        private static void EnsureParameter(AnimatorController controller, string paramName, AnimatorControllerParameterType type, object defaultValue = null)
        {
            var param = controller.parameters.FirstOrDefault(p => p.name == paramName);
            if (param == null)
            {
                controller.AddParameter(paramName, type);
                param = controller.parameters.FirstOrDefault(p => p.name == paramName);
            }

            if (param != null && defaultValue != null)
            {
                if (type == AnimatorControllerParameterType.Bool && defaultValue is bool bVal)
                {
                    param.defaultBool = bVal;
                }
                else if (type == AnimatorControllerParameterType.Float && defaultValue is float fVal)
                {
                    param.defaultFloat = fVal;
                }
                else if (type == AnimatorControllerParameterType.Int && defaultValue is int iVal)
                {
                    param.defaultInt = iVal;
                }
            }
        }

        private static void EnsureRequiredStatesAndTransitions(AnimatorController controller, AnimatorStateMachine sm)
        {
            AnimatorState standingIdle = sm.states.FirstOrDefault(s => s.state.name == "Standing Idle").state;
            AnimatorState fightIdle = sm.states.FirstOrDefault(s => s.state.name == "Fight Idle").state;

            if (fightIdle != null)
            {
                sm.defaultState = fightIdle;
            }

            var walkingStateChild = sm.states.FirstOrDefault(s => s.state.name == "Walking");
            AnimatorState walkingState = walkingStateChild.state;
            if (walkingState == null)
            {
                walkingState = sm.AddState("Walking", new Vector3(240, 220, 0));
            }
            EnsureLocomotionBlendTree(controller, walkingState);

            var roundkickStateChild = sm.states.FirstOrDefault(s => s.state.name == "Roundkick");
            AnimatorState roundkickState = roundkickStateChild.state;
            if (roundkickState == null)
            {
                roundkickState = sm.AddState("Roundkick", new Vector3(520, 360, 0));
            }

            if (standingIdle != null && fightIdle != null)
            {
                var staleToFight = standingIdle.transitions.Where(t => t.destinationState == fightIdle || (t.destinationState != null && t.destinationState.name.Contains("Fight Idle"))).ToArray();
                for (int i = 0; i < staleToFight.Length; i++)
                {
                    standingIdle.RemoveTransition(staleToFight[i]);
                }

                var toFight = standingIdle.AddTransition(fightIdle);
                toFight.AddCondition(AnimatorConditionMode.If, 0, "IsInCombat");
                toFight.AddCondition(AnimatorConditionMode.Equals, 0, "WeaponType");
                toFight.hasExitTime = false;
                toFight.duration = 0.15f;

                var staleToStanding = fightIdle.transitions.Where(t => t.destinationState == standingIdle || (t.destinationState != null && t.destinationState.name.Contains("Standing Idle"))).ToArray();
                for (int i = 0; i < staleToStanding.Length; i++)
                {
                    fightIdle.RemoveTransition(staleToStanding[i]);
                }

                var toStanding = fightIdle.AddTransition(standingIdle);
                toStanding.AddCondition(AnimatorConditionMode.IfNot, 0, "IsInCombat");
                toStanding.hasExitTime = false;
                toStanding.duration = 0.15f;
            }

            if (standingIdle != null)
            {
                var staleWalk = standingIdle.transitions.Where(t => t.destinationState == walkingState).ToArray();
                for (int i = 0; i < staleWalk.Length; i++)
                {
                    standingIdle.RemoveTransition(staleWalk[i]);
                }

                var t = standingIdle.AddTransition(walkingState);
                t.AddCondition(AnimatorConditionMode.If, 0, "IsMoving");
                t.hasExitTime = false;
                t.duration = 0.1f;
                t.canTransitionToSelf = false;
            }

            if (fightIdle != null)
            {
                var staleWalk = fightIdle.transitions.Where(t => t.destinationState == walkingState).ToArray();
                for (int i = 0; i < staleWalk.Length; i++)
                {
                    fightIdle.RemoveTransition(staleWalk[i]);
                }

                var t = fightIdle.AddTransition(walkingState);
                t.AddCondition(AnimatorConditionMode.If, 0, "IsMoving");
                t.hasExitTime = false;
                t.duration = 0.1f;
                t.canTransitionToSelf = false;
            }

            var staleWalkOut = walkingState.transitions.ToArray();
            for (int i = 0; i < staleWalkOut.Length; i++)
            {
                walkingState.RemoveTransition(staleWalkOut[i]);
            }

            if (fightIdle != null)
            {
                var t = walkingState.AddTransition(fightIdle);
                t.AddCondition(AnimatorConditionMode.IfNot, 0, "IsMoving");
                t.AddCondition(AnimatorConditionMode.If, 0, "IsInCombat");
                t.hasExitTime = false;
                t.duration = 0.15f;
                t.canTransitionToSelf = false;
            }

            if (standingIdle != null)
            {
                var t = walkingState.AddTransition(standingIdle);
                t.AddCondition(AnimatorConditionMode.IfNot, 0, "IsMoving");
                t.AddCondition(AnimatorConditionMode.IfNot, 0, "IsInCombat");
                t.hasExitTime = false;
                t.duration = 0.15f;
                t.canTransitionToSelf = false;
            }

            if (fightIdle != null)
            {
                var staleKicks = fightIdle.transitions.Where(t => t.destinationState == roundkickState).ToArray();
                for (int i = 0; i < staleKicks.Length; i++)
                {
                    fightIdle.RemoveTransition(staleKicks[i]);
                }

                var t = fightIdle.AddTransition(roundkickState);
                t.AddCondition(AnimatorConditionMode.If, 0, "TriggerRoundkick");
                t.hasExitTime = false;
                t.duration = 0.15f;
                t.canTransitionToSelf = false;
            }

            var staleFromKick = roundkickState.transitions.Where(t => t.destinationState == fightIdle).ToArray();
            for (int i = 0; i < staleFromKick.Length; i++)
            {
                roundkickState.RemoveTransition(staleFromKick[i]);
            }

            if (fightIdle != null)
            {
                var t = roundkickState.AddTransition(fightIdle);
                t.hasExitTime = true;
                t.exitTime = 0.88f;
                t.duration = 0.15f;
                t.canTransitionToSelf = false;
            }

            // Attaque à l'arme de mêlée : état "Melee Attack" (clip "Standing Melee Attack",
            // one-shot non loopé). Entrées depuis les gardes (Fight/Standing Idle) sur
            // TriggerMeleeAttack, retour auto vers Fight Idle, comme le Roundkick.
            var meleeStateChild = sm.states.FirstOrDefault(s => s.state.name == "Melee Attack");
            AnimatorState meleeState = meleeStateChild.state;
            if (meleeState == null)
            {
                meleeState = sm.AddState("Melee Attack", new Vector3(520, 470, 0));
            }

            if (fightIdle != null)
            {
                var staleMelee = fightIdle.transitions.Where(t => t.destinationState == meleeState).ToArray();
                for (int i = 0; i < staleMelee.Length; i++)
                {
                    fightIdle.RemoveTransition(staleMelee[i]);
                }

                var t = fightIdle.AddTransition(meleeState);
                t.AddCondition(AnimatorConditionMode.If, 0, "TriggerMeleeAttack");
                t.hasExitTime = false;
                t.duration = 0.08f;
                t.canTransitionToSelf = false;
            }

            if (standingIdle != null)
            {
                var staleMelee = standingIdle.transitions.Where(t => t.destinationState == meleeState).ToArray();
                for (int i = 0; i < staleMelee.Length; i++)
                {
                    standingIdle.RemoveTransition(staleMelee[i]);
                }

                var t = standingIdle.AddTransition(meleeState);
                t.AddCondition(AnimatorConditionMode.If, 0, "TriggerMeleeAttack");
                t.hasExitTime = false;
                t.duration = 0.08f;
                t.canTransitionToSelf = false;
            }

            var staleFromMelee = meleeState.transitions.Where(t => t.destinationState == fightIdle).ToArray();
            for (int i = 0; i < staleFromMelee.Length; i++)
            {
                meleeState.RemoveTransition(staleFromMelee[i]);
            }

            if (fightIdle != null)
            {
                var t = meleeState.AddTransition(fightIdle);
                t.hasExitTime = true;
                t.exitTime = 0.88f;
                t.duration = 0.15f;
                t.canTransitionToSelf = false;
            }

            var bodyBlockChild = sm.states.FirstOrDefault(s => s.state.name == "Body Block");
            AnimatorState bodyBlockState = bodyBlockChild.state;
            if (bodyBlockState == null)
            {
                bodyBlockState = sm.AddState("Body Block", new Vector3(800, 190, 0));
            }

            var deathChild = sm.states.FirstOrDefault(s => s.state.name == "Falling Back Death");
            AnimatorState deathState = deathChild.state;
            if (deathState == null)
            {
                deathState = sm.AddState("Falling Back Death", new Vector3(800, 300, 0));
            }

            if (fightIdle != null)
            {
                var staleBlock = fightIdle.transitions.Where(t => t.destinationState == bodyBlockState).ToArray();
                for (int i = 0; i < staleBlock.Length; i++) fightIdle.RemoveTransition(staleBlock[i]);

                var tBlock = fightIdle.AddTransition(bodyBlockState);
                tBlock.AddCondition(AnimatorConditionMode.If, 0, "TriggerBodyBlock");
                tBlock.hasExitTime = false;
                tBlock.duration = 0.1f;
                tBlock.canTransitionToSelf = false;
            }

            if (standingIdle != null)
            {
                var staleBlock = standingIdle.transitions.Where(t => t.destinationState == bodyBlockState).ToArray();
                for (int i = 0; i < staleBlock.Length; i++) standingIdle.RemoveTransition(staleBlock[i]);

                var tBlock = standingIdle.AddTransition(bodyBlockState);
                tBlock.AddCondition(AnimatorConditionMode.If, 0, "TriggerBodyBlock");
                tBlock.hasExitTime = false;
                tBlock.duration = 0.1f;
                tBlock.canTransitionToSelf = false;
            }

            var staleFromBlock = bodyBlockState.transitions.Where(t => t.destinationState == fightIdle).ToArray();
            for (int i = 0; i < staleFromBlock.Length; i++) bodyBlockState.RemoveTransition(staleFromBlock[i]);

            if (fightIdle != null)
            {
                var tBack = bodyBlockState.AddTransition(fightIdle);
                tBack.AddCondition(AnimatorConditionMode.Equals, 0, "WeaponType");
                tBack.hasExitTime = true;
                tBack.exitTime = 0.85f;
                tBack.duration = 0.15f;
                tBack.canTransitionToSelf = false;
            }

            AnimatorState[] sourceStatesForDeath = { fightIdle, standingIdle, walkingState, roundkickState, meleeState, bodyBlockState };
            for (int i = 0; i < sourceStatesForDeath.Length; i++)
            {
                var src = sourceStatesForDeath[i];
                if (src == null) continue;

                var staleDeath = src.transitions.Where(t => t.destinationState == deathState).ToArray();
                for (int j = 0; j < staleDeath.Length; j++) src.RemoveTransition(staleDeath[j]);

                var tDeath = src.AddTransition(deathState);
                tDeath.AddCondition(AnimatorConditionMode.If, 0, "TriggerFallingBackDeath");
                tDeath.hasExitTime = false;
                tDeath.duration = 0.12f;
                tDeath.canTransitionToSelf = false;

                var tDeathKO = src.AddTransition(deathState);
                tDeathKO.AddCondition(AnimatorConditionMode.If, 0, "IsKO");
                tDeathKO.hasExitTime = false;
                tDeathKO.duration = 0.12f;
                tDeathKO.canTransitionToSelf = false;
            }

            var staleFromDeath = deathState.transitions.Where(t => t.destinationState == fightIdle).ToArray();
            for (int i = 0; i < staleFromDeath.Length; i++) deathState.RemoveTransition(staleFromDeath[i]);

            if (fightIdle != null)
            {
                var tRevive = deathState.AddTransition(fightIdle);
                tRevive.AddCondition(AnimatorConditionMode.IfNot, 0, "IsKO");
                tRevive.AddCondition(AnimatorConditionMode.Equals, 0, "WeaponType");
                tRevive.hasExitTime = false;
                tRevive.duration = 0.25f;
                tRevive.canTransitionToSelf = false;
            }

            var rifleIdleChild = sm.states.FirstOrDefault(s => s.state.name == "Rifle Idle");
            AnimatorState rifleIdleState = rifleIdleChild.state;
            if (rifleIdleState == null)
            {
                rifleIdleState = sm.AddState("Rifle Idle", new Vector3(440, 430, 0));
            }

            var rifleWalkChild = sm.states.FirstOrDefault(s => s.state.name == "Rifle Walk To Stop");
            AnimatorState rifleWalkState = rifleWalkChild.state;
            if (rifleWalkState == null)
            {
                rifleWalkState = sm.AddState("Rifle Walk To Stop", new Vector3(200, 430, 0));
            }
            rifleWalkState.speedParameterActive = true;
            rifleWalkState.speedParameter = "WalkSpeedMultiplier";

            var firingRifleChild = sm.states.FirstOrDefault(s => s.state.name == "Firing Rifle");
            AnimatorState firingRifleState = firingRifleChild.state;
            if (firingRifleState == null)
            {
                firingRifleState = sm.AddState("Firing Rifle", new Vector3(680, 530, 0));
            }

            var riflePutAwayChild = sm.states.FirstOrDefault(s => s.state.name == "Rifle Put Away");
            AnimatorState riflePutAwayState = riflePutAwayChild.state;
            if (riflePutAwayState == null)
            {
                riflePutAwayState = sm.AddState("Rifle Put Away", new Vector3(440, 310, 0));
            }

            if (fightIdle != null && rifleIdleState != null)
            {
                var staleToRifle = fightIdle.transitions.Where(t => t.destinationState == rifleIdleState).ToArray();
                for (int i = 0; i < staleToRifle.Length; i++) fightIdle.RemoveTransition(staleToRifle[i]);

                var toRifle = fightIdle.AddTransition(rifleIdleState);
                toRifle.AddCondition(AnimatorConditionMode.Equals, 1, "WeaponType");
                toRifle.hasExitTime = false;
                toRifle.duration = 0.15f;
                toRifle.canTransitionToSelf = false;
            }

            if (standingIdle != null && rifleIdleState != null)
            {
                var staleStandingToRifle = standingIdle.transitions.Where(t => t.destinationState == rifleIdleState).ToArray();
                for (int i = 0; i < staleStandingToRifle.Length; i++) standingIdle.RemoveTransition(staleStandingToRifle[i]);

                var toRifleFromStanding = standingIdle.AddTransition(rifleIdleState);
                toRifleFromStanding.AddCondition(AnimatorConditionMode.If, 0, "IsInCombat");
                toRifleFromStanding.AddCondition(AnimatorConditionMode.Equals, 1, "WeaponType");
                toRifleFromStanding.hasExitTime = false;
                toRifleFromStanding.duration = 0.15f;
                toRifleFromStanding.canTransitionToSelf = false;
            }

            if (rifleIdleState != null && riflePutAwayState != null)
            {
                var stalePutAway = rifleIdleState.transitions.Where(t => t.destinationState == riflePutAwayState).ToArray();
                for (int i = 0; i < stalePutAway.Length; i++) rifleIdleState.RemoveTransition(stalePutAway[i]);

                var tPutAwayExitCombat = rifleIdleState.AddTransition(riflePutAwayState);
                tPutAwayExitCombat.AddCondition(AnimatorConditionMode.IfNot, 0, "IsInCombat");
                tPutAwayExitCombat.hasExitTime = false;
                tPutAwayExitCombat.duration = 0.15f;
                tPutAwayExitCombat.canTransitionToSelf = false;

                var tPutAwayDisarm = rifleIdleState.AddTransition(riflePutAwayState);
                tPutAwayDisarm.AddCondition(AnimatorConditionMode.Equals, 0, "WeaponType");
                tPutAwayDisarm.hasExitTime = false;
                tPutAwayDisarm.duration = 0.15f;
                tPutAwayDisarm.canTransitionToSelf = false;

                var staleFromPutAway = riflePutAwayState.transitions.ToArray();
                for (int i = 0; i < staleFromPutAway.Length; i++) riflePutAwayState.RemoveTransition(staleFromPutAway[i]);

                if (standingIdle != null)
                {
                    var tToStanding = riflePutAwayState.AddTransition(standingIdle);
                    tToStanding.AddCondition(AnimatorConditionMode.IfNot, 0, "IsInCombat");
                    tToStanding.hasExitTime = true;
                    tToStanding.exitTime = 0.88f;
                    tToStanding.duration = 0.15f;
                    tToStanding.canTransitionToSelf = false;
                }

                if (fightIdle != null)
                {
                    var tToFight = riflePutAwayState.AddTransition(fightIdle);
                    tToFight.AddCondition(AnimatorConditionMode.If, 0, "IsInCombat");
                    tToFight.AddCondition(AnimatorConditionMode.Equals, 0, "WeaponType");
                    tToFight.hasExitTime = true;
                    tToFight.exitTime = 0.88f;
                    tToFight.duration = 0.15f;
                    tToFight.canTransitionToSelf = false;
                }
            }

            if (rifleIdleState != null && rifleWalkState != null)
            {
                var staleRifleWalkIn = rifleIdleState.transitions.Where(t => t.destinationState == rifleWalkState).ToArray();
                for (int i = 0; i < staleRifleWalkIn.Length; i++) rifleIdleState.RemoveTransition(staleRifleWalkIn[i]);

                var tRifleWalk = rifleIdleState.AddTransition(rifleWalkState);
                tRifleWalk.AddCondition(AnimatorConditionMode.If, 0, "IsMoving");
                tRifleWalk.hasExitTime = false;
                tRifleWalk.duration = 0.1f;
                tRifleWalk.canTransitionToSelf = false;

                var staleRifleWalkOut = rifleWalkState.transitions.Where(t => t.destinationState == rifleIdleState).ToArray();
                for (int i = 0; i < staleRifleWalkOut.Length; i++) rifleWalkState.RemoveTransition(staleRifleWalkOut[i]);

                var tRifleIdle = rifleWalkState.AddTransition(rifleIdleState);
                tRifleIdle.AddCondition(AnimatorConditionMode.IfNot, 0, "IsMoving");
                tRifleIdle.AddCondition(AnimatorConditionMode.If, 0, "IsInCombat");
                tRifleIdle.hasExitTime = false;
                tRifleIdle.duration = 0.15f;
                tRifleIdle.canTransitionToSelf = false;

                if (standingIdle != null)
                {
                    var staleRifleWalkToStanding = rifleWalkState.transitions.Where(t => t.destinationState == standingIdle).ToArray();
                    for (int i = 0; i < staleRifleWalkToStanding.Length; i++) rifleWalkState.RemoveTransition(staleRifleWalkToStanding[i]);

                    var tRifleStand = rifleWalkState.AddTransition(standingIdle);
                    tRifleStand.AddCondition(AnimatorConditionMode.IfNot, 0, "IsMoving");
                    tRifleStand.AddCondition(AnimatorConditionMode.IfNot, 0, "IsInCombat");
                    tRifleStand.hasExitTime = false;
                    tRifleStand.duration = 0.15f;
                    tRifleStand.canTransitionToSelf = false;
                }
            }

            if (rifleIdleState != null && firingRifleState != null)
            {
                var staleRifleShoot = rifleIdleState.transitions.Where(t => t.destinationState == firingRifleState).ToArray();
                for (int i = 0; i < staleRifleShoot.Length; i++) rifleIdleState.RemoveTransition(staleRifleShoot[i]);

                var tRifleShoot = rifleIdleState.AddTransition(firingRifleState);
                tRifleShoot.AddCondition(AnimatorConditionMode.If, 0, "TriggerFiringRifle");
                tRifleShoot.hasExitTime = false;
                tRifleShoot.duration = 0.05f;
                tRifleShoot.canTransitionToSelf = false;

                var tRifleShootAction = rifleIdleState.AddTransition(firingRifleState);
                tRifleShootAction.AddCondition(AnimatorConditionMode.If, 0, "TriggerAction");
                tRifleShootAction.hasExitTime = false;
                tRifleShootAction.duration = 0.05f;
                tRifleShootAction.canTransitionToSelf = false;

                var staleFromRifleShoot = firingRifleState.transitions.Where(t => t.destinationState == rifleIdleState).ToArray();
                for (int i = 0; i < staleFromRifleShoot.Length; i++) firingRifleState.RemoveTransition(staleFromRifleShoot[i]);

                var tRifleBack = firingRifleState.AddTransition(rifleIdleState);
                tRifleBack.hasExitTime = true;
                tRifleBack.exitTime = 0.85f;
                tRifleBack.duration = 0.12f;
                tRifleBack.canTransitionToSelf = false;
            }

            if (rifleIdleState != null && bodyBlockState != null)
            {
                var staleRifleBlock = rifleIdleState.transitions.Where(t => t.destinationState == bodyBlockState).ToArray();
                for (int i = 0; i < staleRifleBlock.Length; i++) rifleIdleState.RemoveTransition(staleRifleBlock[i]);

                var tRifleBlock = rifleIdleState.AddTransition(bodyBlockState);
                tRifleBlock.AddCondition(AnimatorConditionMode.If, 0, "TriggerBodyBlock");
                tRifleBlock.hasExitTime = false;
                tRifleBlock.duration = 0.1f;
                tRifleBlock.canTransitionToSelf = false;

                var staleBlockToRifle = bodyBlockState.transitions.Where(t => t.destinationState == rifleIdleState).ToArray();
                for (int i = 0; i < staleBlockToRifle.Length; i++) bodyBlockState.RemoveTransition(staleBlockToRifle[i]);

                var tBlockToRifle = bodyBlockState.AddTransition(rifleIdleState);
                tBlockToRifle.AddCondition(AnimatorConditionMode.Equals, 1, "WeaponType");
                tBlockToRifle.hasExitTime = true;
                tBlockToRifle.exitTime = 0.85f;
                tBlockToRifle.duration = 0.15f;
                tBlockToRifle.canTransitionToSelf = false;
            }

            AnimatorState[] rifleDeathSources = { rifleIdleState, rifleWalkState, firingRifleState, riflePutAwayState };
            for (int i = 0; i < rifleDeathSources.Length; i++)
            {
                var src = rifleDeathSources[i];
                if (src == null || deathState == null) continue;

                var staleDeath = src.transitions.Where(t => t.destinationState == deathState).ToArray();
                for (int j = 0; j < staleDeath.Length; j++) src.RemoveTransition(staleDeath[j]);

                var tDeath = src.AddTransition(deathState);
                tDeath.AddCondition(AnimatorConditionMode.If, 0, "TriggerFallingBackDeath");
                tDeath.hasExitTime = false;
                tDeath.duration = 0.12f;
                tDeath.canTransitionToSelf = false;

                var tDeathKO = src.AddTransition(deathState);
                tDeathKO.AddCondition(AnimatorConditionMode.If, 0, "IsKO");
                tDeathKO.hasExitTime = false;
                tDeathKO.duration = 0.12f;
                tDeathKO.canTransitionToSelf = false;
            }

            if (deathState != null && rifleIdleState != null)
            {
                var staleRifleRevive = deathState.transitions.Where(t => t.destinationState == rifleIdleState).ToArray();
                for (int i = 0; i < staleRifleRevive.Length; i++) deathState.RemoveTransition(staleRifleRevive[i]);

                var tReviveRifle = deathState.AddTransition(rifleIdleState);
                tReviveRifle.AddCondition(AnimatorConditionMode.IfNot, 0, "IsKO");
                tReviveRifle.AddCondition(AnimatorConditionMode.Equals, 1, "WeaponType");
                tReviveRifle.hasExitTime = false;
                tReviveRifle.duration = 0.25f;
                tReviveRifle.canTransitionToSelf = false;
            }

            RemoveSelfTransitions(fightIdle);
            RemoveSelfTransitions(standingIdle);
            RemoveSelfTransitions(walkingState);
            RemoveSelfTransitions(roundkickState);
            RemoveSelfTransitions(meleeState);
            RemoveSelfTransitions(bodyBlockState);
            RemoveSelfTransitions(deathState);
            RemoveSelfTransitions(rifleIdleState);
            RemoveSelfTransitions(rifleWalkState);
            RemoveSelfTransitions(firingRifleState);
            RemoveSelfTransitions(riflePutAwayState);

            if (fightIdle != null)
            {
                foreach (var t in fightIdle.transitions)
                {
                    if (t.destinationState == walkingState || t.destinationState == roundkickState || t.destinationState == meleeState || t.destinationState == bodyBlockState || t.destinationState == deathState || t.destinationState == rifleIdleState)
                    {
                        t.hasExitTime = false;
                    }
                }
            }
        }
    }
}
#endif

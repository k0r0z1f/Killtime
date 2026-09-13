#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

namespace Killtime.EditorTools
{
    public static class PlayerAnimatorLinker
    {
        [MenuItem("Killtime/Lier Animations au PlayerAnimator")]
        public static void LinkAnimationsToPlayerAnimator()
        {
            string[] guids = AssetDatabase.FindAssets("PlayerAnimator t:AnimatorController");
            if (guids == null || guids.Length == 0)
            {
                Debug.LogError("[PlayerAnimatorLinker] Contrôleur 'PlayerAnimator' introuvable dans le projet.");
                return;
            }

            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);

            if (controller == null || controller.layers.Length == 0)
            {
                Debug.LogError("[PlayerAnimatorLinker] Couche d'animation introuvable sur 'PlayerAnimator'.");
                return;
            }

            EnsureRequiredParameters(controller);

            var sm = controller.layers[0].stateMachine;
            EnsureRequiredStatesAndTransitions(sm);

            int boundCount = 0;

            foreach (var childState in sm.states)
            {
                var state = childState.state;
                if (state == null) continue;

                string stateName = state.name;
                AnimationClip clip = FindSubAnimationClip(stateName);

                if (clip != null)
                {
                    bool shouldLoop = stateName == "Fight Idle" || stateName == "Standing Idle" || stateName == "Walking";
                    EnsureClipLooping(clip, shouldLoop);

                    state.motion = clip;
                    boundCount++;
                    Debug.Log($"[PlayerAnimatorLinker] Clip '{clip.name}' assigné à l'état '{stateName}' (Loop: {shouldLoop}).");
                }
                else
                {
                    Debug.LogWarning($"[PlayerAnimatorLinker] Aucun clip trouvé pour l'état '{stateName}' sous Resources/Animations.");
                }
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log($"[PlayerAnimatorLinker] Synchronisation terminée : {boundCount}/{sm.states.Length} états liés.");
        }

        private static void EnsureRequiredParameters(AnimatorController controller)
        {
            EnsureParameter(controller, "IsInCombat", AnimatorControllerParameterType.Bool, true);
            EnsureParameter(controller, "IsMoving", AnimatorControllerParameterType.Bool, false);
            EnsureParameter(controller, "TriggerAction", AnimatorControllerParameterType.Trigger);
            EnsureParameter(controller, "TriggerRoundkick", AnimatorControllerParameterType.Trigger);
            EnsureParameter(controller, "WalkSpeedMultiplier", AnimatorControllerParameterType.Float, 1f);
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
            }
        }

        private static void EnsureRequiredStatesAndTransitions(AnimatorStateMachine sm)
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
            walkingState.speedParameterActive = true;
            walkingState.speedParameter = "WalkSpeedMultiplier";

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

            RemoveSelfTransitions(fightIdle);
            RemoveSelfTransitions(standingIdle);
            RemoveSelfTransitions(walkingState);

            if (fightIdle != null)
            {
                foreach (var t in fightIdle.transitions)
                {
                    if (t.destinationState == walkingState || t.destinationState == roundkickState)
                    {
                        t.hasExitTime = false;
                    }
                }
            }
        }

        private static void RemoveSelfTransitions(AnimatorState state)
        {
            if (state == null) return;
            var selfTransitions = state.transitions.Where(t => t.destinationState == state).ToArray();
            for (int i = 0; i < selfTransitions.Length; i++)
            {
                state.RemoveTransition(selfTransitions[i]);
            }
        }

        private static void EnsureClipLooping(AnimationClip clip, bool shouldLoop)
        {
            if (clip == null) return;

            string assetPath = AssetDatabase.GetAssetPath(clip);
            if (string.IsNullOrEmpty(assetPath)) return;

            var modelImporter = AssetImporter.GetAtPath(assetPath) as ModelImporter;
            if (modelImporter != null)
            {
                var clips = modelImporter.clipAnimations;
                if (clips == null || clips.Length == 0)
                {
                    clips = modelImporter.defaultClipAnimations;
                }

                if (clips != null && clips.Length > 0)
                {
                    bool modified = false;
                    for (int i = 0; i < clips.Length; i++)
                    {
                        if (clips[i].name == clip.name || clips.Length == 1)
                        {
                            if (clips[i].loopTime != shouldLoop || clips[i].loopPose != shouldLoop)
                            {
                                clips[i].loopTime = shouldLoop;
                                clips[i].loopPose = shouldLoop;
                                modified = true;
                            }
                        }
                    }

                    if (modified)
                    {
                        modelImporter.clipAnimations = clips;
                        modelImporter.SaveAndReimport();
                    }
                }
            }

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            if (settings.loopTime != shouldLoop || settings.loopBlend != shouldLoop)
            {
                settings.loopTime = shouldLoop;
                settings.loopBlend = shouldLoop;
                AnimationUtility.SetAnimationClipSettings(clip, settings);
                EditorUtility.SetDirty(clip);
            }
        }

        private static AnimationClip FindSubAnimationClip(string clipName)
        {
            string[] guids = AssetDatabase.FindAssets($"{clipName} t:Model", new[] { "Assets/Resources/Animations" });
            if (guids == null || guids.Length == 0)
            {
                guids = AssetDatabase.FindAssets($"{clipName} t:Model");
            }
            if (guids == null || guids.Length == 0)
            {
                guids = AssetDatabase.FindAssets($"{clipName} t:AnimationClip");
            }

            foreach (var guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                string fileName = Path.GetFileNameWithoutExtension(assetPath);
                if (string.Equals(fileName, clipName, StringComparison.OrdinalIgnoreCase))
                {
                    var assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                    foreach (var a in assets)
                    {
                        if (a is AnimationClip c && !c.name.StartsWith("__preview__"))
                        {
                            return c;
                        }
                    }
                }
            }

            foreach (var guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                var assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                foreach (var a in assets)
                {
                    if (a is AnimationClip c && !c.name.StartsWith("__preview__"))
                    {
                        if (string.Equals(c.name, clipName, StringComparison.OrdinalIgnoreCase))
                        {
                            return c;
                        }
                    }
                }
            }

            return null;
        }
    }
}
#endif
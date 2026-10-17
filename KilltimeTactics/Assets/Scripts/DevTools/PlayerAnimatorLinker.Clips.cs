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
    /// BlendTree locomotion + resolution de clips — extrait sans modification depuis PlayerAnimatorLinker.cs (RD-152).
    /// </summary>
    public static partial class PlayerAnimatorLinker
    {
        /// <summary>
        /// Locomotion RD-025 : état "Walking" piloté par un BlendTree 1D (param "Speed",
        /// m/s jeu = TacticalUnit.MoveSpeed). Seuils calés sur les vitesses jeu : marche
        /// 1.35 (cycle naturel), Medium Run 5.75 (Élan 5.5/6.2), Fast Run 8.0 (Élan 7.0/8.5,
        /// base 4.0 = blend marche/medium). Base Female unisexe (takes renommees RD-024 :
        /// "Medium Run"/"Fast Run" Female + Male, override genre par nom resolvable —
        /// swap runtime suivi RD-025).
        /// Sans les clips course : repli état simple + WalkSpeedMultiplier (pré-RD-024).
        /// </summary>
        private static void EnsureLocomotionBlendTree(AnimatorController controller, AnimatorState walkingState)
        {
            if (walkingState == null) return;

            AnimationClip walkClip = FindBaseClip("Walking") ?? FindSubAnimationClip("Walking");
            // Base Female explicite : FindSubAnimationClip("Medium Run") est ambigu
            // (Female + Male portent le même nom de fichier).
            AnimationClip mediumClip = FindGenderedClip("Medium Run", "Female")
                ?? FindSubAnimationClip("Medium Run");
            AnimationClip fastClip = FindGenderedClip("Fast Run", "Female")
                ?? FindSubAnimationClip("Fast Run");

            if (walkClip != null) EnsureClipLooping(walkClip, true);
            if (mediumClip != null) EnsureClipLooping(mediumClip, true);
            if (fastClip != null) EnsureClipLooping(fastClip, true);

            if (mediumClip == null || fastClip == null)
            {
                walkingState.speedParameterActive = true;
                walkingState.speedParameter = "WalkSpeedMultiplier";
                if (walkClip != null) walkingState.motion = walkClip;
                return;
            }

            var tree = new BlendTree
            {
                name = "Locomotion",
                blendType = BlendTreeType.Simple1D,
                blendParameter = "Speed",
                useAutomaticThresholds = false,
                minThreshold = 0f,
                maxThreshold = 8.5f
            };
            if (walkClip == null) walkClip = walkingState.motion as AnimationClip;
            if (walkClip == null) walkClip = mediumClip;
            tree.AddChild(walkClip, 1.35f);
            tree.AddChild(mediumClip, 5.75f);
            tree.AddChild(fastClip, 8.0f);

            var oldTree = walkingState.motion as BlendTree;
            walkingState.motion = tree;
            AssetDatabase.AddObjectToAsset(tree, controller);
            if (oldTree != null && oldTree != tree) UnityEngine.Object.DestroyImmediate(oldTree, true);

            walkingState.speedParameterActive = false;
            walkingState.speedParameter = string.Empty;
        }

        /// <summary>
        /// Charge un clip dans un sous-dossier genré (Animations/Female, Animations/Male).
        /// Null si absent : le runtime replie via LoadAnimationClip (Female historique).
        /// </summary>
        private static AnimationClip FindGenderedClip(string clipName, string genderFolder)
        {
            string[] guids = AssetDatabase.FindAssets($"{clipName} t:Model", new[] { "Assets/Resources/Animations" });
            if (guids == null) return null;
            foreach (var guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (assetPath.IndexOf($"/{genderFolder}/", StringComparison.OrdinalIgnoreCase) < 0) continue;
                string fileName = Path.GetFileNameWithoutExtension(assetPath);
                if (!string.Equals(fileName, clipName, StringComparison.OrdinalIgnoreCase)) continue;
                var assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                foreach (var a in assets)
                {
                    if (a is AnimationClip c && !c.name.StartsWith("__preview__"))
                    {
                        return c;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Clip de base historique : préfère la variante non-Male (racine = Female
        /// historique, puis dossier Female), le runtime appliquant la variante genrée
        /// via AnimatorOverrideController.
        /// </summary>
        private static AnimationClip FindBaseClip(string clipName)
        {
            string[] guids = AssetDatabase.FindAssets($"{clipName} t:Model", new[] { "Assets/Resources/Animations" });
            if (guids == null) return null;
            AnimationClip fallback = null;
            foreach (var guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                string fileName = Path.GetFileNameWithoutExtension(assetPath);
                if (!string.Equals(fileName, clipName, StringComparison.OrdinalIgnoreCase)) continue;
                var assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
                foreach (var a in assets)
                {
                    if (a is AnimationClip c && !c.name.StartsWith("__preview__"))
                    {
                        if (assetPath.IndexOf("/Male/", StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            return c;
                        }
                        fallback ??= c;
                    }
                }
            }
            return fallback;
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

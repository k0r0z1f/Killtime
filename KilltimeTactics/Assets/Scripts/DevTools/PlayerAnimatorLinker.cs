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
    /// Point d'entree du lieur PlayerAnimator (menu Killtime/Lier Animations au PlayerAnimator).
    /// Facade : le point d'entree vit ici, le contenu vit dans les partiels
    /// (States, Clips) — extrait sans modification (RD-152).
    /// </summary>
    public static partial class PlayerAnimatorLinker
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
            EnsureRequiredStatesAndTransitions(controller, sm);

            int boundCount = 0;

            foreach (var childState in sm.states)
            {
                var state = childState.state;
                if (state == null) continue;

                // État locomotion : arbre géré par EnsureLocomotionBlendTree (clips
                // liés par nom de motion, pas par nom d'état). On force juste le loop.
                if (state.motion is BlendTree tree)
                {
                    foreach (var child in tree.children)
                    {
                        if (child.motion is AnimationClip treeClip)
                        {
                            EnsureClipLooping(treeClip, true);
                        }
                    }
                    continue;
                }

                string stateName = state.name;
                // Base historique Female (racine) : FindSubAnimationClip est ambigu
                // quand Root + Male partagent le même nom de fichier.
                AnimationClip clip = FindBaseClip(stateName) ?? FindSubAnimationClip(stateName);
                // L'état "Melee Attack" utilise le clip Mixamo "Standing Melee Attack".
                if (clip == null && stateName == "Melee Attack")
                {
                    clip = FindBaseClip("Standing Melee Attack") ?? FindSubAnimationClip("Standing Melee Attack");
                }

                if (clip != null)
                {
                    bool shouldLoop = stateName == "Fight Idle" || stateName == "Standing Idle" || stateName == "Walking" || stateName == "Rifle Idle" || stateName == "Rifle Walk To Stop" || stateName == "Walk With Rifle";
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
    }
}
#endif

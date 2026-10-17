#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

namespace Killtime.EditorTools
{
    /// <summary>
    /// RD-024 sous-lot : renomme les takes Mixamo "mixamo.com" vers le nom de fichier
    /// (ex. "Medium Run", "Fast Run") sans toucher au reste de l'import (frames,
    /// loop, avatar/retarget, echelle). Seuls les FBX mono-take sous
    /// Assets/Resources/Animations (+ Roundkick.fbx racine) sont traites.
    /// Apres renommage, verifie qu'aucun clip "mixamo.com" ne subsiste et que le
    /// BlendTree Locomotion du PlayerAnimator pointe toujours 3 motions non-nulles.
    /// </summary>
    public static class NormalizeAnimationTakeNames
    {
        private const string AnimationsFolder = "Assets/Resources/Animations";
        private const string RoundkickPath = "Assets/Resources/Roundkick.fbx";

        [MenuItem("Killtime/Animations/Renommer les takes Mixamo (RD-024)")]
        public static void NormalizeAll()
        {
            NormalizeAllBatch();
        }

        /// <summary>Entree batchmode : throw si echec (exit code != 0).</summary>
        public static void NormalizeAllBatch()
        {
            var fbxPaths = CollectFbxPaths();
            Debug.Log($"[NormalizeTakes] {fbxPaths.Count} FBX a traiter.");
            int renamed = 0;
            foreach (var path in fbxPaths)
            {
                if (NormalizeOne(path)) renamed++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            Debug.Log($"[NormalizeTakes] {renamed} take(s) renommee(s).");

            VerifyNoMixamoTakeNames(fbxPaths);
            VerifyLocomotionBlendTree();
            Debug.Log("[NormalizeTakes] OK : 0 take 'mixamo.com', BlendTree Locomotion intact.");
        }

        private static List<string> CollectFbxPaths()
        {
            var paths = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { AnimationsFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
                    paths.Add(path);
            }
            if (File.Exists(RoundkickPath)) paths.Add(RoundkickPath);
            paths.Sort(StringComparer.Ordinal);
            return paths;
        }

        /// <summary>Renomme la take vers le nom de fichier. Retourne true si modifie.</summary>
        private static bool NormalizeOne(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                Debug.LogWarning($"[NormalizeTakes] Pas un ModelImporter : {path} (ignore).");
                return false;
            }

            var clips = importer.clipAnimations;
            if (clips == null || clips.Length == 0)
                clips = importer.defaultClipAnimations;
            if (clips == null || clips.Length == 0)
            {
                Debug.LogWarning($"[NormalizeTakes] Aucun clip : {path} (ignore).");
                return false;
            }
            if (clips.Length != 1)
            {
                Debug.LogWarning($"[NormalizeTakes] Multi-takes ({clips.Length}) : {path} (revue manuelle).");
                return false;
            }

            string expected = Path.GetFileNameWithoutExtension(path);
            if (string.Equals(clips[0].name, expected, StringComparison.Ordinal))
                return false;

            Debug.Log($"[NormalizeTakes] '{clips[0].name}' -> '{expected}' : {path}");
            clips[0].name = expected;
            importer.clipAnimations = clips;
            importer.SaveAndReimport();
            return true;
        }

        private static void VerifyNoMixamoTakeNames(List<string> fbxPaths)
        {
            var offenders = new List<string>();
            foreach (var path in fbxPaths)
            {
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__")
                        && string.Equals(clip.name, "mixamo.com", StringComparison.OrdinalIgnoreCase))
                    {
                        offenders.Add($"{path} : '{clip.name}'");
                    }
                }
            }
            if (offenders.Count > 0)
                throw new Exception("[NormalizeTakes] Takes 'mixamo.com' restantes :\n" + string.Join("\n", offenders));
        }

        private static void VerifyLocomotionBlendTree()
        {
            var guids = AssetDatabase.FindAssets("PlayerAnimator t:AnimatorController");
            if (guids == null || guids.Length == 0)
                throw new Exception("[NormalizeTakes] Controleur 'PlayerAnimator' introuvable.");
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(AssetDatabase.GUIDToAssetPath(guids[0]));
            if (controller == null || controller.layers.Length == 0)
                throw new Exception("[NormalizeTakes] PlayerAnimator illisible.");

            foreach (var childState in controller.layers[0].stateMachine.states)
            {
                if (childState.state == null || childState.state.name != "Walking") continue;
                if (!(childState.state.motion is BlendTree tree))
                    throw new Exception("[NormalizeTakes] L'etat 'Walking' n'est plus un BlendTree.");
                if (tree.children == null || tree.children.Length != 3)
                    throw new Exception($"[NormalizeTakes] BlendTree Locomotion : {tree.children?.Length ?? 0} enfants (attendu 3).");
                var names = new List<string>();
                foreach (var child in tree.children)
                {
                    if (!(child.motion is AnimationClip clip) || clip == null)
                        throw new Exception("[NormalizeTakes] BlendTree Locomotion : motion manquante (reference cassee).");
                    names.Add(clip.name);
                }
                Debug.Log("[NormalizeTakes] BlendTree Locomotion : " + string.Join(" / ", names));
                return;
            }
            throw new Exception("[NormalizeTakes] Etat 'Walking' introuvable dans PlayerAnimator.");
        }
    }
}
#endif

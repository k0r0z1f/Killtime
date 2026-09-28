#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class CleanPrefabMissingScripts
{
    private const string PrefabPath = "Assets/Resources/Prefabs/Environment/Asteroid_Base.prefab";

    [MenuItem("Killtime/Nettoyer Scripts Manquants du Prefab Asteroid_Base")]
    public static void CleanAll()
    {
        int totalCleaned = 0;

        // 1. Décontamination du Prefab Stage actif (si ouvert en édition)
        var stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (stage != null && stage.prefabContentsRoot != null)
        {
            var stageTransforms = stage.prefabContentsRoot.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < stageTransforms.Length; i++)
            {
                totalCleaned += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(stageTransforms[i].gameObject);
            }
            EditorSceneManager.MarkSceneDirty(stage.scene);
            Debug.Log($"[CLEAN] {totalCleaned} script(s) manquant(s) purgé(s) du Prefab Stage ouvert.");
        }

        // 2. Décontamination directe du fichier .prefab physique sur disque
        if (File.Exists(PrefabPath))
        {
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(PrefabPath);
            if (prefabRoot != null)
            {
                try
                {
                    int diskCleaned = 0;
                    var allTransforms = prefabRoot.GetComponentsInChildren<Transform>(true);
                    for (int i = 0; i < allTransforms.Length; i++)
                    {
                        diskCleaned += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(allTransforms[i].gameObject);
                    }

                    PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath);
                    totalCleaned += diskCleaned;
                    Debug.Log($"[CLEAN] {diskCleaned} script(s) manquant(s) purgé(s) du fichier physique '{PrefabPath}'.");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(prefabRoot);
                }
            }
        }

        // 3. Décontamination des instances présentes dans la scène
        var sceneObjects = Object.FindObjectsByType<GameObject>();
        int sceneCleaned = 0;
        for (int i = 0; i < sceneObjects.Length; i++)
        {
            if (sceneObjects[i] != null && sceneObjects[i].name.StartsWith("Hex_"))
            {
                sceneCleaned += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(sceneObjects[i]);
            }
        }

        totalCleaned += sceneCleaned;

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog(
            "Nettoyage Terminé",
            $"Opération réussie :\n{totalCleaned} composants 'Missing Script' ont été définitivement purgés.\n\nVous pouvez désormais sauvegarder et fermer le Prefab Mode.",
            "OK"
        );
    }
}
#endif
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Killtime.Story.Data
{
    /// <summary>
    /// Chargement et persistance des JSON de scènes depuis le disque. Le fichier local
    /// (Scenarios/{sceneId}.json, écrit par l'éditeur) est l'unique source
    /// du contenu narratif : aucun contenu de scène ne vit dans les scripts.
    /// </summary>
    public static class StorySceneRepository
    {
        public static string ScenariosDirectory => Path.Combine(Application.persistentDataPath, "Scenarios");

        public static string GetSceneFilePath(string sceneId)
        {
            if (string.IsNullOrWhiteSpace(sceneId)) return string.Empty;
            string fileName = sceneId.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? sceneId : $"{sceneId}.json";
            return Path.Combine(ScenariosDirectory, fileName);
        }

        public static bool TryLoadSceneData(string sceneId, out StorySceneData data)
        {
            data = null;
            if (string.IsNullOrWhiteSpace(sceneId)) return false;

            try
            {
                string path = GetSceneFilePath(sceneId);
                if (!File.Exists(path))
                {
                    if (File.Exists(sceneId)) path = sceneId;
                    else return false;
                }

                string json = File.ReadAllText(path);
                data = JsonUtility.FromJson<StorySceneData>(json);
                if (data == null) return false;

                if (string.IsNullOrWhiteSpace(data.SceneId))
                {
                    data.SceneId = Path.GetFileNameWithoutExtension(path);
                }

                StorySceneData.EnsureDeepDefaults(data);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[StorySceneRepository] Erreur lecture scène '{sceneId}': {ex.Message}");
                data = null;
                return false;
            }
        }

        public static List<StorySceneData> LoadAllScenes()
        {
            var list = new List<StorySceneData>();
            string dir = ScenariosDirectory;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                return list;
            }

            string[] files = Directory.GetFiles(dir, "*.json");
            for (int i = 0; i < files.Length; i++)
            {
                try
                {
                    string json = File.ReadAllText(files[i]);
                    var sceneData = JsonUtility.FromJson<StorySceneData>(json);
                    if (sceneData != null)
                    {
                        if (string.IsNullOrWhiteSpace(sceneData.SceneId))
                        {
                            sceneData.SceneId = Path.GetFileNameWithoutExtension(files[i]);
                        }
                        StorySceneData.EnsureDeepDefaults(sceneData);
                        list.Add(sceneData);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[StorySceneRepository] Fichier corrompu '{files[i]}': {ex.Message}");
                }
            }

            return list;
        }

        public static bool DeleteScene(string sceneId)
        {
            if (string.IsNullOrWhiteSpace(sceneId)) return false;
            try
            {
                string path = GetSceneFilePath(sceneId);
                if (File.Exists(path))
                {
                    File.Delete(path);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[StorySceneRepository] Erreur lors de la suppression du fichier de scène '{sceneId}': {ex.Message}");
            }
            return false;
        }
    }
}

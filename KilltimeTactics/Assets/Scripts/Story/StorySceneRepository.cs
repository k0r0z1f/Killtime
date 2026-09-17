using System.IO;
using UnityEngine;

namespace Killtime.Story.Data
{
    /// <summary>
    /// Chargement des JSON de scènes depuis le disque. Le fichier local
    /// (Scenarios/{sceneId}.json, écrit par l'éditeur) est l'unique source
    /// du contenu narratif : aucun contenu de scène ne vit dans les scripts.
    /// </summary>
    public static class StorySceneRepository
    {
        public static string GetSceneFilePath(string sceneId)
        {
            return Path.Combine(Application.persistentDataPath, "Scenarios", $"{sceneId}.json");
        }

        public static bool TryLoadSceneData(string sceneId, out StorySceneData data)
        {
            data = null;
            if (string.IsNullOrWhiteSpace(sceneId)) return false;

            try
            {
                string path = GetSceneFilePath(sceneId);
                if (!File.Exists(path)) return false;

                string json = File.ReadAllText(path);
                data = JsonUtility.FromJson<StorySceneData>(json);
                if (data == null || data.Nodes == null || data.Nodes.Count == 0)
                {
                    data = null;
                    return false;
                }

                StorySceneData.EnsureDeepDefaults(data);
                return true;
            }
            catch
            {
                data = null;
                return false;
            }
        }
    }
}

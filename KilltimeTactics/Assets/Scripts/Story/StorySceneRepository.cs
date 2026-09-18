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
            return DeleteSceneByPath(GetSceneFilePath(sceneId));
        }

        /// <summary>
        /// Supprime un fichier de scène par son chemin absolu sur disque.
        /// Utilisé par l'éditeur pour garantir la suppression du bon fichier
        /// lorsque le nom de fichier diffère du SceneId interne (ex. copies).
        /// </summary>
        public static bool DeleteSceneByPath(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath)) return false;
            try
            {
                if (File.Exists(fullPath))
                {
                    File.Delete(fullPath);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[StorySceneRepository] Erreur lors de la suppression de '{fullPath}': {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// Met à jour le champ NextSceneId d'un fichier de scène JSON sur disque.
        /// </summary>
        public static bool UpdateSceneNextSceneId(string fullPath, string newNextSceneId)
        {
            if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath)) return false;
            try
            {
                string json = File.ReadAllText(fullPath);
                var data = JsonUtility.FromJson<StorySceneData>(json);
                if (data != null)
                {
                    data.NextSceneId = newNextSceneId ?? "";
                    string updatedJson = JsonUtility.ToJson(data, true);
                    File.WriteAllText(fullPath, updatedJson);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[StorySceneRepository] Erreur lors de la mise à jour de NextSceneId pour '{fullPath}': {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// Lit le SceneId d'un fichier JSON de scène, avec repli sur le nom de fichier sans extension.
        /// </summary>
        public static string GetSceneIdFromPath(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath)) return string.Empty;
            try
            {
                if (File.Exists(fullPath))
                {
                    string json = File.ReadAllText(fullPath);
                    var data = JsonUtility.FromJson<StorySceneData>(json);
                    if (data != null && !string.IsNullOrWhiteSpace(data.SceneId))
                    {
                        return data.SceneId;
                    }
                }
            }
            catch { }
            return Path.GetFileNameWithoutExtension(fullPath);
        }

        /// <summary>
        /// Lit le NextSceneId d'un fichier JSON de scène.
        /// </summary>
        public static string GetNextSceneIdFromPath(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath) || !File.Exists(fullPath)) return string.Empty;
            try
            {
                string json = File.ReadAllText(fullPath);
                var data = JsonUtility.FromJson<StorySceneData>(json);
                return data?.NextSceneId ?? string.Empty;
            }
            catch { }
            return string.Empty;
        }

        /// <summary>
        /// Trie une liste de chemins de scènes selon leur ordre de chaînage NextSceneId,
        /// en utilisant le tri alphabétique/naturel comme repli pour les scènes non chaînées.
        /// </summary>
        public static List<string> SortSceneFilesByChain(IEnumerable<string> filePaths)
        {
            if (filePaths == null) return new List<string>();
            var fileList = new List<string>(filePaths);
            if (fileList.Count <= 1) return fileList;

            var idToPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var pathToId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var pathToNext = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var targetedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < fileList.Count; i++)
            {
                string p = fileList[i];
                string sId = GetSceneIdFromPath(p);
                string nextId = GetNextSceneIdFromPath(p);

                idToPath[sId] = p;
                pathToId[p] = sId;
                pathToNext[p] = nextId;

                if (!string.IsNullOrWhiteSpace(nextId))
                {
                    targetedIds.Add(nextId);
                }
            }

            var result = new List<string>(fileList.Count);
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Scènes racines : non référencées en NextSceneId par une autre scène de l'ensemble
            var rootPaths = new List<string>();
            for (int i = 0; i < fileList.Count; i++)
            {
                string p = fileList[i];
                string sId = pathToId[p];
                if (!targetedIds.Contains(sId))
                {
                    rootPaths.Add(p);
                }
            }

            rootPaths.Sort((a, b) => string.Compare(Path.GetFileName(a), Path.GetFileName(b), StringComparison.OrdinalIgnoreCase));

            for (int r = 0; r < rootPaths.Count; r++)
            {
                string curr = rootPaths[r];
                while (!string.IsNullOrEmpty(curr) && !visited.Contains(curr))
                {
                    visited.Add(curr);
                    result.Add(curr);

                    if (pathToNext.TryGetValue(curr, out string nId) && !string.IsNullOrWhiteSpace(nId))
                    {
                        if (idToPath.TryGetValue(nId, out string nextPath) && !visited.Contains(nextPath))
                        {
                            curr = nextPath;
                            continue;
                        }
                    }
                    break;
                }
            }

            var remaining = new List<string>();
            for (int i = 0; i < fileList.Count; i++)
            {
                string p = fileList[i];
                if (!visited.Contains(p))
                {
                    remaining.Add(p);
                }
            }
            remaining.Sort((a, b) => string.Compare(Path.GetFileName(a), Path.GetFileName(b), StringComparison.OrdinalIgnoreCase));
            result.AddRange(remaining);

            return result;
        }
    }
}

#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using Killtime.Story.Data;

namespace Killtime.Story.Editor
{
    /// <summary>
    /// RD-055 — "New Chapter = duplicate" : duplique le JSON template
    /// (Assets/Resources/Scenarios/_ChapterTemplate.json) vers le dossier
    /// des scènes (persistentDataPath/Scenarios) avec un nouvel ID.
    /// Ouvrir ensuite en F8 pour câbler cast, dialogues et environnement.
    /// </summary>
    public static class ChapterTemplateMenu
    {
        private const string TemplateAssetPath = "Assets/Resources/Scenarios/_ChapterTemplate.json";

        [MenuItem("Killtime/Chapitre/Nouveau chapitre depuis le template", false, 20)]
        public static void CreateNewChapterFromTemplate()
        {
            var templateAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(TemplateAssetPath);
            if (templateAsset == null)
            {
                Debug.LogError($"[ChapterTemplate] Template introuvable : {TemplateAssetPath}.");
                return;
            }

            var templateData = JsonUtility.FromJson<StorySceneData>(templateAsset.text);
            if (templateData == null)
            {
                Debug.LogError("[ChapterTemplate] Template JSON illisible.");
                return;
            }
            StorySceneData.EnsureDeepDefaults(templateData);

            string dir = StorySceneRepository.ScenariosDirectory;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            string newId = NextAvailableChapterId(dir, templateData.SceneId);
            templateData.SceneId = newId;
            templateData.Title = $"Nouveau chapitre {newId}";
            templateData.NextSceneId = "";
            if (templateData.EmbeddedMap != null) templateData.EmbeddedMap.MapName = newId;

            string path = StorySceneRepository.GetSceneFilePath(newId);
            File.WriteAllText(path, JsonUtility.ToJson(templateData, true));
            Debug.Log($"[ChapterTemplate] ✅ Nouveau chapitre '{newId}' créé : {path} (F8 pour éditer).");
            EditorUtility.RevealInFinder(path);
        }

        [MenuItem("Killtime/Chapitre/Ouvrir le dossier des chapitres", false, 21)]
        public static void OpenChaptersFolder()
        {
            string dir = StorySceneRepository.ScenariosDirectory;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            EditorUtility.RevealInFinder(dir);
        }

        internal static string NextAvailableChapterId(string dir, string baseId)
        {
            if (string.IsNullOrWhiteSpace(baseId)) baseId = "ch_00_modele";
            string stem = baseId.EndsWith("_modele") ? baseId[..^"_modele".Length] : baseId + "_";
            if (!stem.EndsWith("_")) stem += "_";
            for (int n = 1; n < 1000; n++)
            {
                string candidate = $"{stem}{n:00}";
                if (!File.Exists(Path.Combine(dir, candidate + ".json"))) return candidate;
            }
            return $"{stem}{System.DateTime.Now:HHmmss}";
        }
    }
}
#endif

using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

namespace Killtime.Core.Character
{
    /// <summary>
    /// Service d'écriture et de lecture physique des fiches de personnages sur le disque.
    /// </summary>
    public static class CharacterStorageService
    {
        private static string StorageDirectory => Path.Combine(Application.persistentDataPath, "Characters");

        public static void EnsureDirectoryExists()
        {
            if (!Directory.Exists(StorageDirectory))
            {
                Directory.CreateDirectory(StorageDirectory);
            }
        }

        public static string SaveCharacter(CharacterSheet sheet)
        {
            EnsureDirectoryExists();
            string safeName = string.Join("_", sheet.Name.Split(Path.GetInvalidFileNameChars()));
            if (string.IsNullOrWhiteSpace(safeName)) safeName = "Unnamed";

            string fileName = $"{safeName}_{sheet.SheetId[..8]}.json";
            string fullPath = Path.Combine(StorageDirectory, fileName);

            string json = JsonUtility.ToJson(sheet, true);
            File.WriteAllText(fullPath, json);

            Debug.Log($"[CharacterStorageService] Fiche sauvegardée : {fullPath}");
            return fullPath;
        }

        public static CharacterSheet LoadCharacter(string fullPath)
        {
            if (!File.Exists(fullPath))
            {
                Debug.LogError($"[CharacterStorageService] Fichier introuvable : {fullPath}");
                return null;
            }

            string json = File.ReadAllText(fullPath);
            return JsonUtility.FromJson<CharacterSheet>(json);
        }

        public static List<string> GetSavedCharacterFiles()
        {
            EnsureDirectoryExists();
            return new List<string>(Directory.GetFiles(StorageDirectory, "*.json"));
        }

        public static bool DeleteCharacter(string fullPath)
        {
            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                return true;
            }
            return false;
        }

        public static string ExportToJson(CharacterSheet sheet) => JsonUtility.ToJson(sheet, true);
        public static CharacterSheet ImportFromJson(string json) => JsonUtility.FromJson<CharacterSheet>(json);
    }
}
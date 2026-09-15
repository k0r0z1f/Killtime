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
            if (sheet == null) return null;
            EnsureDirectoryExists();
            if (string.IsNullOrEmpty(sheet.SheetId))
            {
                sheet.SheetId = Guid.NewGuid().ToString("N");
            }

            string safeName = string.Join("_", sheet.Name.Split(Path.GetInvalidFileNameChars()));
            if (string.IsNullOrWhiteSpace(safeName)) safeName = "Unnamed";

            string idPrefix = sheet.SheetId.Length >= 8 ? sheet.SheetId[..8] : sheet.SheetId;
            string fileName = $"{safeName}_{idPrefix}.json";
            string fullPath = Path.Combine(StorageDirectory, fileName);

            try
            {
                string[] existingFiles = Directory.GetFiles(StorageDirectory, $"*_{idPrefix}.json");
                for (int i = 0; i < existingFiles.Length; i++)
                {
                    if (!string.Equals(existingFiles[i], fullPath, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Delete(existingFiles[i]);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CharacterStorageService] Nettoyage ancien fichier : {ex.Message}");
            }

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
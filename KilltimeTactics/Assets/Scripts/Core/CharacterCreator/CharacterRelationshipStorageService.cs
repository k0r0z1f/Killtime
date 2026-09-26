using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

namespace Killtime.Core.Character
{
    public static class CharacterRelationshipStorageService
    {
        private static string StorageDirectory => Path.Combine(Application.persistentDataPath, "Relationships");

        public static void EnsureDirectoryExists()
        {
            if (!Directory.Exists(StorageDirectory))
            {
                Directory.CreateDirectory(StorageDirectory);
            }
        }

        public static string GetFilePath(string sheetId)
        {
            if (string.IsNullOrEmpty(sheetId)) return null;
            string idPrefix = sheetId.Length >= 8 ? sheetId[..8] : sheetId;
            return Path.Combine(StorageDirectory, $"Social_{idPrefix}.json");
        }

        public static CharacterSocialGraph LoadSocialGraph(CharacterSheet sheet)
        {
            if (sheet == null) return new CharacterSocialGraph();
            return LoadSocialGraph(sheet.SheetId, sheet.Name);
        }

        public static CharacterSocialGraph LoadSocialGraph(string sheetId, string characterName = "")
        {
            if (string.IsNullOrEmpty(sheetId)) return new CharacterSocialGraph(sheetId, characterName);

            EnsureDirectoryExists();
            string path = GetFilePath(sheetId);

            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    var graph = JsonUtility.FromJson<CharacterSocialGraph>(json);
                    if (graph != null)
                    {
                        if (string.IsNullOrEmpty(graph.OwnerSheetId)) graph.OwnerSheetId = sheetId;
                        if (!string.IsNullOrEmpty(characterName)) graph.OwnerCharacterName = characterName;
                        return graph;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[CharacterRelationshipStorageService] Lecture échouée pour {sheetId} : {ex.Message}");
                }
            }

            return new CharacterSocialGraph(sheetId, characterName);
        }

        public static bool SaveSocialGraph(CharacterSocialGraph graph)
        {
            if (graph == null || string.IsNullOrEmpty(graph.OwnerSheetId)) return false;

            EnsureDirectoryExists();
            string path = GetFilePath(graph.OwnerSheetId);

            try
            {
                string json = JsonUtility.ToJson(graph, true);
                File.WriteAllText(path, json);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CharacterRelationshipStorageService] Sauvegarde échouée pour {graph.OwnerSheetId} : {ex.Message}");
                return false;
            }
        }

        public static (int defaultAffinity, int defaultTrust) GetDefaultMetrics(RelationshipLinkType linkType)
        {
            return linkType switch
            {
                RelationshipLinkType.SymbioteLigneZero => (85, 95),
                RelationshipLinkType.FraterniteDarmes => (40, 65),
                RelationshipLinkType.HierarchieMilitaire => (20, 50),
                RelationshipLinkType.DetteDhonneur => (50, 70),
                RelationshipLinkType.RivaliteMartiale => (10, 40),
                RelationshipLinkType.MentorEleve => (45, 75),
                RelationshipLinkType.MefianceInstinctive => (-20, 20),
                RelationshipLinkType.HostiliteDeclaree => (-60, 5),
                _ => (0, 40)
            };
        }

        public static void IntroduceCharacters(CharacterSheet initiator, CharacterSheet target, RelationshipLinkType linkType = RelationshipLinkType.NeutreInconnu, string location = "Terrain Tactique", string context = "Premier Contact")
        {
            if (initiator == null || target == null) return;
            if (string.Equals(initiator.SheetId, target.SheetId, StringComparison.OrdinalIgnoreCase)) return;

            var (defaultAffinity, defaultTrust) = GetDefaultMetrics(linkType);

            var graphA = LoadSocialGraph(initiator);
            var entryA = graphA.GetRelationship(target.SheetId);
            if (entryA == null)
            {
                entryA = new CharacterRelationshipEntry(target.SheetId, target.Name, linkType, defaultAffinity, defaultTrust, context, location);
                graphA.AddOrUpdateRelationship(entryA);
                SaveSocialGraph(graphA);
            }
            else
            {
                entryA.InteractionsCount++;
                entryA.TargetCharacterName = target.Name;
                SaveSocialGraph(graphA);
            }

            var graphB = LoadSocialGraph(target);
            var entryB = graphB.GetRelationship(initiator.SheetId);
            if (entryB == null)
            {
                entryB = new CharacterRelationshipEntry(initiator.SheetId, initiator.Name, linkType, defaultAffinity, defaultTrust, context, location);
                graphB.AddOrUpdateRelationship(entryB);
                SaveSocialGraph(graphB);
            }
            else
            {
                entryB.InteractionsCount++;
                entryB.TargetCharacterName = initiator.Name;
                SaveSocialGraph(graphB);
            }
        }
    }
}
using System;
using System.Collections.Generic;
using Killtime.Story.Data;

namespace Killtime.Story
{
    /// <summary>
    /// Registre des définitions de scénarios. Protégé contre les relectures disques
    /// synchrones intempestives en cas d'identifiant introuvable.
    /// </summary>
    public static class ScenarioCatalog
    {
        private static readonly Dictionary<string, ScenarioDefinition> Definitions = new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> KnownMissingIds = new(StringComparer.OrdinalIgnoreCase);
        private static bool _catalogLoaded = false;

        public static IEnumerable<ScenarioDefinition> All
        {
            get
            {
                if (!_catalogLoaded)
                {
                    ReloadFromDisk();
                }
                return Definitions.Values;
            }
        }

        public static ScenarioDefinition Find(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;

            if (Definitions.TryGetValue(id, out var definition))
            {
                return definition;
            }

            if (KnownMissingIds.Contains(id))
            {
                return null;
            }

            if (!_catalogLoaded)
            {
                ReloadFromDisk();
                if (Definitions.TryGetValue(id, out definition))
                {
                    return definition;
                }
            }

            KnownMissingIds.Add(id);
            return null;
        }

        public static void Register(ScenarioDefinition definition)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.Id)) return;
            Definitions[definition.Id] = definition;
            KnownMissingIds.Remove(definition.Id);
        }

        public static bool Unregister(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;
            return Definitions.Remove(id);
        }

        public static void ReloadFromDisk()
        {
            _catalogLoaded = true;
            Definitions.Clear();
            KnownMissingIds.Clear();

            var scenes = StorySceneRepository.LoadAllScenes();
            if (scenes == null) return;

            for (int i = 0; i < scenes.Count; i++)
            {
                var def = FromSceneData(scenes[i]);
                if (def != null)
                {
                    Register(def);
                }
            }
        }

        public static ScenarioDefinition FromSceneData(StorySceneData data)
        {
            if (data == null || string.IsNullOrWhiteSpace(data.SceneId)) return null;

            var definition = new ScenarioDefinition
            {
                Id = data.SceneId,
                Volume = data.Volume,
                Title = data.Title,
                CanonReference = data.CanonReference,
                FirstNodeId = data.FirstNodeId,
                NextSceneId = data.NextSceneId
            };

            if (data.Nodes != null)
            {
                for (int i = 0; i < data.Nodes.Count; i++)
                {
                    var n = data.Nodes[i];
                    if (n == null) continue;
                    definition.Nodes.Add(new ScenarioNode
                    {
                        Id = n.NodeId,
                        Kind = n.Kind,
                        Title = n.Title,
                        Location = n.Location,
                        Body = n.Body,
                        ContinueLabel = n.ContinueLabel,
                        NextNodeId = n.NextNodeId,
                        Objectives = n.Objectives ?? new List<ScenarioObjective>(),
                        Choices = n.Choices ?? new List<ScenarioChoice>(),
                        EnterEffects = n.EnterEffects ?? new List<ScenarioEffect>()
                    });
                }
            }

            return definition;
        }

        public static string GetNextScenarioId(string currentScenarioId)
        {
            if (string.IsNullOrWhiteSpace(currentScenarioId)) return null;

            var current = Find(currentScenarioId);
            if (current != null && !string.IsNullOrWhiteSpace(current.NextSceneId))
            {
                return current.NextSceneId.Trim();
            }

            string savedOrder = UnityEngine.PlayerPrefs.GetString("ScenarioEditor_SceneOrder", "");
            if (!string.IsNullOrEmpty(savedOrder))
            {
                string[] entries = savedOrder.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                var orderedIds = new List<string>(entries.Length);
                for (int i = 0; i < entries.Length; i++)
                {
                    string entry = entries[i];
                    if (string.IsNullOrWhiteSpace(entry)) continue;
                    string fullPath = System.IO.Path.IsPathRooted(entry)
                        ? entry
                        : StorySceneRepository.GetSceneFilePath(System.IO.Path.GetFileNameWithoutExtension(entry));
                    if (!System.IO.File.Exists(fullPath)) continue;
                    string id = StorySceneRepository.GetSceneIdFromPath(fullPath);
                    if (!string.IsNullOrWhiteSpace(id)) orderedIds.Add(id);
                }

                int currentOrderIdx = orderedIds.FindIndex(id => string.Equals(id, currentScenarioId, StringComparison.OrdinalIgnoreCase));
                if (currentOrderIdx >= 0 && currentOrderIdx + 1 < orderedIds.Count)
                {
                    string nextId = orderedIds[currentOrderIdx + 1];
                    var nextDef = Find(nextId);
                    return nextDef != null ? nextDef.Id : nextId;
                }
            }

            var match = System.Text.RegularExpressions.Regex.Match(currentScenarioId, @"^(.*[_\-\s])(\d+)$");
            if (match.Success)
            {
                string prefix = match.Groups[1].Value;
                string numStr = match.Groups[2].Value;
                if (int.TryParse(numStr, out int num))
                {
                    int nextNum = num + 1;
                    string candidatePadded = $"{prefix}{nextNum.ToString(new string('0', numStr.Length))}";
                    if (Find(candidatePadded) != null && System.IO.File.Exists(StorySceneRepository.GetSceneFilePath(candidatePadded)))
                    {
                        return candidatePadded;
                    }

                    string candidateSimple = $"{prefix}{nextNum}";
                    if (Find(candidateSimple) != null && System.IO.File.Exists(StorySceneRepository.GetSceneFilePath(candidateSimple)))
                    {
                        return candidateSimple;
                    }
                }
            }

            var allList = new List<ScenarioDefinition>(All);
            allList.Sort((a, b) =>
            {
                int volComp = string.Compare(a.Volume, b.Volume, StringComparison.OrdinalIgnoreCase);
                if (volComp != 0) return volComp;
                return string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase);
            });

            int currentIndex = allList.FindIndex(s => string.Equals(s.Id, currentScenarioId, StringComparison.OrdinalIgnoreCase));
            if (currentIndex >= 0 && currentIndex + 1 < allList.Count)
            {
                return allList[currentIndex + 1].Id;
            }

            return null;
        }
    }
}
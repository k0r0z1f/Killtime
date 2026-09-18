using System.Collections.Generic;
using Killtime.Story.Data;

namespace Killtime.Story
{
    /// <summary>
    /// Registre des définitions de scénarios. Tout le contenu narratif vit dans
    /// les JSON de scènes (StorySceneData) ; ce catalogue ne fait qu'indexer les
    /// définitions converties depuis ces JSON (éditeur, chargement runtime).
    /// </summary>
    public static class ScenarioCatalog
    {
        private static readonly Dictionary<string, ScenarioDefinition> Definitions = new();

        public static IEnumerable<ScenarioDefinition> All
        {
            get
            {
                if (Definitions.Count == 0)
                {
                    ReloadFromDisk();
                }
                return Definitions.Values;
            }
        }

        public static ScenarioDefinition Find(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            if (!Definitions.TryGetValue(id, out var definition))
            {
                ReloadFromDisk();
                Definitions.TryGetValue(id, out definition);
            }
            return definition;
        }

        public static void Register(ScenarioDefinition definition)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.Id)) return;
            Definitions[definition.Id] = definition;
        }

        public static bool Unregister(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;
            return Definitions.Remove(id);
        }

        public static void ReloadFromDisk()
        {
            Definitions.Clear();
            var scenes = StorySceneRepository.LoadAllScenes();
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

        /// <summary>
        /// Détermine l'identifiant de la scène suivante.
        /// Priorité 1 : NextSceneId explicitement renseigné sur la scène.
        /// Priorité 2 : Ordre personnalisé des scènes de l'éditeur (persistance PlayerPrefs).
        /// Priorité 3 : Déduction numérique (ex: volume_1_scene_01 -> volume_1_scene_02 ou nouvelle_scene_5).
        /// Priorité 4 : Élément suivant dans la liste ordonnée des scénarios disponibles.
        /// </summary>
        public static string GetNextScenarioId(string currentScenarioId)
        {
            if (string.IsNullOrWhiteSpace(currentScenarioId)) return null;

            var current = Find(currentScenarioId);
            if (current != null && !string.IsNullOrWhiteSpace(current.NextSceneId))
            {
                return current.NextSceneId.Trim();
            }

            // Priorité 2 : Ordre personnalisé des scènes de l'éditeur (persistance PlayerPrefs).
            // On résout le SceneId réel de chaque entrée (le nom de fichier peut différer
            // du SceneId) et on ignore les fichiers supprimés depuis.
            string savedOrder = UnityEngine.PlayerPrefs.GetString("ScenarioEditor_SceneOrder", "");
            if (!string.IsNullOrEmpty(savedOrder))
            {
                string[] entries = savedOrder.Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries);
                var orderedIds = new System.Collections.Generic.List<string>(entries.Length);
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

                int currentOrderIdx = orderedIds.FindIndex(id => string.Equals(id, currentScenarioId, System.StringComparison.OrdinalIgnoreCase));
                if (currentOrderIdx >= 0)
                {
                    if (currentOrderIdx + 1 < orderedIds.Count)
                    {
                        string nextId = orderedIds[currentOrderIdx + 1];
                        var nextDef = Find(nextId);
                        return nextDef != null ? nextDef.Id : nextId;
                    }
                    // Fin de la chaîne ordonnée : aucune scène suivante
                    return null;
                }
            }

            // Priorité 3 : Déduction numérique
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

                    // Déduction souple : chercher tout scénario enregistré dont le suffixe numérique est nextNum
                    foreach (var def in All)
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(def.Id, @"[_\-\s](\d+)$");
                        if (m.Success && int.TryParse(m.Groups[1].Value, out int defNum) && defNum == nextNum)
                        {
                            string p = StorySceneRepository.GetSceneFilePath(def.Id);
                            if (System.IO.File.Exists(p))
                            {
                                return def.Id;
                            }
                        }
                    }
                }
            }

            // Priorité 4 : Fallback par position dans le catalogue trié naturellement
            var allList = new List<ScenarioDefinition>(All);
            allList.Sort((a, b) =>
            {
                int volComp = string.Compare(a.Volume, b.Volume, System.StringComparison.OrdinalIgnoreCase);
                if (volComp != 0) return volComp;

                var ma = System.Text.RegularExpressions.Regex.Match(a.Id, @"(\d+)$");
                var mb = System.Text.RegularExpressions.Regex.Match(b.Id, @"(\d+)$");
                if (ma.Success && mb.Success && int.TryParse(ma.Value, out int na) && int.TryParse(mb.Value, out int nb) && na != nb)
                {
                    return na.CompareTo(nb);
                }
                return string.Compare(a.Id, b.Id, System.StringComparison.OrdinalIgnoreCase);
            });

            int currentIndex = allList.FindIndex(s => string.Equals(s.Id, currentScenarioId, System.StringComparison.OrdinalIgnoreCase));
            if (currentIndex >= 0 && currentIndex + 1 < allList.Count)
            {
                return allList[currentIndex + 1].Id;
            }

            return null;
        }
    }
}

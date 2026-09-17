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

        public static IEnumerable<ScenarioDefinition> All => Definitions.Values;

        public static ScenarioDefinition Find(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            Definitions.TryGetValue(id, out var definition);
            return definition;
        }

        public static void Register(ScenarioDefinition definition)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.Id)) return;
            Definitions[definition.Id] = definition;
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
                FirstNodeId = data.FirstNodeId
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
    }
}

using System;
using UnityEngine;
using Killtime.Tactics.Grid;

namespace Killtime.Story.Scenes
{
    /// <summary>
    /// Construction des environnements 3D par identifiant. La scène (JSON)
    /// déclare son environnement via StorySceneData.EnvironmentId ; les
    /// constructeurs restent du code car la géométrie 3D ne peut pas vivre
    /// dans un JSON narratif.
    /// </summary>
    public static class SceneEnvironmentLibrary
    {
        public static bool Build(string environmentId, Transform parent, TacticalHexGrid grid)
        {
            if (string.IsNullOrWhiteSpace(environmentId)) return false;

            if (string.Equals(environmentId, "AsteroidBase", StringComparison.OrdinalIgnoreCase))
            {
                IntroEnvironmentBuilder.BuildAsteroidBase(parent, grid);
                return true;
            }

            Debug.LogWarning($"[SceneEnvironmentLibrary] Environnement inconnu : '{environmentId}'.");
            return false;
        }
    }
}

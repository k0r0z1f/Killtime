using System.Collections.Generic;
using Killtime.Story.Data;
using Killtime.Tactics.Units;

namespace Killtime.Story.Scenes
{
    /// <summary>
    /// Service pur "Parler à" (sans état Unity).
    /// La résolution d'affichage du locuteur ne dépend que de la liste
    /// d'acteurs de la scène + paramètres : version statique testable.
    /// Le contrôleur garde un forwarder d'instance (même signature).
    /// </summary>
    public static class SceneTalkService
    {
        public static string ResolveTalkSpeakerDisplay(IReadOnlyList<SceneActorSpawnData> actors, string talkSpeakerId, SceneActorSpawnData targetActor, TacticalUnit targetUnit)
        {
            if (!string.IsNullOrWhiteSpace(talkSpeakerId) && actors != null)
            {
                string want = talkSpeakerId.Trim();
                for (int i = 0; i < actors.Count; i++)
                {
                    var a = actors[i];
                    if (a == null) continue;
                    if (string.Equals(a.ActorId?.Trim(), want, System.StringComparison.OrdinalIgnoreCase))
                        return string.IsNullOrWhiteSpace(a.DisplayName) ? a.ActorId : a.DisplayName;
                    if (!string.IsNullOrWhiteSpace(a.DisplayName) && string.Equals(a.DisplayName.Trim(), want, System.StringComparison.OrdinalIgnoreCase))
                        return a.DisplayName;
                }
                return want;
            }
            if (targetActor != null && !string.IsNullOrWhiteSpace(targetActor.DisplayName)) return targetActor.DisplayName;
            if (targetUnit != null && targetUnit.Stats != null && !string.IsNullOrWhiteSpace(targetUnit.Stats.Name)) return targetUnit.Stats.Name;
            return "???";
        }
    }
}

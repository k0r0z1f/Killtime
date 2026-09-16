using System;
using System.Collections.Generic;

namespace Killtime.Story
{
    public enum StoryRoute
    {
        None,
        Vardis,
        Independent,
        Imperial
    }

    [Serializable]
    public class CampaignIntValue
    {
        public string Key;
        public int Value;
    }

    /// <summary>
    /// État persistant de la campagne narrative. Les listes plutôt que les dictionnaires
    /// permettent une sauvegarde fiable avec JsonUtility, y compris en WebGL.
    /// </summary>
    [Serializable]
    public class CampaignState
    {
        public int Version = 1;
        public string ActiveScenarioId = string.Empty;
        public string CurrentNodeId = string.Empty;
        public StoryRoute Route = StoryRoute.None;
        public bool ActiveScenarioCompleted;
        public List<string> CompletedObjectiveIds = new();
        public List<string> Flags = new();
        public List<CampaignIntValue> Integers = new();
        public List<string> Journal = new();

        public bool HasFlag(string flag) => !string.IsNullOrWhiteSpace(flag) && Flags.Contains(flag);

        public void SetFlag(string flag)
        {
            if (!string.IsNullOrWhiteSpace(flag) && !Flags.Contains(flag)) Flags.Add(flag);
        }

        public int GetInt(string key)
        {
            var entry = Integers.Find(value => value.Key == key);
            return entry != null ? entry.Value : 0;
        }

        public void AddInt(string key, int amount)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            var entry = Integers.Find(value => value.Key == key);
            if (entry == null)
            {
                entry = new CampaignIntValue { Key = key, Value = 0 };
                Integers.Add(entry);
            }
            entry.Value += amount;
        }

        public void AddJournal(string entry)
        {
            if (string.IsNullOrWhiteSpace(entry) || Journal.Contains(entry)) return;
            Journal.Add(entry);
        }
    }
}

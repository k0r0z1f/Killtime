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

        // --- Overworld d'Hybris (réseau de secteurs) ---
        public string PartyNodeId = "kingston";
        public List<string> UnlockedNodeIds = new();
        public List<string> VisitedNodeIds = new();

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

        /// <summary>
        /// Garantit un état overworld valide, y compris pour les sauvegardes
        /// antérieures à la carte monde (listes nulles, position vide).
        /// </summary>
        public void EnsureWorldDefaults()
        {
            UnlockedNodeIds ??= new List<string>();
            VisitedNodeIds ??= new List<string>();
            if (string.IsNullOrWhiteSpace(PartyNodeId))
                PartyNodeId = HybrisWorldMapData.StartNodeId;
            if (HybrisWorldMapData.Find(PartyNodeId) == null)
                PartyNodeId = HybrisWorldMapData.StartNodeId;
            foreach (var node in HybrisWorldMapData.ActiveNodes)
            {
                if (node != null && node.StartingUnlocked && !UnlockedNodeIds.Contains(node.Id))
                    UnlockedNodeIds.Add(node.Id);
            }
            if (!UnlockedNodeIds.Contains(PartyNodeId))
                UnlockedNodeIds.Add(PartyNodeId);
        }

        public bool IsSectorUnlocked(string nodeId)
        {
            if (string.IsNullOrWhiteSpace(nodeId)) return false;
            UnlockedNodeIds ??= new List<string>();
            return UnlockedNodeIds.Contains(nodeId);
        }

        public bool IsSectorVisited(string nodeId)
        {
            if (string.IsNullOrWhiteSpace(nodeId)) return false;
            VisitedNodeIds ??= new List<string>();
            return VisitedNodeIds.Contains(nodeId);
        }

        public void UnlockSector(string nodeId)
        {
            if (string.IsNullOrWhiteSpace(nodeId)) return;
            UnlockedNodeIds ??= new List<string>();
            if (!UnlockedNodeIds.Contains(nodeId)) UnlockedNodeIds.Add(nodeId);
        }

        public void VisitSector(string nodeId)
        {
            if (string.IsNullOrWhiteSpace(nodeId)) return;
            VisitedNodeIds ??= new List<string>();
            if (!VisitedNodeIds.Contains(nodeId)) VisitedNodeIds.Add(nodeId);
            UnlockSector(nodeId);
        }
    }
}

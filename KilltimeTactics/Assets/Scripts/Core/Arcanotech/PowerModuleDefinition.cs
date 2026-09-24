using System;

namespace Killtime.Core.Arcanotech
{
    [Serializable]
    public class PowerModuleDefinition
    {
        public ArcanotechModuleId Id;
        public string DisplayName;
        public string Description;
        public PowerCategory Category;
        public int UnitPointCost;
        public bool IsStackable;
        public int ValuePerRank;

        public PowerModuleDefinition(
            ArcanotechModuleId id,
            string displayName,
            string description,
            PowerCategory category,
            int unitPointCost,
            bool isStackable,
            int valuePerRank = 1)
        {
            Id = id;
            DisplayName = displayName;
            Description = description;
            Category = category;
            UnitPointCost = unitPointCost;
            IsStackable = isStackable;
            ValuePerRank = valuePerRank;
        }
    }

    [Serializable]
    public class PowerModuleSelection
    {
        public ArcanotechModuleId ModuleId;
        public int Rank = 1;

        public PowerModuleSelection() { }

        public PowerModuleSelection(ArcanotechModuleId moduleId, int rank = 1)
        {
            ModuleId = moduleId;
            Rank = Math.Max(1, rank);
        }
    }
}
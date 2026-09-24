using System;
using Killtime.Core.Character;
using Killtime.Core.Dice;

namespace Killtime.Core.Arcanotech
{
    [Serializable]
    public class SpellCastResult
    {
        public bool IsSuccess;
        public string SpellName;
        public string CasterName;
        public string TargetName;
        public int DistanceInTiles;
        public int BaseRange;
        public int DistancePenalty;
        public int ActionPointsSpent;
        [NonSerialized] public DiceRollResult RollResult;
        public int DirectDamageDealt;
        public int AbsoluteDamageDealt;
        public int ResidualDamageApplied;
        public int TotalDamageApplied;
        public int TargetAPLost;
        public StatusEffect InflictedStatus;
        public StatusEffect CasterBuffsApplied;
        public bool WasImmunizedBySymbiosis;
        public string CombatLog;
    }
}
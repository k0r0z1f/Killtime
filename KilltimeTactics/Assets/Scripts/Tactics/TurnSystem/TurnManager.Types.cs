using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics.Units;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Objectives;
using Killtime.Core.Character;
using Killtime.Core.Combat;
using Killtime.Core.Dice;
using Killtime.Core.Rules;

namespace Killtime.Tactics.TurnSystem
{
    public enum CombatOutcome
    {
        InProgress,
        Victory,
        Defeat
    }

    public enum TurnSystemMode
    {
        Exploration,
        CombatTurnBased
    }

    [Serializable]
    public struct InitiativeRollResult
    {
        public TacticalUnit Unit;
        public DiceType Die;
        public int RawRoll;
        public int Total;
    }
}

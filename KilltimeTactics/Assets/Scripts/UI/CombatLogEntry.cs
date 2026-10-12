// Types du feed tactique (extraits de CombatHUD.cs, comportement inchange).
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using Killtime.Core.Combat;
using Killtime.Core.Character;
using Killtime.Core.Dice;
using Killtime.Audio;
using Killtime.Tactics;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Tactics.Objectives;

namespace Killtime.UI
{
    public enum LogCategory
    {
        All,
        Combat,
        ReactionAndAP,
        VitalityAndTrauma,
        MovementAndTurns
    }

    public class CombatLogEntry
    {
        public string Timestamp;
        public string RawMessage;
        public LogCategory Category;
        public string HeaderTag;
        public Color TagColor;
        public float CreatedRealtime;
    }
}

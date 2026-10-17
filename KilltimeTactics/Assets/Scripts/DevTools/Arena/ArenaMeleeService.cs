using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Audio;
using Killtime.Core.Character;
using Killtime.Core.Combat;
using Killtime.Core.Dice;
using Killtime.Core.Inventory;
using Killtime.Core.Rules;
using Killtime.UI;

namespace Killtime.Tactics.Arena
{
    /// <summary>
    /// Entretien d'armes (RD-033), lutte/grappin/étranglement/traînée (RD-039),
    /// bousculade (RD-040), charge (RD-038), assauts groupés et poursuite (RD-084).
    /// Logique déplacée verbatim depuis CombatDevArena.
    /// ExecuteAttack (phase 7) est reçu via ChargeStrike.
    /// </summary>
    public sealed partial class ArenaMeleeService
    {
        private readonly ArenaContext _ctx;
        private readonly ArenaOverwatchService _overwatch;
        private ArenaState _state => _ctx.State;
        private IArenaHost _host => _ctx.Host;

        /// <summary>Enregistre un snapshot chrono (fourni par la façade).</summary>
        public Action<string> SnapshotRecorder;

        /// <summary>Frappe de charge : exécute la passe d'armes de mêlée (fourni par la façade, phase 7).</summary>
        public Action<TacticalUnit, TacticalUnit, BodyPart, SkillType, int, int> ChargeStrike;

        public ArenaMeleeService(ArenaContext ctx, ArenaOverwatchService overwatch)
        {
            _ctx = ctx;
            _overwatch = overwatch;
        }
    }
}

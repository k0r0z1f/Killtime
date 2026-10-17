using System;
using System.Collections;
using UnityEngine;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Audio;
using Killtime.Core.Character;
using Killtime.Core.Combat;
using Killtime.Core.Dice;
using Killtime.Core.Rules;
using Killtime.WebGL;
using Killtime.UI;
using Killtime.CameraSystem;
using Killtime.Tactics.AI;

namespace Killtime.Tactics.Arena
{
    /// <summary>
    /// Passe d'armes principale (duel aveugle Livres II §7 + VI §24.1),
    /// attaque de décor, grappin magnétique. Logique déplacée verbatim
    /// depuis CombatDevArena (ExecuteAttack + 2 routines + obstacle + grappin).
    /// La killcam cinématique reste hébergée par la façade (MonoBehaviour).
    /// </summary>
    public sealed partial class ArenaAttackService
    {
        private readonly ArenaContext _ctx;
        private readonly ArenaTargetingService _targeting;
        private readonly ArenaOverwatchService _overwatch;
        private readonly ArenaMeleeService _melee;
        private ArenaState _state => _ctx.State;
        private IArenaHost _host => _ctx.Host;

        /// <summary>Enregistre un snapshot chrono (fourni par la façade).</summary>
        public Action<string> SnapshotRecorder;

        public ArenaAttackService(ArenaContext ctx, ArenaTargetingService targeting, ArenaOverwatchService overwatch, ArenaMeleeService melee)
        {
            _ctx = ctx;
            _targeting = targeting;
            _overwatch = overwatch;
            _melee = melee;
        }
    }
}

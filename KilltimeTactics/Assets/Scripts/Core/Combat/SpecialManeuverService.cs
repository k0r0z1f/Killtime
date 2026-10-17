using System;
using Killtime.Core.Character;
using Killtime.Core.Dice;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Visibility;

namespace Killtime.Core.Combat
{
    /// <summary>
    /// Manœuvres spéciales : guet RD-030, opportunités RD-031, tenir/retarder/
    /// canalisation RD-048, lutte RD-039, bousculade RD-040, assaut groupé et
    /// poursuite RD-084. Compose setup + résolution, jamais l'inverse.
    /// Corps copiés à l'identique depuis l'ancien CombatCalculator.
    ///
    /// Façade : champs + ctor ici, contenu dans les partiels
    /// (Reactions, Grapple, Assault, Pursuit) — extraits sans modification.
    /// </summary>
    public sealed partial class SpecialManeuverService
    {
        private readonly CombatDuelContext _ctx;
        private readonly DuelSetupService _setup;
        private readonly DuelResolutionService _resolution;
        private readonly CombatProgressionService _progression;

        public SpecialManeuverService(
            CombatDuelContext ctx,
            DuelSetupService setup,
            DuelResolutionService resolution,
            CombatProgressionService progression)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            _setup = setup ?? throw new ArgumentNullException(nameof(setup));
            _resolution = resolution ?? throw new ArgumentNullException(nameof(resolution));
            _progression = progression ?? throw new ArgumentNullException(nameof(progression));
        }
    }
}

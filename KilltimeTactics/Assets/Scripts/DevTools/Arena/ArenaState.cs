using System.Collections.Generic;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.UI;

namespace Killtime.Tactics.Arena
{
    /// <summary>
    /// Source unique de vérité partagée entre la façade CombatDevArena
    /// et les services extraits (V2 : corrige F1/F5).
    /// Contient les MÊMES instances de listes que la façade (jamais de copie),
    /// pour que CoverSystem / visualizer voient toujours l'état à jour.
    /// </summary>
    public sealed class ArenaState
    {
        public TacticalUnit PlayerUnit;
        public TacticalUnit CurrentTarget;
        public readonly List<TacticalUnit> SparringDummies = new();
        public readonly List<TacticalUnit> AdditionalPlayers = new();
        public readonly List<CombatDevArena.CustomSpawnRecord> CustomSpawns = new();
        public readonly List<Core.Combat.SmokeZone> SmokeZones = new();

        public ArenaLayoutType CurrentLayout;
        public TacticalMapSaveData CurrentLoadedMap;
        public string CurrentLoadedMapPath;

        public float EmptyPATimer;
        public int AdaptiveTurnCount;

        public TacticalUnit LosLoggedAttacker;
        public TacticalUnit LosLoggedTarget;
    }
}

using System;
using Killtime.Core.Combat;
using Killtime.Core.Dice;
using Killtime.Tactics.Grid;
using Killtime.Tactics.TurnSystem;
using Killtime.CameraSystem;

namespace Killtime.Tactics.Arena
{
    /// <summary>
    /// Dépendances d'Unity et état partagé fournis par la façade aux services (V2).
    /// Construit une fois dans Awake() après EnsureDependencies().
    /// Les Func lisent l'état vivant (toggles dev modifiables à chaud).
    /// </summary>
    public sealed class ArenaContext
    {
        public TacticalHexGrid Grid;
        public HexGridVisualizer Visualizer;
        public TurnManager TurnManager;
        public HexPathfinder Pathfinder;
        public TacticalCameraController Camera;
        public CinematicDirector Cinematic;
        public Killtime.Tactics.AI.TacticalAIController AI;

        public ArenaState State;
        public ArenaResolutionGuard Guard;
        public ArenaMapStore Map;
        public IArenaHost Host;
        public CombatCalculator Calculator;
        public DiceRoller Dice;

        public Func<bool> GetEnterCombatOnMapLoad = () => true;
        public Func<bool> GetInfiniteAP = () => false;
        public Func<bool> GetKillcamEnabled = () => false;
        public Func<bool> IsStoryScene = () => false;
    }
}

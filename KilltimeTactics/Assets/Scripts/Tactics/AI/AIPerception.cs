using System;
using Killtime.Core.Character;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Visibility;

namespace Killtime.Tactics.AI
{
    /// <summary>
    /// RD-115 (phase 2) : surface sensorielle de l'IA derrière une interface.
    /// Le contrôleur ne touche plus FogOfWarManager.Instance directement :
    /// brouillard, nuit, révélations bruit/observation et dernière position
    /// connue passent par ici. Fakeable en EditMode (aucun singleton).
    /// Comportement live = FogOfWarPerception (null-safe si aucun manager).
    /// </summary>
    public interface IAIPerception
    {
        bool IsNight { get; }
        int SightRange { get; }
        FogVisionParams ResolveObserver(CharacterStats observerStats);
        bool IsCellVisible(
            HexCoordinates observer,
            float observerYawDeg,
            HexCoordinates target,
            Func<HexCoordinates, HexNode> getNode,
            FogVisionParams observerParams,
            float hexRadius);
        bool IsNoiseActive(string unitName);
        bool IsManuallyRevealed(string unitName);
        HexCoordinates? GetLastKnownPosition(string unitName);
    }

    /// <summary>
    /// Implémentation live : lit FogOfWarManager.Instance + FogOfWarSystem.
    /// Sans manager (scène sans fog) : nuit fausse, portée longue, aucune
    /// révélation — identique aux replis codés dans le contrôleur.
    /// Stateless : instance partagée via <see cref="Default"/>.
    /// </summary>
    public sealed class FogOfWarPerception : IAIPerception
    {
        public static readonly FogOfWarPerception Default = new FogOfWarPerception();

        public bool IsNight
        {
            get
            {
                var fog = FogOfWarManager.Instance;
                return fog != null && fog.IsNight;
            }
        }

        public int SightRange
        {
            get
            {
                var fog = FogOfWarManager.Instance;
                return fog != null ? fog.CurrentSightRange : FogOfWarSystem.LongSightRange;
            }
        }

        public FogVisionParams ResolveObserver(CharacterStats observerStats)
        {
            var fog = FogOfWarManager.Instance;
            bool isNight = fog != null && fog.IsNight;
            int range = fog != null ? fog.CurrentSightRange : FogOfWarSystem.LongSightRange;
            int nightRange = fog != null ? fog.NightSightRange : 4;
            return FogOfWarSystem.ResolveObserverParams(observerStats, range, isNight, nightRange);
        }

        public bool IsCellVisible(
            HexCoordinates observer,
            float observerYawDeg,
            HexCoordinates target,
            Func<HexCoordinates, HexNode> getNode,
            FogVisionParams observerParams,
            float hexRadius)
        {
            return FogOfWarSystem.IsCellVisible(
                observer, observerYawDeg, target, getNode, observerParams, hexRadius);
        }

        public bool IsNoiseActive(string unitName)
        {
            var fog = FogOfWarManager.Instance;
            return fog != null && fog.IsNoiseActive(unitName);
        }

        public bool IsManuallyRevealed(string unitName)
        {
            var fog = FogOfWarManager.Instance;
            return fog != null && fog.IsManuallyRevealed(unitName);
        }

        public HexCoordinates? GetLastKnownPosition(string unitName)
        {
            var fog = FogOfWarManager.Instance;
            return fog != null ? fog.GetLastKnownPosition(unitName) : null;
        }
    }
}

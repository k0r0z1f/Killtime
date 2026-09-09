using System.Collections.Generic;

namespace Killtime.Core.Chrono
{
    public enum TimelineId
    {
        Timeline0_Prime,
        TimelineA_Alpha,
        TimelineB_Beta,
        TimelineC_Gamma,
        TimelineD_Delta
    }

    /// <summary>
    /// Gestionnaire de causalité et de déroulement des lignes temporelles (Livre V).
    /// Permet de rembobiner un tour sans paradoxe gratuit ou de prévisualiser l'avenir via Clairsentience.
    /// </summary>
    public class TimelineBranch
    {
        public TimelineId Id { get; }
        private readonly List<TacticalTimeSnapshot> _history = new();

        public TimelineBranch(TimelineId id = TimelineId.Timeline0_Prime)
        {
            Id = id;
        }

        public void PushSnapshot(TacticalTimeSnapshot snapshot)
        {
            _history.Add(snapshot);
        }

        public TacticalTimeSnapshot GetLatestSnapshot()
        {
            return _history.Count > 0 ? _history[^1] : null;
        }

        /// <summary>
        /// Rembobine jusqu'au snapshot précédent (Action d'ancrage temporel).
        /// </summary>
        public TacticalTimeSnapshot RewindLastAction()
        {
            if (_history.Count > 1)
            {
                _history.RemoveAt(_history.Count - 1);
                return _history[^1];
            }
            return _history.Count == 1 ? _history[0] : null;
        }

        public IReadOnlyList<TacticalTimeSnapshot> GetFullChronology() => _history.AsReadOnly();
    }
}

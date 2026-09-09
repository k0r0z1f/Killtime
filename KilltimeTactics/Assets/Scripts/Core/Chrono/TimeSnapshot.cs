using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Core.Character;

namespace Killtime.Core.Chrono
{
    /// <summary>
    /// Capture ponctuelle d'état tactique le long du Fleuve du Temps (Livre V).
    /// Enregistre les PV, PA, statuts et positions des unités à une seconde précise du tour.
    /// </summary>
    [Serializable]
    public class UnitTimeSnapshot
    {
        public string UnitId;
        public int Health;
        public int ActionPoints;
        public int Essoufflement;
        public StatusEffect Status;
        public int GridCoordQ;
        public int GridCoordR;

        public UnitTimeSnapshot Clone()
        {
            return new UnitTimeSnapshot
            {
                UnitId = this.UnitId,
                Health = this.Health,
                ActionPoints = this.ActionPoints,
                Essoufflement = this.Essoufflement,
                Status = this.Status,
                GridCoordQ = this.GridCoordQ,
                GridCoordR = this.GridCoordR
            };
        }
    }

    /// <summary>
    /// Instantané complet du champ de bataille à un temps t.
    /// </summary>
    [Serializable]
    public class TacticalTimeSnapshot
    {
        public int RoundNumber;
        public float SecondInRound; // de 0.0s à 10.0s
        public string ActionDescription;
        [SerializeField] public Dictionary<string, UnitTimeSnapshot> UnitStates = new();

        public TacticalTimeSnapshot(int round, float second, string desc)
        {
            RoundNumber = round;
            SecondInRound = second;
            ActionDescription = desc;
        }

        public void RecordUnit(string unitId, CharacterStats stats, int q, int r)
        {
            UnitStates[unitId] = new UnitTimeSnapshot
            {
                UnitId = unitId,
                Health = stats.CurrentHealth,
                ActionPoints = stats.CurrentActionPoints,
                Essoufflement = stats.Essoufflement,
                Status = stats.ActiveStatus,
                GridCoordQ = q,
                GridCoordR = r
            };
        }
    }
}

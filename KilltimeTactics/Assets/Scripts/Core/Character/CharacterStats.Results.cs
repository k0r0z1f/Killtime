using System;
using Killtime.Core.Dice;
using Killtime.Core.Combat;

namespace Killtime.Core.Character
{
    // Split move-only de CharacterStats — Results : structs top-level (noms inchanges).

    /// <summary>
    /// RD-047 : Résultat du test de moral au seuil critique des 28% PV.
    /// </summary>
    [Serializable]
    public struct MoraleCheckResult
    {
        public bool Triggered;
        public bool Passed;
        public bool Surrendered;
        public bool Routed;
        [NonSerialized] public DiceRollResult Roll;
        public string Log;
    }

    /// <summary>
    /// RD-083 : résultat des Soins Majeurs (Livre VI §25.1).
    /// Pur (aucune dépendance Unity) pour tests et logs d'arène.
    /// </summary>
    [Serializable]
    public struct MajorCareResult
    {
        public int RequestedAmount;
        public int Threshold;
        public int Healed;
        public bool HadSouffrant;
        public bool MeetsThreshold;
        public bool SouffrantLifted;
        public string Log;
    }

    /// <summary>
    /// RD-045 : résultat du tick DoT de début de tour (CharacterStats.TickTurnStatus).
    /// Pur (aucune dépendance Unity) pour tests et logs d'arène.
    /// </summary>
    [Serializable]
    public class DotTickResult
    {
        public int TotalDamage;
        [System.NonSerialized]
        public System.Collections.Generic.Dictionary<StatusEffect, int> DamageByStatus = new();
        public System.Collections.Generic.List<StatusEffect> ClearedStatuses = new();
        public int ShieldAbsorbed;
        public int PaDrained;
        public FatalBlowResolution FatalResolution = FatalBlowResolution.None;
        public bool Died;
        public bool HasTick => TotalDamage > 0 || PaDrained > 0 || ClearedStatuses.Count > 0;
    }
}

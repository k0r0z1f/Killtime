using Killtime.Core.Character;

namespace Killtime.Core.Combat
{
    public enum FatalBlowResolution
    {
        None,
        InstantDeath,
        MiracleSaved,
        ForcedUnconscious,
        EligibleForLastBreath
    }

    /// <summary>
    /// Résultat complet d'une frappe ou d'un tir en combat tactique.
    /// </summary>
    public struct DamageResult
    {
        public bool IsHit;
        public bool IsBlocked;
        public bool IsCritical;
        public BodyPart TargetPart;
        public BodyPart ActualHitPart;
        public bool WasDeflected;
        
        public int Differential;
        public int RawDamage;
        public int ArmorAbsorbed;
        public int FinalDamageApplied;
        
        public bool ExceededEncaissement;
        public StatusEffect InflictedStatus;
        public FatalBlowResolution FatalResolution;
        public string CombatLog;

        public override string ToString() => CombatLog;
    }
}

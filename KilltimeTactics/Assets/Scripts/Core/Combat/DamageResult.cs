using Killtime.Core.Character;
using Killtime.Core.Dice;
using Killtime.Tactics.Grid;

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
        
        public DiceRollResult AttackRoll;
        public DiceRollResult DefenseRoll;
        
        public int Differential;
        public int RawDamage;
        public int ArmorAbsorbed;
        public int FinalDamageApplied;
        
        public bool ExceededEncaissement;
        public StatusEffect InflictedStatus;
        public FatalBlowResolution FatalResolution;
        public bool CausedKnockback;
        public bool IsCanonEntrave;
        public string CombatLog;

        // --- Livre VI §25.3 : Couvert & Visibilité ---
        public CoverType Cover;
        public int CoverAttackPenalty;
        public bool BlockedByCover;

        public override string ToString() => CombatLog;
    }
}

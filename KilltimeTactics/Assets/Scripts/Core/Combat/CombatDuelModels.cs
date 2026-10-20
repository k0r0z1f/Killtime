using System;
using Killtime.Core.Character;
using Killtime.Core.Dice;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Visibility;

namespace Killtime.Core.Combat
{
    /// <summary>
    /// Duel aveugle officiel (Livres II §7 + VI §20.1) :
    /// 1. L'attaquant DÉCLARE sa cible, son action (compétence + zone visée) et sa mise
    ///    (coût de base + PA bonus + PE), débités aussitôt. Résultat CACHÉ.
    /// 2. La cible DÉCLARE à l'aveugle son action opposée (compétence défensive ou
    ///    encaissement passif) et sa mise (base réaction + PA bonus + PE), sans avoir vu
    ///    ni le jet ni la mise de l'attaquant.
    /// 3. RÉSOLUTION : les deux dés sont lancés simultanément et révélés ensemble
    ///    (différentiel, dégâts, armure, états).
    /// Les dés découlent uniquement du niveau de compétence (paliers carac + entraînements
    /// depuis d2). Ex : Mains Nues +2 entr., FOR 5 (+2) = 4 niveaux -> d10 ;
    /// Esquive 1 entr., réf. 5 (+2) = 3 niveaux -> d8.
    /// 1 PA engagé = +1 au total, 1 PE (Essoufflement, plafond Constitution) = +1 au total.
    /// </summary>
    public sealed class AttackDuel
    {
        public CharacterStats Attacker;
        public CharacterStats Defender;
        public BodyPart TargetedPart;
        public SkillType AttackSkill;
        public SkillType DefenseSkill;
        public DiceType AttackDie;
        public DiceType DefenseDie;
        public int AttackStatusMod;
        public int DefenseStatusMod;
        public int WeaponBaseDamage;
        public int DefenderArmor;
        public bool CancelPenaltyWithAP;
        public bool DefenderWantsToDefend;

        // Coûts de base (attaquant débité au Begin, défenseur à sa déclaration)
        public int BaseAttackCost;
        public int BaseDefenseCost;
        public bool CanDefenderReact;
        public bool IsDefenderIncapacitated;
        public bool DefenderBasePaid;

        // Mises déclarées à l'aveugle (débitées à la déclaration, avant tout jet)
        public bool AttackerStakesDeclared;
        public int AttackerBonusPAApplied;
        public int AttackerPEApplied;
        public bool DefenderStakesDeclared;
        public int DefenderBonusPAApplied;
        public int DefenderPEApplied;

        // Modificateurs situationnels (hors mises déclarées)
        public int AttackBaseMod;
        public int DefenseBaseMod;
        public bool IsRangedAttack;
        public bool IsMartialArtsStrike;
        public bool AppliedCanonEntrave;
        // RD-049 : Furtivité / Embuscade (+2 attaque, pas de réaction adverse)
        public bool IsStealthAttack;
        public int StealthAttackBonus;
        // RD-048 : Canalisation / Action en Progression (+2 au jet, -1 en défense)
        public bool IsChannelAttack;
        public int ChannelAttackBonus;
        // Bonus d'arme arcanotech au jet (ex: Deglazer +1ec, Livre VII §27.2).
        // Additionné à AttackBaseMod au BeginDuel, tracé dans le log.
        public int WeaponBonusEc;
        // RD-036 Hauteur / contrebas (palier 1.5 m) : bonus/malus vertical
        // calculé par ElevationAdvantage, additionné à AttackBaseMod.
        public int ElevationAttackMod;
        public string ElevationLabel;
        // Couvert & visibilité (Livre V §21.3) : Half -1, ThreeQuarters -2,
        // Full = cible non visible => attaque impossible (BeginDuel refuse).
        public CoverType Cover;
        public int CoverAttackPenalty;
        public bool BlockedByCover;

        // RD-053 : Nuit / Obscurité (-1 tir sans lampe ni thermique)
        public bool IsNightAttack;
        public int NightAttackMod;

        // Jets uniques, lancés simultanément à la résolution avec les mises déclarées
        public DiceRollResult AttackFinalRoll;
        public DiceRollResult DefenseFinalRoll;
        public bool HasResolved;
        public int DefenderUnreactivePenalty;
    }

    /// <summary>
    /// Résultat générique d'un défi opposé aveugle (hors combat : Intimidation, story).
    /// </summary>
    public sealed class OpposedCheckResult
    {
        public CharacterStats Attacker;
        public CharacterStats Defender;
        public SkillType AttackSkill;
        public SkillType DefenseSkill;
        public DiceRollResult AttackRoll;
        public DiceRollResult DefenseRoll;
        public int AttackerBonusPAApplied;
        public int AttackerPEApplied;
        public int DefenderBonusPAApplied;
        public int DefenderPEApplied;
        public int Differential;
        public bool AttackerWins;
        public string CombatLog;
    }

    /// <summary>
    /// Résultat d'une attaque combinée de groupe (RD-084, Livre V §21.2) :
    /// sommation des résultats offensifs face à l'unique défense de la cible.
    /// </summary>
    public sealed class CombinedAttackResult
    {
        public bool IsValid;
        public string Error;
        public System.Collections.Generic.List<CharacterStats> Attackers = new();
        public CharacterStats Defender;
        public BodyPart TargetedPart;
        public SkillType AttackSkill;
        public SkillType DefenseSkill;
        public System.Collections.Generic.List<DiceRollResult> AttackRolls = new();
        public int SummedAttackTotal;
        public int SummedWeaponBase;
        public DiceRollResult DefenseRoll;
        public int UniqueDefenseTotal;
        public bool DefenderDefended;
        public int Differential;
        public int RawDamage;
        public int ArmorAbsorbed;
        public int ShieldAbsorbed;
        public int FinalDamageApplied;
        public bool ExceededEncaissement;
        public StatusEffect InflictedStatus;
        public FatalBlowResolution FatalResolution;
        public MoraleCheckResult MoraleResult;
        public bool IsHit;
        public int SlowestInitiative;
        public CharacterStats SlowestMember;
        // RD-048 : canalisation interrompue par la sommation (dégât net > encaissement).
        public bool ChannelInterrupted;
        public string InterruptedChannelLabel;
        public string CombatLog;
    }

    /// <summary>
    /// Entrée individuelle d'un poursuivant dans un round de course-poursuite.
    /// </summary>
    public sealed class PursuitDuelEntry
    {
        public CharacterStats Pursuer;
        public DiceRollResult PursuerRoll;
        public int Differential;
        public bool PreyWins;
    }

    /// <summary>
    /// Résultat d'un round de course-poursuite (RD-084, Livre V §21.1) :
    /// un unique jet de la proie opposé à chaque poursuivant.
    /// </summary>
    public sealed class PursuitRoundResult
    {
        public CharacterStats Prey;
        public DiceRollResult PreyRoll;
        public System.Collections.Generic.List<PursuitDuelEntry> Entries = new();
        public int FailuresAdded;
        public int TotalFailures;
        public int FailuresToCapture;
        public bool Caught;
        public string CombatLog;
    }
}

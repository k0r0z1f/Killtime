using System;
using Killtime.Core.Dice;
using Killtime.Core.Combat;

namespace Killtime.Core.Character
{
    /// <summary>
    /// Gestion de l'état dynamique d'un personnage en combat (Livre I, VI et VII).
    /// Intègre la réserve de PA, les seuils d'encaissement et l'arbre de résolution à 0 PV.
    /// </summary>
    [Serializable]
    public class CharacterStats
    {
        public string Name { get; set; }
        public Attributes Attributes { get; set; }

        public int MaxHealth { get; private set; }
        public int CurrentHealth { get; set; }
        public int EncaissementThreshold { get; private set; }

        public int MaxActionPoints { get; private set; }
        public int CurrentActionPoints { get; set; }

        public int Essoufflement { get; set; }
        public StatusEffect ActiveStatus { get; set; }

        public int BaseArmorAbsorption { get; set; }
        public int AttacksThisTurn { get; private set; }

        public bool IsDead { get; set; }
        public bool IsInLastBreath { get; set; }
        public FatalBlowResolution LastFatalBlowResolution { get; set; } = FatalBlowResolution.None;

        public CharacterSheet Sheet { get; set; }

        public CharacterStats(string name, Attributes attributes, int baseArmor = 0, CharacterSheet sheet = null)
        {
            Name = name;
            Attributes = attributes;
            BaseArmorAbsorption = baseArmor;
            Sheet = sheet;
            ActiveStatus = StatusEffect.None;
            Essoufflement = 0;
            AttacksThisTurn = 0;
            IsDead = false;
            IsInLastBreath = false;

            RecalculateDerivedStats();
            ResetTurn();
        }

        public DiceType GetSkillDie(SkillType skill)
        {
            int baseRank = SkillDefinitions.GetBaseRank(skill, Attributes);
            int training = Sheet != null ? Sheet.GetSkill(skill).TrainingLevel : 0;
            return SkillDefinitions.CalculateSkillDie(baseRank + training);
        }

        public int GetStatusModifier(SkillType skill, bool isOffensive = true)
        {
            if (ActiveStatus == StatusEffect.None) return 0;

            int mod = 0;

            // Livre VII : Déstabilisé (-2 à toutes les épreuves)
            if (ActiveStatus.HasFlag(StatusEffect.Destabilise)) mod -= 2;

            // Livre VII : Étourdi (-1 sur tous les jets)
            if (ActiveStatus.HasFlag(StatusEffect.Etourdi)) mod -= 1;

            // Livre VII : À Terre (-1 attaque, -2 défense/esquive)
            if (ActiveStatus.HasFlag(StatusEffect.ATerre))
            {
                mod += isOffensive ? -1 : -2;
            }

            // Livre VII : Aveugle (-4 tirs distance, -2 mêlée et défense)
            if (ActiveStatus.HasFlag(StatusEffect.Aveugle))
            {
                bool isRanged = (skill == SkillType.Ballistique || skill == SkillType.ProjectilesTir);
                mod += isRanged ? -4 : -2;
            }

            // Livre VII : Immobilisé (-2 esquive active)
            if (ActiveStatus.HasFlag(StatusEffect.Immobilise))
            {
                if (!isOffensive && (skill == SkillType.Esquive || skill == SkillType.Acrobatie)) mod -= 2;
            }

            // Livre VII : Agonisant (-2 détresse vitale / panique)
            if (ActiveStatus.HasFlag(StatusEffect.Agonisant)) mod -= 2;

            return mod;
        }

        public string GetStatusBreakdownString(SkillType skill, bool isOffensive = true)
        {
            if (ActiveStatus == StatusEffect.None) return string.Empty;

            var parts = new System.Collections.Generic.List<string>();
            if (ActiveStatus.HasFlag(StatusEffect.Destabilise)) parts.Add("Déstabilisé -2");
            if (ActiveStatus.HasFlag(StatusEffect.Etourdi)) parts.Add("Étourdi -1");
            if (ActiveStatus.HasFlag(StatusEffect.ATerre)) parts.Add(isOffensive ? "À Terre -1" : "À Terre -2");
            if (ActiveStatus.HasFlag(StatusEffect.Aveugle))
            {
                bool isRanged = (skill == SkillType.Ballistique || skill == SkillType.ProjectilesTir);
                parts.Add(isRanged ? "Aveugle -4" : "Aveugle -2");
            }
            if (ActiveStatus.HasFlag(StatusEffect.Immobilise) && !isOffensive && (skill == SkillType.Esquive || skill == SkillType.Acrobatie))
            {
                parts.Add("Immobilisé -2");
            }
            if (ActiveStatus.HasFlag(StatusEffect.Agonisant)) parts.Add("Agonisant -2");

            return parts.Count > 0 ? string.Join(", ", parts) : string.Empty;
        }

        public int GetSkillModifier(SkillType skill, bool isOffensive = true)
        {
            return SkillDefinitions.GetPrimaryModifier(skill, Attributes) + GetStatusModifier(skill, isOffensive);
        }

        public int GetSkillRank(SkillType skill)
        {
            int baseRank = SkillDefinitions.GetBaseRank(skill, Attributes);
            int training = Sheet != null ? Sheet.GetSkill(skill).TrainingLevel : 0;
            return baseRank + training;
        }

        public bool HasSpecialization(string specializationName)
        {
            return Sheet != null && Sheet.UnlockedSpecializations != null && Sheet.UnlockedSpecializations.Contains(specializationName);
        }

        public bool CanAttackWithSkill(SkillType skill)
        {
            return CanAttack(GetSkillDie(skill));
        }

        public bool CanDefendActively()
        {
            if (!IsAlive) return false;
            if (ActiveStatus.HasFlag(StatusEffect.Inconscient)) return false;
            if (ActiveStatus.HasFlag(StatusEffect.Sonne)) return false;
            if (ActiveStatus.HasFlag(StatusEffect.Paralyse)) return false;
            return true;
        }

        public void RecalculateDerivedStats()
        {
            MaxActionPoints = Attributes.CalculateBaseActionPoints();
            EncaissementThreshold = Attributes.CalculateEncaissement();
            MaxHealth = Attributes.CalculateLethalMaximum();
            CurrentHealth = MaxHealth;
        }

        public void ResetTurn()
        {
            if (IsInLastBreath)
            {
                CurrentActionPoints = (int)Math.Round(MaxActionPoints * Rules.CoreRulesConfig.Instance.LastBreathAPRatio);
            }
            else
            {
                CurrentActionPoints = MaxActionPoints;
            }

            AttacksThisTurn = 0;
        }

        public bool ConsumeActionPoints(int cost)
        {
            int effectiveCost = ActiveStatus.HasFlag(StatusEffect.Ralenti) 
                ? cost * Rules.CoreRulesConfig.Instance.RalentiAPMultiplier 
                : cost;

            if (CurrentActionPoints >= effectiveCost)
            {
                CurrentActionPoints -= effectiveCost;

                if (IsInLastBreath)
                {
                    Essoufflement++;
                    if (Essoufflement >= Attributes.Constitution)
                    {
                        IsInLastBreath = false;
                        IsDead = true;
                        ActiveStatus |= StatusEffect.Inconscient;
                    }
                }

                return true;
            }

            return false;
        }

        public int GetMaxAttacksAllowed(DiceType attackDie)
        {
            var cfg = Rules.CoreRulesConfig.Instance;
            return attackDie switch
            {
                DiceType.TwoD6 or DiceType.TwoD8 or DiceType.TwoD10 or DiceType.TwoD12 or DiceType.TwoD12Plus10 => cfg.MultiDiceAttackActionLimit,
                _ => cfg.SingleAttackActionLimit
            };
        }

        public bool CanAttack(DiceType attackDie)
        {
            if (!IsAlive) return false;
            if (ActiveStatus.HasFlag(StatusEffect.Inconscient) || 
                ActiveStatus.HasFlag(StatusEffect.Sonne) || 
                ActiveStatus.HasFlag(StatusEffect.Paralyse))
            {
                return false;
            }
            return AttacksThisTurn < GetMaxAttacksAllowed(attackDie);
        }

        public void RegisterAttack()
        {
            AttacksThisTurn++;
        }

        public bool TakeEmergencyBreath(int bonusAP = -1)
        {
            var cfg = Rules.CoreRulesConfig.Instance;
            int appliedBonus = (bonusAP < 0) ? cfg.EmergencyBreathBonusAP : bonusAP;

            if (Essoufflement >= Attributes.Constitution)
            {
                return false;
            }

            Essoufflement += cfg.EmergencyBreathEssoufflementCost;
            CurrentActionPoints += appliedBonus;
            return true;
        }

        public void RecoverBreath()
        {
            if (Essoufflement > 0)
            {
                Essoufflement--;
            }
        }

        public FatalBlowResolution EvaluateFatalBlow(BodyPart hitPart, int finalDamageDealt)
        {
            bool exceedsEncaissement = finalDamageDealt > EncaissementThreshold;

            if (exceedsEncaissement)
            {
                if (hitPart == BodyPart.Tete || hitPart == BodyPart.YeuxVisage)
                {
                    if (Attributes.PointsMiracle > 0)
                    {
                        var attrs = Attributes;
                        attrs.PointsMiracle--;
                        Attributes = attrs;
                        CurrentHealth = 1;
                        ActiveStatus |= StatusEffect.Inconscient | StatusEffect.ATerre;
                        LastFatalBlowResolution = FatalBlowResolution.MiracleSaved;
                        return FatalBlowResolution.MiracleSaved;
                    }

                    CurrentHealth = 0;
                    IsDead = true;
                    ActiveStatus |= StatusEffect.Inconscient | StatusEffect.ATerre;
                    LastFatalBlowResolution = FatalBlowResolution.InstantDeath;
                    return FatalBlowResolution.InstantDeath;
                }

                CurrentHealth = 0;
                ActiveStatus |= StatusEffect.Inconscient | StatusEffect.ATerre;
                if (hitPart == BodyPart.CoeurPoumons || hitPart == BodyPart.CouTrachee)
                {
                    ActiveStatus |= StatusEffect.Agonisant | StatusEffect.Saignement;
                }
                LastFatalBlowResolution = FatalBlowResolution.ForcedUnconscious;
                return FatalBlowResolution.ForcedUnconscious;
            }

            CurrentHealth = 0;
            LastFatalBlowResolution = FatalBlowResolution.EligibleForLastBreath;
            return FatalBlowResolution.EligibleForLastBreath;
        }

        public void ChooseSombrer()
        {
            IsInLastBreath = false;
            ActiveStatus |= StatusEffect.Inconscient | StatusEffect.ATerre;
            ActiveStatus &= ~StatusEffect.Agonisant;
        }

        public void ChooseLastBreath()
        {
            IsInLastBreath = true;
            ActiveStatus |= StatusEffect.ATerre | StatusEffect.Agonisant;
            ActiveStatus &= ~StatusEffect.Inconscient;
        }

        public bool IsAlive => !IsDead && (CurrentHealth > 0 || IsInLastBreath) && !ActiveStatus.HasFlag(StatusEffect.Inconscient);
    }
}
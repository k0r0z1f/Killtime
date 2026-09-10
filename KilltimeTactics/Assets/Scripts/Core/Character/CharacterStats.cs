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

        public CharacterStats(string name, Attributes attributes, int baseArmor = 0)
        {
            Name = name;
            Attributes = attributes;
            BaseArmorAbsorption = baseArmor;
            ActiveStatus = StatusEffect.None;
            Essoufflement = 0;
            AttacksThisTurn = 0;
            IsDead = false;
            IsInLastBreath = false;

            RecalculateDerivedStats();
            ResetTurn();
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
                CurrentActionPoints = MaxActionPoints / 2;
            }
            else
            {
                CurrentActionPoints = MaxActionPoints;
            }

            AttacksThisTurn = 0;
        }

        public bool ConsumeActionPoints(int cost)
        {
            int effectiveCost = ActiveStatus.HasFlag(StatusEffect.Ralenti) ? cost * 2 : cost;

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
            return attackDie switch
            {
                DiceType.TwoD6 or DiceType.TwoD8 or DiceType.TwoD10 or DiceType.TwoD12 or DiceType.TwoD12Plus10 => 2,
                _ => 1
            };
        }

        public bool CanAttack(DiceType attackDie)
        {
            if (!IsAlive) return false;
            return AttacksThisTurn < GetMaxAttacksAllowed(attackDie);
        }

        public void RegisterAttack()
        {
            AttacksThisTurn++;
        }

        public bool TakeEmergencyBreath(int bonusAP = 2)
        {
            if (Essoufflement >= Attributes.Constitution)
            {
                return false;
            }

            Essoufflement += 1;
            CurrentActionPoints += bonusAP;
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
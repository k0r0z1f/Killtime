using System;
using Killtime.Core.Dice;
using Killtime.Core.Combat;

namespace Killtime.Core.Character
{
    // Split move-only de CharacterStats — Actions : Economie PA, souffle, redline, PE de duel.
    public partial class CharacterStats
    {

        /// <summary>
        /// Calcule le coût PA effectif après malus d'armure lourde portée et multiplicateur Ralenti.
        /// </summary>
        public int GetEffectiveApCost(int baseCost, bool ignoreArmorMalus = false)
        {
            int withArmor = baseCost + (ignoreArmorMalus ? 0 : GetWornApMalus());
            bool isRalenti = ActiveStatus.HasFlag(StatusEffect.Ralenti) && !HasSpecialization("Élan Sans Drag");
            return isRalenti
                ? withArmor * Rules.CoreRulesConfig.Instance.RalentiAPMultiplier
                : withArmor;
        }

        /// <summary>
        /// Vrai si le combattant dispose d'assez de PA pour payer le coût effectif.
        /// </summary>
        public bool CanAffordActionPoints(int baseCost, bool ignoreArmorMalus = false)
        {
            return CurrentActionPoints >= GetEffectiveApCost(baseCost, ignoreArmorMalus);
        }

        /// <summary>
        /// Paie un coût PA. Les blindages lourds portés ajoutent leur malus à TOUTE action
        /// (Livre VII §28.1), avant le multiplicateur Ralenti. Les mises de duel aveugle
        /// (1 PA = +1 au jet) en sont exemptées : passer ignoreArmorMalus.
        /// </summary>
        public bool ConsumeActionPoints(int cost, bool ignoreArmorMalus = false)
        {
            int effectiveCost = GetEffectiveApCost(cost, ignoreArmorMalus);

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
            if (!IsAlive || IsSurrendered) return false;
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
            int maxEss = Attributes.Constitution + (HasSpecialization("Restructuration Neurale : Volonté d'Inflexible") ? 2 : 0);

            if (Essoufflement >= maxEss)
            {
                return false;
            }

            Essoufflement += cfg.EmergencyBreathEssoufflementCost;
            CurrentActionPoints += appliedBonus;
            return true;
        }

        public void RecoverBreath(int amount = 1)
        {
            if (Essoufflement > 0)
            {
                Essoufflement = Math.Max(0, Essoufflement - Math.Max(1, amount));
            }
        }

        public bool TriggerRedlineAP(int extraAP = 1)
        {
            int maxEss = Attributes.Constitution + (HasSpecialization("Restructuration Neurale : Volonté d'Inflexible") ? 2 : 0);
            if (!IsAlive || Essoufflement >= maxEss) return false;
            CurrentActionPoints += extraAP;
            Essoufflement += extraAP;
            if (Essoufflement >= maxEss)
            {
                ApplyStatus(StatusEffect.Ralenti, 1);
            }
            return true;
        }

        /// <summary>
        /// Duel aveugle (Livres II §7 + VI §20) : PE encore engageables dans une
        /// déclaration (plafond = Constitution, cf. Livre V §20.2).
        /// </summary>
        public int GetSpendablePE()
        {
            int maxEss = Attributes.Constitution + (HasSpecialization("Restructuration Neurale : Volonté d'Inflexible") ? 2 : 0);
            return Math.Max(0, maxEss - Essoufflement);
        }

        /// <summary>
        /// Engage des points d'Essoufflement dans une déclaration de duel aveugle.
        /// Chaque PE ajoute +DuelPEBonusPerPoint au total et +1 Essoufflement immédiat.
        /// Retourne le nombre de PE réellement engagés (plafonné par la Constitution).
        /// </summary>
        public int SpendDuelPE(int requested)
        {
            if (requested <= 0 || !IsAlive) return 0;
            int applied = Math.Min(requested, GetSpendablePE());
            Essoufflement += applied;
            return applied;
        }
    }
}

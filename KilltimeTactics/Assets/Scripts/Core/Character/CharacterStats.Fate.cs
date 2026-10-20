using System;
using Killtime.Core.Dice;
using Killtime.Core.Combat;

namespace Killtime.Core.Character
{
    // Split move-only de CharacterStats — Fate : Equipement/bouclier, destin, moral RD-047.
    public partial class CharacterStats
    {

        /// <summary>Blindage porté : somme des protections des armures équipées (Livre VII §28.1).</summary>
        public int GetWornArmorBonus()
        {
            if (Sheet?.Inventory == null) return 0;
            int total = 0;
            for (int i = 0; i < Sheet.Inventory.Count; i++)
            {
                var it = Sheet.Inventory[i];
                if (it == null || !it.IsEquipped) continue;
                if (it.Type == Killtime.Core.Inventory.ItemType.Armor)
                    total += Math.Max(0, it.ArmorProtection);
            }
            return total;
        }

        /// <summary>Malus PA permanent des blindages lourds portés (Livre VII §28.1).</summary>
        public int GetWornApMalus()
        {
            if (Sheet?.Inventory == null) return 0;
            int total = 0;
            for (int i = 0; i < Sheet.Inventory.Count; i++)
            {
                var it = Sheet.Inventory[i];
                if (it == null || !it.IsEquipped) continue;
                if (it.Type == Killtime.Core.Inventory.ItemType.Armor)
                    total += it.ApCostModifier;
            }
            return Math.Max(0, total);
        }

        /// <summary>Recalcule la barrière max depuis le champ porté ; crédite le gain immédiat.</summary>
        public void RecalcShieldMax()
        {
            int max = 0;
            if (Sheet?.Inventory != null)
            {
                for (int i = 0; i < Sheet.Inventory.Count; i++)
                {
                    var it = Sheet.Inventory[i];
                    if (it == null || !it.IsEquipped) continue;
                    if (it.ShieldHP > 0) max += it.ShieldHP;
                }
            }
            max = Math.Max(0, max);
            if (max > MaxShieldHP) CurrentShieldHP += max - MaxShieldHP;
            MaxShieldHP = max;
            CurrentShieldHP = Math.Clamp(CurrentShieldHP, 0, Math.Max(0, MaxShieldHP));
        }

        /// <summary>Recharge auto hors combat (Livre VII §28.2).</summary>
        public void RefillShield()
        {
            RecalcShieldMax();
            CurrentShieldHP = MaxShieldHP;
        }

        /// <summary>Absorbe les dégâts sur la barrière, retourne le reste pour l'armure/la chair.</summary>
        public int AbsorbShield(int damage)
        {
            if (damage <= 0 || CurrentShieldHP <= 0) return Math.Max(0, damage);
            int taken = Math.Min(CurrentShieldHP, damage);
            CurrentShieldHP -= taken;
            return damage - taken;
        }

        public FatalBlowResolution EvaluateFatalBlow(BodyPart hitPart, int finalDamageDealt)
        {
            bool exceedsEncaissement = finalDamageDealt > EncaissementThreshold;

            if (HasSpecialization("Restructuration Neurale : Volonté d'Inflexible")
                && (CurrentHealth - finalDamageDealt) >= -5
                && hitPart != BodyPart.Tete
                && hitPart != BodyPart.YeuxVisage)
            {
                CurrentHealth -= finalDamageDealt;
                LastFatalBlowResolution = FatalBlowResolution.None;
                return FatalBlowResolution.None;
            }

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

                // Fiche héroïque : Cœur d'Éclosion (voir MinaCharacter.TryTriggerHeartOfBloom).
                if (MinaCharacter.TryTriggerHeartOfBloom(this))
                {
                    LastFatalBlowResolution = FatalBlowResolution.MiracleSaved;
                    return FatalBlowResolution.MiracleSaved;
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

            // Fiche héroïque : Cœur d'Éclosion (voir MinaCharacter.TryTriggerHeartOfBloom).
            if (MinaCharacter.TryTriggerHeartOfBloom(this))
            {
                LastFatalBlowResolution = FatalBlowResolution.MiracleSaved;
                return FatalBlowResolution.MiracleSaved;
            }

            CurrentHealth = 0;
            LastFatalBlowResolution = FatalBlowResolution.EligibleForLastBreath;
            return FatalBlowResolution.EligibleForLastBreath;
        }

        public bool HasUsedHeartOfBloom { get; set; } = false;

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

        public bool IsAlive => !IsDead && (CurrentHealth > 0 || IsInLastBreath || (HasSpecialization("Restructuration Neurale : Volonté d'Inflexible") && CurrentHealth >= -5)) && !ActiveStatus.HasFlag(StatusEffect.Inconscient);

        /// <summary>
        /// RD-047 : Test de moral au seuil critique des 28% PV (Livre VI).
        /// Épreuve basée sur le palier max(Charisme, Instinct) vs SD standard.
        /// Réussite = moral intact. Échec = Reddition si IA Survivor, Déroute (Agonisant) sinon.
        /// </summary>
        public MoraleCheckResult CheckMoraleAtThreshold(DiceRoller diceRoller, int targetDC = 6)
        {
            if (HasTestedMorale || !IsAlive || IsSurrendered || CurrentHealth <= 0)
                return default;

            diceRoller ??= new DiceRoller();

            int threshold = (int)Math.Ceiling(MaxHealth * 0.28f);
            if (CurrentHealth > threshold)
                return default;

            HasTestedMorale = true;
            int chaVal = Attributes.Charisme;
            int insVal = Attributes.Instinct;
            int bestAttr = Math.Max(chaVal, insVal);
            var die = SkillDefinitions.DieFromTotalSteps(SkillDefinitions.CharacteristicSteps(bestAttr));
            int statusMod = GetStatusModifier(SkillType.Intuition, false);

            var roll = diceRoller.Roll(die, statusMod, targetDC);
            bool passed = roll.IsSuccess;

            var result = new MoraleCheckResult
            {
                Triggered = true,
                Passed = passed,
                Roll = roll
            };

            if (passed)
            {
                result.Log = $"🧠 <b>JET DE MORAL (28% PV)</b> : {Name} résiste à la panique [Max(CHA {chaVal}, INS {insVal}) ➔ {die}, Total {roll.Total} vs SD {targetDC}] ➔ <b>MORAL INTACT</b>.";
            }
            else
            {
                if (IsSurvivor && !IsPlayerControlled)
                {
                    IsSurrendered = true;
                    result.Surrendered = true;
                    result.Log = $"🏳️ <b>JET DE MORAL ÉCHOUÉ (28% PV)</b> : {Name} [Total {roll.Total} vs SD {targetDC}] ➔ <b>REDDITION (IA Survivor)</b> ! L'unité dépose les armes.";
                }
                else
                {
                    ApplyStatus(StatusEffect.Agonisant, -1);
                    result.Routed = true;
                    result.Log = $"😱 <b>JET DE MORAL ÉCHOUÉ (28% PV)</b> : {Name} [Total {roll.Total} vs SD {targetDC}] ➔ <b>DÉROUTE</b> ! Altération [Agonisant] infligée (Fuite éperdue).";
                }
            }

            return result;
        }

        /// <summary>
        /// RD-192 : Réarmement moral dev/test (aucun appelant live).
        /// Annule un test consommé, lève reddition et déroute pour re-tester
        /// à la demande. Les chemins live réarment via Heal/ResetTurn au-dessus
        /// de 28 % (sans lever IsSurrendered) ; seul ce hook dev fait table rase.
        /// </summary>
        public void RearmMorale()
        {
            HasTestedMorale = false;
            IsSurrendered = false;
            RemoveStatus(StatusEffect.Agonisant);
        }
    }
}

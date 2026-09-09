using System;
using Killtime.Core.Character;
using Killtime.Core.Dice;

namespace Killtime.Core.Combat
{
    /// <summary>
    /// Résolution mathématique intégrale des passes d'armes et tirs ciblés (Livre VI).
    /// </summary>
    public class CombatCalculator
    {
        private readonly DiceRoller _diceRoller;
        private readonly Random _random;

        public CombatCalculator(DiceRoller diceRoller = null)
        {
            _diceRoller = diceRoller ?? new DiceRoller();
            _random = new Random();
        }

        /// <summary>
        /// Résout une attaque ciblée selon les règles du Chapitre 26 :
        /// - Malus de visée (-1) ou annulation si dépense préalable de 1 PA.
        /// - Différentiel Positif : coup chirurgical sur la zone visée, le différentiel s'ajoute aux dégâts.
        /// - Différentiel Égal à 0 : déviation sur zone adjacente, dégâts de base.
        /// - Différentiel Négatif : parade/esquive parfaite, 0 dégât.
        /// </summary>
        public DamageResult ResolveTargetedAttack(
            CharacterStats attacker,
            CharacterStats defender,
            BodyPart targetedPart,
            DiceType attackDie,
            int attackModifier,
            DiceType defenseDie,
            int defenseModifier,
            int weaponBaseDamage,
            bool cancelPenaltyWithAP = false,
            int defenderArmor = 0)
        {
            var targetInfo = BodyPartInfo.GetInfo(targetedPart);

            // Coût en PA de base pour l'attaque (ex: 2 PA)
            int apCost = 2;
            if (cancelPenaltyWithAP) apCost += 1;

            if (!attacker.ConsumeActionPoints(apCost))
            {
                return new DamageResult
                {
                    IsHit = false,
                    IsBlocked = false,
                    CombatLog = $"{attacker.Name} n'a pas assez de PA ({attacker.CurrentActionPoints}/{apCost}) pour attaquer !"
                };
            }

            int finalAttackMod = attackModifier;
            if (!cancelPenaltyWithAP)
            {
                finalAttackMod += targetInfo.DifficultyModifier; // applique le malus de visée
            }

            // Jets d'attaque et de défense
            var attackRoll = _diceRoller.Roll(attackDie, finalAttackMod, 10);
            var defenseRoll = _diceRoller.Roll(defenseDie, defenseModifier, 10);

            int differential = attackRoll.Total - defenseRoll.Total;

            // 1. Différentiel Négatif : parade/esquive complète
            if (differential < 0)
            {
                return new DamageResult
                {
                    IsHit = false,
                    IsBlocked = true,
                    Differential = differential,
                    CombatLog = $"{attacker.Name} vise {targetInfo.DisplayName} mais {defender.Name} bloque/esquive l'attaque ! (Différentiel: {differential})"
                };
            }

            // 2. Différentiel Égal à 0 : Déviation sur zone adjacente
            bool wasDeflected = (differential == 0);
            BodyPart actualHitPart = targetedPart;

            if (wasDeflected)
            {
                actualHitPart = GetAdjacentBodyPart(targetedPart);
            }

            // 3. Calcul des dégâts
            int rawDamage = weaponBaseDamage;
            if (differential > 0)
            {
                // Le différentiel s'ajoute directement aux dégâts
                rawDamage += differential;
            }

            if (attackRoll.IsCriticalSuccess)
            {
                rawDamage *= targetInfo.CriticalDamageMultiplier;
            }

            // Réduction d'armure
            int totalArmor = defenderArmor + defender.BaseArmorAbsorption;
            int absorbed = Math.Min(totalArmor, rawDamage);
            int finalDamage = Math.Max(0, rawDamage - absorbed);

            // Application des dégâts aux PV du défenseur
            defender.CurrentHealth = Math.Max(0, defender.CurrentHealth - finalDamage);

            // Dépassement d'encaissement (Livre I & VII)
            bool exceededEncaissement = finalDamage > defender.EncaissementThreshold;
            StatusEffect inflictedStatus = StatusEffect.None;

            if (exceededEncaissement || attackRoll.IsCriticalSuccess)
            {
                inflictedStatus = DetermineInflictedStatus(actualHitPart);
                defender.ActiveStatus |= inflictedStatus;
            }

            // Vérification de KO ou létalité
            if (defender.CurrentHealth <= 0)
            {
                defender.ActiveStatus |= StatusEffect.Inconscient;
            }

            string log = $"{attacker.Name} frappe {defender.Name} ";
            if (wasDeflected)
            {
                log += $"(DÉVIATION : visait {targetInfo.DisplayName}, touche {BodyPartInfo.GetInfo(actualHitPart).DisplayName}) ";
            }
            else
            {
                log += $"sur {targetInfo.DisplayName} ! ";
            }
            log += $"Dégâts: {finalDamage} (Bruts: {rawDamage} - Armure: {absorbed}) | PV Restants: {defender.CurrentHealth}/{defender.MaxHealth}";

            if (inflictedStatus != StatusEffect.None)
            {
                log += $" [TRAUMATISME: {inflictedStatus}]";
            }

            return new DamageResult
            {
                IsHit = true,
                IsBlocked = false,
                IsCritical = attackRoll.IsCriticalSuccess,
                TargetPart = targetedPart,
                ActualHitPart = actualHitPart,
                WasDeflected = wasDeflected,
                Differential = differential,
                RawDamage = rawDamage,
                ArmorAbsorbed = absorbed,
                FinalDamageApplied = finalDamage,
                ExceededEncaissement = exceededEncaissement,
                InflictedStatus = inflictedStatus,
                CombatLog = log
            };
        }

        private BodyPart GetAdjacentBodyPart(BodyPart targeted)
        {
            return targeted switch
            {
                BodyPart.Tete or BodyPart.YeuxVisage => _random.Next(0, 2) == 0 ? BodyPart.CouTrachee : BodyPart.Torse,
                BodyPart.CouTrachee => _random.Next(0, 2) == 0 ? BodyPart.BrasDroit : BodyPart.Torse,
                BodyPart.CoeurPoumons => BodyPart.Torse,
                BodyPart.Torse => _random.Next(0, 2) == 0 ? BodyPart.BrasGauche : BodyPart.Jambes,
                BodyPart.BrasDroit or BodyPart.BrasGauche => BodyPart.Torse,
                BodyPart.Jambes => BodyPart.Torse,
                _ => BodyPart.Torse
            };
        }

        private StatusEffect DetermineInflictedStatus(BodyPart hitPart)
        {
            return hitPart switch
            {
                BodyPart.Tete => StatusEffect.Sonne | StatusEffect.Etourdi,
                BodyPart.YeuxVisage => StatusEffect.Aveugle | StatusEffect.Saignement,
                BodyPart.CouTrachee => StatusEffect.Asphyxie,
                BodyPart.CoeurPoumons => StatusEffect.Agonisant | StatusEffect.Saignement,
                BodyPart.Torse => StatusEffect.Destabilise,
                BodyPart.BrasDroit or BodyPart.BrasGauche => StatusEffect.Destabilise,
                BodyPart.Jambes => StatusEffect.ATerre | StatusEffect.Ralenti,
                _ => StatusEffect.Destabilise
            };
        }
    }
}

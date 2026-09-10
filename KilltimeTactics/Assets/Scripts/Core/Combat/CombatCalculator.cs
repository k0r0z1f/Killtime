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
            int defenderArmor = 0,
            int attackerBonusAP = 0,
            int defenderBonusAP = 0)
        {
            var targetInfo = BodyPartInfo.GetInfo(targetedPart);

            // 1. Temps de l'Attaquant : Coût de base + PA bonus injectés
            int baseAttackCost = cancelPenaltyWithAP ? 3 : 2;
            int safeAttackerBonus = Math.Max(0, attackerBonusAP);
            int totalAttackerCost = baseAttackCost + safeAttackerBonus;

            if (!attacker.ConsumeActionPoints(totalAttackerCost))
            {
                return new DamageResult
                {
                    IsHit = false,
                    IsBlocked = false,
                    CombatLog = $"{attacker.Name} n'a pas assez de PA ({attacker.CurrentActionPoints}/{totalAttackerCost}) pour attaquer !"
                };
            }

            // 2. Temps du Défenseur : Coût de base de réaction (1 PA) + PA bonus défensifs
            int baseDefenseCost = 1;
            bool canDefenderReact = defender.CurrentActionPoints >= baseDefenseCost;
            int actualDefenderBonus = 0;

            if (canDefenderReact)
            {
                int requestedDefenseCost = baseDefenseCost + Math.Max(0, defenderBonusAP);
                int affordableDefenseCost = Math.Min(defender.CurrentActionPoints, requestedDefenseCost);
                defender.ConsumeActionPoints(affordableDefenseCost);
                actualDefenderBonus = affordableDefenseCost - baseDefenseCost;
            }

            // 3. Calcul des modificateurs finaux
            int finalAttackMod = attackModifier + safeAttackerBonus;
            if (!cancelPenaltyWithAP)
            {
                finalAttackMod += targetInfo.DifficultyModifier;
            }

            int finalDefenseMod = defenseModifier;
            if (canDefenderReact)
            {
                finalDefenseMod += actualDefenderBonus;
            }
            else
            {
                // Défenseur surpris sans PA pour réagir : malus réflexe
                finalDefenseMod -= 2;
            }

            // 4. Lancer des dés et différentiel net
            var attackRoll = _diceRoller.Roll(attackDie, finalAttackMod, 10);
            var defenseRoll = _diceRoller.Roll(defenseDie, finalDefenseMod, 10);

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

            bool exceededEncaissement = finalDamage > defender.EncaissementThreshold;
            StatusEffect inflictedStatus = StatusEffect.None;

            if (exceededEncaissement || attackRoll.IsCriticalSuccess)
            {
                inflictedStatus = DetermineInflictedStatus(actualHitPart);
                defender.ActiveStatus |= inflictedStatus;
            }

            FatalBlowResolution fatalRes = FatalBlowResolution.None;
            if (defender.CurrentHealth - finalDamage <= 0)
            {
                fatalRes = defender.EvaluateFatalBlow(actualHitPart, finalDamage);
            }
            else
            {
                defender.CurrentHealth -= finalDamage;
            }

            string log = $"{attacker.Name} attaque {defender.Name} ";
            log += $"[PA Attaque: {baseAttackCost}+{safeAttackerBonus} | PA Défense: {(canDefenderReact ? $"{baseDefenseCost}+{actualDefenderBonus}" : "0 (Aucune réaction, -2)")}] ";
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

            if (fatalRes == FatalBlowResolution.InstantDeath)
            {
                log += " 💀 [MORT INSTANTANÉE : TÊTE DÉTRUITE]";
            }
            else if (fatalRes == FatalBlowResolution.MiracleSaved)
            {
                log += " 🔮 [POINT DE MIRACLE : SURVIE À 1 PV]";
            }
            else if (fatalRes == FatalBlowResolution.ForcedUnconscious)
            {
                log += " 💥 [SYNCOPE TRAUMATIQUE : K.O. FORCÉ]";
            }
            else if (fatalRes == FatalBlowResolution.EligibleForLastBreath)
            {
                log += " ⚡ [0 PV : ÉLIGIBLE SOMBRER OU DERNIER SOUFFLE]";
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
                FatalResolution = fatalRes,
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

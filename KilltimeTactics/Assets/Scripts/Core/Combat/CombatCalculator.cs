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
            var cfg = Rules.CoreRulesConfig.Instance;

            // 1. Temps de l'Attaquant : Coût de base + PA bonus injectés
            int baseAttackCost = cfg.BaseAttackAPCost + (cancelPenaltyWithAP ? cfg.CancelAimPenaltyAPCost : 0);
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

            // 2. Temps du Défenseur : Coût de base de réaction + PA bonus défensifs
            int baseDefenseCost = cfg.BaseReactionAPCost;
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
                finalDefenseMod += cfg.UnreactiveDefensePenalty;
            }

            // 4. Lancer des dés et différentiel net
            var attackRoll = _diceRoller.Roll(attackDie, finalAttackMod, cfg.StandardTargetDC);
            var defenseRoll = _diceRoller.Roll(defenseDie, finalDefenseMod, cfg.StandardTargetDC);

            int differential = attackRoll.Total - defenseRoll.Total;

            string aimText = cancelPenaltyWithAP ? "Visée compensée (+1 PA)" : $"Malus Visée {targetInfo.DifficultyModifier}";
            string paAttText = safeAttackerBonus > 0 ? $" + PA Bonus {safeAttackerBonus}" : "";
            string critAttText = attackRoll.IsCriticalSuccess ? " <color=#FFE600>[CRITIQUE !]</color>" : "";
            string attackRollStr = $"{attackDie} [Tirage {attackRoll.RawRoll} + AGI {attackModifier} ({aimText}){paAttText} = Total {attackRoll.Total}]{critAttText}";

            string defPaText = canDefenderReact 
                ? (actualDefenderBonus > 0 ? $" + PA Réaction {actualDefenderBonus}" : " + Réaction 1 PA") 
                : " - Malus Réflexe 2 (0 PA)";
            string critDefText = defenseRoll.IsCriticalSuccess ? " <color=#00E5FF>[CRITIQUE !]</color>" : "";
            string defenseRollStr = $"{defenseDie} [Tirage {defenseRoll.RawRoll} + AGI {defenseModifier}{defPaText} = Total {defenseRoll.Total}]{critDefText}";

            // 1. Différentiel Négatif : parade/esquive complète
            if (differential < 0)
            {
                string blockLog = $"🛡️ <b>PARADE / ESQUIVE</b> : {attacker.Name} vise {targetInfo.DisplayName}, mais {defender.Name} neutralise l'assaut !\n";
                blockLog += $"   🎲 <b>Dés :</b> Attaque {attackRollStr} vs Défense {defenseRollStr}\n";
                blockLog += $"   ⚖️ <b>Différentiel Net :</b> <color=#00E5FF>{differential}</color> ➔ <b>0 dégât infligé</b> (Attaque bloquée).";

                return new DamageResult
                {
                    IsHit = false,
                    IsBlocked = true,
                    AttackRoll = attackRoll,
                    DefenseRoll = defenseRoll,
                    Differential = differential,
                    CombatLog = blockLog
                };
            }

            // 2. Différentiel Égal à 0 : Déviation sur zone adjacente
            bool wasDeflected = (differential == 0);
            BodyPart actualHitPart = targetedPart;

            if (wasDeflected)
            {
                actualHitPart = GetAdjacentBodyPart(targetedPart);
            }

            var actualInfo = BodyPartInfo.GetInfo(actualHitPart);

            // 3. Calcul des dégâts
            int rawDamage = weaponBaseDamage;
            string dmgFormula = $"{weaponBaseDamage} (Arme)";

            if (differential > 0)
            {
                rawDamage += differential;
                dmgFormula += $" + {differential} (Diff &Delta;)";
            }

            if (attackRoll.IsCriticalSuccess)
            {
                rawDamage *= actualInfo.CriticalDamageMultiplier;
                dmgFormula += $" x{actualInfo.CriticalDamageMultiplier} (Critique {actualInfo.DisplayName})";
            }

            // Réduction d'armure
            int totalArmor = defenderArmor + defender.BaseArmorAbsorption;
            int absorbed = Math.Min(totalArmor, rawDamage);
            int finalDamage = Math.Max(0, rawDamage - absorbed);
            int prevHp = defender.CurrentHealth;

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

            string headerTag = wasDeflected 
                ? "⚠️ <b>DÉVIATION BALISTIQUE</b>" 
                : (attackRoll.IsCriticalSuccess ? "💥 <b>COUP CRITIQUE CHIRURGICAL</b>" : "🎯 <b>TOUCHÉ CHIRURGICAL</b>");

            string hitPartStr = wasDeflected 
                ? $"Visait <b>{targetInfo.DisplayName}</b> ➔ Dévie sur <b>{actualInfo.DisplayName}</b> (Diff 0)" 
                : $"Frappe sur <b>{actualInfo.DisplayName}</b>";

            string log = $"{headerTag} : {attacker.Name} ➔ {defender.Name} ({hitPartStr})\n";
            log += $"   🎲 <b>Dés :</b> Attaque {attackRollStr} vs Défense {defenseRollStr} ➔ <b>Différentiel Net : <color=#00E5FF>{(differential > 0 ? $"+{differential}" : "0")}</color></b>\n";
            log += $"   ⚔️ <b>Dégâts :</b> [{dmgFormula} = {rawDamage} Bruts] &minus; [Armure {totalArmor} (Absorbé: {absorbed})] ➔ <b><color=#FF3B5C>{finalDamage} Dégâts Nets</color></b>\n";
            log += $"   ❤️ <b>Vitalité {defender.Name} :</b> {prevHp} ➔ <b>{defender.CurrentHealth}/{defender.MaxHealth} PV</b>";

            if (exceededEncaissement)
            {
                log += $" | ⚡ <b>CHOC TRAUMATIQUE</b> (&gt; Encaissement {defender.EncaissementThreshold}) ➔ [{inflictedStatus}]";
            }

            if (fatalRes == FatalBlowResolution.InstantDeath)
            {
                log += " | 💀 <b>MORT INSTANTANÉE (Tête détruite)</b>";
            }
            else if (fatalRes == FatalBlowResolution.MiracleSaved)
            {
                log += " | 🔮 <b>POINT DE MIRACLE DÉPENSÉ (Survie à 1 PV)</b>";
            }
            else if (fatalRes == FatalBlowResolution.ForcedUnconscious)
            {
                log += " | 💥 <b>SYNCOPE TRAUMATIQUE (K.O.)</b>";
            }
            else if (fatalRes == FatalBlowResolution.EligibleForLastBreath)
            {
                log += " | ⚡ <b>0 PV (Choix Sombrer ou Dernier Souffle)</b>";
            }

            return new DamageResult
            {
                IsHit = true,
                IsBlocked = false,
                IsCritical = attackRoll.IsCriticalSuccess,
                AttackRoll = attackRoll,
                DefenseRoll = defenseRoll,
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

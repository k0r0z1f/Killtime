using System;
using Killtime.Core.Character;
using Killtime.Core.Dice;

namespace Killtime.Core.Combat
{
    /// <summary>
    /// Résolution mathématique intégrale des passes d'armes et tirs ciblés (Livre VI).
    /// Conforme à l'Axiome Fondateur : Tout jet découle d'une compétence.
    /// L'attaque est opposée par une compétence défensive, sauf décision de non-défense.
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
        /// Résolution officielle du Livre VI axée sur les compétences d'attaque et de défense.
        /// </summary>
        public DamageResult ResolveTargetedAttack(
            CharacterStats attacker,
            CharacterStats defender,
            BodyPart targetedPart,
            SkillType attackSkill,
            SkillType defenseSkill,
            int weaponBaseDamage = 5,
            bool cancelPenaltyWithAP = false,
            bool defenderWantsToDefend = true,
            string attackerSpecialization = null,
            string defenderSpecialization = null,
            int defenderArmor = 0,
            int attackerBonusAP = 0,
            int defenderBonusAP = 0)
        {
            DiceType attackDie = attacker.GetSkillDie(attackSkill);
            int attackModifier = attacker.GetSkillModifier(attackSkill, isOffensive: true);
            if (!string.IsNullOrEmpty(attackerSpecialization) && attacker.HasSpecialization(attackerSpecialization))
            {
                attackModifier += 1;
            }

            DiceType defenseDie = defender.GetSkillDie(defenseSkill);
            int defenseModifier = defender.GetSkillModifier(defenseSkill, isOffensive: false);

            // Règle de Défense Non-Exclusive (Livre III) :
            // Parer avec une arme ou mains nues sans la spé dédiée fait chuter le dé d'un niveau.
            if (!SkillDefinitions.IsExclusivelyDefensive(defenseSkill))
            {
                bool hasDefSpec = (!string.IsNullOrEmpty(defenderSpecialization) && defender.HasSpecialization(defenderSpecialization))
                                || SkillDefinitions.HasDefensiveSpecialization(defender.Sheet, defenseSkill);

                if (!hasDefSpec)
                {
                    defenseDie = SkillDefinitions.StepDownDie(defenseDie);
                }
                else
                {
                    defenseModifier += 1;
                }
            }

            return ResolveTargetedAttackInternal(
                attacker: attacker,
                defender: defender,
                targetedPart: targetedPart,
                attackDie: attackDie,
                attackModifier: attackModifier,
                defenseDie: defenseDie,
                defenseModifier: defenseModifier,
                weaponBaseDamage: weaponBaseDamage,
                cancelPenaltyWithAP: cancelPenaltyWithAP,
                defenderWantsToDefend: defenderWantsToDefend,
                defenderArmor: defenderArmor,
                attackerBonusAP: attackerBonusAP,
                defenderBonusAP: defenderBonusAP,
                attackSkill: attackSkill,
                defenseSkill: defenseSkill
            );
        }

        /// <summary>
        /// Surcharge de compatibilité descendante avec injection directe de dés.
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
            int defenderBonusAP = 0,
            bool defenderWantsToDefend = true,
            SkillType attackSkill = SkillType.ManiementArmes,
            SkillType defenseSkill = SkillType.Esquive)
        {
            return ResolveTargetedAttackInternal(
                attacker: attacker,
                defender: defender,
                targetedPart: targetedPart,
                attackDie: attackDie,
                attackModifier: attackModifier,
                defenseDie: defenseDie,
                defenseModifier: defenseModifier,
                weaponBaseDamage: weaponBaseDamage,
                cancelPenaltyWithAP: cancelPenaltyWithAP,
                defenderWantsToDefend: defenderWantsToDefend,
                defenderArmor: defenderArmor,
                attackerBonusAP: attackerBonusAP,
                defenderBonusAP: defenderBonusAP,
                attackSkill: attackSkill,
                defenseSkill: defenseSkill
            );
        }

        private DamageResult ResolveTargetedAttackInternal(
            CharacterStats attacker,
            CharacterStats defender,
            BodyPart targetedPart,
            DiceType attackDie,
            int attackModifier,
            DiceType defenseDie,
            int defenseModifier,
            int weaponBaseDamage,
            bool cancelPenaltyWithAP,
            bool defenderWantsToDefend,
            int defenderArmor,
            int attackerBonusAP,
            int defenderBonusAP,
            SkillType attackSkill,
            SkillType defenseSkill)
        {
            var targetInfo = BodyPartInfo.GetInfo(targetedPart);
            var cfg = Rules.CoreRulesConfig.Instance;

            // 1. Dépense PA de l'Attaquant
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

            // 2. Décision & Dépense PA du Défenseur
            int baseDefenseCost = cfg.BaseReactionAPCost;
            bool isDefenderIncapacitated = !defender.CanDefendActively();
            bool effectiveDefenderWantsToDefend = defenderWantsToDefend && !isDefenderIncapacitated;
            bool canDefenderReact = effectiveDefenderWantsToDefend && (defender.CurrentActionPoints >= baseDefenseCost);
            int actualDefenderBonus = 0;

            if (effectiveDefenderWantsToDefend && canDefenderReact)
            {
                int requestedDefenseCost = baseDefenseCost + Math.Max(0, defenderBonusAP);
                int affordableDefenseCost = Math.Min(defender.CurrentActionPoints, requestedDefenseCost);
                defender.ConsumeActionPoints(affordableDefenseCost);
                actualDefenderBonus = affordableDefenseCost - baseDefenseCost;
            }

            // 3. Calcul des Modificateurs et Dés Finaux
            int finalAttackMod = attackModifier + safeAttackerBonus;
            if (!cancelPenaltyWithAP)
            {
                finalAttackMod += targetInfo.DifficultyModifier;
            }

            var attackRoll = _diceRoller.Roll(attackDie, finalAttackMod, cfg.StandardTargetDC);

            DiceRollResult defenseRoll;
            int differential;
            string defRollStr;

            if (!effectiveDefenderWantsToDefend)
            {
                defenseRoll = new DiceRollResult
                {
                    DieType = defenseDie,
                    RawRoll = 0,
                    Modifier = 0,
                    Total = 0,
                    TargetDC = cfg.StandardTargetDC,
                    Differential = -cfg.StandardTargetDC,
                    IsSuccess = false,
                    IsCriticalSuccess = false,
                    IsCriticalFailure = false
                };
                differential = attackRoll.Total;
                string inCapReason = isDefenderIncapacitated
                    ? $"<b>CIBLE HORS D'ÉTAT ({defender.ActiveStatus}) ➔ Parade impossible (0 PA)</b>"
                    : "<b>DÉFENSE PASSIVE (0 PA engagé, aucune parade)</b>";
                defRollStr = inCapReason;
            }
            else
            {
                int finalDefenseMod = defenseModifier;
                if (canDefenderReact)
                {
                    finalDefenseMod += actualDefenderBonus;
                }
                else
                {
                    finalDefenseMod += cfg.UnreactiveDefensePenalty;
                }

                defenseRoll = _diceRoller.Roll(defenseDie, finalDefenseMod, cfg.StandardTargetDC);
                differential = attackRoll.Total - defenseRoll.Total;

                string statusDefInfo = defender.GetStatusBreakdownString(defenseSkill, isOffensive: false);
                string statusDefStr = !string.IsNullOrEmpty(statusDefInfo) ? $" [Malus: {statusDefInfo}]" : "";

                string defPaText = canDefenderReact
                    ? (actualDefenderBonus > 0 ? $" + PA Réaction {actualDefenderBonus}" : " + Réaction 1 PA")
                    : $" {cfg.UnreactiveDefensePenalty} (0 PA)";
                string critDefText = defenseRoll.IsCriticalSuccess ? " <color=#00E5FF>[CRITIQUE !]</color>" : "";
                defRollStr = $"{SkillDefinitions.GetDisplayName(defenseSkill)} : {defenseDie} [Tirage {defenseRoll.RawRoll} + Mod {defenseModifier}{statusDefStr}{defPaText} = Total {defenseRoll.Total}]{critDefText}";
            }

            string statusAttInfo = attacker.GetStatusBreakdownString(attackSkill, isOffensive: true);
            string statusAttStr = !string.IsNullOrEmpty(statusAttInfo) ? $" [Malus: {statusAttInfo}]" : "";
            string aimText = cancelPenaltyWithAP ? "Visée compensée (+1 PA)" : $"Malus Visée {targetInfo.DifficultyModifier}";
            string paAttText = safeAttackerBonus > 0 ? $" + PA Bonus {safeAttackerBonus}" : "";
            string critAttText = attackRoll.IsCriticalSuccess ? " <color=#FFE600>[CRITIQUE !]</color>" : "";
            string attackRollStr = $"{SkillDefinitions.GetDisplayName(attackSkill)} : {attackDie} [Tirage {attackRoll.RawRoll} + Mod {attackModifier}{statusAttStr} ({aimText}){paAttText} = Total {attackRoll.Total}]{critAttText}";

            // 4. Résolution du Différentiel
            if (effectiveDefenderWantsToDefend && differential < 0)
            {
                string blockLog = $"🛡️ <b>PARADE / ESQUIVE</b> : {attacker.Name} attaque, mais {defender.Name} neutralise l'assaut !\n";
                blockLog += $"   🎲 <b>Compétences :</b> Attaque {attackRollStr} vs Défense {defRollStr}\n";
                blockLog += $"   ⚖️ <b>Différentiel Net :</b> <color=#00E5FF>{differential}</color> ➔ <b>0 dégât infligé</b> (Attaque neutralisée).";

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

            bool wasDeflected = effectiveDefenderWantsToDefend && (differential == 0);
            BodyPart actualHitPart = wasDeflected ? GetAdjacentBodyPart(targetedPart) : targetedPart;
            var actualInfo = BodyPartInfo.GetInfo(actualHitPart);

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

            string headerTag = !effectiveDefenderWantsToDefend
                ? (isDefenderIncapacitated ? "💥 <b>FRAPPE SUR CIBLE NEUTRALISÉE</b>" : "🎯 <b>TOUCHÉ SUR CIBLE SANS DÉFENSE</b>")
                : (wasDeflected
                    ? "⚠️ <b>DÉVIATION BALISTIQUE</b>"
                    : (attackRoll.IsCriticalSuccess ? "💥 <b>COUP CRITIQUE CHIRURGICAL</b>" : "🎯 <b>TOUCHÉ CHIRURGICAL</b>"));

            string hitPartStr = wasDeflected
                ? $"Visait <b>{targetInfo.DisplayName}</b> ➔ Dévie sur <b>{actualInfo.DisplayName}</b> (Diff 0)"
                : $"Frappe sur <b>{actualInfo.DisplayName}</b>";

            string log = $"{headerTag} : {attacker.Name} ➔ {defender.Name} ({hitPartStr})\n";
            log += $"   🎲 <b>Compétences :</b> Attaque {attackRollStr} vs Défense {defRollStr} ➔ <b>Différentiel Net : <color=#00E5FF>{(differential > 0 ? $"+{differential}" : "0")}</color></b>\n";
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
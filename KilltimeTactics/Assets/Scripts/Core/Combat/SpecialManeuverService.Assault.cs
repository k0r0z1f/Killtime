using System;
using Killtime.Core.Character;
using Killtime.Core.Dice;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Visibility;

namespace Killtime.Core.Combat
{
    /// <summary>
    /// Assaut groupé RD-084 §21.2 (sommation offensive, défense unique) — extrait sans modification depuis SpecialManeuverService.cs.
    /// </summary>
    public sealed partial class SpecialManeuverService
    {
        // =====================================================================
        // RD-084 : ATTAQUES COMBINÉES DE GROUPE (Livre V §21.2) & POURSUITE (§21.1)
        // =====================================================================

        /// <summary>
        /// RD-084 : Attaque combinée de groupe (Livre V §21.2). Les participants
        /// se synchronisent sur l'initiative du membre le plus lent, chacun
        /// effectue son épreuve (dé de sa compétence + états + mises aveugles),
        /// les résultats totaux s'additionnent face à l'UNIQUE défense de la cible.
        /// L'armure et le bouclier ne sont appliqués qu'UNE fois sur le total
        /// sommationné : le dépassement massif pulvérise les défenses.
        /// Chaque attaquant paie le coût de base (config) + ses mises ; la cible
        /// paie sa réaction de base + ses mises (ou encaisse à 0 sans réaction).
        /// </summary>
        public CombinedAttackResult ResolveCombinedAttack(
            System.Collections.Generic.IList<CharacterStats> attackers,
            CharacterStats defender,
            BodyPart targetedPart,
            SkillType attackSkill,
            SkillType defenseSkill,
            int weaponBaseDamagePerAttacker = 5,
            System.Collections.Generic.IList<int> attackerBonusAP = null,
            System.Collections.Generic.IList<int> attackerPE = null,
            int defenderBonusAP = 0,
            int defenderPE = 0,
            bool defenderWantsToDefend = true,
            bool defenderAutoStakes = false)
        {
            System.Collections.Generic.List<int> uniform = null;
            if (attackers != null)
            {
                uniform = new System.Collections.Generic.List<int>(attackers.Count);
                for (int i = 0; i < attackers.Count; i++) uniform.Add(weaponBaseDamagePerAttacker);
            }
            return ResolveCombinedAttack(attackers, defender, targetedPart, attackSkill, defenseSkill,
                uniform, attackerBonusAP, attackerPE, defenderBonusAP, defenderPE,
                defenderWantsToDefend, defenderAutoStakes);
        }

        /// <summary>
        /// RD-084 : Variante à dégâts d'arme différenciés par participant
        /// (arme équipée de chacun, 5 à mains nues). La sommation reste comparée
        /// à l'unique défense, armure appliquée une seule fois.
        /// </summary>
        public CombinedAttackResult ResolveCombinedAttack(
            System.Collections.Generic.IList<CharacterStats> attackers,
            CharacterStats defender,
            BodyPart targetedPart,
            SkillType attackSkill,
            SkillType defenseSkill,
            System.Collections.Generic.IList<int> weaponBaseDamages,
            System.Collections.Generic.IList<int> attackerBonusAP = null,
            System.Collections.Generic.IList<int> attackerPE = null,
            int defenderBonusAP = 0,
            int defenderPE = 0,
            bool defenderWantsToDefend = true,
            bool defenderAutoStakes = false)
        {
            var result = new CombinedAttackResult
            {
                IsValid = false,
                Defender = defender,
                TargetedPart = targetedPart,
                AttackSkill = attackSkill,
                DefenseSkill = defenseSkill
            };
            var cfg = Rules.CoreRulesConfig.Instance;
            // Livre V §21.2 : « Deux combattants ou plus » — plancher à 2
            // même si la config est abaissée (évite les groupes vides/dégénérés).
            int minMembers = cfg != null ? Math.Max(2, cfg.CombinedAttackMinMembers) : 2;
            int baseCost = cfg != null ? cfg.CombinedAttackAPCost : 2;

            if (attackers == null || attackers.Count < minMembers)
            {
                result.Error = $"Attaque combinée impossible : {minMembers} attaquants minimum requis.";
                result.CombatLog = $"⚠️ {result.Error}";
                return result;
            }
            if (defender == null || !defender.IsAlive)
            {
                result.Error = "Attaque combinée impossible : cible hors de combat.";
                result.CombatLog = $"⚠️ {result.Error}";
                return result;
            }

            var members = new System.Collections.Generic.List<CharacterStats>(attackers.Count);
            for (int i = 0; i < attackers.Count; i++)
            {
                var a = attackers[i];
                if (a == null || !a.IsAlive || a == defender)
                {
                    result.Error = $"Attaque combinée impossible : participant invalide ({(a == null ? "null" : a.Name)}).";
                    result.CombatLog = $"⚠️ {result.Error}";
                    return result;
                }
                members.Add(a);
            }

            if (!GroupAssaultState.TryValidateGroup(members, baseCost, minMembers, out string groupError))
            {
                result.Error = groupError;
                result.CombatLog = $"⚠️ {groupError}";
                return result;
            }

            // Synchronisation sur le membre le plus lent (traçabilité TurnManager).
            result.SlowestInitiative = GroupAssaultState.RegisterSynchronizedGroup(members);
            result.SlowestMember = GroupAssaultState.GetSlowestMember(members);
            result.Attackers.AddRange(members);

            // Coûts de base débités aussitôt (un par participant).
            for (int i = 0; i < members.Count; i++)
            {
                if (!members[i].ConsumeActionPoints(baseCost))
                {
                    result.Error = $"{members[i].Name} n'a plus assez de PA pour frapper à l'unisson !";
                    result.CombatLog = $"⚠️ {result.Error}";
                    return result;
                }
            }

            // Épreuves offensives individuelles (mises aveugles par participant).
            int summedAttack = 0;
            int summedBase = 0;
            for (int i = 0; i < members.Count; i++)
            {
                var a = members[i];
                DiceType die = a.GetSkillDie(attackSkill, true);
                int status = a.GetStatusModifier(attackSkill, isOffensive: true);
                int bAP = (attackerBonusAP != null && i < attackerBonusAP.Count) ? Math.Max(0, attackerBonusAP[i]) : 0;
                int bPE = (attackerPE != null && i < attackerPE.Count) ? Math.Max(0, attackerPE[i]) : 0;
                int paidPA = CombatDuelShared.SpendBonusPA(a, bAP);
                int paidPE = a.SpendDuelPE(bPE);
                int committed = paidPA + paidPE * cfg.DuelPEBonusPerPoint;
                // RD-202 : épreuve préparée = +2 one-shot (consommé ici même, jet immédiat).
                int prepB = PreparationState.ConsumeBonus(a) ? cfg.PreparationBonus : 0;
                var roll = _ctx.DiceRoller.Roll(die, status + committed + prepB, cfg.StandardTargetDC);
                result.AttackRolls.Add(roll);
                summedAttack += roll.Total;
                int wBase = (weaponBaseDamages != null && i < weaponBaseDamages.Count) ? weaponBaseDamages[i] : 5;
                summedBase += Math.Max(0, wBase);
            }
            result.SummedAttackTotal = summedAttack;
            result.SummedWeaponBase = summedBase;
            // RD-202 : chaque assaillant a résolu son épreuve -> fenêtre de fuite.
            for (int i = 0; i < members.Count; i++) GetAwayState.MarkPostAction(members[i]);

            // Unique défense de la cible (réaction de base + mises, ou passif à 0).
            bool wants = defenderWantsToDefend && defender.CanDefendActively();
            bool basePaid = false;
            int defCommitted = 0;
            DiceType defDie = defender.GetSkillDie(defenseSkill);
            int defStatus = defender.GetStatusModifier(defenseSkill, isOffensive: false);
            if (!SkillDefinitions.IsExclusivelyDefensive(defenseSkill)
                && !SkillDefinitions.HasDefensiveSpecialization(defender.Sheet, defenseSkill))
            {
                defDie = SkillDefinitions.StepDownDie(defDie);
            }
            if (wants)
            {
                if (defender.ConsumeActionPoints(cfg.BaseReactionAPCost))
                {
                    basePaid = true;
                    if (defenderAutoStakes)
                    {
                        int expectedDef = (int)Math.Round(CombatDuelShared.DieAverage(defDie)) + defStatus;
                        int need = Math.Max(0, summedAttack - expectedDef + 1);
                        int affordable = Math.Max(0, defender.CurrentActionPoints);
                        defCommitted = CombatDuelShared.SpendBonusPA(defender, Math.Min(need, affordable));
                    }
                    else
                    {
                        int paidPA = CombatDuelShared.SpendBonusPA(defender, Math.Max(0, defenderBonusAP));
                        int paidPE = defender.SpendDuelPE(Math.Max(0, defenderPE));
                        defCommitted = paidPA + paidPE * cfg.DuelPEBonusPerPoint;
                    }
                }
                else
                {
                    wants = false;
                }
            }

            DiceRollResult defRoll;
            if (wants && basePaid)
            {
                int penalty = 0;
                if (ChargeState.HasDefensePenalty(defender)) penalty += cfg.ChargeDefensePenalty;
                // RD-048 : punition de progression en canalisation.
                penalty += ChannelingState.GetDefenseModifier(defender);
                // RD-202 : parade préparée = +2 one-shot (consommé si défense active).
                int prepDefBonus = PreparationState.ConsumeBonus(defender) ? cfg.PreparationBonus : 0;
                defRoll = _ctx.DiceRoller.Roll(defDie, defStatus + penalty + prepDefBonus + defCommitted, cfg.StandardTargetDC);
                result.DefenderDefended = true;
                // RD-202 : la parade résolue ouvre la fenêtre de fuite au défenseur.
                GetAwayState.MarkPostAction(defender);
                result.PreparationDefenseApplied = prepDefBonus > 0;
            }
            else
            {
                defRoll = new DiceRollResult
                {
                    DieType = defDie, RawRoll = 0, Modifier = 0, Total = 0,
                    TargetDC = cfg.StandardTargetDC, Differential = -cfg.StandardTargetDC,
                    IsSuccess = false, IsCriticalSuccess = false, IsCriticalFailure = false
                };
                result.DefenderDefended = false;
            }
            result.DefenseRoll = defRoll;
            result.UniqueDefenseTotal = result.DefenderDefended ? defRoll.Total : 0;

            int differential = result.DefenderDefended
                ? summedAttack - defRoll.Total
                : summedAttack;
            result.Differential = differential;
            result.IsHit = differential >= 0;

            if (!result.IsHit)
            {
                string blockLog = $"🛡️ <b>ASSAUT GROUPÉ PARÉ</b> : {members.Count} assaillants synchronisés (lenteur : {(result.SlowestMember != null ? result.SlowestMember.Name : "?")}, ini {result.SlowestInitiative}) mais {defender.Name} neutralise la sommation !\n";
                blockLog += $"   ⚔️ Sommation : {summedAttack} vs Défense unique {defRoll.Total} ➔ <b>Différentiel {differential}</b> — <b>0 dégât</b>.";
                if (result.PreparationDefenseApplied) blockLog += $"\n   🎯 <b>PRÉPARATION</b> : parade préparée de {defender.Name} (+{cfg.PreparationBonus}, consommée).";
                blockLog += _progression.ApplySkillProgression(defender, defenseSkill, false);
                for (int i = 0; i < members.Count; i++) StealthState.BreakStealth(members[i]);
                result.CombatLog = blockLog;
                result.FatalResolution = FatalBlowResolution.None;
                result.IsValid = true;
                return result;
            }

            // Dépassement massif : armure + bouclier appliqués UNE SEULE FOIS.
            int rawDamage = summedBase + Math.Max(0, differential);
            int totalArmor = defender.BaseArmorAbsorption + defender.GetWornArmorBonus();
            int absorbed = Math.Min(totalArmor, rawDamage);
            int shieldRemainder = defender.AbsorbShield(Math.Max(0, rawDamage - absorbed));
            int shieldAbsorbed = Math.Max(0, Math.Max(0, rawDamage - absorbed) - shieldRemainder);
            int finalDamage = Math.Max(0, shieldRemainder);
            result.RawDamage = rawDamage;
            result.ArmorAbsorbed = absorbed;
            result.ShieldAbsorbed = shieldAbsorbed;
            result.FinalDamageApplied = finalDamage;

            bool exceeded = finalDamage > defender.EncaissementThreshold;
            result.ExceededEncaissement = exceeded;
            StatusEffect inflicted = StatusEffect.None;
            // RD-048 : capture avant statuts (purge silencieuse par K.O. sinon).
            bool combinedWasChanneling = ChannelingState.IsChanneling(defender);
            string combinedPriorLabel = null;
            if (combinedWasChanneling) ChannelingState.TryGetLabel(defender, out combinedPriorLabel);
            if (exceeded)
            {
                inflicted = _progression.DetermineInflictedStatus(targetedPart);
                defender.ActiveStatus |= inflicted;
            }
            result.InflictedStatus = inflicted;

            FatalBlowResolution fatalRes = FatalBlowResolution.None;
            if (defender.CurrentHealth - finalDamage <= 0)
            {
                fatalRes = defender.EvaluateFatalBlow(targetedPart, finalDamage);
            }
            else
            {
                defender.CurrentHealth -= finalDamage;
            }
            result.FatalResolution = fatalRes;

            MoraleCheckResult moraleRes = default;
            if (defender.IsAlive && !defender.HasTestedMorale && defender.CurrentHealth > 0)
            {
                moraleRes = defender.CheckMoraleAtThreshold(_ctx.DiceRoller, cfg.StandardTargetDC);
            }
            result.MoraleResult = moraleRes;

            // RD-048 : la sommation interrompt aussi la canalisation (dégât net > encaissement).
            bool combinedChannelInterrupted = false;
            string combinedChannelLabel = null;
            if (combinedWasChanneling)
            {
                if (ChannelingState.IsChanneling(defender))
                {
                    if (!defender.IsAlive || exceeded)
                    {
                        if (ChannelingState.NotifyDamage(defender, finalDamage, out combinedChannelLabel))
                            combinedChannelInterrupted = true;
                    }
                }
                else
                {
                    combinedChannelInterrupted = true;
                    combinedChannelLabel = combinedPriorLabel;
                    ChannelingState.Cancel(defender);
                }
            }
            result.ChannelInterrupted = combinedChannelInterrupted;
            result.InterruptedChannelLabel = combinedChannelLabel;

            var info = BodyPartInfo.GetInfo(targetedPart);
            string log = $"⚔️ <b>ASSAUT GROUPÉ ({members.Count})</b> : synchronisés sur {(result.SlowestMember != null ? result.SlowestMember.Name : "?")} (initiative la plus lente : {result.SlowestInitiative}) ➔ {defender.Name} ({info.DisplayName})\n";
            for (int i = 0; i < members.Count; i++)
            {
                var r = result.AttackRolls[i];
                log += $"   🗡️ {members[i].Name} : {attackSkill} [{r.DieType} tirage {r.RawRoll} + mod {r.Modifier} = <b>{r.Total}</b>]\n";
            }
            log += $"   ➕ <b>SOMMATION OFFENSIVE : {summedAttack}</b> vs <b>DÉFENSE UNIQUE : {(result.DefenderDefended ? defRoll.Total.ToString() : "0 (passif)")}</b> ➔ <b>Différentiel +{differential}</b>\n";
            if (result.PreparationDefenseApplied) log += $"   🎯 <b>PRÉPARATION</b> : parade préparée de {defender.Name} (+{cfg.PreparationBonus}, consommée)\n";
            log += $"   ⚔️ Dégâts : [{summedBase} (armes cumulées) + {Math.Max(0, differential)} (dépassement) = {rawDamage} bruts] − [Armure {totalArmor} (1×, absorbé {absorbed})]";
            if (shieldAbsorbed > 0) log += $" − [🔮Bouclier {shieldAbsorbed}]";
            log += $" ➔ <b><color=#FF3B5C>{finalDamage} Dégâts Nets</color></b>\n";
            log += $"   ❤️ Vitalité {defender.Name} : ➔ <b>{defender.CurrentHealth}/{defender.MaxHealth} PV</b>";
            if (exceeded) log += $" | ⚡ <b>CHOC TRAUMATIQUE</b> (> Encaissement {defender.EncaissementThreshold}) ➔ [{inflicted}]";
            if (combinedChannelInterrupted) log += $" | ⏳ <b>CANALISATION INTERROMPUE</b> ({finalDamage} dégâts nets &gt; encaissement {defender.EncaissementThreshold}) : « {combinedChannelLabel} » perdue !";
            if (fatalRes == FatalBlowResolution.InstantDeath) log += " | 💀 <b>MORT INSTANTANÉE</b>";
            else if (fatalRes == FatalBlowResolution.ForcedUnconscious) log += " | 💥 <b>SYNCOPE TRAUMATIQUE (K.O.)</b>";
            else if (fatalRes == FatalBlowResolution.EligibleForLastBreath) log += " | ⚡ <b>0 PV (Sombrer ou Dernier Souffle)</b>";
            if (moraleRes.Triggered) log += "\n   " + moraleRes.Log;
            for (int i = 0; i < members.Count; i++)
            {
                log += _progression.ApplySkillProgression(members[i], attackSkill, true);
                StealthState.BreakStealth(members[i]);
            }
            result.CombatLog = log;
            result.IsValid = true;
            return result;
        }
    }
}

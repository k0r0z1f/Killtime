using System;
using Killtime.Core.Character;
using Killtime.Core.Dice;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Visibility;

namespace Killtime.Core.Combat
{
    /// <summary>
    /// Étape 4 du duel aveugle : calcul des dégâts, armures, boucliers, statuts,
    /// critiques §8.2, effets RD-082 sur marge élevée, moral RD-047.
    /// Dépend du contexte (dés) et de la progression (tables, statuts).
    /// Corps copié à l'identique depuis l'ancien CombatCalculator.
    /// </summary>
    public sealed class DamageEngineService
    {
        private readonly CombatDuelContext _ctx;
        private readonly CombatProgressionService _progression;

        public DamageEngineService(CombatDuelContext ctx, CombatProgressionService progression)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            _progression = progression ?? throw new ArgumentNullException(nameof(progression));
        }

        internal DamageResult ResolveDuelDamage(AttackDuel duel)
        {
            var attacker = duel.Attacker;
            var defender = duel.Defender;
            var targetedPart = duel.TargetedPart;
            var attackSkill = duel.AttackSkill;
            var defenseSkill = duel.DefenseSkill;
            var targetInfo = BodyPartInfo.GetInfo(targetedPart);
            var cfg = Rules.CoreRulesConfig.Instance;
            // RD-038 Charge 3+ cases : consommation de l'élan cinétique sur cette frappe de contact
            bool isMeleeAttackForCharge = SkillDefinitions.IsMeleeAttackSkill(duel.AttackSkill) || duel.AttackSkill == SkillType.MainsNues;
            bool chargeApplied = isMeleeAttackForCharge && ChargeState.ConsumeChargeBonus(attacker);
            // RD-048 Canalisation : consommée à l'attaque (bonus déjà inclus dans
            // AttackBaseMod au BeginDuel). Consommée même si parée (effort dépensé).
            bool channelApplied = duel.IsChannelAttack && ChannelingState.ConsumeChannelBonus(attacker);

            var attackRoll = duel.AttackFinalRoll;
            int attCommittedPA = duel.AttackerBonusPAApplied;
            int attCommittedPE = duel.AttackerPEApplied;
            int defCommittedPA = duel.DefenderBonusPAApplied;
            int defCommittedPE = duel.DefenderPEApplied;

            DiceRollResult defenseRoll;
            int differential;
            string defRollStr;

            bool effectiveDefenderWantsToDefend = duel.DefenderWantsToDefend && !duel.IsDefenderIncapacitated;

            if (!effectiveDefenderWantsToDefend)
            {
                defenseRoll = duel.DefenseFinalRoll;
                differential = attackRoll.Total;
                string inCapReason = duel.IsStealthAttack
                    ? "<b>EMBUSCADE DEPUIS LE STEALTH (Cible surprise : 0 PA, aucune réaction adverse)</b>"
                    : (duel.IsDefenderIncapacitated
                        ? $"<b>CIBLE HORS D'ÉTAT ({defender.ActiveStatus}) ➔ Parade impossible (0 PA)</b>"
                        : "<b>ENCAISSEMENT PASSIF AVEUGLE (0 PA engagé, aucune parade, jet adverse jamais vu)</b>");
                defRollStr = inCapReason;
            }
            else
            {
                defenseRoll = duel.DefenseFinalRoll;
                differential = attackRoll.Total - defenseRoll.Total;

                string statusDefInfo = defender.GetStatusBreakdownString(defenseSkill, isOffensive: false);

                string defPaText;
                if (duel.CanDefenderReact && duel.DefenderBasePaid)
                {
                    defPaText = $" + Mise {defCommittedPA} PA + {defCommittedPE} PE (aveugle)";
                }
                else
                {
                    defPaText = $" {cfg.UnreactiveDefensePenalty} (0 PA)";
                }
                string critDefText = defenseRoll.IsCriticalSuccess ? " <color=#00E5FF>[CRITIQUE !]</color>" : "";
                string defStatusDetail = !string.IsNullOrEmpty(statusDefInfo) ? $" {statusDefInfo}" : " 0";
                if (ChargeState.HasDefensePenalty(defender))
                {
                    defStatusDetail += $" [Malus Charge : {cfg.ChargeDefensePenalty} Déf]";
                }
                if (ChannelingState.IsChanneling(defender))
                {
                    defStatusDetail += $" [Canalisation : {ChannelingState.GetDefenseModifier(defender)} Déf]";
                }
                // Les deux jets sont révélés ensemble : aucune injection après tirage.
                defRollStr = $"{SkillDefinitions.GetDisplayName(defenseSkill)} : {duel.DefenseDie} [Tirage {defenseRoll.RawRoll} + États {duel.DefenseStatusMod} ({defStatusDetail}){defPaText} = Total {defenseRoll.Total}]{critDefText}";
            }

            string statusAttInfo = attacker.GetStatusBreakdownString(attackSkill, isOffensive: true);
            string statusAttDetail = !string.IsNullOrEmpty(statusAttInfo) ? statusAttInfo : "0";
            string aimText = duel.CancelPenaltyWithAP ? "Visée compensée (+1 PA base)" : $"Malus Visée {targetInfo.DifficultyModifier}";
            string paAttText = (attCommittedPA > 0 || attCommittedPE > 0) ? $" + Mise {attCommittedPA} PA + {attCommittedPE} PE (aveugle)" : " + Mise 0 (aveugle)";
            string critAttText = attackRoll.IsCriticalSuccess ? " <color=#FFE600>[CRITIQUE !]</color>" : "";
            string entraveText = duel.AppliedCanonEntrave
                ? (duel.CancelPenaltyWithAP ? " [Canon Entravé : Annulé (+1 PA)]" : " [Canon Entravé : -2 Tir]")
                : (duel.IsMartialArtsStrike ? " [Arts Martiaux : Exemption Totale]" : "");
            string coverText = duel.Cover == CoverType.Half
                ? $" [Couvert : moitié visible {duel.CoverAttackPenalty}]"
                : duel.Cover == CoverType.ThreeQuarters
                    ? $" [Couvert : 3/4 couvert {duel.CoverAttackPenalty}]"
                    : "";
            string ecText = duel.WeaponBonusEc > 0 ? $" [+{duel.WeaponBonusEc}ec arme]" : "";
            string elevationText = duel.ElevationAttackMod != 0 ? $" [Hauteur {duel.ElevationAttackMod:+0;-0} {duel.ElevationLabel}]" : "";
            string stealthText = duel.IsStealthAttack ? $" [🥷 Embuscade/Stealth : +{duel.StealthAttackBonus}]" : "";
            string channelText = duel.IsChannelAttack ? $" [⏳ Canalisation : +{duel.ChannelAttackBonus}]" : "";
            string nightText = duel.NightAttackMod != 0 ? $" [Nuit : {duel.NightAttackMod} Tir (sans lampe/thermique)]" : "";

            int normalRef = (attacker.Attributes.Force + attacker.Attributes.Agilite + 1) / 2;
            string augText = (attacker.IsMagicAugmented(attackSkill, true)
                    && attacker.Attributes.Magie > normalRef)
                ? $" [Corps Augmenté : MAG {attacker.Attributes.Magie} (+{SkillDefinitions.CharacteristicSteps(attacker.Attributes.Magie)} paliers)]"
                : "";
            string attackRollStr = $"{SkillDefinitions.GetDisplayName(attackSkill)} : {duel.AttackDie} [Tirage {attackRoll.RawRoll} + États {duel.AttackStatusMod} ({statusAttDetail}) + ({aimText}){paAttText}{entraveText}{coverText}{ecText}{elevationText}{stealthText}{channelText}{nightText} = Total {attackRoll.Total}]{augText}{critAttText}";

            // Traçabilité Livre V §20.1 (duel aveugle) : déclarations masquées puis révélation.
            // RD-048 : la canalisation consommée à l'attaque est tracée, la punition
            // de progression du défenseur aussi.
            string channelDefTag = ChannelingState.IsChanneling(defender) ? " [Canalisation -1]" : "";
            string channelAttTag = channelApplied ? $" [⏳ Canalisation « +{duel.ChannelAttackBonus} » consommée]" : (duel.IsChannelAttack ? " [⏳ Canalisation déjà rompue]" : "");
            string phaseAtt = $"Phase 1 — Déclaration attaquant : {attacker.Name} désigne {defender.Name} ({targetInfo.DisplayName}), annonce {SkillDefinitions.GetDisplayName(attackSkill)}, engage {duel.BaseAttackCost} PA + mise cachée {attCommittedPA} PA + {attCommittedPE} PE. Résultat caché.{channelAttTag}";
            string phaseDef = duel.IsStealthAttack
                ? $"Phase 2 — Déclaration défenseur (surpris) : {defender.Name} subit l'embuscade depuis le stealth (aucune réaction adverse possible)."
                : (!effectiveDefenderWantsToDefend
                    ? $"Phase 2 — Déclaration défenseur (aveugle) : {defender.Name} encaisse (0 PA, 0 PE), sans voir le jet adverse."
                    : ((duel.CanDefenderReact && duel.DefenderBasePaid)
                        ? $"Phase 2 — Déclaration défenseur (aveugle) : {defender.Name} annonce {SkillDefinitions.GetDisplayName(defenseSkill)}, engage 1 PA + mise cachée {defCommittedPA} PA + {defCommittedPE} PE, sans voir le jet adverse{(ChargeState.HasDefensePenalty(defender) ? " [Malus Charge -1]" : "")}{channelDefTag}."
                        : $"Phase 2 — Déclaration défenseur (aveugle) : {defender.Name} sans réaction (0 PA) → {cfg.UnreactiveDefensePenalty} réflexe{(ChargeState.HasDefensePenalty(defender) ? " [Malus Charge -1]" : "")}{channelDefTag}."));

            // 4. Résolution du Différentiel
            if (effectiveDefenderWantsToDefend && differential < 0)
            {
                string blockLog = $"🛡️ <b>PARADE / ESQUIVE</b> : {attacker.Name} attaque, mais {defender.Name} neutralise l'assaut !\n";
                blockLog += $"   {phaseAtt}\n";
                blockLog += $"   {phaseDef}\n";
                blockLog += $"   🎲 <b>RÉVÉLATION SIMULTANÉE :</b> Attaque {attackRollStr} vs Défense {defRollStr}\n";
                blockLog += $"   ⚖️ <b>Différentiel Net :</b> <color=#00E5FF>{differential}</color> ➔ <b>0 dégât infligé</b> (Attaque neutralisée).";

                bool defHasCrit = defenseRoll.IsCriticalSuccess;
                CriticalConsequence defCrit = default;
                if (defHasCrit)
                {
                    defCrit = _ctx.DiceRoller.RollCriticalConsequence(duel.DefenseDie, CriticalEffectCategory.Defensive);
                    blockLog += $"\n   ✨ <b>[CONSÉQUENCE CRITIQUE DÉFENSIVE §8.2]</b> Relance {duel.DefenseDie} ➔ <b>{defCrit.RawRoll}</b> (Palier {defCrit.Threshold} : <i>{defCrit.Title}</i>) — {defCrit.Description}";
                    ApplyDefensiveCriticalEffect(defCrit, defender, attacker);
                }

                // Livre I §5 : le défenseur a RÉUSSI sa compétence → +1 case de progression.
                blockLog += _progression.ApplySkillProgression(defender, defenseSkill, false);

                // RD-049 : toute attaque rompt la furtivité
                StealthState.BreakStealth(attacker);

                return new DamageResult
                {
                    IsHit = false,
                    IsBlocked = true,
                    IsCritical = defHasCrit,
                    HasCriticalConsequence = defHasCrit,
                    CriticalConsequence = defCrit,
                    AttackRoll = attackRoll,
                    DefenseRoll = defenseRoll,
                    Differential = differential,
                    Cover = duel.Cover,
                    CoverAttackPenalty = duel.CoverAttackPenalty,
                    BlockedByCover = false,
                    CombatLog = blockLog
                };
            }

            bool wasDeflected = effectiveDefenderWantsToDefend && (differential == 0);
            int deflectionRoll = 0;
            string deflectionMedical = null;
            BodyPart actualHitPart = targetedPart;
            if (wasDeflected)
            {
                actualHitPart = _progression.ResolveAnatomicalDeflection(targetedPart, out deflectionRoll, out deflectionMedical);
            }
            var actualInfo = BodyPartInfo.GetInfo(actualHitPart);

            int rawDamage = duel.WeaponBaseDamage;
            string dmgFormula = $"{duel.WeaponBaseDamage} (Arme)";

            if (differential > 0)
            {
                rawDamage += differential;
                dmgFormula += $" + {differential} (Diff &Delta;)";
            }

            CriticalConsequence attCrit = default;
            bool attHasCrit = attackRoll.IsCriticalSuccess;
            if (attHasCrit)
            {
                rawDamage *= actualInfo.CriticalDamageMultiplier;
                dmgFormula += $" x{actualInfo.CriticalDamageMultiplier} (Critique {actualInfo.DisplayName})";
                attCrit = _ctx.DiceRoller.RollCriticalConsequence(duel.AttackDie, CriticalEffectCategory.Offensive);
                if (attCrit.Threshold == 3 || attCrit.Threshold == 4)
                {
                    rawDamage += 1;
                    dmgFormula += $" + 1 ({attCrit.Title} §8.2)";
                }
                else if (attCrit.Threshold == 5)
                {
                    rawDamage += 2;
                    dmgFormula += $" + 2 ({attCrit.Title} §8.2)";
                }
            }

            int totalArmor = duel.DefenderArmor + defender.BaseArmorAbsorption + defender.GetWornArmorBonus();
            if (attacker.HasSpecialization("Leviers Densifiés : Pointe Cristalline") && duel.AttackSkill == SkillType.MainsNues)
            {
                totalArmor = Math.Max(0, totalArmor - 3);
            }
            else if (attacker.HasSpecialization("Leviers Densifiés : Perforation Osseuse") && duel.AttackSkill == SkillType.MainsNues)
            {
                totalArmor = Math.Max(0, totalArmor - 2);
            }
            else if (attacker.HasSpecialization("Leviers Densifiés") && duel.AttackSkill == SkillType.MainsNues)
            {
                totalArmor = Math.Max(0, totalArmor - 1);
            }

            if (attacker.HasSpecialization("Perforation de Silicate : Cœur d'Adamas") && duel.AttackSkill == SkillType.MainsNues)
            {
                totalArmor = Math.Max(0, totalArmor - 4);
            }
            else if (attacker.HasSpecialization("Perforation de Silicate : Fracture Sismique") && duel.AttackSkill == SkillType.MainsNues)
            {
                totalArmor = Math.Max(0, totalArmor - 3);
            }
            else if (attacker.HasSpecialization("Perforation de Silicate") && duel.AttackSkill == SkillType.MainsNues)
            {
                totalArmor = Math.Max(0, totalArmor - 2);
            }

            // Arts Martiaux : Brise-Blindage (Livre III) — impact focalisé
            // destructeur d'alliages : les attaques à mains nues ignorent 2 armure.
            if (attacker.HasSpecialization("Arts Martiaux : Brise-Blindage") && duel.AttackSkill == SkillType.MainsNues)
            {
                totalArmor = Math.Max(0, totalArmor - 2);
            }

            if (attacker.HasSpecialization("Fente de Rupture : Transpercement Traversant") && duel.AttackSkill == SkillType.ManiementArmes)
            {
                totalArmor = Math.Max(0, totalArmor - 4);
            }
            else if (attacker.HasSpecialization("Fente de Rupture : Pointe Chirurgicale") && duel.AttackSkill == SkillType.ManiementArmes)
            {
                totalArmor = Math.Max(0, totalArmor - 3);
            }
            else if (attacker.HasSpecialization("Fente de Rupture") && duel.AttackSkill == SkillType.ManiementArmes)
            {
                totalArmor = Math.Max(0, totalArmor - 2);
            }

            // Arme de Hast : perforation à l'estoc (Armes Perçantes).
            if (attacker.HasSpecialization("Arme de Hast : Phalange d'Acier") && duel.AttackSkill == SkillType.ArmesPercantes)
            {
                totalArmor = Math.Max(0, totalArmor - 4);
            }
            else if (attacker.HasSpecialization("Arme de Hast : Mur de Piques") && duel.AttackSkill == SkillType.ArmesPercantes)
            {
                totalArmor = Math.Max(0, totalArmor - 3);
            }
            else if (attacker.HasSpecialization("Arme de Hast : Arrêt de Charge") && duel.AttackSkill == SkillType.ArmesPercantes)
            {
                totalArmor = Math.Max(0, totalArmor - 2);
            }

            // Fiche héroïque : Avatar (voir MinaCharacter.GetAvatarDamageBonus).
            rawDamage += MinaCharacter.GetAvatarDamageBonus(attacker, duel.AttackSkill);

            // Marteau de Guerre et Hache de Guerre : branches du Maniement d'Arme.
            // La valeur legacy ArmesContondantes est rabattue sur ManiementArmes.
            SkillType meleeSkill = SkillDefinitions.ResolveBaseSkill(duel.AttackSkill);

            if (attacker.HasSpecialization("Corps Augmenté : Résonance Cinétique Pure") && duel.AttackSkill == SkillType.MainsNues)
            {
                rawDamage += 2;
                dmgFormula += " + 2 (Résonance Cinétique)";
            }

            if (attacker.HasSpecialization("Impact de Bedrock : Enclume Mortelle") && meleeSkill == SkillType.ManiementArmes)
            {
                totalArmor = 0;
                rawDamage += 6;
                dmgFormula += " + 6 (Enclume Mortelle, Armure Ignorée)";
            }

            if (attacker.HasSpecialization("Marteau de Guerre : Cataclysme de Fer") && meleeSkill == SkillType.ManiementArmes)
            {
                rawDamage += 5;
            }
            else if (attacker.HasSpecialization("Marteau de Guerre : Brise-Crâne") && meleeSkill == SkillType.ManiementArmes)
            {
                rawDamage += 4;
            }
            else if (attacker.HasSpecialization("Marteau de Guerre : Écrasement Osseux") && meleeSkill == SkillType.ManiementArmes)
            {
                rawDamage += 3;
            }
            else if (attacker.HasSpecialization("Marteau de Guerre") && meleeSkill == SkillType.ManiementArmes)
            {
                rawDamage += 2;
            }

            if (attacker.HasSpecialization("Hache de Guerre : Exécution du Bourreau") && meleeSkill == SkillType.ManiementArmes)
            {
                rawDamage += 5;
            }
            else if (attacker.HasSpecialization("Hache de Guerre : Brise-Garde") && meleeSkill == SkillType.ManiementArmes)
            {
                rawDamage += 4;
            }
            else if (attacker.HasSpecialization("Hache de Guerre : Fente du Bûcheron") && meleeSkill == SkillType.ManiementArmes)
            {
                rawDamage += 3;
            }
            else if (attacker.HasSpecialization("Hache de Guerre") && meleeSkill == SkillType.ManiementArmes)
            {
                rawDamage += 2;
            }

            // RD-038 Charge 3+ cases : +2 dégâts sur la frappe de contact (mêlée / mains nues)
            if (chargeApplied)
            {
                int chargeBonus = cfg.ChargeDamageBonus;
                rawDamage += chargeBonus;
                dmgFormula += $" + {chargeBonus} (⚡ Charge)";
            }

            bool isThomasSymbioticImmune = ThomasCharacter.IsImmuneToCasterMagic(defender.Sheet, attacker.Sheet);
            if (isThomasSymbioticImmune)
            {
                rawDamage = 0;
                totalArmor = 0;
            }

            int absorbed = Math.Min(totalArmor, rawDamage);
            int finalDamage = isThomasSymbioticImmune ? 0 : Math.Max(0, rawDamage - absorbed);
            // Champs de force portés (Livre VII §28.2) : la barrière absorbe avant la
            // chair — aucun choc traumatique tant qu'elle encaisse tout.
            int shieldRemainder = defender.AbsorbShield(finalDamage);
            int shieldAbsorbed = Math.Max(0, finalDamage - shieldRemainder);
            finalDamage = Math.Max(0, shieldRemainder);

            // RD-087 (Livre XI §45-46) : réactions des Boss Titans aux dégâts
            // élémentaires (Mémoire des Flammes, Absorption Thermique, Choc
            // Thermique) — avant application létale, tracé log. Inerte sans
            // spé boss ni dégât typé (simple expiration de fissure).
            string titanBossTag = "";
            {
                var equippedWepForBoss = attacker.Sheet?.GetEquippedWeapon();
                var bossElement = TitanBossMechanics.ClassifyDuelElement(duel.AttackSkill, equippedWepForBoss, attacker);
                var bossReact = TitanBossMechanics.ResolveIncomingElementalDamage(defender, bossElement, finalDamage, TitanBossState.CurrentRound);
                if (bossReact.HasReaction)
                {
                    finalDamage = Math.Max(0, bossReact.AdjustedDamage);
                    if (!string.IsNullOrEmpty(bossReact.LogFragment)) titanBossTag = " | " + bossReact.LogFragment;
                }
            }
            int prevHp = defender.CurrentHealth;

            bool exceededEncaissement = finalDamage > defender.EncaissementThreshold;
            StatusEffect inflictedStatus = StatusEffect.None;
            bool failleExploitee = false;

            // RD-048 : capture de la canalisation AVANT dégâts/statuts (l'IsChanneling
            // d'après-coup purgerait silencieusement une canalisation rompue par K.O.).
            bool defenderWasChanneling = ChannelingState.IsChanneling(defender);
            string defenderChannelLabel = null;
            if (defenderWasChanneling) ChannelingState.TryGetLabel(defender, out defenderChannelLabel);

            // Arts Martiaux : Rupture Ligamentaire (Livre III) — la cible doit être
            // Déstabilisée AVANT ce coup (le statut infligé par ce même coup ne compte pas).
            bool wasDestabilisedBefore = (defender.ActiveStatus & StatusEffect.Destabilise) != 0;

            if (!isThomasSymbioticImmune && (exceededEncaissement || attackRoll.IsCriticalSuccess))
            {
                inflictedStatus = _progression.DetermineInflictedStatus(actualHitPart);
                defender.ActiveStatus |= inflictedStatus;
            }

            bool critCausedKnockback = false;
            FatalBlowResolution critFatalOverride = FatalBlowResolution.None;
            if (!isThomasSymbioticImmune && attHasCrit)
            {
                ApplyOffensiveCriticalEffect(attCrit, attacker, defender, ref inflictedStatus, ref critCausedKnockback, ref critFatalOverride);
            }

            // Analyse de Faille (Livre III) : la faille exposée est consommée à la
            // touche — la prochaine attaque ignore le seuil d'encaissement adverse.
            if (!isThomasSymbioticImmune && !exceededEncaissement && SkillTechniqueState.ConsumeExposedFlaw(defender))
            {
                exceededEncaissement = true;
                inflictedStatus |= _progression.DetermineInflictedStatus(actualHitPart);
                defender.ActiveStatus |= inflictedStatus;
                failleExploitee = true;
            }

            // Arts Martiaux : Balayage Rotatif (Livre III) — sur différentiel net
            // Delta >= 2 à mains nues, la cible chute À Terre (même sans choc).
            if (!isThomasSymbioticImmune && duel.AttackSkill == SkillType.MainsNues && differential >= 2
                && attacker.HasSpecialization("Arts Martiaux : Balayage Rotatif"))
            {
                inflictedStatus |= StatusEffect.ATerre;
                defender.ActiveStatus |= StatusEffect.ATerre;
            }

            // Thomas-0 : Impact de Bedrock — fracture d'armure de -2 et Sonné sur Delta >= 2
            if (differential >= 2 && attacker.HasSpecialization("Impact de Bedrock") && meleeSkill == SkillType.ManiementArmes)
            {
                defender.BaseArmorAbsorption = Math.Max(0, defender.BaseArmorAbsorption - 2);
                inflictedStatus |= StatusEffect.Sonne;
                defender.ApplyStatus(StatusEffect.Sonne, 1);
            }

            // Mina-0 : Floraison Cinétique — ronces réflexes sur engagement d'essoufflement au contact
            if (defender.HasSpecialization("Éveil Chlorophyllien : Floraison Cinétique") && defCommittedPE > 0 && _ctx.IsAtContactDistance)
            {
                int thorns = defCommittedPE * 3;
                attacker.CurrentHealth = Math.Max(0, attacker.CurrentHealth - thorns);
            }

            // Arts Martiaux : Rupture Ligamentaire (Livre III) — cible Déstabilisée
            // avant le coup → torsion destructrice : Paralysé pendant 1 tour.
            if (!isThomasSymbioticImmune && duel.AttackSkill == SkillType.MainsNues && wasDestabilisedBefore
                && attacker.HasSpecialization("Arts Martiaux : Rupture Ligamentaire"))
            {
                inflictedStatus |= StatusEffect.Paralyse;
                defender.ApplyStatus(StatusEffect.Paralyse, 1);
            }

            FatalBlowResolution fatalRes = FatalBlowResolution.None;
            bool wasDisintegrated = false;

            if (defender.CurrentHealth - finalDamage <= 0)
            {
                fatalRes = defender.EvaluateFatalBlow(actualHitPart, finalDamage);
            }
            else
            {
                defender.CurrentHealth -= finalDamage;
            }

            // Palier 24 (Cible Inconsciente) : K.O. immédiat si le coup n'est pas déjà mortel ou miraculé
            if (critFatalOverride == FatalBlowResolution.ForcedUnconscious && fatalRes == FatalBlowResolution.None)
            {
                fatalRes = FatalBlowResolution.ForcedUnconscious;
                defender.CurrentHealth = 0;
            }

            // =====================================================================
            // RD-082 (Livre VI §24.4) : DÉCLENCHEMENT DES EFFETS SUR MARGE ÉLEVÉE
            // =====================================================================
            bool bleedApplied = false;
            int bleedDoT = 0;
            bool fireApplied = false;
            int fireTurns = 0;
            bool stunApplied = false;

            if (!isThomasSymbioticImmune && differential > 0)
            {
                // 4) Désintégrer sur Diff >= 20 et Spé Destruction 6 (pulvérisation totale prioritaire)
                if (differential >= 20 && CombatProgressionService.HasDestructionTier6(attacker))
                {
                    defender.CurrentHealth = 0;
                    defender.IsDead = true;
                    defender.ActiveStatus |= StatusEffect.Inconscient;
                    fatalRes = FatalBlowResolution.InstantDeath;
                    wasDisintegrated = true;
                }
                else if (!defender.IsDead)
                {
                    var equippedWep = attacker.Sheet?.GetEquippedWeapon();

                    // 1) Saigner sur Diff >= 5 avec arme tranchante / perçante (DoT résiduel = rang d'entraînement)
                    if (differential >= 5 && CombatProgressionService.IsSlashingOrPiercingAttack(duel.AttackSkill, equippedWep, attacker))
                    {
                        int trainingRank = attacker.Sheet != null ? attacker.Sheet.GetSkill(duel.AttackSkill).TrainingLevel : 0;
                        bleedDoT = Math.Max(1, trainingRank);
                        defender.ApplyResidualDamage(StatusEffect.Saignement, bleedDoT);
                        inflictedStatus |= StatusEffect.Saignement;
                        bleedApplied = true;
                    }

                    // 2) Brûler sur Diff >= 5 avec feu / plasma (EnFeu 1-3 tours)
                    if (differential >= 5 && CombatProgressionService.IsFireOrPlasmaAttack(duel.AttackSkill, equippedWep, attacker))
                    {
                        fireTurns = Math.Clamp(1 + (differential - 5) / 3, 1, 3);
                        defender.ApplyStatus(StatusEffect.EnFeu, fireTurns, residualDamage: 2 * fireTurns);
                        inflictedStatus |= StatusEffect.EnFeu;
                        fireApplied = true;
                    }

                    // 3) Assommer sur Diff >= 10 et visée crânienne (Inconscient 1 tour direct)
                    bool isCranialTarget = (targetedPart == BodyPart.Tete || targetedPart == BodyPart.YeuxVisage
                        || actualHitPart == BodyPart.Tete || actualHitPart == BodyPart.YeuxVisage);
                    if (differential >= 10 && isCranialTarget)
                    {
                        defender.ApplyStatus(StatusEffect.Inconscient, 1);
                        inflictedStatus |= StatusEffect.Inconscient;
                        if (fatalRes == FatalBlowResolution.None)
                        {
                            fatalRes = FatalBlowResolution.ForcedUnconscious;
                        }
                        stunApplied = true;
                    }
                }
            }

            // RD-047 : Test de moral au seuil critique de 28% PV
            MoraleCheckResult moraleRes = default;
            if (!wasDisintegrated && defender.IsAlive && !defender.HasTestedMorale && defender.CurrentHealth > 0)
            {
                moraleRes = defender.CheckMoraleAtThreshold(_ctx.DiceRoller, cfg.StandardTargetDC);
            }

            // RD-048 : interruption de canalisation si dégât net > encaissement
            // (ou mort/neutralisation : la progression est perdue dans tous les cas).
            // Le libellé a été capturé avant dégâts (purge silencieuse sinon).
            bool channelInterrupted = false;
            string interruptedChannelLabel = null;
            if (!isThomasSymbioticImmune && defenderWasChanneling)
            {
                if (ChannelingState.IsChanneling(defender))
                {
                    if (!defender.IsAlive || exceededEncaissement)
                    {
                        if (ChannelingState.NotifyDamage(defender, finalDamage, out interruptedChannelLabel))
                            channelInterrupted = true;
                    }
                }
                else
                {
                    // Rompue par le coup lui-même (K.O., étourdi infligé...) : tracer quand même.
                    channelInterrupted = true;
                    interruptedChannelLabel = defenderChannelLabel;
                    ChannelingState.Cancel(defender);
                }
            }

            string headerTag = duel.IsStealthAttack
                ? "🥷 <b>EMBUSCADE DEPUIS LE STEALTH</b>"
                : (!effectiveDefenderWantsToDefend
                    ? (duel.IsDefenderIncapacitated ? "💥 <b>FRAPPE SUR CIBLE NEUTRALISÉE</b>" : "🎯 <b>TOUCHÉ SUR CIBLE SANS DÉFENSE</b>")
                    : (wasDeflected
                        ? "⚠️ <b>DÉVIATION BALISTIQUE</b>"
                        : (attackRoll.IsCriticalSuccess ? "💥 <b>COUP CRITIQUE CHIRURGICAL</b>" : "🎯 <b>TOUCHÉ CHIRURGICAL</b>")));

            string hitPartStr = wasDeflected
                ? $"Visait <b>{targetInfo.DisplayName}</b> ➔ Dévie au d6 [Tirage {deflectionRoll} : {deflectionMedical}] sur <b>{actualInfo.DisplayName}</b> (Diff 0)"
                : $"Frappe sur <b>{actualInfo.DisplayName}</b>";

            string log = $"{headerTag} : {attacker.Name} ➔ {defender.Name} ({hitPartStr})\n";
            log += $"   {phaseAtt}\n";
            log += $"   {phaseDef}\n";
            log += $"   🎲 <b>RÉVÉLATION SIMULTANÉE :</b> Attaque {attackRollStr} vs Défense {defRollStr} ➔ <b>Différentiel Net : <color=#00E5FF>{(differential > 0 ? $"+{differential}" : "0")}</color></b>\n";
            if (attHasCrit)
            {
                log += $"   💥 <b>[CONSÉQUENCE CRITIQUE §8.2]</b> Relance {duel.AttackDie} ➔ <b>{attCrit.RawRoll}</b> (Palier {attCrit.Threshold} : <i>{attCrit.Title}</i>) — {attCrit.Description}\n";
            }
            log += $"   ⚔️ <b>Dégâts :</b> [{dmgFormula} = {rawDamage} Bruts] &minus; [Armure {totalArmor} (Absorbé: {absorbed})]"
                + (shieldAbsorbed > 0 ? $" &minus; [🔮Bouclier {shieldAbsorbed}]" : "")
                + $" ➔ <b><color=#FF3B5C>{finalDamage} Dégâts Nets</color></b>\n";
            log += $"   ❤️ <b>Vitalité {defender.Name} :</b> {prevHp} ➔ <b>{defender.CurrentHealth}/{defender.MaxHealth} PV</b>";

            if (exceededEncaissement)
            {
                log += $" | ⚡ <b>CHOC TRAUMATIQUE</b> (&gt; Encaissement {defender.EncaissementThreshold}) ➔ [{inflictedStatus}]";
            }

            if (channelInterrupted)
            {
                log += $" | ⏳ <b>CANALISATION INTERROMPUE</b> ({finalDamage} dégâts nets &gt; encaissement {defender.EncaissementThreshold}) : « {interruptedChannelLabel} » perdue !";
            }

            if (failleExploitee)
            {
                log += " | 📡 <b>FAILLE EXPOSÉE EXPLOITÉE</b> (Analyse de Faille : encaissement ignoré)";
            }

            if (isThomasSymbioticImmune)
            {
                log += " | 🛡️ <b>ANCRE SYMBIOTIQUE</b> (0 dégât : neutralité de fréquence face aux alliés)";
            }

            if (bleedApplied)
            {
                log += $" | 🩸 <b>HÉMORRAGIE (Livre VI §24.4)</b> : Saignement (DoT résiduel {bleedDoT})";
            }

            if (fireApplied)
            {
                log += $" | 🔥 <b>COMBUSTION (Livre VI §24.4)</b> : En Feu pendant {fireTurns} tour(s)";
            }

            if (!string.IsNullOrEmpty(titanBossTag))
            {
                log += titanBossTag;
            }

            if (stunApplied)
            {
                log += " | 😵 <b>ASSOMMEMENT NET (Livre VI §24.4)</b> : Inconscient 1 tour direct";
            }

            if (wasDisintegrated)
            {
                log += " | ⚛️ <b>DÉSINTÉGRATION TOTALE (Livre VI §24.4 : Diff >= 20 & Destruction 6)</b> : Cible pulvérisée en poussière !";
            }
            else if (fatalRes == FatalBlowResolution.InstantDeath)
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

            if (moraleRes.Triggered)
            {
                log += "\n   " + moraleRes.Log;
            }

            bool causedKnockback = !isThomasSymbioticImmune && (critCausedKnockback
                || (duel.IsMartialArtsStrike && (exceededEncaissement || attackRoll.IsCriticalSuccess || differential >= 4))
                || (attacker.HasSpecialization("Teep de Rupture") && duel.AttackSkill == SkillType.MainsNues && differential >= 0));

            // Livre I §5 : l'attaquant a RÉUSSI (touché, même dévié diff 0) → +1 case de progression.
            log += _progression.ApplySkillProgression(attacker, attackSkill, true);

            // RD-049 : toute attaque rompt la furtivité
            StealthState.BreakStealth(attacker);

            return new DamageResult
            {
                IsHit = true,
                IsBlocked = false,
                IsCritical = attHasCrit,
                HasCriticalConsequence = attHasCrit,
                CriticalConsequence = attCrit,
                AttackRoll = attackRoll,
                DefenseRoll = defenseRoll,
                TargetPart = targetedPart,
                ActualHitPart = actualHitPart,
                WasDeflected = wasDeflected,
                DeflectionRoll = deflectionRoll,
                DeflectionMedicalConsequence = deflectionMedical,
                WasDisintegrated = wasDisintegrated,
                Differential = differential,
                RawDamage = rawDamage,
                ArmorAbsorbed = absorbed,
                ShieldAbsorbed = shieldAbsorbed,
                FinalDamageApplied = finalDamage,
                ExceededEncaissement = exceededEncaissement,
                InflictedStatus = inflictedStatus,
                FatalResolution = fatalRes,
                CausedKnockback = causedKnockback,
                IsCanonEntrave = duel.AppliedCanonEntrave,
                Cover = duel.Cover,
                CoverAttackPenalty = duel.CoverAttackPenalty,
                BlockedByCover = false,
                CombatLog = log,
                MoraleResult = moraleRes,
                ChannelInterrupted = channelInterrupted,
                InterruptedChannelLabel = interruptedChannelLabel
            };
        }

        private static void ApplyOffensiveCriticalEffect(
            CriticalConsequence crit,
            CharacterStats attacker,
            CharacterStats defender,
            ref StatusEffect inflictedStatus,
            ref bool causedKnockback,
            ref FatalBlowResolution fatalRes)
        {
            switch (crit.Threshold)
            {
                case 1:
                    inflictedStatus |= StatusEffect.ATerre;
                    defender.ActiveStatus |= StatusEffect.ATerre;
                    break;
                case 2:
                    causedKnockback = true;
                    break;
                case 6:
                    inflictedStatus |= StatusEffect.Etourdi;
                    defender.ApplyStatus(StatusEffect.Etourdi, 1);
                    break;
                case 8:
                    ClearOneNegativeStatus(attacker);
                    break;
                case 9:
                    attacker.Essoufflement = Math.Max(0, attacker.Essoufflement - 1);
                    break;
                case 10:
                    inflictedStatus |= StatusEffect.Ralenti;
                    defender.ApplyStatus(StatusEffect.Ralenti, 1);
                    break;
                case 12:
                    inflictedStatus |= StatusEffect.Destabilise;
                    defender.ApplyStatus(StatusEffect.Destabilise, 1);
                    break;
                case 16:
                    attacker.CurrentActionPoints = Math.Min(attacker.MaxActionPoints, attacker.CurrentActionPoints + 2);
                    break;
                case 20:
                    defender.ApplyResidualDamage(StatusEffect.Saignement, 3);
                    inflictedStatus |= StatusEffect.Saignement;
                    defender.ActiveStatus |= StatusEffect.Saignement;
                    break;
                case 24:
                    if (Enum.TryParse<StatusEffect>("Inconscient", out var inc) || Enum.TryParse<StatusEffect>("Inconscience", out inc))
                    {
                        inflictedStatus |= inc;
                        defender.ActiveStatus |= inc;
                    }
                    fatalRes = FatalBlowResolution.ForcedUnconscious;
                    break;
            }
        }

        private static void ApplyDefensiveCriticalEffect(
            CriticalConsequence crit,
            CharacterStats defender,
            CharacterStats attacker)
        {
            switch (crit.Threshold)
            {
                case 1:
                case 24:
                    attacker.ActiveStatus |= StatusEffect.ATerre;
                    break;
                case 6:
                    attacker.ApplyStatus(StatusEffect.Etourdi, 1);
                    break;
                case 8:
                    ClearOneNegativeStatus(defender);
                    break;
                case 9:
                    defender.Essoufflement = Math.Max(0, defender.Essoufflement - 1);
                    break;
                case 10:
                    attacker.ApplyStatus(StatusEffect.Ralenti, 1);
                    break;
                case 12:
                    attacker.ApplyStatus(StatusEffect.Destabilise, 1);
                    break;
                case 16:
                    defender.CurrentActionPoints = Math.Min(defender.MaxActionPoints, defender.CurrentActionPoints + 2);
                    break;
                case 20:
                    // Contre-attaque automatique : riposte réflexe infligeant 3 dégâts bruts à l'assaillant
                    attacker.CurrentHealth = Math.Max(0, attacker.CurrentHealth - 3);
                    break;
            }
        }

        private static void ClearOneNegativeStatus(CharacterStats target)
        {
            if (target == null) return;
            if ((target.ActiveStatus & StatusEffect.Destabilise) != 0) target.ActiveStatus &= ~StatusEffect.Destabilise;
            else if ((target.ActiveStatus & StatusEffect.Etourdi) != 0) target.ActiveStatus &= ~StatusEffect.Etourdi;
            else if ((target.ActiveStatus & StatusEffect.Ralenti) != 0) target.ActiveStatus &= ~StatusEffect.Ralenti;
            else if ((target.ActiveStatus & StatusEffect.ATerre) != 0) target.ActiveStatus &= ~StatusEffect.ATerre;
            else if ((target.ActiveStatus & StatusEffect.Sonne) != 0) target.ActiveStatus &= ~StatusEffect.Sonne;
        }

        // RD-051 hook (Livre V §22.3) : +100 armes lourdes vs véhicules /
        // structures. Le duel aveugle standard ne connaît pas de cible véhicule
        // (que des CharacterStats) : l'appelant véhicule (arène, grenades,
        // SpecialManeuverService.StrikeVehicleSubsystem) applique ce bonus via
        // VehicleMechanics.ApplyHeavyDamage. Wrapper conservé ici pour traçabilité.
        public static int ApplyHeavyBonusVsVehicle(
            int rawDamage,
            Killtime.Core.Inventory.InventoryItem weapon,
            bool targetIsVehicleOrStructure)
        {
            return VehicleMechanics.ApplyHeavyDamage(rawDamage, weapon, targetIsVehicleOrStructure);
        }
    }
}

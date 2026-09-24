using System;
using System.Collections.Generic;
using Killtime.Core.Character;
using Killtime.Core.Dice;

namespace Killtime.Core.Arcanotech
{
    /// <summary>
    /// Moteur d'ingénierie arcanique conforme au Livre IV :
    /// - Catalogue officiel modulaire (§19.2)
    /// - Règle d'or XP = PA et pénalité d'hybridation
    /// - Calcul balistique de portée magique (§16)
    /// - Résolution d'incantation et application des effets
    /// </summary>
    public static class ArcanotechWorkshop
    {
        private static readonly Dictionary<ArcanotechModuleId, PowerModuleDefinition> _catalog = new()
        {
            // --- OFFENSIFS (Livre IV §19.2) ---
            [ArcanotechModuleId.Offensive_TestBonus] = new PowerModuleDefinition(
                ArcanotechModuleId.Offensive_TestBonus,
                "+1 à l'Épreuve Offensive",
                "+1 bonus au résultat de l'épreuve par rang.",
                PowerCategory.Offensif, 1, isStackable: true, valuePerRank: 1),

            [ArcanotechModuleId.Offensive_DirectDamage] = new PowerModuleDefinition(
                ArcanotechModuleId.Offensive_DirectDamage,
                "+2 Dégâts directs",
                "+2 dégâts bruts directs par rang (absorbables par l'armure).",
                PowerCategory.Offensif, 1, isStackable: true, valuePerRank: 2),

            [ArcanotechModuleId.Offensive_DirectAbsoluteDamage] = new PowerModuleDefinition(
                ArcanotechModuleId.Offensive_DirectAbsoluteDamage,
                "+1 Dégât Absolu direct",
                "1 dégât absolu ignorant l'armure et la Constitution.",
                PowerCategory.Offensif, 1, isStackable: true, valuePerRank: 1),

            [ArcanotechModuleId.Offensive_DrainAP] = new PowerModuleDefinition(
                ArcanotechModuleId.Offensive_DrainAP,
                "Perte de 2 PA",
                "La cible perd 2 PA reportables au tour suivant par rang.",
                PowerCategory.Offensif, 1, isStackable: true, valuePerRank: 2),

            [ArcanotechModuleId.Offensive_Debalance] = new PowerModuleDefinition(
                ArcanotechModuleId.Offensive_Debalance,
                "Débalancé (1 tour)",
                "La cible perd son assiette et subit l'état Débalancé.",
                PowerCategory.Offensif, 1, isStackable: true, valuePerRank: 1),

            [ArcanotechModuleId.Offensive_Knockdown] = new PowerModuleDefinition(
                ArcanotechModuleId.Offensive_Knockdown,
                "Chute au sol (À terre)",
                "La cible chute immédiatement au sol.",
                PowerCategory.Offensif, 2, isStackable: false, valuePerRank: 1),

            [ArcanotechModuleId.Offensive_ResidualDamage_Tier1] = new PowerModuleDefinition(
                ArcanotechModuleId.Offensive_ResidualDamage_Tier1,
                "2 Dégâts Résiduels continus",
                "2 dégâts résiduels continus dégressifs (-1 par tour).",
                PowerCategory.Offensif, 3, isStackable: false, valuePerRank: 2),

            [ArcanotechModuleId.Offensive_Destabilise] = new PowerModuleDefinition(
                ArcanotechModuleId.Offensive_Destabilise,
                "Déstabilisé (1 tour)",
                "La cible est Déstabilisée (-2 EC).",
                PowerCategory.Offensif, 3, isStackable: true, valuePerRank: 1),

            [ArcanotechModuleId.Offensive_Immobilise] = new PowerModuleDefinition(
                ArcanotechModuleId.Offensive_Immobilise,
                "Immobilisé (1 tour)",
                "Mouvement impossible pendant 1 tour.",
                PowerCategory.Offensif, 3, isStackable: true, valuePerRank: 1),

            [ArcanotechModuleId.Offensive_AbsoluteDamageArea] = new PowerModuleDefinition(
                ArcanotechModuleId.Offensive_AbsoluteDamageArea,
                "+1 Dégât Absolu en zone (1 case)",
                "1 dégât absolu à toutes les créatures situées à 1 case de zone.",
                PowerCategory.Offensif, 3, isStackable: true, valuePerRank: 1),

            [ArcanotechModuleId.Offensive_Ralenti] = new PowerModuleDefinition(
                ArcanotechModuleId.Offensive_Ralenti,
                "Ralenti (1 tour)",
                "Toute action de la cible coûte le double de PA.",
                PowerCategory.Offensif, 4, isStackable: true, valuePerRank: 1),

            [ArcanotechModuleId.Offensive_ResidualDamage_Tier2] = new PowerModuleDefinition(
                ArcanotechModuleId.Offensive_ResidualDamage_Tier2,
                "3 Dégâts Résiduels continus",
                "3 dégâts résiduels continus dégressifs.",
                PowerCategory.Offensif, 6, isStackable: false, valuePerRank: 3),

            [ArcanotechModuleId.Offensive_Paralyse] = new PowerModuleDefinition(
                ArcanotechModuleId.Offensive_Paralyse,
                "Paralysé (1 tour)",
                "Incapacité totale d'action et de réaction.",
                PowerCategory.Offensif, 7, isStackable: true, valuePerRank: 1),

            [ArcanotechModuleId.Offensive_Sonne] = new PowerModuleDefinition(
                ArcanotechModuleId.Offensive_Sonne,
                "Sonné (1 tour)",
                "Paralysé et chute au sol pendant 1 tour.",
                PowerCategory.Offensif, 8, isStackable: true, valuePerRank: 1),

            [ArcanotechModuleId.Offensive_ResidualDamage_Tier3] = new PowerModuleDefinition(
                ArcanotechModuleId.Offensive_ResidualDamage_Tier3,
                "4 Dégâts Résiduels continus",
                "4 dégâts résiduels continus dégressifs.",
                PowerCategory.Offensif, 10, isStackable: false, valuePerRank: 4),

            // --- DÉFENSIFS & UTILITAIRES (Livre IV §19.2) ---
            [ArcanotechModuleId.Defensive_TestBonus] = new PowerModuleDefinition(
                ArcanotechModuleId.Defensive_TestBonus,
                "+1 à l'Épreuve Défensive/Utilitaire",
                "+1 bonus au jet de réaction ou d'utilité.",
                PowerCategory.Defensif, 1, isStackable: true, valuePerRank: 1),

            [ArcanotechModuleId.Defensive_ResidualResistance] = new PowerModuleDefinition(
                ArcanotechModuleId.Defensive_ResidualResistance,
                "+2 Résistance Résiduelle",
                "Absorption directe sur le calcul des blessures.",
                PowerCategory.Defensif, 3, isStackable: false, valuePerRank: 2),

            [ArcanotechModuleId.Utility_Rapide] = new PowerModuleDefinition(
                ArcanotechModuleId.Utility_Rapide,
                "État Rapide (1 tour)",
                "+3 PA réservés au déplacement.",
                PowerCategory.Utilitaire, 3, isStackable: false, valuePerRank: 1),

            [ArcanotechModuleId.Utility_Survolte] = new PowerModuleDefinition(
                ArcanotechModuleId.Utility_Survolte,
                "État Survolté (1 tour)",
                "+1 EC et +3 PA d'action.",
                PowerCategory.Utilitaire, 4, isStackable: false, valuePerRank: 1),

            [ArcanotechModuleId.Utility_EnTranse] = new PowerModuleDefinition(
                ArcanotechModuleId.Utility_EnTranse,
                "État En Transe (1 tour)",
                "+2 EC et +5 PA d'action.",
                PowerCategory.Utilitaire, 7, isStackable: false, valuePerRank: 1),

            [ArcanotechModuleId.Utility_EnVol] = new PowerModuleDefinition(
                ArcanotechModuleId.Utility_EnVol,
                "État En Vol (1 tour)",
                "Déplacement tridimensionnel à demi-coût.",
                PowerCategory.Utilitaire, 7, isStackable: false, valuePerRank: 1),

            [ArcanotechModuleId.Utility_Accelere] = new PowerModuleDefinition(
                ArcanotechModuleId.Utility_Accelere,
                "État Accéléré (1 tour)",
                "Tout déplacement coûte moitié PA.",
                PowerCategory.Utilitaire, 8, isStackable: false, valuePerRank: 1),

            [ArcanotechModuleId.Utility_Levitation] = new PowerModuleDefinition(
                ArcanotechModuleId.Utility_Levitation,
                "État Lévitation (1 tour)",
                "Lévitation arcanique sans effort.",
                PowerCategory.Utilitaire, 9, isStackable: false, valuePerRank: 1)
        };

        public static PowerModuleDefinition GetModuleDefinition(ArcanotechModuleId id)
        {
            return _catalog.TryGetValue(id, out var def) ? def : null;
        }

        public static IEnumerable<PowerModuleDefinition> GetAllModuleDefinitions() => _catalog.Values;

        public static bool ValidateSpell(NythariteSpell spell, out string error)
        {
            if (spell == null)
            {
                error = "Le pouvoir est null.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(spell.Name))
            {
                error = "Le pouvoir doit posséder un nom.";
                return false;
            }

            if (spell.Modules != null)
            {
                for (int i = 0; i < spell.Modules.Count; i++)
                {
                    var mod = spell.Modules[i];
                    if (mod == null) continue;
                    var def = GetModuleDefinition(mod.ModuleId);
                    if (def == null)
                    {
                        error = $"Module inconnu : {mod.ModuleId}";
                        return false;
                    }
                    if (!def.IsStackable && mod.Rank > 1)
                    {
                        error = $"Le module '{def.DisplayName}' n'est pas cumulable (rang max = 1).";
                        return false;
                    }
                }
            }

            error = null;
            return true;
        }

        public static bool CanLearnSpell(CharacterSheet sheet, NythariteSpell spell, out string reason)
        {
            if (sheet == null)
            {
                reason = "Fiche de personnage inexistante.";
                return false;
            }
            if (!ValidateSpell(spell, out reason)) return false;

            if (sheet.AvailableXP < spell.CreationXpCost)
            {
                reason = $"XP insuffisant ({sheet.AvailableXP}/{spell.CreationXpCost} requis).";
                return false;
            }

            reason = null;
            return true;
        }

        public static bool LearnSpell(CharacterSheet sheet, NythariteSpell spell, out string message)
        {
            if (!CanLearnSpell(sheet, spell, out string failReason))
            {
                message = failReason;
                return false;
            }

            sheet.AvailableXP -= spell.CreationXpCost;
            sheet.TotalSpentXP += spell.CreationXpCost;
            sheet.LearnedSpells ??= new List<NythariteSpell>();
            sheet.LearnedSpells.Add(spell);

            message = $"Sort '{spell.Name}' appris avec succès ! (-{spell.CreationXpCost} XP | Coût combat: {spell.ActionPointCost} PA).";
            return true;
        }

        public static NythariteSpell CreateRalentissementGravitationnel()
        {
            return new NythariteSpell(
                name: "Ralentissement Gravitationnel",
                description: "L'atmosphère devient pesante et tout semble se figer autour de la cible.",
                skill: SkillType.MagieEsprit,
                associatedAttribute: "Magie",
                discipline: PsychicDiscipline.Telekinesie,
                stage: PsychicStage.Stade2_AccesProfond,
                modules: new[]
                {
                    new PowerModuleSelection(ArcanotechModuleId.Offensive_TestBonus, 1),
                    new PowerModuleSelection(ArcanotechModuleId.Offensive_DrainAP, 2)
                }
            );
        }

        public static NythariteSpell CreateEcrasementDeMatiere()
        {
            return new NythariteSpell(
                name: "Écrasement de Matière",
                description: "En concentrant la gravité sur un membre précis, la matière cède et se compresse.",
                skill: SkillType.MagieEsprit,
                associatedAttribute: "Magie",
                discipline: PsychicDiscipline.Telekinesie,
                stage: PsychicStage.Stade2_AccesProfond,
                modules: new[]
                {
                    new PowerModuleSelection(ArcanotechModuleId.Offensive_TestBonus, 1),
                    new PowerModuleSelection(ArcanotechModuleId.Offensive_DirectAbsoluteDamage, 2)
                }
            );
        }

        public static SpellCastResult CastSpell(
            CharacterStats caster,
            CharacterStats target,
            NythariteSpell spell,
            int distanceInTiles,
            bool cancelRangePenaltyWithAP = false,
            DiceRoller diceRoller = null)
        {
            if (caster == null) throw new ArgumentNullException(nameof(caster));
            if (spell == null) throw new ArgumentNullException(nameof(spell));

            diceRoller ??= new DiceRoller();

            int trainingCount = 0;
            if (caster.Sheet != null)
            {
                var sk = caster.Sheet.GetSkill(spell.AssociatedSkill);
                if (sk != null) trainingCount = sk.TrainingLevel;
            }

            int baseRange = NythariteSpell.CalculateBaseRange(caster.Attributes.Magie, trainingCount);
            int distancePenalty = cancelRangePenaltyWithAP ? 0 : NythariteSpell.CalculateDistancePenalty(distanceInTiles, baseRange);

            int totalPaCost = spell.ActionPointCost + (cancelRangePenaltyWithAP ? 1 : 0);

            if (!caster.ConsumeActionPoints(totalPaCost))
            {
                return new SpellCastResult
                {
                    IsSuccess = false,
                    SpellName = spell.Name,
                    CasterName = caster.Name,
                    TargetName = target != null ? target.Name : "Zone",
                    DistanceInTiles = distanceInTiles,
                    BaseRange = baseRange,
                    DistancePenalty = distancePenalty,
                    ActionPointsSpent = 0,
                    CombatLog = $"⚠️ PA insuffisants pour lancer {spell.Name} ({caster.CurrentActionPoints}/{totalPaCost} PA)."
                };
            }

            caster.RegisterAttack();

            bool isSymbioticImmune = false;
            if (target != null && ThomasCharacter.IsImmuneToCasterMagic(target.Sheet, caster.Sheet))
            {
                isSymbioticImmune = true;
                if (target.HasSpecialization("Ancre Symbiotique : Paratonnerre Cinétique"))
                {
                    target.CurrentActionPoints = Math.Min(target.MaxActionPoints + 2, target.CurrentActionPoints + 1);
                }
            }

            int testBonus = 0;
            int directDamage = 0;
            int absoluteDamage = 0;
            int residualDamage = 0;
            int drainAP = 0;
            var targetStatuses = StatusEffect.None;
            var casterBuffs = StatusEffect.None;

            if (spell.Modules != null)
            {
                for (int i = 0; i < spell.Modules.Count; i++)
                {
                    var sel = spell.Modules[i];
                    if (sel == null || sel.Rank <= 0) continue;
                    var def = GetModuleDefinition(sel.ModuleId);
                    if (def == null) continue;

                    switch (sel.ModuleId)
                    {
                        case ArcanotechModuleId.Offensive_TestBonus:
                            testBonus += sel.Rank;
                            break;
                        case ArcanotechModuleId.Offensive_DirectDamage:
                            directDamage += sel.Rank * 2;
                            break;
                        case ArcanotechModuleId.Offensive_DirectAbsoluteDamage:
                            absoluteDamage += sel.Rank;
                            break;
                        case ArcanotechModuleId.Offensive_AbsoluteDamageArea:
                            absoluteDamage += sel.Rank;
                            break;
                        case ArcanotechModuleId.Offensive_DrainAP:
                            drainAP += sel.Rank * 2;
                            break;
                        case ArcanotechModuleId.Offensive_Debalance:
                            targetStatuses |= StatusEffect.Debalance;
                            break;
                        case ArcanotechModuleId.Offensive_Knockdown:
                            targetStatuses |= StatusEffect.ATerre;
                            break;
                        case ArcanotechModuleId.Offensive_ResidualDamage_Tier1:
                            residualDamage = Math.Max(residualDamage, 2);
                            break;
                        case ArcanotechModuleId.Offensive_Destabilise:
                            targetStatuses |= StatusEffect.Destabilise;
                            break;
                        case ArcanotechModuleId.Offensive_Immobilise:
                            targetStatuses |= StatusEffect.Immobilise;
                            break;
                        case ArcanotechModuleId.Offensive_Ralenti:
                            targetStatuses |= StatusEffect.Ralenti;
                            break;
                        case ArcanotechModuleId.Offensive_ResidualDamage_Tier2:
                            residualDamage = Math.Max(residualDamage, 3);
                            break;
                        case ArcanotechModuleId.Offensive_Paralyse:
                            targetStatuses |= StatusEffect.Paralyse;
                            break;
                        case ArcanotechModuleId.Offensive_Sonne:
                            targetStatuses |= StatusEffect.Sonne | StatusEffect.ATerre;
                            break;
                        case ArcanotechModuleId.Offensive_ResidualDamage_Tier3:
                            residualDamage = Math.Max(residualDamage, 4);
                            break;

                        case ArcanotechModuleId.Defensive_TestBonus:
                            testBonus += sel.Rank;
                            break;
                        case ArcanotechModuleId.Utility_Rapide:
                            casterBuffs |= StatusEffect.Rapide;
                            break;
                        case ArcanotechModuleId.Utility_Survolte:
                            casterBuffs |= StatusEffect.Survolte;
                            break;
                        case ArcanotechModuleId.Utility_EnTranse:
                            casterBuffs |= StatusEffect.EnTranse;
                            break;
                        case ArcanotechModuleId.Utility_EnVol:
                            casterBuffs |= StatusEffect.EnVol;
                            break;
                        case ArcanotechModuleId.Utility_Accelere:
                            casterBuffs |= StatusEffect.Accelere;
                            break;
                        case ArcanotechModuleId.Utility_Levitation:
                            casterBuffs |= StatusEffect.Levitation;
                            break;
                    }
                }
            }

            DiceType skillDie = caster.GetSkillDie(spell.AssociatedSkill, true);
            int effectiveMod = caster.GetStatusModifier(spell.AssociatedSkill, isOffensive: true) + testBonus + distancePenalty;
            var roll = diceRoller.Roll(skillDie, effectiveMod, 12);

            int finalDamageApplied = 0;
            if (!isSymbioticImmune && target != null)
            {
                int absorbed = Math.Min(target.BaseArmorAbsorption, directDamage);
                int netDirect = Math.Max(0, directDamage - absorbed);
                finalDamageApplied = netDirect + absoluteDamage;

                target.CurrentHealth = Math.Max(0, target.CurrentHealth - finalDamageApplied);

                if (drainAP > 0)
                {
                    target.CurrentActionPoints = Math.Max(0, target.CurrentActionPoints - drainAP);
                }

                if (targetStatuses != StatusEffect.None)
                {
                    target.ApplyStatus(targetStatuses, 1);
                }
            }

            if (casterBuffs != StatusEffect.None)
            {
                caster.ApplyStatus(casterBuffs, 1);
            }

            string log = $"🔮 <b>ARCANOTECH</b> : {caster.Name} canalise <b>{spell.Name}</b> ({totalPaCost} PA)\n"
                + $"   🎯 Portée {distanceInTiles}/{baseRange} cases (Malus: {distancePenalty})\n"
                + $"   🎲 Épreuve [{spell.AssociatedSkill}] : {skillDie} (Total {roll.Total})\n";

            if (isSymbioticImmune)
            {
                log += $"   🛡️ <b>[ANCRE SYMBIOTIQUE]</b> {target.Name} absorbe la 5e Force sans dommage.\n";
            }
            else if (target != null)
            {
                log += $"   💥 Impact sur {target.Name} : {finalDamageApplied} PV infligés (Absolu: {absoluteDamage} | Bruts: {directDamage}) | PA drainés: -{drainAP}\n";
                if (targetStatuses != StatusEffect.None)
                {
                    log += $"   ⚡ Altérations subies : [{targetStatuses}]\n";
                }
            }

            return new SpellCastResult
            {
                IsSuccess = roll.IsSuccess,
                SpellName = spell.Name,
                CasterName = caster.Name,
                TargetName = target != null ? target.Name : "Soi-même / Zone",
                DistanceInTiles = distanceInTiles,
                BaseRange = baseRange,
                DistancePenalty = distancePenalty,
                ActionPointsSpent = totalPaCost,
                RollResult = roll,
                DirectDamageDealt = directDamage,
                AbsoluteDamageDealt = absoluteDamage,
                ResidualDamageApplied = residualDamage,
                TotalDamageApplied = finalDamageApplied,
                TargetAPLost = drainAP,
                InflictedStatus = isSymbioticImmune ? StatusEffect.None : targetStatuses,
                CasterBuffsApplied = casterBuffs,
                WasImmunizedBySymbiosis = isSymbioticImmune,
                CombatLog = log
            };
        }
    }
}
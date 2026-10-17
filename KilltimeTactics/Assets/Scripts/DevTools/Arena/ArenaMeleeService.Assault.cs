using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Audio;
using Killtime.Core.Character;
using Killtime.Core.Combat;
using Killtime.Core.Dice;
using Killtime.Core.Inventory;
using Killtime.Core.Rules;
using Killtime.UI;

namespace Killtime.Tactics.Arena
{
    /// <summary>
    /// Assaut groupe + poursuite RD-084 + refoulement.
    /// Split move-only : contenu verbatim depuis ArenaMeleeService.cs.
    /// </summary>
    public sealed partial class ArenaMeleeService
    {
        /// <summary>
        /// RD-084 : Collecte les participants d'un assaut groupé : alliés de
        /// l'acteur (même camp) vivants, avec assez de PA, à portée de la cible
        /// pour la compétence partagée (contact ≤ 1 en mêlée, portée d'arme au tir).
        /// </summary>
        public List<TacticalUnit> CollectCombinedAttackers(TacticalUnit actor, TacticalUnit target, SkillType attackSkill)
        {
            var turnManager = _ctx.TurnManager;
            var members = new List<TacticalUnit>();
            if (actor == null || target == null) return members;
            if (turnManager == null) return members;

            int cost = CoreRulesConfig.Instance != null ? CoreRulesConfig.Instance.CombinedAttackAPCost : 2;
            bool isRanged = (attackSkill == SkillType.Ballistique || attackSkill == SkillType.ProjectilesTir);

            var order = turnManager.TurnOrder;
            for (int i = 0; i < order.Count; i++)
            {
                var u = order[i];
                if (u == null || u.Stats == null || !u.Stats.IsAlive) continue;
                if (u == target) continue;
                if (u.IsPlayerControlled != actor.IsPlayerControlled) continue;
                if (u.Stats.CurrentActionPoints < cost) continue;

                int dist = TitanFootprint.MinDistanceBetweenUnits(u.CurrentCoords, u.FootprintType, target.CurrentCoords, target.FootprintType);
                if (isRanged)
                {
                    var w = u.Sheet?.GetEquippedWeapon();
                    int maxRange = (w != null && w.RangeInTiles > 1) ? w.RangeInTiles : 1;
                    if (dist > maxRange) continue;
                    if (!ChargeState.CanFireRanged(u.Stats)) continue;
                }
                else
                {
                    if (dist > 1) continue;
                }
                members.Add(u);
            }

            // L'acteur en tête s'il est éligible (pour l'ordre du log).
            if (members.Contains(actor))
            {
                members.Remove(actor);
                members.Insert(0, actor);
            }
            return members;
        }

        /// <summary>
        /// RD-084 : Résout la compétence partagée de l'assaut (mêlée au contact,
        /// Ballistique à distance si l'acteur a une arme à distance).
        /// </summary>
        public bool TryResolveCombinedSkill(TacticalUnit actor, TacticalUnit target, out SkillType attackSkill)
        {
            attackSkill = SkillType.MainsNues;
            if (actor == null || target == null) return false;
            int dist = TitanFootprint.MinDistanceBetweenUnits(actor.CurrentCoords, actor.FootprintType, target.CurrentCoords, target.FootprintType);
            if (dist <= 1)
            {
                var weapon = actor.Sheet?.GetEquippedWeapon();
                attackSkill = weapon != null
                    ? SkillDefinitions.ResolveBaseSkill(weapon.AssociatedSkill)
                    : SkillType.MainsNues;
                // Arme à distance au contact : coup de crosse d'urgence à mains nues.
                if (attackSkill == SkillType.Ballistique || attackSkill == SkillType.ProjectilesTir)
                    attackSkill = SkillType.MainsNues;
                return true;
            }
            var ranged = actor.Sheet?.GetEquippedWeapon();
            if (ranged != null && ranged.RangeInTiles > 1)
            {
                attackSkill = SkillType.Ballistique;
                return true;
            }
            return false;
        }

        /// <summary>
        /// RD-084 : Exécute un assaut groupé (Livre VI §25.2). Les participants
        /// sont synchronisés sur l'initiative du membre le plus lent, leurs
        /// résultats offensifs sommés face à l'unique défense de la cible.
        /// </summary>
        public void ExecuteCombinedAttack(TacticalUnit actor, TacticalUnit target, BodyPart targetedPart = BodyPart.Torse)
        {
            var turnManager = _ctx.TurnManager;
            var calculator = _ctx.Calculator;
            if (actor == null || target == null || actor.Stats == null || target.Stats == null) return;
            if (!actor.Stats.IsAlive || !target.Stats.IsAlive) return;
            if (actor == target) return;

            if (!TryResolveCombinedSkill(actor, target, out SkillType attackSkill))
            {
                _host.ArenaLog($"⚠️ <b>Assaut groupé impossible</b> : {actor.Stats.Name} hors de portée de {target.Stats.Name} (ni contact ni arme à distance).");
                return;
            }

            var members = CollectCombinedAttackers(actor, target, attackSkill);
            int minMembers = CoreRulesConfig.Instance != null ? Math.Max(2, CoreRulesConfig.Instance.CombinedAttackMinMembers) : 2;
            if (members.Count < minMembers)
            {
                _host.ArenaLog($"⚠️ <b>Assaut groupé impossible</b> : {members.Count} assaillant(s) à portée, {minMembers} minimum requis (Livre VI §25.2).");
                return;
            }

            int slowest = turnManager != null ? turnManager.GetSlowestInitiativeFor(members) : GroupAssaultState.GetSlowestInitiative(ToStatsList(members));

            // Dégâts d'arme par participant (arme équipée ou 5 à mains nues).
            var weapons = new List<int>(members.Count);
            for (int i = 0; i < members.Count; i++)
            {
                var w = members[i].Sheet?.GetEquippedWeapon();
                weapons.Add((w != null && w.BaseDamage > 0) ? w.BaseDamage : 5);
            }

            // Mise injectée du menu (attribuée à l'acteur, tête de colonne).
            var bonusAP = new List<int>(members.Count);
            var bonusPE = new List<int>(members.Count);
            for (int i = 0; i < members.Count; i++) { bonusAP.Add(0); bonusPE.Add(0); }
            int injectedAP = CombatUI.CombatContextMenuUI.CurrentInjectedAP;
            int injectedPE = CombatUI.CombatContextMenuUI.CurrentInjectedPE;
            if (members[0] == actor)
            {
                bonusAP[0] = Math.Max(0, injectedAP);
                bonusPE[0] = Math.Max(0, injectedPE);
            }

            var stats = ToStatsList(members);
            var combined = calculator.ResolveCombinedAttack(
                stats, target.Stats, targetedPart,
                attackSkill, SkillType.Esquive,
                weaponBaseDamages: weapons,
                attackerBonusAP: bonusAP, attackerPE: bonusPE,
                defenderAutoStakes: true);

            var actVis = actor.GetComponent<TacticalUnitVisual>();
            var tgtVis = target.GetComponent<TacticalUnitVisual>();

            if (!combined.IsValid && !combined.IsHit)
            {
                _host.ArenaLog($"⚔️ <b>{actor.Stats.Name}</b> appelle l'assaut groupé sur <b>{target.Stats.Name}</b> :\n   {combined.CombatLog}");
                return;
            }

            if (combined.IsHit)
            {
                actVis?.SpawnFloatingText($"⚔️ ASSAUT GROUPÉ x{members.Count} (ini {slowest})", Color.cyan);
                tgtVis?.TriggerHitFlash();
                tgtVis?.SpawnFloatingText($"-{combined.FinalDamageApplied} PV (sommation)", new Color(1.0f, 0.25f, 0.25f));
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayAt(SoundId.Attack_Whoosh, target.transform.position, 0.9f);

                if (combined.FatalResolution == FatalBlowResolution.InstantDeath || combined.FatalResolution == FatalBlowResolution.ForcedUnconscious)
                {
                    tgtVis?.TriggerFallingBackDeath();
                }
                else if (combined.FatalResolution == FatalBlowResolution.EligibleForLastBreath)
                {
                    if (!target.IsPlayerControlled)
                    {
                        target.Stats.ChooseSombrer();
                        tgtVis?.SpawnFloatingText("[SYNCOPE] CHUTE HORS COMBAT (0 PV)", Color.cyan);
                        tgtVis?.TriggerFallingBackDeath();
                    }
                    else
                    {
                        target.Stats.ChooseLastBreath();
                        tgtVis?.SpawnFloatingText("[SURVIE] DERNIER SOUFFLE (0 PV)", new Color(1.0f, 0.5f, 0.1f));
                    }
                }

                if (!target.Stats.IsAlive) _host.RequestAutoTargetNextAlive();
            }
            else
            {
                actVis?.SpawnFloatingText($"Assaut groupé paré x{members.Count}", Color.gray);
                tgtVis?.SpawnFloatingText("PARÉ", Color.cyan);
            }

            _host.ArenaLog($"⚔️ <b>{actor.Stats.Name}</b> déclenche un <b>ASSAUT GROUPÉ x{members.Count}</b> sur <b>{target.Stats.Name}</b> (synchro initiative la plus lente : {slowest}) :\n   {combined.CombatLog}");
            _overwatch.HandleChannelInterruptionFx(target, combined.ChannelInterrupted);
            SnapshotRecorder?.Invoke($"Assaut groupé x{members.Count} : {actor.Stats.Name} -> {target.Stats.Name}");
            _host.RequestMusicUpdate();
            _host.RequestCombatOverCheck();
        }

        private static List<CharacterStats> ToStatsList(List<TacticalUnit> units)
        {
            var stats = new List<CharacterStats>(units != null ? units.Count : 0);
            if (units == null) return stats;
            for (int i = 0; i < units.Count; i++)
            {
                if (units[i] != null && units[i].Stats != null) stats.Add(units[i].Stats);
            }
            return stats;
        }

        /// <summary>
        /// RD-084 : Exécute un round de course-poursuite (Livre VI §25.1) :
        /// concours d'Athlétisme proie vs poursuivants. Succès proie = distance
        /// maintenue (≥ 1 case), faillite = terrain gagné. À 4 faillites cumulées :
        /// arrêt et capture immédiate (Immobilisé).
        /// </summary>
        public void ExecutePursuitRound(TacticalUnit prey, List<TacticalUnit> pursuers)
        {
            var calculator = _ctx.Calculator;
            if (prey == null || prey.Stats == null || !prey.Stats.IsAlive) return;
            if (pursuers == null || pursuers.Count == 0) return;

            var alivePursuers = new List<TacticalUnit>();
            for (int i = 0; i < pursuers.Count; i++)
            {
                var p = pursuers[i];
                if (p != null && p.Stats != null && p.Stats.IsAlive && p != prey) alivePursuers.Add(p);
            }
            if (alivePursuers.Count == 0)
            {
                _host.ArenaLog($"⚠️ <b>Poursuite impossible</b> : aucun poursuivant valide pour {prey.Stats.Name}.");
                return;
            }

            int cost = CoreRulesConfig.Instance != null ? CoreRulesConfig.Instance.PursuitAPCost : 1;
            if (!_ctx.GetInfiniteAP())
            {
                if (prey.Stats.CurrentActionPoints < cost)
                {
                    _host.ArenaLog($"⚠️ PA insuffisants : la fuite exige {cost} PA (reste {prey.Stats.CurrentActionPoints}).");
                    return;
                }
                for (int i = 0; i < alivePursuers.Count; i++)
                {
                    if (alivePursuers[i].Stats.CurrentActionPoints < cost)
                    {
                        _host.ArenaLog($"⚠️ PA insuffisants : {alivePursuers[i].Stats.Name} ne peut poursuivre ({alivePursuers[i].Stats.CurrentActionPoints}/{cost} PA).");
                        return;
                    }
                }
                prey.Stats.ConsumeActionPoints(cost);
                for (int i = 0; i < alivePursuers.Count; i++) alivePursuers[i].Stats.ConsumeActionPoints(cost);
            }

            int injectedAP = CombatUI.CombatContextMenuUI.CurrentInjectedAP;
            int injectedPE = CombatUI.CombatContextMenuUI.CurrentInjectedPE;

            var pursuerStats = new List<CharacterStats>(alivePursuers.Count);
            for (int i = 0; i < alivePursuers.Count; i++) pursuerStats.Add(alivePursuers[i].Stats);

            var round = calculator.ResolvePursuitRound(
                prey.Stats, pursuerStats,
                preyBonusAP: Math.Max(0, injectedAP), preyPE: Math.Max(0, injectedPE),
                pursuersAutoStakes: true);

            var preyVis = prey.GetComponent<TacticalUnitVisual>();
            if (round.Caught)
            {
                preyVis?.SpawnFloatingText("🛑 RATTRAPÉ ET ACCULÉ !", Color.red);
                preyVis?.TriggerHitFlash();
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayAt(SoundId.Trauma_Shock, prey.transform.position, 0.9f);
            }
            else if (round.FailuresAdded > 0)
            {
                preyVis?.SpawnFloatingText($"🏃 Poursuivi ! ({round.TotalFailures}/{round.FailuresToCapture})", new Color(1.0f, 0.6f, 0.1f));
            }
            else
            {
                preyVis?.SpawnFloatingText("🏃 DISTANCE MAINTENUE", Color.green);
            }

            string pursuerNames = "";
            for (int i = 0; i < alivePursuers.Count; i++)
            {
                pursuerNames += (i > 0 ? ", " : "") + alivePursuers[i].Stats.Name;
            }
            _host.ArenaLog($"🏃 <b>Course-poursuite</b> : {prey.Stats.Name} vs {pursuerNames} (-{cost} PA chacun) :\n   {round.CombatLog}");
            SnapshotRecorder?.Invoke($"Poursuite : {prey.Stats.Name} ({round.TotalFailures}/{round.FailuresToCapture})");
            _host.RequestCombatOverCheck();
        }

        public bool TryApplyKnockback(TacticalUnit attacker, TacticalUnit defender)
        {
            var grid = _ctx.Grid;
            if (attacker == null || defender == null || grid == null) return false;

            int dq = defender.CurrentCoords.Q - attacker.CurrentCoords.Q;
            int dr = defender.CurrentCoords.R - attacker.CurrentCoords.R;

            bool isTeepDeRupture = attacker.Stats != null && attacker.Stats.HasSpecialization("Teep de Rupture");
            bool isBedrock = attacker.Stats != null && attacker.Stats.HasSpecialization("Impact de Bedrock");
            int pushDistance = 1;
            if (isTeepDeRupture)
            {
                if (attacker.Stats.HasSpecialization("Teep de Rupture : Brise-Châssis Titan")) pushDistance = 4;
                else if (attacker.Stats.HasSpecialization("Teep de Rupture : Onde de Choc Linéaire")) pushDistance = 3;
                else pushDistance = 2;
            }
            else if (isBedrock)
            {
                pushDistance = 2;
            }

            HexCoordinates currentDest = defender.CurrentCoords;
            int tilesMoved = 0;

            for (int step = 1; step <= pushDistance; step++)
            {
                var nextCoords = new HexCoordinates(defender.CurrentCoords.Q + dq * step, defender.CurrentCoords.R + dr * step);
                var nextNode = grid.GetNode(nextCoords);

                if (nextNode != null && nextNode.IsWalkable && !nextNode.IsOccupied)
                {
                    currentDest = nextCoords;
                    tilesMoved++;
                }
                else
                {
                    if (tilesMoved > 0)
                    {
                        defender.TeleportTo(currentDest, grid);
                    }
                    var defVis = defender.GetComponent<TacticalUnitVisual>();
                    if (isTeepDeRupture)
                    {
                        bool isDevastating = attacker.Stats.HasSpecialization("Teep de Rupture : Impact Dévastateur");
                        int shockDamage = isDevastating ? 6 : 3;
                        defender.Stats.ApplyStatus(StatusEffect.Sonne, 1);
                        defender.Stats.ApplyStatus(StatusEffect.ATerre, 1);
                        if (isDevastating) defender.Stats.ApplyStatus(StatusEffect.Destabilise, 2);
                        defender.Stats.CurrentHealth = Math.Max(0, defender.Stats.CurrentHealth - shockDamage);
                        defVis?.TriggerHitFlash();
                        string shockLabel = isDevastating ? $"[TEEP DÉVASTATEUR] SONNÉ (-{shockDamage} PV) !" : $"[TEEP PAROI] SONNÉ (-{shockDamage} PV) !";
                        defVis?.SpawnFloatingText(shockLabel, Color.red);
                        _host.ArenaLog($"💥 <b>TEEP DE RUPTURE (Collision)</b> : {defender.Stats.Name} est projeté(e) contre un obstacle, subit {shockDamage} dégâts de choc et [Sonné / À Terre] !");
                    }
                    else
                    {
                        defender.Stats.ApplyStatus(StatusEffect.ATerre, 1);
                        defender.Stats.ApplyStatus(StatusEffect.Destabilise, 1);
                        defVis?.SpawnFloatingText("[IMPACT PAROI] À TERRE !", Color.red);
                        _host.ArenaLog($"💥 <b>IMPACT CONTRE OBSTACLE</b> : {defender.Stats.Name} heurte la paroi et s'écroule <b>À Terre</b> !");
                    }
                    return tilesMoved > 0;
                }
            }

            if (tilesMoved > 0)
            {
                defender.TeleportTo(currentDest, grid);
                Killtime.Core.Combat.GrappleState.ReleaseGrapple(defender.Stats);
                var defVis = defender.GetComponent<TacticalUnitVisual>();
                string teepTag = isTeepDeRupture ? "[TEEP DE RUPTURE]" : (isBedrock ? "[IMPACT DE BEDROCK]" : "[REFOULEMENT]");
                defVis?.SpawnFloatingText($"{teepTag} Repoussé ({tilesMoved} cases) !", Color.yellow);
                _host.ArenaLog($"💨 <b>{teepTag}</b> : {defender.Stats.Name} est repoussé(e) de {tilesMoved} case(s) en ({currentDest.Q}, {currentDest.R}) !");
                return true;
            }

            return false;
        }
    }
}

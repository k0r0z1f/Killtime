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
    /// Lutte RD-039 + bousculade RD-040 (prises, degagement, etranglement, trainee, souffle).
    /// Split move-only : contenu verbatim depuis ArenaMeleeService.cs.
    /// </summary>
    public sealed partial class ArenaMeleeService
    {
        /// <summary>
        /// RD-039 : Exécute une prise de lutte au corps-à-corps (2 PA).
        /// Duel opposé aveugle Athlétisme vs Athlétisme. Succès = cible Immobilisée et saisie en lutte.
        /// </summary>
        public void ExecuteGrapple(TacticalUnit actor, TacticalUnit target)
        {
            var calculator = _ctx.Calculator;
            if (actor == null || target == null || actor.Stats == null || target.Stats == null) return;
            if (!actor.Stats.IsAlive || !target.Stats.IsAlive) return;

            int dist = actor.CurrentCoords.DistanceTo(target.CurrentCoords);
            if (dist > 1)
            {
                _host.ArenaLog($"⚠️ <b>Lutte impossible</b> : cible hors de portée de contact ({dist} cases > 1).");
                return;
            }

            if (!_ctx.GetInfiniteAP() && actor.Stats.CurrentActionPoints < CombatDevArena.GrappleAPCost)
            {
                _host.ArenaLog($"⚠️ PA insuffisants : la prise de lutte exige {CombatDevArena.GrappleAPCost} PA (reste {actor.Stats.CurrentActionPoints}).");
                return;
            }

            if (!_ctx.GetInfiniteAP()) actor.Stats.ConsumeActionPoints(CombatDevArena.GrappleAPCost);

            int injectedAP = CombatUI.CombatContextMenuUI.CurrentInjectedAP;
            int injectedPE = CombatUI.CombatContextMenuUI.CurrentInjectedPE;

            var duel = calculator.ResolveGrappleDuel(
                actor.Stats, target.Stats,
                attackerBonusAP: injectedAP, attackerPE: injectedPE,
                defenderAutoStakes: true);

            var actVis = actor.GetComponent<TacticalUnitVisual>();
            var tgtVis = target.GetComponent<TacticalUnitVisual>();

            if (duel.AttackerWins)
            {
                actVis?.SpawnFloatingText("🥋 PRISE EN LUTTE (-2 PA)", Color.cyan);
                tgtVis?.TriggerHitFlash();
                tgtVis?.SpawnFloatingText("IMMOBILISÉ", new Color(1f, 0.45f, 0.15f));
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayAt(SoundId.Trauma_Shock, target.transform.position, 0.8f);
            }
            else
            {
                actVis?.SpawnFloatingText("Prise manquée (-2 PA)", Color.gray);
                tgtVis?.SpawnFloatingText("ESQUIVÉ", Color.cyan);
            }

            _host.ArenaLog($"🥋 <b>{actor.Stats.Name}</b> tente une prise de lutte sur <b>{target.Stats.Name}</b> (-{CombatDevArena.GrappleAPCost} PA) :\n   {duel.CombatLog}");
            SnapshotRecorder?.Invoke($"Lutte : {actor.Stats.Name} -> {target.Stats.Name}");
            _host.RequestCombatOverCheck();
        }

        /// <summary>
        /// RD-039 : Tente de se libérer d'une étreinte de lutte (2 PA).
        /// Duel opposé aveugle Athlétisme/MainsNues/Acrobatie (victime) vs Athlétisme (grappler).
        /// Succès = prise brisée et statut Immobilisé dissipé.
        /// </summary>
        public void ExecuteGrappleEscape(TacticalUnit victim, TacticalUnit grappler)
        {
            var calculator = _ctx.Calculator;
            if (victim == null || grappler == null || victim.Stats == null || grappler.Stats == null) return;
            if (!victim.Stats.IsAlive || !grappler.Stats.IsAlive) return;

            if (!_ctx.GetInfiniteAP() && victim.Stats.CurrentActionPoints < CombatDevArena.GrappleAPCost)
            {
                _host.ArenaLog($"⚠️ PA insuffisants : se dégager exige {CombatDevArena.GrappleAPCost} PA (reste {victim.Stats.CurrentActionPoints}).");
                return;
            }

            if (!_ctx.GetInfiniteAP()) victim.Stats.ConsumeActionPoints(CombatDevArena.GrappleAPCost);

            int injectedAP = CombatUI.CombatContextMenuUI.CurrentInjectedAP;
            int injectedPE = CombatUI.CombatContextMenuUI.CurrentInjectedPE;

            var duel = calculator.ResolveGrappleEscapeDuel(
                victim.Stats, grappler.Stats,
                victimBonusAP: injectedAP, victimPE: injectedPE,
                grapplerAutoStakes: true);

            var vicVis = victim.GetComponent<TacticalUnitVisual>();
            var grpVis = grappler.GetComponent<TacticalUnitVisual>();

            if (duel.AttackerWins)
            {
                vicVis?.SpawnFloatingText("🔓 DÉGAGÉ ! (-2 PA)", Color.green);
                grpVis?.SpawnFloatingText("Prise rompue", Color.gray);
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayAt(SoundId.Trauma_Shock, victim.transform.position, 0.8f);
            }
            else
            {
                vicVis?.SpawnFloatingText("Maintenu en lutte (-2 PA)", Color.red);
            }

            _host.ArenaLog($"🔓 <b>{victim.Stats.Name}</b> tente de se libérer de la prise de <b>{grappler.Stats.Name}</b> (-{CombatDevArena.GrappleAPCost} PA) :\n   {duel.CombatLog}");
            SnapshotRecorder?.Invoke($"Dégagement lutte : {victim.Stats.Name} -> {grappler.Stats.Name}");
            _host.RequestCombatOverCheck();
        }

        /// <summary>
        /// RD-039 : Étranglement au contact sur cible saisie ou immobilisée (2 PA).
        /// Duel opposé aveugle Athlétisme vs Endurance. Succès = Asphyxie (2 tours, DoT + drain PA).
        /// </summary>
        public void ExecuteStrangulation(TacticalUnit actor, TacticalUnit target)
        {
            var calculator = _ctx.Calculator;
            if (actor == null || target == null || actor.Stats == null || target.Stats == null) return;
            if (!actor.Stats.IsAlive || !target.Stats.IsAlive) return;

            int dist = actor.CurrentCoords.DistanceTo(target.CurrentCoords);
            if (dist > 1)
            {
                _host.ArenaLog($"⚠️ <b>Étranglement impossible</b> : hors de portée de contact ({dist} cases > 1).");
                return;
            }

            if (!_ctx.GetInfiniteAP() && actor.Stats.CurrentActionPoints < CombatDevArena.StrangulationAPCost)
            {
                _host.ArenaLog($"⚠️ PA insuffisants : l'étranglement exige {CombatDevArena.StrangulationAPCost} PA (reste {actor.Stats.CurrentActionPoints}).");
                return;
            }

            if (!_ctx.GetInfiniteAP()) actor.Stats.ConsumeActionPoints(CombatDevArena.StrangulationAPCost);

            int injectedAP = CombatUI.CombatContextMenuUI.CurrentInjectedAP;
            int injectedPE = CombatUI.CombatContextMenuUI.CurrentInjectedPE;

            var duel = calculator.ResolveStrangulationDuel(
                actor.Stats, target.Stats,
                attackerBonusAP: injectedAP, attackerPE: injectedPE,
                defenderAutoStakes: true);

            var actVis = actor.GetComponent<TacticalUnitVisual>();
            var tgtVis = target.GetComponent<TacticalUnitVisual>();

            if (duel.AttackerWins)
            {
                actVis?.SpawnFloatingText("🫁 ÉTRANGLEMENT (-2 PA)", new Color(0.2f, 0.95f, 0.75f));
                tgtVis?.TriggerHitFlash();
                tgtVis?.SpawnFloatingText("ASPHYXIE (-1 PA/tour)", new Color(0.2f, 0.95f, 0.75f));
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayAt(SoundId.Trauma_Shock, target.transform.position, 0.9f);
                if (!target.Stats.IsAlive) _host.RequestAutoTargetNextAlive();
            }
            else
            {
                actVis?.SpawnFloatingText("Étranglement repoussé (-2 PA)", Color.gray);
                tgtVis?.SpawnFloatingText("RÉSISTÉ", Color.cyan);
            }

            _host.ArenaLog($"🫁 <b>{actor.Stats.Name}</b> étrangle <b>{target.Stats.Name}</b> (-{CombatDevArena.StrangulationAPCost} PA) :\n   {duel.CombatLog}");
            SnapshotRecorder?.Invoke($"Étranglement : {actor.Stats.Name} -> {target.Stats.Name}");
            _host.RequestCombatOverCheck();
        }

        /// <summary>
        /// RD-040 : Exécute une Bousculade générique ou un Coup de bouclier (2 PA).
        /// Duel opposé aveugle de Force (Athlétisme / Mains Nues / Défense Corporelle).
        /// Victoire : recul forcé d'1 case (Knockback). Si un mur ou un obstacle bloque le recul, la cible chute À Terre.
        /// </summary>
        public void ExecuteShove(TacticalUnit attacker, TacticalUnit defender, bool isShieldBash, int attackerBonusAP = 0, int attackerPE = 0)
        {
            var calculator = _ctx.Calculator;
            if (_ctx.Guard.IsResolving)
            {
                _host.ArenaLog("⚠️ Action ignorée : une résolution est déjà en cours.");
                return;
            }
            if (attacker == null || defender == null || attacker.Stats == null || defender.Stats == null) return;
            if (!attacker.Stats.IsAlive || !defender.Stats.IsAlive) return;

            if (attacker.IsMoving)
            {
                _host.ArenaLog($"⚠️ <b>{attacker.Stats.Name}</b> est en déplacement : attendez l'arrivée !");
                return;
            }

            int dist = TitanFootprint.MinDistanceBetweenUnits(attacker.CurrentCoords, attacker.FootprintType, defender.CurrentCoords, defender.FootprintType);
            if (dist > 1)
            {
                _host.ArenaLog($"⚠️ <b>Bousculade impossible</b> : cible hors de portée de contact ({dist} cases > 1).");
                return;
            }

            if (TurnManager.IsMultiplayerPlayerClient())
            {
                _host.ArenaLog("⚠️ Bousculade réservée au GM en multijoueur (pas encore synchronisée VTT).");
                return;
            }

            SkillType checkSkill = isShieldBash ? SkillType.DefenseCorporelle : SkillType.Athletisme;
            DiceType atkDie = attacker.Stats.GetSkillDie(checkSkill, true);
            if (!attacker.Stats.CanAttack(atkDie))
            {
                int maxAttacks = attacker.Stats.GetMaxAttacksAllowed(atkDie);
                _host.ArenaLog($"⚠️ <b>{attacker.Stats.Name}</b> a déjà épuisé son quota d'attaque ce tour ({attacker.Stats.AttacksThisTurn}/{maxAttacks}) !");
                return;
            }

            const int shoveCost = 2;
            if (!_ctx.GetInfiniteAP() && attacker.Stats.CurrentActionPoints < shoveCost)
            {
                _host.ArenaLog($"⚠️ PA insuffisants : la bousculade exige {shoveCost} PA (reste {attacker.Stats.CurrentActionPoints}).");
                return;
            }

            if (!_ctx.GetInfiniteAP()) attacker.Stats.ConsumeActionPoints(shoveCost);

            int injectedAP = attackerBonusAP > 0 ? attackerBonusAP : CombatUI.CombatContextMenuUI.CurrentInjectedAP;
            int injectedPE = attackerPE > 0 ? attackerPE : CombatUI.CombatContextMenuUI.CurrentInjectedPE;

            Vector3 combatDir = defender.transform.position - attacker.transform.position;
            combatDir.y = 0f;
            if (combatDir != Vector3.zero)
            {
                attacker.transform.rotation = Quaternion.LookRotation(combatDir);
                if (defender.Stats.CanDefendActively())
                {
                    defender.transform.rotation = Quaternion.LookRotation(-combatDir);
                }
            }

            var attVis = attacker.GetComponent<TacticalUnitVisual>();
            var defVis = defender.GetComponent<TacticalUnitVisual>();

            if (isShieldBash)
            {
                attVis?.TriggerBodyBlock();
            }
            else
            {
                attVis?.TriggerRoundkick();
            }

            if (KilltimeAudioManager.Instance != null)
                KilltimeAudioManager.Instance.PlayAt(SoundId.Attack_Whoosh, attacker.transform.position, 0.7f);

            var result = calculator.ResolveShoveDuel(
                attacker.Stats, defender.Stats, isShieldBash,
                attackerBonusAP: injectedAP, attackerPE: injectedPE,
                defenderAutoStakes: true);

            attacker.Stats.RegisterAttack();
            _host.ArenaLog(result.CombatLog);

            string actionTag = isShieldBash ? "COUP DE BOUCLIER" : "BOUSCULADE";
            if (result.IsHit)
            {
                attVis?.SpawnFloatingText($"💨 {actionTag} (-{shoveCost} PA)", Color.cyan);
                defVis?.TriggerHitFlash();

                if (result.FinalDamageApplied > 0)
                {
                    defVis?.SpawnFloatingText($"-{result.FinalDamageApplied} PV", new Color(1.0f, 0.25f, 0.25f));
                }

                if (result.CausedKnockback)
                {
                    TryApplyKnockback(attacker, defender);
                }

                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayAt(SoundId.Trauma_Shock, defender.transform.position, 0.8f);

                if (result.FatalResolution == FatalBlowResolution.InstantDeath || result.FatalResolution == FatalBlowResolution.ForcedUnconscious)
                {
                    defVis?.TriggerFallingBackDeath();
                }
                else if (result.FatalResolution == FatalBlowResolution.EligibleForLastBreath)
                {
                    if (!defender.IsPlayerControlled)
                    {
                        defender.Stats.ChooseSombrer();
                        defVis?.SpawnFloatingText("[SYNCOPE] CHUTE HORS COMBAT (0 PV)", Color.cyan);
                        defVis?.TriggerFallingBackDeath();
                    }
                    else
                    {
                        defender.Stats.ChooseLastBreath();
                        defVis?.SpawnFloatingText("[SURVIE] DERNIER SOUFFLE (0 PV)", new Color(1.0f, 0.5f, 0.1f));
                    }
                }

                if (!defender.Stats.IsAlive)
                {
                    _host.RequestAutoTargetNextAlive();
                }
            }
            else
            {
                attVis?.SpawnFloatingText($"{actionTag} contenue (-{shoveCost} PA)", Color.gray);
                defVis?.SpawnFloatingText("RÉSISTÉ", Color.cyan);
            }

            SnapshotRecorder?.Invoke($"{actionTag} : {attacker.Stats.Name} -> {defender.Stats.Name}");
            _host.RequestMusicUpdate();
            _host.RequestCombatOverCheck();

            if (Killtime.Multi.VTTTableSync.Instance != null
                && !Killtime.Multi.VTTTableSync.Instance.IsApplyingRemoteAction
                && Killtime.Multi.VTTRoomManager.Instance != null
                && Killtime.Multi.VTTRoomManager.Instance.InRoom
                && Killtime.Multi.VTTRoomManager.Instance.IsGM)
            {
                Killtime.Multi.VTTTableSync.Instance.BroadcastFullCombatState();
            }
        }

        /// <summary>
        /// RD-039 : Vrai si le dragger peut traîner le corps de la victime d'1 case.
        /// </summary>
        public bool CanDragBody(TacticalUnit dragger, TacticalUnit victim)
        {
            var grid = _ctx.Grid;
            if (dragger == null || victim == null || dragger == victim) return false;
            if (dragger.Stats == null || victim.Stats == null) return false;
            if (!dragger.Stats.IsAlive) return false;
            if (!_ctx.GetInfiniteAP() && dragger.Stats.CurrentActionPoints < CombatDevArena.DragBodyAPCost) return false;
            if (dragger.CurrentCoords.DistanceTo(victim.CurrentCoords) > 1) return false;

            bool isDraggable = Killtime.Core.Combat.GrappleState.IsGrappling(dragger.Stats, victim.Stats)
                || victim.Stats.ActiveStatus.HasFlag(StatusEffect.Immobilise)
                || victim.Stats.ActiveStatus.HasFlag(StatusEffect.Inconscient)
                || victim.Stats.ActiveStatus.HasFlag(StatusEffect.ATerre)
                || !victim.Stats.IsAlive;

            if (!isDraggable) return false;
            if (grid == null) return false;

            for (int i = 0; i < 6; i++)
            {
                var n = dragger.CurrentCoords.GetNeighbor(i);
                if (n.Equals(victim.CurrentCoords)) continue;
                if (grid.IsFootprintFree(n, dragger.FootprintType, dragger)) return true;
            }
            return false;
        }

        /// <summary>
        /// RD-039 : Traîne le corps d'une cible saisie, immobilisée ou inconsciente d'1 case (2 PA).
        /// Le porteur recule ou pivote vers une case libre adjacente et tire la victime
        /// dans la case qu'il vient de libérer.
        /// </summary>
        public bool ExecuteDragBody(TacticalUnit dragger, TacticalUnit victim)
        {
            var grid = _ctx.Grid;
            if (!CanDragBody(dragger, victim)) return false;

            HexCoordinates bestHex = default;
            bool found = false;
            int bestDist = -1;

            for (int i = 0; i < 6; i++)
            {
                var n = dragger.CurrentCoords.GetNeighbor(i);
                if (n.Equals(victim.CurrentCoords)) continue;
                if (grid.IsFootprintFree(n, dragger.FootprintType, dragger))
                {
                    int d = n.DistanceTo(victim.CurrentCoords);
                    if (d > bestDist)
                    {
                        bestDist = d;
                        bestHex = n;
                        found = true;
                    }
                }
            }

            if (!found) return false;

            if (!_ctx.GetInfiniteAP()) dragger.Stats.ConsumeActionPoints(CombatDevArena.DragBodyAPCost);

            HexCoordinates vacated = dragger.CurrentCoords;

            var draggerPath = new List<HexCoordinates> { dragger.CurrentCoords, bestHex };
            var victimPath = new List<HexCoordinates> { victim.CurrentCoords, vacated };

            _host.ArenaStartCoroutine(dragger.MoveAlongPath(draggerPath, grid, 0, false, false));
            _host.ArenaStartCoroutine(victim.MoveAlongPath(victimPath, grid, 0, false, false));

            dragger.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("🤼 TRAÎNÉE (-2 PA)", Color.cyan);
            victim.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("TRAÎNÉ", Color.yellow);

            if (KilltimeAudioManager.Instance != null)
                KilltimeAudioManager.Instance.PlayAt(SoundId.Move_Footstep, dragger.transform.position, 0.7f);

            _host.ArenaLog($"🤼 <b>{dragger.Stats.Name}</b> traîne le corps de <b>{victim.Stats.Name}</b> d'1 case vers {vacated} (-{CombatDevArena.DragBodyAPCost} PA) !");
            SnapshotRecorder?.Invoke($"Traînée corps : {dragger.Stats.Name} -> {victim.Stats.Name}");
            return true;
        }

        /// <summary>Refoulement radial : repousse la cible sur le voisin le plus loin du souffle.</summary>
        public bool TryApplyBlastKnockback(TacticalUnit defender, HexCoordinates blastCoords)
        {
            var grid = _ctx.Grid;
            if (defender == null || grid == null) return false;
            HexCoordinates best = defender.CurrentCoords;
            int bestDist = defender.CurrentCoords.DistanceTo(blastCoords);
            for (int dir = 0; dir < 6; dir++)
            {
                var n = defender.CurrentCoords.GetNeighbor(dir);
                var node = grid.GetNode(n);
                if (node == null || !node.IsWalkable || node.IsOccupied) continue;
                int d = n.DistanceTo(blastCoords);
                if (d > bestDist) { bestDist = d; best = n; }
            }
            if (!best.Equals(defender.CurrentCoords) && defender.TeleportTo(best, grid))
            {
                Killtime.Core.Combat.GrappleState.ReleaseGrapple(defender.Stats);
                var vis = defender.GetComponent<TacticalUnitVisual>();
                vis?.SpawnFloatingText("[SOUFFLE] Projeté !", Color.yellow);
                _host.ArenaLog($"💨 <b>SOUFFLE</b> : {defender.Stats.Name} est projeté(e) en ({best.Q}, {best.R}) !");
                return true;
            }
            defender.Stats.ApplyStatus(StatusEffect.Destabilise, 1);
            return false;
        }
    }
}

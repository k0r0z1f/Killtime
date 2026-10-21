using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Core.Combat;
using Killtime.Core.Dice;
using Killtime.Core.Character;
using Killtime.Core.Arcanotech;
using Killtime.Core.Inventory;
using Killtime.Core.Rules;
using Killtime.CameraSystem;

namespace Killtime.Tactics.AI
{
    /// <summary>
    /// RD-115 (lot AITurnPlanner) : exécution des <see cref="AIIntent"/>.
    /// Le planner (<see cref="AITurnPlanner"/>) décide l'ordre ; ce partial
    /// ne fait qu'exécuter les coroutines / Try* déplacés VERBATIM depuis
    /// ExecuteAITurn (mêmes seuils live, mêmes textes, mêmes waits).
    /// Aucune logique de priorité ici : chaque cas applique le seuil
    /// d'origine sur l'état live et signale Failed / ContinueTurn / EndTurn.
    /// </summary>
    public partial class TacticalAIController : MonoBehaviour
    {
        /// <summary>Issue d'une tentative d'intention dans la marche du plan.</summary>
        private enum AIPlanStepOutcome
        {
            Failed,          // tentative sans effet : passer à l'intention suivante
            ContinueTurn,    // action jouée : nouvelle itération (continue d'origine)
            EndTurn,         // tour terminé : break d'origine puis fin de tour
            EndTurnImmediate // reddition : EndCurrentTurn + yield break (sans teardown)
        }

        /// <summary>Cadre mutable d'itération (ex-locaux d'ExecuteAITurn).</summary>
        private sealed class AIPlanFrame
        {
            public TacticalUnit Target;
            public TacticalPosture Posture;
            public bool IsRanged;
            public int MaxRange;
            public SkillType AttackSkill;
            public int Dist;
            public bool HasLOS;
            public CoverType TargetCover;
            public bool InAttackRange;
            public bool TookBreathThisTurn;
        }

        /// <summary>Porteur de l'issue (les coroutines ne retournent rien).</summary>
        private sealed class AIPlanStepState
        {
            public AIPlanStepOutcome Outcome = AIPlanStepOutcome.Failed;
        }

        /// <summary>
        /// Construit le cadre d'itération (posture, portée, LOS, couvert —
        /// verbatim des lectures d'ExecuteAITurn avant la phase 6).
        /// </summary>
        private AIPlanFrame BuildPlanFrame(TacticalUnit unit, TacticalUnit target, bool tookBreathThisTurn)
        {
            var frame = new AIPlanFrame
            {
                Target = target,
                TookBreathThisTurn = tookBreathThisTurn,
                Dist = TitanFootprint.MinDistanceBetweenUnits(unit.CurrentCoords, unit.FootprintType, target.CurrentCoords, target.FootprintType)
            };
            frame.Posture = EvaluateTacticalPosture(unit, target, frame.Dist);

            frame.IsRanged = HasRangedWeapon(unit, out int maxRange, out _, out SkillType attackSkill);
            frame.MaxRange = maxRange;
            frame.AttackSkill = attackSkill;
            if (frame.IsRanged && !ChargeState.CanFireRanged(unit.Stats))
            {
                frame.IsRanged = false; // RD-038 : Le sprint bloque le tir ce tour
            }
            frame.HasLOS = HasLineOfSight(unit.CurrentCoords, target.CurrentCoords, target.FootprintType, unit.FootprintType, unit);
            frame.TargetCover = GetCoverLevel(unit.CurrentCoords, target.CurrentCoords, target.FootprintType, unit.FootprintType, unit);
            frame.InAttackRange = frame.IsRanged ? (frame.Dist <= maxRange && frame.HasLOS) : (frame.Dist <= 1);
            return frame;
        }

        /// <summary>
        /// Recalcule dist / LOS / couvert / portée après le contact (TEEP ou
        /// dégagement) — verbatim des trois lignes de recalcul d'origine.
        /// </summary>
        private void RefreshFrameForStrike(TacticalUnit unit, AIPlanFrame frame)
        {
            frame.Dist = TitanFootprint.MinDistanceBetweenUnits(unit.CurrentCoords, unit.FootprintType, frame.Target.CurrentCoords, frame.Target.FootprintType);
            frame.HasLOS = HasLineOfSight(unit.CurrentCoords, frame.Target.CurrentCoords, frame.Target.FootprintType, unit.FootprintType, unit);
            frame.TargetCover = GetCoverLevel(unit.CurrentCoords, frame.Target.CurrentCoords, frame.Target.FootprintType, unit.FootprintType, unit);
            frame.InAttackRange = frame.IsRanged ? (frame.Dist <= frame.MaxRange && frame.HasLOS) : (frame.Dist <= 1);
        }

        /// <summary>Snapshot pur pour le planner depuis le cadre + état live.</summary>
        private AITurnSnapshot BuildPlanSnapshot(TacticalUnit unit, AIPlanFrame frame)
        {
            return new AITurnSnapshot
            {
                Posture = frame.Posture,
                IsRanged = frame.IsRanged,
                Dist = frame.Dist,
                TargetCover = frame.TargetCover,
                InAttackRange = frame.InAttackRange,
                ActionPoints = unit.Stats.CurrentActionPoints,
                DefensiveReserve = GetRequiredDefensiveReserve(unit, frame.Posture),
                IsWatching = OverwatchState.IsWatching(unit.Stats),
                ArenaAvailable = _arena != null,
                OverwatchAPCost = CoreRulesConfig.Instance.OverwatchAPCost,
                HasMartialArts = unit.Stats.HasSpecialization("Arts Martiaux"),
                CanUnarmedAttack = unit.Stats.CanAttack(unit.Stats.GetSkillDie(SkillType.MainsNues, true)),
            };
        }

        /// <summary>
        /// Marche d'un plan : exécute les intentions dans l'ordre jusqu'à la
        /// première issue non-Failed (appelant : interprète state.Outcome).
        /// </summary>
        private IEnumerator WalkPlan(
            IReadOnlyList<AIIntent> plan, TacticalUnit unit, TacticalUnitVisual visual,
            AIPlanFrame frame, AIPlanStepState state)
        {
            state.Outcome = AIPlanStepOutcome.Failed;
            foreach (var intent in plan)
            {
                yield return StartCoroutine(ExecutePlanIntent(intent, unit, visual, frame, state));
                if (state.Outcome != AIPlanStepOutcome.Failed)
                    break;
            }
        }

        /// <summary>Exécute UNE intention (seuils live verbatim d'origine).</summary>
        private IEnumerator ExecutePlanIntent(
            AIIntent intent, TacticalUnit unit, TacticalUnitVisual visual,
            AIPlanFrame frame, AIPlanStepState state)
        {
            switch (intent)
            {
                case AIIntent.EndTurn_Surrender:
                {
                    // RD-047 : Reddition (Unité désarmée, hors combat)
                    visual?.SpawnFloatingText("🏳️ [REDDITION] Dépôt des armes", Color.white);
                    _turnManager?.EndCurrentTurn();
                    state.Outcome = AIPlanStepOutcome.EndTurnImmediate;
                    break;
                }

                case AIIntent.PanickedFlee:
                {
                    // RD-047 : Déroute & Fuite Panique (StatusEffect.Agonisant)
                    visual?.SpawnFloatingText("😱 [DÉROUTE] Fuite Panique !", new Color(0.95f, 0.2f, 0.85f));
                    yield return StartCoroutine(ExecutePanickedFlee(unit));
                    yield return new WaitForSeconds(_actionDelay);
                    state.Outcome = AIPlanStepOutcome.EndTurn;
                    break;
                }

                case AIIntent.MedicalSupport:
                {
                    // 1. SOUTIEN MÉDICAL D'URGENCE (Livre VI)
                    if (TryExecuteMedicalSupport(unit, visual))
                    {
                        yield return new WaitForSeconds(_actionDelay);
                        state.Outcome = AIPlanStepOutcome.ContinueTurn;
                    }
                    break;
                }

                case AIIntent.AllySupport:
                {
                    // 1b. SOUTIEN TACTIQUE (corde, entraves, traînée, radio,
                    // maintenance, garde du corps) — registre joueur, côté alliés.
                    if (TryExecuteAllySupport(unit, visual))
                    {
                        yield return new WaitForSeconds(_actionDelay);
                        state.Outcome = AIPlanStepOutcome.ContinueTurn;
                    }
                    break;
                }

                case AIIntent.ActiveObservation:
                {
                    // RD-IA01 : face au vide, tentative d'Observation active.
                    if (TryActiveObservationAI(unit, visual))
                    {
                        yield return new WaitForSeconds(_actionDelay);
                        state.Outcome = AIPlanStepOutcome.ContinueTurn;
                    }
                    break;
                }

                case AIIntent.AlertedPatrol:
                {
                    // RD-053 : Patrouille alertée vers bruit / lampe.
                    if (HasAlertedPatrolTarget(unit, out HexCoordinates suspiciousTarget, out string patrolReason))
                    {
                        yield return StartCoroutine(ExecuteAlertedPatrol(unit, suspiciousTarget, patrolReason, visual));
                        yield return new WaitForSeconds(_actionDelay);
                        state.Outcome = AIPlanStepOutcome.ContinueTurn;
                    }
                    break;
                }

                case AIIntent.EndTurn_NoContact:
                {
                    state.Outcome = AIPlanStepOutcome.EndTurn;
                    break;
                }

                case AIIntent.TacticalRetreat:
                {
                    // 2. RETRAITE TACTIQUE SOUS COUVERT (Livre V §21)
                    visual?.SpawnFloatingText("🛡️ [REPLI] Rupture sous Couvert", new Color(1.0f, 0.35f, 0.35f));
                    yield return StartCoroutine(ExecuteCoveredRetreat(unit, frame.Target));
                    yield return new WaitForSeconds(_actionDelay);
                    // RD-IA02 : après repli, verrouille la position en guet si le
                    // surplus le permet (au lieu de finir le tour inactif).
                    if (frame.IsRanged && _arena != null
                        && !OverwatchState.IsWatching(unit.Stats)
                        && unit.Stats.CurrentActionPoints >= CoreRulesConfig.Instance.OverwatchAPCost + GetRequiredDefensiveReserve(unit, frame.Posture))
                    {
                        if (_arena.TryEnterOverwatch(unit, out _))
                            yield return new WaitForSeconds(_actionDelay);
                    }
                    state.Outcome = AIPlanStepOutcome.EndTurn;
                    break;
                }

                case AIIntent.RallyWoundedAlly:
                {
                    // 2a. SOUTIEN : rejoindre l'allié blessé (ralliement).
                    var allyPath = GetWoundedAllyPath(unit);
                    if (allyPath != null && allyPath.Count > 1)
                    {
                        int rawAllyCost = 0;
                        for (int pi = 1; pi < allyPath.Count; pi++)
                        {
                            var anode = _grid != null ? _grid.GetNode(allyPath[pi]) : null;
                            rawAllyCost += anode != null ? anode.ActionPointCost : 1;
                        }
                        int allyApCost = unit.ComputeMovementAPCost(rawAllyCost);
                        visual?.SpawnFloatingText("🩹 Soutien allié", Color.green);
                        EnsureSafeDisengageForMove(unit, allyPath.Count - 1);
                        yield return StartCoroutine(unit.MoveAlongPath(allyPath, _grid, allyApCost));
                        yield return new WaitForSeconds(_actionDelay);
                        state.Outcome = AIPlanStepOutcome.ContinueTurn;
                    }
                    break;
                }

                case AIIntent.CombatTechnique:
                {
                    // 2b. TECHNIQUES DE SPÉCIALISATION (Livre III).
                    if (TryExecuteCombatTechnique(unit, frame.Target, frame.Posture, frame.Dist))
                    {
                        yield return new WaitForSeconds(_actionDelay);
                        state.Outcome = AIPlanStepOutcome.ContinueTurn;
                    }
                    break;
                }

                case AIIntent.LivreXIManeuver:
                {
                    // 2c. MANŒUVRES AVANCÉES PNJ & AUTOMATES (Livre XII §48 & §51).
                    if (TryExecuteLivreXIManeuver(unit, frame.Target, frame.Posture, frame.Dist, visual))
                    {
                        yield return new WaitForSeconds(_actionDelay);
                        state.Outcome = OverwatchState.IsWatching(unit.Stats)
                            ? AIPlanStepOutcome.EndTurn
                            : AIPlanStepOutcome.ContinueTurn;
                    }
                    break;
                }

                case AIIntent.DuoTechAuto:
                {
                    // 2d. DUO-TECH AUTO (RD-105) : Sillage Igné sans tissage souris.
                    if (TryExecuteDuoTechAuto(unit, frame.Posture, visual))
                    {
                        yield return new WaitForSeconds(_actionDelay);
                        state.Outcome = AIPlanStepOutcome.ContinueTurn;
                    }
                    break;
                }

                case AIIntent.TeepBreak:
                {
                    // 3. CONTACT DIRECT (ranged, dist == 1) : RUPTURE MARTIALE.
                    bool hasMartialArts = unit.Stats.HasSpecialization("Arts Martiaux");
                    DiceType unarmedDie = unit.Stats.GetSkillDie(SkillType.MainsNues, true);

                    if (hasMartialArts && unit.Stats.CanAttack(unarmedDie) && unit.Stats.CurrentActionPoints >= 2)
                    {
                        visual?.SpawnFloatingText("🥋 [TEEP] Rupture d'Engagement", Color.cyan);
                        yield return StartCoroutine(ExecuteTacticalAttack(unit, frame.Target, frame.Posture, isRanged: false, SkillType.MainsNues));
                        yield return new WaitForSeconds(_actionDelay);

                        RefreshFrameForStrike(unit, frame);

                        if (frame.InAttackRange && frame.Dist > 1 && unit.Stats.CurrentActionPoints >= 2)
                        {
                            state.Outcome = AIPlanStepOutcome.ContinueTurn;
                        }
                    }
                    break;
                }

                case AIIntent.DisengageStep:
                {
                    // 3. CONTACT DIRECT (ranged, dist == 1) : DÉGAGEMENT.
                    if (unit.Stats.CurrentActionPoints >= 3)
                    {
                        visual?.SpawnFloatingText("🔄 [DÉGAGEMENT] Recul Tactique", new Color(0.2f, 0.85f, 1.0f));
                        yield return StartCoroutine(ExecuteDisengageStep(unit, frame.Target));
                        yield return new WaitForSeconds(_actionDelay);

                        RefreshFrameForStrike(unit, frame);
                    }
                    break;
                }

                case AIIntent.Grenade:
                {
                    // 4. GRENADES DE ZONE (Livre VIII §31.3)
                    if (TryThrowGrenadeAI(unit, frame.Posture))
                    {
                        float waitElapsed = 0f;
                        while (_arena != null && _arena.IsResolving && waitElapsed < 6f)
                        {
                            yield return null;
                            waitElapsed += Time.deltaTime;
                        }
                        yield return new WaitForSeconds(_actionDelay);
                        state.Outcome = AIPlanStepOutcome.ContinueTurn;
                    }
                    break;
                }

                case AIIntent.BestSpell:
                {
                    // 5. SORTS ARCANOTECH (Livre IV).
                    if (TryCastBestSpell(unit, frame.Target, frame.Dist, frame.HasLOS))
                    {
                        yield return new WaitForSeconds(_actionDelay);
                        state.Outcome = AIPlanStepOutcome.ContinueTurn;
                    }
                    break;
                }

                case AIIntent.Intimidation:
                {
                    // 6. INTIMIDATION & PROVOCATION (Livre III)
                    if (TryExecuteIntimidation(unit, frame.Target, frame.Dist, visual))
                    {
                        yield return new WaitForSeconds(_actionDelay);
                        state.Outcome = AIPlanStepOutcome.ContinueTurn;
                    }
                    break;
                }

                case AIIntent.SelfUpkeep:
                {
                    // 6b. ENTRETIEN & BUFFS PERSONNELS.
                    if (TryExecuteSelfUpkeep(unit, frame.Posture, frame.Dist, frame.InAttackRange, visual))
                    {
                        yield return new WaitForSeconds(_actionDelay);
                        state.Outcome = AIPlanStepOutcome.ContinueTurn;
                    }
                    break;
                }

                case AIIntent.LaserDesignation:
                {
                    // 6c. BALISE LASER : expose la faille des cibles blindées.
                    if (TryExecuteLaserDesignation(unit, frame.Target, frame.Dist, visual))
                    {
                        yield return new WaitForSeconds(_actionDelay);
                        state.Outcome = AIPlanStepOutcome.ContinueTurn;
                    }
                    break;
                }

                case AIIntent.ChargeAttack:
                {
                    // 6d. CHARGE AU CONTACT (RD-038, Livre V).
                    if (TryExecuteChargeAttack(unit, frame.Target, visual))
                    {
                        float waitElapsed = 0f;
                        while (_arena != null && _arena.IsResolving && waitElapsed < 8f)
                        {
                            yield return null;
                            waitElapsed += Time.deltaTime;
                        }
                        yield return new WaitForSeconds(_actionDelay);
                        state.Outcome = AIPlanStepOutcome.ContinueTurn;
                    }
                    break;
                }

                case AIIntent.TitanSlam:
                {
                    // 6e. FRAPPE TITANESQUE (RD-052).
                    if (TryExecuteTitanSlam(unit, frame.Target, frame.Posture, frame.Dist, visual))
                    {
                        yield return new WaitForSeconds(_actionDelay);
                        state.Outcome = AIPlanStepOutcome.ContinueTurn;
                    }
                    break;
                }

                case AIIntent.Shove:
                {
                    // 6f. BOUSCULADE / COUP DE BOUCLIER (2 PA, contact).
                    if (TryExecuteShove(unit, frame.Target, frame.TargetCover, visual))
                    {
                        yield return new WaitForSeconds(_actionDelay);
                        state.Outcome = AIPlanStepOutcome.ContinueTurn;
                    }
                    break;
                }

                case AIIntent.WeaponThrow:
                {
                    // 6g. LANCER D'ARME (2 PA, 2-4 cases).
                    if (TryExecuteWeaponThrow(unit, frame.Target, frame.Dist, visual))
                    {
                        float waitElapsed2 = 0f;
                        while (_arena != null && _arena.IsResolving && waitElapsed2 < 6f)
                        {
                            yield return null;
                            waitElapsed2 += Time.deltaTime;
                        }
                        yield return new WaitForSeconds(_actionDelay);
                        state.Outcome = AIPlanStepOutcome.ContinueTurn;
                    }
                    break;
                }

                case AIIntent.FlankToCover:
                {
                    // 7. MANŒUVRE DE FLANKING & PRISE DE COUVERT AVANT ATTAQUE (Livre V §21.3)
                    if (unit.Stats.CurrentActionPoints >= 3
                        && (frame.TargetCover == CoverType.ThreeQuarters || !frame.InAttackRange))
                    {
                        int reserve = GetRequiredDefensiveReserve(unit, frame.Posture);
                        int moveBudget = Mathf.Max(1, unit.Stats.CurrentActionPoints - 2 - reserve);

                        var optimalCombatHex = FindOptimalCombatHex(unit, frame.Target, frame.IsRanged, frame.MaxRange, moveBudget);
                        if (optimalCombatHex.HasValue && !optimalCombatHex.Value.Equals(unit.CurrentCoords))
                        {
                            var path = _pathfinder.FindPath(unit.CurrentCoords, optimalCombatHex.Value, moveBudget, out int cost);
                            if (path != null && path.Count > 1)
                            {
                                visual?.SpawnFloatingText(frame.IsRanged ? $"🎯 Débordement (-{cost} PA)" : $"⚡ Percée (-{cost} PA)", new Color(0.2f, 0.85f, 1.0f));
                                EnsureSafeDisengageForMove(unit, path.Count - 1);
                                yield return StartCoroutine(unit.MoveAlongPath(path, _grid, cost));
                                yield return new WaitForSeconds(_actionDelay);
                                state.Outcome = AIPlanStepOutcome.ContinueTurn;
                            }
                        }
                    }
                    break;
                }

                case AIIntent.PredictiveAttack:
                {
                    // 8. ATTAQUE PRÉDICTIVE (DUEL AVEUGLE CODEX)
                    if (frame.InAttackRange)
                    {
                        DiceType attackDie = unit.Stats.GetSkillDie(frame.AttackSkill, true);

                        if (unit.Stats.CanAttack(attackDie) && unit.Stats.CurrentActionPoints >= 2)
                        {
                            yield return StartCoroutine(ExecuteTacticalAttack(unit, frame.Target, frame.Posture, frame.IsRanged, frame.AttackSkill));

                            if (frame.Posture == TacticalPosture.HitAndRun && unit.Stats.CurrentActionPoints >= 1)
                            {
                                visual?.SpawnFloatingText("💨 [RETRAIT] Décrochage", new Color(0.9f, 0.7f, 0.2f));
                                yield return StartCoroutine(ExecuteDisengageStep(unit, frame.Target));
                                state.Outcome = AIPlanStepOutcome.EndTurn;
                                break;
                            }

                            yield return new WaitForSeconds(_actionDelay);
                            state.Outcome = AIPlanStepOutcome.ContinueTurn;
                        }
                    }
                    break;
                }

                case AIIntent.EmergencyBreath:
                {
                    // 8b. SOUFFLE D'ATTAQUE (+2 PA, +1 ESS) quand l'attaque
                    // manque de peu (verbatim : CanAttack mais PA < 2).
                    if (frame.InAttackRange)
                    {
                        DiceType attackDie = unit.Stats.GetSkillDie(frame.AttackSkill, true);

                        if (unit.Stats.CanAttack(attackDie) && unit.Stats.CurrentActionPoints < 2
                            && !frame.TookBreathThisTurn && CanTakeBreathSafely(unit))
                        {
                            frame.TookBreathThisTurn = true;
                            unit.Stats.TakeEmergencyBreath(2);
                            visual?.SpawnFloatingText("🫁 Souffle d'Attaque (+2 PA, +1 ESS)", Color.yellow);
                            yield return new WaitForSeconds(_actionDelay);
                            state.Outcome = AIPlanStepOutcome.ContinueTurn;
                        }
                    }
                    break;
                }

                case AIIntent.EndTurn_NoAttackOption:
                {
                    state.Outcome = AIPlanStepOutcome.EndTurn;
                    break;
                }

                case AIIntent.AdvanceToContact:
                {
                    // 9. APPROCHE VERS LA LIGNE DE TIR OU ENGAGEMENT
                    int availableAP = unit.Stats.CurrentActionPoints;
                    if (availableAP >= 1)
                    {
                        int reserve = GetRequiredDefensiveReserve(unit, frame.Posture);
                        int moveBudget = Mathf.Max(1, availableAP - reserve);

                        var pathToFiringPos = frame.IsRanged
                            ? FindPathTowardsRangedPosition(unit, frame.Target, frame.MaxRange, moveBudget)
                            : FindPathTowardsTarget(unit, frame.Target, moveBudget);

                        if (pathToFiringPos != null && pathToFiringPos.Count > 1)
                        {
                            int steps = pathToFiringPos.Count - 1;
                            string moveMsg = frame.IsRanged ? $"Position de tir (-{steps} PA)" : $"Avance tactique (-{steps} PA)";
                            visual?.SpawnFloatingText(moveMsg, new Color(0.2f, 0.85f, 1.0f));
                            EnsureSafeDisengageForMove(unit, steps);
                            yield return StartCoroutine(unit.MoveAlongPath(pathToFiringPos, _grid, steps));
                            yield return new WaitForSeconds(_actionDelay);
                            state.Outcome = AIPlanStepOutcome.ContinueTurn;
                        }
                    }
                    break;
                }

                case AIIntent.TacticalOrder:
                {
                    // 10. ORDRE TACTIQUE EN FIN DE TOUR (Livre III, fallthrough).
                    if (unit.Stats.CurrentActionPoints >= 2 && TryDelegateTacticalOrder(unit, visual))
                    {
                        yield return new WaitForSeconds(_actionDelay);
                    }
                    break;
                }

                case AIIntent.Overwatch:
                {
                    // 10b. GUET / OVERWATCH (RD-030) du surplus, puis break.
                    if (frame.IsRanged && _arena != null
                        && !OverwatchState.IsWatching(unit.Stats)
                        && unit.Stats.CurrentActionPoints >= CoreRulesConfig.Instance.OverwatchAPCost + GetRequiredDefensiveReserve(unit, frame.Posture))
                    {
                        if (_arena.TryEnterOverwatch(unit, out _))
                        {
                            yield return new WaitForSeconds(_actionDelay);
                        }
                    }
                    state.Outcome = AIPlanStepOutcome.EndTurn;
                    break;
                }

                default:
                {
                    state.Outcome = AIPlanStepOutcome.EndTurn;
                    break;
                }
            }
        }
    }
}

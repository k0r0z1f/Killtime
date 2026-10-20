using System;
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
    public partial class TacticalAIController : MonoBehaviour
    {
        private HexCoordinates? FindOptimalCombatHex(TacticalUnit actor, TacticalUnit target, bool isRanged, int maxRange, int moveBudget)
        {
            if (_grid == null || _pathfinder == null || moveBudget <= 0) return null;

            var reachable = _pathfinder.GetReachableCoordinates(actor.CurrentCoords, moveBudget);
            if (reachable == null || reachable.Count == 0) return null;

            // Pré-filtrage unique des alliés en ligne de tir (élimine les milliers de raycasts redondants)
            List<TacticalUnit> firingAllies = null;
            if (isRanged)
            {
                for (int i = 0; i < _cachedUnits.Count; i++)
                {
                    var ally = _cachedUnits[i];
                    if (ally == null || ally == actor || ally.Stats == null || !ally.Stats.IsAlive) continue;
                    if (ally.IsPlayerControlled != actor.IsPlayerControlled) continue;
                    int allyDist = TitanFootprint.MinDistanceBetweenUnits(ally.CurrentCoords, ally.FootprintType, target.CurrentCoords, target.FootprintType);
                    if (allyDist < 2 || allyDist > 10) continue;
                    if (GetCoverLevel(ally.CurrentCoords, target.CurrentCoords, target.FootprintType, ally.FootprintType, ally) == CoverType.Full) continue;

                    firingAllies ??= new List<TacticalUnit>();
                    firingAllies.Add(ally);
                }
            }

            HexCoordinates? bestHex = null;
            float highestScore = float.MinValue;

            foreach (var hex in reachable)
            {
                var node = _grid.GetNode(hex);
                if (node == null || !node.IsWalkable || (node.IsOccupied && !hex.Equals(actor.CurrentCoords))) continue;

                // RD-IA09 : distance d'empreinte (les Titans faussaient le filtre).
                // Pré-filtre de portée AVANT le scan de couvert (28 rayons) :
                // ordre et coût identiques à l'original.
                int dist = TitanFootprint.MinDistanceBetweenUnits(hex, actor.FootprintType, target.CurrentCoords, target.FootprintType);
                if (!AIMovementSolver.IsRangeEligible(isRanged, dist, maxRange)) continue;

                // RD-IA09 : couvert + fumée réels d'empreinte (avant : centre à
                // centre sans fumée, incohérent avec HasLineOfSight).
                CoverType targetCoverFromHex = GetCoverLevel(hex, target.CurrentCoords, target.FootprintType, actor.FootprintType, actor);
                if (targetCoverFromHex == CoverType.Full) continue;

                // Couvert défensif : protection contre la riposte
                CoverType defCoverAgainstTarget = GetCoverLevel(target.CurrentCoords, hex, actor.FootprintType, target.FootprintType, actor);

                // Coordination d'Escouade (Livre V §21.3) : Verrouillage en Tir Croisé optimisé
                float crossfire = 0f;
                if (isRanged && firingAllies != null)
                {
                    crossfire = EvaluatePrecomputedCrossfireBonus(target, hex, firingAllies);
                }

                // Évaluation du coût sans instancier d'A* redondant dans la boucle
                float score = AIMovementSolver.ScoreCombatHex(
                    targetCoverFromHex, defCoverAgainstTarget, isRanged, dist,
                    crossfire, actor.CurrentCoords.DistanceTo(hex) * 3.5f, _defaultPersonality);

                if (score > highestScore)
                {
                    highestScore = score;
                    bestHex = hex;
                }
            }

            return bestHex;
        }

        // =========================================================================
        // RD-053 : PATROUILLE ALERTÉE & CONVERGENCE VERS LE BRUIT / LUMIÈRE
        // RD-115 : lectures via IAIPerception, décision dans le pur
        // AIPerceptionDecisions.FindPatrolTarget (ordre et raisons inchangés).
        // =========================================================================
        private bool HasAlertedPatrolTarget(TacticalUnit actor, out HexCoordinates suspiciousTarget, out string patrolReason)
        {
            suspiciousTarget = default;
            patrolReason = "Bruit suspect";

            if (actor?.Stats == null || !actor.Stats.IsAlive || actor.Stats.CurrentActionPoints <= 0) return false;
            if (_grid == null || _pathfinder == null) return false;

            int currentRound = _turnManager != null ? _turnManager.CurrentRound : 1;
            var perception = Perception;

            var enemies = new List<AIPatrolEnemy>(_cachedUnits.Count);
            for (int i = 0; i < _cachedUnits.Count; i++)
            {
                var enemy = _cachedUnits[i];
                if (enemy == null || enemy == actor || enemy.Stats == null || !enemy.Stats.IsAlive) continue;
                string enemyName = enemy.gameObject != null ? enemy.gameObject.name : null;
                enemies.Add(new AIPatrolEnemy
                {
                    IsSelf = false,
                    IsAlive = true,
                    IsPlayerControlled = enemy.IsPlayerControlled,
                    CenterDistance = actor.CurrentCoords.DistanceTo(enemy.CurrentCoords),
                    Coords = enemy.CurrentCoords,
                    HasLamp = enemy.HasFlashlight,
                    NoiseActive = perception.IsNoiseActive(enemyName),
                    LastKnownCoords = perception.GetLastKnownPosition(enemyName),
                });
            }

            var patrolInput = new AIPatrolInput
            {
                ActorAlive = true,
                ActorActionPoints = actor.Stats.CurrentActionPoints,
                SensorsReady = true,
                ActorCoords = actor.CurrentCoords,
                ActorIsPlayer = actor.IsPlayerControlled,
                FullAutoMode = _mode == CombatAIMode.FullAuto,
                CurrentRound = currentRound,
                SquadNoise = new AIPatrolSquadNoise
                {
                    HasValue = _squadLastHeardNoiseCoords.HasValue,
                    Coords = _squadLastHeardNoiseCoords ?? default,
                    AlertRound = _squadNoiseAlertRound,
                    FromPlayer = _squadNoiseFromPlayer,
                },
                IsNight = perception.IsNight,
                Enemies = enemies,
            };
            var result = AIPerceptionDecisions.FindPatrolTarget(in patrolInput);
            if (result.ClearStaleSquadNoise)
                _squadLastHeardNoiseCoords = null;
            if (!result.HasTarget)
                return false;
            suspiciousTarget = result.Target;
            patrolReason = result.Reason;
            return true;
        }

        private IEnumerator ExecuteAlertedPatrol(TacticalUnit actor, HexCoordinates targetCoords, string reason, TacticalUnitVisual visual)
        {
            int reserve = GetRequiredDefensiveReserve(actor, TacticalPosture.AlertedPatrol);
            int moveBudget = Mathf.Max(1, actor.Stats.CurrentActionPoints - reserve);

            var path = FindPathTowardsCoords(actor.CurrentCoords, targetCoords, moveBudget, out int rawCost);
            if (path != null && path.Count > 1)
            {
                int apCost = actor.ComputeMovementAPCost(rawCost);
                visual?.SpawnFloatingText($"🚨 [PATROUILLE] {reason} (-{apCost} PA)", new Color(1f, 0.55f, 0.1f));
                _arena?.Log($"🚨 <b>{actor.Stats.Name}</b> passe en <b>Patrouille Alertée</b> ({reason}) ➔ convergence vers {targetCoords}.");
                yield return StartCoroutine(actor.MoveAlongPath(path, _grid, apCost));
            }
        }

        private List<HexCoordinates> FindPathTowardsCoords(HexCoordinates start, HexCoordinates destination, int apBudget, out int totalRawCost)
        {
            totalRawCost = 0;
            if (_pathfinder == null || _grid == null || apBudget <= 0) return null;

            HexCoordinates targetNodeCoords = destination;
            var destNode = _grid.GetNode(destination);
            if (destNode == null || !destNode.IsWalkable || destNode.IsOccupied)
            {
                int minDistance = int.MaxValue;
                for (int dir = 0; dir < 6; dir++)
                {
                    var n = destination.GetNeighbor(dir);
                    var nNode = _grid.GetNode(n);
                    if (nNode != null && nNode.IsWalkable && !nNode.IsOccupied)
                    {
                        int d = start.DistanceTo(n);
                        if (d < minDistance)
                        {
                            minDistance = d;
                            targetNodeCoords = n;
                        }
                    }
                }
            }

            var fullPath = _pathfinder.FindPath(start, targetNodeCoords, 50, out _);
            if (fullPath == null || fullPath.Count <= 1) return null;

            var truncatedPath = AIMovementSolver.TruncatePathByBudget(
                fullPath,
                apBudget,
                step => { var n = _grid.GetNode(step); return n != null ? n.ActionPointCost : 1; },
                step => { var n = _grid.GetNode(step); return n != null && (n.IsOccupied || !n.IsWalkable); },
                fullPath.Count - 1,
                out int spent);
            totalRawCost = spent;
            return truncatedPath;
        }

        private IEnumerator ExecuteCoveredRetreat(TacticalUnit actor, TacticalUnit threat)
        {
            if (CanTakeBreathSafely(actor) && actor.Stats.CurrentActionPoints < 3)
            {
                actor.Stats.TakeEmergencyBreath(2);
                var visual = actor.GetComponent<TacticalUnitVisual>();
                visual?.SpawnFloatingText("🫁 Souffle de Rupture (+2 PA)", Color.red);
                yield return new WaitForSeconds(_actionDelay);
            }

            int ap = actor.Stats.CurrentActionPoints;
            var retreatHex = FindBestCoveredRetreatHex(actor.CurrentCoords, threat.CurrentCoords, ap);

            if (retreatHex.HasValue && !retreatHex.Value.Equals(actor.CurrentCoords))
            {
                var path = _pathfinder.FindPath(actor.CurrentCoords, retreatHex.Value, ap, out int cost);
                if (path != null && path.Count > 1)
                {
                    yield return StartCoroutine(actor.MoveAlongPath(path, _grid, cost));
                }
            }
        }

        // RD-047 : Fuite panique désordonnée pour unité en Déroute (Agonisant)
        private IEnumerator ExecutePanickedFlee(TacticalUnit actor)
        {
            if (CanTakeBreathSafely(actor) && actor.Stats.CurrentActionPoints < 3)
            {
                actor.Stats.TakeEmergencyBreath(2);
                var visual = actor.GetComponent<TacticalUnitVisual>();
                visual?.SpawnFloatingText("🫁 Souffle de Panique (+2 PA)", Color.red);
                yield return new WaitForSeconds(_actionDelay);
            }

            int ap = actor.Stats.CurrentActionPoints;
            if (ap <= 0 || _pathfinder == null || _grid == null) yield break;

            TacticalUnit nearestThreat = null;
            int minThreatDist = int.MaxValue;
            for (int i = 0; i < _cachedUnits.Count; i++)
            {
                var u = _cachedUnits[i];
                if (u == null || u == actor || u.Stats == null || !u.Stats.IsAlive || u.Stats.IsSurrendered) continue;
                bool hostile = actor.IsPlayerControlled ? !u.IsPlayerControlled : u.IsPlayerControlled;
                if (_mode == CombatAIMode.FullAuto && u.IsPlayerControlled != actor.IsPlayerControlled) hostile = true;
                if (!hostile) continue;
                int d = actor.CurrentCoords.DistanceTo(u.CurrentCoords);
                if (d < minThreatDist)
                {
                    minThreatDist = d;
                    nearestThreat = u;
                }
            }

            HexCoordinates? fleeHex = nearestThreat != null
                ? FindBestPanickedFleeHex(actor.CurrentCoords, nearestThreat.CurrentCoords, ap)
                : null;

            if (fleeHex.HasValue && !fleeHex.Value.Equals(actor.CurrentCoords))
            {
                var path = _pathfinder.FindPath(actor.CurrentCoords, fleeHex.Value, ap, out int cost);
                if (path != null && path.Count > 1)
                {
                    actor.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("💨 Fuite éperdue !", new Color(1f, 0.4f, 0.2f));
                    yield return StartCoroutine(actor.MoveAlongPath(path, _grid, cost));
                }
            }
        }

        private HexCoordinates? FindBestPanickedFleeHex(HexCoordinates current, HexCoordinates threatCoords, int apBudget)
        {
            if (_pathfinder == null) return null;
            var reachable = _pathfinder.GetReachableCoordinates(current, apBudget);
            if (reachable == null || reachable.Count == 0) return null;

            HexCoordinates? bestHex = null;
            float highestScore = float.MinValue;

            foreach (var hex in reachable)
            {
                var node = _grid.GetNode(hex);
                if (node == null || !node.IsWalkable || (node.IsOccupied && !hex.Equals(current))) continue;

                int dist = hex.DistanceTo(threatCoords);
                // Jitter injecté : l'original tirait UnityEngine.Random dans la boucle
                // (non déterministe). Ici on tire une fois par candidat, même distribution.
                float score = AIMovementSolver.ScoreFleeHex(dist, CoverSystem.EvaluateCover(threatCoords, hex, _grid), UnityEngine.Random.Range(0f, 4f));

                if (score > highestScore)
                {
                    highestScore = score;
                    bestHex = hex;
                }
            }

            return bestHex;
        }

        private HexCoordinates? FindBestCoveredRetreatHex(HexCoordinates current, HexCoordinates threatCoords, int apBudget)
        {
            if (_pathfinder == null) return null;
            var reachable = _pathfinder.GetReachableCoordinates(current, apBudget);
            if (reachable == null || reachable.Count == 0) return null;

            HexCoordinates? bestHex = null;
            float highestScore = float.MinValue;

            foreach (var hex in reachable)
            {
                var node = _grid.GetNode(hex);
                if (node == null || !node.IsWalkable || (node.IsOccupied && !hex.Equals(current))) continue;

                int dist = hex.DistanceTo(threatCoords);
                CoverType coverFromThreat = CoverSystem.EvaluateCover(threatCoords, hex, _grid);

                float score = AIMovementSolver.ScoreRetreatHex(dist, coverFromThreat);

                if (score > highestScore)
                {
                    highestScore = score;
                    bestHex = hex;
                }
            }

            return bestHex;
        }

        private IEnumerator ExecuteDisengageStep(TacticalUnit actor, TacticalUnit threat)
        {
            int ap = actor.Stats.CurrentActionPoints;
            if (ap <= 0) yield break;

            // RD-031 : le retrait rompt le contact et provoque (sauf spé esquive).
            // Quand les PA le permettent, sécurise le pas par un Décrochage payé
            // (reste ≥ 1 PA pour marcher après).
            if (actor.Stats != null
                && !OpportunityState.IsExemptFromProvoking(actor.Stats)
                && !OpportunityState.HasSafeDisengage(actor.Stats))
            {
                int disCost = CoreRulesConfig.Instance.DisengageAPCost;
                if (ap >= disCost + 1
                    && OpportunityState.RequestSafeDisengage(actor.Stats, disCost, out _))
                {
                    actor.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("💨 Décrochage IA", new Color(0.4f, 0.9f, 1.0f));
                }
            }

            var retreatHex = FindBestCoveredRetreatHex(actor.CurrentCoords, threat.CurrentCoords, 1);
            if (retreatHex.HasValue && !retreatHex.Value.Equals(actor.CurrentCoords))
            {
                var path = _pathfinder.FindPath(actor.CurrentCoords, retreatHex.Value, 1, out int cost);
                if (path != null && path.Count > 1)
                {
                    yield return StartCoroutine(actor.MoveAlongPath(path, _grid, cost));
                }
            }
        }

        /// <summary>
        /// RD-031 : choisit la posture d'opportunité de l'IA pour ce round.
        /// Grappler (Athlétisme) → Blocage ; rapide (RAP 5+) → Poursuite ;
        /// esquiveur → Balayage ; défaut → Frappe gratuite.
        /// </summary>
        private OpportunityReactionType ChooseOpportunityStance(TacticalUnit unit)
        {
            if (unit == null || unit.Stats == null) return OpportunityReactionType.Strike;
            bool hasAthletisme = unit.Sheet != null && unit.Sheet.GetSkill(SkillType.Athletisme).TrainingLevel >= 1;
            bool hasEsquive = unit.Sheet != null && unit.Sheet.GetSkill(SkillType.Esquive).TrainingLevel >= 1;
            return AIPersonalityProfile.ChooseOpportunityStance(hasAthletisme, unit.Stats.Attributes.Rapidite, hasEsquive);
        }

        // =========================================================================
        // ATTAQUE PRÉDICTIVE & INJECTION OPTIMISÉE DES MISES
        // (struct TacticalAttackPlan + optimisation : voir AttackPlanEvaluator)
        // =========================================================================

    }
}

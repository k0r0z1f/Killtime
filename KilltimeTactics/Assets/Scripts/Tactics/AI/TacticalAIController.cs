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
using Killtime.CameraSystem;

namespace Killtime.Tactics.AI
{
    public enum CombatAIMode
    {
        Normal,   // IA uniquement sur les ennemis/PNJ
        FullAuto  // IA sur l'intégralité des unités
    }

    public enum AIPersonality
    {
        Balanced,   // Équilibre attaque soutenue, réserve minimale (1 PA) et survie
        Aggressive, // Dépense 100% de ses PA, injection maximale, aucune réserve
        Tactician,  // Privilégie la visée des membres, 1 PA de réserve, sorts et hit-and-run
        Survivor    // Repli si PV critiques, sinon attrition
    }

    public enum TacticalPosture
    {
        AllInLethal,      // Opportunité d'élimination : dépense et injection maximales
        CalculatedStrike, // Attaque engagée avec injection des PA excédentaires
        HitAndRun,        // Frappe puis décrochage d'une case
        TacticalRetreat   // Danger mortel : repli vers la distance maximale
    }

    /// <summary>
    /// Contrôleur d'IA Tactique Militaire.
    /// Exploite activement l'économie des PA en sachant qu'ils sont réinitialisés à chaque tour.
    /// </summary>
    public class TacticalAIController : MonoBehaviour
    {
        [Header("Systèmes")]
        [SerializeField] private TurnManager _turnManager;
        [SerializeField] private TacticalHexGrid _grid;
        [SerializeField] private CombatDevArena _arena;
        [SerializeField] private CinematicDirector _cinematicDirector;

        [Header("Paramètres de Doctrine")]
        [SerializeField] private CombatAIMode _mode = CombatAIMode.Normal;
        [SerializeField] private AIPersonality _defaultPersonality = AIPersonality.Balanced;
        [SerializeField] private bool _isAIEnabled = true;
        [SerializeField] [Range(0.05f, 6.0f)] private float _actionDelay = 0.8f;

        [Header("Paramètres de Survie & Économie")]
        [SerializeField] [Range(0.15f, 0.4f)] private float _retreatHealthRatio = 0.25f;
        [SerializeField] [Range(0, 2)] private int _baseDefensiveAPReserve = 1;

        public CombatAIMode Mode { get => _mode; set => _mode = value; }
        public bool IsAIEnabled { get => _isAIEnabled; set => _isAIEnabled = value; }
        public float ActionDelay { get => _actionDelay; set => _actionDelay = Mathf.Clamp(value, 0.05f, 10.0f); }

        private HexPathfinder _pathfinder;
        private Coroutine _activeTurnRoutine;
        private readonly List<TacticalUnit> _cachedUnits = new();

        private void Awake() => EnsureDependencies();

        private void Start()
        {
            EnsureDependencies();
            if (_grid != null) _pathfinder = new HexPathfinder(_grid);

            if (_turnManager != null)
            {
                _turnManager.OnTurnStarted += HandleTurnStarted;
                _turnManager.OnCombatEnded += HandleCombatEnded;
            }
        }

        private void OnDestroy()
        {
            if (_turnManager != null)
            {
                _turnManager.OnTurnStarted -= HandleTurnStarted;
                _turnManager.OnCombatEnded -= HandleCombatEnded;
            }
            StopAITurn();
        }

        private void HandleCombatEnded(CombatOutcome outcome) => StopAITurn();

        private void EnsureDependencies()
        {
            if (_turnManager == null) _turnManager = GetComponent<TurnManager>() ?? FindAnyObjectByType<TurnManager>();
            if (_grid == null) _grid = GetComponent<TacticalHexGrid>() ?? FindAnyObjectByType<TacticalHexGrid>();
            if (_arena == null) _arena = GetComponent<CombatDevArena>() ?? FindAnyObjectByType<CombatDevArena>();
            if (_cinematicDirector == null) _cinematicDirector = GetComponent<CinematicDirector>() ?? FindAnyObjectByType<CinematicDirector>();
        }

        public void StopAITurn()
        {
            if (_activeTurnRoutine != null)
            {
                StopCoroutine(_activeTurnRoutine);
                _activeTurnRoutine = null;
            }
        }

        private void HandleTurnStarted(TacticalUnit unit)
        {
            StopAITurn();

            if (_turnManager != null && _turnManager.IsCombatOver) return;
            if (!_isAIEnabled || unit == null || unit.Stats == null) return;

            if (!unit.Stats.IsAlive)
            {
                _turnManager?.EndCurrentTurn();
                return;
            }

            bool shouldControl = (_mode == CombatAIMode.FullAuto) || (!unit.IsPlayerControlled);
            if (shouldControl)
            {
                _activeTurnRoutine = StartCoroutine(ExecuteAITurn(unit));
            }
        }

        // =========================================================================
        // BOUCLE DÉCISIONNELLE STRATÉGIQUE (AI BRAIN)
        // =========================================================================
        private IEnumerator ExecuteAITurn(TacticalUnit unit)
        {
            yield return new WaitForSeconds(_actionDelay);

            if (_pathfinder == null && _grid != null) _pathfinder = new HexPathfinder(_grid);
            UpdateUnitCache();

            var visual = unit.GetComponent<TacticalUnitVisual>();
            int loopGuard = 0;
            const int maxSteps = 8;

            while (unit.Stats.IsAlive && loopGuard++ < maxSteps && unit.Stats.CurrentActionPoints > 0)
            {
                var target = EvaluateBestTarget(unit);
                if (target == null) break;

                int dist = unit.CurrentCoords.DistanceTo(target.CurrentCoords);
                var posture = EvaluateTacticalPosture(unit, target, dist);

                // -------------------------------------------------------------
                // 1. RETRAITE CRITIQUE (Danger mortel)
                // -------------------------------------------------------------
                if (posture == TacticalPosture.TacticalRetreat)
                {
                    visual?.SpawnFloatingText("[REPLI] Manoeuvre d'Urgence", new Color(1.0f, 0.3f, 0.3f));
                    yield return StartCoroutine(ExecuteRetreat(unit, target));
                    yield return new WaitForSeconds(_actionDelay);
                    break;
                }

                // -------------------------------------------------------------
                // 2. SORTS ARCANOTECH À DISTANCE (Livre IV)
                // -------------------------------------------------------------
                if (dist > 1 && TryCastBestSpell(unit, target))
                {
                    yield return new WaitForSeconds(_actionDelay);
                    continue;
                }

                // -------------------------------------------------------------
                // 3. COMBAT AU CONTACT (Distance == 1)
                // -------------------------------------------------------------
                if (dist == 1)
                {
                    SkillType attackSkill = unit.Stats.GetBestMeleeAttackSkill();
                    DiceType attackDie = unit.Stats.GetSkillDie(attackSkill, true);

                    if (unit.Stats.CanAttack(attackDie) && unit.Stats.CurrentActionPoints >= 2)
                    {
                        yield return StartCoroutine(ExecuteTacticalAttack(unit, target, posture));

                        if (posture == TacticalPosture.HitAndRun && unit.Stats.CurrentActionPoints >= 1)
                        {
                            visual?.SpawnFloatingText("[RETRAIT] Décrochage", new Color(0.9f, 0.7f, 0.2f));
                            yield return StartCoroutine(ExecuteDisengageStep(unit, target));
                            break;
                        }

                        yield return new WaitForSeconds(_actionDelay);
                        continue;
                    }
                    else if (unit.Stats.CanAttack(attackDie) && unit.Stats.CurrentActionPoints < 2 && CanTakeBreathSafely(unit))
                    {
                        unit.Stats.TakeEmergencyBreath(2);
                        visual?.SpawnFloatingText("🫁 Souffle d'Attaque (+2 PA)", Color.yellow);
                        yield return new WaitForSeconds(_actionDelay);
                        continue;
                    }

                    break;
                }

                // -------------------------------------------------------------
                // 4. APPROCHE VERS LA CIBLE (Distance > 1)
                // -------------------------------------------------------------
                int availableAP = unit.Stats.CurrentActionPoints;
                if (availableAP >= 1)
                {
                    int reserve = GetRequiredDefensiveReserve(unit, posture);
                    int moveBudget = Mathf.Max(1, availableAP - reserve);

                    var pathToTarget = FindPathTowardsTarget(unit, target, moveBudget);
                    if (pathToTarget != null && pathToTarget.Count > 1)
                    {
                        int steps = pathToTarget.Count - 1;
                        visual?.SpawnFloatingText($"Avance tactique (-{steps} PA)", new Color(0.2f, 0.85f, 1.0f));
                        yield return StartCoroutine(unit.MoveAlongPath(pathToTarget, _grid, steps));
                        yield return new WaitForSeconds(_actionDelay);
                        continue;
                    }
                }

                break;
            }

            yield return new WaitForSeconds(Mathf.Min(0.4f, _actionDelay * 0.25f));
            _activeTurnRoutine = null;
            _turnManager?.EndCurrentTurn();
        }

        // =========================================================================
        // ÉVALUATION DE LA POSTURE TACTIQUE
        // =========================================================================
        private TacticalPosture EvaluateTacticalPosture(TacticalUnit actor, TacticalUnit target, int dist)
        {
            float hpRatio = (float)actor.Stats.CurrentHealth / Mathf.Max(1, actor.Stats.MaxHealth);
            bool isMortalDanger = hpRatio <= _retreatHealthRatio;

            if (isMortalDanger && !CanKillTargetThisTurn(actor, target))
            {
                return TacticalPosture.TacticalRetreat;
            }

            if (CanKillTargetThisTurn(actor, target))
            {
                return TacticalPosture.AllInLethal;
            }

            if ((_defaultPersonality == AIPersonality.Tactician || _defaultPersonality == AIPersonality.Survivor)
                && dist == 1 && actor.Stats.CurrentActionPoints >= 4)
            {
                return TacticalPosture.HitAndRun;
            }

            return TacticalPosture.CalculatedStrike;
        }

        private int GetRequiredDefensiveReserve(TacticalUnit actor, TacticalPosture posture)
        {
            if (posture == TacticalPosture.AllInLethal) return 0;
            if (_defaultPersonality == AIPersonality.Aggressive) return 0;

            // 1 PA suffit pour réagir (BaseReactionAPCost). Tout garder au-delà est un gâchis net.
            return Mathf.Clamp(_baseDefensiveAPReserve, 0, 1);
        }

        private bool CanKillTargetThisTurn(TacticalUnit actor, TacticalUnit target)
        {
            int maxPotentialAP = actor.Stats.CurrentActionPoints;
            if (CanTakeBreathSafely(actor)) maxPotentialAP += 2;

            int dist = actor.CurrentCoords.DistanceTo(target.CurrentCoords);
            int apToReach = (dist > 1) ? (dist - 1) : 0;
            int apForAttacks = maxPotentialAP - apToReach;

            if (apForAttacks < 2) return false;

            int targetHp = target.Stats.CurrentHealth;
            int estimatedDmg = Mathf.Max(4, (7 - target.Stats.BaseArmorAbsorption) * 2);

            return targetHp <= estimatedDmg;
        }

        private bool CanTakeBreathSafely(TacticalUnit unit)
        {
            return unit.Stats.Essoufflement < unit.Stats.Attributes.Constitution;
        }

        // =========================================================================
        // SORTS ARCANOTECH (LIVRE IV)
        // =========================================================================
        private bool TryCastBestSpell(TacticalUnit actor, TacticalUnit target)
        {
            if (actor.Sheet == null || actor.Sheet.LearnedSpells == null || actor.Sheet.LearnedSpells.Count == 0) return false;
            int dist = actor.CurrentCoords.DistanceTo(target.CurrentCoords);

            NythariteSpell bestSpell = null;
            int highestDmg = -1;

            for (int i = 0; i < actor.Sheet.LearnedSpells.Count; i++)
            {
                var spell = actor.Sheet.LearnedSpells[i];
                if (spell == null) continue;
                if (spell.RangeInTiles >= dist && actor.Stats.CurrentActionPoints >= spell.ActionPointCost)
                {
                    if (spell.BaseArcaneDamage > highestDmg)
                    {
                        highestDmg = spell.BaseArcaneDamage;
                        bestSpell = spell;
                    }
                }
            }

            if (bestSpell != null)
            {
                var dice = new DiceRoller();
                var vis = target.GetComponent<TacticalUnitVisual>();
                var actorVis = actor.GetComponent<TacticalUnitVisual>();
                actorVis?.SpawnFloatingText($"🔮 {bestSpell.Name} (-{bestSpell.ActionPointCost} PA)", Color.cyan);

                if (bestSpell.Cast(actor.Stats, target.Stats, dice, out string log))
                {
                    vis?.TriggerHitFlash();
                    vis?.SpawnFloatingText($"-{bestSpell.BaseArcaneDamage} Arcanique", Color.magenta);
                }

                return true;
            }

            return false;
        }

        // =========================================================================
        // RETRAITE STRATÉGIQUE & DÉCROCHAGE
        // =========================================================================
        private IEnumerator ExecuteRetreat(TacticalUnit actor, TacticalUnit nearestThreat)
        {
            if (CanTakeBreathSafely(actor) && actor.Stats.CurrentActionPoints < 3)
            {
                actor.Stats.TakeEmergencyBreath(2);
                var visual = actor.GetComponent<TacticalUnitVisual>();
                visual?.SpawnFloatingText("🫁 Sprint de Survie (+2 PA)", Color.red);
                yield return new WaitForSeconds(_actionDelay);
            }

            int ap = actor.Stats.CurrentActionPoints;
            var retreatHex = FindBestRetreatHex(actor.CurrentCoords, nearestThreat.CurrentCoords, ap);

            if (retreatHex.HasValue && !retreatHex.Value.Equals(actor.CurrentCoords))
            {
                var path = _pathfinder.FindPath(actor.CurrentCoords, retreatHex.Value, ap, out int cost);
                if (path != null && path.Count > 1)
                {
                    yield return StartCoroutine(actor.MoveAlongPath(path, _grid, cost));
                }
            }
        }

        private IEnumerator ExecuteDisengageStep(TacticalUnit actor, TacticalUnit threat)
        {
            int ap = actor.Stats.CurrentActionPoints;
            if (ap <= 0) yield break;

            var retreatHex = FindBestRetreatHex(actor.CurrentCoords, threat.CurrentCoords, 1);
            if (retreatHex.HasValue && !retreatHex.Value.Equals(actor.CurrentCoords))
            {
                var path = _pathfinder.FindPath(actor.CurrentCoords, retreatHex.Value, 1, out int cost);
                if (path != null && path.Count > 1)
                {
                    yield return StartCoroutine(actor.MoveAlongPath(path, _grid, cost));
                }
            }
        }

        private HexCoordinates? FindBestRetreatHex(HexCoordinates current, HexCoordinates threatCoords, int apBudget)
        {
            HexCoordinates? bestHex = null;
            float maxThreatDistance = current.DistanceTo(threatCoords);

            for (int dir = 0; dir < 6; dir++)
            {
                var step = current.GetNeighbor(dir);
                var node = _grid.GetNode(step);
                if (node == null || !node.IsWalkable || node.IsOccupied) continue;

                int dist = step.DistanceTo(threatCoords);
                if (dist > maxThreatDistance)
                {
                    var path = _pathfinder.FindPath(current, step, apBudget, out int cost);
                    if (path != null && path.Count > 0 && cost <= apBudget)
                    {
                        maxThreatDistance = dist;
                        bestHex = step;
                    }
                }
            }

            return bestHex;
        }

        // =========================================================================
        // ATTAQUE TACTIQUE & INJECTION DES PA (LIVRE VI)
        // =========================================================================
        private IEnumerator ExecuteTacticalAttack(TacticalUnit actor, TacticalUnit target, TacticalPosture posture)
        {
            _arena?.SelectTarget(target);

            int availableAP = actor.Stats.CurrentActionPoints;
            int reserveNeeded = GetRequiredDefensiveReserve(actor, posture);

            SkillType attackSkill = actor.CurrentCoords.DistanceTo(target.CurrentCoords) <= 1
                ? actor.Stats.GetBestMeleeAttackSkill()
                : SkillType.Ballistique;

            DiceType attackDie = actor.Stats.GetSkillDie(attackSkill, true);
            int maxAttacks = actor.Stats.GetMaxAttacksAllowed(attackDie);
            bool canAttackAgainLater = (actor.Stats.AttacksThisTurn + 1 < maxAttacks) && (availableAP >= 4 + reserveNeeded);

            BodyPart chosenPart = BodyPart.Torse;
            bool cancelPenalty = false;

            if (posture == TacticalPosture.AllInLethal && availableAP >= 3)
            {
                chosenPart = BodyPart.Tete;
                cancelPenalty = true;
            }
            else if (_defaultPersonality == AIPersonality.Tactician && availableAP >= 3 && target.Stats.CurrentActionPoints > 2)
            {
                chosenPart = BodyPart.BrasDroit;
                cancelPenalty = true;
            }

            int baseCost = cancelPenalty ? 3 : 2;
            int apAfterBase = availableAP - baseCost;

            // Conversion intégrale des PA excédentaires en bonus de touche & différentiel
            int bonusAP = 0;
            if (canAttackAgainLater)
            {
                int spendableForBonus = apAfterBase - 2 - reserveNeeded;
                bonusAP = Mathf.Clamp(spendableForBonus, 0, 3);
            }
            else
            {
                int spendableForBonus = apAfterBase - reserveNeeded;
                bonusAP = Mathf.Clamp(spendableForBonus, 0, 4);
            }

            var visual = actor.GetComponent<TacticalUnitVisual>();
            string partLabel = chosenPart == BodyPart.Tete ? "[VISÉE] Tête" :
                               chosenPart == BodyPart.BrasDroit ? "[VISÉE] Désarmement" : "[VISÉE] Torse";
            if (bonusAP > 0) partLabel += $" (+{bonusAP} PA Inj.)";

            visual?.SpawnFloatingText(partLabel, chosenPart == BodyPart.Tete ? Color.red : Color.cyan);
            yield return new WaitForSeconds(Mathf.Min(0.5f, _actionDelay * 0.35f));

            SkillType defenseSkill = target.Stats.Attributes.Agilite >= target.Stats.Attributes.Force
                ? SkillType.Esquive
                : SkillType.DefenseCorporelle;

            _arena?.ExecuteAttack(
                targetedPart: chosenPart,
                cancelPenaltyWithAP: cancelPenalty,
                attackSkill: attackSkill,
                defenseSkill: defenseSkill,
                defenderWantsToDefend: true,
                attackerBonusAP: bonusAP
            );

            if (_cinematicDirector != null)
            {
                while (_cinematicDirector.CurrentMode == CameraMode.CinematicAction)
                {
                    yield return null;
                }
            }
        }

        // =========================================================================
        // APPROCHE FLUIDE PAR TRONÇONNAGE DU CHEMIN
        // =========================================================================
        private List<HexCoordinates> FindPathTowardsTarget(TacticalUnit actor, TacticalUnit target, int apBudget)
        {
            if (_pathfinder == null || _grid == null || apBudget <= 0) return null;

            HexCoordinates bestNeighbor = target.CurrentCoords;
            int minDistance = int.MaxValue;

            for (int dir = 0; dir < 6; dir++)
            {
                var n = target.CurrentCoords.GetNeighbor(dir);
                var nNode = _grid.GetNode(n);
                if (nNode != null && nNode.IsWalkable && (!nNode.IsOccupied || n.Equals(actor.CurrentCoords)))
                {
                    int d = actor.CurrentCoords.DistanceTo(n);
                    if (d < minDistance)
                    {
                        minDistance = d;
                        bestNeighbor = n;
                    }
                }
            }

            if (actor.CurrentCoords.DistanceTo(target.CurrentCoords) <= 1)
            {
                return null;
            }

            // Tracé global sans restriction de budget immédiat (capé à 50)
            var fullPath = _pathfinder.FindPath(actor.CurrentCoords, bestNeighbor, 50, out _);
            if (fullPath == null || fullPath.Count <= 1)
            {
                return null;
            }

            // Tronçonnage à la limite stricte du budget de marche alloué ce tour
            var truncatedPath = new List<HexCoordinates> { fullPath[0] };
            int accumulatedCost = 0;

            for (int i = 1; i < fullPath.Count; i++)
            {
                var step = fullPath[i];
                var stepNode = _grid.GetNode(step);
                int stepCost = stepNode != null ? stepNode.ActionPointCost : 1;

                if (accumulatedCost + stepCost > apBudget)
                {
                    break;
                }

                if (stepNode != null && (stepNode.IsOccupied || !stepNode.IsWalkable))
                {
                    break;
                }

                accumulatedCost += stepCost;
                truncatedPath.Add(step);
            }

            return truncatedPath.Count > 1 ? truncatedPath : null;
        }

        private TacticalUnit EvaluateBestTarget(TacticalUnit actor)
        {
            TacticalUnit bestTarget = null;
            float highestScore = float.MinValue;

            for (int i = 0; i < _cachedUnits.Count; i++)
            {
                var potential = _cachedUnits[i];
                if (potential == null || potential == actor || !potential.Stats.IsAlive) continue;

                bool isHostile = actor.IsPlayerControlled ? (!potential.IsPlayerControlled) : potential.IsPlayerControlled;
                if (_mode == CombatAIMode.FullAuto && potential.IsPlayerControlled != actor.IsPlayerControlled) isHostile = true;
                if (!isHostile) continue;

                int dist = actor.CurrentCoords.DistanceTo(potential.CurrentCoords);
                float curHp = potential.Stats.CurrentHealth;

                float score = 70f - (dist * 7f);
                score += (1.0f - (curHp / Mathf.Max(1, potential.Stats.MaxHealth))) * 35f;

                if (dist == 1) score += 40f;
                if (curHp <= 6) score += 45f;

                if (score > highestScore)
                {
                    highestScore = score;
                    bestTarget = potential;
                }
            }

            return bestTarget;
        }

        private void UpdateUnitCache()
        {
            _cachedUnits.Clear();
            _cachedUnits.AddRange(FindObjectsByType<TacticalUnit>());
        }
    }
}
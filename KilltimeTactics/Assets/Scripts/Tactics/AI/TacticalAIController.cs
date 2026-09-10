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

namespace Killtime.Tactics.AI
{
    public enum CombatAIMode
    {
        Normal,   // IA uniquement sur les ennemis/PNJ
        FullAuto  // IA sur l'intégralité des unités
    }

    public enum AIPersonality
    {
        Balanced,   // Équilibre attaque, réserve défensive et survie
        Aggressive, // Dépense tout, prend des risques pour tuer vite
        Tactician,  // Privilégie la réserve de parade, le hit-and-run et les membres
        Survivor    // Fuit très vite si blessé, joue l'attrition à distance
    }

    public enum TacticalPosture
    {
        AllInLethal,     // Opportunité d'élimination : dépense tous ses PA pour tuer
        CalculatedStrike,// Attaque tout en conservant une réserve de PA pour parer/esquiver
        HitAndRun,       // Frappe puis recule pour forcer l'ennemi à marcher
        TacticalRetreat, // En danger mortel : repli, gain de distance, souffle de fuite
        DefensiveHold    // Trop loin pour frapper sans s'exposer : attend en garde haute
    }

    /// <summary>
    /// Contrôleur d'IA Tactique Militaire.
    /// Gère l'économie stricte des PA (défense vs attaque), les décrochages et la survie.
    /// </summary>
    public class TacticalAIController : MonoBehaviour
    {
        [Header("Systèmes")]
        [SerializeField] private TurnManager _turnManager;
        [SerializeField] private TacticalHexGrid _grid;
        [SerializeField] private CombatDevArena _arena;

        [Header("Paramètres de Doctrine")]
        [SerializeField] private CombatAIMode _mode = CombatAIMode.Normal;
        [SerializeField] private AIPersonality _defaultPersonality = AIPersonality.Balanced;
        [SerializeField] private bool _isAIEnabled = true;
        [SerializeField] [Range(0.05f, 4.0f)] private float _actionDelay = 0.55f;

        [Header("Paramètres de Survie & Économie")]
        [SerializeField] [Range(0.15f, 0.5f)] private float _retreatHealthRatio = 0.30f; // Fuit si PV < 30%
        [SerializeField] [Range(0, 3)] private int _baseDefensiveAPReserve = 1;         // PA gardés pour les réactions

        public CombatAIMode Mode { get => _mode; set => _mode = value; }
        public bool IsAIEnabled { get => _isAIEnabled; set => _isAIEnabled = value; }
        public float ActionDelay { get => _actionDelay; set => _actionDelay = Mathf.Clamp(value, 0.05f, 4.0f); }

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

            // Sécurité anti-blocage : si l'IA reçoit le tour d'une unité morte, libérer le tour immédiatement
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
            yield return new WaitForSeconds(_actionDelay * 0.4f);

            if (_pathfinder == null && _grid != null) _pathfinder = new HexPathfinder(_grid);
            UpdateUnitCache();

            var visual = unit.GetComponent<TacticalUnitVisual>();
            int loopGuard = 0;
            const int maxSteps = 6;

            while (unit.Stats.IsAlive && loopGuard++ < maxSteps)
            {
                var target = EvaluateBestTarget(unit);
                if (target == null) break;

                int dist = unit.CurrentCoords.DistanceTo(target.CurrentCoords);
                var posture = EvaluateTacticalPosture(unit, target, dist);

                // -------------------------------------------------------------
                // CAS 1 : RETRAITE STRATÉGIQUE / SURVIE CRITIQUE
                // -------------------------------------------------------------
                if (posture == TacticalPosture.TacticalRetreat)
                {
                    visual?.SpawnFloatingText("🚨 Repli Tactique d'Urgence", new Color(1.0f, 0.3f, 0.3f));
                    yield return StartCoroutine(ExecuteRetreat(unit, target));
                    break;
                }

                // -------------------------------------------------------------
                // CAS 2 : EMBUSCADE / DÉFENSE (Trop loin pour frapper ce tour-ci)
                // -------------------------------------------------------------
                if (posture == TacticalPosture.DefensiveHold)
                {
                    // L'IA refuse de courir se suicider au contact à 0 PA
                    visual?.SpawnFloatingText($"🛡️ Garde Défensive ({unit.Stats.CurrentActionPoints} PA)", new Color(0.3f, 0.8f, 1.0f));
                    yield return new WaitForSeconds(_actionDelay * 0.5f);
                    break;
                }

                // -------------------------------------------------------------
                // CAS 3 : COMBAT AU CONTACT (Distance == 1)
                // -------------------------------------------------------------
                if (dist == 1)
                {
                    int reserveNeeded = GetRequiredDefensiveReserve(unit, posture);
                    int apAfterAttack = unit.Stats.CurrentActionPoints - 2;

                    // Si attaquer nous laisse à 0 PA et sans défense contre un adversaire redoutable
                    if (apAfterAttack < reserveNeeded && posture != TacticalPosture.AllInLethal)
                    {
                        // Peut-on utiliser le souffle pour financer l'attaque ET garder la réserve ?
                        if (CanTakeBreathSafely(unit))
                        {
                            unit.Stats.TakeEmergencyBreath(2);
                            visual?.SpawnFloatingText("🫁 Souffle : Maintien de Garde (+2 PA)", Color.yellow);
                            yield return new WaitForSeconds(_actionDelay * 0.5f);
                        }
                        else
                        {
                            // On refuse d'attaquer si cela annule totalement notre capacité à réagir
                            visual?.SpawnFloatingText("🛡️ Garde Fermée (Économie Réaction)", new Color(0.2f, 0.9f, 0.6f));
                            break;
                        }
                    }

                    if (unit.Stats.CurrentActionPoints >= 2 && unit.Stats.CanAttack(DiceType.D6))
                    {
                        yield return StartCoroutine(ExecuteTacticalAttack(unit, target, posture));

                        // Si posture Hit-and-Run : Utiliser les PA restants pour décrocher !
                        if (posture == TacticalPosture.HitAndRun && unit.Stats.CurrentActionPoints >= 1)
                        {
                            yield return new WaitForSeconds(_actionDelay * 0.4f);
                            visual?.SpawnFloatingText("⚡ Décrochage Tactique", new Color(0.9f, 0.7f, 0.2f));
                            yield return StartCoroutine(ExecuteDisengageStep(unit, target));
                        }

                        yield return new WaitForSeconds(_actionDelay * 0.5f);
                        continue;
                    }
                    break;
                }

                // -------------------------------------------------------------
                // CAS 4 : APPROCHE COORDONNÉE (Budget Déplacement + Attaque)
                // -------------------------------------------------------------
                if (unit.Stats.CurrentActionPoints >= 3)
                {
                    var approachHex = FindSmartApproachHex(unit, target);
                    if (approachHex.HasValue)
                    {
                        var path = _pathfinder.FindPath(unit.CurrentCoords, approachHex.Value, unit.Stats.CurrentActionPoints, out int cost);
                        if (path != null && path.Count > 1)
                        {
                            visual?.SpawnFloatingText($"Manoeuvre d'assaut (-{cost} PA)", new Color(0.2f, 0.85f, 1.0f));
                            yield return StartCoroutine(unit.MoveAlongPath(path, _grid, cost));
                            yield return new WaitForSeconds(_actionDelay * 0.5f);
                            continue;
                        }
                    }
                }

                break;
            }

            yield return new WaitForSeconds(0.2f);
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

            // 1. Survie : Si gravement blessé et pas au contact immédiat avec une cible achevable
            if (isMortalDanger && !CanKillTargetThisTurn(actor, target))
            {
                return TacticalPosture.TacticalRetreat;
            }

            // 2. All-In Létal : Si on a les moyens mathématiques d'éliminer la cible ce tour-ci
            if (CanKillTargetThisTurn(actor, target))
            {
                return TacticalPosture.AllInLethal;
            }

            // 3. Cas où l'ennemi est hors de portée de coup direct ce tour-ci
            if (dist > 1)
            {
                int moveCostNeeded = dist - 1; // Coût approx en PA
                int apAfterMove = actor.Stats.CurrentActionPoints - moveCostNeeded;

                // ANTI-SUICIDE : Si s'approcher au contact nous vide nos PA (< 2 PA restants)
                // L'IA refuse de courir se livrer sans pouvoir frapper ni parer.
                if (apAfterMove < 2)
                {
                    return TacticalPosture.DefensiveHold;
                }
            }

            // 4. Hit-and-Run : Si tactique ou agile, avec des PA pour taper et reculer
            if ((_defaultPersonality == AIPersonality.Tactician || _defaultPersonality == AIPersonality.Survivor) && actor.Stats.CurrentActionPoints >= 3)
            {
                return TacticalPosture.HitAndRun;
            }

            return TacticalPosture.CalculatedStrike;
        }

        private int GetRequiredDefensiveReserve(TacticalUnit actor, TacticalPosture posture)
        {
            if (posture == TacticalPosture.AllInLethal) return 0;
            if (_defaultPersonality == AIPersonality.Aggressive) return 0;
            if (_defaultPersonality == AIPersonality.Tactician) return 2; // Garde toujours de quoi faire une parade active

            return _baseDefensiveAPReserve;
        }

        private bool CanKillTargetThisTurn(TacticalUnit actor, TacticalUnit target)
        {
            int maxPotentialAP = actor.Stats.CurrentActionPoints;
            if (CanTakeBreathSafely(actor)) maxPotentialAP += 2;

            int dist = actor.CurrentCoords.DistanceTo(target.CurrentCoords);
            int apToReach = (dist > 1) ? (dist - 1) : 0;
            int apForAttacks = maxPotentialAP - apToReach;

            if (apForAttacks < 2) return false;

            // Estimation des dégâts d'une frappe tête (critique x2) vs PV cible
            int targetHp = target.Stats.CurrentHealth;
            int estimatedHeadDmg = Mathf.Max(3, (8 - target.Stats.BaseArmorAbsorption) * 2);

            return targetHp <= estimatedHeadDmg;
        }

        private bool CanTakeBreathSafely(TacticalUnit unit)
        {
            return unit.Stats.Essoufflement < unit.Stats.Attributes.Constitution;
        }

        // =========================================================================
        // RETRAITE STRATÉGIQUE & DÉCROCHAGE
        // =========================================================================
        private IEnumerator ExecuteRetreat(TacticalUnit actor, TacticalUnit nearestThreat)
        {
            // Sprint de survie : si on a du souffle, on l'utilise pour fuir plus loin
            if (CanTakeBreathSafely(actor) && actor.Stats.CurrentActionPoints < 3)
            {
                actor.Stats.TakeEmergencyBreath(2);
                var visual = actor.GetComponent<TacticalUnitVisual>();
                visual?.SpawnFloatingText("🫁 Sprint de Survie (+2 PA)", Color.red);
                yield return new WaitForSeconds(0.25f);
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
            // Recule d'un hexagone pour forcer l'adversaire à dépenser 1 PA de marche au tour prochain
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

            // Explore les hexagones atteignables dans le budget PA
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
        // ATTAQUE CHIRURGICALE ADAPTIVE
        // =========================================================================
        private IEnumerator ExecuteTacticalAttack(TacticalUnit actor, TacticalUnit target, TacticalPosture posture)
        {
            _arena?.SelectTarget(target);
            yield return new WaitForSeconds(0.12f);

            int ap = actor.Stats.CurrentActionPoints;
            int targetHp = target.Stats.CurrentHealth;
            int targetArmor = target.Stats.BaseArmorAbsorption;

            BodyPart chosenPart = BodyPart.Torse;
            bool cancelPenalty = false;
            int bonusAP = 0;

            // En All-In Létal : On tente la tête en maximisant l'investissement de PA
            if (posture == TacticalPosture.AllInLethal && ap >= 3)
            {
                chosenPart = BodyPart.Tete;
                cancelPenalty = true;
                if (ap >= 4) bonusAP = 1;
            }
            // Ciblage membre si posture tactique
            else if (_defaultPersonality == AIPersonality.Tactician && target.Stats.CurrentActionPoints > 3 && ap >= 3)
            {
                chosenPart = BodyPart.BrasDroit; // Neutralisation offensive
                cancelPenalty = true;
            }
            // Frappe au torse économique et sûre
            else
            {
                chosenPart = BodyPart.Torse;
                cancelPenalty = false;
            }

            var visual = actor.GetComponent<TacticalUnitVisual>();
            string partLabel = chosenPart == BodyPart.Tete ? "🎯 Tir Tête (All-In)" :
                               chosenPart == BodyPart.BrasDroit ? "🦾 Tir Désarmement (Bras)" : "⚔️ Tir Centré (Torse)";
            visual?.SpawnFloatingText(partLabel, chosenPart == BodyPart.Tete ? Color.red : Color.cyan);

            _arena?.ExecuteAttack(chosenPart, cancelPenalty, attackDie: DiceType.D6, attackerBonusAP: bonusAP);
        }

        // =========================================================================
        // SÉLECTION DE LA CIBLE ET POSITIONNEMENT SMART
        // =========================================================================
        private HexCoordinates? FindSmartApproachHex(TacticalUnit actor, TacticalUnit target)
        {
            int ap = actor.Stats.CurrentActionPoints;
            HexCoordinates? bestHex = null;
            int lowestCost = int.MaxValue;

            for (int dir = 0; dir < 6; dir++)
            {
                var neighbor = target.CurrentCoords.GetNeighbor(dir);
                var node = _grid.GetNode(neighbor);
                if (node == null || !node.IsWalkable || (node.IsOccupied && !neighbor.Equals(actor.CurrentCoords))) continue;

                var path = _pathfinder.FindPath(actor.CurrentCoords, neighbor, ap, out int cost);
                if (path != null && path.Count > 0 && cost < lowestCost)
                {
                    // Ne retient que si on conserve au moins de quoi porter une attaque après la marche
                    if ((ap - cost) >= 2 || _defaultPersonality == AIPersonality.Aggressive)
                    {
                        lowestCost = cost;
                        bestHex = neighbor;
                    }
                }
            }

            return bestHex;
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

                // Score de priorité opérationnelle
                float score = 60f - (dist * 9f);
                score += (1.0f - (curHp / Mathf.Max(1, potential.Stats.MaxHealth))) * 40f; // Focus faibles

                if (dist == 1) score += 30f; // Menace directe
                if (curHp <= 6) score += 40f; // Cible achevable

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
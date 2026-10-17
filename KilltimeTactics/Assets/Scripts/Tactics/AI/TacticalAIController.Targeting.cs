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
        private List<HexCoordinates> FindPathTowardsTarget(TacticalUnit actor, TacticalUnit target, int apBudget)
        {
            if (_pathfinder == null || _grid == null || apBudget <= 0) return null;

            // RD-IA10 : anneau d'approche autour de TOUTE l'empreinte (les
            // voisins du centre d'un Titan sont à l'intérieur de son corps).
            HexCoordinates bestNeighbor = target.CurrentCoords;
            int minDistance = int.MaxValue;
            var targetCells = target.OccupiedCoords;
            var candidates = new HashSet<HexCoordinates>();
            for (int c = 0; c < targetCells.Count; c++)
            {
                for (int dir = 0; dir < 6; dir++)
                    candidates.Add(targetCells[c].GetNeighbor(dir));
            }
            foreach (var n in candidates)
            {
                if (target.Occupies(n)) continue;
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

            if (TitanFootprint.MinDistanceBetweenUnits(actor.CurrentCoords, actor.FootprintType, target.CurrentCoords, target.FootprintType) <= 1) return null;

            var fullPath = _pathfinder.FindPath(actor.CurrentCoords, bestNeighbor, 50, out _);
            if (fullPath == null || fullPath.Count <= 1) return null;

            return AIMovementSolver.TruncatePathByBudget(
                fullPath,
                apBudget,
                step => { var n = _grid.GetNode(step); return n != null ? n.ActionPointCost : 1; },
                step => { var n = _grid.GetNode(step); return n != null && (n.IsOccupied || !n.IsWalkable); },
                fullPath.Count - 1,
                out _);
        }

        private List<HexCoordinates> FindPathTowardsRangedPosition(TacticalUnit actor, TacticalUnit target, int maxRange, int apBudget)
        {
            if (_pathfinder == null || _grid == null || apBudget <= 0) return null;

            int curDist = TitanFootprint.MinDistanceBetweenUnits(actor.CurrentCoords, actor.FootprintType, target.CurrentCoords, target.FootprintType);
            if (curDist <= maxRange && HasLineOfSight(actor.CurrentCoords, target.CurrentCoords, target.FootprintType, actor.FootprintType, actor)) return null;

            // RD-IA10 : anneau d'approche autour de toute l'empreinte cible.
            HexCoordinates bestNeighbor = target.CurrentCoords;
            int minDistance = int.MaxValue;
            var targetCells = target.OccupiedCoords;
            var candidates = new HashSet<HexCoordinates>();
            for (int c = 0; c < targetCells.Count; c++)
            {
                for (int dir = 0; dir < 6; dir++)
                    candidates.Add(targetCells[c].GetNeighbor(dir));
            }
            foreach (var n in candidates)
            {
                if (target.Occupies(n)) continue;
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

            var fullPath = _pathfinder.FindPath(actor.CurrentCoords, bestNeighbor, 50, out _);
            if (fullPath == null || fullPath.Count <= 1) return null;

            int targetStepIndex = fullPath.Count - 1;
            for (int i = 1; i < fullPath.Count; i++)
            {
                int d = TitanFootprint.MinDistanceBetweenUnits(fullPath[i], actor.FootprintType, target.CurrentCoords, target.FootprintType);
                if (d <= maxRange && HasLineOfSight(fullPath[i], target.CurrentCoords, target.FootprintType, actor.FootprintType, actor))
                {
                    targetStepIndex = i;
                    break;
                }
            }

            var truncatedPath = AIMovementSolver.TruncatePathByBudget(
                fullPath,
                apBudget,
                step => { var n = _grid.GetNode(step); return n != null ? n.ActionPointCost : 1; },
                step => { var n = _grid.GetNode(step); return n != null && (n.IsOccupied || !n.IsWalkable); },
                targetStepIndex,
                out _);
            return truncatedPath;
        }

        private bool HasRangedWeapon(TacticalUnit unit, out int maxRange, out int minRange, out SkillType attackSkill)
        {
            maxRange = 1;
            minRange = 1;
            attackSkill = unit != null && unit.Stats != null ? unit.Stats.GetBestMeleeAttackSkill() : SkillType.MainsNues;

            if (unit == null || unit.Stats == null) return false;

            var sheet = unit.Sheet ?? unit.GetOrBuildSheet();
            var weapon = sheet?.GetEquippedWeapon();

            if (weapon != null && weapon.Type == ItemType.Weapon)
            {
                if (weapon.AssociatedSkill == SkillType.Ballistique || weapon.RangeInTiles > 1 || TacticalUnitVisual.IsRifleWeapon(weapon))
                {
                    maxRange = Mathf.Max(2, weapon.RangeInTiles);
                    minRange = 1;
                    attackSkill = SkillType.Ballistique;
                    return true;
                }
            }

            var visual = unit.GetComponent<TacticalUnitVisual>();
            if (visual != null && visual.HasRifleEquipped())
            {
                maxRange = 10;
                minRange = 1;
                attackSkill = SkillType.Ballistique;
                return true;
            }

            // RD-IA08 : suppression du repli "rang Balistique > mêlée +2 ⇒ tir
            // à 8 cases" : sans arme à distance équipée, l'unité ne tire pas
            // (l'ancien code faisait "tirer" des PNJ désarmés en duel
            // Ballistique fantôme). Le contact / les sorts prennent le relais.
            return false;
        }

        private bool HasLineOfSight(HexCoordinates from, HexCoordinates to, TitanFootprintType toFootprint = TitanFootprintType.Single, TitanFootprintType fromFootprint = TitanFootprintType.Single, TacticalUnit viewer = null)
        {
            if (_grid == null) return true;
            // RD-IA08 : CdV unifiée avec le couvert (inclut la fumée RD-041).
            // Avant : EvaluateCover direct ⇒ l'IA "voyait" à travers la fumée
            // pour attaquer mais pas pour évaluer le couvert (incohérent).
            return GetCoverLevel(from, to, toFootprint, fromFootprint, viewer) != CoverType.Full;
        }

        private CoverType GetCoverLevel(HexCoordinates from, HexCoordinates to, TitanFootprintType toFootprint = TitanFootprintType.Single, TitanFootprintType fromFootprint = TitanFootprintType.Single, TacticalUnit viewer = null)
        {
            if (_grid == null) return CoverType.None;
            CoverType baseCover = CoverSystem.EvaluateCover(from, to, _grid, toFootprint, fromFootprint);
            // RD-041 : la fumée coupe aussi la visée de l'IA (sauf thermique).
            if (baseCover != CoverType.Full && _arena != null
                && Killtime.Core.Combat.SmokeScreen.SegmentSmoked(from, to, _arena.SmokeZones)
                && (viewer == null || !CombatDevArena.SeesThroughSmoke(viewer)))
            {
                return CoverType.Full;
            }
            return baseCover;
        }

        private TacticalUnit EvaluateBestTarget(TacticalUnit actor)
        {
            TacticalUnit bestTarget = null;
            float highestScore = float.MinValue;
            bool isRanged = HasRangedWeapon(actor, out int maxRange, out _, out _);

            // Regard de Prédateur (Livre III) : provoqué → l'unité DOIT attaquer
            // son provocateur tant que la marque est active et qu'il est en vie.
            if (SkillTechniqueState.TryGetTaunter(actor.Stats, out var taunter) && taunter != null)
            {
                for (int i = 0; i < _cachedUnits.Count; i++)
                {
                    var provoker = _cachedUnits[i];
                    if (provoker != null && provoker.Stats == taunter && provoker.Stats.IsAlive)
                    {
                        return provoker;
                    }
                }
            }

            // RD-039 : En lutte active (victime ou grappler) → priorité absolue au partenaire de lutte.
            if (GrappleState.TryGetGrappler(actor.Stats, out var holdingGrappler) && holdingGrappler != null)
            {
                for (int i = 0; i < _cachedUnits.Count; i++)
                {
                    var gUnit = _cachedUnits[i];
                    if (gUnit != null && gUnit.Stats == holdingGrappler && gUnit.Stats.IsAlive)
                    {
                        return gUnit;
                    }
                }
            }
            if (GrappleState.TryGetGrappledVictim(actor.Stats, out var heldVictim) && heldVictim != null)
            {
                for (int i = 0; i < _cachedUnits.Count; i++)
                {
                    var vUnit = _cachedUnits[i];
                    if (vUnit != null && vUnit.Stats == heldVictim && vUnit.Stats.IsAlive)
                    {
                        return vUnit;
                    }
                }
            }

            for (int i = 0; i < _cachedUnits.Count; i++)
            {
                var potential = _cachedUnits[i];
                if (potential == null || potential == actor || potential.Stats == null || !potential.Stats.IsAlive || potential.Stats.IsSurrendered) continue;

                bool isHostile = actor.IsPlayerControlled ? !potential.IsPlayerControlled : potential.IsPlayerControlled;
                if (_mode == CombatAIMode.FullAuto && potential.IsPlayerControlled != actor.IsPlayerControlled) isHostile = true;
                if (!isHostile) continue;

                // Brouillard de guerre symétrique (§25.4) : l'IA ne cible que ce que
                // l'acteur voit réellement à 360° (portée d'ambiance + murs
                // Full), sauf cible révélée au bruit (tirs) ou par Observation.
                // Le contact (dist <= 1) reste toujours visible des deux côtés.
                if (_grid != null && actor.Stats != null && potential.Stats != null)
                {
                    var fog = Killtime.Tactics.Visibility.FogOfWarManager.Instance;
                    bool isNight = fog != null && fog.IsNight;
                    int range = fog != null ? fog.CurrentSightRange
                        : Killtime.Tactics.Visibility.FogOfWarSystem.LongSightRange;
                    var vp = Killtime.Tactics.Visibility.FogOfWarSystem.ResolveObserverParams(actor.Stats, range, isNight);
                    float actorYaw = Killtime.Tactics.Visibility.FogOfWarManager.FacingYawDeg(actor);
                    bool seen = Killtime.Tactics.Visibility.FogOfWarSystem.IsCellVisible(
                        actor.CurrentCoords, actorYaw, potential.CurrentCoords,
                        c => _grid.GetNode(c), vp, _grid.HexRadius);

                    // RD-053 : Lampe dans la nuit — la lumière trahit la position même au-delà de la portée nocturne
                    if (!seen && isNight && potential.HasFlashlight && HasLineOfSight(actor.CurrentCoords, potential.CurrentCoords, potential.FootprintType, actor.FootprintType, actor))
                    {
                        seen = true;
                    }

                    bool stealthed = Killtime.Core.Combat.StealthState.IsStealthed(potential.Stats);
                    // RD-IA09 : distance d'empreinte (contact Titan = 1, pas 3).
                    int targetDist = TitanFootprint.MinDistanceBetweenUnits(actor.CurrentCoords, actor.FootprintType, potential.CurrentCoords, potential.FootprintType);
                    if (!seen || (stealthed && targetDist > 1 && !vp.Thermal))
                    {
                        bool revealed = fog != null
                            && (fog.IsNoiseActive(potential.gameObject.name)
                                || fog.IsManuallyRevealed(potential.gameObject.name));
                        if (!revealed) continue;
                    }
                }

                int dist = TitanFootprint.MinDistanceBetweenUnits(actor.CurrentCoords, actor.FootprintType, potential.CurrentCoords, potential.FootprintType);
                float curHp = potential.Stats.CurrentHealth;

                // Scoring pur déporté dans TargetScorer (formule inchangée).
                // Le filtrage visibilité/bruit ci-dessus reste dans le contrôleur.
                bool inRangedZoneForCover = isRanged && (dist <= maxRange);
                bool hasLOSForScore = false;
                CoverType coverForScore = CoverType.None;
                if (isRanged && inRangedZoneForCover)
                {
                    hasLOSForScore = HasLineOfSight(actor.CurrentCoords, potential.CurrentCoords, potential.FootprintType, actor.FootprintType, actor);
                    if (hasLOSForScore)
                        coverForScore = GetCoverLevel(actor.CurrentCoords, potential.CurrentCoords, potential.FootprintType, actor.FootprintType, actor);
                }
                else if (isRanged)
                {
                    hasLOSForScore = HasLineOfSight(actor.CurrentCoords, potential.CurrentCoords, potential.FootprintType, actor.FootprintType, actor);
                }
                TacticalUnit squadTarget = GetSquadMarkedTarget(actor);
                var fogInstance = Killtime.Tactics.Visibility.FogOfWarManager.Instance;
                var scoreInput = new TargetScoreInput
                {
                    IsRanged = isRanged,
                    Dist = dist,
                    MaxRange = maxRange,
                    HasLOS = hasLOSForScore,
                    TargetCover = coverForScore,
                    CurHp = curHp,
                    MaxHp = potential.Stats.MaxHealth,
                    Encaissement = potential.Stats.EncaissementThreshold,
                    IsSquadMarked = (squadTarget != null && squadTarget == potential),
                    NightLampBonus = (fogInstance != null && fogInstance.IsNight && potential.HasFlashlight),
                    NoiseBonus = (fogInstance != null && fogInstance.IsNoiseActive(potential.gameObject.name)),
                    HasDestabilise = ((potential.Stats.ActiveStatus & StatusEffect.Destabilise) != 0),
                    HasATerre = ((potential.Stats.ActiveStatus & StatusEffect.ATerre) != 0),
                    FlawExposed = SkillTechniqueState.IsFlawExposed(potential.Stats),
                    TargetCurrentAP = potential.Stats.CurrentActionPoints,
                };
                float score = TargetScorer.Score(in scoreInput);

                if (score > highestScore)
                {
                    highestScore = score;
                    bestTarget = potential;
                }
            }

            if (bestTarget != null)
            {
                SetSquadMarkedTarget(actor, bestTarget);
            }

            return bestTarget;
        }

        private void UpdateUnitCache()
        {
            _cachedUnits.Clear();
            _cachedUnits.AddRange(TacticalUnitRegistry.AllUnits);
        }

        private TacticalUnit GetSquadMarkedTarget(TacticalUnit actor)
        {
            var target = actor.IsPlayerControlled ? _squadMarkedEnemyOfPlayers : _squadMarkedEnemyOfAI;
            if (target != null && target.Stats != null && target.Stats.IsAlive)
            {
                return target;
            }
            return null;
        }

        private void SetSquadMarkedTarget(TacticalUnit actor, TacticalUnit target)
        {
            if (actor.IsPlayerControlled)
            {
                _squadMarkedEnemyOfPlayers = target;
            }
            else
            {
                _squadMarkedEnemyOfAI = target;
            }
        }

        private float EvaluatePrecomputedCrossfireBonus(TacticalUnit target, HexCoordinates candidateHex, List<TacticalUnit> firingAllies)
        {
            if (_grid == null || target == null || firingAllies == null || firingAllies.Count == 0) return 0f;

            var targetNode = _grid.GetNode(target.CurrentCoords);
            Vector3 targetPos = targetNode != null ? targetNode.WorldPosition : target.transform.position;

            var candNode = _grid.GetNode(candidateHex);
            Vector3 candPos = candNode != null ? candNode.WorldPosition : candidateHex.ToWorldPosition(_grid.HexRadius, targetPos.y);

            // Géométrie pure déportée dans AIMovementSolver (angles en L).
            Vector3 toCandidate = (candPos - targetPos);
            var toAllies = new List<Vector3>(firingAllies.Count);
            for (int i = 0; i < firingAllies.Count; i++)
            {
                var ally = firingAllies[i];
                if (ally == null || ally.Stats == null || !ally.Stats.IsAlive) continue;
                toAllies.Add(ally.transform.position - targetPos);
            }
            return AIMovementSolver.ScoreCrossfire(toCandidate, toAllies);
}
    }
}

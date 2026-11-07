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
        // =========================================================================
        // MANŒUVRES AVANCÉES PNJ & AUTOMATES (LIVRE XII §48 & §51 - RD-088)
        // =========================================================================
        private void CheckAndApplyHydraulicBite(TacticalUnit unit, TacticalUnitVisual visual)
        {
            if (unit?.Stats == null || !unit.Stats.IsAlive) return;
            var victimStats = LivreXIPNJState.GetVictimOf(unit.Stats);
            if (victimStats == null) return;

            TacticalUnit victimUnit = null;
            for (int i = 0; i < _cachedUnits.Count; i++)
            {
                if (_cachedUnits[i] != null && _cachedUnits[i].Stats == victimStats)
                {
                    victimUnit = _cachedUnits[i];
                    break;
                }
            }

            if (victimUnit == null || !victimStats.IsAlive || unit.CurrentCoords.DistanceTo(victimUnit.CurrentCoords) > 1)
            {
                LivreXIPNJState.ReleaseHydraulicBite(unit.Stats);
                return;
            }

            int dmg = 2;
            int rem = victimStats.AbsorbShield(dmg);
            if (rem > 0)
            {
                if (victimStats.CurrentHealth - rem <= 0)
                    victimStats.EvaluateFatalBlow(BodyPart.Jambes, rem);
                else
                    victimStats.CurrentHealth -= rem;
            }

            victimStats.ApplyStatus(StatusEffect.Immobilise, 1);
            visual?.SpawnFloatingText("🦷 Broyage Hydraulique (-2 PV auto)", new Color(1f, 0.25f, 0.1f));
            var tgtVis = victimUnit.GetComponent<TacticalUnitVisual>();
            tgtVis?.TriggerHitFlash();
            tgtVis?.SpawnFloatingText("💥 -2 PV (Morsure Hydraulique)", Color.red);
            _arena?.Log($"🦷 <b>{unit.Stats.Name}</b> broie les os de <b>{victimStats.Name}</b> avec sa mâchoire verrouillée (-2 PV automatiques) !");
        }

        private bool TryExecuteLivreXIManeuver(TacticalUnit actor, TacticalUnit target, TacticalPosture posture, int dist, TacticalUnitVisual visual)
        {
            if (actor?.Stats == null || !actor.Stats.IsAlive) return false;
            int ap = actor.Stats.CurrentActionPoints;

            // 1. BANDIT #3 (Livre XII §48)
            bool isBandit3 = actor.Stats.HasSpecialization(LivreXISpecializations.TirPrepareInterruption)
                || actor.Stats.HasSpecialization(LivreXISpecializations.PriseOtage)
                || (actor.Stats.Name != null && (actor.Stats.Name.IndexOf("Bandit #3", StringComparison.OrdinalIgnoreCase) >= 0 || actor.Stats.Name.IndexOf("Bandit 3", StringComparison.OrdinalIgnoreCase) >= 0));

            if (isBandit3)
            {
                // Prise d'otage (6 PA, arme sur tempe de cible adjacente à terre ou essoufflée)
                if (ap >= 6 && target != null && target.Stats != null && target.Stats.IsAlive && dist <= 1)
                {
                    bool isEligibleTarget = target.Stats.ActiveStatus.HasFlag(StatusEffect.ATerre)
                        || target.Stats.Essoufflement >= (target.Stats.Attributes.Constitution / 2)
                        || target.Stats.Essoufflement > 0;

                    if (isEligibleTarget && !LivreXIPNJState.IsHostage(target.Stats))
                    {
                        if (actor.Stats.ConsumeActionPoints(6))
                        {
                            target.Stats.ApplyStatus(StatusEffect.Immobilise, 2);
                            target.Stats.ApplyStatus(StatusEffect.Destabilise, 2);
                            LivreXIPNJState.RegisterHostage(actor.Stats, target.Stats);

                            visual?.SpawnFloatingText("🛑 [PRISE D'OTAGE] Arme sur tempe (-6 PA)", Color.red);
                            var tgtVis = target.GetComponent<TacticalUnitVisual>();
                            tgtVis?.TriggerHitFlash();
                            tgtVis?.SpawnFloatingText("🛑 EN OTAGE ! (Immobilisé)", Color.red);
                            _arena?.Log($"🛑 <b>{actor.Stats.Name}</b> applique son arme sur la tempe de <b>{target.Stats.Name}</b> (-6 PA, <b>PRISE D'OTAGE</b>) ! La cible est immobilisée.");
                            return true;
                        }
                    }
                }

                // Tir préparé en interruption (réserve 4 PA pour tir réflexe dès rupture de couvert ou canalisation)
                if (ap >= 4 && !OverwatchState.IsWatching(actor.Stats))
                {
                    bool isRanged = HasRangedWeapon(actor, out int maxRange, out _, out _);
                    if (isRanged)
                    {
                        CoverType targetCover = target != null ? GetCoverLevel(actor.CurrentCoords, target.CurrentCoords, target.FootprintType, actor.FootprintType, actor) : CoverType.None;
                        bool targetProtected = (targetCover == CoverType.Half || targetCover == CoverType.ThreeQuarters || targetCover == CoverType.Full);

                        if (targetProtected || dist > maxRange / 2 || ap == 4)
                        {
                            if (OverwatchState.TryEnter(actor.Stats, maxRange, 4, out _))
                            {
                                LivreXIPNJState.RegisterPreparedInterruption(actor.Stats);
                                visual?.SpawnFloatingText("🎯 [BANDIT #3] Tir Préparé (-4 PA)", Color.yellow);
                                _arena?.Log($"🎯 <b>{actor.Stats.Name}</b> réserve 4 PA pour un <b>Tir Préparé en Interruption</b> (réflexe immédiat dès rupture de couvert adverse) !");
                                return true;
                            }
                        }
                    }
                }
            }

            // 2. ILLUMO (Livre XII §51)
            bool isIllumo = actor.Stats.HasSpecialization(LivreXISpecializations.CombustionSpontanee)
                || actor.Stats.HasSpecialization(LivreXISpecializations.FouleeCendres)
                || (actor.Stats.Name != null && actor.Stats.Name.IndexOf("Illumo", StringComparison.OrdinalIgnoreCase) >= 0);

            if (isIllumo)
            {
                // Foulée de cendres (2 PA, téléport 6 cases + Aveuglé sur ennemis au départ)
                bool shouldStride = (dist <= 1 && ap >= 2) || (actor.Stats.CurrentHealth < actor.Stats.MaxHealth * 0.45f && ap >= 2);
                if (shouldStride && _grid != null)
                {
                    HexCoordinates? escapeHex = FindCinderStrideHex(actor, target, 6);
                    if (escapeHex.HasValue && !escapeHex.Value.Equals(actor.CurrentCoords))
                    {
                        if (actor.Stats.ConsumeActionPoints(2))
                        {
                            HexCoordinates origin = actor.CurrentCoords;

                            for (int i = 0; i < _cachedUnits.Count; i++)
                            {
                                var other = _cachedUnits[i];
                                if (other == null || other == actor || other.Stats == null || !other.Stats.IsAlive) continue;
                                if (other.IsPlayerControlled == actor.IsPlayerControlled && _mode != CombatAIMode.FullAuto) continue;

                                if (origin.DistanceTo(other.CurrentCoords) <= 1)
                                {
                                    other.Stats.ApplyStatus(StatusEffect.Aveugle, 1);
                                    other.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("🕶️ AVEUGLÉ (Cendres)", Color.yellow);
                                }
                            }

                            actor.TeleportTo(escapeHex.Value, _grid);

                            visual?.SpawnFloatingText("💨 [ILLUMO] Foulée de Cendres (-2 PA)", new Color(0.85f, 0.85f, 0.85f));
                            _arena?.Log($"💨 <b>{actor.Stats.Name}</b> s'évanouit dans une <b>Foulée de Cendres</b> (-2 PA) ➔ Téléportation à 6 cases, déflagration de cendres aveuglante !");
                            return true;
                        }
                    }
                }

                // Combustion spontanée sans jet projectile (4 PA, 18m = 9 cases, 5 bruts + EnFeu)
                if (ap >= 4 && target != null && target.Stats != null && target.Stats.IsAlive)
                {
                    int combustionMaxRange = 9;
                    if (dist <= combustionMaxRange && HasLineOfSight(actor.CurrentCoords, target.CurrentCoords))
                    {
                        if (actor.Stats.ConsumeActionPoints(4))
                        {
                            int rawDmg = 5;
                            int rem = target.Stats.AbsorbShield(rawDmg);
                            if (rem > 0)
                            {
                                if (target.Stats.CurrentHealth - rem <= 0)
                                    target.Stats.EvaluateFatalBlow(BodyPart.Torse, rem);
                                else
                                    target.Stats.CurrentHealth -= rem;
                            }

                            target.Stats.ApplyStatus(StatusEffect.EnFeu, durationInTurns: 2, residualDamage: 3);

                            visual?.SpawnFloatingText("🔥 [ILLUMO] Combustion Spontanée (-4 PA)", new Color(1f, 0.4f, 0f));
                            var tgtVis = target.GetComponent<TacticalUnitVisual>();
                            tgtVis?.TriggerHitFlash();
                            tgtVis?.SpawnFloatingText("🔥 -5 Dégâts Bruts [EN FEU]", Color.red);
                            _arena?.Log($"🔥 <b>{actor.Stats.Name}</b> déclenche une <b>Combustion Spontanée</b> sur <b>{target.Stats.Name}</b> (-4 PA, 5 bruts sans jet projectile) ➔ Statut [EnFeu] appliqué !");
                            return true;
                        }
                    }
                }
            }

            // 3. DRONES & MOLOSSES (Livre XII §51)
            bool isMolosse = actor.Stats.HasSpecialization(LivreXISpecializations.MorsureHydraulique)
                || (actor.Stats.Name != null && actor.Stats.Name.IndexOf("Molosse", StringComparison.OrdinalIgnoreCase) >= 0);

            if (isMolosse && dist <= 1 && ap >= 2 && target != null && target.Stats != null && target.Stats.IsAlive && !LivreXIPNJState.IsBittenByHydraulicJaw(target.Stats))
            {
                if (actor.Stats.ConsumeActionPoints(2))
                {
                    int dmg = 2;
                    int rem = target.Stats.AbsorbShield(dmg);
                    if (rem > 0)
                    {
                        if (target.Stats.CurrentHealth - rem <= 0)
                            target.Stats.EvaluateFatalBlow(BodyPart.Jambes, rem);
                        else
                            target.Stats.CurrentHealth -= rem;
                    }

                    target.Stats.ApplyStatus(StatusEffect.Immobilise, 2);
                    LivreXIPNJState.RegisterHydraulicBite(actor.Stats, target.Stats);

                    visual?.SpawnFloatingText("🦷 [MOLOSSE] Morsure Hydraulique (-2 PA)", new Color(1f, 0.25f, 0.1f));
                    var tgtVis = target.GetComponent<TacticalUnitVisual>();
                    tgtVis?.TriggerHitFlash();
                    tgtVis?.SpawnFloatingText("🔒 MÂCHOIRE VERROUILLÉE (Immobilisé, -2 PV)", Color.red);
                    _arena?.Log($"🦷 <b>{actor.Stats.Name}</b> referme sa <b>Morsure Hydraulique</b> sur <b>{target.Stats.Name}</b> (-2 PA) ➔ Mâchoire verrouillée : Immobilisé + 2 dégâts auto/tour !");
                    return true;
                }
            }

            bool isDrone = actor.Stats.HasSpecialization(LivreXISpecializations.FlashAveuglant)
                || actor.Stats.HasSpecialization(LivreXISpecializations.BaliseAppelReseau)
                || (actor.Stats.Name != null && actor.Stats.Name.IndexOf("Drone", StringComparison.OrdinalIgnoreCase) >= 0);

            if (isDrone)
            {
                // Balise d'appel de renforts réseau (3 PA, transmission télémétrique globale)
                if (ap >= 3 && !LivreXIPNJState.HasBeaconTriggered(actor.Stats))
                {
                    if (actor.Stats.ConsumeActionPoints(3))
                    {
                        LivreXIPNJState.SetBeaconTriggered(actor.Stats);
                        int round = _turnManager != null ? _turnManager.CurrentRound : 1;
                        AlertSquadToNoise(actor.CurrentCoords, round, actor.IsPlayerControlled);

                        for (int i = 0; i < _cachedUnits.Count; i++)
                        {
                            var ally = _cachedUnits[i];
                            if (ally == null || ally.Stats == null || !ally.Stats.IsAlive) continue;
                            if (ally.IsPlayerControlled != actor.IsPlayerControlled) continue;

                            ally.Stats.CurrentActionPoints = Mathf.Min(ally.Stats.MaxActionPoints, ally.Stats.CurrentActionPoints + 1);
                            ally.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("📡 LIAISON RÉSEAU (+1 PA)", Color.cyan);
                        }

                        visual?.SpawnFloatingText("📡 [DRONE] Balise Appel Réseau (-3 PA)", Color.cyan);
                        _arena?.Log($"📡 <b>{actor.Stats.Name}</b> active sa <b>Balise d'Appel Réseau</b> (-3 PA) : télémétrie transmise, coordination d'escouade (+1 PA) !");
                        return true;
                    }
                }

                // Flash aveuglant (2 PA, rayon de 3 hexagones)
                if (ap >= 2)
                {
                    int enemiesBlinded = 0;
                    for (int i = 0; i < _cachedUnits.Count; i++)
                    {
                        var enemy = _cachedUnits[i];
                        if (enemy == null || enemy.Stats == null || !enemy.Stats.IsAlive) continue;
                        if (enemy.IsPlayerControlled == actor.IsPlayerControlled && _mode != CombatAIMode.FullAuto) continue;

                        int d = actor.CurrentCoords.DistanceTo(enemy.CurrentCoords);
                        if (d <= 3 && !enemy.Stats.ActiveStatus.HasFlag(StatusEffect.Aveugle))
                        {
                            enemiesBlinded++;
                        }
                    }

                    if (enemiesBlinded > 0)
                    {
                        if (actor.Stats.ConsumeActionPoints(2))
                        {
                            for (int i = 0; i < _cachedUnits.Count; i++)
                            {
                                var enemy = _cachedUnits[i];
                                if (enemy == null || enemy.Stats == null || !enemy.Stats.IsAlive) continue;
                                if (enemy.IsPlayerControlled == actor.IsPlayerControlled && _mode != CombatAIMode.FullAuto) continue;

                                int d = actor.CurrentCoords.DistanceTo(enemy.CurrentCoords);
                                if (d <= 3)
                                {
                                    enemy.Stats.ApplyStatus(StatusEffect.Aveugle, 1);
                                    enemy.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("🕶️ AVEUGLÉ (Flash Drone)", Color.yellow);
                                }
                            }

                            visual?.SpawnFloatingText("⚡ [DRONE] Flash Aveuglant (-2 PA)", Color.white);
                            _arena?.Log($"⚡ <b>{actor.Stats.Name}</b> déclenche un <b>Flash Aveuglant</b> stroboscopique (-2 PA) ➔ Ennemis éblouis (-4 Tir à distance, vision nulle) !");
                            return true;
                        }
                    }
                }
            }

            // 4. BOSS TITANS (Livre XII §49-50 - RD-087)
            if (TryExecuteTitanBossManeuver(actor, target, dist, visual)) return true;

            return false;
        }

        private HexCoordinates? FindCinderStrideHex(TacticalUnit actor, TacticalUnit threat, int maxDist)
        {
            if (_grid == null) return null;
            HexCoordinates bestHex = actor.CurrentCoords;
            float bestScore = float.MinValue;

            for (int dq = -maxDist; dq <= maxDist; dq++)
            {
                int rMin = Mathf.Max(-maxDist, -dq - maxDist);
                int rMax = Mathf.Min(maxDist, -dq + maxDist);

                for (int dr = rMin; dr <= rMax; dr++)
                {
                    var hex = new HexCoordinates(actor.CurrentCoords.Q + dq, actor.CurrentCoords.R + dr);
                    int dist = actor.CurrentCoords.DistanceTo(hex);
                    if (dist < 3 || dist > maxDist) continue;

                    var node = _grid.GetNode(hex);
                    if (node == null || !node.IsWalkable || node.IsOccupied) continue;

                    int dThreat = threat != null ? hex.DistanceTo(threat.CurrentCoords) : dist;
                    float score = dThreat * 10f;
                    CoverType cover = threat != null ? CoverSystem.EvaluateCover(threat.CurrentCoords, hex, _grid) : CoverType.None;
                    if (cover == CoverType.Half) score += 20f;
                    if (cover == CoverType.ThreeQuarters || cover == CoverType.Full) score += 35f;

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestHex = hex;
                    }
                }
            }

            return bestScore > float.MinValue ? bestHex : null;
        }
    }
}

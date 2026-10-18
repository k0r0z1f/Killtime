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
        // KITS BOSS TITANS (LIVRE XI §45-46 - RD-087)
        // Garde par nom/spécialisation comme Bandit/Illumo (RD-088) ; calculs purs
        // dans TitanBossMechanics, états dans TitanBossState. Appelé depuis
        // TryExecuteLivreXIManeuver (SupportLivreXI), avant les routines génériques.
        // =========================================================================
        private bool TryExecuteTitanBossManeuver(TacticalUnit actor, TacticalUnit target, int dist, TacticalUnitVisual visual)
        {
            if (_arena != null && _arena.IsResolving) return false;
            if (actor?.Stats == null || !actor.Stats.IsAlive) return false;
            int ap = actor.Stats.CurrentActionPoints;

            bool isBeast = TitanBossMechanics.IsBeastOfHybris(actor.Stats)
                || (actor.Stats.Name != null && (actor.Stats.Name.IndexOf("Hybris", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || actor.Stats.Name.IndexOf("Bête d'", System.StringComparison.OrdinalIgnoreCase) >= 0));
            bool isMinulican = TitanBossMechanics.IsMinulican(actor.Stats)
                || (actor.Stats.Name != null && actor.Stats.Name.IndexOf("Minulican", System.StringComparison.OrdinalIgnoreCase) >= 0);
            bool isDisciple = TitanBossMechanics.IsDiscipleDeTerre(actor.Stats)
                || (actor.Stats.Name != null && actor.Stats.Name.IndexOf("Disciple de Terre", System.StringComparison.OrdinalIgnoreCase) >= 0);

            if (!isBeast && !isMinulican && !isDisciple) return false;

            // Réglages JSON (bossAI, RD-087 suite) ; défauts = consts à JSON égal.
            var cfg = TitanBossMechanics.ParamsFor(actor.Stats);

            // ---- BÊTE D'HYBRIS : Balayage de griffes triple-cible (2-3 PA) ----
            if (isBeast)
            {
                var sweepFoes = GatherTitanBossFoes(actor, actor.CurrentCoords, cfg.sweepRange, cfg.sweepMaxTargets, false);
                if (sweepFoes.Count > 0)
                {
                    int cost = TitanBossMechanics.ClawSweepCostFor(actor.Stats, sweepFoes.Count);
                    if (ap >= cost && actor.Stats.ConsumeActionPoints(cost))
                    {
                        for (int i = 0; i < sweepFoes.Count; i++)
                        {
                            DealTitanBossDamage(sweepFoes[i], cfg.sweepDamage, BodyPart.Torse,
                                StatusEffect.ATerre, cfg.sweepGroundedTurns,
                                $"-{cfg.sweepDamage} BALAYAGE [À TERRE]");
                        }
                        visual?.SpawnFloatingText($"🪓 [BÊTE] Balayage de Griffes en Arc (-{cost} PA)", new Color(1f, 0.45f, 0.1f));
                        _arena?.Log($"🪓 <b>{actor.Stats.Name}</b> fauche <b>{sweepFoes.Count} cible(s)</b> d'un <b>Balayage de Griffes en Arc</b> (-{cost} PA) : {cfg.sweepDamage} dégâts + projection {cfg.sweepKnockback} case + À Terre !");
                        _arena?.RecordChronoSnapshot($"Balayage de Griffes : {actor.Stats.Name} ({sweepFoes.Count} cibles)");
                        return true;
                    }
                }

                // ---- Charge Tellurique des Falaises (5 PA, 3-10 cases + effondrement) ----
                if (ap >= cfg.chargeCost && target != null && target.Stats != null && target.Stats.IsAlive
                    && TitanBossMechanics.CanTelluricChargeFor(actor.Stats, dist) && HasLineOfSight(actor.CurrentCoords, target.CurrentCoords))
                {
                    if (_pathfinder == null && _grid != null) _pathfinder = new HexPathfinder(_grid);
                    var path = _pathfinder != null
                        ? _pathfinder.FindChargePath(actor.CurrentCoords, target.CurrentCoords, ap, cfg.chargeCost, out _, cfg.chargeMinRange, actor.FootprintType)
                        : null;
                    if (path != null && path.Count > 0 && actor.Stats.ConsumeActionPoints(cfg.chargeCost))
                    {
                        _arena.ExecuteChargeAttack(actor, target, BodyPart.Torse, 0, 0);
                        // Zone d'effondrement au sol à l'arrivée : voisins du point de chute.
                        var landed = GatherTitanBossFoes(actor, actor.CurrentCoords, 1, 99, false);
                        for (int i = 0; i < landed.Count; i++)
                        {
                            DealTitanBossDamage(landed[i], cfg.chargeCollapseDamage, BodyPart.Jambes,
                                StatusEffect.ATerre, cfg.chargeCollapseGroundedTurns,
                                $"-{cfg.chargeCollapseDamage} EFFONDREMENT");
                        }
                        visual?.SpawnFloatingText($"⛰️ [BÊTE] Charge Tellurique (-{cfg.chargeCost} PA)", new Color(0.7f, 0.5f, 0.3f));
                        _arena?.Log($"⛰️ <b>{actor.Stats.Name}</b> percute la ligne d'une <b>Charge Tellurique des Falaises</b> (-{cfg.chargeCost} PA) : le sol s'effondre à l'arrivée ({landed.Count} victime(s), {cfg.chargeCollapseDamage} dégâts) !");
                        _arena?.RecordChronoSnapshot($"Charge Tellurique : {actor.Stats.Name} -> {target.Stats.Name}");
                        return true;
                    }
                }
            }

            // ---- MINULICAN : Éclairs de Néant en Chaîne (4 PA, ≤ 3 cibles à 15 cases) ----
            if (isMinulican)
            {
                if (ap >= cfg.chainCost)
                {
                    var chainFoes = GatherTitanBossFoes(actor, actor.CurrentCoords, cfg.chainMaxRange, cfg.chainMaxTargets, true);
                    if (chainFoes.Count > 0 && actor.Stats.ConsumeActionPoints(cfg.chainCost))
                    {
                        for (int i = 0; i < chainFoes.Count; i++)
                        {
                            DealTitanBossDamage(chainFoes[i], cfg.chainDamage, BodyPart.Torse,
                                StatusEffect.Destabilise, cfg.chainDestabilisedTurns,
                                $"-{cfg.chainDamage} NÉANT [DÉSTABILISÉ]");
                        }
                        visual?.SpawnFloatingText($"⚡ [MINULICAN] Éclairs de Néant en Chaîne (-{cfg.chainCost} PA)", new Color(0.6f, 0.2f, 1f));
                        _arena?.Log($"⚡ <b>{actor.Stats.Name}</b> déchaîne des <b>Éclairs de Néant en Chaîne</b> (-{cfg.chainCost} PA) sur <b>{chainFoes.Count} cible(s)</b> : {cfg.chainDamage} dégâts + Déstabilisé !");
                        _arena?.RecordChronoSnapshot($"Éclairs de Néant : {actor.Stats.Name} ({chainFoes.Count} cibles)");
                        return true;
                    }
                }

                // ---- Saut Quantique Déphasé (3 PA, téléport ≤ 6 cases + revers +2 EC) ----
                bool shouldLeap = (dist <= 1 && ap >= cfg.leapCost)
                    || (actor.Stats.CurrentHealth < actor.Stats.MaxHealth * 0.45f && ap >= cfg.leapCost);
                if (shouldLeap && _grid != null)
                {
                    HexCoordinates? leapHex = FindCinderStrideHex(actor, target, cfg.leapMaxRange);
                    if (leapHex.HasValue && !leapHex.Value.Equals(actor.CurrentCoords)
                        && actor.Stats.ConsumeActionPoints(cfg.leapCost))
                    {
                        actor.TeleportTo(leapHex.Value, _grid);
                        var landingFoes = GatherTitanBossFoes(actor, leapHex.Value, 1, 1, false);
                        if (landingFoes.Count > 0)
                        {
                            int riposte = TitanBossMechanics.QuantumRiposteDamage(actor.Stats, System.Math.Max(3, actor.Stats.Attributes.Force));
                            DealTitanBossDamage(landingFoes[0], riposte, BodyPart.Torse,
                                StatusEffect.None, 0, $"-{riposte} REVERS [+{cfg.leapRiposteBonusEC} EC]");
                            _arena?.Log($"🌀 <b>{actor.Stats.Name}</b> se déphase d'un <b>Saut Quantique</b> (-{cfg.leapCost} PA) et frappe en revers à l'arrivée (+{cfg.leapRiposteBonusEC} EC, {riposte} dégâts sur <b>{landingFoes[0].Stats.Name}</b>) !");
                        }
                        else
                        {
                            _arena?.Log($"🌀 <b>{actor.Stats.Name}</b> se déphase d'un <b>Saut Quantique</b> (-{cfg.leapCost} PA) ➔ repositionnement à {cfg.leapMaxRange} cases max !");
                        }
                        visual?.SpawnFloatingText($"🌀 [MINULICAN] Saut Quantique (-{cfg.leapCost} PA)", new Color(0.6f, 0.2f, 1f));
                        return true;
                    }
                }
            }

            // ---- DISCIPLE DE TERRE : Poing Tectonique (3 PA, 7D, pulvérise structures) ----
            if (isDisciple && ap >= cfg.fistCost
                && target != null && target.Stats != null && target.Stats.IsAlive && dist <= 1)
            {
                if (actor.Stats.ConsumeActionPoints(cfg.fistCost))
                {
                    DealTitanBossDamage(target, cfg.fistDamage, BodyPart.Torse,
                        StatusEffect.Destabilise, 1, $"-{cfg.fistDamage} TECTONIQUE");
                    visual?.SpawnFloatingText($"🪨 [DISCIPLE] Poing Tectonique (-{cfg.fistCost} PA)", new Color(0.75f, 0.55f, 0.35f));
                    var tgtVis = target.GetComponent<TacticalUnitVisual>();
                    tgtVis?.SpawnFloatingText($"💥 -{cfg.fistDamage}D (pulvérise structures)", Color.red);
                    _arena?.Log($"🪨 <b>{actor.Stats.Name}</b> écrase <b>{target.Stats.Name}</b> d'un <b>Poing Tectonique</b> (-{cfg.fistCost} PA, {cfg.fistDamage} dégâts, pulvérise les structures) !");
                    _arena?.RecordChronoSnapshot($"Poing Tectonique : {actor.Stats.Name} -> {target.Stats.Name}");
                    return true;
                }
            }

            return false;
        }

        private List<TacticalUnit> GatherTitanBossFoes(TacticalUnit actor, HexCoordinates center, int range, int max, bool needLOS)
        {
            var foes = new List<TacticalUnit>();
            if (actor == null) return foes;
            for (int i = 0; i < _cachedUnits.Count; i++)
            {
                var u = _cachedUnits[i];
                if (u == null || u == actor || u.Stats == null || !u.Stats.IsAlive) continue;
                bool hostile = actor.IsPlayerControlled ? !u.IsPlayerControlled : u.IsPlayerControlled;
                if (_mode == CombatAIMode.FullAuto && u.IsPlayerControlled != actor.IsPlayerControlled) hostile = true;
                if (!hostile) continue;
                if (center.DistanceTo(u.CurrentCoords) > range) continue;
                if (needLOS && !HasLineOfSight(actor.CurrentCoords, u.CurrentCoords)) continue;
                foes.Add(u);
            }
            foes.Sort((a, b) => center.DistanceTo(a.CurrentCoords).CompareTo(center.DistanceTo(b.CurrentCoords)));
            if (foes.Count > max) foes.RemoveRange(max, foes.Count - max);
            return foes;
        }

        private void DealTitanBossDamage(TacticalUnit victim, int dmg, BodyPart part, StatusEffect status, int statusTurns, string floatText)
        {
            if (victim?.Stats == null || !victim.Stats.IsAlive) return;
            int rem = victim.Stats.AbsorbShield(dmg);
            if (rem > 0)
            {
                if (victim.Stats.CurrentHealth - rem <= 0)
                    victim.Stats.EvaluateFatalBlow(part, rem);
                else
                    victim.Stats.CurrentHealth -= rem;
            }
            if (status != StatusEffect.None)
                victim.Stats.ApplyStatus(status, statusTurns);
            var vis = victim.GetComponent<TacticalUnitVisual>();
            vis?.TriggerHitFlash();
            vis?.SpawnFloatingText(floatText, Color.red);
            if (!victim.Stats.IsAlive)
            {
                vis?.TriggerFallingBackDeath();
                _arena?.AutoTargetNextAlive();
            }
        }
    }
}

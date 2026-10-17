using System.Collections.Generic;
using UnityEngine;
using Killtime.Core.Character;
using Killtime.Core.Combat;
using Killtime.Tactics.CombatUI;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;

namespace Killtime.Tactics.AI
{
    /// <summary>
    /// Duo-Tech automatique (RD-105) : l'IA joue Sillage Igné sans tissage souris.
    /// Géométrie dans AIDuoTechSolver (pur, testé), paiement atomique + limite
    /// 1/round dans DuoTechRegistry (inchangés), téléportation + dégâts ici.
    /// Lacet / Fournaise gardent le tissage manuel (boucle / triangle non
    /// auto-géométrisables en petite tâche).
    /// </summary>
    public partial class TacticalAIController : MonoBehaviour
    {
        private bool TryExecuteDuoTechAuto(TacticalUnit actor, TacticalPosture posture, TacticalUnitVisual visual)
        {
            if (actor?.Stats == null || !actor.Stats.IsAlive) return false;
            if (_arena == null || _arena.IsResolving) return false;
            if (_defaultPersonality == AIPersonality.Survivor) return false;
            if (!DuoTechRegistry.IsDuoMember(actor.Stats)) return false;

            TacticalUnit partner = null;
            int bestPartnerDist = int.MaxValue;
            for (int i = 0; i < _cachedUnits.Count; i++)
            {
                var u = _cachedUnits[i];
                if (u == null || u == actor || u.Stats == null || !u.Stats.IsAlive) continue;
                if (u.IsPlayerControlled != actor.IsPlayerControlled) continue;
                if (!DuoTechRegistry.AreComplementary(actor.Stats, u.Stats)) continue;
                int d = TitanFootprint.MinDistanceBetweenUnits(
                    actor.CurrentCoords, actor.FootprintType, u.CurrentCoords, u.FootprintType);
                if (d <= AIDuoTechSolver.MaxBinomeDist && d < bestPartnerDist)
                {
                    partner = u;
                    bestPartnerDist = d;
                }
            }
            if (partner == null) return false;

            int round = (_turnManager != null && !_turnManager.IsInExploration)
                ? _turnManager.CurrentRound : 0;
            var def = DuoTechRegistry.GetDef(DuoTechId.SillageIgne);
            if (!DuoTechRegistry.CanDeclare(actor.Stats, partner.Stats, def, round, out _))
                return false;

            var foes = new List<TacticalUnit>();
            var foeCoords = new List<HexCoordinates>();
            for (int i = 0; i < _cachedUnits.Count; i++)
            {
                var u = _cachedUnits[i];
                if (u == null || u.Stats == null || !u.Stats.IsAlive) continue;
                if (u.IsPlayerControlled == actor.IsPlayerControlled) continue;
                foes.Add(u);
                foeCoords.Add(u.CurrentCoords);
            }
            if (foes.Count < AIDuoTechSolver.MinTargets) return false;

            TacticalUnit minaUnit = DuoTechRegistry.IsMinaMember(actor.Stats) ? actor : partner;
            TacticalUnit lucasUnit = DuoTechRegistry.IsLucasMember(actor.Stats) ? actor : partner;

            if (!AIDuoTechSolver.TryPlanSillage(
                    minaUnit.CurrentCoords, lucasUnit.CurrentCoords, foeCoords, out var plan))
                return false;

            bool allIn = posture == TacticalPosture.AllInLethal
                || _defaultPersonality == AIPersonality.Aggressive;
            if (!AIDuoTechSolver.ShouldExecuteSillage(plan.Grade, allIn)) return false;

            if (!DuoTechRegistry.TryDeclarePayment(actor.Stats, partner.Stats, def, round))
                return false;

            bool arrived = false;
            if (_grid != null)
            {
                arrived = minaUnit.TeleportTo(plan.DashDest, _grid);
                if (!arrived && _grid.TryFindNearestFreeCell(plan.DashDest, out HexCoordinates free, 8))
                    arrived = minaUnit.TeleportTo(free, _grid);
            }
            if (!arrived)
                _arena.Log($"💨 {minaUnit.Stats.Name} : dash auto hors grille, frappe sur place !");

            var targetStats = new List<CharacterStats>(plan.TargetIndexes.Count);
            var targetUnits = new List<TacticalUnit>(plan.TargetIndexes.Count);
            for (int i = 0; i < plan.TargetIndexes.Count; i++)
            {
                int idx = plan.TargetIndexes[i];
                if (idx < 0 || idx >= foes.Count) continue;
                if (foes[idx].Stats == null || !foes[idx].Stats.IsAlive) continue;
                targetStats.Add(foes[idx].Stats);
                targetUnits.Add(foes[idx]);
            }
            if (targetStats.Count == 0)
            {
                _arena.Log("🔥 Sillage auto : aucune cible en vie sur le sillage (PA perdus).");
                _arena.RecordChronoSnapshot($"Sillage auto raté : {minaUnit.Stats.Name} + {lucasUnit.Stats.Name}");
                return true;
            }

            DuoTechRegistry.ResolveSillage(
                minaUnit.Stats, lucasUnit.Stats, targetStats, plan.Grade,
                msg => _arena?.Log(msg));
            for (int i = 0; i < targetUnits.Count; i++)
                targetUnits[i].GetComponent<TacticalUnitVisual>()?.TriggerHitFlash();
            visual?.SpawnFloatingText(
                $"🔥 SILLAGE AUTO [{plan.Grade}] !", new Color(1f, 0.45f, 0.1f));
            _arena.RecordChronoSnapshot(
                $"Sillage auto : {minaUnit.Stats.Name} + {lucasUnit.Stats.Name} [{plan.Grade}]");
            return true;
        }
    }
}

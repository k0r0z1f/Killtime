using System;
using Killtime.Core.Character;
using Killtime.Core.Dice;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Visibility;

namespace Killtime.Core.Combat
{
    /// <summary>
    /// Course-poursuite RD-084 §21.1 (round proie vs poursuivants) — extrait sans modification depuis SpecialManeuverService.cs.
    /// </summary>
    public sealed partial class SpecialManeuverService
    {
        /// <summary>
        /// RD-084 : Round de course-poursuite (Livre V §21.1). La proie effectue
        /// UNE unique épreuve d'Athlétisme (mises une seule fois), chaque
        /// poursuivant effectue la sienne et se compare à ce total :
        /// réussite de la proie = distance maintenue (≥ 1 case), faillite =
        /// le poursuivant gagne du terrain (+1 faillite cumulée). À 4 faillites
        /// cumulées : arrêt et capture immédiate (PursuitState + Immobilisé).
        /// </summary>
        public PursuitRoundResult ResolvePursuitRound(
            CharacterStats prey,
            System.Collections.Generic.IList<CharacterStats> pursuers,
            int preyBonusAP = 0,
            int preyPE = 0,
            bool pursuersAutoStakes = true,
            System.Collections.Generic.IList<int> pursuerBonusAP = null,
            System.Collections.Generic.IList<int> pursuerPE = null)
        {
            var result = new PursuitRoundResult { Prey = prey };
            var cfg = Rules.CoreRulesConfig.Instance;
            PursuitState.FailuresToCapture = cfg != null ? cfg.PursuitFailuresToCapture : 4;
            PursuitState.MinKeptDistance = cfg != null ? cfg.PursuitMinDistance : 1;
            result.FailuresToCapture = PursuitState.FailuresToCapture;

            var alive = new System.Collections.Generic.List<CharacterStats>();
            if (pursuers != null)
            {
                for (int i = 0; i < pursuers.Count; i++)
                {
                    var p = pursuers[i];
                    if (p != null && p != prey && p.IsAlive) alive.Add(p);
                }
            }

            if (prey == null || !prey.IsAlive)
            {
                result.CombatLog = "⚠️ Poursuite impossible : proie hors de combat.";
                result.TotalFailures = 0;
                return result;
            }
            if (alive.Count == 0)
            {
                result.CombatLog = $"⚠️ Poursuite impossible : aucun poursuivant valide pour {prey.Name}.";
                result.TotalFailures = PursuitState.GetFailures(prey);
                return result;
            }

            // Unique épreuve de la proie (notée puis opposée à chacun).
            DiceType preyDie = prey.GetSkillDie(SkillType.Athletisme, true);
            int preyStatus = prey.GetStatusModifier(SkillType.Athletisme, isOffensive: true);
            int preyPA = CombatDuelShared.SpendBonusPA(prey, Math.Max(0, preyBonusAP));
            int preyPEPaid = prey.SpendDuelPE(Math.Max(0, preyPE));
            int preyCommitted = preyPA + preyPEPaid * cfg.DuelPEBonusPerPoint;
            var preyRoll = _ctx.DiceRoller.Roll(preyDie, preyStatus + preyCommitted, cfg.StandardTargetDC);
            result.PreyRoll = preyRoll;

            PursuitState.TrackPursuers(prey, alive);
            int added = 0;
            string log = $"🏃 <b>COURSE-POURSUITE</b> : {prey.Name} fuit ({alive.Count} poursuivant(s)) — Athlétisme vs Athlétisme\n";
            log += $"   🦌 Proie {prey.Name} : [d{preyDie} tirage {preyRoll.RawRoll} + mod {preyRoll.Modifier} = <b>{preyRoll.Total}</b>]\n";

            for (int i = 0; i < alive.Count; i++)
            {
                var p = alive[i];
                DiceType pDie = p.GetSkillDie(SkillType.Athletisme, true);
                int pStatus = p.GetStatusModifier(SkillType.Athletisme, isOffensive: true);
                int paidPA = 0;
                int paidPE = 0;
                if (pursuersAutoStakes)
                {
                    int expectedPrey = preyRoll.Total;
                    int expectedSelf = (int)Math.Round(CombatDuelShared.DieAverage(pDie)) + pStatus;
                    int need = Math.Max(0, expectedPrey - expectedSelf + 1);
                    paidPA = CombatDuelShared.SpendBonusPA(p, Math.Min(need, Math.Max(0, p.CurrentActionPoints)));
                }
                else
                {
                    int wantPA = (pursuerBonusAP != null && i < pursuerBonusAP.Count) ? Math.Max(0, pursuerBonusAP[i]) : 0;
                    int wantPE = (pursuerPE != null && i < pursuerPE.Count) ? Math.Max(0, pursuerPE[i]) : 0;
                    paidPA = CombatDuelShared.SpendBonusPA(p, wantPA);
                    paidPE = p.SpendDuelPE(wantPE);
                }
                int committed = paidPA + paidPE * cfg.DuelPEBonusPerPoint;
                var pRoll = _ctx.DiceRoller.Roll(pDie, pStatus + committed, cfg.StandardTargetDC);
                int diff = preyRoll.Total - pRoll.Total;
                bool preyWins = diff >= 0;

                var entry = new PursuitDuelEntry
                {
                    Pursuer = p,
                    PursuerRoll = pRoll,
                    Differential = diff,
                    PreyWins = preyWins
                };
                result.Entries.Add(entry);

                if (preyWins)
                {
                    PursuitState.RegisterSuccess(prey, p);
                    log += $"   ✅ vs {p.Name} [{pRoll.Total}] : Diff {diff:+0;-0;0} — <b>DISTANCE MAINTENUE</b> (bloqué à ≥ {PursuitState.MinKeptDistance} case).\n";
                }
                else
                {
                    PursuitState.RegisterFailure(prey, p);
                    added++;
                    log += $"   ❌ vs {p.Name} [{pRoll.Total}] : Diff {diff:+0;-0;0} — <b>TERRAIN GAGNÉ</b> par le poursuivant ! (faillites : {PursuitState.GetFailures(prey)}/{PursuitState.FailuresToCapture})\n";
                }
                log += _progression.ApplyOpposedProgression(prey, SkillType.Athletisme, true, p, SkillType.Athletisme, true, diff);
            }

            result.FailuresAdded = added;
            result.TotalFailures = PursuitState.GetFailures(prey);
            result.Caught = PursuitState.IsCaught(prey);
            if (result.Caught)
            {
                log += $"   🛑 <b>RÈGLE D'ARRÊT AUX {PursuitState.FailuresToCapture} FAILLITES</b> : {prey.Name} est <b>DÉFINITIVEMENT RATTRAPÉ(E) ET ACCULÉ(E)</b> — arrêt et capture immédiate ! [Immobilisé]";
            }
            result.CombatLog = log;
            return result;
        }
    }
}

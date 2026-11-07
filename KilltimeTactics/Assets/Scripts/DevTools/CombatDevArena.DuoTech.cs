using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics.AI;
using Killtime.Tactics.Arena;
using Killtime.Tactics.CombatUI;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Core.Character;
using Killtime.Core.Combat;

namespace Killtime.Tactics
{
    // RD-194 : Duo-Tech MJ sans souris (Livre V §21.5 + synergies RD-086).
    // Paiement atomique + tampon 1/round via DuoTechRegistry (inchangé),
    // résolution table aux grades forcés via Resolve* (Classics/Extended/
    // TriFusion). Tissage souris WeaveController inchangé. Dev-only.
    public partial class CombatDevArena : MonoBehaviour, IArenaHost
    {
        // --- Helpers purs (testables EditMode sans scène) ---

        public static bool IsDuoSupported(DuoTechId id)
        {
            switch (id)
            {
                case DuoTechId.SillageIgne:
                case DuoTechId.LacetNytharite:
                case DuoTechId.FournaiseRetardement:
                case DuoTechId.TrempeInversee:
                case DuoTechId.RailgunArtisanale:
                case DuoTechId.CisailleEntropique:
                case DuoTechId.Parallaxe:
                case DuoTechId.MirageDeuxTemps:
                case DuoTechId.UppercutThermobarique:
                case DuoTechId.RempartTellurique:
                case DuoTechId.ZeroTectonique:
                case DuoTechId.TriFusion:
                    return true;
                default:
                    return false;
            }
        }

        public static bool IsTrioDuo(DuoTechId id) => id == DuoTechId.TriFusion;

        public static bool IsThomasPairDuo(DuoTechId id)
            => id == DuoTechId.RempartTellurique || id == DuoTechId.ZeroTectonique;

        /// <summary>Sélecteur toolbar 0/1/2 -> Parfaite/Semi/Ratée (table MJ).</summary>
        public static DuoTechOutcome DuoTechOutcomeFromIndex(int idx)
        {
            if (idx <= 0) return DuoTechOutcome.Perfect;
            if (idx == 1) return DuoTechOutcome.Partial;
            return DuoTechOutcome.Miss;
        }

        public static string DuoTechOutcomeLabel(DuoTechOutcome o)
        {
            switch (o)
            {
                case DuoTechOutcome.Perfect: return "Parfaite";
                case DuoTechOutcome.Partial: return "Semi";
                default: return "Ratée";
            }
        }

        /// <summary>Sélecteur Sillage 0/1/2/3 -> Perfect/Risky/Late/Miss (weave).</summary>
        public static WeaveSyncGrade SillageGradeFromIndex(int idx)
        {
            switch (idx)
            {
                case 0: return WeaveSyncGrade.Perfect;
                case 1: return WeaveSyncGrade.Risky;
                case 2: return WeaveSyncGrade.Late;
                default: return WeaveSyncGrade.Miss;
            }
        }

        public static string SillageGradeLabel(WeaveSyncGrade g)
        {
            switch (g)
            {
                case WeaveSyncGrade.Perfect: return "Perfect (4+3, EnFeu)";
                case WeaveSyncGrade.Risky: return "Risky (4+2, friendly 3 Mina)";
                case WeaveSyncGrade.Late: return "Late (4+1)";
                default: return "Miss (0)";
            }
        }

        /// <summary>Table -> grade Sillage (Semi = Risky, le plus visible au terrain).</summary>
        public static WeaveSyncGrade SillageGradeFromOutcome(DuoTechOutcome o)
        {
            switch (o)
            {
                case DuoTechOutcome.Perfect: return WeaveSyncGrade.Perfect;
                case DuoTechOutcome.Partial: return WeaveSyncGrade.Risky;
                default: return WeaveSyncGrade.Miss;
            }
        }

        public static string DuoTechCostPreview(DuoTechDef def)
        {
            if (def == null) return "Duo inconnu";
            return $"{def.TotalCost} PA (départ {def.DeclareCost} + Mina {def.MinaCost} / Lucas {def.LucasCost} / Thomas {def.ThomasCost})";
        }

        // --- Cycle MJ ---

        public int CurrentDuoRound()
        {
            if (_turnManager == null || _turnManager.IsInExploration) return 0;
            return _turnManager.CurrentRound;
        }

        /// <summary>Initiateurs armables : héros du trio en vie (Lucas/Mina/Thomas).</summary>
        public List<TacticalUnit> ListDuoInitiators()
        {
            var r = new List<TacticalUnit>();
            var all = AllDebugUnits();
            if (all == null) return r;
            for (int i = 0; i < all.Count; i++)
            {
                var u = all[i];
                if (u == null || u.Stats == null || !u.Stats.IsAlive) continue;
                if (DuoTechRegistry.IsTrioMember(u.Stats)) r.Add(u);
            }
            return r;
        }

        private TacticalUnit FindAliveHero(Func<CharacterStats, bool> pred, TacticalUnit exclude = null)
        {
            var all = AllDebugUnits();
            if (all == null) return null;
            for (int i = 0; i < all.Count; i++)
            {
                var u = all[i];
                if (u == null || u == exclude || u.Stats == null || !u.Stats.IsAlive) continue;
                if (pred(u.Stats)) return u;
            }
            return null;
        }

        private List<TacticalUnit> EnemiesOf(TacticalUnit initiator)
        {
            var r = new List<TacticalUnit>();
            var all = AllDebugUnits();
            if (all == null || initiator == null) return r;
            for (int i = 0; i < all.Count; i++)
            {
                var u = all[i];
                if (u == null || u.Stats == null || !u.Stats.IsAlive) continue;
                if (u.IsPlayerControlled == initiator.IsPlayerControlled) continue;
                r.Add(u);
            }
            return r;
        }

        private static List<CharacterStats> ToStats(List<TacticalUnit> units)
        {
            var r = new List<CharacterStats>(units.Count);
            for (int i = 0; i < units.Count; i++)
                if (units[i] != null && units[i].Stats != null && units[i].Stats.IsAlive)
                    r.Add(units[i].Stats);
            return r;
        }

        private void FlashUnits(List<TacticalUnit> units)
        {
            for (int i = 0; i < units.Count; i++)
                units[i]?.GetComponent<TacticalUnitVisual>()?.TriggerHitFlash();
        }

        /// <summary>
        /// MJ sans souris : paiement immédiat atomique (refund visible au log)
        /// puis résolution table au grade forcé. Cibles = tous les ennemis en
        /// vie (sauf mono-cible = cible active, Mirage = aucun). Le placement
        /// (dash, barricade, replacement) reste manuel, comme au tissage.
        /// </summary>
        public bool TryExecuteDebugDuoTech(TacticalUnit initiator, DuoTechId id, DuoTechOutcome outcome, out string message)
        {
            message = "";
            if (!IsDuoSupported(id)) { message = $"⛔ Duo {id} non supporté."; return false; }
            if (id == DuoTechId.SillageIgne)
                return TryExecuteDebugSillage(initiator, SillageGradeFromOutcome(outcome), out message);
            if (initiator == null || initiator.Stats == null || !initiator.Stats.IsAlive)
            {
                message = "⛔ Initiateur invalide ou K.O.";
                Log(message);
                return false;
            }
            var def = DuoTechRegistry.GetDef(id);
            if (def == null) { message = "⛔ Duo inconnu."; return false; }
            int round = CurrentDuoRound();
            var foes = EnemiesOf(initiator);
            TacticalUnit primary = CurrentTarget != null && CurrentTarget.Stats != null && CurrentTarget.Stats.IsAlive
                && CurrentTarget.IsPlayerControlled != initiator.IsPlayerControlled ? CurrentTarget : (foes.Count > 0 ? foes[0] : null);
            if (id != DuoTechId.MirageDeuxTemps && foes.Count == 0)
            {
                message = "⛔ Aucun ennemi en vie — spawnez des mannequins avant d'armer (PA non débités).";
                Log(message);
                return false;
            }

            TacticalUnit partner = null;
            TacticalUnit partnerB = null;
            CharacterStats mina = null, lucas = null, thomas = null;
            if (IsTrioDuo(id))
            {
                if (!FindTrioFor(initiator, out partner, out partnerB))
                {
                    message = "⛔ Tri-Fusion : trio incomplet (Thomas + Mina + Lucas en vie exigés).";
                    Log(message);
                    return false;
                }
                ResolveTrioRoles(initiator, partner, partnerB, out mina, out lucas, out thomas);
                int tInit = initiator.Stats.CurrentActionPoints;
                int tA = partner.Stats.CurrentActionPoints;
                int tB = partnerB.Stats.CurrentActionPoints;
                if (!DuoTechRegistry.CanDeclareTrio(initiator.Stats, partner.Stats, partnerB.Stats, def, round, out string whyT))
                {
                    message = $"⛔ Tri-Fusion refusée : {whyT}";
                    Log(message);
                    return false;
                }
                if (!DuoTechRegistry.TryDeclareTrioPayment(initiator.Stats, partner.Stats, partnerB.Stats, def, round))
                {
                    message = "⛔ Tri-Fusion : paiement refusé (refund auto).";
                    Log(message);
                    return false;
                }
                Log($"⚡ <b>{def.Name}</b> armée : {initiator.Stats.Name} {tInit}->{initiator.Stats.CurrentActionPoints} PA, {partner.Stats.Name} {tA}->{partner.Stats.CurrentActionPoints} PA, {partnerB.Stats.Name} {tB}->{partnerB.Stats.CurrentActionPoints} PA (tampon 1/round au round {round}).");
            }
            else if (IsThomasPairDuo(id))
            {
                partner = FindThomasPairPartner(initiator, id);
                if (partner == null)
                {
                    message = id == DuoTechId.RempartTellurique
                        ? "⛔ Rempart : binôme Thomas+Mina introuvable en vie."
                        : "⛔ Zéro : binôme Thomas+Lucas introuvable en vie.";
                    Log(message);
                    return false;
                }
                ResolvePairRoles(initiator, partner, out mina, out lucas, out thomas);
                int pInit = initiator.Stats.CurrentActionPoints;
                int pPart = partner.Stats.CurrentActionPoints;
                if (!DuoTechRegistry.CanDeclarePair(initiator.Stats, partner.Stats, def, round, out string whyP))
                {
                    message = $"⛔ Duo refusé : {whyP}";
                    Log(message);
                    return false;
                }
                if (!DuoTechRegistry.TryDeclarePairPayment(initiator.Stats, partner.Stats, def, round))
                {
                    message = "⛔ Duo : paiement refusé (refund auto).";
                    Log(message);
                    return false;
                }
                Log($"⚡ <b>{def.Name}</b> armé : {initiator.Stats.Name} {pInit}->{initiator.Stats.CurrentActionPoints} PA, {partner.Stats.Name} {pPart}->{partner.Stats.CurrentActionPoints} PA (tampon 1/round au round {round}).");
            }
            else
            {
                partner = FindAliveHero(s => DuoTechRegistry.AreComplementary(initiator.Stats, s), initiator);
                if (partner == null)
                {
                    message = "⛔ Duo : partenaire complémentaire (Lucas+Mina) introuvable en vie.";
                    Log(message);
                    return false;
                }
                ResolvePairRoles(initiator, partner, out mina, out lucas, out thomas);
                int cInit = initiator.Stats.CurrentActionPoints;
                int cPart = partner.Stats.CurrentActionPoints;
                if (!DuoTechRegistry.CanDeclare(initiator.Stats, partner.Stats, def, round, out string whyC))
                {
                    message = $"⛔ Duo refusé : {whyC}";
                    Log(message);
                    return false;
                }
                if (!DuoTechRegistry.TryDeclarePayment(initiator.Stats, partner.Stats, def, round))
                {
                    message = "⛔ Duo : paiement refusé (refund auto).";
                    Log(message);
                    return false;
                }
                Log($"⚡ <b>{def.Name}</b> armé : {initiator.Stats.Name} {cInit}->{initiator.Stats.CurrentActionPoints} PA, {partner.Stats.Name} {cPart}->{partner.Stats.CurrentActionPoints} PA (tampon 1/round au round {round}).");
            }

            System.Action<string> log = msg => Log(msg);
            string tag = DuoTechOutcomeLabel(outcome);

            switch (id)
            {
                case DuoTechId.LacetNytharite:
                    if (primary == null) { message = "⛔ Lacet : aucun ennemi en vie."; Log(message); return false; }
                    DuoTechRegistry.ResolveLacet(primary.Stats, outcome == DuoTechOutcome.Perfect, log);
                    primary.GetComponent<TacticalUnitVisual>()?.TriggerHitFlash();
                    break;
                case DuoTechId.FournaiseRetardement:
                    if (primary == null) { message = "⛔ Fournaise : aucun ennemi en vie."; Log(message); return false; }
                    DuoTechRegistry.ResolveFournaise(primary.Stats, outcome == DuoTechOutcome.Perfect, log);
                    primary.GetComponent<TacticalUnitVisual>()?.TriggerHitFlash();
                    break;
                case DuoTechId.TrempeInversee:
                    if (foes.Count == 0) { message = "⛔ Trempe : aucun ennemi en vie."; Log(message); return false; }
                    DuoTechRegistry.ResolveTrempe(ToStats(foes), outcome, log);
                    FlashUnits(foes);
                    break;
                case DuoTechId.RailgunArtisanale:
                    if (primary == null) { message = "⛔ Railgun : aucun ennemi en vie."; Log(message); return false; }
                    DuoTechRegistry.ResolveRailgun(primary.Stats, outcome, log);
                    primary.GetComponent<TacticalUnitVisual>()?.TriggerHitFlash();
                    break;
                case DuoTechId.CisailleEntropique:
                    if (foes.Count == 0) { message = "⛔ Cisaille : aucun ennemi en vie."; Log(message); return false; }
                    DuoTechRegistry.ResolveCisaille(ToStats(foes), outcome, log);
                    FlashUnits(foes);
                    break;
                case DuoTechId.Parallaxe:
                    if (primary == null) { message = "⛔ Parallaxe : aucun ennemi en vie."; Log(message); return false; }
                    DuoTechRegistry.ResolveParallaxe(primary.Stats, outcome, log);
                    primary.GetComponent<TacticalUnitVisual>()?.TriggerHitFlash();
                    break;
                case DuoTechId.MirageDeuxTemps:
                    DuoTechRegistry.ResolveMirage(mina, outcome, log);
                    Log("🌫️ Mirage MJ : replacement 3 cases à faire à la main (téléportez Mina).");
                    break;
                case DuoTechId.UppercutThermobarique:
                    if (primary == null)
                    {
                        DuoTechRegistry.ResolveUppercut(mina, lucas, null, DuoTechOutcome.Miss, log);
                    }
                    else
                    {
                        if (!DuoTechRegistry.IsUppercutCondition(primary.Stats))
                            Log($"⬆️ Uppercut MJ : {primary.Stats.Name} hors condition (entravé + ≤50% PV) — whiff appliqué si Ratée.");
                        DuoTechRegistry.ResolveUppercut(mina, lucas, primary.Stats, outcome, log);
                        primary.GetComponent<TacticalUnitVisual>()?.TriggerHitFlash();
                    }
                    break;
                case DuoTechId.RempartTellurique:
                    if (foes.Count == 0) { message = "⛔ Rempart : aucun ennemi en vie."; Log(message); return false; }
                    DuoTechRegistry.ResolveRempart(ToStats(foes), outcome, log);
                    FlashUnits(foes);
                    if (outcome == DuoTechOutcome.Perfect)
                        Log("🛡️ Rempart Parfait : barricade rocheuse (Cover Half) à poser par le MJ sur la grille.");
                    break;
                case DuoTechId.ZeroTectonique:
                    if (foes.Count == 0) { message = "⛔ Zéro : aucun ennemi en vie."; Log(message); return false; }
                    DuoTechRegistry.ResolveZero(ToStats(foes), outcome, log);
                    FlashUnits(foes);
                    break;
                case DuoTechId.TriFusion:
                    if (foes.Count == 0) { message = "⛔ Tri-Fusion : aucun ennemi en vie."; Log(message); return false; }
                    DuoTechRegistry.ResolveTriFusion(mina, lucas, thomas, ToStats(foes), outcome, log);
                    FlashUnits(foes);
                    break;
                default:
                    message = $"⛔ Duo {id} non branché MJ.";
                    Log(message);
                    return false;
            }

            initiator.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText($"[{tag}] {def.Name}", UnityEngine.Color.yellow);
            RecordChronoSnapshot($"{def.Name} MJ [{tag}] : {initiator.Stats.Name}" + (partner != null ? $" + {partner.Stats.Name}" : ""));
            try { _turnManager?.CheckCombatOver(); } catch { /* jamais bloquant */ }
            message = $"{def.Name} [{tag}] résolu sur {(primary != null ? primary.Stats.Name : foes.Count + " ennemis")}.";
            return true;
        }

        /// <summary>
        /// Sillage MJ : même paiement atomique, grade weave forcé
        /// (Perfect/Risky/Late/Miss). Cibles = tous les ennemis en vie.
        /// Dash Mina à faire à la main (téléportez Mina sur la ligne).
        /// </summary>
        public bool TryExecuteDebugSillage(TacticalUnit initiator, WeaveSyncGrade grade, out string message)
        {
            message = "";
            if (initiator == null || initiator.Stats == null || !initiator.Stats.IsAlive)
            {
                message = "⛔ Initiateur invalide ou K.O.";
                Log(message);
                return false;
            }
            var def = DuoTechRegistry.GetDef(DuoTechId.SillageIgne);
            var partner = FindAliveHero(s => DuoTechRegistry.AreComplementary(initiator.Stats, s), initiator);
            if (partner == null)
            {
                message = "⛔ Sillage : partenaire complémentaire (Lucas+Mina) introuvable en vie.";
                Log(message);
                return false;
            }
            int round = CurrentDuoRound();
            var foes = EnemiesOf(initiator);
            if (foes.Count == 0)
            {
                message = "⛔ Sillage : aucun ennemi en vie (PA non débités).";
                Log(message);
                return false;
            }
            TacticalUnit minaUnit = DuoTechRegistry.IsMinaMember(initiator.Stats) ? initiator : partner;
            int bInit = initiator.Stats.CurrentActionPoints;
            int bPart = partner.Stats.CurrentActionPoints;
            if (!DuoTechRegistry.CanDeclare(initiator.Stats, partner.Stats, def, round, out string why))
            {
                message = $"⛔ Sillage refusé : {why}";
                Log(message);
                return false;
            }
            if (!DuoTechRegistry.TryDeclarePayment(initiator.Stats, partner.Stats, def, round))
            {
                message = "⛔ Sillage : paiement refusé (refund auto).";
                Log(message);
                return false;
            }
            Log($"⚡ <b>{def.Name}</b> armé : {initiator.Stats.Name} {bInit}->{initiator.Stats.CurrentActionPoints} PA, {partner.Stats.Name} {bPart}->{partner.Stats.CurrentActionPoints} PA (tampon 1/round au round {round}).");

            ResolvePairRoles(initiator, partner, out CharacterStats mina, out CharacterStats lucas, out _);
            DuoTechRegistry.ResolveSillage(mina, lucas, ToStats(foes), grade, msg => Log(msg));
            FlashUnits(foes);
            if (grade == WeaveSyncGrade.Risky)
                minaUnit?.GetComponent<TacticalUnitVisual>()?.TriggerHitFlash();
            initiator.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText($"[{grade}] Sillage Igné", new UnityEngine.Color(1f, 0.45f, 0.1f));
            RecordChronoSnapshot($"Sillage Igné MJ [{grade}] : {initiator.Stats.Name} + {partner.Stats.Name}");
            try { _turnManager?.CheckCombatOver(); } catch { /* jamais bloquant */ }
            message = $"Sillage Igné [{grade}] résolu sur {foes.Count} ennemi(s).";
            return true;
        }

        /// <summary>
        /// IA auto (solver RD-105) : premier binôme Lucas/Mina de même camp
        /// armable, plan de dash 2-4 alignés, grade du solver (Risky inclus,
        /// AllIn=true : le friendly fire est visible au terrain). Sans
        /// alignement : refusé sans paiement (rapprochez Mina, ≤2 cases).
        /// </summary>
        public bool TryExecuteDebugDuoTechAuto(out string message)
        {
            message = "";
            int round = CurrentDuoRound();
            var def = DuoTechRegistry.GetDef(DuoTechId.SillageIgne);
            var all = AllDebugUnits();
            if (all == null) { message = "⛔ IA auto : aucune unité."; return false; }
            for (int i = 0; i < all.Count; i++)
            {
                var actor = all[i];
                if (actor == null || actor.Stats == null || !actor.Stats.IsAlive) continue;
                if (!DuoTechRegistry.IsDuoMember(actor.Stats)) continue;
                var partner = FindSameFactionPartner(actor);
                if (partner == null) continue;
                if (!DuoTechRegistry.CanDeclare(actor.Stats, partner.Stats, def, round, out _)) continue;

                TacticalUnit minaUnit = DuoTechRegistry.IsMinaMember(actor.Stats) ? actor : partner;
                TacticalUnit lucasUnit = DuoTechRegistry.IsLucasMember(actor.Stats) ? actor : partner;
                var foes = new List<TacticalUnit>();
                var foeCoords = new List<HexCoordinates>();
                for (int j = 0; j < all.Count; j++)
                {
                    var u = all[j];
                    if (u == null || u.Stats == null || !u.Stats.IsAlive) continue;
                    if (u.IsPlayerControlled == actor.IsPlayerControlled) continue;
                    foes.Add(u);
                    foeCoords.Add(u.CurrentCoords);
                }
                if (foes.Count < AIDuoTechSolver.MinTargets) continue;
                if (AIDuoTechSolver.TryPlanSillage(minaUnit.CurrentCoords, lucasUnit.CurrentCoords, foeCoords, out var plan)
                    && AIDuoTechSolver.ShouldExecuteSillage(plan.Grade, true))
                {
                    int bInit = actor.Stats.CurrentActionPoints;
                    int bPart = partner.Stats.CurrentActionPoints;
                    if (!DuoTechRegistry.TryDeclarePayment(actor.Stats, partner.Stats, def, round)) continue;
                    Log($"⚡ <b>Sillage auto</b> armé : {actor.Stats.Name} {bInit}->{actor.Stats.CurrentActionPoints} PA, {partner.Stats.Name} {bPart}->{partner.Stats.CurrentActionPoints} PA (tampon 1/round au round {round}).");
                    if (_grid != null)
                    {
                        bool arrived = minaUnit.TeleportTo(plan.DashDest, _grid);
                        if (!arrived && _grid.TryFindNearestFreeCell(plan.DashDest, out HexCoordinates free, 8))
                            arrived = minaUnit.TeleportTo(free, _grid);
                        Log(arrived ? $"💨 {minaUnit.Stats.Name} dash auto en {plan.DashDest} !" : $"💨 {minaUnit.Stats.Name} : dash auto hors grille, frappe sur place !");
                    }
                    var targetStats = new List<CharacterStats>();
                    var targetUnits = new List<TacticalUnit>();
                    for (int k = 0; k < plan.TargetIndexes.Count; k++)
                    {
                        int idx = plan.TargetIndexes[k];
                        if (idx < 0 || idx >= foes.Count) continue;
                        if (foes[idx].Stats == null || !foes[idx].Stats.IsAlive) continue;
                        targetStats.Add(foes[idx].Stats);
                        targetUnits.Add(foes[idx]);
                    }
                    DuoTechRegistry.ResolveSillage(minaUnit.Stats, lucasUnit.Stats, targetStats, plan.Grade, msg => Log(msg));
                    FlashUnits(targetUnits);
                    actor.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText($"🔥 SILLAGE AUTO [{plan.Grade}] !", new UnityEngine.Color(1f, 0.45f, 0.1f));
                    RecordChronoSnapshot($"Sillage auto [{plan.Grade}] : {minaUnit.Stats.Name} + {lucasUnit.Stats.Name}");
                    try { _turnManager?.CheckCombatOver(); } catch { /* jamais bloquant */ }
                    message = $"Sillage auto [{plan.Grade}] : {targetStats.Count} cible(s), dash {plan.DashDest}.";
                    return true;
                }
            }
            message = "⛔ IA auto : aucun binôme armable avec alignement 2-4 (rapprochez Mina des ennemis, ≤2 cases du partenaire).";
            Log(message);
            return false;
        }

        private TacticalUnit FindSameFactionPartner(TacticalUnit actor)
        {
            var all = AllDebugUnits();
            if (all == null) return null;
            for (int i = 0; i < all.Count; i++)
            {
                var u = all[i];
                if (u == null || u == actor || u.Stats == null || !u.Stats.IsAlive) continue;
                if (u.IsPlayerControlled != actor.IsPlayerControlled) continue;
                if (DuoTechRegistry.AreComplementary(actor.Stats, u.Stats)) return u;
            }
            return null;
        }

        private TacticalUnit FindThomasPairPartner(TacticalUnit initiator, DuoTechId id)
        {
            if (initiator == null || initiator.Stats == null) return null;
            if (id == DuoTechId.RempartTellurique)
            {
                if (DuoTechRegistry.IsThomasMember(initiator.Stats))
                    return FindAliveHero(s => DuoTechRegistry.IsMinaMember(s), initiator);
                if (DuoTechRegistry.IsMinaMember(initiator.Stats))
                    return FindAliveHero(s => DuoTechRegistry.IsThomasMember(s), initiator);
                return null;
            }
            if (id == DuoTechId.ZeroTectonique)
            {
                if (DuoTechRegistry.IsThomasMember(initiator.Stats))
                    return FindAliveHero(s => DuoTechRegistry.IsLucasMember(s), initiator);
                if (DuoTechRegistry.IsLucasMember(initiator.Stats))
                    return FindAliveHero(s => DuoTechRegistry.IsThomasMember(s), initiator);
                return null;
            }
            return null;
        }

        private bool FindTrioFor(TacticalUnit initiator, out TacticalUnit pA, out TacticalUnit pB)
        {
            pA = null;
            pB = null;
            if (initiator == null || initiator.Stats == null) return false;
            bool actorMina = DuoTechRegistry.IsMinaMember(initiator.Stats);
            bool actorLucas = DuoTechRegistry.IsLucasMember(initiator.Stats);
            bool actorThomas = DuoTechRegistry.IsThomasMember(initiator.Stats);
            if (!actorMina && !actorLucas && !actorThomas) return false;
            TacticalUnit mina = actorMina ? initiator : FindAliveHero(s => DuoTechRegistry.IsMinaMember(s), initiator);
            TacticalUnit lucas = actorLucas ? initiator : FindAliveHero(s => DuoTechRegistry.IsLucasMember(s), initiator);
            TacticalUnit thomas = actorThomas ? initiator : FindAliveHero(s => DuoTechRegistry.IsThomasMember(s), initiator);
            if (mina == null || lucas == null || thomas == null) return false;
            var others = new List<TacticalUnit>(2);
            if (mina != initiator) others.Add(mina);
            if (lucas != initiator) others.Add(lucas);
            if (thomas != initiator) others.Add(thomas);
            if (others.Count != 2) return false;
            pA = others[0];
            pB = others[1];
            return true;
        }

        private static void ResolvePairRoles(TacticalUnit a, TacticalUnit b, out CharacterStats mina, out CharacterStats lucas, out CharacterStats thomas)
        {
            mina = DuoTechRegistry.IsMinaMember(a.Stats) ? a.Stats : (b != null && DuoTechRegistry.IsMinaMember(b.Stats) ? b.Stats : null);
            lucas = DuoTechRegistry.IsLucasMember(a.Stats) ? a.Stats : (b != null && DuoTechRegistry.IsLucasMember(b.Stats) ? b.Stats : null);
            thomas = DuoTechRegistry.IsThomasMember(a.Stats) ? a.Stats : (b != null && DuoTechRegistry.IsThomasMember(b.Stats) ? b.Stats : null);
        }

        private static void ResolveTrioRoles(TacticalUnit a, TacticalUnit b, TacticalUnit c, out CharacterStats mina, out CharacterStats lucas, out CharacterStats thomas)
        {
            mina = DuoTechRegistry.IsMinaMember(a.Stats) ? a.Stats
                : (DuoTechRegistry.IsMinaMember(b.Stats) ? b.Stats : c.Stats);
            lucas = DuoTechRegistry.IsLucasMember(a.Stats) ? a.Stats
                : (DuoTechRegistry.IsLucasMember(b.Stats) ? b.Stats : c.Stats);
            thomas = DuoTechRegistry.IsThomasMember(a.Stats) ? a.Stats
                : (DuoTechRegistry.IsThomasMember(b.Stats) ? b.Stats : c.Stats);
        }
    }
}

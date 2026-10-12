using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Audio;
using Killtime.Core.Character;
using Killtime.Core.Combat;
using Killtime.Core.Inventory;
using Killtime.Core.Rules;

namespace Killtime.Tactics.Arena
{
    /// <summary>
    /// Guet (RD-030), opportunité (RD-031), tenir/retarder/canalisation (RD-048),
    /// furtivité (RD-049, hors Fog). Logique déplacée verbatim depuis CombatDevArena.
    /// Les hooks Unity (StepBlockCheck, OnAnyUnitStepCompleted) RESTENT abonnés
    /// en façade (F3) : la façade garde des wrappers une-ligne.
    /// ConsumeShotAndCheckJam (phase 5) est reçu via ShotConsumer.
    /// </summary>
    public sealed class ArenaOverwatchService
    {
        private readonly ArenaContext _ctx;
        private readonly CombatCalculator _calculator;
        private readonly ArenaTargetingService _targeting;
        private readonly ArenaSmokeService _smoke;
        private ArenaState _state => _ctx.State;
        private IArenaHost _host => _ctx.Host;

        /// <summary>Enregistre un snapshot chrono (fourni par la façade).</summary>
        public Action<string> SnapshotRecorder;

        /// <summary>Consomme la munition et gère l'enrayement (fourni par la façade, phase 5).</summary>
        public Action<TacticalUnit, InventoryItem, DamageResult, bool> ShotConsumer;

        public ArenaOverwatchService(ArenaContext ctx, CombatCalculator calculator, ArenaTargetingService targeting, ArenaSmokeService smoke)
        {
            _ctx = ctx;
            _calculator = calculator;
            _targeting = targeting;
            _smoke = smoke;
        }

        /// <summary>
        /// RD-030 Guet / Overwatch : met l'unité en guet (2 PA, rayon = portée de
        /// l'arme à distance équipée). Le tir de réaction partira automatiquement
        /// pendant le tour adverse (CheckOverwatchTriggers).
        /// </summary>
        public bool TryEnterOverwatch(TacticalUnit unit, out string message)
        {
            message = "";
            if (unit == null || unit.Stats == null) return false;
            var weapon = unit.Sheet?.GetEquippedWeapon();
            int range = weapon != null ? Mathf.Max(1, weapon.RangeInTiles) : 1;
            if (!_calculator.TryEnterOverwatch(unit.Stats, range, out string error))
            {
                message = $"⚠️ {error}";
                _host.ArenaLog(message);
                return false;
            }
            var vis = unit.GetComponent<TacticalUnitVisual>();
            vis?.SpawnFloatingText($"👁️ EN GUET ({range} cases, -{CoreRulesConfig.Instance.OverwatchAPCost} PA)", Color.cyan);
            message = $"👁️ <b>{unit.Stats.Name}</b> se met en <b>guet</b> (tir de réaction réservé, rayon {range} cases).";
            _host.ArenaLog(message);
            SnapshotRecorder?.Invoke($"Guet : {unit.Stats.Name} (rayon {range})");
            return true;
        }

        /// <summary>
        /// RD-030 : après qu'une unité a agi en `atCoords` (fin de déplacement,
        /// téléportation, attaque), chaque guetteur ennemi dont le rayon couvre
        /// cette case lâche son tir de réaction en duel aveugle automatique.
        /// 1 tir par guet, couvert total = pas de tir, munitions consommées.
        /// </summary>
        public void CheckOverwatchTriggers(TacticalUnit mover, HexCoordinates atCoords)
        {
            var turnManager = _ctx.TurnManager;
            var grid = _ctx.Grid;
            if (mover == null || mover.Stats == null || !mover.Stats.IsAlive) return;
            if (turnManager != null && turnManager.IsInExploration) return;
            if (turnManager == null) return;
            // Autorité sim : seul le GM/hôte résout les tirs de réaction (sinon
            // client + GM appliqueraient les dégâts en double). Le GM diffuse
            // l'état complet après chaque tir (convergence clients).
            if (TurnManager.IsMultiplayerPlayerClient()) return;

            var order = turnManager.TurnOrder;
            for (int i = 0; i < order.Count; i++)
            {
                var watcher = order[i];
                if (watcher == null || watcher == mover || watcher.Stats == null) continue;
                if (!watcher.Stats.IsAlive) continue;
                if (watcher.IsPlayerControlled == mover.IsPlayerControlled) continue;
                if (!OverwatchState.IsWatching(watcher.Stats)) continue;
                if (!OverwatchState.TryGetRange(watcher.Stats, out int range)) continue;

                int dist = TitanFootprint.MinDistanceBetweenUnits(
                    watcher.CurrentCoords, watcher.FootprintType, atCoords, mover.FootprintType);
                if (dist > range) continue;

                CoverType cover = CoverType.None;
                if (dist > 1 && grid != null)
                {
                    cover = CoverSystem.EvaluateCover(
                        watcher.CurrentCoords, atCoords, grid,
                        mover.FootprintType, watcher.FootprintType,
                        _state.SmokeZones, ArenaSmokeService.SeesThroughSmoke(watcher));
                }
                if (cover == CoverType.Full) continue;

                var weapon = watcher.Sheet?.GetEquippedWeapon();
                SkillType owSkill = (weapon != null && weapon.AssociatedSkill == SkillType.ProjectilesTir)
                    ? SkillType.ProjectilesTir
                    : SkillType.Ballistique;
                bool usesAmmo = WeaponAmmo.UsesAmmo(weapon, owSkill);
                if (weapon != null && usesAmmo && (weapon.Jammed || weapon.AmmoRemaining <= 0)) continue;

                int dmg = (weapon != null && weapon.BaseDamage > 0) ? weapon.BaseDamage : 5;
                int bonusEc = weapon != null ? Math.Max(0, weapon.AttackBonusEc) : 0;
                bool hdShot = usesAmmo && weapon != null && weapon.LoadedHD;
                if (hdShot) dmg += 1;

                // RD-036 : le tir de réaction suit la même règle hauteur que le
                // tir direct (balistique ±1, laser exempté — voir ElevationAdvantage).
                ElevationRuling owRuling = _targeting.GetElevationRuling(watcher, mover, owSkill, weapon, false);
                DamageResult? result = _calculator.ResolveOverwatchFire(
                    watcher.Stats, mover.Stats, dmg, cover, bonusEc, BodyPart.Torse, owSkill,
                    owRuling.AttackMod, owRuling.Label);
                if (result == null) continue;

                if (usesAmmo && weapon != null && result.Value.AttackRoll.RawRoll > 0)
                    ShotConsumer?.Invoke(watcher, weapon, result.Value, hdShot);

                try { Killtime.Tactics.Visibility.FogOfWarManager.Instance?.NotifyLoudShot(watcher); }
                catch { /* fog optionnel */ }

                var watchVis = watcher.GetComponent<TacticalUnitVisual>();
                var moverVis = mover.GetComponent<TacticalUnitVisual>();
                watchVis?.SpawnFloatingText("👁️ TIR DE RÉACTION", new Color(0.2f, 0.9f, 1.0f));
                _host.ArenaLog(result.Value.CombatLog);
                HandleChannelInterruptionFx(mover, result.Value.ChannelInterrupted);
                if (result.Value.IsHit)
                {
                    moverVis?.TriggerHitFlash();
                    moverVis?.SpawnFloatingText($"-{result.Value.FinalDamageApplied} PV (guet)", new Color(1.0f, 0.25f, 0.25f));
                    if (!mover.Stats.IsAlive)
                    {
                        moverVis?.TriggerFallingBackDeath();
                        if (result.Value.FatalResolution == FatalBlowResolution.EligibleForLastBreath)
                        {
                            if (!mover.IsPlayerControlled) mover.Stats.ChooseSombrer();
                            else mover.Stats.ChooseLastBreath();
                        }
                        if (_state.CurrentTarget == mover) _host.RequestAutoTargetNextAlive();
                    }
                }
                else if (result.Value.IsBlocked)
                {
                    moverVis?.SpawnFloatingText("[PARADE] Tir de guet neutralisé", new Color(0.2f, 0.9f, 1.0f));
                }
                SnapshotRecorder?.Invoke($"Guet : {watcher.Stats.Name} -> {mover.Stats.Name}");
                _host.RequestMusicUpdate();
                _host.RequestCombatOverCheck();

                if (Killtime.Multi.VTTTableSync.Instance != null
                    && !Killtime.Multi.VTTTableSync.Instance.IsApplyingRemoteAction
                    && Killtime.Multi.VTTRoomManager.Instance != null
                    && Killtime.Multi.VTTRoomManager.Instance.InRoom
                    && Killtime.Multi.VTTRoomManager.Instance.IsGM)
                {
                    Killtime.Multi.VTTTableSync.Instance.BroadcastFullCombatState();
                }

                if (!mover.Stats.IsAlive) break;
            }
        }

        /// <summary>
        /// Voisins ennemis dont le contact est ROMPU par ce pas (≤ portée avant,
        /// > portée après). Rester au contact (pas latéral) ne provoque rien.
        /// </summary>
        private List<TacticalUnit> FindContactBreakers(TacticalUnit mover, HexCoordinates from, HexCoordinates to)
        {
            var result = new List<TacticalUnit>();
            var turnManager = _ctx.TurnManager;
            if (mover == null || mover.Stats == null || turnManager == null) return result;
            int reach = Mathf.Max(0, CoreRulesConfig.Instance.OpportunityReach);
            var order = turnManager.TurnOrder;
            for (int i = 0; i < order.Count; i++)
            {
                var u = order[i];
                if (u == null || u == mover || u.Stats == null || !u.Stats.IsAlive) continue;
                if (u.IsPlayerControlled == mover.IsPlayerControlled) continue;
                int dFrom = TitanFootprint.MinDistanceBetweenUnits(from, mover.FootprintType, u.CurrentCoords, u.FootprintType);
                if (dFrom > reach) continue;
                int dTo = TitanFootprint.MinDistanceBetweenUnits(to, mover.FootprintType, u.CurrentCoords, u.FootprintType);
                if (dTo <= reach) continue;
                result.Add(u);
            }
            return result;
        }

        /// <summary>
        /// RD-031 Blocage (hook TacticalUnit.StepBlockCheck, avant chaque pas) :
        /// chaque briseur en posture Blocage peut tenter de retenir le fuyard
        /// (duel opposé Athlétisme, 1 PA). Succès = pas annulé, sans grapple ;
        /// les PA du fuyard sont remboursés par MoveAlongPath. Vrai = bloqué.
        /// </summary>
        public bool CheckOpportunityBlock(TacticalUnit mover, HexCoordinates from, HexCoordinates to)
        {
            var turnManager = _ctx.TurnManager;
            if (mover == null || mover.Stats == null || !mover.Stats.IsAlive) return false;
            if (turnManager == null || turnManager.IsInExploration) return false;
            if (TurnManager.IsMultiplayerPlayerClient()) return false;

            var breakers = FindContactBreakers(mover, from, to);
            if (breakers.Count == 0) return false;

            int blockCost = CoreRulesConfig.Instance.OpportunityBlockAPCost;
            for (int i = 0; i < breakers.Count; i++)
            {
                var blocker = breakers[i];
                if (blocker == null || blocker.Stats == null || !blocker.Stats.IsAlive) continue;
                if (OpportunityState.GetStance(blocker.Stats) != OpportunityReactionType.Block) continue;
                if (!OpportunityState.CanReact(blocker.Stats)) continue;
                // Estimation du coût réel (Ralenti x2 + malus armure) avant engagement.
                int need = blockCost + blocker.Stats.GetWornApMalus();
                if (blocker.Stats.ActiveStatus.HasFlag(StatusEffect.Ralenti))
                    need *= CoreRulesConfig.Instance.RalentiAPMultiplier;
                if (blocker.Stats.CurrentActionPoints < need) continue;
                if (!OpportunityState.TryConsumeReaction(blocker.Stats)) continue;
                if (!blocker.Stats.ConsumeActionPoints(blockCost))
                {
                    OpportunityState.CancelReaction(blocker.Stats);
                    continue;
                }

                var opposed = _calculator.ResolveOpportunityBlock(blocker.Stats, mover.Stats);
                _host.ArenaLog(opposed.CombatLog);
                var bVis = blocker.GetComponent<TacticalUnitVisual>();
                bVis?.SpawnFloatingText(opposed.AttackerWins ? "🛡️ BLOCAGE !" : "🛡️ Blocage manqué", new Color(0.4f, 0.8f, 1.0f));
                mover.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText(
                    opposed.AttackerWins ? "⛔ Retenu au contact" : "💨 Esquive le blocage", new Color(1.0f, 0.8f, 0.3f));
                SnapshotRecorder?.Invoke($"Opportunité blocage : {blocker.Stats.Name} -> {mover.Stats.Name}");
                BroadcastOpportunityState();
                if (opposed.AttackerWins) return true;
                // Échec : réaction + PA dépensés, le fuyard passe (autres bloqueurs tentent).
            }
            return false;
        }

        /// <summary>
        /// RD-031 (hook TacticalUnit.OnAnyUnitStepCompleted, après chaque pas) :
        /// chaque briseur non-bloqueur réagit selon sa posture — Frappe (gratuite),
        /// Balayage (gratuit, Jambes), Poursuite (1 PA, suit d'1 case).
        /// </summary>
        public void HandleUnitStepOpportunity(TacticalUnit mover, HexCoordinates from, HexCoordinates to, bool provoked)
        {
            var turnManager = _ctx.TurnManager;
            if (!provoked) return;
            if (mover == null || mover.Stats == null || !mover.Stats.IsAlive) return;
            if (turnManager == null || turnManager.IsInExploration) return;
            if (TurnManager.IsMultiplayerPlayerClient()) return;

            var breakers = FindContactBreakers(mover, from, to);
            for (int i = 0; i < breakers.Count; i++)
            {
                var reactor = breakers[i];
                if (reactor == null || reactor.Stats == null || !reactor.Stats.IsAlive) continue;
                if (!OpportunityState.CanReact(reactor.Stats)) continue;

                var stance = OpportunityState.GetStance(reactor.Stats);
                switch (stance)
                {
                    case OpportunityReactionType.Follow:
                        TryOpportunityFollow(reactor, mover, from);
                        break;
                    case OpportunityReactionType.Trip:
                        if (IsOpportunityMeleeReachable(reactor, mover)
                            && OpportunityState.TryConsumeReaction(reactor.Stats))
                            ResolveOpportunityMelee(reactor, mover, true);
                        break;
                    case OpportunityReactionType.Block:
                        // Déjà résolu avant le pas ( CheckOpportunityBlock ) : le pas a eu
                        // lieu donc le blocage a échoué ou manquait de PA — rien à faire.
                        break;
                    default:
                        if (IsOpportunityMeleeReachable(reactor, mover)
                            && OpportunityState.TryConsumeReaction(reactor.Stats))
                            ResolveOpportunityMelee(reactor, mover, false);
                        break;
                }

                if (!mover.Stats.IsAlive) break;
            }
        }

        /// <summary>
        /// RD-031 Poursuite : suit le fuyard d'1 case (case libérée `from`) pour
        /// 1 PA de réaction. Sans grapple : simple suivi, pas d'attaque.
        /// Le coût est débité UNE fois, par MoveAlongPath (malus armure / Ralenti inclus).
        /// </summary>
        private void TryOpportunityFollow(TacticalUnit follower, TacticalUnit mover, HexCoordinates vacated)
        {
            var grid = _ctx.Grid;
            if (follower == null || mover == null || grid == null) return;
            if (follower.IsMoving) return;
            int followCost = CoreRulesConfig.Instance.OpportunityFollowAPCost;
            int need = followCost + follower.Stats.GetWornApMalus();
            if (follower.Stats.ActiveStatus.HasFlag(StatusEffect.Ralenti))
                need *= CoreRulesConfig.Instance.RalentiAPMultiplier;
            if (follower.Stats.CurrentActionPoints < need) return;
            if (!grid.IsFootprintFree(vacated, follower.FootprintType, follower)) return;
            if (!OpportunityState.TryConsumeReaction(follower.Stats)) return;

            var path = new List<HexCoordinates> { follower.CurrentCoords, vacated };
            _host.ArenaLog($"👣 <b>OPPORTUNITÉ — poursuite</b> : {follower.Stats.Name} suit {mover.Stats.Name} en {vacated} (-{followCost} PA réaction).");
            follower.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("👣 POURSUITE", new Color(0.4f, 0.9f, 1.0f));
            SnapshotRecorder?.Invoke($"Opportunité poursuite : {follower.Stats.Name} -> {mover.Stats.Name}");
            // Mouvement de réaction : coût débité par MoveAlongPath (une seule fois),
            // sans provoquer d'opportunité à son tour (pas de ping-pong).
            // Note : l'arrivée peut toujours déclencher un GUET adverse (RD-030).
            _host.ArenaStartCoroutine(follower.MoveAlongPath(path, grid, followCost, false, false));
            BroadcastOpportunityState();
        }

        /// <summary>
        /// RD-036 : la frappe d'opportunité ne part que si le fuyard est
        /// verticalement atteignable (table mêlée par paliers). Vérifié AVANT
        /// de consommer la réaction : sinon le fuyard est simplement hors
        /// d'atteinte et le réacteur garde sa réaction.
        /// </summary>
        private bool IsOpportunityMeleeReachable(TacticalUnit reactor, TacticalUnit mover)
        {
            if (reactor == null || mover == null) return false;
            var weapon = reactor.Sheet?.GetEquippedWeapon();
            int range = weapon != null ? Math.Max(1, weapon.RangeInTiles) : 1;
            if (!ElevationAdvantage.IsMeleeReachable(_targeting.GetUnitElevation(reactor), _targeting.GetUnitElevation(mover), range))
            {
                _host.ArenaLog($"⛰️ <b>OPPORTUNITÉ manquée</b> : {mover.Stats.Name} est hors d'atteinte verticale pour {reactor.Stats.Name} — réaction conservée.");
                return false;
            }
            return true;
        }

        /// <summary>
        /// RD-031 Frappe / Balayage : coup de mêlée en duel aveugle auto (réaction
        /// seule, 0 PA par défaut — voir OpportunityStrikeIsFree). Balayage = visée
        /// Jambes (À Terre + Ralenti si choc).
        /// </summary>
        private void ResolveOpportunityMelee(TacticalUnit reactor, TacticalUnit mover, bool trip)
        {
            var cfg = CoreRulesConfig.Instance;
            SkillType skill = CombatCalculator.ResolveOpportunitySkill(reactor.Stats);
            var weapon = reactor.Sheet?.GetEquippedWeapon();
            // RD-036 : modificateur hauteur de la frappe (l'atteignabilité a été
            // vérifiée par l'appelant AVANT de consommer la réaction).
            ElevationRuling oppRuling = ElevationAdvantage.Resolve(
                _targeting.GetUnitElevation(reactor), _targeting.GetUnitElevation(mover), true, skill, weapon);
            if (!cfg.OpportunityStrikeIsFree)
            {
                if (!reactor.Stats.ConsumeActionPoints(cfg.BaseReactionAPCost))
                {
                    OpportunityState.CancelReaction(reactor.Stats);
                    return;
                }
            }

            int dmg = (weapon != null && weapon.BaseDamage > 0) ? weapon.BaseDamage : 5;

            DamageResult? result = trip
                ? _calculator.ResolveOpportunityTrip(reactor.Stats, mover.Stats, dmg, skill, oppRuling.AttackMod, oppRuling.Label)
                : _calculator.ResolveOpportunityStrike(reactor.Stats, mover.Stats, dmg, skill, BodyPart.Torse, oppRuling.AttackMod, oppRuling.Label);
            if (result == null) return;

            _host.ArenaLog(result.Value.CombatLog);
            HandleChannelInterruptionFx(mover, result.Value.ChannelInterrupted);
            var moverVis = mover.GetComponent<TacticalUnitVisual>();
            reactor.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText(
                trip ? "🦵 BALAYAGE !" : "⚔️ OPPORTUNITÉ !", new Color(1.0f, 0.55f, 0.15f));
            if (result.Value.IsHit)
            {
                moverVis?.TriggerHitFlash();
                moverVis?.SpawnFloatingText($"-{result.Value.FinalDamageApplied} PV (opportunité)", new Color(1.0f, 0.25f, 0.25f));
                if (!mover.Stats.IsAlive)
                {
                    moverVis?.TriggerFallingBackDeath();
                    if (result.Value.FatalResolution == FatalBlowResolution.EligibleForLastBreath)
                    {
                        if (!mover.IsPlayerControlled) mover.Stats.ChooseSombrer();
                        else mover.Stats.ChooseLastBreath();
                    }
                    if (_state.CurrentTarget == mover) _host.RequestAutoTargetNextAlive();
                }
            }
            else if (result.Value.IsBlocked)
            {
                moverVis?.SpawnFloatingText("[PARADE] Opportunité neutralisée", new Color(0.2f, 0.9f, 1.0f));
            }
            SnapshotRecorder?.Invoke($"Opportunité {(trip ? "balayage" : "frappe")} : {reactor.Stats.Name} -> {mover.Stats.Name}");
            _host.RequestMusicUpdate();
            _host.RequestCombatOverCheck();
            BroadcastOpportunityState();
        }

        /// <summary>
        /// RD-031 Décrochage (Livre VI §26.2) : paie 1 PA pour que le prochain
        /// déplacement de l'unité ne provoque aucune réaction d'opportunité.
        /// </summary>
        public bool TryRequestSafeDisengage(TacticalUnit unit, out string message)
        {
            message = "";
            if (unit == null || unit.Stats == null) return false;
            if (!OpportunityState.RequestSafeDisengage(unit.Stats, CoreRulesConfig.Instance.DisengageAPCost, out string error))
            {
                message = $"⚠️ {error}";
                _host.ArenaLog(message);
                return false;
            }
            unit.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText(
                $"💨 DÉCROCHAGE (-{CoreRulesConfig.Instance.DisengageAPCost} PA)", Color.cyan);
            message = $"💨 <b>{unit.Stats.Name}</b> prépare son <b>décrochage</b> : prochain déplacement sans réaction d'opportunité.";
            _host.ArenaLog(message);
            SnapshotRecorder?.Invoke($"Décrochage : {unit.Stats.Name}");
            return true;
        }

        /// <summary>
        /// RD-048 Tenir : l'unité active passe son tour en conservant ses PA et
        /// se déclare en attente (0 PA). Interruption ultérieure via le
        /// TurnManager (TryResumeHeldUnit, rejouée juste après l'unité active).
        /// </summary>
        public bool TryHoldAction(TacticalUnit unit, out string message)
        {
            var turnManager = _ctx.TurnManager;
            message = "";
            if (unit == null || unit.Stats == null) return false;
            if (turnManager == null) return false;
            if (unit != turnManager.ActiveUnit)
            {
                message = $"⚠️ Seule l'unité active ({turnManager.ActiveUnit?.Stats?.Name ?? "—"}) peut tenir son action.";
                _host.ArenaLog(message);
                return false;
            }
            if (!turnManager.TryHoldActiveUnit(out message))
            {
                _host.ArenaLog(message);
                return false;
            }
            unit.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("⏸️ EN ATTENTE", Color.cyan);
            _host.ArenaLog(message);
            SnapshotRecorder?.Invoke($"Tient l'action : {unit.Stats.Name}");
            BroadcastOpportunityState();
            return true;
        }

        /// <summary>
        /// RD-048 Retarder : l'unité active repousse son tour (initiative
        /// -pénalité, 1/round), réinsérée plus tard dans le round en cours.
        /// </summary>
        public bool TryDelayTurn(TacticalUnit unit, out string message)
        {
            var turnManager = _ctx.TurnManager;
            message = "";
            if (unit == null || unit.Stats == null) return false;
            if (turnManager == null) return false;
            if (unit != turnManager.ActiveUnit)
            {
                message = $"⚠️ Seule l'unité active ({turnManager.ActiveUnit?.Stats?.Name ?? "—"}) peut retarder son tour.";
                _host.ArenaLog(message);
                return false;
            }
            if (!turnManager.TryDelayActiveUnit(out message))
            {
                _host.ArenaLog(message);
                return false;
            }
            unit.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("⏳ RETARDÉ", new Color(1.0f, 0.8f, 0.3f));
            _host.ArenaLog(message);
            SnapshotRecorder?.Invoke($"Retarde : {unit.Stats.Name}");
            BroadcastOpportunityState();
            return true;
        }

        /// <summary>
        /// RD-048 Canalisation / Action en Progression : étale l'effort sur
        /// plusieurs temps (coût 1 PA, +2 au jet à l'attaque, -1 en défense,
        /// interrompue si dégât net &gt; encaissement).
        /// </summary>
        public bool TryBeginChanneling(TacticalUnit unit, string label, out string message)
        {
            message = "";
            if (unit == null || unit.Stats == null) return false;
            if (!unit.Stats.IsAlive)
            {
                message = $"{unit.Stats.Name} est hors de combat : canalisation impossible.";
                _host.ArenaLog(message);
                return false;
            }
            if (!_calculator.TryBeginChanneling(unit.Stats, label, out string error))
            {
                message = $"⚠️ {error}";
                _host.ArenaLog(message);
                return false;
            }
            unit.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText($"⏳ CANALISE : {label}", new Color(0.7f, 0.5f, 1.0f));
            message = $"⏳ <b>{unit.Stats.Name}</b> canalise <b>« {label} »</b> (-{CoreRulesConfig.Instance.ChannelStartAPCost} PA) : +{CoreRulesConfig.Instance.ChannelAttackBonus} au jet à l'attaque, {CoreRulesConfig.Instance.ChannelDefensePenalty} en défense, interrompue si dégât &gt; encaissement.";
            _host.ArenaLog(message);
            SnapshotRecorder?.Invoke($"Canalisation : {unit.Stats.Name} (« {label} »)");
            BroadcastOpportunityState();
            return true;
        }

        /// <summary>
        /// RD-048 : notifie une canalisation rompue par des dégâts hors duel
        /// (grenades, zone, DoT) et affiche le retour visuel. Les duels directs
        /// tracent déjà l'interruption dans leur propre journal de combat.
        /// </summary>
        public void NotifyChannelDamage(TacticalUnit defender, int finalDamageApplied)
        {
            if (defender == null || defender.Stats == null) return;
            if (ChannelingState.NotifyDamage(defender.Stats, finalDamageApplied, out string label))
            {
                defender.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("⏳ CANALISATION ROMPUE", new Color(1.0f, 0.4f, 0.2f));
                _host.ArenaLog($"⏳ <b>CANALISATION INTERROMPUE</b> : {defender.Stats.Name} subit {finalDamageApplied} dégâts nets (&gt; encaissement {defender.Stats.EncaissementThreshold}) — « {label} » perdue !");
                SnapshotRecorder?.Invoke($"Canalisation rompue : {defender.Stats.Name}");
            }
        }

        /// <summary>
        /// RD-048 : retour visuel d'une interruption tracée par le duel
        /// (direct, guet, opportunité, assaut groupé). Le journal contient déjà
        /// la ligne « CANALISATION INTERROMPUE ».
        /// </summary>
        public void HandleChannelInterruptionFx(TacticalUnit defender, bool interrupted)
        {
            if (!interrupted || defender == null) return;
            defender.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("⏳ CANALISATION ROMPUE", new Color(1.0f, 0.4f, 0.2f));
        }

        /// <summary>
        /// RD-049 Furtivité / Stealth : fait passer l'unité en furtivité (2 PA).
        /// Considéré invisible dans le brouillard de guerre, confère +2 à la
        /// prochaine attaque et interdit toute réaction adverse (embuscade).
        /// </summary>
        public bool TryEnterStealth(TacticalUnit unit, out string message)
        {
            message = "";
            if (unit == null || unit.Stats == null) return false;
            if (!unit.Stats.IsAlive)
            {
                message = $"{unit.Stats.Name} est hors de combat : furtivité impossible.";
                _host.ArenaLog(message);
                return false;
            }
            if (StealthState.IsStealthed(unit.Stats))
            {
                message = $"{unit.Stats.Name} est déjà en furtivité.";
                _host.ArenaLog(message);
                return false;
            }
            int cost = StealthState.EnterStealthAPCost;
            if (!_ctx.GetInfiniteAP() && unit.Stats.CurrentActionPoints < cost)
            {
                message = $"⚠️ PA insuffisants : furtivité = {cost} PA (reste {unit.Stats.CurrentActionPoints}).";
                _host.ArenaLog(message);
                return false;
            }
            if (!_ctx.GetInfiniteAP()) unit.Stats.ConsumeActionPoints(cost);

            StealthState.SetStealth(unit.Stats, true);
            var vis = unit.GetComponent<TacticalUnitVisual>();
            vis?.SpawnFloatingText($"🥷 FURTIVITÉ (-{cost} PA)", new Color(0.2f, 0.95f, 0.75f));
            message = $"🥷 <b>{unit.Stats.Name}</b> passe en <b>furtivité</b> (-{cost} PA) : invisible, +2 prochaine attaque, cible surprise.";
            _host.ArenaLog(message);
            Killtime.Tactics.Visibility.FogOfWarManager.Instance?.RefreshFog("stealth");
            SnapshotRecorder?.Invoke($"Furtivité : {unit.Stats.Name}");
            return true;
        }

        /// <summary>Diffuse l'état après une réaction (GM seul, comme le guet).</summary>
        private void BroadcastOpportunityState()
        {
            try
            {
                if (Killtime.Multi.VTTTableSync.Instance != null
                    && !Killtime.Multi.VTTTableSync.Instance.IsApplyingRemoteAction
                    && Killtime.Multi.VTTRoomManager.Instance != null
                    && Killtime.Multi.VTTRoomManager.Instance.InRoom
                    && Killtime.Multi.VTTRoomManager.Instance.IsGM)
                {
                    Killtime.Multi.VTTTableSync.Instance.BroadcastFullCombatState();
                }
            }
            catch { /* VTT optionnel */ }
        }
    }
}

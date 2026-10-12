using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Audio;
using Killtime.Core.Character;
using Killtime.Core.Combat;
using Killtime.Core.Dice;
using Killtime.Core.Inventory;
using Killtime.Core.Rules;
using Killtime.UI;

namespace Killtime.Tactics.Arena
{
    /// <summary>
    /// Entretien d'armes (RD-033), lutte/grappin/étranglement/traînée (RD-039),
    /// bousculade (RD-040), charge (RD-038), assauts groupés et poursuite (RD-084).
    /// Logique déplacée verbatim depuis CombatDevArena.
    /// ExecuteAttack (phase 7) est reçu via ChargeStrike.
    /// </summary>
    public sealed class ArenaMeleeService
    {
        private readonly ArenaContext _ctx;
        private readonly ArenaOverwatchService _overwatch;
        private ArenaState _state => _ctx.State;
        private IArenaHost _host => _ctx.Host;

        /// <summary>Enregistre un snapshot chrono (fourni par la façade).</summary>
        public Action<string> SnapshotRecorder;

        /// <summary>Frappe de charge : exécute la passe d'armes de mêlée (fourni par la façade, phase 7).</summary>
        public Action<TacticalUnit, TacticalUnit, BodyPart, SkillType, int, int> ChargeStrike;

        public ArenaMeleeService(ArenaContext ctx, ArenaOverwatchService overwatch)
        {
            _ctx = ctx;
            _overwatch = overwatch;
        }

        public void ConsumeShotAndCheckJam(TacticalUnit attacker, InventoryItem weapon, DamageResult result, bool hdShot)
        {
            if (attacker == null || weapon == null || weapon.AmmoCapacity <= 0) return;
            weapon.AmmoRemaining = Math.Max(0, weapon.AmmoRemaining - 1);
            if (weapon.AmmoRemaining <= 0) weapon.LoadedHD = false;
            string ammoNote = $" [🔋{weapon.AmmoRemaining}/{weapon.AmmoCapacity}{(weapon.LoadedHD ? "+HD" : "")}]";
            attacker.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText(
                hdShot ? $"Tir HD +1D{ammoNote}" : $"Coup parti{ammoNote}", new Color(1f, 0.72f, 0.15f));
            // Enrayement sur critique adverse (parade/tir contré magistral).
            if (result.DefenseRoll.IsCriticalSuccess && !weapon.Jammed)
            {
                weapon.Jammed = true;
                attacker.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("🔧 ENRAYÉE !", Color.red);
                _host.ArenaLog($"🔧 <b>ENRAYEMENT</b> : {weapon.Name} de {attacker.Stats.Name} s'enraye sur la parade critique ! Désenrayez (1 PA).");
            }
            if (weapon.AmmoRemaining <= 0)
                _host.ArenaLog($"🔋 <b>Chargeur vide</b> : {weapon.Name} — rechargez ({weapon.ReloadAPCost} PA).");
            attacker.NotifyInventoryChanged(true);
        }

        /// <summary>
        /// RD-033 : recharge le chargeur depuis la réserve (éjecte le reste d'un autre
        /// type). useHD exige HeavyAmmo (sniper/Deglazer). Ne compte pas comme attaque.
        /// </summary>
        public bool TryReloadWeapon(TacticalUnit unit, bool useHD, out string message)
        {
            message = "";
            if (unit == null || unit.Stats == null) return false;
            var sheet = unit.GetOrBuildSheet();
            var weapon = sheet?.GetEquippedWeapon();
            if (weapon == null || weapon.AmmoCapacity <= 0) { message = "Aucune arme à chargeur équipée."; return false; }
            if (weapon.Jammed) { message = $"🔧 {weapon.Name} est enrayée : désenrayez d'abord (1 PA)."; return false; }
            if (useHD && !weapon.HeavyAmmo) { message = $"⛔ {weapon.Name} n'accepte pas la Haute Densité (sniper/Deglazer)."; return false; }
            int need = weapon.AmmoCapacity - weapon.AmmoRemaining;
            if (need <= 0) { message = $"🔋 {weapon.Name} déjà plein ({weapon.AmmoCapacity})."; return false; }
            int stock = WeaponAmmo.StockShots(sheet, weapon.AmmoType, useHD);
            if (stock <= 0) { message = $"⚠️ Aucune réserve {(useHD ? "Haute Densité" : weapon.AmmoType)} — achetez au marché."; return false; }
            int cost = Math.Max(1, weapon.ReloadAPCost);
            if (!_ctx.GetInfiniteAP() && unit.Stats.CurrentActionPoints < cost) { message = $"⚠️ PA insuffisants : recharger = {cost} PA."; return false; }
            int taken = WeaponAmmo.TakeShots(sheet, weapon.AmmoType, useHD, Math.Min(need, stock));
            if (taken <= 0) { message = "⚠️ Réserve introuvable."; return false; }
            int ejected = (weapon.LoadedHD != useHD) ? weapon.AmmoRemaining : 0;
            weapon.AmmoRemaining = Math.Min(weapon.AmmoCapacity, taken);
            weapon.LoadedHD = useHD;
            if (!_ctx.GetInfiniteAP()) unit.Stats.ConsumeActionPoints(cost);
            unit.NotifyInventoryChanged(true);
            message = $"🔋 <b>{unit.Stats.Name}</b> recharge <b>{weapon.Name}</b>{(useHD ? " <b>HD +1D</b>" : "")} : +{taken} coups"
                + (ejected > 0 ? $" ({ejected} éjectés)" : "")
                + (taken < need ? " — réserve épuisée" : "")
                + $" [{weapon.AmmoRemaining}/{weapon.AmmoCapacity}] (-{cost} PA).";
            return true;
        }

        /// <summary>RD-033 : désenraye l'arme équipée (1 PA, gratuit 0 PA avec Kit d'Entretien d'Armes).</summary>
        public bool TryClearJam(TacticalUnit unit, out string message)
        {
            message = "";
            if (unit == null || unit.Stats == null) return false;
            var weapon = unit.GetOrBuildSheet()?.GetEquippedWeapon();
            if (weapon == null || !weapon.Jammed) { message = "Arme non enrayée."; return false; }
            bool hasKit = SmokeScreen.HasItemByName(unit.Stats, "Kit d'Entretien");
            int cost = hasKit ? 0 : 1;
            if (!_ctx.GetInfiniteAP() && cost > 0 && unit.Stats.CurrentActionPoints < cost) { message = $"⚠️ PA insuffisants : désenrayer = {cost} PA."; return false; }
            weapon.Jammed = false;
            if (!_ctx.GetInfiniteAP() && cost > 0) unit.Stats.ConsumeActionPoints(cost);
            unit.NotifyInventoryChanged(true);
            message = hasKit
                ? $"🔧 <b>{unit.Stats.Name}</b> désenraye <b>{weapon.Name}</b> avec son <b>Kit d'Entretien d'Armes</b> (0 PA) !"
                : $"🔧 <b>{unit.Stats.Name}</b> désenraye <b>{weapon.Name}</b> (-1 PA).";
            return true;
        }

        /// <summary>
        /// Recharge d'urgence de la barrière de champ de force (1 PA) via Cellule Nytharite (Standard 10 PV / Pure 25 PV).
        /// </summary>
        public bool TryRechargeShieldWithCell(TacticalUnit unit, bool usePureCell, out string message)
        {
            message = "";
            if (unit == null || unit.Stats == null) return false;
            if (unit.Stats.MaxShieldHP <= 0) { message = "Aucun champ de force équipé."; return false; }
            if (unit.Stats.CurrentShieldHP >= unit.Stats.MaxShieldHP) { message = "Barrière énergétique déjà au maximum."; return false; }
            if (!_ctx.GetInfiniteAP() && unit.Stats.CurrentActionPoints < 1) { message = "⚠️ PA insuffisants (1 PA requis)."; return false; }

            string cellName = usePureCell ? "Cellule Nytharite Pure" : "Cellule Nytharite Standard";
            var sheet = unit.GetOrBuildSheet();
            var cell = SmokeScreen.FindItemByName(unit.Stats, cellName);
            if (cell == null) { message = $"⚠️ Aucune {cellName} en inventaire."; return false; }

            int restoreAmount = usePureCell ? 25 : 10;
            int before = unit.Stats.CurrentShieldHP;
            unit.Stats.CurrentShieldHP = Math.Min(unit.Stats.MaxShieldHP, unit.Stats.CurrentShieldHP + restoreAmount);
            int restored = unit.Stats.CurrentShieldHP - before;

            sheet.ConsumeOne(cell.ItemId);
            unit.NotifyInventoryChanged(true);
            if (!_ctx.GetInfiniteAP()) unit.Stats.ConsumeActionPoints(1);

            var vis = unit.GetComponent<TacticalUnitVisual>();
            vis?.SpawnFloatingText($"🔮 +{restored} Bouclier", Color.cyan);
            message = $"🔮 <b>{unit.Stats.Name}</b> consomme une <b>{cellName}</b> : +{restored} PV de bouclier [{unit.Stats.CurrentShieldHP}/{unit.Stats.MaxShieldHP}] (-1 PA).";
            return true;
        }

        /// <summary>
        /// Désarmement Bras Droit : sur touché au bras porteur, différentiel élevé
        /// arrache l'arme qui tombe au sol (2 cases max). Seuils Livre III :
        /// base 4, Croc de Désarmement 3, Désarmement Fleuret 2.
        /// </summary>
        public void TryDisarmDefender(TacticalUnit attacker, TacticalUnit defender, BodyPart targetedPart, DamageResult result)
        {
            var grid = _ctx.Grid;
            if (attacker == null || defender == null) return;
            if (targetedPart != BodyPart.BrasDroit && result.TargetPart != BodyPart.BrasDroit) return;
            if (!result.IsHit) return;
            if (defender.Stats == null || !defender.Stats.IsAlive) return;
            var sheet = defender.GetOrBuildSheet();
            var held = sheet?.GetEquippedWeapon();
            if (held == null) return;

            int threshold = 4;
            try
            {
                if (attacker.Stats != null)
                {
                    if (attacker.Stats.HasSpecialization("Escrime : Désarmement Fleuret")) threshold = 2;
                    else if (attacker.Stats.HasSpecialization("Hache de Guerre : Croc de Désarmement")) threshold = 3;
                }
            }
            catch { }
            if (result.Differential < threshold) return;

            try
            {
                string droppedName = held.Name;
                string droppedId = held.ItemId;
                var toDrop = sheet.Inventory?.Find(i => i != null && i.ItemId == droppedId) ?? held;
                sheet.RemoveItem(droppedId);
                toDrop.IsEquipped = false;
                defender.NotifyInventoryChanged(true);
                Vector3 spawnPos = defender.transform.position;
                var node = grid != null ? grid.GetNode(defender.CurrentCoords) : null;
                if (node != null) spawnPos = node.WorldPosition;
                // L'arme vole à la case du porteur (2 cases au sol selon Livre VI, ici stessa case).
                DroppedWeaponPickup.SpawnAt(toDrop, spawnPos, defender.Stats.Name);
                defender.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText($"🗡️ DÉSARMÉ ! {droppedName}", Color.yellow);
                _host.ArenaLog($"🗡️ <b>DÉSARMEMENT</b> : {attacker.Stats.Name} arrache <b>{droppedName}</b> à {defender.Stats.Name} (Diff +{result.Differential} ≥ {threshold}) — tombe au sol !");
                SnapshotRecorder?.Invoke($"Désarmement : {attacker.Stats.Name} -> {defender.Stats.Name}");
            }
            catch (Exception e) { _host.ArenaLog($"⚠️ Désarmement impossible : {e.Message}"); }
        }

        /// <summary>
        /// RD-039 : Exécute une prise de lutte au corps-à-corps (2 PA).
        /// Duel opposé aveugle Athlétisme vs Athlétisme. Succès = cible Immobilisée et saisie en lutte.
        /// </summary>
        public void ExecuteGrapple(TacticalUnit actor, TacticalUnit target)
        {
            var calculator = _ctx.Calculator;
            if (actor == null || target == null || actor.Stats == null || target.Stats == null) return;
            if (!actor.Stats.IsAlive || !target.Stats.IsAlive) return;

            int dist = actor.CurrentCoords.DistanceTo(target.CurrentCoords);
            if (dist > 1)
            {
                _host.ArenaLog($"⚠️ <b>Lutte impossible</b> : cible hors de portée de contact ({dist} cases > 1).");
                return;
            }

            if (!_ctx.GetInfiniteAP() && actor.Stats.CurrentActionPoints < CombatDevArena.GrappleAPCost)
            {
                _host.ArenaLog($"⚠️ PA insuffisants : la prise de lutte exige {CombatDevArena.GrappleAPCost} PA (reste {actor.Stats.CurrentActionPoints}).");
                return;
            }

            if (!_ctx.GetInfiniteAP()) actor.Stats.ConsumeActionPoints(CombatDevArena.GrappleAPCost);

            int injectedAP = CombatUI.CombatContextMenuUI.CurrentInjectedAP;
            int injectedPE = CombatUI.CombatContextMenuUI.CurrentInjectedPE;

            var duel = calculator.ResolveGrappleDuel(
                actor.Stats, target.Stats,
                attackerBonusAP: injectedAP, attackerPE: injectedPE,
                defenderAutoStakes: true);

            var actVis = actor.GetComponent<TacticalUnitVisual>();
            var tgtVis = target.GetComponent<TacticalUnitVisual>();

            if (duel.AttackerWins)
            {
                actVis?.SpawnFloatingText("🥋 PRISE EN LUTTE (-2 PA)", Color.cyan);
                tgtVis?.TriggerHitFlash();
                tgtVis?.SpawnFloatingText("IMMOBILISÉ", new Color(1f, 0.45f, 0.15f));
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayAt(SoundId.Trauma_Shock, target.transform.position, 0.8f);
            }
            else
            {
                actVis?.SpawnFloatingText("Prise manquée (-2 PA)", Color.gray);
                tgtVis?.SpawnFloatingText("ESQUIVÉ", Color.cyan);
            }

            _host.ArenaLog($"🥋 <b>{actor.Stats.Name}</b> tente une prise de lutte sur <b>{target.Stats.Name}</b> (-{CombatDevArena.GrappleAPCost} PA) :\n   {duel.CombatLog}");
            SnapshotRecorder?.Invoke($"Lutte : {actor.Stats.Name} -> {target.Stats.Name}");
            _host.RequestCombatOverCheck();
        }

        /// <summary>
        /// RD-039 : Tente de se libérer d'une étreinte de lutte (2 PA).
        /// Duel opposé aveugle Athlétisme/MainsNues/Acrobatie (victime) vs Athlétisme (grappler).
        /// Succès = prise brisée et statut Immobilisé dissipé.
        /// </summary>
        public void ExecuteGrappleEscape(TacticalUnit victim, TacticalUnit grappler)
        {
            var calculator = _ctx.Calculator;
            if (victim == null || grappler == null || victim.Stats == null || grappler.Stats == null) return;
            if (!victim.Stats.IsAlive || !grappler.Stats.IsAlive) return;

            if (!_ctx.GetInfiniteAP() && victim.Stats.CurrentActionPoints < CombatDevArena.GrappleAPCost)
            {
                _host.ArenaLog($"⚠️ PA insuffisants : se dégager exige {CombatDevArena.GrappleAPCost} PA (reste {victim.Stats.CurrentActionPoints}).");
                return;
            }

            if (!_ctx.GetInfiniteAP()) victim.Stats.ConsumeActionPoints(CombatDevArena.GrappleAPCost);

            int injectedAP = CombatUI.CombatContextMenuUI.CurrentInjectedAP;
            int injectedPE = CombatUI.CombatContextMenuUI.CurrentInjectedPE;

            var duel = calculator.ResolveGrappleEscapeDuel(
                victim.Stats, grappler.Stats,
                victimBonusAP: injectedAP, victimPE: injectedPE,
                grapplerAutoStakes: true);

            var vicVis = victim.GetComponent<TacticalUnitVisual>();
            var grpVis = grappler.GetComponent<TacticalUnitVisual>();

            if (duel.AttackerWins)
            {
                vicVis?.SpawnFloatingText("🔓 DÉGAGÉ ! (-2 PA)", Color.green);
                grpVis?.SpawnFloatingText("Prise rompue", Color.gray);
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayAt(SoundId.Trauma_Shock, victim.transform.position, 0.8f);
            }
            else
            {
                vicVis?.SpawnFloatingText("Maintenu en lutte (-2 PA)", Color.red);
            }

            _host.ArenaLog($"🔓 <b>{victim.Stats.Name}</b> tente de se libérer de la prise de <b>{grappler.Stats.Name}</b> (-{CombatDevArena.GrappleAPCost} PA) :\n   {duel.CombatLog}");
            SnapshotRecorder?.Invoke($"Dégagement lutte : {victim.Stats.Name} -> {grappler.Stats.Name}");
            _host.RequestCombatOverCheck();
        }

        /// <summary>
        /// RD-039 : Étranglement au contact sur cible saisie ou immobilisée (2 PA).
        /// Duel opposé aveugle Athlétisme vs Endurance. Succès = Asphyxie (2 tours, DoT + drain PA).
        /// </summary>
        public void ExecuteStrangulation(TacticalUnit actor, TacticalUnit target)
        {
            var calculator = _ctx.Calculator;
            if (actor == null || target == null || actor.Stats == null || target.Stats == null) return;
            if (!actor.Stats.IsAlive || !target.Stats.IsAlive) return;

            int dist = actor.CurrentCoords.DistanceTo(target.CurrentCoords);
            if (dist > 1)
            {
                _host.ArenaLog($"⚠️ <b>Étranglement impossible</b> : hors de portée de contact ({dist} cases > 1).");
                return;
            }

            if (!_ctx.GetInfiniteAP() && actor.Stats.CurrentActionPoints < CombatDevArena.StrangulationAPCost)
            {
                _host.ArenaLog($"⚠️ PA insuffisants : l'étranglement exige {CombatDevArena.StrangulationAPCost} PA (reste {actor.Stats.CurrentActionPoints}).");
                return;
            }

            if (!_ctx.GetInfiniteAP()) actor.Stats.ConsumeActionPoints(CombatDevArena.StrangulationAPCost);

            int injectedAP = CombatUI.CombatContextMenuUI.CurrentInjectedAP;
            int injectedPE = CombatUI.CombatContextMenuUI.CurrentInjectedPE;

            var duel = calculator.ResolveStrangulationDuel(
                actor.Stats, target.Stats,
                attackerBonusAP: injectedAP, attackerPE: injectedPE,
                defenderAutoStakes: true);

            var actVis = actor.GetComponent<TacticalUnitVisual>();
            var tgtVis = target.GetComponent<TacticalUnitVisual>();

            if (duel.AttackerWins)
            {
                actVis?.SpawnFloatingText("🫁 ÉTRANGLEMENT (-2 PA)", new Color(0.2f, 0.95f, 0.75f));
                tgtVis?.TriggerHitFlash();
                tgtVis?.SpawnFloatingText("ASPHYXIE (-1 PA/tour)", new Color(0.2f, 0.95f, 0.75f));
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayAt(SoundId.Trauma_Shock, target.transform.position, 0.9f);
                if (!target.Stats.IsAlive) _host.RequestAutoTargetNextAlive();
            }
            else
            {
                actVis?.SpawnFloatingText("Étranglement repoussé (-2 PA)", Color.gray);
                tgtVis?.SpawnFloatingText("RÉSISTÉ", Color.cyan);
            }

            _host.ArenaLog($"🫁 <b>{actor.Stats.Name}</b> étrangle <b>{target.Stats.Name}</b> (-{CombatDevArena.StrangulationAPCost} PA) :\n   {duel.CombatLog}");
            SnapshotRecorder?.Invoke($"Étranglement : {actor.Stats.Name} -> {target.Stats.Name}");
            _host.RequestCombatOverCheck();
        }

        /// <summary>
        /// RD-040 : Exécute une Bousculade générique ou un Coup de bouclier (2 PA).
        /// Duel opposé aveugle de Force (Athlétisme / Mains Nues / Défense Corporelle).
        /// Victoire : recul forcé d'1 case (Knockback). Si un mur ou un obstacle bloque le recul, la cible chute À Terre.
        /// </summary>
        public void ExecuteShove(TacticalUnit attacker, TacticalUnit defender, bool isShieldBash, int attackerBonusAP = 0, int attackerPE = 0)
        {
            var calculator = _ctx.Calculator;
            if (_ctx.Guard.IsResolving)
            {
                _host.ArenaLog("⚠️ Action ignorée : une résolution est déjà en cours.");
                return;
            }
            if (attacker == null || defender == null || attacker.Stats == null || defender.Stats == null) return;
            if (!attacker.Stats.IsAlive || !defender.Stats.IsAlive) return;

            if (attacker.IsMoving)
            {
                _host.ArenaLog($"⚠️ <b>{attacker.Stats.Name}</b> est en déplacement : attendez l'arrivée !");
                return;
            }

            int dist = TitanFootprint.MinDistanceBetweenUnits(attacker.CurrentCoords, attacker.FootprintType, defender.CurrentCoords, defender.FootprintType);
            if (dist > 1)
            {
                _host.ArenaLog($"⚠️ <b>Bousculade impossible</b> : cible hors de portée de contact ({dist} cases > 1).");
                return;
            }

            if (TurnManager.IsMultiplayerPlayerClient())
            {
                _host.ArenaLog("⚠️ Bousculade réservée au GM en multijoueur (pas encore synchronisée VTT).");
                return;
            }

            SkillType checkSkill = isShieldBash ? SkillType.DefenseCorporelle : SkillType.Athletisme;
            DiceType atkDie = attacker.Stats.GetSkillDie(checkSkill, true);
            if (!attacker.Stats.CanAttack(atkDie))
            {
                int maxAttacks = attacker.Stats.GetMaxAttacksAllowed(atkDie);
                _host.ArenaLog($"⚠️ <b>{attacker.Stats.Name}</b> a déjà épuisé son quota d'attaque ce tour ({attacker.Stats.AttacksThisTurn}/{maxAttacks}) !");
                return;
            }

            const int shoveCost = 2;
            if (!_ctx.GetInfiniteAP() && attacker.Stats.CurrentActionPoints < shoveCost)
            {
                _host.ArenaLog($"⚠️ PA insuffisants : la bousculade exige {shoveCost} PA (reste {attacker.Stats.CurrentActionPoints}).");
                return;
            }

            if (!_ctx.GetInfiniteAP()) attacker.Stats.ConsumeActionPoints(shoveCost);

            int injectedAP = attackerBonusAP > 0 ? attackerBonusAP : CombatUI.CombatContextMenuUI.CurrentInjectedAP;
            int injectedPE = attackerPE > 0 ? attackerPE : CombatUI.CombatContextMenuUI.CurrentInjectedPE;

            Vector3 combatDir = defender.transform.position - attacker.transform.position;
            combatDir.y = 0f;
            if (combatDir != Vector3.zero)
            {
                attacker.transform.rotation = Quaternion.LookRotation(combatDir);
                if (defender.Stats.CanDefendActively())
                {
                    defender.transform.rotation = Quaternion.LookRotation(-combatDir);
                }
            }

            var attVis = attacker.GetComponent<TacticalUnitVisual>();
            var defVis = defender.GetComponent<TacticalUnitVisual>();

            if (isShieldBash)
            {
                attVis?.TriggerBodyBlock();
            }
            else
            {
                attVis?.TriggerRoundkick();
            }

            if (KilltimeAudioManager.Instance != null)
                KilltimeAudioManager.Instance.PlayAt(SoundId.Attack_Whoosh, attacker.transform.position, 0.7f);

            var result = calculator.ResolveShoveDuel(
                attacker.Stats, defender.Stats, isShieldBash,
                attackerBonusAP: injectedAP, attackerPE: injectedPE,
                defenderAutoStakes: true);

            attacker.Stats.RegisterAttack();
            _host.ArenaLog(result.CombatLog);

            string actionTag = isShieldBash ? "COUP DE BOUCLIER" : "BOUSCULADE";
            if (result.IsHit)
            {
                attVis?.SpawnFloatingText($"💨 {actionTag} (-{shoveCost} PA)", Color.cyan);
                defVis?.TriggerHitFlash();

                if (result.FinalDamageApplied > 0)
                {
                    defVis?.SpawnFloatingText($"-{result.FinalDamageApplied} PV", new Color(1.0f, 0.25f, 0.25f));
                }

                if (result.CausedKnockback)
                {
                    TryApplyKnockback(attacker, defender);
                }

                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayAt(SoundId.Trauma_Shock, defender.transform.position, 0.8f);

                if (result.FatalResolution == FatalBlowResolution.InstantDeath || result.FatalResolution == FatalBlowResolution.ForcedUnconscious)
                {
                    defVis?.TriggerFallingBackDeath();
                }
                else if (result.FatalResolution == FatalBlowResolution.EligibleForLastBreath)
                {
                    if (!defender.IsPlayerControlled)
                    {
                        defender.Stats.ChooseSombrer();
                        defVis?.SpawnFloatingText("[SYNCOPE] CHUTE HORS COMBAT (0 PV)", Color.cyan);
                        defVis?.TriggerFallingBackDeath();
                    }
                    else
                    {
                        defender.Stats.ChooseLastBreath();
                        defVis?.SpawnFloatingText("[SURVIE] DERNIER SOUFFLE (0 PV)", new Color(1.0f, 0.5f, 0.1f));
                    }
                }

                if (!defender.Stats.IsAlive)
                {
                    _host.RequestAutoTargetNextAlive();
                }
            }
            else
            {
                attVis?.SpawnFloatingText($"{actionTag} contenue (-{shoveCost} PA)", Color.gray);
                defVis?.SpawnFloatingText("RÉSISTÉ", Color.cyan);
            }

            SnapshotRecorder?.Invoke($"{actionTag} : {attacker.Stats.Name} -> {defender.Stats.Name}");
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
        }

        /// <summary>
        /// RD-039 : Vrai si le dragger peut traîner le corps de la victime d'1 case.
        /// </summary>
        public bool CanDragBody(TacticalUnit dragger, TacticalUnit victim)
        {
            var grid = _ctx.Grid;
            if (dragger == null || victim == null || dragger == victim) return false;
            if (dragger.Stats == null || victim.Stats == null) return false;
            if (!dragger.Stats.IsAlive) return false;
            if (!_ctx.GetInfiniteAP() && dragger.Stats.CurrentActionPoints < CombatDevArena.DragBodyAPCost) return false;
            if (dragger.CurrentCoords.DistanceTo(victim.CurrentCoords) > 1) return false;

            bool isDraggable = Killtime.Core.Combat.GrappleState.IsGrappling(dragger.Stats, victim.Stats)
                || victim.Stats.ActiveStatus.HasFlag(StatusEffect.Immobilise)
                || victim.Stats.ActiveStatus.HasFlag(StatusEffect.Inconscient)
                || victim.Stats.ActiveStatus.HasFlag(StatusEffect.ATerre)
                || !victim.Stats.IsAlive;

            if (!isDraggable) return false;
            if (grid == null) return false;

            for (int i = 0; i < 6; i++)
            {
                var n = dragger.CurrentCoords.GetNeighbor(i);
                if (n.Equals(victim.CurrentCoords)) continue;
                if (grid.IsFootprintFree(n, dragger.FootprintType, dragger)) return true;
            }
            return false;
        }

        /// <summary>
        /// RD-039 : Traîne le corps d'une cible saisie, immobilisée ou inconsciente d'1 case (2 PA).
        /// Le porteur recule ou pivote vers une case libre adjacente et tire la victime
        /// dans la case qu'il vient de libérer.
        /// </summary>
        public bool ExecuteDragBody(TacticalUnit dragger, TacticalUnit victim)
        {
            var grid = _ctx.Grid;
            if (!CanDragBody(dragger, victim)) return false;

            HexCoordinates bestHex = default;
            bool found = false;
            int bestDist = -1;

            for (int i = 0; i < 6; i++)
            {
                var n = dragger.CurrentCoords.GetNeighbor(i);
                if (n.Equals(victim.CurrentCoords)) continue;
                if (grid.IsFootprintFree(n, dragger.FootprintType, dragger))
                {
                    int d = n.DistanceTo(victim.CurrentCoords);
                    if (d > bestDist)
                    {
                        bestDist = d;
                        bestHex = n;
                        found = true;
                    }
                }
            }

            if (!found) return false;

            if (!_ctx.GetInfiniteAP()) dragger.Stats.ConsumeActionPoints(CombatDevArena.DragBodyAPCost);

            HexCoordinates vacated = dragger.CurrentCoords;

            var draggerPath = new List<HexCoordinates> { dragger.CurrentCoords, bestHex };
            var victimPath = new List<HexCoordinates> { victim.CurrentCoords, vacated };

            _host.ArenaStartCoroutine(dragger.MoveAlongPath(draggerPath, grid, 0, false, false));
            _host.ArenaStartCoroutine(victim.MoveAlongPath(victimPath, grid, 0, false, false));

            dragger.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("🤼 TRAÎNÉE (-2 PA)", Color.cyan);
            victim.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("TRAÎNÉ", Color.yellow);

            if (KilltimeAudioManager.Instance != null)
                KilltimeAudioManager.Instance.PlayAt(SoundId.Move_Footstep, dragger.transform.position, 0.7f);

            _host.ArenaLog($"🤼 <b>{dragger.Stats.Name}</b> traîne le corps de <b>{victim.Stats.Name}</b> d'1 case vers {vacated} (-{CombatDevArena.DragBodyAPCost} PA) !");
            SnapshotRecorder?.Invoke($"Traînée corps : {dragger.Stats.Name} -> {victim.Stats.Name}");
            return true;
        }

        /// <summary>Refoulement radial : repousse la cible sur le voisin le plus loin du souffle.</summary>
        public bool TryApplyBlastKnockback(TacticalUnit defender, HexCoordinates blastCoords)
        {
            var grid = _ctx.Grid;
            if (defender == null || grid == null) return false;
            HexCoordinates best = defender.CurrentCoords;
            int bestDist = defender.CurrentCoords.DistanceTo(blastCoords);
            for (int dir = 0; dir < 6; dir++)
            {
                var n = defender.CurrentCoords.GetNeighbor(dir);
                var node = grid.GetNode(n);
                if (node == null || !node.IsWalkable || node.IsOccupied) continue;
                int d = n.DistanceTo(blastCoords);
                if (d > bestDist) { bestDist = d; best = n; }
            }
            if (!best.Equals(defender.CurrentCoords) && defender.TeleportTo(best, grid))
            {
                Killtime.Core.Combat.GrappleState.ReleaseGrapple(defender.Stats);
                var vis = defender.GetComponent<TacticalUnitVisual>();
                vis?.SpawnFloatingText("[SOUFFLE] Projeté !", Color.yellow);
                _host.ArenaLog($"💨 <b>SOUFFLE</b> : {defender.Stats.Name} est projeté(e) en ({best.Q}, {best.R}) !");
                return true;
            }
            defender.Stats.ApplyStatus(StatusEffect.Destabilise, 1);
            return false;
        }

        /// <summary>
        /// Exécute une manœuvre de Charge (RD-038, Livre VI) :
        /// - Déplacement d'au moins 3 cases vers un voisin libre de la cible.
        /// - Débite les PA de déplacement (+ surcoût éventuel de manœuvre).
        /// - À l'arrivée au contact, exécute immédiatement l'attaque de mêlée (+2 dégâts, -1 défense jusqu'au prochain tour personnel).
        /// - Bloque le tir pour le reste du tour en cours.
        /// </summary>
        public void ExecuteChargeAttack(TacticalUnit actor, TacticalUnit target, BodyPart targetedPart, int attackerBonusAP = 0, int attackerPE = 0)
        {
            if (actor == null || target == null || actor == target) return;
            if (actor.Stats == null || !actor.Stats.IsAlive || target.Stats == null || !target.Stats.IsAlive) return;

            if (_ctx.Guard.IsResolving)
            {
                _host.ArenaLog("⚠️ Charge ignorée : une action est déjà en cours.");
                return;
            }

            int attackCost = CoreRulesConfig.Instance != null ? CoreRulesConfig.Instance.BaseAttackAPCost : 2;
            int ap = actor.Stats.CurrentActionPoints;
            var pathfinder = _ctx.Pathfinder;
            if (pathfinder == null) return;

            int minDistance = CoreRulesConfig.Instance != null ? CoreRulesConfig.Instance.ChargeMinDistance : 3;
            var path = pathfinder.FindChargePath(actor.CurrentCoords, target.CurrentCoords, ap, attackCost, out int moveCost, minDistance, actor.FootprintType);
            if (path == null || path.Count <= 1 || (path.Count - 1) < minDistance)
            {
                _host.ArenaLog($"⚠️ <b>Charge impossible</b> : aucun chemin de {minDistance}+ cases disponible vers {target.Stats.Name} pour {actor.Stats.Name}.");
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayUI(SoundId.UI_Denied, 0.6f);
                return;
            }

            int extraCost = CoreRulesConfig.Instance != null ? CoreRulesConfig.Instance.ChargeManeuverExtraAPCost : 0;
            int effectiveCost = actor.ComputeMovementAPCost(moveCost) + extraCost;
            if (actor.Stats.CurrentActionPoints < effectiveCost + attackCost)
            {
                _host.ArenaLog($"⚠️ <b>Charge impossible</b> : PA insuffisants ({actor.Stats.CurrentActionPoints} PA disponibles pour un coût de {effectiveCost + attackCost} PA).");
                return;
            }

            _host.ArenaLog($"⚡ <b>{actor.Stats.Name}</b> s'élance en <b>CHARGE D'ASSAUT</b> sur {target.Stats.Name} ({path.Count - 1} cases, coût déplacement {effectiveCost} PA) !");
            _host.ArenaStartCoroutine(ChargeAndStrikeRoutine(actor, target, targetedPart, path, effectiveCost, attackCost, attackerBonusAP, attackerPE));
        }

        private IEnumerator ChargeAndStrikeRoutine(
            TacticalUnit actor,
            TacticalUnit target,
            BodyPart targetedPart,
            List<HexCoordinates> path,
            int moveCost,
            int attackCost,
            int attackerBonusAP,
            int attackerPE)
        {
            var grid = _ctx.Grid;
            _ctx.Guard.TryEnter();
            if (KilltimeAudioManager.Instance != null)
                KilltimeAudioManager.Instance.PlayAt(SoundId.Move_Dash, actor.transform.position, 0.7f);

            // Exécute le déplacement jusqu'au contact
            yield return _host.ArenaStartCoroutine(actor.MoveAlongPath(path, grid, moveCost));

            // Attend la fin effective du mouvement
            while (actor.IsMoving)
            {
                yield return null;
            }

            _ctx.Guard.ForceReset();

            // Si l'attaquant a survécu et est toujours au contact de la cible
            if (actor.Stats != null && actor.Stats.IsAlive && target.Stats != null && target.Stats.IsAlive)
            {
                int dist = actor.CurrentCoords.DistanceTo(target.CurrentCoords);
                if (dist <= 1)
                {
                    var weapon = actor.Sheet?.GetEquippedWeapon();
                    SkillType meleeSkill = weapon != null
                        ? SkillDefinitions.ResolveBaseSkill(weapon.AssociatedSkill)
                        : SkillType.MainsNues;

                    ChargeStrike?.Invoke(actor, target, targetedPart, meleeSkill, attackerBonusAP, attackerPE);
                }
                else
                {
                    _host.ArenaLog($"⚠️ <b>Charge interrompue</b> : {actor.Stats.Name} n'a pas pu atteindre le contact de {target.Stats.Name}.");
                }
            }
        }

        /// <summary>
        /// RD-084 : Collecte les participants d'un assaut groupé : alliés de
        /// l'acteur (même camp) vivants, avec assez de PA, à portée de la cible
        /// pour la compétence partagée (contact ≤ 1 en mêlée, portée d'arme au tir).
        /// </summary>
        public List<TacticalUnit> CollectCombinedAttackers(TacticalUnit actor, TacticalUnit target, SkillType attackSkill)
        {
            var turnManager = _ctx.TurnManager;
            var members = new List<TacticalUnit>();
            if (actor == null || target == null) return members;
            if (turnManager == null) return members;

            int cost = CoreRulesConfig.Instance != null ? CoreRulesConfig.Instance.CombinedAttackAPCost : 2;
            bool isRanged = (attackSkill == SkillType.Ballistique || attackSkill == SkillType.ProjectilesTir);

            var order = turnManager.TurnOrder;
            for (int i = 0; i < order.Count; i++)
            {
                var u = order[i];
                if (u == null || u.Stats == null || !u.Stats.IsAlive) continue;
                if (u == target) continue;
                if (u.IsPlayerControlled != actor.IsPlayerControlled) continue;
                if (u.Stats.CurrentActionPoints < cost) continue;

                int dist = TitanFootprint.MinDistanceBetweenUnits(u.CurrentCoords, u.FootprintType, target.CurrentCoords, target.FootprintType);
                if (isRanged)
                {
                    var w = u.Sheet?.GetEquippedWeapon();
                    int maxRange = (w != null && w.RangeInTiles > 1) ? w.RangeInTiles : 1;
                    if (dist > maxRange) continue;
                    if (!ChargeState.CanFireRanged(u.Stats)) continue;
                }
                else
                {
                    if (dist > 1) continue;
                }
                members.Add(u);
            }

            // L'acteur en tête s'il est éligible (pour l'ordre du log).
            if (members.Contains(actor))
            {
                members.Remove(actor);
                members.Insert(0, actor);
            }
            return members;
        }

        /// <summary>
        /// RD-084 : Résout la compétence partagée de l'assaut (mêlée au contact,
        /// Ballistique à distance si l'acteur a une arme à distance).
        /// </summary>
        public bool TryResolveCombinedSkill(TacticalUnit actor, TacticalUnit target, out SkillType attackSkill)
        {
            attackSkill = SkillType.MainsNues;
            if (actor == null || target == null) return false;
            int dist = TitanFootprint.MinDistanceBetweenUnits(actor.CurrentCoords, actor.FootprintType, target.CurrentCoords, target.FootprintType);
            if (dist <= 1)
            {
                var weapon = actor.Sheet?.GetEquippedWeapon();
                attackSkill = weapon != null
                    ? SkillDefinitions.ResolveBaseSkill(weapon.AssociatedSkill)
                    : SkillType.MainsNues;
                // Arme à distance au contact : coup de crosse d'urgence à mains nues.
                if (attackSkill == SkillType.Ballistique || attackSkill == SkillType.ProjectilesTir)
                    attackSkill = SkillType.MainsNues;
                return true;
            }
            var ranged = actor.Sheet?.GetEquippedWeapon();
            if (ranged != null && ranged.RangeInTiles > 1)
            {
                attackSkill = SkillType.Ballistique;
                return true;
            }
            return false;
        }

        /// <summary>
        /// RD-084 : Exécute un assaut groupé (Livre VI §25.2). Les participants
        /// sont synchronisés sur l'initiative du membre le plus lent, leurs
        /// résultats offensifs sommés face à l'unique défense de la cible.
        /// </summary>
        public void ExecuteCombinedAttack(TacticalUnit actor, TacticalUnit target, BodyPart targetedPart = BodyPart.Torse)
        {
            var turnManager = _ctx.TurnManager;
            var calculator = _ctx.Calculator;
            if (actor == null || target == null || actor.Stats == null || target.Stats == null) return;
            if (!actor.Stats.IsAlive || !target.Stats.IsAlive) return;
            if (actor == target) return;

            if (!TryResolveCombinedSkill(actor, target, out SkillType attackSkill))
            {
                _host.ArenaLog($"⚠️ <b>Assaut groupé impossible</b> : {actor.Stats.Name} hors de portée de {target.Stats.Name} (ni contact ni arme à distance).");
                return;
            }

            var members = CollectCombinedAttackers(actor, target, attackSkill);
            int minMembers = CoreRulesConfig.Instance != null ? Math.Max(2, CoreRulesConfig.Instance.CombinedAttackMinMembers) : 2;
            if (members.Count < minMembers)
            {
                _host.ArenaLog($"⚠️ <b>Assaut groupé impossible</b> : {members.Count} assaillant(s) à portée, {minMembers} minimum requis (Livre VI §25.2).");
                return;
            }

            int slowest = turnManager != null ? turnManager.GetSlowestInitiativeFor(members) : GroupAssaultState.GetSlowestInitiative(ToStatsList(members));

            // Dégâts d'arme par participant (arme équipée ou 5 à mains nues).
            var weapons = new List<int>(members.Count);
            for (int i = 0; i < members.Count; i++)
            {
                var w = members[i].Sheet?.GetEquippedWeapon();
                weapons.Add((w != null && w.BaseDamage > 0) ? w.BaseDamage : 5);
            }

            // Mise injectée du menu (attribuée à l'acteur, tête de colonne).
            var bonusAP = new List<int>(members.Count);
            var bonusPE = new List<int>(members.Count);
            for (int i = 0; i < members.Count; i++) { bonusAP.Add(0); bonusPE.Add(0); }
            int injectedAP = CombatUI.CombatContextMenuUI.CurrentInjectedAP;
            int injectedPE = CombatUI.CombatContextMenuUI.CurrentInjectedPE;
            if (members[0] == actor)
            {
                bonusAP[0] = Math.Max(0, injectedAP);
                bonusPE[0] = Math.Max(0, injectedPE);
            }

            var stats = ToStatsList(members);
            var combined = calculator.ResolveCombinedAttack(
                stats, target.Stats, targetedPart,
                attackSkill, SkillType.Esquive,
                weaponBaseDamages: weapons,
                attackerBonusAP: bonusAP, attackerPE: bonusPE,
                defenderAutoStakes: true);

            var actVis = actor.GetComponent<TacticalUnitVisual>();
            var tgtVis = target.GetComponent<TacticalUnitVisual>();

            if (!combined.IsValid && !combined.IsHit)
            {
                _host.ArenaLog($"⚔️ <b>{actor.Stats.Name}</b> appelle l'assaut groupé sur <b>{target.Stats.Name}</b> :\n   {combined.CombatLog}");
                return;
            }

            if (combined.IsHit)
            {
                actVis?.SpawnFloatingText($"⚔️ ASSAUT GROUPÉ x{members.Count} (ini {slowest})", Color.cyan);
                tgtVis?.TriggerHitFlash();
                tgtVis?.SpawnFloatingText($"-{combined.FinalDamageApplied} PV (sommation)", new Color(1.0f, 0.25f, 0.25f));
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayAt(SoundId.Attack_Whoosh, target.transform.position, 0.9f);

                if (combined.FatalResolution == FatalBlowResolution.InstantDeath || combined.FatalResolution == FatalBlowResolution.ForcedUnconscious)
                {
                    tgtVis?.TriggerFallingBackDeath();
                }
                else if (combined.FatalResolution == FatalBlowResolution.EligibleForLastBreath)
                {
                    if (!target.IsPlayerControlled)
                    {
                        target.Stats.ChooseSombrer();
                        tgtVis?.SpawnFloatingText("[SYNCOPE] CHUTE HORS COMBAT (0 PV)", Color.cyan);
                        tgtVis?.TriggerFallingBackDeath();
                    }
                    else
                    {
                        target.Stats.ChooseLastBreath();
                        tgtVis?.SpawnFloatingText("[SURVIE] DERNIER SOUFFLE (0 PV)", new Color(1.0f, 0.5f, 0.1f));
                    }
                }

                if (!target.Stats.IsAlive) _host.RequestAutoTargetNextAlive();
            }
            else
            {
                actVis?.SpawnFloatingText($"Assaut groupé paré x{members.Count}", Color.gray);
                tgtVis?.SpawnFloatingText("PARÉ", Color.cyan);
            }

            _host.ArenaLog($"⚔️ <b>{actor.Stats.Name}</b> déclenche un <b>ASSAUT GROUPÉ x{members.Count}</b> sur <b>{target.Stats.Name}</b> (synchro initiative la plus lente : {slowest}) :\n   {combined.CombatLog}");
            _overwatch.HandleChannelInterruptionFx(target, combined.ChannelInterrupted);
            SnapshotRecorder?.Invoke($"Assaut groupé x{members.Count} : {actor.Stats.Name} -> {target.Stats.Name}");
            _host.RequestMusicUpdate();
            _host.RequestCombatOverCheck();
        }

        private static List<CharacterStats> ToStatsList(List<TacticalUnit> units)
        {
            var stats = new List<CharacterStats>(units != null ? units.Count : 0);
            if (units == null) return stats;
            for (int i = 0; i < units.Count; i++)
            {
                if (units[i] != null && units[i].Stats != null) stats.Add(units[i].Stats);
            }
            return stats;
        }

        /// <summary>
        /// RD-084 : Exécute un round de course-poursuite (Livre VI §25.1) :
        /// concours d'Athlétisme proie vs poursuivants. Succès proie = distance
        /// maintenue (≥ 1 case), faillite = terrain gagné. À 4 faillites cumulées :
        /// arrêt et capture immédiate (Immobilisé).
        /// </summary>
        public void ExecutePursuitRound(TacticalUnit prey, List<TacticalUnit> pursuers)
        {
            var calculator = _ctx.Calculator;
            if (prey == null || prey.Stats == null || !prey.Stats.IsAlive) return;
            if (pursuers == null || pursuers.Count == 0) return;

            var alivePursuers = new List<TacticalUnit>();
            for (int i = 0; i < pursuers.Count; i++)
            {
                var p = pursuers[i];
                if (p != null && p.Stats != null && p.Stats.IsAlive && p != prey) alivePursuers.Add(p);
            }
            if (alivePursuers.Count == 0)
            {
                _host.ArenaLog($"⚠️ <b>Poursuite impossible</b> : aucun poursuivant valide pour {prey.Stats.Name}.");
                return;
            }

            int cost = CoreRulesConfig.Instance != null ? CoreRulesConfig.Instance.PursuitAPCost : 1;
            if (!_ctx.GetInfiniteAP())
            {
                if (prey.Stats.CurrentActionPoints < cost)
                {
                    _host.ArenaLog($"⚠️ PA insuffisants : la fuite exige {cost} PA (reste {prey.Stats.CurrentActionPoints}).");
                    return;
                }
                for (int i = 0; i < alivePursuers.Count; i++)
                {
                    if (alivePursuers[i].Stats.CurrentActionPoints < cost)
                    {
                        _host.ArenaLog($"⚠️ PA insuffisants : {alivePursuers[i].Stats.Name} ne peut poursuivre ({alivePursuers[i].Stats.CurrentActionPoints}/{cost} PA).");
                        return;
                    }
                }
                prey.Stats.ConsumeActionPoints(cost);
                for (int i = 0; i < alivePursuers.Count; i++) alivePursuers[i].Stats.ConsumeActionPoints(cost);
            }

            int injectedAP = CombatUI.CombatContextMenuUI.CurrentInjectedAP;
            int injectedPE = CombatUI.CombatContextMenuUI.CurrentInjectedPE;

            var pursuerStats = new List<CharacterStats>(alivePursuers.Count);
            for (int i = 0; i < alivePursuers.Count; i++) pursuerStats.Add(alivePursuers[i].Stats);

            var round = calculator.ResolvePursuitRound(
                prey.Stats, pursuerStats,
                preyBonusAP: Math.Max(0, injectedAP), preyPE: Math.Max(0, injectedPE),
                pursuersAutoStakes: true);

            var preyVis = prey.GetComponent<TacticalUnitVisual>();
            if (round.Caught)
            {
                preyVis?.SpawnFloatingText("🛑 RATTRAPÉ ET ACCULÉ !", Color.red);
                preyVis?.TriggerHitFlash();
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayAt(SoundId.Trauma_Shock, prey.transform.position, 0.9f);
            }
            else if (round.FailuresAdded > 0)
            {
                preyVis?.SpawnFloatingText($"🏃 Poursuivi ! ({round.TotalFailures}/{round.FailuresToCapture})", new Color(1.0f, 0.6f, 0.1f));
            }
            else
            {
                preyVis?.SpawnFloatingText("🏃 DISTANCE MAINTENUE", Color.green);
            }

            string pursuerNames = "";
            for (int i = 0; i < alivePursuers.Count; i++)
            {
                pursuerNames += (i > 0 ? ", " : "") + alivePursuers[i].Stats.Name;
            }
            _host.ArenaLog($"🏃 <b>Course-poursuite</b> : {prey.Stats.Name} vs {pursuerNames} (-{cost} PA chacun) :\n   {round.CombatLog}");
            SnapshotRecorder?.Invoke($"Poursuite : {prey.Stats.Name} ({round.TotalFailures}/{round.FailuresToCapture})");
            _host.RequestCombatOverCheck();
        }

        public bool TryApplyKnockback(TacticalUnit attacker, TacticalUnit defender)
        {
            var grid = _ctx.Grid;
            if (attacker == null || defender == null || grid == null) return false;

            int dq = defender.CurrentCoords.Q - attacker.CurrentCoords.Q;
            int dr = defender.CurrentCoords.R - attacker.CurrentCoords.R;

            bool isTeepDeRupture = attacker.Stats != null && attacker.Stats.HasSpecialization("Teep de Rupture");
            bool isBedrock = attacker.Stats != null && attacker.Stats.HasSpecialization("Impact de Bedrock");
            int pushDistance = 1;
            if (isTeepDeRupture)
            {
                if (attacker.Stats.HasSpecialization("Teep de Rupture : Brise-Châssis Titan")) pushDistance = 4;
                else if (attacker.Stats.HasSpecialization("Teep de Rupture : Onde de Choc Linéaire")) pushDistance = 3;
                else pushDistance = 2;
            }
            else if (isBedrock)
            {
                pushDistance = 2;
            }

            HexCoordinates currentDest = defender.CurrentCoords;
            int tilesMoved = 0;

            for (int step = 1; step <= pushDistance; step++)
            {
                var nextCoords = new HexCoordinates(defender.CurrentCoords.Q + dq * step, defender.CurrentCoords.R + dr * step);
                var nextNode = grid.GetNode(nextCoords);

                if (nextNode != null && nextNode.IsWalkable && !nextNode.IsOccupied)
                {
                    currentDest = nextCoords;
                    tilesMoved++;
                }
                else
                {
                    if (tilesMoved > 0)
                    {
                        defender.TeleportTo(currentDest, grid);
                    }
                    var defVis = defender.GetComponent<TacticalUnitVisual>();
                    if (isTeepDeRupture)
                    {
                        bool isDevastating = attacker.Stats.HasSpecialization("Teep de Rupture : Impact Dévastateur");
                        int shockDamage = isDevastating ? 6 : 3;
                        defender.Stats.ApplyStatus(StatusEffect.Sonne, 1);
                        defender.Stats.ApplyStatus(StatusEffect.ATerre, 1);
                        if (isDevastating) defender.Stats.ApplyStatus(StatusEffect.Destabilise, 2);
                        defender.Stats.CurrentHealth = Math.Max(0, defender.Stats.CurrentHealth - shockDamage);
                        defVis?.TriggerHitFlash();
                        string shockLabel = isDevastating ? $"[TEEP DÉVASTATEUR] SONNÉ (-{shockDamage} PV) !" : $"[TEEP PAROI] SONNÉ (-{shockDamage} PV) !";
                        defVis?.SpawnFloatingText(shockLabel, Color.red);
                        _host.ArenaLog($"💥 <b>TEEP DE RUPTURE (Collision)</b> : {defender.Stats.Name} est projeté(e) contre un obstacle, subit {shockDamage} dégâts de choc et [Sonné / À Terre] !");
                    }
                    else
                    {
                        defender.Stats.ApplyStatus(StatusEffect.ATerre, 1);
                        defender.Stats.ApplyStatus(StatusEffect.Destabilise, 1);
                        defVis?.SpawnFloatingText("[IMPACT PAROI] À TERRE !", Color.red);
                        _host.ArenaLog($"💥 <b>IMPACT CONTRE OBSTACLE</b> : {defender.Stats.Name} heurte la paroi et s'écroule <b>À Terre</b> !");
                    }
                    return tilesMoved > 0;
                }
            }

            if (tilesMoved > 0)
            {
                defender.TeleportTo(currentDest, grid);
                Killtime.Core.Combat.GrappleState.ReleaseGrapple(defender.Stats);
                var defVis = defender.GetComponent<TacticalUnitVisual>();
                string teepTag = isTeepDeRupture ? "[TEEP DE RUPTURE]" : (isBedrock ? "[IMPACT DE BEDROCK]" : "[REFOULEMENT]");
                defVis?.SpawnFloatingText($"{teepTag} Repoussé ({tilesMoved} cases) !", Color.yellow);
                _host.ArenaLog($"💨 <b>{teepTag}</b> : {defender.Stats.Name} est repoussé(e) de {tilesMoved} case(s) en ({currentDest.Q}, {currentDest.R}) !");
                return true;
            }

            return false;
        }
    }
}

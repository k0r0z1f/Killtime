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
    /// Entretien d'armes RD-033 + desarmement (munitions, recharge, desenrayage, bouclier).
    /// Split move-only : contenu verbatim depuis ArenaMeleeService.cs.
    /// </summary>
    public sealed partial class ArenaMeleeService
    {
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
                // RD-204 (Livre V) : l'arme arrachée vole à DisarmDropDistance
                // cases dans l'axe attaquant -> défenseur (repli : case du porteur).
                int dropDistance = 2;
                try
                {
                    var cfg = Killtime.Core.Rules.CoreRulesConfig.Instance;
                    if (cfg != null && cfg.DisarmDropDistance > 0) dropDistance = cfg.DisarmDropDistance;
                }
                catch { }
                Vector3 spawnPos = defender.transform.position;
                try
                {
                    var dropCoords = DisarmDropRules.ComputeDropCoords(attacker.CurrentCoords, defender.CurrentCoords, dropDistance);
                    var dropNode = grid != null ? grid.GetNode(dropCoords) : null;
                    if (dropNode != null) spawnPos = dropNode.WorldPosition;
                    else
                    {
                        var homeNode = grid != null ? grid.GetNode(defender.CurrentCoords) : null;
                        if (homeNode != null) spawnPos = homeNode.WorldPosition;
                    }
                }
                catch { }
                DroppedWeaponPickup.SpawnAt(toDrop, spawnPos, defender.Stats.Name);
                defender.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText($"🗡️ DÉSARMÉ ! {droppedName}", Color.yellow);
                _host.ArenaLog($"🗡️ <b>DÉSARMEMENT</b> : {attacker.Stats.Name} arrache <b>{droppedName}</b> à {defender.Stats.Name} (Diff +{result.Differential} ≥ {threshold}) — tombe à {dropDistance} cases !");
                SnapshotRecorder?.Invoke($"Désarmement : {attacker.Stats.Name} -> {defender.Stats.Name}");
            }
            catch (Exception e) { _host.ArenaLog($"⚠️ Désarmement impossible : {e.Message}"); }
        }
    }
}

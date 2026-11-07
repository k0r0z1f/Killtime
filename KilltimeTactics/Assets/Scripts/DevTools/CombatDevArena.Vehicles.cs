using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Core.Combat;
using Killtime.Core.Character;
using Killtime.Tactics.Arena;

namespace Killtime.Tactics
{
    // RD-195 : branchement véhicules en arène (Livre V §22.3).
    // Socle pur déjà couvert par RD-051 (VehicleMechanics/VehicleState +
    // SpecialManeuverService.Vehicle) : ce partiel ajoute le placement
    // figurine, la trajectoire d'écrasement et les frappes de sous-système
    // pilotables depuis la toolbar MJ + le registre d'actions.
    public partial class CombatDevArena : MonoBehaviour, IArenaHost
    {
        public const int VehicleStrikeAPCost = 2;
        public const int VehicleRunOverAPCost = 2;

        private int _vehicleSeq;
        private readonly Dictionary<string, HexCoordinates> _vehicleCoords = new();
        private readonly Dictionary<string, GameObject> _vehicleFigurines = new();

        // --- Helpers purs (testables EditMode sans scène) ---

        public static string VehicleLabelFor(VehicleScale scale)
        {
            switch (scale)
            {
                case VehicleScale.Velo: return "Vélo (1-10 PV)";
                case VehicleScale.Charrette: return "Charrette (11-50 PV)";
                case VehicleScale.JetTank: return "Jet/Tank (51-1000 PV)";
                case VehicleScale.Croiseur: return "Croiseur (1001-10000 PV)";
                default: return "Station (10001+ PV)";
            }
        }

        public static string FormatStrikePreview(int rawDamage, int heavyBonus)
        {
            if (heavyBonus > 0) return $"{rawDamage} bruts +{heavyBonus} lourde = {rawDamage + heavyBonus}";
            return $"{rawDamage} bruts (arme légère)";
        }

        public static VehicleSubsystem DefaultEssentialTarget => VehicleSubsystem.Moteur;
        public static VehicleSubsystem DefaultSuperficialTarget => VehicleSubsystem.Roues;

        // --- Cycle de vie ---

        /// <summary>
        /// Déploie un véhicule : registre VehicleState (PV = Max du palier sauf
        /// hpOverride) + figurine cylindre à la case libre la plus proche.
        /// Retourne l'id ("veh-<gabarit>-<n>"), null si refusé.
        /// </summary>
        public string SpawnArenaVehicle(VehicleScale scale, HexCoordinates at, int? hpOverride = null)
        {
            if (_grid == null) EnsureDependencies();
            if (_grid == null) return null;
            HexCoordinates coords = _unitStore != null ? _unitStore.ResolveFreeCoords(at) : at;
            var node = _grid.GetNode(coords);
            if (node == null || !node.IsWalkable) return null;

            string id = $"veh-{scale.ToString().ToLowerInvariant()}-{++_vehicleSeq}";
            int hp = hpOverride.HasValue
                ? VehicleMechanics.ClampHp(scale, hpOverride.Value)
                : VehicleMechanics.DefaultHpFor(scale);
            var rec = VehicleState.Register(id, scale, hp);
            if (rec == null) return null;

            _vehicleCoords[id] = coords;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = $"[Vehicule] {id}";
            float girth = scale switch
            {
                VehicleScale.Velo => 0.35f,
                VehicleScale.Charrette => 0.6f,
                VehicleScale.JetTank => 0.9f,
                VehicleScale.Croiseur => 1.4f,
                _ => 2.0f,
            };
            go.transform.localScale = new Vector3(girth, 0.6f + girth * 0.4f, girth);
            Vector3 pos = node.WorldPosition;
            pos.y += 0.4f;
            go.transform.position = pos;
            var rend = go.GetComponent<Renderer>();
            if (rend != null) rend.material.color = new Color(0.35f, 0.45f, 0.3f);
            _vehicleFigurines[id] = go;

            Log($"🚙 <b>Véhicule déployé</b> : {id} ({VehicleLabelFor(scale)}, {hp} PV/sous-système) en ({coords.Q},{coords.R}) — armes lourdes +{VehicleMechanics.HeavyBonusFor()} vs blindés.");
            RecordChronoSnapshot($"Déploiement véhicule {id}");
            return id;
        }

        public List<string> ListArenaVehicles() => new(_vehicleCoords.Keys);

        public HexCoordinates? GetVehicleCoords(string vehicleId)
        {
            if (string.IsNullOrEmpty(vehicleId)) return null;
            return _vehicleCoords.TryGetValue(vehicleId, out var c) ? c : null;
        }

        public VehicleRecord GetVehicleRecord(string vehicleId) => VehicleState.Get(vehicleId);

        public bool MoveArenaVehicle(string vehicleId, HexCoordinates to)
        {
            var rec = VehicleState.Get(vehicleId);
            if (rec == null || rec.Destroyed || _grid == null) return false;
            var node = _grid.GetNode(to);
            if (node == null || !node.IsWalkable) return false;
            _vehicleCoords[vehicleId] = to;
            if (_vehicleFigurines.TryGetValue(vehicleId, out var go) && go != null)
            {
                Vector3 pos = node.WorldPosition;
                pos.y += 0.4f;
                go.transform.position = pos;
            }
            Log($"🚙 <b>{vehicleId}</b> manœuvre en ({to.Q},{to.R}).");
            return true;
        }

        // --- Embarquement ---

        public bool TryEmbarkArenaVehicle(TacticalUnit unit, string vehicleId, out string message)
        {
            message = "";
            if (unit == null || unit.Stats == null) { message = "⛔ Embarquement impossible : unité invalide."; return false; }
            var at = unit.CurrentCoords;
            if (!TryGetNearestVehicle(at, 1, out string near, out _) || near != vehicleId)
            {
                var rec0 = VehicleState.Get(vehicleId);
                if (rec0 == null) { message = $"⛔ Embarquement impossible : véhicule « {vehicleId} » inconnu."; return false; }
                if (rec0.Destroyed) { message = $"⛔ Embarquement impossible : {vehicleId} est une épave."; return false; }
                message = $"⛔ Embarquement impossible ({unit.Stats.Name}) : {vehicleId} trop loin — contact (1 case) requis.";
                return false;
            }
            bool ok = VehicleState.TryEmbark(unit.Stats, vehicleId, out message);
            if (ok)
            {
                Log(message);
                RecordChronoSnapshot($"Embarquement : {unit.Stats.Name} -> {vehicleId}");
            }
            else Log(message);
            return ok;
        }

        public bool TryDisembarkArenaVehicle(TacticalUnit unit, out string message)
        {
            message = "";
            if (unit == null || unit.Stats == null) return false;
            bool ok = VehicleState.Disembark(unit.Stats, out message);
            Log(message);
            if (ok) RecordChronoSnapshot($"Débarquement : {unit.Stats.Name}");
            return ok;
        }

        public bool TryGetNearestVehicle(HexCoordinates from, int maxDist, out string vehicleId, out int dist)
        {
            vehicleId = null;
            dist = int.MaxValue;
            foreach (var kv in _vehicleCoords)
            {
                var rec = VehicleState.Get(kv.Key);
                if (rec == null || rec.Destroyed) continue;
                int d = from.DistanceTo(kv.Value);
                if (d <= maxDist && d < dist) { dist = d; vehicleId = kv.Key; }
            }
            return vehicleId != null;
        }

        // --- Écrasement (trajectoire + projection 1 case + À Terre) ---

        /// <summary>
        /// MJ / registre : le véhicule roule sur le piéton (mobile = vrai).
        /// Figurine déplacée sur la case du piéton, dégâts fixes du gabarit via
        /// VehicleState.TryRunOver, puis projection 1 case (voisin libre le plus
        /// loin du véhicule, sinon sur place) + À Terre déjà posée par l'état.
        /// Sans PA (le registre paie 2 PA avant d'appeler).
        /// </summary>
        public bool ExecuteVehicleRunOver(string vehicleId, TacticalUnit pedestrian)
        {
            var rec = VehicleState.Get(vehicleId);
            if (rec == null || rec.Destroyed) { Log($"⛔ Écrasement impossible : {vehicleId} inconnu ou épave."); return false; }
            if (pedestrian == null || pedestrian.Stats == null || !pedestrian.Stats.IsAlive) return false;

            bool ok = VehicleState.TryRunOver(vehicleId, pedestrian.Stats, true, out string log);
            Log(log);
            if (!ok) return false;

            _vehicleCoords[vehicleId] = pedestrian.CurrentCoords;
            if (_vehicleFigurines.TryGetValue(vehicleId, out var go) && go != null && _grid != null)
            {
                var node = _grid.GetNode(pedestrian.CurrentCoords);
                if (node != null) { Vector3 pos = node.WorldPosition; pos.y += 0.4f; go.transform.position = pos; }
            }

            var vis = pedestrian.GetComponent<TacticalUnitVisual>();
            vis?.TriggerHitFlash();
            vis?.SpawnFloatingText("🚛 ÉCRASÉ [À TERRE]", Color.red);

            if (_grid != null && pedestrian.Stats.IsAlive)
            {
                HexCoordinates best = pedestrian.CurrentCoords;
                int bestDist = pedestrian.CurrentCoords.DistanceTo(_vehicleCoords[vehicleId]);
                for (int dir = 0; dir < 6; dir++)
                {
                    var n = pedestrian.CurrentCoords.GetNeighbor(dir);
                    var node = _grid.GetNode(n);
                    if (node == null || !node.IsWalkable || node.IsOccupied) continue;
                    int d = n.DistanceTo(_vehicleCoords[vehicleId]);
                    if (d > bestDist) { bestDist = d; best = n; }
                }
                if (!best.Equals(pedestrian.CurrentCoords) && pedestrian.TeleportTo(best, _grid))
                    Log($"💨 <b>{pedestrian.Stats.Name}</b> projeté(e) en ({best.Q},{best.R}) (projection {VehicleMechanics.RunOverKnockbackTiles} case).");
            }

            if (!pedestrian.Stats.IsAlive) vis?.TriggerFallingBackDeath();
            RecordChronoSnapshot($"Écrasement : {vehicleId} -> {pedestrian.Stats.Name}");
            try { _turnManager?.CheckCombatOver(); } catch { /* jamais bloquant */ }
            return true;
        }

        // --- Frappe sous-système nommé (+100 lourde visible) ---

        /// <summary>
        /// Frappe directe d'un sous-système (2 PA, sans duel : bruts de l'arme +
        /// bonus lourde +100 si lourde vs véhicule). Essentiel à 0 = épave +
        /// débarquement forcé (état) + figurine grisée ; superficiel à 0 =
        /// fonction perdue, engin intact.
        /// </summary>
        public bool ExecuteVehicleSubsystemStrike(TacticalUnit attacker, string vehicleId, VehicleSubsystem subsystem)
        {
            var rec = VehicleState.Get(vehicleId);
            if (rec == null) { Log($"⛔ Frappe impossible : véhicule « {vehicleId} » inconnu."); return false; }
            if (rec.Destroyed) { Log($"⛔ Frappe inutile : {vehicleId} est déjà une épave."); return false; }
            if (attacker == null || attacker.Stats == null || !attacker.Stats.IsAlive) return false;
            if (attacker.Stats.CurrentActionPoints < VehicleStrikeAPCost)
            {
                Log($"⛔ Frappe impossible ({attacker.Stats.Name} : {attacker.Stats.CurrentActionPoints} PA, coût {VehicleStrikeAPCost}).");
                return false;
            }

            var weapon = attacker.Sheet?.GetEquippedWeapon();
            int raw = weapon != null ? weapon.BaseDamage : 3;
            bool heavy = VehicleMechanics.IsHeavyWeapon(weapon);
            int bonus = VehicleMechanics.ComputeHeavyBonus(heavy, true);

            if (!attacker.Stats.ConsumeActionPoints(VehicleStrikeAPCost)) return false;

            bool ok = SpecialManeuverService.StrikeVehicleSubsystem(vehicleId, subsystem, raw, weapon, out var hit);
            if (!ok) return false;

            string heavyTag = bonus > 0 ? $" +{bonus} lourde" : " (arme légère)";
            Log($"🔫 <b>{attacker.Stats.Name}</b> crible <b>{vehicleId}.{subsystem}</b> : {raw} bruts{heavyTag} = <b>{hit.DamageDealt}</b> → {hit.CurrentHp} PV. {hit.LogFragment}");
            attacker.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText($"🔫 {hit.DamageDealt} → {vehicleId}", UnityEngine.Color.yellow);

            if (hit.Destroyed)
            {
                if (_vehicleFigurines.TryGetValue(vehicleId, out var go) && go != null)
                {
                    var rend = go.GetComponent<Renderer>();
                    if (rend != null) rend.material.color = Color.gray;
                }
                Log($"💥 <b>{vehicleId} DÉTRUIT</b> ({subsystem} essentiel pulvérisé) : épave, équipage débarqué de force, survivant.");
            }
            else if (hit.Disabled)
            {
                Log($"⚠️ <b>{vehicleId}.{subsystem}</b> hors service — engin intact.");
            }

            RecordChronoSnapshot($"Frappe véhicule : {attacker.Stats.Name} -> {vehicleId}.{subsystem}");
            try { _turnManager?.CheckCombatOver(); } catch { /* jamais bloquant */ }
            return true;
        }

        /// <summary>Purge figurines + état (reset arène / changement de carte).</summary>
        public void PurgeArenaVehicles()
        {
            foreach (var kv in _vehicleFigurines)
            {
                try { if (kv.Value != null) Object.Destroy(kv.Value); } catch { /* ignore */ }
            }
            _vehicleFigurines.Clear();
            _vehicleCoords.Clear();
            VehicleState.ClearAll();
        }
    }
}

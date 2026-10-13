using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics;
using Killtime.Tactics.AI;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Core.Combat;
using Killtime.Core.Dice;
using Killtime.Core.Character;
using Killtime.CameraSystem;
using Killtime.Audio;
using Killtime.UI;

namespace Killtime.Multi
{
    public partial class VTTTableSync : MonoBehaviour
    {
        // =========================================================================
        // ANNUAIRE DES UNITÉS (unitId = GameObject.name)
        // =========================================================================

        public void RebuildRegistry()
        {
            _units.Clear();
            var all = TacticalUnitRegistry.AllUnits;
            for (int i = 0; i < all.Count; i++)
            {
                var u = all[i];
                if (u == null || !u.gameObject.activeInHierarchy) continue;
                string id = UnitIdOf(u);
                if (id.StartsWith("[Disposed]")) continue;
                if (!_units.ContainsKey(id)) _units.Add(id, u);
            }
        }

        public void RegisterUnit(TacticalUnit unit)
        {
            if (unit == null) return;
            _units[UnitIdOf(unit)] = unit;
        }

        public void UnregisterUnit(TacticalUnit unit)
        {
            if (unit == null) return;
            _units.Remove(UnitIdOf(unit));
        }

        public static string UnitIdOf(TacticalUnit unit)
        {
            if (unit == null) return "";
            return unit.gameObject.name;
        }

        public TacticalUnit FindUnit(string unitId)
        {
            if (string.IsNullOrEmpty(unitId)) return null;
            if (_units.TryGetValue(unitId, out var unit) && unit != null) return unit;
            RebuildRegistry();
            _units.TryGetValue(unitId, out unit);
            return unit;
        }

        private void RebuildRegistryIfMissing(string id, TacticalUnit unit)
        {
            if (!_units.ContainsKey(id)) _units[id] = unit;
        }

        // =========================================================================
        // ÉMISSION CÔTÉ GM (AUTORITÉ DU COMBAT)
        // =========================================================================

        private void HandleAnyUnitMoved(TacticalUnit unit, List<HexCoordinates> path, int apCost)
        {
            if (_applyingRemote) return;
            if (_room == null || !_room.InRoom) return;
            if (!_room.IsGM && !unit.IsPlayerControlled) return;

            NotifyLocalMove(unit, path, apCost);
        }

        private void HandleAnyUnitTeleported(TacticalUnit unit, HexCoordinates coords)
        {
            if (_applyingRemote) return;
            if (_room == null || !_room.InRoom) return;
            if (!_room.IsGM && !unit.IsPlayerControlled) return;

            NotifyLocalMove(unit, null, 0);
        }

        public void NotifyLocalMove(TacticalUnit unit, List<HexCoordinates> path = null, int apCost = 0)
        {
            if (_applyingRemote) return;
            if (unit == null || _room == null || !_room.InRoom) return;
            if (!_room.IsGM && !unit.IsPlayerControlled) return;
            RebuildRegistryIfMissing(UnitIdOf(unit), unit);

            int finalQ = unit.CurrentCoords.Q;
            int finalR = unit.CurrentCoords.R;

            List<VTTCoord> vttPath = null;
            if (path != null && path.Count > 0)
            {
                vttPath = new List<VTTCoord>(path.Count);
                for (int i = 0; i < path.Count; i++)
                {
                    vttPath.Add(new VTTCoord(path[i].Q, path[i].R));
                }
                finalQ = path[path.Count - 1].Q;
                finalR = path[path.Count - 1].R;
            }

            var payload = new VTTCombatActionPayload
            {
                action = "move",
                actorId = UnitIdOf(unit),
                destQ = finalQ,
                destR = finalR,
                path = vttPath,
                apCost = apCost,
                // Brouillard §25.4 : le hub filtre ce paquet par destinataire
                // (portée d'ambiance 360° + murs Full depuis au moins un avatar).
                facingYaw = Killtime.Tactics.Visibility.FogOfWarManager.FacingYawDeg(unit),
                vision = unit.Stats != null ? Mathf.Clamp(unit.Stats.Attributes.Vision, 1, 6) : 3,
                pano = (unit.Stats != null && unit.Stats.HasSpecialization(Killtime.Tactics.Visibility.FogOfWarSystem.SpecPanoramic)) ? 1 : 0,
                thermal = (unit.Stats != null && unit.Stats.HasSpecialization(Killtime.Tactics.Visibility.FogOfWarSystem.SpecThermal)) ? 1 : 0,
            };

            _room.SendTableOp(VTTProtocol.OpUnitMove, JsonUtility.ToJson(payload));
        }

        /// <summary>
        /// Revendique les avatars possédés par CE client (brouillard asymétrique).
        /// Le hub ne transmet la position d'un ennemi à ce client que s'il est dans
        /// le champ d'au moins un avatar revendiqué (anti map-hack mémoire).
        /// Le FogOfWarManager local applique le même masque à l'affichage.
        /// </summary>
        public void PublishUnitClaims(IEnumerable<string> unitIds)
        {
            if (_room == null || !_room.InRoom) return;
            var ids = new List<string>();
            if (unitIds != null)
            {
                foreach (var id in unitIds)
                    if (!string.IsNullOrEmpty(id)) ids.Add(id);
            }
            var fog = Killtime.Tactics.Visibility.FogOfWarManager.Instance;
            fog?.ClaimLocalUnits(ids);
            _room.SendTableOp(VTTProtocol.OpUnitClaim, JsonUtility.ToJson(new VTTUnitClaimPayload { unitIds = ids }));
        }
    }
}

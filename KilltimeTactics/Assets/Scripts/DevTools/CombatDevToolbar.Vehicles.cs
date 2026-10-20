using System;
using UnityEngine;
using Killtime.Tactics;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Core.Combat;

namespace Killtime.UI
{
    // RD-195 : section véhicules de la toolbar MJ (spawn, embarquer, écraser,
    // frapper sous-système nommé +100 lourde visible). Appelée depuis
    // DrawCheatsAndToolsTab (1 ligne), dev-only, 0 règle modifiée.
    public partial class CombatDevToolbar : FloatingWindow<CombatDevToolbar>
    {
        private int _vehicleIndex;
        private int _vehicleSubsystemIndex;

        private void DrawVehiclesSection()
        {
            if (_arena == null) return;

            GUILayout.Space(8);
            GUILayout.Label("<b>🚙 Véhicules (RD-195, Livre V §22.3) :</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            HexCoordinates spawnAt = _arena.CurrentTarget != null
                ? _arena.CurrentTarget.CurrentCoords
                : (_arena.PlayerUnit != null ? _arena.PlayerUnit.CurrentCoords : new HexCoordinates(2, 0));
            GUILayout.Label($"<i>Spawn au point ciblé ({spawnAt.Q},{spawnAt.R}) — PV = Max du palier.</i>");

            GUILayout.BeginHorizontal();
            var scales = new[] { VehicleScale.Velo, VehicleScale.Charrette, VehicleScale.JetTank, VehicleScale.Croiseur, VehicleScale.Station };
            var shortNames = new[] { "Vélo", "Charrette", "Jet/Tank", "Croiseur", "Station" };
            for (int i = 0; i < scales.Length; i++)
            {
                if (GUILayout.Button($"➕ {shortNames[i]}", GUILayout.Height(26)))
                    _arena.SpawnArenaVehicle(scales[i], spawnAt);
            }
            GUILayout.EndHorizontal();

            var ids = _arena.ListArenaVehicles();
            if (ids.Count == 0)
            {
                GUILayout.Label("<i>Aucun véhicule en arène.</i>");
                GUILayout.EndVertical();
                return;
            }
            _vehicleIndex = Mathf.Clamp(_vehicleIndex, 0, ids.Count - 1);

            GUILayout.BeginHorizontal();
            for (int i = 0; i < ids.Count; i++)
            {
                var rec = _arena.GetVehicleRecord(ids[i]);
                string tag = rec != null && rec.Destroyed ? "💥" : "🚙";
                if (i == _vehicleIndex) GUI.backgroundColor = new Color(0.2f, 0.6f, 1f);
                if (GUILayout.Button($"{tag} {ids[i]}", GUILayout.Height(24))) _vehicleIndex = i;
                GUI.backgroundColor = Color.white;
            }
            GUILayout.EndHorizontal();

            string selId = ids[_vehicleIndex];
            var sel = _arena.GetVehicleRecord(selId);
            var selCoords = _arena.GetVehicleCoords(selId);
            if (sel != null)
            {
                int crew = sel.Crew != null ? sel.Crew.Count : 0;
                string state = sel.Destroyed ? "<color=red>ÉPAVE</color>" : "<color=green>OPÉRATIONNEL</color>";
                GUILayout.Label($"<b>{selId}</b> {state} ({CombatDevArena.VehicleLabelFor(sel.Scale)}) en ({selCoords?.Q},{selCoords?.R}) — équipage : {crew}");
                GUILayout.Label($"Moteur {VehicleState.GetHp(selId, VehicleSubsystem.Moteur)} | Cockpit {VehicleState.GetHp(selId, VehicleSubsystem.Cockpit)} | Cadre {VehicleState.GetHp(selId, VehicleSubsystem.Cadre)} | Rails {VehicleState.GetHp(selId, VehicleSubsystem.Rails)} | Roues {VehicleState.GetHp(selId, VehicleSubsystem.Roues)}");
            }

            var activeUnit = _turnManager != null ? _turnManager.ActiveUnit : _arena.PlayerUnit;
            string activeName = activeUnit != null && activeUnit.Stats != null ? activeUnit.Stats.Name : "?";

            GUILayout.BeginHorizontal();
            if (GUILayout.Button($"🚙 Embarquer {activeName} (2 PA)", GUILayout.Height(26)))
            {
                if (activeUnit != null) _arena.TryEmbarkArenaVehicle(activeUnit, selId, out _);
            }
            if (GUILayout.Button($"🚶 Débarquer {activeName} (0 PA)", GUILayout.Height(26)))
            {
                if (activeUnit != null) _arena.TryDisembarkArenaVehicle(activeUnit, out _);
            }
            if (GUILayout.Button("🧹 Purger véhicules", GUILayout.Height(26)))
            {
                _arena.PurgeArenaVehicles();
                _vehicleIndex = 0;
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.9f, 0.4f, 0.2f);
            if (GUILayout.Button("🚛 ÉCRASER la cible active (mobile)", GUILayout.Height(30)))
            {
                var foe = _arena.CurrentTarget;
                if (foe != null) _arena.ExecuteVehicleRunOver(selId, foe);
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();

            var subs = (VehicleSubsystem[])Enum.GetValues(typeof(VehicleSubsystem));
            _vehicleSubsystemIndex = Mathf.Clamp(_vehicleSubsystemIndex, 0, subs.Length - 1);
            for (int row = 0; row < subs.Length; row += 3)
            {
                GUILayout.BeginHorizontal();
                for (int c = 0; c < 3 && row + c < subs.Length; c++)
                {
                    int idx = row + c;
                    string nm = subs[idx].ToString() + (VehicleMechanics.IsEssential(subs[idx]) ? "*" : "");
                    if (idx == _vehicleSubsystemIndex) GUI.backgroundColor = new Color(1f, 0.75f, 0.2f);
                    if (GUILayout.Button(nm, GUILayout.Height(24))) _vehicleSubsystemIndex = idx;
                    GUI.backgroundColor = Color.white;
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.Label("<i>* = essentiel (à 0 = épave + débarquement forcé).</i>");

            if (activeUnit != null && activeUnit.Sheet != null)
            {
                var w = activeUnit.Sheet.GetEquippedWeapon();
                int raw = w != null ? w.BaseDamage : 3;
                int bonus = VehicleMechanics.ComputeHeavyBonus(VehicleMechanics.IsHeavyWeapon(w), true);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"Attaquant : <b>{activeName}</b> ({(w != null ? w.Name : "mains nues")}) → {CombatDevArena.FormatStrikePreview(raw, bonus)}");
                if (GUILayout.Button($"🔫 FRAPPER {subs[_vehicleSubsystemIndex]} (2 PA)", GUILayout.Height(28)))
                    _arena.ExecuteVehicleSubsystemStrike(activeUnit, selId, subs[_vehicleSubsystemIndex]);
                GUILayout.EndHorizontal();
            }

            GUILayout.EndVertical();
        }
    }
}

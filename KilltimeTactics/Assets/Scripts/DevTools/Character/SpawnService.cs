using UnityEngine;
using Killtime.Core.Character;
using Killtime.Tactics;
using Killtime.Tactics.Grid;
using Killtime.Tactics.TurnSystem;
using Killtime.Tactics.Units;

namespace Killtime.DevTools.Character
{
    /// <summary>
    /// Insertion d'une fiche sur la grille hexagonale (onglet Spawner).
    /// Extrait à l'identique de CharacterDevWindow.SpawnCharacterOnGrid.
    /// Retourne le message à afficher — la window ne fait que l'affecter.
    /// </summary>
    public static class SpawnService
    {
        public static bool TrySpawn(
            TacticalHexGrid grid,
            CombatDevArena arena,
            TurnManager turnManager,
            CharacterSheet sheet,
            string spawnQStr,
            string spawnRStr,
            bool spawnAsPlayer,
            out string statusMessage)
        {
            if (grid == null)
            {
                statusMessage = "Erreur : TacticalHexGrid introuvable.";
                return false;
            }
            if (sheet == null)
            {
                statusMessage = "Erreur : aucune fiche active à insérer.";
                return false;
            }

            int.TryParse(spawnQStr, out int q);
            int.TryParse(spawnRStr, out int r);

            var coords = new HexCoordinates(q, r);
            var node = grid.GetNode(coords);

            if (node == null)
            {
                statusMessage = $"Coordonnées ({q}, {r}) hors limites de la grille.";
                return false;
            }

            if (!node.IsWalkable)
            {
                statusMessage = $"La case ({q}, {r}) est impraticable (obstacle) !";
                return false;
            }

            bool isOccupiedByUnit = false;
            var existingUnits = TacticalUnitRegistry.AllUnits;
            for (int i = 0; i < existingUnits.Count; i++)
            {
                if (existingUnits[i] != null && existingUnits[i].Stats != null && existingUnits[i].Stats.IsAlive && existingUnits[i].CurrentCoords.Equals(coords))
                {
                    isOccupiedByUnit = true;
                    break;
                }
            }

            if (node.IsOccupied || isOccupiedByUnit)
            {
                node.IsOccupied = true;
                statusMessage = $"La case ({q}, {r}) est déjà occupée par un autre avatar !";
                return false;
            }

            TacticalUnit unit;
            if (arena != null)
            {
                unit = arena.SpawnCustomCharacter(sheet, coords, spawnAsPlayer);
                if (unit == null)
                {
                    statusMessage = $"Échec d'insertion en ({q}, {r}) : case indisponible.";
                    return false;
                }
                // L'arène a pu relocaliser (anti-empilement) : on affiche la case réelle.
                coords = unit.CurrentCoords;
                q = coords.Q;
                r = coords.R;
            }
            else
            {
                var go = new GameObject($"Unit_{sheet.Name.Replace(" ", "_")}");
                unit = go.AddComponent<TacticalUnit>();
                unit.InitializeFromSheet(sheet, coords, grid, spawnAsPlayer);
                unit.GetComponent<TacticalUnitVisual>()?.SetCombatStance(true);
                turnManager?.RegisterUnit(unit);
            }

            string modelLog = string.IsNullOrEmpty(sheet.ModelPrefabName) ? "Avatar Procédural" : $"Modèle '{sheet.ModelPrefabName}'";
            statusMessage = $"'{sheet.Name}' inséré avec succès en ({q}, {r}) avec {modelLog} !";
            return true;
        }
    }
}

using System;
using Killtime.Core.Character;
using Killtime.Tactics.Units;

namespace Killtime.DevTools.Character
{
    /// <summary>
    /// Session de fiche active : détient CurrentSheet et centralise
    /// création / inspection / chargement / sauvegarde+resync unités.
    /// Logique copiée à l'identique de CharacterDevWindow (pas de changement règle).
    /// La window reste propriétaire de l'affichage (_statusMessage) : les méthodes
    /// retournent le message à afficher au lieu d'écrire un champ UI.
    /// </summary>
    public sealed class SheetSessionService
    {
        public CharacterSheet CurrentSheet { get; private set; } = new();

        public void SetCurrent(CharacterSheet sheet)
        {
            if (sheet != null)
                CurrentSheet = sheet;
        }

        public void InspectSheet(CharacterSheet sheet, out string statusMessage)
        {
            if (sheet == null)
            {
                statusMessage = "Aucune fiche à inspecter.";
                return;
            }
            try { CharacterProgressionManager.SynchronizeProgression(sheet); } catch { /* ignore */ }
            CurrentSheet = sheet;
            statusMessage = $"Inspection active : {sheet.Name} (XP Total : {sheet.TotalSpentXP} XP)";
        }

        public void InspectUnit(TacticalUnit unit, out string statusMessage)
        {
            if (unit == null)
            {
                statusMessage = "Aucune unité à inspecter.";
                return;
            }
            CurrentSheet = unit.GetOrBuildSheet();
            string unitName = unit.Stats != null ? unit.Stats.Name : CurrentSheet.Name;
            int hp = 0, maxHp = 0, ap = 0, maxAp = 0;
            if (unit.Stats != null)
            {
                hp = unit.Stats.CurrentHealth;
                maxHp = unit.Stats.MaxHealth;
                ap = unit.Stats.CurrentActionPoints;
                maxAp = unit.Stats.MaxActionPoints;
            }
            statusMessage = $"Inspection active : {unitName} (PV: {hp}/{maxHp} | PA: {ap}/{maxAp})";
        }

        public void CreateNew(out string statusMessage)
        {
            var sheet = new CharacterSheet();
            AttributeRulesService.ApplyDefaultProfileAttributes(sheet);
            sheet.Name = "Nouveau Personnage";
            CurrentSheet = sheet;
            statusMessage = "➕ Nouveau personnage créé — fiche vierge prête à configurer. Pensez à la sauvegarder.";
        }

        public bool LoadFromFile(string fullPath, out string statusMessage)
        {
            var loaded = CharacterStorageService.LoadCharacter(fullPath);
            if (loaded == null)
            {
                statusMessage = "Échec du chargement du fichier.";
                return false;
            }
            CharacterProgressionManager.SynchronizeProgression(loaded);
            CurrentSheet = loaded;
            statusMessage = $"'{CurrentSheet.Name}' chargé ! (XP Total : {CurrentSheet.TotalSpentXP} XP)";
            return true;
        }

        public bool LoadBuiltSheet(Func<CharacterSheet> builder, string loadedLabel, out string statusMessage)
        {
            var built = builder != null ? builder() : null;
            if (built == null)
            {
                statusMessage = "Échec de construction de la fiche.";
                return false;
            }
            CharacterProgressionManager.SynchronizeProgression(built);
            CurrentSheet = built;
            statusMessage = loadedLabel;
            return true;
        }

        /// <summary>
        /// Sauvegarde disque + resync unités partageant le même SheetId.
        /// Reprend SaveCharacterAndSyncUnits à l'identique.
        /// </summary>
        public static void SaveAndSyncUnits(CharacterSheet sheet)
        {
            if (sheet == null) return;
            CharacterStorageService.SaveCharacter(sheet);

            var allUnits = TacticalUnitRegistry.AllUnits;
            for (int i = 0; i < allUnits.Count; i++)
            {
                var u = allUnits[i];
                if (u != null && u.Sheet != null)
                {
                    if (object.ReferenceEquals(u.Sheet, sheet) || u.Sheet.SheetId == sheet.SheetId)
                    {
                        if (!object.ReferenceEquals(u.Sheet, sheet))
                        {
                            u.Sheet.Inventory.Clear();
                            if (sheet.Inventory != null)
                            {
                                for (int j = 0; j < sheet.Inventory.Count; j++)
                                {
                                    var it = sheet.Inventory[j];
                                    if (it != null) u.Sheet.Inventory.Add(it.Clone());
                                }
                            }
                        }

                        if (u.Stats != null)
                        {
                            u.Stats.Attributes = sheet.GetEffectiveAttributes();
                            u.Stats.RecalculateDerivedStats();
                        }
                        u.NotifyInventoryChanged(saveToDisk: false);
                    }
                }
            }
        }
    }
}

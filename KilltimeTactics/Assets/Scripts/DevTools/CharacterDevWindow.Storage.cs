using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Animations;
using Killtime.Core.Character;
using Killtime.Core.Character.Classes;
using Killtime.Core.Arcanotech;
using Killtime.Core.Inventory;
using Killtime.DevTools.Character;
using Killtime.Tactics;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;

namespace Killtime.UI
{
    public partial class CharacterDevWindow
    {
        // ================= TAB 3 : FICHIERS =================
        private void DrawStorageTab()
        {
            GUI.backgroundColor = new Color(0.2f, 0.6f, 1.0f);
            if (GUILayout.Button("➕ Nouveau Personnage (fiche vierge)", GUILayout.Height(30)))
            {
                CreateNewCharacter();
            }
            GUI.backgroundColor = Color.white;
            GUILayout.Space(6);

            GUILayout.Label("<b>Fichiers Sauvegardés sur le Disque :</b>");
            var files = CharacterStorageService.GetSavedCharacterFiles();

            if (files.Count == 0)
            {
                GUILayout.Label("Aucun fichier trouvé.");
            }

            foreach (var f in files)
            {
                string fName = Path.GetFileNameWithoutExtension(f);
                GUILayout.BeginHorizontal(GUI.skin.box);
                GUILayout.Label(fName, GUILayout.Width(280));

                if (GUILayout.Button("Charger", GUILayout.Width(80)))
                {
                    var loaded = CharacterStorageService.LoadCharacter(f);
                    if (loaded != null)
                    {
                        CharacterProgressionManager.SynchronizeProgression(loaded);
                        _currentSheet = loaded;
                        _selectedInventoryItem = null;
                        _showModelDropdown = false;
                        _showAnimDropdown = false;
                        _previewStudio.InvalidateCache();
                        _statusMessage = $"'{_currentSheet.Name}' chargé ! (XP Total : {_currentSheet.TotalSpentXP} XP)";
                    }
                }

                GUI.backgroundColor = Color.red;
                if (GUILayout.Button("Suppr.", GUILayout.Width(60)))
                {
                    CharacterStorageService.DeleteCharacter(f);
                    _statusMessage = "Fichier supprimé.";
                }
                GUI.backgroundColor = Color.white;
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(12);
            GUILayout.Label("<b>Fiches Héroïques Intégrées (tableau à part — voir MinaCharacter / LucasCharacter / ThomasCharacter / JohnCharacter / ErikaCharacter / VanceCharacter) :</b>");

            DrawHeroicSheetRow("Mina", MinaCharacter.DisplayTag, () => MinaCharacter.BuildHeroicSheet());
            DrawHeroicSheetRow("Lucas", LucasCharacter.DisplayTag, () => LucasCharacter.BuildHeroicSheet());
            DrawHeroicSheetRow("Thomas", ThomasCharacter.DisplayTag, () => ThomasCharacter.BuildHeroicSheet());
            DrawHeroicSheetRow("John", JohnCharacter.DisplayTag, () => JohnCharacter.BuildHeroicSheet());
            DrawHeroicSheetRow("Erika", ErikaCharacter.DisplayTag, () => ErikaCharacter.BuildHeroicSheet());
            DrawHeroicSheetRow("Vance", VanceCharacter.DisplayTag, () => VanceCharacter.BuildHeroicSheet());

            GUILayout.Space(12);
            GUILayout.Label("<b>Boss Titans du Livre XI (Gabarit Rosette7 / 7 Hexagones) :</b>");
            DrawTitanBossRow("La Bête d'Hybris", "Boss Primal • 4m (13 PA / Con 6)", () => TitanBossCatalog.BuildTheBeastSheet());
            DrawTitanBossRow("Disciple de Terre", "Missile Lithosphérique • 3m (Armure 6 / 45 Létal)", () => TitanBossCatalog.BuildDiscipleDeTerreSheet());
            DrawTitanBossRow("Le Minulican", "Loup Quantique du Néant (14 PA / Magie 9)", () => TitanBossCatalog.BuildMinulicanSheet());

            GUILayout.Space(12);
            if (GUILayout.Button("📋 Copier le JSON de cette fiche dans le Presse-Papier"))
            {
                GUIUtility.systemCopyBuffer = CharacterStorageService.ExportToJson(_currentSheet);
                _statusMessage = "JSON copié !";
            }
        }
        /// <summary>
        /// Ligne du tableau séparé des fiches héroïques intégrées (même fenêtre que
        /// les fichiers disque, tableau à part). Charge une fiche construite en code.
        /// </summary>
        private void DrawHeroicSheetRow(string heroName, string heroTag, Func<CharacterSheet> builder)
        {
            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.Label($"<b>{heroName}</b> <color=#94A3B8>{heroTag}</color> <color=#10B981>[Intégré]</color>", GUILayout.Width(280));

            if (GUILayout.Button("Charger", GUILayout.Width(80)))
            {
                var heroic = builder != null ? builder() : null;
                if (heroic != null)
                {
                    CharacterProgressionManager.SynchronizeProgression(heroic);
                    _currentSheet = heroic;
                    _selectedInventoryItem = null;
                    _showModelDropdown = false;
                    _showAnimDropdown = false;
                    _previewStudio.InvalidateCache();
                    _statusMessage = $"'{_currentSheet.Name}' (fiche héroïque intégrée) chargé ! (XP Total : {_currentSheet.TotalSpentXP} XP)";
                }
            }

            GUI.backgroundColor = new Color(0.2f, 0.6f, 1.0f);
            if (GUILayout.Button("Sauver → Disque", GUILayout.Width(120)))
            {
                var heroic = builder != null ? builder() : null;
                if (heroic != null)
                {
                    string path = CharacterStorageService.SaveCharacter(heroic);
                    _statusMessage = $"Fiche héroïque '{heroic.Name}' sauvegardée sur le disque !";
                }
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();
        }
        private void DrawTitanBossRow(string bossName, string bossTag, Func<CharacterSheet> builder)
        {
            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.Label($"👑 <b>{bossName}</b> <color=#94A3B8>{bossTag}</color> <color=#FF3B5C>[Rosette 7]</color>", GUILayout.Width(340));

            if (GUILayout.Button("Charger", GUILayout.Width(80)))
            {
                var boss = builder != null ? builder() : null;
                if (boss != null)
                {
                    CharacterProgressionManager.SynchronizeProgression(boss);
                    _currentSheet = boss;
                    _selectedInventoryItem = null;
                    _showModelDropdown = false;
                    _showAnimDropdown = false;
                    _previewStudio.InvalidateCache();
                    _statusMessage = $"Boss Titan '{_currentSheet.Name}' chargé avec gabarit Rosette 7 !";
                }
            }

            GUI.backgroundColor = new Color(0.85f, 0.25f, 0.2f);
            if (GUILayout.Button("Sauver → Disque", GUILayout.Width(120)))
            {
                var boss = builder != null ? builder() : null;
                if (boss != null)
                {
                    CharacterStorageService.SaveCharacter(boss);
                    _statusMessage = $"Boss Titan '{boss.Name}' sauvegardé sur le disque !";
                }
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();
        }
    }
}

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
        private void DrawInventoryHeader()
        {
            var eqWeapon = _currentSheet.GetEquippedWeapon();
            float totalWeight = 0f;
            for (int i = 0; i < _currentSheet.Inventory.Count; i++)
            {
                if (_currentSheet.Inventory[i] != null)
                    totalWeight += _currentSheet.Inventory[i].WeightKg;
            }

            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.Label($"Combattant : <b>{_currentSheet.Name}</b> | Arme : <color=#00E5FF><b>{(eqWeapon != null ? eqWeapon.Name : "Mains Nues")}</b></color> | Charge : <b>{totalWeight:0.#} kg</b> | 💰 <b>{_currentSheet.CreditsCE} CE</b>", GUILayout.ExpandWidth(true));
            GUI.backgroundColor = new Color(0.2f, 0.75f, 0.4f);
            if (GUILayout.Button("💾 Sauvegarder", GUILayout.Width(110)))
            {
                SaveCharacterAndSyncUnits(_currentSheet);
                _statusMessage = $"Fiche et inventaire de '{_currentSheet.Name}' sauvegardés sur disque.";
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.Label("Crédits CE :", GUILayout.Width(90));
            string credStr = GUILayout.TextField(_currentSheet.CreditsCE.ToString(), GUILayout.Width(90));
            if (int.TryParse(credStr, out int creds)) _currentSheet.CreditsCE = Math.Max(0, creds);
            if (GUILayout.Button("+1k", GUILayout.Width(50))) _currentSheet.CreditsCE += 1000;
            if (GUILayout.Button("+10k", GUILayout.Width(55))) _currentSheet.CreditsCE += 10000;
            GUILayout.EndHorizontal();
        }

        private void DrawInventoryBag()
        {
            GUILayout.Space(6);
            GUILayout.Label($"<b>1. Sac & Objets Portés ({_currentSheet.Inventory.Count}) :</b>");

            _invScroll = GUILayout.BeginScrollView(_invScroll, GUILayout.Height(180));
            if (_currentSheet.Inventory.Count == 0)
            {
                GUILayout.Label("<color=gray><i>Aucun objet dans l'inventaire. Ajoutez des armes depuis l'armurerie ci-dessous.</i></color>");
            }

            for (int i = 0; i < _currentSheet.Inventory.Count; i++)
            {
                var item = _currentSheet.Inventory[i];
                if (item == null) continue;

                if (DrawInventoryItemCard(item)) break;
            }
            GUILayout.EndScrollView();
        }

        private bool DrawInventoryItemCard(InventoryItem item)
        {
            bool isSelected = (_selectedInventoryItem == item);
            GUI.backgroundColor = item.IsEquipped ? new Color(0.1f, 0.7f, 0.35f) : (isSelected ? new Color(0.1f, 0.6f, 0.9f) : Color.white);

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();

            string tag = item.IsEquipped ? "⚔️ [ÉQUIPÉ] " : "📦 ";
            if (GUILayout.Button($"{tag}<b>{item.Name}</b>", GUI.skin.label, GUILayout.ExpandWidth(true)))
            {
                _selectedInventoryItem = item;
            }

            if (item.IsEquipped)
            {
                if (GUILayout.Button("Déséquiper", GUILayout.Width(85)))
                {
                    _currentSheet.UnequipItem(item.ItemId);
                    SaveCharacterAndSyncUnits(_currentSheet);
                    _statusMessage = $"'{item.Name}' déséquipé et sauvegardé sur disque.";
                }
            }
            else
            {
                if (GUILayout.Button("Équiper", GUILayout.Width(75)))
                {
                    _currentSheet.EquipItem(item.ItemId);
                    SaveCharacterAndSyncUnits(_currentSheet);
                    _statusMessage = $"'{item.Name}' équipé et sauvegardé sur disque.";
                }
            }

            GUI.backgroundColor = Color.red;
            if (GUILayout.Button("✕", GUILayout.Width(24)))
            {
                _currentSheet.RemoveItem(item.ItemId);
                if (_selectedInventoryItem == item) _selectedInventoryItem = null;
                SaveCharacterAndSyncUnits(_currentSheet);
                _statusMessage = $"'{item.Name}' retiré de l'inventaire et sauvegardé.";
                return true;
            }
            GUI.backgroundColor = Color.white;

            GUILayout.EndHorizontal();

            GUILayout.Label($"<color=#7090A0>Dégâts: <b>{item.BaseDamage}</b> | Portée: <b>{item.RangeInTiles}</b> | Poids: <b>{item.WeightKg:0.#} kg</b> | Compétence: <b>{item.AssociatedSkill}</b></color>");
            GUILayout.EndVertical();

            return false;
        }

        private void DrawArmorySection()
        {
            GUILayout.Space(8);
            GUILayout.Label("<b>2. Armurerie complète (Livre VII) & Marché :</b>");
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Catalogue : <b>{Core.Inventory.ArmoryCatalog.All.Count} articles</b> • Solde : <b>{_currentSheet.CreditsCE} CE</b>", GUILayout.ExpandWidth(true));
            GUI.backgroundColor = new Color(0.95f, 0.75f, 0.15f);
            if (GUILayout.Button("🏪 Ouvrir Armurerie / Marché (I)", GUILayout.Width(220)))
            {
                // L'armurerie s'ouvre LIÉE au perso en cours de création : chaque
                // ajout (+ Sac / Acheter / dotation) va sur _currentSheet, même si
                // aucune unité n'incarne encore cette fiche sur la grille.
                InventoryDevWindow.Open();
                if (InventoryDevWindow.Instance != null)
                {
                    InventoryDevWindow.Instance.InspectSheet(_currentSheet);
                    _statusMessage = $"Armurerie liée à '{_currentSheet.Name}' : les ajouts vont sur cette fiche.";
                }
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();
            GUILayout.Label("<color=gray><i>Tout ce qui peut aller en inventaire : armes, munitions, armures, champs, pharma, outils, survie, arcanotech, quête. Prefabs réels (Guns) ou placeholders 🧱.</i></color>");
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Modèles balistiques détectés : <b>{_availableGunCatalog.Count}</b>");
            if (GUILayout.Button("🔄 Actualiser", GUILayout.Width(110)))
            {
                RefreshGunCatalog();
                _statusMessage = $"{_availableGunCatalog.Count} arme(s) scannée(s).";
            }
            GUILayout.EndHorizontal();

            _gunCatalogScroll = GUILayout.BeginScrollView(_gunCatalogScroll, GUILayout.Height(130));
            if (_availableGunCatalog.Count == 0)
            {
                GUILayout.Label("<color=gray><i>Aucun préfab d'arme détecté sous Assets/Resources/Guns — les placeholders prennent le relais.</i></color>");
            }

            for (int i = 0; i < _availableGunCatalog.Count; i++)
            {
                DrawGunCatalogRow(_availableGunCatalog[i]);
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void DrawGunCatalogRow(string gunPath)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"🔫 <b>{gunPath}</b>", GUILayout.ExpandWidth(true));

            GUI.backgroundColor = new Color(0.2f, 0.8f, 0.4f);
            if (GUILayout.Button("+ Ajouter au Sac", GUILayout.Width(130)))
            {
                string cleanName = Path.GetFileName(gunPath);
                var newItem = new Core.Inventory.InventoryItem
                {
                    Name = cleanName,
                    PrefabPath = gunPath,
                    Type = Core.Inventory.ItemType.Weapon,
                    EquipSlot = Core.Inventory.ItemEquipSlot.MainHand,
                    IsEquipped = (_currentSheet.GetEquippedWeapon() == null),
                    BaseDamage = 7,
                    RangeInTiles = 10,
                    WeightKg = 2.4f,
                    AssociatedSkill = SkillType.Ballistique,
                    Description = $"Arme balistique importée depuis Resources/Guns/{gunPath}.",
                    PriceCE = 10000,
                    Category = "Fusils Laser",
                    PlaceholderKind = "RifleLaser"
                };

                _currentSheet.AddItem(newItem);
                _selectedInventoryItem = newItem;
                SaveCharacterAndSyncUnits(_currentSheet);
                _statusMessage = $"Arme '{cleanName}' ajoutée et sauvegardée sur disque !";
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();
        }

        private void DrawItemForgeSection()
        {
            GUILayout.Space(8);
            GUILayout.Label("<b>3. Forger un Objet / Arme Personnalisée :</b>");
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Nom :", GUILayout.Width(80));
            _customItemName = GUILayout.TextField(_customItemName);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Dégâts : {_customItemDamage}", GUILayout.Width(110));
            _customItemDamage = (int)GUILayout.HorizontalSlider(_customItemDamage, 1, 25);
            GUILayout.Label($"Portée : {_customItemRange}", GUILayout.Width(100));
            _customItemRange = (int)GUILayout.HorizontalSlider(_customItemRange, 1, 20);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Poids (kg) : {_customItemWeight:0.#}", GUILayout.Width(110));
            _customItemWeight = GUILayout.HorizontalSlider(_customItemWeight, 0.1f, 15f);
            GUILayout.EndHorizontal();

            GUI.backgroundColor = new Color(0.2f, 0.6f, 0.9f);
            if (GUILayout.Button($"➕ Créer et Ajouter '{_customItemName}'", GUILayout.Height(28)))
            {
                var customItem = new Core.Inventory.InventoryItem
                {
                    Name = _customItemName,
                    PrefabPath = "",
                    Type = Core.Inventory.ItemType.Weapon,
                    EquipSlot = Core.Inventory.ItemEquipSlot.MainHand,
                    IsEquipped = (_currentSheet.GetEquippedWeapon() == null),
                    BaseDamage = _customItemDamage,
                    RangeInTiles = _customItemRange,
                    WeightKg = _customItemWeight,
                    AssociatedSkill = SkillType.Ballistique,
                    Description = "Objet personnalisé créé dans l'Atelier."
                };

                _currentSheet.AddItem(customItem);
                _selectedInventoryItem = customItem;
                SaveCharacterAndSyncUnits(_currentSheet);
                _statusMessage = $"Objet '{_customItemName}' créé et sauvegardé sur disque.";
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndVertical();
        }

        private void DrawSelectedItemEditor()
        {
            if (_selectedInventoryItem != null)
            {
                GUILayout.Space(8);
                GUILayout.Label($"<b>4. Édition des Propriétés : <color=#00E5FF>{_selectedInventoryItem.Name}</color></b>");
                GUILayout.BeginVertical(GUI.skin.box);

                GUILayout.BeginHorizontal();
                GUILayout.Label("Nom :", GUILayout.Width(90));
                _selectedInventoryItem.Name = GUILayout.TextField(_selectedInventoryItem.Name);
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("Dégâts Bruts :", GUILayout.Width(90));
                _selectedInventoryItem.BaseDamage = (int)GUILayout.HorizontalSlider(_selectedInventoryItem.BaseDamage, 1, 30);
                GUILayout.Label($"{_selectedInventoryItem.BaseDamage}", GUILayout.Width(30));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("Portée (Cases) :", GUILayout.Width(90));
                _selectedInventoryItem.RangeInTiles = (int)GUILayout.HorizontalSlider(_selectedInventoryItem.RangeInTiles, 1, 25);
                GUILayout.Label($"{_selectedInventoryItem.RangeInTiles}", GUILayout.Width(30));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("Poids (Kg) :", GUILayout.Width(90));
                _selectedInventoryItem.WeightKg = GUILayout.HorizontalSlider(_selectedInventoryItem.WeightKg, 0.1f, 20.0f);
                GUILayout.Label($"{_selectedInventoryItem.WeightKg:0.#}", GUILayout.Width(30));
                GUILayout.EndHorizontal();

                GUILayout.Label("Description :");
                _selectedInventoryItem.Description = GUILayout.TextArea(_selectedInventoryItem.Description, GUILayout.Height(40));

                GUILayout.Space(4);
                GUI.backgroundColor = new Color(0.2f, 0.75f, 0.4f);
                if (GUILayout.Button("💾 Valider & Sauvegarder sur Disque", GUILayout.Height(26)))
                {
                    SaveCharacterAndSyncUnits(_currentSheet);
                    _statusMessage = $"Propriétés de '{_selectedInventoryItem.Name}' enregistrées sur disque.";
                }
                GUI.backgroundColor = Color.white;

                GUILayout.EndVertical();
            }
        }
    }
}

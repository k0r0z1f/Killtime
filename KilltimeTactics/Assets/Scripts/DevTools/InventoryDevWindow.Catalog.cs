// InventoryDevWindow.Catalog.cs — split move-only de InventoryDevWindow.cs (sac, armurerie, marché, filtres).
using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Core.Character;
using Killtime.Core.Inventory;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;

namespace Killtime.UI
{
    public partial class InventoryDevWindow
    {

        /// <summary>Conservé pour compatibilité (CharacterDevWindow / Toolbar).</summary>
        public void RefreshGunCatalog()
        {
            _availableGunPrefabs.Clear();

#if UNITY_EDITOR
            string[] guids = UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Resources/Guns" });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[i]);
                if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                {
                    int resIndex = path.IndexOf("/Resources/Guns/", StringComparison.OrdinalIgnoreCase);
                    if (resIndex >= 0)
                    {
                        string relative = path.Substring(resIndex + "/Resources/Guns/".Length);
                        string ext = Path.GetExtension(relative);
                        if (!string.IsNullOrEmpty(ext)) relative = relative.Substring(0, relative.Length - ext.Length);
                        if (!_availableGunPrefabs.Contains(relative)) _availableGunPrefabs.Add(relative);
                    }
                    else
                    {
                        string fName = Path.GetFileNameWithoutExtension(path);
                        if (!_availableGunPrefabs.Contains(fName)) _availableGunPrefabs.Add(fName);
                    }
                }
            }
#endif
            if (_availableGunPrefabs.Count == 0)
            {
                var prefabs = Resources.LoadAll<GameObject>("Guns");
                for (int i = 0; i < prefabs.Length; i++)
                    if (prefabs[i] != null && !_availableGunPrefabs.Contains(prefabs[i].name))
                        _availableGunPrefabs.Add(prefabs[i].name);
            }
        }

        private InventoryItem GetCurrentPreviewItem()
        {
            // Priorité : définition catalogue/marché sélectionnée, sinon objet inspecté, sinon arme équipée.
            if ((_selectedTab == 1 || _selectedTab == 2) && _previewCatalogDef != null)
                return _previewCatalogDef;
            if (_inspectedItem != null) return _inspectedItem;
            var activeSheet = GetActiveSheet();
            if (activeSheet != null)
            {
                var eq = activeSheet.GetEquippedWeapon();
                if (eq != null) return eq;
                if (activeSheet.Inventory != null && activeSheet.Inventory.Count > 0) return activeSheet.Inventory[0];
            }
            return null;
        }

        private void DrawLeftTab()
        {
            switch (_selectedTab)
            {
                case 0: DrawInventoryListTab(); break;
                case 1: DrawArmoryCatalogTab(); break;
                case 2: DrawMarketTab(); break;
                case 3: DrawItemPropertiesTab(); break;
            }
        }

        private void DrawUnitSelectorHeader()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            // Mode lié au créateur : la fiche en création est la cible unique.
            if (_draftSheet != null)
            {
                var draft = GetActiveSheet();
                GUILayout.BeginHorizontal();
                GUILayout.Label("Combattant :", GUILayout.Width(85));
                GUI.color = Color.cyan;
                GUILayout.Label($"<b>{(draft != null ? draft.Name : "?")} (Création)</b>", GUILayout.Width(200));
                GUI.color = Color.white;
                if (draft != null)
                {
                    GUI.color = Color.yellow;
                    GUILayout.Label($"💰 {draft.CreditsCE} CE", GUILayout.Width(110));
                    GUI.color = Color.white;
                    if (GUILayout.Button("+1k", GUILayout.Width(44)))
                    {
                        draft.EarnCredits(1000);
                        SaveAndSync(draft);
                        _statusMessage = $"+1000 CE (dev). Solde : {draft.CreditsCE} CE.";
                    }
                }
                GUILayout.FlexibleSpace();
                GUI.backgroundColor = new Color(0.5f, 0.5f, 0.55f);
                if (GUILayout.Button("Délier (unités)", GUILayout.Width(110)))
                {
                    ClearDraftBinding();
                }
                GUI.backgroundColor = Color.white;
                if (GUILayout.Button("🔄", GUILayout.Width(36)))
                {
                    RefreshGunCatalog();
                }
                GUILayout.EndHorizontal();
                GUILayout.Label("<color=gray><i>Lié au Créateur (F1) : chaque ajout va sur le perso en cours de création.</i></color>");
            }
            else
            {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Combattant :", GUILayout.Width(85));

            if (_sceneUnits.Count == 0) RefreshSceneUnits();

            if (_sceneUnits.Count > 0)
            {
                if (GUILayout.Button("◀", GUILayout.Width(26)))
                {
                    _selectedUnitIndex = (_selectedUnitIndex - 1 + _sceneUnits.Count) % _sceneUnits.Count;
                    _selectedUnit = _sceneUnits[_selectedUnitIndex];
                    _inspectedItem = null;
                }

                string uName = _selectedUnit != null ? _selectedUnit.Stats.Name : "Aucun";
                GUI.color = (_selectedUnit != null && _selectedUnit.IsPlayerControlled) ? Color.cyan : new Color(1f, 0.45f, 0.45f);
                GUILayout.Label($"<b>{uName}</b>", GUILayout.Width(150));
                GUI.color = Color.white;

                if (GUILayout.Button("▶", GUILayout.Width(26)))
                {
                    _selectedUnitIndex = (_selectedUnitIndex + 1) % _sceneUnits.Count;
                    _selectedUnit = _sceneUnits[_selectedUnitIndex];
                    _inspectedItem = null;
                }

                var sheet = _selectedUnit != null ? _selectedUnit.GetOrBuildSheet() : null;
                if (sheet != null)
                {
                    GUI.color = Color.yellow;
                    GUILayout.Label($"💰 {sheet.CreditsCE} CE", GUILayout.Width(110));
                    GUI.color = Color.white;
                    if (GUILayout.Button("+1k", GUILayout.Width(44)))
                    {
                        sheet.EarnCredits(1000);
                        SaveAndSync(sheet);
                        _statusMessage = $"+1000 CE (dev). Solde : {sheet.CreditsCE} CE.";
                    }
                }
            }
            else
            {
                GUILayout.Label("<i>Aucune unité tactique en scène.</i>");
            }

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("🔄", GUILayout.Width(36)))
            {
                RefreshSceneUnits();
                RefreshGunCatalog();
            }
            GUILayout.EndHorizontal();
            } // fin else (mode unités en scène)
            GUILayout.EndVertical();
        }

        private void SaveAndSync(CharacterSheet sheet)
        {
            if (sheet == null) return;
            CharacterStorageService.SaveCharacter(sheet);
            // Notifie l'unité sélectionnée + toute unité incarnant cette fiche
            // (cas du brouillon déjà spawné sur la grille : le visuel d'arme suit).
            if (_selectedUnit != null) _selectedUnit.NotifyInventoryChanged(saveToDisk: false);
            try
            {
                var allUnits = TacticalUnitRegistry.AllUnits;
                for (int i = 0; i < allUnits.Count; i++)
                {
                    var u = allUnits[i];
                    if (u == null || u == _selectedUnit || u.Sheet == null) continue;
                    if (u.Sheet.SheetId == sheet.SheetId)
                        u.NotifyInventoryChanged(saveToDisk: false);
                }
            }
            catch { /* sync visuel best-effort */ }
        }

        // ================= ONGLET 0 : SAC =================
        private void DrawInventoryListTab()
        {
            var sheet = GetActiveSheet();
            if (sheet == null)
            {
                GUILayout.Label("<i>Sélectionnez une unité pour inspecter son sac.</i>");
                return;
            }
            var inv = sheet.Inventory;

            GUILayout.Label($"<b>Objets en poche ({inv.Count}) — {sheet.GetTotalWeightKg():0.#} kg :</b>");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("⚔️ Tout déséquiper", GUILayout.Height(22)))
            {
                for (int i = 0; i < inv.Count; i++) if (inv[i] != null) inv[i].IsEquipped = false;
                SaveAndSync(sheet);
            }
            GUI.backgroundColor = new Color(0.7f, 0.15f, 0.15f);
            if (GUILayout.Button("🗑️ Vider (dev)", GUILayout.Height(22)))
            {
                inv.Clear();
                _inspectedItem = null;
                SaveAndSync(sheet);
                _statusMessage = "Inventaire vidé.";
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();

            _invScroll = GUILayout.BeginScrollView(_invScroll, GUILayout.Height(320));

            if (inv.Count == 0)
                GUILayout.Label("<color=gray><i>Inventaire vide.\nPiochez dans l'Armurerie ou achetez au Marché.</i></color>");

            for (int i = 0; i < inv.Count; i++)
            {
                var item = inv[i];
                if (item == null) continue;

                bool isSelected = (_inspectedItem == item);
                GUI.backgroundColor = item.IsEquipped ? new Color(0.1f, 0.7f, 0.35f) : (isSelected ? new Color(0.1f, 0.6f, 0.9f) : Color.white);

                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.BeginHorizontal();

                string icon = IconFor(item);
                string prefix = item.IsEquipped ? "⚔️ [ÉQUIPÉ] " : $"{icon} ";
                if (GUILayout.Button($"{prefix}<b>{item.Name}</b>{(item.IsStackable && item.Quantity > 1 ? $" x{item.Quantity}" : "")}", GUI.skin.label, GUILayout.ExpandWidth(true)))
                {
                    _inspectedItem = item;
                    _previewCatalogDef = null;
                }

                if (item.Type == ItemType.Weapon || item.Type == ItemType.Armor || item.Type == ItemType.Arcanotech)
                {
                    if (item.IsEquipped)
                    {
                        if (GUILayout.Button("Déséq.", GUILayout.Width(52)))
                        {
                            sheet.UnequipItem(item.ItemId);
                            SaveAndSync(sheet);
                            _statusMessage = $"'{item.Name}' déséquipé.";
                        }
                    }
                    else if (GUILayout.Button("Équiper", GUILayout.Width(52)))
                    {
                        sheet.EquipItem(item.ItemId);
                        SaveAndSync(sheet);
                        _statusMessage = $"'{item.Name}' équipé !";
                    }
                }
                else if (GUILayout.Button("Voir", GUILayout.Width(52)))
                {
                    _inspectedItem = item;
                }

                GUI.backgroundColor = Color.red;
                if (GUILayout.Button("✕", GUILayout.Width(24)))
                {
                    sheet.RemoveItem(item.ItemId);
                    if (_inspectedItem == item) _inspectedItem = null;
                    SaveAndSync(sheet);
                    _statusMessage = $"'{item.Name}' retiré.";
                    break;
                }
                GUI.backgroundColor = Color.white;

                GUILayout.EndHorizontal();
                GUILayout.Label($"<color=#7090A0>{item.Category} • D:{item.BaseDamage} • P:{item.RangeInTiles} • {item.WeightKg:0.#}kg • {item.PriceCE} CE</color>", RichLabel());
                GUILayout.EndVertical();
            }

            GUILayout.EndScrollView();
        }

        // ================= ONGLET 1 : ARMURERIE (dotation gratuite dev / MJ) =================
        private void DrawArmoryCatalogTab()
        {
            DrawCatalogFilters(showAffordableToggle: false);
            var list = GetFilteredCatalog();

            GUILayout.Label($"<b>Catalogue Livre VIII : {list.Count} article(s)</b> <color=gray>(dotation directe, sans paiement)</color>");
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<color=gray>Source : {ArmoryCatalog.LoadedFrom} — éditable via 🛠️ Armurerie (F12)</color>", RichLabel());
            if (GUILayout.Button("Recharger JSON", GUILayout.Width(110)))
            {
                ArmoryCatalog.InvalidateCache();
                _statusMessage = "Catalogue rechargé : " + ArmoryCatalog.LoadedFrom;
            }
            if (GUILayout.Button("🛠️ Éditeur (F12)", GUILayout.Width(110)))
            {
                ArmoryDevWindow.Open();
            }
            GUILayout.EndHorizontal();
            _catalogScroll = GUILayout.BeginScrollView(_catalogScroll, GUILayout.Height(320));

            string lastEra = "";
            for (int i = 0; i < list.Count; i++)
            {
                var def = list[i];
                string itemEra = ArmoryCatalog.DeduceEra(def);
                if (_eraFilter < 0 && itemEra != lastEra)
                {
                    lastEra = itemEra;
                    GUI.color = new Color(0.0f, 0.9f, 1.0f);
                    GUILayout.Label($"<b>━━━ ⏳ {itemEra.ToUpperInvariant()} ━━━</b>", GUI.skin.box);
                    GUI.color = Color.white;
                }

                bool isSelected = (_previewCatalogDef == def);
                GUI.backgroundColor = isSelected ? new Color(0.0f, 0.85f, 1.0f) : Color.white;
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button($"{IconFor(def)} <b>{def.Name}</b>", GUI.skin.label, GUILayout.ExpandWidth(true)))
                {
                    _selectedCatalogIndex = i;
                    _previewCatalogDef = def;
                    _inspectedItem = null;
                }
                GUI.backgroundColor = new Color(0.2f, 0.8f, 0.4f);
                if (GUILayout.Button("+ Sac", GUILayout.Width(52)))
                {
                    GiveCatalogItemToUnit(def);
                }
                GUI.backgroundColor = Color.white;
                GUILayout.EndHorizontal();
                GUILayout.Label($"<color=#7090A0><b>[{itemEra}]</b> {def.Category} • {def.DlphCode} • {def.PriceCE} CE{(string.IsNullOrEmpty(def.PrefabPath) ? " • 🧱 placeholder" : " • 🔫 prefab")}</color>", RichLabel());
                GUILayout.EndVertical();
            }

            // Prefabs bruts détectés (custom, hors catalogue)
            if (_availableGunPrefabs.Count > 0)
            {
                GUILayout.Space(4);
                GUILayout.Label($"<b>Prefabs 3D détectés ({_availableGunPrefabs.Count}) :</b>");
                for (int i = 0; i < _availableGunPrefabs.Count; i++)
                {
                    string gunPath = _availableGunPrefabs[i];
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"🔫 <b>{gunPath}</b>", GUILayout.ExpandWidth(true));
                    GUI.backgroundColor = new Color(0.2f, 0.8f, 0.4f);
                    if (GUILayout.Button("+ Sac", GUILayout.Width(52))) AddRawGunToUnit(gunPath);
                    GUI.backgroundColor = Color.white;
                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.EndScrollView();
        }

        // ================= ONGLET 2 : MARCHÉ (achat / revente CE) =================
        private void DrawMarketTab()
        {
            var sheet = GetActiveSheet();
            if (sheet == null)
            {
                GUILayout.Label("<i>Sélectionnez une unité (son portefeuille CE sera débité/crédité).</i>");
                return;
            }
            GUI.color = Color.yellow;
            GUILayout.Label($"<b>💰 Solde : {sheet.CreditsCE} CE</b>  <color=gray>(revente = 50% catalogue)</color>", RichLabel());
            GUI.color = Color.white;

            DrawCatalogFilters(showAffordableToggle: true);
            var list = GetFilteredCatalog();

            _marketScroll = GUILayout.BeginScrollView(_marketScroll, GUILayout.Height(220));
            string lastMarketEra = "";
            for (int i = 0; i < list.Count; i++)
            {
                var def = list[i];
                string itemEra = ArmoryCatalog.DeduceEra(def);
                if (_eraFilter < 0 && itemEra != lastMarketEra)
                {
                    lastMarketEra = itemEra;
                    GUI.color = new Color(1.0f, 0.75f, 0.15f);
                    GUILayout.Label($"<b>━━━ ⏳ {itemEra.ToUpperInvariant()} ━━━</b>", GUI.skin.box);
                    GUI.color = Color.white;
                }

                bool afford = sheet.CanAfford(def.PriceCE);
                bool isSelected = (_previewCatalogDef == def);
                GUI.backgroundColor = isSelected ? new Color(0.0f, 0.85f, 1.0f) : (afford ? Color.white : new Color(0.6f, 0.6f, 0.6f));
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button($"{IconFor(def)} <b>{def.Name}</b>  <color=#7090A0>[{itemEra} • {def.Category}]</color>\n{def.PriceCE} CE", GUI.skin.label, GUILayout.ExpandWidth(true)))
                {
                    _selectedMarketIndex = i;
                    _previewCatalogDef = def;
                    _inspectedItem = null;
                }
                GUI.enabled = afford;
                GUI.backgroundColor = afford ? new Color(0.95f, 0.75f, 0.15f) : Color.gray;
                if (GUILayout.Button("Acheter", GUILayout.Width(62)))
                {
                    if (ArmoryCatalog.BuyForSheet(sheet, def.Name, out string msg))
                    {
                        SaveAndSync(sheet);
                        _inspectedItem = sheet.Inventory.Count > 0 ? sheet.Inventory[sheet.Inventory.Count - 1] : null;
                        _previewCatalogDef = null;
                    }
                    _statusMessage = msg;
                }
                GUI.enabled = true;
                GUI.backgroundColor = Color.white;
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }
            GUILayout.EndScrollView();

            GUILayout.Space(4);
            GUILayout.Label("<b>Revente du sac (50%) :</b>");
            GUILayout.BeginHorizontal();
            if (_inspectedItem != null)
            {
                int sell = ArmoryCatalog.GetSellPrice(_inspectedItem);
                GUI.backgroundColor = new Color(0.3f, 0.7f, 1.0f);
                if (GUILayout.Button($"💰 Revendre '{_inspectedItem.Name}' (+{sell} CE)", GUILayout.Height(26)))
                {
                    ArmoryCatalog.SellFromSheet(sheet, _inspectedItem.ItemId, out string msg);
                    _inspectedItem = null;
                    SaveAndSync(sheet);
                    _statusMessage = msg;
                }
                GUI.backgroundColor = Color.white;
            }
            else
            {
                GUILayout.Label("<color=gray><i>Sélectionnez un objet du Sac (onglet 🎒) puis revenez ici pour le revendre.</i></color>");
            }
            GUILayout.EndHorizontal();
        }

        private void DrawCatalogFilters(bool showAffordableToggle)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("🔍", GUILayout.Width(22));
            _searchFilter = GUILayout.TextField(_searchFilter ?? "", GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();

            // 1. Filtrage par Ère Technologique (Livre VIII §30-32)
            GUILayout.Label("<b>Ère Technologique :</b>");
            string[] eras = ArmoryCatalog.Eras;
            string[] eraOptions = new string[eras.Length + 1];
            eraOptions[0] = "Toutes les ères";
            Array.Copy(eras, 0, eraOptions, 1, eras.Length);
            int selEra = _eraFilter + 1;
            int nextEra = GUILayout.SelectionGrid(selEra, eraOptions, 4);
            if (nextEra != selEra)
            {
                _eraFilter = nextEra - 1;
                _previewCatalogDef = null;
            }

            // 2. Filtrage par Catégorie d'équipement
            GUILayout.Label("<b>Catégorie :</b>");
            string[] cats = ArmoryCatalog.Categories;
            string[] options = new string[cats.Length + 1];
            options[0] = "Toutes";
            Array.Copy(cats, 0, options, 1, cats.Length);
            int sel = _categoryFilter + 1;
            int next = GUILayout.SelectionGrid(sel, options, 3);
            if (next != sel)
            {
                _categoryFilter = next - 1;
                _previewCatalogDef = null;
            }

            GUILayout.BeginHorizontal();
            if (showAffordableToggle)
                _affordableOnly = GUILayout.Toggle(_affordableOnly, "Abordables seule.", GUILayout.Width(130));
            _showOnlyRealPrefabs = GUILayout.Toggle(_showOnlyRealPrefabs, "Prefabs réels unique.", GUILayout.Width(150));
            if (GUILayout.Button("✕", GUILayout.Width(26)))
            {
                _searchFilter = "";
                _eraFilter = -1;
                _categoryFilter = -1;
                _affordableOnly = false;
                _showOnlyRealPrefabs = false;
            }
            GUILayout.EndHorizontal();
        }

        private List<InventoryItem> GetFilteredCatalog()
        {
            var result = new List<InventoryItem>();
            var sheet = GetActiveSheet();
            for (int i = 0; i < ArmoryCatalog.All.Count; i++)
            {
                var def = ArmoryCatalog.All[i];
                if (def == null) continue;
                string era = ArmoryCatalog.DeduceEra(def);
                if (_eraFilter >= 0 && era != ArmoryCatalog.Eras[_eraFilter]) continue;
                if (_categoryFilter >= 0 && def.Category != ArmoryCatalog.Categories[_categoryFilter]) continue;
                if (_showOnlyRealPrefabs && string.IsNullOrEmpty(def.PrefabPath)) continue;
                if (_affordableOnly && sheet != null && !sheet.CanAfford(def.PriceCE)) continue;
                if (!string.IsNullOrEmpty(_searchFilter))
                {
                    string q = _searchFilter.ToLowerInvariant();
                    if (!def.Name.ToLowerInvariant().Contains(q) && !(def.Category ?? "").ToLowerInvariant().Contains(q)
                        && !(def.Description ?? "").ToLowerInvariant().Contains(q)
                        && !era.ToLowerInvariant().Contains(q)) continue;
                }
                result.Add(def);
            }

            // Tri par Ère technologique chronologique, puis Catégorie, puis Nom
            result.Sort((a, b) =>
            {
                int eraA = Array.IndexOf(ArmoryCatalog.Eras, ArmoryCatalog.DeduceEra(a));
                int eraB = Array.IndexOf(ArmoryCatalog.Eras, ArmoryCatalog.DeduceEra(b));
                if (eraA != eraB) return eraA.CompareTo(eraB);
                int catCmp = string.Compare(a.Category, b.Category, StringComparison.Ordinal);
                if (catCmp != 0) return catCmp;
                return string.Compare(a.Name, b.Name, StringComparison.Ordinal);
            });

            return result;
        }

        private void GiveCatalogItemToUnit(InventoryItem def)
        {
            var sheet = GetActiveSheet();
            if (sheet == null) { _statusMessage = "Sélectionnez d'abord une unité."; return; }
            var copy = def.Clone();
            if (copy.Type == ItemType.Weapon) copy.IsEquipped = (sheet.GetEquippedWeapon() == null);
            sheet.AddItem(copy);
            _inspectedItem = copy;
            _previewCatalogDef = null;
            SaveAndSync(sheet);
            _statusMessage = $"'{copy.Name}' doté à {sheet.Name} (sans paiement).";
        }

        private void AddRawGunToUnit(string gunPrefabPath)
        {
            var sheet = GetActiveSheet();
            if (sheet == null) { _statusMessage = "Sélectionnez d'abord une unité."; return; }
            string cleanName = Path.GetFileName(gunPrefabPath);
            var newItem = new InventoryItem
            {
                Name = cleanName,
                PrefabPath = gunPrefabPath,
                Type = ItemType.Weapon,
                EquipSlot = ItemEquipSlot.MainHand,
                IsEquipped = (sheet.GetEquippedWeapon() == null),
                BaseDamage = 7,
                RangeInTiles = 10,
                WeightKg = 2.4f,
                AssociatedSkill = SkillType.Ballistique,
                Description = $"Arme balistique importée depuis Resources/Guns/{gunPrefabPath}.",
                PriceCE = 10000,
                Category = "Fusils Laser",
                PlaceholderKind = "RifleLaser"
            };
            sheet.AddItem(newItem);
            _inspectedItem = newItem;
            SaveAndSync(sheet);
            _statusMessage = $"Arme '{cleanName}' ajoutée au sac de {sheet.Name} !";
        }
    }
}

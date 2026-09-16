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
    /// <summary>
    /// Inventaire PJ + Armurerie complète (Livre VIII) + Marché (achat/revente CE).
    /// Accessible via : bouton 🎒 Sac (HUD/Dev Arena), touche I/F5, onglet Inventaire du Créateur (F1).
    /// Catalogue = ArmoryCatalog (60+ items, tout ce qui peut aller en inventaire) ;
    /// modèles 3D = prefabs Resources/Guns si présents, sinon placeholders procéduraux.
    /// </summary>
    public class InventoryDevWindow : FloatingWindow<InventoryDevWindow>
    {
        protected override int WindowId => 894;
        protected override string Title => "Inventaire // Armurerie & Marché";
        protected override Vector2 MinSize => _minSize;
        protected override Rect DefaultRect => new Rect(
            Mathf.Max(10f, Screen.width - _defaultWidth - 20f), 96f,
            Mathf.Min(_defaultWidth, Mathf.Max(_minSize.x, Screen.width - 40f)),
            Mathf.Min(740f, Mathf.Max(_minSize.y, Screen.height - 115f)));
        protected override KeyCode[] ToggleKeys => _toggleKeys;

        private static readonly KeyCode[] _toggleKeys = { KeyCode.I, KeyCode.F5 };
        private static readonly Vector2 _minSize = new Vector2(640, 420);
        private const float _defaultWidth = 920f;

        [Header("Systèmes")]
        [SerializeField] private TurnManager _turnManager;

        private TacticalUnit _selectedUnit;
        private readonly List<TacticalUnit> _sceneUnits = new();
        private int _selectedUnitIndex = 0;

        private readonly List<string> _availableGunPrefabs = new();
        private int _selectedCatalogIndex = 0;
        private int _selectedMarketIndex = 0;

        private InventoryItem _inspectedItem;
        private InventoryItem _previewCatalogDef;
        private int _selectedTab = 0;
        private readonly string[] _tabs = { "🎒 Sac", "📦 Armurerie", "🏪 Marché", "🛠️ Propriétés" };

        // Filtres armurerie / marché
        private string _searchFilter = "";
        private int _categoryFilter = -1; // -1 = toutes
        private bool _affordableOnly = false;
        private bool _showOnlyRealPrefabs = false;

        private Vector2 _invScroll;
        private Vector2 _catalogScroll;
        private Vector2 _marketScroll;
        private Vector2 _mainScroll;
        private string _statusMessage = "Prêt. Catalogue Livre VIII chargé.";

        // Studio de rendu 3D
        private RenderTexture _previewRT;
        private Camera _previewCam;
        private GameObject _previewStudioRoot;
        private GameObject _currentPreviewInstance;
        private string _lastLoadedModelKey = "__NONE__";
        private float _previewYaw = 135f;
        private float _previewPitch = 20f;
        private bool _isDraggingPreview = false;
        private Vector2 _lastMousePos;

        protected override void OnOpened()
        {
            EnsureReferences();
            RefreshSceneUnits();
            RefreshGunCatalog();
            EnsurePreviewStudio();
            try { LoadDevPrefs(); } catch { /* ignore */ }
        }

        protected override void Awake()
        {
            base.Awake();
            EnsureReferences();
            RefreshGunCatalog();
            EnsurePreviewStudio();
            try { LoadDevPrefs(); } catch { /* ignore */ }
        }

        protected override void OnClosed()
        {
            try { CaptureDevPrefs(); DevUIPreferences.SaveNow(); } catch { /* ignore */ }
        }

        private void LoadDevPrefs()
        {
            var p = DevUIPreferences.Current;
            if (p == null) return;
            _selectedTab = Mathf.Clamp(p.InventoryTab, 0, _tabs.Length - 1);
            _searchFilter = p.InventorySearch ?? "";
            _categoryFilter = p.InventoryCategory;
            _affordableOnly = p.InventoryAffordableOnly;
            _showOnlyRealPrefabs = p.InventoryRealPrefabsOnly;
        }

        private void CaptureDevPrefs()
        {
            var p = DevUIPreferences.Current;
            if (p == null) return;
            p.InventoryTab = _selectedTab;
            p.InventorySearch = _searchFilter ?? "";
            p.InventoryCategory = _categoryFilter;
            p.InventoryAffordableOnly = _affordableOnly;
            p.InventoryRealPrefabsOnly = _showOnlyRealPrefabs;
            DevUIPreferences.MarkDirty();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            try { CaptureDevPrefs(); } catch { /* ignore */ }
            CleanupPreviewStudio();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            CleanupPreviewStudio();
        }

        private void EnsureReferences()
        {
            if (_turnManager == null) _turnManager = FindAnyObjectByType<TurnManager>();
        }

        public void InspectUnit(TacticalUnit unit)
        {
            if (unit == null) return;
            EnsureReferences();
            RefreshSceneUnits();
            int idx = _sceneUnits.IndexOf(unit);
            if (idx >= 0)
            {
                _selectedUnitIndex = idx;
                _selectedUnit = unit;
            }
            OpenInstance();
        }

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

        protected override void Update()
        {
            base.Update();
            if (_isOpen)
            {
                var previewItem = GetCurrentPreviewItem();
                UpdatePreviewModel(previewItem);
                RenderPreviewStudio();
            }
        }

        private InventoryItem GetCurrentPreviewItem()
        {
            // Priorité : définition catalogue/marché sélectionnée, sinon objet inspecté, sinon arme équipée.
            if ((_selectedTab == 1 || _selectedTab == 2) && _previewCatalogDef != null)
                return _previewCatalogDef;
            if (_inspectedItem != null) return _inspectedItem;
            if (_selectedUnit != null)
            {
                var eq = _selectedUnit.GetOrBuildSheet().GetEquippedWeapon();
                if (eq != null) return eq;
                var sheet = _selectedUnit.GetOrBuildSheet();
                if (sheet.Inventory != null && sheet.Inventory.Count > 0) return sheet.Inventory[0];
            }
            return null;
        }

        protected override void DrawContent()
        {
            DrawUnitSelectorHeader();

            GUILayout.Space(4);
            _selectedTab = GUILayout.Toolbar(_selectedTab, _tabs);
            GUILayout.Space(6);

            if (!string.IsNullOrEmpty(_statusMessage))
            {
                GUI.color = Color.cyan;
                GUILayout.Label($"ℹ️ {_statusMessage}", GUI.skin.box);
                GUI.color = Color.white;
            }

            // Scroll global : garantit que TOUT le contenu reste atteignable même
            // si la fenêtre est redimensionnée petite (molette + barre verticale).
            _mainScroll = GUILayout.BeginScrollView(_mainScroll);

            // Large = 2 colonnes côte à côte ; étroit = empilé verticalement.
            bool wide = _windowRect.width >= 680f;
            if (wide)
            {
                GUILayout.BeginHorizontal();

                GUILayout.BeginVertical(GUILayout.Width(360), GUILayout.ExpandHeight(true));
                DrawLeftTab();
                GUILayout.EndVertical();

                GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                DrawStudioPreviewPanel();
                DrawItemDetailsCard();
                GUILayout.EndVertical();

                GUILayout.EndHorizontal();
            }
            else
            {
                DrawLeftTab();
                GUILayout.Space(6);
                GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true));
                DrawStudioPreviewPanel();
                DrawItemDetailsCard();
                GUILayout.EndVertical();
            }

            GUILayout.EndScrollView();

            if (GUI.changed)
            {
                try { CaptureDevPrefs(); } catch { /* ignore */ }
            }
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
            GUILayout.EndVertical();
        }

        private void SaveAndSync(CharacterSheet sheet)
        {
            if (sheet == null) return;
            CharacterStorageService.SaveCharacter(sheet);
            if (_selectedUnit != null) _selectedUnit.NotifyInventoryChanged(saveToDisk: false);
        }

        private void RefreshSceneUnits()
        {
            _sceneUnits.Clear();
            var all = FindObjectsByType<TacticalUnit>();
            for (int i = 0; i < all.Length; i++)
                if (all[i] != null && all[i].Stats != null) _sceneUnits.Add(all[i]);
            if (_selectedUnitIndex >= _sceneUnits.Count) _selectedUnitIndex = 0;
            _selectedUnit = _sceneUnits.Count > 0 ? _sceneUnits[_selectedUnitIndex] : null;
        }

        // ================= ONGLET 0 : SAC =================
        private void DrawInventoryListTab()
        {
            if (_selectedUnit == null)
            {
                GUILayout.Label("<i>Sélectionnez une unité pour inspecter son sac.</i>");
                return;
            }

            var sheet = _selectedUnit.GetOrBuildSheet();
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
            _catalogScroll = GUILayout.BeginScrollView(_catalogScroll, GUILayout.Height(320));

            for (int i = 0; i < list.Count; i++)
            {
                var def = list[i];
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
                GUILayout.Label($"<color=#7090A0>{def.Category} • {def.DlphCode} • {def.PriceCE} CE{(string.IsNullOrEmpty(def.PrefabPath) ? " • 🧱 placeholder" : " • 🔫 prefab")}</color>", RichLabel());
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
            if (_selectedUnit == null)
            {
                GUILayout.Label("<i>Sélectionnez une unité (son portefeuille CE sera débité/crédité).</i>");
                return;
            }
            var sheet = _selectedUnit.GetOrBuildSheet();
            GUI.color = Color.yellow;
            GUILayout.Label($"<b>💰 Solde : {sheet.CreditsCE} CE</b>  <color=gray>(revente = 50% catalogue)</color>", RichLabel());
            GUI.color = Color.white;

            DrawCatalogFilters(showAffordableToggle: true);
            var list = GetFilteredCatalog();

            _marketScroll = GUILayout.BeginScrollView(_marketScroll, GUILayout.Height(220));
            for (int i = 0; i < list.Count; i++)
            {
                var def = list[i];
                bool afford = sheet.CanAfford(def.PriceCE);
                bool isSelected = (_previewCatalogDef == def);
                GUI.backgroundColor = isSelected ? new Color(0.0f, 0.85f, 1.0f) : (afford ? Color.white : new Color(0.6f, 0.6f, 0.6f));
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button($"{IconFor(def)} <b>{def.Name}</b>\n{def.PriceCE} CE", GUI.skin.label, GUILayout.ExpandWidth(true)))
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

            // 15 catégories + "Toutes" : un Toolbar mono-ligne les écrase / clippe
            // (seules les 4 premières restaient visibles). Grille multi-lignes =>
            // TOUS les types d'équipements sont visibles et cliquables, et le scroll
            // global permet de tout atteindre même en fenêtre petite.
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
                _categoryFilter = -1;
                _affordableOnly = false;
                _showOnlyRealPrefabs = false;
            }
            GUILayout.EndHorizontal();
        }

        private List<InventoryItem> GetFilteredCatalog()
        {
            var result = new List<InventoryItem>();
            var sheet = _selectedUnit != null ? _selectedUnit.GetOrBuildSheet() : null;
            for (int i = 0; i < ArmoryCatalog.All.Count; i++)
            {
                var def = ArmoryCatalog.All[i];
                if (def == null) continue;
                if (_categoryFilter >= 0 && def.Category != ArmoryCatalog.Categories[_categoryFilter]) continue;
                if (_showOnlyRealPrefabs && string.IsNullOrEmpty(def.PrefabPath)) continue;
                if (_affordableOnly && sheet != null && !sheet.CanAfford(def.PriceCE)) continue;
                if (!string.IsNullOrEmpty(_searchFilter))
                {
                    string q = _searchFilter.ToLowerInvariant();
                    if (!def.Name.ToLowerInvariant().Contains(q) && !(def.Category ?? "").ToLowerInvariant().Contains(q)
                        && !(def.Description ?? "").ToLowerInvariant().Contains(q)) continue;
                }
                result.Add(def);
            }
            return result;
        }

        private void GiveCatalogItemToUnit(InventoryItem def)
        {
            if (_selectedUnit == null) { _statusMessage = "Sélectionnez d'abord une unité."; return; }
            var sheet = _selectedUnit.GetOrBuildSheet();
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
            if (_selectedUnit == null) { _statusMessage = "Sélectionnez d'abord une unité."; return; }
            var sheet = _selectedUnit.GetOrBuildSheet();
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

        // ================= ONGLET 3 : PROPRIÉTÉS =================
        private void DrawItemPropertiesTab()
        {
            if (_inspectedItem == null)
            {
                GUILayout.Label("<i>Sélectionnez un objet dans le sac pour modifier ses propriétés.</i>");
                if (_previewCatalogDef != null)
                    GUILayout.Label($"<i>Aperçu catalogue : {_previewCatalogDef.Name} — utilisez + Sac / Acheter pour l'obtenir puis éditez-le.</i>");
                return;
            }

            GUILayout.Label($"<b>Édition : {_inspectedItem.Name}</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Nom :", GUILayout.Width(90));
            _inspectedItem.Name = GUILayout.TextField(_inspectedItem.Name);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Dégâts Bruts :", GUILayout.Width(90));
            _inspectedItem.BaseDamage = (int)GUILayout.HorizontalSlider(_inspectedItem.BaseDamage, 0, 25);
            GUILayout.Label($"{_inspectedItem.BaseDamage}", GUILayout.Width(30));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Portée (Cases) :", GUILayout.Width(90));
            _inspectedItem.RangeInTiles = (int)GUILayout.HorizontalSlider(_inspectedItem.RangeInTiles, 1, 20);
            GUILayout.Label($"{_inspectedItem.RangeInTiles}", GUILayout.Width(30));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Poids (Kg) :", GUILayout.Width(90));
            _inspectedItem.WeightKg = GUILayout.HorizontalSlider(_inspectedItem.WeightKg, 0.05f, 20.0f);
            GUILayout.Label($"{_inspectedItem.WeightKg:0.#}", GUILayout.Width(30));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Prix (CE) :", GUILayout.Width(90));
            _inspectedItem.PriceCE = (int)GUILayout.HorizontalSlider(_inspectedItem.PriceCE, 0, 250000);
            GUILayout.Label($"{_inspectedItem.PriceCE}", GUILayout.Width(60));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Armure :", GUILayout.Width(90));
            _inspectedItem.ArmorProtection = (int)GUILayout.HorizontalSlider(_inspectedItem.ArmorProtection, 0, 10);
            GUILayout.Label($"{_inspectedItem.ArmorProtection}", GUILayout.Width(30));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Soin PV :", GUILayout.Width(90));
            _inspectedItem.HealingAmount = (int)GUILayout.HorizontalSlider(_inspectedItem.HealingAmount, 0, 30);
            GUILayout.Label($"{_inspectedItem.HealingAmount}", GUILayout.Width(30));
            GUILayout.EndHorizontal();

            GUILayout.Label("Description :");
            _inspectedItem.Description = GUILayout.TextArea(_inspectedItem.Description, GUILayout.Height(50));

            GUILayout.Space(6);
            GUI.backgroundColor = new Color(0.2f, 0.75f, 0.4f);
            if (GUILayout.Button("💾 Sauvegarder sur Disque", GUILayout.Height(28)))
            {
                var sheet = _selectedUnit != null ? _selectedUnit.GetOrBuildSheet() : null;
                SaveAndSync(sheet);
                _statusMessage = $"Propriétés de '{_inspectedItem.Name}' enregistrées.";
            }
            GUI.backgroundColor = Color.white;

            GUILayout.EndVertical();
        }

        private static string IconFor(InventoryItem item)
        {
            if (item == null) return "📦";
            string nm = item.Name ?? "";
            string cat = item.Category ?? "";
            switch (item.Type)
            {
                case ItemType.Weapon:
                    if (cat.Contains("Laser") && (nm.Contains("Épée") || nm.Contains("Sabre") || nm.Contains("Espadon"))) return "🔷";
                    if (cat == "Lance-Grenades" || (item.IsLauncher)) return "🚀";
                    if (item.RangeInTiles > 2) return "🔫";
                    if (cat == "Grenades" || item.IsGrenade) return "💣";
                    return "🗡️";
                case ItemType.Ammunition: return "📦";
                case ItemType.Armor: return "🛡️";
                case ItemType.Arcanotech: return "🔮";
                case ItemType.Consumable: return item.HealingAmount > 0 ? "💉" : "💊";
                default: return "🎒";
            }
        }

        // ================= STUDIO 3D =================
        private void DrawStudioPreviewPanel()
        {
            GUILayout.Label("<b>Inspection 3D (prefab réel ou placeholder) :</b>");
            Rect rect = GUILayoutUtility.GetRect(280, 200);

            Color prevBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.015f, 0.035f, 0.065f, 0.98f);
            GUI.Box(rect, GUIContent.none);
            GUI.backgroundColor = prevBg;

            if (_previewRT != null) GUI.DrawTexture(rect, _previewRT, ScaleMode.ScaleToFit);

            Event e = Event.current;
            if (rect.Contains(e.mousePosition))
            {
                if (e.type == EventType.MouseDown && (e.button == 0 || e.button == 1))
                {
                    _isDraggingPreview = true;
                    _lastMousePos = e.mousePosition;
                    e.Use();
                }
            }

            if (_isDraggingPreview)
            {
                if (e.type == EventType.MouseDrag)
                {
                    _previewYaw -= (e.mousePosition.x - _lastMousePos.x) * 1.5f;
                    _previewPitch = Mathf.Clamp(_previewPitch + (e.mousePosition.y - _lastMousePos.y) * 1.2f, -60f, 60f);
                    _lastMousePos = e.mousePosition;
                    e.Use();
                }
                else if (e.type == EventType.MouseUp)
                {
                    _isDraggingPreview = false;
                    e.Use();
                }
            }

            Rect labelRect = new Rect(rect.x, rect.y + rect.height - 18, rect.width, 16);
            var footerStyle = new GUIStyle(GUI.skin.label) { fontSize = 9, alignment = TextAnchor.MiddleCenter };
            footerStyle.normal.textColor = new Color(0.0f, 0.9f, 1.0f, 0.7f);
            var prev = GetCurrentPreviewItem();
            string tag = prev == null ? "—" : (string.IsNullOrEmpty(prev.PrefabPath) || ArmoryPlaceholderFactory.LoadRealPrefab(prev.PrefabPath) == null ? "🧱 placeholder procédural" : "🔫 prefab réel");
            GUI.Label(labelRect, $"↔ Pivoter (glisser) • {tag}", footerStyle);
        }

        private void DrawItemDetailsCard()
        {
            var item = GetCurrentPreviewItem();
            if (item != null)
            {
                GUI.color = Color.white;
                GUILayout.Label($"<b>Fiche : <color=#00E5FF>{item.Name}</color></b>", RichLabel());
                GUILayout.Label($"<color=#9FB3C8>{item.Category} • {item.Type} • {item.Rarity} • {item.DlphCode}</color>", RichLabel());
                string stats = $"Dégâts <b><color=#FF3B5C>{item.BaseDamage}</color></b> • Portée <b>{item.RangeInTiles}</b> • <b>{item.WeightKg:0.#} kg</b> • <b><color=#FFD75F>{item.PriceCE} CE</color></b>";
                if (item.ArmorProtection > 0) stats += $" • 🛡️+{item.ArmorProtection}";
                if (item.ShieldHP > 0) stats += $" • 🔮{item.ShieldHP} PV";
                if (item.HealingAmount > 0) stats += $" • 💉+{item.HealingAmount} PV";
                if (item.AttackBonusEc != 0) stats += $" • +{item.AttackBonusEc}ec";
                if (item.ApCostModifier != 0) stats += $" • {item.ApCostModifier:+0;-0} PA";
                GUILayout.Label(stats, RichLabel());
                GUILayout.Label($"Compétence <b>{item.AssociatedSkill}</b> • Slot <b>{item.EquipSlot}</b> • {(item.IsEquipped ? "<color=#00FF88>Équipé</color>" : "En réserve")}", RichLabel());
                if (item.IsGrenade)
                {
                    string gLine = $"💣 <b>{item.Era} / {item.GrenadeKind}</b> • Souffle R<b>{item.BlastRadius}</b> • <b>{item.BaseDamage}+{item.DamageDiceCount}d10</b> + shrap <b>{item.ShrapnelDamage}</b>";
                    if (!string.IsNullOrEmpty(item.GrenadeStatuses)) gLine += $" • [{item.GrenadeStatuses}]";
                    if (item.ZoneDurationTurns > 0) gLine += $" • Zone {item.ZoneDurationTurns} tours";
                    gLine += item.LauncherCompatible ? " • <color=#00E5FF>Compatible lanceur</color>" : " • <i>Main uniquement</i>";
                    if (item.AccuracyBonus != 0) gLine += $" • Précision {item.AccuracyBonus:+0;-0}";
                    GUILayout.Label(gLine, RichLabel());
                }
                if (item.IsLauncher)
                {
                    GUILayout.Label($"🚀 Lance-grenades : <b>+{item.LauncherRangeBonus} cases</b> (max 20) • Précision <b>{item.AccuracyBonus:+0;-0}</b> • Tir 3 PA", RichLabel());
                }
                GUILayout.Label($"<i>{item.Description}</i>", RichLabel());

                // Actions rapides contextuelles
                if (_selectedTab != 0 && _selectedUnit != null)
                {
                    GUILayout.BeginHorizontal();
                    GUI.backgroundColor = new Color(0.2f, 0.8f, 0.4f);
                    if (GUILayout.Button("+ Sac (dotation)", GUILayout.Height(26))) GiveCatalogItemToUnit(item);
                    GUI.backgroundColor = new Color(0.95f, 0.75f, 0.15f);
                    var sheet = _selectedUnit.GetOrBuildSheet();
                    GUI.enabled = sheet.CanAfford(item.PriceCE);
                    if (GUILayout.Button($"Acheter {item.PriceCE} CE", GUILayout.Height(26)))
                    {
                        ArmoryCatalog.BuyForSheet(sheet, item.Name, out string msg);
                        SaveAndSync(sheet);
                        _statusMessage = msg;
                    }
                    GUI.enabled = true;
                    GUI.backgroundColor = Color.white;
                    GUILayout.EndHorizontal();
                }
            }
            else
            {
                GUILayout.Label("<color=gray><i>Sélectionnez un objet pour voir sa fiche et son modèle 3D.</i></color>");
            }
        }

        private void EnsurePreviewStudio()
        {
            if (_previewRT == null)
            {
                _previewRT = new RenderTexture(256, 256, 16, RenderTextureFormat.ARGB32) { name = "GunPreview_RT" };
                _previewRT.Create();
            }

            if (_previewStudioRoot == null)
            {
                _previewStudioRoot = new GameObject("[Studio] GunModelPreview");
                _previewStudioRoot.transform.position = new Vector3(6000f, -5000f, 6000f);

                var camGo = new GameObject("GunCamera");
                camGo.transform.SetParent(_previewStudioRoot.transform, false);
                _previewCam = camGo.AddComponent<Camera>();
                _previewCam.clearFlags = CameraClearFlags.SolidColor;
                _previewCam.backgroundColor = new Color(0.015f, 0.035f, 0.065f, 1.0f);
                _previewCam.fieldOfView = 32f;
                _previewCam.nearClipPlane = 0.05f;
                _previewCam.farClipPlane = 40f;
                _previewCam.targetTexture = _previewRT;
                _previewCam.enabled = false;

                var lightGo = new GameObject("GunKeyLight");
                lightGo.transform.SetParent(_previewStudioRoot.transform, false);
                var key = lightGo.AddComponent<Light>();
                key.type = LightType.Directional;
                key.color = new Color(0.95f, 0.98f, 1.0f);
                key.intensity = 1.4f;
                lightGo.transform.rotation = Quaternion.Euler(35f, -30f, 0f);

                var rimGo = new GameObject("GunRimLight");
                rimGo.transform.SetParent(_previewStudioRoot.transform, false);
                var rim = rimGo.AddComponent<Light>();
                rim.type = LightType.Directional;
                rim.color = new Color(0.0f, 0.85f, 1.0f);
                rim.intensity = 0.85f;
                rimGo.transform.rotation = Quaternion.Euler(-20f, 150f, 0f);
            }
        }

        private void UpdatePreviewModel(InventoryItem item)
        {
            string key = item != null ? $"{item.Name}|{item.PrefabPath}|{item.PlaceholderKind}" : "__NONE__";
            if (key == _lastLoadedModelKey && _currentPreviewInstance != null) return;

            EnsurePreviewStudio();
            _lastLoadedModelKey = key;

            if (_currentPreviewInstance != null)
            {
                Destroy(_currentPreviewInstance);
                _currentPreviewInstance = null;
            }

            if (item != null)
                _currentPreviewInstance = ArmoryPlaceholderFactory.ResolveOrBuild(item, _previewStudioRoot.transform);
            else
                _currentPreviewInstance = ArmoryPlaceholderFactory.BuildByKind("Generic", _previewStudioRoot.transform, "Placeholder_Vide");

            _currentPreviewInstance.transform.localPosition = Vector3.zero;
            _currentPreviewInstance.transform.localRotation = Quaternion.Euler(_previewPitch, _previewYaw, 0f);
            FramePreviewCamera();
        }

        private void FramePreviewCamera()
        {
            if (_previewCam == null || _currentPreviewInstance == null) return;
            var renderers = _currentPreviewInstance.GetComponentsInChildren<Renderer>();
            Bounds bounds;
            if (renderers.Length > 0)
            {
                bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            }
            else bounds = new Bounds(_currentPreviewInstance.transform.position, Vector3.one * 0.5f);

            float maxDim = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            maxDim = Mathf.Max(maxDim, 0.25f);
            float dist = (maxDim * 0.5f) / Mathf.Tan(_previewCam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            dist *= 1.4f;
            Vector3 focus = bounds.center;
            _previewCam.transform.position = focus + new Vector3(0f, 0f, dist);
            _previewCam.transform.LookAt(focus);
        }

        private void RenderPreviewStudio()
        {
            if (_previewCam == null || _currentPreviewInstance == null) return;
            if (!_isDraggingPreview) _previewYaw = (_previewYaw + Time.unscaledDeltaTime * 18f) % 360f;
            _currentPreviewInstance.transform.localRotation = Quaternion.Euler(_previewPitch, _previewYaw, 0f);
            _previewCam.Render();
        }

        private void CleanupPreviewStudio()
        {
            if (_currentPreviewInstance != null) { Destroy(_currentPreviewInstance); _currentPreviewInstance = null; }
            if (_previewStudioRoot != null) { Destroy(_previewStudioRoot); _previewStudioRoot = null; }
            if (_previewRT != null) { _previewRT.Release(); Destroy(_previewRT); _previewRT = null; }
            _lastLoadedModelKey = "__NONE__";
        }

        private static GUIStyle _richLabel;
        private static GUIStyle RichLabel()
        {
            if (_richLabel == null)
                _richLabel = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true, fontSize = 10 };
            return _richLabel;
        }
    }
}

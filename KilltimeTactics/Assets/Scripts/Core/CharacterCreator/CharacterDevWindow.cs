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
using Killtime.Tactics;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;

namespace Killtime.UI
{
    /// <summary>
    /// Créateur & Gestionnaire de Personnages avec auto-instanciation et placement sur la grille.
    /// </summary>
    public class CharacterDevWindow : FloatingWindow<CharacterDevWindow>
    {
        protected override int WindowId => 888;
        protected override string Title => "Créateur de Personnages";
        protected override Vector2 MinSize => _minSize;
        protected override Rect DefaultRect => new Rect(Screen.width - 660f - 20f, 92f, 660f, Mathf.Min(720f, Screen.height - 110f));
        protected override KeyCode[] ToggleKeys => _toggleKeys;

        private static readonly KeyCode[] _toggleKeys = { KeyCode.F1 };
        private static readonly Vector2 _minSize = new Vector2(380, 240);

        [Header("Systèmes")]
        [SerializeField] private TacticalHexGrid _grid;
        [SerializeField] private TurnManager _turnManager;
        [SerializeField] private CombatDevArena _arena;

        private CharacterSheet _currentSheet = new();
        private int _selectedTab = 0;
        private readonly string[] _tabTitles = { "👤 Création", "🎒 Inventaire", "📈 Progression", "🔮 Sorts (XP=PA)", "💾 Disque", "🗺️ Spawner" };

        private readonly List<string> _availableModelNames = new();
        private readonly List<string> _availableGunCatalog = new();
        private InventoryItem _selectedInventoryItem;
        private Vector2 _invScroll;
        private Vector2 _gunCatalogScroll;
        private string _customItemName = "Nouvel Équipement";
        private int _customItemDamage = 6;
        private int _customItemRange = 8;
        private float _customItemWeight = 1.5f;
        private bool _showModelDropdown = false;

        // --- Studio d'Aperçu 3D Temps Réel & Animations ---
        private RenderTexture _previewRT;
        private Camera _previewCam;
        private GameObject _previewStudioRoot;
        private GameObject _currentPreviewInstance;
        private Animator _previewAnimator;
        private PlayableGraph _previewPlayableGraph;
        private string _lastLoadedModelName = "__UNINITIALIZED__";
        private string _lastLoadedGenderKey = "__UNINITIALIZED__";
        private float _previewModelYaw = 180f;
        private bool _isDraggingPreview = false;
        private Vector2 _lastMousePos;

        private int _selectedPreviewAnimIndex = 0;
        private bool _showAnimDropdown = false;
        private bool _showHiddenModelCheats = false;
        private readonly string[] _previewAnimationStates = {
            "Standing Idle",
            "Standing Idle To Fight Idle",
            "Fight Idle",
            "Fight Idle To Standing Idle",
            "Action Idle To Fight Idle",
            "Action Idle To Standing Idle",
            "Walking",
            "Roundkick",
            "Body Block",
            "Falling Back Death",
            "Rifle Idle",
            "Rifle Walk To Stop",
            "Firing Rifle",
            "Rifle Put Away"
        };

        private Vector2 _scrollPos;

        private string _statusMessage = "Prêt.";
        private string _spawnQStr = "0";
        private string _spawnRStr = "1";
        private bool _spawnAsPlayer = false;

        // Sorts modulaires (Livre IV §19)
        private string _customSpellName = "Onde de Choc Causal";
        private string _customSpellDesc = "Impulsion de 5e Force comprimée.";
        private PsychicDiscipline _customSpellDiscipline = PsychicDiscipline.Telekinesie;
        private PsychicStage _customSpellStage = PsychicStage.Stade2_AccesProfond;
        private SkillType _customSpellSkill = SkillType.MagieEsprit;
        private readonly Dictionary<ArcanotechModuleId, int> _spellModuleRanks = new();
        private Vector2 _spellForgeModulesScroll;

        protected override void OnOpened()
        {
            EnsureReferences();
            EnsurePreviewStudio();
            try { LoadDevPrefs(); } catch { /* ignore */ }
        }

        protected override void OnClosed()
        {
            try { CaptureDevPrefs(); DevUIPreferences.SaveNow(); } catch { /* ignore */ }
        }

        /// <summary>Fiche en cours de création/édition (cible du lien armurerie).</summary>
        public CharacterSheet CurrentSheet => _currentSheet;

        public static void OpenForUnit(TacticalUnit unit)
        {
            Open();
            Instance?.InspectUnit(unit);
        }

        public void InspectUnit(TacticalUnit unit)
        {
            if (unit == null) return;
            _currentSheet = unit.GetOrBuildSheet();
            _statusMessage = $"Inspection active : {unit.Stats.Name} (PV: {unit.Stats.CurrentHealth}/{unit.Stats.MaxHealth} | PA: {unit.Stats.CurrentActionPoints}/{unit.Stats.MaxActionPoints})";
            _selectedTab = 0;
            OpenInstance();
        }

        protected override void Awake()
        {
            base.Awake();
            EnsureReferences();
            CharacterStorageService.EnsureDirectoryExists();
            RefreshAvailableModels();
            RefreshGunCatalog();
            EnsurePreviewStudio();
            try { LoadDevPrefs(); } catch { /* ignore */ }
        }

        private void LoadDevPrefs()
        {
            var p = DevUIPreferences.Current;
            if (p == null) return;
            _selectedTab = Mathf.Clamp(p.CharacterTab, 0, _tabTitles.Length - 1);
            _previewModelYaw = p.PreviewYaw;
            _selectedPreviewAnimIndex = Mathf.Clamp(p.PreviewAnimIndex, 0, _previewAnimationStates.Length - 1);
            if (!string.IsNullOrEmpty(p.SpawnQ)) _spawnQStr = p.SpawnQ;
            if (!string.IsNullOrEmpty(p.SpawnR)) _spawnRStr = p.SpawnR;
            _spawnAsPlayer = p.SpawnAsPlayer;
        }

        private void CaptureDevPrefs()
        {
            var p = DevUIPreferences.Current;
            if (p == null) return;
            p.CharacterTab = _selectedTab;
            p.PreviewYaw = _previewModelYaw;
            p.PreviewAnimIndex = _selectedPreviewAnimIndex;
            p.SpawnQ = _spawnQStr ?? "0";
            p.SpawnR = _spawnRStr ?? "1";
            p.SpawnAsPlayer = _spawnAsPlayer;
            DevUIPreferences.MarkDirty();
        }

        private void RefreshGunCatalog()
        {
            _availableGunCatalog.Clear();

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
                        if (!_availableGunCatalog.Contains(relative))
                        {
                            _availableGunCatalog.Add(relative);
                        }
                    }
                    else
                    {
                        string fName = Path.GetFileNameWithoutExtension(path);
                        if (!_availableGunCatalog.Contains(fName)) _availableGunCatalog.Add(fName);
                    }
                }
            }
#endif

            if (_availableGunCatalog.Count == 0)
            {
                var prefabs = Resources.LoadAll<GameObject>("Guns");
                for (int i = 0; i < prefabs.Length; i++)
                {
                    if (prefabs[i] != null && !_availableGunCatalog.Contains(prefabs[i].name))
                    {
                        _availableGunCatalog.Add(prefabs[i].name);
                    }
                }
            }
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

        private void RefreshAvailableModels()
        {
            _availableModelNames.Clear();
            _availableModelNames.Add("(Procédural)");

            var prefabs = Resources.LoadAll<GameObject>("Characters");
            if (prefabs != null)
            {
                for (int i = 0; i < prefabs.Length; i++)
                {
                    var p = prefabs[i];
                    if (p != null && !_availableModelNames.Contains(p.name))
                    {
                        _availableModelNames.Add(p.name);
                    }
                }
            }
        }

        private void EnsureReferences()
        {
            if (_grid == null) _grid = FindAnyObjectByType<TacticalHexGrid>();
            if (_turnManager == null) _turnManager = FindAnyObjectByType<TurnManager>();
            if (_arena == null) _arena = FindAnyObjectByType<CombatDevArena>();
        }

        protected override void Update()
        {
            base.Update();

            if (_isOpen && (_selectedTab == 0 || _selectedTab == 5))
            {
                UpdatePreviewModel(_currentSheet.ModelPrefabName);
                RenderPreviewStudio();
            }
        }

        protected override void DrawContent()
        {
            GUILayout.Space(6);

            // --- Barre d'actions globales : fiche active + Nouveau Personnage ---
            GUILayout.BeginHorizontal(GUI.skin.box);
            string sheetLabel = _currentSheet != null ? _currentSheet.Name : "(aucune fiche)";
            GUILayout.Label($"Fiche active : <b>{sheetLabel}</b>", GUILayout.ExpandWidth(true));
            GUI.backgroundColor = new Color(0.2f, 0.6f, 1.0f);
            if (GUILayout.Button("➕ Nouveau Personnage", GUILayout.Width(180), GUILayout.Height(26)))
            {
                CreateNewCharacter();
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();
            GUILayout.Space(4);

            _selectedTab = GUILayout.Toolbar(_selectedTab, _tabTitles);
            GUILayout.Space(6);

            if (!string.IsNullOrEmpty(_statusMessage))
            {
                GUI.color = Color.yellow;
                GUILayout.Label($"ℹ️ {_statusMessage}", GUI.skin.box);
                GUI.color = Color.white;
            }

            _scrollPos = GUILayout.BeginScrollView(_scrollPos);

            switch (_selectedTab)
            {
                case 0: DrawCreationTab(); break;
                case 1: DrawInventoryTab(); break;
                case 2: DrawProgressionTab(); break;
                case 3: DrawSpellForgeTab(); break;
                case 4: DrawStorageTab(); break;
                case 5: DrawMapInsertionTab(); break;
            }

            GUILayout.EndScrollView();

            if (GUI.changed)
            {
                try { CaptureDevPrefs(); } catch { /* ignore */ }
            }
        }

        private void SaveCharacterAndSyncUnits(CharacterSheet sheet)
        {
            if (sheet == null) return;
            CharacterStorageService.SaveCharacter(sheet);

            var allUnits = FindObjectsByType<TacticalUnit>();
            for (int i = 0; i < allUnits.Length; i++)
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

        private void CreateNewCharacter()
        {
            _currentSheet = new CharacterSheet();
            ApplyDefaultProfileAttributes(_currentSheet);
            _currentSheet.Name = "Nouveau Personnage";

            // Réinitialise l'état d'édition lié à l'ancienne fiche.
            _selectedInventoryItem = null;
            _selectedTab = 0;
            _scrollPos = Vector2.zero;
            _invScroll = Vector2.zero;
            _gunCatalogScroll = Vector2.zero;
            _showModelDropdown = false;
            _showAnimDropdown = false;
            _selectedPreviewAnimIndex = 0;
            _lastLoadedModelName = "__UNINITIALIZED__";
            _lastLoadedGenderKey = "__UNINITIALIZED__";

            // L'armurerie suit automatiquement CurrentSheet, mais si elle était
            // liée à l'ancien brouillon on la re-lie explicitement à la nouvelle fiche.
            try
            {
                if (InventoryDevWindow.Instance != null && InventoryDevWindow.Instance.IsBoundToDraft)
                    InventoryDevWindow.Instance.InspectSheet(_currentSheet);
            }
            catch { /* ignore */ }

            _statusMessage = "➕ Nouveau personnage créé — fiche vierge prête à configurer. Pensez à la sauvegarder.";
        }

        private void DrawInventoryTab()
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
                    break;
                }
                GUI.backgroundColor = Color.white;

                GUILayout.EndHorizontal();

                GUILayout.Label($"<color=#7090A0>Dégâts: <b>{item.BaseDamage}</b> | Portée: <b>{item.RangeInTiles}</b> | Poids: <b>{item.WeightKg:0.#} kg</b> | Compétence: <b>{item.AssociatedSkill}</b></color>");
                GUILayout.EndVertical();
            }
            GUILayout.EndScrollView();

            GUILayout.Space(8);
            GUILayout.Label("<b>2. Armurerie complète (Livre VIII) & Marché :</b>");
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
                string gunPath = _availableGunCatalog[i];
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
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

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

        // ================= TAB 0 : CRÉATION =================
        private void DrawCreationTab()
        {
            CharacterProgressionManager.SynchronizeProgression(_currentSheet);

            GUILayout.Label("<b>1. Identité & Profil (Livre I) :</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Nom :", GUILayout.Width(90));
            _currentSheet.Name = GUILayout.TextField(_currentSheet.Name);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Âge / Sexe :", GUILayout.Width(90));
            string ageStr = GUILayout.TextField(_currentSheet.Age.ToString(), GUILayout.Width(50));
            if (int.TryParse(ageStr, out int a)) _currentSheet.Age = a;
            int genderIndex = GenderToIndex(_currentSheet.Gender);
            int newGenderIndex = GUILayout.Toolbar(genderIndex, new[] { "Masculin", "Féminin", "Autre" });
            if (newGenderIndex != genderIndex)
            {
                _currentSheet.Gender = new[] { "Masculin", "Féminin", "Autre" }[Mathf.Clamp(newGenderIndex, 0, 2)];
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Espèce :", GUILayout.Width(90));
            _currentSheet.Species = (SpeciesType)GUILayout.Toolbar((int)_currentSheet.Species, Enum.GetNames(typeof(SpeciesType)));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Profil :", GUILayout.Width(90));
            var prevProfile = _currentSheet.Profile;
            _currentSheet.Profile = (CharacterProfileType)GUILayout.Toolbar((int)_currentSheet.Profile, Enum.GetNames(typeof(CharacterProfileType)));
            if (_currentSheet.Profile != prevProfile)
            {
                ApplyDefaultProfileAttributes(_currentSheet);
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Gabarit :", GUILayout.Width(90));
            _currentSheet.Footprint = (TitanFootprintType)GUILayout.Toolbar((int)_currentSheet.Footprint, new[] { "Normal (1x1)", "Titan (Rosette 7)", "Triangle (3)", "Colosse (19)" });
            GUILayout.EndHorizontal();

            if (_currentSheet.Footprint != TitanFootprintType.Single)
            {
                GUI.color = new Color(1f, 0.4f, 0.4f);
                int cellCount = TitanFootprint.GetOccupiedCoordinates(new HexCoordinates(0, 0), _currentSheet.Footprint).Count;
                GUILayout.Label($"⚠️ <b>Entité Colossale (Livre XI) :</b> Empreinte de {cellCount} hexagones. Nécessite une zone dégagée lors du spawn.", GUI.skin.box);
                GUI.color = Color.white;
            }

            DrawModelSelector();

            GUILayout.EndVertical();

            bool isAllocationValid = ValidateAttributeAllocation(_currentSheet, out string allocationStatus);

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>2. Les 8 Attributs Fondamentaux (Plafond Absolu : 10) :</b>");
            GUILayout.FlexibleSpace();
            GUI.color = isAllocationValid ? Color.green : new Color(1.0f, 0.4f, 0.4f);
            GUILayout.Label(allocationStatus, GUI.skin.box);
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(285));
            GUILayout.Label("<b>🐣 CRÉATION INITIALE (Budget Départ)</b>");
            GUILayout.Label("<color=#94A3B8><size=10>Piliers et points secondaires selon le profil.</size></color>");
            GUILayout.Space(4);

            _currentSheet.BaseAttributes.Force = DrawAttrRow("Force (FOR)", _currentSheet.BaseAttributes.Force, 0);
            _currentSheet.BaseAttributes.Agilite = DrawAttrRow("Agilité (AGI)", _currentSheet.BaseAttributes.Agilite, 1);
            _currentSheet.BaseAttributes.Constitution = DrawAttrRow("Constitution (CON)", _currentSheet.BaseAttributes.Constitution, 2);
            _currentSheet.BaseAttributes.Rapidite = DrawAttrRow("Rapidité (RAP)", _currentSheet.BaseAttributes.Rapidite, 3);
            _currentSheet.BaseAttributes.Intelligence = DrawAttrRow("Intelligence (INT)", _currentSheet.BaseAttributes.Intelligence, 4);
            _currentSheet.BaseAttributes.Erudition = DrawAttrRow("Érudition (ÉRU)", _currentSheet.BaseAttributes.Erudition, 5);
            _currentSheet.BaseAttributes.Charisme = DrawAttrRow("Charisme (CHA)", _currentSheet.BaseAttributes.Charisme, 6);
            _currentSheet.BaseAttributes.Instinct = DrawAttrRow("Instinct (INS)", _currentSheet.BaseAttributes.Instinct, 7);
            _currentSheet.BaseAttributes.Magie = DrawAttrRow("Magie (5e Force)", _currentSheet.BaseAttributes.Magie, 8);

            GUILayout.EndVertical();

            GUILayout.Space(6);

            GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true));
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>⚡ ÉVOLUTION EN JEU (Achat XP)</b>");
            GUILayout.FlexibleSpace();
            GUILayout.Label($"<color=yellow><b>{_currentSheet.AvailableXP} XP Libre</b></color>");
            GUILayout.EndHorizontal();
            GUILayout.Label("<color=#94A3B8><size=10>Coût : passer de N à N+1 = (N+1) XP (ex. 4➔5 = 5 XP, 9➔10 = 10 XP).</size></color>");
            GUILayout.Space(4);

            DrawAttrXpUpgradeRow("Force (FOR)", 0);
            DrawAttrXpUpgradeRow("Agilité (AGI)", 1);
            DrawAttrXpUpgradeRow("Constitution (CON)", 2);
            DrawAttrXpUpgradeRow("Rapidité (RAP)", 3);
            DrawAttrXpUpgradeRow("Intelligence (INT)", 4);
            DrawAttrXpUpgradeRow("Érudition (ÉRU)", 5);
            DrawAttrXpUpgradeRow("Charisme (CHA)", 6);
            DrawAttrXpUpgradeRow("Instinct (INS)", 7);
            DrawAttrXpUpgradeRow("Magie (5e Force)", 8);

            GUILayout.EndVertical();

            GUILayout.EndHorizontal();

            var effective = _currentSheet.GetEffectiveAttributes();
            GUILayout.Space(6);
            GUILayout.Label("<b>3. Métriques Vitales Dérivées (Temps Réel) :</b>");
            GUILayout.BeginHorizontal(GUI.skin.box);
            GUI.color = Color.cyan;
            GUILayout.Label($"⚡ PA : <b>{effective.CalculateBaseActionPoints()}</b>");
            GUI.color = Color.green;
            GUILayout.Label($"🛡️ Encaissement : <b>{effective.CalculateEncaissement()}</b>");
            GUI.color = Color.red;
            GUILayout.Label($"💀 Seuil Mort : <b>{effective.CalculateLethalMaximum()} PV</b>");
            GUI.color = Color.yellow;
            GUILayout.Label($"🫁 Souffle : <b>{effective.Constitution} tours</b>");
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label("<b>4. Arbre de Progression (Livre I §5) :</b>");
            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.Label($"Entraînements : <b>{CountTrainedLevels(_currentSheet)}</b> (gratuits <b>{Mathf.Max(0, _currentSheet.FreeTrainingsUsed)}/{_currentSheet.GetFreeTrainingBudget()}</b>, restants <b>{_currentSheet.GetFreeTrainingsRemaining()}</b>) | Spés : <b>{_currentSheet.UnlockedSpecializations.Count}</b> | Banques liées : <b>{_currentSheet.GetLinkedReserveTotal()} XP</b> | XP libre : <b>{_currentSheet.AvailableXP}</b> | <color=#FFD166>XP Total : <b>{_currentSheet.TotalSpentXP} XP</b></color>", GUILayout.ExpandWidth(true));
            GUI.enabled = _currentSheet.AvailableXP > 0;
            if (GUILayout.Button("↺ XP libre à 0", GUILayout.Width(110), GUILayout.Height(28)))
            {
                if (CharacterProgressionManager.ResetFreeXP(_currentSheet, out string freeMsg, out _))
                    _statusMessage = freeMsg;
                else
                    _statusMessage = freeMsg;
            }
            GUI.enabled = true;
            GUI.backgroundColor = new Color(0.95f, 0.45f, 0.2f);
            if (GUILayout.Button("♻️ Reset arbre (XP restaurés, entraînements à 0)", GUILayout.Width(320), GUILayout.Height(28)))
            {
                if (CharacterProgressionManager.ResetProgressionTree(_currentSheet, out string resetMsg))
                    _statusMessage = resetMsg;
                else
                    _statusMessage = resetMsg;
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();
            GUILayout.Label("<i>Érudition = entraînements gratuits (budget = ÉRU effective, +1 ÉRU = +1 gratuit). Gratuits : 0 XP, hors XP total. Appliquez-les dans l'onglet Progression (bouton Gratuit).</i>");

            GUILayout.Space(6);
            DrawClassPresetSection();

            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.2f, 0.6f, 1.0f);
            if (GUILayout.Button("➕ Nouveau Personnage", GUILayout.Height(34)))
            {
                CreateNewCharacter();
            }
            GUI.backgroundColor = new Color(0.2f, 0.7f, 0.3f);
            if (GUILayout.Button("💾 Sauvegarder cette fiche sur le Disque", GUILayout.Height(34)))
            {
                SaveCharacterAndSyncUnits(_currentSheet);
                _statusMessage = $"Fiche '{_currentSheet.Name}' sauvegardée et synchronisée avec la scène !";
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();
        }

        // ================= CLASSES DE BASE (chargement rapide + guide) =================

        /// <summary>
        /// Section 5 — Archétypes de classe : chargement en 1 clic + guide de build.
        /// Le chargement applique attributs + package de départ ; l'arbre reste
        /// 100% libre ensuite, les étapes futures sont highlightées en or.
        /// </summary>
        private void DrawClassPresetSection()
        {
            GUILayout.Label("<b>5. Archétypes de Classe — Chargement Rapide (arbre libre + guide surligné) :</b>");
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("<i>Chargez un preset au lieu de tout choisir à la main. L'arbre reste accessible partout comme un PJ normal ; les prochaines étapes du build sont highlightées en <color=#FFD166><b>or</b></color> dans la Voûte et ci-dessous.</i>");

            var all = CharacterClassCatalog.GetAll();
            var active = CharacterClassCatalog.GetActiveGuide(_currentSheet);

            if (active != null)
            {
                int done = active.GetAcquiredCount(_currentSheet);
                int total = active.BuildPath != null ? active.BuildPath.Count : 0;
                var next = active.GetNextSteps(_currentSheet);
                string nextLabel = next.Count > 0 ? string.Join(" • ", next.GetRange(0, Math.Min(3, next.Count))) : "Build terminé ★";
                GUILayout.BeginHorizontal(GUI.skin.box);
                GUILayout.Label($"<b>{active.IconGlyph} Guide actif : {active.DisplayName}</b> ({done}/{total}) — <color=#FFD166>Suivant : {nextLabel}</color>", GUILayout.ExpandWidth(true));
                GUI.backgroundColor = new Color(0.1f, 0.75f, 1.0f);
                if (GUILayout.Button("🌌 Voir Voûte", GUILayout.Width(110), GUILayout.Height(26)))
                    SkillTreeCosmosWindow.OpenForCharacter(_currentSheet);
                GUI.backgroundColor = Color.white;
                if (GUILayout.Button("❌ Quitter guide", GUILayout.Width(110), GUILayout.Height(26)))
                {
                    CharacterClassCatalog.ClearGuide(_currentSheet);
                    SaveCharacterAndSyncUnits(_currentSheet);
                    _statusMessage = "Guide de build désactivé — arbre totalement libre, preset conservé sur la fiche.";
                }
                GUILayout.EndHorizontal();
            }

            // Grille 2 colonnes de presets.
            for (int i = 0; i < all.Count; i += 2)
            {
                GUILayout.BeginHorizontal();
                for (int k = 0; k < 2; k++)
                {
                    int idx = i + k;
                    if (idx >= all.Count)
                    {
                        GUILayout.FlexibleSpace();
                        break;
                    }
                    var def = all[idx];
                    bool isActive = active != null && active.ClassId == def.ClassId;
                    Color prevBg = GUI.backgroundColor;
                    GUI.backgroundColor = isActive ? new Color(0.98f, 0.75f, 0.2f) : new Color(0.16f, 0.22f, 0.32f);
                    GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true));
                    GUILayout.Label($"{def.IconGlyph} <b>{def.DisplayName}</b>{(isActive ? " <color=#FFD166>[GUIDE ACTIF]</color>" : "")}");
                    GUILayout.Label($"<color=#94A3B8><i>{def.Tagline}</i></color>");
                    GUILayout.Label($"<color=#94A3B8>{def.Description}</color>");
                    if (GUILayout.Button(isActive ? "↻ Recharger ce preset" : "📥 Charger ce preset", GUILayout.Height(26)))
                    {
                        string keepName = _currentSheet.Name;
                        if (def.ApplyToSheet(_currentSheet, out string msg))
                        {
                            // Le chargement ne doit pas écraser le nom choisi par le joueur.
                            if (!string.IsNullOrWhiteSpace(keepName) && keepName != "Nouveau Personnage")
                                _currentSheet.Name = keepName;
                            SaveCharacterAndSyncUnits(_currentSheet);
                            _statusMessage = msg;
                        }
                        else _statusMessage = msg;
                    }
                    GUILayout.EndVertical();
                    GUI.backgroundColor = prevBg;
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
        }

        private void DrawModelSelector()
        {
            if (_availableModelNames.Count == 0)
            {
                RefreshAvailableModels();
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Modèle 3D :", GUILayout.Width(90));

            int currentIndex = 0;
            if (!string.IsNullOrEmpty(_currentSheet.ModelPrefabName))
            {
                currentIndex = _availableModelNames.IndexOf(_currentSheet.ModelPrefabName);
                if (currentIndex < 0)
                {
                    _availableModelNames.Add(_currentSheet.ModelPrefabName);
                    currentIndex = _availableModelNames.IndexOf(_currentSheet.ModelPrefabName);
                }
            }

            if (GUILayout.Button("◀", GUILayout.Width(28)))
            {
                currentIndex = (currentIndex - 1 + _availableModelNames.Count) % _availableModelNames.Count;
                _currentSheet.ModelPrefabName = (currentIndex == 0) ? "" : _availableModelNames[currentIndex];
            }

            string currentDisplay = string.IsNullOrEmpty(_currentSheet.ModelPrefabName) 
                ? "📦 (Procédural / Défaut)" 
                : $"🤖 {_currentSheet.ModelPrefabName}";

            GUI.color = string.IsNullOrEmpty(_currentSheet.ModelPrefabName) ? Color.gray : Color.cyan;
            GUILayout.Label($"<b>{currentDisplay}</b>", GUILayout.Width(190));
            GUI.color = Color.white;

            if (GUILayout.Button("▶", GUILayout.Width(28)))
            {
                currentIndex = (currentIndex + 1) % _availableModelNames.Count;
                _currentSheet.ModelPrefabName = (currentIndex == 0) ? "" : _availableModelNames[currentIndex];
            }

            if (GUILayout.Button(_showModelDropdown ? "▲ Liste" : "▼ Liste", GUILayout.Width(58)))
            {
                _showModelDropdown = !_showModelDropdown;
            }

            if (GUILayout.Button("🔄 Scan", GUILayout.Width(58)))
            {
                RefreshAvailableModels();
                _statusMessage = $"Modèles scannés dans Resources/Characters ({_availableModelNames.Count - 1} trouvé(s)).";
            }
            GUILayout.EndHorizontal();

            if (_showModelDropdown)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label("<color=#52687A><i>Modèles disponibles sous Assets/Resources/Characters :</i></color>");
                for (int i = 0; i < _availableModelNames.Count; i++)
                {
                    string mName = _availableModelNames[i];
                    bool isSelected = (i == 0 && string.IsNullOrEmpty(_currentSheet.ModelPrefabName)) || (_currentSheet.ModelPrefabName == mName);

                    GUI.backgroundColor = isSelected ? new Color(0.1f, 0.6f, 0.9f) : Color.white;
                    string itemLabel = (i == 0) ? "📦 (Procédural / Défaut)" : $"🤖 {mName}";

                    if (GUILayout.Button(itemLabel))
                    {
                        _currentSheet.ModelPrefabName = (i == 0) ? "" : mName;
                        _showModelDropdown = false;
                    }
                    GUI.backgroundColor = Color.white;
                }
                GUILayout.EndVertical();
            }

            GUILayout.Space(6);

            // Hub Visuel : Aperçu 3D du modèle sélectionné & Contrôleur d'animations
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            DrawPreviewViewport(210, 210);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            DrawAnimationStateSelector();
        }

        private void EnsurePreviewStudio()
        {
            if (_previewRT == null)
            {
                _previewRT = new RenderTexture(256, 256, 16, RenderTextureFormat.ARGB32)
                {
                    name = "CharacterModel_PreviewRT"
                };
                _previewRT.Create();
            }

            if (_previewStudioRoot == null)
            {
                _previewStudioRoot = new GameObject("[Studio] CharacterModelPreview");
                _previewStudioRoot.transform.position = new Vector3(5000f, -5000f, 5000f);

                var camGo = new GameObject("PreviewCamera");
                camGo.transform.SetParent(_previewStudioRoot.transform, false);
                _previewCam = camGo.AddComponent<Camera>();
                _previewCam.clearFlags = CameraClearFlags.SolidColor;
                _previewCam.backgroundColor = new Color(0.02f, 0.04f, 0.07f, 1.0f);
                _previewCam.fieldOfView = 34f;
                _previewCam.nearClipPlane = 0.1f;
                _previewCam.farClipPlane = 50f;
                _previewCam.targetTexture = _previewRT;
                _previewCam.enabled = false;

                var lightGo = new GameObject("PreviewLight");
                lightGo.transform.SetParent(_previewStudioRoot.transform, false);
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.color = new Color(0.95f, 0.98f, 1.0f);
                light.intensity = 1.3f;
                lightGo.transform.rotation = Quaternion.Euler(30f, -35f, 0f);

                var rimLightGo = new GameObject("PreviewRimLight");
                rimLightGo.transform.SetParent(_previewStudioRoot.transform, false);
                var rimLight = rimLightGo.AddComponent<Light>();
                rimLight.type = LightType.Directional;
                rimLight.color = new Color(0.0f, 0.85f, 1.0f);
                rimLight.intensity = 0.65f;
                rimLightGo.transform.rotation = Quaternion.Euler(-25f, 145f, 0f);
            }
        }

        private void UpdatePreviewModel(string modelName)
        {
            string genderKey = ResolvePreviewGenderKey();
            if (modelName == _lastLoadedModelName && genderKey == _lastLoadedGenderKey && _currentPreviewInstance != null) return;

            bool modelChanged = modelName != _lastLoadedModelName;
            EnsurePreviewStudio();
            _lastLoadedModelName = modelName;
            _lastLoadedGenderKey = genderKey;

            // Même modèle mais genre différent -> pas besoin de ré-instancier,
            // on rejoue juste le clip genré.
            if (!modelChanged && _currentPreviewInstance != null)
            {
                PlayPreviewAnimation(_selectedPreviewAnimIndex);
                return;
            }

            if (_currentPreviewInstance != null)
            {
                Destroy(_currentPreviewInstance);
                _currentPreviewInstance = null;
            }

            GameObject prefab = null;

            if (!string.IsNullOrEmpty(modelName) && modelName != "(Procédural)")
            {
                string cleanName = modelName.StartsWith("Characters/") ? modelName.Substring("Characters/".Length) : modelName;
                prefab = Resources.Load<GameObject>($"Characters/{cleanName}") ?? Resources.Load<GameObject>(cleanName);

                if (prefab != null)
                {
                    _currentPreviewInstance = Instantiate(prefab, _previewStudioRoot.transform);
                    _currentPreviewInstance.name = "Preview_" + cleanName;

                    var colliders = _currentPreviewInstance.GetComponentsInChildren<Collider>();
                    for (int i = 0; i < colliders.Length; i++) colliders[i].enabled = false;
                }
            }

            if (_currentPreviewInstance == null)
            {
                _currentPreviewInstance = BuildProceduralPreviewModel();
            }

            _currentPreviewInstance.transform.localPosition = Vector3.zero;
            _currentPreviewInstance.transform.localRotation = Quaternion.Euler(0f, _previewModelYaw, 0f);

            // Même normalisation qu'en jeu : l'aperçu affiche la taille réelle de l'unité.
            CharacterModelScaleNormalizer.NormalizeToUnitHeight(_currentPreviewInstance, 1f);

            SetupPreviewAnimator(_currentPreviewInstance, prefab);
            PlayPreviewAnimation(_selectedPreviewAnimIndex);

            FramePreviewCamera();
        }

        private void DrawAnimationStateSelector()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Animation :", GUILayout.Width(80));

            if (GUILayout.Button("◀", GUILayout.Width(28)))
            {
                int next = (_selectedPreviewAnimIndex - 1 + _previewAnimationStates.Length) % _previewAnimationStates.Length;
                PlayPreviewAnimation(next);
            }

            string currentAnimName = _previewAnimationStates[_selectedPreviewAnimIndex];
            GUI.color = Color.cyan;
            GUILayout.Label($"<b>🎭 {currentAnimName}</b>", GUILayout.Width(200));
            GUI.color = Color.white;

            if (GUILayout.Button("▶", GUILayout.Width(28)))
            {
                int next = (_selectedPreviewAnimIndex + 1) % _previewAnimationStates.Length;
                PlayPreviewAnimation(next);
            }

            if (GUILayout.Button(_showAnimDropdown ? "▲ Liste" : "▼ Liste", GUILayout.Width(58)))
            {
                _showAnimDropdown = !_showAnimDropdown;
            }

            if (GUILayout.Button("↺", GUILayout.Width(30)))
            {
                PlayPreviewAnimation(_selectedPreviewAnimIndex);
            }
            GUILayout.EndHorizontal();

            if (_showAnimDropdown)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label("<color=#52687A><i>États d'animation disponibles (Assets/Resources/Animations) :</i></color>");
                for (int i = 0; i < _previewAnimationStates.Length; i++)
                {
                    bool isSelected = (_selectedPreviewAnimIndex == i);
                    GUI.backgroundColor = isSelected ? new Color(0.0f, 0.85f, 1.0f) : Color.white;
                    if (GUILayout.Button($"🎬 {_previewAnimationStates[i]}"))
                    {
                        PlayPreviewAnimation(i);
                        _showAnimDropdown = false;
                    }
                    GUI.backgroundColor = Color.white;
                }
                GUILayout.EndVertical();
            }
        }

        private void SetupPreviewAnimator(GameObject instance, GameObject sourcePrefab = null)
        {
            _previewAnimator = instance.GetComponentInChildren<Animator>();
            if (_previewAnimator == null)
            {
                _previewAnimator = instance.AddComponent<Animator>();
            }

            _previewAnimator.applyRootMotion = false;
            _previewAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            _previewAnimator.updateMode = AnimatorUpdateMode.UnscaledTime;

            if (_previewAnimator.avatar == null && sourcePrefab != null)
            {
                var srcAnim = sourcePrefab.GetComponentInChildren<Animator>();
                if (srcAnim != null && srcAnim.avatar != null)
                {
                    _previewAnimator.avatar = srcAnim.avatar;
                }
            }
        }

        private void PlayPreviewAnimation(int index)
        {
            if (index < 0 || index >= _previewAnimationStates.Length) return;
            _selectedPreviewAnimIndex = index;
            string stateName = _previewAnimationStates[index];

            if (_previewAnimator == null) return;

            if (_previewPlayableGraph.IsValid())
            {
                _previewPlayableGraph.Destroy();
            }

            AnimationClip clip = LoadAnimationClip(stateName, ResolvePreviewGenderKey());
            if (clip != null)
            {
                AnimationPlayableUtilities.PlayClip(_previewAnimator, clip, out _previewPlayableGraph);
                _previewPlayableGraph.SetTimeUpdateMode(DirectorUpdateMode.UnscaledGameTime);
                _previewPlayableGraph.Play();
            }
            else
            {
                _statusMessage = $"⚠️ Clip '{stateName}' introuvable dans Resources/Animations/{stateName}";
            }
        }

        private string ResolvePreviewGenderKey()
        {
            string gender = _currentSheet != null ? _currentSheet.Gender : null;
            if (!string.IsNullOrEmpty(gender))
            {
                string g = gender.Trim().ToLowerInvariant();
                if (g.StartsWith("m") || g.Contains("masc") || g.Contains("male") || g.Contains("homme"))
                    return "Male";
                if (g.StartsWith("f") || g.Contains("fem") || g.Contains("fém") || g.Contains("woman") || g.Contains("girl"))
                    return "Female";
            }

            string modelName = _currentSheet != null ? _currentSheet.ModelPrefabName : null;
            if (!string.IsNullOrEmpty(modelName))
            {
                string m = modelName.ToLowerInvariant();
                if (m.Contains("mina") || m.Contains("wife") || m.Contains("female") || m.Contains("femelle") || m.Contains("woman") || m.Contains("girl"))
                    return "Female";
                if (m.Contains("soldier") || m.Contains("hitman") || m.Contains("boss") || m.Contains("male") || m.Contains("homme") || m.Contains("thomas") || m.Contains("lucas"))
                    return "Male";
            }

            return "Female";
        }

        private static int GenderToIndex(string gender)
        {
            if (string.IsNullOrEmpty(gender)) return 2;
            string g = gender.Trim().ToLowerInvariant();
            if (g.StartsWith("m") || g.Contains("masc") || g.Contains("male") || g.Contains("homme"))
                return 0;
            if (g.StartsWith("f") || g.Contains("fem") || g.Contains("fém") || g.Contains("woman") || g.Contains("girl"))
                return 1;
            return 2;
        }

        private static AnimationClip LoadAnimationClip(string clipName)
        {
            return LoadAnimationClip(clipName, "Female");
        }

        private static AnimationClip LoadAnimationClip(string clipName, string genderKey)
        {
            AnimationClip clip = null;

            var candidatePaths = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrEmpty(genderKey))
                candidatePaths.Add($"Animations/{genderKey}/{clipName}");
            if (!string.Equals(genderKey, "Female", System.StringComparison.OrdinalIgnoreCase))
                candidatePaths.Add($"Animations/Female/{clipName}");
            candidatePaths.Add($"Animations/{clipName}");
            candidatePaths.Add(clipName);

            foreach (var path in candidatePaths)
            {
                var subClips = Resources.LoadAll<AnimationClip>(path);
                if (subClips != null && subClips.Length > 0)
                {
                    for (int i = 0; i < subClips.Length; i++)
                    {
                        if (subClips[i] != null && !subClips[i].name.StartsWith("__preview__"))
                        {
                            clip = subClips[i];
                            break;
                        }
                    }
                }
                if (clip != null) break;

                clip = Resources.Load<AnimationClip>(path);
                if (clip != null) break;
            }

            return clip;
        }

        private GameObject BuildProceduralPreviewModel()
        {
            var root = new GameObject("Preview_Procedural");
            root.transform.SetParent(_previewStudioRoot.transform, false);

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "BodyCapsule";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0, 1.0f, 0);
            body.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);
            var bCol = body.GetComponent<Collider>();
            if (bCol != null) Destroy(bCol);

            var visor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visor.name = "Visor";
            visor.transform.SetParent(root.transform, false);
            visor.transform.localPosition = new Vector3(0, 1.45f, 0.28f);
            visor.transform.localScale = new Vector3(0.45f, 0.15f, 0.25f);
            var vCol = visor.GetComponent<Collider>();
            if (vCol != null) Destroy(vCol);

            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "BaseRing";
            ring.transform.SetParent(root.transform, false);
            ring.transform.localPosition = new Vector3(0, 0.02f, 0);
            ring.transform.localScale = new Vector3(0.95f, 0.02f, 0.95f);
            var rCol = ring.GetComponent<Collider>();
            if (rCol != null) Destroy(rCol);

            return root;
        }

        private void FramePreviewCamera()
        {
            if (_previewCam == null || _currentPreviewInstance == null) return;

            var renderers = _currentPreviewInstance.GetComponentsInChildren<Renderer>();
            Bounds bounds;

            if (renderers.Length > 0)
            {
                bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }
            }
            else
            {
                bounds = new Bounds(_currentPreviewInstance.transform.position + Vector3.up * 1f, Vector3.one * 1.8f);
            }

            float maxDimension = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            maxDimension = Mathf.Max(maxDimension, 1.2f);

            float distance = (maxDimension * 0.5f) / Mathf.Tan(_previewCam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            distance *= 1.45f;

            Vector3 targetFocus = bounds.center;
            _previewCam.transform.position = targetFocus + new Vector3(0f, maxDimension * 0.15f, distance);
            _previewCam.transform.LookAt(targetFocus);
        }

        private void RenderPreviewStudio()
        {
            if (_previewCam == null || _currentPreviewInstance == null) return;

            if (!_isDraggingPreview)
            {
                _previewModelYaw = (_previewModelYaw + Time.unscaledDeltaTime * 28f) % 360f;
            }

            _currentPreviewInstance.transform.localRotation = Quaternion.Euler(0f, _previewModelYaw, 0f);
            _previewCam.Render();
        }

        private void DrawPreviewViewport(float width, float height)
        {
            Rect rect = GUILayoutUtility.GetRect(width, height);
            Color prevBg = GUI.backgroundColor;

            GUI.backgroundColor = new Color(0.01f, 0.03f, 0.06f, 0.95f);
            GUI.Box(rect, GUIContent.none);
            GUI.backgroundColor = prevBg;

            if (_previewRT != null)
            {
                GUI.DrawTexture(rect, _previewRT, ScaleMode.ScaleToFit);
            }

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
                    float deltaX = e.mousePosition.x - _lastMousePos.x;
                    _previewModelYaw -= deltaX * 1.4f;
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
            var footerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 9,
                alignment = TextAnchor.MiddleCenter
            };
            footerStyle.normal.textColor = new Color(0.0f, 0.9f, 1.0f, 0.7f);
            GUI.Label(labelRect, "↔ Glisser pour pivoter le modèle", footerStyle);
        }

        private void CleanupPreviewStudio()
        {
            if (_previewPlayableGraph.IsValid())
            {
                _previewPlayableGraph.Destroy();
            }

            _previewAnimator = null;

            if (_currentPreviewInstance != null)
            {
                Destroy(_currentPreviewInstance);
                _currentPreviewInstance = null;
            }

            if (_previewStudioRoot != null)
            {
                Destroy(_previewStudioRoot);
                _previewStudioRoot = null;
            }

            if (_previewRT != null)
            {
                _previewRT.Release();
                Destroy(_previewRT);
                _previewRT = null;
            }

            _lastLoadedModelName = "__UNINITIALIZED__";
            _lastLoadedGenderKey = "__UNINITIALIZED__";
        }

        private int DrawAttrRow(string label, int value, int attrIndex)
        {
            bool canDecrease = CanDecreaseAttribute(attrIndex);
            bool canIncrease = CanIncreaseAttribute(attrIndex);

            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(135));

            GUI.enabled = canDecrease;
            if (GUILayout.Button("-", GUILayout.Width(26), GUILayout.Height(20)))
            {
                value--;
            }
            GUI.enabled = true;

            GUILayout.Label($"<b>{value}</b>", GUILayout.Width(24));

            GUI.enabled = canIncrease;
            if (GUILayout.Button("+", GUILayout.Width(26), GUILayout.Height(20)))
            {
                value++;
            }
            GUI.enabled = true;

            GUILayout.EndHorizontal();
            return value;
        }

        private void DrawAttrXpUpgradeRow(string label, int attrIndex)
        {
            int currentBase = _currentSheet.GetBaseAttributeValue(attrIndex);
            int cost = CharacterProgressionManager.GetAttributeUpgradeCost(currentBase);
            int spendable = CharacterProgressionManager.GetSpendableForAttribute(_currentSheet, attrIndex);

            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(125));

            if (currentBase >= 10)
            {
                GUI.color = Color.green;
                GUILayout.Label("★ 10 (Plafond)", GUILayout.ExpandWidth(true));
                GUI.color = Color.white;
            }
            else
            {
                GUILayout.Label($"<color=#38BDF8>{currentBase} ➔ {currentBase + 1}</color>", GUILayout.Width(52));

                bool canAfford = spendable >= cost;
                GUI.enabled = canAfford;
                GUI.backgroundColor = canAfford ? new Color(0.2f, 0.85f, 0.45f) : Color.gray;

                if (GUILayout.Button($"Acheter ({cost} XP)", GUILayout.Height(20), GUILayout.ExpandWidth(true)))
                {
                    if (CharacterProgressionManager.UpgradeAttribute(_currentSheet, attrIndex, out string msg))
                    {
                        SaveCharacterAndSyncUnits(_currentSheet);
                        _statusMessage = msg;
                    }
                    else
                    {
                        _statusMessage = msg;
                    }
                }

                GUI.backgroundColor = Color.white;
                GUI.enabled = true;
            }

            GUILayout.EndHorizontal();
        }

        private bool CanDecreaseAttribute(int attrIndex)
        {
            if (attrIndex == 8)
            {
                return _currentSheet.BaseAttributes.Magie > 0;
            }

            var b = _currentSheet.BaseAttributes;
            int[] core = { b.Force, b.Agilite, b.Constitution, b.Rapidite, b.Intelligence, b.Erudition, b.Charisme, b.Instinct };
            if (attrIndex >= 0 && attrIndex < 8)
            {
                return core[attrIndex] > 1;
            }

            return false;
        }

        private bool CanIncreaseAttribute(int attrIndex)
        {
            var b = _currentSheet.BaseAttributes;
            int[] core = { b.Force, b.Agilite, b.Constitution, b.Rapidite, b.Intelligence, b.Erudition, b.Charisme, b.Instinct };

            if (attrIndex == 8)
            {
                if (b.Magie >= 10) return false;
                if (_currentSheet.Profile == CharacterProfileType.PnjSbire && b.Magie >= 3) return false;
                if (_currentSheet.Profile == CharacterProfileType.HerosPJ && b.Magie >= 5) return false;
                if (_currentSheet.Profile == CharacterProfileType.PnjNormal && b.Magie >= 4) return false;
                return true;
            }

            if (attrIndex < 0 || attrIndex >= 8) return false;
            if (core[attrIndex] >= 10) return false;

            int[] candidate = (int[])core.Clone();
            candidate[attrIndex]++;

            return IsCandidateAllocationValid(candidate, _currentSheet.Profile, b.Magie);
        }

        private bool IsCandidateAllocationValid(int[] candidate, CharacterProfileType profile, int magie)
        {
            for (int i = 0; i < 8; i++)
            {
                if (candidate[i] > 10 || candidate[i] < 1) return false;
            }

            if (profile == CharacterProfileType.PnjBoss)
            {
                return true;
            }

            if (profile == CharacterProfileType.PnjSbire)
            {
                for (int i = 0; i < 8; i++)
                {
                    if (candidate[i] > 3) return false;
                }

                int maxSbirePoints = magie > 0 ? 11 : 12;
                int currentTotal = 0;
                for (int i = 0; i < 8; i++) currentTotal += candidate[i];

                return currentTotal <= maxSbirePoints;
            }

            if (profile == CharacterProfileType.HerosPJ)
            {
                for (int i = 0; i < 8; i++)
                {
                    if (candidate[i] > 5) return false;
                }

                int count5 = 0;
                int countGte4 = 0;
                int count4 = 0;
                for (int i = 0; i < 8; i++)
                {
                    if (candidate[i] == 5) count5++;
                    if (candidate[i] >= 4) countGte4++;
                    if (candidate[i] == 4) count4++;
                }

                if (count5 > 1) return false;
                if (countGte4 > 2) return false;
                if (count5 == 1 && count4 > 1) return false;

                int maxTotal = magie > 0 ? 26 : 24;
                int sum = 0;
                for (int i = 0; i < 8; i++) sum += candidate[i];
                if (sum > maxTotal) return false;

                int[] sorted = (int[])candidate.Clone();
                Array.Sort(sorted);
                Array.Reverse(sorted);

                int secondarySum = 0;
                for (int i = 2; i < 8; i++)
                {
                    if (sorted[i] > 3) return false;
                    secondarySum += sorted[i];
                }

                int maxSecondary = magie > 0 ? 17 : 15;
                if (secondarySum > maxSecondary) return false;

                return true;
            }

            if (profile == CharacterProfileType.PnjNormal)
            {
                for (int i = 0; i < 8; i++)
                {
                    if (candidate[i] > 4) return false;
                }

                int count4 = 0;
                for (int i = 0; i < 8; i++)
                {
                    if (candidate[i] == 4) count4++;
                }
                if (count4 > 2) return false;

                int[] sorted = (int[])candidate.Clone();
                Array.Sort(sorted);
                Array.Reverse(sorted);

                int secondarySum = 0;
                for (int i = 2; i < 8; i++)
                {
                    if (sorted[i] > 3) return false;
                    secondarySum += sorted[i];
                }

                int maxSecondary = magie > 0 ? 17 : 15;
                if (secondarySum > maxSecondary) return false;

                int maxTotal = magie > 0 ? 25 : 23;
                int sum = 0;
                for (int i = 0; i < 8; i++) sum += candidate[i];
                if (sum > maxTotal) return false;

                return true;
            }

            return false;
        }

        private void ApplyDefaultProfileAttributes(CharacterSheet sheet)
        {
            switch (sheet.Profile)
            {
                case CharacterProfileType.HerosPJ:
                    sheet.BaseAttributes.Force = 5;
                    sheet.BaseAttributes.Constitution = 4;
                    sheet.BaseAttributes.Agilite = 3;
                    sheet.BaseAttributes.Rapidite = 3;
                    sheet.BaseAttributes.Intelligence = 2;
                    sheet.BaseAttributes.Erudition = 3;
                    sheet.BaseAttributes.Charisme = 1;
                    sheet.BaseAttributes.Instinct = 3;
                    sheet.BaseAttributes.Magie = 0;
                    break;
                case CharacterProfileType.PnjNormal:
                    sheet.BaseAttributes.Force = 4;
                    sheet.BaseAttributes.Constitution = 4;
                    sheet.BaseAttributes.Agilite = 3;
                    sheet.BaseAttributes.Rapidite = 3;
                    sheet.BaseAttributes.Intelligence = 2;
                    sheet.BaseAttributes.Erudition = 3;
                    sheet.BaseAttributes.Charisme = 1;
                    sheet.BaseAttributes.Instinct = 3;
                    sheet.BaseAttributes.Magie = 0;
                    break;
                case CharacterProfileType.PnjSbire:
                    sheet.BaseAttributes.Force = 2;
                    sheet.BaseAttributes.Agilite = 2;
                    sheet.BaseAttributes.Constitution = 2;
                    sheet.BaseAttributes.Rapidite = 2;
                    sheet.BaseAttributes.Intelligence = 1;
                    sheet.BaseAttributes.Erudition = 1;
                    sheet.BaseAttributes.Charisme = 1;
                    sheet.BaseAttributes.Instinct = 1;
                    sheet.BaseAttributes.Magie = 0;
                    break;
                case CharacterProfileType.PnjBoss:
                    sheet.BaseAttributes.Force = 7;
                    sheet.BaseAttributes.Constitution = 6;
                    sheet.BaseAttributes.Agilite = 5;
                    sheet.BaseAttributes.Rapidite = 4;
                    sheet.BaseAttributes.Intelligence = 4;
                    sheet.BaseAttributes.Erudition = 2;
                    sheet.BaseAttributes.Charisme = 2;
                    sheet.BaseAttributes.Instinct = 5;
                    sheet.BaseAttributes.Magie = 0;
                    break;
            }
        }

        private static int CountTrainedLevels(CharacterSheet sheet)
        {
            if (sheet == null || sheet.Skills == null) return 0;
            int total = 0;
            for (int i = 0; i < sheet.Skills.Count; i++)
                if (sheet.Skills[i] != null) total += System.Math.Max(0, sheet.Skills[i].TrainingLevel);
            return total;
        }

        private bool ValidateAttributeAllocation(CharacterSheet sheet, out string statusMessage)
        {
            var b = sheet.BaseAttributes;
            int[] core = { b.Force, b.Agilite, b.Constitution, b.Rapidite, b.Intelligence, b.Erudition, b.Charisme, b.Instinct };

            for (int i = 0; i < core.Length; i++)
            {
                if (core[i] < 1)
                {
                    statusMessage = "❌ Attribut < 1 interdit (Plancher vital : 1)";
                    return false;
                }
                if (core[i] > 10)
                {
                    statusMessage = "❌ Attribut > 10 interdit (Plafond absolu : 10)";
                    return false;
                }
            }

            int totalPurchased = 0;
            if (sheet.AttributeUpgradesPurchased != null)
            {
                for (int p = 0; p < sheet.AttributeUpgradesPurchased.Length; p++)
                    totalPurchased += Math.Max(0, sheet.AttributeUpgradesPurchased[p]);
            }
            if (totalPurchased > 0)
            {
                statusMessage = $"⭐ Profil Évolué (+{totalPurchased} pts achetés en XP)";
                return true;
            }

            if (sheet.Profile == CharacterProfileType.PnjBoss)
            {
                statusMessage = "👑 Boss : Attribution Libre (Plafond 10 respecté)";
                return true;
            }

            if (sheet.Profile == CharacterProfileType.PnjSbire)
            {
                int total = 0;
                for (int i = 0; i < core.Length; i++)
                {
                    if (core[i] > 3)
                    {
                        statusMessage = $"❌ Sbire : Attribut ({core[i]}) > 3 interdit";
                        return false;
                    }
                    total += core[i];
                }

                int expected = b.Magie > 0 ? 11 : 12;
                if (total == expected)
                {
                    statusMessage = $"✅ Sbire : {total}/{expected} pts alloués";
                    return true;
                }

                statusMessage = $"⚠️ Sbire : {total}/{expected} pts (Requis exact : {expected})";
                return false;
            }

            int count5 = 0;
            int count4 = 0;
            int secondarySum = 0;
            bool remainingOver3 = false;

            for (int i = 0; i < core.Length; i++)
            {
                if (sheet.Profile == CharacterProfileType.HerosPJ && core[i] == 5 && count5 == 0)
                {
                    count5++;
                }
                else if (core[i] == 4 && ((sheet.Profile == CharacterProfileType.HerosPJ && count4 == 0) || (sheet.Profile == CharacterProfileType.PnjNormal && count4 < 2)))
                {
                    count4++;
                }
                else
                {
                    if (core[i] > 3) remainingOver3 = true;
                    secondarySum += core[i];
                }
            }

            int targetSecondary = b.Magie > 0 ? 17 : 15;

            if (sheet.Profile == CharacterProfileType.HerosPJ)
            {
                if (count5 != 1 || count4 != 1)
                {
                    statusMessage = $"⚠️ Héros : Requis 1 pilier à 5 ({count5}/1) & 1 à 4 ({count4}/1)";
                    return false;
                }
                if (remainingOver3)
                {
                    statusMessage = "❌ Héros : Les 6 attributs restants doivent être &le; 3";
                    return false;
                }
                if (secondarySum != targetSecondary)
                {
                    statusMessage = $"⚠️ Héros : {secondarySum}/{targetSecondary} pts secondaires";
                    return false;
                }
                statusMessage = $"✅ Héros : Conforme Codex ({targetSecondary} pts)";
                return true;
            }

            if (sheet.Profile == CharacterProfileType.PnjNormal)
            {
                if (count4 != 2)
                {
                    statusMessage = $"⚠️ Soldat : Requis 2 piliers à 4 ({count4}/2)";
                    return false;
                }
                if (remainingOver3)
                {
                    statusMessage = "❌ Soldat : Les 6 attributs restants doivent être &le; 3";
                    return false;
                }
                if (secondarySum != targetSecondary)
                {
                    statusMessage = $"⚠️ Soldat : {secondarySum}/{targetSecondary} pts secondaires";
                    return false;
                }
                statusMessage = $"✅ Soldat : Conforme Codex ({targetSecondary} pts)";
                return true;
            }

            statusMessage = "Statut indéterminé";
            return false;
        }

        // ================= TAB 1 : PROGRESSION =================
        private void DrawProgressionTab()
        {
            CharacterProgressionManager.SynchronizeProgression(_currentSheet);

            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.Label($"Personnage : <b>{_currentSheet.Name}</b> | XP Libre : <color=yellow><b>{_currentSheet.AvailableXP} XP</b></color> | Banque liée : <b>{_currentSheet.GetLinkedReserveTotal()} XP</b> | <color=#FFD166>XP TOTAL (dépensé) : <b>{_currentSheet.TotalSpentXP} XP</b></color>");
            if (GUILayout.Button("+10 XP", GUILayout.Width(70))) CharacterProgressionManager.GrantXP(_currentSheet, 10);
            if (GUILayout.Button("+50 XP", GUILayout.Width(70))) CharacterProgressionManager.GrantXP(_currentSheet, 50);
            GUI.enabled = _currentSheet.AvailableXP > 0;
            if (GUILayout.Button("↺ XP libre à 0", GUILayout.Width(110)))
            {
                if (CharacterProgressionManager.ResetFreeXP(_currentSheet, out string freeMsg, out _))
                    _statusMessage = freeMsg;
                else
                    _statusMessage = freeMsg;
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.Label($"<color=#94A3B8><size=11>📊 <b>Ventilation XP Total :</b> {CharacterProgressionManager.GetSpentXPBreakdownString(_currentSheet)}</size></color>");
            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.Label("<i>Reset : restaure l'XP payé en libre, gratuits rendus au budget, entraînements à 0, cases effacées.</i>", GUILayout.ExpandWidth(true));
            GUI.backgroundColor = new Color(0.95f, 0.45f, 0.2f);
            if (GUILayout.Button("♻️ Reset arbre", GUILayout.Width(130)))
            {
                if (CharacterProgressionManager.ResetProgressionTree(_currentSheet, out string resetMsg))
                    _statusMessage = resetMsg;
                else
                    _statusMessage = resetMsg;
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();
            GUILayout.Label($"<i>Gratuits Érudition : <b>{_currentSheet.GetFreeTrainingsRemaining()}</b> restant(s) (budget <b>{_currentSheet.GetFreeTrainingBudget()}</b> = ÉRU effective, {Mathf.Max(0, _currentSheet.FreeTrainingsUsed)} consommé(s)). +1 ÉRU = +1 gratuit. Gratuits : 0 XP, hors XP total.</i>");
            GUILayout.Label("<i>Livre I §5 : 1 réussite = 1 case dans la compétence. Piste pleine (cases = base) = +1 XP lié. Dépense associée/libre au coût normal, croisée x2. XP total = dépensé (payé), jamais la banque ni les gratuits.</i>");

            GUILayout.Space(4);
            GUI.backgroundColor = new Color(0.1f, 0.75f, 1.0f);
            if (GUILayout.Button("🌌 OUVRIR LA VOÛTE CÉLESTE (Arbre Cosmique 3D / Constellations)", GUILayout.Height(30)))
            {
                SkillTreeCosmosWindow.OpenForCharacter(_currentSheet);
            }
            GUI.backgroundColor = Color.white;

            GUILayout.Space(6);
            GUILayout.Label("<b>Arbres de Compétences (5 XP liés/libre = +1 Palier, 10 si croisé, ou Gratuit ÉRU) :</b>");

            var activeGuide = CharacterClassCatalog.GetActiveGuide(_currentSheet);
            if (activeGuide != null)
            {
                int doneG = activeGuide.GetAcquiredCount(_currentSheet);
                int totalG = activeGuide.BuildPath != null ? activeGuide.BuildPath.Count : 0;
                var nextG = activeGuide.GetNextSteps(_currentSheet);
                string nextGLabel = nextG.Count > 0 ? string.Join(" • ", nextG.GetRange(0, Math.Min(3, nextG.Count))) : "Build terminé ★";
                GUILayout.BeginHorizontal(GUI.skin.box);
                GUILayout.Label($"{activeGuide.IconGlyph} <b>Guide : {activeGuide.DisplayName}</b> ({doneG}/{totalG}) — <color=#FFD166>Suivant : {nextGLabel}</color>", GUILayout.ExpandWidth(true));
                if (GUILayout.Button("❌ Quitter guide", GUILayout.Width(120)))
                {
                    CharacterClassCatalog.ClearGuide(_currentSheet);
                    SaveCharacterAndSyncUnits(_currentSheet);
                    _statusMessage = "Guide de build désactivé — arbre totalement libre.";
                }
                GUILayout.EndHorizontal();
            }

            foreach (var entry in _currentSheet.Skills)
            {
                bool isAccessible = CharacterProgressionManager.IsSkillAccessible(_currentSheet, entry.Skill);
                if (!isAccessible)
                {
                    GUILayout.BeginHorizontal(GUI.skin.box);
                    GUILayout.Label($"<b>{SkillDefinitions.GetDisplayName(entry.Skill)}</b>", GUILayout.Width(200));
                    GUI.color = new Color(0.95f, 0.4f, 0.4f);
                    // Libellé héroïque délégué (voir MinaCharacter / LucasCharacter / ThomasCharacter).
                    string lockLabel = MinaCharacter.IsMina(_currentSheet)
                        ? MinaCharacter.RestrictionLabel
                        : (LucasCharacter.IsLucas(_currentSheet) 
                            ? LucasCharacter.RestrictionLabel 
                            : (ThomasCharacter.IsThomas(_currentSheet) 
                                ? ThomasCharacter.RestrictionLabel 
                                : "🔒 Inaccessible (Affinité héroïque)"));
                    GUILayout.Label(lockLabel, GUILayout.ExpandWidth(true));
                    GUI.color = Color.white;
                    GUILayout.EndHorizontal();
                    continue;
                }

                GUILayout.BeginHorizontal(GUI.skin.box);
                string skillName = SkillDefinitions.GetDisplayName(entry.Skill);
                bool isMax = entry.TrainingLevel >= CharacterProgressionManager.MAX_SKILL_TRAINING;
                int baseRank = CharacterProgressionManager.GetBaseRankForSheet(_currentSheet, entry.Skill, false);
                int maxTicks = CharacterProgressionManager.GetProgressMax(baseRank);
                int spendable = CharacterProgressionManager.GetSpendableFor(_currentSheet, entry.Skill);
                bool isSkillNext = activeGuide != null && activeGuide.IsSkillNext(_currentSheet, entry.Skill);
                string skillGuideTag = isSkillNext ? $" <color=#FFD166><b>◆ BUILD +{activeGuide.GetTargetTraining(entry.Skill)}</b></color>" : "";

                GUILayout.Label($"<b>{skillName}</b> (+{entry.TrainingLevel}/{CharacterProgressionManager.MAX_SKILL_TRAINING}) | Base {baseRank} | <color=#00E5FF>{entry.ProgressTicks}/{maxTicks}</color> | Banque <color=yellow>{entry.ReserveXP}</color>{skillGuideTag}", GUILayout.Width(340));

                if (isMax)
                {
                    GUI.color = Color.green;
                    GUILayout.Label("✅ Plafond Max (+3)", GUILayout.Width(280));
                    GUI.color = Color.white;
                }
                else
                {
                    bool hasFree = _currentSheet.GetFreeTrainingsRemaining() > 0;
                    GUI.enabled = hasFree;
                    GUI.backgroundColor = hasFree ? new Color(0.35f, 0.85f, 0.45f) : Color.white;
                    if (GUILayout.Button("Gratuit (ÉRU)", GUILayout.Width(110)))
                    {
                        if (CharacterProgressionManager.TrainSkillFree(_currentSheet, entry.Skill, out string freeMsg))
                            _statusMessage = freeMsg;
                        else
                            _statusMessage = freeMsg;
                    }
                    GUI.backgroundColor = Color.white;
                    GUI.enabled = true;

                    bool canAfford = spendable >= CharacterProgressionManager.XP_COST_TRAINING;
                    GUI.enabled = canAfford;
                    if (GUILayout.Button($"Entraîner ({CharacterProgressionManager.XP_COST_TRAINING} XP)", GUILayout.Width(160)))
                    {
                        if (CharacterProgressionManager.TrainSkill(_currentSheet, entry.Skill, out string msg))
                            _statusMessage = msg;
                        else
                            _statusMessage = msg;
                    }
                    GUI.enabled = true;
                }

                GUILayout.EndHorizontal();
            }

            GUILayout.Space(8);

            // 1. Barre de contrôle Dev & Cheat
            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.Label("<b>Maîtrises & Spécialisations du Codex :</b>", GUILayout.Width(260));

            Color prevCheatBg = GUI.backgroundColor;
            GUI.backgroundColor = _showHiddenModelCheats ? new Color(1f, 0.25f, 0.85f) : new Color(0.18f, 0.25f, 0.35f);
            if (GUILayout.Button(_showHiddenModelCheats ? "👁️ [DEV CHEAT] Secrets : VISIBLES" : "👁️ [DEV CHEAT] Secrets : CACHÉS", GUILayout.Width(240), GUILayout.Height(24)))
            {
                _showHiddenModelCheats = !_showHiddenModelCheats;
                _statusMessage = _showHiddenModelCheats
                    ? "Mode Dev Cheat activé : Toutes les arborescences et maîtrises secrètes sont affichées."
                    : "Mode standard restauré : Les maîtrises secrètes respectent leurs prérequis.";
            }
            GUI.backgroundColor = prevCheatBg;

            if (_showHiddenModelCheats)
            {
                GUI.backgroundColor = new Color(1f, 0.2f, 0.8f);
                if (GUILayout.Button("⚡ Tout Débloquer", GUILayout.Width(140), GUILayout.Height(24)))
                {
                    int unlockedCount = 0;
                    foreach (var specDetail in CharacterProgressionManager.GetAllSpecializations())
                    {
                        if (!_currentSheet.UnlockedSpecializations.Contains(specDetail.Name))
                        {
                            CharacterProgressionManager.UnlockSpecialization(_currentSheet, specDetail.Name, out _, forceFree: true);
                            unlockedCount++;
                        }
                    }
                    SaveCharacterAndSyncUnits(_currentSheet);
                    _statusMessage = $"[CHEAT] {unlockedCount} spécialisation(s) et maîtrise(s) débloquées instantanément !";
                }
                GUI.backgroundColor = Color.white;
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            // 2. Rendu complet de toutes les arborescences avec sous-branches et hiérarchie (filtré par fiche héroïque ou profil par défaut)
            var allSpecs = new List<CharacterProgressionManager.SpecializationDetail>(CharacterProgressionManager.GetAllSpecializations(_currentSheet));
            allSpecs.Sort((a, b) =>
            {
                int skillCmp = a.SourceSkill.CompareTo(b.SourceSkill);
                if (skillCmp != 0) return skillCmp;
                if (!a.IsImprovement && b.IsImprovement) return -1;
                if (a.IsImprovement && !b.IsImprovement) return 1;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });

            SkillType lastSkill = (SkillType)(-1);
            for (int sIdx = 0; sIdx < allSpecs.Count; sIdx++)
            {
                var detail = allSpecs[sIdx];
                if (detail == null) continue;

                if (detail.IsHidden && !_showHiddenModelCheats && !_currentSheet.UnlockedSpecializations.Contains(detail.Name))
                {
                    bool parentOwned = !string.IsNullOrEmpty(detail.ParentSpecialization) && _currentSheet.UnlockedSpecializations.Contains(detail.ParentSpecialization);
                    if (!parentOwned) continue;
                }

                bool isVol2Spec = detail.IsVolume2 || CharacterProgressionManager.IsVolume2Specialization(detail.Name);
                bool isVol2Locked = isVol2Spec && !CharacterProgressionManager.IsVolume2Unlocked();
                if (isVol2Locked && !_showHiddenModelCheats && !_currentSheet.UnlockedSpecializations.Contains(detail.Name))
                {
                    continue;
                }

                if (detail.SourceSkill != lastSkill)
                {
                    lastSkill = detail.SourceSkill;
                    GUILayout.Space(4);
                    string skillHeader = SkillDefinitions.GetDisplayName(lastSkill).ToUpperInvariant();
                    GUILayout.Label($"<color=#00E5FF><b>━━━ {skillHeader} ━━━</b></color>");
                }

                bool owned = _currentSheet.UnlockedSpecializations.Contains(detail.Name);
                bool isSecret = detail.IsHidden;
                bool parentMet = string.IsNullOrEmpty(detail.ParentSpecialization) || _currentSheet.UnlockedSpecializations.Contains(detail.ParentSpecialization);

                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.BeginHorizontal();

                string indent = detail.IsImprovement ? "   ↳ " : "★ ";
                if (isSecret) indent = detail.IsImprovement ? "   ↳ 🔮 " : "🔮 ";

                string nameColor = owned ? "#00FF88" : (isSecret ? "#F43F5E" : (detail.IsImprovement ? "#E2E8F0" : "#FFE600"));
                string secretTag = isSecret ? " <color=#F43F5E>[SECRET]</color>" : "";
                // Highlight guide de build : NEXT = or fort, FUTURE = or discret.
                string guideTag = "";
                if (activeGuide != null && !owned)
                {
                    var st = activeGuide.GetSpecState(_currentSheet, detail.Name);
                    if (st == CharacterClassDefinition.GuideStepState.Next)
                        guideTag = " <color=#FFD166><b>◆ SUIVANT BUILD</b></color>";
                    else if (st == CharacterClassDefinition.GuideStepState.Future)
                        guideTag = " <color=#8A6D2B>◇ build</color>";
                }
                else if (activeGuide != null && owned && activeGuide.BuildPath != null && activeGuide.BuildPath.Contains(detail.Name))
                {
                    guideTag = " <color=#00FF88>★ build</color>";
                }
                GUILayout.Label($"{indent}<b><color={nameColor}>{detail.Name}</color></b>{secretTag}{guideTag}", GUILayout.ExpandWidth(true));

                if (owned)
                {
                    GUI.color = Color.green;
                    GUILayout.Label("✅ Acquis", GUILayout.Width(75));
                    GUI.color = Color.white;
                }
                else if (isVol2Locked && !_showHiddenModelCheats)
                {
                    GUI.color = new Color(1f, 0.45f, 0.2f);
                    GUILayout.Label("🔒 Phase 2 / Vol. II Requis", GUILayout.Width(190));
                    GUI.color = Color.white;
                }
                else if (!parentMet && !_showHiddenModelCheats)
                {
                    GUI.color = new Color(0.6f, 0.6f, 0.6f);
                    GUILayout.Label($"🔒 Req: {detail.ParentSpecialization}", GUILayout.Width(190));
                    GUI.color = Color.white;
                }
                else
                {
                    if (!isVol2Locked)
                    {
                        bool canAfford = CharacterProgressionManager.GetSpendableFor(_currentSheet, detail.SourceSkill) >= CharacterProgressionManager.XP_COST_SPECIALIZATION;
                        GUI.enabled = canAfford;
                        GUI.backgroundColor = canAfford ? new Color(0.2f, 0.8f, 0.4f) : Color.gray;

                        if (GUILayout.Button("Débloquer (5 XP)", GUILayout.Width(130)))
                        {
                            if (CharacterProgressionManager.UnlockSpecialization(_currentSheet, detail.Name, out string msg, forceFree: false))
                            {
                                SaveCharacterAndSyncUnits(_currentSheet);
                                _statusMessage = msg;
                            }
                            else
                            {
                                _statusMessage = msg;
                            }
                        }
                        GUI.backgroundColor = Color.white;
                        GUI.enabled = true;
                    }
                    else
                    {
                        GUI.color = new Color(1f, 0.45f, 0.2f);
                        GUILayout.Label("🔒 Verrou Causal (Vol. II)", GUILayout.Width(170));
                        GUI.color = Color.white;
                    }

                    if (_showHiddenModelCheats)
                    {
                        GUI.backgroundColor = new Color(1f, 0.2f, 0.8f);
                        if (GUILayout.Button("⚡ Cheat", GUILayout.Width(65)))
                        {
                            CharacterProgressionManager.UnlockSpecialization(_currentSheet, detail.Name, out string cheatMsg, forceFree: true);
                            SaveCharacterAndSyncUnits(_currentSheet);
                            _statusMessage = cheatMsg;
                        }
                        GUI.backgroundColor = Color.white;
                    }
                }

                GUILayout.EndHorizontal();

                if (!string.IsNullOrEmpty(detail.Description) || !string.IsNullOrEmpty(detail.MechanicalEffect))
                {
                    string effectText = !string.IsNullOrEmpty(detail.MechanicalEffect) ? $" — <i>Effet : {detail.MechanicalEffect}</i>" : "";
                    GUILayout.Label($"<color=#94A3B8>{detail.Description}{effectText}</color>");
                }

                GUILayout.EndVertical();
            }
        }

        // ================= TAB 3 : FORGE DE SORTS (LIVRE IV) =================
        private void DrawSpellForgeTab()
        {
            GUILayout.Label("<b>1. Presets Officiels du Codex (Livre IV §19.3) :</b>");
            GUILayout.BeginHorizontal(GUI.skin.box);
            if (GUILayout.Button("📖 Charger 'Ralentissement Gravitationnel' (3 XP / 3 PA)"))
            {
                var preset = ArcanotechWorkshop.CreateRalentissementGravitationnel();
                LoadSpellIntoForge(preset);
                _statusMessage = "Preset 'Ralentissement Gravitationnel' chargé dans l'Atelier.";
            }
            if (GUILayout.Button("📖 Charger 'Écrasement de Matière' (3 XP / 3 PA)"))
            {
                var preset = ArcanotechWorkshop.CreateEcrasementDeMatiere();
                LoadSpellIntoForge(preset);
                _statusMessage = "Preset 'Écrasement de Matière' chargé dans l'Atelier.";
            }
            if (GUILayout.Button("↺ Vider", GUILayout.Width(70)))
            {
                _spellModuleRanks.Clear();
                _customSpellName = "Nouveau Pouvoir";
                _customSpellDesc = "";
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label("<b>2. Fiche Technique du Pouvoir (Livre IV §19.1) :</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Nom :", GUILayout.Width(100));
            _customSpellName = GUILayout.TextField(_customSpellName);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Description :", GUILayout.Width(100));
            _customSpellDesc = GUILayout.TextField(_customSpellDesc);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Discipline :", GUILayout.Width(100));
            _customSpellDiscipline = (PsychicDiscipline)GUILayout.Toolbar((int)_customSpellDiscipline, Enum.GetNames(typeof(PsychicDiscipline)));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Stade Neuronal :", GUILayout.Width(100));
            int stageIdx = (int)_customSpellStage - 1;
            int newStageIdx = GUILayout.Toolbar(stageIdx, new[] { "Stade 1", "Stade 2", "Stade 3", "Stade 4", "Stade 5", "Stade 6" });
            _customSpellStage = (PsychicStage)(newStageIdx + 1);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Compétence :", GUILayout.Width(100));
            int skillIdx = _customSpellSkill switch
            {
                SkillType.MagieElementale => 0,
                SkillType.MagiePrimale => 1,
                _ => 2
            };
            int newSkillIdx = GUILayout.Toolbar(skillIdx, new[] { "Magie Élémentale", "Magie Primale", "Magie de l'Esprit" });
            _customSpellSkill = newSkillIdx switch
            {
                0 => SkillType.MagieElementale,
                1 => SkillType.MagiePrimale,
                _ => SkillType.MagieEsprit
            };
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();

            var candidateSpell = BuildCandidateSpell();
            var activeCats = candidateSpell.GetActiveCategories();
            string catList = activeCats.Count > 0 ? string.Join(", ", activeCats) : "Aucune";

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>3. Catalogue Modulaire des Effets Achetables (Livre IV §19.2) :</b>");
            GUILayout.FlexibleSpace();
            GUILayout.Label($"Solde : <color=yellow><b>{_currentSheet.AvailableXP} XP Libre</b></color>");
            GUILayout.EndHorizontal();

            _spellForgeModulesScroll = GUILayout.BeginScrollView(_spellForgeModulesScroll, GUILayout.Height(210));
            DrawModuleCatalogGroup(PowerCategory.Offensif, "━━━ EFFETS OFFENSIFS (Attaques & Neutralisation) ━━━", "#FF4444");
            DrawModuleCatalogGroup(PowerCategory.Defensif, "━━━ EFFETS DÉFENSIFS (Protection & Résistance) ━━━", "#4488FF");
            DrawModuleCatalogGroup(PowerCategory.Utilitaire, "━━━ EFFETS UTILITAIRES & ÉTATS BÉNÉFIQUES ━━━", "#00FFAA");
            GUILayout.EndScrollView();

            GUILayout.Space(6);
            GUILayout.Label("<b>4. Bilan Énergétique & Règle d'Or (XP = PA) :</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.BeginHorizontal();
            GUI.color = Color.cyan;
            GUILayout.Label($"⚡ Coût d'Activation Combat : <b>{candidateSpell.ActionPointCost} PA</b>", GUILayout.Width(240));
            GUI.color = Color.yellow;
            GUILayout.Label($"🔮 Coût de Création : <b>{candidateSpell.CreationXpCost} XP</b>", GUILayout.Width(220));
            GUI.color = candidateSpell.HybridXpPenalty > 0 ? new Color(1f, 0.4f, 0.4f) : Color.gray;
            GUILayout.Label($"Pénalité Hybride : <b>+{candidateSpell.HybridXpPenalty} XP</b> ({activeCats.Count} cat.)", GUILayout.ExpandWidth(true));
            GUI.color = Color.white;
            GUILayout.EndHorizontal();

            int magicAttr = _currentSheet.BaseAttributes.Magie;
            int training = _currentSheet.GetSkill(_customSpellSkill)?.TrainingLevel ?? 0;
            int baseRange = NythariteSpell.CalculateBaseRange(magicAttr, training);
            GUILayout.Label($"<color=#94A3B8>Portée nominale : <b>{baseRange} cases</b> (MAG {magicAttr} + Entraînement {training}) | Catégories : <b>{catList}</b></color>");

            GUILayout.Space(4);
            bool canAfford = _currentSheet.AvailableXP >= candidateSpell.CreationXpCost && candidateSpell.ActionPointCost > 0;
            GUI.enabled = canAfford;
            GUI.backgroundColor = canAfford ? new Color(0.2f, 0.85f, 0.45f) : Color.gray;

            if (GUILayout.Button($"🔮 Forger & Apprendre '{candidateSpell.Name}' ({candidateSpell.CreationXpCost} XP ➔ {candidateSpell.ActionPointCost} PA)", GUILayout.Height(34)))
            {
                if (ArcanotechWorkshop.LearnSpell(_currentSheet, candidateSpell, out string msg))
                {
                    SaveCharacterAndSyncUnits(_currentSheet);
                    _statusMessage = msg;
                }
                else
                {
                    _statusMessage = msg;
                }
            }
            GUI.backgroundColor = Color.white;
            GUI.enabled = true;

            GUILayout.EndVertical();

            GUILayout.Space(8);
            GUILayout.Label($"<b>5. Sorts Mémorisés sur la Fiche ({_currentSheet.LearnedSpells.Count}) :</b>");

            if (_currentSheet.LearnedSpells.Count == 0)
            {
                GUILayout.Label("<color=gray><i>Aucun sort mémorisé. Forgez un sort modulaire ou chargez un preset ci-dessus.</i></color>");
            }

            for (int i = 0; i < _currentSheet.LearnedSpells.Count; i++)
            {
                var sp = _currentSheet.LearnedSpells[i];
                if (sp == null) continue;

                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.BeginHorizontal();

                int spTraining = _currentSheet.GetSkill(sp.AssociatedSkill)?.TrainingLevel ?? 0;
                int spRange = NythariteSpell.CalculateBaseRange(_currentSheet.BaseAttributes.Magie, spTraining);

                GUILayout.Label($"🔮 <b>{sp.Name}</b> <color=#00E5FF>[{sp.ActionPointCost} PA // {sp.CreationXpCost} XP]</color> — <i>{sp.Discipline} ({sp.Stage})</i>", GUILayout.ExpandWidth(true));

                GUI.backgroundColor = Color.red;
                if (GUILayout.Button("✕ Oublier", GUILayout.Width(75), GUILayout.Height(20)))
                {
                    _currentSheet.LearnedSpells.RemoveAt(i);
                    SaveCharacterAndSyncUnits(_currentSheet);
                    _statusMessage = $"Sort '{sp.Name}' oublié.";
                    break;
                }
                GUI.backgroundColor = Color.white;

                GUILayout.EndHorizontal();

                var modSummary = new List<string>();
                if (sp.Modules != null)
                {
                    for (int m = 0; m < sp.Modules.Count; m++)
                    {
                        var sel = sp.Modules[m];
                        var def = ArcanotechWorkshop.GetModuleDefinition(sel.ModuleId);
                        if (def != null) modSummary.Add($"{def.DisplayName} x{sel.Rank}");
                    }
                }
                string modText = modSummary.Count > 0 ? string.Join(", ", modSummary) : "Aucun module";

                GUILayout.Label($"<color=#7090A0>Portée: <b>{spRange} cases</b> | Dégâts bruts: <b>{sp.BaseArcaneDamage}</b> | Effets: <i>{modText}</i></color>");
                if (!string.IsNullOrEmpty(sp.Description))
                {
                    GUILayout.Label($"<color=#94A3B8><i>« {sp.Description} »</i></color>");
                }
                GUILayout.EndVertical();
            }
        }

        private void DrawModuleCatalogGroup(PowerCategory category, string header, string hexColor)
        {
            GUILayout.Space(4);
            GUILayout.Label($"<color={hexColor}><b>{header}</b></color>");

            foreach (var def in ArcanotechWorkshop.GetAllModuleDefinitions())
            {
                if (def.Category != category) continue;

                _spellModuleRanks.TryGetValue(def.Id, out int currentRank);

                GUILayout.BeginHorizontal(GUI.skin.box);
                GUILayout.Label($"<b>{def.DisplayName}</b> <color=#94A3B8>({def.UnitPointCost} pt{(def.UnitPointCost > 1 ? "s" : "")})</color>\n<size=10><color=#7090A0>{def.Description}</color></size>", GUILayout.ExpandWidth(true));

                if (def.IsStackable)
                {
                    GUI.enabled = currentRank > 0;
                    if (GUILayout.Button("-", GUILayout.Width(26), GUILayout.Height(22)))
                    {
                        _spellModuleRanks[def.Id] = Mathf.Max(0, currentRank - 1);
                    }
                    GUI.enabled = true;

                    GUILayout.Label($"<b>{currentRank}</b>", GUILayout.Width(22));

                    if (GUILayout.Button("+", GUILayout.Width(26), GUILayout.Height(22)))
                    {
                        _spellModuleRanks[def.Id] = currentRank + 1;
                    }
                }
                else
                {
                    bool active = currentRank > 0;
                    GUI.backgroundColor = active ? new Color(0.1f, 0.75f, 0.4f) : Color.white;
                    if (GUILayout.Button(active ? "✓ Actif" : "Ajouter", GUILayout.Width(70), GUILayout.Height(22)))
                    {
                        _spellModuleRanks[def.Id] = active ? 0 : 1;
                    }
                    GUI.backgroundColor = Color.white;
                }

                GUILayout.EndHorizontal();
            }
        }

        private void LoadSpellIntoForge(NythariteSpell spell)
        {
            if (spell == null) return;
            _customSpellName = spell.Name;
            _customSpellDesc = spell.Description;
            _customSpellDiscipline = spell.Discipline;
            _customSpellStage = spell.Stage;
            _customSpellSkill = spell.AssociatedSkill;

            _spellModuleRanks.Clear();
            if (spell.Modules != null)
            {
                for (int i = 0; i < spell.Modules.Count; i++)
                {
                    var m = spell.Modules[i];
                    if (m != null) _spellModuleRanks[m.ModuleId] = m.Rank;
                }
            }
        }

        private NythariteSpell BuildCandidateSpell()
        {
            var selections = new List<PowerModuleSelection>();
            foreach (var kvp in _spellModuleRanks)
            {
                if (kvp.Value > 0)
                {
                    selections.Add(new PowerModuleSelection(kvp.Key, kvp.Value));
                }
            }

            var spell = new NythariteSpell(
                name: string.IsNullOrWhiteSpace(_customSpellName) ? "Sort sans nom" : _customSpellName,
                description: _customSpellDesc,
                skill: _customSpellSkill,
                associatedAttribute: "Magie",
                discipline: _customSpellDiscipline,
                stage: _customSpellStage,
                modules: selections
            );

            return spell;
        }

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
                        _lastLoadedModelName = "__UNINITIALIZED__";
            _lastLoadedGenderKey = "__UNINITIALIZED__";
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
            GUILayout.Label("<b>Fiches Héroïques Intégrées (tableau à part — voir MinaCharacter / LucasCharacter / ThomasCharacter) :</b>");

            DrawHeroicSheetRow("Mina", MinaCharacter.DisplayTag, () => MinaCharacter.BuildHeroicSheet());
            DrawHeroicSheetRow("Lucas", LucasCharacter.DisplayTag, () => LucasCharacter.BuildHeroicSheet());
            DrawHeroicSheetRow("Thomas", ThomasCharacter.DisplayTag, () => ThomasCharacter.BuildHeroicSheet());

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
                    _lastLoadedModelName = "__UNINITIALIZED__";
            _lastLoadedGenderKey = "__UNINITIALIZED__";
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
                    _lastLoadedModelName = "__UNINITIALIZED__";
                    _lastLoadedGenderKey = "__UNINITIALIZED__";
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

        // ================= TAB 4 : SPAWN SUR LA GRILLE =================
        private void DrawMapInsertionTab()
        {
            GUILayout.Label("<b>Insérer le Personnage sur la Grille Hexagonale 3D :</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.Label($"Personnage actif : <b>{_currentSheet.Name}</b> ({_currentSheet.Species})");
            string appliedModel = string.IsNullOrEmpty(_currentSheet.ModelPrefabName) ? "Avatar Procédural" : _currentSheet.ModelPrefabName;
            GUILayout.Label($"Modèle 3D appliqué : <b><color=#00E5FF>{appliedModel}</color></b>");
            string footprintColor = _currentSheet.Footprint != TitanFootprintType.Single ? "#FF3B5C" : "#00E5FF";
            GUILayout.Label($"Gabarit tactique : <b><color={footprintColor}>{_currentSheet.Footprint}</color></b>");

            GUILayout.BeginHorizontal();
            GUILayout.Label("Coordonnée Hexagonale Q :", GUILayout.Width(180));
            _spawnQStr = GUILayout.TextField(_spawnQStr, GUILayout.Width(60));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Coordonnée Hexagonale R :", GUILayout.Width(180));
            _spawnRStr = GUILayout.TextField(_spawnRStr, GUILayout.Width(60));
            GUILayout.EndHorizontal();

            _spawnAsPlayer = GUILayout.Toggle(_spawnAsPlayer, "Contrôlé par le Joueur (Bleu) / Sinon Ennemi (Rouge)");

            GUILayout.Space(10);
            GUI.backgroundColor = new Color(0.2f, 0.6f, 0.9f);
            if (GUILayout.Button("⚡ INSERER L'UNITÉ SUR LA GRILLE", GUILayout.Height(38)))
            {
                SpawnCharacterOnGrid();
            }
            GUI.backgroundColor = Color.white;

            GUILayout.EndVertical();
        }

        private void SpawnCharacterOnGrid()
        {
            EnsureReferences();

            if (_grid == null)
            {
                _statusMessage = "Erreur : TacticalHexGrid introuvable.";
                return;
            }

            int.TryParse(_spawnQStr, out int q);
            int.TryParse(_spawnRStr, out int r);

            var coords = new HexCoordinates(q, r);
            var node = _grid.GetNode(coords);

            if (node == null)
            {
                _statusMessage = $"Coordonnées ({q}, {r}) hors limites de la grille.";
                return;
            }

            if (!node.IsWalkable)
            {
                _statusMessage = $"La case ({q}, {r}) est impraticable (obstacle) !";
                return;
            }

            bool isOccupiedByUnit = false;
            var existingUnits = FindObjectsByType<TacticalUnit>();
            for (int i = 0; i < existingUnits.Length; i++)
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
                _statusMessage = $"La case ({q}, {r}) est déjà occupée par un autre avatar !";
                return;
            }

            TacticalUnit unit;
            if (_arena != null)
            {
                unit = _arena.SpawnCustomCharacter(_currentSheet, coords, _spawnAsPlayer);
                if (unit == null)
                {
                    _statusMessage = $"Échec d'insertion en ({q}, {r}) : case indisponible.";
                    return;
                }
                // L'arène a pu relocaliser (anti-empilement) : on affiche la case réelle.
                coords = unit.CurrentCoords;
                q = coords.Q;
                r = coords.R;
            }
            else
            {
                var go = new GameObject($"Unit_{_currentSheet.Name.Replace(" ", "_")}");
                unit = go.AddComponent<TacticalUnit>();
                unit.InitializeFromSheet(_currentSheet, coords, _grid, _spawnAsPlayer);
                unit.GetComponent<TacticalUnitVisual>()?.SetCombatStance(true);
                _turnManager?.RegisterUnit(unit);
            }

            string modelLog = string.IsNullOrEmpty(_currentSheet.ModelPrefabName) ? "Avatar Procédural" : $"Modèle '{_currentSheet.ModelPrefabName}'";
            _statusMessage = $"'{_currentSheet.Name}' inséré avec succès en ({q}, {r}) avec {modelLog} !";
        }
    }
}
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
    /// <summary>
    /// Créateur & Gestionnaire de Personnages avec auto-instanciation et placement sur la grille.
    /// Orchestrateur IMGUI fin : la règle métier vit dans Killtime.DevTools.Character
    /// (SheetSessionService, AttributeRulesService, SpellForgeSession,
    /// PreviewStudioService, SpawnService, SocialGraphSession, AnimationClipResolver).
    /// </summary>
    public partial class CharacterDevWindow : FloatingWindow<CharacterDevWindow>
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

        // --- Services : propriétaires de l'état métier (window = binding IMGUI) ---
        private readonly SheetSessionService _sheets = new();
        private readonly SpellForgeSession _forge = new();
        private readonly PreviewStudioService _previewStudio = new();
        private readonly SocialGraphSession _social = new();

        // Fiche active : source unique = SheetSessionService.
        private CharacterSheet _currentSheet
        {
            get => _sheets.CurrentSheet;
            set => _sheets.SetCurrent(value);
        }
        private int _selectedTab = 0;
        private readonly string[] _tabTitles = { "👤 Création", "🎒 Inventaire", "📈 Progression", "🔮 Sorts (XP=PA)", "💾 Disque", "🗺️ Spawner", "🤝 Relations" };

        // Graphe social : source unique = SocialGraphSession.
        private CharacterSocialGraph _currentSocialGraph
        {
            get => _social.Current;
            set => _social.Current = value;
        }
        private string _lastLoadedSocialSheetId
        {
            get => _social.LastLoadedSheetId;
            set => _social.LastLoadedSheetId = value;
        }
        private Vector2 _socialScroll;
        private string _newRelationKnownSince
        {
            get => _social.NewRelationKnownSince;
            set => _social.NewRelationKnownSince = value;
        }
        private string _newRelationLocation
        {
            get => _social.NewRelationLocation;
            set => _social.NewRelationLocation = value;
        }

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

        // --- Studio d'Aperçu 3D : source unique = PreviewStudioService ---
        private float _previewModelYaw
        {
            get => _previewStudio.ModelYaw;
            set => _previewStudio.ModelYaw = value;
        }
        private int _selectedPreviewAnimIndex
        {
            get => _previewStudio.SelectedAnimIndex;
            set => _previewStudio.SetSelectedAnimIndex(value);
        }
        private bool _showAnimDropdown
        {
            get => _previewStudio.ShowAnimDropdown;
            set => _previewStudio.ShowAnimDropdown = value;
        }
        private bool _showHiddenModelCheats = false;
        private string[] _previewAnimationStates => _previewStudio.AnimationStates;

        private Vector2 _scrollPos;

        private string _statusMessage = "Prêt.";
        private string _spawnQStr = "0";
        private string _spawnRStr = "1";
        private bool _spawnAsPlayer = false;

        // Sorts modulaires (Livre IV §19) : source unique = SpellForgeSession.
        private string _customSpellName
        {
            get => _forge.CustomSpellName;
            set => _forge.CustomSpellName = value;
        }
        private string _customSpellDesc
        {
            get => _forge.CustomSpellDesc;
            set => _forge.CustomSpellDesc = value;
        }
        private PsychicDiscipline _customSpellDiscipline
        {
            get => _forge.CustomSpellDiscipline;
            set => _forge.CustomSpellDiscipline = value;
        }
        private PsychicStage _customSpellStage
        {
            get => _forge.CustomSpellStage;
            set => _forge.CustomSpellStage = value;
        }
        private SkillType _customSpellSkill
        {
            get => _forge.CustomSpellSkill;
            set => _forge.CustomSpellSkill = value;
        }
        private Dictionary<ArcanotechModuleId, int> _spellModuleRanks => _forge.ModuleRanks;
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

        public static void OpenForSheet(CharacterSheet sheet, string contextMessage = null)
        {
            if (sheet == null) return;
            Open();
            Instance?.InspectSheet(sheet, contextMessage);
        }

        public void InspectSheet(CharacterSheet sheet, string contextMessage = null)
        {
            if (sheet == null) return;
            try { CharacterProgressionManager.SynchronizeProgression(sheet); } catch { /* ignore */ }
            _currentSheet = sheet;
            _selectedInventoryItem = null;
            _showModelDropdown = false;
            _showAnimDropdown = false;
            _previewStudio.InvalidateCache();
            _selectedTab = 0;
            _scrollPos = Vector2.zero;
            _statusMessage = !string.IsNullOrEmpty(contextMessage)
                ? contextMessage
                : $"Inspection active : {sheet.Name} (XP Total : {sheet.TotalSpentXP} XP)";
            OpenInstance();
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

        private void EnsurePreviewStudio()
        {
            _previewStudio.Ensure();
        }

        private void UpdatePreviewModel(string modelName)
        {
            _previewStudio.UpdatePreviewModel(modelName, ResolvePreviewGenderKey());
        }

        private void PlayPreviewAnimation(int index)
        {
            if (_previewStudio.Play(index, out string missing))
            {
                return;
            }
            if (!string.IsNullOrEmpty(missing))
            {
                _statusMessage = missing;
            }
        }

        private string ResolvePreviewGenderKey()
        {
            return AnimationClipResolver.ResolveGenderKey(_currentSheet);
        }

        private static int GenderToIndex(string gender)
        {
            return AnimationClipResolver.GenderToIndex(gender);
        }

        private static AnimationClip LoadAnimationClip(string clipName)
        {
            return AnimationClipResolver.LoadClip(clipName);
        }

        private static AnimationClip LoadAnimationClip(string clipName, string genderKey)
        {
            return AnimationClipResolver.LoadClip(clipName, genderKey);
        }

        private void RenderPreviewStudio()
        {
            _previewStudio.Render();
        }

        private void DrawPreviewViewport(float width, float height)
        {
            _previewStudio.DrawViewport(width, height);
        }

        private void CleanupPreviewStudio()
        {
            _previewStudio.Cleanup();
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
                case 6: DrawRelationshipsTab(); break;
            }

            GUILayout.EndScrollView();

            if (GUI.changed)
            {
                try { CaptureDevPrefs(); } catch { /* ignore */ }
            }
        }

        private void SaveCharacterAndSyncUnits(CharacterSheet sheet)
        {
            SheetSessionService.SaveAndSyncUnits(sheet);
        }

        private void CreateNewCharacter()
        {
            _currentSheet = new CharacterSheet();
            ApplyDefaultProfileAttributes(_currentSheet);
            _currentSheet.Name = "Nouveau Personnage";

            // Réinitialise l'état d'édition lié à l'ancienne fiche.
            _selectedInventoryItem = null;
            _currentSocialGraph = null;
            _lastLoadedSocialSheetId = string.Empty;
            _selectedTab = 0;
            _scrollPos = Vector2.zero;
            _invScroll = Vector2.zero;
            _gunCatalogScroll = Vector2.zero;
            _showModelDropdown = false;
            _showAnimDropdown = false;
            _selectedPreviewAnimIndex = 0;
            _previewStudio.InvalidateCache();

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


    }
}
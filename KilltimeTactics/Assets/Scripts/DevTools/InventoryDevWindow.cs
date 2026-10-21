// InventoryDevWindow.cs — façade (split move-only ; corps partagé avec .Catalog/.Item/.Studio).
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
    /// Catalogue = ArmoryCatalog (121 items JSON, tout ce qui peut aller en inventaire) ;
    /// modèles 3D = prefabs Resources/Guns si présents, sinon placeholders procéduraux.
    /// Données éditables : Resources/Data/Armory.json + fenêtre Armurerie (F12).
    /// </summary>
    public partial class InventoryDevWindow : FloatingWindow<InventoryDevWindow>
    {
        protected override int WindowId => 894;
        protected override string Title => "Inventaire // Armurerie & Marché (F4 / I)";
        protected override Vector2 MinSize => _minSize;
        protected override Rect DefaultRect => new Rect(
            Mathf.Max(10f, Screen.width - _defaultWidth - 20f), 96f,
            Mathf.Min(_defaultWidth, Mathf.Max(_minSize.x, Screen.width - 40f)),
            Mathf.Min(740f, Mathf.Max(_minSize.y, Screen.height - 115f)));
        protected override KeyCode[] ToggleKeys => _toggleKeys;

        private static readonly KeyCode[] _toggleKeys = { KeyCode.F4, KeyCode.I };
        private static readonly Vector2 _minSize = new Vector2(640, 420);
        private const float _defaultWidth = 920f;

        [Header("Systèmes")]
        [SerializeField] private TurnManager _turnManager;

        private TacticalUnit _selectedUnit;
        private readonly List<TacticalUnit> _sceneUnits = new();
        private int _selectedUnitIndex = 0;

        // --- Liaison au Créateur de Personnages (F1) ---
        // Quand l'armurerie est ouverte depuis le créateur, elle édite directement
        // la fiche en cours de création (_draftSheet) au lieu d'une unité en scène.
        // Référence live : tout ajout/achat/vente touche le perso que l'on crée.
        private CharacterSheet _draftSheet;

        private readonly List<string> _availableGunPrefabs = new();
        private int _selectedCatalogIndex = 0;
        private int _selectedMarketIndex = 0;

        private InventoryItem _inspectedItem;
        private InventoryItem _previewCatalogDef;
        private int _selectedTab = 0;
        private readonly string[] _tabs = { "🎒 Sac", "📦 Armurerie", "🏪 Marché", "🛠️ Propriétés" };

        // Filtres armurerie / marché
        private string _searchFilter = "";
        private int _eraFilter = -1; // -1 = toutes les ères
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
            _draftSheet = null;
            _inspectedItem = null;
            _previewCatalogDef = null;
            RefreshSceneUnits();
            int idx = _sceneUnits.IndexOf(unit);
            if (idx >= 0)
            {
                _selectedUnitIndex = idx;
                _selectedUnit = unit;
            }
            OpenInstance();
        }

        /// <summary>
        /// Lie l'armurerie / le marché à la fiche en cours de création dans le
        /// Créateur de Personnages (F1). Tant que ce lien est actif, tout ajout
        /// (+ Sac, Acheter, dotation) va sur <paramref name="sheet" />.
        /// </summary>
        public void InspectSheet(CharacterSheet sheet)
        {
            if (sheet == null) return;
            EnsureReferences();
            RefreshSceneUnits();
            _draftSheet = sheet;
            _inspectedItem = null;
            _previewCatalogDef = null;
            _statusMessage = $"Lié au personnage en création : '{sheet.Name}'. Les ajouts vont sur cette fiche.";
            OpenInstance();
        }

        /// <summary>Retour au mode standard (unités en scène). Appelé via le header.</summary>
        public void ClearDraftBinding()
        {
            _draftSheet = null;
            _inspectedItem = null;
            _previewCatalogDef = null;
            RefreshSceneUnits();
            _statusMessage = "Prêt. Catalogue Livre VIII chargé.";
        }

        /// <summary>Vrai quand l'armurerie édite la fiche du créateur plutôt qu'une unité.</summary>
        public bool IsBoundToDraft => _draftSheet != null;

        /// <summary>
        /// Fiche active : le brouillon du créateur si lié, sinon la fiche de l'unité
        /// sélectionnée. Si le créateur a rechargé une autre fiche entre-temps, on
        /// suit sa fiche courante pour ne jamais écrire sur un brouillon périmé.
        /// </summary>
        public CharacterSheet GetActiveSheet()
        {
            if (_draftSheet != null)
            {
                var creator = CharacterDevWindow.Instance;
                if (creator != null && creator.CurrentSheet != null)
                {
                    // Le créateur a pu charger une autre fiche : on suit toujours
                    // la fiche qu'il édite actuellement.
                    if (!object.ReferenceEquals(_draftSheet, creator.CurrentSheet))
                    {
                        // Si l'ancien brouillon correspondait à une unité déjà spawnée,
                        // on garde quand même la nouvelle fiche du créateur : c'est elle
                        // que l'utilisateur est en train de créer.
                        _draftSheet = creator.CurrentSheet;
                        _inspectedItem = null;
                        _previewCatalogDef = null;
                    }
                }
                return _draftSheet;
            }
            return _selectedUnit != null ? _selectedUnit.GetOrBuildSheet() : null;
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

        private void RefreshSceneUnits()
        {
            _sceneUnits.Clear();
            var all = TacticalUnitRegistry.AllUnits;
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && all[i].Stats != null) _sceneUnits.Add(all[i]);
            if (_selectedUnitIndex >= _sceneUnits.Count) _selectedUnitIndex = 0;
            // En mode brouillon on ne touche pas à _selectedUnit : la fiche active
            // reste le draft du créateur. Sinon on suit la sélection scène.
            if (_draftSheet == null)
                _selectedUnit = _sceneUnits.Count > 0 ? _sceneUnits[_selectedUnitIndex] : null;
            else if (_selectedUnit != null && !_sceneUnits.Contains(_selectedUnit))
            {
                // L'unité précédemment sélectionnée a disparu : on la garde à null
                // sans casser le lien draft.
                if (_sceneUnits.Count > 0 && _selectedUnitIndex < _sceneUnits.Count)
                    _selectedUnit = _sceneUnits[_selectedUnitIndex];
                else
                    _selectedUnit = null;
            }
        }

        private static GUIStyle _richLabel;
    }
}

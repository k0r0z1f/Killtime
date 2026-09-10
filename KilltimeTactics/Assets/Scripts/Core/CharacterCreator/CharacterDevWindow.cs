using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Core.Character;
using Killtime.Core.Arcanotech;
using Killtime.Tactics;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;

namespace Killtime.UI
{
    /// <summary>
    /// Créateur & Gestionnaire de Personnages avec auto-instanciation et placement sur la grille.
    /// </summary>
    public class CharacterDevWindow : MonoBehaviour
    {
        public static CharacterDevWindow Instance { get; private set; }

        [Header("Systèmes")]
        [SerializeField] private TacticalHexGrid _grid;
        [SerializeField] private TurnManager _turnManager;
        [SerializeField] private CombatDevArena _arena;

        [Header("Affichage")]
        [SerializeField] private bool _isOpen = false;
        [SerializeField] private KeyCode _toggleKey = KeyCode.F1;

        private CharacterSheet _currentSheet = new();
        private int _selectedTab = 0;
        private readonly string[] _tabTitles = { "👤 Création", "📈 Progression", "🔮 Sorts (XP=PA)", "💾 Disque", "🗺️ Spawner" };

        private readonly List<string> _availableModelNames = new();
        private bool _showModelDropdown = false;

        // --- Studio d'Aperçu 3D Temps Réel ---
        private RenderTexture _previewRT;
        private Camera _previewCam;
        private GameObject _previewStudioRoot;
        private GameObject _currentPreviewInstance;
        private string _lastLoadedModelName = "__UNINITIALIZED__";
        private float _previewModelYaw = 180f;
        private bool _isDraggingPreview = false;
        private Vector2 _lastMousePos;

        private Rect _windowRect;
        private Vector2 _scrollPos;
        private string _statusMessage = "Prêt.";
        private string _spawnQStr = "0";
        private string _spawnRStr = "1";
        private bool _spawnAsPlayer = false;

        // Sorts modulaires
        private string _customSpellName = "Onde de Choc Causal";
        private int _customSpellDamage = 6;
        private int _customSpellCost = 3;

        /// <summary>
        /// Ouvre la fenêtre et crée l'objet dans la scène s'il n'existe pas encore.
        /// </summary>
        public static void Open()
        {
            if (Instance == null)
            {
                Instance = FindAnyObjectByType<CharacterDevWindow>();
                if (Instance == null)
                {
                    var go = new GameObject("[UI] CharacterDevWindow");
                    Instance = go.AddComponent<CharacterDevWindow>();
                }
            }

            Instance._isOpen = true;
            Instance.EnsureReferences();
        }

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
            _isOpen = true;
            _selectedTab = 0;
        }

        private void Awake()
        {
            if (Instance == null) Instance = this;
            EnsureReferences();
            CharacterStorageService.EnsureDirectoryExists();
            RefreshAvailableModels();
            EnsurePreviewStudio();

            // Positionne la fenêtre sur la droite de l'écran
            float width = 660;
            float height = Mathf.Min(760, Screen.height - 40);
            _windowRect = new Rect(Screen.width - width - 20, 20, width, height);
        }

        private void OnDisable()
        {
            CleanupPreviewStudio();
        }

        private void OnDestroy()
        {
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

        private void Update()
        {
            if (Input.GetKeyDown(_toggleKey))
            {
                _isOpen = !_isOpen;
                if (_isOpen)
                {
                    EnsureReferences();
                    EnsurePreviewStudio();
                }
            }

            if (_isOpen && (_selectedTab == 0 || _selectedTab == 4))
            {
                UpdatePreviewModel(_currentSheet.ModelPrefabName);
                RenderPreviewStudio();
            }
        }

        public void OpenWindow()
        {
            _isOpen = true;
            EnsureReferences();
        }

        private void OnGUI()
        {
            if (!_isOpen) return;

            // Garde la fenêtre visible à l'écran
            _windowRect.height = Mathf.Min(780, Screen.height - 40);
            _windowRect = GUI.Window(888, _windowRect, DrawWindowContent, "⚔️ Killtime — Créateur de Personnages (Héros & PNJ)");
            GUI.BringWindowToFront(888);
        }

        private void DrawWindowContent(int windowId)
        {
            GUI.DragWindow(new Rect(0, 0, _windowRect.width - 70, 25));
            if (GUI.Button(new Rect(_windowRect.width - 65, 4, 60, 20), "Fermer"))
            {
                _isOpen = false;
            }

            GUILayout.Space(6);
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
                case 1: DrawProgressionTab(); break;
                case 2: DrawSpellForgeTab(); break;
                case 3: DrawStorageTab(); break;
                case 4: DrawMapInsertionTab(); break;
            }

            GUILayout.EndScrollView();
        }

        // ================= TAB 0 : CRÉATION =================
        private void DrawCreationTab()
        {
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
            _currentSheet.Gender = GUILayout.TextField(_currentSheet.Gender);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Espèce :", GUILayout.Width(90));
            _currentSheet.Species = (SpeciesType)GUILayout.Toolbar((int)_currentSheet.Species, Enum.GetNames(typeof(SpeciesType)));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Profil :", GUILayout.Width(90));
            _currentSheet.Profile = (CharacterProfileType)GUILayout.Toolbar((int)_currentSheet.Profile, Enum.GetNames(typeof(CharacterProfileType)));
            GUILayout.EndHorizontal();

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

            GUILayout.BeginVertical(GUI.skin.box);

            _currentSheet.BaseAttributes.Force = DrawAttrRow("Force (FOR)", _currentSheet.BaseAttributes.Force);
            _currentSheet.BaseAttributes.Agilite = DrawAttrRow("Agilité (AGI)", _currentSheet.BaseAttributes.Agilite);
            _currentSheet.BaseAttributes.Constitution = DrawAttrRow("Constitution (CON)", _currentSheet.BaseAttributes.Constitution);
            _currentSheet.BaseAttributes.Rapidite = DrawAttrRow("Rapidité (RAP)", _currentSheet.BaseAttributes.Rapidite);
            _currentSheet.BaseAttributes.Intelligence = DrawAttrRow("Intelligence (INT)", _currentSheet.BaseAttributes.Intelligence);
            _currentSheet.BaseAttributes.Erudition = DrawAttrRow("Érudition (ÉRU)", _currentSheet.BaseAttributes.Erudition);
            _currentSheet.BaseAttributes.Charisme = DrawAttrRow("Charisme (CHA)", _currentSheet.BaseAttributes.Charisme);
            _currentSheet.BaseAttributes.Instinct = DrawAttrRow("Instinct (INS)", _currentSheet.BaseAttributes.Instinct);
            _currentSheet.BaseAttributes.Magie = DrawAttrRow("Magie (5e Force)", _currentSheet.BaseAttributes.Magie);

            GUILayout.EndVertical();

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

            GUILayout.Space(8);
            GUI.backgroundColor = new Color(0.2f, 0.7f, 0.3f);
            if (GUILayout.Button("💾 Sauvegarder cette fiche sur le Disque", GUILayout.Height(34)))
            {
                string path = CharacterStorageService.SaveCharacter(_currentSheet);
                _statusMessage = $"Fiche '{_currentSheet.Name}' sauvegardée !";
            }
            GUI.backgroundColor = Color.white;
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

            // Hub Visuel : Aperçu 3D du modèle sélectionné
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            DrawPreviewViewport(210, 210);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
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
            if (modelName == _lastLoadedModelName && _currentPreviewInstance != null) return;

            EnsurePreviewStudio();
            _lastLoadedModelName = modelName;

            if (_currentPreviewInstance != null)
            {
                Destroy(_currentPreviewInstance);
                _currentPreviewInstance = null;
            }

            if (!string.IsNullOrEmpty(modelName) && modelName != "(Procédural)")
            {
                string cleanName = modelName.StartsWith("Characters/") ? modelName.Substring("Characters/".Length) : modelName;
                var prefab = Resources.Load<GameObject>($"Characters/{cleanName}") ?? Resources.Load<GameObject>(cleanName);

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

            FramePreviewCamera();
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
        }

        private int DrawAttrRow(string label, int value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(170));
            if (GUILayout.Button("-", GUILayout.Width(30))) value = Mathf.Max(1, value - 1);
            GUILayout.Label($"<b>{value}</b>", GUILayout.Width(35));
            if (GUILayout.Button("+", GUILayout.Width(30))) value = Mathf.Min(10, value + 1);
            GUILayout.EndHorizontal();
            return value;
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
            GUILayout.BeginHorizontal(GUI.skin.box);
            GUILayout.Label($"Personnage : <b>{_currentSheet.Name}</b> | XP Disponible : <color=yellow><b>{_currentSheet.AvailableXP} XP</b></color>");
            if (GUILayout.Button("+10 XP", GUILayout.Width(70))) CharacterProgressionManager.GrantXP(_currentSheet, 10);
            if (GUILayout.Button("+50 XP", GUILayout.Width(70))) CharacterProgressionManager.GrantXP(_currentSheet, 50);
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label("<b>Arbres de Compétences (5 XP = +1 Palier de Dé) :</b>");

            foreach (var entry in _currentSheet.Skills)
            {
                GUILayout.BeginHorizontal(GUI.skin.box);
                GUILayout.Label($"<b>{entry.Skill}</b> (Niveau +{entry.TrainingLevel})", GUILayout.Width(250));
                if (GUILayout.Button("Entraîner (+1 Palier / 5 XP)", GUILayout.Width(190)))
                {
                    if (CharacterProgressionManager.TrainSkill(_currentSheet, entry.Skill, out string msg))
                        _statusMessage = msg;
                    else
                        _statusMessage = msg;
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(8);
            GUILayout.Label("<b>Spécialisations (5 XP) :</b>");
            string[] availableSpecs = { "Maniement de l'Épée", "Marteau de Guerre", "Pistolet & Tir Rapide", "Escrime", "Arts Martiaux", "Chirurgie", "Tromper", "Négocier", "Intimider", "Leadership" };

            foreach (var spec in availableSpecs)
            {
                bool owned = _currentSheet.UnlockedSpecializations.Contains(spec);
                GUILayout.BeginHorizontal(GUI.skin.box);
                GUILayout.Label(spec, GUILayout.Width(230));
                if (owned)
                {
                    GUI.color = Color.green;
                    GUILayout.Label("✅ Acquis");
                    GUI.color = Color.white;
                }
                else
                {
                    if (GUILayout.Button("Débloquer (5 XP)", GUILayout.Width(130)))
                    {
                        if (CharacterProgressionManager.UnlockSpecialization(_currentSheet, spec, out string msg))
                            _statusMessage = msg;
                    }
                }
                GUILayout.EndHorizontal();
            }
        }

        // ================= TAB 2 : FORGE DE SORTS =================
        private void DrawSpellForgeTab()
        {
            GUILayout.Label("<b>Atelier de Sorts Modulaires (Livre IV — 1 XP = 1 PA) :</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Nom du Sort :", GUILayout.Width(110));
            _customSpellName = GUILayout.TextField(_customSpellName);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Dégâts : {_customSpellDamage}", GUILayout.Width(110));
            _customSpellDamage = (int)GUILayout.HorizontalSlider(_customSpellDamage, 1, 20);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Coût XP / PA : {_customSpellCost}", GUILayout.Width(110));
            _customSpellCost = (int)GUILayout.HorizontalSlider(_customSpellCost, 1, 10);
            GUILayout.EndHorizontal();

            if (GUILayout.Button($"🔮 Forger & Apprendre ce Sort ({_customSpellCost} XP)", GUILayout.Height(34)))
            {
                var spell = new NythariteSpell(_customSpellName, PsychicDiscipline.Telekinesie, PsychicStage.Stade2_AccesProfond, _customSpellCost, damage: _customSpellDamage);
                if (CharacterProgressionManager.LearnModularSpell(_currentSheet, spell, out string msg))
                    _statusMessage = msg;
                else
                    _statusMessage = msg;
            }

            GUILayout.EndVertical();

            GUILayout.Space(8);
            GUILayout.Label("<b>Sorts Mémorisés :</b>");
            foreach (var sp in _currentSheet.LearnedSpells)
            {
                GUILayout.Label($"• <b>{sp.Name}</b> — Coût : {sp.ActionPointCost} PA | Dégâts : {sp.BaseArcaneDamage}");
            }
        }

        // ================= TAB 3 : FICHIERS =================
        private void DrawStorageTab()
        {
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
                    _currentSheet = CharacterStorageService.LoadCharacter(f);
                    _statusMessage = $"'{_currentSheet.Name}' chargé !";
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
            if (GUILayout.Button("📋 Copier le JSON de cette fiche dans le Presse-Papier"))
            {
                GUIUtility.systemCopyBuffer = CharacterStorageService.ExportToJson(_currentSheet);
                _statusMessage = "JSON copié !";
            }
        }

        // ================= TAB 4 : SPAWN SUR LA GRILLE =================
        private void DrawMapInsertionTab()
        {
            GUILayout.Label("<b>Insérer le Personnage sur la Grille Hexagonale 3D :</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.Label($"Personnage actif : <b>{_currentSheet.Name}</b> ({_currentSheet.Species})");
            string appliedModel = string.IsNullOrEmpty(_currentSheet.ModelPrefabName) ? "Avatar Procédural" : _currentSheet.ModelPrefabName;
            GUILayout.Label($"Modèle 3D appliqué : <b><color=#00E5FF>{appliedModel}</color></b>");

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

            if (node.IsOccupied)
            {
                _statusMessage = $"La case ({q}, {r}) est déjà occupée !";
                return;
            }

            var go = new GameObject($"Unit_{_currentSheet.Name.Replace(" ", "_")}");
            var unit = go.AddComponent<TacticalUnit>();
            unit.InitializeFromSheet(_currentSheet, coords, _grid, _spawnAsPlayer);

            if (_turnManager != null)
            {
                _turnManager.RegisterUnit(unit);
            }

            if (_arena != null && !_spawnAsPlayer)
            {
                _arena.SparringDummies.Add(unit);
            }

            string modelLog = string.IsNullOrEmpty(_currentSheet.ModelPrefabName) ? "Avatar Procédural" : $"Modèle '{_currentSheet.ModelPrefabName}'";
            _statusMessage = $"'{_currentSheet.Name}' inséré avec succès en ({q}, {r}) avec {modelLog} !";
        }
    }
}
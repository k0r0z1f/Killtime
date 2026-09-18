using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using Killtime.UI;
using Killtime.Story.Data;
using Killtime.Core.Character;
using Killtime.Core.Inventory;
using Killtime.Tactics;
using Killtime.Tactics.Grid;
using Killtime.Tactics.TurnSystem;

namespace Killtime.Story
{
    public class ScenarioEditorDevWindow : FloatingWindow<ScenarioEditorDevWindow>
    {
        protected override int WindowId => 991;
        protected override string Title => "Éditeur de Scènes Narratives & Graphe Nodal (JSON)";
        protected override Vector2 MinSize => new Vector2(980f, 640f);
        protected override Rect DefaultRect => new Rect(20f, 40f, Mathf.Min(1280f, Screen.width - 40f), Mathf.Min(840f, Screen.height - 60f));
        protected override KeyCode[] ToggleKeys => new[] { KeyCode.F8 };

        private readonly string[] _tabs = { "📋 Scène & Fichier", "👥 Acteurs", "🔧 Interactables", "🎬 Nœuds & Graphe Nodal" };
        private int _selectedTab = 3;
        private Vector2 _scroll;
        private bool _showRawJsonPreview = false;

        private StorySceneData _data = new();
        private string _activeFilePath = "";
        private int _selectedActorIndex = 0;
        private int _selectedInteractableIndex = 0;

        private Vector2 _graphPan = new Vector2(80f, 80f);
        private float _zoom = 1.0f;
        private string _draggingCardKey = null;
        private string _resizingCardKey = null;
        private string _draggingNodeId = null;
        private Vector2 _dragOffset;
        private Vector2 _dragNodeStartMouse;
        private bool _isPanning = false;
        private Vector2 _panStartMouse;
        private Vector2 _panStartOffset;

        private struct WireConnectionDraft
        {
            public bool IsActive;
            public bool FromChoice;
            public bool FromAutoCheck;
            public bool IsAutoSuccess;
            public bool IsEvent;
            public bool IsTrigger;
            public bool FromConsequence;
            public bool ConsLinkToCons;
            public bool FromChallengeLink;
            public bool ChallengeLinkIsSuccess;
            public SceneDialogueLineData SourceLine;
            public SceneDialogueChoiceData SourceChoice;
            public SceneDialogueChallengeData SourceChallenge;
            public SceneEventData SourceEvent;
            public SceneTriggerData SourceTrigger;
            public SceneConsequenceData SourceConsequence;
            public Vector2 StartPos;
        }

        private WireConnectionDraft _wireDraft;
        private readonly Dictionary<string, Vector2> _cachedInputSockets = new();
        private readonly Dictionary<string, Vector2> _cachedTriggerOutSockets = new();
        private bool _hasInitializedLayout = false;

        // Vue : suivi du dernier cadrage pour recadrer quand la fenêtre est redimensionnée.
        // _fitZoom = zoom posé par le dernier cadrage programmatique (Cadrer/Mosaïque/focus).
        // Tant que l'utilisateur ne zoome pas AU-DESSUS (édition rapprochée), un redimensionnement
        // ré-applique l'intention de cadrage au lieu de laisser le contenu tronqué hors champ.
        private float _fitZoom = 1.0f;
        private bool _lastViewWasAll = true;

        // Infobulle lisible au survol (espace écran, hors matrice zoomée) : à petit zoom les
        // champs sont illisibles, le survol du header d'une carte montre son contenu en clair.
        private bool _hasHoverCard;
        private string _hoverTitle = "";
        private string _hoverBody = "";
        private Vector2 _hoverScreenPos;
        // Position écran stable pour les infobulles : les cartes sont dessinées dans un BeginGroup.
        private Vector2 _graphMouseScreenPos;

        private readonly List<string> _availableCharacterFiles = new();
        private readonly List<string> _availableMapFiles = new();
        private readonly List<string> _availableSceneFiles = new();

        // Drag & Drop pour la réorganisation des scènes dans la liste
        private int _draggedSceneIndex = -1;
        private int _dropTargetSceneIndex = -1;
        private bool _isDraggingSceneItem = false;
        private Vector2 _dragSceneMouseStart;
        private readonly List<Rect> _sceneRowRects = new();

        protected override void OnAwake()
        {
            RefreshCatalogCache();
            if (_data.Nodes.Count == 0)
            {
                CreateDefaultSceneTemplate();
            }
        }

        private void RefreshCatalogCache()
        {
            _availableCharacterFiles.Clear();
            _availableCharacterFiles.Add("(Générer fiche par défaut)");
            string charDir = Path.Combine(Application.persistentDataPath, "Characters");
            if (Directory.Exists(charDir))
            {
                foreach (var f in Directory.GetFiles(charDir, "*.json"))
                {
                    _availableCharacterFiles.Add(Path.GetFileName(f));
                }
            }

            _availableMapFiles.Clear();
            _availableMapFiles.Add("(Aucune carte liée)");
            string mapDir = Path.Combine(Application.persistentDataPath, "Maps");
            if (Directory.Exists(mapDir))
            {
                foreach (var f in Directory.GetFiles(mapDir, "*.json"))
                {
                    _availableMapFiles.Add(Path.GetFileNameWithoutExtension(f));
                }
            }

            _availableSceneFiles.Clear();
            string sceneDir = Path.Combine(Application.persistentDataPath, "Scenarios");
            if (!Directory.Exists(sceneDir)) Directory.CreateDirectory(sceneDir);

            string[] diskFiles = Directory.GetFiles(sceneDir, "*.json");
            var diskFileSet = new HashSet<string>(diskFiles, StringComparer.OrdinalIgnoreCase);

            // Restaurer l'ordre personnalisé si enregistré
            string savedOrder = PlayerPrefs.GetString("ScenarioEditor_SceneOrder", "");
            if (!string.IsNullOrEmpty(savedOrder))
            {
                string[] savedNames = savedOrder.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                for (int s = 0; s < savedNames.Length; s++)
                {
                    string target = Path.Combine(sceneDir, savedNames[s]);
                    if (diskFileSet.Contains(target) && !_availableSceneFiles.Contains(target))
                    {
                        _availableSceneFiles.Add(target);
                    }
                }
            }

            // Ajouter les fichiers restants (ou par défaut, tri alphabétique)
            Array.Sort(diskFiles, (a, b) => string.Compare(Path.GetFileName(a), Path.GetFileName(b), StringComparison.OrdinalIgnoreCase));
            for (int i = 0; i < diskFiles.Length; i++)
            {
                if (!_availableSceneFiles.Contains(diskFiles[i]))
                {
                    _availableSceneFiles.Add(diskFiles[i]);
                }
            }

            // Mettre à jour l'ordre sauvegardé pour éliminer les fichiers supprimés
            SaveSceneOrder();
        }

        public void LoadSceneFromPath(string path)
        {
            if (!File.Exists(path)) return;
            try
            {
                string json = File.ReadAllText(path);
                var loaded = JsonUtility.FromJson<StorySceneData>(json);
                if (loaded != null)
                {
                    StorySceneData.EnsureDeepDefaults(loaded);
                    _data = loaded;
                    _activeFilePath = path;
                    _hasInitializedLayout = false;
                    _selectedActorIndex = 0;
                    _selectedInteractableIndex = 0;
                    _focusedNodeIndex = 0;
                    CombatHUD.Instance?.AddAdvancedLog($"📖 Scène chargée : <b>{_data.Title}</b> [{_data.SceneId}]", LogCategory.MovementAndTurns, "[ÉDITEUR]", Color.cyan);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ScenarioEditor] Échec du chargement de '{path}' : {ex.Message}");
            }
        }

        public void CreateNewEmptyScene()
        {
            _data = new StorySceneData
            {
                SceneId = $"nouvelle_scene_{_availableSceneFiles.Count + 1}",
                Title = "Nouvelle Scène",
                Volume = "Livre I / Volume 1",
                FirstNodeId = "node_1"
            };
            _data.Nodes.Add(new SceneNodeData
            {
                NodeId = "node_1",
                Title = "Introduction",
                GraphPosX = 60f,
                GraphPosY = 60f
            });
            StorySceneData.EnsureDeepDefaults(_data);
            _activeFilePath = "";
            _hasInitializedLayout = false;
            _selectedActorIndex = 0;
            _selectedInteractableIndex = 0;
            _focusedNodeIndex = 0;
        }

        private void CreateDefaultSceneTemplate()
        {
            string directory = Path.Combine(Application.persistentDataPath, "Scenarios");
            string path = Path.Combine(directory, "volume_1_scene_01.json");

            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    var loaded = JsonUtility.FromJson<StorySceneData>(json);
                    if (loaded != null)
                    {
                        StorySceneData.EnsureDeepDefaults(loaded);
                        _data = loaded;
                        _activeFilePath = path;
                        if (_data.Nodes != null && _data.Nodes.Count > 0) return;
                    }
                }
                catch { /* ignore */ }
            }

            _data = new StorySceneData();
            StorySceneData.EnsureDeepDefaults(_data);
        }

        protected override void DrawContent()
        {
            GUILayout.Space(4);
            _selectedTab = GUILayout.Toolbar(_selectedTab, _tabs, GUILayout.Height(28));
            GUILayout.Space(6);

            if (_selectedTab == 3)
            {
                DrawNodesAndDialoguesTab();
            }
            else
            {
                _scroll = GUILayout.BeginScrollView(_scroll);
                switch (_selectedTab)
                {
                    case 0: DrawSceneAndDiskTab(); break;
                    case 1: DrawActorsTab(); break;
                    case 2: DrawInteractablesTab(); break;
                }
                GUILayout.EndScrollView();
            }
        }

        private void DrawSceneAndDiskTab()
        {
            // 1. BARRE D'ACTIONS RAPIDES & FICHIER ACTIF
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>Scène Active :</b>", GUILayout.Width(100));

            if (_availableSceneFiles.Count == 0)
            {
                GUILayout.Label("<color=grey>(Aucune scène sur disque)</color>", GUILayout.Width(180));
            }
            else
            {
                int currentSceneIdx = !string.IsNullOrEmpty(_activeFilePath)
                    ? _availableSceneFiles.FindIndex(f => string.Equals(f, _activeFilePath, StringComparison.OrdinalIgnoreCase))
                    : _availableSceneFiles.FindIndex(f => string.Equals(Path.GetFileNameWithoutExtension(f), _data.SceneId, StringComparison.OrdinalIgnoreCase));
                if (currentSceneIdx < 0) currentSceneIdx = 0;

                if (GUILayout.Button("◀", GUILayout.Width(26)))
                {
                    currentSceneIdx = (currentSceneIdx - 1 + _availableSceneFiles.Count) % _availableSceneFiles.Count;
                    LoadSceneFromPath(_availableSceneFiles[currentSceneIdx]);
                }

                string currentFileName = Path.GetFileName(_availableSceneFiles[currentSceneIdx]);
                GUILayout.Label($"<b>{currentFileName}</b>", GUILayout.Width(180));

                if (GUILayout.Button("▶", GUILayout.Width(26)))
                {
                    currentSceneIdx = (currentSceneIdx + 1) % _availableSceneFiles.Count;
                    LoadSceneFromPath(_availableSceneFiles[currentSceneIdx]);
                }
            }

            GUILayout.FlexibleSpace();

            GUI.backgroundColor = new Color(0.2f, 0.75f, 0.4f);
            if (GUILayout.Button("💾 Enregistrer", GUILayout.Width(105), GUILayout.Height(24)))
            {
                SaveCurrentSceneJson();
            }

            GUI.backgroundColor = new Color(0.2f, 0.6f, 0.9f);
            if (GUILayout.Button("📁 Recharger", GUILayout.Width(95), GUILayout.Height(24)))
            {
                ReloadCurrentSceneJson();
            }

            GUI.backgroundColor = new Color(0.2f, 0.8f, 0.4f);
            if (GUILayout.Button("+ Nouvelle Scène", GUILayout.Width(125), GUILayout.Height(24)))
            {
                CreateNewEmptyScene();
            }

            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();

            // Confirmation de suppression si engagée
            if (_confirmDeleteScene)
            {
                GUILayout.Space(4);
                GUILayout.BeginHorizontal(GUI.skin.box);
                string deleteFileName = !string.IsNullOrEmpty(_activeFilePath) ? Path.GetFileName(_activeFilePath) : $"{_data.SceneId}.json";
                GUILayout.Label($"<color=#FF5555><b>Supprimer définitivement '{deleteFileName}' du disque ?</b></color>");
                GUILayout.FlexibleSpace();
                GUI.backgroundColor = new Color(1f, 0.25f, 0.25f);
                if (GUILayout.Button("⚠ Confirmer destruction", GUILayout.Width(170), GUILayout.Height(22)))
                {
                    DeleteCurrentSceneJson();
                    _confirmDeleteScene = false;
                }
                GUI.backgroundColor = Color.gray;
                if (GUILayout.Button("Annuler", GUILayout.Width(80), GUILayout.Height(22)))
                {
                    _confirmDeleteScene = false;
                }
                GUI.backgroundColor = Color.white;
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.Space(2);
                GUILayout.BeginHorizontal();
                string directory = Path.Combine(Application.persistentDataPath, "Scenarios");
                GUILayout.Label($"<color=grey>Dossier : {directory}</color>");
                GUILayout.FlexibleSpace();
                GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
                if (GUILayout.Button("🗑 Supprimer ce JSON", GUILayout.Width(150), GUILayout.Height(20)))
                {
                    _confirmDeleteScene = true;
                }
                GUI.backgroundColor = Color.white;
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();

            GUILayout.Space(6);

            // 2. PARAMÈTRES GÉNÉRAUX DU SCÉNARIO
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("<b>Paramètres Généraux du Scénario</b>");

            GUILayout.BeginHorizontal();
            GUILayout.Label("Identifiant Unique :", GUILayout.Width(130));
            _data.SceneId = GUILayout.TextField(_data.SceneId);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Titre de la Scène :", GUILayout.Width(130));
            _data.Title = GUILayout.TextField(_data.Title);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Volume Codex :", GUILayout.Width(130));
            _data.Volume = GUILayout.TextField(_data.Volume);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Réf. Manuscrit :", GUILayout.Width(130));
            _data.CanonReference = GUILayout.TextField(_data.CanonReference);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Carte Tactique Liée :", GUILayout.Width(130));
            int mapIdx = Mathf.Max(0, _availableMapFiles.IndexOf(_data.LinkedMapName));
            if (GUILayout.Button("◀", GUILayout.Width(24)))
            {
                mapIdx = (mapIdx - 1 + _availableMapFiles.Count) % _availableMapFiles.Count;
                _data.LinkedMapName = mapIdx == 0 ? "" : _availableMapFiles[mapIdx];
            }
            string mapDisplay = string.IsNullOrEmpty(_data.LinkedMapName) ? "(Aucune carte)" : _data.LinkedMapName;
            GUILayout.Label($"<b>{mapDisplay}</b>", GUILayout.Width(160));
            if (GUILayout.Button("▶", GUILayout.Width(24)))
            {
                mapIdx = (mapIdx + 1) % _availableMapFiles.Count;
                _data.LinkedMapName = mapIdx == 0 ? "" : _availableMapFiles[mapIdx];
            }
            if (GUILayout.Button("🔄", GUILayout.Width(30))) RefreshCatalogCache();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Environnement 3D :", GUILayout.Width(130));
            _data.EnvironmentId = GUILayout.TextField(_data.EnvironmentId ?? "", GUILayout.Width(140));
            GUILayout.Label("<color=grey>(ex : AsteroidBase — vide = aucun décor construit)</color>");
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Nœud de Départ :", GUILayout.Width(130));
            _data.FirstNodeId = GUILayout.TextField(_data.FirstNodeId);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Scène Suivante :", GUILayout.Width(130));
            _data.NextSceneId = GUILayout.TextField(_data.NextSceneId ?? "", GUILayout.Width(160));

            if (_availableSceneFiles != null && _availableSceneFiles.Count > 0)
            {
                var sceneNames = new List<string> { "(Automatique)" };
                for (int i = 0; i < _availableSceneFiles.Count; i++)
                {
                    string name = Path.GetFileNameWithoutExtension(_availableSceneFiles[i]);
                    if (!string.Equals(name, _data.SceneId, StringComparison.OrdinalIgnoreCase))
                    {
                        sceneNames.Add(name);
                    }
                }

                int selIdx = 0;
                if (!string.IsNullOrEmpty(_data.NextSceneId))
                {
                    selIdx = sceneNames.FindIndex(s => string.Equals(s, _data.NextSceneId, StringComparison.OrdinalIgnoreCase));
                    if (selIdx < 0) selIdx = 0;
                }

                if (GUILayout.Button("◀", GUILayout.Width(22)))
                {
                    selIdx = (selIdx - 1 + sceneNames.Count) % sceneNames.Count;
                    _data.NextSceneId = selIdx == 0 ? "" : sceneNames[selIdx];
                }
                string currentTargetLabel = string.IsNullOrEmpty(_data.NextSceneId) ? "(Auto)" : _data.NextSceneId;
                GUILayout.Label($"<b>{currentTargetLabel}</b>", GUILayout.Width(140));
                if (GUILayout.Button("▶", GUILayout.Width(22)))
                {
                    selIdx = (selIdx + 1) % sceneNames.Count;
                    _data.NextSceneId = selIdx == 0 ? "" : sceneNames[selIdx];
                }
            }

            string autoNext = GetAutoNextScenarioId();
            string hint = string.IsNullOrEmpty(_data.NextSceneId)
                ? (string.IsNullOrEmpty(autoNext) ? "<color=grey>(Dernière scène — aucune suite)</color>" : $"<color=cyan>(Auto : {autoNext})</color>")
                : "<color=#88DDAA>(Cible explicite)</color>";
            GUILayout.Label(hint);
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();

            GUILayout.Space(6);

            // 3. DÉPLOIEMENT RUNTIME
            GUI.backgroundColor = new Color(1.0f, 0.55f, 0.1f);
            if (GUILayout.Button("🚀 DÉPLOYER & TESTER CETTE SCÈNE EN DIRECT DANS LA VUE 3D", GUILayout.Height(38)))
            {
                DeployCurrentSceneToRuntime();
            }
            GUI.backgroundColor = Color.white;

            GUILayout.Space(6);

            // 4. SCÈNES DÉTECTÉES SUR DISQUE (Glisser-Déposer / Réorganisation)
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>Scènes Détectées sur Disque ({_availableSceneFiles.Count})</b>");
            GUILayout.Label("<color=grey>(Glisser-déposer ⠿ pour réordonner la liste)</color>");
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("🔄 Actualiser le catalogue", GUILayout.Width(170)))
            {
                RefreshCatalogCache();
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(2);

            Event evt = Event.current;

            for (int i = 0; i < _availableSceneFiles.Count; i++)
            {
                string filePath = _availableSceneFiles[i];
                string fName = Path.GetFileName(filePath);
                bool isActive = !string.IsNullOrEmpty(_activeFilePath)
                    ? string.Equals(filePath, _activeFilePath, StringComparison.OrdinalIgnoreCase)
                    : string.Equals(Path.GetFileNameWithoutExtension(filePath), _data.SceneId, StringComparison.OrdinalIgnoreCase);

                bool isBeingDragged = _isDraggingSceneItem && _draggedSceneIndex == i;
                bool isDropTarget = _isDraggingSceneItem && _dropTargetSceneIndex == i;

                if (isDropTarget)
                {
                    DrawDropInsertionLine();
                }

                Color prevGuiCol = GUI.color;
                if (isBeingDragged)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.35f);
                }

                GUILayout.BeginHorizontal(isActive ? GUI.skin.box : GUI.skin.textArea);

                // Poignée de glisser-déposer
                int handleControlId = GUIUtility.GetControlID(FocusType.Passive);
                Rect handleRect = GUILayoutUtility.GetRect(new GUIContent(" ⠿ "), GUI.skin.label, GUILayout.Width(24), GUILayout.Height(22));
                GUI.Label(handleRect, isBeingDragged ? "<color=#00E5FF><b> ⠿ </b></color>" : "<color=#FFD700><b> ⠿ </b></color>");

                // Ordre dans la liste
                GUILayout.Label($"<color=grey>#{i + 1}</color>", GUILayout.Width(26));

                // Statut actif et nom de fichier
                string activeMarker = isActive ? "<color=#00E5FF>● [ACTIVE]</color> " : "○ ";
                GUILayout.Label($"{activeMarker}<b>{fName}</b>", GUILayout.Width(230));

                if (File.Exists(filePath))
                {
                    var fileInfo = new FileInfo(filePath);
                    GUILayout.Label($"<color=grey>{fileInfo.Length / 1024f:0.0} Ko — {fileInfo.LastWriteTime:dd/MM HH:mm}</color>", GUILayout.Width(160));
                }

                GUILayout.FlexibleSpace();

                // Boutons de déplacement rapide ▲ / ▼
                GUI.enabled = i > 0 && !_isDraggingSceneItem;
                if (GUILayout.Button("▲", GUILayout.Width(22), GUILayout.Height(20)))
                {
                    MoveSceneInList(i, i - 1);
                }
                GUI.enabled = i < _availableSceneFiles.Count - 1 && !_isDraggingSceneItem;
                if (GUILayout.Button("▼", GUILayout.Width(22), GUILayout.Height(20)))
                {
                    MoveSceneInList(i, i + 1);
                }
                GUI.enabled = true;

                GUILayout.Space(6);

                // Bouton Ouvrir / En cours
                if (!isActive)
                {
                    GUI.backgroundColor = new Color(0.2f, 0.75f, 1f);
                    if (GUILayout.Button("Ouvrir", GUILayout.Width(65), GUILayout.Height(20)))
                    {
                        LoadSceneFromPath(filePath);
                    }
                    GUI.backgroundColor = Color.white;
                }
                else
                {
                    GUILayout.Label("<color=#7CFF9B><b>En cours</b></color>", GUILayout.Width(65));
                }

                GUILayout.EndHorizontal();
                GUI.color = prevGuiCol;

                Rect rowRect = GUILayoutUtility.GetLastRect();
                if (_sceneRowRects.Count <= i)
                    _sceneRowRects.Add(rowRect);
                else
                    _sceneRowRects[i] = rowRect;

                // Clic pour démarrer le drag sur la poignée ou le début de la ligne
                if (evt.type == EventType.MouseDown && evt.button == 0)
                {
                    if (handleRect.Contains(evt.mousePosition) || (rowRect.Contains(evt.mousePosition) && evt.mousePosition.x < rowRect.x + 280f))
                    {
                        GUIUtility.hotControl = handleControlId;
                        _draggedSceneIndex = i;
                        _dropTargetSceneIndex = i;
                        _dragSceneMouseStart = evt.mousePosition;
                        _isDraggingSceneItem = false;
                        evt.Use();
                    }
                }
            }

            // Indicateur de drop en fin de liste
            if (_isDraggingSceneItem && _dropTargetSceneIndex == _availableSceneFiles.Count)
            {
                DrawDropInsertionLine();
            }

            GUILayout.EndVertical();

            // Traitement du glisser-déposer
            if (_draggedSceneIndex >= 0)
            {
                if (evt.type == EventType.MouseDrag && evt.button == 0)
                {
                    if (!_isDraggingSceneItem && Vector2.Distance(evt.mousePosition, _dragSceneMouseStart) > 4f)
                    {
                        _isDraggingSceneItem = true;
                    }

                    if (_isDraggingSceneItem)
                    {
                        UpdateDropTargetIndex(evt.mousePosition.y);
                        evt.Use();
                    }
                }
                else if (evt.type == EventType.MouseUp || evt.rawType == EventType.MouseUp)
                {
                    if (_isDraggingSceneItem && _draggedSceneIndex >= 0 && _dropTargetSceneIndex >= 0)
                    {
                        int fromIdx = _draggedSceneIndex;
                        int toIdx = _dropTargetSceneIndex;
                        int targetInsertIdx = toIdx > fromIdx ? toIdx - 1 : toIdx;
                        if (targetInsertIdx != fromIdx && targetInsertIdx >= 0 && targetInsertIdx < _availableSceneFiles.Count)
                        {
                            MoveSceneInList(fromIdx, targetInsertIdx);
                        }
                        evt.Use();
                    }
                    GUIUtility.hotControl = 0;
                    _isDraggingSceneItem = false;
                    _draggedSceneIndex = -1;
                    _dropTargetSceneIndex = -1;
                }

                // Vignette d'information flottante sous le curseur
                if (_isDraggingSceneItem && _draggedSceneIndex >= 0 && _draggedSceneIndex < _availableSceneFiles.Count)
                {
                    string draggedName = Path.GetFileName(_availableSceneFiles[_draggedSceneIndex]);
                    Rect badgeRect = new Rect(evt.mousePosition.x + 14f, evt.mousePosition.y - 12f, 250f, 26f);
                    Color prevCol = GUI.color;
                    GUI.color = new Color(0.08f, 0.12f, 0.18f, 0.95f);
                    GUI.DrawTexture(badgeRect, PureWhiteTex);
                    GUI.color = Color.cyan;
                    GUI.Box(badgeRect, $" ↕ <b>{draggedName}</b>");
                    GUI.color = prevCol;
                }
            }

            GUILayout.Space(6);

            // 5. APERÇU JSON BRUT (DÉPLIABLE)
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            _showRawJsonPreview = GUILayout.Toggle(_showRawJsonPreview, _showRawJsonPreview ? "▼ Masquer l'Aperçu JSON brut" : "▶ Afficher l'Aperçu JSON brut", GUI.skin.button, GUILayout.Height(22));
            GUILayout.EndHorizontal();

            if (_showRawJsonPreview)
            {
                GUILayout.Space(4);
                string preview = JsonUtility.ToJson(_data, true);
                GUILayout.TextArea(preview, GUILayout.Height(170));
            }
            GUILayout.EndVertical();
        }

        private void DrawActorsTab()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>Acteurs Déployés ({_data.Actors.Count})</b>");
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("+ Ajouter un Acteur", GUILayout.Width(150)))
            {
                _data.Actors.Add(new SceneActorSpawnData
                {
                    ActorId = $"actor_{_data.Actors.Count + 1}",
                    DisplayName = "Nouvel Acteur",
                    Q = 0,
                    R = 0
                });
                _selectedActorIndex = _data.Actors.Count - 1;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            for (int i = 0; i < _data.Actors.Count; i++)
            {
                var actor = _data.Actors[i];
                bool isSelected = (_selectedActorIndex == i);

                GUILayout.BeginVertical(isSelected ? GUI.skin.box : GUI.skin.textArea);
                GUILayout.BeginHorizontal();

                string factionTag = actor.IsPlayer ? "<color=#00E5FF>[PJ]</color>" : "<color=#FF5555>[PNJ]</color>";
                if (GUILayout.Toggle(isSelected, $"{factionTag} <b>{actor.DisplayName}</b> (<i>{actor.ActorId}</i>) — Hex ({actor.Q}, {actor.R})"))
                {
                    _selectedActorIndex = i;
                }

                GUI.backgroundColor = new Color(1f, 0.35f, 0.35f);
                if (GUILayout.Button("✕", GUILayout.Width(26), GUILayout.Height(18)))
                {
                    _data.Actors.RemoveAt(i);
                    break;
                }
                GUI.backgroundColor = Color.white;
                GUILayout.EndHorizontal();

                if (isSelected)
                {
                    DrawActorEditor(actor);
                }
                GUILayout.EndVertical();
                GUILayout.Space(2);
            }
        }

        private void DrawActorEditor(SceneActorSpawnData actor)
        {
            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUILayout.Label("ID Script :", GUILayout.Width(90));
            actor.ActorId = GUILayout.TextField(actor.ActorId, GUILayout.Width(140));
            GUILayout.Label("Nom Affiché :", GUILayout.Width(90));
            actor.DisplayName = GUILayout.TextField(actor.DisplayName);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Position Hex Q :", GUILayout.Width(90));
            int.TryParse(GUILayout.TextField(actor.Q.ToString(), GUILayout.Width(45)), out actor.Q);
            GUILayout.Label("R :", GUILayout.Width(20));
            int.TryParse(GUILayout.TextField(actor.R.ToString(), GUILayout.Width(45)), out actor.R);
            GUILayout.Label("Orientation (°) :", GUILayout.Width(95));
            float.TryParse(GUILayout.TextField(actor.FacingAngle.ToString("0"), GUILayout.Width(45)), out actor.FacingAngle);
            actor.IsPlayer = GUILayout.Toggle(actor.IsPlayer, "Joueur (Allié)");
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Fiche Personnage :", GUILayout.Width(120));
            int charIdx = Mathf.Max(0, _availableCharacterFiles.IndexOf(actor.CharacterSheetFileName));
            if (GUILayout.Button("◀", GUILayout.Width(24)))
            {
                charIdx = (charIdx - 1 + _availableCharacterFiles.Count) % _availableCharacterFiles.Count;
                actor.CharacterSheetFileName = charIdx == 0 ? "" : _availableCharacterFiles[charIdx];
            }
            string charDisplay = string.IsNullOrEmpty(actor.CharacterSheetFileName) ? "(Défaut)" : actor.CharacterSheetFileName;
            GUILayout.Label($"<b>{charDisplay}</b>", GUILayout.Width(160));
            if (GUILayout.Button("▶", GUILayout.Width(24)))
            {
                charIdx = (charIdx + 1) % _availableCharacterFiles.Count;
                actor.CharacterSheetFileName = charIdx == 0 ? "" : _availableCharacterFiles[charIdx];
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Modèle 3D :", GUILayout.Width(90));
            actor.ModelPrefabName = GUILayout.TextField(actor.ModelPrefabName, GUILayout.Width(140));
            GUILayout.Label("Arme Équipée :", GUILayout.Width(95));
            actor.EquippedWeaponName = GUILayout.TextField(actor.EquippedWeaponName);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            actor.SpawnInitially = GUILayout.Toggle(actor.SpawnInitially, "Apparition Initiale", GUILayout.Width(140));
            GUILayout.Label("Si Nœud ID :", GUILayout.Width(80));
            actor.SpawnOnNodeId = GUILayout.TextField(actor.SpawnOnNodeId ?? "", GUILayout.Width(140));
            GUILayout.EndHorizontal();

            actor.EmbeddedSheet ??= new CharacterSheet();
            var attr = actor.EmbeddedSheet.BaseAttributes;
            GUILayout.BeginHorizontal();
            GUILayout.Label("FOR:", GUILayout.Width(32));
            int.TryParse(GUILayout.TextField(attr.Force.ToString(), GUILayout.Width(28)), out attr.Force);
            GUILayout.Label("AGI:", GUILayout.Width(30));
            int.TryParse(GUILayout.TextField(attr.Agilite.ToString(), GUILayout.Width(28)), out attr.Agilite);
            GUILayout.Label("CON:", GUILayout.Width(32));
            int.TryParse(GUILayout.TextField(attr.Constitution.ToString(), GUILayout.Width(28)), out attr.Constitution);
            GUILayout.Label("RAP:", GUILayout.Width(30));
            int.TryParse(GUILayout.TextField(attr.Rapidite.ToString(), GUILayout.Width(28)), out attr.Rapidite);
            GUILayout.Label("INT:", GUILayout.Width(28));
            int.TryParse(GUILayout.TextField(attr.Intelligence.ToString(), GUILayout.Width(28)), out attr.Intelligence);
            GUILayout.Label("ERU:", GUILayout.Width(30));
            int.TryParse(GUILayout.TextField(attr.Erudition.ToString(), GUILayout.Width(28)), out attr.Erudition);
            GUILayout.Label("CHA:", GUILayout.Width(30));
            int.TryParse(GUILayout.TextField(attr.Charisme.ToString(), GUILayout.Width(28)), out attr.Charisme);
            GUILayout.Label("INS:", GUILayout.Width(28));
            int.TryParse(GUILayout.TextField(attr.Instinct.ToString(), GUILayout.Width(28)), out attr.Instinct);
            GUILayout.Label("MAG:", GUILayout.Width(32));
            int.TryParse(GUILayout.TextField(attr.Magie.ToString(), GUILayout.Width(28)), out attr.Magie);
            actor.EmbeddedSheet.BaseAttributes = attr;
            GUILayout.EndHorizontal();
        }

        private void DrawInteractablesTab()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>Éléments de Décor Interactifs ({_data.Interactables.Count})</b>");
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("+ Ajouter Interactable", GUILayout.Width(160)))
            {
                _data.Interactables.Add(new SceneInteractableSpawnData
                {
                    InteractableId = $"obj_{_data.Interactables.Count + 1}",
                    DisplayName = "Terminal d'Accès",
                    Q = 0,
                    R = 0
                });
                _selectedInteractableIndex = _data.Interactables.Count - 1;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            for (int i = 0; i < _data.Interactables.Count; i++)
            {
                var item = _data.Interactables[i];
                bool isSelected = (_selectedInteractableIndex == i);

                GUILayout.BeginVertical(isSelected ? GUI.skin.box : GUI.skin.textArea);
                GUILayout.BeginHorizontal();

                if (GUILayout.Toggle(isSelected, $"🔧 <b>{item.DisplayName}</b> [{item.ActionLabel}] — Hex ({item.Q}, {item.R})"))
                {
                    _selectedInteractableIndex = i;
                }

                GUI.backgroundColor = new Color(1f, 0.35f, 0.35f);
                if (GUILayout.Button("✕", GUILayout.Width(26), GUILayout.Height(18)))
                {
                    _data.Interactables.RemoveAt(i);
                    break;
                }
                GUI.backgroundColor = Color.white;
                GUILayout.EndHorizontal();

                if (isSelected)
                {
                    DrawInteractableEditor(item);
                }
                GUILayout.EndVertical();
                GUILayout.Space(2);
            }
        }

        private void DrawInteractableEditor(SceneInteractableSpawnData item)
        {
            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUILayout.Label("ID :", GUILayout.Width(60));
            item.InteractableId = GUILayout.TextField(item.InteractableId, GUILayout.Width(130));
            GUILayout.Label("Nom :", GUILayout.Width(45));
            item.DisplayName = GUILayout.TextField(item.DisplayName);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Action :", GUILayout.Width(60));
            item.ActionLabel = GUILayout.TextField(item.ActionLabel, GUILayout.Width(130));
            GUILayout.Label("Hex Q :", GUILayout.Width(45));
            int.TryParse(GUILayout.TextField(item.Q.ToString(), GUILayout.Width(40)), out item.Q);
            GUILayout.Label("R :", GUILayout.Width(20));
            int.TryParse(GUILayout.TextField(item.R.ToString(), GUILayout.Width(40)), out item.R);
            GUILayout.Label("Rayon :", GUILayout.Width(50));
            int.TryParse(GUILayout.TextField(item.Radius.ToString(), GUILayout.Width(35)), out item.Radius);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Compétence Requise :", GUILayout.Width(140));
            int skillCount = Enum.GetValues(typeof(SkillType)).Length;
            int skillIdx = (int)item.RequiredSkill;
            if (GUILayout.Button("◀", GUILayout.Width(24)))
            {
                skillIdx = (skillIdx - 1 + skillCount) % skillCount;
                item.RequiredSkill = (SkillType)skillIdx;
            }
            GUILayout.Label($"<b>{SkillDefinitions.GetDisplayName(item.RequiredSkill)}</b>", GUILayout.Width(160));
            if (GUILayout.Button("▶", GUILayout.Width(24)))
            {
                skillIdx = (skillIdx + 1) % skillCount;
                item.RequiredSkill = (SkillType)skillIdx;
            }
            GUILayout.Label("Seuil SD :", GUILayout.Width(60));
            int.TryParse(GUILayout.TextField(item.SkillThreshold.ToString(), GUILayout.Width(40)), out item.SkillThreshold);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Objectif Débloqué (ID) :", GUILayout.Width(150));
            item.CompletionObjectiveId = GUILayout.TextField(item.CompletionObjectiveId);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Message Succès :", GUILayout.Width(110));
            item.SuccessLog = GUILayout.TextField(item.SuccessLog);
            GUILayout.EndHorizontal();
        }

        private void DrawNodesAndDialoguesTab()
        {
            if (_data.Nodes.Count == 0)
            {
                if (GUILayout.Button("+ Créer le premier Nœud de Scène", GUILayout.Height(36)))
                {
                    _data.Nodes.Add(new SceneNodeData { NodeId = "node_1", Title = "Départ", GraphPosX = 60f, GraphPosY = 60f });
                }
                return;
            }

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();

            // # de la scène tel qu'affiché dans la liste "Scènes Détectées sur Disque" (onglet Scène & Fichier : #{i+1}).
            int activeSceneIdx = -1;
            if (_availableSceneFiles.Count > 0)
            {
                activeSceneIdx = !string.IsNullOrEmpty(_activeFilePath)
                    ? _availableSceneFiles.FindIndex(f => string.Equals(f, _activeFilePath, StringComparison.OrdinalIgnoreCase))
                    : _availableSceneFiles.FindIndex(f => string.Equals(Path.GetFileNameWithoutExtension(f), _data.SceneId, StringComparison.OrdinalIgnoreCase));
            }

            if (_availableSceneFiles.Count > 1)
            {
                int navIdx = activeSceneIdx >= 0 ? activeSceneIdx : 0;
                if (GUILayout.Button("◀", GUILayout.Width(24), GUILayout.Height(22)))
                {
                    navIdx = (navIdx - 1 + _availableSceneFiles.Count) % _availableSceneFiles.Count;
                    LoadSceneFromPath(_availableSceneFiles[navIdx]);
                }
                if (GUILayout.Button("▶", GUILayout.Width(24), GUILayout.Height(22)))
                {
                    navIdx = (navIdx + 1) % _availableSceneFiles.Count;
                    LoadSceneFromPath(_availableSceneFiles[navIdx]);
                }
            }

            string sceneNumPrefix = activeSceneIdx >= 0 ? $"#{activeSceneIdx + 1} " : "";
            GUILayout.Label($"<b>■ SCÈNE {sceneNumPrefix}: {_data.Title}</b> (<color=#00E5FF>{_data.Nodes.Count} Nœuds</color>)", GUILayout.ExpandWidth(true));

            GUI.backgroundColor = new Color(0.2f, 0.75f, 1f);
            if (GUILayout.Button("+ Nouveau Nœud", GUILayout.Width(120), GUILayout.Height(22)))
            {
                ComputeNewNodeSpawn(out float newX, out float newY);
                _data.Nodes.Add(new SceneNodeData
                {
                    NodeId = $"node_{_data.Nodes.Count + 1}",
                    Title = "Nouveau Nœud",
                    GraphPosX = newX,
                    GraphPosY = newY
                });
            }
            GUI.backgroundColor = new Color(0.2f, 0.92f, 0.45f);
            if (GUILayout.Button("+ Nœud Objectif", GUILayout.Width(120), GUILayout.Height(22)))
            {
                ComputeNewNodeSpawn(out float newX, out float newY);
                var objNode = new SceneNodeData
                {
                    NodeId = $"node_{_data.Nodes.Count + 1}",
                    Title = "Nouvel Objectif",
                    Kind = ScenarioNodeKind.Objective,
                    Body = "Texte descriptif ou récapitulatif du segment...",
                    GraphPosX = newX,
                    GraphPosY = newY
                };
                objNode.Objectives.Add(new ScenarioObjective { Id = "obj_1", Label = "Objectif principal...", Optional = false });
                objNode.Objectives.Add(new ScenarioObjective { Id = "obj_2", Label = "Objectif optionnel...", Optional = true });
                _data.Nodes.Add(objNode);
            }
            GUI.backgroundColor = Color.white;

            GUILayout.Space(6);
            GUI.backgroundColor = new Color(1f, 0.78f, 0.25f);
            if (GUILayout.Button("+ Déclencheur", GUILayout.Width(110), GUILayout.Height(22)))
            {
                if (_data.Triggers == null) _data.Triggers = new System.Collections.Generic.List<SceneTriggerData>();
                Vector2 viewCenter = new Vector2(_lastCanvasSize.x * 0.5f, _lastCanvasSize.y * 0.5f);
                Vector2 worldCenter = (viewCenter - _graphPan) / Mathf.Max(0.01f, _zoom);
                _data.Triggers.Add(new SceneTriggerData
                {
                    TriggerId = $"trig_{_data.Triggers.Count + 1}",
                    Label = "Nouveau déclencheur",
                    GraphPosX = worldCenter.x - 170f,
                    GraphPosY = worldCenter.y - 100f
                });
            }
            GUI.backgroundColor = Color.white;

            GUILayout.Space(6);
            GUI.backgroundColor = new Color(0.15f, 0.85f, 0.5f);
            if (GUILayout.Button("↺ Auto-Disposition", GUILayout.Width(140), GUILayout.Height(22)))
            {
                AutoLayoutGlobalScene();
            }

            GUILayout.Space(4);
            GUI.backgroundColor = new Color(0.55f, 0.85f, 1f);
            if (GUILayout.Button("▦ Mosaïque", GUILayout.Width(95), GUILayout.Height(22)))
            {
                AutoLayoutMosaic();
            }

            GUI.backgroundColor = new Color(1.0f, 0.75f, 0.2f);
            if (GUILayout.Button("🔍 Cadrer Tout", GUILayout.Width(105), GUILayout.Height(22)))
            {
                FocusAllNodes();
            }
            GUI.backgroundColor = Color.white;
            GUILayout.Space(6);
            _showNodeInspector = GUILayout.Toggle(_showNodeInspector, "🔧 Inspecteur", GUILayout.Width(105), GUILayout.Height(22));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>Aller à :</b>", GUILayout.Width(80));
            for (int q = 0; q < Mathf.Min(10, _data.Nodes.Count); q++)
            {
                var targetNode = _data.Nodes[q];
                if (GUILayout.Button($"#{q + 1}", GUILayout.Width(28), GUILayout.Height(20)))
                {
                    FocusNodeByIndex(q);
                }
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label("<color=grey>Molette = Zoom  ·  Clic Droit = Pan  ·  Double-clic nœud = cadrer  ·  Survol carte = lire</color>");
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            Rect canvasRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.ExpandWidth(true), GUILayout.MinHeight(380f), GUILayout.ExpandHeight(true));
            if (canvasRect.width < 100f || canvasRect.height < 100f)
            {
                // Premier passage de layout : GetRect retourne 1x1. On saute la frame
                // au lieu d'inventer un faux rect (qui désynchronisait pan/zoom et saddle les nœuds en haut-gauche).
                return;
            }

            if (!_hasInitializedLayout)
            {
                _hasInitializedLayout = true;
                if (_data.Nodes.Count > 0)
                {
                    if (CheckIfSceneNeedsInitialLayout(_data))
                    {
                        AutoLayoutMosaic(canvasRect);
                    }
                    else
                    {
                        FocusAllNodes(canvasRect);
                    }
                    _focusedNodeIndex = 0;
                }
            }
            else if (_lastCanvasSize.x > 100f && _lastCanvasSize.y > 100f)
            {
                // Fenêtre redimensionnée : on ré-applique l'intention de cadrage.
                // - Vue d'ensemble (zoom <= zoom de cadrage) → on recadre tout à la nouvelle taille.
                // - Nœud focalisé → on le recentre en gardant SON zoom d'édition.
                // - Zoom manuel rapproché → on garde zoom + centre monde (pas de yank).
                // Le pan seul ne désarme plus jamais le recadrage (c'était le bug : un simple
                // clic droit suffisait à figer une vue ensuite tronquée au redimensionnement).
                float dw = Mathf.Abs(canvasRect.width - _lastCanvasSize.x);
                float dh = Mathf.Abs(canvasRect.height - _lastCanvasSize.y);
                if (dw > canvasRect.width * 0.04f || dh > canvasRect.height * 0.04f)
                {
                    if (_data.Nodes.Count == 0) { /* rien à cadrer */ }
                    else if (_lastViewWasAll && _zoom <= _fitZoom + 0.02f) FocusAllNodes(canvasRect);
                    else if (!_lastViewWasAll && _focusedNodeIndex >= 0 && _focusedNodeIndex < _data.Nodes.Count) RecenterNode(_data.Nodes[_focusedNodeIndex], canvasRect);
                    else
                    {
                        Vector2 oldC = new Vector2(_lastCanvasSize.x * 0.5f, _lastCanvasSize.y * 0.5f);
                        Vector2 worldC = (oldC - _graphPan) / _zoom;
                        Vector2 newC = new Vector2(canvasRect.width * 0.5f, canvasRect.height * 0.5f);
                        _graphPan = newC - worldC * _zoom;
                        ClampPanToContent(canvasRect);
                    }
                }
            }

            DrawNodeGraphCanvas(canvasRect);
        }

        private static bool CheckIfSceneNeedsInitialLayout(StorySceneData data)
        {
            if (data == null || data.Nodes == null || data.Nodes.Count == 0) return false;
            for (int i = 0; i < data.Nodes.Count; i++)
            {
                var n = data.Nodes[i];
                if (n == null) continue;
                if (n.GraphPosX <= 0f && n.GraphPosY <= 0f) return true;
            }
            return false;
        }

        private static void LayoutCardsInsideNode(SceneNodeData node)
        {
            if (node == null) return;
            float cardStartX = node.GraphPosX + 20f;
            float cardStartY = node.GraphPosY + 48f;
            const int maxCardsPerRow = 3;
            const float gapCardsX = 18f;
            const float gapCardsY = 18f;

            float curY = cardStartY;
            int totalDialogues = node.Dialogues != null ? node.Dialogues.Count : 0;

            for (int rowStart = 0; rowStart < totalDialogues; rowStart += maxCardsPerRow)
            {
                int rowEnd = Mathf.Min(rowStart + maxCardsPerRow, totalDialogues);
                float rowHeight = 0f;
                for (int d = rowStart; d < rowEnd; d++)
                {
                    Vector2 s = GetDialogueCardSize(node.Dialogues[d]);
                    rowHeight = Mathf.Max(rowHeight, s.y);
                }

                for (int d = rowStart; d < rowEnd; d++)
                {
                    int col = d - rowStart;
                    node.Dialogues[d].GraphPosX = cardStartX + col * (DlgCardDefaultW + gapCardsX);
                    node.Dialogues[d].GraphPosY = curY;
                }

                curY += rowHeight + gapCardsY;
            }

            int totalEvents = node.Events != null ? node.Events.Count : 0;
            float curEvtY = curY;
            if (totalEvents > 0)
            {
                float evtStartY = (totalDialogues > 0) ? curY + 10f : cardStartY;
                curEvtY = evtStartY;

                for (int rowStart = 0; rowStart < totalEvents; rowStart += maxCardsPerRow)
                {
                    int rowEnd = Mathf.Min(rowStart + maxCardsPerRow, totalEvents);
                    float rowHeight = 0f;
                    for (int e = rowStart; e < rowEnd; e++)
                    {
                        Vector2 s = GetEventCardSize(node.Events[e]);
                        rowHeight = Mathf.Max(rowHeight, s.y);
                    }

                    for (int e = rowStart; e < rowEnd; e++)
                    {
                        int col = e - rowStart;
                        node.Events[e].GraphPosX = cardStartX + col * (EvtCardDefaultW + gapCardsX);
                        node.Events[e].GraphPosY = curEvtY;
                    }

                    curEvtY += rowHeight + 14f;
                }
            }

            int totalCons = node.Consequences != null ? node.Consequences.Count : 0;
            if (totalCons > 0)
            {
                float consStartY = (totalDialogues > 0 || totalEvents > 0) ? curEvtY + 10f : cardStartY;
                float curConsY = consStartY;

                for (int rowStart = 0; rowStart < totalCons; rowStart += maxCardsPerRow)
                {
                    int rowEnd = Mathf.Min(rowStart + maxCardsPerRow, totalCons);
                    float rowHeight = 0f;
                    for (int k = rowStart; k < rowEnd; k++)
                    {
                        Vector2 s = GetConsequenceCardSize(node.Consequences[k]);
                        rowHeight = Mathf.Max(rowHeight, s.y);
                    }

                    for (int k = rowStart; k < rowEnd; k++)
                    {
                        int col = k - rowStart;
                        node.Consequences[k].GraphPosX = cardStartX + col * (ConsCardDefaultW + gapCardsX);
                        node.Consequences[k].GraphPosY = curConsY;
                    }

                    curConsY += rowHeight + 14f;
                }
            }
        }

        // Spot libre pour une NOUVELLE carte : ne touche JAMAIS aux cartes existantes.
        // L'ajout (+ Dialogue / + Événement / + Conséq.) place la carte sous le contenu
        // existant, alignée sur la colonne la plus à gauche, sans regrouper/re-griller
        // les positions manuelles de l'utilisateur. Seuls Auto-Disposition / Mosaïque
        // appellent LayoutCardsInsideNode (regroupement explicite voulu).
        private static void ComputeFreeSpotForNewCard(SceneNodeData node, out float x, out float y)
        {
            float baseX = (node != null ? node.GraphPosX : 60f) + 20f;
            float baseY = (node != null ? node.GraphPosY : 60f) + 48f;
            if (node == null)
            {
                x = baseX;
                y = baseY;
                return;
            }

            bool has = false;
            float minLeft = float.MaxValue;
            float maxBottom = float.MinValue;

            if (node.Dialogues != null)
            {
                for (int i = 0; i < node.Dialogues.Count; i++)
                {
                    var d = node.Dialogues[i];
                    if (d == null) continue;
                    Vector2 s = GetDialogueCardSize(d);
                    minLeft = Mathf.Min(minLeft, d.GraphPosX);
                    maxBottom = Mathf.Max(maxBottom, d.GraphPosY + s.y);
                    has = true;
                }
            }
            if (node.Events != null)
            {
                for (int i = 0; i < node.Events.Count; i++)
                {
                    var e = node.Events[i];
                    if (e == null) continue;
                    Vector2 s = GetEventCardSize(e);
                    minLeft = Mathf.Min(minLeft, e.GraphPosX);
                    maxBottom = Mathf.Max(maxBottom, e.GraphPosY + s.y);
                    has = true;
                }
            }
            if (node.Consequences != null)
            {
                for (int i = 0; i < node.Consequences.Count; i++)
                {
                    var k = node.Consequences[i];
                    if (k == null) continue;
                    Vector2 s = GetConsequenceCardSize(k);
                    minLeft = Mathf.Min(minLeft, k.GraphPosX);
                    maxBottom = Mathf.Max(maxBottom, k.GraphPosY + s.y);
                    has = true;
                }
            }

            if (!has)
            {
                x = baseX;
                y = baseY;
                return;
            }

            x = (minLeft != float.MaxValue) ? minLeft : baseX;
            y = maxBottom + 18f;
        }

        // ------------------------------------------------------------------
        // Entrées / sorties de nœud (routage explicite : vide = fin du nœud).
        // - Carte sans lien entrant = point d'entrée (input vert, plus gros).
        // - Carte sans lien sortant = point de sortie (output principal rouge, plus gros).
        // - Plusieurs entrées dialogue = erreur utilisateur (carré vert, cadre rouge).
        // Comparaisons insensibles à la casse, sans allocation (éditeur 60fps).
        // ------------------------------------------------------------------
        private static bool DialogueIdHasIncoming(SceneNodeData node, string lineId)
        {
            if (node == null || string.IsNullOrEmpty(lineId)) return false;
            if (node.Dialogues != null)
            {
                for (int i = 0; i < node.Dialogues.Count; i++)
                {
                    var l = node.Dialogues[i];
                    if (l == null) continue;
                    if (string.Equals(l.NextLineId, lineId, System.StringComparison.OrdinalIgnoreCase)) return true;
                    if (l.Choices != null)
                    {
                        for (int c = 0; c < l.Choices.Count; c++)
                        {
                            var ch = l.Choices[c];
                            if (ch == null) continue;
                            if (string.Equals(ch.NextLineId, lineId, System.StringComparison.OrdinalIgnoreCase)) return true;
                            var chal = ch.Challenge;
                            if (chal != null && chal.HasChallenge)
                            {
                                if (string.Equals(chal.SuccessNextLineId, lineId, System.StringComparison.OrdinalIgnoreCase)) return true;
                                if (string.Equals(chal.FailureNextLineId, lineId, System.StringComparison.OrdinalIgnoreCase)) return true;
                            }
                        }
                    }
                    if (l.AutoSkillCheck != null && l.AutoSkillCheck.HasAutoCheck)
                    {
                        if (string.Equals(l.AutoSkillCheck.SuccessNextLineId, lineId, System.StringComparison.OrdinalIgnoreCase)) return true;
                        if (string.Equals(l.AutoSkillCheck.FailureNextLineId, lineId, System.StringComparison.OrdinalIgnoreCase)) return true;
                    }
                }
            }
            if (node.Consequences != null)
            {
                for (int k = 0; k < node.Consequences.Count; k++)
                {
                    var cons = node.Consequences[k];
                    if (cons == null) continue;
                    if (string.Equals(cons.NextLineId, lineId, System.StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            return false;
        }

        private static bool ConsIdHasIncoming(SceneNodeData node, string consId)
        {
            if (node == null || string.IsNullOrEmpty(consId)) return false;
            if (node.Dialogues != null)
            {
                for (int i = 0; i < node.Dialogues.Count; i++)
                {
                    var l = node.Dialogues[i];
                    if (l == null || l.Choices == null) continue;
                    for (int c = 0; c < l.Choices.Count; c++)
                    {
                        var ch = l.Choices[c];
                        if (ch == null) continue;
                        if (string.Equals(ch.NextLineId, consId, System.StringComparison.OrdinalIgnoreCase)) return true;
                        var chal = ch.Challenge;
                        if (chal != null && chal.HasChallenge)
                        {
                            if (string.Equals(chal.SuccessConsequenceId, consId, System.StringComparison.OrdinalIgnoreCase)) return true;
                            if (string.Equals(chal.FailureConsequenceId, consId, System.StringComparison.OrdinalIgnoreCase)) return true;
                        }
                    }
                }
            }
            if (node.Consequences != null)
            {
                for (int k = 0; k < node.Consequences.Count; k++)
                {
                    var cons = node.Consequences[k];
                    if (cons == null) continue;
                    if (string.Equals(cons.NextConsequenceId, consId, System.StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            return false;
        }

        private static bool EventIdHasIncoming(SceneNodeData node, string evtId)
        {
            if (node == null || string.IsNullOrEmpty(evtId) || node.Events == null) return false;
            for (int i = 0; i < node.Events.Count; i++)
            {
                var e = node.Events[i];
                if (e == null) continue;
                if (string.Equals(e.NextEventId, evtId, System.StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static int CountDialogueEntries(SceneNodeData node)
        {
            if (node == null || node.Dialogues == null) return 0;
            int count = 0;
            for (int i = 0; i < node.Dialogues.Count; i++)
            {
                var l = node.Dialogues[i];
                if (l == null || string.IsNullOrEmpty(l.LineId)) continue;
                if (!DialogueIdHasIncoming(node, l.LineId)) count++;
            }
            return count;
        }

        private static bool DialogueHasOutgoing(SceneDialogueLineData line)
        {
            if (line == null) return false;
            if (!string.IsNullOrEmpty(line.NextLineId)) return true;
            if (line.Choices != null)
            {
                for (int c = 0; c < line.Choices.Count; c++)
                {
                    var ch = line.Choices[c];
                    if (ch == null) continue;
                    if (!string.IsNullOrEmpty(ch.NextLineId)) return true;
                    var chal = ch.Challenge;
                    if (chal != null && chal.HasChallenge)
                    {
                        if (!string.IsNullOrEmpty(chal.SuccessNextLineId)) return true;
                        if (!string.IsNullOrEmpty(chal.FailureNextLineId)) return true;
                        if (!string.IsNullOrEmpty(chal.SuccessConsequenceId)) return true;
                        if (!string.IsNullOrEmpty(chal.FailureConsequenceId)) return true;
                    }
                }
            }
            if (line.AutoSkillCheck != null && line.AutoSkillCheck.HasAutoCheck)
            {
                if (!string.IsNullOrEmpty(line.AutoSkillCheck.SuccessNextLineId)) return true;
                if (!string.IsNullOrEmpty(line.AutoSkillCheck.FailureNextLineId)) return true;
            }
            return false;
        }

        private static bool ConsHasOutgoing(SceneConsequenceData cons)
        {
            if (cons == null) return false;
            return !string.IsNullOrEmpty(cons.NextLineId) || !string.IsNullOrEmpty(cons.NextConsequenceId);
        }

        private static bool EventHasOutgoing(SceneEventData ev)
        {
            if (ev == null) return false;
            return !string.IsNullOrEmpty(ev.NextEventId);
        }

        private static readonly Color EntryGreen = new Color(0.2f, 1f, 0.4f);
        private static readonly Color ExitRed = new Color(1f, 0.25f, 0.25f);

        // Input : normal 14px couleur d'origine, entrée = carré vert 20px,
        // erreur multi-entrées = cadre rouge 24px autour du carré vert.
        private void DrawEntryInputPort(Rect cardRect, Color normalColor, bool isEntry, bool entryError)
        {
            if (!isEntry)
            {
                DrawGraphSolidRect(new Rect(cardRect.x - 7f, cardRect.y + 19f, 14f, 14f), normalColor);
                return;
            }
            if (entryError)
                DrawGraphSolidRect(new Rect(cardRect.x - 12f, cardRect.y + 14f, 24f, 24f), ExitRed);
            DrawGraphSolidRect(new Rect(cardRect.x - 10f, cardRect.y + 16f, 20f, 20f), EntryGreen);
        }

        private bool IsMouseOverAnyNodeOrCard(Vector2 mouseWorld)
        {
            for (int n = 0; n < _data.Nodes.Count; n++)
            {
                var node = _data.Nodes[n];
                Rect nodeBox = ComputeNodeBoundingBox(node);
                if (nodeBox.Contains(mouseWorld)) return true;

                for (int d = 0; d < node.Dialogues.Count; d++)
                {
                    var dlg = node.Dialogues[d];
                    Vector2 s = GetDialogueCardSize(dlg);
                    if (new Rect(dlg.GraphPosX, dlg.GraphPosY, s.x, s.y).Contains(mouseWorld)) return true;
                }

                for (int e = 0; e < node.Events.Count; e++)
                {
                    var ev = node.Events[e];
                    Vector2 s = GetEventCardSize(ev);
                    if (new Rect(ev.GraphPosX, ev.GraphPosY, s.x, s.y).Contains(mouseWorld)) return true;
                }

                if (node.Consequences != null)
                {
                    for (int k = 0; k < node.Consequences.Count; k++)
                    {
                        var cs = node.Consequences[k];
                        if (cs == null) continue;
                        Vector2 s = GetConsequenceCardSize(cs);
                        if (new Rect(cs.GraphPosX, cs.GraphPosY, s.x, s.y).Contains(mouseWorld)) return true;
                    }
                }
            }
            if (_data.Triggers != null)
            {
                for (int t = 0; t < _data.Triggers.Count; t++)
                {
                    var trg = _data.Triggers[t];
                    if (trg == null) continue;
                    Vector2 s = GetTriggerCardSize(trg);
                    if (new Rect(trg.GraphPosX, trg.GraphPosY, s.x, s.y).Contains(mouseWorld)) return true;
                }
            }
            return false;
        }

        private System.Collections.Generic.Dictionary<string, int> ComputeNodeDepths()
        {
            var depths = new System.Collections.Generic.Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
            if (_data == null || _data.Nodes == null || _data.Nodes.Count == 0) return depths;

            string rootId = _data.FirstNodeId;
            if (string.IsNullOrEmpty(rootId) || !_data.Nodes.Exists(n => n != null && string.Equals(n.NodeId, rootId, System.StringComparison.OrdinalIgnoreCase)))
            {
                rootId = _data.Nodes[0].NodeId;
            }

            var queue = new System.Collections.Generic.Queue<string>();
            depths[rootId] = 0;
            queue.Enqueue(rootId);

            while (queue.Count > 0)
            {
                string currentId = queue.Dequeue();
                int depth = depths[currentId];
                var node = _data.Nodes.Find(x => x != null && string.Equals(x.NodeId, currentId, System.StringComparison.OrdinalIgnoreCase));
                if (node == null) continue;

                var targets = new System.Collections.Generic.List<string>();
                if (!string.IsNullOrEmpty(node.NextNodeId)) targets.Add(node.NextNodeId);
                if (node.Choices != null)
                {
                    for (int c = 0; c < node.Choices.Count; c++)
                        if (node.Choices[c] != null && !string.IsNullOrEmpty(node.Choices[c].NextNodeId))
                            targets.Add(node.Choices[c].NextNodeId);
                }
                if (node.Dialogues != null)
                {
                    for (int d = 0; d < node.Dialogues.Count; d++)
                    {
                        var dlg = node.Dialogues[d];
                        if (dlg == null) continue;
                        if (!string.IsNullOrEmpty(dlg.NextLineId) && _data.Nodes.Exists(n => n != null && string.Equals(n.NodeId, dlg.NextLineId, System.StringComparison.OrdinalIgnoreCase)))
                            targets.Add(dlg.NextLineId);
                        if (dlg.Choices != null)
                        {
                            for (int dc = 0; dc < dlg.Choices.Count; dc++)
                                if (dlg.Choices[dc] != null && !string.IsNullOrEmpty(dlg.Choices[dc].NextLineId) && _data.Nodes.Exists(n => n != null && string.Equals(n.NodeId, dlg.Choices[dc].NextLineId, System.StringComparison.OrdinalIgnoreCase)))
                                    targets.Add(dlg.Choices[dc].NextLineId);
                        }
                    }
                }

                for (int i = 0; i < targets.Count; i++)
                {
                    if (!depths.ContainsKey(targets[i]))
                    {
                        depths[targets[i]] = depth + 1;
                        queue.Enqueue(targets[i]);
                    }
                }
            }

            int currentMax = 0;
            foreach (var kvp in depths) currentMax = System.Math.Max(currentMax, kvp.Value);

            for (int n = 0; n < _data.Nodes.Count; n++)
            {
                var node = _data.Nodes[n];
                if (node == null || string.IsNullOrEmpty(node.NodeId)) continue;
                if (!depths.ContainsKey(node.NodeId))
                {
                    currentMax++;
                    depths[node.NodeId] = currentMax;
                }
            }

            return depths;
        }

        private void AutoLayoutGlobalScene(Rect? canvasRect = null)
        {
            if (_data.Nodes.Count == 0) return;

            for (int n = 0; n < _data.Nodes.Count; n++)
            {
                var node = _data.Nodes[n];
                EnsureDialogueGraphIntegrity(node);
                StorySceneData.EnsureNodeLists(node);
            }

            // Normalisation immédiate de tous les dialogues internes en 3 colonnes régulières
            for (int n = 0; n < _data.Nodes.Count; n++)
            {
                LayoutCardsInsideNode(_data.Nodes[n]);
            }

            const float startX = 60f;
            const float startY = 60f;
            const float gapX = 50f;
            const float gapY = 28f;

            // Placement structuré en flux 2D compact
            var rootNode = _data.Nodes[0];
            rootNode.GraphPosX = startX;
            rootNode.GraphPosY = startY;
            LayoutCardsInsideNode(rootNode);
            Rect rootBox = ComputeNodeBoundingBox(rootNode);

            float col2X = rootBox.xMax + gapX;
            float col2CurY = startY;
            float maxSceneH = rootBox.height;

            for (int n = 1; n < _data.Nodes.Count; n++)
            {
                var node = _data.Nodes[n];
                node.GraphPosX = col2X;
                node.GraphPosY = col2CurY;
                LayoutCardsInsideNode(node);
                Rect b = ComputeNodeBoundingBox(node);

                col2CurY += b.height + gapY;
                maxSceneH = Mathf.Max(maxSceneH, col2CurY - startY);
            }

            if (_data.Triggers != null && _data.Triggers.Count > 0)
            {
                float trigX = startX;
                float trigY = startY + maxSceneH + 36f;
                for (int t = 0; t < _data.Triggers.Count; t++)
                {
                    var trg = _data.Triggers[t];
                    if (trg == null) continue;
                    trg.GraphPosX = trigX;
                    trg.GraphPosY = trigY;
                    Vector2 s = GetTriggerCardSize(trg);
                    trigX += s.x + 20f;
                }
            }

            _focusedNodeIndex = 0;
            FocusAllNodes(canvasRect);
        }

        private void AutoLayoutMosaic(Rect? canvasRect = null)
        {
            if (_data.Nodes.Count == 0) return;
            Vector2 view = ResolveViewSize(canvasRect);
            float maxRowWidth = Mathf.Max(1800f, view.x / Mathf.Clamp(_zoom, 0.35f, 1f));
            const float gapX = 50f;
            const float gapY = 36f;
            const float startX = 60f;
            const float startY = 60f;

            float curX = startX;
            float curY = startY;
            float currentRowHeight = 0f;
            var placedBoxes = new List<Rect>();

            for (int n = 0; n < _data.Nodes.Count; n++)
            {
                var node = _data.Nodes[n];
                EnsureDialogueGraphIntegrity(node);
                StorySceneData.EnsureNodeLists(node);

                node.GraphPosX = curX;
                node.GraphPosY = curY;
                LayoutCardsInsideNode(node);

                Rect box = ComputeNodeBoundingBox(node);
                if (curX + box.width > startX + maxRowWidth && curX > startX)
                {
                    curX = startX;
                    curY += currentRowHeight + gapY;
                    currentRowHeight = 0f;

                    node.GraphPosX = curX;
                    node.GraphPosY = curY;
                    LayoutCardsInsideNode(node);
                    box = ComputeNodeBoundingBox(node);
                }

                Rect candidate = new Rect(curX, curY, box.width, box.height);
                bool collision;
                int guard = 0;
                do
                {
                    collision = false;
                    for (int p = 0; p < placedBoxes.Count && guard < 64; p++)
                    {
                        if (placedBoxes[p].Overlaps(candidate))
                        {
                            candidate.y = placedBoxes[p].yMax + gapY;
                            collision = true;
                            guard++;
                            break;
                        }
                    }
                } while (collision);

                node.GraphPosX = candidate.x;
                node.GraphPosY = candidate.y;
                LayoutCardsInsideNode(node);
                box = ComputeNodeBoundingBox(node);

                placedBoxes.Add(box);
                curX += box.width + gapX;
                currentRowHeight = Mathf.Max(currentRowHeight, box.height);
            }

            if (_data.Triggers != null && _data.Triggers.Count > 0)
            {
                GetSceneContentBounds(out float _, out float _, out float _, out float maxY);
                float trigX = startX;
                float trigY = maxY + 50f;
                for (int t = 0; t < _data.Triggers.Count; t++)
                {
                    var trg = _data.Triggers[t];
                    if (trg == null) continue;
                    trg.GraphPosX = trigX;
                    trg.GraphPosY = trigY;
                    Vector2 s = GetTriggerCardSize(trg);
                    trigX += s.x + 30f;
                }
            }

            _focusedNodeIndex = 0;
            FocusAllNodes(canvasRect);
        }

        // Tailles de cartes UNIFIÉES : une seule source de vérité pour éviter
        // frame trop petite / clipping / wires désalignés (310 vs 340 vs 360 selon les méthodes).
        private const float DlgCardDefaultW = 360f;
        private const float EvtCardDefaultW = 360f;
        private const float EvtCardDefaultH = 155f;
        private const float ConsCardDefaultW = 360f;
        private const float ConsCardDefaultH = 250f;
        private const float TrigCardDefaultW = 340f;
        private const float TrigCardDefaultH = 215f;
        private static float DlgCardDefaultH(int choiceCount) => 215f + choiceCount * 28f;

        // Nombre de lignes de config d'un défi (sans les lignes-liens).
        private static int GetChallengeConfigRows(SceneDialogueChallengeData ch)
        {
            if (ch == null || !ch.HasChallenge) return 0;
            return ch.IsOpposed ? 2 : 1;
        }

        // Toutes les lignes de détail sous la rangée principale d'un choix :
        // config défi + liens ✔/✕, méta (prérequis/effets).
        // Routage direct vers fenêtres normales : plus de dialogue embarqué
        // (↳ réponse, discours succès/échec supprimés).
        // Doit correspondre EXACTEMENT à l'ordre de dessin dans DrawDialogueCard.
        private static int GetChoiceDetailRows(SceneDialogueChoiceData ch)
        {
            if (ch == null) return 0;
            int rows = 0;
            var chal = ch.Challenge;
            if (chal != null && chal.HasChallenge)
                rows += GetChallengeConfigRows(chal) + 2; // config + liens ✔/✕
            bool hasPre = ch.Prerequisite != null && ch.Prerequisite.HasPrerequisite;
            bool hasFx = ch.Effects != null && ch.Effects.Count > 0;
            if (hasPre || hasFx) rows += 1; // méta 🔒/fx
            return rows;
        }

        // Y (relatif au haut de la carte) de la rangée principale de chaque choix.
        // Doit rester identique entre GetDialogueCardSize, DrawDialogueCard et DrawConnectedGraphWires.
        private static void GetDialogueChoiceRowOffsets(SceneDialogueLineData line, List<float> outOffsets)
        {
            outOffsets.Clear();
            if (line == null || line.Choices == null) return;
            float y = 100f;
            for (int c = 0; c < line.Choices.Count; c++)
            {
                outOffsets.Add(y);
                y += 22f;
                y += GetChoiceDetailRows(line.Choices[c]) * 20f;
            }
        }

        private static float GetDialogueCardNeededHeight(SceneDialogueLineData line)
        {
            int cc = line != null && line.Choices != null ? line.Choices.Count : 0;
            float needed = 132f + cc * 22f + 24f;
            if (line != null && line.Choices != null)
            {
                for (int c = 0; c < line.Choices.Count; c++)
                {
                    needed += GetChoiceDetailRows(line.Choices[c]) * 20f;
                }
                needed += GetDialogueLineSummaryRows(line) * 18f;
            }
            return needed;
        }

        // Lignes de résumé de la réplique (sous les choix) : prérequis / test auto / ambiance.
        private static int GetDialogueLineSummaryRows(SceneDialogueLineData line)
        {
            if (line == null) return 0;
            int rows = 0;
            if (line.Prerequisite != null && line.Prerequisite.HasPrerequisite) rows++;
            if (line.AutoSkillCheck != null && line.AutoSkillCheck.HasAutoCheck) rows++;
            if (line.Ambience != null && line.Ambience.HasAmbience) rows++;
            return rows;
        }

        private static Vector2 GetDialogueCardSize(SceneDialogueLineData line)
        {
            if (line == null) return new Vector2(DlgCardDefaultW, DlgCardDefaultH(0));
            float w = line.CardWidth > 100f ? line.CardWidth : DlgCardDefaultW;
            float h = line.CardHeight > 80f ? line.CardHeight : DlgCardDefaultH(line.Choices != null ? line.Choices.Count : 0);
            // Hauteur minimale pour que le contenu GUI absolu ne déborde jamais de la carte.
            float needed = GetDialogueCardNeededHeight(line);
            if (needed > h) h = needed;
            return new Vector2(w, h);
        }

        private static Vector2 GetEventCardSize(SceneEventData ev)
        {
            if (ev == null) return new Vector2(EvtCardDefaultW, EvtCardDefaultH);
            float w = ev.CardWidth > 100f ? ev.CardWidth : EvtCardDefaultW;
            float h = ev.CardHeight > 80f ? ev.CardHeight : EvtCardDefaultH;
            return new Vector2(w, h);
        }

        // Hauteur carte conséquence : 30 (haut) + 22 (ID/titre) + 20 (toggles)
        // + dialogue éventuel (22 locuteur + 44 speech) + 22 (liens) + 22 (+XP/+CE)
        // + 20 × (1 + N items) + 10 (marge).
        private static float GetConsequenceCardNeededHeight(SceneConsequenceData cons)
        {
            float needed = 30f + 22f + 20f + 22f + 22f + 20f + 10f;
            if (cons != null)
            {
                if (cons.HasDialogue) needed += 22f + 44f;
                int extras = cons.Rewards?.AdditionalItems != null ? cons.Rewards.AdditionalItems.Count : 0;
                needed += Mathf.Max(0, extras) * 20f;
            }
            return needed;
        }

        private static Vector2 GetConsequenceCardSize(SceneConsequenceData cons)
        {
            if (cons == null) return new Vector2(ConsCardDefaultW, ConsCardDefaultH);
            float w = cons.CardWidth > 100f ? cons.CardWidth : ConsCardDefaultW;
            float h = cons.CardHeight > 80f ? cons.CardHeight : ConsCardDefaultH;
            float needed = GetConsequenceCardNeededHeight(cons);
            if (needed > h) h = needed;
            return new Vector2(w, h);
        }

        private static Vector2 GetTriggerCardSize(SceneTriggerData trg)
        {
            if (trg == null) return new Vector2(TrigCardDefaultW, TrigCardDefaultH);
            float w = trg.CardWidth > 100f ? trg.CardWidth : TrigCardDefaultW;
            float h = trg.CardHeight > 80f ? trg.CardHeight : TrigCardDefaultH;
            float needed = trg.Kind == SceneTriggerKind.ActorCondition ? 238f : 196f;
            if (h < needed) h = needed;
            return new Vector2(w, h);
        }

        private Vector2 _lastCanvasSize = new Vector2(1100f, 600f);
        private int _focusedNodeIndex = 0;

        private Vector2 ResolveViewSize(Rect? canvasRect)
        {
            if (canvasRect.HasValue && canvasRect.Value.width > 100f && canvasRect.Value.height > 100f)
                return new Vector2(canvasRect.Value.width, canvasRect.Value.height);
            if (_lastCanvasSize.x > 100f && _lastCanvasSize.y > 100f)
                return _lastCanvasSize;
            return new Vector2(Mathf.Max(800f, _windowRect.width - 24f), Mathf.Max(420f, _windowRect.height - 140f));
        }

        private void FocusNodeByIndex(int index, Rect? canvasRect = null)
        {
            if (_data.Nodes.Count == 0) return;
            // Navigation circulaire : on peut parcourir tous les nœuds avec ◀ ▶ sans cul-de-sac.
            int n = _data.Nodes.Count;
            _focusedNodeIndex = ((index % n) + n) % n;
            CenterViewOnNode(_data.Nodes[_focusedNodeIndex], canvasRect);
        }

        private bool GetSceneContentBounds(out float minX, out float minY, out float maxX, out float maxY)
        {
            minX = float.MaxValue; minY = float.MaxValue;
            maxX = float.MinValue; maxY = float.MinValue;
            if (_data.Nodes.Count == 0) return false;
            for (int i = 0; i < _data.Nodes.Count; i++)
            {
                Rect b = ComputeNodeBoundingBox(_data.Nodes[i]);
                minX = Mathf.Min(minX, b.xMin);
                minY = Mathf.Min(minY, b.yMin);
                maxX = Mathf.Max(maxX, b.xMax);
                maxY = Mathf.Max(maxY, b.yMax);
            }
            if (_data.Triggers != null)
            {
                for (int t = 0; t < _data.Triggers.Count; t++)
                {
                    var trg = _data.Triggers[t];
                    if (trg == null) continue;
                    Vector2 tsize = GetTriggerCardSize(trg);
                    minX = Mathf.Min(minX, trg.GraphPosX);
                    minY = Mathf.Min(minY, trg.GraphPosY);
                    maxX = Mathf.Max(maxX, trg.GraphPosX + tsize.x);
                    maxY = Mathf.Max(maxY, trg.GraphPosY + tsize.y);
                }
            }
            return (maxX > minX) && (maxY > minY);
        }

        private void ClampPanToContent(Rect? canvasRect = null)
        {
            if (!GetSceneContentBounds(out float minX, out float minY, out float maxX, out float maxY)) return;
            Vector2 view = ResolveViewSize(canvasRect);

            const float edgePadding = 40f;
            float contentW = (maxX - minX) * _zoom;
            float contentH = (maxY - minY) * _zoom;

            if (contentW <= view.x - edgePadding * 2f)
            {
                float idealX = (view.x - (minX + maxX) * _zoom) * 0.5f;
                _graphPan.x = Mathf.Clamp(_graphPan.x, idealX - 180f, idealX + 180f);
            }
            else
            {
                float minPanX = view.x - edgePadding - maxX * _zoom;
                float maxPanX = edgePadding - minX * _zoom;
                _graphPan.x = Mathf.Clamp(_graphPan.x, minPanX, maxPanX);
            }

            if (contentH <= view.y - edgePadding * 2f)
            {
                float idealY = (view.y - (minY + maxY) * _zoom) * 0.5f;
                _graphPan.y = Mathf.Clamp(_graphPan.y, idealY - 140f, idealY + 140f);
            }
            else
            {
                float minPanY = view.y - edgePadding - maxY * _zoom;
                float maxPanY = edgePadding - minY * _zoom;
                _graphPan.y = Mathf.Clamp(_graphPan.y, minPanY, maxPanY);
            }
        }

        private void CenterViewOnNode(SceneNodeData node, Rect? canvasRect = null)
        {
            if (node == null) return;
            Rect box = ComputeNodeBoundingBox(node);
            Vector2 view = ResolveViewSize(canvasRect);
            _lastCanvasSize = view;

            float fitZX = (view.x - 80f) / Mathf.Max(1f, box.width);
            float fitZY = (view.y - 80f) / Mathf.Max(1f, box.height);
            _zoom = Mathf.Clamp(Mathf.Min(1.0f, fitZX, fitZY), 0.35f, 1.0f);
            _graphPan = new Vector2(
                (view.x * 0.5f) - box.center.x * _zoom,
                (view.y * 0.5f) - box.center.y * _zoom
            );
            _lastViewWasAll = false;
            _fitZoom = _zoom;
        }

        private void ZoomAroundCenter(float newZoom, Rect? canvasRect = null)
        {
            newZoom = Mathf.Clamp(newZoom, 0.12f, 2.0f);
            Vector2 view = ResolveViewSize(canvasRect);
            Vector2 centerScreen = new Vector2(view.x * 0.5f, view.y * 0.5f);
            Vector2 worldUnderCenter = (centerScreen - _graphPan) / _zoom;
            _zoom = newZoom;
            _graphPan = centerScreen - worldUnderCenter * _zoom;
            ClampPanToContent(canvasRect);
        }

        private void FocusAllNodes(Rect? canvasRect = null)
        {
            if (_data.Nodes.Count == 0) return;

            if (!GetSceneContentBounds(out float minX, out float minY, out float maxX, out float maxY)) return;

            float width = maxX - minX;
            float height = maxY - minY;
            if (width <= 0f || height <= 0f) return;

            Vector2 view = ResolveViewSize(canvasRect);
            _lastCanvasSize = view;

            const float padView = 50f;
            float targetZoomX = (view.x - padView * 2f) / width;
            float targetZoomY = (view.y - padView * 2f) / height;
            _zoom = Mathf.Clamp(Mathf.Min(targetZoomX, targetZoomY), 0.15f, 1.0f);

            _graphPan = new Vector2(
                (view.x * 0.5f) - (minX + width * 0.5f) * _zoom,
                (view.y * 0.5f) - (minY + height * 0.5f) * _zoom
            );
            _lastViewWasAll = true;
            _fitZoom = _zoom;
        }

        private void RecenterNode(SceneNodeData node, Rect? canvasRect = null)
        {
            // Recentrage SANS toucher au zoom (édition rapprochée préservée au redimensionnement).
            if (node == null) return;
            Rect box = ComputeNodeBoundingBox(node);
            Vector2 view = ResolveViewSize(canvasRect);
            _lastCanvasSize = view;
            _graphPan = new Vector2(
                (view.x * 0.5f) - box.center.x * _zoom,
                (view.y * 0.5f) - box.center.y * _zoom
            );
            ClampPanToContent(canvasRect);
        }

        private void AutoLayoutNodeGraph(SceneNodeData node)
        {
            float curX = 60f;
            float curY = 80f;
            for (int i = 0; i < node.Dialogues.Count; i++)
            {
                node.Dialogues[i].GraphPosX = curX;
                node.Dialogues[i].GraphPosY = curY;
                Vector2 s = GetDialogueCardSize(node.Dialogues[i]);
                curX += Mathf.Max(370f, s.x + 35f);
                if ((i + 1) % 3 == 0)
                {
                    curX = 60f;
                    curY += 290f;
                }
            }

            curX = 60f;
            curY += 340f;
            for (int e = 0; e < node.Events.Count; e++)
            {
                node.Events[e].GraphPosX = curX;
                node.Events[e].GraphPosY = curY;
                Vector2 s = GetEventCardSize(node.Events[e]);
                curX += Mathf.Max(370f, s.x + 35f);
            }
        }

        private void ComputeNewNodeSpawn(out float x, out float y)
        {
            x = 60f;
            y = 60f;
            if (_data.Nodes.Count > 0)
            {
                if (GetSceneContentBounds(out float _, out float _, out float maxX, out float _))
                {
                    x = maxX + 60f;
                    y = 60f;
                }
                else
                {
                    var lastBox = ComputeNodeBoundingBox(_data.Nodes[_data.Nodes.Count - 1]);
                    x = lastBox.xMax + 60f;
                    y = lastBox.yMin;
                }
            }
        }

        private static Rect ComputeNodeBoundingBox(SceneNodeData node)
        {
            float nx = SanitizeCoord(node.GraphPosX, 60f);
            float ny = SanitizeCoord(node.GraphPosY, 60f);
            float minX = nx;
            float minY = ny;
            float maxX = nx + 560f;
            float maxY = ny + 220f;

            bool hasEntries = false;
            if (node.Dialogues != null && node.Dialogues.Count > 0)
            {
                for (int i = 0; i < node.Dialogues.Count; i++)
                {
                    var d = node.Dialogues[i];
                    Vector2 s = GetDialogueCardSize(d);
                    if (!hasEntries)
                    {
                        minX = d.GraphPosX;
                        minY = d.GraphPosY;
                        maxX = d.GraphPosX + s.x;
                        maxY = d.GraphPosY + s.y;
                        hasEntries = true;
                    }
                    else
                    {
                        minX = Mathf.Min(minX, d.GraphPosX);
                        minY = Mathf.Min(minY, d.GraphPosY);
                        maxX = Mathf.Max(maxX, d.GraphPosX + s.x);
                        maxY = Mathf.Max(maxY, d.GraphPosY + s.y);
                    }
                }
            }

            if (node.Events != null && node.Events.Count > 0)
            {
                for (int i = 0; i < node.Events.Count; i++)
                {
                    var e = node.Events[i];
                    if (e == null) continue;
                    Vector2 s = GetEventCardSize(e);
                    if (!hasEntries)
                    {
                        minX = e.GraphPosX;
                        minY = e.GraphPosY;
                        maxX = e.GraphPosX + s.x;
                        maxY = e.GraphPosY + s.y;
                        hasEntries = true;
                    }
                    else
                    {
                        minX = Mathf.Min(minX, e.GraphPosX);
                        minY = Mathf.Min(minY, e.GraphPosY);
                        maxX = Mathf.Max(maxX, e.GraphPosX + s.x);
                        maxY = Mathf.Max(maxY, e.GraphPosY + s.y);
                    }
                }
            }

            if (node.Consequences != null && node.Consequences.Count > 0)
            {
                for (int i = 0; i < node.Consequences.Count; i++)
                {
                    var k = node.Consequences[i];
                    if (k == null) continue;
                    Vector2 s = GetConsequenceCardSize(k);
                    if (!hasEntries)
                    {
                        minX = k.GraphPosX;
                        minY = k.GraphPosY;
                        maxX = k.GraphPosX + s.x;
                        maxY = k.GraphPosY + s.y;
                        hasEntries = true;
                    }
                    else
                    {
                        minX = Mathf.Min(minX, k.GraphPosX);
                        minY = Mathf.Min(minY, k.GraphPosY);
                        maxX = Mathf.Max(maxX, k.GraphPosX + s.x);
                        maxY = Mathf.Max(maxY, k.GraphPosY + s.y);
                    }
                }
            }

            if (!hasEntries)
            {
                float w = node.FrameWidth > 400f ? node.FrameWidth : 560f;
                // Éditeurs inline de la frame vide : 46 (header) + 26 (type) + 26 (titre)
                // + 78 (narration) + 26 (entête objectifs) + 28 / objectif + 10 (marge).
                int objCount = (node.Objectives != null) ? node.Objectives.Count : 0;
                float choiceH = (node.Choices != null) ? node.Choices.Count * 32f : 0f;
                float customH = 208f + objCount * 28f + choiceH + 10f;
                return new Rect(nx, ny, w, Mathf.Max(220f, customH));
            }

            float pad = 14f;
            float topHeaderH = 34f;
            minX = Mathf.Min(nx, minX);
            minY = Mathf.Min(ny + topHeaderH, minY);
            Rect r = new Rect(minX - pad, minY - topHeaderH - pad, (maxX - minX) + pad * 2f, (maxY - minY) + topHeaderH + pad * 2f);
            if (r.width < 560f) r.width = 560f;
            return r;
        }

        private static float SanitizeCoord(float v, float fallback)
        {
            return (float.IsNaN(v) || float.IsInfinity(v)) ? fallback : Mathf.Clamp(v, -20000f, 20000f);
        }

        // ---------------------------------------------------------------------
        // Transformation du graphe sans GUI.matrix : le clipping reste toujours
        // dans l'espace écran du BeginGroup. Toutes les coordonnées du graphe
        // sont conservées en espace monde et converties seulement au rendu.
        // ---------------------------------------------------------------------
        private Vector2 GraphPoint(Vector2 world)
        {
            return _graphPan + world * _zoom;
        }

        private Rect GraphRect(Rect worldRect)
        {
            Vector2 p = GraphPoint(worldRect.position);
            return new Rect(p.x, p.y, worldRect.width * _zoom, worldRect.height * _zoom);
        }

        private bool GraphPrimaryDown(Vector2 mouseWorld, Rect worldRect)
        {
            if (_pickerCapturesMouse) return false;
            Event evt = Event.current;
            return evt.type == EventType.MouseDown && evt.button == 0 && worldRect.Contains(mouseWorld);
        }

        private void GraphLabel(Rect worldRect, string text)
        {
            GUI.Label(GraphRect(worldRect), text);
        }

        private void GraphLabel(Rect worldRect, string text, GUIStyle style)
        {
            GUI.Label(GraphRect(worldRect), text, style);
        }

        private bool GraphButton(Rect worldRect, string text)
        {
            bool pressed = GUI.Button(GraphRect(worldRect), text);
            return pressed && !_pickerCapturesMouse;
        }

        private string GraphTextField(Rect worldRect, string text)
        {
            string result = GUI.TextField(GraphRect(worldRect), text);
            return _pickerCapturesMouse ? text : result;
        }

        private string GraphTextArea(Rect worldRect, string text)
        {
            string result = GUI.TextArea(GraphRect(worldRect), text);
            return _pickerCapturesMouse ? text : result;
        }

        private bool GraphToggle(Rect worldRect, bool value, string text)
        {
            bool result = GUI.Toggle(GraphRect(worldRect), value, text);
            return _pickerCapturesMouse ? value : result;
        }

        private int GraphToolbar(Rect worldRect, int selected, string[] contents)
        {
            int result = GUI.Toolbar(GraphRect(worldRect), selected, contents);
            return _pickerCapturesMouse ? selected : result;
        }

        private void DrawGraphSolidRect(Rect worldRect, Color color)
        {
            DrawSolidRect(GraphRect(worldRect), color);
        }

        private readonly Dictionary<string, Vector2> _cachedNodeInputSockets = new();

        private void DrawNodeGraphCanvas(Rect canvasRect)
        {
            _lastCanvasSize = new Vector2(canvasRect.width, canvasRect.height);
            _hasHoverCard = false;

            Event evt = Event.current;
            Vector2 mouseLocal = evt.mousePosition - canvasRect.min;
            Vector2 mouseWorld = (mouseLocal - _graphPan) / Mathf.Max(0.0001f, _zoom);
            _graphMouseScreenPos = evt.mousePosition;
            _pickerCapturesMouse = (IsItemPickerOpen() && GetItemPickerRect(canvasRect).Contains(evt.mousePosition))
                || (_actorDropKey != null && WorldToScreenRect(canvasRect, _actorDropWorld).Contains(evt.mousePosition));

            // Clic hors du dropdown locuteurs => le refermer (sauf sur son bouton ▼).
            if (_actorDropKey != null && evt.type == EventType.MouseDown
                && !_actorDropWorld.Contains(mouseWorld) && !_actorDropAnchorWorld.Contains(mouseWorld))
                _actorDropKey = null;

            // Zoom centré sur le curseur. IMPORTANT : aucun scale de GUI.matrix n'est utilisé
            // pour le graphe. Le clipping du BeginGroup reste donc en coordonnées écran 1:1.
            if (evt.type == EventType.ScrollWheel && canvasRect.Contains(evt.mousePosition) && !_pickerCapturesMouse)
            {
                float zoomFactor = evt.delta.y > 0 ? 0.88f : 1.14f;
                float oldZoom = _zoom;
                _zoom = Mathf.Clamp(_zoom * zoomFactor, 0.15f, 2.0f);
                _graphPan = mouseLocal - (mouseLocal - _graphPan) * (_zoom / Mathf.Max(0.0001f, oldZoom));
                ClampPanToContent(canvasRect);
                evt.Use();
            }

            // Panoramique fluide (Clic droit et molette universels ; Clic gauche uniquement sur le fond vide).
            bool isOverInteractiveElement = (_draggingCardKey != null || _draggingNodeId != null || _resizingCardKey != null || _wireDraft.IsActive || IsMouseOverAnyNodeOrCard(mouseWorld));
            bool pickerBlocksMouse = _pickerCapturesMouse;
            if (evt.type == EventType.MouseDown && canvasRect.Contains(evt.mousePosition))
            {
                if (evt.button == 1 || evt.button == 2 || (evt.button == 0 && !isOverInteractiveElement && !pickerBlocksMouse))
                {
                    _isPanning = true;
                    _panStartMouse = evt.mousePosition;
                    _panStartOffset = _graphPan;
                    evt.Use();
                }
            }
            else if (evt.type == EventType.MouseDrag && _isPanning)
            {
                _graphPan = _panStartOffset + (evt.mousePosition - _panStartMouse);
                ClampPanToContent(canvasRect);
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && _isPanning)
            {
                _isPanning = false;
                evt.Use();
            }

            ClampPanToContent(canvasRect);

            // Le groupe de clipping reste strictement à 1:1.
            // Le zoom/pan sont appliqués explicitement par GraphRect()/GraphPoint().
            GUI.BeginGroup(canvasRect);
            DrawGraphGridBackground(new Rect(0f, 0f, canvasRect.width, canvasRect.height));

            _cachedInputSockets.Clear();
            _cachedNodeInputSockets.Clear();
            _cachedTriggerOutSockets.Clear();

            for (int n = 0; n < _data.Nodes.Count; n++)
            {
                var node = _data.Nodes[n];
                EnsureDialogueGraphIntegrity(node);
                StorySceneData.EnsureNodeLists(node);

                node.GraphPosX = SanitizeCoord(node.GraphPosX, 60f + n * 500f);
                node.GraphPosY = SanitizeCoord(node.GraphPosY, 220f);
                for (int d = 0; d < node.Dialogues.Count; d++)
                {
                    var dl = node.Dialogues[d];
                    if (dl == null) continue;
                    dl.GraphPosX = SanitizeCoord(dl.GraphPosX, node.GraphPosX + 20f);
                    dl.GraphPosY = SanitizeCoord(dl.GraphPosY, node.GraphPosY + 50f);
                }
                for (int e = 0; e < node.Events.Count; e++)
                {
                    var ev0 = node.Events[e];
                    if (ev0 == null) continue;
                    ev0.GraphPosX = SanitizeCoord(ev0.GraphPosX, node.GraphPosX + 20f);
                    ev0.GraphPosY = SanitizeCoord(ev0.GraphPosY, node.GraphPosY + 300f);
                }
                if (node.Consequences == null) node.Consequences = new System.Collections.Generic.List<SceneConsequenceData>();
                for (int k = 0; k < node.Consequences.Count; k++)
                {
                    var cs0 = node.Consequences[k];
                    if (cs0 == null) continue;
                    cs0.GraphPosX = SanitizeCoord(cs0.GraphPosX, node.GraphPosX + 20f);
                    cs0.GraphPosY = SanitizeCoord(cs0.GraphPosY, node.GraphPosY + 560f);
                }

                Rect box = ComputeNodeBoundingBox(node);
                _cachedNodeInputSockets[node.NodeId] = new Vector2(box.xMin, box.yMin + 18f);

                for (int d = 0; d < node.Dialogues.Count; d++)
                {
                    var line = node.Dialogues[d];
                    if (string.IsNullOrEmpty(line.LineId)) line.LineId = $"line_{d + 1}";
                    _cachedInputSockets[line.LineId] = new Vector2(line.GraphPosX, line.GraphPosY + 26f);
                }

                for (int e = 0; e < node.Events.Count; e++)
                {
                    var ev = node.Events[e];
                    if (string.IsNullOrEmpty(ev.EventId)) ev.EventId = $"evt_{e + 1}";
                    _cachedInputSockets[ev.EventId] = new Vector2(ev.GraphPosX, ev.GraphPosY + 26f);
                }

                if (node.Consequences == null) node.Consequences = new System.Collections.Generic.List<SceneConsequenceData>();
                for (int k = 0; k < node.Consequences.Count; k++)
                {
                    var cons = node.Consequences[k];
                    if (cons == null) continue;
                    if (string.IsNullOrEmpty(cons.ConsequenceId)) cons.ConsequenceId = $"cons_{k + 1}";
                    _cachedInputSockets[cons.ConsequenceId] = new Vector2(cons.GraphPosX, cons.GraphPosY + 26f);
                }
            }

            if (_data.Triggers == null) _data.Triggers = new System.Collections.Generic.List<SceneTriggerData>();
            {
                var seenTriggerIds = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                for (int t = 0; t < _data.Triggers.Count; t++)
                {
                    var trg = _data.Triggers[t];
                    if (trg == null) continue;
                    trg.GraphPosX = SanitizeCoord(trg.GraphPosX, 60f + t * 380f);
                    trg.GraphPosY = SanitizeCoord(trg.GraphPosY, 900f);
                    if (string.IsNullOrEmpty(trg.TriggerId)) trg.TriggerId = $"trig_{t + 1}";
                    if (!seenTriggerIds.Add(trg.TriggerId)) trg.TriggerId = $"{trg.TriggerId}_{t + 1}";
                    Vector2 tsize = GetTriggerCardSize(trg);
                    _cachedTriggerOutSockets[trg.TriggerId] = new Vector2(trg.GraphPosX + tsize.x, trg.GraphPosY + 26f);
                }
            }

            Rect visibleWorld = GetVisibleWorldRect(canvasRect);

            // Passe 1 : frames et connexions globales.
            for (int n = 0; n < _data.Nodes.Count; n++)
            {
                Rect nodeBox = ComputeNodeBoundingBox(_data.Nodes[n]);
                if (visibleWorld.Overlaps(nodeBox))
                    DrawNodeFrame(_data.Nodes[n], n, mouseWorld);
            }

            DrawGlobalSceneWires(visibleWorld);

            if (_wireDraft.IsActive)
            {
                DrawBezierWire(_wireDraft.StartPos, mouseWorld, Color.yellow, 3.5f);
                if (evt.type == EventType.MouseUp && evt.button == 0 && !_pickerCapturesMouse)
                {
                    FinalizeGlobalWireDraft(mouseWorld);
                    _wireDraft.IsActive = false;
                    evt.Use();
                }
            }

            // Passe 2 : cartes internes par-dessus les fils.
            for (int n = 0; n < _data.Nodes.Count; n++)
            {
                var node = _data.Nodes[n];
                Rect nodeBox = ComputeNodeBoundingBox(node);
                if (!visibleWorld.Overlaps(nodeBox)) continue;

                for (int d = 0; d < node.Dialogues.Count; d++)
                {
                    var dlg = node.Dialogues[d];
                    Vector2 size = GetDialogueCardSize(dlg);
                    if (visibleWorld.Overlaps(new Rect(dlg.GraphPosX, dlg.GraphPosY, size.x, size.y)))
                        DrawDialogueCard(dlg, d, node, mouseWorld);
                }
                for (int e = 0; e < node.Events.Count; e++)
                {
                    var ev = node.Events[e];
                    Vector2 size = GetEventCardSize(ev);
                    if (visibleWorld.Overlaps(new Rect(ev.GraphPosX, ev.GraphPosY, size.x, size.y)))
                        DrawEventCard(ev, e, node, mouseWorld);
                }
                if (node.Consequences != null)
                {
                    for (int k = 0; k < node.Consequences.Count; k++)
                    {
                        var cons = node.Consequences[k];
                        if (cons == null) continue;
                        Vector2 size = GetConsequenceCardSize(cons);
                        if (visibleWorld.Overlaps(new Rect(cons.GraphPosX, cons.GraphPosY, size.x, size.y)))
                            DrawConsequenceCard(cons, k, node, mouseWorld);
                    }
                }
            }

            if (_data.Triggers != null)
            {
                for (int t = 0; t < _data.Triggers.Count; t++)
                {
                    var trg = _data.Triggers[t];
                    if (trg == null) continue;
                    Vector2 tsize = GetTriggerCardSize(trg);
                    if (visibleWorld.Overlaps(new Rect(trg.GraphPosX, trg.GraphPosY, tsize.x, tsize.y)))
                        DrawTriggerCard(trg, t, mouseWorld);
                }
            }

            // Dropdown locuteurs par-dessus tout le graphe.
            DrawActorDropdownPopup();

            // Le seul clip du graphe est terminé ici, avant tous les overlays écran.
            GUI.EndGroup();

            DrawZoomControlsOverlay(canvasRect);

            if (_showNodeInspector && !IsItemPickerOpen()) DrawNodeInspectorOverlay(canvasRect);

            if (_hasHoverCard && !IsItemPickerOpen() && Event.current.type == EventType.Repaint)
                DrawCardHoverTooltip(canvasRect);

            DrawItemPickerPanel(canvasRect);
        }

        private bool _showNodeInspector = false;
        private Vector2 _inspectorScroll;

        // Moteur de recherche d'item du catalogue (récompense de défi, ligne ✔ des cartes + inspecteur).
        // État purement éditeur : rien n'est sérialisé dans le JSON de scène.
        private SceneDialogueChoiceData _itemPickerTarget = null;
        private SceneConsequenceData _itemPickerCons = null;
        private int _itemPickerEntry = -1; // -1 = récompense principale, >= 0 = AdditionalItems[i]
        private string _itemPickerSearch = "";
        private int _itemPickerCat = -1; // -1 = toutes catégories
        private Vector2 _itemPickerScroll;
        private bool _itemPickerFocusSearch = false;
        // Vrai quand le sélecteur catalogue est ouvert et que la souris est dessus :
        // le panneau est modal, tout le graphe derrière devient inerte (pas de drag,
        // pas de wire, pas de zoom, boutons/champs sans effet).
        private bool _pickerCapturesMouse = false;

        // Dropdown des locuteurs (champs Loc: des cartes) : alimenté par les acteurs
        // de la scène + les noms déjà utilisés. Pur état éditeur.
        private string _actorDropKey = null;
        private Rect _actorDropAnchorWorld = new Rect(-10000f, -10000f, 0f, 0f);
        private Rect _actorDropWorld = new Rect(-10000f, -10000f, 0f, 0f);
        private System.Action<string> _actorDropSetter = null;
        private string _actorDropCurrent = "";

        private void DrawNodeInspectorOverlay(Rect canvasRect)
        {
            float w = Mathf.Min(440f, canvasRect.width * 0.5f);
            if (w < 260f || canvasRect.height < 200f) return;
            Rect panel = new Rect(canvasRect.xMax - w - 8f, canvasRect.y + 40f, w, canvasRect.height - 48f);
            DrawSolidRect(panel, new Color(0.02f, 0.03f, 0.05f, 0.96f));
            DrawSolidRect(new Rect(panel.x, panel.y, panel.width, 2f), new Color(0.2f, 0.75f, 1f, 0.9f));
            GUILayout.BeginArea(panel);
            _inspectorScroll = GUILayout.BeginScrollView(_inspectorScroll);
            if (_focusedNodeIndex >= 0 && _focusedNodeIndex < _data.Nodes.Count && _data.Nodes[_focusedNodeIndex] != null)
            {
                var node = _data.Nodes[_focusedNodeIndex];
                GUILayout.Label($"<b>🔧 Nœud #{_focusedNodeIndex + 1} : {node.Title}</b> [{node.Kind}]");
                StorySceneData.EnsureDeepDefaults(_data);
                DrawNodeEditor(node);
            }
            else
            {
                GUILayout.Label("Aucun nœud focalisé.");
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private bool IsItemPickerOpen()
        {
            return _itemPickerTarget != null || _itemPickerCons != null;
        }

        private SceneDialogueRewardData PickerRewards()
        {
            if (_itemPickerTarget != null) return _itemPickerTarget.Challenge?.SuccessRewards;
            if (_itemPickerCons != null) return _itemPickerCons.Rewards;
            return null;
        }

        private string PickerOwnerLabel()
        {
            if (_itemPickerTarget != null) return _itemPickerTarget.Label ?? "";
            if (_itemPickerCons != null) return _itemPickerCons.Title ?? "";
            return "";
        }

        private void OpenItemPicker(SceneDialogueChoiceData choice, int entryIndex = -1)
        {
            if (choice == null) return;
            choice.Challenge ??= new SceneDialogueChallengeData();
            choice.Challenge.SuccessRewards ??= new SceneDialogueRewardData();
            StorySceneData.EnsureRewardDefaults(choice.Challenge.SuccessRewards);
            if (entryIndex >= 0)
            {
                var list = choice.Challenge.SuccessRewards.AdditionalItems;
                if (list == null || entryIndex >= list.Count) return;
            }
            _itemPickerTarget = choice;
            _itemPickerCons = null;
            _itemPickerEntry = entryIndex;
            _itemPickerSearch = GetPickerItemName();
            _itemPickerCat = -1;
            _itemPickerScroll = Vector2.zero;
            _itemPickerFocusSearch = true;
        }

        private void OpenItemPicker(SceneConsequenceData cons, int entryIndex = -1)
        {
            if (cons == null) return;
            cons.Rewards ??= new SceneDialogueRewardData();
            StorySceneData.EnsureRewardDefaults(cons.Rewards);
            if (entryIndex >= 0)
            {
                var list = cons.Rewards.AdditionalItems;
                if (list == null || entryIndex >= list.Count) return;
            }
            _itemPickerCons = cons;
            _itemPickerTarget = null;
            _itemPickerEntry = entryIndex;
            _itemPickerSearch = GetPickerItemName();
            _itemPickerCat = -1;
            _itemPickerScroll = Vector2.zero;
            _itemPickerFocusSearch = true;
        }

        private bool IsPickerOpenForCons(SceneConsequenceData cons, int entry)
        {
            return cons != null && ReferenceEquals(_itemPickerCons, cons) && _itemPickerEntry == entry;
        }

        private string GetPickerItemName()
        {
            var rew = PickerRewards();
            if (rew == null) return "";
            if (_itemPickerEntry >= 0)
            {
                if (rew.AdditionalItems == null || _itemPickerEntry >= rew.AdditionalItems.Count) return "";
                return rew.AdditionalItems[_itemPickerEntry]?.ItemName ?? "";
            }
            return rew.ItemRewardName ?? "";
        }

        private void SetPickerItem(InventoryItem def)
        {
            var rew = PickerRewards();
            if (rew == null || def == null) return;
            if (_itemPickerEntry >= 0)
            {
                if (rew.AdditionalItems == null || _itemPickerEntry >= rew.AdditionalItems.Count) return;
                var entry = rew.AdditionalItems[_itemPickerEntry];
                if (entry == null) return;
                entry.ItemName = def.Name;
                entry.ItemRewardType = def.Type;
            }
            else
            {
                rew.ItemRewardName = def.Name;
                rew.ItemRewardType = def.Type;
            }
            CloseItemPicker();
        }

        private void ClearPickerItem()
        {
            var rew = PickerRewards();
            if (rew == null) return;
            if (_itemPickerEntry >= 0)
            {
                if (rew.AdditionalItems != null && _itemPickerEntry < rew.AdditionalItems.Count)
                    rew.AdditionalItems.RemoveAt(_itemPickerEntry);
            }
            else
            {
                rew.ItemRewardName = "";
            }
            CloseItemPicker();
        }

        private void CloseItemPicker()
        {
            _itemPickerTarget = null;
            _itemPickerCons = null;
        }

        private bool ValidateItemPickerTarget()
        {
            if (_data == null || _data.Nodes == null) return false;
            if (_itemPickerTarget != null)
            {
                if (_itemPickerTarget.Challenge == null) return false;
                if (_itemPickerEntry >= 0)
                {
                    var list = _itemPickerTarget.Challenge.SuccessRewards?.AdditionalItems;
                    if (list == null || _itemPickerEntry >= list.Count) return false;
                }
            }
            else if (_itemPickerCons != null)
            {
                if (_itemPickerEntry >= 0)
                {
                    var list = _itemPickerCons.Rewards?.AdditionalItems;
                    if (list == null || _itemPickerEntry >= list.Count) return false;
                }
            }
            else return false;
            for (int n = 0; n < _data.Nodes.Count; n++)
            {
                var node = _data.Nodes[n];
                if (node == null) continue;
                if (_itemPickerTarget != null && node.Dialogues != null)
                {
                    for (int d = 0; d < node.Dialogues.Count; d++)
                    {
                        var line = node.Dialogues[d];
                        if (line == null || line.Choices == null) continue;
                        for (int c = 0; c < line.Choices.Count; c++)
                        {
                            if (ReferenceEquals(line.Choices[c], _itemPickerTarget)) return true;
                        }
                    }
                }
                if (_itemPickerCons != null && node.Consequences != null)
                {
                    for (int k = 0; k < node.Consequences.Count; k++)
                    {
                        if (ReferenceEquals(node.Consequences[k], _itemPickerCons)) return true;
                    }
                }
            }
            return false;
        }

        private static string NormalizeForSearch(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string decomposed = s.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
            var sb = new System.Text.StringBuilder(decomposed.Length);
            for (int i = 0; i < decomposed.Length; i++)
            {
                char ch = decomposed[i];
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark)
                    sb.Append(ch);
            }
            return sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
        }

        private Rect GetItemPickerRect(Rect canvasRect)
        {
            float pw = Mathf.Min(540f, canvasRect.width - 30f);
            float ph = Mathf.Min(430f, canvasRect.height - 70f);
            return new Rect(canvasRect.x + (canvasRect.width - pw) * 0.5f, canvasRect.y + (canvasRect.height - ph) * 0.5f, pw, ph);
        }

        private void DrawItemPickerPanel(Rect canvasRect)
        {
            if (!IsItemPickerOpen()) return;
            if (!ValidateItemPickerTarget()) { CloseItemPicker(); return; }

            Rect panel = GetItemPickerRect(canvasRect);
            if (panel.width < 300f || panel.height < 220f) return;
            Event evt = Event.current;

            DrawSolidRect(panel, new Color(0.02f, 0.03f, 0.05f, 0.97f));
            DrawSolidRect(new Rect(panel.x, panel.y, panel.width, 2f), new Color(0.35f, 1f, 0.55f, 0.9f));

            float x = panel.x + 10f;
            float y = panel.y + 10f;
            float w = panel.width - 20f;

            GUI.Label(new Rect(x, y, w - 40f, 22f), $"<b>🎁 Catalogue — récompense</b> <color=grey>« {PickerOwnerLabel()} »{(_itemPickerEntry >= 0 ? $" (item #{_itemPickerEntry + 2})" : "")}</color>");
            if (GUI.Button(new Rect(panel.xMax - 34f, y, 26f, 22f), "✕")) { CloseItemPicker(); return; }
            y += 26f;

            string current = GetPickerItemName();
            if (string.IsNullOrEmpty(current)) current = "(aucun)";
            GUI.Label(new Rect(x, y, w - 90f, 20f), $"Actuel : <b>{current}</b>");
            GUI.backgroundColor = new Color(0.85f, 0.35f, 0.35f);
            if (GUI.Button(new Rect(panel.xMax - 94f, y, 84f, 20f), "Effacer"))
            {
                ClearPickerItem();
                GUI.backgroundColor = Color.white;
                return;
            }
            GUI.backgroundColor = Color.white;
            y += 24f;

            GUI.Label(new Rect(x, y, 30f, 20f), "🔍");
            GUI.SetNextControlName("ItemPickerSearch");
            _itemPickerSearch = GUI.TextField(new Rect(x + 32f, y, w - 32f - 190f, 20f), _itemPickerSearch ?? "");
            if (_itemPickerFocusSearch) { GUI.FocusControl("ItemPickerSearch"); _itemPickerFocusSearch = false; }
            float cx = x + w - 186f;
            if (GUI.Button(new Rect(cx, y, 24f, 20f), "◀"))
            {
                _itemPickerCat--;
                if (_itemPickerCat < -1) _itemPickerCat = ArmoryCatalog.Categories.Length - 1;
                _itemPickerScroll = Vector2.zero;
            }
            string catLabel = _itemPickerCat < 0 ? "Toutes" : ArmoryCatalog.Categories[_itemPickerCat];
            GUI.Label(new Rect(cx + 26f, y, 132f, 20f), $"<b>{catLabel}</b>");
            if (GUI.Button(new Rect(cx + 160f, y, 24f, 20f), "▶"))
            {
                _itemPickerCat++;
                if (_itemPickerCat >= ArmoryCatalog.Categories.Length) _itemPickerCat = -1;
                _itemPickerScroll = Vector2.zero;
            }
            y += 24f;

            // Filtrage : normalisé sans accents, préfixe d'abord, puis contenu, puis catégorie.
            string normSearch = NormalizeForSearch(_itemPickerSearch);
            var matches = new List<KeyValuePair<int, InventoryItem>>();
            var all = ArmoryCatalog.All;
            for (int i = 0; i < all.Count; i++)
            {
                var def = all[i];
                if (def == null || string.IsNullOrEmpty(def.Name)) continue;
                if (_itemPickerCat >= 0 && !string.Equals(def.Category, ArmoryCatalog.Categories[_itemPickerCat], StringComparison.OrdinalIgnoreCase)) continue;
                if (normSearch.Length > 0)
                {
                    string nName = NormalizeForSearch(def.Name);
                    int score;
                    if (nName.StartsWith(normSearch, StringComparison.Ordinal)) score = 0;
                    else if (nName.Contains(normSearch)) score = 1;
                    else if (NormalizeForSearch(def.Category ?? "").Contains(normSearch)) score = 2;
                    else continue;
                    matches.Add(new KeyValuePair<int, InventoryItem>(score, def));
                }
                else
                {
                    matches.Add(new KeyValuePair<int, InventoryItem>(3, def));
                }
            }
            matches.Sort((a, b) =>
            {
                int d = a.Key.CompareTo(b.Key);
                return d != 0 ? d : string.Compare(a.Value.Name, b.Value.Name, StringComparison.OrdinalIgnoreCase);
            });
            if (matches.Count > 200) matches.RemoveRange(200, matches.Count - 200);

            GUI.Label(new Rect(x, y, w, 18f), $"<color=grey>{matches.Count} résultat(s) — Entrée = premier, Échap = fermer</color>");
            y += 20f;

            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape) { CloseItemPicker(); evt.Use(); return; }
            if (evt.type == EventType.KeyDown && (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter) && matches.Count > 0)
            {
                SetPickerItem(matches[0].Value);
                evt.Use();
                return;
            }

            Rect listRect = new Rect(x, y, w, panel.yMax - 8f - y);
            if (listRect.height < 40f) return;
            float rowH = 22f;
            Rect viewRect = new Rect(0f, 0f, listRect.width - 16f, matches.Count * rowH);
            _itemPickerScroll = GUI.BeginScrollView(listRect, _itemPickerScroll, viewRect);
            for (int i = 0; i < matches.Count; i++)
            {
                var def = matches[i].Value;
                if (GUI.Button(new Rect(0f, i * rowH, viewRect.width, rowH - 2f),
                    $"{def.Name} — <color=grey>{def.Category} · {def.PriceCE} CE · {def.Rarity}</color>"))
                {
                    SetPickerItem(def);
                    GUI.EndScrollView();
                    return;
                }
            }
            if (matches.Count == 0)
                GUI.Label(new Rect(0f, 0f, viewRect.width, 22f), "<color=grey>(aucun résultat — modifiez la recherche)</color>");
            GUI.EndScrollView();
        }

        private Rect WorldToScreenRect(Rect canvasRect, Rect world)
        {
            Vector2 p = GraphPoint(world.position);
            return new Rect(canvasRect.x + p.x, canvasRect.y + p.y, world.width * _zoom, world.height * _zoom);
        }

        private void OpenActorDropdown(string key, Rect anchorWorld, string current, System.Action<string> setter)
        {
            _actorDropKey = key;
            _actorDropAnchorWorld = anchorWorld;
            _actorDropCurrent = current ?? "";
            _actorDropSetter = setter;
        }

        private System.Collections.Generic.List<string> BuildActorCandidates()
        {
            var list = new System.Collections.Generic.List<string>();
            var seen = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            System.Action<string> add = n =>
            {
                if (string.IsNullOrWhiteSpace(n)) return;
                n = n.Trim();
                if (seen.Add(n)) list.Add(n);
            };
            if (_data != null && _data.Actors != null)
            {
                for (int i = 0; i < _data.Actors.Count; i++)
                {
                    var a = _data.Actors[i];
                    if (a == null) continue;
                    add(a.DisplayName);
                    if (!string.IsNullOrWhiteSpace(a.ActorId) && !string.Equals(a.ActorId, a.DisplayName, System.StringComparison.OrdinalIgnoreCase))
                        add(a.ActorId);
                }
            }
            if (_data != null && _data.Nodes != null)
            {
                for (int n = 0; n < _data.Nodes.Count; n++)
                {
                    var node = _data.Nodes[n];
                    if (node == null) continue;
                    if (node.Dialogues != null)
                    {
                        for (int d = 0; d < node.Dialogues.Count; d++)
                        {
                            var line = node.Dialogues[d];
                            if (line == null) continue;
                            add(line.SpeakerId);
                        }
                    }
                    if (node.Consequences != null)
                    {
                        for (int k = 0; k < node.Consequences.Count; k++)
                        {
                            var cons = node.Consequences[k];
                            if (cons == null) continue;
                            add(cons.SpeakerId);
                        }
                    }
                }
            }
            if (list.Count > 40) list.RemoveRange(40, list.Count - 40);
            return list;
        }

        private void DrawActorDropdownPopup()
        {
            if (_actorDropKey == null) return;
            Event evt = Event.current;
            var candidates = BuildActorCandidates();

            const float rowH = 20f;
            const float popW = 210f;
            float popH = (candidates.Count > 0 ? candidates.Count : 1) * rowH + 6f;
            Rect pop = new Rect(_actorDropAnchorWorld.x, _actorDropAnchorWorld.yMax + 2f, popW, popH);
            _actorDropWorld = pop;

            DrawGraphSolidRect(pop, new Color(0.03f, 0.05f, 0.08f, 0.97f));
            DrawGraphSolidRect(new Rect(pop.x, pop.y, pop.width, 1.5f), new Color(0.0f, 0.85f, 1f, 0.9f));
            DrawGraphSolidRect(new Rect(pop.x, pop.yMax - 1f, pop.width, 1f), new Color(1f, 1f, 1f, 0.15f));
            DrawGraphSolidRect(new Rect(pop.x, pop.y, 1f, pop.height), new Color(1f, 1f, 1f, 0.15f));
            DrawGraphSolidRect(new Rect(pop.xMax - 1f, pop.y, 1f, pop.height), new Color(1f, 1f, 1f, 0.15f));

            if (candidates.Count == 0)
            {
                GUI.Label(GraphRect(new Rect(pop.x + 6f, pop.y + 3f, popW - 12f, rowH)), "<color=grey>(aucun acteur — saisie libre)</color>");
            }
            else
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    Rect r = new Rect(pop.x + 3f, pop.y + 3f + i * rowH, popW - 6f, rowH - 2f);
                    string label = string.Equals(candidates[i], _actorDropCurrent, System.StringComparison.OrdinalIgnoreCase)
                        ? $"► {candidates[i]}" : candidates[i];
                    // Bouton brut (pas de wrapper) : la popup reste cliquable en mode modal.
                    if (GUI.Button(GraphRect(r), label))
                    {
                        _actorDropSetter?.Invoke(candidates[i]);
                        _actorDropKey = null;
                        evt.Use();
                        return;
                    }
                }
            }

            if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
            {
                _actorDropKey = null;
                evt.Use();
            }
        }

        private static GUIStyle _hoverBodyStyle;

        private void DrawCardHoverTooltip(Rect canvasRect)
        {
            if (string.IsNullOrEmpty(_hoverBody) && string.IsNullOrEmpty(_hoverTitle)) return;
            if (_hoverBodyStyle == null)
            {
                _hoverBodyStyle = new GUIStyle(GUI.skin.label);
                _hoverBodyStyle.wordWrap = true;
                _hoverBodyStyle.richText = true;
            }

            float tipW = Mathf.Min(360f, canvasRect.width - 20f);
            if (tipW < 160f) return;
            float bodyH = _hoverBodyStyle.CalcHeight(new GUIContent(_hoverBody), tipW - 16f);
            bodyH = Mathf.Min(bodyH, 220f);
            float tipH = 26f + bodyH + 10f;

            Vector2 anchor = _hoverScreenPos + new Vector2(18f, 20f);
            if (anchor.x + tipW > canvasRect.xMax - 6f) anchor.x = canvasRect.xMax - 6f - tipW;
            if (anchor.y + tipH > canvasRect.yMax - 6f) anchor.y = _hoverScreenPos.y - 8f - tipH;
            if (anchor.x < canvasRect.x + 6f) anchor.x = canvasRect.x + 6f;
            if (anchor.y < canvasRect.y + 6f) anchor.y = canvasRect.y + 6f;
            Rect tip = new Rect(anchor.x, anchor.y, tipW, tipH);

            DrawSolidRect(tip, new Color(0.03f, 0.05f, 0.08f, 0.96f));
            Color prev = GUI.color;
            GUI.color = new Color(0.0f, 0.85f, 1f, 0.9f);
            GUI.DrawTexture(new Rect(tip.x, tip.y, tip.width, 2f), PureWhiteTex);
            GUI.color = prev;
            GUI.Label(new Rect(tip.x + 8f, tip.y + 5f, tip.width - 16f, 20f), $"<b>{_hoverTitle}</b>");
            GUI.Label(new Rect(tip.x + 8f, tip.y + 26f, tip.width - 16f, bodyH), _hoverBody, _hoverBodyStyle);
        }

        private void DrawGlobalSceneWires(Rect visibleWorld)
        {
            for (int n = 0; n < _data.Nodes.Count; n++)
            {
                var node = _data.Nodes[n];
                Rect nodeBox = ComputeNodeBoundingBox(node);

                if (!string.IsNullOrEmpty(node.NextNodeId)
                    && _cachedNodeInputSockets.TryGetValue(node.NextNodeId, out var targetNodeIn))
                {
                    Vector2 nodeOut = new Vector2(nodeBox.xMax, nodeBox.yMin + 18f);
                    DrawBezierWire(nodeOut, targetNodeIn, new Color(1.0f, 0.6f, 0.1f, 0.95f), 4.5f);
                }

                for (int c = 0; c < node.Choices.Count; c++)
                {
                    var choice = node.Choices[c];
                    if (!string.IsNullOrEmpty(choice.NextNodeId)
                        && _cachedNodeInputSockets.TryGetValue(choice.NextNodeId, out var choiceNodeIn))
                    {
                        float choiceRowY = nodeBox.yMin + 60f + (c * 36f);
                        Vector2 choiceOut = new Vector2(nodeBox.xMax, choiceRowY + 14f);
                        Color choiceWireCol = choice.Id.Contains("hostile") ? new Color(1.0f, 0.35f, 0.35f, 0.95f) : new Color(0.2f, 0.92f, 0.45f, 0.95f);
                        DrawBezierWire(choiceOut, choiceNodeIn, choiceWireCol, 3.5f);
                    }
                }

                DrawConnectedGraphWires(node, visibleWorld);
            }

            if (_data.Triggers != null)
            {
                for (int t = 0; t < _data.Triggers.Count; t++)
                {
                    var trg = _data.Triggers[t];
                    if (trg == null || string.IsNullOrEmpty(trg.TargetNodeId)) continue;
                    if (_cachedTriggerOutSockets.TryGetValue(trg.TriggerId, out var trgOut)
                        && _cachedNodeInputSockets.TryGetValue(trg.TargetNodeId, out var targetIn))
                    {
                        DrawBezierWire(trgOut, targetIn, new Color(1.0f, 0.85f, 0.25f, 0.95f), 3.5f);
                    }
                }
            }
        }

        private bool SceneContainsLine(string id)
        {
            if (string.IsNullOrEmpty(id) || _data == null || _data.Nodes == null) return false;
            for (int n = 0; n < _data.Nodes.Count; n++)
            {
                var node = _data.Nodes[n];
                if (node == null || node.Dialogues == null) continue;
                for (int d = 0; d < node.Dialogues.Count; d++)
                {
                    var line = node.Dialogues[d];
                    if (line != null && string.Equals(line.LineId, id, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            return false;
        }

        private bool SceneContainsConsequence(string id)
        {
            if (string.IsNullOrEmpty(id) || _data == null || _data.Nodes == null) return false;
            for (int n = 0; n < _data.Nodes.Count; n++)
            {
                var node = _data.Nodes[n];
                if (node == null || node.Consequences == null) continue;
                for (int k = 0; k < node.Consequences.Count; k++)
                {
                    var cons = node.Consequences[k];
                    if (cons != null && string.Equals(cons.ConsequenceId, id, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            return false;
        }

        private void FinalizeGlobalWireDraft(Vector2 mousePos)
        {
            // Déclencheur : déposé sur l'entrée d'un nœud, sa sortie entre dans ce nœud.
            if (_wireDraft.IsTrigger && _wireDraft.SourceTrigger != null)
            {
                foreach (var kvp in _cachedNodeInputSockets)
                {
                    if (Vector2.Distance(kvp.Value, mousePos) < 36f)
                    {
                        _wireDraft.SourceTrigger.TargetNodeId = kvp.Key;
                        return;
                    }
                }
                return;
            }

            string targetId = null;

            // 1. Socket de dialogue/événement/conséquence LA PLUS PROCHE (pas la première
            // du dictionnaire : en graphe dense plusieurs sockets sont dans le rayon).
            float bestDist = 32f;
            foreach (var kvp in _cachedInputSockets)
            {
                float dist = Vector2.Distance(kvp.Value, mousePos);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    targetId = kvp.Key;
                }
            }

            // 2. Test de collision avec les sockets de Nœud complet
            if (string.IsNullOrEmpty(targetId))
            {
                float bestNodeDist = 36f;
                foreach (var kvp in _cachedNodeInputSockets)
                {
                    float dist = Vector2.Distance(kvp.Value, mousePos);
                    if (dist < bestNodeDist)
                    {
                        bestNodeDist = dist;
                        targetId = kvp.Key;
                    }
                }
            }

            if (string.IsNullOrEmpty(targetId)) return;

            if (_wireDraft.IsEvent && _wireDraft.SourceEvent != null)
            {
                _wireDraft.SourceEvent.NextEventId = targetId;
            }
            else if (_wireDraft.FromChallengeLink && _wireDraft.SourceChallenge != null)
            {
                // La sortie ✔/✕ accepte une conséquence (prioritaire) ou un dialogue normal.
                // Le wire fait foi : le champ concurrent est effacé pour éviter toute ambiguïté.
                if (SceneContainsConsequence(targetId))
                {
                    if (_wireDraft.ChallengeLinkIsSuccess)
                    {
                        _wireDraft.SourceChallenge.SuccessConsequenceId = targetId;
                        _wireDraft.SourceChallenge.SuccessNextLineId = "";
                    }
                    else
                    {
                        _wireDraft.SourceChallenge.FailureConsequenceId = targetId;
                        _wireDraft.SourceChallenge.FailureNextLineId = "";
                    }
                }
                else if (SceneContainsLine(targetId))
                {
                    if (_wireDraft.ChallengeLinkIsSuccess)
                    {
                        _wireDraft.SourceChallenge.SuccessNextLineId = targetId;
                        _wireDraft.SourceChallenge.SuccessConsequenceId = "";
                    }
                    else
                    {
                        _wireDraft.SourceChallenge.FailureNextLineId = targetId;
                        _wireDraft.SourceChallenge.FailureConsequenceId = "";
                    }
                }
            }
            else if (_wireDraft.FromConsequence && _wireDraft.SourceConsequence != null)
            {
                // Les deux sorties (→Diag et →Cons) acceptent répliques comme conséquences.
                // Le wire fait foi : le champ concurrent est effacé pour éviter toute ambiguïté.
                if (SceneContainsConsequence(targetId))
                {
                    _wireDraft.SourceConsequence.NextConsequenceId = targetId;
                    _wireDraft.SourceConsequence.NextLineId = "";
                }
                else if (SceneContainsLine(targetId))
                {
                    _wireDraft.SourceConsequence.NextLineId = targetId;
                    _wireDraft.SourceConsequence.NextConsequenceId = "";
                }
            }
            else if (_wireDraft.FromChoice && _wireDraft.SourceChoice != null)
            {
                _wireDraft.SourceChoice.NextLineId = targetId;
            }
            else if (_wireDraft.SourceLine != null)
            {
                _wireDraft.SourceLine.NextLineId = targetId;
            }

            if (Killtime.Audio.KilltimeAudioManager.Instance != null)
            {
                Killtime.Audio.KilltimeAudioManager.Instance.PlayUI(Killtime.Audio.SoundId.UI_Filter, 0.6f);
            }
        }

        private void DrawNodeFrame(SceneNodeData node, int nodeIndex, Vector2 mouseWorld)
        {
            Rect frameRect = ComputeNodeBoundingBox(node);
            Rect headerRect = new Rect(frameRect.x, frameRect.y, frameRect.width, 38f);
            // Zone boutons réelle : 90 + 82 + 95 + 26 + espacements ≈ 315px calée à droite.
            Rect actionsArea = new Rect(headerRect.xMax - 325f, headerRect.y + 5f, 315f, 26f);
            Rect nodeInSocket = new Rect(frameRect.x - 9f, frameRect.y + 10f, 18f, 18f);
            Rect nodeOutSocket = new Rect(frameRect.xMax - 9f, frameRect.y + 10f, 18f, 18f);

            Event evt = Event.current;
            int nodeControlId = GUIUtility.GetControlID(FocusType.Passive);

            // Glissement du Nœud Englobant dans l'espace monde (protection stricte des boutons d'actions)
            bool isOverHeaderDragZone = headerRect.Contains(mouseWorld) && !actionsArea.Contains(mouseWorld);

            // Double-clic sur la frame = cadrer ce nœud en lisible (raccourci direct vers
            // le focus, sans passer par Aller à / ◀ ▶).
            if (evt.type == EventType.MouseDown && evt.button == 0 && evt.clickCount == 2 && !_pickerCapturesMouse && frameRect.Contains(mouseWorld))
            {
                _draggingNodeId = null;
                GUIUtility.hotControl = 0;
                FocusNodeByIndex(nodeIndex);
                evt.Use();
            }
            else if (evt.type == EventType.MouseDown && evt.button == 0 && evt.clickCount < 2 && !_pickerCapturesMouse && isOverHeaderDragZone)
            {
                GUIUtility.hotControl = nodeControlId;
                _draggingNodeId = node.NodeId;
                _dragNodeStartMouse = mouseWorld;
                evt.Use();
            }
            else if ((evt.type == EventType.MouseDrag || evt.type == EventType.MouseMove) && GUIUtility.hotControl == nodeControlId && _draggingNodeId == node.NodeId)
            {
                Vector2 delta = mouseWorld - _dragNodeStartMouse;
                _dragNodeStartMouse = mouseWorld;

                node.GraphPosX += delta.x;
                node.GraphPosY += delta.y;

                for (int d = 0; d < node.Dialogues.Count; d++)
                {
                    node.Dialogues[d].GraphPosX += delta.x;
                    node.Dialogues[d].GraphPosY += delta.y;
                }
                for (int e = 0; e < node.Events.Count; e++)
                {
                    node.Events[e].GraphPosX += delta.x;
                    node.Events[e].GraphPosY += delta.y;
                }
                if (node.Consequences != null)
                {
                    for (int k = 0; k < node.Consequences.Count; k++)
                    {
                        if (node.Consequences[k] == null) continue;
                        node.Consequences[k].GraphPosX += delta.x;
                        node.Consequences[k].GraphPosY += delta.y;
                    }
                }
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == nodeControlId)
            {
                GUIUtility.hotControl = 0;
                _draggingNodeId = null;
                evt.Use();
            }

            Color frameBorderCol = node.Kind switch
            {
                ScenarioNodeKind.Briefing => new Color(0.0f, 0.85f, 1.0f, 0.75f),
                ScenarioNodeKind.Choice => new Color(1.0f, 0.78f, 0.15f, 0.75f),
                ScenarioNodeKind.Objective => new Color(0.2f, 0.92f, 0.45f, 0.75f),
                ScenarioNodeKind.Resolution => new Color(0.85f, 0.35f, 1.0f, 0.75f),
                _ => new Color(0.5f, 0.5f, 0.5f, 0.75f)
            };

            DrawGraphSolidRect(frameRect, new Color(0.025f, 0.04f, 0.065f, 0.75f));
            DrawGraphSolidRect(headerRect, new Color(frameBorderCol.r, frameBorderCol.g, frameBorderCol.b, 0.25f));

            DrawGraphSolidRect(new Rect(frameRect.x, frameRect.y, frameRect.width, 2.5f), frameBorderCol);
            DrawGraphSolidRect(new Rect(frameRect.x, frameRect.yMax - 1.5f, frameRect.width, 1.5f), frameBorderCol);
            DrawGraphSolidRect(new Rect(frameRect.x, frameRect.y, 1.5f, frameRect.height), frameBorderCol);
            DrawGraphSolidRect(new Rect(frameRect.xMax - 1.5f, frameRect.y, 1.5f, frameRect.height), frameBorderCol);

            DrawGraphSolidRect(nodeInSocket, frameBorderCol);

            GUI.color = Color.white;
            float titleW = Mathf.Max(90f, frameRect.width - 475f);
            GraphLabel(new Rect(headerRect.x + 16f, headerRect.y + 8f, titleW, 22f), $"<b>■ NŒUD #{nodeIndex + 1} : {node.Title.ToUpperInvariant()}</b> [{node.Kind}]");

            GUI.color = new Color(1f, 1f, 1f, 0.75f);
            float idLabelW = Mathf.Max(40f, Mathf.Min(130f, frameRect.width - titleW - 350f));
            GraphLabel(new Rect(headerRect.x + 16f + titleW + 4f, headerRect.y + 8f, idLabelW, 20f), $"ID: <i>{node.NodeId}</i>");
            GUI.color = Color.white;

            float btnY = headerRect.y + 7f;
            float btnH = 22f;
            float bx = headerRect.xMax - 10f;
            Color prevBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.85f, 0.2f, 0.2f);
            Rect delBtn = new Rect(bx - 26f, btnY, 26f, btnH);
            bool deleteNode = GraphButton(delBtn, "✕");
            bx -= 26f + 6f;
            GUI.backgroundColor = new Color(1f, 0.45f, 0.2f);
            Rect evtBtn = new Rect(bx - 95f, btnY, 95f, btnH);
            bool addEvt = GraphButton(evtBtn, "+ Événement");
            bx -= 95f + 6f;
            GUI.backgroundColor = new Color(0.35f, 0.85f, 0.45f);
            Rect consBtn = new Rect(bx - 82f, btnY, 82f, btnH);
            bool addCons = GraphButton(consBtn, "+ Conséq.");
            bx -= 82f + 6f;
            GUI.backgroundColor = new Color(0.2f, 0.75f, 1f);
            Rect dlgBtn = new Rect(bx - 90f, btnY, 90f, btnH);
            bool addDlg = GraphButton(dlgBtn, "+ Dialogue");
            GUI.backgroundColor = prevBg;

            if (addDlg)
            {
                int nextIdx = node.Dialogues.Count + 1;
                ComputeFreeSpotForNewCard(node, out float freeX, out float freeY);
                node.Dialogues.Add(new SceneDialogueLineData
                {
                    LineId = $"line_{nodeIndex + 1}_{nextIdx}",
                    SpeakerId = "",
                    Speech = "Nouvelle réplique...",
                    GraphPosX = freeX,
                    GraphPosY = freeY
                });
            }

            if (addEvt)
            {
                int nextIdx = node.Events.Count + 1;
                ComputeFreeSpotForNewCard(node, out float freeX, out float freeY);
                node.Events.Add(new SceneEventData
                {
                    EventId = $"evt_{nodeIndex + 1}_{nextIdx}",
                    Title = "Action Scénique",
                    GraphPosX = freeX,
                    GraphPosY = freeY
                });
            }

            if (addCons)
            {
                if (node.Consequences == null) node.Consequences = new System.Collections.Generic.List<SceneConsequenceData>();
                int nextIdx = node.Consequences.Count + 1;
                ComputeFreeSpotForNewCard(node, out float freeX, out float freeY);
                node.Consequences.Add(new SceneConsequenceData
                {
                    ConsequenceId = $"cons_{nodeIndex + 1}_{nextIdx}",
                    Title = "Récompense",
                    GraphPosX = freeX,
                    GraphPosY = freeY
                });
            }

            if (deleteNode)
            {
                _data.Nodes.RemoveAt(nodeIndex);
                return;
            }

            if (!string.IsNullOrEmpty(node.NextNodeId))
            {
                DrawGraphSolidRect(nodeOutSocket, new Color(1.0f, 0.65f, 0.15f));
            }

            float contentY = frameRect.y + 46f;
            if (node.Dialogues.Count == 0 && node.Events.Count == 0 && (node.Consequences == null || node.Consequences.Count == 0))
            {
                node.Objectives ??= new System.Collections.Generic.List<ScenarioObjective>();
                float innerObjX = frameRect.x + 16f;
                float innerObjW = frameRect.width - 32f;

                // Sélecteur de type : transforme n'importe quel nœud en nœud Objectif (vert) comme le nœud #2.
                GraphLabel(new Rect(innerObjX, contentY, 45f, 22f), "Type :");
                int kindCount = Enum.GetValues(typeof(ScenarioNodeKind)).Length;
                if (GraphButton(new Rect(innerObjX + 47f, contentY, 22f, 22f), "◀"))
                    node.Kind = (ScenarioNodeKind)(((int)node.Kind - 1 + kindCount) % kindCount);
                GraphLabel(new Rect(innerObjX + 71f, contentY, 95f, 22f), $"<b>{node.Kind}</b>");
                if (GraphButton(new Rect(innerObjX + 168f, contentY, 22f, 22f), "▶"))
                    node.Kind = (ScenarioNodeKind)(((int)node.Kind + 1) % kindCount);
                contentY += 26f;

                GraphLabel(new Rect(innerObjX, contentY, 45f, 22f), "Titre :");
                node.Title = GraphTextField(new Rect(innerObjX + 47f, contentY, Mathf.Max(60f, innerObjW - 47f), 22f), node.Title ?? "");
                contentY += 26f;

                GraphLabel(new Rect(innerObjX, contentY, innerObjW, 18f), "<color=grey>Narration :</color>");
                contentY += 18f;
                node.Body = GraphTextArea(new Rect(innerObjX, contentY, innerObjW, 54f), node.Body ?? "");
                contentY += 60f;

                GraphLabel(new Rect(innerObjX, contentY, 140f, 22f), $"<b>Objectifs ({node.Objectives.Count})</b>");
                Color prevFrameBg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.2f, 0.92f, 0.45f);
                if (GraphButton(new Rect(innerObjX + innerObjW - 92f, contentY, 92f, 22f), "+ Objectif"))
                    node.Objectives.Add(new ScenarioObjective { Id = $"obj_{node.Objectives.Count + 1}", Label = "Nouvel objectif...", Optional = false });
                GUI.backgroundColor = prevFrameBg;
                contentY += 26f;

                for (int o = 0; o < node.Objectives.Count; o++)
                {
                    var obj = node.Objectives[o];
                    if (obj == null) continue;
                    Rect objRow = new Rect(innerObjX, contentY, innerObjW, 24f);
                    DrawGraphSolidRect(objRow, new Color(0.04f, 0.14f, 0.10f, 0.85f));
                    Color prevObjBg = GUI.backgroundColor;
                    GUI.backgroundColor = obj.Optional ? new Color(0.45f, 0.45f, 0.48f) : new Color(0.2f, 0.75f, 1f);
                    if (GraphButton(new Rect(objRow.x + 2f, objRow.y + 2f, 56f, 20f), obj.Optional ? "Opt." : "Princ."))
                        obj.Optional = !obj.Optional;
                    GUI.backgroundColor = prevObjBg;
                    obj.Label = GraphTextField(new Rect(objRow.x + 62f, objRow.y + 2f, Mathf.Max(40f, objRow.width - 62f - 28f), 20f), obj.Label ?? "");
                    GUI.backgroundColor = new Color(0.85f, 0.25f, 0.25f);
                    if (GraphButton(new Rect(objRow.xMax - 24f, objRow.y + 2f, 22f, 20f), "✕"))
                    {
                        node.Objectives.RemoveAt(o);
                        GUI.backgroundColor = Color.white;
                        break;
                    }
                    GUI.backgroundColor = Color.white;
                    contentY += 28f;
                }
                contentY += 6f;
            }

            if (node.Choices != null && node.Choices.Count > 0)
            {
                for (int c = 0; c < node.Choices.Count; c++)
                {
                    var ch = node.Choices[c];
                    if (ch == null) continue;
                    Rect choiceRow = new Rect(frameRect.x + 16f, contentY, frameRect.width - 32f, 26f);
                    DrawGraphSolidRect(choiceRow, new Color(0.08f, 0.12f, 0.17f, 0.90f));

                    GraphLabel(new Rect(choiceRow.x + 8f, choiceRow.y + 3f, choiceRow.width - 200f, 20f), $"<b>Choix #{c + 1} :</b> {ch.Label}");
                    GUI.color = new Color(1f, 0.75f, 0.2f);
                    GraphLabel(new Rect(choiceRow.xMax - 190f, choiceRow.y + 3f, 180f, 20f), $"➔ <b>{ch.NextNodeId}</b>");
                    GUI.color = Color.white;

                    Rect choiceSocket = new Rect(frameRect.xMax - 8f, choiceRow.y + 5f, 16f, 16f);
                    DrawGraphSolidRect(choiceSocket, ch.Id.Contains("hostile") ? new Color(1.0f, 0.35f, 0.35f) : new Color(0.2f, 0.92f, 0.45f));
                    contentY += 28f;
                }
            }
        }

        private void ComputeVisibleNodes(Rect canvasRect, out int fully, out int partial, out int total)
        {
            total = _data.Nodes.Count;
            fully = 0;
            partial = 0;
            if (total == 0) return;
            Rect viewWorld = GetVisibleWorldRect(canvasRect);
            for (int i = 0; i < total; i++)
            {
                Rect b = ComputeNodeBoundingBox(_data.Nodes[i]);
                // "Visible" = ENTIÈREMENT dans le champ. Un nœud à cheval sur le bord compte
                // comme partiel (c'est exactement le cas "tronqué" signalé).
                if (viewWorld.Overlaps(b))
                {
                    if (viewWorld.xMin <= b.xMin && viewWorld.yMin <= b.yMin
                        && viewWorld.xMax >= b.xMax && viewWorld.yMax >= b.yMax)
                        fully++;
                    else
                        partial++;
                }
            }
        }

        private Rect GetVisibleWorldRect(Rect canvasRect)
        {
            float zoom = Mathf.Max(0.0001f, _zoom);
            Vector2 wMin = (Vector2.zero - _graphPan) / zoom;
            Vector2 wMax = (new Vector2(canvasRect.width, canvasRect.height) - _graphPan) / zoom;
            return Rect.MinMaxRect(wMin.x, wMin.y, wMax.x, wMax.y);
        }

        private static bool IsWorldRectVisible(Rect content, Rect viewport)
        {
            return viewport.Overlaps(content);
        }

        private void DrawZoomControlsOverlay(Rect canvasRect)
        {
            // HUD ancré en HAUT-GAUCHE du canvas (zone toujours visible) et dessiné en GUI
            // absolu : aucun layout, aucun chevauchement, aucune dépendance au bas de fenêtre.
            const float hudH = 26f;
            float hudW = Mathf.Min(620f, canvasRect.width - 20f);
            if (hudW < 300f || canvasRect.height < 120f) return;
            Rect hud = new Rect(canvasRect.x + 10f, canvasRect.y + 8f, hudW, hudH);
            DrawSolidRect(hud, new Color(0.02f, 0.04f, 0.07f, 0.90f));

            float x = hud.x + 6f;
            float y = hud.y + 3f;
            const float h = 20f;
            Color prevBg = GUI.backgroundColor;

            GUI.Label(new Rect(x, y, 74f, h), $"<b>{Mathf.RoundToInt(_zoom * 100f)}%</b>");
            x += 78f;
            if (GUI.Button(new Rect(x, y, 26f, h), "−")) ZoomAroundCenter(_zoom - 0.15f);
            x += 30f;
            if (GUI.Button(new Rect(x, y, 26f, h), "+")) ZoomAroundCenter(_zoom + 0.15f);
            x += 30f;
            if (GUI.Button(new Rect(x, y, 48f, h), "100%")) ZoomAroundCenter(1.0f);
            x += 52f;
            GUI.backgroundColor = new Color(1.0f, 0.75f, 0.2f);
            if (GUI.Button(new Rect(x, y, 86f, h), "Cadrer Tout")) FocusAllNodes();
            GUI.backgroundColor = prevBg;
            x += 90f;

            // Stepper nœud par nœud : garantit qu'on peut voir TOUS les nœuds un par un
            // à zoom lisible, même quand la scène complète ne tient pas à l'écran.
            if (_data.Nodes.Count > 0)
            {
                _focusedNodeIndex = Mathf.Clamp(_focusedNodeIndex, 0, _data.Nodes.Count - 1);
                if (GUI.Button(new Rect(x, y, 26f, h), "◀")) FocusNodeByIndex(_focusedNodeIndex - 1);
                x += 30f;
                GUI.Label(new Rect(x, y, 52f, h), $"<b>{_focusedNodeIndex + 1}/{_data.Nodes.Count}</b>");
                x += 56f;
                if (GUI.Button(new Rect(x, y, 26f, h), "▶")) FocusNodeByIndex(_focusedNodeIndex + 1);
                x += 30f;
            }

            // Compteur live : preuve à l'écran que tous les nœuds sont joignables.
            // Vert = tous ENTIERS dans le champ. Orange = chevauchants ou hors champ
            // → Cadrer Tout ou ◀ ▶.
            float rem = hud.xMax - 4f - x;
            if (rem > 110f)
            {
                ComputeVisibleNodes(canvasRect, out int full, out int part, out int tot);
                if (full == tot)
                {
                    GUI.Label(new Rect(x, y, rem, h), $"<color=#7CFF9B><b>{full}/{tot} visibles</b></color>");
                }
                else
                {
                    GUI.Label(new Rect(x, y, rem, h), $"<color=#FFC94D><b>{full}/{tot} +{part} bords</b></color>");
                }
            }
        }

        private void DrawGraphGridBackground(Rect rect)
        {
            DrawSolidRect(rect, new Color(0.035f, 0.045f, 0.06f, 1f));
            const float baseGrid = 32f;
            float gridSize = baseGrid * _zoom;
            if (gridSize < 10f) gridSize = 10f;
            float offX = ((_graphPan.x % gridSize) + gridSize) % gridSize;
            float offY = ((_graphPan.y % gridSize) + gridSize) % gridSize;

            Color gridSub = new Color(1f, 1f, 1f, 0.025f);
            Color gridMain = new Color(0.0f, 0.8f, 1f, 0.06f);

            // Lignes majeures stables : indexées en espace monde, pas en espace écran
            // (l'ancien modulo écran faisait sauter les lignes fortes pendant le pan).
            float worldLeft = (0f - _graphPan.x) / _zoom;
            float worldTop = (0f - _graphPan.y) / _zoom;
            int idx0x = Mathf.FloorToInt(worldLeft / baseGrid);
            int idx0y = Mathf.FloorToInt(worldTop / baseGrid);
            int cols = Mathf.CeilToInt(rect.width / gridSize) + 2;
            int rows = Mathf.CeilToInt(rect.height / gridSize) + 2;
            for (int i = 0; i <= cols; i++)
            {
                float x = rect.x + offX + (i - 1) * gridSize;
                if (x < rect.x || x > rect.xMax) continue;
                bool isMajor = ((idx0x + i) % 4) == 0;
                DrawSolidRect(new Rect(x, rect.y, 1f, rect.height), isMajor ? gridMain : gridSub);
            }
            for (int j = 0; j <= rows; j++)
            {
                float y = rect.y + offY + (j - 1) * gridSize;
                if (y < rect.y || y > rect.yMax) continue;
                bool isMajor = ((idx0y + j) % 4) == 0;
                DrawSolidRect(new Rect(rect.x, y, rect.width, 1f), isMajor ? gridMain : gridSub);
            }
        }

        private void DrawConnectedGraphWires(SceneNodeData node, Rect visibleWorld)
        {
            for (int d = 0; d < node.Dialogues.Count; d++)
            {
                var line = node.Dialogues[d];
                Vector2 size = GetDialogueCardSize(line);
                Vector2 cardPos = new Vector2(line.GraphPosX, line.GraphPosY);

                if (!string.IsNullOrEmpty(line.NextLineId)
                    && _cachedInputSockets.TryGetValue(line.NextLineId, out var targetIn))
                {
                    Vector2 outSocket = new Vector2(cardPos.x + size.x, cardPos.y + 26f);
                    DrawBezierWire(outSocket, targetIn, new Color(0.0f, 0.85f, 1.0f, 0.95f), 3.5f);
                }

                var choiceRowYs = new List<float>();
                GetDialogueChoiceRowOffsets(line, choiceRowYs);
                for (int c = 0; c < line.Choices.Count; c++)
                {
                    var ch = line.Choices[c];
                    if (ch == null) continue;
                    if (!string.IsNullOrEmpty(ch.NextLineId)
                        && _cachedInputSockets.TryGetValue(ch.NextLineId, out var chTargetIn))
                    {
                        // Aligné sur la rangée GUI réelle (cf. DrawDialogueCard : +100 + extras).
                        float rowY = (c >= 0 && c < choiceRowYs.Count)
                            ? cardPos.y + choiceRowYs[c] + 10f
                            : cardPos.y + 110f + (c * 22f);
                        Vector2 chOutSocket = new Vector2(cardPos.x + size.x, rowY);
                        bool wVs = ch.Challenge != null && ch.Challenge.HasChallenge && ch.Challenge.IsOpposed;
                        bool wChal = ch.Challenge != null && ch.Challenge.HasChallenge;
                        DrawBezierWire(chOutSocket, chTargetIn, wVs ? new Color(1f, 0.55f, 0.2f, 0.95f) : (wChal ? new Color(1f, 0.85f, 0.3f, 0.95f) : new Color(1.0f, 0.8f, 0.2f, 0.95f)), 3.0f);
                    }

                    // Liens du défi : ✔ et ✕ vers cartes Conséquence OU répliques directes.
                    if (ch.Challenge != null && ch.Challenge.HasChallenge)
                    {
                        float linkTop = (c >= 0 && c < choiceRowYs.Count)
                            ? choiceRowYs[c] + 22f + GetChallengeConfigRows(ch.Challenge) * 20f
                            : 122f + (c * 22f);
                        string okId = !string.IsNullOrEmpty(ch.Challenge.SuccessConsequenceId)
                            ? ch.Challenge.SuccessConsequenceId : ch.Challenge.SuccessNextLineId;
                        if (!string.IsNullOrEmpty(okId)
                            && _cachedInputSockets.TryGetValue(okId, out var okIn))
                        {
                            DrawBezierWire(new Vector2(cardPos.x + size.x, cardPos.y + linkTop + 10f), okIn, new Color(0.35f, 1f, 0.55f, 0.95f), 3.0f);
                        }
                        string koId = !string.IsNullOrEmpty(ch.Challenge.FailureConsequenceId)
                            ? ch.Challenge.FailureConsequenceId : ch.Challenge.FailureNextLineId;
                        if (!string.IsNullOrEmpty(koId)
                            && _cachedInputSockets.TryGetValue(koId, out var koIn))
                        {
                            DrawBezierWire(new Vector2(cardPos.x + size.x, cardPos.y + linkTop + 30f), koIn, new Color(1f, 0.4f, 0.35f, 0.95f), 3.0f);
                        }
                    }
                }

                if (line.AutoSkillCheck != null && line.AutoSkillCheck.HasAutoCheck)
                {
                    if (!string.IsNullOrEmpty(line.AutoSkillCheck.SuccessNextLineId)
                        && _cachedInputSockets.TryGetValue(line.AutoSkillCheck.SuccessNextLineId, out var sIn))
                    {
                        DrawBezierWire(new Vector2(cardPos.x + size.x, cardPos.y + 70f), sIn, Color.green, 2.5f);
                    }
                    if (!string.IsNullOrEmpty(line.AutoSkillCheck.FailureNextLineId)
                        && _cachedInputSockets.TryGetValue(line.AutoSkillCheck.FailureNextLineId, out var fIn))
                    {
                        DrawBezierWire(new Vector2(cardPos.x + size.x, cardPos.y + 90f), fIn, new Color(1f, 0.35f, 0.35f), 2.5f);
                    }
                }
            }

            for (int e = 0; e < node.Events.Count; e++)
            {
                var ev = node.Events[e];
                Vector2 size = GetEventCardSize(ev);
                Vector2 cardPos = new Vector2(ev.GraphPosX, ev.GraphPosY);
                if (!string.IsNullOrEmpty(ev.NextEventId)
                    && _cachedInputSockets.TryGetValue(ev.NextEventId, out var targetIn))
                {
                    Vector2 outSocket = new Vector2(cardPos.x + size.x, cardPos.y + 26f);
                    DrawBezierWire(outSocket, targetIn, new Color(0.85f, 0.45f, 1.0f, 0.95f), 3.5f);
                }
            }

            if (node.Consequences != null)
            {
                for (int k = 0; k < node.Consequences.Count; k++)
                {
                    var cons = node.Consequences[k];
                    if (cons == null) continue;
                    Vector2 size = GetConsequenceCardSize(cons);
                    Vector2 cardPos = new Vector2(cons.GraphPosX, cons.GraphPosY);
                    if (!string.IsNullOrEmpty(cons.NextLineId)
                        && _cachedInputSockets.TryGetValue(cons.NextLineId, out var lineIn))
                    {
                        DrawBezierWire(new Vector2(cardPos.x + size.x, cardPos.y + 26f), lineIn, new Color(0.0f, 0.85f, 1.0f, 0.95f), 3.0f);
                    }
                    if (!string.IsNullOrEmpty(cons.NextConsequenceId)
                        && _cachedInputSockets.TryGetValue(cons.NextConsequenceId, out var consIn))
                    {
                        float nextRowTop = 72f + (cons.HasDialogue ? 66f : 0f);
                        DrawBezierWire(new Vector2(cardPos.x + size.x, cardPos.y + nextRowTop + 10f), consIn, new Color(0.4f, 1.0f, 0.5f, 0.95f), 3.0f);
                    }
                }
            }
        }

        private static string TrimHover(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\n", " ").Replace("\r", "").Trim();
            return s.Length > max ? s.Substring(0, max) + "…" : s;
        }

        private void DrawDialogueCard(SceneDialogueLineData line, int index, SceneNodeData node, Vector2 mouseWorld)
        {
            if (string.IsNullOrEmpty(line.LineId)) line.LineId = $"line_{index + 1}";
            if (line.Choices == null) line.Choices = new System.Collections.Generic.List<SceneDialogueChoiceData>();
            if (line.Ambience == null) line.Ambience = new SceneAmbienceData();
            if (line.Prerequisite == null) line.Prerequisite = new ScenePrerequisiteData();
            if (line.AutoSkillCheck == null) line.AutoSkillCheck = new SceneAutoSkillCheckData();

            Vector2 size = GetDialogueCardSize(line);
            float cardW = size.x;
            float cardH = size.y;

            Rect cardRect = new Rect(line.GraphPosX, line.GraphPosY, cardW, cardH);
            Rect headerRect = new Rect(cardRect.x, cardRect.y, cardW, 26f);
            Rect closeRect = new Rect(headerRect.xMax - 22f, headerRect.y + 3f, 18f, 18f);
            Rect dragHeaderRect = new Rect(headerRect.x, headerRect.y, headerRect.width - 26f, headerRect.height);
            Rect resizeGripRect = new Rect(cardRect.xMax - 14f, cardRect.yMax - 14f, 14f, 14f);

            Event evt = Event.current;
            string dragKey = $"dialogue_{index}_{line.LineId}";
            string resizeKey = $"resize_{dragKey}";
            int dragControlId = GUIUtility.GetControlID(FocusType.Passive);
            int resizeControlId = GUIUtility.GetControlID(FocusType.Passive);

            // Redimensionnement de la carte
            if (GraphPrimaryDown(mouseWorld, resizeGripRect))
            {
                GUIUtility.hotControl = resizeControlId;
                _resizingCardKey = resizeKey;
                evt.Use();
            }
            else if ((evt.type == EventType.MouseDrag || evt.type == EventType.MouseMove) && GUIUtility.hotControl == resizeControlId && _resizingCardKey == resizeKey)
            {
                line.CardWidth = Mathf.Clamp(mouseWorld.x - cardRect.x, 320f, 750f);
                line.CardHeight = Mathf.Clamp(mouseWorld.y - cardRect.y, 160f, 650f);
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == resizeControlId)
            {
                GUIUtility.hotControl = 0;
                _resizingCardKey = null;
                evt.Use();
            }

            // Déplacement de la carte
            if (GraphPrimaryDown(mouseWorld, dragHeaderRect))
            {
                GUIUtility.hotControl = dragControlId;
                _draggingCardKey = dragKey;
                _dragOffset = mouseWorld - new Vector2(line.GraphPosX, line.GraphPosY);
                evt.Use();
            }
            else if ((evt.type == EventType.MouseDrag || evt.type == EventType.MouseMove) && GUIUtility.hotControl == dragControlId && _draggingCardKey == dragKey)
            {
                line.GraphPosX = mouseWorld.x - _dragOffset.x;
                line.GraphPosY = mouseWorld.y - _dragOffset.y;
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == dragControlId)
            {
                GUIUtility.hotControl = 0;
                _draggingCardKey = null;
                evt.Use();
            }

            // 1. Rendu d'arrière-plan de la Carte et de l'En-tête
            DrawGraphSolidRect(cardRect, new Color(0.08f, 0.11f, 0.15f, 0.95f));
            int chalSDCount = 0;
            int chalVSCount = 0;
            for (int hc0 = 0; hc0 < line.Choices.Count; hc0++)
            {
                var hx = line.Choices[hc0];
                if (hx != null && hx.Challenge != null && hx.Challenge.HasChallenge)
                {
                    if (hx.Challenge.IsOpposed) chalVSCount++;
                    else chalSDCount++;
                }
            }
            Color headerCol = line.Choices.Count > 0 ? new Color(0.35f, 0.24f, 0.05f) : (line.AutoSkillCheck != null && line.AutoSkillCheck.HasAutoCheck ? new Color(0.25f, 0.12f, 0.35f) : new Color(0.08f, 0.28f, 0.36f));
            if (chalVSCount > 0) headerCol = new Color(0.42f, 0.24f, 0.08f);
            DrawGraphSolidRect(headerRect, headerCol);

            // Bordure de carte nette
            DrawGraphSolidRect(new Rect(cardRect.x, cardRect.y, cardRect.width, 1f), headerCol * 1.4f);
            DrawGraphSolidRect(new Rect(cardRect.x, cardRect.yMax - 1f, cardRect.width, 1f), new Color(1f, 1f, 1f, 0.08f));
            DrawGraphSolidRect(new Rect(cardRect.x, cardRect.y, 1f, cardRect.height), new Color(1f, 1f, 1f, 0.08f));
            DrawGraphSolidRect(new Rect(cardRect.xMax - 1f, cardRect.y, 1f, cardRect.height), new Color(1f, 1f, 1f, 0.08f));

            // 2. Port d'Entrée : vert + gros si point d'entrée, cadre rouge si
            // plusieurs entrées (erreur utilisateur).
            DrawEntryInputPort(cardRect, Color.cyan,
                !DialogueIdHasIncoming(node, line.LineId),
                CountDialogueEntries(node) > 1);

            // 3. Titre & Locuteur
            string chalBadge = chalVSCount > 0 ? $" ⚔️x{chalVSCount}" : (chalSDCount > 0 ? $" 🎲x{chalSDCount}" : "");
            GraphLabel(new Rect(headerRect.x + 8f, headerRect.y + 3f, cardW - 35f, 20f), $"<b>◆ {line.SpeakerId}</b> (<i>{line.LineId}</i>){chalBadge}");

            // 4. Bouton Supprimer
            GUI.backgroundColor = new Color(0.85f, 0.22f, 0.22f, 1f);
            GUI.color = Color.white;
            if (GraphButton(closeRect, "✕"))
            {
                node.Dialogues.RemoveAt(index);
                if (_draggingCardKey == dragKey)
                {
                    _draggingCardKey = null;
                    GUIUtility.hotControl = 0;
                }
                GUI.backgroundColor = Color.white;
                evt.Use();
                return;
            }
            GUI.backgroundColor = Color.white;

            // 5. Poignée de redimensionnement
            GUI.color = new Color(1f, 1f, 1f, 0.4f);
            GraphLabel(resizeGripRect, "◢");
            GUI.color = Color.white;

            // Survol du header : mémorise une infobulle lisible (dessinée après la fermeture du clip).
            if (_zoom < 0.85f && !_isPanning && _draggingCardKey == null && _resizingCardKey == null && !_wireDraft.IsActive
                && headerRect.Contains(mouseWorld))
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine(line.Speech ?? "");
                if (line.Choices.Count > 0)
                {
                    sb.AppendLine($"✦ {line.Choices.Count} choix :");
                    for (int hc = 0; hc < Mathf.Min(4, line.Choices.Count); hc++)
                    {
                        var hch = line.Choices[hc];
                        if (hch == null) continue;
                        string hl = hch.Label ?? "";
                        if (hl.Length > 90) hl = hl.Substring(0, 90) + "…";
                        string tag = "";
                        if (hch.Challenge != null && hch.Challenge.HasChallenge)
                        {
                            tag = hch.Challenge.IsOpposed ? $" [⚔️ {hch.Challenge.GetShortLabel()}]" : $" [🎲 {hch.Challenge.GetShortLabel()}]";
                            if (!string.IsNullOrEmpty(hch.Challenge.SuccessConsequenceId)) tag += $" ✔→🎁{hch.Challenge.SuccessConsequenceId}";
                            if (!string.IsNullOrEmpty(hch.Challenge.FailureConsequenceId)) tag += $" ✕→🎁{hch.Challenge.FailureConsequenceId}";
                            var hrw = hch.Challenge.SuccessRewards;
                            if (hrw != null)
                            {
                                if (hrw.EarnXP > 0) tag += $" +{hrw.EarnXP}XP";
                                if (hrw.EarnCredits > 0) tag += $" +{hrw.EarnCredits}CE";
                                var itemBits = new System.Collections.Generic.List<string>();
                                if (!string.IsNullOrEmpty(hrw.ItemRewardName))
                                    itemBits.Add(hrw.ItemRewardQuantity > 1 ? $"{hrw.ItemRewardName} x{hrw.ItemRewardQuantity}" : hrw.ItemRewardName);
                                if (hrw.AdditionalItems != null)
                                {
                                    for (int hi = 0; hi < hrw.AdditionalItems.Count; hi++)
                                    {
                                        var he = hrw.AdditionalItems[hi];
                                        if (he == null || string.IsNullOrEmpty(he.ItemName)) continue;
                                        itemBits.Add(he.Quantity > 1 ? $"{he.ItemName} x{he.Quantity}" : he.ItemName);
                                    }
                                }
                                if (itemBits.Count > 0) tag += " [" + string.Join(", ", itemBits) + "]";
                            }
                            if (hch.Challenge.FailureTriggersCombat) tag += " ✕⚔";
                        }
                        if (hch.Prerequisite != null && hch.Prerequisite.HasPrerequisite) tag += $" 🔒{hch.Prerequisite.GetSummary()}";
                        sb.AppendLine($"• {hl}{tag}");
                    }
                }
                else if (!string.IsNullOrEmpty(line.NextLineId))
                {
                    sb.AppendLine($"➔ {line.NextLineId}");
                }
                if (line.Prerequisite != null && line.Prerequisite.HasPrerequisite)
                    sb.AppendLine($"🔒 Prérequis : {line.Prerequisite.GetSummary()}");
                if (line.AutoSkillCheck != null && line.AutoSkillCheck.HasAutoCheck)
                    sb.AppendLine($"🎲 Auto : {SkillDefinitions.GetDisplayName(line.AutoSkillCheck.RequiredSkill)} SD {line.AutoSkillCheck.TargetDC}");
                if (line.Ambience != null && line.Ambience.HasAmbience && !string.IsNullOrEmpty(line.Ambience.SoundCueId))
                    sb.AppendLine($"🔊 {line.Ambience.SoundCueId}");
                _hasHoverCard = true;
                _hoverTitle = $"◆ {line.SpeakerId}  ({line.LineId})";
                _hoverBody = sb.ToString().Trim();
                _hoverScreenPos = _graphMouseScreenPos;
            }

            // 6. Contenu ÉDITABLE en GUI ABSOLU (pas de GUILayout : incompatible avec la matrice zoomée,
            //    c'était la cause du rendu écrasé/chevauché du screenshot).
            float innerX = cardRect.x + 8f;
            float innerW = cardW - 16f;
            float y = cardRect.y + 30f;

            GraphLabel(new Rect(innerX, y, 22f, 18f), "ID:");
            line.LineId = GraphTextField(new Rect(innerX + 24f, y, 70f, 18f), line.LineId ?? "");
            GraphLabel(new Rect(innerX + 98f, y, 28f, 18f), "Loc:");
            line.SpeakerId = GraphTextField(new Rect(innerX + 128f, y, 60f, 18f), line.SpeakerId ?? "");
            {
                Rect dropBtn = new Rect(innerX + 190f, y, 20f, 18f);
                string dropKey = $"dlg_{line.LineId}";
                if (GraphButton(dropBtn, "▼"))
                {
                    if (_actorDropKey == dropKey) _actorDropKey = null;
                    else OpenActorDropdown(dropKey, dropBtn, line.SpeakerId ?? "", v => line.SpeakerId = v);
                }
            }
            line.StageDirection = GraphTextField(new Rect(innerX + 212f, y, Mathf.Max(40f, innerW - 212f), 18f), line.StageDirection ?? "");
            y += 20f;

            line.Speech = GraphTextArea(new Rect(innerX, y, innerW, 48f), line.Speech ?? "");
            y += 50f;

            // Choix Multiples / Liaison : première rangée à cardY+100, pas vertical de 22 + 20/ligne de défi (référence des wires).
            var cardChoiceOffsets = new List<float>();
            GetDialogueChoiceRowOffsets(line, cardChoiceOffsets);
            if (line.Choices.Count > 0)
            {
                int skillCount = Enum.GetValues(typeof(SkillType)).Length;
                for (int c = 0; c < line.Choices.Count; c++)
                {
                    var ch = line.Choices[c];
                    if (ch == null) { y += 22f; continue; }
                    ch.Challenge ??= new SceneDialogueChallengeData();
                    var chal = ch.Challenge;

                    // Rangée principale : [mode 30][label][➔][next 70][⊘].
                    string modeLabel = !chal.HasChallenge ? "–" : (chal.IsOpposed ? "VS" : "SD");
                    Color prevBg = GUI.backgroundColor;
                    GUI.backgroundColor = !chal.HasChallenge ? new Color(0.35f, 0.38f, 0.42f)
                        : (chal.IsOpposed ? new Color(1.0f, 0.55f, 0.2f) : new Color(1.0f, 0.78f, 0.25f));
                    if (GraphButton(new Rect(innerX, y, 30f, 20f), modeLabel))
                    {
                        if (!chal.HasChallenge) { chal.HasChallenge = true; chal.IsOpposed = false; if (chal.TargetDC <= 0) chal.TargetDC = 10; }
                        else if (!chal.IsOpposed) { chal.IsOpposed = true; }
                        else { chal.HasChallenge = false; chal.IsOpposed = false; }
                    }
                    GUI.backgroundColor = prevBg;

                    float labelW = Mathf.Max(40f, innerW - 32f - 122f);
                    ch.Label = GraphTextField(new Rect(innerX + 32f, y, labelW, 20f), ch.Label ?? "");
                    GraphLabel(new Rect(innerX + 32f + labelW + 2f, y, 16f, 20f), "➔");
                    ch.NextLineId = GraphTextField(new Rect(innerX + 32f + labelW + 20f, y, 70f, 20f), ch.NextLineId ?? "");
                    if (GraphButton(new Rect(innerX + 32f + labelW + 92f, y, 20f, 20f), "⊘")) ch.NextLineId = "";
                    y += 22f;

                    if (chal.HasChallenge && !chal.IsOpposed)
                    {
                        // Sous-rangée test SD : [<][compétence][>][SD][val][Acteur].
                        float sx = innerX + 12f;
                        if (GraphButton(new Rect(sx, y, 18f, 18f), "◀"))
                        {
                            int si = ((int)chal.RequiredSkill - 1 + skillCount) % skillCount;
                            chal.RequiredSkill = (SkillType)si;
                        }
                        sx += 20f;
                        GraphLabel(new Rect(sx, y, 95f, 18f), $"<b>{SkillDefinitions.GetDisplayName(chal.RequiredSkill)}</b>");
                        sx += 97f;
                        if (GraphButton(new Rect(sx, y, 18f, 18f), "▶"))
                        {
                            int si = ((int)chal.RequiredSkill + 1) % skillCount;
                            chal.RequiredSkill = (SkillType)si;
                        }
                        sx += 20f;
                        GraphLabel(new Rect(sx, y, 24f, 18f), "SD:");
                        sx += 26f;
                        string sdTxt = GraphTextField(new Rect(sx, y, 30f, 18f), chal.TargetDC.ToString());
                        int.TryParse(sdTxt, out chal.TargetDC);
                        sx += 32f;
                        GraphLabel(new Rect(sx, y, 28f, 18f), "Act:");
                        sx += 30f;
                        chal.SpecificActorId = GraphTextField(new Rect(sx, y, Mathf.Max(30f, innerX + innerW - sx), 18f), chal.SpecificActorId ?? "");
                        y += 20f;
                    }
                    else if (chal.HasChallenge && chal.IsOpposed)
                    {
                        // Sous-rangée 1 : compétence joueur vs compétence adverse.
                        float sx = innerX + 12f;
                        GraphLabel(new Rect(sx, y, 18f, 18f), "J:");
                        sx += 20f;
                        if (GraphButton(new Rect(sx, y, 18f, 18f), "◀"))
                        {
                            int si = ((int)chal.RequiredSkill - 1 + skillCount) % skillCount;
                            chal.RequiredSkill = (SkillType)si;
                        }
                        sx += 20f;
                        GraphLabel(new Rect(sx, y, 72f, 18f), $"<b>{SkillDefinitions.GetDisplayName(chal.RequiredSkill)}</b>");
                        sx += 74f;
                        if (GraphButton(new Rect(sx, y, 18f, 18f), "▶"))
                        {
                            int si = ((int)chal.RequiredSkill + 1) % skillCount;
                            chal.RequiredSkill = (SkillType)si;
                        }
                        sx += 20f;
                        GraphLabel(new Rect(sx, y, 22f, 18f), "vs");
                        sx += 24f;
                        if (GraphButton(new Rect(sx, y, 18f, 18f), "◀"))
                        {
                            int oi = ((int)chal.OpposedSkill - 1 + skillCount) % skillCount;
                            chal.OpposedSkill = (SkillType)oi;
                        }
                        sx += 20f;
                        GraphLabel(new Rect(sx, y, 72f, 18f), $"<b>{SkillDefinitions.GetDisplayName(chal.OpposedSkill)}</b>");
                        sx += 74f;
                        if (GraphButton(new Rect(sx, y, 18f, 18f), "▶"))
                        {
                            int oi = ((int)chal.OpposedSkill + 1) % skillCount;
                            chal.OpposedSkill = (SkillType)oi;
                        }
                        y += 20f;

                        // Sous-rangée 2 : opposant + bonus + SD de repli (total fixe si opposant introuvable).
                        float qx = innerX + 12f;
                        GraphLabel(new Rect(qx, y, 32f, 18f), "Adv:");
                        qx += 34f;
                        chal.OpposedActorId = GraphTextField(new Rect(qx, y, 80f, 18f), chal.OpposedActorId ?? "");
                        qx += 82f;
                        GraphLabel(new Rect(qx, y, 26f, 18f), "+B:");
                        qx += 28f;
                        string bTxt = GraphTextField(new Rect(qx, y, 30f, 18f), chal.OpposedBonus.ToString());
                        int.TryParse(bTxt, out chal.OpposedBonus);
                        qx += 32f;
                        GraphLabel(new Rect(qx, y, 44f, 18f), "SD fix:");
                        qx += 46f;
                        string fixTxt = GraphTextField(new Rect(qx, y, 30f, 18f), chal.TargetDC.ToString());
                        int.TryParse(fixTxt, out chal.TargetDC);
                        y += 20f;
                    }

                    if (chal.HasChallenge)
                    {
                        chal.SuccessEffects ??= new System.Collections.Generic.List<ScenarioEffect>();
                        chal.FailureEffects ??= new System.Collections.Generic.List<ScenarioEffect>();

                        // Sortie succès : vers carte Conséquence (prioritaire) + lien direct vers réplique.
                        {
                            float rx = innerX + 12f;
                            GUI.color = new Color(0.35f, 1f, 0.55f);
                            GraphLabel(new Rect(rx, y, 18f, 18f), "✔");
                            GUI.color = Color.white;
                            rx += 20f;
                            GraphLabel(new Rect(rx, y, 52f, 18f), "→Cons:");
                            rx += 54f;
                            chal.SuccessConsequenceId = GraphTextField(new Rect(rx, y, 84f, 18f), chal.SuccessConsequenceId ?? "");
                            rx += 86f;
                            GraphLabel(new Rect(rx, y, 14f, 18f), "→");
                            rx += 16f;
                            chal.SuccessNextLineId = GraphTextField(new Rect(rx, y, Mathf.Max(30f, innerX + innerW - rx), 18f), chal.SuccessNextLineId ?? "");
                            y += 20f;
                        }

                        // Sortie échec : vers carte Conséquence (prioritaire) + lien direct vers réplique.
                        {
                            float fx2 = innerX + 12f;
                            GUI.color = new Color(1f, 0.45f, 0.4f);
                            GraphLabel(new Rect(fx2, y, 18f, 18f), "✕");
                            GUI.color = Color.white;
                            fx2 += 20f;
                            GraphLabel(new Rect(fx2, y, 52f, 18f), "→Cons:");
                            fx2 += 54f;
                            chal.FailureConsequenceId = GraphTextField(new Rect(fx2, y, 84f, 18f), chal.FailureConsequenceId ?? "");
                            fx2 += 86f;
                            GraphLabel(new Rect(fx2, y, 14f, 18f), "→");
                            fx2 += 16f;
                            chal.FailureNextLineId = GraphTextField(new Rect(fx2, y, Mathf.Max(30f, innerX + innerW - fx2), 18f), chal.FailureNextLineId ?? "");
                            y += 20f;
                        }

                    }

                    // Méta du choix : prérequis et effets (résumé visible, détail inspecteur).
                    {
                        bool hasPre = ch.Prerequisite != null && ch.Prerequisite.HasPrerequisite;
                        int fxCount = ch.Effects != null ? ch.Effects.Count : 0;
                        int sfx = (chal.HasChallenge && chal.SuccessEffects != null) ? chal.SuccessEffects.Count : 0;
                        int ffx = (chal.HasChallenge && chal.FailureEffects != null) ? chal.FailureEffects.Count : 0;
                        if (hasPre || fxCount > 0 || sfx + ffx > 0)
                        {
                            string meta = "";
                            if (hasPre) meta += $"🔒 {ch.Prerequisite.GetSummary()}  ";
                            if (fxCount > 0) meta += $" +fx:{fxCount}";
                            if (sfx + ffx > 0) meta += $" 🎲fx:{sfx}/{ffx}";
                            GUI.color = new Color(1f, 1f, 1f, 0.55f);
                            GraphLabel(new Rect(innerX + 12f, y, Mathf.Max(40f, innerW - 12f), 18f), meta);
                            GUI.color = Color.white;
                            y += 20f;
                        }
                    }
                }
            }
            else
            {
                GraphLabel(new Rect(innerX, y, 65f, 20f), "Liaison ➔ :");
                line.NextLineId = GraphTextField(new Rect(innerX + 67f, y, Mathf.Max(60f, innerW - 67f - 26f), 20f), line.NextLineId ?? "");
                if (GraphButton(new Rect(innerX + innerW - 22f, y, 22f, 20f), "⊘")) line.NextLineId = "";
                y += 22f;
            }

            // Résumés de la réplique (rien de caché : prérequis / test auto / ambiance).
            if (line.Prerequisite != null && line.Prerequisite.HasPrerequisite)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.6f);
                GraphLabel(new Rect(innerX + 12f, y, Mathf.Max(40f, innerW - 12f), 18f), $"🔒 Prérequis : {line.Prerequisite.GetSummary()}");
                GUI.color = Color.white;
                y += 18f;
            }
            if (line.AutoSkillCheck != null && line.AutoSkillCheck.HasAutoCheck)
            {
                var auto = line.AutoSkillCheck;
                GUI.color = new Color(0.7f, 0.85f, 1f, 0.65f);
                GraphLabel(new Rect(innerX + 12f, y, Mathf.Max(40f, innerW - 12f), 18f),
                    $"🎲 Auto : {SkillDefinitions.GetDisplayName(auto.RequiredSkill)} SD {auto.TargetDC} ✔→{auto.SuccessNextLineId} ✕→{auto.FailureNextLineId}");
                GUI.color = Color.white;
                y += 18f;
            }
            if (line.Ambience != null && line.Ambience.HasAmbience)
            {
                var amb = line.Ambience;
                GUI.color = new Color(1f, 1f, 1f, 0.6f);
                GraphLabel(new Rect(innerX + 12f, y, Mathf.Max(40f, innerW - 12f), 18f),
                    $"🔊 Ambiance : {amb.SoundCueId}{(amb.ChangeMusic ? $" ♪{amb.MusicMood}/{amb.MusicIntensity}" : "")}{(amb.TriggerAlarm ? " 🚨" : "")}");
                GUI.color = Color.white;
                y += 18f;
            }

            // Rangée basse : +Choix / toggles (protégée du débordement par CardHeight auto).
            if (y + 20f <= cardRect.yMax - 4f)
            {
                if (GraphButton(new Rect(innerX, y, 64f, 18f), "+ Choix"))
                {
                    line.Choices.Add(new SceneDialogueChoiceData { ChoiceId = $"ch_{line.Choices.Count + 1}", Label = "Option..." });
                }
                line.Ambience.HasAmbience = GraphToggle(new Rect(innerX + 68f, y, 90f, 18f), line.Ambience.HasAmbience, "Ambiance");
                line.Prerequisite.HasPrerequisite = GraphToggle(new Rect(innerX + 160f, y, Mathf.Max(60f, innerW - 160f), 18f), line.Prerequisite.HasPrerequisite, "Prérequis");
            }
            else if (GraphButton(new Rect(innerX, cardRect.yMax - 22f, 64f, 18f), "+ Choix"))
            {
                line.Choices.Add(new SceneDialogueChoiceData { ChoiceId = $"ch_{line.Choices.Count + 1}", Label = "Option..." });
            }

            // Port de Sortie Principal : rouge + gros si point de sortie (sans liaison).
            bool dlgIsExit = !DialogueHasOutgoing(line);
            Rect outPort = dlgIsExit
                ? new Rect(cardRect.xMax - 10f, cardRect.y + 16f, 20f, 20f)
                : new Rect(cardRect.xMax - 8f, cardRect.y + 18f, 16f, 16f);
            DrawGraphSolidRect(outPort, dlgIsExit ? ExitRed : new Color(0f, 0.85f, 1f));

            if (GraphPrimaryDown(mouseWorld, outPort))
            {
                _wireDraft = new WireConnectionDraft
                {
                    IsActive = true,
                    FromChoice = false,
                    SourceLine = line,
                    StartPos = outPort.center
                };
                evt.Use();
            }

            // Ports de Sortie des Choix (alignés sur les rangées GUI : offsets réels avec défis).
            GetDialogueChoiceRowOffsets(line, cardChoiceOffsets);
            for (int c = 0; c < line.Choices.Count; c++)
            {
                var ch = line.Choices[c];
                float rowTop = (c >= 0 && c < cardChoiceOffsets.Count) ? cardChoiceOffsets[c] : (100f + c * 22f);
                Rect chOutPort = new Rect(cardRect.xMax - 7f, cardRect.y + rowTop + 3f, 14f, 14f);
                var chal = ch != null ? ch.Challenge : null;
                bool isChal = chal != null && chal.HasChallenge;
                bool isVs = isChal && chal.IsOpposed;
                DrawGraphSolidRect(chOutPort, isVs ? new Color(1f, 0.55f, 0.2f) : (isChal ? new Color(1f, 0.85f, 0.3f) : new Color(1f, 0.8f, 0.2f)));

                if (ch != null && GraphPrimaryDown(mouseWorld, chOutPort))
                {
                    _wireDraft = new WireConnectionDraft
                    {
                        IsActive = true,
                        FromChoice = true,
                        SourceLine = line,
                        SourceChoice = ch,
                        StartPos = chOutPort.center
                    };
                    evt.Use();
                }

                // Sorties du défi : ✔ (succès → conséquence) et ✕ (échec → conséquence).
                if (isChal)
                {
                    float linkTop = rowTop + 22f + GetChallengeConfigRows(chal) * 20f;
                    Rect okPort = new Rect(cardRect.xMax - 7f, cardRect.y + linkTop + 3f, 14f, 14f);
                    DrawGraphSolidRect(okPort, new Color(0.35f, 1f, 0.55f));
                    if (ch != null && GraphPrimaryDown(mouseWorld, okPort))
                    {
                        _wireDraft = new WireConnectionDraft
                        {
                            IsActive = true,
                            FromChallengeLink = true,
                            ChallengeLinkIsSuccess = true,
                            SourceChallenge = chal,
                            StartPos = okPort.center
                        };
                        evt.Use();
                    }
                    Rect koPort = new Rect(cardRect.xMax - 7f, cardRect.y + linkTop + 23f, 14f, 14f);
                    DrawGraphSolidRect(koPort, new Color(1f, 0.4f, 0.35f));
                    if (ch != null && GraphPrimaryDown(mouseWorld, koPort))
                    {
                        _wireDraft = new WireConnectionDraft
                        {
                            IsActive = true,
                            FromChallengeLink = true,
                            ChallengeLinkIsSuccess = false,
                            SourceChallenge = chal,
                            StartPos = koPort.center
                        };
                        evt.Use();
                    }
                }
            }
        }

        private void DrawEventCard(SceneEventData ev, int index, SceneNodeData node, Vector2 mouseWorld)
        {
            if (string.IsNullOrEmpty(ev.EventId)) ev.EventId = $"evt_{index + 1}";

            Vector2 size = GetEventCardSize(ev);
            float cardW = size.x;
            float cardH = size.y;

            Rect cardRect = new Rect(ev.GraphPosX, ev.GraphPosY, cardW, cardH);
            Rect headerRect = new Rect(cardRect.x, cardRect.y, cardW, 26f);
            Rect closeRect = new Rect(headerRect.xMax - 22f, headerRect.y + 3f, 18f, 18f);
            Rect dragHeaderRect = new Rect(headerRect.x, headerRect.y, headerRect.width - 26f, headerRect.height);
            Rect resizeGripRect = new Rect(cardRect.xMax - 14f, cardRect.yMax - 14f, 14f, 14f);

            Event evt = Event.current;
            string dragKey = $"event_{index}_{ev.EventId}";
            string resizeKey = $"resize_{dragKey}";
            int dragControlId = GUIUtility.GetControlID(FocusType.Passive);
            int resizeControlId = GUIUtility.GetControlID(FocusType.Passive);

            // Redimensionnement de la carte d'événement
            if (GraphPrimaryDown(mouseWorld, resizeGripRect))
            {
                GUIUtility.hotControl = resizeControlId;
                _resizingCardKey = resizeKey;
                evt.Use();
            }
            else if ((evt.type == EventType.MouseDrag || evt.type == EventType.MouseMove) && GUIUtility.hotControl == resizeControlId && _resizingCardKey == resizeKey)
            {
                ev.CardWidth = Mathf.Clamp(mouseWorld.x - cardRect.x, 280f, 750f);
                ev.CardHeight = Mathf.Clamp(mouseWorld.y - cardRect.y, 120f, 500f);
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == resizeControlId)
            {
                GUIUtility.hotControl = 0;
                _resizingCardKey = null;
                evt.Use();
            }

            // Déplacement de la carte d'événement
            if (GraphPrimaryDown(mouseWorld, dragHeaderRect))
            {
                GUIUtility.hotControl = dragControlId;
                _draggingCardKey = dragKey;
                _dragOffset = mouseWorld - new Vector2(ev.GraphPosX, ev.GraphPosY);
                evt.Use();
            }
            else if ((evt.type == EventType.MouseDrag || evt.type == EventType.MouseMove) && GUIUtility.hotControl == dragControlId && _draggingCardKey == dragKey)
            {
                ev.GraphPosX = mouseWorld.x - _dragOffset.x;
                ev.GraphPosY = mouseWorld.y - _dragOffset.y;
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == dragControlId)
            {
                GUIUtility.hotControl = 0;
                _draggingCardKey = null;
                evt.Use();
            }

            // 1. Rendu d'arrière-plan de la Carte et de l'En-tête
            DrawGraphSolidRect(cardRect, new Color(0.12f, 0.08f, 0.12f, 0.95f));
            DrawGraphSolidRect(headerRect, new Color(0.40f, 0.15f, 0.15f));

            // Bordures
            DrawGraphSolidRect(new Rect(cardRect.x, cardRect.y, cardRect.width, 1f), new Color(0.85f, 0.3f, 0.3f, 0.8f));
            DrawGraphSolidRect(new Rect(cardRect.x, cardRect.yMax - 1f, cardRect.width, 1f), new Color(1f, 1f, 1f, 0.08f));
            DrawGraphSolidRect(new Rect(cardRect.x, cardRect.y, 1f, cardRect.height), new Color(1f, 1f, 1f, 0.08f));
            DrawGraphSolidRect(new Rect(cardRect.xMax - 1f, cardRect.y, 1f, cardRect.height), new Color(1f, 1f, 1f, 0.08f));

            // 2. Port d'Entrée : vert + gros si point d'entrée (sans lien entrant).
            DrawEntryInputPort(cardRect, new Color(0.85f, 0.4f, 1f),
                !EventIdHasIncoming(node, ev.EventId), false);

            // 3. Titre & Nature de l'Événement
            GraphLabel(new Rect(headerRect.x + 8f, headerRect.y + 3f, cardW - 35f, 20f), $"<b>▲ {ev.Title}</b> [{ev.Kind}]");

            // 4. Bouton Supprimer
            GUI.backgroundColor = new Color(0.85f, 0.22f, 0.22f, 1f);
            GUI.color = Color.white;
            if (GraphButton(closeRect, "✕"))
            {
                node.Events.RemoveAt(index);
                if (_draggingCardKey == dragKey)
                {
                    _draggingCardKey = null;
                    GUIUtility.hotControl = 0;
                }
                GUI.backgroundColor = Color.white;
                evt.Use();
                return;
            }
            GUI.backgroundColor = Color.white;

            // 5. Poignée de redimensionnement
            GUI.color = new Color(1f, 1f, 1f, 0.4f);
            GraphLabel(resizeGripRect, "◢");
            GUI.color = Color.white;

            // Survol du header : infobulle lisible (cf. cartes dialogue).
            if (_zoom < 0.85f && !_isPanning && _draggingCardKey == null && _resizingCardKey == null && !_wireDraft.IsActive
                && headerRect.Contains(mouseWorld))
            {
                _hasHoverCard = true;
                _hoverTitle = $"▲ {ev.Title}  ({ev.EventId}) [{ev.Kind}]";
                _hoverBody = string.IsNullOrEmpty(ev.Description) ? "(aucune description)" : ev.Description;
                _hoverScreenPos = _graphMouseScreenPos;
            }

            // Contenu éditable en coordonnées monde, converti en coordonnées écran par GraphRect().
            float innerX = cardRect.x + 8f;
            float innerW = cardW - 16f;
            float y = cardRect.y + 30f;

            GraphLabel(new Rect(innerX, y, 22f, 18f), "ID:");
            ev.EventId = GraphTextField(new Rect(innerX + 24f, y, 70f, 18f), ev.EventId ?? "");
            GraphLabel(new Rect(innerX + 98f, y, 35f, 18f), "Titre:");
            ev.Title = GraphTextField(new Rect(innerX + 135f, y, Mathf.Max(40f, innerW - 135f), 18f), ev.Title ?? "");
            y += 20f;

            GraphLabel(new Rect(innerX, y, 45f, 18f), "Action:");
            ev.Kind = (SceneEventKind)GraphToolbar(new Rect(innerX + 47f, y, Mathf.Max(80f, innerW - 47f), 18f), (int)ev.Kind, new[] { "Normal", "Combat", "Spawn", "Cam" });
            y += 22f;

            Color prevBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(1f, 0.7f, 0.2f);
            if (GraphButton(new Rect(innerX, y, innerW, 20f), "▲ Tester en Direct"))
            {
                var controller = FindAnyObjectByType<Scenes.JsonStorySceneController>();
                controller?.ExecuteScenicEvent(ev);
            }
            GUI.backgroundColor = prevBg;

            bool evtIsExit = !EventHasOutgoing(ev);
            Rect outPort = evtIsExit
                ? new Rect(cardRect.xMax - 10f, cardRect.y + 16f, 20f, 20f)
                : new Rect(cardRect.xMax - 7f, cardRect.y + 19f, 14f, 14f);
            DrawGraphSolidRect(outPort, evtIsExit ? ExitRed : new Color(0.85f, 0.4f, 1f));

            if (GraphPrimaryDown(mouseWorld, outPort))
            {
                _wireDraft = new WireConnectionDraft
                {
                    IsActive = true,
                    IsEvent = true,
                    SourceEvent = ev,
                    StartPos = outPort.center
                };
                evt.Use();
            }
        }

        private void DrawConsequenceCard(SceneConsequenceData cons, int index, SceneNodeData node, Vector2 mouseWorld)
        {
            if (string.IsNullOrEmpty(cons.ConsequenceId)) cons.ConsequenceId = $"cons_{index + 1}";
            cons.Rewards ??= new SceneDialogueRewardData();
            StorySceneData.EnsureRewardDefaults(cons.Rewards);
            cons.Effects ??= new System.Collections.Generic.List<ScenarioEffect>();
            var rew = cons.Rewards;

            Vector2 size = GetConsequenceCardSize(cons);
            float cardW = size.x;
            float cardH = size.y;

            Rect cardRect = new Rect(cons.GraphPosX, cons.GraphPosY, cardW, cardH);
            Rect headerRect = new Rect(cardRect.x, cardRect.y, cardW, 26f);
            Rect closeRect = new Rect(headerRect.xMax - 22f, headerRect.y + 3f, 18f, 18f);
            Rect dragHeaderRect = new Rect(headerRect.x, headerRect.y, headerRect.width - 26f, headerRect.height);
            Rect resizeGripRect = new Rect(cardRect.xMax - 14f, cardRect.yMax - 14f, 14f, 14f);

            Event evt = Event.current;
            string dragKey = $"cons_{index}_{cons.ConsequenceId}";
            string resizeKey = $"resize_{dragKey}";
            int dragControlId = GUIUtility.GetControlID(FocusType.Passive);
            int resizeControlId = GUIUtility.GetControlID(FocusType.Passive);

            if (GraphPrimaryDown(mouseWorld, resizeGripRect))
            {
                GUIUtility.hotControl = resizeControlId;
                _resizingCardKey = resizeKey;
                evt.Use();
            }
            else if ((evt.type == EventType.MouseDrag || evt.type == EventType.MouseMove) && GUIUtility.hotControl == resizeControlId && _resizingCardKey == resizeKey)
            {
                cons.CardWidth = Mathf.Clamp(mouseWorld.x - cardRect.x, 320f, 750f);
                cons.CardHeight = Mathf.Clamp(mouseWorld.y - cardRect.y, 160f, 650f);
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == resizeControlId)
            {
                GUIUtility.hotControl = 0;
                _resizingCardKey = null;
                evt.Use();
            }

            if (GraphPrimaryDown(mouseWorld, dragHeaderRect))
            {
                GUIUtility.hotControl = dragControlId;
                _draggingCardKey = dragKey;
                _dragOffset = mouseWorld - new Vector2(cons.GraphPosX, cons.GraphPosY);
                evt.Use();
            }
            else if ((evt.type == EventType.MouseDrag || evt.type == EventType.MouseMove) && GUIUtility.hotControl == dragControlId && _draggingCardKey == dragKey)
            {
                cons.GraphPosX = mouseWorld.x - _dragOffset.x;
                cons.GraphPosY = mouseWorld.y - _dragOffset.y;
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == dragControlId)
            {
                GUIUtility.hotControl = 0;
                _draggingCardKey = null;
                evt.Use();
            }

            Color headerCol = new Color(0.30f, 0.42f, 0.12f);
            DrawGraphSolidRect(cardRect, new Color(0.10f, 0.13f, 0.08f, 0.95f));
            DrawGraphSolidRect(headerRect, headerCol);
            DrawGraphSolidRect(new Rect(cardRect.x, cardRect.y, cardRect.width, 1f), headerCol * 1.4f);
            DrawGraphSolidRect(new Rect(cardRect.x, cardRect.yMax - 1f, cardRect.width, 1f), new Color(1f, 1f, 1f, 0.08f));
            DrawGraphSolidRect(new Rect(cardRect.x, cardRect.y, 1f, cardRect.height), new Color(1f, 1f, 1f, 0.08f));
            DrawGraphSolidRect(new Rect(cardRect.xMax - 1f, cardRect.y, 1f, cardRect.height), new Color(1f, 1f, 1f, 0.08f));

            DrawEntryInputPort(cardRect, new Color(0.35f, 1f, 0.55f),
                !ConsIdHasIncoming(node, cons.ConsequenceId), false);

            GraphLabel(new Rect(headerRect.x + 8f, headerRect.y + 3f, cardW - 35f, 20f), $"<b>🎁 {cons.Title}</b> (<i>{cons.ConsequenceId}</i>)");

            GUI.backgroundColor = new Color(0.85f, 0.22f, 0.22f, 1f);
            GUI.color = Color.white;
            if (GraphButton(closeRect, "✕"))
            {
                node.Consequences.RemoveAt(index);
                if (_draggingCardKey == dragKey)
                {
                    _draggingCardKey = null;
                    GUIUtility.hotControl = 0;
                }
                GUI.backgroundColor = Color.white;
                evt.Use();
                return;
            }
            GUI.backgroundColor = Color.white;

            GUI.color = new Color(1f, 1f, 1f, 0.4f);
            GraphLabel(resizeGripRect, "◢");
            GUI.color = Color.white;

            if (_zoom < 0.85f && !_isPanning && _draggingCardKey == null && _resizingCardKey == null && !_wireDraft.IsActive
                && !_pickerCapturesMouse && headerRect.Contains(mouseWorld))
            {
                _hasHoverCard = true;
                _hoverTitle = $"🎁 {cons.Title}  ({cons.ConsequenceId})";
                var hb = new System.Text.StringBuilder();
                if (cons.HasDialogue && !string.IsNullOrEmpty(cons.Speech)) hb.AppendLine(cons.Speech);
                hb.AppendLine(cons.GetSummary());
                _hoverBody = hb.ToString().Trim();
                _hoverScreenPos = _graphMouseScreenPos;
            }

            float innerX = cardRect.x + 8f;
            float innerW = cardW - 16f;
            float y = cardRect.y + 30f;

            GraphLabel(new Rect(innerX, y, 22f, 18f), "ID:");
            cons.ConsequenceId = GraphTextField(new Rect(innerX + 24f, y, 80f, 18f), cons.ConsequenceId ?? "");
            GraphLabel(new Rect(innerX + 108f, y, 38f, 18f), "Titre:");
            cons.Title = GraphTextField(new Rect(innerX + 148f, y, Mathf.Max(40f, innerW - 148f), 18f), cons.Title ?? "");
            y += 22f;

            cons.HasDialogue = GraphToggle(new Rect(innerX, y, 110f, 18f), cons.HasDialogue, "💬 Dialogue");
            cons.TriggersCombat = GraphToggle(new Rect(innerX + 114f, y, 100f, 18f), cons.TriggersCombat, "⚔ Combat");
            GUI.color = new Color(1f, 1f, 1f, 0.55f);
            GraphLabel(new Rect(innerX + 218f, y, Mathf.Max(40f, innerW - 218f), 18f), cons.Effects.Count > 0 ? $"+fx:{cons.Effects.Count}" : "");
            GUI.color = Color.white;
            y += 20f;

            if (cons.HasDialogue)
            {
                GraphLabel(new Rect(innerX, y, 30f, 18f), "Loc:");
                cons.SpeakerId = GraphTextField(new Rect(innerX + 32f, y, 64f, 18f), cons.SpeakerId ?? "");
                {
                    Rect dropBtn = new Rect(innerX + 98f, y, 20f, 18f);
                    string dropKey = $"cons_{cons.ConsequenceId}";
                    if (GraphButton(dropBtn, "▼"))
                    {
                        if (_actorDropKey == dropKey) _actorDropKey = null;
                        else OpenActorDropdown(dropKey, dropBtn, cons.SpeakerId ?? "", v => cons.SpeakerId = v);
                    }
                }
                cons.StageDirection = GraphTextField(new Rect(innerX + 122f, y, Mathf.Max(40f, innerW - 122f), 18f), cons.StageDirection ?? "");
                y += 22f;
                cons.Speech = GraphTextArea(new Rect(innerX, y, innerW, 42f), cons.Speech ?? "");
                y += 44f;
            }

            float nextRowTop = y - cardRect.y;
            GraphLabel(new Rect(innerX, y, 52f, 18f), "→Diag:");
            cons.NextLineId = GraphTextField(new Rect(innerX + 54f, y, 84f, 18f), cons.NextLineId ?? "");
            GraphLabel(new Rect(innerX + 142f, y, 52f, 18f), "→Cons:");
            cons.NextConsequenceId = GraphTextField(new Rect(innerX + 196f, y, Mathf.Max(40f, innerW - 196f), 18f), cons.NextConsequenceId ?? "");
            y += 22f;

            GraphLabel(new Rect(innerX, y, 30f, 18f), "+XP");
            string cxp = GraphTextField(new Rect(innerX + 32f, y, 34f, 18f), rew.EarnXP.ToString());
            int.TryParse(cxp, out rew.EarnXP);
            GraphLabel(new Rect(innerX + 70f, y, 30f, 18f), "+CE");
            string cce = GraphTextField(new Rect(innerX + 102f, y, 34f, 18f), rew.EarnCredits.ToString());
            int.TryParse(cce, out rew.EarnCredits);
            GraphLabel(new Rect(innerX + 140f, y, 30f, 18f), "Obj:");
            rew.CompletionObjectiveId = GraphTextField(new Rect(innerX + 172f, y, Mathf.Max(40f, innerW - 172f), 18f), rew.CompletionObjectiveId ?? "");
            y += 22f;

            // Ligne item principal.
            {
                float ix = innerX + 12f;
                GraphLabel(new Rect(ix, y, 20f, 18f), "🎁");
                ix += 22f;
                float tailW = 14f + 2f + 30f + 2f + 22f + 2f + 22f;
                rew.ItemRewardName = GraphTextField(new Rect(ix, y, Mathf.Max(40f, innerX + innerW - ix - tailW), 18f), rew.ItemRewardName ?? "");
                ix = innerX + innerW - tailW;
                GraphLabel(new Rect(ix, y, 14f, 18f), "x");
                ix += 16f;
                string cq = GraphTextField(new Rect(ix, y, 30f, 18f), rew.ItemRewardQuantity.ToString());
                int.TryParse(cq, out rew.ItemRewardQuantity);
                if (rew.ItemRewardQuantity < 1) rew.ItemRewardQuantity = 1;
                ix += 32f;
                Color prevPkM = GUI.backgroundColor;
                if (IsPickerOpenForCons(cons, -1)) GUI.backgroundColor = new Color(0.35f, 1f, 0.55f);
                if (GraphButton(new Rect(ix, y, 22f, 18f), "🔍"))
                {
                    if (IsPickerOpenForCons(cons, -1)) CloseItemPicker();
                    else OpenItemPicker(cons, -1);
                }
                GUI.backgroundColor = prevPkM;
                ix += 24f;
                if (GraphButton(new Rect(ix, y, 22f, 18f), "+"))
                {
                    rew.AdditionalItems.Add(new SceneItemRewardEntry { ItemName = "", Quantity = 1 });
                }
                y += 20f;
            }

            // Lignes items supplémentaires.
            for (int ri = 0; ri < rew.AdditionalItems.Count; ri++)
            {
                var entry = rew.AdditionalItems[ri];
                if (entry == null) { rew.AdditionalItems.RemoveAt(ri); break; }
                float ix = innerX + 12f;
                GraphLabel(new Rect(ix, y, 20f, 18f), "🎁");
                ix += 22f;
                float tailW = 14f + 2f + 30f + 2f + 22f + 2f + 22f;
                entry.ItemName = GraphTextField(new Rect(ix, y, Mathf.Max(40f, innerX + innerW - ix - tailW), 18f), entry.ItemName ?? "");
                ix = innerX + innerW - tailW;
                GraphLabel(new Rect(ix, y, 14f, 18f), "x");
                ix += 16f;
                string eq2 = GraphTextField(new Rect(ix, y, 30f, 18f), entry.Quantity.ToString());
                int.TryParse(eq2, out entry.Quantity);
                if (entry.Quantity < 1) entry.Quantity = 1;
                ix += 32f;
                Color prevPkC = GUI.backgroundColor;
                if (IsPickerOpenForCons(cons, ri)) GUI.backgroundColor = new Color(0.35f, 1f, 0.55f);
                if (GraphButton(new Rect(ix, y, 22f, 18f), "🔍"))
                {
                    if (IsPickerOpenForCons(cons, ri)) CloseItemPicker();
                    else OpenItemPicker(cons, ri);
                }
                GUI.backgroundColor = prevPkC;
                ix += 24f;
                GUI.backgroundColor = new Color(0.85f, 0.25f, 0.25f);
                if (GraphButton(new Rect(ix, y, 22f, 18f), "✕"))
                {
                    rew.AdditionalItems.RemoveAt(ri);
                    GUI.backgroundColor = Color.white;
                    break;
                }
                GUI.backgroundColor = Color.white;
                y += 20f;
            }

            // Ports de sortie : → réplique (cyan, rouge + gros si sortie) et → conséquence (vert).
            bool consIsExit = !ConsHasOutgoing(cons);
            Rect outLinePort = consIsExit
                ? new Rect(cardRect.xMax - 10f, cardRect.y + 16f, 20f, 20f)
                : new Rect(cardRect.xMax - 8f, cardRect.y + 18f, 16f, 16f);
            DrawGraphSolidRect(outLinePort, consIsExit ? ExitRed : new Color(0f, 0.85f, 1f));
            if (GraphPrimaryDown(mouseWorld, outLinePort))
            {
                _wireDraft = new WireConnectionDraft
                {
                    IsActive = true,
                    FromConsequence = true,
                    ConsLinkToCons = false,
                    SourceConsequence = cons,
                    StartPos = outLinePort.center
                };
                evt.Use();
            }

            Rect outConsPort = new Rect(cardRect.xMax - 7f, cardRect.y + nextRowTop + 3f, 14f, 14f);
            DrawGraphSolidRect(outConsPort, new Color(0.4f, 1.0f, 0.5f));
            if (GraphPrimaryDown(mouseWorld, outConsPort))
            {
                _wireDraft = new WireConnectionDraft
                {
                    IsActive = true,
                    FromConsequence = true,
                    ConsLinkToCons = true,
                    SourceConsequence = cons,
                    StartPos = outConsPort.center
                };
                evt.Use();
            }
        }

        private void DrawTriggerCard(SceneTriggerData trg, int index, Vector2 mouseWorld)
        {
            if (trg == null) return;
            if (string.IsNullOrEmpty(trg.TriggerId)) trg.TriggerId = $"trig_{index + 1}";
            string targetDisplay = string.IsNullOrEmpty(trg.TargetNodeId) ? "(sans cible)" : trg.TargetNodeId;

            Vector2 size = GetTriggerCardSize(trg);
            float cardW = size.x;
            float cardH = size.y;

            Rect cardRect = new Rect(trg.GraphPosX, trg.GraphPosY, cardW, cardH);
            Rect headerRect = new Rect(cardRect.x, cardRect.y, cardW, 26f);
            Rect closeRect = new Rect(headerRect.xMax - 22f, headerRect.y + 3f, 18f, 18f);
            Rect dragHeaderRect = new Rect(headerRect.x, headerRect.y, headerRect.width - 26f, headerRect.height);
            Rect resizeGripRect = new Rect(cardRect.xMax - 14f, cardRect.yMax - 14f, 14f, 14f);

            Event evt = Event.current;
            string dragKey = $"trigger_{index}_{trg.TriggerId}";
            string resizeKey = $"resize_{dragKey}";
            int dragControlId = GUIUtility.GetControlID(FocusType.Passive);
            int resizeControlId = GUIUtility.GetControlID(FocusType.Passive);

            if (GraphPrimaryDown(mouseWorld, resizeGripRect))
            {
                GUIUtility.hotControl = resizeControlId;
                _resizingCardKey = resizeKey;
                evt.Use();
            }
            else if ((evt.type == EventType.MouseDrag || evt.type == EventType.MouseMove) && GUIUtility.hotControl == resizeControlId && _resizingCardKey == resizeKey)
            {
                trg.CardWidth = Mathf.Clamp(mouseWorld.x - cardRect.x, 300f, 750f);
                trg.CardHeight = Mathf.Clamp(mouseWorld.y - cardRect.y, 180f, 500f);
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == resizeControlId)
            {
                GUIUtility.hotControl = 0;
                _resizingCardKey = null;
                evt.Use();
            }

            if (GraphPrimaryDown(mouseWorld, dragHeaderRect))
            {
                GUIUtility.hotControl = dragControlId;
                _draggingCardKey = dragKey;
                _dragOffset = mouseWorld - new Vector2(trg.GraphPosX, trg.GraphPosY);
                evt.Use();
            }
            else if ((evt.type == EventType.MouseDrag || evt.type == EventType.MouseMove) && GUIUtility.hotControl == dragControlId && _draggingCardKey == dragKey)
            {
                trg.GraphPosX = mouseWorld.x - _dragOffset.x;
                trg.GraphPosY = mouseWorld.y - _dragOffset.y;
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == dragControlId)
            {
                GUIUtility.hotControl = 0;
                _draggingCardKey = null;
                evt.Use();
            }

            DrawGraphSolidRect(cardRect, new Color(0.13f, 0.10f, 0.05f, 0.95f));
            Color headerCol = new Color(0.45f, 0.32f, 0.08f);
            DrawGraphSolidRect(headerRect, headerCol);

            DrawGraphSolidRect(new Rect(cardRect.x, cardRect.y, cardRect.width, 1f), new Color(1f, 0.85f, 0.25f, 0.8f));
            DrawGraphSolidRect(new Rect(cardRect.x, cardRect.yMax - 1f, cardRect.width, 1f), new Color(1f, 1f, 1f, 0.08f));
            DrawGraphSolidRect(new Rect(cardRect.x, cardRect.y, 1f, cardRect.height), new Color(1f, 1f, 1f, 0.08f));
            DrawGraphSolidRect(new Rect(cardRect.xMax - 1f, cardRect.y, 1f, cardRect.height), new Color(1f, 1f, 1f, 0.08f));

            GraphLabel(new Rect(headerRect.x + 8f, headerRect.y + 3f, cardW - 35f, 20f), $"<b>▼ {trg.Label}</b> [{trg.TriggerId}]");

            GUI.backgroundColor = new Color(0.85f, 0.22f, 0.22f, 1f);
            GUI.color = Color.white;
            if (GraphButton(closeRect, "✕"))
            {
                if (_data.Triggers != null) _data.Triggers.RemoveAt(index);
                if (_draggingCardKey == dragKey)
                {
                    _draggingCardKey = null;
                    GUIUtility.hotControl = 0;
                }
                GUI.backgroundColor = Color.white;
                evt.Use();
                return;
            }
            GUI.backgroundColor = Color.white;

            GUI.color = new Color(1f, 1f, 1f, 0.4f);
            GraphLabel(resizeGripRect, "◢");
            GUI.color = Color.white;

            if (_zoom < 0.85f && !_isPanning && _draggingCardKey == null && _resizingCardKey == null && !_wireDraft.IsActive
                && headerRect.Contains(mouseWorld))
            {
                _hasHoverCard = true;
                _hoverTitle = $"▼ {trg.Label}  ({trg.TriggerId})";
                _hoverBody = $"{trg.GetSummary()}\n➔ {targetDisplay}";
                _hoverScreenPos = _graphMouseScreenPos;
            }

            float innerX = cardRect.x + 8f;
            float innerW = cardW - 16f;
            float y = cardRect.y + 30f;

            GraphLabel(new Rect(innerX, y, 24f, 18f), "ID:");
            trg.TriggerId = GraphTextField(new Rect(innerX + 26f, y, 100f, 18f), trg.TriggerId ?? "");
            GUI.color = new Color(1f, 0.85f, 0.25f);
            GraphLabel(new Rect(innerX + 130f, y, Mathf.Max(40f, innerW - 130f), 18f), $"➔ {targetDisplay}");
            GUI.color = Color.white;
            y += 20f;

            GraphLabel(new Rect(innerX, y, 42f, 18f), "Titre:");
            trg.Label = GraphTextField(new Rect(innerX + 44f, y, Mathf.Max(40f, innerW - 44f), 18f), trg.Label ?? "");
            y += 20f;

            trg.Kind = (SceneTriggerKind)GraphToolbar(new Rect(innerX, y, innerW, 18f), (int)trg.Kind, new[] { "Action", "Objectif", "Flag", "Acteur" });
            y += 22f;

            if (trg.Kind == SceneTriggerKind.InteractableActivated)
            {
                GraphLabel(new Rect(innerX, y, 95f, 18f), "Action carte :");
                trg.InteractableId = GraphTextField(new Rect(innerX + 97f, y, Mathf.Max(40f, innerW - 97f), 18f), trg.InteractableId ?? "");
                y += 20f;
            }
            else if (trg.Kind == SceneTriggerKind.ObjectiveCompleted)
            {
                GraphLabel(new Rect(innerX, y, 65f, 18f), "Objectif :");
                trg.ObjectiveId = GraphTextField(new Rect(innerX + 67f, y, Mathf.Max(40f, innerW - 67f), 18f), trg.ObjectiveId ?? "");
                y += 20f;
            }
            else if (trg.Kind == SceneTriggerKind.CampaignFlagSet)
            {
                GraphLabel(new Rect(innerX, y, 42f, 18f), "Flag :");
                trg.FlagKey = GraphTextField(new Rect(innerX + 44f, y, 150f, 18f), trg.FlagKey ?? "");
                trg.FlagMustBeSet = GraphToggle(new Rect(innerX + 198f, y, Mathf.Max(60f, innerW - 198f), 18f), trg.FlagMustBeSet, "Présent");
                y += 20f;
            }
            else
            {
                GraphLabel(new Rect(innerX, y, 58f, 18f), "Acteur :");
                trg.ActorId = GraphTextField(new Rect(innerX + 60f, y, 110f, 18f), trg.ActorId ?? "");
                trg.ActorCondition = (SceneActorConditionKind)GraphToolbar(new Rect(innerX + 174f, y, Mathf.Max(60f, innerW - 174f), 18f), (int)trg.ActorCondition, new[] { "Statut", "PV < %" });
                y += 22f;
                if (trg.ActorCondition == SceneActorConditionKind.HasStatus)
                {
                    GraphLabel(new Rect(innerX, y, 58f, 18f), "Statut :");
                    trg.StatusName = GraphTextField(new Rect(innerX + 60f, y, Mathf.Max(40f, innerW - 60f), 18f), trg.StatusName ?? "");
                }
                else
                {
                    GraphLabel(new Rect(innerX, y, 45f, 18f), "PV <");
                    int.TryParse(GraphTextField(new Rect(innerX + 47f, y, 45f, 18f), trg.HPPercentThreshold.ToString()), out trg.HPPercentThreshold);
                    trg.HPPercentThreshold = Mathf.Clamp(trg.HPPercentThreshold, 1, 100);
                    GraphLabel(new Rect(innerX + 96f, y, Mathf.Max(30f, innerW - 96f), 18f), "% max");
                }
                y += 20f;
            }

            GraphLabel(new Rect(innerX, y, 68f, 18f), "Si nœud :");
            trg.SourceNodeId = GraphTextField(new Rect(innerX + 70f, y, 120f, 18f), trg.SourceNodeId ?? "");
            trg.OneShot = GraphToggle(new Rect(innerX + 194f, y, Mathf.Max(60f, innerW - 194f), 18f), trg.OneShot, "Unique");
            y += 20f;

            if (y + 4f <= cardRect.yMax)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.55f);
                GraphLabel(new Rect(innerX, y, innerW, 18f), trg.GetSummary());
                GUI.color = Color.white;
            }

            // Sortie seule : pas de port d'entrée sur un déclencheur.
            Rect outPort = new Rect(cardRect.xMax - 8f, cardRect.y + 18f, 16f, 16f);
            DrawGraphSolidRect(outPort, new Color(1f, 0.85f, 0.25f));

            if (GraphPrimaryDown(mouseWorld, outPort))
            {
                _wireDraft = new WireConnectionDraft
                {
                    IsActive = true,
                    IsTrigger = true,
                    SourceTrigger = trg,
                    StartPos = outPort.center
                };
                evt.Use();
            }
        }

        private static Texture2D _pureWhiteTex;
        private static Texture2D PureWhiteTex
        {
            get
            {
                if (_pureWhiteTex == null)
                {
                    _pureWhiteTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                    _pureWhiteTex.SetPixel(0, 0, Color.white);
                    _pureWhiteTex.Apply();
                }
                return _pureWhiteTex;
            }
        }

        public static void EnsureDialogueGraphIntegrity(SceneNodeData node)
        {
            if (node == null || node.Dialogues == null) return;

            for (int i = 0; i < node.Dialogues.Count; i++)
            {
                var line = node.Dialogues[i];
                if (string.IsNullOrEmpty(line.LineId))
                {
                    line.LineId = $"line_{i + 1}";
                }

                // Pas de remplissage auto de NextLineId : vide = ordre séquentiel
                // au runtime. L'auto-liaison écrasait le bouton ⊘ (vider la liaison)
                // dès la frame suivante, donnant l'impression qu'il ne fonctionne pas.
            }
        }

        private void DrawBezierWire(Vector2 start, Vector2 end, Color color, float width = 3.5f)
        {
            // Wires sont également en espace monde; conversion unique au rendu.
            start = GraphPoint(start);
            end = GraphPoint(end);
            float dx = end.x - start.x;
            float tangentDist = Mathf.Clamp(Mathf.Abs(dx) * 0.5f, 40f, 220f);
            Vector2 startTan = start + new Vector2(tangentDist, 0f);
            Vector2 endTan = end - new Vector2(tangentDist, 0f);

            float approxLength = Vector2.Distance(start, startTan) + Vector2.Distance(startTan, endTan) + Vector2.Distance(endTan, end);
            int steps = Mathf.Clamp(Mathf.RoundToInt(approxLength / 2.0f), 24, 180);
            float dotSize = width;

            Color prevCol = GUI.color;
            GUI.color = color;

            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float u = 1f - t;
                Vector2 p = (u * u * u) * start + (3f * u * u * t) * startTan + (3f * u * t * t) * endTan + (t * t * t) * end;
                GUI.DrawTexture(new Rect(p.x - dotSize * 0.5f, p.y - dotSize * 0.5f, dotSize, dotSize), PureWhiteTex);
            }

            GUI.color = prevCol;

            Vector2 mid = 0.125f * start + 0.375f * startTan + 0.375f * endTan + 0.125f * end;
            DrawSolidRect(new Rect(mid.x - 4f, mid.y - 4f, 8f, 8f), color);
        }

        private static void DrawSolidRect(Rect r, Color c)
        {
            Color prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, PureWhiteTex);
            GUI.color = prev;
        }

        private void DrawNodeEditor(SceneNodeData node)
        {
            if (node == null) return;
            StorySceneData.EnsureNodeLists(node);
            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUILayout.Label("ID Nœud :", GUILayout.Width(75));
            node.NodeId = GUILayout.TextField(node.NodeId, GUILayout.Width(140));
            GUILayout.Label("Titre :", GUILayout.Width(50));
            node.Title = GUILayout.TextField(node.Title);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Lieu :", GUILayout.Width(75));
            node.Location = GUILayout.TextField(node.Location, GUILayout.Width(160));
            GUILayout.Label("Type :", GUILayout.Width(45));
            int kindCount = Enum.GetValues(typeof(ScenarioNodeKind)).Length;
            int kindIdx = (int)node.Kind;
            if (GUILayout.Button("◀", GUILayout.Width(24)))
            {
                kindIdx = (kindIdx - 1 + kindCount) % kindCount;
                node.Kind = (ScenarioNodeKind)kindIdx;
            }
            GUILayout.Label($"<b>{node.Kind}</b>", GUILayout.Width(90));
            if (GUILayout.Button("▶", GUILayout.Width(24)))
            {
                kindIdx = (kindIdx + 1) % kindCount;
                node.Kind = (ScenarioNodeKind)kindIdx;
            }
            GUILayout.EndHorizontal();

            node.TriggerCombatOnEnter = GUILayout.Toggle(node.TriggerCombatOnEnter, "Déclencher l'état de Combat Tactique à l'entrée de ce nœud");

            GUILayout.Label("<b>Texte Descriptif / Narration :</b>");
            node.Body = GUILayout.TextArea(node.Body, GUILayout.Height(50));

            GUILayout.BeginHorizontal();
            GUILayout.Label("Bouton Continuer :", GUILayout.Width(120));
            node.ContinueLabel = GUILayout.TextField(node.ContinueLabel, GUILayout.Width(140));
            GUILayout.Label("Nœud Suivant :", GUILayout.Width(90));
            node.NextNodeId = GUILayout.TextField(node.NextNodeId);
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            DrawNodeChoicesEditor(node);
            GUILayout.Space(6);
            DrawNodeObjectivesEditor(node);
            GUILayout.Space(6);
            DrawEffectListMini("Effets d'Entrée du Nœud", node.EnterEffects);

            GUILayout.Space(6);
            GUILayout.Label($"<b>Répliques de Dialogue Séquencées ({node.Dialogues.Count})</b>");

            for (int d = 0; d < node.Dialogues.Count; d++)
            {
                var line = node.Dialogues[d];
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"#{d + 1}", GUILayout.Width(25));
                GUILayout.Label("Locuteur :", GUILayout.Width(65));
                line.SpeakerId = GUILayout.TextField(line.SpeakerId, GUILayout.Width(110));
                GUILayout.Label("Didascalie :", GUILayout.Width(75));
                line.StageDirection = GUILayout.TextField(line.StageDirection);

                GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
                if (GUILayout.Button("✕", GUILayout.Width(24)))
                {
                    node.Dialogues.RemoveAt(d);
                    break;
                }
                GUI.backgroundColor = Color.white;
                GUILayout.EndHorizontal();

                line.Speech = GUILayout.TextField(line.Speech);

                GUILayout.BeginHorizontal();
                GUILayout.Label("ID Réplique :", GUILayout.Width(80));
                line.LineId = GUILayout.TextField(line.LineId, GUILayout.Width(110));
                GUILayout.Label("Saut Suivant (ID) :", GUILayout.Width(120));
                line.NextLineId = GUILayout.TextField(line.NextLineId, GUILayout.Width(110));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("Focus Caméra (Acteur ID) :", GUILayout.Width(160));
                line.CameraFocusActorId = GUILayout.TextField(line.CameraFocusActorId, GUILayout.Width(100));
                GUILayout.Label("Pitch :", GUILayout.Width(40));
                float.TryParse(GUILayout.TextField(line.CameraPitch.ToString("0.0"), GUILayout.Width(35)), out line.CameraPitch);
                GUILayout.Label("Dist :", GUILayout.Width(35));
                float.TryParse(GUILayout.TextField(line.CameraDistance.ToString("0.0"), GUILayout.Width(35)), out line.CameraDistance);
                GUILayout.EndHorizontal();

                DrawPrerequisiteEditor(line.Prerequisite, "🔒 Prérequis pour Déclencher cette Réplique");
                DrawAmbienceEditor(line.Ambience);
                DrawAutoSkillCheckEditor(line.AutoSkillCheck);

                GUILayout.Space(2);
                GUILayout.BeginHorizontal();
                string choiceCountLabel = line.Choices.Count > 0 ? $"<color=#00E5FF>✦ Choix Multiples Déclenchés ({line.Choices.Count})</color>" : "✦ Choix Multiples (aucun)";
                GUILayout.Label(choiceCountLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("+ Ajouter Choix", GUILayout.Width(120), GUILayout.Height(20)))
                {
                    line.Choices.Add(new SceneDialogueChoiceData
                    {
                        ChoiceId = $"choice_{d + 1}_{line.Choices.Count + 1}",
                        Label = "Option de réponse du joueur..."
                    });
                }
                GUILayout.EndHorizontal();

                for (int c = 0; c < line.Choices.Count; c++)
                {
                    var choice = line.Choices[c];
                    GUILayout.BeginVertical(GUI.skin.textArea);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"<b>Choix #{c + 1} ID :</b>", GUILayout.Width(85));
                    choice.ChoiceId = GUILayout.TextField(choice.ChoiceId, GUILayout.Width(95));
                    GUILayout.Label("Texte Joueur :", GUILayout.Width(85));
                    choice.Label = GUILayout.TextField(choice.Label);

                    GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
                    if (GUILayout.Button("✕", GUILayout.Width(22), GUILayout.Height(18)))
                    {
                        line.Choices.RemoveAt(c);
                        break;
                    }
                    GUI.backgroundColor = Color.white;
                    GUILayout.EndHorizontal();

                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Vers Réplique (ID) :", GUILayout.Width(125));
                    choice.NextLineId = GUILayout.TextField(choice.NextLineId, GUILayout.Width(110));
                    GUILayout.Label("<color=grey>(Vide = ligne suivante)</color>");
                    GUILayout.EndHorizontal();

                    DrawPrerequisiteEditor(choice.Prerequisite, "🔒 Condition d'Affichage du Choix");
                    DrawChoiceEffectsEditor(choice);
                    DrawChallengeEditor(choice);

                    GUILayout.EndVertical();
                    GUILayout.Space(2);
                }

                GUILayout.EndVertical();
            }

            if (GUILayout.Button("+ Ajouter une Réplique", GUILayout.Height(24)))
            {
                ComputeFreeSpotForNewCard(node, out float freeX, out float freeY);
                node.Dialogues.Add(new SceneDialogueLineData { SpeakerId = "", Speech = "...", GraphPosX = freeX, GraphPosY = freeY });
            }

            GUILayout.Space(10);
            DrawNodeEventsEditor(node);
            GUILayout.Space(10);
            DrawNodeConsequencesEditor(node);
        }

        private void DrawNodeConsequencesEditor(SceneNodeData node)
        {
            if (node.Consequences == null) node.Consequences = new System.Collections.Generic.List<SceneConsequenceData>();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>🎁 Conséquences Récompense / Punition ({node.Consequences.Count})</b>");
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("+ Ajouter Conséquence", GUILayout.Width(170), GUILayout.Height(20)))
            {
                ComputeFreeSpotForNewCard(node, out float freeX, out float freeY);
                node.Consequences.Add(new SceneConsequenceData
                {
                    ConsequenceId = $"cons_{node.Consequences.Count + 1}",
                    Title = "Récompense",
                    GraphPosX = freeX,
                    GraphPosY = freeY
                });
            }
            GUILayout.EndHorizontal();

            for (int k = 0; k < node.Consequences.Count; k++)
            {
                var cons = node.Consequences[k];
                if (cons == null) continue;
                cons.Rewards ??= new SceneDialogueRewardData();
                StorySceneData.EnsureRewardDefaults(cons.Rewards);
                cons.Effects ??= new System.Collections.Generic.List<ScenarioEffect>();
                var rew = cons.Rewards;

                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"<b>#{k + 1}</b>", GUILayout.Width(25));
                GUILayout.Label("ID :", GUILayout.Width(30));
                cons.ConsequenceId = GUILayout.TextField(cons.ConsequenceId, GUILayout.Width(90));
                GUILayout.Label("Titre :", GUILayout.Width(45));
                cons.Title = GUILayout.TextField(cons.Title, GUILayout.Width(140));
                cons.TriggersCombat = GUILayout.Toggle(cons.TriggersCombat, "⚔ Combat");
                GUILayout.FlexibleSpace();
                GUI.backgroundColor = new Color(1f, 0.35f, 0.35f);
                if (GUILayout.Button("✕", GUILayout.Width(22), GUILayout.Height(18)))
                {
                    node.Consequences.RemoveAt(k);
                    GUI.backgroundColor = Color.white;
                    GUILayout.EndHorizontal();
                    GUILayout.EndVertical();
                    break;
                }
                GUI.backgroundColor = Color.white;
                GUILayout.EndHorizontal();

                cons.HasDialogue = GUILayout.Toggle(cons.HasDialogue, "<b>💬 Dialogue affiché à l'exécution (comme une réplique)</b>");
                if (cons.HasDialogue)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Locuteur :", GUILayout.Width(70));
                    cons.SpeakerId = GUILayout.TextField(cons.SpeakerId, GUILayout.Width(110));
                    GUILayout.Label("Didascalie :", GUILayout.Width(75));
                    cons.StageDirection = GUILayout.TextField(cons.StageDirection);
                    GUILayout.EndHorizontal();
                    GUILayout.Label("Réplique :");
                    cons.Speech = GUILayout.TextField(cons.Speech);
                }

                GUILayout.BeginHorizontal();
                GUILayout.Label("Vers Réplique (ID) :", GUILayout.Width(125));
                cons.NextLineId = GUILayout.TextField(cons.NextLineId, GUILayout.Width(110));
                GUILayout.Label("Vers Conséq. (ID) :", GUILayout.Width(125));
                cons.NextConsequenceId = GUILayout.TextField(cons.NextConsequenceId, GUILayout.Width(110));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("+ Crédits CE :", GUILayout.Width(85));
                int.TryParse(GUILayout.TextField(rew.EarnCredits.ToString(), GUILayout.Width(50)), out rew.EarnCredits);
                GUILayout.Label("+ XP :", GUILayout.Width(40));
                int.TryParse(GUILayout.TextField(rew.EarnXP.ToString(), GUILayout.Width(40)), out rew.EarnXP);
                GUILayout.Label("Obj. Validé (ID) :", GUILayout.Width(105));
                rew.CompletionObjectiveId = GUILayout.TextField(rew.CompletionObjectiveId);
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("Item :", GUILayout.Width(90));
                rew.ItemRewardName = GUILayout.TextField(rew.ItemRewardName, GUILayout.Width(140));
                if (GUILayout.Button("🔍", GUILayout.Width(30), GUILayout.Height(18))) OpenItemPicker(cons, -1);
                GUILayout.Label("Qté :", GUILayout.Width(35));
                int.TryParse(GUILayout.TextField(rew.ItemRewardQuantity.ToString(), GUILayout.Width(35)), out rew.ItemRewardQuantity);
                if (rew.ItemRewardQuantity < 1) rew.ItemRewardQuantity = 1;
                GUILayout.Label($"Items suppl. ({rew.AdditionalItems.Count})", GUILayout.Width(110));
                if (GUILayout.Button("+ Item", GUILayout.Width(65), GUILayout.Height(18)))
                    rew.AdditionalItems.Add(new SceneItemRewardEntry { ItemName = "", Quantity = 1 });
                GUILayout.EndHorizontal();

                for (int ri = 0; ri < rew.AdditionalItems.Count; ri++)
                {
                    var entry = rew.AdditionalItems[ri];
                    if (entry == null) { rew.AdditionalItems.RemoveAt(ri); break; }
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"#{ri + 2}", GUILayout.Width(30));
                    entry.ItemName = GUILayout.TextField(entry.ItemName);
                    if (GUILayout.Button("🔍", GUILayout.Width(30), GUILayout.Height(18))) OpenItemPicker(cons, ri);
                    GUILayout.Label("x", GUILayout.Width(15));
                    int.TryParse(GUILayout.TextField(entry.Quantity.ToString(), GUILayout.Width(35)), out entry.Quantity);
                    if (entry.Quantity < 1) entry.Quantity = 1;
                    GUI.backgroundColor = new Color(1f, 0.3f, 0.3f);
                    if (GUILayout.Button("✕", GUILayout.Width(22), GUILayout.Height(18)))
                    {
                        rew.AdditionalItems.RemoveAt(ri);
                        GUI.backgroundColor = Color.white;
                        GUILayout.EndHorizontal();
                        break;
                    }
                    GUI.backgroundColor = Color.white;
                    GUILayout.EndHorizontal();
                }

                DrawEffectListMini("Effets Conséquence", cons.Effects);

                GUILayout.EndVertical();
                GUILayout.Space(2);
            }
        }

        private void DrawNodeChoicesEditor(SceneNodeData node)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>Choix Majeurs du Nœud ({node.Choices.Count})</b>");
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("+ Ajouter Choix", GUILayout.Width(130), GUILayout.Height(20)))
            {
                node.Choices.Add(new ScenarioChoice { Id = $"choice_{node.Choices.Count + 1}", Label = "Nouveau choix", NextNodeId = "" });
            }
            GUILayout.EndHorizontal();

            for (int c = 0; c < node.Choices.Count; c++)
            {
                var ch = node.Choices[c];
                if (ch == null) continue;
                GUILayout.BeginVertical(GUI.skin.textArea);
                GUILayout.BeginHorizontal();
                GUILayout.Label("ID :", GUILayout.Width(30));
                ch.Id = GUILayout.TextField(ch.Id, GUILayout.Width(130));
                GUILayout.Label("Cible ➔ :", GUILayout.Width(60));
                ch.NextNodeId = GUILayout.TextField(ch.NextNodeId, GUILayout.Width(130));
                GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
                if (GUILayout.Button("✕", GUILayout.Width(22), GUILayout.Height(18)))
                {
                    node.Choices.RemoveAt(c);
                    GUI.backgroundColor = Color.white;
                    GUILayout.EndHorizontal();
                    GUILayout.EndVertical();
                    break;
                }
                GUI.backgroundColor = Color.white;
                GUILayout.EndHorizontal();
                GUILayout.Label("Libellé :");
                ch.Label = GUILayout.TextField(ch.Label);
                GUILayout.Label("Conséquence :");
                ch.Consequence = GUILayout.TextField(ch.Consequence);
                if (ch.Effects == null) ch.Effects = new System.Collections.Generic.List<ScenarioEffect>();
                DrawEffectListMini("Effets Choix", ch.Effects);
                GUILayout.EndVertical();
                GUILayout.Space(2);
            }
        }

        private void DrawNodeObjectivesEditor(SceneNodeData node)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>Objectifs du Nœud ({node.Objectives.Count})</b>");
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("+ Ajouter Objectif", GUILayout.Width(140), GUILayout.Height(20)))
            {
                node.Objectives.Add(new ScenarioObjective { Id = $"obj_{node.Objectives.Count + 1}", Label = "Nouvel objectif" });
            }
            GUILayout.EndHorizontal();

            for (int i = 0; i < node.Objectives.Count; i++)
            {
                var obj = node.Objectives[i];
                if (obj == null) continue;
                GUILayout.BeginHorizontal();
                GUILayout.Label("ID :", GUILayout.Width(30));
                obj.Id = GUILayout.TextField(obj.Id, GUILayout.Width(130));
                GUILayout.Label("Libellé :", GUILayout.Width(55));
                obj.Label = GUILayout.TextField(obj.Label, GUILayout.Width(150));
                obj.Optional = GUILayout.Toggle(obj.Optional, "Optionnel", GUILayout.Width(90));
                GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
                if (GUILayout.Button("✕", GUILayout.Width(22), GUILayout.Height(18)))
                {
                    node.Objectives.RemoveAt(i);
                    GUI.backgroundColor = Color.white;
                    GUILayout.EndHorizontal();
                    break;
                }
                GUI.backgroundColor = Color.white;
                GUILayout.EndHorizontal();
                GUILayout.Label("Détail :");
                obj.Detail = GUILayout.TextField(obj.Detail);
            }
        }

        private void DrawPrerequisiteEditor(ScenePrerequisiteData prereq, string label)
        {
            if (prereq == null) return;
            prereq.HasPrerequisite = GUILayout.Toggle(prereq.HasPrerequisite, $"<b>{label}</b>");
            if (!prereq.HasPrerequisite) return;

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Type :", GUILayout.Width(45));
            prereq.Kind = (PrerequisiteKind)GUILayout.Toolbar((int)prereq.Kind, new[] { "None", "Compétence", "Spécialité", "Carac", "Flag", "Item", "Route" }, GUILayout.Height(20));
            GUILayout.EndHorizontal();

            switch (prereq.Kind)
            {
                case PrerequisiteKind.SkillTraining:
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Compétence :", GUILayout.Width(90));
                    int skillCount = Enum.GetValues(typeof(SkillType)).Length;
                    int skillIdx = (int)prereq.RequiredSkill;
                    if (GUILayout.Button("◀", GUILayout.Width(20))) { skillIdx = (skillIdx - 1 + skillCount) % skillCount; prereq.RequiredSkill = (SkillType)skillIdx; }
                    GUILayout.Label($"<b>{SkillDefinitions.GetDisplayName(prereq.RequiredSkill)}</b>", GUILayout.Width(140));
                    if (GUILayout.Button("▶", GUILayout.Width(20))) { skillIdx = (skillIdx + 1) % skillCount; prereq.RequiredSkill = (SkillType)skillIdx; }
                    GUILayout.Label("Palier Min :", GUILayout.Width(75));
                    int.TryParse(GUILayout.TextField(prereq.MinTrainingLevel.ToString(), GUILayout.Width(35)), out prereq.MinTrainingLevel);
                    GUILayout.EndHorizontal();
                    break;

                case PrerequisiteKind.Specialization:
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Nom Spécialisation :", GUILayout.Width(140));
                    prereq.SpecializationName = GUILayout.TextField(prereq.SpecializationName);
                    GUILayout.EndHorizontal();
                    break;

                case PrerequisiteKind.AttributeThreshold:
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Attribut :", GUILayout.Width(60));
                    prereq.AttributeName = GUILayout.TextField(prereq.AttributeName, GUILayout.Width(110));
                    GUILayout.Label("Valeur Min :", GUILayout.Width(75));
                    int.TryParse(GUILayout.TextField(prereq.MinAttributeValue.ToString(), GUILayout.Width(35)), out prereq.MinAttributeValue);
                    GUILayout.EndHorizontal();
                    break;

                case PrerequisiteKind.CampaignFlag:
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Clé Flag :", GUILayout.Width(65));
                    prereq.FlagKey = GUILayout.TextField(prereq.FlagKey, GUILayout.Width(140));
                    prereq.FlagMustBeSet = GUILayout.Toggle(prereq.FlagMustBeSet, "Doit être Posé (Vrai)");
                    GUILayout.EndHorizontal();
                    break;

                case PrerequisiteKind.ItemInInventory:
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Objet Requis :", GUILayout.Width(90));
                    prereq.ItemName = GUILayout.TextField(prereq.ItemName);
                    GUILayout.EndHorizontal();
                    break;

                case PrerequisiteKind.StoryRouteRequired:
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Route :", GUILayout.Width(50));
                    prereq.RequiredRoute = (StoryRoute)GUILayout.Toolbar((int)prereq.RequiredRoute, new[] { "None", "Vardis", "Indep", "Imper" }, GUILayout.Height(18));
                    GUILayout.EndHorizontal();
                    break;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("Acteur Spécifique (optionnel) :", GUILayout.Width(180));
            prereq.SpecificActorId = GUILayout.TextField(prereq.SpecificActorId, GUILayout.Width(100));
            GUILayout.Label("<color=grey>(Vide = n'importe quel membre de l'escouade)</color>");
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }

        private void DrawAmbienceEditor(SceneAmbienceData ambience)
        {
            if (ambience == null) return;
            ambience.HasAmbience = GUILayout.Toggle(ambience.HasAmbience, "<b>🔊 Déclencheur Audio & Ambiance Atmosphérique</b>");
            if (!ambience.HasAmbience) return;

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Sound Cue ID :", GUILayout.Width(95));
            ambience.SoundCueId = GUILayout.TextField(ambience.SoundCueId, GUILayout.Width(110));
            GUILayout.Label("Secousse Caméra :", GUILayout.Width(115));
            float.TryParse(GUILayout.TextField(ambience.CameraShakeIntensity.ToString("0.0"), GUILayout.Width(40)), out ambience.CameraShakeIntensity);
            ambience.TriggerAlarm = GUILayout.Toggle(ambience.TriggerAlarm, "Gyrophare Alarme Rouge");
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            ambience.ChangeMusic = GUILayout.Toggle(ambience.ChangeMusic, "Changer Musique", GUILayout.Width(130));
            if (ambience.ChangeMusic)
            {
                GUILayout.Label("Mood :", GUILayout.Width(45));
                ambience.MusicMood = (Killtime.Audio.MusicMood)GUILayout.Toolbar((int)ambience.MusicMood, new[] { "Explore", "Combat", "Stealth", "Mystery" }, GUILayout.Height(18));
                GUILayout.Label("Intensité :", GUILayout.Width(65));
                ambience.MusicIntensity = (Killtime.Audio.MusicIntensity)GUILayout.Toolbar((int)ambience.MusicIntensity, new[] { "Calm", "Low", "Mid", "High" }, GUILayout.Height(18));
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private void DrawAutoSkillCheckEditor(SceneAutoSkillCheckData autoCheck)
        {
            if (autoCheck == null) return;
            autoCheck.HasAutoCheck = GUILayout.Toggle(autoCheck.HasAutoCheck, "<b>🎲 Jet de Compétence Automatique / Passif (Sans Choix)</b>");
            if (!autoCheck.HasAutoCheck) return;

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Compétence :", GUILayout.Width(90));
            int skillCount = Enum.GetValues(typeof(SkillType)).Length;
            int skillIdx = (int)autoCheck.RequiredSkill;
            if (GUILayout.Button("◀", GUILayout.Width(20))) { skillIdx = (skillIdx - 1 + skillCount) % skillCount; autoCheck.RequiredSkill = (SkillType)skillIdx; }
            GUILayout.Label($"<b>{SkillDefinitions.GetDisplayName(autoCheck.RequiredSkill)}</b>", GUILayout.Width(140));
            if (GUILayout.Button("▶", GUILayout.Width(20))) { skillIdx = (skillIdx + 1) % skillCount; autoCheck.RequiredSkill = (SkillType)skillIdx; }
            GUILayout.Label("Seuil SD :", GUILayout.Width(60));
            int.TryParse(GUILayout.TextField(autoCheck.TargetDC.ToString(), GUILayout.Width(35)), out autoCheck.TargetDC);
            GUILayout.Label("Acteur (ID) :", GUILayout.Width(80));
            autoCheck.SpecificActorId = GUILayout.TextField(autoCheck.SpecificActorId, GUILayout.Width(90));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Discours si Succès :", GUILayout.Width(125));
            autoCheck.SuccessSpeech = GUILayout.TextField(autoCheck.SuccessSpeech);
            GUILayout.Label("Vers Réplique :", GUILayout.Width(90));
            autoCheck.SuccessNextLineId = GUILayout.TextField(autoCheck.SuccessNextLineId, GUILayout.Width(90));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Discours si Échec :", GUILayout.Width(125));
            autoCheck.FailureSpeech = GUILayout.TextField(autoCheck.FailureSpeech);
            GUILayout.Label("Vers Réplique :", GUILayout.Width(90));
            autoCheck.FailureNextLineId = GUILayout.TextField(autoCheck.FailureNextLineId, GUILayout.Width(90));
            GUILayout.EndHorizontal();

            DrawEffectListMini("Effets Succès", autoCheck.SuccessEffects);
            DrawEffectListMini("Effets Échec", autoCheck.FailureEffects);

            GUILayout.EndVertical();
        }

        private void DrawNodeEventsEditor(SceneNodeData node)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>⚡ Événements Scéniques du Nœud ({node.Events.Count})</b>");
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("+ Ajouter un Événement", GUILayout.Width(180), GUILayout.Height(22)))
            {
                ComputeFreeSpotForNewCard(node, out float freeX, out float freeY);
                node.Events.Add(new SceneEventData
                {
                    EventId = $"event_{node.Events.Count + 1}",
                    Title = "Nouvel Événement",
                    GraphPosX = freeX,
                    GraphPosY = freeY
                });
            }
            GUILayout.EndHorizontal();

            for (int i = 0; i < node.Events.Count; i++)
            {
                var evt = node.Events[i];
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"<b>#{i + 1}</b>", GUILayout.Width(25));
                GUILayout.Label("ID :", GUILayout.Width(30));
                evt.EventId = GUILayout.TextField(evt.EventId, GUILayout.Width(90));
                GUILayout.Label("Titre :", GUILayout.Width(45));
                evt.Title = GUILayout.TextField(evt.Title, GUILayout.Width(160));
                GUILayout.Label("Type :", GUILayout.Width(40));
                evt.Kind = (SceneEventKind)GUILayout.Toolbar((int)evt.Kind, new[] { "Normal", "Combat", "Spawn", "Camera", "Shake", "Obj", "Loot" }, GUILayout.Height(18));

                GUI.backgroundColor = new Color(1f, 0.35f, 0.35f);
                if (GUILayout.Button("✕", GUILayout.Width(22), GUILayout.Height(18)))
                {
                    node.Events.RemoveAt(i);
                    break;
                }
                GUI.backgroundColor = Color.white;
                GUILayout.EndHorizontal();

                DrawPrerequisiteEditor(evt.Prerequisite, "🔒 Condition pour Déclencher cet Événement");
                DrawAmbienceEditor(evt.Ambience);
                DrawAutoSkillCheckEditor(evt.AutoSkillCheck);
                DrawEffectListMini("Effets Événement", evt.Effects);

                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUI.backgroundColor = new Color(1.0f, 0.7f, 0.2f);
                if (GUILayout.Button("⚡ Tester / Déclencher en Direct", GUILayout.Width(220), GUILayout.Height(22)))
                {
                    var controller = FindAnyObjectByType<Scenes.JsonStorySceneController>();
                    controller?.ExecuteScenicEvent(evt);
                }
                GUI.backgroundColor = Color.white;
                GUILayout.EndHorizontal();

                GUILayout.EndVertical();
                GUILayout.Space(2);
            }
        }

        private void DrawChoiceEffectsEditor(SceneDialogueChoiceData choice)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<color=grey>Effets de Campagne ({choice.Effects.Count})</color>", GUILayout.Width(150));
            if (GUILayout.Button("+ Effet", GUILayout.Width(65), GUILayout.Height(18)))
            {
                choice.Effects.Add(new ScenarioEffect { Type = ScenarioEffectType.AddFlag, Key = "nouveau_flag" });
            }
            GUILayout.EndHorizontal();

            for (int e = 0; e < choice.Effects.Count; e++)
            {
                var eff = choice.Effects[e];
                GUILayout.BeginHorizontal();
                eff.Type = (ScenarioEffectType)GUILayout.Toolbar((int)eff.Type, new[] { "Route", "Flag", "Int", "Journal" }, GUILayout.Height(18), GUILayout.Width(180));
                switch (eff.Type)
                {
                    case ScenarioEffectType.SetRoute:
                        eff.Route = (StoryRoute)GUILayout.Toolbar((int)eff.Route, new[] { "None", "Vardis", "Indep", "Imper" }, GUILayout.Height(18));
                        break;
                    case ScenarioEffectType.AddFlag:
                        GUILayout.Label("Clé :", GUILayout.Width(35));
                        eff.Key = GUILayout.TextField(eff.Key);
                        break;
                    case ScenarioEffectType.AddInteger:
                        GUILayout.Label("Clé :", GUILayout.Width(35));
                        eff.Key = GUILayout.TextField(eff.Key, GUILayout.Width(80));
                        GUILayout.Label("Qté :", GUILayout.Width(35));
                        int.TryParse(GUILayout.TextField(eff.Amount.ToString(), GUILayout.Width(35)), out eff.Amount);
                        break;
                    case ScenarioEffectType.AddJournal:
                        GUILayout.Label("Texte :", GUILayout.Width(45));
                        eff.Text = GUILayout.TextField(eff.Text);
                        break;
                }
                GUI.backgroundColor = new Color(1f, 0.3f, 0.3f);
                if (GUILayout.Button("✕", GUILayout.Width(20), GUILayout.Height(18)))
                {
                    choice.Effects.RemoveAt(e);
                    break;
                }
                GUI.backgroundColor = Color.white;
                GUILayout.EndHorizontal();
            }
        }

        private void DrawChallengeEditor(SceneDialogueChoiceData choice)
        {
            if (choice == null) return;
            choice.Challenge ??= new SceneDialogueChallengeData();
            var ch = choice.Challenge;
            ch.HasChallenge = GUILayout.Toggle(ch.HasChallenge, "<b>🎲 Défi de Compétence (Jet de Dé & Événements)</b>");
            if (!ch.HasChallenge) return;
            ch.SuccessRewards ??= new SceneDialogueRewardData();
            StorySceneData.EnsureRewardDefaults(ch.SuccessRewards);

            GUILayout.BeginVertical(GUI.skin.box);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Mode :", GUILayout.Width(45));
            int modeIdx = ch.IsOpposed ? 1 : 0;
            int newMode = GUILayout.Toolbar(modeIdx, new[] { "🎲 Test SD", "⚔️ Défi Opposé" }, GUILayout.Height(20), GUILayout.Width(260));
            if (newMode != modeIdx) ch.IsOpposed = newMode == 1;
            GUILayout.Label(ch.IsOpposed
                ? "<color=grey>(jet joueur vs jet opposant)</color>"
                : "<color=grey>(jet joueur vs SD fixe)</color>");
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Compétence :", GUILayout.Width(90));
            int skillCount = Enum.GetValues(typeof(SkillType)).Length;
            int skillIdx = (int)ch.RequiredSkill;
            if (GUILayout.Button("◀", GUILayout.Width(22)))
            {
                skillIdx = (skillIdx - 1 + skillCount) % skillCount;
                ch.RequiredSkill = (SkillType)skillIdx;
            }
            GUILayout.Label($"<b>{SkillDefinitions.GetDisplayName(ch.RequiredSkill)}</b>", GUILayout.Width(150));
            if (GUILayout.Button("▶", GUILayout.Width(22)))
            {
                skillIdx = (skillIdx + 1) % skillCount;
                ch.RequiredSkill = (SkillType)skillIdx;
            }

            GUILayout.Label("Seuil SD :", GUILayout.Width(60));
            int.TryParse(GUILayout.TextField(ch.TargetDC.ToString(), GUILayout.Width(35)), out ch.TargetDC);

            GUILayout.Label("Acteur Testé (ID) :", GUILayout.Width(110));
            ch.SpecificActorId = GUILayout.TextField(ch.SpecificActorId, GUILayout.Width(90));
            GUILayout.EndHorizontal();

            if (ch.IsOpposed)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Comp. Adverse :", GUILayout.Width(90));
                int oppCount = Enum.GetValues(typeof(SkillType)).Length;
                int oppIdx = (int)ch.OpposedSkill;
                if (GUILayout.Button("◀", GUILayout.Width(22)))
                {
                    oppIdx = (oppIdx - 1 + oppCount) % oppCount;
                    ch.OpposedSkill = (SkillType)oppIdx;
                }
                GUILayout.Label($"<b>{SkillDefinitions.GetDisplayName(ch.OpposedSkill)}</b>", GUILayout.Width(150));
                if (GUILayout.Button("▶", GUILayout.Width(22)))
                {
                    oppIdx = (oppIdx + 1) % oppCount;
                    ch.OpposedSkill = (SkillType)oppIdx;
                }
                GUILayout.Label("Opposant (ID) :", GUILayout.Width(90));
                ch.OpposedActorId = GUILayout.TextField(ch.OpposedActorId, GUILayout.Width(90));
                GUILayout.Label("+ Bonus :", GUILayout.Width(60));
                int.TryParse(GUILayout.TextField(ch.OpposedBonus.ToString(), GUILayout.Width(35)), out ch.OpposedBonus);
                GUILayout.EndHorizontal();
                GUILayout.Label("<color=grey>(Vide = locuteur de la réplique ; si introuvable, total adverse fixe = SD + Bonus)</color>");
            }
            else
            {
                GUILayout.Label("<color=grey>(SD fixe : le joueur lance Compétence contre Seuil SD)</color>");
            }

            // Branche de Succès
            GUILayout.Space(2);
            GUI.backgroundColor = new Color(0.15f, 0.4f, 0.25f);
            GUILayout.BeginVertical(GUI.skin.box);
            GUI.backgroundColor = Color.white;
            GUILayout.Label("<color=#55FF88><b>✔ BRANCHE EN CAS DE SUCCÈS</b></color>");

            GUILayout.BeginHorizontal();
            GUILayout.Label("Vers Réplique (ID) :", GUILayout.Width(125));
            ch.SuccessNextLineId = GUILayout.TextField(ch.SuccessNextLineId, GUILayout.Width(110));
            GUILayout.Label("Vers Conséq. (ID) :", GUILayout.Width(125));
            ch.SuccessConsequenceId = GUILayout.TextField(ch.SuccessConsequenceId, GUILayout.Width(110));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("+ Crédits CE :", GUILayout.Width(80));
            int.TryParse(GUILayout.TextField(ch.SuccessRewards.EarnCredits.ToString(), GUILayout.Width(50)), out ch.SuccessRewards.EarnCredits);
            GUILayout.Label("+ XP :", GUILayout.Width(40));
            int.TryParse(GUILayout.TextField(ch.SuccessRewards.EarnXP.ToString(), GUILayout.Width(40)), out ch.SuccessRewards.EarnXP);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Item Obtenu :", GUILayout.Width(90));
            ch.SuccessRewards.ItemRewardName = GUILayout.TextField(ch.SuccessRewards.ItemRewardName, GUILayout.Width(140));
            if (GUILayout.Button("🔍", GUILayout.Width(30), GUILayout.Height(18))) OpenItemPicker(choice);
            GUILayout.Label("Obj. Validé (ID) :", GUILayout.Width(105));
            ch.SuccessRewards.CompletionObjectiveId = GUILayout.TextField(ch.SuccessRewards.CompletionObjectiveId);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Qté item :", GUILayout.Width(90));
            int.TryParse(GUILayout.TextField(ch.SuccessRewards.ItemRewardQuantity.ToString(), GUILayout.Width(40)), out ch.SuccessRewards.ItemRewardQuantity);
            if (ch.SuccessRewards.ItemRewardQuantity < 1) ch.SuccessRewards.ItemRewardQuantity = 1;
            GUILayout.Label($"Items suppl. ({ch.SuccessRewards.AdditionalItems.Count})", GUILayout.Width(130));
            if (GUILayout.Button("+ Item", GUILayout.Width(70), GUILayout.Height(18)))
                ch.SuccessRewards.AdditionalItems.Add(new SceneItemRewardEntry { ItemName = "", Quantity = 1 });
            GUILayout.EndHorizontal();

            for (int ri = 0; ri < ch.SuccessRewards.AdditionalItems.Count; ri++)
            {
                var entry = ch.SuccessRewards.AdditionalItems[ri];
                if (entry == null) { ch.SuccessRewards.AdditionalItems.RemoveAt(ri); break; }
                GUILayout.BeginHorizontal();
                GUILayout.Label($"#{ri + 2}", GUILayout.Width(30));
                entry.ItemName = GUILayout.TextField(entry.ItemName);
                if (GUILayout.Button("🔍", GUILayout.Width(30), GUILayout.Height(18))) OpenItemPicker(choice, ri);
                GUILayout.Label("x", GUILayout.Width(15));
                int.TryParse(GUILayout.TextField(entry.Quantity.ToString(), GUILayout.Width(35)), out entry.Quantity);
                if (entry.Quantity < 1) entry.Quantity = 1;
                GUI.backgroundColor = new Color(1f, 0.3f, 0.3f);
                if (GUILayout.Button("✕", GUILayout.Width(22), GUILayout.Height(18)))
                {
                    ch.SuccessRewards.AdditionalItems.RemoveAt(ri);
                    GUI.backgroundColor = Color.white;
                    GUILayout.EndHorizontal();
                    break;
                }
                GUI.backgroundColor = Color.white;
                GUILayout.EndHorizontal();
            }

            DrawEffectListMini("Effets Succès", ch.SuccessEffects);

            GUILayout.EndVertical();

            // Branche d'Échec
            GUILayout.Space(2);
            GUI.backgroundColor = new Color(0.45f, 0.15f, 0.15f);
            GUILayout.BeginVertical(GUI.skin.box);
            GUI.backgroundColor = Color.white;
            GUILayout.Label("<color=#FF6655><b>✕ BRANCHE EN CAS D'ÉCHEC</b></color>");

            GUILayout.BeginHorizontal();
            GUILayout.Label("Vers Réplique (ID) :", GUILayout.Width(125));
            ch.FailureNextLineId = GUILayout.TextField(ch.FailureNextLineId, GUILayout.Width(110));
            GUILayout.Label("Vers Conséq. (ID) :", GUILayout.Width(125));
            ch.FailureConsequenceId = GUILayout.TextField(ch.FailureConsequenceId, GUILayout.Width(110));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            ch.FailureTriggersCombat = GUILayout.Toggle(ch.FailureTriggersCombat, "<b>Déclencher Combat Immédiat</b>");
            GUILayout.EndHorizontal();

            DrawEffectListMini("Effets Échec", ch.FailureEffects);

            GUILayout.EndVertical();

            GUILayout.EndVertical();
        }

        private static void DrawEffectListMini(string label, List<ScenarioEffect> list)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<color=grey>{label} ({list.Count})</color>", GUILayout.Width(120));
            if (GUILayout.Button("+ Effet", GUILayout.Width(60), GUILayout.Height(18)))
            {
                list.Add(new ScenarioEffect { Type = ScenarioEffectType.AddFlag, Key = "flag_result" });
            }
            GUILayout.EndHorizontal();

            for (int i = 0; i < list.Count; i++)
            {
                var eff = list[i];
                GUILayout.BeginHorizontal();
                eff.Type = (ScenarioEffectType)GUILayout.Toolbar((int)eff.Type, new[] { "Route", "Flag", "Int", "Journal" }, GUILayout.Height(18), GUILayout.Width(170));
                switch (eff.Type)
                {
                    case ScenarioEffectType.SetRoute:
                        eff.Route = (StoryRoute)GUILayout.Toolbar((int)eff.Route, new[] { "None", "Vardis", "Indep", "Imper" }, GUILayout.Height(18));
                        break;
                    case ScenarioEffectType.AddFlag:
                        GUILayout.Label("Clé :", GUILayout.Width(35));
                        eff.Key = GUILayout.TextField(eff.Key);
                        break;
                    case ScenarioEffectType.AddInteger:
                        GUILayout.Label("Clé :", GUILayout.Width(35));
                        eff.Key = GUILayout.TextField(eff.Key, GUILayout.Width(80));
                        GUILayout.Label("Qté :", GUILayout.Width(35));
                        int.TryParse(GUILayout.TextField(eff.Amount.ToString(), GUILayout.Width(35)), out eff.Amount);
                        break;
                    case ScenarioEffectType.AddJournal:
                        GUILayout.Label("Txt :", GUILayout.Width(35));
                        eff.Text = GUILayout.TextField(eff.Text);
                        break;
                }
                GUI.backgroundColor = new Color(1f, 0.3f, 0.3f);
                if (GUILayout.Button("✕", GUILayout.Width(20), GUILayout.Height(18)))
                {
                    list.RemoveAt(i);
                    break;
                }
                GUI.backgroundColor = Color.white;
                GUILayout.EndHorizontal();
            }
        }

        private void SaveCurrentSceneJson()
        {
            string directory = Path.Combine(Application.persistentDataPath, "Scenarios");
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

            // Sauvegarder vers le fichier d'origine si chargé depuis un fichier existant,
            // sinon construire le chemin à partir du SceneId (nouvelle scène).
            string path;
            if (!string.IsNullOrEmpty(_activeFilePath) && File.Exists(_activeFilePath))
            {
                path = _activeFilePath;
            }
            else
            {
                string filename = string.IsNullOrWhiteSpace(_data.SceneId) ? "scene_export" : _data.SceneId;
                path = Path.Combine(directory, $"{filename}.json");
            }

            string json = JsonUtility.ToJson(_data, true);
            File.WriteAllText(path, json);
            _activeFilePath = path;
            RefreshCatalogCache();
            Debug.Log($"[ScenarioEditor] Scène enregistrée sous : {path}");
        }

        private void ReloadCurrentSceneJson()
        {
            // Recharger depuis le fichier réel si disponible
            if (!string.IsNullOrEmpty(_activeFilePath) && File.Exists(_activeFilePath))
            {
                LoadSceneFromPath(_activeFilePath);
                return;
            }

            // Fallback : reconstruction du chemin depuis SceneId (nouvelles scènes)
            string directory = Path.Combine(Application.persistentDataPath, "Scenarios");
            string filename = string.IsNullOrWhiteSpace(_data.SceneId) ? "scene_export" : _data.SceneId;
            string path = Path.Combine(directory, $"{filename}.json");
            if (File.Exists(path))
            {
                LoadSceneFromPath(path);
            }
        }

        private bool _confirmDeleteScene = false;

        private void DeleteCurrentSceneJson()
        {
            string sceneId = _data.SceneId;
            if (string.IsNullOrWhiteSpace(sceneId)) return;

            var director = ScenarioDirector.EnsureInstance();
            if (director.ActiveScenario != null && string.Equals(director.ActiveScenario.Id, sceneId, StringComparison.OrdinalIgnoreCase))
            {
                director.ResetCampaign();
                StorySceneManager.EnsureInstance().CleanupCurrentScene();
            }

            // Supprimer par le chemin réel du fichier chargé, pas par reconstruction
            // depuis SceneId (qui peut pointer vers un autre fichier si le nom diffère).
            string deletedName;
            if (!string.IsNullOrEmpty(_activeFilePath) && File.Exists(_activeFilePath))
            {
                deletedName = Path.GetFileName(_activeFilePath);
                StorySceneRepository.DeleteSceneByPath(_activeFilePath);
            }
            else
            {
                deletedName = $"{sceneId}.json";
                StorySceneRepository.DeleteScene(sceneId);
            }

            ScenarioCatalog.Unregister(sceneId);
            ScenarioCatalog.ReloadFromDisk();

            CombatHUD.Instance?.AddAdvancedLog($"🗑 Fichier '{deletedName}' supprimé.", LogCategory.MovementAndTurns, "[SCÈNE]", Color.yellow);
            _activeFilePath = "";
            RefreshCatalogCache();
            CreateDefaultSceneTemplate();
        }

        private void DeployCurrentSceneToRuntime()
        {
            var sceneMgr = StorySceneManager.EnsureInstance();
            var grid = FindAnyObjectByType<TacticalHexGrid>();
            var turnManager = FindAnyObjectByType<TurnManager>();
            var arena = FindAnyObjectByType<CombatDevArena>();
            var director = ScenarioDirector.EnsureInstance();

            if (sceneMgr.CurrentSceneController != null)
            {
                sceneMgr.CurrentSceneController.CleanupScene();
            }

            var go = new GameObject($"[SceneRuntime] {_data.SceneId}");
            var controller = go.AddComponent<Scenes.JsonStorySceneController>();
            controller.SetSceneData(_data);
            sceneMgr.CurrentSceneController = controller;

            var definition = ScenarioCatalog.FromSceneData(_data);
            if (definition == null)
            {
                Debug.LogWarning("[ScenarioEditor] Scène invalide : impossible de construire une définition déployable.");
                return;
            }

            ScenarioCatalog.Register(definition);
            director.StartScenario(_data.SceneId);
            sceneMgr.NotifyExternalScenarioStarted(_data.SceneId);
            StartCoroutine(controller.InitializeSceneRoutine(grid, turnManager, arena, director));
        }

        private void MoveSceneInList(int fromIdx, int toIdx)
        {
            if (fromIdx < 0 || fromIdx >= _availableSceneFiles.Count) return;
            if (toIdx < 0 || toIdx >= _availableSceneFiles.Count) return;
            if (fromIdx == toIdx) return;

            string movedFilePath = _availableSceneFiles[fromIdx];

            // Réordonner la liste des fichiers en mémoire
            _availableSceneFiles.RemoveAt(fromIdx);
            _availableSceneFiles.Insert(toIdx, movedFilePath);

            // Enregistrer l'ordre personnalisé
            SaveSceneOrder();

            CombatHUD.Instance?.AddAdvancedLog(
                $"↕ Scène '{Path.GetFileName(movedFilePath)}' repositionnée en #{toIdx + 1}.",
                LogCategory.MovementAndTurns,
                "[SCÈNE]",
                Color.cyan
            );
        }

        private void SaveSceneOrder()
        {
            var names = new List<string>(_availableSceneFiles.Count);
            for (int i = 0; i < _availableSceneFiles.Count; i++)
            {
                names.Add(Path.GetFileName(_availableSceneFiles[i]));
            }
            PlayerPrefs.SetString("ScenarioEditor_SceneOrder", string.Join(";", names));
            PlayerPrefs.Save();
        }

        private string GetAutoNextScenarioId()
        {
            // 1. Détection prioritaire par position séquentielle dans la liste ordonnée des scènes
            if (_availableSceneFiles != null && _availableSceneFiles.Count > 0)
            {
                int currentIdx = !string.IsNullOrEmpty(_activeFilePath)
                    ? _availableSceneFiles.FindIndex(f => string.Equals(f, _activeFilePath, StringComparison.OrdinalIgnoreCase))
                    : _availableSceneFiles.FindIndex(f => string.Equals(Path.GetFileNameWithoutExtension(f), _data.SceneId, StringComparison.OrdinalIgnoreCase));

                if (currentIdx >= 0)
                {
                    // Si c'est la dernière scène de la liste de l'éditeur, aucune suite automatique
                    if (currentIdx + 1 >= _availableSceneFiles.Count)
                    {
                        return null;
                    }

                    string nextFilePath = _availableSceneFiles[currentIdx + 1];
                    if (File.Exists(nextFilePath))
                    {
                        string nextSceneId = StorySceneRepository.GetSceneIdFromPath(nextFilePath);
                        if (!string.IsNullOrWhiteSpace(nextSceneId))
                        {
                            return nextSceneId;
                        }
                        return Path.GetFileNameWithoutExtension(nextFilePath);
                    }
                }
            }

            // 2. Fallback via le catalogue global
            return ScenarioCatalog.GetNextScenarioId(_data.SceneId);
        }

        private void UpdateDropTargetIndex(float mouseY)
        {
            if (_sceneRowRects == null || _sceneRowRects.Count == 0) return;
            if (mouseY < _sceneRowRects[0].y + _sceneRowRects[0].height * 0.5f)
            {
                _dropTargetSceneIndex = 0;
                return;
            }
            if (mouseY > _sceneRowRects[_sceneRowRects.Count - 1].y + _sceneRowRects[_sceneRowRects.Count - 1].height * 0.5f)
            {
                _dropTargetSceneIndex = _sceneRowRects.Count;
                return;
            }
            for (int k = 0; k < _sceneRowRects.Count; k++)
            {
                Rect r = _sceneRowRects[k];
                if (mouseY >= r.y && mouseY <= r.yMax)
                {
                    _dropTargetSceneIndex = mouseY < r.y + r.height * 0.5f ? k : k + 1;
                    return;
                }
            }
        }

        private void DrawDropInsertionLine()
        {
            GUILayout.Space(2);
            Rect r = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(4f));
            Color prevColor = GUI.color;
            GUI.color = new Color(0f, 0.95f, 1f, 0.95f);
            GUI.DrawTexture(new Rect(r.x + 2f, r.y + 1f, r.width - 4f, 2f), PureWhiteTex);
            GUI.color = prevColor;
            GUILayout.Space(2);
        }
    }
}

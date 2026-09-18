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

        private readonly string[] _tabs = { "📋 Scène & Fichier", "🎬 Nœuds & Graphe Nodal" };
        private int _selectedTab = 1;
        private Vector2 _scroll;
        private bool _showRawJsonPreview = false;

        private StorySceneData _data = new();
        private string _activeFilePath = "";

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
            public bool IsInteractable;
            public bool IsNode;
            public bool IsNodeChoice;
            public int NodeChoiceIndex;
            public bool FromConsequence;
            public bool ConsLinkToCons;
            public bool FromChallengeLink;
            public bool ChallengeLinkIsSuccess;
            public SceneDialogueLineData SourceLine;
            public SceneDialogueChoiceData SourceChoice;
            public SceneDialogueChallengeData SourceChallenge;
            public SceneEventData SourceEvent;
            public SceneTriggerData SourceTrigger;
            public SceneInteractableSpawnData SourceInteractable;
            public SceneNodeData SourceNode;
            public SceneConsequenceData SourceConsequence;
            public Vector2 StartPos;
        }

        private WireConnectionDraft _wireDraft;
        private readonly Dictionary<string, Vector2> _cachedInputSockets = new();
        private readonly Dictionary<string, Vector2> _cachedTriggerOutSockets = new();
        private readonly Dictionary<string, Vector2> _cachedInteractableOutSockets = new();
        private readonly Dictionary<string, Vector2> _cachedActorInSockets = new();
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

            if (_selectedTab == 1)
            {
                DrawNodesAndDialoguesTab();
            }
            else
            {
                _scroll = GUILayout.BeginScrollView(_scroll);
                DrawSceneAndDiskTab();
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

        private void DrawNodesAndDialoguesTab()
        {
            bool hasGlobalsOnly = _data.Nodes.Count == 0
                && ((_data.Triggers != null && _data.Triggers.Count > 0)
                    || (_data.Interactables != null && _data.Interactables.Count > 0)
                    || (_data.Actors != null && _data.Actors.Count > 0));
            if (_data.Nodes.Count == 0 && !hasGlobalsOnly)
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
            int trigCount = _data.Triggers != null ? _data.Triggers.Count : 0;
            int interCount = _data.Interactables != null ? _data.Interactables.Count : 0;
            int actorCount = _data.Actors != null ? _data.Actors.Count : 0;
            GUILayout.Label($"<b>■ SCÈNE {sceneNumPrefix}: {_data.Title}</b> (<color=#00E5FF>{_data.Nodes.Count} Nœuds</color> + <color=#FFD75E>{trigCount} ▼</color> + <color=#33E6CC>{interCount} 🔧</color> + <color=#7CB3FF>{actorCount} 👥</color>)", GUILayout.ExpandWidth(true));

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
            GUI.backgroundColor = new Color(0.2f, 0.9f, 0.85f);
            if (GUILayout.Button("+ Interactable", GUILayout.Width(110), GUILayout.Height(22)))
            {
                if (_data.Interactables == null) _data.Interactables = new System.Collections.Generic.List<SceneInteractableSpawnData>();
                Vector2 viewCenter = new Vector2(_lastCanvasSize.x * 0.5f, _lastCanvasSize.y * 0.5f);
                Vector2 worldCenter = (viewCenter - _graphPan) / Mathf.Max(0.01f, _zoom);
                _data.Interactables.Add(new SceneInteractableSpawnData
                {
                    InteractableId = $"obj_{_data.Interactables.Count + 1}",
                    DisplayName = "Nouvel interactable",
                    ActionLabel = "Interagir",
                    Q = 0,
                    R = 0,
                    GraphPosX = worldCenter.x - 170f,
                    GraphPosY = worldCenter.y + 60f
                });
            }
            GUI.backgroundColor = new Color(0.35f, 0.6f, 1f);
            if (GUILayout.Button("+ Acteur", GUILayout.Width(90), GUILayout.Height(22)))
            {
                if (_data.Actors == null) _data.Actors = new System.Collections.Generic.List<SceneActorSpawnData>();
                Vector2 viewCenter = new Vector2(_lastCanvasSize.x * 0.5f, _lastCanvasSize.y * 0.5f);
                Vector2 worldCenter = (viewCenter - _graphPan) / Mathf.Max(0.01f, _zoom);
                _data.Actors.Add(new SceneActorSpawnData
                {
                    ActorId = $"actor_{_data.Actors.Count + 1}",
                    DisplayName = "Nouvel Acteur",
                    Q = 0,
                    R = 0,
                    GraphPosX = worldCenter.x - 170f,
                    GraphPosY = worldCenter.y + 220f
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
            GUILayout.Label("<color=grey>Molette = Zoom  ·  Clic Droit = Pan  ·  Double-clic nœud = cadrer  ·  Drag port = lier (vide = effacer)  ·  Tag bord = suivre</color>");
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
                bool hasContent = _data.Nodes.Count > 0
                    || (_data.Triggers != null && _data.Triggers.Count > 0)
                    || (_data.Interactables != null && _data.Interactables.Count > 0)
                    || (_data.Actors != null && _data.Actors.Count > 0);
                if (hasContent)
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
                    bool hasNodes = _data.Nodes.Count > 0;
                    if (!hasNodes && (_data.Triggers == null || _data.Triggers.Count == 0) && (_data.Interactables == null || _data.Interactables.Count == 0) && (_data.Actors == null || _data.Actors.Count == 0)) { /* rien à cadrer */ }
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
            if (data == null) return false;
            if (data.Nodes != null)
            {
                for (int i = 0; i < data.Nodes.Count; i++)
                {
                    var n = data.Nodes[i];
                    if (n == null) continue;
                    if (n.GraphPosX <= 0f && n.GraphPosY <= 0f) return true;
                }
            }
            if (data.Triggers != null)
            {
                for (int t = 0; t < data.Triggers.Count; t++)
                {
                    var trg = data.Triggers[t];
                    if (trg == null) continue;
                    if (trg.GraphPosX <= 0f && trg.GraphPosY <= 0f) return true;
                }
            }
            // Interactables historiques sans position graphe (0,0) → auto-dispose.
            if (data.Interactables != null)
            {
                for (int i = 0; i < data.Interactables.Count; i++)
                {
                    var it = data.Interactables[i];
                    if (it == null) continue;
                    if (it.GraphPosX <= 0f && it.GraphPosY <= 0f) return true;
                }
            }
            // Acteurs historiques sans position graphe (0,0) → auto-dispose.
            if (data.Actors != null)
            {
                for (int i = 0; i < data.Actors.Count; i++)
                {
                    var a = data.Actors[i];
                    if (a == null) continue;
                    if (a.GraphPosX <= 0f && a.GraphPosY <= 0f) return true;
                }
            }
            return false;
        }

        private void LayoutCardsInsideNode(SceneNodeData node)
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
        private void ComputeFreeSpotForNewCard(SceneNodeData node, out float x, out float y)
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

        // ------------------------------------------------------------------
        // Sections repliables des cartes/frames — TOUT est éditable dans le graphe.
        // État purement éditeur (HashSet en mémoire) : RIEN n'est sérialisé en JSON.
        // Règle d'or : chaque bloc détail a sa mesure (lignes × 20px) utilisée à la
        // FOIS par les calculs de taille et par le dessin, dans le même ordre.
        // ------------------------------------------------------------------
        private readonly HashSet<string> _expandedSections = new();
        private static string SKey(object o, string tag)
        {
            return (o == null ? "null" : o.GetHashCode().ToString("X8")) + ":" + tag;
        }
        private static bool SectionOpen(HashSet<string> expanded, object o, string tag)
        {
            return expanded != null && o != null && expanded.Contains(SKey(o, tag));
        }
        // Bouton ▸/▾ qui bascule la section. Retourne l'état après clic.
        private bool FoldoutButton(Rect worldRect, object o, string tag, string label)
        {
            string key = SKey(o, tag);
            bool open = _expandedSections.Contains(key);
            if (GraphButton(worldRect, (open ? "▾ " : "▸ ") + label))
            {
                if (open) _expandedSections.Remove(key);
                else _expandedSections.Add(key);
                open = !open;
            }
            return open;
        }

        // --- Listes d'effets en GUI absolu (lignes de 20px) ---
        private static int MeasureEffectsBlock(System.Collections.Generic.List<ScenarioEffect> list, bool open, bool alwaysHeader)
        {
            int n = list != null ? list.Count : 0;
            if (!alwaysHeader && !open) return 0;
            return 1 + (open ? n : 0);
        }
        // En-tête (toujours si alwaysHeader, sinon seulement si ouvert) + lignes si ouvert.
        private void DrawEffectsBlock(ref float y, float x, float w, System.Collections.Generic.List<ScenarioEffect> list, object owner, string tag, string label, bool alwaysHeader)
        {
            if (list == null) return;
            bool open = SectionOpen(_expandedSections, owner, tag);
            if (alwaysHeader || open)
            {
                FoldoutButton(new Rect(x, y, Mathf.Max(60f, w - 70f), 20f), owner, tag, $"{label} ({list.Count})");
                if (GraphButton(new Rect(x + w - 66f, y, 66f, 20f), "+ Effet"))
                    list.Add(new ScenarioEffect { Type = ScenarioEffectType.AddFlag, Key = "nouveau_flag" });
                y += 20f;
                open = SectionOpen(_expandedSections, owner, tag);
            }
            if (!open) return;
            for (int i = 0; i < list.Count; i++)
            {
                var eff = list[i];
                if (eff == null) { list.RemoveAt(i); i--; continue; }
                float ex = x;
                eff.Type = (ScenarioEffectType)GraphToolbar(new Rect(ex, y, 128f, 18f), (int)eff.Type, new[] { "Route", "Flag", "Int", "Journal" });
                ex += 130f;
                float tailW = 22f;
                switch (eff.Type)
                {
                    case ScenarioEffectType.SetRoute:
                        eff.Route = (StoryRoute)GraphToolbar(new Rect(ex, y, Mathf.Max(40f, x + w - ex - tailW), 18f), (int)eff.Route, new[] { "None", "Vardis", "Indep", "Imper" });
                        break;
                    case ScenarioEffectType.AddFlag:
                        GraphLabel(new Rect(ex, y, 34f, 18f), "Clé:");
                        ex += 36f;
                        eff.Key = GraphTextField(new Rect(ex, y, Mathf.Max(30f, x + w - ex - tailW), 18f), eff.Key ?? "");
                        break;
                    case ScenarioEffectType.AddInteger:
                        GraphLabel(new Rect(ex, y, 32f, 18f), "Clé:");
                        ex += 34f;
                        eff.Key = GraphTextField(new Rect(ex, y, 62f, 18f), eff.Key ?? "");
                        ex += 64f;
                        GraphLabel(new Rect(ex, y, 30f, 18f), "Qté:");
                        ex += 32f;
                        string amtTxt = GraphTextField(new Rect(ex, y, Mathf.Max(28f, x + w - ex - tailW), 18f), eff.Amount.ToString());
                        int.TryParse(amtTxt, out eff.Amount);
                        break;
                    case ScenarioEffectType.AddJournal:
                        GraphLabel(new Rect(ex, y, 32f, 18f), "Txt:");
                        ex += 34f;
                        eff.Text = GraphTextField(new Rect(ex, y, Mathf.Max(30f, x + w - ex - tailW), 18f), eff.Text ?? "");
                        break;
                }
                if (GraphButton(new Rect(x + w - 20f, y, 20f, 18f), "✕"))
                {
                    list.RemoveAt(i);
                    break;
                }
                y += 20f;
            }
        }

        // --- Prérequis en GUI absolu : 1 (toggle+type) + 0/1 (kind) + 0/1 (acteur) ---
        private static int MeasurePrereqDetail(ScenePrerequisiteData pre, bool open)
        {
            if (!open || pre == null) return 0;
            if (!pre.HasPrerequisite) return 1;
            return 3 + (pre.Kind == PrerequisiteKind.None ? 0 : 1);
        }
        private void DrawPrereqDetail(ref float y, float x, float w, ScenePrerequisiteData pre)
        {
            if (pre == null) return;
            float px = x;
            pre.HasPrerequisite = GraphToggle(new Rect(px, y, 62f, 20f), pre.HasPrerequisite, "🔒");
            px += 64f;
            GraphLabel(new Rect(px, y, 42f, 20f), "Type:");
            px += 44f;
            pre.Kind = (PrerequisiteKind)GraphToolbar(new Rect(px, y, Mathf.Max(60f, x + w - px), 20f), (int)pre.Kind, new[] { "None", "Compét.", "Spécia.", "Carac", "Flag", "Item", "Route" });
            y += 20f;
            if (!pre.HasPrerequisite) return;
            if (pre.Kind != PrerequisiteKind.None)
            {
                float qx = x + 12f;
                float qw = w - 12f;
                switch (pre.Kind)
                {
                    case PrerequisiteKind.SkillTraining:
                    {
                        int skillCount = Enum.GetValues(typeof(SkillType)).Length;
                        if (GraphButton(new Rect(qx, y, 18f, 18f), "◀"))
                            pre.RequiredSkill = (SkillType)(((int)pre.RequiredSkill - 1 + skillCount) % skillCount);
                        qx += 20f;
                        GraphLabel(new Rect(qx, y, 108f, 18f), $"<b>{SkillDefinitions.GetDisplayName(pre.RequiredSkill)}</b>");
                        qx += 110f;
                        if (GraphButton(new Rect(qx, y, 18f, 18f), "▶"))
                            pre.RequiredSkill = (SkillType)(((int)pre.RequiredSkill + 1) % skillCount);
                        qx += 20f;
                        GraphLabel(new Rect(qx, y, 52f, 18f), "Palier:");
                        qx += 54f;
                        string palTxt = GraphTextField(new Rect(qx, y, Mathf.Max(28f, x + w - qx), 18f), pre.MinTrainingLevel.ToString());
                        int.TryParse(palTxt, out pre.MinTrainingLevel);
                        break;
                    }
                    case PrerequisiteKind.Specialization:
                        GraphLabel(new Rect(qx, y, 62f, 18f), "Spécia.:");
                        pre.SpecializationName = GraphTextField(new Rect(qx + 64f, y, Mathf.Max(40f, x + w - qx - 64f), 18f), pre.SpecializationName ?? "");
                        break;
                    case PrerequisiteKind.AttributeThreshold:
                        GraphLabel(new Rect(qx, y, 52f, 18f), "Attribut:");
                        pre.AttributeName = GraphTextField(new Rect(qx + 54f, y, 76f, 18f), pre.AttributeName ?? "");
                        GraphLabel(new Rect(qx + 132f, y, 40f, 18f), "Min:");
                        string attrTxt = GraphTextField(new Rect(qx + 174f, y, Mathf.Max(28f, x + w - qx - 174f), 18f), pre.MinAttributeValue.ToString());
                        int.TryParse(attrTxt, out pre.MinAttributeValue);
                        break;
                    case PrerequisiteKind.CampaignFlag:
                        GraphLabel(new Rect(qx, y, 36f, 18f), "Clé:");
                        pre.FlagKey = GraphTextField(new Rect(qx + 38f, y, 104f, 18f), pre.FlagKey ?? "");
                        pre.FlagMustBeSet = GraphToggle(new Rect(qx + 144f, y, Mathf.Max(60f, x + w - qx - 144f), 18f), pre.FlagMustBeSet, "Posé");
                        break;
                    case PrerequisiteKind.ItemInInventory:
                        GraphLabel(new Rect(qx, y, 52f, 18f), "Objet:");
                        pre.ItemName = GraphTextField(new Rect(qx + 54f, y, Mathf.Max(40f, x + w - qx - 54f), 18f), pre.ItemName ?? "");
                        break;
                    case PrerequisiteKind.StoryRouteRequired:
                        GraphLabel(new Rect(qx, y, 52f, 18f), "Route:");
                        pre.RequiredRoute = (StoryRoute)GraphToolbar(new Rect(qx + 54f, y, Mathf.Max(60f, x + w - qx - 54f), 18f), (int)pre.RequiredRoute, new[] { "None", "Vardis", "Indep", "Imper" });
                        break;
                }
                y += 20f;
            }
            GraphLabel(new Rect(x + 12f, y, 62f, 18f), "Acteur:");
            pre.SpecificActorId = GraphTextField(new Rect(x + 76f, y, Mathf.Max(40f, x + w - x - 76f), 18f), pre.SpecificActorId ?? "");
            y += 20f;
        }

        // --- Ambiance en GUI absolu : 2 + 0/1 (musique) ---
        private static int MeasureAmbienceDetail(SceneAmbienceData amb, bool open)
        {
            if (!open || amb == null) return 0;
            return 2 + (amb.ChangeMusic ? 1 : 0);
        }
        private void DrawAmbienceDetail(ref float y, float x, float w, SceneAmbienceData amb)
        {
            if (amb == null) return;
            GraphLabel(new Rect(x, y, 40f, 18f), "Cue:");
            amb.SoundCueId = GraphTextField(new Rect(x + 42f, y, 92f, 18f), amb.SoundCueId ?? "");
            GraphLabel(new Rect(x + 136f, y, 36f, 18f), "Sec:");
            string shakeTxt = GraphTextField(new Rect(x + 174f, y, 34f, 18f), amb.CameraShakeIntensity.ToString("0.0"));
            float.TryParse(shakeTxt, out amb.CameraShakeIntensity);
            amb.TriggerAlarm = GraphToggle(new Rect(x + 210f, y, Mathf.Max(60f, x + w - x - 210f), 18f), amb.TriggerAlarm, "Alarme");
            y += 20f;
            amb.ChangeMusic = GraphToggle(new Rect(x, y, 110f, 20f), amb.ChangeMusic, "Musique");
            if (amb.ChangeMusic)
            {
                amb.MusicMood = (Killtime.Audio.MusicMood)GraphToolbar(new Rect(x + 112f, y, Mathf.Max(60f, x + w - x - 112f), 20f), (int)amb.MusicMood, new[] { "Explore", "Combat", "Stealth", "Mystery" });
                y += 20f;
                GraphLabel(new Rect(x + 12f, y, 70f, 18f), "Intensité:");
                amb.MusicIntensity = (Killtime.Audio.MusicIntensity)GraphToolbar(new Rect(x + 84f, y, Mathf.Max(60f, x + w - x - 84f), 18f), (int)amb.MusicIntensity, new[] { "Calm", "Low", "Mid", "High" });
                y += 20f;
            }
            else y += 20f;
        }

        // --- Récompenses en GUI absolu : 2 + M (items suppl.) ; picker si supporté ---
        private static int MeasureRewardsBlock(SceneDialogueRewardData rew)
        {
            if (rew == null) return 0;
            int m = rew.AdditionalItems != null ? rew.AdditionalItems.Count : 0;
            return 2 + m;
        }
        // picker: null = sans loupe ; sinon OpenItemPicker est appelé (challenge).
        private void DrawRewardsBlock(ref float y, float x, float w, SceneDialogueRewardData rew, SceneDialogueChoiceData pickerChoice)
        {
            if (rew == null) return;
            StorySceneData.EnsureRewardDefaults(rew);
            GraphLabel(new Rect(x, y, 36f, 18f), "+XP");
            string xpTxt = GraphTextField(new Rect(x + 38f, y, 34f, 18f), rew.EarnXP.ToString());
            int.TryParse(xpTxt, out rew.EarnXP);
            GraphLabel(new Rect(x + 74f, y, 34f, 18f), "+CE");
            string ceTxt = GraphTextField(new Rect(x + 108f, y, 36f, 18f), rew.EarnCredits.ToString());
            int.TryParse(ceTxt, out rew.EarnCredits);
            GraphLabel(new Rect(x + 146f, y, 34f, 18f), "Obj:");
            rew.CompletionObjectiveId = GraphTextField(new Rect(x + 182f, y, Mathf.Max(40f, x + w - x - 182f), 18f), rew.CompletionObjectiveId ?? "");
            y += 20f;
            float ix = x;
            GraphLabel(new Rect(ix, y, 44f, 18f), "Item:");
            ix += 46f;
            bool hasPicker = pickerChoice != null;
            float tailW = (hasPicker ? 26f : 0f) + 16f + 32f + 2f + 24f;
            rew.ItemRewardName = GraphTextField(new Rect(ix, y, Mathf.Max(40f, x + w - ix - tailW), 18f), rew.ItemRewardName ?? "");
            ix = x + w - tailW;
            if (hasPicker)
            {
                if (GraphButton(new Rect(ix, y, 24f, 18f), "🔍")) OpenItemPicker(pickerChoice);
                ix += 26f;
            }
            GraphLabel(new Rect(ix, y, 14f, 18f), "x");
            ix += 16f;
            string qTxt = GraphTextField(new Rect(ix, y, 30f, 18f), rew.ItemRewardQuantity.ToString());
            int.TryParse(qTxt, out rew.ItemRewardQuantity);
            if (rew.ItemRewardQuantity < 1) rew.ItemRewardQuantity = 1;
            ix += 32f;
            if (GraphButton(new Rect(ix, y, 22f, 18f), "+"))
                rew.AdditionalItems.Add(new SceneItemRewardEntry { ItemName = "", Quantity = 1 });
            y += 20f;
            for (int ri = 0; ri < rew.AdditionalItems.Count; ri++)
            {
                var entry = rew.AdditionalItems[ri];
                if (entry == null) { rew.AdditionalItems.RemoveAt(ri); ri--; continue; }
                float jx = x + 12f;
                GraphLabel(new Rect(jx, y, 30f, 18f), $"#{ri + 2}");
                jx += 32f;
                float jtail = (hasPicker ? 26f : 0f) + 16f + 34f + 2f + 22f;
                entry.ItemName = GraphTextField(new Rect(jx, y, Mathf.Max(30f, x + w - jx - jtail), 18f), entry.ItemName ?? "");
                jx = x + w - jtail;
                if (hasPicker)
                {
                    if (GraphButton(new Rect(jx, y, 24f, 18f), "🔍")) OpenItemPicker(pickerChoice, ri);
                    jx += 26f;
                }
                GraphLabel(new Rect(jx, y, 14f, 18f), "x");
                jx += 16f;
                string eqTxt = GraphTextField(new Rect(jx, y, 32f, 18f), entry.Quantity.ToString());
                int.TryParse(eqTxt, out entry.Quantity);
                if (entry.Quantity < 1) entry.Quantity = 1;
                jx += 34f;
                GUI.backgroundColor = new Color(1f, 0.3f, 0.3f);
                if (GraphButton(new Rect(jx, y, 20f, 18f), "✕"))
                {
                    rew.AdditionalItems.RemoveAt(ri);
                    GUI.backgroundColor = Color.white;
                    break;
                }
                GUI.backgroundColor = Color.white;
                y += 20f;
            }
        }

        // --- Test auto en GUI absolu : 1 + (11+M+S+F si actif) ---
        private static int MeasureAutoDetail(SceneAutoSkillCheckData auto, bool open)
        {
            if (!open || auto == null) return 0;
            if (!auto.HasAutoCheck) return 1;
            int m = auto.SuccessRewards?.AdditionalItems != null ? auto.SuccessRewards.AdditionalItems.Count : 0;
            int s = auto.SuccessEffects != null ? auto.SuccessEffects.Count : 0;
            int f = auto.FailureEffects != null ? auto.FailureEffects.Count : 0;
            return 11 + m + s + f;
        }
        private void DrawAutoDetail(ref float y, float x, float w, SceneAutoSkillCheckData auto)
        {
            if (auto == null) return;
            auto.HasAutoCheck = GraphToggle(new Rect(x, y, Mathf.Max(120f, w), 20f), auto.HasAutoCheck, "🎲 Test auto");
            y += 20f;
            if (!auto.HasAutoCheck) return;
            auto.SuccessRewards ??= new SceneDialogueRewardData();
            StorySceneData.EnsureRewardDefaults(auto.SuccessRewards);
            auto.SuccessEffects ??= new System.Collections.Generic.List<ScenarioEffect>();
            auto.FailureEffects ??= new System.Collections.Generic.List<ScenarioEffect>();
            int skillCount = Enum.GetValues(typeof(SkillType)).Length;
            float sx = x + 12f;
            if (GraphButton(new Rect(sx, y, 18f, 18f), "◀"))
                auto.RequiredSkill = (SkillType)(((int)auto.RequiredSkill - 1 + skillCount) % skillCount);
            sx += 20f;
            GraphLabel(new Rect(sx, y, 96f, 18f), $"<b>{SkillDefinitions.GetDisplayName(auto.RequiredSkill)}</b>");
            sx += 98f;
            if (GraphButton(new Rect(sx, y, 18f, 18f), "▶"))
                auto.RequiredSkill = (SkillType)(((int)auto.RequiredSkill + 1) % skillCount);
            sx += 20f;
            GraphLabel(new Rect(sx, y, 26f, 18f), "SD:");
            sx += 28f;
            string sdTxt = GraphTextField(new Rect(sx, y, 30f, 18f), auto.TargetDC.ToString());
            int.TryParse(sdTxt, out auto.TargetDC);
            sx += 32f;
            GraphLabel(new Rect(sx, y, 32f, 18f), "Act:");
            sx += 34f;
            auto.SpecificActorId = GraphTextField(new Rect(sx, y, Mathf.Max(30f, x + w - sx), 18f), auto.SpecificActorId ?? "");
            y += 20f;
            GUI.color = new Color(0.35f, 1f, 0.55f);
            GraphLabel(new Rect(x + 12f, y, 20f, 18f), "✔");
            GUI.color = Color.white;
            auto.SuccessSpeech = GraphTextField(new Rect(x + 34f, y, Mathf.Max(40f, x + w - x - 34f), 18f), auto.SuccessSpeech ?? "");
            y += 20f;
            GraphLabel(new Rect(x + 34f, y, 42f, 18f), "✔ →");
            auto.SuccessNextLineId = GraphTextField(new Rect(x + 78f, y, Mathf.Max(40f, x + w - x - 78f), 18f), auto.SuccessNextLineId ?? "");
            y += 20f;
            DrawRewardsBlock(ref y, x + 12f, w - 12f, auto.SuccessRewards, null);
            DrawEffectsBlock(ref y, x + 12f, w - 12f, auto.SuccessEffects, auto, "sfx", "fx ✔", true);
            GUI.color = new Color(1f, 0.45f, 0.4f);
            GraphLabel(new Rect(x + 12f, y, 20f, 18f), "✕");
            GUI.color = Color.white;
            auto.FailureSpeech = GraphTextField(new Rect(x + 34f, y, Mathf.Max(40f, x + w - x - 34f), 18f), auto.FailureSpeech ?? "");
            y += 20f;
            GraphLabel(new Rect(x + 34f, y, 42f, 18f), "✕ →");
            auto.FailureNextLineId = GraphTextField(new Rect(x + 78f, y, Mathf.Max(40f, x + w - x - 78f), 18f), auto.FailureNextLineId ?? "");
            y += 20f;
            DrawEffectsBlock(ref y, x + 12f, w - 12f, auto.FailureEffects, auto, "ffx", "fx ✕", true);
        }

        // --- Caméra réplique : 2 lignes ---
        private void DrawCameraDetail(ref float y, float x, float w, SceneDialogueLineData line)
        {
            GraphLabel(new Rect(x, y, 52f, 18f), "Focus:");
            line.CameraFocusActorId = GraphTextField(new Rect(x + 54f, y, Mathf.Max(40f, x + w - x - 54f), 18f), line.CameraFocusActorId ?? "");
            y += 20f;
            GraphLabel(new Rect(x, y, 46f, 18f), "Pitch:");
            string pTxt = GraphTextField(new Rect(x + 48f, y, 40f, 18f), line.CameraPitch.ToString("0.0"));
            float.TryParse(pTxt, out line.CameraPitch);
            GraphLabel(new Rect(x + 92f, y, 40f, 18f), "Dist:");
            string dTxt = GraphTextField(new Rect(x + 134f, y, Mathf.Max(30f, x + w - x - 134f), 18f), line.CameraDistance.ToString("0.0"));
            float.TryParse(dTxt, out line.CameraDistance);
            y += 20f;
        }

        // --- Détail défi (carte) : récompenses + effets + combat : 5+M+S+F ---
        private static int MeasureChallengeDetail(SceneDialogueChallengeData chal, bool open)
        {
            if (!open || chal == null || !chal.HasChallenge) return 0;
            int m = chal.SuccessRewards?.AdditionalItems != null ? chal.SuccessRewards.AdditionalItems.Count : 0;
            int s = chal.SuccessEffects != null ? chal.SuccessEffects.Count : 0;
            int f = chal.FailureEffects != null ? chal.FailureEffects.Count : 0;
            return 5 + m + s + f;
        }
        private void DrawChallengeDetail(ref float y, float x, float w, SceneDialogueChoiceData choice)
        {
            var chal = choice != null ? choice.Challenge : null;
            if (chal == null || !chal.HasChallenge) return;
            chal.SuccessRewards ??= new SceneDialogueRewardData();
            StorySceneData.EnsureRewardDefaults(chal.SuccessRewards);
            chal.SuccessEffects ??= new System.Collections.Generic.List<ScenarioEffect>();
            chal.FailureEffects ??= new System.Collections.Generic.List<ScenarioEffect>();
            DrawRewardsBlock(ref y, x, w, chal.SuccessRewards, choice);
            DrawEffectsBlock(ref y, x, w, chal.SuccessEffects, chal, "sfx", "fx ✔", true);
            DrawEffectsBlock(ref y, x, w, chal.FailureEffects, chal, "ffx", "fx ✕", true);
            chal.FailureTriggersCombat = GraphToggle(new Rect(x, y, Mathf.Max(150f, w), 20f), chal.FailureTriggersCombat, "✕→Combat immédiat");
            y += 20f;
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
            if (_data.Interactables != null)
            {
                for (int i = 0; i < _data.Interactables.Count; i++)
                {
                    var it = _data.Interactables[i];
                    if (it == null) continue;
                    Vector2 s = GetInteractableCardSize(it);
                    if (new Rect(it.GraphPosX, it.GraphPosY, s.x, s.y).Contains(mouseWorld)) return true;
                }
            }
            if (_data.Actors != null)
            {
                for (int i = 0; i < _data.Actors.Count; i++)
                {
                    var a = _data.Actors[i];
                    if (a == null) continue;
                    Vector2 s = GetActorCardSize(a);
                    if (new Rect(a.GraphPosX, a.GraphPosY, s.x, s.y).Contains(mouseWorld)) return true;
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
            bool hasNodes = _data.Nodes.Count > 0;
            bool hasGlobals = (_data.Triggers != null && _data.Triggers.Count > 0)
                || (_data.Interactables != null && _data.Interactables.Count > 0)
                || (_data.Actors != null && _data.Actors.Count > 0);
            if (!hasNodes && !hasGlobals) return;

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

            float maxSceneH = 0f;
            if (hasNodes)
            {
                // Placement structuré en flux 2D compact
                var rootNode = _data.Nodes[0];
                rootNode.GraphPosX = startX;
                rootNode.GraphPosY = startY;
                LayoutCardsInsideNode(rootNode);
                Rect rootBox = ComputeNodeBoundingBox(rootNode);

                float col2X = rootBox.xMax + gapX;
                float col2CurY = startY;
                maxSceneH = rootBox.height;

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
                // Les cartes Interactables vivent sur la rangée sous les déclencheurs.
                if (_data.Interactables != null && _data.Interactables.Count > 0)
                {
                    GetSceneContentBounds(out float _, out float _, out float _, out float trigMaxY);
                    float interX = startX;
                    float interY = trigMaxY + 30f;
                    for (int i = 0; i < _data.Interactables.Count; i++)
                    {
                        var it = _data.Interactables[i];
                        if (it == null) continue;
                        it.GraphPosX = interX;
                        it.GraphPosY = interY;
                        Vector2 s = GetInteractableCardSize(it);
                        interX += s.x + 20f;
                    }
                }
            }
            else if (_data.Interactables != null && _data.Interactables.Count > 0)
            {
                float interX = startX;
                float interY = startY + maxSceneH + 36f;
                for (int i = 0; i < _data.Interactables.Count; i++)
                {
                    var it = _data.Interactables[i];
                    if (it == null) continue;
                    it.GraphPosX = interX;
                    it.GraphPosY = interY;
                    Vector2 s = GetInteractableCardSize(it);
                    interX += s.x + 20f;
                }
            }
            // Les cartes Acteurs vivent sur la rangée sous les interactables.
            if (_data.Actors != null && _data.Actors.Count > 0)
            {
                float actorY = startY + maxSceneH + 36f;
                if ((_data.Triggers != null && _data.Triggers.Count > 0) || (_data.Interactables != null && _data.Interactables.Count > 0))
                {
                    GetSceneContentBounds(out float _, out float _, out float _, out float placedMaxY);
                    actorY = placedMaxY + 30f;
                }
                float actorX = startX;
                for (int i = 0; i < _data.Actors.Count; i++)
                {
                    var a = _data.Actors[i];
                    if (a == null) continue;
                    a.GraphPosX = actorX;
                    a.GraphPosY = actorY;
                    Vector2 s = GetActorCardSize(a);
                    actorX += s.x + 20f;
                }
            }

            _focusedNodeIndex = 0;
            FocusAllNodes(canvasRect);
        }

        private void AutoLayoutMosaic(Rect? canvasRect = null)
        {
            bool hasNodes = _data.Nodes.Count > 0;
            bool hasGlobals = (_data.Triggers != null && _data.Triggers.Count > 0)
                || (_data.Interactables != null && _data.Interactables.Count > 0)
                || (_data.Actors != null && _data.Actors.Count > 0);
            if (!hasNodes && !hasGlobals) return;
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
            if (_data.Interactables != null && _data.Interactables.Count > 0)
            {
                GetSceneContentBounds(out float _, out float _, out float _, out float maxY2);
                float interX = startX;
                float interY = maxY2 + 50f;
                for (int i = 0; i < _data.Interactables.Count; i++)
                {
                    var it = _data.Interactables[i];
                    if (it == null) continue;
                    it.GraphPosX = interX;
                    it.GraphPosY = interY;
                    Vector2 s = GetInteractableCardSize(it);
                    interX += s.x + 30f;
                }
            }
            if (_data.Actors != null && _data.Actors.Count > 0)
            {
                GetSceneContentBounds(out float _, out float _, out float _, out float maxY3);
                float actorX = startX;
                float actorY = maxY3 + 50f;
                for (int i = 0; i < _data.Actors.Count; i++)
                {
                    var a = _data.Actors[i];
                    if (a == null) continue;
                    a.GraphPosX = actorX;
                    a.GraphPosY = actorY;
                    Vector2 s = GetActorCardSize(a);
                    actorX += s.x + 30f;
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
        private const float InterCardDefaultW = 340f;
        private const float InterCardDefaultH = 252f;
        private const float ActorCardDefaultW = 340f;
        private const float ActorCardDefaultH = 208f;
        private static float DlgCardDefaultH(int choiceCount) => 215f + choiceCount * 28f;

        // Nombre de lignes de config d'un défi (sans les lignes-liens).
        private static int GetChallengeConfigRows(SceneDialogueChallengeData ch)
        {
            if (ch == null || !ch.HasChallenge) return 0;
            return ch.IsOpposed ? 2 : 1;
        }

        // Toutes les lignes de détail sous la rangée principale d'un choix :
        // config défi + liens ✔/✕, méta (prérequis/effets), détails repliables
        // (prérequis / effets / défi : préfixés APRÈS, les offsets de fils restent justes).
        // Doit correspondre EXACTEMENT à l'ordre de dessin dans DrawDialogueCard.
        private int GetChoiceDetailRows(SceneDialogueChoiceData ch)
        {
            if (ch == null) return 0;
            int rows = 0;
            var chal = ch.Challenge;
            if (chal != null && chal.HasChallenge)
                rows += GetChallengeConfigRows(chal) + 2; // config + liens ✔/✕
            bool hasPre = ch.Prerequisite != null && ch.Prerequisite.HasPrerequisite;
            bool hasFx = ch.Effects != null && ch.Effects.Count > 0;
            if (hasPre || hasFx) rows += 1; // méta 🔒/fx
            rows += MeasurePrereqDetail(ch.Prerequisite, SectionOpen(_expandedSections, ch, "pre"));
            rows += MeasureEffectsBlock(ch.Effects, SectionOpen(_expandedSections, ch, "fx"), false);
            if (chal != null)
                rows += MeasureChallengeDetail(chal, SectionOpen(_expandedSections, ch, "chd"));
            return rows;
        }

        // Y (relatif au haut de la carte) de la rangée principale de chaque choix.
        // Doit rester identique entre GetDialogueCardSize, DrawDialogueCard et DrawConnectedGraphWires.
        private void GetDialogueChoiceRowOffsets(SceneDialogueLineData line, List<float> outOffsets)
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

        private float GetDialogueCardNeededHeight(SceneDialogueLineData line)
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
            needed += 20f; // strip détails 🎥🔒🔊🎲
            if (line != null)
            {
                if (SectionOpen(_expandedSections, line, "cam")) needed += 40f;
                needed += MeasurePrereqDetail(line.Prerequisite, SectionOpen(_expandedSections, line, "pre")) * 20f;
                needed += MeasureAmbienceDetail(line.Ambience, SectionOpen(_expandedSections, line, "amb")) * 20f;
                needed += MeasureAutoDetail(line.AutoSkillCheck, SectionOpen(_expandedSections, line, "auto")) * 20f;
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

        private Vector2 GetDialogueCardSize(SceneDialogueLineData line)
        {
            if (line == null) return new Vector2(DlgCardDefaultW, DlgCardDefaultH(0));
            float w = line.CardWidth > 100f ? line.CardWidth : DlgCardDefaultW;
            float h = line.CardHeight > 80f ? line.CardHeight : DlgCardDefaultH(line.Choices != null ? line.Choices.Count : 0);
            // Hauteur minimale pour que le contenu GUI absolu ne déborde jamais de la carte.
            float needed = GetDialogueCardNeededHeight(line);
            if (needed > h) h = needed;
            return new Vector2(w, h);
        }

        private float GetEventCardNeededHeight(SceneEventData ev)
        {
            // ID 20 + Kind 22 + strip 20 + détails + Tester 20 + marges.
            float needed = 124f;
            if (ev != null)
            {
                needed += MeasurePrereqDetail(ev.Prerequisite, SectionOpen(_expandedSections, ev, "pre")) * 20f;
                needed += MeasureAmbienceDetail(ev.Ambience, SectionOpen(_expandedSections, ev, "amb")) * 20f;
                needed += MeasureAutoDetail(ev.AutoSkillCheck, SectionOpen(_expandedSections, ev, "auto")) * 20f;
                needed += MeasureEffectsBlock(ev.Effects, SectionOpen(_expandedSections, ev, "fx"), true) * 20f;
            }
            return needed;
        }

        private Vector2 GetEventCardSize(SceneEventData ev)
        {
            if (ev == null) return new Vector2(EvtCardDefaultW, EvtCardDefaultH);
            float w = ev.CardWidth > 100f ? ev.CardWidth : EvtCardDefaultW;
            float h = ev.CardHeight > 80f ? ev.CardHeight : EvtCardDefaultH;
            float needed = GetEventCardNeededHeight(ev);
            if (needed > h) h = needed;
            return new Vector2(w, h);
        }

        // Hauteur carte conséquence : 30 (haut) + 22 (ID/titre) + 20 (toggles)
        // + dialogue éventuel (22 locuteur + 44 speech) + bloc fx (1 + N si ouvert)
        // + 22 (liens) + 22 (+XP/+CE) + 20 × (1 + N items) + 10 (marge).
        private float GetConsequenceCardNeededHeight(SceneConsequenceData cons)
        {
            float needed = 30f + 22f + 20f + 22f + 22f + 20f + 10f;
            if (cons != null)
            {
                if (cons.HasDialogue) needed += 22f + 44f;
                int extras = cons.Rewards?.AdditionalItems != null ? cons.Rewards.AdditionalItems.Count : 0;
                needed += Mathf.Max(0, extras) * 20f;
            }
            needed += MeasureEffectsBlock(cons != null ? cons.Effects : null, SectionOpen(_expandedSections, cons, "fx"), true) * 20f;
            return needed;
        }

        private Vector2 GetConsequenceCardSize(SceneConsequenceData cons)
        {
            if (cons == null) return new Vector2(ConsCardDefaultW, ConsCardDefaultH);
            float w = cons.CardWidth > 100f ? cons.CardWidth : ConsCardDefaultW;
            float h = cons.CardHeight > 80f ? cons.CardHeight : ConsCardDefaultH;
            float needed = GetConsequenceCardNeededHeight(cons);
            if (needed > h) h = needed;
            return new Vector2(w, h);
        }

        // Offset Y (relatif carte) de la rangée →Diag/→Cons. Source unique pour
        // le dessin (DrawConsequenceCard) et les fils (DrawConnectedGraphWires).
        private float GetConsLinkRowTop(SceneConsequenceData cons)
        {
            float top = 72f + ((cons != null && cons.HasDialogue) ? 66f : 0f);
            top += MeasureEffectsBlock(cons != null ? cons.Effects : null, SectionOpen(_expandedSections, cons, "fx"), true) * 20f;
            return top;
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

        private static Vector2 GetInteractableCardSize(SceneInteractableSpawnData it)
        {
            if (it == null) return new Vector2(InterCardDefaultW, InterCardDefaultH);
            float w = it.CardWidth > 100f ? it.CardWidth : InterCardDefaultW;
            float h = it.CardHeight > 80f ? it.CardHeight : InterCardDefaultH;
            if (h < InterCardDefaultH) h = InterCardDefaultH;
            return new Vector2(w, h);
        }

        private static Vector2 GetActorCardSize(SceneActorSpawnData a)
        {
            if (a == null) return new Vector2(ActorCardDefaultW, ActorCardDefaultH);
            float w = a.CardWidth > 100f ? a.CardWidth : ActorCardDefaultW;
            float h = a.CardHeight > 80f ? a.CardHeight : ActorCardDefaultH;
            if (h < ActorCardDefaultH) h = ActorCardDefaultH;
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
            bool hasNodes = _data != null && _data.Nodes != null && _data.Nodes.Count > 0;
            bool hasTriggers = _data != null && _data.Triggers != null && _data.Triggers.Count > 0;
            bool hasInters = _data != null && _data.Interactables != null && _data.Interactables.Count > 0;
            bool hasActors = _data != null && _data.Actors != null && _data.Actors.Count > 0;
            if (!hasNodes && !hasTriggers && !hasInters && !hasActors) return false;
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
            if (_data.Interactables != null)
            {
                for (int i = 0; i < _data.Interactables.Count; i++)
                {
                    var it = _data.Interactables[i];
                    if (it == null) continue;
                    Vector2 isize = GetInteractableCardSize(it);
                    minX = Mathf.Min(minX, it.GraphPosX);
                    minY = Mathf.Min(minY, it.GraphPosY);
                    maxX = Mathf.Max(maxX, it.GraphPosX + isize.x);
                    maxY = Mathf.Max(maxY, it.GraphPosY + isize.y);
                }
            }
            if (_data.Actors != null)
            {
                for (int i = 0; i < _data.Actors.Count; i++)
                {
                    var a = _data.Actors[i];
                    if (a == null) continue;
                    Vector2 asize = GetActorCardSize(a);
                    minX = Mathf.Min(minX, a.GraphPosX);
                    minY = Mathf.Min(minY, a.GraphPosY);
                    maxX = Mathf.Max(maxX, a.GraphPosX + asize.x);
                    maxY = Mathf.Max(maxY, a.GraphPosY + asize.y);
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

        private Rect ComputeNodeBoundingBox(SceneNodeData node)
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
                // Frame vide : header 38 + section détails (ex-inspecteur + ex-inline).
                float sh = GetNodeSectionHeight(node);
                return new Rect(nx, ny, w, Mathf.Max(120f, 38f + 10f + sh + 14f));
            }

            float pad = 14f;
            float topHeaderH = 34f;
            minX = Mathf.Min(nx, minX);
            minY = Mathf.Min(ny + topHeaderH, minY);
            Rect r = new Rect(minX - pad, minY - topHeaderH - pad, (maxX - minX) + pad * 2f, (maxY - minY) + topHeaderH + pad * 2f);
            if (r.width < 560f) r.width = 560f;
            // Section détails du nœud sous les cartes.
            r.height += 10f + GetNodeSectionHeight(node) + 14f;
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
            // Tags de bord AVANT toute interaction : un tag sous le curseur bloque le
            // pan (clic = cadrer la cible) et sert de cible de drop pendant un drag.
            CollectOffscreenLinkTags(canvasRect);
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
            bool mouseOverTag = false;
            for (int tg = 0; tg < _offscreenTags.Count; tg++)
            {
                if (_offscreenTags[tg].ScreenRect.Contains(evt.mousePosition)) { mouseOverTag = true; break; }
            }
            bool isOverInteractiveElement = (_draggingCardKey != null || _draggingNodeId != null || _resizingCardKey != null || _wireDraft.IsActive || mouseOverTag || IsMouseOverAnyNodeOrCard(mouseWorld));
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
            _cachedInteractableOutSockets.Clear();
            _cachedActorInSockets.Clear();

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

            if (_data.Interactables == null) _data.Interactables = new System.Collections.Generic.List<SceneInteractableSpawnData>();
            {
                var seenInterIds = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < _data.Interactables.Count; i++)
                {
                    var it = _data.Interactables[i];
                    if (it == null) continue;
                    it.GraphPosX = SanitizeCoord(it.GraphPosX, 60f + i * 380f);
                    it.GraphPosY = SanitizeCoord(it.GraphPosY, 1250f);
                    // Les anciennes scènes ont souvent GraphPos = 0,0 → on les décale
                    // en cascade pour éviter l'empilement avant le prochain Auto-Layout.
                    if (it.GraphPosX <= 1f && it.GraphPosY <= 1f)
                    {
                        it.GraphPosX = 60f + i * 380f;
                        it.GraphPosY = 1250f;
                    }
                    if (string.IsNullOrEmpty(it.InteractableId)) it.InteractableId = $"obj_{i + 1}";
                    if (!seenInterIds.Add(it.InteractableId)) it.InteractableId = $"{it.InteractableId}_{i + 1}";
                    Vector2 isize = GetInteractableCardSize(it);
                    _cachedInteractableOutSockets[it.InteractableId] = new Vector2(it.GraphPosX + isize.x, it.GraphPosY + 26f);
                }
            }

            if (_data.Actors == null) _data.Actors = new System.Collections.Generic.List<SceneActorSpawnData>();
            {
                var seenActorIds = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < _data.Actors.Count; i++)
                {
                    var a = _data.Actors[i];
                    if (a == null) continue;
                    a.GraphPosX = SanitizeCoord(a.GraphPosX, 60f + i * 380f);
                    a.GraphPosY = SanitizeCoord(a.GraphPosY, 1600f);
                    // Anciennes scènes : GraphPos = 0,0 → cascade sous les interactables.
                    if (a.GraphPosX <= 1f && a.GraphPosY <= 1f)
                    {
                        a.GraphPosX = 60f + i * 380f;
                        a.GraphPosY = 1600f;
                    }
                    if (string.IsNullOrEmpty(a.ActorId)) a.ActorId = $"actor_{i + 1}";
                    if (!seenActorIds.Add(a.ActorId)) a.ActorId = $"{a.ActorId}_{i + 1}";
                    Vector2 asize = GetActorCardSize(a);
                    _cachedActorInSockets[a.ActorId] = new Vector2(a.GraphPosX, a.GraphPosY + 26f);
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

            if (_data.Interactables != null)
            {
                for (int i = 0; i < _data.Interactables.Count; i++)
                {
                    var it = _data.Interactables[i];
                    if (it == null) continue;
                    Vector2 isize = GetInteractableCardSize(it);
                    if (visibleWorld.Overlaps(new Rect(it.GraphPosX, it.GraphPosY, isize.x, isize.y)))
                        DrawInteractableCard(it, i, mouseWorld);
                }
            }

            if (_data.Actors != null)
            {
                for (int i = 0; i < _data.Actors.Count; i++)
                {
                    var a = _data.Actors[i];
                    if (a == null) continue;
                    Vector2 asize = GetActorCardSize(a);
                    if (visibleWorld.Overlaps(new Rect(a.GraphPosX, a.GraphPosY, asize.x, asize.y)))
                        DrawActorCard(a, i, mouseWorld);
                }
            }

            // Dropdown locuteurs par-dessus tout le graphe.
            DrawActorDropdownPopup();

            // Le seul clip du graphe est terminé ici, avant tous les overlays écran.
            GUI.EndGroup();

            DrawZoomControlsOverlay(canvasRect);
            DrawOffscreenLinkTags();

            if (_hasHoverCard && !IsItemPickerOpen() && Event.current.type == EventType.Repaint)
                DrawCardHoverTooltip(canvasRect);

            DrawItemPickerPanel(canvasRect);
        }

        // Moteur de recherche d'item du catalogue (récompenses des cartes).
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

        // --- Ports de sortie des frames de nœud : source unique de vérité partagée
        // entre le dessin (DrawNodeFrame) et les wires (DrawGlobalSceneWires).
        // - Sortie principale (NextNodeId) : flanc droit, yMin+18.
        // - Sorties choix majeurs : flanc droit, une par choix, espacées de 36px.
        private static Vector2 GetNodeMainOutCenter(Rect nodeBox)
        {
            return new Vector2(nodeBox.xMax, nodeBox.yMin + 18f);
        }

        private static Vector2 GetNodeChoiceOutCenter(Rect nodeBox, int choiceIndex)
        {
            return new Vector2(nodeBox.xMax, nodeBox.yMin + 60f + (choiceIndex * 36f) + 14f);
        }

        private static Rect GetNodeChoicePortRect(Rect nodeBox, int choiceIndex)
        {
            Vector2 c = GetNodeChoiceOutCenter(nodeBox, choiceIndex);
            return new Rect(c.x - 7f, c.y - 7f, 14f, 14f);
        }

        private static bool IsOverNodeChoicePort(Rect nodeBox, SceneNodeData node, Vector2 mouseWorld)
        {
            if (node == null || node.Choices == null) return false;
            for (int c = 0; c < node.Choices.Count; c++)
            {
                if (node.Choices[c] == null) continue;
                if (GetNodeChoicePortRect(nodeBox, c).Contains(mouseWorld)) return true;
            }
            return false;
        }

        private int FindNodeIndex(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId) || _data == null || _data.Nodes == null) return -1;
            return _data.Nodes.FindIndex(n => n != null && string.Equals(n.NodeId, nodeId, System.StringComparison.OrdinalIgnoreCase));
        }

        // --- Tags de bord "bords" : quand la cible d'un lien est hors-champ, un tag
        // cliquable "→ id" reste ancré au bord du viewport dans sa direction.
        // Clic = cadrer le nœud cible. Pendant un drag de wire, drop sur le tag = lier.
        // Pur état éditeur, calculé en coordonnées pures (aucun cache de sockets).
        private struct OffscreenLinkTag
        {
            public string TargetNodeId;
            public int TargetNodeIndex;
            public string Label;
            public Color Color;
            public Vector2 WorldPos;
            public Rect ScreenRect;
        }

        private readonly List<OffscreenLinkTag> _offscreenTags = new();

        private void CollectOffscreenLinkTags(Rect canvasRect)
        {
            _offscreenTags.Clear();
            if (_data == null || _data.Nodes == null || _pickerCapturesMouse) return;
            Rect view = GetVisibleWorldRect(canvasRect);
            float zoom = Mathf.Max(0.0001f, _zoom);
            float mX = 100f / zoom;
            float mY = 30f / zoom;

            void TryAddTag(Rect sourceRect, Vector2 targetCenter, string targetId, string label, Color col)
            {
                if (string.IsNullOrEmpty(targetId)) return;
                if (!sourceRect.Overlaps(view)) return;
                if (view.Contains(targetCenter)) return;
                int idx = FindNodeIndex(targetId);
                if (idx < 0) return;
                float cx = (view.xMin + mX < view.xMax - mX)
                    ? Mathf.Clamp(targetCenter.x, view.xMin + mX, view.xMax - mX)
                    : view.center.x;
                float cy = (view.yMin + mY < view.yMax - mY)
                    ? Mathf.Clamp(targetCenter.y, view.yMin + mY, view.yMax - mY)
                    : view.center.y;
                Vector2 worldPos = new Vector2(cx, cy);
                Vector2 screen = new Vector2(canvasRect.x, canvasRect.y) + GraphPoint(worldPos);
                const float tagW = 160f;
                const float tagH = 22f;
                float tx = Mathf.Clamp(screen.x - tagW * 0.5f, canvasRect.x + 6f, canvasRect.xMax - 6f - tagW);
                float ty = Mathf.Clamp(screen.y - tagH * 0.5f, canvasRect.y + 6f, canvasRect.yMax - 6f - tagH);
                _offscreenTags.Add(new OffscreenLinkTag
                {
                    TargetNodeId = targetId,
                    TargetNodeIndex = idx,
                    Label = label,
                    Color = col,
                    WorldPos = worldPos,
                    ScreenRect = new Rect(tx, ty, tagW, tagH)
                });
            }

            Vector2 NodeTargetCenter(string targetId, out bool found)
            {
                found = false;
                var target = _data.FindNode(targetId);
                if (target == null) return Vector2.zero;
                found = true;
                return ComputeNodeBoundingBox(target).center;
            }

            for (int n = 0; n < _data.Nodes.Count; n++)
            {
                var node = _data.Nodes[n];
                if (node == null) continue;
                Rect nodeBox = ComputeNodeBoundingBox(node);
                if (!string.IsNullOrEmpty(node.NextNodeId))
                {
                    Vector2 c = NodeTargetCenter(node.NextNodeId, out bool ok);
                    if (ok) TryAddTag(nodeBox, c, node.NextNodeId, $"■→ {node.NextNodeId}", new Color(1.0f, 0.6f, 0.1f));
                }
                if (node.Choices != null)
                {
                    for (int c = 0; c < node.Choices.Count; c++)
                    {
                        var ch = node.Choices[c];
                        if (ch == null || string.IsNullOrEmpty(ch.NextNodeId)) continue;
                        Vector2 cc = NodeTargetCenter(ch.NextNodeId, out bool ok);
                        if (!ok) continue;
                        Color wcol = ch.Id.Contains("hostile") ? new Color(1.0f, 0.35f, 0.35f) : new Color(0.2f, 0.92f, 0.45f);
                        TryAddTag(nodeBox, cc, ch.NextNodeId, $"◆→ {ch.NextNodeId}", wcol);
                    }
                }
            }

            if (_data.Triggers != null)
            {
                for (int t = 0; t < _data.Triggers.Count; t++)
                {
                    var trg = _data.Triggers[t];
                    if (trg == null || string.IsNullOrEmpty(trg.TargetNodeId)) continue;
                    Vector2 c = NodeTargetCenter(trg.TargetNodeId, out bool ok);
                    if (!ok) continue;
                    Vector2 ts = GetTriggerCardSize(trg);
                    TryAddTag(new Rect(trg.GraphPosX, trg.GraphPosY, ts.x, ts.y), c, trg.TargetNodeId, $"▼→ {trg.TargetNodeId}", new Color(1.0f, 0.85f, 0.25f));
                }
            }

            if (_data.Interactables != null)
            {
                for (int i = 0; i < _data.Interactables.Count; i++)
                {
                    var it = _data.Interactables[i];
                    if (it == null || string.IsNullOrEmpty(it.TriggerNodeId)) continue;
                    Vector2 c = NodeTargetCenter(it.TriggerNodeId, out bool ok);
                    if (!ok) continue;
                    Vector2 ins = GetInteractableCardSize(it);
                    TryAddTag(new Rect(it.GraphPosX, it.GraphPosY, ins.x, ins.y), c, it.TriggerNodeId, $"🔧→ {it.TriggerNodeId}", new Color(0.2f, 0.95f, 0.85f));
                }
            }
        }

        private void DrawOffscreenLinkTags()
        {
            if (_offscreenTags.Count == 0 || _pickerCapturesMouse) return;
            bool quiet = _wireDraft.IsActive;
            for (int i = 0; i < _offscreenTags.Count; i++)
            {
                var tag = _offscreenTags[i];
                DrawSolidRect(tag.ScreenRect, new Color(0.02f, 0.04f, 0.07f, 0.92f));
                DrawSolidRect(new Rect(tag.ScreenRect.x, tag.ScreenRect.y, tag.ScreenRect.width, 2f), tag.Color);
                if (quiet)
                {
                    GUI.Label(new Rect(tag.ScreenRect.x + 6f, tag.ScreenRect.y + 2f, tag.ScreenRect.width - 12f, tag.ScreenRect.height - 4f), $"<b>{tag.Label}</b>");
                }
                else if (GUI.Button(tag.ScreenRect, tag.Label))
                {
                    FocusNodeByIndex(tag.TargetNodeIndex);
                    Event.current.Use();
                    return;
                }
            }
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
                    DrawBezierWire(GetNodeMainOutCenter(nodeBox), targetNodeIn, new Color(1.0f, 0.6f, 0.1f, 0.95f), 4.5f);
                }

                for (int c = 0; c < node.Choices.Count; c++)
                {
                    var choice = node.Choices[c];
                    if (!string.IsNullOrEmpty(choice.NextNodeId)
                        && _cachedNodeInputSockets.TryGetValue(choice.NextNodeId, out var choiceNodeIn))
                    {
                        Vector2 choiceOut = GetNodeChoiceOutCenter(nodeBox, c);
                        Color choiceWireCol = choice.Id.Contains("hostile") ? new Color(1.0f, 0.35f, 0.35f, 0.95f) : new Color(0.2f, 0.92f, 0.45f, 0.95f);
                        DrawBezierWire(choiceOut, choiceNodeIn, choiceWireCol, 3.5f);
                    }
                }

                // Wires Nœud → Acteur (apparition différée : SpawnOnNodeId) : la source
                // est le flanc droit du nœud (yMin+34, sous le wire NextNodeId), la cible
                // le port d'entrée de la carte acteur. Couleur faction si effectif,
                // gris atténué si l'acteur spawn déjà à l'initiale (SpawnOnNodeId redondant).
                if (_data.Actors != null && !string.IsNullOrEmpty(node.NodeId))
                {
                    for (int ai = 0; ai < _data.Actors.Count; ai++)
                    {
                        var a = _data.Actors[ai];
                        if (a == null || string.IsNullOrEmpty(a.SpawnOnNodeId)) continue;
                        if (!string.Equals(a.SpawnOnNodeId, node.NodeId, System.StringComparison.OrdinalIgnoreCase)) continue;
                        if (!_cachedActorInSockets.TryGetValue(a.ActorId, out var actorIn)) continue;
                        Vector2 actorOut = new Vector2(nodeBox.xMax, nodeBox.yMin + 34f);
                        Color actorWireCol = !a.SpawnInitially
                            ? (a.IsPlayer ? new Color(0.0f, 0.85f, 1.0f, 0.95f) : new Color(1.0f, 0.4f, 0.4f, 0.95f))
                            : new Color(0.6f, 0.6f, 0.6f, 0.5f);
                        DrawBezierWire(actorOut, actorIn, actorWireCol, 2.5f);
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

            // Wires Interactables → Nœud cible (TriggerNodeId) : teal distinct des triggers.
            if (_data.Interactables != null)
            {
                for (int i = 0; i < _data.Interactables.Count; i++)
                {
                    var it = _data.Interactables[i];
                    if (it == null || string.IsNullOrEmpty(it.TriggerNodeId)) continue;
                    if (_cachedInteractableOutSockets.TryGetValue(it.InteractableId, out var itOut)
                        && _cachedNodeInputSockets.TryGetValue(it.TriggerNodeId, out var targetIn))
                    {
                        DrawBezierWire(itOut, targetIn, new Color(0.2f, 0.95f, 0.85f, 0.95f), 3.5f);
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
            // Cible nœud la plus proche du drop (auto-lien exclu), sinon tag de bord
            // hors-champ sous le curseur. Faux si rien d'acceptable sous la souris.
            bool TryPickNodeTarget(Vector2 at, string selfId, out string picked)
            {
                picked = null;
                float best = 36f;
                foreach (var kvp in _cachedNodeInputSockets)
                {
                    if (string.Equals(kvp.Key, selfId, System.StringComparison.OrdinalIgnoreCase)) continue;
                    float dist = Vector2.Distance(kvp.Value, at);
                    if (dist < best)
                    {
                        best = dist;
                        picked = kvp.Key;
                    }
                }
                if (!string.IsNullOrEmpty(picked)) return true;
                for (int i = 0; i < _offscreenTags.Count; i++)
                {
                    var tag = _offscreenTags[i];
                    if (string.Equals(tag.TargetNodeId, selfId, System.StringComparison.OrdinalIgnoreCase)) continue;
                    if (tag.ScreenRect.Contains(_graphMouseScreenPos))
                    {
                        picked = tag.TargetNodeId;
                        return true;
                    }
                }
                return false;
            }

            void PlayWireSoundLocal()
            {
                if (Killtime.Audio.KilltimeAudioManager.Instance != null)
                {
                    Killtime.Audio.KilltimeAudioManager.Instance.PlayUI(Killtime.Audio.SoundId.UI_Filter, 0.6f);
                }
            }

            // Nœud → nœud (sortie principale NextNodeId) : drop sur l'entrée d'un
            // nœud = lier (auto-lien refusé), drop sur un tag de bord = lier la cible
            // hors-champ, drop dans le vide = effacer le lien.
            if (_wireDraft.IsNode && _wireDraft.SourceNode != null)
            {
                var src = _wireDraft.SourceNode;
                // Simple clic sans drag = annulation (pas d'effacement accidentel).
                if (Vector2.Distance(mousePos, _wireDraft.StartPos) * _zoom <= 6f) return;
                if (_data.Nodes.Contains(src))
                {
                    if (TryPickNodeTarget(mousePos, src.NodeId, out string picked))
                    {
                        src.NextNodeId = picked;
                        PlayWireSoundLocal();
                    }
                    else
                    {
                        src.NextNodeId = "";
                    }
                }
                return;
            }
            // Choix majeur → nœud : même routage vers Choices[i].NextNodeId.
            if (_wireDraft.IsNodeChoice && _wireDraft.SourceNode != null)
            {
                var src = _wireDraft.SourceNode;
                int ci = _wireDraft.NodeChoiceIndex;
                bool valid = _data.Nodes.Contains(src) && src.Choices != null && ci >= 0 && ci < src.Choices.Count && src.Choices[ci] != null;
                // Simple clic sans drag = annulation (pas d'effacement accidentel).
                if (valid && Vector2.Distance(mousePos, _wireDraft.StartPos) * _zoom <= 6f) return;
                if (valid)
                {
                    if (TryPickNodeTarget(mousePos, src.NodeId, out string picked))
                    {
                        src.Choices[ci].NextNodeId = picked;
                        PlayWireSoundLocal();
                    }
                    else
                    {
                        src.Choices[ci].NextNodeId = "";
                    }
                }
                return;
            }
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
            // Interactable : même routage, la cible est TriggerNodeId (saut runtime existant).
            if (_wireDraft.IsInteractable && _wireDraft.SourceInteractable != null)
            {
                foreach (var kvp in _cachedNodeInputSockets)
                {
                    if (Vector2.Distance(kvp.Value, mousePos) < 36f)
                    {
                        _wireDraft.SourceInteractable.TriggerNodeId = kvp.Key;
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

        // ------------------------------------------------------------------
        // Section détails du nœud (ex-inspecteur) : dessinée SOUS les cartes,
        // incluse dans la bounding box. Repliée par défaut si le nœud a des cartes.
        // ------------------------------------------------------------------
        private readonly Dictionary<string, bool> _nodeSecState = new();
        private static bool NodeHasCards(SceneNodeData node)
        {
            if (node == null) return false;
            return (node.Dialogues != null && node.Dialogues.Count > 0)
                || (node.Events != null && node.Events.Count > 0)
                || (node.Consequences != null && node.Consequences.Count > 0);
        }
        private bool IsNodeSecOpen(SceneNodeData node)
        {
            if (node == null) return false;
            if (_nodeSecState.TryGetValue(SKey(node, "sec"), out bool v)) return v;
            return !NodeHasCards(node);
        }
        private float GetNodeSectionHeight(SceneNodeData node)
        {
            if (node == null) return 24f;
            if (!IsNodeSecOpen(node)) return 24f;
            float h = 22f; // header ▾
            h += 22f + 22f + 20f + 18f + 54f + 22f + 22f; // id/titre, lieu/type, combat, narr, bouton, next
            h += 22f; // header choix majeurs
            if (node.Choices != null)
            {
                for (int c = 0; c < node.Choices.Count; c++)
                {
                    if (node.Choices[c] == null) continue;
                    h += 66f; // id/cible, libellé, conséquence
                    h += MeasureEffectsBlock(node.Choices[c].Effects, SectionOpen(_expandedSections, node.Choices[c], "nfx"), true) * 20f;
                }
            }
            h += 22f; // header objectifs
            if (node.Objectives != null)
            {
                for (int o = 0; o < node.Objectives.Count; o++)
                {
                    if (node.Objectives[o] == null) continue;
                    h += 44f; // ligne + détail
                }
            }
            h += MeasureEffectsBlock(node.EnterEffects, SectionOpen(_expandedSections, node, "nenter"), true) * 20f;
            return h;
        }
        private void DrawNodeSection(SceneNodeData node, float secX, float secY, float secW)
        {
            if (node == null) return;
            node.Choices ??= new System.Collections.Generic.List<ScenarioChoice>();
            node.Objectives ??= new System.Collections.Generic.List<ScenarioObjective>();
            node.EnterEffects ??= new System.Collections.Generic.List<ScenarioEffect>();
            bool open = IsNodeSecOpen(node);
            if (!open)
            {
                if (GraphButton(new Rect(secX, secY, secW, 20f), $"▸ ⚙ Nœud : {node.Title}"))
                    _nodeSecState[SKey(node, "sec")] = true;
                return;
            }
            float y = secY;
            if (GraphButton(new Rect(secX, y, secW, 22f), $"▾ ⚙ Nœud : {node.Title}"))
                _nodeSecState[SKey(node, "sec")] = false;
            y += 22f;
            GraphLabel(new Rect(secX, y, 30f, 20f), "ID:");
            node.NodeId = GraphTextField(new Rect(secX + 32f, y, 130f, 20f), node.NodeId ?? "");
            GraphLabel(new Rect(secX + 166f, y, 45f, 20f), "Titre:");
            node.Title = GraphTextField(new Rect(secX + 213f, y, Mathf.Max(60f, secX + secW - secX - 213f), 20f), node.Title ?? "");
            y += 22f;
            GraphLabel(new Rect(secX, y, 45f, 20f), "Lieu:");
            node.Location = GraphTextField(new Rect(secX + 47f, y, 110f, 20f), node.Location ?? "");
            GraphLabel(new Rect(secX + 161f, y, 45f, 20f), "Type:");
            {
                int kindCount = Enum.GetValues(typeof(ScenarioNodeKind)).Length;
                if (GraphButton(new Rect(secX + 208f, y, 20f, 20f), "◀"))
                    node.Kind = (ScenarioNodeKind)(((int)node.Kind - 1 + kindCount) % kindCount);
                GraphLabel(new Rect(secX + 230f, y, 84f, 20f), $"<b>{node.Kind}</b>");
                if (GraphButton(new Rect(secX + 316f, y, 20f, 20f), "▶"))
                    node.Kind = (ScenarioNodeKind)(((int)node.Kind + 1) % kindCount);
            }
            y += 22f;
            node.TriggerCombatOnEnter = GraphToggle(new Rect(secX, y, Mathf.Max(200f, secW), 20f), node.TriggerCombatOnEnter, "⚔ Combat à l'entrée");
            y += 20f;
            GraphLabel(new Rect(secX, y, secW, 18f), "<color=grey>Narration :</color>");
            y += 18f;
            node.Body = GraphTextArea(new Rect(secX, y, secW, 54f), node.Body ?? "");
            y += 54f;
            GraphLabel(new Rect(secX, y, 70f, 20f), "Bouton :");
            node.ContinueLabel = GraphTextField(new Rect(secX + 72f, y, Mathf.Max(60f, secX + secW - secX - 72f), 20f), node.ContinueLabel ?? "");
            y += 22f;
            GraphLabel(new Rect(secX, y, 110f, 20f), "Nœud Suivant :");
            node.NextNodeId = GraphTextField(new Rect(secX + 112f, y, Mathf.Max(60f, secX + secW - secX - 112f), 20f), node.NextNodeId ?? "");
            y += 22f;
            GUI.backgroundColor = new Color(0.2f, 0.75f, 0.45f);
            if (GraphButton(new Rect(secX + secW - 92f, y, 92f, 20f), "+ Choix"))
                node.Choices.Add(new ScenarioChoice { Id = $"choice_{node.Choices.Count + 1}", Label = "Nouveau choix", NextNodeId = "" });
            GUI.backgroundColor = Color.white;
            GraphLabel(new Rect(secX, y, Mathf.Max(80f, secW - 96f), 20f), $"<b>Choix majeurs ({node.Choices.Count})</b>");
            y += 22f;
            for (int c = 0; c < node.Choices.Count; c++)
            {
                var chx = node.Choices[c];
                if (chx == null) continue;
                chx.Effects ??= new System.Collections.Generic.List<ScenarioEffect>();
                chx.Id = GraphTextField(new Rect(secX, y, 100f, 20f), chx.Id ?? "");
                GraphLabel(new Rect(secX + 102f, y, 20f, 20f), "➔");
                chx.NextNodeId = GraphTextField(new Rect(secX + 124f, y, 100f, 20f), chx.NextNodeId ?? "");
                GUI.backgroundColor = new Color(0.85f, 0.25f, 0.25f);
                if (GraphButton(new Rect(secX + 226f, y, 22f, 20f), "✕"))
                {
                    node.Choices.RemoveAt(c);
                    GUI.backgroundColor = Color.white;
                    break;
                }
                GUI.backgroundColor = Color.white;
                DrawGraphSolidRect(new Rect(secX + secW - 18f, y + 2f, 16f, 16f),
                    chx.Id.Contains("hostile") ? new Color(1.0f, 0.35f, 0.35f) : new Color(0.2f, 0.92f, 0.45f));
                y += 22f;
                GraphLabel(new Rect(secX, y, 70f, 20f), "Libellé :");
                chx.Label = GraphTextField(new Rect(secX + 72f, y, Mathf.Max(60f, secX + secW - secX - 72f), 20f), chx.Label ?? "");
                y += 22f;
                GraphLabel(new Rect(secX, y, 95f, 20f), "Conséquence :");
                chx.Consequence = GraphTextField(new Rect(secX + 97f, y, Mathf.Max(60f, secX + secW - secX - 97f), 20f), chx.Consequence ?? "");
                y += 22f;
                DrawEffectsBlock(ref y, secX + 12f, secW - 12f, chx.Effects, chx, "nfx", "+fx", true);
            }
            GUI.backgroundColor = new Color(0.2f, 0.92f, 0.45f);
            if (GraphButton(new Rect(secX + secW - 100f, y, 100f, 20f), "+ Objectif"))
                node.Objectives.Add(new ScenarioObjective { Id = $"obj_{node.Objectives.Count + 1}", Label = "Nouvel objectif...", Optional = false });
            GUI.backgroundColor = Color.white;
            GraphLabel(new Rect(secX, y, Mathf.Max(80f, secW - 104f), 20f), $"<b>Objectifs ({node.Objectives.Count})</b>");
            y += 22f;
            for (int o = 0; o < node.Objectives.Count; o++)
            {
                var obj = node.Objectives[o];
                if (obj == null) continue;
                Color prevObjBg = GUI.backgroundColor;
                GUI.backgroundColor = obj.Optional ? new Color(0.45f, 0.45f, 0.48f) : new Color(0.2f, 0.75f, 1f);
                if (GraphButton(new Rect(secX, y, 56f, 20f), obj.Optional ? "Opt." : "Princ."))
                    obj.Optional = !obj.Optional;
                GUI.backgroundColor = prevObjBg;
                obj.Label = GraphTextField(new Rect(secX + 60f, y, Mathf.Max(40f, secX + secW - secX - 60f - 26f), 20f), obj.Label ?? "");
                GUI.backgroundColor = new Color(0.85f, 0.25f, 0.25f);
                if (GraphButton(new Rect(secX + secW - 22f, y, 22f, 20f), "✕"))
                {
                    node.Objectives.RemoveAt(o);
                    GUI.backgroundColor = Color.white;
                    break;
                }
                GUI.backgroundColor = Color.white;
                y += 22f;
                GraphLabel(new Rect(secX, y, 30f, 20f), "ID:");
                obj.Id = GraphTextField(new Rect(secX + 32f, y, 90f, 20f), obj.Id ?? "");
                GraphLabel(new Rect(secX + 126f, y, 55f, 20f), "Détail:");
                obj.Detail = GraphTextField(new Rect(secX + 183f, y, Mathf.Max(40f, secX + secW - secX - 183f), 20f), obj.Detail ?? "");
                y += 22f;
            }
            DrawEffectsBlock(ref y, secX, secW, node.EnterEffects, node, "nenter", "Effets d'entrée", true);
        }

        private void DrawNodeFrame(SceneNodeData node, int nodeIndex, Vector2 mouseWorld)
        {
            Rect frameRect = ComputeNodeBoundingBox(node);
            Rect headerRect = new Rect(frameRect.x, frameRect.y, frameRect.width, 38f);
            // Zone boutons réelle : ⚙ + 90 + 82 + 95 + 26 + espacements ≈ 350px calée à droite.
            Rect actionsArea = new Rect(headerRect.xMax - 360f, headerRect.y + 5f, 350f, 26f);
            Rect nodeInSocket = new Rect(frameRect.x - 9f, frameRect.y + 10f, 18f, 18f);
            Rect nodeOutSocket = new Rect(frameRect.xMax - 9f, frameRect.y + 10f, 18f, 18f);

            Event evt = Event.current;
            int nodeControlId = GUIUtility.GetControlID(FocusType.Passive);

            // Glissement du Nœud Englobant dans l'espace monde (protection stricte des boutons d'actions
            // ET des ports de sortie : un port reste un port, pas une poignée de déplacement).
            bool isOverHeaderDragZone = headerRect.Contains(mouseWorld) && !actionsArea.Contains(mouseWorld)
                && !nodeOutSocket.Contains(mouseWorld);

            // Double-clic sur la frame = cadrer ce nœud en lisible (raccourci direct vers
            // le focus, sans passer par Aller à / ◀ ▶). Hors ports : double-cliquer un port
            // ne doit ni déplacer ni effacer le lien.
            if (evt.type == EventType.MouseDown && evt.button == 0 && evt.clickCount == 2 && !_pickerCapturesMouse && frameRect.Contains(mouseWorld)
                && !nodeOutSocket.Contains(mouseWorld) && !IsOverNodeChoicePort(frameRect, node, mouseWorld))
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
            float titleW = Mathf.Max(90f, frameRect.width - 507f);
            GraphLabel(new Rect(headerRect.x + 16f, headerRect.y + 8f, titleW, 22f), $"<b>■ NŒUD #{nodeIndex + 1} : {node.Title.ToUpperInvariant()}</b> [{node.Kind}]");

            GUI.color = new Color(1f, 1f, 1f, 0.75f);
            float idLabelW = Mathf.Max(40f, Mathf.Min(130f, frameRect.width - titleW - 350f));
            GraphLabel(new Rect(headerRect.x + 16f + titleW + 4f, headerRect.y + 8f, idLabelW, 20f), $"ID: <i>{node.NodeId}</i>");
            GUI.color = Color.white;

            float btnY = headerRect.y + 7f;
            float btnH = 22f;
            float bx = headerRect.xMax - 10f;
            Color prevBg = GUI.backgroundColor;
            bool secOpen = IsNodeSecOpen(node);
            if (GraphButton(new Rect(bx - 26f, btnY, 26f, btnH), secOpen ? "⚙▾" : "⚙▸"))
                _nodeSecState[SKey(node, "sec")] = !secOpen;
            bx -= 26f + 6f;
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

            // Sortie principale (NextNodeId) : toujours visible (atténuée si vide),
            // drag vers l'entrée d'un nœud = lier, drop dans le vide = effacer.
            bool hasNext = !string.IsNullOrEmpty(node.NextNodeId);
            Color nextPortCol = hasNext ? new Color(1.0f, 0.65f, 0.15f) : new Color(1.0f, 0.65f, 0.15f, 0.35f);
            DrawGraphSolidRect(nodeOutSocket, nextPortCol);

            if (evt.clickCount < 2 && GraphPrimaryDown(mouseWorld, nodeOutSocket))
            {
                _wireDraft = new WireConnectionDraft
                {
                    IsActive = true,
                    IsNode = true,
                    SourceNode = node,
                    StartPos = GetNodeMainOutCenter(frameRect)
                };
                evt.Use();
            }

            // Sorties choix majeurs : un port par choix sur le flanc droit, aux mêmes
            // positions que les origines des wires (GetNodeChoiceOutCenter).
            if (node.Choices != null)
            {
                for (int cp = 0; cp < node.Choices.Count; cp++)
                {
                    var chx = node.Choices[cp];
                    if (chx == null) continue;
                    Vector2 choiceCenter = GetNodeChoiceOutCenter(frameRect, cp);
                    Rect choicePort = GetNodeChoicePortRect(frameRect, cp);
                    bool hasChoiceLink = !string.IsNullOrEmpty(chx.NextNodeId);
                    Color baseChoiceCol = chx.Id.Contains("hostile") ? new Color(1.0f, 0.35f, 0.35f) : new Color(0.2f, 0.92f, 0.45f);
                    if (!hasChoiceLink) baseChoiceCol.a = 0.35f;
                    DrawGraphSolidRect(choicePort, baseChoiceCol);

                    if (evt.clickCount < 2 && GraphPrimaryDown(mouseWorld, choicePort))
                    {
                        _wireDraft = new WireConnectionDraft
                        {
                            IsActive = true,
                            IsNodeChoice = true,
                            NodeChoiceIndex = cp,
                            SourceNode = node,
                            StartPos = choiceCenter
                        };
                        evt.Use();
                    }
                }
            }

            // Section détails du nœud (ex-inspecteur + ex-inline) : sous les cartes,
            // incluse dans la bounding box.
            {
                float sectionH = GetNodeSectionHeight(node);
                float secW = frameRect.width - 32f;
                DrawNodeSection(node, frameRect.x + 16f, frameRect.yMax - 14f - sectionH, secW);
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
                        float nextRowTop = GetConsLinkRowTop(cons);
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
                    ch.Prerequisite ??= new ScenePrerequisiteData();
                    ch.Effects ??= new System.Collections.Generic.List<ScenarioEffect>();
                    var chal = ch.Challenge;

                    // Rangée principale : [mode 30][label][➔][next 70][⋯][⊘].
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

                    float labelW = Mathf.Max(40f, innerW - 32f - 144f);
                    ch.Label = GraphTextField(new Rect(innerX + 32f, y, labelW, 20f), ch.Label ?? "");
                    GraphLabel(new Rect(innerX + 32f + labelW + 2f, y, 16f, 20f), "➔");
                    ch.NextLineId = GraphTextField(new Rect(innerX + 32f + labelW + 20f, y, 70f, 20f), ch.NextLineId ?? "");
                    bool detAny = SectionOpen(_expandedSections, ch, "pre") || SectionOpen(_expandedSections, ch, "fx") || SectionOpen(_expandedSections, ch, "chd");
                    if (GraphButton(new Rect(innerX + 32f + labelW + 92f, y, 20f, 20f), detAny ? "▾" : "▸"))
                    {
                        _expandedSections.Remove(SKey(ch, "pre"));
                        _expandedSections.Remove(SKey(ch, "fx"));
                        _expandedSections.Remove(SKey(ch, "chd"));
                        if (!detAny)
                        {
                            _expandedSections.Add(SKey(ch, "pre"));
                            _expandedSections.Add(SKey(ch, "fx"));
                            _expandedSections.Add(SKey(ch, "chd"));
                        }
                    }
                    if (GraphButton(new Rect(innerX + 32f + labelW + 114f, y, 20f, 20f), "⊘")) ch.NextLineId = "";
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

                    // Méta du choix : prérequis et effets (résumé).
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

                    // Détails repliables du choix (ex-inspecteur) : prérequis / effets / défi.
                    if (SectionOpen(_expandedSections, ch, "pre"))
                        DrawPrereqDetail(ref y, innerX + 12f, innerW - 12f, ch.Prerequisite);
                    DrawEffectsBlock(ref y, innerX + 12f, innerW - 12f, ch.Effects, ch, "fx", "+fx", false);
                    if (chal.HasChallenge && SectionOpen(_expandedSections, ch, "chd"))
                        DrawChallengeDetail(ref y, innerX + 12f, innerW - 12f, ch);
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

            // Strip détails (ex-inspecteur) : 🎥 caméra, 🔒 prérequis, 🔊 ambiance, 🎲 test auto.
            {
                float stripX = innerX;
                bool camOpen = FoldoutButton(new Rect(stripX, y, 46f, 20f), line, "cam", "🎥"); stripX += 48f;
                bool preOpen = FoldoutButton(new Rect(stripX, y, 46f, 20f), line, "pre", "🔒"); stripX += 48f;
                bool ambOpen = FoldoutButton(new Rect(stripX, y, 46f, 20f), line, "amb", "🔊"); stripX += 48f;
                bool autoOpen = FoldoutButton(new Rect(stripX, y, 46f, 20f), line, "auto", "🎲");
                y += 20f;
                if (camOpen) DrawCameraDetail(ref y, innerX + 12f, innerW - 12f, line);
                if (preOpen) DrawPrereqDetail(ref y, innerX + 12f, innerW - 12f, line.Prerequisite);
                if (ambOpen) DrawAmbienceDetail(ref y, innerX + 12f, innerW - 12f, line.Ambience);
                if (autoOpen) DrawAutoDetail(ref y, innerX + 12f, innerW - 12f, line.AutoSkillCheck);
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
            {
                int kindCount = Enum.GetValues(typeof(SceneEventKind)).Length;
                if (GraphButton(new Rect(innerX + 47f, y, 20f, 18f), "◀"))
                    ev.Kind = (SceneEventKind)(((int)ev.Kind - 1 + kindCount) % kindCount);
                GraphLabel(new Rect(innerX + 69f, y, Mathf.Max(40f, innerW - 69f - 22f), 18f), $"<b>{ev.Kind}</b>");
                if (GraphButton(new Rect(innerX + innerW - 20f, y, 20f, 18f), "▶"))
                    ev.Kind = (SceneEventKind)(((int)ev.Kind + 1) % kindCount);
            }
            y += 22f;

            // Strip + détails (ex-inspecteur) : prérequis, ambiance, test auto, effets.
            {
                float stripX = innerX;
                bool preOpen = FoldoutButton(new Rect(stripX, y, 46f, 20f), ev, "pre", "🔒"); stripX += 48f;
                bool ambOpen = FoldoutButton(new Rect(stripX, y, 46f, 20f), ev, "amb", "🔊"); stripX += 48f;
                bool autoOpen = FoldoutButton(new Rect(stripX, y, 46f, 20f), ev, "auto", "🎲");
                y += 20f;
                if (preOpen) DrawPrereqDetail(ref y, innerX + 12f, innerW - 12f, ev.Prerequisite);
                if (ambOpen) DrawAmbienceDetail(ref y, innerX + 12f, innerW - 12f, ev.Ambience);
                if (autoOpen) DrawAutoDetail(ref y, innerX + 12f, innerW - 12f, ev.AutoSkillCheck);
            }
            DrawEffectsBlock(ref y, innerX + 12f, innerW - 12f, ev.Effects, ev, "fx", "+fx", true);

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

            cons.Effects ??= new System.Collections.Generic.List<ScenarioEffect>();
            DrawEffectsBlock(ref y, innerX, innerW, cons.Effects, cons, "fx", "+fx", true);

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
                float fldX = innerX + 97f;
                bool hasInters = _data.Interactables != null && _data.Interactables.Count > 0;
                float arrowsW = hasInters ? 44f : 0f;
                trg.InteractableId = GraphTextField(new Rect(fldX, y, Mathf.Max(40f, innerW - 97f - arrowsW), 18f), trg.InteractableId ?? "");
                if (hasInters)
                {
                    int curIdx = _data.Interactables.FindIndex(a => a != null && string.Equals(a.InteractableId, trg.InteractableId, System.StringComparison.OrdinalIgnoreCase));
                    if (GraphButton(new Rect(innerX + innerW - 42f, y, 20f, 18f), "◀"))
                    {
                        curIdx = (curIdx < 0 ? _data.Interactables.Count - 1 : (curIdx - 1 + _data.Interactables.Count) % _data.Interactables.Count);
                        trg.InteractableId = _data.Interactables[curIdx]?.InteractableId ?? "";
                    }
                    if (GraphButton(new Rect(innerX + innerW - 20f, y, 20f, 18f), "▶"))
                    {
                        curIdx = (curIdx < 0 ? 0 : (curIdx + 1) % _data.Interactables.Count);
                        trg.InteractableId = _data.Interactables[curIdx]?.InteractableId ?? "";
                    }
                }
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

        // ------------------------------------------------------------------
        // Carte Interactable : objet physique persistant niveau scène (comme un
        // Trigger global, PAS un Nœud narratif). Source seule : aucun port
        // d'entrée, une sortie teal vers le Nœud cible (TriggerNodeId).
        // Réponse à la question nodes-vs-cartes : les Nœuds = étapes narratives
        // (frames avec dialogues/events/conséquences) ; les Interactables = sources
        // ponctuelles → cartes flottantes globales, même rangée que les Triggers.
        // ------------------------------------------------------------------
        private void DrawInteractableCard(SceneInteractableSpawnData it, int index, Vector2 mouseWorld)
        {
            if (it == null) return;
            if (string.IsNullOrEmpty(it.InteractableId)) it.InteractableId = $"obj_{index + 1}";
            string targetDisplay = string.IsNullOrEmpty(it.TriggerNodeId) ? "(sans cible)" : it.TriggerNodeId;

            Vector2 size = GetInteractableCardSize(it);
            float cardW = size.x;
            float cardH = size.y;

            Rect cardRect = new Rect(it.GraphPosX, it.GraphPosY, cardW, cardH);
            Rect headerRect = new Rect(cardRect.x, cardRect.y, cardW, 26f);
            Rect closeRect = new Rect(headerRect.xMax - 22f, headerRect.y + 3f, 18f, 18f);
            Rect dragHeaderRect = new Rect(headerRect.x, headerRect.y, headerRect.width - 26f, headerRect.height);
            Rect resizeGripRect = new Rect(cardRect.xMax - 14f, cardRect.yMax - 14f, 14f, 14f);

            Event evt = Event.current;
            string dragKey = $"inter_{index}_{it.InteractableId}";
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
                it.CardWidth = Mathf.Clamp(mouseWorld.x - cardRect.x, 300f, 750f);
                it.CardHeight = Mathf.Clamp(mouseWorld.y - cardRect.y, 200f, 500f);
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
                _dragOffset = mouseWorld - new Vector2(it.GraphPosX, it.GraphPosY);
                evt.Use();
            }
            else if ((evt.type == EventType.MouseDrag || evt.type == EventType.MouseMove) && GUIUtility.hotControl == dragControlId && _draggingCardKey == dragKey)
            {
                it.GraphPosX = mouseWorld.x - _dragOffset.x;
                it.GraphPosY = mouseWorld.y - _dragOffset.y;
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == dragControlId)
            {
                GUIUtility.hotControl = 0;
                _draggingCardKey = null;
                evt.Use();
            }

            DrawGraphSolidRect(cardRect, new Color(0.05f, 0.12f, 0.12f, 0.95f));
            Color headerCol = new Color(0.08f, 0.45f, 0.42f);
            DrawGraphSolidRect(headerRect, headerCol);

            DrawGraphSolidRect(new Rect(cardRect.x, cardRect.y, cardRect.width, 1f), new Color(0.2f, 0.95f, 0.85f, 0.8f));
            DrawGraphSolidRect(new Rect(cardRect.x, cardRect.yMax - 1f, cardRect.width, 1f), new Color(1f, 1f, 1f, 0.08f));
            DrawGraphSolidRect(new Rect(cardRect.x, cardRect.y, 1f, cardRect.height), new Color(1f, 1f, 1f, 0.08f));
            DrawGraphSolidRect(new Rect(cardRect.xMax - 1f, cardRect.y, 1f, cardRect.height), new Color(1f, 1f, 1f, 0.08f));

            string headerName = string.IsNullOrEmpty(it.DisplayName) ? "(sans nom)" : it.DisplayName;
            GraphLabel(new Rect(headerRect.x + 8f, headerRect.y + 3f, cardW - 35f, 20f), $"<b>🔧 {headerName}</b> [{it.InteractableId}]");

            GUI.backgroundColor = new Color(0.85f, 0.22f, 0.22f, 1f);
            GUI.color = Color.white;
            if (GraphButton(closeRect, "✕"))
            {
                if (_data.Interactables != null) _data.Interactables.RemoveAt(index);
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
                _hoverTitle = $"🔧 {headerName}  ({it.InteractableId})";
                _hoverBody = $"{it.GetSummary()}\nAction : {it.ActionLabel}\n{(string.IsNullOrEmpty(it.SuccessLog) ? "(aucun log)" : it.SuccessLog)}";
                _hoverScreenPos = _graphMouseScreenPos;
            }

            float innerX = cardRect.x + 8f;
            float innerW = cardW - 16f;
            float y = cardRect.y + 30f;

            GraphLabel(new Rect(innerX, y, 24f, 18f), "ID:");
            it.InteractableId = GraphTextField(new Rect(innerX + 26f, y, 100f, 18f), it.InteractableId ?? "");
            GUI.color = new Color(0.2f, 0.95f, 0.85f);
            GraphLabel(new Rect(innerX + 130f, y, Mathf.Max(40f, innerW - 130f), 18f), $"➔ {targetDisplay}");
            GUI.color = Color.white;
            y += 20f;

            GraphLabel(new Rect(innerX, y, 36f, 18f), "Nom:");
            it.DisplayName = GraphTextField(new Rect(innerX + 38f, y, Mathf.Max(40f, innerW - 38f), 18f), it.DisplayName ?? "");
            y += 20f;

            GraphLabel(new Rect(innerX, y, 48f, 18f), "Action:");
            it.ActionLabel = GraphTextField(new Rect(innerX + 50f, y, Mathf.Max(40f, innerW - 50f), 18f), it.ActionLabel ?? "");
            y += 20f;

            // Position hex + rayon sur une ligne compacte.
            GraphLabel(new Rect(innerX, y, 16f, 18f), "Q:");
            int.TryParse(GraphTextField(new Rect(innerX + 18f, y, 36f, 18f), it.Q.ToString()), out it.Q);
            GraphLabel(new Rect(innerX + 58f, y, 14f, 18f), "R:");
            int.TryParse(GraphTextField(new Rect(innerX + 72f, y, 36f, 18f), it.R.ToString()), out it.R);
            GraphLabel(new Rect(innerX + 112f, y, 44f, 18f), "Rayon:");
            int.TryParse(GraphTextField(new Rect(innerX + 158f, y, 32f, 18f), it.Radius.ToString()), out it.Radius);
            it.Radius = Mathf.Clamp(it.Radius, 0, 12);
            it.IsOneShot = GraphToggle(new Rect(innerX + 194f, y, Mathf.Max(60f, innerW - 194f), 18f), it.IsOneShot, "Unique");
            y += 20f;

            // Compétence requise + SD.
            GraphLabel(new Rect(innerX, y, 46f, 18f), "Test :");
            {
                int skillCount = Enum.GetValues(typeof(SkillType)).Length;
                int skillIdx = (int)it.RequiredSkill;
                if (GraphButton(new Rect(innerX + 48f, y, 20f, 18f), "◀"))
                {
                    skillIdx = (skillIdx - 1 + skillCount) % skillCount;
                    it.RequiredSkill = (SkillType)skillIdx;
                }
                GraphLabel(new Rect(innerX + 70f, y, Mathf.Max(40f, innerW - 70f - 72f), 18f), $"<b>{SkillDefinitions.GetDisplayName(it.RequiredSkill)}</b>");
                if (GraphButton(new Rect(innerX + innerW - 70f, y, 20f, 18f), "▶"))
                {
                    skillIdx = (skillIdx + 1) % skillCount;
                    it.RequiredSkill = (SkillType)skillIdx;
                }
                GraphLabel(new Rect(innerX + innerW - 48f, y, 20f, 18f), "SD:");
                int.TryParse(GraphTextField(new Rect(innerX + innerW - 26f, y, 26f, 18f), it.SkillThreshold.ToString()), out it.SkillThreshold);
            }
            y += 20f;

            GraphLabel(new Rect(innerX, y, 68f, 18f), "→ Nœud :");
            it.TriggerNodeId = GraphTextField(new Rect(innerX + 70f, y, Mathf.Max(40f, innerW - 70f), 18f), it.TriggerNodeId ?? "");
            y += 20f;

            GraphLabel(new Rect(innerX, y, 66f, 18f), "Objectif :");
            it.CompletionObjectiveId = GraphTextField(new Rect(innerX + 68f, y, Mathf.Max(40f, innerW - 68f), 18f), it.CompletionObjectiveId ?? "");
            y += 20f;

            GraphLabel(new Rect(innerX, y, 66f, 18f), "Log ✔ :");
            it.SuccessLog = GraphTextField(new Rect(innerX + 68f, y, Mathf.Max(40f, innerW - 68f), 18f), it.SuccessLog ?? "");
            y += 20f;

            if (y + 4f <= cardRect.yMax)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.55f);
                GraphLabel(new Rect(innerX, y, innerW, 18f), it.GetSummary());
                GUI.color = Color.white;
            }

            // Sortie seule : pas de port d'entrée sur un interactable.
            Rect outPort2 = new Rect(cardRect.xMax - 8f, cardRect.y + 18f, 16f, 16f);
            DrawGraphSolidRect(outPort2, new Color(0.2f, 0.95f, 0.85f));

            if (GraphPrimaryDown(mouseWorld, outPort2))
            {
                _wireDraft = new WireConnectionDraft
                {
                    IsActive = true,
                    IsInteractable = true,
                    SourceInteractable = it,
                    StartPos = outPort2.center
                };
                evt.Use();
            }
        }

        // Nombre de références à un acteur dans la scène (locuteur, cible, déclencheur).
        // Match sur ActorId OU DisplayName, insensible à la casse (comme BuildActorCandidates).
        private void CountActorReferences(SceneActorSpawnData a, out int lines, out int events, out int triggers)
        {
            lines = 0; events = 0; triggers = 0;
            if (a == null || _data == null) return;
            bool Match(string v)
            {
                if (string.IsNullOrWhiteSpace(v)) return false;
                return string.Equals(v.Trim(), a.ActorId?.Trim(), System.StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrWhiteSpace(a.DisplayName) && string.Equals(v.Trim(), a.DisplayName.Trim(), System.StringComparison.OrdinalIgnoreCase));
            }
            if (_data.Nodes != null)
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
                            if (Match(line.SpeakerId)) lines++;
                        }
                    }
                    if (node.Consequences != null)
                    {
                        for (int k = 0; k < node.Consequences.Count; k++)
                        {
                            var cons = node.Consequences[k];
                            if (cons == null) continue;
                            if (cons.HasDialogue && Match(cons.SpeakerId)) lines++;
                        }
                    }
                    if (node.Events != null)
                    {
                        for (int e = 0; e < node.Events.Count; e++)
                        {
                            var ev = node.Events[e];
                            if (ev == null) continue;
                            if (Match(ev.TargetActorId)) events++;
                        }
                    }
                }
            }
            if (_data.Triggers != null)
            {
                for (int t = 0; t < _data.Triggers.Count; t++)
                {
                    var trg = _data.Triggers[t];
                    if (trg == null) continue;
                    if (trg.Kind == SceneTriggerKind.ActorCondition && Match(trg.ActorId)) triggers++;
                }
            }
        }

        // ------------------------------------------------------------------
        // Carte Acteur : pion déployé niveau scène (comme un Interactable global,
        // PAS un Nœud narratif). Cible seule : aucun port de sortie, un port
        // d'entrée visuel à gauche (wire Nœud → Acteur quand SpawnOnNodeId est
        // renseigné). Couleur faction : bleu PJ, rouge PNJ.
        // La carte édite identité + placement + spawn ; les stats fines
        // (FOR/AGI/… de l'EmbeddedSheet) se règlent hors éditeur.
        // ------------------------------------------------------------------
        private void DrawActorCard(SceneActorSpawnData a, int index, Vector2 mouseWorld)
        {
            if (a == null) return;
            if (string.IsNullOrEmpty(a.ActorId)) a.ActorId = $"actor_{index + 1}";
            string spawnDisplay = a.SpawnInitially
                ? "● INIT"
                : (string.IsNullOrEmpty(a.SpawnOnNodeId) ? "○ (jamais spawné)" : $"○ si {a.SpawnOnNodeId}");

            Vector2 size = GetActorCardSize(a);
            float cardW = size.x;
            float cardH = size.y;

            Rect cardRect = new Rect(a.GraphPosX, a.GraphPosY, cardW, cardH);
            Rect headerRect = new Rect(cardRect.x, cardRect.y, cardW, 26f);
            Rect closeRect = new Rect(headerRect.xMax - 22f, headerRect.y + 3f, 18f, 18f);
            Rect dragHeaderRect = new Rect(headerRect.x, headerRect.y, headerRect.width - 26f, headerRect.height);
            Rect resizeGripRect = new Rect(cardRect.xMax - 14f, cardRect.yMax - 14f, 14f, 14f);

            Event evt = Event.current;
            string dragKey = $"actor_{index}_{a.ActorId}";
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
                a.CardWidth = Mathf.Clamp(mouseWorld.x - cardRect.x, 300f, 750f);
                a.CardHeight = Mathf.Clamp(mouseWorld.y - cardRect.y, 180f, 500f);
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
                _dragOffset = mouseWorld - new Vector2(a.GraphPosX, a.GraphPosY);
                evt.Use();
            }
            else if ((evt.type == EventType.MouseDrag || evt.type == EventType.MouseMove) && GUIUtility.hotControl == dragControlId && _draggingCardKey == dragKey)
            {
                a.GraphPosX = mouseWorld.x - _dragOffset.x;
                a.GraphPosY = mouseWorld.y - _dragOffset.y;
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == dragControlId)
            {
                GUIUtility.hotControl = 0;
                _draggingCardKey = null;
                evt.Use();
            }

            Color factionCol = a.IsPlayer ? new Color(0.0f, 0.85f, 1.0f) : new Color(1.0f, 0.4f, 0.4f);
            Color headerCol = a.IsPlayer ? new Color(0.10f, 0.32f, 0.55f) : new Color(0.45f, 0.15f, 0.15f);
            DrawGraphSolidRect(cardRect, new Color(0.06f, 0.08f, 0.12f, 0.95f));
            DrawGraphSolidRect(headerRect, headerCol);

            DrawGraphSolidRect(new Rect(cardRect.x, cardRect.y, cardRect.width, 1f), new Color(factionCol.r, factionCol.g, factionCol.b, 0.8f));
            DrawGraphSolidRect(new Rect(cardRect.x, cardRect.yMax - 1f, cardRect.width, 1f), new Color(1f, 1f, 1f, 0.08f));
            DrawGraphSolidRect(new Rect(cardRect.x, cardRect.y, 1f, cardRect.height), new Color(1f, 1f, 1f, 0.08f));
            DrawGraphSolidRect(new Rect(cardRect.xMax - 1f, cardRect.y, 1f, cardRect.height), new Color(1f, 1f, 1f, 0.08f));

            // Port d'entrée visuel (spawn) : pas de drag, la cible se règle via "Si nœud".
            DrawGraphSolidRect(new Rect(cardRect.x - 7f, cardRect.y + 19f, 14f, 14f), factionCol);

            string headerName = string.IsNullOrEmpty(a.DisplayName) ? "(sans nom)" : a.DisplayName;
            GraphLabel(new Rect(headerRect.x + 8f, headerRect.y + 3f, cardW - 35f, 20f), $"<b>👥 {headerName}</b> [{a.ActorId}]");

            GUI.backgroundColor = new Color(0.85f, 0.22f, 0.22f, 1f);
            GUI.color = Color.white;
            if (GraphButton(closeRect, "✕"))
            {
                if (_data.Actors != null) _data.Actors.RemoveAt(index);
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

            CountActorReferences(a, out int refLines, out int refEvents, out int refTriggers);
            if (_zoom < 0.85f && !_isPanning && _draggingCardKey == null && _resizingCardKey == null && !_wireDraft.IsActive
                && headerRect.Contains(mouseWorld))
            {
                _hasHoverCard = true;
                _hoverTitle = $"👥 {headerName}  ({a.ActorId})";
                _hoverBody = $"{a.GetSummary()}\nFiche : {(string.IsNullOrEmpty(a.CharacterSheetFileName) ? "(Défaut)" : a.CharacterSheetFileName)}\nRépliques : {refLines} · Cibles : {refEvents} · Triggers : {refTriggers}";
                _hoverScreenPos = _graphMouseScreenPos;
            }

            float innerX = cardRect.x + 8f;
            float innerW = cardW - 16f;
            float y = cardRect.y + 30f;

            GraphLabel(new Rect(innerX, y, 24f, 18f), "ID:");
            a.ActorId = GraphTextField(new Rect(innerX + 26f, y, 100f, 18f), a.ActorId ?? "");
            GUI.color = a.SpawnInitially ? new Color(0.4f, 1f, 0.55f) : new Color(1f, 1f, 1f, 0.75f);
            GraphLabel(new Rect(innerX + 130f, y, Mathf.Max(40f, innerW - 130f), 18f), spawnDisplay);
            GUI.color = Color.white;
            y += 20f;

            GraphLabel(new Rect(innerX, y, 36f, 18f), "Nom:");
            a.DisplayName = GraphTextField(new Rect(innerX + 38f, y, Mathf.Max(40f, innerW - 38f), 18f), a.DisplayName ?? "");
            y += 20f;

            // Faction + position hex + orientation sur une ligne compacte.
            a.IsPlayer = GraphToggle(new Rect(innerX, y, 52f, 18f), a.IsPlayer, "PJ");
            GraphLabel(new Rect(innerX + 54f, y, 16f, 18f), "Q:");
            int.TryParse(GraphTextField(new Rect(innerX + 70f, y, 36f, 18f), a.Q.ToString()), out a.Q);
            GraphLabel(new Rect(innerX + 110f, y, 14f, 18f), "R:");
            int.TryParse(GraphTextField(new Rect(innerX + 124f, y, 36f, 18f), a.R.ToString()), out a.R);
            GraphLabel(new Rect(innerX + 164f, y, 30f, 18f), "Ori:");
            float.TryParse(GraphTextField(new Rect(innerX + 196f, y, 40f, 18f), a.FacingAngle.ToString("0")), out a.FacingAngle);
            y += 20f;

            // Spawn : initiale ou différé sur nœud (avec cycleur de nœuds).
            a.SpawnInitially = GraphToggle(new Rect(innerX, y, 78f, 18f), a.SpawnInitially, "Initiale");
            GraphLabel(new Rect(innerX + 80f, y, 58f, 18f), "Si nœud:");
            {
                bool hasNodes = _data.Nodes != null && _data.Nodes.Count > 0;
                float arrowsW = hasNodes ? 44f : 0f;
                float fldW = Mathf.Max(40f, innerW - 80f - 58f - arrowsW);
                a.SpawnOnNodeId = GraphTextField(new Rect(innerX + 140f, y, fldW, 18f), a.SpawnOnNodeId ?? "");
                if (hasNodes)
                {
                    int curIdx = _data.Nodes.FindIndex(n => n != null && string.Equals(n.NodeId, a.SpawnOnNodeId, System.StringComparison.OrdinalIgnoreCase));
                    if (GraphButton(new Rect(innerX + innerW - 42f, y, 20f, 18f), "◀"))
                    {
                        curIdx = (curIdx < 0 ? _data.Nodes.Count - 1 : (curIdx - 1 + _data.Nodes.Count) % _data.Nodes.Count);
                        a.SpawnOnNodeId = _data.Nodes[curIdx] != null ? _data.Nodes[curIdx].NodeId : "";
                    }
                    if (GraphButton(new Rect(innerX + innerW - 20f, y, 20f, 18f), "▶"))
                    {
                        curIdx = (curIdx < 0 ? 0 : (curIdx + 1) % _data.Nodes.Count);
                        a.SpawnOnNodeId = _data.Nodes[curIdx] != null ? _data.Nodes[curIdx].NodeId : "";
                    }
                }
            }
            y += 20f;

            // Fiche personnage (cycleur catalogue des fiches disque).
            GraphLabel(new Rect(innerX, y, 42f, 18f), "Fiche:");
            {
                int charIdx = Mathf.Max(0, _availableCharacterFiles.IndexOf(a.CharacterSheetFileName));
                if (GraphButton(new Rect(innerX + 44f, y, 20f, 18f), "◀"))
                {
                    charIdx = (charIdx - 1 + _availableCharacterFiles.Count) % _availableCharacterFiles.Count;
                    a.CharacterSheetFileName = charIdx == 0 ? "" : _availableCharacterFiles[charIdx];
                }
                string charDisplay = string.IsNullOrEmpty(a.CharacterSheetFileName) ? "(Défaut)" : a.CharacterSheetFileName;
                GraphLabel(new Rect(innerX + 66f, y, Mathf.Max(40f, innerW - 66f - 22f), 18f), $"<b>{charDisplay}</b>");
                if (GraphButton(new Rect(innerX + innerW - 20f, y, 20f, 18f), "▶"))
                {
                    charIdx = (charIdx + 1) % _availableCharacterFiles.Count;
                    a.CharacterSheetFileName = charIdx == 0 ? "" : _availableCharacterFiles[charIdx];
                }
            }
            y += 20f;

            GraphLabel(new Rect(innerX, y, 38f, 18f), "Mod:");
            a.ModelPrefabName = GraphTextField(new Rect(innerX + 40f, y, 110f, 18f), a.ModelPrefabName ?? "");
            GraphLabel(new Rect(innerX + 154f, y, 42f, 18f), "Arme:");
            a.EquippedWeaponName = GraphTextField(new Rect(innerX + 198f, y, Mathf.Max(40f, innerW - 198f), 18f), a.EquippedWeaponName ?? "");
            y += 20f;

            if (y + 4f <= cardRect.yMax)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.55f);
                GraphLabel(new Rect(innerX, y, innerW, 18f), $"{a.GetSummary()} · ◀{refLines} ▶{refEvents} ▼{refTriggers}");
                GUI.color = Color.white;
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

using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using Killtime.UI;
using Killtime.Story.Data;
using Killtime.Core.Character;
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

        private readonly string[] _tabs = { "📋 Scène & Carte", "👥 Acteurs", "🔧 Interactables", "🎬 Nœuds & Graphe Nodal", "💾 JSON & Disque" };
        private int _selectedTab = 3;
        private Vector2 _scroll;

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
            public SceneDialogueLineData SourceLine;
            public SceneDialogueChoiceData SourceChoice;
            public SceneEventData SourceEvent;
            public SceneTriggerData SourceTrigger;
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

        private readonly List<string> _availableCharacterFiles = new();
        private readonly List<string> _availableMapFiles = new();

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
                    case 0: DrawMetadataTab(); break;
                    case 1: DrawActorsTab(); break;
                    case 2: DrawInteractablesTab(); break;
                    case 4: DrawJsonDiskTab(); break;
                }
                GUILayout.EndScrollView();
            }
        }

        private void DrawMetadataTab()
        {
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
            GUILayout.Label($"<b>■ SCÈNE : {_data.Title}</b> (<color=#00E5FF>{_data.Nodes.Count} Nœuds</color>)", GUILayout.ExpandWidth(true));

            GUI.backgroundColor = new Color(0.2f, 0.75f, 1f);
            if (GUILayout.Button("+ Nouveau Nœud", GUILayout.Width(120), GUILayout.Height(22)))
            {
                float newX = 60f + _data.Nodes.Count * 480f;
                _data.Nodes.Add(new SceneNodeData
                {
                    NodeId = $"node_{_data.Nodes.Count + 1}",
                    Title = "Nouveau Nœud",
                    GraphPosX = newX,
                    GraphPosY = 100f
                });
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
                    // Première ouverture : une grille responsive utilise la largeur ET la
                    // hauteur du viewport. L'ancien flux narratif en X envoyait #3 à droite,
                    // puis laissait une grande zone vide sous #1/#2 selon la taille de fenêtre.
                    // La grille est appliquée une seule fois ; l'utilisateur garde ensuite le
                    // déplacement manuel et les deux dispositions explicites de la barre.
                    AutoLayoutMosaic(canvasRect);
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
            if (data == null || data.Nodes == null || data.Nodes.Count <= 1) return false;
            for (int i = 0; i < data.Nodes.Count; i++)
            {
                if (data.Nodes[i].GraphPosY < 0f || data.Nodes[i].GraphPosX == 0f) return true;
            }
            return false;
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
            if (_data == null || _data.Nodes == null) return depths;

            var queue = new System.Collections.Generic.Queue<string>();
            if (!string.IsNullOrEmpty(_data.FirstNodeId))
            {
                depths[_data.FirstNodeId] = 0;
                queue.Enqueue(_data.FirstNodeId);
            }

            while (queue.Count > 0)
            {
                string currentId = queue.Dequeue();
                int depth = depths[currentId];
                var node = _data.Nodes.Find(x => x != null && string.Equals(x.NodeId, currentId, System.StringComparison.OrdinalIgnoreCase));
                if (node == null) continue;

                var targets = new System.Collections.Generic.List<string>();
                if (!string.IsNullOrEmpty(node.NextNodeId)) targets.Add(node.NextNodeId);
                if (node.Choices != null)
                    for (int c = 0; c < node.Choices.Count; c++)
                        if (node.Choices[c] != null && !string.IsNullOrEmpty(node.Choices[c].NextNodeId))
                            targets.Add(node.Choices[c].NextNodeId);

                for (int i = 0; i < targets.Count; i++)
                {
                    if (!depths.ContainsKey(targets[i]))
                    {
                        depths[targets[i]] = depth + 1;
                        queue.Enqueue(targets[i]);
                    }
                }
            }

            int maxDepth = -1;
            foreach (var kvp in depths) maxDepth = System.Math.Max(maxDepth, kvp.Value);
            if (_data.Triggers != null)
            {
                for (int t = 0; t < _data.Triggers.Count; t++)
                {
                    var trg = _data.Triggers[t];
                    if (trg == null || string.IsNullOrEmpty(trg.TargetNodeId) || depths.ContainsKey(trg.TargetNodeId)) continue;
                    if (!string.IsNullOrEmpty(trg.SourceNodeId) && depths.TryGetValue(trg.SourceNodeId, out var sd))
                        depths[trg.TargetNodeId] = sd + 1;
                }
                foreach (var kvp in depths) maxDepth = System.Math.Max(maxDepth, kvp.Value);
            }

            for (int n = 0; n < _data.Nodes.Count; n++)
            {
                var node = _data.Nodes[n];
                if (node == null || string.IsNullOrEmpty(node.NodeId) || depths.ContainsKey(node.NodeId)) continue;
                depths[node.NodeId] = maxDepth + 1;
            }

            return depths;
        }

        private void AutoLayoutGlobalScene(Rect? canvasRect = null)
        {
            var depths = ComputeNodeDepths();
            var depthRows = new System.Collections.Generic.Dictionary<int, int>();

            for (int n = 0; n < _data.Nodes.Count; n++)
            {
                var node = _data.Nodes[n];
                EnsureDialogueGraphIntegrity(node);
                StorySceneData.EnsureNodeLists(node);

                int depth = depths.TryGetValue(node.NodeId, out var dd) ? dd : 0;
                int row = depthRows.TryGetValue(depth, out var rr) ? rr : 0;
                depthRows[depth] = row + 1;

                node.GraphPosX = 60f + depth * 1250f;
                node.GraphPosY = 120f + row * 560f;

                float cardX = node.GraphPosX + 20f;
                float cardY = node.GraphPosY + 50f;
                for (int d = 0; d < node.Dialogues.Count; d++)
                {
                    node.Dialogues[d].GraphPosX = cardX;
                    node.Dialogues[d].GraphPosY = cardY;
                    Vector2 s = GetDialogueCardSize(node.Dialogues[d]);
                    cardX += s.x + 18f;
                }

                float evtX = node.GraphPosX + 20f;
                float evtY = cardY + 220f;
                for (int e = 0; e < node.Events.Count; e++)
                {
                    node.Events[e].GraphPosX = evtX;
                    node.Events[e].GraphPosY = evtY;
                    Vector2 s = GetEventCardSize(node.Events[e]);
                    evtX += s.x + 18f;
                }

                StorySceneData.EnsureNodeLists(node);
            }

            FocusAllNodes(canvasRect);
        }

        private void AutoLayoutMosaic(Rect? canvasRect = null)
        {
            // Mosaïque compacte : tasse les FRAMES de nœuds (layout interne intact : chaque
            // nœud + ses cartes sont translatés en bloc) en lignes qui tiennent à l'écran.
            // But : voir TOUS les nœuds à zoom lisible (~60%+) au lieu du spread narratif 4000px.
            // ↺ Auto-Disposition restaure le flux narratif large.
            if (_data.Nodes.Count == 0) return;
            Vector2 view = ResolveViewSize(canvasRect);
            float rowMaxW = Mathf.Max(1200f, (view.x - 120f) / 0.6f);
            const float gapX = 48f, gapY = 40f, startX = 60f, startY = 60f;

            float x = startX, y = startY, rowH = 0f;
            var placed = new System.Collections.Generic.List<Rect>();
            for (int n = 0; n < _data.Nodes.Count; n++)
            {
                var node = _data.Nodes[n];
                EnsureDialogueGraphIntegrity(node);
                StorySceneData.EnsureNodeLists(node);
                // Assainit d'abord : une coordonnée NaN/Infinity (drag corrompu, JSON édité à la
                // main) empilait les frames au même endroit → nœud invisible sous un autre.
                node.GraphPosX = SanitizeCoord(node.GraphPosX, x);
                node.GraphPosY = SanitizeCoord(node.GraphPosY, y);
                for (int d = 0; d < node.Dialogues.Count; d++)
                {
                    node.Dialogues[d].GraphPosX = SanitizeCoord(node.Dialogues[d].GraphPosX, x + 20f);
                    node.Dialogues[d].GraphPosY = SanitizeCoord(node.Dialogues[d].GraphPosY, y + 50f);
                }
                for (int e = 0; e < node.Events.Count; e++)
                {
                    node.Events[e].GraphPosX = SanitizeCoord(node.Events[e].GraphPosX, x + 20f);
                    node.Events[e].GraphPosY = SanitizeCoord(node.Events[e].GraphPosY, y + 300f);
                }
                Rect b = ComputeNodeBoundingBox(node);
                if (b.width <= 0f || b.height <= 0f) continue;
                if (x + b.width > startX + rowMaxW && x > startX)
                {
                    x = startX;
                    y += rowH + gapY;
                    rowH = 0f;
                }
                // Anti-recouvrement : si la frame chevauche une frame déjà posée (données
                // tassées par un drag ou un layout précédent), glisse à droite jusqu'au jour.
                // Sans ça, un nœud rendu sous un autre opaque est perçu comme "manquant".
                Rect candidate = new Rect(x, y, b.width, b.height);
                bool moved;
                int guard = 0;
                do
                {
                    moved = false;
                    for (int p = 0; p < placed.Count && guard < 64; p++)
                    {
                        if (placed[p].Overlaps(candidate))
                        {
                            candidate.x = placed[p].xMax + gapX;
                            if (candidate.x + candidate.width > startX + rowMaxW && candidate.x > startX)
                            {
                                candidate.x = startX;
                                candidate.y += rowH + gapY;
                            }
                            moved = true;
                            guard++;
                            break;
                        }
                    }
                } while (moved);
                x = candidate.x;
                y = candidate.y;
                Vector2 delta = new Vector2(x - b.xMin, y - b.yMin);
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
                placed.Add(new Rect(x, y, b.width, b.height));
                x += b.width + gapX;
                rowH = Mathf.Max(rowH, b.height);
            }

            // Déclencheurs autonomes : assainis puis dégagés sous les nœuds en cas de recouvrement.
            if (_data.Triggers != null)
            {
                for (int t = 0; t < _data.Triggers.Count; t++)
                {
                    var trg = _data.Triggers[t];
                    if (trg == null) continue;
                    trg.GraphPosX = SanitizeCoord(trg.GraphPosX, startX);
                    trg.GraphPosY = SanitizeCoord(trg.GraphPosY, startY);
                    Vector2 tsize = GetTriggerCardSize(trg);
                    Rect tr = new Rect(trg.GraphPosX, trg.GraphPosY, tsize.x, tsize.y);
                    int guard = 0;
                    bool moved;
                    do
                    {
                        moved = false;
                        for (int p = 0; p < placed.Count && guard < 64; p++)
                        {
                            if (placed[p].Overlaps(tr))
                            {
                                tr.y = placed[p].yMax + gapY;
                                moved = true;
                                guard++;
                                break;
                            }
                        }
                    } while (moved);
                    trg.GraphPosX = tr.x;
                    trg.GraphPosY = tr.y;
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
        private const float TrigCardDefaultW = 340f;
        private const float TrigCardDefaultH = 215f;
        private static float DlgCardDefaultH(int choiceCount) => 215f + choiceCount * 28f;

        private static Vector2 GetDialogueCardSize(SceneDialogueLineData line)
        {
            if (line == null) return new Vector2(DlgCardDefaultW, DlgCardDefaultH(0));
            float w = line.CardWidth > 100f ? line.CardWidth : DlgCardDefaultW;
            float h = line.CardHeight > 80f ? line.CardHeight : DlgCardDefaultH(line.Choices != null ? line.Choices.Count : 0);
            // Hauteur minimale pour que le contenu GUI absolu ne déborde jamais de la carte.
            int cc = line.Choices != null ? line.Choices.Count : 0;
            float needed = 132f + cc * 22f;
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
            // Empêche de perdre le contenu hors cadre : on garde toujours ≥120px (X) / 80px (Y)
            // de la bounding box globale visibles dans le canvas, en zoom in comme en zoom out.
            if (!GetSceneContentBounds(out float minX, out float minY, out float maxX, out float maxY)) return;
            Vector2 view = ResolveViewSize(canvasRect);
            const float keepX = 120f;
            const float keepY = 80f;
            float loX = keepX - maxX * _zoom;
            float hiX = view.x - keepX - minX * _zoom;
            float loY = keepY - maxY * _zoom;
            float hiY = view.y - keepY - minY * _zoom;
            if (loX > hiX) { float m = (loX + hiX) * 0.5f; loX = hiX = m; }
            if (loY > hiY) { float m = (loY + hiY) * 0.5f; loY = hiY = m; }
            _graphPan.x = Mathf.Clamp(_graphPan.x, loX, hiX);
            _graphPan.y = Mathf.Clamp(_graphPan.y, loY, hiY);
        }

        private void CenterViewOnNode(SceneNodeData node, Rect? canvasRect = null)
        {
            if (node == null) return;
            Rect box = ComputeNodeBoundingBox(node);
            Vector2 view = ResolveViewSize(canvasRect);
            _lastCanvasSize = view;
            // Zoom adaptatif : 100% si le nœud tient dans la vue, sinon dézoom juste assez
            // pour l'encadrer ENTIÈREMENT (fini le nœud coupé au bord du cadre en zoom in).
            float fitZX = (view.x - 60f) / Mathf.Max(1f, box.width);
            float fitZY = (view.y - 60f) / Mathf.Max(1f, box.height);
            _zoom = Mathf.Clamp(Mathf.Min(1.0f, fitZX, fitZY), 0.12f, 1.0f);
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

            float targetZoomX = (view.x - 120f) / width;
            float targetZoomY = (view.y - 120f) / height;
            // Vue d'ensemble : on cadre VRAIMENT tout (borne basse 0.12 pour les grandes scènes
            // de 4000px). C'est un mode "aperçu" volontairement petit : cliquer Aller à / ◀ ▶
            // pour éditer un nœud à zoom lisible.
            _zoom = Mathf.Clamp(Mathf.Min(targetZoomX, targetZoomY), 0.12f, 1.0f);

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

        private static Rect ComputeNodeBoundingBox(SceneNodeData node)
        {
            float nx = SanitizeCoord(node.GraphPosX, 60f);
            float ny = SanitizeCoord(node.GraphPosY, 60f);
            float minX = nx;
            float minY = ny;
            float maxX = nx + 420f;
            float maxY = ny + 200f;

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

            if (!hasEntries)
            {
                float customH = 80f + (node.Choices.Count * 36f);
                return new Rect(node.GraphPosX, node.GraphPosY, 500f, Mathf.Max(180f, customH));
            }

            // Marges resserrées et compactes (pad 14 px)
            float pad = 14f;
            float topHeaderH = 34f;
            Rect r = new Rect(minX - pad, minY - topHeaderH - pad, (maxX - minX) + pad * 2f, (maxY - minY) + topHeaderH + pad * 2f);
            // Largeur minimale = header complet (titre + ID + 3 boutons ≈ 490px) : en dessous,
            // les boutons "+ Événement" se retrouvaient coupés au bord du cadre.
            if (r.width < 500f) r.width = 500f;
            return r;
        }

        private static float SanitizeCoord(float v, float fallback)
        {
            return (float.IsNaN(v) || float.IsInfinity(v)) ? fallback : Mathf.Clamp(v, -20000f, 20000f);
        }

        private readonly Dictionary<string, Vector2> _cachedNodeInputSockets = new();

        private void DrawNodeGraphCanvas(Rect canvasRect)
        {
            _lastCanvasSize = new Vector2(canvasRect.width, canvasRect.height);
            _hasHoverCard = false;
            GUI.Box(canvasRect, GUIContent.none);

            Event evt = Event.current;
            Vector2 mouseLocal = evt.mousePosition - canvasRect.min;
            Vector2 mouseWorld = (mouseLocal - _graphPan) / _zoom;

            // Zoom multiplicatif centré sur le curseur
            if (evt.type == EventType.ScrollWheel && canvasRect.Contains(evt.mousePosition))
            {
                float zoomFactor = evt.delta.y > 0 ? 0.88f : 1.14f;
                float oldZoom = _zoom;
                _zoom = Mathf.Clamp(_zoom * zoomFactor, 0.12f, 2.0f);
                _graphPan = mouseLocal - (mouseLocal - _graphPan) * (_zoom / oldZoom);
                ClampPanToContent(canvasRect);
                evt.Use();
            }

            // Panoramique fluide (Clic droit et molette universels ; Clic gauche uniquement sur le fond vide)
            bool isOverInteractiveElement = (_draggingCardKey != null || _draggingNodeId != null || _resizingCardKey != null || _wireDraft.IsActive || IsMouseOverAnyNodeOrCard(mouseWorld));
            if (evt.type == EventType.MouseDown && canvasRect.Contains(evt.mousePosition))
            {
                if (evt.button == 1 || evt.button == 2 || (evt.button == 0 && !isOverInteractiveElement))
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

            // Filet de sécurité : même après un drag de nœud/carte, le contenu reste joignable.
            ClampPanToContent(canvasRect);

            DrawGraphGridBackground(canvasRect);

            // GUI.matrix transforme AUSSI les rectangles de clipping d'IMGUI. Le viewport
            // doit donc être exprimé à l'échelle inverse, et le pan injecté dans le scroll
            // offset de BeginClip. Ainsi, après la matrice de zoom, les bords du clip restent
            // exactement ceux du canvas : aucune frame/carte ne peut sortir de l'éditeur.
            float inverseZoom = 1f / Mathf.Max(0.01f, _zoom);
            Rect zoomInvariantClip = new Rect(
                canvasRect.x * inverseZoom,
                canvasRect.y * inverseZoom,
                canvasRect.width * inverseZoom,
                canvasRect.height * inverseZoom);
            GUI.BeginClip(zoomInvariantClip, _graphPan * inverseZoom, Vector2.zero, false);
            Matrix4x4 oldMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(_zoom, _zoom, 1f)) * GUI.matrix;

            _cachedInputSockets.Clear();
            _cachedNodeInputSockets.Clear();
            _cachedTriggerOutSockets.Clear();

            // 1. Enregistrement spatial de TOUS les nœuds et cartes de la scène
            for (int n = 0; n < _data.Nodes.Count; n++)
            {
                var node = _data.Nodes[n];
                EnsureDialogueGraphIntegrity(node);
                StorySceneData.EnsureNodeLists(node);

                // Assainissement live : une coordonnée NaN/Infinity (drag interrompu, JSON
                // édité à la main) rend carte/frame invisibles sans erreur → nœud "manquant".
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
            }

            // 1b. Enregistrement spatial des Déclencheurs autonomes (niveau scène :
            // sortie seule vers l'entrée d'un nœud, sans port d'entrée).
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

            // 2. Rendu des Super-Cartes de Nœuds. Unity IMGUI laisse encore quelques
            // pixels s'échapper du clip quand une matrice zoomée rencontre le bord gauche.
            // Ne rendre que les nœuds entièrement dans le viewport ferme cette dernière
            // échappée (et un glissement/pan les ramène dès qu'ils rentrent dans le cadre).
            for (int n = 0; n < _data.Nodes.Count; n++)
            {
                if (IsWorldRectFullyVisible(ComputeNodeBoundingBox(_data.Nodes[n]), visibleWorld))
                    DrawNodeFrame(_data.Nodes[n], n, mouseWorld);
            }

            // 3. Rendu de TOUS les Câbles Bézier Connectés (Intra-Nœud & Macro Inter-Nœuds)
            DrawGlobalSceneWires(visibleWorld);

            // 4. Câble temporaire interactif lors d'un drag de connexion
            if (_wireDraft.IsActive && IsWorldPointVisible(_wireDraft.StartPos, visibleWorld) && IsWorldPointVisible(mouseWorld, visibleWorld))
            {
                DrawBezierWire(_wireDraft.StartPos, mouseWorld, Color.yellow, 3.5f);
                if (evt.type == EventType.MouseUp && evt.button == 0)
                {
                    FinalizeGlobalWireDraft(mouseWorld);
                    _wireDraft.IsActive = false;
                    evt.Use();
                }
            }

            // 5. Rendu des Cartes Enfants (Dialogues, Événements et Déclencheurs de tous les nœuds)
            for (int n = 0; n < _data.Nodes.Count; n++)
            {
                var node = _data.Nodes[n];
                if (!IsWorldRectFullyVisible(ComputeNodeBoundingBox(node), visibleWorld)) continue;
                for (int d = 0; d < node.Dialogues.Count; d++)
                {
                    DrawDialogueCard(node.Dialogues[d], d, node, mouseWorld);
                }
                for (int e = 0; e < node.Events.Count; e++)
                {
                    DrawEventCard(node.Events[e], e, node, mouseWorld);
                }
            }

            // 5b. Rendu des Déclencheurs autonomes (niveau scène, sortie seule).
            if (_data.Triggers != null)
            {
                for (int t = 0; t < _data.Triggers.Count; t++)
                {
                    var trg = _data.Triggers[t];
                    if (trg == null) continue;
                    Vector2 tsize = GetTriggerCardSize(trg);
                    if (!visibleWorld.Overlaps(new Rect(trg.GraphPosX, trg.GraphPosY, tsize.x, tsize.y))) continue;
                    DrawTriggerCard(trg, t, mouseWorld);
                }
            }

            GUI.matrix = oldMatrix;
            GUI.EndClip();

            // Superposition HUD de Zoom & Contrôles
            DrawZoomControlsOverlay(canvasRect);

            // Inspecteur du nœud focalisé (optionnel, dessiné hors zoom).
            if (_showNodeInspector) DrawNodeInspectorOverlay(canvasRect);

            // Infobulle de survol : texte à taille normale, ancrée au curseur, calée au canvas.
            if (_hasHoverCard && Event.current.type == EventType.Repaint)
            {
                DrawCardHoverTooltip(canvasRect);
            }
        }

        private bool _showNodeInspector = false;
        private Vector2 _inspectorScroll;

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
                if (!IsWorldRectFullyVisible(nodeBox, visibleWorld)) continue;

                // Câblage macro inter-nœuds (NextNodeId direct du nœud)
                if (!string.IsNullOrEmpty(node.NextNodeId)
                    && IsNodeFullyVisible(node.NextNodeId, visibleWorld)
                    && _cachedNodeInputSockets.TryGetValue(node.NextNodeId, out var targetNodeIn))
                {
                    Vector2 nodeOut = new Vector2(nodeBox.xMax, nodeBox.yMin + 18f);
                    DrawBezierWire(nodeOut, targetNodeIn, new Color(1.0f, 0.6f, 0.1f, 0.95f), 4.5f);
                }

                // Câblage des choix majeurs du nœud vers les nœuds de destination (alignement exact sur les sockets de choix)
                for (int c = 0; c < node.Choices.Count; c++)
                {
                    var choice = node.Choices[c];
                    if (!string.IsNullOrEmpty(choice.NextNodeId)
                        && IsNodeFullyVisible(choice.NextNodeId, visibleWorld)
                        && _cachedNodeInputSockets.TryGetValue(choice.NextNodeId, out var choiceNodeIn))
                    {
                        float choiceRowY = nodeBox.yMin + 60f + (c * 36f);
                        Vector2 choiceOut = new Vector2(nodeBox.xMax, choiceRowY + 14f);
                        Color choiceWireCol = choice.Id.Contains("hostile") ? new Color(1.0f, 0.35f, 0.35f, 0.95f) : new Color(0.2f, 0.92f, 0.45f, 0.95f);
                        DrawBezierWire(choiceOut, choiceNodeIn, choiceWireCol, 3.5f);
                    }
                }

                // Câblage intra-nœud des dialogues enfants
                DrawConnectedGraphWires(node, visibleWorld);
            }

            // Câblage des Déclencheurs autonomes : sortie seule vers l'entrée du nœud cible.
            if (_data.Triggers != null)
            {
                for (int t = 0; t < _data.Triggers.Count; t++)
                {
                    var trg = _data.Triggers[t];
                    if (trg == null || string.IsNullOrEmpty(trg.TargetNodeId)) continue;
                    if (_cachedTriggerOutSockets.TryGetValue(trg.TriggerId, out var trgOut)
                        && _cachedNodeInputSockets.TryGetValue(trg.TargetNodeId, out var targetIn)
                        && IsWorldPointVisible(trgOut, visibleWorld)
                        && IsWorldPointVisible(targetIn, visibleWorld))
                    {
                        DrawBezierWire(trgOut, targetIn, new Color(1.0f, 0.85f, 0.25f, 0.95f), 3.5f);
                    }
                }
            }
        }

        private bool IsNodeFullyVisible(string nodeId, Rect visibleWorld)
        {
            for (int i = 0; i < _data.Nodes.Count; i++)
            {
                var candidate = _data.Nodes[i];
                if (candidate.NodeId == nodeId)
                    return IsWorldRectFullyVisible(ComputeNodeBoundingBox(candidate), visibleWorld);
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

            // 1. Test de collision avec les sockets de dialogue/événement
            foreach (var kvp in _cachedInputSockets)
            {
                if (Vector2.Distance(kvp.Value, mousePos) < 32f)
                {
                    targetId = kvp.Key;
                    break;
                }
            }

            // 2. Test de collision avec les sockets de Nœud complet
            if (string.IsNullOrEmpty(targetId))
            {
                foreach (var kvp in _cachedNodeInputSockets)
                {
                    if (Vector2.Distance(kvp.Value, mousePos) < 36f)
                    {
                        targetId = kvp.Key;
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(targetId)) return;

            if (_wireDraft.IsEvent && _wireDraft.SourceEvent != null)
            {
                _wireDraft.SourceEvent.NextEventId = targetId;
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
            // Zone boutons réelle : 90 + 95 + 26 + espacements ≈ 225px calée à droite.
            Rect actionsArea = new Rect(headerRect.xMax - 235f, headerRect.y + 5f, 225f, 26f);
            Rect nodeInSocket = new Rect(frameRect.x - 9f, frameRect.y + 10f, 18f, 18f);
            Rect nodeOutSocket = new Rect(frameRect.xMax - 9f, frameRect.y + 10f, 18f, 18f);

            Event evt = Event.current;
            int nodeControlId = GUIUtility.GetControlID(FocusType.Passive);

            // Glissement du Nœud Englobant dans l'espace monde (protection stricte des boutons d'actions)
            bool isOverHeaderDragZone = headerRect.Contains(mouseWorld) && !actionsArea.Contains(mouseWorld);

            // Double-clic sur la frame = cadrer ce nœud en lisible (raccourci direct vers
            // le focus, sans passer par Aller à / ◀ ▶).
            if (evt.type == EventType.MouseDown && evt.button == 0 && evt.clickCount == 2 && frameRect.Contains(mouseWorld))
            {
                _draggingNodeId = null;
                GUIUtility.hotControl = 0;
                FocusNodeByIndex(nodeIndex);
                evt.Use();
            }
            else if (evt.type == EventType.MouseDown && evt.button == 0 && evt.clickCount < 2 && isOverHeaderDragZone)
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

            // Conteneur d'arrière-plan englobant
            DrawSolidRect(frameRect, new Color(0.025f, 0.04f, 0.065f, 0.75f));
            DrawSolidRect(headerRect, new Color(frameBorderCol.r, frameBorderCol.g, frameBorderCol.b, 0.25f));

            // Bordures rectilignes précises dessinées sans rotation
            DrawSolidRect(new Rect(frameRect.x, frameRect.y, frameRect.width, 2.5f), frameBorderCol);
            DrawSolidRect(new Rect(frameRect.x, frameRect.yMax - 1.5f, frameRect.width, 1.5f), frameBorderCol);
            DrawSolidRect(new Rect(frameRect.x, frameRect.y, 1.5f, frameRect.height), frameBorderCol);
            DrawSolidRect(new Rect(frameRect.xMax - 1.5f, frameRect.y, 1.5f, frameRect.height), frameBorderCol);

            // Port Entrée Macro Nœud
            DrawSolidRect(nodeInSocket, frameBorderCol);

            // Titre, Lieu et Actions de Nœud (largeurs relatives au frame pour éviter tout débordement)
            GUI.color = Color.white;
            float titleW = Mathf.Max(120f, frameRect.width - 380f);
            GUI.Label(new Rect(headerRect.x + 16f, headerRect.y + 8f, titleW, 22f), $"<b>■ NŒUD #{nodeIndex + 1} : {node.Title.ToUpperInvariant()}</b> [{node.Kind}]");

            GUI.color = new Color(1f, 1f, 1f, 0.75f);
            float idLabelW = Mathf.Max(60f, Mathf.Min(220f, frameRect.width - titleW - 260f));
            GUI.Label(new Rect(headerRect.x + 16f + titleW + 4f, headerRect.y + 8f, idLabelW, 20f), $"ID: <i>{node.NodeId}</i>");
            GUI.color = Color.white;

            // Boutons d'ajout directs (GUI absolu : GUILayout est incompatible avec la matrice zoomée)
            float btnY = headerRect.y + 7f;
            float btnH = 22f;
            float bx = headerRect.xMax - 10f;
            Color prevBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.85f, 0.2f, 0.2f);
            Rect delBtn = new Rect(bx - 26f, btnY, 26f, btnH);
            bool deleteNode = GUI.Button(delBtn, "✕");
            bx -= 26f + 6f;
            GUI.backgroundColor = new Color(1f, 0.45f, 0.2f);
            Rect evtBtn = new Rect(bx - 95f, btnY, 95f, btnH);
            bool addEvt = GUI.Button(evtBtn, "+ Événement");
            bx -= 95f + 6f;
            GUI.backgroundColor = new Color(0.2f, 0.75f, 1f);
            Rect dlgBtn = new Rect(bx - 90f, btnY, 90f, btnH);
            bool addDlg = GUI.Button(dlgBtn, "+ Dialogue");
            GUI.backgroundColor = prevBg;
            // (La protection du drag utilise la zone estimée en tête de méthode, volontairement
            // un peu plus large que les boutons pour éviter tout drag accidentel.)

            if (addDlg)
            {
                int nextIdx = node.Dialogues.Count + 1;
                node.Dialogues.Add(new SceneDialogueLineData
                {
                    LineId = $"line_{nodeIndex + 1}_{nextIdx}",
                    SpeakerId = "",
                    Speech = "Nouvelle réplique...",
                    GraphPosX = node.GraphPosX + 40f + (node.Dialogues.Count * 380f),
                    GraphPosY = node.GraphPosY + 70f
                });
            }

            if (addEvt)
            {
                int nextIdx = node.Events.Count + 1;
                node.Events.Add(new SceneEventData
                {
                    EventId = $"evt_{nodeIndex + 1}_{nextIdx}",
                    Title = "Action Scénique",
                    GraphPosX = node.GraphPosX + 40f + (node.Events.Count * 380f),
                    GraphPosY = node.GraphPosY + 320f
                });
            }

            if (deleteNode)
            {
                _data.Nodes.RemoveAt(nodeIndex);
                return;
            }

            // Port Sortie Macro Nœud
            if (node.Choices.Count == 0 && !string.IsNullOrEmpty(node.NextNodeId))
            {
                DrawSolidRect(nodeOutSocket, new Color(1.0f, 0.65f, 0.15f));
            }

            // Affichage spécifique des Choix Majeurs
            if (node.Kind == ScenarioNodeKind.Choice && node.Choices.Count > 0)
            {
                Rect choicesPanel = new Rect(frameRect.x + 24f, frameRect.y + 60f, frameRect.width - 48f, node.Choices.Count * 36f);
                for (int c = 0; c < node.Choices.Count; c++)
                {
                    var ch = node.Choices[c];
                    Rect choiceRow = new Rect(choicesPanel.x, choicesPanel.y + (c * 36f), choicesPanel.width, 30f);
                    DrawSolidRect(choiceRow, new Color(0.08f, 0.12f, 0.17f, 0.90f));

                    GUI.Label(new Rect(choiceRow.x + 10f, choiceRow.y + 5f, choiceRow.width - 250f, 20f), $"<b>Choix #{c + 1} :</b> {ch.Label}");
                    GUI.color = new Color(1f, 0.75f, 0.2f);
                    GUI.Label(new Rect(choiceRow.xMax - 240f, choiceRow.y + 5f, 230f, 20f), $"➔ Cible: <b>{ch.NextNodeId}</b>");
                    GUI.color = Color.white;

                    Rect choiceSocket = new Rect(frameRect.xMax - 8f, choiceRow.y + 7f, 16f, 16f);
                    DrawSolidRect(choiceSocket, ch.Id.Contains("hostile") ? new Color(1.0f, 0.35f, 0.35f) : new Color(0.2f, 0.92f, 0.45f));
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
            Vector2 wMin = (Vector2.zero - _graphPan) / _zoom;
            Vector2 wMax = (new Vector2(canvasRect.width, canvasRect.height) - _graphPan) / _zoom;
            return Rect.MinMaxRect(wMin.x, wMin.y, wMax.x, wMax.y);
        }

        private static bool IsWorldRectFullyVisible(Rect content, Rect viewport)
        {
            const float edgeTolerance = 0.5f;
            return content.xMin >= viewport.xMin - edgeTolerance
                && content.yMin >= viewport.yMin - edgeTolerance
                && content.xMax <= viewport.xMax + edgeTolerance
                && content.yMax <= viewport.yMax + edgeTolerance;
        }

        private static bool IsWorldPointVisible(Vector2 point, Rect viewport)
        {
            return point.x >= viewport.xMin && point.x <= viewport.xMax
                && point.y >= viewport.yMin && point.y <= viewport.yMax;
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

                // Sortie séquentielle directe
                if (!string.IsNullOrEmpty(line.NextLineId)
                    && IsInputFullyVisible(line.NextLineId, visibleWorld)
                    && _cachedInputSockets.TryGetValue(line.NextLineId, out var targetIn))
                {
                    Vector2 outSocket = new Vector2(cardPos.x + size.x, cardPos.y + 26f);
                    DrawBezierWire(outSocket, targetIn, new Color(0.0f, 0.85f, 1.0f, 0.95f), 3.5f);
                }

                // Sorties des choix multiples (alignées sur les ports GUI absolus : première rangée à +100, pas de 22)
                for (int c = 0; c < line.Choices.Count; c++)
                {
                    var ch = line.Choices[c];
                    if (!string.IsNullOrEmpty(ch.NextLineId)
                        && IsInputFullyVisible(ch.NextLineId, visibleWorld)
                        && _cachedInputSockets.TryGetValue(ch.NextLineId, out var chTargetIn))
                    {
                        Vector2 chOutSocket = new Vector2(cardPos.x + size.x, cardPos.y + 110f + (c * 22f));
                        DrawBezierWire(chOutSocket, chTargetIn, new Color(1.0f, 0.8f, 0.2f, 0.95f), 3.0f);
                    }
                }

                // Sorties de test automatique
                if (line.AutoSkillCheck != null && line.AutoSkillCheck.HasAutoCheck)
                {
                    if (!string.IsNullOrEmpty(line.AutoSkillCheck.SuccessNextLineId)
                        && IsInputFullyVisible(line.AutoSkillCheck.SuccessNextLineId, visibleWorld)
                        && _cachedInputSockets.TryGetValue(line.AutoSkillCheck.SuccessNextLineId, out var sIn))
                    {
                        DrawBezierWire(new Vector2(cardPos.x + size.x, cardPos.y + 70f), sIn, Color.green, 2.5f);
                    }
                    if (!string.IsNullOrEmpty(line.AutoSkillCheck.FailureNextLineId)
                        && IsInputFullyVisible(line.AutoSkillCheck.FailureNextLineId, visibleWorld)
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
                    && IsInputFullyVisible(ev.NextEventId, visibleWorld)
                    && _cachedInputSockets.TryGetValue(ev.NextEventId, out var targetIn))
                {
                    Vector2 outSocket = new Vector2(cardPos.x + size.x, cardPos.y + 26f);
                    DrawBezierWire(outSocket, targetIn, new Color(0.85f, 0.45f, 1.0f, 0.95f), 3.5f);
                }
            }
        }

        private bool IsInputFullyVisible(string inputId, Rect visibleWorld)
        {
            for (int n = 0; n < _data.Nodes.Count; n++)
            {
                var node = _data.Nodes[n];
                if (!IsWorldRectFullyVisible(ComputeNodeBoundingBox(node), visibleWorld)) continue;

                for (int d = 0; d < node.Dialogues.Count; d++)
                    if (node.Dialogues[d].LineId == inputId) return true;
                for (int e = 0; e < node.Events.Count; e++)
                    if (node.Events[e].EventId == inputId) return true;
            }
            return false;
        }

        private void DrawDialogueCard(SceneDialogueLineData line, int index, SceneNodeData node, Vector2 mouseWorld)
        {
            if (string.IsNullOrEmpty(line.LineId)) line.LineId = $"line_{index + 1}";
            if (line.Choices == null) line.Choices = new System.Collections.Generic.List<SceneDialogueChoiceData>();
            if (line.Ambience == null) line.Ambience = new SceneAmbienceData();
            if (line.Prerequisite == null) line.Prerequisite = new ScenePrerequisiteData();

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
            if (evt.type == EventType.MouseDown && evt.button == 0 && resizeGripRect.Contains(mouseWorld))
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
            if (evt.type == EventType.MouseDown && evt.button == 0 && dragHeaderRect.Contains(mouseWorld))
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
            DrawSolidRect(cardRect, new Color(0.08f, 0.11f, 0.15f, 0.95f));
            Color headerCol = line.Choices.Count > 0 ? new Color(0.35f, 0.24f, 0.05f) : (line.AutoSkillCheck != null && line.AutoSkillCheck.HasAutoCheck ? new Color(0.25f, 0.12f, 0.35f) : new Color(0.08f, 0.28f, 0.36f));
            DrawSolidRect(headerRect, headerCol);

            // Bordure de carte nette
            DrawSolidRect(new Rect(cardRect.x, cardRect.y, cardRect.width, 1f), headerCol * 1.4f);
            DrawSolidRect(new Rect(cardRect.x, cardRect.yMax - 1f, cardRect.width, 1f), new Color(1f, 1f, 1f, 0.08f));
            DrawSolidRect(new Rect(cardRect.x, cardRect.y, 1f, cardRect.height), new Color(1f, 1f, 1f, 0.08f));
            DrawSolidRect(new Rect(cardRect.xMax - 1f, cardRect.y, 1f, cardRect.height), new Color(1f, 1f, 1f, 0.08f));

            // 2. Port d'Entrée
            Rect inPort = new Rect(cardRect.x - 7f, cardRect.y + 19f, 14f, 14f);
            DrawSolidRect(inPort, Color.cyan);

            // 3. Titre & Locuteur
            GUI.Label(new Rect(headerRect.x + 8f, headerRect.y + 3f, cardW - 35f, 20f), $"<b>◆ {line.SpeakerId}</b> (<i>{line.LineId}</i>)");

            // 4. Bouton Supprimer
            GUI.backgroundColor = new Color(0.85f, 0.22f, 0.22f, 1f);
            GUI.color = Color.white;
            if (GUI.Button(closeRect, "✕"))
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
            GUI.Label(resizeGripRect, "◢");
            GUI.color = Color.white;

            // Survol du header : mémorise une infobulle lisible (dessinée hors zoom après EndGroup).
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
                        sb.AppendLine($"• {hl}");
                    }
                }
                else if (!string.IsNullOrEmpty(line.NextLineId))
                {
                    sb.AppendLine($"➔ {line.NextLineId}");
                }
                _hasHoverCard = true;
                _hoverTitle = $"◆ {line.SpeakerId}  ({line.LineId})";
                _hoverBody = sb.ToString().Trim();
                _hoverScreenPos = Event.current.mousePosition;
            }

            // 6. Contenu ÉDITABLE en GUI ABSOLU (pas de GUILayout : incompatible avec la matrice zoomée,
            //    c'était la cause du rendu écrasé/chevauché du screenshot).
            float innerX = cardRect.x + 8f;
            float innerW = cardW - 16f;
            float y = cardRect.y + 30f;

            GUI.Label(new Rect(innerX, y, 22f, 18f), "ID:");
            line.LineId = GUI.TextField(new Rect(innerX + 24f, y, 70f, 18f), line.LineId ?? "");
            GUI.Label(new Rect(innerX + 98f, y, 28f, 18f), "Loc:");
            line.SpeakerId = GUI.TextField(new Rect(innerX + 128f, y, 80f, 18f), line.SpeakerId ?? "");
            line.StageDirection = GUI.TextField(new Rect(innerX + 212f, y, Mathf.Max(40f, innerW - 212f), 18f), line.StageDirection ?? "");
            y += 20f;

            line.Speech = GUI.TextArea(new Rect(innerX, y, innerW, 48f), line.Speech ?? "");
            y += 50f;

            // Choix Multiples / Liaison : première rangée à cardY+100, pas vertical de 22 (référence des wires).
            if (line.Choices.Count > 0)
            {
                for (int c = 0; c < line.Choices.Count; c++)
                {
                    var ch = line.Choices[c];
                    float labelW = Mathf.Max(60f, innerW - 124f);
                    ch.Label = GUI.TextField(new Rect(innerX, y, labelW, 20f), ch.Label ?? "");
                    GUI.Label(new Rect(innerX + labelW + 2f, y, 16f, 20f), "➔");
                    ch.NextLineId = GUI.TextField(new Rect(innerX + labelW + 20f, y, 80f, 20f), ch.NextLineId ?? "");
                    if (GUI.Button(new Rect(innerX + labelW + 102f, y, 20f, 20f), "⊘")) ch.NextLineId = "";
                    y += 22f;
                }
            }
            else
            {
                GUI.Label(new Rect(innerX, y, 65f, 20f), "Liaison ➔ :");
                line.NextLineId = GUI.TextField(new Rect(innerX + 67f, y, Mathf.Max(60f, innerW - 67f - 26f), 20f), line.NextLineId ?? "");
                if (GUI.Button(new Rect(innerX + innerW - 22f, y, 22f, 20f), "⊘")) line.NextLineId = "";
                y += 22f;
            }

            // Rangée basse : +Choix / toggles (protégée du débordement par CardHeight auto).
            if (y + 20f <= cardRect.yMax - 4f)
            {
                if (GUI.Button(new Rect(innerX, y, 64f, 18f), "+ Choix"))
                {
                    line.Choices.Add(new SceneDialogueChoiceData { ChoiceId = $"ch_{line.Choices.Count + 1}", Label = "Option..." });
                }
                line.Ambience.HasAmbience = GUI.Toggle(new Rect(innerX + 68f, y, 90f, 18f), line.Ambience.HasAmbience, "Ambiance");
                line.Prerequisite.HasPrerequisite = GUI.Toggle(new Rect(innerX + 160f, y, Mathf.Max(60f, innerW - 160f), 18f), line.Prerequisite.HasPrerequisite, "Prérequis");
            }
            else if (GUI.Button(new Rect(innerX, cardRect.yMax - 22f, 64f, 18f), "+ Choix"))
            {
                line.Choices.Add(new SceneDialogueChoiceData { ChoiceId = $"ch_{line.Choices.Count + 1}", Label = "Option..." });
            }

            // Port de Sortie Principal
            Rect outPort = new Rect(cardRect.xMax - 8f, cardRect.y + 18f, 16f, 16f);
            DrawSolidRect(outPort, new Color(0f, 0.85f, 1f));

            if (evt.type == EventType.MouseDown && evt.button == 0 && outPort.Contains(mouseWorld))
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

            // Ports de Sortie des Choix (alignés sur les rangées GUI : +100, pas 22)
            for (int c = 0; c < line.Choices.Count; c++)
            {
                var ch = line.Choices[c];
                Rect chOutPort = new Rect(cardRect.xMax - 7f, cardRect.y + 103f + (c * 22f), 14f, 14f);
                DrawSolidRect(chOutPort, new Color(1f, 0.8f, 0.2f));

                if (evt.type == EventType.MouseDown && evt.button == 0 && chOutPort.Contains(mouseWorld))
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
            if (evt.type == EventType.MouseDown && evt.button == 0 && resizeGripRect.Contains(mouseWorld))
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
            if (evt.type == EventType.MouseDown && evt.button == 0 && dragHeaderRect.Contains(mouseWorld))
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
            DrawSolidRect(cardRect, new Color(0.12f, 0.08f, 0.12f, 0.95f));
            DrawSolidRect(headerRect, new Color(0.40f, 0.15f, 0.15f));

            // Bordures
            DrawSolidRect(new Rect(cardRect.x, cardRect.y, cardRect.width, 1f), new Color(0.85f, 0.3f, 0.3f, 0.8f));
            DrawSolidRect(new Rect(cardRect.x, cardRect.yMax - 1f, cardRect.width, 1f), new Color(1f, 1f, 1f, 0.08f));
            DrawSolidRect(new Rect(cardRect.x, cardRect.y, 1f, cardRect.height), new Color(1f, 1f, 1f, 0.08f));
            DrawSolidRect(new Rect(cardRect.xMax - 1f, cardRect.y, 1f, cardRect.height), new Color(1f, 1f, 1f, 0.08f));

            // 2. Port d'Entrée
            Rect inPort = new Rect(cardRect.x - 7f, cardRect.y + 19f, 14f, 14f);
            DrawSolidRect(inPort, new Color(0.85f, 0.4f, 1f));

            // 3. Titre & Nature de l'Événement
            GUI.Label(new Rect(headerRect.x + 8f, headerRect.y + 3f, cardW - 35f, 20f), $"<b>▲ {ev.Title}</b> [{ev.Kind}]");

            // 4. Bouton Supprimer
            GUI.backgroundColor = new Color(0.85f, 0.22f, 0.22f, 1f);
            GUI.color = Color.white;
            if (GUI.Button(closeRect, "✕"))
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
            GUI.Label(resizeGripRect, "◢");
            GUI.color = Color.white;

            // Survol du header : infobulle lisible (cf. cartes dialogue).
            if (_zoom < 0.85f && !_isPanning && _draggingCardKey == null && _resizingCardKey == null && !_wireDraft.IsActive
                && headerRect.Contains(mouseWorld))
            {
                _hasHoverCard = true;
                _hoverTitle = $"▲ {ev.Title}  ({ev.EventId}) [{ev.Kind}]";
                _hoverBody = string.IsNullOrEmpty(ev.Description) ? "(aucune description)" : ev.Description;
                _hoverScreenPos = Event.current.mousePosition;
            }

            // Contenu en GUI ABSOLU (même raison que les cartes dialogue : GUILayout + zoom = rendu cassé).
            float innerX = cardRect.x + 8f;
            float innerW = cardW - 16f;
            float y = cardRect.y + 30f;

            GUI.Label(new Rect(innerX, y, 22f, 18f), "ID:");
            ev.EventId = GUI.TextField(new Rect(innerX + 24f, y, 70f, 18f), ev.EventId ?? "");
            GUI.Label(new Rect(innerX + 98f, y, 35f, 18f), "Titre:");
            ev.Title = GUI.TextField(new Rect(innerX + 135f, y, Mathf.Max(40f, innerW - 135f), 18f), ev.Title ?? "");
            y += 20f;

            GUI.Label(new Rect(innerX, y, 45f, 18f), "Action:");
            ev.Kind = (SceneEventKind)GUI.Toolbar(new Rect(innerX + 47f, y, Mathf.Max(80f, innerW - 47f), 18f), (int)ev.Kind, new[] { "Normal", "Combat", "Spawn", "Cam" });
            y += 22f;

            Color prevBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(1f, 0.7f, 0.2f);
            if (GUI.Button(new Rect(innerX, y, innerW, 20f), "▲ Tester en Direct"))
            {
                var controller = FindAnyObjectByType<Scenes.JsonStorySceneController>();
                controller?.ExecuteScenicEvent(ev);
            }
            GUI.backgroundColor = prevBg;

            Rect outPort = new Rect(cardRect.xMax - 7f, cardRect.y + 19f, 14f, 14f);
            DrawSolidRect(outPort, new Color(0.85f, 0.4f, 1f));

            if (evt.type == EventType.MouseDown && evt.button == 0 && outPort.Contains(mouseWorld))
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

            if (evt.type == EventType.MouseDown && evt.button == 0 && resizeGripRect.Contains(mouseWorld))
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

            if (evt.type == EventType.MouseDown && evt.button == 0 && dragHeaderRect.Contains(mouseWorld))
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

            DrawSolidRect(cardRect, new Color(0.13f, 0.10f, 0.05f, 0.95f));
            Color headerCol = new Color(0.45f, 0.32f, 0.08f);
            DrawSolidRect(headerRect, headerCol);

            DrawSolidRect(new Rect(cardRect.x, cardRect.y, cardRect.width, 1f), new Color(1f, 0.85f, 0.25f, 0.8f));
            DrawSolidRect(new Rect(cardRect.x, cardRect.yMax - 1f, cardRect.width, 1f), new Color(1f, 1f, 1f, 0.08f));
            DrawSolidRect(new Rect(cardRect.x, cardRect.y, 1f, cardRect.height), new Color(1f, 1f, 1f, 0.08f));
            DrawSolidRect(new Rect(cardRect.xMax - 1f, cardRect.y, 1f, cardRect.height), new Color(1f, 1f, 1f, 0.08f));

            GUI.Label(new Rect(headerRect.x + 8f, headerRect.y + 3f, cardW - 35f, 20f), $"<b>▼ {trg.Label}</b> [{trg.TriggerId}]");

            GUI.backgroundColor = new Color(0.85f, 0.22f, 0.22f, 1f);
            GUI.color = Color.white;
            if (GUI.Button(closeRect, "✕"))
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
            GUI.Label(resizeGripRect, "◢");
            GUI.color = Color.white;

            if (_zoom < 0.85f && !_isPanning && _draggingCardKey == null && _resizingCardKey == null && !_wireDraft.IsActive
                && headerRect.Contains(mouseWorld))
            {
                _hasHoverCard = true;
                _hoverTitle = $"▼ {trg.Label}  ({trg.TriggerId})";
                _hoverBody = $"{trg.GetSummary()}\n➔ {targetDisplay}";
                _hoverScreenPos = Event.current.mousePosition;
            }

            float innerX = cardRect.x + 8f;
            float innerW = cardW - 16f;
            float y = cardRect.y + 30f;

            GUI.Label(new Rect(innerX, y, 24f, 18f), "ID:");
            trg.TriggerId = GUI.TextField(new Rect(innerX + 26f, y, 100f, 18f), trg.TriggerId ?? "");
            GUI.color = new Color(1f, 0.85f, 0.25f);
            GUI.Label(new Rect(innerX + 130f, y, Mathf.Max(40f, innerW - 130f), 18f), $"➔ {targetDisplay}");
            GUI.color = Color.white;
            y += 20f;

            GUI.Label(new Rect(innerX, y, 42f, 18f), "Titre:");
            trg.Label = GUI.TextField(new Rect(innerX + 44f, y, Mathf.Max(40f, innerW - 44f), 18f), trg.Label ?? "");
            y += 20f;

            trg.Kind = (SceneTriggerKind)GUI.Toolbar(new Rect(innerX, y, innerW, 18f), (int)trg.Kind, new[] { "Action", "Objectif", "Flag", "Acteur" });
            y += 22f;

            if (trg.Kind == SceneTriggerKind.InteractableActivated)
            {
                GUI.Label(new Rect(innerX, y, 95f, 18f), "Action carte :");
                trg.InteractableId = GUI.TextField(new Rect(innerX + 97f, y, Mathf.Max(40f, innerW - 97f), 18f), trg.InteractableId ?? "");
                y += 20f;
            }
            else if (trg.Kind == SceneTriggerKind.ObjectiveCompleted)
            {
                GUI.Label(new Rect(innerX, y, 65f, 18f), "Objectif :");
                trg.ObjectiveId = GUI.TextField(new Rect(innerX + 67f, y, Mathf.Max(40f, innerW - 67f), 18f), trg.ObjectiveId ?? "");
                y += 20f;
            }
            else if (trg.Kind == SceneTriggerKind.CampaignFlagSet)
            {
                GUI.Label(new Rect(innerX, y, 42f, 18f), "Flag :");
                trg.FlagKey = GUI.TextField(new Rect(innerX + 44f, y, 150f, 18f), trg.FlagKey ?? "");
                trg.FlagMustBeSet = GUI.Toggle(new Rect(innerX + 198f, y, Mathf.Max(60f, innerW - 198f), 18f), trg.FlagMustBeSet, "Présent");
                y += 20f;
            }
            else
            {
                GUI.Label(new Rect(innerX, y, 58f, 18f), "Acteur :");
                trg.ActorId = GUI.TextField(new Rect(innerX + 60f, y, 110f, 18f), trg.ActorId ?? "");
                trg.ActorCondition = (SceneActorConditionKind)GUI.Toolbar(new Rect(innerX + 174f, y, Mathf.Max(60f, innerW - 174f), 18f), (int)trg.ActorCondition, new[] { "Statut", "PV < %" });
                y += 22f;
                if (trg.ActorCondition == SceneActorConditionKind.HasStatus)
                {
                    GUI.Label(new Rect(innerX, y, 58f, 18f), "Statut :");
                    trg.StatusName = GUI.TextField(new Rect(innerX + 60f, y, Mathf.Max(40f, innerW - 60f), 18f), trg.StatusName ?? "");
                }
                else
                {
                    GUI.Label(new Rect(innerX, y, 45f, 18f), "PV <");
                    int.TryParse(GUI.TextField(new Rect(innerX + 47f, y, 45f, 18f), trg.HPPercentThreshold.ToString()), out trg.HPPercentThreshold);
                    trg.HPPercentThreshold = Mathf.Clamp(trg.HPPercentThreshold, 1, 100);
                    GUI.Label(new Rect(innerX + 96f, y, Mathf.Max(30f, innerW - 96f), 18f), "% max");
                }
                y += 20f;
            }

            GUI.Label(new Rect(innerX, y, 68f, 18f), "Si nœud :");
            trg.SourceNodeId = GUI.TextField(new Rect(innerX + 70f, y, 120f, 18f), trg.SourceNodeId ?? "");
            trg.OneShot = GUI.Toggle(new Rect(innerX + 194f, y, Mathf.Max(60f, innerW - 194f), 18f), trg.OneShot, "Unique");
            y += 20f;

            if (y + 4f <= cardRect.yMax)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.55f);
                GUI.Label(new Rect(innerX, y, innerW, 18f), trg.GetSummary());
                GUI.color = Color.white;
            }

            // Sortie seule : pas de port d'entrée sur un déclencheur.
            Rect outPort = new Rect(cardRect.xMax - 8f, cardRect.y + 18f, 16f, 16f);
            DrawSolidRect(outPort, new Color(1f, 0.85f, 0.25f));

            if (evt.type == EventType.MouseDown && evt.button == 0 && outPort.Contains(mouseWorld))
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

                if (string.IsNullOrEmpty(line.NextLineId) && (line.Choices == null || line.Choices.Count == 0))
                {
                    if (i < node.Dialogues.Count - 1)
                    {
                        if (string.IsNullOrEmpty(node.Dialogues[i + 1].LineId))
                        {
                            node.Dialogues[i + 1].LineId = $"line_{i + 2}";
                        }
                        line.NextLineId = node.Dialogues[i + 1].LineId;
                    }
                }
            }
        }

        private static void DrawBezierWire(Vector2 start, Vector2 end, Color color, float width = 3.5f)
        {
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

        private static void DrawLine(Vector2 pointA, Vector2 pointB, Color color, float width)
        {
            float dx = pointB.x - pointA.x;
            float dy = pointB.y - pointA.y;
            float length = Mathf.Sqrt(dx * dx + dy * dy);
            if (length < 0.001f) return;

            float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;

            Color savedColor = GUI.color;
            GUI.color = color;

            Matrix4x4 savedMatrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, pointA);
            GUI.DrawTexture(new Rect(pointA.x, pointA.y - width * 0.5f, length + 0.5f, width), PureWhiteTex);
            GUI.matrix = savedMatrix;

            GUI.color = savedColor;
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
                        Label = "Option de réponse du joueur...",
                        ReactionSpeech = "Réaction du personnage..."
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
                    GUILayout.Label("Réponse PNJ :", GUILayout.Width(85));
                    choice.ReactionSpeech = GUILayout.TextField(choice.ReactionSpeech);
                    GUILayout.Label("Didascalie :", GUILayout.Width(75));
                    choice.ReactionStageDirection = GUILayout.TextField(choice.ReactionStageDirection, GUILayout.Width(100));
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
                node.Dialogues.Add(new SceneDialogueLineData { SpeakerId = "", Speech = "..." });
            }

            GUILayout.Space(10);
            DrawNodeEventsEditor(node);
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
                node.Events.Add(new SceneEventData
                {
                    EventId = $"event_{node.Events.Count + 1}",
                    Title = "Nouvel Événement"
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
            var ch = choice.Challenge;
            ch.HasChallenge = GUILayout.Toggle(ch.HasChallenge, "<b>🎲 Défi de Compétence (Jet de Dé & Événements)</b>");
            if (!ch.HasChallenge) return;

            GUILayout.BeginVertical(GUI.skin.box);

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

            // Branche de Succès
            GUILayout.Space(2);
            GUI.backgroundColor = new Color(0.15f, 0.4f, 0.25f);
            GUILayout.BeginVertical(GUI.skin.box);
            GUI.backgroundColor = Color.white;
            GUILayout.Label("<color=#55FF88><b>✔ BRANCHE EN CAS DE SUCCÈS</b></color>");

            GUILayout.BeginHorizontal();
            GUILayout.Label("Réponse PNJ :", GUILayout.Width(90));
            ch.SuccessSpeech = GUILayout.TextField(ch.SuccessSpeech);
            GUILayout.Label("Didascalie :", GUILayout.Width(75));
            ch.SuccessStageDirection = GUILayout.TextField(ch.SuccessStageDirection, GUILayout.Width(95));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Vers Réplique (ID) :", GUILayout.Width(125));
            ch.SuccessNextLineId = GUILayout.TextField(ch.SuccessNextLineId, GUILayout.Width(110));
            GUILayout.Label("+ Crédits CE :", GUILayout.Width(80));
            int.TryParse(GUILayout.TextField(ch.SuccessRewards.EarnCredits.ToString(), GUILayout.Width(50)), out ch.SuccessRewards.EarnCredits);
            GUILayout.Label("+ XP :", GUILayout.Width(40));
            int.TryParse(GUILayout.TextField(ch.SuccessRewards.EarnXP.ToString(), GUILayout.Width(40)), out ch.SuccessRewards.EarnXP);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Item Obtenu :", GUILayout.Width(90));
            ch.SuccessRewards.ItemRewardName = GUILayout.TextField(ch.SuccessRewards.ItemRewardName, GUILayout.Width(140));
            GUILayout.Label("Obj. Validé (ID) :", GUILayout.Width(105));
            ch.SuccessRewards.CompletionObjectiveId = GUILayout.TextField(ch.SuccessRewards.CompletionObjectiveId);
            GUILayout.EndHorizontal();

            DrawEffectListMini("Effets Succès", ch.SuccessEffects);

            GUILayout.EndVertical();

            // Branche d'Échec
            GUILayout.Space(2);
            GUI.backgroundColor = new Color(0.45f, 0.15f, 0.15f);
            GUILayout.BeginVertical(GUI.skin.box);
            GUI.backgroundColor = Color.white;
            GUILayout.Label("<color=#FF6655><b>✕ BRANCHE EN CAS D'ÉCHEC</b></color>");

            GUILayout.BeginHorizontal();
            GUILayout.Label("Réponse PNJ :", GUILayout.Width(90));
            ch.FailureSpeech = GUILayout.TextField(ch.FailureSpeech);
            GUILayout.Label("Didascalie :", GUILayout.Width(75));
            ch.FailureStageDirection = GUILayout.TextField(ch.FailureStageDirection, GUILayout.Width(95));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Vers Réplique (ID) :", GUILayout.Width(125));
            ch.FailureNextLineId = GUILayout.TextField(ch.FailureNextLineId, GUILayout.Width(110));
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

        private void DrawJsonDiskTab()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("<b>Sauvegarde, Chargement & Exécution</b>");

            string directory = Path.Combine(Application.persistentDataPath, "Scenarios");
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

            GUILayout.Label($"Dossier cible : <color=grey>{directory}</color>");
            GUILayout.Space(6);

            GUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.2f, 0.75f, 0.4f);
            if (GUILayout.Button("💾 Enregistrer JSON", GUILayout.Height(36)))
            {
                string filename = string.IsNullOrWhiteSpace(_data.SceneId) ? "scene_export" : _data.SceneId;
                string path = Path.Combine(directory, $"{filename}.json");
                string json = JsonUtility.ToJson(_data, true);
                File.WriteAllText(path, json);
                _activeFilePath = path;
                Debug.Log($"[ScenarioEditor] Scène enregistrée sous : {path}");
            }

            GUI.backgroundColor = new Color(0.2f, 0.6f, 0.9f);
            if (GUILayout.Button("📁 Recharger depuis Fichier", GUILayout.Height(36)))
            {
                string filename = string.IsNullOrWhiteSpace(_data.SceneId) ? "scene_export" : _data.SceneId;
                string path = Path.Combine(directory, $"{filename}.json");
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    _data = JsonUtility.FromJson<StorySceneData>(json);
                    StorySceneData.EnsureDeepDefaults(_data);
                    _activeFilePath = path;
                }
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.Space(8);
            GUI.backgroundColor = new Color(1.0f, 0.55f, 0.1f);
            if (GUILayout.Button("🚀 DÉPLOYER & TESTER CETTE SCÈNE EN DIRECT DANS LA VUE 3D", GUILayout.Height(40)))
            {
                DeployCurrentSceneToRuntime();
            }
            GUI.backgroundColor = Color.white;

            GUILayout.Space(8);
            GUILayout.Label("<b>Aperçu JSON brut :</b>");
            string preview = JsonUtility.ToJson(_data, true);
            GUILayout.TextArea(preview, GUILayout.Height(170));
            GUILayout.EndVertical();
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

            var definition = ScenarioCatalog.FromSceneData(_data);
            if (definition == null)
            {
                Debug.LogWarning("[ScenarioEditor] Scène invalide : impossible de construire une définition déployable.");
                return;
            }

            ScenarioCatalog.Register(definition);
            director.StartScenario(_data.SceneId);
            StartCoroutine(controller.InitializeSceneRoutine(grid, turnManager, arena, director));
        }
    }
}

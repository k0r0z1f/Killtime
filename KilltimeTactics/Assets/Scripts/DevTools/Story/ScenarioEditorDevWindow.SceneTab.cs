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
using Killtime.Audio;

namespace Killtime.Story
{
    public partial class ScenarioEditorDevWindow : FloatingWindow<ScenarioEditorDevWindow>
    {

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

            GUILayout.BeginHorizontal();
            bool newPause = GUILayout.Toggle(_pauseOnDeploy, " ⏸️ Mettre sur pause par défaut au déploiement (Stase tactique)");
            if (newPause != _pauseOnDeploy)
            {
                _pauseOnDeploy = newPause;
                PlayerPrefs.SetInt("ScenarioEditor_PauseOnDeploy", _pauseOnDeploy ? 1 : 0);
                PlayerPrefs.Save();
            }
            GUILayout.EndHorizontal();

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
                    || (_data.Actors != null && _data.Actors.Count > 0)
                    || (_data.Cinematics != null && _data.Cinematics.Count > 0));
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
            int cineCount = _data.Cinematics != null ? _data.Cinematics.Count : 0;
            int ambCount = _data.Ambiences != null ? _data.Ambiences.Count : 0;
            var headerTitleStyle = new GUIStyle(GUI.skin.label) { wordWrap = false, richText = true, alignment = TextAnchor.MiddleLeft };
            GUILayout.Label($"<b>■ SCÈNE {sceneNumPrefix}: {_data.Title}</b> (<color=#00E5FF>{_data.Nodes.Count} Nœuds</color> + <color=#FFD75E>{trigCount} ▼</color> + <color=#33E6CC>{interCount} 🔧</color> + <color=#7CB3FF>{actorCount} 👥</color> + <color=#FF59D9>{cineCount} 🎬</color> + <color=#26E0BF>{ambCount} 🌌</color>)", headerTitleStyle, GUILayout.Height(22));

            GUILayout.FlexibleSpace();

            GUI.backgroundColor = new Color(0.15f, 0.85f, 0.5f);
            if (GUILayout.Button("↺ Auto-Disposition", GUILayout.Width(135), GUILayout.Height(22)))
            {
                AutoLayoutGlobalScene();
            }

            GUILayout.Space(4);
            GUI.backgroundColor = new Color(0.55f, 0.85f, 1f);
            if (GUILayout.Button("▦ Mosaïque", GUILayout.Width(90), GUILayout.Height(22)))
            {
                AutoLayoutMosaic();
            }

            GUILayout.Space(4);
            GUI.backgroundColor = new Color(1.0f, 0.75f, 0.2f);
            if (GUILayout.Button("🔍 Cadrer Tout", GUILayout.Width(100), GUILayout.Height(22)))
            {
                FocusAllNodes();
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.Space(2);
            GUILayout.BeginHorizontal();

            GUI.backgroundColor = new Color(0.2f, 0.75f, 1f);
            if (GUILayout.Button("+ Nouveau Nœud", GUILayout.Width(115), GUILayout.Height(22)))
            {
                ComputeNewNodeSpawn(out float newX, out float newY);
                _data.Nodes.Add(new SceneNodeData
                {
                    // Unicité garantie (les suppressions rendent Count+1 réutilisable).
                    NodeId = MakeUniqueNodeId(),
                    Title = "Nouveau Nœud",
                    GraphPosX = newX,
                    GraphPosY = newY
                });
            }
            GUI.backgroundColor = new Color(0.2f, 0.92f, 0.45f);
            if (GUILayout.Button("+ Nœud Objectif", GUILayout.Width(115), GUILayout.Height(22)))
            {
                ComputeNewNodeSpawn(out float newX, out float newY);
                var objNode = new SceneNodeData
                {
                    // Unicité garantie (les suppressions rendent Count+1 réutilisable).
                    NodeId = MakeUniqueNodeId(),
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

            GUILayout.Space(4);
            GUI.backgroundColor = new Color(1f, 0.78f, 0.25f);
            if (GUILayout.Button("+ Déclencheur", GUILayout.Width(105), GUILayout.Height(22)))
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
            if (GUILayout.Button("+ Interactable", GUILayout.Width(105), GUILayout.Height(22)))
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
            if (GUILayout.Button("+ Acteur", GUILayout.Width(85), GUILayout.Height(22)))
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
            GUI.backgroundColor = new Color(1f, 0.35f, 0.85f);
            if (GUILayout.Button("+ Cinéma", GUILayout.Width(85), GUILayout.Height(22)))
            {
                if (_data.Cinematics == null) _data.Cinematics = new System.Collections.Generic.List<SceneCinematicData>();
                Vector2 viewCenter = new Vector2(_lastCanvasSize.x * 0.5f, _lastCanvasSize.y * 0.5f);
                Vector2 worldCenter = (viewCenter - _graphPan) / Mathf.Max(0.01f, _zoom);
                int cineIdx = _data.Cinematics.Count + 1;
                var cine = new SceneCinematicData
                {
                    // Unicité garantie (les suppressions rendent Count+1 réutilisable).
                    CinematicId = MakeUniqueCineId(),
                    Title = $"Cinématique {cineIdx}",
                    GraphPosX = worldCenter.x - 180f,
                    GraphPosY = worldCenter.y - 100f
                };
                cine.Shots.Add(new SceneCinematicShotData { ShotId = "shot_1", Label = "Plan 1" });
                _data.Cinematics.Add(cine);
            }
            GUI.backgroundColor = new Color(1f, 0.6f, 0.1f);
            if (GUILayout.Button("+ Son 🔊", GUILayout.Width(75), GUILayout.Height(22)))
            {
                if (_data.Sounds == null) _data.Sounds = new System.Collections.Generic.List<SceneSoundData>();
                Vector2 viewCenter = new Vector2(_lastCanvasSize.x * 0.5f, _lastCanvasSize.y * 0.5f);
                Vector2 worldCenter = (viewCenter - _graphPan) / Mathf.Max(0.01f, _zoom);
                _data.Sounds.Add(new SceneSoundData
                {
                    SoundId = MakeUniqueSoundId(),
                    Title = $"Son {_data.Sounds.Count + 1}",
                    GraphPosX = worldCenter.x - 150f,
                    GraphPosY = worldCenter.y + 100f
                });
            }
            GUI.backgroundColor = new Color(0.18f, 0.88f, 0.72f);
            if (GUILayout.Button("+ Ambiance 🌌", GUILayout.Width(105), GUILayout.Height(22)))
            {
                if (_data.Ambiences == null) _data.Ambiences = new System.Collections.Generic.List<SceneAmbienceTrackData>();
                Vector2 viewCenter = new Vector2(_lastCanvasSize.x * 0.5f, _lastCanvasSize.y * 0.5f);
                Vector2 worldCenter = (viewCenter - _graphPan) / Mathf.Max(0.01f, _zoom);
                _data.Ambiences.Add(new SceneAmbienceTrackData
                {
                    AmbienceId = MakeUniqueAmbienceId(),
                    Title = $"Ambiance {_data.Ambiences.Count + 1}",
                    PresetId = "amb_asteroid_base",
                    GraphPosX = worldCenter.x - 160f,
                    GraphPosY = worldCenter.y + 150f
                });
            }
            GUI.backgroundColor = new Color(0.2f, 0.8f, 1f);
            if (GUILayout.Button("🪐 Décor (Shift+F3)", GUILayout.Width(130), GUILayout.Height(22)))
            {
                SceneEnvironmentDevWindow.Open();
            }
            GUI.backgroundColor = new Color(0.7f, 0.25f, 0.6f);
            if (GUILayout.Button("🎬 Ciné (Shift+F4)", GUILayout.Width(120), GUILayout.Height(22)))
            {
                CinematicEditorDevWindow.Open();
            }
            GUI.backgroundColor = Color.white;
            GUILayout.FlexibleSpace();
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
            GUILayout.Label("<color=grey>Molette = Zoom  ·  Clic Droit = Pan  ·  Double-clic nœud = cadrer  ·  Drag port = lier · Vide = créer (menu)  ·  Tag bord = suivre</color>");
            GUILayout.EndHorizontal();

            DrawAudioToolbar();
            if (_showBankBrowser) DrawBankBrowserPanel();
            if (_showAudioAudit) DrawAudioAuditPanel();
            if (_showAudioMonitor) DrawAudioMonitorPanel();
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
            // Cinématiques historiques sans position graphe (0,0) → auto-dispose.
            if (data.Cinematics != null)
            {
                for (int i = 0; i < data.Cinematics.Count; i++)
                {
                    var c = data.Cinematics[i];
                    if (c == null) continue;
                    if (c.GraphPosX <= 0f && c.GraphPosY <= 0f) return true;
                }
            }
            if (data.Sounds != null)
            {
                for (int i = 0; i < data.Sounds.Count; i++)
                {
                    var snd = data.Sounds[i];
                    if (snd == null) continue;
                    if (snd.GraphPosX <= 0f && snd.GraphPosY <= 0f) return true;
                }
            }
            if (data.Ambiences != null)
            {
                for (int i = 0; i < data.Ambiences.Count; i++)
                {
                    var amb = data.Ambiences[i];
                    if (amb == null) continue;
                    if (amb.GraphPosX <= 0f && amb.GraphPosY <= 0f) return true;
                }
            }
            return false;
        }
    }
}

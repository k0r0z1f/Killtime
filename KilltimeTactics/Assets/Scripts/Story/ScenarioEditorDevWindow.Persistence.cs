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
        public void SaveActiveScene() => SaveCurrentSceneJson();
        public void DeployActiveSceneToRuntime() => DeployCurrentSceneToRuntime();
        public void PurgeCinematicReferences(string cinematicId)
        {
            if (string.IsNullOrWhiteSpace(cinematicId) || _data == null) return;
            bool Match(string v) => !string.IsNullOrWhiteSpace(v) && string.Equals(v.Trim(), cinematicId.Trim(), StringComparison.OrdinalIgnoreCase);
            void Purge(List<string> list) { if (list != null) list.RemoveAll(Match); }
            if (_data.Nodes != null)
                for (int n = 0; n < _data.Nodes.Count; n++)
                {
                    var node = _data.Nodes[n];
                    if (node == null) continue;
                    Purge(node.CinematicIds);
                    if (node.Dialogues != null)
                        for (int d = 0; d < node.Dialogues.Count; d++)
                            if (node.Dialogues[d] != null) Purge(node.Dialogues[d].CinematicIds);
                    if (node.Events != null)
                        for (int e = 0; e < node.Events.Count; e++)
                            if (node.Events[e] != null) Purge(node.Events[e].CinematicIds);
                    if (node.Consequences != null)
                        for (int k = 0; k < node.Consequences.Count; k++)
                            if (node.Consequences[k] != null) Purge(node.Consequences[k].CinematicIds);
                }
            if (_data.Triggers != null)
                for (int t = 0; t < _data.Triggers.Count; t++)
                    if (_data.Triggers[t] != null) Purge(_data.Triggers[t].CinematicIds);
            if (_data.Interactables != null)
                for (int i = 0; i < _data.Interactables.Count; i++)
                    if (_data.Interactables[i] != null) Purge(_data.Interactables[i].CinematicIds);
            if (_data.Actors != null)
                for (int a = 0; a < _data.Actors.Count; a++)
                    if (_data.Actors[a] != null) Purge(_data.Actors[a].CinematicIds);
        }

        /// <summary>
        /// Supprime les branchements son 🔊 pointant vers une carte supprimée
        /// (SourceCardId de démarrage + StopSourceCardId d'arrêt) : évite les
        /// déclenchements fantômes en cas de réutilisation d'id.
        /// </summary>
        public void PurgeSoundReferences(string cardId)
        {
            if (string.IsNullOrWhiteSpace(cardId) || _data == null || _data.Sounds == null) return;
            for (int i = 0; i < _data.Sounds.Count; i++)
            {
                var snd = _data.Sounds[i];
                if (snd == null) continue;
                if (!string.IsNullOrWhiteSpace(snd.SourceCardId)
                    && string.Equals(snd.SourceCardId.Trim(), cardId.Trim(), System.StringComparison.OrdinalIgnoreCase))
                    snd.SourceCardId = "";
                if (!string.IsNullOrWhiteSpace(snd.StopSourceCardId)
                    && string.Equals(snd.StopSourceCardId.Trim(), cardId.Trim(), System.StringComparison.OrdinalIgnoreCase))
                    snd.StopSourceCardId = "";
            }
        }

        public void PurgeAmbienceReferences(string cardId)
        {
            if (string.IsNullOrWhiteSpace(cardId) || _data == null || _data.Ambiences == null) return;
            string target = cardId.Trim();
            for (int i = 0; i < _data.Ambiences.Count; i++)
            {
                var a = _data.Ambiences[i];
                if (a == null) continue;
                if (string.Equals(a.StartSourceCardId?.Trim(), target, StringComparison.OrdinalIgnoreCase))
                    a.StartSourceCardId = "";
                if (string.Equals(a.StopSourceCardId?.Trim(), target, StringComparison.OrdinalIgnoreCase))
                    a.StopSourceCardId = "";
            }
        }

        protected override void OnAwake()
        {
            _pauseOnDeploy = PlayerPrefs.GetInt("ScenarioEditor_PauseOnDeploy", 1) == 1;
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
            // Déploiement live : repart d'un état cinématique propre (sinon la
            // routine / caméra / warp skybox / letterbox en cours survit au
            // redeploy et l'enchaînement de début paraît "brisé" — seul un
            // restart remettait tout en place). Sans danger si absent.
            try { FindAnyObjectByType<CameraSystem.CinematicDirector>()?.ResetCinematicState(); }
            catch { /* ignore */ }

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

            if (_pauseOnDeploy)
            {
                CombatHUD.EnsureInstance()?.SetPause(true);
            }
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

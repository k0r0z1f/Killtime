using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Killtime.Tactics;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Core.Character;
using Killtime.Core.Dice;
using Killtime.Core.Inventory;
using Killtime.CameraSystem;
using Killtime.UI;
using Killtime.Audio;
using Killtime.Story.Data;

namespace Killtime.Story.Scenes
{
    public class JsonStorySceneController : MonoBehaviour, IStorySceneController
    {
        private TacticalHexGrid _grid;
        private TurnManager _turnManager;
        private CombatDevArena _arena;
        private ScenarioDirector _director;
        private StorySceneData _sceneData;

        private readonly Dictionary<string, TacticalUnit> _spawnedActors = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<TacticalUnit> _playerParty = new();
        private readonly List<TacticalInteractable> _interactables = new();
        private readonly HashSet<string> _firedTriggerIds = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _activatedInteractableIds = new(StringComparer.OrdinalIgnoreCase);
        // Nœud courant au moment de chaque activation : un trigger InteractableActivated
        // scoped sur un nœud source ne tire que si l'activation a eu lieu PENDANT ce
        // nœud (sinon une utilisation précoce au nœud précédent skippe instantanément
        // le nœud source dès son entrée). Triggers globaux (Source vide) : inchangés.
        private readonly Dictionary<string, string> _activationNodeIds = new(StringComparer.OrdinalIgnoreCase);
        // Positions du groupe à l'entrée du nœud courant : la validation par proximité
        // exige un déplacement (ou un hors-portée initial), sinon un spawn sur l'objectif
        // valide gratuitement dès la première frame et skippe le nœud.
        private readonly Dictionary<TacticalUnit, HexCoordinates> _nodeEnterCoords = new();

        private int _currentDialogueIndex = 0;
        private string _lastNodeId = "";
        private SceneDialogueChoiceData _activeReactionChoice = null;
        private SceneDialogueLineData _activeReactionPromptLine = null;
        private string _activeChallengeSummary = "";
        private string _activeChallengeRewards = "";
        // File d'attente des textes interactables vers le chat milieu :
        // si holotable + datapad se valident coup sur coup, le 2e ne doit pas
        // être perdu quand une réaction est déjà affichée.
        private readonly Queue<PendingMiddleChatMessage> _pendingMiddleChat = new();
        private struct PendingMiddleChatMessage
        {
            public string Speaker;
            public string StageDirection;
            public string Speech;
            public string ActionLabel;
            public string DisplayName;
            public string Rewards;
        }

        // Rects IMGUI du chat de scène : servent de bloqueurs clics 3D via FloatingWindowChrome.
        private Rect _dialogueRect = Rect.zero;
        private Rect _partyBarRect = Rect.zero;
        private bool _hasPartyBar;
        private readonly List<Rect> _interactButtonRects = new();

        public StorySceneData SceneData => _sceneData;

        public void SetSceneData(StorySceneData data)
        {
            _sceneData = data;
        }

        public IReadOnlyDictionary<string, TacticalUnit> SpawnedActors => _spawnedActors;
        public IReadOnlyList<TacticalInteractable> SpawnedInteractables => _interactables;

        /// <summary>
        /// Déclenche les cinématiques liées à une carte en FIRE-AND-FORGET :
        /// retour immédiat, la carte suivante s'enclenche sans attendre la fin
        /// de la cinématique. Les ids inconnus sont ignorés avec un avertissement.
        /// </summary>
        public void FireLinkedCinematics(List<string> cinematicIds)
        {
            if (cinematicIds == null || cinematicIds.Count == 0 || _sceneData == null) return;
            var director = FindAnyObjectByType<CinematicDirector>();
            if (director == null)
            {
                Debug.LogWarning("[JsonStorySceneController] CinematicDirector introuvable : cinématiques ignorées.");
                return;
            }
            for (int i = 0; i < cinematicIds.Count; i++)
            {
                string id = cinematicIds[i];
                if (string.IsNullOrWhiteSpace(id)) continue;
                var cine = _sceneData.FindCinematic(id);
                if (cine == null)
                {
                    Debug.LogWarning($"[JsonStorySceneController] Cinématique introuvable : '{id}'.");
                    continue;
                }
                director.PlaySceneCinematic(cine);
                CombatHUD.Instance?.AddAdvancedLog($"🎬 <b>Cinématique :</b> {cine.Title} [{cine.CinematicId}]", LogCategory.MovementAndTurns, "[CINÉMATIQUE]", new Color(1f, 0.5f, 0.9f));
            }
        }

        public bool TryGetSpawnedInteractable(string interactableId, out TacticalInteractable prop)
        {
            prop = null;
            if (string.IsNullOrWhiteSpace(interactableId)) return false;
            for (int i = 0; i < _interactables.Count; i++)
            {
                var p = _interactables[i];
                if (p == null) continue;
                if (string.Equals(p.gameObject.name, "Interactable_" + interactableId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(p.ObjectName, interactableId, StringComparison.OrdinalIgnoreCase))
                {
                    prop = p;
                    return true;
                }
            }
            return false;
        }

        private void OnEnable()
        {
            FloatingWindowChrome.RegisterExtraBlocker(IsPointerOverSceneChat);
            RegisterSceneHudExclusionZones();
        }

        private void OnDisable()
        {
            FloatingWindowChrome.UnregisterExtraBlocker(IsPointerOverSceneChat);
            UnregisterSceneHudExclusionZones();
        }

        // Zones d'exclusion story (barre party, boîte dialogue) : en mode story scene
        // le HUD a des boutons de plus que le HUD combat ; sans ça les vignettes
        // dockées et les ouvertures de fenêtres les recouvrent.
        private System.Func<Rect> _partyBarZone;
        private System.Func<Rect> _dialogueBoxZone;

        private void RegisterSceneHudExclusionZones()
        {
            _partyBarZone ??= () => (_hasPartyBar && _partyBarRect.width > 0f && _partyBarRect.height > 0f)
                ? _partyBarRect : Rect.zero;
            _dialogueBoxZone ??= () => (_dialogueRect.width > 0f && _dialogueRect.height > 0f)
                ? _dialogueRect : Rect.zero;
            FloatingWindowChrome.RegisterExclusionZone(_partyBarZone);
            FloatingWindowChrome.RegisterExclusionZone(_dialogueBoxZone);
        }

        private void UnregisterSceneHudExclusionZones()
        {
            if (_partyBarZone != null) FloatingWindowChrome.UnregisterExclusionZone(_partyBarZone);
            if (_dialogueBoxZone != null) FloatingWindowChrome.UnregisterExclusionZone(_dialogueBoxZone);
        }

        /// <summary>
        /// Bloqueur central : survol du chat de scène (boîte dialogue, barre party,
        /// boutons d'interaction projetés) => la carte 3D ne reçoit aucun clic.
        /// Enregistré dans FloatingWindowChrome : couvre sélection, déplacement,
        /// zoom et orbit (tous les handlers 3D interrogent ce registre).
        /// </summary>
        private bool IsPointerOverSceneChat(Vector2 mouseGui)
        {
            if (CombatHUD.IsPaused) return false;
            if (_hasPartyBar && _partyBarRect.width > 0f && _partyBarRect.height > 0f && _partyBarRect.Contains(mouseGui))
                return true;
            if (_dialogueRect.width > 0f && _dialogueRect.height > 0f && _dialogueRect.Contains(mouseGui))
                return true;
            for (int i = 0; i < _interactButtonRects.Count; i++)
            {
                Rect r = _interactButtonRects[i];
                if (r.width > 0f && r.height > 0f && r.Contains(mouseGui))
                    return true;
            }
            // Filet live : avant la première OnGUI (rects encore vides), recalcule
            // le rect théorique pour ne laisser passer aucun clic.
            if ((_dialogueRect.width <= 0f || _dialogueRect.height <= 0f) && _director != null && _director.CurrentNode != null)
            {
                Rect live = ComputeDialogueRect();
                if (live.width > 0f && live.height > 0f && live.Contains(mouseGui))
                    return true;
            }
            return false;
        }

        private Rect ComputeDialogueRect()
        {
            float boxWidth = Mathf.Min(920f, Screen.width - 40f);
            float baseHeight = 150f;
            try
            {
                bool isReactionActive = _activeReactionChoice != null && _activeReactionPromptLine != null;
                var dataNode = _sceneData != null && _director != null ? _sceneData.FindNode(_director.CurrentNode?.Id) : null;
                bool hasDialogues = dataNode != null && dataNode.Dialogues != null && dataNode.Dialogues.Count > 0 && _currentDialogueIndex >= 0 && _currentDialogueIndex < dataNode.Dialogues.Count;
                var line = hasDialogues ? dataNode.Dialogues[_currentDialogueIndex] : null;
                bool hasChoices = !isReactionActive && line != null && line.Choices != null && line.Choices.Count > 0;
                if (hasChoices) baseHeight += line.Choices.Count * 34f;
                else if (isReactionActive) baseHeight += 20f;
            }
            catch { /* ignore */ }
            if (Screen.width <= 0 || Screen.height <= 0) return Rect.zero;
            float boxHeight = Mathf.Min(baseHeight, Screen.height - 120f);
            if (boxWidth <= 0f || boxHeight <= 0f) return Rect.zero;
            return new Rect((Screen.width - boxWidth) * 0.5f, Screen.height - boxHeight - 20f, boxWidth, boxHeight);
        }

        public IEnumerator InitializeSceneRoutine(TacticalHexGrid grid, TurnManager turnManager, CombatDevArena arena, ScenarioDirector director)
        {
            _grid = grid;
            _turnManager = turnManager;
            _arena = arena;
            _director = director;

            if (_sceneData == null)
            {
                Debug.LogError("[JsonStorySceneController] Aucune donnée de scène injectée !");
                yield break;
            }

            _firedTriggerIds.Clear();
            _activatedInteractableIds.Clear();
            _activationNodeIds.Clear();
            _nodeEnterCoords.Clear();
            _pendingMiddleChat.Clear();
            _activeReactionChoice = null;
            _activeReactionPromptLine = null;
            _activeChallengeSummary = "";
            _activeChallengeRewards = "";

            if (_arena != null)
            {
                _arena.ClearAllUnits();
            }

            yield return LoadLinkedMapRoutine();

            if (_arena != null)
            {
                _arena.ClearAllUnits();
            }

            SpawnActors();
            SpawnInteractables();

            if (_turnManager != null)
            {
                _turnManager.ClearUnits();
                TacticalUnit leadUnit = _playerParty.Count > 0 ? _playerParty[0] : null;
                _turnManager.SetExplorationMode(true, leadUnit);

                for (int i = 0; i < _playerParty.Count; i++)
                {
                    _turnManager.RegisterUnit(_playerParty[i]);
                    _playerParty[i]?.GetComponent<TacticalUnitVisual>()?.SetCombatStance(false);
                }
            }

            if (_arena != null && _playerParty.Count > 0)
            {
                var allies = new List<TacticalUnit>(_playerParty);
                allies.RemoveAt(0);
                _arena.RegisterSceneParty(_playerParty[0], allies);
            }

            if (KilltimeAudioManager.Instance != null)
            {
                KilltimeAudioManager.Instance.PlayMusic(MusicMood.Explore, MusicIntensity.Calm, true);
            }

            CombatHUD.Instance?.AddAdvancedLog($"🎬 <b>{_sceneData.Title}</b> [{_sceneData.Volume}] initialisée.", LogCategory.MovementAndTurns, "[SCÈNE]", Color.cyan);

            UpdateNodeState();
        }

        private IEnumerator LoadLinkedMapRoutine()
        {
            var mapEditor = MapEditorDevWindow.Instance ?? FindAnyObjectByType<MapEditorDevWindow>();
            if (mapEditor == null)
            {
                mapEditor = new GameObject("[UI] MapEditorDevWindow").AddComponent<MapEditorDevWindow>();
            }

            if (_sceneData.EmbeddedMap != null && _sceneData.EmbeddedMap.ModifiedTiles.Count > 0)
            {
                var cachedUnits = _sceneData.EmbeddedMap.PlacedUnits;
                _sceneData.EmbeddedMap.PlacedUnits = null;
                try
                {
                    mapEditor.ApplyLoadedMap(_sceneData.EmbeddedMap);
                }
                finally
                {
                    _sceneData.EmbeddedMap.PlacedUnits = cachedUnits;
                }
                yield return null;
            }
            else if (!string.IsNullOrEmpty(_sceneData.LinkedMapName))
            {
                string mapFolder = Path.Combine(Application.persistentDataPath, "Maps");
                string mapPath = Path.Combine(mapFolder, $"{_sceneData.LinkedMapName}.json");

                if (!File.Exists(mapPath))
                {
                    mapPath = Path.Combine(mapFolder, _sceneData.LinkedMapName);
                }

                if (File.Exists(mapPath))
                {
                    string json = File.ReadAllText(mapPath);
                    var mapData = JsonUtility.FromJson<TacticalMapSaveData>(json);
                    if (mapData != null)
                    {
                        mapData.PlacedUnits = null;
                        mapEditor.ApplyLoadedMap(mapData, mapPath);
                        yield return null;
                    }
                }
            }

            if (!string.IsNullOrEmpty(_sceneData.EnvironmentId))
            {
                if (SceneEnvironmentLibrary.Build(_sceneData.EnvironmentId, transform, _grid))
                {
                    yield return null;
                }
            }
        }

        private void SpawnActors()
        {
            _spawnedActors.Clear();
            _playerParty.Clear();

            for (int i = 0; i < _sceneData.Actors.Count; i++)
            {
                var actorData = _sceneData.Actors[i];
                if (actorData == null) continue;
                if (!actorData.SpawnInitially && !string.IsNullOrEmpty(actorData.SpawnOnNodeId)) continue;

                SpawnSingleActor(actorData);
            }
        }

        public TacticalUnit SpawnSingleActor(SceneActorSpawnData actorData)
        {
            if (actorData == null) return null;
            if (_spawnedActors.TryGetValue(actorData.ActorId, out var existing) && existing != null)
                return existing;

            CharacterSheet sheet = null;
            string charFolder = Path.Combine(Application.persistentDataPath, "Characters");

            if (!string.IsNullOrEmpty(actorData.CharacterSheetFileName))
            {
                string sheetPath = Path.Combine(charFolder, actorData.CharacterSheetFileName.EndsWith(".json") ? actorData.CharacterSheetFileName : $"{actorData.CharacterSheetFileName}.json");
                if (File.Exists(sheetPath))
                {
                    sheet = CharacterStorageService.LoadCharacter(sheetPath);
                }
            }

            if (sheet == null && actorData.EmbeddedSheet != null && !string.IsNullOrEmpty(actorData.EmbeddedSheet.Name))
            {
                sheet = JsonUtility.FromJson<CharacterSheet>(JsonUtility.ToJson(actorData.EmbeddedSheet));
            }

            if (sheet == null)
            {
                sheet = new CharacterSheet
                {
                    Name = actorData.DisplayName,
                    BaseAttributes = new Attributes(3, 3, 3, 3, 2, 2, 1, 2, 0),
                    BaseArmor = actorData.BaseArmor,
                    Profile = actorData.IsPlayer ? CharacterProfileType.HerosPJ : CharacterProfileType.PnjNormal,
                    ModelPrefabName = actorData.ModelPrefabName
                };
            }

            if (!string.IsNullOrEmpty(actorData.EquippedWeaponName) && sheet.GetEquippedWeapon() == null)
            {
                bool isRanged = actorData.EquippedWeaponName.ToLowerInvariant().Contains("laser")
                             || actorData.EquippedWeaponName.ToLowerInvariant().Contains("pistolet")
                             || actorData.EquippedWeaponName.ToLowerInvariant().Contains("fusil")
                             || actorData.EquippedWeaponName.ToLowerInvariant().Contains("blaster");

                var weapon = new InventoryItem
                {
                    ItemId = $"weapon_{actorData.ActorId}",
                    Name = actorData.EquippedWeaponName,
                    Type = ItemType.Weapon,
                    AssociatedSkill = isRanged ? SkillType.Ballistique : SkillType.ManiementArmes,
                    RangeInTiles = isRanged ? 8 : 1,
                    BaseDamage = isRanged ? 5 : 4,
                    IsEquipped = true
                };
                sheet.AddItem(weapon);
                if (sheet.GetSkill(weapon.AssociatedSkill).TrainingLevel == 0)
                    sheet.GetSkill(weapon.AssociatedSkill).TrainingLevel = 1;
            }

            var coords = new HexCoordinates(actorData.Q, actorData.R);
            var go = new GameObject($"Actor_{actorData.ActorId}");
            var unit = go.AddComponent<TacticalUnit>();
            unit.InitializeFromSheet(sheet, coords, _grid, actorData.IsPlayer);

            var visual = unit.GetComponent<TacticalUnitVisual>();
            if (visual != null)
            {
                visual.SetColor(actorData.PrimaryColor, actorData.AccentColor);
                visual.SetCombatStance(actorData.StartInCombatStance);
            }

            if (actorData.FacingAngle != 0f)
            {
                unit.transform.rotation = Quaternion.Euler(0f, actorData.FacingAngle, 0f);
            }

            _spawnedActors[actorData.ActorId] = unit;

            if (actorData.IsPlayer)
            {
                _playerParty.Add(unit);
            }

            return unit;
        }

        public void SpawnActorsForNode(string nodeId, bool isCombat = false)
        {
            if (_sceneData == null || _sceneData.Actors == null || string.IsNullOrEmpty(nodeId)) return;

            var dataNode = _sceneData.FindNode(nodeId);
            bool isCombatNode = isCombat || (dataNode != null && dataNode.TriggerCombatOnEnter) || (_turnManager != null && !_turnManager.IsInExploration);

            for (int i = 0; i < _sceneData.Actors.Count; i++)
            {
                var actorData = _sceneData.Actors[i];
                if (actorData == null) continue;
                if (_spawnedActors.ContainsKey(actorData.ActorId)) continue;
                if (!string.Equals(actorData.SpawnOnNodeId, nodeId, StringComparison.OrdinalIgnoreCase)) continue;

                var unit = SpawnSingleActor(actorData);
                if (unit != null)
                {
                    // Cinématiques liées à l'acteur (spawn différé) : non-bloquantes.
                    FireLinkedCinematics(actorData.CinematicIds);
                    _turnManager?.RegisterUnit(unit);
                    if (!actorData.IsPlayer && isCombatNode)
                    {
                        _arena?.RegisterHostileUnit(unit);
                        _arena?.SelectTarget(unit);
                    }
                }
            }
        }

        private void SpawnInteractables()
        {
            for (int i = 0; i < _interactables.Count; i++)
            {
                if (_interactables[i] != null) Destroy(_interactables[i].gameObject);
            }
            _interactables.Clear();

            for (int i = 0; i < _sceneData.Interactables.Count; i++)
            {
                var data = _sceneData.Interactables[i];
                if (data == null) continue;

                var go = new GameObject($"Interactable_{data.InteractableId}");
                var interactable = go.AddComponent<TacticalInteractable>();
                interactable.Configure(data.DisplayName, data.ActionLabel, new HexCoordinates(data.Q, data.R), data.Radius);

                string objId = data.CompletionObjectiveId;
                string successMsg = data.SuccessLog;
                string triggerNode = data.TriggerNodeId;

                interactable.OnInteractionTriggered += (unit) =>
                {
                    if (!string.IsNullOrEmpty(data.InteractableId))
                    {
                        MarkInteractableActivated(data.InteractableId);
                    }
                    // Cinématiques liées à l'interactable : non-bloquantes.
                    FireLinkedCinematics(data.CinematicIds);
                    if (!string.IsNullOrEmpty(objId))
                    {
                        _director?.SetObjectiveComplete(objId, true);
                    }
                    if (!string.IsNullOrEmpty(successMsg))
                    {
                        CombatHUD.Instance?.AddAdvancedLog($"✔ {unit.Stats.Name} : {successMsg}", LogCategory.MovementAndTurns, "[ACTION]", Color.yellow);
                        ShowInteractableSuccessInMiddleChat(unit, data);
                    }
                    if (!string.IsNullOrEmpty(triggerNode))
                    {
                        if (_director != null && !_director.GoToNode(triggerNode))
                        {
                            _director.Choose(triggerNode);
                        }
                    }
                    unit.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText($"✔ {data.ActionLabel}", Color.green);
                };

                _interactables.Add(interactable);
            }
        }

        private void Update()
        {
            if (CombatHUD.IsPaused || (StorySceneManager.Instance != null && StorySceneManager.Instance.IsTransitioning)) return;

            HandlePartyInput();
            HandleDialoguesInput();
            CheckProximityObjectives();
            UpdateNodeState();
        }

        private void HandlePartyInput()
        {
            if (_turnManager == null || !_turnManager.IsInExploration) return;

            if (Input.GetKeyDown(KeyCode.Alpha1) && _playerParty.Count > 0) SelectOperative(_playerParty[0]);
            if (Input.GetKeyDown(KeyCode.Alpha2) && _playerParty.Count > 1) SelectOperative(_playerParty[1]);
            if (Input.GetKeyDown(KeyCode.Alpha3) && _playerParty.Count > 2) SelectOperative(_playerParty[2]);
            if (Input.GetKeyDown(KeyCode.Alpha4) && _playerParty.Count > 3) SelectOperative(_playerParty[3]);
            if (Input.GetKeyDown(KeyCode.Tab) && _playerParty.Count > 1) CyclePartyMember();

            if (Input.GetKeyDown(KeyCode.E) && _turnManager.ActiveUnit != null)
            {
                InteractWithNearest(_turnManager.ActiveUnit);
            }
        }

        private void SelectOperative(TacticalUnit unit)
        {
            if (unit == null || _arena == null) return;
            _arena.SwitchActiveExplorer(unit);
        }

        private void CyclePartyMember()
        {
            int idx = _playerParty.IndexOf(_turnManager.ActiveUnit);
            int next = (idx + 1) % _playerParty.Count;
            SelectOperative(_playerParty[next]);
        }

        private void InteractWithNearest(TacticalUnit unit)
        {
            for (int i = 0; i < _interactables.Count; i++)
            {
                var target = _interactables[i];
                if (target != null && target.CanInteract(unit))
                {
                    target.TryInteract(unit, out string log);
                    if (!string.IsNullOrEmpty(log))
                    {
                        CombatHUD.Instance?.AddAdvancedLog(log, LogCategory.MovementAndTurns, "[ACTION]", Color.yellow);
                    }
                    return;
                }
            }
        }

        private void HandleDialoguesInput()
        {
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))
            {
                if (_activeReactionChoice != null)
                {
                    ConfirmReactionAndAdvance();
                    return;
                }

                var dataNode = _sceneData != null && _director != null ? _sceneData.FindNode(_director.CurrentNode?.Id) : null;
                if (dataNode != null && _currentDialogueIndex >= 0 && _currentDialogueIndex < dataNode.Dialogues.Count)
                {
                    var currentLine = dataNode.Dialogues[_currentDialogueIndex];
                    if (currentLine != null && currentLine.Choices.Count > 0)
                    {
                        return;
                    }
                }

                AdvanceDialogueOrNode();
            }
        }

        private void CheckProximityObjectives()
        {
            if (_director == null) return;
            var node = _director.CurrentNode;
            if (node == null || node.Kind != ScenarioNodeKind.Objective) return;

            // Garde anti-skip d'entrée : Update() appelle CheckProximity AVANT
            // UpdateNodeState. Sur la frame d'arrivée sur un nouveau nœud,
            // _nodeEnterCoords contient encore les positions d'entrée du nœud
            // PRÉCÉDENT. Si le joueur a bougé pendant le nœud précédent
            // (ex : 12 dialogues = le temps de se pré-positionner sur
            // l'holotable, qui est déjà à portée du spawn), moved=true et la
            // validation passe instantanément, puis le trigger saute la
            // narration. Avec un nœud intermédiaire vide (pas le temps de
            // bouger), moved=false + wasInRange=true => skip, par chance.
            // On refresh donc le snapshot et on saute la validation sur la
            // frame de transition, quelle que soit la longueur du chaînage.
            if (!string.Equals(_lastNodeId, node.Id, StringComparison.OrdinalIgnoreCase))
            {
                _nodeEnterCoords.Clear();
                for (int p = 0; p < _playerParty.Count; p++)
                {
                    var u = _playerParty[p];
                    if (u != null) _nodeEnterCoords[u] = u.CurrentCoords;
                }
                return;
            }

            for (int p = 0; p < _playerParty.Count; p++)
            {
                var member = _playerParty[p];
                if (member == null) continue;

                for (int i = 0; i < _sceneData.Interactables.Count; i++)
                {
                    var it = _sceneData.Interactables[i];
                    if (!string.IsNullOrEmpty(it.CompletionObjectiveId) && !_director.IsObjectiveComplete(it.CompletionObjectiveId))
                    {
                        var target = new HexCoordinates(it.Q, it.R);
                        if (member.CurrentCoords.DistanceTo(target) <= it.Radius)
                        {
                            // Pas de validation gratuite : un membre immobile depuis l'entrée
                            // du nœud et déjà à portée (spawn sur l'objectif) ne valide pas.
                            // Il faut s'éloigner/revenir, bouger, ou activer à la main (E).
                            bool hasEntry = _nodeEnterCoords.TryGetValue(member, out var enterCoords);
                            bool moved = !hasEntry || !enterCoords.Equals(member.CurrentCoords);
                            bool wasInRange = hasEntry && enterCoords.DistanceTo(target) <= it.Radius;
                            if (!moved && wasInRange) continue;
                            _director.SetObjectiveComplete(it.CompletionObjectiveId, true);
                            if (!string.IsNullOrEmpty(it.InteractableId))
                            {
                                MarkInteractableActivated(it.InteractableId);
                            }
                            string memberName = member.Stats != null ? member.Stats.Name : "Escouade";
                            if (!string.IsNullOrWhiteSpace(it.SuccessLog))
                            {
                                CombatHUD.Instance?.AddAdvancedLog($"✔ {memberName} : {it.SuccessLog}", LogCategory.MovementAndTurns, "[ACTION]", Color.yellow);
                                ShowInteractableSuccessInMiddleChat(member, it);
                            }
                            member.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText($"✔ Objectif Validé", Color.green);
                        }
                    }
                }
            }
        }

        private void MarkInteractableActivated(string interactableId)
        {
            if (string.IsNullOrEmpty(interactableId)) return;
            _activatedInteractableIds.Add(interactableId);
            try { _activationNodeIds[interactableId] = _director != null && _director.CurrentNode != null ? _director.CurrentNode.Id : ""; }
            catch { _activationNodeIds[interactableId] = ""; }
        }

        /// <summary>
        /// Envoie le texte de résolution d'un interactable (SuccessLog) vers le
        /// chat central (boîte dialogue du milieu), en plus du flux latéral.
        /// Utilise l'affichage Réaction existant : bloque les triggers d'entrée
        /// (UpdateNodeState exige _activeReactionChoice==null) jusqu'au
        /// "Continuer", pour laisser le temps de lire avant le saut de nœud.
        /// Si une réaction est déjà affichée, mise en file d'attente.
        /// </summary>
        private void ShowInteractableSuccessInMiddleChat(TacticalUnit unit, SceneInteractableSpawnData data)
        {
            if (unit == null || data == null) return;
            if (string.IsNullOrWhiteSpace(data.SuccessLog)) return;

            string speaker = !string.IsNullOrEmpty(unit.Stats?.Name)
                ? unit.Stats.Name
                : (!string.IsNullOrEmpty(data.DisplayName) ? data.DisplayName : "Escouade");
            var msg = new PendingMiddleChatMessage
            {
                Speaker = speaker,
                StageDirection = data.ActionLabel ?? "",
                Speech = data.SuccessLog,
                ActionLabel = data.ActionLabel ?? "",
                DisplayName = data.DisplayName ?? "",
                Rewards = !string.IsNullOrEmpty(data.CompletionObjectiveId) ? "✔ Objectif validé" : ""
            };
            if (_activeReactionChoice != null)
            {
                _pendingMiddleChat.Enqueue(msg);
                return;
            }
            ShowMiddleChatMessage(msg);
        }

        // Sentinelle : info interactable non-destructive. Le Continuer déjà
        // présent (Suivant dialogue, bouton objectif...) doit rester visible
        // et suivre le prochain chat : on dismiss sans avancer dialogue/nœud,
        // on retrouve l'état sous-jacent (_currentDialogueIndex inchangé).
        private const string InteractableInfoReturnSentinel = "__INTERACTABLE_INFO__";

        private void ShowMiddleChatMessage(PendingMiddleChatMessage msg)
        {
            var prompt = new SceneDialogueLineData
            {
                SpeakerId = msg.Speaker,
                StageDirection = msg.StageDirection,
                CameraFocusActorId = "",
                CameraPitch = 42f,
                CameraDistance = 9.5f
            };
            SetupReactionDisplay(prompt, msg.Speech, msg.ActionLabel, InteractableInfoReturnSentinel, $"🔧 {msg.DisplayName}", msg.Rewards);
        }

        private bool CurrentLineHasPendingChoice()
        {
            if (_activeReactionChoice != null) return true;
            var dataNode = _sceneData != null && _director != null ? _sceneData.FindNode(_director.CurrentNode?.Id) : null;
            if (dataNode == null || dataNode.Dialogues == null) return false;
            if (_currentDialogueIndex < 0 || _currentDialogueIndex >= dataNode.Dialogues.Count) return false;
            var line = dataNode.Dialogues[_currentDialogueIndex];
            return line != null && line.Choices != null && line.Choices.Count > 0;
        }

        private bool IsTriggerConditionMet(SceneTriggerData trigger)
        {
            if (trigger == null) return false;
            switch (trigger.Kind)
            {
                case SceneTriggerKind.InteractableActivated:
                    if (string.IsNullOrEmpty(trigger.InteractableId) || !_activatedInteractableIds.Contains(trigger.InteractableId))
                        return false;
                    // Trigger scopé sur un nœud : l'activation doit dater de ce nœud,
                    // sinon une utilisation précoce skippe le nœud dès son entrée.
                    if (!string.IsNullOrEmpty(trigger.SourceNodeId)
                        && (!_activationNodeIds.TryGetValue(trigger.InteractableId, out string atNode)
                            || !string.Equals(atNode, trigger.SourceNodeId, StringComparison.OrdinalIgnoreCase)))
                        return false;
                    return true;

                case SceneTriggerKind.ObjectiveCompleted:
                    return !string.IsNullOrEmpty(trigger.ObjectiveId) && _director != null && _director.IsObjectiveComplete(trigger.ObjectiveId);

                case SceneTriggerKind.CampaignFlagSet:
                {
                    if (string.IsNullOrEmpty(trigger.FlagKey) || _director == null || _director.State == null) return false;
                    bool has = _director.State.HasFlag(trigger.FlagKey);
                    return trigger.FlagMustBeSet ? has : !has;
                }

                case SceneTriggerKind.ActorCondition:
                {
                    if (string.IsNullOrEmpty(trigger.ActorId)) return false;
                    if (!_spawnedActors.TryGetValue(trigger.ActorId, out var unit) || unit == null || unit.Stats == null) return false;
                    if (trigger.ActorCondition == SceneActorConditionKind.HasStatus)
                    {
                        if (!System.Enum.TryParse<StatusEffect>(trigger.StatusName, true, out var status) || status == StatusEffect.None) return false;
                        return unit.Stats.ActiveStatus.HasFlag(status);
                    }
                    int max = Mathf.Max(1, unit.Stats.MaxHealth);
                    float pct = (float)unit.Stats.CurrentHealth / max * 100f;
                    return pct < Mathf.Clamp(trigger.HPPercentThreshold, 1, 100);
                }

                default:
                    return false;
            }
        }

        private bool TryFireReadyTrigger()
        {
            if (_sceneData == null || _sceneData.Triggers == null || _director == null) return false;
            string currentId = _director.CurrentNode?.Id;
            if (string.IsNullOrEmpty(currentId)) return false;

            for (int t = 0; t < _sceneData.Triggers.Count; t++)
            {
                var trg = _sceneData.Triggers[t];
                if (trg == null || string.IsNullOrEmpty(trg.TargetNodeId)) continue;
                if (string.Equals(trg.TargetNodeId, currentId, StringComparison.OrdinalIgnoreCase)) continue;
                if (trg.OneShot && _firedTriggerIds.Contains(trg.TriggerId)) continue;
                if (!string.IsNullOrEmpty(trg.SourceNodeId)
                    && !string.Equals(trg.SourceNodeId, currentId, StringComparison.OrdinalIgnoreCase)) continue;
                if (!IsTriggerConditionMet(trg)) continue;
                if (!_director.GoToNode(trg.TargetNodeId)) continue;

                // Cinématiques liées au déclencheur : non-bloquantes (le saut a déjà eu lieu).
                FireLinkedCinematics(trg.CinematicIds);

                if (trg.OneShot) _firedTriggerIds.Add(trg.TriggerId);
                var target = _sceneData.FindNode(trg.TargetNodeId);
                CombatHUD.Instance?.AddAdvancedLog($"⚡ <b>Déclencheur :</b> {trg.Label} ➔ {(target != null ? target.Title : trg.TargetNodeId)}", LogCategory.MovementAndTurns, "[ÉVÉNEMENT]", Color.yellow);
                return true;
            }
            return false;
        }

        private void UpdateNodeState()
        {
            if (_director == null) return;
            var node = _director.CurrentNode;
            if (node == null) return;

            if (_lastNodeId != node.Id)
            {
                _lastNodeId = node.Id;
                _activeReactionChoice = null;
                _activeReactionPromptLine = null;
                _activeChallengeSummary = "";
                _activeChallengeRewards = "";

                var dataNode = _sceneData != null ? _sceneData.FindNode(node.Id) : null;
                // Entrée du nœud = carte sans lien entrant (routage explicite),
                // repli sur l'index 0 si cycle ou setempat ambigu.
                _currentDialogueIndex = FindEntryDialogueIndex(dataNode);
                // Snapshot des positions : anti validation par proximité gratuite
                // (spawn sur l'objectif qui skippe le nœud dès son entrée).
                _nodeEnterCoords.Clear();
                for (int p = 0; p < _playerParty.Count; p++)
                {
                    var u = _playerParty[p];
                    if (u != null) _nodeEnterCoords[u] = u.CurrentCoords;
                }
                if (dataNode != null)
                {
                    // Déploiement des acteurs programmés pour apparaître à ce nœud (SpawnOnNodeId)
                    SpawnActorsForNode(node.Id, dataNode.TriggerCombatOnEnter);

                    // Cinématiques d'entrée de nœud : non-bloquantes (la suite s'enclenche aussitôt).
                    FireLinkedCinematics(dataNode.CinematicIds);

                    ExecuteNodeEvents(dataNode);

                    if (dataNode.TriggerCombatOnEnter && _turnManager != null && _turnManager.IsInExploration)
                    {
                        _turnManager.EnterCombatMode();
                    }

                    // Les spawns d'entrée (SpawnOnNodeId) arrivent après le
                    // snapshot initial : sans ça, hasEntry=false => moved=true
                    // => validation proximitée gratuite dès la frame suivante.
                    for (int p = 0; p < _playerParty.Count; p++)
                    {
                        var u = _playerParty[p];
                        if (u != null && !_nodeEnterCoords.ContainsKey(u))
                            _nodeEnterCoords[u] = u.CurrentCoords;
                    }
                }

                ApplyCameraFocusForCurrentLine();
            }

            // Déclencheurs d'entrée : évalués en continu pendant l'exploration
            // des nœuds Objectif (actions carte, conditions d'acteurs, flags...).
            // Les nœuds de dialogue/choix les évaluent à leur sortie (boutons).
            if (node.Kind == ScenarioNodeKind.Objective
                && _activeReactionChoice == null
                && !CurrentLineHasPendingChoice())
            {
                TryFireReadyTrigger();
            }
        }

        private void ApplyCameraFocusForCurrentLine()
        {
            var dataNode = _sceneData.FindNode(_director.CurrentNode?.Id);
            if (dataNode == null || dataNode.Dialogues.Count == 0 || _currentDialogueIndex >= dataNode.Dialogues.Count) return;

            var line = dataNode.Dialogues[_currentDialogueIndex];

            // Évaluation des prérequis : si non satisfaits, passage immédiat à la réplique suivante
            if (line.Prerequisite != null && line.Prerequisite.HasPrerequisite)
            {
                if (!line.Prerequisite.IsMet(_playerParty, _turnManager?.ActiveUnit, _director))
                {
                    CombatHUD.Instance?.AddAdvancedLog($"<color=grey>[PRÉREQUIS NON SATISFAIT] Réplique '{line.LineId}' ignorée ({line.Prerequisite.GetSummary()}).</color>", LogCategory.MovementAndTurns, "[NARRATION]", Color.gray);
                    AdvanceDialogueOrNode();
                    return;
                }
            }

            // Cinématiques liées à la réplique : non-bloquantes (le dialogue continue aussitôt).
            FireLinkedCinematics(line.CinematicIds);

            // Gestion de l'ambiance et des cues audio
            ApplyAmbienceSettings(line.Ambience, line.SoundCueId);

            // Gestion des tests de compétence automatiques
            if (line.AutoSkillCheck != null && line.AutoSkillCheck.HasAutoCheck)
            {
                ExecuteAutoSkillCheck(line);
            }

            var cam = FindAnyObjectByType<TacticalCameraController>();
            if (!string.IsNullOrEmpty(line.CameraFocusActorId) && _spawnedActors.TryGetValue(line.CameraFocusActorId, out var focusUnit))
            {
                if (cam != null && focusUnit != null)
                {
                    cam.FocusOn(focusUnit.transform);
                    cam.SetPitchAndDistance(line.CameraPitch, line.CameraDistance);
                }

                focusUnit.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText($"💬 {line.SpeakerId}", Color.cyan);
            }
        }

        private void ApplyAmbienceSettings(SceneAmbienceData ambience, string fallbackSoundCueId)
        {
            string sound = ambience != null && !string.IsNullOrEmpty(ambience.SoundCueId) ? ambience.SoundCueId : fallbackSoundCueId;
            if (!string.IsNullOrEmpty(sound) && KilltimeAudioManager.Instance != null)
            {
                if (Enum.TryParse<SoundId>(sound, out var soundEnum))
                {
                    KilltimeAudioManager.Instance.PlayUI(soundEnum, 0.55f);
                }
            }

            if (ambience != null && ambience.HasAmbience)
            {
                if (ambience.ChangeMusic && KilltimeAudioManager.Instance != null)
                {
                    KilltimeAudioManager.Instance.PlayMusic(ambience.MusicMood, ambience.MusicIntensity, false);
                }

                if (ambience.CameraShakeIntensity > 0f)
                {
                    GrenadeCameraShake.Shake(ambience.CameraShakeIntensity);
                }

                if (ambience.TriggerAlarm)
                {
                    CombatHUD.Instance?.AddAdvancedLog("🚨 <b>ALERTE SYSTÈME :</b> Sirènes enclenchées dans le secteur.", LogCategory.Combat, "[ALARME]", Color.red);
                }
            }
        }

        private void ExecuteAutoSkillCheck(SceneDialogueLineData line)
        {
            var check = line.AutoSkillCheck;
            TacticalUnit actorUnit = null;

            if (!string.IsNullOrEmpty(check.SpecificActorId) && _spawnedActors.TryGetValue(check.SpecificActorId, out var specificUnit))
            {
                actorUnit = specificUnit;
            }
            else if (_turnManager != null && _turnManager.ActiveUnit != null && _turnManager.ActiveUnit.IsPlayerControlled)
            {
                actorUnit = _turnManager.ActiveUnit;
            }
            else if (_playerParty.Count > 0)
            {
                actorUnit = _playerParty[0];
            }

            var sheet = actorUnit != null ? actorUnit.Sheet : null;
            string actorName = sheet != null ? sheet.Name : "Protagoniste";
            Attributes effectiveAttr = sheet != null ? sheet.GetEffectiveAttributes() : new Attributes();

            int rank = SkillDefinitions.GetBaseRank(check.RequiredSkill, effectiveAttr, false);
            int steps = SkillDefinitions.CharacteristicSteps(rank);
            int training = sheet != null ? sheet.GetSkill(check.RequiredSkill).TrainingLevel : 0;
            DiceType die = SkillDefinitions.DieFromTotalSteps(steps + training);

            var roller = new DiceRoller();
            var result = roller.Roll(die, modifier: 0, targetDC: check.TargetDC);

            string skillName = SkillDefinitions.GetDisplayName(check.RequiredSkill);
            string outcomeStr = result.IsSuccess ? "SUCCÈS PASSİF" : "ÉCHEC PASSİF";
            Color col = result.IsSuccess ? Color.green : new Color(1.0f, 0.45f, 0.2f);

            CombatHUD.Instance?.AddAdvancedLog(
                $"🎲 <b>[TEST AUTO]</b> {actorName} teste <b>{skillName}</b> ({die}) contre SD {check.TargetDC} ➔ Résultat: <b>{result.Total}</b> ({result.Differential:+0;-0;0}) — <color=#{ColorUtility.ToHtmlStringRGB(col)}>{outcomeStr}</color>",
                LogCategory.Combat,
                "[PASSİF]",
                col
            );

            actorUnit?.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText($"{outcomeStr} ({result.Total} vs SD {check.TargetDC})", col);

            if (result.IsSuccess)
            {
                if (check.SuccessEffects != null && check.SuccessEffects.Count > 0)
                {
                    _director?.ApplyEffects(check.SuccessEffects);
                }

                if (!string.IsNullOrEmpty(check.SuccessSpeech))
                {
                    line.Speech = check.SuccessSpeech;
                    if (!string.IsNullOrEmpty(check.SuccessStageDirection)) line.StageDirection = check.SuccessStageDirection;
                }

                if (!string.IsNullOrEmpty(check.SuccessNextLineId))
                {
                    line.NextLineId = check.SuccessNextLineId;
                }
            }
            else
            {
                if (check.FailureEffects != null && check.FailureEffects.Count > 0)
                {
                    _director?.ApplyEffects(check.FailureEffects);
                }

                if (!string.IsNullOrEmpty(check.FailureSpeech))
                {
                    line.Speech = check.FailureSpeech;
                    if (!string.IsNullOrEmpty(check.FailureStageDirection)) line.StageDirection = check.FailureStageDirection;
                }

                if (!string.IsNullOrEmpty(check.FailureNextLineId))
                {
                    line.NextLineId = check.FailureNextLineId;
                }
            }
        }

        public void ExecuteNodeEvents(SceneNodeData nodeData)
        {
            if (nodeData == null || nodeData.Events.Count == 0) return;

            for (int i = 0; i < nodeData.Events.Count; i++)
            {
                ExecuteScenicEvent(nodeData.Events[i]);
            }
        }

        public void ExecuteScenicEvent(SceneEventData evt)
        {
            if (evt == null) return;

            // Prérequis de l'événement
            if (evt.Prerequisite != null && evt.Prerequisite.HasPrerequisite)
            {
                if (!evt.Prerequisite.IsMet(_playerParty, _turnManager?.ActiveUnit, _director))
                {
                    CombatHUD.Instance?.AddAdvancedLog($"<color=grey>[ÉVÉNEMENT IGNORÉ] '{evt.Title}' ({evt.Prerequisite.GetSummary()}).</color>", LogCategory.MovementAndTurns, "[ÉVÉNEMENT]", Color.gray);
                    return;
                }
            }

            // Cinématiques liées à l'événement : non-bloquantes.
            FireLinkedCinematics(evt.CinematicIds);

            // Ambiance & Audio
            ApplyAmbienceSettings(evt.Ambience, "");

            // Action spécifique de l'événement
            switch (evt.Kind)
            {
                case SceneEventKind.TriggerCombat:
                    SpawnActorsForNode(_director?.CurrentNode?.Id, true);
                    _turnManager?.EnterCombatMode();
                    CombatHUD.Instance?.AddAdvancedLog($"⚡ <b>ÉVÉNEMENT :</b> Combat déclenché [{evt.Title}].", LogCategory.Combat, "[COMBAT]", Color.red);
                    break;

                case SceneEventKind.SpawnEnemies:
                    SpawnActorsForNode(_director?.CurrentNode?.Id, true);
                    _turnManager?.EnterCombatMode();
                    CombatHUD.Instance?.AddAdvancedLog($"⚡ <b>ÉVÉNEMENT :</b> Déploiement d'unités [{evt.Title}].", LogCategory.Combat, "[RENFORTS]", Color.red);
                    break;

                case SceneEventKind.CameraFocus:
                    if (!string.IsNullOrEmpty(evt.TargetActorId) && _spawnedActors.TryGetValue(evt.TargetActorId, out var targetUnit))
                    {
                        var cam = FindAnyObjectByType<TacticalCameraController>();
                        cam?.FocusOn(targetUnit.transform);
                        cam?.SetPitchAndDistance(evt.CameraPitch, evt.CameraDistance);
                    }
                    break;

                case SceneEventKind.CameraShake:
                    GrenadeCameraShake.Shake(evt.Ambience.CameraShakeIntensity > 0 ? evt.Ambience.CameraShakeIntensity : 0.35f);
                    break;

                case SceneEventKind.ObjectiveUpdate:
                    if (!string.IsNullOrEmpty(evt.CompletionObjectiveId))
                    {
                        _director?.SetObjectiveComplete(evt.CompletionObjectiveId, true);
                    }
                    break;

                case SceneEventKind.GrantRewards:
                    if (evt.Rewards != null && _playerParty.Count > 0 && _playerParty[0].Sheet != null)
                    {
                        var s = _playerParty[0].Sheet;
                        if (evt.Rewards.EarnCredits > 0) s.EarnCredits(evt.Rewards.EarnCredits);
                        if (evt.Rewards.EarnXP > 0) { s.AvailableXP += evt.Rewards.EarnXP; s.TotalEarnedXP += evt.Rewards.EarnXP; }
                        if (!string.IsNullOrEmpty(evt.Rewards.ItemRewardName))
                            s.AddItem(new InventoryItem { Name = evt.Rewards.ItemRewardName, Type = evt.Rewards.ItemRewardType });
                    }
                    break;
            }

            // Effets de campagne de l'événement
            if (evt.Effects != null && evt.Effects.Count > 0)
            {
                _director?.ApplyEffects(evt.Effects);
            }

            CombatHUD.Instance?.AddAdvancedLog($"⚡ <b>ÉVÉNEMENT EXÉCUTÉ :</b> {evt.Title}", LogCategory.MovementAndTurns, "[ÉVÉNEMENT]", Color.yellow);
        }

        private void AdvanceDialogueOrNode()
        {
            AdvanceDialogueOrNodeInternal(null, false);
        }

        private void AdvanceDialogueOrNodeInternal(HashSet<string> visited, bool skipLink = false)
        {
            var dataNode = _sceneData.FindNode(_director.CurrentNode?.Id);
            if (dataNode != null && _currentDialogueIndex >= 0 && _currentDialogueIndex < dataNode.Dialogues.Count)
            {
                var currentLine = dataNode.Dialogues[_currentDialogueIndex];
                if (!skipLink && !string.IsNullOrEmpty(currentLine.NextLineId))
                {
                    if (TryAdvanceToId(currentLine.NextLineId, visited)) return;
                }

                // Liaison vide ou cible introuvable = fin du nœud (pas de fallback
                // séquentiel : le routage est explicite via les fenêtres normales).
            }

            var node = _director.CurrentNode;
            if (node == null) return;

            if (node.Kind == ScenarioNodeKind.Resolution)
            {
                if (!TryFireReadyTrigger()) _director.CompleteActiveScenario();
            }
            else if (node.Kind == ScenarioNodeKind.Objective && !_director.AreRequiredObjectivesComplete())
            {
                // Objectifs requis incomplets : on reste (pas de fuite en avant),
                // mais on l'explique au lieu de rester muet.
                if (!TryFireReadyTrigger())
                    CombatHUD.Instance?.AddAdvancedLog("🎯 <b>Objectifs requis incomplets.</b> Terminez les objectifs principaux pour poursuivre.", LogCategory.MovementAndTurns, "[OBJECTIF]", Color.yellow);
            }
            else
            {
                // Nœud épuisé (Briefing / Choice / Objective validé) : on suit le lien
                // vers le nœud suivant, sinon la scène est terminée -> transition + scène suivante.
                if (!TryFireReadyTrigger() && !_director.Continue()) _director.CompleteActiveScenario();
            }
        }

        private void SelectDialogueChoice(SceneDialogueChoiceData choice, SceneDialogueLineData promptLine)
        {
            if (choice == null || promptLine == null) return;

            if (choice.Challenge != null && choice.Challenge.HasChallenge)
            {
                ExecuteSkillChallenge(choice, promptLine);
                return;
            }

            if (choice.Effects != null && choice.Effects.Count > 0 && _director != null)
            {
                _director.ApplyEffects(choice.Effects);
            }

            CombatHUD.Instance?.AddAdvancedLog($"💬 [Choix] {choice.Label}", LogCategory.MovementAndTurns, "[DIALOGUE]", Color.white);

            // Routage direct vers fenêtre normale : pas d'écran de réaction intermédiaire.
            // Choix sans cible = fin du nœud (routage explicite, pas de séquentiel).
            if (!string.IsNullOrEmpty(choice.NextLineId))
            {
                if (TryAdvanceToId(choice.NextLineId, null)) return;
            }

            AdvanceDialogueOrNode();
        }

        private void GrantRewardItem(CharacterSheet sheet, string itemName, ItemType itemType, int quantity, ref string rewardsSummary)
        {
            if (sheet == null || string.IsNullOrEmpty(itemName)) return;
            int qty = Mathf.Max(1, quantity);
            var catalogDef = ArmoryCatalog.GetByName(itemName);
            InventoryItem rewardItem;
            if (catalogDef != null)
            {
                rewardItem = catalogDef.Clone();
                rewardItem.Quantity = qty;
                rewardItem.IsEquipped = false;
            }
            else
            {
                rewardItem = new InventoryItem
                {
                    Name = itemName,
                    Type = itemType,
                    PriceCE = 100,
                    Quantity = qty
                };
            }
            sheet.AddItem(rewardItem);
            rewardsSummary += qty > 1 ? $"[{itemName} x{qty}]  " : $"[{itemName}]  ";
        }

        // Avance vers une réplique ou une conséquence (carte 🎁). Retourne false si l'id
        // est vide, introuvable, ou si une chaîne de conséquences boucle (anti-blocage).
        private bool TryAdvanceToId(string id, HashSet<string> visited)
        {
            if (string.IsNullOrEmpty(id)) return false;
            var dataNode = _sceneData.FindNode(_director.CurrentNode?.Id);
            if (dataNode == null) return false;

            int targetIdx = dataNode.FindDialogueIndex(id);
            if (targetIdx >= 0)
            {
                _currentDialogueIndex = targetIdx;
                ApplyCameraFocusForCurrentLine();
                return true;
            }

            var cons = dataNode.FindConsequence(id);
            if (cons != null)
            {
                SceneDialogueLineData prompt = null;
                if (_currentDialogueIndex >= 0 && _currentDialogueIndex < dataNode.Dialogues.Count)
                    prompt = dataNode.Dialogues[_currentDialogueIndex];
                return ExecuteConsequenceChain(cons, prompt, visited ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            }

            return false;
        }

        // Entrée du nœud = première carte dialogue sans lien entrant (routage explicite).
        // Repli sur 0 si aucune (cycle) ou liste vide.
        private static int FindEntryDialogueIndex(SceneNodeData dataNode)
        {
            if (dataNode == null || dataNode.Dialogues == null || dataNode.Dialogues.Count == 0) return 0;
            if (dataNode.Dialogues.Count == 1) return 0;
            var incoming = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < dataNode.Dialogues.Count; i++)
            {
                var l = dataNode.Dialogues[i];
                if (l == null) continue;
                if (!string.IsNullOrEmpty(l.NextLineId)) incoming.Add(l.NextLineId);
                if (l.Choices != null)
                {
                    for (int c = 0; c < l.Choices.Count; c++)
                    {
                        var ch = l.Choices[c];
                        if (ch == null) continue;
                        if (!string.IsNullOrEmpty(ch.NextLineId)) incoming.Add(ch.NextLineId);
                        var chal = ch.Challenge;
                        if (chal != null && chal.HasChallenge)
                        {
                            if (!string.IsNullOrEmpty(chal.SuccessNextLineId)) incoming.Add(chal.SuccessNextLineId);
                            if (!string.IsNullOrEmpty(chal.FailureNextLineId)) incoming.Add(chal.FailureNextLineId);
                        }
                    }
                }
                if (l.AutoSkillCheck != null && l.AutoSkillCheck.HasAutoCheck)
                {
                    if (!string.IsNullOrEmpty(l.AutoSkillCheck.SuccessNextLineId)) incoming.Add(l.AutoSkillCheck.SuccessNextLineId);
                    if (!string.IsNullOrEmpty(l.AutoSkillCheck.FailureNextLineId)) incoming.Add(l.AutoSkillCheck.FailureNextLineId);
                }
            }
            if (dataNode.Consequences != null)
            {
                for (int k = 0; k < dataNode.Consequences.Count; k++)
                {
                    var cons = dataNode.Consequences[k];
                    if (cons == null) continue;
                    if (!string.IsNullOrEmpty(cons.NextLineId)) incoming.Add(cons.NextLineId);
                }
            }
            for (int i = 0; i < dataNode.Dialogues.Count; i++)
            {
                var l = dataNode.Dialogues[i];
                if (l == null || string.IsNullOrEmpty(l.LineId)) continue;
                if (!incoming.Contains(l.LineId)) return i;
            }
            return 0;
        }

        private void ApplyConsequencePayload(SceneConsequenceData cons, out string rewardsSummary)
        {
            rewardsSummary = "";
            if (cons == null) return;

            // Cinématiques liées à la conséquence : non-bloquantes.
            FireLinkedCinematics(cons.CinematicIds);

            TacticalUnit unit = null;
            if (_turnManager != null && _turnManager.ActiveUnit != null && _turnManager.ActiveUnit.IsPlayerControlled)
                unit = _turnManager.ActiveUnit;
            else if (_playerParty.Count > 0)
                unit = _playerParty[0];
            var sheet = unit != null ? unit.Sheet : null;

            var rew = cons.Rewards;
            if (rew != null && sheet != null)
            {
                if (rew.EarnCredits > 0)
                {
                    sheet.EarnCredits(rew.EarnCredits);
                    rewardsSummary += $"+{rew.EarnCredits} CE  ";
                }
                if (rew.EarnXP > 0)
                {
                    sheet.AvailableXP += rew.EarnXP;
                    sheet.TotalEarnedXP += rew.EarnXP;
                    rewardsSummary += $"+{rew.EarnXP} XP  ";
                }
                if (!string.IsNullOrEmpty(rew.ItemRewardName))
                    GrantRewardItem(sheet, rew.ItemRewardName, rew.ItemRewardType, rew.ItemRewardQuantity, ref rewardsSummary);
                if (rew.AdditionalItems != null)
                {
                    for (int i = 0; i < rew.AdditionalItems.Count; i++)
                    {
                        var extra = rew.AdditionalItems[i];
                        if (extra == null || string.IsNullOrEmpty(extra.ItemName)) continue;
                        GrantRewardItem(sheet, extra.ItemName, extra.ItemRewardType, extra.Quantity, ref rewardsSummary);
                    }
                }
                if (!string.IsNullOrEmpty(rew.CompletionObjectiveId))
                {
                    _director?.SetObjectiveComplete(rew.CompletionObjectiveId, true);
                    rewardsSummary += "✔ Objectif validé  ";
                }
            }

            if (cons.Effects != null && cons.Effects.Count > 0 && _director != null)
                _director.ApplyEffects(cons.Effects);

            string who = sheet != null ? sheet.Name : "Escouade";
            string fxTag = cons.Effects != null && cons.Effects.Count > 0 ? $" (+{cons.Effects.Count} effet(s))" : "";
            CombatHUD.Instance?.AddAdvancedLog(
                $"🎁 <b>[CONSÉQUENCE]</b> {who} — <b>{cons.Title}</b>{(string.IsNullOrEmpty(rewardsSummary) ? "" : $" : {rewardsSummary}")}{fxTag}",
                LogCategory.MovementAndTurns,
                "[BUTIN]",
                Color.yellow
            );

            if (cons.TriggersCombat)
            {
                SpawnActorsForNode(_director?.CurrentNode?.Id, true);
                _turnManager?.EnterCombatMode();
                CombatHUD.Instance?.AddAdvancedLog("🚨 <b>CONSÉQUENCE :</b> Combat déclenché !", LogCategory.Combat, "[ALERTE]", Color.red);
            }
        }

        // Exécute une conséquence et sa chaîne (cons → cons). Retourne true si le flux a
        // progressé (saut de réplique, affichage dialogue, avance d'index).
        private bool ExecuteConsequenceChain(SceneConsequenceData first, SceneDialogueLineData promptLine, HashSet<string> visited)
        {
            if (first == null) return false;
            visited ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dataNode = _sceneData.FindNode(_director.CurrentNode?.Id);
            if (dataNode == null) return false;

            var cons = first;
            int guard = 0;
            while (cons != null && guard++ < 8)
            {
                if (!visited.Add(cons.ConsequenceId)) return false;

                ApplyConsequencePayload(cons, out string rewardsSummary);
                string onwardId = !string.IsNullOrEmpty(cons.NextLineId) ? cons.NextLineId : cons.NextConsequenceId;

                if (cons.HasDialogue && !string.IsNullOrWhiteSpace(cons.Speech))
                {
                    var displayPrompt = new SceneDialogueLineData
                    {
                        SpeakerId = !string.IsNullOrEmpty(cons.SpeakerId) ? cons.SpeakerId : (promptLine != null ? promptLine.SpeakerId : "???"),
                        StageDirection = cons.StageDirection ?? "",
                        CameraFocusActorId = promptLine != null ? promptLine.CameraFocusActorId : "",
                        CameraPitch = promptLine != null ? promptLine.CameraPitch : 42f,
                        CameraDistance = promptLine != null ? promptLine.CameraDistance : 9.5f
                    };
                    SetupReactionDisplay(displayPrompt, cons.Speech, cons.StageDirection, onwardId, $"🎁 {cons.Title}", rewardsSummary);
                    return true;
                }

                if (!string.IsNullOrEmpty(cons.NextConsequenceId))
                {
                    var next = dataNode.FindConsequence(cons.NextConsequenceId);
                    if (next != null) { cons = next; continue; }
                }
                if (!string.IsNullOrEmpty(cons.NextLineId))
                {
                    int idx = dataNode.FindDialogueIndex(cons.NextLineId);
                    if (idx >= 0)
                    {
                        _currentDialogueIndex = idx;
                        ApplyCameraFocusForCurrentLine();
                        return true;
                    }
                }

                // Cul-de-sac : avancer sans rejouer le lien d'origine (skipLink).
                AdvanceDialogueOrNodeInternal(visited, true);
                return true;
            }
            return true;
        }

        private void ExecuteSkillChallenge(SceneDialogueChoiceData choice, SceneDialogueLineData promptLine)
        {
            var challenge = choice.Challenge;

            TacticalUnit actorUnit = null;
            if (!string.IsNullOrEmpty(challenge.SpecificActorId) && _spawnedActors.TryGetValue(challenge.SpecificActorId, out var specificUnit))
            {
                actorUnit = specificUnit;
            }
            else if (_turnManager != null && _turnManager.ActiveUnit != null && _turnManager.ActiveUnit.IsPlayerControlled)
            {
                actorUnit = _turnManager.ActiveUnit;
            }
            else if (_playerParty.Count > 0)
            {
                actorUnit = _playerParty[0];
            }

            var sheet = actorUnit != null ? actorUnit.Sheet : null;
            string actorName = sheet != null ? sheet.Name : "Protagoniste";
            Attributes effectiveAttr = sheet != null ? sheet.GetEffectiveAttributes() : new Attributes();

            int rank = SkillDefinitions.GetBaseRank(challenge.RequiredSkill, effectiveAttr, false);
            int steps = SkillDefinitions.CharacteristicSteps(rank);
            int training = sheet != null ? sheet.GetSkill(challenge.RequiredSkill).TrainingLevel : 0;
            DiceType skillDie = SkillDefinitions.DieFromTotalSteps(steps + training);

            string skillName = SkillDefinitions.GetDisplayName(challenge.RequiredSkill);
            var roller = new DiceRoller();

            DiceRollResult result;
            string vsLabel;
            string oppSkillName = "";
            string oppName = "";
            int oppTotal = 0;

            if (challenge.IsOpposed)
            {
                // Défi opposé aveugle (Livre II §7) : mises masquées des deux camps,
                // les deux dés sont lancés ensemble et révélés simultanément.
                TacticalUnit oppUnit = null;
                if (!string.IsNullOrEmpty(challenge.OpposedActorId))
                    _spawnedActors.TryGetValue(challenge.OpposedActorId, out oppUnit);
                else if (promptLine != null && !string.IsNullOrEmpty(promptLine.SpeakerId))
                    _spawnedActors.TryGetValue(promptLine.SpeakerId, out oppUnit);

                oppSkillName = SkillDefinitions.GetDisplayName(challenge.OpposedSkill);
                if (oppUnit != null && oppUnit.Sheet != null)
                {
                    var oppSheet = oppUnit.Sheet;
                    oppName = !string.IsNullOrEmpty(oppSheet.Name) ? oppSheet.Name : challenge.OpposedActorId;
                    Attributes oppAttr = oppSheet.GetEffectiveAttributes();
                    int oppRank = SkillDefinitions.GetBaseRank(challenge.OpposedSkill, oppAttr, false);
                    int oppTraining = oppSheet.GetSkill(challenge.OpposedSkill).TrainingLevel;
                    DiceType oppDie = SkillDefinitions.DieFromTotalSteps(SkillDefinitions.CharacteristicSteps(oppRank) + oppTraining);
                    var oppRoll = roller.Roll(oppDie, modifier: challenge.OpposedBonus, targetDC: 0);
                    oppTotal = oppRoll.Total;
                    // Mémorise le brut pour le log.
                    result = roller.Roll(skillDie, modifier: 0, targetDC: oppTotal);
                    vsLabel = $"{actorName} [{skillName} {skillDie}={result.RawRoll} → {result.Total}] vs {oppName} [{oppSkillName} {oppDie}={oppRoll.RawRoll}{(challenge.OpposedBonus != 0 ? $" +{challenge.OpposedBonus}" : "")} → {oppTotal}]";
                }
                else
                {
                    oppName = !string.IsNullOrEmpty(challenge.OpposedActorId) ? challenge.OpposedActorId : "Opposition";
                    oppTotal = challenge.TargetDC + challenge.OpposedBonus;
                    result = roller.Roll(skillDie, modifier: 0, targetDC: oppTotal);
                    vsLabel = $"{actorName} [{skillName} {skillDie}={result.RawRoll} → {result.Total}] vs {oppName} [{oppSkillName} fixe {oppTotal}]";
                }
            }
            else
            {
                result = roller.Roll(skillDie, modifier: 0, targetDC: challenge.TargetDC);
                vsLabel = $"{actorName} teste <b>{skillName}</b> ({skillDie}) contre SD {challenge.TargetDC}";
            }

            string outcomeTag = result.IsSuccess ? (result.IsCriticalSuccess ? "RÉUSSITE CRITIQUE" : "SUCCÈS") : (result.IsCriticalFailure ? "ÉCHEC CRITIQUE" : "ÉCHEC");
            Color outcomeCol = result.IsSuccess ? (result.IsCriticalSuccess ? new Color(1f, 0.85f, 0.2f) : Color.green) : (result.IsCriticalFailure ? Color.red : new Color(1f, 0.5f, 0.2f));

            string defiTag = challenge.IsOpposed ? "DÉFI OPPOSÉ AVEUGLE" : "DÉFI";
            string detailRes = challenge.IsOpposed
                ? $"Révélation simultanée (mises cachées): <b>{result.Total} vs {oppTotal}</b> (Diff: {result.Differential:+0;-0;0})"
                : $"Résultat: <b>{result.Total}</b> (Brut {result.RawRoll}, Diff: {result.Differential:+0;-0;0})";

            CombatHUD.Instance?.AddAdvancedLog(
                $"{(challenge.IsOpposed ? "⚔️" : "🎲")} <b>[{defiTag}]</b> {vsLabel} ➔ {detailRes} — <color=#{ColorUtility.ToHtmlStringRGB(outcomeCol)}>{outcomeTag}</color>",
                LogCategory.Combat,
                "[DÉFI]",
                outcomeCol
            );

            string floatVs = challenge.IsOpposed ? $"{result.Total} vs {oppTotal}" : $"{result.Total} vs SD {challenge.TargetDC}";
            actorUnit?.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText(
                $"{outcomeTag} ({floatVs})",
                outcomeCol
            );

            if (result.IsSuccess)
            {
                if (KilltimeAudioManager.Instance != null)
                {
                    KilltimeAudioManager.Instance.PlayUI(SoundId.UI_Filter, 0.7f);
                }

                if (challenge.SuccessEffects != null && challenge.SuccessEffects.Count > 0)
                {
                    _director?.ApplyEffects(challenge.SuccessEffects);
                }
                if (choice.Effects != null && choice.Effects.Count > 0)
                {
                    _director?.ApplyEffects(choice.Effects);
                }

                string rewardsSummary = "";
                if (challenge.SuccessRewards != null)
                {
                    var rew = challenge.SuccessRewards;
                    if (rew.EarnCredits > 0 && sheet != null)
                    {
                        sheet.EarnCredits(rew.EarnCredits);
                        rewardsSummary += $"+{rew.EarnCredits} CE  ";
                    }
                    if (rew.EarnXP > 0 && sheet != null)
                    {
                        sheet.AvailableXP += rew.EarnXP;
                        sheet.TotalEarnedXP += rew.EarnXP;
                        rewardsSummary += $"+{rew.EarnXP} XP  ";
                    }
                    if (!string.IsNullOrEmpty(rew.ItemRewardName) && sheet != null)
                    {
                        GrantRewardItem(sheet, rew.ItemRewardName, rew.ItemRewardType, rew.ItemRewardQuantity, ref rewardsSummary);
                    }
                    if (rew.AdditionalItems != null)
                    {
                        for (int ri = 0; ri < rew.AdditionalItems.Count; ri++)
                        {
                            var extra = rew.AdditionalItems[ri];
                            if (extra == null || string.IsNullOrEmpty(extra.ItemName)) continue;
                            GrantRewardItem(sheet, extra.ItemName, extra.ItemRewardType, extra.Quantity, ref rewardsSummary);
                        }
                    }
                    if (!string.IsNullOrEmpty(rew.CompletionObjectiveId))
                    {
                        _director?.SetObjectiveComplete(rew.CompletionObjectiveId, true);
                        rewardsSummary += "✔ Objectif validé  ";
                    }
                }

                if (!string.IsNullOrEmpty(rewardsSummary))
                {
                    CombatHUD.Instance?.AddAdvancedLog($"🎁 <b>Récompenses obtenues :</b> {rewardsSummary}", LogCategory.MovementAndTurns, "[BUTIN]", Color.yellow);
                }

                string targetLineId = !string.IsNullOrWhiteSpace(challenge.SuccessConsequenceId) ? challenge.SuccessConsequenceId
                    : (!string.IsNullOrWhiteSpace(challenge.SuccessNextLineId) ? challenge.SuccessNextLineId : choice.NextLineId);
                string successVs = challenge.IsOpposed ? $"{result.Total} vs {oppTotal}" : $"{result.Total} vs SD {challenge.TargetDC}";
                string successSkill = challenge.IsOpposed ? $"{skillName} vs {oppSkillName}" : skillName;

                // Routage direct vers fenêtre normale (pas de réaction intermédiaire).
                CombatHUD.Instance?.AddAdvancedLog($"✔ {outcomeTag} ({successVs}) — {successSkill}{(string.IsNullOrEmpty(rewardsSummary) ? "" : $" 🎁 {rewardsSummary}")}", LogCategory.Combat, "[DÉFI]", Color.green);
                if (!TryAdvanceToId(targetLineId, null))
                    AdvanceDialogueOrNode();
            }
            else
            {
                if (KilltimeAudioManager.Instance != null)
                {
                    KilltimeAudioManager.Instance.Play(SoundId.Impact_DeepBoom, 0.6f);
                }
                GrenadeCameraShake.Shake(0.2f);

                if (challenge.FailureEffects != null && challenge.FailureEffects.Count > 0)
                {
                    _director?.ApplyEffects(challenge.FailureEffects);
                }

                if (challenge.FailureTriggersCombat)
                {
                    SpawnActorsForNode(_director?.CurrentNode?.Id, true);
                    _turnManager?.EnterCombatMode();
                    CombatHUD.Instance?.AddAdvancedLog("🚨 <b>ÉCHEC CRITIQUE :</b> Alerte déclenchée ! Déploiement d'urgence hostile.", LogCategory.Combat, "[ALERTE]", Color.red);
                }

                string targetLineId = !string.IsNullOrWhiteSpace(challenge.FailureConsequenceId) ? challenge.FailureConsequenceId
                    : (!string.IsNullOrWhiteSpace(challenge.FailureNextLineId) ? challenge.FailureNextLineId : choice.NextLineId);
                string failVs = challenge.IsOpposed ? $"{result.Total} vs {oppTotal}" : $"{result.Total} vs SD {challenge.TargetDC}";
                string failSkill = challenge.IsOpposed ? $"{skillName} vs {oppSkillName}" : skillName;

                // Routage direct vers fenêtre normale (pas de réaction intermédiaire).
                CombatHUD.Instance?.AddAdvancedLog($"✕ {outcomeTag} ({failVs}) — {failSkill}", LogCategory.Combat, "[DÉFI]", new Color(1f, 0.5f, 0.2f));
                if (!TryAdvanceToId(targetLineId, null))
                    AdvanceDialogueOrNode();
            }
        }

        private void SetupReactionDisplay(SceneDialogueLineData promptLine, string speech, string stageDirection, string nextLineId, string challengeSummary = "", string rewardsText = "")
        {
            _activeReactionChoice = new SceneDialogueChoiceData
            {
                ReactionSpeech = speech,
                ReactionStageDirection = stageDirection,
                NextLineId = nextLineId
            };
            _activeReactionPromptLine = promptLine;
            _activeChallengeSummary = challengeSummary;
            _activeChallengeRewards = rewardsText;

            if (!string.IsNullOrEmpty(promptLine.CameraFocusActorId) && _spawnedActors.TryGetValue(promptLine.CameraFocusActorId, out var focusUnit))
            {
                var cam = FindAnyObjectByType<TacticalCameraController>();
                if (cam != null && focusUnit != null)
                {
                    cam.FocusOn(focusUnit.transform);
                    cam.SetPitchAndDistance(promptLine.CameraPitch, promptLine.CameraDistance);
                }
                focusUnit.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText($"💬 {promptLine.SpeakerId}", Color.yellow);
            }

            if (KilltimeAudioManager.Instance != null)
            {
                KilltimeAudioManager.Instance.PlayUI(SoundId.UI_Filter, 0.45f);
            }

            string stage = !string.IsNullOrEmpty(stageDirection) ? $" <i>({stageDirection})</i>" : "";
            CombatHUD.Instance?.AddAdvancedLog($"<b>[{promptLine.SpeakerId}]</b>{stage} {speech}", LogCategory.MovementAndTurns, "[RÉPONSE]", Color.cyan);
        }

        private void ConfirmReactionAndAdvance()
        {
            if (_activeReactionChoice == null) return;

            var choice = _activeReactionChoice;
            _activeReactionChoice = null;
            _activeReactionPromptLine = null;
            _activeChallengeSummary = "";
            _activeChallengeRewards = "";

            // File d'attente du chat milieu (ex : holotable puis datapad) :
            // afficher le message suivant avant de reprendre le flux normal.
            if (_pendingMiddleChat.Count > 0)
            {
                ShowMiddleChatMessage(_pendingMiddleChat.Dequeue());
                return;
            }

            if (!string.IsNullOrEmpty(choice.NextLineId))
            {
                if (TryAdvanceToId(choice.NextLineId, null)) return;
            }

            // Sans cible = fin du nœud (routage explicite).
            AdvanceDialogueOrNodeInternal(null, true);
        }

        private void OnGUI()
        {
            if (CombatHUD.IsPaused || (StorySceneManager.Instance != null && StorySceneManager.Instance.IsTransitioning)) return;

            _interactButtonRects.Clear();
            DrawPartySwitchBar();
            DrawInteractablesWorldOverlay();

            var node = _director != null ? _director.CurrentNode : null;
            if (node == null)
            {
                _dialogueRect = Rect.zero;
                return;
            }

            DrawDialogueBox(node);
        }

        private void DrawPartySwitchBar()
        {
            if (_turnManager == null || !_turnManager.IsInExploration || _playerParty.Count <= 1)
            {
                _hasPartyBar = false;
                _partyBarRect = Rect.zero;
                return;
            }

            float barWidth = _playerParty.Count * 110f;
            Rect rect = new Rect(24f, 96f, barWidth, 38f);
            _partyBarRect = rect;
            _hasPartyBar = true;
            GUI.Box(rect, GUIContent.none);

            GUILayout.BeginArea(rect);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < _playerParty.Count; i++)
            {
                var unit = _playerParty[i];
                bool active = (_turnManager.ActiveUnit == unit);
                GUI.backgroundColor = active ? new Color(0.2f, 0.8f, 1f) : Color.white;
                if (GUILayout.Button($"[{i + 1}] {unit.Stats.Name}", GUILayout.Height(28)))
                {
                    SelectOperative(unit);
                }
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void DrawInteractablesWorldOverlay()
        {
            if (UnityEngine.Camera.main == null || _turnManager == null || _turnManager.ActiveUnit == null) return;

            var activeUnit = _turnManager.ActiveUnit;

            for (int i = 0; i < _interactables.Count; i++)
            {
                var it = _interactables[i];
                if (it == null || !it.CanInteract(activeUnit)) continue;

                var node = _grid?.GetNode(it.Coordinates);
                Vector3 worldPos = node != null ? node.WorldPosition + Vector3.up * 1.2f : it.transform.position;
                Vector3 screenPos = UnityEngine.Camera.main.WorldToScreenPoint(worldPos);

                if (screenPos.z > 0)
                {
                    float y = Screen.height - screenPos.y;
                    Rect r = new Rect(screenPos.x - 90f, y - 24f, 180f, 26f);
                    _interactButtonRects.Add(r);
                    GUI.backgroundColor = new Color(0.2f, 0.9f, 0.5f);
                    if (GUI.Button(r, $"[E] {it.ActionLabel}"))
                    {
                        it.TryInteract(activeUnit, out string log);
                        if (!string.IsNullOrEmpty(log))
                            CombatHUD.Instance?.AddAdvancedLog(log, LogCategory.MovementAndTurns, "[ACTION]", Color.yellow);
                    }
                    GUI.backgroundColor = Color.white;
                }
            }
        }

        private void DrawDialogueBox(ScenarioNode node)
        {
            var dataNode = _sceneData.FindNode(node.Id);
            bool hasDialogues = dataNode != null && dataNode.Dialogues.Count > 0 && _currentDialogueIndex < dataNode.Dialogues.Count;

            var line = hasDialogues ? dataNode.Dialogues[_currentDialogueIndex] : null;
            bool isReactionActive = _activeReactionChoice != null && _activeReactionPromptLine != null;
            bool hasChoices = !isReactionActive && line != null && line.Choices.Count > 0;

            float boxWidth = Mathf.Min(920f, Screen.width - 40f);
            float baseHeight = 150f;
            if (hasChoices)
            {
                baseHeight += line.Choices.Count * 34f;
            }
            else if (isReactionActive)
            {
                baseHeight += 20f;
            }

            float boxHeight = Mathf.Min(baseHeight, Screen.height - 120f);
            Rect rect = new Rect((Screen.width - boxWidth) * 0.5f, Screen.height - boxHeight - 20f, boxWidth, boxHeight);
            _dialogueRect = rect;

            GUI.Box(rect, GUIContent.none);
            GUILayout.BeginArea(rect);
            GUILayout.Space(6);

            if (isReactionActive)
            {
                var promptLine = _activeReactionPromptLine;
                var reaction = _activeReactionChoice;

                GUILayout.BeginHorizontal();
                GUI.color = Color.cyan;
                GUILayout.Label($"<b>【 {promptLine.SpeakerId} 】</b>", GUILayout.Width(180));
                GUI.color = Color.white;

                if (!string.IsNullOrEmpty(reaction.ReactionStageDirection))
                {
                    GUILayout.Label($"<color=grey><i>({reaction.ReactionStageDirection})</i></color>");
                }
                GUILayout.FlexibleSpace();
                GUILayout.Label("<color=#00E5FF>[RÉACTION]</color>", GUILayout.Width(90));
                GUILayout.EndHorizontal();

                if (!string.IsNullOrEmpty(_activeChallengeSummary))
                {
                    GUILayout.BeginHorizontal();
                    GUI.color = _activeChallengeSummary.StartsWith("✔") ? Color.green : new Color(1f, 0.45f, 0.35f);
                    GUILayout.Label($"<b>{_activeChallengeSummary}</b>");
                    GUI.color = Color.white;
                    if (!string.IsNullOrEmpty(_activeChallengeRewards))
                    {
                        GUI.color = Color.yellow;
                        GUILayout.Label($"🎁 {_activeChallengeRewards}");
                        GUI.color = Color.white;
                    }
                    GUILayout.EndHorizontal();
                }

                GUILayout.Space(4);
                GUILayout.Label($"« {reaction.ReactionSpeech} »", GUILayout.ExpandHeight(true));

                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUI.backgroundColor = new Color(0.2f, 0.8f, 1f);
                if (GUILayout.Button("Continuer (Espace) ➔", GUILayout.Width(200), GUILayout.Height(30)))
                {
                    ConfirmReactionAndAdvance();
                }
                GUI.backgroundColor = Color.white;
                GUILayout.EndHorizontal();
            }
            else if (hasDialogues)
            {
                GUILayout.BeginHorizontal();
                GUI.color = Color.cyan;
                GUILayout.Label($"<b>【 {line.SpeakerId} 】</b>", GUILayout.Width(180));
                GUI.color = Color.white;

                if (!string.IsNullOrEmpty(line.StageDirection))
                {
                    GUILayout.Label($"<color=grey><i>({line.StageDirection})</i></color>");
                }
                GUILayout.FlexibleSpace();
                GUILayout.Label($"<color=grey>{_currentDialogueIndex + 1} / {dataNode.Dialogues.Count}</color>", GUILayout.Width(50));
                GUILayout.EndHorizontal();

                GUILayout.Space(4);
                GUILayout.Label($"« {line.Speech} »");

                if (hasChoices)
                {
                    GUILayout.Space(6);
                    GUILayout.Label("<color=#FFD54F><b>Faites votre choix :</b></color>");
                    for (int c = 0; c < line.Choices.Count; c++)
                    {
                        var choice = line.Choices[c];
                        bool isMet = choice.Prerequisite == null || !choice.Prerequisite.HasPrerequisite || choice.Prerequisite.IsMet(_playerParty, _turnManager?.ActiveUnit, _director);

                        bool hasChallenge = choice.Challenge != null && choice.Challenge.HasChallenge;
                        bool isOpposed = hasChallenge && choice.Challenge.IsOpposed;
                        string prefix = !isMet
                            ? $"🔒 [{choice.Prerequisite.GetSummary()}] "
                            : (hasChallenge
                                ? (isOpposed
                                    ? $"⚔️ [{choice.Challenge.GetShortLabel()}] "
                                    : $"🎲 [{choice.Challenge.GetShortLabel()}] ")
                                : "➤  ");
                        if (isMet && !string.IsNullOrEmpty(choice.NextLineId) && dataNode != null && dataNode.FindConsequence(choice.NextLineId) != null)
                            prefix += "🎁 ";

                        string btnLabel = $"{prefix}{choice.Label}";

                        GUI.enabled = isMet;
                        GUI.backgroundColor = !isMet
                            ? Color.gray
                            : (hasChallenge
                                ? (isOpposed ? new Color(1.0f, 0.55f, 0.25f) : new Color(1.0f, 0.78f, 0.25f))
                                : new Color(0.25f, 0.75f, 1.0f));

                        if (GUILayout.Button(btnLabel, GUILayout.Height(30)))
                        {
                            SelectDialogueChoice(choice, line);
                        }

                        GUI.backgroundColor = Color.white;
                        GUI.enabled = true;
                    }
                }
                else
                {
                    GUILayout.FlexibleSpace();
                    GUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();

                    // Liaison vide = fin du nœud : on montre les actions de sortie,
                    // sinon bouton Suivant vers la cible routée.
                    if (!string.IsNullOrEmpty(line.NextLineId))
                    {
                        if (GUILayout.Button("Suivant (Espace) ➔", GUILayout.Width(170), GUILayout.Height(28)))
                        {
                            AdvanceDialogueOrNode();
                        }
                    }
                    else
                    {
                        DrawActionButtons(node);
                    }
                    GUILayout.EndHorizontal();
                }
            }
            else
            {
                GUILayout.Label($"<b>{node.Title}</b> — <color=orange>{node.Location}</color>");
                GUILayout.Label(node.Body);
                GUILayout.FlexibleSpace();
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                DrawActionButtons(node);
                GUILayout.EndHorizontal();
            }

            GUILayout.EndArea();
        }

        private void DrawActionButtons(ScenarioNode node)
        {
            if (node.Kind == ScenarioNodeKind.Briefing)
            {
                GUI.backgroundColor = new Color(0.2f, 0.8f, 1f);
                if (GUILayout.Button($"➔ {node.ContinueLabel}", GUILayout.Width(240), GUILayout.Height(28)))
                {
                    if (!TryFireReadyTrigger()) _director.Continue();
                }
                GUI.backgroundColor = Color.white;
            }
            else if (node.Kind == ScenarioNodeKind.Choice)
            {
                if (TryFireReadyTrigger()) return;
                if (node.Choices != null && node.Choices.Count > 0)
                {
                    foreach (var choice in node.Choices)
                    {
                        if (GUILayout.Button(choice.Label, GUILayout.Height(28)))
                        {
                            _director.Choose(choice.Id);
                        }
                    }
                }
                else
                {
                    GUI.backgroundColor = new Color(0.2f, 0.8f, 1f);
                    string label = !string.IsNullOrWhiteSpace(node.ContinueLabel) ? node.ContinueLabel : "Continuer";
                    if (GUILayout.Button($"➔ {label}", GUILayout.Width(240), GUILayout.Height(28)))
                    {
                        if (!TryFireReadyTrigger()) _director.Continue();
                    }
                    GUI.backgroundColor = Color.white;
                }
            }
            else if (node.Kind == ScenarioNodeKind.Objective)
            {
                bool complete = _director.AreRequiredObjectivesComplete();
                GUI.enabled = complete;
                GUI.backgroundColor = complete ? new Color(0.25f, 0.85f, 0.45f) : Color.gray;
                if (GUILayout.Button(complete ? $"✔ {node.ContinueLabel}" : "Objectifs requis non validés", GUILayout.Width(260), GUILayout.Height(28)))
                {
                    if (!TryFireReadyTrigger()) _director.Continue();
                }
                GUI.backgroundColor = Color.white;
                GUI.enabled = true;
            }
            else if (node.Kind == ScenarioNodeKind.Resolution)
            {
                GUI.backgroundColor = new Color(0.2f, 0.9f, 0.5f);
                string btnText = string.IsNullOrWhiteSpace(node.ContinueLabel) ? "✔ Conclure & Scène Suivante ➔" : $"✔ {node.ContinueLabel} ➔";
                if (GUILayout.Button(btnText, GUILayout.Width(280), GUILayout.Height(28)))
                {
                    if (!TryFireReadyTrigger()) _director.CompleteActiveScenario();
                }
                GUI.backgroundColor = Color.white;
            }
        }

        public void CleanupScene()
        {
            Killtime.Tactics.Units.DroppedWeaponPickup.ClearAllDropped();
            foreach (var kvp in _spawnedActors)
            {
                if (kvp.Value != null) Destroy(kvp.Value.gameObject);
            }
            _spawnedActors.Clear();
            _playerParty.Clear();

            for (int i = 0; i < _interactables.Count; i++)
            {
                if (_interactables[i] != null) Destroy(_interactables[i].gameObject);
            }
            _interactables.Clear();

            _grid?.ClearOccupancy();
            _turnManager?.ClearUnits();

            if (gameObject != null) Destroy(gameObject);
        }

        private void OnDestroy()
        {
            FloatingWindowChrome.UnregisterExtraBlocker(IsPointerOverSceneChat);
            _dialogueRect = Rect.zero;
            _partyBarRect = Rect.zero;
            _hasPartyBar = false;
            _interactButtonRects.Clear();
            CleanupScene();
        }
    }
}
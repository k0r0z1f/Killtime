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

        private int _currentDialogueIndex = 0;
        private string _lastNodeId = "";
        private SceneDialogueChoiceData _activeReactionChoice = null;
        private SceneDialogueLineData _activeReactionPromptLine = null;
        private string _activeChallengeSummary = "";
        private string _activeChallengeRewards = "";

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

        private void OnEnable()
        {
            FloatingWindowChrome.RegisterExtraBlocker(IsPointerOverSceneChat);
        }

        private void OnDisable()
        {
            FloatingWindowChrome.UnregisterExtraBlocker(IsPointerOverSceneChat);
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

        public void SpawnActorsForNode(string nodeId)
        {
            if (_sceneData == null || _sceneData.Actors == null || string.IsNullOrEmpty(nodeId)) return;

            for (int i = 0; i < _sceneData.Actors.Count; i++)
            {
                var actorData = _sceneData.Actors[i];
                if (actorData == null) continue;
                if (_spawnedActors.ContainsKey(actorData.ActorId)) continue;
                if (!string.Equals(actorData.SpawnOnNodeId, nodeId, StringComparison.OrdinalIgnoreCase)) continue;

                var unit = SpawnSingleActor(actorData);
                if (unit != null)
                {
                    _turnManager?.RegisterUnit(unit);
                    if (!actorData.IsPlayer)
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
                        _activatedInteractableIds.Add(data.InteractableId);
                    }
                    if (!string.IsNullOrEmpty(objId))
                    {
                        _director?.SetObjectiveComplete(objId, true);
                    }
                    if (!string.IsNullOrEmpty(successMsg))
                    {
                        CombatHUD.Instance?.AddAdvancedLog($"✔ {unit.Stats.Name} : {successMsg}", LogCategory.MovementAndTurns, "[ACTION]", Color.yellow);
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

            for (int p = 0; p < _playerParty.Count; p++)
            {
                var member = _playerParty[p];
                if (member == null) continue;

                for (int i = 0; i < _sceneData.Interactables.Count; i++)
                {
                    var it = _sceneData.Interactables[i];
                    if (!string.IsNullOrEmpty(it.CompletionObjectiveId) && !_director.IsObjectiveComplete(it.CompletionObjectiveId))
                    {
                        if (member.CurrentCoords.DistanceTo(new HexCoordinates(it.Q, it.R)) <= it.Radius)
                        {
                            _director.SetObjectiveComplete(it.CompletionObjectiveId, true);
                            if (!string.IsNullOrEmpty(it.InteractableId))
                            {
                                _activatedInteractableIds.Add(it.InteractableId);
                            }
                            member.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText($"✔ Objectif Validé", Color.green);
                        }
                    }
                }
            }
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
                    return !string.IsNullOrEmpty(trigger.InteractableId) && _activatedInteractableIds.Contains(trigger.InteractableId);

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
                _currentDialogueIndex = 0;
                _activeReactionChoice = null;
                _activeReactionPromptLine = null;
                _activeChallengeSummary = "";
                _activeChallengeRewards = "";

                var dataNode = _sceneData != null ? _sceneData.FindNode(node.Id) : null;
                if (dataNode != null)
                {
                    ExecuteNodeEvents(dataNode);

                    if (dataNode.TriggerCombatOnEnter && _turnManager != null && _turnManager.IsInExploration)
                    {
                        SpawnActorsForNode(node.Id);
                        _turnManager.EnterCombatMode();
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

            // Ambiance & Audio
            ApplyAmbienceSettings(evt.Ambience, "");

            // Action spécifique de l'événement
            switch (evt.Kind)
            {
                case SceneEventKind.TriggerCombat:
                    SpawnActorsForNode(_director?.CurrentNode?.Id);
                    _turnManager?.EnterCombatMode();
                    CombatHUD.Instance?.AddAdvancedLog($"⚡ <b>ÉVÉNEMENT :</b> Combat déclenché [{evt.Title}].", LogCategory.Combat, "[COMBAT]", Color.red);
                    break;

                case SceneEventKind.SpawnEnemies:
                    SpawnActorsForNode(_director?.CurrentNode?.Id);
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
            var dataNode = _sceneData.FindNode(_director.CurrentNode?.Id);
            if (dataNode != null && _currentDialogueIndex >= 0 && _currentDialogueIndex < dataNode.Dialogues.Count)
            {
                var currentLine = dataNode.Dialogues[_currentDialogueIndex];
                if (!string.IsNullOrEmpty(currentLine.NextLineId))
                {
                    int targetIdx = dataNode.FindDialogueIndex(currentLine.NextLineId);
                    if (targetIdx >= 0)
                    {
                        _currentDialogueIndex = targetIdx;
                        ApplyCameraFocusForCurrentLine();
                        return;
                    }
                }

                if (_currentDialogueIndex < dataNode.Dialogues.Count - 1)
                {
                    _currentDialogueIndex++;
                    ApplyCameraFocusForCurrentLine();
                    return;
                }
            }

            var node = _director.CurrentNode;
            if (node == null) return;

            if (node.Kind == ScenarioNodeKind.Briefing)
            {
                if (!TryFireReadyTrigger()) _director.Continue();
            }
            else if (node.Kind == ScenarioNodeKind.Objective && _director.AreRequiredObjectivesComplete())
            {
                if (!TryFireReadyTrigger()) _director.Continue();
            }
            else if (node.Kind == ScenarioNodeKind.Resolution)
            {
                if (!TryFireReadyTrigger()) _director.CompleteActiveScenario();
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

            if (!string.IsNullOrWhiteSpace(choice.ReactionSpeech))
            {
                SetupReactionDisplay(promptLine, choice.ReactionSpeech, choice.ReactionStageDirection, choice.NextLineId);
            }
            else
            {
                var dataNode = _sceneData.FindNode(_director.CurrentNode?.Id);
                if (dataNode != null && !string.IsNullOrEmpty(choice.NextLineId))
                {
                    int targetIdx = dataNode.FindDialogueIndex(choice.NextLineId);
                    if (targetIdx >= 0)
                    {
                        _currentDialogueIndex = targetIdx;
                        ApplyCameraFocusForCurrentLine();
                        return;
                    }
                }

                if (dataNode != null && _currentDialogueIndex < dataNode.Dialogues.Count - 1)
                {
                    _currentDialogueIndex++;
                    ApplyCameraFocusForCurrentLine();
                }
                else
                {
                    AdvanceDialogueOrNode();
                }
            }
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

            var roller = new DiceRoller();
            var result = roller.Roll(skillDie, modifier: 0, targetDC: challenge.TargetDC);

            string skillName = SkillDefinitions.GetDisplayName(challenge.RequiredSkill);
            string outcomeTag = result.IsSuccess ? (result.IsCriticalSuccess ? "RÉUSSITE CRITIQUE" : "SUCCÈS") : (result.IsCriticalFailure ? "ÉCHEC CRITIQUE" : "ÉCHEC");
            Color outcomeCol = result.IsSuccess ? (result.IsCriticalSuccess ? new Color(1f, 0.85f, 0.2f) : Color.green) : (result.IsCriticalFailure ? Color.red : new Color(1f, 0.5f, 0.2f));

            CombatHUD.Instance?.AddAdvancedLog(
                $"🎲 <b>[DÉFI]</b> {actorName} teste <b>{skillName}</b> ({skillDie}) contre SD {challenge.TargetDC} ➔ Résultat: <b>{result.Total}</b> (Brut {result.RawRoll}, Diff: {result.Differential:+0;-0;0}) — <color=#{ColorUtility.ToHtmlStringRGB(outcomeCol)}>{outcomeTag}</color>",
                LogCategory.Combat,
                "[DÉFI]",
                outcomeCol
            );

            actorUnit?.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText(
                $"{outcomeTag} ({result.Total} vs SD {challenge.TargetDC})",
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
                        var rewardItem = new InventoryItem
                        {
                            Name = rew.ItemRewardName,
                            Type = rew.ItemRewardType,
                            PriceCE = 100,
                            Quantity = 1
                        };
                        sheet.AddItem(rewardItem);
                        rewardsSummary += $"[{rew.ItemRewardName}]  ";
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

                string replySpeech = !string.IsNullOrWhiteSpace(challenge.SuccessSpeech) ? challenge.SuccessSpeech : choice.ReactionSpeech;
                string replyStage = !string.IsNullOrWhiteSpace(challenge.SuccessStageDirection) ? challenge.SuccessStageDirection : choice.ReactionStageDirection;
                string targetLineId = !string.IsNullOrWhiteSpace(challenge.SuccessNextLineId) ? challenge.SuccessNextLineId : choice.NextLineId;

                SetupReactionDisplay(promptLine, replySpeech, replyStage, targetLineId, $"✔ {outcomeTag} ({result.Total} vs SD {challenge.TargetDC}) — {skillName}", rewardsSummary);
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
                    SpawnActorsForNode(_director?.CurrentNode?.Id);
                    _turnManager?.EnterCombatMode();
                    CombatHUD.Instance?.AddAdvancedLog("🚨 <b>ÉCHEC CRITIQUE :</b> Alerte déclenchée ! Déploiement d'urgence hostile.", LogCategory.Combat, "[ALERTE]", Color.red);
                }

                string replySpeech = !string.IsNullOrWhiteSpace(challenge.FailureSpeech) ? challenge.FailureSpeech : "Votre tentative se solde par un échec.";
                string replyStage = challenge.FailureStageDirection;
                string targetLineId = challenge.FailureNextLineId;

                SetupReactionDisplay(promptLine, replySpeech, replyStage, targetLineId, $"✕ {outcomeTag} ({result.Total} vs SD {challenge.TargetDC}) — {skillName}", "");
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

            var dataNode = _sceneData.FindNode(_director.CurrentNode?.Id);
            if (dataNode == null) return;

            if (!string.IsNullOrEmpty(choice.NextLineId))
            {
                int targetIdx = dataNode.FindDialogueIndex(choice.NextLineId);
                if (targetIdx >= 0)
                {
                    _currentDialogueIndex = targetIdx;
                    ApplyCameraFocusForCurrentLine();
                    return;
                }
            }

            if (_currentDialogueIndex < dataNode.Dialogues.Count - 1)
            {
                _currentDialogueIndex++;
                ApplyCameraFocusForCurrentLine();
            }
            else
            {
                AdvanceDialogueOrNode();
            }
        }

        private void OnGUI()
        {
            if (CombatHUD.IsPaused || (StorySceneManager.Instance != null && StorySceneManager.Instance.IsTransitioning)) return;

            _interactButtonRects.Clear();
            DrawPartySwitchBar();
            DrawInteractablesWorldOverlay();

            var node = _director != null ? _director.CurrentNode : null;
            if (node == null) return;

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
                        string prefix = !isMet
                            ? $"🔒 [{choice.Prerequisite.GetSummary()}] "
                            : (hasChallenge ? $"🎲 [SD {choice.Challenge.TargetDC} · {SkillDefinitions.GetDisplayName(choice.Challenge.RequiredSkill)}] " : "➤  ");

                        string btnLabel = $"{prefix}{choice.Label}";

                        GUI.enabled = isMet;
                        GUI.backgroundColor = !isMet
                            ? Color.gray
                            : (hasChallenge ? new Color(1.0f, 0.78f, 0.25f) : new Color(0.25f, 0.75f, 1.0f));

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

                    if (_currentDialogueIndex < dataNode.Dialogues.Count - 1 || !string.IsNullOrEmpty(line.NextLineId))
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
                foreach (var choice in node.Choices)
                {
                    if (GUILayout.Button(choice.Label, GUILayout.Height(28)))
                    {
                        _director.Choose(choice.Id);
                    }
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
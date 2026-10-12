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
    public partial class JsonStorySceneController : MonoBehaviour, IStorySceneController
    {
        // État centralisé : tous les champs d'instance ont migré dans SceneState (move-only).
        internal readonly SceneState State = new();

        // Throttle session de l'erreur "CinematicDirector absent" (log jeu 1x, console à chaque fois).
        private static bool _warnedMissingCinematicDirector = false;

        private const float InteractablePopupAutoCloseDelay = 7f;
        private const float InteractablePopupFadeDuration = 1f;

        public StorySceneData SceneData => State._sceneData;

        public void SetSceneData(StorySceneData data)
        {
            State._sceneData = data;
        }

        public IReadOnlyDictionary<string, TacticalUnit> SpawnedActors => State._spawnedActors;
        public IReadOnlyList<TacticalInteractable> SpawnedInteractables => State._interactables;

        public bool TryGetSpawnedInteractable(string interactableId, out TacticalInteractable prop)
        {
            prop = null;
            if (string.IsNullOrWhiteSpace(interactableId)) return false;
            for (int i = 0; i < State._interactables.Count; i++)
            {
                var p = State._interactables[i];
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

        private void RegisterSceneHudExclusionZones()
        {
            State._partyBarZone ??= () => (State._hasPartyBar && State._partyBarRect.width > 0f && State._partyBarRect.height > 0f)
                ? State._partyBarRect : Rect.zero;
            State._dialogueBoxZone ??= () => (State._dialogueRect.width > 0f && State._dialogueRect.height > 0f)
                ? State._dialogueRect : Rect.zero;
            FloatingWindowChrome.RegisterExclusionZone(State._partyBarZone);
            FloatingWindowChrome.RegisterExclusionZone(State._dialogueBoxZone);
        }

        private void UnregisterSceneHudExclusionZones()
        {
            if (State._partyBarZone != null) FloatingWindowChrome.UnregisterExclusionZone(State._partyBarZone);
            if (State._dialogueBoxZone != null) FloatingWindowChrome.UnregisterExclusionZone(State._dialogueBoxZone);
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
            if (State._hasPartyBar && State._partyBarRect.width > 0f && State._partyBarRect.height > 0f && State._partyBarRect.Contains(mouseGui))
                return true;
            if (State._dialogueRect.width > 0f && State._dialogueRect.height > 0f && State._dialogueRect.Contains(mouseGui))
                return true;
            for (int i = 0; i < State._interactButtonRects.Count; i++)
            {
                Rect r = State._interactButtonRects[i];
                if (r.width > 0f && r.height > 0f && r.Contains(mouseGui))
                    return true;
            }
            // Filet live : avant la première OnGUI (rects encore vides), recalcule
            // le rect théorique pour ne laisser passer aucun clic.
            if ((State._dialogueRect.width <= 0f || State._dialogueRect.height <= 0f) && State._director != null && State._director.CurrentNode != null && State._chatAnimProgress > 0.05f)
            {
                Rect live = ComputeDialogueRect();
                if (live.width > 0f && live.height > 0f && live.Contains(mouseGui))
                    return true;
            }
            return false;
        }

        public IEnumerator InitializeSceneRoutine(TacticalHexGrid grid, TurnManager turnManager, CombatDevArena arena, ScenarioDirector director)
        {
            State._isSceneInitialized = false;
            State._grid = grid;
            State._turnManager = turnManager;
            State._arena = arena;
            State._director = director;

            if (State._sceneData == null)
            {
                Debug.LogError("[JsonStorySceneController] Aucune donnée de scène injectée !");
                yield break;
            }

            State._firedTriggerIds.Clear();
            State._activatedInteractableIds.Clear();
            State._consumedActionKeys.Clear();
            State._activationNodeIds.Clear();
            State._nodeEnterCoords.Clear();
            State._pendingMiddleChat.Clear();
            State._activePopups.Clear();
            State._pendingNodeJump = "";
            State._pendingNodeJumpSource = "";
            State._pendingTalkLineId = "";
            State._talkReturnNodeId = "";
            State._talkReturnLineId = "";
            State._talkLandingNodeId = "";
            // Mode scène : boîtes d'info personnages masquées par défaut.
            if (!State._overheadHudOverridden)
            {
                State._overheadHudOverridden = true;
                State._savedOverheadHud = TacticalUnitVisual.ShowOverheadHUD;
            }
            TacticalUnitVisual.ShowOverheadHUD = false;
            State._chatAnimProgress = 0f;
            State._lastActiveNode = null;
            State._activeReactionChoice = null;
            State._activeReactionPromptLine = null;
            State._activeChallengeSummary = "";
            State._activeChallengeRewards = "";

            if (State._sceneData != null && State._sceneData.HasOpeningSuperluminalWarp())
            {
                SpaceEnvironment.SetWarpForAll(1f);
            }

            if (State._arena != null)
            {
                State._arena.ClearAllUnits();
            }

            yield return LoadLinkedMapRoutine();

            if (State._arena != null)
            {
                State._arena.ClearAllUnits();
            }

            SpawnActors();
            SpawnInteractables();

            if (State._turnManager != null)
            {
                State._turnManager.ClearUnits();
                TacticalUnit leadUnit = State._playerParty.Count > 0 ? State._playerParty[0] : null;
                State._turnManager.SetExplorationMode(true, leadUnit);

                for (int i = 0; i < State._playerParty.Count; i++)
                {
                    State._turnManager.RegisterUnit(State._playerParty[i]);
                    State._playerParty[i]?.GetComponent<TacticalUnitVisual>()?.SetCombatStance(false);
                }
            }

            if (State._arena != null && State._playerParty.Count > 0)
            {
                var allies = new List<TacticalUnit>(State._playerParty);
                allies.RemoveAt(0);
                State._arena.RegisterSceneParty(State._playerParty[0], allies);
            }

            if (KilltimeAudioManager.Instance != null)
            {
                KilltimeAudioManager.Instance.StopAmbience();
                KilltimeAudioManager.Instance.StopAllSceneAmbiences(0f);
                // Kill-switch d'entrée de scène (opt-out par scène) : aucun son de
                // la scène précédente ne doit fuiter (loops, durées 0). Les files
                // en délai sont invalidées par génération (aucun son fantôme).
                bool stopOnExit = State._sceneData == null || State._sceneData.AudioSettings == null || State._sceneData.AudioSettings.StopAllSoundsOnSceneExit;
                if (stopOnExit) KilltimeAudioManager.Instance.StopAllSceneSounds(0f);
                State._soundCooldowns.Clear();
                State._sceneGeneration++;
                StorySceneManager.ResolveInitialSceneMusic(State._sceneData, out var targetMood, out var targetIntensity);
                if (KilltimeAudioManager.Instance.CurrentMood != targetMood || KilltimeAudioManager.Instance.CurrentIntensityLevel != targetIntensity)
                {
                    KilltimeAudioManager.Instance.PlayMusic(targetMood, targetIntensity, forceRestart: false);
                }
            }

            CombatHUD.Instance?.AddAdvancedLog($"🎬 <b>{State._sceneData.Title}</b> [{State._sceneData.Volume}] initialisée.", LogCategory.MovementAndTurns, "[SCÈNE]", Color.cyan);

            State._isSceneInitialized = true;
            UpdateNodeState();
        }

        private IEnumerator LoadLinkedMapRoutine()
        {
            var mapEditor = MapEditorDevWindow.Instance ?? FindAnyObjectByType<MapEditorDevWindow>();
            if (mapEditor == null)
            {
                mapEditor = new GameObject("[UI] MapEditorDevWindow").AddComponent<MapEditorDevWindow>();
            }

            if (State._sceneData.EmbeddedMap != null && State._sceneData.EmbeddedMap.ModifiedTiles.Count > 0)
            {
                var cachedUnits = State._sceneData.EmbeddedMap.PlacedUnits;
                State._sceneData.EmbeddedMap.PlacedUnits = null;
                try
                {
                    mapEditor.ApplyLoadedMap(State._sceneData.EmbeddedMap);
                }
                finally
                {
                    State._sceneData.EmbeddedMap.PlacedUnits = cachedUnits;
                }
                yield return null;
            }
            else if (!string.IsNullOrEmpty(State._sceneData.LinkedMapName))
            {
                string mapFolder = Path.Combine(Application.persistentDataPath, "Maps");
                string mapPath = Path.Combine(mapFolder, $"{State._sceneData.LinkedMapName}.json");

                if (!File.Exists(mapPath))
                {
                    mapPath = Path.Combine(mapFolder, State._sceneData.LinkedMapName);
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

            bool hasEnvironment = !string.IsNullOrEmpty(State._sceneData.EnvironmentId);
            bool hasPlaceholders = State._sceneData.EnvironmentPlaceholders != null && State._sceneData.EnvironmentPlaceholders.Count > 0;

            if (hasEnvironment || hasPlaceholders)
            {
                float initialWarp = (State._sceneData != null && State._sceneData.HasOpeningSuperluminalWarp()) ? 1f : 0f;
                if (SceneEnvironmentLibrary.Build(State._sceneData.EnvironmentId, transform, State._grid, State._sceneData.EnvironmentPlaceholders, State._sceneData.Lighting, initialWarp))
                {
                    yield return null;
                }
            }
        }

        private void Update()
        {
            if (!State._isSceneInitialized) return;
            UpdateChatAnimation();
            if (CombatHUD.IsPaused || (StorySceneManager.Instance != null && StorySceneManager.Instance.IsTransitioning)) return;
            PruneInteractablePopups();

            var director = FindAnyObjectByType<CinematicDirector>();
            if (director != null && director.IsPlayingSceneCinematic)
            {
                if (director.CurrentPlayingSceneCinematic == null || director.CurrentPlayingSceneCinematic.HideSceneChat)
                {
                    return;
                }
                // Si la cinématique active ne masque pas le chat, on autorise l'avancement du dialogue
                HandleDialoguesInput();
                return;
            }

            HandlePartyInput();
            HandleDialoguesInput();
            CheckProximityObjectives();
            UpdateNodeState();
        }

        public void CleanupScene()
        {
            State._isSceneInitialized = false;
            State._chatAnimProgress = 0f;
            State._lastActiveNode = null;
            Killtime.Tactics.Units.DroppedWeaponPickup.ClearAllDropped();
            foreach (var kvp in State._spawnedActors)
            {
                if (kvp.Value != null) Destroy(kvp.Value.gameObject);
            }
            State._spawnedActors.Clear();
            State._playerParty.Clear();

            for (int i = 0; i < State._interactables.Count; i++)
            {
                if (State._interactables[i] != null) Destroy(State._interactables[i].gameObject);
            }
            State._interactables.Clear();

            State._grid?.ClearOccupancy();
            State._turnManager?.ClearUnits();
            State._activePopups.Clear();
            ClearPendingNodeJump();
            KilltimeAudioManager.Instance?.StopAmbience();
            KilltimeAudioManager.Instance?.StopAllSceneAmbiences(0.2f);
            if (State._overheadHudOverridden)
            {
                State._overheadHudOverridden = false;
                TacticalUnitVisual.ShowOverheadHUD = State._savedOverheadHud;
            }

            if (gameObject != null) Destroy(gameObject);
        }

        private void OnDestroy()
        {
            FloatingWindowChrome.UnregisterExtraBlocker(IsPointerOverSceneChat);
            State._dialogueRect = Rect.zero;
            State._partyBarRect = Rect.zero;
            State._hasPartyBar = false;
            State._interactButtonRects.Clear();
            CleanupScene();
        }
    }
}
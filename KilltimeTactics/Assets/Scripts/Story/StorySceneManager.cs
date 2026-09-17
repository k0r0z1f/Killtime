using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics;
using Killtime.Tactics.Grid;
using Killtime.Tactics.TurnSystem;
using Killtime.UI;
using Killtime.Audio;

namespace Killtime.Story
{
    [DisallowMultipleComponent]
    public class StorySceneManager : MonoBehaviour
    {
        public static StorySceneManager Instance { get; private set; }

        [Header("Systèmes")]
        [SerializeField] private ScenarioDirector _director;
        [SerializeField] private TacticalHexGrid _grid;
        [SerializeField] private TurnManager _turnManager;
        [SerializeField] private CombatDevArena _arena;

        [Header("État de Scène")]
        [SerializeField] private string _activeScenarioId = "";
        [SerializeField] private string _autoPlayScenarioId = "volume_1_scene_01";
        [SerializeField] private bool _autoPlayOnStart = false;

        public bool IsSceneLoading { get; private set; }
        public IStorySceneController CurrentSceneController { get; private set; }

        public event Action<string> OnSceneLoadStarted;
        public event Action<string> OnSceneLoadCompleted;
        public event Action OnSceneCleanedUp;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap() => EnsureInstance();

        public static StorySceneManager EnsureInstance()
        {
            if (Instance != null) return Instance;
            var existing = UnityEngine.Object.FindAnyObjectByType<StorySceneManager>();
            if (existing != null)
            {
                Instance = existing;
                return existing;
            }

            var go = new GameObject("[Story] StorySceneManager");
            Instance = go.AddComponent<StorySceneManager>();
            return Instance;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            EnsureDependencies();
        }

        private void Start()
        {
            EnsureDependencies();
            if (_autoPlayOnStart && string.IsNullOrEmpty(_activeScenarioId))
            {
                StartScenario(_autoPlayScenarioId);
            }
        }

        private void EnsureDependencies()
        {
            if (_director == null) _director = ScenarioDirector.EnsureInstance();
            if (_grid == null) _grid = FindAnyObjectByType<TacticalHexGrid>();
            if (_turnManager == null) _turnManager = FindAnyObjectByType<TurnManager>();
            if (_arena == null) _arena = FindAnyObjectByType<CombatDevArena>();
        }

        public void StartScenario(string scenarioId)
        {
            if (IsSceneLoading) return;
            StartCoroutine(LoadScenarioRoutine(scenarioId));
        }

        private IEnumerator LoadScenarioRoutine(string scenarioId)
        {
            IsSceneLoading = true;
            OnSceneLoadStarted?.Invoke(scenarioId);

            while (CombatHUD.IsPaused)
            {
                yield return null;
            }

            EnsureDependencies();

            if (CurrentSceneController != null)
            {
                CurrentSceneController.CleanupScene();
                CurrentSceneController = null;
                OnSceneCleanedUp?.Invoke();
                yield return null;
            }

            _arena?.ClearAllUnits();
            _grid?.ClearOccupancy();
            _grid?.ClearAllCovers();

            if (!Story.Data.StorySceneRepository.TryLoadSceneData(scenarioId, out var sceneData))
            {
                Debug.LogWarning($"[StorySceneManager] JSON introuvable pour le scénario '{scenarioId}' (Scenarios/{scenarioId}.json). Contenu édité dans l'éditeur requis.");
                IsSceneLoading = false;
                OnSceneLoadCompleted?.Invoke(scenarioId);
                yield break;
            }

            var definition = ScenarioCatalog.FromSceneData(sceneData);
            if (definition == null)
            {
                Debug.LogWarning($"[StorySceneManager] Scène '{scenarioId}' illisible.");
                IsSceneLoading = false;
                OnSceneLoadCompleted?.Invoke(scenarioId);
                yield break;
            }

            ScenarioCatalog.Register(definition);
            _director.StartScenario(scenarioId);
            _activeScenarioId = scenarioId;

            var jsonGo = new GameObject($"[SceneJSON] {scenarioId}");
            var jsonController = jsonGo.AddComponent<Scenes.JsonStorySceneController>();
            jsonController.SetSceneData(sceneData);
            CurrentSceneController = jsonController;
            yield return StartCoroutine(jsonController.InitializeSceneRoutine(_grid, _turnManager, _arena, _director));

            IsSceneLoading = false;
            OnSceneLoadCompleted?.Invoke(scenarioId);

            if (KilltimeAudioManager.Instance != null)
            {
                KilltimeAudioManager.Instance.PlayMusic(MusicMood.Explore, MusicIntensity.Calm, forceRestart: true);
            }
        }

        public void CleanupCurrentScene()
        {
            if (CurrentSceneController != null)
            {
                CurrentSceneController.CleanupScene();
                CurrentSceneController = null;
                OnSceneCleanedUp?.Invoke();
            }

            _activeScenarioId = string.Empty;

            if (_arena != null)
            {
                _arena.ResetArena();
            }
            else
            {
                _grid?.ClearOccupancy();
                _grid?.ClearAllCovers();
                _turnManager?.ClearUnits();
            }
        }

        public void CompleteCurrentObjective(string objectiveId)
        {
            if (_director == null || string.IsNullOrEmpty(objectiveId)) return;
            _director.SetObjectiveComplete(objectiveId, true);
        }

        public void ProgressToNextNode()
        {
            if (IsSceneLoading || CombatHUD.IsPaused) return;
            _director?.Continue();
        }
    }

    public interface IStorySceneController
    {
        IEnumerator InitializeSceneRoutine(TacticalHexGrid grid, TurnManager turnManager, CombatDevArena arena, ScenarioDirector director);
        void CleanupScene();
    }
}
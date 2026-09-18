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
        [SerializeField] private SceneTransitionOverlay _transitionOverlay;

        [Header("État de Scène")]
        [SerializeField] private string _activeScenarioId = "";
        [SerializeField] private string _autoPlayScenarioId = "volume_1_scene_01";
        [SerializeField] private bool _autoPlayOnStart = false;

        public bool IsSceneLoading { get; private set; }
        public bool IsTransitioning => (_transitionOverlay != null && _transitionOverlay.IsTransitioning) || IsSceneLoading;
        public IStorySceneController CurrentSceneController { get; set; }
        public string ActiveScenarioId => _activeScenarioId;

        public event Action<string> OnSceneLoadStarted;
        public event Action<string> OnSceneLoadCompleted;
        public event Action OnSceneCleanedUp;
        public event Action<string, string> OnSceneTransitionStarted; // (fromSceneId, toSceneId)

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

        private void OnEnable()
        {
            EnsureDependencies();
            SubscribeEvents();
        }

        private void OnDisable()
        {
            UnsubscribeEvents();
        }

        private void Start()
        {
            EnsureDependencies();
            SubscribeEvents();

            if (_autoPlayOnStart && string.IsNullOrEmpty(_activeScenarioId))
            {
                StartScenario(_autoPlayScenarioId, useTransition: true);
            }
        }

        private void EnsureDependencies()
        {
            if (_director == null) _director = ScenarioDirector.EnsureInstance();
            if (_transitionOverlay == null) _transitionOverlay = SceneTransitionOverlay.EnsureInstance();
            if (_grid == null) _grid = FindAnyObjectByType<TacticalHexGrid>();
            if (_turnManager == null) _turnManager = FindAnyObjectByType<TurnManager>();
            if (_arena == null) _arena = FindAnyObjectByType<CombatDevArena>();
        }

        private void SubscribeEvents()
        {
            if (_director != null)
            {
                _director.ScenarioCompleted -= HandleScenarioCompleted;
                _director.ScenarioCompleted += HandleScenarioCompleted;
            }
        }

        private void UnsubscribeEvents()
        {
            if (_director != null)
            {
                _director.ScenarioCompleted -= HandleScenarioCompleted;
            }
        }

        private void HandleScenarioCompleted(ScenarioDefinition scenario)
        {
            CompleteCurrentSceneAndAdvance();
        }

        /// <summary>
        /// Conclut la scène actuelle et enchaîne directement avec la scène suivante,
        /// en appliquant l'effet de transition cinématique.
        /// </summary>
        public void CompleteCurrentSceneAndAdvance(string overrideNextScenarioId = null)
        {
            if (IsTransitioning) return;

            string currentId = _activeScenarioId;
            if (string.IsNullOrWhiteSpace(currentId) && _director != null && _director.ActiveScenario != null)
                currentId = _director.ActiveScenario.Id;

            string nextId = overrideNextScenarioId;
            if (string.IsNullOrWhiteSpace(nextId))
            {
                nextId = ScenarioCatalog.GetNextScenarioId(currentId);
            }

            if (!string.IsNullOrWhiteSpace(nextId))
            {
                Debug.Log($"[StorySceneManager] Enchaînement de la scène '{currentId}' vers '{nextId}'");
                StartScenario(nextId, useTransition: true);
            }
            else
            {
                Debug.Log($"[StorySceneManager] Aucune scène suivante détectée après '{currentId}'. Fin de séquence.");
                StartCoroutine(HandleCampaignCompleteRoutine());
            }
        }

        /// <summary>
        /// Déclare une scène démarrée hors manager (ex : bouton DÉPLOYER de l'éditeur)
        /// pour que l'enchaînement automatique retrouve la scène suivante dans la liste.
        /// </summary>
        public void NotifyExternalScenarioStarted(string scenarioId)
        {
            if (string.IsNullOrWhiteSpace(scenarioId)) return;
            _activeScenarioId = scenarioId;
        }

        private IEnumerator HandleCampaignCompleteRoutine()
        {
            var def = _director?.ActiveScenario;
            string vol = def != null ? def.Volume : "CAMPAGNE TERMINÉE";
            string title = def != null ? def.Title : "Fin de Mission";

            EnsureDependencies();
            yield return _transitionOverlay.PlayCampaignCompleteRoutine(vol, title, () =>
            {
                // Retour à l'état neutre après fermeture de l'écran de fin
                CleanupCurrentScene();
            });
        }

        /// <summary>
        /// Démarre un scénario avec ou sans transition cinématique.
        /// </summary>
        public void StartScenario(string scenarioId, bool useTransition = true)
        {
            if (IsTransitioning) return;

            if (useTransition)
            {
                StartCoroutine(LoadScenarioWithTransitionRoutine(scenarioId));
            }
            else
            {
                StartCoroutine(LoadScenarioRoutine(scenarioId));
            }
        }

        public void StartScenario(string scenarioId)
        {
            StartScenario(scenarioId, useTransition: true);
        }

        private IEnumerator LoadScenarioWithTransitionRoutine(string scenarioId)
        {
            EnsureDependencies();
            OnSceneTransitionStarted?.Invoke(_activeScenarioId, scenarioId);

            // Charger les métadonnées de la scène suivante pour alimenter le carton de titre
            string nextVolume = "MISSION TACTIQUE";
            string nextTitle = scenarioId;
            string nextCanon = "";

            if (Story.Data.StorySceneRepository.TryLoadSceneData(scenarioId, out var previewData))
            {
                if (!string.IsNullOrWhiteSpace(previewData.Volume)) nextVolume = previewData.Volume;
                if (!string.IsNullOrWhiteSpace(previewData.Title)) nextTitle = previewData.Title;
                if (!string.IsNullOrWhiteSpace(previewData.CanonReference)) nextCanon = previewData.CanonReference;
            }

            // Lancer la transition cinématique
            yield return _transitionOverlay.PlaySceneTransitionRoutine(nextVolume, nextTitle, nextCanon, () => LoadScenarioCoreRoutine(scenarioId));
        }

        private IEnumerator LoadScenarioRoutine(string scenarioId)
        {
            yield return LoadScenarioCoreRoutine(scenarioId);
        }

        private IEnumerator LoadScenarioCoreRoutine(string scenarioId)
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
            if (IsTransitioning || CombatHUD.IsPaused) return;
            _director?.Continue();
        }
    }

    public interface IStorySceneController
    {
        IEnumerator InitializeSceneRoutine(TacticalHexGrid grid, TurnManager turnManager, CombatDevArena arena, ScenarioDirector director);
        void CleanupScene();
    }
}
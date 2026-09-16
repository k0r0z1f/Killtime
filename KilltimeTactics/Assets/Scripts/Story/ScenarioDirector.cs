using System;
using UnityEngine;

namespace Killtime.Story
{
    /// <summary>
    /// Pilote de campagne indépendant de l'UI. Il expose une petite API utilisable
    /// aussi bien par une DevUI que par une future cinématique, carte ou interaction 3D.
    /// </summary>
    public class ScenarioDirector : MonoBehaviour
    {
        private const string SaveKey = "KT_CampaignState_v1";
        public static ScenarioDirector Instance { get; private set; }
        public CampaignState State { get; private set; } = new();
        public event Action StateChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap() => EnsureInstance();

        public static ScenarioDirector EnsureInstance()
        {
            if (Instance != null) return Instance;
            var existing = UnityEngine.Object.FindAnyObjectByType<ScenarioDirector>();
            if (existing != null)
            {
                Instance = existing;
                return existing;
            }

            var go = new GameObject("[Story] ScenarioDirector");
            Instance = go.AddComponent<ScenarioDirector>();
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
            Load();
        }

        public ScenarioDefinition ActiveScenario => ScenarioCatalog.Find(State.ActiveScenarioId);
        public ScenarioNode CurrentNode => ActiveScenario?.FindNode(State.CurrentNodeId);

        public void StartScenario(string scenarioId)
        {
            var definition = ScenarioCatalog.Find(scenarioId);
            if (definition == null)
            {
                Debug.LogWarning($"[ScenarioDirector] Scène introuvable: {scenarioId}");
                return;
            }

            State = new CampaignState
            {
                ActiveScenarioId = definition.Id,
                CurrentNodeId = definition.FirstNodeId
            };
            ApplyEnterEffects(CurrentNode);
            Commit();
        }

        public void ResetCampaign()
        {
            State = new CampaignState();
            Commit();
        }

        public bool IsObjectiveComplete(string objectiveId) => State.CompletedObjectiveIds.Contains(objectiveId);

        public void SetObjectiveComplete(string objectiveId, bool complete)
        {
            if (string.IsNullOrWhiteSpace(objectiveId)) return;
            if (complete)
            {
                if (!State.CompletedObjectiveIds.Contains(objectiveId)) State.CompletedObjectiveIds.Add(objectiveId);
            }
            else
            {
                State.CompletedObjectiveIds.Remove(objectiveId);
            }
            Commit();
        }

        public bool AreRequiredObjectivesComplete()
        {
            var node = CurrentNode;
            if (node == null) return false;
            foreach (var objective in node.Objectives)
            {
                if (!objective.Optional && !IsObjectiveComplete(objective.Id)) return false;
            }
            return true;
        }

        public bool Choose(string choiceId)
        {
            var node = CurrentNode;
            if (node == null) return false;
            var choice = node.Choices.Find(entry => entry.Id == choiceId);
            if (choice == null) return false;

            ApplyEffects(choice.Effects);
            MoveTo(choice.NextNodeId);
            return true;
        }

        public bool Continue()
        {
            var node = CurrentNode;
            if (node == null || string.IsNullOrWhiteSpace(node.NextNodeId)) return false;
            if (node.Kind == ScenarioNodeKind.Objective && !AreRequiredObjectivesComplete()) return false;
            MoveTo(node.NextNodeId);
            return true;
        }

        public void CompleteActiveScenario()
        {
            if (ActiveScenario == null) return;
            State.ActiveScenarioCompleted = true;
            State.AddJournal($"Scène terminée: {ActiveScenario.Title}.");
            Commit();
        }

        private void MoveTo(string nodeId)
        {
            if (ActiveScenario?.FindNode(nodeId) == null)
            {
                Debug.LogWarning($"[ScenarioDirector] Nœud introuvable: {nodeId}");
                return;
            }
            State.CurrentNodeId = nodeId;
            ApplyEnterEffects(CurrentNode);
            Commit();
        }

        private void ApplyEnterEffects(ScenarioNode node)
        {
            if (node != null) ApplyEffects(node.EnterEffects);
        }

        private void ApplyEffects(System.Collections.Generic.List<ScenarioEffect> effects)
        {
            if (effects == null) return;
            foreach (var effect in effects)
            {
                switch (effect.Type)
                {
                    case ScenarioEffectType.SetRoute:
                        State.Route = effect.Route;
                        break;
                    case ScenarioEffectType.AddFlag:
                        State.SetFlag(effect.Key);
                        break;
                    case ScenarioEffectType.AddInteger:
                        State.AddInt(effect.Key, effect.Amount);
                        break;
                    case ScenarioEffectType.AddJournal:
                        State.AddJournal(effect.Text);
                        break;
                }
            }
        }

        private void Load()
        {
            try
            {
                var json = PlayerPrefs.GetString(SaveKey, string.Empty);
                if (!string.IsNullOrWhiteSpace(json)) State = JsonUtility.FromJson<CampaignState>(json) ?? new CampaignState();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[ScenarioDirector] Sauvegarde de campagne illisible: {exception.Message}");
                State = new CampaignState();
            }
        }

        private void Commit()
        {
            try
            {
                PlayerPrefs.SetString(SaveKey, JsonUtility.ToJson(State));
                PlayerPrefs.Save();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[ScenarioDirector] Sauvegarde de campagne impossible: {exception.Message}");
            }
            StateChanged?.Invoke();
        }
    }
}

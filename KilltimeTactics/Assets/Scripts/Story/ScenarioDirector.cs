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
        public event Action<ScenarioDefinition> ScenarioCompleted;

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
            State.EnsureWorldDefaults();
            ApplyEnterEffects(CurrentNode);
            Commit();
        }

        public void ResetCampaign()
        {
            State = new CampaignState();
            State.EnsureWorldDefaults();
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

        public bool GoToNode(string nodeId)
        {
            if (string.IsNullOrWhiteSpace(nodeId)) return false;
            if (ActiveScenario?.FindNode(nodeId) == null) return false;
            MoveTo(nodeId);
            return true;
        }

        public void CompleteActiveScenario()
        {
            if (ActiveScenario == null) return;
            var completedScenario = ActiveScenario;
            State.ActiveScenarioCompleted = true;
            State.AddJournal($"Scène terminée: {completedScenario.Title}.");
            Commit();
            ScenarioCompleted?.Invoke(completedScenario);
        }

        // ================= OVERWORLD D'HYBRIS =================

        /// <summary>
        /// Déplace le groupe vers un secteur voisin (lien direct requis).
        /// Valide le lien, les flags requis et applique les coûts (jours, journal).
        /// En godMode dev, le lien direct suffit (flags ignorés).
        /// </summary>
        public bool TravelToSector(string nodeId, bool godMode = false)
        {
            if (State == null) State = new CampaignState();
            State.EnsureWorldDefaults();
            var dest = HybrisWorldMapData.Find(nodeId);
            if (dest == null) return false;
            if (State.PartyNodeId == nodeId) return true;

            var link = HybrisWorldMapData.FindLink(State.PartyNodeId, nodeId);
            if (link == null)
            {
                Debug.LogWarning($"[ScenarioDirector] Voyage impossible : '{State.PartyNodeId}' et '{nodeId}' ne sont pas reliés.");
                return false;
            }

            if (!godMode)
            {
                if (!string.IsNullOrWhiteSpace(link.RequiresFlag) && !State.HasFlag(link.RequiresFlag))
                {
                    Debug.LogWarning($"[ScenarioDirector] Lien verrouillé (flag requis: {link.RequiresFlag}).");
                    return false;
                }
                if (!string.IsNullOrWhiteSpace(dest.RequiredFlag) && !State.HasFlag(dest.RequiredFlag))
                {
                    Debug.LogWarning($"[ScenarioDirector] Secteur verrouillé (flag requis: {dest.RequiredFlag}).");
                    return false;
                }
            }

            string fromName = HybrisWorldMapData.Find(State.PartyNodeId)?.Name ?? State.PartyNodeId;
            State.PartyNodeId = dest.Id;
            State.UnlockSector(dest.Id);
            State.VisitSector(dest.Id);
            if (!string.IsNullOrWhiteSpace(dest.GrantsFlagOnVisit))
                State.SetFlag(dest.GrantsFlagOnVisit);
            State.AddInt("jours_voyage", link.Days);
            State.AddJournal($"Voyage : {fromName} ➔ {dest.Name} ({link.Miles} miles, {link.Days} j — {link.Label}).");
            // Déverrouille les voisins pour lecture de carte (brouillard levé au contact).
            foreach (var neighbor in HybrisWorldMapData.GetLinksFor(dest.Id))
            {
                string other = neighbor.OtherEnd(dest.Id);
                if (!string.IsNullOrWhiteSpace(other)) State.UnlockSector(other);
            }
            Commit();
            return true;
        }

        /// <summary>Téléportation dev (sans validation de lien).</summary>
        public void TeleportPartyTo(string nodeId)
        {
            if (State == null) State = new CampaignState();
            State.EnsureWorldDefaults();
            var dest = HybrisWorldMapData.Find(nodeId);
            if (dest == null) return;
            State.PartyNodeId = dest.Id;
            State.UnlockSector(dest.Id);
            State.VisitSector(dest.Id);
            if (!string.IsNullOrWhiteSpace(dest.GrantsFlagOnVisit))
                State.SetFlag(dest.GrantsFlagOnVisit);
            State.AddJournal($"Téléportation dev vers {dest.Name}.");
            Commit();
        }

        public void UnlockAllSectors()
        {
            if (State == null) State = new CampaignState();
            State.EnsureWorldDefaults();
            foreach (var node in HybrisWorldMapData.ActiveNodes)
                if (node != null) State.UnlockSector(node.Id);
            Commit();
        }

        public void AllowEasternExpedition()
        {
            if (State == null) State = new CampaignState();
            State.EnsureWorldDefaults();
            State.SetFlag(HybrisWorldMapData.EasternExpeditionFlag);
            State.UnlockSector("rivage_est");
            State.AddJournal("Expédition vers le Continent Est autorisée (brumes chronales).");
            Commit();
        }

        public void ResetWorldMap()
        {
            if (State == null) State = new CampaignState();
            State.PartyNodeId = HybrisWorldMapData.StartNodeId;
            State.UnlockedNodeIds = new System.Collections.Generic.List<string>();
            State.VisitedNodeIds = new System.Collections.Generic.List<string>();
            State.EnsureWorldDefaults();
            State.AddJournal("Carte monde réinitialisée : groupe à Kingston.");
            Commit();
        }

        /// <summary>
        /// Répare les défauts monde après application d'une carte éditée, puis sauvegarde.
        /// </summary>
        public void RefreshWorldDefaults()
        {
            if (State == null) State = new CampaignState();
            State.EnsureWorldDefaults();
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

        public void ApplyEffects(System.Collections.Generic.List<ScenarioEffect> effects)
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
            Commit();
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
            if (State == null) State = new CampaignState();
            State.EnsureWorldDefaults();
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

using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Core.Character;
using Killtime.Story.Data;
using Killtime.Tactics.Grid;
using Killtime.UI;

namespace Killtime.Story.Scenes
{
    /// <summary>
    /// RD-055 — Template de scène unique clonable (board "02 Usine Vol I").
    /// UNE seule structure pour les 23-25 chapitres du Vol I : 1 env = 1 scène JSON.
    /// "New Chapter = duplicate" : <see cref="ChapterTemplateLibrary.CloneForNewChapter"/>
    /// ou menu Killtime/Chapitre/Nouveau chapitre depuis le template.
    /// Couvre : GridRadius variable, CeilingHeight, lighting preset, spawns, triggers F7.
    /// </summary>
    public enum ChapterLightingPreset
    {
        DaylightExterior = 0,
        NightExterior = 1,
        InteriorLit = 2,
        InteriorDark = 3,
        BunkerConfined = 4
    }

    /// <summary>
    /// Presets lumière calibrés pour l'usine Vol I. La portée de vision associée
    /// suit la règle FogOfWar : 12 cases extérieur dégagé, 6 cases intérieur/bunker.
    /// </summary>
    public static class ChapterLightingPresets
    {
        public static string Label(ChapterLightingPreset preset) => preset switch
        {
            ChapterLightingPreset.DaylightExterior => "Extérieur jour",
            ChapterLightingPreset.NightExterior => "Extérieur nuit",
            ChapterLightingPreset.InteriorLit => "Intérieur éclairé",
            ChapterLightingPreset.InteriorDark => "Intérieur sombre",
            ChapterLightingPreset.BunkerConfined => "Bunker confiné",
            _ => preset.ToString()
        };

        public static bool IsInterior(ChapterLightingPreset preset) => preset switch
        {
            ChapterLightingPreset.InteriorLit => true,
            ChapterLightingPreset.InteriorDark => true,
            ChapterLightingPreset.BunkerConfined => true,
            _ => false
        };

        /// <summary>Portée de vision FogOfWar recommandée (6 intérieur, 12 extérieur).</summary>
        public static int VisionRange(ChapterLightingPreset preset) => IsInterior(preset) ? 6 : 12;

        public static SceneLightingData ToLightingData(ChapterLightingPreset preset)
        {
            var data = new SceneLightingData { OverrideLighting = true };
            switch (preset)
            {
                case ChapterLightingPreset.DaylightExterior:
                    data.SunPosition = new Vector3(0f, 25f, 0f);
                    data.SunEulerAngles = new Vector3(25f, 315f, 0f);
                    data.SunColor = new Color(1.0f, 0.92f, 0.85f);
                    data.SunIntensity = 1.8f;
                    data.AmbientColor = new Color(0.28f, 0.30f, 0.35f);
                    break;
                case ChapterLightingPreset.NightExterior:
                    data.SunPosition = new Vector3(0f, 25f, 0f);
                    data.SunEulerAngles = new Vector3(155f, 315f, 0f);
                    data.SunColor = new Color(0.35f, 0.45f, 0.65f);
                    data.SunIntensity = 0.5f;
                    data.AmbientColor = new Color(0.10f, 0.12f, 0.18f);
                    break;
                case ChapterLightingPreset.InteriorLit:
                    data.SunPosition = new Vector3(0f, 8f, -2f);
                    data.SunEulerAngles = new Vector3(50f, 25f, 0f);
                    data.SunColor = new Color(0.90f, 0.94f, 1.0f);
                    data.SunIntensity = 1.1f;
                    data.AmbientColor = new Color(0.32f, 0.35f, 0.42f);
                    break;
                case ChapterLightingPreset.InteriorDark:
                    data.SunPosition = new Vector3(0f, 8f, -2f);
                    data.SunEulerAngles = new Vector3(50f, 25f, 0f);
                    data.SunColor = new Color(0.55f, 0.65f, 0.85f);
                    data.SunIntensity = 0.4f;
                    data.AmbientColor = new Color(0.15f, 0.15f, 0.18f);
                    break;
                case ChapterLightingPreset.BunkerConfined:
                    data.SunPosition = new Vector3(0f, 6f, 0f);
                    data.SunEulerAngles = new Vector3(65f, 10f, 0f);
                    data.SunColor = new Color(1.0f, 0.85f, 0.70f);
                    data.SunIntensity = 0.9f;
                    data.AmbientColor = new Color(0.22f, 0.20f, 0.18f);
                    break;
            }
            data.Shadows = LightShadows.Soft;
            data.ShadowStrength = 0.85f;
            return data;
        }
    }

    /// <summary>Un slot de spawn du template (PJ ou PNJ), converti en acteur F7.</summary>
    [Serializable]
    public class ChapterSpawnSlot
    {
        public string ActorId = "pj_1";
        public string DisplayName = "Opératif 1";
        public int Q = 0;
        public int R = 0;
        public bool IsPlayer = true;
        public float FacingAngle = 0f;
        public bool SpawnInitially = true;
        public string SpawnOnNodeId = "";
        public string ModelPrefabName = "";
        public int BaseArmor = 1;

        public SceneActorSpawnData ToActorSpawnData()
        {
            return new SceneActorSpawnData
            {
                ActorId = ActorId,
                DisplayName = DisplayName,
                Q = Q,
                R = R,
                IsPlayer = IsPlayer,
                FacingAngle = FacingAngle,
                SpawnInitially = SpawnInitially,
                SpawnOnNodeId = SpawnOnNodeId ?? "",
                ModelPrefabName = ModelPrefabName ?? "",
                BaseArmor = Mathf.Max(0, BaseArmor),
                PrimaryColor = IsPlayer ? new Color(0.2f, 0.5f, 0.9f) : new Color(0.85f, 0.25f, 0.2f),
                AccentColor = Color.cyan
            };
        }
    }

    /// <summary>Un déclencheur F7 du template (objectif, flag, interactable, état acteur).</summary>
    [Serializable]
    public class ChapterTriggerStub
    {
        public string TriggerId = "trig_objectif";
        public string Label = "Objectif accompli → assaut";
        public SceneTriggerKind Kind = SceneTriggerKind.ObjectiveCompleted;
        public string SourceNodeId = "";
        public string TargetNodeId = "node_action";
        public string ObjectiveId = "obj_principal";
        public string FlagKey = "";
        public string InteractableId = "";
        public bool OneShot = true;

        public SceneTriggerData ToTriggerData()
        {
            return new SceneTriggerData
            {
                TriggerId = TriggerId,
                Label = Label,
                Kind = Kind,
                SourceNodeId = SourceNodeId ?? "",
                TargetNodeId = TargetNodeId ?? "",
                ObjectiveId = ObjectiveId ?? "",
                FlagKey = FlagKey ?? "",
                FlagMustBeSet = true,
                InteractableId = InteractableId ?? "",
                OneShot = OneShot
            };
        }
    }

    /// <summary>
    /// Configuration sérialisable du chapitre : tout ce qui varie d'un chapitre
    /// à l'autre (le reste — grille, F7, combat — est mutualisé dans Main.unity).
    /// </summary>
    [Serializable]
    public class ChapterTemplateConfig
    {
        [Header("Chapitre")]
        public string ChapterId = "ch_00_modele";
        public string Title = "Chapitre modèle (à dupliquer)";
        public string Volume = "Volume I";
        public string EnvironmentId = "";
        public string NextSceneId = "";

        [Header("Tactique")]
        [Range(4, 15)] public int GridRadius = 8;
        [Range(1f, 12f)] public float CeilingHeight = 3.5f;
        public ChapterLightingPreset LightingPreset = ChapterLightingPreset.InteriorLit;

        [Header("Spawns F7")]
        public List<ChapterSpawnSlot> PlayerSpawns = new();
        public List<ChapterSpawnSlot> EnemySpawns = new();

        [Header("Interactable F7")]
        public string InteractableId = "terminal_1";
        public string InteractableLabel = "Terminal";
        public int InteractableQ = 4;
        public int InteractableR = 0;
        public SkillType InteractableSkill = SkillType.Observation;
        public int InteractableThreshold = 10;

        [Header("Trame F7")]
        public string FirstNodeId = "node_briefing";
        public string ActionNodeId = "node_action";
        public string BriefingTitle = "Briefing";
        public string BriefingBody = "Objectif : sécuriser la zone, puis passer à l'action.";
        public string ObjectiveId = "obj_principal";
        public string ObjectiveLabel = "Sécuriser la zone";
        public List<ChapterTriggerStub> Triggers = new();

        /// <summary>Valide la config. Retourne faux + liste d'erreurs si non clonable en l'état.</summary>
        public bool Validate(List<string> errors)
        {
            errors ??= new List<string>();
            errors.Clear();

            if (string.IsNullOrWhiteSpace(ChapterId))
                errors.Add("ChapterId vide.");
            else if (ChapterId.IndexOfAny(new[] { ' ', '/', '\\' }) >= 0)
                errors.Add($"ChapterId '{ChapterId}' : sans espaces ni slash.");

            if (GridRadius < ChapterTemplateLibrary.MinGridRadius || GridRadius > ChapterTemplateLibrary.MaxGridRadius)
                errors.Add($"GridRadius {GridRadius} hors [{ChapterTemplateLibrary.MinGridRadius}-{ChapterTemplateLibrary.MaxGridRadius}].");
            if (CeilingHeight < ChapterTemplateLibrary.MinCeilingHeight || CeilingHeight > ChapterTemplateLibrary.MaxCeilingHeight)
                errors.Add($"CeilingHeight {CeilingHeight:0.##} hors [{ChapterTemplateLibrary.MinCeilingHeight}-{ChapterTemplateLibrary.MaxCeilingHeight}]m.");

            if (string.IsNullOrWhiteSpace(FirstNodeId)) errors.Add("FirstNodeId vide.");
            if (string.IsNullOrWhiteSpace(ActionNodeId)) errors.Add("ActionNodeId vide.");
            if (!string.IsNullOrWhiteSpace(FirstNodeId) && string.Equals(FirstNodeId, ActionNodeId, StringComparison.OrdinalIgnoreCase))
                errors.Add("FirstNodeId et ActionNodeId doivent différer.");
            if (string.IsNullOrWhiteSpace(ObjectiveId)) errors.Add("ObjectiveId vide.");
            if (string.IsNullOrWhiteSpace(InteractableId)) errors.Add("InteractableId vide.");
            int clampedRadius = Mathf.Clamp(GridRadius, ChapterTemplateLibrary.MinGridRadius, ChapterTemplateLibrary.MaxGridRadius);
            if (!IsInsideRadius(InteractableQ, InteractableR, clampedRadius))
                errors.Add($"Interactable '{InteractableId}' hors grille ({InteractableQ},{InteractableR}, rayon {GridRadius}).");

            var seenActors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CheckSlots(PlayerSpawns, "PJ", seenActors, errors);
            CheckSlots(EnemySpawns, "ennemi", seenActors, errors);

            var seenTriggers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (Triggers != null)
            {
                for (int i = 0; i < Triggers.Count; i++)
                {
                    var t = Triggers[i];
                    if (t == null) { errors.Add($"Trigger[{i}] null."); continue; }
                    if (string.IsNullOrWhiteSpace(t.TriggerId)) errors.Add($"Trigger[{i}] sans TriggerId.");
                    else if (!seenTriggers.Add(t.TriggerId)) errors.Add($"TriggerId '{t.TriggerId}' en double.");
                    if (t.Kind == SceneTriggerKind.ObjectiveCompleted && string.IsNullOrWhiteSpace(t.ObjectiveId))
                        errors.Add($"Trigger '{t.TriggerId}' : ObjectiveId vide (ObjectiveCompleted).");
                    if (t.Kind == SceneTriggerKind.InteractableActivated && string.IsNullOrWhiteSpace(t.InteractableId))
                        errors.Add($"Trigger '{t.TriggerId}' : InteractableId vide (InteractableActivated).");
                    if (t.Kind == SceneTriggerKind.CampaignFlagSet && string.IsNullOrWhiteSpace(t.FlagKey))
                        errors.Add($"Trigger '{t.TriggerId}' : FlagKey vide (CampaignFlagSet).");
                }
            }

            return errors.Count == 0;
        }

        private void CheckSlots(List<ChapterSpawnSlot> slots, string kind, HashSet<string> seen, List<string> errors)
        {
            if (slots == null) return;
            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                if (s == null) { errors.Add($"Spawn {kind}[{i}] null."); continue; }
                if (string.IsNullOrWhiteSpace(s.ActorId)) errors.Add($"Spawn {kind}[{i}] sans ActorId.");
                else if (!seen.Add(s.ActorId)) errors.Add($"ActorId '{s.ActorId}' en double.");
                if (!IsInsideRadius(s.Q, s.R, Mathf.Clamp(GridRadius, ChapterTemplateLibrary.MinGridRadius, ChapterTemplateLibrary.MaxGridRadius)))
                    errors.Add($"Spawn '{s.ActorId}' hors grille ({s.Q},{s.R}, rayon {GridRadius}).");
                if (!s.SpawnInitially && string.IsNullOrWhiteSpace(s.SpawnOnNodeId))
                    errors.Add($"Spawn '{s.ActorId}' : jamais spawné (SpawnInitially=false sans SpawnOnNodeId).");
            }
        }

        private static bool IsInsideRadius(int q, int r, int radius)
        {
            if (q < -radius || q > radius || r < -radius || r > radius) return false;
            int s = -q - r;
            return s >= -radius && s <= radius;
        }

        /// <summary>Construit le StorySceneData F7/F8 complet à partir du template.</summary>
        public StorySceneData ToStorySceneData()
        {
            int radius = Mathf.Clamp(GridRadius, ChapterTemplateLibrary.MinGridRadius, ChapterTemplateLibrary.MaxGridRadius);
            float ceiling = Mathf.Clamp(CeilingHeight, ChapterTemplateLibrary.MinCeilingHeight, ChapterTemplateLibrary.MaxCeilingHeight);

            var data = new StorySceneData
            {
                Version = ChapterTemplateLibrary.TemplateVersion,
                SceneId = ChapterId,
                Volume = Volume ?? "",
                Title = Title ?? "",
                FirstNodeId = FirstNodeId,
                EnvironmentId = EnvironmentId ?? "",
                NextSceneId = NextSceneId ?? "",
                Lighting = ChapterLightingPresets.ToLightingData(LightingPreset),
                EnvironmentPlaceholders = new List<ScenePlaceholderData>(),
                EmbeddedMap = new TacticalMapSaveData
                {
                    MapName = ChapterId,
                    GridRadius = radius,
                    CeilingHeight = ceiling
                },
                Actors = new List<SceneActorSpawnData>(),
                Interactables = new List<SceneInteractableSpawnData>(),
                Nodes = new List<SceneNodeData>(),
                Triggers = new List<SceneTriggerData>()
            };

            if (PlayerSpawns != null)
                for (int i = 0; i < PlayerSpawns.Count; i++)
                    if (PlayerSpawns[i] != null) data.Actors.Add(PlayerSpawns[i].ToActorSpawnData());
            if (EnemySpawns != null)
                for (int i = 0; i < EnemySpawns.Count; i++)
                {
                    if (EnemySpawns[i] == null) continue;
                    var slot = EnemySpawns[i];
                    if (slot.SpawnInitially && !string.IsNullOrWhiteSpace(ActionNodeId))
                    {
                        slot = new ChapterSpawnSlot
                        {
                            ActorId = slot.ActorId, DisplayName = slot.DisplayName,
                            Q = slot.Q, R = slot.R, IsPlayer = slot.IsPlayer,
                            FacingAngle = slot.FacingAngle, SpawnInitially = false,
                            SpawnOnNodeId = ActionNodeId, ModelPrefabName = slot.ModelPrefabName,
                            BaseArmor = slot.BaseArmor
                        };
                    }
                    data.Actors.Add(slot.ToActorSpawnData());
                }

            data.Interactables.Add(new SceneInteractableSpawnData
            {
                InteractableId = InteractableId,
                DisplayName = InteractableLabel,
                ActionLabel = "Interagir",
                Description = "Un terminal standard. Une action legacy-compatible est préremplie.",
                Q = InteractableQ,
                R = InteractableR,
                Radius = 1,
                RequiredSkill = InteractableSkill,
                SkillThreshold = Mathf.Max(0, InteractableThreshold),
                CompletionObjectiveId = ObjectiveId,
                IsOneShot = true,
                TriggerNodeId = ActionNodeId,
                Actions = new System.Collections.Generic.List<SceneInteractableAction>
                {
                    new SceneInteractableAction
                    {
                        ActionId = "action_1",
                        Label = "Interagir",
                        RequiredSkill = (int)InteractableSkill,
                        SkillThreshold = Mathf.Max(0, InteractableThreshold),
                        CompletionObjectiveId = ObjectiveId,
                        TriggerNodeId = ActionNodeId,
                        IsOneShot = true
                    }
                }
            });

            data.Nodes.Add(new SceneNodeData
            {
                NodeId = FirstNodeId,
                Kind = ScenarioNodeKind.Briefing,
                Title = BriefingTitle,
                Body = BriefingBody,
                ContinueLabel = "Passer à l'action",
                NextNodeId = ActionNodeId,
                Objectives = new List<ScenarioObjective>
                {
                    new ScenarioObjective { Id = ObjectiveId, Label = ObjectiveLabel }
                }
            });
            data.Nodes.Add(new SceneNodeData
            {
                NodeId = ActionNodeId,
                Kind = ScenarioNodeKind.Objective,
                Title = ObjectiveLabel,
                Body = "Zone d'action : terminer l'objectif pour enchaîner.",
                ContinueLabel = "Terminer",
                TriggerCombatOnEnter = true
            });

            if (Triggers != null)
                for (int i = 0; i < Triggers.Count; i++)
                    if (Triggers[i] != null) data.Triggers.Add(Triggers[i].ToTriggerData());

            StorySceneData.EnsureDeepDefaults(data);
            return data;
        }
    }

    /// <summary>
    /// Usine du template : défauts, duplication "New Chapter", validation, application live.
    /// </summary>
    public static class ChapterTemplateLibrary
    {
        public const int TemplateVersion = 1;
        public const int MinGridRadius = 4;
        public const int MaxGridRadius = 15;
        public const float MinCeilingHeight = 1f;
        public const float MaxCeilingHeight = 12f;

        public static string BuildFileName(string chapterId) => $"{chapterId}.json";

        /// <summary>Config modèle : 4 PJ, 2 ennemis (renforts au nœud action), 1 terminal, 1 trigger.</summary>
        public static ChapterTemplateConfig CreateDefault(string chapterId = "ch_00_modele")
        {
            var config = new ChapterTemplateConfig
            {
                ChapterId = string.IsNullOrWhiteSpace(chapterId) ? "ch_00_modele" : chapterId.Trim(),
                Title = "Chapitre modèle (à dupliquer)",
                PlayerSpawns = new List<ChapterSpawnSlot>
                {
                    new ChapterSpawnSlot { ActorId = "pj_1", DisplayName = "Opératif 1", Q = 0, R = 2, IsPlayer = true },
                    new ChapterSpawnSlot { ActorId = "pj_2", DisplayName = "Opératif 2", Q = -1, R = 2, IsPlayer = true },
                    new ChapterSpawnSlot { ActorId = "pj_3", DisplayName = "Opératif 3", Q = 1, R = 1, IsPlayer = true },
                    new ChapterSpawnSlot { ActorId = "pj_4", DisplayName = "Opératif 4", Q = 0, R = 3, IsPlayer = true }
                },
                EnemySpawns = new List<ChapterSpawnSlot>
                {
                    new ChapterSpawnSlot { ActorId = "ennemi_1", DisplayName = "Hostile 1", Q = 0, R = -3, IsPlayer = false, FacingAngle = 180f, BaseArmor = 1 },
                    new ChapterSpawnSlot { ActorId = "ennemi_2", DisplayName = "Hostile 2", Q = 2, R = -2, IsPlayer = false, FacingAngle = 180f, BaseArmor = 1 }
                },
                Triggers = new List<ChapterTriggerStub>
                {
                    new ChapterTriggerStub()
                }
            };
            return config;
        }

        /// <summary>
        /// "New Chapter = duplicate" : copie profonde + renommage. Le chaînage
        /// (NextSceneId) est volontairement vidé : à recâbler dans F8.
        /// </summary>
        public static ChapterTemplateConfig CloneForNewChapter(ChapterTemplateConfig source, string newChapterId, string newTitle)
        {
            if (source == null) source = CreateDefault(newChapterId);
            string json = JsonUtility.ToJson(source);
            var clone = JsonUtility.FromJson<ChapterTemplateConfig>(json);
            if (clone == null) clone = CreateDefault(newChapterId);
            clone.ChapterId = string.IsNullOrWhiteSpace(newChapterId) ? "ch_xx_nouveau" : newChapterId.Trim();
            if (!string.IsNullOrWhiteSpace(newTitle)) clone.Title = newTitle;
            clone.NextSceneId = "";
            clone.PlayerSpawns ??= new List<ChapterSpawnSlot>();
            clone.EnemySpawns ??= new List<ChapterSpawnSlot>();
            clone.Triggers ??= new List<ChapterTriggerStub>();
            return clone;
        }

        public static bool ValidateConfig(ChapterTemplateConfig config, List<string> errors)
        {
            errors ??= new List<string>();
            errors.Clear();
            if (config == null) { errors.Add("Config null."); return false; }
            return config.Validate(errors);
        }

        /// <summary>
        /// Applique grille (rayon régénéré) + plafond + lumière sur la scène live.
        /// Retourne faux si config invalide ou grille absente (rien n'est appliqué).
        /// </summary>
        public static bool ApplyGridAndLighting(ChapterTemplateConfig config, TacticalHexGrid grid)
        {
            var errors = new List<string>();
            if (!ValidateConfig(config, errors))
            {
                Debug.LogWarning($"[ChapterTemplate] Config invalide : {string.Join(" | ", errors)}");
                return false;
            }
            if (grid == null)
            {
                Debug.LogWarning("[ChapterTemplate] Pas de TacticalHexGrid en scène.");
                return false;
            }
            try
            {
                var radiusField = typeof(TacticalHexGrid).GetField("_gridRadius",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (radiusField != null && (int)radiusField.GetValue(grid) != config.GridRadius)
                {
                    radiusField.SetValue(grid, config.GridRadius);
                    grid.GenerateGrid();
                }
                grid.CeilingHeight = config.CeilingHeight;
                SceneEnvironmentLibrary.ApplySceneLighting(ChapterLightingPresets.ToLightingData(config.LightingPreset));
                Debug.Log($"[ChapterTemplate] '{config.ChapterId}' appliqué : rayon {config.GridRadius}, plafond {config.CeilingHeight:0.##}m, lumière {ChapterLightingPresets.Label(config.LightingPreset)}.");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ChapterTemplate] Application impossible : {ex.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// Composant à poser UNE fois dans Main.unity : la scène reste unique,
    /// chaque chapitre est un clone JSON du template (F7/F8). "New Chapter = duplicate".
    /// </summary>
    [DisallowMultipleComponent]
    public class KilltimeChapterTemplate : MonoBehaviour
    {
        [Header("Template clonable (RD-055)")]
        public ChapterTemplateConfig Config = ChapterTemplateLibrary.CreateDefault();
        [Tooltip("Applique grille + plafond + lumière au démarrage (F2/F7 restent modifiables).")]
        public bool AutoApplyOnAwake = false;

        private void Awake()
        {
            if (AutoApplyOnAwake) ApplyToScene();
        }

        private void OnValidate()
        {
            if (Config == null) return;
            Config.GridRadius = Mathf.Clamp(Config.GridRadius, ChapterTemplateLibrary.MinGridRadius, ChapterTemplateLibrary.MaxGridRadius);
            Config.CeilingHeight = Mathf.Clamp(Config.CeilingHeight, ChapterTemplateLibrary.MinCeilingHeight, ChapterTemplateLibrary.MaxCeilingHeight);
        }

        [ContextMenu("RD-055 : Appliquer grille + plafond + lumière")]
        public void ApplyToScene()
        {
            var grid = GetComponent<TacticalHexGrid>() ?? FindAnyObjectByType<TacticalHexGrid>();
            ChapterTemplateLibrary.ApplyGridAndLighting(Config, grid);
        }

        [ContextMenu("RD-055 : Valider le template")]
        public void ValidateTemplate()
        {
            var errors = new List<string>();
            bool ok = ChapterTemplateLibrary.ValidateConfig(Config, errors);
            Debug.Log(ok
                ? $"[ChapterTemplate] '{Config?.ChapterId}' valide : clonable."
                : $"[ChapterTemplate] '{Config?.ChapterId}' invalide : {string.Join(" | ", errors)}");
        }

        /// <summary>Construit le StorySceneData F7 du chapitre courant (embarqué ou exporté en JSON).</summary>
        public StorySceneData BuildSceneData()
        {
            if (Config == null) Config = ChapterTemplateLibrary.CreateDefault();
            return Config.ToStorySceneData();
        }
    }
}

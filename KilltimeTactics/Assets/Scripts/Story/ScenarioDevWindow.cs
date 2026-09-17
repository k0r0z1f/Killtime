using UnityEngine;
using Killtime.UI;

namespace Killtime.Story
{
    /// <summary>
    /// Lecteur de scènes temporairement destiné à la DevUI. Il permet de tester les
    /// embranchements avant que les cartes, objectifs 3D et cinématiques y soient liés.
    /// </summary>
    public class ScenarioDevWindow : FloatingWindow<ScenarioDevWindow>
    {
        protected override int WindowId => 990;
        protected override string Title => "Campagne — Scènes & Choix";
        protected override Vector2 MinSize => _minSize;
        protected override Rect DefaultRect => new Rect(
            Mathf.Max(10f, Screen.width - 640f - 20f),
            96f,
            Mathf.Min(620f, Mathf.Max(_minSize.x, Screen.width - 40f)),
            Mathf.Min(660f, Mathf.Max(_minSize.y, Screen.height - 110f)));
        protected override KeyCode[] ToggleKeys => _toggleKeys;

        private static readonly KeyCode[] _toggleKeys = { KeyCode.F7 };
        private static readonly Vector2 _minSize = new Vector2(520f, 480f);
        private Vector2 _scroll;
        private bool _showJournal;

        protected override void OnOpened()
        {
            ScenarioDirector.EnsureInstance();
            StorySceneManager.EnsureInstance();
        }

        protected override void DrawContent()
        {
            var director = ScenarioDirector.EnsureInstance();
            var scenario = director.ActiveScenario;

            GUILayout.Space(6);
            DrawHeader(director, scenario);
            GUILayout.Space(6);

            _scroll = GUILayout.BeginScrollView(_scroll);
            if (scenario == null)
            {
                DrawSceneLauncher(director);
            }
            else
            {
                DrawActiveScene(director, scenario);
            }
            GUILayout.EndScrollView();
        }

        private static void DrawHeader(ScenarioDirector director, ScenarioDefinition scenario)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("<b>🎬 Killtime Tactics — Directeur de scène</b>");
            GUILayout.Label(scenario == null
                ? "Aucune scène active. Lancez une scène depuis le catalogue."
                : $"<color=cyan>{scenario.Volume}</color>  •  {scenario.CanonReference}");
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Route: <b>{RouteLabel(director.State.Route)}</b>", GUILayout.Width(150));
            GUILayout.Label($"Compteurs: <b>{CountersSummary(director)}</b>");
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("🛠️ Éditeur (F8)", GUILayout.Width(110)))
            {
                ScenarioEditorDevWindow.Open();
            }
            if (GUILayout.Button("↺ Réinitialiser", GUILayout.Width(105)))
            {
                director.ResetCampaign();
                StorySceneManager.EnsureInstance().CleanupCurrentScene();
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private void DrawSceneLauncher(ScenarioDirector director)
        {
            GUILayout.Label("<b>Scènes disponibles</b>");
            foreach (var definition in ScenarioCatalog.All)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label($"<b>{definition.Title}</b>");
                GUILayout.Label($"{definition.Volume} • {definition.CanonReference}");
                if (GUILayout.Button("▶ Lancer cette scène", GUILayout.Height(32)))
                {
                    StorySceneManager.EnsureInstance().StartScenario(definition.Id);
                    _scroll = Vector2.zero;
                }
                GUILayout.EndVertical();
            }

            DrawJournal(director);
        }

        private void DrawActiveScene(ScenarioDirector director, ScenarioDefinition scenario)
        {
            var sceneMgr = StorySceneManager.EnsureInstance();
            if (sceneMgr.CurrentSceneController == null && !sceneMgr.IsSceneLoading)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                GUI.backgroundColor = new Color(0.2f, 0.75f, 1f);
                if (GUILayout.Button("🚀 Déployer la Scène 3D & l'Environnement", GUILayout.Height(36)))
                {
                    sceneMgr.StartScenario(scenario.Id);
                }
                GUI.backgroundColor = Color.white;
                GUILayout.Label("<color=yellow>Campagne active en mémoire mais environnement 3D non instancié.</color>");
                GUILayout.EndVertical();
                GUILayout.Space(6);
            }

            var node = director.CurrentNode;
            if (node == null)
            {
                GUILayout.Label("<color=red>Le nœud de scène actif est introuvable.</color>");
                if (GUILayout.Button("↺ Recharger la scène")) sceneMgr.StartScenario(scenario.Id);
                return;
            }

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label($"<b>{scenario.Title}</b>");
            GUILayout.Label($"<color=orange>{node.Location}</color>");
            GUILayout.Space(6);
            GUILayout.Label($"<b>{node.Title}</b>");
            GUILayout.Label(node.Body, GUILayout.ExpandHeight(true));
            GUILayout.EndVertical();

            if (node.Kind == ScenarioNodeKind.Objective) DrawObjectives(director, node);
            if (node.Kind == ScenarioNodeKind.Choice) DrawChoices(director, node);

            if (node.Kind == ScenarioNodeKind.Briefing || node.Kind == ScenarioNodeKind.Objective)
            {
                bool ready = node.Kind != ScenarioNodeKind.Objective || director.AreRequiredObjectivesComplete();
                GUI.enabled = ready;
                if (GUILayout.Button(node.ContinueLabel, GUILayout.Height(34)))
                {
                    director.Continue();
                    _scroll = Vector2.zero;
                }
                GUI.enabled = true;
                if (!ready) GUILayout.Label("<color=yellow>Terminez les objectifs obligatoires pour continuer.</color>");
            }
            else if (node.Kind == ScenarioNodeKind.Resolution)
            {
                GUI.backgroundColor = new Color(0.22f, 0.72f, 0.42f);
                if (GUILayout.Button(node.ContinueLabel, GUILayout.Height(36))) director.CompleteActiveScenario();
                GUI.backgroundColor = Color.white;
                if (director.State.ActiveScenarioCompleted)
                {
                    GUILayout.Label("<color=green>Scène terminée. La scène suivante pourra lire la route et les drapeaux de campagne.</color>");
                }
            }

            GUILayout.Space(8);
            DrawState(director);
            DrawJournal(director);
            GUILayout.Space(16);
        }

        private static void DrawObjectives(ScenarioDirector director, ScenarioNode node)
        {
            GUILayout.Space(6);
            GUILayout.Label("<b>Objectifs de mission</b>");
            foreach (var objective in node.Objectives)
            {
                bool completed = director.IsObjectiveComplete(objective.Id);
                bool nextValue = GUILayout.Toggle(completed,
                    $"{(objective.Optional ? "Optionnel" : "Obligatoire")} — {objective.Label}\n<color=grey>{objective.Detail}</color>");
                if (nextValue != completed) director.SetObjectiveComplete(objective.Id, nextValue);
            }
        }

        private static void DrawChoices(ScenarioDirector director, ScenarioNode node)
        {
            GUILayout.Space(8);
            GUILayout.Label("<b>Choix majeur</b>");
            foreach (var choice in node.Choices)
            {
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label($"<b>{choice.Label}</b>");
                GUILayout.Label(choice.Consequence);
                if (GUILayout.Button("Choisir cette voie", GUILayout.Height(30))) director.Choose(choice.Id);
                GUILayout.EndVertical();
            }
        }

        private static void DrawState(ScenarioDirector director)
        {
            GUILayout.Space(8);
            GUILayout.Label("<b>État persistant</b>");
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label($"Route: {RouteLabel(director.State.Route)} | Drapeaux: {director.State.Flags.Count} | Objectifs: {director.State.CompletedObjectiveIds.Count}");
            if (director.State.Flags.Count > 0) GUILayout.Label(string.Join(" • ", director.State.Flags));
            GUILayout.EndVertical();
        }

        private void DrawJournal(ScenarioDirector director)
        {
            GUILayout.Space(8);
            _showJournal = GUILayout.Toggle(_showJournal, "Afficher le journal de campagne");
            if (!_showJournal) return;

            GUILayout.BeginVertical(GUI.skin.box);
            if (director.State.Journal.Count == 0) GUILayout.Label("Aucune entrée pour le moment.");
            for (int index = director.State.Journal.Count - 1; index >= 0; index--)
            {
                GUILayout.Label($"• {director.State.Journal[index]}");
            }
            GUILayout.EndVertical();
        }

        private static string RouteLabel(StoryRoute route)
        {
            return route switch
            {
                StoryRoute.Vardis => "Vardis",
                StoryRoute.Independent => "Indépendante",
                StoryRoute.Imperial => "Impériale",
                _ => "Non définie"
            };
        }

        private static string CountersSummary(ScenarioDirector director)
        {
            if (director == null || director.State == null || director.State.Integers.Count == 0) return "—";
            var parts = new System.Collections.Generic.List<string>();
            for (int i = 0; i < director.State.Integers.Count; i++)
            {
                var entry = director.State.Integers[i];
                if (entry != null) parts.Add($"{entry.Key}: {entry.Value}");
            }
            return parts.Count == 0 ? "—" : string.Join(" • ", parts);
        }
    }
}

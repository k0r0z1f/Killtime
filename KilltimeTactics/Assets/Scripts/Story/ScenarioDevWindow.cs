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
        protected override Vector2 MinSize => new(520f, 480f);
        protected override Rect DefaultRect => new(640f, 100f, 620f, 700f);
        protected override KeyCode[] ToggleKeys => _toggleKeys;

        private static readonly KeyCode[] _toggleKeys = { KeyCode.F7 };
        private Vector2 _scroll;
        private bool _showJournal;

        protected override void OnOpened()
        {
            ScenarioDirector.EnsureInstance();
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
            GUILayout.Label($"Route: <b>{RouteLabel(director.State.Route)}</b>", GUILayout.Width(190));
            GUILayout.Label($"Confiance John–Erika: <b>{director.State.GetInt("john_erika_trust")}</b>");
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("↺ Réinitialiser", GUILayout.Width(105))) director.ResetCampaign();
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
                    director.StartScenario(definition.Id);
                    _scroll = Vector2.zero;
                }
                GUILayout.EndVertical();
            }

            DrawJournal(director);
        }

        private void DrawActiveScene(ScenarioDirector director, ScenarioDefinition scenario)
        {
            var node = director.CurrentNode;
            if (node == null)
            {
                GUILayout.Label("<color=red>Le nœud de scène actif est introuvable.</color>");
                if (GUILayout.Button("↺ Recharger la scène 01")) director.StartScenario(scenario.Id);
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
                    GUILayout.Label("<color=green>Scène 01 terminée. La scène 02 pourra lire la route et les drapeaux de campagne.</color>");
                }
            }

            GUILayout.Space(8);
            DrawState(director);
            DrawJournal(director);
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
                StoryRoute.Vardis => "Vardis — voie du manuscrit",
                StoryRoute.Independent => "Fuite indépendante",
                StoryRoute.Imperial => "Évasion hostile",
                _ => "Non définie"
            };
        }
    }
}

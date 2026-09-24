using UnityEngine;
using Killtime.UI;

namespace Killtime.Story
{
    /// <summary>
    /// Overworld d'Hybris — réseau de secteurs (carte nodale sur treillis hexagonal).
    /// Fenêtre DevUI (F12) : déplace le groupe entre scènes tactiques fermées,
    /// applique coûts/distances/verrous et lance les scènes JSON.
    /// Fond stylisé d'après "Hybris 2035.png" + Livre X chap. 41-42.
    /// </summary>
    public class HybrisWorldMapDevWindow : FloatingWindow<HybrisWorldMapDevWindow>
    {
        protected override int WindowId => 992;
        protected override string Title => "Overworld Hybris — Réseau de Secteurs";
        protected override Vector2 MinSize => new Vector2(760f, 560f);
        protected override Rect DefaultRect => new Rect(
            Mathf.Max(10f, Screen.width - 980f),
            96f,
            Mathf.Min(960f, Mathf.Max(760f, Screen.width - 40f)),
            Mathf.Min(680f, Mathf.Max(560f, Screen.height - 110f)));
        protected override KeyCode[] ToggleKeys => _toggleKeys;

        private static readonly KeyCode[] _toggleKeys = { KeyCode.F12 };

        private string _selectedNodeId;
        private bool _godMode;
        private bool _showHelp;
        private Vector2 _rightScroll;

        protected override void OnOpened()
        {
            var director = ScenarioDirector.EnsureInstance();
            if (director.State == null) director.ResetCampaign();
            director.State.EnsureWorldDefaults();
            if (string.IsNullOrWhiteSpace(_selectedNodeId))
                _selectedNodeId = director.State.PartyNodeId;
            if (HybrisWorldMapData.Find(_selectedNodeId) == null)
                _selectedNodeId = director.State.PartyNodeId;
        }

        protected override void DrawContent()
        {
            var director = ScenarioDirector.EnsureInstance();
            if (director.State == null) director.ResetCampaign();
            director.State.EnsureWorldDefaults();
            var state = director.State;

            if (HybrisWorldMapData.Find(_selectedNodeId) == null)
                _selectedNodeId = state.PartyNodeId;

            DrawTopBar(director);

            GUILayout.BeginHorizontal();
            DrawMapPanel(director);
            DrawInspectorPanel(director);
            GUILayout.EndHorizontal();

            if (_showHelp) DrawHelpBox();
        }

        // ================= BARRE SUPÉRIEURE =================

        private void DrawTopBar(ScenarioDirector director)
        {
            var state = director.State;
            var party = HybrisWorldMapData.Find(state.PartyNodeId);

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>◎ Groupe : {party?.Name ?? state.PartyNodeId}</b>", GUILayout.Width(220));
            GUILayout.Label($"Route: <b>{state.Route}</b>", GUILayout.Width(150));
            GUILayout.Label($"Jours de voyage: <b>{state.GetInt("jours_voyage")}</b>", GUILayout.Width(150));
            GUILayout.Label(HybrisWorldMapData.IsCustomized
                ? $"<color=cyan>🗺️ {HybrisWorldMapData.ActiveMapName} (rev {HybrisWorldMapData.ActiveRevision})</color>"
                : "<color=grey>🗺️ canon</color>");
            GUILayout.FlexibleSpace();
            bool newGod = GUILayout.Toggle(_godMode, "🛠️ God dev (ignore verrous)", GUILayout.Width(200));
            if (newGod != _godMode) _godMode = newGod;
            _showHelp = GUILayout.Toggle(_showHelp, "❓ Aide", GUILayout.Width(70));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("🔓 Tout déverrouiller", GUILayout.Height(26)))
                director.UnlockAllSectors();
            if (GUILayout.Button("⛵ Autoriser expédition Est", GUILayout.Height(26)))
                director.AllowEasternExpedition();
            if (GUILayout.Button("🛠️ Éditeur", GUILayout.Width(100), GUILayout.Height(26)))
                HybrisWorldMapEditorWindow.Open();
            if (GUILayout.Button("📜 Scènes (F7)", GUILayout.Width(110), GUILayout.Height(26)))
                ScenarioDevWindow.Open();
            if (GUILayout.Button("↺ Reset carte", GUILayout.Width(110), GUILayout.Height(26)))
            {
                director.ResetWorldMap();
                _selectedNodeId = director.State.PartyNodeId;
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        // ================= CARTE =================

        private void DrawMapPanel(ScenarioDirector director)
        {
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            Rect mapRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
                GUILayout.ExpandWidth(true), GUILayout.Height(430f));
            if (mapRect.width > 10f && mapRect.height > 10f)
                DrawMap(mapRect, director);
            GUILayout.Label("<i>Fond stylisé d'après Hybris 2035.png (2 continents + Vagas + Aurora) • Treillis hexagonal • ◎ = groupe</i>");
            GUILayout.EndVertical();
        }

        private void DrawMap(Rect mapRect, ScenarioDirector director)
        {
            var state = director.State;

            HybrisWorldMapDraw.DrawBackdrop(mapRect);

            // Liens.
            foreach (var link in HybrisWorldMapData.ActiveLinks)
            {
                var a = HybrisWorldMapData.Find(link.FromId);
                var b = HybrisWorldMapData.Find(link.ToId);
                if (a == null || b == null) continue;
                Vector2 pa = HybrisWorldMapDraw.NodeScreenPos(mapRect, a);
                Vector2 pb = HybrisWorldMapDraw.NodeScreenPos(mapRect, b);

                bool locked = IsLinkLocked(state, link) || IsNodeLocked(state, a) || IsNodeLocked(state, b);
                bool active = link.Connects(state.PartyNodeId);
                Color c = locked ? new Color(0.9f, 0.25f, 0.25f, 0.55f)
                    : link.IsSeaCrossing ? new Color(0.35f, 0.75f, 1f, active ? 0.95f : 0.6f)
                    : new Color(0.55f, 0.9f, 0.6f, active ? 0.95f : 0.55f);
                HybrisWorldMapDraw.DrawLine(pa, pb, c, active ? 3f : 2f);

                Vector2 mid = (pa + pb) * 0.5f;
                GUI.Label(new Rect(mid.x - 30, mid.y - 20, 60, 16),
                    $"<color=#cbd5e1><size=10>{link.Miles} mi</size></color>");
            }

            // Nœuds.
            foreach (var node in HybrisWorldMapData.ActiveNodes)
            {
                if (node == null) continue;
                Vector2 p = HybrisWorldMapDraw.NodeScreenPos(mapRect, node);
                bool isParty = state.PartyNodeId == node.Id;
                bool isSelected = _selectedNodeId == node.Id;
                bool locked = IsNodeLocked(state, node);

                if (isParty)
                    GUI.Label(new Rect(p.x - 20, p.y - 34, 40, 18), "<color=yellow><b><size=14>◎</size></b></color>");

                string label = (locked ? "🔒 " : "") + node.Name;
                Color prevBg = GUI.backgroundColor;
                GUI.backgroundColor = isParty ? new Color(0.9f, 0.75f, 0.2f)
                    : isSelected ? new Color(0.25f, 0.7f, 1f)
                    : locked ? new Color(0.35f, 0.2f, 0.2f)
                    : state.IsSectorVisited(node.Id) ? new Color(0.3f, 0.65f, 0.4f)
                    : new Color(0.5f, 0.55f, 0.6f);
                Rect btnRect = new Rect(p.x - 66, p.y - 12, 132, 24);
                if (GUI.Button(btnRect, label)) _selectedNodeId = node.Id;
                GUI.backgroundColor = prevBg;
            }
        }

        // ================= INSPECTEUR =================

        private void DrawInspectorPanel(ScenarioDirector director)
        {
            var state = director.State;
            var node = HybrisWorldMapData.Find(_selectedNodeId);
            var party = HybrisWorldMapData.Find(state.PartyNodeId);

            GUILayout.BeginVertical(GUILayout.Width(320));
            _rightScroll = GUILayout.BeginScrollView(_rightScroll, GUILayout.Height(430f));

            if (node == null)
            {
                GUILayout.Label("Sélectionnez un secteur sur la carte.");
                GUILayout.EndScrollView();
                GUILayout.EndVertical();
                return;
            }

            bool locked = IsNodeLocked(state, node);
            bool isParty = state.PartyNodeId == node.Id;

            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label($"<b>{(locked ? "🔒 " : "")}{node.Name}</b>");
            GUILayout.Label($"<color=orange>{node.Region}</color>");
            GUILayout.Label($"{HybrisWorldMapData.BiomeLabel(node.Biome)} • {HybrisWorldMapData.DangerLabel(node.Danger)}");
            GUILayout.Label(node.Description);
            GUILayout.Label(isParty ? "<color=yellow>◎ Position actuelle du groupe</color>"
                : state.IsSectorVisited(node.Id) ? "<color=green>✓ Déjà visité</color>"
                : locked ? "<color=red>Verrouillé — voir condition</color>"
                : "<color=grey>Non visité</color>");
            if (!string.IsNullOrWhiteSpace(node.RequiredFlag))
                GUILayout.Label($"Condition : flag <b>{node.RequiredFlag}</b> {(state.HasFlag(node.RequiredFlag) ? "<color=green>(rempli)</color>" : "<color=red>(manquant)</color>")}");
            if (!string.IsNullOrWhiteSpace(node.LinkedScenarioId))
                GUILayout.Label($"Scène tactique : <b>{node.LinkedScenarioId}</b>");
            GUILayout.EndVertical();

            // Voyage.
            GUILayout.Space(4);
            GUILayout.Label("<b>Voyage</b>");
            if (isParty)
            {
                GUILayout.Label("<color=grey>Le groupe est déjà ici.</color>");
            }
            else
            {
                var link = HybrisWorldMapData.FindLink(state.PartyNodeId, node.Id);
                if (link == null)
                {
                    GUILayout.Label($"<color=grey>Pas de lien direct depuis {party?.Name}. Voyagez de proche en proche.</color>");
                    if (_godMode && GUILayout.Button("🛠️ Téléporter (dev)", GUILayout.Height(30)))
                        director.TeleportPartyTo(node.Id);
                }
                else
                {
                    GUILayout.Label($"{link.Label} — <b>{link.Miles} miles, {link.Days} j</b>{(link.IsSeaCrossing ? " ⛵ maritime" : "")}");
                    string blockReason = TravelBlockReason(state, link, node);
                    if (blockReason != null && !_godMode)
                    {
                        GUILayout.Label($"<color=red>Verrouillé : {blockReason}</color>");
                        if (GUILayout.Button("🛠️ Forcer quand même (dev)", GUILayout.Height(28)))
                        {
                            if (director.TravelToSector(node.Id, true)) _selectedNodeId = node.Id;
                        }
                    }
                    else if (GUILayout.Button(_godMode ? $"🛠️ Voyager (god) ➔ {node.Name}" : $"➔ Voyager vers {node.Name}", GUILayout.Height(32)))
                    {
                        if (director.TravelToSector(node.Id, _godMode)) _selectedNodeId = node.Id;
                    }
                }
            }

            // Voisins.
            GUILayout.Space(4);
            GUILayout.Label("<b>Liaisons</b>");
            foreach (var link in HybrisWorldMapData.GetLinksFor(node.Id))
            {
                string otherId = link.OtherEnd(node.Id);
                var other = HybrisWorldMapData.Find(otherId);
                if (other == null) continue;
                bool here = state.PartyNodeId == otherId;
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{(here ? "◎ " : "")}{other.Name} <color=grey>({link.Miles} mi • {link.Days} j)</color>");
                if (!here && GUILayout.Button("➔", GUILayout.Width(32)))
                    _selectedNodeId = otherId;
                GUILayout.EndHorizontal();
            }

            // Scène tactique.
            GUILayout.Space(4);
            GUILayout.Label("<b>Scène tactique fermée</b>");
            if (string.IsNullOrWhiteSpace(node.LinkedScenarioId))
            {
                GUILayout.Label("<color=grey>Aucune scène liée — à créer.</color>");
                if (GUILayout.Button("🛠️ Éditeur (F8)", GUILayout.Height(28)))
                    ScenarioEditorDevWindow.Open();
            }
            else
            {
                var def = ScenarioCatalog.Find(node.LinkedScenarioId);
                if (def != null)
                {
                    GUILayout.Label($"<color=cyan>{def.Title}</color>");
                    GUI.backgroundColor = new Color(0.2f, 0.75f, 1f);
                    if (GUILayout.Button("🚀 Déployer la scène 3D", GUILayout.Height(32)))
                        StorySceneManager.EnsureInstance().StartScenario(def.Id);
                    GUI.backgroundColor = Color.white;
                }
                else
                {
                    GUILayout.Label($"<color=yellow>Id réservé : {node.LinkedScenarioId} (JSON absent dans Scenarios/).</color>");
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("🎬 Scènes (F7)", GUILayout.Height(28)))
                        ScenarioDevWindow.Open();
                    if (GUILayout.Button("🛠️ Éditeur (F8)", GUILayout.Height(28)))
                        ScenarioEditorDevWindow.Open();
                    GUILayout.EndHorizontal();
                }
            }

            // Journal.
            GUILayout.Space(4);
            GUILayout.Label("<b>Journal (récent)</b>");
            GUILayout.BeginVertical(GUI.skin.box);
            if (state.Journal.Count == 0) GUILayout.Label("<color=grey>Aucune entrée.</color>");
            for (int i = state.Journal.Count - 1; i >= Mathf.Max(0, state.Journal.Count - 4); i--)
                GUILayout.Label($"• {state.Journal[i]}");
            GUILayout.EndVertical();

            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private void DrawHelpBox()
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("<b>❓ Carte nodale + treillis hexagonal — mode d'emploi dev</b>");
            GUILayout.Label("• Chaque pastille = un secteur (scène tactique fermée potentielle). Les traits = routes/car traversées maritimes avec coûts en miles et jours (échelle de la vieille carte : 0-300 miles).");
            GUILayout.Label("• Le voyage exige un lien direct. L'Est (Rivage, Profondeurs) exige le flag « expedition_est » — bouton « Autoriser expédition Est » ou God dev.");
            GUILayout.Label("• Chaque déplacement écrit dans le journal de campagne et cumule « jours_voyage » ; la première visite pose un flag (visited_*) et dévoile les voisins.");
            GUILayout.Label("• Sources : Killtime/Killtime/img/Hybris 2035.png (légende villages a.-ah., lacs A-H, royaumes 1-8) + Livre X §41-42 (Kingston, Temple de Brum'korath, Forêts Primordiales, Haliriel, Guetteur).");
            GUILayout.EndVertical();
        }

        // ================= RÈGLES =================

        private static bool IsNodeLocked(CampaignState state, HybrisSectorNode node)
        {
            if (node == null || state == null) return true;
            return !string.IsNullOrWhiteSpace(node.RequiredFlag) && !state.HasFlag(node.RequiredFlag);
        }

        private static bool IsLinkLocked(CampaignState state, HybrisSectorLink link)
        {
            if (link == null || state == null) return true;
            return !string.IsNullOrWhiteSpace(link.RequiresFlag) && !state.HasFlag(link.RequiresFlag);
        }

        private static string TravelBlockReason(CampaignState state, HybrisSectorLink link, HybrisSectorNode dest)
        {
            if (!string.IsNullOrWhiteSpace(link.RequiresFlag) && !state.HasFlag(link.RequiresFlag))
                return $"lien « {link.Label} » (flag {link.RequiresFlag})";
            if (!string.IsNullOrWhiteSpace(dest.RequiredFlag) && !state.HasFlag(dest.RequiredFlag))
                return $"secteur {dest.Name} (flag {dest.RequiredFlag})";
            return null;
        }

        // ================= DESSIN =================
        // Primitives dans HybrisWorldMapDraw (partagées avec l'éditeur).
    }
}

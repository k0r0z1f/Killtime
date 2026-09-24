using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.UI;
using Killtime.Multi;

namespace Killtime.Story
{
    /// <summary>
    /// Éditeur complet de la carte monde (overworld d'Hybris).
    /// Sans raccourci clavier : s'ouvre depuis le visualiseur (F12) ou la Dev Arena.
    /// - Carte : sélection, déplacement drag &amp; drop, création de liens (clic-clic),
    ///   création de secteurs (clic), fond partagé avec le visualiseur.
    /// - Secteurs / Liaisons : formulaires complets, renommage avec recâblage.
    /// - Fichier : WorldMaps/*.json (sauver/charger/import/export/canon/appliquer).
    /// - Test &amp; VTT : validation, simulation de voyage, op worldmap copiable.
    /// Tout est IMGUI runtime (aucune dépendance UnityEditor).
    /// </summary>
    public class HybrisWorldMapEditorWindow : FloatingWindow<HybrisWorldMapEditorWindow>
    {
        protected override int WindowId => 993;
        protected override string Title => "Éditeur World Map — Overworld d'Hybris";
        protected override Vector2 MinSize => new Vector2(980f, 640f);
        protected override Rect DefaultRect => new Rect(
            20f, 40f,
            Mathf.Min(1280f, Mathf.Max(980f, Screen.width - 40f)),
            Mathf.Min(840f, Mathf.Max(640f, Screen.height - 60f)));

        private readonly string[] _tabs = { "🗺️ Carte", "📋 Secteurs", "🔗 Liaisons", "💾 Fichier", "🧪 Test & VTT" };
        private int _tab;

        // ---- Document de travail ----
        private HybrisWorldMapSaveData _doc;
        private bool _dirty;
        private string _mapName = "hybris_overworld";
        private string _statusMsg = "";
        private Color _statusColor = Color.grey;

        // ---- Sélection ----
        private string _selectedNodeId;
        private int _selectedLinkIndex = -1;
        private string _idEditBuffer = "";
        private string _confirmDelete = null;

        // ---- Carte interactive ----
        // 0 Sélection, 1 Déplacer, 2 Lier, 3 Ajouter.
        private int _mapMode;
        private readonly string[] _mapModes = { "👆 Sélection", "✋ Déplacer", "🔗 Lier (clic-clic)", "➕ Ajouter (clic)" };
        private string _linkFrom;
        private string _dragNodeId;
        private const float MapHeight = 430f;

        // ---- Formulaires ----
        private string _newNodeName = "Nouveau secteur";
        private string _scenarioFilter = "";
        private Vector2 _scrollList;
        private Vector2 _scrollForm;
        private Vector2 _scrollScenarios;

        // ---- Fichier ----
        private string _importBuffer = "";
        private bool _showRawJson;
        private Vector2 _scrollRaw;

        // ---- Test & VTT ----
        private List<WorldMapIssue> _issues;
        private bool _issuesStale = true;
        private Vector2 _scrollIssues;
        private string _vttPreview = "";
        private List<KeyValuePair<string, string>> _scenarioCache;

        // ================= CYCLE =================

        protected override void OnOpened()
        {
            if (_doc == null) LoadDocFromActive();
            RefreshScenarioCache();
            _issuesStale = true;
        }

        private void LoadDocFromActive()
        {
            _doc = new HybrisWorldMapSaveData { MapName = HybrisWorldMapData.ActiveMapName };
            foreach (var n in HybrisWorldMapData.ActiveNodes)
            {
                if (n == null) continue;
                _doc.Nodes.Add(new HybrisSectorNode(
                    n.Id, n.Name, n.Region, n.Description,
                    n.MapPos.x, n.MapPos.y, n.Biome, n.Danger,
                    n.LinkedScenarioId, n.RequiredFlag, n.GrantsFlagOnVisit, n.StartingUnlocked));
            }
            foreach (var l in HybrisWorldMapData.ActiveLinks)
            {
                if (l == null) continue;
                _doc.Links.Add(new HybrisSectorLink(
                    l.FromId, l.ToId, l.Miles, l.Days, l.Label, l.IsSeaCrossing, l.RequiresFlag));
            }
            _mapName = _doc.MapName;
            _dirty = false;
            _selectedNodeId = null;
            _selectedLinkIndex = -1;
            _linkFrom = null;
            _dragNodeId = null;
            _confirmDelete = null;
            _issuesStale = true;
            if (_doc.Nodes.Count > 0 && FindInDoc(ScenarioDirector.EnsureInstance().State.PartyNodeId) != null)
                _selectedNodeId = ScenarioDirector.EnsureInstance().State.PartyNodeId;
            else if (_doc.Nodes.Count > 0)
                _selectedNodeId = _doc.Nodes[0].Id;
            SyncIdBuffer();
        }

        private void RefreshScenarioCache()
        {
            _scenarioCache = new List<KeyValuePair<string, string>>();
            try
            {
                foreach (var def in ScenarioCatalog.All)
                {
                    if (def == null || string.IsNullOrWhiteSpace(def.Id)) continue;
                    _scenarioCache.Add(new KeyValuePair<string, string>(def.Id, def.Title ?? def.Id));
                }
                _scenarioCache.Sort((a, b) => string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase));
            }
            catch { /* catalogue optionnel */ }
        }

        protected override void DrawContent()
        {
            if (_doc == null) LoadDocFromActive();
            _doc.EnsureDefaults();

            DrawDocHeader();

            int newTab = GUILayout.Toolbar(_tab, _tabs);
            if (newTab != _tab)
            {
                _tab = newTab;
                _confirmDelete = null;
                if (_tab == 4) { _issuesStale = true; RefreshScenarioCache(); }
            }
            GUILayout.Space(4);

            switch (_tab)
            {
                case 0: DrawMapTab(); break;
                case 1: DrawSectorsTab(); break;
                case 2: DrawLinksTab(); break;
                case 3: DrawFileTab(); break;
                case 4: DrawTestTab(); break;
            }
        }

        private void DrawDocHeader()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>🗺️ {_mapName}</b>  •  {_doc.Nodes.Count} secteur(s)  •  {_doc.Links.Count} liaison(s)", GUILayout.Width(380));
            GUILayout.Label(_dirty
                ? "<color=orange>● modifications non sauvegardées</color>"
                : "<color=green>✓ synchronisé au fichier</color>");
            GUILayout.FlexibleSpace();
            GUILayout.Label(HybrisWorldMapData.IsCustomized
                ? $"<color=cyan>jeu : {HybrisWorldMapData.ActiveMapName} (rev {HybrisWorldMapData.ActiveRevision})</color>"
                : "<color=grey>jeu : canon</color>");
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(_statusMsg))
            {
                Color prev = GUI.color;
                GUI.color = _statusColor;
                GUILayout.Label(_statusMsg);
                GUI.color = prev;
            }
        }

        private void SetStatus(string msg, Color c)
        {
            _statusMsg = msg;
            _statusColor = c;
        }

        private void MarkDirty()
        {
            _dirty = true;
            _issuesStale = true;
        }

        // ================= ONGLET CARTE =================

        private void DrawMapTab()
        {
            GUILayout.BeginHorizontal();
            int newMode = GUILayout.Toolbar(_mapMode, _mapModes);
            if (newMode != _mapMode)
            {
                _mapMode = newMode;
                _linkFrom = null;
                _dragNodeId = null;
            }
            GUILayout.EndHorizontal();
            GUILayout.Label(_mapMode switch
            {
                1 => "<i>Glissez une pastille pour la repositionner (position normalisée 0-1).</i>",
                2 => "<i>Cliquez un premier secteur puis un second pour créer la liaison. Re-cliquez le premier pour annuler.</i>",
                3 => "<i>Cliquez un endroit vide pour créer un secteur.</i>",
                _ => "<i>Cliquez une pastille pour la sélectionner (édition dans les onglets Secteurs/Liaisons).</i>",
            });

            Rect mapRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
                GUILayout.ExpandWidth(true), GUILayout.Height(MapHeight));
            if (mapRect.width > 10f && mapRect.height > 10f)
            {
                HandleMapEvents(mapRect);
                DrawEditorMap(mapRect);
            }

            // Liaisons du secteur sélectionné (accès rapide).
            var sel = FindInDoc(_selectedNodeId);
            if (sel != null)
            {
                GUILayout.Space(2);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"<b>{sel.Name}</b> — liaisons :", GUILayout.Width(260));
                bool any = false;
                for (int i = 0; i < _doc.Links.Count; i++)
                {
                    var l = _doc.Links[i];
                    if (l == null || !l.Connects(sel.Id)) continue;
                    any = true;
                    string other = l.OtherEnd(sel.Id);
                    if (GUILayout.Button($"{other} ({l.Miles} mi)", GUILayout.Width(170)))
                    {
                        _selectedLinkIndex = i;
                        _tab = 2;
                    }
                }
                if (!any) GUILayout.Label("<color=grey>aucune — mode Lier ou onglet Liaisons.</color>");
                GUILayout.EndHorizontal();
            }
        }

        private string HitNode(Rect mapRect, Vector2 mouse)
        {
            string best = null;
            float bestD = 20f;
            foreach (var n in _doc.Nodes)
            {
                if (n == null) continue;
                Vector2 p = HybrisWorldMapDraw.NodeScreenPos(mapRect, n);
                float d = Vector2.Distance(p, mouse);
                if (d < bestD) { bestD = d; best = n.Id; }
            }
            return best;
        }

        private void HandleMapEvents(Rect mapRect)
        {
            var e = Event.current;
            if (e == null) return;
            Vector2 mp = e.mousePosition;
            bool inside = mapRect.Contains(mp);

            if (e.type == EventType.MouseUp)
            {
                if (_dragNodeId != null) { _dragNodeId = null; e.Use(); }
                return;
            }
            if (_dragNodeId != null && e.type == EventType.MouseDrag && inside)
            {
                var n = FindInDoc(_dragNodeId);
                if (n != null)
                {
                    n.MapPos = new Vector2(
                        Mathf.Clamp01((mp.x - mapRect.x) / mapRect.width),
                        Mathf.Clamp01((mp.y - mapRect.y) / mapRect.height));
                    MarkDirty();
                }
                e.Use();
                return;
            }
            if (!inside || e.type != EventType.MouseDown || e.button != 0) return;

            string hit = HitNode(mapRect, mp);
            if (_mapMode == 1)
            {
                if (hit != null)
                {
                    _dragNodeId = hit;
                    _selectedNodeId = hit;
                    SyncIdBuffer();
                    e.Use();
                }
            }
            else if (_mapMode == 3)
            {
                if (hit != null)
                {
                    _selectedNodeId = hit;
                    SyncIdBuffer();
                }
                else
                {
                    CreateNodeAt(new Vector2(
                        Mathf.Clamp01((mp.x - mapRect.x) / mapRect.width),
                        Mathf.Clamp01((mp.y - mapRect.y) / mapRect.height)));
                    e.Use();
                }
            }
            else if (_mapMode == 2)
            {
                if (hit == null) { _linkFrom = null; }
                else if (_linkFrom == null) { _linkFrom = hit; _selectedNodeId = hit; SyncIdBuffer(); }
                else if (_linkFrom == hit) { _linkFrom = null; }
                else
                {
                    CreateLink(_linkFrom, hit);
                    _linkFrom = null;
                }
                e.Use();
            }
            // Mode 0 : les boutons pastilles gèrent la sélection.
        }

        private void DrawEditorMap(Rect mapRect)
        {
            HybrisWorldMapDraw.DrawBackdrop(mapRect);

            var state = ScenarioDirector.EnsureInstance().State;
            var errors = ErrorNodeIds();

            foreach (var l in _doc.Links)
            {
                if (l == null) continue;
                var a = FindInDoc(l.FromId);
                var b = FindInDoc(l.ToId);
                if (a == null || b == null) continue;
                Vector2 pa = HybrisWorldMapDraw.NodeScreenPos(mapRect, a);
                Vector2 pb = HybrisWorldMapDraw.NodeScreenPos(mapRect, b);
                bool sel = _doc.Links.IndexOf(l) == _selectedLinkIndex;
                Color c = sel ? new Color(1f, 0.85f, 0.25f, 0.95f)
                    : l.IsSeaCrossing ? new Color(0.35f, 0.75f, 1f, 0.6f)
                    : new Color(0.6f, 0.65f, 0.7f, 0.5f);
                HybrisWorldMapDraw.DrawLine(pa, pb, c, sel ? 3.5f : 2f);
            }

            foreach (var n in _doc.Nodes)
            {
                if (n == null) continue;
                Vector2 p = HybrisWorldMapDraw.NodeScreenPos(mapRect, n);
                bool isSel = _selectedNodeId == n.Id;
                bool isFrom = _linkFrom == n.Id;
                bool isParty = state != null && state.PartyNodeId == n.Id;

                if (isParty)
                    GUI.Label(new Rect(p.x - 20, p.y - 34, 40, 18), "<color=yellow><b><size=14>◎</size></b></color>");

                Color prevBg = GUI.backgroundColor;
                GUI.backgroundColor = isFrom ? new Color(1f, 0.6f, 0.15f)
                    : isSel ? new Color(0.25f, 0.7f, 1f)
                    : errors.Contains(n.Id) ? new Color(0.75f, 0.25f, 0.25f)
                    : new Color(0.5f, 0.55f, 0.6f);
                string label = (!string.IsNullOrWhiteSpace(n.RequiredFlag) ? "🔒 " : "") + (string.IsNullOrEmpty(n.Name) ? n.Id : n.Name);
                if (GUI.Button(new Rect(p.x - 66, p.y - 12, 132, 24), label))
                {
                    _selectedNodeId = n.Id;
                    SyncIdBuffer();
                }
                GUI.backgroundColor = prevBg;
            }

            if (_linkFrom != null)
                GUILayout.Label($"<color=orange>Liaison en cours depuis <b>{_linkFrom}</b> — cliquez le second secteur.</color>");
        }

        private HashSet<string> ErrorNodeIds()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var i in GetIssues())
                if (i != null && i.Severity == WorldMapIssueSeverity.Error && !string.IsNullOrEmpty(i.NodeId))
                    set.Add(i.NodeId);
            return set;
        }

        // ================= ONGLET SECTEURS =================

        private void DrawSectorsTab()
        {
            GUILayout.BeginHorizontal();

            // Liste.
            GUILayout.BeginVertical(GUILayout.Width(300));
            if (GUILayout.Button("➕ Nouveau secteur", GUILayout.Height(30)))
                CreateNodeAt(new Vector2(0.5f, 0.5f));
            _scrollList = GUILayout.BeginScrollView(_scrollList, GUILayout.ExpandHeight(true));
            foreach (var n in _doc.Nodes)
            {
                if (n == null) continue;
                bool sel = _selectedNodeId == n.Id;
                GUI.backgroundColor = sel ? new Color(0.25f, 0.7f, 1f) : Color.white;
                string row = $"{(n.StartingUnlocked ? "★ " : "")}{n.Name} <color=grey>({n.Id})</color>";
                if (GUILayout.Button(row, GUILayout.Height(26)))
                {
                    _selectedNodeId = n.Id;
                    SyncIdBuffer();
                    _confirmDelete = null;
                }
                GUI.backgroundColor = Color.white;
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            // Formulaire.
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            _scrollForm = GUILayout.BeginScrollView(_scrollForm, GUILayout.ExpandHeight(true));
            var node = FindInDoc(_selectedNodeId);
            if (node == null)
            {
                GUILayout.Label("<color=grey>Sélectionnez un secteur dans la liste ou sur la carte.</color>");
            }
            else
            {
                DrawNodeForm(node);
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }

        private void DrawNodeForm(HybrisSectorNode node)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("<b>Identité</b>");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Id :", GUILayout.Width(90));
            string newId = GUILayout.TextField(_idEditBuffer);
            if (newId != _idEditBuffer) _idEditBuffer = newId.Trim();
            if (GUILayout.Button("Renommer", GUILayout.Width(90)))
                RenameNode(node, _idEditBuffer);
            GUILayout.EndHorizontal();

            LabeledField("Nom :", node.Name, v => { node.Name = v; MarkDirty(); }, 90);
            LabeledField("Région :", node.Region, v => { node.Region = v; MarkDirty(); }, 90);
            GUILayout.Label("Description :");
            string newDesc = GUILayout.TextArea(node.Description ?? "", GUILayout.Height(64));
            if (newDesc != node.Description) { node.Description = newDesc; MarkDirty(); }
            GUILayout.EndVertical();

            GUILayout.Space(4);
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("<b>Géographie & danger</b>");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Biome :", GUILayout.Width(90));
            if (GUILayout.Button("◀", GUILayout.Width(32)))
            {
                node.Biome = (HybrisBiome)(((int)node.Biome + Enum.GetNames(typeof(HybrisBiome)).Length - 1) % Enum.GetNames(typeof(HybrisBiome)).Length);
                MarkDirty();
            }
            GUILayout.Label($"<b>{HybrisWorldMapData.BiomeLabel(node.Biome)}</b>", GUILayout.Width(170));
            if (GUILayout.Button("▶", GUILayout.Width(32)))
            {
                node.Biome = (HybrisBiome)(((int)node.Biome + 1) % Enum.GetNames(typeof(HybrisBiome)).Length);
                MarkDirty();
            }
            GUILayout.EndHorizontal();

            int newDanger = (int)GUILayout.HorizontalSlider(node.Danger, 0, 5);
            if (newDanger != node.Danger) { node.Danger = newDanger; MarkDirty(); }
            GUILayout.Label($"Danger : <b>{HybrisWorldMapData.DangerLabel(node.Danger)}</b>");

            float nx = FloatSlider("Position X :", node.MapPos.x);
            float ny = FloatSlider("Position Y :", node.MapPos.y);
            if (Math.Abs(nx - node.MapPos.x) > 0.0001f || Math.Abs(ny - node.MapPos.y) > 0.0001f)
            {
                node.MapPos = new Vector2(Mathf.Clamp01(nx), Mathf.Clamp01(ny));
                MarkDirty();
            }
            GUILayout.EndVertical();

            GUILayout.Space(4);
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("<b>Liaison narrative</b>");
            DrawScenarioPicker(node);
            DrawFlagField("Flag requis :", node.RequiredFlag, v => { node.RequiredFlag = v; MarkDirty(); });
            DrawFlagField("Octroie à la visite :", node.GrantsFlagOnVisit, v => { node.GrantsFlagOnVisit = v; MarkDirty(); });
            bool newStart = GUILayout.Toggle(node.StartingUnlocked, "★ Déverrouillé au départ (brouillard levé)");
            if (newStart != node.StartingUnlocked) { node.StartingUnlocked = newStart; MarkDirty(); }
            GUILayout.EndVertical();

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("⧉ Dupliquer", GUILayout.Height(30)))
                DuplicateNode(node);
            if (GUILayout.Button("🗺️ Voir sur la carte", GUILayout.Height(30)))
                _tab = 0;
            GUI.backgroundColor = new Color(1f, 0.35f, 0.35f);
            if (_confirmDelete == "node:" + node.Id)
            {
                if (GUILayout.Button("⚠ Confirmer suppression (+ liaisons)", GUILayout.Height(30)))
                {
                    DeleteNode(node.Id);
                    _confirmDelete = null;
                }
            }
            else if (GUILayout.Button("🗑 Supprimer", GUILayout.Height(30)))
            {
                _confirmDelete = "node:" + node.Id;
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();
            if (_confirmDelete == "node:" + node.Id && GUILayout.Button("Annuler"))
                _confirmDelete = null;
        }

        private void LabeledField(string label, string value, Action<string> set, float labelWidth)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(labelWidth));
            string nv = GUILayout.TextField(value ?? "");
            if (nv != (value ?? "")) set(nv);
            GUILayout.EndHorizontal();
        }

        private float FloatSlider(string label, float value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(90));
            float nv = GUILayout.HorizontalSlider(value, 0f, 1f);
            GUILayout.Label(value.ToString("0.00"), GUILayout.Width(42));
            GUILayout.EndHorizontal();
            return nv;
        }

        private void DrawScenarioPicker(HybrisSectorNode node)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Scène liée :", GUILayout.Width(90));
            string nv = GUILayout.TextField(node.LinkedScenarioId ?? "");
            if (nv != (node.LinkedScenarioId ?? "")) { node.LinkedScenarioId = nv.Trim(); MarkDirty(); }
            if (GUILayout.Button("✖", GUILayout.Width(28)))
            {
                node.LinkedScenarioId = "";
                MarkDirty();
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Catalogue :", GUILayout.Width(90));
            _scenarioFilter = GUILayout.TextField(_scenarioFilter ?? "", GUILayout.Width(150));
            if (GUILayout.Button("↻", GUILayout.Width(28))) RefreshScenarioCache();
            GUILayout.EndHorizontal();

            _scrollScenarios = GUILayout.BeginScrollView(_scrollScenarios, GUILayout.Height(86));
            if (_scenarioCache != null)
            {
                string f = (_scenarioFilter ?? "").Trim();
                int shown = 0;
                foreach (var kv in _scenarioCache)
                {
                    if (!string.IsNullOrEmpty(f)
                        && kv.Key.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0
                        && kv.Value.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (++shown > 60) { GUILayout.Label("<color=grey>… affinez le filtre …</color>"); break; }
                    bool isLinked = string.Equals(node.LinkedScenarioId, kv.Key, StringComparison.OrdinalIgnoreCase);
                    GUI.backgroundColor = isLinked ? new Color(0.3f, 0.65f, 0.4f) : Color.white;
                    if (GUILayout.Button($"{kv.Key} — {kv.Value}", GUILayout.Height(22)))
                    {
                        node.LinkedScenarioId = kv.Key;
                        MarkDirty();
                    }
                    GUI.backgroundColor = Color.white;
                }
                if (shown == 0) GUILayout.Label("<color=grey>Aucune scène (filtre ou catalogue vide).</color>");
            }
            GUILayout.EndScrollView();
        }

        private void DrawFlagField(string label, string value, Action<string> set)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(90));
            string nv = GUILayout.TextField(value ?? "");
            if (nv != (value ?? "")) set(nv.Trim());
            if (GUILayout.Button("✖", GUILayout.Width(28))) set("");
            GUILayout.EndHorizontal();
            // Flags connus : clic pour remplir.
            GUILayout.BeginHorizontal();
            GUILayout.Space(94);
            foreach (var known in CollectKnownFlags())
            {
                if (string.Equals(known, value ?? "", StringComparison.OrdinalIgnoreCase)) continue;
                if (GUILayout.Button(known, GUILayout.Height(20))) set(known);
            }
            GUILayout.EndHorizontal();
        }

        private List<string> CollectKnownFlags()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                HybrisWorldMapData.EasternExpeditionFlag
            };
            foreach (var n in _doc.Nodes)
            {
                if (n == null) continue;
                if (!string.IsNullOrWhiteSpace(n.RequiredFlag)) set.Add(n.RequiredFlag.Trim());
                if (!string.IsNullOrWhiteSpace(n.GrantsFlagOnVisit)) set.Add(n.GrantsFlagOnVisit.Trim());
            }
            foreach (var l in _doc.Links)
            {
                if (l == null) continue;
                if (!string.IsNullOrWhiteSpace(l.RequiresFlag)) set.Add(l.RequiresFlag.Trim());
            }
            var list = new List<string>(set);
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        // ================= ONGLET LIAISONS =================

        private void DrawLinksTab()
        {
            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical(GUILayout.Width(340));
            if (GUILayout.Button("➕ Nouvelle liaison", GUILayout.Height(30)))
                CreateLinkDefault();
            _scrollList = GUILayout.BeginScrollView(_scrollList, GUILayout.ExpandHeight(true));
            for (int i = 0; i < _doc.Links.Count; i++)
            {
                var l = _doc.Links[i];
                if (l == null) continue;
                bool sel = i == _selectedLinkIndex;
                GUI.backgroundColor = sel ? new Color(1f, 0.85f, 0.25f) : Color.white;
                string row = $"{l.FromId} ↔ {l.ToId} <color=grey>({l.Miles} mi • {l.Days} j{(l.IsSeaCrossing ? " ⛵" : "")})</color>";
                if (GUILayout.Button(row, GUILayout.Height(26)))
                {
                    _selectedLinkIndex = i;
                    _confirmDelete = null;
                }
                GUI.backgroundColor = Color.white;
            }
            if (_doc.Links.Count == 0) GUILayout.Label("<color=grey>Aucune liaison.</color>");
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            _scrollForm = GUILayout.BeginScrollView(_scrollForm, GUILayout.ExpandHeight(true));
            if (_selectedLinkIndex < 0 || _selectedLinkIndex >= _doc.Links.Count || _doc.Links[_selectedLinkIndex] == null)
            {
                GUILayout.Label("<color=grey>Sélectionnez une liaison, ou créez-en une (bouton / mode Lier sur la carte).</color>");
            }
            else
            {
                DrawLinkForm(_doc.Links[_selectedLinkIndex]);
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }

        private void DrawLinkForm(HybrisSectorLink link)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("<b>Extrémités</b>");
            int fromIdx = NodeIndex(link.FromId);
            int toIdx = NodeIndex(link.ToId);
            int newFrom = DrawNodeSelector("De :", fromIdx);
            int newTo = DrawNodeSelector("Vers :", toIdx);
            if (newFrom != fromIdx && newFrom >= 0)
            {
                link.FromId = _doc.Nodes[newFrom].Id;
                MarkDirty();
            }
            if (newTo != toIdx && newTo >= 0)
            {
                link.ToId = _doc.Nodes[newTo].Id;
                MarkDirty();
            }
            if (GUILayout.Button("⇄ Inverser"))
            {
                (link.FromId, link.ToId) = (link.ToId, link.FromId);
                MarkDirty();
            }
            GUILayout.EndVertical();

            GUILayout.Space(4);
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("<b>Coûts & nature</b>");
            int miles = (int)GUILayout.HorizontalSlider(link.Miles, 1, 300);
            if (miles != link.Miles) { link.Miles = miles; MarkDirty(); }
            GUILayout.Label($"Distance : <b>{link.Miles} miles</b> (échelle vieille carte : 0-300)");
            int days = (int)GUILayout.HorizontalSlider(link.Days, 0, 30);
            if (days != link.Days) { link.Days = days; MarkDirty(); }
            GUILayout.Label($"Durée : <b>{link.Days} jour(s)</b>");
            LabeledField("Libellé :", link.Label, v => { link.Label = v; MarkDirty(); }, 90);
            bool sea = GUILayout.Toggle(link.IsSeaCrossing, "⛵ Traversée maritime (Mer des Déportés)");
            if (sea != link.IsSeaCrossing) { link.IsSeaCrossing = sea; MarkDirty(); }
            DrawFlagField("Flag requis :", link.RequiresFlag, v => { link.RequiresFlag = v; MarkDirty(); });
            GUILayout.EndVertical();

            GUILayout.Space(4);
            GUI.backgroundColor = new Color(1f, 0.35f, 0.35f);
            if (_confirmDelete == "link:" + _selectedLinkIndex)
            {
                if (GUILayout.Button("⚠ Confirmer suppression", GUILayout.Height(30)))
                {
                    _doc.Links.RemoveAt(_selectedLinkIndex);
                    _selectedLinkIndex = -1;
                    _confirmDelete = null;
                    MarkDirty();
                    SetStatus("Liaison supprimée.", Color.yellow);
                }
            }
            else if (GUILayout.Button("🗑 Supprimer la liaison", GUILayout.Height(30)))
            {
                _confirmDelete = "link:" + _selectedLinkIndex;
            }
            GUI.backgroundColor = Color.white;
            if (_confirmDelete == "link:" + _selectedLinkIndex && GUILayout.Button("Annuler"))
                _confirmDelete = null;
        }

        private int DrawNodeSelector(string label, int current)
        {
            GUILayout.Label(label);
            string[] names = new string[_doc.Nodes.Count];
            for (int i = 0; i < _doc.Nodes.Count; i++)
                names[i] = _doc.Nodes[i] == null ? "?" : $"{_doc.Nodes[i].Name} ({_doc.Nodes[i].Id})";
            return GUILayout.SelectionGrid(current, names, 1);
        }

        private int NodeIndex(string id)
        {
            if (string.IsNullOrEmpty(id)) return -1;
            for (int i = 0; i < _doc.Nodes.Count; i++)
                if (_doc.Nodes[i] != null && string.Equals(_doc.Nodes[i].Id, id, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        // ================= MUTATIONS =================

        private HybrisSectorNode FindInDoc(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || _doc == null) return null;
            foreach (var n in _doc.Nodes)
                if (n != null && string.Equals(n.Id, id, StringComparison.OrdinalIgnoreCase)) return n;
            return null;
        }

        private void SyncIdBuffer()
        {
            _idEditBuffer = FindInDoc(_selectedNodeId)?.Id ?? "";
        }

        private string UniqueNodeId(string baseId)
        {
            string candidate = baseId;
            int n = 2;
            while (FindInDoc(candidate) != null) candidate = $"{baseId}_{n++}";
            return candidate;
        }

        private void CreateNodeAt(Vector2 normPos)
        {
            string id = UniqueNodeId("secteur_" + (_doc.Nodes.Count + 1));
            string name = string.IsNullOrWhiteSpace(_newNodeName) ? "Nouveau secteur" : _newNodeName.Trim();
            var node = new HybrisSectorNode(id, name, "Hybris", "", normPos.x, normPos.y,
                HybrisBiome.Foret, 1, "", "", "visited_" + id, false);
            _doc.Nodes.Add(node);
            _selectedNodeId = id;
            SyncIdBuffer();
            MarkDirty();
            SetStatus($"Secteur « {id} » créé.", Color.green);
        }

        private void DuplicateNode(HybrisSectorNode source)
        {
            string id = UniqueNodeId(source.Id + "_copie");
            var copy = new HybrisSectorNode(id, source.Name + " (copie)", source.Region, source.Description,
                Mathf.Clamp01(source.MapPos.x + 0.04f), Mathf.Clamp01(source.MapPos.y + 0.04f),
                source.Biome, source.Danger, source.LinkedScenarioId, source.RequiredFlag, "", false);
            _doc.Nodes.Add(copy);
            _selectedNodeId = id;
            SyncIdBuffer();
            MarkDirty();
            SetStatus($"Secteur dupliqué vers « {id} ».", Color.green);
        }

        private void RenameNode(HybrisSectorNode node, string newId)
        {
            newId = (newId ?? "").Trim();
            if (string.IsNullOrEmpty(newId))
            {
                SetStatus("Id vide : renommage annulé.", Color.red);
                SyncIdBuffer();
                return;
            }
            if (!HybrisWorldMapValidation.IsValidId(newId))
            {
                SetStatus("Id invalide : lettres, chiffres, '_' et '-' uniquement.", Color.red);
                SyncIdBuffer();
                return;
            }
            if (string.Equals(newId, node.Id, StringComparison.OrdinalIgnoreCase))
            {
                node.Id = newId;
                SyncIdBuffer();
                return;
            }
            if (FindInDoc(newId) != null)
            {
                SetStatus($"Id « {newId} » déjà utilisé.", Color.red);
                SyncIdBuffer();
                return;
            }
            string oldId = node.Id;
            node.Id = newId;
            foreach (var l in _doc.Links)
            {
                if (l == null) continue;
                if (string.Equals(l.FromId, oldId, StringComparison.OrdinalIgnoreCase)) l.FromId = newId;
                if (string.Equals(l.ToId, oldId, StringComparison.OrdinalIgnoreCase)) l.ToId = newId;
            }
            var director = ScenarioDirector.EnsureInstance();
            if (director.State != null && string.Equals(director.State.PartyNodeId, oldId, StringComparison.OrdinalIgnoreCase))
            {
                director.State.PartyNodeId = newId;
                director.RefreshWorldDefaults();
            }
            _selectedNodeId = newId;
            SyncIdBuffer();
            MarkDirty();
            SetStatus($"Renommé « {oldId} » ➔ « {newId} » (liaisons recâblées).", Color.green);
        }

        private void DeleteNode(string id)
        {
            _doc.Nodes.RemoveAll(n => n != null && string.Equals(n.Id, id, StringComparison.OrdinalIgnoreCase));
            int removed = _doc.Links.RemoveAll(l => l == null
                || string.Equals(l.FromId, id, StringComparison.OrdinalIgnoreCase)
                || string.Equals(l.ToId, id, StringComparison.OrdinalIgnoreCase));
            if (_selectedNodeId == id) _selectedNodeId = _doc.Nodes.Count > 0 ? _doc.Nodes[0].Id : null;
            _selectedLinkIndex = -1;
            SyncIdBuffer();
            MarkDirty();
            SetStatus($"Secteur supprimé ({removed} liaison(s) retirée(s)).", Color.yellow);
        }

        private void CreateLink(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b) || a == b) return;
            foreach (var l in _doc.Links)
            {
                if (l == null) continue;
                if ((l.FromId == a && l.ToId == b) || (l.FromId == b && l.ToId == a))
                {
                    _selectedLinkIndex = _doc.Links.IndexOf(l);
                    SetStatus("Liaison déjà existante (sélectionnée).", Color.yellow);
                    return;
                }
            }
            var link = new HybrisSectorLink(a, b, 50, 2, "Nouvelle route");
            _doc.Links.Add(link);
            _selectedLinkIndex = _doc.Links.Count - 1;
            MarkDirty();
            SetStatus($"Liaison {a} ↔ {b} créée.", Color.green);
        }

        private void CreateLinkDefault()
        {
            if (_doc.Nodes.Count < 2)
            {
                SetStatus("Il faut au moins 2 secteurs pour créer une liaison.", Color.red);
                return;
            }
            string a = FindInDoc(_selectedNodeId)?.Id ?? _doc.Nodes[0].Id;
            string b = null;
            foreach (var n in _doc.Nodes)
            {
                if (n == null || n.Id == a) continue;
                if (HasLink(a, n.Id)) continue;
                b = n.Id;
                break;
            }
            if (b == null)
            {
                SetStatus("Aucune paire libre (tout est déjà relié).", Color.yellow);
                return;
            }
            CreateLink(a, b);
        }

        private bool HasLink(string a, string b)
        {
            foreach (var l in _doc.Links)
            {
                if (l == null) continue;
                if ((l.FromId == a && l.ToId == b) || (l.FromId == b && l.ToId == a)) return true;
            }
            return false;
        }

        // ================= ONGLET FICHIER =================

        private void DrawFileTab()
        {
            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical(GUILayout.Width(340));
            GUILayout.Label("<b>Carte en cours</b>");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Nom :", GUILayout.Width(50));
            string nn = GUILayout.TextField(_mapName ?? "");
            if (nn != (_mapName ?? "")) _mapName = nn.Trim();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.3f, 0.7f, 0.4f);
            if (GUILayout.Button("💾 Sauver", GUILayout.Height(32)))
                SaveDoc();
            GUI.backgroundColor = Color.white;
            if (GUILayout.Button("📄 Nouveau (canon)", GUILayout.Height(32)))
            {
                _doc = HybrisWorldMapSaveData.FromCanon();
                _mapName = _doc.MapName;
                _dirty = true;
                _selectedLinkIndex = -1;
                _selectedNodeId = _doc.Nodes.Count > 0 ? _doc.Nodes[0].Id : null;
                SyncIdBuffer();
                _issuesStale = true;
                SetStatus("Nouveau document depuis le canon (non sauvé).", Color.yellow);
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6);
            GUILayout.Label("<b>Appliquer au jeu</b>");
            GUI.backgroundColor = new Color(0.25f, 0.7f, 1f);
            if (GUILayout.Button("🚀 Appliquer au jeu actif", GUILayout.Height(34)))
                ApplyDocToGame();
            GUI.backgroundColor = Color.white;
            if (GUILayout.Button("↩ Revenir au canon", GUILayout.Height(28)))
            {
                HybrisWorldMapData.ClearRuntimeData();
                ScenarioDirector.EnsureInstance().RefreshWorldDefaults();
                LoadDocFromActive();
                SetStatus("Jeu revenu au canon.", Color.green);
            }
            GUILayout.Space(6);
            GUILayout.Label("<b>Cartes sauvegardées (WorldMaps/)</b>");
            _scrollList = GUILayout.BeginScrollView(_scrollList, GUILayout.Height(150));
            foreach (var name in HybrisWorldMapRepository.ListMapNames())
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(name, GUILayout.ExpandWidth(true));
                if (GUILayout.Button("Charger", GUILayout.Width(70)))
                    LoadNamedMap(name);
                GUI.backgroundColor = new Color(1f, 0.35f, 0.35f);
                if (_confirmDelete == "map:" + name)
                {
                    if (GUILayout.Button("⚠ Oui", GUILayout.Width(60)))
                    {
                        HybrisWorldMapRepository.Delete(name);
                        _confirmDelete = null;
                        SetStatus($"Carte « {name} » supprimée.", Color.yellow);
                    }
                }
                else if (GUILayout.Button("🗑", GUILayout.Width(40)))
                {
                    _confirmDelete = "map:" + name;
                }
                GUI.backgroundColor = Color.white;
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            GUILayout.Label("<b>Import / Export (partage, hors-bande VTT)</b>");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("📤 Exporter (copier JSON)", GUILayout.Height(30)))
            {
                try
                {
                    _doc.EnsureDefaults();
                    GUIUtility.systemCopyBuffer = JsonUtility.ToJson(_doc, true);
                    SetStatus("JSON copié dans le presse-papiers.", Color.green);
                }
                catch (Exception ex) { SetStatus("Export impossible : " + ex.Message, Color.red); }
            }
            if (GUILayout.Button("🧹 Vider", GUILayout.Width(80), GUILayout.Height(30)))
                _importBuffer = "";
            GUILayout.EndHorizontal();
            GUILayout.Label("Coller un JSON de carte monde puis Importer :");
            _importBuffer = GUILayout.TextArea(_importBuffer ?? "", GUILayout.Height(90));
            if (GUILayout.Button("📥 Importer le JSON ci-dessus", GUILayout.Height(30)))
                ImportBuffer();
            _showRawJson = GUILayout.Toggle(_showRawJson, "Voir le JSON brut du document");
            if (_showRawJson)
            {
                _doc.EnsureDefaults();
                _scrollRaw = GUILayout.BeginScrollView(_scrollRaw, GUILayout.ExpandHeight(true));
                GUILayout.TextArea(JsonUtility.ToJson(_doc, true), GUILayout.ExpandHeight(true));
                GUILayout.EndScrollView();
            }
            else
            {
                GUILayout.Label("<i>Le fichier local WorldMaps/*.json est l'unique source du contenu édité (comme Scenarios/*.json).</i>");
            }
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }

        private void SaveDoc()
        {
            if (string.IsNullOrWhiteSpace(_mapName))
            {
                SetStatus("Nom de carte vide.", Color.red);
                return;
            }
            string path = HybrisWorldMapRepository.Save(_doc, _mapName);
            if (path != null)
            {
                _mapName = _doc.MapName;
                _dirty = false;
                SetStatus($"Sauvé : {path}", Color.green);
            }
            else
            {
                SetStatus("Sauvegarde impossible (voir console).", Color.red);
            }
        }

        private void LoadNamedMap(string name)
        {
            if (_dirty)
            {
                SetStatus("Modifications non sauvées — sauvez ou elles seront perdues (re-cliquez pour forcer).", Color.yellow);
                _dirty = false;
                return;
            }
            if (HybrisWorldMapRepository.TryLoad(name, out var data) && data != null)
            {
                _doc = data;
                _mapName = data.MapName;
                _selectedLinkIndex = -1;
                _selectedNodeId = _doc.Nodes.Count > 0 ? _doc.Nodes[0].Id : null;
                SyncIdBuffer();
                _issuesStale = true;
                SetStatus($"Carte « {name} » chargée.", Color.green);
            }
            else
            {
                SetStatus($"Chargement impossible : {name}.", Color.red);
            }
        }

        private void ImportBuffer()
        {
            if (HybrisWorldMapRepository.TryLoadFromJson(_importBuffer, out var data) && data != null)
            {
                _doc = data;
                _mapName = data.MapName;
                _dirty = true;
                _selectedLinkIndex = -1;
                _selectedNodeId = _doc.Nodes.Count > 0 ? _doc.Nodes[0].Id : null;
                SyncIdBuffer();
                _issuesStale = true;
                SetStatus($"JSON importé ({data.Nodes.Count} secteurs, {data.Links.Count} liaisons) — à sauver.", Color.green);
            }
            else
            {
                SetStatus("JSON illisible.", Color.red);
            }
        }

        private void ApplyDocToGame()
        {
            var issues = GetIssues();
            int errors = HybrisWorldMapValidation.CountErrors(issues);
            if (errors > 0)
            {
                SetStatus($"Application bloquée : {errors} erreur(s) — voir Test & VTT.", Color.red);
                _tab = 4;
                return;
            }
            if (FindInDoc(HybrisWorldMapData.StartNodeId) == null)
            {
                SetStatus($"Secteur de départ « {HybrisWorldMapData.StartNodeId} » absent — ajoutez-le ou le groupe sera replacé.", Color.yellow);
            }
            _doc.MapName = string.IsNullOrWhiteSpace(_mapName) ? "custom" : _mapName.Trim();
            HybrisWorldMapData.ApplyRuntimeData(_doc.Clone());
            var director = ScenarioDirector.EnsureInstance();
            director.RefreshWorldDefaults();
            if (HybrisWorldMapData.Find(director.State.PartyNodeId) == null)
            {
                string fallback = HybrisWorldMapData.Find(HybrisWorldMapData.StartNodeId) != null
                    ? HybrisWorldMapData.StartNodeId
                    : HybrisWorldMapData.ActiveNodes.Count > 0 ? HybrisWorldMapData.ActiveNodes[0].Id : null;
                if (!string.IsNullOrEmpty(fallback)) director.TeleportPartyTo(fallback);
            }
            SetStatus($"Appliqué au jeu : {_doc.MapName} (rev {HybrisWorldMapData.ActiveRevision}).", Color.green);
        }

        // ================= ONGLET TEST & VTT =================

        private List<WorldMapIssue> GetIssues()
        {
            if (_issues == null || _issuesStale)
            {
                _issues = HybrisWorldMapValidation.Validate(_doc.Nodes, _doc.Links, ScenarioExists);
                _issuesStale = false;
            }
            return _issues;
        }

        private bool ScenarioExists(string id)
        {
            try { return !string.IsNullOrWhiteSpace(id) && ScenarioCatalog.Find(id) != null; }
            catch { return true; }
        }

        private void DrawTestTab()
        {
            var director = ScenarioDirector.EnsureInstance();
            var state = director.State;

            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>Validation du document</b>");
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("↻ Revalider", GUILayout.Width(110)))
                _issuesStale = true;
            GUILayout.EndHorizontal();

            var issues = GetIssues();
            int errors = HybrisWorldMapValidation.CountErrors(issues);
            GUILayout.Label(errors > 0
                ? $"<color=red><b>{errors} erreur(s)</b></color> — application au jeu bloquée."
                : "<color=green><b>✓ 0 erreur</b> — la carte peut être appliquée au jeu.</color>");

            _scrollIssues = GUILayout.BeginScrollView(_scrollIssues, GUILayout.Height(170));
            foreach (var issue in issues)
            {
                if (issue == null) continue;
                string color = issue.Severity switch
                {
                    WorldMapIssueSeverity.Error => "red",
                    WorldMapIssueSeverity.Warning => "yellow",
                    _ => "grey",
                };
                GUILayout.BeginHorizontal();
                GUILayout.Label($"<color={color}>[{issue.Severity}] {issue.Code}</color> {issue.Message}", GUILayout.ExpandWidth(true));
                if (!string.IsNullOrEmpty(issue.NodeId) && FindInDoc(issue.NodeId) != null && GUILayout.Button("Voir", GUILayout.Width(60)))
                {
                    _selectedNodeId = issue.NodeId;
                    SyncIdBuffer();
                    _tab = 1;
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            GUILayout.Space(4);
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("<b>Simulation de voyage (campagne active)</b>");
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Groupe : <b>{state?.PartyNodeId}</b> • Carte jeu : <b>{HybrisWorldMapData.ActiveMapName}</b> (rev {HybrisWorldMapData.ActiveRevision})", GUILayout.ExpandWidth(true));
            if (GUILayout.Button("↺ Reset monde", GUILayout.Width(120)))
            {
                director.ResetWorldMap();
                SetStatus("Monde réinitialisé (Kingston).", Color.green);
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("◎ Téléporter au secteur sélectionné (god)", GUILayout.Height(28)))
            {
                if (FindInDoc(_selectedNodeId) != null && HybrisWorldMapData.Find(_selectedNodeId) != null)
                {
                    director.TeleportPartyTo(_selectedNodeId);
                    SetStatus($"Groupe téléporté à {_selectedNodeId}.", Color.green);
                }
                else
                {
                    SetStatus("Appliquez d'abord la carte au jeu (onglet Fichier).", Color.yellow);
                }
            }
            if (GUILayout.Button("🔓 Tout déverrouiller", GUILayout.Width(170), GUILayout.Height(28)))
                director.UnlockAllSectors();
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.Space(4);
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("<b>VTT — partage d'état overworld (préparation feature joueur)</b>");
            GUILayout.Label("<i>Le contenu complet voyage hors-bande (onglet Fichier : Export/Import). Ici transite l'état compact GM ➔ joueurs via l'op « worldmap ».</i>");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("📋 Copier l'op worldmap/state", GUILayout.Height(30)))
            {
                try
                {
                    string op = VTTProtocol.BuildWorldMapStateOp(
                        HybrisWorldMapData.ActiveMapName,
                        HybrisWorldMapData.ActiveRevision,
                        state?.PartyNodeId ?? "",
                        state?.UnlockedNodeIds ?? new List<string>(),
                        state?.VisitedNodeIds ?? new List<string>(),
                        state != null ? state.GetInt("jours_voyage") : 0);
                    _vttPreview = op;
                    GUIUtility.systemCopyBuffer = op;
                    SetStatus("Op worldmap/state copiée.", Color.green);
                }
                catch (Exception ex) { SetStatus("Copie impossible : " + ex.Message, Color.red); }
            }
            if (GUILayout.Button("📍 Copier ping du secteur", GUILayout.Width(220), GUILayout.Height(30)))
            {
                string op = VTTProtocol.BuildWorldMapPingSectorOp(_selectedNodeId ?? "", "#FFDD00", "Ici !");
                _vttPreview = op;
                GUIUtility.systemCopyBuffer = op;
                SetStatus("Op worldmap/ping_sector copiée.", Color.green);
            }
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(_vttPreview))
                GUILayout.TextArea(_vttPreview, GUILayout.Height(64));
            GUILayout.Label("<color=grey>Diffusion live (GM ➔ table) : phase suivante — relais hub users/server.js + réception côté joueur (miroir VTTTableSync).</color>");
            GUILayout.EndVertical();
        }
    }
}

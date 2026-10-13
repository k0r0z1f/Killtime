using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.UI;
using Killtime.Core.World;

namespace Killtime.Story
{
    public partial class HybrisWorldMapEditorWindow : FloatingWindow<HybrisWorldMapEditorWindow>
    {
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
            catch { }
        }
        private void DrawSectorsTab()
        {
            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical(GUILayout.Width(330));
            GUILayout.BeginHorizontal();
            GUILayout.Label("Recherche :", GUILayout.Width(80));
            _sectorSearch = GUILayout.TextField(_sectorSearch ?? "");
            GUILayout.EndHorizontal();

            if (GUILayout.Button("➕ Nouveau Secteur", GUILayout.Height(28)))
            {
                RecordUndo("Créer secteur");
                CreateNodeAt(new Vector2(0.5f, 0.5f));
            }

            _scrollList = GUILayout.BeginScrollView(_scrollList, GUILayout.ExpandHeight(true));
            foreach (var n in _doc.Nodes)
            {
                if (n == null) continue;
                if (!string.IsNullOrEmpty(_sectorSearch) &&
                    n.Name.IndexOf(_sectorSearch, StringComparison.OrdinalIgnoreCase) < 0 &&
                    n.Id.IndexOf(_sectorSearch, StringComparison.OrdinalIgnoreCase) < 0) continue;

                bool sel = _selectedNodeId == n.Id;
                GUI.backgroundColor = sel ? new Color(0.2f, 0.75f, 1f) : Color.white;
                if (GUILayout.Button($"{n.Name} ({n.Id})", GUILayout.Height(26)))
                {
                    _selectedNodeId = n.Id;
                    SyncIdBuffer();
                }
                GUI.backgroundColor = Color.white;
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            _scrollForm = GUILayout.BeginScrollView(_scrollForm, GUILayout.ExpandHeight(true));
            var node = FindInDoc(_selectedNodeId);
            if (node != null) DrawNodeForm(node);
            else GUILayout.Label("<color=grey>Sélectionnez un secteur.</color>");
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }
        private void DrawNodeForm(HybrisSectorNode node)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("<b>Identité & Coordonnées Axiales Codex</b>");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Id Unique :", GUILayout.Width(90));
            _idEditBuffer = GUILayout.TextField(_idEditBuffer);
            if (GUILayout.Button("Renommer", GUILayout.Width(90)))
            {
                RecordUndo("Renommer secteur");
                RenameNode(node, _idEditBuffer);
            }
            GUILayout.EndHorizontal();

            LabeledField("Nom affiché :", node.Name, v => { RecordUndo("Nom"); node.Name = v; });
            LabeledField("Région :", node.Region, v => { RecordUndo("Région"); node.Region = v; });
            LabeledField("Description :", node.Description, v => { RecordUndo("Desc"); node.Description = v; });

            GUILayout.BeginHorizontal();
            GUILayout.Label("Coordonnées Axiales :", GUILayout.Width(140));
            GUILayout.Label($"<b>Q = {node.HexQ} | R = {node.HexR} | S = {node.HexS}</b> (Invariance Q+R+S=0)");
            if (GUILayout.Button("Reprojeter vers Hex", GUILayout.Width(160)))
            {
                RecordUndo("Snap axial");
                var axial = HybrisWorldMapData.NormToAxial(node.MapPos);
                node.HexQ = axial.x;
                node.HexR = axial.y;
                node.MapPos = HybrisWorldMapData.AxialToNorm(node.HexQ, node.HexR);
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.Space(4);
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("<b>Biome & Dangerosité</b>");
            GUILayout.BeginHorizontal();
            GUILayout.Label("Biome :", GUILayout.Width(90));
            if (GUILayout.Button("◀", GUILayout.Width(30)))
            {
                RecordUndo("Biome");
                int len = Enum.GetNames(typeof(HybrisBiome)).Length;
                node.Biome = (HybrisBiome)(((int)node.Biome + len - 1) % len);
            }
            GUILayout.Label($"<b>{HybrisWorldMapData.BiomeLabel(node.Biome)}</b>", GUILayout.Width(170));
            if (GUILayout.Button("▶", GUILayout.Width(30)))
            {
                RecordUndo("Biome");
                int len = Enum.GetNames(typeof(HybrisBiome)).Length;
                node.Biome = (HybrisBiome)(((int)node.Biome + 1) % len);
            }
            GUILayout.EndHorizontal();

            int danger = (int)GUILayout.HorizontalSlider(node.Danger, 0, 5);
            if (danger != node.Danger) { RecordUndo("Danger"); node.Danger = danger; }
            GUILayout.Label($"Danger : <b>{HybrisWorldMapData.DangerLabel(node.Danger)}</b>");
            GUILayout.EndVertical();

            GUILayout.Space(4);
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("<b>Liaison Narrative & Déverrouillages</b>");
            DrawScenarioPicker(node);
            LabeledField("Flag requis :", node.RequiredFlag, v => { RecordUndo("Flag requis"); node.RequiredFlag = v.Trim(); });
            LabeledField("Octroie à visite :", node.GrantsFlagOnVisit, v => { RecordUndo("Flag visit"); node.GrantsFlagOnVisit = v.Trim(); });
            bool startUnlocked = GUILayout.Toggle(node.StartingUnlocked, "★ Découvert dès le début de campagne");
            if (startUnlocked != node.StartingUnlocked) { RecordUndo("StartingUnlocked"); node.StartingUnlocked = startUnlocked; }
            GUILayout.EndVertical();

            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(1f, 0.35f, 0.35f);
            if (_confirmDelete == "node:" + node.Id)
            {
                if (GUILayout.Button("⚠ Confirmer Suppression Immédiate", GUILayout.Height(30)))
                {
                    RecordUndo("Supprimer secteur");
                    DeleteNode(node.Id);
                    _confirmDelete = null;
                }
            }
            else if (GUILayout.Button("🗑 Supprimer Secteur", GUILayout.Height(30)))
            {
                _confirmDelete = "node:" + node.Id;
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();
        }
        private void DrawLinksTab()
        {
            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical(GUILayout.Width(340));
            GUILayout.BeginHorizontal();
            GUILayout.Label("Filtre :", GUILayout.Width(50));
            _linkSearch = GUILayout.TextField(_linkSearch ?? "");
            GUILayout.EndHorizontal();

            if (GUILayout.Button("➕ Nouvelle Liaison", GUILayout.Height(28)))
            {
                RecordUndo("Créer liaison");
                CreateLinkDefault();
            }

            _scrollList = GUILayout.BeginScrollView(_scrollList, GUILayout.ExpandHeight(true));
            for (int i = 0; i < _doc.Links.Count; i++)
            {
                var l = _doc.Links[i];
                if (l == null) continue;
                if (!string.IsNullOrEmpty(_linkSearch) &&
                    l.FromId.IndexOf(_linkSearch, StringComparison.OrdinalIgnoreCase) < 0 &&
                    l.ToId.IndexOf(_linkSearch, StringComparison.OrdinalIgnoreCase) < 0) continue;

                bool sel = (i == _selectedLinkIndex);
                GUI.backgroundColor = sel ? new Color(1f, 0.85f, 0.2f) : Color.white;
                if (GUILayout.Button($"{l.FromId} ↔ {l.ToId} ({l.Miles} mi)", GUILayout.Height(26)))
                {
                    _selectedLinkIndex = i;
                    _selectedNodeId = null;
                }
                GUI.backgroundColor = Color.white;
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            _scrollForm = GUILayout.BeginScrollView(_scrollForm, GUILayout.ExpandHeight(true));
            if (_selectedLinkIndex >= 0 && _selectedLinkIndex < _doc.Links.Count && _doc.Links[_selectedLinkIndex] != null)
                DrawLinkForm(_doc.Links[_selectedLinkIndex]);
            else
                GUILayout.Label("<color=grey>Sélectionnez une liaison.</color>");
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
        }
        private void DrawLinkForm(HybrisSectorLink link)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label($"<b>Liaison : {link.FromId} ↔ {link.ToId}</b>");
            if (GUILayout.Button("⚡ Auto-Calculer Miles et Jours selon l'Échelle Manuscrite", GUILayout.Height(30)))
            {
                RecordUndo("Auto calcul link");
                AutoCalculateLinkMetrics(link);
            }

            int miles = (int)GUILayout.HorizontalSlider(link.Miles, 1, 500);
            if (miles != link.Miles) { RecordUndo("Miles"); link.Miles = miles; }
            GUILayout.Label($"Distance : <b>{link.Miles} miles</b>");

            int days = (int)GUILayout.HorizontalSlider(link.Days, 0, 30);
            if (days != link.Days) { RecordUndo("Days"); link.Days = days; }
            GUILayout.Label($"Durée : <b>{link.Days} jour(s)</b>");

            LabeledField("Libellé :", link.Label, v => { RecordUndo("Label"); link.Label = v; });
            bool sea = GUILayout.Toggle(link.IsSeaCrossing, "⛵ Traversée maritime (Mer des Déportés)");
            if (sea != link.IsSeaCrossing) { RecordUndo("SeaCrossing"); link.IsSeaCrossing = sea; }
            LabeledField("Flag requis :", link.RequiresFlag, v => { RecordUndo("RequiresFlag"); link.RequiresFlag = v.Trim(); });

            GUILayout.Space(6);
            GUI.backgroundColor = new Color(1f, 0.3f, 0.3f);
            if (GUILayout.Button("🗑 Supprimer cette Liaison", GUILayout.Height(30)))
            {
                RecordUndo("Supprimer liaison");
                _doc.Links.RemoveAt(_selectedLinkIndex);
                _selectedLinkIndex = -1;
                SetStatus("Liaison supprimée.", Color.yellow);
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndVertical();
        }
        private void AutoCalculateLinkMetrics(HybrisSectorLink link)
        {
            var a = FindInDoc(link.FromId);
            var b = FindInDoc(link.ToId);
            if (a == null || b == null) return;

            float distNorm = Vector2.Distance(a.MapPos, b.MapPos);
            link.Miles = Mathf.Max(5, Mathf.RoundToInt(distNorm * (300f / 0.175f)));
            link.Days = Mathf.Max(1, Mathf.RoundToInt(link.Miles / (link.IsSeaCrossing ? 26f : 20f)));
            SetStatus($"Métriques calculées : {link.Miles} mi, {link.Days} j.", Color.green);
        }
        private void DrawFileTab()
        {
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(360));
            GUILayout.Label("<b>Fichier Local</b>");
            _mapName = GUILayout.TextField(_mapName ?? "");
            if (GUILayout.Button("💾 Sauvegarder dans WorldMaps/", GUILayout.Height(32))) SaveDoc();
            if (GUILayout.Button("🚀 Appliquer au Jeu Actif", GUILayout.Height(34))) ApplyDocToGame();

            GUILayout.Space(10);
            GUILayout.Label("Cartes sauvegardées sur disque :");
            foreach (var name in HybrisWorldMapRepository.ListMapNames())
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(name, GUILayout.ExpandWidth(true));
                if (GUILayout.Button("Charger", GUILayout.Width(70))) LoadNamedMap(name);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();

            GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            GUILayout.Label("<b>Import / Export JSON</b>");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("📋 Exporter (copier JSON)", GUILayout.Height(30)))
            {
                _doc.EnsureDefaults();
                GUIUtility.systemCopyBuffer = JsonUtility.ToJson(_doc, true);
                SetStatus("JSON copié dans le presse-papier.", Color.green);
            }
            if (GUILayout.Button("🧹 Vider", GUILayout.Width(80), GUILayout.Height(30)))
                _importBuffer = "";
            GUILayout.EndHorizontal();

            GUILayout.Label("Coller un JSON de carte monde puis Importer :");
            _importBuffer = GUILayout.TextArea(_importBuffer ?? "", GUILayout.Height(90));
            if (GUILayout.Button("📥 Importer le JSON ci-dessus", GUILayout.Height(30)))
                ImportBuffer();

            _showRawJson = GUILayout.Toggle(_showRawJson, "Afficher l'inspecteur JSON brut");
            if (_showRawJson)
            {
                _scrollRaw = GUILayout.BeginScrollView(_scrollRaw, GUILayout.ExpandHeight(true));
                GUILayout.TextArea(JsonUtility.ToJson(_doc, true));
                GUILayout.EndScrollView();
            }
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }
        private void ImportBuffer()
        {
            if (HybrisWorldMapRepository.TryLoadFromJson(_importBuffer, out var data) && data != null)
            {
                RecordUndo("Importer JSON");
                _doc = data;
                _mapName = data.MapName;
                _selectedLinkIndex = -1;
                _selectedNodeId = _doc.Nodes.Count > 0 ? _doc.Nodes[0].Id : null;
                SyncIdBuffer();
                _issuesStale = true;
                SetStatus($"JSON importé ({data.Nodes.Count} secteurs, {data.Links.Count} liaisons).", Color.green);
            }
            else
            {
                SetStatus("JSON illisible ou structure non conforme.", Color.red);
            }
        }
        private void DrawTestValidationTab()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>Validation d'Intégrité</b>");
            if (GUILayout.Button("↻ Revalider", GUILayout.Width(110))) _issuesStale = true;
            GUILayout.EndHorizontal();

            var issues = GetIssues();
            int errors = HybrisWorldMapValidation.CountErrors(issues);
            GUILayout.Label(errors > 0 ? $"<color=red><b>{errors} erreur(s) critiques</b></color>" : "<color=green><b>✓ Carte 100% conforme</b></color>");

            _scrollIssues = GUILayout.BeginScrollView(_scrollIssues, GUILayout.Height(180));
            foreach (var i in issues)
            {
                string col = i.Severity == WorldMapIssueSeverity.Error ? "red" : "yellow";
                GUILayout.Label($"<color={col}>[{i.Severity}] {i.Code}</color> : {i.Message}");
            }
            GUILayout.EndScrollView();
        }
        private List<WorldMapIssue> GetIssues()
        {
            if (_issues == null || _issuesStale)
            {
                _issues = HybrisWorldMapValidation.Validate(_doc.Nodes, _doc.Links, id => ScenarioCatalog.Find(id) != null);
                _issuesStale = false;
            }
            return _issues;
        }
        private void SaveDoc()
        {
            if (string.IsNullOrWhiteSpace(_mapName)) _mapName = "hybris_overworld";
            _doc.MapName = _mapName;

            string path = HybrisWorldMapRepository.Save(_doc, _mapName);
            if (path != null)
            {
                _dirty = false;
                HybrisWorldMapData.ApplyRuntimeData(_doc.Clone(), persistPref: true);
                ScenarioDirector.EnsureInstance().RefreshWorldDefaults();
                SetStatus($"Sauvegardé et appliqué au jeu actif : {path}", Color.green);
            }
        }
        private void LoadNamedMap(string name)
        {
            if (HybrisWorldMapRepository.TryLoad(name, out var data))
            {
                _doc = data;
                _mapName = data.MapName;
                _dirty = false;
                _selectedNodeId = _doc.Nodes.Count > 0 ? _doc.Nodes[0].Id : null;
                SyncIdBuffer();
                SetStatus($"Carte « {name} » chargée.", Color.green);
            }
        }
        private void ApplyDocToGame()
        {
            var issues = GetIssues();
            if (HybrisWorldMapValidation.CountErrors(issues) > 0)
            {
                SetStatus("Impossible d'appliquer : des erreurs bloquantes subsistent.", Color.red);
                _tab = 4;
                return;
            }

            if (string.IsNullOrWhiteSpace(_mapName)) _mapName = "hybris_overworld";
            _doc.MapName = _mapName;

            HybrisWorldMapRepository.Save(_doc, _mapName);
            HybrisWorldMapData.ApplyRuntimeData(_doc.Clone(), persistPref: true);
            ScenarioDirector.EnsureInstance().RefreshWorldDefaults();
            _dirty = false;
            SetStatus($"Carte « {_mapName} » synchronisée sur disque et appliquée au jeu actif.", Color.green);
        }
        private void DrawScenarioPicker(HybrisSectorNode node)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Scène Liée :", GUILayout.Width(110));
            string nv = GUILayout.TextField(node.LinkedScenarioId ?? "");
            if (nv != (node.LinkedScenarioId ?? "")) { RecordUndo("Scène liée"); node.LinkedScenarioId = nv.Trim(); }
            if (GUILayout.Button("✖", GUILayout.Width(28)))
            {
                RecordUndo("Vider scène liée");
                node.LinkedScenarioId = "";
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Catalogue :", GUILayout.Width(110));
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
                        RecordUndo("Choisir scène catalogue");
                        node.LinkedScenarioId = kv.Key;
                    }
                    GUI.backgroundColor = Color.white;
                }
                if (shown == 0) GUILayout.Label("<color=grey>Aucune scène (filtre ou catalogue vide).</color>");
            }
            GUILayout.EndScrollView();
        }
    }
}

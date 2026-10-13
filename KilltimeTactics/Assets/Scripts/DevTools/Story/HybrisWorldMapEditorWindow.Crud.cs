using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.UI;
using Killtime.Core.World;

namespace Killtime.Story
{
    public partial class HybrisWorldMapEditorWindow : FloatingWindow<HybrisWorldMapEditorWindow>
    {
        private void LoadDocFromActive()
        {
            string targetMap = PlayerPrefs.GetString(HybrisWorldMapData.ActiveMapPrefKey, "hybris_overworld");
            if (string.Equals(targetMap, "factory_canon", StringComparison.OrdinalIgnoreCase))
            {
                targetMap = "hybris_overworld";
            }

            if (HybrisWorldMapRepository.TryLoad(targetMap, out var savedOnDisk))
            {
                _doc = savedOnDisk;
                _mapName = string.IsNullOrWhiteSpace(_doc.MapName) ? "hybris_overworld" : _doc.MapName;
            }
            else if (HybrisWorldMapRepository.TryLoad("hybris_overworld", out var defaultDoc))
            {
                _doc = defaultDoc;
                _mapName = string.IsNullOrWhiteSpace(_doc.MapName) ? "hybris_overworld" : _doc.MapName;
            }
            else
            {
                _doc = new HybrisWorldMapSaveData { MapName = "hybris_overworld" };
                foreach (var n in HybrisWorldMapData.ActiveNodes)
                {
                    if (n == null) continue;
                    _doc.Nodes.Add(new HybrisSectorNode(
                        n.Id, n.Name, n.Region, n.Description,
                        n.MapPos.x, n.MapPos.y, n.Biome, n.Danger,
                        n.LinkedScenarioId, n.RequiredFlag, n.GrantsFlagOnVisit, n.StartingUnlocked,
                        n.HexQ, n.HexR));
                }
                foreach (var l in HybrisWorldMapData.ActiveLinks)
                {
                    if (l == null) continue;
                    _doc.Links.Add(new HybrisSectorLink(
                        l.FromId, l.ToId, l.Miles, l.Days, l.Label, l.IsSeaCrossing, l.RequiresFlag));
                }
                _mapName = "hybris_overworld";
                _doc.MapName = "hybris_overworld";
            }

            _dirty = false;
            _undoStack.Clear();
            _redoStack.Clear();
            _selectedNodeId = _doc.Nodes.Count > 0 ? _doc.Nodes[0].Id : null;
            _selectedLinkIndex = -1;
            SyncIdBuffer();
            _issuesStale = true;
        }
        private void RecordUndo(string action = "")
        {
            _undoStack.Add(JsonUtility.ToJson(_doc));
            if (_undoStack.Count > 40) _undoStack.RemoveAt(0);
            _redoStack.Clear();
            _dirty = true;
            _issuesStale = true;
        }
        private void PerformUndo()
        {
            if (_undoStack.Count == 0) return;
            _redoStack.Add(JsonUtility.ToJson(_doc));
            string json = _undoStack[^1];
            _undoStack.RemoveAt(_undoStack.Count - 1);
            _doc = JsonUtility.FromJson<HybrisWorldMapSaveData>(json);
            _selectedLinkIndex = -1;
            SyncIdBuffer();
            _dirty = true;
            _issuesStale = true;
            SetStatus("Annulation effectuée.", Color.yellow);
        }
        private void PerformRedo()
        {
            if (_redoStack.Count == 0) return;
            _undoStack.Add(JsonUtility.ToJson(_doc));
            string json = _redoStack[^1];
            _redoStack.RemoveAt(_redoStack.Count - 1);
            _doc = JsonUtility.FromJson<HybrisWorldMapSaveData>(json);
            _selectedLinkIndex = -1;
            SyncIdBuffer();
            _dirty = true;
            _issuesStale = true;
            SetStatus("Rétablissement effectué.", Color.yellow);
        }
        private void InjectCodexRegistry()
        {
            RecordUndo("Synchroniser Registre Codex");
            int added = 0;
            foreach (var kvp in HybrisWorldMapRegistry.Locations)
            {
                var loc = kvp.Value;
                var existing = FindInDoc(loc.Code);
                if (existing == null)
                {
                    HybrisBiome biome = loc.Category switch
                    {
                        LandmarkCategory.MajorKingdom => HybrisBiome.CiteHumaine,
                        LandmarkCategory.Hydrology => HybrisBiome.Sanctuaire,
                        LandmarkCategory.MilitaryCamp => HybrisBiome.Montagne,
                        LandmarkCategory.TitanLair => HybrisBiome.TerresInterdites,
                        LandmarkCategory.SettlementOrPort => loc.DominantSpecies == "Taurien" ? HybrisBiome.CiteElfique :
                                                             loc.DominantSpecies == "Nain" ? HybrisBiome.CiteNaine : HybrisBiome.Port,
                        _ => HybrisBiome.Foret
                    };

                    int danger = loc.Category == LandmarkCategory.TitanLair ? 5 : (loc.Category == LandmarkCategory.MilitaryCamp ? 3 : 1);
                    var node = new HybrisSectorNode(
                        loc.Code, loc.CanonicalName, "Codex // " + loc.Category, loc.LoreNotes,
                        loc.NormalizedPos.x, loc.NormalizedPos.y, biome, danger,
                        "", "", "visited_" + loc.Code, loc.Code == "kingston" || loc.Category == LandmarkCategory.MajorKingdom,
                        loc.AxialCoords.Q, loc.AxialCoords.R);
                    _doc.Nodes.Add(node);
                    added++;
                }
            }
            _dirty = true;
            _issuesStale = true;
            SetStatus($"Synchronisation Codex : {added} nouveau(x) secteur(s) importé(s).", Color.green);
        }
        private void CreateNodeAt(Vector2 norm)
        {
            string id = "secteur_" + (_doc.Nodes.Count + 1);
            var axial = HybrisWorldMapData.NormToAxial(norm);
            var node = new HybrisSectorNode(id, _newNodeName, "Hybris", "", norm.x, norm.y, HybrisBiome.Foret, 1, "", "", "visited_" + id, false, axial.x, axial.y);
            _doc.Nodes.Add(node);
            _selectedNodeId = id;
            SyncIdBuffer();
            _dirty = true;
            SetStatus($"Secteur « {id} » créé.", Color.green);
        }
        private void CreateLink(string a, string b)
        {
            if (HasLink(a, b)) return;
            var l = new HybrisSectorLink(a, b, 50, 2, "Route");
            AutoCalculateLinkMetrics(l);
            _doc.Links.Add(l);
            _selectedLinkIndex = _doc.Links.Count - 1;
            _dirty = true;
        }
        private void CreateLinkDefault()
        {
            if (_doc.Nodes.Count < 2) return;
            string a = _selectedNodeId ?? _doc.Nodes[0].Id;
            foreach (var n in _doc.Nodes)
            {
                if (n.Id == a || HasLink(a, n.Id)) continue;
                CreateLink(a, n.Id);
                break;
            }
        }
        private bool HasLink(string a, string b)
        {
            return _doc.Links.Exists(l => l != null && l.Connects(a) && l.Connects(b));
        }
        private void DeleteNode(string id)
        {
            _doc.Nodes.RemoveAll(n => n != null && n.Id == id);
            _doc.Links.RemoveAll(l => l != null && l.Connects(id));
            _selectedNodeId = _doc.Nodes.Count > 0 ? _doc.Nodes[0].Id : null;
            _selectedLinkIndex = -1;
            SyncIdBuffer();
            _dirty = true;
        }
        private bool RenameNode(HybrisSectorNode node, string newId)
        {
            newId = (newId ?? "").Trim();
            if (string.IsNullOrEmpty(newId))
            {
                SetStatus("Erreur : l'identifiant unique ne peut pas être vide.", Color.red);
                return false;
            }
            if (!HybrisWorldMapValidation.IsValidId(newId))
            {
                SetStatus($"Erreur : « {newId} » est invalide (lettres, chiffres, '_' et '-' uniquement).", Color.red);
                return false;
            }
            if (FindInDoc(newId) != null && !string.Equals(newId, node.Id, StringComparison.OrdinalIgnoreCase))
            {
                SetStatus($"Erreur : un secteur avec l'ID « {newId} » existe déjà.", Color.red);
                return false;
            }

            string oldId = node.Id;
            node.Id = newId;

            if (node.GrantsFlagOnVisit == "visited_" + oldId)
            {
                node.GrantsFlagOnVisit = "visited_" + newId;
            }

            foreach (var l in _doc.Links)
            {
                if (l == null) continue;
                if (string.Equals(l.FromId, oldId, StringComparison.OrdinalIgnoreCase)) l.FromId = newId;
                if (string.Equals(l.ToId, oldId, StringComparison.OrdinalIgnoreCase)) l.ToId = newId;
            }

            if (_linkFrom == oldId) _linkFrom = newId;
            _selectedNodeId = newId;
            SyncIdBuffer();
            _dirty = true;
            _issuesStale = true;
            SetStatus($"Secteur renommé : « {newId} ».", Color.green);
            return true;
        }
        private HybrisSectorNode FindInDoc(string id)
        {
            if (string.IsNullOrWhiteSpace(id) || _doc == null) return null;
            return _doc.Nodes.Find(n => n != null && string.Equals(n.Id, id, StringComparison.OrdinalIgnoreCase));
        }
        private void SyncIdBuffer()
        {
            _idEditBuffer = FindInDoc(_selectedNodeId)?.Id ?? "";
        }
        private void LabeledField(string label, string val, Action<string> onSet)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(110));
            string n = GUILayout.TextField(val ?? "");
            if (n != (val ?? "")) onSet(n);
            GUILayout.EndHorizontal();
        }
        private void DuplicateNode(HybrisSectorNode source)
        {
            if (source == null) return;
            RecordUndo("Dupliquer secteur");
            string newId = source.Id + "_copie";
            int counter = 1;
            while (FindInDoc(newId) != null)
            {
                newId = $"{source.Id}_copie{++counter}";
            }

            Vector2 offsetPos = source.MapPos + new Vector2(0.02f, 0.02f);
            var axial = HybrisWorldMapData.NormToAxial(offsetPos);
            var clone = new HybrisSectorNode(
                newId, source.Name + " (Copie)", source.Region, source.Description,
                offsetPos.x, offsetPos.y, source.Biome, source.Danger,
                source.LinkedScenarioId, source.RequiredFlag, source.GrantsFlagOnVisit,
                source.StartingUnlocked, axial.x, axial.y);

            _doc.Nodes.Add(clone);
            _selectedNodeId = newId;
            SyncIdBuffer();
            _dirty = true;
            _issuesStale = true;
            SetStatus($"Secteur « {newId} » créé par duplication.", Color.green);
        }
    }
}

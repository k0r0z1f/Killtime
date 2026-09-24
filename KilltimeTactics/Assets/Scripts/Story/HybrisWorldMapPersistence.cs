using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Killtime.Story
{
    /// <summary>
    /// Conteneur sérialisable d'une carte monde (secteurs + liaisons).
    /// Format versionné, compatible JsonUtility / WebGL / futur partage VTT.
    /// Les listes plutôt que les dictionnaires garantissent la désérialisation.
    /// </summary>
    [Serializable]
    public class HybrisWorldMapSaveData
    {
        public int Version = 1;
        public string MapName = "hybris_overworld";
        public string SavedAtUtc = "";
        public List<HybrisSectorNode> Nodes = new();
        public List<HybrisSectorLink> Links = new();

        /// <summary>Clone profond via JSON (isole l'édition du jeu actif).</summary>
        public HybrisWorldMapSaveData Clone()
        {
            try
            {
                return JsonUtility.FromJson<HybrisWorldMapSaveData>(JsonUtility.ToJson(this));
            }
            catch
            {
                var copy = new HybrisWorldMapSaveData { MapName = MapName };
                if (Nodes != null) copy.Nodes = new List<HybrisSectorNode>(Nodes);
                if (Links != null) copy.Links = new List<HybrisSectorLink>(Links);
                return copy;
            }
        }

        /// <summary>Répare les données chargées (listes nulles, champs vides, danger hors bornes).</summary>
        public void EnsureDefaults()
        {
            Nodes ??= new List<HybrisSectorNode>();
            Links ??= new List<HybrisSectorLink>();
            if (string.IsNullOrWhiteSpace(MapName)) MapName = "hybris_overworld";
            for (int i = 0; i < Nodes.Count; i++)
            {
                var n = Nodes[i];
                if (n == null) continue;
                n.Id ??= "";
                n.Name ??= "";
                n.Region ??= "";
                n.Description ??= "";
                n.LinkedScenarioId ??= "";
                n.RequiredFlag ??= "";
                n.GrantsFlagOnVisit ??= "";
                n.Danger = Mathf.Clamp(n.Danger, 0, 5);
                n.MapPos = new Vector2(Mathf.Clamp01(n.MapPos.x), Mathf.Clamp01(n.MapPos.y));
            }
            for (int i = 0; i < Links.Count; i++)
            {
                var l = Links[i];
                if (l == null) continue;
                l.FromId ??= "";
                l.ToId ??= "";
                l.Label ??= "";
                l.RequiresFlag ??= "";
                l.Miles = Mathf.Max(1, l.Miles);
                l.Days = Mathf.Clamp(l.Days, 0, 30);
            }
        }

        public static HybrisWorldMapSaveData FromCanon()
        {
            var data = new HybrisWorldMapSaveData { MapName = "hybris_overworld" };
            foreach (var n in HybrisWorldMapData.Nodes)
            {
                if (n == null) continue;
                data.Nodes.Add(new HybrisSectorNode(
                    n.Id, n.Name, n.Region, n.Description,
                    n.MapPos.x, n.MapPos.y, n.Biome, n.Danger,
                    n.LinkedScenarioId, n.RequiredFlag, n.GrantsFlagOnVisit, n.StartingUnlocked));
            }
            foreach (var l in HybrisWorldMapData.Links)
            {
                if (l == null) continue;
                data.Links.Add(new HybrisSectorLink(
                    l.FromId, l.ToId, l.Miles, l.Days, l.Label, l.IsSeaCrossing, l.RequiresFlag));
            }
            return data;
        }
    }

    /// <summary>
    /// Persistance des cartes monde en JSON (WorldMaps/*.json), calquée sur
    /// StorySceneRepository : le fichier local est l'unique source du contenu édité.
    /// </summary>
    public static class HybrisWorldMapRepository
    {
        public const string DefaultFileName = "hybris_overworld.json";

        public static string WorldMapsDirectory => Path.Combine(Application.persistentDataPath, "WorldMaps");

        public static string GetWorldMapFilePath(string mapName)
        {
            if (string.IsNullOrWhiteSpace(mapName)) return string.Empty;
            string fileName = mapName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? mapName : $"{mapName}.json";
            // Sécurise : nom de fichier uniquement, jamais un chemin.
            fileName = Path.GetFileName(fileName);
            return Path.Combine(WorldMapsDirectory, fileName);
        }

        public static string Save(HybrisWorldMapSaveData data, string mapName)
        {
            if (data == null) return null;
            try
            {
                string path = GetWorldMapFilePath(mapName);
                if (string.IsNullOrEmpty(path)) return null;
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                data.MapName = Path.GetFileNameWithoutExtension(path);
                data.SavedAtUtc = DateTime.UtcNow.ToString("o");
                data.EnsureDefaults();
                File.WriteAllText(path, JsonUtility.ToJson(data, true));
                return path;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[HybrisWorldMapRepository] Sauvegarde impossible : {ex.Message}");
                return null;
            }
        }

        public static bool TryLoad(string mapName, out HybrisWorldMapSaveData data)
        {
            data = null;
            if (string.IsNullOrWhiteSpace(mapName)) return false;
            try
            {
                string path = GetWorldMapFilePath(mapName);
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
                data = JsonUtility.FromJson<HybrisWorldMapSaveData>(File.ReadAllText(path));
                if (data == null) return false;
                if (string.IsNullOrWhiteSpace(data.MapName))
                    data.MapName = Path.GetFileNameWithoutExtension(path);
                data.EnsureDefaults();
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[HybrisWorldMapRepository] Lecture impossible '{mapName}' : {ex.Message}");
                data = null;
                return false;
            }
        }

        public static bool TryLoadFromJson(string json, out HybrisWorldMapSaveData data)
        {
            data = null;
            if (string.IsNullOrWhiteSpace(json)) return false;
            try
            {
                data = JsonUtility.FromJson<HybrisWorldMapSaveData>(json);
                if (data == null) return false;
                data.EnsureDefaults();
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[HybrisWorldMapRepository] JSON illisible : {ex.Message}");
                data = null;
                return false;
            }
        }

        public static List<string> ListMapNames()
        {
            var names = new List<string>();
            try
            {
                string dir = WorldMapsDirectory;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                foreach (var f in Directory.GetFiles(dir, "*.json"))
                    names.Add(Path.GetFileNameWithoutExtension(f));
                names.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[HybrisWorldMapRepository] Listage impossible : {ex.Message}");
            }
            return names;
        }

        public static bool Delete(string mapName)
        {
            if (string.IsNullOrWhiteSpace(mapName)) return false;
            try
            {
                string path = GetWorldMapFilePath(mapName);
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    File.Delete(path);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[HybrisWorldMapRepository] Suppression impossible : {ex.Message}");
            }
            return false;
        }
    }
}

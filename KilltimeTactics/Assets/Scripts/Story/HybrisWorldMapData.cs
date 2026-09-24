using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Core.World;

namespace Killtime.Story
{
    public enum HybrisBiome
    {
        CiteHumaine,
        CiteNaine,
        CiteElfique,
        Montagne,
        Foret,
        Jungle,
        Sanctuaire,
        Port,
        Ile,
        TerresInterdites
    }

    [System.Serializable]
    public class HybrisSectorNode
    {
        public string Id;
        public string Name;
        public string Region;
        public string Description;
        public Vector2 MapPos;
        public int HexQ;
        public int HexR;
        public int HexS => -HexQ - HexR;
        public HybrisBiome Biome;
        public int Danger;
        public string LinkedScenarioId;
        public string RequiredFlag;
        public string GrantsFlagOnVisit;
        public bool StartingUnlocked;

        public HybrisSectorNode() { }

        public HybrisSectorNode(string id, string name, string region, string description,
            float x, float y, HybrisBiome biome, int danger,
            string linkedScenarioId = "", string requiredFlag = "",
            string grantsFlagOnVisit = "", bool startingUnlocked = false,
            int hexQ = 0, int hexR = 0)
        {
            Id = id;
            Name = name;
            Region = region;
            Description = description;
            MapPos = new Vector2(x, y);
            Biome = biome;
            Danger = Mathf.Clamp(danger, 0, 5);
            LinkedScenarioId = linkedScenarioId;
            RequiredFlag = requiredFlag;
            GrantsFlagOnVisit = grantsFlagOnVisit;
            StartingUnlocked = startingUnlocked;

            if (hexQ == 0 && hexR == 0)
            {
                var axial = HybrisWorldMapData.NormToAxial(MapPos);
                HexQ = axial.x;
                HexR = axial.y;
            }
            else
            {
                HexQ = hexQ;
                HexR = hexR;
            }
        }
    }

    [System.Serializable]
    public class HybrisSectorLink
    {
        public string FromId;
        public string ToId;
        public int Miles;
        public int Days;
        public string Label;
        public bool IsSeaCrossing;
        public string RequiresFlag;

        public HybrisSectorLink() { }

        public HybrisSectorLink(string fromId, string toId, int miles, int days,
            string label, bool isSeaCrossing = false, string requiresFlag = "")
        {
            FromId = fromId;
            ToId = toId;
            Miles = miles;
            Days = days;
            Label = label;
            IsSeaCrossing = isSeaCrossing;
            RequiresFlag = requiresFlag;
        }

        public bool Connects(string nodeId) => FromId == nodeId || ToId == nodeId;

        public string OtherEnd(string nodeId)
        {
            if (FromId == nodeId) return ToId;
            if (ToId == nodeId) return FromId;
            return null;
        }
    }

    public class WorldMapPathResult
    {
        public List<string> Path = new();
        public List<HybrisSectorLink> Links = new();
        public int TotalMiles;
        public int TotalDays;
        public bool IsReachable;
        public string BlockedReason;
    }

    public static class HybrisWorldMapData
    {
        public const string StartNodeId = "kingston";
        public const string EasternExpeditionFlag = "expedition_est";
        public const float HexLatticeScaleX = 26f;
        public const float HexLatticeScaleY = 20f;

        public static readonly List<HybrisSectorNode> Nodes = new()
        {
            new HybrisSectorNode(
                "kingston", "Kingston", "Terres Centrales // Hybris Ouest",
                "Plus grande métropole humaine. Académie (Lucas, Mina — 1771 HSC), QG de la World Police. Carrefour arcanotech.",
                0.30f, 0.56f, HybrisBiome.CiteHumaine, 1,
                "volume_1_scene_09", "", "visited_kingston", true),

            new HybrisSectorNode(
                "4", "Marscua", "Montagnes Naines // Hybris Ouest",
                "Cité fortifiée érigée au confluent des bassins miniers. Portes d'acier noir et de nytharite.",
                0.28f, 0.35f, HybrisBiome.CiteNaine, 2,
                "volume_1_scene_11", "", "visited_4", false),

            new HybrisSectorNode(
                "5", "Brot", "Fiefs Troglodytiques // Nord-Ouest",
                "Mégapole souterraine : fonderies thermiques, machineries magi-tech.",
                0.20f, 0.36f, HybrisBiome.CiteNaine, 2,
                "volume_1_scene_12", "", "visited_5", false),

            new HybrisSectorNode(
                "Haliriel", "Falaises d'Haliriel", "Grande Forêt // Pic Sud-Ouest",
                "Cratère volcanique, Temple Haut de la Nature et territoire de la Bête d'Hybris.",
                0.24f, 0.70f, HybrisBiome.TerresInterdites, 4,
                "volume_1_scene_17", "", "visited_Haliriel", false),

            new HybrisSectorNode(
                "h", "Port de Mito", "Côte Ouest // Mer des Déportés",
                "Port de pêche et d'embarquement vers Vagas et les archipels centraux.",
                0.19f, 0.47f, HybrisBiome.Port, 1,
                "", "", "visited_h", true),

            new HybrisSectorNode(
                "8", "Akalemna", "Détroit Occidental // Nord-Ouest",
                "Capitale marchande aux comptoirs fortifiés.",
                0.16f, 0.50f, HybrisBiome.CiteHumaine, 1,
                "", "", "visited_8", false),

            new HybrisSectorNode(
                "ae", "Île de Vagas", "Mer des Déportés // Centre",
                "Île pivot et escale obligatoire contrôlant la passe entre les deux continents.",
                0.52f, 0.68f, HybrisBiome.Ile, 2,
                "", "", "visited_ae", false),

            new HybrisSectorNode(
                "2", "Royaume d'Atika", "Continent Est // Littoral Aride",
                "Royaume côtier bordant les sables occidentaux du continent Est. Tête de pont de l'expédition.",
                0.68f, 0.60f, HybrisBiome.CiteHumaine, 3,
                "", EasternExpeditionFlag, "visited_2", false),

            new HybrisSectorNode(
                "3", "Cité d'Obelia", "Continent Est // Cœur Académique",
                "Sanctuaire d'études des flux arcanotech et des lignes telluriques.",
                0.74f, 0.48f, HybrisBiome.Sanctuaire, 3,
                "", EasternExpeditionFlag, "visited_3", false),

            new HybrisSectorNode(
                "6", "Royaume de Lepla", "Continent Est // Forêt Taurienne",
                "Fief sylvestre oriental régi par les pactes des hardes tauriennes.",
                0.83f, 0.38f, HybrisBiome.CiteElfique, 3,
                "", EasternExpeditionFlag, "visited_6", false),

            new HybrisSectorNode(
                "7", "Royaume de Prat", "Continent Est // Marches Australes",
                "Bastions militarisés gardant les frontières du sud vardien.",
                0.81f, 0.76f, HybrisBiome.Montagne, 3,
                "", EasternExpeditionFlag, "visited_7", false),

            new HybrisSectorNode(
                "1", "Aurora", "Péninsule Septentrionale",
                "Cité isolée des pionniers et du savoir temporel boréal.",
                0.63f, 0.10f, HybrisBiome.Ile, 2,
                "volume_1_scene_02", "", "visited_1", false),

            new HybrisSectorNode(
                "f", "Camp d'Atola", "Plaine Nord // Hybris Ouest",
                "Garnison d'observation militaire de la World Police.",
                0.35f, 0.44f, HybrisBiome.Montagne, 2,
                "", "", "visited_f", false),

            new HybrisSectorNode(
                "g", "Camp d'Emilia", "Détroit Central // Surveillance",
                "Fort de garde surplombant la Mer des Déportés.",
                0.39f, 0.50f, HybrisBiome.Montagne, 2,
                "", "", "visited_g", false)
        };

        public static readonly List<HybrisSectorLink> Links = new()
        {
            new HybrisSectorLink("kingston", "f", 40, 2, "Voie de patrouille"),
            new HybrisSectorLink("f", "4", 35, 2, "Passe de Marscua"),
            new HybrisSectorLink("4", "5", 25, 1, "Galeries troglodytiques"),
            new HybrisSectorLink("kingston", "Haliriel", 75, 4, "Route de la Canopée"),
            new HybrisSectorLink("kingston", "h", 50, 2, "Route de la Côte"),
            new HybrisSectorLink("h", "8", 20, 1, "Détroit d'Akalemna"),
            new HybrisSectorLink("kingston", "g", 30, 1, "Crête d'Emilia"),
            new HybrisSectorLink("g", "ae", 90, 4, "Traversée de Vagas", true),
            new HybrisSectorLink("h", "ae", 120, 5, "Mer des Déportés", true),
            new HybrisSectorLink("ae", "2", 85, 3, "Passe orientale", true, EasternExpeditionFlag),
            new HybrisSectorLink("2", "3", 60, 3, "Sables d'Atika", false, EasternExpeditionFlag),
            new HybrisSectorLink("3", "6", 70, 3, "Canopée de Lepla", false, EasternExpeditionFlag),
            new HybrisSectorLink("2", "7", 80, 4, "Marches de Prat", false, EasternExpeditionFlag),
            new HybrisSectorLink("h", "1", 160, 6, "Route Boréale", true)
        };

        public static Vector2Int NormToAxial(Vector2 norm)
        {
            float q = (norm.x - 0.5f) * HexLatticeScaleX;
            float r = (norm.y - 0.5f) * HexLatticeScaleY - q * 0.5f;
            return RoundAxial(q, r);
        }

        public static Vector2 AxialToNorm(int q, int r)
        {
            float x = (q / HexLatticeScaleX) + 0.5f;
            float y = ((r + q * 0.5f) / HexLatticeScaleY) + 0.5f;
            return new Vector2(Mathf.Clamp01(x), Mathf.Clamp01(y));
        }

        private static Vector2Int RoundAxial(float fracQ, float fracR)
        {
            float fracS = -fracQ - fracR;
            int q = Mathf.RoundToInt(fracQ);
            int r = Mathf.RoundToInt(fracR);
            int s = Mathf.RoundToInt(fracS);

            float qDiff = Mathf.Abs(q - fracQ);
            float rDiff = Mathf.Abs(r - fracR);
            float sDiff = Mathf.Abs(s - fracS);

            if (qDiff > rDiff && qDiff > sDiff)
                q = -r - s;
            else if (rDiff > sDiff)
                r = -q - s;

            return new Vector2Int(q, r);
        }

        public static WorldMapPathResult FindShortestPath(string fromId, string toId, bool ignoreLocks = false, CampaignState state = null)
        {
            var res = new WorldMapPathResult();
            if (string.IsNullOrEmpty(fromId) || string.IsNullOrEmpty(toId)) return res;
            if (fromId == toId)
            {
                res.Path.Add(fromId);
                res.IsReachable = true;
                return res;
            }

            var distances = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var previous = new Dictionary<string, (string prevNode, HybrisSectorLink link)>(StringComparer.OrdinalIgnoreCase);
            var queue = new List<string>();

            foreach (var n in ActiveNodes)
            {
                if (n == null) continue;
                distances[n.Id] = int.MaxValue;
                queue.Add(n.Id);
            }

            if (!distances.ContainsKey(fromId) || !distances.ContainsKey(toId)) return res;

            distances[fromId] = 0;

            while (queue.Count > 0)
            {
                queue.Sort((a, b) => distances[a].CompareTo(distances[b]));
                string u = queue[0];
                queue.RemoveAt(0);

                if (distances[u] == int.MaxValue) break;
                if (u == toId) break;

                foreach (var link in GetLinksFor(u))
                {
                    string v = link.OtherEnd(u);
                    if (!queue.Contains(v)) continue;

                    if (!ignoreLocks && state != null)
                    {
                        if (!string.IsNullOrWhiteSpace(link.RequiresFlag) && !state.HasFlag(link.RequiresFlag)) continue;
                        var destNode = Find(v);
                        if (destNode != null && !string.IsNullOrWhiteSpace(destNode.RequiredFlag) && !state.HasFlag(destNode.RequiredFlag)) continue;
                    }

                    int alt = distances[u] + Mathf.Max(1, link.Days);
                    if (alt < distances[v])
                    {
                        distances[v] = alt;
                        previous[v] = (u, link);
                    }
                }
            }

            if (!previous.ContainsKey(toId))
            {
                res.IsReachable = false;
                res.BlockedReason = "Aucun chemin praticable ou secteur verrouillé.";
                return res;
            }

            string curr = toId;
            while (previous.ContainsKey(curr))
            {
                var entry = previous[curr];
                res.Path.Insert(0, curr);
                res.Links.Insert(0, entry.link);
                res.TotalDays += entry.link.Days;
                res.TotalMiles += entry.link.Miles;
                curr = entry.prevNode;
            }
            res.Path.Insert(0, fromId);
            res.IsReachable = true;
            return res;
        }

        public static HybrisSectorNode Find(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            var nodes = ActiveNodes;
            for (int i = 0; i < nodes.Count; i++)
                if (nodes[i] != null && string.Equals(nodes[i].Id, id, StringComparison.OrdinalIgnoreCase)) return nodes[i];
            return null;
        }

        public static HybrisSectorLink FindLink(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return null;
            var links = ActiveLinks;
            for (int i = 0; i < links.Count; i++)
            {
                var l = links[i];
                if (l == null) continue;
                if ((string.Equals(l.FromId, a, StringComparison.OrdinalIgnoreCase) && string.Equals(l.ToId, b, StringComparison.OrdinalIgnoreCase)) ||
                    (string.Equals(l.FromId, b, StringComparison.OrdinalIgnoreCase) && string.Equals(l.ToId, a, StringComparison.OrdinalIgnoreCase)))
                    return l;
            }
            return null;
        }

        public static List<HybrisSectorLink> GetLinksFor(string nodeId)
        {
            var result = new List<HybrisSectorLink>();
            if (string.IsNullOrWhiteSpace(nodeId)) return result;
            var links = ActiveLinks;
            for (int i = 0; i < links.Count; i++)
                if (links[i] != null && links[i].Connects(nodeId)) result.Add(links[i]);
            return result;
        }

        public const string ActiveMapPrefKey = "KT_ActiveWorldMapName";

        private static List<HybrisSectorNode> _runtimeNodes;
        private static List<HybrisSectorLink> _runtimeLinks;
        private static bool _initialized;

        public static string ActiveMapName { get; private set; } = "canon";
        public static int ActiveRevision { get; private set; }

        public static bool IsCustomized
        {
            get
            {
                EnsureInitialized();
                return _runtimeNodes != null || _runtimeLinks != null;
            }
        }

        public static List<HybrisSectorNode> ActiveNodes
        {
            get
            {
                EnsureInitialized();
                return _runtimeNodes ?? Nodes;
            }
        }

        public static List<HybrisSectorLink> ActiveLinks
        {
            get
            {
                EnsureInitialized();
                return _runtimeLinks ?? Links;
            }
        }

        public static void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;
            InitializeWorldMap();
        }

        public static void ForceReloadActive()
        {
            _initialized = false;
            EnsureInitialized();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void InitializeWorldMap()
        {
            _initialized = true;
            string targetMap = PlayerPrefs.GetString(ActiveMapPrefKey, "hybris_overworld");

            if (string.Equals(targetMap, "factory_canon", StringComparison.OrdinalIgnoreCase))
            {
                _runtimeNodes = null;
                _runtimeLinks = null;
                ActiveMapName = "canon";
                return;
            }

            if (!string.IsNullOrEmpty(targetMap) && HybrisWorldMapRepository.TryLoad(targetMap, out var targetData))
            {
                ApplyRuntimeData(targetData, persistPref: false);
                return;
            }

            if (HybrisWorldMapRepository.TryLoad("hybris_overworld", out var defaultOverworld))
            {
                ApplyRuntimeData(defaultOverworld, persistPref: false);
            }
        }

        public static void ApplyRuntimeData(HybrisWorldMapSaveData data)
        {
            ApplyRuntimeData(data, persistPref: true);
        }

        public static void ApplyRuntimeData(HybrisWorldMapSaveData data, bool persistPref)
        {
            _initialized = true;
            if (data == null)
            {
                ClearRuntimeData();
                return;
            }
            data.EnsureDefaults();
            _runtimeNodes = new List<HybrisSectorNode>(data.Nodes.Count);
            foreach (var n in data.Nodes)
            {
                if (n == null) continue;
                _runtimeNodes.Add(new HybrisSectorNode(
                    n.Id, n.Name, n.Region, n.Description,
                    n.MapPos.x, n.MapPos.y, n.Biome, n.Danger,
                    n.LinkedScenarioId, n.RequiredFlag, n.GrantsFlagOnVisit, n.StartingUnlocked,
                    n.HexQ, n.HexR));
            }
            _runtimeLinks = new List<HybrisSectorLink>(data.Links.Count);
            foreach (var l in data.Links)
            {
                if (l == null) continue;
                _runtimeLinks.Add(new HybrisSectorLink(
                    l.FromId, l.ToId, l.Miles, l.Days, l.Label, l.IsSeaCrossing, l.RequiresFlag));
            }

            string cleanMapName = string.IsNullOrWhiteSpace(data.MapName) ? "hybris_overworld" : data.MapName;
            ActiveMapName = cleanMapName;
            ActiveRevision++;

            if (persistPref)
            {
                PlayerPrefs.SetString(ActiveMapPrefKey, ActiveMapName);
                PlayerPrefs.Save();
            }
        }

        public static void ClearRuntimeData()
        {
            _runtimeNodes = null;
            _runtimeLinks = null;
            ActiveMapName = "canon";
            ActiveRevision++;
            _initialized = true;
            PlayerPrefs.SetString(ActiveMapPrefKey, "factory_canon");
            PlayerPrefs.Save();
        }

        public static string DangerLabel(int danger)
        {
            return danger switch
            {
                <= 0 => "Sûr",
                1 => "★☆☆☆☆ Surveillé",
                2 => "★★☆☆☆ Hostile",
                3 => "★★★☆☆ Dangereux",
                4 => "★★★★☆ Mortel",
                _ => "★★★★★ Anomalie chronale",
            };
        }

        public static string BiomeLabel(HybrisBiome biome)
        {
            return biome switch
            {
                HybrisBiome.CiteHumaine => "Métropole humaine",
                HybrisBiome.CiteNaine => "Cité naine",
                HybrisBiome.CiteElfique => "Cité elfique",
                HybrisBiome.Montagne => "Montagne",
                HybrisBiome.Foret => "Forêt",
                HybrisBiome.Jungle => "Jungle",
                HybrisBiome.Sanctuaire => "Sanctuaire",
                HybrisBiome.Port => "Port",
                HybrisBiome.Ile => "Île",
                HybrisBiome.TerresInterdites => "Terres Interdites",
                _ => biome.ToString(),
            };
        }
    }
}
using System.Collections.Generic;
using UnityEngine;

namespace Killtime.Story
{
    /// <summary>
    /// Overworld d'Hybris — réseau de secteurs (carte nodale sur fond hexagonal léger).
    /// V1 dev : relie les scènes tactiques fermées pour une campagne complète.
    ///
    /// Sources :
    /// - Vieille carte "Hybris 2035.png" (Killtime/Killtime/img) : 2 continents + île
    ///   centrale (Vagas) + île nord, échelle ~0-300 miles, légende villages/lacs/royaumes.
    /// - Livre X, chap. 41-42 : Continent Ouest (Montagnes Naines, Montagnes de l'Orage,
    ///   Grande Forêt/Haliriel, Terres Centrales/Kingston, Jungle du Sud/Guetteur),
    ///   Continent Est (Terres Interdites, brumes chronales), Mer des Déportés.
    /// - Plan Volume I : Kingston (09), Brum'korath (12), Jungle/Candice (08),
    ///   Haliriel (17), Culte/Disciple (14-15).
    /// </summary>
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
        /// <summary>Position normalisée 0-1 sur la carte (0,0 = haut-gauche). Calquée sur Hybris 2035.png.</summary>
        public Vector2 MapPos;
        public HybrisBiome Biome;
        /// <summary>Danger tactique 0 (sûr) à 5 (anomalie chronale).</summary>
        public int Danger;
        /// <summary>Id de scène tactique suggérée (ex: volume_1_scene_09). Vide = scène fermée à créer.</summary>
        public string LinkedScenarioId;
        /// <summary>Flag de campagne requis pour déverrouiller (ex: expedition_est). Vide = libre.</summary>
        public string RequiredFlag;
        /// <summary>Flag posé à la première visite.</summary>
        public string GrantsFlagOnVisit;
        public bool StartingUnlocked;

        public HybrisSectorNode() { }

        public HybrisSectorNode(string id, string name, string region, string description,
            float x, float y, HybrisBiome biome, int danger,
            string linkedScenarioId = "", string requiredFlag = "",
            string grantsFlagOnVisit = "", bool startingUnlocked = false)
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
        /// <summary>Flag requis pour emprunter ce lien (en plus des verrous de nœuds).</summary>
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

    /// <summary>
    /// Catalogue statique du réseau de secteurs. Traité comme non-dirigé.
    /// Nodes/Links = canon livré. L'éditeur peut appliquer un override runtime
    /// (issu d'un JSON) : le jeu passe alors par ActiveNodes/ActiveLinks.
    /// </summary>
    public static class HybrisWorldMapData
    {
        public const string StartNodeId = "kingston";
        public const string EasternExpeditionFlag = "expedition_est";

        public static readonly List<HybrisSectorNode> Nodes = new()
        {
            new HybrisSectorNode(
                "kingston", "Kingston", "Terres Centrales // Hybris Ouest",
                "Plus grande métropole humaine. Académie (Lucas, Mina — 1771 HSC), QG de la World Police. Carrefour artisanal / arcanotech. Point de départ de la campagne.",
                0.30f, 0.56f, HybrisBiome.CiteHumaine, 1,
                "volume_1_scene_09", "", "visited_kingston", true),

            new HybrisSectorNode(
                "portes_naines", "Portes Naines", "Col // Montagnes Naines",
                "Deux vantaux d'acier noir et nytharite. Verrou stratégique entre Terres Centrales et mines de Brum'korath.",
                0.30f, 0.43f, HybrisBiome.Montagne, 2,
                "volume_1_scene_11", "", "visited_portes_naines", true),

            new HybrisSectorNode(
                "brum_forges", "Forges de Brum'korath", "Montagnes Naines // Hybris Ouest",
                "Mégapole troglodytique : fonderies d'alliages rares, laboratoires Magi-Tech, Conseil Nain.",
                0.27f, 0.30f, HybrisBiome.CiteNaine, 2,
                "volume_1_scene_12", "", "visited_brum_forges", false),

            new HybrisSectorNode(
                "temple_brum", "Temple de Brum'korath", "Profondeurs // Temple des Sent Ones",
                "Sanctuaire des Sent Ones sous la cité naine. Siège du Disciple de Terre (Capricius). Nœud narratif majeur.",
                0.34f, 0.27f, HybrisBiome.Sanctuaire, 3,
                "volume_1_scene_14", "", "visited_temple_brum", false),

            new HybrisSectorNode(
                "mont_orage", "Montagnes de l'Orage", "Crêtes électrostatiques // Nord-Ouest",
                "Crêtes balayées d'arcs permanents. Passage alternatif vers Brum'korath, risqué mais hors des contrôles.",
                0.43f, 0.32f, HybrisBiome.Montagne, 3,
                "", "", "visited_mont_orage", false),

            new HybrisSectorNode(
                "port_mito", "Port de Mito", "Côte Ouest // Mer des Déportés",
                "Port de pêche et de contrebande (légende h. de la vieille carte). Embarquement vers Vagas, le Nord et l'Est.",
                0.19f, 0.47f, HybrisBiome.Port, 1,
                "", "", "visited_port_mito", false),

            new HybrisSectorNode(
                "haliriel", "Haliriel", "Grande Forêt // Hybris Sud",
                "Cité suspendue des Elfes Tauriens. Temple Haut de la Nature, trône de la Reine Aphyrosia, hangars Windfighters.",
                0.24f, 0.70f, HybrisBiome.CiteElfique, 1,
                "volume_1_scene_17", "", "visited_haliriel", false),

            new HybrisSectorNode(
                "forets_primordiales", "Forêts Primordiales", "Cœur de la Grande Forêt",
                "Canopée millénaire gorgée de 5e Force. Faune magique, sanctuaire inviolable elfique. Au-delà d'Haliriel.",
                0.33f, 0.76f, HybrisBiome.Foret, 2,
                "volume_1_scene_10", "", "visited_forets_primordiales", false),

            new HybrisSectorNode(
                "village_nythari", "Village Nythari", "Jungle du Sud",
                "Huttes végétales sur marécages. Magie chamanique terre/plantes. Fief de Candice et Ceylan.",
                0.26f, 0.85f, HybrisBiome.Jungle, 2,
                "volume_1_scene_08", "", "visited_village_nythari", false),

            new HybrisSectorNode(
                "guetteur", "Le Guetteur (The Watcher)", "Profondeurs de la Jungle",
                "Monument cyclopéen pré-colonial. Résonance du Minulican (loup temporel). Gardé par les tribus Nytharis.",
                0.33f, 0.91f, HybrisBiome.Sanctuaire, 4,
                "volume_1_scene_15", "", "visited_guetteur", false),

            new HybrisSectorNode(
                "ile_vagas", "Île de Vagas", "Mer des Déportés // Centre",
                "Petite île boisée de la vieille carte (ae.). Escale obligatoire vers le Continent Est.",
                0.52f, 0.68f, HybrisBiome.Ile, 2,
                "", "", "visited_ile_vagas", false),

            new HybrisSectorNode(
                "rivage_est", "Rivage Est — Tête de pont", "Continent Est // Barrière temporelle",
                "Premier rivage des Terres Interdites. Brumes chronales, vieillissement accéléré, créatures hors-trames. Expédition à préparer.",
                0.70f, 0.55f, HybrisBiome.TerresInterdites, 4,
                "", EasternExpeditionFlag, "visited_rivage_est", false),

            new HybrisSectorNode(
                "profondeurs_est", "Profondeurs Interdites", "Continent Est // Inconnu",
                "Intérieur du Continent Est, jamais cartographié. Aucune expédition de Kingston n'en est revenue intacte.",
                0.79f, 0.66f, HybrisBiome.TerresInterdites, 5,
                "", EasternExpeditionFlag, "visited_profondeurs_est", false),

            new HybrisSectorNode(
                "ile_nord_aurora", "Île Nord Aurora", "Nord // Mer des Déportés",
                "Île forestière du haut de la vieille carte (cf. 1. Aurora). Halte nord, route maritime depuis Mito.",
                0.63f, 0.10f, HybrisBiome.Ile, 2,
                "volume_1_scene_02", "", "visited_ile_nord", false),
        };

        public static readonly List<HybrisSectorLink> Links = new()
        {
            new HybrisSectorLink("kingston", "portes_naines", 45, 2, "Route du col"),
            new HybrisSectorLink("portes_naines", "brum_forges", 30, 2, "Pas des Nains"),
            new HybrisSectorLink("brum_forges", "temple_brum", 12, 1, "Galeries du Temple"),
            new HybrisSectorLink("temple_brum", "mont_orage", 40, 3, "Crêtes électrostatiques"),
            new HybrisSectorLink("portes_naines", "mont_orage", 55, 3, "Sentier des orages"),
            new HybrisSectorLink("kingston", "mont_orage", 70, 3, "Plaine centrale"),
            new HybrisSectorLink("kingston", "haliriel", 80, 4, "Route de la Canopée"),
            new HybrisSectorLink("haliriel", "forets_primordiales", 25, 2, "Cœur vert"),
            new HybrisSectorLink("forets_primordiales", "village_nythari", 60, 3, "Descente marécageuse"),
            new HybrisSectorLink("village_nythari", "guetteur", 20, 1, "Sentier Nythari"),
            new HybrisSectorLink("kingston", "port_mito", 50, 2, "Route de l'Ouest"),
            new HybrisSectorLink("port_mito", "ile_vagas", 120, 5, "Traversée — Mer des Déportés", true),
            new HybrisSectorLink("ile_vagas", "rivage_est", 110, 4, "Traversée Est", true, EasternExpeditionFlag),
            new HybrisSectorLink("rivage_est", "profondeurs_est", 70, 4, "Brumes chronales", false, EasternExpeditionFlag),
            new HybrisSectorLink("port_mito", "ile_nord_aurora", 150, 6, "Route du Nord", true),
            new HybrisSectorLink("haliriel", "ile_vagas", 140, 6, "Cabottage sud", true),
        };

        public static HybrisSectorNode Find(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            var nodes = ActiveNodes;
            for (int i = 0; i < nodes.Count; i++)
                if (nodes[i] != null && nodes[i].Id == id) return nodes[i];
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
                if ((l.FromId == a && l.ToId == b) || (l.FromId == b && l.ToId == a)) return l;
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

        // ================= OVERRIDE RUNTIME (ÉDITEUR) =================

        private static List<HybrisSectorNode> _runtimeNodes;
        private static List<HybrisSectorLink> _runtimeLinks;

        /// <summary>Nom de la carte active ("canon" si aucune surcharge).</summary>
        public static string ActiveMapName { get; private set; } = "canon";

        /// <summary>Incrémenté à chaque application/effacement (repère de synchro VTT).</summary>
        public static int ActiveRevision { get; private set; }

        public static bool IsCustomized => _runtimeNodes != null || _runtimeLinks != null;

        public static List<HybrisSectorNode> ActiveNodes => _runtimeNodes ?? Nodes;
        public static List<HybrisSectorLink> ActiveLinks => _runtimeLinks ?? Links;

        /// <summary>Applique une carte éditée au jeu actif (clone défensif).</summary>
        public static void ApplyRuntimeData(HybrisWorldMapSaveData data)
        {
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
                    n.LinkedScenarioId, n.RequiredFlag, n.GrantsFlagOnVisit, n.StartingUnlocked));
            }
            _runtimeLinks = new List<HybrisSectorLink>(data.Links.Count);
            foreach (var l in data.Links)
            {
                if (l == null) continue;
                _runtimeLinks.Add(new HybrisSectorLink(
                    l.FromId, l.ToId, l.Miles, l.Days, l.Label, l.IsSeaCrossing, l.RequiresFlag));
            }
            ActiveMapName = string.IsNullOrWhiteSpace(data.MapName) ? "custom" : data.MapName;
            ActiveRevision++;
        }

        public static void ClearRuntimeData()
        {
            _runtimeNodes = null;
            _runtimeLinks = null;
            ActiveMapName = "canon";
            ActiveRevision++;
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

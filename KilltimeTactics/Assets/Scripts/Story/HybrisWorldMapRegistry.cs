using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics.Grid;

namespace Killtime.Core.World
{
    public enum LandmarkCategory
    {
        MajorKingdom = 0,
        Hydrology = 1,
        SettlementOrPort = 2,
        MilitaryCamp = 3,
        TitanLair = 4,
        CentralHub = 5
    }

    [Serializable]
    public struct HybrisLocationData
    {
        public string Code;
        public string CanonicalName;
        public LandmarkCategory Category;
        public HexCoordinates AxialCoords;
        public Vector2 NormalizedPos;
        public string DominantSpecies;
        public string LoreNotes;

        public HybrisLocationData(string code, string name, LandmarkCategory category, HexCoordinates coords, Vector2 normPos, string species, string lore)
        {
            Code = code;
            CanonicalName = name;
            Category = category;
            AxialCoords = coords;
            NormalizedPos = normPos;
            DominantSpecies = species;
            LoreNotes = lore;
        }
    }

    /// <summary>
    /// Registre officiel exhaustif de la géographie planétaire d'Hybris (Livres I à XI).
    /// Correspondance absolue avec la carte originale manuscrite (Royaumes 1-8, Hydrologie A-H, Établissements b-ah).
    /// </summary>
    public static class HybrisWorldMapRegistry
    {
        public static readonly Dictionary<string, HybrisLocationData> Locations = new(StringComparer.OrdinalIgnoreCase)
        {
            // --- I. PÔLE CENTRAL & TITAN ---
            { "kingston", new HybrisLocationData("kingston", "Kingston", LandmarkCategory.CentralHub, new HexCoordinates(-6, 3), new Vector2(0.30f, 0.56f), "Humain", "Capitale des Terres Centrales, siège de la World Police et point de départ.") },
            { "Haliriel", new HybrisLocationData("Haliriel", "Falaises d'Haliriel", LandmarkCategory.TitanLair, new HexCoordinates(-9, 7), new Vector2(0.24f, 0.70f), "Faune Tellurique Légendaire", "Marqueur rouge austral : cratère et repaire de la Bête d'Hybris (Livre XI).") },

            // --- II. ROYAUMES MAJEURS (1 à 8) ---
            { "1", new HybrisLocationData("1", "Aurora", LandmarkCategory.MajorKingdom, new HexCoordinates(5, -9), new Vector2(0.63f, 0.10f), "Humain", "Cité des pionniers et du savoir temporel, péninsule boréale.") },
            { "2", new HybrisLocationData("2", "Royaume d'Atika", LandmarkCategory.MajorKingdom, new HexCoordinates(6, 3), new Vector2(0.68f, 0.60f), "Humain", "Royaume côtier bordant les sables arides occidentaux d'Hybris Est.") },
            { "3", new HybrisLocationData("3", "Cité d'Obelia", LandmarkCategory.MajorKingdom, new HexCoordinates(8, -1), new Vector2(0.74f, 0.48f), "Cleien", "Foyer académique et sanctuaire d'études des flux arcanotech.") },
            { "4", new HybrisLocationData("4", "Marscua", LandmarkCategory.MajorKingdom, new HexCoordinates(-7, -4), new Vector2(0.28f, 0.35f), "Nain", "Cité fortifiée érigée au confluent des bassins miniers d'Hybris Ouest.") },
            { "5", new HybrisLocationData("5", "Brot", LandmarkCategory.MajorKingdom, new HexCoordinates(-10, -5), new Vector2(0.20f, 0.36f), "Nain", "Bastion troglodytique de pierre et de machineries thermiques.") },
            { "6", new HybrisLocationData("6", "Royaume de Lepla", LandmarkCategory.MajorKingdom, new HexCoordinates(11, -5), new Vector2(0.83f, 0.38f), "Taurien", "Fief forestier oriental régi par les pactes sylvestres.") },
            { "7", new HybrisLocationData("7", "Royaume de Prat", LandmarkCategory.MajorKingdom, new HexCoordinates(10, 8), new Vector2(0.81f, 0.76f), "Vardien", "Marches militarisées gardant les frontières du sud.") },
            { "8", new HybrisLocationData("8", "Akalemna", LandmarkCategory.MajorKingdom, new HexCoordinates(-11, 0), new Vector2(0.16f, 0.50f), "Mikyai", "Capitale marchande du détroit occidental aux comptoirs fortifiés.") },

            // --- III. HYDROGRAPHIE PRINCIPALE (A à H) ---
            { "A", new HybrisLocationData("A", "Lac Géant", LandmarkCategory.Hydrology, new HexCoordinates(-8, -1), new Vector2(0.26f, 0.47f), "Faune Aquatique", "Mer intérieure tectonique d'Hybris Ouest.") },
            { "B", new HybrisLocationData("B", "Mer de Césil", LandmarkCategory.Hydrology, new HexCoordinates(7, 0), new Vector2(0.71f, 0.50f), "Neutre", "Bassin navigable intérieur d'Hybris Est.") },
            { "C", new HybrisLocationData("C", "Lac Majestueux", LandmarkCategory.Hydrology, new HexCoordinates(9, -3), new Vector2(0.78f, 0.42f), "Neutre", "Plan d'eau royal ceinturé par la canopée taurienne.") },
            { "D", new HybrisLocationData("D", "Lac Rondinoi", LandmarkCategory.Hydrology, new HexCoordinates(-3, -4), new Vector2(0.42f, 0.37f), "Neutre", "Réservoir d'altitude alimentant les chutes de l'Ouest.") },
            { "E", new HybrisLocationData("E", "Lac Royal de Lassot", LandmarkCategory.Hydrology, new HexCoordinates(9, 4), new Vector2(0.77f, 0.63f), "Neutre", "Bassin de plaisance et de pêche des souverains orientaux.") },
            { "F", new HybrisLocationData("F", "Lac Rond", LandmarkCategory.Hydrology, new HexCoordinates(2, -10), new Vector2(0.56f, 0.08f), "Faune Boréale", "Étendue d'eau froide du littoral septentrional.") },
            { "G", new HybrisLocationData("G", "Réservoir Itimeï", LandmarkCategory.Hydrology, new HexCoordinates(-4, -6), new Vector2(0.38f, 0.32f), "Inorganique", "Retenue artificielle arcanotech régulant l'irrigation.") },
            { "H", new HybrisLocationData("H", "Lac Royal de Vatima", LandmarkCategory.Hydrology, new HexCoordinates(8, 7), new Vector2(0.73f, 0.73f), "Neutre", "Réservoir méridional au pied des falaises de Vardis.") },

            // --- IV. ÉTABLISSEMENTS, PORTS & CAMPS (b à ah) ---
            { "b", new HybrisLocationData("b", "Aramea", LandmarkCategory.SettlementOrPort, new HexCoordinates(-12, -7), new Vector2(0.14f, 0.31f), "Humain", "Comptoir côtier du nord-ouest.") },
            { "c", new HybrisLocationData("c", "Kitiari", LandmarkCategory.SettlementOrPort, new HexCoordinates(-10, -9), new Vector2(0.20f, 0.25f), "Nain", "Avant-poste minier des crêtes septentrionales.") },
            { "d", new HybrisLocationData("d", "Otuma", LandmarkCategory.SettlementOrPort, new HexCoordinates(-7, -8), new Vector2(0.27f, 0.28f), "Nain", "Forge d'extraction haute.") },
            { "e", new HybrisLocationData("e", "Liegil", LandmarkCategory.SettlementOrPort, new HexCoordinates(-3, -7), new Vector2(0.40f, 0.31f), "Mixte", "Colonie forestière des contreforts.") },
            { "f", new HybrisLocationData("f", "Camp d'Atola", LandmarkCategory.MilitaryCamp, new HexCoordinates(-5, -2), new Vector2(0.35f, 0.44f), "World Police", "Camp de surveillance militaire des Terres Centrales.") },
            { "g", new HybrisLocationData("g", "Camp d'Emilia", LandmarkCategory.MilitaryCamp, new HexCoordinates(-4, 0), new Vector2(0.39f, 0.50f), "World Police", "Bastion d'observation stratégique du détroit central.") },
            { "h", new HybrisLocationData("h", "Port de Mito", LandmarkCategory.SettlementOrPort, new HexCoordinates(-10, -1), new Vector2(0.19f, 0.47f), "Humain", "Grand port d'embarquement vers Vagas et le large.") },
            { "i", new HybrisLocationData("i", "Riveria", LandmarkCategory.SettlementOrPort, new HexCoordinates(-8, 2), new Vector2(0.25f, 0.55f), "Humain", "Cité fluviale bordant les plaines agricoles.") },
            { "j", new HybrisLocationData("j", "Hamsi", LandmarkCategory.SettlementOrPort, new HexCoordinates(-11, 4), new Vector2(0.16f, 0.62f), "Cleien", "Port de pêche lagunaire.") },
            { "k", new HybrisLocationData("k", "Étolé", LandmarkCategory.SettlementOrPort, new HexCoordinates(-9, 5), new Vector2(0.21f, 0.65f), "Humain", "Cité frontière de la clairière sud.") },
            { "l", new HybrisLocationData("l", "Kuart-Kavé", LandmarkCategory.SettlementOrPort, new HexCoordinates(-11, 6), new Vector2(0.15f, 0.69f), "Vardien", "Place forte rocheuse côtière.") },
            { "m", new HybrisLocationData("m", "Outaï", LandmarkCategory.SettlementOrPort, new HexCoordinates(-10, 8), new Vector2(0.18f, 0.74f), "Mixte", "Village côtier ceinturé de digues.") },
            { "n", new HybrisLocationData("n", "Muria", LandmarkCategory.SettlementOrPort, new HexCoordinates(-8, 9), new Vector2(0.23f, 0.78f), "Taurien", "Campement nomade taurien de lisière.") },
            { "o", new HybrisLocationData("o", "Melke", LandmarkCategory.SettlementOrPort, new HexCoordinates(-6, 8), new Vector2(0.30f, 0.76f), "Humain", "Station relais des caravanes australes.") },
            { "p", new HybrisLocationData("p", "Romoda", LandmarkCategory.SettlementOrPort, new HexCoordinates(-5, 6), new Vector2(0.33f, 0.70f), "Humain", "Bourg commerçant du Grand Fleuve.") },
            { "q", new HybrisLocationData("q", "Duvna", LandmarkCategory.SettlementOrPort, new HexCoordinates(-2, 4), new Vector2(0.43f, 0.64f), "Mixte", "Porte orientale d'Hybris Ouest face au détroit.") },
            { "r", new HybrisLocationData("r", "Shumai", LandmarkCategory.SettlementOrPort, new HexCoordinates(-1, 2), new Vector2(0.46f, 0.58f), "Humain", "Comptoir maritime d'observation.") },
            { "s", new HybrisLocationData("s", "Grejka", LandmarkCategory.SettlementOrPort, new HexCoordinates(5, -6), new Vector2(0.66f, 0.33f), "Mixte", "Colonie septentrionale du continent Est.") },
            { "t", new HybrisLocationData("t", "Port d'Anestar", LandmarkCategory.SettlementOrPort, new HexCoordinates(6, -4), new Vector2(0.68f, 0.40f), "Humain", "Grand port d'entrée d'Hybris Est.") },
            { "u", new HybrisLocationData("u", "Kalici", LandmarkCategory.SettlementOrPort, new HexCoordinates(8, -5), new Vector2(0.75f, 0.36f), "Cleien", "Cité universitaire des falaises.") },
            { "v", new HybrisLocationData("v", "Brogre", LandmarkCategory.SettlementOrPort, new HexCoordinates(10, -7), new Vector2(0.81f, 0.30f), "Nain", "Enclave minière de l'Est.") },
            { "w", new HybrisLocationData("w", "Geth-ekor", LandmarkCategory.SettlementOrPort, new HexCoordinates(12, -4), new Vector2(0.87f, 0.41f), "Taurien", "Bastion de guerre sylvestre.") },
            { "x", new HybrisLocationData("x", "Dermantil", LandmarkCategory.SettlementOrPort, new HexCoordinates(10, -1), new Vector2(0.82f, 0.50f), "Humain", "Cité marchande du grand plateau.") },
            { "y", new HybrisLocationData("y", "Port de Magisma", LandmarkCategory.SettlementOrPort, new HexCoordinates(11, 2), new Vector2(0.84f, 0.59f), "Cleien", "Port arcanotech d'Hybris Est.") },
            { "z", new HybrisLocationData("z", "Port d'Orof-garen", LandmarkCategory.SettlementOrPort, new HexCoordinates(9, 5), new Vector2(0.79f, 0.67f), "Vardien", "Cité navale et chantiers de guerre.") },
            { "aa", new HybrisLocationData("aa", "Marte", LandmarkCategory.SettlementOrPort, new HexCoordinates(7, 9), new Vector2(0.72f, 0.80f), "Humain", "Oasis fortifiée bordant le désert sud.") },
            { "ab", new HybrisLocationData("ab", "Vilousi", LandmarkCategory.SettlementOrPort, new HexCoordinates(5, 7), new Vector2(0.66f, 0.74f), "Humain", "Comptoir côtier des sables d'Atika.") },
            { "ac", new HybrisLocationData("ac", "Tredmor", LandmarkCategory.SettlementOrPort, new HexCoordinates(4, 5), new Vector2(0.63f, 0.67f), "Vardien", "Garnison de surveillance des côtes occidentales de l'Est.") },
            { "ad", new HybrisLocationData("ad", "Trestoï", LandmarkCategory.SettlementOrPort, new HexCoordinates(5, 2), new Vector2(0.65f, 0.57f), "Humain", "Cité côtière face à la Mer des Déportés.") },
            { "ae", new HybrisLocationData("ae", "Île de Vagas", LandmarkCategory.SettlementOrPort, new HexCoordinates(1, 5), new Vector2(0.52f, 0.68f), "Neutre", "Île forteresse pivot contrôlant la passe entre les deux continents.") },
            { "af", new HybrisLocationData("af", "Nostra", LandmarkCategory.SettlementOrPort, new HexCoordinates(-1, 6), new Vector2(0.46f, 0.72f), "Neutre", "Comptoir de troc sur l'archipel central.") },
            { "ag", new HybrisLocationData("ag", "Damaroa", LandmarkCategory.SettlementOrPort, new HexCoordinates(0, 8), new Vector2(0.50f, 0.78f), "Faune Sylvestre", "Ancien sanctuaire insulaire.") },
            { "ah", new HybrisLocationData("ah", "Tritoa", LandmarkCategory.SettlementOrPort, new HexCoordinates(2, 7), new Vector2(0.55f, 0.75f), "Neutre", "Crique refuge des corsaires temporels.") }
        };

        public static bool TryGetLocation(string code, out HybrisLocationData location)
        {
            return Locations.TryGetValue(code, out location);
        }
    }
}
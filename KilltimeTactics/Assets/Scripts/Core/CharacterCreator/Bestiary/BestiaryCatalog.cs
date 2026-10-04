using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Killtime.Core.Inventory;
using Killtime.Tactics.Grid;

namespace Killtime.Core.Character
{
    /// <summary>
    /// Bestiaire Killtime Tactics — source unique JSON éditable (Livre XI).
    /// Fichier : Assets/Resources/Data/Bestiary.json ("Data/Bestiary" en Resources).
    /// Éditable à la main (UTF-8) puis rechargé en jeu via la fenêtre Bestiaire (touche B).
    /// Au runtime, un override JSON dans persistentDataPath/Bestiary.json (sauvegardé
    /// depuis la fenêtre) prend le pas sur le Resources embarqué.
    /// Format volontairement lisible : espèces/profils/skills en NOMS (pas d'ids
    /// numériques), équipements en NOMS du catalogue d'armurerie (Livre VIII).
    /// </summary>

    [Serializable]
    public class BestiaryAttributes
    {
        public int FOR = 2;
        public int AGI = 2;
        public int CON = 2;
        public int RAP = 2;
        public int INT = 2;
        public int ERU = 1;
        public int CHA = 1;
        public int INS = 1;
        public int MAG = 0;
        public int Vision = 3;
        public int Ouie = 3;
        public int Miracle = 0;
    }

    [Serializable]
    public class BestiarySkillEntry
    {
        public string skill = "MainsNues";
        public int level = 0;
    }

    [Serializable]
    public class BestiaryEntry
    {
        public string id = "nouveau";
        public string name = "Nouvelle Créature";
        public string category = "Minion"; // Minion, Base, Civil, Bandit, Elite, Faune, Automate, Boss, Build
        public string rank = "Rang I";     // Rang I, Rang II, Rang III
        public string profile = "PnjSbire"; // PnjSbire, PnjNormal, PnjBoss (+HerosPJ accepté)
        public string species = "Humain";  // Humain, Nain, Taurien, Cleien, Mikyai, Vardien
        public string footprint = "Single"; // Single, Triangle3, Rosette7, Colossus19
        public string modelPrefab = "";
        public int age = 30;
        public string gender = "Indéterminé";
        public string description = "";
        public string tactics = "";
        public BestiaryAttributes attributes = new BestiaryAttributes();
        public int armor = 0;
        public int credits = 0;
        public List<BestiarySkillEntry> skills = new List<BestiarySkillEntry>();
        public List<string> specializations = new List<string>();
        public List<string> equipment = new List<string>();
        public int equippedIndex = -1; // -1 = rien d'équipé (poings / griffes / crocs)
    }

    [Serializable]
    public class BestiaryData
    {
        public int version = 1;
        public string source = "";
        public List<BestiaryEntry> entries = new List<BestiaryEntry>();
    }

    /// <summary>
    /// Spécialisations des PNJ & Automates militaires (Livre XI §44 & §47 - RD-088).
    /// </summary>
    public static class LivreXISpecializations
    {
        public const string TirPrepareInterruption = "Tir Préparé en Interruption";
        public const string PriseOtage = "Prise d'Otage";
        public const string CombustionSpontanee = "Combustion Spontanée";
        public const string FouleeCendres = "Foulée de Cendres";
        public const string MorsureHydraulique = "Morsure Hydraulique";
        public const string FlashAveuglant = "Flash Aveuglant";
        public const string BaliseAppelReseau = "Balise d'Appel Réseau";
    }

    /// <summary>
    /// Registre d'états tactiques pour les manœuvres avancées PNJ & Automates (Livre XI - RD-088).
    /// </summary>
    public static class LivreXIPNJState
    {
        private static readonly Dictionary<CharacterStats, CharacterStats> _hostages = new();
        private static readonly Dictionary<CharacterStats, CharacterStats> _hydraulicBites = new();
        private static readonly HashSet<CharacterStats> _preparedSnipers = new();
        private static readonly HashSet<CharacterStats> _activatedBeacons = new();

        public static void RegisterHostage(CharacterStats captor, CharacterStats hostage)
        {
            if (captor == null || hostage == null) return;
            _hostages[captor] = hostage;
        }

        public static bool IsHostage(CharacterStats stats)
        {
            if (stats == null) return false;
            foreach (var kv in _hostages)
            {
                if (kv.Value == stats) return true;
            }
            return false;
        }

        public static CharacterStats GetHostageOf(CharacterStats captor)
        {
            return (captor != null && _hostages.TryGetValue(captor, out var h)) ? h : null;
        }

        public static void ReleaseHostage(CharacterStats captor)
        {
            if (captor != null) _hostages.Remove(captor);
        }

        public static void RegisterHydraulicBite(CharacterStats dog, CharacterStats victim)
        {
            if (dog == null || victim == null) return;
            _hydraulicBites[dog] = victim;
        }

        public static bool IsBittenByHydraulicJaw(CharacterStats victim)
        {
            if (victim == null) return false;
            foreach (var kv in _hydraulicBites)
            {
                if (kv.Value == victim) return true;
            }
            return false;
        }

        public static CharacterStats GetVictimOf(CharacterStats dog)
        {
            return (dog != null && _hydraulicBites.TryGetValue(dog, out var v)) ? v : null;
        }

        public static void ReleaseHydraulicBite(CharacterStats dog)
        {
            if (dog != null) _hydraulicBites.Remove(dog);
        }

        public static void RegisterPreparedInterruption(CharacterStats sniper)
        {
            if (sniper != null) _preparedSnipers.Add(sniper);
        }

        public static bool HasPreparedInterruption(CharacterStats sniper)
        {
            return sniper != null && _preparedSnipers.Contains(sniper);
        }

        public static void ClearPreparedInterruption(CharacterStats sniper)
        {
            if (sniper != null) _preparedSnipers.Remove(sniper);
        }

        public static bool HasBeaconTriggered(CharacterStats drone)
        {
            return drone != null && _activatedBeacons.Contains(drone);
        }

        public static void SetBeaconTriggered(CharacterStats drone)
        {
            if (drone != null) _activatedBeacons.Add(drone);
        }

        public static void ClearCombatState()
        {
            _hostages.Clear();
            _hydraulicBites.Clear();
            _preparedSnipers.Clear();
            _activatedBeacons.Clear();
        }
    }

    /// <summary>
    /// Chargeur + convertisseur du bestiaire JSON vers CharacterSheet / CharacterStats.
    /// Pur C# testable (seul Resources.Load / persistentDataPath touchent à Unity).
    /// </summary>
    public static class BestiaryCatalog
    {
        public const string ResourcesPath = "Data/Bestiary";
        public const string OverrideFileName = "Bestiary.json";

        private static List<BestiaryEntry> _cached;
        private static string _loadedFrom = "";

        public static string LoadedFrom => _loadedFrom;
        public static int Count => GetAll().Count;

        public static void InvalidateCache() { _cached = null; _loadedFrom = ""; }

        /// <summary>Priorité : override disque (éditions en jeu) > Resources embarqué.</summary>
        public static List<BestiaryEntry> GetAll()
        {
            if (_cached != null) return _cached;
            _cached = new List<BestiaryEntry>();

            // 1. Override édité en jeu (persistentDataPath).
            try
            {
                string overridePath = Path.Combine(Application.persistentDataPath, OverrideFileName);
                if (File.Exists(overridePath))
                {
                    string json = File.ReadAllText(overridePath);
                    var data = JsonUtility.FromJson<BestiaryData>(json);
                    if (data != null && data.entries != null && data.entries.Count > 0)
                    {
                        _cached = data.entries;
                        _loadedFrom = "override:" + overridePath;
                        return _cached;
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning($"[Bestiary] Override illisible : {e.Message}"); }

            // 2. Resources embarqué (édition à la main dans Assets/Resources/Data/Bestiary.json).
            try
            {
                var asset = Resources.Load<TextAsset>(ResourcesPath);
                if (asset != null)
                {
                    var data = JsonUtility.FromJson<BestiaryData>(asset.text);
                    if (data != null && data.entries != null)
                    {
                        _cached = data.entries;
                        EnsureLivreXIArchetypes(_cached);
                        _loadedFrom = "resources:" + ResourcesPath;
                        return _cached;
                    }
                }
                Debug.LogWarning("[Bestiary] Aucun TextAsset 'Data/Bestiary' trouvé dans Resources.");
            }
            catch (Exception e) { Debug.LogWarning($"[Bestiary] Resources illisible : {e.Message}"); }

            EnsureLivreXIArchetypes(_cached);
            _loadedFrom = "defaults:livreXI";
            return _cached;
        }

        public static void EnsureLivreXIArchetypes(List<BestiaryEntry> list)
        {
            if (list == null) return;

            if (!list.Exists(x => x != null && (x.id == "bandit_3" || (x.name != null && x.name.IndexOf("Bandit #3", StringComparison.OrdinalIgnoreCase) >= 0))))
            {
                list.Add(new BestiaryEntry
                {
                    id = "bandit_3",
                    name = "Bandit #3 (Tireur d'Élite)",
                    category = "Bandit",
                    rank = "Rang II",
                    profile = "PnjNormal",
                    species = "Humain",
                    footprint = "Single",
                    description = "Tireur embusqué cruel et calculateur. Maître du tir préparé en interruption et de la prise d'otage.",
                    tactics = "Réserve 4 PA pour un tir réflexe dès rupture de couvert adverse ; prend en otage toute cible adjacente à terre ou essoufflée.",
                    attributes = new BestiaryAttributes { FOR = 2, AGI = 4, CON = 3, RAP = 4, INT = 3, ERU = 2, CHA = 2, INS = 3, MAG = 0, Vision = 4, Ouie = 4, Miracle = 0 },
                    armor = 2,
                    skills = new List<BestiarySkillEntry>
                    {
                        new BestiarySkillEntry { skill = "Ballistique", level = 2 },
                        new BestiarySkillEntry { skill = "Esquive", level = 1 },
                        new BestiarySkillEntry { skill = "Intuition", level = 1 }
                    },
                    specializations = new List<string> { LivreXISpecializations.TirPrepareInterruption, LivreXISpecializations.PriseOtage },
                    equipment = new List<string> { "Fusil de Précision", "Couteau de Combat" },
                    equippedIndex = 0
                });
            }

            if (!list.Exists(x => x != null && (x.id == "illumo_pyro" || (x.name != null && x.name.IndexOf("Illumo", StringComparison.OrdinalIgnoreCase) >= 0))))
            {
                list.Add(new BestiaryEntry
                {
                    id = "illumo_pyro",
                    name = "Illumo (Adepte Pyromancien)",
                    category = "Elite",
                    rank = "Rang II",
                    profile = "PnjNormal",
                    species = "Humain",
                    footprint = "Single",
                    description = "Fanatique exalté canalisant l'arcanotech du feu. Ignore les trajectoires balistiques pour consumer la cible de l'intérieur.",
                    tactics = "Combustion spontanée à 18m sans projectile, puis foulée de cendres pour s'évanouir en aveuglant les poursuivants.",
                    attributes = new BestiaryAttributes { FOR = 2, AGI = 3, CON = 3, RAP = 3, INT = 4, ERU = 2, CHA = 3, INS = 3, MAG = 4, Vision = 3, Ouie = 3, Miracle = 0 },
                    armor = 1,
                    skills = new List<BestiarySkillEntry>
                    {
                        new BestiarySkillEntry { skill = "MainsNues", level = 2 },
                        new BestiarySkillEntry { skill = "Esquive", level = 2 },
                        new BestiarySkillEntry { skill = "Arcanes", level = 2 }
                    },
                    specializations = new List<string> { LivreXISpecializations.CombustionSpontanee, LivreXISpecializations.FouleeCendres },
                    equipment = new List<string> { "Dague Sacrificielle" },
                    equippedIndex = 0
                });
            }

            if (!list.Exists(x => x != null && (x.id == "molosse_combat" || (x.name != null && x.name.IndexOf("Molosse", StringComparison.OrdinalIgnoreCase) >= 0))))
            {
                list.Add(new BestiaryEntry
                {
                    id = "molosse_combat",
                    name = "Molosse Mécanisé",
                    category = "Automate",
                    rank = "Rang I",
                    profile = "PnjSbire",
                    species = "Humain",
                    footprint = "Single",
                    description = "Quadrupède cybernétique militaire équipé d'une mâchoire à serrage hydraulique verrouillable.",
                    tactics = "Fonce au contact, verrouille sa mâchoire pour clouer la cible au sol et broyer les os à chaque tour.",
                    attributes = new BestiaryAttributes { FOR = 4, AGI = 3, CON = 4, RAP = 4, INT = 1, ERU = 1, CHA = 1, INS = 3, MAG = 0, Vision = 4, Ouie = 4, Miracle = 0 },
                    armor = 3,
                    skills = new List<BestiarySkillEntry>
                    {
                        new BestiarySkillEntry { skill = "MainsNues", level = 2 },
                        new BestiarySkillEntry { skill = "Athletisme", level = 2 }
                    },
                    specializations = new List<string> { LivreXISpecializations.MorsureHydraulique },
                    equipment = new List<string>(),
                    equippedIndex = -1
                });
            }

            if (!list.Exists(x => x != null && (x.id == "drone_reco" || (x.name != null && x.name.IndexOf("Drone", StringComparison.OrdinalIgnoreCase) >= 0))))
            {
                list.Add(new BestiaryEntry
                {
                    id = "drone_reco",
                    name = "Drone de Reconnaissance",
                    category = "Automate",
                    rank = "Rang I",
                    profile = "PnjSbire",
                    species = "Humain",
                    footprint = "Single",
                    description = "Unité aéroportée autonome équipée d'un projecteur stroboscopique aveuglant et d'un relais de liaison réseau.",
                    tactics = "Aveugle les tireurs adverses à courte distance et transmet les données de tir à toute l'escouade via sa balise.",
                    attributes = new BestiaryAttributes { FOR = 1, AGI = 4, CON = 2, RAP = 5, INT = 3, ERU = 1, CHA = 1, INS = 3, MAG = 0, Vision = 5, Ouie = 3, Miracle = 0 },
                    armor = 1,
                    skills = new List<BestiarySkillEntry>
                    {
                        new BestiarySkillEntry { skill = "Esquive", level = 2 },
                        new BestiarySkillEntry { skill = "Observation", level = 2 }
                    },
                    specializations = new List<string> { LivreXISpecializations.FlashAveuglant, LivreXISpecializations.BaliseAppelReseau },
                    equipment = new List<string>(),
                    equippedIndex = -1
                });
            }
        }

        public static BestiaryEntry GetById(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            var all = GetAll();
            for (int i = 0; i < all.Count; i++)
                if (all[i] != null && string.Equals(all[i].id, id.Trim(), StringComparison.OrdinalIgnoreCase))
                    return all[i];
            return null;
        }

        public static List<BestiaryEntry> GetByCategory(string category)
        {
            var all = GetAll();
            var list = new List<BestiaryEntry>();
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] == null) continue;
                if (string.IsNullOrWhiteSpace(category) || category == "Toutes"
                    || string.Equals(all[i].category, category, StringComparison.OrdinalIgnoreCase))
                    list.Add(all[i]);
            }
            return list;
        }

        // ------------------------------------------------------------------
        // CONVERSION → CharacterSheet (moteur de règles)
        // ------------------------------------------------------------------

        public static CharacterProfileType ParseProfile(string s)
        {
            if (Enum.TryParse(s, true, out CharacterProfileType p)) return p;
            return CharacterProfileType.PnjNormal;
        }

        public static SpeciesType ParseSpecies(string s)
        {
            if (Enum.TryParse(s, true, out SpeciesType sp)) return sp;
            return SpeciesType.Humain;
        }

        public static TitanFootprintType ParseFootprint(string s)
        {
            if (Enum.TryParse(s, true, out TitanFootprintType f)) return f;
            return TitanFootprintType.Single;
        }

        public static bool TryParseSkill(string s, out SkillType skill)
        {
            skill = SkillType.MainsNues;
            if (string.IsNullOrWhiteSpace(s)) return false;
            string t = s.Trim();
            // 1. Nom d'enum direct (MainsNues, ManiementArmes, Ballistique...).
            if (Enum.TryParse(t, true, out SkillType direct))
            {
                skill = SkillDefinitions.ResolveBaseSkill(direct);
                return true;
            }
            // 2. Nom d'affichage ("Maniement d'Arme", "Ballistique / Tir"...).
            if (SkillDefinitions.TryParseDisplayName(t, out SkillType disp))
            {
                skill = disp;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Convertit une entrée JSON éditable en fiche moteur complète (skills,
        /// spécialisations, équipement catalogue avec arme équipée).
        /// </summary>
        public static CharacterSheet ToCharacterSheet(BestiaryEntry e)
        {
            if (e == null) return null;
            var a = e.attributes ?? new BestiaryAttributes();

            var attrs = new Attributes(
                @for: Mathf.Clamp(a.FOR, 1, 10),
                agi: Mathf.Clamp(a.AGI, 1, 10),
                con: Mathf.Clamp(a.CON, 1, 10),
                rap: Mathf.Clamp(a.RAP, 1, 10),
                @int: Mathf.Clamp(a.INT, 1, 10),
                eru: Mathf.Clamp(a.ERU, 1, 10),
                cha: Mathf.Clamp(a.CHA, 1, 10),
                ins: Mathf.Clamp(a.INS, 1, 10),
                mag: Mathf.Clamp(a.MAG, 0, 10),
                vision: Mathf.Clamp(a.Vision, 1, 6),
                ouie: Mathf.Clamp(a.Ouie, 1, 6),
                miracle: Mathf.Max(0, a.Miracle)
            );

            var sheet = new CharacterSheet
            {
                Name = string.IsNullOrWhiteSpace(e.name) ? e.id : e.name,
                Age = Mathf.Max(0, e.age),
                Gender = string.IsNullOrWhiteSpace(e.gender) ? "Indéterminé" : e.gender,
                Species = ParseSpecies(e.species),
                Profile = ParseProfile(e.profile),
                Footprint = ParseFootprint(e.footprint),
                ModelPrefabName = e.modelPrefab ?? "",
                LoreNotes = $"[{e.category} · {e.rank}] {e.description}",
                BaseAttributes = attrs,
                BaseArmor = Mathf.Max(0, e.armor),
                CreditsCE = Mathf.Max(0, e.credits),
            };

            sheet.InitializeDefaultSkills();
            if (e.skills != null)
            {
                for (int i = 0; i < e.skills.Count; i++)
                {
                    var se = e.skills[i];
                    if (se == null) continue;
                    if (TryParseSkill(se.skill, out SkillType st))
                        sheet.GetSkill(st).TrainingLevel = Mathf.Clamp(se.level, 0, 3);
                    else
                        Debug.LogWarning($"[Bestiary:{e.id}] Compétence inconnue '{se.skill}' ignorée.");
                }
            }

            sheet.UnlockedSpecializations = new List<string>();
            if (e.specializations != null)
            {
                for (int i = 0; i < e.specializations.Count; i++)
                {
                    string spec = e.specializations[i];
                    if (string.IsNullOrWhiteSpace(spec)) continue;
                    if (!sheet.UnlockedSpecializations.Contains(spec))
                        sheet.UnlockedSpecializations.Add(spec);
                }
            }

            sheet.Inventory = new List<InventoryItem>();
            if (e.equipment != null)
            {
                for (int i = 0; i < e.equipment.Count; i++)
                {
                    string itemName = e.equipment[i];
                    if (string.IsNullOrWhiteSpace(itemName)) continue;
                    var def = ArmoryCatalog.GetByName(itemName);
                    if (def == null)
                    {
                        Debug.LogWarning($"[Bestiary:{e.id}] Équipement '{itemName}' introuvable au catalogue — ignoré.");
                        continue;
                    }
                    var copy = def.Clone();
                    copy.IsEquipped = (i == e.equippedIndex && copy.Type == ItemType.Weapon);
                    sheet.AddItem(copy);
                }
            }

            return sheet;
        }

        /// <summary>Fiche → stats de combat prêtes à spawner (PA / encaissement recalculés).</summary>
        public static CharacterStats ToCombatStats(BestiaryEntry e)
        {
            var sheet = ToCharacterSheet(e);
            return sheet != null ? sheet.ToCombatStats() : null;
        }

        // ------------------------------------------------------------------
        // VALIDATION LIVRE I (budgets de création)
        // ------------------------------------------------------------------

        public static bool Validate(BestiaryEntry e, out string message)
        {
            message = "";
            if (e == null) { message = "Entrée nulle."; return false; }
            if (string.IsNullOrWhiteSpace(e.id)) { message = "id manquant."; return false; }
            var a = e.attributes ?? new BestiaryAttributes();
            int sum8 = a.FOR + a.AGI + a.CON + a.RAP + a.INT + a.ERU + a.CHA + a.INS;
            var profile = ParseProfile(e.profile);

            // Boss : budget libre (Livre XI) — on vérifie juste les bornes.
            if (profile == CharacterProfileType.PnjBoss)
            {
                message = $"Boss (budget libre) : 8 attrs = {sum8}.";
                return true;
            }
            if (profile == CharacterProfileType.PnjSbire)
            {
                bool ok = sum8 <= 12;
                message = ok ? $"Sbire OK : {sum8}/12." : $"Sbire HORS BUDGET : {sum8}/12 !";
                return ok;
            }
            // PnjNormal / HerosPJ : 2 piliers à 4 + 15 secondaires (17 si MAG>0).
            int[] v = { a.FOR, a.AGI, a.CON, a.RAP, a.INT, a.ERU, a.CHA, a.INS };
            Array.Sort(v);
            int pillars = v[7] + v[6];
            int secondaries = sum8 - pillars;
            int budget = a.MAG > 0 ? 17 : 15;
            bool okN = secondaries <= budget;
            message = okN
                ? $"Régulier OK : piliers {v[7]}+{v[6]}, secondaires {secondaries}/{budget}."
                : $"Régulier HORS BUDGET : secondaires {secondaries}/{budget} (piliers {v[7]}+{v[6]}) !";
            return okN;
        }

        // ------------------------------------------------------------------
        // SAUVEGARDE DE L'OVERRIDE ÉDITÉ (fenêtre Bestiaire)
        // ------------------------------------------------------------------

        public static string GetOverridePath()
        {
            return Path.Combine(Application.persistentDataPath, OverrideFileName);
        }

        /// <summary>Sérialise la liste courante vers l'override disque (rechargé en priorité).</summary>
        public static bool SaveOverride(List<BestiaryEntry> entries, out string message)
        {
            message = "";
            try
            {
                var data = new BestiaryData
                {
                    version = 1,
                    source = "Édité en jeu (fenêtre Bestiaire) — " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                    entries = entries ?? new List<BestiaryEntry>()
                };
                string json = JsonUtility.ToJson(data, true);
                File.WriteAllText(GetOverridePath(), json);
                InvalidateCache();
                message = $"Bestiaire sauvegardé : {GetOverridePath()} ({data.entries.Count} entrées). Rechargé.";
                return true;
            }
            catch (Exception ex)
            {
                message = $"Échec sauvegarde : {ex.Message}";
                return false;
            }
        }

#if UNITY_EDITOR
        /// <summary>Éditeur Unity uniquement : réexporte vers le Resources embarqué.</summary>
        public static bool ExportToResources(List<BestiaryEntry> entries, out string message)
        {
            message = "";
            try
            {
                var data = new BestiaryData
                {
                    version = 1,
                    source = "Export éditeur — " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                    entries = entries ?? new List<BestiaryEntry>()
                };
                string path = Path.Combine(Application.dataPath, "Resources/Data/Bestiary.json");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonUtility.ToJson(data, true));
                UnityEditor.AssetDatabase.Refresh();
                InvalidateCache();
                message = $"Exporté vers {path} ({data.entries.Count} entrées).";
                return true;
            }
            catch (Exception ex)
            {
                message = $"Échec export : {ex.Message}";
                return false;
            }
        }
#endif
    }
}

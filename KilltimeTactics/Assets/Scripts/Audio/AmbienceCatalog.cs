using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

namespace Killtime.Audio
{
    [Serializable]
    public class AmbiencePreset
    {
        public string id = "amb_asteroid_base";
        public string displayName = "Base d'Astéroïde (Pressurisation & Machinerie)";
        public string soundCueId = "Ambience_AsteroidBase";
        public float defaultVolume = 0.65f;
        public float defaultFadeDuration = 1.5f;
        public bool spatial = false;
        public float defaultRadius = 15f;
        public string description = "Atmosphère pressurisée industrielle avec conduits et ventilations lourdes.";
    }

    [Serializable]
    public class AmbienceCatalogData
    {
        public List<AmbiencePreset> presets = new();
    }

    /// <summary>
    /// Service de persistance et de distribution des ambiances scéniques (AmbiencePresets.json).
    /// </summary>
    public static class AmbienceCatalog
    {
        private static readonly List<AmbiencePreset> _cachedPresets = new();
        private static bool _isLoaded = false;

        public static string FilePath => Path.Combine(Application.persistentDataPath, "AmbiencePresets.json");

        public static List<AmbiencePreset> GetAll()
        {
            if (!_isLoaded || _cachedPresets.Count == 0)
            {
                LoadOrInitialize();
            }
            return _cachedPresets;
        }

        public static AmbiencePreset Find(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            var list = GetAll();
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i].id, id, StringComparison.OrdinalIgnoreCase))
                    return list[i];
            }
            return null;
        }

        public static void LoadOrInitialize()
        {
            _cachedPresets.Clear();
            string path = FilePath;

            if (File.Exists(path))
            {
                try
                {
                    string json = File.ReadAllText(path);
                    var data = JsonUtility.FromJson<AmbienceCatalogData>(json);
                    if (data != null && data.presets != null && data.presets.Count > 0)
                    {
                        _cachedPresets.AddRange(data.presets);
                        _isLoaded = true;
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[AmbienceCatalog] Échec de lecture du JSON '{path}' : {ex.Message}. Réinitialisation par défaut.");
                }
            }

            _cachedPresets.AddRange(CreateDefaultPresets());
            Save();
            _isLoaded = true;
        }

        public static void Save()
        {
            try
            {
                var data = new AmbienceCatalogData { presets = _cachedPresets };
                string json = JsonUtility.ToJson(data, true);
                File.WriteAllText(FilePath, json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AmbienceCatalog] Échec d'écriture du fichier '{FilePath}' : {ex.Message}");
            }
        }

        private static List<AmbiencePreset> CreateDefaultPresets()
        {
            return new List<AmbiencePreset>
            {
                new AmbiencePreset
                {
                    id = "amb_asteroid_base",
                    displayName = "Base d'Astéroïde (Vent & Machinerie)",
                    soundCueId = "Ambience_AsteroidBase",
                    defaultVolume = 0.65f,
                    defaultFadeDuration = 1.5f,
                    spatial = false,
                    defaultRadius = 15f,
                    description = "Bourdonnement de machinerie et flux de ventilation pressurisé sous roche."
                },
                new AmbiencePreset
                {
                    id = "amb_deep_space",
                    displayName = "Vide Spatial & Nébuleuse",
                    soundCueId = "Ambience_Space",
                    defaultVolume = 0.55f,
                    defaultFadeDuration = 2.0f,
                    spatial = false,
                    defaultRadius = 25f,
                    description = "Sub-bass profond et résonance cosmique éthérée."
                },
                new AmbiencePreset
                {
                    id = "amb_reactor_core",
                    displayName = "Cœur de Réacteur Arcanotech",
                    soundCueId = "Ambience_Reactor",
                    defaultVolume = 0.70f,
                    defaultFadeDuration = 1.2f,
                    spatial = true,
                    defaultRadius = 12f,
                    description = "Vibrations magnétiques nytharite pulsantes et harmoniques arcaniques."
                },
                new AmbiencePreset
                {
                    id = "amb_derelict_hull",
                    displayName = "Épave & Structure Métallique",
                    soundCueId = "Ambience_Derelict",
                    defaultVolume = 0.60f,
                    defaultFadeDuration = 1.8f,
                    spatial = false,
                    defaultRadius = 18f,
                    description = "Craquements thermiques de coque et dépressurisations sporadiques."
                },
                new AmbiencePreset
                {
                    id = "amb_surface_storm",
                    displayName = "Tempête de Surface",
                    soundCueId = "Ambience_Storm",
                    defaultVolume = 0.75f,
                    defaultFadeDuration = 1.5f,
                    spatial = false,
                    defaultRadius = 30f,
                    description = "Bourrasques éoliennes violentes et crépitements de régolithe."
                },
                new AmbiencePreset
                {
                    id = "amb_rebel_hangar",
                    displayName = "Hangar & Avant-Poste",
                    soundCueId = "Ambience_Hangar",
                    defaultVolume = 0.65f,
                    defaultFadeDuration = 1.0f,
                    spatial = true,
                    defaultRadius = 16f,
                    description = "Générateurs diesel-arcanotech et activité opérationnelle distante."
                },
                new AmbiencePreset
                {
                    id = "amb_tactical_stealth",
                    displayName = "Infiltration / Tension Sourde",
                    soundCueId = "Ambience_Tension",
                    defaultVolume = 0.50f,
                    defaultFadeDuration = 2.5f,
                    spatial = false,
                    defaultRadius = 10f,
                    description = "Basse fréquence minimale et battements sourds d'inconfort."
                },
                new AmbiencePreset
                {
                    id = "amb_creuset_command",
                    displayName = "Station Creuset - Passerelle",
                    soundCueId = "Ambience_Bridge",
                    defaultVolume = 0.60f,
                    defaultFadeDuration = 1.4f,
                    spatial = true,
                    defaultRadius = 14f,
                    description = "Faisceaux de données arcaniques, consoles et ventilation stérile."
                }
            };
        }
    }
}
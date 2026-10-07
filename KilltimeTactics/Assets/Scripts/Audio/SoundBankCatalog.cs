using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Killtime.Audio
{
    /// <summary>
    /// Banque sons/fx — presets procéduraux décrits en JSON (recettes), pas en code.
    /// Fichier : Assets/Resources/Data/SoundBank.json ("Data/SoundBank" en Resources).
    /// Un override persistentDataPath/SoundBank.json prend le pas (éditions en jeu).
    /// kind "swell" = recette complète interprétée par ProceduralAudioFactory ;
    /// kind "ref" = alias vers un SoundId procédural existant.
    /// </summary>
    [Serializable]
    public class SoundBankEntry
    {
        public string id = "";
        public string label = "";
        public string category = "";
        public string kind = "swell";
        public float duration = 5f;
        public float volume = 0.9f;
        public int seed = 1;
        public bool startHot = false;
        public bool bloom = false;
        public bool releaseEnd = true;
        public float shepardOctaves = 2f;
        public float shepardGain = 0.2f;
        public bool brass = false;
        public float brassGain = 0.22f;
        public string refSoundId = "";
        public string details = "";

        public string DisplayName()
        {
            if (!string.IsNullOrWhiteSpace(label)) return label;
            if (!string.IsNullOrWhiteSpace(id)) return id;
            return "(sans nom)";
        }
    }

    [Serializable]
    public class SoundBankData
    {
        public int version = 1;
        public string source = "";
        public List<SoundBankEntry> entries = new();
    }

    /// <summary>
    /// Chargeur de la banque (override disque > Resources). Pur C# testable :
    /// seul Resources.Load / persistentDataPath touchent à Unity.
    /// </summary>
    public static class SoundBankCatalog
    {
        public const string ResourcesPath = "Data/SoundBank";
        public const string OverrideFileName = "SoundBank.json";

        private static List<SoundBankEntry> _cached;
        private static string _loadedFrom = "";

        public static string LoadedFrom => _loadedFrom;

        public static void InvalidateCache() { _cached = null; _loadedFrom = ""; }

        /// <summary>Priorité : override disque (éditions en jeu) > Resources embarqué.</summary>
        public static List<SoundBankEntry> GetAll()
        {
            if (_cached != null) return _cached;
            _cached = new List<SoundBankEntry>();

            try
            {
                string overridePath = Path.Combine(Application.persistentDataPath, OverrideFileName);
                if (File.Exists(overridePath))
                {
                    if (TryParseBankJson(File.ReadAllText(overridePath), out var list, out _)
                        && list != null && list.Count > 0)
                    {
                        _cached = list;
                        _loadedFrom = "override:" + overridePath;
                        return _cached;
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning($"[SoundBank] Override illisible : {e.Message}"); }

            try
            {
                var asset = Resources.Load<TextAsset>(ResourcesPath);
                if (asset != null)
                {
                    if (TryParseBankJson(asset.text, out var list, out _)
                        && list != null && list.Count > 0)
                    {
                        _cached = list;
                        _loadedFrom = "resources:" + ResourcesPath;
                        return _cached;
                    }
                }
                Debug.LogWarning("[SoundBank] Aucun TextAsset 'Data/SoundBank' trouvé dans Resources.");
            }
            catch (Exception e) { Debug.LogWarning($"[SoundBank] Resources illisible : {e.Message}"); }

            _loadedFrom = "empty";
            return _cached;
        }

        public static SoundBankEntry Find(string bankId)
        {
            if (string.IsNullOrWhiteSpace(bankId)) return null;
            var all = GetAll();
            if (all == null) return null;
            string want = bankId.Trim();

            if (string.Equals(want, "HBO", StringComparison.OrdinalIgnoreCase)
                || string.Equals(want, "Cinematic_WarpSwell", StringComparison.OrdinalIgnoreCase)
                || string.Equals(want, "Cinematic_ApproachSwell", StringComparison.OrdinalIgnoreCase)
                || string.Equals(want, "WarpSwell", StringComparison.OrdinalIgnoreCase)
                || string.Equals(want, "ApproachSwell", StringComparison.OrdinalIgnoreCase))
            {
                want = "Cinematic_OpeningSwell";
            }

            for (int i = 0; i < all.Count; i++)
            {
                var e = all[i];
                if (e == null || string.IsNullOrWhiteSpace(e.id)) continue;
                if (string.Equals(e.id.Trim(), want, StringComparison.OrdinalIgnoreCase))
                    return e;
            }
            for (int i = 0; i < all.Count; i++)
            {
                var e = all[i];
                if (e == null) continue;
                if (!string.IsNullOrWhiteSpace(e.label) && (string.Equals(e.label.Trim(), want, StringComparison.OrdinalIgnoreCase)
                    || e.label.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0))
                    return e;
            }
            return null;
        }

        /// <summary>Parse pur (testable sans Unity) : ne touche ni disque ni Resources.</summary>
        public static bool TryParseBankJson(string json, out List<SoundBankEntry> entries, out string error)
        {
            entries = new List<SoundBankEntry>();
            error = "";
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "JSON vide.";
                return false;
            }
            try
            {
                var data = JsonUtility.FromJson<SoundBankData>(json);
                if (data == null || data.entries == null)
                {
                    error = "Racine illisible (version/entries).";
                    return false;
                }
                for (int i = 0; i < data.entries.Count; i++)
                {
                    var e = data.entries[i];
                    if (e == null || string.IsNullOrWhiteSpace(e.id)) continue;
                    e.id = e.id.Trim();
                    if (e.duration < 0.2f) e.duration = 0.2f;
                    else if (e.duration > 60f) e.duration = 60f;
                    if (e.volume < 0f) e.volume = 0f;
                    else if (e.volume > 2f) e.volume = 2f;
                    if (e.shepardGain < 0f) e.shepardGain = 0f;
                    else if (e.shepardGain > 1f) e.shepardGain = 1f;
                    if (e.brassGain < 0f) e.brassGain = 0f;
                    else if (e.brassGain > 1f) e.brassGain = 1f;
                    entries.Add(e);
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}

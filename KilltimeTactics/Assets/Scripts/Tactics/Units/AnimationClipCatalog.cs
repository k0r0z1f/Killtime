using System;
using System.Collections.Generic;
using UnityEngine;

namespace Killtime.Tactics.Units
{
    /// <summary>
    /// RD-023 — Catalogue validé game design des clips d'animation requis.
    /// Fichier : Assets/Resources/Data/AnimationClipCatalog.json ("Data/AnimationClipCatalog" en Resources).
    /// Source unique avant prod : chaque entrée = 1 clip requis par le game design
    /// (familles Idle, Marche, Course, Visée, Tir, Blessé, Mort + Transitions, Utilitaire),
    /// avec statut (present_* / missing), priorité et actions RD-024 (import/retarget) / RD-025 (Animator).
    /// Pattern calqué sur BestiaryCatalog : pur C# testable (seul Resources.Load touche à Unity),
    /// parse injectable pour les tests NUnit (Parse).
    /// </summary>
    [Serializable]
    public class AnimationClipEntry
    {
        public string id = "";
        public string name = "";
        public string category = "";
        public string animatorState = "";
        public string clipFile = "";
        public string genderVariants = "";
        public string status = "missing";
        public string priority = "medium";
        public string gameDesignRef = "";
        public string codeRef = "";
        public string fallback = "";
        public string action = "";

        public bool IsPresent => !string.IsNullOrEmpty(status) && status.StartsWith("present", StringComparison.OrdinalIgnoreCase);
        public bool IsMissing => !IsPresent;
        public bool IsHighPriority => string.Equals(priority, "high", StringComparison.OrdinalIgnoreCase);
    }

    [Serializable]
    public class AnimationClipCatalogData
    {
        public int version = 1;
        public string source = "";
        public string updated = "";
        public List<string> families = new List<string>();
        public List<AnimationClipEntry> entries = new List<AnimationClipEntry>();
    }

    /// <summary>
    /// Chargeur + requêtes du catalogue RD-023. Priorité : Resources embarqué.
    /// </summary>
    public static class AnimationClipCatalog
    {
        public const string ResourcesPath = "Data/AnimationClipCatalog";

        /// <summary>Les 7 familles game design imposées par RD-023 (details : Idle, marche, course, visée, tir, blessé, mort).</summary>
        public static readonly string[] RequiredFamilies =
        {
            "Idle", "Marche", "Course", "Visée", "Tir", "Blessé", "Mort"
        };

        private static List<AnimationClipEntry> _cached;
        private static AnimationClipCatalogData _cachedData;

        public static void InvalidateCache() { _cached = null; _cachedData = null; }

        public static AnimationClipCatalogData Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try { return JsonUtility.FromJson<AnimationClipCatalogData>(json); }
            catch (Exception e)
            {
                Debug.LogWarning($"[AnimationClipCatalog] JSON illisible : {e.Message}");
                return null;
            }
        }

        public static AnimationClipCatalogData LoadData()
        {
            if (_cachedData != null) return _cachedData;
            try
            {
                var asset = Resources.Load<TextAsset>(ResourcesPath);
                if (asset != null)
                {
                    _cachedData = Parse(asset.text);
                    if (_cachedData != null)
                    {
                        _cached = _cachedData.entries ?? new List<AnimationClipEntry>();
                        return _cachedData;
                    }
                }
                Debug.LogWarning("[AnimationClipCatalog] Aucun TextAsset 'Data/AnimationClipCatalog' trouvé dans Resources.");
            }
            catch (Exception e) { Debug.LogWarning($"[AnimationClipCatalog] Resources illisible : {e.Message}"); }
            return null;
        }

        public static List<AnimationClipEntry> GetAll()
        {
            if (_cached != null) return _cached;
            var data = LoadData();
            if (data != null && data.entries != null) return _cached;
            _cached = new List<AnimationClipEntry>();
            return _cached;
        }

        public static List<AnimationClipEntry> GetByCategory(string category)
        {
            var out_ = new List<AnimationClipEntry>();
            if (string.IsNullOrEmpty(category)) return out_;
            foreach (var e in GetAll())
                if (e != null && string.Equals(e.category, category.Trim(), StringComparison.OrdinalIgnoreCase))
                    out_.Add(e);
            return out_;
        }

        public static List<AnimationClipEntry> GetMissing()
        {
            var out_ = new List<AnimationClipEntry>();
            foreach (var e in GetAll())
                if (e != null && e.IsMissing) out_.Add(e);
            return out_;
        }

        public static List<AnimationClipEntry> GetMissingHighPriority()
        {
            var out_ = new List<AnimationClipEntry>();
            foreach (var e in GetAll())
                if (e != null && e.IsMissing && e.IsHighPriority) out_.Add(e);
            return out_;
        }

        public static AnimationClipEntry GetById(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            foreach (var e in GetAll())
                if (e != null && string.Equals(e.id, id.Trim(), StringComparison.OrdinalIgnoreCase))
                    return e;
            return null;
        }

        /// <summary>
        /// Validation RD-023 : chaque famille requise a ≥1 entrée, et tout le P0 présent
        /// est identifié. Retourne la liste d'erreurs (vide = valide).
        /// </summary>
        public static List<string> Validate(AnimationClipCatalogData data = null)
        {
            var errors = new List<string>();
            data ??= LoadData();
            if (data == null) { errors.Add("Catalogue introuvable ou illisible."); return errors; }
            var entries = data.entries ?? new List<AnimationClipEntry>();

            foreach (var fam in RequiredFamilies)
            {
                bool found = false;
                foreach (var e in entries)
                    if (e != null && string.Equals(e.category, fam, StringComparison.OrdinalIgnoreCase)) { found = true; break; }
                if (!found) errors.Add($"Famille game design sans entrée : {fam}.");
            }

            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in entries)
            {
                if (e == null) { errors.Add("Entrée nulle."); continue; }
                if (string.IsNullOrEmpty(e.id)) errors.Add("Entrée sans id.");
                else if (!ids.Add(e.id)) errors.Add($"Id dupliqué : {e.id}.");
                if (string.IsNullOrEmpty(e.animatorState)) errors.Add($"Entrée {e.id} sans animatorState.");
                if (string.IsNullOrEmpty(e.status)) errors.Add($"Entrée {e.id} sans status.");
            }
            return errors;
        }
    }
}

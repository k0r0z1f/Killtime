using System;
using System.Collections.Generic;
using Killtime.Core.Character;

namespace Killtime.Core.Combat
{
    /// <summary>
    /// Registre des réglages IA des Boss Titans lus depuis Bestiary.json
    /// ("bossAI" par entrée, Livre XII §49-50 — RD-087 suite).
    /// Pur C# testable : le registre se charge depuis une
    /// <see cref="BestiaryData"/> injectée (aucun accès Unity direct).
    /// Au runtime, <see cref="EnsureLoaded"/> charge paresseusement depuis
    /// <see cref="BestiaryCatalog.GetAll"/> ; <see cref="InvalidateCache"/>
    /// est appelé par <see cref="BestiaryCatalog.InvalidateCache"/> (cohérent).
    /// Clé = signature de spécialisations (mêmes règles que
    /// <see cref="TitanBossMechanics"/>) avec repli sur le nom.
    /// Entrée absente ou boss inconnu → défauts moteur (consts).
    /// </summary>
    public static class TitanBossAIConfig
    {
        public const string KeyBeast = "beast";
        public const string KeyMinulican = "minulican";
        public const string KeyDisciple = "disciple";

        private static readonly Dictionary<string, TitanBossAIParams> _byKey = new();
        private static readonly TitanBossAIParams _defaults = new();
        private static bool _loaded;

        public static bool IsLoaded => _loaded;

        /// <summary>Vide le registre ; le prochain accès rechargera depuis le catalogue.</summary>
        public static void InvalidateCache()
        {
            _byKey.Clear();
            _loaded = false;
        }

        /// <summary>Charge le registre depuis des entrées injectées (tests, sans Unity).</summary>
        public static void InjectEntries(List<BestiaryEntry> entries)
        {
            LoadEntries(entries);
            _loaded = true;
        }

        /// <summary>Charge le registre depuis un bloc JSON désérialisé (tests, sans Unity).</summary>
        public static void InjectData(BestiaryData data)
        {
            InjectEntries(data != null ? data.entries : null);
        }

        private static void LoadEntries(List<BestiaryEntry> entries)
        {
            _byKey.Clear();
            if (entries == null) return;
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e == null || e.bossAI == null) continue;
                string key = KeyForEntry(e);
                if (!string.IsNullOrEmpty(key))
                    _byKey[key] = e.bossAI;
            }
        }

        /// <summary>Chargement paresseux runtime depuis le catalogue (lazy).</summary>
        public static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                LoadEntries(BestiaryCatalog.GetAll());
            }
            catch
            {
                _byKey.Clear();
            }
        }

        /// <summary>Params JSON du boss reconnu, ou défauts si entrée absente / inconnue.</summary>
        public static TitanBossAIParams GetParams(CharacterStats stats)
        {
            EnsureLoaded();
            string key = KeyForStats(stats);
            if (!string.IsNullOrEmpty(key)
                && _byKey.TryGetValue(key, out var p)
                && p != null)
                return p;
            return _defaults;
        }

        /// <summary>Défauts moteur partagés (lecture seule : ne pas muter).</summary>
        public static TitanBossAIParams Defaults => _defaults;

        // ------------------------------------------------------------------
        // Classification (spécialisations d'abord, nom en repli — comme l'IA)
        // ------------------------------------------------------------------

        public static string KeyForStats(CharacterStats stats)
        {
            if (stats == null) return null;
            if (TitanBossMechanics.IsBeastOfHybris(stats)) return KeyBeast;
            if (TitanBossMechanics.IsMinulican(stats)) return KeyMinulican;
            if (TitanBossMechanics.IsDiscipleDeTerre(stats)) return KeyDisciple;
            return KeyForName(stats.Name);
        }

        public static string KeyForEntry(BestiaryEntry entry)
        {
            if (entry == null) return null;
            var specs = entry.specializations;
            if (specs != null)
            {
                for (int i = 0; i < specs.Count; i++)
                {
                    string s = specs[i];
                    if (string.IsNullOrWhiteSpace(s)) continue;
                    if (string.Equals(s, TitanBossMechanics.SpecFlameMemory, StringComparison.Ordinal)
                        || string.Equals(s, TitanBossMechanics.SpecClawSweep, StringComparison.Ordinal)
                        || string.Equals(s, TitanBossMechanics.SpecTelluricCharge, StringComparison.Ordinal))
                        return KeyBeast;
                    if (string.Equals(s, TitanBossMechanics.SpecThermalAbsorption, StringComparison.Ordinal)
                        || string.Equals(s, TitanBossMechanics.SpecVoidChain, StringComparison.Ordinal)
                        || string.Equals(s, TitanBossMechanics.SpecQuantumLeap, StringComparison.Ordinal))
                        return KeyMinulican;
                    if (string.Equals(s, TitanBossMechanics.SpecTectonicFist, StringComparison.Ordinal)
                        || string.Equals(s, TitanBossMechanics.SpecLithoRegen, StringComparison.Ordinal)
                        || string.Equals(s, TitanBossMechanics.SpecThermalShock, StringComparison.Ordinal)
                        || string.Equals(s, TitanBossMechanics.SpecMagmaCarapace, StringComparison.Ordinal))
                        return KeyDisciple;
                }
            }
            string key = KeyForName(entry.name);
            if (!string.IsNullOrEmpty(key)) return key;
            return KeyForName(entry.id);
        }

        private static string KeyForName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            if (name.IndexOf("Minulican", StringComparison.OrdinalIgnoreCase) >= 0) return KeyMinulican;
            if (name.IndexOf("Disciple de Terre", StringComparison.OrdinalIgnoreCase) >= 0) return KeyDisciple;
            if (name.IndexOf("Hybris", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Bête d'", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Bete d'", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("bete_hybris", StringComparison.OrdinalIgnoreCase) >= 0)
                return KeyBeast;
            return null;
        }
    }
}

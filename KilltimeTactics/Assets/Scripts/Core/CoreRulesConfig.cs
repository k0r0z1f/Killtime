using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics.Units;
using Killtime.Core.Combat;

namespace Killtime.Core.Rules
{
    [Serializable]
    public class BodyPartRuleEntry
    {
        public BodyPart Part;
        public int DifficultyModifier;
        public int CriticalDamageMultiplier;
    }

    [Serializable]
    public class CoreRulesConfig
    {
        private static CoreRulesConfig _instance;
        public static CoreRulesConfig Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = LoadOrCreate();
                }
                return _instance;
            }
        }

        // --- Livre I : Attributs & Vitalité ---
        [Header("Attributs & Seuils Vitaux (Livre I)")]
        public int EncaissementMultiplier = 2;
        public int LethalMultiplier = 5;
        public int AttributeMin = 1;
        public int AttributeMax = 10;
        public int SenseMin = 1;
        public int SenseMax = 6;

        // --- Livre I & VI : Points d'Action & Souffle ---
        [Header("Économie d'Action & Souffle (Livre I & VI)")]
        public int EmergencyBreathBonusAP = 2;
        public int EmergencyBreathEssoufflementCost = 1;
        public float LastBreathAPRatio = 0.5f;

        // --- Livre VI : Combat & Passes d'Armes ---
        [Header("Passes d'Armes & Résolution (Livre VI)")]
        public int BaseAttackAPCost = 2;
        public int CancelAimPenaltyAPCost = 1;
        public int BaseReactionAPCost = 1;
        public int UnreactiveDefensePenalty = -2;
        public int StandardTargetDC = 10;
        public int SingleAttackActionLimit = 1;
        public int MultiDiceAttackActionLimit = 2;

        // --- Livre VI, Chap. 26 : VATS Anatomique ---
        [Header("Anatomie Chirurgicale (Livre VI, Chap. 26)")]
        public List<BodyPartRuleEntry> BodyPartRules = new();

        // --- Livre I, III & IV : Progression & Arcanotech ---
        [Header("Progression & Arcanotech (Livre I & IV)")]
        public int XPCostTraining = 5;
        public int XPCostSpecialization = 5;
        public int ArcanotechResonancePerStageMultiplier = 2;

        // --- Déplacement & Statuts (Livre VII) ---
        [Header("Déplacement & Statuts")]
        public int BaseMovementAPCost = 1;
        public int RalentiAPMultiplier = 2;

        public static event Action OnRulesChanged;

        public CoreRulesConfig()
        {
            ResetToCodexDefaults();
        }

        public void ResetToCodexDefaults()
        {
            EncaissementMultiplier = 2;
            LethalMultiplier = 5;
            AttributeMin = 1;
            AttributeMax = 10;
            SenseMin = 1;
            SenseMax = 6;

            EmergencyBreathBonusAP = 2;
            EmergencyBreathEssoufflementCost = 1;
            LastBreathAPRatio = 0.5f;

            BaseAttackAPCost = 2;
            CancelAimPenaltyAPCost = 1;
            BaseReactionAPCost = 1;
            UnreactiveDefensePenalty = -2;
            StandardTargetDC = 10;
            SingleAttackActionLimit = 1;
            MultiDiceAttackActionLimit = 2;

            XPCostTraining = 5;
            XPCostSpecialization = 5;
            ArcanotechResonancePerStageMultiplier = 2;

            BaseMovementAPCost = 1;
            RalentiAPMultiplier = 2;

            BodyPartRules.Clear();
            BodyPartRules.Add(new BodyPartRuleEntry { Part = BodyPart.Tete, DifficultyModifier = -2, CriticalDamageMultiplier = 3 });
            BodyPartRules.Add(new BodyPartRuleEntry { Part = BodyPart.YeuxVisage, DifficultyModifier = -3, CriticalDamageMultiplier = 3 });
            BodyPartRules.Add(new BodyPartRuleEntry { Part = BodyPart.CouTrachee, DifficultyModifier = -3, CriticalDamageMultiplier = 3 });
            BodyPartRules.Add(new BodyPartRuleEntry { Part = BodyPart.CoeurPoumons, DifficultyModifier = -2, CriticalDamageMultiplier = 2 });
            BodyPartRules.Add(new BodyPartRuleEntry { Part = BodyPart.Torse, DifficultyModifier = 0, CriticalDamageMultiplier = 1 });
            BodyPartRules.Add(new BodyPartRuleEntry { Part = BodyPart.BrasDroit, DifficultyModifier = -1, CriticalDamageMultiplier = 1 });
            BodyPartRules.Add(new BodyPartRuleEntry { Part = BodyPart.BrasGauche, DifficultyModifier = -1, CriticalDamageMultiplier = 1 });
            BodyPartRules.Add(new BodyPartRuleEntry { Part = BodyPart.Jambes, DifficultyModifier = -1, CriticalDamageMultiplier = 1 });
        }

        public BodyPartRuleEntry GetPartRule(BodyPart part)
        {
            for (int i = 0; i < BodyPartRules.Count; i++)
            {
                if (BodyPartRules[i].Part == part) return BodyPartRules[i];
            }

            var fallback = new BodyPartRuleEntry { Part = part, DifficultyModifier = 0, CriticalDamageMultiplier = 1 };
            BodyPartRules.Add(fallback);
            return fallback;
        }

        public void PropagateLiveChanges()
        {
            var units = UnityEngine.Object.FindObjectsByType<TacticalUnit>();
            for (int i = 0; i < units.Length; i++)
            {
                if (units[i] != null && units[i].Stats != null)
                {
                    units[i].Stats.RecalculateDerivedStats();
                }
            }
            OnRulesChanged?.Invoke();
        }

        private static string ConfigPath => Path.Combine(Application.persistentDataPath, "CoreRulesConfig.json");

        public static CoreRulesConfig LoadOrCreate()
        {
            if (File.Exists(ConfigPath))
            {
                try
                {
                    string json = File.ReadAllText(ConfigPath);
                    var cfg = JsonUtility.FromJson<CoreRulesConfig>(json);
                    if (cfg != null) return cfg;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[CoreRulesConfig] Échec de lecture du fichier : {e.Message}. Restauration des valeurs Codex.");
                }
            }
            var newConfig = new CoreRulesConfig();
            newConfig.SaveToDisk();
            return newConfig;
        }

        public void SaveToDisk()
        {
            try
            {
                string json = JsonUtility.ToJson(this, true);
                File.WriteAllText(ConfigPath, json);
            }
            catch (Exception e)
            {
                Debug.LogError($"[CoreRulesConfig] Erreur de sauvegarde : {e.Message}");
            }
        }
    }
}
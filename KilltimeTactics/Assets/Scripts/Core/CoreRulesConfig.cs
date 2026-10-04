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
        // Duel aveugle (Livres II §7 + VI §24) : bonus au total par point
        // d'Essoufflement (PE) engagé dans une déclaration (plafond = Constitution).
        public int DuelPEBonusPerPoint = 1;

        // --- Livre VI : Combat & Passes d'Armes ---
        [Header("Passes d'Armes & Résolution (Livre VI)")]
        public int BaseAttackAPCost = 2;
        public int CancelAimPenaltyAPCost = 1;
        public int BaseReactionAPCost = 1;
        public int UnreactiveDefensePenalty = -2;
        public int StandardTargetDC = 10;
        public int SingleAttackActionLimit = 1;
        public int MultiDiceAttackActionLimit = 2;

        // --- Livre VI §25.3 : Couvert & Visibilité ---
        [Header("Couvert & Visibilité (Livre VI §25.3)")]
        public int HalfCoverAttackPenalty = -1;
        public int ThreeQuartersCoverAttackPenalty = -2;
        public bool FullCoverBlocksAttack = true;
        public bool CoverIgnoredAtContactDistance = true;

        // --- Livre VI §25.3 : Cône de visée (lecture rigoureuse) ---
        [Header("Cône de Visée (Livre VI §25.3)")]
        public float CoverEyeHeight = 1.5f;
        public float CoverBodyHeight = 1.8f;
        public float CoverHalfHeight = 0.7f;
        public float CoverThreeQuartersHeight = 1.2f;
        public float CoverFullHeight = 2.2f;
        public float CoverFullVisibleFraction = 0.9f;
        public float CoverHalfVisibleFraction = 0.45f;

        // --- RD-030 : Guet / Overwatch (Réactions) ---
        [Header("Guet / Overwatch (RD-030)")]
        [Tooltip("Coût en PA pour se mettre en guet (tir de réaction réservé).")]
        public int OverwatchAPCost = 2;
        [Tooltip("Tirs de réaction par guet (1 : le guet est consommé au premier tir).")]
        public int OverwatchShotsPerWatch = 1;

        // --- RD-031 : Attaque d'opportunité / désengagement (Réactions) ---
        [Header("Opportunité / Désengagement (RD-031)")]
        [Tooltip("Portée de contact : quitter cette distance provoque 1 réaction (cases).")]
        public int OpportunityReach = 1;
        [Tooltip("Coût en PA de la Poursuite (suit d'1 case en réaction).")]
        public int OpportunityFollowAPCost = 1;
        [Tooltip("Coût en PA du Blocage (duel opposé qui annule le pas).")]
        public int OpportunityBlockAPCost = 1;
        [Tooltip("Coût en PA du Décrochage (prochain déplacement sans réaction).")]
        public int DisengageAPCost = 1;
        [Tooltip("Si vrai, la frappe et le balayage d'opportunité sont gratuits (0 PA, réaction seule).")]
        public bool OpportunityStrikeIsFree = true;

        // --- RD-038 : Charge & Sprint (Positionnement) ---
        [Header("Charge & Sprint (RD-038)")]
        [Tooltip("Distance minimale en cases pour déclencher une charge / sprint (3 par défaut).")]
        public int ChargeMinDistance = 3;
        [Tooltip("Bonus aux dégâts sur l'attaque de contact suivant une charge (+2 par défaut).")]
        public int ChargeDamageBonus = 2;
        [Tooltip("Malus en défense jusqu'au prochain tour personnel après une charge (-1 par défaut).")]
        public int ChargeDefensePenalty = -1;
        [Tooltip("Coût PA supplémentaire pour la manœuvre de charge déclarée (+1 PA Livre VI §Manœuvres Mobiles).")]
        public int ChargeManeuverExtraAPCost = 1;
        [Tooltip("Si vrai, le sprint (3+ cases) bloque tout tir à distance pour le reste du tour (RD-038).")]
        public bool SprintBlocksRanged = true;

        // --- RD-084 : Attaques combinées & Poursuite (Livre VI §25.1 & §25.2) ---
        [Header("Assaut groupé & Poursuite (RD-084, Livre VI §25.1-25.2)")]
        [Tooltip("Attaquants minimum pour frapper à l'unisson (2 par défaut, Livre VI §25.2).")]
        public int CombinedAttackMinMembers = 2;
        [Tooltip("Coût en PA par participant pour l'attaque combinée (2 par défaut).")]
        public int CombinedAttackAPCost = 2;
        [Tooltip("Faillites cumulées avant capture immédiate de la proie (4 par défaut, Livre VI §25.1).")]
        public int PursuitFailuresToCapture = 4;
        [Tooltip("Distance minimale maintenue sur réussite de la proie (1 case par défaut).")]
        public int PursuitMinDistance = 1;
        [Tooltip("Coût en PA d'un round de course-poursuite par participant (1 par défaut).")]
        public int PursuitAPCost = 1;

        // --- RD-048 : Retarder / Tenir / Canalisation (Livre VI — Tours) ---
        [Header("Retarder / Tenir / Canalisation (RD-048)")]
        [Tooltip("Pénalité d'initiative appliquée quand un combattant retarde son tour (-2 par défaut).")]
        public int DelayInitiativePenalty = 2;
        [Tooltip("Coût en PA pour tenir son action / se déclarer en attente (0 par défaut).")]
        public int HoldAPCost = 0;
        [Tooltip("Coût en PA pour entrer en canalisation / action en progression (1 par défaut).")]
        public int ChannelStartAPCost = 1;
        [Tooltip("Bonus au jet conféré par la canalisation consommée à l'attaque (+2 par défaut, comme la Préparation).")]
        public int ChannelAttackBonus = 2;
        [Tooltip("Malus en défense tant que le combattant canalise (-1 par défaut, Livre VI §24.3).")]
        public int ChannelDefensePenalty = -1;

        // --- Livre VI, Chap. 26 : VATS Anatomique ---
        [Header("Anatomie Chirurgicale (Livre VI, Chap. 26)")]
        public List<BodyPartRuleEntry> BodyPartRules = new();

        // --- Livre I, III & IV : Progression & Arcanotech ---
        [Header("Progression & Arcanotech (Livre I & IV)")]
        public int XPCostTraining = 5;
        public int XPCostSpecialization = 5;
        public int ArcanotechResonancePerStageMultiplier = 2;

        // --- Livre I §5 : Progression organique par réussites ---
        [Header("Progression organique (Livre I §5)")]
        [Tooltip("Multiplicateur appliqué quand de l'XP lié est dépensé hors de sa compétence d'origine (x2).")]
        public int XPOffSkillCostMultiplier = 2;
        [Tooltip("Si vrai, chaque réussite coche 1 case de progression dans la compétence utilisée.")]
        public bool EnableSkillProgressTicks = true;

        // --- Déplacement & Statuts (Livre VII) ---
        [Header("Déplacement & Statuts")]
        public int BaseMovementAPCost = 1;
        public int RalentiAPMultiplier = 2;

        // --- RD-083 : Souffrant & Soins Majeurs (Livre VII §28.3, §29.1, §29.3) ---
        [Header("Souffrant & Soins Majeurs (Livre VII §28.3/29.1/29.3)")]
        [Tooltip("Multiplicateur du seuil de Soins Majeurs : soin unique Premiers Soins/Chirurgie >= CON x Multiplicateur pour lever Souffrant.")]
        public int SouffrantMajorCareMultiplier = 2;
        [Tooltip("Base de jours réels de convalescence post-résurrection : Durée = Base - CON (Livre VII §29.3).")]
        public int ResurrectionSequelaeBaseDays = 10;
        [Tooltip("Malus en ec sur tous les jets pendant la convalescence post-résurrection (-1 par défaut).")]
        public int ResurrectionSequelaePenalty = -1;

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
            DuelPEBonusPerPoint = 1;

            BaseAttackAPCost = 2;
            CancelAimPenaltyAPCost = 1;
            BaseReactionAPCost = 1;
            UnreactiveDefensePenalty = -2;
            StandardTargetDC = 10;
            SingleAttackActionLimit = 1;
            MultiDiceAttackActionLimit = 2;

            HalfCoverAttackPenalty = -1;
            ThreeQuartersCoverAttackPenalty = -2;
            FullCoverBlocksAttack = true;
            CoverIgnoredAtContactDistance = true;

            CoverEyeHeight = 1.5f;
            CoverBodyHeight = 1.8f;
            CoverHalfHeight = 0.7f;
            CoverThreeQuartersHeight = 1.2f;
            CoverFullHeight = 2.2f;
            CoverFullVisibleFraction = 0.9f;
            CoverHalfVisibleFraction = 0.45f;

            XPCostTraining = 5;
            XPCostSpecialization = 5;
            ArcanotechResonancePerStageMultiplier = 2;
            XPOffSkillCostMultiplier = 2;
            EnableSkillProgressTicks = true;

            BaseMovementAPCost = 1;
            RalentiAPMultiplier = 2;

            SouffrantMajorCareMultiplier = 2;
            ResurrectionSequelaeBaseDays = 10;
            ResurrectionSequelaePenalty = -1;

            OverwatchAPCost = 2;
            OverwatchShotsPerWatch = 1;

            OpportunityReach = 1;
            OpportunityFollowAPCost = 1;
            OpportunityBlockAPCost = 1;
            DisengageAPCost = 1;
            OpportunityStrikeIsFree = true;

            ChargeMinDistance = 3;
            ChargeDamageBonus = 2;
            ChargeDefensePenalty = -1;
            ChargeManeuverExtraAPCost = 1;
            SprintBlocksRanged = true;

            CombinedAttackMinMembers = 2;
            CombinedAttackAPCost = 2;
            PursuitFailuresToCapture = 4;
            PursuitMinDistance = 1;
            PursuitAPCost = 1;

            DelayInitiativePenalty = 2;
            HoldAPCost = 0;
            ChannelStartAPCost = 1;
            ChannelAttackBonus = 2;
            ChannelDefensePenalty = -1;

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
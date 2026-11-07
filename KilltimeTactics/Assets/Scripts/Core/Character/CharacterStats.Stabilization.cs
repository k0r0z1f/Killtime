using System;
using Killtime.Core.Dice;

namespace Killtime.Core.Character
{
    // RD-205 (Livre VI §25) : Stabilisation & Convalescence.
    // Split move-only de CharacterStats — pur (aucune dépendance Unity), testable en EditMode.
    // 1) Stabilisation : Premiers Soins 3 PA au chevet, jet vs Difficulté 12.
    //    Succès = Agonisant → Inconscient stable (Saignement stoppé, plus de jet
    //    de moral en boucle : Inconscient coupe IsAlive + HasTestedMorale verrouillé).
    // 2) Convalescence : 1 nuit sûre (repos campagne, hors-arène) = CON + rang
    //    Endurance ; magie/arcanotech accélère (×2 paramétrable) ; rations branchées
    //    (bonus si repas+eau, malus si négligés — simple et réversible, sans flag
    //    permanent). Ne touche PAS à la séquelle résurrection -1 ec (RD-083).

    /// <summary>
    /// RD-205 : résultat d'une tentative de stabilisation (Livre VI §25).
    /// Pur (aucune dépendance Unity) pour tests et logs d'arène.
    /// </summary>
    [Serializable]
    public struct StabilizationResult
    {
        public bool Attempted;
        public bool HadAgonisant;
        public bool Succeeded;
        public bool IsStable;
        public int Difficulty;
        public int Modifier;
        public DiceType Die;
        [NonSerialized] public DiceRollResult Roll;
        public string Log;
    }

    /// <summary>
    /// RD-205 : résultat d'une nuit de convalescence (Livre VI §25).
    /// Pur (aucune dépendance Unity) pour tests et logs de campagne.
    /// </summary>
    [Serializable]
    public struct ConvalescenceResult
    {
        public int BaseAmount;
        public int RationAdjustment;
        public int MagicMultiplier;
        public bool Accelerated;
        public bool HadMeal;
        public bool HadWater;
        public int Amount;
        public int Healed;
        public bool WokeUp;
        public string Log;
    }

    public partial class CharacterStats
    {
        /// <summary>
        /// RD-205 : patient stabilisé (Inconscient stable après Premiers Soins réussis).
        /// Distingue le coma stabilisé du K.O. brut : réveil via convalescence uniquement.
        /// </summary>
        public bool IsStabilized { get; set; } = false;

        public bool IsAgonisant => ActiveStatus.HasFlag(StatusEffect.Agonisant);

        /// <summary>
        /// RD-205 : coût PA de la Stabilisation au chevet (3 par défaut, Livre VI §25).
        /// Fixe : la spécialisation Suture Réflexe (2 PA) ne s'applique qu'au soin
        /// d'urgence, pas au geste de stabilisation. Paramétrable via CoreRulesConfig.
        /// </summary>
        public int GetStabilizationAPCost()
        {
            try
            {
                int cost = Rules.CoreRulesConfig.Instance.StabilizationAPCost;
                if (cost < 1) return 3;
                return cost;
            }
            catch { return 3; }
        }

        /// <summary>
        /// RD-205 : Difficulté du jet de Premiers Soins pour stabiliser (12 par défaut).
        /// Paramétrable via CoreRulesConfig.
        /// </summary>
        public int GetStabilizationDifficulty()
        {
            try
            {
                int dc = Rules.CoreRulesConfig.Instance.StabilizationDifficulty;
                if (dc < 1) return 12;
                return dc;
            }
            catch { return 12; }
        }

        /// <summary>
        /// RD-205 : Stabilisation d'un Agonisant (Livre VI §25).
        /// Jet de Premiers Soins du soignant au chevet vs Difficulté (12 par défaut).
        /// Succès : Agonisant levé, Saignement stoppé, Inconscient appliqué + flag
        /// IsStabilized, moral verrouillé (plus de test en boucle : Inconscient coupe
        /// IsAlive et HasTestedMorale passe à vrai). Échec : Agonisant persiste.
        /// Ne soigne aucun PV (la convalescence s'en charge hors combat).
        /// Non tentée si le patient n'est pas Agonisant, est mort, ou sans soignant.
        /// </summary>
        public StabilizationResult TryStabilize(CharacterStats caregiver, DiceRoller roller, int? difficultyOverride = null)
        {
            var result = new StabilizationResult
            {
                Attempted = false,
                HadAgonisant = ActiveStatus.HasFlag(StatusEffect.Agonisant),
                Difficulty = difficultyOverride ?? GetStabilizationDifficulty(),
                Log = string.Empty
            };
            if (result.Difficulty < 1) result.Difficulty = 12;
            if (IsDead)
            {
                result.Log = $"🩹 <b>STABILISATION IMPOSSIBLE</b> : {Name} est mort (la réanimation relève de la Défibrillation).";
                return result;
            }
            if (!result.HadAgonisant)
            {
                result.Log = $"🩹 <b>STABILISATION INUTILE</b> : {Name} n'est pas Agonisant.";
                return result;
            }
            if (caregiver == null || caregiver.IsDead)
            {
                result.Log = $"🩹 <b>STABILISATION IMPOSSIBLE</b> : aucun soignant valide au chevet de {Name}.";
                return result;
            }

            result.Attempted = true;
            roller ??= new DiceRoller();
            DiceType die = caregiver.GetSkillDie(SkillType.PremiersSoins, false);
            int mod = caregiver.GetStatusModifier(SkillType.PremiersSoins, false);
            result.Die = die;
            result.Modifier = mod;
            var roll = roller.Roll(die, mod, result.Difficulty);
            result.Roll = roll;
            result.Succeeded = roll.IsSuccess;

            if (result.Succeeded)
            {
                RemoveStatus(StatusEffect.Agonisant);
                ClearResidualDamage(StatusEffect.Saignement);
                ApplyStatus(StatusEffect.Inconscient);
                // Stable : plus de jet de moral/reddition en boucle (Inconscient
                // coupe IsAlive de toute façon, verrou explicite en renfort).
                HasTestedMorale = true;
                IsStabilized = true;
                result.IsStable = true;
                result.Log = $"🩹 <b>STABILISATION RÉUSSIE</b> : {Name} [Premiers Soins {die} + {mod}, Total {roll.Total} vs Difficulté {result.Difficulty}] ➔ <b>Inconscient stable</b> (Saignement stoppé, moral verrouillé).";
            }
            else
            {
                result.IsStable = false;
                result.Log = $"🩹 <b>STABILISATION ÉCHOUÉE</b> : {Name} [Premiers Soins {die} + {mod}, Total {roll.Total} vs Difficulté {result.Difficulty}] ➔ <b>Agonisant PERSISTE</b> (nouvel essai possible).";
            }
            return result;
        }

        // =====================================================================
        // RD-205 : CONVALESCENCE & RATIONS (Livre VI §25 — repos campagne, hors-arène)
        // =====================================================================

        /// <summary>
        /// RD-205 : base de convalescence d'une nuit sûre = CON + rang Endurance
        /// (EndurancePhysique : base (CON+FOR)/2 + entraînements). Pure, testable.
        /// </summary>
        public int GetConvalescenceBaseAmount()
        {
            return Math.Max(0, Attributes.Constitution + GetSkillRank(SkillType.EndurancePhysique, false));
        }

        /// <summary>Vrai si le nom d'item est une ration (Meal Pack, Meal Pack + Water…).</summary>
        public static bool IsMealRationName(string name) =>
            !string.IsNullOrEmpty(name) && name.IndexOf("Meal", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Vrai si le nom d'item apporte de l'hydratation (Water Blob, Meal Pack + Water…).</summary>
        public static bool IsWaterRationName(string name) =>
            !string.IsNullOrEmpty(name) && name.IndexOf("Water", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>Le fourbi contient-il une ration ? (inventaire lu, rien consommé ici).</summary>
        public bool HasMealRation()
        {
            var inv = Sheet?.Inventory;
            if (inv == null) return false;
            for (int i = 0; i < inv.Count; i++)
                if (inv[i] != null && IsMealRationName(inv[i].Name)) return true;
            return false;
        }

        /// <summary>Le fourbi contient-il de l'hydratation ? (inventaire lu, rien consommé ici).</summary>
        public bool HasWaterRation()
        {
            var inv = Sheet?.Inventory;
            if (inv == null) return false;
            for (int i = 0; i < inv.Count; i++)
                if (inv[i] != null && IsWaterRationName(inv[i].Name)) return true;
            return false;
        }

        /// <summary>
        /// RD-205 : nuit sûre de convalescence (Livre VI §25, hors combat).
        /// Soigne CON + rang Endurance, +bonus si repas ET eau, malus si NI repas
        /// NI eau (partiel = base simple — réversible, aucun flag permanent).
        /// Magie/arcanotech : multiplie le total (×2 par défaut). Réveille un
        /// stabilisé (Inconscient + IsStabilized levés), stoppe le Saignement,
        /// réarme le moral au-dessus de 28 % comme un soin ordinaire.
        /// Ne lève JAMAIS Souffrant (Soins Majeurs CON×2 requis, RD-083).
        /// </summary>
        public ConvalescenceResult ApplySafeNightRest(bool hasMeal, bool hasWater, bool magicBoost = false, bool arcanotechBoost = false)
        {
            var result = new ConvalescenceResult
            {
                HadMeal = hasMeal,
                HadWater = hasWater,
                MagicMultiplier = 1,
                Log = string.Empty
            };
            if (IsDead)
            {
                result.Log = $"🌙 <b>CONVALESCENCE IMPOSSIBLE</b> : {Name} est mort.";
                return result;
            }

            int rationBonus = 2;
            int neglectPenalty = -3;
            int magicMult = 2;
            try
            {
                var cfg = Rules.CoreRulesConfig.Instance;
                if (cfg.ConvalescenceRationBonus >= 0) rationBonus = cfg.ConvalescenceRationBonus;
                if (cfg.ConvalescenceNeglectPenalty <= 0) neglectPenalty = cfg.ConvalescenceNeglectPenalty;
                if (cfg.ConvalescenceMagicMultiplier >= 1) magicMult = cfg.ConvalescenceMagicMultiplier;
            }
            catch { /* défauts Codex */ }

            int baseAmount = GetConvalescenceBaseAmount();
            result.BaseAmount = baseAmount;
            bool wellFed = hasMeal && hasWater;
            bool neglected = !hasMeal && !hasWater;
            int amount = baseAmount;
            if (wellFed) { amount += rationBonus; result.RationAdjustment = rationBonus; }
            else if (neglected) { amount += neglectPenalty; result.RationAdjustment = neglectPenalty; }
            else { result.RationAdjustment = 0; }

            bool accelerated = magicBoost || arcanotechBoost;
            result.Accelerated = accelerated;
            if (accelerated) { amount *= magicMult; result.MagicMultiplier = magicMult; }

            amount = Math.Max(0, amount);
            result.Amount = amount;

            int before = CurrentHealth;
            CurrentHealth = Math.Min(MaxHealth, CurrentHealth + amount);
            result.Healed = Math.Max(0, CurrentHealth - before);

            // Une nuit sûre et surveillée stoppe l'hémorragie comme un soin ordinaire.
            ClearResidualDamage(StatusEffect.Saignement);

            // Réveil du stabilisé : la convalescence lève le coma stable.
            if ((IsStabilized || ActiveStatus.HasFlag(StatusEffect.Inconscient)) && !IsDead)
            {
                RemoveStatus(StatusEffect.Inconscient);
                IsStabilized = false;
                result.WokeUp = true;
            }

            if (CurrentHealth > (int)Math.Ceiling(MaxHealth * 0.28f)) HasTestedMorale = false;
            else HasTestedMorale = true;

            string rationTxt = wellFed ? $"repu (+{rationBonus})" : (neglected ? $"négligé ({neglectPenalty})" : "partiel (+0)");
            string magicTxt = accelerated ? $", magie/arcanotech ×{magicMult}" : string.Empty;
            result.Log = $"🌙 <b>CONVALESCENCE</b> : {Name} +{result.Healed} PV (base CON+Endurance {baseAmount}, {rationTxt}{magicTxt}){(result.WokeUp ? " ➔ <b>RÉVEILLÉ</b> (Inconscient stable levé)" : "")}.";
            return result;
        }
    }
}

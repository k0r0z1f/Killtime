#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using NUnit.Framework;
using Killtime.Core.Character;
using Killtime.Core.Combat;
using Killtime.Core.Dice;
using Killtime.Core.Rules;

namespace Killtime.Tests
{
    /// <summary>
    /// RD-205 : Stabilisation Agonisant + convalescence + rations (Livre VI §25).
    /// - Stabilisation : Premiers Soins au chevet (3 PA, Difficulté 12) : Agonisant
    ///   → Inconscient stable (Saignement stoppé, moral verrouillé, réessayable).
    /// - Convalescence : 1 nuit sûre = CON + rang Endurance ; magie/arcanotech ×2 ;
    ///   rations (bonus repu, malus négligé, partiel = base — réversible).
    /// - Séquelle résurrection RD-083 non rejouée ici (cf. SouffrantMajorCareTests).
    /// Pur Core, sans MonoBehaviour.
    /// </summary>
    [TestFixture]
    public class StabilizationConvalescenceTests
    {
        private int _savedCost;
        private int _savedDC;
        private int _savedBonus;
        private int _savedPenalty;
        private int _savedMagic;

        [SetUp]
        public void SaveConfig()
        {
            var cfg = CoreRulesConfig.Instance;
            _savedCost = cfg.StabilizationAPCost;
            _savedDC = cfg.StabilizationDifficulty;
            _savedBonus = cfg.ConvalescenceRationBonus;
            _savedPenalty = cfg.ConvalescenceNeglectPenalty;
            _savedMagic = cfg.ConvalescenceMagicMultiplier;
            cfg.StabilizationAPCost = 3;
            cfg.StabilizationDifficulty = 12;
            cfg.ConvalescenceRationBonus = 2;
            cfg.ConvalescenceNeglectPenalty = -3;
            cfg.ConvalescenceMagicMultiplier = 2;
        }

        [TearDown]
        public void RestoreConfig()
        {
            var cfg = CoreRulesConfig.Instance;
            cfg.StabilizationAPCost = _savedCost;
            cfg.StabilizationDifficulty = _savedDC;
            cfg.ConvalescenceRationBonus = _savedBonus;
            cfg.ConvalescenceNeglectPenalty = _savedPenalty;
            cfg.ConvalescenceMagicMultiplier = _savedMagic;
            DiceRoller.ClearDevRig();
        }

        private static CharacterStats MakeStats(string name, int con = 4, int hp = 10, int enduranceTraining = 0)
        {
            var attr = new Attributes(4, 4, con, 3, 3, 2, 2, 2);
            var sheet = new CharacterSheet
            {
                Name = name,
                BaseAttributes = attr,
                UnlockedSpecializations = new System.Collections.Generic.List<string>()
            };
            if (enduranceTraining > 0)
                sheet.GetSkill(SkillType.EndurancePhysique).TrainingLevel = enduranceTraining;
            var stats = sheet.ToCombatStats();
            stats.Sheet = sheet;
            stats.CurrentHealth = hp;
            return stats;
        }

        private static CharacterStats MakeAgonisant(string name, int con = 4, int hp = 5, bool bleeding = true)
        {
            var stats = MakeStats(name, con, hp);
            stats.ApplyStatus(StatusEffect.Agonisant, -1);
            if (bleeding) stats.ApplyResidualDamage(StatusEffect.Saignement, 3);
            return stats;
        }

        [Test]
        public void Config_Getters_ReturnCodexDefaults_AndMigrateZero()
        {
            var stats = MakeStats("Règle");
            Assert.AreEqual(3, stats.GetStabilizationAPCost());
            Assert.AreEqual(12, stats.GetStabilizationDifficulty());

            CoreRulesConfig.Instance.StabilizationAPCost = 0;
            CoreRulesConfig.Instance.StabilizationDifficulty = 0;
            Assert.AreEqual(3, stats.GetStabilizationAPCost(), "Migration : 0 = ancienne sauvegarde → défaut Codex 3 PA.");
            Assert.AreEqual(12, stats.GetStabilizationDifficulty(), "Migration : 0 = ancienne sauvegarde → défaut Codex 12.");
        }

        [Test]
        public void Stabilize_NotAgonisant_NotAttempted()
        {
            var patient = MakeStats("Calme");
            var soignant = MakeStats("Doc");
            var res = patient.TryStabilize(soignant, new DiceRoller(1));
            Assert.IsFalse(res.Attempted);
            Assert.IsFalse(res.Succeeded);
            Assert.IsFalse(patient.ActiveStatus.HasFlag(StatusEffect.Inconscient));
            Assert.IsFalse(patient.IsStabilized);
        }

        [Test]
        public void Stabilize_Dead_NotAttempted()
        {
            var patient = MakeAgonisant("Cadavre");
            patient.IsDead = true;
            var soignant = MakeStats("Doc");
            var res = patient.TryStabilize(soignant, new DiceRoller(2));
            Assert.IsFalse(res.Attempted);
            Assert.IsFalse(patient.IsStabilized);
        }

        [Test]
        public void Stabilize_Success_ForceCrit_BecomesStableUnconscious()
        {
            var patient = MakeAgonisant("Mourant");
            var soignant = MakeStats("Doc");
            DiceRoller.DevForceCritical = true;
            var res = patient.TryStabilize(soignant, new DiceRoller(3));
            Assert.IsTrue(res.Attempted);
            Assert.IsTrue(res.HadAgonisant);
            Assert.IsTrue(res.Succeeded);
            Assert.AreEqual(12, res.Difficulty);
            Assert.IsFalse(patient.ActiveStatus.HasFlag(StatusEffect.Agonisant));
            Assert.IsFalse(patient.ActiveStatus.HasFlag(StatusEffect.Saignement));
            Assert.AreEqual(0, patient.GetResidualDamage(StatusEffect.Saignement));
            Assert.IsTrue(patient.ActiveStatus.HasFlag(StatusEffect.Inconscient));
            Assert.IsTrue(patient.IsStabilized);
            Assert.IsTrue(patient.HasTestedMorale);
            StringAssert.Contains("STABILISATION RÉUSSIE", res.Log);
        }

        [Test]
        public void Stabilize_Failure_ForceFumble_KeepsAgonisant()
        {
            var patient = MakeAgonisant("Malchanceux");
            var soignant = MakeStats("Doc");
            DiceRoller.DevForceFumble = true;
            var res = patient.TryStabilize(soignant, new DiceRoller(4));
            Assert.IsTrue(res.Attempted);
            Assert.IsFalse(res.Succeeded);
            Assert.IsTrue(patient.ActiveStatus.HasFlag(StatusEffect.Agonisant));
            Assert.IsFalse(patient.ActiveStatus.HasFlag(StatusEffect.Inconscient));
            Assert.IsFalse(patient.IsStabilized);
            StringAssert.Contains("ÉCHOUÉE", res.Log);
        }

        [Test]
        public void Stabilize_StopsMoraleLoop()
        {
            var patient = MakeAgonisant("Paniqué", hp: 3);
            var soignant = MakeStats("Doc");
            DiceRoller.DevForceCritical = true;
            patient.TryStabilize(soignant, new DiceRoller(5));
            // Inconscient coupe IsAlive : plus aucun jet de moral ne se déclenche.
            var moral = patient.CheckMoraleAtThreshold(new DiceRoller(6), 6);
            Assert.IsFalse(moral.Triggered);
            Assert.IsFalse(patient.IsAlive);
        }

        [Test]
        public void Convalescence_Base_EqualsConPlusEnduranceRank()
        {
            // CON 4 + FOR 4 → rang Endurance (4+4+1)/2 = 4 → base 8.
            var stats = MakeStats("Marcheur", con: 4);
            Assert.AreEqual(8, stats.GetConvalescenceBaseAmount());

            var trained = MakeStats("Athlète", con: 4, enduranceTraining: 2);
            Assert.AreEqual(10, trained.GetConvalescenceBaseAmount());
        }

        [Test]
        public void Convalescence_PartialRation_HealsBase()
        {
            var stats = MakeStats("Campagnard", con: 4, hp: 5);
            var res = stats.ApplySafeNightRest(hasMeal: true, hasWater: false);
            Assert.AreEqual(8, res.BaseAmount);
            Assert.AreEqual(0, res.RationAdjustment);
            Assert.AreEqual(8, res.Amount);
            Assert.AreEqual(8, res.Healed);
            Assert.AreEqual(13, stats.CurrentHealth);
        }

        [Test]
        public void Convalescence_WellFed_Bonus()
        {
            var stats = MakeStats("Repu", con: 4, hp: 5);
            var res = stats.ApplySafeNightRest(hasMeal: true, hasWater: true);
            Assert.AreEqual(2, res.RationAdjustment);
            Assert.AreEqual(10, res.Amount);
            Assert.AreEqual(15, stats.CurrentHealth);
        }

        [Test]
        public void Convalescence_Neglected_Malus_ButReversible()
        {
            var stats = MakeStats("Affamé", con: 4, hp: 5);
            var bad = stats.ApplySafeNightRest(hasMeal: false, hasWater: false);
            Assert.AreEqual(-3, bad.RationAdjustment);
            Assert.AreEqual(5, bad.Amount);
            Assert.AreEqual(10, stats.CurrentHealth);

            // Réversible : la nuit suivante avec rations soigne plein pot, sans séquelle.
            var good = stats.ApplySafeNightRest(hasMeal: true, hasWater: true);
            Assert.AreEqual(10, good.Amount);
            Assert.AreEqual(20, stats.CurrentHealth, "Plafonné au MaxHealth, aucune trace du malus précédent.");
        }

        [Test]
        public void Convalescence_Magic_DoublesTotal()
        {
            var stats = MakeStats("Enchanté", con: 4, hp: 2);
            var res = stats.ApplySafeNightRest(hasMeal: true, hasWater: true, magicBoost: true);
            Assert.IsTrue(res.Accelerated);
            Assert.AreEqual(2, res.MagicMultiplier);
            Assert.AreEqual(20, res.Amount, "(8 + 2) × 2");
            Assert.AreEqual(20, stats.CurrentHealth, "Plafond MaxHealth (CON 4 × 5).");
        }

        [Test]
        public void Convalescence_WakesStabilized_AndKeepsSouffrant()
        {
            var patient = MakeAgonisant("Stabilisé", hp: 5);
            var soignant = MakeStats("Doc");
            DiceRoller.DevForceCritical = true;
            patient.TryStabilize(soignant, new DiceRoller(7));
            Assert.IsTrue(patient.IsStabilized);

            patient.ApplyStatus(StatusEffect.Souffrant);
            var res = patient.ApplySafeNightRest(hasMeal: true, hasWater: true);
            Assert.IsTrue(res.WokeUp);
            Assert.IsFalse(patient.ActiveStatus.HasFlag(StatusEffect.Inconscient));
            Assert.IsFalse(patient.IsStabilized);
            Assert.IsTrue(patient.ActiveStatus.HasFlag(StatusEffect.Souffrant),
                "La convalescence ne referme pas la blessure critique (Soins Majeurs requis, RD-083).");
            Assert.Greater(patient.CurrentHealth, 5);
        }

        [Test]
        public void Convalescence_Dead_NoEffect()
        {
            var stats = MakeStats("Défunt", hp: 0);
            stats.IsDead = true;
            var res = stats.ApplySafeNightRest(true, true, true);
            Assert.AreEqual(0, res.Healed);
            Assert.IsFalse(res.WokeUp);
        }

        [Test]
        public void RationNameHelpers_MatchCatalog()
        {
            Assert.IsTrue(CharacterStats.IsMealRationName("Meal Pack"));
            Assert.IsTrue(CharacterStats.IsWaterRationName("Water Blob 1L"));
            Assert.IsTrue(CharacterStats.IsMealRationName("Meal Pack + Water"));
            Assert.IsTrue(CharacterStats.IsWaterRationName("Meal Pack + Water"));
            Assert.IsFalse(CharacterStats.IsMealRationName("Seringue de soins 5 PV"));
            Assert.IsFalse(CharacterStats.IsWaterRationName("Bandage Compressif"));
        }
    }
}
#endif

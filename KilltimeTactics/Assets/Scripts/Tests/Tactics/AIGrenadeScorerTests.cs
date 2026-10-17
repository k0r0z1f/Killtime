#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using NUnit.Framework;

namespace Killtime.Tactics.AI.Tests
{
    /// <summary>
    /// RD-106 : non-régression du scoring des grenades utilitaires IA.
    /// Pur EditMode : seul AIGrenadeScorer est exercé (aucune scène/arène).
    /// Seuils : létal 18 (historique, inchangé), utilitaire 12.
    /// </summary>
    [TestFixture]
    public class AIGrenadeScorerTests
    {
        // ---- Létal : formule historique verbatim ----

        [Test]
        public void Lethal_OneFoe_LightGrenade_ReachesLethalThreshold()
        {
            // Grenade Légère 1d10 : 10 + 1×5.5 = 15.5 ; 1×(10+15.5) = 25.5 ≥ 18.
            float s = AIGrenadeScorer.ScoreLethal(1, 15.5f, 0, false);
            Assert.AreEqual(25.5f, s, 1e-4f);
            Assert.GreaterOrEqual(s, AIGrenadeScorer.LethalPlayThreshold);
        }

        [Test]
        public void Lethal_FinishingBlow_Adds10()
        {
            Assert.AreEqual(
                AIGrenadeScorer.ScoreLethal(1, 15.5f, 0, false) + 10f,
                AIGrenadeScorer.ScoreLethal(1, 15.5f, 0, true), 1e-4f);
        }

        // ---- Fumigène : écran avant repli / avance à découvert ----

        [Test]
        public void Smoke_RetreatLowHp_SingleFoe_Plays()
        {
            // 1 bloqué×7 + 1 blast×4 + repli 6 + PV<50% 4 = 21 ≥ 12.
            float s = AIGrenadeScorer.ScoreSmoke(1, 1, 0,
                TacticalPosture.TacticalRetreat, 0.3f);
            Assert.AreEqual(21f, s, 1e-4f);
            Assert.GreaterOrEqual(s, AIGrenadeScorer.UtilityPlayThreshold);
        }

        [Test]
        public void Smoke_NoThreat_Peacetime_Skips()
        {
            float s = AIGrenadeScorer.ScoreSmoke(0, 0, 0,
                TacticalPosture.CalculatedStrike, 1f);
            Assert.AreEqual(0f, s, 1e-4f);
            Assert.Less(s, AIGrenadeScorer.UtilityPlayThreshold);
        }

        [Test]
        public void Smoke_AlreadySmoked_NoNewBlock_Skips()
        {
            // Ennemi dans le souffle mais vue déjà coupée, sans contexte : 4 < 12.
            float s = AIGrenadeScorer.ScoreSmoke(0, 1, 0,
                TacticalPosture.CalculatedStrike, 1f);
            Assert.AreEqual(4f, s, 1e-4f);
            Assert.Less(s, AIGrenadeScorer.UtilityPlayThreshold);
        }

        [Test]
        public void Smoke_AlliesInBlast_SoftPenalty_NotBlocked()
        {
            // La fumée ne blesse pas : 1 allié dedans = -3 seulement, ça joue encore.
            float s = AIGrenadeScorer.ScoreSmoke(1, 1, 1,
                TacticalPosture.TacticalRetreat, 0.3f);
            Assert.AreEqual(18f, s, 1e-4f);
            Assert.GreaterOrEqual(s, AIGrenadeScorer.UtilityPlayThreshold);
        }

        // ---- Flash : Aveugle (-4 tir) avant assaut ----

        [Test]
        public void Flash_TwoFoes_Plays()
        {
            // M84 (2 dmg) : 2×9 + 2 + assaut 6 = 26 ≥ 12.
            float s = AIGrenadeScorer.ScoreFlash(2, 2f, TacticalPosture.CalculatedStrike);
            Assert.AreEqual(26f, s, 1e-4f);
            Assert.GreaterOrEqual(s, AIGrenadeScorer.UtilityPlayThreshold);
        }

        [Test]
        public void Flash_SingleFoe_Peacetime_Skips_AssaultPlays()
        {
            // 1×9 + 2 = 11 < 12 : on garde le stock hors assaut.
            float calm = AIGrenadeScorer.ScoreFlash(1, 2f, TacticalPosture.SupportAndHeal);
            Assert.AreEqual(11f, calm, 1e-4f);
            Assert.Less(calm, AIGrenadeScorer.UtilityPlayThreshold);
            // Même cas avant assaut : +6 = 17 ≥ 12.
            float assault = AIGrenadeScorer.ScoreFlash(1, 2f, TacticalPosture.FlankAndSuppress);
            Assert.AreEqual(17f, assault, 1e-4f);
            Assert.GreaterOrEqual(assault, AIGrenadeScorer.UtilityPlayThreshold);
        }

        // ---- Gaz persistant : DoT × tours, masque allié vérifié ----

        [Test]
        public void Gas_TwoFoes_MaskedAlliesIgnored_Plays()
        {
            // CM-6 (2+1d10=7.5, 2 DoT×2 tours) : 2×(10+7.5+16) = 67 ≥ 12.
            float s = AIGrenadeScorer.ScorePersistent(2, 7.5f, 2, 2, false, 0, false);
            Assert.AreEqual(67f, s, 1e-4f);
            Assert.GreaterOrEqual(s, AIGrenadeScorer.UtilityPlayThreshold);
        }

        [Test]
        public void Gas_ExposedAlly_OutweighsSingleFoe()
        {
            // 1×33.5 − 1×25 = 8.5 < 12 (le contrôleur bloque déjà avant, hors AllIn).
            float s = AIGrenadeScorer.ScorePersistent(1, 7.5f, 2, 2, false, 1, false);
            Assert.AreEqual(8.5f, s, 1e-4f);
            Assert.Less(s, AIGrenadeScorer.UtilityPlayThreshold);
        }

        [Test]
        public void Stase_HardControl_BonusApplies()
        {
            // T-0 (0+1d10=5.5, Paralyse 2 tours) : 15.5 + 6 = 21.5 ≥ 12.
            Assert.IsTrue(AIGrenadeScorer.HasHardControl("Paralyse,ChronoFracture"));
            float s = AIGrenadeScorer.ScorePersistent(1, 5.5f, 0, 2, true, 0, false);
            Assert.AreEqual(21.5f, s, 1e-4f);
            Assert.GreaterOrEqual(s, AIGrenadeScorer.UtilityPlayThreshold);
        }

        [Test]
        public void HasHardControl_OnlyParalyseOrImmobilise()
        {
            Assert.IsTrue(AIGrenadeScorer.HasHardControl("Ralenti,Immobilise"));
            Assert.IsFalse(AIGrenadeScorer.HasHardControl("Aveugle,Asphyxie,Etourdi"));
            Assert.IsFalse(AIGrenadeScorer.HasHardControl(""));
            Assert.IsFalse(AIGrenadeScorer.HasHardControl(null));
        }

        // ---- Classification ----

        [Test]
        public void Classify_Families()
        {
            Assert.IsTrue(AIGrenadeScorer.IsSmoke("Fumigene"));
            Assert.IsFalse(AIGrenadeScorer.IsSmoke("Flash"));
            Assert.IsTrue(AIGrenadeScorer.IsFlashKind("Flash"));
            Assert.IsFalse(AIGrenadeScorer.IsFlashKind("Gaz"));
            Assert.IsTrue(AIGrenadeScorer.IsPersistent("Gaz", 2, 2));
            Assert.IsTrue(AIGrenadeScorer.IsPersistent("Cryo", 1, 2));
            Assert.IsFalse(AIGrenadeScorer.IsPersistent("Fumigene", 2, 2)); // fumée d'abord
            Assert.IsFalse(AIGrenadeScorer.IsPersistent("Flash", 0, 2)); // instantané
            Assert.IsFalse(AIGrenadeScorer.IsPersistent("Fragmentation", 0, 2));
        }
    }
}
#endif

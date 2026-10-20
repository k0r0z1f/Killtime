#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using NUnit.Framework;
using Killtime.Core.Character;
using Killtime.Core.Combat;
using Killtime.Core.Dice;

namespace Killtime.Tests
{
    /// <summary>
    /// RD-203 Réussites automatiques par rang (Livre II §7.2).
    /// Le « rang » du Livre = rang de compétence effectif = base (caracs) + entraînements,
    /// soit CharacterStats.GetSkillRank. Bandes : Débutant 1-3 (auto 0), Initié 4-5 (auto 4),
    /// Avancé 6-7 (auto 6), Expert 8-9 (auto 8), Maître 10+ (auto 10).
    /// Si le seuil est couvert par le rang : pas de jet. Sinon : jet normal inchangé.
    /// </summary>
    [TestFixture]
    public class AutoSuccessTests
    {
        private static CharacterStats MakeObserver(string name, int vision, int instinct, int training)
        {
            // Observation : base = (Vision + Instinct + 1) / 2 (division entière).
            var attr = new Attributes(4, 4, 3, 3, 3, 2, 2, instinct, 0, vision, 3);
            var sheet = new CharacterSheet { Name = name, BaseAttributes = attr };
            sheet.GetSkill(SkillType.Observation).TrainingLevel = training;
            var stats = sheet.ToCombatStats();
            stats.Sheet = sheet;
            return stats;
        }

        [TearDown]
        public void TearDown() => DiceRoller.ClearDevRig();

        [Test]
        public void RankMapsToMasteryBands_LivreIISection72()
        {
            Assert.AreEqual(SkillRank.Debutant, SkillAutoSuccess.ForSkillRank(-1));
            Assert.AreEqual(SkillRank.Debutant, SkillAutoSuccess.ForSkillRank(0));
            Assert.AreEqual(SkillRank.Debutant, SkillAutoSuccess.ForSkillRank(1));
            Assert.AreEqual(SkillRank.Debutant, SkillAutoSuccess.ForSkillRank(3));
            Assert.AreEqual(SkillRank.Initie, SkillAutoSuccess.ForSkillRank(4));
            Assert.AreEqual(SkillRank.Initie, SkillAutoSuccess.ForSkillRank(5));
            Assert.AreEqual(SkillRank.Avance, SkillAutoSuccess.ForSkillRank(6));
            Assert.AreEqual(SkillRank.Avance, SkillAutoSuccess.ForSkillRank(7));
            Assert.AreEqual(SkillRank.Expert, SkillAutoSuccess.ForSkillRank(8));
            Assert.AreEqual(SkillRank.Expert, SkillAutoSuccess.ForSkillRank(9));
            Assert.AreEqual(SkillRank.Maitre, SkillAutoSuccess.ForSkillRank(10));
            Assert.AreEqual(SkillRank.Maitre, SkillAutoSuccess.ForSkillRank(14));
        }

        [Test]
        public void AutoThresholds_MatchLivreIISection7()
        {
            Assert.AreEqual(0, SkillAutoSuccess.AutoThresholdFor(3));
            Assert.AreEqual(4, SkillAutoSuccess.AutoThresholdFor(4));
            Assert.AreEqual(4, SkillAutoSuccess.AutoThresholdFor(5));
            Assert.AreEqual(6, SkillAutoSuccess.AutoThresholdFor(6));
            Assert.AreEqual(6, SkillAutoSuccess.AutoThresholdFor(7));
            Assert.AreEqual(8, SkillAutoSuccess.AutoThresholdFor(8));
            Assert.AreEqual(8, SkillAutoSuccess.AutoThresholdFor(9));
            Assert.AreEqual(10, SkillAutoSuccess.AutoThresholdFor(10));
            Assert.AreEqual(10, SkillAutoSuccess.AutoThresholdFor(13));
        }

        [Test]
        public void Bornes_SeuilEgalAuto_Succes_SeuilAutoPlus1_JetNormal(
            [Values(1, 2, 3, 4, 5, 6, 7, 8, 9, 10)] int rank)
        {
            int auto = SkillAutoSuccess.AutoThresholdFor(rank);
            Assert.IsTrue(SkillAutoSuccess.IsAutoSuccess(rank, auto),
                $"rang {rank} : seuil == auto ({auto}) doit réussir sans jet");
            if (auto > 0)
                Assert.IsTrue(SkillAutoSuccess.IsAutoSuccess(rank, 1),
                    $"rang {rank} : seuil 1 couvert par auto {auto}");
            Assert.IsFalse(SkillAutoSuccess.IsAutoSuccess(rank, auto + 1),
                $"rang {rank} : seuil auto+1 ({auto + 1}) doit passer par un jet normal");
        }

        [Test]
        public void Debutant_Rank3_ZeroAuto_OneNeedsRoll()
        {
            Assert.IsTrue(SkillAutoSuccess.IsAutoSuccess(3, 0));
            Assert.IsFalse(SkillAutoSuccess.IsAutoSuccess(3, 1));
        }

        [Test]
        public void ResolveSkillCheck_InitieRank4_AutoSkipsRoll_EvenOnForcedFumble()
        {
            // Base 3 (vision 3 + instinct 2) + 1 entraînement = rang 4 → Initié, auto 4.
            // Le fumble forcé (dev-only) ne peut pas s'appliquer puisqu'aucun dé n'est lancé.
            var user = MakeObserver("Initiee", 3, 2, 1);
            Assert.AreEqual(4, user.GetSkillRank(SkillType.Observation, false));
            var calc = new CombatCalculator(new DiceRoller(7));
            DiceRoller.DevForceFumble = true;

            var res = calc.ResolveSkillCheck(user, SkillType.Observation, 4);

            Assert.IsTrue(res.IsSuccess);
            Assert.IsFalse(res.IsCriticalFailure);
            Assert.IsFalse(res.IsCriticalSuccess);
            Assert.AreEqual(0, res.RawRoll, "pas de jet : brut sentinelle 0");
            Assert.AreEqual(4, res.Total);
            Assert.AreEqual(4, res.TargetDC);
        }

        [Test]
        public void ResolveSkillCheck_AboveAuto_RollPreserved_FumbleFails_CritSucceeds()
        {
            // Rang 4, auto 4 : SD 5 passe par le jet normal, règle inchangée.
            var user = MakeObserver("Initiee", 3, 2, 1);
            var calc = new CombatCalculator(new DiceRoller(7));

            DiceRoller.DevForceFumble = true;
            var failed = calc.ResolveSkillCheck(user, SkillType.Observation, 5);
            Assert.IsFalse(failed.IsSuccess);
            Assert.IsTrue(failed.IsCriticalFailure);

            DiceRoller.DevForceFumble = false;
            DiceRoller.DevForceCritical = true;
            var crit = calc.ResolveSkillCheck(user, SkillType.Observation, 5);
            Assert.IsTrue(crit.IsSuccess);
            Assert.IsTrue(crit.IsCriticalSuccess);
        }

        [Test]
        public void ResolveSkillCheck_GiftedUntrained_AvanceAuto_DespiteZeroTraining()
        {
            // NON-RÉGRESSION : le rang compte la base. Vision 6 + instinct 5 → base 6,
            // 0 entraînement = rang 6 → Avancé, auto 6. L'ancien code (entraînement seul)
            // classait ce doué « Débutant » et lui imposait un jet sur SD 6.
            var user = MakeObserver("Douee", 6, 5, 0);
            Assert.AreEqual(6, user.GetSkillRank(SkillType.Observation, false));
            var calc = new CombatCalculator(new DiceRoller(7));
            DiceRoller.DevForceFumble = true;

            var res = calc.ResolveSkillCheck(user, SkillType.Observation, 6);

            Assert.IsTrue(res.IsSuccess, "SD 6 ≤ auto 6 du rang Avancé : pas de jet malgré le fumble forcé");
            Assert.AreEqual(0, res.RawRoll);
        }

        [Test]
        public void ResolveSkillCheck_WeakBaseOvertrained_IsInitie_NotExpert()
        {
            // NON-RÉGRESSION : 3 entraînements sur une base 1 ne font pas un Expert.
            // Base 2 (vision remontée au minimum d'espèce 3 + instinct 1 : (3+1+1)/2)
            // + 3 = rang 5 → Initié, auto 4.
            // L'ancien code accordait auto 8 (Expert) : succès gratuit sur seuil Formidable.
            var user = MakeObserver("Bosseuse", 3, 1, 3);
            Assert.AreEqual(5, user.GetSkillRank(SkillType.Observation, false));
            var calc = new CombatCalculator(new DiceRoller(7));

            DiceRoller.DevForceFumble = true;
            Assert.IsTrue(calc.ResolveSkillCheck(user, SkillType.Observation, 4).IsSuccess,
                "SD 4 ≤ auto 4 : succès sans jet malgré le fumble forcé");
            Assert.IsFalse(calc.ResolveSkillCheck(user, SkillType.Observation, 8).IsSuccess,
                "SD 8 > auto 4 : jet normal, le fumble forcé fait échouer");
        }

        [Test]
        public void AutoSuccessLog_TracesRankRankNumberThresholdAndAuto()
        {
            string log = SkillAutoSuccess.BuildAutoSuccessLog("Observation", 8, 8);
            StringAssert.Contains("Expert", log);
            StringAssert.Contains("8", log);
            StringAssert.Contains("Livre II §7", log);
            StringAssert.Contains("pas de jet", log);
        }

        [Test]
        public void RankLabels_French()
        {
            Assert.AreEqual("Débutant", SkillAutoSuccess.RankLabel(SkillRank.Debutant));
            Assert.AreEqual("Initié", SkillAutoSuccess.RankLabel(SkillRank.Initie));
            Assert.AreEqual("Avancé", SkillAutoSuccess.RankLabel(SkillRank.Avance));
            Assert.AreEqual("Expert", SkillAutoSuccess.RankLabel(SkillRank.Expert));
            Assert.AreEqual("Maître", SkillAutoSuccess.RankLabel(SkillRank.Maitre));
        }
    }
}
#endif

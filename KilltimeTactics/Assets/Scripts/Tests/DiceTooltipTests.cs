#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using NUnit.Framework;
using Killtime.Core.Character;
using Killtime.Core.Dice;

namespace Killtime.Tests
{
    /// <summary>
    /// Couvre le tooltip des dés du feed : décomposition du calcul
    /// (rang de base, paliers, entraînements, total) et parsing des lignes de log.
    /// </summary>
    [TestFixture]
    public class DiceTooltipTests
    {
        [Test]
        public void TestDescribeBaseRank_MonoStat()
        {
            var attr = new Attributes(4, 5, 3, 3, 3, 2, 2, 2);
            Assert.AreEqual("AGI 5", SkillDefinitions.DescribeBaseRank(SkillType.ArmesPercantes, attr, true));
            Assert.AreEqual("AGI 5", SkillDefinitions.DescribeBaseRank(SkillType.Ballistique, attr, true));
            Assert.AreEqual("FOR 4", SkillDefinitions.DescribeBaseRank(SkillType.ArmesContondantes, attr, true));
        }

        [Test]
        public void TestDescribeBaseRank_AveragedStats()
        {
            var attr = new Attributes(4, 6, 3, 4, 3, 2, 2, 2);
            Assert.AreEqual("(AGI 6+RAP 4)/2=5",
                SkillDefinitions.DescribeBaseRank(SkillType.Esquive, attr, false));
            Assert.AreEqual("(FOR 4+AGI 6)/2=5",
                SkillDefinitions.DescribeBaseRank(SkillType.MainsNues, attr, true));
        }

        [Test]
        public void TestDescribeBaseRank_MagicAugmentation()
        {
            var attr = new Attributes(4, 6, 3, 3, 3, 2, 2, 2, 5);
            Assert.AreEqual("MAG 5 ★",
                SkillDefinitions.DescribeBaseRank(SkillType.MainsNues, attr, true));
            // Défense : pas d'augmentation, FOR/AGI conservés.
            Assert.AreEqual("(FOR 4+AGI 6)/2=5",
                SkillDefinitions.DescribeBaseRank(SkillType.MainsNues, attr, false));
        }

        [Test]
        public void TestBreakdownSteps_AddUpToLoggedDie()
        {
            // Roger : AGI 5 → +2 paliers, Armes Perçantes +1 entraînement → total 3 → D8.
            var attr = new Attributes(4, 5, 3, 3, 3, 2, 2, 2);
            var sheet = new CharacterSheet { Name = "Roger", BaseAttributes = attr };
            sheet.GetSkill(SkillType.ArmesPercantes).TrainingLevel = 1;
            var stats = sheet.ToCombatStats();

            int rank = SkillDefinitions.GetBaseRank(SkillType.ArmesPercantes, attr, true);
            int steps = SkillDefinitions.CharacteristicSteps(rank);
            int training = stats.Sheet.GetSkill(SkillType.ArmesPercantes).TrainingLevel;
            Assert.AreEqual(5, rank);
            Assert.AreEqual(2, steps);
            Assert.AreEqual(1, training);
            Assert.AreEqual(DiceType.D8, stats.GetSkillDie(SkillType.ArmesPercantes, true));
            Assert.AreEqual(DiceType.D8, SkillDefinitions.DieFromTotalSteps(steps + training));
        }

        [Test]
        public void TestTryParseDisplayName_LogForms()
        {
            Assert.IsTrue(SkillDefinitions.TryParseDisplayName("Armes Perçantes", out var s1));
            Assert.AreEqual(SkillType.ArmesPercantes, s1);
            Assert.IsTrue(SkillDefinitions.TryParseDisplayName("Ballistique / Tir", out var s2));
            Assert.AreEqual(SkillType.Ballistique, s2);
            Assert.IsTrue(SkillDefinitions.TryParseDisplayName("Esquive", out var s3));
            Assert.AreEqual(SkillType.Esquive, s3);
            Assert.IsTrue(SkillDefinitions.TryParseDisplayName("Défense Corporelle", out var s4));
            Assert.AreEqual(SkillType.DefenseCorporelle, s4);
            Assert.IsFalse(SkillDefinitions.TryParseDisplayName("Différentiel Net", out _));
        }

        [Test]
        public void TestDiceLogParser_HitLine()
        {
            string line = "TOUCHÉ CHIRURGICAL : Roger (Opératif) ➔ Mina (Frappe sur Torse)";
            var parties = DiceLogParser.ParseParties(line);
            Assert.IsTrue(parties.HasParties);
            Assert.AreEqual("Roger (Opératif)", parties.Attacker);
            Assert.AreEqual("Mina", parties.Defender);

            var mentions = DiceLogParser.ParseMentions(
                "Attaque Armes Perçantes : D6 [Tirage 5] vs Défense Esquive : D4 [Tirage 4]");
            Assert.AreEqual(2, mentions.Count);
            Assert.AreEqual(DieMentionRole.Attack, mentions[0].Role);
            Assert.AreEqual("Armes Perçantes", mentions[0].SkillName);
            Assert.AreEqual(DiceType.D6, mentions[0].Die);
            Assert.AreEqual(DieMentionRole.Defense, mentions[1].Role);
            Assert.AreEqual(DiceType.D4, mentions[1].Die);
        }

        [Test]
        public void TestDiceLogParser_NoContextLines()
        {
            Assert.AreEqual(0, DiceLogParser.ParseMentions("Jet de Nytharite : 1d10 [Tirage 4]").Count);
            Assert.AreEqual(0, DiceLogParser.ParseMentions("Requis pour 2 attaques : palier 2d6, 2d8.").Count);
            Assert.IsFalse(DiceLogParser.ParseParties("Engagement : Mina (PA: 11/11)").HasParties);
        }

        [Test]
        public void TestDiceLogParser_TrailingMention()
        {
            Assert.IsTrue(DiceLogParser.TryParseTrailingMention("Attaque Armes Perçantes : ",
                out var r1, out var s1));
            Assert.AreEqual(DieMentionRole.Attack, r1);
            Assert.AreEqual("Armes Perçantes", s1);

            // Dernier marqueur gagne : le dé appartient à la défense.
            Assert.IsTrue(DiceLogParser.TryParseTrailingMention(
                "Attaque Armes Perçantes : D6 [Tirage 5] vs Défense Esquive : ",
                out var r2, out var s2));
            Assert.AreEqual(DieMentionRole.Defense, r2);
            Assert.AreEqual("Esquive", s2);

            // Marqueur inclus dans le nom de la compétence.
            Assert.IsTrue(DiceLogParser.TryParseTrailingMention("Défense Défense Corporelle : ",
                out var r3, out var s3));
            Assert.AreEqual(DieMentionRole.Defense, r3);
            Assert.AreEqual("Défense Corporelle", s3);

            Assert.IsFalse(DiceLogParser.TryParseTrailingMention("Compétences : ",
                out _, out _));
            Assert.IsFalse(DiceLogParser.TryParseTrailingMention("Défense passive, aucune parade",
                out _, out _));
        }

        [Test]
        public void TestDiceLogParser_TrailingPhaseRole()
        {
            Assert.IsTrue(DiceLogParser.TryParseTrailingPhaseRole(
                "Phase 1 — Attaque : Roger (Opératif) désigne Mina (Torse), paie 2 PA, lance ",
                out var r1));
            Assert.AreEqual(DieMentionRole.Attack, r1);
            Assert.IsTrue(DiceLogParser.TryParseTrailingPhaseRole(
                "Phase 2 — Défense : Mina paie 1 PA, lance ",
                out var r2));
            Assert.AreEqual(DieMentionRole.Defense, r2);
            Assert.IsFalse(DiceLogParser.TryParseTrailingPhaseRole(
                "Attaque Armes Perçantes : ", out _));
        }
    }
}
#endif

using NUnit.Framework;
using Killtime.Core.Character;
using Killtime.Core.Dice;
using Killtime.Core.Combat;
using Killtime.Core.Arcanotech;
using Killtime.Core.Chrono;

namespace Killtime.Tests
{
    [TestFixture]
    public class CoreRulesTests
    {
        [Test]
        public void TestRogerAttributesAndActionPoints_MatchesCodexLivreI()
        {
            // Exemple officiel du Livre I (Chapitre 4 / Roger) :
            // Agi 3, Int 2, Rap 3, Con 4, For 3, Cha 1 (Cha est le min)
            var rogerAttr = new Attributes(agi: 3, @int: 2, rap: 3, con: 4, @for: 3, cha: 1);

            // Vérification du calcul des PA : Max(3, 2) + 3 + 1 = 7 PA
            Assert.AreEqual(7, rogerAttr.CalculateBaseActionPoints());

            // Vérification de l'Encaissement : CON × 2 = 4 × 2 = 8
            Assert.AreEqual(8, rogerAttr.CalculateEncaissement());

            // Vérification du Seuil Létal : CON × 5 = 4 × 5 = 20
            Assert.AreEqual(20, rogerAttr.CalculateLethalMaximum());
        }

        [Test]
        public void TestEmergencyBreath_IncreasesActionPointsAndEssoufflement()
        {
            var attr = new Attributes(3, 2, 3, 4, 3, 1);
            var stats = new CharacterStats("Testeur", attr);

            // Dépenser tous ses PA
            stats.ConsumeActionPoints(7);
            Assert.AreEqual(0, stats.CurrentActionPoints);
            Assert.AreEqual(0, stats.Essoufflement);

            // Prendre 1 point d'essoufflement d'urgence pour 2 PA
            bool success = stats.TakeEmergencyBreath(2);
            Assert.IsTrue(success);
            Assert.AreEqual(2, stats.CurrentActionPoints);
            Assert.AreEqual(1, stats.Essoufflement);
        }

        [Test]
        public void TestNythariteSpell_CreationCostEqualsActionPointCost_MatchesLivreIV()
        {
            var spell = new NythariteSpell("Décharge Cinétique", PsychicDiscipline.Telekinesie, PsychicStage.Stade2_AccesProfond, costXpPa: 4);

            Assert.AreEqual(4, spell.CreationXpCost);
            Assert.AreEqual(4, spell.ActionPointCost);
        }

        [Test]
        public void TestTargetedShot_BodyPartsModifiers_MatchesLivreVI()
        {
            var headInfo = BodyPartInfo.GetInfo(BodyPart.Tete);
            Assert.AreEqual(-2, headInfo.DifficultyModifier);
            Assert.AreEqual(3, headInfo.CriticalDamageMultiplier);

            var torsoInfo = BodyPartInfo.GetInfo(BodyPart.Torse);
            Assert.AreEqual(0, torsoInfo.DifficultyModifier);
            Assert.AreEqual(1, torsoInfo.CriticalDamageMultiplier);
        }

        [Test]
        public void TestChronoTimelineSnapshot_AllowsStateRewind_MatchesLivreV()
        {
            var branch = new TimelineBranch(TimelineId.Timeline0_Prime);
            var snap1 = new TacticalTimeSnapshot(round: 1, second: 0.0f, desc: "Début du round");
            var snap2 = new TacticalTimeSnapshot(round: 1, second: 2.5f, desc: "Tir de Lucas");

            branch.PushSnapshot(snap1);
            branch.PushSnapshot(snap2);

            Assert.AreEqual(2, branch.GetFullChronology().Count);

            // Rembobinage de la dernière action (ancrage temporel de Thomas)
            var rewound = branch.RewindLastAction();
            Assert.AreEqual("Début du round", rewound.ActionDescription);
            Assert.AreEqual(1, branch.GetFullChronology().Count);
        }
    }
}

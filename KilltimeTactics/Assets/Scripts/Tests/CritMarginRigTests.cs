#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using NUnit.Framework;
using Killtime.Core.Character;
using Killtime.Core.Combat;
using Killtime.Core.Dice;

namespace Killtime.Tests
{
    /// <summary>
    /// RD-193 Rig critiques / marges : forçage dev-only des critiques par camp,
    /// du différentiel net, de la déviation d6 et des conséquences §8.2.
    /// Pur Core, aucune règle modifiée : tous les flags à false/null = jets live.
    /// Couvre RD-179 (terrain VATS).
    /// </summary>
    [TestFixture]
    public class CritMarginRigTests
    {
        private static CharacterStats MakeStats(string name, int force = 5, int agi = 4, int con = 5, int ap = 6, int hp = 40)
        {
            var attr = new Attributes(force, agi, con, 3, 3, 2, 2, 2);
            var sheet = new CharacterSheet
            {
                Name = name,
                BaseAttributes = attr,
                UnlockedSpecializations = new System.Collections.Generic.List<string>()
            };
            var stats = sheet.ToCombatStats();
            stats.Sheet = sheet;
            stats.CurrentActionPoints = ap;
            stats.CurrentHealth = hp;
            return stats;
        }

        [SetUp]
        public void SetUp()
        {
            DiceRoller.ClearDevRig();
        }

        [TearDown]
        public void TearDown()
        {
            DiceRoller.ClearDevRig();
        }

        [Test]
        public void DevRig_NewFlags_OffByDefault()
        {
            Assert.IsFalse(DiceRoller.DevForceAttackCrit);
            Assert.IsFalse(DiceRoller.DevForceAttackFumble);
            Assert.IsFalse(DiceRoller.DevForceDefenseCrit);
            Assert.IsFalse(DiceRoller.DevForceDefenseFumble);
            Assert.IsNull(DiceRoller.DevForcedDifferential);
            Assert.IsNull(DiceRoller.DevForcedCritConsequenceRaw);
            Assert.IsNull(DiceRoller.DevForcedDeflectionRoll);
        }

        [Test]
        public void DevRig_Clear_RestoresNewFlags()
        {
            DiceRoller.DevForceAttackCrit = true;
            DiceRoller.DevForceDefenseFumble = true;
            DiceRoller.DevForcedDifferential = 10;
            DiceRoller.DevForcedCritConsequenceRaw = 24;
            DiceRoller.DevForcedDeflectionRoll = 1;
            DiceRoller.ClearDevRig();
            Assert.IsFalse(DiceRoller.DevForceAttackCrit);
            Assert.IsFalse(DiceRoller.DevForceDefenseFumble);
            Assert.IsNull(DiceRoller.DevForcedDifferential);
            Assert.IsNull(DiceRoller.DevForcedCritConsequenceRaw);
            Assert.IsNull(DiceRoller.DevForcedDeflectionRoll);
        }

        [Test]
        public void DevForcedResult_Crit_ReturnsMaxAndCritSuccess()
        {
            var r = DiceRoller.DevForcedResult(DiceType.D8, 2, 10, true);
            Assert.AreEqual(8, r.RawRoll);
            Assert.AreEqual(10, r.Total);
            Assert.IsTrue(r.IsCriticalSuccess);
            Assert.IsTrue(r.IsSuccess);
            Assert.IsFalse(r.IsCriticalFailure);
        }

        [Test]
        public void DevForcedResult_Fumble_ReturnsMinAndCritFail()
        {
            var r = DiceRoller.DevForcedResult(DiceType.D6, 0, 10, false);
            Assert.AreEqual(1, r.RawRoll);
            Assert.IsTrue(r.IsCriticalFailure);
            Assert.IsFalse(r.IsSuccess);
        }

        [Test]
        public void DevApplyForcedDifferential_Math()
        {
            Assert.AreEqual(5, DiceRoller.DevApplyForcedDifferential(10, 5));
            Assert.AreEqual(10, DiceRoller.DevApplyForcedDifferential(10, 0));
            Assert.AreEqual(-10, DiceRoller.DevApplyForcedDifferential(10, 20));
        }

        [Test]
        public void CritConsequence_ForcedRaw_MapsToThreshold()
        {
            var roller = new DiceRoller(7);
            DiceRoller.DevForcedCritConsequenceRaw = 24;
            var off = roller.RollCriticalConsequence(DiceType.TwoD12, CriticalEffectCategory.Offensive);
            Assert.AreEqual(24, off.Threshold);
            Assert.AreEqual(24, off.RawRoll);
            StringAssert.Contains("Inconsciente", off.Title);
            var def = roller.RollCriticalConsequence(DiceType.TwoD12, CriticalEffectCategory.Defensive);
            Assert.AreEqual(24, def.Threshold);
            StringAssert.Contains("Désarçonnement", def.Title);
            DiceRoller.DevForcedCritConsequenceRaw = 1;
            var low = roller.RollCriticalConsequence(DiceType.D6, CriticalEffectCategory.Offensive);
            Assert.AreEqual(1, low.Threshold);
        }

        private static CombatCalculator MakeCalc()
        {
            return new CombatCalculator(new DiceRoller(1234));
        }

        [Test]
        public void ForcedAttackCrit_HitsWithCriticalTag()
        {
            DiceRoller.DevForceAttackCrit = true;
            DiceRoller.DevForceDefenseFumble = true;
            var calc = MakeCalc();
            var att = MakeStats("Attaquant");
            var def = MakeStats("Défenseur");
            var res = calc.ResolveTargetedAttack(att, def, BodyPart.Torse,
                DiceType.D8, 0, DiceType.D6, 0, 5);
            Assert.IsTrue(res.IsHit);
            Assert.IsTrue(res.IsCritical);
            Assert.IsTrue(res.HasCriticalConsequence);
            StringAssert.Contains("[CRITIQUE", res.CombatLog);
        }

        [Test]
        public void ForcedDefenseCrit_BlocksWithDefensiveConsequence()
        {
            DiceRoller.DevForceAttackFumble = true;
            DiceRoller.DevForceDefenseCrit = true;
            var calc = MakeCalc();
            var att = MakeStats("Attaquant");
            var def = MakeStats("Défenseur");
            var res = calc.ResolveTargetedAttack(att, def, BodyPart.Torse,
                DiceType.D8, 0, DiceType.D6, 0, 5);
            Assert.IsFalse(res.IsHit);
            Assert.IsTrue(res.IsBlocked);
            Assert.IsTrue(res.HasCriticalConsequence);
            StringAssert.Contains("CONSÉQUENCE CRITIQUE DÉFENSIVE", res.CombatLog);
        }

        [Test]
        public void ForcedConsequence24_Offensive_KnocksOut()
        {
            DiceRoller.DevForceAttackCrit = true;
            DiceRoller.DevForceDefenseFumble = true;
            DiceRoller.DevForcedCritConsequenceRaw = 24;
            var calc = MakeCalc();
            var att = MakeStats("Attaquant");
            var def = MakeStats("Défenseur");
            var res = calc.ResolveTargetedAttack(att, def, BodyPart.Torse,
                DiceType.TwoD12, 0, DiceType.D6, 0, 5);
            Assert.IsTrue(res.IsHit);
            Assert.AreEqual(24, res.CriticalConsequence.Threshold);
            StringAssert.Contains("Palier 24", res.CombatLog);
            Assert.AreEqual(FatalBlowResolution.ForcedUnconscious, res.FatalResolution);
        }

        [Test]
        public void ForcedDiffZero_DeflectsWithForcedD6()
        {
            DiceRoller.DevForcedDifferential = 0;
            DiceRoller.DevForcedDeflectionRoll = 1;
            var calc = MakeCalc();
            var att = MakeStats("Attaquant");
            var def = MakeStats("Défenseur");
            var res = calc.ResolveTargetedAttack(att, def, BodyPart.Torse,
                DiceType.D8, 0, DiceType.D6, 0, 5);
            Assert.IsTrue(res.IsHit);
            Assert.AreEqual(0, res.Differential);
            Assert.IsTrue(res.WasDeflected);
            Assert.AreEqual(1, res.DeflectionRoll);
            // Table Haute (Torse visé) tirage 1 -> Yeux/Visage.
            Assert.AreEqual(BodyPart.YeuxVisage, res.ActualHitPart);
            StringAssert.Contains("DÉVIATION", res.CombatLog);
        }

        [Test]
        public void ForcedDiff5_Slashing_Bleeds()
        {
            DiceRoller.DevForcedDifferential = 5;
            var calc = MakeCalc();
            var att = MakeStats("Attaquant");
            var def = MakeStats("Défenseur");
            var res = calc.ResolveTargetedAttack(att, def, BodyPart.Torse,
                DiceType.D8, 0, DiceType.D6, 0, 5,
                attackSkill: SkillType.ArmesPercantes);
            Assert.IsTrue(res.IsHit);
            Assert.AreEqual(5, res.Differential);
            Assert.IsTrue(res.InflictedStatus.HasFlag(StatusEffect.Saignement));
            StringAssert.Contains("HÉMORRAGIE", res.CombatLog);
        }

        [Test]
        public void ForcedDiff10_Cranial_KnocksOut()
        {
            DiceRoller.DevForcedDifferential = 10;
            var calc = MakeCalc();
            var att = MakeStats("Attaquant");
            var def = MakeStats("Défenseur");
            var res = calc.ResolveTargetedAttack(att, def, BodyPart.Tete,
                DiceType.D8, 0, DiceType.D6, 0, 5);
            Assert.IsTrue(res.IsHit);
            Assert.AreEqual(10, res.Differential);
            Assert.IsTrue(def.ActiveStatus.HasFlag(StatusEffect.Inconscient));
            StringAssert.Contains("ASSOMMEMENT", res.CombatLog);
        }

        [Test]
        public void ForcedDiff20_Destruction_DisintegratesDespiteMiracle()
        {
            DiceRoller.DevForcedDifferential = 20;
            var calc = MakeCalc();
            var att = MakeStats("Attaquant");
            att.Sheet.UnlockedSpecializations.Add("Destruction 6");
            var def = MakeStats("Défenseur", hp: 20);
            // Garde-fou : même avec 1 point de Miracle, la pulvérisation est totale.
            var at = def.Attributes; at.PointsMiracle = 1; def.Attributes = at;
            var res = calc.ResolveTargetedAttack(att, def, BodyPart.Torse,
                DiceType.D8, 0, DiceType.D6, 0, 5);
            Assert.IsTrue(res.WasDisintegrated);
            Assert.IsTrue(def.IsDead);
            Assert.AreEqual(FatalBlowResolution.InstantDeath, res.FatalResolution);
            StringAssert.Contains("DÉSINTÉGRATION", res.CombatLog);
        }

        [Test]
        public void GardeFou_Miracle_SavesLethalHead()
        {
            DiceRoller.DevForcedDifferential = 20;
            var calc = MakeCalc();
            var att = MakeStats("Attaquant");
            var def = MakeStats("Défenseur", hp: 20);
            var at = def.Attributes; at.PointsMiracle = 1; def.Attributes = at;
            var res = calc.ResolveTargetedAttack(att, def, BodyPart.Tete,
                DiceType.D8, 0, DiceType.D6, 0, 5);
            Assert.IsFalse(res.WasDisintegrated);
            Assert.AreEqual(FatalBlowResolution.MiracleSaved, res.FatalResolution);
            Assert.AreEqual(1, def.CurrentHealth);
            StringAssert.Contains("MIRACLE", res.CombatLog);
        }

        [Test]
        public void GardeFou_NoMiracle_LethalHead_InstantDeath()
        {
            DiceRoller.DevForcedDifferential = 20;
            var calc = MakeCalc();
            var att = MakeStats("Attaquant");
            var def = MakeStats("Défenseur", hp: 20);
            var at = def.Attributes; at.PointsMiracle = 0; def.Attributes = at;
            var res = calc.ResolveTargetedAttack(att, def, BodyPart.Tete,
                DiceType.D8, 0, DiceType.D6, 0, 5);
            Assert.IsFalse(res.WasDisintegrated);
            Assert.AreEqual(FatalBlowResolution.InstantDeath, res.FatalResolution);
            Assert.IsTrue(def.IsDead);
        }
    }
}
#endif

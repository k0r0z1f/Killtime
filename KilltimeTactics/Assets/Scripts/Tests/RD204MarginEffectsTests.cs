#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using NUnit.Framework;
using Killtime.Core.Character;
using Killtime.Core.Combat;
using Killtime.Core.Dice;
using Killtime.Tactics.Grid;

namespace Killtime.Tests
{
    /// <summary>
    /// RD-204 (Livre V §20.2 & Livre VI §24.4) : marges manquantes —
    /// Possession (Diff 10+, 15 si boss, contrôle 1 tour),
    /// Attirer / Repousser en épreuve (Diff 1+),
    /// Choc traumatique lourd (&gt; 2xCON = +1 Essoufflement + épreuve CON),
    /// Désarmement à 2 cases (DisarmDropRules + config).
    /// Pur Core, différentiels forcés via le rig dev-only (aucune règle modifiée).
    /// </summary>
    [TestFixture]
    public class RD204MarginEffectsTests
    {
        private static CharacterStats MakeStats(string name, int force = 5, int agi = 4, int con = 5, int ap = 6, int hp = 40, int mag = 0)
        {
            var attr = new Attributes(force, agi, con, 3, 3, 2, 2, 2, mag);
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

        private static CombatCalculator MakeCalc()
        {
            return new CombatCalculator(new DiceRoller(1234));
        }

        [SetUp]
        public void SetUp()
        {
            DiceRoller.ClearDevRig();
            PossessionState.ClearAll();
            Killtime.Core.Rules.CoreRulesConfig.Instance.ResetToCodexDefaults();
        }

        [TearDown]
        public void TearDown()
        {
            DiceRoller.ClearDevRig();
            PossessionState.ClearAll();
            Killtime.Core.Rules.CoreRulesConfig.Instance.ResetToCodexDefaults();
        }

        [Test]
        public void Possession_Diff10_ControlsOneTurn()
        {
            DiceRoller.DevForcedDifferential = 10;
            var calc = MakeCalc();
            var att = MakeStats("Contrôleur", mag: 6);
            var def = MakeStats("Cible", mag: 2);
            var res = calc.ResolvePossessionDuel(att, def);
            Assert.IsTrue(res.AttackerWins);
            Assert.AreEqual(10, res.Differential);
            Assert.IsTrue(PossessionState.IsPossessed(def));
            Assert.IsTrue(PossessionState.IsControlling(att));
            Assert.IsTrue(PossessionState.TryGetController(def, out var ctrl));
            Assert.AreEqual(att, ctrl);
            StringAssert.Contains("POSSESSION", res.CombatLog);
            StringAssert.Contains("CONTRÔLE", res.CombatLog);
        }

        [Test]
        public void Possession_Diff9_Resisted()
        {
            DiceRoller.DevForcedDifferential = 9;
            var calc = MakeCalc();
            var att = MakeStats("Contrôleur", mag: 6);
            var def = MakeStats("Cible", mag: 2);
            var res = calc.ResolvePossessionDuel(att, def);
            Assert.IsFalse(res.AttackerWins);
            Assert.IsFalse(PossessionState.IsPossessed(def));
            StringAssert.Contains("RÉSISTÉE", res.CombatLog);
        }

        [Test]
        public void Possession_Boss_RequiresDiff15()
        {
            var calc = MakeCalc();
            var att = MakeStats("Contrôleur", mag: 6);
            var boss = MakeStats("Gardien", mag: 4);
            boss.Sheet.Profile = CharacterProfileType.PnjBoss;

            DiceRoller.DevForcedDifferential = 10;
            var res10 = calc.ResolvePossessionDuel(att, boss);
            Assert.IsFalse(res10.AttackerWins);
            Assert.IsFalse(PossessionState.IsPossessed(boss));
            StringAssert.Contains("seuil boss", res10.CombatLog);

            DiceRoller.DevForcedDifferential = 15;
            var res15 = calc.ResolvePossessionDuel(att, boss);
            Assert.IsTrue(res15.AttackerWins);
            Assert.IsTrue(PossessionState.IsPossessed(boss));
        }

        [Test]
        public void Possession_OneTurnExpiry_VictimPlaysThenReleased()
        {
            DiceRoller.DevForcedDifferential = 12;
            var calc = MakeCalc();
            var att = MakeStats("Contrôleur", mag: 6);
            var def = MakeStats("Cible", mag: 2);
            calc.ResolvePossessionDuel(att, def);
            Assert.IsTrue(PossessionState.IsPossessed(def));

            // Le tour du contrôleur ne fait jamais expirer (ordre d'initiative libre).
            PossessionState.OnUnitTurnStart(att);
            Assert.IsTrue(PossessionState.IsPossessed(def));

            // Premier tour de la victime = tour contrôlé (marqué consommé).
            PossessionState.OnUnitTurnStart(def);
            Assert.IsTrue(PossessionState.IsPossessed(def));

            // Tour suivant = expiration.
            PossessionState.OnUnitTurnStart(def);
            Assert.IsFalse(PossessionState.IsPossessed(def));
            Assert.IsFalse(PossessionState.IsControlling(att));
        }

        [Test]
        public void Possession_DeathBreaksHold()
        {
            DiceRoller.DevForcedDifferential = 12;
            var calc = MakeCalc();
            var att = MakeStats("Contrôleur", mag: 6);
            var def = MakeStats("Cible", mag: 2);
            calc.ResolvePossessionDuel(att, def);
            Assert.IsTrue(PossessionState.IsPossessed(def));
            def.CurrentHealth = 0;
            Assert.IsFalse(PossessionState.IsPossessed(def));
        }

        [Test]
        public void Possession_ControllerDeathPurgedOnSweep_NoOrphan()
        {
            DiceRoller.DevForcedDifferential = 12;
            var calc = MakeCalc();
            var att = MakeStats("Contrôleur", mag: 6);
            var def = MakeStats("Cible", mag: 2);
            calc.ResolvePossessionDuel(att, def);
            Assert.IsTrue(PossessionState.IsControlling(att));
            att.CurrentHealth = 0;
            PossessionState.OnUnitTurnStart(def);
            Assert.IsFalse(PossessionState.IsPossessed(def));
            Assert.IsFalse(PossessionState.IsControlling(att));
        }

        [Test]
        public void Push_Diff1_KnocksBack()
        {
            DiceRoller.DevForcedDifferential = 1;
            var calc = MakeCalc();
            var att = MakeStats("Pousseur");
            var def = MakeStats("Cible");
            var res = calc.ResolvePushDuel(att, def);
            Assert.IsTrue(res.IsHit);
            Assert.AreEqual(1, res.Differential);
            Assert.IsTrue(res.CausedKnockback);
            Assert.IsFalse(res.CausedPull);
            Assert.AreEqual(0, res.FinalDamageApplied);
            StringAssert.Contains("REPOUSSER", res.CombatLog);
        }

        [Test]
        public void Push_Diff0_Fails()
        {
            DiceRoller.DevForcedDifferential = 0;
            var calc = MakeCalc();
            var att = MakeStats("Pousseur");
            var def = MakeStats("Cible");
            var res = calc.ResolvePushDuel(att, def);
            Assert.IsFalse(res.IsHit);
            Assert.IsFalse(res.CausedKnockback);
            StringAssert.Contains("ÉCHEC", res.CombatLog);
        }

        [Test]
        public void Pull_Diff1_Attracts()
        {
            DiceRoller.DevForcedDifferential = 1;
            var calc = MakeCalc();
            var att = MakeStats("Aimant");
            var def = MakeStats("Cible");
            var res = calc.ResolvePullDuel(att, def);
            Assert.IsTrue(res.IsHit);
            Assert.IsTrue(res.CausedPull);
            Assert.IsFalse(res.CausedKnockback);
            Assert.AreEqual(0, res.FinalDamageApplied);
            StringAssert.Contains("ATTIRER", res.CombatLog);
        }

        [Test]
        public void Pull_Diff0_Fails()
        {
            DiceRoller.DevForcedDifferential = 0;
            var calc = MakeCalc();
            var att = MakeStats("Aimant");
            var def = MakeStats("Cible");
            var res = calc.ResolvePullDuel(att, def);
            Assert.IsFalse(res.IsHit);
            Assert.IsFalse(res.CausedPull);
        }

        [Test]
        public void TraumaticShock_AboveTwiceCon_GainsEssoufflement_ConHeld()
        {
            DiceRoller.DevForcedDifferential = 5;
            DiceRoller.DevForceCritical = true;
            var calc = MakeCalc();
            var att = MakeStats("Frappeur");
            var def = MakeStats("Cible", con: 3);
            int before = def.Essoufflement;
            var res = calc.ResolveTargetedAttack(att, def, BodyPart.Torse,
                DiceType.D8, 0, DiceType.D6, 0, 5);
            Assert.IsTrue(res.IsHit);
            Assert.Greater(res.FinalDamageApplied, 6);
            Assert.IsTrue(res.TraumaticShockApplied);
            Assert.IsFalse(res.TraumaticShockConFailed);
            Assert.AreEqual(before + 1, def.Essoufflement);
            StringAssert.Contains("CHOC TRAUMATIQUE LOURD", res.CombatLog);
        }

        [Test]
        public void TraumaticShock_AboveTwiceCon_ConFailed_DoubleEssoufflement()
        {
            DiceRoller.DevForcedDifferential = 5;
            DiceRoller.DevForceFumble = true;
            var calc = MakeCalc();
            var att = MakeStats("Frappeur");
            var def = MakeStats("Cible", con: 3);
            var res = calc.ResolveTargetedAttack(att, def, BodyPart.Torse,
                DiceType.D8, 0, DiceType.D6, 0, 5);
            Assert.IsTrue(res.IsHit);
            Assert.IsTrue(res.TraumaticShockApplied);
            Assert.IsTrue(res.TraumaticShockConFailed);
            Assert.AreEqual(2, def.Essoufflement);
            Assert.IsFalse(def.ActiveStatus.HasFlag(StatusEffect.Etourdi));
            StringAssert.Contains("Essoufflement supp", res.CombatLog);
        }

        [Test]
        public void TraumaticShock_BelowThreshold_NoEffect()
        {
            DiceRoller.DevForcedDifferential = 1;
            var calc = MakeCalc();
            var att = MakeStats("Frappeur");
            var def = MakeStats("Cible", con: 5);
            var res = calc.ResolveTargetedAttack(att, def, BodyPart.Torse,
                DiceType.D8, 0, DiceType.D6, 0, 5);
            Assert.IsTrue(res.IsHit);
            Assert.IsFalse(res.TraumaticShockApplied);
            Assert.AreEqual(0, def.Essoufflement);
        }

        [Test]
        public void DisarmDrop_TwoCasesAlongAttackAxis()
        {
            var from = new HexCoordinates(0, 0);
            var holder = new HexCoordinates(1, 0);
            var drop = DisarmDropRules.ComputeDropCoords(from, holder, 2);
            Assert.AreEqual(new HexCoordinates(3, 0), drop);
            Assert.AreEqual(2, holder.DistanceTo(drop));
        }

        [Test]
        public void DisarmDrop_SameCell_FallsBackToDefaultDirection()
        {
            var cell = new HexCoordinates(2, -1);
            var drop = DisarmDropRules.ComputeDropCoords(cell, cell, 2);
            Assert.AreEqual(2, cell.DistanceTo(drop));
        }

        [Test]
        public void DisarmDrop_ConfigDefaultsToTwo()
        {
            Assert.AreEqual(2, Killtime.Core.Rules.CoreRulesConfig.Instance.DisarmDropDistance);
            Assert.AreEqual(10, Killtime.Core.Rules.CoreRulesConfig.Instance.PossessDifferentialThreshold);
            Assert.AreEqual(15, Killtime.Core.Rules.CoreRulesConfig.Instance.PossessBossThreshold);
            Assert.AreEqual(1, Killtime.Core.Rules.CoreRulesConfig.Instance.ForceMoveDifferentialThreshold);
        }
    }
}
#endif

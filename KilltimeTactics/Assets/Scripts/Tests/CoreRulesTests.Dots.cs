#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEngine;
using Killtime.Core.Character;
using Killtime.Core.Dice;
using Killtime.Core.Combat;
using Killtime.Core.Arcanotech;
using Killtime.Core.Chrono;
using Killtime.Tactics.Grid;

namespace Killtime.Tests
{
    public partial class CoreRulesTests
    {
        [Test]
        public void TestDot_RD045_ResidualDegressif_3_2_1_PuisDissipe()
        {
            var attr = new Attributes(4, 4, 4, 3, 3, 2, 2, 2);
            var sheet = new CharacterSheet { Name = "Roger", BaseAttributes = attr };
            var stats = sheet.ToCombatStats();
            stats.Sheet = sheet;

            stats.ApplyResidualDamage(StatusEffect.Saignement, 3);
            Assert.AreEqual(3, stats.GetResidualDamage(StatusEffect.Saignement));
            int hp0 = stats.CurrentHealth;

            var t1 = stats.TickTurnStatus();
            Assert.AreEqual(3, t1.DamageByStatus[StatusEffect.Saignement]);
            Assert.AreEqual(hp0 - 3, stats.CurrentHealth);
            Assert.AreEqual(2, stats.GetResidualDamage(StatusEffect.Saignement));
            Assert.IsTrue(stats.ActiveStatus.HasFlag(StatusEffect.Saignement));

            var t2 = stats.TickTurnStatus();
            Assert.AreEqual(2, t2.DamageByStatus[StatusEffect.Saignement]);
            Assert.AreEqual(1, stats.GetResidualDamage(StatusEffect.Saignement));

            var t3 = stats.TickTurnStatus();
            Assert.AreEqual(1, t3.DamageByStatus[StatusEffect.Saignement]);
            Assert.IsTrue(t3.ClearedStatuses.Contains(StatusEffect.Saignement));
            Assert.IsFalse(stats.ActiveStatus.HasFlag(StatusEffect.Saignement));
            Assert.AreEqual(0, stats.GetResidualDamage(StatusEffect.Saignement));
        }

        [Test]
        public void TestDot_RD045_SoinStoppeSaignement()
        {
            var attr = new Attributes(4, 4, 4, 3, 3, 2, 2, 2);
            var sheet = new CharacterSheet { Name = "Roger", BaseAttributes = attr };
            var stats = sheet.ToCombatStats();
            stats.Sheet = sheet;

            stats.ApplyResidualDamage(StatusEffect.Saignement, 3);
            stats.CurrentHealth -= 5;
            int hpBeforeHeal = stats.CurrentHealth;
            int healed = stats.Heal(2);
            Assert.Greater(healed, 0);
            Assert.IsFalse(stats.ActiveStatus.HasFlag(StatusEffect.Saignement));
            Assert.AreEqual(0, stats.GetResidualDamage(StatusEffect.Saignement));

            // Plus aucun tick après le soin.
            var tick = stats.TickTurnStatus();
            Assert.AreEqual(0, tick.TotalDamage);
            Assert.AreEqual(hpBeforeHeal + healed, stats.CurrentHealth);
        }

        [Test]
        public void TestDot_RD045_Aggravation_AuLieuDeDecroitre()
        {
            var attr = new Attributes(4, 4, 4, 3, 3, 2, 2, 2);
            var sheet = new CharacterSheet { Name = "Roger", BaseAttributes = attr };
            var stats = sheet.ToCombatStats();
            stats.Sheet = sheet;

            stats.ApplyResidualDamage(StatusEffect.Empoisonne, 2, aggravating: true);
            int hp0 = stats.CurrentHealth;
            var tick = stats.TickTurnStatus();
            Assert.Greater(tick.TotalDamage, 2); // doublé par aggravation
            Assert.IsTrue(stats.ActiveStatus.HasFlag(StatusEffect.Empoisonne));
            Assert.Greater(stats.GetResidualDamage(StatusEffect.Empoisonne), 2);
            Assert.AreEqual(hp0 - tick.TotalDamage, stats.CurrentHealth);
        }

        [Test]
        public void TestDot_RD045_Asphyxie_DraineUnPA_EnPlusDesPV()
        {
            var attr = new Attributes(4, 4, 4, 3, 3, 2, 2, 2);
            var sheet = new CharacterSheet { Name = "Roger", BaseAttributes = attr };
            var stats = sheet.ToCombatStats();
            stats.Sheet = sheet;

            stats.ApplyResidualDamage(StatusEffect.Asphyxie, 2);
            stats.ResetTurn();
            int pa0 = stats.CurrentActionPoints;
            var tick = stats.TickTurnStatus();
            Assert.AreEqual(2, tick.DamageByStatus[StatusEffect.Asphyxie]);
            Assert.AreEqual(1, tick.PaDrained);
            Assert.AreEqual(pa0 - 1, stats.CurrentActionPoints);
        }

        [Test]
        public void TestDot_RD045_EnFeu_PasseParLeBouclier()
        {
            var attr = new Attributes(4, 4, 4, 3, 3, 2, 2, 2);
            var sheet = new CharacterSheet { Name = "Roger", BaseAttributes = attr };
            var stats = sheet.ToCombatStats();
            stats.Sheet = sheet;

            stats.ApplyResidualDamage(StatusEffect.EnFeu, 3);
            // Bouclier factice : 2 PV absorbés avant la chair.
            stats.CurrentShieldHP = 2;
            int hp0 = stats.CurrentHealth;
            var tick = stats.TickTurnStatus();
            Assert.AreEqual(2, tick.ShieldAbsorbed);
            Assert.AreEqual(1, tick.DamageByStatus[StatusEffect.EnFeu]);
            Assert.AreEqual(hp0 - 1, stats.CurrentHealth);
        }

        [Test]
        public void TestMina_InvisibleSteps_AppliesMoveDiscountOnFirstMoveOnly()
        {
            var attr = new Attributes(@for: 3, agi: 5, con: 3, rap: 4, @int: 3, eru: 2, cha: 3, ins: 3, mag: 5);
            var sheet = new CharacterSheet { Name = "Mina", BaseAttributes = attr };
            sheet.UnlockedSpecializations.Add("Protocole des Pas Invisibles");

            var go = new GameObject("TestUnit_Mina");
            var unit = go.AddComponent<Killtime.Tactics.Units.TacticalUnit>();
            unit.ConfigureStats("Mina", attr, 1, true);
            unit.GetOrBuildSheet().UnlockedSpecializations.Add("Protocole des Pas Invisibles");

            Assert.AreEqual(0, unit.Stats.MovesThisTurn);
            Assert.AreEqual(0, unit.ComputeMovementAPCost(1));
            Assert.AreEqual(2, unit.ComputeMovementAPCost(3));

            unit.Stats.RegisterMove();
            Assert.AreEqual(1, unit.Stats.MovesThisTurn);
            Assert.AreEqual(1, unit.ComputeMovementAPCost(1));
            Assert.AreEqual(3, unit.ComputeMovementAPCost(3));

            unit.Stats.ResetTurn();
            Assert.AreEqual(0, unit.Stats.MovesThisTurn);
            Assert.AreEqual(0, unit.ComputeMovementAPCost(1));

            Object.DestroyImmediate(go);
        }

        [Test]
        public void TestMina_FibresResilientes_And_Homeostasie()
        {
            var attr = new Attributes(@for: 3, agi: 5, con: 3, rap: 4, @int: 3, eru: 2, cha: 3, ins: 3, mag: 5);
            var sheet = new CharacterSheet { Name = "Mina", BaseAttributes = attr };

            var statsNormal = sheet.ToCombatStats();
            Assert.AreEqual(6, statsNormal.EncaissementThreshold); // CON 3 * 2 = 6

            sheet.UnlockedSpecializations.Add("Fibres Résilientes");
            sheet.UnlockedSpecializations.Add("Homéostasie Accélérée");
            var statsMina = sheet.ToCombatStats();
            Assert.AreEqual(8, statsMina.EncaissementThreshold); // (CON 3 * 2) + 2 = 8

            statsMina.CurrentHealth = 10;
            statsMina.ApplyStatus(StatusEffect.Saignement);
            Assert.IsTrue(statsMina.ActiveStatus.HasFlag(StatusEffect.Saignement));

            statsMina.ResetTurn();
            Assert.AreEqual(11, statsMina.CurrentHealth); // +1 PV régénéré
            Assert.IsFalse(statsMina.ActiveStatus.HasFlag(StatusEffect.Saignement)); // Saignement purgé
        }
    }
}
#endif
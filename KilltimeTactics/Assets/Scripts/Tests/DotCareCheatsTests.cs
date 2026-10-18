#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using System;
using NUnit.Framework;
using Killtime.Core.Character;
using Killtime.Core.Combat;

namespace Killtime.Tests
{
    /// <summary>
    /// RD-191 : DoT calibrés, Souffrant, résurrection — chemins Core existants
    /// (ApplyResidualDamage, Heal, ApplyMajorCare, ApplyResurrection, TickTurnStatus).
    /// Pur Core, sans MonoBehaviour. Miroir des cheats dev-only (toolbar Dots).
    /// </summary>
    [TestFixture]
    public class DotCareCheatsTests
    {
        private static CharacterStats MakeStats(string name, int con = 4, int hp = 20)
        {
            var attr = new Attributes(4, 4, con, 3, 3, 2, 2, 2);
            var sheet = new CharacterSheet
            {
                Name = name,
                BaseAttributes = attr,
                UnlockedSpecializations = new System.Collections.Generic.List<string>()
            };
            var stats = sheet.ToCombatStats();
            stats.Sheet = sheet;
            stats.CurrentHealth = hp;
            return stats;
        }

        [Test]
        public void DebugDot_DosesPool_AndStatus()
        {
            var stats = MakeStats("Cobaye", con: 4, hp: 20);
            // Chemin cheat : ApplyResidualDamage (refuse les non-DoT comme le service).
            Assert.IsTrue(CharacterStats.IsDotStatus(StatusEffect.Saignement));
            Assert.IsFalse(CharacterStats.IsDotStatus(StatusEffect.Souffrant));

            stats.ApplyResidualDamage(StatusEffect.Saignement, 5);
            Assert.IsTrue(stats.ActiveStatus.HasFlag(StatusEffect.Saignement));
            Assert.AreEqual(5, stats.GetResidualDamage(StatusEffect.Saignement));

            // Re-dose inférieure : max conservé (pas de cumul infini).
            stats.ApplyResidualDamage(StatusEffect.Saignement, 2);
            Assert.AreEqual(5, stats.GetResidualDamage(StatusEffect.Saignement));
        }

        [Test]
        public void Heal_NeverLiftsSouffrant_EvenBeyondThreshold()
        {
            var stats = MakeStats("Blessé", con: 4, hp: 10);
            stats.ApplyStatus(StatusEffect.Souffrant);
            int threshold = stats.GetMajorCareThreshold(); // 8

            stats.CurrentHealth = 5;
            int healed = stats.Heal(threshold + 50); // méga-seringue au-delà de CON×2
            Assert.Greater(healed, 0);
            Assert.IsTrue(stats.ActiveStatus.HasFlag(StatusEffect.Souffrant),
                "Un soin ordinaire, même massif, ne doit jamais lever Souffrant.");
        }

        [Test]
        public void MajorCare_BelowPersists_AtThresholdLifts()
        {
            var low = MakeStats("Bas", con: 4, hp: 10);
            low.ApplyStatus(StatusEffect.Souffrant);
            var resLow = low.ApplyMajorCare(low.GetMajorCareThreshold() - 1);
            Assert.IsFalse(resLow.MeetsThreshold);
            Assert.IsFalse(resLow.SouffrantLifted);
            Assert.IsTrue(low.ActiveStatus.HasFlag(StatusEffect.Souffrant));

            var high = MakeStats("Haut", con: 4, hp: 10);
            high.ApplyStatus(StatusEffect.Souffrant);
            var resHigh = high.ApplyMajorCare(high.GetMajorCareThreshold());
            Assert.IsTrue(resHigh.MeetsThreshold);
            Assert.IsTrue(resHigh.SouffrantLifted);
            Assert.IsFalse(high.ActiveStatus.HasFlag(StatusEffect.Souffrant));
        }

        [Test]
        public void Resurrection_Timestamps_AndAppliesMinusOne()
        {
            var stats = MakeStats("Revenant", con: 4);
            stats.IsDead = true;
            stats.CurrentHealth = 0;
            DateTime t0 = DateTime.UtcNow;

            Assert.IsFalse(stats.HasResurrectionSequelae(t0));
            int duration = stats.ApplyResurrection(1, t0);
            Assert.AreEqual(stats.GetResurrectionSequelaeDurationDays(), duration);
            Assert.Greater(duration, 0);
            Assert.IsNotNull(stats.LastResurrectionUtc);

            DateTime dayAfter = t0.AddDays(1);
            Assert.IsTrue(stats.HasResurrectionSequelae(dayAfter));
            Assert.AreEqual(-1, stats.GetResurrectionSequelaeModifier(dayAfter));
            Assert.AreEqual(1, stats.CurrentHealth);
            Assert.IsFalse(stats.IsDead);
        }

        [Test]
        public void TickTurnStatus_Decrements_ThenPurges()
        {
            var stats = MakeStats("Saignant", con: 4, hp: 20);
            stats.ApplyResidualDamage(StatusEffect.Saignement, 3);
            Assert.AreEqual(3, stats.GetResidualDamage(StatusEffect.Saignement));

            int hp0 = stats.CurrentHealth;
            var tick1 = stats.TickTurnStatus();
            Assert.AreEqual(3, tick1.DamageByStatus[StatusEffect.Saignement]);
            Assert.Less(stats.CurrentHealth, hp0);
            Assert.AreEqual(2, stats.GetResidualDamage(StatusEffect.Saignement));
            Assert.IsTrue(stats.ActiveStatus.HasFlag(StatusEffect.Saignement));

            stats.TickTurnStatus(); // pool 2 → 1
            Assert.AreEqual(1, stats.GetResidualDamage(StatusEffect.Saignement));
            var tick3 = stats.TickTurnStatus(); // pool 1 → 0 : purge
            Assert.IsTrue(tick3.ClearedStatuses.Contains(StatusEffect.Saignement));
            Assert.IsFalse(stats.ActiveStatus.HasFlag(StatusEffect.Saignement));
            Assert.AreEqual(0, stats.GetResidualDamage(StatusEffect.Saignement));
        }
    }
}
#endif

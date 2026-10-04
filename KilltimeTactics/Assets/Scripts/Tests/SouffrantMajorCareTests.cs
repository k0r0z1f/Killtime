#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using System;
using NUnit.Framework;
using Killtime.Core.Character;

namespace Killtime.Tests
{
    /// <summary>
    /// RD-083 : Statut Souffrant & Règle des Soins Majeurs (Livre VII §28.3, §29.1, §29.3).
    /// - Souffrant = blessure critique ouverte, statut persistant (pas de tick).
    /// - Soins ordinaires (Heal : seringues/bandages/repos) : rendent des PV mais ne
    ///   lèvent JAMAIS Souffrant, même au-delà de CON×2.
    /// - Seuls des Soins Majeurs (Premiers Soins / Chirurgie, UN SEUL coup >= CON×2)
    ///   via ApplyMajorCare lèvent le statut.
    /// - Résurrection : Poids du Trépas, -1 ec sur tous les jets pendant (10-CON)j réels.
    /// </summary>
    [TestFixture]
    public class SouffrantMajorCareTests
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
        public void Souffrant_IsPersistent_NoTickExpiry()
        {
            var stats = MakeStats("Blessé", con: 4);
            Assert.IsTrue(CharacterStats.IsPersistentStatus(StatusEffect.Souffrant));

            stats.ApplyStatus(StatusEffect.Souffrant);
            Assert.IsTrue(stats.ActiveStatus.HasFlag(StatusEffect.Souffrant));

            var expired = stats.TickTurnStatusDurations();
            Assert.IsFalse(expired.Contains(StatusEffect.Souffrant));
            Assert.IsTrue(stats.ActiveStatus.HasFlag(StatusEffect.Souffrant));
        }

        [Test]
        public void Souffrant_Threshold_EqualsConFoisDeux()
        {
            var c3 = MakeStats("C3", con: 3);
            var c5 = MakeStats("C5", con: 5);
            Assert.AreEqual(6, c3.GetMajorCareThreshold());
            Assert.AreEqual(10, c5.GetMajorCareThreshold());
        }

        [Test]
        public void OrdinaryHeal_RestoresHP_ButNeverLiftsSouffrant()
        {
            var stats = MakeStats("Blessé", con: 4, hp: 10);
            stats.ApplyStatus(StatusEffect.Souffrant);
            int max = stats.MaxHealth;

            // Seringue ordinaire sous le seuil : PV rendus, statut conservé.
            int healed = stats.Heal(5);
            Assert.Greater(healed, 0);
            Assert.IsTrue(stats.ActiveStatus.HasFlag(StatusEffect.Souffrant));

            // Même une méga-seringue au-delà de CON×2 (=8) ne lève PAS Souffrant :
            // seul ApplyMajorCare (Premiers Soins/Chirurgie) le peut.
            stats.CurrentHealth = 5;
            int bigHeal = stats.Heal(50);
            Assert.Greater(bigHeal, 0);
            Assert.AreEqual(max, stats.CurrentHealth);
            Assert.IsTrue(stats.ActiveStatus.HasFlag(StatusEffect.Souffrant),
                "Un soin ordinaire, même massif, ne doit jamais lever Souffrant.");
        }

        [Test]
        public void MajorCare_BelowThreshold_HealsButKeepsSouffrant()
        {
            var stats = MakeStats("Blessé", con: 4, hp: 10);
            stats.ApplyStatus(StatusEffect.Souffrant);

            var res = stats.ApplyMajorCare(7); // seuil = 8
            Assert.IsFalse(res.MeetsThreshold);
            Assert.IsFalse(res.SouffrantLifted);
            Assert.IsTrue(stats.ActiveStatus.HasFlag(StatusEffect.Souffrant));
            Assert.Greater(res.Healed, 0);
        }

        [Test]
        public void MajorCare_AtThreshold_LiftsSouffrant()
        {
            var stats = MakeStats("Blessé", con: 4, hp: 10);
            stats.ApplyStatus(StatusEffect.Souffrant);

            var res = stats.ApplyMajorCare(8); // CON 4 × 2 = 8
            Assert.IsTrue(res.MeetsThreshold);
            Assert.IsTrue(res.SouffrantLifted);
            Assert.IsFalse(stats.ActiveStatus.HasFlag(StatusEffect.Souffrant));
            StringAssert.Contains("LEVÉ", res.Log);
        }

        [Test]
        public void MajorCare_FirstAidConFoisDeux_AlwaysLifts()
        {
            // Premiers Soins d'urgence = CON×2 (CON×3 au kit) : lève toujours Souffrant.
            var stats = MakeStats("Blessé", con: 5, hp: 5);
            stats.ApplyStatus(StatusEffect.Souffrant);
            var res = stats.ApplyMajorCare(stats.Attributes.Constitution * 2);
            Assert.IsTrue(res.SouffrantLifted);
            Assert.IsFalse(stats.IsSouffrant);
        }

        [Test]
        public void MajorCare_StopsBleeding_LikeOrdinaryCare()
        {
            var stats = MakeStats("Blessé", con: 4, hp: 10);
            stats.ApplyStatus(StatusEffect.Saignement, 1);
            var res = stats.ApplyMajorCare(8);
            Assert.Greater(res.Healed, 0);
            Assert.IsFalse(stats.ActiveStatus.HasFlag(StatusEffect.Saignement));
        }

        [Test]
        public void ResetTurn_Regen_PreservesSouffrant()
        {
            var stats = MakeStats("Marcheur", con: 4, hp: 10);
            stats.Sheet.UnlockedSpecializations.Add("Condition de Fer");
            stats.ApplyStatus(StatusEffect.Souffrant);
            int hp0 = stats.CurrentHealth;

            stats.ResetTurn();
            Assert.Greater(stats.CurrentHealth, hp0);
            Assert.IsTrue(stats.ActiveStatus.HasFlag(StatusEffect.Souffrant),
                "Le repos / la régénération ordinaire ne referme pas la blessure critique.");
        }

        [Test]
        public void PulsionPhenix_PreservesSouffrant()
        {
            var stats = MakeStats("Phénix", con: 4, hp: 10);
            stats.Sheet.UnlockedSpecializations.Add("Homéostasie Accélérée : Pulsion Phénix");
            stats.ApplyStatus(StatusEffect.Souffrant);
            stats.ApplyStatus(StatusEffect.Saignement, 1);

            stats.ResetTurn();
            Assert.IsTrue(stats.ActiveStatus.HasFlag(StatusEffect.Souffrant),
                "Pulsion Phénix (régénération arcanique) ≠ Premiers Soins/Chirurgie.");
            Assert.IsFalse(stats.ActiveStatus.HasFlag(StatusEffect.Saignement));
        }

        [Test]
        public void Resurrection_Duration_Equals_DixMoinsCon()
        {
            var c1 = MakeStats("Fragile", con: 1);
            var c5 = MakeStats("Acier", con: 5);
            var c10 = MakeStats("Colosse", con: 10);
            Assert.AreEqual(9, c1.GetResurrectionSequelaeDurationDays());
            Assert.AreEqual(5, c5.GetResurrectionSequelaeDurationDays());
            Assert.AreEqual(0, c10.GetResurrectionSequelaeDurationDays());
        }

        [Test]
        public void Resurrection_Applies_MinusOne_UntilExpiry()
        {
            var stats = MakeStats("Revenant", con: 4); // durée = 6 jours
            DateTime t0 = DateTime.UtcNow;

            Assert.IsFalse(stats.HasResurrectionSequelae(t0));
            Assert.AreEqual(0, stats.GetResurrectionSequelaeModifier(t0));

            int duration = stats.ApplyResurrection(1, t0);
            Assert.AreEqual(6, duration);
            Assert.IsTrue(stats.IsAlive);

            // J+1 : séquelle active → -1 sur tous les jets.
            DateTime dayAfter = t0.AddDays(1);
            Assert.IsTrue(stats.HasResurrectionSequelae(dayAfter));
            Assert.AreEqual(-1, stats.GetResurrectionSequelaeModifier(dayAfter));
            Assert.AreEqual(-1, stats.GetStatusModifier(SkillType.ManiementArmes, true) - NoSequelaeBaseline(stats));
            Assert.AreEqual(5, stats.GetResurrectionSequelaeRemainingDays(dayAfter));

            // Après la convalescence (J+7) : purgée.
            DateTime after = t0.AddDays(7);
            Assert.IsFalse(stats.HasResurrectionSequelae(after));
            Assert.AreEqual(0, stats.GetResurrectionSequelaeModifier(after));
            Assert.AreEqual(0, stats.GetResurrectionSequelaeRemainingDays(after));
        }

        private static int NoSequelaeBaseline(CharacterStats stats)
        {
            // Modificateur sans séquelle : retire temporairement l'horodatage.
            var saved = stats.LastResurrectionUtc;
            stats.LastResurrectionUtc = null;
            int baseline = stats.GetStatusModifier(SkillType.ManiementArmes, true);
            stats.LastResurrectionUtc = saved;
            return baseline;
        }

        [Test]
        public void Resurrection_HighCon_NoSequelae()
        {
            var stats = MakeStats("Colosse", con: 10);
            DateTime t0 = DateTime.UtcNow;
            stats.ApplyResurrection(1, t0);
            Assert.IsFalse(stats.HasResurrectionSequelae(t0.AddHours(1)));
            Assert.AreEqual(0, stats.GetResurrectionSequelaeModifier(t0.AddHours(1)));
        }

        [Test]
        public void Resurrection_ClearsDeath_ButKeepsSouffrant()
        {
            var stats = MakeStats("Revenant", con: 4);
            stats.IsDead = true;
            stats.CurrentHealth = 0;
            stats.ApplyStatus(StatusEffect.Inconscient);
            stats.ApplyStatus(StatusEffect.Souffrant);

            stats.ApplyResurrection(1, DateTime.UtcNow);
            Assert.IsFalse(stats.IsDead);
            Assert.AreEqual(1, stats.CurrentHealth);
            Assert.IsFalse(stats.ActiveStatus.HasFlag(StatusEffect.Inconscient));
            Assert.IsTrue(stats.ActiveStatus.HasFlag(StatusEffect.Souffrant),
                "La résurrection ne referme pas la blessure critique (Soins Majeurs requis).");
        }
    }
}
#endif

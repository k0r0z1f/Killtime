#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using NUnit.Framework;
using Killtime.Core.Character;
using Killtime.Core.Combat;
using Killtime.Core.Dice;

namespace Killtime.Tests
{
    /// <summary>
    /// RD-192 : Moral / reddition / déroute — chemins Core existants
    /// (CheckMoraleAtThreshold seuil 28 %, Max CHA/INS vs SD, Survivor+PNJ→reddition
    /// sinon déroute Agonisant, réarmement au-dessus du seuil).
    /// Pur Core, sans MonoBehaviour. Miroir des cheats dev-only (toolbar Moral).
    /// Le rig 🎲 RD-187 (DevForceCritical/DevForceFumble) pilote l'issue.
    /// </summary>
    [TestFixture]
    public class MoraleCheatsTests
    {
        private static CharacterStats MakeStats(string name, int cha = 3, int ins = 5, int con = 4, bool survivor = false, bool player = false)
        {
            var attr = new Attributes(4, 4, con, 3, 3, 2, cha, ins);
            var sheet = new CharacterSheet
            {
                Name = name,
                BaseAttributes = attr,
                UnlockedSpecializations = new System.Collections.Generic.List<string>()
            };
            var stats = sheet.ToCombatStats();
            stats.Sheet = sheet;
            stats.IsSurvivor = survivor;
            stats.IsPlayerControlled = player;
            return stats;
        }

        private static int ThresholdOf(CharacterStats stats)
        {
            return (int)System.Math.Ceiling(stats.MaxHealth * 0.28f);
        }

        [TearDown]
        public void RestoreRng()
        {
            DiceRoller.ClearDevRig();
        }

        [Test]
        public void AboveThreshold_CheckNotTriggered()
        {
            var stats = MakeStats("Serein");
            stats.CurrentHealth = stats.MaxHealth;
            var res = stats.CheckMoraleAtThreshold(new DiceRoller(1), 6);
            Assert.IsFalse(res.Triggered);
            Assert.IsFalse(stats.HasTestedMorale);
        }

        [Test]
        public void ForceFumble_NonSurvivor_RoutesWithAgonisant()
        {
            var stats = MakeStats("Brave");
            stats.CurrentHealth = ThresholdOf(stats);
            DiceRoller.DevForceFumble = true;
            var res = stats.CheckMoraleAtThreshold(new DiceRoller(2), 6);
            Assert.IsTrue(res.Triggered);
            Assert.IsFalse(res.Passed);
            Assert.IsTrue(res.Routed);
            Assert.IsFalse(res.Surrendered);
            Assert.IsTrue(stats.ActiveStatus.HasFlag(StatusEffect.Agonisant));
            Assert.IsTrue(stats.HasTestedMorale);
        }

        [Test]
        public void ForceFumble_SurvivorNPC_Surrenders()
        {
            var stats = MakeStats("Lache", survivor: true, player: false);
            stats.CurrentHealth = ThresholdOf(stats);
            DiceRoller.DevForceFumble = true;
            var res = stats.CheckMoraleAtThreshold(new DiceRoller(3), 6);
            Assert.IsTrue(res.Triggered);
            Assert.IsFalse(res.Passed);
            Assert.IsTrue(res.Surrendered);
            Assert.IsFalse(res.Routed);
            Assert.IsTrue(stats.IsSurrendered);
        }

        [Test]
        public void ForceFumble_SurvivorPlayer_RoutesInsteadOfSurrender()
        {
            var stats = MakeStats("Heros", survivor: true, player: true);
            stats.CurrentHealth = ThresholdOf(stats);
            DiceRoller.DevForceFumble = true;
            var res = stats.CheckMoraleAtThreshold(new DiceRoller(4), 6);
            Assert.IsTrue(res.Triggered);
            Assert.IsTrue(res.Routed);
            Assert.IsFalse(res.Surrendered);
            Assert.IsFalse(stats.IsSurrendered);
        }

        [Test]
        public void ForceCrit_PassesMorale()
        {
            var stats = MakeStats("Stoique");
            stats.CurrentHealth = ThresholdOf(stats);
            DiceRoller.DevForceCritical = true;
            var res = stats.CheckMoraleAtThreshold(new DiceRoller(5), 6);
            Assert.IsTrue(res.Triggered);
            Assert.IsTrue(res.Passed);
            Assert.IsFalse(res.Surrendered);
            Assert.IsFalse(res.Routed);
        }

        [Test]
        public void SecondCheck_BlockedUntilRearm()
        {
            var stats = MakeStats("Sonne");
            stats.CurrentHealth = ThresholdOf(stats);
            DiceRoller.DevForceFumble = true;
            var first = stats.CheckMoraleAtThreshold(new DiceRoller(6), 6);
            Assert.IsTrue(first.Triggered);
            var second = stats.CheckMoraleAtThreshold(new DiceRoller(7), 6);
            Assert.IsFalse(second.Triggered);
        }

        [Test]
        public void HealAboveThreshold_RearmsMorale()
        {
            var stats = MakeStats("Soigne");
            stats.CurrentHealth = ThresholdOf(stats);
            DiceRoller.DevForceFumble = true;
            var res = stats.CheckMoraleAtThreshold(new DiceRoller(8), 6);
            Assert.IsTrue(res.Triggered);
            Assert.IsTrue(stats.HasTestedMorale);
            stats.Heal(stats.MaxHealth);
            Assert.AreEqual(stats.MaxHealth, stats.CurrentHealth);
            Assert.IsFalse(stats.HasTestedMorale);
        }

        [Test]
        public void RearmMorale_ClearsTestedSurrenderAndAgonisant()
        {
            var stats = MakeStats("Repenti", survivor: true);
            // État après reddition + déroute simulées : combo impossible en live,
            // le hook dev doit en faire table rase (vrai code exercé, pas un miroir).
            stats.IsSurrendered = true;
            stats.HasTestedMorale = true;
            stats.ApplyStatus(StatusEffect.Agonisant, -1);
            stats.RearmMorale();
            Assert.IsFalse(stats.HasTestedMorale);
            Assert.IsFalse(stats.IsSurrendered);
            Assert.IsFalse(stats.ActiveStatus.HasFlag(StatusEffect.Agonisant));
        }

        [Test]
        public void ForceCheck_AboveThreshold_PreservesTestedFlag()
        {
            var stats = MakeStats("Marque");
            stats.CurrentHealth = ThresholdOf(stats);
            DiceRoller.DevForceFumble = true;
            var res = stats.CheckMoraleAtThreshold(new DiceRoller(9), 6);
            Assert.IsTrue(res.Triggered);
            // Forcer un jet au-dessus du seuil ne doit pas effacer le test consommé.
            stats.CurrentHealth = stats.MaxHealth;
            var idle = stats.CheckMoraleAtThreshold(new DiceRoller(10), 6);
            Assert.IsFalse(idle.Triggered);
            Assert.IsTrue(stats.HasTestedMorale);
        }

        [Test]
        public void HealBelowThreshold_DoesNotRearm()
        {
            var stats = MakeStats("Convalescent");
            stats.CurrentHealth = ThresholdOf(stats) - 2;
            DiceRoller.DevForceFumble = true;
            var res = stats.CheckMoraleAtThreshold(new DiceRoller(11), 6);
            Assert.IsTrue(res.Triggered);
            stats.Heal(1); // Reste sous le seuil 28 % : pas de réarmement.
            Assert.IsTrue(stats.HasTestedMorale);
        }
    }
}
#endif

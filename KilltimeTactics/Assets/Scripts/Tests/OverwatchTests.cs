#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using NUnit.Framework;
using Killtime.Core.Character;
using Killtime.Core.Combat;
using Killtime.Core.Dice;
using Killtime.Tactics.Grid;

namespace Killtime.Tests
{
    /// <summary>
    /// RD-030 Guet / Overwatch : entrée 2 PA, rayon, tir gratuit en duel aveugle
    /// auto (1 tir puis consommé), expiration au prochain tour personnel,
    /// annulation si Étourdi / Paralysé, couvert total = pas de tir.
    /// </summary>
    [TestFixture]
    public class OverwatchTests
    {
        private static CharacterStats MakeStats(string name)
        {
            var attr = new Attributes(4, 4, 3, 3, 3, 2, 2, 2);
            var sheet = new CharacterSheet { Name = name, BaseAttributes = attr };
            var stats = sheet.ToCombatStats();
            stats.Sheet = sheet;
            return stats;
        }

        [SetUp]
        public void SetUp() => OverwatchState.ClearAll();

        [TearDown]
        public void TearDown() => OverwatchState.ClearAll();

        [Test]
        public void EnterOverwatch_Costs2PA_StoresRange()
        {
            var watcher = MakeStats("Guetteur");
            var calc = new CombatCalculator(new DiceRoller(7));
            int before = watcher.CurrentActionPoints;

            Assert.IsTrue(calc.TryEnterOverwatch(watcher, 8, out string error), error);
            Assert.IsTrue(OverwatchState.IsWatching(watcher));
            Assert.IsTrue(OverwatchState.TryGetRange(watcher, out int range));
            Assert.AreEqual(8, range);
            Assert.AreEqual(before - 2, watcher.CurrentActionPoints);
        }

        [Test]
        public void EnterOverwatch_RefusedWithoutRangedWeapon()
        {
            var watcher = MakeStats("Poings");
            var calc = new CombatCalculator(new DiceRoller(7));

            Assert.IsFalse(calc.TryEnterOverwatch(watcher, 1, out _));
            Assert.IsFalse(OverwatchState.IsWatching(watcher));
        }

        [Test]
        public void EnterOverwatch_RefusedWhenStunnedOrParalyzed()
        {
            var calc = new CombatCalculator(new DiceRoller(7));

            var stunned = MakeStats("Sonné-Léger");
            stunned.ApplyStatus(StatusEffect.Etourdi, 1);
            Assert.IsFalse(calc.TryEnterOverwatch(stunned, 8, out _));

            var paralyzed = MakeStats("Paralysé");
            paralyzed.ApplyStatus(StatusEffect.Paralyse, 1);
            Assert.IsFalse(calc.TryEnterOverwatch(paralyzed, 8, out _));
        }

        [Test]
        public void Overwatch_CancelledWhenStunnedAfterEntry()
        {
            var watcher = MakeStats("Guetteur");
            var calc = new CombatCalculator(new DiceRoller(7));
            Assert.IsTrue(calc.TryEnterOverwatch(watcher, 8, out _));

            watcher.ApplyStatus(StatusEffect.Etourdi, 1);
            Assert.IsFalse(OverwatchState.IsWatching(watcher));
        }

        [Test]
        public void OverwatchShot_IsFree_ConsumesWatch_LogsGuet()
        {
            var watcher = MakeStats("Guetteur");
            var target = MakeStats("Cible");
            var calc = new CombatCalculator(new DiceRoller(7));
            Assert.IsTrue(calc.TryEnterOverwatch(watcher, 8, out _));
            int paBeforeShot = watcher.CurrentActionPoints;

            var result = calc.ResolveOverwatchFire(watcher, target, 5, CoverType.None);
            Assert.IsTrue(result.HasValue);
            StringAssert.Contains("GUET", result.Value.CombatLog);
            Assert.AreEqual(paBeforeShot, watcher.CurrentActionPoints);
            Assert.IsFalse(OverwatchState.IsWatching(watcher));
            // Second tir impossible : guet consommé.
            Assert.IsFalse(calc.ResolveOverwatchFire(watcher, target, 5, CoverType.None).HasValue);
        }

        [Test]
        public void OverwatchShot_FullCover_KeepsWatch()
        {
            var watcher = MakeStats("Guetteur");
            var target = MakeStats("Cible");
            var calc = new CombatCalculator(new DiceRoller(7));
            Assert.IsTrue(calc.TryEnterOverwatch(watcher, 8, out _));

            Assert.IsFalse(calc.ResolveOverwatchFire(watcher, target, 5, CoverType.Full).HasValue);
            Assert.IsTrue(OverwatchState.IsWatching(watcher));
        }

        [Test]
        public void Overwatch_ExpiresOnOwnerTurnStart()
        {
            var watcher = MakeStats("Guetteur");
            var calc = new CombatCalculator(new DiceRoller(7));
            Assert.IsTrue(calc.TryEnterOverwatch(watcher, 8, out _));

            OverwatchState.OnUnitTurnStart(watcher);
            Assert.IsFalse(OverwatchState.IsWatching(watcher));
        }
    }
}
#endif

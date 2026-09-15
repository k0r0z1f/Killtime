#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using NUnit.Framework;
using Killtime.Core.Character;
using Killtime.Core.Combat;
using Killtime.Core.Dice;
using Killtime.Core.Inventory;

namespace Killtime.Tests
{
    [TestFixture]
    public class GrenadeSystemTests
    {
        private static CharacterStats MakeStats(int con = 4)
        {
            var attr = new Attributes(@for: 3, agi: 3, con: con, rap: 3, @int: 2, eru: 2, cha: 1, ins: 2, mag: 0);
            var sheet = new CharacterSheet { Name = "Lanceur", BaseAttributes = attr };
            return sheet.ToCombatStats();
        }

        private static InventoryItem MakeGrenade(int flat = 10, int nd10 = 2, int blast = 2, string kind = "Fragmentation", int shrap = 2)
        {
            return new InventoryItem
            {
                Name = "Test Frag",
                Type = ItemType.Weapon,
                Category = "Grenades",
                IsGrenade = true,
                IsStackable = true,
                Quantity = 3,
                BaseDamage = flat,
                DamageDiceCount = nd10,
                BlastRadius = blast,
                GrenadeKind = kind,
                ShrapnelDamage = shrap,
                GrenadeStatuses = "Destabilise,Saignement",
                Era = "Moderne",
                LauncherCompatible = true,
                RangeInTiles = 8
            };
        }

        [Test]
        public void Catalog_ContainsMultiEraGrenadesAndLaunchers()
        {
            var all = ArmoryCatalog.All;
            int grenades = 0;
            var eras = new System.Collections.Generic.HashSet<string>();
            int launchers = 0;
            for (int i = 0; i < all.Count; i++)
            {
                var it = all[i];
                if (it == null) continue;
                if (it.IsGrenade) { grenades++; if (!string.IsNullOrEmpty(it.Era)) eras.Add(it.Era); }
                if (it.IsLauncher) launchers++;
            }
            Assert.GreaterOrEqual(grenades, 15, "Le marché doit proposer 15+ grenades multi-époques.");
            Assert.GreaterOrEqual(eras.Count, 5, "Au moins 5 ères distinctes attendues.");
            Assert.GreaterOrEqual(launchers, 4, "Au moins 4 lance-grenades attendus.");
            Assert.Contains("Lance-Grenades", ArmoryCatalog.Categories);
        }

        [Test]
        public void LegacyGrenades_ArePatchedWithZoneProfile()
        {
            var g = ArmoryCatalog.GetByName("Grenade Tactique 2d10");
            Assert.IsNotNull(g);
            Assert.IsTrue(g.IsGrenade);
            Assert.IsTrue(g.IsStackable);
            Assert.AreEqual(2, g.DamageDiceCount);
            Assert.GreaterOrEqual(g.BlastRadius, 1);
            Assert.IsTrue(g.LauncherCompatible);
        }

        [Test]
        public void Rules_RangeAndAPCost_HandVsLauncher()
        {
            var g = MakeGrenade();
            Assert.AreEqual(8, GrenadeRules.ComputeMaxRange(g, null));
            var launcher = new InventoryItem { IsLauncher = true, LauncherRangeBonus = 12 };
            Assert.AreEqual(20, GrenadeRules.ComputeMaxRange(g, launcher));
            Assert.AreEqual(2, GrenadeRules.ComputeAPCost(null, false));
            Assert.AreEqual(3, GrenadeRules.ComputeAPCost(launcher, false));
            Assert.AreEqual(3, GrenadeRules.ComputeAPCost(null, true));
            Assert.AreEqual(4, GrenadeRules.ComputeAPCost(launcher, true));
        }

        [Test]
        public void Rules_DistancePenalty_OnlyBeyond4_UnlessAimed()
        {
            Assert.AreEqual(0, GrenadeRules.DistancePenalty(4, false));
            Assert.AreEqual(-1, GrenadeRules.DistancePenalty(7, false));
            Assert.AreEqual(0, GrenadeRules.DistancePenalty(8, true));
        }

        [Test]
        public void Blast_FalloffDecreasesWithDistance_AndFloorAt25Pct()
        {
            var calc = new GrenadeCalculator(new DiceRoller(7));
            var g = MakeGrenade(flat: 10, nd10: 2, blast: 3);
            int c = calc.ComputeRawDamage(g, 11, 0);
            int r1 = calc.ComputeRawDamage(g, 11, 1);
            int r2 = calc.ComputeRawDamage(g, 11, 2);
            int r5 = calc.ComputeRawDamage(g, 11, 5);
            Assert.Greater(c, r1);
            Assert.Greater(r1, r2);
            // Plancher 25% : (10+11)*0.25=5 + shrap 2 = 7.
            Assert.AreEqual(7, r5);
        }

        [Test]
        public void Flash_DoesMinimalDamageBeyondEpicenter()
        {
            var calc = new GrenadeCalculator(new DiceRoller(9));
            var flash = MakeGrenade(flat: 2, nd10: 0, blast: 2, kind: "Flash", shrap: 0);
            int center = calc.ComputeRawDamage(flash, 0, 0);
            int ring = calc.ComputeRawDamage(flash, 0, 1);
            Assert.AreEqual(2, center);
            Assert.LessOrEqual(ring, 1);
        }

        [Test]
        public void Cover_ReducesDamage_AndFullBlocksShrapnel()
        {
            var calc = new GrenadeCalculator(new DiceRoller(11));
            var g = MakeGrenade(flat: 10, nd10: 0, blast: 2, shrap: 4);
            var target = MakeStats();
            int hp0 = target.CurrentHealth;
            var noCover = calc.ResolveHitOnTarget(target, g, 0, 1, 0, false);
            target.CurrentHealth = hp0; target.ActiveStatus = StatusEffect.None;
            var full = calc.ResolveHitOnTarget(target, g, 0, 1, 2, false);
            // Sans couvert : (10)*0.75=7 +4 shrap = 11 bruts. Full : 7 bruts (shrap bloqués) -4 couv = 3 avant armure.
            Assert.AreEqual(11, noCover.RawDamage);
            Assert.AreEqual(7, full.RawDamage);
            Assert.Greater(noCover.FinalDamage, full.FinalDamage);
        }

        [Test]
        public void Throw_CriticalAlwaysOnTarget_AndScatterBounded()
        {
            // 50 lancers seedés : la dispersion reste dans [0..3], jamais négative.
            var calc = new GrenadeCalculator(new DiceRoller(1234), seed: 42);
            var attacker = MakeStats();
            var g = MakeGrenade();
            for (int i = 0; i < 50; i++)
            {
                var o = calc.ResolveThrow(attacker, 6, g, null, 0, false);
                Assert.GreaterOrEqual(o.ScatterDistance, 0);
                Assert.LessOrEqual(o.ScatterDistance, 3);
                if (o.IsOnTarget) Assert.AreEqual(0, o.ScatterDistance);
                else Assert.GreaterOrEqual(o.ScatterDirIndex, 0);
            }
        }

        [Test]
        public void Statuses_ParseAndForceOnUtility()
        {
            var fx = GrenadeCalculator.ParseStatuses("Aveugle,Sourd,Etourdi");
            Assert.IsTrue(fx.HasFlag(StatusEffect.Aveugle));
            Assert.IsTrue(fx.HasFlag(StatusEffect.Sourd));
            var calc = new GrenadeCalculator(new DiceRoller(5));
            var flash = MakeGrenade(flat: 0, nd10: 0, blast: 2, kind: "Flash", shrap: 0);
            flash.GrenadeStatuses = "Aveugle,Sourd";
            var target = MakeStats(con: 10); // Encaissement élevé : aucun dégât ne dépasse.
            var hit = calc.ResolveHitOnTarget(target, flash, 0, 0, 0, true);
            Assert.IsTrue(hit.InflictedStatus.HasFlag(StatusEffect.Aveugle), "Le flash aveugle même sans dégât.");
        }

        [Test]
        public void Grenade_IsThrowable_AndLauncherCompatibleFlag()
        {
            var g = ArmoryCatalog.GetByName("Mills Bomb Mk1 (1915)");
            Assert.IsNotNull(g);
            Assert.IsTrue(g.IsThrowableGrenade());
            Assert.IsTrue(g.LauncherCompatible);
            var pot = ArmoryCatalog.GetByName("Pot à Feu (XVe)");
            Assert.IsNotNull(pot);
            Assert.IsFalse(pot.LauncherCompatible, "Poudre noire : main uniquement.");
            var lp = ArmoryCatalog.GetByName("Lance Plasma Nytharite LP-9");
            Assert.IsNotNull(lp);
            Assert.IsTrue(lp.IsLauncher);
            Assert.AreEqual(20, lp.RangeInTiles);
        }
    }
}
#endif

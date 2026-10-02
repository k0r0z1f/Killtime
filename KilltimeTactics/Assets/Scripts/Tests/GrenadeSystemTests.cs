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
        public void Cover_ThreeQuartersReducesBetweenHalfAndFull()
        {
            // Livre VI §25.3 : souffle réduit de 2 (Half), 3 (ThreeQuarters), 4 (Full).
            var calc = new GrenadeCalculator(new DiceRoller(11));
            var g = MakeGrenade(flat: 10, nd10: 0, blast: 2, shrap: 0);
            var target = MakeStats();
            int hp0 = target.CurrentHealth;
            var half = calc.ResolveHitOnTarget(target, g, 0, 1, 1, false);
            target.CurrentHealth = hp0; target.ActiveStatus = StatusEffect.None;
            var tq = calc.ResolveHitOnTarget(target, g, 0, 1, 3, false);
            target.CurrentHealth = hp0; target.ActiveStatus = StatusEffect.None;
            var full = calc.ResolveHitOnTarget(target, g, 0, 1, 2, false);
            Assert.AreEqual(GrenadeRules.HalfCoverReduction, half.CoverReduction);
            Assert.AreEqual(GrenadeRules.ThreeQuartersCoverReduction, tq.CoverReduction);
            Assert.AreEqual(GrenadeRules.FullCoverReduction, full.CoverReduction);
            Assert.Greater(half.FinalDamage, tq.FinalDamage);
            Assert.Greater(tq.FinalDamage, full.FinalDamage);
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

        [Test]
        public void AreaFire_ConeGeometry_ShotgunSpreadsProgressively()
        {
            var origin = new Killtime.Tactics.Grid.HexCoordinates(0, 0);
            var target = new Killtime.Tactics.Grid.HexCoordinates(4, 0);
            var cells = AreaFireRules.ComputeConeCells(origin, target, range: 4, spreadWidth: 1);

            Assert.IsNotNull(cells);
            Assert.Contains(new Killtime.Tactics.Grid.HexCoordinates(1, 0), cells);
            Assert.Contains(new Killtime.Tactics.Grid.HexCoordinates(2, 0), cells);
            Assert.Contains(new Killtime.Tactics.Grid.HexCoordinates(2, -1), cells);
            Assert.Contains(new Killtime.Tactics.Grid.HexCoordinates(1, 1), cells);

            for (int i = 0; i < cells.Count; i++)
            {
                int dist = origin.DistanceTo(cells[i]);
                Assert.GreaterOrEqual(dist, 1);
                Assert.LessOrEqual(dist, 4);
            }
        }

        [Test]
        public void AreaFire_BurstGeometry_IsStraightCorridor()
        {
            var origin = new Killtime.Tactics.Grid.HexCoordinates(0, 0);
            var target = new Killtime.Tactics.Grid.HexCoordinates(4, 0);
            var cells = AreaFireRules.ComputeConeCells(origin, target, range: 4, spreadWidth: 0);

            Assert.AreEqual(4, cells.Count);
            Assert.AreEqual(new Killtime.Tactics.Grid.HexCoordinates(1, 0), cells[0]);
            Assert.AreEqual(new Killtime.Tactics.Grid.HexCoordinates(2, 0), cells[1]);
            Assert.AreEqual(new Killtime.Tactics.Grid.HexCoordinates(3, 0), cells[2]);
            Assert.AreEqual(new Killtime.Tactics.Grid.HexCoordinates(4, 0), cells[3]);
        }

        [Test]
        public void AreaFire_Falloff_DecreasesWithDistanceAndFloors()
        {
            float f1 = AreaFireRules.ComputeDistanceFalloff(1, AreaFireMode.ShotgunCone);
            float f2 = AreaFireRules.ComputeDistanceFalloff(2, AreaFireMode.ShotgunCone);
            float f3 = AreaFireRules.ComputeDistanceFalloff(3, AreaFireMode.ShotgunCone);
            float f4 = AreaFireRules.ComputeDistanceFalloff(4, AreaFireMode.ShotgunCone);
            float f8 = AreaFireRules.ComputeDistanceFalloff(8, AreaFireMode.ShotgunCone);

            Assert.AreEqual(1.0f, f1, 0.001f);
            Assert.AreEqual(0.75f, f2, 0.001f);
            Assert.AreEqual(0.50f, f3, 0.001f);
            Assert.AreEqual(0.25f, f4, 0.001f);
            Assert.AreEqual(0.25f, f8, 0.001f, "Le plancher shotgun doit être verrouillé à 25%.");
        }

        [Test]
        public void AreaFire_Knockback_CalculatesNextCellCorrectly()
        {
            var origin = new Killtime.Tactics.Grid.HexCoordinates(0, 0);
            var target = new Killtime.Tactics.Grid.HexCoordinates(2, 0);
            var knock = AreaFireRules.ComputeKnockbackCell(origin, target);

            Assert.AreEqual(new Killtime.Tactics.Grid.HexCoordinates(3, 0), knock);
            Assert.AreEqual(3, origin.DistanceTo(knock));
        }

        [Test]
        public void AreaFire_FriendlyFire_HitsAlliesAndEnemiesInCone()
        {
            var calc = new AreaFireCalculator(new DiceRoller(10));
            var shooter = MakeStats(con: 5);
            var enemy = MakeStats(con: 3);
            var ally = MakeStats(con: 3);

            var origin = new Killtime.Tactics.Grid.HexCoordinates(0, 0);
            var target = new Killtime.Tactics.Grid.HexCoordinates(4, 0);

            var field = new System.Collections.Generic.List<(CharacterStats stats, Killtime.Tactics.Grid.HexCoordinates pos, bool isAlly)>
            {
                (enemy, new Killtime.Tactics.Grid.HexCoordinates(2, 0), false),
                (ally, new Killtime.Tactics.Grid.HexCoordinates(1, 0), true)
            };

            var profile = new AreaFireProfile
            {
                WeaponName = "Fusil à Pompe Cal.12",
                Mode = AreaFireMode.ShotgunCone,
                RangeTiles = 4,
                BaseDamage = 12,
                AmmoCost = 1,
                CausesKnockback = true
            };

            var res = calc.ResolveAreaFire(shooter, origin, target, profile, field, attackerBonusAP: 5);

            Assert.AreEqual(2, res.Hits.Count);
            Assert.AreEqual(1, res.FriendlyFireHits.Count);
            Assert.IsTrue(res.FriendlyFireHits[0].IsAlly);
            Assert.Greater(res.FriendlyFireHits[0].FinalDamage, 0);
            Assert.IsTrue(res.FriendlyFireHits[0].KnockedBack);
            Assert.AreEqual(new Killtime.Tactics.Grid.HexCoordinates(2, 0), res.FriendlyFireHits[0].KnockbackTargetCell);
        }

        [Test]
        public void AreaFire_AmmoCosts_MatchWeaponSpecifications()
        {
            Assert.AreEqual(1, AreaFireRules.DefaultAmmoCost(AreaFireMode.SingleShot));
            Assert.AreEqual(1, AreaFireRules.DefaultAmmoCost(AreaFireMode.ShotgunCone));
            Assert.AreEqual(3, AreaFireRules.DefaultAmmoCost(AreaFireMode.Burst));
            Assert.AreEqual(6, AreaFireRules.DefaultAmmoCost(AreaFireMode.FullAuto));
            Assert.AreEqual(10, AreaFireRules.DefaultAmmoCost(AreaFireMode.Suppression));
        }
    }
}
#endif

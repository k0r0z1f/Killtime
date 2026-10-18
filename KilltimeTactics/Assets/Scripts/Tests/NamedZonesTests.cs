#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using Killtime.Core.Combat;
using Killtime.Tactics.Grid;

namespace Killtime.Tests
{
    /// <summary>
    /// RD-188 Zones nommées : 6 profils miroirs catalogue (Kind/Radius/Turns/
    /// Statuses/Era), durées + overlays vérifiés, purge sélective. Pur, EditMode.
    /// </summary>
    [TestFixture]
    public class NamedZonesTests
    {
        [Test]
        public void All_HasSixProfiles_WithCatalogRadiiTurns()
        {
            Assert.AreEqual(6, NamedZoneProfiles.All.Count);
            var byKey = new Dictionary<string, NamedZoneProfile>();
            for (int i = 0; i < NamedZoneProfiles.All.Count; i++)
                byKey[NamedZoneProfiles.All[i].Key] = NamedZoneProfiles.All[i];

            Assert.AreEqual(2, byKey["M18"].Radius);
            Assert.AreEqual(2, byKey["M18"].Turns);
            Assert.AreEqual("Fumigene", byKey["M18"].Kind);

            Assert.AreEqual(1, byKey["ANM14"].Radius);
            Assert.AreEqual(2, byKey["ANM14"].Turns);
            Assert.AreEqual("Incendiaire", byKey["ANM14"].Kind);

            Assert.AreEqual(2, byKey["CM6"].Radius);
            Assert.AreEqual(2, byKey["CM6"].Turns);
            Assert.AreEqual("Gaz", byKey["CM6"].Kind);

            Assert.AreEqual(2, byKey["C9"].Radius);
            Assert.AreEqual(1, byKey["C9"].Turns);
            Assert.AreEqual("Cryo", byKey["C9"].Kind);

            Assert.AreEqual(2, byKey["T0"].Radius);
            Assert.AreEqual(2, byKey["T0"].Turns);
            Assert.AreEqual("Stase", byKey["T0"].Kind);

            Assert.AreEqual(3, byKey["KARK"].Radius);
            Assert.AreEqual(3, byKey["KARK"].Turns);
            Assert.AreEqual("Pestilentielle", byKey["KARK"].Kind);
        }

        [Test]
        public void TryGet_IsCaseInsensitive_UnknownReturnsFalse()
        {
            Assert.IsTrue(NamedZoneProfiles.TryGet("m18", out var m18));
            Assert.AreEqual("Fumigene", m18.Kind);
            Assert.IsTrue(NamedZoneProfiles.TryGet("kark", out _));
            Assert.IsFalse(NamedZoneProfiles.TryGet("NOPE", out _));
            Assert.IsFalse(NamedZoneProfiles.TryGet(null, out _));
        }

        [Test]
        public void Build_ParsesStatuses_AndArcanotechFlag()
        {
            Assert.IsTrue(NamedZoneProfiles.TryGet("CM6", out var cm6));
            var z = NamedZoneProfiles.BuildSmokeZone(cm6, new HexCoordinates(3, 0));
            Assert.AreEqual("Gaz", z.Kind);
            Assert.AreEqual(2, z.Radius);
            Assert.AreEqual(2, z.TurnsLeft);
            Assert.IsFalse(z.Arcanotech);
            Assert.IsTrue(z.Statuses.HasFlag(Killtime.Core.Character.StatusEffect.Aveugle));
            Assert.IsTrue(z.Statuses.HasFlag(Killtime.Core.Character.StatusEffect.Asphyxie));
            Assert.IsTrue(z.Statuses.HasFlag(Killtime.Core.Character.StatusEffect.Etourdi));

            Assert.IsTrue(NamedZoneProfiles.TryGet("T0", out var t0));
            var zt = NamedZoneProfiles.BuildSmokeZone(t0, new HexCoordinates(0, 0));
            Assert.IsTrue(zt.Arcanotech);
            Assert.IsTrue(zt.Statuses.HasFlag(Killtime.Core.Character.StatusEffect.Paralyse));

            Assert.IsTrue(NamedZoneProfiles.TryGet("KARK", out var kark));
            var zk = NamedZoneProfiles.BuildSmokeZone(kark, new HexCoordinates(1, 1));
            Assert.IsTrue(zk.Arcanotech);
            Assert.IsTrue(zk.Statuses.HasFlag(Killtime.Core.Character.StatusEffect.Empoisonne));
            Assert.IsTrue(zk.Statuses.HasFlag(Killtime.Core.Character.StatusEffect.Saignement));
            // Garde-fou parseur : "Empoisonne" contient "sonn" — ne doit pas mapper vers Sonne.
            Assert.IsFalse(zk.Statuses.HasFlag(Killtime.Core.Character.StatusEffect.Sonne));
        }

        [Test]
        public void ZoneDamage_MatchesSpec_Fire4_Gas2_Cryo2_Pestil3()
        {
            Assert.AreEqual(0, SmokeScreen.ZoneDamagePerTurn("Fumigene"));
            Assert.AreEqual(4, SmokeScreen.ZoneDamagePerTurn("Incendiaire"));
            Assert.AreEqual(2, SmokeScreen.ZoneDamagePerTurn("Gaz"));
            Assert.AreEqual(2, SmokeScreen.ZoneDamagePerTurn("Cryo"));
            Assert.AreEqual(0, SmokeScreen.ZoneDamagePerTurn("Stase"));
            Assert.AreEqual(3, SmokeScreen.ZoneDamagePerTurn("Pestilentielle"));
        }

        [Test]
        public void BlocksSight_OnlyFumigene()
        {
            Assert.IsTrue(SmokeScreen.BlocksSight("Fumigene"));
            Assert.IsFalse(SmokeScreen.BlocksSight("Incendiaire"));
            Assert.IsFalse(SmokeScreen.BlocksSight("Gaz"));
            Assert.IsFalse(SmokeScreen.BlocksSight("Cryo"));
            Assert.IsFalse(SmokeScreen.BlocksSight("Stase"));
            Assert.IsFalse(SmokeScreen.BlocksSight("Pestilentielle"));
        }

        [Test]
        public void OverlayColors_AreDistinctPerFamily()
        {
            var fumi = SmokeScreen.ZoneThemeColor("Fumigene");
            var feu = SmokeScreen.ZoneThemeColor("Incendiaire");
            var gaz = SmokeScreen.ZoneThemeColor("Gaz");
            var cryo = SmokeScreen.ZoneThemeColor("Cryo");
            var stase = SmokeScreen.ZoneThemeColor("Stase");
            var pestil = SmokeScreen.ZoneThemeColor("Pestilentielle");
            Assert.AreNotEqual(fumi, feu);
            Assert.AreNotEqual(feu, cryo);
            Assert.AreNotEqual(cryo, stase);
            Assert.AreNotEqual(stase, pestil);
            Assert.AreNotEqual(gaz, pestil);
        }

        [Test]
        public void PurgeByKind_RemovesOnlyMatchingKind()
        {
            var zones = new List<SmokeZone>();
            Assert.IsTrue(NamedZoneProfiles.TryGet("M18", out var m18));
            Assert.IsTrue(NamedZoneProfiles.TryGet("CM6", out var cm6));
            zones.Add(NamedZoneProfiles.BuildSmokeZone(m18, new HexCoordinates(0, 0)));
            zones.Add(NamedZoneProfiles.BuildSmokeZone(m18, new HexCoordinates(1, 0)));
            zones.Add(NamedZoneProfiles.BuildSmokeZone(cm6, new HexCoordinates(2, 0)));

            int removed = NamedZoneProfiles.PurgeByKind(zones, "Fumigene");
            Assert.AreEqual(2, removed);
            Assert.AreEqual(1, zones.Count);
            Assert.AreEqual("Gaz", zones[0].Kind);

            Assert.AreEqual(0, NamedZoneProfiles.PurgeByKind(zones, "Stase"));
            Assert.AreEqual(1, zones.Count);
            Assert.AreEqual(0, NamedZoneProfiles.PurgeByKind(null, "Gaz"));
            Assert.AreEqual(1, zones.Count);
        }

        [Test]
        public void ChronoFracture_IsNotAPersistentStatus_ParalyseOnly()
        {
            // Garde-fou : le parseur ignore "ChronoFracture" (pas un StatusEffect),
            // seule Paralyse persiste — la bulle T-0 reste un effet, pas un DoT.
            Assert.AreEqual(0, SmokeScreen.ZoneDamagePerTurn("Stase"));
            var parsed = GrenadeCalculator.ParseStatuses("Paralyse,ChronoFracture");
            Assert.IsTrue(parsed.HasFlag(Killtime.Core.Character.StatusEffect.Paralyse));
        }
    }
}
#endif

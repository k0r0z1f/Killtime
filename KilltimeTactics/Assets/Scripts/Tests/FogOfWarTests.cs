#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Killtime.Core.Character;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Visibility;

namespace Killtime.Tests
{
    /// <summary>
    /// Brouillard de guerre asymétrique (Livre VI §25.4) : 360° sans direction
    /// de vision, portée ENVIRONNEMENTALE (courte 6 / longue 12, jamais les
    /// stats), occlusion par les murs Full (CoverSystem), contact toujours
    /// visible, thermique, modificateurs de détection et rayon de bruit (Ouïe).
    /// Miroir des tests serveur (users/tests/vtt_visibility.test.js).
    /// </summary>
    [TestFixture]
    public class FogOfWarTests
    {
        private static HexNode MakeNode(int q, int r, CoverType cover)
        {
            var coords = new HexCoordinates(q, r);
            var node = new HexNode(coords, coords.ToWorldPosition(1f, 0f));
            node.Cover = cover;
            return node;
        }

        private static System.Func<HexCoordinates, HexNode> MakeField(params HexNode[] nodes)
        {
            var dict = new Dictionary<HexCoordinates, HexNode>();
            for (int i = 0; i < nodes.Length; i++)
                dict[nodes[i].Coordinates] = nodes[i];
            return c => dict.TryGetValue(c, out var n) ? n : null;
        }

        private static FogVisionParams Params(int range, bool thermal = false)
        {
            return new FogVisionParams
            {
                Range = range,
                HalfAngleDeg = 180f,
                Panoramic = true,
                Thermal = thermal,
                HearingRange = 3,
            };
        }

        private static CharacterStats MakeStats(int vision, int ouie, params string[] specs)
        {
            var attrs = new Attributes(@for: 3, agi: 3, con: 4, rap: 3, @int: 2, eru: 2, cha: 1, ins: 2, mag: 0, vision: vision, ouie: ouie);
            var sheet = new CharacterSheet { Name = "Test", BaseAttributes = attrs };
            if (specs != null)
                for (int i = 0; i < specs.Length; i++) sheet.UnlockedSpecializations.Add(specs[i]);
            return new CharacterStats("Test", attrs, 0, sheet);
        }

        [Test]
        public void NoFacing_KTSees360_AllDirectionsVisible()
        {
            var getNode = MakeField();
            var p = Params(FogOfWarSystem.LongSightRange);
            // Plein est comme plein sud, quel que soit le cap : pas de direction.
            Assert.IsTrue(FogOfWarSystem.IsCellVisible(new HexCoordinates(0, 0), 0f, new HexCoordinates(2, 0), getNode, p, 1f));
            Assert.IsTrue(FogOfWarSystem.IsCellVisible(new HexCoordinates(0, 0), 180f, new HexCoordinates(2, 0), getNode, p, 1f));
            Assert.IsTrue(FogOfWarSystem.IsCellVisible(new HexCoordinates(0, 0), 90f, new HexCoordinates(0, 2), getNode, p, 1f));
            Assert.IsTrue(FogOfWarSystem.IsInCone(Vector3.zero, 0f, Vector3.right * 5f,
                new FogVisionParams { Panoramic = true, HalfAngleDeg = 180f }));
        }

        [Test]
        public void EnvironmentalRange_Short6_Long12()
        {
            var getNode = MakeField();
            var shortP = Params(FogOfWarSystem.ShortSightRange);
            Assert.IsTrue(FogOfWarSystem.IsCellVisible(new HexCoordinates(0, 0), 0f, new HexCoordinates(0, 6), getNode, shortP, 1f));
            Assert.IsFalse(FogOfWarSystem.IsCellVisible(new HexCoordinates(0, 0), 0f, new HexCoordinates(0, 7), getNode, shortP, 1f));
            var longP = Params(FogOfWarSystem.LongSightRange);
            Assert.IsTrue(FogOfWarSystem.IsCellVisible(new HexCoordinates(0, 0), 0f, new HexCoordinates(0, 10), getNode, longP, 1f));
            Assert.IsFalse(FogOfWarSystem.IsCellVisible(new HexCoordinates(0, 0), 0f, new HexCoordinates(0, 13), getNode, longP, 1f));
        }

        [Test]
        public void RangeIgnoresVisionStat()
        {
            // Vision 1 ou 6 : même portée d'ambiance (la stat sert aux jets).
            var weak = MakeStats(1, 3);
            var keen = MakeStats(6, 3);
            Assert.AreEqual(
                FogOfWarSystem.ResolveObserverParams(weak, FogOfWarSystem.LongSightRange).Range,
                FogOfWarSystem.ResolveObserverParams(keen, FogOfWarSystem.LongSightRange).Range);
            Assert.AreEqual(FogOfWarSystem.LongSightRange,
                FogOfWarSystem.ResolveObserverParams(keen, FogOfWarSystem.LongSightRange).Range);
        }

        [Test]
        public void Contact_AlwaysVisible()
        {
            var getNode = MakeField();
            var p = Params(FogOfWarSystem.ShortSightRange);
            Assert.IsTrue(FogOfWarSystem.IsCellVisible(new HexCoordinates(0, 0), 0f, new HexCoordinates(1, 0), getNode, p, 1f));
        }

        [Test]
        public void FullWall_BlocksVision()
        {
            var getNode = MakeField(
                MakeNode(0, 0, CoverType.Full), MakeNode(0, 1, CoverType.Full), MakeNode(0, 2, CoverType.Full));
            var p = Params(FogOfWarSystem.LongSightRange);
            Assert.IsFalse(FogOfWarSystem.IsCellVisible(new HexCoordinates(0, -1), 0f, new HexCoordinates(0, 3), getNode, p, 1f));
        }

        [Test]
        public void HalfCover_NeverBlocksVision()
        {
            var getNode = MakeField(MakeNode(0, 1, CoverType.Half));
            var p = Params(FogOfWarSystem.LongSightRange);
            Assert.IsTrue(FogOfWarSystem.IsCellVisible(new HexCoordinates(0, -1), 0f, new HexCoordinates(0, 3), getNode, p, 1f));
        }

        [Test]
        public void Thermal_SeesThroughUpToHalfRange()
        {
            var getNode = MakeField(
                MakeNode(0, 0, CoverType.Full), MakeNode(0, 1, CoverType.Full), MakeNode(0, 2, CoverType.Full),
                MakeNode(0, 3, CoverType.Full), MakeNode(0, 4, CoverType.Full), MakeNode(0, 5, CoverType.Full));
            var p = Params(FogOfWarSystem.LongSightRange, thermal: true);
            // Longue 12 -> thermique jusqu'à 6 : dist 6 visible, dist 7 aveugle.
            Assert.IsTrue(FogOfWarSystem.IsCellVisible(new HexCoordinates(0, -1), 0f, new HexCoordinates(0, 5), getNode, p, 1f));
            Assert.IsFalse(FogOfWarSystem.IsCellVisible(new HexCoordinates(0, -1), 0f, new HexCoordinates(0, 6), getNode, p, 1f));
        }

        [Test]
        public void ResolveObserverParams_AmbiancePlusSenses()
        {
            var plain = MakeStats(4, 2);
            var pp = FogOfWarSystem.ResolveObserverParams(plain, FogOfWarSystem.ShortSightRange);
            Assert.AreEqual(FogOfWarSystem.ShortSightRange, pp.Range);
            Assert.AreEqual(2, pp.HearingRange);
            Assert.IsTrue(pp.Panoramic);
            Assert.IsFalse(pp.Thermal);

            var therm = MakeStats(3, 3, FogOfWarSystem.SpecThermal);
            Assert.IsTrue(FogOfWarSystem.ResolveObserverParams(therm, 10).Thermal);
        }

        [Test]
        public void DetectionModifier_DistanceCoverThermal()
        {
            Assert.AreEqual(0, FogOfWarSystem.BuildDetectionModifier(1, CoverType.None, false));
            Assert.AreEqual(-2, FogOfWarSystem.BuildDetectionModifier(3, CoverType.None, false));
            Assert.AreEqual(-3, FogOfWarSystem.BuildDetectionModifier(3, CoverType.Half, false));
            Assert.AreEqual(-4, FogOfWarSystem.BuildDetectionModifier(3, CoverType.ThreeQuarters, false));
            Assert.AreEqual(-6, FogOfWarSystem.BuildDetectionModifier(3, CoverType.Full, false));
            Assert.AreEqual(-4, FogOfWarSystem.BuildDetectionModifier(3, CoverType.Full, true));
        }

        [Test]
        public void NoiseRevealRadius_EqualsOuie()
        {
            Assert.AreEqual(5, FogOfWarSystem.NoiseRevealRadius(MakeStats(3, 5)));
            Assert.AreEqual(1, FogOfWarSystem.NoiseRevealRadius(MakeStats(3, 1)));
        }

        [Test]
        public void SquadVisibleCells_UnionAndWallShadow()
        {
            var getNode = MakeField(
                MakeNode(0, 0, CoverType.Full), MakeNode(0, 1, CoverType.Full), MakeNode(0, 2, CoverType.Full));
            var observers = new List<(HexCoordinates coords, float yawDeg, FogVisionParams vision)>
            {
                (new HexCoordinates(0, -1), 0f, Params(FogOfWarSystem.LongSightRange)),
                (new HexCoordinates(5, 5), 0f, Params(FogOfWarSystem.ShortSightRange)),
            };
            var visible = FogOfWarSystem.ComputeSquadVisibleCells(observers, getNode, 1f);
            // L'observateur voit sa case et le sud non masqué...
            Assert.IsTrue(visible.Contains(new HexCoordinates(0, -1)));
            Assert.IsTrue(visible.Contains(new HexCoordinates(0, -2)));
            // ...mais pas derrière le mur Full.
            Assert.IsFalse(visible.Contains(new HexCoordinates(0, 3)));
            // Le second observateur couvre son voisinage à 360°.
            Assert.IsTrue(visible.Contains(new HexCoordinates(5, 5)));
            Assert.IsTrue(visible.Contains(new HexCoordinates(7, 5)));
        }

        [TearDown]
        public void ClearPropRegistry() => PropObstacleRegistry.Clear();

        private static PropObstacle TallPillar(float minY = 0f, float maxY = 2.5f)
        {
            var c = new HexCoordinates(0, 1).ToWorldPosition(1f, 0f);
            return new PropObstacle
            {
                CenterXZ = new Vector2(c.x, c.z),
                Radius = 0.4f,
                MinY = minY,
                MaxY = maxY,
                HasMeshBounds = false,
                PropName = "TestPilier"
            };
        }

        [Test]
        public void FogLineOfSight_TallPropBlocks_LowCratePasses()
        {
            var from = new HexCoordinates(0, -1);
            var to = new HexCoordinates(0, 3);
            // Pilier haut (2,5 m) sur case marquée None : bloque quand même.
            PropObstacleRegistry.SetForTests(new HexCoordinates(0, 1), TallPillar());
            Assert.IsFalse(FogOfWarSystem.HasFogLineOfSight(from, to, MakeField(), 1f, false));
            PropObstacleRegistry.Clear();
            // Caisse basse (0,5 m) : laisse voir.
            PropObstacleRegistry.SetForTests(new HexCoordinates(0, 1), TallPillar(0f, 0.5f));
            Assert.IsTrue(FogOfWarSystem.HasFogLineOfSight(from, to, MakeField(), 1f, false));
            PropObstacleRegistry.Clear();
            // Suspendu passant au-dessus du rayon (bas à 3 m) : laisse voir.
            PropObstacleRegistry.SetForTests(new HexCoordinates(0, 1), TallPillar(3f, 3.5f));
            Assert.IsTrue(FogOfWarSystem.HasFogLineOfSight(from, to, MakeField(), 1f, false));
        }

        [Test]
        public void FogLineOfSight_ReliefBlocks_EndpointsExcluded()
        {
            var ridgeCoords = new HexCoordinates(0, 1);
            var ridge = new HexNode(ridgeCoords, ridgeCoords.ToWorldPosition(1f, 2f)); // crête +2 m.
            var getNode = MakeField(ridge);
            // La crête avale le rayon œil 1,5 m ➔ buste 0,9 m.
            Assert.IsFalse(FogOfWarSystem.HasFogLineOfSight(
                new HexCoordinates(0, -1), new HexCoordinates(0, 3), getNode, 1f, false));
            // Mais un mur Full POSÉ sur la case observateur ou cible n'aveugle pas.
            var getNodeEnds = MakeField(
                MakeNode(0, -1, CoverType.Full), MakeNode(0, 3, CoverType.Full));
            Assert.IsTrue(FogOfWarSystem.HasFogLineOfSight(
                new HexCoordinates(0, -1), new HexCoordinates(0, 3), getNodeEnds, 1f, false));
            // Thermique : ignore les murs.
            var getNodeWall = MakeField(MakeNode(0, 1, CoverType.Full));
            Assert.IsFalse(FogOfWarSystem.HasFogLineOfSight(
                new HexCoordinates(0, -1), new HexCoordinates(0, 3), getNodeWall, 1f, false));
            Assert.IsTrue(FogOfWarSystem.HasFogLineOfSight(
                new HexCoordinates(0, -1), new HexCoordinates(0, 3), getNodeWall, 1f, true));
        }
    }
}
#endif

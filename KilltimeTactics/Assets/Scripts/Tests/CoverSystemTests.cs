#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Killtime.Tactics.Grid;

namespace Killtime.Tests
{
    /// <summary>
    /// Validation du cône de visée rigoureux (Livre VI §25.3) : meilleur coin
    /// attaquant + 28 rayons sur la hauteur de la cible, prismes 3D exacts.
    /// </summary>
    [TestFixture]
    public class CoverSystemTests
    {
        private static HexNode MakeNode(int q, int r, CoverType cover, float baseY = 0f)
        {
            var coords = new HexCoordinates(q, r);
            var node = new HexNode(coords, coords.ToWorldPosition(1f, baseY));
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

        private static CoverScanParams Params() => CoverScanParams.CodexDefaults(1f);

        [TearDown]
        public void ClearPropRegistry() => PropObstacleRegistry.Clear();

        private static PropObstacle PillarAt(int q, int r, float minY, float maxY, float radius)
        {
            var coords = new HexCoordinates(q, r);
            Vector3 w = coords.ToWorldPosition(1f, 0f);
            return new PropObstacle
            {
                CenterXZ = new Vector2(w.x, w.z),
                Radius = radius,
                MinY = minY,
                MaxY = maxY,
                PropName = "TestPilier"
            };
        }

        [Test]
        public void OpenField_IsFullyVisible_NoPenalty()
        {
            var getNode = MakeField();
            var res = CoverSystem.ScanCover(
                new HexCoordinates(0, 0), new HexCoordinates(4, 0), getNode, Params());
            Assert.AreEqual(CoverType.None, res.Cover);
            Assert.AreEqual(1f, res.VisibleFraction, 1e-4f);
            Assert.AreEqual(28, res.TestedRays);
            Assert.IsTrue(res.HasBestOrigin);
        }

        [Test]
        public void FullBarrier_BlocksEveryRay_AttackImpossible()
        {
            // Mur continu de 2×3 : aucun rayon ne le contourne ni ne passe dessus.
            var getNode = MakeField(
                MakeNode(1, -1, CoverType.Full), MakeNode(1, 0, CoverType.Full), MakeNode(1, 1, CoverType.Full),
                MakeNode(2, -1, CoverType.Full), MakeNode(2, 0, CoverType.Full), MakeNode(2, 1, CoverType.Full));
            var res = CoverSystem.ScanCover(
                new HexCoordinates(0, 0), new HexCoordinates(4, 0), getNode, Params());
            Assert.AreEqual(CoverType.Full, res.Cover);
            Assert.AreEqual(0f, res.VisibleFraction, 1e-4f);
            // Chaque rayon bloqué expose un point d'impact strictement entre
            // l'origine (coin attaquant) et la cible.
            Assert.AreEqual(28, res.Rays.Count);
            for (int i = 0; i < res.Rays.Count; i++)
            {
                Assert.IsTrue(res.Rays[i].Blocked);
                Assert.IsTrue(res.Rays[i].HasHitPoint);
                Assert.Greater(Vector3.Distance(res.Rays[i].Origin, res.Rays[i].HitPoint), 0.01f);
                Assert.Greater(Vector3.Distance(res.Rays[i].HitPoint, res.Rays[i].Target), 0.01f);
            }
        }

        [Test]
        public void SingleLowWall_GivesHalfCover()
        {
            // Muret 0.7 sur l'axe : bloque le bas du corps, laisse passer le haut.
            var getNode = MakeField(MakeNode(2, 0, CoverType.Half));
            var res = CoverSystem.ScanCover(
                new HexCoordinates(0, 0), new HexCoordinates(4, 0), getNode, Params());
            Assert.AreEqual(CoverType.Half, res.Cover);
            Assert.Greater(res.VisibleFraction, 0.2f);
            Assert.Less(res.VisibleFraction, 0.9f);
        }

        [Test]
        public void OffAxisWall_IsIgnoredByBestCorner()
        {
            // Muret loin de l'axe : le meilleur coin l'évite entièrement.
            var getNode = MakeField(MakeNode(2, 2, CoverType.Half));
            var res = CoverSystem.ScanCover(
                new HexCoordinates(0, 0), new HexCoordinates(4, 0), getNode, Params());
            Assert.AreEqual(CoverType.None, res.Cover);
            Assert.AreEqual(1f, res.VisibleFraction, 1e-4f);
        }

        [Test]
        public void HighRidge_BlocksLowRays_TerrainCountsAsCover()
        {
            // Crête à +2 m sans aucun obstacle posé : le relief seul masque la cible.
            var getNode = MakeField(MakeNode(2, 0, CoverType.None, baseY: 2f));
            var res = CoverSystem.ScanCover(
                new HexCoordinates(0, 0), new HexCoordinates(4, 0), getNode, Params());
            Assert.AreEqual(CoverType.Full, res.Cover);
            Assert.AreEqual(0f, res.VisibleFraction, 1e-4f);
        }
        [Test]
        public void PropPillar_BlocksRays_EvenOnNoneCell()
        {
            // Pilier fin (r=0.4, 2.5 m) sur une case marquée None : il masque une
            // partie du cône sans tout bloquer (les rayons le contournent).
            PropObstacleRegistry.SetForTests(new HexCoordinates(2, 0), PillarAt(2, 0, 0f, 2.5f, 0.4f));
            var res = CoverSystem.ScanCover(
                new HexCoordinates(0, 0), new HexCoordinates(4, 0), MakeField(), Params());
            Assert.Less(res.VisibleFraction, 1f);
            Assert.Greater(res.VisibleFraction, 0f);
        }

        [Test]
        public void PropCrate_BlocksOnlyLowRays()
        {
            // Caisse 0.5 m : ne masque que le bas du corps (fraction haute).
            PropObstacleRegistry.SetForTests(new HexCoordinates(2, 0), PillarAt(2, 0, 0f, 0.5f, 0.8f));
            var res = CoverSystem.ScanCover(
                new HexCoordinates(0, 0), new HexCoordinates(4, 0), MakeField(), Params());
            Assert.AreEqual(CoverType.Half, res.Cover);
            Assert.Greater(res.VisibleFraction, 0.45f);
            Assert.Less(res.VisibleFraction, 0.9f);
        }

        [Test]
        public void PropWideWall_NearlyBlindsTarget()
        {
            // Mur prop large (r=1.0, 2.5 m) : ne laisse passer qu'une frange.
            PropObstacleRegistry.SetForTests(new HexCoordinates(2, 0), PillarAt(2, 0, 0f, 2.5f, 1.0f));
            var res = CoverSystem.ScanCover(
                new HexCoordinates(0, 0), new HexCoordinates(4, 0), MakeField(), Params());
            Assert.Less(res.VisibleFraction, 0.45f);
        }

        [Test]
        public void MeshBounds_AreTestedExactly()
        {
            // Borne mesh affichée (mur de 1.6 m de large, 2.5 m de haut) : le cône
            // s'effondre, que la case soit marquée ou non.
            var coords = new HexCoordinates(2, 0);
            Vector3 c = coords.ToWorldPosition(1f, 0f);
            var bounds = new Bounds(c + Vector3.up * 1.25f, new Vector3(1.6f, 2.5f, 1.6f));
            PropObstacleRegistry.SetForTests(coords, new PropObstacle
            {
                CenterXZ = new Vector2(c.x, c.z),
                Radius = 0.8f,
                MinY = 0f,
                MaxY = 2.5f,
                MeshBounds = bounds,
                HasMeshBounds = true,
                PropName = "MurAffiche"
            });
            var res = CoverSystem.ScanCover(
                new HexCoordinates(0, 0), new HexCoordinates(4, 0), MakeField(), Params());
            Assert.Less(res.VisibleFraction, 0.45f);
            int blocked = 0;
            for (int i = 0; i < res.Rays.Count; i++)
                if (res.Rays[i].Blocked && res.Rays[i].HasHitPoint) blocked++;
            Assert.Greater(blocked, 0);
        }

        [Test]
        public void HexCorners_MatchVisualMeshOrientation()
        {
            // Les coins du scan doivent coïncider avec le maillage (30° + 60°×i).
            for (int i = 0; i < 6; i++)
            {
                Vector2 off = CoverSystem.HexCornerOffset(i, 1f);
                float expectedAngle = (30f + 60f * i) * Mathf.Deg2Rad;
                Assert.AreEqual(Mathf.Cos(expectedAngle), off.x, 1e-5f);
                Assert.AreEqual(Mathf.Sin(expectedAngle), off.y, 1e-5f);
            }
        }
    }
}
#endif

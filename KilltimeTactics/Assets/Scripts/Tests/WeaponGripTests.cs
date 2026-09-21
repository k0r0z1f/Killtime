#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Killtime.Core.Inventory;

namespace Killtime.Tests
{
    [TestFixture]
    public class WeaponGripTests
    {
        [Test]
        public void WeaponGripProfile_Serialization_PreservesAllFields()
        {
            var profile = new WeaponGripProfile
            {
                Key = "Gourdin / Matraque",
                Socket = WeaponGripSocket.RightHand,
                PositionOffset = new Vector3(0.04f, 0.08f, -0.02f),
                RotationOffset = new Vector3(15f, 0f, 90f),
                TargetWorldLength = 0.65f,
                ScaleMultiplier = 1.15f,
                GripPivotOffset = new Vector3(0f, -0.15f, 0f)
            };

            string json = JsonUtility.ToJson(profile);
            Assert.IsNotNull(json);
            StringAssert.Contains("Gourdin / Matraque", json);
            StringAssert.Contains("\"Socket\":0", json);

            var loaded = JsonUtility.FromJson<WeaponGripProfile>(json);
            Assert.IsNotNull(loaded);
            Assert.AreEqual("Gourdin / Matraque", loaded.Key);
            Assert.AreEqual(WeaponGripSocket.RightHand, loaded.Socket);
            Assert.AreEqual(0.04f, loaded.PositionOffset.x, 0.001f);
            Assert.AreEqual(15f, loaded.RotationOffset.x, 0.001f);
            Assert.AreEqual(0.65f, loaded.TargetWorldLength, 0.001f);
            Assert.AreEqual(1.15f, loaded.ScaleMultiplier, 0.001f);
            Assert.AreEqual(-0.15f, loaded.GripPivotOffset.y, 0.001f);
        }

        [Test]
        public void WeaponGripService_ResolvesClubProfileToRightHand()
        {
            WeaponGripService.EnsureInitialized();

            var clubItem = ArmoryCatalog.GetByName("Gourdin / Matraque");
            Assert.IsNotNull(clubItem);

            var profile = WeaponGripService.ResolveProfile(clubItem);
            Assert.IsNotNull(profile);
            Assert.AreEqual(WeaponGripSocket.RightHand, profile.Socket, "Le gourdin doit être dans la main droite par défaut");
            Assert.Greater(profile.TargetWorldLength, 0.4f, "La longueur cible doit être définie");
        }

        [Test]
        public void WeaponGripService_ResolvesRifleToChestTwoHands()
        {
            WeaponGripService.EnsureInitialized();

            var rifle = ArmoryCatalog.GetByName("Fusil d'Assaut Laser");
            Assert.IsNotNull(rifle);

            var profile = WeaponGripService.ResolveProfile(rifle);
            Assert.IsNotNull(profile);
            Assert.AreEqual(WeaponGripSocket.ChestTwoHands, profile.Socket, "Les fusils à deux mains s'ancrent sur la poitrine / deux mains");
        }
    }
}
#endif
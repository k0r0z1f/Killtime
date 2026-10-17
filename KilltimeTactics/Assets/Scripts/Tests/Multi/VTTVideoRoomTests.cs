#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEngine;
using Killtime.Multi.Video;

namespace Killtime.Multi.Tests
{
    [TestFixture]
    public class VTTVideoRoomTests
    {
        [Test]
        public void PopupVideoManager_OpensAndClosesPopups()
        {
            VTTPopupVideoManager.EnsureExists();
            var manager = VTTPopupVideoManager.Instance;
            Assert.IsNotNull(manager);

            string testClientId = "client_alpha_99";
            Assert.IsFalse(manager.IsPoppedOut(testClientId));

            manager.OpenPopup(testClientId, "Guerrier_Alpha", isLocal: false);
            Assert.IsTrue(manager.IsPoppedOut(testClientId));

            manager.ClosePopup(testClientId);
            Assert.IsFalse(manager.IsPoppedOut(testClientId));
        }

        [Test]
        public void VideoRoom_GatherParticipants_IncludesLocalParticipant()
        {
            var participants = VTTVideoRoomWindow.GatherParticipants(null);
            Assert.IsNotNull(participants);
            Assert.GreaterOrEqual(participants.Count, 1);
            Assert.IsTrue(participants[0].IsLocal);
        }

        [Test]
        public void PopupVideoManager_ReintegrateAll_ClearsAllPopups()
        {
            VTTPopupVideoManager.EnsureExists();
            var manager = VTTPopupVideoManager.Instance;

            manager.OpenPopup("c1", "Player_1");
            manager.OpenPopup("c2", "Player_2");
            Assert.IsTrue(manager.IsPoppedOut("c1"));
            Assert.IsTrue(manager.IsPoppedOut("c2"));

            manager.ReintegrateAll();
            Assert.IsFalse(manager.IsPoppedOut("c1"));
            Assert.IsFalse(manager.IsPoppedOut("c2"));
        }
    }
}
#endif
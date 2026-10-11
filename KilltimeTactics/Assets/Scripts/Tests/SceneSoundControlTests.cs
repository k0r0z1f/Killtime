#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using NUnit.Framework;
using Killtime.Story.Data;

namespace Killtime.Tests
{
    /// <summary>
    /// Contrôle total des sons de scène : données, finders, défauts rétro-compatibles.
    /// EditMode, pur C# (aucun AudioSource, aucune grille).
    /// </summary>
    [TestFixture]
    public class SceneSoundControlTests
    {
        private static StorySceneData NewSceneWithSound(SceneSoundData snd)
        {
            var data = new StorySceneData { SceneId = "snd_ctrl" };
            data.Nodes.Add(new SceneNodeData { NodeId = "node_1" });
            data.Sounds.Add(snd);
            return data;
        }

        [Test]
        public void EnsureDeepDefaults_InitializesAudioSettingsWithLegacyBehavior()
        {
            var data = new StorySceneData { SceneId = "snd_defaults" };
            data.AudioSettings = null;
            StorySceneData.EnsureDeepDefaults(data);

            Assert.IsNotNull(data.AudioSettings);
            Assert.AreEqual(1f, data.AudioSettings.MasterVolumeScale);
            Assert.IsFalse(data.AudioSettings.Muted);
            // Rétro-compat : pas de kill inter-nœuds par défaut, kill sortie oui,
            // cartes prioritaires sur le legacy (évite les doubles déclenchements).
            Assert.IsFalse(data.AudioSettings.StopAllSoundsOnNodeChange);
            Assert.IsTrue(data.AudioSettings.StopAllSoundsOnSceneExit);
            Assert.IsTrue(data.AudioSettings.PreferCardsOverLegacy);
        }

        [Test]
        public void EnsureDeepDefaults_ClampsNewSoundFields()
        {
            var data = NewSceneWithSound(new SceneSoundData
            {
                SoundId = "snd_1",
                PitchScale = 99f,
                Priority = 999,
                CooldownSeconds = -5f,
                FadeInDuration = -1f,
                FadeOutDuration = 99f,
                Radius = 0f,
                TriggerPoint = (SoundTriggerPoint)999,
                CardHeight = 0f
            });
            StorySceneData.EnsureDeepDefaults(data);
            var snd = data.Sounds[0];

            Assert.AreEqual(3f, snd.PitchScale);
            Assert.AreEqual(255, snd.Priority);
            Assert.AreEqual(0f, snd.CooldownSeconds);
            Assert.AreEqual(0f, snd.FadeInDuration);
            Assert.AreEqual(20f, snd.FadeOutDuration);
            Assert.AreEqual(15f, snd.Radius);
            Assert.AreEqual(SoundTriggerPoint.AtLinkedStart, snd.TriggerPoint);
            Assert.GreaterOrEqual(snd.CardHeight, 270f);
        }

        [Test]
        public void FindSoundsFrom_IsCaseInsensitive()
        {
            var data = NewSceneWithSound(new SceneSoundData
            {
                SoundId = "snd_1",
                SoundCueId = "Cinematic_WarpSwell",
                SourceCardId = "Line_Intro"
            });

            Assert.AreEqual(1, data.FindSoundsFrom("line_intro").Count);
            Assert.AreEqual(1, data.FindSoundsFrom("LINE_INTRO").Count);
            Assert.AreEqual(0, data.FindSoundsFrom("line_missing").Count);
        }

        [Test]
        public void FindSoundsStoppingFrom_MirrorsAmbienceStops()
        {
            var data = NewSceneWithSound(new SceneSoundData
            {
                SoundId = "snd_loop",
                SoundCueId = "Ambience_AsteroidBase",
                SourceCardId = "line_a",
                StopSourceCardId = "line_b",
                Loop = true
            });

            var stops = data.FindSoundsStoppingFrom("LINE_B");
            Assert.AreEqual(1, stops.Count);
            Assert.AreEqual("snd_loop", stops[0].SoundId);
            Assert.AreEqual(0, data.FindSoundsStoppingFrom("line_a").Count);
        }

        [Test]
        public void HasLinkedSounds_IgnoresMutedAndEmptyCues()
        {
            var data = new StorySceneData { SceneId = "snd_linked" };
            data.Sounds.Add(new SceneSoundData { SoundId = "snd_ok", SoundCueId = "UI_Click", SourceCardId = "line_1" });
            data.Sounds.Add(new SceneSoundData { SoundId = "snd_mute", SoundCueId = "UI_Click", SourceCardId = "line_2", Muted = true });
            data.Sounds.Add(new SceneSoundData { SoundId = "snd_empty", SoundCueId = "", SourceCardId = "line_3" });

            Assert.IsTrue(data.HasLinkedSounds("line_1"));
            Assert.IsFalse(data.HasLinkedSounds("line_2"));
            Assert.IsFalse(data.HasLinkedSounds("line_3"));
            Assert.IsFalse(data.HasLinkedSounds("line_missing"));
        }

        [Test]
        public void GetSummary_ExposesTotalControlFlags()
        {
            var snd = new SceneSoundData
            {
                SoundId = "snd_1",
                SoundCueId = "UI_Click",
                SourceCardId = "line_1",
                StopSourceCardId = "line_9",
                Loop = true,
                Muted = true,
                Spatialized = true
            };
            string summary = snd.GetSummary();

            StringAssert.Contains("UI_Click", summary);
            StringAssert.Contains("line_1", summary);
            StringAssert.Contains("line_9", summary);
            StringAssert.Contains("∞", summary);
            StringAssert.Contains("🔇", summary);
            StringAssert.Contains("3D", summary);
        }
    }
}
#endif

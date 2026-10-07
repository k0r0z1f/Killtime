#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using NUnit.Framework;
using UnityEngine;
using Killtime.Audio;

namespace Killtime.Tests
{
    /// <summary>
    /// Validation du système son : couverture procédurale + définitions par défaut.
    /// EditMode, sans Play (AudioClip.Create autorisé dans l'éditeur).
    /// </summary>
    public class AudioSystemTests
    {
        [Test]
        public void AllSoundIds_HaveProceduralFallback()
        {
            foreach (SoundId id in System.Enum.GetValues(typeof(SoundId)))
            {
                if (id == SoundId.None) continue;
                var clip = ProceduralAudioFactory.GetClip(id);
                Assert.IsNotNull(clip, $"Pas de fallback procédural pour {id}");
                Assert.Greater(clip.samples, 0, $"Clip vide pour {id}");
                Assert.AreEqual(ProceduralAudioFactory.SampleRate, clip.frequency);
                Assert.AreEqual(1, clip.channels, "Mono attendu (WebGL léger)");
            }
        }

        [Test]
        public void AllMusicMoods_HaveLoopableClip()
        {
            foreach (MusicMood mood in System.Enum.GetValues(typeof(MusicMood)))
            {
                if (mood == MusicMood.None) continue;
                var clip = ProceduralAudioFactory.GetMusicLoop(mood);
                Assert.IsNotNull(clip, $"Pas de boucle pour {mood}");
                // 8s à 22050 Hz
                Assert.Greater(clip.samples, ProceduralAudioFactory.SampleRate * 4);
            }
        }

        [Test]
        public void PlanetSwells_CrescendoAndResolve()
        {
            // Montée statique (cine_11 warp 5s, cine_1 approche 10s), paire soudée :
            // le warp part du silence et SOUTIENT jusqu'au bout (crescendo), puis
            // l'approche démarre à chaud au même niveau (pas de redémarrage) et ne
            // se relâche qu'à la fin (fin de cine_1 → dialogue). Déterministe.
            var warp = ProceduralAudioFactory.GetClip(SoundId.Cinematic_WarpSwell);
            Assert.IsNotNull(warp, "Pas de clip pour Cinematic_WarpSwell");
            float[] w = new float[warp.samples];
            Assert.IsTrue(warp.GetData(w, 0));
            int half = w.Length / 2;
            double wFirst = 0, wSecond = 0;
            for (int i = 0; i < half; i++) wFirst += w[i] * w[i];
            for (int i = half; i < w.Length; i++) wSecond += w[i] * w[i];
            Assert.Greater(wSecond, wFirst * 1.5, "WarpSwell n'amplifie pas (crescendo attendu)");

            var appr = ProceduralAudioFactory.GetClip(SoundId.Cinematic_ApproachSwell);
            Assert.IsNotNull(appr, "Pas de clip pour Cinematic_ApproachSwell");
            float[] a = new float[appr.samples];
            Assert.IsTrue(appr.GetData(a, 0));
            double aHead = 0, aMid = 0, aTail = 0;
            int n10 = a.Length / 10, n3 = a.Length * 3 / 100;
            for (int i = 0; i < n10; i++) aHead += a[i] * a[i];
            for (int i = a.Length * 4 / 10; i < a.Length * 6 / 10; i++) aMid += a[i] * a[i];
            for (int i = a.Length - n3; i < a.Length; i++) aTail += a[i] * a[i];
            aHead /= n10; aMid /= (a.Length / 5); aTail /= n3;
            Assert.Greater(aHead, aMid * 0.3, "ApproachSwell redémarre du silence (départ à chaud attendu)");
            Assert.Less(aTail, aMid * 0.3, "ApproachSwell ne se relâche pas en fin");

            // Déterminisme : régénération identique (cache vidé).
            ProceduralAudioFactory.ClearCache();
            var warp2 = ProceduralAudioFactory.GetClip(SoundId.Cinematic_WarpSwell);
            float[] w2 = new float[warp2.samples];
            Assert.IsTrue(warp2.GetData(w2, 0));
            Assert.AreEqual(w.Length, w2.Length);
            for (int i = 0; i < w.Length; i += 997)
                Assert.AreEqual(w[i], w2[i], 1e-6f, $"WarpSwell non déterministe @ {i}");

            // Durées : warp 5s, approche 10s (22050 Hz).
            Assert.AreEqual(5 * ProceduralAudioFactory.SampleRate, warp.samples);
            Assert.AreEqual(10 * ProceduralAudioFactory.SampleRate, appr.samples);
        }

        [Test]
        public void SoundBank_ParseSample_PreservesRecipe()
        {
            // Recette HBO minimale (même forme que Resources/Data/SoundBank.json).
            const string sample = @"{""version"":1,""entries"":[{""id"":""Cinematic_OpeningSwell"",""label"":""HBO"",""category"":""Cinématique"",""kind"":""swell"",""duration"":15.0,""volume"":0.9,""seed"":51501,""startHot"":false,""bloom"":true,""releaseEnd"":true,""shepardOctaves"":2.0,""shepardGain"":0.2,""brass"":true,""brassGain"":0.22,""details"":""x""}]}";
            Assert.IsTrue(SoundBankCatalog.TryParseBankJson(sample, out var entries, out string err), err);
            Assert.AreEqual(1, entries.Count);
            var hbo = entries[0];
            Assert.AreEqual("Cinematic_OpeningSwell", hbo.id);
            Assert.AreEqual(15f, hbo.duration, 0.001f);
            Assert.IsFalse(hbo.startHot);
            Assert.IsTrue(hbo.bloom && hbo.releaseEnd && hbo.brass);
            Assert.IsFalse(SoundBankCatalog.TryParseBankJson("", out _, out _));
            Assert.IsFalse(SoundBankCatalog.TryParseBankJson("{pas du json", out _, out _));
        }

        [Test]
        public void SoundBank_OpeningSwellClipIsContinuous()
        {
            // Le HBO (ex-hardcodé, désormais recette) : 15 s non-nulles,
            // crescendo global (pas de trou médian), déterministe.
            const string sample = @"{""version"":1,""entries"":[{""id"":""Cinematic_OpeningSwell"",""kind"":""swell"",""duration"":15.0,""volume"":0.9,""seed"":51501,""startHot"":false,""bloom"":true,""releaseEnd"":true,""shepardOctaves"":2.0,""shepardGain"":0.2,""brass"":true,""brassGain"":0.22}]}";
            Assert.IsTrue(SoundBankCatalog.TryParseBankJson(sample, out var entries, out string err), err);
            var clip = ProceduralAudioFactory.GetBankClip(entries[0]);
            Assert.IsNotNull(clip);
            Assert.AreEqual(15 * ProceduralAudioFactory.SampleRate, clip.samples);
            float[] s = new float[clip.samples];
            Assert.IsTrue(clip.GetData(s, 0));
            double first = 0, second = 0;
            for (int i = 0; i < s.Length / 2; i++) first += s[i] * s[i];
            for (int i = s.Length / 2; i < s.Length; i++) second += s[i] * s[i];
            Assert.Greater(second, first * 1.5, "OpeningSwell banque n'amplifie pas");
            // Déterminisme : régénération identique (cache vidé).
            ProceduralAudioFactory.ClearCache();
            var clip2 = ProceduralAudioFactory.GetBankClip(entries[0]);
            float[] s2 = new float[clip2.samples];
            Assert.IsTrue(clip2.GetData(s2, 0));
            for (int i = 0; i < s.Length; i += 4999)
                Assert.AreEqual(s[i], s2[i], 1e-6f, $"non déterministe @ {i}");
        }

        [Test]
        public void SoundBank_RefKindAliasesProcedural()
        {
            const string sample = @"{""version"":1,""entries"":[{""id"":""sting"",""kind"":""ref"",""refSoundId"":""Victory_Stinger""}]}";
            Assert.IsTrue(SoundBankCatalog.TryParseBankJson(sample, out var entries, out string err), err);
            var clip = ProceduralAudioFactory.GetBankClip(entries[0]);
            Assert.IsNotNull(clip);
            Assert.Greater(clip.samples, 0);
            Assert.IsNull(ProceduralAudioFactory.GetBankClip(null));
        }

        [Test]
        public void SceneSoundData_DefaultsAndLookupWork()
        {
            var data = new Killtime.Story.Data.StorySceneData();
            data.Sounds.Add(new Killtime.Story.Data.SceneSoundData
            {
                SoundId = "snd_intro",
                SourceCardId = "line_01",
                SoundCueId = "Cinematic_OpeningSwell",
                TriggerPoint = Killtime.Story.Data.SoundTriggerPoint.AtLinkedStart
            });
            data.Sounds.Add(new Killtime.Story.Data.SceneSoundData
            {
                SoundId = "snd_outro",
                SourceCardId = "line_01",
                SoundCueId = "Victory_Stinger",
                TriggerPoint = Killtime.Story.Data.SoundTriggerPoint.AtLinkedEnd
            });

            Killtime.Story.Data.StorySceneData.EnsureDeepDefaults(data);
            Assert.AreEqual(2, data.Sounds.Count);
            Assert.AreEqual("snd_intro", data.FindSound("snd_intro").SoundId);

            var lineSounds = data.FindSoundsFrom("line_01");
            Assert.AreEqual(2, lineSounds.Count);
            Assert.AreEqual(Killtime.Story.Data.SoundTriggerPoint.AtLinkedStart, lineSounds[0].TriggerPoint);
            Assert.AreEqual(Killtime.Story.Data.SoundTriggerPoint.AtLinkedEnd, lineSounds[1].TriggerPoint);
        }

        [Test]
        public void MusicIntensityVariants_ExistAndAreLoopable()
        {
            foreach (MusicMood mood in System.Enum.GetValues(typeof(MusicMood)))
            {
                if (mood == MusicMood.None) continue;
                for (int level = 0; level <= 2; level++)
                {
                    var clip = ProceduralAudioFactory.GetMusicLoop(mood, level);
                    Assert.IsNotNull(clip, $"Pas de variante L{level} pour {mood}");
                    Assert.Greater(clip.samples, ProceduralAudioFactory.SampleRate * 4);
                    Assert.AreEqual(ProceduralAudioFactory.SampleRate, clip.frequency);
                    Assert.AreEqual(1, clip.channels, "Mono attendu (WebGL léger)");
                }
            }
        }

        [Test]
        public void MusicIntensityVariants_DifferByDensity()
        {
            // Calme vs intense : le contenu doit différer (couches ajoutées), pas le même clip.
            var calm = ProceduralAudioFactory.GetMusicLoop(MusicMood.Combat, 0);
            var intense = ProceduralAudioFactory.GetMusicLoop(MusicMood.Combat, 2);
            Assert.AreNotSame(calm, intense, "Les variantes d'intensité doivent être des clips distincts");
            float[] sc = new float[calm.samples], si = new float[intense.samples];
            calm.GetData(sc, 0);
            intense.GetData(si, 0);
            double diff = 0;
            int stride = calm.samples / 2048;
            for (int i = 0; i < calm.samples; i += stride)
                diff += System.Math.Abs(sc[i] - si[i]);
            diff /= (calm.samples / stride);
            Assert.Greater(diff, 0.01, "Calm et Intense sonnent pareil : les couches ne s'empilent pas");
        }

        [Test]
        public void CombatBoss_GeneratesMasterpieceLoopAndStinger()
        {
            var loop = ProceduralAudioFactory.GetMusicLoop(MusicMood.CombatBoss, 2);
            Assert.IsNotNull(loop, "Boucle CombatBoss manquante");
            Assert.AreEqual(ProceduralAudioFactory.SampleRate * 16, loop.samples, "Durée stricte de 16.0s non respectée");
            Assert.AreEqual(1, loop.channels, "Format mono requis pour WebGL");

            var stinger = ProceduralAudioFactory.GetStinger(MusicMood.CombatBoss);
            Assert.IsNotNull(stinger, "Stinger CombatBoss manquant");
            Assert.Greater(stinger.samples, 0);
            Assert.Less(stinger.samples, (int)(ProceduralAudioFactory.SampleRate * 2.5f));
        }

        [Test]
        public void CombatPlaylist_HasMultipleDistinctTracks()
        {
            int trackCount = ProceduralAudioFactory.GetTrackCount(MusicMood.Combat);
            Assert.GreaterOrEqual(trackCount, 3, "La playlist de combat standard doit contenir au moins 3 thèmes.");

            var track0 = ProceduralAudioFactory.GetMusicLoop(MusicMood.Combat, 0, 1);
            var track1 = ProceduralAudioFactory.GetMusicLoop(MusicMood.Combat, 1, 1);
            var track2 = ProceduralAudioFactory.GetMusicLoop(MusicMood.Combat, 2, 1);

            Assert.IsNotNull(track0, "Piste 1 de combat introuvable");
            Assert.IsNotNull(track1, "Piste 2 de combat introuvable");
            Assert.IsNotNull(track2, "Piste 3 de combat introuvable");

            Assert.AreNotSame(track0, track1, "Les pistes 0 et 1 doivent être des instances distinctes.");
            Assert.AreNotSame(track1, track2, "Les pistes 1 et 2 doivent être des instances distinctes.");
            Assert.AreNotSame(track0, track2, "Les pistes 0 et 2 doivent être des instances distinctes.");

            Assert.AreEqual(ProceduralAudioFactory.SampleRate * 16, track0.samples);
            Assert.AreEqual(ProceduralAudioFactory.SampleRate * 16, track1.samples);
            Assert.AreEqual(ProceduralAudioFactory.SampleRate * 16, track2.samples);
        }

        [Test]
        public void CombatPlaylist_AllTracksAreAudible()
        {
            // Non-régression "piste 1 ne joue pas" : chaque piste doit contenir
            // du signal (pas de boucle silencieuse / que des zéros).
            for (int t = 0; t < 3; t++)
            {
                for (int level = 0; level <= 2; level++)
                {
                    var clip = ProceduralAudioFactory.GetMusicLoop(MusicMood.Combat, t, level);
                    Assert.IsNotNull(clip, $"Piste {t + 1} L{level} introuvable");
                    float[] s = new float[clip.samples];
                    clip.GetData(s, 0);
                    float peak = 0f;
                    double sumSq = 0;
                    int n = 0;
                    for (int i = 0; i < s.Length; i += 7)
                    {
                        float a = System.Math.Abs(s[i]);
                        if (a > peak) peak = a;
                        sumSq += s[i] * s[i];
                        n++;
                    }
                    double rms = System.Math.Sqrt(sumSq / System.Math.Max(1, n));
                    Assert.Greater(peak, 0.05f, $"Piste {t + 1} L{level} silencieuse (peak={peak})");
                    Assert.Greater(rms, 0.01, $"Piste {t + 1} L{level} quasi-silencieuse (rms={rms})");
                }
            }
        }

        [Test]
        public void Stingers_ExistAndAreShort()
        {
            foreach (MusicMood mood in System.Enum.GetValues(typeof(MusicMood)))
            {
                if (mood == MusicMood.None) continue;
                var clip = ProceduralAudioFactory.GetStinger(mood);
                Assert.IsNotNull(clip, $"Pas de stinger pour {mood}");
                Assert.Greater(clip.samples, 0);
                // Stinger court (< 2.5s), pas une boucle.
                Assert.Less(clip.samples, (int)(ProceduralAudioFactory.SampleRate * 2.5f));
            }
        }

        [Test]
        public void AmbienceLoop_IsLongAndQuiet()
        {
            var clip = ProceduralAudioFactory.GetAmbienceLoop();
            Assert.IsNotNull(clip, "Pas de nappe d'ambiance");
            Assert.Greater(clip.samples, ProceduralAudioFactory.SampleRate * 4, "Ambiance trop courte (clic de boucle)");
            float[] s = new float[clip.samples];
            clip.GetData(s, 0);
            float peak = 0f;
            foreach (var v in s) peak = System.Math.Max(peak, System.Math.Abs(v));
            Assert.Greater(peak, 0.05f, "Ambience silencieuse");
            Assert.LessOrEqual(peak, 0.6f, "Ambience trop forte pour un lit sonore");
        }

        [Test]
        public void AdaptiveDirector_CriticalHpSwitchesToTension()
        {
            var (mood, intensity) = AdaptiveMusicDirector.ComputeTarget(
                0.1f, 2, 3, 5, MusicMood.Combat, 0.5f);
            Assert.AreEqual(MusicMood.Tension, mood, "PV critiques => Tension");
            Assert.GreaterOrEqual(intensity, 0.7f, "PV critiques => intensité haute");
        }

        [Test]
        public void AdaptiveDirector_HysteresisKeepsTension()
        {
            // À 0.25 on vient de Tension : on y reste (sortie à 0.32).
            var (stay, _) = AdaptiveMusicDirector.ComputeTarget(
                0.25f, 2, 2, 3, MusicMood.Tension, 0.7f);
            Assert.AreEqual(MusicMood.Tension, stay);
            // Même PV en venant de Combat : on n'y est jamais entré, on reste en Combat... sauf sous 0.22.
            var (enter, _) = AdaptiveMusicDirector.ComputeTarget(
                0.25f, 2, 2, 3, MusicMood.Combat, 0.5f);
            Assert.AreEqual(MusicMood.Combat, enter);
        }

        [Test]
        public void AdaptiveDirector_LastStandBoostsIntensity()
        {
            float normal = AdaptiveMusicDirector.ComputeIntensity(0.6f, 3, 2, 2);
            float lastStand = AdaptiveMusicDirector.ComputeIntensity(0.6f, 1, 3, 2);
            Assert.Greater(lastStand, normal, "Dernier carré => intensité supérieure");
        }

        [Test]
        public void DefaultDefinitions_AreSane()
        {
            foreach (SoundId id in System.Enum.GetValues(typeof(SoundId)))
            {
                if (id == SoundId.None) continue;
                var def = KilltimeAudioManager.CreateDefaultDefinition(id);
                Assert.IsNotNull(def);
                Assert.Greater(def.Volume, 0f);
                Assert.LessOrEqual(def.PitchMin, def.PitchMax);
                Assert.GreaterOrEqual((int)def.Category, 0);
            }
        }

        [Test]
        public void SoundBank_Lookup_Works()
        {
            var bank = ScriptableObject.CreateInstance<SoundBank>();
            bank.FillWithDefaults();
            Assert.Greater(bank.Sounds.Count, 30);
            var def = bank.Get(SoundId.Attack_Impact_Crit);
            Assert.IsNotNull(def);
            Assert.AreEqual(SoundId.Attack_Impact_Crit, def.Id);
            Object.DestroyImmediate(bank);
        }

        [Test]
        public void Chameleon_ChoirVoicePropertiesAndStyles_AreValid()
        {
            var go = new GameObject("[Test] Chameleon");
            var chameleon = go.AddComponent<Killtime.Audio.Experimental.SystemAudioChameleon>();
            Assert.IsTrue(chameleon.EmulateChoir);
            chameleon.ChoirStyle = Killtime.Audio.Experimental.ChameleonChoirStyle.ArcanotechHymn;
            Assert.AreEqual(Killtime.Audio.Experimental.ChameleonChoirStyle.ArcanotechHymn, chameleon.ChoirStyle);
            chameleon.ChoirMasterVolume = 1.5f;
            Assert.AreEqual(1.5f, chameleon.ChoirMasterVolume);
            chameleon.ChoirDetune = 2.4f;
            Assert.AreEqual(2.4f, chameleon.ChoirDetune);
            chameleon.ChoirShimmer = 0.65f;
            Assert.AreEqual(0.65f, chameleon.ChoirShimmer);
            chameleon.TestTriggerChoir();
            Assert.GreaterOrEqual(chameleon.HarmonicPurity, 0f);
            Assert.LessOrEqual(chameleon.HarmonicPurity, 1f);

            chameleon.BassStyle = Killtime.Audio.Experimental.ChameleonBassStyle.NeuroMod;
            Assert.AreEqual(Killtime.Audio.Experimental.ChameleonBassStyle.NeuroMod, chameleon.BassStyle);
            chameleon.BassSubCleanLayer = 0.80f;
            Assert.AreEqual(0.80f, chameleon.BassSubCleanLayer);
            chameleon.BassPunch = 0.65f;
            Assert.AreEqual(0.65f, chameleon.BassPunch);
            chameleon.TestTriggerBass();
            Assert.GreaterOrEqual(chameleon.DetectedBassFrequencyHz, 0f);
            Assert.GreaterOrEqual(chameleon.BassPitchConfidence, 0f);

            Assert.IsTrue(chameleon.EmulateVocal);
            chameleon.VocalStyle = Killtime.Audio.Experimental.ChameleonVocalStyle.CyberSoprano;
            Assert.AreEqual(Killtime.Audio.Experimental.ChameleonVocalStyle.CyberSoprano, chameleon.VocalStyle);
            chameleon.VocalMasterVolume = 1.2f;
            Assert.AreEqual(1.2f, chameleon.VocalMasterVolume);
            chameleon.VocalVibratoDepth = 0.70f;
            Assert.AreEqual(0.70f, chameleon.VocalVibratoDepth);
            chameleon.VocalBreathiness = 0.40f;
            Assert.AreEqual(0.40f, chameleon.VocalBreathiness);
            chameleon.TestTriggerVocal();
            Assert.GreaterOrEqual(chameleon.CurrentVocalPitchHz, 0f);

            Object.DestroyImmediate(go);
        }
    }
}
#endif

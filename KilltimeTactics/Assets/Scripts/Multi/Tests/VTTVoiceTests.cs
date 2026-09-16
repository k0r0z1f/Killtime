#if UNITY_EDITOR || UNITY_INCLUDE_TESTS
using System;
using NUnit.Framework;
using UnityEngine;
using Killtime.Multi.Voice;

namespace Killtime.Multi.Tests
{
    [TestFixture]
    public class VTTVoiceTests
    {
        [Test]
        public void ImaAdpcm16k_EncodesAndDecodesCleanly()
        {
            var codec = new ImaAdpcmCodec(VoiceCodecType.ImaAdpcm16k);
            Assert.AreEqual(16000, codec.SampleRate);
            Assert.AreEqual(64, codec.BitrateKbps);

            // Synthèse d'une onde sinusoïdale 440 Hz (voix / son de test)
            int sampleCount = 960; // 60ms @ 16kHz
            float[] original = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                original[i] = 0.65f * Mathf.Sin(2f * Mathf.PI * 440f * i / 16000f);
            }

            byte[] encoded = codec.Encode(original, sampleCount);
            Assert.IsNotNull(encoded);
            // 4 octets d'en-tête + (960 / 2) octets = 484 octets
            Assert.AreEqual(4 + 480, encoded.Length);

            float[] decoded = codec.Decode(encoded);
            Assert.IsNotNull(decoded);
            Assert.AreEqual(sampleCount, decoded.Length);

            // Vérification de l'erreur quadratique moyenne (ADPCM 4-bit conserve la fidélité avec faible erreur)
            float sumSquaredError = 0f;
            float sumSignal = 0f;
            for (int i = 0; i < sampleCount; i++)
            {
                float diff = original[i] - decoded[i];
                sumSquaredError += diff * diff;
                sumSignal += original[i] * original[i];
            }

            float snr = 10f * Mathf.Log10(sumSignal / Mathf.Max(0.0001f, sumSquaredError));
            Assert.Greater(snr, 15f, "Le SNR de l'IMA-ADPCM doit être supérieur à 15 dB");
        }

        [Test]
        public void G711MuLaw_CompandsAccurately()
        {
            var codec = new G711MuLawCodec(VoiceCodecType.G711MuLaw16k);
            Assert.AreEqual(16000, codec.SampleRate);
            Assert.AreEqual(128, codec.BitrateKbps);

            int sampleCount = 480;
            float[] original = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                original[i] = 0.5f * Mathf.Sin(2f * Mathf.PI * 800f * i / 16000f);
            }

            byte[] encoded = codec.Encode(original, sampleCount);
            Assert.AreEqual(sampleCount, encoded.Length); // 8-bit par échantillon

            float[] decoded = codec.Decode(encoded);
            Assert.AreEqual(sampleCount, decoded.Length);

            // G.711 µ-law a un SNR typique de ~30 dB
            float sumSquaredError = 0f;
            float sumSignal = 0f;
            for (int i = 0; i < sampleCount; i++)
            {
                float diff = original[i] - decoded[i];
                sumSquaredError += diff * diff;
                sumSignal += original[i] * original[i];
            }

            float snr = 10f * Mathf.Log10(sumSignal / Mathf.Max(0.0001f, sumSquaredError));
            Assert.Greater(snr, 22f, "Le SNR de G.711 µ-law doit être supérieur à 22 dB");
        }

        [Test]
        public void RawPcm16_IsLossless()
        {
            var codec = new RawPcm16Codec();
            Assert.AreEqual(16000, codec.SampleRate);

            float[] original = new float[] { -1.0f, -0.5f, 0f, 0.5f, 1.0f };
            byte[] encoded = codec.Encode(original, original.Length);
            Assert.AreEqual(original.Length * 2, encoded.Length);

            float[] decoded = codec.Decode(encoded);
            Assert.AreEqual(original.Length, decoded.Length);

            for (int i = 0; i < original.Length; i++)
            {
                Assert.AreEqual(original[i], decoded[i], 0.001f);
            }
        }

        [Test]
        public void VoiceDsp_NoiseGateClosesOnSilenceAndOpensOnSpeech()
        {
            var dsp = new VoiceDspProcessor(16000);
            dsp.GateThreshold = 0.03f; // Seuil à 3%
            dsp.GateHoldTimeSec = 0.05f;

            int count = 480;
            float[] silence = new float[count];
            float[] output = new float[count];

            // 1. Silence -> Gate doit se fermer
            for (int i = 0; i < 5; i++)
            {
                dsp.Process(silence, output, count, 0.03f);
            }
            Assert.IsFalse(dsp.IsGateOpen, "Le Noise Gate doit être fermé lors du silence");
            Assert.Less(dsp.GateEnvelope, 0.1f);

            // 2. Parole forte (amplitude 0.5) -> Gate doit s'ouvrir
            float[] speech = new float[count];
            for (int i = 0; i < count; i++) speech[i] = 0.5f * Mathf.Sin(i * 0.1f);

            dsp.Process(speech, output, count, 0.03f);
            Assert.IsTrue(dsp.IsGateOpen, "Le Noise Gate doit s'ouvrir lorsque la parole dépasse le seuil");
            Assert.Greater(dsp.CurrentRms, 0.03f);
        }

        [Test]
        public void VoiceDsp_HighPassFilterAttenuatesSubRumble()
        {
            var dsp = new VoiceDspProcessor(16000);
            dsp.HighPassFilterEnabled = true;
            dsp.NoiseGateEnabled = false; // Désactiver pour tester la réponse pure du filtre
            dsp.NoiseReductionEnabled = false;

            int count = 1600;
            float[] subRumble = new float[count];
            float[] output = new float[count];

            // Signal parasite très basse fréquence (20 Hz)
            for (int i = 0; i < count; i++)
            {
                subRumble[i] = 0.8f * Mathf.Sin(2f * Mathf.PI * 20f * i / 16000f);
            }

            dsp.Process(subRumble, output, count, 0.1f);

            // Mesure de l'amplitude résiduelle en fin de bloc (stabilisation du filtre)
            float finalRms = 0f;
            for (int i = count - 400; i < count; i++)
            {
                finalRms += output[i] * output[i];
            }
            finalRms = Mathf.Sqrt(finalRms / 400f);

            // Le filtre 80 Hz doit atténuer fortement le 20 Hz (au moins 10 dB d'atténuation)
            Assert.Less(finalRms, 0.25f, "Le filtre passe-haut 80 Hz doit atténuer le ronflement à 20 Hz");
        }

        [Test]
        public void VoiceDsp_LimiterPreventsClipping()
        {
            var dsp = new VoiceDspProcessor(16000);
            dsp.LimiterEnabled = true;
            dsp.NoiseGateEnabled = false;

            int count = 100;
            float[] shouting = new float[count];
            float[] output = new float[count];

            for (int i = 0; i < count; i++) shouting[i] = 3.5f; // Signal saturé

            dsp.Process(shouting, output, count, 0.01f);

            for (int i = 0; i < count; i++)
            {
                Assert.LessOrEqual(output[i], 1.0f, "Le limiteur ne doit jamais dépasser +1.0f");
                Assert.GreaterOrEqual(output[i], -1.0f, "Le limiteur ne doit jamais descendre sous -1.0f");
            }
        }

        [Test]
        public void VTTProtocol_VoiceOpSerialization()
        {
            string base64 = "ABCD1234==";
            string json = VTTProtocol.BuildVoiceOp(0, 16000, 42, base64);

            StringAssert.Contains("\"type\":\"op\"", json);
            StringAssert.Contains("\"op\":\"voice\"", json);
            StringAssert.Contains("\"codec\":0", json);
            StringAssert.Contains("\"sampleRate\":16000", json);
            StringAssert.Contains("\"seq\":42", json);
            StringAssert.Contains("\"data\":\"ABCD1234==\"", json);

            // Désérialisation du payload
            string payloadJson = VTTProtocol.BuildVoicePayloadJson(1, 8000, 99, base64);
            var payload = JsonUtility.FromJson<VTTVoicePayload>(payloadJson);
            Assert.IsNotNull(payload);
            Assert.AreEqual(1, payload.codec);
            Assert.AreEqual(8000, payload.sampleRate);
            Assert.AreEqual(99, payload.seq);
            Assert.AreEqual(base64, payload.data);
        }

        [Test]
        public void VoiceChanger_ProcessesCleanlyWithoutNaN()
        {
            var changer = new VoiceChangerProcessor(16000);
            changer.Enabled = true;

            int count = 960;
            float[] input = new float[count];
            for (int i = 0; i < count; i++) input[i] = 0.6f * Mathf.Sin(2f * Mathf.PI * 300f * i / 16000f);

            var presets = (VoiceChangerPreset[])Enum.GetValues(typeof(VoiceChangerPreset));
            for (int p = 0; p < presets.Length; p++)
            {
                changer.ApplyPreset(presets[p]);
                float[] buffer = (float[])input.Clone();
                changer.Process(buffer, count);

                for (int i = 0; i < count; i++)
                {
                    Assert.IsFalse(float.IsNaN(buffer[i]), $"NaN détecté avec le preset {presets[p]} à l'index {i}");
                    Assert.IsFalse(float.IsInfinity(buffer[i]), $"Infini détecté avec le preset {presets[p]} à l'index {i}");
                    Assert.LessOrEqual(Mathf.Abs(buffer[i]), 1.5f, $"Dépassement d'amplitude sur le preset {presets[p]}");
                }
            }
        }

        [Test]
        public void VoiceChanger_PresetsApplyExpectedParameters()
        {
            var changer = new VoiceChangerProcessor(16000);

            changer.ApplyPreset(VoiceChangerPreset.TacticalHeavy);
            Assert.IsTrue(changer.Enabled);
            Assert.AreEqual(-3.5f, changer.PitchSemitones, 0.01f);
            Assert.Less(changer.FormantShift, 0f);

            changer.ApplyPreset(VoiceChangerPreset.GhostInfiltrator);
            Assert.IsTrue(changer.Enabled);
            Assert.AreEqual(3.5f, changer.PitchSemitones, 0.01f);
            Assert.Greater(changer.FormantShift, 0f);

            changer.ApplyPreset(VoiceChangerPreset.CyborgDroid);
            Assert.IsTrue(changer.Enabled);
            Assert.Greater(changer.RoboticModulation, 0.3f);

            changer.ApplyPreset(VoiceChangerPreset.Off);
            Assert.IsFalse(changer.Enabled);
            Assert.AreEqual(0f, changer.PitchSemitones);
        }

        [Test]
        public void VoiceChanger_FemalePresetsApplyExpectedParameters()
        {
            var changer = new VoiceChangerProcessor(16000);

            changer.ApplyPreset(VoiceChangerPreset.ValkyrieOperator);
            Assert.IsTrue(changer.Enabled);
            Assert.Greater(changer.PitchSemitones, 3.0f);
            Assert.Greater(changer.FormantShift, 0.35f);

            changer.ApplyPreset(VoiceChangerPreset.InfiltratorFemale);
            Assert.IsTrue(changer.Enabled);
            Assert.Greater(changer.PitchSemitones, 4.5f);
            Assert.Greater(changer.FormantShift, 0.5f);

            changer.ApplyPreset(VoiceChangerPreset.NytharitePriestess);
            Assert.IsTrue(changer.Enabled);
            Assert.Greater(changer.PitchSemitones, 2.0f);
            Assert.Greater(changer.HarmonicDrive, 0.15f);

            changer.ApplyPreset(VoiceChangerPreset.CyberAndroidFemale);
            Assert.IsTrue(changer.Enabled);
            Assert.Greater(changer.PitchSemitones, 3.5f);
            Assert.Greater(changer.RoboticModulation, 0.3f);
        }
    }
}
#endif

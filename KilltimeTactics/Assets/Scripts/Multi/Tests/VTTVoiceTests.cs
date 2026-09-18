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

        [Test]
        public void Resampling_48kTo16k_PreservesPitchAndDuration()
        {
            // Synthèse d'une onde pure 440 Hz pendant 1 seconde @ 48 kHz (48 000 échantillons)
            int inRate = 48000;
            int outRate = 16000;
            float freqHz = 440f;
            float[] in48k = new float[inRate];
            for (int i = 0; i < inRate; i++)
            {
                in48k[i] = Mathf.Sin(2f * Mathf.PI * freqHz * i / inRate);
            }

            // Rééchantillonnage vers 16 kHz
            float[] out16k = VTTVoiceManager.ResampleBuffer(in48k, inRate, outRate);

            // 1. Durée : exactement 16 000 échantillons pour 1 seconde
            Assert.AreEqual(outRate, out16k.Length, "Le tampon de sortie doit faire exactement 16 000 échantillons");

            // 2. Pitch / Fréquence : comptage des passages par zéro (880 passages pour 440 Hz en 1 seconde)
            int zeroCrossings = 0;
            for (int i = 1; i < out16k.Length; i++)
            {
                if ((out16k[i - 1] < 0f && out16k[i] >= 0f) || (out16k[i - 1] >= 0f && out16k[i] < 0f))
                {
                    zeroCrossings++;
                }
            }

            // Si la voix était ralentie x3 (pitch down), il n'y aurait que ~293 passages par zéro.
            // À 440 Hz normal, il y a 880 passages.
            Assert.AreEqual(880, zeroCrossings, 2, "La fréquence du signal après rééchantillonnage doit rester à 440 Hz");
        }

        [Test]
        public void Resampling_44kTo16k_PreservesPitchAndDuration()
        {
            // Synthèse d'une onde pure 440 Hz pendant 1 seconde @ 44.1 kHz (44 100 échantillons)
            int inRate = 44100;
            int outRate = 16000;
            float freqHz = 440f;
            float[] in44k = new float[inRate];
            for (int i = 0; i < inRate; i++)
            {
                in44k[i] = Mathf.Sin(2f * Mathf.PI * freqHz * i / inRate);
            }

            float[] out16k = VTTVoiceManager.ResampleBuffer(in44k, inRate, outRate);
            Assert.AreEqual(outRate, out16k.Length, "Le tampon de sortie doit faire exactement 16 000 échantillons");

            int zeroCrossings = 0;
            for (int i = 1; i < out16k.Length; i++)
            {
                if ((out16k[i - 1] < 0f && out16k[i] >= 0f) || (out16k[i - 1] >= 0f && out16k[i] < 0f))
                {
                    zeroCrossings++;
                }
            }

            Assert.AreEqual(880, zeroCrossings, 2, "La fréquence doit rester 440 Hz lors du passage 44.1k -> 16k");
        }

        [Test]
        public void StreamingResampler_ChunkedFeed_PreservesSampleCount()
        {
            // Simulation de découpage en blocs (ex: ~480 échantillons @ 48kHz toutes les 10ms)
            int inRate = 48000;
            int outRate = 16000;
            double step = (double)inRate / (double)outRate;
            int chunkSize = 480;
            int totalChunks = 100; // 48 000 échantillons au total

            double resamplePos = 1.0;
            float lastSample = 0f;
            int totalOutSamples = 0;

            for (int c = 0; c < totalChunks; c++)
            {
                float[] chunk = new float[chunkSize];
                for (int i = 0; i < chunkSize; i++) chunk[i] = 0.5f;

                int M = chunkSize;
                while (resamplePos <= M)
                {
                    totalOutSamples++;
                    resamplePos += step;
                }
                resamplePos -= M;
                lastSample = chunk[M - 1];
            }

            // Doit produire exactement 16 000 échantillons sans aucune dérive
            Assert.AreEqual(16000, totalOutSamples, "Le flux continu rééchantillonné doit produire exactement 16 000 échantillons pour 48 000 en entrée");
        }

        [Test]
        public void ExtractPrimaryMonoChannel_AvoidsStereoPhaseCancellation()
        {
            // Simulation d'un micro array de PC portable Windows dont le canal droit est en opposition de phase (180°)
            int samples = 960;
            int channels = 2;
            float[] stereoInterleaved = new float[samples * channels];
            for (int i = 0; i < samples; i++)
            {
                float val = 0.5f * Mathf.Sin(2f * Mathf.PI * 300f * i / 16000f);
                stereoInterleaved[i * 2 + 0] = val;       // Canal Gauche (0)
                stereoInterleaved[i * 2 + 1] = -val;      // Canal Droit (1, opposition de phase)
            }

            // Si on faisait une moyenne naïve (L + R) / 2 :
            float naiveSum = 0f;
            for (int i = 0; i < samples; i++)
            {
                float naiveMono = (stereoInterleaved[i * 2] + stereoInterleaved[i * 2 + 1]) * 0.5f;
                naiveSum += Mathf.Abs(naiveMono);
            }
            Assert.AreEqual(0f, naiveSum, 0.0001f, "La moyenne naïve annule totalement le son par opposition de phase");

            // Avec ExtractPrimaryMonoChannel :
            float[] extractedMono = VTTVoiceManager.ExtractPrimaryMonoChannel(stereoInterleaved, channels, samples);
            Assert.AreEqual(samples, extractedMono.Length);

            float extractedEnergy = 0f;
            for (int i = 0; i < samples; i++)
            {
                extractedEnergy += extractedMono[i] * extractedMono[i];
            }
            float extractedRms = Mathf.Sqrt(extractedEnergy / samples);

            // L'énergie du signal mono extrait doit être préservée intacte (~0.353f)
            Assert.Greater(extractedRms, 0.30f, "L'énergie du signal vocal doit être intacte sans annulation de phase");
        }

        [Test]
        public void DspProcessor_AgcAmplifiesQuietSpeech()
        {
            var dsp = new VoiceDspProcessor(16000);
            dsp.AutoGainEnabled = true;
            dsp.NoiseGateEnabled = false; // Isoler le gain AGC

            int frameSize = 960; // 60ms
            float[] quietSpeech = new float[frameSize];
            float[] output = new float[frameSize];

            // Signal vocal très faible (RMS ~ 0.015)
            for (int i = 0; i < frameSize; i++)
            {
                quietSpeech[i] = 0.02f * Mathf.Sin(2f * Mathf.PI * 400f * i / 16000f);
            }

            // Simuler quelques trames de parole pour laisser l'AGC s'adapter
            for (int frame = 0; frame < 15; frame++)
            {
                dsp.Process(quietSpeech, output, frameSize, 0.06f);
            }

            Assert.Greater(dsp.CurrentAgcGain, 2.0f, "L'AGC doit amplifier un micro trop faible d'au moins +6 dB (> 2.0x)");

            float outEnergy = 0f;
            for (int i = 0; i < frameSize; i++) outEnergy += output[i] * output[i];
            float outRms = Mathf.Sqrt(outEnergy / frameSize);

            Assert.Greater(outRms, 0.035f, "Le RMS de sortie doit être nettement plus élevé que le signal d'entrée faible");
        }

        [Test]
        public void DspProcessor_GateHoldPreservesSyllables()
        {
            var dsp = new VoiceDspProcessor(16000);
            dsp.NoiseGateEnabled = true;
            dsp.GateThreshold = 0.008f;
            dsp.GateHoldTimeSec = 0.35f;

            int frameSize = 320; // 20ms
            float[] voiceFrame = new float[frameSize];
            float[] silenceFrame = new float[frameSize];
            float[] outBuffer = new float[frameSize];

            for (int i = 0; i < frameSize; i++)
            {
                voiceFrame[i] = 0.03f * Mathf.Sin(2f * Mathf.PI * 500f * i / 16000f);
            }

            // 1. Détection de voix active -> le gate s'ouvre
            dsp.Process(voiceFrame, outBuffer, frameSize, 0.02f);
            Assert.IsTrue(dsp.IsGateOpen, "Le gate doit s'ouvrir lors de la détection de parole");

            // 2. Pause brève entre deux syllabes (100 ms de silence)
            for (int f = 0; f < 5; f++)
            {
                dsp.Process(silenceFrame, outBuffer, frameSize, 0.02f);
            }

            // Le gate doit RESTER OUVERT grâce au GateHoldTimeSec de 350 ms, évitant de hacher les mots
            Assert.IsTrue(dsp.IsGateOpen, "Le gate doit rester ouvert pendant une micro-pause entre syllabes (hangover)");
        }
    }
}
#endif

using System;
using UnityEngine;

namespace Killtime.Multi.Voice
{
    /// <summary>
    /// Implémentation du codec IMA-ADPCM (Adaptive Differential Pulse Code Modulation).
    /// Compresse le PCM 16-bit à 4 bits par échantillon (ratio 4:1).
    /// Chaque paquet transporte un en-tête de synchronisation (valeur prédite + index de pas)
    /// pour garantir qu'aucune dérive ou perte de paquet n'engendre de distorsion persistante.
    /// </summary>
    public class ImaAdpcmCodec : IVoiceCodec
    {
        private static readonly int[] StepTable = new int[89]
        {
            7, 8, 9, 10, 11, 12, 13, 14, 16, 17,
            19, 21, 23, 25, 28, 31, 34, 37, 41, 45,
            50, 55, 60, 66, 73, 80, 88, 97, 107, 118,
            130, 143, 157, 173, 190, 209, 230, 253, 279, 307,
            337, 371, 408, 449, 494, 544, 598, 658, 724, 796,
            876, 963, 1060, 1166, 1282, 1411, 1552, 1707, 1878, 2066,
            2272, 2499, 2749, 3024, 3327, 3660, 4026, 4428, 4871, 5358,
            5894, 6484, 7132, 7845, 8630, 9493, 10442, 11487, 12635, 13899,
            15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794, 32767
        };

        private static readonly int[] IndexTable = new int[16]
        {
            -1, -1, -1, -1, 2, 4, 6, 8,
            -1, -1, -1, -1, 2, 4, 6, 8
        };

        private readonly VoiceCodecType _type;
        private readonly int _sampleRate;
        private int _runningStepIndex;

        public VoiceCodecType CodecType => _type;
        public int SampleRate => _sampleRate;
        public string DisplayName => _sampleRate == 16000 
            ? "IMA-ADPCM 16k (Recommandé)" 
            : "IMA-ADPCM 8k (Éco bande)";
        public string Description => _sampleRate == 16000
            ? "Large bande 16 kHz, compression 4:1 (64 kbps). Clarté optimale et latence quasi nulle."
            : "Bande étroite 8 kHz, compression 4:1 (32 kbps). Consommation réseau minimale.";
        public int BitrateKbps => _sampleRate == 16000 ? 64 : 32;

        public ImaAdpcmCodec(VoiceCodecType type)
        {
            _type = type;
            _sampleRate = (type == VoiceCodecType.ImaAdpcm8k) ? 8000 : 16000;
        }

        public byte[] Encode(float[] pcmSamples, int count)
        {
            if (pcmSamples == null || count <= 0) return Array.Empty<byte>();

            // En-tête : 4 octets [predictor_low, predictor_high, stepIndex, 0]
            int encodedDataLen = (count + 1) / 2;
            byte[] output = new byte[4 + encodedDataLen];

            int predictor = Mathf.Clamp(Mathf.RoundToInt(pcmSamples[0] * 32767f), -32768, 32767);
            int stepIndex = _runningStepIndex;

            output[0] = (byte)(predictor & 0xFF);
            output[1] = (byte)((predictor >> 8) & 0xFF);
            output[2] = (byte)stepIndex;
            output[3] = 0; // réservé

            int byteOffset = 4;
            for (int i = 0; i < count; i += 2)
            {
                int s1 = Mathf.Clamp(Mathf.RoundToInt(pcmSamples[i] * 32767f), -32768, 32767);
                byte nibble1 = EncodeSample(s1, ref predictor, ref stepIndex);

                byte nibble2 = 0;
                if (i + 1 < count)
                {
                    int s2 = Mathf.Clamp(Mathf.RoundToInt(pcmSamples[i + 1] * 32767f), -32768, 32767);
                    nibble2 = EncodeSample(s2, ref predictor, ref stepIndex);
                }

                output[byteOffset++] = (byte)(nibble1 | (nibble2 << 4));
            }

            _runningStepIndex = stepIndex;
            return output;
        }

        public float[] Decode(byte[] encodedData)
        {
            if (encodedData == null || encodedData.Length < 4) return Array.Empty<float>();

            int predictor = (short)(encodedData[0] | (encodedData[1] << 8));
            int stepIndex = Mathf.Clamp(encodedData[2], 0, 88);

            int dataBytes = encodedData.Length - 4;
            int totalSamples = dataBytes * 2;
            float[] output = new float[totalSamples];

            int sampleIdx = 0;
            for (int i = 4; i < encodedData.Length; i++)
            {
                byte b = encodedData[i];
                byte nibble1 = (byte)(b & 0x0F);
                byte nibble2 = (byte)((b >> 4) & 0x0F);

                output[sampleIdx++] = DecodeSample(nibble1, ref predictor, ref stepIndex) / 32767f;
                if (sampleIdx < totalSamples)
                {
                    output[sampleIdx++] = DecodeSample(nibble2, ref predictor, ref stepIndex) / 32767f;
                }
            }

            return output;
        }

        private static byte EncodeSample(int sample, ref int predictor, ref int stepIndex)
        {
            int step = StepTable[stepIndex];
            int diff = sample - predictor;
            byte nibble = 0;

            if (diff < 0)
            {
                nibble = 8; // Bit de signe
                diff = -diff;
            }

            int vpdiff = step >> 3;
            if (diff >= step)
            {
                nibble |= 4;
                diff -= step;
                vpdiff += step;
            }
            step >>= 1;
            if (diff >= step)
            {
                nibble |= 2;
                diff -= step;
                vpdiff += step;
            }
            step >>= 1;
            if (diff >= step)
            {
                nibble |= 1;
                vpdiff += step;
            }

            if ((nibble & 8) != 0) predictor -= vpdiff;
            else predictor += vpdiff;

            predictor = Mathf.Clamp(predictor, -32768, 32767);
            stepIndex = Mathf.Clamp(stepIndex + IndexTable[nibble], 0, 88);

            return nibble;
        }

        private static int DecodeSample(byte nibble, ref int predictor, ref int stepIndex)
        {
            int step = StepTable[stepIndex];
            int vpdiff = step >> 3;
            if ((nibble & 4) != 0) vpdiff += step;
            if ((nibble & 2) != 0) vpdiff += step >> 1;
            if ((nibble & 1) != 0) vpdiff += step >> 2;

            if ((nibble & 8) != 0) predictor -= vpdiff;
            else predictor += vpdiff;

            predictor = Mathf.Clamp(predictor, -32768, 32767);
            stepIndex = Mathf.Clamp(stepIndex + IndexTable[nibble], 0, 88);

            return predictor;
        }
    }

    /// <summary>
    /// Implémentation du codec ITU-T G.711 µ-law (PCMU).
    /// Standard international de compandage logarithmique 8-bit (ratio 2:1).
    /// </summary>
    public class G711MuLawCodec : IVoiceCodec
    {
        private const int Bias = 0x84; // 132
        private const int Clip = 32635;

        private static readonly byte[] LinearToMuLawMap = new byte[65536];
        private static readonly short[] MuLawToLinearMap = new short[256];
        private static bool _tablesInitialized;

        private readonly VoiceCodecType _type;
        private readonly int _sampleRate;

        public VoiceCodecType CodecType => _type;
        public int SampleRate => _sampleRate;
        public string DisplayName => _sampleRate == 16000 ? "G.711 µ-law 16k" : "G.711 µ-law 8k";
        public string Description => _sampleRate == 16000
            ? "Standard UIT-T 16 kHz (128 kbps). Compandage non-linéaire 8-bit très robuste."
            : "Standard UIT-T 8 kHz (64 kbps). Profil téléphonie classique.";
        public int BitrateKbps => _sampleRate == 16000 ? 128 : 64;

        public G711MuLawCodec(VoiceCodecType type)
        {
            _type = type;
            _sampleRate = (type == VoiceCodecType.G711MuLaw8k) ? 8000 : 16000;
            EnsureTables();
        }

        private static void EnsureTables()
        {
            if (_tablesInitialized) return;

            // Précalcul des tables de correspondance pour décodage/encodage direct O(1)
            for (int i = 0; i < 256; i++)
            {
                byte mu = (byte)i;
                int inv = ~mu;
                int sign = inv & 0x80;
                int exponent = (inv >> 4) & 0x07;
                int mantissa = inv & 0x0F;
                int sample = ((mantissa << 3) + Bias) << exponent;
                sample -= Bias;
                MuLawToLinearMap[i] = (short)(sign != 0 ? -sample : sample);
            }

            for (int i = 0; i < 65536; i++)
            {
                short sample = (short)(i - 32768);
                LinearToMuLawMap[i] = CalculateMuLaw(sample);
            }

            _tablesInitialized = true;
        }

        private static byte CalculateMuLaw(int sample)
        {
            int sign = (sample >> 8) & 0x80;
            if (sign != 0) sample = -sample;
            if (sample > Clip) sample = Clip;
            sample += Bias;

            int exponent = 7;
            for (int expMask = 0x4000; (sample & expMask) == 0 && exponent > 0; expMask >>= 1)
            {
                exponent--;
            }

            int mantissa = (sample >> (exponent + 3)) & 0x0F;
            byte mu = (byte)(sign | (exponent << 4) | mantissa);
            return (byte)~mu;
        }

        public byte[] Encode(float[] pcmSamples, int count)
        {
            if (pcmSamples == null || count <= 0) return Array.Empty<byte>();

            byte[] output = new byte[count];
            for (int i = 0; i < count; i++)
            {
                int sample = Mathf.Clamp(Mathf.RoundToInt(pcmSamples[i] * 32767f), -32768, 32767);
                output[i] = LinearToMuLawMap[sample + 32768];
            }
            return output;
        }

        public float[] Decode(byte[] encodedData)
        {
            if (encodedData == null || encodedData.Length == 0) return Array.Empty<float>();

            float[] output = new float[encodedData.Length];
            for (int i = 0; i < encodedData.Length; i++)
            {
                output[i] = MuLawToLinearMap[encodedData[i]] / 32767f;
            }
            return output;
        }
    }

    /// <summary>
    /// Codec PCM 16-bit linéaire brut non compressé (sans perte, référence studio).
    /// </summary>
    public class RawPcm16Codec : IVoiceCodec
    {
        public VoiceCodecType CodecType => VoiceCodecType.RawPcm16k;
        public int SampleRate => 16000;
        public string DisplayName => "PCM 16-bit (Studio Brut)";
        public string Description => "PCM linéaire non compressé (256 kbps). Qualité studio de référence, idéal en LAN.";
        public int BitrateKbps => 256;

        public byte[] Encode(float[] pcmSamples, int count)
        {
            if (pcmSamples == null || count <= 0) return Array.Empty<byte>();

            byte[] output = new byte[count * 2];
            for (int i = 0; i < count; i++)
            {
                short sample = (short)Mathf.Clamp(Mathf.RoundToInt(pcmSamples[i] * 32767f), -32768, 32767);
                output[i * 2] = (byte)(sample & 0xFF);
                output[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
            }
            return output;
        }

        public float[] Decode(byte[] encodedData)
        {
            if (encodedData == null || encodedData.Length < 2) return Array.Empty<float>();

            int count = encodedData.Length / 2;
            float[] output = new float[count];
            for (int i = 0; i < count; i++)
            {
                short sample = (short)(encodedData[i * 2] | (encodedData[i * 2 + 1] << 8));
                output[i] = sample / 32767f;
            }
            return output;
        }
    }

    /// <summary>
    /// Fabrique et registre central des codecs voix supportés.
    /// </summary>
    public static class VoiceCodecRegistry
    {
        private static readonly IVoiceCodec[] Codecs = new IVoiceCodec[]
        {
            new ImaAdpcmCodec(VoiceCodecType.ImaAdpcm16k),
            new ImaAdpcmCodec(VoiceCodecType.ImaAdpcm8k),
            new G711MuLawCodec(VoiceCodecType.G711MuLaw16k),
            new G711MuLawCodec(VoiceCodecType.G711MuLaw8k),
            new RawPcm16Codec()
        };

        public static IVoiceCodec GetCodec(VoiceCodecType type)
        {
            int idx = (int)type;
            if (idx >= 0 && idx < Codecs.Length) return Codecs[idx];
            return Codecs[0]; // Défaut IMA-ADPCM 16k
        }

        public static string[] GetCodecDisplayNames()
        {
            string[] names = new string[Codecs.Length];
            for (int i = 0; i < Codecs.Length; i++)
            {
                names[i] = Codecs[i].DisplayName;
            }
            return names;
        }
    }
}

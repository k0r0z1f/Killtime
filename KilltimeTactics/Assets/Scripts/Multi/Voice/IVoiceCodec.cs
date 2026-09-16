using System;

namespace Killtime.Multi.Voice
{
    /// <summary>
    /// Type de codec voix sélectionnable pour la compression et transmission VTT.
    /// </summary>
    public enum VoiceCodecType
    {
        /// <summary>IMA-ADPCM 16 kHz Wideband (64 kbps) — Par défaut et recommandé pour la clarté vocale et faible débit.</summary>
        ImaAdpcm16k = 0,
        /// <summary>IMA-ADPCM 8 kHz Narrowband (32 kbps) — Économie maximale de bande passante.</summary>
        ImaAdpcm8k = 1,
        /// <summary>ITU-T G.711 µ-law 16 kHz (128 kbps) — Compandage logarithmique 8-bit, son télécom robuste.</summary>
        G711MuLaw16k = 2,
        /// <summary>ITU-T G.711 µ-law 8 kHz (64 kbps) — Téléphonie classique standard.</summary>
        G711MuLaw8k = 3,
        /// <summary>PCM 16-bit linéaire non compressé (256 kbps) — Qualité studio de référence / test LAN.</summary>
        RawPcm16k = 4
    }

    /// <summary>
    /// Interface commune pour les codecs voix VTT (100% C# managé, multiplateforme).
    /// </summary>
    public interface IVoiceCodec
    {
        VoiceCodecType CodecType { get; }
        string DisplayName { get; }
        string Description { get; }
        int SampleRate { get; }
        int BitrateKbps { get; }

        /// <summary>
        /// Encode un tableau d'échantillons PCM normalisés [-1.0f, +1.0f] vers un flux d'octets compressés.
        /// </summary>
        byte[] Encode(float[] pcmSamples, int count);

        /// <summary>
        /// Décode un flux d'octets compressés vers un tableau d'échantillons PCM normalisés [-1.0f, +1.0f].
        /// </summary>
        float[] Decode(byte[] encodedData);
    }
}

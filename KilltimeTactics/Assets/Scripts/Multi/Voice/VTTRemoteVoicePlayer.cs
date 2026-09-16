using System;
using UnityEngine;

namespace Killtime.Multi.Voice
{
    /// <summary>
    /// Lecteur audio dédié pour un joueur distant connecté dans la table VTT (ou retour test).
    /// Intègre un rééchantillonneur temps réel avec interpolation linéaire pour adapter la fréquence
    /// du codec (16 kHz / 8 kHz) à la fréquence de sortie Unity (AudioSettings.outputSampleRate, typiquement 48 kHz).
    /// Élimine totalement le pitch aigu (effet chipmunk) et les saccades de tampon (jitter buffer avec pré-tamponnage).
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class VTTRemoteVoicePlayer : MonoBehaviour
    {
        public string ClientId { get; private set; }
        public string Username { get; private set; }

        [Header("Contrôle Audio Joueur")]
        [Range(0f, 2f)] public float PlayerVolume = 1.0f;
        public bool IsMuted = false;

        public bool IsSpeaking { get; private set; }
        public float CurrentPeerRms { get; private set; }

        public event Action<string, bool> OnSpeakingChanged;

        private AudioSource _audioSource;
        private AudioClip _silentClip;

        // Jitter / Ring Buffer circulaire pour l'audio thread
        private const int RingBufferSize = 65536; // ~4 secondes de tampon à 16kHz
        private readonly float[] _ringBuffer = new float[RingBufferSize];
        private int _writeIdx;
        private int _readIdx;
        private int _bufferedSamples;
        private readonly object _bufferLock = new object();

        // Rééchantillonnage de lecture
        private int _sourceSampleRate = 16000;
        private double _readFraction = 0.0;
        private bool _isPrebuffering = true;
        private const int PrebufferThresholdSamples = 960; // 1 trame à 16kHz (60ms) pour reprise immédiate
        private float _fadeGain = 0.0f;

        private float _lastAudioPacketTime = -999f;
        private const float SpeakingTimeoutSec = 0.35f;

        public void Initialize(string clientId, string username, int initialSampleRate = 16000)
        {
            ClientId = clientId;
            Username = username;
            _sourceSampleRate = initialSampleRate > 0 ? initialSampleRate : 16000;
            name = $"[VoicePlayer] {username} ({clientId})";

            _audioSource = GetComponent<AudioSource>();
            if (_audioSource == null) _audioSource = gameObject.AddComponent<AudioSource>();

            _audioSource.playOnAwake = false;
            _audioSource.loop = true;
            _audioSource.spatialBlend = 0.0f; // 2D flat par défaut
            _audioSource.priority = 10; // Haute priorité vocale

            int outputRate = AudioSettings.outputSampleRate;
            if (outputRate <= 0) outputRate = 48000;

            // Clip silencieux infini pour déclencher OnAudioFilterRead
            if (_silentClip == null)
            {
                _silentClip = AudioClip.Create("VoiceLoopClip", outputRate, 1, outputRate, false);
                float[] silence = new float[outputRate];
                _silentClip.SetData(silence, 0);
            }
            _audioSource.clip = _silentClip;
            _audioSource.Play();
        }

        private void Update()
        {
            bool wasSpeaking = IsSpeaking;
            IsSpeaking = (Time.realtimeSinceStartup - _lastAudioPacketTime) < SpeakingTimeoutSec;

            if (wasSpeaking != IsSpeaking)
            {
                try { OnSpeakingChanged?.Invoke(ClientId, IsSpeaking); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }

        /// <summary>
        /// Reçoit des échantillons PCM décodés et les place dans le Jitter Buffer.
        /// </summary>
        public void EnqueueSamples(float[] pcmSamples, int sampleRate = 16000)
        {
            if (pcmSamples == null || pcmSamples.Length == 0) return;

            if (sampleRate > 0 && _sourceSampleRate != sampleRate)
            {
                _sourceSampleRate = sampleRate;
            }

            _lastAudioPacketTime = Time.realtimeSinceStartup;

            // Calcul du niveau RMS pour métrologie
            float sum = 0f;
            for (int i = 0; i < pcmSamples.Length; i++) sum += pcmSamples[i] * pcmSamples[i];
            CurrentPeerRms = Mathf.Sqrt(sum / pcmSamples.Length);

            lock (_bufferLock)
            {
                for (int i = 0; i < pcmSamples.Length; i++)
                {
                    if (_bufferedSamples >= RingBufferSize - 1)
                    {
                        // Tampon plein : on saute les plus anciens pour rattraper le flux direct
                        _readIdx = (_readIdx + 1) % RingBufferSize;
                        _bufferedSamples--;
                    }

                    _ringBuffer[_writeIdx] = pcmSamples[i];
                    _writeIdx = (_writeIdx + 1) % RingBufferSize;
                    _bufferedSamples++;
                }

                if (_isPrebuffering && _bufferedSamples >= PrebufferThresholdSamples)
                {
                    _isPrebuffering = false;
                }
            }
        }

        /// <summary>
        /// Callback audio Unity (s'exécute sur le thread audio à AudioSettings.outputSampleRate, ex: 48 kHz).
        /// Rééchantillonne en continu les échantillons source (16 kHz ou 8 kHz) vers le taux de sortie système.
        /// </summary>
        private void OnAudioFilterRead(float[] data, int channels)
        {
            float masterVoiceGain = VTTVoiceManager.Instance != null ? VTTVoiceManager.Instance.MasterOutputVolume : 1.0f;
            bool isDeafened = VTTVoiceManager.Instance != null && VTTVoiceManager.Instance.IsDeafened;
            float effectiveGain = (IsMuted || isDeafened) ? 0.0f : (PlayerVolume * masterVoiceGain);

            int outputRate = AudioSettings.outputSampleRate;
            if (outputRate <= 0) outputRate = 48000;

            double baseStep = (double)_sourceSampleRate / (double)outputRate;
            int samplesNeeded = data.Length / channels;

            lock (_bufferLock)
            {
                // Asservissement de vitesse de lecture pour compenser la dérive d'horloge matérielle
                int targetBuffer = 960;
                double driftFactor = 1.0;
                if (_bufferedSamples > targetBuffer + 480)
                {
                    driftFactor = 1.012; // Écoulement accéléré imperceptible pour résorber l'excédent
                }
                else if (_bufferedSamples < targetBuffer - 320 && _bufferedSamples > 0)
                {
                    driftFactor = 0.988; // Écoulement ralenti imperceptible pour éviter la disette
                }
                double step = baseStep * driftFactor;

                float fadeStep = 1f / 32f;

                for (int i = 0; i < samplesNeeded; i++)
                {
                    float sample = 0f;

                    if (!_isPrebuffering && _bufferedSamples > 0)
                    {
                        _fadeGain = Mathf.MoveTowards(_fadeGain, 1.0f, fadeStep);

                        int idx0 = _readIdx;
                        int idx1 = (idx0 + 1) % RingBufferSize;

                        float s0 = _ringBuffer[idx0];
                        float s1 = (_bufferedSamples > 1) ? _ringBuffer[idx1] : s0;

                        sample = (float)(s0 + _readFraction * (s1 - s0)) * effectiveGain * _fadeGain;

                        _readFraction += step;
                        while (_readFraction >= 1.0)
                        {
                            _readFraction -= 1.0;
                            _readIdx = (_readIdx + 1) % RingBufferSize;
                            _bufferedSamples--;
                            if (_bufferedSamples <= 0)
                            {
                                _bufferedSamples = 0;
                                _readFraction = 0.0;
                                _fadeGain = 0.0f;
                                break;
                            }
                        }
                    }
                    else
                    {
                        _fadeGain = 0.0f;
                    }

                    for (int ch = 0; ch < channels; ch++)
                    {
                        data[i * channels + ch] = sample;
                    }
                }
            }
        }

        private void OnDestroy()
        {
            if (_audioSource != null && _audioSource.isPlaying)
            {
                _audioSource.Stop();
            }
            if (_silentClip != null)
            {
                Destroy(_silentClip);
            }
        }
    }
}

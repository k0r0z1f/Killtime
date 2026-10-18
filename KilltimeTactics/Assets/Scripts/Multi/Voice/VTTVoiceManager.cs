using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.UI;

namespace Killtime.Multi.Voice
{
    public enum VoiceActivationMode
    {
        VoiceActivity = 0, // Détection d'activité vocale (VAD)
        PushToTalk = 1,    // Push-to-Talk (PTT)
        Continuous = 2     // Transmission continue
    }

    /// <summary>
    /// Gestionnaire central de la voix multijoueur VTT :
    /// - Capture micro via Unity Microphone API
    /// - Exécution du pipeline DSP de réduction de bruit et de métrologie
    /// - Sélection et encodage dynamique via les codecs voix (IMA-ADPCM 16k par défaut)
    /// - Émission des trames réseau via le hub WebSocket VTT
    /// - Réception et spatialisation/mixage des voix distantes via VTTRemoteVoicePlayer
    /// - Persistance des réglages via DevUIPreferences.
    /// </summary>
    [DisallowMultipleComponent]
    public partial class VTTVoiceManager : MonoBehaviour
    {
        public static VTTVoiceManager Instance { get; private set; }

        [Header("Périphérique & Capture")]
        [SerializeField] private string _selectedDevice = "";
        [SerializeField] private int _sampleRate = 16000;
        [SerializeField] private bool _autoStartCapture = true;

        [Header("Contrôles Voix")]
        [SerializeField] private VoiceCodecType _codecType = VoiceCodecType.ImaAdpcm16k;
        [SerializeField] private VoiceActivationMode _activationMode = VoiceActivationMode.VoiceActivity;
        [SerializeField] private KeyCode _pttKey = KeyCode.V;
        [SerializeField] [Range(0f, 2.5f)] private float _masterOutputVolume = 1.0f;
        [SerializeField] private bool _outputLimiterEnabled = true;
        [SerializeField] [Range(0.15f, 1.0f)] private float _outputCeiling = 0.50f;
        [SerializeField] private bool _gateDeleterEnabled = true;
        [SerializeField] private bool _radioCommsEffectEnabled = false;
        [SerializeField] private bool _antiKeyboardFilterEnabled = true;
        [SerializeField] private bool _voiceClarityEnabled = true;
        [SerializeField] [Range(0f, 1.5f)] private float _clarityBoostAmount = 0.65f;
        [SerializeField] private bool _isMuted = false;
        [SerializeField] private bool _isDeafened = false;
        [SerializeField] private bool _isLoopbackTestActive = false;

        [Header("Modulateur Vocal (Voice Changer)")]
        [SerializeField] private bool _voiceChangerEnabled = false;
        [SerializeField] private VoiceChangerPreset _voiceChangerPreset = VoiceChangerPreset.Off;

        // Propriétés publiques
        public VoiceDspProcessor Dsp => _dsp;
        public VoiceChangerProcessor VoiceChanger => _voiceChanger;
        public bool VoiceChangerEnabled => _voiceChangerEnabled;
        public VoiceChangerPreset CurrentVoiceChangerPreset => _voiceChangerPreset;
        public VoiceCodecType CurrentCodecType => _codecType;
        public VoiceActivationMode ActivationMode => _activationMode;
        public KeyCode PttKey => _pttKey;
        public float MasterOutputVolume => _masterOutputVolume;
        public bool OutputLimiterEnabled => _outputLimiterEnabled;
        public float OutputCeiling => _outputCeiling;
        public float CurrentOutputPeak { get; private set; }
        public bool GateDeleterEnabled => _gateDeleterEnabled;
        public bool RadioCommsEffectEnabled => _radioCommsEffectEnabled;
        public bool AntiKeyboardFilterEnabled => _antiKeyboardFilterEnabled;
        public bool VoiceClarityEnabled => _voiceClarityEnabled;
        public float ClarityBoostAmount => _clarityBoostAmount;
        public bool IsMuted => _isMuted;
        public bool IsDeafened => _isDeafened;
        public bool IsLoopbackTestActive => _isLoopbackTestActive;
        public bool IsCapturing => _isRecording;
        public bool IsLocalSpeaking => _isLocalSpeaking;
        public string ActiveDeviceName => _selectedDevice;
        public IReadOnlyList<string> AvailableDevices => _availableDevices;

        // Événements
        public event Action<bool> OnLocalSpeakingChanged;
        public event Action<string, bool> OnRemoteSpeakingChanged;
        public event Action OnDevicesRefreshed;

        // Lecteurs distants (clientId -> VTTRemoteVoicePlayer)
        private readonly Dictionary<string, VTTRemoteVoicePlayer> _remotePlayers = new();
        public IReadOnlyDictionary<string, VTTRemoteVoicePlayer> RemotePlayers => _remotePlayers;

        // Pipeline DSP, Modulateur Vocal et Codecs
        private VoiceDspProcessor _dsp;
        private VoiceChangerProcessor _voiceChanger;
        private IVoiceCodec _activeCodec;

        // Microphone Unity state
        private AudioClip _micClip;
        private bool _isRecording;
        private int _lastReadHead;
        private bool _capturePrimed = false;
        private readonly List<string> _availableDevices = new();

        // Fréquence matérielle et canaux du microphone (détectés dynamiquement)
        private int _capturedSampleRate = 16000;
        private int _capturedChannels = 1;
        public int CapturedSampleRate => _capturedSampleRate;
        public int CapturedChannels => _capturedChannels;

        // Rééchantillonneur temps réel Microphone -> Pipeline
        private double _resamplePos = 1.0;
        private float _lastInputSample = 0f;

        // Filtre anti-repliement biquad passe-bas (si sous-échantillonnage micro 48k/44k -> 16k)
        private bool _antiAliasingNeeded = false;
        private float _aaB0, _aaB1, _aaB2, _aaA1, _aaA2;
        private float _aaX1, _aaX2, _aaY1, _aaY2;

        // Tampons d'accumulation audio (60ms par trame @ 16kHz = 960 échantillons)
        private const int FrameDurationMs = 60;
        private int _frameSize;
        private float[] _accumulator;
        private int _accumCount;
        private float[] _dspOutBuffer;

        // VAD / PTT state
        private bool _isLocalSpeaking;
        private float _vadHangoverSec = 0.42f;
        private float _vadHangoverTimer;
        private int _packetSequence;
        private bool _wasTransmitting;
        private float _gateGain = 0f;
        private float _smoothedLimiterGain = 1.0f;
        private float _radioHpState = 0f;
        private float _radioLpState = 0f;
        private float _radioPrevIn = 0f;
        private float _clarityHpState = 0f;
        private float _clarityBpState = 0f;

        // Joueur local pour test loopback
        private VTTRemoteVoicePlayer _loopbackPlayer;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            InitializeDspAndCodecs();
            LoadPreferences();
            RefreshDevices();

            if (_autoStartCapture)
            {
                StartCapture();
            }
        }

        private void InitializeDspAndCodecs()
        {
            _activeCodec = VoiceCodecRegistry.GetCodec(_codecType);
            _sampleRate = _activeCodec.SampleRate;

            _frameSize = (_sampleRate * FrameDurationMs) / 1000;
            _accumulator = new float[_frameSize];
            _dspOutBuffer = new float[_frameSize];

            _dsp = new VoiceDspProcessor(_sampleRate);
            _dsp.AntiKeyboardFilterEnabled = _antiKeyboardFilterEnabled || _radioCommsEffectEnabled;
            if (_voiceChanger == null) _voiceChanger = new VoiceChangerProcessor(_sampleRate);
            else _voiceChanger.SetSampleRate(_sampleRate);

            _resamplePos = 1.0;
            _lastInputSample = 0f;
            _accumCount = 0;
            SetupAntiAliasingFilter(_capturedSampleRate, _sampleRate);
        }

        private void SetupAntiAliasingFilter(int inRate, int outRate)
        {
            _aaX1 = _aaX2 = _aaY1 = _aaY2 = 0f;
            if (inRate <= outRate)
            {
                _antiAliasingNeeded = false;
                return;
            }

            _antiAliasingNeeded = true;
            float fs = inRate;
            float fc = Mathf.Min(7200f, outRate * 0.44f);
            float w0 = 2f * Mathf.PI * fc / fs;
            float cosW0 = Mathf.Cos(w0);
            float sinW0 = Mathf.Sin(w0);
            float alpha = sinW0 / (2f * 0.7071f);

            float a0 = 1f + alpha;
            _aaB0 = ((1f - cosW0) / 2f) / a0;
            _aaB1 = (1f - cosW0) / a0;
            _aaB2 = ((1f - cosW0) / 2f) / a0;
            _aaA1 = (-2f * cosW0) / a0;
            _aaA2 = (1f - alpha) / a0;
        }

        private void ApplyAntiAliasingFilter(float[] samples, int count)
        {
            if (!_antiAliasingNeeded || samples == null || count <= 0) return;
            for (int i = 0; i < count; i++)
            {
                float x = samples[i];
                float y = _aaB0 * x + _aaB1 * _aaX1 + _aaB2 * _aaX2 - _aaA1 * _aaY1 - _aaA2 * _aaY2;
                if (Mathf.Abs(y) < 1e-15f) y = 0f;
                else if (y > 1.0f) y = 1.0f;
                else if (y < -1.0f) y = -1.0f;

                _aaX2 = _aaX1;
                _aaX1 = x;
                _aaY2 = _aaY1;
                _aaY1 = y;
                samples[i] = y;
            }
        }

        private void OnEnable()
        {
            var room = VTTRoomManager.Instance;
            if (room != null)
            {
                room.OnLeft += HandleRoomLeft;
                room.OnPresenceChanged += HandlePresenceChanged;
            }
        }

        private void OnDisable()
        {
            var room = VTTRoomManager.Instance;
            if (room != null)
            {
                room.OnLeft -= HandleRoomLeft;
                room.OnPresenceChanged -= HandlePresenceChanged;
            }
            StopCapture();
        }
    }
}

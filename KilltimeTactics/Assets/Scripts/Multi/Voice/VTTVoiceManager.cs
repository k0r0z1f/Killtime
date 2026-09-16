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
    public class VTTVoiceManager : MonoBehaviour
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
        private readonly List<string> _availableDevices = new();

        // Tampons d'accumulation audio (60ms par trame @ 16kHz = 960 échantillons)
        private const int FrameDurationMs = 60;
        private int _frameSize;
        private float[] _accumulator;
        private int _accumCount;
        private float[] _dspOutBuffer;

        // VAD / PTT state
        private bool _isLocalSpeaking;
        private float _vadHangoverSec = 0.35f;
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

        public void RefreshDevices()
        {
            _availableDevices.Clear();
            var devs = Microphone.devices;
            if (devs != null && devs.Length > 0)
            {
                _availableDevices.AddRange(devs);
            }

            if (string.IsNullOrEmpty(_selectedDevice) || !_availableDevices.Contains(_selectedDevice))
            {
                _selectedDevice = _availableDevices.Count > 0 ? _availableDevices[0] : "";
            }

            try { OnDevicesRefreshed?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
        }

        public void SelectDevice(string deviceName)
        {
            if (_selectedDevice == deviceName && _isRecording) return;
            _selectedDevice = deviceName;
            SavePreferences();

            if (_isRecording)
            {
                StartCapture();
            }
        }

        public void StartCapture()
        {
            StopCapture();

            if (_availableDevices.Count == 0)
            {
                RefreshDevices();
                if (_availableDevices.Count == 0)
                {
                    Debug.LogWarning("[VTTVoiceManager] Aucun microphone détecté.");
                    return;
                }
            }

            string device = string.IsNullOrEmpty(_selectedDevice) ? null : _selectedDevice;

            try
            {
                int minFreq, maxFreq;
                Microphone.GetDeviceCaps(device, out minFreq, out maxFreq);
                int targetFreq = _sampleRate;
                if (maxFreq > 0 && targetFreq > maxFreq) targetFreq = maxFreq;
                if (minFreq > 0 && targetFreq < minFreq) targetFreq = minFreq;

                _micClip = Microphone.Start(device, true, 10, targetFreq);
                _isRecording = (_micClip != null);
                _lastReadHead = 0;
                _accumCount = 0;
                _dsp?.Reset();
                Debug.Log($"[VTTVoiceManager] Capture démarrée sur '{device ?? "Défaut"}' @ {targetFreq} Hz.");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[VTTVoiceManager] Échec du démarrage du microphone: {e.Message}");
                _isRecording = false;
            }
        }

        public void StopCapture()
        {
            if (_isRecording)
            {
                try
                {
                    string device = string.IsNullOrEmpty(_selectedDevice) ? null : _selectedDevice;
                    Microphone.End(device);
                }
                catch { /* ignore */ }
                _isRecording = false;
            }

            if (_micClip != null)
            {
                Destroy(_micClip);
                _micClip = null;
            }
        }

        private void Update()
        {
            if (!_isRecording || _micClip == null) return;

            string device = string.IsNullOrEmpty(_selectedDevice) ? null : _selectedDevice;
            int currentHead = Microphone.GetPosition(device);
            if (currentHead < 0 || currentHead == _lastReadHead) return;

            int clipSamples = _micClip.samples;
            int samplesToRead;
            if (currentHead >= _lastReadHead)
            {
                samplesToRead = currentHead - _lastReadHead;
            }
            else
            {
                samplesToRead = (clipSamples - _lastReadHead) + currentHead;
            }

            if (samplesToRead <= 0) return;

            // Lecture et accumulation
            float[] tempBuffer = new float[samplesToRead];
            if (currentHead >= _lastReadHead)
            {
                _micClip.GetData(tempBuffer, _lastReadHead);
            }
            else
            {
                int part1 = clipSamples - _lastReadHead;
                float[] b1 = new float[part1];
                _micClip.GetData(b1, _lastReadHead);
                Array.Copy(b1, 0, tempBuffer, 0, part1);

                if (currentHead > 0)
                {
                    float[] b2 = new float[currentHead];
                    _micClip.GetData(b2, 0);
                    Array.Copy(b2, 0, tempBuffer, part1, currentHead);
                }
            }
            _lastReadHead = currentHead;

            // Découpage en trames de 60ms
            int readOffset = 0;
            while (readOffset < samplesToRead)
            {
                int needed = _frameSize - _accumCount;
                int available = samplesToRead - readOffset;
                int toCopy = Mathf.Min(needed, available);

                Array.Copy(tempBuffer, readOffset, _accumulator, _accumCount, toCopy);
                _accumCount += toCopy;
                readOffset += toCopy;

                if (_accumCount >= _frameSize)
                {
                    ProcessAudioFrame(_accumulator, _frameSize);
                    _accumCount = 0;
                }
            }
        }

        private void ProcessAudioFrame(float[] rawFrame, int count)
        {
            float dt = FrameDurationMs / 1000f;
            if (_dsp != null)
            {
                _dsp.AntiKeyboardFilterEnabled = _antiKeyboardFilterEnabled || _radioCommsEffectEnabled;
            }
            _dsp.Process(rawFrame, _dspOutBuffer, count, dt);

            // Détermination de l'état d'émission
            bool shouldTransmit = false;

            if (!_isMuted && !_isDeafened)
            {
                switch (_activationMode)
                {
                    case VoiceActivationMode.VoiceActivity:
                        if (_dsp.IsGateOpen)
                        {
                            _vadHangoverTimer = _vadHangoverSec;
                            shouldTransmit = true;
                        }
                        else if (_vadHangoverTimer > 0f)
                        {
                            _vadHangoverTimer -= dt;
                            shouldTransmit = true;
                        }
                        break;

                    case VoiceActivationMode.PushToTalk:
                        shouldTransmit = Input.GetKey(_pttKey);
                        break;

                    case VoiceActivationMode.Continuous:
                        shouldTransmit = true;
                        break;
                }
            }

            // Mise à jour de l'indicateur local
            if (_isLocalSpeaking != shouldTransmit)
            {
                _isLocalSpeaking = shouldTransmit;
                try { OnLocalSpeakingChanged?.Invoke(_isLocalSpeaking); }
                catch (Exception e) { Debug.LogException(e); }
            }

            // Nettoyage et Effets à l'enregistrement (Noise Gate Deleter & Radio Comms)
            ApplyRecordingEffects(_dspOutBuffer, count, shouldTransmit, dt);

            // Plafond Anti-Cris / Limiteur Dynamique (actif en temps réel à l'enregistrement et au test)
            if (_outputLimiterEnabled)
            {
                ApplyLimiterBuffer(_dspOutBuffer, count);
            }
            else
            {
                CurrentOutputPeak = _dsp != null ? _dsp.CurrentPeak : 0f;
            }

            // Émission réseau avec trame de fin pour clore le flux sans coupure abrupte
            if (shouldTransmit || _wasTransmitting)
            {
                TransmitFrame(_dspOutBuffer, count);
            }
            _wasTransmitting = shouldTransmit;

            // Test Loopback local (pour calibrer son micro en s'écoutant)
            if (_isLoopbackTestActive)
            {
                EnsureLoopbackPlayer();
                _loopbackPlayer.EnqueueSamples(_dspOutBuffer);
            }
        }

        private void TransmitFrame(float[] pcm, int count)
        {
            var room = VTTRoomManager.Instance;
            if (room == null || !room.InRoom) return;

            byte[] encoded = _activeCodec.Encode(pcm, count);
            if (encoded == null || encoded.Length == 0) return;

            string base64 = Convert.ToBase64String(encoded);
            _packetSequence++;

            string payload = VTTProtocol.BuildVoicePayloadJson((int)_codecType, _sampleRate, _packetSequence, base64);
            room.SendTableOp(VTTProtocol.OpVoice, payload);
        }

        /// <summary>
        /// Appelé par VTTRoomManager lors de la réception d'un op "voice".
        /// </summary>
        public void ReceiveVoicePacket(string fromClientId, string fromUsername, string payloadJson)
        {
            if (string.IsNullOrEmpty(fromClientId) || string.IsNullOrEmpty(payloadJson)) return;
            if (_isDeafened) return;

            var room = VTTRoomManager.Instance;
            if (room != null && fromClientId == room.ClientId) return; // Ignore l'écho local

            VTTVoicePayload payload;
            try
            {
                payload = JsonUtility.FromJson<VTTVoicePayload>(payloadJson);
            }
            catch
            {
                return;
            }

            if (payload == null || string.IsNullOrEmpty(payload.data)) return;

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(payload.data);
            }
            catch
            {
                return;
            }

            var codec = VoiceCodecRegistry.GetCodec((VoiceCodecType)payload.codec);
            float[] decoded = codec.Decode(bytes);
            if (decoded == null || decoded.Length == 0) return;

            if (_outputLimiterEnabled)
            {
                ApplyLimiterBuffer(decoded, decoded.Length);
            }

            var player = GetOrCreateRemotePlayer(fromClientId, fromUsername);
            player.EnqueueSamples(decoded);
        }

        public VTTRemoteVoicePlayer GetOrCreateRemotePlayer(string clientId, string username)
        {
            if (_remotePlayers.TryGetValue(clientId, out var player) && player != null)
            {
                return player;
            }

            var go = new GameObject($"[Voice] {username}");
            go.transform.SetParent(transform);
            player = go.AddComponent<VTTRemoteVoicePlayer>();
            player.Initialize(clientId, username);

            player.OnSpeakingChanged += (id, speaking) =>
            {
                try { OnRemoteSpeakingChanged?.Invoke(id, speaking); }
                catch (Exception e) { Debug.LogException(e); }
            };

            _remotePlayers[clientId] = player;
            return player;
        }

        private void EnsureLoopbackPlayer()
        {
            if (_loopbackPlayer == null)
            {
                var go = new GameObject("[Voice] LoopbackTest");
                go.transform.SetParent(transform);
                _loopbackPlayer = go.AddComponent<VTTRemoteVoicePlayer>();
                _loopbackPlayer.Initialize("local_loopback", "Moi (Test)");
            }
        }

        private void HandleRoomLeft(string reason)
        {
            CleanupRemotePlayers();
        }

        private void HandlePresenceChanged()
        {
            var room = VTTRoomManager.Instance;
            if (room == null || !room.InRoom)
            {
                CleanupRemotePlayers();
                return;
            }

            // Supprimer les joueurs déconnectés
            var activeIds = new HashSet<string>();
            foreach (var m in room.Members)
            {
                if (m != null && !string.IsNullOrEmpty(m.clientId))
                    activeIds.Add(m.clientId);
            }

            var toRemove = new List<string>();
            foreach (var kvp in _remotePlayers)
            {
                if (!activeIds.Contains(kvp.Key))
                {
                    toRemove.Add(kvp.Key);
                }
            }

            foreach (var id in toRemove)
            {
                if (_remotePlayers.TryGetValue(id, out var p) && p != null)
                {
                    Destroy(p.gameObject);
                }
                _remotePlayers.Remove(id);
            }
        }

        public void CleanupRemotePlayers()
        {
            foreach (var p in _remotePlayers.Values)
            {
                if (p != null) Destroy(p.gameObject);
            }
            _remotePlayers.Clear();
        }

        // ---------- Méthodes de Configuration Publiques ----------

        public void SetCodec(VoiceCodecType codec)
        {
            if (_codecType == codec) return;
            _codecType = codec;
            InitializeDspAndCodecs();
            SavePreferences();
            if (_isRecording) StartCapture();
        }

        public void SetActivationMode(VoiceActivationMode mode)
        {
            _activationMode = mode;
            SavePreferences();
        }

        public void SetPttKey(KeyCode key)
        {
            _pttKey = key;
            SavePreferences();
        }

        public void SetInputGain(float gain)
        {
            if (_dsp != null) _dsp.InputGain = gain;
            SavePreferences();
        }

        public void SetGateThreshold(float threshold)
        {
            if (_dsp != null) _dsp.GateThreshold = threshold;
            SavePreferences();
        }

        public void SetNoiseGateEnabled(bool enabled)
        {
            if (_dsp != null) _dsp.NoiseGateEnabled = enabled;
            SavePreferences();
        }

        public void SetNoiseReductionEnabled(bool enabled)
        {
            if (_dsp != null) _dsp.NoiseReductionEnabled = enabled;
            SavePreferences();
        }

        public void SetHighPassFilterEnabled(bool enabled)
        {
            if (_dsp != null) _dsp.HighPassFilterEnabled = enabled;
            SavePreferences();
        }

        public void SetMasterOutputVolume(float vol)
        {
            _masterOutputVolume = Mathf.Clamp(vol, 0f, 2f);
            SavePreferences();
        }

        public void SetOutputLimiterEnabled(bool enabled)
        {
            _outputLimiterEnabled = enabled;
            SavePreferences();
        }

        public void SetOutputCeiling(float ceiling)
        {
            _outputCeiling = Mathf.Clamp(ceiling, 0.15f, 1.0f);
            SavePreferences();
        }

        public void SetGateDeleterEnabled(bool enabled)
        {
            _gateDeleterEnabled = enabled;
            SavePreferences();
        }

        public void SetRadioCommsEffectEnabled(bool enabled)
        {
            _radioCommsEffectEnabled = enabled;
            if (_dsp != null) _dsp.AntiKeyboardFilterEnabled = _antiKeyboardFilterEnabled || _radioCommsEffectEnabled;
            SavePreferences();
        }

        public void SetAntiKeyboardFilterEnabled(bool enabled)
        {
            _antiKeyboardFilterEnabled = enabled;
            if (_dsp != null) _dsp.AntiKeyboardFilterEnabled = _antiKeyboardFilterEnabled || _radioCommsEffectEnabled;
            SavePreferences();
        }

        public void SetVoiceClarityEnabled(bool enabled)
        {
            _voiceClarityEnabled = enabled;
            SavePreferences();
        }

        public void SetClarityBoostAmount(float amount)
        {
            _clarityBoostAmount = Mathf.Clamp(amount, 0f, 1.5f);
            SavePreferences();
        }

        public void SetVoiceChangerEnabled(bool enabled)
        {
            _voiceChangerEnabled = enabled;
            if (_voiceChanger != null) _voiceChanger.Enabled = enabled;
            SavePreferences();
        }

        public void SetVoiceChangerPreset(VoiceChangerPreset preset)
        {
            _voiceChangerPreset = preset;
            if (_voiceChanger != null)
            {
                _voiceChanger.ApplyPreset(preset);
                _voiceChangerEnabled = _voiceChanger.Enabled;
            }
            SavePreferences();
        }

        public void SetVoiceChangerPitch(float semitones)
        {
            if (_voiceChanger != null) _voiceChanger.PitchSemitones = Mathf.Clamp(semitones, -12f, 12f);
            SavePreferences();
        }

        public void SetVoiceChangerFormant(float formant)
        {
            if (_voiceChanger != null) _voiceChanger.FormantShift = Mathf.Clamp(formant, -1f, 1f);
            SavePreferences();
        }

        public void SetVoiceChangerDrive(float drive)
        {
            if (_voiceChanger != null) _voiceChanger.HarmonicDrive = Mathf.Clamp01(drive);
            SavePreferences();
        }

        public void SetVoiceChangerRobotic(float robotic)
        {
            if (_voiceChanger != null) _voiceChanger.RoboticModulation = Mathf.Clamp01(robotic);
            SavePreferences();
        }

        public void SetVoiceChangerMix(float mix)
        {
            if (_voiceChanger != null) _voiceChanger.WetDryMix = Mathf.Clamp01(mix);
            SavePreferences();
        }

        private void ApplyRecordingEffects(float[] buffer, int count, bool isSpeaking, float dt)
        {
            if (buffer == null || count <= 0) return;

            // 1. Égaliseur de Présence (exécuté en premier sur le signal audio pur)
            if (_voiceClarityEnabled && !_radioCommsEffectEnabled)
            {
                float aHp = Mathf.Exp(-2f * Mathf.PI * 2600f / _sampleRate);
                float aBp = 1f - Mathf.Exp(-2f * Mathf.PI * 5400f / _sampleRate);
                float gain = _clarityBoostAmount * 0.35f;

                for (int i = 0; i < count; i++)
                {
                    float s = buffer[i];
                    _clarityHpState = aHp * _clarityHpState + (1f - aHp) * s;
                    float presence = s - _clarityHpState;
                    _clarityBpState += aBp * (presence - _clarityBpState);
                    buffer[i] = s + (_clarityBpState * gain);
                }
            }

            // 2. Filtre Radio Comms Tactique
            if (_radioCommsEffectEnabled)
            {
                float rHp = Mathf.Exp(-2f * Mathf.PI * 300f / _sampleRate);
                float aLp = 1f - Mathf.Exp(-2f * Mathf.PI * 3400f / _sampleRate);

                for (int i = 0; i < count; i++)
                {
                    float s = buffer[i];
                    float hp = s - _radioPrevIn + rHp * _radioHpState;
                    _radioPrevIn = s;
                    _radioHpState = hp;
                    _radioLpState += aLp * (hp - _radioLpState);
                    buffer[i] = (float)Math.Tanh(_radioLpState * 1.35) * 0.92f;
                }
            }

            // 2.b Modulateur Vocal Avancé (Pitch Shift, Formants, Saturation, Ring Mod)
            if (_voiceChangerEnabled && _voiceChanger != null)
            {
                _voiceChanger.Process(buffer, count);
            }

            // 3. Noise Gate Deleter (exécuté en dernier pour couper tout résidu de filtre ou souffle)
            if (_gateDeleterEnabled)
            {
                float targetGain = isSpeaking ? 1.0f : 0.0f;
                float attackStep = 1f - Mathf.Exp(-1f / (_sampleRate * 0.012f));
                float releaseStep = 1f - Mathf.Exp(-1f / (_sampleRate * 0.045f));

                for (int i = 0; i < count; i++)
                {
                    float rate = (targetGain > _gateGain) ? attackStep : releaseStep;
                    _gateGain += rate * (targetGain - _gateGain);
                    buffer[i] *= _gateGain;
                }
            }
        }

        public void ApplyOutputLimiter(float[] buffer)
        {
            if (buffer != null) ApplyLimiterBuffer(buffer, buffer.Length);
        }

        public void ApplyLimiterBuffer(float[] buffer, int count)
        {
            if (!_outputLimiterEnabled || buffer == null || count <= 0) return;
            float ceiling = _outputCeiling;
            if (ceiling >= 0.99f) return;

            float maxPeak = 0f;
            for (int i = 0; i < count; i++)
            {
                float a = Mathf.Abs(buffer[i]);
                if (a > maxPeak) maxPeak = a;
            }

            float targetGain = (maxPeak > ceiling) ? (ceiling / maxPeak) : 1.0f;
            float attackCoeff = 1f - Mathf.Exp(-1f / (_sampleRate * 0.005f));
            float releaseCoeff = 1f - Mathf.Exp(-1f / (_sampleRate * 0.075f));

            float kneeThreshold = ceiling * 0.88f;
            float kneeSpan = Mathf.Max(0.001f, ceiling - kneeThreshold);

            for (int i = 0; i < count; i++)
            {
                float coeff = (targetGain < _smoothedLimiterGain) ? attackCoeff : releaseCoeff;
                _smoothedLimiterGain += coeff * (targetGain - _smoothedLimiterGain);

                float s = buffer[i] * _smoothedLimiterGain;
                float abs = Mathf.Abs(s);

                if (abs > kneeThreshold)
                {
                    float excess = abs - kneeThreshold;
                    float compressed = kneeThreshold + kneeSpan * (float)Math.Tanh(excess / kneeSpan);
                    s = Mathf.Sign(s) * Mathf.Min(compressed, ceiling);
                }

                buffer[i] = s;
            }

            CurrentOutputPeak = Mathf.Min(maxPeak * _smoothedLimiterGain, ceiling);
        }

        public void SetMuted(bool muted)
        {
            _isMuted = muted;
            SavePreferences();
        }

        public void SetDeafened(bool deafened)
        {
            _isDeafened = deafened;
            if (_isDeafened) _isMuted = true;
            SavePreferences();
        }

        public void SetLoopbackTest(bool active)
        {
            _isLoopbackTestActive = active;
        }

        // ---------- Persistance DevUIPreferences ----------

        private void LoadPreferences()
        {
            var p = DevUIPreferences.Current;
            if (p == null) return;

            if (!string.IsNullOrEmpty(p.VoiceInputDevice)) _selectedDevice = p.VoiceInputDevice;
            _masterOutputVolume = p.VoiceOutputVolume;
            _outputLimiterEnabled = p.VoiceOutputLimiterEnabled;
            _outputCeiling = p.VoiceOutputCeiling > 0.05f ? p.VoiceOutputCeiling : 0.50f;
            _gateDeleterEnabled = p.VoiceGateDeleterEnabled;
            _radioCommsEffectEnabled = p.VoiceRadioCommsEffectEnabled;
            _antiKeyboardFilterEnabled = p.VoiceAntiKeyboardFilter;
            _voiceClarityEnabled = p.VoiceClarityEnabled;
            _clarityBoostAmount = p.VoiceClarityAmount > 0.01f ? p.VoiceClarityAmount : 0.65f;
            if (_dsp != null)
            {
                _dsp.InputGain = p.VoiceInputGain;
                _dsp.GateThreshold = p.VoiceVadThreshold;
                _dsp.NoiseGateEnabled = p.VoiceNoiseGateEnabled;
                _dsp.NoiseReductionEnabled = p.VoiceNoiseReductionEnabled;
                _dsp.HighPassFilterEnabled = p.VoiceHighPassFilter;
            }
            _activationMode = (VoiceActivationMode)Mathf.Clamp(p.VoiceActivationMode, 0, 2);
            _vadHangoverSec = Mathf.Max(0.1f, p.VoiceVadHangoverMs / 1000f);
            _pttKey = (KeyCode)p.VoicePttKey;
            _codecType = (VoiceCodecType)Mathf.Clamp(p.VoiceCodec, 0, 4);
            _isMuted = p.VoiceMuted;
            _isDeafened = p.VoiceDeafened;

            _voiceChangerEnabled = p.VoiceChangerEnabled;
            int maxPreset = Enum.GetValues(typeof(VoiceChangerPreset)).Length - 1;
            _voiceChangerPreset = (VoiceChangerPreset)Mathf.Clamp(p.VoiceChangerPreset, 0, maxPreset);
            if (_voiceChanger != null)
            {
                _voiceChanger.Enabled = _voiceChangerEnabled;
                _voiceChanger.Preset = _voiceChangerPreset;
                _voiceChanger.PitchSemitones = p.VoiceChangerPitch;
                _voiceChanger.FormantShift = p.VoiceChangerFormant;
                _voiceChanger.HarmonicDrive = p.VoiceChangerDrive;
                _voiceChanger.RoboticModulation = p.VoiceChangerRobotic;
                _voiceChanger.WetDryMix = p.VoiceChangerMix;
            }
        }

        private void SavePreferences()
        {
            var p = DevUIPreferences.Current;
            if (p == null) return;

            p.VoiceInputDevice = _selectedDevice ?? "";
            p.VoiceOutputVolume = _masterOutputVolume;
            p.VoiceOutputLimiterEnabled = _outputLimiterEnabled;
            p.VoiceOutputCeiling = _outputCeiling;
            p.VoiceGateDeleterEnabled = _gateDeleterEnabled;
            p.VoiceRadioCommsEffectEnabled = _radioCommsEffectEnabled;
            p.VoiceAntiKeyboardFilter = _antiKeyboardFilterEnabled;
            p.VoiceClarityEnabled = _voiceClarityEnabled;
            p.VoiceClarityAmount = _clarityBoostAmount;
            if (_dsp != null)
            {
                p.VoiceInputGain = _dsp.InputGain;
                p.VoiceVadThreshold = _dsp.GateThreshold;
                p.VoiceNoiseGateEnabled = _dsp.NoiseGateEnabled;
                p.VoiceNoiseReductionEnabled = _dsp.NoiseReductionEnabled;
                p.VoiceHighPassFilter = _dsp.HighPassFilterEnabled;
            }
            p.VoiceActivationMode = (int)_activationMode;
            p.VoiceVadHangoverMs = _vadHangoverSec * 1000f;
            p.VoicePttKey = (int)_pttKey;
            p.VoiceCodec = (int)_codecType;
            p.VoiceMuted = _isMuted;
            p.VoiceDeafened = _isDeafened;

            p.VoiceChangerEnabled = _voiceChangerEnabled;
            p.VoiceChangerPreset = (int)_voiceChangerPreset;
            if (_voiceChanger != null)
            {
                p.VoiceChangerPitch = _voiceChanger.PitchSemitones;
                p.VoiceChangerFormant = _voiceChanger.FormantShift;
                p.VoiceChangerDrive = _voiceChanger.HarmonicDrive;
                p.VoiceChangerRobotic = _voiceChanger.RoboticModulation;
                p.VoiceChangerMix = _voiceChanger.WetDryMix;
            }

            DevUIPreferences.MarkDirty();
        }
    }
}

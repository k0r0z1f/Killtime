using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Audio;

namespace Killtime.Audio.Experimental
{
    public enum ChameleonSynthMode
    {
        SubBassOnly = 0,
        CyberneticHybrid = 1,
        FullNytharite = 2
    }

    public enum ChameleonBassStyle
    {
        Acid303 = 0,
        ReeseCyber = 1,
        DeepSub808 = 2,
        NeuroMod = 3
    }

    public enum ChameleonChoirStyle
    {
        EtherealAah = 0,
        CyberOoh = 1,
        ArcanotechHymn = 2
    }

    public enum ChameleonVocalStyle
    {
        CyberSoprano = 0,
        AnalogTenor = 1,
        VocaloidMorph = 2
    }

    /// <summary>
    /// Module Caméléon Auto-Calibré (Auto-Gain Control & Dynamic Pitch Filter) :
    /// Régule automatiquement le préampli d'entrée, ajuste le filtre d'extraction
    /// en temps réel pour maximiser la clarté NSDF et isole le flux PipeWire sans Larsen.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public class SystemAudioChameleon : MonoBehaviour
    {
        public static SystemAudioChameleon Instance { get; private set; }

        [Header("Capture & Auto-Calibration")]
        [SerializeField] private bool _enableChameleonMode = false;
        [SerializeField] private bool _autoCalibrateClarity = true;
        [SerializeField] private int _sampleRate = 48000;
        [SerializeField] [Range(0.5f, 6.0f)] private float _inputPreGain = 2.2f;
        [SerializeField] [Range(0.005f, 0.2f)] private float _silenceThreshold = 0.010f;

        [Header("Overlay Harmonique / Lead")]
        [SerializeField] private ChameleonSynthMode _synthMode = ChameleonSynthMode.CyberneticHybrid;
        [SerializeField] [Range(0f, 1f)] private float _mimicVolume = 0.45f;
        [SerializeField] private bool _generateCyberneticOverlay = true;
        [SerializeField] private bool _isolateByMutingGameMusic = true;
        [SerializeField] [Range(10f, 80f)] private float _portamentoSpeed = 45f;

        [Header("Émulateur Basse (Bass Synth)")]
        [SerializeField] private bool _emulateBass = true;
        [SerializeField] private ChameleonBassStyle _bassStyle = ChameleonBassStyle.Acid303;
        [SerializeField] private bool _bassRhythmicPluck = true;
        [SerializeField] [Range(0f, 2.5f)] private float _bassMasterVolume = 1.35f;
        [SerializeField] [Range(1f, 8f)] private float _bassDrive = 3.2f;
        [SerializeField] [Range(150f, 5000f)] private float _bassCutoff = 1200f;
        [SerializeField] [Range(0.2f, 3.5f)] private float _bassResonance = 2.2f;
        [SerializeField] [Range(-1, 1)] private int _bassOctave = 0;
        [SerializeField] [Range(0f, 1f)] private float _bassSidechainDuck = 0.70f;
        [SerializeField] [Range(0f, 1f)] private float _bassSubCleanLayer = 0.55f;
        [SerializeField] [Range(0f, 1f)] private float _bassPunch = 0.50f;

        [Header("Voix de Chorale (Choir Synth)")]
        [SerializeField] private bool _emulateChoir = true;
        [SerializeField] private ChameleonChoirStyle _choirStyle = ChameleonChoirStyle.EtherealAah;
        [SerializeField] [Range(0f, 2.5f)] private float _choirMasterVolume = 0.85f;
        [SerializeField] [Range(0.5f, 4.0f)] private float _choirDetune = 1.6f;
        [SerializeField] [Range(3.0f, 8.0f)] private float _choirVibratoRate = 5.2f;
        [SerializeField] [Range(0f, 1f)] private float _choirShimmer = 0.40f;
        [SerializeField] [Range(-1, 2)] private int _choirOctave = 0;

        [Header("Voix Chantée Émulée (Singing Voice Synth)")]
        [SerializeField] private bool _emulateVocal = true;
        [SerializeField] private ChameleonVocalStyle _vocalStyle = ChameleonVocalStyle.CyberSoprano;
        [SerializeField] [Range(0f, 2.5f)] private float _vocalMasterVolume = 0.95f;
        [SerializeField] [Range(3.5f, 7.5f)] private float _vocalVibratoRate = 5.4f;
        [SerializeField] [Range(0f, 1f)] private float _vocalVibratoDepth = 0.55f;
        [SerializeField] [Range(0f, 1f)] private float _vocalBreathiness = 0.30f;
        [SerializeField] [Range(5f, 60f)] private float _vocalPortamento = 28f;
        [SerializeField] [Range(-1, 2)] private int _vocalOctave = 0;

        [Header("Émulation Percussive (Drums)")]
        [SerializeField] private bool _emulateDrums = true;
        [SerializeField] [Range(0f, 2.5f)] private float _drumMasterVolume = 1.15f;
        [SerializeField] [Range(0f, 2.5f)] private float _kickVolume = 1.2f;
        [SerializeField] [Range(0f, 2.5f)] private float _snareVolume = 1.0f;
        [SerializeField] [Range(0f, 2.5f)] private float _hatVolume = 0.8f;

        [Header("Synchronisation & Latence")]
        [SerializeField] private bool _usePredictiveSync = false;
        [SerializeField] [Range(0f, 250f)] private float _latencyCompensationMs = 95f;

        private float _savedGameMusicVolume = -1f;
        private AudioSource _audioSource;
        private AudioClip _micClip;
        private string _activeDeviceName;
        private const int ClipDurationSec = 10;

        private const int BufferSize = 4096;
        private readonly float[] _analysisBuffer = new float[BufferSize];
        private readonly float[] _unityOutputBuffer = new float[BufferSize];
        private readonly float[] _cleanedBuffer = new float[BufferSize];

        // Tampon décimé x4 (12 kHz) pour les leads et voix
        private const int DecimationFactor = 4;
        private const int DecimatedSize = BufferSize / DecimationFactor;
        private readonly float[] _decimatedPitchBuffer = new float[DecimatedSize];
        private readonly float[] _nsdfBuffer = new float[DecimatedSize];

        // Tampon dédié sub-basse x8 (6 kHz)
        private const int BassDecimation = 8;
        private const int BassBufferSize = BufferSize / BassDecimation;
        private readonly float[] _bassDecimatedBuffer = new float[BassBufferSize];
        private readonly float[] _bassNsdfBuffer = new float[BassBufferSize];

        // Télémétrie DSP
        public bool IsExternalMusicDetected { get; private set; }
        public bool IsHarmonicNoteStable { get; private set; }
        public float PitchConfidence { get; private set; }
        public float DetectedFrequencyHz { get; private set; }
        public float DetectedBassFrequencyHz { get; private set; }
        public float BassPitchConfidence { get; private set; }
        public string DetectedNoteName { get; private set; } = "---";
        public int DetectedCentsOffset { get; private set; }

        public float RawInputRMS { get; private set; }
        public float UnityOutputRMS { get; private set; }
        public float DetectedEnergyRMS { get; private set; }

        public float BassEnergyRMS { get; private set; }
        public float MidEnergyRMS { get; private set; }
        public float HighEnergyRMS { get; private set; }
        public float SpectralCentroidHz { get; private set; }

        public float DetectedBPM { get; private set; } = 120f;
        public float BeatPulse { get; private set; }
        public float KickPulse { get; private set; }
        public float SnarePulse { get; private set; }
        public float HatPulse { get; private set; }
        public float EstimatedLoopbackDelayMs { get; private set; }
        public float PhaseLockConfidence { get; private set; }
        public float HarmonicPurity { get; private set; } = 1.0f;
        public float CurrentBassPitchHz => _currentBassFreq;
        public float CurrentChoirPitchHz => _currentChoirFreq;
        public float CurrentVocalPitchHz => _currentVocalFreq;

        public float[] RawBuffer => _analysisBuffer;
        public float[] CleanedBuffer => _cleanedBuffer;
        public string ActiveDeviceName => _activeDeviceName;
        public bool IsRecordingActive => _micClip != null && Microphone.IsRecording(_activeDeviceName);
        public ChameleonSynthMode SynthMode { get => _synthMode; set => _synthMode = value; }
        public float MimicVolume { get => _mimicVolume; set => _mimicVolume = Mathf.Clamp01(value); }
        public float SilenceThreshold { get => _silenceThreshold; set => _silenceThreshold = Mathf.Clamp(value, 0.002f, 0.3f); }
        public float InputPreGain { get => _inputPreGain; set => _inputPreGain = Mathf.Clamp(value, 0.5f, 8.0f); }
        public bool AutoCalibrateClarity { get => _autoCalibrateClarity; set => _autoCalibrateClarity = value; }

        // Contrôles Basse
        public bool EmulateBass { get => _emulateBass; set => _emulateBass = value; }
        public ChameleonBassStyle BassStyle { get => _bassStyle; set => _bassStyle = value; }
        public bool BassRhythmicPluck { get => _bassRhythmicPluck; set => _bassRhythmicPluck = value; }
        public float BassMasterVolume { get => _bassMasterVolume; set => _bassMasterVolume = Mathf.Clamp(value, 0f, 3f); }
        public float BassDrive { get => _bassDrive; set => _bassDrive = Mathf.Clamp(value, 1f, 8f); }
        public float BassCutoff { get => _bassCutoff; set => _bassCutoff = Mathf.Clamp(value, 100f, 6000f); }
        public float BassResonance { get => _bassResonance; set => _bassResonance = Mathf.Clamp(value, 0.1f, 4.5f); }
        public int BassOctave { get => _bassOctave; set => _bassOctave = Mathf.Clamp(value, -1, 1); }
        public float BassSubCleanLayer { get => _bassSubCleanLayer; set => _bassSubCleanLayer = Mathf.Clamp01(value); }
        public float BassPunch { get => _bassPunch; set => _bassPunch = Mathf.Clamp01(value); }

        // Contrôles Chorale
        public bool EmulateChoir { get => _emulateChoir; set => _emulateChoir = value; }
        public ChameleonChoirStyle ChoirStyle { get => _choirStyle; set => _choirStyle = value; }
        public float ChoirMasterVolume { get => _choirMasterVolume; set => _choirMasterVolume = Mathf.Clamp(value, 0f, 3f); }
        public float ChoirDetune { get => _choirDetune; set => _choirDetune = Mathf.Clamp(value, 0.1f, 6.0f); }
        public float ChoirVibratoRate { get => _choirVibratoRate; set => _choirVibratoRate = Mathf.Clamp(value, 1f, 12f); }
        public float ChoirShimmer { get => _choirShimmer; set => _choirShimmer = Mathf.Clamp01(value); }
        public int ChoirOctave { get => _choirOctave; set => _choirOctave = Mathf.Clamp(value, -1, 2); }

        // Contrôles Voix Chantée
        public bool EmulateVocal { get => _emulateVocal; set => _emulateVocal = value; }
        public ChameleonVocalStyle VocalStyle { get => _vocalStyle; set => _vocalStyle = value; }
        public float VocalMasterVolume { get => _vocalMasterVolume; set => _vocalMasterVolume = Mathf.Clamp(value, 0f, 3f); }
        public float VocalVibratoRate { get => _vocalVibratoRate; set => _vocalVibratoRate = Mathf.Clamp(value, 1f, 10f); }
        public float VocalVibratoDepth { get => _vocalVibratoDepth; set => _vocalVibratoDepth = Mathf.Clamp01(value); }
        public float VocalBreathiness { get => _vocalBreathiness; set => _vocalBreathiness = Mathf.Clamp01(value); }
        public float VocalPortamento { get => _vocalPortamento; set => _vocalPortamento = Mathf.Clamp(value, 5f, 80f); }
        public int VocalOctave { get => _vocalOctave; set => _vocalOctave = Mathf.Clamp(value, -1, 2); }

        // Contrôles Drums
        public bool EmulateDrums { get => _emulateDrums; set => _emulateDrums = value; }
        public float DrumMasterVolume { get => _drumMasterVolume; set => _drumMasterVolume = Mathf.Clamp(value, 0f, 3f); }
        public float KickVolume { get => _kickVolume; set => _kickVolume = Mathf.Clamp(value, 0f, 3f); }
        public float SnareVolume { get => _snareVolume; set => _snareVolume = Mathf.Clamp(value, 0f, 3f); }
        public float HatVolume { get => _hatVolume; set => _hatVolume = Mathf.Clamp(value, 0f, 3f); }

        public bool UsePredictiveSync { get => _usePredictiveSync; set => _usePredictiveSync = value; }
        public float LatencyCompensationMs { get => _latencyCompensationMs; set => _latencyCompensationMs = Mathf.Clamp(value, 0f, 300f); }

        // Routage PipeWire
        public class MediaAppInfo
        {
            public string DisplayName;
            public string NodeName;
            public readonly List<string> Ports = new();
        }

        public string LinkedLinuxApp { get; private set; }
        public string LinkStatusMessage { get; private set; }
        public readonly List<MediaAppInfo> DetectedMediaApps = new();

        // DSP Overlay
        private double _phaseLeadSub;
        private double _phaseHarm1;
        private double _phaseHarm2;
        private float _currentSynthFreq = 110f;
        private float _targetSynthFreq = 110f;
        private float _synthFilterCutoff = 800f;
        private float _leadFilterLp;
        private float _leadFilterBp;
        private float _freezeDetectionTimer;

        // DSP Basse Dédiée
        private double _bassPhase;
        private double _bassSubPhase;
        private double _bassPhaseDetune1;
        private double _bassPhaseDetune2;
        private double _bassPhaseDetune3;
        private float _currentBassFreq = 55f;
        private float _targetBassFreq = 55f;
        private float _bassAmpEnv = 0f;
        private float _bassFilterEnv = 0f;
        private float _bassPitchKickEnv = 0f;
        private float _bassLadder1;
        private float _bassLadder2;
        private float _bassLadder3;
        private float _bassLadder4;
        private double _bassNeuroLfoPhase;
        private int _lastMidiNoteTracked = -1;
        private volatile float _pendingBassTrigger = 0f;

        // DSP Chorale Dédiée
        private double _choirPhase1;
        private double _choirPhase2;
        private double _choirPhase3;
        private double _choirPhaseFifth;
        private double _choirVibratoPhase;
        private float _currentChoirFreq = 220f;
        private float _targetChoirFreq = 220f;
        private float _choirAmpEnv = 0f;
        private float _choirF1_Lp;
        private float _choirF1_Bp;
        private float _choirF2_Lp;
        private float _choirF2_Bp;
        private float _choirBreathNoiseState;
        private volatile float _pendingChoirTrigger = 0f;

        // DSP Voix Chantée Solo Dédiée
        private double _vocalPhase;
        private double _vocalVibratoPhase;
        private double _vocalMorphPhase;
        private float _currentVocalFreq = 330f;
        private float _targetVocalFreq = 330f;
        private float _vocalAmpEnv = 0f;
        private float _vocalNoteTimer = 0f;
        private float _vocalF1_Lp;
        private float _vocalF1_Bp;
        private float _vocalF2_Lp;
        private float _vocalF2_Bp;
        private float _vocalF3_Lp;
        private float _vocalF3_Bp;
        private float _vocalBreathState;
        private volatile float _pendingVocalTrigger = 0f;

        // Détection Percussive
        private float _prevBassEnergy;
        private float _prevMidEnergy;
        private float _prevHighEnergy;
        private float _lastKickTime;
        private float _lastSnareTime;
        private float _lastHatTime;
        private float _lastBeatTime;

        // Horloge Prédictive PLL
        private double _pllClockPhase;
        private float _pllPhaseErrorSmoothed;
        private readonly float[] _kickPatternMask = new float[16];
        private readonly float[] _snarePatternMask = new float[16];
        private readonly float[] _hatPatternMask = new float[16];
        private int _lastFiredPredictiveStep = -1;

        // Verrous Atomiques Thread-Safe
        private volatile float _pendingKickVel = 0f;
        private volatile float _pendingSnareToneVel = 0f;
        private volatile float _pendingSnareNoiseVel = 0f;
        private volatile float _pendingHatVel = 0f;

        // Moteur Percussif DSP
        private double _kickPhase;
        private float _kickEnv;
        private float _kickPitchEnv;
        private double _snareTonePhase1;
        private double _snareTonePhase2;
        private float _snareToneEnv;
        private float _snareNoiseEnv;
        private float _snareHpState;
        private float _hatEnv;
        private float _hatHpState;
        private uint _noiseState = 54321;
        private float _drumSidechainDuck;

        // États d'Auto-Calibration Dynamique
        private float _adaptiveNoiseFloor = 0.006f;
        private float _calibratedPitchCutoff = 420f;
        private float _harmonicPuritySmoothed = 0.85f;
        private float _adaptiveCenterClip = 0.18f;

        private static readonly string[] NoteNames = { "Do", "Do#", "Ré", "Mib", "Mi", "Fa", "Fa#", "Sol", "Sol#", "La", "Sib", "Si" };

        public bool IsActive
        {
            get => _enableChameleonMode;
            set
            {
                _enableChameleonMode = value;
                if (_enableChameleonMode) StartCapture();
                else StopCapture();
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            _audioSource = GetComponent<AudioSource>();
            _audioSource.loop = true;
            _audioSource.playOnAwake = false;
            _audioSource.spatialBlend = 0f;
        }

        private void Start()
        {
            OptimizeAudioConfiguration();
            if (_enableChameleonMode)
            {
                StartCapture();
            }
        }

        private static void OptimizeAudioConfiguration()
        {
            var config = AudioSettings.GetConfiguration();
            if (config.dspBufferSize > 256)
            {
                config.dspBufferSize = 256;
                AudioSettings.Reset(config);
            }
        }

        private void Update()
        {
            if (!_enableChameleonMode) return;

            AnalyzeIncomingAudio();
            UpdateHarmonicParameters();
        }

        private void OnDestroy()
        {
            StopCapture();
            if (Instance == this) Instance = null;
        }

        public void SelectDevice(string deviceName)
        {
            _activeDeviceName = deviceName;
            if (_enableChameleonMode)
            {
                StartCapture();
            }
        }

        public void StartCapture()
        {
            StopCapture();

            var devices = Microphone.devices;
            if (devices == null || devices.Length == 0)
            {
                Debug.LogWarning("[SystemAudioChameleon] Aucun périphérique audio détecté.");
                return;
            }

            if (string.IsNullOrEmpty(_activeDeviceName) || Array.IndexOf(devices, _activeDeviceName) < 0)
            {
                _activeDeviceName = FindBestLoopbackDevice() ?? devices[0];
            }

            try
            {
                _micClip = Microphone.Start(_activeDeviceName, true, ClipDurationSec, _sampleRate);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SystemAudioChameleon] Échec avec '{_activeDeviceName}', repli sur défaut : {ex.Message}");
                _activeDeviceName = null;
                _micClip = Microphone.Start(null, true, ClipDurationSec, _sampleRate);
            }

            if (_micClip == null)
            {
                Debug.LogError("[SystemAudioChameleon] Impossible d'initialiser le flux audio.");
                IsExternalMusicDetected = false;
                return;
            }

            if (_isolateByMutingGameMusic && KilltimeAudioManager.Instance != null)
            {
                _savedGameMusicVolume = KilltimeAudioManager.Instance.GetCategoryVolume(SoundCategory.Music);
                KilltimeAudioManager.Instance.SetCategoryVolume(SoundCategory.Music, 0f);
            }

            _audioSource.clip = _micClip;
            StopAllCoroutines();
            StartCoroutine(WaitForAudioStreamReady());
        }

        private System.Collections.IEnumerator WaitForAudioStreamReady()
        {
            float timeout = Time.realtimeSinceStartup + 3.0f;
            while (Microphone.GetPosition(_activeDeviceName) < BufferSize && Time.realtimeSinceStartup < timeout)
            {
                yield return null;
            }

            if (Microphone.IsRecording(_activeDeviceName))
            {
                _audioSource.Play();
                Debug.Log($"[SystemAudioChameleon] Écoute système engagée à {_sampleRate} Hz sur : '{_activeDeviceName ?? "Défaut Système"}'");

                if (Application.platform == RuntimePlatform.LinuxPlayer || Application.platform == RuntimePlatform.LinuxEditor)
                {
                    RefreshLinuxApps();
                    if (DetectedMediaApps.Count == 1)
                    {
                        LinkLinuxApp(DetectedMediaApps[0]);
                    }
                }
            }
            else
            {
                Debug.LogWarning("[SystemAudioChameleon] Le périphérique n'a pas pu démarrer.");
            }
        }

        public void StopCapture()
        {
            StopAllCoroutines();

            if (Microphone.IsRecording(_activeDeviceName))
            {
                Microphone.End(_activeDeviceName);
            }

            if (_audioSource != null && _audioSource.isPlaying)
            {
                _audioSource.Stop();
            }

            if (_savedGameMusicVolume >= 0f && KilltimeAudioManager.Instance != null)
            {
                KilltimeAudioManager.Instance.SetCategoryVolume(SoundCategory.Music, _savedGameMusicVolume);
                _savedGameMusicVolume = -1f;
            }

            IsExternalMusicDetected = false;
            IsHarmonicNoteStable = false;
            DetectedNoteName = "---";
            PitchConfidence = 0f;
            DetectedBassFrequencyHz = 0f;
            BassPitchConfidence = 0f;
            BeatPulse = 0f;
            KickPulse = 0f;
            SnarePulse = 0f;
            HatPulse = 0f;
            PhaseLockConfidence = 0f;
            _pendingKickVel = 0f;
            _pendingSnareToneVel = 0f;
            _pendingSnareNoiseVel = 0f;
            _pendingHatVel = 0f;
            _pendingBassTrigger = 0f;
            _bassAmpEnv = 0f;
            _bassFilterEnv = 0f;
            _bassPitchKickEnv = 0f;
            _pendingChoirTrigger = 0f;
            _choirAmpEnv = 0f;
            _pendingVocalTrigger = 0f;
            _vocalAmpEnv = 0f;
        }

        private string FindBestLoopbackDevice()
        {
            var devices = Microphone.devices;
            if (devices == null || devices.Length == 0) return null;

            for (int i = 0; i < devices.Length; i++)
            {
                string d = devices[i].ToLowerInvariant();
                if (d.Contains("monitor") || d.Contains("moniteur") || d.Contains("mixage") ||
                    d.Contains("stereo mix") || d.Contains("loopback") || d.Contains("what u hear") || d.Contains("cable"))
                {
                    return devices[i];
                }
            }

            return devices[0];
        }

        private void AnalyzeIncomingAudio()
        {
            if (_micClip == null || !Microphone.IsRecording(_activeDeviceName)) return;

            int micPos = Microphone.GetPosition(_activeDeviceName);
            if (micPos < 0) return;

            int totalSamples = _micClip.samples;
            int readStart = micPos - BufferSize;
            if (readStart < 0) readStart += totalSamples;

            if (readStart + BufferSize <= totalSamples)
            {
                _micClip.GetData(_analysisBuffer, readStart);
            }
            else
            {
                int firstChunk = totalSamples - readStart;
                float[] temp1 = new float[firstChunk];
                _micClip.GetData(temp1, readStart);
                Array.Copy(temp1, 0, _analysisBuffer, 0, firstChunk);

                int secondChunk = BufferSize - firstChunk;
                float[] temp2 = new float[secondChunk];
                _micClip.GetData(temp2, 0);
                Array.Copy(temp2, 0, _analysisBuffer, firstChunk, secondChunk);
            }

            // Étage 1 : Mesure Brute Pré-Gain & Boucle AGC
            float rawPeak = 0f;
            float rawSum = 0f;
            for (int i = 0; i < BufferSize; i++)
            {
                float absVal = Mathf.Abs(_analysisBuffer[i]);
                if (absVal > rawPeak) rawPeak = absVal;
                rawSum += _analysisBuffer[i] * _analysisBuffer[i];
            }
            float unamplifiedRMS = Mathf.Sqrt(rawSum / BufferSize);

            // AGC (Contrôle Automatique de Gain)
            if (_autoCalibrateClarity && unamplifiedRMS > 0.001f)
            {
                float targetGain = 0.22f / Mathf.Max(0.005f, unamplifiedRMS);
                targetGain = Mathf.Clamp(targetGain, 0.6f, 5.5f);

                if (rawPeak * _inputPreGain > 0.92f)
                {
                    _inputPreGain = Mathf.MoveTowards(_inputPreGain, 0.88f / Mathf.Max(0.01f, rawPeak), Time.unscaledDeltaTime * 6.0f);
                }
                else
                {
                    _inputPreGain = Mathf.MoveTowards(_inputPreGain, targetGain, Time.unscaledDeltaTime * 0.9f);
                }
            }

            // Application du Gain Normalisé
            for (int i = 0; i < BufferSize; i++)
            {
                _analysisBuffer[i] *= _inputPreGain;
            }
            RawInputRMS = unamplifiedRMS * _inputPreGain;

            AudioListener.GetOutputData(_unityOutputBuffer, 0);
            float unitySum = 0f;
            for (int i = 0; i < BufferSize; i++) unitySum += _unityOutputBuffer[i] * _unityOutputBuffer[i];
            UnityOutputRMS = Mathf.Sqrt(unitySum / BufferSize);

            if (UnityOutputRMS > 0.08f)
            {
                _freezeDetectionTimer = 0.18f;
            }
            if (_freezeDetectionTimer > 0f)
            {
                _freezeDetectionTimer -= Time.unscaledDeltaTime;
            }

            Array.Copy(_analysisBuffer, _cleanedBuffer, BufferSize);

            float sumCleaned = 0f;
            for (int i = 0; i < BufferSize; i++) sumCleaned += _cleanedBuffer[i] * _cleanedBuffer[i];
            DetectedEnergyRMS = Mathf.Sqrt(sumCleaned / BufferSize);

            ExtractSpectralBands(_cleanedBuffer);

            float tonalEnergy = BassEnergyRMS + MidEnergyRMS;
            float inharmonicEnergy = HighEnergyRMS * 1.85f;
            float rawPurity = tonalEnergy / (tonalEnergy + inharmonicEnergy + 0.0001f);
            _harmonicPuritySmoothed = Mathf.Lerp(_harmonicPuritySmoothed, rawPurity, Time.unscaledDeltaTime * 6.5f);
            HarmonicPurity = _harmonicPuritySmoothed;

            // Calibrage Adaptatif du Seuil de Silence
            if (_autoCalibrateClarity)
            {
                if (RawInputRMS < _adaptiveNoiseFloor * 1.5f || _adaptiveNoiseFloor < 0.001f)
                {
                    _adaptiveNoiseFloor = Mathf.MoveTowards(_adaptiveNoiseFloor, RawInputRMS, Time.unscaledDeltaTime * 0.5f);
                }
                else
                {
                    _adaptiveNoiseFloor = Mathf.MoveTowards(_adaptiveNoiseFloor, RawInputRMS * 0.2f, Time.unscaledDeltaTime * 0.05f);
                }
                _silenceThreshold = Mathf.Clamp(_adaptiveNoiseFloor * 1.6f + 0.006f, 0.006f, 0.035f);
            }

            if (DetectedEnergyRMS < _silenceThreshold)
            {
                IsExternalMusicDetected = false;
                IsHarmonicNoteStable = false;
                DetectedNoteName = "---";
                PitchConfidence = 0f;
                PhaseLockConfidence = Mathf.MoveTowards(PhaseLockConfidence, 0f, Time.unscaledDeltaTime * 0.5f);
                return;
            }

            IsExternalMusicDetected = true;

            DetectPercussiveElements();

            // Analyse Pitch Décimée x4 (12 kHz) avec Filtrage en Boucle Fermée & Écrêtage de Sondhi
            if (_freezeDetectionTimer <= 0f)
            {
                if (_autoCalibrateClarity)
                {
                    float targetCutoff;
                    if (IsHarmonicNoteStable && _targetSynthFreq >= 35f)
                    {
                        float harmonicSuppressionSpan = Mathf.Lerp(1.75f, 2.20f, 1f - _harmonicPuritySmoothed);
                        targetCutoff = Mathf.Clamp(_targetSynthFreq * harmonicSuppressionSpan, 160f, 720f);
                    }
                    else
                    {
                        float energyWeightedGuide = (BassEnergyRMS * 200f + MidEnergyRMS * 520f) / Mathf.Max(0.001f, tonalEnergy);
                        targetCutoff = Mathf.Clamp(energyWeightedGuide + 100f, 220f, 680f);
                    }

                    _adaptiveCenterClip = Mathf.Lerp(0.14f, 0.26f, 1f - _harmonicPuritySmoothed);

                    bool isPercussiveTransient = (KickPulse > 0.25f || SnarePulse > 0.25f);
                    float filterAgility = isPercussiveTransient ? 1.8f : 6.5f;
                    _calibratedPitchCutoff = Mathf.Lerp(_calibratedPitchCutoff, targetCutoff, Time.unscaledDeltaTime * filterAgility);
                }
                else
                {
                    _calibratedPitchCutoff = 420f;
                    _adaptiveCenterClip = 0f;
                }

                PrepareDecimatedPitchBuffer(_calibratedPitchCutoff, _adaptiveCenterClip);
                int effectivePitchSampleRate = _sampleRate / DecimationFactor;

                var (foundHz, confidence) = ComputeNSDFPitch(effectivePitchSampleRate);
                PitchConfidence = confidence;

                bool wasStable = IsHarmonicNoteStable;
                float enterThreshold = 0.32f;
                float exitThreshold = 0.22f;
                bool isNowStable = wasStable ? (confidence >= exitThreshold) : (confidence >= enterThreshold);

                // Analyse dédiée au registre grave (24–175 Hz) isolée des harmoniques supérieures
                PrepareBassBuffer();
                int effectiveBassSampleRate = _sampleRate / BassDecimation;
                var (bassHz, bassConf) = ComputeBassPitch(effectiveBassSampleRate);
                BassPitchConfidence = bassConf;

                if (bassConf >= 0.26f && bassHz >= 24f && bassHz <= 180f)
                {
                    DetectedBassFrequencyHz = bassHz;
                    float finalBassTarget = bassHz;
                    if (_bassOctave == -1) finalBassTarget *= 0.5f;
                    else if (_bassOctave == 1) finalBassTarget *= 2f;
                    _targetBassFreq = Mathf.Clamp(finalBassTarget, 24f, 260f);

                    float bassMidi = 69f + 12f * Mathf.Log(bassHz / 440f, 2f);
                    int nearestBassMidi = Mathf.RoundToInt(bassMidi);
                    if (nearestBassMidi != _lastMidiNoteTracked)
                    {
                        _lastMidiNoteTracked = nearestBassMidi;
                        _pendingBassTrigger = 1.0f;
                    }
                }

                if (isNowStable && foundHz >= 35f && foundHz <= 720f)
                {
                    IsHarmonicNoteStable = true;
                    DetectedFrequencyHz = foundHz;
                    _targetSynthFreq = foundHz;

                    if (bassConf < 0.26f)
                    {
                        float bassCalculated = foundHz;
                        while (bassCalculated > 130f) bassCalculated *= 0.5f;
                        while (bassCalculated < 32f) bassCalculated *= 2f;

                        if (_bassOctave == -1) bassCalculated *= 0.5f;
                        else if (_bassOctave == 1) bassCalculated *= 2f;

                        _targetBassFreq = Mathf.Clamp(bassCalculated, 24f, 260f);
                    }

                    float choirCalculated = foundHz;
                    while (choirCalculated < 130f) choirCalculated *= 2f;
                    while (choirCalculated > 520f) choirCalculated *= 0.5f;

                    if (_choirOctave == -1) choirCalculated *= 0.5f;
                    else if (_choirOctave == 1) choirCalculated *= 2f;
                    else if (_choirOctave == 2) choirCalculated *= 4f;

                    _targetChoirFreq = Mathf.Clamp(choirCalculated, 65f, 1600f);

                    float vocalCalculated = foundHz;
                    while (vocalCalculated < 165f) vocalCalculated *= 2f;
                    while (vocalCalculated > 660f) vocalCalculated *= 0.5f;

                    if (_vocalOctave == -1) vocalCalculated *= 0.5f;
                    else if (_vocalOctave == 1) vocalCalculated *= 2f;
                    else if (_vocalOctave == 2) vocalCalculated *= 4f;

                    _targetVocalFreq = Mathf.Clamp(vocalCalculated, 80f, 1800f);

                    float midiVal = 69f + 12f * Mathf.Log(foundHz / 440f, 2f);
                    int nearestMidi = Mathf.RoundToInt(midiVal);
                    DetectedCentsOffset = Mathf.RoundToInt((midiVal - nearestMidi) * 100f);

                    int noteIndex = (nearestMidi % 12 + 12) % 12;
                    int octave = (nearestMidi / 12) - 1;
                    string sign = DetectedCentsOffset >= 0 ? "+" : "";
                    DetectedNoteName = $"{NoteNames[noteIndex]}{octave} ({sign}{DetectedCentsOffset}¢)";
                }
                else
                {
                    IsHarmonicNoteStable = false;
                    if (confidence < 0.20f)
                    {
                        DetectedNoteName = "---";
                    }
                }
            }
        }

        private void PrepareBassBuffer()
        {
            float alphaLP = 1f - Mathf.Exp(-2f * Mathf.PI * 160f / _sampleRate);
            float rHP = Mathf.Exp(-2f * Mathf.PI * 22f / _sampleRate);
            float lp1 = 0f, lp2 = 0f, lp3 = 0f, lp4 = 0f;
            float hp = 0f, prevIn = 0f;
            float peak = 0.0001f;

            for (int i = 0; i < BassBufferSize; i++)
            {
                float sum = 0f;
                int baseIdx = i * BassDecimation;
                for (int k = 0; k < BassDecimation; k++)
                {
                    float s = _cleanedBuffer[baseIdx + k];

                    float hpOut = s - prevIn + rHP * hp;
                    prevIn = s;
                    hp = hpOut;

                    lp1 += alphaLP * (hpOut - lp1);
                    lp2 += alphaLP * (lp1 - lp2);
                    lp3 += alphaLP * (lp2 - lp3);
                    lp4 += alphaLP * (lp3 - lp4);

                    sum += lp4;
                }

                float sample = sum / BassDecimation;
                _bassDecimatedBuffer[i] = sample;
                float absVal = Mathf.Abs(sample);
                if (absVal > peak) peak = absVal;
            }

            if (peak > 0.001f)
            {
                float clipLevel = peak * 0.18f;
                for (int i = 0; i < BassBufferSize; i++)
                {
                    float v = _bassDecimatedBuffer[i];
                    if (v > clipLevel) _bassDecimatedBuffer[i] = v - clipLevel;
                    else if (v < -clipLevel) _bassDecimatedBuffer[i] = v + clipLevel;
                    else _bassDecimatedBuffer[i] = 0f;
                }
            }
        }

        private (float frequency, float confidence) ComputeBassPitch(int sampleRate)
        {
            int minLag = Math.Max(2, sampleRate / 180);
            int maxLag = Math.Min(sampleRate / 24, BassBufferSize - 1);

            for (int tau = 0; tau <= maxLag; tau++)
            {
                float r = 0f;
                float m = 0f;
                int maxJ = BassBufferSize - tau;
                for (int j = 0; j < maxJ; j++)
                {
                    float a = _bassDecimatedBuffer[j];
                    float b = _bassDecimatedBuffer[j + tau];
                    r += a * b;
                    m += a * a + b * b;
                }
                _bassNsdfBuffer[tau] = (m > 0.00001f) ? (2f * r / m) : 0f;
            }

            int bestLag = -1;
            float maxPeakVal = 0f;

            for (int tau = minLag; tau < maxLag - 1; tau++)
            {
                if (_bassNsdfBuffer[tau] > _bassNsdfBuffer[tau - 1] && _bassNsdfBuffer[tau] >= _bassNsdfBuffer[tau + 1])
                {
                    if (_bassNsdfBuffer[tau] > maxPeakVal)
                    {
                        maxPeakVal = _bassNsdfBuffer[tau];
                        bestLag = tau;
                    }
                }
            }

            if (maxPeakVal >= 0.26f && bestLag > 0)
            {
                int subLagCandidate = bestLag * 2;
                int searchMargin = Mathf.Max(2, (int)(bestLag * 0.12f));
                int subBestLag = -1;
                float subMaxVal = 0f;

                int sMin = Math.Max(minLag, subLagCandidate - searchMargin);
                int sMax = Math.Min(maxLag - 1, subLagCandidate + searchMargin);

                for (int tau = sMin; tau <= sMax; tau++)
                {
                    if (_bassNsdfBuffer[tau] > _bassNsdfBuffer[tau - 1] && _bassNsdfBuffer[tau] >= _bassNsdfBuffer[tau + 1])
                    {
                        if (_bassNsdfBuffer[tau] > subMaxVal)
                        {
                            subMaxVal = _bassNsdfBuffer[tau];
                            subBestLag = tau;
                        }
                    }
                }

                if (subBestLag > 0 && subMaxVal >= maxPeakVal * 0.58f && subMaxVal >= 0.22f)
                {
                    bestLag = subBestLag;
                    maxPeakVal = subMaxVal;
                }

                float alpha = _bassNsdfBuffer[bestLag - 1];
                float beta = _bassNsdfBuffer[bestLag];
                float gamma = _bassNsdfBuffer[bestLag + 1];
                float denom = alpha - 2f * beta + gamma;
                float delta = Mathf.Abs(denom) > 0.00001f ? (alpha - gamma) / (2f * denom) : 0f;
                delta = Mathf.Clamp(delta, -0.5f, 0.5f);

                float exactLag = bestLag + delta;
                float hz = (float)sampleRate / exactLag;
                return (hz, Mathf.Clamp01(maxPeakVal));
            }

            return (0f, 0f);
        }

        private void PrepareDecimatedPitchBuffer(float cutoffHz, float centerClipFactor)
        {
            float lpState1 = 0f;
            float lpState2 = 0f;
            float alphaLP = 1f - Mathf.Exp(-2f * Mathf.PI * cutoffHz / _sampleRate);

            float hpState = 0f;
            float prevInput = 0f;
            float rHP = Mathf.Exp(-2f * Mathf.PI * 35f / _sampleRate);

            float peak = 0.0001f;

            for (int i = 0; i < DecimatedSize; i++)
            {
                float sum = 0f;
                int baseIdx = i * DecimationFactor;
                for (int k = 0; k < DecimationFactor; k++)
                {
                    float s = _cleanedBuffer[baseIdx + k];

                    float hpOut = s - prevInput + rHP * hpState;
                    prevInput = s;
                    hpState = hpOut;

                    lpState1 += alphaLP * (hpOut - lpState1);
                    lpState2 += alphaLP * (lpState1 - lpState2);
                    sum += lpState2;
                }

                float sample = sum / DecimationFactor;
                _decimatedPitchBuffer[i] = sample;
                float absVal = Mathf.Abs(sample);
                if (absVal > peak) peak = absVal;
            }

            if (centerClipFactor > 0.01f && peak > 0.001f)
            {
                float clipLevel = peak * centerClipFactor;
                for (int i = 0; i < DecimatedSize; i++)
                {
                    float v = _decimatedPitchBuffer[i];
                    if (v > clipLevel) _decimatedPitchBuffer[i] = v - clipLevel;
                    else if (v < -clipLevel) _decimatedPitchBuffer[i] = v + clipLevel;
                    else _decimatedPitchBuffer[i] = 0f;
                }
            }
        }

        private void DetectPercussiveElements()
        {
            float now = Time.unscaledTime;

            float bassFlux = Mathf.Max(0f, BassEnergyRMS - _prevBassEnergy);
            float midFlux = Mathf.Max(0f, MidEnergyRMS - _prevMidEnergy);
            float highFlux = Mathf.Max(0f, HighEnergyRMS - _prevHighEnergy);

            _prevBassEnergy = BassEnergyRMS;
            _prevMidEnergy = MidEnergyRMS;
            _prevHighEnergy = HighEnergyRMS;

            float kickThreshold = Mathf.Max(0.012f, _silenceThreshold * 1.15f);
            float snareThreshold = Mathf.Max(0.016f, _silenceThreshold * 1.35f);
            float hatThreshold = Mathf.Max(0.007f, _silenceThreshold * 0.70f);

            bool kickDetected = false;
            int current16th = Mathf.Clamp((int)(_pllClockPhase * 16.0), 0, 15);

            // 1. Kick & Impulsion Basse
            if (bassFlux > kickThreshold && (now - _lastKickTime) > 0.15f && bassFlux > highFlux * 1.10f)
            {
                _lastKickTime = now;
                KickPulse = 1.0f;
                BeatPulse = 1.0f;
                kickDetected = true;

                _pendingBassTrigger = Mathf.Clamp(0.8f + (bassFlux * 12f), 0.8f, 1.3f);

                double targetBeatPhase = 0.0;
                double phaseErr = (_pllClockPhase * 4.0) % 1.0 - targetBeatPhase;
                if (phaseErr > 0.5) phaseErr -= 1.0;
                if (phaseErr < -0.5) phaseErr += 1.0;
                _pllPhaseErrorSmoothed = Mathf.Lerp(_pllPhaseErrorSmoothed, (float)phaseErr, 0.25f);
                _pllClockPhase -= (_pllPhaseErrorSmoothed * 0.25f) / 4.0;

                PhaseLockConfidence = Mathf.Clamp01(PhaseLockConfidence + 0.15f);
                _kickPatternMask[current16th] = 1.0f;

                float solidKickVel = Mathf.Clamp(0.80f + (bassFlux * 12f), 0.80f, 1.35f);
                if (!_usePredictiveSync || PhaseLockConfidence < 0.35f)
                {
                    _pendingKickVel = solidKickVel;
                }

                float interval = now - _lastBeatTime;
                _lastBeatTime = now;
                if (interval >= 0.26f && interval <= 1.5f)
                {
                    float instantBpm = 60f / interval;
                    while (instantBpm < 75f) instantBpm *= 2f;
                    while (instantBpm > 175f) instantBpm *= 0.5f;
                    DetectedBPM = Mathf.Lerp(DetectedBPM, instantBpm, 0.25f);
                }
            }
            else if (bassFlux > kickThreshold * 0.7f && (now - _lastKickTime) > 0.10f)
            {
                _pendingBassTrigger = 0.9f;
            }

            // 2. Snare
            if (!kickDetected && midFlux > snareThreshold && (now - _lastSnareTime) > 0.16f && highFlux > 0.006f)
            {
                _lastSnareTime = now;
                SnarePulse = 1.0f;
                BeatPulse = Mathf.Max(BeatPulse, 0.75f);
                _snarePatternMask[current16th] = 1.0f;

                float solidSnareVel = Mathf.Clamp(0.75f + (midFlux * 10f), 0.75f, 1.25f);
                if (!_usePredictiveSync || PhaseLockConfidence < 0.35f)
                {
                    _pendingSnareToneVel = solidSnareVel;
                    _pendingSnareNoiseVel = solidSnareVel * 1.15f;
                }
            }

            // 3. Hi-Hat
            if (highFlux > hatThreshold && (now - _lastHatTime) > 0.055f && bassFlux < highFlux * 1.6f)
            {
                _lastHatTime = now;
                HatPulse = 1.0f;
                _hatPatternMask[current16th] = 1.0f;

                float solidHatVel = Mathf.Clamp(0.65f + (highFlux * 15f), 0.65f, 1.15f);
                if (!_usePredictiveSync || PhaseLockConfidence < 0.35f)
                {
                    _pendingHatVel = solidHatVel;
                }
            }

            for (int i = 0; i < 16; i++)
            {
                _kickPatternMask[i] = Mathf.MoveTowards(_kickPatternMask[i], 0f, Time.unscaledDeltaTime * 0.20f);
                _snarePatternMask[i] = Mathf.MoveTowards(_snarePatternMask[i], 0f, Time.unscaledDeltaTime * 0.20f);
                _hatPatternMask[i] = Mathf.MoveTowards(_hatPatternMask[i], 0f, Time.unscaledDeltaTime * 0.30f);
            }
        }

        public void TestTriggerKick()
        {
            KickPulse = 1.0f;
            BeatPulse = 1.0f;
            _pendingKickVel = 1.0f;
        }

        public void TestTriggerSnare()
        {
            SnarePulse = 1.0f;
            _pendingSnareToneVel = 0.9f;
            _pendingSnareNoiseVel = 1.1f;
        }

        public void TestTriggerHat()
        {
            HatPulse = 1.0f;
            _pendingHatVel = 0.85f;
        }

        public void TestTriggerBass()
        {
            _pendingBassTrigger = 1.2f;
        }

        public void TestTriggerChoir()
        {
            _pendingChoirTrigger = 1.15f;
        }

        public void TestTriggerVocal()
        {
            _pendingVocalTrigger = 1.25f;
            _vocalNoteTimer = 0f;
        }

        private void ExtractSpectralBands(float[] buffer)
        {
            float bassSum = 0f;
            float midSum = 0f;
            float highSum = 0f;
            float weightedFreqSum = 0f;
            float totalPower = 0.0001f;

            float prevSample = 0f;
            float lpBass = 0f;
            float lpMid = 0f;

            for (int i = 0; i < buffer.Length; i++)
            {
                float x = buffer[i];

                lpBass += 0.035f * (x - lpBass);
                bassSum += lpBass * lpBass;

                lpMid += 0.28f * (x - lpMid);
                float midPart = lpMid - lpBass;
                midSum += midPart * midPart;

                float hpHigh = x - lpMid;
                highSum += hpHigh * hpHigh;

                float diff = x - prevSample;
                prevSample = x;
                float power = x * x;
                totalPower += power;
                weightedFreqSum += Mathf.Abs(diff) * power;
            }

            BassEnergyRMS = Mathf.Sqrt(bassSum / buffer.Length);
            MidEnergyRMS = Mathf.Sqrt(midSum / buffer.Length);
            HighEnergyRMS = Mathf.Sqrt(highSum / buffer.Length);

            SpectralCentroidHz = Mathf.Clamp((weightedFreqSum / totalPower) * (_sampleRate * 0.5f) * 0.5f, 60f, 10000f);
        }

        private (float frequency, float confidence) ComputeNSDFPitch(int sampleRate)
        {
            int minLag = sampleRate / 720;
            int maxLag = Math.Min(sampleRate / 35, DecimatedSize - 1);

            for (int tau = 0; tau <= maxLag; tau++)
            {
                float r = 0f;
                float m = 0f;
                int maxJ = DecimatedSize - tau;
                for (int j = 0; j < maxJ; j++)
                {
                    float a = _decimatedPitchBuffer[j];
                    float b = _decimatedPitchBuffer[j + tau];
                    r += a * b;
                    m += a * a + b * b;
                }
                _nsdfBuffer[tau] = (m > 0.00001f) ? (2f * r / m) : 0f;
            }

            float maxPeakValue = 0f;
            bool isPositive = false;

            for (int tau = 1; tau < maxLag - 1; tau++)
            {
                if (!isPositive && _nsdfBuffer[tau] > 0f) isPositive = true;
                if (isPositive && _nsdfBuffer[tau] <= 0f) isPositive = false;

                if (isPositive && tau >= minLag)
                {
                    if (_nsdfBuffer[tau] > _nsdfBuffer[tau - 1] && _nsdfBuffer[tau] >= _nsdfBuffer[tau + 1])
                    {
                        if (_nsdfBuffer[tau] > maxPeakValue)
                        {
                            maxPeakValue = _nsdfBuffer[tau];
                        }
                    }
                }
            }

            if (maxPeakValue < 0.25f) return (0f, 0f);

            float threshold = maxPeakValue * 0.80f;
            int chosenPeakIndex = -1;
            isPositive = false;

            for (int tau = 1; tau < maxLag - 1; tau++)
            {
                if (!isPositive && _nsdfBuffer[tau] > 0f) isPositive = true;
                if (isPositive && _nsdfBuffer[tau] <= 0f) isPositive = false;

                if (isPositive && tau >= minLag)
                {
                    if (_nsdfBuffer[tau] > _nsdfBuffer[tau - 1] && _nsdfBuffer[tau] >= _nsdfBuffer[tau + 1])
                    {
                        if (_nsdfBuffer[tau] >= threshold)
                        {
                            chosenPeakIndex = tau;
                            break;
                        }
                    }
                }
            }

            if (chosenPeakIndex > 0)
            {
                float alpha = _nsdfBuffer[chosenPeakIndex - 1];
                float beta = _nsdfBuffer[chosenPeakIndex];
                float gamma = _nsdfBuffer[chosenPeakIndex + 1];

                float denom = alpha - 2f * beta + gamma;
                float delta = Mathf.Abs(denom) > 0.00001f ? (alpha - gamma) / (2f * denom) : 0f;
                delta = Mathf.Clamp(delta, -0.5f, 0.5f);

                float exactLag = chosenPeakIndex + delta;
                float hz = (float)sampleRate / exactLag;
                return (hz, Mathf.Clamp01(beta));
            }

            return (0f, 0f);
        }

        private void UpdateHarmonicParameters()
        {
            float dt = Time.unscaledDeltaTime;
            BeatPulse = Mathf.MoveTowards(BeatPulse, 0f, dt * 4.2f);
            KickPulse = Mathf.MoveTowards(KickPulse, 0f, dt * 5.5f);
            SnarePulse = Mathf.MoveTowards(SnarePulse, 0f, dt * 5.0f);
            HatPulse = Mathf.MoveTowards(HatPulse, 0f, dt * 8.0f);
        }

        private float NextFastNoise()
        {
            _noiseState = _noiseState * 1664525u + 1013904223u;
            return (_noiseState / 2147483648.0f) - 1.0f;
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (!_enableChameleonMode || !IsExternalMusicDetected || !_generateCyberneticOverlay)
            {
                Array.Clear(data, 0, data.Length);
                return;
            }

            if (_pendingKickVel > 0f)
            {
                _kickEnv = _pendingKickVel;
                _kickPitchEnv = 1.0f;
                _kickPhase = 0.0;
                _drumSidechainDuck = 1.0f;
                _pendingKickVel = 0f;
            }

            if (_pendingSnareToneVel > 0f)
            {
                _snareToneEnv = _pendingSnareToneVel;
                _snareNoiseEnv = _pendingSnareNoiseVel;
                _snareTonePhase1 = 0.0;
                _snareTonePhase2 = 0.0;
                _pendingSnareToneVel = 0f;
                _pendingSnareNoiseVel = 0f;
            }

            if (_pendingHatVel > 0f)
            {
                _hatEnv = _pendingHatVel;
                _pendingHatVel = 0f;
            }

            if (_pendingBassTrigger > 0f)
            {
                _bassFilterEnv = 1.0f;
                _bassPitchKickEnv = 1.0f;
                _bassAmpEnv = Mathf.Max(_bassAmpEnv, _pendingBassTrigger);
                _pendingBassTrigger = 0f;
            }

            if (_pendingChoirTrigger > 0f)
            {
                _choirAmpEnv = Mathf.Max(_choirAmpEnv, _pendingChoirTrigger);
                _pendingChoirTrigger = 0f;
            }

            if (_pendingVocalTrigger > 0f)
            {
                _vocalAmpEnv = Mathf.Max(_vocalAmpEnv, _pendingVocalTrigger);
                _vocalNoteTimer = 0f;
                _pendingVocalTrigger = 0f;
            }

            double sampleRate = AudioSettings.outputSampleRate;
            float dt = 1f / (float)sampleRate;

            float kickDecay = Mathf.Exp(-9.0f * dt);
            float kickPitchDecay = Mathf.Exp(-32.0f * dt);
            float snareToneDecay = Mathf.Exp(-16.0f * dt);
            float snareNoiseDecay = Mathf.Exp(-8.5f * dt);
            float hatDecay = Mathf.Exp(-38.0f * dt);
            float duckDecay = Mathf.Exp(-14.0f * dt);

            float bassFilterDecay = Mathf.Exp(-7.5f * dt);
            float bassAmpDecay = _bassRhythmicPluck ? Mathf.Exp(-4.8f * dt) : Mathf.Exp(-0.8f * dt);
            float bassPitchKickDecay = Mathf.Exp(-38.0f * dt);
            float choirAttack = Mathf.Clamp01(12.0f * dt);
            float choirRelease = Mathf.Exp(-3.5f * dt);
            float vocalAttack = Mathf.Clamp01(18.0f * dt);
            float vocalRelease = Mathf.Exp(-4.2f * dt);
            float vocalGlide = Mathf.Clamp01(_vocalPortamento * dt);

            float glidePerSample = Mathf.Clamp01(_portamentoSpeed * dt);

            float targetLeadFreq = _targetSynthFreq;
            while (targetLeadFreq > 140f) targetLeadFreq *= 0.5f;
            while (targetLeadFreq < 45f) targetLeadFreq *= 2f;

            float synthGain = Mathf.Clamp01(DetectedEnergyRMS * 3.8f) * _mimicVolume;
            if (!IsHarmonicNoteStable) synthGain *= 0.30f;

            float targetCutoff = Mathf.Clamp(SpectralCentroidHz * 0.8f + (BeatPulse * 1200f), 250f, 6500f);

            double beatsPerSec = (DetectedBPM / 60.0);
            double phaseIncPerSample = (beatsPerSec / 4.0) / sampleRate;
            double latencyCompCycles = (_latencyCompensationMs / 1000.0) * beatsPerSec / 4.0;

            for (int i = 0; i < data.Length; i += channels)
            {
                _pllClockPhase += phaseIncPerSample;
                if (_pllClockPhase >= 1.0) _pllClockPhase -= 1.0;

                _bassFilterEnv *= bassFilterDecay;
                _bassAmpEnv *= bassAmpDecay;
                _bassPitchKickEnv *= bassPitchKickDecay;

                if (_emulateDrums && _usePredictiveSync && PhaseLockConfidence >= 0.35f)
                {
                    double lookahead = _pllClockPhase + latencyCompCycles;
                    if (lookahead >= 1.0) lookahead -= 1.0;

                    int step16 = Mathf.Clamp((int)(lookahead * 16.0), 0, 15);
                    if (step16 != _lastFiredPredictiveStep)
                    {
                        _lastFiredPredictiveStep = step16;

                        if (_kickPatternMask[step16] > 0.35f)
                        {
                            _kickEnv = _kickPatternMask[step16] * 1.0f;
                            _kickPitchEnv = 1.0f;
                            _kickPhase = 0.0;
                            _drumSidechainDuck = 1.0f;
                            _bassFilterEnv = 1.0f;
                            _bassPitchKickEnv = 1.0f;
                            _bassAmpEnv = 1.0f;
                        }

                        if (_snarePatternMask[step16] > 0.35f)
                        {
                            _snareToneEnv = _snarePatternMask[step16] * 0.90f;
                            _snareNoiseEnv = _snarePatternMask[step16] * 1.10f;
                            _snareTonePhase1 = 0.0;
                            _snareTonePhase2 = 0.0;
                        }

                        if (_hatPatternMask[step16] > 0.25f)
                        {
                            _hatEnv = _hatPatternMask[step16] * 0.85f;
                        }
                    }
                }

                // 1. Émulateur Basse Dédié (Stéréo & 4-Pole Ladder)
                float bassSampleL = 0f;
                float bassSampleR = 0f;

                if (_emulateBass)
                {
                    _currentBassFreq += (_targetBassFreq - _currentBassFreq) * (glidePerSample * 0.85f);
                    float pitchedBassFreq = _currentBassFreq * (1.0f + (_bassPunch * 0.85f * _bassPitchKickEnv));

                    _bassSubPhase += (2.0 * Math.PI * _currentBassFreq) / sampleRate;
                    if (_bassSubPhase > Math.PI * 2) _bassSubPhase -= Math.PI * 2;
                    float cleanSubSine = (float)Math.Sin(_bassSubPhase);

                    double incBass = (2.0 * Math.PI * pitchedBassFreq) / sampleRate;
                    _bassPhase += incBass;
                    if (_bassPhase > Math.PI * 2) _bassPhase -= Math.PI * 2;

                    float processedBassL = 0f;
                    float processedBassR = 0f;

                    if (_bassStyle == ChameleonBassStyle.Acid303)
                    {
                        float saw = (float)(2.0 * ((_bassPhase / (Math.PI * 2)) % 1.0) - 1.0);
                        float sqr = Mathf.Sign((float)Math.Sin(_bassPhase)) * 0.45f;
                        float rawBass = saw * 0.75f + sqr * 0.25f;

                        float acidCutoff = Mathf.Clamp(_bassCutoff * (0.20f + 2.8f * _bassFilterEnv), 45f, 13500f);
                        float fLadder = 2f * Mathf.Sin(Mathf.PI * Mathf.Clamp(acidCutoff / (float)sampleRate, 0.002f, 0.45f));
                        float res = Mathf.Clamp(_bassResonance * 1.05f, 0.1f, 3.85f);

                        float inputWithFeedback = rawBass - (res * _bassLadder4);
                        inputWithFeedback = (float)Math.Tanh(inputWithFeedback * 1.15f);

                        _bassLadder1 += fLadder * (inputWithFeedback - _bassLadder1);
                        _bassLadder2 += fLadder * (_bassLadder1 - _bassLadder2);
                        _bassLadder3 += fLadder * (_bassLadder2 - _bassLadder3);
                        _bassLadder4 += fLadder * (_bassLadder3 - _bassLadder4);

                        float ladderOut = _bassLadder4 * (1.0f + res * 0.42f);
                        float driven = ladderOut * _bassDrive;
                        float saturated = (float)Math.Tanh(driven + (driven * driven * 0.18f * Mathf.Sign(driven)));

                        processedBassL = saturated;
                        processedBassR = saturated;
                    }
                    else if (_bassStyle == ChameleonBassStyle.ReeseCyber)
                    {
                        _bassPhaseDetune1 += (2.0 * Math.PI * (pitchedBassFreq * 0.991)) / sampleRate;
                        _bassPhaseDetune2 += (2.0 * Math.PI * (pitchedBassFreq * 1.009)) / sampleRate;
                        _bassPhaseDetune3 += (2.0 * Math.PI * (pitchedBassFreq * 1.018)) / sampleRate;
                        if (_bassPhaseDetune1 > Math.PI * 2) _bassPhaseDetune1 -= Math.PI * 2;
                        if (_bassPhaseDetune2 > Math.PI * 2) _bassPhaseDetune2 -= Math.PI * 2;
                        if (_bassPhaseDetune3 > Math.PI * 2) _bassPhaseDetune3 -= Math.PI * 2;

                        float sawCenter = (float)(2.0 * ((_bassPhase / (Math.PI * 2)) % 1.0) - 1.0);
                        float sawLeft = (float)(2.0 * ((_bassPhaseDetune1 / (Math.PI * 2)) % 1.0) - 1.0);
                        float sawRight = (float)(2.0 * ((_bassPhaseDetune2 / (Math.PI * 2)) % 1.0) - 1.0);
                        float sawSpread = (float)(2.0 * ((_bassPhaseDetune3 / (Math.PI * 2)) % 1.0) - 1.0);

                        float rawL = sawCenter * 0.40f + sawLeft * 0.60f;
                        float rawR = sawCenter * 0.40f + sawRight * 0.45f + sawSpread * 0.25f;

                        float reeseCut = Mathf.Clamp(_bassCutoff * (0.45f + 1.4f * _bassFilterEnv), 90f, 9500f);
                        float fReese = 2f * Mathf.Sin(Mathf.PI * Mathf.Clamp(reeseCut / (float)sampleRate, 0.005f, 0.45f));

                        _bassLadder1 += fReese * (rawL - _bassLadder1);
                        _bassLadder2 += fReese * (_bassLadder1 - _bassLadder2);
                        _bassLadder3 += fReese * (rawR - _bassLadder3);
                        _bassLadder4 += fReese * (_bassLadder3 - _bassLadder4);

                        processedBassL = (float)Math.Tanh(_bassLadder2 * (_bassDrive * 0.90f));
                        processedBassR = (float)Math.Tanh(_bassLadder4 * (_bassDrive * 0.90f));
                    }
                    else if (_bassStyle == ChameleonBassStyle.DeepSub808)
                    {
                        float mainSine = (float)Math.Sin(_bassPhase);
                        float secondHarm = (float)Math.Sin(_bassPhase * 2.0) * (0.22f + 0.35f * _bassFilterEnv);
                        float thirdHarm = (float)Math.Sin(_bassPhase * 3.0) * 0.08f;

                        float composite = mainSine + secondHarm + thirdHarm;
                        float driven = composite * _bassDrive;
                        float warmTube = (float)Math.Tanh(driven + (driven * driven * 0.22f));

                        processedBassL = warmTube * 0.96f;
                        processedBassR = warmTube * 0.96f;
                    }
                    else if (_bassStyle == ChameleonBassStyle.NeuroMod)
                    {
                        _bassNeuroLfoPhase += (2.0 * Math.PI * 2.6) / sampleRate;
                        if (_bassNeuroLfoPhase > Math.PI * 2) _bassNeuroLfoPhase -= Math.PI * 2;
                        float lfoMod = 0.5f + 0.5f * (float)Math.Sin(_bassNeuroLfoPhase);

                        float saw = (float)(2.0 * ((_bassPhase / (Math.PI * 2)) % 1.0) - 1.0);
                        float folded = (float)Math.Sin(saw * (_bassDrive * 2.8f + lfoMod * 2.0f));

                        float neuroCutL = Mathf.Clamp(_bassCutoff * (0.35f + 1.8f * lfoMod), 120f, 7500f);
                        float neuroCutR = Mathf.Clamp(_bassCutoff * (0.45f + 1.6f * (1f - lfoMod)), 120f, 7500f);
                        float fL = 2f * Mathf.Sin(Mathf.PI * Mathf.Clamp(neuroCutL / (float)sampleRate, 0.005f, 0.45f));
                        float fR = 2f * Mathf.Sin(Mathf.PI * Mathf.Clamp(neuroCutR / (float)sampleRate, 0.005f, 0.45f));

                        _bassLadder1 += fL * (folded - _bassLadder1);
                        _bassLadder2 += fL * (_bassLadder1 - _bassLadder2);
                        _bassLadder3 += fR * (folded - _bassLadder3);
                        _bassLadder4 += fR * (_bassLadder3 - _bassLadder4);

                        processedBassL = (float)Math.Tanh(_bassLadder2 * 1.35f);
                        processedBassR = (float)Math.Tanh(_bassLadder4 * 1.35f);
                    }

                    float subLayerWeight = _bassSubCleanLayer * 0.75f;
                    float harmonicWeight = 1.0f - (subLayerWeight * 0.40f);

                    bassSampleL = (processedBassL * harmonicWeight) + (cleanSubSine * subLayerWeight);
                    bassSampleR = (processedBassR * harmonicWeight) + (cleanSubSine * subLayerWeight);

                    float targetEnergy = Mathf.Clamp01(BassEnergyRMS * 3.8f);
                    float dynamicAmp = _bassRhythmicPluck ? _bassAmpEnv : Mathf.Max(_bassAmpEnv * 0.6f, targetEnergy);
                    float bassDuck = 1.0f - (_drumSidechainDuck * _bassSidechainDuck);

                    float totalGain = dynamicAmp * _bassMasterVolume * bassDuck;
                    bassSampleL *= totalGain;
                    bassSampleR *= totalGain;
                }

                // 2. Synthétiseur Harmonique / Lead
                float synthSample = 0f;
                _currentSynthFreq += (targetLeadFreq - _currentSynthFreq) * glidePerSample;
                _synthFilterCutoff += (targetCutoff - _synthFilterCutoff) * (glidePerSample * 0.5f);

                double incLead = (2.0 * Math.PI * _currentSynthFreq) / sampleRate;
                _phaseLeadSub += incLead;
                _phaseHarm1 += incLead * 1.5;
                _phaseHarm2 += incLead * 2.0;

                if (_phaseLeadSub > Math.PI * 2) _phaseLeadSub -= Math.PI * 2;
                if (_phaseHarm1 > Math.PI * 2) _phaseHarm1 -= Math.PI * 2;
                if (_phaseHarm2 > Math.PI * 2) _phaseHarm2 -= Math.PI * 2;

                float subRaw = (float)Math.Sin(_phaseLeadSub);
                float subSaturated = (float)Math.Tanh(subRaw * 2.4) * 0.55f;
                float rawOsc = subSaturated;

                if (_synthMode != ChameleonSynthMode.SubBassOnly)
                {
                    float harm1 = (float)Math.Sin(_phaseHarm1) * 0.32f;
                    float sawHarm = (float)(2.0 * ((_phaseHarm2 / (Math.PI * 2)) % 1.0) - 1.0) * 0.18f;
                    rawOsc += harm1 + sawHarm;
                }

                if (_synthMode == ChameleonSynthMode.FullNytharite)
                {
                    float shimmer = (float)Math.Sin(_phaseLeadSub * 4.02) * (HighEnergyRMS * 1.5f) * 0.22f;
                    rawOsc += shimmer;
                }

                float fSv = 2f * Mathf.Sin(Mathf.PI * Mathf.Clamp(_synthFilterCutoff / (float)sampleRate, 0.005f, 0.45f));
                _leadFilterLp += fSv * _leadFilterBp;
                float highPassLead = rawOsc - _leadFilterLp - 0.82f * _leadFilterBp;
                _leadFilterBp += fSv * highPassLead;

                synthSample = _leadFilterLp * synthGain * (1.0f - (_drumSidechainDuck * 0.6f));

                // 3. Émulation Voix de Chorale (Choir Synth)
                float choirSample = 0f;
                if (_emulateChoir)
                {
                    _currentChoirFreq += (_targetChoirFreq - _currentChoirFreq) * (glidePerSample * 0.75f);

                    _choirVibratoPhase += (2.0 * Math.PI * _choirVibratoRate) / sampleRate;
                    if (_choirVibratoPhase > Math.PI * 2) _choirVibratoPhase -= Math.PI * 2;
                    float vibrato = (float)Math.Sin(_choirVibratoPhase) * 0.0055f;

                    float detuneOffset = _choirDetune * 0.003f;
                    double incC1 = (2.0 * Math.PI * (_currentChoirFreq * (1f + vibrato))) / sampleRate;
                    double incC2 = (2.0 * Math.PI * (_currentChoirFreq * (1f - detuneOffset + vibrato * 0.85f))) / sampleRate;
                    double incC3 = (2.0 * Math.PI * (_currentChoirFreq * (1f + detuneOffset + vibrato * 1.15f))) / sampleRate;
                    double incC5 = (2.0 * Math.PI * (_currentChoirFreq * 1.498307f * (1f + vibrato * 0.9f))) / sampleRate;

                    _choirPhase1 += incC1;
                    if (_choirPhase1 > Math.PI * 2) _choirPhase1 -= Math.PI * 2;
                    _choirPhase2 += incC2;
                    if (_choirPhase2 > Math.PI * 2) _choirPhase2 -= Math.PI * 2;
                    _choirPhase3 += incC3;
                    if (_choirPhase3 > Math.PI * 2) _choirPhase3 -= Math.PI * 2;
                    _choirPhaseFifth += incC5;
                    if (_choirPhaseFifth > Math.PI * 2) _choirPhaseFifth -= Math.PI * 2;

                    float voice1 = (float)(Math.Sin(_choirPhase1) + 0.45 * Math.Sin(_choirPhase1 * 2.0) + 0.22 * Math.Sin(_choirPhase1 * 3.0) + 0.10 * Math.Sin(_choirPhase1 * 4.0));
                    float voice2 = (float)(Math.Sin(_choirPhase2) + 0.40 * Math.Sin(_choirPhase2 * 2.0) + 0.18 * Math.Sin(_choirPhase2 * 3.0));
                    float voice3 = (float)(Math.Sin(_choirPhase3) + 0.40 * Math.Sin(_choirPhase3 * 2.0) + 0.18 * Math.Sin(_choirPhase3 * 3.0));
                    float rawChoir = (voice1 * 0.42f + voice2 * 0.29f + voice3 * 0.29f);

                    if (_choirStyle == ChameleonChoirStyle.ArcanotechHymn)
                    {
                        float voiceFifth = (float)(Math.Sin(_choirPhaseFifth) + 0.35 * Math.Sin(_choirPhaseFifth * 2.0));
                        rawChoir = (rawChoir * 0.72f) + (voiceFifth * 0.38f);
                    }

                    float f1Target = 750f;
                    float f2Target = 1250f;
                    float q1 = 0.24f;
                    float q2 = 0.20f;

                    if (_choirStyle == ChameleonChoirStyle.CyberOoh)
                    {
                        f1Target = 380f;
                        f2Target = 820f;
                        q1 = 0.20f;
                        q2 = 0.16f;
                    }
                    else if (_choirStyle == ChameleonChoirStyle.ArcanotechHymn)
                    {
                        f1Target = 620f;
                        f2Target = 1750f;
                        q1 = 0.22f;
                        q2 = 0.18f;
                    }

                    float fSv1 = 2f * Mathf.Sin(Mathf.PI * Mathf.Clamp(f1Target / (float)sampleRate, 0.005f, 0.45f));
                    _choirF1_Lp += fSv1 * _choirF1_Bp;
                    float hpF1 = rawChoir - _choirF1_Lp - q1 * _choirF1_Bp;
                    _choirF1_Bp += fSv1 * hpF1;

                    float fSv2 = 2f * Mathf.Sin(Mathf.PI * Mathf.Clamp(f2Target / (float)sampleRate, 0.005f, 0.45f));
                    _choirF2_Lp += fSv2 * _choirF2_Bp;
                    float hpF2 = rawChoir - _choirF2_Lp - q2 * _choirF2_Bp;
                    _choirF2_Bp += fSv2 * hpF2;

                    float vocalResonance = (_choirF1_Bp * 0.65f + _choirF2_Bp * 0.55f);

                    if (_choirShimmer > 0.01f)
                    {
                        float rawN = NextFastNoise();
                        _choirBreathNoiseState += 0.42f * (rawN - _choirBreathNoiseState);
                        float breath = (rawN - _choirBreathNoiseState) * _choirShimmer * 0.12f;
                        vocalResonance += breath;
                    }

                    float targetChoirAmp = IsHarmonicNoteStable
                        ? Mathf.Clamp01(DetectedEnergyRMS * 3.6f)
                        : (DetectedEnergyRMS > _silenceThreshold ? 0.25f : 0f);

                    if (targetChoirAmp > _choirAmpEnv)
                        _choirAmpEnv += (targetChoirAmp - _choirAmpEnv) * choirAttack;
                    else
                        _choirAmpEnv *= choirRelease;

                    float choirDuck = 1.0f - (_drumSidechainDuck * 0.42f);
                    choirSample = (float)Math.Tanh(vocalResonance * 1.4) * _choirAmpEnv * _choirMasterVolume * choirDuck;
                }

                // 3.c Émulateur Voix Chantée Solo (Singing Voice Synth)
                float vocalSample = 0f;
                if (_emulateVocal)
                {
                    _currentVocalFreq += (_targetVocalFreq - _currentVocalFreq) * vocalGlide;
                    _vocalNoteTimer += dt;

                    float vibratoDelayRamp = Mathf.Clamp01((_vocalNoteTimer - 0.18f) / 0.28f);
                    _vocalVibratoPhase += (2.0 * Math.PI * _vocalVibratoRate) / sampleRate;
                    if (_vocalVibratoPhase > Math.PI * 2) _vocalVibratoPhase -= Math.PI * 2;
                    float vocalPitchMod = (float)Math.Sin(_vocalVibratoPhase) * (_vocalVibratoDepth * 0.018f) * vibratoDelayRamp;

                    double incVocal = (2.0 * Math.PI * (_currentVocalFreq * (1f + vocalPitchMod))) / sampleRate;
                    _vocalPhase += incVocal;
                    if (_vocalPhase > Math.PI * 2) _vocalPhase -= Math.PI * 2;

                    double pNorm = (_vocalPhase / (Math.PI * 2.0)) % 1.0;
                    float glottalPulse;
                    if (pNorm < 0.58)
                    {
                        glottalPulse = (float)(0.5 * (1.0 - Math.Cos(Math.PI * pNorm / 0.58)));
                    }
                    else if (pNorm < 0.82)
                    {
                        glottalPulse = (float)Math.Cos(Math.PI * (pNorm - 0.58) / 0.48);
                    }
                    else
                    {
                        glottalPulse = 0f;
                    }
                    glottalPulse -= 0.32f;

                    if (_vocalBreathiness > 0.01f)
                    {
                        float breathNoise = NextFastNoise();
                        _vocalBreathState += 0.35f * (breathNoise - _vocalBreathState);
                        glottalPulse += (breathNoise - _vocalBreathState) * (_vocalBreathiness * 0.45f);
                    }

                    _vocalMorphPhase += (2.0 * Math.PI * 0.45) / sampleRate;
                    if (_vocalMorphPhase > Math.PI * 2) _vocalMorphPhase -= Math.PI * 2;
                    float morphLfo = 0.5f + 0.5f * (float)Math.Sin(_vocalMorphPhase);

                    float f1 = 800f, f2 = 1350f, f3 = 2650f;
                    float q1 = 0.18f, q2 = 0.15f, q3 = 0.12f;

                    if (_vocalStyle == ChameleonVocalStyle.CyberSoprano)
                    {
                        f1 = 920f;
                        f2 = 1680f;
                        f3 = 3100f;
                        q1 = 0.16f;
                        q2 = 0.13f;
                        q3 = 0.10f;
                    }
                    else if (_vocalStyle == ChameleonVocalStyle.AnalogTenor)
                    {
                        f1 = 540f;
                        f2 = 1150f;
                        f3 = 2450f;
                        q1 = 0.20f;
                        q2 = 0.16f;
                        q3 = 0.14f;
                    }
                    else if (_vocalStyle == ChameleonVocalStyle.VocaloidMorph)
                    {
                        f1 = Mathf.Lerp(420f, 850f, morphLfo);
                        f2 = Mathf.Lerp(2100f, 1200f, morphLfo);
                        f3 = Mathf.Lerp(2900f, 2500f, morphLfo);
                        q1 = 0.15f;
                        q2 = 0.14f;
                        q3 = 0.11f;
                    }

                    float fSv1 = 2f * Mathf.Sin(Mathf.PI * Mathf.Clamp(f1 / (float)sampleRate, 0.005f, 0.45f));
                    _vocalF1_Lp += fSv1 * _vocalF1_Bp;
                    float hp1 = glottalPulse - _vocalF1_Lp - q1 * _vocalF1_Bp;
                    _vocalF1_Bp += fSv1 * hp1;

                    float fSv2 = 2f * Mathf.Sin(Mathf.PI * Mathf.Clamp(f2 / (float)sampleRate, 0.005f, 0.45f));
                    _vocalF2_Lp += fSv2 * _vocalF2_Bp;
                    float hp2 = glottalPulse - _vocalF2_Lp - q2 * _vocalF2_Bp;
                    _vocalF2_Bp += fSv2 * hp2;

                    float fSv3 = 2f * Mathf.Sin(Mathf.PI * Mathf.Clamp(f3 / (float)sampleRate, 0.005f, 0.45f));
                    _vocalF3_Lp += fSv3 * _vocalF3_Bp;
                    float hp3 = glottalPulse - _vocalF3_Lp - q3 * _vocalF3_Bp;
                    _vocalF3_Bp += fSv3 * hp3;

                    float vocalResonance = (_vocalF1_Bp * 0.95f) + (_vocalF2_Bp * 0.72f) + (_vocalF3_Bp * 0.45f);

                    float targetVocalAmp = IsHarmonicNoteStable
                        ? Mathf.Clamp01(DetectedEnergyRMS * 3.8f)
                        : (DetectedEnergyRMS > _silenceThreshold ? 0.20f : 0f);

                    if (targetVocalAmp > _vocalAmpEnv)
                        _vocalAmpEnv += (targetVocalAmp - _vocalAmpEnv) * vocalAttack;
                    else
                        _vocalAmpEnv *= vocalRelease;

                    float vocalDuck = 1.0f - (_drumSidechainDuck * 0.35f);
                    vocalSample = (float)Math.Tanh(vocalResonance * 1.65) * _vocalAmpEnv * _vocalMasterVolume * vocalDuck;
                }

                // 4. Émulation Percussive DSP
                float drumMix = 0f;

                if (_emulateDrums)
                {
                    // Kick
                    float currentKickFreq = 38f + 130f * _kickPitchEnv;
                    _kickPhase += (2.0 * Math.PI * currentKickFreq) / sampleRate;
                    if (_kickPhase > Math.PI * 2) _kickPhase -= Math.PI * 2;
                    float kickBody = (float)Math.Tanh(Math.Sin(_kickPhase) * 2.8);
                    float kickClick = NextFastNoise() * Mathf.Exp(-75f * (1f - _kickPitchEnv)) * 0.35f;
                    float kickSample = (kickBody * 0.85f + kickClick) * _kickEnv * _kickVolume;

                    _kickEnv *= kickDecay;
                    _kickPitchEnv *= kickPitchDecay;

                    // Snare
                    _snareTonePhase1 += (2.0 * Math.PI * 185.0) / sampleRate;
                    _snareTonePhase2 += (2.0 * Math.PI * 242.0) / sampleRate;
                    if (_snareTonePhase1 > Math.PI * 2) _snareTonePhase1 -= Math.PI * 2;
                    if (_snareTonePhase2 > Math.PI * 2) _snareTonePhase2 -= Math.PI * 2;

                    float snareTone = ((float)Math.Sin(_snareTonePhase1) * 0.6f + (float)Math.Sin(_snareTonePhase2) * 0.4f) * _snareToneEnv;

                    float rawNoise = NextFastNoise();
                    _snareHpState += 0.32f * (rawNoise - _snareHpState);
                    float snareSnappy = (rawNoise - _snareHpState) * _snareNoiseEnv;

                    float snareSample = (snareTone * 0.55f + snareSnappy * 0.90f) * _snareVolume;

                    _snareToneEnv *= snareToneDecay;
                    _snareNoiseEnv *= snareNoiseDecay;

                    // Hi-Hat
                    float hatRaw = NextFastNoise();
                    _hatHpState += 0.52f * (hatRaw - _hatHpState);
                    float hatSample = (hatRaw - _hatHpState) * _hatEnv * _hatVolume;

                    _hatEnv *= hatDecay;
                    _drumSidechainDuck *= duckDecay;

                    drumMix = (kickSample + snareSample + hatSample) * _drumMasterVolume;
                }

                // 5. Sommation Stéréo & Limiteur Analogique
                float sharedElements = (synthSample * 0.45f) + (choirSample * 0.70f) + (vocalSample * 0.88f) + (drumMix * 1.10f);
                float finalL = (float)Math.Tanh((sharedElements + (bassSampleL * 0.92f)) * 0.92);
                float finalR = (float)Math.Tanh((sharedElements + (bassSampleR * 0.92f)) * 0.92);

                if (channels == 1)
                {
                    data[i] = (finalL + finalR) * 0.5f;
                }
                else
                {
                    data[i] = finalL;
                    data[i + 1] = finalR;
                    for (int c = 2; c < channels; c++)
                    {
                        data[i + c] = (finalL + finalR) * 0.5f;
                    }
                }
            }
        }

        public void RefreshLinuxApps()
        {
            DetectedMediaApps.Clear();
            if (Application.platform != RuntimePlatform.LinuxPlayer && Application.platform != RuntimePlatform.LinuxEditor)
                return;

            string outputs = RunCommandSimple("pw-link", "-o");
            if (string.IsNullOrEmpty(outputs)) return;

            var lines = outputs.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var appMap = new Dictionary<string, MediaAppInfo>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;

                string node = line.Substring(0, colon).Trim();
                string port = line.Substring(colon + 1).Trim();
                string nodeLower = node.ToLowerInvariant();
                string portLower = port.ToLowerInvariant();

                if (nodeLower.StartsWith("alsa_") ||
                    nodeLower.StartsWith("system") ||
                    nodeLower.StartsWith("v4l2") ||
                    nodeLower.StartsWith("midi") ||
                    nodeLower.StartsWith("bluez_") ||
                    nodeLower.Contains("fmod") ||
                    nodeLower.Contains("speech-dispatcher") ||
                    nodeLower.Contains("easyeffects") ||
                    nodeLower.Contains("wireplumber") ||
                    nodeLower.Contains("pipewire") ||
                    nodeLower.Contains("dummy") ||
                    nodeLower.Contains("loopback") ||
                    nodeLower.Contains("unity") ||
                    nodeLower.Contains("killtime"))
                {
                    continue;
                }

                bool isPlaybackPort = portLower.Contains("output") || portLower.Contains("playback") || portLower.EndsWith("_fl") || portLower.EndsWith("_fr");
                bool isNotCapture = !portLower.Contains("capture") && !portLower.Contains("monitor") && !portLower.Contains("midi");
                if (!isPlaybackPort || !isNotCapture) continue;

                string displayName = CleanApplicationName(node);

                if (!appMap.TryGetValue(node, out var appInfo))
                {
                    appInfo = new MediaAppInfo
                    {
                        DisplayName = displayName,
                        NodeName = node
                    };
                    appMap[node] = appInfo;
                }

                if (!appInfo.Ports.Contains(line))
                {
                    appInfo.Ports.Add(line);
                }
            }

            DetectedMediaApps.AddRange(appMap.Values);
        }

        public bool LinkLinuxApp(MediaAppInfo app)
        {
            if (app == null || app.Ports.Count == 0) return false;

            if (!IsRecordingActive)
            {
                StartCapture();
                System.Threading.Thread.Sleep(150);
            }

            string inputs = RunCommandSimple("pw-link", "-i");
            if (string.IsNullOrEmpty(inputs))
            {
                LinkStatusMessage = "<color=#FF4444>pw-link -i n'a retourné aucun port.</color>";
                return false;
            }

            var inLines = inputs.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var unityPorts = new List<string>();

            for (int i = 0; i < inLines.Length; i++)
            {
                string line = inLines[i].Trim();
                string lower = line.ToLowerInvariant();
                if (lower.Contains("fmod") || lower.Contains("unity") || lower.Contains("killtime") || lower.Contains("alsa plug-in"))
                {
                    if (!lower.Contains("output_") && !lower.Contains("playback_") && !lower.Contains("monitor_"))
                    {
                        unityPorts.Add(line);
                    }
                }
            }

            if (unityPorts.Count == 0)
            {
                LinkStatusMessage = "<color=#FF4444>Port d'enregistrement Unity introuvable dans PipeWire.</color>";
                return false;
            }

            SeverLoopbackFeedbackLinks(unityPorts);

            if (!string.IsNullOrEmpty(LinkedLinuxApp) && !string.Equals(LinkedLinuxApp, app.DisplayName, StringComparison.OrdinalIgnoreCase))
            {
                var prevApp = DetectedMediaApps.Find(a => string.Equals(a.DisplayName, LinkedLinuxApp, StringComparison.OrdinalIgnoreCase));
                if (prevApp != null) UnlinkLinuxApp(prevApp);
            }

            int linkedCount = 0;
            string lastError = null;
            int maxLinks = Math.Min(app.Ports.Count, unityPorts.Count);

            for (int i = 0; i < maxLinks; i++)
            {
                var (exitCode, stdout, stderr) = ExecuteDirectLink(app.Ports[i], unityPorts[i], disconnect: false);
                if (exitCode == 0 || (!string.IsNullOrEmpty(stderr) && (stderr.ToLowerInvariant().Contains("exist") || stderr.ToLowerInvariant().Contains("already"))))
                {
                    linkedCount++;
                }
                else
                {
                    lastError = stderr;
                }
            }

            if (linkedCount > 0)
            {
                LinkedLinuxApp = app.DisplayName;
                LinkStatusMessage = $"<color=#00FF88>Connecté à {app.DisplayName} (Anti-Larsen actif)</color>";
                return true;
            }

            LinkStatusMessage = $"<color=#FF4444>Échec liaison ({lastError?.Trim()})</color>";
            return false;
        }

        private void SeverLoopbackFeedbackLinks(List<string> unityPorts)
        {
            string linksOutput = RunCommandSimple("pw-link", "-l");
            if (string.IsNullOrEmpty(linksOutput)) return;

            var lines = linksOutput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                int arrowIdx = line.IndexOf("->", StringComparison.Ordinal);
                if (arrowIdx <= 0) continue;

                string src = line.Substring(0, arrowIdx).Trim();
                string dst = line.Substring(arrowIdx + 2).Trim();

                if (unityPorts.Contains(dst))
                {
                    string srcLower = src.ToLowerInvariant();
                    if (srcLower.Contains("monitor") || srcLower.Contains("alsa_output"))
                    {
                        ExecuteDirectLink(src, dst, disconnect: true);
                    }
                }
            }
        }

        public bool UnlinkLinuxApp(MediaAppInfo app)
        {
            if (app == null || app.Ports.Count == 0) return false;

            string inputs = RunCommandSimple("pw-link", "-i");
            if (string.IsNullOrEmpty(inputs)) return false;

            var inLines = inputs.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var unityPorts = new List<string>();

            for (int i = 0; i < inLines.Length; i++)
            {
                string line = inLines[i].Trim();
                string lower = line.ToLowerInvariant();
                if (lower.Contains("fmod") || lower.Contains("unity") || lower.Contains("killtime") || lower.Contains("alsa plug-in"))
                {
                    if (!lower.Contains("output_") && !lower.Contains("playback_") && !lower.Contains("monitor_"))
                    {
                        unityPorts.Add(line);
                    }
                }
            }

            int maxLinks = Math.Min(app.Ports.Count, unityPorts.Count);
            for (int i = 0; i < maxLinks; i++)
            {
                ExecuteDirectLink(app.Ports[i], unityPorts[i], disconnect: true);
            }

            if (string.Equals(LinkedLinuxApp, app.DisplayName, StringComparison.OrdinalIgnoreCase))
            {
                LinkedLinuxApp = null;
            }

            LinkStatusMessage = $"<color=yellow>Liaison rompue avec {app.DisplayName}.</color>";
            return true;
        }

        private static string CleanApplicationName(string rawNode)
        {
            string lower = rawNode.ToLowerInvariant();
            if (lower.Contains("chrome")) return "Google Chrome";
            if (lower.Contains("chromium")) return "Chromium";
            if (lower.Contains("firefox")) return "Firefox";
            if (lower.Contains("spotify")) return "Spotify";
            if (lower.Contains("vlc")) return "VLC Media Player";
            if (lower.Contains("mpv")) return "MPV Player";
            if (lower.Contains("discord")) return "Discord";
            if (lower.Contains("brave")) return "Brave Browser";
            return rawNode;
        }

        private static string RunCommandSimple(string cmd, string args)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = cmd,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc == null) return null;
                string output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit(1000);
                return output;
            }
            catch
            {
                return null;
            }
        }

        private static (int exitCode, string stdout, string stderr) ExecuteDirectLink(string srcPort, string dstPort, bool disconnect = false)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "pw-link",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                if (disconnect) psi.ArgumentList.Add("-d");
                psi.ArgumentList.Add(srcPort);
                psi.ArgumentList.Add(dstPort);

                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc == null) return (-1, "", "Process null");
                string stdout = proc.StandardOutput.ReadToEnd();
                string stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit(1000);
                return (proc.ExitCode, stdout, stderr);
            }
            catch (Exception ex)
            {
                return (-1, "", ex.Message);
            }
        }
    }
}
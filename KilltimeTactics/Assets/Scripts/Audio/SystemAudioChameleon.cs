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
    /// <summary>SystemAudioChameleon — Core : façade, Inspector, buffers, télémétrie, lifecycle (partial, move-only).</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public partial class SystemAudioChameleon : MonoBehaviour
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
        // Gain Master global du jeu (KilltimeAudioManager Master + Mute), caché sur le
        // thread principal (Update) car OnAudioFilterRead tourne sur le thread audio.
        // La sortie Caméléon (AudioSource sans Mixer) le bypassait et restait à fond
        // quand l'utilisateur baissait le volume global.
        private volatile float _cachedMasterGain = 1f;
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
            // Le volume global est appliqué manuellement dans OnAudioFilterRead
            // (gain Master caché) : on verrouille le volume source à 1 pour éviter
            // toute double atténuation.
            _audioSource.volume = 1f;
            RefreshMasterGain();
        }

        private void Start()
        {
            // Pas de AudioSettings.Reset ici : il coupe toutes les sources en cours
            // (c'était le "la musique se coupe à la 1re ouverture de F11", le panneau
            // créant ce composant à la volée). Le Reset vit dans StartCapture().
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
            // Toujours suivre le volume global, même en veille : sinon un changement
            // de Master pendant que le Caméléon est inactif serait ignoré à la reprise.
            RefreshMasterGain();
            if (!_enableChameleonMode) return;

            AnalyzeIncomingAudio();
            UpdateHarmonicParameters();
        }

        /// <summary>
        /// Met en cache le volume sonore global du jeu (Master + Mute).
        /// Appelé sur le thread principal uniquement ; OnAudioFilterRead ne fait
        /// que lire _cachedMasterGain (thread audio : pas d'accès Unity direct).
        /// </summary>
        private void RefreshMasterGain()
        {
            try
            {
                var mgr = KilltimeAudioManager.Instance;
                if (mgr == null)
                {
                    _cachedMasterGain = 1f;
                }
                else
                {
                    _cachedMasterGain = mgr.IsMuted
                        ? 0f
                        : Mathf.Clamp01(mgr.GetCategoryVolume(SoundCategory.Master));
                }
                if (_audioSource != null && _audioSource.volume != 1f)
                    _audioSource.volume = 1f;
            }
            catch
            {
                _cachedMasterGain = 1f;
            }
        }

        private void OnDestroy()
        {
            StopCapture();
            if (Instance == this) Instance = null;
        }
    }
}

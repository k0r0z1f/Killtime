using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using Killtime.Core.Combat;

namespace Killtime.Audio
{
    /// <summary>
    /// Coeur du systeme son Killtime Tactics (Unity natif, WebGL-safe).
    /// - Singleton persistant, auto-cree si absent.
    /// - Pools 2D / 3D + double source musique (crossfade) + nappe d'ambiance.
    /// - Hybride : clips du SoundBank prioritaires, sinon synthese procedurale.
    /// - Snapshots code (bullet-time / stase / rewind) via pitch + lowpass, sans .mixer requis.
    /// - Volumes persistes (PlayerPrefs), ducking cinematique, musique adaptative.
    ///
    /// Aucune dependance externe (pas de FMOD/Wwise) pour garder l'export WebGL leger.
    /// </summary>
    public struct ProceduralStemStatus
    {
        public ProceduralStemType Type;
        public string Name;
        public string SubType;
        public bool IsActive;
        public float Intensity;
        public float RhythmicPulse;
        public Color Color;
        public StemOverrideState OverrideState;
    }

    [DisallowMultipleComponent]
    public partial class KilltimeAudioManager : MonoBehaviour
    {
        public static KilltimeAudioManager Instance { get; private set; }

        [Header("Banque (optionnelle)")]
        [Tooltip("Si null => 100% procédural. Si assignée, les clips importés overriden au cas par cas.")]
        [SerializeField] private SoundBank _bank;

        [Header("Mixer (optionnel, recommandé)")]
        [Tooltip("Si assigné, les volumes utilisent les paramètres exposés MasterVol/MusicVol/... Sinon volumes directs.")]
        [SerializeField] private AudioMixer _mixer;
        [SerializeField] private string _masterParam = "MasterVol";
        [SerializeField] private string _musicParam = "MusicVol";
        [SerializeField] private string _sfxParam = "SFXVol";
        [SerializeField] private string _uiParam = "UIVol";
        [SerializeField] private string _ambienceParam = "AmbienceVol";

        [Header("Pools")]
        [SerializeField, Range(8, 32)] private int _pool2DSize = 24;
        [SerializeField, Range(4, 24)] private int _pool3DSize = 20;
        [SerializeField, Range(0.5f, 4f)] private float _musicCrossfade = 1.6f;

        [Header("Musique adaptative")]
        [Tooltip("Anti-yoyo de l'intensité : ignore les micro-variations sous ce seuil.")]
        [SerializeField, Range(0f, 10f)] private float _adaptiveCooldown = 3f;
        [SerializeField, Range(0f, 0.5f)] private float _adaptiveHysteresis = 0.12f;
        [Tooltip("Crossfade des fins de partie (Victory/Defeat), plus solennelle.")]
        [SerializeField, Range(0.5f, 4f)] private float _finaleCrossfade = 2.8f;
        [SerializeField, Range(0f, 1.5f)] private float _stingerVolume = 0.9f;

        [Header("Playlist Combat")]
        [Tooltip("Durée avant de basculer automatiquement sur le thème suivant de la liste combat (secondes).")]
        [SerializeField, Range(16f, 120f)] private float _combatRotationInterval = 48f;

        [Header("Musique générative ∞ (sans chanson préfaite)")]
        [Tooltip("Si actif, Combat / Boss / Tension = boucles génératives infinies (jamais 2x pareil). Explore / Victory / Defeat restent les chansons préfaites.")]
        [SerializeField] private bool _generativeCombat = false;
        [Tooltip("Identité de la famille motivique (changez pour un autre 'groupe virtuel').")]
        [SerializeField] private int _generativeSeed = 1337;
        [SerializeField, Range(0f, 1f)] private float _generativeBrightness = 0.55f;
        [SerializeField, Range(0f, 1f)] private float _generativeChaos = 0.35f;
        [Tooltip("Style imposé au moteur génératif. Auto = pilotage énergie/tension actuel (défaut).")]
        [SerializeField] private GenerativeStylePreset _generativePreset = GenerativeStylePreset.Auto;
        [Tooltip("Fondu entre deux variations génératives. Plus court que le legacy (1.6s) : les variations s'enchaînent sur un impact/crash qui masque la transition.")]
        [SerializeField, Range(0.4f, 3f)] private float _generativeCrossfade = 1.1f;

        [Header("Volumes initiaux")]
        [SerializeField, Range(0f, 1f)] private float _masterVolume = 0.9f;
        [SerializeField, Range(0f, 1f)] private float _musicVolume = 0.75f;
        [SerializeField, Range(0f, 1f)] private float _ambienceVolume = 0.6f;
        [SerializeField, Range(0f, 1f)] private float _sfxVolume = 0.9f;
        [SerializeField, Range(0f, 1f)] private float _uiVolume = 0.85f;
        [SerializeField, Range(0f, 1f)] private float _cinematicVolume = 1f;

        [Header("Bullet-time")]
        [SerializeField, Range(0.4f, 1f)] private float _slowMoSfxPitch = 0.72f;
#pragma warning disable CS0414 // Legacy : musique verrouillée à pitch 1, champ conservé pour compatibilité Inspecteur.
        [Tooltip("Legacy : la musique reste désormais à pitch 1 pendant les cinématiques (non-régression ralenti). Conservé pour compatibilité Inspecteur.")]
        [SerializeField, Range(0.4f, 1f)] private float _slowMoMusicPitch = 0.62f;
#pragma warning restore CS0414
        #pragma warning disable CS0414
        [Tooltip("Legacy : le filtre passe-bas sur l'AudioListener a été neutralisé pour préserver l'intégrité fréquentielle de la musique.")]
        [SerializeField] private float _slowMoLowpass = 3800f;
#pragma warning restore CS0414

        private const string PP_MASTER = "KT_Audio_Master";
        private const string PP_MUSIC = "KT_Audio_Music";
        private const string PP_AMB = "KT_Audio_Ambience";
        private const string PP_SFX = "KT_Audio_SFX";
        private const string PP_UI = "KT_Audio_UI";

        private readonly List<AudioSource> _pool2D = new();
        private readonly List<AudioSource> _pool3D = new();
        private int _cursor2D;
        private int _cursor3D;

        private AudioSource _musicA;
        private AudioSource _musicB;
        private bool _musicFlip;
        private MusicMood _currentMood = MusicMood.None;
        private MusicMood _targetMood = MusicMood.None;
        private int _currentTrackIndex = 0;
        private float _combatTrackTimer = 0f;
        private int _currentLevel = 1;
        private float _targetIntensity01 = 0.5f;
        private float _currentIntensity01 = 0.5f;
        private float _lastAdaptiveSwitchTime = -99f;
        private float _lastStingerTime = -99f;
        private AudioSource _activeMusic;
        private bool _isFading;
        private Coroutine _musicRoutine;

        public int CurrentTrackIndex => _currentTrackIndex;

        private AudioSource _ambienceSource;
        private readonly Dictionary<string, AudioSource> _activeSceneAmbienceSources = new();
        private readonly Dictionary<string, Coroutine> _activeSceneAmbienceFades = new();
        // Sons de scène 🔊 pilotés par cartes (contrôle total) : instance suivie par
        // clé "SoundId de carte" pour stop explicite, monitor éditeur et kill-switch.
        private readonly Dictionary<string, AudioSource> _activeSceneSoundSources = new();
        private readonly Dictionary<string, Coroutine> _activeSceneSoundFades = new();

        private readonly Dictionary<SoundId, float> _lastPlayTime = new();
        private float _slowMoWeight;           // 0 normal -> 1 bullet-time
        private float _cinematicDuck;          // 0 normal -> 1 ducké
        private float _pauseDuck;
        private bool _muted;

        public MusicMood CurrentMood => _targetMood;
        public float CurrentIntensity => _targetIntensity01;
        public MusicIntensity CurrentIntensityLevel => AdaptiveMusicDirector.Quantize(_targetIntensity01);
        public bool IsMuted => _muted;
        public AudioSource ActiveMusicSource => _activeMusic;

        private readonly float[] _rmsBuffer = new float[64];

        // =====================================================================
        // Cycle de vie
        // =====================================================================
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoBootstrap()
        {
            if (Instance == null && FindAnyObjectByType<KilltimeAudioManager>() == null)
            {
                var go = new GameObject("[Audio] KilltimeAudioManager");
                go.AddComponent<KilltimeAudioManager>();
            }
            if (FindAnyObjectByType<KilltimeAudioSettingsUI>() == null)
            {
                var ui = new GameObject("[Audio] KilltimeAudioSettingsUI");
                ui.AddComponent<KilltimeAudioSettingsUI>();
            }
            // Pré-créé au démarrage (et non à la 1re ouverture du panneau F11) :
            // son Start() ne doit plus jamais couper la musique en cours.
            if (Experimental.SystemAudioChameleon.Instance == null
                && FindAnyObjectByType<Experimental.SystemAudioChameleon>() == null)
            {
                var cGo = new GameObject("[Audio] SystemAudioChameleon");
                cGo.AddComponent<Experimental.SystemAudioChameleon>();
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

            ProceduralAudioFactory.ClearCache();
            LoadVolumes();
            BuildPools();
            ApplyAllVolumes();
        }   

        private void Start()
        {
            // Démarrage musical par défaut : exploration calme, bascule en combat via hooks.
            if (_targetMood == MusicMood.None)
                PlayMusic(MusicMood.Explore, MusicIntensity.Calm);
        }

        private void Update()
        {
            // Le ralenti dramatique des cinématiques (timeScale 0.45 dans
            // CinematicDirector) ne doit JAMAIS ralentir la musique : pitch
            // verrouillé à 1. Seuls les SFX plongent (pitch + lowpass), la
            // musique ne fait que ducker en volume (voir ApplyAllVolumes).
            // Donc _slowMoWeight ne suit QUE le vrai timeScale, sans résiduel
            // _cinematicDuck*0.5 qui pitchait déjà à 0.81 pendant le zoom.
            float targetSlow = (Time.timeScale < 0.7f && Time.timeScale > 0.0001f) ? 1f : 0f;
            _slowMoWeight = Mathf.MoveTowards(_slowMoWeight, targetSlow, Time.unscaledDeltaTime * 5f);

            // Musique : pitch constant. Le bullet-time ne s'applique qu'aux SFX
            // (voir ConfigureAndPlay avec _slowMoSfxPitch). Forcer 1f corrige
            // aussi les sessions où un ancien _slowMoWeight l'avait descendu.
            if (_musicA != null && _musicA.pitch != 1f)
                _musicA.pitch = 1f;
            if (_musicB != null && _musicB.pitch != 1f)
                _musicB.pitch = 1f;

            if (_generativeCombat && IsGenerativeMood(_targetMood) && !_isFading && _genParamsValid)
            {
                // Scheduler infini : on pré-génère la variation suivante juste
                // avant la fin de la boucle pour un enchaînement sans blanc.
                _genLoopTimer += Time.unscaledDeltaTime;
                float lookahead = Mathf.Max(0.6f, _generativeCrossfade + 0.15f);
                if (_genLoopTimer >= _genLoopDur - lookahead)
                    AdvanceGenerativeVariation();
            }
            else if (_targetMood == MusicMood.Combat && !_isFading)
            {
                int trackCount = ProceduralAudioFactory.GetTrackCount(MusicMood.Combat);
                if (trackCount > 1)
                {
                    _combatTrackTimer += Time.unscaledDeltaTime;
                    if (_combatTrackTimer >= _combatRotationInterval)
                    {
                        NextCombatTrack();
                    }
                }
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}

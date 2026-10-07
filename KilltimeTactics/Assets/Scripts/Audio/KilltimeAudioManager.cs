using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using Killtime.Core.Combat;

namespace Killtime.Audio
{
    /// <summary>
    /// Cœur du système son Killtime Tactics (Unity natif, WebGL-safe).
    /// - Singleton persistant, auto-créé si absent.
    /// - Pools 2D / 3D + double source musique (crossfade) + nappe d'ambiance.
    /// - Hybride : clips du SoundBank prioritaires, sinon synthèse procédurale.
    /// - Snapshots code (bullet-time / stase / rewind) via pitch + lowpass, sans .mixer requis.
    /// - Volumes persistés (PlayerPrefs), ducking cinématique, musique adaptative.
    ///
    /// Aucune dépendance externe (pas de FMOD/Wwise) pour garder l'export WebGL léger.
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
    public class KilltimeAudioManager : MonoBehaviour
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

        public float GetActiveMusicRms()
        {
            if (_activeMusic == null || !_activeMusic.isPlaying) return 0f;
            try
            {
                _activeMusic.GetOutputData(_rmsBuffer, 0);
                float sum = 0f;
                for (int i = 0; i < _rmsBuffer.Length; i++)
                {
                    sum += _rmsBuffer[i] * _rmsBuffer[i];
                }
                return Mathf.Sqrt(sum / _rmsBuffer.Length);
            }
            catch (System.Exception)
            {
                return 0f;
            }
        }

        public List<ProceduralStemStatus> GetProceduralStemsStatus()
        {
            var stems = new List<ProceduralStemStatus>(8);
            float rms = GetActiveMusicRms();
            float t = (_activeMusic != null && _activeMusic.isPlaying) ? _activeMusic.time : Time.unscaledTime;

            if (_generativeCombat && IsGenerativeMood(_targetMood) && _genParamsValid)
            {
                var gp = _genParams;
                float bpm = Mathf.Max(60f, gp.bpm);
                float beat = t * (bpm / 60f);

                float cineStemDucking = Mathf.Clamp01(1f - 0.45f * _cinematicDuck);

                // 1. Batterie
                var drumOv = GetStemOverride(ProceduralStemType.Drums);
                bool isBreak = (gp.generation % 5 == 4) && gp.intensity01 < 0.85f;
                float drumGate = isBreak ? 0.25f : 1f;
                float drumVol = (drumOv == StemOverrideState.ForceActive) ? 0.85f : ((0.55f + 0.65f * gp.intensity01) * drumGate * cineStemDucking);
                bool drumsOn = drumOv == StemOverrideState.ForceActive || (drumOv == StemOverrideState.Auto && drumVol > 0.04f);
                if (drumOv == StemOverrideState.Muted) drumsOn = false;
                float drumPulse = drumsOn ? Mathf.Pow(Mathf.Abs(Mathf.Sin(beat * Mathf.PI)), 3f) : 0f;
                string drumSub = isBreak ? $"Break ({gp.drumStyle})" : gp.drumStyle.ToString();
                if (_cinematicDuck > 0.1f) drumSub += " [Ciné-Duck]";
                stems.Add(new ProceduralStemStatus
                {
                    Type = ProceduralStemType.Drums,
                    Name = "Batterie",
                    SubType = drumSub,
                    IsActive = drumsOn,
                    Intensity = drumsOn ? Mathf.Clamp01(drumVol) : 0f,
                    RhythmicPulse = Mathf.Clamp01(drumPulse * (0.6f + rms * 2.5f)),
                    Color = new Color(1.0f, 0.25f, 0.4f),
                    OverrideState = drumOv
                });

                // 2. Basse
                var bassOv = GetStemOverride(ProceduralStemType.Bass);
                float bassVol = (bassOv == StemOverrideState.ForceActive) ? 0.42f : (0.30f + 0.14f * gp.intensity01);
                bool bassOn = bassOv != StemOverrideState.Muted;
                bool bassGrowl = gp.intensity01 > 0.62f && gp.preset != GenerativeStylePreset.Retro16Bit;
                float bassPulse = bassOn ? Mathf.Pow(Mathf.Abs(Mathf.Sin(beat * 2f * Mathf.PI)), 2.5f) : 0f;
                stems.Add(new ProceduralStemStatus
                {
                    Type = ProceduralStemType.Bass,
                    Name = "Basse FM",
                    SubType = bassGrowl ? "Growl FM & Reese" : "Sub Clean / 808",
                    IsActive = bassOn,
                    Intensity = bassOn ? Mathf.Clamp01(bassVol * 1.5f) : 0f,
                    RhythmicPulse = Mathf.Clamp01(bassPulse * (0.5f + rms * 3f)),
                    Color = new Color(0.2f, 0.9f, 0.3f),
                    OverrideState = bassOv
                });

                // 3. Pad Supersaw
                var padOv = GetStemOverride(ProceduralStemType.Pad);
                float padVol = (padOv == StemOverrideState.ForceActive) ? 0.32f : (0.20f + 0.10f * (1f - gp.intensity01) + 0.06f * gp.tension01);
                bool padOn = padOv != StemOverrideState.Muted;
                float padPulse = padOn ? (0.65f + 0.35f * Mathf.Sin(t * 0.75f)) : 0f;
                stems.Add(new ProceduralStemStatus
                {
                    Type = ProceduralStemType.Pad,
                    Name = "Nappe (Pad)",
                    SubType = $"Supersaw (Bri {Mathf.RoundToInt(gp.brightness01 * 100)}%)",
                    IsActive = padOn,
                    Intensity = padOn ? Mathf.Clamp01(padVol * 2.2f) : 0f,
                    RhythmicPulse = Mathf.Clamp01(padPulse * (0.7f + rms * 1.5f)),
                    Color = new Color(0.15f, 0.75f, 1.0f),
                    OverrideState = padOv
                });

                // 4. Arpège Euclidien
                var arpOv = GetStemOverride(ProceduralStemType.Arp);
                bool arpAuto = gp.intensity01 > 0.25f;
                bool arpOn = arpOv == StemOverrideState.ForceActive || (arpOv == StemOverrideState.Auto && arpAuto);
                if (arpOv == StemOverrideState.Muted) arpOn = false;
                float arpVol = arpOn ? (0.08f + 0.10f * gp.intensity01) : 0f;
                float arpPulse = arpOn ? Mathf.Pow(Mathf.Abs(Mathf.Sin(beat * 4f * Mathf.PI)), 2f) : 0f;
                stems.Add(new ProceduralStemStatus
                {
                    Type = ProceduralStemType.Arp,
                    Name = "Arpège",
                    SubType = arpOn ? "Euclidien 16-step" : "Mute (< 25% Énergie)",
                    IsActive = arpOn,
                    Intensity = arpOn ? Mathf.Clamp01(arpVol * 3.5f) : 0f,
                    RhythmicPulse = Mathf.Clamp01(arpPulse * (0.6f + rms * 2f)),
                    Color = new Color(0.95f, 0.85f, 0.2f),
                    OverrideState = arpOv
                });

                // 5. Lead Soliste
                var leadOv = GetStemOverride(ProceduralStemType.Lead);
                bool leadAuto = gp.intensity01 > 0.45f || gp.tension01 > 0.5f;
                bool leadOn = leadOv == StemOverrideState.ForceActive || (leadOv == StemOverrideState.Auto && leadAuto);
                if (leadOv == StemOverrideState.Muted) leadOn = false;
                float leadVol = leadOn ? ((0.10f + 0.09f * gp.intensity01) * cineStemDucking) : 0f;
                float leadPulse = leadOn ? Mathf.Abs(Mathf.Sin(beat * 0.5f * Mathf.PI)) : 0f;
                string leadSub = leadOn ? (gp.preset == GenerativeStylePreset.Retro16Bit ? "Square 16-Bit" : "Call-Response Saw") : "Standby";
                if (_cinematicDuck > 0.1f && leadOn) leadSub += " [Ciné-Duck]";
                stems.Add(new ProceduralStemStatus
                {
                    Type = ProceduralStemType.Lead,
                    Name = "Lead Synth",
                    SubType = leadSub,
                    IsActive = leadOn,
                    Intensity = leadOn ? Mathf.Clamp01(leadVol * 3f) : 0f,
                    RhythmicPulse = Mathf.Clamp01(leadPulse * (0.5f + rms * 2.5f)),
                    Color = new Color(1.0f, 0.55f, 0.1f),
                    OverrideState = leadOv
                });

                // 6. Chœur Arcanotech
                var choirOv = GetStemOverride(ProceduralStemType.Choir);
                bool choirAuto = gp.tension01 > 0.45f && gp.preset != GenerativeStylePreset.Retro16Bit;
                bool choirOn = choirOv == StemOverrideState.ForceActive || (choirOv == StemOverrideState.Auto && choirAuto);
                if (choirOv == StemOverrideState.Muted) choirOn = false;
                float choirVol = choirOn ? (0.05f + 0.09f * gp.tension01) : 0f;
                float choirPulse = choirOn ? (0.6f + 0.4f * Mathf.Sin(t * 1.1f)) : 0f;
                stems.Add(new ProceduralStemStatus
                {
                    Type = ProceduralStemType.Choir,
                    Name = "Chœur",
                    SubType = choirOn ? "Arcanotech Vox Bed" : "Inactif (Tension < 45%)",
                    IsActive = choirOn,
                    Intensity = choirOn ? Mathf.Clamp01(choirVol * 4f) : 0f,
                    RhythmicPulse = Mathf.Clamp01(choirPulse * (0.7f + rms * 1.5f)),
                    Color = new Color(0.8f, 0.45f, 1.0f),
                    OverrideState = choirOv
                });

                // 7. Staccato / Djent
                var staccOv = GetStemOverride(ProceduralStemType.Staccato);
                bool staccAuto = gp.intensity01 > 0.35f;
                bool staccOn = staccOv == StemOverrideState.ForceActive || (staccOv == StemOverrideState.Auto && staccAuto);
                if (staccOv == StemOverrideState.Muted) staccOn = false;
                float staccVol = staccOn ? (0.10f + 0.10f * gp.intensity01) : 0f;
                float staccPulse = staccOn ? Mathf.Pow(Mathf.Abs(Mathf.Sin(beat * 2f * Mathf.PI + 0.5f)), 3f) : 0f;
                stems.Add(new ProceduralStemStatus
                {
                    Type = ProceduralStemType.Staccato,
                    Name = "Staccato",
                    SubType = staccOn ? "Strings & Chug" : "Mute (< 35% Énergie)",
                    IsActive = staccOn,
                    Intensity = staccOn ? Mathf.Clamp01(staccVol * 3f) : 0f,
                    RhythmicPulse = Mathf.Clamp01(staccPulse * (0.5f + rms * 2.5f)),
                    Color = new Color(0.9f, 0.3f, 0.6f),
                    OverrideState = staccOv
                });

                // 8. Sub & Shimmer
                var subAirOv = GetStemOverride(ProceduralStemType.SubAir);
                bool subAirOn = subAirOv != StemOverrideState.Muted;
                float subPulse = subAirOn ? (0.8f + 0.2f * Mathf.Sin(t * 0.4f)) : 0f;
                stems.Add(new ProceduralStemStatus
                {
                    Type = ProceduralStemType.SubAir,
                    Name = "Sub & Air",
                    SubType = $"{gp.rootMidi} MIDI / Shimmer",
                    IsActive = subAirOn,
                    Intensity = subAirOn ? (0.5f + 0.3f * gp.tension01) : 0f,
                    RhythmicPulse = Mathf.Clamp01(subPulse * (0.8f + rms * 1.2f)),
                    Color = new Color(0.0f, 0.85f, 0.95f),
                    OverrideState = subAirOv
                });
            }
            else
            {
                // Mode Standard / Préfaites (Explore, Tension, Combat T0-T2, Boss, Victory, Defeat)
                int level = _currentLevel;
                float bpm = (_targetMood == MusicMood.Explore || _targetMood == MusicMood.Tension || _targetMood == MusicMood.Defeat) ? 60f : 120f;
                float beat = t * (bpm / 60f);

                bool drumsActive = level >= 1 && (_targetMood == MusicMood.Combat || _targetMood == MusicMood.CombatBoss || _targetMood == MusicMood.Victory);
                stems.Add(new ProceduralStemStatus
                {
                    Name = "Batterie",
                    SubType = drumsActive ? (_targetMood == MusicMood.CombatBoss ? "War Taikos" : "Production Drums") : "Mute (Niveau 0)",
                    IsActive = drumsActive,
                    Intensity = drumsActive ? (0.4f + level * 0.3f) : 0f,
                    RhythmicPulse = drumsActive ? Mathf.Pow(Mathf.Abs(Mathf.Sin(beat * Mathf.PI)), 2.5f) : 0f,
                    Color = new Color(1.0f, 0.25f, 0.4f)
                });

                bool bassActive = level >= 1 && (_targetMood == MusicMood.Combat || _targetMood == MusicMood.CombatBoss || _targetMood == MusicMood.Victory);
                stems.Add(new ProceduralStemStatus
                {
                    Name = "Basse",
                    SubType = bassActive ? (_targetMood == MusicMood.Combat && _currentTrackIndex == 2 ? "Neuro Cyber Bass" : "Master Bassline") : "Mute",
                    IsActive = bassActive,
                    Intensity = bassActive ? (0.4f + level * 0.28f) : 0f,
                    RhythmicPulse = bassActive ? Mathf.Pow(Mathf.Abs(Mathf.Sin(beat * 2f * Mathf.PI)), 2f) : 0f,
                    Color = new Color(0.2f, 0.9f, 0.3f)
                });

                stems.Add(new ProceduralStemStatus
                {
                    Name = "Harmonie (Pad)",
                    SubType = $"{_targetMood} Chords",
                    IsActive = true,
                    Intensity = 0.6f + level * 0.2f,
                    RhythmicPulse = 0.7f + 0.3f * Mathf.Sin(t * 0.8f),
                    Color = new Color(0.15f, 0.75f, 1.0f)
                });

                bool arpActive = level >= 1 && (_targetMood == MusicMood.Combat || _targetMood == MusicMood.CombatBoss || _targetMood == MusicMood.Victory);
                stems.Add(new ProceduralStemStatus
                {
                    Name = "Arpège",
                    SubType = arpActive ? "Master Arp Echo" : "Mute",
                    IsActive = arpActive,
                    Intensity = arpActive ? (0.35f + level * 0.3f) : 0f,
                    RhythmicPulse = arpActive ? Mathf.Abs(Mathf.Sin(beat * 4f * Mathf.PI)) : 0f,
                    Color = new Color(0.95f, 0.85f, 0.2f)
                });

                bool leadActive = _targetMood == MusicMood.CombatBoss && level >= 1;
                stems.Add(new ProceduralStemStatus
                {
                    Name = "Lead Acid",
                    SubType = leadActive ? "Cybernetic Acid Lead" : "Inactif",
                    IsActive = leadActive,
                    Intensity = leadActive ? (0.45f + level * 0.25f) : 0f,
                    RhythmicPulse = leadActive ? Mathf.Abs(Mathf.Sin(beat * Mathf.PI)) : 0f,
                    Color = new Color(1.0f, 0.55f, 0.1f)
                });

                bool choirActive = _targetMood == MusicMood.CombatBoss;
                stems.Add(new ProceduralStemStatus
                {
                    Name = "Chœur",
                    SubType = choirActive ? "Arcanotech Choir (Boss)" : "Inactif",
                    IsActive = choirActive,
                    Intensity = choirActive ? (0.5f + level * 0.25f) : 0f,
                    RhythmicPulse = choirActive ? (0.6f + 0.4f * Mathf.Sin(t * 1.2f)) : 0f,
                    Color = new Color(0.8f, 0.45f, 1.0f)
                });

                bool staccActive = _targetMood == MusicMood.Combat && (_currentTrackIndex == 0 || _currentTrackIndex == 1);
                stems.Add(new ProceduralStemStatus
                {
                    Name = "Staccato / Clang",
                    SubType = staccActive ? "Pulse Strings & Anvil" : (_targetMood == MusicMood.Combat && _currentTrackIndex == 2 ? "Djent Chug" : "Mute"),
                    IsActive = staccActive || (_targetMood == MusicMood.Combat && _currentTrackIndex == 2),
                    Intensity = staccActive ? 0.65f : 0f,
                    RhythmicPulse = staccActive ? Mathf.Pow(Mathf.Abs(Mathf.Sin(beat * 2f * Mathf.PI)), 2.5f) : 0f,
                    Color = new Color(0.9f, 0.3f, 0.6f)
                });

                bool heartActive = _targetMood == MusicMood.Tension && level >= 1;
                stems.Add(new ProceduralStemStatus
                {
                    Name = "Sub / Atmos",
                    SubType = heartActive ? "Heartbeat & Riser" : (_targetMood == MusicMood.Explore ? "Sub & Shimmer" : "Sub Rumble"),
                    IsActive = true,
                    Intensity = 0.6f,
                    RhythmicPulse = heartActive ? Mathf.Pow(Mathf.Abs(Mathf.Sin(beat * Mathf.PI)), 4f) : (0.7f + 0.3f * Mathf.Sin(t * 0.5f)),
                    Color = new Color(0.0f, 0.85f, 0.95f)
                });
            }

            return stems;
        }

        // --- État génératif ∞ ---
        private int _genGeneration = 0;
        private GenerativeMusicParams _genParams;
        private bool _genParamsValid = false;
        private float _genLoopTimer = 0f;
        private float _genLoopDur = 16f;
        private float _pendingStrike = 0f;
        private float _lastAllyHp = 1f;
        private int _lastAllies = 3;
        private int _lastEnemies = 2;
        private int _lastTurn = 0;
        private int _stemOverrides = 0;

        public int StemOverridesMask => _stemOverrides;

        public StemOverrideState GetStemOverride(ProceduralStemType stem)
        {
            return (StemOverrideState)((_stemOverrides >> ((int)stem * 2)) & 0x3);
        }

        public void SetStemOverride(ProceduralStemType stem, StemOverrideState state)
        {
            int shift = (int)stem * 2;
            _stemOverrides = (_stemOverrides & ~(0x3 << shift)) | (((int)state & 0x3) << shift);
            ApplyStemOverridesAndRefresh();
        }

        public void ResetAllStemOverrides()
        {
            if (_stemOverrides == 0) return;
            _stemOverrides = 0;
            ApplyStemOverridesAndRefresh();
        }

        private void ApplyStemOverridesAndRefresh()
        {
            if (_generativeCombat && IsGenerativeMood(_targetMood))
            {
                _genParams.stemOverrides = _stemOverrides;
                RequestGenerativeVariation();
            }
            else if (_targetMood != MusicMood.None)
            {
                PlayMusic(_targetMood, _currentTrackIndex, _targetIntensity01, forceRestart: true);
            }
        }

        public bool GenerativeCombatEnabled => _generativeCombat;
        public GenerativeStylePreset GenerativePreset => _generativePreset;
        public float GenerativeBrightness => _generativeBrightness;
        public float GenerativeChaos => _generativeChaos;
        public int GenerativeGeneration => _genGeneration;
        public GenerativeMusicParams CurrentGenerativeParams => _genParams;
        public float GenerativeLoopProgress => _genLoopDur > 0f ? Mathf.Clamp01(_genLoopTimer / _genLoopDur) : 0f;

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
                float lookahead = Mathf.Max(0.6f, _generativeCrossfade + 0.4f);
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

        // =====================================================================
        // API principale
        // =====================================================================
        public void Play(SoundId id, float volumeScale = 1f, float pitchScale = 1f)
        {
            var def = Resolve(id);
            if (!CheckCooldown(def)) return;
            var clip = PickClip(def);
            if (clip == null) return;
            var src = Next2D();
            ConfigureAndPlay(src, def, clip, volumeScale, pitchScale, Vector3.zero, false);
        }

        public AudioSource PlayUI(SoundId id, float volumeScale = 1f)
        {
            var def = Resolve(id);
            if (!CheckCooldown(def)) return null;
            var clip = PickClip(def);
            if (clip == null) return null;
            var src = Next2D(isUI: true);
            ConfigureAndPlay(src, def, clip, volumeScale, 1f, Vector3.zero, false, forceCategory: SoundCategory.UI);
            return src;
        }

        /// <summary>
        /// Lecture d'un clip de banque (recette JSON) : 2D non-spatial, sans jitter
        /// de pitch (les swells longs ne doivent pas varier). Le volume de la
        /// recette est déjà combiné par l'appelant.
        /// </summary>
        public AudioSource PlayBankUI(string bankId, AudioClip clip, float volumeScale = 1f)
        {
            if (clip == null || string.IsNullOrWhiteSpace(bankId)) return null;
            var def = new SoundDefinition
            {
                Category = SoundCategory.Cinematic,
                Volume = 1f,
                PitchMin = 1f,
                PitchMax = 1f,
                Priority = 128,
                Loop = false,
                SpatialBlend = 0f
            };
            var src = Next2D(isUI: true);
            ConfigureAndPlay(src, def, clip, volumeScale, 1f, Vector3.zero, false, forceCategory: SoundCategory.UI);
            return src;
        }

        public void PlayAt(SoundId id, Vector3 worldPos, float volumeScale = 1f, float pitchScale = 1f)
        {
            var def = Resolve(id);
            if (!CheckCooldown(def)) return;
            var clip = PickClip(def);
            if (clip == null) return;
            // Sons UI et musique restent 2D même via PlayAt.
            bool is3D = def.SpatialBlend > 0.01f || def.Category == SoundCategory.SFX || def.Category == SoundCategory.Cinematic;
            var src = is3D ? Next3D() : Next2D();
            if (is3D) src.transform.position = worldPos;
            ConfigureAndPlay(src, def, clip, volumeScale, pitchScale, worldPos, is3D);
        }

        /// <summary>
        /// Résolution audio directe depuis la structure DamageResult officielle (Livre VI).
        /// Automatise les accents percussifs et l'élévation de tension des stems procéduraux.
        /// </summary>
        public void PlayCombatResult(DamageResult result, Vector3 atPos)
        {
            if (_generativeCombat && result.IsHit)
            {
                bool isKill = result.FatalResolution == FatalBlowResolution.InstantDeath;
                float strikeWeight = isKill ? 1.0f : (result.IsCritical ? 0.85f : (result.ExceededEncaissement ? 0.65f : 0.4f));
                NotifyCombatStrike(result.IsCritical, isKill, strikeWeight);
            }

            PlayCombatResult(
                result.IsHit,
                result.IsBlocked,
                result.IsCritical,
                result.ArmorAbsorbed > 0,
                result.ExceededEncaissement,
                result.FatalResolution != FatalBlowResolution.None ? result.FatalResolution.ToString() : null,
                atPos
            );
        }

        /// <summary>Helper combat legacy : mappe un DamageResult vers les bons SFX (appelé par l'arène).</summary>
        public void PlayCombatResult(bool isHit, bool isBlocked, bool isCritical, bool armorAbsorbed,
            bool shock, string fatalResolution, Vector3 atPos)
        {
            // Voie générative : chaque coup marquant injecte un accent dans la musique.
            if (_generativeCombat && isHit)
            {
                bool kill = !string.IsNullOrEmpty(fatalResolution) && fatalResolution != "None"
                    && fatalResolution != "MiracleSaved" && fatalResolution != "EligibleForLastBreath";
                NotifyCombatStrike(isCritical, kill);
            }
            if (!isHit && isBlocked) { PlayAt(SoundId.Defense_Parry, atPos); return; }
            if (!isHit) { PlayAt(SoundId.Attack_Miss, atPos); PlayAt(SoundId.Defense_Dodge, atPos, 0.6f); return; }

            PlayAt(isCritical ? SoundId.Attack_Impact_Crit : SoundId.Attack_Impact_Hit, atPos);
            PlayAt(SoundId.Impact_DeepBoom, atPos, isCritical ? 0.8f : 0.35f);
            if (armorAbsorbed) PlayAt(SoundId.Armor_Absorb, atPos, 0.7f);
            else PlayAt(isCritical ? SoundId.Hurt_Heavy : SoundId.Hurt_Light, atPos, 0.8f);
            if (shock) PlayAt(SoundId.Trauma_Shock, atPos, 0.8f);

            if (!string.IsNullOrEmpty(fatalResolution) && fatalResolution != "None")
            {
                switch (fatalResolution)
                {
                    case "InstantDeath": PlayAt(SoundId.Death_Instant, atPos); PlayAt(SoundId.KO_Fall, atPos, 0.8f); break;
                    case "MiracleSaved": PlayAt(SoundId.Miracle_Saved, atPos); break;
                    case "ForcedUnconscious": PlayAt(SoundId.KO_Fall, atPos); break;
                    case "EligibleForLastBreath": PlayAt(SoundId.LastBreath, atPos); break;
                }
            }
        }

        /// <summary>
        /// Séquence sonore complète d'une grenade : goupille -&gt; lancer (ou thump lanceur) -&gt;
        /// rebond -&gt; détonation (frag lourde / flash / fumée) + shrapnels. Appelé par l'arène/FX.
        /// kind : GrenadeKind brut ("Flash", "Fumigene", "Gaz", ...). heavy = gros calibre / thermobarique / plasma.
        /// </summary>
        public void PlayGrenadeThrow(Vector3 fromPos, bool withLauncher)
        {
            PlayAt(SoundId.Grenade_Pin, fromPos, 0.7f);
            if (withLauncher) PlayAt(SoundId.Launcher_Thump, fromPos, 1f);
            else PlayAt(SoundId.Grenade_Throw, fromPos, 0.8f);
        }

        public void NotifyGrenadeStrike(bool heavy)
        {
            if (_generativeCombat) NotifyCombatStrike(false, heavy, heavy ? 0.9f : 0.6f);
        }

        public void PlayGrenadeDetonation(Vector3 atPos, string kind, bool heavy)
        {
            string k = (kind ?? "").ToLowerInvariant();
            bool isFlash = k.Contains("flash") || k.Contains("stun") || k.Contains("sonique");
            bool isSmoke = k.Contains("fumi") || k.Contains("smoke");
            bool isGas = k.Contains("gaz") || k.Contains("gas");
            if (isFlash) { PlayAt(SoundId.Grenade_Flash, atPos, 1f); PlayAt(SoundId.Impact_DeepBoom, atPos, 0.4f); NotifyGrenadeStrike(false); return; }
            if (isSmoke || isGas) { PlayAt(SoundId.Grenade_Smoke, atPos, 1f); PlayAt(SoundId.Grenade_Gas, atPos, 0.8f); return; }
            PlayAt(heavy ? SoundId.Grenade_Explosion_Heavy : SoundId.Grenade_Explosion_Frag, atPos, 1f);
            PlayAt(SoundId.Grenade_Shrapnel, atPos, 0.7f);
            PlayAt(SoundId.Impact_DeepBoom, atPos, heavy ? 0.9f : 0.5f);
            NotifyGrenadeStrike(heavy);
        }

        // --- Musique adaptative ---
        /// <summary>Bascule d'humeur en conservant l'intensité courante (compat ascendante).</summary>
        public void PlayMusic(MusicMood mood, bool forceRestart = false)
        {
            float keep = _targetMood == mood ? _targetIntensity01 : 0.5f;
            PlayMusic(mood, keep, forceRestart);
        }

        public void PlayMusic(MusicMood mood, MusicIntensity level, bool forceRestart = false)
        {
            PlayMusic(mood, AdaptiveMusicDirector.IntensityTo01(level), forceRestart);
        }

        /// <summary>
        /// Cœur adaptatif : mood + intensité 0..1 (quantifiée en 3 variantes procédurales).
        /// Sans effet si même mood ET même variante, sauf forceRestart.
        /// </summary>
        public void PlayMusic(MusicMood mood, float intensity01, bool forceRestart = false)
        {
            if (mood != _targetMood || forceRestart)
            {
                _currentTrackIndex = 0;
                _combatTrackTimer = 0f;
            }
            PlayMusic(mood, _currentTrackIndex, intensity01, forceRestart);
        }

        public void PlayMusic(MusicMood mood, int trackIndex, float intensity01, bool forceRestart = false)
        {
            if (mood == MusicMood.None) { StopMusic(); return; }
            intensity01 = Mathf.Clamp01(intensity01);
            // Mode génératif ∞ : Combat / Boss / Tension = composition live, pas de chanson préfaite.
            if (_generativeCombat && IsGenerativeMood(mood))
            {
                StartGenerativeLoop(mood, intensity01, forceRestart);
                return;
            }
            int level = (int)AdaptiveMusicDirector.Quantize(intensity01);
            int trackCount = ProceduralAudioFactory.GetTrackCount(mood);
            trackIndex = Mathf.Clamp(trackIndex, 0, Mathf.Max(0, trackCount - 1));

            if (mood == _targetMood && trackIndex == _currentTrackIndex && level == _currentLevel && !forceRestart)
            {
                _targetIntensity01 = intensity01;
                return;
            }

            bool isSameTrackIntensityShift = (mood == _targetMood && trackIndex == _currentTrackIndex && !forceRestart);

            _targetMood = mood;
            _currentTrackIndex = trackIndex;
            _targetIntensity01 = intensity01;
            _lastAdaptiveSwitchTime = Time.unscaledTime;
            // Nouvelle piste combat => on repart sur un cycle complet de 48s.
            // Sans ce reset, un clic manuel sur "Piste 1" juste avant l'échéance
            // se faisait voler aussitôt par la rotation auto (impression "piste 1 ne joue pas").
            if (mood == MusicMood.Combat)
                _combatTrackTimer = 0f;

            if (_musicRoutine != null) StopCoroutine(_musicRoutine);
            _musicRoutine = StartCoroutine(CrossfadeMusicRoutine(mood, trackIndex, level, intensity01, isSameTrackIntensityShift));
        }

        public void NextCombatTrack()
        {
            // En génératif, "piste suivante" = variation inédite immédiate.
            if (_generativeCombat && IsGenerativeMood(_targetMood))
            {
                RequestGenerativeVariation();
                return;
            }
            if (_targetMood != MusicMood.Combat) return;
            int count = ProceduralAudioFactory.GetTrackCount(MusicMood.Combat);
            if (count <= 1) return;

            _combatTrackTimer = 0f;
            int next = (_currentTrackIndex + 1) % count;
            PlayMusic(MusicMood.Combat, next, _targetIntensity01, forceRestart: true);
        }

        // =====================================================================
        // MODE GÉNÉRATIF ∞ — composition live sans chanson préfaite
        // =====================================================================
        public static bool IsGenerativeMood(MusicMood mood)
            => mood == MusicMood.Combat || mood == MusicMood.CombatBoss || mood == MusicMood.Tension;

        /// <summary>Active/coupe le mode génératif. Les chansons préfaites restent intactes.</summary>
        public void SetGenerativeMode(bool on, int seed = -1)
        {
            bool wasOn = _generativeCombat;
            _generativeCombat = on;
            if (seed >= 0) _generativeSeed = seed;
            if (on && !wasOn)
            {
                _genGeneration = 0;
                _genParamsValid = false;
                _genLoopTimer = 0f;
                _pendingStrike = 0f;
                // Rebranche immédiatement le mood courant en génératif s'il s'y prête.
                if (IsGenerativeMood(_targetMood))
                    StartGenerativeLoop(_targetMood, _targetIntensity01, forceRestart: true);
            }
            else if (!on && wasOn)
            {
                _genParamsValid = false;
                _genLoopTimer = 0f;
                _pendingStrike = 0f;
                // Retour aux chansons préfaites sur le mood courant.
                if (IsGenerativeMood(_targetMood))
                    PlayMusic(_targetMood, _currentTrackIndex, _targetIntensity01, forceRestart: true);
            }
        }

        public void SetGenerativeColor(float brightness01, float chaos01)
        {
            _generativeBrightness = Mathf.Clamp01(brightness01);
            _generativeChaos = Mathf.Clamp01(chaos01);
        }

        /// <summary>Impose un style au moteur génératif (Auto = défaut actuel).
        /// Changer de style régénère aussitôt une variation dans le nouveau style.</summary>
        public void SetGenerativePreset(GenerativeStylePreset preset)
        {
            if (_generativePreset == preset) return;
            _generativePreset = preset;
            if (_generativeCombat && IsGenerativeMood(_targetMood))
                RequestGenerativeVariation();
        }

        /// <summary>
        /// Accent réactif : à appeler sur coup critique / kill / gros dégât.
        /// Le sub-drop + taiko sont cousés dans la variation SUIVANTE (barre 4),
        /// donc le coup "colle" musicalement au lieu de juste empiler un SFX.
        /// </summary>
        public void NotifyCombatStrike(bool critical, bool kill, float amount01 = -1f)
        {
            float s = amount01 >= 0f ? Mathf.Clamp01(amount01) : (kill ? 1f : (critical ? 0.75f : 0.4f));
            _pendingStrike = Mathf.Max(_pendingStrike, s);
        }

        /// <summary>Force immédiatement une variation inédite (bouton UI / rotation).</summary>
        public void RequestGenerativeVariation()
        {
            if (!IsGenerativeMood(_targetMood))
            {
                PlayMusic(MusicMood.Combat, _targetIntensity01, forceRestart: true);
                return;
            }
            _genGeneration++;
            float strike = _pendingStrike;
            _pendingStrike = 0f;
            _genParams = AdaptiveMusicDirector.ComputeGenerativeParams(
                _lastAllyHp, _lastAllies, _lastEnemies, _lastTurn,
                _generativeSeed, _genGeneration, strike, _generativeBrightness, _generativeChaos, _generativePreset);
            _genParams.stemOverrides = _stemOverrides;
            _genParamsValid = true;
            _genLoopDur = ProceduralAudioFactory.EstimateGenerativeDuration(_genParams);
            _genLoopTimer = 0f;
            _targetIntensity01 = _genParams.intensity01;
            _lastAdaptiveSwitchTime = Time.unscaledTime;
            AudioClip clip;
            try { clip = ProceduralAudioFactory.GetGenerativeLoop(_genParams); }
            catch (System.Exception ex)
            {
                Debug.LogError($"[Audio] Génératif G{_genGeneration} impossible : {ex.GetType().Name}: {ex.Message}");
                return;
            }
            if (_musicRoutine != null) StopCoroutine(_musicRoutine);
            _musicRoutine = StartCoroutine(CrossfadeClipRoutine(clip, _targetMood, _genGeneration, _genParams.intensity01));
            Debug.Log($"[Audio] ∞ Génératif {_targetMood} G{_genGeneration} : {clip.samples} spl ({clip.length:F1}s) bpm={_genParams.bpm:0} sty={_genParams.drumStyle} gam={_genParams.scale} int={_genParams.intensity01:F2} ten={_genParams.tension01:F2} strike={strike:F2}");
        }

        private void StartGenerativeLoop(MusicMood mood, float intensity01, bool forceRestart)
        {
            bool sameMood = (mood == _targetMood);
            _targetMood = mood;
            _targetIntensity01 = Mathf.Clamp01(intensity01);
            if (!sameMood || !_genParamsValid || forceRestart)
            {
                if (!sameMood || forceRestart) { _genGeneration = forceRestart && sameMood ? _genGeneration + 1 : 0; }
                float strike = _pendingStrike;
                _pendingStrike = 0f;
                _genParams = AdaptiveMusicDirector.ComputeGenerativeParams(
                    _lastAllyHp, _lastAllies, _lastEnemies, _lastTurn,
                    _generativeSeed, _genGeneration, strike, _generativeBrightness, _generativeChaos, _generativePreset);
                _genParams.stemOverrides = _stemOverrides;
                // L'intensité de référence vient de l'état tactique, pas du paramètre d'appel,
                // pour que le BPM/gamme suivent le vrai danger (l'appel ne donne qu'une base).
                if (!sameMood && _genParamsValid) { /* garde la continuité */ }
                _genParamsValid = true;
                _genLoopDur = ProceduralAudioFactory.EstimateGenerativeDuration(_genParams);
                _genLoopTimer = 0f;
                _targetIntensity01 = _genParams.intensity01;
                _currentLevel = (int)AdaptiveMusicDirector.Quantize(_targetIntensity01);
                _lastAdaptiveSwitchTime = Time.unscaledTime;
                AudioClip clip;
                try { clip = ProceduralAudioFactory.GetGenerativeLoop(_genParams); }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[Audio] Génératif {mood} G{_genGeneration} impossible : {ex.GetType().Name}: {ex.Message}");
                    return;
                }
                if (_musicRoutine != null) StopCoroutine(_musicRoutine);
                _musicRoutine = StartCoroutine(CrossfadeClipRoutine(clip, mood, _genGeneration, _genParams.intensity01));
                Debug.Log($"[Audio] ∞ Génératif {mood} G{_genGeneration} : {clip.samples} spl ({clip.length:F1}s) bpm={_genParams.bpm:0} sty={_genParams.drumStyle} gam={_genParams.scale}");
            }
            else
            {
                _targetIntensity01 = Mathf.Clamp01(intensity01);
            }
        }

        private void AdvanceGenerativeVariation()
        {
            // Garde-fou : une seule transition à la fois.
            if (_isFading) return;
            _genGeneration++;
            float strike = _pendingStrike;
            _pendingStrike = 0f;
            _genParams = AdaptiveMusicDirector.ComputeGenerativeParams(
                _lastAllyHp, _lastAllies, _lastEnemies, _lastTurn,
                _generativeSeed, _genGeneration, strike, _generativeBrightness, _generativeChaos, _generativePreset);
            _genParams.stemOverrides = _stemOverrides;
            _genLoopDur = ProceduralAudioFactory.EstimateGenerativeDuration(_genParams);
            _genLoopTimer = 0f;
            _targetIntensity01 = _genParams.intensity01;
            _currentLevel = (int)AdaptiveMusicDirector.Quantize(_targetIntensity01);
            _lastAdaptiveSwitchTime = Time.unscaledTime;
            AudioClip clip;
            try { clip = ProceduralAudioFactory.GetGenerativeLoop(_genParams); }
            catch (System.Exception ex)
            {
                Debug.LogError($"[Audio] Génératif advance G{_genGeneration} impossible : {ex.GetType().Name}: {ex.Message}");
                return;
            }
            if (_musicRoutine != null) StopCoroutine(_musicRoutine);
            _musicRoutine = StartCoroutine(CrossfadeClipRoutine(clip, _targetMood, _genGeneration, _genParams.intensity01));
        }

        public void StopMusic()
        {
            _targetMood = MusicMood.None;
            _currentMood = MusicMood.None;
            _currentTrackIndex = 0;
            _combatTrackTimer = 0f;
            _genLoopTimer = 0f;
            _genParamsValid = false;
            _pendingStrike = 0f;
            _currentLevel = 1;
            _targetIntensity01 = 0.5f;
            _currentIntensity01 = 0.5f;
            _activeMusic = null;
            _isFading = false;
            if (_musicRoutine != null) StopCoroutine(_musicRoutine);
            if (_musicA != null) _musicA.Stop();
            if (_musicB != null) _musicB.Stop();
        }

        /// <summary>Stinger superposé (transition dramatique) : ne coupe pas la boucle. Anti-spam 1s.</summary>
        public void PlayStinger(MusicMood mood, float volumeScale = 1f)
        {
            if (mood == MusicMood.None) return;
            if (Time.unscaledTime - _lastStingerTime < 1f) return;
            var clip = ProceduralAudioFactory.GetStinger(mood);
            if (clip == null) return;
            _lastStingerTime = Time.unscaledTime;
            var src = Next2D();
            src.Stop();
            src.clip = clip;
            src.loop = false;
            src.spatialBlend = 0f;
            src.priority = 64;
            // Stinger musical : jamais ralenti, même en pleine cinématique.
            src.pitch = 1f;
            src.volume = (_muted ? 0f : 1f) * _masterVolume * _musicVolume * _stingerVolume * volumeScale;
            src.Play();
        }

        /// <summary>
        /// Pilotage automatique depuis l'état tactique (appelé sur événements : tour, dégâts, reset).
        /// Hystérésis + cooldown anti-yoyo ; les changements de mood restent immédiats.
        /// Retourne true si une transition a démarré.
        /// </summary>
        public bool NotifyBattleState(float allyHpRatio01, int alliesAlive, int enemiesAlive, int turnIndex)
        {
            _lastAllyHp = Mathf.Clamp01(allyHpRatio01);
            _lastAllies = alliesAlive;
            _lastEnemies = enemiesAlive;
            _lastTurn = turnIndex;
            var (mood, intensity) = AdaptiveMusicDirector.ComputeTarget(
                allyHpRatio01, alliesAlive, enemiesAlive, turnIndex, _targetMood, _targetIntensity01);
            // Mode génératif : le mood Combat/Boss/Tension part en composition live.
            if (_generativeCombat && IsGenerativeMood(mood))
            {
                bool moodChange = mood != _targetMood;
                var genParams = AdaptiveMusicDirector.ComputeGenerativeParams(
                    allyHpRatio01, alliesAlive, enemiesAlive, turnIndex,
                    _generativeSeed, _genGeneration, 0f, _generativeBrightness, _generativeChaos, _generativePreset);
                bool bigShift = Mathf.Abs(genParams.intensity01 - _targetIntensity01) >= _adaptiveHysteresis
                    || Mathf.Abs(genParams.tension01 - (_genParamsValid ? _genParams.tension01 : 0.5f)) >= 0.25f;
                if (!moodChange && !bigShift && _genParamsValid) return false;
                if (!moodChange && Time.unscaledTime - _lastAdaptiveSwitchTime < _adaptiveCooldown && _genParamsValid) return false;
                StartGenerativeLoop(mood, genParams.intensity01, forceRestart: moodChange || bigShift);
                return true;
            }
            bool legacyMoodChange = mood != _targetMood;
            bool legacyShift = Mathf.Abs(intensity - _targetIntensity01) >= _adaptiveHysteresis;
            if (!legacyMoodChange && !legacyShift) return false;
            if (!legacyMoodChange && Time.unscaledTime - _lastAdaptiveSwitchTime < _adaptiveCooldown) return false;
            PlayMusic(mood, intensity);
            return true;
        }

        public void StartAmbience(float volumeScale = 1f)
        {
            if (_ambienceSource == null) return;
            if (_ambienceSource.isPlaying) return;
            // Nappe procédurale 6s bouclable (vent + grave), sans clic.
            _ambienceSource.clip = ProceduralAudioFactory.GetAmbienceLoop();
            _ambienceSource.loop = true;
            _ambienceSource.volume = _ambienceVolume * volumeScale * _masterVolume;
            _ambienceSource.Play();
        }

        public void StopAmbience()
        {
            if (_ambienceSource != null && _ambienceSource.isPlaying)
            {
                _ambienceSource.Stop();
            }
        }

        public void PlaySceneAmbience(string instanceId, string cueOrPresetId, float volumeScale, float fadeInDuration, bool spatial, Vector3 worldPos, float radius)
        {
            if (string.IsNullOrWhiteSpace(instanceId)) return;
            string key = instanceId.Trim();

            AudioClip clip = null;
            var preset = AmbienceCatalog.Find(cueOrPresetId);
            string soundCue = preset != null ? preset.soundCueId : cueOrPresetId;

            var bankEntry = SoundBankCatalog.Find(soundCue);
            if (bankEntry != null)
            {
                clip = ProceduralAudioFactory.GetBankClip(bankEntry, bankEntry.duration > 0.05f ? bankEntry.duration : 6f);
            }
            if (clip == null && System.Enum.TryParse<SoundId>(soundCue, out var sEnum))
            {
                clip = ProceduralAudioFactory.GetClip(sEnum);
            }
            if (clip == null)
            {
                clip = ProceduralAudioFactory.GetAmbienceLoop();
            }

            if (!_activeSceneAmbienceSources.TryGetValue(key, out var src) || src == null)
            {
                var go = new GameObject($"[AmbienceTrack] {key}");
                go.transform.SetParent(transform, false);
                src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.loop = true;
                src.dopplerLevel = 0f;
                _activeSceneAmbienceSources[key] = src;
            }

            src.clip = clip;
            src.spatialBlend = spatial ? 1f : 0f;
            if (spatial)
            {
                src.rolloffMode = AudioRolloffMode.Linear;
                src.minDistance = Mathf.Max(1.5f, radius * 0.25f);
                src.maxDistance = Mathf.Max(radius, 4f);
                src.transform.position = worldPos;
            }

            float targetVol = (_muted ? 0f : 1f) * _masterVolume * _ambienceVolume * volumeScale;

            if (_activeSceneAmbienceFades.TryGetValue(key, out var activeRoutine) && activeRoutine != null)
            {
                StopCoroutine(activeRoutine);
            }

            if (!src.isPlaying)
            {
                src.volume = 0f;
                src.Play();
            }

            _activeSceneAmbienceFades[key] = StartCoroutine(FadeSceneAmbienceRoutine(key, src, targetVol, Mathf.Max(0.05f, fadeInDuration), false));
        }

        public void StopSceneAmbience(string instanceId, float fadeOutDuration)
        {
            if (string.IsNullOrWhiteSpace(instanceId)) return;
            string key = instanceId.Trim();
            if (!_activeSceneAmbienceSources.TryGetValue(key, out var src) || src == null) return;

            if (_activeSceneAmbienceFades.TryGetValue(key, out var activeRoutine) && activeRoutine != null)
            {
                StopCoroutine(activeRoutine);
            }

            _activeSceneAmbienceFades[key] = StartCoroutine(FadeSceneAmbienceRoutine(key, src, 0f, Mathf.Max(0.05f, fadeOutDuration), true));
        }

        public void StopAllSceneAmbiences(float fadeOutDuration = 0.5f)
        {
            var keys = new List<string>(_activeSceneAmbienceSources.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                StopSceneAmbience(keys[i], fadeOutDuration);
            }
        }

        private IEnumerator FadeSceneAmbienceRoutine(string key, AudioSource src, float targetVol, float duration, bool destroyOnComplete)
        {
            if (src == null) yield break;
            float startVol = src.volume;
            float elapsed = 0f;

            while (elapsed < duration && src != null)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                src.volume = Mathf.Lerp(startVol, targetVol, t);
                yield return null;
            }

            if (src != null)
            {
                src.volume = targetVol;
                if (destroyOnComplete)
                {
                    src.Stop();
                    _activeSceneAmbienceSources.Remove(key);
                    _activeSceneAmbienceFades.Remove(key);
                    Destroy(src.gameObject);
                }
            }
        }

        // --- Snapshots code ---
        public void EnterCinematicMode()
        {
            // Déjà en cinématique (chaîne Fin→ sans Exit entre les deux) :
            // on maintient le duck sans rejouer le whoosh de transition, sinon
            // il coupe la continuité (ex : swell warp → approche).
            bool already = _cinematicDuck > 0.5f;
            _cinematicDuck = 1f;
            if (already) return;
            // Uniquement le whoosh d'air : le sweep SlowMo_Enter (800->120 Hz)
            // joué pendant le zoom-in sonnait comme un doppler/compression.
            Play(SoundId.Cinematic_WhooshIn, 0.8f);
        }

        public void ExitCinematicMode()
        {
            _cinematicDuck = 0f;
            // Idem en sortie : WhooshOut seul, sans sweep SlowMo_Exit.
            Play(SoundId.Cinematic_WhooshOut, 0.7f);
        }

        public void EnterPauseMode()
        {
            _pauseDuck = 1f;
            ApplyAllVolumes();
        }

        public void ExitPauseMode()
        {
            _pauseDuck = 0f;
            ApplyAllVolumes();
        }

        // --- Volumes ---
        /// <summary>
        /// Règle un volume. Si save=false, ne touche pas aux PlayerPrefs
        /// (réservé aux ducks temporaires, ex. Caméléon qui isole la musique :
        /// on ne doit jamais persister le 0 temporaire, sinon la musique
        /// reste à 0% au redémarrage suivant).
        /// </summary>
        public void SetCategoryVolume(SoundCategory cat, float v01, bool save = true)
        {
            v01 = Mathf.Clamp01(v01);
            switch (cat)
            {
                case SoundCategory.Master: _masterVolume = v01; if (save) PlayerPrefs.SetFloat(PP_MASTER, v01); break;
                case SoundCategory.Music: _musicVolume = v01; if (save) PlayerPrefs.SetFloat(PP_MUSIC, v01); break;
                case SoundCategory.Ambience: _ambienceVolume = v01; if (save) PlayerPrefs.SetFloat(PP_AMB, v01); break;
                case SoundCategory.SFX: _sfxVolume = v01; if (save) PlayerPrefs.SetFloat(PP_SFX, v01); break;
                case SoundCategory.UI: _uiVolume = v01; if (save) PlayerPrefs.SetFloat(PP_UI, v01); break;
                case SoundCategory.Cinematic: _cinematicVolume = v01; break;
            }
            if (save) PlayerPrefs.Save();
            ApplyAllVolumes();
        }

        public float GetCategoryVolume(SoundCategory cat)
        {
            return cat switch
            {
                SoundCategory.Master => _masterVolume,
                SoundCategory.Music => _musicVolume,
                SoundCategory.Ambience => _ambienceVolume,
                SoundCategory.SFX => _sfxVolume,
                SoundCategory.UI => _uiVolume,
                SoundCategory.Cinematic => _cinematicVolume,
                _ => 1f
            };
        }

        public void SetMuted(bool muted)
        {
            _muted = muted;
            ApplyAllVolumes();
        }

        // =====================================================================
        // Internes
        // =====================================================================
        private void BuildPools()
        {
            var root2D = new GameObject("Pool2D") { hideFlags = HideFlags.DontSave };
            root2D.transform.SetParent(transform, false);
            for (int i = 0; i < _pool2DSize; i++)
            {
                var src = root2D.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 0f;
                src.dopplerLevel = 0f;
                _pool2D.Add(src);
            }

            var root3D = new GameObject("Pool3D") { hideFlags = HideFlags.DontSave };
            root3D.transform.SetParent(transform, false);
            for (int i = 0; i < _pool3DSize; i++)
            {
                var go = new GameObject($"SFX3D_{i:00}");
                go.transform.SetParent(root3D.transform, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 0.35f;
                src.rolloffMode = AudioRolloffMode.Linear;
                src.minDistance = 18f;
                src.maxDistance = 85f;
                src.dopplerLevel = 0f;
                _pool3D.Add(src);
            }

            var musicGo = new GameObject("Music") { hideFlags = HideFlags.DontSave };
            musicGo.transform.SetParent(transform, false);
            _musicA = musicGo.AddComponent<AudioSource>();
            _musicB = musicGo.AddComponent<AudioSource>();
            foreach (var m in new[] { _musicA, _musicB })
            {
                m.playOnAwake = false;
                m.loop = true;
                m.spatialBlend = 0f;
                m.dopplerLevel = 0f;
                m.volume = 0f;
            }

            var ambGo = new GameObject("Ambience");
            ambGo.transform.SetParent(transform, false);
            _ambienceSource = ambGo.AddComponent<AudioSource>();
            _ambienceSource.playOnAwake = false;
            _ambienceSource.loop = true;
            _ambienceSource.spatialBlend = 0f;
            _ambienceSource.volume = 0.4f;
        }

        private AudioSource Next2D(bool isUI = false)
        {
            for (int i = 0; i < _pool2D.Count; i++)
            {
                var s = _pool2D[(_cursor2D + i) % _pool2D.Count];
                if (!s.isPlaying)
                {
                    _cursor2D = (_cursor2D + i + 1) % _pool2D.Count;
                    s.ignoreListenerPause = isUI;
                    return s;
                }
            }
            var src = _pool2D[_cursor2D % _pool2D.Count];
            _cursor2D++;
            src.ignoreListenerPause = isUI;
            return src;
        }

        private AudioSource Next3D()
        {
            for (int i = 0; i < _pool3D.Count; i++)
            {
                var s = _pool3D[(_cursor3D + i) % _pool3D.Count];
                if (!s.isPlaying)
                {
                    _cursor3D = (_cursor3D + i + 1) % _pool3D.Count;
                    return s;
                }
            }
            var src = _pool3D[_cursor3D % _pool3D.Count];
            _cursor3D++;
            return src;
        }

        public static SoundDefinition CreateDefaultDefinition(SoundId id)
        {
            var def = new SoundDefinition { Id = id };
            int code = (int)id;
            if (code >= 100 && code < 200) { def.Category = SoundCategory.UI; def.Volume = 0.65f; def.Cooldown = 0.03f; def.SpatialBlend = 0f; }
            else if (code >= 200 && code < 300) { def.Category = SoundCategory.SFX; def.Volume = 0.85f; def.Cooldown = 0.06f; def.SpatialBlend = 0.35f; }
            else if (code >= 300 && code < 380) { def.Category = SoundCategory.SFX; def.Volume = 1.0f; def.Cooldown = 0.01f; def.SpatialBlend = 0.35f; }
            else if (code >= 380 && code < 400) { def.Category = SoundCategory.Cinematic; def.Volume = 0.95f; def.Cooldown = 0.1f; def.SpatialBlend = 0f; }
            else { def.Category = SoundCategory.SFX; def.Volume = 0.85f; def.Cooldown = 0.04f; def.SpatialBlend = 0.35f; }

            if (id == SoundId.Attack_Impact_Crit || id == SoundId.Impact_DeepBoom || id == SoundId.Death_Instant)
                def.Volume = 1.15f;
            if (id == SoundId.UI_Hover || id == SoundId.Move_PathTick)
                def.Cooldown = 0.04f;
            return def;
        }

        private SoundDefinition Resolve(SoundId id)
        {
            var fromBank = _bank != null ? _bank.Get(id) : null;
            if (fromBank != null) return fromBank;
            return GetCachedDefault(id);
        }

        private static readonly Dictionary<SoundId, SoundDefinition> _defaultCache = new();

        private static SoundDefinition GetCachedDefault(SoundId id)
        {
            if (_defaultCache.TryGetValue(id, out var def) && def != null) return def;
            def = CreateDefaultDefinition(id);
            _defaultCache[id] = def;
            return def;
        }

        private static AudioClip PickClip(SoundDefinition def)
        {
            if (def.Clips != null && def.Clips.Count > 0)
            {
                // Pioche aléatoire parmi les overrides, ignore les slots vides.
                for (int tries = 0; tries < 3; tries++)
                {
                    var c = def.Clips[Random.Range(0, def.Clips.Count)];
                    if (c != null) return c;
                }
            }
            return ProceduralAudioFactory.GetClip(def.Id);
        }

        private bool CheckCooldown(SoundDefinition def)
        {
            if (def.Cooldown <= 0f) return true;
            float now = Time.unscaledTime;
            if (_lastPlayTime.TryGetValue(def.Id, out float last) && now - last < def.Cooldown)
                return false;
            _lastPlayTime[def.Id] = now;
            return true;
        }

        private void ConfigureAndPlay(AudioSource src, SoundDefinition def, AudioClip clip,
            float volumeScale, float pitchScale, Vector3 pos, bool is3D, SoundCategory? forceCategory = null)
        {
            var cat = forceCategory ?? def.Category;
            src.Stop();
            src.clip = clip;
            src.loop = def.Loop;
            src.priority = def.Priority;
            src.spatialBlend = is3D ? Mathf.Clamp(def.SpatialBlend, 0.20f, 0.45f) : 0f;
            if (is3D)
            {
                src.minDistance = 18f;
                src.maxDistance = Mathf.Max(def.MaxDistance, 85f);
                src.transform.position = pos;
            }

            float catVol = CategoryVolume(cat);
            float duck = cat == SoundCategory.Music ? (1f - 0.5f * _pauseDuck)
                       : cat == SoundCategory.Ambience ? (1f - 0.4f * _cinematicDuck)
                       : 1f;
            src.volume = (_muted ? 0f : 1f) * _masterVolume * catVol * def.Volume * volumeScale * Mathf.Max(0f, duck);

            float pitchJitter = Random.Range(def.PitchMin, def.PitchMax);
            float slowFactor = (cat == SoundCategory.Music || cat == SoundCategory.Ambience || cat == SoundCategory.UI || cat == SoundCategory.Cinematic) ? 1f
                : Mathf.Lerp(1f, _slowMoSfxPitch, _slowMoWeight);
            // En pause (timeScale ~0) on garde un pitch valide : AudioListener.pause gère le silence.
            src.pitch = Mathf.Clamp(pitchScale * pitchJitter * slowFactor, 0.1f, 3f);
            src.Play();
        }

        private float CategoryVolume(SoundCategory cat)
        {
            return cat switch
            {
                SoundCategory.Music => _musicVolume,
                SoundCategory.Ambience => _ambienceVolume,
                SoundCategory.SFX => _sfxVolume,
                SoundCategory.UI => _uiVolume,
                SoundCategory.Cinematic => _cinematicVolume,
                _ => 1f
            };
        }

        private IEnumerator CrossfadeMusicRoutine(MusicMood mood, int trackIndex, int level, float intensity01, bool syncPhaseWithPrevious = false)
        {
            AudioClip clip = null;
            try
            {
                clip = ProceduralAudioFactory.GetMusicLoop(mood, trackIndex, level);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[Audio] GetMusicLoop({mood} T{trackIndex} L{level}) a levé : {ex.GetType().Name}: {ex.Message}");
                _isFading = false;
                yield break;
            }
            if (clip == null)
            {
                Debug.LogError($"[Audio] Boucle introuvable : {mood} T{trackIndex} L{level} (piste silencieuse).");
                _isFading = false;
                yield break;
            }

            var fadeIn = _musicFlip ? _musicA : _musicB;
            var fadeOut = _musicFlip ? _musicB : _musicA;
            if (fadeIn == null)
            {
                Debug.LogError("[Audio] Source musique fadeIn manquante (BuildPools non appelé ?).");
                _isFading = false;
                yield break;
            }

            _isFading = true;
            _currentMood = mood;
            _currentLevel = level;
            _currentIntensity01 = intensity01;
            _musicFlip = !_musicFlip;

            // Setup sans yield => try/catch autorisé (une exception ici ne doit
            // jamais laisser _isFading bloqué à true, sinon la rotation s'arrête).
            try
            {
                fadeIn.clip = clip;
                fadeIn.loop = true;
                // Boucle musicale : pitch toujours 1 (pas de ralenti cinématique).
                fadeIn.pitch = 1f;
                fadeIn.volume = 0f;
                fadeIn.Play();

                if (syncPhaseWithPrevious)
                {
                    try
                    {
                        if (fadeOut != null && fadeOut.isPlaying && fadeOut.clip != null && fadeOut.time > 0.05f)
                            fadeIn.time = fadeOut.time % clip.length;
                    }
                    catch (System.Exception) { /* WebGL-safe : ignore le seek si refusé */ }
                }
                else
                {
                    // WebGL-safe : le seek immédiat après Play() peut être refusé.
                    try { fadeIn.time = 0f; }
                    catch (System.Exception) { }
                }

                _activeMusic = fadeIn;
                // Diagnostic "piste 1 muette" : la 2e lecture de T0 rendait un clip
                // à 0 samples côté Unity alors que la synthèse est saine hors Unity.
                // On sonde les données réelles pour trancher cache vide vs données zéros.
                float probePeak = -1f;
                string loadState = "?";
                try
                {
                    loadState = clip.loadState == AudioDataLoadState.Loaded ? "ready" : clip.loadState.ToString().ToLowerInvariant();
                    int probeN = System.Math.Min(2048, clip.samples);
                    if (probeN > 0)
                    {
                        float[] probe = new float[probeN];
                        if (clip.GetData(probe, 0))
                        {
                            float pk = 0f;
                            for (int i = 0; i < probe.Length; i++)
                            {
                                float a = System.Math.Abs(probe[i]);
                                if (a > pk) pk = a;
                            }
                            probePeak = pk;
                        }
                        else probePeak = -2f; // GetData refusé (données déchargées ?)
                    }
                    else probePeak = 0f;
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[Audio] Sonde {mood} T{trackIndex} L{level} impossible : {ex.GetType().Name}: {ex.Message}");
                }
                Debug.Log($"[Audio] ▶ {mood} piste {trackIndex + 1} L{level} : clip='{clip.name}' {clip.samples} spl ({clip.length:F1}s) load={loadState} peak1k={probePeak:F3} src={(fadeIn == _musicA ? "A" : "B")} volMus={_musicVolume:F2} volMast={_masterVolume:F2} mute={_muted}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[Audio] Démarrage {mood} T{trackIndex} L{level} impossible : {ex.GetType().Name}: {ex.Message}");
                _isFading = false;
                ApplyAllVolumes();
                yield break;
            }

            float t = 0f;
            bool isFinale = mood == MusicMood.Victory || mood == MusicMood.Defeat;
            float dur = isFinale ? Mathf.Max(_musicCrossfade, _finaleCrossfade) : Mathf.Max(0.4f, _musicCrossfade);

            // Boucle de fondu avec yield => try/finally uniquement (pas de catch,
            // sinon CS1626 : yield interdit dans un try avec catch).
            try
            {
                while (t < dur)
                {
                    t += Time.unscaledDeltaTime;
                    float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / dur));
                    float gIn = Mathf.Sin(k * Mathf.PI * 0.5f);
                    float gOut = Mathf.Cos(k * Mathf.PI * 0.5f);
                    float targetVol = _musicVolume * _masterVolume;
                    float duck = Mathf.Max(0f, 1f - 0.5f * _pauseDuck);
                    float mute = _muted ? 0f : 1f;
                    fadeIn.volume = mute * targetVol * gIn * duck;
                    if (fadeOut != null) fadeOut.volume = mute * targetVol * gOut * duck;
                    yield return null;
                }

                if (fadeOut != null)
                {
                    fadeOut.Stop();
                    fadeOut.volume = 0f;
                }
            }
            finally
            {
                _isFading = false;
                ApplyAllVolumes();
            }
        }

        /// <summary>
        /// Crossfade vers un clip déjà généré (voie générative ∞).
        /// Même courbe que la voie legacy, sans re-résolution SoundBank.
        /// </summary>
        private IEnumerator CrossfadeClipRoutine(AudioClip clip, MusicMood mood, int generation, float intensity01)
        {
            if (clip == null)
            {
                Debug.LogError($"[Audio] Clip génératif null : {mood} G{generation} (piste silencieuse).");
                _isFading = false;
                yield break;
            }
            var fadeIn = _musicFlip ? _musicA : _musicB;
            var fadeOut = _musicFlip ? _musicB : _musicA;
            if (fadeIn == null)
            {
                Debug.LogError("[Audio] Source musique fadeIn manquante (BuildPools non appelé ?).");
                _isFading = false;
                yield break;
            }
            _isFading = true;
            _currentMood = mood;
            _currentIntensity01 = intensity01;
            _currentLevel = (int)AdaptiveMusicDirector.Quantize(intensity01);
            _musicFlip = !_musicFlip;
            try
            {
                fadeIn.clip = clip;
                fadeIn.loop = true;
                fadeIn.pitch = 1f;
                fadeIn.volume = 0f;
                fadeIn.Play();
                try { fadeIn.time = 0f; }
                catch (System.Exception) { }
                _activeMusic = fadeIn;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[Audio] Démarrage génératif {mood} G{generation} impossible : {ex.GetType().Name}: {ex.Message}");
                _isFading = false;
                ApplyAllVolumes();
                yield break;
            }
            float t = 0f;
            float dur = Mathf.Max(0.4f, _generativeCrossfade);
            try
            {
                while (t < dur)
                {
                    t += Time.unscaledDeltaTime;
                    float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / dur));
                    float gIn = Mathf.Sin(k * Mathf.PI * 0.5f);
                    float gOut = Mathf.Cos(k * Mathf.PI * 0.5f);
                    float targetVol = _musicVolume * _masterVolume;
                    float duck = Mathf.Max(0f, 1f - 0.5f * _pauseDuck);
                    float mute = _muted ? 0f : 1f;
                    fadeIn.volume = mute * targetVol * gIn * duck;
                    if (fadeOut != null) fadeOut.volume = mute * targetVol * gOut * duck;
                    yield return null;
                }
                if (fadeOut != null)
                {
                    fadeOut.Stop();
                    fadeOut.volume = 0f;
                }
            }
            finally
            {
                _isFading = false;
                ApplyAllVolumes();
            }
        }

        /// <summary>
        /// Preuve visuelle anti-doute : nom + état des 2 seules sources du bus
        /// musique. En génératif on doit y voir UN clip GEN_* qui joue et
        /// l'autre source à "stop" (hors le ~1s de fondu entre variations).
        /// Legacy et génératif partagent ce bus : ils ne peuvent pas coexister.
        /// </summary>
        public string GetMusicSourcesDebug()
        {
            return $"A:{SrcTag(_musicA)}  B:{SrcTag(_musicB)}";
        }

        private static string SrcTag(AudioSource s)
        {
            if (s == null) return "-";
            try
            {
                if (!s.isPlaying) return "stop";
                string n = s.clip != null ? s.clip.name : "?";
                if (n.StartsWith("PROC_")) n = n.Substring(5);
                if (n.Length > 24) n = n.Substring(0, 24) + "…";
                return $"{n} v={s.volume:0.0}";
            }
            catch (System.Exception) { return "?"; }
        }

        private void LoadVolumes()
        {
            _masterVolume = PlayerPrefs.GetFloat(PP_MASTER, _masterVolume);
            _musicVolume = PlayerPrefs.GetFloat(PP_MUSIC, _musicVolume);
            _ambienceVolume = PlayerPrefs.GetFloat(PP_AMB, _ambienceVolume);
            _sfxVolume = PlayerPrefs.GetFloat(PP_SFX, _sfxVolume);
            _uiVolume = PlayerPrefs.GetFloat(PP_UI, _uiVolume);
        }

        private void ApplyAllVolumes()
        {
            if (_mixer != null)
            {
                // Paramètres exposés en dB : 0dB = 1.0, -40dB = quasi muet.
                SetMixerDb(_masterParam, _masterVolume);
                SetMixerDb(_musicParam, _musicVolume * (1f - 0.5f * _pauseDuck));
                SetMixerDb(_sfxParam, _sfxVolume);
                SetMixerDb(_uiParam, _uiVolume);
                SetMixerDb(_ambienceParam, _ambienceVolume);
            }
            if (_ambienceSource != null)
                _ambienceSource.volume = (_muted ? 0f : 1f) * _ambienceVolume * _masterVolume * 0.7f;
            // La boucle musicale active suit les sliders/mute en direct (hors crossfade,
            // où la coroutine pilote déjà les volumes chaque frame).
            if (_activeMusic != null && !_isFading)
            {
                float duck = Mathf.Max(0f, 1f - 0.5f * _pauseDuck);
                _activeMusic.volume = (_muted ? 0f : 1f) * _musicVolume * _masterVolume * duck;
            }
        }

        private void SetMixerDb(string param, float v01)
        {
            if (string.IsNullOrEmpty(param)) return;
            float db = v01 <= 0.0001f ? -60f : Mathf.Lerp(-40f, 0f, Mathf.Pow(v01, 0.6f));
            _mixer.SetFloat(param, db);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            _pool2DSize = Mathf.Clamp(_pool2DSize, 8, 32);
            _pool3DSize = Mathf.Clamp(_pool3DSize, 4, 24);
            // Clamp du champ legacy (CS0414 déjà masqué par pragma au niveau du champ).
            _slowMoMusicPitch = Mathf.Clamp(_slowMoMusicPitch, 0.4f, 1f);
            if (Application.isPlaying) ApplyAllVolumes();
        }
#endif
    }
}

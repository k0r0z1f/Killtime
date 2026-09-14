using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

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
        [SerializeField, Range(8, 32)] private int _pool2DSize = 18;
        [SerializeField, Range(4, 24)] private int _pool3DSize = 12;
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

        [Header("Volumes initiaux")]
        [SerializeField, Range(0f, 1f)] private float _masterVolume = 0.9f;
        [SerializeField, Range(0f, 1f)] private float _musicVolume = 0.75f;
        [SerializeField, Range(0f, 1f)] private float _ambienceVolume = 0.6f;
        [SerializeField, Range(0f, 1f)] private float _sfxVolume = 0.9f;
        [SerializeField, Range(0f, 1f)] private float _uiVolume = 0.85f;
        [SerializeField, Range(0f, 1f)] private float _cinematicVolume = 1f;

        [Header("Bullet-time")]
        [SerializeField, Range(0.4f, 1f)] private float _slowMoSfxPitch = 0.72f;
        [SerializeField, Range(0.4f, 1f)] private float _slowMoMusicPitch = 0.62f;
        [SerializeField] private float _slowMoLowpass = 750f;

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
        private AudioLowPassFilter _masterLowpass;

        private readonly Dictionary<SoundId, float> _lastPlayTime = new();
        private float _slowMoWeight;           // 0 normal -> 1 bullet-time
        private float _cinematicDuck;          // 0 normal -> 1 ducké
        private float _pauseDuck;
        private bool _muted;

        public MusicMood CurrentMood => _targetMood;
        public float CurrentIntensity => _targetIntensity01;
        public MusicIntensity CurrentIntensityLevel => AdaptiveMusicDirector.Quantize(_targetIntensity01);
        public bool IsMuted => _muted;

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

            LoadVolumes();
            BuildPools();
            _masterLowpass = gameObject.GetComponent<AudioLowPassFilter>();
            if (_masterLowpass == null) _masterLowpass = gameObject.AddComponent<AudioLowPassFilter>();
            _masterLowpass.enabled = false;
            _masterLowpass.cutoffFrequency = 22000f;
            ApplyAllVolumes();
        }

        private void Start()
        {
            // Démarrage musical par défaut : exploration calme, bascule en combat via hooks.
            if (_targetMood == MusicMood.None)
                PlayMusic(MusicMood.Explore, MusicIntensity.Calm);
            StartAmbience();
        }

        private void Update()
        {
            // Suit le timeScale pour le bullet-time même si le hook ciné est oublié.
            float targetSlow = (Time.timeScale < 0.7f && Time.timeScale > 0.0001f) ? 1f : _cinematicDuck * 0.5f;
            _slowMoWeight = Mathf.MoveTowards(_slowMoWeight, targetSlow, Time.unscaledDeltaTime * 5f);

            if (_masterLowpass != null)
            {
                bool needFilter = _slowMoWeight > 0.02f;
                _masterLowpass.enabled = needFilter;
                if (needFilter)
                    _masterLowpass.cutoffFrequency = Mathf.Lerp(22000f, _slowMoLowpass, _slowMoWeight);
            }

            if (_musicA != null)
                _musicA.pitch = Mathf.Lerp(1f, _slowMoMusicPitch, _slowMoWeight);
            if (_musicB != null)
                _musicB.pitch = Mathf.Lerp(1f, _slowMoMusicPitch, _slowMoWeight);

            if (_targetMood == MusicMood.Combat && !_isFading)
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

        public void PlayUI(SoundId id, float volumeScale = 1f)
        {
            var def = Resolve(id);
            if (!CheckCooldown(def)) return;
            var clip = PickClip(def);
            if (clip == null) return;
            var src = Next2D();
            ConfigureAndPlay(src, def, clip, volumeScale, 1f, Vector3.zero, false, forceCategory: SoundCategory.UI);
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

        /// <summary>Helper combat : mappe un DamageResult vers les bons SFX (appelé par l'arène).</summary>
        public void PlayCombatResult(bool isHit, bool isBlocked, bool isCritical, bool armorAbsorbed,
            bool shock, string fatalResolution, Vector3 atPos)
        {
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
            if (mood != _targetMood)
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
            int level = (int)AdaptiveMusicDirector.Quantize(intensity01);
            int trackCount = ProceduralAudioFactory.GetTrackCount(mood);
            trackIndex = Mathf.Clamp(trackIndex, 0, Mathf.Max(0, trackCount - 1));

            if (mood == _targetMood && trackIndex == _currentTrackIndex && level == _currentLevel && !forceRestart)
            {
                _targetIntensity01 = intensity01;
                return;
            }

            _targetMood = mood;
            _currentTrackIndex = trackIndex;
            _targetIntensity01 = intensity01;
            _lastAdaptiveSwitchTime = Time.unscaledTime;

            if (_musicRoutine != null) StopCoroutine(_musicRoutine);
            _musicRoutine = StartCoroutine(CrossfadeMusicRoutine(mood, trackIndex, level, intensity01));
        }

        public void NextCombatTrack()
        {
            if (_targetMood != MusicMood.Combat) return;
            int count = ProceduralAudioFactory.GetTrackCount(MusicMood.Combat);
            if (count <= 1) return;

            _combatTrackTimer = 0f;
            int next = (_currentTrackIndex + 1) % count;
            PlayMusic(MusicMood.Combat, next, _targetIntensity01, forceRestart: true);
        }

        public void StopMusic()
        {
            _targetMood = MusicMood.None;
            _currentMood = MusicMood.None;
            _currentTrackIndex = 0;
            _combatTrackTimer = 0f;
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
            src.pitch = Mathf.Lerp(1f, _slowMoMusicPitch, _slowMoWeight);
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
            var (mood, intensity) = AdaptiveMusicDirector.ComputeTarget(
                allyHpRatio01, alliesAlive, enemiesAlive, turnIndex, _targetMood, _targetIntensity01);
            bool moodChange = mood != _targetMood;
            bool bigShift = Mathf.Abs(intensity - _targetIntensity01) >= _adaptiveHysteresis;
            if (!moodChange && !bigShift) return false;
            if (!moodChange && Time.unscaledTime - _lastAdaptiveSwitchTime < _adaptiveCooldown) return false;
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

        // --- Snapshots code ---
        public void EnterCinematicMode()
        {
            _cinematicDuck = 1f;
            Play(SoundId.Cinematic_WhooshIn, 0.8f);
            Play(SoundId.SlowMo_Enter, 0.7f);
        }

        public void ExitCinematicMode()
        {
            _cinematicDuck = 0f;
            Play(SoundId.Cinematic_WhooshOut, 0.7f);
            Play(SoundId.SlowMo_Exit, 0.6f);
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
        public void SetCategoryVolume(SoundCategory cat, float v01)
        {
            v01 = Mathf.Clamp01(v01);
            switch (cat)
            {
                case SoundCategory.Master: _masterVolume = v01; PlayerPrefs.SetFloat(PP_MASTER, v01); break;
                case SoundCategory.Music: _musicVolume = v01; PlayerPrefs.SetFloat(PP_MUSIC, v01); break;
                case SoundCategory.Ambience: _ambienceVolume = v01; PlayerPrefs.SetFloat(PP_AMB, v01); break;
                case SoundCategory.SFX: _sfxVolume = v01; PlayerPrefs.SetFloat(PP_SFX, v01); break;
                case SoundCategory.UI: _uiVolume = v01; PlayerPrefs.SetFloat(PP_UI, v01); break;
                case SoundCategory.Cinematic: _cinematicVolume = v01; break;
            }
            PlayerPrefs.Save();
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
                src.spatialBlend = 1f;
                src.rolloffMode = AudioRolloffMode.Logarithmic;
                src.minDistance = 4f;
                src.maxDistance = 30f;
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

        private AudioSource Next2D()
        {
            // Vol de voix : oldest-first circulaire, écrase le plus ancien.
            var src = _pool2D[_cursor2D % _pool2D.Count];
            _cursor2D++;
            return src;
        }

        private AudioSource Next3D()
        {
            var src = _pool3D[_cursor3D % _pool3D.Count];
            _cursor3D++;
            return src;
        }

        public static SoundDefinition CreateDefaultDefinition(SoundId id)
        {
            var def = new SoundDefinition { Id = id };
            // Réglages par famille
            int code = (int)id;
            if (code >= 100 && code < 200) { def.Category = SoundCategory.UI; def.Volume = 0.6f; def.Cooldown = 0.05f; def.SpatialBlend = 0f; }
            else if (code >= 200 && code < 300) { def.Category = SoundCategory.SFX; def.Volume = 0.75f; def.Cooldown = 0.09f; def.SpatialBlend = 1f; }
            else if (code >= 300 && code < 380) { def.Category = SoundCategory.SFX; def.Volume = 0.9f; def.Cooldown = 0.05f; def.SpatialBlend = 1f; }
            else if (code >= 380 && code < 400) { def.Category = SoundCategory.Cinematic; def.Volume = 0.85f; def.Cooldown = 0.2f; def.SpatialBlend = 0f; }
            else { def.Category = SoundCategory.SFX; def.Volume = 0.8f; def.Cooldown = 0.1f; def.SpatialBlend = 0.5f; }

            if (id == SoundId.Attack_Impact_Crit || id == SoundId.Impact_DeepBoom || id == SoundId.Death_Instant)
                def.Volume = 1f;
            if (id == SoundId.UI_Hover || id == SoundId.Move_PathTick)
                def.Cooldown = 0.08f;
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
            src.spatialBlend = is3D ? Mathf.Max(def.SpatialBlend, 0.7f) : 0f;
            if (is3D) { src.minDistance = 4f; src.maxDistance = def.MaxDistance; src.transform.position = pos; }

            float catVol = CategoryVolume(cat);
            float duck = cat == SoundCategory.Music ? (1f - 0.35f * _cinematicDuck - 0.5f * _pauseDuck)
                       : cat == SoundCategory.Ambience ? (1f - 0.4f * _cinematicDuck)
                       : 1f;
            src.volume = (_muted ? 0f : 1f) * _masterVolume * catVol * def.Volume * volumeScale * Mathf.Max(0f, duck);

            float pitchJitter = Random.Range(def.PitchMin, def.PitchMax);
            float slowFactor = (cat == SoundCategory.Music || cat == SoundCategory.Ambience) ? 1f
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

        private IEnumerator CrossfadeMusicRoutine(MusicMood mood, int trackIndex, int level, float intensity01)
        {
            var clip = ProceduralAudioFactory.GetMusicLoop(mood, trackIndex, level);
            // Si un jour un SoundBank musical existe, on le préférera ici.
            if (clip == null) yield break;

            _isFading = true;
            _currentMood = mood;
            _currentLevel = level;
            _currentIntensity01 = intensity01;
            var fadeIn = _musicFlip ? _musicA : _musicB;
            var fadeOut = _musicFlip ? _musicB : _musicA;
            _musicFlip = !_musicFlip;

            fadeIn.clip = clip;
            fadeIn.loop = true;
            fadeIn.pitch = Mathf.Lerp(1f, _slowMoMusicPitch, _slowMoWeight);
            fadeIn.volume = 0f;
            fadeIn.Play();
            // Aligne la phase sur la boucle sortante : les downbeats restent en place,
            // la transition sonne comme un vrai changement d'arrangement, pas un saut.
            try
            {
                if (fadeOut != null && fadeOut.isPlaying && fadeOut.clip != null && fadeOut.time > 0.05f)
                    fadeIn.time = fadeOut.time % clip.length;
            }
            catch (System.Exception) { /* WebGL-safe : ignore le seek si refusé */ }
            _activeMusic = fadeIn;

            float t = 0f;
            bool isFinale = mood == MusicMood.Victory || mood == MusicMood.Defeat;
            float dur = isFinale ? Mathf.Max(_musicCrossfade, _finaleCrossfade) : Mathf.Max(0.4f, _musicCrossfade);
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / dur));
                // Equal-power : pas de creux au milieu du crossfade (vs linéaire k / 1-k).
                float gIn = Mathf.Sin(k * Mathf.PI * 0.5f);
                float gOut = Mathf.Cos(k * Mathf.PI * 0.5f);
                float targetVol = _musicVolume * _masterVolume; // relu chaque frame : suit les sliders
                float duck = Mathf.Max(0f, 1f - 0.35f * _cinematicDuck - 0.5f * _pauseDuck);
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
            _isFading = false;
            ApplyAllVolumes();
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
                SetMixerDb(_musicParam, _musicVolume * (1f - 0.35f * _cinematicDuck - 0.5f * _pauseDuck));
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
                float duck = Mathf.Max(0f, 1f - 0.35f * _cinematicDuck - 0.5f * _pauseDuck);
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
            if (Application.isPlaying) ApplyAllVolumes();
        }
#endif
    }
}

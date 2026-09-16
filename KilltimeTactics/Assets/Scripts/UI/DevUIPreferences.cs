using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Killtime.UI
{
    /// <summary>
    /// Entrée de layout persistée pour une fenêtre flottante dev (position, taille, état).
    /// Champs publics uniquement : requis pour JsonUtility.
    /// </summary>
    [Serializable]
    public class WindowLayoutEntry
    {
        public int WindowId;
        public float X;
        public float Y;
        public float W;
        public float H;
        public bool IsOpen;
        public bool IsMinimized;
        // Le rect enregistré reste toujours le rect normal, jamais la vignette réduite.
        public bool IsDocked;
    }

    /// <summary>
    /// Toutes les préférences des Dev UI (F1-F5, F9, toolbar, IA, arène, caméra, map editor...).
    /// Stockées en ints/strings/floats/bools pour rester sérialisables et robustes
    /// aux changements d'enums (aucune dépendance d'enum ici).
    /// </summary>
    [Serializable]
    public class DevUIPreferencesData
    {
        public int Version = 1;

        // --- Dev Arena / VATS (CombatDevToolbar) ---
        public int VatsSelectedPart = 4; // BodyPart.Torse
        public bool VatsCancelPenalty = false;
        public int VatsAttackerBonus = 0;
        public int VatsDefenderBonus = 0;
        public int VatsAttackSkill = 6;  // SkillType.Ballistique
        public int VatsDefenseSkill = 7; // SkillType.Esquive
        public bool VatsDefenderWantsToDefend = true;
        public int VatsWeaponDamage = 5;
        public int CombatToolbarTab = 0;
        public bool CombatLogScrollLock = false;

        // --- Arène ---
        public bool InfiniteAP = false;
        public int ArenaLayout = 1; // TacticalBarricades
        public bool EnableCinematicKillcam = true;

        // --- IA tactique ---
        public int AiMode = 0; // Normal
        public int AiPersonality = 0; // Balanced
        public bool AiEnabled = true;
        public float AiActionDelay = 0.8f;
        public float AiRetreatRatio = 0.25f;
        public int AiDefensiveReserve = 1;

        // --- Éditeur de carte (F2) ---
        public int MapBrush = 2; // HalfCover
        public string MapName = "Killzone_Alpha";
        public string GridRadiusInput = "8";
        public float CeilingHeight = 3.5f;
        public int MapEditorTab = 0;
        public float SculptStep = 0.25f;
        public float TargetElevation = 1.0f;
        public int SculptRadius = 1;
        public bool SmoothSlope = false;
        public float GroundTiling = 1.0f;
        public int SelectedPropIndex = 0;
        public float PropRotationY = 0f;
        public float PropScale = 1.0f;
        public int PropCover = 2; // Full
        public bool PropWalkable = false;
        public int PropPlacement = 0; // Plancher
        public float PropHeightOffset = 0f;
        public int SelectedGroundIndex = 0;
        public int SelectedCategoryIndex = 0;
        public float CeilingOpacity = 0.22f;
        public bool ShowCeilingInGame = true;

        // --- Créateur de perso (F1) ---
        public int CharacterTab = 0;
        public float PreviewYaw = 180f;
        public int PreviewAnimIndex = 0;
        public string SpawnQ = "0";
        public string SpawnR = "1";
        public bool SpawnAsPlayer = false;

        // --- Inventaire / Armurerie (F5/I) ---
        public int InventoryTab = 0;
        public string InventorySearch = "";
        public int InventoryCategory = -1;
        public bool InventoryAffordableOnly = false;
        public bool InventoryRealPrefabsOnly = false;

        // --- Caméra tactique ---
        public float CamDistance = 12f;
        public float CamYaw = 45f;
        public float CamPitch = 45f;

        // --- Multijoueur VTT (F4) : identité de table uniquement, jamais les secrets réseau ---
        public string VttServerUrl = "";
        public string VttUsername = "Joueur";
        public string VttJoinCode = "";
        public string VttTableName = "";
        public string VttMaxPlayers = "6";
        public bool VttIsPublic = true;

        // --- Voix VTT (F4 / Audio) ---
        public bool VoiceEnabled = true;
        public string VoiceInputDevice = "";
        public float VoiceOutputVolume = 1.0f;
        public float VoiceInputGain = 1.0f;
        public int VoiceActivationMode = 0; // 0 = VAD (Détection vocale), 1 = PTT (Push-to-Talk), 2 = Continu
        public float VoiceVadThreshold = 0.02f; // ~ -34 dB
        public float VoiceVadHangoverMs = 350f;
        public int VoicePttKey = 118; // (int)KeyCode.V
        public bool VoiceNoiseGateEnabled = true;
        public bool VoiceNoiseReductionEnabled = true;
        public bool VoiceHighPassFilter = true;
        public bool VoiceOutputLimiterEnabled = true;
        public float VoiceOutputCeiling = 0.50f;
        public bool VoiceGateDeleterEnabled = true;
        public bool VoiceRadioCommsEffectEnabled = false;
        public bool VoiceAntiKeyboardFilter = true;
        public bool VoiceClarityEnabled = true;
        public float VoiceClarityAmount = 0.65f;
        public int VoiceCodec = 0; // 0 = IMA-ADPCM 16k [Défaut]
        public bool VoiceMuted = false;
        public bool VoiceDeafened = false;

        // --- Modulateur Vocal (Voice Changer) ---
        public bool VoiceChangerEnabled = false;
        public int VoiceChangerPreset = 0;
        public float VoiceChangerPitch = -3.5f;
        public float VoiceChangerFormant = -0.55f;
        public float VoiceChangerDrive = 0.25f;
        public float VoiceChangerRobotic = 0.0f;
        public float VoiceChangerMix = 1.0f;

        // --- Vidéo VTT (Webcam) ---
        public bool VideoBlurBackground = true;
        public int VideoBlurRadius = 5;
        public float VideoCropZoom = 1.0f;
        public float VideoCropOffsetX = 0.0f;
        public float VideoCropOffsetY = 0.0f;

        // --- Layouts des fenêtres flottantes ---
        public List<WindowLayoutEntry> Windows = new List<WindowLayoutEntry>();
    }

    /// <summary>
    /// Service central de persistance des préférences Dev UI.
    /// - Fichier JSON dans Application.persistentDataPath/DevUIPreferences.json
    /// - Miroir PlayerPrefs (clé KT_DevUIPrefs_JSON) pour WebGL / fallback si le fichier échoue.
    /// - Sauvegarde différée (debounce) via MarkDirty(), sauvegarde immédiate via SaveNow().
    /// - Chargement précoce via RuntimeInitializeOnLoadMethod + chargement paresseux.
    /// Les fenêtres dev appellent : Apply au Awake/OnOpened, Capture + MarkDirty() sur GUI.changed,
    /// SaveNow() sur OnClosed/OnDisable si besoin.
    /// </summary>
    public class DevUIPreferences : MonoBehaviour
    {
        public static DevUIPreferences Instance { get; private set; }
        public static DevUIPreferencesData Current { get; private set; }

        private const string FileName = "DevUIPreferences.json";
        private const string PP_KEY = "KT_DevUIPrefs_JSON";

        private static bool _dirty;
        private static float _nextSaveTime;

        private static string FilePath
        {
            get
            {
                try { return Path.Combine(Application.persistentDataPath, FileName); }
                catch { return null; }
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            LoadNow();
            EnsureSaver();
            try
            {
                Application.quitting -= OnAppQuitting;
                Application.quitting += OnAppQuitting;
            }
            catch { /* ignore hors play-mode */ }
        }

        private static void OnAppQuitting()
        {
            if (_dirty)
            {
                try { SaveNow(); } catch { /* ignore : fermeture */ }
            }
        }

        private static void EnsureSaver()
        {
            if (Instance != null) return;
            var existing = FindAnyObjectByType<DevUIPreferences>();
            if (existing != null)
            {
                Instance = existing;
                return;
            }
            try
            {
                var go = new GameObject("[Dev] DevUIPreferences");
                go.hideFlags = HideFlags.DontSave;
                DontDestroyOnLoad(go);
                Instance = go.AddComponent<DevUIPreferences>();
            }
            catch
            {
                // Hors play-mode (tests) : pas de saver, Load/Save restent appelables.
            }
        }

        private void Update()
        {
            if (_dirty && Time.realtimeSinceStartup >= _nextSaveTime)
            {
                SaveNow();
            }
        }

        private void OnApplicationQuit()
        {
            if (_dirty)
            {
                try { SaveNow(); } catch { /* ignore */ }
            }
        }

        /// <summary>Marque les prefs comme modifiées, sauvegarde différée (anti-spam disque).</summary>
        public static void MarkDirty(float delaySeconds = 0.8f)
        {
            EnsureDefaults();
            _dirty = true;
            try { _nextSaveTime = Time.realtimeSinceStartup + Mathf.Max(0.05f, delaySeconds); }
            catch { _nextSaveTime = 0f; }
            EnsureSaver();
        }

        public static void SaveNow()
        {
            EnsureDefaults();
            try
            {
                string json = JsonUtility.ToJson(Current, true);
                bool fileOk = false;
                string path = FilePath;
                if (!string.IsNullOrEmpty(path))
                {
                    try
                    {
                        string dir = Path.GetDirectoryName(path);
                        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                            Directory.CreateDirectory(dir);
                        File.WriteAllText(path, json);
                        fileOk = true;
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[DevUIPreferences] Écriture fichier impossible : {e.Message}. Repli PlayerPrefs.");
                    }
                }
                try
                {
                    PlayerPrefs.SetString(PP_KEY, json);
                    PlayerPrefs.Save();
                }
                catch (Exception e)
                {
                    if (!fileOk) Debug.LogWarning($"[DevUIPreferences] Sauvegarde PlayerPrefs impossible : {e.Message}");
                }
                _dirty = false;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DevUIPreferences] SaveNow échoué : {e.Message}");
            }
        }

        public static void LoadNow()
        {
            DevUIPreferencesData data = null;
            string path = FilePath;
            if (!string.IsNullOrEmpty(path))
            {
                try
                {
                    if (File.Exists(path))
                    {
                        string json = File.ReadAllText(path);
                        if (!string.IsNullOrEmpty(json))
                            data = JsonUtility.FromJson<DevUIPreferencesData>(json);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[DevUIPreferences] Lecture fichier impossible : {e.Message}. Essai PlayerPrefs.");
                }
            }
            if (data == null)
            {
                try
                {
                    if (PlayerPrefs.HasKey(PP_KEY))
                    {
                        string json = PlayerPrefs.GetString(PP_KEY, "");
                        if (!string.IsNullOrEmpty(json))
                            data = JsonUtility.FromJson<DevUIPreferencesData>(json);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[DevUIPreferences] Lecture PlayerPrefs impossible : {e.Message}");
                }
            }
            Current = data ?? new DevUIPreferencesData();
            if (Current.Windows == null) Current.Windows = new List<WindowLayoutEntry>();
            Sanitize(Current);
            _dirty = false;
        }

        public static void ResetToDefaults()
        {
            Current = new DevUIPreferencesData();
            SaveNow();
        }

        private static void EnsureDefaults()
        {
            if (Current == null) LoadNow();
            if (Current == null) Current = new DevUIPreferencesData();
            if (Current.Windows == null) Current.Windows = new List<WindowLayoutEntry>();
        }

        private static void Sanitize(DevUIPreferencesData d)
        {
            if (d == null) return;
            d.AiActionDelay = Mathf.Clamp(d.AiActionDelay, 0.05f, 10f);
            d.AiRetreatRatio = Mathf.Clamp(d.AiRetreatRatio, 0.05f, 0.6f);
            d.AiDefensiveReserve = Mathf.Clamp(d.AiDefensiveReserve, 0, 2);
            d.VatsWeaponDamage = Mathf.Clamp(d.VatsWeaponDamage, 1, 30);
            d.VatsAttackerBonus = Mathf.Clamp(d.VatsAttackerBonus, 0, 10);
            d.VatsDefenderBonus = Mathf.Clamp(d.VatsDefenderBonus, 0, 10);
            d.CeilingOpacity = Mathf.Clamp(d.CeilingOpacity, 0.01f, 0.8f);
            d.CeilingHeight = Mathf.Clamp(d.CeilingHeight, 1f, 12f);
            d.SculptStep = Mathf.Clamp(d.SculptStep, 0.05f, 2f);
            d.TargetElevation = Mathf.Clamp(d.TargetElevation, -5f, 8f);
            d.SculptRadius = Mathf.Clamp(d.SculptRadius, 1, 5);
            d.GroundTiling = Mathf.Clamp(d.GroundTiling, 0.1f, 8f);
            d.PropScale = Mathf.Clamp(d.PropScale, 0.2f, 3f);
            d.PropHeightOffset = Mathf.Clamp(d.PropHeightOffset, -2f, 4f);
            d.CamDistance = Mathf.Clamp(d.CamDistance, 0.5f, 35f);
            d.CamPitch = Mathf.Clamp(d.CamPitch, 30f, 60f);
            d.VoiceOutputCeiling = Mathf.Clamp(d.VoiceOutputCeiling, 0.15f, 1.0f);
            d.VoiceChangerPitch = Mathf.Clamp(d.VoiceChangerPitch, -12f, 12f);
            d.VoiceChangerFormant = Mathf.Clamp(d.VoiceChangerFormant, -1f, 1f);
            d.VoiceChangerDrive = Mathf.Clamp01(d.VoiceChangerDrive);
            d.VoiceChangerRobotic = Mathf.Clamp01(d.VoiceChangerRobotic);
            d.VoiceChangerMix = Mathf.Clamp01(d.VoiceChangerMix);
            if (d.MapName == null) d.MapName = "Killzone_Alpha";
            if (d.GridRadiusInput == null) d.GridRadiusInput = "8";
            if (d.SpawnQ == null) d.SpawnQ = "0";
            if (d.SpawnR == null) d.SpawnR = "1";
            if (d.InventorySearch == null) d.InventorySearch = "";
            if (d.VttUsername == null) d.VttUsername = "Joueur";
            if (d.VttJoinCode == null) d.VttJoinCode = "";
            if (d.VttTableName == null) d.VttTableName = "";
            if (d.VttMaxPlayers == null) d.VttMaxPlayers = "6";
            if (d.VttServerUrl == null) d.VttServerUrl = "";
        }

        // --- Layouts fenêtres ---

        public static WindowLayoutEntry GetWindow(int windowId)
        {
            EnsureDefaults();
            for (int i = 0; i < Current.Windows.Count; i++)
            {
                if (Current.Windows[i] != null && Current.Windows[i].WindowId == windowId)
                    return Current.Windows[i];
            }
            return null;
        }

        public static void SetWindow(int windowId, Rect rect, bool isOpen, bool isMinimized, bool isDocked)
        {
            EnsureDefaults();
            WindowLayoutEntry entry = null;
            for (int i = 0; i < Current.Windows.Count; i++)
            {
                if (Current.Windows[i] != null && Current.Windows[i].WindowId == windowId)
                {
                    entry = Current.Windows[i];
                    break;
                }
            }
            if (entry == null)
            {
                entry = new WindowLayoutEntry { WindowId = windowId };
                Current.Windows.Add(entry);
            }
            bool changed =
                !Mathf.Approximately(entry.X, rect.x) || !Mathf.Approximately(entry.Y, rect.y) ||
                !Mathf.Approximately(entry.W, rect.width) || !Mathf.Approximately(entry.H, rect.height) ||
                entry.IsOpen != isOpen || entry.IsMinimized != isMinimized || entry.IsDocked != isDocked;
            entry.X = rect.x;
            entry.Y = rect.y;
            entry.W = rect.width;
            entry.H = rect.height;
            entry.IsOpen = isOpen;
            entry.IsMinimized = isMinimized;
            entry.IsDocked = isDocked;
            if (changed) MarkDirty(1.2f);
        }

        public static void SetWindowOpen(int windowId, bool isOpen, bool isMinimized)
        {
            EnsureDefaults();
            WindowLayoutEntry entry = GetWindow(windowId);
            if (entry == null)
            {
                entry = new WindowLayoutEntry { WindowId = windowId };
                Current.Windows.Add(entry);
            }
            if (entry.IsOpen != isOpen || entry.IsMinimized != isMinimized)
            {
                entry.IsOpen = isOpen;
                entry.IsMinimized = isMinimized;
                MarkDirty(0.5f);
            }
        }
    }
}

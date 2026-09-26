using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Audio;
using Killtime.Story.Data;
using Killtime.Tactics;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.UI;

namespace Killtime.CameraSystem
{
    public enum CameraMode
    {
        TacticalIsometric,
        FreeLook,
        CinematicAction
    }

    /// <summary>
    /// Directeur cinématique orchestrant les transitions entre la vue isométrique,
    /// la caméra libre (Free Look) et les gros plans d'action cinématique (V.A.T.S. Killtime).
    /// </summary>
    public class CinematicDirector : MonoBehaviour
    {
        [Header("Composants")]
        [SerializeField] private TacticalCameraController _tacticalCam;
        [SerializeField] private UnityEngine.Camera _mainCamera;

        [Header("Configuration Free Look")]
        [SerializeField] private float _freeLookSensitivity = 2.5f;
        [SerializeField] private float _freeLookSpeed = 12.0f;

        [Header("Configuration Cinématique")]
        [SerializeField] private float _cinematicFOV = 48.0f;
        [SerializeField] private float _normalFOV = 60.0f;

        public CameraMode CurrentMode { get; private set; } = CameraMode.TacticalIsometric;

        /// <summary>
        /// Vrai quand l'utilisateur pilote la caméra librement : aucune cinématique
        /// (killshot d'attaque ou autre) ne doit s'enclencher ni lui voler la caméra.
        /// </summary>
        public bool IsFreeLook => CurrentMode == CameraMode.FreeLook;

        /// <summary>
        /// Ralenti global dev (1x → 0.1x) : les plans d'action restaurent cette
        /// valeur au lieu de forcer 1x, sinon le slider dev serait écrasé.
        /// </summary>
        private static float DevTimeScale()
        {
            try
            {
                return Killtime.UI.CombatDevToolbar.ReadDevTimeScalePref();
            }
            catch { return 1f; }
        }

        private float _freePitch = 0.0f;
        private float _freeYaw = 0.0f;
        private Coroutine _activeCinematicRoutine;

        private Vector3 _shakeOffsetPos = Vector3.zero;
        private Vector3 _shakeOffsetEuler = Vector3.zero;
        private float _shakeOffsetFov = 0f;

        private Transform GetCameraTransform()
        {
            if (_mainCamera != null) return _mainCamera.transform;
            var main = UnityEngine.Camera.main;
            if (main != null) return main.transform;
            return transform;
        }

        // ================= CINÉMATIQUES DE SCÈNE (cartes 🎬) =================
        // Lecture fire-and-forget : le runtime déclenche sans attendre la fin,
        // la carte suivante s'enclenche immédiatement. Les cinématiques qui se
        // chevauchent sont mises en file au lieu de se couper.
        private Coroutine _activeSceneCinematicRoutine;
        private struct QueuedCinematic
        {
            public SceneCinematicData Data;
            public Action OnComplete;
        }
        private readonly Queue<QueuedCinematic> _sceneCinematicQueue = new();
        private Action _sceneCinematicOnComplete;
        private bool _letterboxActive;
        private float _letterboxProgress;
        [SerializeField] private float _letterboxEnterDuration = 0.35f;
        [SerializeField] private float _letterboxExitDuration = 0.45f;
        private float _skipNoiseSeed;

        public bool IsPlayingSceneCinematic => _activeSceneCinematicRoutine != null;
        public SceneCinematicData CurrentPlayingSceneCinematic { get; private set; }

        public void ResetCinematicState()
        {
            if (_activeCinematicRoutine != null)
            {
                StopCoroutine(_activeCinematicRoutine);
                _activeCinematicRoutine = null;
            }
            if (_activeSceneCinematicRoutine != null)
            {
                StopCoroutine(_activeSceneCinematicRoutine);
                _activeSceneCinematicRoutine = null;
            }
            _sceneCinematicQueue.Clear();
            _sceneCinematicOnComplete = null;
            _letterboxActive = false;
            _letterboxProgress = 0f;
            ClearAllCinematicLocks();

            Time.timeScale = DevTimeScale();
            if (KilltimeAudioManager.Instance != null)
                KilltimeAudioManager.Instance.ExitCinematicMode();

            if (_tacticalCam != null)
            {
                _tacticalCam.enabled = true;
            }

            if (_mainCamera != null && _normalFOV > 0)
            {
                _mainCamera.fieldOfView = _normalFOV;
            }

            CurrentMode = CameraMode.TacticalIsometric;
        }

        private void Awake()
        {
            if (_mainCamera == null)
            {
                _mainCamera = GetComponent<UnityEngine.Camera>() ?? UnityEngine.Camera.main;
            }
            if (_mainCamera != null && _normalFOV > 0)
            {
                _mainCamera.fieldOfView = _normalFOV;
            }
            ResolveTacticalCam();
            _skipNoiseSeed = UnityEngine.Random.Range(0f, 100f);
        }

        // Sécurité : si _tacticalCam n'est pas câblé dans l'inspecteur, le retrouver
        // (sinon il resterait activé et écraserait la cinématique chaque frame —
        // symptôme "ça veut jouer mais ça revient aussitôt").
        private void ResolveTacticalCam()
        {
            if (_tacticalCam != null) return;
            try { _tacticalCam = FindAnyObjectByType<TacticalCameraController>(); }
            catch { /* ignore */ }
        }

        // ---------- API publique cinématiques de scène ----------

        /// <summary>
        /// Déclenche une cinématique de scène SANS bloquer : retour immédiat,
        /// la carte suivante peut s'enclencher pendant la lecture.
        /// </summary>
        public void PlaySceneCinematic(SceneCinematicData cine, Action onComplete = null)
        {
            if (cine == null || cine.Shots == null || cine.Shots.Count == 0) return;

            // Une cinématique sur place ne prend pas le contrôle exclusif de la caméra
            // et ne s'empile pas : elle s'exécute immédiatement en superposition additive.
            if (cine.InPlace)
            {
                StartCoroutine(InPlaceCinematicRoutine(cine, onComplete));
                return;
            }

            if (IsPlayingSceneCinematic)
            {
                _sceneCinematicQueue.Enqueue(new QueuedCinematic { Data = cine, OnComplete = onComplete });
                return;
            }
            _sceneCinematicOnComplete = onComplete;
            _activeSceneCinematicRoutine = StartCoroutine(SceneCinematicRoutine(cine));
        }

        private IEnumerator InPlaceCinematicRoutine(SceneCinematicData cine, Action onComplete)
        {
            float speed = cine.PlaybackSpeed > 0.01f ? cine.PlaybackSpeed : 1f;
            var shots = cine.Shots != null ? new List<SceneCinematicShotData>(cine.Shots) : new List<SceneCinematicShotData>();

            bool skipped = false;

            for (int s = 0; s < shots.Count; s++)
            {
                var shot = shots[s];
                if (shot == null) continue;
                float duration = Mathf.Max(0.1f, shot.Duration / speed);
                float startDelay = Mathf.Max(0f, shot.StartDelay / speed);

                // Sécurité : si aucun effet n'est assigné sur un plan sur place, forcer la secousse tactique
                if (shot.MoveEffect == CinematicCameraEffect.None)
                {
                    shot.MoveEffect = CinematicCameraEffect.HandheldShake;
                    if (shot.ShakeIntensity < 0.05f) shot.ShakeIntensity = 0.25f;
                }

                if (!string.IsNullOrWhiteSpace(shot.Speech))
                {
                    string who = string.IsNullOrWhiteSpace(shot.SpeakerId) ? cine.Title : shot.SpeakerId;
                    CombatHUD.Instance?.AddAdvancedLog($"🎬 <b>[{who}]</b> « {shot.Speech} »", LogCategory.MovementAndTurns, "[CINÉMATIQUE]", new Color(1f, 0.5f, 0.9f));
                }
                if (!string.IsNullOrWhiteSpace(shot.SoundCueId) && KilltimeAudioManager.Instance != null)
                {
                    try
                    {
                        if (Enum.TryParse<SoundId>(shot.SoundCueId, out var cue))
                            KilltimeAudioManager.Instance.PlayUI(cue, 0.6f);
                    }
                    catch { /* ignore */ }
                }

                if (startDelay > 0.001f)
                {
                    float delayElapsed = 0f;
                    while (delayElapsed < startDelay)
                    {
                        while (CombatHUD.IsPaused) yield return null;
                        delayElapsed += Mathf.Min(Time.unscaledDeltaTime, 0.1f);
                        if (cine.Skippable && Input.GetKeyDown(KeyCode.Escape)) { skipped = true; break; }
                        yield return null;
                    }
                }
                if (skipped) break;

                float elapsed = 0f;
                while (elapsed < duration)
                {
                    while (CombatHUD.IsPaused) yield return null;
                    elapsed += Mathf.Min(Time.unscaledDeltaTime, 0.1f);
                    float t = Mathf.Clamp01(elapsed / duration);
                    float e = ApplyCinematicEase(shot.Ease, t);

                    // Génération de l'offset additif dans la passe normale (avant rendu)
                    Vector3 sPos = Vector3.zero;
                    Vector3 sEuler = Vector3.zero;
                    float sFov = 0f;
                    ApplyShotEffect(shot, t, e, ref sPos, ref sEuler, ref sFov);

                    _shakeOffsetPos = sPos;
                    _shakeOffsetEuler = sEuler;
                    _shakeOffsetFov = sFov;

                    if (cine.Skippable && Input.GetKeyDown(KeyCode.Escape))
                    {
                        skipped = true;
                        break;
                    }

                    yield return null;
                }

                _shakeOffsetPos = Vector3.zero;
                _shakeOffsetEuler = Vector3.zero;
                _shakeOffsetFov = 0f;

                if (skipped) break;
            }

            _shakeOffsetPos = Vector3.zero;
            _shakeOffsetEuler = Vector3.zero;
            _shakeOffsetFov = 0f;

            try { onComplete?.Invoke(); } catch { /* ignore */ }
        }

        /// <summary>
        /// Interrompt la lecture (abort) : la sortie "fin →" n'est PAS résolue
        /// (un abort n'est pas une fin — seul le terme naturel ou le skip enchaîne).
        /// </summary>
        public void StopSceneCinematic(bool restoreCamera = true)
        {
            // Idle : rien à restaurer (évite d'écraser FOV/timeScale pour rien).
            if (!IsPlayingSceneCinematic && _sceneCinematicQueue.Count == 0 && _letterboxProgress <= 0.001f) return;
            _sceneCinematicQueue.Clear();
            if (_activeSceneCinematicRoutine != null)
            {
                StopCoroutine(_activeSceneCinematicRoutine);
                _activeSceneCinematicRoutine = null;
            }
            _letterboxActive = false;
            _letterboxProgress = 0f;
            ClearAllCinematicLocks();
            if (restoreCamera) RestoreTacticalCamera();
            else if (_tacticalCam != null) _tacticalCam.enabled = true;
            if (CurrentMode == CameraMode.CinematicAction) CurrentMode = CameraMode.TacticalIsometric;
            _sceneCinematicOnComplete = null;
            CurrentPlayingSceneCinematic = null;
        }

        /// <summary>Lecture de l'état caméra pour le mode éditeur cinématique (capture START/END).</summary>
        public bool CaptureCameraState(out Vector3 pos, out Vector3 euler, out float fov)
        {
            pos = transform.position;
            euler = transform.rotation.eulerAngles;
            fov = _mainCamera != null ? _mainCamera.fieldOfView : _normalFOV;
            return true;
        }

        /// <summary>Application d'un état caméra (prévisualisation dans le mode éditeur).</summary>
        public void ApplyCameraState(Vector3 pos, Vector3 euler, float fov)
        {
            if (IsPlayingSceneCinematic) StopSceneCinematic(false);
            transform.position = pos;
            transform.rotation = Quaternion.Euler(euler);
            if (_mainCamera != null && fov > 1f) _mainCamera.fieldOfView = fov;
        }

        public void SetTacticalControlEnabled(bool enabled)
        {
            if (_tacticalCam != null) _tacticalCam.enabled = enabled;
            if (!enabled) CurrentMode = CameraMode.FreeLook;
            else if (CurrentMode == CameraMode.FreeLook && !IsPlayingSceneCinematic)
                CurrentMode = CameraMode.TacticalIsometric;
        }

        /// <summary>
        /// Rend la main à la caméra tactique après une prévisualisation d'édition
        /// (restaure le FOV normal, laissée sinon figée par ApplyCameraState).
        /// </summary>
        public void ReleaseToTactical()
        {
            if (IsPlayingSceneCinematic) StopSceneCinematic(false);
            if (_tacticalCam != null) _tacticalCam.enabled = true;
            if (_mainCamera != null && _normalFOV > 0) _mainCamera.fieldOfView = _normalFOV;
            if (CurrentMode == CameraMode.FreeLook) CurrentMode = CameraMode.TacticalIsometric;
            _letterboxActive = false;
        }

        private void RestoreTacticalCamera()
        {
            Time.timeScale = DevTimeScale();
            if (KilltimeAudioManager.Instance != null)
                KilltimeAudioManager.Instance.ExitCinematicMode();
            if (_tacticalCam != null) _tacticalCam.enabled = true;
            if (_mainCamera != null && _normalFOV > 0) _mainCamera.fieldOfView = _normalFOV;
            _letterboxActive = false;
        }

        private void OnGUI()
        {
            if (_letterboxProgress <= 0.0001f) return;
            try
            {
                float maxBarH = Mathf.Max(24f, Screen.height * 0.11f);
                float eased = ApplyCinematicEase(CinematicEase.Smooth, _letterboxProgress);
                float barH = maxBarH * eased;
                if (barH > 0.5f)
                {
                    Color prev = GUI.color;
                    GUI.color = Color.black;
                    GUI.DrawTexture(new Rect(0f, 0f, Screen.width, barH), Texture2D.whiteTexture);
                    GUI.DrawTexture(new Rect(0f, Screen.height - barH, Screen.width, barH), Texture2D.whiteTexture);
                    GUI.color = prev;
                }
            }
            catch { /* ignore */ }
        }

        public static float ApplyCinematicEase(CinematicEase ease, float t)
        {
            t = Mathf.Clamp01(t);
            switch (ease)
            {
                case CinematicEase.Linear: return t;
                case CinematicEase.EaseIn: return t * t * t;
                case CinematicEase.EaseOut: return 1f - Mathf.Pow(1f - t, 3f);
                case CinematicEase.EaseInOut:
                    return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;
                case CinematicEase.Punch:
                    return t < 0.25f ? EaseOutQuad(t / 0.25f) * 1.06f - 0.06f * (t / 0.25f) : 1.06f - 0.06f * EaseInOutQuad((t - 0.25f) / 0.75f);
                case CinematicEase.Smooth:
                default: return t * t * (3f - 2f * t);
            }
        }

        private static float EaseOutQuad(float t) { t = Mathf.Clamp01(t); return 1f - (1f - t) * (1f - t); }
        private static float EaseInOutQuad(float t) { t = Mathf.Clamp01(t); return t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) / 2f; }

        private struct ShotActorTrack
        {
            public TacticalUnit Unit;
            public Vector3 StartWorld;
            public Vector3 EndWorld;
            public int EndQ;
            public int EndR;
            public float StartYaw;
            public float EndYaw;
            public bool HasEnd;
        }

        // Vrai si piloter notre transform déplace réellement une caméra (rig direct,
        // enfant, ou transform de la caméra principale). Sinon le travel est invisible.
        private bool IsCameraOnDrivenTransform()
        {
            try
            {
                if (GetComponent<UnityEngine.Camera>() != null) return true;
                if (GetComponentInChildren<UnityEngine.Camera>() != null) return true;
                var main = _mainCamera != null ? _mainCamera : UnityEngine.Camera.main;
                if (main != null && transform == main.transform) return true;
            }
            catch { /* ignore */ }
            return false;
        }

        // Sécurité : libère les verrous de pilotage sur toutes les unités (ex : Stop
        // en plein plan, changement de scène). Sans ça, une unité resterait
        // indéplaçable (MoveAlongPath refuse tant que CinematicDriveActive).
        private static void ClearAllCinematicLocks()
        {
            try
            {
                var units = FindObjectsByType<TacticalUnit>();
                for (int i = 0; i < units.Length; i++)
                    if (units[i] != null) units[i].CinematicDriveActive = false;
            }
            catch { /* ignore */ }
        }

        private struct ShotPropTrack
        {
            public TacticalInteractable Prop;
            public Vector3 StartWorld;
            public Vector3 EndWorld;
            public int EndQ;
            public int EndR;
            public bool HasEnd;
        }

        private static bool IsUnitTracked(List<ShotActorTrack> a, List<ShotActorTrack> b, TacticalUnit unit)
        {
            if (unit == null) return true;
            if (a != null)
                for (int i = 0; i < a.Count; i++)
                    if (a[i].Unit == unit) return true;
            if (b != null)
                for (int i = 0; i < b.Count; i++)
                    if (b[i].Unit == unit) return true;
            return false;
        }

        private static bool IsPropTracked(List<ShotPropTrack> list, TacticalInteractable prop)
        {
            if (prop == null) return true;
            if (list == null) return false;
            for (int i = 0; i < list.Count; i++)
                if (list[i].Prop == prop) return true;
            return false;
        }

        private static TacticalUnit FindSceneUnit(string actorIdOrName)
        {
            if (string.IsNullOrWhiteSpace(actorIdOrName)) return null;
            try
            {
                var units = FindObjectsByType<TacticalUnit>();
                for (int i = 0; i < units.Length; i++)
                {
                    var u = units[i];
                    if (u == null) continue;
                    if (string.Equals(u.gameObject.name, "Actor_" + actorIdOrName, StringComparison.OrdinalIgnoreCase))
                        return u;
                    if (u.Stats != null && string.Equals(u.Stats.Name, actorIdOrName, StringComparison.OrdinalIgnoreCase))
                        return u;
                }
            }
            catch { /* ignore */ }
            return null;
        }

        private static TacticalInteractable FindSceneProp(string interactableId)
        {
            if (string.IsNullOrWhiteSpace(interactableId)) return null;
            try
            {
                var props = FindObjectsByType<TacticalInteractable>();
                for (int i = 0; i < props.Length; i++)
                {
                    var p = props[i];
                    if (p == null) continue;
                    if (string.Equals(p.ObjectName, interactableId, StringComparison.OrdinalIgnoreCase))
                        return p;
                }
            }
            catch { /* ignore */ }
            return null;
        }

        private static bool TryHexWorld(TacticalHexGrid grid, int q, int r, out Vector3 world)
        {
            world = Vector3.zero;
            if (grid == null) return false;
            try
            {
                var node = grid.GetNode(new HexCoordinates(q, r));
                if (node == null) return false;
                world = node.WorldPosition;
                return true;
            }
            catch { return false; }
        }

        private IEnumerator SceneCinematicRoutine(SceneCinematicData cine)
        {
            CurrentPlayingSceneCinematic = cine;
            float speed = cine.PlaybackSpeed > 0.01f ? cine.PlaybackSpeed : 1f;
            ResolveTacticalCam();
            // (Pas de mémorisation de "retour" : la fin de plan RESTE — voir bas de routine.)
            // Validation décisive : sans caméra, le travel est invisible (symptôme
            // "ça bouge [acteurs/barres] puis revient [rien]").
            if (_mainCamera == null)
            {
                try { _mainCamera = UnityEngine.Camera.main; } catch { /* ignore */ }
                if (_mainCamera == null)
                {
                    Debug.LogError("[CinematicDirector] Aucune caméra : cinématique annulée (CinematicDirector mal placé ?).");
                    CombatHUD.Instance?.AddAdvancedLog("<color=red><b>[CINÉMATIQUE] Aucune caméra trouvée : lecture annulée.</b></color>", LogCategory.MovementAndTurns, "[CINÉMATIQUE]", Color.red);
                    if (_tacticalCam != null) _tacticalCam.enabled = true;
                    if (CurrentMode == CameraMode.CinematicAction) CurrentMode = CameraMode.TacticalIsometric;
                    _letterboxActive = false;
                    _activeSceneCinematicRoutine = null;
                    _sceneCinematicOnComplete = null;
                    yield break;
                }
            }
            bool driveCamera = !IsFreeLook && CurrentMode != CameraMode.FreeLook;
            if (driveCamera && !IsCameraOnDrivenTransform())
            {
                Debug.LogError("[CinematicDirector] Le CinematicDirector ne porte aucune caméra (ni lui ni ses enfants, et ce n'est pas la transform de la caméra principale) : le travel sera INVISIBLE. Placez-le sur le rig caméra.");
                CombatHUD.Instance?.AddAdvancedLog("<color=red><b>[CINÉMATIQUE] Director hors caméra : travel invisible (acteurs/barres seuls). Placez CinematicDirector sur le rig caméra.</b></color>", LogCategory.MovementAndTurns, "[CINÉMATIQUE]", Color.red);
            }
            if (!driveCamera)
            {
                // Pas un bug : en pilotage libre la caméra reste à la main (comme les
                // killshots). Log visible pour ne pas passer pour un "skip".
                CombatHUD.Instance?.AddAdvancedLog("<color=grey>[CINÉMATIQUE] Pilotage libre actif (F pour rendre la main) : caméra non prise en charge, les acteurs jouent.</color>", LogCategory.MovementAndTurns, "[CINÉMATIQUE]", Color.gray);
            }
            if (driveCamera)
            {
                CurrentMode = CameraMode.CinematicAction;
                if (_tacticalCam != null) _tacticalCam.enabled = false;
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.EnterCinematicMode();
            }

            var grid = FindAnyObjectByType<TacticalHexGrid>();
            bool skipped = false;
            Transform handoverTarget = null;

            var shots = cine.Shots != null ? new List<SceneCinematicShotData>(cine.Shots) : new List<SceneCinematicShotData>();

            Vector3 livePrePos = transform.position;
            Quaternion livePreRot = transform.rotation;
            float livePreFov = _mainCamera != null ? _mainCamera.fieldOfView : _normalFOV;
            Vector3 preFocusPos = Vector3.zero;
            bool hasPreFocus = false;
            if (shots.Count > 0 && !string.IsNullOrWhiteSpace(shots[0]?.FocusActorId))
            {
                var fUnit = FindSceneUnit(shots[0].FocusActorId);
                if (fUnit != null)
                {
                    preFocusPos = fUnit.transform.position;
                    hasPreFocus = true;
                }
            }

            if (!cine.InPlace && driveCamera && cine.StartTransition == CinematicCameraTransition.Smooth && shots.Count > 0 && shots[0] != null)
            {
                var firstShot = shots[0];
                Vector3 targetStartPos = firstShot.CamStartPos;
                Vector3 targetStartEuler = firstShot.CamStartEuler;
                float targetStartFov = firstShot.CamStartFov;

                if (firstShot.TrackFocusActor && !string.IsNullOrWhiteSpace(firstShot.FocusActorId))
                {
                    var tracked = FindSceneUnit(firstShot.FocusActorId);
                    if (tracked != null)
                    {
                        try
                        {
                            Vector3 anchor = new Vector3(tracked.transform.position.x, 0f, tracked.transform.position.z) + new Vector3(0f, 0.6f, 0f);
                            float dist = Mathf.Clamp(firstShot.TrackDistance, 0.5f, 35f);
                            Quaternion sRot = Quaternion.Euler(firstShot.CamStartEuler.x, firstShot.TrackYaw, firstShot.CamStartEuler.z);
                            targetStartPos = anchor - (sRot * Vector3.forward) * dist;
                            if (targetStartPos.y < 0.15f) targetStartPos.y = 0.15f;
                            targetStartEuler = new Vector3(firstShot.CamStartEuler.x, firstShot.TrackYaw, firstShot.CamStartEuler.z);
                        }
                        catch { /* ignore */ }
                    }
                }

                float startTransDur = Mathf.Max(0.05f, (cine.StartTransitionDuration > 0.01f ? cine.StartTransitionDuration : 0.5f) / speed);
                float startTransElapsed = 0f;
                while (startTransElapsed < startTransDur)
                {
                    while (CombatHUD.IsPaused) yield return null;
                    startTransElapsed += Mathf.Min(Time.unscaledDeltaTime, 0.1f);
                    float st = Mathf.Clamp01(startTransElapsed / startTransDur);
                    float se = ApplyCinematicEase(CinematicEase.Smooth, st);
                    transform.position = Vector3.Lerp(livePrePos, targetStartPos, se);
                    transform.rotation = Quaternion.Slerp(livePreRot, Quaternion.Euler(targetStartEuler), se);
                    if (_mainCamera != null) _mainCamera.fieldOfView = Mathf.Lerp(livePreFov, targetStartFov, se);
                    if (cine.Skippable && Input.GetKeyDown(KeyCode.Escape))
                    {
                        skipped = true;
                        break;
                    }
                    yield return null;
                }
            }

            for (int s = 0; s < shots.Count; s++)
            {
                var shot = shots[s];
                if (shot == null) continue;
                float duration = Mathf.Max(0.2f, shot.Duration / speed);

                // Poses acteurs : verrou anti-déplacement + téléport au START,
                // interpolation vers END, finalisation au END. Les doublons (même unité
                // résolue deux fois) sont ignorés par référence pour éviter un double pilotage.
                var tracks = new List<ShotActorTrack>();
                if (shot.StartPoses != null)
                {
                    for (int p = 0; p < shot.StartPoses.Count; p++)
                    {
                        var pose = shot.StartPoses[p];
                        if (pose == null || string.IsNullOrWhiteSpace(pose.ActorId)) continue;
                        var unit = FindSceneUnit(pose.ActorId);
                        if (unit == null || IsUnitTracked(tracks, null, unit)) continue;
                        unit.CinematicDriveActive = true;
                        SceneCinematicActorPose endPose = null;
                        if (shot.EndPoses != null)
                            endPose = shot.EndPoses.Find(e => e != null && string.Equals(e.ActorId, pose.ActorId, StringComparison.OrdinalIgnoreCase));
                        var track = new ShotActorTrack { Unit = unit, HasEnd = endPose != null };
                        if (TryHexWorld(grid, pose.Q, pose.R, out var sw))
                        {
                            try
                            {
                                // Échec possible (empreinte occupée) : l'unité reste sur place,
                                // on part donc de sa position RÉELLE pour rester cohérent.
                                if (grid != null) unit.TeleportTo(new HexCoordinates(pose.Q, pose.R), grid);
                                else unit.transform.position = sw;
                            }
                            catch { try { unit.transform.position = sw; } catch { /* ignore */ } }
                            try { unit.transform.rotation = Quaternion.Euler(0f, pose.FacingAngle, 0f); }
                            catch { /* ignore */ }
                            track.StartYaw = pose.FacingAngle;
                        }
                        else
                        {
                            track.StartYaw = unit.transform.rotation.eulerAngles.y;
                        }
                        try { track.StartWorld = unit.transform.position; }
                        catch { track.StartWorld = sw; }
                        if (endPose != null)
                        {
                            track.EndQ = endPose.Q;
                            track.EndR = endPose.R;
                            if (TryHexWorld(grid, endPose.Q, endPose.R, out var ew)) track.EndWorld = ew;
                            else track.EndWorld = track.StartWorld;
                            track.EndYaw = endPose.FacingAngle;
                        }
                        else
                        {
                            track.EndWorld = track.StartWorld;
                            track.EndYaw = track.StartYaw;
                        }
                        tracks.Add(track);
                    }
                }
                // Finisseurs seuls en END (sans START) : interpolation depuis la
                // position actuelle, ancrage logique en fin de plan.
                var endOnly = new List<ShotActorTrack>();
                if (shot.EndPoses != null)
                {
                    for (int p = 0; p < shot.EndPoses.Count; p++)
                    {
                        var pose = shot.EndPoses[p];
                        if (pose == null || string.IsNullOrWhiteSpace(pose.ActorId)) continue;
                        var unit = FindSceneUnit(pose.ActorId);
                        if (unit == null || IsUnitTracked(tracks, endOnly, unit)) continue;
                        unit.CinematicDriveActive = true;
                        var track = new ShotActorTrack { Unit = unit, HasEnd = true, EndQ = pose.Q, EndR = pose.R };
                        try
                        {
                            track.StartWorld = unit.transform.position;
                            track.StartYaw = unit.transform.rotation.eulerAngles.y;
                        }
                        catch { track.StartWorld = Vector3.zero; track.StartYaw = 0f; }
                        if (TryHexWorld(grid, pose.Q, pose.R, out var ew)) track.EndWorld = ew;
                        else track.EndWorld = track.StartWorld;
                        track.EndYaw = pose.FacingAngle;
                        endOnly.Add(track);
                    }
                }
                // Props (interactables) : Relocate logique+visuel au START, interpolation
                // visuelle pendant le plan, Relocate d'ancrage au END (CanInteract suit).
                var propTracks = new List<ShotPropTrack>();
                if (shot.StartProps != null)
                {
                    for (int p = 0; p < shot.StartProps.Count; p++)
                    {
                        var pose = shot.StartProps[p];
                        if (pose == null || string.IsNullOrWhiteSpace(pose.InteractableId)) continue;
                        var prop = FindSceneProp(pose.InteractableId);
                        if (prop == null || IsPropTracked(propTracks, prop)) continue;
                        var track = new ShotPropTrack { Prop = prop, HasEnd = false };
                        if (grid != null)
                        {
                            // Sans grille on ne touche à rien (pas de monde hex de référence).
                            try { prop.Relocate(new HexCoordinates(pose.Q, pose.R), grid); }
                            catch { /* ignore */ }
                        }
                        try { track.StartWorld = prop.transform.position; }
                        catch { track.StartWorld = Vector3.zero; }
                        SceneCinematicPropPose endPose = null;
                        if (shot.EndProps != null)
                            endPose = shot.EndProps.Find(e => e != null && string.Equals(e.InteractableId, pose.InteractableId, StringComparison.OrdinalIgnoreCase));
                        if (endPose != null)
                        {
                            track.EndQ = endPose.Q;
                            track.EndR = endPose.R;
                            if (TryHexWorld(grid, endPose.Q, endPose.R, out var ew))
                            {
                                track.EndWorld = ew;
                                track.HasEnd = true;
                            }
                            else track.EndWorld = track.StartWorld;
                        }
                        else track.EndWorld = track.StartWorld;
                        propTracks.Add(track);
                    }
                }

                // Focus + sous-titre + son du plan.
                if (!string.IsNullOrWhiteSpace(shot.FocusActorId))
                {
                    var focus = FindSceneUnit(shot.FocusActorId);
                    if (focus != null) handoverTarget = focus.transform;
                    try { focus?.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText($"🎬 {shot.Label}", Color.magenta); }
                    catch { /* ignore */ }
                }
                if (!string.IsNullOrWhiteSpace(shot.Speech))
                {
                    string who = string.IsNullOrWhiteSpace(shot.SpeakerId) ? cine.Title : shot.SpeakerId;
                    CombatHUD.Instance?.AddAdvancedLog($"🎬 <b>[{who}]</b> « {shot.Speech} »", LogCategory.MovementAndTurns, "[CINÉMATIQUE]", new Color(1f, 0.5f, 0.9f));
                }
                if (!string.IsNullOrWhiteSpace(shot.SoundCueId) && KilltimeAudioManager.Instance != null)
                {
                    try
                    {
                        if (Enum.TryParse<SoundId>(shot.SoundCueId, out var cue))
                            KilltimeAudioManager.Instance.PlayUI(cue, 0.6f);
                    }
                    catch { /* ignore */ }
                }

                bool isFinalShot = (s == shots.Count - 1) && (_sceneCinematicQueue.Count == 0);
                bool shotLetterbox = (cine == null || cine.Letterbox) && shot.Letterbox;
                _letterboxActive = shotLetterbox;

                float retractStartTime = isFinalShot && shotLetterbox
                    ? Mathf.Max(0f, duration - _letterboxExitDuration)
                    : float.MaxValue;
                bool exitRetractTriggered = false;

                // Cadrage effectif du plan : absolu par défaut, ou dérivé de la scène live.
                // - TrackFocusActor : framing calculé sur la position LIVE de FocusActorId
                //   (miroir TacticalCameraController : pivot au sol + 0.6m, pitch/yaw/dist).
                // - RelativeToCurrent : START = caméra live, END = START + (End − Start).
                // Le tracking est prioritaire si les deux sont cochés. Les valeurs du
                // shot ne sont JAMAIS mutées (copies locales : l'éditeur garde ses données).
                Vector3 effStartPos = shot.CamStartPos;
                Vector3 effStartEuler = shot.CamStartEuler;
                float effStartFov = shot.CamStartFov;
                Vector3 effEndPos = shot.CamEndPos;
                Vector3 effEndEuler = shot.CamEndEuler;
                float effEndFov = shot.CamEndFov;
                if (cine.InPlace)
                {
                    effStartPos = livePrePos;
                    effStartEuler = livePreRot.eulerAngles;
                    effStartFov = livePreFov;
                    effEndPos = livePrePos;
                    effEndEuler = livePreRot.eulerAngles;
                    effEndFov = livePreFov;
                }
                else if (shot.TrackFocusActor && !string.IsNullOrWhiteSpace(shot.FocusActorId))
                {
                    var tracked = FindSceneUnit(shot.FocusActorId);
                    if (tracked != null)
                    {
                        try
                        {
                            Vector3 anchor = new Vector3(tracked.transform.position.x, 0f, tracked.transform.position.z) + new Vector3(0f, 0.6f, 0f);
                            float dist = Mathf.Clamp(shot.TrackDistance, 0.5f, 35f);
                            Quaternion sRot = Quaternion.Euler(shot.CamStartEuler.x, shot.TrackYaw, shot.CamStartEuler.z);
                            Quaternion eRot = Quaternion.Euler(shot.CamEndEuler.x, shot.TrackYaw, shot.CamEndEuler.z);
                            effStartPos = anchor - (sRot * Vector3.forward) * dist;
                            effEndPos = anchor - (eRot * Vector3.forward) * dist;
                            if (effStartPos.y < 0.15f) effStartPos.y = 0.15f;
                            if (effEndPos.y < 0.15f) effEndPos.y = 0.15f;
                            effStartEuler = new Vector3(shot.CamStartEuler.x, shot.TrackYaw, shot.CamStartEuler.z);
                            effEndEuler = new Vector3(shot.CamEndEuler.x, shot.TrackYaw, shot.CamEndEuler.z);
                        }
                        catch { /* ignore : repli sur l'absolu */ }
                    }
                }
                else if (shot.RelativeToCurrent)
                {
                    Vector3 livePos = transform.position;
                    Vector3 liveEuler = transform.rotation.eulerAngles;
                    float liveFov = _mainCamera != null ? _mainCamera.fieldOfView : _normalFOV;
                    effStartPos = livePos;
                    effStartEuler = liveEuler;
                    effStartFov = liveFov;
                    effEndPos = livePos + (shot.CamEndPos - shot.CamStartPos);
                    effEndEuler = liveEuler + (shot.CamEndEuler - shot.CamStartEuler);
                    effEndFov = liveFov + (shot.CamEndFov - shot.CamStartFov);
                }

                Vector3 camPos = driveCamera ? effStartPos : Vector3.zero;
                Vector3 camEuler = driveCamera ? effStartEuler : Vector3.zero;
                if (driveCamera)
                {
                    transform.position = effStartPos;
                    transform.rotation = Quaternion.Euler(effStartEuler);
                    if (_mainCamera != null) _mainCamera.fieldOfView = effStartFov;
                }

                float startDelay = Mathf.Max(0f, shot.StartDelay / speed);
                bool wasPaused = CombatHUD.IsPaused;

                if (startDelay > 0.001f)
                {
                    float delayElapsed = 0f;
                    while (delayElapsed < startDelay)
                    {
                        while (CombatHUD.IsPaused) { wasPaused = true; yield return null; }
                        bool pausedNow = CombatHUD.IsPaused;
                        delayElapsed += Mathf.Min(Time.unscaledDeltaTime, 0.1f);
                        if (driveCamera)
                        {
                            Vector3 waitPos = effStartPos;
                            Vector3 waitEuler = effStartEuler;
                            float waitFov = effStartFov;
                            ApplyShotEffect(shot, 0f, 0f, ref waitPos, ref waitEuler, ref waitFov);
                            transform.position = waitPos;
                            transform.rotation = Quaternion.Euler(waitEuler);
                            if (_mainCamera != null) _mainCamera.fieldOfView = waitFov;
                        }
                        if (cine.Skippable && !pausedNow && !wasPaused && Input.GetKeyDown(KeyCode.Escape))
                        {
                            skipped = true;
                            CombatHUD.Instance?.AddAdvancedLog("⏩ <b>Cinématique passée.</b>", LogCategory.MovementAndTurns, "[CINÉMATIQUE]", Color.gray);
                            break;
                        }
                        wasPaused = pausedNow;
                        yield return null;
                    }
                }

                float elapsed = 0f;
                while (!skipped && elapsed < duration)
                {
                    while (CombatHUD.IsPaused) { wasPaused = true; yield return null; }
                    bool pausedNow = CombatHUD.IsPaused;
                    // Temps borné : un hitch (surtout à l'entrée de scène / Deploy :
                    // construction d'environnement, compilation shaders) fait bondir
                    // unscaledDeltaTime de plusieurs secondes et AVALE la cinématique
                    // (snap + retour instantané). Borné à 0.1s : la ciné dure sa vraie
                    // durée même en cas de rame (quitte à durer plus en temps mural).
                    elapsed += Mathf.Min(Time.unscaledDeltaTime, 0.1f);
                    float t = Mathf.Clamp01(elapsed / duration);
                    float e = ApplyCinematicEase(shot.Ease, t);

                    if (!exitRetractTriggered && elapsed >= retractStartTime)
                    {
                        exitRetractTriggered = true;
                        _letterboxActive = false;
                    }

                    if (driveCamera)
                    {
                        camPos = Vector3.Lerp(effStartPos, effEndPos, e);
                        camEuler = new Vector3(
                            Mathf.LerpAngle(effStartEuler.x, effEndEuler.x, e),
                            Mathf.LerpAngle(effStartEuler.y, effEndEuler.y, e),
                            Mathf.Lerp(effStartEuler.z, effEndEuler.z, e));
                        float fov = Mathf.Lerp(effStartFov, effEndFov, e);
                        ApplyShotEffect(shot, t, e, ref camPos, ref camEuler, ref fov);

                        // Injection additive de toute secousse concurrente (ex: focus Erika + secousse active)
                        camPos += _shakeOffsetPos;
                        camEuler += _shakeOffsetEuler;
                        fov += _shakeOffsetFov;

                        Transform camTrans = GetCameraTransform();
                        camTrans.position = camPos;
                        camTrans.rotation = Quaternion.Euler(camEuler);
                        if (_mainCamera != null) _mainCamera.fieldOfView = fov;
                    }

                    for (int t2 = 0; t2 < tracks.Count; t2++)
                    {
                        var tr = tracks[t2];
                        if (tr.Unit == null) continue;
                        try
                        {
                            tr.Unit.transform.position = Vector3.Lerp(tr.StartWorld, tr.EndWorld, e);
                            tr.Unit.transform.rotation = Quaternion.Euler(0f, Mathf.LerpAngle(tr.StartYaw, tr.EndYaw, e), 0f);
                        }
                        catch { /* ignore */ }
                    }
                    for (int t2 = 0; t2 < endOnly.Count; t2++)
                    {
                        var tr = endOnly[t2];
                        if (tr.Unit == null) continue;
                        try
                        {
                            tr.Unit.transform.position = Vector3.Lerp(tr.StartWorld, tr.EndWorld, e);
                            tr.Unit.transform.rotation = Quaternion.Euler(0f, Mathf.LerpAngle(tr.StartYaw, tr.EndYaw, e), 0f);
                        }
                        catch { /* ignore */ }
                    }
                    for (int t2 = 0; t2 < propTracks.Count; t2++)
                    {
                        var tr = propTracks[t2];
                        if (tr.Prop == null || !tr.HasEnd) continue;
                        try { tr.Prop.transform.position = Vector3.Lerp(tr.StartWorld, tr.EndWorld, e); }
                        catch { /* ignore */ }
                    }

                    // Skip Escape : seulement hors pause et hors transition de pause
                    // (sinon "reprendre" = "skipper", le symptôme "ça skip tout seul").
                    if (cine.Skippable && !pausedNow && !wasPaused && Input.GetKeyDown(KeyCode.Escape))
                    {
                        skipped = true;
                        CombatHUD.Instance?.AddAdvancedLog("⏩ <b>Cinématique passée.</b>", LogCategory.MovementAndTurns, "[CINÉMATIQUE]", Color.gray);
                        break;
                    }
                    wasPaused = pausedNow;
                    yield return null;
                }

                // Finalisation du plan : ancrage logique END (occupancy grille à jour),
                // libération systématique du verrou anti-déplacement. En cas d'échec
                // de téléport (case occupée), le visuel est recalé sur la logique
                // pour ne jamais laisser visuel/logique désynchronisés.
                for (int t2 = 0; t2 < tracks.Count; t2++)
                {
                    var tr = tracks[t2];
                    if (tr.Unit == null) continue;
                    try
                    {
                        if (tr.HasEnd)
                        {
                            // Sans grille il n'y a pas de logique hex à ancrer : le visuel fait foi.
                            bool ok = grid == null;
                            if (grid != null) ok = tr.Unit.TeleportTo(new HexCoordinates(tr.EndQ, tr.EndR), grid);
                            else tr.Unit.transform.position = tr.EndWorld;
                            if (!ok)
                            {
                                var n = grid != null ? grid.GetNode(tr.Unit.CurrentCoords) : null;
                                if (n != null) tr.Unit.transform.position = n.WorldPosition;
                            }
                            tr.Unit.transform.rotation = Quaternion.Euler(0f, tr.EndYaw, 0f);
                        }
                        tr.Unit.CinematicDriveActive = false;
                    }
                    catch
                    {
                        try { if (tr.Unit != null) tr.Unit.CinematicDriveActive = false; }
                        catch { /* ignore */ }
                    }
                }
                for (int t2 = 0; t2 < endOnly.Count; t2++)
                {
                    var tr = endOnly[t2];
                    if (tr.Unit == null) continue;
                    try
                    {
                        bool ok = grid == null;
                        if (grid != null) ok = tr.Unit.TeleportTo(new HexCoordinates(tr.EndQ, tr.EndR), grid);
                        else tr.Unit.transform.position = tr.EndWorld;
                        if (!ok)
                        {
                            var n = grid != null ? grid.GetNode(tr.Unit.CurrentCoords) : null;
                            if (n != null) tr.Unit.transform.position = n.WorldPosition;
                        }
                        tr.Unit.transform.rotation = Quaternion.Euler(0f, tr.EndYaw, 0f);
                        tr.Unit.CinematicDriveActive = false;
                    }
                    catch
                    {
                        try { if (tr.Unit != null) tr.Unit.CinematicDriveActive = false; }
                        catch { /* ignore */ }
                    }
                }
                for (int t2 = 0; t2 < propTracks.Count; t2++)
                {
                    var tr = propTracks[t2];
                    if (tr.Prop == null || !tr.HasEnd) continue;
                    try { tr.Prop.Relocate(new HexCoordinates(tr.EndQ, tr.EndR), grid); }
                    catch { /* ignore */ }
                }

                if (skipped) break;
            }

            bool chainMore = _sceneCinematicQueue.Count > 0;

            if (skipped)
            {
                _letterboxActive = false;
                _letterboxProgress = 0f;
            }
            else if (!chainMore)
            {
                _letterboxActive = false;
                while (_letterboxProgress > 0.001f)
                {
                    yield return null;
                }
                _letterboxProgress = 0f;
            }

            ClearAllCinematicLocks();
            // Si une autre cinématique suit, on enchaîne DIRECTEMENT (évite un double pop).
            if (driveCamera && !chainMore)
            {
                if (cine.InPlace)
                {
                    transform.position = livePrePos;
                    transform.rotation = livePreRot;
                    if (_mainCamera != null) _mainCamera.fieldOfView = livePreFov;
                }
                else if (cine.EndTransition == CinematicCameraTransition.Smooth && !skipped)
                {
                    Vector3 endFromPos = transform.position;
                    Quaternion endFromRot = transform.rotation;
                    float endFromFov = _mainCamera != null ? _mainCamera.fieldOfView : _normalFOV;

                    Vector3 targetTacticalPos = livePrePos;
                    if (hasPreFocus && handoverTarget != null)
                    {
                        targetTacticalPos += (handoverTarget.position - preFocusPos);
                    }

                    float endTransDur = Mathf.Max(0.05f, (cine.EndTransitionDuration > 0.01f ? cine.EndTransitionDuration : 0.5f) / speed);
                    float endTransElapsed = 0f;
                    while (endTransElapsed < endTransDur)
                    {
                        while (CombatHUD.IsPaused) yield return null;
                        endTransElapsed += Mathf.Min(Time.unscaledDeltaTime, 0.1f);
                        float et = Mathf.Clamp01(endTransElapsed / endTransDur);
                        float ee = ApplyCinematicEase(CinematicEase.Smooth, et);
                        transform.position = Vector3.Lerp(endFromPos, targetTacticalPos, ee);
                        transform.rotation = Quaternion.Slerp(endFromRot, livePreRot, ee);
                        if (_mainCamera != null) _mainCamera.fieldOfView = Mathf.Lerp(endFromFov, _normalFOV, ee);
                        if (cine.Skippable && Input.GetKeyDown(KeyCode.Escape)) break;
                        yield return null;
                    }
                }
                else
                {
                    if (_tacticalCam != null)
                    {
                        try { _tacticalCam.AdoptWorldPose(transform.position, transform.rotation.eulerAngles, handoverTarget); }
                        catch { /* ignore */ }
                    }
                }

                if (_tacticalCam != null) _tacticalCam.enabled = true;
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.ExitCinematicMode();
                Time.timeScale = DevTimeScale();
                CurrentMode = CameraMode.TacticalIsometric;
            }

            _activeSceneCinematicRoutine = null;
            CurrentPlayingSceneCinematic = null;
            var cb = _sceneCinematicOnComplete;
            _sceneCinematicOnComplete = null;
            try { cb?.Invoke(); } catch { /* ignore */ }

            if (chainMore) PlayNextQueuedCinematic();
        }

        private void PlayNextQueuedCinematic()
        {
            if (_sceneCinematicQueue.Count == 0 || IsPlayingSceneCinematic) return;
            var next = _sceneCinematicQueue.Dequeue();
            if (next.Data == null)
            {
                // Paranoïa (l'enqueue filtre déjà les requêtes vides) : ne jamais
                // avaler un callback ni caler la file.
                try { next.OnComplete?.Invoke(); } catch { /* ignore */ }
                PlayNextQueuedCinematic();
                return;
            }
            _sceneCinematicOnComplete = next.OnComplete;
            _activeSceneCinematicRoutine = StartCoroutine(SceneCinematicRoutine(next.Data));
        }

        private void ApplyShotEffect(SceneCinematicShotData shot, float t, float e, ref Vector3 pos, ref Vector3 euler, ref float fov)
        {
            if (shot == null || shot.MoveEffect == CinematicCameraEffect.None) return;
            float intensity = Mathf.Max(0.05f, shot.ShakeIntensity);
            float time = Time.unscaledTime + _skipNoiseSeed;
            switch (shot.MoveEffect)
            {
                case CinematicCameraEffect.HandheldShake:
                {
                    float fastTime = time * 26f;
                    float nx = (Mathf.PerlinNoise(fastTime, 0.3f) - 0.5f) * 2f;
                    float ny = (Mathf.PerlinNoise(1.7f, fastTime) - 0.5f) * 2f;
                    float nz = (Mathf.PerlinNoise(fastTime * 0.85f, 7.1f) - 0.5f) * 2f;

                    float rx = (Mathf.PerlinNoise(fastTime * 1.15f, 12f) - 0.5f) * 2f;
                    float ry = (Mathf.PerlinNoise(18f, fastTime * 1.15f) - 0.5f) * 2f;
                    float rz = (Mathf.PerlinNoise(fastTime * 1.35f, 25f) - 0.5f) * 2f;

                    float posScale = intensity * 1.5f;
                    float rotScale = intensity * 8.5f;

                    pos.x += nx * posScale;
                    pos.y += ny * posScale * 0.7f;
                    pos.z += nz * posScale * 0.8f;

                    euler.x += rx * rotScale * 0.6f;
                    euler.y += ry * rotScale * 0.6f;
                    euler.z += rz * rotScale;
                    break;
                }
                case CinematicCameraEffect.PushIn:
                {
                    Vector3 fwd = Quaternion.Euler(euler) * Vector3.forward;
                    pos += fwd * (e * (0.8f + intensity * 2f));
                    break;
                }
                case CinematicCameraEffect.PullOut:
                {
                    Vector3 fwd = Quaternion.Euler(euler) * Vector3.forward;
                    pos -= fwd * (e * (0.8f + intensity * 2f));
                    break;
                }
                case CinematicCameraEffect.OrbitLeft:
                    euler.y -= e * 22f;
                    break;
                case CinematicCameraEffect.OrbitRight:
                    euler.y += e * 22f;
                    break;
                case CinematicCameraEffect.FovPunch:
                    fov += Mathf.Sin(t * Mathf.PI) * (4f + intensity * 10f);
                    break;
                case CinematicCameraEffect.DutchSway:
                    euler.z += Mathf.Sin(e * Mathf.PI * 2f) * (2f + intensity * 4f);
                    break;
            }
        }

        private void Update()
        {
            UpdateLetterbox();

            // Basculer en mode Free Look avec la touche 'F' ou clic droit maintenu
            if (Input.GetKeyDown(KeyCode.F))
            {
                ToggleFreeLook();
            }

            if (CurrentMode == CameraMode.FreeLook)
            {
                HandleFreeLook();
            }
        }

        private void LateUpdate()
        {
            // Si aucune cinématique principale ne pilote la caméra mais qu'une secousse
            // in-place est active, on applique l'offset directement sur la caméra après TacticalCameraController.
            if (_activeSceneCinematicRoutine == null && (_shakeOffsetPos != Vector3.zero || _shakeOffsetEuler != Vector3.zero || _shakeOffsetFov != 0f))
            {
                Transform camTrans = GetCameraTransform();
                if (camTrans != null)
                {
                    camTrans.position += _shakeOffsetPos;
                    camTrans.rotation = Quaternion.Euler(camTrans.rotation.eulerAngles + _shakeOffsetEuler);
                    if (_mainCamera != null && _shakeOffsetFov != 0f)
                        _mainCamera.fieldOfView += _shakeOffsetFov;
                }
            }
        }

        private void UpdateLetterbox()
        {
            float target = _letterboxActive ? 1f : 0f;
            float duration = _letterboxActive ? Mathf.Max(0.01f, _letterboxEnterDuration) : Mathf.Max(0.01f, _letterboxExitDuration);
            float speed = 1f / duration;

            if (!Mathf.Approximately(_letterboxProgress, target))
            {
                _letterboxProgress = Mathf.MoveTowards(_letterboxProgress, target, Time.unscaledDeltaTime * speed);
            }
        }

        public void ToggleFreeLook()
        {
            if (CurrentMode == CameraMode.TacticalIsometric)
            {
                CurrentMode = CameraMode.FreeLook;
                _tacticalCam.enabled = false;
                
                Vector3 euler = transform.rotation.eulerAngles;
                _freePitch = euler.x;
                _freeYaw = euler.y;
                Cursor.lockState = CursorLockMode.Locked;
            }
            else if (CurrentMode == CameraMode.FreeLook)
            {
                CurrentMode = CameraMode.TacticalIsometric;
                _tacticalCam.enabled = true;
                Cursor.lockState = CursorLockMode.None;
            }
        }

        private void HandleFreeLook()
        {
            float mouseX = Input.GetAxis("Mouse X") * _freeLookSensitivity;
            float mouseY = Input.GetAxis("Mouse Y") * _freeLookSensitivity;

            _freeYaw += mouseX;
            _freePitch = Mathf.Clamp(_freePitch - mouseY, -80f, 80f);

            transform.rotation = Quaternion.Euler(_freePitch, _freeYaw, 0f);

            // Mouvement 3D libre
            float h = Input.GetAxis("Horizontal");
            float v = Input.GetAxis("Vertical");
            float upDown = 0f;
            if (Input.GetKey(KeyCode.Space)) upDown += 1f;
            if (Input.GetKey(KeyCode.LeftControl)) upDown -= 1f;

            Vector3 move = (transform.forward * v + transform.right * h + Vector3.up * upDown) * (_freeLookSpeed * Time.deltaTime);
            transform.position += move;
        }

        /// <summary>
        /// Déclenche un gros plan cinématique dramatique lors d'un tir ciblé ou d'un critique.
        /// En mode Free Look, la cinématique est inhibée : les callbacks s'enchaînent
        /// sans prise de contrôle caméra, sans ralenti ni changement de FOV.
        /// </summary>
        public void PlayCinematicKillshot(Transform attacker, Transform target, Action onStrikePoint, Action onComplete, Action onActionStart = null, Action onDefenseStart = null)
        {
            if (_activeCinematicRoutine != null)
            {
                StopCoroutine(_activeCinematicRoutine);
                _activeCinematicRoutine = null;
            }

            if (IsFreeLook)
            {
                _activeCinematicRoutine = StartCoroutine(NonCinematicRoutine(onStrikePoint, onComplete, onActionStart, onDefenseStart));
                return;
            }

            _activeCinematicRoutine = StartCoroutine(CinematicRoutine(attacker, target, onStrikePoint, onComplete, onActionStart, onDefenseStart));
        }

        /// <summary>
        /// Séquence d'attaque sans cinéma (Free Look) : même ordre d'appels que la
        /// routine non-cinématique de l'arène, sans toucher caméra / timeScale / FOV.
        /// </summary>
        private IEnumerator NonCinematicRoutine(Action onStrikePoint, Action onComplete, Action onActionStart = null, Action onDefenseStart = null)
        {
            onActionStart?.Invoke();
            yield return new WaitForSeconds(0.06f);
            onDefenseStart?.Invoke();
            yield return new WaitForSeconds(0.16f);
            onStrikePoint?.Invoke();
            yield return new WaitForSeconds(0.65f);
            _activeCinematicRoutine = null;
            onComplete?.Invoke();
        }

        private IEnumerator CinematicRoutine(Transform attacker, Transform target, Action onStrikePoint, Action onComplete, Action onActionStart = null, Action onDefenseStart = null)
        {
            CurrentMode = CameraMode.CinematicAction;
            _tacticalCam.enabled = false;
            if (KilltimeAudioManager.Instance != null)
                KilltimeAudioManager.Instance.EnterCinematicMode();

            Vector3 startCamPos = transform.position;
            Quaternion startCamRot = transform.rotation;
            float startFOV = _mainCamera != null ? _mainCamera.fieldOfView : _normalFOV;

            Vector3 attackerChest = attacker.position + Vector3.up * 1.0f;
            Vector3 targetChest = target.position + Vector3.up * 1.0f;
            Vector3 focalPoint = (attackerChest + targetChest) * 0.5f;

            Vector3 combatLine = target.position - attacker.position;
            combatLine.y = 0f;
            float combatDistance = Mathf.Max(combatLine.magnitude, 1.2f);
            Vector3 lineDir = combatLine.normalized;

            Vector3 sideDir = Vector3.Cross(lineDir, Vector3.up).normalized;
            if (sideDir == Vector3.zero) sideDir = Vector3.right;

            Vector3 cameraOffset = (sideDir * 1.5f - lineDir * 0.6f).normalized * (combatDistance * 0.85f + 4.2f) + Vector3.up * 1.75f;
            Vector3 cinematicPos = focalPoint + cameraOffset;

            if (cinematicPos.y < 1.1f)
            {
                cinematicPos.y = 1.1f;
            }

            Quaternion cinematicRot = Quaternion.LookRotation(focalPoint - cinematicPos, Vector3.up);

            // 1. Transition vers le plan cinématique
            float elapsed = 0f;
            float duration = 0.35f;

            while (elapsed < duration)
            {
                while (Time.timeScale <= 0.0001f) yield return null;
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0, 1, elapsed / duration);
                transform.position = Vector3.Lerp(startCamPos, cinematicPos, t);
                transform.rotation = Quaternion.Slerp(startCamRot, cinematicRot, t);
                _mainCamera.fieldOfView = Mathf.Lerp(startFOV, _cinematicFOV, t);
                yield return null;
            }

            // Déclenchement de l'attaque
            onActionStart?.Invoke();
            if (KilltimeAudioManager.Instance != null && attacker != null)
                KilltimeAudioManager.Instance.PlayAt(SoundId.Attack_Whoosh, attacker.position, 0.8f);

            // 2. Trajectoire de frappe en ralenti dramatique jusqu'au point de contact
            Time.timeScale = 0.45f;
            float windupTimer = 0f;
            bool defenseTriggered = false;

            while (windupTimer < 0.22f)
            {
                while (Time.timeScale <= 0.0001f) yield return null;
                windupTimer += Time.unscaledDeltaTime;

                if (!defenseTriggered && windupTimer >= 0.05f)
                {
                    defenseTriggered = true;
                    onDefenseStart?.Invoke();
                }

                yield return null;
            }

            if (!defenseTriggered)
            {
                onDefenseStart?.Invoke();
            }

            // Point d'impact physique : calculs, flash et apparition des textes au-dessus des têtes
            onStrikePoint?.Invoke();

            // Temps de lecture de l'impact pendant que la jambe termine son mouvement et que le texte flotte
            float readTimer = 0f;
            while (readTimer < 0.85f)
            {
                while (Time.timeScale <= 0.0001f) yield return null;
                readTimer += Time.unscaledDeltaTime;
                yield return null;
            }
            Time.timeScale = DevTimeScale();

            // 3. Retour fluide à la vue tactique
            elapsed = 0f;
            duration = 0.35f;

            while (elapsed < duration)
            {
                while (Time.timeScale <= 0.0001f) yield return null;
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0, 1, elapsed / duration);
                transform.position = Vector3.Lerp(cinematicPos, startCamPos, t);
                transform.rotation = Quaternion.Slerp(cinematicRot, startCamRot, t);
                _mainCamera.fieldOfView = Mathf.Lerp(_cinematicFOV, startFOV, t);
                yield return null;
            }

            _tacticalCam.enabled = true;
            CurrentMode = CameraMode.TacticalIsometric;
            _activeCinematicRoutine = null;
            if (KilltimeAudioManager.Instance != null)
                KilltimeAudioManager.Instance.ExitCinematicMode();
            onComplete?.Invoke();
        }
    }
}

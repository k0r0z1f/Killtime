using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Audio;
using Killtime.Story.Data;
using Killtime.Story.Scenes;
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
    public partial class CinematicDirector : MonoBehaviour
    {
        [Header("Composants")]
        [SerializeField] private TacticalCameraController _tacticalCam;
        [SerializeField] private UnityEngine.Camera _mainCamera;

        [Header("Configuration Free Look")]
        [SerializeField] private float _freeLookSensitivity = 2.5f;
        [SerializeField] private float _freeLookSpeed = 12.0f;
        [SerializeField] private float _freeLookFastMultiplier = 2.5f;

        [Header("Configuration Cinématique")]
        [SerializeField] private float _cinematicFOV = 48.0f;
        [SerializeField] private float _normalFOV = 60.0f;
        [SerializeField] private float _cinematicFarClip = 1000000.0f;

        [Header("Clipping adaptatif (espace lointain)")]
        [Tooltip("Si coché, le near clip suit la distance caméra (évite le z-fighting à 1M).")]
        [SerializeField] private bool _adaptiveNearEnabled = true;
        [Tooltip("near = distance * ratio, borné par min/max. 0.005 = même règle que l'inspecteur d'environnement.")]
        [SerializeField] private float _adaptiveNearRatio = 0.005f;
        [SerializeField] private float _adaptiveNearMin = 0.3f;
        [SerializeField] private float _adaptiveNearMax = 5000f;

        public CameraMode CurrentMode { get; private set; } = CameraMode.TacticalIsometric;

        /// <summary>
        /// Vrai quand l'utilisateur pilote la caméra librement : aucune cinématique
        /// (killshot d'attaque ou autre) ne doit s'enclencher ni lui voler la caméra.
        /// </summary>
        public bool IsFreeLook => CurrentMode == CameraMode.FreeLook;

        /// <summary>
        /// Vitesse du jeu dev (0.01x → 100x, fenêtre F10, session uniquement) :
        /// les plans d'action restaurent cette consigne au lieu de forcer 1x,
        /// sinon le réglage serait écrasé après chaque killcam.
        /// </summary>
        private static float DevTimeScale()
        {
            try
            {
                return Killtime.UI.TimeScaleDevWindow.CurrentTimeScale;
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
        // la carte suivante s'enclenche immédiatement. Si une cinématique caméra
        // est déjà en cours, la nouvelle la PRÉEMPTE fluidement (handover depuis
        // la position live, sans retour tactique ni snap) au lieu d'attendre en
        // file. Seul un batch même-frame (plusieurs ids d'un coup) s'enfile.
        private Coroutine _activeSceneCinematicRoutine;
        private struct QueuedCinematic
        {
            public SceneCinematicData Data;
            public Action OnComplete;
        }
        private readonly Queue<QueuedCinematic> _sceneCinematicQueue = new();
        private Action _sceneCinematicOnComplete;
        // Raccord fluide demandé par une préemption : force une transition d'entrée
        // Smooth même si la nouvelle ciné est réglée en Téléportation.
        private bool _handoverSmoothRequested;
        private bool _letterboxActive;
        private float _letterboxProgress;
        [SerializeField] private float _letterboxEnterDuration = 0.35f;
        [SerializeField] private float _letterboxExitDuration = 0.45f;
        private float _skipNoiseSeed;
        private AudioSource _activeShotAudioSource;
        private string _activeShotSoundCueId = "";
        private bool _isAudioPausedByDirector;
        private bool _swellSequenceConsumed;
        private Coroutine _activeAudioLimitRoutine;

        private static AudioSource _activeSwellAudioSource;
        private static string _activeSwellCueId = "";

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
            _handoverSmoothRequested = false;
            _letterboxActive = false;
            _letterboxProgress = 0f;
            if (_activeAudioLimitRoutine != null)
            {
                StopCoroutine(_activeAudioLimitRoutine);
                _activeAudioLimitRoutine = null;
            }
            _activeShotAudioSource = null;
            _activeShotSoundCueId = "";
            _isAudioPausedByDirector = false;
            _swellSequenceConsumed = false;
            ClearAllCinematicLocks();
            ResetSkyboxWarp();

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
            ResetAdaptiveNear();

            CurrentMode = CameraMode.TacticalIsometric;
        }

        private void Awake()
        {
            if (_mainCamera == null)
            {
                _mainCamera = GetComponent<UnityEngine.Camera>() ?? UnityEngine.Camera.main;
            }
            if (_mainCamera != null)
            {
                if (_normalFOV > 0) _mainCamera.fieldOfView = _normalFOV;
                float targetFar = Mathf.Max(_cinematicFarClip, 1000000f);
                if (_mainCamera.farClipPlane < targetFar) _mainCamera.farClipPlane = targetFar;
                if (_mainCamera.nearClipPlane < 0.3f) _mainCamera.nearClipPlane = 0.3f;
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
    }
}

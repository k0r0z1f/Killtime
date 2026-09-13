using System;
using System.Collections;
using UnityEngine;

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

        private float _freePitch = 0.0f;
        private float _freeYaw = 0.0f;
        private Coroutine _activeCinematicRoutine;

        public void ResetCinematicState()
        {
            if (_activeCinematicRoutine != null)
            {
                StopCoroutine(_activeCinematicRoutine);
                _activeCinematicRoutine = null;
            }

            Time.timeScale = 1.0f;

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
        }

        private void Update()
        {
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
        /// </summary>
        public void PlayCinematicKillshot(Transform attacker, Transform target, Action onStrikePoint, Action onComplete, Action onActionStart = null)
        {
            if (_activeCinematicRoutine != null)
            {
                StopCoroutine(_activeCinematicRoutine);
            }

            _activeCinematicRoutine = StartCoroutine(CinematicRoutine(attacker, target, onStrikePoint, onComplete, onActionStart));
        }

        private IEnumerator CinematicRoutine(Transform attacker, Transform target, Action onStrikePoint, Action onComplete, Action onActionStart = null)
        {
            CurrentMode = CameraMode.CinematicAction;
            _tacticalCam.enabled = false;

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

            // Déclenchement du roundkick juste avant l'impact
            onActionStart?.Invoke();

            // 2. Trajectoire de frappe en ralenti dramatique jusqu'au point de contact
            Time.timeScale = 0.45f;
            float windupTimer = 0f;
            while (windupTimer < 0.22f)
            {
                while (Time.timeScale <= 0.0001f) yield return null;
                windupTimer += Time.unscaledDeltaTime;
                yield return null;
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
            Time.timeScale = 1.0f;

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
            onComplete?.Invoke();
        }
    }
}

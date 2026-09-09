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
        [SerializeField] private float _cinematicFOV = 40.0f;
        [SerializeField] private float _normalFOV = 60.0f;

        public CameraMode CurrentMode { get; private set; } = CameraMode.TacticalIsometric;

        private float _freePitch = 0.0f;
        private float _freeYaw = 0.0f;
        private Coroutine _activeCinematicRoutine;

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
        public void PlayCinematicKillshot(Transform attacker, Transform target, Action onStrikePoint, Action onComplete)
        {
            if (_activeCinematicRoutine != null)
            {
                StopCoroutine(_activeCinematicRoutine);
            }

            _activeCinematicRoutine = StartCoroutine(CinematicRoutine(attacker, target, onStrikePoint, onComplete));
        }

        private IEnumerator CinematicRoutine(Transform attacker, Transform target, Action onStrikePoint, Action onComplete)
        {
            CurrentMode = CameraMode.CinematicAction;
            _tacticalCam.enabled = false;

            Vector3 startCamPos = transform.position;
            Quaternion startCamRot = transform.rotation;
            float startFOV = _mainCamera.fieldOfView;

            // Position d'angle dramatique (vue rapprochée légèrement en contre-plongée)
            Vector3 midPoint = (attacker.position + target.position) * 0.5f;
            Vector3 offset = Vector3.Cross(target.position - attacker.position, Vector3.up).normalized * 3.5f + Vector3.up * 1.5f;
            Vector3 cinematicPos = midPoint + offset;
            Quaternion cinematicRot = Quaternion.LookRotation(target.position - cinematicPos);

            // 1. Transition rapide vers le plan cinématique
            float elapsed = 0f;
            float duration = 0.4f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.SmoothStep(0, 1, elapsed / duration);
                transform.position = Vector3.Lerp(startCamPos, cinematicPos, t);
                transform.rotation = Quaternion.Slerp(startCamRot, cinematicRot, t);
                _mainCamera.fieldOfView = Mathf.Lerp(startFOV, _cinematicFOV, t);
                yield return null;
            }

            // 2. Léger ralenti (Bullet-Time) à l'impact
            Time.timeScale = 0.35f;
            yield return new WaitForSecondsRealtime(0.5f);

            // Point d'impact (calculs et déclenchement FX)
            onStrikePoint?.Invoke();

            yield return new WaitForSecondsRealtime(0.8f);
            Time.timeScale = 1.0f;

            // 3. Retour fluide à la vue tactique
            elapsed = 0f;
            duration = 0.5f;

            while (elapsed < duration)
            {
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

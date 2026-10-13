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
    public partial class CinematicDirector : MonoBehaviour
    {

        public void ToggleFreeLook()
        {
            if (CurrentMode == CameraMode.TacticalIsometric)
            {
                CurrentMode = CameraMode.FreeLook;
                ResolveTacticalCam();
                if (_tacticalCam != null) _tacticalCam.enabled = false;
                
                Vector3 euler = transform.rotation.eulerAngles;
                _freePitch = euler.x;
                _freeYaw = euler.y;
                Cursor.lockState = CursorLockMode.Locked;
            }
            else if (CurrentMode == CameraMode.FreeLook)
            {
                CurrentMode = CameraMode.TacticalIsometric;
                ResolveTacticalCam();
                if (_tacticalCam != null)
                {
                    _tacticalCam.AdoptWorldPose(transform.position, transform.rotation.eulerAngles);
                    _tacticalCam.enabled = true;
                }
                ResetAdaptiveNear();
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

            float speedMultiplier = (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) ? _freeLookFastMultiplier : 1.0f;
            Vector3 move = (transform.forward * v + transform.right * h + Vector3.up * upDown) * (_freeLookSpeed * speedMultiplier * Time.deltaTime);
            transform.position += move;
            UpdateAdaptiveNear(transform.position);
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
            if (KilltimeAudioManager.Instance != null)
            {
                KilltimeAudioManager.Instance.NotifyCombatStrike(false, false, 0.8f);
            }

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

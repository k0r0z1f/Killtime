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

        // ---------- API publique cinématiques de scène ----------

        /// <summary>
        /// Déclenche une cinématique de scène SANS bloquer : retour immédiat,
        /// la carte suivante peut s'enclencher pendant la lecture.
        /// Si une cinématique caméra est en cours, la nouvelle la préempte
        /// FLUIDEMENT (handover live, pas de snap) sauf si preemptCurrent=false
        /// (batch même-frame : mise en file derrière).
        /// </summary>
        public void PlaySceneCinematic(SceneCinematicData cine, Action onComplete = null, bool preemptCurrent = true)
        {
            if (cine == null || cine.Shots == null || cine.Shots.Count == 0) return;

            // Une cinématique sur place ne prend pas le contrôle exclusif de la caméra
            // et ne s'empile pas : elle s'exécute immédiatement en superposition additive.
            if (cine.InPlace)
            {
                if (cine.Shots.Count > 0 && cine.Shots[0] != null && cine.Shots[0].SuperluminalWarp) SetSkyboxWarp(1f);
                StartCoroutine(InPlaceCinematicRoutine(cine, onComplete));
                return;
            }

            if (IsPlayingSceneCinematic)
            {
                if (!preemptCurrent)
                {
                    _sceneCinematicQueue.Enqueue(new QueuedCinematic { Data = cine, OnComplete = onComplete });
                    return;
                }
                // Préemption fluide : on coupe l'ancienne SANS restaurer la caméra
                // tactique (ni FOV, ni letterbox, ni timeScale) pour enchaîner depuis
                // la position live exacte. L'ancienne sortie "fin →" est abandonnée
                // (un abort ne chaîne pas — cf StopSceneCinematic). La file existante
                // est conservée et jouera après la nouvelle.
                if (_activeSceneCinematicRoutine != null)
                {
                    try { StopCoroutine(_activeSceneCinematicRoutine); }
                    catch { /* ignore */ }
                    _activeSceneCinematicRoutine = null;
                }
                ClearAllCinematicLocks();
                _sceneCinematicOnComplete = null;
                CurrentPlayingSceneCinematic = null;
                _handoverSmoothRequested = true;
                try { CombatHUD.Instance?.AddAdvancedLog($"🔀 <b>Cinématique préemptée →</b> {cine.Title} [{cine.CinematicId}] (raccord fluide).", LogCategory.MovementAndTurns, "[CINÉMATIQUE]", new Color(1f, 0.5f, 0.9f)); }
                catch { /* ignore */ }
                _sceneCinematicOnComplete = onComplete;
                CurrentPlayingSceneCinematic = cine;
                if (cine.Shots.Count > 0 && cine.Shots[0] != null && cine.Shots[0].SuperluminalWarp) SetSkyboxWarp(1f);
                _activeSceneCinematicRoutine = StartCoroutine(SceneCinematicRoutine(cine));
                return;
            }
            _sceneCinematicOnComplete = onComplete;
            if (cine.Shots.Count > 0 && cine.Shots[0] != null && cine.Shots[0].SuperluminalWarp) SetSkyboxWarp(1f);
            _activeSceneCinematicRoutine = StartCoroutine(SceneCinematicRoutine(cine));
        }

        private IEnumerator InPlaceCinematicRoutine(SceneCinematicData cine, Action onComplete)
        {
            float speed = cine.PlaybackSpeed > 0.01f ? cine.PlaybackSpeed : 1f;
            var shots = cine.Shots != null ? new List<SceneCinematicShotData>(cine.Shots) : new List<SceneCinematicShotData>();
            Debug.Log("[CINE] v14 routine sur-place " + (cine != null ? cine.CinematicId : "?") + " : " + shots.Count + " plans");
            try { CombatHUD.Instance?.AddAdvancedLog("<color=grey>[CINE] v14 sur-place " + (cine != null ? cine.CinematicId : "?") + " : " + shots.Count + " plans</color>", LogCategory.MovementAndTurns, "[CINÉMATIQUE]", Color.gray); }
            catch { /* ignore */ }

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

                if (startDelay > 0.001f)
                {
                    float delayElapsed = 0f;
                    while (delayElapsed < startDelay)
                    {
                        while (CombatHUD.IsPaused) yield return null;
                        delayElapsed += Mathf.Min(Time.unscaledDeltaTime, 0.1f);
                        // Warp tenu à 1 pendant l'attente (toujours vitesse lumière).
                        if (shot.SuperluminalWarp) SetSkyboxWarp(1f);
                        if (cine.Skippable && Input.GetKeyDown(KeyCode.Escape)) { skipped = true; break; }
                        yield return null;
                    }
                }
                if (skipped) break;

                while (CombatHUD.IsPaused) yield return null;

                if (!string.IsNullOrWhiteSpace(shot.SoundCueId))
                {
                    PlayShotAudio(shot);
                }

                float elapsed = 0f;
                while (elapsed < duration)
                {
                    if (CombatHUD.IsPaused)
                    {
                        if (_activeShotAudioSource != null && _activeShotAudioSource.isPlaying)
                        {
                            _activeShotAudioSource.Pause();
                            _isAudioPausedByDirector = true;
                        }
                        while (CombatHUD.IsPaused) yield return null;
                        if (_activeShotAudioSource != null && _isAudioPausedByDirector)
                        {
                            _activeShotAudioSource.UnPause();
                            _isAudioPausedByDirector = false;
                        }
                    }
                    elapsed += Mathf.Min(Time.unscaledDeltaTime, 0.1f);
                    float t = Mathf.Clamp01(elapsed / duration);
                    float e = ApplyCinematicEase(shot.Ease, t);

                    // Warp v2 : palier singularité (noir + 1px) puis
                    // point qui grossit → stries → voûte reformée.
                    if (shot.SuperluminalWarp) SetSkyboxWarp(WarpAmountForShotProgress(t, shot.Ease));

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

                if (shot.SuperluminalWarp) ResetSkyboxWarp();
                if (skipped) break;
            }

            _shakeOffsetPos = Vector3.zero;
            _shakeOffsetEuler = Vector3.zero;
            _shakeOffsetFov = 0f;
            ResetSkyboxWarp();

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
            ResetAdaptiveNear();
            ResetSkyboxWarp();
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
            ResetAdaptiveNear();
            ResetSkyboxWarp();
            _letterboxActive = false;
        }
    }
}

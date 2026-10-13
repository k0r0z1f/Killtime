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

        /// <summary>
        /// Effet supraluminique sur la voûte céleste : 1 = point central
        /// (vitesse lumière), 0 = voûte normale. Piloté par les plans cochés
        /// « warp » (1→0 sur la durée du plan = ralenti qui reforme la voûte).
        /// Reset systématique à 0 (fin/skip/abort/stop) pour ne jamais laisser
        /// la skybox compressée.
        /// </summary>
        private static void SetSkyboxWarp(float warp)
        {
            try { SpaceEnvironment.SetWarpForAll(warp); }
            catch { /* HUD/skybox optionnels */ }
        }

        private static void ResetSkyboxWarp()
        {
            try { SpaceEnvironment.SetWarpForAll(0f); }
            catch { /* ignore */ }
        }

        /// <summary>
        /// Courbe relativiste de vitesse supraluminique et décélération (RD-110, réf vidéo) :
        /// - t &lt;= CruiseFraction (~3s sur 15s) : plein régime supraluminique (warp = 1).
        ///   Aberration maximale : cône central lumineux, Doppler blueshift intense, fond noir.
        /// - ensuite (~12s sur 15s) : grande décélération continue et progressive.
        ///   L'aberration se relâche de façon fluide : les étoiles jaillissent en éventail radial
        ///   du centre vers la périphérie de l'écran avec étirement continu (1 - u)^1.35.
        /// </summary>
        private static float WarpAmountForShotProgress(float t, CinematicEase ease)
        {
            t = Mathf.Clamp01(t);
            const float CruiseFraction = 0.20f;
            if (t <= CruiseFraction) return 1f;

            float u = (t - CruiseFraction) / (1f - CruiseFraction);
            float decel = 1f - u;
            return Mathf.Pow(Mathf.Clamp01(decel), 1.35f);
        }

        /// <summary>
        /// Near adaptatif pour les plans spatiaux : le ratio far/near explose à
        /// 1M/0.3 = 3.3M (z-fighting surface/nuages). On remonte le near avec la
        /// distance caméra (même règle x0.005 que l'inspecteur d'environnement) :
        /// à 600km → near ~3000m (ratio ~333), au sol → 0.3m.
        /// Basé sur la distance à l'origine (centre tactique), proxy continu qui
        /// suit le travel sans pop (camPos interpolée → near interpolé).
        /// </summary>
        private void UpdateAdaptiveNear(Vector3 camPos)
        {
            if (!_adaptiveNearEnabled || _mainCamera == null) return;
            try
            {
                float dist = camPos.magnitude;
                float ratio = _adaptiveNearRatio > 0.0001f ? _adaptiveNearRatio : 0.005f;
                float min = Mathf.Max(0.05f, _adaptiveNearMin);
                float max = Mathf.Max(min, _adaptiveNearMax);
                float targetNear = Mathf.Clamp(dist * ratio, min, max);
                // Garantit far >> near (au moins 10x) pour éviter une pyramide inversée.
                float far = Mathf.Max(_mainCamera.farClipPlane, 1000000f);
                targetNear = Mathf.Min(targetNear, far * 0.1f);
                _mainCamera.nearClipPlane = targetNear;
            }
            catch { /* ignore */ }
        }

        private void ResetAdaptiveNear()
        {
            try
            {
                if (_mainCamera != null)
                {
                    float fallback = Mathf.Max(0.3f, _adaptiveNearMin);
                    _mainCamera.nearClipPlane = fallback;
                }
            }
            catch { /* ignore */ }
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

        private void ApplyShotEffect(SceneCinematicShotData shot, float t, float e, ref Vector3 pos, ref Vector3 euler, ref float fov)
        {
            if (shot == null) return;

            // Micro-vibrations physiques pendant le vol supraluminique et le freinage (réf vidéo RD-110)
            if (shot.SuperluminalWarp)
            {
                float warpVal = WarpAmountForShotProgress(t, shot.Ease);
                if (warpVal > 0.005f)
                {
                    float fastTime = (Time.unscaledTime + _skipNoiseSeed) * 38f;
                    float decelRumble = warpVal * (1.15f - warpVal * 0.35f) * 0.04f;
                    pos.x += Mathf.Sin(fastTime * 1.15f) * decelRumble;
                    pos.y += Mathf.Cos(fastTime * 1.35f) * decelRumble * 0.7f;
                    euler.z += Mathf.Sin(fastTime * 0.95f) * decelRumble * 1.8f;
                }
            }

            if (shot.MoveEffect == CinematicCameraEffect.None) return;
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

            // Basculer en mode Free Look avec la touche 'F' (ignoré en cours de saisie texte UI)
            if (Input.GetKeyDown(KeyCode.F) && GUIUtility.keyboardControl == 0)
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
    }
}

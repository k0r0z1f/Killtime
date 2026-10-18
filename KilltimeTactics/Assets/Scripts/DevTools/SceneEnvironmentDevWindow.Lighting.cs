// PARTIAL-SPLIT: SceneEnvironmentDevWindow.Lighting.cs - onglet lumieres et soleil. Deplacement verbatim, logique inchangee.
using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Story;
using Killtime.Story.Data;
using Killtime.Story.Scenes;
using Killtime.CameraSystem;
using Killtime.Tactics.Grid;

namespace Killtime.UI
{
    public partial class SceneEnvironmentDevWindow : FloatingWindow<SceneEnvironmentDevWindow>
    {
        private void DrawLightingTab()
        {
            FindSceneLighting();
            var lighting = _activeData.Lighting;

            string lightName = _directionalLight != null ? _directionalLight.name : "<color=red>Aucune</color>";
            GUILayout.Label($"<b>1. Directional Light // Source Principale (<color=#00E5FF>{lightName}</color>) :</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            lighting.OverrideLighting = GUILayout.Toggle(lighting.OverrideLighting, "<b>Activer le pilotage temps réel de la lumière</b>");

            if (_directionalLight == null)
            {
                GUILayout.Label("<color=red>⚠️ Aucune Directional Light trouvée dans la scène.</color>");
            }
            else
            {
                _directionalLight.cullingMask = ~0;

                GUILayout.Label("<b>Position Spatiale de la Source (X, Y, Z) :</b>");
                Vector3 sunPos = lighting.SunPosition;
                sunPos.x = DrawWideAxisControl("Pos X", sunPos.x, -2500f, 2500f, 10f);
                sunPos.y = DrawWideAxisControl("Pos Y", sunPos.y, -1000f, 2500f, 10f);
                sunPos.z = DrawWideAxisControl("Pos Z", sunPos.z, -2500f, 2500f, 10f);
                if (sunPos != lighting.SunPosition)
                {
                    lighting.SunPosition = sunPos;
                    if (lighting.OverrideLighting) _directionalLight.transform.position = sunPos;
                }

                GUILayout.Space(6);

                GUILayout.Label("<b>Orientation Complète du Faisceau (Angles Euler) :</b>");
                Vector3 sunRot = lighting.SunEulerAngles;
                sunRot.x = DrawAngleControl("Pitch (X) Assiette", sunRot.x);
                sunRot.y = DrawAngleControl("Yaw (Y) Direction", sunRot.y);
                sunRot.z = DrawAngleControl("Roll (Z) Roulis", sunRot.z);

                if (sunRot != lighting.SunEulerAngles)
                {
                    lighting.SunEulerAngles = sunRot;
                    if (lighting.OverrideLighting) _directionalLight.transform.rotation = Quaternion.Euler(sunRot);
                }

                GUILayout.Space(4);

                GUILayout.BeginHorizontal();
                var curItem = (_unifiedItems.Count > 0 && _selectedItemIndex < _unifiedItems.Count) ? _unifiedItems[_selectedItemIndex] : null;

                if (curItem != null && GUILayout.Button($"🎯 Viser {curItem.Id}", GUILayout.Height(24)))
                {
                    AimSunAtTarget(curItem.Position);
                }
                if (GUILayout.Button("🏠 Viser Station (0,0,0)", GUILayout.Height(24)))
                {
                    AimSunAtTarget(Vector3.zero);
                }
                if (GUILayout.Button("🔄 Inverser (180°)", GUILayout.Height(24)))
                {
                    InvertSunDirection();
                }
                if (GUILayout.Button("📐 Terminatrice Verticale (Pitch 0°)", GUILayout.Height(24)))
                {
                    Vector3 r = lighting.SunEulerAngles;
                    r.x = 0f;
                    lighting.SunEulerAngles = r;
                    lighting.OverrideLighting = true;
                    if (_directionalLight != null) _directionalLight.transform.rotation = Quaternion.Euler(r);
                    _statusMessage = "Faisceau aligné à Pitch 0° (terminatrice verticale méridienne).";
                }
                GUILayout.EndHorizontal();

                GUILayout.Space(6);

                GUILayout.BeginHorizontal();
                GUILayout.Label($"Intensité : <b>{lighting.SunIntensity:0.##} Lux</b>", GUILayout.Width(150));
                lighting.SunIntensity = GUILayout.HorizontalSlider(lighting.SunIntensity, 0.0f, 15.0f);
                string intStr = GUILayout.TextField(lighting.SunIntensity.ToString("0.##"), GUILayout.Width(50));
                float.TryParse(intStr, out lighting.SunIntensity);
                if (lighting.OverrideLighting) _directionalLight.intensity = lighting.SunIntensity;
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("Ombres :", GUILayout.Width(75));
                lighting.Shadows = (LightShadows)GUILayout.Toolbar((int)lighting.Shadows, new[] { "Aucune", "Nettes", "Douces" }, GUILayout.Height(20));
                if (lighting.OverrideLighting) _directionalLight.shadows = lighting.Shadows;
                GUILayout.EndHorizontal();

                if (lighting.Shadows != LightShadows.None)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"Force Ombres : <b>{lighting.ShadowStrength:0.##}</b>", GUILayout.Width(150));
                    lighting.ShadowStrength = GUILayout.HorizontalSlider(lighting.ShadowStrength, 0.0f, 1.0f);
                    if (lighting.OverrideLighting) _directionalLight.shadowStrength = lighting.ShadowStrength;
                    GUILayout.EndHorizontal();
                }

                GUILayout.Space(4);

                GUILayout.Label("Couleur Spectrale (RGB) :");
                Color col = lighting.SunColor;
                col.r = DrawColorSlider("Rouge", col.r);
                col.g = DrawColorSlider("Vert", col.g);
                col.b = DrawColorSlider("Bleu", col.b);

                if (col != lighting.SunColor)
                {
                    lighting.SunColor = col;
                    if (lighting.OverrideLighting) _directionalLight.color = col;
                }

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Blanc Solaire")) ApplySunColor(new Color(1f, 0.98f, 0.92f));
                if (GUILayout.Button("Écarlate Nefris")) ApplySunColor(new Color(1.0f, 0.45f, 0.15f));
                if (GUILayout.Button("Bleu Stellaire")) ApplySunColor(new Color(0.75f, 0.90f, 1.0f));
                if (GUILayout.Button("Ambre Toxique")) ApplySunColor(new Color(1.0f, 0.78f, 0.25f));
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();

            GUILayout.Space(8);
            GUILayout.Label("<b>2. Ambiance Globale (Éclairage Indirect / Ombres) :</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            Color amb = lighting.AmbientColor;
            amb.r = DrawColorSlider("Ambiance R", amb.r);
            amb.g = DrawColorSlider("Ambiance V", amb.g);
            amb.b = DrawColorSlider("Ambiance B", amb.b);

            if (amb != lighting.AmbientColor)
            {
                lighting.AmbientColor = amb;
                if (lighting.OverrideLighting)
                {
                    RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                    RenderSettings.ambientLight = amb;
                }
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Ambiance Sombre (0.15)")) ApplyAmbientColor(new Color(0.15f, 0.15f, 0.18f));
            if (GUILayout.Button("Ambiance Modérée (0.35)")) ApplyAmbientColor(new Color(0.32f, 0.35f, 0.42f));
            if (GUILayout.Button("Ambiance Chaude Nefris")) ApplyAmbientColor(new Color(0.40f, 0.25f, 0.18f));
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
        }

        private float DrawAngleControl(string label, float angle)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(160));

            if (GUILayout.Button("-45°", GUILayout.Width(40))) angle -= 45f;
            if (GUILayout.Button("-15°", GUILayout.Width(35))) angle -= 15f;
            if (GUILayout.Button("0°", GUILayout.Width(28))) angle = 0f;
            if (GUILayout.Button("+15°", GUILayout.Width(35))) angle += 15f;
            if (GUILayout.Button("+45°", GUILayout.Width(40))) angle += 45f;

            string txt = GUILayout.TextField(angle.ToString("0.#"), GUILayout.Width(55));
            float.TryParse(txt, out angle);

            angle = GUILayout.HorizontalSlider(angle, -180f, 180f);
            GUILayout.EndHorizontal();
            return NormalizeAngle(angle);
        }

        private float DrawColorSlider(string label, float val)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(100));
            val = GUILayout.HorizontalSlider(val, 0f, 1f);
            string txt = GUILayout.TextField(val.ToString("0.##"), GUILayout.Width(45));
            float.TryParse(txt, out val);
            GUILayout.EndHorizontal();
            return Mathf.Clamp01(val);
        }

        private void ApplySunColor(Color c)
        {
            _activeData.Lighting.SunColor = c;
            if (_activeData.Lighting.OverrideLighting && _directionalLight != null)
            {
                _directionalLight.color = c;
            }
        }

        private void ApplyAmbientColor(Color c)
        {
            _activeData.Lighting.AmbientColor = c;
            if (_activeData.Lighting.OverrideLighting)
            {
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = c;
            }
        }

        private void AimSunAtTarget(Vector3 targetPos)
        {
            FindSceneLighting();
            if (_directionalLight == null) return;

            Vector3 originPos = _activeData.Lighting.SunPosition;
            Vector3 aimDir = (targetPos - originPos).normalized;
            if (aimDir == Vector3.zero) aimDir = Vector3.down;

            Quaternion targetRot = Quaternion.LookRotation(aimDir);
            _activeData.Lighting.SunEulerAngles = targetRot.eulerAngles;
            _activeData.Lighting.OverrideLighting = true;

            _directionalLight.transform.rotation = targetRot;
            _statusMessage = $"Faisceau orienté vers ({targetPos.x:0}, {targetPos.y:0}, {targetPos.z:0}) — Pitch: {targetRot.eulerAngles.x:0}°, Yaw: {targetRot.eulerAngles.y:0}°";
        }

        private void InvertSunDirection()
        {
            FindSceneLighting();
            if (_directionalLight == null) return;

            Vector3 curRot = _directionalLight.transform.rotation.eulerAngles;
            curRot.y = NormalizeAngle(curRot.y + 180f);
            curRot.x = NormalizeAngle(-curRot.x);

            _activeData.Lighting.SunEulerAngles = curRot;
            _activeData.Lighting.OverrideLighting = true;
            _directionalLight.transform.rotation = Quaternion.Euler(curRot);

            _statusMessage = "Faisceau solaire inversé à 180°.";
        }

        private static float NormalizeAngle(float a)
        {
            while (a > 180f) a -= 360f;
            while (a < -180f) a += 360f;
            return a;
        }
    }
}

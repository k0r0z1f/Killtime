using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.UI;
using Killtime.Story.Data;
using Killtime.Tactics;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.CameraSystem;
using Killtime.Story.Scenes;

namespace Killtime.Story
{
    public partial class CinematicEditorDevWindow
    {
        private void DrawCameraBlocks(SceneCinematicShotData shot)
        {
            GUILayout.Space(4);
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("<b>📷 Caméra START → END (voyage automatique pendant le plan) :</b>");
            try
            {
                var tcc = FindAnyObjectByType<TacticalCameraController>();
                if (tcc != null && !tcc.enabled)
                    GUILayout.Label("<color=yellow>🎥 Caméra tactique suspendue (« ▶ Voir ») — bouton « 🎥 Rendre la main caméra » pour naviguer à nouveau.</color>");
            }
            catch { /* ignore */ }
            DrawCameraStateRow("START", true, shot, ref shot.CamStartPos, ref shot.CamStartEuler, ref shot.CamStartFov);
            DrawCameraStateRow("END", false, shot, ref shot.CamEndPos, ref shot.CamEndEuler, ref shot.CamEndFov);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("START → END", GUILayout.Height(22)))
            {
                shot.CamEndPos = shot.CamStartPos;
                shot.CamEndEuler = shot.CamStartEuler;
                shot.CamEndFov = shot.CamStartFov;
                LogHud("📷 Caméra <b>START → END</b> copiée.");
            }
            if (GUILayout.Button("END → START", GUILayout.Height(22)))
            {
                shot.CamStartPos = shot.CamEndPos;
                shot.CamStartEuler = shot.CamEndEuler;
                shot.CamStartFov = shot.CamEndFov;
                LogHud("📷 Caméra <b>END → START</b> copiée.");
            }
            if (GUILayout.Button("⇄ Inverser", GUILayout.Width(90), GUILayout.Height(22)))
            {
                var p = shot.CamStartPos; var e = shot.CamStartEuler; var f = shot.CamStartFov;
                shot.CamStartPos = shot.CamEndPos; shot.CamStartEuler = shot.CamEndEuler; shot.CamStartFov = shot.CamEndFov;
                shot.CamEndPos = p; shot.CamEndEuler = e; shot.CamEndFov = f;
                LogHud("📷 Caméra <b>START ⇄ END</b> inversée.");
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("<color=grey>📋 = copier (presse-papier + texte), 📥 = coller. START → END = figer le plan (pas de voyage).</color>");
            GUILayout.EndVertical();
        }

        private void DrawCameraStateRow(string tag, bool isStart, SceneCinematicShotData shot, ref Vector3 pos, ref Vector3 euler, ref float fov)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>{tag}</b>", GUILayout.Width(42));
            GUI.backgroundColor = new Color(0.2f, 0.75f, 1f);
            string capBtnText = isStart ? "📸 Capturer [PgUp]" : "📸 Capturer [PgDn]";
            if (GUILayout.Button(capBtnText, GUILayout.Width(125))) CaptureShot(isStart);
            GUI.backgroundColor = new Color(0.25f, 0.85f, 0.45f);
            if (GUILayout.Button("▶ Voir", GUILayout.Width(52))) ApplyShot(isStart);
            GUI.backgroundColor = new Color(0.95f, 0.85f, 0.4f);
            if (GUILayout.Button(new GUIContent("📋", $"Copier {tag} (presse-papier + texte)"), GUILayout.Width(28))) CopyCameraToClipboard(pos, euler, fov, tag);
            if (GUILayout.Button(new GUIContent("📥", $"Coller vers {tag}"), GUILayout.Width(28))) PasteCameraFromClipboard(ref pos, ref euler, ref fov, tag);
            GUI.backgroundColor = Color.white;
            GUILayout.Label($"<color=grey>({pos.x:0.0}, {pos.y:0.0}, {pos.z:0.0}) ∠({euler.x:0}, {euler.y:0}, {euler.z:0}) fov {fov:0}</color>");
            GUILayout.EndHorizontal();

            string key = $"shot_{_shotIndex}_{(isStart ? "s" : "e")}";
            GUILayout.BeginHorizontal();
            GUILayout.Label("Pos :", GUILayout.Width(36));
            pos.x = NumField(key + "_px", pos.x, 56f);
            pos.y = NumField(key + "_py", pos.y, 56f);
            pos.z = NumField(key + "_pz", pos.z, 56f);
            GUILayout.Label("Rot :", GUILayout.Width(36));
            euler.x = NumField(key + "_rx", euler.x, 50f);
            euler.y = NumField(key + "_ry", euler.y, 50f);
            euler.z = NumField(key + "_rz", euler.z, 50f);
            GUILayout.Label("Fov :", GUILayout.Width(34));
            fov = NumField(key + "_fov", fov, 50f);
            fov = Mathf.Clamp(fov, 10f, 100f);
            GUILayout.EndHorizontal();
        }

        private void CopyCameraToClipboard(Vector3 pos, Vector3 euler, float fov, string tag)
        {
            _camClipPos = pos;
            _camClipEuler = euler;
            _camClipFov = fov;
            _hasCamClip = true;
            try
            {
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                GUIUtility.systemCopyBuffer =
                    $"pos=({pos.x.ToString("0.##", inv)}, {pos.y.ToString("0.##", inv)}, {pos.z.ToString("0.##", inv)}) " +
                    $"rot=({euler.x.ToString("0.##", inv)}, {euler.y.ToString("0.##", inv)}, {euler.z.ToString("0.##", inv)}) " +
                    $"fov={fov.ToString("0.##", inv)}";
            }
            catch { /* ignore */ }
            LogHud($"📋 Caméra <b>{tag}</b> copiée (collable sur START/END d'un autre plan avec 📥).");
        }

        private void PasteCameraFromClipboard(ref Vector3 pos, ref Vector3 euler, ref float fov, string tag)
        {
            // 1) Tente le texte système (permet copier depuis notes/Discord/autre plan).
            try
            {
                string sys = GUIUtility.systemCopyBuffer ?? "";
                if (TryParseCameraText(sys, out var p, out var e, out var fv))
                {
                    pos = p; euler = e; fov = Mathf.Clamp(fv, 10f, 100f);
                    _camClipPos = p; _camClipEuler = e; _camClipFov = fov; _hasCamClip = true;
                    LogHud($"📥 Caméra <b>{tag}</b> collée depuis le texte : <color=grey>{sys}</color>");
                    return;
                }
            }
            catch { /* ignore */ }
            // 2) Repli sur le presse-papier interne (même session).
            if (_hasCamClip)
            {
                pos = _camClipPos; euler = _camClipEuler; fov = Mathf.Clamp(_camClipFov, 10f, 100f);
                LogHud($"📥 Caméra <b>{tag}</b> collée (mémoire interne).");
            }
            else
            {
                LogHud($"<color=#FF8A8A>📥 Rien à coller vers <b>{tag}</b> : copiez d'abord avec 📋.</color>");
            }
        }

        private static bool TryParseCameraText(string s, out Vector3 pos, out Vector3 euler, out float fov)
        {
            pos = Vector3.zero; euler = Vector3.zero; fov = 60f;
            if (string.IsNullOrWhiteSpace(s)) return false;
            try
            {
                // Extrait les 7 premiers nombres : px py pz rx ry rz fov.
                var matches = System.Text.RegularExpressions.Regex.Matches(s, @"-?\d+(?:[.,]\d+)?");
                if (matches.Count < 7) return false;
                float[] v = new float[7];
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                for (int i = 0; i < 7; i++)
                {
                    string tok = matches[i].Value.Replace(',', '.');
                    if (!float.TryParse(tok, System.Globalization.NumberStyles.Float, inv, out v[i]))
                        return false;
                }
                pos = new Vector3(v[0], v[1], v[2]);
                euler = new Vector3(v[3], v[4], v[5]);
                fov = v[6];
                return true;
            }
            catch { return false; }
        }

    }
}

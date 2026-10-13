using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.UI;
using Killtime.Multi.Voice;

namespace Killtime.Multi
{
    public partial class VTTRoomWindow : FloatingWindow<VTTRoomWindow>
    {
        // Onglet Video : devices, mire locale, flux distants.

        private void DrawVideoSection()
        {
            if (_video == null)
            {
                Label("<i>Gestionnaire vidéo non initialisé.</i>");
                return;
            }

            Label("<b>Flux Caméra & Webcams de Table :</b>");

            // 1. Contrôles de la Caméra Locale
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            Label("<b>Périphérique :</b>", GUILayout.Width(95));

            var devs = _video.AvailableDevices;
            int devCount = devs.Count;
            if (devCount == 0)
            {
                Label("<color=red>Aucune caméra détectée</color>");
            }
            else
            {
                int curIdx = -1;
                for (int i = 0; i < devCount; i++)
                {
                    if (devs[i] == _video.ActiveDeviceName) { curIdx = i; break; }
                }
                if (curIdx < 0) curIdx = 0;

                if (GUILayout.Button("◀", GUILayout.Width(22), GUILayout.Height(20)))
                {
                    int prev = (curIdx - 1 + devCount) % devCount;
                    _video.SelectDevice(devs[prev]);
                }

                string devName = TruncateStr(_video.ActiveDeviceName, 20);
                GUI.color = _video.IsCameraActive ? Color.cyan : Color.yellow;
                Label($"<b>{devName}</b>", GUILayout.Width(140));
                GUI.color = Color.white;

                if (GUILayout.Button("▶", GUILayout.Width(22), GUILayout.Height(20)))
                {
                    int next = (curIdx + 1) % devCount;
                    _video.SelectDevice(devs[next]);
                }
            }

            if (GUILayout.Button("Scan", GUILayout.Width(50), GUILayout.Height(20)))
            {
                _video.RefreshDevices();
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            Color prevBg = GUI.backgroundColor;
            if (_video.IsCameraActive) GUI.backgroundColor = new Color(0.2f, 0.85f, 0.4f);
            if (GUILayout.Button(_video.IsCameraActive ? "■ Couper ma Caméra" : "▶ Activer ma Caméra", GUILayout.Height(24)))
            {
                _video.SetCameraActive(!_video.IsCameraActive);
            }
            GUI.backgroundColor = prevBg;

            if (GUILayout.Button("📹 Salon Vidéo (Ctrl+F2)", GUILayout.Width(155), GUILayout.Height(24)))
            {
                Killtime.Multi.Video.VTTVideoRoomWindow.Open();
            }

            GUILayout.Label($"Cadence: <b>{_video.TargetFps} FPS</b>", RichLabel(), GUILayout.Width(95));
            int newFps = (int)GUILayout.HorizontalSlider(_video.TargetFps, 5, 20, GUILayout.Width(60));
            if (newFps != _video.TargetFps) _video.TargetFps = newFps;
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            bool prevBlur = _video.BlurBackground;
            bool newBlur = GUILayout.Toggle(prevBlur, " Flou d'Arrière-Plan (Portrait)", RichToggle());
            if (newBlur != prevBlur)
            {
                _video.SetBlurBackground(newBlur);
            }
            if (_video.BlurBackground)
            {
                GUILayout.Label($"Intensité: <b>{_video.BlurRadius}px</b>", RichLabel(), GUILayout.Width(85));
                int nRad = (int)GUILayout.HorizontalSlider(_video.BlurRadius, 2, 12, GUILayout.Width(60));
                if (nRad != _video.BlurRadius) _video.BlurRadius = nRad;
            }
            else
            {
                GUILayout.Label("<color=gray><i>Arrière-plan naturel</i></color>", RichLabel());
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            Label("<b>Recadrage & Zoom (Cropping) :</b>", GUILayout.ExpandWidth(true));
            if (GUILayout.Button("Réinitialiser", GUILayout.Width(85), GUILayout.Height(18)))
            {
                _video.ResetCrop();
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            Label("Zoom :", GUILayout.Width(75));
            float nZoom = GUILayout.HorizontalSlider(_video.CropZoom, 1.0f, 2.5f);
            if (!Mathf.Approximately(nZoom, _video.CropZoom)) _video.CropZoom = nZoom;
            Label($"{_video.CropZoom:0.0}x", GUILayout.Width(40));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            Label("Axe X :", GUILayout.Width(75));
            float nOffX = GUILayout.HorizontalSlider(_video.CropOffsetX, -0.5f, 0.5f);
            if (!Mathf.Approximately(nOffX, _video.CropOffsetX)) _video.CropOffsetX = nOffX;
            string signX = _video.CropOffsetX >= 0 ? "+" : "";
            Label($"{signX}{Mathf.RoundToInt(_video.CropOffsetX * 100f)}%", GUILayout.Width(40));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            Label("Axe Y :", GUILayout.Width(75));
            float nOffY = GUILayout.HorizontalSlider(_video.CropOffsetY, -0.5f, 0.5f);
            if (!Mathf.Approximately(nOffY, _video.CropOffsetY)) _video.CropOffsetY = nOffY;
            string signY = _video.CropOffsetY >= 0 ? "+" : "";
            Label($"{signY}{Mathf.RoundToInt(_video.CropOffsetY * 100f)}%", GUILayout.Width(40));
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            // Mire locale (Agrandie à 320x240)
            if (_video.IsCameraActive && _video.LocalPreviewTexture != null)
            {
                GUILayout.Space(6);
                Rect previewRect = GUILayoutUtility.GetRect(320, 240, GUILayout.ExpandWidth(true));
                GUI.DrawTexture(previewRect, _video.LocalPreviewTexture, ScaleMode.ScaleToFit);
                string filterTag = _video.BlurBackground ? "Flou Bokeh Actif" : "Arrière-Plan Naturel";
                Label($"<color=gray><i>Mire locale (320x240 affichage / 240x180 flux — {filterTag})</i></color>");
            }
            GUILayout.EndVertical();

            GUILayout.Space(6);

            // 2. Grille des Caméras des Autres Joueurs (Agrandies à 320x240)
            Label("<b>Flux Distants des Joueurs :</b>");
            GUILayout.BeginVertical(GUI.skin.box);

            if (_room == null || !_room.InRoom || _room.Members.Count <= 1)
            {
                Label("<color=gray><i>Aucun autre participant connecté dans cette room.</i></color>");
            }
            else
            {
                foreach (var m in _room.Members)
                {
                    if (m == null || m.clientId == _room.ClientId) continue;

                    var player = _video.GetOrCreateRemotePlayer(m.clientId, m.username);
                    bool hasActiveStream = player.IsStreamActive && player.VideoTexture != null;

                    GUILayout.BeginVertical(GUI.skin.box);
                    GUILayout.BeginHorizontal();
                    string statusIcon = hasActiveStream ? "<color=#00FF88>● EN DIRECT</color>" : "<color=gray>○ CAM ÉTEINTE</color>";
                    Label($"<b>{m.username}</b> [{m.role}] {statusIcon}", GUILayout.ExpandWidth(true));
                    GUILayout.EndHorizontal();

                    if (hasActiveStream)
                    {
                        Rect feedRect = GUILayoutUtility.GetRect(320, 240, GUILayout.ExpandWidth(true));
                        GUI.DrawTexture(feedRect, player.VideoTexture, ScaleMode.ScaleToFit);
                    }
                    else
                    {
                        GUILayout.Label("<color=gray><i>En attente du flux vidéo du joueur...</i></color>");
                    }
                    GUILayout.EndVertical();
                    GUILayout.Space(4);
                }
            }
            GUILayout.EndVertical();
        }
    }
}

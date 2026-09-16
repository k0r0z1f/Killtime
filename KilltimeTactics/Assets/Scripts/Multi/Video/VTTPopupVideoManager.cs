using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.UI;
using Killtime.Multi.Voice;

namespace Killtime.Multi.Video
{
    /// <summary>
    /// Gestionnaire des fenêtres pop-up flottantes individuelles de flux vidéo VTT.
    /// Permet à chaque joueur ou GM d'extraire la caméra ou la fiche d'un participant
    /// sous forme de fenêtre flottante indépendante et redimensionnable (FloatingWindowChrome),
    /// entourée en direct d'un cadre vert réactif (#00FF88) lorsque la personne parle.
    /// </summary>
    [DisallowMultipleComponent]
    public class VTTPopupVideoManager : MonoBehaviour
    {
        public static VTTPopupVideoManager Instance { get; private set; }

        public class VideoFeedPopup
        {
            public string ClientId;
            public string Username;
            public bool IsLocal;
            public int WindowId;
            public Rect WindowRect;
            public bool IsOpen;
            public bool IsMinimized;
            public Vector2 SavedSize;
        }

        private readonly Dictionary<string, VideoFeedPopup> _popups = new();
        private static Texture2D _whiteTex;
        private static GUIStyle _initialsStyle;
        private static GUIStyle _badgeStyle;

        private static readonly Color SpeakingGreen = new Color(0.0f, 1.0f, 0.53f, 0.95f);
        private static readonly Color SilentBorder = new Color(0.12f, 0.22f, 0.32f, 0.65f);
        private static readonly Color BackgroundColor = new Color(0.015f, 0.025f, 0.040f, 0.96f);
        private static readonly Vector2 MinPopupSize = new Vector2(180f, 160f);

        private static Texture2D WhiteTex
        {
            get
            {
                if (_whiteTex == null)
                {
                    _whiteTex = new Texture2D(1, 1);
                    _whiteTex.SetPixel(0, 0, Color.white);
                    _whiteTex.Apply();
                }
                return _whiteTex;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        public static void EnsureExists()
        {
            if (Instance == null && FindAnyObjectByType<VTTPopupVideoManager>() == null)
            {
                var go = new GameObject("[VTT] VideoPopupManager");
                Instance = go.AddComponent<VTTPopupVideoManager>();
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        private void OnDestroy()
        {
            // Désenregistrement propre de toutes les fenêtres pop-up auprès du chrome
            foreach (var p in _popups.Values)
            {
                FloatingWindowChrome.UnregisterWindow(p.WindowId);
            }
            _popups.Clear();
            if (Instance == this) Instance = null;
        }

        public bool IsPoppedOut(string clientId)
        {
            return _popups.TryGetValue(clientId, out var p) && p.IsOpen;
        }

        public void OpenPopup(string clientId, string username, bool isLocal = false)
        {
            if (!_popups.TryGetValue(clientId, out var popup))
            {
                int windowId = 2100 + Mathf.Abs(clientId.GetHashCode() % 800);

                // Calcul d'une position en cascade sans recouvrir le HUD
                float w = 240f;
                float h = 210f;
                float cascadeOffset = (_popups.Count * 26f) % 200f;
                Rect initialRect = new Rect(Screen.width - w - 24f - cascadeOffset, 120f + cascadeOffset, w, h);

                popup = new VideoFeedPopup
                {
                    ClientId = clientId,
                    Username = username,
                    IsLocal = isLocal,
                    WindowId = windowId,
                    WindowRect = initialRect,
                    IsOpen = true,
                    IsMinimized = false,
                    SavedSize = Vector2.zero
                };

                FloatingWindowChrome.RegisterWindow(windowId, () => popup.WindowRect, () => popup.IsOpen);
                _popups[clientId] = popup;
            }

            popup.Username = username;
            popup.IsLocal = isLocal;
            popup.IsOpen = true;
            popup.IsMinimized = false;
            FloatingWindowChrome.FocusWindow(popup.WindowId);
        }

        public void ClosePopup(string clientId)
        {
            if (_popups.TryGetValue(clientId, out var popup))
            {
                popup.IsOpen = false;
                FloatingWindowChrome.UnregisterWindow(popup.WindowId);
                _popups.Remove(clientId);
            }
        }

        public void PopOutAll(List<VTTVideoRoomWindow.ParticipantInfo> participants)
        {
            if (participants == null) return;
            for (int i = 0; i < participants.Count; i++)
            {
                var p = participants[i];
                OpenPopup(p.ClientId, p.Username, p.IsLocal);
            }
        }

        public void ReintegrateAll()
        {
            var keys = new List<string>(_popups.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                ClosePopup(keys[i]);
            }
        }

        private void OnGUI()
        {
            if (_popups.Count == 0) return;

            EnsureStyles();
            var toClose = new List<string>();

            foreach (var kvp in _popups)
            {
                var popup = kvp.Value;
                if (!popup.IsOpen)
                {
                    toClose.Add(kvp.Key);
                    continue;
                }

                // 1. Gestion des événements de redimensionnement et z-order
                FloatingWindowChrome.HandleResizeEvents(ref popup.WindowRect, popup.WindowId, MinPopupSize, popup.IsMinimized);
                FloatingWindowChrome.ApplyDockedRect(ref popup.WindowRect, popup.WindowId);

                // 2. Fenêtre IMGUI
                popup.WindowRect = GUI.Window(popup.WindowId, popup.WindowRect, (id) => DrawPopupWindowContent(popup), "", GUIStyle.none);

                // 3. Maintien dans l'écran
                FloatingWindowChrome.ClampToScreen(ref popup.WindowRect);
            }

            for (int i = 0; i < toClose.Count; i++)
            {
                ClosePopup(toClose[i]);
            }
        }

        private void DrawPopupWindowContent(VideoFeedPopup p)
        {
            // 1. Barre de titre avec drag, minimiser et fermer (fermer = réintégrer)
            string title = p.IsLocal ? $"📹 {p.Username} (Moi)" : $"📹 {p.Username}";
            FloatingWindowChrome.DrawTitleBar(ref p.WindowRect, ref p.IsOpen, ref p.IsMinimized,
                ref p.SavedSize, p.WindowId, () => ClosePopup(p.ClientId), title);

            if (p.IsMinimized) return;

            // 2. Détection de parole en temps réel
            var voiceMgr = VTTVoiceManager.Instance;
            bool isSpeaking = p.IsLocal
                ? (voiceMgr != null && voiceMgr.IsLocalSpeaking)
                : (voiceMgr != null && voiceMgr.RemotePlayers.TryGetValue(p.ClientId, out var rp) && rp.IsSpeaking);

            // 3. Résolution du flux vidéo
            var videoMgr = VTTVideoManager.Instance;
            Texture videoTexture = null;
            bool hasVideo = false;

            if (p.IsLocal)
            {
                hasVideo = videoMgr != null && videoMgr.IsCameraActive && videoMgr.LocalPreviewTexture != null;
                videoTexture = hasVideo ? videoMgr.LocalPreviewTexture : null;
            }
            else if (videoMgr != null && videoMgr.RemotePlayers.TryGetValue(p.ClientId, out var remotePlayer))
            {
                hasVideo = remotePlayer.IsStreamActive && remotePlayer.VideoTexture != null;
                videoTexture = hasVideo ? remotePlayer.VideoTexture : null;
            }

            // 4. Zone d'affichage vidéo
            float topOffset = FloatingWindowChrome.TitleHeight + 2f;
            float footerHeight = 24f;
            Rect viewRect = new Rect(4f, topOffset, p.WindowRect.width - 8f, p.WindowRect.height - topOffset - footerHeight - 6f);

            // Fond
            DrawSolidRect(viewRect, BackgroundColor);

            if (hasVideo && videoTexture != null)
            {
                GUI.DrawTexture(viewRect, videoTexture, ScaleMode.ScaleToFit);
            }
            else
            {
                // Carte avatar stylisée si pas de caméra
                DrawPopupNoCameraPlaceholder(viewRect, p.Username, isSpeaking);
            }

            // Cadre vert néon de parole (#00FF88, 3px)
            if (isSpeaking)
            {
                DrawBorder(viewRect, SpeakingGreen, 3f);
            }
            else
            {
                DrawBorder(viewRect, SilentBorder, 1f);
            }

            // 5. Pied de fenêtre (Indicateur & Bouton réintégrer)
            Rect footerRect = new Rect(6f, p.WindowRect.height - footerHeight - 3f, p.WindowRect.width - 12f, footerHeight);

            if (isSpeaking)
            {
                GUI.Label(new Rect(footerRect.x, footerRect.y + 2f, footerRect.width - 82f, 18f),
                    "<color=#00FF88><b>● EN PAROLE</b></color>", _badgeStyle);
            }
            else
            {
                GUI.Label(new Rect(footerRect.x, footerRect.y + 2f, footerRect.width - 82f, 18f),
                    "<color=gray>○ Silencieux</color>", _badgeStyle);
            }

            if (GUI.Button(new Rect(footerRect.xMax - 78f, footerRect.y, 76f, 20f), "📥 Réintégrer"))
            {
                ClosePopup(p.ClientId);
            }

            // 6. Poignée de redimensionnement officielle
            FloatingWindowChrome.DrawResizeHandle(ref p.WindowRect, p.WindowId, MinPopupSize, p.IsMinimized);
        }

        private void DrawPopupNoCameraPlaceholder(Rect area, string username, bool isSpeaking)
        {
            string initials = (username != null && username.Length >= 2)
                ? username.Substring(0, 2).ToUpperInvariant()
                : "??";

            Color c = isSpeaking ? SpeakingGreen : new Color(0.4f, 0.6f, 0.8f);
            _initialsStyle.normal.textColor = c;

            Rect initialsRect = new Rect(area.x, area.y + (area.height - 36f) * 0.45f, area.width, 36f);
            GUI.Label(initialsRect, initials, _initialsStyle);

            Rect camOffRect = new Rect(area.x, area.yMax - 20f, area.width, 16f);
            GUI.Label(camOffRect, "<color=gray>[Caméra Coupée]</color>", _badgeStyle);
        }

        private static void DrawBorder(Rect rect, Color color, float thickness)
        {
            Color prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, thickness), WhiteTex);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), WhiteTex);
            GUI.DrawTexture(new Rect(rect.x, rect.y, thickness, rect.height), WhiteTex);
            GUI.DrawTexture(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), WhiteTex);
            GUI.color = prev;
        }

        private static void DrawSolidRect(Rect rect, Color color)
        {
            Color prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, WhiteTex);
            GUI.color = prev;
        }

        private static void EnsureStyles()
        {
            if (_badgeStyle == null)
            {
                _badgeStyle = new GUIStyle(GUI.skin.label)
                {
                    richText = true,
                    fontSize = 9,
                    alignment = TextAnchor.MiddleLeft
                };
            }

            if (_initialsStyle == null)
            {
                _initialsStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 22,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    richText = true
                };
            }
        }
    }
}
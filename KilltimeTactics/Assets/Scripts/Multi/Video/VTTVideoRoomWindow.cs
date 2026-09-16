using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.UI;
using Killtime.Multi.Voice;

namespace Killtime.Multi.Video
{
    /// <summary>
    /// Salon Vidéo & Galerie Visio VTT (Touche F6).
    /// Affiche l'ensemble des caméras actives et des participants sans caméra connectés à la table.
    /// Entoure d'un cadre vert néon dynamique (#00FF88) tout participant qui parle.
    /// Permet d'extraire chaque flux en fenêtre flottante indépendante (pop-up) via le bouton "Détacher".
    /// </summary>
    public class VTTVideoRoomWindow : FloatingWindow<VTTVideoRoomWindow>
    {
        protected override int WindowId => 898;
        protected override string Title => "Salon Vidéo // Table Virtuelle";
        protected override Vector2 MinSize => new Vector2(480f, 340f);
        protected override Rect DefaultRect => new Rect(
            Mathf.Max(20f, (Screen.width - 820f) * 0.5f),
            Mathf.Max(30f, (Screen.height - 620f) * 0.5f),
            Mathf.Min(820f, Screen.width - 40f),
            Mathf.Min(620f, Screen.height - 60f));
        protected override KeyCode[] ToggleKeys => _toggleKeys;

        private static readonly KeyCode[] _toggleKeys = { KeyCode.F6 };

        private Vector2 _galleryScroll;
        private static GUIStyle _richLabel;
        private static GUIStyle _badgeStyle;
        private static GUIStyle _initialsStyle;
        private static Texture2D _whiteTex;

        private static readonly Color SpeakingGreen = new Color(0.0f, 1.0f, 0.53f, 0.95f);
        private static readonly Color SilentBorder = new Color(0.12f, 0.22f, 0.32f, 0.65f);
        private static readonly Color CardBackground = new Color(0.015f, 0.028f, 0.045f, 0.94f);
        private static readonly Color DetachedBackground = new Color(0.010f, 0.018f, 0.030f, 0.88f);

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

        protected override void OnOpened()
        {
            VTTPopupVideoManager.EnsureExists();
        }

        protected override void DrawContent()
        {
            EnsureStyles();
            VTTPopupVideoManager.EnsureExists();

            var room = VTTRoomManager.Instance;
            var videoMgr = VTTVideoManager.Instance;
            var voiceMgr = VTTVoiceManager.Instance;

            // 1. Barre d'actions & commandes globales du salon
            DrawGlobalControlsBar(room, videoMgr, voiceMgr);
            GUILayout.Space(6);

            // 2. Collecte des participants
            var participants = GatherParticipants(room);

            // 3. Galerie mosaïque responsive
            _galleryScroll = GUILayout.BeginScrollView(_galleryScroll);

            int totalCards = participants.Count;
            if (totalCards == 0)
            {
                GUILayout.Label("<color=gray><i>Aucun participant détecté.</i></color>", RichLabel());
            }
            else
            {
                int cols = Mathf.Clamp(Mathf.FloorToInt((_windowRect.width - 40f) / 250f), 1, 4);
                float cardW = ((_windowRect.width - 40f) - (cols - 1) * 8f) / cols;
                float cardH = Mathf.Clamp(cardW * 0.75f + 48f, 180f, 290f);

                for (int i = 0; i < totalCards; i += cols)
                {
                    GUILayout.BeginHorizontal();
                    for (int c = 0; c < cols; c++)
                    {
                        int index = i + c;
                        if (index < totalCards)
                        {
                            var p = participants[index];
                            DrawParticipantCard(p, cardW, cardH, videoMgr, voiceMgr);
                        }
                        else
                        {
                            GUILayout.Space(cardW);
                        }
                    }
                    GUILayout.EndHorizontal();
                    GUILayout.Space(6);
                }
            }

            GUILayout.EndScrollView();
        }

        private void DrawGlobalControlsBar(VTTRoomManager room, VTTVideoManager video, VTTVoiceManager voice)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();

            // Statut de salle
            string roomCode = (room != null && room.InRoom) ? room.RoomCode : "Hors-Ligne (Démo Locale)";
            string roleTag = (room != null && room.InRoom && room.IsGM) ? "<color=#FF9900>👑 GM</color>" : "<color=#00E5FF>🎲 Joueur</color>";
            GUILayout.Label($"<b>Table :</b> {roomCode}  [{roleTag}]", RichLabel(), GUILayout.Width(240));

            // Contrôles locaux directs (Caméra & Micro)
            if (video != null)
            {
                Color prevCol = GUI.backgroundColor;
                if (video.IsCameraActive) GUI.backgroundColor = new Color(0.2f, 0.85f, 0.4f);
                if (GUILayout.Button(video.IsCameraActive ? "📹 Caméra Active" : "📷 Caméra Coupée", GUILayout.Width(130), GUILayout.Height(22)))
                {
                    video.SetCameraActive(!video.IsCameraActive);
                }
                GUI.backgroundColor = prevCol;
            }

            if (voice != null)
            {
                Color prevCol = GUI.backgroundColor;
                if (voice.IsMuted) GUI.backgroundColor = new Color(1f, 0.35f, 0.35f);
                if (GUILayout.Button(voice.IsMuted ? "🔇 Micro Coupé" : "🎙️ Micro Actif", GUILayout.Width(115), GUILayout.Height(22)))
                {
                    voice.SetMuted(!voice.IsMuted);
                }
                GUI.backgroundColor = prevCol;
            }

            GUILayout.FlexibleSpace();

            // Commandes de pop-out en masse
            var popupMgr = VTTPopupVideoManager.Instance;
            if (popupMgr != null)
            {
                if (GUILayout.Button("⧉ Détacher Tous", GUILayout.Width(110), GUILayout.Height(22)))
                {
                    popupMgr.PopOutAll(participants: GatherParticipants(room));
                }

                if (GUILayout.Button("📥 Réintégrer Tous", GUILayout.Width(120), GUILayout.Height(22)))
                {
                    popupMgr.ReintegrateAll();
                }
            }

            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private void DrawParticipantCard(ParticipantInfo p, float width, float height, VTTVideoManager video, VTTVoiceManager voice)
        {
            Rect cardRect = GUILayoutUtility.GetRect(width, height, GUILayout.Width(width), GUILayout.Height(height));

            // Détection de l'état de parole
            bool isSpeaking = p.IsLocal
                ? (voice != null && voice.IsLocalSpeaking)
                : (voice != null && voice.RemotePlayers.TryGetValue(p.ClientId, out var rp) && rp.IsSpeaking);

            // Vérification si ce flux a été détaché sous forme de fenêtre flottante
            bool isPoppedOut = VTTPopupVideoManager.Instance != null && VTTPopupVideoManager.Instance.IsPoppedOut(p.ClientId);

            // Fond
            DrawSolidRect(cardRect, isPoppedOut ? DetachedBackground : CardBackground);

            // Cadre réactif : Vert Fluo Vibrant (#00FF88, 3px) si l'utilisateur parle, bordure subtile sinon
            if (isSpeaking)
            {
                DrawBorder(cardRect, SpeakingGreen, 3f);
            }
            else
            {
                DrawBorder(cardRect, SilentBorder, 1f);
            }

            // En-tête de la tuile (Pseudo, Rôle, Bouton Détacher)
            Rect headerRect = new Rect(cardRect.x + 8f, cardRect.y + 6f, cardRect.width - 16f, 22f);
            string roleBadge = p.IsGM ? "<color=#FF9900>👑 GM</color>" : "<color=#88AACC>🎲 Joueur</color>";
            string localTag = p.IsLocal ? " <color=#00E5FF>(Moi)</color>" : "";
            GUI.Label(new Rect(headerRect.x, headerRect.y, headerRect.width - 70f, 20f), $"<b>{p.Username}</b> [{roleBadge}]{localTag}", RichLabel());

            // Bouton Pop-out / Détacher
            Rect detachBtnRect = new Rect(cardRect.xMax - 74f, cardRect.y + 5f, 66f, 20f);
            if (isPoppedOut)
            {
                GUI.color = new Color(0.2f, 0.8f, 1f);
                if (GUI.Button(detachBtnRect, "📥 Intégrer"))
                {
                    VTTPopupVideoManager.Instance?.ClosePopup(p.ClientId);
                }
                GUI.color = Color.white;
            }
            else
            {
                if (GUI.Button(detachBtnRect, "⧉ Détacher"))
                {
                    VTTPopupVideoManager.Instance?.OpenPopup(p.ClientId, p.Username, p.IsLocal);
                }
            }

            // Zone d'affichage vidéo ou carte de profil sans caméra
            Rect contentArea = new Rect(cardRect.x + 6f, cardRect.y + 30f, cardRect.width - 12f, cardRect.height - 62f);

            if (isPoppedOut)
            {
                // Message discret si le flux est détaché sur l'écran
                GUI.Label(new Rect(contentArea.x, contentArea.y + (contentArea.height - 40f) * 0.5f, contentArea.width, 36f),
                    "<color=#00E5FF><b>⧉ Flux vidéo détaché</b></color>\n<color=gray><i>Fenêtre flottante active sur l'écran</i></color>", _initialsStyle);
            }
            else
            {
                // Résolution de la texture vidéo
                Texture videoTexture = null;
                bool hasVideo = false;

                if (p.IsLocal)
                {
                    hasVideo = video != null && video.IsCameraActive && video.LocalPreviewTexture != null;
                    videoTexture = hasVideo ? video.LocalPreviewTexture : null;
                }
                else
                {
                    if (video != null && video.RemotePlayers.TryGetValue(p.ClientId, out var remotePlayer))
                    {
                        hasVideo = remotePlayer.IsStreamActive && remotePlayer.VideoTexture != null;
                        videoTexture = hasVideo ? remotePlayer.VideoTexture : null;
                    }
                }

                if (hasVideo && videoTexture != null)
                {
                    // Rendu vidéo avec correction de ratio
                    GUI.DrawTexture(contentArea, videoTexture, ScaleMode.ScaleToFit);
                }
                else
                {
                    // Utilisateur sans caméra : Profil stylisé avec initiales géantes
                    DrawNoCameraPlaceholder(contentArea, p.Username, isSpeaking);
                }
            }

            // Barre de pied de tuile (Indicateur de parole & Statut micro)
            Rect footerRect = new Rect(cardRect.x + 8f, cardRect.yMax - 26f, cardRect.width - 16f, 20f);
            if (isSpeaking)
            {
                GUI.Label(footerRect, "<color=#00FF88><b>● EN PAROLE</b></color>", _badgeStyle);
            }
            else
            {
                GUI.Label(footerRect, "<color=gray>○ Silencieux</color>", _badgeStyle);
            }
        }

        private void DrawNoCameraPlaceholder(Rect area, string username, bool isSpeaking)
        {
            // Fond sombre bleuté
            DrawSolidRect(area, new Color(0.025f, 0.045f, 0.070f, 0.90f));

            // Initiales (ex: "Alex" -> "AL", "Roger" -> "RO")
            string initials = GetInitials(username);

            Color initialsColor = isSpeaking ? SpeakingGreen : new Color(0.40f, 0.60f, 0.80f, 0.80f);
            _initialsStyle.normal.textColor = initialsColor;

            Rect initialsRect = new Rect(area.x, area.y + (area.height - 50f) * 0.45f, area.width, 42f);
            GUI.Label(initialsRect, initials, _initialsStyle);

            Rect camOffRect = new Rect(area.x, area.yMax - 22f, area.width, 16f);
            GUI.Label(camOffRect, "<color=gray>📷 [Caméra Inactive]</color>", _badgeStyle);
        }

        private static string GetInitials(string name)
        {
            if (string.IsNullOrEmpty(name)) return "??";
            name = name.Trim();
            if (name.Length <= 2) return name.ToUpperInvariant();

            string[] parts = name.Split(new[] { ' ', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                return $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[1][0])}";
            }
            return name.Substring(0, 2).ToUpperInvariant();
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

        public class ParticipantInfo
        {
            public string ClientId;
            public string Username;
            public bool IsLocal;
            public bool IsGM;
        }

        public static List<ParticipantInfo> GatherParticipants(VTTRoomManager room)
        {
            var list = new List<ParticipantInfo>();

            // 1. Participant local (Soi-même)
            string localName = room != null ? room.name : "Joueur";
            if (VTTRoomWindow.Instance != null)
            {
                // Extraction sécurisée du pseudo configuré
                localName = PlayerPrefs.GetString("KT_DevUIPrefs_JSON", "");
                if (string.IsNullOrEmpty(localName)) localName = "Joueur Local";
                else localName = "Joueur";
            }

            list.Add(new ParticipantInfo
            {
                ClientId = (room != null && !string.IsNullOrEmpty(room.ClientId)) ? room.ClientId : "local_me",
                Username = (room != null && room.InRoom) ? (room.FindMember(room.ClientId)?.username ?? "Moi") : "Moi",
                IsLocal = true,
                IsGM = (room != null && room.IsGM)
            });

            // 2. Participants distants dans la room
            if (room != null && room.InRoom)
            {
                foreach (var m in room.Members)
                {
                    if (m == null || m.clientId == room.ClientId) continue;
                    list.Add(new ParticipantInfo
                    {
                        ClientId = m.clientId,
                        Username = m.username,
                        IsLocal = false,
                        IsGM = m.IsGM
                    });
                }
            }

            return list;
        }

        private static GUIStyle RichLabel()
        {
            EnsureStyles();
            return _richLabel;
        }

        private static void EnsureStyles()
        {
            if (_richLabel == null)
            {
                _richLabel = new GUIStyle(GUI.skin.label)
                {
                    richText = true,
                    fontSize = 11,
                    wordWrap = false
                };
            }

            if (_badgeStyle == null)
            {
                _badgeStyle = new GUIStyle(GUI.skin.label)
                {
                    richText = true,
                    fontSize = 10,
                    alignment = TextAnchor.MiddleLeft
                };
            }

            if (_initialsStyle == null)
            {
                _initialsStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 24,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    richText = true
                };
            }
        }
    }
}
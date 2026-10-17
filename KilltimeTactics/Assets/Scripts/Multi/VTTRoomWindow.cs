using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.UI;
using Killtime.Multi.Voice;

namespace Killtime.Multi
{
    /// <summary>
    /// Fenêtre dev minimale pour la Room multijoueur (Table Virtuelle).
    /// Pattern IMGUI identique aux autres fenêtres dev (F1/F2/F3) : touche F4,
    /// singleton auto-créé via Open(), pile réseau auto-instanciée si absente.
    /// Affiche : connexion, création/join de room, présence + contrôles GM,
    /// chat de table relayé par le hub.
    /// </summary>
    public partial class VTTRoomWindow : FloatingWindow<VTTRoomWindow>
    {
        protected override int WindowId => 886;
        protected override string Title => "Room Multijoueur (Ctrl+F1)";
        protected override Vector2 MinSize => _minSize;
        protected override Rect DefaultRect => new Rect(600, 96, 420, 560);
        protected override KeyCode[] ToggleKeys => _toggleKeys;
        protected override bool RequireControl => true;

        private static readonly KeyCode[] _toggleKeys = { KeyCode.F1 };
        private static readonly Vector2 _minSize = new Vector2(320, 200);
        private Vector2 _scrollPos;
        private Vector2 _chatScroll;
        private bool _chatScrollLock = false;
        private bool _chatScrollToBottomPending = false;
        private string _statusMessage = "Prêt. Connectez-vous puis créez ou rejoignez une room.";

        private string _serverUrl = VTTProtocol.DefaultHubUrl;
        private string _username = "Joueur";
        private string _joinCode = "";
        private string _chatInput = "";
        private readonly List<string> _chatLog = new();

        // Création de table (annuaire public par défaut).
        private string _tableName = "";
        private string _maxPlayersStr = "6";
        private bool _isPublic = true;

        // Annuaire public des tables (server browser).
        private readonly List<VTTPublicRoom> _publicRooms = new();
        private Vector2 _dirScroll;
        private bool _dirBusy;
        private string _dirError;
        private float _nextDirRefresh;
        private const float DirRefreshInterval = 15f;

        private VTTRoomManager _room;
        private VTTNetworkClient _net;
        private VTTTableSync _sync;
        private VTTRoomDirectory _directory;
        private Killtime.Multi.Voice.VTTVoiceManager _voice;
        private Killtime.Multi.Voice.VTTVoiceVisualizer _voiceVisualizer;
        private Killtime.Multi.Video.VTTVideoManager _video;
        private int _currentTab = 0; // 0 = Table, 1 = Annuaire, 2 = Chat, 3 = Voix, 4 = Vidéo
        private bool _subscribed;
        private GUIStyle _richLabel;
        private GUIStyle _richToggle;

        /// <summary>Style label avec rich text (le skin IMGUI a richText=false par défaut).</summary>
        private GUIStyle RichLabel()
        {
            if (_richLabel == null)
            {
                _richLabel = new GUIStyle(GUI.skin.label) { richText = true, wordWrap = false };
            }
            return _richLabel;
        }

        /// <summary>Style toggle avec case à cocher native IMGUI et rich text activé.</summary>
        private GUIStyle RichToggle()
        {
            if (_richToggle == null)
            {
                _richToggle = new GUIStyle(GUI.skin.toggle)
                {
                    richText = true,
                    fontSize = 11,
                    alignment = TextAnchor.MiddleLeft
                };
            }
            return _richToggle;
        }

        /// <summary>Équivalent GUILayout.Label avec &lt;b&gt;/&lt;i&gt;/&lt;color&gt; interprétés.</summary>
        private void Label(string text, params GUILayoutOption[] options)
        {
            GUILayout.Label(text, RichLabel(), options);
        }

        protected override void OnOpened()
        {
            EnsureMultiStack();
            _nextDirRefresh = 0f; // force un refresh d'annuaire à l'ouverture
            try { LoadDevPrefs(); } catch { /* ignore */ }
        }

        protected override void Awake()
        {
            base.Awake();
            try { LoadDevPrefs(); } catch { /* ignore */ }
        }

        protected override void OnClosed()
        {
            try { CaptureDevPrefs(); Killtime.UI.DevUIPreferences.SaveNow(); } catch { /* ignore */ }
        }

        private void LoadDevPrefs()
        {
            var p = Killtime.UI.DevUIPreferences.Current;
            if (p == null) return;
            if (!string.IsNullOrEmpty(p.VttServerUrl)) _serverUrl = p.VttServerUrl;
            if (!string.IsNullOrEmpty(p.VttUsername)) _username = p.VttUsername;
            if (p.VttJoinCode != null) _joinCode = p.VttJoinCode;
            if (p.VttTableName != null) _tableName = p.VttTableName;
            if (!string.IsNullOrEmpty(p.VttMaxPlayers)) _maxPlayersStr = p.VttMaxPlayers;
            _isPublic = p.VttIsPublic;
        }

        private void CaptureDevPrefs()
        {
            var p = Killtime.UI.DevUIPreferences.Current;
            if (p == null) return;
            p.VttServerUrl = _serverUrl ?? "";
            p.VttUsername = _username ?? "Joueur";
            p.VttJoinCode = _joinCode ?? "";
            p.VttTableName = _tableName ?? "";
            p.VttMaxPlayers = _maxPlayersStr ?? "6";
            p.VttIsPublic = _isPublic;
            Killtime.UI.DevUIPreferences.MarkDirty();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            EnsureMultiStack();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            try { CaptureDevPrefs(); } catch { /* ignore */ }
            Unsubscribe();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            Unsubscribe();
            _voiceVisualizer?.Destroy();
        }

        protected override void Update()
        {
            base.Update();
            // Rafraîchit l'annuaire en continu tant que la fenêtre est ouverte
            // et qu'on n'est pas en room (pas de spam une fois attablé).
            if (_isOpen && !_dirBusy && _room != null && !_room.InRoom
                && Time.realtimeSinceStartup >= _nextDirRefresh)
            {
                RefreshDirectory();
            }
        }

        /// <summary>
        /// Garantit la présence de la pile réseau (client + room + sync table).
        /// </summary>
        public void EnsureMultiStack()
        {
            if (_room == null) _room = VTTRoomManager.Instance ?? FindAnyObjectByType<VTTRoomManager>();
            if (_room == null)
            {
                var go = new GameObject("[VTT] Network");
                _room = go.AddComponent<VTTRoomManager>(); // RequireComponent ajoute le NetworkClient
            }
            if (_net == null)
            {
                _net = _room.Net ?? _room.GetComponent<VTTNetworkClient>();
            }
            if (_net != null && _serverUrl == VTTProtocol.DefaultHubUrl
                && _net.ServerUrl != _serverUrl)
            {
                // Le client a une URL configurée (ex: inspecteur en LAN) et le
                // champ est encore au défaut : on s'aligne dessus, une seule fois.
                // Dès que l'utilisateur tape sa propre URL, on n'y touche plus.
                _serverUrl = _net.ServerUrl;
            }
            if (_sync == null)
            {
                _sync = FindAnyObjectByType<VTTTableSync>();
                if (_sync == null)
                {
                    var go = new GameObject("[VTT] TableSync");
                    _sync = go.AddComponent<VTTTableSync>();
                }
            }
            if (_directory == null)
            {
                _directory = FindAnyObjectByType<VTTRoomDirectory>();
                if (_directory == null)
                {
                    var go = new GameObject("[VTT] Directory");
                    _directory = go.AddComponent<VTTRoomDirectory>();
                }
            }
            if (_voice == null)
            {
                _voice = Killtime.Multi.Voice.VTTVoiceManager.Instance ?? FindAnyObjectByType<Killtime.Multi.Voice.VTTVoiceManager>();
                if (_voice == null)
                {
                    var go = new GameObject("[VTT] Voice");
                    _voice = go.AddComponent<Killtime.Multi.Voice.VTTVoiceManager>();
                }
            }
            if (_voiceVisualizer == null)
            {
                _voiceVisualizer = new Killtime.Multi.Voice.VTTVoiceVisualizer();
            }
            if (_video == null)
            {
                _video = Killtime.Multi.Video.VTTVideoManager.Instance ?? FindAnyObjectByType<Killtime.Multi.Video.VTTVideoManager>();
                if (_video == null)
                {
                    var go = new GameObject("[VTT] Video");
                    _video = go.AddComponent<Killtime.Multi.Video.VTTVideoManager>();
                }
            }
            Subscribe();
        }

        private void Subscribe()
        {
            if (_subscribed) return;
            if (_room != null)
            {
                _room.OnJoined += HandleJoined;
                _room.OnLeft += HandleLeft;
                _room.OnPresenceChanged += HandlePresence;
                _room.OnRoleChanged += HandleRoleChanged;
                _room.OnVisibilityChanged += HandleVisibilityChanged;
                _room.OnKicked += HandleKicked;
                _room.OnError += HandleRoomError;
            }
            if (_net != null)
            {
                _net.OnConnected += HandleConnected;
                _net.OnDisconnected += HandleDisconnected;
            }
            if (_sync != null)
            {
                _sync.OnChatReceived += HandleChatReceived;
            }
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            if (_room != null)
            {
                _room.OnJoined -= HandleJoined;
                _room.OnLeft -= HandleLeft;
                _room.OnPresenceChanged -= HandlePresence;
                _room.OnRoleChanged -= HandleRoleChanged;
                _room.OnVisibilityChanged -= HandleVisibilityChanged;
                _room.OnKicked -= HandleKicked;
                _room.OnError -= HandleRoomError;
            }
            if (_net != null)
            {
                _net.OnConnected -= HandleConnected;
                _net.OnDisconnected -= HandleDisconnected;
            }
            if (_sync != null)
            {
                _sync.OnChatReceived -= HandleChatReceived;
            }
            _subscribed = false;
        }

        protected override void DrawContent()
        {
            EnsureMultiStack();

            // 1. Barre rapide vocale persistante (toujours accessible)
            DrawQuickVoiceBar();
            GUILayout.Space(4);

            // 2. Navigation par onglets
            _currentTab = GUILayout.Toolbar(_currentTab, new[] { "Table", "Annuaire", "Chat", "Voix", "Vidéo" }, GUILayout.Height(24));
            GUILayout.Space(6);

            _scrollPos = GUILayout.BeginScrollView(_scrollPos);

            switch (_currentTab)
            {
                case 0:
                    DrawConnectionSection();
                    GUILayout.Space(6);
                    DrawRoomSection();
                    GUILayout.Space(6);
                    DrawMembersSection();
                    break;

                case 1:
                    DrawDirectorySection();
                    break;

                case 2:
                    DrawChatSection();
                    break;

                case 3:
                    DrawVoiceSection();
                    break;

                case 4:
                    DrawVideoSection();
                    break;
            }

            GUILayout.Space(6);
            Label($"<i>{_statusMessage}</i>");

            GUILayout.EndScrollView();

            if (GUI.changed)
            {
                // Le chat tapé en direct ne doit pas écraser les prefs à chaque frappe :
                // on ne persiste que l'identité de table (serveur, pseudo, room).
                try { CaptureDevPrefs(); } catch { /* ignore */ }
            }
        }

        private static string TruncateStr(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Length <= maxLen) return s;
            return s.Substring(0, maxLen - 1) + "…";
        }
    }
}

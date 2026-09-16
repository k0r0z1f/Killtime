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
    public class VTTRoomWindow : FloatingWindow<VTTRoomWindow>
    {
        protected override int WindowId => 886;
        protected override string Title => "Room Multijoueur";
        protected override Vector2 MinSize => _minSize;
        protected override Rect DefaultRect => new Rect(600, 96, 420, 560);
        protected override KeyCode[] ToggleKeys => _toggleKeys;

        private static readonly KeyCode[] _toggleKeys = { KeyCode.F4 };
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

        private void DrawConnectionSection()
        {
            Label("<b>1. Connexion au hub :</b>");
            GUILayout.BeginVertical(GUI.skin.box);
            bool connected = _net != null && _net.IsConnected;

            if (!connected)
            {
                GUILayout.BeginHorizontal();
                Label("Serveur :", GUILayout.Width(80));
                _serverUrl = GUILayout.TextField(_serverUrl ?? "");
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                Label("Pseudo :", GUILayout.Width(80));
                _username = GUILayout.TextField(_username ?? "");
                GUILayout.EndHorizontal();
                GUI.backgroundColor = new Color(0.3f, 0.8f, 0.4f);
                if (GUILayout.Button("🔌 Se connecter", GUILayout.Height(30)))
                {
                    EnsureMultiStack();
                    _room.SetIdentity(_username.Trim());
                    _net.SetServerUrl(_serverUrl.Trim());
                    _net.Connect();
                    _statusMessage = $"Connexion à {_serverUrl.Trim()}…";
                }
                GUI.backgroundColor = Color.white;
            }
            else
            {
                GUILayout.BeginHorizontal();
                GUI.color = Color.green;
                Label($"🟢 Connecté ({_room.ClientId ?? "…"})");
                GUI.color = Color.white;
                if (GUILayout.Button("🔌 Déconnecter", GUILayout.Width(120)))
                {
                    _room.Disconnect();
                }
                GUILayout.EndHorizontal();
                if (_net != null)
                {
                    Label($"<color=gray><i>Hub : {_net.ServerUrl}</i></color>");
                }
            }
            GUILayout.EndVertical();
        }

        private void DrawRoomSection()
        {
            Label("<b>2. Room de table :</b>");
            GUILayout.BeginVertical(GUI.skin.box);
            if (_room == null || !_room.InRoom)
            {
                GUILayout.BeginHorizontal();
                Label("Nom table :", GUILayout.Width(95));
                _tableName = GUILayout.TextField(_tableName ?? "");
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                Label("Joueurs max :", GUILayout.Width(95));
                _maxPlayersStr = GUILayout.TextField(_maxPlayersStr ?? "6", GUILayout.Width(45));
                _isPublic = GUILayout.Toggle(_isPublic, "📢 Lister publiquement");
                GUILayout.EndHorizontal();
                if (GUILayout.Button("✨ Créer une table (je deviens GM)", GUILayout.Height(30)))
                {
                    EnsureMultiStack();
                    if (!IsNetReady()) return;
                    int max = 6;
                    int.TryParse((_maxPlayersStr ?? "6").Trim(), out max);
                    max = Mathf.Clamp(max, 2, 12);
                    _maxPlayersStr = max.ToString();
                    _room.SetIdentity((_username ?? "Joueur").Trim());
                    _room.CreateRoom((_tableName ?? "").Trim(),
                        _isPublic ? VTTProtocol.VisibilityPublic : VTTProtocol.VisibilityPrivate, max);
                    _statusMessage = "Création de la table…";
                }
                GUILayout.BeginHorizontal();
                Label("Code room :", GUILayout.Width(95));
                _joinCode = GUILayout.TextField(_joinCode ?? "");
                string cleanCode = (_joinCode ?? "").Trim().ToUpperInvariant();
                GUI.enabled = !string.IsNullOrEmpty(cleanCode) && IsNetReady();
                if (GUILayout.Button("🚪 Rejoindre", GUILayout.Width(110)))
                {
                    _room.SetIdentity((_username ?? "Joueur").Trim());
                    _room.JoinRoom(cleanCode);
                    _statusMessage = $"Jonction à {cleanCode}…";
                }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
                if (!IsNetReady())
                {
                    Label("<color=gray><i>Connectez-vous d'abord pour créer/rejoindre.</i></color>");
                }
            }
            else
            {
                string roleLabel = _room.IsGM ? "<color=orange><b>GM</b></color>" : "<b>Joueur</b>";
                GUILayout.BeginHorizontal();
                Label($"<b>{_room.TableName ?? _room.RoomCode}</b> [{_room.RoomCode}] — vous êtes {roleLabel}");
                if (GUILayout.Button("🚪 Quitter", GUILayout.Width(90)))
                {
                    _room.LeaveRoom();
                }
                GUILayout.EndHorizontal();
                if (_room.IsGM)
                {
                    bool pub = _room.IsPublic;
                    bool next = GUILayout.Toggle(pub, "📢 Lister publiquement (annuaire)");
                    if (next != pub)
                    {
                        _room.SetVisibility(next ? VTTProtocol.VisibilityPublic : VTTProtocol.VisibilityPrivate);
                    }
                }
                else if (!_room.IsPublic)
                {
                    Label("<color=gray><i>Table privée (sur code uniquement).</i></color>");
                }

                GUILayout.Space(4);
                string mapName = _sync != null ? _sync.ActiveMapName : null;
                Label(string.IsNullOrEmpty(mapName)
                    ? "<color=gray><i>🗺️ Aucune carte VTT chargée par le GM.</i></color>"
                    : $"<color=#8FE3FF>🗺️ Carte VTT active : <b>{mapName}</b></color>");
                if (_room.IsGM && GUILayout.Button("🗺️ Charger / changer la carte de la table", GUILayout.Height(26)))
                {
                    MapEditorDevWindow.Open();
                    _statusMessage = "Choisissez « Charger » dans l'onglet Cartes : la carte sera envoyée à la table.";
                }

                GUILayout.Space(6);
                var ai = FindAnyObjectByType<Killtime.Tactics.AI.TacticalAIController>();
                if (_room.IsGM)
                {
                    Label("<b>🎮 Contrôle du Combat & de l'IA (GM) :</b>");
                    if (ai != null)
                    {
                        GUILayout.BeginHorizontal();
                        bool aiOn = GUILayout.Toggle(ai.IsAIEnabled, "IA Active");
                        if (aiOn != ai.IsAIEnabled) ai.IsAIEnabled = aiOn;

                        bool isFull = ai.Mode == Killtime.Tactics.AI.CombatAIMode.FullAuto;
                        string modeTxt = isFull ? "Mode: FullAuto" : "Mode: Normal";
                        if (GUILayout.Button(modeTxt, GUILayout.Width(130)))
                        {
                            ai.Mode = isFull ? Killtime.Tactics.AI.CombatAIMode.Normal : Killtime.Tactics.AI.CombatAIMode.FullAuto;
                        }
                        GUILayout.EndHorizontal();
                    }

                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("🔄 Synchroniser Combat (PV/PA)", GUILayout.Height(24)))
                    {
                        _sync?.BroadcastFullCombatState();
                        _sync?.BroadcastRoomSettings();
                        _statusMessage = "État du combat et paramètres diffusés à tous les joueurs.";
                    }
                    GUILayout.EndHorizontal();
                }
                else
                {
                    Label("<color=gray><i>🎮 Combat VTT : L'IA et l'arène sont pilotées en temps réel par le GM.</i></color>");
                }
            }
            GUILayout.EndVertical();
        }

        private void DrawDirectorySection()
        {
            Label($"<b>3. Tables publiques (build {Application.version}) :</b>");
            Label($"<color=gray><i>Annuaire : {EffectiveServerUrl()}</i></color>");
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUI.enabled = !_dirBusy;
            if (GUILayout.Button(_dirBusy ? "⏳ Actualisation…" : "🔄 Actualiser", GUILayout.Width(130)))
            {
                RefreshDirectory();
            }
            GUI.enabled = true;
            if (_dirError != null)
                Label($"<color=red>⚠️ {_dirError}</color>");
            else
                Label($"<color=gray><i>{_publicRooms.Count} table(s) compatible(s).</i></color>");
            GUILayout.EndHorizontal();
            _dirScroll = GUILayout.BeginScrollView(_dirScroll, GUILayout.Height(110));
            if (_publicRooms.Count == 0)
            {
                Label("<color=gray><i>Aucune table publique pour ce build. Créez-en une !</i></color>");
            }
            else
            {
                foreach (var r in _publicRooms)
                {
                    if (r == null) continue;
                    bool full = r.players >= r.maxPlayers;
                    GUILayout.BeginHorizontal();
                    Label($"<b>{r.tableName}</b> [{r.code}]  {r.players}/{r.maxPlayers}  GM: {r.gmName}");
                    GUI.enabled = !full && IsNetReadySoft();
                    if (GUILayout.Button(full ? "Complet" : "Rejoindre", GUILayout.Width(85)))
                    {
                        EnsureMultiStack();
                        _room.SetIdentity(_username.Trim());
                        _room.JoinRoom(r.code);
                        _joinCode = r.code;
                        _statusMessage = $"Jonction à {r.code}…";
                    }
                    GUI.enabled = true;
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        private bool IsNetReadySoft()
        {
            return _net != null && _net.IsConnected && _room != null;
        }

        /// <summary>
        /// URL source de vérité : la connexion active si elle existe, sinon le champ.
        /// Ça garantit que l'annuaire interroge toujours le hub auquel on est connecté.
        /// </summary>
        private string EffectiveServerUrl()
        {
            if (_net != null && _net.IsConnected && !string.IsNullOrEmpty(_net.ServerUrl))
                return _net.ServerUrl;
            return _serverUrl;
        }

        private void RefreshDirectory()
        {
            EnsureMultiStack();
            if (_directory == null || _dirBusy) return;
            string baseUrl = EffectiveServerUrl().Trim();
            if (string.IsNullOrEmpty(baseUrl))
            {
                _dirError = "renseignez l'URL du hub puis connectez-vous";
                return;
            }
            _dirBusy = true;
            _directory.FetchPublicRooms(baseUrl, Application.version, (rooms, error) =>
            {
                _dirBusy = false;
                if (error != null)
                {
                    _dirError = error;
                }
                else
                {
                    _dirError = null;
                    _publicRooms.Clear();
                    if (rooms != null) _publicRooms.AddRange(rooms);
                }
                _nextDirRefresh = Time.realtimeSinceStartup + DirRefreshInterval;
            });
        }

        private void DrawMembersSection()
        {
            Label("<b>4. Membres :</b>");
            GUILayout.BeginVertical(GUI.skin.box);
            if (_room == null || !_room.InRoom || _room.Members.Count == 0)
            {
                Label("<color=gray><i>Aucun membre (rejoignez une room).</i></color>");
            }
            else
            {
                foreach (var m in _room.Members)
                {
                    if (m == null) continue;
                    bool isSelf = (m.clientId == _room.ClientId);
                    string badge = m.IsGM ? "👑 GM" : "🎲 Joueur";

                    bool isSpeaking = false;
                    if (isSelf)
                    {
                        isSpeaking = _voice != null && _voice.IsLocalSpeaking;
                    }
                    else if (_voice != null && _voice.RemotePlayers.TryGetValue(m.clientId, out var rp))
                    {
                        isSpeaking = rp.IsSpeaking;
                    }
                    string speakIcon = isSpeaking ? "<color=#00FF88>● [VOIX]</color> " : "<color=gray>○</color> ";

                    GUILayout.BeginHorizontal();
                    Label($"{speakIcon}{(isSelf ? "<b>" : "")}{m.username} [{badge}]{(isSelf ? " (vous)</b>" : "")}");
                    if (_room.IsGM && !isSelf)
                    {
                        if (GUILayout.Button("⬆ GM", GUILayout.Width(60)))
                        {
                            _room.TransferGM(m.clientId);
                        }
                        if (GUILayout.Button("⛔", GUILayout.Width(40)))
                        {
                            _room.Kick(m.clientId);
                        }
                    }
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndVertical();
        }

        private void DrawChatSection()
        {
            GUILayout.BeginHorizontal();
            Label("<b>5. Chat de table :</b>");
            bool newLock = GUILayout.Toggle(_chatScrollLock, "🔒 Scroll Lock", GUILayout.Width(110));
            if (newLock != _chatScrollLock)
            {
                _chatScrollLock = newLock;
                if (!_chatScrollLock) _chatScrollToBottomPending = true;
                else _chatScrollToBottomPending = false;
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginVertical(GUI.skin.box);
            if (_chatScrollLock)
            {
                _chatScrollToBottomPending = false;
            }
            else if (_chatScrollToBottomPending)
            {
                _chatScroll.y = float.MaxValue;
                _chatScrollToBottomPending = false;
            }
            _chatScroll = GUILayout.BeginScrollView(_chatScroll, GUILayout.Height(120));
            if (_chatLog.Count == 0)
            {
                Label("<color=gray><i>Aucun message pour l'instant.</i></color>");
            }
            else
            {
                foreach (var line in _chatLog)
                {
                    Label(line);
                }
            }
            GUILayout.EndScrollView();
            GUILayout.BeginHorizontal();
            _chatInput = GUILayout.TextField(_chatInput);
            GUI.enabled = _room != null && _room.InRoom && !string.IsNullOrWhiteSpace(_chatInput);
            if (GUILayout.Button("Envoyer", GUILayout.Width(80)))
            {
                string text = _chatInput.Trim();
                _chatInput = "";
                _room.SendChat(text);
                AddChatLine($"<b>{_username.Trim()} (vous) :</b> {text}");
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private bool IsNetReady()
        {
            EnsureMultiStack();
            return _net != null && _net.IsConnected;
        }

        private void AddChatLine(string line)
        {
            _chatLog.Add(line);
            if (_chatLog.Count > 50) _chatLog.RemoveAt(0);
            // Verrou actif => la lecture reste figée ; sinon retour en bas.
            if (!_chatScrollLock)
            {
                _chatScrollToBottomPending = true;
            }
        }

        // ---------- Événements réseau -> UI ----------

        private void HandleConnected()
        {
            _statusMessage = "Connecté au hub. Créez ou rejoignez une room.";
        }

        private void HandleDisconnected(string reason)
        {
            _statusMessage = $"Déconnecté ({reason}).";
        }

        private void HandleJoined()
        {
            if (_room != null)
                _statusMessage = $"Dans la room {_room.RoomCode} en tant que {(_room.IsGM ? "GM 👑" : "Joueur 🎲")}.";
        }

        private void HandleLeft(string reason)
        {
            _statusMessage = $"Hors room ({reason}).";
            _nextDirRefresh = 0f; // re-liste les tables publiques en sortie de room
        }

        private void HandlePresence()
        {
            // L'état est relu à chaque OnGUI ; rien à faire ici.
        }

        private void HandleRoleChanged(string targetId, string role)
        {
            if (_room != null && targetId == _room.ClientId)
                _statusMessage = role == VTTProtocol.RoleGM
                    ? "Vous êtes maintenant GM 👑."
                    : "Vous êtes maintenant Joueur 🎲.";
            else
                _statusMessage = $"Rôle mis à jour : {targetId} -> {role}.";
        }

        private void HandleKicked(string reason)
        {
            _statusMessage = $"Expulsé de la room ({reason}).";
            _nextDirRefresh = 0f;
        }

        private void HandleVisibilityChanged(string visibility)
        {
            _statusMessage = visibility == VTTProtocol.VisibilityPrivate
                ? "Table privée — absente de l'annuaire."
                : "Table publique — visible dans l'annuaire.";
        }

        private void HandleRoomError(string message)
        {
            _statusMessage = $"⚠️ {message}";
        }

        private void HandleChatReceived(string from, string role, string text)
        {
            string badge = role == VTTProtocol.RoleGM ? "👑" : "🎲";
            AddChatLine($"<b>{from} {badge} :</b> {text}");
        }

        // =========================================================================
        // MODE VOIX VTT : CONTRÔLES, GRAPHES ET RÉDUCTION DE BRUITS
        // =========================================================================

        private void DrawQuickVoiceBar()
        {
            if (_voice == null) return;

            GUILayout.BeginHorizontal(GUI.skin.box);

            // Bouton Mute micro
            Color prevBg = GUI.backgroundColor;
            if (_voice.IsMuted) GUI.backgroundColor = new Color(1f, 0.35f, 0.35f);
            if (GUILayout.Button(_voice.IsMuted ? "Micro Coupé" : "Micro Actif", GUILayout.Width(105), GUILayout.Height(22)))
            {
                _voice.SetMuted(!_voice.IsMuted);
            }
            GUI.backgroundColor = prevBg;

            // Bouton Sourdine (Deafen)
            if (_voice.IsDeafened) GUI.backgroundColor = new Color(1f, 0.35f, 0.35f);
            if (GUILayout.Button(_voice.IsDeafened ? "Sourdine" : "Écoute", GUILayout.Width(85), GUILayout.Height(22)))
            {
                _voice.SetDeafened(!_voice.IsDeafened);
            }
            GUI.backgroundColor = prevBg;

            // Statut de transmission
            if (_voice.ActivationMode == Killtime.Multi.Voice.VoiceActivationMode.PushToTalk)
            {
                bool pttActive = Input.GetKey(_voice.PttKey);
                GUI.color = pttActive ? Color.green : Color.gray;
                GUILayout.Label($"[PTT: {_voice.PttKey}]", RichLabel(), GUILayout.Width(68));
                GUI.color = Color.white;
            }
            else if (_voice.ActivationMode == Killtime.Multi.Voice.VoiceActivationMode.VoiceActivity)
            {
                GUI.color = _voice.IsLocalSpeaking ? Color.green : Color.gray;
                GUILayout.Label(_voice.IsLocalSpeaking ? "● PARLE" : "○ Silencieux", RichLabel(), GUILayout.Width(75));
                GUI.color = Color.white;
            }

            // Mini VU-Mètre
            if (_voice.Dsp != null && _voiceVisualizer != null)
            {
                Rect vuRect = GUILayoutUtility.GetRect(60, 16, GUILayout.ExpandWidth(true));
                _voiceVisualizer.DrawVuMeter(vuRect, _voice.Dsp.CurrentRms, _voice.Dsp.CurrentPeak, _voice.Dsp.GateThreshold, _voice.Dsp.IsGateOpen);
            }

            GUILayout.EndHorizontal();
        }

        private void DrawVoiceSection()
        {
            if (_voice == null)
            {
                Label("<i>Gestionnaire de voix non initialisé.</i>");
                return;
            }

            Label("<b>🎙️ Configuration & Métrologie Voix :</b>");

            // 1. Périphérique Microphone
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            Label("<b>Microphone :</b>", GUILayout.Width(85));

            var devs = _voice.AvailableDevices;
            int devCount = devs.Count;
            if (devCount == 0)
            {
                Label("<color=red>⚠️ Aucun micro détecté</color>");
            }
            else
            {
                int curIdx = -1;
                for (int i = 0; i < devCount; i++)
                {
                    if (devs[i] == _voice.ActiveDeviceName) { curIdx = i; break; }
                }
                if (curIdx < 0) curIdx = 0;

                if (GUILayout.Button("◀", GUILayout.Width(22), GUILayout.Height(20)))
                {
                    int prev = (curIdx - 1 + devCount) % devCount;
                    _voice.SelectDevice(devs[prev]);
                }

                string devName = TruncateStr(_voice.ActiveDeviceName, 18);
                GUI.color = _voice.IsCapturing ? Color.cyan : Color.yellow;
                Label($"<b>{devName}</b>", GUILayout.Width(135));
                GUI.color = Color.white;

                if (GUILayout.Button("▶", GUILayout.Width(22), GUILayout.Height(20)))
                {
                    int next = (curIdx + 1) % devCount;
                    _voice.SelectDevice(devs[next]);
                }
            }

            if (GUILayout.Button("🔄 Scan", GUILayout.Width(58), GUILayout.Height(20)))
            {
                _voice.RefreshDevices();
                if (!_voice.IsCapturing) _voice.StartCapture();
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            Label($"Capture : {(_voice.IsCapturing ? "<color=#00FF88>● Active</color>" : "<color=orange>○ Inactive</color>")}");
            if (!_voice.IsCapturing && GUILayout.Button("Démarrer Capture", GUILayout.Width(130), GUILayout.Height(18)))
            {
                _voice.StartCapture();
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.Space(4);

            // 2. Choix du Codec Voix
            GUILayout.BeginVertical(GUI.skin.box);
            Label("<b>Codec Audio :</b>");
            int curCodecIdx = (int)_voice.CurrentCodecType;
            string[] codecLabels = new[] { "IMA 16k (Défaut)", "IMA 8k", "µ-law 16k", "µ-law 8k", "PCM 16k" };
            int newCodecIdx = GUILayout.Toolbar(curCodecIdx, codecLabels, GUILayout.Height(22));
            if (newCodecIdx != curCodecIdx)
            {
                _voice.SetCodec((Killtime.Multi.Voice.VoiceCodecType)newCodecIdx);
            }

            var activeCodec = Killtime.Multi.Voice.VoiceCodecRegistry.GetCodec(_voice.CurrentCodecType);
            Label($"<color=#00E5FF><b>{activeCodec.DisplayName}</b></color> — {activeCodec.Description}");
            GUILayout.EndVertical();

            GUILayout.Space(4);

            // 3. Mode de transmission (VAD / PTT / Continu)
            GUILayout.BeginVertical(GUI.skin.box);
            Label("<b>Mode de Transmission :</b>");
            int curModeIdx = (int)_voice.ActivationMode;
            int newModeIdx = GUILayout.Toolbar(curModeIdx, new[] { "🎙️ Détection (VAD)", "⚡ Push-to-Talk (PTT)", "〰️ Continu" }, GUILayout.Height(22));
            if (newModeIdx != curModeIdx)
            {
                _voice.SetActivationMode((Killtime.Multi.Voice.VoiceActivationMode)newModeIdx);
            }

            if (_voice.ActivationMode == Killtime.Multi.Voice.VoiceActivationMode.PushToTalk)
            {
                GUILayout.BeginHorizontal();
                Label("Touche PTT :", GUILayout.Width(85));
                KeyCode[] pttOptions = new[] { KeyCode.V, KeyCode.Space, KeyCode.T, KeyCode.LeftAlt, KeyCode.C };
                string[] pttNames = new[] { "V", "Espace", "T", "Alt", "C" };
                int currentPttIdx = Array.IndexOf(pttOptions, _voice.PttKey);
                if (currentPttIdx < 0) currentPttIdx = 0;
                int newPttIdx = GUILayout.Toolbar(currentPttIdx, pttNames, GUILayout.Height(20));
                if (newPttIdx != currentPttIdx)
                {
                    _voice.SetPttKey(pttOptions[newPttIdx]);
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();

            GUILayout.Space(4);

            // 4. Contrôles de Gain & Volumes
            GUILayout.BeginVertical(GUI.skin.box);
            Label("<b>Niveaux Entrée & Sortie :</b>");

            GUILayout.BeginHorizontal();
            Label("Gain Micro :", GUILayout.Width(110));
            float curGain = _voice.Dsp != null ? _voice.Dsp.InputGain : 1.0f;
            float newGain = GUILayout.HorizontalSlider(curGain, 0.2f, 2.5f);
            if (!Mathf.Approximately(curGain, newGain)) _voice.SetInputGain(newGain);
            Label($"{newGain:0.0}x", GUILayout.Width(40));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            Label("Volume Voix :", GUILayout.Width(110));
            float curOutVol = _voice.MasterOutputVolume;
            float newOutVol = GUILayout.HorizontalSlider(curOutVol, 0f, 2.0f);
            if (!Mathf.Approximately(curOutVol, newOutVol)) _voice.SetMasterOutputVolume(newOutVol);
            Label($"{Mathf.RoundToInt(newOutVol * 100)}%", GUILayout.Width(40));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            bool testActive = _voice.IsLoopbackTestActive;
            Color prevBg = GUI.backgroundColor;
            if (testActive) GUI.backgroundColor = new Color(0f, 0.85f, 1f);
            if (GUILayout.Button(testActive ? "■ Arrêter le Test Micro" : "▶ Tester mon micro (Loopback)", GUILayout.Height(22)))
            {
                _voice.SetLoopbackTest(!testActive);
            }
            GUI.backgroundColor = prevBg;
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.Space(4);

            // 4.b Modulateur Vocal Avancé (Voice Changer)
            GUILayout.BeginVertical(GUI.skin.box);
            bool prevVc = _voice.VoiceChangerEnabled;
            GUILayout.BeginHorizontal();
            bool newVc = GUILayout.Toggle(prevVc, " 🎭 <b>Modulateur Vocal (Voice Changer)</b>", RichToggle(), GUILayout.ExpandWidth(true));
            GUILayout.Label(newVc ? "<color=#00E5FF>● MODULÉ</color>" : "<color=gray>○ DIRECT</color>", RichLabel(), GUILayout.Width(75));
            GUILayout.EndHorizontal();

            if (newVc != prevVc)
            {
                _voice.SetVoiceChangerEnabled(newVc);
            }

            if (_voice.VoiceChanger != null)
            {
                GUILayout.Space(2);
                Label("<b>Profils & Personnalités Vocales :</b>");

                int curPresetIdx = (int)_voice.CurrentVoiceChangerPreset;
                string[] presetLabels = new[]
                {
                    "Naturel", "Opérateur Lourd", "Cyborg", "Infiltrateur", "Nytharite", "Radio",
                    "♀ Valkyrie", "♀ Mina", "♀ Prêtresse", "♀ V.I.C.I. (Android)", "Manuel"
                };
                int newPresetIdx = GUILayout.SelectionGrid(curPresetIdx, presetLabels, 3, GUILayout.Height(92));
                if (newPresetIdx != curPresetIdx)
                {
                    _voice.SetVoiceChangerPreset((VoiceChangerPreset)newPresetIdx);
                }

                if (_voice.VoiceChangerEnabled)
                {
                    GUILayout.Space(4);

                    // Transposition / Pitch
                    GUILayout.BeginHorizontal();
                    Label("Hauteur / Pitch :", GUILayout.Width(115));
                    float curPitch = _voice.VoiceChanger.PitchSemitones;
                    float newPitch = GUILayout.HorizontalSlider(curPitch, -12f, 12f);
                    if (!Mathf.Approximately(curPitch, newPitch))
                    {
                        _voice.SetVoiceChangerPitch(newPitch);
                        _voice.SetVoiceChangerPreset(VoiceChangerPreset.Custom);
                    }
                    string sign = newPitch >= 0 ? "+" : "";
                    Label($"{sign}{newPitch:0.0} st", GUILayout.Width(55));
                    GUILayout.EndHorizontal();

                    // Timbre / Formant
                    GUILayout.BeginHorizontal();
                    Label("Timbre / Larynx :", GUILayout.Width(115));
                    float curFormant = _voice.VoiceChanger.FormantShift;
                    float newFormant = GUILayout.HorizontalSlider(curFormant, -1f, 1f);
                    if (!Mathf.Approximately(curFormant, newFormant))
                    {
                        _voice.SetVoiceChangerFormant(newFormant);
                        _voice.SetVoiceChangerPreset(VoiceChangerPreset.Custom);
                    }
                    string formantTag = newFormant < -0.1f ? "Grave" : (newFormant > 0.1f ? "Clair" : "Neutre");
                    Label($"{formantTag} ({newFormant:0.0})", GUILayout.Width(55));
                    GUILayout.EndHorizontal();

                    // Saturation / Chaleur
                    GUILayout.BeginHorizontal();
                    Label("Grain / Chaleur :", GUILayout.Width(115));
                    float curDrive = _voice.VoiceChanger.HarmonicDrive;
                    float newDrive = GUILayout.HorizontalSlider(curDrive, 0f, 1f);
                    if (!Mathf.Approximately(curDrive, newDrive))
                    {
                        _voice.SetVoiceChangerDrive(newDrive);
                        _voice.SetVoiceChangerPreset(VoiceChangerPreset.Custom);
                    }
                    Label($"{Mathf.RoundToInt(newDrive * 100)}%", GUILayout.Width(55));
                    GUILayout.EndHorizontal();

                    // Robotisation / Ring Mod
                    GUILayout.BeginHorizontal();
                    Label("Robotisation :", GUILayout.Width(115));
                    float curRobo = _voice.VoiceChanger.RoboticModulation;
                    float newRobo = GUILayout.HorizontalSlider(curRobo, 0f, 1f);
                    if (!Mathf.Approximately(curRobo, newRobo))
                    {
                        _voice.SetVoiceChangerRobotic(newRobo);
                        _voice.SetVoiceChangerPreset(VoiceChangerPreset.Custom);
                    }
                    Label($"{Mathf.RoundToInt(newRobo * 100)}%", GUILayout.Width(55));
                    GUILayout.EndHorizontal();

                    // Mix Wet/Dry
                    GUILayout.BeginHorizontal();
                    Label("Mixage Effet :", GUILayout.Width(115));
                    float curMix = _voice.VoiceChanger.WetDryMix;
                    float newMix = GUILayout.HorizontalSlider(curMix, 0f, 1f);
                    if (!Mathf.Approximately(curMix, newMix))
                    {
                        _voice.SetVoiceChangerMix(newMix);
                    }
                    Label($"{Mathf.RoundToInt(newMix * 100)}%", GUILayout.Width(55));
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndVertical();

            GUILayout.Space(4);

            // 5. Réduction de Bruits & Traitement DSP
            GUILayout.BeginVertical(GUI.skin.box);
            Label("<b>Réduction de Bruits de Fond (DSP) :</b>");

            if (_voice.Dsp != null)
            {
                bool prevNg = _voice.Dsp.NoiseGateEnabled;
                GUILayout.BeginHorizontal();
                bool newNg = GUILayout.Toggle(prevNg, " <b>Noise Gate</b> (Suppression des silences & clics)", RichToggle(), GUILayout.ExpandWidth(true));
                GUILayout.Label(newNg ? "<color=#00FF88>● ACTIF</color>" : "<color=gray>○ INACTIF</color>", RichLabel(), GUILayout.Width(75));
                GUILayout.EndHorizontal();

                if (newNg != prevNg)
                {
                    _voice.SetNoiseGateEnabled(newNg);
                    if (Killtime.UI.DevUIPreferences.Current != null)
                    {
                        Killtime.UI.DevUIPreferences.Current.VoiceNoiseGateEnabled = newNg;
                        Killtime.UI.DevUIPreferences.MarkDirty();
                    }
                }

                if (_voice.Dsp.NoiseGateEnabled)
                {
                    GUILayout.BeginHorizontal();
                    Label("Seuil du Gate :", GUILayout.Width(115));
                    float curThresh = _voice.Dsp.GateThreshold;
                    float newThresh = GUILayout.HorizontalSlider(curThresh, 0.005f, 0.12f);
                    if (!Mathf.Approximately(curThresh, newThresh)) _voice.SetGateThreshold(newThresh);
                    float db = 20f * Mathf.Log10(newThresh);
                    Label($"{db:0.0} dB", GUILayout.Width(55));
                    GUILayout.EndHorizontal();
                }

                bool prevHp = _voice.Dsp.HighPassFilterEnabled;
                GUILayout.BeginHorizontal();
                bool newHp = GUILayout.Toggle(prevHp, " <b>Filtre Passe-Haut 80Hz</b> (Anti-ronflement & vibrations)", RichToggle(), GUILayout.ExpandWidth(true));
                GUILayout.Label(newHp ? "<color=#00FF88>● ACTIF</color>" : "<color=gray>○ INACTIF</color>", RichLabel(), GUILayout.Width(75));
                GUILayout.EndHorizontal();

                if (newHp != prevHp)
                {
                    _voice.SetHighPassFilterEnabled(newHp);
                    if (Killtime.UI.DevUIPreferences.Current != null)
                    {
                        Killtime.UI.DevUIPreferences.Current.VoiceHighPassFilter = newHp;
                        Killtime.UI.DevUIPreferences.MarkDirty();
                    }
                }

                bool prevNr = _voice.Dsp.NoiseReductionEnabled;
                GUILayout.BeginHorizontal();
                bool newNr = GUILayout.Toggle(prevNr, " <b>Réducteur de souffle adaptatif</b> (Bruit ambiant)", RichToggle(), GUILayout.ExpandWidth(true));
                GUILayout.Label(newNr ? "<color=#00FF88>● ACTIF</color>" : "<color=gray>○ INACTIF</color>", RichLabel(), GUILayout.Width(75));
                GUILayout.EndHorizontal();

                if (newNr != prevNr)
                {
                    _voice.SetNoiseReductionEnabled(newNr);
                    if (Killtime.UI.DevUIPreferences.Current != null)
                    {
                        Killtime.UI.DevUIPreferences.Current.VoiceNoiseReductionEnabled = newNr;
                        Killtime.UI.DevUIPreferences.MarkDirty();
                    }
                }

                bool prevLim = _voice.OutputLimiterEnabled;
                GUILayout.BeginHorizontal();
                bool newLim = GUILayout.Toggle(prevLim, " <b>Plafond Anti-Cris & Rires</b> (Protection auditive)", RichToggle(), GUILayout.ExpandWidth(true));
                GUILayout.Label(newLim ? "<color=#00FF88>● ACTIF</color>" : "<color=gray>○ INACTIF</color>", RichLabel(), GUILayout.Width(75));
                GUILayout.EndHorizontal();

                if (newLim != prevLim)
                {
                    _voice.SetOutputLimiterEnabled(newLim);
                    if (Killtime.UI.DevUIPreferences.Current != null)
                    {
                        Killtime.UI.DevUIPreferences.Current.VoiceOutputLimiterEnabled = newLim;
                        Killtime.UI.DevUIPreferences.MarkDirty();
                    }
                }

                if (_voice.OutputLimiterEnabled)
                {
                    GUILayout.BeginHorizontal();
                    Label("Seuil Plafond :", GUILayout.Width(110));
                    float curCeil = _voice.OutputCeiling;
                    float newCeil = GUILayout.HorizontalSlider(curCeil, 0.15f, 0.95f);
                    if (!Mathf.Approximately(curCeil, newCeil))
                    {
                        _voice.SetOutputCeiling(newCeil);
                        if (Killtime.UI.DevUIPreferences.Current != null)
                        {
                            Killtime.UI.DevUIPreferences.Current.VoiceOutputCeiling = newCeil;
                            Killtime.UI.DevUIPreferences.MarkDirty();
                        }
                    }
                    float ceilDb = 20f * Mathf.Log10(newCeil);
                    Label($"{ceilDb:0.0} dB", GUILayout.Width(55));
                    GUILayout.EndHorizontal();
                }

                // Gate Deleter (Élimination absolue des bruits hors parole)
                bool prevDel = _voice.GateDeleterEnabled;
                GUILayout.BeginHorizontal();
                bool newDel = GUILayout.Toggle(prevDel, " <b>Silence Absolu (Gate Deleter)</b> (Zéro fuite de bruit)", RichToggle(), GUILayout.ExpandWidth(true));
                GUILayout.Label(newDel ? "<color=#00FF88>● ACTIF</color>" : "<color=gray>○ INACTIF</color>", RichLabel(), GUILayout.Width(75));
                GUILayout.EndHorizontal();

                if (newDel != prevDel)
                {
                    _voice.SetGateDeleterEnabled(newDel);
                }

                // Filtre Anti-Clavier & Détection Transitoires
                bool prevKb = _voice.AntiKeyboardFilterEnabled;
                GUILayout.BeginHorizontal();
                bool newKb = GUILayout.Toggle(prevKb, " <b>Filtre Anti-Clavier</b> (Suppression des frappes & clics souris)", RichToggle(), GUILayout.ExpandWidth(true));
                GUILayout.Label(newKb ? "<color=#00FF88>● ACTIF</color>" : "<color=gray>○ INACTIF</color>", RichLabel(), GUILayout.Width(75));
                GUILayout.EndHorizontal();

                if (newKb != prevKb)
                {
                    _voice.SetAntiKeyboardFilterEnabled(newKb);
                }

                // Effet Comms Radio Militaire (Filtre passe-bande 300Hz - 3.4kHz)
                bool prevRad = _voice.RadioCommsEffectEnabled;
                GUILayout.BeginHorizontal();
                bool newRad = GUILayout.Toggle(prevRad, " <b>Filtre Radio Tactique</b> (Intercom casque 300Hz - 3.4kHz)", RichToggle(), GUILayout.ExpandWidth(true));
                GUILayout.Label(newRad ? "<color=#00FF88>● ACTIF</color>" : "<color=gray>○ INACTIF</color>", RichLabel(), GUILayout.Width(75));
                GUILayout.EndHorizontal();

                if (newRad != prevRad)
                {
                    _voice.SetRadioCommsEffectEnabled(newRad);
                }

                // Clarté & Présence Cristalline (Supprime le son étouffé / téléphone)
                bool prevCla = _voice.VoiceClarityEnabled;
                GUILayout.BeginHorizontal();
                bool newCla = GUILayout.Toggle(prevCla, " <b>Clarté Vocale HD</b> (Présence studio & brillance)", RichToggle(), GUILayout.ExpandWidth(true));
                GUILayout.Label(newCla ? "<color=#00FF88>● ACTIF</color>" : "<color=gray>○ INACTIF</color>", RichLabel(), GUILayout.Width(75));
                GUILayout.EndHorizontal();

                if (newCla != prevCla)
                {
                    _voice.SetVoiceClarityEnabled(newCla);
                }

                if (_voice.VoiceClarityEnabled && !_voice.RadioCommsEffectEnabled)
                {
                    GUILayout.BeginHorizontal();
                    Label("Brillance / Air :", GUILayout.Width(110));
                    float curCla = _voice.ClarityBoostAmount;
                    float newClaVal = GUILayout.HorizontalSlider(curCla, 0f, 1.2f);
                    if (!Mathf.Approximately(curCla, newClaVal))
                    {
                        _voice.SetClarityBoostAmount(newClaVal);
                    }
                    Label($"{Mathf.RoundToInt(newClaVal * 100)}%", GUILayout.Width(55));
                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndVertical();

            GUILayout.Space(4);

            // 6. Graphes & Visualisation Temps Réel
            GUILayout.BeginVertical(GUI.skin.box);
            Label("<b>Graphes & Métrologie Temps Réel :</b>");

            if (_voice.Dsp != null && _voiceVisualizer != null)
            {
                float displayPeak = (_voice.OutputLimiterEnabled && _voice.CurrentOutputPeak > 0f)
                    ? _voice.CurrentOutputPeak
                    : _voice.Dsp.CurrentPeak;

                bool isClamped = _voice.OutputLimiterEnabled && displayPeak >= (_voice.OutputCeiling - 0.02f);
                string peakStr = isClamped
                    ? $"<color=#00E5FF><b>{Mathf.RoundToInt(displayPeak * 100)}% [PLAFONNÉ]</b></color>"
                    : $"{Mathf.RoundToInt(displayPeak * 100)}%";

                Label($"RMS: {_voice.Dsp.CurrentRmsDb:0.0} dB | Crête: {peakStr} | Porte: {(_voice.Dsp.IsGateOpen ? "<color=#00FF88>OUVERTE</color>" : "<color=#888888>FERMÉE</color>")}");
                Rect vuRect = GUILayoutUtility.GetRect(280, 20, GUILayout.ExpandWidth(true));
                _voiceVisualizer.DrawVuMeter(vuRect, _voice.Dsp.CurrentRms, displayPeak, _voice.Dsp.GateThreshold, _voice.Dsp.IsGateOpen);

                GUILayout.Space(4);

                Label("Oscilloscope (Cyan: Signal propre / Gris: Brut) :");
                _voiceVisualizer.UpdateScopeTexture(_voice.Dsp);
                if (_voiceVisualizer.ScopeTexture != null)
                {
                    Rect scopeRect = GUILayoutUtility.GetRect(280, 50, GUILayout.ExpandWidth(true));
                    GUI.DrawTexture(scopeRect, _voiceVisualizer.ScopeTexture);
                }

                GUILayout.Space(4);

                Label("Spectre Fréquentiel (Basses, Bas-Médiums, Haut-Médiums, Aigus) :");
                Rect specRect = GUILayoutUtility.GetRect(280, 32, GUILayout.ExpandWidth(true));
                _voiceVisualizer.DrawSpectrumBars(specRect, _voice.Dsp);
            }
            GUILayout.EndVertical();

            GUILayout.Space(4);

            // 7. Joueurs Distants & Volumes Individuels
            GUILayout.BeginVertical(GUI.skin.box);
            Label("<b>Volumes des Joueurs Connectés :</b>");
            if (_room == null || !_room.InRoom || _room.Members.Count <= 1)
            {
                Label("<color=gray><i>Aucun autre joueur dans la room.</i></color>");
            }
            else
            {
                foreach (var m in _room.Members)
                {
                    if (m == null || m.clientId == _room.ClientId) continue;

                    var player = _voice.GetOrCreateRemotePlayer(m.clientId, m.username);
                    bool isSpeaking = player.IsSpeaking;
                    string speakIcon = isSpeaking ? "<color=#00FF88>●</color>" : "<color=gray>○</color>";

                    GUILayout.BeginHorizontal();
                    Label($"{speakIcon} <b>{m.username}</b>", GUILayout.Width(100));

                    player.PlayerVolume = GUILayout.HorizontalSlider(player.PlayerVolume, 0f, 2f);
                    Label($"{Mathf.RoundToInt(player.PlayerVolume * 100)}%", GUILayout.Width(35));

                    Color prevPlayerBg = GUI.backgroundColor;
                    if (player.IsMuted) GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
                    if (GUILayout.Button(player.IsMuted ? "MUET" : "ACTIF", GUILayout.Width(50), GUILayout.Height(18)))
                    {
                        player.IsMuted = !player.IsMuted;
                    }
                    GUI.backgroundColor = prevPlayerBg;

                    GUILayout.EndHorizontal();
                }
            }
            GUILayout.EndVertical();
        }

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

            if (GUILayout.Button("📹 Salon Vidéo (F6)", GUILayout.Width(135), GUILayout.Height(24)))
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

        private static string TruncateStr(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Length <= maxLen) return s;
            return s.Substring(0, maxLen - 1) + "…";
        }
    }
}

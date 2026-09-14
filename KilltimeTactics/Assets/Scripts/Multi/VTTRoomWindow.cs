using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.UI;

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
        protected override Rect DefaultRect => new Rect(600, 40, 420, 560);
        protected override KeyCode[] ToggleKeys => _toggleKeys;

        private static readonly KeyCode[] _toggleKeys = { KeyCode.F4 };
        private static readonly Vector2 _minSize = new Vector2(320, 200);
        private Vector2 _scrollPos;
        private Vector2 _chatScroll;
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
        private bool _subscribed;
        private GUIStyle _richLabel;

        /// <summary>Style label avec rich text (le skin IMGUI a richText=false par défaut).</summary>
        private GUIStyle RichLabel()
        {
            if (_richLabel == null)
            {
                _richLabel = new GUIStyle(GUI.skin.label) { richText = true };
            }
            return _richLabel;
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
        }

        protected override void Awake()
        {
            base.Awake();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            EnsureMultiStack();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            Unsubscribe();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            Unsubscribe();
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
            _scrollPos = GUILayout.BeginScrollView(_scrollPos);

            DrawConnectionSection();
            GUILayout.Space(6);
            DrawRoomSection();
            GUILayout.Space(6);
            DrawDirectorySection();
            GUILayout.Space(6);
            DrawMembersSection();
            GUILayout.Space(6);
            DrawChatSection();

            GUILayout.Space(6);
            Label($"<i>{_statusMessage}</i>");

            GUILayout.EndScrollView();
        }

        private void DrawConnectionSection()
        {
            Label("<b>1. Connexion au hub :</b>");
            GUILayout.BeginVertical(GUI.skin.box);
            bool connected = _net != null && _net.IsConnected;

            if (!connected)
            {
                GUILayout.BeginHorizontal();
                Label("Serveur :", GUILayout.Width(65));
                _serverUrl = GUILayout.TextField(_serverUrl);
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                Label("Pseudo :", GUILayout.Width(65));
                _username = GUILayout.TextField(_username);
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
                Label("Nom de table :", GUILayout.Width(95));
                _tableName = GUILayout.TextField(_tableName);
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                Label("Joueurs max :", GUILayout.Width(95));
                _maxPlayersStr = GUILayout.TextField(_maxPlayersStr, GUILayout.Width(50));
                _isPublic = GUILayout.Toggle(_isPublic, "📢 Lister publiquement");
                GUILayout.EndHorizontal();
                if (GUILayout.Button("✨ Créer une table (je deviens GM)", GUILayout.Height(30)))
                {
                    EnsureMultiStack();
                    if (!IsNetReady()) return;
                    int max = 6;
                    int.TryParse(_maxPlayersStr.Trim(), out max);
                    max = Mathf.Clamp(max, 2, 12);
                    _maxPlayersStr = max.ToString();
                    _room.SetIdentity(_username.Trim());
                    _room.CreateRoom(_tableName.Trim(),
                        _isPublic ? VTTProtocol.VisibilityPublic : VTTProtocol.VisibilityPrivate, max);
                    _statusMessage = "Création de la table…";
                }
                GUILayout.BeginHorizontal();
                _joinCode = GUILayout.TextField(_joinCode).Trim().ToUpperInvariant();
                GUI.enabled = !string.IsNullOrEmpty(_joinCode) && IsNetReady();
                if (GUILayout.Button("🚪 Rejoindre (code)", GUILayout.Width(130)))
                {
                    _room.SetIdentity(_username.Trim());
                    _room.JoinRoom(_joinCode);
                    _statusMessage = $"Jonction à {_joinCode}…";
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
                    GUILayout.BeginHorizontal();
                    Label($"{(isSelf ? "<b>" : "")}{m.username} [{badge}]{(isSelf ? " (vous)</b>" : "")}");
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
            Label("<b>5. Chat de table :</b>");
            GUILayout.BeginVertical(GUI.skin.box);
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
            _chatScroll.y = float.MaxValue;
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
    }
}

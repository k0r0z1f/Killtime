using System;
using System.Collections.Generic;
using UnityEngine;

namespace Killtime.Multi
{
    /// <summary>
    /// Gestionnaire de room VTT : create / join / leave, présence, rôles GM.
    /// Se place au-dessus de VTTNetworkClient (transport) et expose un état
    /// prêt à binder sur une UI (OnPresenceChanged, OnRoleChanged...).
    /// Autorité : seul le GM peut set_role / kick / ops GM-only — le serveur
    /// applique ces règles, le client ne fait que refléter l'état reçu.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(VTTNetworkClient))]
    public class VTTRoomManager : MonoBehaviour
    {
        [Header("Identité locale")]
        [SerializeField] private string _username = "Joueur";
        [Tooltip("Token de session Codex (cookie codex_auth_token) — optionnel, mode invité sinon.")]
        [SerializeField] private string _authToken = "";

        public static VTTRoomManager Instance { get; private set; }

        public VTTNetworkClient Net { get; private set; }
        public string ClientId { get; private set; }
        public string RoomCode { get; private set; }
        public string LocalRole { get; private set; } // "gm" | "player" | null
        public bool IsGM => LocalRole == VTTProtocol.RoleGM;
        public bool InRoom => !string.IsNullOrEmpty(RoomCode);
        // Métadonnées d'annuaire (synchronisées via room_joined / presence).
        public string TableName { get; private set; }
        public string Visibility { get; private set; } // "public" | "private"
        public bool IsPublic => Visibility != VTTProtocol.VisibilityPrivate;
        public int MaxPlayers { get; private set; }
        public string RoomBuildVersion { get; private set; }
        public IReadOnlyList<VTTMember> Members => _members;
        public string LastError { get; private set; }

        public event Action OnJoined;
        public event Action<string> OnLeft; // raison
        public event Action OnPresenceChanged;
        public event Action<string, string> OnRoleChanged; // (targetId, role)
        public event Action<string> OnVisibilityChanged;   // (visibility)
        public event Action<string> OnKicked;              // raison
        public event Action<string> OnError;
        public event Action<VTTOpEnvelope, string> OnTableOp; // (enveloppe, payloadJson brut)

        private readonly List<VTTMember> _members = new();

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Net = GetComponent<VTTNetworkClient>();
            if (Net == null) Net = gameObject.AddComponent<VTTNetworkClient>();
        }

        private void OnEnable()
        {
            if (Net != null)
            {
                Net.OnConnected += HandleConnected;
                Net.OnMessage += HandleMessage;
                Net.OnDisconnected += HandleDisconnected;
                Net.OnError += HandleNetError;
            }
        }

        private void OnDisable()
        {
            if (Net != null)
            {
                Net.OnConnected -= HandleConnected;
                Net.OnMessage -= HandleMessage;
                Net.OnDisconnected -= HandleDisconnected;
                Net.OnError -= HandleNetError;
            }
        }

        // ---------- API publique (appelée par l'UI / la table) ----------

        public void SetIdentity(string username, string authToken = null)
        {
            if (!string.IsNullOrEmpty(username)) _username = username.Trim().Substring(0, Math.Min(24, username.Trim().Length));
            if (authToken != null) _authToken = authToken;
        }

        public void Connect(string url = null)
        {
            Net.Connect(url);
        }

        public void Disconnect()
        {
            Net.Disconnect("client_leave");
        }

        public void CreateRoom()
        {
            CreateRoom(null, VTTProtocol.VisibilityPublic, 0);
        }

        /// <summary>Crée une room listée publiquement par défaut (server browser).</summary>
        public void CreateRoom(string tableName, string visibility, int maxPlayers)
        {
            if (string.IsNullOrEmpty(tableName)) tableName = $"Table de {_username}";
            if (visibility != VTTProtocol.VisibilityPrivate) visibility = VTTProtocol.VisibilityPublic;
            if (maxPlayers < 2) maxPlayers = 6;
            Net.Send(VTTProtocol.BuildCreateRoom(_username, _authToken, tableName, visibility, maxPlayers));
        }

        /// <summary>Bascule la visibilité d'annuaire (GM uniquement, appliquée par le hub).</summary>
        public void SetVisibility(string visibility)
        {
            if (!IsGM)
            {
                EmitError("Seul le GM peut changer la visibilité.", "forbidden_local");
                return;
            }
            Net.Send(VTTProtocol.BuildSetVisibility(visibility));
        }

        public void JoinRoom(string code)
        {
            code = VTTProtocol.NormalizeCode(code);
            if (string.IsNullOrEmpty(code))
            {
                EmitError("Code de room vide.", "empty_code");
                return;
            }
            Net.Send(VTTProtocol.BuildJoinRoom(code, _username));
        }

        public void LeaveRoom()
        {
            Net.Send(VTTProtocol.BuildLeaveRoom());
            // L'état local sera nettoyé à la réception de room_left / presence,
            // mais on nettoie tout de suite pour une UI réactive.
            ClearRoomState();
            try { OnLeft?.Invoke("leave"); } catch (Exception e) { Debug.LogException(e); }
        }

        public void SetRole(string targetId, string role)
        {
            if (!IsGM)
            {
                EmitError("Seul le GM peut changer les rôles.", "forbidden_local");
                return;
            }
            Net.Send(VTTProtocol.BuildSetRole(targetId, role));
        }

        public void TransferGM(string targetId) => SetRole(targetId, VTTProtocol.RoleGM);

        public void Kick(string targetId, string reason = "kicked_by_gm")
        {
            if (!IsGM)
            {
                EmitError("Seul le GM peut expulser.", "forbidden_local");
                return;
            }
            Net.Send(VTTProtocol.BuildKick(targetId, reason));
        }

        /// <summary>Envoie un op de table (relayé à toute la room par le hub).</summary>
        public void SendTableOp(string op, string payloadJson)
        {
            if (!InRoom)
            {
                EmitError("Rejoignez une room avant d'envoyer des ops.", "not_in_room");
                return;
            }
            Net.Send(VTTProtocol.BuildOp(op, payloadJson));
        }

        public void SendChat(string text)
        {
            SendTableOp(VTTProtocol.OpChat, $"{{\"text\":\"{VTTProtocol.Escape(text)}\"}}");
        }

        public VTTMember FindMember(string clientId)
        {
            return _members.Find(m => m.clientId == clientId);
        }

        // ---------- Réception ----------

        private void HandleConnected()
        {
            Net.Send(VTTProtocol.BuildHello(_username, _authToken, Application.version));
        }

        private void HandleDisconnected(string reason)
        {
            ClearRoomState();
            try { OnLeft?.Invoke(reason); } catch (Exception e) { Debug.LogException(e); }
        }

        private void HandleNetError(string err)
        {
            EmitError(err, "net");
        }

        private void HandleMessage(string json)
        {
            string type = VTTProtocol.PeekType(json);
            try
            {
                switch (type)
                {
                    case VTTProtocol.T_Welcome:
                        var w = JsonUtility.FromJson<VTTWelcomePayload>(json);
                        ClientId = w != null ? w.clientId : null;
                        break;

                    case VTTProtocol.T_RoomCreated:
                    case VTTProtocol.T_RoomJoined:
                        var info = JsonUtility.FromJson<VTTRoomInfoPayload>(json);
                        if (info != null)
                        {
                            RoomCode = VTTProtocol.NormalizeCode(info.code);
                            LocalRole = info.role;
                            ApplyRoomMeta(info.tableName, info.visibility, info.maxPlayers, info.buildVersion);
                            ApplyMembers(info.members);
                            try { OnJoined?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
                        }
                        break;

                    case VTTProtocol.T_Presence:
                        var p = JsonUtility.FromJson<VTTPresencePayload>(json);
                        if (p != null)
                        {
                            if (!string.IsNullOrEmpty(p.code)) RoomCode = VTTProtocol.NormalizeCode(p.code);
                            ApplyRoomMeta(p.tableName, p.visibility, p.maxPlayers, p.buildVersion);
                            ApplyMembers(p.members);
                        }
                        break;

                    case VTTProtocol.T_VisibilityUpdated:
                        var vis = JsonUtility.FromJson<VTTVisibilityUpdatedPayload>(json);
                        if (vis != null && !string.IsNullOrEmpty(vis.visibility))
                        {
                            Visibility = vis.visibility;
                            try { OnVisibilityChanged?.Invoke(vis.visibility); } catch (Exception e) { Debug.LogException(e); }
                            try { OnPresenceChanged?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
                        }
                        break;

                    case VTTProtocol.T_RoleUpdated:
                        var r = JsonUtility.FromJson<VTTRoleUpdatedPayload>(json);
                        if (r != null)
                        {
                            ApplyMembers(r.members);
                            if (r.targetId == ClientId) LocalRole = r.role;
                            try { OnRoleChanged?.Invoke(r.targetId, r.role); } catch (Exception e) { Debug.LogException(e); }
                        }
                        break;

                    case VTTProtocol.T_Kicked:
                        ClearRoomState();
                        string reason = ExtractStringField(json, "reason", "kicked_by_gm");
                        try { OnKicked?.Invoke(reason); } catch (Exception e) { Debug.LogException(e); }
                        break;

                    case "room_left":
                        ClearRoomState();
                        try { OnLeft?.Invoke("leave"); } catch (Exception e) { Debug.LogException(e); }
                        break;

                    case VTTProtocol.T_Op:
                        var op = JsonUtility.FromJson<VTTOpEnvelope>(json);
                        string payload = ExtractObjectField(json, "payload");
                        if (op != null)
                        {
                            if (op.op == VTTProtocol.OpVoice)
                            {
                                if (op.from != ClientId)
                                {
                                    Voice.VTTVoiceManager.Instance?.ReceiveVoicePacket(op.from, op.fromName, payload);
                                }
                                break;
                            }

                            if (op.op == VTTProtocol.OpVideo)
                            {
                                if (op.from != ClientId)
                                {
                                    Video.VTTVideoManager.Instance?.ReceiveVideoPacket(op.from, op.fromName, payload);
                                }
                                break;
                            }

                            if (op.from == ClientId)
                            {
                                // Écho local : le hub diffuse aussi à l'expéditeur.
                                // On le traite comme les autres pour garder un code simple,
                                // VTTTableSync filtre les doublons si besoin.
                            }
                            try { OnTableOp?.Invoke(op, payload); } catch (Exception e) { Debug.LogException(e); }
                        }
                        break;

                    case VTTProtocol.T_Error:
                        var err = JsonUtility.FromJson<VTTErrorPayload>(json);
                        EmitError(err != null ? err.message : "Erreur hub.", err != null ? err.code : "hub");
                        break;

                    case VTTProtocol.T_Pong:
                        break;

                    default:
                        Debug.LogWarning($"[VTT] Message inconnu ignoré : {type}");
                        break;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[VTT] Parse impossible ({type}) : {e.Message}\n{json}");
            }
        }

        private void ApplyMembers(List<VTTMember> members)
        {
            _members.Clear();
            if (members != null) _members.AddRange(members);
            if (!string.IsNullOrEmpty(ClientId))
            {
                var me = _members.Find(m => m.clientId == ClientId);
                if (me != null) LocalRole = me.role;
            }
            try { OnPresenceChanged?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
        }

        private void ApplyRoomMeta(string tableName, string visibility, int maxPlayers, string buildVersion)
        {
            if (!string.IsNullOrEmpty(tableName)) TableName = tableName;
            if (visibility == VTTProtocol.VisibilityPrivate || visibility == VTTProtocol.VisibilityPublic)
                Visibility = visibility;
            if (maxPlayers >= 2) MaxPlayers = maxPlayers;
            if (!string.IsNullOrEmpty(buildVersion)) RoomBuildVersion = buildVersion;
        }

        private void ClearRoomState()
        {
            RoomCode = null;
            LocalRole = null;
            TableName = null;
            Visibility = null;
            MaxPlayers = 0;
            RoomBuildVersion = null;
            _members.Clear();
            try { OnPresenceChanged?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
        }

        private void EmitError(string message, string code)
        {
            LastError = message;
            Debug.LogWarning($"[VTT] {code}: {message}");
            try { OnError?.Invoke(message); } catch (Exception e) { Debug.LogException(e); }
        }

        // --- Mini-extracteurs (délégation vers VTTProtocol) ---
        private static string ExtractStringField(string json, string field, string fallback)
        {
            return VTTProtocol.ExtractString(json, field, fallback);
        }

        private static string ExtractObjectField(string json, string field)
        {
            return VTTProtocol.ExtractObject(json, field);
        }
    }
}

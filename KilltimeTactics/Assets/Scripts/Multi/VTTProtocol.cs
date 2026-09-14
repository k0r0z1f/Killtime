using System;
using System.Collections.Generic;
using UnityEngine;

namespace Killtime.Multi
{
    /// <summary>
    /// Protocole VTT v1 — Virtual Table Killtime Tactics (Rooms + rôles GM).
    /// Miroir exact des messages JSON du hub Node (users/server.js, /ws/vtt).
    /// Tout passe en JSON texte, un objet par frame, champ discriminant "type".
    /// Zéro dépendance : sérialisation via JsonUtility + mini-helpers.
    /// </summary>
    public static class VTTProtocol
    {
        public const int Version = 1;
        public const string DefaultPath = "/ws/vtt";
        /// <summary>
        /// Hub par défaut (builds) : le Node users/server.js exposé via ngrok.
        /// Modifiable à tout moment dans la fenêtre Room ou l'inspecteur.
        /// </summary>
        public const string DefaultHubUrl = "wss://unsaddle-computer-profile.ngrok-free.dev/ws/vtt";

        // --- Types de messages (doivent matcher users/server.js) ---
        public const string T_Hello = "hello";
        public const string T_Welcome = "welcome";
        public const string T_CreateRoom = "create_room";
        public const string T_RoomCreated = "room_created";
        public const string T_JoinRoom = "join_room";
        public const string T_RoomJoined = "room_joined";
        public const string T_RoomLeft = "room_left";
        public const string T_LeaveRoom = "leave_room";
        public const string T_Presence = "presence";
        public const string T_SetRole = "set_role";
        public const string T_RoleUpdated = "role_updated";
        public const string T_Kick = "kick";
        public const string T_Kicked = "kicked";
        public const string T_SetVisibility = "set_visibility";
        public const string T_VisibilityUpdated = "visibility_updated";
        public const string T_Op = "op";
        public const string T_Ping = "ping";
        public const string T_Pong = "pong";
        public const string T_Error = "error";

        // --- Ops de table relayées (champ "op" des messages type "op") ---
        // Le serveur relaie sans simuler ; Unity reste l'autorité des règles.
        public const string OpUnitMove = "unit_move";       // { unitId, q, r }
        public const string OpChat = "chat";                // { text }
        public const string OpDice = "dice";                // { roll, detail }
        public const string OpTurnControl = "turn_control"; // GM uniquement { action }
        public const string OpSceneControl = "scene_control"; // GM uniquement
        public const string OpRoomSettings = "room_settings"; // GM uniquement

        public const string RoleGM = "gm";
        public const string RolePlayer = "player";

        // --- Visibilité d'annuaire (server browser auto-hébergé) ---
        public const string VisibilityPublic = "public";
        public const string VisibilityPrivate = "private";

        public static string Escape(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                    .Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }

        public static string BuildHello(string username, string authToken = null, string buildVersion = null)
        {
            string tokenPart = string.IsNullOrEmpty(authToken)
                ? ""
                : $",\"authToken\":\"{Escape(authToken)}\"";
            string buildPart = string.IsNullOrEmpty(buildVersion)
                ? ""
                : $",\"buildVersion\":\"{Escape(buildVersion)}\"";
            return $"{{\"type\":\"{T_Hello}\",\"username\":\"{Escape(username)}\"{tokenPart}{buildPart}}}";
        }

        public static string BuildCreateRoom(string username = null, string authToken = null,
            string tableName = null, string visibility = null, int maxPlayers = 0)
        {
            string u = string.IsNullOrEmpty(username) ? "" : $",\"username\":\"{Escape(username)}\"";
            string t = string.IsNullOrEmpty(authToken) ? "" : $",\"authToken\":\"{Escape(authToken)}\"";
            string n = string.IsNullOrEmpty(tableName) ? "" : $",\"tableName\":\"{Escape(tableName)}\"";
            string v = (visibility == VisibilityPrivate || visibility == VisibilityPublic)
                ? $",\"visibility\":\"{visibility}\"" : "";
            string m = (maxPlayers >= 2) ? $",\"maxPlayers\":{maxPlayers}" : "";
            return $"{{\"type\":\"{T_CreateRoom}\"{u}{t}{n}{v}{m}}}";
        }

        public static string BuildJoinRoom(string code, string username = null)
        {
            string u = string.IsNullOrEmpty(username) ? "" : $",\"username\":\"{Escape(username)}\"";
            return $"{{\"type\":\"{T_JoinRoom}\",\"code\":\"{Escape(code?.Trim().ToUpperInvariant())}\"{u}}}";
        }

        public static string BuildLeaveRoom() => "{\"type\":\"leave_room\"}";
        public static string BuildPing() => "{\"type\":\"ping\"}";

        public static string BuildSetRole(string targetId, string role)
        {
            return $"{{\"type\":\"{T_SetRole}\",\"targetId\":\"{Escape(targetId)}\",\"role\":\"{Escape(role)}\"}}";
        }

        public static string BuildKick(string targetId, string reason = "kicked_by_gm")
        {
            return $"{{\"type\":\"{T_Kick}\",\"targetId\":\"{Escape(targetId)}\",\"reason\":\"{Escape(reason)}\"}}";
        }

        public static string BuildSetVisibility(string visibility)
        {
            if (visibility != VisibilityPrivate) visibility = VisibilityPublic;
            return $"{{\"type\":\"{T_SetVisibility}\",\"visibility\":\"{visibility}\"}}";
        }

        /// <summary>Construit un op de table. payloadJson doit être un objet JSON valide (ex: "{}").</summary>
        public static string BuildOp(string op, string payloadJson)
        {
            if (string.IsNullOrEmpty(payloadJson)) payloadJson = "{}";
            return $"{{\"type\":\"{T_Op}\",\"op\":\"{Escape(op)}\",\"payload\":{payloadJson}}}";
        }

        public static string BuildUnitMoveOp(string unitId, int q, int r)
        {
            return BuildOp(OpUnitMove, $"{{\"unitId\":\"{Escape(unitId)}\",\"q\":{q},\"r\":{r}}}");
        }

        public static string BuildChatOp(string text)
        {
            return BuildOp(OpChat, $"{{\"text\":\"{Escape(text)}\"}}");
        }

        /// <summary>Normalise un code de room côté client (même règle que le serveur).</summary>
        public static string NormalizeCode(string code)
        {
            return (code ?? "").Trim().ToUpperInvariant();
        }

        /// <summary>Lecture rapide du champ "type" sans allocation lourde.</summary>
        public static string PeekType(string json)
        {
            if (string.IsNullOrEmpty(json)) return "";
            // Format attendu : {"type":"xxx",...} — on cherche "type":"..."
            int i = json.IndexOf("\"type\"", StringComparison.Ordinal);
            if (i < 0) return "";
            int colon = json.IndexOf(':', i);
            if (colon < 0) return "";
            int q1 = json.IndexOf('"', colon);
            if (q1 < 0) return "";
            int q2 = json.IndexOf('"', q1 + 1);
            if (q2 < 0) return "";
            return json.Substring(q1 + 1, q2 - q1 - 1);
        }
    }

    [Serializable]
    public class VTTMember
    {
        public string clientId;
        public string username;
        public string role; // "gm" | "player"
        public string userId;

        public bool IsGM => role == VTTProtocol.RoleGM;
    }

    [Serializable]
    public class VTTPresencePayload
    {
        public string type;
        public string code;
        public string tableName;
        public string visibility;
        public int maxPlayers;
        public string buildVersion;
        public List<VTTMember> members;
    }

    [Serializable]
    public class VTTWelcomePayload
    {
        public string type;
        public string clientId;
        public string username;
        public string userId;
    }

    [Serializable]
    public class VTTRoomInfoPayload
    {
        public string type;
        public string code;
        public string role;
        public string tableName;
        public string visibility;
        public int maxPlayers;
        public string buildVersion;
        public List<VTTMember> members;
    }

    [Serializable]
    public class VTTRoleUpdatedPayload
    {
        public string type;
        public string code;
        public string targetId;
        public string role;
        public string reason;
        public List<VTTMember> members;
    }

    [Serializable]
    public class VTTVisibilityUpdatedPayload
    {
        public string type;
        public string code;
        public string visibility;
    }

    [Serializable]
    public class VTTErrorPayload
    {
        public string type;
        public string code;
        public string message;
    }

    [Serializable]
    public class VTTOpEnvelope
    {
        public string type; // toujours "op"
        public string from;
        public string fromName;
        public string fromRole;
        public string op;
        public long at;
        // payload reste brut : chaque système (TableSync, chat, dés) le parse à son tour.
    }

    // --- Annuaire public des tables (GET /api/vtt/rooms) ---
    // JsonUtility ne parse pas les tableaux racine : le hub renvoie {"rooms":[...]}.

    [Serializable]
    public class VTTPublicRoom
    {
        public string code;
        public string tableName;
        public string buildVersion;
        public string gmName;
        public int players;
        public int maxPlayers;
        public long createdAt;
        public long lastActive;
    }

    [Serializable]
    public class VTTRoomDirectoryResponse
    {
        public List<VTTPublicRoom> rooms;
        public int count;
        public string build;
    }
}

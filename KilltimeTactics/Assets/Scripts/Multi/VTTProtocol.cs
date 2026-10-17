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
        public const string OpUnitMove = "unit_move";       // { unitId, q, r, path?, apCost? }
        public const string OpCombatAction = "combat_action"; // GM uniquement : attaque résolue, grenade, etc.
        public const string OpChat = "chat";                // { text }
        public const string OpDice = "dice";                // { roll, detail }
        public const string OpVoice = "voice";              // Trame audio voix { codec, sampleRate, seq, data }
        public const string OpVideo = "video";              // Trame vidéo webcam { width, height, seq, data }
        public const string OpTurnControl = "turn_control"; // GM uniquement { action, round, activeUnitId... }
        public const string OpSceneControl = "scene_control"; // GM uniquement
        public const string OpRoomSettings = "room_settings"; // GM uniquement { isAIEnabled, mode... }
        public const string OpMapLoad = "map_load";           // GM uniquement { map: TacticalMapSaveData }
        public const string OpMapRequest = "map_request";     // { } -> le GM renvoie OpMapLoad
        public const string OpActionRequest = "action_request"; // Joueur -> GM : intention d'action (attaque, grenade, souffle, fin de tour)
        public const string OpUnitClaim = "unit_claim";       // Joueur -> hub : avatars possédés { unitIds[] } (brouillard asymétrique)
        public const string OpWorldMap = "worldmap";          // GM uniquement { action: state|ping_sector|focus_sector }

        // --- Sous-actions pour OpTurnControl ---
        public const string TurnActionRoundStart = "round_start";
        public const string TurnActionTurnStart = "turn_start";
        public const string TurnActionEndTurn = "end_turn";
        public const string TurnActionCombatEnded = "combat_ended";
        public const string TurnActionStateSync = "state_sync";
        public const string TurnActionTargetSync = "target_sync";
        public const string TurnActionArenaReset = "arena_reset";
        public const string TurnActionPause = "pause";
        public const string TurnActionResume = "resume";

        // --- Sous-actions pour OpCombatAction ---
        public const string CombatActionMove = "move";
        public const string CombatActionAttack = "attack";
        public const string CombatActionGrenade = "grenade";
        public const string CombatActionSpell = "spell";
        public const string CombatActionBreath = "breath";

        // --- Sous-actions pour OpSceneControl ---
        public const string SceneActionPingHex = "ping_hex";
        public const string SceneActionFocusCamera = "focus_camera";
        public const string SceneActionFogReveal = "fog_reveal";
        public const string SceneActionFogHide = "fog_hide";
        public const string SceneActionSetLighting = "set_lighting";

        // --- Sous-actions pour OpWorldMap (overworld d'Hybris, GM uniquement) ---
        public const string WorldMapActionState = "state";         // { mapName, revision, partyNodeId, unlocked[], visited[], jours }
        public const string WorldMapActionPingSector = "ping_sector";   // { sectorId, colorHex, message }
        public const string WorldMapActionFocusSector = "focus_sector"; // { sectorId }

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

        public static string BuildUnitMoveOp(string unitId, int q, int r, List<VTTCoord> path = null, int apCost = 0)
        {
            if (path == null || path.Count == 0)
            {
                return BuildOp(OpUnitMove, $"{{\"unitId\":\"{Escape(unitId)}\",\"q\":{q},\"r\":{r}}}");
            }
            var payload = new VTTCombatActionPayload
            {
                action = "move",
                actorId = unitId,
                destQ = q,
                destR = r,
                path = path,
                apCost = apCost
            };
            return BuildOp(OpUnitMove, JsonUtility.ToJson(payload));
        }

        public static string BuildCombatActionOp(VTTCombatActionPayload payload)
        {
            return BuildOp(OpCombatAction, payload != null ? JsonUtility.ToJson(payload) : "{}");
        }

        public static string BuildTurnControlOp(VTTTurnControlPayload payload)
        {
            return BuildOp(OpTurnControl, payload != null ? JsonUtility.ToJson(payload) : "{}");
        }

        public static string BuildRoomSettingsOp(VTTRoomSettingsPayload payload)
        {
            return BuildOp(OpRoomSettings, payload != null ? JsonUtility.ToJson(payload) : "{}");
        }

        public static string BuildChatOp(string text)
        {
            return BuildOp(OpChat, $"{{\"text\":\"{Escape(text)}\"}}");
        }

        public static string BuildDiceOp(int roll, string formula = "", string detail = "", string reason = "", bool isCritical = false, bool isFumble = false)
        {
            var payload = new VTTDicePayload
            {
                roll = roll,
                formula = formula,
                detail = detail,
                reason = reason,
                isCritical = isCritical,
                isFumble = isFumble
            };
            return BuildOp(OpDice, JsonUtility.ToJson(payload));
        }

        public static string BuildDiceOp(VTTDicePayload payload)
        {
            return BuildOp(OpDice, payload != null ? JsonUtility.ToJson(payload) : "{}");
        }

        public static string BuildVoicePayloadJson(int codec, int sampleRate, int seq, string base64Data)
        {
            return $"{{\"codec\":{codec},\"sampleRate\":{sampleRate},\"seq\":{seq},\"data\":\"{Escape(base64Data)}\"}}";
        }

        public static string BuildVoiceOp(int codec, int sampleRate, int seq, string base64Data)
        {
            return BuildOp(OpVoice, BuildVoicePayloadJson(codec, sampleRate, seq, base64Data));
        }

        public static string BuildVideoPayloadJson(int width, int height, int seq, string base64Data)
        {
            return $"{{\"width\":{width},\"height\":{height},\"seq\":{seq},\"data\":\"{Escape(base64Data)}\"}}";
        }

        public static string BuildVideoOp(int width, int height, int seq, string base64Data)
        {
            return BuildOp(OpVideo, BuildVideoPayloadJson(width, height, seq, base64Data));
        }

        public static string BuildSceneControlOp(VTTSceneControlPayload payload)
        {
            return BuildOp(OpSceneControl, payload != null ? JsonUtility.ToJson(payload) : "{}");
        }

        public static string BuildScenePingHexOp(int q, int r, string colorHex = "#FFDD00", string message = "")
        {
            var payload = new VTTSceneControlPayload
            {
                action = SceneActionPingHex,
                targetQ = q,
                targetR = r,
                colorHex = colorHex,
                message = message
            };
            return BuildSceneControlOp(payload);
        }

        public static string BuildSceneFocusCameraOp(float x, float y, float zoom = 0f)
        {
            var payload = new VTTSceneControlPayload
            {
                action = SceneActionFocusCamera,
                cameraX = x,
                cameraY = y,
                cameraZoom = zoom
            };
            return BuildSceneControlOp(payload);
        }

        public static string BuildMapLoadOp(string mapJson)
        {
            if (string.IsNullOrEmpty(mapJson)) mapJson = "{}";
            return BuildOp(OpMapLoad, $"{{\"map\":{mapJson}}}");
        }

        public static string BuildMapRequestOp()
        {
            return BuildOp(OpMapRequest, "{}");
        }

        public static string BuildActionRequestOp(VTTActionRequestPayload payload)
        {
            return BuildOp(OpActionRequest, payload != null ? JsonUtility.ToJson(payload) : "{}");
        }

        /// <summary>
        /// Revendication d'avatars (brouillard asymétrique) : déclare au hub les
        /// unitId possédés par CE client. Le serveur ne transmet la position d'un
        /// ennemi à ce client que s'il est dans le champ d'au moins un avatar
        /// revendiqué (anti map-hack mémoire). Le client applique le même masque.
        /// </summary>
        public static string BuildUnitClaimOp(List<string> unitIds)
        {
            var payload = new VTTUnitClaimPayload { unitIds = unitIds ?? new List<string>() };
            return BuildOp(OpUnitClaim, JsonUtility.ToJson(payload));
        }

        /// <summary>Opération carte monde (overworld d'Hybris, GM uniquement).</summary>
        public static string BuildWorldMapOp(VTTWorldMapPayload payload)
        {
            return BuildOp(OpWorldMap, payload != null ? JsonUtility.ToJson(payload) : "{}");
        }

        /// <summary>État compact de campagne overworld : carte, révision, position, dévoilé.</summary>
        public static string BuildWorldMapStateOp(string mapName, int revision, string partyNodeId,
            List<string> unlocked, List<string> visited, int jours)
        {
            var payload = new VTTWorldMapPayload
            {
                action = WorldMapActionState,
                mapName = mapName ?? "",
                revision = revision,
                partyNodeId = partyNodeId ?? "",
                unlocked = unlocked ?? new List<string>(),
                visited = visited ?? new List<string>(),
                jours = jours
            };
            return BuildWorldMapOp(payload);
        }

        public static string BuildWorldMapPingSectorOp(string sectorId, string colorHex = "#FFDD00", string message = "")
        {
            var payload = new VTTWorldMapPayload
            {
                action = WorldMapActionPingSector,
                sectorId = sectorId ?? "",
                colorHex = colorHex,
                message = message ?? ""
            };
            return BuildWorldMapOp(payload);
        }

        public static string BuildWorldMapFocusSectorOp(string sectorId)
        {
            var payload = new VTTWorldMapPayload
            {
                action = WorldMapActionFocusSector,
                sectorId = sectorId ?? ""
            };
            return BuildWorldMapOp(payload);
        }

        /// <summary>Indique si l'opération requiert le rôle GM sur le hub (users/server.js).</summary>
        public static bool IsGMOp(string op)
        {
            return op == OpTurnControl || op == OpCombatAction || op == OpSceneControl || op == OpRoomSettings || op == OpMapLoad || op == OpWorldMap;
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

        /// <summary>Extrait un champ objet JSON brut (ex: "payload" ou "map").</summary>
        public static string ExtractObject(string json, string field)
        {
            if (string.IsNullOrEmpty(json)) return "{}";
            string key = "\"" + field + "\"";
            int i = json.IndexOf(key, StringComparison.Ordinal);
            if (i < 0) return "{}";
            int colon = json.IndexOf(':', i);
            if (colon < 0) return "{}";
            int start = json.IndexOf('{', colon);
            if (start < 0) return "{}";
            int depth = 0;
            bool inStr = false;
            for (int k = start; k < json.Length; k++)
            {
                char c = json[k];
                if (c == '"' && (k == 0 || json[k - 1] != '\\')) inStr = !inStr;
                if (!inStr)
                {
                    if (c == '{') depth++;
                    else if (c == '}')
                    {
                        depth--;
                        if (depth == 0) return json.Substring(start, k - start + 1);
                    }
                }
            }
            return "{}";
        }

        /// <summary>Extrait la valeur d'une chaîne scalaire dans un JSON sans allocation lourde.</summary>
        public static string ExtractString(string json, string field, string fallback = "")
        {
            if (string.IsNullOrEmpty(json)) return fallback;
            string key = "\"" + field + "\"";
            int i = json.IndexOf(key, StringComparison.Ordinal);
            if (i < 0) return fallback;
            int colon = json.IndexOf(':', i);
            if (colon < 0) return fallback;
            int q1 = json.IndexOf('"', colon);
            if (q1 < 0) return fallback;
            int q2 = json.IndexOf('"', q1 + 1);
            if (q2 < 0) return fallback;
            return json.Substring(q1 + 1, q2 - q1 - 1);
        }
    }

}


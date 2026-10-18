using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Killtime.UI;

namespace Killtime.Multi.Video
{
    public partial class VTTVideoManager
    {

        private void BroadcastEncodedFrame()
        {
            var room = VTTRoomManager.Instance;
            if (room == null || !room.InRoom) return;

            byte[] jpg = _localPreviewTexture.EncodeToJPG(_jpgQuality);
            if (jpg == null || jpg.Length == 0) return;

            string base64 = Convert.ToBase64String(jpg);
            _seq++;

            string payload = VTTProtocol.BuildVideoPayloadJson(TargetWidth, TargetHeight, _seq, base64);
            room.SendTableOp(VTTProtocol.OpVideo, payload);
        }

        public void ReceiveVideoPacket(string fromClientId, string fromUsername, string payloadJson)
        {
            if (string.IsNullOrEmpty(fromClientId) || string.IsNullOrEmpty(payloadJson)) return;

            var room = VTTRoomManager.Instance;
            if (room != null && fromClientId == room.ClientId) return;

            VTTVideoPayload payload;
            try
            {
                payload = JsonUtility.FromJson<VTTVideoPayload>(payloadJson);
            }
            catch
            {
                return;
            }

            if (payload == null) return;

            var player = GetOrCreateRemotePlayer(fromClientId, fromUsername);

            if (string.IsNullOrEmpty(payload.data))
            {
                player.SetStreamInactive();
                return;
            }

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(payload.data);
            }
            catch
            {
                return;
            }

            player.ReceiveFrame(bytes, payload.width > 0 ? payload.width : TargetWidth, payload.height > 0 ? payload.height : TargetHeight);
        }

        public VTTRemoteVideoPlayer GetOrCreateRemotePlayer(string clientId, string username)
        {
            if (_remotePlayers.TryGetValue(clientId, out var player) && player != null)
            {
                return player;
            }

            var go = new GameObject($"[Video] {username}");
            go.transform.SetParent(transform);
            player = go.AddComponent<VTTRemoteVideoPlayer>();
            player.Initialize(clientId, username);
            _remotePlayers[clientId] = player;
            return player;
        }

        private void HandleRoomLeft(string reason)
        {
            CleanupRemotePlayers();
        }

        private void HandlePresenceChanged()
        {
            var room = VTTRoomManager.Instance;
            if (room == null || !room.InRoom)
            {
                CleanupRemotePlayers();
                return;
            }

            var activeIds = new HashSet<string>();
            foreach (var m in room.Members)
            {
                if (m != null && !string.IsNullOrEmpty(m.clientId))
                {
                    activeIds.Add(m.clientId);
                }
            }

            var toRemove = new List<string>();
            foreach (var kvp in _remotePlayers)
            {
                if (!activeIds.Contains(kvp.Key))
                {
                    toRemove.Add(kvp.Key);
                }
            }

            for (int i = 0; i < toRemove.Count; i++)
            {
                string id = toRemove[i];
                if (_remotePlayers.TryGetValue(id, out var p) && p != null)
                {
                    Destroy(p.gameObject);
                }
                _remotePlayers.Remove(id);
            }
        }

        public void CleanupRemotePlayers()
        {
            foreach (var p in _remotePlayers.Values)
            {
                if (p != null) Destroy(p.gameObject);
            }
            _remotePlayers.Clear();
        }
    }
}

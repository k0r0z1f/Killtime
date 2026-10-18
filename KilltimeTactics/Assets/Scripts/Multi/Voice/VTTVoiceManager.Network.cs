using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.UI;

namespace Killtime.Multi.Voice
{
    public partial class VTTVoiceManager : MonoBehaviour
    {
        /// <summary>
        /// Appelé par VTTRoomManager lors de la réception d'un op "voice".
        /// </summary>
        public void ReceiveVoicePacket(string fromClientId, string fromUsername, string payloadJson)
        {
            if (string.IsNullOrEmpty(fromClientId) || string.IsNullOrEmpty(payloadJson)) return;
            if (_isDeafened) return;

            var room = VTTRoomManager.Instance;
            if (room != null && fromClientId == room.ClientId) return; // Ignore l'écho local

            VTTVoicePayload payload;
            try
            {
                payload = JsonUtility.FromJson<VTTVoicePayload>(payloadJson);
            }
            catch
            {
                return;
            }

            if (payload == null || string.IsNullOrEmpty(payload.data)) return;

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(payload.data);
            }
            catch
            {
                return;
            }

            var codec = VoiceCodecRegistry.GetCodec((VoiceCodecType)payload.codec);
            float[] decoded = codec.Decode(bytes);
            if (decoded == null || decoded.Length == 0) return;

            if (_outputLimiterEnabled)
            {
                ApplyLimiterBuffer(decoded, decoded.Length);
            }

            int packetSampleRate = (payload.sampleRate > 0) ? payload.sampleRate : codec.SampleRate;
            var player = GetOrCreateRemotePlayer(fromClientId, fromUsername);
            player.EnqueueSamples(decoded, packetSampleRate);
        }

        public VTTRemoteVoicePlayer GetOrCreateRemotePlayer(string clientId, string username)
        {
            if (_remotePlayers.TryGetValue(clientId, out var player) && player != null)
            {
                return player;
            }

            var go = new GameObject($"[Voice] {username}");
            go.transform.SetParent(transform);
            player = go.AddComponent<VTTRemoteVoicePlayer>();
            player.Initialize(clientId, username);

            player.OnSpeakingChanged += (id, speaking) =>
            {
                try { OnRemoteSpeakingChanged?.Invoke(id, speaking); }
                catch (Exception e) { Debug.LogException(e); }
            };

            _remotePlayers[clientId] = player;
            return player;
        }

        private void EnsureLoopbackPlayer()
        {
            if (_loopbackPlayer == null)
            {
                var go = new GameObject("[Voice] LoopbackTest");
                go.transform.SetParent(transform);
                _loopbackPlayer = go.AddComponent<VTTRemoteVoicePlayer>();
                _loopbackPlayer.Initialize("local_loopback", "Moi (Test)");
            }
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

            // Supprimer les joueurs déconnectés
            var activeIds = new HashSet<string>();
            foreach (var m in room.Members)
            {
                if (m != null && !string.IsNullOrEmpty(m.clientId))
                    activeIds.Add(m.clientId);
            }

            var toRemove = new List<string>();
            foreach (var kvp in _remotePlayers)
            {
                if (!activeIds.Contains(kvp.Key))
                {
                    toRemove.Add(kvp.Key);
                }
            }

            foreach (var id in toRemove)
            {
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

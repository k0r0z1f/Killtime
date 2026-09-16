using System;
using UnityEngine;

namespace Killtime.Multi.Video
{
    [DisallowMultipleComponent]
    public class VTTRemoteVideoPlayer : MonoBehaviour
    {
        public string ClientId { get; private set; }
        public string Username { get; private set; }
        public Texture2D VideoTexture { get; private set; }
        public bool IsStreamActive { get; private set; }

        private float _lastFrameTime = -999f;
        private const float StreamTimeoutSec = 2.5f;

        public void Initialize(string clientId, string username)
        {
            ClientId = clientId;
            Username = username;
            name = $"[VideoPlayer] {username} ({clientId})";
        }

        public void ReceiveFrame(byte[] jpgBytes, int width, int height)
        {
            if (jpgBytes == null || jpgBytes.Length == 0)
            {
                IsStreamActive = false;
                return;
            }

            if (VideoTexture == null || VideoTexture.width != width || VideoTexture.height != height)
            {
                if (VideoTexture != null) Destroy(VideoTexture);
                VideoTexture = new Texture2D(width, height, TextureFormat.RGB24, false)
                {
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
            }

            VideoTexture.LoadImage(jpgBytes, false);
            IsStreamActive = true;
            _lastFrameTime = Time.realtimeSinceStartup;
        }

        public void SetStreamInactive()
        {
            IsStreamActive = false;
        }

        private void Update()
        {
            if (IsStreamActive && (Time.realtimeSinceStartup - _lastFrameTime > StreamTimeoutSec))
            {
                IsStreamActive = false;
            }
        }

        private void OnDestroy()
        {
            if (VideoTexture != null)
            {
                Destroy(VideoTexture);
                VideoTexture = null;
            }
        }
    }
}
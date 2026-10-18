using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Killtime.UI;

namespace Killtime.Multi.Video
{
    [DisallowMultipleComponent]
    public partial class VTTVideoManager : MonoBehaviour
    {
        public static VTTVideoManager Instance { get; private set; }

        public const int TargetWidth = 240;
        public const int TargetHeight = 180;
        private const int PixelCount = TargetWidth * TargetHeight;

        [Header("Modèle IA (Unity Sentis / ONNX)")]
        [SerializeField] private Unity.InferenceEngine.ModelAsset _segmentationModel;
        [SerializeField, Range(0.1f, 0.9f)] private float _confidenceThreshold = 0.5f;

        [Header("Périphérique & Capture")]
        [SerializeField] private string _activeDeviceName = "";
        [SerializeField, Range(5, 20)] private int _targetFps = 10;
        [SerializeField, Range(20, 80)] private int _jpgQuality = 45;

        [Header("Flou d'Arrière-Plan (Bokeh Portrait)")]
        [SerializeField] private bool _blurBackground = true;
        [SerializeField, Range(2, 12)] private int _blurRadius = 8;

        [Header("Recadrage & Zoom (Cropping)")]
        [SerializeField, Range(1.0f, 2.5f)] private float _cropZoom = 1.0f;
        [SerializeField, Range(-0.5f, 0.5f)] private float _cropOffsetX = 0.0f;
        [SerializeField, Range(-0.5f, 0.5f)] private float _cropOffsetY = 0.0f;

        public bool IsCameraActive => _isCameraActive;
        public float CropZoom
        {
            get => _cropZoom;
            set => _cropZoom = Mathf.Clamp(value, 1.0f, 2.5f);
        }
        public float CropOffsetX
        {
            get => _cropOffsetX;
            set => _cropOffsetX = Mathf.Clamp(value, -0.5f, 0.5f);
        }
        public float CropOffsetY
        {
            get => _cropOffsetY;
            set => _cropOffsetY = Mathf.Clamp(value, -0.5f, 0.5f);
        }

        public void SetCrop(float zoom, float offsetX, float offsetY)
        {
            _cropZoom = Mathf.Clamp(zoom, 1.0f, 2.5f);
            _cropOffsetX = Mathf.Clamp(offsetX, -0.5f, 0.5f);
            _cropOffsetY = Mathf.Clamp(offsetY, -0.5f, 0.5f);
            SavePreferences();
        }

        public void ResetCrop()
        {
            _cropZoom = 1.0f;
            _cropOffsetX = 0.0f;
            _cropOffsetY = 0.0f;
            SavePreferences();
        }

        public string ActiveDeviceName => _activeDeviceName;
        public int TargetFps
        {
            get => _targetFps;
            set => _targetFps = Mathf.Clamp(value, 5, 20);
        }
        public bool BlurBackground
        {
            get => _blurBackground;
            set
            {
                _blurBackground = value;
                SavePreferences();
            }
        }
        public int BlurRadius
        {
            get => _blurRadius;
            set
            {
                _blurRadius = Mathf.Clamp(value, 2, 12);
                SavePreferences();
            }
        }

        public Texture2D LocalPreviewTexture => _localPreviewTexture;
        public IReadOnlyList<string> AvailableDevices => _availableDevices;
        public IReadOnlyDictionary<string, VTTRemoteVideoPlayer> RemotePlayers => _remotePlayers;

        private WebCamTexture _webCamTexture;
        private Texture2D _localPreviewTexture;
        private Color32[] _dstPixels;
        private Color32[] _blurredPixels;
        private Color32[] _tempBlurPixels;
        private Color32[] _rawSrcPixels;
        private float[] _smoothedMask;

        private bool _isCameraActive;
        private float _captureTimer;
        private int _seq;

        private Unity.InferenceEngine.Model _runtimeModel;
        private Unity.InferenceEngine.Worker _worker;
        private Unity.InferenceEngine.TensorShape _inputTensorShape;
        private int _modelInW = 256;
        private int _modelInH = 144;
        private bool _isInputNHWC = true;
        private float[] _inputBuffer;

        private readonly List<string> _availableDevices = new();
        private readonly Dictionary<string, VTTRemoteVideoPlayer> _remotePlayers = new();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            _dstPixels = new Color32[PixelCount];
            _blurredPixels = new Color32[PixelCount];
            _tempBlurPixels = new Color32[PixelCount];
            _smoothedMask = new float[PixelCount];

            _localPreviewTexture = new Texture2D(TargetWidth, TargetHeight, TextureFormat.RGB24, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            LoadPreferences();
            RefreshDevices();
        }

        private void OnEnable()
        {
            var room = VTTRoomManager.Instance;
            if (room != null)
            {
                room.OnLeft += HandleRoomLeft;
                room.OnPresenceChanged += HandlePresenceChanged;
            }
        }

        private void OnDisable()
        {
            var room = VTTRoomManager.Instance;
            if (room != null)
            {
                room.OnLeft -= HandleRoomLeft;
                room.OnPresenceChanged -= HandlePresenceChanged;
            }
            SetCameraActive(false);
            CleanupSentis();
        }

        private void OnDestroy()
        {
            SetCameraActive(false);
            CleanupSentis();
            if (_localPreviewTexture != null)
            {
                Destroy(_localPreviewTexture);
                _localPreviewTexture = null;
            }
            if (Instance == this) Instance = null;
        }

        private void CleanupSentis()
        {
            if (_worker != null)
            {
                _worker.Dispose();
                _worker = null;
            }
            _runtimeModel = null;
            _inputBuffer = null;
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Killtime.Multi.Video
{
    [DisallowMultipleComponent]
    public class VTTVideoManager : MonoBehaviour
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
        }

        public void ResetCrop()
        {
            _cropZoom = 1.0f;
            _cropOffsetX = 0.0f;
            _cropOffsetY = 0.0f;
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
            set => _blurBackground = value;
        }
        public int BlurRadius
        {
            get => _blurRadius;
            set => _blurRadius = Mathf.Clamp(value, 2, 12);
        }

        public Texture2D LocalPreviewTexture => _localPreviewTexture;
        public IReadOnlyList<string> AvailableDevices => _availableDevices;
        public IReadOnlyDictionary<string, VTTRemoteVideoPlayer> RemotePlayers => _remotePlayers;

        private WebCamTexture _webCamTexture;
        private Texture2D _localPreviewTexture;
        private Color32[] _dstPixels;
        private Color32[] _blurredPixels;
        private Color32[] _tempBlurPixels;
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

        public void RefreshDevices()
        {
            _availableDevices.Clear();
            var devs = WebCamTexture.devices;
            if (devs != null)
            {
                for (int i = 0; i < devs.Length; i++)
                {
                    _availableDevices.Add(devs[i].name);
                }
            }

            if (string.IsNullOrEmpty(_activeDeviceName) || !_availableDevices.Contains(_activeDeviceName))
            {
                _activeDeviceName = _availableDevices.Count > 0 ? _availableDevices[0] : "";
            }
        }

        public void SelectDevice(string deviceName)
        {
            if (_activeDeviceName == deviceName) return;
            _activeDeviceName = deviceName;

            if (_isCameraActive)
            {
                StartCamera();
            }
        }

        public void SetCameraActive(bool active)
        {
            if (_isCameraActive == active) return;

            if (active)
            {
                StartCamera();
            }
            else
            {
                StopCamera();
            }
        }

        public void SetBlurBackground(bool enabled)
        {
            _blurBackground = enabled;
        }

        private void InitSentisWorker()
        {
            CleanupSentis();

            if (_segmentationModel == null)
            {
                _segmentationModel = Resources.Load<Unity.InferenceEngine.ModelAsset>("selfie_segmentation_landscape")
                                  ?? Resources.Load<Unity.InferenceEngine.ModelAsset>("selfie_segmentation")
                                  ?? Resources.Load<Unity.InferenceEngine.ModelAsset>("model");
            }

            if (_segmentationModel == null)
            {
                Debug.LogWarning("[VTTVideoManager] Aucun modèle ONNX trouvé sous 'Assets/Resources/'. Le flou utilisera le mode portrait géométrique de repli.");
                return;
            }

            try
            {
                _runtimeModel = Unity.InferenceEngine.ModelLoader.Load(_segmentationModel);

                // Détection dynamique des dimensions et de l'orientation d'entrée du modèle ONNX
                if (_runtimeModel.inputs != null && _runtimeModel.inputs.Count > 0)
                {
                    var dynShape = _runtimeModel.inputs[0].shape;
                    if (dynShape.IsStatic() && dynShape.rank == 4)
                    {
                        var staticShape = dynShape.ToTensorShape();
                        _inputTensorShape = staticShape;
                        if (staticShape[3] == 3)
                        {
                            // NHWC: [1, Height, Width, 3] (format standard selfie_segmentation_landscape: 1x144x256x3)
                            _modelInH = staticShape[1];
                            _modelInW = staticShape[2];
                            _isInputNHWC = true;
                        }
                        else if (staticShape[1] == 3)
                        {
                            // NCHW: [1, 3, Height, Width]
                            _modelInH = staticShape[2];
                            _modelInW = staticShape[3];
                            _isInputNHWC = false;
                        }
                        else
                        {
                            _modelInH = 144;
                            _modelInW = 256;
                            _isInputNHWC = true;
                            _inputTensorShape = new Unity.InferenceEngine.TensorShape(1, _modelInH, _modelInW, 3);
                        }
                    }
                    else
                    {
                        _modelInH = 144;
                        _modelInW = 256;
                        _isInputNHWC = true;
                        _inputTensorShape = new Unity.InferenceEngine.TensorShape(1, _modelInH, _modelInW, 3);
                    }
                }
                else
                {
                    _modelInH = 144;
                    _modelInW = 256;
                    _isInputNHWC = true;
                    _inputTensorShape = new Unity.InferenceEngine.TensorShape(1, _modelInH, _modelInW, 3);
                }

                _inputBuffer = new float[_inputTensorShape.length];

                // Worker CPU optimisé Burst : exécution locale déterministe et instantanée
                var backend = Unity.InferenceEngine.BackendType.CPU;
                _worker = new Unity.InferenceEngine.Worker(_runtimeModel, backend);

                Debug.Log($"[VTTVideoManager] Modèle ONNX prêt : {_segmentationModel.name} ({_modelInW}x{_modelInH}, {(_isInputNHWC ? "NHWC" : "NCHW")})");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[VTTVideoManager] Échec chargement Sentis : {ex.Message}");
                CleanupSentis();
            }
        }

        private void StartCamera()
        {
            StopCamera();

            if (_availableDevices.Count == 0)
            {
                RefreshDevices();
                if (_availableDevices.Count == 0)
                {
                    Debug.LogWarning("[VTTVideoManager] Aucune caméra disponible.");
                    return;
                }
            }

            string device = string.IsNullOrEmpty(_activeDeviceName) ? _availableDevices[0] : _activeDeviceName;

            try
            {
                _webCamTexture = new WebCamTexture(device, 640, 480, _targetFps);
                _webCamTexture.Play();
                _isCameraActive = true;
                _captureTimer = 0f;
                Array.Clear(_smoothedMask, 0, _smoothedMask.Length);
                InitSentisWorker();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[VTTVideoManager] Échec du démarrage caméra '{device}': {ex.Message}");
                _isCameraActive = false;
            }
        }

        private void StopCamera()
        {
            _isCameraActive = false;

            if (_webCamTexture != null)
            {
                if (_webCamTexture.isPlaying)
                {
                    _webCamTexture.Stop();
                }
                Destroy(_webCamTexture);
                _webCamTexture = null;
            }

            CleanupSentis();

            var room = VTTRoomManager.Instance;
            if (room != null && room.InRoom)
            {
                string payload = VTTProtocol.BuildVideoPayloadJson(TargetWidth, TargetHeight, ++_seq, "");
                room.SendTableOp(VTTProtocol.OpVideo, payload);
            }
        }

        private void Update()
        {
            if (!_isCameraActive || _webCamTexture == null) return;
            if (!_webCamTexture.isPlaying || _webCamTexture.width <= 16 || _webCamTexture.height <= 16) return;

            if (_webCamTexture.didUpdateThisFrame)
            {
                ResampleWebcamFrame();
            }

            _captureTimer += Time.unscaledDeltaTime;
            float interval = 1f / Mathf.Max(1, _targetFps);

            if (_captureTimer >= interval)
            {
                _captureTimer = 0f;
                BroadcastEncodedFrame();
            }
        }

        private void ResampleWebcamFrame()
        {
            int srcW = _webCamTexture.width;
            int srcH = _webCamTexture.height;
            if (srcW <= 16 || srcH <= 16) return;

            Color32[] srcPixels = _webCamTexture.GetPixels32();
            if (srcPixels == null || srcPixels.Length != srcW * srcH) return;

            const float targetAspect = (float)TargetWidth / TargetHeight;
            float srcAspect = (float)srcW / srcH;

            int baseCropW = srcW;
            int baseCropH = srcH;

            if (srcAspect > targetAspect + 0.02f)
            {
                baseCropW = Mathf.RoundToInt(srcH * targetAspect);
                baseCropH = srcH;
            }
            else if (srcAspect < targetAspect - 0.02f)
            {
                baseCropW = srcW;
                baseCropH = Mathf.RoundToInt(srcW / targetAspect);
            }

            float zoom = Mathf.Max(1.0f, _cropZoom);
            int cropW = Mathf.Clamp(Mathf.RoundToInt(baseCropW / zoom), 32, srcW);
            int cropH = Mathf.Clamp(Mathf.RoundToInt(baseCropH / zoom), 24, srcH);

            int maxShiftX = srcW - cropW;
            int maxShiftY = srcH - cropH;

            int startX = maxShiftX / 2;
            int startY = maxShiftY / 2;

            if (maxShiftX > 0)
            {
                int shiftX = Mathf.RoundToInt(_cropOffsetX * maxShiftX);
                startX = Mathf.Clamp(startX + shiftX, 0, maxShiftX);
            }
            if (maxShiftY > 0)
            {
                int shiftY = Mathf.RoundToInt(_cropOffsetY * maxShiftY);
                startY = Mathf.Clamp(startY + shiftY, 0, maxShiftY);
            }

            bool vMirror = _webCamTexture.videoVerticallyMirrored;

            for (int y = 0; y < TargetHeight; y++)
            {
                int sampleY = startY + (y * cropH) / TargetHeight;
                if (vMirror)
                {
                    sampleY = (startY + cropH - 1) - (sampleY - startY);
                }
                sampleY = Mathf.Clamp(sampleY, 0, srcH - 1);

                int srcRowOffset = sampleY * srcW;
                int dstRowOffset = y * TargetWidth;

                for (int x = 0; x < TargetWidth; x++)
                {
                    int sampleX = Mathf.Clamp(startX + (x * cropW) / TargetWidth, 0, srcW - 1);
                    _dstPixels[dstRowOffset + x] = srcPixels[srcRowOffset + sampleX];
                }
            }

            if (_blurBackground)
            {
                if (_worker != null && _inputBuffer != null)
                {
                    ApplySentisSegmentationBokeh(_dstPixels);
                }
                else
                {
                    ApplyFallbackPortraitBokeh(_dstPixels);
                }
            }

            _localPreviewTexture.SetPixels32(_dstPixels);
            _localPreviewTexture.Apply(false);
        }

        private void ApplySentisSegmentationBokeh(Color32[] pixels)
        {
            if (_inputBuffer == null || _worker == null) return;

            // 1. Échantillonnage direct CPU des pixels caméra vers le tenseur d'entrée Sentis
            // Dans _dstPixels (Texture2D), y = 0 est le bas, y = TargetHeight - 1 est le haut.
            // Pour le modèle de vision, ty = 0 est le haut, ty = _modelInH - 1 est le bas.
            for (int ty = 0; ty < _modelInH; ty++)
            {
                float v = 1.0f - ((float)ty / (_modelInH - 1));
                int srcY = Mathf.Clamp(Mathf.RoundToInt(v * (TargetHeight - 1)), 0, TargetHeight - 1);
                int srcRow = srcY * TargetWidth;
                int dstRow = ty * _modelInW;

                for (int tx = 0; tx < _modelInW; tx++)
                {
                    float u = (float)tx / (_modelInW - 1);
                    int srcX = Mathf.Clamp(Mathf.RoundToInt(u * (TargetWidth - 1)), 0, TargetWidth - 1);
                    Color32 c = pixels[srcRow + srcX];

                    if (_isInputNHWC)
                    {
                        int idx = (dstRow + tx) * 3;
                        _inputBuffer[idx + 0] = c.r * (1f / 255f);
                        _inputBuffer[idx + 1] = c.g * (1f / 255f);
                        _inputBuffer[idx + 2] = c.b * (1f / 255f);
                    }
                    else
                    {
                        int plane = _modelInH * _modelInW;
                        int pix = dstRow + tx;
                        _inputBuffer[0 * plane + pix] = c.r * (1f / 255f);
                        _inputBuffer[1 * plane + pix] = c.g * (1f / 255f);
                        _inputBuffer[2 * plane + pix] = c.b * (1f / 255f);
                    }
                }
            }

            // 2. Inférence Sentis
            using var inputTensor = new Unity.InferenceEngine.Tensor<float>(_inputTensorShape, _inputBuffer);
            _worker.Schedule(inputTensor);

            var outputTensor = _worker.PeekOutput() as Unity.InferenceEngine.Tensor<float>;
            if (outputTensor == null && _runtimeModel != null && _runtimeModel.outputs.Count > 0)
            {
                outputTensor = _worker.PeekOutput(_runtimeModel.outputs[0].name) as Unity.InferenceEngine.Tensor<float>;
            }
            if (outputTensor == null) return;

            Unity.InferenceEngine.TensorShape outShape = outputTensor.shape;
            float[] rawMask = outputTensor.DownloadToArray();
            if (rawMask == null || rawMask.Length == 0) return;

            int outH = _modelInH;
            int outW = _modelInW;
            int outChannels = 1;
            bool isNHWC = true;

            if (outShape.rank == 4)
            {
                if (outShape[3] == 1 || outShape[3] == 2)
                {
                    outH = outShape[1];
                    outW = outShape[2];
                    outChannels = outShape[3];
                    isNHWC = true;
                }
                else if (outShape[1] == 1 || outShape[1] == 2)
                {
                    outChannels = outShape[1];
                    outH = outShape[2];
                    outW = outShape[3];
                    isNHWC = false;
                }
            }
            else if (outShape.rank == 3)
            {
                if (outShape[0] == 1) { outH = outShape[1]; outW = outShape[2]; }
                else { outH = outShape[0]; outW = outShape[1]; }
            }

            int fgChannel = (outChannels == 2) ? 1 : 0;
            int GetMaskIdx(int mx, int my)
            {
                if (isNHWC) return (my * outW + mx) * outChannels + fgChannel;
                return fgChannel * (outH * outW) + (my * outW + mx);
            }

            // Échantillonnage min/max pour déterminer la plage de sortie
            float minVal = float.MaxValue;
            float maxVal = float.MinValue;
            int step = Mathf.Max(1, rawMask.Length / 512);
            for (int i = 0; i < rawMask.Length; i += step)
            {
                float val = rawMask[i];
                if (val < minVal) minVal = val;
                if (val > maxVal) maxVal = val;
            }

            if (maxVal - minVal < 0.04f)
            {
                ApplyFallbackPortraitBokeh(pixels);
                return;
            }

            bool isLogits = (minVal < -0.15f || maxVal > 1.25f) && maxVal <= 50f;
            bool isByteScale = maxVal > 50f;

            float NormProb(float z)
            {
                if (isLogits) return 1.0f / (1.0f + Mathf.Exp(-Mathf.Clamp(z, -20f, 20f)));
                if (isByteScale) return Mathf.Clamp01(z / 255f);
                return Mathf.Clamp01(z);
            }

            // Test de polarité : compare les 4 coins (arrière-plan) au centre (sujet)
            float cTL = NormProb(rawMask[GetMaskIdx(0, 0)]);
            float cTR = NormProb(rawMask[GetMaskIdx(outW - 1, 0)]);
            float cBL = NormProb(rawMask[GetMaskIdx(0, outH - 1)]);
            float cBR = NormProb(rawMask[GetMaskIdx(outW - 1, outH - 1)]);
            float cornersAvg = (cTL + cTR + cBL + cBR) * 0.25f;
            float centerVal = NormProb(rawMask[GetMaskIdx(outW / 2, outH / 2)]);
            bool invert = (cornersAvg > centerVal + 0.20f);

            // 3. Remappage du masque sur la résolution de flux (TargetWidth x TargetHeight)
            for (int y = 0; y < TargetHeight; y++)
            {
                int row = y * TargetWidth;
                float v = (float)y / (TargetHeight - 1);
                int my = Mathf.Clamp(Mathf.RoundToInt((1.0f - v) * (outH - 1)), 0, outH - 1);

                for (int x = 0; x < TargetWidth; x++)
                {
                    int idx = row + x;
                    float u = (float)x / (TargetWidth - 1);
                    int mx = Mathf.Clamp(Mathf.RoundToInt(u * (outW - 1)), 0, outW - 1);

                    int maskIdx = GetMaskIdx(mx, my);
                    float rawVal = (maskIdx >= 0 && maskIdx < rawMask.Length) ? rawMask[maskIdx] : 0f;
                    float prob = NormProb(rawVal);
                    if (invert) prob = 1.0f - prob;

                    float targetMask;
                    if (prob >= _confidenceThreshold + 0.08f)
                    {
                        targetMask = 1.0f;
                    }
                    else if (prob <= _confidenceThreshold - 0.08f)
                    {
                        targetMask = 0.0f;
                    }
                    else
                    {
                        targetMask = Mathf.Clamp01((prob - (_confidenceThreshold - 0.08f)) / 0.16f);
                    }

                    _smoothedMask[idx] = Mathf.Lerp(_smoothedMask[idx], targetMask, 0.75f);
                }
            }

            // 4. Calcul du flou cinématique Bokeh
            SeparableBoxBlur(pixels, _blurredPixels, _tempBlurPixels, TargetWidth, TargetHeight, _blurRadius);

            // 5. Composition finale Premier-Plan / Arrière-Plan flouté
            for (int i = 0; i < PixelCount; i++)
            {
                float m = _smoothedMask[i];
                if (m >= 0.98f) continue;

                if (m <= 0.02f)
                {
                    pixels[i] = _blurredPixels[i];
                }
                else
                {
                    Color32 fg = pixels[i];
                    Color32 bg = _blurredPixels[i];
                    byte r = (byte)(fg.r * m + bg.r * (1f - m));
                    byte g = (byte)(fg.g * m + bg.g * (1f - m));
                    byte b = (byte)(fg.b * m + bg.b * (1f - m));
                    pixels[i] = new Color32(r, g, b, 255);
                }
            }
        }

        private void ApplyFallbackPortraitBokeh(Color32[] pixels)
        {
            SeparableBoxBlur(pixels, _blurredPixels, _tempBlurPixels, TargetWidth, TargetHeight, _blurRadius);

            float cx = (TargetWidth - 1) * 0.5f;
            float cy = (TargetHeight - 1) * 0.45f;
            float rx = TargetWidth * 0.32f;
            float ry = TargetHeight * 0.42f;

            for (int y = 0; y < TargetHeight; y++)
            {
                int row = y * TargetWidth;
                float dy = (y - cy) / ry;
                for (int x = 0; x < TargetWidth; x++)
                {
                    int idx = row + x;
                    float dx = (x - cx) / rx;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    float targetMask;
                    if (dist <= 0.75f) targetMask = 1.0f;
                    else if (dist >= 1.25f) targetMask = 0.0f;
                    else targetMask = Mathf.Clamp01((1.25f - dist) / 0.50f);

                    _smoothedMask[idx] = Mathf.Lerp(_smoothedMask[idx], targetMask, 0.75f);

                    float m = _smoothedMask[idx];
                    if (m >= 0.98f) continue;

                    if (m <= 0.02f)
                    {
                        pixels[idx] = _blurredPixels[idx];
                    }
                    else
                    {
                        Color32 fg = pixels[idx];
                        Color32 bg = _blurredPixels[idx];
                        byte r = (byte)(fg.r * m + bg.r * (1f - m));
                        byte g = (byte)(fg.g * m + bg.g * (1f - m));
                        byte b = (byte)(fg.b * m + bg.b * (1f - m));
                        pixels[idx] = new Color32(r, g, b, 255);
                    }
                }
            }
        }

        private static void SeparableBoxBlur(Color32[] src, Color32[] dst, Color32[] temp, int w, int h, int r)
        {
            r = Mathf.Clamp(r, 1, 16);
            SingleBoxBlurPass(src, dst, temp, w, h, r);
            if (r >= 3)
            {
                // Deuxième passe pour un bokeh doux de qualité cinématographique (approximation gaussienne B-spline)
                SingleBoxBlurPass(dst, dst, temp, w, h, Mathf.Max(2, r - 2));
            }
        }

        private static void SingleBoxBlurPass(Color32[] src, Color32[] dst, Color32[] temp, int w, int h, int r)
        {
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                int sumR = 0, sumG = 0, sumB = 0;
                int count = 0;

                for (int x = -r; x <= r; x++)
                {
                    Color32 p = src[row + Mathf.Clamp(x, 0, w - 1)];
                    sumR += p.r; sumG += p.g; sumB += p.b;
                    count++;
                }

                for (int x = 0; x < w; x++)
                {
                    temp[row + x] = new Color32((byte)(sumR / count), (byte)(sumG / count), (byte)(sumB / count), 255);
                    Color32 pOut = src[row + Mathf.Clamp(x - r, 0, w - 1)];
                    Color32 pIn = src[row + Mathf.Clamp(x + r + 1, 0, w - 1)];
                    sumR += pIn.r - pOut.r;
                    sumG += pIn.g - pOut.g;
                    sumB += pIn.b - pOut.b;
                }
            }

            for (int x = 0; x < w; x++)
            {
                int sumR = 0, sumG = 0, sumB = 0;
                int count = 0;

                for (int y = -r; y <= r; y++)
                {
                    Color32 p = temp[Mathf.Clamp(y, 0, h - 1) * w + x];
                    sumR += p.r; sumG += p.g; sumB += p.b;
                    count++;
                }

                for (int y = 0; y < h; y++)
                {
                    dst[y * w + x] = new Color32((byte)(sumR / count), (byte)(sumG / count), (byte)(sumB / count), 255);
                    Color32 pOut = temp[Mathf.Clamp(y - r, 0, h - 1) * w + x];
                    Color32 pIn = temp[Mathf.Clamp(y + r + 1, 0, h - 1) * w + x];
                    sumR += pIn.r - pOut.r;
                    sumG += pIn.g - pOut.g;
                    sumB += pIn.b - pOut.b;
                }
            }
        }

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
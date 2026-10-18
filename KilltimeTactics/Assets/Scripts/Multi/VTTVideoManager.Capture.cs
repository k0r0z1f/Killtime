using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Killtime.UI;

namespace Killtime.Multi.Video
{
    public partial class VTTVideoManager
    {

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
            SavePreferences();
        }

        private void LoadPreferences()
        {
            var p = DevUIPreferences.Current;
            if (p == null) return;

            _blurBackground = p.VideoBlurBackground;
            _blurRadius = Mathf.Clamp(p.VideoBlurRadius, 2, 12);
            _cropZoom = Mathf.Clamp(p.VideoCropZoom, 1.0f, 2.5f);
            _cropOffsetX = Mathf.Clamp(p.VideoCropOffsetX, -0.5f, 0.5f);
            _cropOffsetY = Mathf.Clamp(p.VideoCropOffsetY, -0.5f, 0.5f);
        }

        private void SavePreferences()
        {
            var p = DevUIPreferences.Current;
            if (p == null) return;

            p.VideoBlurBackground = _blurBackground;
            p.VideoBlurRadius = _blurRadius;
            p.VideoCropZoom = _cropZoom;
            p.VideoCropOffsetX = _cropOffsetX;
            p.VideoCropOffsetY = _cropOffsetY;

            DevUIPreferences.MarkDirty();
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

            int totalSrc = srcW * srcH;
            if (_rawSrcPixels == null || _rawSrcPixels.Length != totalSrc)
            {
                _rawSrcPixels = new Color32[totalSrc];
            }
            Color32[] srcPixels = _webCamTexture.GetPixels32(_rawSrcPixels);
            if (srcPixels == null || srcPixels.Length != totalSrc) return;

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
    }
}

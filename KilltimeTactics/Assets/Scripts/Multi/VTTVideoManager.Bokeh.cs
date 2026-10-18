using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Killtime.UI;

namespace Killtime.Multi.Video
{
    public partial class VTTVideoManager
    {

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
    }
}

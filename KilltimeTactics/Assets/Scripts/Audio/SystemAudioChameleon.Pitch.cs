using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Audio;

namespace Killtime.Audio.Experimental
{
    /// <summary>SystemAudioChameleon — Pitch DSP — NSDF grave/aigu, decimations, bandes spectrales (partial, move-only).</summary>
    public partial class SystemAudioChameleon : MonoBehaviour
    {
        private void PrepareBassBuffer()
        {
            float alphaLP = 1f - Mathf.Exp(-2f * Mathf.PI * 160f / _sampleRate);
            float rHP = Mathf.Exp(-2f * Mathf.PI * 22f / _sampleRate);
            float lp1 = 0f, lp2 = 0f, lp3 = 0f, lp4 = 0f;
            float hp = 0f, prevIn = 0f;
            float peak = 0.0001f;

            for (int i = 0; i < BassBufferSize; i++)
            {
                float sum = 0f;
                int baseIdx = i * BassDecimation;
                for (int k = 0; k < BassDecimation; k++)
                {
                    float s = _cleanedBuffer[baseIdx + k];

                    float hpOut = s - prevIn + rHP * hp;
                    prevIn = s;
                    hp = hpOut;

                    lp1 += alphaLP * (hpOut - lp1);
                    lp2 += alphaLP * (lp1 - lp2);
                    lp3 += alphaLP * (lp2 - lp3);
                    lp4 += alphaLP * (lp3 - lp4);

                    sum += lp4;
                }

                float sample = sum / BassDecimation;
                _bassDecimatedBuffer[i] = sample;
                float absVal = Mathf.Abs(sample);
                if (absVal > peak) peak = absVal;
            }

            if (peak > 0.001f)
            {
                float clipLevel = peak * 0.18f;
                for (int i = 0; i < BassBufferSize; i++)
                {
                    float v = _bassDecimatedBuffer[i];
                    if (v > clipLevel) _bassDecimatedBuffer[i] = v - clipLevel;
                    else if (v < -clipLevel) _bassDecimatedBuffer[i] = v + clipLevel;
                    else _bassDecimatedBuffer[i] = 0f;
                }
            }
        }

        private (float frequency, float confidence) ComputeBassPitch(int sampleRate)
        {
            int minLag = Math.Max(2, sampleRate / 180);
            int maxLag = Math.Min(sampleRate / 24, BassBufferSize - 1);

            for (int tau = 0; tau <= maxLag; tau++)
            {
                float r = 0f;
                float m = 0f;
                int maxJ = BassBufferSize - tau;
                for (int j = 0; j < maxJ; j++)
                {
                    float a = _bassDecimatedBuffer[j];
                    float b = _bassDecimatedBuffer[j + tau];
                    r += a * b;
                    m += a * a + b * b;
                }
                _bassNsdfBuffer[tau] = (m > 0.00001f) ? (2f * r / m) : 0f;
            }

            int bestLag = -1;
            float maxPeakVal = 0f;

            for (int tau = minLag; tau < maxLag - 1; tau++)
            {
                if (_bassNsdfBuffer[tau] > _bassNsdfBuffer[tau - 1] && _bassNsdfBuffer[tau] >= _bassNsdfBuffer[tau + 1])
                {
                    if (_bassNsdfBuffer[tau] > maxPeakVal)
                    {
                        maxPeakVal = _bassNsdfBuffer[tau];
                        bestLag = tau;
                    }
                }
            }

            if (maxPeakVal >= 0.26f && bestLag > 0)
            {
                int subLagCandidate = bestLag * 2;
                int searchMargin = Mathf.Max(2, (int)(bestLag * 0.12f));
                int subBestLag = -1;
                float subMaxVal = 0f;

                int sMin = Math.Max(minLag, subLagCandidate - searchMargin);
                int sMax = Math.Min(maxLag - 1, subLagCandidate + searchMargin);

                for (int tau = sMin; tau <= sMax; tau++)
                {
                    if (_bassNsdfBuffer[tau] > _bassNsdfBuffer[tau - 1] && _bassNsdfBuffer[tau] >= _bassNsdfBuffer[tau + 1])
                    {
                        if (_bassNsdfBuffer[tau] > subMaxVal)
                        {
                            subMaxVal = _bassNsdfBuffer[tau];
                            subBestLag = tau;
                        }
                    }
                }

                if (subBestLag > 0 && subMaxVal >= maxPeakVal * 0.58f && subMaxVal >= 0.22f)
                {
                    bestLag = subBestLag;
                    maxPeakVal = subMaxVal;
                }

                float alpha = _bassNsdfBuffer[bestLag - 1];
                float beta = _bassNsdfBuffer[bestLag];
                float gamma = _bassNsdfBuffer[bestLag + 1];
                float denom = alpha - 2f * beta + gamma;
                float delta = Mathf.Abs(denom) > 0.00001f ? (alpha - gamma) / (2f * denom) : 0f;
                delta = Mathf.Clamp(delta, -0.5f, 0.5f);

                float exactLag = bestLag + delta;
                float hz = (float)sampleRate / exactLag;
                return (hz, Mathf.Clamp01(maxPeakVal));
            }

            return (0f, 0f);
        }

        private void PrepareDecimatedPitchBuffer(float cutoffHz, float centerClipFactor)
        {
            float lpState1 = 0f;
            float lpState2 = 0f;
            float alphaLP = 1f - Mathf.Exp(-2f * Mathf.PI * cutoffHz / _sampleRate);

            float hpState = 0f;
            float prevInput = 0f;
            float rHP = Mathf.Exp(-2f * Mathf.PI * 35f / _sampleRate);

            float peak = 0.0001f;

            for (int i = 0; i < DecimatedSize; i++)
            {
                float sum = 0f;
                int baseIdx = i * DecimationFactor;
                for (int k = 0; k < DecimationFactor; k++)
                {
                    float s = _cleanedBuffer[baseIdx + k];

                    float hpOut = s - prevInput + rHP * hpState;
                    prevInput = s;
                    hpState = hpOut;

                    lpState1 += alphaLP * (hpOut - lpState1);
                    lpState2 += alphaLP * (lpState1 - lpState2);
                    sum += lpState2;
                }

                float sample = sum / DecimationFactor;
                _decimatedPitchBuffer[i] = sample;
                float absVal = Mathf.Abs(sample);
                if (absVal > peak) peak = absVal;
            }

            if (centerClipFactor > 0.01f && peak > 0.001f)
            {
                float clipLevel = peak * centerClipFactor;
                for (int i = 0; i < DecimatedSize; i++)
                {
                    float v = _decimatedPitchBuffer[i];
                    if (v > clipLevel) _decimatedPitchBuffer[i] = v - clipLevel;
                    else if (v < -clipLevel) _decimatedPitchBuffer[i] = v + clipLevel;
                    else _decimatedPitchBuffer[i] = 0f;
                }
            }
        }

        private void ExtractSpectralBands(float[] buffer)
        {
            float bassSum = 0f;
            float midSum = 0f;
            float highSum = 0f;
            float weightedFreqSum = 0f;
            float totalPower = 0.0001f;

            float prevSample = 0f;
            float lpBass = 0f;
            float lpMid = 0f;

            for (int i = 0; i < buffer.Length; i++)
            {
                float x = buffer[i];

                lpBass += 0.035f * (x - lpBass);
                bassSum += lpBass * lpBass;

                lpMid += 0.28f * (x - lpMid);
                float midPart = lpMid - lpBass;
                midSum += midPart * midPart;

                float hpHigh = x - lpMid;
                highSum += hpHigh * hpHigh;

                float diff = x - prevSample;
                prevSample = x;
                float power = x * x;
                totalPower += power;
                weightedFreqSum += Mathf.Abs(diff) * power;
            }

            BassEnergyRMS = Mathf.Sqrt(bassSum / buffer.Length);
            MidEnergyRMS = Mathf.Sqrt(midSum / buffer.Length);
            HighEnergyRMS = Mathf.Sqrt(highSum / buffer.Length);

            SpectralCentroidHz = Mathf.Clamp((weightedFreqSum / totalPower) * (_sampleRate * 0.5f) * 0.5f, 60f, 10000f);
        }

        private (float frequency, float confidence) ComputeNSDFPitch(int sampleRate)
        {
            int minLag = sampleRate / 720;
            int maxLag = Math.Min(sampleRate / 35, DecimatedSize - 1);

            for (int tau = 0; tau <= maxLag; tau++)
            {
                float r = 0f;
                float m = 0f;
                int maxJ = DecimatedSize - tau;
                for (int j = 0; j < maxJ; j++)
                {
                    float a = _decimatedPitchBuffer[j];
                    float b = _decimatedPitchBuffer[j + tau];
                    r += a * b;
                    m += a * a + b * b;
                }
                _nsdfBuffer[tau] = (m > 0.00001f) ? (2f * r / m) : 0f;
            }

            float maxPeakValue = 0f;
            bool isPositive = false;

            for (int tau = 1; tau < maxLag - 1; tau++)
            {
                if (!isPositive && _nsdfBuffer[tau] > 0f) isPositive = true;
                if (isPositive && _nsdfBuffer[tau] <= 0f) isPositive = false;

                if (isPositive && tau >= minLag)
                {
                    if (_nsdfBuffer[tau] > _nsdfBuffer[tau - 1] && _nsdfBuffer[tau] >= _nsdfBuffer[tau + 1])
                    {
                        if (_nsdfBuffer[tau] > maxPeakValue)
                        {
                            maxPeakValue = _nsdfBuffer[tau];
                        }
                    }
                }
            }

            if (maxPeakValue < 0.25f) return (0f, 0f);

            float threshold = maxPeakValue * 0.80f;
            int chosenPeakIndex = -1;
            isPositive = false;

            for (int tau = 1; tau < maxLag - 1; tau++)
            {
                if (!isPositive && _nsdfBuffer[tau] > 0f) isPositive = true;
                if (isPositive && _nsdfBuffer[tau] <= 0f) isPositive = false;

                if (isPositive && tau >= minLag)
                {
                    if (_nsdfBuffer[tau] > _nsdfBuffer[tau - 1] && _nsdfBuffer[tau] >= _nsdfBuffer[tau + 1])
                    {
                        if (_nsdfBuffer[tau] >= threshold)
                        {
                            chosenPeakIndex = tau;
                            break;
                        }
                    }
                }
            }

            if (chosenPeakIndex > 0)
            {
                float alpha = _nsdfBuffer[chosenPeakIndex - 1];
                float beta = _nsdfBuffer[chosenPeakIndex];
                float gamma = _nsdfBuffer[chosenPeakIndex + 1];

                float denom = alpha - 2f * beta + gamma;
                float delta = Mathf.Abs(denom) > 0.00001f ? (alpha - gamma) / (2f * denom) : 0f;
                delta = Mathf.Clamp(delta, -0.5f, 0.5f);

                float exactLag = chosenPeakIndex + delta;
                float hz = (float)sampleRate / exactLag;
                return (hz, Mathf.Clamp01(beta));
            }

            return (0f, 0f);
        }
    }
}

using System;
using UnityEngine;

namespace Killtime.Multi.Voice
{
    /// <summary>
    /// Pipeline complet de traitement numérique du signal micro (DSP) :
    /// - Préamplification & régulation du gain
    /// - Filtre passe-haut anti-ronflement (80 Hz biquad)
    /// - Filtre passe-bas anti-aliasing & sifflements (7.2 kHz)
    /// - Réducteur de bruit de fond adaptatif (souffle statique / ventilateur)
    /// - Noise Gate avec hystérésis, maintien (hold) et fondu doux (attack/release)
    /// - Limiteur soft-knee anti-saturation / écrêtage
    /// - Télémétrie temps réel : RMS, Peak, dB, spectre 4 bandes et tampons d'oscilloscope.
    /// </summary>
    public class VoiceDspProcessor
    {
        // --- Paramètres configurables ---
        public float InputGain = 1.0f;
        public bool HighPassFilterEnabled = true;
        public bool NoiseReductionEnabled = true;
        public bool NoiseGateEnabled = true;
        public bool LimiterEnabled = true;
        public bool AntiKeyboardFilterEnabled = true;
        public bool IsKeyboardDetected { get; private set; }

        /// <summary>Seuil d'ouverture du Noise Gate en valeur linéaire [0.001f .. 0.25f] (ex: 0.02f ~ -34 dB).</summary>
        public float GateThreshold = 0.02f;
        /// <summary>Durée de maintien du Gate ouvert après passage sous le seuil (secondes).</summary>
        public float GateHoldTimeSec = 0.18f;
        /// <summary>Durée du fondu de fermeture (secondes).</summary>
        public float GateReleaseTimeSec = 0.08f;
        /// <summary>Durée du fondu d'ouverture (secondes).</summary>
        public float GateAttackTimeSec = 0.01f;

        // --- Télémétrie temps réel (lue par l'UI / les graphes) ---
        public float CurrentRms { get; private set; }
        public float CurrentPeak { get; private set; }
        public float CurrentRmsDb { get; private set; }
        public bool IsGateOpen { get; private set; }
        public float GateEnvelope { get; private set; } // [0.0 = fermé/muet, 1.0 = ouvert]

        // 4 bandes de fréquences normalisées [0..1]
        public float EnergyBass { get; private set; }
        public float EnergyLowMid { get; private set; }
        public float EnergyHighMid { get; private set; }
        public float EnergyTreble { get; private set; }

        // Tampons d'oscilloscope pour rendu graphique
        public const int ScopeBufferSize = 256;
        public readonly float[] RawScopeBuffer = new float[ScopeBufferSize];
        public readonly float[] CleanScopeBuffer = new float[ScopeBufferSize];
        private int _scopeWriteIdx;

        // --- État interne DSP ---
        private readonly int _sampleRate;

        // Coefficients Biquad Passe-Haut (80 Hz @ sampleRate)
        private float _hpB0, _hpB1, _hpB2, _hpA1, _hpA2;
        private float _hpX1, _hpX2, _hpY1, _hpY2;

        // Coefficients Biquad Passe-Bas (7200 Hz @ sampleRate)
        private float _lpB0, _lpB1, _lpB2, _lpA1, _lpA2;
        private float _lpX1, _lpX2, _lpY1, _lpY2;

        // Noise Gate state
        private float _gateHoldTimer;
        private float _currentGateGain = 1.0f;

        // Adaptive Noise Floor Estimation
        private float _noiseFloorRms = 0.005f;

        // Détection de bandes de fréquences (filtres résonants 1er ordre)
        private float _bandBassState, _bandLowMidState, _bandHighMidState, _bandTrebleState;

        public VoiceDspProcessor(int sampleRate = 16000)
        {
            _sampleRate = Mathf.Max(8000, sampleRate);
            CalculateFilterCoefficients();
        }

        private void CalculateFilterCoefficients()
        {
            float fs = _sampleRate;

            // 1. Filtre Passe-Haut Butterworth 2nd ordre (fc = 80 Hz, Q = 0.7071)
            float fcHp = Mathf.Min(80f, fs * 0.45f);
            float w0Hp = 2f * Mathf.PI * fcHp / fs;
            float cosHp = Mathf.Cos(w0Hp);
            float sinHp = Mathf.Sin(w0Hp);
            float alphaHp = sinHp / (2f * 0.7071f);

            float a0Hp = 1f + alphaHp;
            _hpB0 = ((1f + cosHp) / 2f) / a0Hp;
            _hpB1 = (-(1f + cosHp)) / a0Hp;
            _hpB2 = ((1f + cosHp) / 2f) / a0Hp;
            _hpA1 = (-2f * cosHp) / a0Hp;
            _hpA2 = (1f - alphaHp) / a0Hp;

            // 2. Filtre Passe-Bas Butterworth 2nd ordre (fc = 7200 Hz, Q = 0.7071)
            float fcLp = Mathf.Min(7200f, fs * 0.45f);
            float w0Lp = 2f * Mathf.PI * fcLp / fs;
            float cosLp = Mathf.Cos(w0Lp);
            float sinLp = Mathf.Sin(w0Lp);
            float alphaLp = sinLp / (2f * 0.7071f);

            float a0Lp = 1f + alphaLp;
            _lpB0 = ((1f - cosLp) / 2f) / a0Lp;
            _lpB1 = (1f - cosLp) / a0Lp;
            _lpB2 = ((1f - cosLp) / 2f) / a0Lp;
            _lpA1 = (-2f * cosLp) / a0Lp;
            _lpA2 = (1f - alphaLp) / a0Lp;
        }

        /// <summary>
        /// Traite un bloc d'échantillons audio en entrée et écrit les échantillons nettoyés dans outputBuffer.
        /// </summary>
        public void Process(float[] inputBuffer, float[] outputBuffer, int count, float deltaTime)
        {
            if (inputBuffer == null || outputBuffer == null || count <= 0) return;

            float sumSquares = 0f;
            float peak = 0f;

            // 1. Calcul des métriques brutes & préampli
            for (int i = 0; i < count; i++)
            {
                float raw = inputBuffer[i] * InputGain;
                float absRaw = Mathf.Abs(raw);
                sumSquares += raw * raw;
                if (absRaw > peak) peak = absRaw;

                // Filtrage Passe-Haut (80 Hz)
                float s = raw;
                if (HighPassFilterEnabled)
                {
                    float y = _hpB0 * s + _hpB1 * _hpX1 + _hpB2 * _hpX2 - _hpA1 * _hpY1 - _hpA2 * _hpY2;
                    _hpX2 = _hpX1; _hpX1 = s;
                    _hpY2 = _hpY1; _hpY1 = y;
                    s = y;
                }

                // Filtrage Passe-Bas (Anti-aliasing / coupe-sifflement)
                if (_sampleRate > 8000)
                {
                    float y = _lpB0 * s + _lpB1 * _lpX1 + _lpB2 * _lpX2 - _lpA1 * _lpY1 - _lpA2 * _lpY2;
                    _lpX2 = _lpX1; _lpX1 = s;
                    _lpY2 = _lpY1; _lpY1 = y;
                    s = y;
                }

                outputBuffer[i] = s;
            }

            float rms = Mathf.Sqrt(sumSquares / count);
            CurrentRms = rms;
            CurrentPeak = peak;
            CurrentRmsDb = rms > 0.00001f ? 20f * Mathf.Log10(rms) : -100f;

            // 2. Estimation adaptative du plancher de bruit (Noise Floor Tracking)
            if (rms < _noiseFloorRms * 1.5f || rms < 0.015f)
            {
                _noiseFloorRms = Mathf.Lerp(_noiseFloorRms, rms, 0.08f);
            }
            else
            {
                _noiseFloorRms = Mathf.Lerp(_noiseFloorRms, rms * 0.1f, 0.002f);
            }
            _noiseFloorRms = Mathf.Clamp(_noiseFloorRms, 0.001f, 0.05f);

            // 2b. Analyse multi-blocs pour discrimination des bruits d'impact (clavier / souris)
            bool isKeyboardTransient = false;
            if (AntiKeyboardFilterEnabled && rms >= GateThreshold)
            {
                int subBlockSize = count / 8;
                float maxSubRms = 0f;
                float minSubRms = float.MaxValue;
                int activeSubBlocks = 0;
                float subThreshold = Mathf.Max(0.008f, GateThreshold * 0.70f);

                for (int b = 0; b < 8; b++)
                {
                    float subSum = 0f;
                    int startIdx = b * subBlockSize;
                    int endIdx = (b == 7) ? count : startIdx + subBlockSize;
                    int len = endIdx - startIdx;
                    for (int j = startIdx; j < endIdx; j++)
                    {
                        float val = inputBuffer[j] * InputGain;
                        subSum += val * val;
                    }
                    float bRms = Mathf.Sqrt(subSum / len);
                    if (bRms > maxSubRms) maxSubRms = bRms;
                    if (bRms < minSubRms) minSubRms = bRms;
                    if (bRms >= subThreshold) activeSubBlocks++;
                }

                float crestFactor = (rms > 0.001f) ? (peak / rms) : 0f;
                if (activeSubBlocks <= 2 && maxSubRms > 2.2f * (minSubRms + 0.002f) && crestFactor > 2.8f)
                {
                    isKeyboardTransient = true;
                }
            }
            IsKeyboardDetected = isKeyboardTransient;

            // 3. Machine d'état du Noise Gate (inhibée par les clics clavier isolés)
            bool thresholdExceeded = (rms >= GateThreshold) && !isKeyboardTransient;

            if (thresholdExceeded)
            {
                _gateHoldTimer = GateHoldTimeSec;
                IsGateOpen = true;
            }
            else
            {
                if (_gateHoldTimer > 0f)
                {
                    _gateHoldTimer -= deltaTime;
                    IsGateOpen = true;
                }
                else
                {
                    IsGateOpen = false;
                }
            }

            float targetGateGain = (!NoiseGateEnabled || IsGateOpen) ? 1.0f : 0.0f;
            float sampleAttack = 1f - Mathf.Exp(-1f / (_sampleRate * Mathf.Max(0.001f, GateAttackTimeSec)));
            float sampleRelease = 1f - Mathf.Exp(-1f / (_sampleRate * Mathf.Max(0.001f, GateReleaseTimeSec)));

            // 4. Expandeur descendant progressif & lissage de porte continu sans clics
            float bassSum = 0f, lowMidSum = 0f, highMidSum = 0f, trebleSum = 0f;
            float snr = rms / Mathf.Max(0.001f, _noiseFloorRms);
            float expansionGain = 1.0f;

            if (NoiseReductionEnabled && _noiseFloorRms > 0.001f && snr < 2.5f)
            {
                float normalizedSnr = Mathf.Clamp01((snr - 1.0f) / 1.5f);
                expansionGain = Mathf.Lerp(0.20f, 1.0f, normalizedSnr);
            }

            for (int i = 0; i < count; i++)
            {
                float s = outputBuffer[i];

                if (NoiseReductionEnabled)
                {
                    s *= expansionGain;
                }

                if (isKeyboardTransient)
                {
                    s *= 0.08f;
                }
                else if (AntiKeyboardFilterEnabled && IsGateOpen)
                {
                    float maxAllowed = Mathf.Max(0.025f, rms * 2.8f);
                    if (Mathf.Abs(s) > maxAllowed)
                    {
                        float excess = Mathf.Abs(s) - maxAllowed;
                        s = Mathf.Sign(s) * (maxAllowed + 0.25f * excess);
                    }
                }

                float gateCoeff = (targetGateGain > _currentGateGain) ? sampleAttack : sampleRelease;
                _currentGateGain += gateCoeff * (targetGateGain - _currentGateGain);
                s *= _currentGateGain;

                if (LimiterEnabled)
                {
                    s = SoftKneeLimit(s);
                }

                outputBuffer[i] = s;

                // Télémétrie spectrale 4 bandes (filtres à un pôle)
                _bandBassState += 0.08f * (s - _bandBassState);
                _bandLowMidState += 0.20f * (s - _bandLowMidState);
                _bandHighMidState += 0.45f * (s - _bandHighMidState);
                _bandTrebleState += 0.75f * (s - _bandTrebleState);

                bassSum += Mathf.Abs(_bandBassState);
                lowMidSum += Mathf.Abs(_bandLowMidState - _bandBassState);
                highMidSum += Mathf.Abs(_bandHighMidState - _bandLowMidState);
                trebleSum += Mathf.Abs(s - _bandHighMidState);

                // Écriture dans le buffer d'oscilloscope (décimation)
                if (i % Mathf.Max(1, count / 16) == 0)
                {
                    RawScopeBuffer[_scopeWriteIdx] = inputBuffer[i] * InputGain;
                    CleanScopeBuffer[_scopeWriteIdx] = s;
                    _scopeWriteIdx = (_scopeWriteIdx + 1) % ScopeBufferSize;
                }
            }

            GateEnvelope = _currentGateGain;

            // Normalisation des 4 bandes pour le visualiseur graphique
            EnergyBass = Mathf.Lerp(EnergyBass, Mathf.Clamp01((bassSum / count) * 4.5f), 0.25f);
            EnergyLowMid = Mathf.Lerp(EnergyLowMid, Mathf.Clamp01((lowMidSum / count) * 4.0f), 0.25f);
            EnergyHighMid = Mathf.Lerp(EnergyHighMid, Mathf.Clamp01((highMidSum / count) * 3.5f), 0.25f);
            EnergyTreble = Mathf.Lerp(EnergyTreble, Mathf.Clamp01((trebleSum / count) * 4.0f), 0.25f);
        }

        private static float SoftKneeLimit(float x)
        {
            const float threshold = 0.82f;
            if (x > threshold)
            {
                float excess = x - threshold;
                return threshold + (1f - threshold) * (float)Math.Tanh(excess / (1f - threshold));
            }
            if (x < -threshold)
            {
                float excess = -x - threshold;
                return -(threshold + (1f - threshold) * (float)Math.Tanh(excess / (1f - threshold)));
            }
            return x;
        }

        public void Reset()
        {
            _hpX1 = _hpX2 = _hpY1 = _hpY2 = 0f;
            _lpX1 = _lpX2 = _lpY1 = _lpY2 = 0f;
            _bandBassState = _bandLowMidState = _bandHighMidState = _bandTrebleState = 0f;
            _currentGateGain = 1.0f;
            _gateHoldTimer = 0f;
            IsGateOpen = false;
            IsKeyboardDetected = false;
        }
    }
}
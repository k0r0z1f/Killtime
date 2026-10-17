using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Audio;

namespace Killtime.Audio.Experimental
{
    /// <summary>SystemAudioChameleon — Analyse entrante — AGC/freeze/pitch-routing/harmoniques (partial, move-only).</summary>
    public partial class SystemAudioChameleon : MonoBehaviour
    {
        private void AnalyzeIncomingAudio()
        {
            if (_micClip == null || !Microphone.IsRecording(_activeDeviceName)) return;

            int micPos = Microphone.GetPosition(_activeDeviceName);
            if (micPos < 0) return;

            int totalSamples = _micClip.samples;
            int readStart = micPos - BufferSize;
            if (readStart < 0) readStart += totalSamples;

            if (readStart + BufferSize <= totalSamples)
            {
                _micClip.GetData(_analysisBuffer, readStart);
            }
            else
            {
                int firstChunk = totalSamples - readStart;
                float[] temp1 = new float[firstChunk];
                _micClip.GetData(temp1, readStart);
                Array.Copy(temp1, 0, _analysisBuffer, 0, firstChunk);

                int secondChunk = BufferSize - firstChunk;
                float[] temp2 = new float[secondChunk];
                _micClip.GetData(temp2, 0);
                Array.Copy(temp2, 0, _analysisBuffer, firstChunk, secondChunk);
            }

            // Étage 1 : Mesure Brute Pré-Gain & Boucle AGC
            float rawPeak = 0f;
            float rawSum = 0f;
            for (int i = 0; i < BufferSize; i++)
            {
                float absVal = Mathf.Abs(_analysisBuffer[i]);
                if (absVal > rawPeak) rawPeak = absVal;
                rawSum += _analysisBuffer[i] * _analysisBuffer[i];
            }
            float unamplifiedRMS = Mathf.Sqrt(rawSum / BufferSize);

            // AGC (Contrôle Automatique de Gain)
            if (_autoCalibrateClarity && unamplifiedRMS > 0.001f)
            {
                float targetGain = 0.22f / Mathf.Max(0.005f, unamplifiedRMS);
                targetGain = Mathf.Clamp(targetGain, 0.6f, 5.5f);

                if (rawPeak * _inputPreGain > 0.92f)
                {
                    _inputPreGain = Mathf.MoveTowards(_inputPreGain, 0.88f / Mathf.Max(0.01f, rawPeak), Time.unscaledDeltaTime * 6.0f);
                }
                else
                {
                    _inputPreGain = Mathf.MoveTowards(_inputPreGain, targetGain, Time.unscaledDeltaTime * 0.9f);
                }
            }

            // Application du Gain Normalisé
            for (int i = 0; i < BufferSize; i++)
            {
                _analysisBuffer[i] *= _inputPreGain;
            }
            RawInputRMS = unamplifiedRMS * _inputPreGain;

            AudioListener.GetOutputData(_unityOutputBuffer, 0);
            float unitySum = 0f;
            for (int i = 0; i < BufferSize; i++) unitySum += _unityOutputBuffer[i] * _unityOutputBuffer[i];
            UnityOutputRMS = Mathf.Sqrt(unitySum / BufferSize);

            if (UnityOutputRMS > 0.08f)
            {
                _freezeDetectionTimer = 0.18f;
            }
            if (_freezeDetectionTimer > 0f)
            {
                _freezeDetectionTimer -= Time.unscaledDeltaTime;
            }

            Array.Copy(_analysisBuffer, _cleanedBuffer, BufferSize);

            float sumCleaned = 0f;
            for (int i = 0; i < BufferSize; i++) sumCleaned += _cleanedBuffer[i] * _cleanedBuffer[i];
            DetectedEnergyRMS = Mathf.Sqrt(sumCleaned / BufferSize);

            ExtractSpectralBands(_cleanedBuffer);

            float tonalEnergy = BassEnergyRMS + MidEnergyRMS;
            float inharmonicEnergy = HighEnergyRMS * 1.85f;
            float rawPurity = tonalEnergy / (tonalEnergy + inharmonicEnergy + 0.0001f);
            _harmonicPuritySmoothed = Mathf.Lerp(_harmonicPuritySmoothed, rawPurity, Time.unscaledDeltaTime * 6.5f);
            HarmonicPurity = _harmonicPuritySmoothed;

            // Calibrage Adaptatif du Seuil de Silence
            if (_autoCalibrateClarity)
            {
                if (RawInputRMS < _adaptiveNoiseFloor * 1.5f || _adaptiveNoiseFloor < 0.001f)
                {
                    _adaptiveNoiseFloor = Mathf.MoveTowards(_adaptiveNoiseFloor, RawInputRMS, Time.unscaledDeltaTime * 0.5f);
                }
                else
                {
                    _adaptiveNoiseFloor = Mathf.MoveTowards(_adaptiveNoiseFloor, RawInputRMS * 0.2f, Time.unscaledDeltaTime * 0.05f);
                }
                _silenceThreshold = Mathf.Clamp(_adaptiveNoiseFloor * 1.6f + 0.006f, 0.006f, 0.035f);
            }

            if (DetectedEnergyRMS < _silenceThreshold)
            {
                IsExternalMusicDetected = false;
                IsHarmonicNoteStable = false;
                DetectedNoteName = "---";
                PitchConfidence = 0f;
                PhaseLockConfidence = Mathf.MoveTowards(PhaseLockConfidence, 0f, Time.unscaledDeltaTime * 0.5f);
                return;
            }

            IsExternalMusicDetected = true;

            DetectPercussiveElements();

            // Analyse Pitch Décimée x4 (12 kHz) avec Filtrage en Boucle Fermée & Écrêtage de Sondhi
            if (_freezeDetectionTimer <= 0f)
            {
                if (_autoCalibrateClarity)
                {
                    float targetCutoff;
                    if (IsHarmonicNoteStable && _targetSynthFreq >= 35f)
                    {
                        float harmonicSuppressionSpan = Mathf.Lerp(1.75f, 2.20f, 1f - _harmonicPuritySmoothed);
                        targetCutoff = Mathf.Clamp(_targetSynthFreq * harmonicSuppressionSpan, 160f, 720f);
                    }
                    else
                    {
                        float energyWeightedGuide = (BassEnergyRMS * 200f + MidEnergyRMS * 520f) / Mathf.Max(0.001f, tonalEnergy);
                        targetCutoff = Mathf.Clamp(energyWeightedGuide + 100f, 220f, 680f);
                    }

                    _adaptiveCenterClip = Mathf.Lerp(0.14f, 0.26f, 1f - _harmonicPuritySmoothed);

                    bool isPercussiveTransient = (KickPulse > 0.25f || SnarePulse > 0.25f);
                    float filterAgility = isPercussiveTransient ? 1.8f : 6.5f;
                    _calibratedPitchCutoff = Mathf.Lerp(_calibratedPitchCutoff, targetCutoff, Time.unscaledDeltaTime * filterAgility);
                }
                else
                {
                    _calibratedPitchCutoff = 420f;
                    _adaptiveCenterClip = 0f;
                }

                PrepareDecimatedPitchBuffer(_calibratedPitchCutoff, _adaptiveCenterClip);
                int effectivePitchSampleRate = _sampleRate / DecimationFactor;

                var (foundHz, confidence) = ComputeNSDFPitch(effectivePitchSampleRate);
                PitchConfidence = confidence;

                bool wasStable = IsHarmonicNoteStable;
                float enterThreshold = 0.32f;
                float exitThreshold = 0.22f;
                bool isNowStable = wasStable ? (confidence >= exitThreshold) : (confidence >= enterThreshold);

                // Analyse dédiée au registre grave (24–175 Hz) isolée des harmoniques supérieures
                PrepareBassBuffer();
                int effectiveBassSampleRate = _sampleRate / BassDecimation;
                var (bassHz, bassConf) = ComputeBassPitch(effectiveBassSampleRate);
                BassPitchConfidence = bassConf;

                if (bassConf >= 0.26f && bassHz >= 24f && bassHz <= 180f)
                {
                    DetectedBassFrequencyHz = bassHz;
                    float finalBassTarget = bassHz;
                    if (_bassOctave == -1) finalBassTarget *= 0.5f;
                    else if (_bassOctave == 1) finalBassTarget *= 2f;
                    _targetBassFreq = Mathf.Clamp(finalBassTarget, 24f, 260f);

                    float bassMidi = 69f + 12f * Mathf.Log(bassHz / 440f, 2f);
                    int nearestBassMidi = Mathf.RoundToInt(bassMidi);
                    if (nearestBassMidi != _lastMidiNoteTracked)
                    {
                        _lastMidiNoteTracked = nearestBassMidi;
                        _pendingBassTrigger = 1.0f;
                    }
                }

                if (isNowStable && foundHz >= 35f && foundHz <= 720f)
                {
                    IsHarmonicNoteStable = true;
                    DetectedFrequencyHz = foundHz;
                    _targetSynthFreq = foundHz;

                    if (bassConf < 0.26f)
                    {
                        float bassCalculated = foundHz;
                        while (bassCalculated > 130f) bassCalculated *= 0.5f;
                        while (bassCalculated < 32f) bassCalculated *= 2f;

                        if (_bassOctave == -1) bassCalculated *= 0.5f;
                        else if (_bassOctave == 1) bassCalculated *= 2f;

                        _targetBassFreq = Mathf.Clamp(bassCalculated, 24f, 260f);
                    }

                    float choirCalculated = foundHz;
                    while (choirCalculated < 130f) choirCalculated *= 2f;
                    while (choirCalculated > 520f) choirCalculated *= 0.5f;

                    if (_choirOctave == -1) choirCalculated *= 0.5f;
                    else if (_choirOctave == 1) choirCalculated *= 2f;
                    else if (_choirOctave == 2) choirCalculated *= 4f;

                    _targetChoirFreq = Mathf.Clamp(choirCalculated, 65f, 1600f);

                    float vocalCalculated = foundHz;
                    while (vocalCalculated < 165f) vocalCalculated *= 2f;
                    while (vocalCalculated > 660f) vocalCalculated *= 0.5f;

                    if (_vocalOctave == -1) vocalCalculated *= 0.5f;
                    else if (_vocalOctave == 1) vocalCalculated *= 2f;
                    else if (_vocalOctave == 2) vocalCalculated *= 4f;

                    _targetVocalFreq = Mathf.Clamp(vocalCalculated, 80f, 1800f);

                    float midiVal = 69f + 12f * Mathf.Log(foundHz / 440f, 2f);
                    int nearestMidi = Mathf.RoundToInt(midiVal);
                    DetectedCentsOffset = Mathf.RoundToInt((midiVal - nearestMidi) * 100f);

                    int noteIndex = (nearestMidi % 12 + 12) % 12;
                    int octave = (nearestMidi / 12) - 1;
                    string sign = DetectedCentsOffset >= 0 ? "+" : "";
                    DetectedNoteName = $"{NoteNames[noteIndex]}{octave} ({sign}{DetectedCentsOffset}¢)";
                }
                else
                {
                    IsHarmonicNoteStable = false;
                    if (confidence < 0.20f)
                    {
                        DetectedNoteName = "---";
                    }
                }
            }
        }

        private void UpdateHarmonicParameters()
        {
            float dt = Time.unscaledDeltaTime;
            BeatPulse = Mathf.MoveTowards(BeatPulse, 0f, dt * 4.2f);
            KickPulse = Mathf.MoveTowards(KickPulse, 0f, dt * 5.5f);
            SnarePulse = Mathf.MoveTowards(SnarePulse, 0f, dt * 5.0f);
            HatPulse = Mathf.MoveTowards(HatPulse, 0f, dt * 8.0f);
        }
    }
}

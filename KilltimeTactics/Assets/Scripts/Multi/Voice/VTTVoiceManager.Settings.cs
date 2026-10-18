using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.UI;

namespace Killtime.Multi.Voice
{
    public partial class VTTVoiceManager : MonoBehaviour
    {
        // ---------- Méthodes de Configuration Publiques ----------

        public void SetCodec(VoiceCodecType codec)
        {
            if (_codecType == codec) return;
            _codecType = codec;
            InitializeDspAndCodecs();
            SavePreferences();
            if (_isRecording) StartCapture();
        }

        public void SetActivationMode(VoiceActivationMode mode)
        {
            _activationMode = mode;
            SavePreferences();
        }

        public void SetPttKey(KeyCode key)
        {
            _pttKey = key;
            SavePreferences();
        }

        public void SetInputGain(float gain)
        {
            if (_dsp != null) _dsp.InputGain = gain;
            SavePreferences();
        }

        public void SetGateThreshold(float threshold)
        {
            if (_dsp != null) _dsp.GateThreshold = threshold;
            SavePreferences();
        }

        public void SetNoiseGateEnabled(bool enabled)
        {
            if (_dsp != null) _dsp.NoiseGateEnabled = enabled;
            SavePreferences();
        }

        public void SetNoiseReductionEnabled(bool enabled)
        {
            if (_dsp != null) _dsp.NoiseReductionEnabled = enabled;
            SavePreferences();
        }

        public void SetHighPassFilterEnabled(bool enabled)
        {
            if (_dsp != null) _dsp.HighPassFilterEnabled = enabled;
            SavePreferences();
        }

        public void SetMasterOutputVolume(float vol)
        {
            _masterOutputVolume = Mathf.Clamp(vol, 0f, 2f);
            SavePreferences();
        }

        public bool AutoGainEnabled => _dsp != null && _dsp.AutoGainEnabled;

        public void SetAutoGainEnabled(bool enabled)
        {
            if (_dsp != null) _dsp.AutoGainEnabled = enabled;
            SavePreferences();
        }

        public void SetOutputLimiterEnabled(bool enabled)
        {
            _outputLimiterEnabled = enabled;
            SavePreferences();
        }

        public void SetOutputCeiling(float ceiling)
        {
            _outputCeiling = Mathf.Clamp(ceiling, 0.15f, 1.0f);
            SavePreferences();
        }

        public void SetGateDeleterEnabled(bool enabled)
        {
            _gateDeleterEnabled = enabled;
            SavePreferences();
        }

        public void SetRadioCommsEffectEnabled(bool enabled)
        {
            _radioCommsEffectEnabled = enabled;
            if (_dsp != null) _dsp.AntiKeyboardFilterEnabled = _antiKeyboardFilterEnabled || _radioCommsEffectEnabled;
            SavePreferences();
        }

        public void SetAntiKeyboardFilterEnabled(bool enabled)
        {
            _antiKeyboardFilterEnabled = enabled;
            if (_dsp != null) _dsp.AntiKeyboardFilterEnabled = _antiKeyboardFilterEnabled || _radioCommsEffectEnabled;
            SavePreferences();
        }

        public void SetVoiceClarityEnabled(bool enabled)
        {
            _voiceClarityEnabled = enabled;
            SavePreferences();
        }

        public void SetClarityBoostAmount(float amount)
        {
            _clarityBoostAmount = Mathf.Clamp(amount, 0f, 1.5f);
            SavePreferences();
        }

        public void SetVoiceChangerEnabled(bool enabled)
        {
            _voiceChangerEnabled = enabled;
            if (_voiceChanger != null) _voiceChanger.Enabled = enabled;
            SavePreferences();
        }

        public void SetVoiceChangerPreset(VoiceChangerPreset preset)
        {
            _voiceChangerPreset = preset;
            if (_voiceChanger != null)
            {
                _voiceChanger.ApplyPreset(preset);
                _voiceChangerEnabled = _voiceChanger.Enabled;
            }
            SavePreferences();
        }

        public void SetVoiceChangerPitch(float semitones)
        {
            if (_voiceChanger != null) _voiceChanger.PitchSemitones = Mathf.Clamp(semitones, -12f, 12f);
            SavePreferences();
        }

        public void SetVoiceChangerFormant(float formant)
        {
            if (_voiceChanger != null) _voiceChanger.FormantShift = Mathf.Clamp(formant, -1f, 1f);
            SavePreferences();
        }

        public void SetVoiceChangerDrive(float drive)
        {
            if (_voiceChanger != null) _voiceChanger.HarmonicDrive = Mathf.Clamp01(drive);
            SavePreferences();
        }

        public void SetVoiceChangerRobotic(float robotic)
        {
            if (_voiceChanger != null) _voiceChanger.RoboticModulation = Mathf.Clamp01(robotic);
            SavePreferences();
        }

        public void SetVoiceChangerMix(float mix)
        {
            if (_voiceChanger != null) _voiceChanger.WetDryMix = Mathf.Clamp01(mix);
            SavePreferences();
        }

        private void ApplyRecordingEffects(float[] buffer, int count, bool isSpeaking, float dt)
        {
            if (buffer == null || count <= 0) return;

            // 1. Égaliseur de Présence (exécuté en premier sur le signal audio pur)
            if (_voiceClarityEnabled && !_radioCommsEffectEnabled)
            {
                float aHp = Mathf.Exp(-2f * Mathf.PI * 2600f / _sampleRate);
                float aBp = 1f - Mathf.Exp(-2f * Mathf.PI * 5400f / _sampleRate);
                float gain = _clarityBoostAmount * 0.35f;

                for (int i = 0; i < count; i++)
                {
                    float s = buffer[i];
                    _clarityHpState = aHp * _clarityHpState + (1f - aHp) * s;
                    float presence = s - _clarityHpState;
                    _clarityBpState += aBp * (presence - _clarityBpState);
                    buffer[i] = s + (_clarityBpState * gain);
                }
            }

            // 2. Filtre Radio Comms Tactique
            if (_radioCommsEffectEnabled)
            {
                float rHp = Mathf.Exp(-2f * Mathf.PI * 300f / _sampleRate);
                float aLp = 1f - Mathf.Exp(-2f * Mathf.PI * 3400f / _sampleRate);

                for (int i = 0; i < count; i++)
                {
                    float s = buffer[i];
                    float hp = s - _radioPrevIn + rHp * _radioHpState;
                    _radioPrevIn = s;
                    _radioHpState = hp;
                    _radioLpState += aLp * (hp - _radioLpState);
                    buffer[i] = (float)Math.Tanh(_radioLpState * 1.35) * 0.92f;
                }
            }

            // 2.b Modulateur Vocal Avancé (Pitch Shift, Formants, Saturation, Ring Mod)
            if (_voiceChangerEnabled && _voiceChanger != null)
            {
                _voiceChanger.Process(buffer, count);
            }

            // 3. Noise Gate Deleter (exécuté en dernier pour couper tout résidu de filtre ou souffle)
            if (_gateDeleterEnabled)
            {
                float targetGain = isSpeaking ? 1.0f : 0.0f;
                float attackStep = 1f - Mathf.Exp(-1f / (_sampleRate * 0.015f));
                float releaseStep = 1f - Mathf.Exp(-1f / (_sampleRate * 0.120f));

                for (int i = 0; i < count; i++)
                {
                    float rate = (targetGain > _gateGain) ? attackStep : releaseStep;
                    _gateGain += rate * (targetGain - _gateGain);
                    buffer[i] *= _gateGain;
                }
            }
        }

        public void ApplyOutputLimiter(float[] buffer)
        {
            if (buffer != null) ApplyLimiterBuffer(buffer, buffer.Length);
        }

        public void ApplyLimiterBuffer(float[] buffer, int count)
        {
            if (!_outputLimiterEnabled || buffer == null || count <= 0) return;
            float ceiling = _outputCeiling;
            if (ceiling >= 0.99f) return;

            float maxPeak = 0f;
            for (int i = 0; i < count; i++)
            {
                float a = Mathf.Abs(buffer[i]);
                if (a > maxPeak) maxPeak = a;
            }

            float targetGain = (maxPeak > ceiling) ? (ceiling / maxPeak) : 1.0f;
            float attackCoeff = 1f - Mathf.Exp(-1f / (_sampleRate * 0.005f));
            float releaseCoeff = 1f - Mathf.Exp(-1f / (_sampleRate * 0.075f));

            float kneeThreshold = ceiling * 0.88f;
            float kneeSpan = Mathf.Max(0.001f, ceiling - kneeThreshold);

            for (int i = 0; i < count; i++)
            {
                float coeff = (targetGain < _smoothedLimiterGain) ? attackCoeff : releaseCoeff;
                _smoothedLimiterGain += coeff * (targetGain - _smoothedLimiterGain);

                float s = buffer[i] * _smoothedLimiterGain;
                float abs = Mathf.Abs(s);

                if (abs > kneeThreshold)
                {
                    float excess = abs - kneeThreshold;
                    float compressed = kneeThreshold + kneeSpan * (float)Math.Tanh(excess / kneeSpan);
                    s = Mathf.Sign(s) * Mathf.Min(compressed, ceiling);
                }

                buffer[i] = s;
            }

            CurrentOutputPeak = Mathf.Min(maxPeak * _smoothedLimiterGain, ceiling);
        }

        public void SetMuted(bool muted)
        {
            _isMuted = muted;
            SavePreferences();
        }

        public void SetDeafened(bool deafened)
        {
            _isDeafened = deafened;
            if (_isDeafened) _isMuted = true;
            SavePreferences();
        }

        public void SetLoopbackTest(bool active)
        {
            _isLoopbackTestActive = active;
        }

        // ---------- Persistance DevUIPreferences ----------

        private void LoadPreferences()
        {
            var p = DevUIPreferences.Current;
            if (p == null) return;

            if (!string.IsNullOrEmpty(p.VoiceInputDevice)) _selectedDevice = p.VoiceInputDevice;
            _masterOutputVolume = p.VoiceOutputVolume;
            _outputLimiterEnabled = p.VoiceOutputLimiterEnabled;
            _outputCeiling = p.VoiceOutputCeiling > 0.05f ? p.VoiceOutputCeiling : 0.50f;
            _gateDeleterEnabled = p.VoiceGateDeleterEnabled;
            _radioCommsEffectEnabled = p.VoiceRadioCommsEffectEnabled;
            _antiKeyboardFilterEnabled = p.VoiceAntiKeyboardFilter;
            _voiceClarityEnabled = p.VoiceClarityEnabled;
            _clarityBoostAmount = p.VoiceClarityAmount > 0.01f ? p.VoiceClarityAmount : 0.65f;
            if (_dsp != null)
            {
                _dsp.AutoGainEnabled = p.VoiceAgcEnabled;
                _dsp.InputGain = p.VoiceInputGain;
                _dsp.GateThreshold = p.VoiceVadThreshold;
                _dsp.NoiseGateEnabled = p.VoiceNoiseGateEnabled;
                _dsp.NoiseReductionEnabled = p.VoiceNoiseReductionEnabled;
                _dsp.HighPassFilterEnabled = p.VoiceHighPassFilter;
            }
            _activationMode = (VoiceActivationMode)Mathf.Clamp(p.VoiceActivationMode, 0, 2);
            _vadHangoverSec = Mathf.Max(0.1f, p.VoiceVadHangoverMs / 1000f);
            _pttKey = (KeyCode)p.VoicePttKey;
            _codecType = (VoiceCodecType)Mathf.Clamp(p.VoiceCodec, 0, 4);
            _isMuted = p.VoiceMuted;
            _isDeafened = p.VoiceDeafened;

            _voiceChangerEnabled = p.VoiceChangerEnabled;
            int maxPreset = Enum.GetValues(typeof(VoiceChangerPreset)).Length - 1;
            _voiceChangerPreset = (VoiceChangerPreset)Mathf.Clamp(p.VoiceChangerPreset, 0, maxPreset);
            if (_voiceChanger != null)
            {
                _voiceChanger.Enabled = _voiceChangerEnabled;
                _voiceChanger.Preset = _voiceChangerPreset;
                _voiceChanger.PitchSemitones = p.VoiceChangerPitch;
                _voiceChanger.FormantShift = p.VoiceChangerFormant;
                _voiceChanger.HarmonicDrive = p.VoiceChangerDrive;
                _voiceChanger.RoboticModulation = p.VoiceChangerRobotic;
                _voiceChanger.WetDryMix = p.VoiceChangerMix;
            }
        }

        private void SavePreferences()
        {
            var p = DevUIPreferences.Current;
            if (p == null) return;

            p.VoiceInputDevice = _selectedDevice ?? "";
            p.VoiceOutputVolume = _masterOutputVolume;
            p.VoiceOutputLimiterEnabled = _outputLimiterEnabled;
            p.VoiceOutputCeiling = _outputCeiling;
            p.VoiceGateDeleterEnabled = _gateDeleterEnabled;
            p.VoiceRadioCommsEffectEnabled = _radioCommsEffectEnabled;
            p.VoiceAntiKeyboardFilter = _antiKeyboardFilterEnabled;
            p.VoiceClarityEnabled = _voiceClarityEnabled;
            p.VoiceClarityAmount = _clarityBoostAmount;
            if (_dsp != null)
            {
                p.VoiceAgcEnabled = _dsp.AutoGainEnabled;
                p.VoiceInputGain = _dsp.InputGain;
                p.VoiceVadThreshold = _dsp.GateThreshold;
                p.VoiceNoiseGateEnabled = _dsp.NoiseGateEnabled;
                p.VoiceNoiseReductionEnabled = _dsp.NoiseReductionEnabled;
                p.VoiceHighPassFilter = _dsp.HighPassFilterEnabled;
            }
            p.VoiceActivationMode = (int)_activationMode;
            p.VoiceVadHangoverMs = _vadHangoverSec * 1000f;
            p.VoicePttKey = (int)_pttKey;
            p.VoiceCodec = (int)_codecType;
            p.VoiceMuted = _isMuted;
            p.VoiceDeafened = _isDeafened;

            p.VoiceChangerEnabled = _voiceChangerEnabled;
            p.VoiceChangerPreset = (int)_voiceChangerPreset;
            if (_voiceChanger != null)
            {
                p.VoiceChangerPitch = _voiceChanger.PitchSemitones;
                p.VoiceChangerFormant = _voiceChanger.FormantShift;
                p.VoiceChangerDrive = _voiceChanger.HarmonicDrive;
                p.VoiceChangerRobotic = _voiceChanger.RoboticModulation;
                p.VoiceChangerMix = _voiceChanger.WetDryMix;
            }

            DevUIPreferences.MarkDirty();
        }
    }
}

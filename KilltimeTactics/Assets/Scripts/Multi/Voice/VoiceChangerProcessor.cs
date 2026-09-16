using System;
using UnityEngine;

namespace Killtime.Multi.Voice
{
    public enum VoiceChangerPreset
    {
        Off = 0,
        TacticalHeavy = 1,
        CyborgDroid = 2,
        GhostInfiltrator = 3,
        NythariteResonance = 4,
        RadioTactical = 5,
        ValkyrieOperator = 6,
        InfiltratorFemale = 7,
        NytharitePriestess = 8,
        CyberAndroidFemale = 9,
        Custom = 10
    }

    public class VoiceChangerProcessor
    {
        public bool Enabled { get; set; } = false;
        public VoiceChangerPreset Preset { get; set; } = VoiceChangerPreset.Off;

        public float PitchSemitones
        {
            get => _pitchSemitones;
            set => _pitchSemitones = Mathf.Clamp(value, -12f, 12f);
        }

        public float FormantShift
        {
            get => _formantShift;
            set => _formantShift = Mathf.Clamp(value, -1f, 1f);
        }

        public float HarmonicDrive
        {
            get => _harmonicDrive;
            set => _harmonicDrive = Mathf.Clamp01(value);
        }

        public float RoboticModulation
        {
            get => _roboticModulation;
            set => _roboticModulation = Mathf.Clamp01(value);
        }

        public float WetDryMix
        {
            get => _wetDryMix;
            set => _wetDryMix = Mathf.Clamp01(value);
        }

        private float _pitchSemitones = 0f;
        private float _formantShift = 0f;
        private float _harmonicDrive = 0f;
        private float _roboticModulation = 0f;
        private float _wetDryMix = 1f;

        private int _sampleRate;
        private const int DelayBufferSize = 4096;
        private const int BufferMask = DelayBufferSize - 1;
        private readonly float[] _delayBuffer = new float[DelayBufferSize];
        private int _writeIndex = 0;

        private float _grainPhase = 0f;
        private double _robotPhase = 0.0;

        private float _formantLpState = 0f;
        private float _formantBpState = 0f;
        private float _chestLpState = 0f;

        public VoiceChangerProcessor(int sampleRate = 16000)
        {
            _sampleRate = Mathf.Max(8000, sampleRate);
        }

        public void SetSampleRate(int sampleRate)
        {
            _sampleRate = Mathf.Max(8000, sampleRate);
            Reset();
        }

        public void ApplyPreset(VoiceChangerPreset preset)
        {
            Preset = preset;

            switch (preset)
            {
                case VoiceChangerPreset.Off:
                    Enabled = false;
                    _pitchSemitones = 0f;
                    _formantShift = 0f;
                    _harmonicDrive = 0f;
                    _roboticModulation = 0f;
                    _wetDryMix = 1f;
                    break;

                case VoiceChangerPreset.TacticalHeavy:
                    Enabled = true;
                    _pitchSemitones = -3.5f;
                    _formantShift = -0.55f;
                    _harmonicDrive = 0.30f;
                    _roboticModulation = 0.0f;
                    _wetDryMix = 1f;
                    break;

                case VoiceChangerPreset.CyborgDroid:
                    Enabled = true;
                    _pitchSemitones = -2.0f;
                    _formantShift = -0.20f;
                    _harmonicDrive = 0.25f;
                    _roboticModulation = 0.45f;
                    _wetDryMix = 0.95f;
                    break;

                case VoiceChangerPreset.GhostInfiltrator:
                    Enabled = true;
                    _pitchSemitones = 3.5f;
                    _formantShift = 0.35f;
                    _harmonicDrive = 0.10f;
                    _roboticModulation = 0.0f;
                    _wetDryMix = 1f;
                    break;

                case VoiceChangerPreset.NythariteResonance:
                    Enabled = true;
                    _pitchSemitones = -1.5f;
                    _formantShift = 0.15f;
                    _harmonicDrive = 0.40f;
                    _roboticModulation = 0.15f;
                    _wetDryMix = 0.90f;
                    break;

                case VoiceChangerPreset.RadioTactical:
                    Enabled = true;
                    _pitchSemitones = 0f;
                    _formantShift = 0.10f;
                    _harmonicDrive = 0.45f;
                    _roboticModulation = 0.05f;
                    _wetDryMix = 1f;
                    break;

                case VoiceChangerPreset.ValkyrieOperator:
                    Enabled = true;
                    _pitchSemitones = 3.8f;
                    _formantShift = 0.48f;
                    _harmonicDrive = 0.12f;
                    _roboticModulation = 0.0f;
                    _wetDryMix = 1f;
                    break;

                case VoiceChangerPreset.InfiltratorFemale:
                    Enabled = true;
                    _pitchSemitones = 5.2f;
                    _formantShift = 0.65f;
                    _harmonicDrive = 0.10f;
                    _roboticModulation = 0.0f;
                    _wetDryMix = 1f;
                    break;

                case VoiceChangerPreset.NytharitePriestess:
                    Enabled = true;
                    _pitchSemitones = 3.0f;
                    _formantShift = 0.40f;
                    _harmonicDrive = 0.22f;
                    _roboticModulation = 0.08f;
                    _wetDryMix = 0.95f;
                    break;

                case VoiceChangerPreset.CyberAndroidFemale:
                    Enabled = true;
                    _pitchSemitones = 4.2f;
                    _formantShift = 0.52f;
                    _harmonicDrive = 0.18f;
                    _roboticModulation = 0.35f;
                    _wetDryMix = 1f;
                    break;

                case VoiceChangerPreset.Custom:
                    Enabled = true;
                    break;
            }
        }

        public void Process(float[] buffer, int count)
        {
            if (!Enabled || buffer == null || count <= 0) return;

            float pitchRatio = Mathf.Pow(2f, _pitchSemitones / 12f);
            float grainWindowSamples = Mathf.Clamp(0.035f * _sampleRate, 128f, 1024f);
            float phaseDelta = (1f - pitchRatio) / grainWindowSamples;
            float baseDelay = grainWindowSamples;

            float fFormant = 2f * Mathf.Sin(Mathf.PI * Mathf.Clamp(2800f / _sampleRate, 0.005f, 0.45f));
            float qFormant = 0.35f;
            float aChest = 1f - Mathf.Exp(-2f * Mathf.PI * 220f / _sampleRate);

            for (int i = 0; i < count; i++)
            {
                float dry = buffer[i];

                _delayBuffer[_writeIndex] = dry;

                float wet;
                if (Mathf.Abs(_pitchSemitones) < 0.01f)
                {
                    wet = dry;
                }
                else
                {
                    _grainPhase += phaseDelta;
                    if (_grainPhase >= 1f) _grainPhase -= 1f;
                    else if (_grainPhase < 0f) _grainPhase += 1f;

                    float p1 = _grainPhase;
                    float p2 = p1 + 0.5f;
                    if (p2 >= 1f) p2 -= 1f;

                    float w1 = 1f - 2f * Mathf.Abs(p1 - 0.5f);
                    float w2 = 1f - 2f * Mathf.Abs(p2 - 0.5f);

                    float delay1 = baseDelay + p1 * grainWindowSamples;
                    float delay2 = baseDelay + p2 * grainWindowSamples;

                    float s1 = ReadDelay(delay1);
                    float s2 = ReadDelay(delay2);

                    wet = s1 * w1 + s2 * w2;
                }

                _writeIndex = (_writeIndex + 1) & BufferMask;

                if (Mathf.Abs(_formantShift) > 0.01f)
                {
                    if (_formantShift > 0f)
                    {
                        _chestLpState += aChest * (wet - _chestLpState);
                        float deChested = wet - (_chestLpState * (_formantShift * 0.55f));

                        _formantLpState += fFormant * _formantBpState;
                        float hp = deChested - _formantLpState - qFormant * _formantBpState;
                        _formantBpState += fFormant * hp;

                        wet = deChested + _formantBpState * (_formantShift * 0.60f);
                    }
                    else
                    {
                        float inv = -_formantShift;
                        _chestLpState += aChest * (wet - _chestLpState);
                        wet = wet + _chestLpState * (inv * 0.45f);

                        _formantLpState += fFormant * _formantBpState;
                        float hp = wet - _formantLpState - qFormant * _formantBpState;
                        _formantBpState += fFormant * hp;
                        wet = wet - _formantBpState * (inv * 0.30f);
                    }
                }

                if (_harmonicDrive > 0.001f)
                {
                    float driveScale = 1f + _harmonicDrive * 2.5f;
                    float driven = wet * driveScale;
                    float saturated = (float)Math.Tanh(driven);
                    float evenHarmonic = driven * Mathf.Abs(driven) * 0.12f * _harmonicDrive;
                    wet = (saturated + (float)Math.Tanh(evenHarmonic)) / (1f + _harmonicDrive * 0.45f);
                }

                if (_roboticModulation > 0.001f)
                {
                    _robotPhase += (2.0 * Math.PI * 185.0) / _sampleRate;
                    if (_robotPhase > Math.PI * 2.0) _robotPhase -= Math.PI * 2.0;

                    float carrier = (float)Math.Cos(_robotPhase);
                    float modulated = wet * carrier * 1.35f;
                    wet = Mathf.Lerp(wet, modulated, _roboticModulation);
                }

                float mixed = Mathf.Lerp(dry, wet, _wetDryMix);
                buffer[i] = Mathf.Clamp(mixed, -1.25f, 1.25f);
            }
        }

        private float ReadDelay(float delaySamples)
        {
            float readPos = _writeIndex - delaySamples;
            while (readPos < 0f) readPos += DelayBufferSize;
            while (readPos >= DelayBufferSize) readPos -= DelayBufferSize;

            int i0 = (int)readPos;
            int i1 = (i0 + 1) & BufferMask;
            float frac = readPos - i0;

            return _delayBuffer[i0] + frac * (_delayBuffer[i1] - _delayBuffer[i0]);
        }

        public void Reset()
        {
            Array.Clear(_delayBuffer, 0, _delayBuffer.Length);
            _writeIndex = 0;
            _grainPhase = 0f;
            _robotPhase = 0.0;
            _formantLpState = 0f;
            _formantBpState = 0f;
            _chestLpState = 0f;
        }
    }
}
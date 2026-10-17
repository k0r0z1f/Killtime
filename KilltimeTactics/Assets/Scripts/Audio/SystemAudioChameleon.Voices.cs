using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Audio;

namespace Killtime.Audio.Experimental
{
    /// <summary>SystemAudioChameleon — Moteur audio — OnAudioFilterRead lead/choir/vocal/drums/mix (partial, move-only).</summary>
    public partial class SystemAudioChameleon : MonoBehaviour
    {
        private float NextFastNoise()
        {
            _noiseState = _noiseState * 1664525u + 1013904223u;
            return (_noiseState / 2147483648.0f) - 1.0f;
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (!_enableChameleonMode || !IsExternalMusicDetected || !_generateCyberneticOverlay)
            {
                Array.Clear(data, 0, data.Length);
                return;
            }

            if (_pendingKickVel > 0f)
            {
                _kickEnv = _pendingKickVel;
                _kickPitchEnv = 1.0f;
                _kickPhase = 0.0;
                _drumSidechainDuck = 1.0f;
                _pendingKickVel = 0f;
            }

            if (_pendingSnareToneVel > 0f)
            {
                _snareToneEnv = _pendingSnareToneVel;
                _snareNoiseEnv = _pendingSnareNoiseVel;
                _snareTonePhase1 = 0.0;
                _snareTonePhase2 = 0.0;
                _pendingSnareToneVel = 0f;
                _pendingSnareNoiseVel = 0f;
            }

            if (_pendingHatVel > 0f)
            {
                _hatEnv = _pendingHatVel;
                _pendingHatVel = 0f;
            }

            if (_pendingBassTrigger > 0f)
            {
                _bassFilterEnv = 1.0f;
                _bassPitchKickEnv = 1.0f;
                _bassAmpEnv = Mathf.Max(_bassAmpEnv, _pendingBassTrigger);
                _pendingBassTrigger = 0f;
            }

            if (_pendingChoirTrigger > 0f)
            {
                _choirAmpEnv = Mathf.Max(_choirAmpEnv, _pendingChoirTrigger);
                _pendingChoirTrigger = 0f;
            }

            if (_pendingVocalTrigger > 0f)
            {
                _vocalAmpEnv = Mathf.Max(_vocalAmpEnv, _pendingVocalTrigger);
                _vocalNoteTimer = 0f;
                _pendingVocalTrigger = 0f;
            }

            double sampleRate = AudioSettings.outputSampleRate;
            float dt = 1f / (float)sampleRate;

            float kickDecay = Mathf.Exp(-9.0f * dt);
            float kickPitchDecay = Mathf.Exp(-32.0f * dt);
            float snareToneDecay = Mathf.Exp(-16.0f * dt);
            float snareNoiseDecay = Mathf.Exp(-8.5f * dt);
            float hatDecay = Mathf.Exp(-38.0f * dt);
            float duckDecay = Mathf.Exp(-14.0f * dt);

            float bassFilterDecay = Mathf.Exp(-7.5f * dt);
            float bassAmpDecay = _bassRhythmicPluck ? Mathf.Exp(-4.8f * dt) : Mathf.Exp(-0.8f * dt);
            float bassPitchKickDecay = Mathf.Exp(-38.0f * dt);
            float choirAttack = Mathf.Clamp01(12.0f * dt);
            float choirRelease = Mathf.Exp(-3.5f * dt);
            float vocalAttack = Mathf.Clamp01(18.0f * dt);
            float vocalRelease = Mathf.Exp(-4.2f * dt);
            float vocalGlide = Mathf.Clamp01(_vocalPortamento * dt);

            float glidePerSample = Mathf.Clamp01(_portamentoSpeed * dt);

            float targetLeadFreq = _targetSynthFreq;
            while (targetLeadFreq > 140f) targetLeadFreq *= 0.5f;
            while (targetLeadFreq < 45f) targetLeadFreq *= 2f;

            float synthGain = Mathf.Clamp01(DetectedEnergyRMS * 3.8f) * _mimicVolume;
            if (!IsHarmonicNoteStable) synthGain *= 0.30f;

            float targetCutoff = Mathf.Clamp(SpectralCentroidHz * 0.8f + (BeatPulse * 1200f), 250f, 6500f);

            double beatsPerSec = (DetectedBPM / 60.0);
            double phaseIncPerSample = (beatsPerSec / 4.0) / sampleRate;
            double latencyCompCycles = (_latencyCompensationMs / 1000.0) * beatsPerSec / 4.0;

            for (int i = 0; i < data.Length; i += channels)
            {
                _pllClockPhase += phaseIncPerSample;
                if (_pllClockPhase >= 1.0) _pllClockPhase -= 1.0;

                _bassFilterEnv *= bassFilterDecay;
                _bassAmpEnv *= bassAmpDecay;
                _bassPitchKickEnv *= bassPitchKickDecay;

                if (_emulateDrums && _usePredictiveSync && PhaseLockConfidence >= 0.35f)
                {
                    double lookahead = _pllClockPhase + latencyCompCycles;
                    if (lookahead >= 1.0) lookahead -= 1.0;

                    int step16 = Mathf.Clamp((int)(lookahead * 16.0), 0, 15);
                    if (step16 != _lastFiredPredictiveStep)
                    {
                        _lastFiredPredictiveStep = step16;

                        if (_kickPatternMask[step16] > 0.35f)
                        {
                            _kickEnv = _kickPatternMask[step16] * 1.0f;
                            _kickPitchEnv = 1.0f;
                            _kickPhase = 0.0;
                            _drumSidechainDuck = 1.0f;
                            _bassFilterEnv = 1.0f;
                            _bassPitchKickEnv = 1.0f;
                            _bassAmpEnv = 1.0f;
                        }

                        if (_snarePatternMask[step16] > 0.35f)
                        {
                            _snareToneEnv = _snarePatternMask[step16] * 0.90f;
                            _snareNoiseEnv = _snarePatternMask[step16] * 1.10f;
                            _snareTonePhase1 = 0.0;
                            _snareTonePhase2 = 0.0;
                        }

                        if (_hatPatternMask[step16] > 0.25f)
                        {
                            _hatEnv = _hatPatternMask[step16] * 0.85f;
                        }
                    }
                }

                // 1. Emulateur Basse Dedie (voir Bass.cs, move-only).
                float bassSampleL;
                float bassSampleR;
                RenderBassStereo(sampleRate, glidePerSample, out bassSampleL, out bassSampleR);


                // 2. Synthétiseur Harmonique / Lead
                float synthSample = 0f;
                _currentSynthFreq += (targetLeadFreq - _currentSynthFreq) * glidePerSample;
                _synthFilterCutoff += (targetCutoff - _synthFilterCutoff) * (glidePerSample * 0.5f);

                double incLead = (2.0 * Math.PI * _currentSynthFreq) / sampleRate;
                _phaseLeadSub += incLead;
                _phaseHarm1 += incLead * 1.5;
                _phaseHarm2 += incLead * 2.0;

                if (_phaseLeadSub > Math.PI * 2) _phaseLeadSub -= Math.PI * 2;
                if (_phaseHarm1 > Math.PI * 2) _phaseHarm1 -= Math.PI * 2;
                if (_phaseHarm2 > Math.PI * 2) _phaseHarm2 -= Math.PI * 2;

                float subRaw = (float)Math.Sin(_phaseLeadSub);
                float subSaturated = (float)Math.Tanh(subRaw * 2.4) * 0.55f;
                float rawOsc = subSaturated;

                if (_synthMode != ChameleonSynthMode.SubBassOnly)
                {
                    float harm1 = (float)Math.Sin(_phaseHarm1) * 0.32f;
                    float sawHarm = (float)(2.0 * ((_phaseHarm2 / (Math.PI * 2)) % 1.0) - 1.0) * 0.18f;
                    rawOsc += harm1 + sawHarm;
                }

                if (_synthMode == ChameleonSynthMode.FullNytharite)
                {
                    float shimmer = (float)Math.Sin(_phaseLeadSub * 4.02) * (HighEnergyRMS * 1.5f) * 0.22f;
                    rawOsc += shimmer;
                }

                float fSv = 2f * Mathf.Sin(Mathf.PI * Mathf.Clamp(_synthFilterCutoff / (float)sampleRate, 0.005f, 0.45f));
                _leadFilterLp += fSv * _leadFilterBp;
                float highPassLead = rawOsc - _leadFilterLp - 0.82f * _leadFilterBp;
                _leadFilterBp += fSv * highPassLead;

                synthSample = _leadFilterLp * synthGain * (1.0f - (_drumSidechainDuck * 0.6f));

                // 3. Émulation Voix de Chorale (Choir Synth)
                float choirSample = 0f;
                if (_emulateChoir)
                {
                    _currentChoirFreq += (_targetChoirFreq - _currentChoirFreq) * (glidePerSample * 0.75f);

                    _choirVibratoPhase += (2.0 * Math.PI * _choirVibratoRate) / sampleRate;
                    if (_choirVibratoPhase > Math.PI * 2) _choirVibratoPhase -= Math.PI * 2;
                    float vibrato = (float)Math.Sin(_choirVibratoPhase) * 0.0055f;

                    float detuneOffset = _choirDetune * 0.003f;
                    double incC1 = (2.0 * Math.PI * (_currentChoirFreq * (1f + vibrato))) / sampleRate;
                    double incC2 = (2.0 * Math.PI * (_currentChoirFreq * (1f - detuneOffset + vibrato * 0.85f))) / sampleRate;
                    double incC3 = (2.0 * Math.PI * (_currentChoirFreq * (1f + detuneOffset + vibrato * 1.15f))) / sampleRate;
                    double incC5 = (2.0 * Math.PI * (_currentChoirFreq * 1.498307f * (1f + vibrato * 0.9f))) / sampleRate;

                    _choirPhase1 += incC1;
                    if (_choirPhase1 > Math.PI * 2) _choirPhase1 -= Math.PI * 2;
                    _choirPhase2 += incC2;
                    if (_choirPhase2 > Math.PI * 2) _choirPhase2 -= Math.PI * 2;
                    _choirPhase3 += incC3;
                    if (_choirPhase3 > Math.PI * 2) _choirPhase3 -= Math.PI * 2;
                    _choirPhaseFifth += incC5;
                    if (_choirPhaseFifth > Math.PI * 2) _choirPhaseFifth -= Math.PI * 2;

                    float voice1 = (float)(Math.Sin(_choirPhase1) + 0.45 * Math.Sin(_choirPhase1 * 2.0) + 0.22 * Math.Sin(_choirPhase1 * 3.0) + 0.10 * Math.Sin(_choirPhase1 * 4.0));
                    float voice2 = (float)(Math.Sin(_choirPhase2) + 0.40 * Math.Sin(_choirPhase2 * 2.0) + 0.18 * Math.Sin(_choirPhase2 * 3.0));
                    float voice3 = (float)(Math.Sin(_choirPhase3) + 0.40 * Math.Sin(_choirPhase3 * 2.0) + 0.18 * Math.Sin(_choirPhase3 * 3.0));
                    float rawChoir = (voice1 * 0.42f + voice2 * 0.29f + voice3 * 0.29f);

                    if (_choirStyle == ChameleonChoirStyle.ArcanotechHymn)
                    {
                        float voiceFifth = (float)(Math.Sin(_choirPhaseFifth) + 0.35 * Math.Sin(_choirPhaseFifth * 2.0));
                        rawChoir = (rawChoir * 0.72f) + (voiceFifth * 0.38f);
                    }

                    float f1Target = 750f;
                    float f2Target = 1250f;
                    float q1 = 0.24f;
                    float q2 = 0.20f;

                    if (_choirStyle == ChameleonChoirStyle.CyberOoh)
                    {
                        f1Target = 380f;
                        f2Target = 820f;
                        q1 = 0.20f;
                        q2 = 0.16f;
                    }
                    else if (_choirStyle == ChameleonChoirStyle.ArcanotechHymn)
                    {
                        f1Target = 620f;
                        f2Target = 1750f;
                        q1 = 0.22f;
                        q2 = 0.18f;
                    }

                    float fSv1 = 2f * Mathf.Sin(Mathf.PI * Mathf.Clamp(f1Target / (float)sampleRate, 0.005f, 0.45f));
                    _choirF1_Lp += fSv1 * _choirF1_Bp;
                    float hpF1 = rawChoir - _choirF1_Lp - q1 * _choirF1_Bp;
                    _choirF1_Bp += fSv1 * hpF1;

                    float fSv2 = 2f * Mathf.Sin(Mathf.PI * Mathf.Clamp(f2Target / (float)sampleRate, 0.005f, 0.45f));
                    _choirF2_Lp += fSv2 * _choirF2_Bp;
                    float hpF2 = rawChoir - _choirF2_Lp - q2 * _choirF2_Bp;
                    _choirF2_Bp += fSv2 * hpF2;

                    float vocalResonance = (_choirF1_Bp * 0.65f + _choirF2_Bp * 0.55f);

                    if (_choirShimmer > 0.01f)
                    {
                        float rawN = NextFastNoise();
                        _choirBreathNoiseState += 0.42f * (rawN - _choirBreathNoiseState);
                        float breath = (rawN - _choirBreathNoiseState) * _choirShimmer * 0.12f;
                        vocalResonance += breath;
                    }

                    float targetChoirAmp = IsHarmonicNoteStable
                        ? Mathf.Clamp01(DetectedEnergyRMS * 3.6f)
                        : (DetectedEnergyRMS > _silenceThreshold ? 0.25f : 0f);

                    if (targetChoirAmp > _choirAmpEnv)
                        _choirAmpEnv += (targetChoirAmp - _choirAmpEnv) * choirAttack;
                    else
                        _choirAmpEnv *= choirRelease;

                    float choirDuck = 1.0f - (_drumSidechainDuck * 0.42f);
                    choirSample = (float)Math.Tanh(vocalResonance * 1.4) * _choirAmpEnv * _choirMasterVolume * choirDuck;
                }

                // 3.c Émulateur Voix Chantée Solo (Singing Voice Synth)
                float vocalSample = 0f;
                if (_emulateVocal)
                {
                    _currentVocalFreq += (_targetVocalFreq - _currentVocalFreq) * vocalGlide;
                    _vocalNoteTimer += dt;

                    float vibratoDelayRamp = Mathf.Clamp01((_vocalNoteTimer - 0.18f) / 0.28f);
                    _vocalVibratoPhase += (2.0 * Math.PI * _vocalVibratoRate) / sampleRate;
                    if (_vocalVibratoPhase > Math.PI * 2) _vocalVibratoPhase -= Math.PI * 2;
                    float vocalPitchMod = (float)Math.Sin(_vocalVibratoPhase) * (_vocalVibratoDepth * 0.018f) * vibratoDelayRamp;

                    double incVocal = (2.0 * Math.PI * (_currentVocalFreq * (1f + vocalPitchMod))) / sampleRate;
                    _vocalPhase += incVocal;
                    if (_vocalPhase > Math.PI * 2) _vocalPhase -= Math.PI * 2;

                    double pNorm = (_vocalPhase / (Math.PI * 2.0)) % 1.0;
                    float glottalPulse;
                    if (pNorm < 0.58)
                    {
                        glottalPulse = (float)(0.5 * (1.0 - Math.Cos(Math.PI * pNorm / 0.58)));
                    }
                    else if (pNorm < 0.82)
                    {
                        glottalPulse = (float)Math.Cos(Math.PI * (pNorm - 0.58) / 0.48);
                    }
                    else
                    {
                        glottalPulse = 0f;
                    }
                    glottalPulse -= 0.32f;

                    if (_vocalBreathiness > 0.01f)
                    {
                        float breathNoise = NextFastNoise();
                        _vocalBreathState += 0.35f * (breathNoise - _vocalBreathState);
                        glottalPulse += (breathNoise - _vocalBreathState) * (_vocalBreathiness * 0.45f);
                    }

                    _vocalMorphPhase += (2.0 * Math.PI * 0.45) / sampleRate;
                    if (_vocalMorphPhase > Math.PI * 2) _vocalMorphPhase -= Math.PI * 2;
                    float morphLfo = 0.5f + 0.5f * (float)Math.Sin(_vocalMorphPhase);

                    float f1 = 800f, f2 = 1350f, f3 = 2650f;
                    float q1 = 0.18f, q2 = 0.15f, q3 = 0.12f;

                    if (_vocalStyle == ChameleonVocalStyle.CyberSoprano)
                    {
                        f1 = 920f;
                        f2 = 1680f;
                        f3 = 3100f;
                        q1 = 0.16f;
                        q2 = 0.13f;
                        q3 = 0.10f;
                    }
                    else if (_vocalStyle == ChameleonVocalStyle.AnalogTenor)
                    {
                        f1 = 540f;
                        f2 = 1150f;
                        f3 = 2450f;
                        q1 = 0.20f;
                        q2 = 0.16f;
                        q3 = 0.14f;
                    }
                    else if (_vocalStyle == ChameleonVocalStyle.VocaloidMorph)
                    {
                        f1 = Mathf.Lerp(420f, 850f, morphLfo);
                        f2 = Mathf.Lerp(2100f, 1200f, morphLfo);
                        f3 = Mathf.Lerp(2900f, 2500f, morphLfo);
                        q1 = 0.15f;
                        q2 = 0.14f;
                        q3 = 0.11f;
                    }

                    float fSv1 = 2f * Mathf.Sin(Mathf.PI * Mathf.Clamp(f1 / (float)sampleRate, 0.005f, 0.45f));
                    _vocalF1_Lp += fSv1 * _vocalF1_Bp;
                    float hp1 = glottalPulse - _vocalF1_Lp - q1 * _vocalF1_Bp;
                    _vocalF1_Bp += fSv1 * hp1;

                    float fSv2 = 2f * Mathf.Sin(Mathf.PI * Mathf.Clamp(f2 / (float)sampleRate, 0.005f, 0.45f));
                    _vocalF2_Lp += fSv2 * _vocalF2_Bp;
                    float hp2 = glottalPulse - _vocalF2_Lp - q2 * _vocalF2_Bp;
                    _vocalF2_Bp += fSv2 * hp2;

                    float fSv3 = 2f * Mathf.Sin(Mathf.PI * Mathf.Clamp(f3 / (float)sampleRate, 0.005f, 0.45f));
                    _vocalF3_Lp += fSv3 * _vocalF3_Bp;
                    float hp3 = glottalPulse - _vocalF3_Lp - q3 * _vocalF3_Bp;
                    _vocalF3_Bp += fSv3 * hp3;

                    float vocalResonance = (_vocalF1_Bp * 0.95f) + (_vocalF2_Bp * 0.72f) + (_vocalF3_Bp * 0.45f);

                    float targetVocalAmp = IsHarmonicNoteStable
                        ? Mathf.Clamp01(DetectedEnergyRMS * 3.8f)
                        : (DetectedEnergyRMS > _silenceThreshold ? 0.20f : 0f);

                    if (targetVocalAmp > _vocalAmpEnv)
                        _vocalAmpEnv += (targetVocalAmp - _vocalAmpEnv) * vocalAttack;
                    else
                        _vocalAmpEnv *= vocalRelease;

                    float vocalDuck = 1.0f - (_drumSidechainDuck * 0.35f);
                    vocalSample = (float)Math.Tanh(vocalResonance * 1.65) * _vocalAmpEnv * _vocalMasterVolume * vocalDuck;
                }

                // 4. Émulation Percussive DSP
                float drumMix = 0f;

                if (_emulateDrums)
                {
                    // Kick
                    float currentKickFreq = 38f + 130f * _kickPitchEnv;
                    _kickPhase += (2.0 * Math.PI * currentKickFreq) / sampleRate;
                    if (_kickPhase > Math.PI * 2) _kickPhase -= Math.PI * 2;
                    float kickBody = (float)Math.Tanh(Math.Sin(_kickPhase) * 2.8);
                    float kickClick = NextFastNoise() * Mathf.Exp(-75f * (1f - _kickPitchEnv)) * 0.35f;
                    float kickSample = (kickBody * 0.85f + kickClick) * _kickEnv * _kickVolume;

                    _kickEnv *= kickDecay;
                    _kickPitchEnv *= kickPitchDecay;

                    // Snare
                    _snareTonePhase1 += (2.0 * Math.PI * 185.0) / sampleRate;
                    _snareTonePhase2 += (2.0 * Math.PI * 242.0) / sampleRate;
                    if (_snareTonePhase1 > Math.PI * 2) _snareTonePhase1 -= Math.PI * 2;
                    if (_snareTonePhase2 > Math.PI * 2) _snareTonePhase2 -= Math.PI * 2;

                    float snareTone = ((float)Math.Sin(_snareTonePhase1) * 0.6f + (float)Math.Sin(_snareTonePhase2) * 0.4f) * _snareToneEnv;

                    float rawNoise = NextFastNoise();
                    _snareHpState += 0.32f * (rawNoise - _snareHpState);
                    float snareSnappy = (rawNoise - _snareHpState) * _snareNoiseEnv;

                    float snareSample = (snareTone * 0.55f + snareSnappy * 0.90f) * _snareVolume;

                    _snareToneEnv *= snareToneDecay;
                    _snareNoiseEnv *= snareNoiseDecay;

                    // Hi-Hat
                    float hatRaw = NextFastNoise();
                    _hatHpState += 0.52f * (hatRaw - _hatHpState);
                    float hatSample = (hatRaw - _hatHpState) * _hatEnv * _hatVolume;

                    _hatEnv *= hatDecay;
                    _drumSidechainDuck *= duckDecay;

                    drumMix = (kickSample + snareSample + hatSample) * _drumMasterVolume;
                }

                // 5. Sommation Stéréo & Limiteur Analogique
                // Masterisé par le volume sonore global du jeu (Master + Mute) :
                // sans ça la sortie Caméléon (hors Mixer) restait à fond même à Master 0.
                float masterGain = _cachedMasterGain;
                float sharedElements = (synthSample * 0.45f) + (choirSample * 0.70f) + (vocalSample * 0.88f) + (drumMix * 1.10f);
                float finalL = (float)Math.Tanh((sharedElements + (bassSampleL * 0.92f)) * 0.92) * masterGain;
                float finalR = (float)Math.Tanh((sharedElements + (bassSampleR * 0.92f)) * 0.92) * masterGain;

                if (channels == 1)
                {
                    data[i] = (finalL + finalR) * 0.5f;
                }
                else
                {
                    data[i] = finalL;
                    data[i + 1] = finalR;
                    for (int c = 2; c < channels; c++)
                    {
                        data[i + c] = (finalL + finalR) * 0.5f;
                    }
                }
            }
        }

        public void TestTriggerChoir()
        {
            _pendingChoirTrigger = 1.15f;
        }

        public void TestTriggerVocal()
        {
            _pendingVocalTrigger = 1.25f;
            _vocalNoteTimer = 0f;
        }
    }
}

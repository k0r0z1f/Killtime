using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Audio;

namespace Killtime.Audio.Experimental
{
    /// <summary>SystemAudioChameleon — Basse dediee — 4 styles + sub-layer/duck (partial, move-only).</summary>
    public partial class SystemAudioChameleon : MonoBehaviour
    {
        // --- Basse 4 styles (extrait move-only de OnAudioFilterRead, meme ordre DSP) ---
        // Tout nouvel etat DSP : ajouter son reset dans Capture.cs (StopCapture).
        private void RenderBassStereo(double sampleRate, float glidePerSample, out float bassSampleL, out float bassSampleR)
        {
            bassSampleL = 0f;
            bassSampleR = 0f;

                if (_emulateBass)
                {
                    _currentBassFreq += (_targetBassFreq - _currentBassFreq) * (glidePerSample * 0.85f);
                    float pitchedBassFreq = _currentBassFreq * (1.0f + (_bassPunch * 0.85f * _bassPitchKickEnv));

                    _bassSubPhase += (2.0 * Math.PI * _currentBassFreq) / sampleRate;
                    if (_bassSubPhase > Math.PI * 2) _bassSubPhase -= Math.PI * 2;
                    float cleanSubSine = (float)Math.Sin(_bassSubPhase);

                    double incBass = (2.0 * Math.PI * pitchedBassFreq) / sampleRate;
                    _bassPhase += incBass;
                    if (_bassPhase > Math.PI * 2) _bassPhase -= Math.PI * 2;

                    float processedBassL = 0f;
                    float processedBassR = 0f;

                    if (_bassStyle == ChameleonBassStyle.Acid303)
                    {
                        float saw = (float)(2.0 * ((_bassPhase / (Math.PI * 2)) % 1.0) - 1.0);
                        float sqr = Mathf.Sign((float)Math.Sin(_bassPhase)) * 0.45f;
                        float rawBass = saw * 0.75f + sqr * 0.25f;

                        float acidCutoff = Mathf.Clamp(_bassCutoff * (0.20f + 2.8f * _bassFilterEnv), 45f, 13500f);
                        float fLadder = 2f * Mathf.Sin(Mathf.PI * Mathf.Clamp(acidCutoff / (float)sampleRate, 0.002f, 0.45f));
                        float res = Mathf.Clamp(_bassResonance * 1.05f, 0.1f, 3.85f);

                        float inputWithFeedback = rawBass - (res * _bassLadder4);
                        inputWithFeedback = (float)Math.Tanh(inputWithFeedback * 1.15f);

                        _bassLadder1 += fLadder * (inputWithFeedback - _bassLadder1);
                        _bassLadder2 += fLadder * (_bassLadder1 - _bassLadder2);
                        _bassLadder3 += fLadder * (_bassLadder2 - _bassLadder3);
                        _bassLadder4 += fLadder * (_bassLadder3 - _bassLadder4);

                        float ladderOut = _bassLadder4 * (1.0f + res * 0.42f);
                        float driven = ladderOut * _bassDrive;
                        float saturated = (float)Math.Tanh(driven + (driven * driven * 0.18f * Mathf.Sign(driven)));

                        processedBassL = saturated;
                        processedBassR = saturated;
                    }
                    else if (_bassStyle == ChameleonBassStyle.ReeseCyber)
                    {
                        _bassPhaseDetune1 += (2.0 * Math.PI * (pitchedBassFreq * 0.991)) / sampleRate;
                        _bassPhaseDetune2 += (2.0 * Math.PI * (pitchedBassFreq * 1.009)) / sampleRate;
                        _bassPhaseDetune3 += (2.0 * Math.PI * (pitchedBassFreq * 1.018)) / sampleRate;
                        if (_bassPhaseDetune1 > Math.PI * 2) _bassPhaseDetune1 -= Math.PI * 2;
                        if (_bassPhaseDetune2 > Math.PI * 2) _bassPhaseDetune2 -= Math.PI * 2;
                        if (_bassPhaseDetune3 > Math.PI * 2) _bassPhaseDetune3 -= Math.PI * 2;

                        float sawCenter = (float)(2.0 * ((_bassPhase / (Math.PI * 2)) % 1.0) - 1.0);
                        float sawLeft = (float)(2.0 * ((_bassPhaseDetune1 / (Math.PI * 2)) % 1.0) - 1.0);
                        float sawRight = (float)(2.0 * ((_bassPhaseDetune2 / (Math.PI * 2)) % 1.0) - 1.0);
                        float sawSpread = (float)(2.0 * ((_bassPhaseDetune3 / (Math.PI * 2)) % 1.0) - 1.0);

                        float rawL = sawCenter * 0.40f + sawLeft * 0.60f;
                        float rawR = sawCenter * 0.40f + sawRight * 0.45f + sawSpread * 0.25f;

                        float reeseCut = Mathf.Clamp(_bassCutoff * (0.45f + 1.4f * _bassFilterEnv), 90f, 9500f);
                        float fReese = 2f * Mathf.Sin(Mathf.PI * Mathf.Clamp(reeseCut / (float)sampleRate, 0.005f, 0.45f));

                        _bassLadder1 += fReese * (rawL - _bassLadder1);
                        _bassLadder2 += fReese * (_bassLadder1 - _bassLadder2);
                        _bassLadder3 += fReese * (rawR - _bassLadder3);
                        _bassLadder4 += fReese * (_bassLadder3 - _bassLadder4);

                        processedBassL = (float)Math.Tanh(_bassLadder2 * (_bassDrive * 0.90f));
                        processedBassR = (float)Math.Tanh(_bassLadder4 * (_bassDrive * 0.90f));
                    }
                    else if (_bassStyle == ChameleonBassStyle.DeepSub808)
                    {
                        float mainSine = (float)Math.Sin(_bassPhase);
                        float secondHarm = (float)Math.Sin(_bassPhase * 2.0) * (0.22f + 0.35f * _bassFilterEnv);
                        float thirdHarm = (float)Math.Sin(_bassPhase * 3.0) * 0.08f;

                        float composite = mainSine + secondHarm + thirdHarm;
                        float driven = composite * _bassDrive;
                        float warmTube = (float)Math.Tanh(driven + (driven * driven * 0.22f));

                        processedBassL = warmTube * 0.96f;
                        processedBassR = warmTube * 0.96f;
                    }
                    else if (_bassStyle == ChameleonBassStyle.NeuroMod)
                    {
                        _bassNeuroLfoPhase += (2.0 * Math.PI * 2.6) / sampleRate;
                        if (_bassNeuroLfoPhase > Math.PI * 2) _bassNeuroLfoPhase -= Math.PI * 2;
                        float lfoMod = 0.5f + 0.5f * (float)Math.Sin(_bassNeuroLfoPhase);

                        float saw = (float)(2.0 * ((_bassPhase / (Math.PI * 2)) % 1.0) - 1.0);
                        float folded = (float)Math.Sin(saw * (_bassDrive * 2.8f + lfoMod * 2.0f));

                        float neuroCutL = Mathf.Clamp(_bassCutoff * (0.35f + 1.8f * lfoMod), 120f, 7500f);
                        float neuroCutR = Mathf.Clamp(_bassCutoff * (0.45f + 1.6f * (1f - lfoMod)), 120f, 7500f);
                        float fL = 2f * Mathf.Sin(Mathf.PI * Mathf.Clamp(neuroCutL / (float)sampleRate, 0.005f, 0.45f));
                        float fR = 2f * Mathf.Sin(Mathf.PI * Mathf.Clamp(neuroCutR / (float)sampleRate, 0.005f, 0.45f));

                        _bassLadder1 += fL * (folded - _bassLadder1);
                        _bassLadder2 += fL * (_bassLadder1 - _bassLadder2);
                        _bassLadder3 += fR * (folded - _bassLadder3);
                        _bassLadder4 += fR * (_bassLadder3 - _bassLadder4);

                        processedBassL = (float)Math.Tanh(_bassLadder2 * 1.35f);
                        processedBassR = (float)Math.Tanh(_bassLadder4 * 1.35f);
                    }

                    float subLayerWeight = _bassSubCleanLayer * 0.75f;
                    float harmonicWeight = 1.0f - (subLayerWeight * 0.40f);

                    bassSampleL = (processedBassL * harmonicWeight) + (cleanSubSine * subLayerWeight);
                    bassSampleR = (processedBassR * harmonicWeight) + (cleanSubSine * subLayerWeight);

                    float targetEnergy = Mathf.Clamp01(BassEnergyRMS * 3.8f);
                    float dynamicAmp = _bassRhythmicPluck ? _bassAmpEnv : Mathf.Max(_bassAmpEnv * 0.6f, targetEnergy);
                    float bassDuck = 1.0f - (_drumSidechainDuck * _bassSidechainDuck);

                    float totalGain = dynamicAmp * _bassMasterVolume * bassDuck;
                    bassSampleL *= totalGain;
                    bassSampleR *= totalGain;
                }
        }

        public void TestTriggerBass()
        {
            _pendingBassTrigger = 1.2f;
        }
    }
}

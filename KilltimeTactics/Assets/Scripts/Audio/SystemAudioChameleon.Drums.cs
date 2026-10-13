using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Audio;

namespace Killtime.Audio.Experimental
{
    /// <summary>SystemAudioChameleon — Detection percussive — flux/PLL/patterns + triggers test (partial, move-only).</summary>
    public partial class SystemAudioChameleon : MonoBehaviour
    {
        private void DetectPercussiveElements()
        {
            float now = Time.unscaledTime;

            float bassFlux = Mathf.Max(0f, BassEnergyRMS - _prevBassEnergy);
            float midFlux = Mathf.Max(0f, MidEnergyRMS - _prevMidEnergy);
            float highFlux = Mathf.Max(0f, HighEnergyRMS - _prevHighEnergy);

            _prevBassEnergy = BassEnergyRMS;
            _prevMidEnergy = MidEnergyRMS;
            _prevHighEnergy = HighEnergyRMS;

            float kickThreshold = Mathf.Max(0.012f, _silenceThreshold * 1.15f);
            float snareThreshold = Mathf.Max(0.016f, _silenceThreshold * 1.35f);
            float hatThreshold = Mathf.Max(0.007f, _silenceThreshold * 0.70f);

            bool kickDetected = false;
            int current16th = Mathf.Clamp((int)(_pllClockPhase * 16.0), 0, 15);

            // 1. Kick & Impulsion Basse
            if (bassFlux > kickThreshold && (now - _lastKickTime) > 0.15f && bassFlux > highFlux * 1.10f)
            {
                _lastKickTime = now;
                KickPulse = 1.0f;
                BeatPulse = 1.0f;
                kickDetected = true;

                _pendingBassTrigger = Mathf.Clamp(0.8f + (bassFlux * 12f), 0.8f, 1.3f);

                double targetBeatPhase = 0.0;
                double phaseErr = (_pllClockPhase * 4.0) % 1.0 - targetBeatPhase;
                if (phaseErr > 0.5) phaseErr -= 1.0;
                if (phaseErr < -0.5) phaseErr += 1.0;
                _pllPhaseErrorSmoothed = Mathf.Lerp(_pllPhaseErrorSmoothed, (float)phaseErr, 0.25f);
                _pllClockPhase -= (_pllPhaseErrorSmoothed * 0.25f) / 4.0;

                PhaseLockConfidence = Mathf.Clamp01(PhaseLockConfidence + 0.15f);
                _kickPatternMask[current16th] = 1.0f;

                float solidKickVel = Mathf.Clamp(0.80f + (bassFlux * 12f), 0.80f, 1.35f);
                if (!_usePredictiveSync || PhaseLockConfidence < 0.35f)
                {
                    _pendingKickVel = solidKickVel;
                }

                float interval = now - _lastBeatTime;
                _lastBeatTime = now;
                if (interval >= 0.26f && interval <= 1.5f)
                {
                    float instantBpm = 60f / interval;
                    while (instantBpm < 75f) instantBpm *= 2f;
                    while (instantBpm > 175f) instantBpm *= 0.5f;
                    DetectedBPM = Mathf.Lerp(DetectedBPM, instantBpm, 0.25f);
                }
            }
            else if (bassFlux > kickThreshold * 0.7f && (now - _lastKickTime) > 0.10f)
            {
                _pendingBassTrigger = 0.9f;
            }

            // 2. Snare
            if (!kickDetected && midFlux > snareThreshold && (now - _lastSnareTime) > 0.16f && highFlux > 0.006f)
            {
                _lastSnareTime = now;
                SnarePulse = 1.0f;
                BeatPulse = Mathf.Max(BeatPulse, 0.75f);
                _snarePatternMask[current16th] = 1.0f;

                float solidSnareVel = Mathf.Clamp(0.75f + (midFlux * 10f), 0.75f, 1.25f);
                if (!_usePredictiveSync || PhaseLockConfidence < 0.35f)
                {
                    _pendingSnareToneVel = solidSnareVel;
                    _pendingSnareNoiseVel = solidSnareVel * 1.15f;
                }
            }

            // 3. Hi-Hat
            if (highFlux > hatThreshold && (now - _lastHatTime) > 0.055f && bassFlux < highFlux * 1.6f)
            {
                _lastHatTime = now;
                HatPulse = 1.0f;
                _hatPatternMask[current16th] = 1.0f;

                float solidHatVel = Mathf.Clamp(0.65f + (highFlux * 15f), 0.65f, 1.15f);
                if (!_usePredictiveSync || PhaseLockConfidence < 0.35f)
                {
                    _pendingHatVel = solidHatVel;
                }
            }

            for (int i = 0; i < 16; i++)
            {
                _kickPatternMask[i] = Mathf.MoveTowards(_kickPatternMask[i], 0f, Time.unscaledDeltaTime * 0.20f);
                _snarePatternMask[i] = Mathf.MoveTowards(_snarePatternMask[i], 0f, Time.unscaledDeltaTime * 0.20f);
                _hatPatternMask[i] = Mathf.MoveTowards(_hatPatternMask[i], 0f, Time.unscaledDeltaTime * 0.30f);
            }
        }

        public void TestTriggerKick()
        {
            KickPulse = 1.0f;
            BeatPulse = 1.0f;
            _pendingKickVel = 1.0f;
        }

        public void TestTriggerSnare()
        {
            SnarePulse = 1.0f;
            _pendingSnareToneVel = 0.9f;
            _pendingSnareNoiseVel = 1.1f;
        }

        public void TestTriggerHat()
        {
            HatPulse = 1.0f;
            _pendingHatVel = 0.85f;
        }
    }
}

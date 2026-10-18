using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.UI;

namespace Killtime.Multi.Voice
{
    public partial class VTTVoiceManager : MonoBehaviour
    {

        public void RefreshDevices()
        {
            _availableDevices.Clear();
            var devs = Microphone.devices;
            if (devs != null && devs.Length > 0)
            {
                _availableDevices.AddRange(devs);
            }

            if (string.IsNullOrEmpty(_selectedDevice) || !_availableDevices.Contains(_selectedDevice))
            {
                _selectedDevice = _availableDevices.Count > 0 ? _availableDevices[0] : "";
            }

            try { OnDevicesRefreshed?.Invoke(); } catch (Exception e) { Debug.LogException(e); }
        }

        public void SelectDevice(string deviceName)
        {
            if (_selectedDevice == deviceName && _isRecording) return;
            _selectedDevice = deviceName;
            SavePreferences();

            if (_isRecording)
            {
                StartCapture();
            }
        }

        public void StartCapture()
        {
            StopCapture();

            if (_availableDevices.Count == 0)
            {
                RefreshDevices();
                if (_availableDevices.Count == 0)
                {
                    Debug.LogWarning("[VTTVoiceManager] Aucun microphone détecté.");
                    return;
                }
            }

            string device = string.IsNullOrEmpty(_selectedDevice) ? null : _selectedDevice;

            try
            {
                int minFreq, maxFreq;
                Microphone.GetDeviceCaps(device, out minFreq, out maxFreq);
                int targetFreq = _sampleRate;
                if (maxFreq > 0 && targetFreq > maxFreq) targetFreq = maxFreq;
                if (minFreq > 0 && targetFreq < minFreq) targetFreq = minFreq;

                _micClip = Microphone.Start(device, true, 10, targetFreq);
                _isRecording = (_micClip != null);
                _lastReadHead = 0;
                _capturePrimed = false;
                _accumCount = 0;
                _resamplePos = 1.0;
                _lastInputSample = 0f;

                _capturedSampleRate = (_micClip != null && _micClip.frequency > 0) ? _micClip.frequency : targetFreq;
                _capturedChannels = (_micClip != null && _micClip.channels > 0) ? _micClip.channels : 1;
                SetupAntiAliasingFilter(_capturedSampleRate, _sampleRate);

                _dsp?.Reset();
                Debug.Log($"[VTTVoiceManager] Capture démarrée sur '{device ?? "Défaut"}' @ {_capturedSampleRate} Hz ({_capturedChannels} ch) -> pipeline {_sampleRate} Hz.");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[VTTVoiceManager] Échec du démarrage du microphone: {e.Message}");
                _isRecording = false;
            }
        }

        public void StopCapture()
        {
            if (_isRecording)
            {
                try
                {
                    string device = string.IsNullOrEmpty(_selectedDevice) ? null : _selectedDevice;
                    Microphone.End(device);
                }
                catch { /* ignore */ }
                _isRecording = false;
            }

            if (_micClip != null)
            {
                Destroy(_micClip);
                _micClip = null;
            }
        }

        /// <summary>
        /// Rééchantillonne un flux ou tampon audio d'une fréquence source vers une fréquence cible (ex: 48k -> 16k).
        /// </summary>
        public static float[] ResampleBuffer(float[] input, int inRate, int outRate)
        {
            if (input == null || input.Length == 0) return Array.Empty<float>();
            if (inRate <= 0 || outRate <= 0 || inRate == outRate) return (float[])input.Clone();

            double step = (double)inRate / (double)outRate;
            int outCount = Mathf.RoundToInt((float)(input.Length * (double)outRate / (double)inRate));
            float[] output = new float[outCount];

            double pos = 0.0;
            int inLen = input.Length;

            for (int i = 0; i < outCount; i++)
            {
                int idx = (int)pos;
                float frac = (float)(pos - idx);
                float s0 = (idx < inLen) ? input[idx] : 0f;
                float s1 = (idx + 1 < inLen) ? input[idx + 1] : s0;
                output[i] = s0 + frac * (s1 - s0);
                pos += step;
            }

            return output;
        }

        /// <summary>
        /// Extrait le canal mono principal (Canal 0) d'un flux audio entrelacé multicanal.
        /// Évite absolument l'annulation de phase fatale ((L+R)/2 = 0) causée par l'inversion de phase des micros arrays Windows,
        /// et garantit l'absence totale de sauts de phase (popping / craquements) dus à des commutations intempestives de canal.
        /// </summary>
        public static float[] ExtractPrimaryMonoChannel(float[] rawBuffer, int channels, int samplesToRead)
        {
            if (rawBuffer == null || samplesToRead <= 0) return Array.Empty<float>();
            if (channels <= 1) return rawBuffer;

            float[] monoSamples = new float[samplesToRead];
            for (int i = 0; i < samplesToRead; i++)
            {
                monoSamples[i] = rawBuffer[i * channels];
            }
            return monoSamples;
        }

        private void Update()
        {
            if (!_isRecording || _micClip == null) return;

            string device = string.IsNullOrEmpty(_selectedDevice) ? null : _selectedDevice;
            int currentHead = Microphone.GetPosition(device);
            if (currentHead < 0) return;

            int clipSamples = _micClip.samples;
            // Marge de sécurité (25ms) pour éviter de lire sur la tête d'écriture matérielle de Windows (WASAPI race condition / crackles)
            int safetyMargin = Mathf.Max(512, (_capturedSampleRate * 25) / 1000);

            if (!_capturePrimed)
            {
                // Attente d'un tampon initial suffisant avant d'engager la lecture
                if (currentHead < safetyMargin * 2) return;
                _lastReadHead = (currentHead - safetyMargin + clipSamples) % clipSamples;
                _capturePrimed = true;
                return;
            }

            int targetHead = (currentHead - safetyMargin + clipSamples) % clipSamples;
            if (targetHead == _lastReadHead) return;

            int samplesToRead = (targetHead >= _lastReadHead)
                ? (targetHead - _lastReadHead)
                : ((clipSamples - _lastReadHead) + targetHead);

            if (samplesToRead <= 0) return;

            // Détection de décrochage anormal (si l'application a gelé)
            if (samplesToRead >= clipSamples - safetyMargin)
            {
                _lastReadHead = (currentHead - safetyMargin + clipSamples) % clipSamples;
                return;
            }

            // Lecture et accumulation (gestion mono / multi-canaux)
            int channels = _capturedChannels > 0 ? _capturedChannels : 1;
            int totalFloats = samplesToRead * channels;
            float[] rawBuffer = new float[totalFloats];

            if (_lastReadHead + samplesToRead <= clipSamples)
            {
                _micClip.GetData(rawBuffer, _lastReadHead);
            }
            else
            {
                int part1 = clipSamples - _lastReadHead;
                float[] b1 = new float[part1 * channels];
                _micClip.GetData(b1, _lastReadHead);
                Array.Copy(b1, 0, rawBuffer, 0, b1.Length);

                int part2 = samplesToRead - part1;
                if (part2 > 0)
                {
                    float[] b2 = new float[part2 * channels];
                    _micClip.GetData(b2, 0);
                    Array.Copy(b2, 0, rawBuffer, b1.Length, b2.Length);
                }
            }
            _lastReadHead = (_lastReadHead + samplesToRead) % clipSamples;

            // Conversion en mono si le micro capture en stéréo ou plus (cas des micros arrays de PC portables) :
            // Ne JAMAIS moyenner aveuglément L et R (risque fatal d'opposition de phase 180° et de filtre en peigne destructeur).
            // On sélectionne le canal principal (Canal 0) ou le canal le plus dynamique s'il est plus fort.
            float[] monoSamples = ExtractPrimaryMonoChannel(rawBuffer, channels, samplesToRead);

            // Filtre anti-repliement si la fréquence du micro dépasse la fréquence du codec (ex: 48kHz -> 16kHz)
            if (_antiAliasingNeeded)
            {
                ApplyAntiAliasingFilter(monoSamples, samplesToRead);
            }

            // Découpage et rééchantillonnage continu vers les trames de 60ms du codec
            if (_capturedSampleRate == _sampleRate)
            {
                // Fréquences identiques (ex: 16 kHz sous Linux) : transfert direct
                int readOffset = 0;
                while (readOffset < samplesToRead)
                {
                    int needed = _frameSize - _accumCount;
                    int available = samplesToRead - readOffset;
                    int toCopy = Mathf.Min(needed, available);

                    Array.Copy(monoSamples, readOffset, _accumulator, _accumCount, toCopy);
                    _accumCount += toCopy;
                    readOffset += toCopy;

                    if (_accumCount >= _frameSize)
                    {
                        ProcessAudioFrame(_accumulator, _frameSize);
                        _accumCount = 0;
                    }
                }
            }
            else
            {
                // Fréquences différentes (ex: 48 kHz ou 44.1 kHz sous Windows vers 16 kHz) :
                // Rééchantillonneur continu par interpolation linéaire sans à-coup entre les blocs
                double step = (double)_capturedSampleRate / (double)_sampleRate;
                int M = samplesToRead;

                while (_resamplePos <= M)
                {
                    int i = (int)_resamplePos;
                    float frac = (float)(_resamplePos - i);
                    float s0 = (i == 0) ? _lastInputSample : monoSamples[i - 1];
                    float s1 = (i < M) ? monoSamples[i] : s0;
                    float sample = s0 + frac * (s1 - s0);

                    _accumulator[_accumCount++] = sample;
                    if (_accumCount >= _frameSize)
                    {
                        ProcessAudioFrame(_accumulator, _frameSize);
                        _accumCount = 0;
                    }

                    _resamplePos += step;
                }

                _resamplePos -= M;
                _lastInputSample = monoSamples[M - 1];
            }
        }

        private void ProcessAudioFrame(float[] rawFrame, int count)
        {
            float dt = FrameDurationMs / 1000f;
            if (_dsp != null)
            {
                _dsp.AntiKeyboardFilterEnabled = _antiKeyboardFilterEnabled || _radioCommsEffectEnabled;
            }
            _dsp.Process(rawFrame, _dspOutBuffer, count, dt);

            // Détermination de l'état d'émission
            bool shouldTransmit = false;

            if (!_isMuted && !_isDeafened)
            {
                switch (_activationMode)
                {
                    case VoiceActivationMode.VoiceActivity:
                        if (_dsp.IsGateOpen)
                        {
                            _vadHangoverTimer = _vadHangoverSec;
                            shouldTransmit = true;
                        }
                        else if (_vadHangoverTimer > 0f)
                        {
                            _vadHangoverTimer -= dt;
                            shouldTransmit = true;
                        }
                        break;

                    case VoiceActivationMode.PushToTalk:
                        shouldTransmit = Input.GetKey(_pttKey);
                        break;

                    case VoiceActivationMode.Continuous:
                        shouldTransmit = true;
                        break;
                }
            }

            // Mise à jour de l'indicateur local
            if (_isLocalSpeaking != shouldTransmit)
            {
                _isLocalSpeaking = shouldTransmit;
                try { OnLocalSpeakingChanged?.Invoke(_isLocalSpeaking); }
                catch (Exception e) { Debug.LogException(e); }
            }

            // Nettoyage et Effets à l'enregistrement (Noise Gate Deleter & Radio Comms)
            ApplyRecordingEffects(_dspOutBuffer, count, shouldTransmit, dt);

            // Plafond Anti-Cris / Limiteur Dynamique (actif en temps réel à l'enregistrement et au test)
            if (_outputLimiterEnabled)
            {
                ApplyLimiterBuffer(_dspOutBuffer, count);
            }
            else
            {
                CurrentOutputPeak = _dsp != null ? _dsp.CurrentPeak : 0f;
            }

            // Émission réseau avec trame de fin pour clore le flux sans coupure abrupte
            if (shouldTransmit || _wasTransmitting)
            {
                TransmitFrame(_dspOutBuffer, count);
            }
            _wasTransmitting = shouldTransmit;

            // Test Loopback local (pour calibrer son micro en s'écoutant)
            if (_isLoopbackTestActive)
            {
                EnsureLoopbackPlayer();
                _loopbackPlayer.EnqueueSamples(_dspOutBuffer);
            }
        }

        private void TransmitFrame(float[] pcm, int count)
        {
            var room = VTTRoomManager.Instance;
            if (room == null || !room.InRoom) return;

            byte[] encoded = _activeCodec.Encode(pcm, count);
            if (encoded == null || encoded.Length == 0) return;

            string base64 = Convert.ToBase64String(encoded);
            _packetSequence++;

            string payload = VTTProtocol.BuildVoicePayloadJson((int)_codecType, _sampleRate, _packetSequence, base64);
            room.SendTableOp(VTTProtocol.OpVoice, payload);
        }

    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Audio;

namespace Killtime.Audio.Experimental
{
    /// <summary>SystemAudioChameleon — Capture micro — SelectDevice/Start/Stop/Loopback (partial, move-only).</summary>
    public partial class SystemAudioChameleon : MonoBehaviour
    {
        public void SelectDevice(string deviceName)
        {
            _activeDeviceName = deviceName;
            if (_enableChameleonMode)
            {
                StartCapture();
            }
        }

        public void StartCapture()
        {
            StopCapture();
            RefreshMasterGain();

            // Latence DSP faible requise pour l'analyse : on l'applique juste avant
            // d'ouvrir le micro (jamais à la création / ouverture du panneau).
            // Le Reset peut stopper les sources en cours => on relance l'humeur
            // courante juste après (le mute d'isolation, lui, s'applique ensuite).
            OptimizeAudioConfiguration();
            var musicMgr = KilltimeAudioManager.Instance;
            if (musicMgr != null && musicMgr.CurrentMood != MusicMood.None)
            {
                musicMgr.PlayMusic(musicMgr.CurrentMood, musicMgr.CurrentTrackIndex, musicMgr.CurrentIntensity, forceRestart: true);
            }

            var devices = Microphone.devices;
            if (devices == null || devices.Length == 0)
            {
                Debug.LogWarning("[SystemAudioChameleon] Aucun périphérique audio détecté.");
                return;
            }

            if (string.IsNullOrEmpty(_activeDeviceName) || Array.IndexOf(devices, _activeDeviceName) < 0)
            {
                _activeDeviceName = FindBestLoopbackDevice() ?? devices[0];
            }

            try
            {
                _micClip = Microphone.Start(_activeDeviceName, true, ClipDurationSec, _sampleRate);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SystemAudioChameleon] Échec avec '{_activeDeviceName}', repli sur défaut : {ex.Message}");
                _activeDeviceName = null;
                _micClip = Microphone.Start(null, true, ClipDurationSec, _sampleRate);
            }

            if (_micClip == null)
            {
                Debug.LogError("[SystemAudioChameleon] Impossible d'initialiser le flux audio.");
                IsExternalMusicDetected = false;
                return;
            }

            if (_isolateByMutingGameMusic && KilltimeAudioManager.Instance != null)
            {
                _savedGameMusicVolume = KilltimeAudioManager.Instance.GetCategoryVolume(SoundCategory.Music);
                // Duck temporaire : ne jamais persister le 0, sinon la musique
                // reste à 0% au redémarrage (PlayerPrefs écrasé).
                KilltimeAudioManager.Instance.SetCategoryVolume(SoundCategory.Music, 0f, save: false);
            }

            _audioSource.clip = _micClip;
            StopAllCoroutines();
            StartCoroutine(WaitForAudioStreamReady());
        }

        private System.Collections.IEnumerator WaitForAudioStreamReady()
        {
            float timeout = Time.realtimeSinceStartup + 3.0f;
            while (Microphone.GetPosition(_activeDeviceName) < BufferSize && Time.realtimeSinceStartup < timeout)
            {
                yield return null;
            }

            if (Microphone.IsRecording(_activeDeviceName))
            {
                _audioSource.Play();
                Debug.Log($"[SystemAudioChameleon] Écoute système engagée à {_sampleRate} Hz sur : '{_activeDeviceName ?? "Défaut Système"}'");

                if (Application.platform == RuntimePlatform.LinuxPlayer || Application.platform == RuntimePlatform.LinuxEditor)
                {
                    RefreshLinuxApps();
                    if (DetectedMediaApps.Count == 1)
                    {
                        LinkLinuxApp(DetectedMediaApps[0]);
                    }
                }
            }
            else
            {
                Debug.LogWarning("[SystemAudioChameleon] Le périphérique n'a pas pu démarrer.");
            }
        }

        public void StopCapture()
        {
            StopAllCoroutines();

            if (Microphone.IsRecording(_activeDeviceName))
            {
                Microphone.End(_activeDeviceName);
            }

            if (_audioSource != null && _audioSource.isPlaying)
            {
                _audioSource.Stop();
            }

            if (_savedGameMusicVolume >= 0f && KilltimeAudioManager.Instance != null)
            {
                KilltimeAudioManager.Instance.SetCategoryVolume(SoundCategory.Music, _savedGameMusicVolume, save: false);
                _savedGameMusicVolume = -1f;
            }

            IsExternalMusicDetected = false;
            IsHarmonicNoteStable = false;
            DetectedNoteName = "---";
            PitchConfidence = 0f;
            DetectedBassFrequencyHz = 0f;
            BassPitchConfidence = 0f;
            BeatPulse = 0f;
            KickPulse = 0f;
            SnarePulse = 0f;
            HatPulse = 0f;
            PhaseLockConfidence = 0f;
            _pendingKickVel = 0f;
            _pendingSnareToneVel = 0f;
            _pendingSnareNoiseVel = 0f;
            _pendingHatVel = 0f;
            _pendingBassTrigger = 0f;
            _bassAmpEnv = 0f;
            _bassFilterEnv = 0f;
            _bassPitchKickEnv = 0f;
            _pendingChoirTrigger = 0f;
            _choirAmpEnv = 0f;
            _pendingVocalTrigger = 0f;
            _vocalAmpEnv = 0f;
        }

        private string FindBestLoopbackDevice()
        {
            var devices = Microphone.devices;
            if (devices == null || devices.Length == 0) return null;

            for (int i = 0; i < devices.Length; i++)
            {
                string d = devices[i].ToLowerInvariant();
                if (d.Contains("monitor") || d.Contains("moniteur") || d.Contains("mixage") ||
                    d.Contains("stereo mix") || d.Contains("loopback") || d.Contains("what u hear") || d.Contains("cable"))
                {
                    return devices[i];
                }
            }

            return devices[0];
        }
    }
}

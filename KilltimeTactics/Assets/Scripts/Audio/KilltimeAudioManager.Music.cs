using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using Killtime.Core.Combat;

namespace Killtime.Audio
{
    /// <summary>KilltimeAudioManager — Musique adaptative — PlayMusic/Stop/Stinger/Battle + crossfades (partial, move-only).</summary>
    public partial class KilltimeAudioManager : MonoBehaviour
    {
        // --- Musique adaptative ---
        /// <summary>Bascule d'humeur en conservant l'intensité courante (compat ascendante).</summary>
        public void PlayMusic(MusicMood mood, bool forceRestart = false)
        {
            float keep = _targetMood == mood ? _targetIntensity01 : 0.5f;
            PlayMusic(mood, keep, forceRestart);
        }

        public void PlayMusic(MusicMood mood, MusicIntensity level, bool forceRestart = false)
        {
            PlayMusic(mood, AdaptiveMusicDirector.IntensityTo01(level), forceRestart);
        }

        /// <summary>
        /// Cœur adaptatif : mood + intensité 0..1 (quantifiée en 3 variantes procédurales).
        /// Sans effet si même mood ET même variante, sauf forceRestart.
        /// </summary>
        public void PlayMusic(MusicMood mood, float intensity01, bool forceRestart = false)
        {
            if (mood != _targetMood || forceRestart)
            {
                _currentTrackIndex = 0;
                _combatTrackTimer = 0f;
            }
            PlayMusic(mood, _currentTrackIndex, intensity01, forceRestart);
        }

        public void PlayMusic(MusicMood mood, int trackIndex, float intensity01, bool forceRestart = false)
        {
            if (mood == MusicMood.None) { StopMusic(); return; }
            intensity01 = Mathf.Clamp01(intensity01);
            // Mode génératif ∞ : Combat / Boss / Tension = composition live, pas de chanson préfaite.
            if (_generativeCombat && IsGenerativeMood(mood))
            {
                StartGenerativeLoop(mood, intensity01, forceRestart);
                return;
            }
            int level = (int)AdaptiveMusicDirector.Quantize(intensity01);
            int trackCount = ProceduralAudioFactory.GetTrackCount(mood);
            trackIndex = Mathf.Clamp(trackIndex, 0, Mathf.Max(0, trackCount - 1));

            if (mood == _targetMood && trackIndex == _currentTrackIndex && level == _currentLevel && !forceRestart)
            {
                _targetIntensity01 = intensity01;
                return;
            }

            bool isSameTrackIntensityShift = (mood == _targetMood && trackIndex == _currentTrackIndex && !forceRestart);

            _targetMood = mood;
            _currentTrackIndex = trackIndex;
            _targetIntensity01 = intensity01;
            _lastAdaptiveSwitchTime = Time.unscaledTime;
            // Nouvelle piste combat => on repart sur un cycle complet de 48s.
            // Sans ce reset, un clic manuel sur "Piste 1" juste avant l'échéance
            // se faisait voler aussitôt par la rotation auto (impression "piste 1 ne joue pas").
            if (mood == MusicMood.Combat)
                _combatTrackTimer = 0f;

            if (_musicRoutine != null) StopCoroutine(_musicRoutine);
            _musicRoutine = StartCoroutine(CrossfadeMusicRoutine(mood, trackIndex, level, intensity01, isSameTrackIntensityShift));
        }

        public void NextCombatTrack()
        {
            // En génératif, "piste suivante" = variation inédite immédiate.
            if (_generativeCombat && IsGenerativeMood(_targetMood))
            {
                RequestGenerativeVariation();
                return;
            }
            if (_targetMood != MusicMood.Combat) return;
            int count = ProceduralAudioFactory.GetTrackCount(MusicMood.Combat);
            if (count <= 1) return;

            _combatTrackTimer = 0f;
            int next = (_currentTrackIndex + 1) % count;
            PlayMusic(MusicMood.Combat, next, _targetIntensity01, forceRestart: true);
        }

        public void StopMusic()
        {
            _targetMood = MusicMood.None;
            _currentMood = MusicMood.None;
            _currentTrackIndex = 0;
            _combatTrackTimer = 0f;
            _genLoopTimer = 0f;
            _genParamsValid = false;
            _pendingStrike = 0f;
            _currentLevel = 1;
            _targetIntensity01 = 0.5f;
            _currentIntensity01 = 0.5f;
            _activeMusic = null;
            _isFading = false;
            if (_musicRoutine != null) StopCoroutine(_musicRoutine);
            if (_musicA != null) _musicA.Stop();
            if (_musicB != null) _musicB.Stop();
        }

        /// <summary>Stinger superposé (transition dramatique) : ne coupe pas la boucle. Anti-spam 1s.</summary>
        public void PlayStinger(MusicMood mood, float volumeScale = 1f)
        {
            if (mood == MusicMood.None) return;
            if (Time.unscaledTime - _lastStingerTime < 1f) return;
            var clip = ProceduralAudioFactory.GetStinger(mood);
            if (clip == null) return;
            _lastStingerTime = Time.unscaledTime;
            var src = Next2D();
            src.Stop();
            src.clip = clip;
            src.loop = false;
            src.spatialBlend = 0f;
            src.priority = 64;
            // Stinger musical : jamais ralenti, même en pleine cinématique.
            src.pitch = 1f;
            src.volume = (_muted ? 0f : 1f) * _masterVolume * _musicVolume * _stingerVolume * volumeScale;
            src.Play();
        }

        /// <summary>
        /// Pilotage automatique depuis l'état tactique (appelé sur événements : tour, dégâts, reset).
        /// Hystérésis + cooldown anti-yoyo ; les changements de mood restent immédiats.
        /// Retourne true si une transition a démarré.
        /// </summary>
        public bool NotifyBattleState(float allyHpRatio01, int alliesAlive, int enemiesAlive, int turnIndex)
        {
            _lastAllyHp = Mathf.Clamp01(allyHpRatio01);
            _lastAllies = alliesAlive;
            _lastEnemies = enemiesAlive;
            _lastTurn = turnIndex;
            var (mood, intensity) = AdaptiveMusicDirector.ComputeTarget(
                allyHpRatio01, alliesAlive, enemiesAlive, turnIndex, _targetMood, _targetIntensity01);
            // Mode génératif : le mood Combat/Boss/Tension part en composition live.
            if (_generativeCombat && IsGenerativeMood(mood))
            {
                bool moodChange = mood != _targetMood;
                var genParams = AdaptiveMusicDirector.ComputeGenerativeParams(
                    allyHpRatio01, alliesAlive, enemiesAlive, turnIndex,
                    _generativeSeed, _genGeneration, 0f, _generativeBrightness, _generativeChaos, _generativePreset);
                bool bigShift = Mathf.Abs(genParams.intensity01 - _targetIntensity01) >= _adaptiveHysteresis
                    || Mathf.Abs(genParams.tension01 - (_genParamsValid ? _genParams.tension01 : 0.5f)) >= 0.25f;
                if (!moodChange && !bigShift && _genParamsValid) return false;
                if (!moodChange && Time.unscaledTime - _lastAdaptiveSwitchTime < _adaptiveCooldown && _genParamsValid) return false;
                StartGenerativeLoop(mood, genParams.intensity01, forceRestart: moodChange || bigShift);
                return true;
            }
            bool legacyMoodChange = mood != _targetMood;
            bool legacyShift = Mathf.Abs(intensity - _targetIntensity01) >= _adaptiveHysteresis;
            if (!legacyMoodChange && !legacyShift) return false;
            if (!legacyMoodChange && Time.unscaledTime - _lastAdaptiveSwitchTime < _adaptiveCooldown) return false;
            PlayMusic(mood, intensity);
            return true;
        }

        private IEnumerator CrossfadeMusicRoutine(MusicMood mood, int trackIndex, int level, float intensity01, bool syncPhaseWithPrevious = false)
        {
            AudioClip clip = null;
            try
            {
                clip = ProceduralAudioFactory.GetMusicLoop(mood, trackIndex, level);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[Audio] GetMusicLoop({mood} T{trackIndex} L{level}) a levé : {ex.GetType().Name}: {ex.Message}");
                _isFading = false;
                yield break;
            }
            if (clip == null)
            {
                Debug.LogError($"[Audio] Boucle introuvable : {mood} T{trackIndex} L{level} (piste silencieuse).");
                _isFading = false;
                yield break;
            }

            var fadeIn = _musicFlip ? _musicA : _musicB;
            var fadeOut = _musicFlip ? _musicB : _musicA;
            if (fadeIn == null)
            {
                Debug.LogError("[Audio] Source musique fadeIn manquante (BuildPools non appelé ?).");
                _isFading = false;
                yield break;
            }

            _isFading = true;
            _currentMood = mood;
            _currentLevel = level;
            _currentIntensity01 = intensity01;
            _musicFlip = !_musicFlip;

            // Setup sans yield => try/catch autorisé (une exception ici ne doit
            // jamais laisser _isFading bloqué à true, sinon la rotation s'arrête).
            try
            {
                fadeIn.clip = clip;
                fadeIn.loop = true;
                // Boucle musicale : pitch toujours 1 (pas de ralenti cinématique).
                fadeIn.pitch = 1f;
                fadeIn.volume = 0f;
                fadeIn.Play();

                if (syncPhaseWithPrevious)
                {
                    try
                    {
                        if (fadeOut != null && fadeOut.isPlaying && fadeOut.clip != null && fadeOut.time > 0.05f)
                            fadeIn.time = fadeOut.time % clip.length;
                    }
                    catch (System.Exception) { /* WebGL-safe : ignore le seek si refusé */ }
                }
                else
                {
                    // WebGL-safe : le seek immédiat après Play() peut être refusé.
                    try { fadeIn.time = 0f; }
                    catch (System.Exception) { }
                }

                _activeMusic = fadeIn;
                // Diagnostic "piste 1 muette" : la 2e lecture de T0 rendait un clip
                // à 0 samples côté Unity alors que la synthèse est saine hors Unity.
                // On sonde les données réelles pour trancher cache vide vs données zéros.
                float probePeak = -1f;
                string loadState = "?";
                try
                {
                    loadState = clip.loadState == AudioDataLoadState.Loaded ? "ready" : clip.loadState.ToString().ToLowerInvariant();
                    int probeN = System.Math.Min(2048, clip.samples);
                    if (probeN > 0)
                    {
                        float[] probe = new float[probeN];
                        if (clip.GetData(probe, 0))
                        {
                            float pk = 0f;
                            for (int i = 0; i < probe.Length; i++)
                            {
                                float a = System.Math.Abs(probe[i]);
                                if (a > pk) pk = a;
                            }
                            probePeak = pk;
                        }
                        else probePeak = -2f; // GetData refusé (données déchargées ?)
                    }
                    else probePeak = 0f;
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"[Audio] Sonde {mood} T{trackIndex} L{level} impossible : {ex.GetType().Name}: {ex.Message}");
                }
                Debug.Log($"[Audio] ▶ {mood} piste {trackIndex + 1} L{level} : clip='{clip.name}' {clip.samples} spl ({clip.length:F1}s) load={loadState} peak1k={probePeak:F3} src={(fadeIn == _musicA ? "A" : "B")} volMus={_musicVolume:F2} volMast={_masterVolume:F2} mute={_muted}");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[Audio] Démarrage {mood} T{trackIndex} L{level} impossible : {ex.GetType().Name}: {ex.Message}");
                _isFading = false;
                ApplyAllVolumes();
                yield break;
            }

            float t = 0f;
            bool isFinale = mood == MusicMood.Victory || mood == MusicMood.Defeat;
            float dur = isFinale ? Mathf.Max(_musicCrossfade, _finaleCrossfade) : Mathf.Max(0.4f, _musicCrossfade);

            // Boucle de fondu avec yield => try/finally uniquement (pas de catch,
            // sinon CS1626 : yield interdit dans un try avec catch).
            try
            {
                while (t < dur)
                {
                    t += Time.unscaledDeltaTime;
                    float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / dur));
                    float gIn = Mathf.Sin(k * Mathf.PI * 0.5f);
                    float gOut = Mathf.Cos(k * Mathf.PI * 0.5f);
                    float targetVol = _musicVolume * _masterVolume;
                    float duck = Mathf.Max(0f, 1f - 0.5f * _pauseDuck);
                    float mute = _muted ? 0f : 1f;
                    fadeIn.volume = mute * targetVol * gIn * duck;
                    if (fadeOut != null) fadeOut.volume = mute * targetVol * gOut * duck;
                    yield return null;
                }

                if (fadeOut != null)
                {
                    fadeOut.Stop();
                    fadeOut.volume = 0f;
                }
            }
            finally
            {
                _isFading = false;
                ApplyAllVolumes();
            }
        }

        /// <summary>
        /// Crossfade vers un clip déjà généré (voie générative ∞).
        /// Même courbe que la voie legacy, sans re-résolution SoundBank.
        /// </summary>
        private IEnumerator CrossfadeClipRoutine(AudioClip clip, MusicMood mood, int generation, float intensity01)
        {
            if (clip == null)
            {
                Debug.LogError($"[Audio] Clip génératif null : {mood} G{generation} (piste silencieuse).");
                _isFading = false;
                yield break;
            }
            var fadeIn = _musicFlip ? _musicA : _musicB;
            var fadeOut = _musicFlip ? _musicB : _musicA;
            if (fadeIn == null)
            {
                Debug.LogError("[Audio] Source musique fadeIn manquante (BuildPools non appelé ?).");
                _isFading = false;
                yield break;
            }
            _isFading = true;
            _currentMood = mood;
            _currentIntensity01 = intensity01;
            _currentLevel = (int)AdaptiveMusicDirector.Quantize(intensity01);
            _musicFlip = !_musicFlip;
            try
            {
                fadeIn.clip = clip;
                fadeIn.loop = true;
                fadeIn.pitch = 1f;
                fadeIn.volume = 0f;
                fadeIn.Play();
                try { fadeIn.time = 0f; }
                catch (System.Exception) { }
                _activeMusic = fadeIn;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[Audio] Démarrage génératif {mood} G{generation} impossible : {ex.GetType().Name}: {ex.Message}");
                _isFading = false;
                ApplyAllVolumes();
                yield break;
            }
            float t = 0f;
            float dur = Mathf.Max(0.4f, _generativeCrossfade);
            try
            {
                while (t < dur)
                {
                    t += Time.unscaledDeltaTime;
                    float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / dur));
                    float gIn = Mathf.Sin(k * Mathf.PI * 0.5f);
                    float gOut = Mathf.Cos(k * Mathf.PI * 0.5f);
                    float targetVol = _musicVolume * _masterVolume;
                    float duck = Mathf.Max(0f, 1f - 0.5f * _pauseDuck);
                    float mute = _muted ? 0f : 1f;
                    fadeIn.volume = mute * targetVol * gIn * duck;
                    if (fadeOut != null) fadeOut.volume = mute * targetVol * gOut * duck;
                    yield return null;
                }
                if (fadeOut != null)
                {
                    fadeOut.Stop();
                    fadeOut.volume = 0f;
                }
            }
            finally
            {
                _isFading = false;
                ApplyAllVolumes();
            }
        }

        /// <summary>
        /// Preuve visuelle anti-doute : nom + état des 2 seules sources du bus
        /// musique. En génératif on doit y voir UN clip GEN_* qui joue et
        /// l'autre source à "stop" (hors le ~1s de fondu entre variations).
        /// Legacy et génératif partagent ce bus : ils ne peuvent pas coexister.
        /// </summary>
        public string GetMusicSourcesDebug()
        {
            return $"A:{SrcTag(_musicA)}  B:{SrcTag(_musicB)}";
        }

        private static string SrcTag(AudioSource s)
        {
            if (s == null) return "-";
            try
            {
                if (!s.isPlaying) return "stop";
                string n = s.clip != null ? s.clip.name : "?";
                if (n.StartsWith("PROC_")) n = n.Substring(5);
                if (n.Length > 24) n = n.Substring(0, 24) + "…";
                return $"{n} v={s.volume:0.0}";
            }
            catch (System.Exception) { return "?"; }
        }
    }
}

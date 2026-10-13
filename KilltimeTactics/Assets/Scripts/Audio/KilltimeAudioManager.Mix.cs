using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using Killtime.Core.Combat;

namespace Killtime.Audio
{
    /// <summary>KilltimeAudioManager — Mix — cinematique/pause/mute/volumes (partial, move-only).</summary>
    public partial class KilltimeAudioManager : MonoBehaviour
    {
        // --- Snapshots code ---
        public void EnterCinematicMode()
        {
            // Déjà en cinématique (chaîne Fin→ sans Exit entre les deux) :
            // on maintient le duck sans rejouer le whoosh de transition, sinon
            // il coupe la continuité (ex : swell warp → approche).
            bool already = _cinematicDuck > 0.5f;
            _cinematicDuck = 1f;
            if (already) return;
            // Uniquement le whoosh d'air : le sweep SlowMo_Enter (800->120 Hz)
            // joué pendant le zoom-in sonnait comme un doppler/compression.
            Play(SoundId.Cinematic_WhooshIn, 0.8f);
        }

        public void ExitCinematicMode()
        {
            _cinematicDuck = 0f;
            // Idem en sortie : WhooshOut seul, sans sweep SlowMo_Exit.
            Play(SoundId.Cinematic_WhooshOut, 0.7f);
        }

        public void EnterPauseMode()
        {
            _pauseDuck = 1f;
            ApplyAllVolumes();
        }

        public void ExitPauseMode()
        {
            _pauseDuck = 0f;
            ApplyAllVolumes();
        }

        // --- Volumes ---
        /// <summary>
        /// Règle un volume. Si save=false, ne touche pas aux PlayerPrefs
        /// (réservé aux ducks temporaires, ex. Caméléon qui isole la musique :
        /// on ne doit jamais persister le 0 temporaire, sinon la musique
        /// reste à 0% au redémarrage suivant).
        /// </summary>
        public void SetCategoryVolume(SoundCategory cat, float v01, bool save = true)
        {
            v01 = Mathf.Clamp01(v01);
            switch (cat)
            {
                case SoundCategory.Master: _masterVolume = v01; if (save) PlayerPrefs.SetFloat(PP_MASTER, v01); break;
                case SoundCategory.Music: _musicVolume = v01; if (save) PlayerPrefs.SetFloat(PP_MUSIC, v01); break;
                case SoundCategory.Ambience: _ambienceVolume = v01; if (save) PlayerPrefs.SetFloat(PP_AMB, v01); break;
                case SoundCategory.SFX: _sfxVolume = v01; if (save) PlayerPrefs.SetFloat(PP_SFX, v01); break;
                case SoundCategory.UI: _uiVolume = v01; if (save) PlayerPrefs.SetFloat(PP_UI, v01); break;
                case SoundCategory.Cinematic: _cinematicVolume = v01; break;
            }
            if (save) PlayerPrefs.Save();
            ApplyAllVolumes();
        }

        public float GetCategoryVolume(SoundCategory cat)
        {
            return cat switch
            {
                SoundCategory.Master => _masterVolume,
                SoundCategory.Music => _musicVolume,
                SoundCategory.Ambience => _ambienceVolume,
                SoundCategory.SFX => _sfxVolume,
                SoundCategory.UI => _uiVolume,
                SoundCategory.Cinematic => _cinematicVolume,
                _ => 1f
            };
        }

        public void SetMuted(bool muted)
        {
            _muted = muted;
            ApplyAllVolumes();
        }

        private void LoadVolumes()
        {
            _masterVolume = PlayerPrefs.GetFloat(PP_MASTER, _masterVolume);
            _musicVolume = PlayerPrefs.GetFloat(PP_MUSIC, _musicVolume);
            _ambienceVolume = PlayerPrefs.GetFloat(PP_AMB, _ambienceVolume);
            _sfxVolume = PlayerPrefs.GetFloat(PP_SFX, _sfxVolume);
            _uiVolume = PlayerPrefs.GetFloat(PP_UI, _uiVolume);
        }

        private void ApplyAllVolumes()
        {
            if (_mixer != null)
            {
                // Paramètres exposés en dB : 0dB = 1.0, -40dB = quasi muet.
                SetMixerDb(_masterParam, _masterVolume);
                SetMixerDb(_musicParam, _musicVolume * (1f - 0.5f * _pauseDuck));
                SetMixerDb(_sfxParam, _sfxVolume);
                SetMixerDb(_uiParam, _uiVolume);
                SetMixerDb(_ambienceParam, _ambienceVolume);
            }
            if (_ambienceSource != null)
                _ambienceSource.volume = (_muted ? 0f : 1f) * _ambienceVolume * _masterVolume * 0.7f;
            // La boucle musicale active suit les sliders/mute en direct (hors crossfade,
            // où la coroutine pilote déjà les volumes chaque frame).
            if (_activeMusic != null && !_isFading)
            {
                float duck = Mathf.Max(0f, 1f - 0.5f * _pauseDuck);
                _activeMusic.volume = (_muted ? 0f : 1f) * _musicVolume * _masterVolume * duck;
            }
        }

        private void SetMixerDb(string param, float v01)
        {
            if (string.IsNullOrEmpty(param)) return;
            float db = v01 <= 0.0001f ? -60f : Mathf.Lerp(-40f, 0f, Mathf.Pow(v01, 0.6f));
            _mixer.SetFloat(param, db);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            _pool2DSize = Mathf.Clamp(_pool2DSize, 8, 32);
            _pool3DSize = Mathf.Clamp(_pool3DSize, 4, 24);
            // Clamp du champ legacy (CS0414 déjà masqué par pragma au niveau du champ).
            _slowMoMusicPitch = Mathf.Clamp(_slowMoMusicPitch, 0.4f, 1f);
            if (Application.isPlaying) ApplyAllVolumes();
        }
#endif
    }
}

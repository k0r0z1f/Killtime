using System.Collections;
using UnityEngine;
using Killtime.Story.Data;
using Killtime.UI;

namespace Killtime.CameraSystem
{
    /// <summary>
    /// RD-111 — helpers partagés des 2 lecteurs cinématiques (décision : PAS de
    /// fusion en lecteur unique à états). SceneCinematicRoutine (exclusif :
    /// caméra + letterbox + raccord + acteurs/props) et InPlaceCinematicRoutine
    /// (overlay additif sans caméra) gardent chacun leur boucle de frames — le
    /// corps par-frame n'a rien de commun. Ce qui est factorisé ici, c'est la
    /// machinerie identique des deux côtés : attente de pause, pause audio,
    /// horloge bornée, skip Escape, warp supraluminique, speech, cue audio.
    /// Toutes ces méthodes sont non-coroutines (sauf WaitWhilePaused) et sans
    /// effet de bord supplémentaire : comportement byte-identique.
    /// </summary>
    public partial class CinematicDirector : MonoBehaviour
    {
        /// <summary>Attend la levée de pause (remplace les boucles nues).</summary>
        private static IEnumerator WaitWhilePaused()
        {
            while (CombatHUD.IsPaused) yield return null;
        }

        /// <summary>Log sous-titre du plan (bloc speech identique des 2 côtés).</summary>
        private void LogShotSpeech(SceneCinematicShotData shot, string fallbackTitle)
        {
            if (!string.IsNullOrWhiteSpace(shot.Speech))
            {
                string who = string.IsNullOrWhiteSpace(shot.SpeakerId) ? fallbackTitle : shot.SpeakerId;
                CombatHUD.Instance?.AddAdvancedLog($"🎬 <b>[{who}]</b> « {shot.Speech} »", LogCategory.MovementAndTurns, "[CINÉMATIQUE]", new Color(1f, 0.5f, 0.9f));
            }
        }

        /// <summary>Joue le cue audio du plan s'il y en a un.</summary>
        private void PlayShotAudioIfCued(SceneCinematicShotData shot)
        {
            if (!string.IsNullOrWhiteSpace(shot.SoundCueId))
            {
                PlayShotAudio(shot);
            }
        }

        /// <summary>Met en pause la source du plan (entrée en pause jeu).</summary>
        private void PauseShotAudio()
        {
            if (_activeShotAudioSource != null && _activeShotAudioSource.isPlaying)
            {
                _activeShotAudioSource.Pause();
                _isAudioPausedByDirector = true;
            }
        }

        /// <summary>Reprend la source du plan (sortie de pause jeu).</summary>
        private void ResumeShotAudio()
        {
            if (_activeShotAudioSource != null && _isAudioPausedByDirector)
            {
                _activeShotAudioSource.UnPause();
                _isAudioPausedByDirector = false;
            }
        }

        /// <summary>
        /// Avance l'horloge d'un plan : delta borné à 0.1s (un hitch ne doit pas
        /// avaler la cinématique) + progression normalisée + easing.
        /// </summary>
        private static void AdvanceShotClock(ref float elapsed, float duration, CinematicEase ease, out float t, out float e)
        {
            elapsed += Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            t = Mathf.Clamp01(elapsed / duration);
            e = ApplyCinematicEase(ease, t);
        }

        /// <summary>Warp tenu à 1 pendant une attente (toujours vitesse lumière).</summary>
        private static void HoldShotWarp(SceneCinematicShotData shot)
        {
            if (shot.SuperluminalWarp) SetSkyboxWarp(1f);
        }

        /// <summary>Warp progressif pendant la lecture (palier singularité → voûte).</summary>
        private static void ProgressShotWarp(SceneCinematicShotData shot, float t)
        {
            if (shot.SuperluminalWarp) SetSkyboxWarp(WarpAmountForShotProgress(t, shot.Ease));
        }

        /// <summary>Retombée du warp en fin de plan (même si skip).</summary>
        private static void TeardownShotWarp(SceneCinematicShotData shot)
        {
            if (shot.SuperluminalWarp) ResetSkyboxWarp();
        }

        /// <summary>
        /// Prédicat skip Escape. Les gardes anti-transition-de-pause
        /// (sinon "reprendre" = "skipper") ne concernent que le lecteur
        /// exclusif : l'overlay appelle sans drapeaux (défauts false).
        /// </summary>
        private static bool ShouldSkipShot(SceneCinematicData cine, bool pausedNow = false, bool wasPaused = false)
        {
            return cine.Skippable && !pausedNow && !wasPaused && Input.GetKeyDown(KeyCode.Escape);
        }

        /// <summary>Log "cinématique passée" (lecteur exclusif uniquement).</summary>
        private void LogShotSkipped()
        {
            CombatHUD.Instance?.AddAdvancedLog("⏩ <b>Cinématique passée.</b>", LogCategory.MovementAndTurns, "[CINÉMATIQUE]", Color.gray);
        }
    }
}

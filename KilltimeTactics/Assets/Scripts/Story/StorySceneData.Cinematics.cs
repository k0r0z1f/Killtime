using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Core.Character;
using Killtime.Tactics.Grid;

namespace Killtime.Story.Data
{
    // ================= CINÉMATIQUES =================
    // Une cinématique = une carte globale 🎬 avec UNE entrée (liée depuis n'importe
    // quelle autre carte) et AUCUNE sortie. Son exécution est fire-and-forget :
    // elle ne bloque jamais l'enclenchement de la carte suivante.
    public enum CinematicEase
    {
        Linear,
        Smooth,
        EaseIn,
        EaseOut,
        EaseInOut,
        Punch
    }

    public enum CinematicCameraEffect
    {
        None,
        HandheldShake,
        PushIn,
        PullOut,
        OrbitLeft,
        OrbitRight,
        FovPunch,
        DutchSway
    }

    [Serializable]
    public class SceneCinematicActorPose
    {
        public string ActorId = "";
        public int Q = 0;
        public int R = 0;
        public float FacingAngle = 0f;
    }

    [Serializable]
    public class SceneCinematicPropPose
    {
        public string InteractableId = "";
        public int Q = 0;
        public int R = 0;
    }

    [Serializable]
    public class SceneCinematicShotData
    {
        public string ShotId = "shot_1";
        public string Label = "Plan 1";
        public float Duration = 2.5f;
        public float StartDelay = 0f;
        // Voyage caméra automatique : interpolation Start → End sur Duration.
        public Vector3 CamStartPos = new Vector3(0f, 9f, -9f);
        public Vector3 CamStartEuler = new Vector3(45f, 0f, 0f);
        public float CamStartFov = 60f;
        public Vector3 CamEndPos = new Vector3(0f, 4f, -5f);
        public Vector3 CamEndEuler = new Vector3(38f, 12f, 0f);
        public float CamEndFov = 48f;
        public CinematicEase Ease = CinematicEase.Smooth;
        public CinematicCameraEffect MoveEffect = CinematicCameraEffect.None;
        public float ShakeIntensity = 0.15f;
        public string FocusActorId = "";
        // Voyage RELATIF : START = caméra live au début du plan, END = START + (End − Start)
        // utilisé comme DÉCALAGE. Pour les effets (secousse...) sans imposer de position absolue.
        public bool RelativeToCurrent = false;
        // TRACKING : cadrage calculé sur la POSITION LIVE de FocusActorId au début du plan
        // (pitch = CamStartEuler.x, yaw = TrackYaw, distance = TrackDistance, hauteur pivot +0.6m).
        // Reproduit les anciens focus dialogue hardcodés, via carte 🎬 au lieu du code.
        public bool TrackFocusActor = false;
        public float TrackDistance = 7f;
        public float TrackYaw = 45f;
        // Sous-titre optionnel affiché pendant le plan.
        public string SpeakerId = "";
        public string Speech = "";
        public string SoundCueId = "";
        public float SoundDuration = 0f;
        public bool PersistSound = false;
        public bool Letterbox = true;
        // Effet supraluminique sur la voûte céleste (skybox) : coché dans
        // l'éditeur de cinématiques. Quand vrai, le plan démarre en singularité
        // (écran noir + 1px, temps dilaté, palier 20%) puis le pixel grossit
        // lui-même avant de devenir halo + stries jusqu'à la voûte normale.
        // SANS déplacement caméra requis : piloté par le temps du plan uniquement
        // (fonctionne sur plan fixe, START=END, sur place ou free-look).
        // Piloté au runtime par CinematicDirector → SpaceEnvironment.
        public bool SuperluminalWarp = false;
        // Trajectoires des pions : positions de départ et d'arrivée du plan.
        // Éditées dans le mode éditeur cinématique (capture depuis la carte de combat).
        public List<SceneCinematicActorPose> StartPoses = new();
        public List<SceneCinematicActorPose> EndPoses = new();
        public List<SceneCinematicPropPose> StartProps = new();
        public List<SceneCinematicPropPose> EndProps = new();

        public string GetSummary()
        {
            int moves = 0;
            if (StartPoses != null) moves += StartPoses.Count;
            if (EndPoses != null) moves += EndPoses.Count;
            string sub = string.IsNullOrWhiteSpace(Speech) ? "" : " 💬";
            string fx = MoveEffect == CinematicCameraEffect.None ? "" : $" ✦{MoveEffect}";
            string warp = SuperluminalWarp ? " ☄warp" : "";
            string delay = StartDelay > 0.01f ? $"[attente {StartDelay:0.0}s] " : "";
            string snd = string.IsNullOrWhiteSpace(SoundCueId) ? "" : $" 🔊{SoundCueId}{(SoundDuration > 0.01f ? $"({SoundDuration:0.#}s)" : "")}{(PersistSound ? " [persistant]" : "")}";
            return $"{delay}{Duration:0.0}s {Ease}{fx}{warp} · {moves} poses{sub}{snd}";
        }
    }

    public enum CinematicCameraTransition
    {
        Teleport = 0,
        Smooth = 1
    }

    /// <summary>
    /// Point de déclenchement d'une carte son par rapport à sa carte liée :
    /// au début (entrée/mise en scène) ou à la fin (sortie/chaînage).
    /// </summary>
    public enum SoundTriggerPoint
    {
        AtLinkedStart = 0,
        AtLinkedEnd = 1
    }

    /// <summary>
    /// Réglages audio globaux de la scène (contrôle total) : trim master appliqué
    /// à toutes les cartes 🔊/🌌 de la scène, mute global, kill-switch à la
    /// transition de nœud / sortie de scène, et arbitrage legacy vs cartes.
    /// Rétro-compatible : valeurs par défaut = comportement historique.
    /// </summary>
    [Serializable]
    public class SceneAudioSettings
    {
        public float MasterVolumeScale = 1f;
        public bool Muted = false;
        public bool StopAllSoundsOnNodeChange = false;
        public bool StopAllSoundsOnSceneExit = true;
        // Vrai (défaut) : si une carte 🔊 est liée à une réplique/event, le cue
        // legacy (Line.SoundCueId / ApplyAmbienceSettings) est ignoré pour éviter
        // le double déclenchement. Faux = comportement historique (les deux jouent).
        public bool PreferCardsOverLegacy = true;
    }

    /// <summary>
    /// Carte son/fx 🔊 : branchée depuis le bas d'une carte source (fil orange),
    /// elle démarre un son (SoundCueId → SoundId) avec délai et volume réglables,
    /// au début OU à la fin de la carte liée. Lecture non-bloquante (fire-and-forget).
    /// Le lien vit sur la carte son (SourceCardId) : les cartes sources ne changent pas.
    /// Contrôle total : stop explicite (StopSourceCardId), loop + fades, pitch,
    /// priorité, cooldown anti-spam, mute unitaire, spatialisation 3D.
    /// </summary>
    [Serializable]
    public class SceneSoundData
    {
        public string SoundId = "snd_1";
        public string Title = "Nouveau son";
        public string SoundCueId = "";
        public float DelaySeconds = 0f;
        public float DurationSeconds = 0f;
        public SoundTriggerPoint TriggerPoint = SoundTriggerPoint.AtLinkedStart;
        public float VolumeScale = 0.8f;
        public string SourceCardId = "";
        // Stop explicite : le son (y compris loop / durée 0 = infini) est coupé
        // avec FadeOutDuration quand la carte cible est atteinte (miroir de
        // l'arrêt d'ambiance). Vide = aucun arrêt (seule DurationSeconds auto-stoppe).
        public string StopSourceCardId = "";
        public bool Loop = false;
        public float FadeInDuration = 0f;
        public float FadeOutDuration = 0.3f;
        public float PitchScale = 1f;
        public int Priority = 128;
        public float CooldownSeconds = 0f;
        public bool Muted = false;
        public bool Spatialized = false;
        public int Q = 0;
        public int R = 0;
        public float Radius = 15f;
        public float GraphPosX = 0f;
        public float GraphPosY = 0f;
        public float CardWidth = 300f;
        public float CardHeight = 340f;

        public string GetSummary()
        {
            string when = TriggerPoint == SoundTriggerPoint.AtLinkedEnd ? "fin" : "début";
            string delay = DelaySeconds > 0.01f ? $" +{DelaySeconds:0.0}s" : "";
            string dur = DurationSeconds > 0.01f ? $" dur:{DurationSeconds:0.0}s" : (Loop ? " ∞" : "");
            string cue = string.IsNullOrWhiteSpace(SoundCueId) ? "(aucun son)" : SoundCueId;
            string src = string.IsNullOrWhiteSpace(SourceCardId) ? "(non liée)" : $"← {SourceCardId}";
            string stop = string.IsNullOrWhiteSpace(StopSourceCardId) ? "" : $" ■→{StopSourceCardId}";
            string flags = $"{(Muted ? " 🔇" : "")}{(Loop ? " ∞" : "")}{(Spatialized ? " 3D" : "")}";
            return $"{cue} [{when}{delay}{dur}]{flags} {src}{stop}";
        }
    }

    /// <summary>
    /// Carte d'ambiance 🌌 : branchée depuis le bas d'une carte source (entrée en haut),
    /// et branchée depuis son bas vers une autre carte pour l'arrêt.
    /// Gère volume, rayon spatial 3D ou boucle 2D, et presets sauvegardés en JSON.
    /// </summary>
    [Serializable]
    public class SceneAmbienceTrackData
    {
        public string AmbienceId = "amb_1";
        public string Title = "Nouvelle ambiance";
        public string PresetId = "amb_asteroid_base";
        public float VolumeScale = 0.65f;
        public float FadeInDuration = 1.5f;
        public float FadeOutDuration = 1.5f;
        public bool Spatialized = false;
        public int Q = 0;
        public int R = 0;
        public float Radius = 15f;
        public string StartSourceCardId = "";
        public string StopSourceCardId = "";
        public float GraphPosX = 0f;
        public float GraphPosY = 0f;
        public float CardWidth = 320f;
        public float CardHeight = 240f;

        public string GetSummary()
        {
            string spat = Spatialized ? $"Spatial ({Q},{R}) r:{Radius:0.#}m" : "Global 2D";
            string start = string.IsNullOrWhiteSpace(StartSourceCardId) ? "aucun" : StartSourceCardId;
            string stop = string.IsNullOrWhiteSpace(StopSourceCardId) ? "fin" : StopSourceCardId;
            return $"{PresetId} [{spat}] ➔ In:{start} | Out:{stop}";
        }
    }

}
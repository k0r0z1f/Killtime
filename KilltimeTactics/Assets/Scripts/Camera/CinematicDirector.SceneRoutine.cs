using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Audio;
using Killtime.Story.Data;
using Killtime.Story.Scenes;
using Killtime.Tactics;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.UI;

namespace Killtime.CameraSystem
{
    public partial class CinematicDirector : MonoBehaviour
    {

        private IEnumerator SceneCinematicRoutine(SceneCinematicData cine)
        {
            CurrentPlayingSceneCinematic = cine;
            float speed = cine.PlaybackSpeed > 0.01f ? cine.PlaybackSpeed : 1f;
            ResolveTacticalCam();
            // (Pas de mémorisation de "retour" : la fin de plan RESTE — voir bas de routine.)
            // Validation décisive : sans caméra, le travel est invisible (symptôme
            // "ça bouge [acteurs/barres] puis revient [rien]").
            if (_mainCamera == null)
            {
                try { _mainCamera = UnityEngine.Camera.main; } catch { /* ignore */ }
                if (_mainCamera == null)
                {
                    Debug.LogError("[CinematicDirector] Aucune caméra : cinématique annulée (CinematicDirector mal placé ?).");
                    CombatHUD.Instance?.AddAdvancedLog("<color=red><b>[CINÉMATIQUE] Aucune caméra trouvée : lecture annulée.</b></color>", LogCategory.MovementAndTurns, "[CINÉMATIQUE]", Color.red);
                    if (_tacticalCam != null) _tacticalCam.enabled = true;
                    if (CurrentMode == CameraMode.CinematicAction) CurrentMode = CameraMode.TacticalIsometric;
                    _letterboxActive = false;
                    ResetSkyboxWarp();
                    _activeSceneCinematicRoutine = null;
                    _sceneCinematicOnComplete = null;
                    yield break;
                }
            }
            bool driveCamera = !IsFreeLook && CurrentMode != CameraMode.FreeLook;
            if (driveCamera && !IsCameraOnDrivenTransform())
            {
                Debug.LogError("[CinematicDirector] Le CinematicDirector ne porte aucune caméra (ni lui ni ses enfants, et ce n'est pas la transform de la caméra principale) : le travel sera INVISIBLE. Placez-le sur le rig caméra.");
                CombatHUD.Instance?.AddAdvancedLog("<color=red><b>[CINÉMATIQUE] Director hors caméra : travel invisible (acteurs/barres seuls). Placez CinematicDirector sur le rig caméra.</b></color>", LogCategory.MovementAndTurns, "[CINÉMATIQUE]", Color.red);
            }
            if (!driveCamera)
            {
                // Pas un bug : en pilotage libre la caméra reste à la main (comme les
                // killshots). Log visible pour ne pas passer pour un "skip".
                CombatHUD.Instance?.AddAdvancedLog("<color=grey>[CINÉMATIQUE] Pilotage libre actif (F pour rendre la main) : caméra non prise en charge, les acteurs jouent.</color>", LogCategory.MovementAndTurns, "[CINÉMATIQUE]", Color.gray);
            }
            if (driveCamera)
            {
                CurrentMode = CameraMode.CinematicAction;
                if (_tacticalCam != null) _tacticalCam.enabled = false;
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.EnterCinematicMode();
            }

            var grid = FindAnyObjectByType<TacticalHexGrid>();
            bool skipped = false;
            Transform handoverTarget = null;

            var shots = cine.Shots != null ? new List<SceneCinematicShotData>(cine.Shots) : new List<SceneCinematicShotData>();

            Vector3 livePrePos = transform.position;
            Quaternion livePreRot = transform.rotation;
            float livePreFov = _mainCamera != null ? _mainCamera.fieldOfView : _normalFOV;
            // Handover de préemption : la nouvelle ciné part du live exact, avec un
            // raccord Smooth même si elle est réglée en Téléportation (sinon snap).
            bool handover = _handoverSmoothRequested;
            _handoverSmoothRequested = false;
            Debug.Log("[CINE] v14 routine scène " + (cine != null ? cine.CinematicId : "?") + " : " + shots.Count + " plans, warp1er=" + (shots.Count > 0 && shots[0] != null && shots[0].SuperluminalWarp) + ", transi=" + (cine != null ? cine.StartTransition.ToString() : "?") + ", drive=" + driveCamera + ", handover=" + handover);
            try { CombatHUD.Instance?.AddAdvancedLog("<color=grey>[CINE] v14 " + (cine != null ? cine.CinematicId : "?") + " : " + shots.Count + " plans, warp1er=" + (shots.Count > 0 && shots[0] != null && shots[0].SuperluminalWarp) + ", transi=" + (cine != null ? cine.StartTransition.ToString() : "?") + "</color>", LogCategory.MovementAndTurns, "[CINÉMATIQUE]", Color.gray); }
            catch { /* ignore */ }
            Vector3 preFocusPos = Vector3.zero;
            bool hasPreFocus = false;
            if (shots.Count > 0 && !string.IsNullOrWhiteSpace(shots[0]?.FocusActorId))
            {
                var fUnit = FindSceneUnit(shots[0].FocusActorId);
                if (fUnit != null)
                {
                    preFocusPos = fUnit.transform.position;
                    hasPreFocus = true;
                }
            }

            if (!cine.InPlace && driveCamera && (cine.StartTransition == CinematicCameraTransition.Smooth || handover) && shots.Count > 0 && shots[0] != null)
            {
                var firstShot = shots[0];
                Vector3 targetStartPos = firstShot.CamStartPos;
                Vector3 targetStartEuler = firstShot.CamStartEuler;
                float targetStartFov = firstShot.CamStartFov;

                if (firstShot.RelativeToCurrent)
                {
                    // Le plan part du live de toute façon : aucun voyage d'entrée.
                    targetStartPos = livePrePos;
                    targetStartEuler = livePreRot.eulerAngles;
                    targetStartFov = livePreFov;
                }
                else if (firstShot.TrackFocusActor && !string.IsNullOrWhiteSpace(firstShot.FocusActorId))
                {
                    var tracked = FindSceneUnit(firstShot.FocusActorId);
                    if (tracked != null)
                    {
                        try
                        {
                            Vector3 anchor = new Vector3(tracked.transform.position.x, 0f, tracked.transform.position.z) + new Vector3(0f, 0.6f, 0f);
                            float dist = Mathf.Clamp(firstShot.TrackDistance, 0.5f, 35f);
                            Quaternion sRot = Quaternion.Euler(firstShot.CamStartEuler.x, firstShot.TrackYaw, firstShot.CamStartEuler.z);
                            targetStartPos = anchor - (sRot * Vector3.forward) * dist;
                            if (targetStartPos.y < 0.15f) targetStartPos.y = 0.15f;
                            targetStartEuler = new Vector3(firstShot.CamStartEuler.x, firstShot.TrackYaw, firstShot.CamStartEuler.z);
                        }
                        catch { /* ignore */ }
                    }
                }

                float baseDur = cine.StartTransitionDuration > 0.01f ? cine.StartTransitionDuration : 0.5f;
                if (handover && cine.StartTransition != CinematicCameraTransition.Smooth)
                    baseDur = Mathf.Clamp(baseDur, 0.35f, 0.6f);
                float startTransDur = Mathf.Max(0.05f, baseDur / speed);
                float startTransElapsed = 0f;
                // Warp : si le premier plan part à vitesse lumière, la voûte est DÉJÀ contractée
                // pendant le raccord (on ouvre sur le point, pas de phase "les étoiles se contractent").
                bool firstIsWarp = firstShot != null && firstShot.SuperluminalWarp;
                if (firstIsWarp)
                {
                    SetSkyboxWarp(1f);
                    Debug.Log("[CinematicDirector] Warp pré-roll v10 : voûte déjà contractée pour " + (cine != null ? cine.CinematicId : "?"));
                    try { CombatHUD.Instance?.AddAdvancedLog("<color=grey>[WARP] pré-roll : ouverture déjà contractée.</color>", LogCategory.MovementAndTurns, "[CINÉMATIQUE]", Color.gray); }
                    catch { /* ignore */ }
                }
                while (startTransElapsed < startTransDur)
                {
                    yield return WaitWhilePaused();
                    startTransElapsed += Mathf.Min(Time.unscaledDeltaTime, 0.1f);
                    float st = Mathf.Clamp01(startTransElapsed / startTransDur);
                    float se = ApplyCinematicEase(CinematicEase.Smooth, st);
                    if (firstIsWarp) SetSkyboxWarp(1f);
                    transform.position = Vector3.Lerp(livePrePos, targetStartPos, se);
                    transform.rotation = Quaternion.Slerp(livePreRot, Quaternion.Euler(targetStartEuler), se);
                    if (_mainCamera != null) _mainCamera.fieldOfView = Mathf.Lerp(livePreFov, targetStartFov, se);
                    if (driveCamera) UpdateAdaptiveNear(transform.position);
                    if (ShouldSkipShot(cine))
                    {
                        skipped = true;
                        break;
                    }
                    yield return null;
                }
            }

            for (int s = 0; s < shots.Count; s++)
            {
                var shot = shots[s];
                if (shot == null) continue;
                float duration = Mathf.Max(0.2f, shot.Duration / speed);

                // Poses acteurs : verrou anti-déplacement + téléport au START,
                // interpolation vers END, finalisation au END. Les doublons (même unité
                // résolue deux fois) sont ignorés par référence pour éviter un double pilotage.
                var tracks = new List<ShotActorTrack>();
                if (shot.StartPoses != null)
                {
                    for (int p = 0; p < shot.StartPoses.Count; p++)
                    {
                        var pose = shot.StartPoses[p];
                        if (pose == null || string.IsNullOrWhiteSpace(pose.ActorId)) continue;
                        var unit = FindSceneUnit(pose.ActorId);
                        if (unit == null || IsUnitTracked(tracks, null, unit)) continue;
                        unit.CinematicDriveActive = true;
                        SceneCinematicActorPose endPose = null;
                        if (shot.EndPoses != null)
                            endPose = shot.EndPoses.Find(e => e != null && string.Equals(e.ActorId, pose.ActorId, StringComparison.OrdinalIgnoreCase));
                        var track = new ShotActorTrack { Unit = unit, HasEnd = endPose != null };
                        if (TryHexWorld(grid, pose.Q, pose.R, out var sw))
                        {
                            try
                            {
                                // Échec possible (empreinte occupée) : l'unité reste sur place,
                                // on part donc de sa position RÉELLE pour rester cohérent.
                                if (grid != null) unit.TeleportTo(new HexCoordinates(pose.Q, pose.R), grid);
                                else unit.transform.position = sw;
                            }
                            catch { try { unit.transform.position = sw; } catch { /* ignore */ } }
                            try { unit.transform.rotation = Quaternion.Euler(0f, pose.FacingAngle, 0f); }
                            catch { /* ignore */ }
                            track.StartYaw = pose.FacingAngle;
                        }
                        else
                        {
                            track.StartYaw = unit.transform.rotation.eulerAngles.y;
                        }
                        try { track.StartWorld = unit.transform.position; }
                        catch { track.StartWorld = sw; }
                        if (endPose != null)
                        {
                            track.EndQ = endPose.Q;
                            track.EndR = endPose.R;
                            if (TryHexWorld(grid, endPose.Q, endPose.R, out var ew)) track.EndWorld = ew;
                            else track.EndWorld = track.StartWorld;
                            track.EndYaw = endPose.FacingAngle;
                        }
                        else
                        {
                            track.EndWorld = track.StartWorld;
                            track.EndYaw = track.StartYaw;
                        }
                        tracks.Add(track);
                    }
                }
                // Finisseurs seuls en END (sans START) : interpolation depuis la
                // position actuelle, ancrage logique en fin de plan.
                var endOnly = new List<ShotActorTrack>();
                if (shot.EndPoses != null)
                {
                    for (int p = 0; p < shot.EndPoses.Count; p++)
                    {
                        var pose = shot.EndPoses[p];
                        if (pose == null || string.IsNullOrWhiteSpace(pose.ActorId)) continue;
                        var unit = FindSceneUnit(pose.ActorId);
                        if (unit == null || IsUnitTracked(tracks, endOnly, unit)) continue;
                        unit.CinematicDriveActive = true;
                        var track = new ShotActorTrack { Unit = unit, HasEnd = true, EndQ = pose.Q, EndR = pose.R };
                        try
                        {
                            track.StartWorld = unit.transform.position;
                            track.StartYaw = unit.transform.rotation.eulerAngles.y;
                        }
                        catch { track.StartWorld = Vector3.zero; track.StartYaw = 0f; }
                        if (TryHexWorld(grid, pose.Q, pose.R, out var ew)) track.EndWorld = ew;
                        else track.EndWorld = track.StartWorld;
                        track.EndYaw = pose.FacingAngle;
                        endOnly.Add(track);
                    }
                }
                // Props (interactables) : Relocate logique+visuel au START, interpolation
                // visuelle pendant le plan, Relocate d'ancrage au END (CanInteract suit).
                var propTracks = new List<ShotPropTrack>();
                if (shot.StartProps != null)
                {
                    for (int p = 0; p < shot.StartProps.Count; p++)
                    {
                        var pose = shot.StartProps[p];
                        if (pose == null || string.IsNullOrWhiteSpace(pose.InteractableId)) continue;
                        var prop = FindSceneProp(pose.InteractableId);
                        if (prop == null || IsPropTracked(propTracks, prop)) continue;
                        var track = new ShotPropTrack { Prop = prop, HasEnd = false };
                        if (grid != null)
                        {
                            // Sans grille on ne touche à rien (pas de monde hex de référence).
                            try { prop.Relocate(new HexCoordinates(pose.Q, pose.R), grid); }
                            catch { /* ignore */ }
                        }
                        try { track.StartWorld = prop.transform.position; }
                        catch { track.StartWorld = Vector3.zero; }
                        SceneCinematicPropPose endPose = null;
                        if (shot.EndProps != null)
                            endPose = shot.EndProps.Find(e => e != null && string.Equals(e.InteractableId, pose.InteractableId, StringComparison.OrdinalIgnoreCase));
                        if (endPose != null)
                        {
                            track.EndQ = endPose.Q;
                            track.EndR = endPose.R;
                            if (TryHexWorld(grid, endPose.Q, endPose.R, out var ew))
                            {
                                track.EndWorld = ew;
                                track.HasEnd = true;
                            }
                            else track.EndWorld = track.StartWorld;
                        }
                        else track.EndWorld = track.StartWorld;
                        propTracks.Add(track);
                    }
                }

                // Focus + sous-titre du plan.
                if (!string.IsNullOrWhiteSpace(shot.FocusActorId))
                {
                    var focus = FindSceneUnit(shot.FocusActorId);
                    if (focus != null) handoverTarget = focus.transform;
                    try { focus?.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText($"🎬 {shot.Label}", Color.magenta); }
                    catch { /* ignore */ }
                }
                LogShotSpeech(shot, cine.Title);

                // Plan final : retrait anticipé des bandes SAUF si une suite Fin→
                // est déclarée — elle part dans le callback de fin (chaîne), pas
                // dans la file : sans ce garde, les bandes sortent à la fin de
                // chaque chaînée puis re-rentrent au début de la suivante (blink),
                // même si les deux les veulent. Si la cible est invalide/absente,
                // la sortie différée de fin s'en charge (aucun blink : rien ne suit).
                bool hasDeclaredSuccessor = cine != null && !string.IsNullOrWhiteSpace(cine.NextTargetId);
                bool isFinalShot = (s == shots.Count - 1) && (_sceneCinematicQueue.Count == 0) && !hasDeclaredSuccessor;
                bool shotLetterbox = (cine == null || cine.Letterbox) && shot.Letterbox;
                _letterboxActive = shotLetterbox;

                float retractStartTime = isFinalShot && shotLetterbox
                    ? Mathf.Max(0f, duration - _letterboxExitDuration)
                    : float.MaxValue;
                bool exitRetractTriggered = false;

                // Cadrage effectif du plan : absolu par défaut, ou dérivé de la scène live.
                // - TrackFocusActor : framing calculé sur la position LIVE de FocusActorId
                //   (miroir TacticalCameraController : pivot au sol + 0.6m, pitch/yaw/dist).
                // - RelativeToCurrent : START = caméra live, END = START + (End − Start).
                // Le tracking est prioritaire si les deux sont cochés. Les valeurs du
                // shot ne sont JAMAIS mutées (copies locales : l'éditeur garde ses données).
                Vector3 effStartPos = shot.CamStartPos;
                Vector3 effStartEuler = shot.CamStartEuler;
                float effStartFov = shot.CamStartFov;
                Vector3 effEndPos = shot.CamEndPos;
                Vector3 effEndEuler = shot.CamEndEuler;
                float effEndFov = shot.CamEndFov;
                if (cine.InPlace)
                {
                    effStartPos = livePrePos;
                    effStartEuler = livePreRot.eulerAngles;
                    effStartFov = livePreFov;
                    effEndPos = livePrePos;
                    effEndEuler = livePreRot.eulerAngles;
                    effEndFov = livePreFov;
                }
                else if (shot.TrackFocusActor && !string.IsNullOrWhiteSpace(shot.FocusActorId))
                {
                    var tracked = FindSceneUnit(shot.FocusActorId);
                    if (tracked != null)
                    {
                        try
                        {
                            Vector3 anchor = new Vector3(tracked.transform.position.x, 0f, tracked.transform.position.z) + new Vector3(0f, 0.6f, 0f);
                            float dist = Mathf.Clamp(shot.TrackDistance, 0.5f, 35f);
                            Quaternion sRot = Quaternion.Euler(shot.CamStartEuler.x, shot.TrackYaw, shot.CamStartEuler.z);
                            Quaternion eRot = Quaternion.Euler(shot.CamEndEuler.x, shot.TrackYaw, shot.CamEndEuler.z);
                            effStartPos = anchor - (sRot * Vector3.forward) * dist;
                            effEndPos = anchor - (eRot * Vector3.forward) * dist;
                            if (effStartPos.y < 0.15f) effStartPos.y = 0.15f;
                            if (effEndPos.y < 0.15f) effEndPos.y = 0.15f;
                            effStartEuler = new Vector3(shot.CamStartEuler.x, shot.TrackYaw, shot.CamStartEuler.z);
                            effEndEuler = new Vector3(shot.CamEndEuler.x, shot.TrackYaw, shot.CamEndEuler.z);
                        }
                        catch { /* ignore : repli sur l'absolu */ }
                    }
                }
                else if (shot.RelativeToCurrent)
                {
                    Vector3 livePos = transform.position;
                    Vector3 liveEuler = transform.rotation.eulerAngles;
                    float liveFov = _mainCamera != null ? _mainCamera.fieldOfView : _normalFOV;
                    effStartPos = livePos;
                    effStartEuler = liveEuler;
                    effStartFov = liveFov;
                    effEndPos = livePos + (shot.CamEndPos - shot.CamStartPos);
                    effEndEuler = liveEuler + (shot.CamEndEuler - shot.CamStartEuler);
                    effEndFov = liveFov + (shot.CamEndFov - shot.CamStartFov);
                }

                Vector3 camPos = driveCamera ? effStartPos : Vector3.zero;
                Vector3 camEuler = driveCamera ? effStartEuler : Vector3.zero;
                if (driveCamera)
                {
                    transform.position = effStartPos;
                    transform.rotation = Quaternion.Euler(effStartEuler);
                    if (_mainCamera != null) _mainCamera.fieldOfView = effStartFov;
                }

                float startDelay = Mathf.Max(0f, shot.StartDelay / speed);
                bool wasPaused = CombatHUD.IsPaused;

                // Warp supraluminique : le plan démarre à vitesse lumière (point central).
                if (shot.SuperluminalWarp)
                {
                    HoldShotWarp(shot);
                    Debug.Log("[CINE] v14 warp=1 plan " + shot.ShotId + " (déjà contracté)");
                }

                if (startDelay > 0.001f)
                {
                    float delayElapsed = 0f;
                    while (delayElapsed < startDelay)
                    {
                        if (CombatHUD.IsPaused) { wasPaused = true; yield return WaitWhilePaused(); }
                        bool pausedNow = CombatHUD.IsPaused;
                        delayElapsed += Mathf.Min(Time.unscaledDeltaTime, 0.1f);
                        HoldShotWarp(shot);
                        if (driveCamera)
                        {
                            Vector3 waitPos = effStartPos;
                            Vector3 waitEuler = effStartEuler;
                            float waitFov = effStartFov;
                            ApplyShotEffect(shot, 0f, 0f, ref waitPos, ref waitEuler, ref waitFov);
                            transform.position = waitPos;
                            transform.rotation = Quaternion.Euler(waitEuler);
                            if (_mainCamera != null) _mainCamera.fieldOfView = waitFov;
                            UpdateAdaptiveNear(waitPos);
                        }
                        if (ShouldSkipShot(cine, pausedNow, wasPaused))
                        {
                            skipped = true;
                            LogShotSkipped();
                            break;
                        }
                        wasPaused = pausedNow;
                        yield return null;
                    }
                }

                if (CombatHUD.IsPaused) { wasPaused = true; yield return WaitWhilePaused(); }

                PlayShotAudioIfCued(shot);

                float elapsed = 0f;
                while (!skipped && elapsed < duration)
                {
                    if (CombatHUD.IsPaused)
                    {
                        wasPaused = true;
                        PauseShotAudio();
                        yield return WaitWhilePaused();
                        ResumeShotAudio();
                    }
                    bool pausedNow = CombatHUD.IsPaused;
                    // Temps borné : un hitch (surtout à l'entrée de scène / Deploy :
                    // construction d'environnement, compilation shaders) fait bondir
                    // unscaledDeltaTime de plusieurs secondes et AVALE la cinématique
                    // (snap + retour instantané). Borné à 0.1s : la ciné dure sa vraie
                    // durée même en cas de rame (quitte à durer plus en temps mural).
                    AdvanceShotClock(ref elapsed, duration, shot.Ease, out float t, out float e);

                    // Warp v2 : palier noir+1px (temps dilaté) puis le point
                    // grossit → stries de ralentissement → voûte normale.
                    ProgressShotWarp(shot, t);

                    if (!exitRetractTriggered && elapsed >= retractStartTime)
                    {
                        exitRetractTriggered = true;
                        _letterboxActive = false;
                    }

                    if (driveCamera)
                    {
                        camPos = Vector3.Lerp(effStartPos, effEndPos, e);
                        camEuler = new Vector3(
                            Mathf.LerpAngle(effStartEuler.x, effEndEuler.x, e),
                            Mathf.LerpAngle(effStartEuler.y, effEndEuler.y, e),
                            Mathf.Lerp(effStartEuler.z, effEndEuler.z, e));
                        float fov = Mathf.Lerp(effStartFov, effEndFov, e);
                        ApplyShotEffect(shot, t, e, ref camPos, ref camEuler, ref fov);

                        // Injection additive de toute secousse concurrente (ex: focus Erika + secousse active)
                        camPos += _shakeOffsetPos;
                        camEuler += _shakeOffsetEuler;
                        fov += _shakeOffsetFov;

                        Transform camTrans = GetCameraTransform();
                        camTrans.position = camPos;
                        camTrans.rotation = Quaternion.Euler(camEuler);
                        if (_mainCamera != null) _mainCamera.fieldOfView = fov;
                        UpdateAdaptiveNear(camPos);
                    }

                    for (int t2 = 0; t2 < tracks.Count; t2++)
                    {
                        var tr = tracks[t2];
                        if (tr.Unit == null) continue;
                        try
                        {
                            tr.Unit.transform.position = Vector3.Lerp(tr.StartWorld, tr.EndWorld, e);
                            tr.Unit.transform.rotation = Quaternion.Euler(0f, Mathf.LerpAngle(tr.StartYaw, tr.EndYaw, e), 0f);
                        }
                        catch { /* ignore */ }
                    }
                    for (int t2 = 0; t2 < endOnly.Count; t2++)
                    {
                        var tr = endOnly[t2];
                        if (tr.Unit == null) continue;
                        try
                        {
                            tr.Unit.transform.position = Vector3.Lerp(tr.StartWorld, tr.EndWorld, e);
                            tr.Unit.transform.rotation = Quaternion.Euler(0f, Mathf.LerpAngle(tr.StartYaw, tr.EndYaw, e), 0f);
                        }
                        catch { /* ignore */ }
                    }
                    for (int t2 = 0; t2 < propTracks.Count; t2++)
                    {
                        var tr = propTracks[t2];
                        if (tr.Prop == null || !tr.HasEnd) continue;
                        try { tr.Prop.transform.position = Vector3.Lerp(tr.StartWorld, tr.EndWorld, e); }
                        catch { /* ignore */ }
                    }

                    // Skip Escape : seulement hors pause et hors transition de pause
                    // (sinon "reprendre" = "skipper", le symptôme "ça skip tout seul").
                    if (ShouldSkipShot(cine, pausedNow, wasPaused))
                    {
                        skipped = true;
                        LogShotSkipped();
                        break;
                    }
                    wasPaused = pausedNow;
                    yield return null;
                }

                // Finalisation du plan : ancrage logique END (occupancy grille à jour),
                // libération systématique du verrou anti-déplacement. En cas d'échec
                // de téléport (case occupée), le visuel est recalé sur la logique
                // pour ne jamais laisser visuel/logique désynchronisés.
                for (int t2 = 0; t2 < tracks.Count; t2++)
                {
                    var tr = tracks[t2];
                    if (tr.Unit == null) continue;
                    try
                    {
                        if (tr.HasEnd)
                        {
                            // Sans grille il n'y a pas de logique hex à ancrer : le visuel fait foi.
                            bool ok = grid == null;
                            if (grid != null) ok = tr.Unit.TeleportTo(new HexCoordinates(tr.EndQ, tr.EndR), grid);
                            else tr.Unit.transform.position = tr.EndWorld;
                            if (!ok)
                            {
                                var n = grid != null ? grid.GetNode(tr.Unit.CurrentCoords) : null;
                                if (n != null) tr.Unit.transform.position = n.WorldPosition;
                            }
                            tr.Unit.transform.rotation = Quaternion.Euler(0f, tr.EndYaw, 0f);
                        }
                        tr.Unit.CinematicDriveActive = false;
                    }
                    catch
                    {
                        try { if (tr.Unit != null) tr.Unit.CinematicDriveActive = false; }
                        catch { /* ignore */ }
                    }
                }
                for (int t2 = 0; t2 < endOnly.Count; t2++)
                {
                    var tr = endOnly[t2];
                    if (tr.Unit == null) continue;
                    try
                    {
                        bool ok = grid == null;
                        if (grid != null) ok = tr.Unit.TeleportTo(new HexCoordinates(tr.EndQ, tr.EndR), grid);
                        else tr.Unit.transform.position = tr.EndWorld;
                        if (!ok)
                        {
                            var n = grid != null ? grid.GetNode(tr.Unit.CurrentCoords) : null;
                            if (n != null) tr.Unit.transform.position = n.WorldPosition;
                        }
                        tr.Unit.transform.rotation = Quaternion.Euler(0f, tr.EndYaw, 0f);
                        tr.Unit.CinematicDriveActive = false;
                    }
                    catch
                    {
                        try { if (tr.Unit != null) tr.Unit.CinematicDriveActive = false; }
                        catch { /* ignore */ }
                    }
                }
                for (int t2 = 0; t2 < propTracks.Count; t2++)
                {
                    var tr = propTracks[t2];
                    if (tr.Prop == null || !tr.HasEnd) continue;
                    try { tr.Prop.Relocate(new HexCoordinates(tr.EndQ, tr.EndR), grid); }
                    catch { /* ignore */ }
                }

                // Fin du plan : voûte reformée (warp retombe à 0, même si skip).
                TeardownShotWarp(shot);

                if (skipped) break;
            }

            // Sortie DIFFÉRÉE (whiplash inter-cinématiques) : la teardown de fin
            // (retrait letterbox + transition de sortie + retour tactique) ne doit
            // jouer que si RIEN ne suit. Or la suite Fin→ part dans le callback
            // ci-dessous — l'ancienne variable file-seule ne la voyait pas. On décide
            // donc APRÈS le callback : si une suite a démarré (chaîne ou file), on garde
            // tout tel quel, la suivante prend le relais depuis la pose live.
            ClearAllCinematicLocks();

            ResetSkyboxWarp();
            _activeSceneCinematicRoutine = null;
            CurrentPlayingSceneCinematic = null;
            var cb = _sceneCinematicOnComplete;
            _sceneCinematicOnComplete = null;
            try { cb?.Invoke(); } catch { /* ignore */ }

            if (_sceneCinematicQueue.Count > 0) PlayNextQueuedCinematic();

            // Une suite a démarré dans le callback (chaîne Fin→) ou attend en file :
            // PAS de teardown (sinon : glide vers la pose tactique/nœud puis
            // re-swoosh + letterbox qui clignote entre les deux chaînées).
            bool followed = IsPlayingSceneCinematic || _sceneCinematicQueue.Count > 0;

            if (skipped)
            {
                // Skip + suite chaînée : on garde les bandes si la suivante les
                // veut aussi (pas de blink) ; sinon la suivante les rétracte seule.
                if (!followed)
                {
                    _letterboxActive = false;
                    _letterboxProgress = 0f;
                }
            }
            else if (!followed)
            {
                _letterboxActive = false;
                while (_letterboxProgress > 0.001f)
                {
                    yield return null;
                }
                _letterboxProgress = 0f;
            }

            // Si RIEN ne suit, on rend la main (évite un double pop quand ça enchaîne).
            if (!followed)
            {
                if (!IsSwellPlaying())
                {
                    _swellSequenceConsumed = false;
                }
                if (_activeAudioLimitRoutine != null)
                {
                    StopCoroutine(_activeAudioLimitRoutine);
                    _activeAudioLimitRoutine = null;
                }
                if (_activeShotAudioSource != null)
                {
                    // Ne jamais couper un swell dont la durée n'est pas achevée
                    if (!IsSwellCue(_activeShotSoundCueId))
                    {
                        _activeShotAudioSource.Stop();
                    }
                    _activeShotAudioSource = null;
                    _activeShotSoundCueId = "";
                }
            }

            // Si RIEN ne suit, on rend la main (évite un double pop quand ça enchaîne).
            if (driveCamera && !followed)
            {
                if (cine.InPlace)
                {
                    transform.position = livePrePos;
                    transform.rotation = livePreRot;
                    if (_mainCamera != null) _mainCamera.fieldOfView = livePreFov;
                }
                else if (cine.EndTransition == CinematicCameraTransition.Smooth && !skipped)
                {
                    Vector3 endFromPos = transform.position;
                    Quaternion endFromRot = transform.rotation;
                    float endFromFov = _mainCamera != null ? _mainCamera.fieldOfView : _normalFOV;

                    Vector3 targetTacticalPos = livePrePos;
                    if (hasPreFocus && handoverTarget != null)
                    {
                        targetTacticalPos += (handoverTarget.position - preFocusPos);
                    }

                    float endTransDur = Mathf.Max(0.05f, (cine.EndTransitionDuration > 0.01f ? cine.EndTransitionDuration : 0.5f) / speed);
                    float endTransElapsed = 0f;
                    while (endTransElapsed < endTransDur)
                    {
                        yield return WaitWhilePaused();
                        endTransElapsed += Mathf.Min(Time.unscaledDeltaTime, 0.1f);
                        float et = Mathf.Clamp01(endTransElapsed / endTransDur);
                        float ee = ApplyCinematicEase(CinematicEase.Smooth, et);
                        transform.position = Vector3.Lerp(endFromPos, targetTacticalPos, ee);
                        transform.rotation = Quaternion.Slerp(endFromRot, livePreRot, ee);
                        if (_mainCamera != null) _mainCamera.fieldOfView = Mathf.Lerp(endFromFov, _normalFOV, ee);
                        UpdateAdaptiveNear(transform.position);
                        if (ShouldSkipShot(cine)) break;
                        yield return null;
                    }
                }
                else
                {
                    if (_tacticalCam != null)
                    {
                        try { _tacticalCam.AdoptWorldPose(transform.position, transform.rotation.eulerAngles, handoverTarget); }
                        catch { /* ignore */ }
                    }
                }

                if (_tacticalCam != null) _tacticalCam.enabled = true;
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.ExitCinematicMode();
                Time.timeScale = DevTimeScale();
                CurrentMode = CameraMode.TacticalIsometric;
                // Retour au sol : near fixe 0.3 (le tactique travaille à 0.5-35m).
                ResetAdaptiveNear();
            }
        }

        private float GetSequenceTotalDuration(SceneCinematicData cine)
        {
            if (cine == null || cine.Shots == null) return 0f;
            float total = 0f;
            for (int i = 0; i < cine.Shots.Count; i++)
                if (cine.Shots[i] != null) total += Mathf.Max(0.1f, cine.Shots[i].Duration);

            if (!string.IsNullOrWhiteSpace(cine.NextTargetId))
            {
                var controller = FindAnyObjectByType<JsonStorySceneController>();
                var nextCine = controller != null && controller.SceneData != null ? controller.SceneData.FindCinematic(cine.NextTargetId) : null;
                if (nextCine != null && nextCine.Shots != null)
                {
                    for (int j = 0; j < nextCine.Shots.Count; j++)
                        if (nextCine.Shots[j] != null) total += Mathf.Max(0.1f, nextCine.Shots[j].Duration);
                }
            }
            return total;
        }

        private static bool IsSameSwellSequence(string currentId, string nextId)
        {
            if (string.IsNullOrWhiteSpace(currentId) || string.IsNullOrWhiteSpace(nextId)) return false;
            string a = currentId.Trim();
            string b = nextId.Trim();
            if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return true;

            bool isSwellA = string.Equals(a, "Cinematic_OpeningSwell", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(a, "HBO", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(a, "Cinematic_WarpSwell", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(a, "Cinematic_ApproachSwell", StringComparison.OrdinalIgnoreCase);

            bool isSwellB = string.Equals(b, "Cinematic_OpeningSwell", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(b, "HBO", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(b, "Cinematic_WarpSwell", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(b, "Cinematic_ApproachSwell", StringComparison.OrdinalIgnoreCase);

            return isSwellA && isSwellB;
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.UI;
using Killtime.Story.Data;
using Killtime.Tactics;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.CameraSystem;
using Killtime.Story.Scenes;

namespace Killtime.Story
{
    public partial class CinematicEditorDevWindow
    {
        // ================= Capture / Apply depuis la carte de combat =================

        private void CreateCinematic()
        {
            var list = Cines();
            if (list == null) return;
            int idx = list.Count + 1;
            var cine = new SceneCinematicData
            {
                CinematicId = $"cine_{idx}",
                Title = $"Cinématique {idx}",
                GraphPosX = 60f + list.Count * 400f,
                GraphPosY = 1950f
            };
            cine.Shots.Add(new SceneCinematicShotData { ShotId = "shot_1", Label = "Plan 1" });
            list.Add(cine);
            _cineIndex = list.Count - 1;
            _shotIndex = 0;
        }

        private void AddShot(SceneCinematicData cine)
        {
            if (cine == null) return;
            cine.Shots ??= new List<SceneCinematicShotData>();
            int ns = cine.Shots.Count + 1;
            cine.Shots.Add(new SceneCinematicShotData { ShotId = $"shot_{ns}", Label = $"Plan {ns}" });
            _shotIndex = cine.Shots.Count - 1;
        }

        private static string LogicalActorId(TacticalUnit u)
        {
            if (u == null) return "";
            try
            {
                string go = u.gameObject.name ?? "";
                if (go.StartsWith("Actor_", StringComparison.OrdinalIgnoreCase) && go.Length > 6)
                    return go.Substring(6);
                if (u.Stats != null && !string.IsNullOrWhiteSpace(u.Stats.Name)) return u.Stats.Name;
                return go;
            }
            catch { return ""; }
        }

        private string ResolvePropId(TacticalInteractable p)
        {
            if (p == null) return "";
            string objName = p.ObjectName ?? "";
            var data = Data;
            if (data != null && data.Interactables != null)
            {
                for (int i = 0; i < data.Interactables.Count; i++)
                {
                    var entry = data.Interactables[i];
                    if (entry == null) continue;
                    if (string.Equals(entry.DisplayName, objName, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(entry.InteractableId, objName, StringComparison.OrdinalIgnoreCase))
                        return entry.InteractableId;
                }
            }
            return objName;
        }

        private static TacticalUnit FindUnit(string actorIdOrName)
        {
            if (string.IsNullOrWhiteSpace(actorIdOrName)) return null;
            try
            {
                var units = TacticalUnitRegistry.AllUnits;
                for (int i = 0; i < units.Count; i++)
                {
                    var u = units[i];
                    if (u == null) continue;
                    if (string.Equals(u.gameObject.name, "Actor_" + actorIdOrName, StringComparison.OrdinalIgnoreCase))
                        return u;
                    if (u.Stats != null && string.Equals(u.Stats.Name, actorIdOrName, StringComparison.OrdinalIgnoreCase))
                        return u;
                }
            }
            catch { /* ignore */ }
            return null;
        }

        private static bool TryHexWorld(TacticalHexGrid grid, int q, int r, out Vector3 world)
        {
            world = Vector3.zero;
            if (grid == null) return false;
            try
            {
                var node = grid.GetNode(new HexCoordinates(q, r));
                if (node == null) return false;
                world = node.WorldPosition;
                return true;
            }
            catch { return false; }
        }

        /// <summary>Capture caméra + pions depuis la carte de combat vers START ou END du plan.</summary>
        private void CaptureShot(bool isStart)
        {
            var shot = CurrentShot();
            if (shot == null) return;
            var cd = FindAnyObjectByType<CinematicDirector>();
            if (cd != null && cd.CaptureCameraState(out var pos, out var euler, out var fov))
            {
                if (isStart) { shot.CamStartPos = pos; shot.CamStartEuler = euler; shot.CamStartFov = fov; }
                else { shot.CamEndPos = pos; shot.CamEndEuler = euler; shot.CamEndFov = fov; }
            }
            CapturePoses(isStart);
            LogHud($"📸 Plan <b>{shot.Label}</b> : {(isStart ? "START" : "END")} capturé (caméra + {shot.StartPoses.Count + shot.EndPoses.Count} poses).");
        }

        private void CapturePoses(bool isStart)
        {
            var shot = CurrentShot();
            if (shot == null) return;
            var grid = FindAnyObjectByType<TacticalHexGrid>();

            var actorPoses = new List<SceneCinematicActorPose>();
            try
            {
                var units = TacticalUnitRegistry.AllUnits;
                for (int i = 0; i < units.Count; i++)
                {
                    var u = units[i];
                    if (u == null) continue;
                    string logical = LogicalActorId(u);
                    if (string.IsNullOrWhiteSpace(logical)) continue;
                    int q = u.CurrentCoords.Q;
                    int r = u.CurrentCoords.R;
                    if (grid != null)
                    {
                        try
                        {
                            if (grid.TryGetNodeAtWorldPosition(u.transform.position, out var node))
                            {
                                q = node.Coordinates.Q;
                                r = node.Coordinates.R;
                            }
                        }
                        catch { /* ignore */ }
                    }
                    actorPoses.Add(new SceneCinematicActorPose
                    {
                        ActorId = logical,
                        Q = q,
                        R = r,
                        FacingAngle = u.transform.rotation.eulerAngles.y
                    });
                }
            }
            catch { /* ignore */ }

            var propPoses = new List<SceneCinematicPropPose>();
            try
            {
                var props = FindObjectsByType<TacticalInteractable>();
                for (int i = 0; i < props.Length; i++)
                {
                    var p = props[i];
                    if (p == null) continue;
                    string id = ResolvePropId(p);
                    if (string.IsNullOrWhiteSpace(id)) continue;
                    int q = p.Coordinates.Q;
                    int r = p.Coordinates.R;
                    if (grid != null)
                    {
                        try
                        {
                            if (grid.TryGetNodeAtWorldPosition(p.transform.position, out var node))
                            {
                                q = node.Coordinates.Q;
                                r = node.Coordinates.R;
                            }
                        }
                        catch { /* ignore */ }
                    }
                    propPoses.Add(new SceneCinematicPropPose { InteractableId = id, Q = q, R = r });
                }
            }
            catch { /* ignore */ }

            if (isStart) { shot.StartPoses = actorPoses; shot.StartProps = propPoses; }
            else { shot.EndPoses = actorPoses; shot.EndProps = propPoses; }
        }

        /// <summary>Applique caméra + pions du START ou END sur la carte (prévisualisation figée).</summary>
        private void ApplyShot(bool isStart)
        {
            var shot = CurrentShot();
            if (shot == null) return;
            var cd = FindAnyObjectByType<CinematicDirector>();
            if (cd != null)
            {
                if (isStart) cd.ApplyCameraState(shot.CamStartPos, shot.CamStartEuler, shot.CamStartFov);
                else cd.ApplyCameraState(shot.CamEndPos, shot.CamEndEuler, shot.CamEndFov);
                cd.SetTacticalControlEnabled(false);
            }
            ApplyPoses(isStart);
        }

        private void ApplyPoses(bool isStart)
        {
            var shot = CurrentShot();
            if (shot == null) return;
            var grid = FindAnyObjectByType<TacticalHexGrid>();

            var actors = isStart ? shot.StartPoses : shot.EndPoses;
            if (actors != null)
            {
                for (int i = 0; i < actors.Count; i++)
                {
                    var pose = actors[i];
                    if (pose == null || string.IsNullOrWhiteSpace(pose.ActorId)) continue;
                    var unit = FindUnit(pose.ActorId);
                    if (unit == null) continue;
                    try
                    {
                        if (grid != null) unit.TeleportTo(new HexCoordinates(pose.Q, pose.R), grid);
                        else if (TryHexWorld(grid, pose.Q, pose.R, out var w)) unit.transform.position = w;
                        unit.transform.rotation = Quaternion.Euler(0f, pose.FacingAngle, 0f);
                    }
                    catch { /* ignore */ }
                }
            }

            var props = isStart ? shot.StartProps : shot.EndProps;
            if (props != null)
            {
                for (int i = 0; i < props.Count; i++)
                {
                    var pose = props[i];
                    if (pose == null || string.IsNullOrWhiteSpace(pose.InteractableId)) continue;
                    TacticalInteractable target = null;
                    try
                    {
                        var all = FindObjectsByType<TacticalInteractable>();
                        for (int k = 0; k < all.Length; k++)
                        {
                            if (all[k] == null) continue;
                            if (string.Equals(ResolvePropId(all[k]), pose.InteractableId, StringComparison.OrdinalIgnoreCase))
                            {
                                target = all[k];
                                break;
                            }
                        }
                    }
                    catch { /* ignore */ }
                    // Relocate = logique + visuel (CanInteract et bouton [E] suivent).
                    if (target != null)
                    {
                        try { target.Relocate(new HexCoordinates(pose.Q, pose.R), grid); }
                        catch { /* ignore */ }
                    }
                }
            }
            LogHud($"▶ {(isStart ? "START" : "END")} appliqué sur la carte.");
        }

        private static List<SceneCinematicActorPose> CloneActorPoses(List<SceneCinematicActorPose> src)
        {
            var dst = new List<SceneCinematicActorPose>();
            if (src == null) return dst;
            for (int i = 0; i < src.Count; i++)
            {
                var p = src[i];
                if (p == null) continue;
                dst.Add(new SceneCinematicActorPose { ActorId = p.ActorId, Q = p.Q, R = p.R, FacingAngle = p.FacingAngle });
            }
            return dst;
        }

        private static List<SceneCinematicPropPose> ClonePropPoses(List<SceneCinematicPropPose> src)
        {
            var dst = new List<SceneCinematicPropPose>();
            if (src == null) return dst;
            for (int i = 0; i < src.Count; i++)
            {
                var p = src[i];
                if (p == null) continue;
                dst.Add(new SceneCinematicPropPose { InteractableId = p.InteractableId, Q = p.Q, R = p.R });
            }
            return dst;
        }

        private void LogHud(string msg)
        {
            try { CombatHUD.Instance?.AddAdvancedLog(msg, LogCategory.MovementAndTurns, "[CINÉ-ÉDIT]", Color.magenta); }
            catch { /* ignore */ }
        }

        // Champ numérique tolérant la frappe (signe, décimales) + resync sur capture externe.
        private float NumField(string key, float v, float fieldW)
        {
            string lastKey = key + "#last";
            if (!_numBuf.TryGetValue(lastKey, out string lastTxt)
                || !float.TryParse(lastTxt, out float lastV)
                || !Mathf.Approximately(lastV, v))
            {
                _numBuf[key] = v.ToString("0.##");
                _numBuf[lastKey] = v.ToString("0.##");
            }
            string txt = GUILayout.TextField(_numBuf.TryGetValue(key, out string cur) ? cur : "", GUILayout.Width(fieldW));
            _numBuf[key] = txt;
            if (float.TryParse(txt, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsed)
                || float.TryParse(txt, out parsed))
            {
                _numBuf[lastKey] = parsed.ToString("0.##");
                return parsed;
            }
            return v;
        }
    }
}

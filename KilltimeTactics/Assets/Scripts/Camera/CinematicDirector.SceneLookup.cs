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

        private struct ShotActorTrack
        {
            public TacticalUnit Unit;
            public Vector3 StartWorld;
            public Vector3 EndWorld;
            public int EndQ;
            public int EndR;
            public float StartYaw;
            public float EndYaw;
            public bool HasEnd;
        }

        // Vrai si piloter notre transform déplace réellement une caméra (rig direct,
        // enfant, ou transform de la caméra principale). Sinon le travel est invisible.
        private bool IsCameraOnDrivenTransform()
        {
            try
            {
                if (GetComponent<UnityEngine.Camera>() != null) return true;
                if (GetComponentInChildren<UnityEngine.Camera>() != null) return true;
                var main = _mainCamera != null ? _mainCamera : UnityEngine.Camera.main;
                if (main != null && transform == main.transform) return true;
            }
            catch { /* ignore */ }
            return false;
        }

        // Sécurité : libère les verrous de pilotage sur toutes les unités (ex : Stop
        // en plein plan, changement de scène). Sans ça, une unité resterait
        // indéplaçable (MoveAlongPath refuse tant que CinematicDriveActive).
        private static void ClearAllCinematicLocks()
        {
            try
            {
                var units = TacticalUnitRegistry.AllUnits;
                for (int i = 0; i < units.Count; i++)
                    if (units[i] != null) units[i].CinematicDriveActive = false;
            }
            catch { /* ignore */ }
        }

        private struct ShotPropTrack
        {
            public TacticalInteractable Prop;
            public Vector3 StartWorld;
            public Vector3 EndWorld;
            public int EndQ;
            public int EndR;
            public bool HasEnd;
        }

        private static bool IsUnitTracked(List<ShotActorTrack> a, List<ShotActorTrack> b, TacticalUnit unit)
        {
            if (unit == null) return true;
            if (a != null)
                for (int i = 0; i < a.Count; i++)
                    if (a[i].Unit == unit) return true;
            if (b != null)
                for (int i = 0; i < b.Count; i++)
                    if (b[i].Unit == unit) return true;
            return false;
        }

        private static bool IsPropTracked(List<ShotPropTrack> list, TacticalInteractable prop)
        {
            if (prop == null) return true;
            if (list == null) return false;
            for (int i = 0; i < list.Count; i++)
                if (list[i].Prop == prop) return true;
            return false;
        }

        private static TacticalUnit FindSceneUnit(string actorIdOrName)
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

        private static TacticalInteractable FindSceneProp(string interactableId)
        {
            if (string.IsNullOrWhiteSpace(interactableId)) return null;
            try
            {
                var props = FindObjectsByType<TacticalInteractable>();
                for (int i = 0; i < props.Length; i++)
                {
                    var p = props[i];
                    if (p == null) continue;
                    if (string.Equals(p.ObjectName, interactableId, StringComparison.OrdinalIgnoreCase))
                        return p;
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
    }
}

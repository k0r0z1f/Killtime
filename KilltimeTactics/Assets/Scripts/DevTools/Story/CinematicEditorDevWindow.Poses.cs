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
        private void DrawPosesBlock(SceneCinematicShotData shot)
        {
            GUILayout.Space(4);
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("<b>👥 Pions START → END (acteurs + objets déplacés sur la carte 3D) :</b>");
            GUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.2f, 0.75f, 1f);
            if (GUILayout.Button("📸 Capturer START (pions)", GUILayout.Height(24))) CapturePoses(true);
            if (GUILayout.Button("📸 Capturer END (pions)", GUILayout.Height(24))) CapturePoses(false);
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.25f, 0.85f, 0.45f);
            if (GUILayout.Button("▶ Appliquer START", GUILayout.Height(22))) ApplyPoses(true);
            if (GUILayout.Button("▶ Appliquer END", GUILayout.Height(22))) ApplyPoses(false);
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Pions START → END", GUILayout.Height(22)))
            {
                shot.EndPoses = CloneActorPoses(shot.StartPoses);
                shot.EndProps = ClonePropPoses(shot.StartProps);
                LogHud("👥 Pions <b>START → END</b> copiés.");
            }
            if (GUILayout.Button("Pions END → START", GUILayout.Height(22)))
            {
                shot.StartPoses = CloneActorPoses(shot.EndPoses);
                shot.StartProps = ClonePropPoses(shot.EndProps);
                LogHud("👥 Pions <b>END → START</b> copiés.");
            }
            GUILayout.EndHorizontal();

            DrawPoseList("START — acteurs", shot.StartPoses, true, true);
            DrawPoseList("END — acteurs", shot.EndPoses, false, true);
            DrawPoseList("START — objets", shot.StartProps, true, false);
            DrawPoseList("END — objets", shot.EndProps, false, false);
            GUILayout.EndVertical();
        }

        private void DrawPoseList(string label, List<SceneCinematicActorPose> actors, bool isStart, bool isActor)
        {
            if (!isActor) return;
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<color=grey>{label} ({(actors != null ? actors.Count : 0)})</color>", GUILayout.ExpandWidth(true));
            if (actors != null && actors.Count > 0 && GUILayout.Button("Effacer", GUILayout.Width(70)))
                actors.Clear();
            GUILayout.EndHorizontal();
            if (actors == null) return;
            for (int i = 0; i < actors.Count; i++)
            {
                var p = actors[i];
                if (p == null) { actors.RemoveAt(i); i--; continue; }
                GUILayout.BeginHorizontal();
                GUILayout.Label($"• {p.ActorId}", GUILayout.Width(150));
                GUILayout.Label($"Q {p.Q}  R {p.R}  ∠{p.FacingAngle:0}°", GUILayout.ExpandWidth(true));
                GUI.backgroundColor = new Color(0.85f, 0.25f, 0.25f);
                if (GUILayout.Button("✕", GUILayout.Width(26))) { actors.RemoveAt(i); i--; }
                GUI.backgroundColor = Color.white;
                GUILayout.EndHorizontal();
            }
        }

        private void DrawPoseList(string label, List<SceneCinematicPropPose> props, bool isStart, bool isActor)
        {
            if (isActor) return;
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<color=grey>{label} ({(props != null ? props.Count : 0)})</color>", GUILayout.ExpandWidth(true));
            if (props != null && props.Count > 0 && GUILayout.Button("Effacer", GUILayout.Width(70)))
                props.Clear();
            GUILayout.EndHorizontal();
            if (props == null) return;
            for (int i = 0; i < props.Count; i++)
            {
                var p = props[i];
                if (p == null) { props.RemoveAt(i); i--; continue; }
                GUILayout.BeginHorizontal();
                GUILayout.Label($"• {p.InteractableId}", GUILayout.Width(150));
                GUILayout.Label($"Q {p.Q}  R {p.R}", GUILayout.ExpandWidth(true));
                GUI.backgroundColor = new Color(0.85f, 0.25f, 0.25f);
                if (GUILayout.Button("✕", GUILayout.Width(26))) { props.RemoveAt(i); i--; }
                GUI.backgroundColor = Color.white;
                GUILayout.EndHorizontal();
            }
        }

        private void DrawPreviewBlock(SceneCinematicData cine)
        {
            GUILayout.Space(4);
            GUILayout.BeginVertical(GUI.skin.box);
            var cd = FindAnyObjectByType<CinematicDirector>();
            bool playing = cd != null && cd.IsPlayingSceneCinematic;
            GUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(1f, 0.35f, 0.85f);
            GUI.enabled = !playing;
            if (GUILayout.Button(playing ? "▶ Lecture en cours…" : "▶ Prévisualiser la cinématique", GUILayout.Height(28)))
            {
                if (cine != null && cine.Shots != null && cine.Shots.Count > 0 && cine.Shots[0] != null && cine.Shots[0].SuperluminalWarp)
                {
                    SpaceEnvironment.SetWarpForAll(1f);
                }
                cd?.PlaySceneCinematic(cine);
            }
            GUI.enabled = true;
            GUI.backgroundColor = Color.white;
            if (GUILayout.Button("⏹ Stop", GUILayout.Width(80), GUILayout.Height(28)))
                cd?.StopSceneCinematic();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("🎥 Rendre la main caméra", GUILayout.Height(22)))
                cd?.ReleaseToTactical();
            GUI.backgroundColor = new Color(0.2f, 0.75f, 0.4f);
            if (GUILayout.Button("💾 Enregistrer la scène (JSON)", GUILayout.Height(22)))
            {
                Editor.SaveActiveScene();
                LogHud($"💾 Scène <b>{Data.Title}</b> enregistrée (cinématiques incluses).");
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();
            GUILayout.Label("<color=grey>💾 écrit le JSON via l'éditeur de scènes : trames, poses START/END et caméra persistées.</color>");
            GUILayout.EndVertical();
        }

    }
}

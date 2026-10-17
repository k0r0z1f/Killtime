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
        private void DrawSceneHeader(StorySceneData data)
        {
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label($"<b>🎬 Scène :</b> {data.Title} <color=grey>[{data.SceneId}]</color>");
            bool loaded = IsEditedSceneLoaded(out string loadedId);
            if (loaded)
                GUILayout.Label("<color=#7CFF9B><b>✔ Scène chargée sur la carte 3D — édition WYSIWYG.</b></color>");
            else if (!string.IsNullOrWhiteSpace(loadedId))
                GUILayout.Label($"<color=#FFC94D><b>⚠ Carte 3D : « {loadedId} » — pas la scène éditée.</b></color>");
            else
                GUILayout.Label("<color=#FFC94D><b>⚠ Aucune scène chargée sur la carte 3D.</b></color>");
            GUILayout.Label("<color=grey>1) Déplacez caméra + pions sur la carte 3D. 2) 📸 Capturez START [PgUp] / END [PgDn]. 3) ▶ Prévisualisez. 4) 💾 Enregistrez (JSON).</color>");
            GUILayout.EndVertical();
        }

        /// <summary>
        /// La scène éditée est-elle celle chargée sur la carte 3D live ?
        /// Condition du WYSIWYG : captures START/END et prévisualisation fidèles.
        /// </summary>
        private bool IsEditedSceneLoaded(out string loadedId)
        {
            loadedId = "";
            var data = Data;
            if (data == null || string.IsNullOrWhiteSpace(data.SceneId)) return false;
            StorySceneManager sm = null;
            try { sm = StorySceneManager.EnsureInstance(); }
            catch { return false; }
            if (sm == null) return false;
            loadedId = sm.ActiveScenarioId ?? "";
            if (string.IsNullOrWhiteSpace(loadedId)) return false;
            if (sm.CurrentSceneController == null) return false;
            return string.Equals(loadedId.Trim(), data.SceneId.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Confirmation de (re)chargement : si la scène éditée n'est pas chargée,
        /// propose de la déployer (version en cours d'édition, même non sauvegardée).
        /// Écartable ("Continuer sans charger") avec rappel persistant + bouton.
        /// </summary>
        private void DrawSceneLoadConfirmation(StorySceneData data)
        {
            if (data == null) return;
            if (IsEditedSceneLoaded(out _)) return;

            string editId = data.SceneId ?? "";
            bool dismissed = !string.IsNullOrWhiteSpace(_dismissedMismatchSceneId)
                && string.Equals(_dismissedMismatchSceneId.Trim(), editId.Trim(), StringComparison.OrdinalIgnoreCase);

            bool canDeploy = !string.IsNullOrWhiteSpace(editId);

            if (!dismissed)
            {
                GUILayout.Space(4);
                GUILayout.BeginVertical(GUI.skin.box);
                GUI.color = new Color(1f, 0.78f, 0.25f);
                GUILayout.Label("<b>⚠ SCÈNE NON CHARGÉE</b>");
                GUI.color = Color.white;
                GUILayout.Label($"La scène éditée <b>« {data.Title} » [{editId}]</b> n'est pas celle de la carte 3D. " +
                    "Les captures START/END et la prévisualisation travailleraient sur la <b>mauvaise carte</b> " +
                    "(mauvais pions, mauvaises cases).");
                if (!canDeploy)
                {
                    GUILayout.Label("<color=#FF8A8A>SceneId vide : définissez l'identifiant dans l'onglet « Scène & Fichier » de l'éditeur avant de charger.</color>");
                }
                GUILayout.BeginHorizontal();
                GUI.backgroundColor = new Color(1f, 0.55f, 0.1f);
                GUI.enabled = canDeploy;
                if (GUILayout.Button("🎬 Charger la scène de l'éditeur", GUILayout.Height(26)))
                {
                    _dismissedMismatchSceneId = "";
                    Editor.DeployActiveSceneToRuntime();
                    LogHud($"🎬 Scène <b>{data.Title}</b> déployée pour l'édition cinématique.");
                }
                GUI.enabled = true;
                GUI.backgroundColor = Color.white;
                if (GUILayout.Button("Continuer sans charger", GUILayout.Width(170), GUILayout.Height(26)))
                {
                    _dismissedMismatchSceneId = editId;
                }
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }
            else
            {
                // Rappel persistant : l'écart subsiste, le chargement reste à un clic.
                GUILayout.Space(4);
                GUILayout.BeginHorizontal(GUI.skin.box);
                GUILayout.Label("<color=#FFC94D><b>⚠ Carte 3D ≠ scène éditée</b> (captures non fidèles).</color>", GUILayout.ExpandWidth(true));
                GUI.backgroundColor = new Color(1f, 0.55f, 0.1f);
                GUI.enabled = canDeploy;
                if (GUILayout.Button("🎬 Charger", GUILayout.Width(110), GUILayout.Height(22)))
                {
                    _dismissedMismatchSceneId = "";
                    Editor.DeployActiveSceneToRuntime();
                    LogHud($"🎬 Scène <b>{data.Title}</b> déployée pour l'édition cinématique.");
                }
                GUI.enabled = true;
                GUI.backgroundColor = Color.white;
                GUILayout.EndHorizontal();
            }
        }

        private void DrawCineSection(StorySceneData data, SceneCinematicData cine)
        {
            var list = Cines();
            GUILayout.Space(4);
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("◀", GUILayout.Width(28)) && list.Count > 0)
                _cineIndex = (_cineIndex - 1 + list.Count) % list.Count;
            GUILayout.Label($"<b>Cinématique {_cineIndex + 1}/{list.Count}</b> — {cine.GetSummary()}", GUILayout.ExpandWidth(true));
            if (GUILayout.Button("▶", GUILayout.Width(28)) && list.Count > 0)
                _cineIndex = (_cineIndex + 1) % list.Count;
            GUI.backgroundColor = new Color(1f, 0.35f, 0.85f);
            if (GUILayout.Button("+ Nouvelle", GUILayout.Width(95))) CreateCinematic();
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("ID :", GUILayout.Width(30));
            cine.CinematicId = GUILayout.TextField(cine.CinematicId ?? "", GUILayout.Width(120));
            GUILayout.Label("Titre :", GUILayout.Width(40));
            cine.Title = GUILayout.TextField(cine.Title ?? "", GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            cine.Skippable = GUILayout.Toggle(cine.Skippable, "Esc = skip", GUILayout.Width(100));
            GUILayout.Label("Vitesse x", GUILayout.Width(70));
            cine.PlaybackSpeed = NumField("cine_speed", cine.PlaybackSpeed, 50f);
            cine.PlaybackSpeed = Mathf.Clamp(cine.PlaybackSpeed, 0.1f, 4f);
            GUILayout.FlexibleSpace();
            GUILayout.Label("Fin → :", GUILayout.Width(48));
            cine.NextTargetId = GUILayout.TextField(cine.NextTargetId ?? "", GUILayout.Width(110));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            cine.Letterbox = GUILayout.Toggle(cine.Letterbox, "Bandes noires (Letterbox)", GUILayout.Width(180));
            cine.HideSceneChat = GUILayout.Toggle(cine.HideSceneChat, "Masquer dialogue (chat de scène)", GUILayout.Width(220));
            cine.InPlace = GUILayout.Toggle(cine.InPlace, "Sur place (caméra fixe)", GUILayout.Width(170));
            GUILayout.EndHorizontal();

            if (!cine.InPlace)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Cam Début :", GUILayout.Width(80));
                cine.StartTransition = (CinematicCameraTransition)GUILayout.Toolbar((int)cine.StartTransition, new[] { "Téléportation", "Fluide" }, GUILayout.Width(170));
                if (cine.StartTransition == CinematicCameraTransition.Smooth)
                {
                    GUILayout.Label("Durée :", GUILayout.Width(45));
                    cine.StartTransitionDuration = NumField("cine_start_dur", cine.StartTransitionDuration, 45f);
                    cine.StartTransitionDuration = Mathf.Clamp(cine.StartTransitionDuration, 0.1f, 5f);
                    GUILayout.Label("s", GUILayout.Width(15));
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("Cam Fin :", GUILayout.Width(80));
                cine.EndTransition = (CinematicCameraTransition)GUILayout.Toolbar((int)cine.EndTransition, new[] { "Téléportation", "Fluide" }, GUILayout.Width(170));
                if (cine.EndTransition == CinematicCameraTransition.Smooth)
                {
                    GUILayout.Label("Durée :", GUILayout.Width(45));
                    cine.EndTransitionDuration = NumField("cine_end_dur", cine.EndTransitionDuration, 45f);
                    cine.EndTransitionDuration = Mathf.Clamp(cine.EndTransitionDuration, 0.1f, 5f);
                    GUILayout.Label("s", GUILayout.Width(15));
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }
            else
            {
                GUILayout.Label("<color=#FFC94D>📷 <b>Mode Sur Place actif :</b> La caméra reste sur sa position de vue actuelle (aucun voyage, secousse/effets appliqués directement).</color>");
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label("<color=grey>« Fin → » : carte enchaînée à la fin (ciné, réplique, conséquence, event, nœud). Vide = fin simple. Éditable aussi au wire depuis la carte graphe.</color>");
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.85f, 0.25f, 0.25f);
            if (GUILayout.Button("🗑 Supprimer", GUILayout.Width(100)))
            {
                string doomed = cine.CinematicId;
                list.RemoveAt(_cineIndex);
                _cineIndex = Mathf.Max(0, _cineIndex - 1);
                _shotIndex = 0;
                Editor.PurgeCinematicReferences(doomed);
                GUI.backgroundColor = Color.white;
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
                return;
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        private void DrawShotSection(SceneCinematicData cine, SceneCinematicShotData shot)
        {
            GUILayout.Space(4);
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("◀", GUILayout.Width(28)) && cine.Shots.Count > 0)
                _shotIndex = (_shotIndex - 1 + cine.Shots.Count) % cine.Shots.Count;
            GUILayout.Label($"<b>Plan {_shotIndex + 1}/{cine.Shots.Count}</b> — {shot.GetSummary()}", GUILayout.ExpandWidth(true));
            if (GUILayout.Button("▶", GUILayout.Width(28)) && cine.Shots.Count > 0)
                _shotIndex = (_shotIndex + 1) % cine.Shots.Count;
            GUI.backgroundColor = new Color(1f, 0.35f, 0.85f);
            if (GUILayout.Button("+ Plan", GUILayout.Width(70))) AddShot(cine);
            GUI.backgroundColor = new Color(0.85f, 0.25f, 0.25f);
            if (GUILayout.Button("✕", GUILayout.Width(28)))
            {
                cine.Shots.RemoveAt(_shotIndex);
                _shotIndex = Mathf.Max(0, _shotIndex - 1);
                GUI.backgroundColor = Color.white;
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
                return;
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Label :", GUILayout.Width(46));
            shot.Label = GUILayout.TextField(shot.Label ?? "", GUILayout.Width(110));
            GUILayout.Label("Durée s :", GUILayout.Width(54));
            shot.Duration = NumField($"shot_{_shotIndex}_dur", shot.Duration, 42f);
            shot.Duration = Mathf.Clamp(shot.Duration, 0.2f, 60f);
            GUILayout.Label("Délai s :", GUILayout.Width(50));
            shot.StartDelay = NumField($"shot_{_shotIndex}_delay", shot.StartDelay, 42f);
            shot.StartDelay = Mathf.Clamp(shot.StartDelay, 0f, 60f);
            shot.Letterbox = GUILayout.Toggle(shot.Letterbox, "Letterbox", GUILayout.Width(85));
            GUILayout.EndHorizontal();

            GUILayout.Label("<b>Voyage caméra (START → END automatique) :</b>");
            GUILayout.BeginHorizontal();
            bool newRel = GUILayout.Toggle(shot.RelativeToCurrent, "Relatif caméra live", GUILayout.Width(150));
            bool newTrk = GUILayout.Toggle(shot.TrackFocusActor, "Tracking acteur (Focus)", GUILayout.Width(170));
            // Mutuellement exclusifs : le dernier coché gagne (tracking prioritaire moteur).
            if (newRel != shot.RelativeToCurrent && newRel) { shot.RelativeToCurrent = true; shot.TrackFocusActor = false; }
            else if (newTrk != shot.TrackFocusActor && newTrk) { shot.TrackFocusActor = true; shot.RelativeToCurrent = false; }
            else { shot.RelativeToCurrent = newRel; shot.TrackFocusActor = newTrk; }
            GUILayout.EndHorizontal();
            if (shot.RelativeToCurrent)
                GUILayout.Label("<color=grey>START = caméra live, END = START + décalage (champs START/END ci-dessous).</color>");
            if (shot.TrackFocusActor)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Dist. suivi :", GUILayout.Width(80));
                shot.TrackDistance = NumField($"shot_{_shotIndex}_trkdist", shot.TrackDistance, 50f);
                shot.TrackDistance = Mathf.Clamp(shot.TrackDistance, 0.5f, 35f);
                GUILayout.Label("Yaw :", GUILayout.Width(36));
                shot.TrackYaw = NumField($"shot_{_shotIndex}_trkyaw", shot.TrackYaw, 50f);
                GUILayout.Label("<color=grey>(pitch = Rot.x, acteur = Focus)</color>", GUILayout.ExpandWidth(true));
                GUILayout.EndHorizontal();
            }
            GUILayout.Label("<color=grey>Ease :</color>");
            string[] easeNames = { "Lin", "Lisse", "In", "Out", "InOut", "Punch" };
            shot.Ease = (CinematicEase)GUILayout.Toolbar((int)shot.Ease, easeNames);
            GUILayout.Label("<color=grey>Effet de mouvement :</color>");
            string[] fxNames = { "Aucun", "Secoué", "Avancée", "Recul", "Orbite ◀", "Orbite ▶", "Punch FOV", "Roulis" };
            int fxCount = Enum.GetValues(typeof(CinematicCameraEffect)).Length;
            shot.MoveEffect = (CinematicCameraEffect)GUILayout.SelectionGrid((int)shot.MoveEffect, fxNames, 4);
            if ((int)shot.MoveEffect < 0 || (int)shot.MoveEffect >= fxCount) shot.MoveEffect = CinematicCameraEffect.None;

            if (shot.MoveEffect != CinematicCameraEffect.None)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Intensité effet :", GUILayout.Width(110));
                shot.ShakeIntensity = GUILayout.HorizontalSlider(Mathf.Clamp01(shot.ShakeIntensity / 0.6f), 0f, 1f, GUILayout.Width(160)) * 0.6f;
                GUILayout.Label(shot.ShakeIntensity.ToString("0.00"), GUILayout.Width(50));
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(4);
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("<b>☄ Voûte céleste :</b>");
            shot.SuperluminalWarp = GUILayout.Toggle(shot.SuperluminalWarp, "Effet supraluminique (noir+1px → pixel grossi → halo → voûte normale)", GUILayout.ExpandWidth(true));
            if (shot.SuperluminalWarp)
                GUILayout.Label("<color=grey>Actif sur ce plan, SANS déplacement caméra requis (fonctionne sur plan fixe / sur place : START=END accepté). Noir + 1px (20% début) puis pixel grossi → halo + stries → voûte reformée. Idéal : plan d'approche lointaine (ex : scène 01, 1000 km → base).</color>");
            else
                GUILayout.Label("<color=grey>Coché = noir + point central en début de plan, voûte normale en fin. Aucun voyage caméra requis.</color>");
            GUILayout.EndVertical();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Focus acteur :", GUILayout.Width(90));
            shot.FocusActorId = GUILayout.TextField(shot.FocusActorId ?? "", GUILayout.Width(130));
            GUILayout.Label("Son :", GUILayout.Width(36));
            shot.SoundCueId = GUILayout.TextField(shot.SoundCueId ?? "", GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Voix :", GUILayout.Width(50));
            shot.SpeakerId = GUILayout.TextField(shot.SpeakerId ?? "", GUILayout.Width(130));
            GUILayout.EndHorizontal();
            GUILayout.Label("Sous-titre du plan :");
            shot.Speech = GUILayout.TextField(shot.Speech ?? "", GUILayout.Height(36));
            GUILayout.EndVertical();
        }

    }
}

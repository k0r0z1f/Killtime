using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.UI;
using Killtime.Story.Data;
using Killtime.Tactics;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.CameraSystem;

namespace Killtime.Story
{
    /// <summary>
    /// Mode éditeur de cinématique : s'ouvre depuis l'éditeur de scènes (bouton
    /// "🎬 Édition Ciné" ou "◉/⤴ Éditer" des cartes 🎬) et travaille DIRECTEMENT
    /// sur la carte de combat : on déplace la caméra, les acteurs et les objets,
    /// on capture les START/END des plans, on prévisualise, et on sauvegarde les
    /// trames dans le JSON de la scène (bouton 💾).
    /// Lecture runtime fire-and-forget : une cinématique ne bloque jamais la carte suivante.
    /// </summary>
    public class CinematicEditorDevWindow : FloatingWindow<CinematicEditorDevWindow>
    {
        protected override int WindowId => 994;
        protected override string Title => "Éditeur de Cinématiques 🎬 — carte 3D (START / END)";
        protected override Vector2 MinSize => new Vector2(560f, 600f);
        protected override Rect DefaultRect => new Rect(60f, 60f, 640f, 780f);
        protected override KeyCode[] ToggleKeys => null;

        private Vector2 _scroll;
        private int _cineIndex;
        private int _shotIndex;
        // Buffers de saisie numérique (permet de taper "-", ".", etc. sans écrasement).
        private readonly Dictionary<string, string> _numBuf = new();
        // Confirmation de chargement : mémorise le SceneId pour lequel l'avertissement
        // "scène non chargée" a été écarté (on ne renague que si la scène éditée change).
        private string _dismissedMismatchSceneId = "";

        private ScenarioEditorDevWindow Editor => ScenarioEditorDevWindow.Instance;
        private StorySceneData Data => Editor != null ? Editor.ActiveSceneData : null;

        public void SelectCinematic(string cinematicId, int shotIndex = 0)
        {
            var data = Data;
            if (data == null || data.Cinematics == null) return;
            for (int i = 0; i < data.Cinematics.Count; i++)
            {
                if (data.Cinematics[i] != null && string.Equals(data.Cinematics[i].CinematicId, cinematicId, StringComparison.OrdinalIgnoreCase))
                {
                    _cineIndex = i;
                    _shotIndex = Mathf.Max(0, shotIndex);
                    return;
                }
            }
        }

        protected override void OnClosed()
        {
            // Sécurité : ne jamais laisser la caméra tactique coupée en fermant.
            try { FindAnyObjectByType<CinematicDirector>()?.ReleaseToTactical(); }
            catch { /* ignore */ }
        }

        private List<SceneCinematicData> Cines()
        {
            var data = Data;
            if (data == null) return null;
            data.Cinematics ??= new List<SceneCinematicData>();
            return data.Cinematics;
        }

        private SceneCinematicData CurrentCine()
        {
            var list = Cines();
            if (list == null || list.Count == 0) return null;
            _cineIndex = Mathf.Clamp(_cineIndex, 0, list.Count - 1);
            return list[_cineIndex];
        }

        private SceneCinematicShotData CurrentShot()
        {
            var cine = CurrentCine();
            if (cine == null) return null;
            cine.Shots ??= new List<SceneCinematicShotData>();
            if (cine.Shots.Count == 0) return null;
            _shotIndex = Mathf.Clamp(_shotIndex, 0, cine.Shots.Count - 1);
            return cine.Shots[_shotIndex];
        }

        protected override void DrawContent()
        {
            var editor = Editor;
            var data = Data;
            if (editor == null || data == null)
            {
                GUILayout.Space(8);
                GUILayout.Label("<b>Ouvrez d'abord l'éditeur de scènes (F8)</b> : le mode cinématique travaille sur sa scène active.");
                return;
            }

            _scroll = GUILayout.BeginScrollView(_scroll);
            DrawSceneHeader(data);
            // La capture START/END et la prévisualisation travaillent sur la carte 3D
            // LIVE : si la scène éditée n'y est pas chargée, on demande confirmation
            // pour la déployer avant toute édition de trames.
            DrawSceneLoadConfirmation(data);
            var cine = CurrentCine();
            if (cine == null)
            {
                GUILayout.Space(6);
                GUILayout.Label("<color=grey>(Aucune cinématique — créez-en une avec « + Cinéma » dans l'éditeur de scènes.)</color>");
                if (GUILayout.Button("+ Nouvelle cinématique", GUILayout.Height(26))) CreateCinematic();
                GUILayout.EndScrollView();
                return;
            }

            DrawCineSection(data, cine);
            var shot = CurrentShot();
            if (shot == null)
            {
                GUILayout.Space(6);
                GUILayout.Label("<color=grey>(Aucun plan — ajoutez un plan.)</color>");
                if (GUILayout.Button("+ Plan", GUILayout.Height(24))) AddShot(cine);
            }
            else
            {
                DrawShotSection(cine, shot);
                DrawCameraBlocks(shot);
                DrawPosesBlock(shot);
            }

            DrawPreviewBlock(cine);
            GUILayout.EndScrollView();
        }

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
            GUILayout.Label("<color=grey>1) Déplacez caméra + pions sur la carte 3D. 2) 📸 Capturez START/END. 3) ▶ Prévisualisez. 4) 💾 Enregistrez (JSON).</color>");
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
            GUILayout.Label("Label :", GUILayout.Width(50));
            shot.Label = GUILayout.TextField(shot.Label ?? "", GUILayout.Width(150));
            GUILayout.Label("Durée s :", GUILayout.Width(60));
            shot.Duration = NumField($"shot_{_shotIndex}_dur", shot.Duration, 50f);
            shot.Duration = Mathf.Clamp(shot.Duration, 0.2f, 60f);
            shot.Letterbox = GUILayout.Toggle(shot.Letterbox, "Letterbox", GUILayout.Width(90));
            GUILayout.EndHorizontal();

            GUILayout.Label("<b>Voyage caméra (START → END automatique) :</b>");
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

        private void DrawCameraBlocks(SceneCinematicShotData shot)
        {
            GUILayout.Space(4);
            GUILayout.BeginVertical(GUI.skin.box);
            GUILayout.Label("<b>📷 Caméra START → END (voyage automatique pendant le plan) :</b>");
            try
            {
                var tcc = FindAnyObjectByType<TacticalCameraController>();
                if (tcc != null && !tcc.enabled)
                    GUILayout.Label("<color=yellow>🎥 Caméra tactique suspendue (« ▶ Voir ») — bouton « 🎥 Rendre la main caméra » pour naviguer à nouveau.</color>");
            }
            catch { /* ignore */ }
            DrawCameraStateRow("START", true, shot, ref shot.CamStartPos, ref shot.CamStartEuler, ref shot.CamStartFov);
            DrawCameraStateRow("END", false, shot, ref shot.CamEndPos, ref shot.CamEndEuler, ref shot.CamEndFov);
            GUILayout.EndVertical();
        }

        private void DrawCameraStateRow(string tag, bool isStart, SceneCinematicShotData shot, ref Vector3 pos, ref Vector3 euler, ref float fov)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"<b>{tag}</b>", GUILayout.Width(48));
            GUI.backgroundColor = new Color(0.2f, 0.75f, 1f);
            if (GUILayout.Button("📸 Capturer", GUILayout.Width(95))) CaptureShot(isStart);
            GUI.backgroundColor = new Color(0.25f, 0.85f, 0.45f);
            if (GUILayout.Button("▶ Voir", GUILayout.Width(60))) ApplyShot(isStart);
            GUI.backgroundColor = Color.white;
            GUILayout.Label($"<color=grey>({pos.x:0.0}, {pos.y:0.0}, {pos.z:0.0}) ∠({euler.x:0}, {euler.y:0}, {euler.z:0}) fov {fov:0}</color>");
            GUILayout.EndHorizontal();

            string key = $"shot_{_shotIndex}_{(isStart ? "s" : "e")}";
            GUILayout.BeginHorizontal();
            GUILayout.Label("Pos :", GUILayout.Width(36));
            pos.x = NumField(key + "_px", pos.x, 56f);
            pos.y = NumField(key + "_py", pos.y, 56f);
            pos.z = NumField(key + "_pz", pos.z, 56f);
            GUILayout.Label("Rot :", GUILayout.Width(36));
            euler.x = NumField(key + "_rx", euler.x, 50f);
            euler.y = NumField(key + "_ry", euler.y, 50f);
            euler.z = NumField(key + "_rz", euler.z, 50f);
            GUILayout.Label("Fov :", GUILayout.Width(34));
            fov = NumField(key + "_fov", fov, 50f);
            fov = Mathf.Clamp(fov, 10f, 100f);
            GUILayout.EndHorizontal();
        }

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
                cd?.PlaySceneCinematic(cine);
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
                var units = FindObjectsByType<TacticalUnit>();
                for (int i = 0; i < units.Length; i++)
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
                var units = FindObjectsByType<TacticalUnit>();
                for (int i = 0; i < units.Length; i++)
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

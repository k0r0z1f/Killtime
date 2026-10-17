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
    /// <summary>
    /// Mode éditeur de cinématique : s'ouvre depuis l'éditeur de scènes (bouton
    /// "🎬 Édition Ciné" ou "◉/⤴ Éditer" des cartes 🎬) et travaille DIRECTEMENT
    /// sur la carte de combat : on déplace la caméra, les acteurs et les objets,
    /// on capture les START/END des plans, on prévisualise, et on sauvegarde les
    /// trames dans le JSON de la scène (bouton 💾).
    /// Lecture runtime fire-and-forget : une cinématique ne bloque jamais la carte suivante.
    /// </summary>
    public partial class CinematicEditorDevWindow : FloatingWindow<CinematicEditorDevWindow>
    {
        protected override int WindowId => 994;
        protected override string Title => "Éditeur de Cinématiques 🎬 — carte 3D (Shift+F4)";
        protected override Vector2 MinSize => new Vector2(560f, 600f);
        protected override Rect DefaultRect => new Rect(60f, 60f, 640f, 780f);
        protected override KeyCode[] ToggleKeys => new[] { KeyCode.F4 };
        protected override bool RequireShift => true;

        private Vector2 _scroll;
        private int _cineIndex;
        private int _shotIndex;
        // Presse-papier caméra START/END (copier/coller inter-plans + texte système).
        private static Vector3 _camClipPos;
        private static Vector3 _camClipEuler;
        private static float _camClipFov = 60f;
        private static bool _hasCamClip;
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

        protected override void OnUpdate()
        {
            base.OnUpdate();

            if (!_isOpen || _isMinimized || FloatingWindowChrome.IsDocked(WindowId)) return;

            if (Input.GetKeyDown(KeyCode.PageUp))
            {
                CaptureShot(true);
            }
            else if (Input.GetKeyDown(KeyCode.PageDown))
            {
                CaptureShot(false);
            }
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

    }
}

using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using Killtime.UI;
using Killtime.Story.Data;
using Killtime.Core.Character;
using Killtime.Core.Inventory;
using Killtime.Tactics;
using Killtime.Tactics.Grid;
using Killtime.Tactics.TurnSystem;
using Killtime.Audio;

namespace Killtime.Story
{
    public partial class ScenarioEditorDevWindow : FloatingWindow<ScenarioEditorDevWindow>
    {
        private void DrawCinematicCard(SceneCinematicData cine, int index, Vector2 mouseWorld)
        {
            if (cine == null) return;
            if (string.IsNullOrEmpty(cine.CinematicId)) cine.CinematicId = MakeUniqueCineId();
            cine.Shots ??= new System.Collections.Generic.List<SceneCinematicShotData>();

            Vector2 size = GetCinematicCardSize(cine);
            float cardW = size.x;
            float cardH = size.y;

            Rect cardRect = new Rect(cine.GraphPosX, cine.GraphPosY, cardW, cardH);
            Rect headerRect = new Rect(cardRect.x, cardRect.y, cardW, 26f);
            Rect closeRect = new Rect(headerRect.xMax - 22f, headerRect.y + 3f, 18f, 18f);
            Rect dragHeaderRect = new Rect(headerRect.x, headerRect.y, headerRect.width - 26f, headerRect.height);
            Rect resizeGripRect = new Rect(cardRect.xMax - 14f, cardRect.yMax - 14f, 14f, 14f);

            Event evt = Event.current;
            string dragKey = $"cine_{index}_{cine.CinematicId}";
            string resizeKey = $"resize_{dragKey}";
            int dragControlId = GUIUtility.GetControlID(FocusType.Passive);
            int resizeControlId = GUIUtility.GetControlID(FocusType.Passive);

            bool lateResizeCine = IsLateResizeFor(cine);
            if (GraphPrimaryDown(mouseWorld, resizeGripRect) || lateResizeCine)
            {
                GUIUtility.hotControl = resizeControlId;
                _resizingCardKey = resizeKey;
                if (lateResizeCine) { _lateResizeTarget = null; GUIUtility.keyboardControl = 0; }
                evt.Use();
            }
            else if ((evt.type == EventType.MouseDrag || evt.type == EventType.MouseMove) && GUIUtility.hotControl == resizeControlId && _resizingCardKey == resizeKey)
            {
                cine.CardWidth = Mathf.Clamp(mouseWorld.x - cardRect.x, 300f, 750f);
                cine.CardHeight = Mathf.Clamp(mouseWorld.y - cardRect.y, 120f, 600f);
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == resizeControlId)
            {
                GUIUtility.hotControl = 0;
                _resizingCardKey = null;
                evt.Use();
            }

            bool lateDragCine = IsLateDragFor(cine);
            if (GraphPrimaryDown(mouseWorld, dragHeaderRect) || lateDragCine)
            {
                GUIUtility.hotControl = dragControlId;
                _draggingCardKey = dragKey;
                _dragOffset = (lateDragCine ? _lateDownWorld : mouseWorld) - new Vector2(cine.GraphPosX, cine.GraphPosY);
                if (lateDragCine) { _lateDragTarget = null; GUIUtility.keyboardControl = 0; }
                evt.Use();
            }
            else if ((evt.type == EventType.MouseDrag || evt.type == EventType.MouseMove) && GUIUtility.hotControl == dragControlId && _draggingCardKey == dragKey)
            {
                cine.GraphPosX = mouseWorld.x - _dragOffset.x;
                cine.GraphPosY = mouseWorld.y - _dragOffset.y;
                evt.Use();
            }
            else if (evt.type == EventType.MouseUp && GUIUtility.hotControl == dragControlId)
            {
                GUIUtility.hotControl = 0;
                _draggingCardKey = null;
                evt.Use();
            }

            Color headerCol = new Color(0.45f, 0.12f, 0.38f);
            DrawGraphSolidRect(cardRect, new Color(0.12f, 0.06f, 0.10f, 0.95f));
            DrawGraphSolidRect(headerRect, headerCol);
            DrawGraphSolidRect(new Rect(cardRect.x, cardRect.y, cardRect.width, 1f), new Color(1f, 0.35f, 0.85f, 0.9f));
            DrawGraphSolidRect(new Rect(cardRect.x, cardRect.yMax - 1f, cardRect.width, 1f), new Color(1f, 1f, 1f, 0.08f));
            DrawGraphSolidRect(new Rect(cardRect.x, cardRect.y, 1f, cardRect.height), new Color(1f, 1f, 1f, 0.08f));
            DrawGraphSolidRect(new Rect(cardRect.xMax - 1f, cardRect.y, 1f, cardRect.height), new Color(1f, 1f, 1f, 0.08f));

            // Port d'entrée (magenta) : la carte est liée DEPUIS une autre carte. Pas de sortie.
            DrawGraphSolidRect(new Rect(cardRect.x - 7f, cardRect.y + 19f, 14f, 14f), CinePortCol);

            GraphLabel(new Rect(headerRect.x + 8f, headerRect.y + 3f, cardW - 35f, 20f), $"<b>🎬 {cine.Title}</b> [{cine.CinematicId}]");

            GUI.backgroundColor = new Color(0.85f, 0.22f, 0.22f, 1f);
            GUI.color = Color.white;
            if (GraphButton(closeRect, "✕"))
            {
                string doomedId = cine.CinematicId;
                if (_data.Cinematics != null) _data.Cinematics.RemoveAt(index);
                PurgeCinematicReferences(doomedId);
                PurgeSoundReferences(doomedId);
                if (_draggingCardKey == dragKey)
                {
                    _draggingCardKey = null;
                    GUIUtility.hotControl = 0;
                }
                GUI.backgroundColor = Color.white;
                evt.Use();
                return;
            }
            GUI.backgroundColor = Color.white;

            GUI.color = new Color(1f, 1f, 1f, 0.4f);
            GraphLabel(resizeGripRect, "◢");
            GUI.color = Color.white;

            if (_zoom < 0.85f && !_isPanning && _draggingCardKey == null && _resizingCardKey == null && !_wireDraft.IsActive
                && headerRect.Contains(mouseWorld))
            {
                _hasHoverCard = true;
                _hoverTitle = $"🎬 {cine.Title}  ({cine.CinematicId})";
                var hb = new System.Text.StringBuilder();
                hb.AppendLine(cine.GetSummary());
                for (int hi = 0; hi < Mathf.Min(6, cine.Shots.Count); hi++)
                {
                    var hs = cine.Shots[hi];
                    if (hs == null) continue;
                    hb.AppendLine($"• {hs.Label} : {hs.GetSummary()}");
                }
                _hoverBody = hb.ToString().Trim();
                _hoverScreenPos = _graphMouseScreenPos;
            }

            float innerX = cardRect.x + 8f;
            float innerW = cardW - 16f;

            if (_zoom < CardLodThreshold)
            {
                DrawGraphSolidRect(new Rect(innerX, cardRect.y + 32f, innerW * 0.85f, 8f), new Color(1f, 0.35f, 0.85f, 0.16f));
                DrawGraphSolidRect(new Rect(innerX, cardRect.y + 44f, innerW * 0.5f, 6f), new Color(1f, 1f, 1f, 0.06f));
            }
            else
            {
                float y = cardRect.y + 30f;

                GraphLabel(new Rect(innerX, y, 24f, 18f), "ID:");
                cine.CinematicId = GraphTextField(new Rect(innerX + 26f, y, 92f, 18f), cine.CinematicId ?? "");
                GraphLabel(new Rect(innerX + 122f, y, 40f, 18f), "Titre:");
                cine.Title = GraphTextField(new Rect(innerX + 164f, y, Mathf.Max(40f, innerW - 164f), 18f), cine.Title ?? "");
                y += 22f;

                cine.Skippable = GraphToggle(new Rect(innerX, y, 96f, 18f), cine.Skippable, "Esc = skip");
                GraphLabel(new Rect(innerX + 100f, y, 44f, 18f), "Vit x");
                string speedTxt = GraphTextField(new Rect(innerX + 146f, y, 40f, 18f), cine.PlaybackSpeed.ToString("0.0"));
                float.TryParse(speedTxt, out cine.PlaybackSpeed);
                cine.PlaybackSpeed = Mathf.Clamp(cine.PlaybackSpeed, 0.1f, 4f);
                GUI.color = new Color(1f, 1f, 1f, 0.6f);
                GraphLabel(new Rect(innerX + 190f, y, Mathf.Max(40f, innerW - 190f), 18f), cine.GetSummary());
                GUI.color = Color.white;
                y += 20f;

                cine.Letterbox = GraphToggle(new Rect(innerX, y, 105f, 18f), cine.Letterbox, "Bandes noires");
                cine.HideSceneChat = GraphToggle(new Rect(innerX + 110f, y, 125f, 18f), cine.HideSceneChat, "Masquer dialogue");
                cine.InPlace = GraphToggle(new Rect(innerX + 240f, y, 95f, 18f), cine.InPlace, "Sur place");
                y += 20f;

                if (!cine.InPlace)
                {
                    GraphLabel(new Rect(innerX, y, 48f, 18f), "Début:");
                    cine.StartTransition = (CinematicCameraTransition)GraphToolbar(new Rect(innerX + 50f, y, 105f, 18f), (int)cine.StartTransition, new[] { "Téléport", "Fluide" });
                    GraphLabel(new Rect(innerX + 162f, y, 32f, 18f), "Fin:");
                    cine.EndTransition = (CinematicCameraTransition)GraphToolbar(new Rect(innerX + 196f, y, 105f, 18f), (int)cine.EndTransition, new[] { "Téléport", "Fluide" });
                }
                else
                {
                    GUI.color = new Color(1f, 0.82f, 0.35f, 0.9f);
                    GraphLabel(new Rect(innerX, y, innerW, 18f), "📷 <b>Sur place :</b> caméra fixe (secousse/effets en direct)");
                    GUI.color = Color.white;
                }
                y += 20f;

                for (int s = 0; s < cine.Shots.Count; s++)
                {
                    var shot = cine.Shots[s];
                    if (shot == null) { cine.Shots.RemoveAt(s); s--; continue; }

                    // Initialisation automatique de l'effet de secousse si la carte est déclarée sur place
                    if (cine.InPlace && shot.MoveEffect == CinematicCameraEffect.None)
                    {
                        shot.MoveEffect = CinematicCameraEffect.HandheldShake;
                        if (shot.ShakeIntensity < 0.05f) shot.ShakeIntensity = 0.25f;
                    }

                    if (GraphButton(new Rect(innerX, y, 22f, 18f), "◉"))
                    {
                        CinematicEditorDevWindow.Open();
                        if (CinematicEditorDevWindow.Instance != null)
                            CinematicEditorDevWindow.Instance.SelectCinematic(cine.CinematicId, s);
                    }
                    shot.Label = GraphTextField(new Rect(innerX + 24f, y, Mathf.Max(40f, innerW - 24f - 134f - 24f), 18f), shot.Label ?? "");
                    GraphLabel(new Rect(innerX + innerW - 134f - 22f, y, 16f, 18f), "s:");
                    string durTxt = GraphTextField(new Rect(innerX + innerW - 134f, y, 36f, 18f), shot.Duration.ToString("0.0"));
                    float.TryParse(durTxt, out shot.Duration);
                    shot.Duration = Mathf.Clamp(shot.Duration, 0.2f, 60f);
                    GraphLabel(new Rect(innerX + innerW - 94f, y, 24f, 18f), "+d:");
                    string delTxt = GraphTextField(new Rect(innerX + innerW - 70f, y, 36f, 18f), shot.StartDelay.ToString("0.0"));
                    float.TryParse(delTxt, out shot.StartDelay);
                    shot.StartDelay = Mathf.Clamp(shot.StartDelay, 0f, 60f);
                    GUI.backgroundColor = new Color(0.85f, 0.25f, 0.25f);
                    if (GraphButton(new Rect(innerX + innerW - 22f, y, 22f, 18f), "✕"))
                    {
                        cine.Shots.RemoveAt(s);
                        GUI.backgroundColor = Color.white;
                        break;
                    }
                    GUI.backgroundColor = Color.white;
                    y += 20f;
                }

                if (cine.Shots.Count == 0 && y + 18f <= cardRect.yMax)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.5f);
                    GraphLabel(new Rect(innerX, y, innerW, 18f), "<i>(aucun plan — + Plan pour commencer)</i>");
                    GUI.color = Color.white;
                }

                // Sortie "fin →" : à la fin de la lecture, enchaîner vers une carte
                // (cinématique, réplique, conséquence, événement ou nœud). Vide = fin simple.
                GraphLabel(new Rect(innerX, y, 62f, 18f), "Fin → :");
                cine.NextTargetId = GraphTextField(new Rect(innerX + 64f, y, Mathf.Max(40f, innerW - 64f - 26f), 18f), cine.NextTargetId ?? "");
                if (!string.IsNullOrEmpty(cine.NextTargetId) && GraphButton(new Rect(innerX + innerW - 22f, y, 22f, 18f), "⊘")) cine.NextTargetId = "";
                y += CineRowH;
                DrawSoundLinkRow(ref y, innerX, innerW, cine.CinematicId);

                float halfBtn = (innerW - 6f) * 0.5f;
                float btnY = Mathf.Min(y, cardRect.yMax - 22f);
                GUI.backgroundColor = new Color(1f, 0.35f, 0.85f);
                if (GraphButton(new Rect(innerX, btnY, halfBtn, 20f), "+ Plan"))
                {
                    int ns = cine.Shots.Count + 1;
                    cine.Shots.Add(new SceneCinematicShotData { ShotId = $"shot_{ns}", Label = $"Plan {ns}" });
                }
                GUI.backgroundColor = new Color(0.7f, 0.25f, 0.6f);
                if (GraphButton(new Rect(innerX + halfBtn + 6f, btnY, halfBtn, 20f), "⤴ Éditer (carte 3D)"))
                {
                    CinematicEditorDevWindow.Open();
                    if (CinematicEditorDevWindow.Instance != null)
                        CinematicEditorDevWindow.Instance.SelectCinematic(cine.CinematicId, Mathf.Max(0, cine.Shots.Count - 1));
                }
                GUI.backgroundColor = Color.white;
            }

            // Port de sortie "fin →" : flanc droit, rangée dédiée (magenta).
            // La carte garde son port d'entrée à gauche : input + output coexistent.
            DrawCineOutPort(GetCinematicOutCenter(cine), null, mouseWorld, cine);
            DrawSoundOutPort(new Vector2(cardRect.x + cardRect.width * 0.5f, cardRect.yMax), cine.CinematicId, mouseWorld);
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Killtime.Tactics;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Core.Character;
using Killtime.Core.Dice;
using Killtime.Core.Inventory;
using Killtime.CameraSystem;
using Killtime.UI;
using Killtime.Audio;
using Killtime.Story.Data;


namespace Killtime.Story.Scenes
{
    public partial class JsonStorySceneController
    {
        private void OnGUI()
        {
            if (!State._isSceneInitialized) return;
            if (CombatHUD.IsPaused || (StorySceneManager.Instance != null && StorySceneManager.Instance.IsTransitioning)) return;

            var director = FindAnyObjectByType<CinematicDirector>();
            bool hideByCine = director != null && director.IsPlayingSceneCinematic
                && (director.CurrentPlayingSceneCinematic == null || director.CurrentPlayingSceneCinematic.HideSceneChat);

            if (hideByCine && State._chatAnimProgress <= 0.001f)
            {
                State._dialogueRect = Rect.zero;
                State._partyBarRect = Rect.zero;
                State._hasPartyBar = false;
                State._interactButtonRects.Clear();
                return;
            }

            State._interactButtonRects.Clear();
            if (!hideByCine)
            {
                DrawPartySwitchBar();
                DrawInteractablesWorldOverlay();
                DrawInteractablePopups();
            }

            var node = State._director != null ? State._director.CurrentNode : null;
            if (node != null)
            {
                State._lastActiveNode = node;
            }

            var nodeToDraw = node ?? State._lastActiveNode;
            if (nodeToDraw == null || State._chatAnimProgress <= 0.001f)
            {
                State._dialogueRect = Rect.zero;
                return;
            }

            DrawDialogueBox(nodeToDraw);
        }

        private void DrawPartySwitchBar()
        {
            if (State._turnManager == null || !State._turnManager.IsInExploration || State._playerParty.Count <= 1)
            {
                State._hasPartyBar = false;
                State._partyBarRect = Rect.zero;
                return;
            }

            float barWidth = State._playerParty.Count * 110f;
            Rect rect = new Rect(24f, 96f, barWidth, 38f);
            State._partyBarRect = rect;
            State._hasPartyBar = true;
            GUI.Box(rect, GUIContent.none);

            GUILayout.BeginArea(rect);
            GUILayout.BeginHorizontal();
            for (int i = 0; i < State._playerParty.Count; i++)
            {
                var unit = State._playerParty[i];
                bool active = (State._turnManager.ActiveUnit == unit);
                GUI.backgroundColor = active ? new Color(0.2f, 0.8f, 1f) : Color.white;
                if (GUILayout.Button($"[{i + 1}] {unit.Stats.Name}", GUILayout.Height(28)))
                {
                    SelectOperative(unit);
                }
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void DrawInteractablesWorldOverlay()
        {
            if (UnityEngine.Camera.main == null || State._turnManager == null || State._turnManager.ActiveUnit == null) return;

            var activeUnit = State._turnManager.ActiveUnit;

            for (int i = 0; i < State._interactables.Count; i++)
            {
                var it = State._interactables[i];
                if (it == null || !it.CanInteract(activeUnit)) continue;

                var node = State._grid?.GetNode(it.Coordinates);
                Vector3 worldPos = node != null ? node.WorldPosition + Vector3.up * 1.2f : it.transform.position;
                Vector3 screenPos = UnityEngine.Camera.main.WorldToScreenPoint(worldPos);

                if (screenPos.z > 0)
                {
                    float y = Screen.height - screenPos.y;
                    Rect r = new Rect(screenPos.x - 90f, y - 24f, 180f, 26f);
                    State._interactButtonRects.Add(r);
                    GUI.backgroundColor = new Color(0.2f, 0.9f, 0.5f);
                    var data = FindInteractableData(it);
                    if (GUI.Button(r, $"[E] {it.ActionLabel}") && data != null)
                    {
                        OpenInteractableWindow(activeUnit, data);
                    }
                    GUI.backgroundColor = Color.white;
                }
            }
        }

        /// <summary>
        /// Popups monde des interactables : ancrés au-dessus de l'objet,
        /// bloqueurs de clics 3D, info en fondu auto, offre de saut persistante.
        /// </summary>
        private void DrawInteractablePopups()
        {
            if (State._activePopups.Count == 0 || UnityEngine.Camera.main == null) return;
            var snapshot = State._activePopups.ToArray();
            var wrap = new GUIStyle(GUI.skin.label) { wordWrap = true };
            for (int s = 0; s < snapshot.Length; s++)
            {
                var popup = snapshot[s];
                if (popup == null || !State._activePopups.Contains(popup)) continue;
                // Les interactables avec Actions explicites utilisent la fenêtre 887 ;
                // on ne dessine plus de popup monde résultat pour eux.
                var popupData = FindInteractableDataById(popup.InteractableId);
                if (popupData != null && popupData.Actions != null && popupData.Actions.Count > 0) continue;
                if (!TryGetSpawnedInteractable(popup.InteractableId, out var prop) || prop == null) continue;

                var hexNode = State._grid?.GetNode(prop.Coordinates);
                Vector3 worldPos = hexNode != null
                    ? hexNode.WorldPosition + Vector3.up * 2.1f
                    : prop.transform.position + Vector3.up * 2.1f;
                Vector3 sp = UnityEngine.Camera.main.WorldToScreenPoint(worldPos);
                if (sp.z <= 0f) { popup.LastScreenRect = Rect.zero; continue; }

                float w = 300f;
                float msgH = string.IsNullOrEmpty(popup.Message) ? 0f : wrap.CalcHeight(new GUIContent(popup.Message), w - 20f);
                msgH = Mathf.Clamp(msgH, 0f, 120f);
                float h = Mathf.Clamp(34f + msgH + (popup.HasJump ? 64f : 12f), 80f, 230f);
                float x = Mathf.Clamp(sp.x - w * 0.5f, 8f, Mathf.Max(8f, Screen.width - w - 8f));
                float yBase = Screen.height - sp.y;
                float y = yBase - h - 34f;
                if (y < 60f) y = yBase + 26f;
                Rect r = new Rect(x, y, w, h);
                // Le popup NE bloque les clics 3D que sur ses boutons (voir
                // BlockPopupButton) : le titre et le message laissent passer,
                // sinon un popup persistant recouvre les personnages derrière
                // et les rend insélectionnables.
                popup.LastScreenRect = r;

                float alpha = 1f;
                if (!popup.HasJump)
                {
                    float remain = InteractablePopupAutoCloseDelay + InteractablePopupFadeDuration
                        - (Time.unscaledTime - popup.CreatedUnscaledTime);
                    if (remain <= 0f) { popup.LastScreenRect = Rect.zero; continue; } // purgé par PruneInteractablePopups
                    if (remain <= InteractablePopupFadeDuration)
                        alpha = Mathf.Clamp01(remain / InteractablePopupFadeDuration);
                }

                Color prevColor = GUI.color;
                Color prevBg = GUI.backgroundColor;
                GUI.color = new Color(1f, 1f, 1f, alpha);
                GUI.Box(r, GUIContent.none);
                Rect area = new Rect(r.x + 8f, r.y + 4f, r.width - 16f, r.height - 8f);
                GUILayout.BeginArea(area);
                GUILayout.BeginHorizontal();
                GUILayout.Label($"🔧 {popup.DisplayName}", GUILayout.ExpandWidth(true));
                if (GUILayout.Button("✕", GUILayout.Width(24), GUILayout.Height(20)))
                {
                    DismissPopup(popup);
                }
                BlockPopupButton(area);
                GUILayout.EndHorizontal();
                if (!string.IsNullOrEmpty(popup.Message))
                {
                    GUILayout.Label(popup.Message, wrap);
                }
                if (popup.HasJump)
                {
                    GUILayout.Space(4);
                    GUILayout.BeginHorizontal();
                    GUI.backgroundColor = new Color(0.25f, 0.85f, 0.45f);
                    if (GUILayout.Button($"Suivre ➔ {popup.TargetNodeId}", GUILayout.Height(26)))
                    {
                        FollowPopupJump(popup);
                    }
                    BlockPopupButton(area);
                    GUI.backgroundColor = Color.white;
                    if (GUILayout.Button("Rester", GUILayout.Width(70), GUILayout.Height(26)))
                    {
                        DismissPopup(popup);
                    }
                    BlockPopupButton(area);
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndArea();
                GUI.color = prevColor;
                GUI.backgroundColor = prevBg;
            }
        }

        /// <summary>
        /// Seuls les boutons du popup bloquent les clics 3D (la carte derrière
        /// reste cliquable : sélection et déplacement possibles sous le popup).
        /// </summary>
        private void BlockPopupButton(Rect area)
        {
            try
            {
                Rect last = GUILayoutUtility.GetLastRect();
                last.x += area.x;
                last.y += area.y;
                if (last.width > 0f && last.height > 0f)
                    State._interactButtonRects.Add(last);
            }
            catch { /* ignore */ }
        }
    }
}

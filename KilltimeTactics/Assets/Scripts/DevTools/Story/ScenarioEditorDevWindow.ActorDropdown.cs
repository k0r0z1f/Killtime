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

        private void OpenActorDropdown(string key, Rect anchorWorld, string current, System.Action<string> setter)
        {
            _actorDropKey = key;
            _actorDropAnchorWorld = anchorWorld;
            _actorDropCurrent = current ?? "";
            _actorDropSetter = setter;
            _actorDropScroll = Vector2.zero;
            _actorDropIndex = -1;
        }

        private System.Collections.Generic.List<string> BuildActorCandidates()
        {
            var list = new System.Collections.Generic.List<string>();
            var seen = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            System.Action<string> add = n =>
            {
                if (string.IsNullOrWhiteSpace(n)) return;
                n = n.Trim();
                if (seen.Add(n)) list.Add(n);
            };
            if (_data != null && _data.Actors != null)
            {
                for (int i = 0; i < _data.Actors.Count; i++)
                {
                    var a = _data.Actors[i];
                    if (a == null) continue;
                    add(a.DisplayName);
                    if (!string.IsNullOrWhiteSpace(a.ActorId) && !string.Equals(a.ActorId, a.DisplayName, System.StringComparison.OrdinalIgnoreCase))
                        add(a.ActorId);
                }
            }
            if (_data != null && _data.Nodes != null)
            {
                for (int n = 0; n < _data.Nodes.Count; n++)
                {
                    var node = _data.Nodes[n];
                    if (node == null) continue;
                    if (node.Dialogues != null)
                    {
                        for (int d = 0; d < node.Dialogues.Count; d++)
                        {
                            var line = node.Dialogues[d];
                            if (line == null) continue;
                            add(line.SpeakerId);
                        }
                    }
                    if (node.Consequences != null)
                    {
                        for (int k = 0; k < node.Consequences.Count; k++)
                        {
                            var cons = node.Consequences[k];
                            if (cons == null) continue;
                            add(cons.SpeakerId);
                        }
                    }
                }
            }
            if (list.Count > 40) list.RemoveRange(40, list.Count - 40);
            return list;
        }

        private void DrawActorDropdownPopup(Rect canvasRect)
        {
            if (_actorDropKey == null) return;
            Event evt = Event.current;
            var candidates = BuildActorCandidates();

            const float rowH = 20f;
            const float popW = 210f;
            // Hauteur bornée + défilement : sans ça le bas de liste est
            // inatteignable (popup plus haute que le canvas, sans scroll).
            const int maxVisibleRows = 12;
            int rows = candidates.Count > 0 ? candidates.Count : 1;
            float fullH = rows * rowH + 6f;
            float viewH = Mathf.Min(fullH, maxVisibleRows * rowH + 6f);

            // Cadrage dans la zone monde visible (le BeginGroup clippe tout ce
            // qui dépasse) : on rabat en X, et on bascule au-dessus de l'ancre
            // s'il n'y a pas la place en dessous.
            Rect visWorld = GetVisibleWorldRect(canvasRect);
            float popX = Mathf.Clamp(_actorDropAnchorWorld.x, visWorld.xMin + 4f, Mathf.Max(visWorld.xMin + 4f, visWorld.xMax - popW - 4f));
            float popY = _actorDropAnchorWorld.yMax + 2f;
            if (popY + viewH > visWorld.yMax - 4f)
                popY = _actorDropAnchorWorld.y - viewH - 2f;
            popY = Mathf.Clamp(popY, visWorld.yMin + 4f, Mathf.Max(visWorld.yMin + 4f, visWorld.yMax - viewH - 4f));
            Rect pop = new Rect(popX, popY, popW, viewH);
            _actorDropWorld = pop;

            DrawGraphSolidRect(pop, new Color(0.03f, 0.05f, 0.08f, 0.97f));
            DrawGraphSolidRect(new Rect(pop.x, pop.y, pop.width, 1.5f), new Color(0.0f, 0.85f, 1f, 0.9f));
            DrawGraphSolidRect(new Rect(pop.x, pop.yMax - 1f, pop.width, 1f), new Color(1f, 1f, 1f, 0.15f));
            DrawGraphSolidRect(new Rect(pop.x, pop.y, 1f, pop.height), new Color(1f, 1f, 1f, 0.15f));
            DrawGraphSolidRect(new Rect(pop.xMax - 1f, pop.y, 1f, pop.height), new Color(1f, 1f, 1f, 0.15f));

            if (candidates.Count == 0)
            {
                GUI.Label(GraphRect(new Rect(pop.x + 6f, pop.y + 3f, popW - 12f, rowH)), "<color=grey>(aucun acteur — saisie libre)</color>");
            }
            else
            {
                // Navigation clavier : index courant initialisé sur la valeur active.
                if (_actorDropIndex < 0 || _actorDropIndex >= candidates.Count)
                {
                    _actorDropIndex = 0;
                    for (int k = 0; k < candidates.Count; k++)
                    {
                        if (string.Equals(candidates[k], _actorDropCurrent, System.StringComparison.OrdinalIgnoreCase))
                        {
                            _actorDropIndex = k;
                            break;
                        }
                    }
                }
                float zoom = Mathf.Max(0.0001f, _zoom);
                float rowHs = rowH * zoom;
                Rect viewScreen = GraphRect(pop);
                // Réserve la barre de défilement quand le contenu déborde.
                float sbW = fullH > viewH + 0.5f ? 16f : 0f;
                Rect contentScreen = new Rect(0f, 0f, Mathf.Max(10f, viewScreen.width - sbW - 8f), candidates.Count * rowHs);
                // Zone de scroll intérieure (marges de 3px comme avant).
                Rect scrollScreen = new Rect(viewScreen.x + 3f * zoom, viewScreen.y + 3f * zoom, viewScreen.width - 6f * zoom, viewScreen.height - 6f * zoom);
                _actorDropScroll = GUI.BeginScrollView(scrollScreen, _actorDropScroll, contentScreen, false, false);
                for (int i = 0; i < candidates.Count; i++)
                {
                    Rect r = new Rect(0f, i * rowHs, contentScreen.width, rowHs - 2f * zoom);
                    bool isCurrent = string.Equals(candidates[i], _actorDropCurrent, System.StringComparison.OrdinalIgnoreCase);
                    string label = (i == _actorDropIndex ? "► " : (isCurrent ? "► " : "")) + candidates[i];
                    // Survol souris = sélection clavier (Entrée valide la ligne survolée).
                    if (evt.type == EventType.Repaint && r.Contains(Event.current.mousePosition))
                        _actorDropIndex = i;
                    // Bouton brut (pas de wrapper) : la popup reste cliquable en mode modal.
                    if (GUI.Button(r, label))
                    {
                        _actorDropSetter?.Invoke(candidates[i]);
                        _actorDropKey = null;
                        GUI.EndScrollView();
                        evt.Use();
                        return;
                    }
                }
                GUI.EndScrollView();
                // Maintient la ligne clavier visible après un déplacement flèches.
                float visTop = _actorDropScroll.y;
                float visH = Mathf.Max(1f, scrollScreen.height);
                float selTop = _actorDropIndex * rowHs;
                if (selTop < visTop) _actorDropScroll.y = selTop;
                else if (selTop + rowHs > visTop + visH) _actorDropScroll.y = selTop + rowHs - visH;
            }

            if (evt.type == EventType.KeyDown)
            {
                if (evt.keyCode == KeyCode.Escape)
                {
                    _actorDropKey = null;
                    evt.Use();
                }
                else if (candidates.Count > 0 && (evt.keyCode == KeyCode.DownArrow || evt.keyCode == KeyCode.UpArrow))
                {
                    if (_actorDropIndex < 0) _actorDropIndex = 0;
                    else _actorDropIndex = (evt.keyCode == KeyCode.DownArrow)
                        ? Mathf.Min(candidates.Count - 1, _actorDropIndex + 1)
                        : Mathf.Max(0, _actorDropIndex - 1);
                    _actorDropCurrent = candidates[_actorDropIndex];
                    evt.Use();
                }
                else if (candidates.Count > 0 && (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter))
                {
                    int pick = (_actorDropIndex >= 0 && _actorDropIndex < candidates.Count) ? _actorDropIndex : 0;
                    _actorDropSetter?.Invoke(candidates[pick]);
                    _actorDropKey = null;
                    evt.Use();
                }
            }
        }
    }
}

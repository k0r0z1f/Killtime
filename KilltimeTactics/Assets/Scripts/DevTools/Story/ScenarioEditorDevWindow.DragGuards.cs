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

        private void SwallowOverlappedHeaderPress(Rect canvasRect, Vector2 mouseWorld, Vector2 mouseScreen)
        {
            Event evt = Event.current;
            if (evt == null) return;
            // Fin de geste : purger les réservations (aucun Use : les nettoyages
            // inline des drags doivent toujours voir ce MouseUp).
            if (evt.type == EventType.MouseUp)
            {
                _lateDragTarget = null;
                _lateResizeTarget = null;
                return;
            }
            if (evt.type != EventType.MouseDown || evt.button != 0 || evt.clickCount >= 2) return;
            if (_pickerCapturesMouse) return;
            if (!string.IsNullOrEmpty(_draggingCardKey) || !string.IsNullOrEmpty(_resizingCardKey) || !string.IsNullOrEmpty(_draggingNodeId)) return;
            if (_lateDragTarget != null || _lateResizeTarget != null) return;
            if (!canvasRect.Contains(mouseScreen)) return;
            if (_data == null || _data.Nodes == null) return;

            Rect vis = GetVisibleWorldRect(canvasRect);

            // Cartes globales, de la plus au-dessus vers le fond.
            if (_data.Cinematics != null)
            {
                for (int i = _data.Cinematics.Count - 1; i >= 0; i--)
                {
                    var c = _data.Cinematics[i];
                    if (c == null) continue;
                    Vector2 s = GetCinematicCardSize(c);
                    if (!vis.Overlaps(new Rect(c.GraphPosX, c.GraphPosY, s.x, s.y))) continue;
                    if (HitCardGrip(c.GraphPosX, c.GraphPosY, s, mouseWorld)) { ClaimLateResize(c, mouseWorld, mouseScreen, evt); return; }
                    if (HitCardHeader(c.GraphPosX, c.GraphPosY, s.x, mouseWorld)) { ClaimLateDrag(c, mouseWorld, mouseScreen, evt); return; }
                }
            }
            if (_data.Actors != null)
            {
                for (int i = _data.Actors.Count - 1; i >= 0; i--)
                {
                    var a = _data.Actors[i];
                    if (a == null) continue;
                    Vector2 s = GetActorCardSize(a);
                    if (!vis.Overlaps(new Rect(a.GraphPosX, a.GraphPosY, s.x, s.y))) continue;
                    if (HitCardGrip(a.GraphPosX, a.GraphPosY, s, mouseWorld)) { ClaimLateResize(a, mouseWorld, mouseScreen, evt); return; }
                    if (HitCardHeader(a.GraphPosX, a.GraphPosY, s.x, mouseWorld)) { ClaimLateDrag(a, mouseWorld, mouseScreen, evt); return; }
                }
            }
            if (_data.Interactables != null)
            {
                for (int i = _data.Interactables.Count - 1; i >= 0; i--)
                {
                    var it = _data.Interactables[i];
                    if (it == null) continue;
                    Vector2 s = GetInteractableCardSize(it);
                    if (!vis.Overlaps(new Rect(it.GraphPosX, it.GraphPosY, s.x, s.y))) continue;
                    if (HitCardGrip(it.GraphPosX, it.GraphPosY, s, mouseWorld)) { ClaimLateResize(it, mouseWorld, mouseScreen, evt); return; }
                    if (HitCardHeader(it.GraphPosX, it.GraphPosY, s.x, mouseWorld)) { ClaimLateDrag(it, mouseWorld, mouseScreen, evt); return; }
                }
            }
            if (_data.Triggers != null)
            {
                for (int t = _data.Triggers.Count - 1; t >= 0; t--)
                {
                    var trg = _data.Triggers[t];
                    if (trg == null) continue;
                    Vector2 s = GetTriggerCardSize(trg);
                    if (!vis.Overlaps(new Rect(trg.GraphPosX, trg.GraphPosY, s.x, s.y))) continue;
                    if (HitCardGrip(trg.GraphPosX, trg.GraphPosY, s, mouseWorld)) { ClaimLateResize(trg, mouseWorld, mouseScreen, evt); return; }
                    if (HitCardHeader(trg.GraphPosX, trg.GraphPosY, s.x, mouseWorld)) { ClaimLateDrag(trg, mouseWorld, mouseScreen, evt); return; }
                }
            }

            // Cartes internes, nœuds en sens inverse, conséquences/événements/dialogues inversés.
            for (int n = _data.Nodes.Count - 1; n >= 0; n--)
            {
                var node = _data.Nodes[n];
                if (node == null) continue;
                if (node.Consequences != null)
                {
                    for (int k = node.Consequences.Count - 1; k >= 0; k--)
                    {
                        var cons = node.Consequences[k];
                        if (cons == null) continue;
                        Vector2 s = GetConsequenceCardSize(cons);
                        if (!vis.Overlaps(new Rect(cons.GraphPosX, cons.GraphPosY, s.x, s.y))) continue;
                        if (HitCardGrip(cons.GraphPosX, cons.GraphPosY, s, mouseWorld)) { ClaimLateResize(cons, mouseWorld, mouseScreen, evt); return; }
                        if (HitCardHeader(cons.GraphPosX, cons.GraphPosY, s.x, mouseWorld)) { ClaimLateDrag(cons, mouseWorld, mouseScreen, evt); return; }
                    }
                }
                if (node.Events != null)
                {
                    for (int e = node.Events.Count - 1; e >= 0; e--)
                    {
                        var evd = node.Events[e];
                        if (evd == null) continue;
                        Vector2 s = GetEventCardSize(evd);
                        if (!vis.Overlaps(new Rect(evd.GraphPosX, evd.GraphPosY, s.x, s.y))) continue;
                        if (HitCardGrip(evd.GraphPosX, evd.GraphPosY, s, mouseWorld)) { ClaimLateResize(evd, mouseWorld, mouseScreen, evt); return; }
                        if (HitCardHeader(evd.GraphPosX, evd.GraphPosY, s.x, mouseWorld)) { ClaimLateDrag(evd, mouseWorld, mouseScreen, evt); return; }
                    }
                }
                if (node.Dialogues != null)
                {
                    for (int d = node.Dialogues.Count - 1; d >= 0; d--)
                    {
                        var dlg = node.Dialogues[d];
                        if (dlg == null) continue;
                        Vector2 s = GetDialogueCardSize(dlg);
                        if (!vis.Overlaps(new Rect(dlg.GraphPosX, dlg.GraphPosY, s.x, s.y))) continue;
                        if (HitCardGrip(dlg.GraphPosX, dlg.GraphPosY, s, mouseWorld)) { ClaimLateResize(dlg, mouseWorld, mouseScreen, evt); return; }
                        if (HitCardHeader(dlg.GraphPosX, dlg.GraphPosY, s.x, mouseWorld)) { ClaimLateDrag(dlg, mouseWorld, mouseScreen, evt); return; }
                    }
                }
            }

            // Frames de nœuds (peintes en premier = tout en dessous). Miroir exact de
            // isOverHeaderDragZone dans DrawNodeFrame (boutons d'actions et port de
            // sortie exclus : un port reste un port, le bouton ✕ reste cliquable).
            for (int n = _data.Nodes.Count - 1; n >= 0; n--)
            {
                var node = _data.Nodes[n];
                if (node == null) continue;
                Rect box = ComputeNodeBoundingBox(node);
                if (!vis.Overlaps(box)) continue;
                Rect header = new Rect(box.x, box.y, box.width, 38f);
                Rect actions = new Rect(header.xMax - 360f, header.y + 5f, 350f, 26f);
                Rect outSock = new Rect(box.xMax - 9f, box.y + 10f, 18f, 18f);
                if (header.Contains(mouseWorld) && !actions.Contains(mouseWorld) && !outSock.Contains(mouseWorld))
                {
                    ClaimLateDrag(node, mouseWorld, mouseScreen, evt);
                    return;
                }
            }
        }

        private static bool HitCardHeader(float gx, float gy, float gw, Vector2 mouseWorld)
        {
            // Miroir de dragHeaderRect : (x, y, W-26, 26). Le bouton ✕ (26px à droite)
            // reste cliquable, les ports du flanc droit restent des ports.
            return new Rect(gx, gy, gw - 26f, 26f).Contains(mouseWorld);
        }

        private static bool HitCardGrip(float gx, float gy, Vector2 size, Vector2 mouseWorld)
        {
            // Miroir de resizeGripRect : carré 14px en bas à droite.
            return new Rect(gx + size.x - 14f, gy + size.y - 14f, 14f, 14f).Contains(mouseWorld);
        }

        private void ClaimLateDrag(object target, Vector2 mouseWorld, Vector2 mouseScreen, Event evt)
        {
            _lateDragTarget = target;
            _lateResizeTarget = null;
            _lateDownWorld = mouseWorld;
            _lateDownScreen = mouseScreen;
            evt.Use();
        }

        private void ClaimLateResize(object target, Vector2 mouseWorld, Vector2 mouseScreen, Event evt)
        {
            _lateResizeTarget = target;
            _lateDragTarget = null;
            _lateDownWorld = mouseWorld;
            _lateDownScreen = mouseScreen;
            evt.Use();
        }
        private bool IsLateDragFor(object o)
        {
            if (!ReferenceEquals(_lateDragTarget, o)) return false;
            if (!string.IsNullOrEmpty(_draggingCardKey) || !string.IsNullOrEmpty(_draggingNodeId)) return false;
            var e = Event.current;
            if (e == null || (e.type != EventType.MouseDrag && e.type != EventType.MouseMove)) return false;
            return Vector2.Distance(_graphMouseScreenPos, _lateDownScreen) > 6f;
        }

        private bool IsLateResizeFor(object o)
        {
            if (!ReferenceEquals(_lateResizeTarget, o)) return false;
            if (!string.IsNullOrEmpty(_resizingCardKey) || !string.IsNullOrEmpty(_draggingCardKey)) return false;
            var e = Event.current;
            if (e == null || (e.type != EventType.MouseDrag && e.type != EventType.MouseMove)) return false;
            return Vector2.Distance(_graphMouseScreenPos, _lateDownScreen) > 6f;
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.UI;
using Killtime.Core.World;

namespace Killtime.Story
{
    public partial class HybrisWorldMapEditorWindow : FloatingWindow<HybrisWorldMapEditorWindow>
    {
        private void HandleCanvasInput(Rect viewRect)
        {
            if (_showRenameModal) return;

            Event e = Event.current;
            if (e == null) return;

            if (_contextMenuVisible && e.type == EventType.MouseDown) return;

            Vector2 mp = e.mousePosition;
            bool inside = viewRect.Contains(mp);

            Rect canvasRect = new Rect(_canvasPan.x, _canvasPan.y, CanvasBaseWidth * _canvasZoom, CanvasBaseHeight * _canvasZoom);
            Vector2 localMapPos = mp - viewRect.position;
            _canvasLocalMouse = localMapPos;

            if (inside)
            {
                _hoverNodeId = HitNodeCanvas(canvasRect, localMapPos);
                _hoverLinkIndex = HitLinkCanvas(canvasRect, localMapPos);
            }
            else
            {
                _hoverNodeId = null;
                _hoverLinkIndex = -1;
            }

            if (inside && HandleKeyboardShortcuts(e, canvasRect, localMapPos)) return;

            if (e.type == EventType.MouseDown && inside)
            {
                _contextMenuVisible = false;
            }

            if (e.type == EventType.MouseUp || e.rawType == EventType.MouseUp)
            {
                if (e.button == 1 && _potentialRightClick)
                {
                    _potentialRightClick = false;
                    if (Vector2.Distance(mp, _rightClickStartMouse) < 6f)
                    {
                        OpenContextMenuAt(canvasRect, localMapPos, mp);
                        _isPanning = false;
                        e.Use();
                        return;
                    }
                }

                if (_dragNodeId != null)
                {
                    if (_hasDraggedNode)
                    {
                        SetStatus("Position du secteur mise à jour.", Color.green);
                    }
                    _dragNodeId = null;
                    _hasDraggedNode = false;
                    e.Use();
                    return;
                }

                if (_isPanning)
                {
                    _isPanning = false;
                    e.Use();
                    return;
                }
            }

            if (inside && e.type == EventType.ScrollWheel)
            {
                float oldZoom = _canvasZoom;
                float zoomFactor = Mathf.Pow(1.12f, -e.delta.y);
                _canvasZoom = Mathf.Clamp(_canvasZoom * zoomFactor, 0.30f, 50.0f);
                Vector2 mouseCanvasPos = (mp - viewRect.position - _canvasPan) / oldZoom;
                _canvasPan = (mp - viewRect.position) - mouseCanvasPos * _canvasZoom;
                e.Use();
                return;
            }

            if (_isPanning && e.type == EventType.MouseDrag)
            {
                if (Vector2.Distance(mp, _rightClickStartMouse) >= 6f)
                {
                    _potentialRightClick = false;
                }
                _canvasPan = _panStartOrigin + (mp - _panStartMouse);
                e.Use();
                return;
            }

            if (_dragNodeId != null && e.type == EventType.MouseDrag)
            {
                var n = FindInDoc(_dragNodeId);
                if (n != null)
                {
                    if (!_hasDraggedNode && Vector2.Distance(mp, _dragStartMousePos) > 4f)
                    {
                        RecordUndo("Déplacement de secteur");
                        _hasDraggedNode = true;
                    }

                    if (_hasDraggedNode)
                    {
                        float nx = Mathf.Clamp01((localMapPos.x - _canvasPan.x) / canvasRect.width);
                        float ny = Mathf.Clamp01((localMapPos.y - _canvasPan.y) / canvasRect.height);

                        if (_snapToHex)
                        {
                            var axial = HybrisWorldMapData.NormToAxial(new Vector2(nx, ny));
                            n.HexQ = axial.x;
                            n.HexR = axial.y;
                            n.MapPos = HybrisWorldMapData.AxialToNorm(axial.x, axial.y);
                        }
                        else
                        {
                            n.MapPos = new Vector2(nx, ny);
                            var axial = HybrisWorldMapData.NormToAxial(n.MapPos);
                            n.HexQ = axial.x;
                            n.HexR = axial.y;
                        }
                        _dirty = true;
                        _issuesStale = true;
                    }
                }
                e.Use();
                return;
            }

            if (inside)
            {
                if (e.type == EventType.MouseDown && (e.button == 1 || e.button == 2))
                {
                    _isPanning = true;
                    _panStartMouse = mp;
                    _panStartOrigin = _canvasPan;
                    _rightClickStartMouse = mp;
                    _potentialRightClick = (e.button == 1);
                    e.Use();
                    return;
                }

                if (e.type == EventType.MouseDown && e.button == 0)
                {
                    if (e.alt)
                    {
                        _isPanning = true;
                        _panStartMouse = mp;
                        _panStartOrigin = _canvasPan;
                        e.Use();
                        return;
                    }

                    string hitNode = HitNodeCanvas(canvasRect, localMapPos);
                    int hitLink = HitLinkCanvas(canvasRect, localMapPos);

                    if (hitNode != null && e.clickCount >= 2 && _mapMode == 0)
                    {
                        var nTarget = FindInDoc(hitNode);
                        if (nTarget != null)
                        {
                            ZoomToNode(nTarget, 43.0f);
                            e.Use();
                            return;
                        }
                    }

                    if (_mapMode == 3)
                    {
                        if (hitNode == null)
                        {
                            RecordUndo("Création de secteur");
                            float nx = Mathf.Clamp01((localMapPos.x - _canvasPan.x) / canvasRect.width);
                            float ny = Mathf.Clamp01((localMapPos.y - _canvasPan.y) / canvasRect.height);
                            CreateNodeAt(new Vector2(nx, ny));
                            e.Use();
                            return;
                        }
                    }
                    else if (_mapMode == 2)
                    {
                        if (hitNode == null)
                        {
                            _linkFrom = null;
                        }
                        else if (_linkFrom == null)
                        {
                            _linkFrom = hitNode;
                            _selectedNodeId = hitNode;
                            SyncIdBuffer();
                        }
                        else if (_linkFrom == hitNode)
                        {
                            _linkFrom = null;
                        }
                        else
                        {
                            RecordUndo("Création de liaison");
                            CreateLink(_linkFrom, hitNode);
                            _linkFrom = null;
                        }
                        e.Use();
                        return;
                    }
                    else if (_mapMode == 4)
                    {
                        if (!_rulerStart.HasValue) _rulerStart = localMapPos;
                        else if (!_rulerEnd.HasValue) _rulerEnd = localMapPos;
                        else { _rulerStart = localMapPos; _rulerEnd = null; }
                        e.Use();
                        return;
                    }
                    else
                    {
                        if (hitNode != null)
                        {
                            _selectedNodeId = hitNode;
                            _selectedLinkIndex = -1;
                            SyncIdBuffer();

                            _dragNodeId = hitNode;
                            _hasDraggedNode = false;
                            _dragStartMousePos = mp;
                            var nodeObj = FindInDoc(hitNode);
                            if (nodeObj != null) _dragNodeStartPosNorm = nodeObj.MapPos;

                            e.Use();
                            return;
                        }
                        else if (hitLink >= 0)
                        {
                            _selectedLinkIndex = hitLink;
                            _selectedNodeId = null;
                            e.Use();
                            return;
                        }
                        else
                        {
                            _selectedNodeId = null;
                            _selectedLinkIndex = -1;
                            _linkFrom = null;
                            _isPanning = true;
                            _panStartMouse = mp;
                            _panStartOrigin = _canvasPan;
                            e.Use();
                            return;
                        }
                    }
                }

                if (e.type == EventType.MouseDrag)
                {
                    e.Use();
                    return;
                }
            }
        }
        private bool HandleKeyboardShortcuts(Event e, Rect canvasRect, Vector2 localMapPos)
        {
            if (e.type != EventType.KeyDown) return false;

            if ((e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace))
            {
                if (!string.IsNullOrEmpty(_selectedNodeId))
                {
                    RecordUndo("Supprimer secteur");
                    DeleteNode(_selectedNodeId);
                    e.Use();
                    return true;
                }
                if (_selectedLinkIndex >= 0 && _selectedLinkIndex < _doc.Links.Count)
                {
                    RecordUndo("Supprimer liaison");
                    _doc.Links.RemoveAt(_selectedLinkIndex);
                    _selectedLinkIndex = -1;
                    _dirty = true;
                    _issuesStale = true;
                    SetStatus("Liaison supprimée.", Color.yellow);
                    e.Use();
                    return true;
                }
            }

            if (e.keyCode == KeyCode.F2 && !string.IsNullOrEmpty(_selectedNodeId))
            {
                var n = FindInDoc(_selectedNodeId);
                if (n != null)
                {
                    OpenRenameModal(n);
                    e.Use();
                    return true;
                }
            }

            if (e.keyCode == KeyCode.F && !string.IsNullOrEmpty(_selectedNodeId))
            {
                var n = FindInDoc(_selectedNodeId);
                if (n != null)
                {
                    FocusOnNode(n);
                    e.Use();
                    return true;
                }
            }

            if (e.control && e.keyCode == KeyCode.Z)
            {
                if (e.shift) PerformRedo();
                else PerformUndo();
                e.Use();
                return true;
            }

            if (e.control && e.keyCode == KeyCode.Y)
            {
                PerformRedo();
                e.Use();
                return true;
            }

            if (e.keyCode == KeyCode.Escape)
            {
                _linkFrom = null;
                _dragNodeId = null;
                _rulerStart = null;
                _rulerEnd = null;
                _contextMenuVisible = false;
                e.Use();
                return true;
            }

            return false;
        }
        private string HitNodeCanvas(Rect canvasRect, Vector2 localPos)
        {
            string best = null;
            float bestD = 26f * _canvasZoom;
            foreach (var n in _doc.Nodes)
            {
                if (n == null) continue;
                Vector2 p = HybrisWorldMapDraw.NodeCanvasPos(canvasRect, n);
                float d = Vector2.Distance(p, localPos);
                if (d < bestD) { bestD = d; best = n.Id; }
            }
            return best;
        }
        private int HitLinkCanvas(Rect canvasRect, Vector2 localPos)
        {
            float bestDist = 14f * _canvasZoom;
            int bestIdx = -1;

            for (int i = 0; i < _doc.Links.Count; i++)
            {
                var l = _doc.Links[i];
                if (l == null) continue;
                var a = FindInDoc(l.FromId);
                var b = FindInDoc(l.ToId);
                if (a == null || b == null) continue;

                Vector2 pa = HybrisWorldMapDraw.NodeCanvasPos(canvasRect, a);
                Vector2 pb = HybrisWorldMapDraw.NodeCanvasPos(canvasRect, b);
                float dist = HybrisWorldMapDraw.DistanceToSegment(localPos, pa, pb);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestIdx = i;
                }
            }
            return bestIdx;
        }
    }
}

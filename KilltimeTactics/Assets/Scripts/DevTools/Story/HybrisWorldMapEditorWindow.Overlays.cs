using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.UI;
using Killtime.Core.World;

namespace Killtime.Story
{
    public partial class HybrisWorldMapEditorWindow : FloatingWindow<HybrisWorldMapEditorWindow>
    {
        private void OpenRenameModal(HybrisSectorNode node)
        {
            if (node == null) return;
            _renameModalTargetId = node.Id;
            _renameModalNameBuffer = node.Name ?? "";
            _renameModalIdBuffer = node.Id ?? "";
            _showRenameModal = true;
        }
        private void DrawRenameModalOverlay()
        {
            if (!_showRenameModal) return;

            var node = FindInDoc(_renameModalTargetId);
            if (node == null)
            {
                _showRenameModal = false;
                return;
            }

            Event e = Event.current;

            if (e != null && e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                {
                    ConfirmRenameModal(node);
                    e.Use();
                    return;
                }
                if (e.keyCode == KeyCode.Escape)
                {
                    _showRenameModal = false;
                    e.Use();
                    return;
                }
            }

            float modalW = 420f;
            float modalH = 175f;
            float cx = _lastCanvasViewRect.width > 100f
                ? _lastCanvasViewRect.x + (_lastCanvasViewRect.width - modalW) * 0.5f
                : (Screen.width - modalW) * 0.5f;
            float cy = _lastCanvasViewRect.height > 100f
                ? _lastCanvasViewRect.y + (_lastCanvasViewRect.height - modalH) * 0.45f
                : (Screen.height - modalH) * 0.45f;
            Rect modalRect = new Rect(cx, cy, modalW, modalH);

            HybrisWorldMapDraw.FillRect(new Rect(0, 0, 5000f, 5000f), new Color(0f, 0f, 0f, 0.6f));
            HybrisWorldMapDraw.FillRect(modalRect, new Color(0.08f, 0.11f, 0.16f, 0.98f));
            HybrisWorldMapDraw.DrawLine(new Vector2(modalRect.x, modalRect.y), new Vector2(modalRect.xMax, modalRect.y), new Color(0.3f, 0.7f, 1f, 0.9f), 2f);
            HybrisWorldMapDraw.DrawLine(new Vector2(modalRect.x, modalRect.yMax), new Vector2(modalRect.xMax, modalRect.yMax), new Color(0.2f, 0.4f, 0.6f, 0.6f), 1f);
            HybrisWorldMapDraw.DrawLine(new Vector2(modalRect.x, modalRect.y), new Vector2(modalRect.x, modalRect.yMax), new Color(0.2f, 0.4f, 0.6f, 0.6f), 1f);
            HybrisWorldMapDraw.DrawLine(new Vector2(modalRect.xMax, modalRect.y), new Vector2(modalRect.xMax, modalRect.yMax), new Color(0.2f, 0.4f, 0.6f, 0.6f), 1f);

            GUILayout.BeginArea(new Rect(modalRect.x + 16f, modalRect.y + 12f, modalRect.width - 32f, modalRect.height - 24f));
            GUILayout.Label($"<b><size=13>Renommer le Secteur : {node.Name}</size></b>");
            GUILayout.Space(6);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Nom Affiché :", GUILayout.Width(110));
            _renameModalNameBuffer = GUILayout.TextField(_renameModalNameBuffer ?? "");
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("ID Unique :", GUILayout.Width(110));
            _renameModalIdBuffer = GUILayout.TextField(_renameModalIdBuffer ?? "");
            GUILayout.EndHorizontal();

            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.2f, 0.85f, 0.4f);
            if (GUILayout.Button("Valider (Entrée)", GUILayout.Height(28)))
            {
                ConfirmRenameModal(node);
            }

            GUI.backgroundColor = new Color(0.7f, 0.25f, 0.25f);
            if (GUILayout.Button("Annuler (Échap)", GUILayout.Height(28)))
            {
                _showRenameModal = false;
            }
            GUI.backgroundColor = Color.white;
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }
        private void ConfirmRenameModal(HybrisSectorNode node)
        {
            RecordUndo("Renommage secteur");
            string newName = (_renameModalNameBuffer ?? "").Trim();
            string targetId = (_renameModalIdBuffer ?? "").Trim();

            node.Name = string.IsNullOrEmpty(newName) ? node.Name : newName;

            bool renameOk = true;
            if (!string.IsNullOrEmpty(targetId) && targetId != node.Id)
            {
                renameOk = RenameNode(node, targetId);
            }

            if (renameOk)
            {
                _dirty = true;
                _issuesStale = true;
                SetStatus($"Secteur mis à jour : « {node.Name} » ({node.Id}).", Color.green);
                _showRenameModal = false;
            }
        }
        private void OpenContextMenuAt(Rect canvasRect, Vector2 localMapPos, Vector2 screenMousePos)
        {
            _contextMenuItems.Clear();
            _contextMenuWindowPos = screenMousePos;

            string hitNode = HitNodeCanvas(canvasRect, localMapPos);
            int hitLink = HitLinkCanvas(canvasRect, localMapPos);

            if (hitNode != null)
            {
                BuildContextMenuForNode(hitNode);
            }
            else if (hitLink >= 0)
            {
                BuildContextMenuForLink(hitLink);
            }
            else
            {
                BuildContextMenuForCanvas(canvasRect, localMapPos);
            }

            if (_contextMenuItems.Count > 0)
            {
                _contextMenuVisible = true;
            }
        }
        private void BuildContextMenuForNode(string nodeId)
        {
            var node = FindInDoc(nodeId);
            if (node == null) return;

            _selectedNodeId = nodeId;
            _selectedLinkIndex = -1;
            SyncIdBuffer();

            _contextMenuItems.Add(new ContextMenuItem { Label = $"<b>Secteur : {node.Name}</b>", IsDisabled = true });
            _contextMenuItems.Add(new ContextMenuItem { IsSeparator = true });

            _contextMenuItems.Add(new ContextMenuItem
            {
                Label = "🎯 Centrer la vue",
                Action = () => FocusOnNode(node)
            });

            _contextMenuItems.Add(new ContextMenuItem
            {
                Label = "✏️ Renommer le secteur (F2)",
                Action = () => OpenRenameModal(node)
            });

            _contextMenuItems.Add(new ContextMenuItem
            {
                Label = "🔗 Lier vers un autre secteur",
                Action = () =>
                {
                    _mapMode = 2;
                    _linkFrom = nodeId;
                    SetStatus($"Mode liaison activé depuis {node.Name}. Cliquez sur la cible.", Color.yellow);
                }
            });

            _contextMenuItems.Add(new ContextMenuItem
            {
                Label = "📋 Dupliquer le secteur",
                Action = () => DuplicateNode(node)
            });

            _contextMenuItems.Add(new ContextMenuItem
            {
                Label = "🧲 Reprojeter vers le centre Hex",
                Action = () =>
                {
                    RecordUndo("Snap axial");
                    var axial = HybrisWorldMapData.NormToAxial(node.MapPos);
                    node.HexQ = axial.x;
                    node.HexR = axial.y;
                    node.MapPos = HybrisWorldMapData.AxialToNorm(node.HexQ, node.HexR);
                    _dirty = true;
                    _issuesStale = true;
                }
            });

            _contextMenuItems.Add(new ContextMenuItem { IsSeparator = true });
            _contextMenuItems.Add(new ContextMenuItem
            {
                Label = "🗑 Supprimer le secteur",
                Action = () =>
                {
                    RecordUndo("Supprimer secteur");
                    DeleteNode(node.Id);
                }
            });
        }
        private void BuildContextMenuForLink(int linkIdx)
        {
            if (linkIdx < 0 || linkIdx >= _doc.Links.Count) return;
            var l = _doc.Links[linkIdx];
            if (l == null) return;

            _selectedLinkIndex = linkIdx;
            _selectedNodeId = null;

            _contextMenuItems.Add(new ContextMenuItem { Label = $"<b>Liaison : {l.FromId} ↔ {l.ToId}</b>", IsDisabled = true });
            _contextMenuItems.Add(new ContextMenuItem { IsSeparator = true });

            _contextMenuItems.Add(new ContextMenuItem
            {
                Label = "⚡ Recalculer Miles & Jours (Canon)",
                Action = () =>
                {
                    RecordUndo("Auto calcul liaison");
                    AutoCalculateLinkMetrics(l);
                    _dirty = true;
                }
            });

            _contextMenuItems.Add(new ContextMenuItem
            {
                Label = l.IsSeaCrossing ? "🚶 Convertir en Route Terrestre" : "⛵ Convertir en Traversée Maritime",
                Action = () =>
                {
                    RecordUndo("Type liaison");
                    l.IsSeaCrossing = !l.IsSeaCrossing;
                    AutoCalculateLinkMetrics(l);
                    _dirty = true;
                }
            });

            _contextMenuItems.Add(new ContextMenuItem { IsSeparator = true });
            _contextMenuItems.Add(new ContextMenuItem
            {
                Label = "🗑 Supprimer cette liaison",
                Action = () =>
                {
                    RecordUndo("Supprimer liaison");
                    _doc.Links.RemoveAt(linkIdx);
                    _selectedLinkIndex = -1;
                    _dirty = true;
                    _issuesStale = true;
                    SetStatus("Liaison supprimée.", Color.yellow);
                }
            });
        }
        private void BuildContextMenuForCanvas(Rect canvasRect, Vector2 localMapPos)
        {
            float nx = Mathf.Clamp01((localMapPos.x - _canvasPan.x) / canvasRect.width);
            float ny = Mathf.Clamp01((localMapPos.y - _canvasPan.y) / canvasRect.height);
            var axial = HybrisWorldMapData.NormToAxial(new Vector2(nx, ny));

            _contextMenuItems.Add(new ContextMenuItem { Label = $"<b>Position Axiale : (Q:{axial.x}, R:{axial.y}, S:{-axial.x - axial.y})</b>", IsDisabled = true });
            _contextMenuItems.Add(new ContextMenuItem { IsSeparator = true });

            _contextMenuItems.Add(new ContextMenuItem
            {
                Label = "➕ Créer un secteur ici",
                Action = () =>
                {
                    RecordUndo("Créer secteur");
                    CreateNodeAt(new Vector2(nx, ny));
                }
            });

            _contextMenuItems.Add(new ContextMenuItem
            {
                Label = "🎯 Recentrer / Ajuster vue",
                Action = () => _hasAutoFittedView = false
            });

            _contextMenuItems.Add(new ContextMenuItem
            {
                Label = _snapToHex ? "🔓 Désactiver Magnétisme Hex" : "🧲 Activer Magnétisme Hex",
                Action = () => _snapToHex = !_snapToHex
            });

            _contextMenuItems.Add(new ContextMenuItem
            {
                Label = _showHexGrid ? "Masquer Grille Hex" : "Afficher Grille Hex",
                Action = () => _showHexGrid = !_showHexGrid
            });
        }
        private void DrawContextMenuOverlay()
        {
            if (!_contextMenuVisible || _contextMenuItems.Count == 0) return;

            Event e = Event.current;
            float itemH = 22f;
            float sepH = 6f;
            float totalH = 10f;

            for (int i = 0; i < _contextMenuItems.Count; i++)
            {
                totalH += _contextMenuItems[i].IsSeparator ? sepH : itemH;
            }

            float menuW = 230f;
            Rect menuRect = new Rect(
                Mathf.Min(_contextMenuWindowPos.x, Screen.width - menuW - 10f),
                Mathf.Min(_contextMenuWindowPos.y, Screen.height - totalH - 10f),
                menuW,
                totalH
            );

            if (e != null && e.type == EventType.MouseDown && !menuRect.Contains(e.mousePosition))
            {
                _contextMenuVisible = false;
                e.Use();
                return;
            }

            HybrisWorldMapDraw.FillRect(menuRect, new Color(0.08f, 0.11f, 0.16f, 0.98f));
            HybrisWorldMapDraw.DrawLine(new Vector2(menuRect.x, menuRect.y), new Vector2(menuRect.xMax, menuRect.y), new Color(0.3f, 0.7f, 1f, 0.9f), 2f);
            HybrisWorldMapDraw.DrawLine(new Vector2(menuRect.x, menuRect.yMax), new Vector2(menuRect.xMax, menuRect.yMax), new Color(0.2f, 0.4f, 0.6f, 0.6f), 1f);
            HybrisWorldMapDraw.DrawLine(new Vector2(menuRect.x, menuRect.y), new Vector2(menuRect.x, menuRect.yMax), new Color(0.2f, 0.4f, 0.6f, 0.6f), 1f);
            HybrisWorldMapDraw.DrawLine(new Vector2(menuRect.xMax, menuRect.y), new Vector2(menuRect.xMax, menuRect.yMax), new Color(0.2f, 0.4f, 0.6f, 0.6f), 1f);

            float curY = menuRect.y + 5f;
            for (int i = 0; i < _contextMenuItems.Count; i++)
            {
                var item = _contextMenuItems[i];
                if (item.IsSeparator)
                {
                    HybrisWorldMapDraw.DrawLine(
                        new Vector2(menuRect.x + 8f, curY + sepH * 0.5f),
                        new Vector2(menuRect.xMax - 8f, curY + sepH * 0.5f),
                        new Color(1f, 1f, 1f, 0.18f),
                        1f);
                    curY += sepH;
                }
                else
                {
                    Rect r = new Rect(menuRect.x + 4f, curY, menuRect.width - 8f, itemH);
                    if (item.IsDisabled)
                    {
                        GUI.Label(r, item.Label);
                    }
                    else
                    {
                        bool hover = r.Contains(e.mousePosition);
                        if (hover) HybrisWorldMapDraw.FillRect(r, new Color(0.2f, 0.5f, 0.8f, 0.45f));

                        if (GUI.Button(r, item.Label, GUI.skin.label))
                        {
                            _contextMenuVisible = false;
                            item.Action?.Invoke();
                            if (e != null) e.Use();
                            return;
                        }
                    }
                    curY += itemH;
                }
            }
        }
    }
}

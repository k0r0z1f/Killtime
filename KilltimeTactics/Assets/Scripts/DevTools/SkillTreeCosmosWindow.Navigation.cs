using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Core.Character;
using Killtime.Core.Character.Classes;
using Killtime.Core.Dice;
using Killtime.Tactics.Units;

namespace Killtime.UI
{
    public partial class SkillTreeCosmosWindow : FloatingWindow<SkillTreeCosmosWindow>
    {
        /// <summary>Recentre la caméra sur la première étape NEXT du guide.</summary>
        private void FocusNextGuideStep()
        {
            var guide = CharacterClassCatalog.GetActiveGuide(_sheet);
            if (guide == null || _sheet == null) return;
            var next = guide.GetNextSteps(_sheet);
            if (next.Count == 0) return;
            string target = next[0];
            for (int i = 0; i < _allNodes.Count; i++)
            {
                var n = _allNodes[i];
                if (n == null) continue;
                if ((n.IsSpecialization || n.IsImprovement) && n.SpecializationName == target)
                {
                    _selectedNode = n;
                    _canvasPan = new Vector2(-n.Position.x, -n.Position.y);
                    _canvasZoom = 1.05f;
                    _statusFeedback = $"◆ Prochaine étape du build : {target}";
                    return;
                }
            }
            // Étape d'entraînement (compétence) : recentre sur le nœud de compétence.
            for (int i = 0; i < _allNodes.Count; i++)
            {
                var n = _allNodes[i];
                if (n == null || n.IsSpecialization || n.IsImprovement) continue;
                if (guide.IsSkillNext(_sheet, n.Skill))
                {
                    _selectedNode = n;
                    _canvasPan = new Vector2(-n.Position.x, -n.Position.y);
                    _canvasZoom = 1.05f;
                    _statusFeedback = $"◆ Entraînement suivant : {n.DisplayName} (cible +{guide.GetTargetTraining(n.Skill)})";
                    return;
                }
            }
        }

        private void FocusSector(float x, float y)
        {
            _canvasPan = new Vector2(-x, -y);
            _canvasZoom = 1.05f;
        }

        private void ResetView()
        {
            _canvasPan = Vector2.zero;
            _canvasZoom = 0.68f;
        }

        private void HandleCanvasNavigation(Rect viewportRect)
        {
            Event e = Event.current;
            if (!viewportRect.Contains(e.mousePosition)) return;

            Vector2 viewCenter = new Vector2(viewportRect.width * 0.5f, viewportRect.height * 0.5f);
            Vector2 localMouse = e.mousePosition - viewportRect.min;

            if (e.type == EventType.MouseDown)
            {
                var hitNode = FindNodeAtScreenPos(localMouse, viewCenter);

                if (hitNode != null && e.button == 0)
                {
                    _selectedNode = hitNode;
                    e.Use();
                    return;
                }

                if (e.button == 0 || e.button == 1 || e.button == 2)
                {
                    _isPanning = true;
                    _lastMousePos = e.mousePosition;
                    e.Use();
                }
            }
            else if (e.type == EventType.MouseDrag && _isPanning)
            {
                Vector2 delta = e.mousePosition - _lastMousePos;
                _canvasPan += delta / _canvasZoom;
                _lastMousePos = e.mousePosition;
                e.Use();
            }
            else if (e.type == EventType.MouseUp && _isPanning)
            {
                _isPanning = false;
                e.Use();
            }

            if (e.type == EventType.ScrollWheel)
            {
                float zoomFactor = e.delta.y > 0 ? 0.88f : 1.12f;
                _canvasZoom = Mathf.Clamp(_canvasZoom * zoomFactor, 0.35f, 2.5f);
                e.Use();
            }
        }

        private bool IsNodeVisible(CosmosNode node)
        {
            if (node == null) return false;
            if (_showHiddenCheats) return true;
            if (IsNodeUnlocked(node)) return true;

            if (CharacterProgressionManager.IsVolume2Specialization(node.SpecializationName))
            {
                if (!CharacterProgressionManager.IsVolume2Unlocked())
                {
                    return false;
                }
            }

            if (CharacterProgressionManager.IsHiddenSpecialization(node.SpecializationName))
            {
                bool parentUnlocked = node.ParentSkillNode != null && IsNodeUnlocked(node.ParentSkillNode);
                if (!parentUnlocked)
                {
                    return false;
                }
            }

            return true;
        }

        private CosmosNode FindNodeAtScreenPos(Vector2 localMousePos, Vector2 viewCenter)
        {
            for (int i = _allNodes.Count - 1; i >= 0; i--)
            {
                var node = _allNodes[i];
                if (!IsNodeVisible(node)) continue;
                Vector2 screenPos = WorldToScreen(node.Position, viewCenter);
                float radius = (node.IsSpecialization ? 16f : 24f) * _canvasZoom;
                if (Vector2.Distance(localMousePos, screenPos) <= radius)
                {
                    return node;
                }
            }
            return null;
        }
    }
}

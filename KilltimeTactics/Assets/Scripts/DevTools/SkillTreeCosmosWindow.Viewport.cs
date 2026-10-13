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
        private void DrawCosmosViewport(Rect viewport)
        {
            GUI.BeginClip(viewport);

            DrawSolidRect(new Rect(0, 0, viewport.width, viewport.height), new Color(0.010f, 0.018f, 0.032f, 1.0f));

            Vector2 viewCenter = new Vector2(viewport.width * 0.5f, viewport.height * 0.5f);

            DrawBackdropStarfield(viewCenter, viewport.size);

            // 1. Conduits
            var activeGuideForLines = _frameGuide ?? CharacterClassCatalog.GetActiveGuide(_sheet);
            float cullMargin = 120f;
            for (int i = 0; i < _conduits.Count; i++)
            {
                var c = _conduits[i];
                if (!IsNodeVisible(c.From) || !IsNodeVisible(c.To)) continue;

                Vector2 p1 = WorldToScreen(c.From.Position, viewCenter);
                Vector2 p2 = WorldToScreen(c.To.Position, viewCenter);
                if (IsOffscreen(p1, viewport, cullMargin) && IsOffscreen(p2, viewport, cullMargin)) continue;

                bool isUnlocked = IsNodeUnlocked(c.To);
                Color lineCol = isUnlocked ? c.ConduitColor : new Color(c.ConduitColor.r, c.ConduitColor.g, c.ConduitColor.b, 0.18f);
                float thickness = isUnlocked ? 2.5f * _canvasZoom : 1.2f * _canvasZoom;

                DrawCosmicLine(p1, p2, lineCol, thickness);

                // Surbrillance or du chemin de build (jamais de verrou, pure indication).
                if (!isUnlocked && activeGuideForLines != null && c.To != null
                    && (c.To.IsSpecialization || c.To.IsImprovement))
                {
                    var gs = activeGuideForLines.GetSpecState(_sheet, c.To.SpecializationName);
                    if (gs == CharacterClassDefinition.GuideStepState.Next)
                        DrawCosmicLine(p1, p2, new Color(1f, 0.82f, 0.3f, 0.85f), 3.2f * _canvasZoom);
                    else if (gs == CharacterClassDefinition.GuideStepState.Future)
                        DrawCosmicLine(p1, p2, new Color(1f, 0.82f, 0.3f, 0.22f), 1.6f * _canvasZoom);
                }
            }

            // 2. Titres des Constellations
            for (int i = 0; i < _sectors.Count; i++)
            {
                var sec = _sectors[i];
                Vector2 p = WorldToScreen(sec.Position + new Vector2(0f, -160f), viewCenter);
                if (IsOffscreen(p, viewport, 240f)) continue;
                Color c = sec.SectorColor;
                c.a = 0.65f;
                _constellationTitleStyle.normal.textColor = c;
                GUI.Label(new Rect(p.x - 220f, p.y, 440f, 22f), sec.Name, _constellationTitleStyle);
            }

            // 3. Étoiles (LOD : zoom < 0.5 = forme seule, halo/labels réservés aux nœuds d'intérêt)
            Vector2 localMouse = Event.current.mousePosition;
            _hoveredNode = null;
            bool lowDetail = _canvasZoom < 0.5f;

            for (int i = 0; i < _allNodes.Count; i++)
            {
                var node = _allNodes[i];
                if (!IsNodeVisible(node)) continue;

                Vector2 screenPos = WorldToScreen(node.Position, viewCenter);
                if (IsOffscreen(screenPos, viewport, cullMargin)) continue;

                float baseRadius = node.IsSpecialization ? 12f : 18f;
                float drawRadius = baseRadius * _canvasZoom;

                bool isHover = Vector2.Distance(localMouse, screenPos) <= drawRadius * 1.3f;
                if (isHover) _hoveredNode = node;
                bool isSelected = (_selectedNode == node);
                bool unlocked = IsNodeUnlocked(node);

                DrawCosmicStar(node, screenPos, drawRadius, isSelected, isHover, unlocked, lowDetail);
            }

            // 4. Légende
            GUI.color = new Color(0.35f, 0.55f, 0.75f, 0.7f);
            GUI.Label(new Rect(8, viewport.height - 18, 480, 16), "Glisser (Clic-Gauche / Clic-Droit) pour explorer | Molette pour zoomer");
            GUI.color = Color.white;

            GUI.EndClip();
        }

        private Vector2 WorldToScreen(Vector2 worldPos, Vector2 viewCenter)
        {
            return viewCenter + (worldPos + _canvasPan) * _canvasZoom;
        }

        private static bool IsOffscreen(Vector2 p, Rect viewport, float margin)
        {
            return p.x < -margin || p.y < -margin || p.x > viewport.width + margin || p.y > viewport.height + margin;
        }

        private void DrawBackdropStarfield(Vector2 viewCenter, Vector2 size)
        {
            if (_starfieldPos == null) InitProceduralTextures();
            for (int i = 0; i < _starfieldPos.Length; i++)
            {
                Vector2 p = WorldToScreen(_starfieldPos[i], viewCenter);
                if (p.x >= 0 && p.x <= size.x && p.y >= 0 && p.y <= size.y)
                {
                    float starSize = (i % 3 == 0) ? 2.5f : 1.5f;
                    GUI.DrawTexture(new Rect(p.x, p.y, starSize, starSize), _pixelTex);
                }
            }
        }

        private void DrawCosmicStar(CosmosNode node, Vector2 center, float radius, bool isSelected, bool isHover, bool unlocked, bool lowDetail)
        {
            bool isSecret = CharacterProgressionManager.IsHiddenSpecialization(node.SpecializationName);
            bool isVol2 = CharacterProgressionManager.IsVolume2Specialization(node.SpecializationName);
            bool isVol2Locked = isVol2 && !CharacterProgressionManager.IsVolume2Unlocked();

            // Highlight guide de build (arbre libre : simple surlignage or, jamais de verrou).
            var guideState = GetGuideSpecState(node);
            bool isGuideNextSkill = IsGuideNextSkill(node);
            bool isNext = guideState == CharacterClassDefinition.GuideStepState.Next || isGuideNextSkill;
            bool isFuture = guideState == CharacterClassDefinition.GuideStepState.Future;

            // En zoom-out, seuls les nœuds d'intérêt gardent halo + labels.
            bool showExtras = !lowDetail || isSelected || isHover || isNext;

            float pulse = 0.85f + 0.15f * Mathf.Sin(Time.realtimeSinceStartup * 4f + node.Position.x);
            Color starCol = isSecret ? new Color(1f, 0.35f, 0.85f) : node.NodeColor;

            if (!unlocked)
            {
                starCol = Color.Lerp(Color.gray, starCol, 0.25f);
                starCol.a = isSecret ? 0.55f : 0.35f;
            }
            else
            {
                starCol.a = pulse;
            }

            if (isSelected) starCol = Color.white;

            if (showExtras)
            {
                float haloR = radius * (isSelected ? 3.4f : (isHover ? 2.8f : 2.0f));
                Color haloCol = isSecret ? new Color(1f, 0.2f, 0.75f) : node.NodeColor;
                haloCol.a = (isSelected ? 0.75f : 0.35f) * (unlocked ? 1f : 0.30f);

                Color prevCol = GUI.color;
                GUI.color = haloCol;
                GUI.DrawTexture(new Rect(center.x - haloR, center.y - haloR, haloR * 2f, haloR * 2f), _starGlowTex);

                // Halo or du guide : NEXT = pulsant fort, FUTURE = discret.
                if (!unlocked && (isNext || isFuture))
                {
                    float goldPulse = isNext
                        ? 0.65f + 0.35f * Mathf.Sin(Time.realtimeSinceStartup * 5f)
                        : 0.25f;
                    Color gold = new Color(1f, 0.82f, 0.35f, isNext ? (0.55f + goldPulse * 0.4f) : 0.28f);
                    GUI.color = gold;
                    float goldR = radius * (isNext ? 2.6f : 2.1f);
                    GUI.DrawTexture(new Rect(center.x - goldR, center.y - goldR, goldR * 2f, goldR * 2f), _starGlowTex);
                }
                GUI.color = prevCol;
            }

            Color prevCol2 = GUI.color;

            GUI.color = starCol;
            Texture2D shapeTex = (node.IsSpecialization || node.IsImprovement) ? _starDiamondTex : _starCircleTex;
            GUI.DrawTexture(new Rect(center.x - radius, center.y - radius, radius * 2f, radius * 2f), shapeTex);
            GUI.color = prevCol2;

            if (isSelected)
            {
                GUI.color = isSecret ? new Color(1f, 0.4f, 0.9f, 0.95f) : new Color(0f, 0.95f, 1f, 0.9f);
                float reticleR = radius * 1.45f;
                DrawWireSquare(new Rect(center.x - reticleR, center.y - reticleR, reticleR * 2f, reticleR * 2f));
                GUI.color = prevCol2;
            }

            // Réticule or pour la prochaine étape du build (visible même sans sélection).
            if (!isSelected && isNext && !unlocked)
            {
                GUI.color = new Color(1f, 0.82f, 0.3f, 0.65f + 0.3f * Mathf.Sin(Time.realtimeSinceStartup * 5f));
                float reticleR = radius * 1.7f;
                DrawWireSquare(new Rect(center.x - reticleR, center.y - reticleR, reticleR * 2f, reticleR * 2f));
                GUI.color = prevCol2;
            }

            if (_canvasZoom >= 0.28f && showExtras)
            {
                int training = GetSkillTrainingLevel(node);
                string tag;
                string guideTag = isNext ? "\n<color=#FFD166><b>◆ SUIVANT BUILD</b></color>"
                    : (isFuture ? "\n<color=#8A6D2B>◇ build</color>" : "");
                if (isSecret && !unlocked && !_showHiddenCheats)
                {
                    tag = "<color=#F43F5E>🔒 ??? [Secret]</color>";
                }
                else if (isSecret)
                {
                    tag = unlocked ? $"<color=#F43F5E>★ {node.DisplayName}</color>" : $"<color=#FB7185>☆ {node.DisplayName} [SECRET]</color>";
                    tag += guideTag;
                }
                else if (isVol2Locked && _showHiddenCheats)
                {
                    tag = unlocked ? $"<color=#10B981>★ {node.DisplayName}</color>" : $"<color=#C084FC>☆ {node.DisplayName} [VOL II]</color>";
                    tag += guideTag;
                }
                else if (node.IsImprovement)
                {
                    tag = unlocked ? $"◆ {node.DisplayName}" : $"◇ {node.DisplayName}";
                    tag += guideTag;
                }
                else if (node.IsSpecialization)
                {
                    tag = unlocked ? $"★ {node.DisplayName}" : $"☆ {node.DisplayName}";
                    tag += guideTag;
                }
                else
                {
                    string dieTag = GetSkillDieString(node.Skill);
                    string pips = TrainingPips(training);
                    string caracs = SkillDefinitions.GetAssociatedAttributeNames(node.Skill);
                    tag = $"<b>{node.DisplayName}</b>\n<color=#00E5FF>[{dieTag}]</color> {pips}\n<color=#FFD27F>{caracs}</color>";
                    if (isGuideNextSkill)
                    {
                        var g = _frameGuide ?? CharacterClassCatalog.GetActiveGuide(_sheet);
                        int tgt = g != null ? g.GetTargetTraining(node.Skill) : 0;
                        tag += $"\n<color=#FFD166><b>◆ BUILD +{tgt}</b></color>";
                    }
                }

                _nodeLabelStyle.normal.textColor = isNext && !unlocked
                    ? new Color(1f, 0.85f, 0.45f)
                    : (unlocked ? Color.white : new Color(0.65f, 0.72f, 0.82f, 0.65f));
                GUI.Label(new Rect(center.x - 90f, center.y + radius + 3f, 180f, 60f), tag, _nodeLabelStyle);
            }
        }

        private void DrawWireSquare(Rect r)
        {
            DrawSolidRect(new Rect(r.x, r.y, r.width, 1.5f), GUI.color);
            DrawSolidRect(new Rect(r.x, r.yMax - 1.5f, r.width, 1.5f), GUI.color);
            DrawSolidRect(new Rect(r.x, r.y, 1.5f, r.height), GUI.color);
            DrawSolidRect(new Rect(r.xMax - 1.5f, r.y, 1.5f, r.height), GUI.color);
        }

        private void DrawCosmicLine(Vector2 a, Vector2 b, Color color, float width)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 1f) return;

            float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;

            GUIUtility.RotateAroundPivot(angle, a);
            Color prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(a.x, a.y - width * 0.5f, len, width), _pixelTex);
            GUI.color = prev;
            GUIUtility.RotateAroundPivot(-angle, a);
        }

        private static void DrawSolidRect(Rect r, Color c)
        {
            Color prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, _pixelTex);
            GUI.color = prev;
        }
    }
}

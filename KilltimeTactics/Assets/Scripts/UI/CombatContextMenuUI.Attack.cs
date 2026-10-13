// Rendu radial attaque : OnGUI dispatch + hub central + orbites categories/actions (extrait de CombatContextMenuUI.cs).
using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.UI;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Core.Character;
using Killtime.Core.Combat;

namespace Killtime.Tactics.CombatUI
{
    public partial class CombatContextMenuUI : MonoBehaviour
    {
        private void OnGUI()
        {
            Event e = Event.current;
            Vector2 mousePos = e.mousePosition;
            float distFromCenter = Vector2.Distance(mousePos, _centerPos);
            bool isInsideMenu = distFromCenter <= (OuterRadius + 45f);

            // Phase de blocage après fermeture : consomme les événements résiduels
            if (!_isOpen)
            {
                if (_blockUntilMouseUp && isInsideMenu)
                {
                    if (e.type == EventType.MouseDown || e.type == EventType.MouseUp || e.type == EventType.ContextClick)
                    {
                        e.Use();
                    }
                }
                return;
            }

            if (_contextTarget == null) return;

            // Mode Défense : tout le paramétrage vit dans le radial, centré sur le
            // défenseur comme après un clic droit sur lui. Aucun modal séparé.
            if (_isDefenseMode)
            {
                if (_defenseDefender == null || _defenseDuel == null)
                {
                    CloseMenu();
                    return;
                }
                EnsureProceduralTextures();
                // Le menu suit la cible si la caméra bouge.
                _centerPos = DefenseScreenCenter(_defenseDefender);
                UpdateDefenseAnchor();
                DrawDefenseBackdrop();
                DrawDefenseHub();
                DrawDefenseRing(mousePos, e);
                if (e.type == EventType.MouseDown || e.type == EventType.MouseUp || e.type == EventType.ContextClick || e.type == EventType.ScrollWheel)
                {
                    e.Use();
                }
                return;
            }

            EnsureProceduralTextures();
            var activeActor = GetActiveActor();
            if (activeActor == null) return;

            bool isCurrentTurnUnit = _turnManager != null
                && !_turnManager.IsInExploration
                && !_turnManager.IsCombatOver
                && _turnManager.ActiveUnit == _contextTarget
                && _contextTarget.IsPlayerControlled;

            DrawHoloBackdrop();
            DrawCenterHub(activeActor);
            DrawCategoryOrbit(mousePos, e, isCurrentTurnUnit);
            DrawActionOrbit(activeActor, mousePos, e, isCurrentTurnUnit);

            if (_hoveredCategory.HasValue)
            {
                DrawCategoryTooltip(_hoveredCategory.Value, _hoveredCategoryPos);
            }

            // Consommation systématique de tout clic résiduel dans l'anneau pour interdire la propagation
            if (isInsideMenu && (e.type == EventType.MouseDown || e.type == EventType.MouseUp || e.type == EventType.ContextClick))
            {
                e.Use();
            }
        }

        private void DrawHoloBackdrop()
        {
            Color prevColor = GUI.color;

            GUI.color = new Color(0f, 0.7f, 1f, 0.12f);
            float glowSize = (OuterRadius + 40f) * 2f;
            GUI.DrawTexture(new Rect(_centerPos.x - glowSize * 0.5f, _centerPos.y - glowSize * 0.5f, glowSize, glowSize), _glowTex);

            GUI.color = new Color(0f, 0.85f, 1f, 0.30f);
            float ringOuterSize = OuterRadius * 2f;
            GUI.DrawTexture(new Rect(_centerPos.x - OuterRadius, _centerPos.y - OuterRadius, ringOuterSize, ringOuterSize), _ringTex);

            GUI.color = new Color(0f, 0.85f, 1f, 0.18f);
            float ringInnerSize = InnerRadius * 2f;
            GUI.DrawTexture(new Rect(_centerPos.x - InnerRadius, _centerPos.y - InnerRadius, ringInnerSize, ringInnerSize), _ringTex);

            GUI.color = prevColor;
        }

        private void DrawCenterHub(TacticalUnit activeActor)
        {
            Color prevColor = GUI.color;

            int hoveredBonusAP = (_hoveredAction != null && _bonusAPMap.TryGetValue(_hoveredAction, out int b)) ? b : 0;
            bool hoveredCanAfford = false;
            if (_hoveredAction != null)
            {
                int totalCost = _hoveredAction.ActionPointCost + hoveredBonusAP;
                hoveredCanAfford = activeActor.Stats.CurrentActionPoints >= totalCost && _hoveredAction.CanExecute(activeActor, _contextTarget);
            }

            Color hubBorderCol = _hoveredAction != null
                ? (!hoveredCanAfford ? ColorArcaneCrimson : (hoveredBonusAP > 0 ? ColorArcaneAmber : ColorArcaneCyan))
                : ColorArcaneCyan;

            // Halo de surbrillance doux pour détacher le hub du décor 3D
            Color glowCol = _hoveredAction != null
                ? (!hoveredCanAfford ? new Color(1f, 0.25f, 0.35f, 0.16f) : (hoveredBonusAP > 0 ? new Color(1f, 0.75f, 0.1f, 0.20f) : new Color(0f, 0.92f, 1f, 0.16f)))
                : new Color(0f, 0.92f, 1f, 0.12f);
            GUI.color = glowCol;
            float hubGlowSize = HubRadius * 2.3f;
            GUI.DrawTexture(new Rect(_centerPos.x - hubGlowSize * 0.5f, _centerPos.y - hubGlowSize * 0.5f, hubGlowSize, hubGlowSize), _glowTex);

            // Fond sombre opaque pour un contraste maximal
            GUI.color = ColorHubBackground;
            GUI.DrawTexture(new Rect(_centerPos.x - HubRadius, _centerPos.y - HubRadius, HubRadius * 2f, HubRadius * 2f), _circleTex);

            // Bordure lumineuse
            GUI.color = new Color(hubBorderCol.r, hubBorderCol.g, hubBorderCol.b, 0.90f);
            GUI.DrawTexture(new Rect(_centerPos.x - HubRadius, _centerPos.y - HubRadius, HubRadius * 2f, HubRadius * 2f), _ringTex);

            // Zone intérieure réservée au texte
            float usableWidth = HubRadius * 1.54f;
            float usableHeight = HubRadius * 1.54f;
            Rect hubRect = new Rect(_centerPos.x - usableWidth * 0.5f, _centerPos.y - usableHeight * 0.5f, usableWidth, usableHeight);

            // IMPORTANT: Réinitialiser GUI.color à blanc pour éviter que la teinte de bordure ne s'applique aux textes
            GUI.color = Color.white;

            GUILayout.BeginArea(hubRect);
            GUILayout.BeginVertical();
            GUILayout.FlexibleSpace();

            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                wordWrap = true,
                richText = true,
                clipping = TextClipping.Clip
            };
            var statusStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                wordWrap = true,
                richText = true
            };
            var bodyStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 10,
                wordWrap = true,
                richText = true
            };
            var descStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = (_hoveredAction != null && _hoveredAction.Description.Length > 115) ? 9 : 10,
                wordWrap = true,
                richText = true
            };

            if (_hoveredAction != null)
            {
                int currentAP = activeActor.Stats.CurrentActionPoints;
                int totalCost = _hoveredAction.ActionPointCost + hoveredBonusAP;
                int remainingAP = currentAP - totalCost;
                bool hasEnoughAP = remainingAP >= 0;
                bool isExecutable = _hoveredAction.CanExecute(activeActor, _contextTarget);
                bool canAfford = hasEnoughAP && isExecutable;
                bool allowsVar = ActionAllowsVariableAP(_hoveredAction);

                // Titre toujours blanc éclatant pour une lisibilité parfaite
                titleStyle.normal.textColor = Color.white;
                GUI.color = Color.white;
                GUILayout.Label(_hoveredAction.Title, titleStyle);

                int dist = activeActor.CurrentCoords.DistanceTo(_contextTarget.CurrentCoords);
                int maxRange = CombatActionRegistry.GetAttackMaxRange(activeActor);

                if (!isExecutable && dist > maxRange)
                {
                    GUI.color = new Color(1f, 0.45f, 0.45f, 1f); // Rouge corail vif très lisible sur fond noir
                    GUILayout.Label($"⚠️ <b>HORS DE PORTÉE</b> ({dist} / max {maxRange} cases)", statusStyle);
                }
                else if (!hasEnoughAP)
                {
                    GUI.color = new Color(1f, 0.45f, 0.45f, 1f);
                    GUILayout.Label($"⚠️ <b>PA INSUFFISANTS</b> ({totalCost} req. | {currentAP} dispo)", statusStyle);
                }
                else if (hoveredBonusAP > 0)
                {
                    GUI.color = ColorArcaneAmber;
                    GUILayout.Label($"⚡ <b>{totalCost} PA</b> ({_hoveredAction.ActionPointCost} + {hoveredBonusAP} bonus) [{currentAP} ➔ {Mathf.Max(0, remainingAP)}]", statusStyle);

                    GUI.color = new Color(1f, 0.90f, 0.50f, 1f);
                    GUILayout.Label($"<b>Duel aveugle :</b> mise cachée +{hoveredBonusAP} PA", descStyle);
                }
                else
                {
                    GUI.color = canAfford ? ColorArcaneEmerald : new Color(1f, 0.45f, 0.45f, 1f);
                    string hint = allowsVar ? " (Molette: +PA)" : "";
                    GUILayout.Label($"⚡ <b>{_hoveredAction.ActionPointCost} PA</b>{hint} [{currentAP} ➔ {Mathf.Max(0, remainingAP)}]", statusStyle);
                }

                // Déclaration aveugle : mise PE (Essoufflement) pour les attaques.
                if (allowsVar && _hoveredAction.Category == ActionCategory.AttaqueEtPassesDarmes
                    && !_hoveredAction.Title.StartsWith("💣"))
                {
                    int hoveredPE = 0;
                    _bonusPEMap.TryGetValue(_hoveredAction, out hoveredPE);
                    int maxPE = activeActor.Stats.GetSpendablePE();
                    if (hoveredPE > maxPE) { hoveredPE = maxPE; _bonusPEMap[_hoveredAction] = hoveredPE; }

                    GUI.color = Color.white;
                    GUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();
                    GUI.enabled = hoveredPE > 0;
                    if (GUILayout.Button("- PE", GUILayout.Width(44), GUILayout.Height(18)))
                    {
                        hoveredPE = Mathf.Max(0, hoveredPE - 1);
                        _bonusPEMap[_hoveredAction] = hoveredPE;
                    }
                    GUI.enabled = hoveredPE < maxPE;
                    if (GUILayout.Button("+ PE", GUILayout.Width(44), GUILayout.Height(18)))
                    {
                        hoveredPE = Mathf.Min(maxPE, hoveredPE + 1);
                        _bonusPEMap[_hoveredAction] = hoveredPE;
                    }
                    GUI.enabled = true;
                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();

                    GUI.color = hoveredPE > 0 ? ColorArcaneAmber : new Color(0.72f, 0.82f, 0.94f, 1f);
                    GUILayout.Label($"<b>PE : +{hoveredPE}</b> (ESS {activeActor.Stats.Essoufflement}/{activeActor.Stats.Attributes.Constitution})", descStyle);
                }

                GUI.color = new Color(0.92f, 0.96f, 1f, 1f);
                GUILayout.Label(_hoveredAction.Description, descStyle);
            }
            else
            {
                Color nameCol = _contextTarget.IsPlayerControlled
                    ? new Color(0.35f, 0.92f, 1f, 1f)
                    : new Color(1f, 0.45f, 0.55f, 1f);
                titleStyle.normal.textColor = nameCol;
                GUI.color = Color.white;
                GUILayout.Label(_contextTarget.Stats.Name, titleStyle);

                GUI.color = ColorArcaneEmerald;
                GUILayout.Label($"❤️ <b>{_contextTarget.Stats.CurrentHealth} / {_contextTarget.Stats.MaxHealth} PV</b>", bodyStyle);

                GUI.color = new Color(0.45f, 0.88f, 1f, 1f);
                GUILayout.Label($"⚡ PA : {_contextTarget.Stats.CurrentActionPoints}   🛡️ {_contextTarget.Stats.EncaissementThreshold}", bodyStyle);

                if (_contextTarget.Stats.ActiveStatus != StatusEffect.None)
                {
                    GUI.color = new Color(0.85f, 0.65f, 1f, 1f);
                    GUILayout.Label($"<b>[{_contextTarget.Stats.ActiveStatus}]</b>", bodyStyle);
                }

                // Marques de techniques de spécialisation (Livre III) : faille
                // exposée, provocation et bonus de ligne — visibles d'un coup d'œil.
                if (SkillTechniqueState.IsFlawExposed(_contextTarget.Stats))
                {
                    GUI.color = ColorArcaneAmber;
                    GUILayout.Label("📡 <b>FAILLE EXPOSÉE</b> (armure 0)", descStyle);
                }

                if (SkillTechniqueState.TryGetTaunter(_contextTarget.Stats, out var taunter) && taunter != null)
                {
                    GUI.color = ColorArcaneAmber;
                    GUILayout.Label($"👁️ <b>PROVOQUÉ</b> par {taunter.Name}", descStyle);
                }

                if (SkillTechniqueState.HasLineBonus(_contextTarget.Stats))
                {
                    GUI.color = ColorArcaneEmerald;
                    GUILayout.Label("🛡️ <b>LIGNE TENUE</b> (+armure)", descStyle);
                }

                // RD-039 : Étreintes de lutte actives (Livre VI)
                if (Killtime.Core.Combat.GrappleState.TryGetGrappledVictim(_contextTarget.Stats, out var heldVictim) && heldVictim != null)
                {
                    GUI.color = ColorArcaneCyan;
                    GUILayout.Label($"🥋 <b>LUTTE : Maintient {heldVictim.Name}</b>", descStyle);
                }
                else if (Killtime.Core.Combat.GrappleState.TryGetGrappler(_contextTarget.Stats, out var grappler) && grappler != null)
                {
                    GUI.color = ColorArcaneAmber;
                    GUILayout.Label($"🥋 <b>LUTTE : Saisi par {grappler.Name}</b>", descStyle);
                }
            }

            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();
            GUILayout.EndArea();

            GUI.color = prevColor;
        }

        private void DrawCategoryOrbit(Vector2 mousePos, Event e, bool isCurrentTurnUnit)
        {
            var categories = GetAvailableCategories(isCurrentTurnUnit);
            int count = categories.Count;
            if (count == 0) return;

            _hoveredCategory = null;

            float step = count > 1 ? Mathf.Min(0.38f, (Mathf.PI * 0.65f) / (count - 1)) : 0f;
            float totalArc = step * (count - 1);
            float startAngle = -Mathf.PI * 0.5f - totalArc * 0.5f;

            for (int i = 0; i < count; i++)
            {
                ActionCategory cat = categories[i];
                float angle = count == 1 ? -Mathf.PI * 0.5f : startAngle + step * i;
                Vector2 nodePos = _centerPos + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * InnerRadius;
                Rect nodeRect = new Rect(nodePos.x - CategoryNodeRadius, nodePos.y - CategoryNodeRadius, CategoryNodeRadius * 2f, CategoryNodeRadius * 2f);

                bool isHovered = nodeRect.Contains(mousePos);
                bool isSelected = (_selectedCategory == cat);
                Color catColor = GetCategoryColor(cat);

                if (isHovered)
                {
                    _hoveredCategory = cat;
                    _hoveredCategoryPos = nodePos;
                }

                Color prevCol = GUI.color;
                GUI.color = isSelected ? new Color(catColor.r, catColor.g, catColor.b, 0.90f) : new Color(0.04f, 0.08f, 0.12f, 0.90f);
                GUI.DrawTexture(nodeRect, _circleTex);

                GUI.color = isSelected ? Color.white : (isHovered ? catColor : new Color(catColor.r, catColor.g, catColor.b, 0.5f));
                GUI.DrawTexture(nodeRect, _ringTex);

                Texture2D icon = GetCategoryPlaceholderIcon(cat);
                GUI.color = isSelected ? Color.black : Color.white;
                Rect iconRect = new Rect(nodeRect.x + 3f, nodeRect.y + 3f, nodeRect.width - 6f, nodeRect.height - 6f);
                GUI.DrawTexture(iconRect, icon);

                GUI.color = prevCol;

                if (isHovered && e.type == EventType.MouseDown && e.button == 0)
                {
                    _selectedCategory = cat;
                    _hoveredAction = null;
                    ArmInputBlocker();
                    e.Use();
                }
            }
        }

        private void DrawCategoryTooltip(ActionCategory cat, Vector2 tabPos)
        {
            string label = GetCategoryDisplayName(cat);
            Color catColor = GetCategoryColor(cat);

            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };

            Vector2 size = style.CalcSize(new GUIContent(label));
            size.x += 16f;
            size.y += 8f;

            float tipX = tabPos.x - size.x * 0.5f;
            float tipY = tabPos.y - CategoryNodeRadius - size.y - 6f;

            tipX = Mathf.Clamp(tipX, 6f, Screen.width - size.x - 6f);
            tipY = Mathf.Clamp(tipY, 6f, Screen.height - size.y - 6f);

            Rect tipRect = new Rect(tipX, tipY, size.x, size.y);

            Color prev = GUI.color;

            GUI.color = new Color(0.02f, 0.04f, 0.08f, 0.96f);
            GUI.DrawTexture(tipRect, _pixelTex);

            GUI.color = new Color(catColor.r, catColor.g, catColor.b, 0.85f);
            DrawHollowRect(tipRect, 1f);

            GUI.color = Color.white;
            GUI.Label(tipRect, label, style);

            GUI.color = prev;
        }

        private void DrawActionOrbit(TacticalUnit activeActor, Vector2 mousePos, Event e, bool isCurrentTurnUnit)
        {
            var actions = _cachedActions.FindAll(a => a.Category == _selectedCategory);
            // Fin de tour TOUJOURS visible sur le menu d'une unité alliée (comme
            // l'ancien panneau « Terminer le tour ») : active seulement quand c'est
            // réellement son tour en combat, grisée + explicative sinon.
            bool showEndTurn = _contextTarget != null && _contextTarget.IsPlayerControlled;
            bool canEndTurn = showEndTurn && isCurrentTurnUnit;

            int actionCount = actions.Count + (showEndTurn ? 1 : 0);
            if (actionCount == 0) return;

            float angleStep = (Mathf.PI * 2f) / actionCount;
            float initialAngle = -Mathf.PI * 0.5f;

            _hoveredAction = null;

            for (int i = 0; i < actions.Count; i++)
            {
                var action = actions[i];
                float angle = initialAngle + angleStep * i;
                Vector2 nodePos = _centerPos + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * OuterRadius;
                Rect nodeRect = new Rect(nodePos.x - ActionNodeRadius, nodePos.y - ActionNodeRadius, ActionNodeRadius * 2f, ActionNodeRadius * 2f);

                bool isHovered = nodeRect.Contains(mousePos);
                if (isHovered) _hoveredAction = action;

                bool allowsVarAP = ActionAllowsVariableAP(action);
                int bonusAP = _bonusAPMap.TryGetValue(action, out int b) ? b : 0;
                int bonusPE = _bonusPEMap.TryGetValue(action, out int bpe) ? bpe : 0;
                int maxAffordableBonus = Mathf.Max(0, activeActor.Stats.CurrentActionPoints - action.ActionPointCost);
                if (bonusPE > activeActor.Stats.GetSpendablePE()) { bonusPE = activeActor.Stats.GetSpendablePE(); _bonusPEMap[action] = bonusPE; }

                // Modulation immédiate par molette ou clic droit
                if (isHovered && allowsVarAP)
                {
                    if (e.type == EventType.ScrollWheel)
                    {
                        if (e.delta.y < 0f && bonusAP < maxAffordableBonus)
                        {
                            bonusAP++;
                            _bonusAPMap[action] = bonusAP;
                        }
                        else if (e.delta.y > 0f && bonusAP > 0)
                        {
                            bonusAP--;
                            _bonusAPMap[action] = bonusAP;
                        }
                        e.Use();
                    }
                    else if (e.type == EventType.MouseDown && e.button == 1)
                    {
                        bonusAP = (bonusAP < maxAffordableBonus) ? bonusAP + 1 : 0;
                        _bonusAPMap[action] = bonusAP;
                        ArmInputBlocker();
                        e.Use();
                    }
                }

                int totalCost = action.ActionPointCost + bonusAP;
                bool canAfford = activeActor.Stats.CurrentActionPoints >= totalCost && action.CanExecute(activeActor, _contextTarget);
                bool hasStakes = bonusAP > 0 || bonusPE > 0;

                Color prevCol = GUI.color;

                // Halo de survol ou de surtension énergétique
                if (isHovered || hasStakes)
                {
                    Color haloCol = hasStakes ? ColorArcaneAmber : (canAfford ? ColorArcaneCyan : ColorArcaneCrimson);
                    GUI.color = new Color(haloCol.r, haloCol.g, haloCol.b, hasStakes ? 0.50f : 0.35f);
                    float haloSize = ActionNodeRadius * (hasStakes ? 3.1f : 2.8f);
                    GUI.DrawTexture(new Rect(nodePos.x - haloSize * 0.5f, nodePos.y - haloSize * 0.5f, haloSize, haloSize), _glowTex);
                }

                // Fond
                GUI.color = canAfford ? new Color(0.03f, 0.06f, 0.1f, 0.95f) : new Color(0.14f, 0.04f, 0.04f, 0.85f);
                GUI.DrawTexture(nodeRect, _circleTex);

                // Bordure : Cyan si disponible, Ambre si surtension, Blanc si survol, Rouge UNIQUEMENT si indisponible
                Color borderCol = !canAfford 
                    ? ColorArcaneCrimson 
                    : (hasStakes ? ColorArcaneAmber : (isHovered ? Color.white : ColorArcaneCyan));
                GUI.color = borderCol;
                GUI.DrawTexture(nodeRect, _ringTex);

                // Icône
                Texture2D glyph = GetActionPlaceholderIcon(action.Title, action.Category);
                GUI.color = canAfford ? (isHovered ? Color.white : new Color(0.9f, 0.95f, 1f, 0.9f)) : new Color(0.6f, 0.6f, 0.6f, 0.4f);
                Rect glyphRect = new Rect(nodeRect.x + 5f, nodeRect.y + 5f, nodeRect.width - 10f, nodeRect.height - 10f);
                GUI.DrawTexture(glyphRect, glyph);

                // Badge PA (+ PE déclarés en aveugle)
                if (totalCost > 0)
                {
                    float badgeWidth = hasStakes ? 30f : 15f;
                    Rect badgeRect = new Rect(nodePos.x + 4f, nodePos.y + 5f, badgeWidth, 12f);

                    GUI.color = canAfford ? (hasStakes ? ColorArcaneAmber : ColorArcaneCyan) : ColorArcaneCrimson;
                    GUI.DrawTexture(badgeRect, _circleTex);

                    GUI.color = Color.black;
                    var badgeStyle = new GUIStyle(GUI.skin.label)
                    {
                        fontSize = 8,
                        fontStyle = FontStyle.Bold,
                        alignment = TextAnchor.MiddleCenter
                    };

                    string badgeLabel = bonusAP > 0 ? $"{totalCost}⚡" : totalCost.ToString();
                    if (bonusPE > 0) badgeLabel += $"+{bonusPE}";
                    GUI.Label(badgeRect, badgeLabel, badgeStyle);
                }

                GUI.color = prevCol;

                // Clic gauche : exécution immédiate avec blocage absolu du click-through
                if (isHovered && e.type == EventType.MouseDown && e.button == 0)
                {
                    ArmInputBlocker();
                    if (canAfford)
                    {
                        ExecuteActionWithInjectedAP(action, activeActor, _contextTarget, bonusAP, bonusPE);
                        CloseMenu();
                    }
                    e.Use();
                    return;
                }
            }

            if (showEndTurn)
            {
                float angle = initialAngle + angleStep * actions.Count;
                Vector2 nodePos = _centerPos + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * OuterRadius;
                Rect nodeRect = new Rect(nodePos.x - ActionNodeRadius, nodePos.y - ActionNodeRadius, ActionNodeRadius * 2f, ActionNodeRadius * 2f);
                bool isHovered = nodeRect.Contains(mousePos);

                Color prevCol = GUI.color;
                GUI.color = !canEndTurn
                    ? new Color(0.12f, 0.12f, 0.12f, 0.85f)
                    : (isHovered ? ColorArcaneAmber : new Color(0.2f, 0.15f, 0.05f, 0.95f));
                GUI.DrawTexture(nodeRect, _circleTex);
                GUI.color = !canEndTurn
                    ? new Color(0.45f, 0.45f, 0.45f, 0.7f)
                    : (isHovered ? Color.white : ColorArcaneAmber);
                GUI.DrawTexture(nodeRect, _ringTex);

                Texture2D hourglassGlyph = GetGlyphTexture("GLYPH_ENDTURN");
                GUI.color = !canEndTurn
                    ? new Color(0.5f, 0.5f, 0.5f, 0.6f)
                    : (isHovered ? Color.white : ColorArcaneAmber);
                Rect glyphRect = new Rect(nodeRect.x + 5f, nodeRect.y + 5f, nodeRect.width - 10f, nodeRect.height - 10f);
                GUI.DrawTexture(glyphRect, hourglassGlyph);

                GUI.color = prevCol;

                if (isHovered && e.type == EventType.MouseDown && e.button == 0)
                {
                    ArmInputBlocker();
                    if (canEndTurn)
                    {
                        EndActiveUnitTurn();
                        CloseMenu();
                    }
                    else
                    {
                        _arena?.Log(EndTurnBlockedHint());
                    }
                    e.Use();
                }
            }
        }
    }
}

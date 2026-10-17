// Hub central masqué (RD-158) : détail du choix avant + steppers PA/PE (extrait move-only de CombatContextMenuUI.Attack.cs).
// Code conservé tel quel pour la suite (réactivé via ShowCenterHub).
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
                GUILayout.Label("◈ " + _hoveredAction.Title, titleStyle);

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

                DrawPAPips(currentAP, totalCost);

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

                GUI.color = Color.white;
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                GUI.enabled = canAfford;
                _bonusPEMap.TryGetValue(_hoveredAction, out int execPE);
                string execLabel = $"✔ EXÉCUTER · {totalCost} PA" + (execPE > 0 ? $" + {execPE} PE" : "");
                if (GUILayout.Button(execLabel, GUILayout.Height(22))) ExecuteFrontAction();
                GUI.enabled = true;
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();

                GUI.color = new Color(0.72f, 0.82f, 0.94f, 1f);
                GUILayout.Label("◀ ▶ bords = tourner • clic = choisir • molette fond = item ±1 • molette pastille = +PA/PE • droit vide/Bksp = retour", descStyle);

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
                GUILayout.Label($"◈ ⚡ PA : {_contextTarget.Stats.CurrentActionPoints}   🛡️ {_contextTarget.Stats.EncaissementThreshold}", bodyStyle);
                DrawPAPips(_contextTarget.Stats.CurrentActionPoints, 0);

                // Sablier à l'avant : fin de tour proposée depuis le hub.
                if (_frontIsEndTurn)
                {
                    GUI.enabled = _frontCanEndTurn;
                    if (GUILayout.Button("⏳ TERMINER LE TOUR", GUILayout.Height(22)))
                    {
                        ExecuteFrontAction();
                    }
                    GUI.enabled = true;
                }

                // Niveau catégories : guider vers l'anneau.
                if (_showOrbitCategories && !_frontIsEndTurn)
                {
                    GUI.color = new Color(0.72f, 0.82f, 0.94f, 1f);
                    GUILayout.Label("◈ Choisissez un domaine sur l'anneau", bodyStyle);
                }

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
    }
}

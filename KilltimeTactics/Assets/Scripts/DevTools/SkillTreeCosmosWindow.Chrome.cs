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
        protected override void DrawContent()
        {
            EnsureStyles();

            var guide = CharacterClassCatalog.GetActiveGuide(_sheet);
            _frameGuide = guide;
            float guideH = guide != null ? 30f : 0f;
            float topBarH = 34f;
            float footerH = 105f;
            float pad = 8f;

            // 1. Barre supérieure fixe
            Rect topBarRect = new Rect(pad, pad, _windowRect.width - pad * 2f, topBarH);
            DrawTopCommandBar(topBarRect);

            // 1b. Bandeau guide de build (arbre libre + prochaines étapes en or)
            Rect guideRect = new Rect(pad, topBarRect.yMax + 4f, _windowRect.width - pad * 2f, guideH);
            float viewY = topBarRect.yMax + 4f;
            if (guide != null)
            {
                DrawBuildGuideStrip(guideRect, guide);
                viewY = guideRect.yMax + 4f;
            }

            // 2. Fiche d'inspection inférieure fixe
            Rect footerRect = new Rect(pad, _windowRect.height - footerH - pad, _windowRect.width - pad * 2f, footerH);

            // 3. Viewport central
            float viewH = Mathf.Max(180f, footerRect.yMin - viewY - 4f);
            Rect viewportRect = new Rect(pad, viewY, _windowRect.width - pad * 2f, viewH);

            HandleCanvasNavigation(viewportRect);
            DrawCosmosViewport(viewportRect);

            DrawNodeInspectionFooter(footerRect);
        }

        /// <summary>
        /// Bandeau guide de build : rappel que l'arbre reste libre, progression + prochaines étapes.
        /// </summary>
        private void DrawBuildGuideStrip(Rect r, CharacterClassDefinition guide)
        {
            GUI.BeginGroup(r, GUI.skin.box);
            int done = guide.GetAcquiredCount(_sheet);
            int total = guide.BuildPath != null ? guide.BuildPath.Count : 0;
            var next = guide.GetNextSteps(_sheet);
            string nextLabel = next.Count > 0
                ? string.Join(" • ", next.GetRange(0, Math.Min(2, next.Count)))
                : "Build terminé ★";
            GUI.Label(new Rect(8, 6, r.width - 260, 20),
                $"{guide.IconGlyph} <b>Guide : {guide.DisplayName}</b> ({done}/{total}) — <color=#FFD166>◆ Suivant : {nextLabel}</color> <color=#94A3B8>(arbre libre)</color>");
            if (GUI.Button(new Rect(r.width - 240, 4, 115, 22), "◎ Étape suiv."))
                FocusNextGuideStep();
            if (GUI.Button(new Rect(r.width - 120, 4, 112, 22), "❌ Quitter guide"))
            {
                CharacterClassCatalog.ClearGuide(_sheet);
                SaveSheet();
                _statusFeedback = "Guide de build désactivé — arbre totalement libre, preset conservé.";
            }
            GUI.EndGroup();
        }

        private void DrawTopCommandBar(Rect r)
        {
            GUI.BeginGroup(r, GUI.skin.box);

            bool isMina = IsCurrentMina;
            bool isLucas = IsCurrentLucas;
            bool isJohn = IsCurrentJohn;
            bool isErika = IsCurrentErika;
            bool isVance = IsCurrentVance;
            string charName = _sheet != null ? _sheet.Name : "Sans Alter-Ego";
            int xp = _sheet != null ? _sheet.AvailableXP : 0;

            GUI.Label(new Rect(8, 6, 200, 22), $"<b>Alter-Ego :</b> <color=#00E5FF>{charName}</color> | <color=#FFE600><b>{xp} XP</b></color>");

            float btnX = 210f;
            float btnW = 34f;

            if (GUI.Button(new Rect(btnX, 5, btnW, 22), "Fer")) FocusSector(-880f, -480f); btnX += btnW + 2f;
            if (GUI.Button(new Rect(btnX, 5, btnW, 22), "Tir")) FocusSector(0f, -880f); btnX += btnW + 2f;
            if (GUI.Button(new Rect(btnX, 5, 42, 22), "Corps")) FocusSector(880f, -480f); btnX += 44f;
            if (GUI.Button(new Rect(btnX, 5, 40, 22), "Intel")) FocusSector(-880f, 480f); btnX += 42f;
            if (GUI.Button(new Rect(btnX, 5, 38, 22), "Char")) FocusSector(0f, 880f); btnX += 40f;
            if (GUI.Button(new Rect(btnX, 5, 38, 22), "Inst")) FocusSector(880f, 480f); btnX += 40f;
            if (GUI.Button(new Rect(btnX, 5, 58, 22), "5e Force")) FocusSector(0f, 0f); btnX += 60f;

            if (isMina)
            {
                if (GUI.Button(new Rect(btnX, 5, 125, 22), "⚡ Phase 1 (Soma)")) FocusSector(-440f, -680f); btnX += 128f;
            }
            if (isLucas)
            {
                if (GUI.Button(new Rect(btnX, 5, 125, 22), "❄ Vide Calculant")) FocusSector(-260f, 30f); btnX += 128f;
            }
            if (isJohn)
            {
                if (GUI.Button(new Rect(btnX, 5, 125, 22), "🛰 Passeur")) FocusSector(0f, -880f); btnX += 128f;
            }
            if (isErika)
            {
                if (GUI.Button(new Rect(btnX, 5, 125, 22), "🔥 Flamme")) FocusSector(-60f, -110f); btnX += 128f;
            }
            if (isVance)
            {
                if (GUI.Button(new Rect(btnX, 5, 125, 22), "🛡 Commandant")) FocusSector(120f, 630f); btnX += 128f;
            }

            // Bouton DEV CHEAT
            Color prevBg = GUI.backgroundColor;
            GUI.backgroundColor = _showHiddenCheats ? new Color(1f, 0.25f, 0.85f) : new Color(0.18f, 0.25f, 0.35f);
            if (GUI.Button(new Rect(btnX, 5, 140, 22), _showHiddenCheats ? "👁️ [CHEAT] ON" : "👁️ [CHEAT] OFF"))
            {
                _showHiddenCheats = !_showHiddenCheats;
                BuildProceduralCosmology();
                _statusFeedback = _showHiddenCheats
                    ? "MODE DEV CHEAT ACTIF : Toutes les maîtrises secrètes et Phase 2 sont révélées et déblocables sans coût."
                    : "Mode standard restauré : Les maîtrises secrètes et Phase 2 respectent leurs verrous causals.";
            }
            GUI.backgroundColor = prevBg;

            float rightSide = r.width - 146f;
            if (GUI.Button(new Rect(rightSide, 5, 24, 22), "-")) _canvasZoom = Mathf.Clamp(_canvasZoom * 0.85f, 0.25f, 2.5f);
            GUI.Label(new Rect(rightSide + 26, 6, 42, 20), $"{Mathf.RoundToInt(_canvasZoom * 100)}%");
            if (GUI.Button(new Rect(rightSide + 70, 5, 24, 22), "+")) _canvasZoom = Mathf.Clamp(_canvasZoom * 1.15f, 0.25f, 2.5f);
            if (GUI.Button(new Rect(rightSide + 96, 5, 44, 22), "Vue")) ResetView();

            GUI.EndGroup();
        }

        private void DrawNodeInspectionFooter(Rect r)
        {
            GUI.BeginGroup(r, GUI.skin.box);

            var node = _selectedNode ?? _hoveredNode;
            if (node == null)
            {
                GUI.Label(new Rect(12, 10, r.width - 24, 20), $"<i>{_statusFeedback}</i>");
                GUI.Label(new Rect(12, 34, r.width - 24, 40), "<color=#94A3B8>Cliquez sur un nexus stellaire pour examiner ses effets mécaniques du Codex et y allouer vos Points d'Expérience (XP).</color>");
                GUI.EndGroup();
                return;
            }

            bool unlocked = IsNodeUnlocked(node);
            int training = GetSkillTrainingLevel(node);
            string typeHeader = node.IsImprovement
                ? $"<color=#38BDF8>AMÉLIORATION DE MAÎTRISE</color> <color=#94A3B8>(Branche : {node.ParentSpecializationName})</color>"
                : (node.IsSpecialization
                    ? "<color=#FFE600>SPÉCIALISATION MARTIALE / TECHNIQUE</color>"
                    : "<color=#00E5FF>COMPÉTENCE DE BASE</color>");

            GUI.Label(new Rect(12, 6, r.width - 260, 20), $"<b>{node.DisplayName.ToUpperInvariant()}</b> — {typeHeader}{GetCaracsHeaderSuffix(node)}", _tooltipHeaderStyle);

            float actionBtnW = 230f;
            float actionBtnX = r.width - actionBtnW - 25f;

            if (_sheet != null)
            {
                if (!node.IsSpecialization && !node.IsImprovement)
                {
                    bool isMax = training >= CharacterProgressionManager.MAX_SKILL_TRAINING;
                    if (isMax)
                    {
                        GUI.color = Color.green;
                        GUI.Label(new Rect(actionBtnX, 6, actionBtnW, 24), "✅ Maîtrise Maximale (+3 Paliers)", _tooltipHeaderStyle);
                        GUI.color = Color.white;
                    }
                    else
                    {
                        bool hasFree = _sheet.GetFreeTrainingsRemaining() > 0;
                        float freeBtnW = 110f;
                        GUI.enabled = hasFree;
                        GUI.backgroundColor = hasFree ? new Color(0.35f, 0.85f, 0.45f) : Color.gray;
                        if (GUI.Button(new Rect(actionBtnX - freeBtnW - 6f, 5, freeBtnW, 26), "🎓 Gratuit"))
                        {
                            if (CharacterProgressionManager.TrainSkillFree(_sheet, node.Skill, out string freeMsg))
                            {
                                SaveSheet();
                                _statusFeedback = freeMsg;
                            }
                            else
                            {
                                _statusFeedback = freeMsg;
                            }
                        }
                        GUI.enabled = true;
                        GUI.backgroundColor = Color.white;

                        bool canAfford = CharacterProgressionManager.GetSpendableFor(_sheet, node.Skill) >= node.XpCost;
                        GUI.enabled = canAfford;
                        GUI.backgroundColor = canAfford ? new Color(0.2f, 0.8f, 0.4f) : Color.gray;
                        if (GUI.Button(new Rect(actionBtnX, 5, actionBtnW, 26), $"⚡ Entraîner (+1 Palier : {node.XpCost} XP)"))
                        {
                            if (CharacterProgressionManager.TrainSkill(_sheet, node.Skill, out string msg))
                            {
                                SaveSheet();
                                _statusFeedback = msg;
                            }
                            else
                            {
                                _statusFeedback = msg;
                            }
                        }
                        GUI.enabled = true;
                        GUI.backgroundColor = Color.white;
                    }
                }
                else
                {
                    if (unlocked)
                    {
                        GUI.color = Color.green;
                        string badge = node.IsImprovement ? "◆ Amélioration Gravée" : "★ Spécialisation Gravée";
                        GUI.Label(new Rect(actionBtnX, 6, actionBtnW, 24), badge, _tooltipHeaderStyle);
                        GUI.color = Color.white;
                    }
                    else
                    {
                        bool parentUnlocked = true;
                    if (node.IsImprovement && !string.IsNullOrEmpty(node.ParentSpecializationName))
                    {
                        parentUnlocked = _sheet.UnlockedSpecializations != null && _sheet.UnlockedSpecializations.Contains(node.ParentSpecializationName);
                    }

                    bool isVol2 = CharacterProgressionManager.IsVolume2Specialization(node.SpecializationName);
                    bool isVol2Locked = isVol2 && !CharacterProgressionManager.IsVolume2Unlocked();

                    if (isVol2Locked && !_showHiddenCheats)
                    {
                        GUI.color = new Color(1f, 0.45f, 0.2f);
                        GUI.Label(new Rect(actionBtnX, 6, actionBtnW, 24), "🔒 Phase 2 / Vol. II Requis", _tooltipHeaderStyle);
                        GUI.color = Color.white;
                    }
                    else if (!parentUnlocked && !_showHiddenCheats)
                    {
                        GUI.color = new Color(0.7f, 0.7f, 0.7f);
                        GUI.Label(new Rect(actionBtnX, 6, actionBtnW, 24), $"🔒 Requiert : {node.ParentSpecializationName}", _tooltipHeaderStyle);
                        GUI.color = Color.white;
                    }
                    else
                    {
                        if (!isVol2Locked)
                        {
                            bool canAfford = _sheet.AvailableXP >= node.XpCost;
                            GUI.enabled = canAfford;
                            GUI.backgroundColor = canAfford ? new Color(0.95f, 0.7f, 0.15f) : Color.gray;
                            string btnText = node.IsImprovement
                                ? $"◆ Graver Amélioration ({node.XpCost} XP)"
                                : $"★ Débloquer Spécialisation ({node.XpCost} XP)";

                            if (GUI.Button(new Rect(actionBtnX, 5, actionBtnW, 26), btnText))
                            {
                                if (CharacterProgressionManager.UnlockSpecialization(_sheet, node.SpecializationName, out string msg, forceFree: false))
                                {
                                    SaveSheet();
                                    _statusFeedback = msg;
                                }
                                else
                                {
                                    _statusFeedback = msg;
                                }
                            }
                            GUI.enabled = true;
                            GUI.backgroundColor = Color.white;
                        }
                        else
                        {
                            GUI.color = new Color(1f, 0.45f, 0.2f);
                            GUI.Label(new Rect(actionBtnX - 145f, 6, 140f, 24), "🔒 Verrou Causal (Vol. II)", _tooltipHeaderStyle);
                            GUI.color = Color.white;
                        }

                        if (_showHiddenCheats)
                        {
                            float cheatBtnW = 120f;
                            float cheatBtnX = isVol2Locked ? actionBtnX : (actionBtnX - cheatBtnW - 6f);
                            GUI.backgroundColor = new Color(1f, 0.2f, 0.8f);
                            if (GUI.Button(new Rect(cheatBtnX, 5, cheatBtnW, 26), "⚡ Cheat Gratuit"))
                            {
                                CharacterProgressionManager.UnlockSpecialization(_sheet, node.SpecializationName, out string cheatMsg, forceFree: true);
                                SaveSheet();
                                _statusFeedback = cheatMsg;
                            }
                            GUI.backgroundColor = Color.white;
                        }
                    }
                    }
                }
            }

            GUI.Label(new Rect(12, 30, r.width - 24, 18), $"<color=#94A3B8>{GetInspectionDescription(node)}</color>", _tooltipDescStyle);
            GUI.Label(new Rect(12, 52, r.width - 24, 44), $"<b>Effet Codex :</b> <color=#F8FAFC>{node.MechanicalEffect}</color>{GetGuideFooterSuffix(node)}", _tooltipRuleStyle);

            GUI.EndGroup();
        }
    }
}

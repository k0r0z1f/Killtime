// Mode Defense : parametrage aveugle dans le radial, hub + anneau + glyphes defense (extrait de CombatContextMenuUI.cs).
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
        #region Mode Défense (paramétrage dans le radial, sans modal)

        private Vector2 DefenseScreenCenter(TacticalUnit defender)
        {
            var cam = Camera.main;
            if (cam == null || defender == null)
                return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Vector3 scr = cam.WorldToScreenPoint(defender.transform.position + Vector3.up * 0.9f);
            if (scr.z < 0f) return _centerPos;
            Vector2 gui = new Vector2(scr.x, Screen.height - scr.y);
            float m = OuterRadius + 50f;
            gui.x = Mathf.Clamp(gui.x, m, Mathf.Max(m, Screen.width - m));
            gui.y = Mathf.Clamp(gui.y, m, Mathf.Max(m, Screen.height - m));
            return gui;
        }

        private void UpdateDefenseAnchor()
        {
            _defenseHasAnchor = false;
            var cam = Camera.main;
            if (cam == null || _defenseDefender == null) return;
            Vector3 scr = cam.WorldToScreenPoint(_defenseDefender.transform.position + Vector3.up * 0.9f);
            if (scr.z < 0f) return;
            _defenseTargetGui = new Vector2(scr.x, Screen.height - scr.y);
            _defenseHasAnchor = true;
        }

        private int DefenseRequiredBase()
        {
            if (_defenseDuel != null && _defenseDuel.DefenderBasePaid) return 0;
            if (_defenseDefender == null) return Killtime.Core.Rules.CoreRulesConfig.Instance.BaseReactionAPCost;
            return _defenseDefender.Stats.GetEffectiveApCost(Killtime.Core.Rules.CoreRulesConfig.Instance.BaseReactionAPCost);
        }

        private bool DefenseCanUseActive()
        {
            if (_defenseDefender == null) return false;
            if (_defenseDuel != null && _defenseDuel.DefenderBasePaid)
                return _defenseDefender.Stats.CanDefendActively();
            return _defenseDefender.Stats.CanDefendActively()
                && _defenseDefender.Stats.CurrentActionPoints >= DefenseRequiredBase();
        }

        private int DefenseMaxBonusAP()
        {
            if (_defenseDefender == null) return 0;
            return Mathf.Max(0, _defenseDefender.Stats.CurrentActionPoints - (_defenseWants ? DefenseRequiredBase() : 0));
        }

        private int DefenseMaxPE()
        {
            if (_defenseDefender == null) return 0;
            return Mathf.Max(0, _defenseDefender.Stats.GetSpendablePE());
        }

        private int DefensePEBonusPerPoint()
        {
            return Killtime.Core.Rules.CoreRulesConfig.Instance.DuelPEBonusPerPoint;
        }

        private bool DefenseCanParry()
        {
            if (_defenseDefender == null) return false;
            var weapon = _defenseDefender.Sheet?.GetEquippedWeapon();
            bool hasMeleeWeapon = weapon != null && weapon.RangeInTiles <= 1;
            return hasMeleeWeapon || _defenseDefender.Stats.HasSpecialization("Arts Martiaux");
        }

        private string DefenseSkillLabel()
        {
            if (!_defenseWants) return "Passif";
            if (_defenseSkill == SkillType.Esquive) return "Esquive";
            if (_defenseSkill == SkillType.DefenseCorporelle) return "Blocage";
            return _defenseParryLabel;
        }

        private int DefenseSelectedIndex()
        {
            if (!_defenseWants) return 3;
            if (_defenseSkill == SkillType.Esquive) return 0;
            if (_defenseSkill == SkillType.DefenseCorporelle) return 2;
            return 1;
        }

        private void ConfirmDefense()
        {
            if (!_isDefenseMode) return;
            bool canActive = DefenseCanUseActive();
            SkillType skill = _defenseSkill;
            bool wants = _defenseWants && canActive;
            int ap = wants ? Mathf.Min(_defenseBonusAP, DefenseMaxBonusAP()) : 0;
            int pe = wants ? Mathf.Min(_defenseBonusPE, DefenseMaxPE()) : 0;
            var cb = _defenseCallback;
            CloseMenu();
            ArmInputBlocker();
            cb?.Invoke(skill, wants, ap, pe);
        }

        /// <summary>Focus visuel : scène assombrie sauf autour du défenseur + anneau pulsant.</summary>
        private void DrawDefenseBackdrop()
        {
            if (!_defenseHasAnchor || _defenseDefender == null) return;
            Color prev = GUI.color;

            float hx = Mathf.Clamp(_defenseTargetGui.x - DefenseSpotlightHalfSize, 0f, Screen.width);
            float hy = Mathf.Clamp(_defenseTargetGui.y - DefenseSpotlightHalfSize, 0f, Screen.height);
            float hw = Mathf.Min(DefenseSpotlightHalfSize * 2f, Screen.width - hx);
            float hh = Mathf.Min(DefenseSpotlightHalfSize * 2f, Screen.height - hy);

            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, hy), _pixelTex);
            GUI.DrawTexture(new Rect(0f, hy + hh, Screen.width, Mathf.Max(0f, Screen.height - hy - hh)), _pixelTex);
            GUI.DrawTexture(new Rect(0f, hy, hx, hh), _pixelTex);
            GUI.DrawTexture(new Rect(hx + hw, hy, Mathf.Max(0f, Screen.width - hx - hw), hh), _pixelTex);

            float pulse = 1f + Mathf.Sin(Time.time * 4f) * 0.08f;
            float glowSize = DefenseSpotlightHalfSize * 2.6f * pulse;
            GUI.color = new Color(1f, 0.75f, 0.1f, 0.22f);
            GUI.DrawTexture(new Rect(_defenseTargetGui.x - glowSize * 0.5f, _defenseTargetGui.y - glowSize * 0.5f, glowSize, glowSize), _glowTex);

            float ringSize = DefenseSpotlightHalfSize * 1.5f * pulse;
            Rect defRingRect = new Rect(_defenseTargetGui.x - ringSize * 0.5f, _defenseTargetGui.y - ringSize * 0.5f, ringSize, ringSize);
            GUI.color = new Color(1f, 0.75f, 0.1f, 0.95f);
            GUI.DrawTexture(defRingRect, _ringTex);
            DrawRuneTickRing(defRingRect, new Color(1f, 0.75f, 0.1f, 0.6f), -Time.time * 12f);
            // Thereline : même langage que le radial d'attaque (RD-155).
            DrawTetherLine(_defenseTargetGui, _centerPos, new Color(1f, 0.75f, 0.1f, 0.35f), 2f);

            GUI.color = Color.white;
            var tagStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 10,
                fontStyle = FontStyle.Bold
            };
            tagStyle.normal.textColor = ColorArcaneAmber;
            Rect tagRect = new Rect(_defenseTargetGui.x - 110f, _defenseTargetGui.y + DefenseSpotlightHalfSize * 0.85f, 220f, 18f);
            GUI.Label(tagRect, $"◉ {_defenseDefender.Stats.Name} — DÉFENSEUR", tagStyle);

            GUI.color = prev;
        }

        /// <summary>Hub central : résumé de la mise aveugle + steppers PA/PE + confirmer.</summary>
        private void DrawDefenseHub()
        {
            Color prevColor = GUI.color;

            GUI.color = ColorHubBackground;
            GUI.DrawTexture(new Rect(_centerPos.x - DefenseHubRadius, _centerPos.y - DefenseHubRadius, DefenseHubRadius * 2f, DefenseHubRadius * 2f), _circleTex);

            bool canActive = DefenseCanUseActive();
            if (!canActive)
            {
                _defenseWants = false;
                _defenseBonusAP = 0;
                _defenseBonusPE = 0;
            }
            int maxBonus = DefenseMaxBonusAP();
            if (_defenseBonusAP > maxBonus) _defenseBonusAP = maxBonus;
            if (_defenseBonusAP < 0) _defenseBonusAP = 0;
            int maxPE = DefenseMaxPE();
            if (_defenseBonusPE > maxPE) _defenseBonusPE = maxPE;
            if (_defenseBonusPE < 0) _defenseBonusPE = 0;

            int requiredBase = DefenseRequiredBase();
            int availPA = _defenseDefender.Stats.CurrentActionPoints;
            int totalCost = _defenseWants ? requiredBase + _defenseBonusAP : 0;
            int remainPA = Mathf.Max(0, availPA - totalCost);
            int bonusTotal = _defenseWants ? _defenseBonusAP + _defenseBonusPE * DefensePEBonusPerPoint() : 0;

            GUI.color = _defenseWants
                ? new Color(ColorArcaneAmber.r, ColorArcaneAmber.g, ColorArcaneAmber.b, 0.9f)
                : new Color(ColorArcaneCyan.r, ColorArcaneCyan.g, ColorArcaneCyan.b, 0.9f);
            GUI.DrawTexture(new Rect(_centerPos.x - DefenseHubRadius, _centerPos.y - DefenseHubRadius, DefenseHubRadius * 2f, DefenseHubRadius * 2f), _ringTex);

            float usable = DefenseHubRadius * 1.54f;
            Rect hubRect = new Rect(_centerPos.x - usable * 0.5f, _centerPos.y - usable * 0.5f, usable, usable);

            // IMPORTANT: Réinitialiser GUI.color pour des textes clairs et contrastés
            GUI.color = Color.white;

            GUILayout.BeginArea(hubRect);
            GUILayout.BeginVertical();
            GUILayout.FlexibleSpace();

            var titleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                wordWrap = true,
                richText = true,
                clipping = TextClipping.Clip
            };
            var bodyStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 10,
                wordWrap = true,
                richText = true,
                clipping = TextClipping.Clip
            };
            var rowStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 10,
                fontStyle = FontStyle.Bold
            };

            titleStyle.normal.textColor = Color.white;
            GUI.color = Color.white;
            GUILayout.Label($"◈ 🛡️ {_defenseDefender.Stats.Name}", titleStyle);
            DrawPAPips(availPA, totalCost);

            string partName = BodyPartInfo.GetInfo(_defenseDuel.TargetedPart).DisplayName;
            GUI.color = new Color(0.92f, 0.96f, 1f, 1f);
            GUILayout.Label($"{_defenseAttacker.Stats.Name} vise <b>{partName}</b>", bodyStyle);

            int shownIdx = _hoveredDefenseNode >= 0 ? _hoveredDefenseNode : DefenseSelectedIndex();
            string[] nodeNames = { "Esquive", _defenseParryLabel, "Blocage", "Passif", "Confirmer" };
            GUI.color = shownIdx == 4 ? ColorArcaneEmerald : ColorArcaneAmber;
            GUILayout.Label(shownIdx == 4 ? "✔ Révélation simultanée" : $"{nodeNames[shownIdx]} · +{bonusTotal}", bodyStyle);

            bool rowsEnabled = _defenseWants && canActive;
            GUI.color = Color.white;

            GUILayout.BeginHorizontal();
            rowStyle.normal.textColor = Color.white;
            GUILayout.Label($"PA +{_defenseBonusAP}", rowStyle, GUILayout.Width(52));
            GUI.enabled = rowsEnabled && _defenseBonusAP > 0;
            if (GUILayout.Button("−", GUILayout.Width(26), GUILayout.Height(18))) _defenseBonusAP = Mathf.Max(0, _defenseBonusAP - 1);
            GUI.enabled = rowsEnabled && _defenseBonusAP < maxBonus;
            if (GUILayout.Button("+", GUILayout.Width(26), GUILayout.Height(18))) _defenseBonusAP = Mathf.Min(maxBonus, _defenseBonusAP + 1);
            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"PE +{_defenseBonusPE}", rowStyle, GUILayout.Width(52));
            GUI.enabled = rowsEnabled && _defenseBonusPE > 0;
            if (GUILayout.Button("−", GUILayout.Width(26), GUILayout.Height(18))) _defenseBonusPE = Mathf.Max(0, _defenseBonusPE - 1);
            GUI.enabled = rowsEnabled && _defenseBonusPE < maxPE;
            if (GUILayout.Button("+", GUILayout.Width(26), GUILayout.Height(18))) _defenseBonusPE = Mathf.Min(maxPE, _defenseBonusPE + 1);
            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            if (!canActive)
            {
                GUI.color = ColorArcaneCrimson;
                GUILayout.Label("PA insuffisants → passif (0 PA)", bodyStyle);
                GUI.color = Color.white;
            }
            else
            {
                GUI.color = remainPA >= 0 ? ColorArcaneCyan : ColorArcaneCrimson;
                GUILayout.Label($"Mise {totalCost} PA + {_defenseBonusPE} PE · [{availPA} ➔ {remainPA}]", bodyStyle);
                GUI.color = Color.white;
            }

            if (_eligibleProtectors != null && _eligibleProtectors.Count > 0)
            {
                for (int pIdx = 0; pIdx < _eligibleProtectors.Count; pIdx++)
                {
                    var prot = _eligibleProtectors[pIdx];
                    if (prot == null || !prot.Stats.IsAlive) continue;
                    GUI.backgroundColor = new Color(0.2f, 0.7f, 1f, 1f);
                    if (GUILayout.Button($"🛡️ Interception : {prot.Stats.Name} (-1 PA)", GUILayout.Height(20)))
                    {
                        if (CombatTechniqueRegistry.ExecuteIntercept(prot, _defenseDefender, _arena))
                        {
                            _interceptedBy = prot;
                            _defenseDefender = prot;
                            _contextTarget = prot;
                            if (_defenseDuel != null)
                            {
                                var calc = new CombatCalculator();
                                calc.RedirectDuelToProtector(_defenseDuel, prot.Stats);
                            }

                            _defenseSkill = prot.Stats.Attributes.Agilite >= prot.Stats.Attributes.Force
                                ? SkillType.Esquive
                                : SkillType.DefenseCorporelle;
                            _defenseParrySkill = SkillType.ManiementArmes;
                            _defenseParryLabel = "Parade";
                            var weapon = prot.Sheet?.GetEquippedWeapon();
                            if (weapon != null && weapon.RangeInTiles <= 1 && weapon.AssociatedSkill != SkillType.Ballistique)
                            {
                                _defenseSkill = SkillDefinitions.ResolveBaseSkill(weapon.AssociatedSkill);
                                _defenseParrySkill = SkillDefinitions.ResolveBaseSkill(weapon.AssociatedSkill);
                                _defenseParryLabel = $"Parade ({weapon.Name})";
                            }

                            _defenseWants = DefenseCanUseActive();
                            _defenseBonusAP = 0;
                            _defenseBonusPE = 0;
                            _centerPos = DefenseScreenCenter(prot);
                            UpdateDefenseAnchor();
                            _eligibleProtectors.RemoveAt(pIdx);
                            break;
                        }
                    }
                    GUI.backgroundColor = Color.white;
                }
            }

            GUI.backgroundColor = ColorArcaneAmber;
            if (GUILayout.Button("✔ CONFIRMER", GUILayout.Height(24)))
            {
                ConfirmDefense();
            }
            GUI.backgroundColor = Color.white;

            GUILayout.FlexibleSpace();
            GUILayout.EndVertical();
            GUILayout.EndArea();

            GUI.color = prevColor;
        }

        // Projection au sol du défenseur : centre de l'orbite inclinée.
        private Vector2 DefenseGroundGui()
        {
            var cam = Camera.main;
            if (cam == null || _defenseDefender == null) return _centerPos + new Vector2(0f, 120f);
            Vector3 scr = cam.WorldToScreenPoint(_defenseDefender.transform.position);
            if (scr.z < 0f) return _centerPos + new Vector2(0f, 120f);
            return new Vector2(scr.x, Screen.height - scr.y);
        }

        // Clavier RD-156 : amène le voisin de l'avant au premier plan.
        private void DefOrbitStepKeyboard(int dir)
        {
            if (!_isDefenseMode) return;
            int next = (OrbitFrontIndex(_defOrbitAngle, 5) + dir + 5) % 5;
            _defOrbitTargetAngle = OrbitSnapTarget(_defOrbitAngle, 5, next);
            _defOrbitHasTarget = true;
        }

        /// <summary>Orbite inclinée RD-156 : 5 skills défensifs + confirmer, l'avant paramètre. Molette/clic droit sur l'avant = +PA.</summary>
        private void DrawDefenseRing(Vector2 mousePos, Event e)
        {
            bool canActive = DefenseCanUseActive();
            bool canParry = canActive && DefenseCanParry();
            string[] nodeNames = { "Esquive", _defenseParryLabel, "Blocage", "Passif", "Confirmer" };
            string[] glyphKeys = { "DEF_ESQUIVE", "DEF_PARADE", "DEF_BLOCAGE", "DEF_PASSIF", "DEF_CONFIRM" };
            const int totalNodes = 5;
            bool[] enabled = { canActive, canParry, canActive, true, true };
            int selectedIdx = DefenseSelectedIndex();

            // RD-160 : seules les options valides dans le moment s'affichent
            // (Confirmer toujours). visIdx[position] = index logique 0..4.
            var visIdx = new List<int>(totalNodes);
            for (int v = 0; v < totalNodes; v++)
                if (enabled[v] || v == 4) visIdx.Add(v);
            int count = visIdx.Count;
            if (count == 0) return;

            Vector2 orbitCenter = DefenseGroundGui() + new Vector2(0f, -30f);
            float rx = OrbitRadiusX;
            float ry = OrbitRadiusX * OrbitTilt;
            float step = 360f / count;
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);

            UpdateOrbitAngle(ref _defOrbitAngle, _defOrbitTargetAngle, ref _defOrbitHasTarget, count, mousePos, orbitCenter, rx, ry, dt);
            int front = OrbitFrontIndex(_defOrbitAngle, count);
            // Le hub prévisualise l'avant : le paramétrage suit l'anneau.
            _hoveredDefenseNode = visIdx[front];

            // Molette sur l'orbite = +PA aveugle sur le choix avant (skill actif).
            if (e.type == EventType.ScrollWheel && visIdx[front] != 4 && enabled[visIdx[front]] && _defenseWants)
            {
                Vector2 rel = mousePos - orbitCenter;
                float norm = (rel.x * rel.x) / (rx * rx) + (rel.y * rel.y) / (ry * ry);
                if (norm < 1.7f)
                {
                    int maxBonus = DefenseMaxBonusAP();
                    if (e.delta.y < 0f && _defenseBonusAP < maxBonus) _defenseBonusAP++;
                    else if (e.delta.y > 0f && _defenseBonusAP > 0) _defenseBonusAP--;
                    e.Use();
                }
            }

            // Guide elliptique : même langage que l'orbite d'attaque.
            Color prevGuide = GUI.color;
            Rect orbitRect = new Rect(orbitCenter.x - rx, orbitCenter.y - ry, rx * 2f, ry * 2f);
            GUI.color = new Color(1f, 0.75f, 0.1f, 0.16f);
            GUI.DrawTexture(orbitRect, _ringTex);
            DrawRuneTickRing(orbitRect, new Color(1f, 0.75f, 0.1f, 0.30f), _defOrbitAngle);
            GUI.color = prevGuide;

            var order = new List<int>(count);
            for (int i = 0; i < count; i++) order.Add(i);
            order.Sort((a, b) => OrbitDepth(_defOrbitAngle + a * step).CompareTo(OrbitDepth(_defOrbitAngle + b * step)));

            foreach (int i in order)
            {
                int li = visIdx[i]; // index logique du nœud (RD-160)
                float ang = _defOrbitAngle + i * step;
                float depth = OrbitDepth(ang);
                Vector2 nodePos = OrbitNodePos(orbitCenter, rx, ry, ang);
                float r = Mathf.Lerp(11f, ActionNodeRadius, depth);
                Rect nodeRect = new Rect(nodePos.x - r, nodePos.y - r, r * 2f, r * 2f);

                bool isFront = (i == front);
                bool mouseOn = nodeRect.Contains(mousePos);
                bool isSelected = (li == selectedIdx);
                bool isConfirm = (li == 4);
                float alpha = Mathf.Lerp(0.45f, 1f, depth);

                bool hasStakes = isSelected && (_defenseBonusAP > 0 || _defenseBonusPE > 0);
                Color prevCol = GUI.color;

                if (isFront || (isSelected && !isConfirm))
                {
                    Color haloCol = hasStakes ? ColorArcaneAmber : (isConfirm ? ColorArcaneEmerald : ColorArcaneCyan);
                    GUI.color = new Color(haloCol.r, haloCol.g, haloCol.b, (hasStakes ? 0.5f : 0.35f) * alpha);
                    float haloSize = r * (hasStakes ? 3.1f : 2.8f);
                    GUI.DrawTexture(new Rect(nodePos.x - haloSize * 0.5f, nodePos.y - haloSize * 0.5f, haloSize, haloSize), _glowTex);
                    DrawRuneTickRing(nodeRect, new Color(haloCol.r, haloCol.g, haloCol.b, 0.6f * alpha), Time.time * 20f);
                }

                GUI.color = enabled[li] ? new Color(0.03f, 0.06f, 0.1f, 0.95f * alpha) : new Color(0.14f, 0.04f, 0.04f, 0.85f * alpha);
                GUI.DrawTexture(nodeRect, _circleTex);

                Color borderCol = !enabled[li]
                    ? ColorArcaneCrimson
                    : (isConfirm ? ColorArcaneEmerald : (isFront ? Color.white : (isSelected ? Color.white : ColorArcaneCyan)));
                GUI.color = new Color(borderCol.r, borderCol.g, borderCol.b, alpha);
                GUI.DrawTexture(nodeRect, _ringTex);

                Texture2D glyph = GetDefenseGlyph(glyphKeys[li]);
                GUI.color = enabled[li] ? (mouseOn ? Color.white : new Color(0.9f, 0.95f, 1f, 0.9f * alpha)) : new Color(0.6f, 0.6f, 0.6f, 0.4f * alpha);
                Rect glyphRect = new Rect(nodeRect.x + 5f, nodeRect.y + 5f, nodeRect.width - 10f, nodeRect.height - 10f);
                GUI.DrawTexture(glyphRect, glyph);

                if (!isConfirm)
                {
                    Rect badgeRect = new Rect(nodePos.x + 4f, nodePos.y + 5f, 15f, 12f);
                    GUI.color = enabled[li] ? ColorArcaneCyan : ColorArcaneCrimson;
                    GUI.DrawTexture(badgeRect, _circleTex);
                    GUI.color = Color.black;
                    var badgeStyle = new GUIStyle(GUI.skin.label)
                    {
                        fontSize = 8,
                        fontStyle = FontStyle.Bold,
                        alignment = TextAnchor.MiddleCenter
                    };
                    GUI.Label(badgeRect, li == 3 ? "0" : DefenseRequiredBase().ToString(), badgeStyle);
                }

                var tipStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 9,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
                tipStyle.normal.textColor = enabled[li] ? Color.white : new Color(0.6f, 0.6f, 0.6f);
                Rect tipRect = new Rect(nodePos.x - 50f, nodePos.y + r + 1f, 100f, 14f);
                GUI.Label(tipRect, nodeNames[li], tipStyle);

                GUI.color = prevCol;

                // Clic droit sur l'avant : +PA aveugle (mise cachée).
                if (isFront && !isConfirm && mouseOn && e.type == EventType.MouseDown && e.button == 1 && enabled[li] && _defenseWants)
                {
                    int maxBonus = DefenseMaxBonusAP();
                    _defenseBonusAP = (_defenseBonusAP < maxBonus) ? _defenseBonusAP + 1 : 0;
                    ArmInputBlocker();
                    e.Use();
                    return;
                }

                if (mouseOn && e.type == EventType.MouseDown && e.button == 0)
                {
                    ArmInputBlocker();
                    if (isFront)
                    {
                        ApplyDefenseNodeClick(li, enabled[li]);
                    }
                    else
                    {
                        // Amener devant ; un skill cliqué est sélectionné aussitôt
                        // (Confirmer exige un second clic une fois à l'avant).
                        _defOrbitTargetAngle = OrbitSnapTarget(_defOrbitAngle, count, i);
                        _defOrbitHasTarget = true;
                        if (!isConfirm && enabled[li])
                        {
                            ApplyDefenseNodeClick(li, true);
                        }
                        else if (!enabled[li])
                        {
                            if (Killtime.Audio.KilltimeAudioManager.Instance != null)
                                Killtime.Audio.KilltimeAudioManager.Instance.PlayUI(Killtime.Audio.SoundId.UI_Denied, 0.6f);
                        }
                    }
                    e.Use();
                    return;
                }
            }
        }

        // Applique le choix d'un nœud défense (sélection ou confirmation).
        private void ApplyDefenseNodeClick(int i, bool nodeEnabled)
        {
            if (i == 4)
            {
                ConfirmDefense();
                return;
            }
            if (!nodeEnabled)
            {
                if (Killtime.Audio.KilltimeAudioManager.Instance != null)
                    Killtime.Audio.KilltimeAudioManager.Instance.PlayUI(Killtime.Audio.SoundId.UI_Denied, 0.6f);
                return;
            }
            if (i == 3)
            {
                _defenseWants = false;
                _defenseBonusAP = 0;
                _defenseBonusPE = 0;
            }
            else
            {
                _defenseWants = true;
                _defenseSkill = i == 0 ? SkillType.Esquive : (i == 2 ? SkillType.DefenseCorporelle : _defenseParrySkill);
            }
        }

        private static Texture2D GetDefenseGlyph(string key)
        {
            if (_glyphCache.TryGetValue(key, out var cached)) return cached;
            int s = 32;
            var tex = CreateEmptyTexture(s);
            switch (key)
            {
                case "DEF_ESQUIVE":
                    DrawChevron(tex, 16, 12, 10, Color.white);
                    DrawChevron(tex, 16, 20, 10, Color.white);
                    break;
                case "DEF_PARADE":
                    DrawLine(tex, 8, 24, 24, 8, Color.white);
                    DrawLine(tex, 8, 8, 24, 24, Color.white);
                    break;
                case "DEF_BLOCAGE":
                    DrawHollowSquare(tex, 10, 10, 12, Color.white);
                    DrawDot(tex, 16, 16, 3, Color.white);
                    break;
                case "DEF_PASSIF":
                    DrawCircle(tex, 16, 16, 9, Color.white);
                    break;
                case "DEF_CONFIRM":
                    DrawLine(tex, 9, 17, 15, 23, Color.white);
                    DrawLine(tex, 15, 23, 24, 8, Color.white);
                    break;
            }
            tex.Apply();
            _glyphCache[key] = tex;
            return tex;
        }

        #endregion
    }
}

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
            bool isInsideMenu = distFromCenter <= (OuterRadius + 45f)
                || Vector2.Distance(mousePos, _orbitCenterGui) <= (OrbitRadiusX + 60f);

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

            // Ancrage diégétique (RD-155) : la cible est projetée à l'écran à
            // chaque frame (anneau au sol + thereline), le hub suit la souris comme avant.
            UpdateAttackAnchor();
            // Orbite inclinée RD-156 : le hub flotte au-dessus de l'unité, plus à la souris.
            _centerPos = AttackHubAnchor();

            bool isCurrentTurnUnit = _turnManager != null
                && !_turnManager.IsInExploration
                && !_turnManager.IsCombatOver
                && _turnManager.ActiveUnit == _contextTarget
                && _contextTarget.IsPlayerControlled;

            DrawDiegeticAnchor();
            // RD-158 : seul l'anneau des éléments reste visible (hub + halos
            // centraux masqués via ShowCenterHub, code conservé pour la suite).
            if (ShowCenterHub)
            {
                DrawHoloBackdrop(activeActor);
                DrawCenterHub(activeActor);
            }
            DrawActionOrbit(activeActor, mousePos, e, isCurrentTurnUnit);

            // Consommation systématique de tout clic résiduel dans l'anneau pour interdire la propagation
            if (isInsideMenu && (e.type == EventType.MouseDown || e.type == EventType.MouseUp || e.type == EventType.ContextClick))
            {
                e.Use();
            }
        }

        // Fond holographique allégé (RD-155) : halos réduits pour laisser la
        // scène respirer ; la teinte suit la fraction de PA (cyan → ambre → crimson).
        private void DrawHoloBackdrop(TacticalUnit activeActor)
        {
            Color prevColor = GUI.color;

            float paFrac = 1f;
            if (activeActor != null && activeActor.Stats != null)
            {
                int maxPA = Mathf.Max(1, activeActor.Stats.MaxActionPoints);
                paFrac = Mathf.Clamp01((float)activeActor.Stats.CurrentActionPoints / maxPA);
            }
            Color tint = GetPAFractionColor(paFrac);

            GUI.color = new Color(tint.r, tint.g, tint.b, 0.05f);
            float glowSize = (OuterRadius + 40f) * 2f;
            GUI.DrawTexture(new Rect(_centerPos.x - glowSize * 0.5f, _centerPos.y - glowSize * 0.5f, glowSize, glowSize), _glowTex);

            float ringOuterSize = OuterRadius * 2f;
            Rect outerRect = new Rect(_centerPos.x - OuterRadius, _centerPos.y - OuterRadius, ringOuterSize, ringOuterSize);
            GUI.color = new Color(tint.r, tint.g, tint.b, 0.20f);
            GUI.DrawTexture(outerRect, _ringTex);
            // Anneau runique en rotation lente : l'interface respire avec la scène.
            DrawRuneTickRing(outerRect, new Color(tint.r, tint.g, tint.b, 0.35f), Time.time * 6f);

            GUI.color = new Color(tint.r, tint.g, tint.b, 0.14f);
            float ringInnerSize = InnerRadius * 2f;
            GUI.DrawTexture(new Rect(_centerPos.x - InnerRadius, _centerPos.y - InnerRadius, ringInnerSize, ringInnerSize), _ringTex);

            GUI.color = prevColor;
        }

        // Projette la cible à l'écran (miroir de l'ancre défense).
        private void UpdateAttackAnchor()
        {
            _attackHasAnchor = false;
            var cam = Camera.main;
            if (cam == null || _contextTarget == null) return;
            Vector3 scrTop = cam.WorldToScreenPoint(_contextTarget.transform.position + Vector3.up * 0.9f);
            Vector3 scrFeet = cam.WorldToScreenPoint(_contextTarget.transform.position);
            if (scrTop.z < 0f || scrFeet.z < 0f) return;
            _attackTargetGui = new Vector2(scrTop.x, Screen.height - scrTop.y);
            _attackGroundGui = new Vector2(scrFeet.x, Screen.height - scrFeet.y);
            _attackHasAnchor = true;
        }

        // Orbite RD-156 : hub flottant au-dessus de l'unité (clampé à l'écran).
        private Vector2 AttackHubAnchor()
        {
            if (!_attackHasAnchor) return _centerPos;
            Vector2 hub = _attackTargetGui + new Vector2(0f, -(HubRadius + 26f));
            float m = HubRadius + 20f;
            hub.x = Mathf.Clamp(hub.x, m, Mathf.Max(m, Screen.width - m));
            hub.y = Mathf.Clamp(hub.y, m, Mathf.Max(m, Screen.height - m));
            return hub;
        }

        // Clavier RD-156/157 : amène le voisin de l'avant au premier plan (niveau courant).
        private void OrbitStepKeyboard(int dir)
        {
            if (!_isOpen || _isDefenseMode || _contextTarget == null) return;
            bool turnUnit = _turnManager != null
                && !_turnManager.IsInExploration
                && !_turnManager.IsCombatOver
                && _turnManager.ActiveUnit == _contextTarget
                && _contextTarget.IsPlayerControlled;
            // RD-160 : le clavier parcourt exactement les nœuds affichés.
            int count = BuildAttackNodes(GetActiveActor(), turnUnit, out _).Count;
            if (count <= 0) return;
            int next = (OrbitFrontIndex(_orbitAngle, count) + dir + count) % count;
            _orbitTargetAngle = OrbitSnapTarget(_orbitAngle, count, next);
            _orbitHasTarget = true;
        }

        // Exécute le nœud avant (clic avant, bouton EXÉCUTER, Entrée).
        private void ExecuteFrontAction()
        {
            if (!_isOpen || _isDefenseMode) return;
            var activeActor = GetActiveActor();
            if (activeActor == null) return;
            if (_hoveredAction == null)
            {
                if (_frontIsEndTurn)
                {
                    if (_frontCanEndTurn)
                    {
                        ArmInputBlocker();
                        EndActiveUnitTurn();
                        CloseMenu();
                    }
                    else
                    {
                        _arena?.Log(EndTurnBlockedHint());
                    }
                }
                return;
            }
            if (!_frontCanAfford)
            {
                if (Killtime.Audio.KilltimeAudioManager.Instance != null)
                    Killtime.Audio.KilltimeAudioManager.Instance.PlayUI(Killtime.Audio.SoundId.UI_Denied, 0.6f);
                return;
            }
            int bonusAP = _bonusAPMap.TryGetValue(_hoveredAction, out int b) ? b : 0;
            int bonusPE = _bonusPEMap.TryGetValue(_hoveredAction, out int bpe) ? bpe : 0;
            ArmInputBlocker();
            ExecuteActionWithInjectedAP(_hoveredAction, activeActor, _contextTarget, bonusAP, bonusPE);
            CloseMenu();
        }

        // Ancrage diégétique : anneau runique au sol sous la cible + thereline
        // pointillée vers le hub. Pur rendu : aucune logique d'interaction.
        private void DrawDiegeticAnchor()
        {
            if (!_attackHasAnchor || _contextTarget == null) return;
            Color prev = GUI.color;

            float pulse = 1f + Mathf.Sin(Time.time * 3f) * 0.06f;
            float groundW = 120f * pulse;
            float groundH = 34f * pulse;
            Rect groundRect = new Rect(_attackGroundGui.x - groundW * 0.5f, _attackGroundGui.y - groundH * 0.5f, groundW, groundH);
            bool isFoe = !_contextTarget.IsPlayerControlled;
            Color baseCol = isFoe ? ColorArcaneCrimson : ColorArcaneCyan;
            GUI.color = new Color(baseCol.r, baseCol.g, baseCol.b, 0.55f);
            GUI.DrawTexture(groundRect, _ringTex);
            DrawRuneTickRing(groundRect, new Color(baseCol.r, baseCol.g, baseCol.b, 0.5f), -Time.time * 10f);
            // RD-158 : thereline vers le hub supprimée avec lui (DrawTetherLine
            // conservée : la défense l'utilise encore).

            GUI.color = prev;
        }

        // Nœud générique de l'orbite unifiée RD-157 : catégorie, action ou fin de tour.
        private struct OrbitNode
        {
            public int Kind; // 0 = catégorie, 1 = action, 2 = fin de tour
            public ActionCategory Category;
            public CombatAction Action;
        }

        // Forage RD-157 : la catégorie cliquée remplace les catégories sur l'anneau.
        private void DrillIntoCategory(ActionCategory cat)
        {
            _selectedCategory = cat;
            _showOrbitCategories = false;
            _orbitAngle = 0f;
            _orbitHasTarget = false;
            _hoveredAction = null;
            ArmInputBlocker();
        }

        // Retour RD-157 : l'anneau remontre les catégories.
        // Tout le code PA/PE/exécution est conservé tel quel pour la suite.
        private void BackToCategories()
        {
            if (!_isOpen || _isDefenseMode || _showOrbitCategories) return;
            _showOrbitCategories = true;
            _orbitAngle = 0f;
            _orbitHasTarget = false;
            _hoveredAction = null;
        }

        // Orbite unifiée RD-157 : niveau 1 = catégories sur l'anneau, clic sur une
        // catégorie = ses items remplacent les catégories. L'avant reste le choix
        // du hub ; tout le code PA/PE/exécution est conservé tel quel pour la suite.
        // Construit les nœuds affichables RD-160 (commun dessin + clavier) :
        // catégories, ou items valides de la catégorie forée. Seules les options
        // valides dans le moment s'affichent : coût de base payable + CanExecute
        // pour les actions, fin de tour seulement si c'est le tour de l'unité.
        private List<OrbitNode> BuildAttackNodes(TacticalUnit activeActor, bool isCurrentTurnUnit, out bool canEndTurn)
        {
            bool showEndTurn = _contextTarget != null && _contextTarget.IsPlayerControlled;
            canEndTurn = showEndTurn && isCurrentTurnUnit;
            var nodes = new List<OrbitNode>();
            if (_showOrbitCategories)
            {
                var categories = GetAvailableCategories(isCurrentTurnUnit);
                for (int c = 0; c < categories.Count; c++)
                    nodes.Add(new OrbitNode { Kind = 0, Category = categories[c] });
            }
            else if (activeActor != null)
            {
                var actions = _cachedActions.FindAll(a => a.Category == _selectedCategory);
                for (int a = 0; a < actions.Count; a++)
                {
                    var act = actions[a];
                    if (activeActor.Stats.CurrentActionPoints >= act.ActionPointCost
                        && act.CanExecute(activeActor, _contextTarget))
                        nodes.Add(new OrbitNode { Kind = 1, Action = act });
                }
            }
            if (canEndTurn) nodes.Add(new OrbitNode { Kind = 2 });
            return nodes;
        }

        private void DrawActionOrbit(TacticalUnit activeActor, Vector2 mousePos, Event e, bool isCurrentTurnUnit)
        {
            var nodes = BuildAttackNodes(activeActor, isCurrentTurnUnit, out bool canEndTurn);

            int nodeCount = nodes.Count;
            if (nodeCount == 0)
            {
                _hoveredAction = null;
                _frontCanAfford = false;
                _frontIsEndTurn = false;
                _frontCanEndTurn = false;
                return;
            }

            Vector2 orbitCenter = _attackHasAnchor
                ? _attackGroundGui + new Vector2(0f, -30f)
                : _centerPos + new Vector2(0f, 120f);
            _orbitCenterGui = orbitCenter;
            float rx = OrbitRadiusX;
            float ry = OrbitRadiusX * OrbitTilt;
            float step = 360f / nodeCount;
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);

            UpdateOrbitAngle(ref _orbitAngle, _orbitTargetAngle, ref _orbitHasTarget, nodeCount, mousePos, orbitCenter, rx, ry, dt);
            int front = OrbitFrontIndex(_orbitAngle, nodeCount);
            var frontNode = nodes[front];
            bool frontIsEnd = frontNode.Kind == 2;
            var frontAction = frontNode.Kind == 1 ? frontNode.Action : null;

            // Le hub suit l'avant : le choix courant y est détaillé (code hub inchangé).
            _hoveredAction = frontAction;
            _frontIsEndTurn = frontIsEnd;
            _frontCanEndTurn = canEndTurn;
            _frontCanAfford = false;
            int frontBonusAP = 0;
            int frontBonusPE = 0;
            if (frontAction != null)
            {
                frontBonusAP = _bonusAPMap.TryGetValue(frontAction, out int b) ? b : 0;
                frontBonusPE = _bonusPEMap.TryGetValue(frontAction, out int bpe) ? bpe : 0;
                if (frontBonusPE > activeActor.Stats.GetSpendablePE()) { frontBonusPE = activeActor.Stats.GetSpendablePE(); _bonusPEMap[frontAction] = frontBonusPE; }
                int frontCost = frontAction.ActionPointCost + frontBonusAP;
                _frontCanAfford = activeActor.Stats.CurrentActionPoints >= frontCost && frontAction.CanExecute(activeActor, _contextTarget);
            }

            // La molette est gérée dans la boucle : pastilles PA/PE du nœud
            // survolé, sinon repli sur la mise PA de l'avant (voir après boucle).

            // Guide elliptique : l'orbite appartient au sol de la scène.
            Color prevGuide = GUI.color;
            Rect orbitRect = new Rect(orbitCenter.x - rx, orbitCenter.y - ry, rx * 2f, ry * 2f);
            GUI.color = new Color(0f, 0.85f, 1f, 0.16f);
            GUI.DrawTexture(orbitRect, _ringTex);
            DrawRuneTickRing(orbitRect, new Color(0f, 0.85f, 1f, 0.30f), _orbitAngle);
            GUI.color = prevGuide;

            // Nœuds du fond vers l'avant (occlusion = faux 3D), taille/profondeur liées.
            var order = new List<int>(nodeCount);
            for (int i = 0; i < nodeCount; i++) order.Add(i);
            order.Sort((a, b) => OrbitDepth(_orbitAngle + a * step).CompareTo(OrbitDepth(_orbitAngle + b * step)));

            bool mouseOnAny = false;
            bool wheelUsed = false;
            foreach (int i in order)
            {
                var node = nodes[i];
                bool isCat = node.Kind == 0;
                bool isEnd = node.Kind == 2;
                var action = node.Action;
                float ang = _orbitAngle + i * step;
                float depth = OrbitDepth(ang);
                Vector2 nodePos = OrbitNodePos(orbitCenter, rx, ry, ang);
                float r = Mathf.Lerp(11f, ActionNodeRadius, depth);
                Rect nodeRect = new Rect(nodePos.x - r, nodePos.y - r, r * 2f, r * 2f);
                bool isFront = (i == front);
                bool mouseOn = nodeRect.Contains(mousePos);
                if (mouseOn) mouseOnAny = true;

                int bonusAP = 0;
                int bonusPE = 0;
                bool canAfford = false;
                Color accent = ColorArcaneCyan;
                Texture2D glyph;
                string badgeText = null;
                if (isCat)
                {
                    accent = GetCategoryColor(node.Category);
                    glyph = GetCategoryPlaceholderIcon(node.Category);
                    canAfford = true;
                }
                else if (isEnd)
                {
                    accent = ColorArcaneAmber;
                    glyph = GetGlyphTexture("GLYPH_ENDTURN");
                    canAfford = canEndTurn;
                }
                else
                {
                    bonusAP = _bonusAPMap.TryGetValue(action, out int ab) ? ab : 0;
                    bonusPE = _bonusPEMap.TryGetValue(action, out int pb) ? pb : 0;
                    int totalCost = action.ActionPointCost + bonusAP;
                    canAfford = activeActor.Stats.CurrentActionPoints >= totalCost && action.CanExecute(activeActor, _contextTarget);
                    bool stakes = bonusAP > 0 || bonusPE > 0;
                    accent = !canAfford ? ColorArcaneCrimson : (stakes ? ColorArcaneAmber : ColorArcaneCyan);
                    glyph = GetActionPlaceholderIcon(action.Title, action.Category);
                    if (totalCost > 0)
                    {
                        badgeText = bonusAP > 0 ? $"{totalCost}⚡" : totalCost.ToString();
                        if (bonusPE > 0) badgeText += $"+{bonusPE}";
                    }
                }
                bool hasStakes = bonusAP > 0 || bonusPE > 0;
                float alpha = Mathf.Lerp(0.45f, 1f, depth);

                Color prevCol = GUI.color;

                // Halo d'avant ou de surtension énergétique.
                if (isFront || hasStakes)
                {
                    Color haloCol = hasStakes ? ColorArcaneAmber : (!canAfford ? ColorArcaneCrimson : (isCat || isEnd ? accent : ColorArcaneCyan));
                    GUI.color = new Color(haloCol.r, haloCol.g, haloCol.b, (hasStakes ? 0.50f : 0.35f) * alpha);
                    float haloSize = r * (hasStakes ? 3.1f : 2.8f);
                    GUI.DrawTexture(new Rect(nodePos.x - haloSize * 0.5f, nodePos.y - haloSize * 0.5f, haloSize, haloSize), _glowTex);
                }

                // Fond
                GUI.color = canAfford ? new Color(0.03f, 0.06f, 0.1f, 0.95f * alpha) : new Color(0.14f, 0.04f, 0.04f, 0.85f * alpha);
                GUI.DrawTexture(nodeRect, _circleTex);

                // Bordure : blanc à l'avant, couleur de catégorie/domaine sinon, rouge si indisponible.
                Color borderCol = !canAfford
                    ? ColorArcaneCrimson
                    : (isFront ? Color.white : (hasStakes ? ColorArcaneAmber : accent));
                GUI.color = new Color(borderCol.r, borderCol.g, borderCol.b, alpha);
                GUI.DrawTexture(nodeRect, _ringTex);

                // Icône (catégorie, action ou sablier de fin de tour)
                GUI.color = canAfford
                    ? (isFront ? Color.white : new Color(0.9f, 0.95f, 1f, 0.9f * alpha))
                    : new Color(0.6f, 0.6f, 0.6f, 0.4f * alpha);
                Rect glyphRect = new Rect(nodeRect.x + 5f, nodeRect.y + 5f, nodeRect.width - 10f, nodeRect.height - 10f);
                GUI.DrawTexture(glyphRect, glyph);

                // Badge PA (+ PE déclarés en aveugle) pour les actions chiffrées (conservé).
                if (badgeText != null)
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

                    GUI.Label(badgeRect, badgeText, badgeStyle);
                }

                // Pastilles latérales RD-161 : PA à gauche, PE à droite, en
                // transparence. Molette dessus = ajuster cette mise du nœud.
                bool paCapable = !isCat && !isEnd && ActionAllowsVariableAP(action);
                bool peCapable = paCapable && action.Category == ActionCategory.AttaqueEtPassesDarmes
                    && !action.Title.StartsWith("💣");
                if ((paCapable || peCapable) && depth >= 0.4f)
                {
                    float chipW = 30f, chipH = 13f;
                    Rect paRect = new Rect(nodeRect.x - chipW - 2f, nodePos.y - chipH * 0.5f, chipW, chipH);
                    Rect peRect = new Rect(nodeRect.xMax + 2f, nodePos.y - chipH * 0.5f, chipW, chipH);
                    bool paHover = paCapable && paRect.Contains(mousePos);
                    bool peHover = peCapable && peRect.Contains(mousePos);
                    if (paHover || peHover) mouseOnAny = true;
                    if (paCapable)
                    {
                        DrawStakeChip(paRect, $"+{bonusAP} PA", ColorArcaneAmber, paHover);
                        if (paHover && e.type == EventType.ScrollWheel)
                        {
                            int maxAff = Mathf.Max(0, activeActor.Stats.CurrentActionPoints - action.ActionPointCost);
                            if (e.delta.y < 0f && bonusAP < maxAff) _bonusAPMap[action] = bonusAP + 1;
                            else if (e.delta.y > 0f && bonusAP > 0) _bonusAPMap[action] = bonusAP - 1;
                            e.Use();
                            wheelUsed = true;
                        }
                    }
                    if (peCapable)
                    {
                        DrawStakeChip(peRect, $"+{bonusPE} PE", ColorArcaneViolet, peHover);
                        if (peHover && e.type == EventType.ScrollWheel)
                        {
                            int maxPe = activeActor.Stats.GetSpendablePE();
                            if (e.delta.y < 0f && bonusPE < maxPe) _bonusPEMap[action] = bonusPE + 1;
                            else if (e.delta.y > 0f && bonusPE > 0) _bonusPEMap[action] = bonusPE - 1;
                            e.Use();
                            wheelUsed = true;
                        }
                    }
                }

                // Titre du choix avant, sous le nœud, sur plaque opaque.
                // Matrice identité forcée : un autre composant IMGUI peut laisser
                // une rotation active, ce qui rendait le texte en miroir.
                if (isFront)
                {
                    var frontStyle = new GUIStyle(GUI.skin.label)
                    {
                        fontSize = 10,
                        fontStyle = FontStyle.Bold,
                        alignment = TextAnchor.MiddleCenter
                    };
                    frontStyle.normal.textColor = canAfford ? Color.white : new Color(1f, 0.55f, 0.55f);
                    string frontTitle = isCat ? GetCategoryDisplayName(node.Category) : (isEnd ? "Terminer le tour" : action.Title);
                    Color tintPrev = GUI.color;
                    Matrix4x4 matPrev = GUI.matrix;
                    GUI.matrix = Matrix4x4.identity;
                    Rect plateRect = new Rect(nodePos.x - 70f, nodePos.y + r + 4f, 140f, 18f);
                    GUI.color = new Color(0.02f, 0.03f, 0.06f, 0.92f);
                    GUI.DrawTexture(plateRect, _pixelTex);
                    GUI.color = Color.white;
                    GUI.Label(plateRect, frontTitle, frontStyle);
                    GUI.matrix = matPrev;
                    GUI.color = tintPrev;
                }

                GUI.color = prevCol;

                // Clic gauche : l'avant agit (forer / exécuter / fin de tour),
                // les autres nœuds viennent devant.
                if (mouseOn && e.type == EventType.MouseDown && e.button == 0)
                {
                    ArmInputBlocker();
                    if (isFront)
                    {
                        if (isCat)
                        {
                            DrillIntoCategory(node.Category);
                        }
                        else if (isEnd)
                        {
                            if (canEndTurn)
                            {
                                EndActiveUnitTurn();
                                CloseMenu();
                            }
                            else
                            {
                                _arena?.Log(EndTurnBlockedHint());
                            }
                        }
                        else if (canAfford)
                        {
                            ExecuteFrontAction();
                        }
                    }
                    else
                    {
                        _orbitTargetAngle = OrbitSnapTarget(_orbitAngle, nodeCount, i);
                        _orbitHasTarget = true;
                    }
                    e.Use();
                    return;
                }

                // Clic droit sur l'avant-action : modulation immédiate de la mise PA (conservé).
                if (isFront && !isCat && !isEnd && mouseOn && e.type == EventType.MouseDown && e.button == 1 && ActionAllowsVariableAP(action))
                {
                    int maxAffordableBonus = Mathf.Max(0, activeActor.Stats.CurrentActionPoints - action.ActionPointCost);
                    _bonusAPMap[action] = (bonusAP < maxAffordableBonus) ? bonusAP + 1 : 0;
                    ArmInputBlocker();
                    e.Use();
                    return;
                }
            }

            // Molette hors pastilles : repli historique = mise PA de l'avant.
            if (!wheelUsed && e.type == EventType.ScrollWheel && frontAction != null && ActionAllowsVariableAP(frontAction))
            {
                Vector2 rel = mousePos - orbitCenter;
                float norm = (rel.x * rel.x) / (rx * rx) + (rel.y * rel.y) / (ry * ry);
                if (norm < 1.7f)
                {
                    int maxAffordable = Mathf.Max(0, activeActor.Stats.CurrentActionPoints - frontAction.ActionPointCost);
                    if (e.delta.y < 0f && frontBonusAP < maxAffordable) _bonusAPMap[frontAction] = frontBonusAP + 1;
                    else if (e.delta.y > 0f && frontBonusAP > 0) _bonusAPMap[frontAction] = frontBonusAP - 1;
                    e.Use();
                }
            }

            // Clic droit dans le vide de l'orbite (niveau items) = retour aux catégories.
            if (!_showOrbitCategories && e.type == EventType.MouseDown && e.button == 1 && !mouseOnAny)
            {
                Vector2 rel = mousePos - orbitCenter;
                float norm = (rel.x * rel.x) / (rx * rx) + (rel.y * rel.y) / (ry * ry);
                if (norm < 1.9f)
                {
                    BackToCategories();
                    e.Use();
                    return;
                }
            }
        }
    }
}

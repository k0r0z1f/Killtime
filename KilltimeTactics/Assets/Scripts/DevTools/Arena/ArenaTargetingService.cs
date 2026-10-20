using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Audio;
using Killtime.Core.Character;
using Killtime.Core.Combat;
using Killtime.Core.Inventory;
using Killtime.Tactics.AI;
using Killtime.UI;

namespace Killtime.Tactics.Arena
{
    /// <summary>
    /// Sélection, ciblage, souris 3D et ligne de mire.
    /// Logique déplacée verbatim depuis CombatDevArena (HandleMouseInteraction,
    /// UpdateReachableHighlights, GetUnitAtCoords, SelectTarget, AutoTarget*,
    /// CycleTarget, SwitchActiveExplorer, GetCoverToTarget, GetUnitElevation,
    /// GetElevationRuling, UpdateCoverLineOfSight, LogCoverDiagnostics,
    /// IsPlayerManualControl).
    /// Les abonnements d'events RESTENT en façade (F3) : la façade délègue.
    /// CheckOverwatchTriggers (phase 4) est reçu via OverwatchTrigger.
    /// </summary>
    public sealed class ArenaTargetingService
    {
        private readonly ArenaContext _ctx;
        private readonly ArenaSmokeService _smoke;
        private ArenaState _state => _ctx.State;
        private IArenaHost _host => _ctx.Host;

        /// <summary>Tirs de guet déclenchés par move/téléport (fourni par la façade, phase 4).</summary>
        public Action<TacticalUnit, HexCoordinates> OverwatchTrigger;

        /// <summary>
        /// Budget PA fictif des déplacements hors combat : libres et illimités.
        /// Largement au-dessus de toute carte (rayon 8 ≈ 217 cases).
        /// </summary>
        private const int ExplorationFreeAP = 100000;

        public ArenaTargetingService(ArenaContext ctx, ArenaSmokeService smoke)
        {
            _ctx = ctx;
            _smoke = smoke;
        }

        /// <summary>
        /// Vrai si l'unité active est un allié contrôlé manuellement par le joueur.
        /// En mode FullAuto, l'IA contrôle aussi les alliés : aucun sélecteur de
        /// tuiles de mouvement (portée bleue / aperçu de chemin / clic) ne doit apparaître.
        /// En mode Normal (semi-auto), les alliés restent manuels : les sélecteurs s'affichent.
        /// </summary>
        public bool IsPlayerManualControl(TacticalUnit unit)
        {
            if (unit == null || !unit.IsPlayerControlled || unit.IsMoving) return false;
            if (_ctx.TurnManager != null && _ctx.TurnManager.IsInExploration) return true;
            if (_ctx.AI != null && _ctx.AI.Mode == CombatAIMode.FullAuto) return false;
            return true;
        }

        public void HandleMouseInteraction()
        {
            var grid = _ctx.Grid;
            var visualizer = _ctx.Visualizer;
            var turnManager = _ctx.TurnManager;
            if (CombatHUD.IsPaused)
            {
                // Sur pause : seule la fonction dock des fenêtres flottantes reste active.
                // Clic carte 3D (hors UI/HUD) => repli, sans sélection ni déplacement.
                if (Input.GetMouseButtonDown(0)
                    && !Killtime.UI.FloatingWindowChrome.IsPointerOverAnyWindow())
                {
                    Killtime.UI.FloatingWindowChrome.OnMapClicked();
                }
                return;
            }
            if (UnityEngine.Camera.main == null || visualizer == null) return;

            // Filet : unité active éventée (transition de scène, acteur détruit)
            // = clics 3D totalement morts. On reprend le premier allié vivant.
            if (turnManager != null && turnManager.IsInExploration && turnManager.ActiveUnit == null)
            {
                var fallback = (_state.PlayerUnit != null && _state.PlayerUnit.Stats != null && _state.PlayerUnit.Stats.IsAlive)
                    ? _state.PlayerUnit : null;
                if (fallback == null)
                {
                    for (int i = 0; i < _state.AdditionalPlayers.Count; i++)
                    {
                        var p = _state.AdditionalPlayers[i];
                        if (p != null && p.Stats != null && p.Stats.IsAlive) { fallback = p; break; }
                    }
                }
                if (fallback != null) turnManager.SetActiveUnitExplicit(fallback);
            }

            // Souris sur fenêtre flottante/HUD : interaction exclusive avec l'UI, pas la carte 3D.
            // Évite clics fantômes (mouvement/sélection) et nettoie les survols derrière la fenêtre.
            if (Killtime.UI.FloatingWindowChrome.IsPointerOverAnyWindow())
            {
                visualizer.SetHoveredCoord(null);
                visualizer.ClearPathPreview();
                return;
            }

            // Tissage Duo-Tech en cours (dessin T1/T2 ou exécution) : la souris
            // appartient au tissage. Ni survol, ni aperçu de chemin, ni clic de
            // déplacement — sinon le drag de dessin déplacerait l'unité active.
            if (DuoTech.DuoTechWeaveController.AnyWeaving)
            {
                visualizer.SetHoveredCoord(null);
                visualizer.ClearPathPreview();
                return;
            }

            Ray ray = UnityEngine.Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                if (grid.TryGetNodeAtWorldPosition(hit.point, out var hoveredNode))
                {
                    visualizer.SetHoveredCoord(hoveredNode.Coordinates);

                    var activeUnit = turnManager.ActiveUnit;
                    if (IsPlayerManualControl(activeUnit))
                    {
                        var start = activeUnit.CurrentCoords;
                        var target = hoveredNode.Coordinates;

                        var hitUnit = hit.collider.GetComponentInParent<TacticalUnit>() ?? GetUnitAtCoords(target);

                        if (Input.GetMouseButtonDown(1))
                        {
                            if (hitUnit != null)
                            {
                                if (hitUnit == activeUnit)
                                {
                                    CombatHUD.Instance?.ToggleActiveUnitContextMenu();
                                    return;
                                }
                                else if (!hitUnit.IsPlayerControlled)
                                {
                                    SelectTarget(hitUnit);
                                }
                            }
                        }

                        if (Input.GetMouseButtonDown(0))
                        {
                            Killtime.UI.FloatingWindowChrome.OnMapClicked();
                            var clickedUnit = hitUnit;

                            if (clickedUnit == activeUnit)
                            {
                                CombatHUD.Instance?.ToggleActiveUnitContextMenu();
                                return;
                            }

                            if (clickedUnit != null && clickedUnit != activeUnit)
                            {
                                if (turnManager != null && turnManager.IsInExploration && clickedUnit.IsPlayerControlled)
                                {
                                    SwitchActiveExplorer(clickedUnit);
                                    return;
                                }

                                SelectTarget(clickedUnit);
                                return;
                            }

                            bool isTargetOccupied = clickedUnit != null || hoveredNode.IsOccupied;
                            if (!start.Equals(target) && hoveredNode.IsWalkable && !isTargetOccupied)
                            {
                                // Anti-chevauchement : en exploration on peut changer
                                // d'opérateur pendant qu'un autre marche encore ; deux
                                // trajets simultanés se croisent et empilent les unités
                                // (validations faites sur des positions périmées).
                                if (IsAnotherPlayerUnitMoving(activeUnit))
                                {
                                    _host.ArenaLog($"⏳ <b>{activeUnit.Stats.Name}</b> attend la fin du déplacement en cours.");
                                    activeUnit.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("⏳ Un allié marche...", new Color(1f, 0.8f, 0.3f));
                                    return;
                                }
                                // Hors combat : déplacement libre (ni calcul ni coût de PA)
                                // + faufile (on traverse les personnages sans s'y arrêter).
                                bool isExploration = turnManager != null && turnManager.IsInExploration;
                                bool traverse = isExploration;
                                int availableAP = isExploration ? ExplorationFreeAP : activeUnit.Stats.CurrentActionPoints;
                                bool hasInvisibleSteps = !isExploration && activeUnit.Stats.HasSpecialization("Protocole des Pas Invisibles") && activeUnit.Stats.MovesThisTurn == 0;
                                if (hasInvisibleSteps) availableAP += 1;

                                if (!activeUnit.Stats.CanMove())
                                {
                                    _host.ArenaLog($"⚠️ <b>{activeUnit.Stats.Name}</b> ne peut pas se déplacer ({activeUnit.Stats.ActiveStatus}).");
                                    activeUnit.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("IMMOBILISÉ", Color.yellow);
                                    return;
                                }

                                // Si l'unité maintenait une prise de lutte et se déplace seule (hors traînée), la prise cède
                                if (Killtime.Core.Combat.GrappleState.IsGrappling(activeUnit.Stats))
                                {
                                    Killtime.Core.Combat.GrappleState.ReleaseGrapple(activeUnit.Stats);
                                    _host.ArenaLog($"🥋 <b>{activeUnit.Stats.Name}</b> rompt sa prise de lutte pour se déplacer.");
                                }

                                var path = _ctx.Pathfinder.FindPath(start, target, availableAP, out int apCost, activeUnit.FootprintType, traverse);

                                if (path.Count > 0)
                                {
                                    int effectiveCost = isExploration ? 0 : activeUnit.ComputeMovementAPCost(apCost);
                                    int remainingAP = activeUnit.Stats.CurrentActionPoints - effectiveCost;
                                    string moveTag = isExploration ? "Déplacement libre" : (hasInvisibleSteps ? $"Déplacement (-{effectiveCost} PA / Pas Invisibles)" : $"Déplacement (-{effectiveCost} PA)");
                                    if (isExploration)
                                        _host.ArenaLog($"🚶 <b>{activeUnit.Stats.Name}</b> avance de {path.Count - 1} case(s) vers ({target.Q}, {target.R})");
                                    else
                                        _host.ArenaLog($"🚶 <b>{activeUnit.Stats.Name}</b> avance de {path.Count - 1} case(s) vers ({target.Q}, {target.R}) [Coût: -{effectiveCost} PA | Restant: {remainingAP} PA]");
                                    activeUnit.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText(moveTag, new Color(0.2f, 0.85f, 1.0f));

                                    visualizer.ClearPathPreview();
                                    if (KilltimeAudioManager.Instance != null)
                                        KilltimeAudioManager.Instance.PlayAt(SoundId.Move_Dash, activeUnit.transform.position, 0.5f);
                                    _host.ArenaStartCoroutine(activeUnit.MoveAlongPath(path, grid, effectiveCost, traverse));
                                }
                            }
                        }
                        else
                        {
                            // Aperçu du chemin au survol
                            if (!start.Equals(target) && hoveredNode.IsWalkable && !hoveredNode.IsOccupied)
                            {
                                bool previewTraverse = turnManager != null && turnManager.IsInExploration;
                                int previewAP = previewTraverse ? ExplorationFreeAP : activeUnit.Stats.CurrentActionPoints;
                                if (!previewTraverse && activeUnit.Stats.HasSpecialization("Protocole des Pas Invisibles") && activeUnit.Stats.MovesThisTurn == 0)
                                    previewAP += 1;

                                var path = _ctx.Pathfinder.FindPath(start, target, previewAP, out _, activeUnit.FootprintType, previewTraverse);
                                visualizer.SetPathPreview(path);
                            }
                            else
                            {
                                visualizer.ClearPathPreview();
                            }
                        }
                    }
                    else
                    {
                        // Mode FullAuto (IA contrôle les alliés) ou tour ennemi :
                        // aucun sélecteur de mouvement (ni aperçu de chemin, ni clic).
                        visualizer.ClearPathPreview();
                    }
                }
            }
            else
            {
                visualizer.SetHoveredCoord(null);
                visualizer.ClearPathPreview();
            }
        }

        public void UpdateReachableHighlights()
        {
            var turnManager = _ctx.TurnManager;
            var visualizer = _ctx.Visualizer;
            var active = turnManager.ActiveUnit;
            if (IsPlayerManualControl(active) && visualizer != null)
            {
                if (!active.Stats.CanMove())
                {
                    visualizer.SetReachableCoords(null);
                    return;
                }

                bool reachTraverse = turnManager != null && turnManager.IsInExploration;
                int reachAP = reachTraverse ? ExplorationFreeAP : active.Stats.CurrentActionPoints;
                if (!reachTraverse && active.Stats.HasSpecialization("Protocole des Pas Invisibles") && active.Stats.MovesThisTurn == 0)
                    reachAP += 1;

                var reachable = _ctx.Pathfinder.GetReachableCoordinates(active.CurrentCoords, reachAP, reachTraverse);
                visualizer.SetReachableCoords(reachable);
            }
            else if (visualizer != null)
            {
                visualizer.SetReachableCoords(null);
            }
        }

        /// <summary>
        /// Rafraîchit chaque frame la ligne de visée de détection du couvert entre
        /// l'unité active et la cible courante (bon marché : balayage de quelques cases).
        /// Au changement de cible : log diagnostic (fraction visible, cases et
        /// accessoires bloqueurs, taille du registre 3D).
        /// </summary>
        public void UpdateCoverLineOfSight(bool showCoverLOS)
        {
            var visualizer = _ctx.Visualizer;
            if (!showCoverLOS || visualizer == null)
            {
                visualizer?.ClearLineOfSight();
                _state.LosLoggedAttacker = null;
                _state.LosLoggedTarget = null;
                return;
            }

            var active = _ctx.TurnManager != null ? _ctx.TurnManager.ActiveUnit : null;
            if (active != null && _state.CurrentTarget != null && _state.CurrentTarget.Stats != null && _state.CurrentTarget.Stats.IsAlive)
            {
                visualizer.ShowLineOfSight(active.CurrentCoords, _state.CurrentTarget.CurrentCoords, _state.SmokeZones, ArenaSmokeService.SeesThroughSmoke(active));
                if (active != _state.LosLoggedAttacker || _state.CurrentTarget != _state.LosLoggedTarget)
                {
                    _state.LosLoggedAttacker = active;
                    _state.LosLoggedTarget = _state.CurrentTarget;
                    LogCoverDiagnostics(active, _state.CurrentTarget);
                }
            }
            else
            {
                visualizer.ClearLineOfSight();
                _state.LosLoggedAttacker = null;
                _state.LosLoggedTarget = null;
            }
        }

        private void LogCoverDiagnostics(TacticalUnit attacker, TacticalUnit defender)
        {
            var grid = _ctx.Grid;
            if (grid == null) return;
            CoverScanResult scan;
            try { scan = CoverSystem.ScanCover(attacker.CurrentCoords, defender.CurrentCoords, grid, smokeZones: _state.SmokeZones, seesThroughSmoke: ArenaSmokeService.SeesThroughSmoke(attacker)); }
            catch (Exception e)
            {
                _host.ArenaLog($"🎯 Ligne de mire : échec du scan ({e.Message}).");
                return;
            }

            var blockers = new System.Collections.Generic.List<string>();
            if (scan.Rays != null)
            {
                var seen = new System.Collections.Generic.HashSet<HexCoordinates>();
                for (int i = 0; i < scan.Rays.Count && blockers.Count < 4; i++)
                {
                    var ray = scan.Rays[i];
                    if (!ray.Blocked || !ray.HasBlockingCell || !seen.Add(ray.BlockingCell)) continue;
                    string tag = $"({ray.BlockingCell.Q},{ray.BlockingCell.R})";
                    var node = grid.GetNode(ray.BlockingCell);
                    if (node != null && node.Cover != CoverType.None) tag += $"[{node.Cover}]";
                    if (PropObstacleRegistry.TryGet(ray.BlockingCell, out PropObstacle prop))
                        tag += $"[Prop '{prop.PropName}' h={prop.MaxY - prop.MinY:0.##}m r={prop.Radius:0.##}]";
                    blockers.Add(tag);
                }
            }

            _host.ArenaLog($"🎯 <b>Ligne de mire {attacker.Stats.Name} ➔ {defender.Stats.Name}</b> :"
                + $" {CoverSystem.Label(scan.Cover)} — {scan.VisibleRays}/{scan.TestedRays} rayons"
                + (blockers.Count > 0 ? $" — bloqueurs : {string.Join(", ", blockers)}" : " — aucun bloqueur")
                + $" — registre 3D : {PropObstacleRegistry.Count} obstacle(s).");
        }

        /// <summary>
        /// Vrai si un autre avatar contrôlé par le joueur est en train de marcher.
        /// Verrou anti-chevauchement pour les ordres de déplacement manuels.
        /// </summary>
        private bool IsAnotherPlayerUnitMoving(TacticalUnit activeUnit)
        {
            var all = TacticalUnitRegistry.AllUnits;
            for (int i = 0; i < all.Count; i++)
            {
                var u = all[i];
                if (u == null || u == activeUnit || !u.IsPlayerControlled || !u.IsMoving) continue;
                return true;
            }
            return false;
        }

        public TacticalUnit GetUnitAtCoords(HexCoordinates coords)
        {
            if (_state.PlayerUnit != null && _state.PlayerUnit.Stats != null && _state.PlayerUnit.Stats.IsAlive && _state.PlayerUnit.CurrentCoords.Equals(coords)) return _state.PlayerUnit;
            for (int i = 0; i < _state.AdditionalPlayers.Count; i++)
            {
                var p = _state.AdditionalPlayers[i];
                if (p != null && p.Stats != null && p.Stats.IsAlive && p.CurrentCoords.Equals(coords)) return p;
            }
            for (int i = 0; i < _state.SparringDummies.Count; i++)
            {
                var d = _state.SparringDummies[i];
                if (d != null && d.Stats != null && d.Stats.IsAlive && d.CurrentCoords.Equals(coords)) return d;
            }

            var all = TacticalUnitRegistry.AllUnits;
            for (int i = 0; i < all.Count; i++)
            {
                var u = all[i];
                if (u != null && u.Stats != null && u.Stats.IsAlive && u.CurrentCoords.Equals(coords))
                {
                    return u;
                }
            }
            return null;
        }

        public void SelectTarget(TacticalUnit target)
        {
            if (target == null) return;
            if (_state.CurrentTarget == target) return;

            _state.CurrentTarget = target;
            if (_ctx.Visualizer != null)
            {
                _ctx.Visualizer.SetTargetCoord(target.CurrentCoords);
            }
            _host.NotifyTargetChanged(target);
            if (KilltimeAudioManager.Instance != null)
                KilltimeAudioManager.Instance.PlayUI(SoundId.VATS_Lock, 0.7f);
            _host.ArenaLog($"🎯 Cible verrouillée : <b>{target.Stats.Name}</b> ({target.Stats.CurrentHealth}/{target.Stats.MaxHealth} PV)");

            if (Killtime.Multi.VTTTableSync.Instance != null
                && !Killtime.Multi.VTTTableSync.Instance.IsApplyingRemoteAction
                && Killtime.Multi.VTTRoomManager.Instance != null
                && Killtime.Multi.VTTRoomManager.Instance.InRoom)
            {
                if (Killtime.Multi.VTTRoomManager.Instance.IsGM)
                {
                    Killtime.Multi.VTTTableSync.Instance.BroadcastTargetSelection(target);
                }
                else
                {
                    Killtime.Multi.VTTTableSync.Instance.RequestTargetSelection(target);
                }
            }
        }

        public void AutoTargetOpponent(TacticalUnit activeUnit)
        {
            if (activeUnit == null) return;

            if (activeUnit.IsPlayerControlled)
            {
                AutoTargetNextAlive();
            }
            else
            {
                if (_state.PlayerUnit != null && _state.PlayerUnit.Stats != null && _state.PlayerUnit.Stats.IsAlive && !_state.PlayerUnit.Stats.IsSurrendered)
                {
                    SelectTarget(_state.PlayerUnit);
                    return;
                }
                for (int i = 0; i < _state.AdditionalPlayers.Count; i++)
                {
                    var p = _state.AdditionalPlayers[i];
                    if (p != null && p.Stats != null && p.Stats.IsAlive && !p.Stats.IsSurrendered)
                    {
                        SelectTarget(p);
                        return;
                    }
                }
            }
        }

        public void HandleTargetMovedFollow(TacticalUnit unit, List<HexCoordinates> path, int apCost)
        {
            if (_state.CurrentTarget != null && _state.CurrentTarget == unit && _ctx.Visualizer != null && path != null && path.Count > 0)
            {
                _ctx.Visualizer.SetTargetCoord(path[path.Count - 1]);
            }
            if (unit != null && unit.Stats != null && unit.Stats.IsAlive && _state.SmokeZones.Count > 0)
            {
                _smoke.TickSmokeForUnit(unit, _state.SmokeZones);
            }
            // RD-030 : l'arrivée en case adverse déclenche les tirs de guet en portée.
            if (unit != null && path != null && path.Count > 1)
            {
                OverwatchTrigger?.Invoke(unit, path[path.Count - 1]);
            }
        }

        public void HandleTargetTeleportedFollow(TacticalUnit unit, HexCoordinates coords)
        {
            if (_state.CurrentTarget != null && _state.CurrentTarget == unit && _ctx.Visualizer != null)
            {
                _ctx.Visualizer.SetTargetCoord(coords);
            }
            if (unit != null && unit.Stats != null && unit.Stats.IsAlive && _state.SmokeZones.Count > 0)
            {
                _smoke.TickSmokeForUnit(unit, _state.SmokeZones);
            }
            // RD-030 : la réapparition déclenche les tirs de guet en portée.
            if (unit != null)
            {
                OverwatchTrigger?.Invoke(unit, coords);
            }
        }

        public void CycleTarget()
        {
            if (_state.SparringDummies.Count == 0) return;
            int idx = _state.SparringDummies.IndexOf(_state.CurrentTarget);
            int nextIdx = (idx + 1) % _state.SparringDummies.Count;
            if (KilltimeAudioManager.Instance != null)
                KilltimeAudioManager.Instance.PlayUI(SoundId.VATS_TargetChange, 0.6f);
            SelectTarget(_state.SparringDummies[nextIdx]);
        }

        public void AutoTargetNextAlive()
        {
            for (int i = 0; i < _state.SparringDummies.Count; i++)
            {
                var dummy = _state.SparringDummies[i];
                if (dummy != null && dummy.Stats != null && dummy.Stats.IsAlive && !dummy.Stats.IsSurrendered)
                {
                    SelectTarget(dummy);
                    return;
                }
            }

            if (_ctx.TurnManager != null)
            {
                var units = _ctx.TurnManager.TurnOrder;
                for (int i = 0; i < units.Count; i++)
                {
                    var u = units[i];
                    if (u != null && !u.IsPlayerControlled && u.Stats != null && u.Stats.IsAlive && !u.Stats.IsSurrendered)
                    {
                        SelectTarget(u);
                        return;
                    }
                }
            }
        }

        public void SwitchActiveExplorer(TacticalUnit unit)
        {
            if (unit == null) return;
            _ctx.TurnManager?.SetActiveUnitExplicit(unit);
            if (_ctx.Camera != null)
            {
                _ctx.Camera.FocusOn(unit.transform);
            }
            var vis = unit.GetComponent<TacticalUnitVisual>();
            vis?.SpawnFloatingText($"Opérateur : {unit.Stats.Name}", Color.cyan);
            if (KilltimeAudioManager.Instance != null)
                KilltimeAudioManager.Instance.PlayUI(SoundId.UI_Filter, 0.5f);
            _host.ArenaLog($"🕹️ Contrôle basculé sur <b>{unit.Stats.Name}</b>.");
        }

        /// <summary>
        /// Livre V §21.3 : couvert de la cible vue par l'attaquant (None au contact,
        /// Full si non visible). Utilisé par le HUD (affichage) et ExecuteAttack (blocage).
        /// </summary>
        public CoverType GetCoverToTarget(TacticalUnit attacker, TacticalUnit defender)
        {
            var grid = _ctx.Grid;
            if (attacker == null || defender == null || grid == null) return CoverType.None;
            if (TitanFootprint.MinDistanceBetweenUnits(attacker.CurrentCoords, attacker.FootprintType, defender.CurrentCoords, defender.FootprintType) <= 1) return CoverType.None;
            return CoverSystem.EvaluateCover(
                attacker.CurrentCoords,
                defender.CurrentCoords,
                grid,
                defender.FootprintType,
                attacker.FootprintType,
                _state.SmokeZones,
                ArenaSmokeService.SeesThroughSmoke(attacker));
        }

        /// <summary>
        /// RD-036 Hauteur / contrebas : élévation d'une unité (0 si grille
        /// absente ou case inconnue — les arènes sans relief restent à 0).
        /// </summary>
        public float GetUnitElevation(TacticalUnit unit)
        {
            var grid = _ctx.Grid;
            if (unit == null || grid == null) return 0f;
            try
            {
                var node = grid.GetNode(unit.CurrentCoords);
                if (node != null) return node.Elevation;
            }
            catch { /* ignore */ }
            return 0f;
        }

        /// <summary>
        /// RD-036 : ruling hauteur/contrebas attaquant ➔ défenseur pour le type
        /// d'attaque donné (palier 1.5 m, table mêlée + gate, ±1 balistique,
        /// laser/énergie exempté). Voir ElevationAdvantage.
        /// </summary>
        public ElevationRuling GetElevationRuling(TacticalUnit attacker, TacticalUnit defender, SkillType attackSkill, InventoryItem weapon, bool isMeleeAttack)
        {
            return ElevationAdvantage.Resolve(
                GetUnitElevation(attacker), GetUnitElevation(defender),
                isMeleeAttack, attackSkill, weapon);
        }
    }
}

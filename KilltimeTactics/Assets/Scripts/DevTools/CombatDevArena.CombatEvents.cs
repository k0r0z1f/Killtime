using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Audio;
using Killtime.Core.Character;
using Killtime.Core.Combat;
using Killtime.Core.Dice;
using Killtime.Core.Chrono;
using Killtime.Core.Inventory;
using Killtime.Core.Rules;
using Killtime.CameraSystem;
using Killtime.WebGL;
using Killtime.UI;
using Killtime.Tactics.Arena;

namespace Killtime.Tactics
{
    // Events tours/combat, fumee/DoT, obstacle, grappin (RD-152).
    public partial class CombatDevArena : MonoBehaviour, IArenaHost
    {
        /// <summary>
        /// Remplace les zones persistantes par le snapshot d'autorité GM (VTT RD-089)
        /// et rafraîchit l'overlay au sol. Appelé uniquement depuis l'application
        /// distante (jamais en émission locale).
        /// </summary>
        public void ApplyRemoteSmokeZones(List<SmokeZone> zones)
        {
            _smokeZones.Clear();
            if (zones != null)
            {
                for (int i = 0; i < zones.Count; i++)
                {
                    if (zones[i].TurnsLeft > 0) _smokeZones.Add(zones[i]);
                }
            }
            if (_gridVisualizer == null) _gridVisualizer = GetComponent<HexGridVisualizer>() ?? FindAnyObjectByType<HexGridVisualizer>();
            _gridVisualizer?.UpdatePersistentZones(_smokeZones);
        }

        private void HandleUnitStatusExpired(TacticalUnit unit, List<StatusEffect> expiredList)
        {
            _turnEvents.HandleUnitStatusExpired(unit, expiredList);
        }

        private void HandleInitiativeRolled(IReadOnlyList<InitiativeRollResult> results)
        {
            _turnEvents.HandleInitiativeRolled(results);
        }

        private void HandleCombatEnded(CombatOutcome outcome)
        {
            _turnEvents.HandleCombatEnded(outcome);
        }

        private void HandleTurnStarted(TacticalUnit unit)
        {
            _turnEvents.HandleTurnStarted(unit);
        }

        /// <summary>Décompte des zones à chaque round ; dissipation loggée.</summary>
        private void HandleRoundStarted(int round)
        {
            _turnEvents.HandleRoundStarted(round);
        }

        /// <summary>Case couverte par un écran de fumée actif ?</summary>
        public bool IsSmoked(HexCoordinates coords)
        {
            if (_smokeService != null) return _smokeService.IsSmoked(coords, _smokeZones);
            return SmokeScreen.IsCellSmoked(coords, _smokeZones);
        }

        /// <summary>Thermique (spécialisation ou Jumelles) : voit à travers la fumée.</summary>
        public static bool SeesThroughSmoke(TacticalUnit viewer)
        {
            return ArenaSmokeService.SeesThroughSmoke(viewer);
        }

        /// <summary>
        /// RD-045 : journalise le tick DoT résiduel du début de tour (déjà appliqué
        /// par TurnManager.RefreshAndNotifyActiveUnitTurnStart) et gère la mort.
        /// </summary>
        public void LogDotTick(TacticalUnit unit)
        {
            _smokeService?.LogDotTick(unit);
        }

        /// Effets d'environnement en début de tour : dégâts (ignore armure, barrière
        /// d'abord) + statuts de la zone. Masque : filtre les gaz (dégâts /2).
        /// </summary>
        public void TickSmokeForUnit(TacticalUnit unit)
        {
            _smokeService?.TickSmokeForUnit(unit, _smokeZones);
        }

        /// <summary>
        /// Grappin Magnétique (2 PA, 2-4 cases) : harpon 3D, À Terre si Diff ≥ 2.
        /// Le grappin revient (non consommé). Requiert l'outil en poche.
        /// </summary>
        /// <summary>
        /// Attaque directe contre un obstacle ou élément du décor (Livre V).
        /// Applique les dégâts selon le matériau et le type de frappe (Contondant, Tranchant, Perforant, Thermique...).
        /// </summary>
        public bool ExecuteAttackOnObstacle(
            HexCoordinates targetCoords,
            SkillType attackSkill = SkillType.ManiementArmes,
            TacticalUnit explicitAttacker = null)
        {
            return _attack.ExecuteAttackOnObstacle(targetCoords, attackSkill, explicitAttacker);
        }

        public void ExecuteGrapnel(TacticalUnit attacker, TacticalUnit defender)
        {
            _attack.ExecuteGrapnel(attacker, defender);
        }

        /// <summary>
        /// Vrai si l'unité active est un allié contrôlé manuellement par le joueur.
        /// En mode FullAuto, l'IA contrôle aussi les alliés : aucun sélecteur de
        /// tuiles de mouvement (portée bleue / aperçu de chemin / clic) ne doit apparaître.
        /// En mode Normal (semi-auto), les alliés restent manuels : les sélecteurs s'affichent.
        /// </summary>
        private bool IsPlayerManualControl(TacticalUnit unit)
        {
            return _targeting != null ? _targeting.IsPlayerManualControl(unit) : unit != null && unit.IsPlayerControlled && !unit.IsMoving;
        }

        public void RegisterSceneParty(TacticalUnit player, IEnumerable<TacticalUnit> allies)
        {
            _unitStore.RegisterSceneParty(player, allies);
        }
    }
}

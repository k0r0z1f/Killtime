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
    // Souris, ciblage, couvert/elevation (RD-152).
    public partial class CombatDevArena : MonoBehaviour, IArenaHost
    {
        private void HandleMouseInteraction()
        {
            _targeting.HandleMouseInteraction();
        }

        private void UpdateReachableHighlights()
        {
            _targeting.UpdateReachableHighlights();
        }

        public TacticalUnit GetUnitAtCoords(HexCoordinates coords)
        {
            return _targeting.GetUnitAtCoords(coords);
        }

        public void SelectTarget(TacticalUnit target)
        {
            _targeting.SelectTarget(target);
        }

        public void AutoTargetOpponent(TacticalUnit activeUnit)
        {
            _targeting.AutoTargetOpponent(activeUnit);
        }

        private void HandleTargetMovedFollow(TacticalUnit unit, List<HexCoordinates> path, int apCost)
        {
            _targeting.HandleTargetMovedFollow(unit, path, apCost);
        }

        private void HandleTargetTeleportedFollow(TacticalUnit unit, HexCoordinates coords)
        {
            _targeting.HandleTargetTeleportedFollow(unit, coords);
        }

        public void CycleTarget()
        {
            _targeting.CycleTarget();
        }

        public void RegisterHostileUnit(TacticalUnit unit)
        {
            _unitStore.RegisterHostileUnit(unit);
        }

        public void AutoTargetNextAlive()
        {
            _targeting.AutoTargetNextAlive();
        }

        public void SwitchActiveExplorer(TacticalUnit unit)
        {
            _targeting.SwitchActiveExplorer(unit);
        }

        /// <summary>
        /// Déclenche une passe d'armes ou tir ciblé selon le duel aveugle officiel (Livres II §7 + VI §20.1) :
        /// déclaration attaquant (mise PA/PE cachée) -> déclaration défenseur à l'aveugle
        /// -> révélation simultanée -> résolution normale. Conforme à l'Axiome Fondateur : résolution par compétence.
        /// defenderBonusAP &lt; 0 = défense réactive auto (besoin réel après révélation de l'attaque).
        /// </summary>
        /// <summary>
        /// Livre V §21.3 : couvert de la cible vue par l'attaquant (None au contact,
        /// Full si non visible). Utilisé par le HUD (affichage) et ExecuteAttack (blocage).
        /// </summary>
        public CoverType GetCoverToTarget(TacticalUnit attacker, TacticalUnit defender)
        {
            return _targeting.GetCoverToTarget(attacker, defender);
        }

        /// <summary>
        /// RD-036 Hauteur / contrebas : élévation d'une unité (0 si grille
        /// absente ou case inconnue — les arènes sans relief restent à 0).
        /// </summary>
        public float GetUnitElevation(TacticalUnit unit)
        {
            return _targeting.GetUnitElevation(unit);
        }

        /// <summary>
        /// RD-036 : ruling hauteur/contrebas attaquant ➔ défenseur pour le type
        /// d'attaque donné (palier 1.5 m, table mêlée + gate, ±1 balistique,
        /// laser/énergie exempté). Voir ElevationAdvantage.
        /// </summary>
        public ElevationRuling GetElevationRuling(TacticalUnit attacker, TacticalUnit defender, SkillType attackSkill, InventoryItem weapon, bool isMeleeAttack)
        {
            return _targeting.GetElevationRuling(attacker, defender, attackSkill, weapon, isMeleeAttack);
        }
    }
}

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
    // Guet, opportunite, tenir/retarder/canalisation, furtivite (RD-152).
    public partial class CombatDevArena : MonoBehaviour, IArenaHost
    {
        /// <summary>
        /// RD-030 Guet / Overwatch : met l'unité en guet (2 PA, rayon = portée de
        /// l'arme à distance équipée). Le tir de réaction partira automatiquement
        /// pendant le tour adverse (CheckOverwatchTriggers).
        /// </summary>
        public bool TryEnterOverwatch(TacticalUnit unit, out string message)
        {
            return _overwatch.TryEnterOverwatch(unit, out message);
        }

        /// <summary>
        /// RD-030 : après qu'une unité a agi en `atCoords` (fin de déplacement,
        /// téléportation, attaque), chaque guetteur ennemi dont le rayon couvre
        /// cette case lâche son tir de réaction en duel aveugle automatique.
        /// 1 tir par guet, couvert total = pas de tir, munitions consommées.
        /// </summary>
        public void CheckOverwatchTriggers(TacticalUnit mover, HexCoordinates atCoords)
        {
            _overwatch.CheckOverwatchTriggers(mover, atCoords);
        }

        /// <summary>
        /// RD-031 Blocage (hook TacticalUnit.StepBlockCheck, avant chaque pas) :
        /// chaque briseur en posture Blocage peut tenter de retenir le fuyard
        /// (duel opposé Athlétisme, 1 PA). Succès = pas annulé, sans grapple ;
        /// les PA du fuyard sont remboursés par MoveAlongPath. Vrai = bloqué.
        /// </summary>
        public bool CheckOpportunityBlock(TacticalUnit mover, HexCoordinates from, HexCoordinates to)
        {
            return _overwatch.CheckOpportunityBlock(mover, from, to);
        }

        /// <summary>
        /// RD-031 (hook TacticalUnit.OnAnyUnitStepCompleted, après chaque pas) :
        /// chaque briseur non-bloqueur réagit selon sa posture — Frappe (gratuite),
        /// Balayage (gratuit, Jambes), Poursuite (1 PA, suit d'1 case).
        /// </summary>
        private void HandleUnitStepOpportunity(TacticalUnit mover, HexCoordinates from, HexCoordinates to, bool provoked)
        {
            _overwatch.HandleUnitStepOpportunity(mover, from, to, provoked);
        }

        /// <summary>
        /// RD-031 Décrochage (Livre VI §26.2) : paie 1 PA pour que le prochain
        /// déplacement de l'unité ne provoque aucune réaction d'opportunité.
        /// </summary>
        public bool TryRequestSafeDisengage(TacticalUnit unit, out string message)
        {
            return _overwatch.TryRequestSafeDisengage(unit, out message);
        }

        /// <summary>
        /// RD-048 Tenir : l'unité active passe son tour en conservant ses PA et
        /// se déclare en attente (0 PA). Interruption ultérieure via le
        /// TurnManager (TryResumeHeldUnit, rejouée juste après l'unité active).
        /// </summary>
        public bool TryHoldAction(TacticalUnit unit, out string message)
        {
            return _overwatch.TryHoldAction(unit, out message);
        }

        /// <summary>
        /// RD-048 Retarder : l'unité active repousse son tour (initiative
        /// -pénalité, 1/round), réinsérée plus tard dans le round en cours.
        /// </summary>
        public bool TryDelayTurn(TacticalUnit unit, out string message)
        {
            return _overwatch.TryDelayTurn(unit, out message);
        }

        /// <summary>
        /// RD-048 Canalisation / Action en Progression : étale l'effort sur
        /// plusieurs temps (coût 1 PA, +2 au jet à l'attaque, -1 en défense,
        /// interrompue si dégât net &gt; encaissement).
        /// </summary>
        public bool TryBeginChanneling(TacticalUnit unit, string label, out string message)
        {
            return _overwatch.TryBeginChanneling(unit, label, out message);
        }

        /// <summary>
        /// RD-048 : notifie une canalisation rompue par des dégâts hors duel
        /// (grenades, zone, DoT) et affiche le retour visuel. Les duels directs
        /// tracent déjà l'interruption dans leur propre journal de combat.
        /// </summary>
        public void NotifyChannelDamage(TacticalUnit defender, int finalDamageApplied)
        {
            _overwatch.NotifyChannelDamage(defender, finalDamageApplied);
        }

        /// <summary>
        /// RD-048 : retour visuel d'une interruption tracée par le duel
        /// (direct, guet, opportunité, assaut groupé). Le journal contient déjà
        /// la ligne « CANALISATION INTERROMPUE ».
        /// </summary>
        private void HandleChannelInterruptionFx(TacticalUnit defender, bool interrupted)
        {
            _overwatch.HandleChannelInterruptionFx(defender, interrupted);
        }

        /// <summary>
        /// RD-049 Furtivité / Stealth : fait passer l'unité en furtivité (2 PA).
        /// Considéré invisible dans le brouillard de guerre, confère +2 à la
        /// prochaine attaque et interdit toute réaction adverse (embuscade).
        /// </summary>
        public bool TryEnterStealth(TacticalUnit unit, out string message)
        {
            return _overwatch.TryEnterStealth(unit, out message);
        }
    }
}

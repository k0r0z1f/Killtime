using System.Collections.Generic;
using Killtime.Core.Character;
using Killtime.Core.Rules;

namespace Killtime.Core.Combat
{
    /// <summary>
    /// Gestionnaire d'état de la Charge et du Sprint (RD-038, Livre VI — Manœuvres Mobiles) :
    /// - Charge 3+ cases : +2 dégâts sur la prochaine attaque au contact (mêlée),
    ///   -1 en défense jusqu'au début du prochain tour personnel.
    /// - Sprint : tout déplacement de 3+ cases bloque le tir (armes à distance) ce tour.
    ///
    /// Keyé sur <see cref="CharacterStats"/> pour rester dans la couche Core
    /// (utilisable indifféremment par le joueur et l'IA, sans dépendance Unity).
    /// </summary>
    public static class ChargeState
    {
        private static readonly HashSet<CharacterStats> _sprintedThisTurn = new();
        private static readonly HashSet<CharacterStats> _chargeBonus = new();
        private static readonly HashSet<CharacterStats> _defensePenalty = new();
        private static readonly Dictionary<CharacterStats, int> _tilesMovedThisTurn = new();

        /// <summary>
        /// Enregistre un déplacement accompli pour cette unité.
        /// Si le trajet continu atteint ou dépasse le seuil (3 cases par défaut) :
        /// - Active le flag de sprint (bloque le tir ce tour).
        /// - Confère le bonus de charge (+2 dégâts sur prochaine frappe au contact).
        /// - Inflige le malus défensif (-1 défense jusqu'au prochain tour personnel).
        /// </summary>
        public static bool ApplyMovement(CharacterStats unit, int tileDistance)
        {
            if (unit == null || !unit.IsAlive) return false;

            int currentMoved = _tilesMovedThisTurn.GetValueOrDefault(unit, 0);
            _tilesMovedThisTurn[unit] = currentMoved + tileDistance;

            int threshold = CoreRulesConfig.Instance != null ? CoreRulesConfig.Instance.ChargeMinDistance : 3;
            if (tileDistance >= threshold)
            {
                _sprintedThisTurn.Add(unit);
                _chargeBonus.Add(unit);
                _defensePenalty.Add(unit);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Vrai si l'unité a sprinté (déplacement de 3+ cases) pendant ce tour.
        /// RD-038 : Le sprint bloque tout tir à distance pour le reste du tour.
        /// </summary>
        public static bool HasSprinted(CharacterStats unit)
        {
            if (unit == null) return false;
            return _sprintedThisTurn.Contains(unit);
        }

        /// <summary>
        /// Détermine si l'unité a le droit de tirer à distance ce tour.
        /// RD-038 : "Sprint bloque tir ce tour".
        /// </summary>
        public static bool CanFireRanged(CharacterStats unit)
        {
            if (unit == null) return true;
            if (CoreRulesConfig.Instance != null && !CoreRulesConfig.Instance.SprintBlocksRanged)
                return true;
            return !HasSprinted(unit);
        }

        /// <summary>
        /// Vrai si l'unité bénéficie du bonus de charge (+2 dégâts d'impact au contact).
        /// </summary>
        public static bool HasChargeBonus(CharacterStats unit)
        {
            if (unit == null || !unit.IsAlive) return false;
            return _chargeBonus.Contains(unit);
        }

        /// <summary>
        /// Consomme le bonus de charge lors d'une attaque de mêlée/contact.
        /// Retourne vrai si le bonus était actif et a été appliqué (+2 dégâts).
        /// Le malus défensif, lui, persiste jusqu'au prochain tour.
        /// </summary>
        public static bool ConsumeChargeBonus(CharacterStats unit)
        {
            if (unit == null) return false;
            if (_chargeBonus.Contains(unit))
            {
                _chargeBonus.Remove(unit);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Vrai si l'unité subit le malus défensif de charge (-1 en défense).
        /// RD-038 : Persiste jusqu'au début de son prochain tour personnel.
        /// </summary>
        public static bool HasDefensePenalty(CharacterStats unit)
        {
            if (unit == null || !unit.IsAlive) return false;
            return _defensePenalty.Contains(unit);
        }

        /// <summary>
        /// Renvoie la valeur numérique du modificateur de défense (ex: -1 si malus actif, 0 sinon).
        /// </summary>
        public static int GetDefenseModifier(CharacterStats unit)
        {
            if (!HasDefensePenalty(unit)) return 0;
            return CoreRulesConfig.Instance != null ? CoreRulesConfig.Instance.ChargeDefensePenalty : -1;
        }

        /// <summary>
        /// Nombre total de cases parcourues par l'unité pendant son tour en cours.
        /// </summary>
        public static int GetTilesMovedThisTurn(CharacterStats unit)
        {
            if (unit == null) return 0;
            return _tilesMovedThisTurn.GetValueOrDefault(unit, 0);
        }

        /// <summary>
        /// À appeler au début du tour personnel de l'unité (TurnManager / ResetTurn) :
        /// - Le malus défensif de charge expire ("jusqu'au prochain tour").
        /// - L'interdiction de tir (sprint) est levée pour le nouveau tour.
        /// - Le bonus offensif non consommé expire.
        /// - Le compteur de cases franchies est réinitialisé.
        /// </summary>
        public static void OnUnitTurnStart(CharacterStats unit)
        {
            if (unit == null) return;
            _sprintedThisTurn.Remove(unit);
            _chargeBonus.Remove(unit);
            _defensePenalty.Remove(unit);
            _tilesMovedThisTurn.Remove(unit);
        }

        /// <summary>
        /// Annule l'état pour une unité spécifique (mort, sortie de combat).
        /// </summary>
        public static void Cancel(CharacterStats unit)
        {
            if (unit == null) return;
            _sprintedThisTurn.Remove(unit);
            _chargeBonus.Remove(unit);
            _defensePenalty.Remove(unit);
            _tilesMovedThisTurn.Remove(unit);
        }

        /// <summary>
        /// Purge tous les états de charge et sprint (nouveau combat, reset d'arène, tests).
        /// </summary>
        public static void ClearAll()
        {
            _sprintedThisTurn.Clear();
            _chargeBonus.Clear();
            _defensePenalty.Clear();
            _tilesMovedThisTurn.Clear();
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics.Units;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Objectives;
using Killtime.Core.Character;
using Killtime.Core.Combat;
using Killtime.Core.Dice;
using Killtime.Core.Rules;

namespace Killtime.Tactics.TurnSystem
{
    public partial class TurnManager : MonoBehaviour
    {
        // =====================================================================
        // RD-048 : RETARDER / TENIR / INTERROMPRE (Livre V — Tours).
        // - Retarder : l'unité active repousse son tour plus tard dans le round
        //   (initiative -pénalité, 1/round), réinsérée après celles qui la
        //   devancent encore. Passe la main à l'unité suivante.
        // - Tenir : l'unité active passe son tour en conservant ses PA et se
        //   déclare en attente (0 PA). Elle pourra intervenir juste après
        //   l'unité active du moment via TryResumeHeldUnit, ou rejouer
        //   normalement au round suivant (reliquat expiré).
        // =====================================================================

        /// <summary>
        /// Retarde le tour de l'unité active (RD-048) : initiative réduite de
        /// la pénalité configurée (-2), réinsertion plus tard dans le round en
        /// cours, puis passage à l'unité suivante. Faux si hors combat, sans
        /// unité active, déjà retardé ce round ou déjà dernière.
        /// </summary>
        public bool TryDelayActiveUnit(out string message)
        {
            message = "";
            if (IsInExploration) { message = "⚠️ Retarder impossible en exploration."; return false; }
            if (IsCombatOver) { message = "⚠️ Combat terminé : retarder impossible."; return false; }
            var unit = ActiveUnit;
            if (unit == null || unit.Stats == null || !_allUnits.Contains(unit))
            { message = "⚠️ Aucune unité active à retarder."; return false; }

            int penalty = CoreRulesConfig.Instance != null ? CoreRulesConfig.Instance.DelayInitiativePenalty : 2;
            int oldTotal = GetInitiativeTotal(unit);
            int oldIdx = _allUnits.IndexOf(unit);
            bool hasLaterAlive = false;
            for (int k = oldIdx + 1; k < _allUnits.Count; k++)
            {
                var o = _allUnits[k];
                if (o != null && o.Stats != null && o.Stats.IsAlive) { hasLaterAlive = true; break; }
            }
            if (!hasLaterAlive)
            {
                message = $"⚠️ {unit.Stats.Name} est déjà dernier à jouer : retarder impossible.";
                return false;
            }
            if (!DelayedTurnState.TryDelay(unit.Stats, penalty, out int newTotal, out string error))
            {
                message = $"⚠️ {error}";
                return false;
            }

            _allUnits.RemoveAt(oldIdx);

            // Cherche la première position restante où l'unité retardée devance
            // encore (initiative, puis RAP/AGI/INT, puis nom — même tie-break
            // que le jet général). À égalité stricte, l'occupant garde sa place
            // (le retardé passe après les égalités déjà installées).
            int insertIdx = _allUnits.Count;
            for (int j = oldIdx; j < _allUnits.Count; j++)
            {
                var other = _allUnits[j];
                if (other == null || other.Stats == null) continue;
                if (DelayOutranks(unit, other)) { insertIdx = j; break; }
            }
            // Garantie de mouvement : retarder recule d'au moins un cran même
            // si le nouveau total devance encore le suivant immédiat.
            if (insertIdx == oldIdx && oldIdx < _allUnits.Count) insertIdx = oldIdx + 1;
            if (insertIdx > _allUnits.Count) insertIdx = _allUnits.Count;
            _allUnits.Insert(insertIdx, unit);

            // La main passe à l'unité suivante (celle qui occupait le cran).
            _activeUnitIndex = oldIdx;
            while (_activeUnitIndex < _allUnits.Count
                && (_allUnits[_activeUnitIndex] == null
                    || _allUnits[_activeUnitIndex].Stats == null
                    || !_allUnits[_activeUnitIndex].Stats.IsAlive))
            {
                _activeUnitIndex++;
            }

            string oldStr = oldTotal == int.MinValue ? "?" : oldTotal.ToString();
            message = $"⏳ <b>{unit.Stats.Name}</b> retarde son tour (initiative {oldStr} → {newTotal}, -{penalty}) : réinséré en position {insertIdx + 1}/{_allUnits.Count}.";

            if (_activeUnitIndex >= _allUnits.Count)
            {
                if (CheckCombatOver()) return true;
                CurrentRound++;
                StartNewRound();
            }
            else
            {
                StartUnitTurn();
            }
            BroadcastRoundStartIfGM();
            return true;
        }

        /// <summary>
        /// Compare deux unités pour la réinsertion d'un retard : vrai si
        /// <paramref name="candidate"/> doit passer AVANT <paramref name="other"/>
        /// (initiative supérieure, puis RAP, AGI, INT, puis nom — même ordre que
        /// le jet général, sans le passe-droit Titan qui reste figé au round 1).
        /// </summary>
        private bool DelayOutranks(TacticalUnit candidate, TacticalUnit other)
        {
            int initC = GetInitiativeTotal(candidate);
            int initO = GetInitiativeTotal(other);
            if (initC != initO) return initC > initO;
            int rapC = (candidate != null && candidate.Stats != null) ? candidate.Stats.Attributes.Rapidite : 0;
            int rapO = (other != null && other.Stats != null) ? other.Stats.Attributes.Rapidite : 0;
            if (rapC != rapO) return rapC > rapO;
            int agiC = (candidate != null && candidate.Stats != null) ? candidate.Stats.Attributes.Agilite : 0;
            int agiO = (other != null && other.Stats != null) ? other.Stats.Attributes.Agilite : 0;
            if (agiC != agiO) return agiC > agiO;
            int intC = (candidate != null && candidate.Stats != null) ? candidate.Stats.Attributes.Intelligence : 0;
            int intO = (other != null && other.Stats != null) ? other.Stats.Attributes.Intelligence : 0;
            if (intC != intO) return intC > intO;
            string nameC = candidate != null && candidate.Stats != null ? candidate.Stats.Name ?? "" : "";
            string nameO = other != null && other.Stats != null ? other.Stats.Name ?? "" : "";
            return string.Compare(nameC, nameO, StringComparison.Ordinal) < 0;
        }

        /// <summary>
        /// Tient l'action de l'unité active (RD-048) : passe son tour en
        /// conservant ses PA restants et se déclare en attente d'interruption.
        /// </summary>
        public bool TryHoldActiveUnit(out string message)
        {
            message = "";
            if (IsInExploration) { message = "⚠️ Tenir impossible en exploration."; return false; }
            if (IsCombatOver) { message = "⚠️ Combat terminé : tenir impossible."; return false; }
            var unit = ActiveUnit;
            if (unit == null || unit.Stats == null || !_allUnits.Contains(unit))
            { message = "⚠️ Aucune unité active à mettre en attente."; return false; }

            int cost = CoreRulesConfig.Instance != null ? CoreRulesConfig.Instance.HoldAPCost : 0;
            if (!DelayedTurnState.TryHold(unit.Stats, cost, out string error))
            {
                message = $"⚠️ {error}";
                return false;
            }

            message = $"⏸️ <b>{unit.Stats.Name}</b> tient son action (en attente, {unit.Stats.CurrentActionPoints} PA conservés) : interviendra via interruption ou au prochain tour.";
            EndCurrentTurn();
            return true;
        }

        /// <summary>
        /// Fait intervenir une unité en attente juste après l'unité active du
        /// moment (RD-048, interruption). L'attente est consommée et l'unité
        /// est réinsérée en position suivante avec ses PA conservés (sans
        /// recharge : elle rejoue quand son nouveau cran arrive). Faux si
        /// l'unité ne tenait pas son action ou est neutralisée.
        /// </summary>
        public bool TryResumeHeldUnit(TacticalUnit unit, out string message)
        {
            message = "";
            if (IsInExploration) { message = "⚠️ Interruption impossible en exploration."; return false; }
            if (IsCombatOver) { message = "⚠️ Combat terminé : interruption impossible."; return false; }
            if (unit == null || unit.Stats == null || !_allUnits.Contains(unit))
            { message = "⚠️ Unité inconnue : interruption impossible."; return false; }
            if (unit == ActiveUnit)
            { message = $"⚠️ {unit.Stats.Name} est déjà l'unité active : interruption inutile."; return false; }
            if (!DelayedTurnState.IsHolding(unit.Stats))
            { message = $"⚠️ {unit.Stats.Name} ne tient aucune action (pas en attente)."; return false; }
            if (DelayedTurnState.IsCancelledByStatus(unit.Stats))
            { message = $"⚠️ {unit.Stats.Name} est neutralisé : interruption impossible."; return false; }

            DelayedTurnState.ConsumeHoldToAct(unit.Stats);
            _allUnits.Remove(unit);

            int currentIdx = ActiveUnit != null ? _allUnits.IndexOf(ActiveUnit) : -1;
            int insertIdx = currentIdx >= 0 ? currentIdx + 1 : _allUnits.Count;
            if (insertIdx < 0) insertIdx = 0;
            if (insertIdx > _allUnits.Count) insertIdx = _allUnits.Count;
            _allUnits.Insert(insertIdx, unit);
            if (ActiveUnit != null)
                _activeUnitIndex = _allUnits.IndexOf(ActiveUnit);

            message = $"⚡ <b>{unit.Stats.Name}</b> interrompt ({unit.Stats.CurrentActionPoints} PA conservés) : rejouera en position {insertIdx + 1}/{_allUnits.Count}, juste après {(ActiveUnit != null ? ActiveUnit.Stats.Name : "—")}.";
            BroadcastRoundStartIfGM();
            return true;
        }
    }
}

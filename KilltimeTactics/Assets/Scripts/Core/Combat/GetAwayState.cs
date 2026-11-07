using System.Collections.Generic;
using Killtime.Core.Character;

namespace Killtime.Core.Combat
{
    /// <summary>
    /// Dégagement / GetAway (RD-202, Livre V §20.3) :
    /// une action principale (attaque OU parade) immédiatement suivie d'un
    /// déplacement de fuite coûtant +1 PA de surcharge (déplacement normal
    /// payé à part, via MoveAlongPath).
    ///
    /// Le duel résolu ouvre une fenêtre de fuite immédiate pour son auteur
    /// (attaquant toujours, défenseur s'il a activement paré). La fenêtre est
    /// one-shot : consommée par le paiement de la surcharge, expirée au tour
    /// personnel suivant.
    ///
    /// DISTINCT du Décrochage préventif RD-031 (1 PA, posture armée AVANT le
    /// déplacement, prochain déplacement sans réaction) : le Dégagement est un
    /// surcoût appliqué APRÈS une attaque/parade résolue, sans exemption —
    /// la fuite peut toujours provoquer frappes, poursuites et blocages.
    ///
    /// Keyé sur CharacterStats (couche Core, joueur + IA, sans Unity).
    /// </summary>
    public static class GetAwayState
    {
        private static readonly HashSet<CharacterStats> _window = new();

        /// <summary>Vrai si l'unité vient de résoudre une attaque/parade et peut
        /// payer la surcharge de fuite immédiate (fenêtre one-shot valide).</summary>
        public static bool HasWindow(CharacterStats unit)
        {
            if (unit == null || !unit.IsAlive) return false;
            return _window.Contains(unit);
        }

        /// <summary>
        /// Ouvre (ou confirme) la fenêtre de fuite après une attaque ou une
        /// parade résolue par l'unité. Appelé par la résolution des duels.
        /// </summary>
        public static void MarkPostAction(CharacterStats unit)
        {
            if (unit == null || !unit.IsAlive) return;
            _window.Add(unit);
        }

        /// <summary>
        /// déplacement de fuite coûtant +1 PA de surcharge, puis déplacement
        /// normal (coût du trajet en sus, via MoveAlongPath). Refuse sans
        /// fenêtre ouverte (aucune attaque/parade résolue), sans PA ou
        /// neutralisé. En cas d'échec (PA insuffisants) la fenêtre est
        /// CONSERVÉE pour un nouvel essai ; en cas de succès elle est consommée.
        /// </summary>
        public static bool TryGetAwayMove(CharacterStats unit, int extraAPCost, out string error)
        {
            error = null;
            if (unit == null) { error = "Dégagement impossible : sans combattant."; return false; }
            if (!unit.IsAlive) { error = $"{unit.Name} est hors de combat : dégagement impossible."; return false; }
            if (PreparationState.IsCancelledByStatus(unit))
            {
                error = $"{unit.Name} est neutralisé (étourdi/paralysé) : dégagement impossible.";
                return false;
            }
            if (!_window.Contains(unit))
            {
                error = $"{unit.Name} doit d'abord résoudre une attaque ou une parade avant de se dégager (+1 PA de fuite).";
                return false;
            }
            if (!unit.ConsumeActionPoints(extraAPCost))
            {
                error = $"{unit.Name} n'a pas assez de PA ({unit.CurrentActionPoints}/{extraAPCost}) pour se dégager après sa frappe !";
                return false;
            }
            _window.Remove(unit);
            return true;
        }

        /// <summary>À appeler au début du tour personnel : la fuite doit suivre
        /// IMMÉDIATEMENT l'action — la fenêtre non consommée expire. Les
        /// fenêtres des unités mortes sont purgées partout.</summary>
        public static void OnUnitTurnStart(CharacterStats unit)
        {
            if (unit != null) _window.Remove(unit);
            if (_window.Count == 0) return;
            CharacterStats[] keys = new CharacterStats[_window.Count];
            _window.CopyTo(keys, 0);
            for (int i = 0; i < keys.Length; i++)
            {
                var k = keys[i];
                if (k == null || !k.IsAlive)
                    _window.Remove(k);
            }
        }

        /// <summary>Retire une unité qui quitte la carte (fenêtre perdue).</summary>
        public static void RemoveUnit(CharacterStats unit)
        {
            if (unit == null) return;
            _window.Remove(unit);
        }

        public static void ClearAll() => _window.Clear();
    }
}

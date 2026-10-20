using Killtime.Tactics.Grid;

namespace Killtime.Core.Combat
{
    /// <summary>
    /// RD-204 (Livre V, désarmement) : l'arme arrachée tombe à
    /// <see cref="Killtime.Core.Rules.CoreRulesConfig.DisarmDropDistance"/>
    /// cases du porteur (2 par défaut), dans le prolongement de l'axe
    /// attaquant → défenseur — jamais sur la case du porteur.
    /// Pur C# (aucune dépendance Unity) : testable en EditMode.
    /// L'arène résout la coordonnée en position monde (repli case du
    /// porteur si la grille est absente).
    /// </summary>
    public static class DisarmDropRules
    {
        /// <summary>
        /// Calcule la case de chute : défenseur + distance × pas unitaire le
        /// long de l'axe attaquant → défenseur. Redevient le défenseur lui-même
        /// si les deux sont sur la même case (pas nul → direction +Q par défaut).
        /// </summary>
        public static HexCoordinates ComputeDropCoords(
            HexCoordinates attackerCoords,
            HexCoordinates defenderCoords,
            int distance)
        {
            if (distance <= 0) return defenderCoords;

            int dq = defenderCoords.Q - attackerCoords.Q;
            int dr = defenderCoords.R - attackerCoords.R;

            if (dq == 0 && dr == 0)
            {
                var dir = HexCoordinates.Directions[0];
                return new HexCoordinates(
                    defenderCoords.Q + dir.Q * distance,
                    defenderCoords.R + dir.R * distance);
            }

            int dist = attackerCoords.DistanceTo(defenderCoords);
            if (dist <= 0) dist = 1;

            // Pas unitaire arrondi le long de l'axe (2 cases pile en ligne droite,
            // approximation honnête en diagonale axiale).
            int stepQ = (int)System.Math.Round((double)dq * distance / dist);
            int stepR = (int)System.Math.Round((double)dr * distance / dist);
            if (stepQ == 0 && stepR == 0)
            {
                stepQ = System.Math.Sign(dq);
                stepR = System.Math.Sign(dr);
            }

            return new HexCoordinates(defenderCoords.Q + stepQ, defenderCoords.R + stepR);
        }
    }
}

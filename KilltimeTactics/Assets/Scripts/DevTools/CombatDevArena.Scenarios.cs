using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Killtime.Tactics.Arena;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Core.Combat;

namespace Killtime.Tactics
{
    // RD-198 : scénarios terrain 1-clic + carnet d'écarts (Livre VI §24-26).
    // Dev-only, 0 règle modifiée : positionne via TeleportTo, arme le guet via
    // TryEnterOverwatch, pose la posture via OpportunityState.SetStance, log +
    // snapshot chrono. Le MJ déclenche ensuite au radial / en jeu.
    public partial class CombatDevArena : MonoBehaviour, IArenaHost
    {
        // --- Helpers purs (testables EditMode sans scène) ---

        public static string ScenarioPostureLabel(int index)
        {
            return index switch
            {
                1 => "Poursuite",
                2 => "Blocage",
                3 => "Balayage",
                _ => "Frappe"
            };
        }

        public static OpportunityReactionType ScenarioPostureFromIndex(int index)
        {
            return index switch
            {
                1 => OpportunityReactionType.Follow,
                2 => OpportunityReactionType.Block,
                3 => OpportunityReactionType.Trip,
                _ => OpportunityReactionType.Strike
            };
        }

        public static bool IsChargeSetupDistance(int dist) => dist >= 3;

        public static string RadialChecklistText()
        {
            return "Radial orbital : forage catégorie→items (clic), molette pastille PA/PE, "
                + "sablier fin de tour (masqué hors tour / 0 PA), snap ease-out 250ms, "
                + "Maj+F9 tuning. Rien à coder (RD-184).";
        }

        public static string DemoChecklistText()
        {
            return "Démo 60s : 6 beats DemoDirectorAlpha3 (cadrage/move/tir+couvert/"
                + "duel aveugle/ramassage/Sillage), watermark PLACEHOLDER, télémétrie "
                + "PA/PE/PV, LOS forcée puis restaurée. Capture OBS : RD-029.";
        }

        /// <summary>
        /// Carnet d'écarts Livres exportable : gabarit + logs toolbar (même source
        /// que le feed CombatHUD). Le MJ coche en jeu, copie via le presse-papiers.
        /// </summary>
        public static string BuildGapReport(IEnumerable<string> logs)
        {
            var sb = new StringBuilder();
            sb.AppendLine("CARNET D'ÉCARTS TERRAIN (RD-198) — code vs Livres");
            sb.AppendLine("Cocher en jeu : [OK] conforme / [ÉCART] + fix à créer.");
            sb.AppendLine("[ ] RD-171 Réactions §24 : guet 2 PA / 1 tir / purge, opportunité 4 postures + décrochage, garde 1 PA.");
            sb.AppendLine("[ ] RD-174 Positionnement : surplomb +1 / contrebas -1, charge 3+ cases +2 dégâts, LOS/couverts.");
            sb.AppendLine("[ ] RD-175 CaC : lutte 2 PA + Immobilisé, étranglement Asphyxie, traînée 2 PA, bousculade.");
            sb.AppendLine("[ ] RD-184 Radial : forage, molette, sablier, snap, Maj+F9.");
            sb.AppendLine("[ ] RD-185 Alignement : inventaire écarts code vs Livres.");
            sb.AppendLine("[ ] RD-186 Démo 60s re-validée + capture OBS.");
            sb.AppendLine("--- LOGS ARÈNE ---");
            if (logs != null)
            {
                int n = 0;
                foreach (var l in logs)
                {
                    if (string.IsNullOrEmpty(l)) continue;
                    sb.AppendLine(l);
                    if (++n >= 50) break;
                }
                if (n == 0) sb.AppendLine("(aucun log — jouez un scénario puis ré-exportez)");
            }
            return sb.ToString();
        }

        // --- Scénarios 1-clic (dev-only) ---

        /// <summary>
        /// RD-171 : guetteur (unité joueur) + traverseur (cible) à ~4 cases, guet armé.
        /// Le MJ déplace ensuite la cible dans le rayon (tour adverse / FullAuto).
        /// Attendu Livre VI §24 : 1 tir/round, purge au tour, couvert total bloque.
        /// </summary>
        public bool SetupScenarioGuet()
        {
            var watcher = PlayerUnit;
            var mover = CurrentTarget;
            if (watcher == null || watcher.Stats == null) { Log("⛔ Scénario guet : aucune unité joueur."); return false; }
            if (mover == null || mover.Stats == null || !mover.Stats.IsAlive) { Log("⛔ Scénario guet : aucune cible vivante (sélectionnez-la)."); return false; }
            if (_grid == null) { Log("⛔ Scénario guet : grille introuvable."); return false; }
            RefillAPAll();
            var spot = FindSpotAtDistance(watcher.CurrentCoords, 4);
            if (spot.HasValue && !spot.Value.Equals(mover.CurrentCoords))
                mover.TeleportTo(spot.Value, _grid);
            int dist = watcher.CurrentCoords.DistanceTo(mover.CurrentCoords);
            if (!TryEnterOverwatch(watcher, out string msg))
            { Log($"⛔ Guet refusé : {msg}"); return false; }
            SelectTarget(mover);
            Log($"🎯 <b>Scénario guet prêt</b> : {watcher.Stats.Name} en guet, {mover.Stats.Name} à {dist} cases. Déplacez la cible dans le rayon (tour adverse) : 1 tir/round attendu.");
            RecordChronoSnapshot("Scénario guet 1-clic (RD-198)");
            return true;
        }

        /// <summary>
        /// RD-171 : acteur + voisin au contact, posture d'opportunité posée.
        /// Le MJ fait quitter le contact au voisin : réaction selon la posture.
        /// </summary>
        public bool SetupScenarioOpportunite(OpportunityReactionType stance)
        {
            var actor = PlayerUnit;
            var foe = CurrentTarget;
            if (actor == null || actor.Stats == null) { Log("⛔ Scénario opportunité : aucune unité joueur."); return false; }
            if (foe == null || foe.Stats == null || !foe.Stats.IsAlive) { Log("⛔ Scénario opportunité : aucune cible vivante."); return false; }
            if (_grid == null) { Log("⛔ Scénario opportunité : grille introuvable."); return false; }
            RefillAPAll();
            var spot = FindAdjacentSpot(actor.CurrentCoords);
            if (spot.HasValue && !spot.Value.Equals(foe.CurrentCoords))
                foe.TeleportTo(spot.Value, _grid);
            OpportunityState.SetStance(actor.Stats, stance);
            SelectTarget(foe);
            int dist = actor.CurrentCoords.DistanceTo(foe.CurrentCoords);
            string contact = dist <= 1 ? "au contact" : $"à {dist} cases — rapprochez au contact";
            Log($"🎯 <b>Scénario opportunité prêt</b> : {actor.Stats.Name} en {OpportunityState.StanceLabel(stance)}, {foe.Stats.Name} {contact}. Faites quitter le contact : réaction 1/round attendue.");
            RecordChronoSnapshot($"Scénario opportunité 1-clic {stance} (RD-198)");
            return true;
        }

        /// <summary>
        /// RD-174 : acteur à 3-4 cases de la cible (charge éligible), surplomb noté.
        /// Le MJ exécute ensuite ⚡ Charge au radial. Attendu : +2 dégâts d'impact.
        /// </summary>
        public bool SetupScenarioCharge()
        {
            var actor = PlayerUnit;
            var foe = CurrentTarget;
            if (actor == null || actor.Stats == null) { Log("⛔ Scénario charge : aucune unité joueur."); return false; }
            if (foe == null || foe.Stats == null || !foe.Stats.IsAlive) { Log("⛔ Scénario charge : aucune cible vivante."); return false; }
            if (_grid == null) { Log("⛔ Scénario charge : grille introuvable."); return false; }
            RefillAPAll();
            var spot = FindSpotAtDistance(actor.CurrentCoords, 3);
            if (spot.HasValue && !spot.Value.Equals(foe.CurrentCoords))
                foe.TeleportTo(spot.Value, _grid);
            SelectTarget(foe);
            int dist = actor.CurrentCoords.DistanceTo(foe.CurrentCoords);
            string verdict = IsChargeSetupDistance(dist) ? "charge éligible (3+ cases)" : "trop près — éloignez la cible à 3+ cases";
            Log($"🎯 <b>Scénario charge prêt</b> : {actor.Stats.Name} → {foe.Stats.Name} à {dist} cases, {verdict}. Exécutez ⚡ Charge au radial (+2 dégâts). Surplomb +1 palier si relief.");
            RecordChronoSnapshot("Scénario charge 1-clic (RD-198)");
            return IsChargeSetupDistance(dist);
        }

        private HexCoordinates? FindSpotAtDistance(HexCoordinates center, int dist)
        {
            if (_grid == null) return null;
            for (int dq = -dist - 2; dq <= dist + 2; dq++)
            {
                for (int dr = -dist - 2; dr <= dist + 2; dr++)
                {
                    var cand = new HexCoordinates(center.Q + dq, center.R + dr);
                    int d = cand.DistanceTo(center);
                    if (d < dist - 1 || d > dist + 1) continue;
                    if (!_grid.TryFindNearestFreeCell(cand, out var free, 2)) continue;
                    int fd = free.DistanceTo(center);
                    if (fd < dist - 1 || fd > dist + 1) continue;
                    return free;
                }
            }
            return null;
        }

        private HexCoordinates? FindAdjacentSpot(HexCoordinates center)
        {
            if (_grid == null) return null;
            for (int dir = 0; dir < 6; dir++)
            {
                var cand = center.GetNeighbor(dir);
                if (_grid.TryFindNearestFreeCell(cand, out var free, 1) && free.DistanceTo(center) <= 1)
                    return free;
            }
            return null;
        }
    }
}

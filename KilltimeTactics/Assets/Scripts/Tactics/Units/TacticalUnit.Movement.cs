using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Audio;
using Killtime.Core.Character;
using Killtime.Core.Combat;
using Killtime.Tactics.Grid;

namespace Killtime.Tactics.Units
{
    public partial class TacticalUnit : MonoBehaviour
    {
        public void InitializePosition(HexCoordinates startCoords, TacticalHexGrid grid)
        {
            if (grid != null && !grid.IsFootprintFree(startCoords, _footprintType, this))
            {
                Debug.LogWarning($"[TacticalUnit] Placement refusé pour '{_unitName}' en {startCoords} : empreinte encombrée.");
                return;
            }

            _grid = grid;
            if (_hasPosition && _grid != null && !CurrentCoords.Equals(startCoords))
            {
                var oldList = OccupiedCoords;
                for (int i = 0; i < oldList.Count; i++)
                {
                    var prevNode = _grid.GetNode(oldList[i]);
                    if (prevNode != null && prevNode.IsOccupied && !IsAnyOtherUnitAt(oldList[i]))
                    {
                        prevNode.IsOccupied = false;
                    }
                }
            }

            CurrentCoords = startCoords;
            var node = grid != null ? grid.GetNode(startCoords) : null;
            float yPos = node != null ? node.WorldPosition.y : 0.0f;
            transform.position = startCoords.ToWorldPosition(grid != null ? grid.HexRadius : 1.0f, yPos);

            if (_grid != null)
            {
                var newList = OccupiedCoords;
                for (int i = 0; i < newList.Count; i++)
                {
                    var n = _grid.GetNode(newList[i]);
                    if (n != null) n.IsOccupied = true;
                }
            }
            _hasPosition = true;
        }

        /// <summary>
        /// Positionne directement l'unité sans validation complexe (utile pour les tests et placements scriptés).
        /// </summary>
        public void SetPositionDirect(HexCoordinates coords, TacticalHexGrid grid = null)
        {
            CurrentCoords = coords;
            _hasPosition = true;
            if (grid != null)
            {
                _grid = grid;
                var node = grid.GetNode(coords);
                float yPos = node != null ? node.WorldPosition.y : 0.0f;
                transform.position = coords.ToWorldPosition(grid.HexRadius, yPos);
            }
            else
            {
                transform.position = coords.ToWorldPosition(1.0f, 0.0f);
            }
        }

        /// <summary>
        /// Vrai si un autre avatar occupe ces coordonnées.
        /// La présence physique d'une unité vivante fait foi et répare le nœud.
        /// </summary>
        private bool IsOccupiedByOther(HexCoordinates coords, TacticalHexGrid grid)
        {
            if (grid == null) return false;
            var node = grid.GetNode(coords);
            if (node == null || !node.IsWalkable) return true;

            if (_hasPosition && Occupies(coords)) return false;

            if (IsAnyOtherUnitAt(coords))
            {
                node.IsOccupied = true;
                return true;
            }

            return node.IsOccupied;
        }

        private bool IsAnyOtherUnitAt(HexCoordinates coords)
        {
            var units = TacticalUnitRegistry.GetUnitsAt(coords);
            foreach (var u in units)
            {
                if (u == null || u == this) continue;
                if (u.Stats != null && !u.Stats.IsAlive) continue;
                return true;
            }
            return false;
        }

        public bool TeleportTo(HexCoordinates coords, TacticalHexGrid grid)
        {
            grid ??= _grid;
            if (grid == null) return false;

            if (_hasPosition && CurrentCoords.Equals(coords))
            {
                _grid = grid;
                return true;
            }

            if (!grid.IsFootprintFree(coords, _footprintType, this))
            {
                Debug.LogWarning($"[TacticalUnit] Téléportation refusée pour '{_unitName}' vers {coords} : empreinte occupée.");
                return false;
            }

            _grid = grid;
            if (_hasPosition)
            {
                var oldList = OccupiedCoords;
                for (int i = 0; i < oldList.Count; i++)
                {
                    var oldNode = grid.GetNode(oldList[i]);
                    if (oldNode != null && !IsAnyOtherUnitAt(oldList[i]))
                    {
                        oldNode.IsOccupied = false;
                    }
                }
            }

            InitializePosition(coords, grid);
            bool success = _hasPosition && CurrentCoords.Equals(coords);
            if (success) OnAnyUnitTeleported?.Invoke(this, coords);
            return success;
        }

        /// <summary>
        /// Déplace l'unité case par case le long d'un chemin d'hexagones en consommant ses PA.
        /// Par défaut refuse tout mouvement vers / à travers une case occupée (1 case = 1 avatar).
        /// allowPassThrough (exploration) : traverser les cases occupées pour passer
        /// de l'autre côté, sans jamais s'y arrêter (destination toujours contrôlée).
        /// provokesOpportunity (RD-031) : si vrai (défaut), quitter le contact (≤ portée)
        /// expose aux réactions d'opportunité (frappe / poursuite / blocage / balayage),
        /// sauf spé esquive (Roulade de Décrochage, Acrobatie d'Évitement) ou Décrochage
        /// payé. Les déplacements de réaction (poursuite) passent faux (pas de ping-pong).
        /// Un pas annulé par Blocage rembourse les PA non consommés (intégral si immobile).
        /// </summary>
        public IEnumerator MoveAlongPath(List<HexCoordinates> path, TacticalHexGrid grid, int apCost, bool allowPassThrough = false, bool provokesOpportunity = true)
        {
            if (path == null || path.Count <= 1) yield break;
            if (grid == null) yield break;

            // Cinématique en cours de pilotage : refuse tout nouveau déplacement
            // (avant consommation des PA et recalage).
            if (CinematicDriveActive) yield break;

            _grid = grid;

            // En cas de léger décalage réseau ou rejeu, recalage immédiat sur le départ du chemin
            if (!path[0].Equals(CurrentCoords))
            {
                TeleportTo(path[0], grid);
            }

            // Validation préalable : praticable partout ; occupée = refus sauf
            // faufile (traversée autorisée, destination toujours contrôlée).
            for (int v = 1; v < path.Count; v++)
            {
                bool isDest = v == path.Count - 1;
                if ((!allowPassThrough || isDest) && IsOccupiedByOther(path[v], grid))
                {
                    Debug.LogWarning($"[TacticalUnit] Déplacement refusé pour '{_unitName}' : {path[v]} occupée.");
                    yield break;
                }
                var vNode = grid.GetNode(path[v]);
                if (vNode == null || !vNode.IsWalkable)
                {
                    Debug.LogWarning($"[TacticalUnit] Déplacement refusé pour '{_unitName}' : {path[v]} impraticable.");
                    yield break;
                }
            }

            var destNode = grid.GetNode(path[^1]);
            if (destNode != null)
            {
                destNode.IsOccupied = true;
            }

            IsMoving = true;
            Stats.ConsumeActionPoints(apCost);
            Stats.RegisterMove();
            OnAnyUnitMoved?.Invoke(this, path, apCost);

            // RD-031 : ce déplacement expose-t-il aux réactions d'opportunité ?
            // Faux si spé esquive (Roulade de Décrochage, Acrobatie d'Évitement)
            // ou Décrochage payé (consommé ici, valable pour tout le trajet).
            bool moveProvokes = provokesOpportunity
                && (Stats == null || OpportunityState.TryBeginProvokingMove(Stats));

            HexCoordinates previous = path[0];

            for (int i = 1; i < path.Count; i++)
            {
                // Prise de contrôle cinématique : interrompt le déplacement en cours
                // (la cinématique re-synchronise occupancy via TeleportTo).
                if (CinematicDriveActive)
                {
                    AbortMoveForCinematic(path, grid);
                    yield break;
                }

                var nextCoords = path[i];

                // RD-031 Blocage : un voisin en posture Blocage peut annuler le pas
                // (duel opposé résolu par l'arène). Les PA non consommés sont
                // remboursés — intégralement si l'unité n'a pas bougé.
                if (moveProvokes && IsStepBlockedByReaction(previous, nextCoords))
                {
                    Debug.LogWarning($"[TacticalUnit] Déplacement bloqué pour '{_unitName}' : réaction d'opportunité (pas {previous} -> {nextCoords} annulé).");
                    RefundUnspentMovePA(apCost, path.Count - 1, i - 1);
                    if (Stats != null && i - 1 > 0)
                    {
                        bool charged = ChargeState.ApplyMovement(Stats, i - 1);
                        if (charged)
                        {
                            GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("⚡ CHARGE / SPRINT", new Color(1f, 0.85f, 0.2f));
                        }
                    }
                    var blockedFallback = grid.GetNode(CurrentCoords);
                    if (blockedFallback != null) blockedFallback.IsOccupied = true;
                    if (destNode != null && !destNode.Coordinates.Equals(CurrentCoords)) destNode.IsOccupied = false;
                    IsMoving = false;
                    yield break;
                }

                // La destination est exclue du test drapeau (réservée en début
                // de trajet) mais pas du test physique : si un autre avatar y
                // est arrivé entre-temps, on s'arrête AVANT d'entrer (pas d'empilement).
                // En faufile on traverse les étapes occupées, jamais la destination.
                bool isDestStep = nextCoords.Equals(path[^1]);
                bool stepBlocked = isDestStep
                    ? IsAnyOtherUnitAt(nextCoords)
                    : (!allowPassThrough && IsOccupiedByOther(nextCoords, grid));
                if (stepBlocked)
                {
                    Debug.LogWarning($"[TacticalUnit] Déplacement interrompu pour '{_unitName}' : {nextCoords} devenue occupée.");
                    var fallbackNode = grid.GetNode(CurrentCoords);
                    if (fallbackNode != null) fallbackNode.IsOccupied = true;
                    if (destNode != null && !destNode.Coordinates.Equals(CurrentCoords)) destNode.IsOccupied = false;
                    IsMoving = false;
                    yield break;
                }

                var nextNode = grid.GetNode(nextCoords);
                if (nextNode == null || !nextNode.IsWalkable)
                {
                    var fallbackNode2 = grid.GetNode(CurrentCoords);
                    if (fallbackNode2 != null) fallbackNode2.IsOccupied = true;
                    IsMoving = false;
                    yield break;
                }

                float yPos = nextNode.WorldPosition.y;
                var targetPos = nextCoords.ToWorldPosition(grid.HexRadius, yPos);

                // Rotation orientée vers la destination
                Vector3 lookDir = (targetPos - transform.position).normalized;
                if (lookDir != Vector3.zero)
                {
                    transform.rotation = Quaternion.LookRotation(lookDir);
                }

                // Déplacement progressif
                while (Vector3.Distance(transform.position, targetPos) > 0.05f)
                {
                    // Prise de contrôle cinématique même au milieu d'un pas.
                    if (CinematicDriveActive)
                    {
                        AbortMoveForCinematic(path, grid);
                        yield break;
                    }

                    while (Time.timeScale <= 0.0001f)
                    {
                        yield return null;
                    }

                    transform.position = Vector3.MoveTowards(transform.position, targetPos, _moveSpeed * Time.deltaTime);
                    yield return null;
                }

                transform.position = targetPos;
                // L'occupation suit l'unité : libère la précédente, occupe la nouvelle.
                var prevNode = grid.GetNode(previous);
                if (prevNode != null && !IsAnyOtherUnitAt(previous))
                {
                    prevNode.IsOccupied = false;
                }
                CurrentCoords = nextCoords;
                nextNode.IsOccupied = true;
                previous = nextCoords;
                _hasPosition = true;
                // SFX : pas spatialisé à chaque case (cooldown interne anti-spam).
                if (KilltimeAudioManager.Instance != null)
                    KilltimeAudioManager.Instance.PlayAt(SoundId.Move_Footstep, transform.position, 0.8f);

                // RD-031 : notifie la case franchie pour les réactions d'opportunité
                // (frappe / poursuite / balayage si le contact est rompu).
                if (moveProvokes)
                {
                    try { OnAnyUnitStepCompleted?.Invoke(this, path[i - 1], nextCoords, moveProvokes); }
                    catch { /* handler arène optionnel */ }
                }

                // Tué en cours de trajet (frappe d'opportunité) : le reste du
                // chemin est annulé, l'unité reste sur la case atteinte.
                if (Stats != null && !Stats.IsAlive)
                {
                    IsMoving = false;
                    yield break;
                }
            }

            IsMoving = false;
            int totalSteps = path.Count - 1;
            if (Stats != null && totalSteps > 0)
            {
                bool charged = ChargeState.ApplyMovement(Stats, totalSteps);
                if (charged)
                {
                    GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("⚡ CHARGE / SPRINT", new Color(1f, 0.85f, 0.2f));
                }
            }
            try { OnAnyUnitMoveCompleted?.Invoke(this); } catch { /* ignore */ }
        }

        /// <summary>
        /// Interroge le hook de blocage posé par l'arène (RD-031 Blocage).
        /// Toute exception du handler vaut "pas autorisé" (jamais de blocage fantôme).
        /// </summary>
        private bool IsStepBlockedByReaction(HexCoordinates from, HexCoordinates to)
        {
            var check = StepBlockCheck;
            if (check == null) return false;
            try { return check(this, from, to); }
            catch { return false; }
        }

        /// <summary>
        /// Rembourse les PA du trajet non effectué après un Blocage (RD-031) :
        /// intégral si aucun pas accompli, prorata arrondi au supérieur sinon.
        /// Plafonné au maximum (jamais de PA au-delà du plafond).
        /// </summary>
        private void RefundUnspentMovePA(int apCost, int totalSteps, int completedSteps)
        {
            if (Stats == null || apCost <= 0 || totalSteps <= 0) return;
            int refund = apCost;
            if (completedSteps > 0)
            {
                int remaining = totalSteps - completedSteps;
                if (remaining <= 0) return;
                refund = Mathf.CeilToInt((float)apCost * remaining / totalSteps);
            }
            refund = Mathf.Clamp(refund, 0, apCost);
            if (refund > 0)
                Stats.CurrentActionPoints = Mathf.Min(Stats.MaxActionPoints, Stats.CurrentActionPoints + refund);
        }
    }
}

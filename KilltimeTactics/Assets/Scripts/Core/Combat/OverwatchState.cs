using System.Collections.Generic;
using Killtime.Core.Character;

namespace Killtime.Core.Combat
{
    /// <summary>
    /// Guet / Overwatch (RD-030, Livre VI — Réactions) :
    /// action 2 PA qui réserve un tir de réaction contre un ennemi agissant
    /// dans le rayon de l'arme pendant le tour adverse.
    ///
    /// - Posé pendant son propre tour (2 PA débités aussitôt).
    /// - Rayon = portée de l'arme équipée (cône 360° en V1 : pas de facing).
    /// - 1 tir automatique en duel aveugle (attaquant 0 mise, défenseur auto),
    ///   puis le guet est consommé.
    /// - Expire au début du prochain tour personnel du guetteur (utilisé ou non).
    /// - Annulé si Étourdi / Paralysé ( + Sonné / Inconscient / mort : CanDefendActively).
    ///
    /// Keyé sur CharacterStats (couche Core, joueur + IA, sans dépendance Unity).
    /// </summary>
    public static class OverwatchState
    {
        private sealed class Watch
        {
            public CharacterStats Watcher;
            public int Range;
            public int ShotsLeft;
        }

        private static readonly Dictionary<CharacterStats, Watch> _watches = new();

        /// <summary>Vrai si le guet est invalidé par les statuts (RD-030 : étourdi/paralysé).</summary>
        public static bool IsCancelledByStatus(CharacterStats watcher)
        {
            if (watcher == null) return true;
            if (!watcher.IsAlive) return true;
            var fx = watcher.ActiveStatus;
            if (fx.HasFlag(StatusEffect.Etourdi)) return true;
            if (fx.HasFlag(StatusEffect.Paralyse)) return true;
            if (fx.HasFlag(StatusEffect.Sonne)) return true;
            if (fx.HasFlag(StatusEffect.Inconscient)) return true;
            return false;
        }

        /// <summary>
        /// Entre en guet : débite le coût (2 PA config) et enregistre le rayon.
        /// Refuse si déjà en guet, PA insuffisants, sans arme à distance effective,
        /// ou statuts invalidants. Le rayon est figé à la pose.
        /// </summary>
        public static bool TryEnter(CharacterStats watcher, int range, int apCost, out string error, int shots = 1)
        {
            error = null;
            if (watcher == null) { error = "Guet impossible : sans guetteur."; return false; }
            if (!watcher.IsAlive) { error = $"{watcher.Name} est hors de combat : guet impossible."; return false; }
            if (IsCancelledByStatus(watcher))
            {
                error = $"{watcher.Name} est neutralisé (étourdi/paralysé) : guet impossible.";
                return false;
            }
            if (IsWatching(watcher))
            {
                error = $"{watcher.Name} est déjà en guet : tir de réaction réservé.";
                return false;
            }
            if (range < 2)
            {
                error = $"{watcher.Name} n'a pas d'arme à distance (portée {range}) : guet impossible.";
                return false;
            }
            if (!watcher.ConsumeActionPoints(apCost))
            {
                error = $"{watcher.Name} n'a pas assez de PA ({watcher.CurrentActionPoints}/{apCost}) pour se mettre en guet !";
                return false;
            }
            _watches[watcher] = new Watch { Watcher = watcher, Range = range, ShotsLeft = System.Math.Max(1, shots) };
            return true;
        }

        /// <summary>Vrai si le guetteur a un tir de réaction réservé et valide.</summary>
        public static bool IsWatching(CharacterStats watcher)
        {
            if (watcher == null) return false;
            if (!_watches.TryGetValue(watcher, out var w) || w == null) return false;
            if (w.ShotsLeft <= 0) { _watches.Remove(watcher); return false; }
            if (IsCancelledByStatus(watcher)) { _watches.Remove(watcher); return false; }
            return true;
        }

        public static bool TryGetRange(CharacterStats watcher, out int range)
        {
            range = 0;
            if (!IsWatching(watcher)) return false;
            range = _watches[watcher].Range;
            return true;
        }

        /// <summary>Consomme le tir de réaction (1 tir puis fin du guet).</summary>
        public static void ConsumeShot(CharacterStats watcher)
        {
            if (watcher == null) return;
            if (!_watches.TryGetValue(watcher, out var w) || w == null) return;
            w.ShotsLeft--;
            if (w.ShotsLeft <= 0) _watches.Remove(watcher);
        }

        /// <summary>Annule le guet (tir volontaire, annulation, sanction).</summary>
        public static void Cancel(CharacterStats watcher)
        {
            if (watcher == null) return;
            _watches.Remove(watcher);
        }

        /// <summary>À appeler au début du tour personnel : expire le guet du
        /// guetteur qui rejoue (utilisé ou non) + purge les guets invalidés.</summary>
        public static void OnUnitTurnStart(CharacterStats unit)
        {
            if (unit != null) _watches.Remove(unit);
            if (_watches.Count == 0) return;
            CharacterStats[] keys = new CharacterStats[_watches.Count];
            _watches.Keys.CopyTo(keys, 0);
            for (int i = 0; i < keys.Length; i++)
            {
                var k = keys[i];
                if (k == null || !_watches.TryGetValue(k, out var w) || w == null
                    || w.ShotsLeft <= 0 || IsCancelledByStatus(k))
                    _watches.Remove(k);
            }
        }

        public static void ClearAll() => _watches.Clear();
    }
}

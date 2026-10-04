using System.Collections.Generic;
using Killtime.Core.Character;

namespace Killtime.Core.Combat
{
    /// <summary>
    /// Concours d'Athlétisme de poursuite (RD-084, Livre VI §25.1) :
    /// échapper à un ou plusieurs ennemis est un concours d'Athlétisme sous
    /// tension. La proie lance son épreuve, chaque poursuivant lance la sienne.
    /// - Réussite de la proie contre un poursuivant : distance maintenue, le
    ///   poursuivant ne peut pas être approché à moins d'1 case ;
    /// - Faillite de la proie : le poursuivant gagne du terrain ;
    /// - Règle d'arrêt aux 4 faillites : dès que le total cumulé de faillites
    ///   de la proie contre l'ensemble des poursuivants atteint 4, la proie est
    ///   définitivement rattrapée et acculée (arrêt + capture immédiate).
    ///
    /// Keyé sur CharacterStats (couche Core, joueur + IA, sans dépendance Unity).
    /// Les faillites sont cumulatives entre les rounds jusqu'à capture ou
    /// libération (poursuivants neutralisés, sortie de carte, reset combat).
    /// </summary>
    public static class PursuitState
    {
        private sealed class PursuitContest
        {
            public CharacterStats Prey;
            public int Failures;
            public bool IsCaught;
            public readonly HashSet<CharacterStats> Pursuers = new();
        }

        private static readonly Dictionary<CharacterStats, PursuitContest> _contests = new();

        /// <summary>Seuil de capture (défaut Codex : 4 faillites cumulées).</summary>
        public static int FailuresToCapture = 4;

        /// <summary>Distance minimale maintenue sur réussite de la proie (cases).</summary>
        public static int MinKeptDistance = 1;

        private static PursuitContest GetOrCreate(CharacterStats prey)
        {
            if (!_contests.TryGetValue(prey, out var c) || c == null)
            {
                c = new PursuitContest { Prey = prey };
                _contests[prey] = c;
            }
            return c;
        }

        /// <summary>Faillites cumulées de la proie (0 si aucun concours en cours).</summary>
        public static int GetFailures(CharacterStats prey)
        {
            if (prey == null) return 0;
            return _contests.TryGetValue(prey, out var c) && c != null ? c.Failures : 0;
        }

        /// <summary>Vrai si la proie a été rattrapée et acculée (4 faillites).</summary>
        public static bool IsCaught(CharacterStats prey)
        {
            if (prey == null) return false;
            return _contests.TryGetValue(prey, out var c) && c != null && c.IsCaught;
        }

        /// <summary>
        /// Enregistre les poursuivants actifs d'un round (traçabilité, sans jet).
        /// </summary>
        public static void TrackPursuers(CharacterStats prey, IEnumerable<CharacterStats> pursuers)
        {
            if (prey == null) return;
            var c = GetOrCreate(prey);
            if (pursuers == null) return;
            foreach (var p in pursuers)
            {
                if (p != null && p != prey) c.Pursuers.Add(p);
            }
        }

        /// <summary>
        /// Enregistre une faillite de la proie contre un poursuivant.
        /// Retourne le total cumulé. À 4 : capture immédiate (flag + Immobilisé 1 tour = arrêt).
        /// </summary>
        public static int RegisterFailure(CharacterStats prey, CharacterStats pursuer = null)
        {
            if (prey == null) return 0;
            var c = GetOrCreate(prey);
            if (c.IsCaught) return c.Failures;
            if (pursuer != null && pursuer != prey) c.Pursuers.Add(pursuer);
            c.Failures++;
            if (c.Failures >= FailuresToCapture)
            {
                c.IsCaught = true;
                // Arrêt net : la proie acculée ne peut plus fuir (mais peut se battre).
                prey.ApplyStatus(StatusEffect.Immobilise, 1);
            }
            return c.Failures;
        }

        /// <summary>
        /// Enregistre une réussite de la proie (maintien de la distance, sans effet compteur).
        /// </summary>
        public static void RegisterSuccess(CharacterStats prey, CharacterStats pursuer = null)
        {
            if (prey == null) return;
            var c = GetOrCreate(prey);
            if (pursuer != null && pursuer != prey) c.Pursuers.Add(pursuer);
        }

        /// <summary>Libère une proie (poursuite abandonnée / cible hors d'atteinte).</summary>
        public static bool ReleasePrey(CharacterStats prey, bool liftImmobilise = false)
        {
            if (prey == null) return false;
            bool removed = _contests.Remove(prey);
            if (liftImmobilise) prey.RemoveStatus(StatusEffect.Immobilise);
            return removed;
        }

        /// <summary>
        /// À appeler au début du tour personnel : purge les concours caducs
        /// (proie morte/null, poursuivants tous morts). Les faillites cumulées
        /// survivent d'un round à l'autre (règle des 4 faillites).
        /// </summary>
        public static void OnUnitTurnStart(CharacterStats unit)
        {
            if (_contests.Count == 0) return;
            var keys = new CharacterStats[_contests.Count];
            _contests.Keys.CopyTo(keys, 0);
            for (int i = 0; i < keys.Length; i++)
            {
                var prey = keys[i];
                if (prey == null || !_contests.TryGetValue(prey, out var c) || c == null)
                {
                    if (prey != null) _contests.Remove(prey);
                    continue;
                }
                if (!prey.IsAlive && !c.IsCaught)
                {
                    _contests.Remove(prey);
                    continue;
                }
                // Sans poursuivant vivant et proie non capturée, la poursuite
                // est classée (fuite réussie ou traque abandonnée).
                bool anyAlive = false;
                foreach (var p in c.Pursuers)
                {
                    if (p != null && p.IsAlive) { anyAlive = true; break; }
                }
                if (!anyAlive && !c.IsCaught) _contests.Remove(prey);
            }
        }

        /// <summary>Retire une unité qui quitte la carte (proie libérée ou poursuivant retiré).</summary>
        public static void RemoveUnit(CharacterStats unit)
        {
            if (unit == null) return;
            _contests.Remove(unit);
            if (_contests.Count == 0) return;
            var keys = new CharacterStats[_contests.Count];
            _contests.Keys.CopyTo(keys, 0);
            for (int i = 0; i < keys.Length; i++)
            {
                if (_contests.TryGetValue(keys[i], out var c) && c != null)
                {
                    c.Pursuers.Remove(unit);
                }
            }
        }

        public static void ClearAll() => _contests.Clear();

        public static int ActiveContestCount => _contests.Count;
    }
}

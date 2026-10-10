using System;
using System.Collections.Generic;
using Killtime.Tactics.Grid;

namespace Killtime.Tactics.Units
{
    /// <summary>
    /// Registre global des unités tactiques présentes dans le combat courant.
    /// Remplace les appels à <see cref="UnityEngine.Object.FindObjectsByType{T}"/>
    /// dans les chemins chauds (pathfinding, occupation de grille, IA).
    /// </summary>
    /// <remarks>
    /// Les unités s'inscrivent dans <see cref="OnEnable"/> et se retirent dans
    /// <see cref="OnDestroy"/> afin de rester référencées tant qu'elles sont vivantes,
    /// même si elles sont temporairement désactivées (<c>SetActive(false)</c>).
    /// </remarks>
    public static class TacticalUnitRegistry
    {
        private static readonly List<TacticalUnit> _units = new();
        private static readonly object _lock = new();
        private static IReadOnlyList<TacticalUnit> _snapshot = Array.Empty<TacticalUnit>();

        /// <summary>
        /// Vue en lecture seule et thread-safe de toutes les unités enregistrées.
        /// </summary>
        public static IReadOnlyList<TacticalUnit> AllUnits => _snapshot;

        /// <summary>
        /// Déclenché après l'ajout stable d'une unité dans le registre.
        /// </summary>
        public static event Action<TacticalUnit> OnUnitRegistered;

        /// <summary>
        /// Déclenché après le retrait stable d'une unité du registre.
        /// </summary>
        public static event Action<TacticalUnit> OnUnitUnregistered;

        /// <summary>
        /// Enregistre une unité. Ignore les doublons.
        /// </summary>
        public static void Register(TacticalUnit unit)
        {
            if (unit == null) return;

            lock (_lock)
            {
                if (_units.Contains(unit)) return;
                _units.Add(unit);
                PublishSnapshot();
            }

            OnUnitRegistered?.Invoke(unit);
        }

        /// <summary>
        /// Retire une unité. Ignore si absente.
        /// </summary>
        public static void Unregister(TacticalUnit unit)
        {
            if (unit == null) return;

            bool removed;
            lock (_lock)
            {
                removed = _units.Remove(unit);
                if (removed) PublishSnapshot();
            }

            if (removed) OnUnitUnregistered?.Invoke(unit);
        }

        /// <summary>
        /// Vide le registre. À appeler entre deux rencontres pour éviter
        /// les unités résiduelles d'une scène précédente.
        /// </summary>
        public static void Clear()
        {
            List<TacticalUnit> removed;
            lock (_lock)
            {
                removed = new List<TacticalUnit>(_units);
                _units.Clear();
                PublishSnapshot();
            }

            foreach (var unit in removed)
            {
                OnUnitUnregistered?.Invoke(unit);
            }
        }

        /// <summary>
        /// Unités dont la logique de vie indique qu'elles sont en vie.
        /// </summary>
        public static IEnumerable<TacticalUnit> GetAliveUnits()
        {
            var snapshot = _snapshot;
            for (int i = 0; i < snapshot.Count; i++)
            {
                var unit = snapshot[i];
                if (unit != null && unit.Stats != null && unit.Stats.IsAlive)
                    yield return unit;
            }
        }

        /// <summary>
        /// Unités contrôlées par le joueur.
        /// </summary>
        public static IEnumerable<TacticalUnit> GetPlayerUnits()
        {
            var snapshot = _snapshot;
            for (int i = 0; i < snapshot.Count; i++)
            {
                var unit = snapshot[i];
                if (unit != null && unit.IsPlayerControlled)
                    yield return unit;
            }
        }

        /// <summary>
        /// Unités non contrôlées par le joueur.
        /// </summary>
        public static IEnumerable<TacticalUnit> GetEnemyUnits()
        {
            var snapshot = _snapshot;
            for (int i = 0; i < snapshot.Count; i++)
            {
                var unit = snapshot[i];
                if (unit != null && !unit.IsPlayerControlled)
                    yield return unit;
            }
        }

        /// <summary>
        /// Unités occupant les coordonnées données (y compris les empreintes multi-case).
        /// Une unité sans position logique valide est ignorée.
        /// </summary>
        public static IEnumerable<TacticalUnit> GetUnitsAt(HexCoordinates hex)
        {
            var snapshot = _snapshot;
            for (int i = 0; i < snapshot.Count; i++)
            {
                var unit = snapshot[i];
                if (unit == null || !unit.HasLogicalPosition) continue;
                if (unit.Occupies(hex)) yield return unit;
            }
        }

        private static void PublishSnapshot()
        {
            _snapshot = _units.ToArray();
        }
    }
}

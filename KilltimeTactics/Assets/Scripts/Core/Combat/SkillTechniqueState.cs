using System;
using System.Collections.Generic;
using Killtime.Core.Character;

namespace Killtime.Core.Combat
{
    /// <summary>
    /// État partagé des techniques de spécialisation à effet différé (Livre III) :
    /// failles exposées (Analyse de Faille), provocations (Regard de Prédateur)
    /// et bonus de ligne (Mener : Tenir la Ligne !).
    ///
    /// Keyé sur <see cref="CharacterStats"/> pour rester dans
    /// la couche Core (utilisable joueur + IA, sans dépendance Tactics/Unity).
    /// Durées exprimées en ticks de tours personnels : <see cref="OnUnitTurnStart"/>
    /// DOIT être appelé au début de chaque tour personnel (TurnManager).
    /// Les marques expirent d'elles-mêmes (~1 round) : aucune purge manuelle requise.
    /// </summary>
    public static class SkillTechniqueState
    {
        /// <summary>Durée standard d'une marque, en tours personnels globaux (~1 round).</summary>
        public const int MarkDurationTurnTicks = 4;

        private sealed class FlawMark
        {
            public CharacterStats Marker;
            public int TicksLeft;
        }

        private sealed class TauntMark
        {
            public CharacterStats Taunter;
            public int TicksLeft;
        }

        private sealed class LineBonus
        {
            public CharacterStats Donor;
            public int Amount;
        }

        private static readonly Dictionary<CharacterStats, FlawMark> _exposedFlaws = new();
        private static readonly Dictionary<CharacterStats, TauntMark> _taunts = new();
        private static readonly Dictionary<CharacterStats, LineBonus> _lineBonuses = new();

        // =====================================================================
        // FAILLES EXPOSÉES (Analyse de Faille — Tactique & Stratégie)
        // =====================================================================

        /// <summary>
        /// Expose la faille de la cible : la prochaine attaque qui la touche ignore
        /// son seuil d'encaissement (choc traumatique garanti). Marque consommable.
        /// </summary>
        public static void ApplyExposedFlaw(CharacterStats marker, CharacterStats target)
        {
            if (marker == null || target == null) return;
            _exposedFlaws[target] = new FlawMark { Marker = marker, TicksLeft = MarkDurationTurnTicks };
        }

        public static bool IsFlawExposed(CharacterStats target)
        {
            if (target == null) return false;
            return _exposedFlaws.ContainsKey(target);
        }

        /// <summary>
        /// Retrouve l'auteur de la faille (pour la coordination d'escouade de l'IA).
        /// </summary>
        public static bool TryGetFlawMarker(CharacterStats target, out CharacterStats marker)
        {
            marker = null;
            if (target == null) return false;
            if (_exposedFlaws.TryGetValue(target, out var mark) && mark?.Marker != null && mark.Marker.IsAlive)
            {
                marker = mark.Marker;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Consomme la faille (appelée par CombatCalculator à la touche).
        /// Retourne true si une faille a été exploitée.
        /// </summary>
        public static bool ConsumeExposedFlaw(CharacterStats target)
        {
            if (target == null) return false;
            if (_exposedFlaws.ContainsKey(target))
            {
                _exposedFlaws.Remove(target);
                return true;
            }
            return false;
        }

        // =====================================================================
        // PROVOCATIONS (Regard de Prédateur — Intimidation)
        // =====================================================================

        /// <summary>
        /// La victime est provoquée : tant que la marque est active, elle doit
        /// attaquer son provocateur (appliqué strictement par l'IA, affiché au
        /// joueur dans le menu contextuel).
        /// </summary>
        public static void ApplyTaunt(CharacterStats taunter, CharacterStats victim)
        {
            if (taunter == null || victim == null) return;
            _taunts[victim] = new TauntMark { Taunter = taunter, TicksLeft = MarkDurationTurnTicks };
        }

        /// <summary>
        /// Retourne le provocateur vivant de la victime, ou false si aucun / expiré.
        /// </summary>
        public static bool TryGetTaunter(CharacterStats victim, out CharacterStats taunter)
        {
            taunter = null;
            if (victim == null) return false;
            if (_taunts.TryGetValue(victim, out var mark) && mark?.Taunter != null && mark.Taunter.IsAlive)
            {
                taunter = mark.Taunter;
                return true;
            }
            return false;
        }

        // =====================================================================
        // BONUS DE LIGNE (Mener : Tenir la Ligne ! — Leadership)
        // =====================================================================

        /// <summary>
        /// Confère +Amount d'armure temporaire au bénéficiaire. Expire au début du
        /// prochain tour personnel du donneur (effet « pendant 1 tour », Livre III).
        /// </summary>
        public static void ApplyLineBonus(CharacterStats donor, CharacterStats beneficiary, int amount)
        {
            if (donor == null || beneficiary == null || amount <= 0) return;
            if (_lineBonuses.TryGetValue(beneficiary, out var existing))
            {
                beneficiary.BaseArmorAbsorption = Math.Max(0, beneficiary.BaseArmorAbsorption - existing.Amount);
                _lineBonuses.Remove(beneficiary);
            }
            beneficiary.BaseArmorAbsorption += amount;
            _lineBonuses[beneficiary] = new LineBonus { Donor = donor, Amount = amount };
        }

        public static bool HasLineBonus(CharacterStats beneficiary)
        {
            if (beneficiary == null) return false;
            return _lineBonuses.ContainsKey(beneficiary);
        }

        private static void RemoveDonorBonuses(CharacterStats donor)
        {
            if (donor == null) return;
            CharacterStats[] keys = new CharacterStats[_lineBonuses.Count];
            _lineBonuses.Keys.CopyTo(keys, 0);
            for (int i = 0; i < keys.Length; i++)
            {
                if (_lineBonuses.TryGetValue(keys[i], out var bonus) && bonus != null && bonus.Donor == donor)
                {
                    keys[i].BaseArmorAbsorption = Math.Max(0, keys[i].BaseArmorAbsorption - bonus.Amount);
                    _lineBonuses.Remove(keys[i]);
                }
            }
        }

        // =====================================================================
        // TICK (TurnManager — début de chaque tour personnel)
        // =====================================================================

        /// <summary>
        /// Fait vieillir les marques d'un tick ; expire celles du donneur qui rejoue
        /// (fin de ses bonus de ligne « 1 tour »). Appelé par le TurnManager au début
        /// du tour personnel de chaque unité.
        /// </summary>
        public static void OnUnitTurnStart(CharacterStats unit)
        {
            if (unit == null) return;

            if (_exposedFlaws.Count > 0)
            {
                CharacterStats[] keys = new CharacterStats[_exposedFlaws.Count];
                _exposedFlaws.Keys.CopyTo(keys, 0);
                for (int i = 0; i < keys.Length; i++)
                {
                    if (_exposedFlaws.TryGetValue(keys[i], out var mark) && mark != null)
                    {
                        mark.TicksLeft--;
                        if (mark.TicksLeft <= 0 || !keys[i].IsAlive) _exposedFlaws.Remove(keys[i]);
                    }
                }
            }

            if (_taunts.Count > 0)
            {
                CharacterStats[] keys = new CharacterStats[_taunts.Count];
                _taunts.Keys.CopyTo(keys, 0);
                for (int i = 0; i < keys.Length; i++)
                {
                    if (_taunts.TryGetValue(keys[i], out var mark) && mark != null)
                    {
                        mark.TicksLeft--;
                        if (mark.TicksLeft <= 0 || !keys[i].IsAlive || mark.Taunter == null || !mark.Taunter.IsAlive) _taunts.Remove(keys[i]);
                    }
                }
            }

            // Le donneur rejoue : ses bonus de ligne « pendant 1 tour » expirent.
            RemoveDonorBonuses(unit);
        }
    }
}

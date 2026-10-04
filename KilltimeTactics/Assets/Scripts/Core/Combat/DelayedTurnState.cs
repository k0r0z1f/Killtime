using System.Collections.Generic;
using Killtime.Core.Character;

namespace Killtime.Core.Combat
{
    /// <summary>
    /// Retarder / Tenir l'action (RD-048, Livre VI — Tours) :
    /// - Tenir (Hold, 0 PA) : le combattant passe son tour en conservant ses PA
    ///   restants et se déclare "en attente". Il pourra intervenir juste après
    ///   l'unité active du moment (interruption déclenchée par le joueur/MJ via
    ///   TurnManager.TryResumeHeldUnit), ou retrouver son tour normal au round
    ///   suivant (le reliquat non consommé expire alors).
    /// - Retarder (Delay, 0 PA) : le combattant repousse son tour plus tard dans
    ///   le round en cours. Son total d'initiative est réduit de la pénalité
    ///   configurée (-2 par défaut, CoreRulesConfig.DelayInitiativePenalty) puis
    ///   il est réinséré après les unités qui le devancent encore. 1 retard par
    ///   round et par combattant (rechargé à son prochain tour personnel).
    ///
    /// Les deux postures sont invalidées par les statuts neutralisants (même
    /// assiette que le guet RD-030 : Étourdi / Paralysé / Sonné / Inconscient /
    /// mort). Keyé sur CharacterStats (couche Core, joueur + IA, sans Unity).
    /// </summary>
    public static class DelayedTurnState
    {
        private static readonly HashSet<CharacterStats> _holding = new();
        private static readonly HashSet<CharacterStats> _delayedThisRound = new();

        /// <summary>Vrai si la posture est invalidée par les statuts
        /// (même assiette que le guet RD-030).</summary>
        public static bool IsCancelledByStatus(CharacterStats unit)
        {
            if (unit == null) return true;
            if (!unit.IsAlive) return true;
            var fx = unit.ActiveStatus;
            if (fx.HasFlag(StatusEffect.Etourdi)) return true;
            if (fx.HasFlag(StatusEffect.Paralyse)) return true;
            if (fx.HasFlag(StatusEffect.Sonne)) return true;
            if (fx.HasFlag(StatusEffect.Inconscient)) return true;
            return false;
        }

        /// <summary>Vrai si l'unité s'est déclarée en attente (action tenue).</summary>
        public static bool IsHolding(CharacterStats unit)
        {
            if (unit == null) return false;
            if (!_holding.Contains(unit)) return false;
            if (IsCancelledByStatus(unit)) { _holding.Remove(unit); return false; }
            return true;
        }

        /// <summary>Vrai si l'unité a déjà retardé son tour ce round.</summary>
        public static bool HasDelayed(CharacterStats unit)
        {
            return unit != null && _delayedThisRound.Contains(unit);
        }

        /// <summary>
        /// Se déclare en attente : débite le coût (0 PA par défaut) et marque
        /// l'unité comme tenant son action. Refuse si déjà en attente, PA
        /// insuffisants ou statuts invalidants.
        /// </summary>
        public static bool TryHold(CharacterStats unit, int apCost, out string error)
        {
            error = null;
            if (unit == null) { error = "Tenir impossible : sans combattant."; return false; }
            if (!unit.IsAlive) { error = $"{unit.Name} est hors de combat : tenir impossible."; return false; }
            if (IsCancelledByStatus(unit))
            {
                error = $"{unit.Name} est neutralisé (étourdi/paralysé) : tenir impossible.";
                return false;
            }
            if (_holding.Contains(unit))
            {
                error = $"{unit.Name} tient déjà son action (en attente d'interruption).";
                return false;
            }
            if (!unit.ConsumeActionPoints(apCost))
            {
                error = $"{unit.Name} n'a pas assez de PA ({unit.CurrentActionPoints}/{apCost}) pour tenir son action !";
                return false;
            }
            _holding.Add(unit);
            return true;
        }

        /// <summary>
        /// Consomme l'attente pour intervenir (interruption). Faux si l'unité
        /// ne tenait pas son action. À appeler avant la réinsertion/activation.
        /// </summary>
        public static bool ConsumeHoldToAct(CharacterStats unit)
        {
            if (unit == null) return false;
            return _holding.Remove(unit);
        }

        /// <summary>Annule l'attente sans agir (sanction, annulation volontaire).</summary>
        public static void CancelHold(CharacterStats unit)
        {
            if (unit == null) return;
            _holding.Remove(unit);
        }

        /// <summary>
        /// Retarde le tour : réduit le total d'initiative de la pénalité et
        /// marque l'unité comme ayant retardé ce round (1/round). La réinsertion
        /// dans l'ordre est effectuée par l'appelant (TurnManager) à partir du
        /// nouveau total. Refuse si déjà retardé, sans jet d'initiative
        /// enregistré ou si neutralisé.
        /// </summary>
        public static bool TryDelay(CharacterStats unit, int penalty, out int newTotal, out string error)
        {
            newTotal = int.MinValue;
            error = null;
            if (unit == null) { error = "Retarder impossible : sans combattant."; return false; }
            if (!unit.IsAlive) { error = $"{unit.Name} est hors de combat : retarder impossible."; return false; }
            if (IsCancelledByStatus(unit))
            {
                error = $"{unit.Name} est neutralisé (étourdi/paralysé) : retarder impossible.";
                return false;
            }
            if (_delayedThisRound.Contains(unit))
            {
                error = $"{unit.Name} a déjà retardé son tour ce round (1/round).";
                return false;
            }
            if (unit.InitiativeRollTotal == int.MinValue)
            {
                error = $"{unit.Name} n'a pas encore tiré son initiative : retarder impossible.";
                return false;
            }
            int applied = System.Math.Max(0, penalty);
            newTotal = unit.InitiativeRollTotal - applied;
            unit.InitiativeRollTotal = newTotal;
            _delayedThisRound.Add(unit);
            return true;
        }

        /// <summary>À appeler au début du tour personnel : l'attente non
        /// consommée expire (le combattant rejoue normalement) ; le retard se
        /// recharge (1 nouveau retard autorisé) ; purge les postures invalidées.</summary>
        public static void OnUnitTurnStart(CharacterStats unit)
        {
            if (unit != null)
            {
                _holding.Remove(unit);
                _delayedThisRound.Remove(unit);
            }
            if (_holding.Count == 0 && _delayedThisRound.Count == 0) return;
            if (_holding.Count > 0)
            {
                CharacterStats[] keys = new CharacterStats[_holding.Count];
                _holding.CopyTo(keys, 0);
                for (int i = 0; i < keys.Length; i++)
                {
                    var k = keys[i];
                    if (k == null || !k.IsAlive || IsCancelledByStatus(k))
                        _holding.Remove(k);
                }
            }
            if (_delayedThisRound.Count > 0)
            {
                CharacterStats[] keys = new CharacterStats[_delayedThisRound.Count];
                _delayedThisRound.CopyTo(keys, 0);
                for (int i = 0; i < keys.Length; i++)
                {
                    var k = keys[i];
                    if (k == null || !k.IsAlive)
                        _delayedThisRound.Remove(k);
                }
            }
        }

        /// <summary>Retire une unité qui quitte la carte (plus d'attente/retard).</summary>
        public static void RemoveUnit(CharacterStats unit)
        {
            if (unit == null) return;
            _holding.Remove(unit);
            _delayedThisRound.Remove(unit);
        }

        public static void ClearAll()
        {
            _holding.Clear();
            _delayedThisRound.Clear();
        }
    }
}

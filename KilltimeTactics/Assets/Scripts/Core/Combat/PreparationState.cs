using System.Collections.Generic;
using Killtime.Core.Character;
using Killtime.Core.Rules;

namespace Killtime.Core.Combat
{
    /// <summary>
    /// Préparation d'Action (RD-202, Livre V §20.3) :
    /// 1 PA dépensé -> +2 au jet de l'action ou de la défense SUIVANTE.
    /// One-shot : la préparation est consommée au jet suivant, que ce soit
    /// une attaque (bonus offensif) ou une parade (bonus défensif).
    ///
    /// DISTINCTE de la canalisation RD-048 (pas de cumul, exclusion mutuelle) :
    /// - aucun malus de -1 en défense pendant la préparation ;
    /// - aucune persistance multi-tours (expire au tour personnel suivant) ;
    /// - aucune interruption sur dégât net (le bonus tient jusqu'au jet).
    /// Encaissement passif (0 PA, aucun jet) ne consomme PAS la préparation.
    ///
    /// Keyée sur CharacterStats (couche Core, joueur + IA, sans Unity).
    /// </summary>
    public static class PreparationState
    {
        private static readonly HashSet<CharacterStats> _prepared = new();

        /// <summary>Vrai si la préparation est invalidée par les statuts
        /// (même assiette que le guet RD-030 + mort).</summary>
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

        /// <summary>Vrai si l'unité a une préparation armée (et valide).</summary>
        public static bool IsPrepared(CharacterStats unit)
        {
            if (unit == null) return false;
            if (!_prepared.Contains(unit)) return false;
            if (IsCancelledByStatus(unit)) { _prepared.Remove(unit); return false; }
            return true;
        }

        /// <summary>
        /// Bonus de préparation (+2 si armée, 0 sinon).
        /// Lu SANS consommer (la consommation a lieu à la résolution).
        /// </summary>
        public static int PeekBonus(CharacterStats unit)
        {
            if (!IsPrepared(unit)) return 0;
            var cfg = CoreRulesConfig.Instance;
            return cfg != null ? cfg.PreparationBonus : 2;
        }

        /// <summary>
        /// Prépare l'action suivante : débite le coût et arme le bonus one-shot.
        /// Refuse si déjà préparé, si canalisation en cours (pas de cumul
        /// RD-048/RD-202), PA insuffisants ou neutralisé.
        /// </summary>
        public static bool TryPrepare(CharacterStats unit, int apCost, out string error)
        {
            error = null;
            if (unit == null) { error = "Préparation impossible : sans combattant."; return false; }
            if (!unit.IsAlive) { error = $"{unit.Name} est hors de combat : préparation impossible."; return false; }
            if (IsCancelledByStatus(unit))
            {
                error = $"{unit.Name} est neutralisé (étourdi/paralysé) : préparation impossible.";
                return false;
            }
            if (ChannelingState.IsChanneling(unit))
            {
                error = $"{unit.Name} canalise déjà : la préparation ne cumule pas avec la canalisation (exécutez ou annulez d'abord).";
                return false;
            }
            if (_prepared.Contains(unit))
            {
                error = $"{unit.Name} est déjà préparé : jouez l'action ou la parade suivante d'abord.";
                return false;
            }
            if (!unit.ConsumeActionPoints(apCost))
            {
                error = $"{unit.Name} n'a pas assez de PA ({unit.CurrentActionPoints}/{apCost}) pour préparer son action !";
                return false;
            }
            _prepared.Add(unit);
            return true;
        }

        /// <summary>
        /// Consomme la préparation pour le jet qui se résout (bonus déjà inclus
        /// dans le duel au BeginDuel). Retourne vrai si le bonus était armé et
        /// valide — et donc appliqué au jet. Même parée, l'attaque consomme
        /// l'effort ; seul l'encaissement passif (aucun jet) le préserve.
        /// </summary>
        public static bool ConsumeBonus(CharacterStats unit)
        {
            if (unit == null) return false;
            if (!_prepared.Contains(unit)) return false;
            _prepared.Remove(unit);
            if (!unit.IsAlive || IsCancelledByStatus(unit)) return false;
            return true;
        }

        /// <summary>À appeler au début du tour personnel : la préparation one-shot
        /// non consommée expire (pas d'effort étalé multi-tours, contrairement à
        /// la canalisation). Les préparations invalidées sont purgées partout.</summary>
        public static void OnUnitTurnStart(CharacterStats unit)
        {
            if (unit != null) _prepared.Remove(unit);
            if (_prepared.Count == 0) return;
            CharacterStats[] keys = new CharacterStats[_prepared.Count];
            _prepared.CopyTo(keys, 0);
            for (int i = 0; i < keys.Length; i++)
            {
                var k = keys[i];
                if (k == null || !k.IsAlive || IsCancelledByStatus(k))
                    _prepared.Remove(k);
            }
        }

        /// <summary>Retire une unité qui quitte la carte (préparation perdue).</summary>
        public static void RemoveUnit(CharacterStats unit)
        {
            if (unit == null) return;
            _prepared.Remove(unit);
        }

        public static void ClearAll() => _prepared.Clear();
    }
}

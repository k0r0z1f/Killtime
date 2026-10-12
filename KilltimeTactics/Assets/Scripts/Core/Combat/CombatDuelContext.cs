using System;
using Killtime.Core.Character;
using Killtime.Core.Dice;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Visibility;

namespace Killtime.Core.Combat
{
    /// <summary>
    /// État partagé injecté dans tous les services du duel aveugle.
    /// Remplace les 3 champs privés de l'ancien CombatCalculator monolithe
    /// (_diceRoller, _random, _currentCombatIsAtContactDistance) : une seule
    /// instance créée par la façade, aucune divergence d'état possible.
    /// </summary>
    public sealed class CombatDuelContext
    {
        public DiceRoller DiceRoller;
        public Random Random;
        public bool IsAtContactDistance;

        public CombatDuelContext(DiceRoller diceRoller = null)
        {
            DiceRoller = diceRoller ?? new DiceRoller();
            Random = new Random();
            IsAtContactDistance = false;
        }
    }

    /// <summary>
    /// Helpers purs partagés par tous les services (mises, espérances de dés).
    /// Aucune dépendance vers les services : pas de cycle possible.
    /// Corps copiés à l'identique depuis l'ancien CombatCalculator.
    /// </summary>
    public static class CombatDuelShared
    {
        /// <summary>Espérance d'un dé (pour l'estimation aveugle de l'IA, mise adverse inconnue).</summary>
        public static double DieAverage(DiceType die)
        {
            return die switch
            {
                DiceType.D2 => 1.5,
                DiceType.D3 => 2.0,
                DiceType.D4 => 2.5,
                DiceType.D6 => 3.5,
                DiceType.D8 => 4.5,
                DiceType.D10 => 5.5,
                DiceType.D12 => 6.5,
                DiceType.D20 => 10.5,
                DiceType.TwoD6 => 7.0,
                DiceType.TwoD8 => 9.0,
                DiceType.TwoD10 => 11.0,
                DiceType.TwoD12 => 13.0,
                DiceType.TwoD12Plus10 => 23.0,
                _ => 3.5,
            };
        }

        public static int CommittedAttackBonus(AttackDuel duel)
        {
            var cfg = Rules.CoreRulesConfig.Instance;
            return duel.AttackerBonusPAApplied + duel.AttackerPEApplied * cfg.DuelPEBonusPerPoint;
        }

        public static int CommittedDefenseBonus(AttackDuel duel)
        {
            var cfg = Rules.CoreRulesConfig.Instance;
            return duel.DefenderBonusPAApplied + duel.DefenderPEApplied * cfg.DuelPEBonusPerPoint;
        }

        public static int SpendBonusPA(CharacterStats stats, int requested)
        {
            int applied = 0;
            for (int i = 0; i < requested; i++)
            {
                // 1 PA bonus = +1 au jet ; Ralenti / Dernier Souffle gérés dans ConsumeActionPoints.
                // Mises exemptées du malus armures (ce sont des modificateurs, pas des actions).
                if (stats.ConsumeActionPoints(1, true)) applied++;
                else break;
            }
            return applied;
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Tactics.Grid;
using Killtime.Core.Combat;
using Killtime.Core.Character;
using Killtime.Core.Dice;
using Killtime.Core.Inventory;
using Killtime.UI;

namespace Killtime.Tactics.CombatUI
{
    /// <summary>
    /// §2 Cinquième Force & sorts modulaires (Livre IV) — extrait sans modification depuis CombatActionRegistry.cs.
    /// </summary>
    public static partial class CombatActionRegistry
    {
        private static void AddSpellActions(List<CombatAction> actions, TacticalUnit actor, TacticalUnit target, CombatDevArena arena, bool isSelf, bool isEnemy, bool targetIsAlive)
        {
            // =========================================================================
            // 2. CINQUIÈME FORCE & SORTS MODULAIRES (LIVRE IV)
            // =========================================================================
            if (actor.Sheet != null && actor.Sheet.LearnedSpells.Count > 0)
            {
                bool hasFocus = Killtime.Core.Arcanotech.ArcanotechWorkshop.HasFocus(actor.Sheet);
                bool hasEclat = Killtime.Core.Arcanotech.ArcanotechWorkshop.HasEclat(actor.Sheet);
                foreach (var spell in actor.Sheet.LearnedSpells)
                {
                    int spellCost = spell.ActionPointCost + (hasFocus ? 0 : 2);
                    actions.Add(new CombatAction(
                        $"🔮 Canaliser : {spell.Name} ({spellCost} PA{(hasFocus ? "" : ", sans focus +2")}{(hasEclat ? ", +1 Éclat" : "")})",
                        $"5e Force ({spell.Discipline}) — Dégâts de base: {spell.BaseArcaneDamage}",
                        ActionCategory.CinquiemeForceEtSorts,
                        spellCost,
                        (act, tgt) => act.Stats.CurrentActionPoints >= spell.ActionPointCost + Killtime.Core.Arcanotech.ArcanotechWorkshop.FocusTax(act.Sheet) && targetIsAlive,
                        (act, tgt) =>
                        {
                            var dice = new DiceRoller();
                            if (spell.Cast(act.Stats, tgt.Stats, dice, out string log))
                            {
                                var vis = tgt.GetComponent<TacticalUnitVisual>();
                                vis?.TriggerHitFlash();
                                vis?.SpawnFloatingText($"-{spell.BaseArcaneDamage} Arcanique", Color.magenta);
                            }
                        }
                    ));
                }
            }
        }
    }
}

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
    /// Registre officiel des actions contextuelles de combat classées par livre du Codex.
    /// Valide rigoureusement la portée géométrique (mêlée vs tir) et l'armement équipé.
    /// Façade : le contenu vit dans les partiels (Core, Attack, Spells, Care, Self, Tactics, Techniques, Dev, Inventory).
    /// L'ordre de concaténation définit l'ordre d'affichage du menu — ne pas le modifier.
    /// </summary>
    public static partial class CombatActionRegistry
    {
        public static List<CombatAction> GetAvailableActions(TacticalUnit actor, TacticalUnit target, CombatDevArena arena)
        {
            var actions = new List<CombatAction>();
            if (actor == null || target == null) return actions;

            bool isSelf = (actor == target);
            bool isEnemy = (actor.IsPlayerControlled != target.IsPlayerControlled);
            bool targetIsAlive = target.Stats.IsAlive;

            AddAttackActions(actions, actor, target, arena, isSelf, isEnemy, targetIsAlive);
            AddSpellActions(actions, actor, target, arena, isSelf, isEnemy, targetIsAlive);
            AddCareActions(actions, actor, target, arena, isSelf, isEnemy, targetIsAlive);
            AddSelfActions(actions, actor, target, arena, isSelf, isEnemy, targetIsAlive);
            AddTacticsActions(actions, actor, target, arena, isSelf, isEnemy, targetIsAlive);
            AddVehicleActions(actions, actor, target, arena, isSelf, isEnemy, targetIsAlive);
            AddTechniqueActions(actions, actor, target, arena, isSelf, isEnemy, targetIsAlive);
            AddDevActions(actions, actor, target, arena, isSelf, isEnemy, targetIsAlive);

            return actions;
        }
    }
}

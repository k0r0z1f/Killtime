using System;
using Killtime.Tactics.Units;

namespace Killtime.Tactics.CombatUI
{
    public enum ActionCategory
    {
        AttaqueEtPassesDarmes,   // Livre VI : Tirs ciblés, frappes chirurgicales
        CinquiemeForceEtSorts,   // Livre IV : Arcanotech modulaire (XP = PA)
        TraumatologieEtSoins,    // Livre VII : Premiers soins, garrot, stimulants
        TactiqueEtOrdres,        // Livre III : Intimidation, commandement, garde
        CommandesDev             // Outils de test développeur
    }

    /// <summary>
    /// Représentation unitaire d'une action contextuelle exécutable sur une cible.
    /// </summary>
    public class CombatAction
    {
        public string Title { get; }
        public string Description { get; }
        public ActionCategory Category { get; }
        public int ActionPointCost { get; }
        public Func<TacticalUnit, TacticalUnit, bool> Condition { get; }
        public Action<TacticalUnit, TacticalUnit> Execution { get; }

        public CombatAction(
            string title, 
            string description, 
            ActionCategory category, 
            int apCost, 
            Func<TacticalUnit, TacticalUnit, bool> condition, 
            Action<TacticalUnit, TacticalUnit> execution)
        {
            Title = title;
            Description = description;
            Category = category;
            ActionPointCost = apCost;
            Condition = condition;
            Execution = execution;
        }

        public bool CanExecute(TacticalUnit actor, TacticalUnit target)
        {
            if (actor == null || target == null) return false;
            if (actor.Stats.CurrentActionPoints < ActionPointCost) return false;
            return Condition == null || Condition(actor, target);
        }
    }
}
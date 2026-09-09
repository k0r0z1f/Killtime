using System;
using Killtime.Core.Character;
using Killtime.Core.Dice;

namespace Killtime.Core.Arcanotech
{
    /// <summary>
    /// Modèle de sort arcanotechnique modulaire (Livre IV).
    /// Loi fondamentale d'équilibre : Coût en XP de création = Coût en PA d'exécution en combat.
    /// </summary>
    [Serializable]
    public class NythariteSpell
    {
        public string Name { get; set; }
        public PsychicDiscipline Discipline { get; set; }
        public PsychicStage RequiredStage { get; set; }

        public int CreationXpCost { get; set; }
        
        /// <summary>
        /// Règle d'or : le coût en PA est rigoureusement équivalent au coût en XP.
        /// </summary>
        public int ActionPointCost => CreationXpCost;

        public int RangeInTiles { get; set; }
        public int AreaOfEffectRadius { get; set; }
        public int BaseArcaneDamage { get; set; }
        public StatusEffect InflictedStatus { get; set; }
        public int RequiredNythariteResonance { get; set; }

        public NythariteSpell(string name, PsychicDiscipline discipline, PsychicStage stage, int costXpPa, int range = 4, int damage = 6, StatusEffect status = StatusEffect.None)
        {
            Name = name;
            Discipline = discipline;
            RequiredStage = stage;
            CreationXpCost = costXpPa;
            RangeInTiles = range;
            BaseArcaneDamage = damage;
            InflictedStatus = status;
            RequiredNythariteResonance = (int)stage * 2;
        }

        public bool CanCast(CharacterStats caster, int currentResonance)
        {
            if (caster.CurrentActionPoints < ActionPointCost) return false;
            if (currentResonance < RequiredNythariteResonance) return false;
            return true;
        }

        public bool Cast(CharacterStats caster, CharacterStats target, DiceRoller diceRoller, out string log)
        {
            if (!caster.ConsumeActionPoints(ActionPointCost))
            {
                log = $"{caster.Name} n'a pas assez de PA ({caster.CurrentActionPoints}/{ActionPointCost}) pour canaliser {Name} !";
                return false;
            }

            var roll = diceRoller.Roll(DiceType.D10, caster.Attributes.Intelligence, 10);
            if (roll.IsSuccess)
            {
                int totalDamage = BaseArcaneDamage + Math.Max(0, roll.Differential);
                target.CurrentHealth = Math.Max(0, target.CurrentHealth - totalDamage);
                if (InflictedStatus != StatusEffect.None)
                {
                    target.ActiveStatus |= InflictedStatus;
                }

                log = $"{caster.Name} canalise {Name} (5e Force - {Discipline}) ! Dégâts arcaniques: {totalDamage} | PV Cible: {target.CurrentHealth}/{target.MaxHealth}";
                return true;
            }
            else
            {
                log = $"{caster.Name} échoue à stabiliser le flux de Nytharite pour {Name} (Jet: {roll.Total} vs SD 10).";
                return false;
            }
        }
    }
}

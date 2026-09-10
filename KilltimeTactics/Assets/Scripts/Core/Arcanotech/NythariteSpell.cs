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

            int prevHp = target.CurrentHealth;
            var roll = diceRoller.Roll(DiceType.D10, caster.Attributes.Intelligence, 10);
            if (roll.IsSuccess)
            {
                int diffBonus = Math.Max(0, roll.Differential);
                int totalDamage = BaseArcaneDamage + diffBonus;
                target.CurrentHealth = Math.Max(0, target.CurrentHealth - totalDamage);
                if (InflictedStatus != StatusEffect.None)
                {
                    target.ActiveStatus |= InflictedStatus;
                }

                log = $"🔮 <b>CANALISATION ARCANOTECH : {Name}</b> (5e Force — {Discipline})\n";
                log += $"   🎲 <b>Jet de Nytharite :</b> 1d10 [Tirage {roll.RawRoll} + INT {caster.Attributes.Intelligence} = {roll.Total}] vs SD 10 ➔ <b>Différentiel Net : +{roll.Differential}</b>\n";
                log += $"   ⚡ <b>Dégâts Arcaniques :</b> {BaseArcaneDamage} (Base) + {diffBonus} (Diff &Delta;) = <b><color=#00E5FF>{totalDamage} Absolus</color></b> (Ignore l'armure)\n";
                log += $"   ❤️ <b>Vitalité {target.Name} :</b> {prevHp} ➔ <b>{target.CurrentHealth}/{target.MaxHealth} PV</b>";
                if (InflictedStatus != StatusEffect.None)
                {
                    log += $" | ⚡ <b>ALTÉRATION</b> [{InflictedStatus}]";
                }
                return true;
            }
            else
            {
                log = $"🔮 <b>ÉCHEC DE CANALISATION : {Name}</b>\n";
                log += $"   🎲 <b>Jet de Nytharite :</b> 1d10 [Tirage {roll.RawRoll} + INT {caster.Attributes.Intelligence} = {roll.Total}] vs SD 10 (Différentiel: {roll.Differential}) ➔ Flux arcanique dissipé.";
                return false;
            }
        }
    }
}

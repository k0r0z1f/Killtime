using System;
using Killtime.Core.Character;
using Killtime.Core.Dice;

namespace Killtime.Core.Combat
{
    /// <summary>
    /// Effets des conséquences critiques §8.2 (Livre II) — extrait sans
    /// modification depuis DamageEngineService.cs (split taille RD-201,
    /// règle fichiers &lt; ~800 lignes).
    /// </summary>
    public sealed partial class DamageEngineService
    {
        private static void ApplyOffensiveCriticalEffect(
            CriticalConsequence crit,
            CharacterStats attacker,
            CharacterStats defender,
            ref StatusEffect inflictedStatus,
            ref bool causedKnockback,
            ref FatalBlowResolution fatalRes)
        {
            switch (crit.Threshold)
            {
                case 1:
                    inflictedStatus |= StatusEffect.ATerre;
                    defender.ActiveStatus |= StatusEffect.ATerre;
                    break;
                case 2:
                    causedKnockback = true;
                    break;
                case 6:
                    inflictedStatus |= StatusEffect.Etourdi;
                    defender.ApplyStatus(StatusEffect.Etourdi, 1);
                    break;
                case 8:
                    ClearOneNegativeStatus(attacker);
                    break;
                case 9:
                    attacker.Essoufflement = Math.Max(0, attacker.Essoufflement - 1);
                    break;
                case 10:
                    inflictedStatus |= StatusEffect.Ralenti;
                    defender.ApplyStatus(StatusEffect.Ralenti, 1);
                    break;
                case 12:
                    inflictedStatus |= StatusEffect.Destabilise;
                    defender.ApplyStatus(StatusEffect.Destabilise, 1);
                    break;
                case 16:
                    attacker.CurrentActionPoints = Math.Min(attacker.MaxActionPoints, attacker.CurrentActionPoints + 2);
                    break;
                case 20:
                    defender.ApplyResidualDamage(StatusEffect.Saignement, 3);
                    inflictedStatus |= StatusEffect.Saignement;
                    defender.ActiveStatus |= StatusEffect.Saignement;
                    break;
                case 24:
                    if (Enum.TryParse<StatusEffect>("Inconscient", out var inc) || Enum.TryParse<StatusEffect>("Inconscience", out inc))
                    {
                        inflictedStatus |= inc;
                        defender.ActiveStatus |= inc;
                    }
                    fatalRes = FatalBlowResolution.ForcedUnconscious;
                    break;
            }
        }

        private static void ApplyDefensiveCriticalEffect(
            CriticalConsequence crit,
            CharacterStats defender,
            CharacterStats attacker)
        {
            switch (crit.Threshold)
            {
                case 1:
                case 24:
                    attacker.ActiveStatus |= StatusEffect.ATerre;
                    break;
                case 6:
                    attacker.ApplyStatus(StatusEffect.Etourdi, 1);
                    break;
                case 8:
                    ClearOneNegativeStatus(defender);
                    break;
                case 9:
                    defender.Essoufflement = Math.Max(0, defender.Essoufflement - 1);
                    break;
                case 10:
                    attacker.ApplyStatus(StatusEffect.Ralenti, 1);
                    break;
                case 12:
                    attacker.ApplyStatus(StatusEffect.Destabilise, 1);
                    break;
                case 16:
                    defender.CurrentActionPoints = Math.Min(defender.MaxActionPoints, defender.CurrentActionPoints + 2);
                    break;
                case 20:
                    // Contre-attaque automatique : riposte réflexe infligeant 3 dégâts bruts à l'assaillant
                    attacker.CurrentHealth = Math.Max(0, attacker.CurrentHealth - 3);
                    break;
            }
        }

        private static void ClearOneNegativeStatus(CharacterStats target)
        {
            if (target == null) return;
            if ((target.ActiveStatus & StatusEffect.Destabilise) != 0) target.ActiveStatus &= ~StatusEffect.Destabilise;
            else if ((target.ActiveStatus & StatusEffect.Etourdi) != 0) target.ActiveStatus &= ~StatusEffect.Etourdi;
            else if ((target.ActiveStatus & StatusEffect.Ralenti) != 0) target.ActiveStatus &= ~StatusEffect.Ralenti;
            else if ((target.ActiveStatus & StatusEffect.ATerre) != 0) target.ActiveStatus &= ~StatusEffect.ATerre;
            else if ((target.ActiveStatus & StatusEffect.Sonne) != 0) target.ActiveStatus &= ~StatusEffect.Sonne;
        }
    }
}

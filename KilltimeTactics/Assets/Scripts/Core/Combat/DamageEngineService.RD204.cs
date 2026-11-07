using Killtime.Core.Character;
using Killtime.Core.Dice;

namespace Killtime.Core.Combat
{
    /// <summary>
    /// RD-204 (Livre V §20.2) : choc traumatique lourd — des dégâts nets sur
    /// une zone qui dépassent 2×CON en un seul coup infligent +1 Essoufflement
    /// automatique, puis une épreuve d'Endurance Physique (SD standard) :
    /// échec = +1 Essoufflement supplémentaire (souffle coupé, sans statut —
    /// le choc ordinaire > encaissement porte déjà les statuts de localisation).
    /// Distinct du choc traumatique ordinaire (> encaissement, statuts de
    /// localisation) : les deux peuvent se cumuler sur le même coup.
    /// </summary>
    public sealed partial class DamageEngineService
    {
        internal string ApplyTraumaticShock(
            CharacterStats defender,
            int finalDamage,
            out bool applied,
            out bool conFailed)
        {
            applied = false;
            conFailed = false;
            if (defender == null || !defender.IsAlive || finalDamage <= 0) return "";
            int con = defender.Attributes.Constitution;
            if (con <= 0) return "";
            if (finalDamage <= 2 * con) return "";

            applied = true;
            defender.Essoufflement++;

            var cfg = Rules.CoreRulesConfig.Instance;
            int dc = cfg != null ? cfg.StandardTargetDC : 10;
            DiceType die = defender.GetSkillDie(SkillType.EndurancePhysique, false);
            int mod = defender.GetStatusModifier(SkillType.EndurancePhysique, isOffensive: false);
            var roll = _ctx.DiceRoller.Roll(die, mod, dc);

            if (!roll.IsSuccess)
            {
                conFailed = true;
                defender.Essoufflement++;
                return $" | 🫁 <b>CHOC TRAUMATIQUE LOURD (Livre V §20.2)</b> : {finalDamage} nets > 2×CON ({2 * con}) ➔ +1 Essoufflement auto + épreuve CON ratée [{roll.Total}/{dc}] ➔ <b>+1 Essoufflement supp ({defender.Essoufflement})</b>";
            }

            return $" | 🫁 <b>CHOC TRAUMATIQUE LOURD (Livre V §20.2)</b> : {finalDamage} nets > 2×CON ({2 * con}) ➔ +1 Essoufflement ({defender.Essoufflement}), épreuve CON tenue [{roll.Total}/{dc}]";
        }
    }
}

using System.Collections.Generic;
using Killtime.Tactics.Units;
using Killtime.Core.Combat;
using Killtime.Core.Inventory;
using Killtime.UI;

namespace Killtime.Tactics.CombatUI
{
    /// <summary>
    /// RD-195 : manœuvres véhicules au menu radial (Livre V §22.3).
    /// Embarquer 2 PA / débarquer 0 PA (soi), écraser au volant (2 PA, ennemi),
    /// cribler le véhicule adverse (2 PA, essentiel Moteur / superficiel Roues,
    /// +100 lourde visible). Pur : délégation à CombatDevArena (rien sans
    /// arène : sortie immédiate si arena nulle), fichiers < 800 l.
    /// </summary>
    public static partial class CombatActionRegistry
    {
        private static void AddVehicleActions(List<CombatAction> actions, TacticalUnit actor, TacticalUnit target, CombatDevArena arena, bool isSelf, bool isEnemy, bool targetIsAlive)
        {
            if (actor == null || actor.Stats == null || !actor.Stats.IsAlive) return;
            if (arena == null) return;

            if (isSelf)
            {
                if (VehicleState.IsEmbarked(actor.Stats))
                {
                    string veh = VehicleState.GetVehicleOf(actor.Stats) ?? "?";
                    actions.Add(new CombatAction(
                        $"🚶 Débarquer de {veh} (0 PA)",
                        "Descend du véhicule (gratuit, réflexe).",
                        ActionCategory.TactiqueEtOrdres,
                        0,
                        (act, tgt) => VehicleState.IsEmbarked(act.Stats),
                        (act, tgt) => { arena.TryDisembarkArenaVehicle(act, out _); }
                    ));
                }
                else if (arena.TryGetNearestVehicle(actor.CurrentCoords, 1, out string near, out _) && near != null)
                {
                    string vehId = near;
                    actions.Add(new CombatAction(
                        $"🚙 Embarquer {vehId} (2 PA, contact)",
                        "Monte à bord du véhicule adjacent (2 PA, refusé sans PA / sur épave).",
                        ActionCategory.TactiqueEtOrdres,
                        2,
                        (act, tgt) => act.Stats.CurrentActionPoints >= 2
                            && !VehicleState.IsEmbarked(act.Stats)
                            && arena.TryGetNearestVehicle(act.CurrentCoords, 1, out _, out _),
                        (act, tgt) => { arena.TryEmbarkArenaVehicle(act, vehId, out _); }
                    ));
                }
            }

            if (isEnemy && targetIsAlive)
            {
                string ownVeh = VehicleState.GetVehicleOf(actor.Stats);
                if (ownVeh != null)
                {
                    string foe = target.Stats.Name;
                    actions.Add(new CombatAction(
                        $"🚛 Écraser {foe} ({ownVeh}, 2 PA)",
                        $"Roule sur {foe} avec {ownVeh} : dégâts fixes du gabarit + projection 1 case + À Terre (mobile).",
                        ActionCategory.AttaqueEtPassesDarmes,
                        CombatDevArena.VehicleRunOverAPCost,
                        (act, tgt) => tgt != null && tgt.Stats != null && tgt.Stats.IsAlive
                            && VehicleState.GetVehicleOf(act.Stats) != null
                            && act.Stats.CurrentActionPoints >= CombatDevArena.VehicleRunOverAPCost,
                        (act, tgt) =>
                        {
                            string veh = VehicleState.GetVehicleOf(act.Stats);
                            if (veh == null) return;
                            if (!act.Stats.ConsumeActionPoints(CombatDevArena.VehicleRunOverAPCost)) return;
                            arena.ExecuteVehicleRunOver(veh, tgt);
                        }
                    ));
                }

                string foeVeh = target.Stats != null ? VehicleState.GetVehicleOf(target.Stats) : null;
                if (foeVeh != null)
                {
                    var weapon = actor.Sheet?.GetEquippedWeapon();
                    int raw = weapon != null ? weapon.BaseDamage : 3;
                    int bonus = VehicleMechanics.ComputeHeavyBonus(VehicleMechanics.IsHeavyWeapon(weapon), true);
                    string heavyTag = bonus > 0 ? $" [+{bonus} lourde]" : "";
                    string foeVehId = foeVeh;
                    actions.Add(new CombatAction(
                        $"🔫 Cribler Moteur {foeVehId} ({CombatDevArena.VehicleStrikeAPCost} PA){heavyTag}",
                        $"Frappe le Moteur (essentiel) : {CombatDevArena.FormatStrikePreview(raw, bonus)}. À 0 = épave + équipage débarqué.",
                        ActionCategory.AttaqueEtPassesDarmes,
                        CombatDevArena.VehicleStrikeAPCost,
                        (act, tgt) => tgt != null && tgt.Stats != null && tgt.Stats.IsAlive
                            && VehicleState.GetVehicleOf(tgt.Stats) != null
                            && act.Stats.CurrentActionPoints >= CombatDevArena.VehicleStrikeAPCost,
                        (act, tgt) => { arena.ExecuteVehicleSubsystemStrike(act, foeVehId, VehicleSubsystem.Moteur); }
                    ));
                    actions.Add(new CombatAction(
                        $"🔫 Cribler Roues {foeVehId} ({CombatDevArena.VehicleStrikeAPCost} PA){heavyTag}",
                        $"Frappe les Roues (superficiel) : {CombatDevArena.FormatStrikePreview(raw, bonus)}. À 0 = fonction perdue, engin intact.",
                        ActionCategory.AttaqueEtPassesDarmes,
                        CombatDevArena.VehicleStrikeAPCost,
                        (act, tgt) => tgt != null && tgt.Stats != null && tgt.Stats.IsAlive
                            && VehicleState.GetVehicleOf(tgt.Stats) != null
                            && act.Stats.CurrentActionPoints >= CombatDevArena.VehicleStrikeAPCost,
                        (act, tgt) => { arena.ExecuteVehicleSubsystemStrike(act, foeVehId, VehicleSubsystem.Roues); }
                    ));
                }
            }
        }
    }
}

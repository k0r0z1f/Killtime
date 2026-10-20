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
    /// §3 Traumatologie & soins (Livre VI) — extrait sans modification depuis CombatActionRegistry.cs.
    /// </summary>
    public static partial class CombatActionRegistry
    {
        private static void AddCareActions(List<CombatAction> actions, TacticalUnit actor, TacticalUnit target, CombatDevArena arena, bool isSelf, bool isEnemy, bool targetIsAlive)
        {
            // =========================================================================
            // 3. TRAUMATOLOGIE & SOINS (LIVRE VII)
            // =========================================================================
            if (targetIsAlive)
            {
                // Chirurgie : Suture Réflexe (Livre III) — gestes médicaux d'urgence
                // sur le front : les premiers soins passent de 3 PA à 2 PA.
                // Kit de Chirurgie en poche (non consommé) : CON×3 + purge Empoisonne.
                int healCost = (actor.Stats != null && actor.Stats.HasSpecialization("Chirurgie : Suture Réflexe")) ? 2 : 3;
                bool hasChirKit = HasTool(actor, "Chirurgie");
                string healDesc = (healCost == 2
                    ? "Suture Réflexe au contact (Régénère Constitution × 2 PV). Coût réduit à 2 PA par la spécialisation."
                    : "Stabilisation et suture d'urgence au contact (Régénère Constitution × 2 PV).")
                    + (hasChirKit ? " Kit de Chirurgie : CON×3 + purge Empoisonne." : "")
                    + " RD-083 : Soins Majeurs — lève Souffrant si le soin unique atteint CON×2 (×3 au kit).";
                actions.Add(new CombatAction(
                    $"🩹 Premiers Soins d'Urgence ({healCost} PA{(hasChirKit ? " +Kit" : "")})",
                    healDesc,
                    ActionCategory.TraumatologieEtSoins,
                    healCost,
                    (act, tgt) => act.Stats.CurrentActionPoints >= healCost && act.CurrentCoords.DistanceTo(tgt.CurrentCoords) <= 1,
                    (act, tgt) =>
                    {
                        if (act.Stats.ConsumeActionPoints(healCost))
                        {
                            bool kit = HasTool(act, "Chirurgie");
                            int healAmount = tgt.Stats.Attributes.Constitution * (kit ? 3 : 2);
                            // RD-083 : Premiers Soins / Chirurgie = Soins Majeurs (CON×2/×3
                            // en un seul coup → lève Souffrant, cf. Livre VI §25.1).
                            var major = tgt.Stats.ApplyMajorCare(healAmount);
                            if (kit)
                            {
                                tgt.Stats.RemoveStatus(StatusEffect.Empoisonne);
                                tgt.Stats.RemoveStatus(StatusEffect.EnFeu);
                                tgt.Stats.RemoveStatus(StatusEffect.Asphyxie);
                            }

                            var vis = tgt.GetComponent<TacticalUnitVisual>();
                            vis?.SpawnFloatingText($"+{major.Healed} PV Soignés{(kit ? " (kit)" : "")}{(major.SouffrantLifted ? " — Souffrant LEVÉ" : "")}", Color.green);
                            arena?.Log($"🩹 <b>{act.Stats.Name}</b> prodigue des premiers soins à <b>{tgt.Stats.Name}</b> (+{major.Healed} PV{(kit ? " (kit)" : "")}, seuil CON×2 = {major.Threshold}){(major.SouffrantLifted ? " ➔ <b>Souffrant LEVÉ</b> (blessure refermée) !" : (major.HadSouffrant ? " ➔ <b>Souffrant PERSISTE</b>." : ""))}");
                            if (kit) arena?.Log($"🩹 Bloc opératoire de campagne : Empoisonne/EnFeu/Asphyxie purgés sur <b>{tgt.Stats.Name}</b>.");
                        }
                    }
                ));

                // Consommables marketplace Livre VII §28.3 : seringues + bandage (HealingAmount).
                // 1 PA, soi-même ou allié adjacent, consomme 1 dose du stock.
                InventoryItem bestHeal = FindBestHealingConsumable(actor);
                if (bestHeal != null)
                {
                    string healName = bestHeal.Name;
                    int healValue = bestHeal.HealingAmount;
                    int healStock = CountConsumableStock(actor, healName);
                    bool isBandage = healName.Contains("Bandage");
                    actions.Add(new CombatAction(
                        $"💉 {healName} (+{healValue} PV, 1 PA){(healStock > 1 ? $" x{healStock}" : "")}",
                        isBandage
                            ? $"Injection/pansement au contact : +{healValue} PV + stoppe Saignement. Consomme 1 dose."
                            : $"Injection au contact : +{healValue} PV. Consomme 1 dose (ne réanime pas).",
                        ActionCategory.TraumatologieEtSoins,
                        1,
                        (act, tgt) => act.Stats.CurrentActionPoints >= 1
                            && targetIsAlive
                            && act.CurrentCoords.DistanceTo(tgt.CurrentCoords) <= 1
                            && FindBestHealingConsumable(act) != null,
                        (act, tgt) =>
                        {
                            var dose = FindBestHealingConsumable(act);
                            if (dose == null) return;
                            if (!act.Stats.ConsumeActionPoints(1)) return;
                            string usedName = dose.Name;
                            int usedHeal = dose.HealingAmount;
                            bool usedBandage = usedName.Contains("Bandage");
                            act.Sheet?.ConsumeOne(dose.ItemId);
                            act.NotifyInventoryChanged(true);
                            // RD-083 : soin ordinaire (seringue/bandage) — restaure des PV
                            // mais ne lève JAMAIS Souffrant (Livre VI §24.3).
                            bool hadSouffrant = tgt.Stats.ActiveStatus.HasFlag(StatusEffect.Souffrant);
                            int healed = tgt.Stats.Heal(usedHeal);
                            if (usedBandage) tgt.Stats.RemoveStatus(StatusEffect.Saignement);
                            var vis = tgt.GetComponent<TacticalUnitVisual>();
                            vis?.SpawnFloatingText($"+{healed} PV ({usedName}){(hadSouffrant ? " — Souffrant persiste" : "")}", Color.green);
                            arena?.Log($"💉 <b>{act.Stats.Name}</b> utilise <b>{usedName}</b> sur <b>{tgt.Stats.Name}</b> (+{healed} PV{(usedBandage ? ", Saignement stoppé" : "")}{(hadSouffrant ? " — <b>Souffrant PERSISTE</b> (Soins Majeurs CON×2 requis, Livre VI §25.1)" : "")}).");
                            arena?.RecordChronoSnapshot($"Soin {usedName} : {act.Stats.Name} -> {tgt.Stats.Name}");
                        }
                    ));
                }

                // Attelle Rigide : retire Ralenti (fracture immobilisée), 1 PA, contact.
                InventoryItem attelle = FindConsumableByName(actor, "Attelle");
                if (attelle != null)
                {
                    string attName = attelle.Name;
                    actions.Add(new CombatAction(
                        $"🦴 {attName} (retire Ralenti, 1 PA)",
                        "Immobilise un membre fracturé : retire Ralenti (et Immobilise léger). Consomme 1 dose.",
                        ActionCategory.TraumatologieEtSoins,
                        1,
                        (act, tgt) => act.Stats.CurrentActionPoints >= 1
                            && targetIsAlive
                            && act.CurrentCoords.DistanceTo(tgt.CurrentCoords) <= 1
                            && FindConsumableByName(act, "Attelle") != null,
                        (act, tgt) =>
                        {
                            var dose = FindConsumableByName(act, "Attelle");
                            if (dose == null) return;
                            if (!act.Stats.ConsumeActionPoints(1)) return;
                            act.Sheet?.ConsumeOne(dose.ItemId);
                            act.NotifyInventoryChanged(true);
                            tgt.Stats.RemoveStatus(StatusEffect.Ralenti);
                            var vis = tgt.GetComponent<TacticalUnitVisual>();
                            vis?.SpawnFloatingText("🦴 Fracture immobilisée", Color.green);
                            arena?.Log($"🦴 <b>{act.Stats.Name}</b> pose <b>{dose.Name}</b> sur <b>{tgt.Stats.Name}</b> (Ralenti retiré).");
                        }
                    ));
                }
            }
            else
            {
                // Réanimation d'urgence (Livre VI §25.3 : Poids du Trépas)
                actions.Add(new CombatAction(
                    "⚡ Défibrillation Arcanique (4 PA)",
                    "Tente de ramener un combattant au contact à 1 PV avant le coma définitif. Résurrection : -1 ec pendant (10 - CON) jours réels.",
                    ActionCategory.TraumatologieEtSoins,
                    4,
                    (act, tgt) => act.Stats.CurrentActionPoints >= 4 && act.CurrentCoords.DistanceTo(tgt.CurrentCoords) <= 1,
                    (act, tgt) =>
                    {
                        if (act.Stats.ConsumeActionPoints(4))
                        {
                            // RD-083 : centralise la résurrection + horodate la séquelle.
                            int convalescence = tgt.Stats.ApplyResurrection(1);
                            arena?.Log($"⚡ <b>{act.Stats.Name}</b> réanime <b>{tgt.Stats.Name}</b> (1 PV) — <b>Poids du Trépas</b> : -1 ec pendant {convalescence} jour(s) réels (10 - CON, Livre VI §25.3).");

                            var vis = tgt.GetComponent<TacticalUnitVisual>();
                            vis?.SpawnFloatingText("RÉANIMATION!", Color.cyan);
                        }
                    }
                ));
            }
        }
    }
}

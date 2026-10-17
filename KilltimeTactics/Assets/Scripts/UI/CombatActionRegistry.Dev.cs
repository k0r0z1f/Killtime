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
    /// §6 Commandes développeur — extrait sans modification depuis CombatActionRegistry.cs.
    /// </summary>
    public static partial class CombatActionRegistry
    {
        private static void AddDevActions(List<CombatAction> actions, TacticalUnit actor, TacticalUnit target, CombatDevArena arena, bool isSelf, bool isEnemy, bool targetIsAlive)
        {
            // =========================================================================
            // 6. COMMANDES DÉVELOPPEUR
            // =========================================================================
            actions.Add(new CombatAction(
                "📜 [DEV] Fiche de Personnage Complète",
                "Ouvre et affiche la fiche technique intégrale (Attributs, PA, Encaissement, Compétences).",
                ActionCategory.CommandesDev,
                0,
                null,
                (act, tgt) => CharacterDevWindow.OpenForUnit(tgt)
            ));

            actions.Add(new CombatAction(
                "🎒 [DEV] Gérer l'Inventaire & Armes",
                "Ouvre la fenêtre d'inventaire et d'armurerie 3D pour cette unité.",
                ActionCategory.CommandesDev,
                0,
                null,
                (act, tgt) =>
                {
                    InventoryDevWindow.Open();
                    InventoryDevWindow.Instance?.InspectUnit(tgt);
                }
            ));

            actions.Add(new CombatAction(
                "⚡ [DEV] Recharger tous les PA",
                "Restaure instantanément la réserve de PA au plafond maximal.",
                ActionCategory.CommandesDev,
                0,
                null,
                (act, tgt) => tgt.Stats.CurrentActionPoints = tgt.Stats.MaxActionPoints
            ));

            actions.Add(new CombatAction(
                "❤️ [DEV] Soin Intégral & Dissipation",
                "Restaure l'intégralité des PV et annule tous les statuts négatifs.",
                ActionCategory.CommandesDev,
                0,
                null,
                (act, tgt) =>
                {
                    tgt.Stats.CurrentHealth = tgt.Stats.MaxHealth;
                    tgt.Stats.ActiveStatus = StatusEffect.None;
                    tgt.Stats.Essoufflement = 0;
                }
            ));

            actions.Add(new CombatAction(
                "💀 [DEV] Neutraliser Immédiatement (K.O.)",
                "Bascule les PV à 0 et applique l'inconscience clinique.",
                ActionCategory.CommandesDev,
                0,
                null,
                (act, tgt) =>
                {
                    tgt.Stats.CurrentHealth = 0;
                    tgt.Stats.ActiveStatus |= StatusEffect.Inconscient;
                }
            ));

            actions.Add(new CombatAction(
                "🗑️ [DEV] Retirer de la Carte (Supprimer)",
                "Désenregistre et détruit définitivement cette unité de la grille.",
                ActionCategory.CommandesDev,
                0,
                null,
                (act, tgt) =>
                {
                    if (arena != null)
                    {
                        arena.RemoveUnit(tgt);
                    }
                    else
                    {
                        var tm = Object.FindAnyObjectByType<TurnManager>();
                        tm?.UnregisterUnit(tgt);
                        Object.Destroy(tgt.gameObject);
                    }
                }
            ));
        }
    }
}

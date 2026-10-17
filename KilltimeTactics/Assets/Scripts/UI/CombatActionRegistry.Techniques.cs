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
    /// §5 Techniques de spécialisation (Livre III) + §5b Duo-Tech — extrait sans modification depuis CombatActionRegistry.cs.
    /// </summary>
    public static partial class CombatActionRegistry
    {
        private static void AddTechniqueActions(List<CombatAction> actions, TacticalUnit actor, TacticalUnit target, CombatDevArena arena, bool isSelf, bool isEnemy, bool targetIsAlive)
        {
            // =========================================================================
            // 5. TECHNIQUES DE SPÉCIALISATION (LIVRE III)
            // Clé d'Articulation, Analyse de Faille, Rugissement, Regard de
            // Prédateur, Commandement de zone, Tenir la Ligne ! — 2 PA chacune.
            // Construites par le registre partagé joueur + IA.
            // =========================================================================
            if (CombatTechniqueRegistry.HasAnyCombatTechnique(actor))
            {
                actions.AddRange(CombatTechniqueRegistry.GetTechniqueActions(actor, target, arena));
            }

            // =========================================================================
            // 5b. DUO-TECH (Lucas + Mina) — action unique, paiement immédiat des
            // deux, tissage souris T1 + onde + T2, sans expiration.
            // =========================================================================
            actions.AddRange(DuoTechRegistry.GetDuoTechActions(actor, target, arena));
        }
    }
}

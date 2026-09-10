using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics.Units;
using Killtime.Core.Combat;
using Killtime.Core.Character;
using Killtime.Core.Dice;
using Killtime.UI;

namespace Killtime.Tactics.CombatUI
{
    /// <summary>
    /// Registre officiel des actions contextuelles de combat classées par livre du Codex.
    /// </summary>
    public static class CombatActionRegistry
    {
        public static List<CombatAction> GetAvailableActions(TacticalUnit actor, TacticalUnit target, CombatDevArena arena)
        {
            var actions = new List<CombatAction>();
            if (actor == null || target == null) return actions;

            bool isSelf = (actor == target);
            bool isEnemy = !target.IsPlayerControlled;
            bool targetIsAlive = target.Stats.IsAlive;

            // =========================================================================
            // 1. ATTAQUE & PASSES D'ARMES (LIVRE VI)
            // =========================================================================
            if (!isSelf && isEnemy && targetIsAlive)
            {
                // Frappe Standard
                actions.Add(new CombatAction(
                    "🎯 Attaque Standard (2 PA)",
                    "Frappe générale sur le centre de masse (Torse).",
                    ActionCategory.AttaqueEtPassesDarmes,
                    2,
                    (act, tgt) => act.Stats.CurrentActionPoints >= 2,
                    (act, tgt) => arena.ExecuteAttack(BodyPart.Torse, cancelPenaltyWithAP: false)
                ));

                // Visée Chirurgicale Tête
                actions.Add(new CombatAction(
                    "💀 Visée : Tête (3 PA)",
                    "Tir chirurgical avec dépense préalable de +1 PA pour annuler le malus de -2.",
                    ActionCategory.AttaqueEtPassesDarmes,
                    3,
                    (act, tgt) => act.Stats.CurrentActionPoints >= 3,
                    (act, tgt) => arena.ExecuteAttack(BodyPart.Tete, cancelPenaltyWithAP: true)
                ));

                // Désarmement (Bras Droit)
                actions.Add(new CombatAction(
                    "🗡️ Visée : Bras Droit (2 PA)",
                    "Frappe ciblée pour tenter un désarmement ou un malus d'attaque.",
                    ActionCategory.AttaqueEtPassesDarmes,
                    2,
                    (act, tgt) => act.Stats.CurrentActionPoints >= 2,
                    (act, tgt) => arena.ExecuteAttack(BodyPart.BrasDroit, cancelPenaltyWithAP: false)
                ));

                // Faucher (Jambes)
                actions.Add(new CombatAction(
                    "🦵 Visée : Jambes (2 PA)",
                    "Impact sur les membres inférieurs pour infliger l'état À Terre et Ralenti.",
                    ActionCategory.AttaqueEtPassesDarmes,
                    2,
                    (act, tgt) => act.Stats.CurrentActionPoints >= 2,
                    (act, tgt) => arena.ExecuteAttack(BodyPart.Jambes, cancelPenaltyWithAP: false)
                ));
            }

            // =========================================================================
            // 2. CINQUIÈME FORCE & SORTS MODULAIRES (LIVRE IV)
            // =========================================================================
            if (actor.Sheet != null && actor.Sheet.LearnedSpells.Count > 0)
            {
                foreach (var spell in actor.Sheet.LearnedSpells)
                {
                    actions.Add(new CombatAction(
                        $"🔮 Canaliser : {spell.Name} ({spell.ActionPointCost} PA)",
                        $"5e Force ({spell.Discipline}) — Dégâts de base: {spell.BaseArcaneDamage}",
                        ActionCategory.CinquiemeForceEtSorts,
                        spell.ActionPointCost,
                        (act, tgt) => act.Stats.CurrentActionPoints >= spell.ActionPointCost && targetIsAlive,
                        (act, tgt) =>
                        {
                            var dice = new DiceRoller();
                            if (spell.Cast(act.Stats, tgt.Stats, dice, out string log))
                            {
                                var vis = tgt.GetComponent<TacticalUnitVisual>();
                                vis?.TriggerHitFlash();
                                vis?.SpawnFloatingText($"-{spell.BaseArcaneDamage} Arcanique", Color.magenta);
                            }
                        }
                    ));
                }
            }

            // =========================================================================
            // 3. TRAUMATOLOGIE & SOINS (LIVRE VII)
            // =========================================================================
            if (targetIsAlive)
            {
                actions.Add(new CombatAction(
                    "🩹 Premiers Soins d'Urgence (3 PA)",
                    "Stabilisation et suture d'urgence (Régénère Constitution × 2 PV).",
                    ActionCategory.TraumatologieEtSoins,
                    3,
                    (act, tgt) => act.Stats.CurrentActionPoints >= 3,
                    (act, tgt) =>
                    {
                        if (act.Stats.ConsumeActionPoints(3))
                        {
                            int healAmount = tgt.Stats.Attributes.Constitution * 2;
                            tgt.Stats.CurrentHealth = Mathf.Min(tgt.Stats.MaxHealth, tgt.Stats.CurrentHealth + healAmount);
                            tgt.Stats.ActiveStatus &= ~StatusEffect.Saignement;

                            var vis = tgt.GetComponent<TacticalUnitVisual>();
                            vis?.SpawnFloatingText($"+{healAmount} PV Soignés", Color.green);
                        }
                    }
                ));
            }
            else
            {
                // Réanimation d'urgence
                actions.Add(new CombatAction(
                    "⚡ Défibrillation Arcanique (4 PA)",
                    "Tente de ramener un combattant à 1 PV avant le coma définitif.",
                    ActionCategory.TraumatologieEtSoins,
                    4,
                    (act, tgt) => act.Stats.CurrentActionPoints >= 4,
                    (act, tgt) =>
                    {
                        if (act.Stats.ConsumeActionPoints(4))
                        {
                            tgt.Stats.CurrentHealth = 1;
                            tgt.Stats.ActiveStatus &= ~StatusEffect.Inconscient;

                            var vis = tgt.GetComponent<TacticalUnitVisual>();
                            vis?.SpawnFloatingText("RÉANIMATION!", Color.cyan);
                        }
                    }
                ));
            }

            // Souffle d'urgence si on clique sur soi-même
            if (isSelf)
            {
                actions.Add(new CombatAction(
                    "🫁 Souffle d'Urgence (+2 PA, +1 ESS)",
                    "Prend 1 point d'essoufflement pour gagner 2 PA immédiats.",
                    ActionCategory.TraumatologieEtSoins,
                    0,
                    (act, tgt) => act.Stats.Essoufflement < act.Stats.Attributes.Constitution,
                    (act, tgt) => act.Stats.TakeEmergencyBreath(2)
                ));
            }

            // =========================================================================
            // 4. TACTIQUE & COMMANDEMENT (LIVRE III)
            // =========================================================================
            if (!isSelf && !isEnemy && targetIsAlive)
            {
                actions.Add(new CombatAction(
                    "📢 Ordre Tactique : Couvrir (+1 PA) (2 PA)",
                    "Délègue un point d'action réflexe à l'allié désigné.",
                    ActionCategory.TactiqueEtOrdres,
                    2,
                    (act, tgt) => act.Stats.CurrentActionPoints >= 2,
                    (act, tgt) =>
                    {
                        if (act.Stats.ConsumeActionPoints(2))
                        {
                            tgt.Stats.CurrentActionPoints = Mathf.Min(tgt.Stats.MaxActionPoints, tgt.Stats.CurrentActionPoints + 1);
                            var vis = tgt.GetComponent<TacticalUnitVisual>();
                            vis?.SpawnFloatingText("+1 PA Donné", Color.cyan);
                        }
                    }
                ));
            }

            if (!isSelf && isEnemy && targetIsAlive)
            {
                actions.Add(new CombatAction(
                    "🗣️ Intimidation / Provocation (2 PA)",
                    "Épreuve opposée Charisme vs Instinct pour déstabiliser la cible (-2 aux épreuves).",
                    ActionCategory.TactiqueEtOrdres,
                    2,
                    (act, tgt) => act.Stats.CurrentActionPoints >= 2,
                    (act, tgt) =>
                    {
                        if (act.Stats.ConsumeActionPoints(2))
                        {
                            tgt.Stats.ActiveStatus |= StatusEffect.Destabilise;
                            var vis = tgt.GetComponent<TacticalUnitVisual>();
                            vis?.SpawnFloatingText("DÉSTABILISÉ!", Color.yellow);
                        }
                    }
                ));
            }

            // =========================================================================
            // 5. COMMANDES DÉVELOPPEUR
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

            return actions;
        }
    }
}
using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Tactics.DuoTech;
using Killtime.Core.Character;
using Killtime.Core.Combat;

namespace Killtime.Tactics.CombatUI
{
    /// <summary>
    /// Résolutions du trio d’origine : Sillage Igné, Lacet de Nytharite, Fournaise à Retardement. — extrait sans modification depuis DuoTechRegistry.cs.
    /// </summary>
    public static partial class DuoTechRegistry
    {
        // =====================================================================
        // RÉSOLUTION (appelée par le WeaveController après T2 complet)
        // =====================================================================

        private static void DealBrut(CharacterStats target, int dmg, Action<string> log, string label)
        {
            if (target == null || !target.IsAlive) return;
            int remaining = target.CurrentHealth - dmg;
            if (remaining <= 0)
            {
                target.EvaluateFatalBlow(BodyPart.Torse, dmg);
                log?.Invoke($"{label} : {target.Name} prend {dmg} bruts (létal, {target.LastFatalBlowResolution}).");
            }
            else
            {
                target.CurrentHealth = remaining;
                log?.Invoke($"{label} : {target.Name} prend {dmg} bruts ({remaining} PV).");
            }
        }

        /// <summary>
        /// Sillage Igné : dash Mina (4 bruts à chaque cible de la ligne) puis feu
        /// Lucas selon synchro. Risky = friendly fire 3 bruts sur Mina.
        /// </summary>
        public static void ResolveSillage(
            CharacterStats mina, CharacterStats lucas,
            List<CharacterStats> targets, WeaveSyncGrade grade, Action<string> log)
        {
            const int dashDmg = 4;
            int fireDmg = grade switch
            {
                WeaveSyncGrade.Perfect => 3,
                WeaveSyncGrade.Risky => 2,
                WeaveSyncGrade.Late => 1,
                _ => 0
            };

            log?.Invoke($"🔥 <b>Sillage Igné</b> [{grade}] : dash {dashDmg} + feu {fireDmg}.");
            if (targets != null)
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    var t = targets[i];
                    if (t == null || !t.IsAlive) continue;
                    DealBrut(t, dashDmg + fireDmg, log, "Sillage");
                    if (fireDmg > 0 && grade == WeaveSyncGrade.Perfect)
                    {
                        t.ApplyStatus(StatusEffect.EnFeu, 1);
                        log?.Invoke($"Sillage : {t.Name} est [En Feu] !");
                    }
                }
            }
            if (grade == WeaveSyncGrade.Risky && mina != null && mina.IsAlive)
            {
                DealBrut(mina, 3, log, "Sillage (friendly fire)");
                log?.Invoke("Sillage : le trait de Lucas a léché Mina — T2 trop collé à l'onde !");
            }
            if (grade == WeaveSyncGrade.Miss)
                log?.Invoke("Sillage : aucun croisement — le feu de Lucas passe à côté.");
        }

        /// <summary>
        /// Lacet : 0 dégât, Immobilisé 1 tour sur la cible si boucle fermée.
        /// </summary>
        public static void ResolveLacet(
            CharacterStats target, bool loopClosed, Action<string> log)
        {
            if (target == null || !target.IsAlive) return;
            if (!loopClosed)
            {
                log?.Invoke("🪢 <b>Lacet</b> : boucle non fermée — la cible glisse hors du lasso (PA perdus).");
                return;
            }
            target.ApplyStatus(StatusEffect.Immobilise, 1);
            log?.Invoke($"🪢 <b>Lacet</b> : {target.Name} est [Immobilisé] 1 tour !");
        }

        /// <summary>
        /// Fournaise : 5 bruts + Étourdi 1 tour + -2 PA sur l'isolé si triangle fermé.
        /// </summary>
        public static void ResolveFournaise(
            CharacterStats target, bool triangleClosed, Action<string> log)
        {
            if (target == null || !target.IsAlive) return;
            if (!triangleClosed)
            {
                log?.Invoke("♨️ <b>Fournaise</b> : triangle non fermé — implosion éventée (PA perdus).");
                return;
            }
            DealBrut(target, 5, log, "Fournaise");
            if (!target.IsAlive) return;
            target.ApplyStatus(StatusEffect.Etourdi, 1);
            target.CurrentActionPoints = Math.Max(0, target.CurrentActionPoints - 2);
            log?.Invoke($"Fournaise : {target.Name} est [Étourdi] 1 tour et perd 2 PA !");
        }

    }
}

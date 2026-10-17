using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics.Units;
using Killtime.Tactics.Grid;
using Killtime.Core.Combat;
using Killtime.Core.Character;
using Killtime.Core.Rules;

namespace Killtime.Tactics.CombatUI
{
    /// <summary>
    /// Mener, Tenir la Ligne ! + techniques héroïques Thomas-0 / Lucas-0 (RD-063). — extrait sans modification depuis CombatTechniqueRegistry.cs.
    /// </summary>
    public static partial class CombatTechniqueRegistry
    {

        // =====================================================================
        // 5. MENER / COMMANDEMENT ZONE (Leadership — 2 PA, alliés ≤3 cases, sur soi)
        // =====================================================================

        public static bool CanUseMenerZone(TacticalUnit actor)
        {
            if (!IsAliveUnit(actor)) return false;
            if (!actor.Stats.HasSpecialization(SpecMener)) return false;
            if (actor.Stats.CurrentActionPoints < MenerCost) return false;
            return CountAlliesInRange(actor, AuraRange, onlyMissingPA: true) > 0;
        }

        public static bool ExecuteMenerZone(TacticalUnit actor, CombatDevArena arena)
        {
            if (!CanUseMenerZone(actor)) return false;
            if (!actor.Stats.ConsumeActionPoints(MenerCost)) return false;

            int rallied = 0;
            var all = GetAllUnits();
            for (int i = 0; i < all.Count; i++)
            {
                var u = all[i];
                if (!IsAliveUnit(u) || u == actor || IsEnemyOf(actor, u)) continue;
                if (actor.CurrentCoords.DistanceTo(u.CurrentCoords) > AuraRange) continue;
                if (u.Stats.CurrentActionPoints >= u.Stats.MaxActionPoints) continue;
                u.Stats.CurrentActionPoints = Mathf.Min(u.Stats.MaxActionPoints, u.Stats.CurrentActionPoints + 1);
                u.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("+1 PA Reçu", Color.cyan);
                rallied++;
            }

            actor.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText($"📢 Commandement (-2 PA, {rallied} rallié(s))", new Color(0.2f, 0.85f, 1.0f));
            arena?.Log($"📢 <b>{actor.Stats.Name}</b> galvanise l'escouade (-2 PA) : +1 PA réflexe à {rallied} allié(s) à moins de {AuraRange} cases !");
            arena?.RecordChronoSnapshot($"Commandement : {actor.Stats.Name} ({rallied})");

            TickProgression(actor.Sheet, SkillType.Leadership, arena);
            return true;
        }

        // =====================================================================
        // 6. TENIR LA LIGNE ! (Leadership — 2 PA, alliés ≤3 cases, sur soi)
        // =====================================================================

        public static bool CanUseTenir(TacticalUnit actor)
        {
            if (!IsAliveUnit(actor)) return false;
            if (!actor.Stats.HasSpecialization(SpecTenir)) return false;
            if (actor.Stats.CurrentActionPoints < TenirCost) return false;
            return CountAlliesInRange(actor, AuraRange, onlyWithoutLineBonus: true) > 0;
        }

        public static bool ExecuteTenir(TacticalUnit actor, CombatDevArena arena)
        {
            if (!CanUseTenir(actor)) return false;
            if (!actor.Stats.ConsumeActionPoints(TenirCost)) return false;

            int braced = 0;
            var all = GetAllUnits();
            for (int i = 0; i < all.Count; i++)
            {
                var u = all[i];
                if (!IsAliveUnit(u) || u == actor || IsEnemyOf(actor, u)) continue;
                if (actor.CurrentCoords.DistanceTo(u.CurrentCoords) > AuraRange) continue;
                if (SkillTechniqueState.HasLineBonus(u.Stats)) continue;
                SkillTechniqueState.ApplyLineBonus(actor.Stats, u.Stats, TenirArmorBonus);
                u.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText($"+{TenirArmorBonus} Armure (Ligne !)", Color.green);
                braced++;
            }

            actor.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText($"🛡️ Tenir la Ligne ! (-2 PA, {braced} soutenu(s))", Color.green);
            arena?.Log($"🛡️ <b>{actor.Stats.Name}</b> ordonne de <b>Tenir la Ligne !</b> (-2 PA) : +{TenirArmorBonus} armure à {braced} allié(s) jusqu'à son prochain tour !");
            arena?.RecordChronoSnapshot($"Tenir la Ligne ! : {actor.Stats.Name} ({braced})");

            TickProgression(actor.Sheet, SkillType.Leadership, arena);
            return true;
        }

        // =====================================================================
        // 7. TECHNIQUES HÉROÏQUES : THOMAS-0 & LUCAS-0 (RD-063)
        // =====================================================================

        public static bool CanUseCommandementTectonique(TacticalUnit actor)
        {
            if (!IsAliveUnit(actor)) return false;
            if (!actor.Stats.HasSpecialization(SpecCommandementTectonique)) return false;
            return actor.Stats.CurrentActionPoints >= 2 && CountAlliesInRange(actor, 4) > 0;
        }

        public static bool ExecuteCommandementTectonique(TacticalUnit actor, CombatDevArena arena)
        {
            if (!CanUseCommandementTectonique(actor)) return false;
            if (!actor.Stats.ConsumeActionPoints(2)) return false;

            int boosted = 0;
            var all = GetAllUnits();
            for (int i = 0; i < all.Count; i++)
            {
                var u = all[i];
                if (!IsAliveUnit(u) || u == actor || IsEnemyOf(actor, u)) continue;
                if (actor.CurrentCoords.DistanceTo(u.CurrentCoords) > 4) continue;
                u.Stats.CurrentActionPoints = Mathf.Min(u.Stats.MaxActionPoints + 2, u.Stats.CurrentActionPoints + 1);
                u.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("+1 PA Manœuvre", Color.cyan);
                boosted++;
            }

            actor.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("⚡ Manœuvre Tectonique (-2 PA)", Color.cyan);
            arena?.Log($"⚡ <b>{actor.Stats.Name}</b> ordonne une manœuvre coordonnée (-2 PA) : +1 PA de mouvement accordé à {boosted} allié(s) !");
            TickProgression(actor.Sheet, SkillType.Leadership, arena);
            return true;
        }

        public static bool CanUsePasDeRetraite(TacticalUnit actor)
        {
            if (!IsAliveUnit(actor)) return false;
            if (!actor.Stats.HasSpecialization(SpecPasDeRetraite)) return false;
            return actor.Stats.CurrentActionPoints >= 2;
        }

        public static bool ExecutePasDeRetraite(TacticalUnit actor, CombatDevArena arena)
        {
            if (!CanUsePasDeRetraite(actor)) return false;
            if (!actor.Stats.ConsumeActionPoints(2)) return false;

            int purged = 0;
            var all = GetAllUnits();
            for (int i = 0; i < all.Count; i++)
            {
                var u = all[i];
                if (!IsAliveUnit(u) || IsEnemyOf(actor, u)) continue;
                if (actor.CurrentCoords.DistanceTo(u.CurrentCoords) > 3) continue;

                bool hadStatus = u.Stats.ActiveStatus.HasFlag(StatusEffect.Destabilise)
                              || u.Stats.ActiveStatus.HasFlag(StatusEffect.Etourdi)
                              || u.Stats.ActiveStatus.HasFlag(StatusEffect.Agonisant);

                if (hadStatus)
                {
                    u.Stats.RemoveStatus(StatusEffect.Destabilise);
                    u.Stats.RemoveStatus(StatusEffect.Etourdi);
                    u.Stats.RemoveStatus(StatusEffect.Agonisant);
                    u.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("PANIQUE PURGÉE !", Color.green);
                    purged++;
                }
            }

            actor.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("🛡️ Pas de Retraite ! (-2 PA)", Color.green);
            arena?.Log($"🛡️ <b>{actor.Stats.Name}</b> lance '<b>Pas de Retraite !</b>' (-2 PA) : panique et étourdissement purgés sur {purged} allié(s) !");
            TickProgression(actor.Sheet, SkillType.Leadership, arena);
            return true;
        }

        public static bool CanUseChuteDePression(TacticalUnit actor, TacticalUnit target)
        {
            if (!IsAliveUnit(actor) || !IsAliveUnit(target) || !IsEnemyOf(actor, target)) return false;
            if (!actor.Stats.HasSpecialization(SpecChuteDePression)) return false;
            return actor.Stats.CurrentActionPoints >= 2 && actor.CurrentCoords.DistanceTo(target.CurrentCoords) <= 4;
        }

        public static bool ExecuteChuteDePression(TacticalUnit actor, TacticalUnit target, CombatDevArena arena)
        {
            if (!CanUseChuteDePression(actor, target)) return false;
            if (!actor.Stats.ConsumeActionPoints(2)) return false;

            int remaining = target.Stats.CurrentHealth - 4;
            if (remaining <= 0)
            {
                target.Stats.EvaluateFatalBlow(BodyPart.Torse, 4);
            }
            else
            {
                target.Stats.CurrentHealth = remaining;
            }

            target.Stats.ApplyStatus(StatusEffect.Destabilise, 1);

            actor.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("❄️ Chute de Pression (-2 PA)", Color.cyan);
            var tgtVis = target.GetComponent<TacticalUnitVisual>();
            tgtVis?.TriggerHitFlash();
            tgtVis?.SpawnFloatingText("-4 PV Absolus (Vide) [Déstabilisé]", Color.cyan);

            if (!target.Stats.IsAlive)
            {
                tgtVis?.TriggerFallingBackDeath();
                arena?.AutoTargetNextAlive();
            }

            arena?.Log($"❄️ <b>{actor.Stats.Name}</b> raréfie l'air autour de <b>{target.Stats.Name}</b> (Vide Calculant : -2 PA) : 4 dégâts Absolus et [Déstabilisé] !");
            TickProgression(actor.Sheet, SkillType.MagieElementale, arena);
            return true;
        }

        public static bool CanUseSuggestionsBreves(TacticalUnit actor, TacticalUnit target)
        {
            if (!IsAliveUnit(actor) || !IsAliveUnit(target) || !IsEnemyOf(actor, target)) return false;
            if (!actor.Stats.HasSpecialization(SpecSuggestionsBreves)) return false;
            return actor.Stats.CurrentActionPoints >= 2 && actor.CurrentCoords.DistanceTo(target.CurrentCoords) <= 4;
        }

        public static bool ExecuteSuggestionsBreves(TacticalUnit actor, TacticalUnit target, CombatDevArena arena)
        {
            if (!CanUseSuggestionsBreves(actor, target)) return false;
            if (!actor.Stats.ConsumeActionPoints(2)) return false;

            target.Stats.CurrentActionPoints = Mathf.Max(0, target.Stats.CurrentActionPoints - 1);
            target.Stats.ApplyStatus(StatusEffect.Destabilise, 1);

            actor.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("🧠 Suggestion Brève (-2 PA)", new Color(0.7f, 0.4f, 1f));
            var tgtVis = target.GetComponent<TacticalUnitVisual>();
            tgtVis?.SpawnFloatingText("-1 PA Réserve & Déstabilisé", Color.magenta);

            arena?.Log($"🧠 <b>{actor.Stats.Name}</b> perturbe les synapses de <b>{target.Stats.Name}</b> (Pare-feu Psychologique : -2 PA) : perte de 1 PA et [Déstabilisé] !");
            TickProgression(actor.Sheet, SkillType.MagieEsprit, arena);
            return true;
        }

        public static bool CanUseArretVectoriel(TacticalUnit actor, TacticalUnit target)
        {
            if (!IsAliveUnit(actor) || !IsAliveUnit(target) || !IsEnemyOf(actor, target)) return false;
            if (!actor.Stats.HasSpecialization(SpecArretVectoriel)) return false;
            return actor.Stats.CurrentActionPoints >= 1 && actor.CurrentCoords.DistanceTo(target.CurrentCoords) <= 4;
        }

        public static bool ExecuteArretVectoriel(TacticalUnit actor, TacticalUnit target, CombatDevArena arena)
        {
            if (!CanUseArretVectoriel(actor, target)) return false;
            if (!actor.Stats.ConsumeActionPoints(1)) return false;

            target.Stats.ApplyStatus(StatusEffect.Immobilise, 1);

            int remaining = target.Stats.CurrentHealth - 3;
            if (remaining <= 0)
            {
                target.Stats.EvaluateFatalBlow(BodyPart.Torse, 3);
            }
            else
            {
                target.Stats.CurrentHealth = remaining;
            }

            actor.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("🛑 Arrêt Vectoriel (-1 PA)", Color.cyan);
            var tgtVis = target.GetComponent<TacticalUnitVisual>();
            tgtVis?.TriggerHitFlash();
            tgtVis?.SpawnFloatingText("IMMOBILISÉ (-3 PV Absolus)", Color.cyan);

            if (!target.Stats.IsAlive)
            {
                tgtVis?.TriggerFallingBackDeath();
                arena?.AutoTargetNextAlive();
            }

            arena?.Log($"🛑 <b>{actor.Stats.Name}</b> brise les vecteurs cinétiques de <b>{target.Stats.Name}</b> (-1 PA) : cible figée [Immobilisé] et 3 dégâts Absolus !");
            TickProgression(actor.Sheet, SkillType.MagieEsprit, arena);
            return true;
        }
    }
}

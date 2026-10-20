using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Core.Combat;
using Killtime.Core.Dice;
using Killtime.Core.Character;
using Killtime.Core.Arcanotech;
using Killtime.Core.Inventory;
using Killtime.Core.Rules;
using Killtime.CameraSystem;

namespace Killtime.Tactics.AI
{
    public partial class TacticalAIController : MonoBehaviour
    {
        private bool TryExecuteMedicalSupport(TacticalUnit actor, TacticalUnitVisual visual)
        {
            // Chirurgie : Suture Réflexe (Livre III) — soins d'urgence à 2 PA au lieu de 3.
            int healCost = actor.Stats.HasSpecialization("Chirurgie : Suture Réflexe") ? 2 : 3;
            if (actor.Stats.CurrentActionPoints < healCost) return false;
            if (_defaultPersonality == AIPersonality.Aggressive) return false;

            for (int i = 0; i < _cachedUnits.Count; i++)
            {
                var ally = _cachedUnits[i];
                if (ally == null || ally == actor || ally.Stats == null) continue;
                if (ally.IsPlayerControlled != actor.IsPlayerControlled) continue;

                int dist = actor.CurrentCoords.DistanceTo(ally.CurrentCoords);
                if (dist > 1) continue;

                // Réanimation proscrite si mort instantanée irréversible (boîte crânienne détruite, Livre VI §26)
                if (!ally.Stats.IsAlive && ally.Stats.LastFatalBlowResolution != FatalBlowResolution.InstantDeath && actor.Stats.CurrentActionPoints >= 4)
                {
                    if (actor.Stats.ConsumeActionPoints(4))
                    {
                        // RD-083 : résurrection horodatée (Poids du Trépas §25.3).
                        int convalescence = ally.Stats.ApplyResurrection(1);
                        visual?.SpawnFloatingText("⚡ [RÉANIMATION] Défibrillation (-4 PA)", Color.cyan);
                        ally.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("RÉANIMÉ ! (1 PV)", Color.green);
                        _arena?.Log($"⚡ <b>{actor.Stats.Name}</b> réanime <b>{ally.Stats.Name}</b> au contact (-4 PA) ! <b>Poids du Trépas</b> : -1 ec pendant {convalescence}j réels.");
                        return true;
                    }
                }

                float allyHpRatio = (float)ally.Stats.CurrentHealth / Mathf.Max(1, ally.Stats.MaxHealth);
                bool hasBleed = (ally.Stats.ActiveStatus & StatusEffect.Saignement) != 0;
                bool hasSouffrant = (ally.Stats.ActiveStatus & StatusEffect.Souffrant) != 0;
                bool hasPoison = (ally.Stats.ActiveStatus & StatusEffect.Empoisonne) != 0;
                bool hasChirKit = SmokeScreen.FindItemByName(actor.Stats, "Chirurgie") != null;

                if (ally.Stats.IsAlive && (allyHpRatio <= 0.35f || hasBleed || hasSouffrant || (hasPoison && hasChirKit)) && actor.Stats.CurrentActionPoints >= healCost)
                {
                    if (actor.Stats.ConsumeActionPoints(healCost))
                    {
                        int healAmount = ally.Stats.Attributes.Constitution * (hasChirKit ? 3 : 2);
                        // RD-083 : suture tactique = Soins Majeurs (lève Souffrant, CON×2/×3).
                        var major = ally.Stats.ApplyMajorCare(healAmount);
                        if (hasChirKit)
                        {
                            ally.Stats.RemoveStatus(StatusEffect.Empoisonne);
                            ally.Stats.RemoveStatus(StatusEffect.EnFeu);
                            ally.Stats.RemoveStatus(StatusEffect.Asphyxie);
                        }
                        visual?.SpawnFloatingText($"🩹 [SOINS] Suture Tactique (-{healCost} PA)", Color.green);
                        ally.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText($"+{major.Healed} PV{(hasChirKit ? " (Kit)" : "")}{(major.SouffrantLifted ? " — Souffrant LEVÉ" : "")}", Color.green);
                        _arena?.Log($"🩹 <b>{actor.Stats.Name}</b> soigne <b>{ally.Stats.Name}</b> (+{major.Healed} PV{(hasChirKit ? ", Kit Chirurgie" : "")}{(major.SouffrantLifted ? " ➔ <b>Souffrant LEVÉ</b>" : (major.HadSouffrant ? " ➔ Souffrant persiste" : ""))}) !");
                        return true;
                    }
                }
            }

            return false;
        }

        private bool TryExecuteIntimidation(TacticalUnit actor, TacticalUnit target, int dist, TacticalUnitVisual visual)
        {
            int maxTauntRange = SmokeScreen.HasItemByName(actor.Stats, "Radio") ? 12 : 6;
            if (dist > maxTauntRange || actor.Stats.CurrentActionPoints < 2) return false;
            if ((target.Stats.ActiveStatus & StatusEffect.Destabilise) != 0) return false;
            if (_defaultPersonality != AIPersonality.Tactician && _defaultPersonality != AIPersonality.Aggressive) return false;

            int actorCha = actor.Stats.Attributes.Charisme;
            int targetIns = target.Stats.Attributes.Instinct;
            if (actorCha < targetIns) return false;

            if (actor.Stats.ConsumeActionPoints(2))
            {
                var calc = new CombatCalculator();
                var duel = calc.ResolveOpposedCheck(actor.Stats, SkillType.Intimidation, target.Stats, SkillType.Intuition, 0, 0, defenderAutoStakes: true);
                _arena?.Log($"🗣️ <b>{actor.Stats.Name}</b> lance une provocation militaire sur <b>{target.Stats.Name}</b> :\n   {duel.CombatLog}");

                if (duel.AttackerWins)
                {
                    visual?.SpawnFloatingText("🗣️ Provocation Efficace (-2 PA)", Color.cyan);
                    target.Stats.ActiveStatus |= StatusEffect.Destabilise;
                    var tgtVis = target.GetComponent<TacticalUnitVisual>();
                    tgtVis?.TriggerHitFlash();
                    tgtVis?.SpawnFloatingText("DÉSTABILISÉ ! (-2)", Color.yellow);

                    if (target.Stats.CurrentActionPoints > 0)
                    {
                        target.Stats.ConsumeActionPoints(1);
                        tgtVis?.SpawnFloatingText("-1 PA Réaction", new Color(1f, 0.6f, 0.2f));
                    }
                }
                else
                {
                    visual?.SpawnFloatingText("Provocation sans effet", Color.gray);
                }

                return true;
            }

            return false;
        }

        private bool TryDelegateTacticalOrder(TacticalUnit actor, TacticalUnitVisual visual)
        {
            // Mener (Commandement) (Livre III) : version de zone — galvanise toute
            // l'escouade à ≤3 cases d'un coup, strictement meilleure que l'ordre
            // mono-cible quand au moins un allié peut en profiter.
            if (actor.Stats.HasSpecialization(Killtime.Tactics.CombatUI.CombatTechniqueRegistry.SpecMener))
            {
                if (Killtime.Tactics.CombatUI.CombatTechniqueRegistry.ExecuteMenerZone(actor, _arena))
                {
                    return true;
                }
            }

            int orderRange = SmokeScreen.HasItemByName(actor.Stats, "Radio") ? 12 : 6;
            for (int i = 0; i < _cachedUnits.Count; i++)
            {
                var ally = _cachedUnits[i];
                if (ally == null || ally == actor || ally.Stats == null || !ally.Stats.IsAlive) continue;
                if (ally.IsPlayerControlled != actor.IsPlayerControlled) continue;

                if (actor.CurrentCoords.DistanceTo(ally.CurrentCoords) <= orderRange && ally.Stats.CurrentActionPoints < ally.Stats.MaxActionPoints)
                {
                    if (actor.Stats.ConsumeActionPoints(2))
                    {
                        ally.Stats.CurrentActionPoints = Mathf.Min(ally.Stats.MaxActionPoints, ally.Stats.CurrentActionPoints + 1);
                        visual?.SpawnFloatingText("📢 Ordre Tactique (-2 PA)", new Color(0.2f, 0.85f, 1.0f));
                        ally.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("+1 PA Reçu", Color.cyan);
                        _arena?.Log($"📢 <b>{actor.Stats.Name}</b> transmet 1 PA d'action réflexe à <b>{ally.Stats.Name}</b> !");
                        return true;
                    }
                }
            }

            return false;
        }

        // =========================================================================
        // TECHNIQUES DE SPÉCIALISATION (LIVRE III) — même registre que le menu
        // joueur (CombatTechniqueRegistry) : Clé d'Articulation, Analyse de Faille,
        // Rugissement de Terreur, Regard de Prédateur, Commandement, Tenir la Ligne !
        // Priorités modulées par la personnalité et la posture tactique.
        // =========================================================================
        private bool HasWoundedAllyInRange(TacticalUnit actor, int range)
        {
            for (int i = 0; i < _cachedUnits.Count; i++)
            {
                var ally = _cachedUnits[i];
                if (ally == null || ally == actor || ally.Stats == null || !ally.Stats.IsAlive) continue;
                if (ally.IsPlayerControlled != actor.IsPlayerControlled) continue;
                if (actor.CurrentCoords.DistanceTo(ally.CurrentCoords) > range) continue;

                float ratio = (float)ally.Stats.CurrentHealth / Mathf.Max(1, ally.Stats.MaxHealth);
                if (ratio <= 0.6f) return true;
                if ((ally.Stats.ActiveStatus & (StatusEffect.Destabilise | StatusEffect.Etourdi | StatusEffect.Saignement)) != 0)
                    return true;
            }
            return false;
        }

        private bool HasThreatenedAllyNear(TacticalUnit actor, TacticalUnit threat, int range)
        {
            if (actor == null) return false;
            bool allyNearby = false;
            for (int i = 0; i < _cachedUnits.Count; i++)
            {
                var ally = _cachedUnits[i];
                if (ally == null || ally == actor || ally.Stats == null || !ally.Stats.IsAlive) continue;
                if (ally.IsPlayerControlled != actor.IsPlayerControlled) continue;
                if (actor.CurrentCoords.DistanceTo(ally.CurrentCoords) > range) continue;

                allyNearby = true;
                float ratio = (float)ally.Stats.CurrentHealth / Mathf.Max(1, ally.Stats.MaxHealth);
                if (ratio <= 0.4f) return true;
            }
            // Provocation préventive : un allié proche + une menace encore dangereuse.
            return allyNearby && threat != null && threat.Stats != null && threat.Stats.CurrentActionPoints >= 3;
        }

        /// <summary>RD-084 : projette une liste d'unités vers leurs stats (assaut groupé).</summary>
        private static List<CharacterStats> ToStatsList(List<TacticalUnit> units)
        {
            var stats = new List<CharacterStats>(units != null ? units.Count : 0);
            if (units == null) return stats;
            for (int i = 0; i < units.Count; i++)
            {
                if (units[i] != null && units[i].Stats != null) stats.Add(units[i].Stats);
            }
            return stats;
        }

        private bool TryExecuteCombatTechnique(TacticalUnit actor, TacticalUnit target, TacticalPosture posture, int dist)
        {
            if (actor?.Stats == null || target?.Stats == null) return false;
            if (_arena == null || _arena.IsResolving) return false;
            bool isAggressive = (_defaultPersonality == AIPersonality.Aggressive);
            bool isTacticien = (_defaultPersonality == AIPersonality.Tactician);
            int pa = actor.Stats.CurrentActionPoints;

            // 1. TENIR LA LIGNE ! — blindage d'escouade si un allié proche souffre.
            if (!isAggressive && pa >= 2 && HasWoundedAllyInRange(actor, Killtime.Tactics.CombatUI.CombatTechniqueRegistry.AuraRange))
            {
                if (Killtime.Tactics.CombatUI.CombatTechniqueRegistry.ExecuteTenir(actor, _arena)) return true;
            }

            // 2. RUGISSEMENT DE TERREUR — zone dès 2 ennemis secouables
            // (dès 1 si Tacticien avec réserve d'attaque derrière).
            if (pa >= 2)
            {
                int shakable = Killtime.Tactics.CombatUI.CombatTechniqueRegistry.CountEnemiesInRange(actor, 3, onlyNotDestabilised: true);
                if (shakable >= 2 || (isTacticien && shakable >= 1 && pa >= 4))
                {
                    if (Killtime.Tactics.CombatUI.CombatTechniqueRegistry.ExecuteRugissement(actor, _arena)) return true;
                }
            }

            // 3. REGARD DE PRÉDATEUR — détourne la menace d'un allié en danger.
            if ((isTacticien || _defaultPersonality == AIPersonality.Survivor)
                && pa >= 4 && dist <= 6
                && actor.Stats.Attributes.Charisme >= target.Stats.Attributes.Instinct
                && HasThreatenedAllyNear(actor, target, 3))
            {
                if (Killtime.Tactics.CombatUI.CombatTechniqueRegistry.ExecuteRegard(actor, target, _arena)) return true;
            }

            // 4. ANALYSE DE FAILLE — expose les cibles coriaces avant de frapper.
            if ((isTacticien || _defaultPersonality == AIPersonality.Balanced)
                && pa >= 4 && dist >= 2 && dist <= 10
                && (target.Stats.EncaissementThreshold >= 3 || target.Stats.CurrentHealth >= 8))
            {
                if (Killtime.Tactics.CombatUI.CombatTechniqueRegistry.ExecuteAnalyse(actor, target, _arena)) return true;
            }

            // 5. MENER / COMMANDEMENT — galvanise l'escouade à court de PA.
            if (!isAggressive && pa >= 2)
            {
                int alliesMissing = Killtime.Tactics.CombatUI.CombatTechniqueRegistry.CountAlliesInRange(actor, 3, onlyMissingPA: true);
                if (alliesMissing >= 2 || (posture == TacticalPosture.SupportAndHeal && alliesMissing >= 1))
                {
                    if (Killtime.Tactics.CombatUI.CombatTechniqueRegistry.ExecuteMenerZone(actor, _arena)) return true;
                }
            }

            // 6. CLÉ D'ARTICULATION — finition (3 bruts) ou neutralisation d'une menace.
            if (dist <= 1 && pa >= 2)
            {
                bool finisher = target.Stats.CurrentHealth <= Killtime.Tactics.CombatUI.CombatTechniqueRegistry.CleRawDamage;
                bool neutralise = !isAggressive && pa >= 4 && target.Stats.CurrentActionPoints >= 4;
                if ((finisher || neutralise) && Killtime.Tactics.CombatUI.CombatTechniqueRegistry.ExecuteCle(actor, target, _arena)) return true;
            }

            // 7. LUTTE, GRAPPLE & ÉTRANGLEMENT (RD-039)
            if (dist <= 1 && pa >= 2)
            {
                if (Killtime.Core.Combat.GrappleState.IsGrappling(actor.Stats, target.Stats))
                {
                    _arena.ExecuteStrangulation(actor, target);
                    return true;
                }

                if (Killtime.Core.Combat.GrappleState.IsGrappling(target.Stats, actor.Stats))
                {
                    _arena.ExecuteGrappleEscape(actor, target);
                    return true;
                }

                bool isGrapplerProfile = actor.Stats.Attributes.Force >= 5 || actor.Stats.Attributes.Constitution >= 5;
                // RD-IA05 : règle de gabarit (menu joueur) : l'attaquant doit
                // être au moins aussi massif que sa victime.
                bool sizeOk = TitanFootprint.GetHexCount(actor.FootprintType) >= TitanFootprint.GetHexCount(target.FootprintType);
                if (isGrapplerProfile && sizeOk && !target.Stats.ActiveStatus.HasFlag(StatusEffect.Immobilise) && pa >= 4
                    && !actor.Stats.ActiveStatus.HasFlag(StatusEffect.Immobilise))
                {
                    _arena.ExecuteGrapple(actor, target);
                    return true;
                }
            }

            // 8. ASSAUT GROUPÉ & POURSUITE (RD-084, Livre V §21.1-25.2)
            if (pa >= 2 && _arena != null)
            {
                // Assaut groupé : cible blindée (armure/encaissement élevés) + alliés
                // à portée. Seul le membre le plus lent déclenche (synchro §21.2).
                int targetArmor = target.Stats.BaseArmorAbsorption + target.Stats.GetWornArmorBonus();
                if ((targetArmor >= 4 || target.Stats.EncaissementThreshold >= 6)
                    && _arena.TryResolveCombinedSkill(actor, target, out var combinedSkill))
                {
                    var members = _arena.CollectCombinedAttackers(actor, target, combinedSkill);
                    int minMembers = CoreRulesConfig.Instance != null ? CoreRulesConfig.Instance.CombinedAttackMinMembers : 2;
                    if (members.Count >= minMembers && members.Contains(actor))
                    {
                        CharacterStats slowest = GroupAssaultState.GetSlowestMember(ToStatsList(members));
                        if (slowest == actor.Stats)
                        {
                            _arena.ExecuteCombinedAttack(actor, target, BodyPart.Torse);
                            return true;
                        }
                    }
                }

                // Poursuite : cible en fuite à distance (proie affaiblie ou concours
                // déjà engagé). Concours d'Athlétisme, 4 faillites = capture.
                int distP = actor.CurrentCoords.DistanceTo(target.CurrentCoords);
                int pursuitCost = CoreRulesConfig.Instance != null ? CoreRulesConfig.Instance.PursuitAPCost : 1;
                if (distP >= 2 && pa > pursuitCost
                    && !PursuitState.IsCaught(target.Stats)
                    && (target.Stats.CurrentHealth <= target.Stats.MaxHealth * 0.35f
                        || PursuitState.GetFailures(target.Stats) > 0))
                {
                    _arena.ExecutePursuitRound(target, new List<TacticalUnit> { actor });
                    return true;
                }
            }

            // 9. TECHNIQUES HÉROÏQUES (RD-063 : Thomas-0 & Lucas-0) — même
            // registre que le menu joueur, ignorées jusque-là par l'IA.
            if (_arena != null)
            {
                // Arrêt Vectoriel (1 PA, ≤4) : figement prioritaire des menaces.
                if (pa >= 1 && dist <= 4
                    && Killtime.Tactics.CombatUI.CombatTechniqueRegistry.CanUseArretVectoriel(actor, target)
                    && Killtime.Tactics.CombatUI.CombatTechniqueRegistry.ExecuteArretVectoriel(actor, target, _arena)) return true;
                // Chute de Pression (2 PA, ≤4) : 4 absolus + Déstabilisé.
                if (pa >= 2 && dist <= 4
                    && Killtime.Tactics.CombatUI.CombatTechniqueRegistry.CanUseChuteDePression(actor, target)
                    && Killtime.Tactics.CombatUI.CombatTechniqueRegistry.ExecuteChuteDePression(actor, target, _arena)) return true;
                // Suggestions Brèves (2 PA, ≤4) : draine 1 PA adverse.
                if (pa >= 2 && dist <= 4 && target.Stats.CurrentActionPoints >= 2
                    && Killtime.Tactics.CombatUI.CombatTechniqueRegistry.CanUseSuggestionsBreves(actor, target)
                    && Killtime.Tactics.CombatUI.CombatTechniqueRegistry.ExecuteSuggestionsBreves(actor, target, _arena)) return true;
                // Commandement Tectonique (2 PA) : +1 PA de manœuvre aux alliés.
                if (pa >= 2
                    && Killtime.Tactics.CombatUI.CombatTechniqueRegistry.CanUseCommandementTectonique(actor)
                    && Killtime.Tactics.CombatUI.CombatTechniqueRegistry.ExecuteCommandementTectonique(actor, _arena)) return true;
                // Pas de Retraite ! (2 PA) : purge panique/étourdi des alliés.
                if (pa >= 2 && HasPanickedAllyInRange(actor, Killtime.Tactics.CombatUI.CombatTechniqueRegistry.AuraRange)
                    && Killtime.Tactics.CombatUI.CombatTechniqueRegistry.CanUsePasDeRetraite(actor)
                    && Killtime.Tactics.CombatUI.CombatTechniqueRegistry.ExecutePasDeRetraite(actor, _arena)) return true;
            }

            return false;
        }

        // =========================================================================
        // RD-IA : MANŒUVRES COMPLÉMENTAIRES (même registre que le menu joueur)
        // Charge, bousculade, frappe titanesque, lancer d'arme, balise laser,
        // entretien personnel, soutien allié, observation active.
        // =========================================================================

        /// <summary>RD-IA01 : Observation active quand aucune cible visible.</summary>
        private bool TryActiveObservationAI(TacticalUnit actor, TacticalUnitVisual visual)
        {
            var fog = Killtime.Tactics.Visibility.FogOfWarManager.Instance;
            if (fog == null || actor?.Stats == null || actor.Stats.CurrentActionPoints < 1) return false;

            TacticalUnit nearestHidden = null;
            int nearestDist = int.MaxValue;
            for (int i = 0; i < _cachedUnits.Count; i++)
            {
                var u = _cachedUnits[i];
                if (u == null || u == actor || u.Stats == null || !u.Stats.IsAlive || u.Stats.IsSurrendered) continue;
                bool hostile = actor.IsPlayerControlled ? !u.IsPlayerControlled : u.IsPlayerControlled;
                if (_mode == CombatAIMode.FullAuto && u.IsPlayerControlled != actor.IsPlayerControlled) hostile = true;
                if (!hostile) continue;
                int d = TitanFootprint.MinDistanceBetweenUnits(actor.CurrentCoords, actor.FootprintType, u.CurrentCoords, u.FootprintType);
                if (d < nearestDist)
                {
                    nearestDist = d;
                    nearestHidden = u;
                }
            }
            if (nearestHidden == null) return false;

            if (fog.TryActiveObservation(actor, nearestHidden, out string log))
            {
                visual?.SpawnFloatingText("👁️ Menace repérée !", Color.cyan);
            }
            else
            {
                visual?.SpawnFloatingText("👁️ Observation (1 PA)", Color.gray);
            }
            _arena?.Log(log);
            return true;
        }

        /// <summary>RD-IA02 : chemin vers l'allié à soigner quand la posture
        /// SupportAndHeal est active mais personne n'est au contact.</summary>
        private List<HexCoordinates> GetWoundedAllyPath(TacticalUnit actor)
        {
            TacticalUnit bestAlly = null;
            int bestDist = int.MaxValue;
            bool bestDead = false;
            for (int i = 0; i < _cachedUnits.Count; i++)
            {
                var ally = _cachedUnits[i];
                if (ally == null || ally == actor || ally.Stats == null) continue;
                if (ally.IsPlayerControlled != actor.IsPlayerControlled) continue;
                int d = TitanFootprint.MinDistanceBetweenUnits(actor.CurrentCoords, actor.FootprintType, ally.CurrentCoords, ally.FootprintType);
                if (d <= 1) continue;

                bool dead = !ally.Stats.IsAlive;
                bool wounded = ally.Stats.IsAlive
                    && ((float)ally.Stats.CurrentHealth / Mathf.Max(1, ally.Stats.MaxHealth) <= 0.35f
                        || (ally.Stats.ActiveStatus & (StatusEffect.Saignement | StatusEffect.Souffrant)) != 0);
                if (!dead && !wounded) continue;
                if (dead && (ally.Stats.LastFatalBlowResolution == FatalBlowResolution.InstantDeath || actor.Stats.CurrentActionPoints < 4)) continue;
                if (d < bestDist)
                {
                    bestDist = d;
                    bestAlly = ally;
                    bestDead = dead;
                }
            }
            if (bestAlly == null || _pathfinder == null || _grid == null) return null;

            int careCost = bestDead ? 4 : 3;
            int moveBudget = Mathf.Max(0, actor.Stats.CurrentActionPoints - careCost - GetRequiredDefensiveReserve(actor, TacticalPosture.SupportAndHeal));
            if (moveBudget <= 0) return null;

            return FindPathTowardsTarget(actor, bestAlly, moveBudget);
        }

        private bool HasPanickedAllyInRange(TacticalUnit actor, int range)
        {
            for (int i = 0; i < _cachedUnits.Count; i++)
            {
                var ally = _cachedUnits[i];
                if (ally == null || ally == actor || ally.Stats == null || !ally.Stats.IsAlive) continue;
                if (ally.IsPlayerControlled != actor.IsPlayerControlled) continue;
                if (actor.CurrentCoords.DistanceTo(ally.CurrentCoords) > range) continue;
                var fx = ally.Stats.ActiveStatus;
                if (fx.HasFlag(StatusEffect.Destabilise) || fx.HasFlag(StatusEffect.Etourdi) || fx.HasFlag(StatusEffect.Agonisant))
                    return true;
            }
            return false;
        }

        /// <summary>RD-031 : sécurise les longs déplacements qui rompent le
        // contact (Décrochage payé) au lieu de subir les réactions.</summary>
        private void EnsureSafeDisengageForMove(TacticalUnit actor, int steps)
        {
            if (actor?.Stats == null || steps <= 0) return;
            if (OpportunityState.IsExemptFromProvoking(actor.Stats)) return;
            if (OpportunityState.HasSafeDisengage(actor.Stats)) return;
            if (OpportunityState.IsCancelledByStatus(actor.Stats)) return;

            bool inContact = false;
            for (int i = 0; i < _cachedUnits.Count; i++)
            {
                var u = _cachedUnits[i];
                if (u == null || u == actor || u.Stats == null || !u.Stats.IsAlive) continue;
                bool hostile = actor.IsPlayerControlled ? !u.IsPlayerControlled : u.IsPlayerControlled;
                if (_mode == CombatAIMode.FullAuto && u.IsPlayerControlled != actor.IsPlayerControlled) hostile = true;
                if (!hostile) continue;
                if (TitanFootprint.MinDistanceBetweenUnits(actor.CurrentCoords, actor.FootprintType, u.CurrentCoords, u.FootprintType) <= 1)
                {
                    inContact = true;
                    break;
                }
            }
            if (!inContact) return;

            int disCost = CoreRulesConfig.Instance != null ? CoreRulesConfig.Instance.DisengageAPCost : 1;
            if (actor.Stats.CurrentActionPoints >= disCost + 1
                && OpportunityState.RequestSafeDisengage(actor.Stats, disCost, out _))
            {
                actor.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("💨 Décrochage IA", new Color(0.4f, 0.9f, 1.0f));
            }
        }

        private static bool AIHasShield(TacticalUnit actor)
        {
            var inv = actor?.Sheet?.Inventory;
            if (inv == null) return false;
            for (int i = 0; i < inv.Count; i++)
            {
                var it = inv[i];
                if (it == null || !it.IsEquipped) continue;
                if (!string.IsNullOrEmpty(it.Name) && it.Name.IndexOf("Bouclier", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                if (!string.IsNullOrEmpty(it.Category) && it.Category.IndexOf("Bouclier", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private static InventoryItem AIFindByName(TacticalUnit actor, string fragment)
        {
            var inv = actor?.Sheet?.Inventory;
            if (inv == null || string.IsNullOrEmpty(fragment)) return null;
            for (int i = 0; i < inv.Count; i++)
            {
                var it = inv[i];
                if (it == null || string.IsNullOrEmpty(it.Name)) continue;
                if (it.Name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0) return it;
            }
            return null;
        }

        private static InventoryItem AIFindBestHeal(TacticalUnit actor)
        {
            var inv = actor?.Sheet?.Inventory;
            if (inv == null) return null;
            InventoryItem best = null;
            for (int i = 0; i < inv.Count; i++)
            {
                var it = inv[i];
                if (it == null || it.Type != ItemType.Consumable || it.HealingAmount <= 0) continue;
                if (best == null || it.HealingAmount > best.HealingAmount) best = it;
            }
            return best;
        }

        private bool CanShootNow(TacticalUnit actor)
        {
            if (actor?.Stats == null) return false;
            if (!HasRangedWeapon(actor, out _, out _, out _)) return false;
            var mag = actor.Sheet?.GetEquippedWeapon();
            if (mag != null && mag.AmmoCapacity > 0)
                return !mag.Jammed && mag.AmmoRemaining > 0;
            return ChargeState.CanFireRanged(actor.Stats);
        }

        // =========================================================================
        // SOUTIEN TACTIQUE ALLIÉ (corde, entraves, traînée, radio, maintenance,
        // consommables, garde du corps) — registre joueur, sans la partie soins.
        // =========================================================================
    }
}

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
        private IEnumerator ExecuteTacticalAttack(TacticalUnit actor, TacticalUnit target, TacticalPosture posture, bool isRanged, SkillType attackSkill)
        {
            _arena?.SelectTarget(target);

            // RD-033 : maintenance munitions avant le tir (désenrayer, recharger).
            // Sans coup disponible, l'attaque est annulée (repli mêlée/déplacement de l'appelant).
            if (isRanged && actor != null && actor.Sheet != null)
            {
                var mag = actor.Sheet.GetEquippedWeapon();
                if (mag != null && mag.AmmoCapacity > 0)
                {
                    if (mag.Jammed)
                    {
                        if (_arena != null && _arena.TryClearJam(actor, out string jamMsg))
                        {
                            _arena.Log(jamMsg);
                            yield return new WaitForSeconds(Mathf.Min(0.4f, _actionDelay * 0.3f));
                        }
                        else
                        {
                            _arena?.Log($"⚠️ {actor.Stats.Name} ne peut ni tirer ni désenrayer.");
                            yield break;
                        }
                    }
                    if (mag.AmmoRemaining <= 0)
                    {
                        bool reloaded = false;
                        if (_arena != null)
                        {
                            // Standard de préférence, HD seulement si c'est la seule réserve.
                            if (WeaponAmmo.StockShots(actor.Sheet, mag.AmmoType, false) > 0)
                                reloaded = _arena.TryReloadWeapon(actor, false, out _);
                            else if (mag.HeavyAmmo && WeaponAmmo.StockShots(actor.Sheet, mag.AmmoType, true) > 0)
                                reloaded = _arena.TryReloadWeapon(actor, true, out _);
                        }
                        if (reloaded)
                        {
                            _arena?.Log($"🔋 <b>{actor.Stats.Name}</b> recharge <b>{mag.Name}</b> [{mag.AmmoRemaining}/{mag.AmmoCapacity}].");
                            yield return new WaitForSeconds(Mathf.Min(0.4f, _actionDelay * 0.3f));
                        }
                        else
                        {
                            _arena?.Log($"⚠️ {actor.Stats.Name} : chargeur vide, aucune réserve ({mag.AmmoType}).");
                            yield break;
                        }
                    }
                }
            }

            SkillType defenseSkill = target.Stats.Attributes.Agilite >= target.Stats.Attributes.Force
                ? SkillType.Esquive
                : SkillType.DefenseCorporelle;

            TacticalAttackPlan plan = CalculateBestAttackPlan(actor, target, posture, isRanged, attackSkill, defenseSkill);

            var visual = actor.GetComponent<TacticalUnitVisual>();
            string partLabel = plan.TargetedPart == BodyPart.Tete ? "[VISÉE] Tête" :
                               plan.TargetedPart == BodyPart.BrasDroit ? "[VISÉE] Désarmement" :
                               plan.TargetedPart == BodyPart.Jambes ? "[VISÉE] Jambes" : "[VISÉE] Torse";
            if (isRanged) partLabel = $"🎯 Tir {partLabel}";
            if (plan.BonusAP > 0 || plan.BonusPE > 0) partLabel += $" (+{plan.BonusAP} PA / +{plan.BonusPE} PE)";

            while (Killtime.UI.CombatHUD.IsPaused)
            {
                yield return null;
            }

            visual?.SpawnFloatingText(partLabel, plan.TargetedPart == BodyPart.Tete ? Color.red : Color.cyan);
            yield return new WaitForSeconds(Mathf.Min(0.5f, _actionDelay * 0.35f));

            int weaponDmg = 5;
            var equippedWeapon = actor.Sheet?.GetEquippedWeapon();
            if (equippedWeapon != null && equippedWeapon.BaseDamage > 0) weaponDmg = equippedWeapon.BaseDamage + (equippedWeapon.LoadedHD ? 1 : 0);

            _arena?.ExecuteAttack(
                targetedPart: plan.TargetedPart,
                cancelPenaltyWithAP: plan.CancelPenalty,
                attackSkill: attackSkill,
                defenseSkill: defenseSkill,
                defenderWantsToDefend: true,
                attackerSpecialization: null,
                defenderSpecialization: null,
                weaponBaseDamage: weaponDmg,
                attackerBonusAP: plan.BonusAP,
                defenderBonusAP: -1,
                attackDie: null,
                defenseDie: null,
                explicitTarget: target,
                attackerPE: plan.BonusPE,
                defenderPE: 0,
                explicitAttacker: actor
            );

            if (_arena != null)
            {
                float waitElapsed = 0f;
                while (_arena.IsResolving && (waitElapsed < 6f || CombatUI.CombatContextMenuUI.IsDefenseOpen))
                {
                    yield return null;
                    if (!CombatUI.CombatContextMenuUI.IsDefenseOpen)
                    {
                        waitElapsed += Time.deltaTime;
                    }
                }
            }
            else if (_cinematicDirector != null)
            {
                float waitElapsed = 0f;
                while (_cinematicDirector.CurrentMode == CameraMode.CinematicAction && waitElapsed < 5f)
                {
                    yield return null;
                    waitElapsed += Time.deltaTime;
                }
            }
        }

        private TacticalAttackPlan CalculateBestAttackPlan(
            TacticalUnit actor, TacticalUnit target, TacticalPosture posture,
            bool isRanged, SkillType attackSkill, SkillType defenseSkill)
        {
            DiceType attackDie = actor.Stats.GetSkillDie(attackSkill, true);
            DiceType defenseDie = target.Stats.GetSkillDie(defenseSkill, false);

            int attStatusMod = actor.Stats.GetStatusModifier(attackSkill, true);
            int defStatusMod = target.Stats.GetStatusModifier(defenseSkill, false);

            // RD-IA07 : couvert réel d'empreinte + fumée (avant : Single/single).
            CoverType targetCover = isRanged ? GetCoverLevel(actor.CurrentCoords, target.CurrentCoords, target.FootprintType, actor.FootprintType, actor) : CoverType.None;
            int coverPen = CoverSystem.AttackPenalty(targetCover);

            int nightPen = 0;
            var fowManager = Killtime.Tactics.Visibility.FogOfWarManager.Instance;
            if (isRanged && fowManager != null && fowManager.IsNight && !CombatCalculator.HasLampOrThermal(actor.Stats))
            {
                nightPen = -1;
            }

            // RD-IA07 : modificateurs de duel aveugle ignorés par l'estimation :
            // furtivité (+2, sans réaction), canalisation (+2), bonus arme EC,
            // hauteur RD-036 et canon entravé au contact (-2).
            int stealthBonus = StealthState.IsStealthed(actor.Stats) ? StealthState.StealthAttackBonus : 0;
            int channelBonus = ChannelingState.IsChanneling(actor.Stats) ? ChannelingState.PeekAttackBonus(actor.Stats) : 0;
            int weaponEcBonus = 0;
            int elevationMod = 0;
            var equippedForRuling = actor.Sheet?.GetEquippedWeapon();
            if (equippedForRuling != null) weaponEcBonus = Mathf.Max(0, equippedForRuling.AttackBonusEc);
            try
            {
                if (_arena != null)
                {
                    var ruling = _arena.GetElevationRuling(actor, target, attackSkill, equippedForRuling, !isRanged);
                    elevationMod = ruling.AttackMod;
                }
            }
            catch { /* estimation optionnelle */ }
            int entravePen = 0;
            try
            {
                if (actor.IsCanonEntrave() && (attackSkill == SkillType.Ballistique || attackSkill == SkillType.ProjectilesTir))
                    entravePen = -2;
            }
            catch { /* ignore */ }

            int weaponDmg = 5;
            var equippedWeapon = actor.Sheet?.GetEquippedWeapon();
            if (equippedWeapon != null && equippedWeapon.BaseDamage > 0) weaponDmg = equippedWeapon.BaseDamage + (equippedWeapon.LoadedHD ? 1 : 0);

            int targetArmor = target.Stats.BaseArmorAbsorption + target.Stats.GetWornArmorBonus();
            int targetEncaissement = target.Stats.EncaissementThreshold;
            int targetHp = target.Stats.CurrentHealth;

            int availableAP = actor.Stats.CurrentActionPoints;
            int reserveNeeded = GetRequiredDefensiveReserve(actor, posture);
            int spendablePE = actor.Stats.GetSpendablePE();

            double baseAttExpected = CombatCalculator.DieAverage(attackDie) + attStatusMod + coverPen + nightPen
                + stealthBonus + channelBonus + weaponEcBonus + elevationMod + entravePen;
            double baseDefExpected = target.Stats.CanDefendActively()
                ? CombatCalculator.DieAverage(defenseDie) + defStatusMod
                : 0.0;

            // Optimisation pure déportée dans AttackPlanEvaluator (formule inchangée).
            // La collecte ci-dessus (dés, couvert, nuit, arme, élévation, PE) reste ici.
            var planInput = new AttackPlanInput
            {
                BaseAttExpected = baseAttExpected,
                BaseDefExpected = baseDefExpected,
                WeaponDmg = weaponDmg,
                TargetArmor = targetArmor,
                TargetEncaissement = targetEncaissement,
                TargetHp = targetHp,
                AvailableAP = availableAP,
                ReserveNeeded = reserveNeeded,
                SpendablePE = spendablePE,
                PeValue = CoreRulesConfig.Instance != null ? CoreRulesConfig.Instance.DuelPEBonusPerPoint : 1,
                MaxAttacks = actor.Stats.GetMaxAttacksAllowed(attackDie),
                AttacksThisTurn = actor.Stats.AttacksThisTurn,
                IsRanged = isRanged,
                Posture = posture,
                TargetRapidite = target.Stats.Attributes.Rapidite,
                TargetHasATerre = (target.Stats.ActiveStatus & StatusEffect.ATerre) != 0,
                TargetCurrentAP = target.Stats.CurrentActionPoints,
            };
            return AttackPlanEvaluator.Evaluate(
                in planInput, p => BodyPartInfo.GetInfo(p).DifficultyModifier);
        }

        // =========================================================================
        // SORTS ARCANOTECH (LIVRE IV)
        // =========================================================================
        private bool TryCastBestSpell(TacticalUnit actor, TacticalUnit target, int dist, bool hasLOS)
        {
            if (actor.Sheet == null || actor.Sheet.LearnedSpells == null || actor.Sheet.LearnedSpells.Count == 0) return false;
            if (target == null || target.Stats == null || !target.Stats.IsAlive) return false;
            // RD-IA06 : sans ligne de vue, aucun sort ciblé (avant : cast à
            // l'aveugle à travers les murs).
            if (!hasLOS) return false;

            // RD-IA06 : coût réel = PA du sort + taxe focus (+2 sans focus).
            int focusTax = Killtime.Core.Arcanotech.ArcanotechWorkshop.FocusTax(actor.Sheet);
            NythariteSpell bestSpell = null;
            int bestNetScore = int.MinValue;

            for (int i = 0; i < actor.Sheet.LearnedSpells.Count; i++)
            {
                var spell = actor.Sheet.LearnedSpells[i];
                if (spell == null) continue;
                int realCost = spell.ActionPointCost + focusTax;
                if (spell.RangeInTiles < dist || actor.Stats.CurrentActionPoints < realCost) continue;
                // Score : dégâts nets estimés après armure, au prorata du coût.
                int targetArmor = target.Stats.BaseArmorAbsorption + target.Stats.GetWornArmorBonus();
                int net = spell.BaseArcaneDamage - targetArmor;
                int score = net * 10 - realCost * 3;
                if (target.Stats.CurrentHealth <= System.Math.Max(1, net)) score += 60;
                if (score > bestNetScore)
                {
                    bestNetScore = score;
                    bestSpell = spell;
                }
            }

            if (bestSpell != null && bestNetScore > 0)
            {
                var dice = new DiceRoller();
                var vis = target.GetComponent<TacticalUnitVisual>();
                var actorVis = actor.GetComponent<TacticalUnitVisual>();
                actorVis?.SpawnFloatingText($"🔮 {bestSpell.Name} (-{bestSpell.ActionPointCost + focusTax} PA)", Color.cyan);

                if (bestSpell.Cast(actor.Stats, target.Stats, dice, out _, dist))
                {
                    vis?.TriggerHitFlash();
                    vis?.SpawnFloatingText($"-{bestSpell.BaseArcaneDamage} Arcanique", Color.magenta);
                }

                return true;
            }

            return false;
        }

        // =========================================================================
        // GRENADES TACTIQUES DE ZONE (LIVRE VIII §31.3)
        // =========================================================================
        private bool TryThrowGrenadeAI(TacticalUnit actor, TacticalPosture posture)
        {
            if (_arena == null || actor?.Stats == null || actor.Sheet == null) return false;
            if (_arena.IsResolving) return false;
            var throwables = CombatDevArena.GetThrowableGrenades(actor.Sheet);
            if (throwables.Count == 0) return false;
            DiceType atkDie = actor.Stats.GetSkillDie(SkillType.Ballistique, true);
            if (!actor.Stats.CanAttack(atkDie)) return false;

            UpdateUnitCache();
            var launcher = CombatDevArena.GetAnyLauncher(actor.Sheet);

            InventoryItem bestGrenade = null;
            TacticalUnit bestCell = null;
            bool bestUseLauncher = false;
            float bestScore = float.MinValue;
            int bestCost = 99;

            for (int gi = 0; gi < throwables.Count; gi++)
            {
                var g = throwables[gi];
                if (g == null) continue;
                bool isUtility = (g.GrenadeKind ?? "").Contains("Flash") || (g.GrenadeKind ?? "").Contains("Fumi")
                    || (g.GrenadeKind ?? "").Contains("Gaz") && g.BaseDamage <= 2;

                for (int li = 0; li < 2; li++)
                {
                    bool useL = (li == 1);
                    InventoryItem l = useL ? launcher : null;
                    if (useL && (launcher == null || !g.LauncherCompatible)) continue;
                    int maxRange = GrenadeRules.ComputeMaxRange(g, l);
                    int cost = GrenadeRules.ComputeAPCost(l, false);
                    if (actor.Stats.CurrentActionPoints < cost) continue;
                    float expected = g.BaseDamage + g.DamageDiceCount * 5.5f;

                    for (int i = 0; i < _cachedUnits.Count; i++)
                    {
                        var enemy = _cachedUnits[i];
                        if (enemy == null || enemy == actor || enemy.Stats == null || !enemy.Stats.IsAlive) continue;
                        bool hostile = actor.IsPlayerControlled ? !enemy.IsPlayerControlled : enemy.IsPlayerControlled;
                        if (_mode == CombatAIMode.FullAuto && enemy.IsPlayerControlled != actor.IsPlayerControlled) hostile = true;
                        if (!hostile) continue;

                        int dThrow = actor.CurrentCoords.DistanceTo(enemy.CurrentCoords);

                        // Brouillard symétrique (§25.4) : pas de grenade sur un ennemi
                        // invisible à 360° (sauf révélé au bruit / Observation).
                        if (_grid != null && actor.Stats != null && enemy.Stats != null)
                        {
                            var gfog = Killtime.Tactics.Visibility.FogOfWarManager.Instance;
                            int grange = gfog != null ? gfog.CurrentSightRange
                                : Killtime.Tactics.Visibility.FogOfWarSystem.LongSightRange;
                            var gvp = Killtime.Tactics.Visibility.FogOfWarSystem.ResolveObserverParams(actor.Stats, grange);
                            float gyaw = Killtime.Tactics.Visibility.FogOfWarManager.FacingYawDeg(actor);
                            bool gseen = Killtime.Tactics.Visibility.FogOfWarSystem.IsCellVisible(
                                actor.CurrentCoords, gyaw, enemy.CurrentCoords,
                                c => _grid.GetNode(c), gvp, _grid.HexRadius);
                            bool gestealthed = Killtime.Core.Combat.StealthState.IsStealthed(enemy.Stats);
                            if (!gseen || (gestealthed && dThrow > 1 && !gvp.Thermal))
                            {
                                bool grevealed = gfog != null
                                    && (gfog.IsNoiseActive(enemy.gameObject.name)
                                        || gfog.IsManuallyRevealed(enemy.gameObject.name));
                                if (!grevealed) continue;
                            }
                        }
                        if (dThrow > maxRange) continue;
                        if (actor.CurrentCoords.DistanceTo(enemy.CurrentCoords) <= Mathf.Max(0, g.BlastRadius)) continue;

                        int foes = 0, allies = 0;
                        for (int j = 0; j < _cachedUnits.Count; j++)
                        {
                            var u = _cachedUnits[j];
                            if (u == null || u.Stats == null || !u.Stats.IsAlive) continue;
                            if (u.CurrentCoords.DistanceTo(enemy.CurrentCoords) > Mathf.Max(0, g.BlastRadius)) continue;
                            bool uHostile = actor.IsPlayerControlled ? !u.IsPlayerControlled : u.IsPlayerControlled;
                            if (_mode == CombatAIMode.FullAuto && u.IsPlayerControlled != actor.IsPlayerControlled) uHostile = true;
                            if (u == actor || !uHostile) allies += (u == actor) ? 2 : 1;
                            else foes++;
                        }

                        if (allies > 0 && posture != TacticalPosture.AllInLethal) continue;
                        float score = foes * (10f + expected) - allies * 25f + (isUtility ? -6f : 0f);
                        if (enemy.Stats.CurrentHealth <= expected) score += 10f;

                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestGrenade = g;
                            bestCell = enemy;
                            bestUseLauncher = useL;
                            bestCost = cost;
                        }
                    }
                }
            }

            if (bestGrenade == null || bestCell == null || bestScore < 18f) return false;
            if (actor.Stats.CurrentActionPoints < bestCost) return false;

            var visual = actor.GetComponent<TacticalUnitVisual>();
            visual?.SpawnFloatingText(
                bestUseLauncher ? $"💣 [IA] Tir {bestGrenade.Name}" : $"💣 [IA] Lancer {bestGrenade.Name}",
                new Color(1f, 0.6f, 0.15f));
            int bonus = (posture == TacticalPosture.AllInLethal) ? Mathf.Min(2, actor.Stats.CurrentActionPoints - bestCost) : 0;
            _arena.ExecuteGrenadeThrow(bestCell.CurrentCoords, bestGrenade.ItemId, bestUseLauncher, false, Mathf.Max(0, bonus));
            return true;
        }

        // =========================================================================
        // RECHERCHE DE CHEMIN ET TOPOLOGIE DE GRILLE
        // =========================================================================
    }
}

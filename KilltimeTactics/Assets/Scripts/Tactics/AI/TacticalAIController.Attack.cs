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
                            // RD-201 : sans désenrayage possible, repli crosse /
                            // mains nues au contact entravé avant d'abandonner.
                            if (TryGetEntraveFallbackStrike(actor, target, out var fbSkill, out var fbDmg, out var fbIsButt))
                            {
                                yield return StartCoroutine(ExecuteEntraveFallbackStrike(actor, target, posture, fbSkill, fbDmg, fbIsButt));
                                yield break;
                            }
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
                            // RD-201 : chargeur vide sans réserve, repli crosse /
                            // mains nues au contact entravé avant d'abandonner.
                            if (TryGetEntraveFallbackStrike(actor, target, out var fbSkill2, out var fbDmg2, out var fbIsButt2))
                            {
                                yield return StartCoroutine(ExecuteEntraveFallbackStrike(actor, target, posture, fbSkill2, fbDmg2, fbIsButt2));
                                yield break;
                            }
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
            double entraveCancelBonus = 0;
            try
            {
                if (actor.IsCanonEntrave() && (attackSkill == SkillType.Ballistique || attackSkill == SkillType.ProjectilesTir))
                    entravePen = -2;
                // RD-201 : le tir visé (+1 PA) annule aussi le -2 d'entrave :
                // compense l'estimation des plans visés au contact.
                int contactDist = TitanFootprint.MinDistanceBetweenUnits(
                    actor.CurrentCoords, actor.FootprintType, target.CurrentCoords, target.FootprintType);
                if (entravePen != 0 && contactDist <= 1 && target.Stats != null && target.Stats.CanDefendActively())
                    entraveCancelBonus = -entravePen;
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
                EntraveCancelBonus = entraveCancelBonus,
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
            HexCoordinates bestCoords = actor.CurrentCoords;
            bool haveBest = false;
            bool bestIsUtility = false;
            bool bestUseLauncher = false;
            float bestScore = float.MinValue;
            int bestCost = 99;
            float actorHpRatio = actor.Stats.MaxHealth > 0
                ? (float)actor.Stats.CurrentHealth / actor.Stats.MaxHealth
                : 1f;

            for (int gi = 0; gi < throwables.Count; gi++)
            {
                var g = throwables[gi];
                if (g == null) continue;
                // RD-106 : fini la pénalité forfaitaire -6 sur l'utilitaire.
                // Chaque famille est scorée selon son effet réel (AIGrenadeScorer pur).
                bool isSmoke = AIGrenadeScorer.IsSmoke(g.GrenadeKind);
                bool isFlash = !isSmoke && AIGrenadeScorer.IsFlashKind(g.GrenadeKind);
                bool isPersistent = !isSmoke && !isFlash
                    && AIGrenadeScorer.IsPersistent(g.GrenadeKind, g.ZoneDurationTurns, g.BlastRadius);
                bool isUtility = isSmoke || isFlash || isPersistent;
                // Masque filtrant : immunise contre les gaz conventionnels (dégâts ×0) ;
                // l'arcanotech n'est que divisé par 2 ⇒ alliés masqués toujours exposés.
                bool gasConventional = SmokeScreen.IsGas(g.GrenadeKind)
                    && !SmokeScreen.IsArcanotechEra(g.Era);
                int zoneDpt = SmokeScreen.ZoneDamagePerTurn(g.GrenadeKind);

                for (int li = 0; li < 2; li++)
                {
                    bool useL = (li == 1);
                    InventoryItem l = useL ? launcher : null;
                    if (useL && (launcher == null || !g.LauncherCompatible)) continue;
                    int maxRange = GrenadeRules.ComputeMaxRange(g, l);
                    int cost = GrenadeRules.ComputeAPCost(l, false);
                    if (actor.Stats.CurrentActionPoints < cost) continue;
                    float expected = AIGrenadeScorer.ExpectedDirect(g.BaseDamage, g.DamageDiceCount);

                    for (int i = 0; i < _cachedUnits.Count; i++)
                    {
                        var enemy = _cachedUnits[i];
                        if (enemy == null || enemy == actor || enemy.Stats == null || !enemy.Stats.IsAlive) continue;
                        bool hostile = actor.IsPlayerControlled ? !enemy.IsPlayerControlled : enemy.IsPlayerControlled;
                        if (_mode == CombatAIMode.FullAuto && enemy.IsPlayerControlled != actor.IsPlayerControlled) hostile = true;
                        if (!hostile) continue;

                        int dThrow = actor.CurrentCoords.DistanceTo(enemy.CurrentCoords);

                        // Brouillard symétrique (§21.4) : pas de grenade sur un ennemi
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
                        // La fumée ne blesse pas : on peut la poser sur soi. Le reste
                        // (létal / flash / gaz) s'interdit le suicide de souffle.
                        if (!isSmoke && actor.CurrentCoords.DistanceTo(enemy.CurrentCoords) <= Mathf.Max(0, g.BlastRadius)) continue;

                        if (isSmoke)
                        {
                            // 3 poses : sur l'ennemi (l'aveugle dedans), sur soi (écran
                            // total avant repli/avance à découvert), au milieu (couloir).
                            var mid = new HexCoordinates(
                                (actor.CurrentCoords.Q + enemy.CurrentCoords.Q) / 2,
                                (actor.CurrentCoords.R + enemy.CurrentCoords.R) / 2);
                            TryScoreSmokeCell(actor, enemy.CurrentCoords, enemy, g.BlastRadius, posture, actorHpRatio,
                                g, useL, cost, true, ref bestGrenade, ref bestCoords, ref haveBest,
                                ref bestIsUtility, ref bestUseLauncher, ref bestScore, ref bestCost);
                            TryScoreSmokeCell(actor, actor.CurrentCoords, enemy, g.BlastRadius, posture, actorHpRatio,
                                g, useL, cost, true, ref bestGrenade, ref bestCoords, ref haveBest,
                                ref bestIsUtility, ref bestUseLauncher, ref bestScore, ref bestCost);
                            if (!mid.Equals(enemy.CurrentCoords) && !mid.Equals(actor.CurrentCoords)
                                && actor.CurrentCoords.DistanceTo(mid) <= maxRange)
                                TryScoreSmokeCell(actor, mid, enemy, g.BlastRadius, posture, actorHpRatio,
                                    g, useL, cost, true, ref bestGrenade, ref bestCoords, ref haveBest,
                                    ref bestIsUtility, ref bestUseLauncher, ref bestScore, ref bestCost);
                            continue;
                        }

                        int foes = 0, allies = 0, exposed = 0;
                        for (int j = 0; j < _cachedUnits.Count; j++)
                        {
                            var u = _cachedUnits[j];
                            if (u == null || u.Stats == null || !u.Stats.IsAlive) continue;
                            if (u.CurrentCoords.DistanceTo(enemy.CurrentCoords) > Mathf.Max(0, g.BlastRadius)) continue;
                            bool uHostile = actor.IsPlayerControlled ? !u.IsPlayerControlled : u.IsPlayerControlled;
                            if (_mode == CombatAIMode.FullAuto && u.IsPlayerControlled != actor.IsPlayerControlled) uHostile = true;
                            if (u == actor || !uHostile)
                            {
                                int w = (u == actor) ? 2 : 1;
                                allies += w;
                                bool masked = gasConventional && SmokeScreen.HasGasMask(u.Stats);
                                if (!masked) exposed += w;
                            }
                            else foes++;
                        }

                        bool finishing = enemy.Stats.CurrentHealth <= expected;
                        float score;
                        if (isFlash)
                        {
                            // Flash aveugle aussi les alliés ⇒ blocage dur sauf AllInLethal.
                            if (allies > 0 && posture != TacticalPosture.AllInLethal) continue;
                            score = AIGrenadeScorer.ScoreFlash(foes, expected, posture);
                            if (finishing) score += 10f;
                        }
                        else if (isPersistent)
                        {
                            // Gaz : les alliés masqués (conventionnel) sont déjà retirés
                            // de `exposed` et ne bloquent plus.
                            if (exposed > 0 && posture != TacticalPosture.AllInLethal) continue;
                            score = AIGrenadeScorer.ScorePersistent(foes, expected, zoneDpt,
                                g.ZoneDurationTurns, AIGrenadeScorer.HasHardControl(g.GrenadeStatuses),
                                exposed, finishing);
                        }
                        else
                        {
                            // Létal : formule historique inchangée.
                            if (allies > 0 && posture != TacticalPosture.AllInLethal) continue;
                            score = AIGrenadeScorer.ScoreLethal(foes, expected, allies, finishing);
                        }

                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestGrenade = g;
                            bestCoords = enemy.CurrentCoords;
                            haveBest = true;
                            bestIsUtility = isUtility;
                            bestUseLauncher = useL;
                            bestCost = cost;
                        }
                    }
                }
            }

            if (!haveBest || bestGrenade == null) return false;
            float threshold = bestIsUtility
                ? AIGrenadeScorer.UtilityPlayThreshold
                : AIGrenadeScorer.LethalPlayThreshold;
            if (bestScore < threshold) return false;
            if (actor.Stats.CurrentActionPoints < bestCost) return false;

            var visual = actor.GetComponent<TacticalUnitVisual>();
            visual?.SpawnFloatingText(
                bestUseLauncher ? $"💣 [IA] Tir {bestGrenade.Name}" : $"💣 [IA] Lancer {bestGrenade.Name}",
                new Color(1f, 0.6f, 0.15f));
            int bonus = (posture == TacticalPosture.AllInLethal) ? Mathf.Min(2, actor.Stats.CurrentActionPoints - bestCost) : 0;
            _arena.ExecuteGrenadeThrow(bestCoords, bestGrenade.ItemId, bestUseLauncher, false, Mathf.Max(0, bonus));
            return true;
        }

        /// <summary>
        /// RD-106 : évalue une pose de fumigène et retient la meilleure (écran de
        /// couverture avant repli/avance à découvert). La fumée ne blesse pas :
        /// les alliés dedans ne bloquent que par pénalité douce (Aveugle local).
        /// </summary>
        private void TryScoreSmokeCell(TacticalUnit actor, HexCoordinates candidate, TacticalUnit anchorEnemy,
            int blast, TacticalPosture posture, float actorHpRatio,
            InventoryItem grenade, bool useLauncher, int cost, bool isUtility,
            ref InventoryItem bestGrenade, ref HexCoordinates bestCoords, ref bool haveBest,
            ref bool bestIsUtility, ref bool bestUseLauncher, ref float bestScore, ref int bestCost)
        {
            int foes = 0, allies = 0, blocked = 0;
            for (int j = 0; j < _cachedUnits.Count; j++)
            {
                var u = _cachedUnits[j];
                if (u == null || u.Stats == null || !u.Stats.IsAlive) continue;
                bool uHostile = actor.IsPlayerControlled ? !u.IsPlayerControlled : u.IsPlayerControlled;
                if (_mode == CombatAIMode.FullAuto && u.IsPlayerControlled != actor.IsPlayerControlled) uHostile = true;
                if (uHostile)
                {
                    if (u.CurrentCoords.DistanceTo(candidate) <= Mathf.Max(0, blast)) foes++;
                    // Posée sur soi : coupe toutes les vues non déjà enfumées.
                    // Ailleurs : ne compte que l'ancre si la pose est près de son segment.
                    if (candidate.Equals(actor.CurrentCoords))
                    {
                        if (!SmokeScreen.SegmentSmoked(actor.CurrentCoords, u.CurrentCoords, _arena.SmokeZones))
                            blocked++;
                    }
                    else if (u == anchorEnemy
                        && !SmokeScreen.SegmentSmoked(actor.CurrentCoords, u.CurrentCoords, _arena.SmokeZones)
                        && NearSegment(actor.CurrentCoords, u.CurrentCoords, candidate))
                        blocked++;
                }
                else if (u.CurrentCoords.DistanceTo(candidate) <= Mathf.Max(0, blast))
                    allies++;
            }

            float score = AIGrenadeScorer.ScoreSmoke(blocked, foes, allies, posture, actorHpRatio);
            if (score > bestScore)
            {
                bestScore = score;
                bestGrenade = grenade;
                bestCoords = candidate;
                haveBest = true;
                bestIsUtility = isUtility;
                bestUseLauncher = useLauncher;
                bestCost = cost;
            }
        }

        /// <summary>La case est-elle sur (ou près de) le segment, à 1 case de tolérance grille ?</summary>
        private static bool NearSegment(HexCoordinates a, HexCoordinates b, HexCoordinates c)
        {
            return a.DistanceTo(c) + c.DistanceTo(b) <= a.DistanceTo(b) + 1;
        }

        /// <summary>
        /// RD-201 (Livre V §22.2) : repli crosse / mains nues au contact
        /// entravé quand le tir est impossible (enrayé, chargeur vide sans
        /// réserve). Retourne faux si l'option ne s'applique pas (pas
        /// d'entrave, cible inconsciente, PA insuffisants, quota épuisé).
        /// </summary>
        private bool TryGetEntraveFallbackStrike(
            TacticalUnit actor, TacticalUnit target,
            out SkillType skill, out int baseDamage, out bool isButtstroke)
        {
            skill = SkillType.MainsNues;
            baseDamage = CanonEntraveRules.UnarmedBaseDamage;
            isButtstroke = false;
            if (actor == null || actor.Stats == null || target == null || target.Stats == null) return false;
            if (!target.Stats.CanDefendActively()) return false;
            if (!actor.IsCanonEntrave()) return false;
            if (TitanFootprint.MinDistanceBetweenUnits(
                    actor.CurrentCoords, actor.FootprintType,
                    target.CurrentCoords, target.FootprintType) > 1) return false;
            if (actor.Stats.CurrentActionPoints < CanonEntraveRules.ButtstrokeAPCost) return false;
            var option = CanonEntraveRules.ChooseContactOption(
                actor.Stats.CurrentActionPoints, false,
                CanonEntraveRules.HasLongWeapon(actor.Stats),
                actor.Stats.CanAttackWithSkill(SkillType.MainsNues));
            if (option == CanonEntraveOption.Buttstroke
                && actor.Stats.CanAttackWithSkill(SkillType.ManiementArmes))
            {
                skill = SkillType.ManiementArmes;
                baseDamage = CanonEntraveRules.ButtstrokeBaseDamage(
                    actor.Sheet?.GetEquippedWeapon());
                isButtstroke = true;
                return true;
            }
            // Repli : si la crosse est indisponible (compétence manquante)
            // les mains nues restent une option dès qu'elles portent.
            if (actor.Stats.CanAttackWithSkill(SkillType.MainsNues))
            {
                skill = SkillType.MainsNues;
                baseDamage = CanonEntraveRules.UnarmedBaseDamage;
                isButtstroke = false;
                return true;
            }
            return false;
        }

        /// <summary>
        /// RD-201 : exécute le repli crosse / mains nues (2 PA) puis attend
        /// la fin de résolution comme une attaque tactique normale.
        /// </summary>
        private IEnumerator ExecuteEntraveFallbackStrike(
            TacticalUnit actor, TacticalUnit target, TacticalPosture posture,
            SkillType skill, int baseDamage, bool isButtstroke)
        {
            var visual = actor.GetComponent<TacticalUnitVisual>();
            visual?.SpawnFloatingText(
                isButtstroke ? "🔨 [CROSSE] Canon entravé" : "🥋 [MAINS NUES] Canon entravé",
                new Color(1f, 0.75f, 0.2f));
            SkillType defenseSkill = target.Stats.Attributes.Agilite >= target.Stats.Attributes.Force
                ? SkillType.Esquive
                : SkillType.DefenseCorporelle;
            _arena?.ExecuteAttack(
                BodyPart.Torse, false, skill, defenseSkill, true, null, null,
                baseDamage, 0, -1, null, null, target, 0, 0, actor,
                isButtstroke: isButtstroke);
            if (_arena != null)
            {
                float waitElapsed = 0f;
                while (_arena.IsResolving && waitElapsed < 6f)
                {
                    yield return null;
                    waitElapsed += Time.deltaTime;
                }
            }
            yield return new WaitForSeconds(Mathf.Min(0.4f, _actionDelay * 0.3f));
        }

        // =========================================================================
        // RECHERCHE DE CHEMIN ET TOPOLOGIE DE GRILLE
        // =========================================================================
    }
}

using System;
using System.Collections;
using UnityEngine;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Audio;
using Killtime.Core.Character;
using Killtime.Core.Combat;
using Killtime.Core.Dice;
using Killtime.Core.Rules;
using Killtime.WebGL;
using Killtime.UI;
using Killtime.CameraSystem;
using Killtime.Tactics.AI;

namespace Killtime.Tactics.Arena
{
    /// <summary>
    /// Routines cinématiques (ExecuteAttackRoutine + ExecuteAttackWithPlayerDefenseRoutine).
    /// Split move-only : contenu verbatim depuis ArenaAttackService.cs.
    /// </summary>
    public sealed partial class ArenaAttackService
    {
        public IEnumerator ExecuteAttackRoutine(Action triggerKick, Action triggerDefense, Action resolveImpact)
        {
            triggerKick?.Invoke();
            yield return new WaitForSeconds(0.06f);
            triggerDefense?.Invoke();
            yield return new WaitForSeconds(0.16f);
            resolveImpact?.Invoke();
            yield return new WaitForSeconds(0.65f);
            _ctx.Guard.ForceReset();
        }

        public IEnumerator ExecuteAttackWithPlayerDefenseRoutine(
            TacticalUnit attacker,
            TacticalUnit defender,
            BodyPart targetedPart,
            bool cancelPenaltyWithAP,
            SkillType attackSkill,
            SkillType defenseSkill,
            int weaponBaseDamage,
            int attackerBonusAP,
            int attackerPE,
            string attackerSpecialization,
            string defenderSpecialization,
            Action triggerKick,
            Action triggerDefense)
        {
            var grid = _ctx.Grid;
            var visualizer = _ctx.Visualizer;
            var calculator = _ctx.Calculator;
            _ctx.Guard.TryEnter();

            int effectiveWeaponDamage = weaponBaseDamage;
            var equippedWeapon = attacker.Sheet?.GetEquippedWeapon();
            if (equippedWeapon != null && equippedWeapon.BaseDamage > 0)
            {
                effectiveWeaponDamage = equippedWeapon.BaseDamage;
            }
            int weaponBonusEc2 = equippedWeapon != null ? Math.Max(0, equippedWeapon.AttackBonusEc) : 0;
            // RD-033 : charge Haute Densité chambrée = +1 dégât (sniper/Deglazer).
            bool hdShot2 = WeaponAmmo.UsesAmmo(equippedWeapon, attackSkill) && equippedWeapon.LoadedHD;
            if (hdShot2) effectiveWeaponDamage += 1;

            calculator.SetContactDistanceState(attacker.IsCanonEntrave());

            // Duel aveugle : Begin (base attaque) puis DÉCLARATION attaquant immédiate
            // (mise cachée, aucun jet lancé). La fenêtre défenseur s'ouvre ensuite en
            // aveugle : le défenseur ne voit ni jet ni mise adverse.
            // Livre VI §25.3 : couvert évalué sur positions réelles (Full => refus).
            // RD-036 : ruling hauteur recalculé sur positions réelles (même gate
            // que ExecuteAttack — la routine n'est atteinte qu'après le gate, le
            // mod suffit ici).
            CoverType defenseCover = _targeting.GetCoverToTarget(attacker, defender);
            bool isMeleeHere = SkillDefinitions.IsMeleeAttackSkill(attackSkill)
                || (equippedWeapon != null && equippedWeapon.RangeInTiles <= 1);
            ElevationRuling defenseRuling = _targeting.GetElevationRuling(attacker, defender, attackSkill, equippedWeapon, isMeleeHere);
            var duel = calculator.BeginSkillDuel(
                attacker.Stats, defender.Stats, targetedPart,
                attackSkill, defenseSkill, effectiveWeaponDamage, cancelPenaltyWithAP,
                false, attackerSpecialization, defenderSpecialization,
                defender.Stats.BaseArmorAbsorption, out string duelError, defenseCover, weaponBonusEc2,
                false, defenseRuling.AttackMod, defenseRuling.Label);

            if (duel == null)
            {
                _host.ArenaLog(duelError ?? "⚠️ Attaque impossible : PA insuffisants pour le coût de base.");
                _ctx.Guard.ForceReset();
                yield break;
            }

            calculator.DeclareAttackerStakes(duel, attackerBonusAP, attackerPE);

            bool playerDecided = false;
            SkillType finalDefSkill = defenseSkill;
            bool finalWantsDefend = true;
            int finalBonusAP = 0;
            int finalPE = 0;

            // La défense se paramètre dans le menu contextuel radial, centré sur le
            // défenseur comme après un clic droit sur lui (aucun modal séparé).
            var menu = CombatUI.CombatContextMenuUI.EnsureInstance();
            menu.OpenDefenseMenu(duel, attacker, defender, (chosenSkill, wantsDef, bonusAP, pe) =>
            {
                if (menu.InterceptedBy != null)
                {
                    defender = menu.InterceptedBy;
                }
                finalDefSkill = chosenSkill;
                finalWantsDefend = wantsDef;
                finalBonusAP = bonusAP;
                finalPE = pe;
                playerDecided = true;
            });

            while (!playerDecided)
            {
                // Garde-fou : si le menu de défense a disparu sans décision (changement
                // de scène, unité détruite...), on résout en encaissement passif plutôt
                // que de bloquer toutes les attaques suivantes pour toujours.
                if (!CombatUI.CombatContextMenuUI.IsDefenseOpen)
                {
                    finalDefSkill = defenseSkill;
                    finalWantsDefend = false;
                    finalBonusAP = 0;
                    finalPE = 0;
                    playerDecided = true;
                    _host.ArenaLog($"🛡️ <b>{defender.Stats.Name}</b> ne répond pas : encaissement passif (0 PA).");
                    break;
                }
                yield return null;
            }

            if (menu.InterceptedBy != null)
            {
                defender = menu.InterceptedBy;
            }

            // Déclaration aveugle du défenseur (mise débitée, rien révélé).
            calculator.DeclareDefenderStakes(duel, finalDefSkill, finalWantsDefend, finalBonusAP, finalPE, defenderSpecialization);

            // RÉVÉLATION SIMULTANÉE : les deux dés sont lancés ensemble.
            DamageResult result = calculator.ResolveBlindDuel(duel);

            triggerKick?.Invoke();
            yield return new WaitForSeconds(0.06f);
            if (finalWantsDefend)
            {
                var curDefVisual = defender != null ? defender.GetComponent<TacticalUnitVisual>() : null;
                if (defender != null && defender.Stats != null && defender.Stats.CanDefendActively())
                {
                    curDefVisual?.TriggerBodyBlock();
                    if (KilltimeAudioManager.Instance != null)
                        KilltimeAudioManager.Instance.PlayAt(SoundId.Defense_Block, defender.transform.position, 0.5f);
                }
            }
            yield return new WaitForSeconds(0.16f);

            attacker.Stats.RegisterAttack();
            _host.ArenaLog(result.CombatLog);
            _overwatch.HandleChannelInterruptionFx(defender, result.ChannelInterrupted);

            // Dégâts collatéraux sur l'obstacle si l'attaque est absorbée par le couvert ou neutralisée
            if (result.IsBlocked && defenseCover != CoverType.None && grid != null)
            {
                ObstacleDamageType directDmgType = ObstacleDamageType.Perforant;
                if (isMeleeHere)
                {
                    directDmgType = (attackSkill == SkillType.ManiementArmes && attacker.Stats.HasSpecialization("Marteau de Guerre"))
                        ? ObstacleDamageType.Contondant
                        : (attacker.Stats.HasSpecialization("Hache de Guerre") ? ObstacleDamageType.Tranchant : ObstacleDamageType.Contondant);
                }
                else if (equippedWeapon != null && equippedWeapon.Category.Contains("Laser"))
                {
                    directDmgType = ObstacleDamageType.Thermique;
                }

                if (CoverSystem.ScanCover(attacker.CurrentCoords, defender.CurrentCoords, grid, smokeZones: _state.SmokeZones, seesThroughSmoke: ArenaSmokeService.SeesThroughSmoke(attacker)).Rays is var rays && rays != null)
                {
                    for (int rIdx = 0; rIdx < rays.Count; rIdx++)
                    {
                        if (rays[rIdx].Blocked && rays[rIdx].HasBlockingCell)
                        {
                            if (PropObstacleRegistry.ApplyDamage(rays[rIdx].BlockingCell, effectiveWeaponDamage, directDmgType, grid, visualizer, out var obsRes))
                            {
                                _host.ArenaLog($"   🛡️ <b>IMPACT COLLATÉRAL</b> : {obsRes.Log}");
                            }
                            break;
                        }
                    }
                }
            }

            _melee.TryDisarmDefender(attacker, defender, targetedPart, result);
            if (WeaponAmmo.UsesAmmo(equippedWeapon, attackSkill) && equippedWeapon != null && result.AttackRoll.RawRoll > 0)
                _melee.ConsumeShotAndCheckJam(attacker, equippedWeapon, result, hdShot2);

            var laserWeapon = attacker.GetComponentInChildren<LaserRifleWeapon>();
            bool skillIsMelee = SkillDefinitions.IsMeleeAttackSkill(attackSkill);
            int freshDistAtImpact = (attacker != null && defender != null) ? attacker.CurrentCoords.DistanceTo(defender.CurrentCoords) : 1;
            bool isMeleeStrike = skillIsMelee && freshDistAtImpact <= 1;
            if (attackSkill == SkillType.Ballistique && !isMeleeStrike && laserWeapon != null && defender != null)
            {
                Vector3 targetCenter = defender.transform.position + Vector3.up * 1.15f;
                laserWeapon.FireLaser(targetCenter, result.IsHit);
            }

            bool isTargetDeadOrDown = !defender.Stats.IsAlive || result.FatalResolution != FatalBlowResolution.None;
            CombatHUD.RecordCombatAction(
                attacker.Stats.Name,
                defender.Stats.Name,
                attacker.IsPlayerControlled,
                defender.IsPlayerControlled,
                result.IsHit,
                result.IsCritical,
                result.IsBlocked,
                result.RawDamage,
                result.ArmorAbsorbed,
                result.FinalDamageApplied,
                result.ExceededEncaissement,
                isTargetDeadOrDown,
                (cancelPenaltyWithAP ? 3 : 2) + attackerBonusAP
            );

            if (KilltimeAudioManager.Instance != null && defender != null)
            {
                KilltimeAudioManager.Instance.PlayCombatResult(result, defender.transform.position);
                KilltimeAudioManager.Instance.Play(SoundId.Dice_Roll, 0.35f);
            }

            var attVisual = attacker.GetComponent<TacticalUnitVisual>();
            var defVisual = defender.GetComponent<TacticalUnitVisual>();

            if (attVisual != null)
            {
                int totalCost = (cancelPenaltyWithAP ? 3 : 2) + attackerBonusAP;
                attVisual.SpawnFloatingText($"Attaque {targetedPart} (-{totalCost} PA)", new Color(0.3f, 0.8f, 1.0f));
            }

            if (defVisual != null)
            {
                if (result.IsHit)
                {
                    defVisual.TriggerHitFlash();

                    if (result.IsCritical)
                    {
                        defVisual.SpawnFloatingText("[CRITIQUE] COUP DÉCISIF !", new Color(1.0f, 0.85f, 0.1f));
                    }

                    if (result.WasDeflected)
                    {
                        defVisual.SpawnFloatingText($"[DÉVIATION] -> {BodyPartInfo.GetInfo(result.ActualHitPart).DisplayName} (Diff 0)", Color.yellow);
                    }
                    else
                    {
                        string diffSign = result.Differential > 0 ? $"+{result.Differential}" : $"{result.Differential}";
                        defVisual.SpawnFloatingText($"[TOUCHÉ] {BodyPartInfo.GetInfo(result.ActualHitPart).DisplayName} (Diff {diffSign})", new Color(0.9f, 0.9f, 1.0f));
                    }

                    string dmgText = (result.ArmorAbsorbed > 0 || result.ShieldAbsorbed > 0)
                        ? $"-{result.FinalDamageApplied} PV (Bruts {result.RawDamage} | Armure -{result.ArmorAbsorbed}" + (result.ShieldAbsorbed > 0 ? $" | Bouclier -{result.ShieldAbsorbed}" : "") + ")"
                        : $"-{result.FinalDamageApplied} PV";
                    defVisual.SpawnFloatingText(dmgText, new Color(1.0f, 0.25f, 0.25f));

                    if (result.ExceededEncaissement)
                    {
                        defVisual.SpawnFloatingText($"[CHOC] TRAUMATIQUE ! [{result.InflictedStatus}]", new Color(1.0f, 0.4f, 0.95f));
                    }

                    if (result.CausedKnockback)
                    {
                        _melee.TryApplyKnockback(attacker, defender);
                    }

                    if (result.MoraleResult.Triggered)
                    {
                        if (result.MoraleResult.Surrendered)
                        {
                            defVisual.SpawnFloatingText("🏳️ REDDITION !", Color.white);
                            defVisual.DropHeldItemsWithPhysics();
                        }
                        else if (result.MoraleResult.Routed)
                        {
                            defVisual.SpawnFloatingText("😱 DÉROUTE ! [Agonisant]", new Color(0.95f, 0.2f, 0.85f));
                        }
                        else
                        {
                            defVisual.SpawnFloatingText("🧠 Sang-froid préservé", Color.cyan);
                        }
                    }

                    if (result.FatalResolution == FatalBlowResolution.InstantDeath)
                    {
                        defVisual.SpawnFloatingText("[MORTEL] INSTANTANÉ !", Color.black);
                        defVisual.TriggerFallingBackDeath();
                    }
                    else if (result.FatalResolution == FatalBlowResolution.MiracleSaved)
                    {
                        defVisual.SpawnFloatingText("[MIRACLE] DESTIN SAUVÉ ! (1 PV)", new Color(1.0f, 0.85f, 0.1f));
                        defVisual.TriggerFallingBackDeath();
                    }
                    else if (result.FatalResolution == FatalBlowResolution.ForcedUnconscious)
                    {
                        defVisual.SpawnFloatingText("[K.O.] SYNCOPE TRAUMATIQUE !", Color.magenta);
                        defVisual.TriggerFallingBackDeath();
                    }
                    else if (result.FatalResolution == FatalBlowResolution.EligibleForLastBreath)
                    {
                        if (!defender.IsPlayerControlled)
                        {
                            defender.Stats.ChooseSombrer();
                            defVisual.SpawnFloatingText("[SYNCOPE] CHUTE HORS COMBAT (0 PV)", Color.cyan);
                            defVisual.TriggerFallingBackDeath();
                        }
                        else
                        {
                            defender.Stats.ChooseLastBreath();
                            defVisual.SpawnFloatingText("[SURVIE] DERNIER SOUFFLE (0 PV)", new Color(1.0f, 0.5f, 0.1f));
                        }
                    }
                }
                else if (result.IsBlocked)
                {
                    defVisual.SpawnFloatingText($"[PARADE] Neutralisation (Diff {result.Differential})", new Color(0.2f, 0.9f, 1.0f));
                }
            }

            WebBridgeManager.Instance?.SendCombatEvent(
                result.IsHit ? "HIT" : "MISS",
                result.CombatLog,
                result.FinalDamageApplied
            );

            SnapshotRecorder?.Invoke($"Attaque sur {defender.Stats.Name} ({targetedPart})");

            if (!defender.Stats.IsAlive)
            {
                _host.RequestAutoTargetNextAlive();
            }

            _host.RequestMusicUpdate();
            _host.RequestCombatOverCheck();

            yield return new WaitForSeconds(0.55f);
            _ctx.Guard.ForceReset();
        }
    }
}

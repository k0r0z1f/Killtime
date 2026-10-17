using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics;
using Killtime.Tactics.AI;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Units;
using Killtime.Tactics.TurnSystem;
using Killtime.Core.Combat;
using Killtime.Core.Dice;
using Killtime.Core.Character;
using Killtime.CameraSystem;
using Killtime.Audio;
using Killtime.UI;

namespace Killtime.Multi
{
    public partial class VTTTableSync : MonoBehaviour
    {
        private IEnumerator ExecuteRemoteAttackRoutine(VTTCombatActionPayload p)
        {
            _applyingRemote = true;
            try
            {
                var attacker = FindUnit(p.actorId);
                var defender = FindUnit(p.targetId);

                if (attacker == null || defender == null)
                {
                    Debug.LogWarning($"[VTT] Attaque distante ignorée : attaquant '{p.actorId}' ou défenseur '{p.targetId}' introuvable.");
                    yield break;
                }

                if (_arena != null)
                {
                    _arena.SelectTarget(defender);
                }

                Vector3 combatDir = defender.transform.position - attacker.transform.position;
                combatDir.y = 0f;
                if (combatDir != Vector3.zero)
                {
                    attacker.transform.rotation = Quaternion.LookRotation(combatDir);
                    if (defender.Stats != null && defender.Stats.CanDefendActively())
                    {
                        defender.transform.rotation = Quaternion.LookRotation(-combatDir);
                    }
                }

                var attVisual = attacker.GetComponent<TacticalUnitVisual>();
                var defVisual = defender.GetComponent<TacticalUnitVisual>();
                var laserWeapon = attacker.GetComponentInChildren<LaserRifleWeapon>();

                Action onActionStart = () =>
                {
                    attVisual?.TriggerRoundkick(p.isMeleeStrike);

                    if (!p.isMeleeStrike && laserWeapon != null)
                    {
                        Vector3 targetCenter = defender.transform.position + Vector3.up * 1.15f;
                        laserWeapon.FireLaser(targetCenter, p.isHit);
                    }

                    if (KilltimeAudioManager.Instance != null)
                    {
                        KilltimeAudioManager.Instance.PlayAt(SoundId.Attack_Whoosh, attacker.transform.position, 0.7f);
                    }

                    string partLabel = BodyPartInfo.GetInfo((BodyPart)p.targetedPart).DisplayName;
                    attVisual?.SpawnFloatingText($"Attaque {partLabel} (-{p.attackerCostAP} PA)", new Color(0.3f, 0.8f, 1.0f));
                };

                Action onDefenseStart = () =>
                {
                    if (defender.Stats != null && defender.Stats.CanDefendActively())
                    {
                        defVisual?.TriggerBodyBlock();
                    }
                };

                Action onStrikePoint = () =>
                {
                    if (KilltimeAudioManager.Instance != null)
                    {
                        KilltimeAudioManager.Instance.PlayCombatResult(
                            p.isHit, !p.isHit, p.isCritical,
                            p.armorAbsorbed > 0, false, "None", defender.transform.position);
                        KilltimeAudioManager.Instance.Play(SoundId.Dice_Roll, 0.35f);
                    }

                    if (defVisual != null)
                    {
                        if (p.isHit)
                        {
                            defVisual.TriggerHitFlash();
                            if (p.isCritical)
                            {
                                defVisual.SpawnFloatingText("[CRITIQUE] COUP DÉCISIF !", new Color(1.0f, 0.85f, 0.1f));
                            }

                            if (p.wasDeflected)
                            {
                                string deflectedPart = BodyPartInfo.GetInfo((BodyPart)p.actualHitPart).DisplayName;
                                defVisual.SpawnFloatingText($"[DÉVIATION] -> {deflectedPart} (Diff 0)", Color.yellow);
                            }
                            else
                            {
                                string diffSign = p.differential > 0 ? $"+{p.differential}" : $"{p.differential}";
                                string hitPart = BodyPartInfo.GetInfo((BodyPart)p.actualHitPart).DisplayName;
                                defVisual.SpawnFloatingText($"[TOUCHÉ] {hitPart} (Diff {diffSign})", new Color(0.9f, 0.9f, 1.0f));
                            }

                            string dmgText = p.armorAbsorbed > 0
                                ? $"-{p.finalDamageApplied} PV (Bruts {p.rawDamage} | Armure -{p.armorAbsorbed})"
                                : $"-{p.finalDamageApplied} PV";
                            defVisual.SpawnFloatingText(dmgText, new Color(1.0f, 0.25f, 0.25f));

                            if (!string.IsNullOrEmpty(p.inflictedStatus))
                            {
                                defVisual.SpawnFloatingText($"[CHOC] TRAUMATIQUE ! [{p.inflictedStatus}]", new Color(1.0f, 0.4f, 0.95f));
                            }
                        }
                        else
                        {
                            defVisual.TriggerBodyBlock();
                            defVisual.SpawnFloatingText("🛡️ Parade / Esquive réussie", Color.cyan);
                        }
                    }

                    if (p.actorRotationY != 0f)
                    {
                        attacker.transform.rotation = Quaternion.Euler(0f, p.actorRotationY, 0f);
                    }
                    if (p.defenderRotationY != 0f)
                    {
                        defender.transform.rotation = Quaternion.Euler(0f, p.defenderRotationY, 0f);
                    }

                    if (p.defenderNewQ != 0 || p.defenderNewR != 0)
                    {
                        var targetCoords = new HexCoordinates(p.defenderNewQ, p.defenderNewR);
                        var currentGrid = _grid != null ? _grid : FindAnyObjectByType<TacticalHexGrid>();
                        if (currentGrid != null && !defender.CurrentCoords.Equals(targetCoords))
                        {
                            defender.TeleportTo(targetCoords, currentGrid);
                        }
                    }

                    if (attacker.Stats != null)
                    {
                        attacker.Stats.CurrentActionPoints = p.attackerNewAP;
                        attacker.Stats.RegisterAttack();
                    }

                    if (defender.Stats != null)
                    {
                        defender.Stats.CurrentActionPoints = p.defenderNewAP;
                        defender.Stats.CurrentHealth = p.defenderNewHealth;

                        if (p.defenderNewActiveStatus != 0)
                        {
                            defender.Stats.ActiveStatus = (StatusEffect)p.defenderNewActiveStatus;
                        }

                        bool shouldDie = p.defenderIsDead || p.defenderNewHealth <= 0 || defender.Stats.ActiveStatus.HasFlag(StatusEffect.Inconscient);
                        if (shouldDie)
                        {
                            defender.Stats.IsDead = p.defenderIsDead || p.defenderNewHealth <= 0;
                            if (p.defenderNewHealth <= 0) defender.Stats.CurrentHealth = 0;
                            defender.Stats.ActiveStatus |= StatusEffect.Inconscient | StatusEffect.ATerre;

                            defVisual?.SpawnFloatingText("[HORS DE COMBAT]", Color.red);
                            defVisual?.TriggerFallingBackDeath();
                        }
                    }

                    bool isFatalRemote = p.defenderIsDead || p.defenderNewHealth <= 0;
                    CombatHUD.RecordCombatAction(
                        attacker.Stats != null ? attacker.Stats.Name : p.actorId,
                        defender.Stats != null ? defender.Stats.Name : p.targetId,
                        attacker.IsPlayerControlled,
                        defender.IsPlayerControlled,
                        p.isHit,
                        p.isCritical,
                        !p.isHit,
                        p.rawDamage,
                        p.armorAbsorbed,
                        p.finalDamageApplied,
                        !string.IsNullOrEmpty(p.inflictedStatus),
                        isFatalRemote,
                        p.attackerCostAP
                    );

                    if (!string.IsNullOrEmpty(p.combatLog))
                    {
                        _hud?.AddCombatLog(p.combatLog);
                        _arena?.Log(p.combatLog);
                    }
                };

                bool fastForward = _teleportRemoteMoves || _actionQueue.Count > 3 || CombatHUD.IsPaused;
                bool freeLookActive = _cinematicDirector != null && _cinematicDirector.IsFreeLook;
                bool allowCinematic = !fastForward && _cinematicDirector != null && !freeLookActive 
                                      && (_arena == null || _arena.EnableCinematicKillcam);

                if (allowCinematic)
                {
                    bool cinematicDone = false;
                    _cinematicDirector.PlayCinematicKillshot(
                        attacker.transform,
                        defender.transform,
                        onStrikePoint: onStrikePoint,
                        onComplete: () => { cinematicDone = true; },
                        onActionStart: onActionStart,
                        onDefenseStart: onDefenseStart
                    );

                    float safetyTimeout = 5.0f;
                    while (!cinematicDone && safetyTimeout > 0f)
                    {
                        safetyTimeout -= Time.unscaledDeltaTime;
                        yield return null;
                    }
                }
                else
                {
                    onActionStart();
                    if (!fastForward) yield return new WaitForSeconds(0.06f);
                    onDefenseStart();
                    if (!fastForward) yield return new WaitForSeconds(0.16f);
                    onStrikePoint();
                    if (!fastForward) yield return new WaitForSeconds(0.35f);
                }
            }
            finally
            {
                _applyingRemote = false;
            }
        }

        private IEnumerator ExecuteRemoteGrenadeRoutine(VTTCombatActionPayload p)
        {
            _applyingRemote = true;
            try
            {
                var attacker = FindUnit(p.actorId);
                var attVisual = attacker?.GetComponent<TacticalUnitVisual>();
                attVisual?.SpawnFloatingText(p.useLauncher ? "💣 Tir Lance-Grenades" : "💣 Lancer de Grenade", new Color(1f, 0.6f, 0.15f));

                if (attacker?.Stats != null)
                {
                    attacker.Stats.ConsumeActionPoints(p.attackerCostAP);
                }

                if (KilltimeAudioManager.Instance != null && attacker != null)
                {
                    KilltimeAudioManager.Instance.PlayAt(SoundId.Attack_Whoosh, attacker.transform.position, 0.8f);
                }

                yield return new WaitForSeconds(0.4f);

                if (!string.IsNullOrEmpty(p.combatLog))
                {
                    _hud?.AddCombatLog(p.combatLog);
                    _arena?.Log(p.combatLog);
                }

                yield return new WaitForSeconds(0.2f);
            }
            finally
            {
                _applyingRemote = false;
            }
        }

        private IEnumerator ExecuteRemoteSpellRoutine(VTTCombatActionPayload p)
        {
            _applyingRemote = true;
            try
            {
                var actor = FindUnit(p.actorId);
                var target = FindUnit(p.targetId);
                var actorVis = actor?.GetComponent<TacticalUnitVisual>();
                var targetVis = target?.GetComponent<TacticalUnitVisual>();

                actorVis?.SpawnFloatingText($"🔮 {p.spellName} (-{p.apCost} PA)", Color.cyan);
                yield return new WaitForSeconds(0.3f);

                targetVis?.TriggerHitFlash();
                targetVis?.SpawnFloatingText($"-{p.spellDamage} Arcanique", Color.magenta);

                if (target?.Stats != null)
                {
                    target.Stats.CurrentHealth = p.defenderNewHealth;
                }
                if (actor?.Stats != null)
                {
                    actor.Stats.CurrentActionPoints = p.attackerNewAP;
                }

                yield return new WaitForSeconds(0.2f);
            }
            finally
            {
                _applyingRemote = false;
            }
        }

        private IEnumerator ExecuteRemoteBreathRoutine(VTTCombatActionPayload p)
        {
            _applyingRemote = true;
            try
            {
                var unit = FindUnit(p.actorId);
                var vis = unit?.GetComponent<TacticalUnitVisual>();
                vis?.SpawnFloatingText($"🫁 Souffle d'Urgence (+{p.breathGainAP} PA)", Color.yellow);
                if (unit?.Stats != null)
                {
                    unit.Stats.CurrentActionPoints = p.attackerNewAP;
                }
                yield return new WaitForSeconds(0.2f);
            }
            finally
            {
                _applyingRemote = false;
            }
        }
    }
}

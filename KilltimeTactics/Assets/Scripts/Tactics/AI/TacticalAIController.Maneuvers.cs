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
        private bool TryExecuteAllySupport(TacticalUnit actor, TacticalUnitVisual visual)
        {
            if (actor?.Stats == null || !actor.Stats.IsAlive) return false;
            if (_defaultPersonality == AIPersonality.Aggressive) return false;

            for (int i = 0; i < _cachedUnits.Count; i++)
            {
                var ally = _cachedUnits[i];
                if (ally == null || ally == actor || ally.Stats == null) continue;
                if (ally.IsPlayerControlled != actor.IsPlayerControlled) continue;

                int d = TitanFootprint.MinDistanceBetweenUnits(actor.CurrentCoords, actor.FootprintType, ally.CurrentCoords, ally.FootprintType);
                int pa = actor.Stats.CurrentActionPoints;
                var tgtVis = ally.GetComponent<TacticalUnitVisual>();

                // Corde : Relever un allié À Terre (1 PA, contact).
                if (d <= 1 && pa >= 1 && ally.Stats.IsAlive
                    && ally.Stats.ActiveStatus.HasFlag(StatusEffect.ATerre)
                    && SmokeScreen.HasItemByName(actor.Stats, "Corde"))
                {
                    if (!actor.Stats.ConsumeActionPoints(1)) return false;
                    ally.Stats.RemoveStatus(StatusEffect.ATerre);
                    visual?.SpawnFloatingText("🪢 Allié relevé (-1 PA)", Color.green);
                    tgtVis?.SpawnFloatingText("🪢 RELEVÉ", Color.green);
                    _arena?.Log($"🪢 <b>{actor.Stats.Name}</b> relève <b>{ally.Stats.Name}</b> à la corde.");
                    return true;
                }

                // Clé / Crochetage : Libérer un allié entravé (1 PA, contact).
                if (d <= 1 && pa >= 1 && ally.Stats.IsAlive
                    && ally.Stats.ActiveStatus.HasFlag(StatusEffect.Immobilise)
                    && (SmokeScreen.HasItemByName(actor.Stats, "Clé Magnétique") || SmokeScreen.HasItemByName(actor.Stats, "Crochetage")))
                {
                    if (!actor.Stats.ConsumeActionPoints(1)) return false;
                    ally.Stats.RemoveStatus(StatusEffect.Immobilise);
                    visual?.SpawnFloatingText("🔓 Allié libéré (-1 PA)", Color.green);
                    tgtVis?.SpawnFloatingText("🔓 LIBÉRÉ", Color.green);
                    _arena?.Log($"🔓 <b>{actor.Stats.Name}</b> libère <b>{ally.Stats.Name}</b> de ses entraves.");
                    return true;
                }

                // Traînée d'évacuation (2 PA, contact : immobilisé / à terre / inconscient).
                if (d <= 1 && pa >= 2 && _arena != null
                    && (ally.Stats.ActiveStatus.HasFlag(StatusEffect.Immobilise)
                        || ally.Stats.ActiveStatus.HasFlag(StatusEffect.Inconscient)
                        || ally.Stats.ActiveStatus.HasFlag(StatusEffect.ATerre))
                    && _arena.CanDragBody(actor, ally))
                {
                    _arena.ExecuteDragBody(actor, ally);
                    return true;
                }

                // Consommable de soin sur allié au contact (1 PA).
                if (d <= 1 && pa >= 1 && ally.Stats.IsAlive
                    && (float)ally.Stats.CurrentHealth / Mathf.Max(1, ally.Stats.MaxHealth) <= 0.5f)
                {
                    var dose = AIFindBestHeal(actor);
                    if (dose != null)
                    {
                        if (!actor.Stats.ConsumeActionPoints(1)) return false;
                        string usedName = dose.Name;
                        int usedHeal = dose.HealingAmount;
                        bool usedBandage = usedName.IndexOf("Bandage", StringComparison.OrdinalIgnoreCase) >= 0;
                        actor.Sheet?.ConsumeOne(dose.ItemId);
                        actor.NotifyInventoryChanged(true);
                        int healed = ally.Stats.Heal(usedHeal);
                        if (usedBandage) ally.Stats.RemoveStatus(StatusEffect.Saignement);
                        visual?.SpawnFloatingText($"💉 {usedName} (-1 PA)", Color.green);
                        tgtVis?.SpawnFloatingText($"+{healed} PV", Color.green);
                        _arena?.Log($"💉 <b>{actor.Stats.Name}</b> utilise <b>{usedName}</b> sur <b>{ally.Stats.Name}</b> (+{healed} PV).");
                        return true;
                    }
                }

                // Radio : Ralliement à distance (1 PA, ≤12, Déstabilisé/Étourdi).
                if (d <= 12 && pa >= 1 && ally.Stats.IsAlive
                    && (ally.Stats.ActiveStatus.HasFlag(StatusEffect.Destabilise) || ally.Stats.ActiveStatus.HasFlag(StatusEffect.Etourdi))
                    && SmokeScreen.HasItemByName(actor.Stats, "Radio"))
                {
                    if (!actor.Stats.ConsumeActionPoints(1)) return false;
                    ally.Stats.RemoveStatus(StatusEffect.Destabilise);
                    ally.Stats.RemoveStatus(StatusEffect.Etourdi);
                    visual?.SpawnFloatingText("📻 Ralliement (-1 PA)", Color.cyan);
                    tgtVis?.SpawnFloatingText("📻 RALLIÉ", Color.cyan);
                    _arena?.Log($"📻 <b>{actor.Stats.Name}</b> rallie <b>{ally.Stats.Name}</b> par radio.");
                    return true;
                }

                // Boîte à Outils : maintenance sur allié (1 PA, contact).
                if (d <= 1 && pa >= 1 && ally.Stats.IsAlive && _arena != null
                    && SmokeScreen.HasItemByName(actor.Stats, "Boîte à Outils")
                    && (ally.Sheet?.GetEquippedWeapon()?.Jammed == true
                        || (ally.Stats.MaxShieldHP > 0 && ally.Stats.CurrentShieldHP < ally.Stats.MaxShieldHP)))
                {
                    if (!actor.Stats.ConsumeActionPoints(1)) return false;
                    var tgtWeapon = ally.Sheet?.GetEquippedWeapon();
                    if (tgtWeapon != null && tgtWeapon.Jammed)
                    {
                        tgtWeapon.Jammed = false;
                        ally.NotifyInventoryChanged(true);
                        tgtVis?.SpawnFloatingText("🔧 DÉSENRAYÉE", Color.cyan);
                        _arena.Log($"🔧 <b>{actor.Stats.Name}</b> désenraye <b>{tgtWeapon.Name}</b> de <b>{ally.Stats.Name}</b> (-1 PA).");
                    }
                    else
                    {
                        int before = ally.Stats.CurrentShieldHP;
                        ally.Stats.CurrentShieldHP = Mathf.Min(ally.Stats.MaxShieldHP, ally.Stats.CurrentShieldHP + 10);
                        tgtVis?.SpawnFloatingText($"🔧 +{ally.Stats.CurrentShieldHP - before} Bouclier", Color.cyan);
                        _arena.Log($"🔧 <b>{actor.Stats.Name}</b> répare le champ de <b>{ally.Stats.Name}</b> (-1 PA).");
                    }
                    return true;
                }

                // Garde du corps (0 PA, contact) sur allié fragile.
                if (d <= 1 && ally.Stats.IsAlive
                    && !SkillTechniqueState.IsGuardedBy(ally.Stats, actor.Stats)
                    && ((float)ally.Stats.CurrentHealth / Mathf.Max(1, ally.Stats.MaxHealth) <= 0.4f
                        || ally.Stats.ActiveStatus.HasFlag(StatusEffect.Destabilise))
                    && Killtime.Tactics.CombatUI.CombatTechniqueRegistry.CanSetBodyguard(actor, ally))
                {
                    if (Killtime.Tactics.CombatUI.CombatTechniqueRegistry.ExecuteSetBodyguard(actor, ally, _arena))
                        return true;
                }
            }

            return false;
        }

        // =========================================================================
        // ENTRETIEN PERSONNEL (bouclier, soins, dopants, arme, canalisation,
        // furtivité, souffle) — registre joueur, une action par itération.
        // =========================================================================
        private bool TryExecuteSelfUpkeep(TacticalUnit actor, TacticalPosture posture, int dist, bool inAttackRange, TacticalUnitVisual visual)
        {
            if (actor?.Stats == null || !actor.Stats.IsAlive) return false;
            int pa = actor.Stats.CurrentActionPoints;
            int reserve = GetRequiredDefensiveReserve(actor, posture);

            // 0 PA : Seconde Respiration (1x/combat, -2 ESS).
            if (actor.Stats.Essoufflement > 0 && !actor.Stats.HasUsedSecondeRespiration
                && actor.Stats.HasSpecialization("Seconde Respiration"))
            {
                actor.Stats.HasUsedSecondeRespiration = true;
                actor.Stats.RecoverBreath(2);
                visual?.SpawnFloatingText("Seconde Respiration (-2 ESS)", Color.cyan);
                _arena?.Log($"🌬️ <b>{actor.Stats.Name}</b> déclenche sa <b>Seconde Respiration</b> (-2 ESS, 0 PA) !");
                return true;
            }

            // Garde du corps (0 PA) : traitée côté alliés, rappelée ici pour les
            // tours où aucun soin n'est dû mais un allié reste exposé.
            if (TryExecuteAllySupport(actor, visual)) return true;

            // Arme : ramasser au sol ou équiper un remplaçant si désarmé.
            var equipped = actor.Sheet?.GetEquippedWeapon();
            if (equipped == null && pa >= DroppedWeaponPickup.PickupCostPA())
            {
                if (DroppedWeaponPickup.TryPickupNearest(actor, out string pickupMsg))
                {
                    _arena?.Log($"⚔ <b>{actor.Stats.Name}</b> : {pickupMsg}");
                    return true;
                }
                if (actor.Sheet?.Inventory != null)
                {
                    for (int i = 0; i < actor.Sheet.Inventory.Count; i++)
                    {
                        var cand = actor.Sheet.Inventory[i];
                        if (cand == null || cand.Type != ItemType.Weapon || cand.IsEquipped) continue;
                        if (cand.IsThrowableGrenade() || cand.IsLauncher) continue;
                        int cost = DroppedWeaponPickup.PickupCostPA();
                        if (cost > 0 && !actor.Stats.ConsumeActionPoints(cost)) return false;
                        if (actor.Sheet.EquipItem(cand.ItemId))
                        {
                            actor.NotifyInventoryChanged(true);
                            visual?.SpawnFloatingText($"🔄 {cand.Name} équipée", Color.cyan);
                            _arena?.Log($"🔄 <b>{actor.Stats.Name}</b> équipe <b>{cand.Name}</b>.");
                            return true;
                        }
                    }
                }
            }

            if (pa < 1) return false;

            // Bouclier : cellule nytharite via l'arène, sinon boîte/moteur.
            if (actor.Stats.MaxShieldHP > 0 && actor.Stats.CurrentShieldHP < actor.Stats.MaxShieldHP && _arena != null)
            {
                if (SmokeScreen.HasItemByName(actor.Stats, "Cellule Nytharite Standard"))
                {
                    if (_arena.TryRechargeShieldWithCell(actor, false, out string msg)) { _arena.Log(msg); return true; }
                }
                else if (SmokeScreen.HasItemByName(actor.Stats, "Cellule Nytharite Pure"))
                {
                    if (_arena.TryRechargeShieldWithCell(actor, true, out string msg)) { _arena.Log(msg); return true; }
                }
                else if (SmokeScreen.HasItemByName(actor.Stats, "Boîte à Outils"))
                {
                    if (!actor.Stats.ConsumeActionPoints(1)) return false;
                    int before = actor.Stats.CurrentShieldHP;
                    actor.Stats.CurrentShieldHP = Mathf.Min(actor.Stats.MaxShieldHP, actor.Stats.CurrentShieldHP + 10);
                    visual?.SpawnFloatingText($"🔧 +{actor.Stats.CurrentShieldHP - before} Bouclier", Color.cyan);
                    _arena.Log($"🔧 <b>{actor.Stats.Name}</b> répare son champ (-1 PA).");
                    return true;
                }
                else if (SmokeScreen.HasItemByName(actor.Stats, "Moteur"))
                {
                    if (!actor.Stats.ConsumeActionPoints(1)) return false;
                    int before = actor.Stats.CurrentShieldHP;
                    actor.Stats.CurrentShieldHP = Mathf.Min(actor.Stats.MaxShieldHP, actor.Stats.CurrentShieldHP + 10);
                    visual?.SpawnFloatingText($"⚙️ +{actor.Stats.CurrentShieldHP - before} Bouclier", Color.cyan);
                    _arena.Log($"⚙️ <b>{actor.Stats.Name}</b> suralimente son champ (-1 PA).");
                    return true;
                }
            }

            // Soin personnel par consommable (≤50% PV).
            if ((float)actor.Stats.CurrentHealth / Mathf.Max(1, actor.Stats.MaxHealth) <= 0.5f)
            {
                var dose = AIFindBestHeal(actor);
                if (dose != null)
                {
                    if (!actor.Stats.ConsumeActionPoints(1)) return false;
                    string usedName = dose.Name;
                    int usedHeal = dose.HealingAmount;
                    bool usedBandage = usedName.IndexOf("Bandage", StringComparison.OrdinalIgnoreCase) >= 0;
                    actor.Sheet?.ConsumeOne(dose.ItemId);
                    actor.NotifyInventoryChanged(true);
                    int healed = actor.Stats.Heal(usedHeal);
                    if (usedBandage) actor.Stats.RemoveStatus(StatusEffect.Saignement);
                    visual?.SpawnFloatingText($"💉 {usedName} (+{healed} PV)", Color.green);
                    _arena?.Log($"💉 <b>{actor.Stats.Name}</b> utilise <b>{usedName}</b> (+{healed} PV).");
                    return true;
                }
            }

            // Attelle : retire Ralenti (fracture immobilisée).
            if (actor.Stats.ActiveStatus.HasFlag(StatusEffect.Ralenti) && AIFindByName(actor, "Attelle") != null)
            {
                var dose = AIFindByName(actor, "Attelle");
                if (!actor.Stats.ConsumeActionPoints(1)) return false;
                actor.Sheet?.ConsumeOne(dose.ItemId);
                actor.NotifyInventoryChanged(true);
                actor.Stats.RemoveStatus(StatusEffect.Ralenti);
                visual?.SpawnFloatingText("🦴 Fracture immobilisée", Color.green);
                _arena?.Log($"🦴 <b>{actor.Stats.Name}</b> pose une attelle (Ralenti retiré).");
                return true;
            }

            // Antidouleurs : +2 Encaissement, purge Étourdi/Déstabilisé.
            if ((actor.Stats.ActiveStatus.HasFlag(StatusEffect.Etourdi) || actor.Stats.ActiveStatus.HasFlag(StatusEffect.Destabilise))
                && AIFindByName(actor, "Antidouleur") != null)
            {
                var dose = AIFindByName(actor, "Antidouleur");
                if (!actor.Stats.ConsumeActionPoints(1)) return false;
                actor.Sheet?.ConsumeOne(dose.ItemId);
                actor.NotifyInventoryChanged(true);
                actor.Stats.AddEncaissementBonus(2);
                actor.Stats.RemoveStatus(StatusEffect.Etourdi);
                actor.Stats.RemoveStatus(StatusEffect.Destabilise);
                visual?.SpawnFloatingText("💊 Dopé (+2 Encaissement)", new Color(1f, 0.6f, 0.2f));
                _arena?.Log($"💊 <b>{actor.Stats.Name}</b> prend <b>{dose.Name}</b> (+2 Encaissement).");
                return true;
            }

            // Speed : 1 PA → +3 PA quand la réserve est basse.
            if (pa >= 1 && pa < 3 && AIFindByName(actor, "Speed") != null)
            {
                var dose = AIFindByName(actor, "Speed");
                if (!actor.Stats.ConsumeActionPoints(1)) return false;
                actor.Sheet?.ConsumeOne(dose.ItemId);
                actor.NotifyInventoryChanged(true);
                actor.Stats.CurrentActionPoints += 3;
                actor.Stats.ApplyStatus(StatusEffect.Rapide, 3);
                visual?.SpawnFloatingText("💨 SPEED (+3 PA)", Color.yellow);
                _arena?.Log($"💨 <b>{actor.Stats.Name}</b> prend <b>{dose.Name}</b> (+3 PA, Rapide).");
                return true;
            }

            // Harmonisation psi (Résonateur → Survolté +1 EC) avant sort ou tir EC.
            if (!actor.Stats.ActiveStatus.HasFlag(StatusEffect.Survolte)
                && SmokeScreen.HasItemByName(actor.Stats, "Résonateur")
                && (actor.Sheet?.LearnedSpells.Count > 0 || (actor.Sheet?.GetEquippedWeapon()?.AttackBonusEc ?? 0) > 0))
            {
                if (!actor.Stats.ConsumeActionPoints(1)) return false;
                actor.Stats.ApplyStatus(StatusEffect.Survolte, 1);
                visual?.SpawnFloatingText("🔮 SURVOLTÉ (+1 EC)", Color.magenta);
                _arena?.Log($"🔮 <b>{actor.Stats.Name}</b> s'harmonise avec son <b>Résonateur Nytharite</b> (+1 EC).");
                return true;
            }

            // Canaliser le coup (+2 au jet) quand la frappe suit derrière.
            if (inAttackRange && (_defaultPersonality == AIPersonality.Tactician || _defaultPersonality == AIPersonality.Balanced)
                && !ChannelingState.IsChanneling(actor.Stats)
                && !ChannelingState.IsCancelledByStatus(actor.Stats)
                && pa >= CoreRulesConfig.Instance.ChannelStartAPCost + 2 + reserve
                && _arena != null && _arena.TryBeginChanneling(actor, "Coup en progression", out _))
            {
                return true;
            }

            // Furtivité (2 PA, +2 embuscade) pour les doctrines prudentes à distance.
            if (!StealthState.IsStealthed(actor.Stats) && dist > 2
                && (_defaultPersonality == AIPersonality.Survivor || _defaultPersonality == AIPersonality.Tactician)
                && pa >= StealthState.EnterStealthAPCost + 2 + reserve
                && _arena != null && _arena.TryEnterStealth(actor, out _))
            {
                return true;
            }

            // Reprendre son souffle : 1 PA → -1 ESS (Second Souffle : -2).
            int ess = actor.Stats.Essoufflement;
            if (ess > 0 && ess >= Mathf.Max(1, actor.Stats.Attributes.Constitution / 2) && pa >= 2 + reserve)
            {
                if (!actor.Stats.ConsumeActionPoints(1)) return false;
                int recovered = actor.Stats.HasSpecialization("Course d'Endurance : Second Souffle") ? 2 : 1;
                actor.Stats.RecoverBreath(recovered);
                visual?.SpawnFloatingText($"Souffle repris (-{recovered} ESS)", Color.green);
                return true;
            }

            return false;
        }

        // =========================================================================
        // BALISE LASER, CHARGE, FRAPPE TITANESQUE, BOUSCULADE, LANCER D'ARME
        // =========================================================================
        private bool TryExecuteLaserDesignation(TacticalUnit actor, TacticalUnit target, int dist, TacticalUnitVisual visual)
        {
            if (actor?.Stats == null || target?.Stats == null || !target.Stats.IsAlive) return false;
            if (dist < 1 || dist > 12) return false;
            if (!SmokeScreen.HasItemByName(actor.Stats, "Lampe") || !SmokeScreen.HasItemByName(actor.Stats, "Batterie")) return false;
            if (SkillTechniqueState.IsFlawExposed(target.Stats)) return false;
            if (!HasLineOfSight(actor.CurrentCoords, target.CurrentCoords, target.FootprintType, actor.FootprintType, actor)) return false;
            if (target.Stats.EncaissementThreshold < 3 && target.Stats.CurrentHealth < 8) return false;
            if (actor.Stats.CurrentActionPoints < 1 + 2 + GetRequiredDefensiveReserve(actor, TacticalPosture.CalculatedStrike)) return false;
            if (!actor.Stats.ConsumeActionPoints(1)) return false;

            SkillTechniqueState.ApplyExposedFlaw(actor.Stats, target.Stats);
            visual?.SpawnFloatingText("🔦 Faille désignée (-1 PA)", Color.yellow);
            target.GetComponent<TacticalUnitVisual>()?.SpawnFloatingText("🔦 DÉSIGNÉ", Color.yellow);
            _arena?.Log($"🔦 <b>{actor.Stats.Name}</b> désigne <b>{target.Stats.Name}</b> à la balise (faille exposée).");
            _arena?.RecordChronoSnapshot($"Balise : {actor.Stats.Name} -> {target.Stats.Name}");
            return true;
        }

        private bool TryExecuteChargeAttack(TacticalUnit actor, TacticalUnit target, TacticalUnitVisual visual)
        {
            if (_arena == null || _arena.IsResolving || _pathfinder == null) return false;
            if (actor?.Stats == null || target?.Stats == null || !target.Stats.IsAlive) return false;
            if (_defaultPersonality == AIPersonality.Survivor) return false;
            if (GrappleState.IsGrappled(actor.Stats)) return false;
            if (!_pathfinder.CanChargeTarget(actor, target, 3, 2)) return false;

            visual?.SpawnFloatingText("⚡ Charge d'assaut !", new Color(1f, 0.85f, 0.2f));
            _arena.ExecuteChargeAttack(actor, target, BodyPart.Torse, 0, 0);
            return true;
        }

        private bool TryExecuteTitanSlam(TacticalUnit actor, TacticalUnit target, TacticalPosture posture, int dist, TacticalUnitVisual visual)
        {
            if (_arena != null && _arena.IsResolving) return false;
            if (actor?.Stats == null || !actor.Stats.IsAlive) return false;
            if (target?.Stats == null || !target.Stats.IsAlive) return false;
            if (!TitanFootprint.IsTitan(actor.FootprintType)) return false;
            int pa = actor.Stats.CurrentActionPoints;
            bool allIn = (posture == TacticalPosture.AllInLethal) || _defaultPersonality == AIPersonality.Aggressive;

            // Saisie colossale au contact (2 PA) : broyage + Immobilisé.
            if (dist <= 1 && pa >= 2
                && !actor.Stats.ActiveStatus.HasFlag(StatusEffect.Immobilise)
                && TitanFootprint.CanTitanGrab(actor.FootprintType, target.FootprintType, dist)
                && (target.Stats.CurrentActionPoints >= 3 || target.Stats.CurrentHealth <= Mathf.Max(3, actor.Stats.Attributes.Force) || allIn))
            {
                if (!actor.Stats.ConsumeActionPoints(2)) return false;
                int grabDmg = Mathf.Max(3, actor.Stats.Attributes.Force);
                if (target.Stats.CurrentHealth - grabDmg <= 0) target.Stats.EvaluateFatalBlow(BodyPart.Torse, grabDmg);
                else target.Stats.CurrentHealth -= grabDmg;

                var tgtVis = target.GetComponent<TacticalUnitVisual>();
                tgtVis?.TriggerHitFlash();
                if (target.Stats.IsAlive)
                {
                    GrappleState.SetGrapple(actor.Stats, target.Stats);
                    target.Stats.ApplyStatus(StatusEffect.Destabilise, 1);
                    tgtVis?.SpawnFloatingText($"-{grabDmg} SAISIE [IMMOBILISÉ]", Color.yellow);
                }
                else
                {
                    tgtVis?.TriggerFallingBackDeath();
                    _arena?.AutoTargetNextAlive();
                }
                visual?.SpawnFloatingText("✊ SAISIE TITANESQUE", Color.cyan);
                _arena?.Log($"✊ <b>{actor.Stats.Name}</b> saisit <b>{target.Stats.Name}</b> (-2 PA) : {grabDmg} dégâts de broyage !");
                _arena?.RecordChronoSnapshot($"Saisie Titanesque : {actor.Stats.Name} -> {target.Stats.Name}");
                return true;
            }

            // Piétinement / Souffle : zones multi-cibles (jamais d'alliés dedans).
            if (pa >= 3)
            {
                // Piétinement (cible à ≤2 cases).
                if (dist <= 2)
                {
                    var zone = TitanFootprint.GetStompZone(target.CurrentCoords, actor.FootprintType);
                    if (CountZoneTargets(actor, zone, out int foes, out int allies) && foes >= (allIn ? 1 : 2) && allies == 0)
                    {
                        ExecuteTitanZoneHit(actor, target, zone, Mathf.Max(4, actor.Stats.Attributes.Force + 2), 3, StatusEffect.ATerre, 1, "💥 PIÉTINEMENT", "Piétinement de Zone", visual);
                        return true;
                    }
                }
                // Souffle (cible à ≤8 cases).
                if (dist <= 8)
                {
                    var zone = TitanFootprint.GetBreathZone(target.CurrentCoords, actor.FootprintType);
                    int breathDmg = Mathf.Max(5, (actor.Stats.Attributes.Magie > 0 ? actor.Stats.Attributes.Magie : actor.Stats.Attributes.Constitution) + 2);
                    if (CountZoneTargets(actor, zone, out int foes, out int allies)
                        && (foes >= 2 || (foes >= 1 && (allIn || target.Stats.CurrentHealth <= breathDmg))) && allies == 0)
                    {
                        ExecuteTitanZoneHit(actor, target, zone, breathDmg, 3, StatusEffect.EnFeu, 2, "🔥 SOUFFLE", "Souffle Titanesque", visual);
                        return true;
                    }
                }
            }

            return false;
        }

        private bool CountZoneTargets(TacticalUnit actor, List<HexCoordinates> zone, out int foes, out int allies)
        {
            foes = 0;
            allies = 0;
            if (zone == null || zone.Count == 0) return false;
            var zoneSet = new HashSet<HexCoordinates>(zone);
            for (int i = 0; i < _cachedUnits.Count; i++)
            {
                var u = _cachedUnits[i];
                if (u == null || u == actor || u.Stats == null || !u.Stats.IsAlive) continue;
                var cells = u.OccupiedCoords;
                bool inZone = false;
                for (int c = 0; c < cells.Count; c++)
                {
                    if (zoneSet.Contains(cells[c]))
                    {
                        inZone = true;
                        break;
                    }
                }
                if (!inZone) continue;
                bool hostile = actor.IsPlayerControlled ? !u.IsPlayerControlled : u.IsPlayerControlled;
                if (_mode == CombatAIMode.FullAuto && u.IsPlayerControlled != actor.IsPlayerControlled) hostile = true;
                if (hostile) foes++;
                else allies++;
            }
            return foes > 0;
        }

        private void ExecuteTitanZoneHit(TacticalUnit actor, TacticalUnit target, List<HexCoordinates> zone, int dmg, int apCost, StatusEffect zoneStatus, int statusTurns, string icon, string label, TacticalUnitVisual visual)
        {
            if (!actor.Stats.ConsumeActionPoints(apCost)) return;
            var zoneSet = new HashSet<HexCoordinates>(zone);
            int hitCount = 0;
            for (int i = 0; i < _cachedUnits.Count; i++)
            {
                var u = _cachedUnits[i];
                if (u == null || u == actor || u.Stats == null || !u.Stats.IsAlive) continue;
                bool inZone = false;
                var cells = u.OccupiedCoords;
                for (int c = 0; c < cells.Count; c++)
                {
                    if (zoneSet.Contains(cells[c]))
                    {
                        inZone = true;
                        break;
                    }
                }
                if (!inZone) continue;

                hitCount++;
                if (u.Stats.CurrentHealth - dmg <= 0) u.Stats.EvaluateFatalBlow(BodyPart.Torse, dmg);
                else u.Stats.CurrentHealth -= dmg;
                u.Stats.ApplyStatus(zoneStatus, statusTurns);
                u.Stats.ApplyStatus(StatusEffect.Destabilise, 1);
                var uVis = u.GetComponent<TacticalUnitVisual>();
                uVis?.TriggerHitFlash();
                uVis?.SpawnFloatingText($"-{dmg} {icon}", Color.red);
                if (!u.Stats.IsAlive)
                {
                    uVis?.TriggerFallingBackDeath();
                    if (u == target) _arena?.AutoTargetNextAlive();
                }
            }
            visual?.SpawnFloatingText($"{icon} {label} ({hitCount})", new Color(1f, 0.45f, 0.1f));
            _arena?.Log($"{icon} <b>{actor.Stats.Name}</b> : <b>{label} ({zone.Count} hex)</b> (-{apCost} PA) — {hitCount} unité(s) ({dmg} dégâts) !");
            _arena?.RecordChronoSnapshot($"{label} : {actor.Stats.Name} ({zone.Count} hex)");
        }

        private bool TryExecuteShove(TacticalUnit actor, TacticalUnit target, CoverType targetCover, TacticalUnitVisual visual)
        {
            if (_arena == null || _arena.IsResolving) return false;
            if (actor?.Stats == null || target?.Stats == null || !target.Stats.IsAlive) return false;
            if (actor.Stats.CurrentActionPoints < 2) return false;
            if (GrappleState.IsGrappled(actor.Stats)) return false;
            if ((target.Stats.ActiveStatus & StatusEffect.ATerre) != 0) return false;
            if (TitanFootprint.GetHexCount(target.FootprintType) > TitanFootprint.GetHexCount(actor.FootprintType) + 6) return false;

            bool wantShove = (targetCover == CoverType.ThreeQuarters)
                || _defaultPersonality == AIPersonality.Survivor;
            if (!wantShove && _defaultPersonality == AIPersonality.Tactician
                && target.Stats.BaseArmorAbsorption + target.Stats.GetWornArmorBonus() >= 5)
            {
                wantShove = true;
            }
            if (!wantShove) return false;

            bool shieldBash = AIHasShield(actor);
            SkillType checkSkill = shieldBash ? SkillType.DefenseCorporelle : SkillType.Athletisme;
            if (!actor.Stats.CanAttack(actor.Stats.GetSkillDie(checkSkill, true))) return false;

            visual?.SpawnFloatingText(shieldBash ? "🛡️ Coup de bouclier !" : "💨 Bousculade !", Color.cyan);
            _arena.ExecuteShove(actor, target, shieldBash, 0, 0);
            return true;
        }

        private bool TryExecuteWeaponThrow(TacticalUnit actor, TacticalUnit target, int dist, TacticalUnitVisual visual)
        {
            if (_arena == null || _arena.IsResolving) return false;
            if (actor?.Stats == null || actor.Sheet == null) return false;
            if (dist < 2 || dist > 4 || actor.Stats.CurrentActionPoints < 2) return false;
            var w = actor.Sheet.GetEquippedWeapon();
            if (w == null || w.IsThrowableGrenade() || w.IsLauncher) return false;
            // Le tir direct reste meilleur quand il est disponible.
            if (CanShootNow(actor)) return false;

            visual?.SpawnFloatingText($"🗡️ Lancer : {w.Name} !", Color.cyan);
            _arena.ExecuteWeaponThrow(actor, target);
            return true;
        }

        // =========================================================================
        // RECHERCHE DE COUVERT & DÉBORDEMENT GÉOMÉTRIQUE (FLANKING 3D)
        // =========================================================================
    }
}

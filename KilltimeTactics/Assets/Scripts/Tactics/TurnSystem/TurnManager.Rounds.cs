using System;
using System.Collections.Generic;
using UnityEngine;
using Killtime.Tactics.Units;
using Killtime.Tactics.Grid;
using Killtime.Tactics.Objectives;
using Killtime.Core.Character;
using Killtime.Core.Combat;
using Killtime.Core.Dice;
using Killtime.Core.Rules;

namespace Killtime.Tactics.TurnSystem
{
    public partial class TurnManager : MonoBehaviour
    {
        public void StartNewRound()
        {
            if (IsInExploration) return;
            _allUnits.RemoveAll(u => u == null);

            // Garde EditMode/tests : aucun combattant enregistré (ex : passage en
            // mode combat avant tout RegisterUnit) — rien à ordonner, et sans ce
            // retour CheckCombatOver() renvoie false (liste vide) puis la branche
            // « aucune unité active » récursionne à l'infini (StackOverflow).
            if (_allUnits.Count == 0) return;

            if (CheckCombatOver()) return;

            if (_needsInitiativeRoll || CurrentRound <= 1)
            {
                // Début de combat / reset : jet d'initiative général (Livre I §4.3).
                RollInitiativeAndSort();
            }
            else
            {
                // Rounds suivants : ordre conservé (pas de re-tri, pas de re-jet).
                // Les renforts sans relevé reçoivent un jet individuel (sans reshuffle).
                for (int i = 0; i < _allUnits.Count; i++)
                {
                    EnsureInitiativeFor(_allUnits[i]);
                }
            }

            _activeUnitIndex = 0;

            // Sauter immédiatement les unités mortes au début du round
            while (_activeUnitIndex < _allUnits.Count
                && (_allUnits[_activeUnitIndex] == null
                    || _allUnits[_activeUnitIndex].Stats == null
                    || !_allUnits[_activeUnitIndex].Stats.IsAlive))
            {
                _activeUnitIndex++;
            }

            if (_activeUnitIndex >= _allUnits.Count)
            {
                if (CheckCombatOver()) return;

                CurrentRound++;
                StartNewRound();
                return;
            }

            OnRoundStarted?.Invoke(CurrentRound);
            BroadcastRoundStartIfGM();
            StartUnitTurn();
        }

        public void EndCurrentTurn()
        {
            if (IsInExploration) return;
            if (CheckCombatOver()) return;

            if (ActiveUnit != null && ActiveUnit.Stats != null)
            {
                var expired = ActiveUnit.Stats.TickTurnStatusDurations();
                if (expired.Count > 0)
                {
                    var vis = ActiveUnit.GetComponent<TacticalUnitVisual>();
                    for (int i = 0; i < expired.Count; i++)
                    {
                        vis?.SpawnFloatingText($"[{expired[i]}] Dissipé", new Color(0.4f, 0.9f, 1.0f));
                    }
                    OnUnitStatusExpired?.Invoke(ActiveUnit, expired);
                }
            }

            if (IsMultiplayerGM() && ActiveUnit != null)
            {
                var payload = new Killtime.Multi.VTTTurnControlPayload
                {
                    action = "end_turn",
                    round = CurrentRound,
                    activeUnitId = ActiveUnit.gameObject.name
                };
                Killtime.Multi.VTTTableSync.Instance?.BroadcastTurnControl(payload);
            }

            _activeUnitIndex++;

            // Trouver la prochaine unité en vie
            while (_activeUnitIndex < _allUnits.Count
                && (_allUnits[_activeUnitIndex] == null
                    || _allUnits[_activeUnitIndex].Stats == null
                    || !_allUnits[_activeUnitIndex].Stats.IsAlive))
            {
                _activeUnitIndex++;
            }

            if (_activeUnitIndex >= _allUnits.Count)
            {
                if (CheckCombatOver()) return;

                CurrentRound++;
                StartNewRound();
            }
            else
            {
                StartUnitTurn();
            }
        }

        private void StartUnitTurn()
        {
            if (_activeUnitIndex < 0 || _activeUnitIndex >= _allUnits.Count) return;

            ActiveUnit = _allUnits[_activeUnitIndex];

            // Sécurité absolue : si l'unité est morte ou invalide, passer immédiatement le tour
            if (ActiveUnit == null || ActiveUnit.Stats == null || !ActiveUnit.Stats.IsAlive)
            {
                EndCurrentTurn();
                return;
            }

            RefreshAndNotifyActiveUnitTurnStart(ActiveUnit);

            if (ActiveUnit == null || ActiveUnit.Stats == null || !ActiveUnit.Stats.IsAlive)
            {
                EndCurrentTurn();
                return;
            }

            NotifyAudioBattleState();

            OnTurnStarted?.Invoke(ActiveUnit);

            if (IsMultiplayerGM() && ActiveUnit != null)
            {
                var arena = FindAnyObjectByType<CombatDevArena>();
                var curTarget = arena != null ? arena.CurrentTarget : null;

                var payload = new Killtime.Multi.VTTTurnControlPayload
                {
                    action = "turn_start",
                    round = CurrentRound,
                    activeUnitId = ActiveUnit.gameObject.name,
                    targetUnitId = curTarget != null ? curTarget.gameObject.name : "",
                    targetQ = curTarget != null ? curTarget.CurrentCoords.Q : 0,
                    targetR = curTarget != null ? curTarget.CurrentCoords.R : 0
                };
                Killtime.Multi.VTTTableSync.Instance?.BroadcastTurnControl(payload);
            }
        }

        /// <summary>
        /// Refresh individuel (Livres I §4 + VI §20) : PA remis au max AU DÉBUT
        /// DU TOUR PERSONNEL de l'unité active. Aucun refresh global au début
        /// du round : StartNewRound ne recharge personne, seul StartUnitTurn
        /// recharge l'unité dont l'initiative arrive. L'Essoufflement (Souffle)
        /// n'est PAS effacé ici : il se récupère via l'action Reprendre son
        /// Souffle (1 PA = 1 PE) jouée en début de son propre tour.
        /// </summary>
        private void RefreshAndNotifyActiveUnitTurnStart(TacticalUnit unit)
        {
            if (unit == null || unit.Stats == null || !unit.Stats.IsAlive) return;

            int prevAP = unit.Stats.CurrentActionPoints;
            unit.Stats.ResetTurn();

            if (unit.FootprintType == TitanFootprintType.Rosette7 || unit.FootprintType == TitanFootprintType.Colossus19)
            {
                unit.Stats.CurrentActionPoints = unit.Stats.MaxActionPoints * 2;
            }

            var visual = unit.GetComponent<TacticalUnitVisual>();

            // RD-045 : tick DoT résiduel en DÉBUT de tour personnel (après ResetTurn
            // pour que le drain PA d'Asphyxie s'applique sur la réserve rechargée).
            // Ignore l'armure ; EnFeu via bouclier ; dégressif -1/tour ; soin stoppe.
            DotTickResult dotTick = unit.Stats.TickTurnStatus();
            if (dotTick != null && dotTick.HasTick)
            {
                foreach (var kv in dotTick.DamageByStatus)
                {
                    if (kv.Value > 0)
                        visual?.SpawnFloatingText($"-{kv.Value} PV [{kv.Key}]", new Color(1f, 0.25f, 0.2f));
                }
                if (dotTick.PaDrained > 0)
                    visual?.SpawnFloatingText($"🫁 Asphyxie -{dotTick.PaDrained} PA", new Color(0.2f, 0.95f, 0.75f));
                for (int i = 0; i < dotTick.ClearedStatuses.Count; i++)
                    visual?.SpawnFloatingText($"[{dotTick.ClearedStatuses[i]}] dissipé", new Color(0.4f, 0.9f, 1.0f));

                if (dotTick.FatalResolution == Killtime.Core.Combat.FatalBlowResolution.EligibleForLastBreath)
                {
                    if (!unit.IsPlayerControlled)
                    {
                        unit.Stats.ChooseSombrer();
                        visual?.SpawnFloatingText("[SYNCOPE] CHUTE HORS COMBAT (0 PV)", Color.cyan);
                        visual?.TriggerFallingBackDeath();
                    }
                    else
                    {
                        unit.Stats.ChooseLastBreath();
                        visual?.SpawnFloatingText("[SURVIE] DERNIER SOUFFLE (0 PV)", new Color(1.0f, 0.5f, 0.1f));
                    }
                }

                OnUnitDotTick?.Invoke(unit, dotTick);
            }

            if (!unit.Stats.IsAlive)
            {
                visual?.TriggerFallingBackDeath();
                Killtime.Core.Combat.GrappleState.ReleaseGrapple(unit.Stats);
                Killtime.Core.Combat.PossessionState.ReleasePossession(unit.Stats);
                return;
            }

            // RD-087 (Livre XII §50) : Régénération lithosphérique du Disciple
            // de Terre (montant bossAI regenAmount, défaut 3 PV) au début de
            // son tour personnel s'il est au contact de la pierre ou d'un
            // couvert. Pur + testé via TitanBossMechanics (contact = voisin
            // non praticable/couvert). Texte sans montant : le montant réel
            // vient du JSON (bossAI), pas d'une constante d'affichage.
            TitanBossState.CurrentRound = CurrentRound;
            if (TitanBossMechanics.TickLithosphericRegen(unit.Stats,
                TitanBossMechanics.IsInStoneContact(unit.OccupiedCoords, probe =>
                {
                    var node = _grid != null ? _grid.GetNode(probe) : null;
                    if (node == null) return (false, false, false);
                    return (true, node.IsWalkable, node.Cover != CoverType.None);
                }), out _))
            {
                visual?.SpawnFloatingText("🪨 RÉGÉN. LITHOSPHÈRE", new Color(0.6f, 0.85f, 0.5f));
            }

            // Techniques de spécialisation (Livre III) : vieillissement des marques
            // (failles exposées, provocations) et expiration des bonus de ligne
            // « Tenir la Ligne ! » du donneur qui rejoue son tour.
            Killtime.Core.Combat.SkillTechniqueState.OnUnitTurnStart(unit.Stats);

            // RD-030 Guet / Overwatch : le guet du guetteur qui rejoue expire
            // (utilisé ou non) ; les guets invalidés (étourdi/paralysé) sont purgés.
            Killtime.Core.Combat.OverwatchState.OnUnitTurnStart(unit.Stats);

            // RD-031 Opportunités : la réaction se recharge au début du tour
            // personnel (1/round) ; le décrochage armé non consommé expire.
            Killtime.Core.Combat.OpportunityState.OnUnitTurnStart(unit.Stats);

            // RD-048 Retarder / Tenir : l'attente non consommée expire quand
            // l'unité rejoue (tour normal) ; le retard se recharge (1/round).
            Killtime.Core.Combat.DelayedTurnState.OnUnitTurnStart(unit.Stats);

            // RD-048 Canalisation : purge uniquement les canalisations
            // invalidées (mort/neutralisé) ; une canalisation valide survit
            // au nouveau tour (effort étalé multi-tours).
            Killtime.Core.Combat.ChannelingState.OnUnitTurnStart(unit.Stats);

            // RD-202 Préparation one-shot : expire si non consommée avant le
            // tour personnel suivant (pas d'effort étalé) ; Dégagement : la
            // fenêtre de fuite immédiate expire elle aussi au tour suivant.
            Killtime.Core.Combat.PreparationState.OnUnitTurnStart(unit.Stats);
            Killtime.Core.Combat.GetAwayState.OnUnitTurnStart(unit.Stats);

            // RD-039 Lutte / Grapple : vérification et maintien des prises au contact.
            Killtime.Core.Combat.GrappleState.OnUnitTurnStart(unit.Stats);

            // RD-204 Possession : la victime joue UN tour sous contrôle puis le lien expire.
            Killtime.Core.Combat.PossessionState.OnUnitTurnStart(unit.Stats);

            // RD-038 Charge & Sprint : expiration du malus défensif (-1 défense) et levée du blocage tir.
            Killtime.Core.Combat.ChargeState.OnUnitTurnStart(unit.Stats);

            // RD-084 Poursuite : purge les concours caducs (proie/poursuivants morts) ;
            // les faillites cumulées survivent jusqu'aux 4 = capture.
            Killtime.Core.Combat.PursuitState.OnUnitTurnStart(unit.Stats);

            int newAP = unit.Stats.CurrentActionPoints;
            int deltaAP = newAP - prevAP;

            if (visual != null)
            {
                if (deltaAP > 0)
                {
                    visual.SpawnFloatingText($"⚡ +{deltaAP} PA ({newAP}/{unit.Stats.MaxActionPoints})", new Color(0.2f, 0.95f, 1.0f));
                }
                else
                {
                    visual.SpawnFloatingText($"⚡ PA COMPLETS ({newAP})", new Color(0.2f, 0.85f, 1.0f, 0.75f));
                }

                if (unit.Stats.Essoufflement > 0)
                {
                    visual.SpawnFloatingText($"🫁 SOUFFLE : {unit.Stats.Essoufflement} PE", new Color(1.0f, 0.72f, 0.15f));
                }
            }
        }
    }
}
